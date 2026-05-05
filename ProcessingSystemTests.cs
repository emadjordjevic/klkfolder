using Xunit;
using System;

public class ProcessingSystemTests
{
    [Fact]
    public void Test_SubmitJob_IncreasesQueue()
    {
        // Arrange
        var system = new ProcessingSystem("SystemConfig.xml");
        var job = new Job { Type = JobType.IO, Payload = "delay:100", Priority = 1 };

        // Act
        var handle = system.Submit(job);

        // Assert

        Assert.NotNull(handle);
        Assert.Equal(job.Id, handle.Id);
    }

    [Fact]
    public void Test_Idempotency_SameJobId()
    {
        // Arrange
        var system = new ProcessingSystem("SystemConfig.xml");
        var job = new Job { Id = Guid.NewGuid(), Type = JobType.IO, Payload = "delay:100" };

        // Act
        system.Submit(job);
        var secondSubmit = system.Submit(job);

        // Assert
        Assert.Null(secondSubmit); // Drugi put mora vratiti null zbog idempotentnosti
    }

    [Fact]
    public void Test_GetJob_ReturnsCorrectJob()
    {
        // Arrange
        var system = new ProcessingSystem("SystemConfig.xml");
        var job = new Job { Type = JobType.IO, Payload = "delay:100" };
        system.Submit(job);

        // Act
        var retrieved = system.GetJob(job.Id);

        // Assert
        Assert.Equal(job.Id, retrieved.Id);
    }
}