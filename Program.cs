using System;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        try
        {
            var system = new ProcessingSystem("SystemConfig.xml");
            Console.WriteLine("Sistem pokrenut. Pritisni Enter za kraj.");

            _ = Task.Run(async () => {
                while (true)
                {
                    var j = new Job { Type = JobType.IO, Payload = "delay:100", Priority = 1 };
                    system.Submit(j);
                    await Task.Delay(2000);
                }
            });

            Console.ReadLine();
        }
        catch (Exception ex) { Console.WriteLine("Greska: " + ex.Message); }
    }
}