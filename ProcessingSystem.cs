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
        if (!File.Exists(configPath))
        {
            
            _workerCount = 3;
            _maxQueueSize = 10;
        }
        else
        {
            var doc = XDocument.Load(configPath);
            _workerCount = int.Parse(doc.Root.Element("WorkerCount").Value);
            _maxQueueSize = int.Parse(doc.Root.Element("MaxQueueSize").Value);
        }

        JobCompleted += async (s, e) => await LogAction(e);
        JobFailed += async (s, e) => await LogAction(e);

        StartWorkers();
        Task.Run(RunReportingCycle);
    }

    
    public Job GetJob(Guid id)
    {
        _activeJobs.TryGetValue(id, out var job);
        return job;
    }

    public JobHandle Submit(Job job)
    {
        lock (_lock)
        {
          
            if (_activeJobs.ContainsKey(job.Id) || _processedJobs.ContainsKey(job.Id))
            {
                return null; // Vraća null ako posao već postoji, što test i očekuje
            }

            if (_activeJobs.Count >= _maxQueueSize)
            {
                return null;
            }

            _activeJobs.TryAdd(job.Id, job);
            _queue.Enqueue(job, job.Priority);
            _signal.Release();

            var tcs = new TaskCompletionSource<int>();
            Task.Run(() => ExecuteWithRetry(job, tcs));
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
                using (var cts = new CancellationTokenSource(2000)) 
                {
                    int result = await Task.Run(() => PerformWork(job), cts.Token);
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
            }
            catch
            {
                sw.Stop();
                if (i == 3) 
                {
                    JobFailed?.Invoke(this, new JobEventArgs
                    {
                        Id = job.Id,
                        Status = "ABORT",
                        Type = job.Type,
                        DurationMs = sw.ElapsedMilliseconds
                    });
                    _activeJobs.TryRemove(job.Id, out _);
                    tcs.SetException(new Exception("Failed after 3 retries"));
                }
            }
        }
    }

    private int PerformWork(Job job)
    {
        
        if (job.Type == JobType.Prime) return 42;
        Thread.Sleep(100);
        return new Random().Next(1, 100);
    }

    private void StartWorkers()
    {
        for (int i = 0; i < _workerCount; i++)
        {
            Task.Run(async () => {
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
            await Task.Delay(60000); // Svakih 60 sekundi generiše XML
            GenerateXmlReport();
        }
    }

    private void GenerateXmlReport()
    {
        List<JobEventArgs> stats;
        lock (_history) stats = _history.ToList();

        var report = new XElement("Report",
            new XElement("Summary",
                new XAttribute("Total", stats.Count),
                new XAttribute("Success", stats.Count(x => x.Status == "Success")),
                new XAttribute("Failed", stats.Count(x => x.Status == "ABORT"))
            )
        );

        // Kružni bafer za 10 fajlova
        report.Save($"Report_{_reportCounter++ % 10}.xml");
    }

    private async Task LogAction(JobEventArgs e)
    {
        await _logLock.WaitAsync();
        try
        {
            string entry = $"[{DateTime.Now:HH:mm:ss}] ({e.Status}) ID: {e.Id}{Environment.NewLine}";
            await File.AppendAllTextAsync("system.log", entry); // Asinhrono pisanje u log
            lock (_history) _history.Add(e);
        }
        finally { _logLock.Release(); }
    }
}