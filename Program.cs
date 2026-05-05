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
            var system = new ProcessingSystem("SystemConfig.xml");

            // Inicijalno učitavanje iz Poslovi.xml
            if (File.Exists("Poslovi.xml"))
            {
                var doc = XDocument.Load("Poslovi.xml");
                foreach (var jElem in doc.Root.Elements("Job"))
                {
                    system.Submit(new Job
                    {
                        Type = Enum.Parse<JobType>(jElem.Element("Type").Value),
                        Priority = int.Parse(jElem.Element("Priority").Value),
                        Payload = jElem.Element("Payload").Value
                    });
                }
            }

            // Simulacija nasumičnog dodavanja iz više niti
            Random rnd = new Random();
            for (int i = 0; i < 3; i++)
            {
                int nitId = i;
                _ = Task.Run(async () => {
                    while (true)
                    {
                        bool isIo = rnd.Next(0, 2) == 0;
                        var j = new Job
                        {
                            Type = isIo ? JobType.IO : JobType.Prime,
                            Priority = rnd.Next(1, 11),
                            Payload = isIo ? "delay:300" : "limit:5000,threads:4"
                        };
                        var h = system.Submit(j);
                        if (h == null) Console.WriteLine($"[Nit {nitId}] Red pun.");
                        await Task.Delay(rnd.Next(1000, 3000));
                    }
                });
            }

            Console.WriteLine("Sistem pokrenut. Pritisni Enter za izlaz.");
            Console.ReadLine();
        }
        catch (Exception ex) { Console.WriteLine("Greška: " + ex.Message); }
    }
}