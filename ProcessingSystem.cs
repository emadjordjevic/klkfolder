using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

public class ProcessingSystem
{
    private readonly int _workerCount;
    private readonly int _maxQueueSize;
    private readonly PriorityQueue<Job, int> _queue = new PriorityQueue<Job, int>();
    private readonly ConcurrentDictionary<Guid, byte> _processedJobs = new ConcurrentDictionary<Guid, byte>();
    private readonly ConcurrentDictionary<Guid, Job> _activeJobs = new ConcurrentDictionary<Guid, Job>();
    private readonly List<JobEventArgs> _history = new List<JobEventArgs>();
    private readonly object _lock = new object();
    private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
    private readonly SemaphoreSlim _logLock = new SemaphoreSlim(1, 1);
    private int _reportCounter = 0;

    public event EventHandler<JobEventArgs> JobCompleted;
    public event EventHandler<JobEventArgs> JobFailed;

    public ProcessingSystem(string configPath)
    {
        if (File.Exists(configPath))
        {
            var doc = XDocument.Load(configPath);
            _workerCount = int.Parse(doc.Root.Element("WorkerCount").Value);
            _maxQueueSize = int.Parse(doc.Root.Element("MaxQueueSize").Value);
        }
        else
        {
            _workerCount = 3;
            _maxQueueSize = 10;
        }

        JobCompleted += async (s, e) => await LogAction(e);
        JobFailed += async (s, e) => await LogAction(e);

        StartWorkers();
        _ = Task.Run(RunReportingCycle);
    }

    public JobHandle Submit(Job job)
    {
        lock (_lock)
        {
            if (_activeJobs.ContainsKey(job.Id) || _processedJobs.ContainsKey(job.Id)) return null;
            if (_activeJobs.Count >= _maxQueueSize) return null;

            _activeJobs.TryAdd(job.Id, job);
            _queue.Enqueue(job, -job.Priority); // Veći broj = veći prioritet
            _signal.Release();

            var tcs = new TaskCompletionSource<int>();
            _ = Task.Run(() => ExecuteWithRetry(job, tcs));
            return new JobHandle(job.Id, tcs.Task);
        }
    }

    private async Task ExecuteWithRetry(Job job, TaskCompletionSource<int> tcs)
    {
        for (int i = 1; i <= 3; i++)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var workTask = Task.Run(() => PerformWork(job));
                // Timeout na 2 sekunde
                if (await Task.WhenAny(workTask, Task.Delay(2000)) == workTask)
                {
                    int result = await workTask;
                    sw.Stop();
                    _processedJobs.TryAdd(job.Id, 0);
                    _activeJobs.TryRemove(job.Id, out _);

                    JobCompleted?.Invoke(this, new JobEventArgs
                    {
                        Id = job.Id,
                        Result = result,
                        Status = "Success",
                        Type = job.Type,
                        DurationMs = sw.ElapsedMilliseconds
                    });
                    tcs.SetResult(result);
                    return;
                }
                throw new TimeoutException();
            }
            catch
            {
                sw.Stop();
                if (i == 3) // Posle trećeg fail-a
                {
                    JobFailed?.Invoke(this, new JobEventArgs
                    {
                        Id = job.Id,
                        Status = "ABORT",
                        Type = job.Type,
                        DurationMs = sw.ElapsedMilliseconds,
                        Result = -1
                    });
                    _activeJobs.TryRemove(job.Id, out _);
                    tcs.SetException(new Exception("ABORT"));
                }
            }
        }
    }

    private int PerformWork(Job job)
    {
        if (job.Type == JobType.IO)
        {
            // Parsiranje: "delay:500"
            int delay = int.Parse(job.Payload.Split(':')[1]);
            Thread.Sleep(delay);
            return new Random().Next(0, 101);
        }
        else // Prime
        {
            // Parsiranje: "limit:1000,threads:4"
            var parts = job.Payload.Split(',');
            int limit = int.Parse(parts[0].Split(':')[1]);
            int threads = Math.Clamp(int.Parse(parts[1].Split(':')[1]), 1, 8);

            int count = 0;
            Parallel.For(2, limit + 1, new ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
            {
                bool isPrime = true;
                for (int j = 2; j * j <= i; j++) if (i % j == 0) { isPrime = false; break; }
                if (isPrime) Interlocked.Increment(ref count);
            });
            return count;
        }
    }

    public IEnumerable<Job> GetTopJobs(int n)
    {
        lock (_lock)
        {
            return _queue.UnorderedItems
                .Select(x => x.Element)
                .OrderByDescending(j => j.Priority)
                .Take(n)
                .ToList();
        }
    }

    public Job GetJob(Guid id)
    {
        _activeJobs.TryGetValue(id, out var job);
        return job;
    }

    private void StartWorkers()
    {
        for (int i = 0; i < _workerCount; i++)
        {
            _ = Task.Run(async () => {
                while (true)
                {
                    await _signal.WaitAsync();
                    lock (_lock) { if (_queue.Count > 0) _queue.Dequeue(); }
                }
            });
        }
    }

    private async Task RunReportingCycle()
    {
        while (true)
        {
            await Task.Delay(60000); // 1 minut
            GenerateXmlReport();
        }
    }

    private void GenerateXmlReport()
    {
        lock (_history)
        {
            var report = new XElement("Report",
                new XElement("Stats",
                    _history.GroupBy(x => x.Type).Select(g => new XElement("JobGroup",
                        new XAttribute("Type", g.Key),
                        new XAttribute("Executed", g.Count(x => x.Status == "Success")),
                        new XAttribute("AvgTime", g.Where(x => x.Status == "Success").Any() ? g.Average(x => x.DurationMs) : 0),
                        new XAttribute("Failed", g.Count(x => x.Status == "ABORT"))
                    ))
                )
            );
            report.Save($"Report_{_reportCounter++ % 10}.xml");
        }
    }

    private async Task LogAction(JobEventArgs e)
    {
        await _logLock.WaitAsync();
        try
        {
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{e.Status}] {e.Id}, {e.Result}{Environment.NewLine}";
            await File.AppendAllTextAsync("system.log", entry);
            lock (_history) _history.Add(e);
        }
        finally { _logLock.Release(); }
    }
}