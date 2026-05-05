using System;
using System.IO;
using System.Xml.Linq;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        try
        {
            // Inicijalizacija sistema (čita konfiguraciju)
            var system = new ProcessingSystem("SystemConfig.xml");
            Console.WriteLine("Sistem konfigurisan.");

            // --- DEO KOJI UCITAVA POSLOVE IZ POSEBNOG XML-A ---
            string jobsPath = "Poslovi.xml";
            if (File.Exists(jobsPath))
            {
                Console.WriteLine("Učitavam inicijalne poslove iz Poslovi.xml...");
                var doc = XDocument.Load(jobsPath);
                foreach (var jElem in doc.Root.Elements("Job"))
                {
                    var job = new Job
                    {
                        Id = Guid.NewGuid(),
                        Type = Enum.Parse<JobType>(jElem.Element("Type").Value),
                        Priority = int.Parse(jElem.Element("Priority").Value),
                        Payload = jElem.Element("Payload").Value
                    };
                    system.Submit(job); // Ovo ubacuje posao u sistem
                }
            }

            // --- DEO KOJI POKREĆE NITI ZA NASUMIČNO DODAVANJE ---
            Random rnd = new Random();
            for (int i = 0; i < 3; i++) // 3 niti koje stalno dodaju
            {
                int nitId = i;
                _ = Task.Run(async () => {
                    while (true)
                    {
                        var j = new Job { Type = JobType.IO, Payload = "Novi", Priority = rnd.Next(1, 5) };
                        var res = system.Submit(j);
                        if (res == null) Console.WriteLine($"[Nit {nitId}] Red pun!");
                        await Task.Delay(rnd.Next(1000, 3000));
                    }
                });
            }

            Console.WriteLine("Sve radi. Pritisni Enter za kraj.");
            Console.ReadLine();
        }
        catch (Exception ex) { Console.WriteLine("Greska: " + ex.Message); }
    }
}