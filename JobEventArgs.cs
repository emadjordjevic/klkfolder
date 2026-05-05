using System;

public class JobEventArgs : EventArgs
{
    public Guid Id { get; set; }
    public int? Result { get; set; }
    public string Status { get; set; } = "";
    public JobType Type { get; set; }
    public long DurationMs { get; set; }
}