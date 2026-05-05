using System;

public class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public JobType Type { get; set; }
    public string Payload { get; set; } = "";
    public int Priority { get; set; }
}