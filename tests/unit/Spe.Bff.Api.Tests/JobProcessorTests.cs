using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spe.Bff.Api.Services.BackgroundServices;
using Spe.Bff.Api.Services.Jobs;
using System.Text.Json;
using Xunit;
using FluentAssertions;

namespace Spe.Bff.Api.Tests;

public class JobContractTests
{
    [Fact]
    public void JobContract_HasRequiredProperties()
    {
        var job = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = "TestJob",
            SubjectId = "user123",
            CorrelationId = "corr123",
            IdempotencyKey = "idem123",
            Attempt = 1,
            MaxAttempts = 3,
            Payload = JsonDocument.Parse("{\"test\": \"data\"}")
        };

        job.JobId.Should().NotBeEmpty();
        job.JobType.Should().Be("TestJob");
        job.SubjectId.Should().Be("user123");
        job.CorrelationId.Should().Be("corr123");
        job.IdempotencyKey.Should().Be("idem123");
        job.Attempt.Should().Be(1);
        job.MaxAttempts.Should().Be(3);
        job.Payload.Should().NotBeNull();
    }

    [Fact]
    public void JobContract_CanBeSerializedAndDeserialized()
    {
        var originalJob = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = "TestJob",
            SubjectId = "user123",
            CorrelationId = "corr123",
            IdempotencyKey = "idem123",
            Attempt = 1,
            MaxAttempts = 3,
            Payload = JsonDocument.Parse("{\"test\": \"data\"}")
        };

        var json = JsonSerializer.Serialize(originalJob);
        var deserializedJob = JsonSerializer.Deserialize<JobContract>(json);

        deserializedJob.Should().NotBeNull();
        deserializedJob!.JobId.Should().Be(originalJob.JobId);
        deserializedJob.JobType.Should().Be(originalJob.JobType);
        deserializedJob.SubjectId.Should().Be(originalJob.SubjectId);
        deserializedJob.CorrelationId.Should().Be(originalJob.CorrelationId);
        deserializedJob.IdempotencyKey.Should().Be(originalJob.IdempotencyKey);
        deserializedJob.Attempt.Should().Be(originalJob.Attempt);
        deserializedJob.MaxAttempts.Should().Be(originalJob.MaxAttempts);
    }

    [Fact]
    public void JobContract_IsAtMaxAttempts_ReturnsCorrectValue()
    {
        var job = new JobContract
        {
            Attempt = 3,
            MaxAttempts = 3
        };

        job.IsAtMaxAttempts.Should().BeTrue();

        job.Attempt = 2;
        job.IsAtMaxAttempts.Should().BeFalse();
    }
}

public class JobOutcomeTests
{
    [Fact]
    public void JobOutcome_Success_HasCorrectProperties()
    {
        var jobId = Guid.NewGuid();
        var outcome = JobOutcome.Success(jobId, "TestJob", TimeSpan.FromSeconds(1.5));

        outcome.JobId.Should().Be(jobId);
        outcome.JobType.Should().Be("TestJob");
        outcome.Status.Should().Be(JobStatus.Completed);
        outcome.Duration.Should().Be(TimeSpan.FromSeconds(1.5));
        outcome.ErrorMessage.Should().BeNull();
        outcome.Attempt.Should().Be(0);
    }

    [Fact]
    public void JobOutcome_Failure_HasCorrectProperties()
    {
        var jobId = Guid.NewGuid();
        var outcome = JobOutcome.Failure(jobId, "TestJob", "Test error", 2, TimeSpan.FromSeconds(0.5));

        outcome.JobId.Should().Be(jobId);
        outcome.JobType.Should().Be("TestJob");
        outcome.Status.Should().Be(JobStatus.Failed);
        outcome.Duration.Should().Be(TimeSpan.FromSeconds(0.5));
        outcome.ErrorMessage.Should().Be("Test error");
        outcome.Attempt.Should().Be(2);
    }

    [Fact]
    public void JobOutcome_Poisoned_HasCorrectProperties()
    {
        var jobId = Guid.NewGuid();
        var outcome = JobOutcome.Poisoned(jobId, "TestJob", "Max attempts exceeded", 3, TimeSpan.FromSeconds(2));

        outcome.JobId.Should().Be(jobId);
        outcome.JobType.Should().Be("TestJob");
        outcome.Status.Should().Be(JobStatus.Poisoned);
        outcome.Duration.Should().Be(TimeSpan.FromSeconds(2));
        outcome.ErrorMessage.Should().Be("Max attempts exceeded");
        outcome.Attempt.Should().Be(3);
    }
}

public class JobHandlerTests
{
    [Fact]
    public async Task TestJobHandler_ProcessAsync_CompletesSuccessfully()
    {
        var logger = new TestLogger<TestJobHandler>();
        var handler = new TestJobHandler(logger);

        var job = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = "TestJob",
            SubjectId = "user123",
            CorrelationId = "corr123",
            IdempotencyKey = "idem123",
            Attempt = 1,
            MaxAttempts = 3,
            Payload = JsonDocument.Parse("{\"message\": \"test\"}")
        };

        var result = await handler.ProcessAsync(job, CancellationToken.None);

        result.Should().NotBeNull();
        result.Status.Should().Be(JobStatus.Completed);
        result.JobId.Should().Be(job.JobId);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task TestJobHandler_ProcessAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        var logger = new TestLogger<TestJobHandler>();
        var handler = new TestJobHandler(logger);

        var job = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = "TestJob",
            SubjectId = "user123",
            CorrelationId = "corr123",
            IdempotencyKey = "idem123",
            Attempt = 1,
            MaxAttempts = 3,
            Payload = JsonDocument.Parse("{\"message\": \"test\"}")
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await handler.ProcessAsync(job, cts.Token);
        });
    }
}

public class JobProcessorIntegrationTests
{
    [Fact]
    public async Task JobProcessor_ProcessesJobsInQueue()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddScoped<IJobHandler, TestJobHandler>();

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<JobProcessor>>();

        var processor = new JobProcessor(logger, serviceProvider);

        var job = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = "TestJob",
            SubjectId = "user123",
            CorrelationId = "corr123",
            IdempotencyKey = "idem123",
            Attempt = 1,
            MaxAttempts = 3,
            Payload = JsonDocument.Parse("{\"message\": \"test\"}")
        };

        // Enqueue job
        processor.EnqueueJob(job);
        processor.QueueDepth.Should().Be(1);

        // Start processing for a short time
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var processingTask = processor.StartAsync(cts.Token);

        // Wait a bit for processing
        await Task.Delay(500);

        // Stop processing
        await processor.StopAsync(CancellationToken.None);

        // Queue should be empty and job should be processed
        processor.QueueDepth.Should().Be(0);
        processor.ProcessedJobsCount.Should().Be(1);
    }

    [Fact]
    public async Task JobProcessor_HandlesIdempotency()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddScoped<IJobHandler, TestJobHandler>();

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<JobProcessor>>();

        var processor = new JobProcessor(logger, serviceProvider);

        var job1 = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = "TestJob",
            SubjectId = "user123",
            CorrelationId = "corr123",
            IdempotencyKey = "same-key",
            Attempt = 1,
            MaxAttempts = 3,
            Payload = JsonDocument.Parse("{\"message\": \"test1\"}")
        };

        var job2 = new JobContract
        {
            JobId = Guid.NewGuid(),
            JobType = "TestJob",
            SubjectId = "user123",
            CorrelationId = "corr456",
            IdempotencyKey = "same-key", // Same idempotency key
            Attempt = 1,
            MaxAttempts = 3,
            Payload = JsonDocument.Parse("{\"message\": \"test2\"}")
        };

        // Enqueue both jobs
        processor.EnqueueJob(job1);
        processor.EnqueueJob(job2);
        processor.QueueDepth.Should().Be(2);

        // Start processing for a short time
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var processingTask = processor.StartAsync(cts.Token);

        // Wait a bit for processing
        await Task.Delay(500);

        // Stop processing
        await processor.StopAsync(CancellationToken.None);

        // Both jobs should be dequeued but only one should be processed due to idempotency
        processor.QueueDepth.Should().Be(0);
        processor.ProcessedJobsCount.Should().Be(1);
    }
}

// Test implementations
public class TestJobHandler : IJobHandler
{
    private readonly ILogger<TestJobHandler> _logger;

    public TestJobHandler(ILogger<TestJobHandler> logger)
    {
        _logger = logger;
    }

    public string JobType => "TestJob";

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        _logger.LogInformation("Processing test job {JobId}", job.JobId);

        // Simulate some work
        await Task.Delay(10, ct);

        return JobOutcome.Success(job.JobId, job.JobType, TimeSpan.FromMilliseconds(10));
    }
}

public class TestLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}