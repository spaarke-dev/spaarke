# Background Workers Pattern

> **Domain**: BFF API / Async Processing
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-001, ADR-004, ADR-017

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Services/Jobs/JobContract.cs` | Job envelope schema |
| `src/server/api/Sprk.Bff.Api/Services/Jobs/JobSubmissionService.cs` | Submit jobs to Service Bus |
| `src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs` | BackgroundService processor |
| `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/` | Job handler implementations |

---

## Job Contract Schema

```csharp
public record JobContract
{
    public Guid JobId { get; init; }
    public string JobType { get; init; } = "";
    public string SubjectId { get; init; } = "";
    public string CorrelationId { get; init; } = "";
    public string IdempotencyKey { get; init; } = "";
    public int Attempt { get; init; } = 1;
    public int MaxAttempts { get; init; } = 3;
    public JsonElement Payload { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
```

---

## Job Handler Interface

```csharp
public interface IJobHandler
{
    string JobType { get; }
    Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct);
}

public record JobOutcome(
    bool IsSuccess,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static JobOutcome Success() => new(true);
    public static JobOutcome Failure(string code, string message) => new(false, code, message);
}
```

---

## Job Handler Implementation

```csharp
public class DocumentProcessingJobHandler : IJobHandler
{
    public string JobType => "document-processing";

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        // 1. Check idempotency (already processed?)
        if (await _cache.ExistsAsync(job.IdempotencyKey))
            return JobOutcome.Success();  // Safe replay

        try
        {
            // 2. Deserialize payload
            var payload = JsonSerializer.Deserialize<DocumentPayload>(job.Payload);

            // 3. Process
            await ProcessDocumentAsync(payload, ct);

            // 4. Mark as processed
            await _cache.SetAsync(job.IdempotencyKey, true, TimeSpan.FromDays(7));

            return JobOutcome.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed", job.JobId);
            return JobOutcome.Failure("processing_error", ex.Message);
        }
    }
}
```

---

## Job Submission

```csharp
public class JobSubmissionService
{
    public async Task SubmitJobAsync(JobContract job, CancellationToken ct = default)
    {
        var sender = _serviceBusClient.CreateSender(_queueName);

        var message = new ServiceBusMessage(JsonSerializer.Serialize(job))
        {
            MessageId = job.JobId.ToString(),
            CorrelationId = job.IdempotencyKey,
            ContentType = "application/json",
            Subject = job.JobType,
            ApplicationProperties =
            {
                ["JobType"] = job.JobType,
                ["Attempt"] = job.Attempt
            }
        };

        await sender.SendMessageAsync(message, ct);
    }
}
```

---

## Background Service Processor

```csharp
public class ServiceBusJobProcessor : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var processor = _serviceBusClient.CreateProcessor(_queueName, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = _maxConcurrentCalls,
            AutoCompleteMessages = false,
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(10)
        });

        processor.ProcessMessageAsync += ProcessMessageAsync;
        processor.ProcessErrorAsync += ProcessErrorAsync;

        await processor.StartProcessingAsync(stoppingToken);
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var job = JsonSerializer.Deserialize<JobContract>(args.Message.Body.ToString());

        // Route to handler by JobType
        var handler = _handlers.FirstOrDefault(h => h.JobType == job.JobType);
        if (handler == null)
        {
            await args.DeadLetterMessageAsync(args.Message, "NoHandler", "...");
            return;
        }

        var outcome = await handler.ProcessAsync(job, args.CancellationToken);

        if (outcome.IsSuccess)
            await args.CompleteMessageAsync(args.CancellationToken);
        else if (job.Attempt < MaxRetries)
            await RetryJobAsync(job, args, outcome);
        else
            await args.DeadLetterMessageAsync(args.Message, outcome.ErrorCode, outcome.ErrorMessage);
    }
}
```

---

## Idempotency Key Patterns

| Job Type | Key Pattern |
|----------|-------------|
| Document indexing | `doc-{docId}-v{rowVersion}` |
| AI analysis | `analysis-{docId}-{analysisType}` |
| Email processing | `email-{messageId}` |

---

## Service Registration

```csharp
// WorkersModule.cs
services.AddSingleton<JobSubmissionService>();
services.AddHostedService<ServiceBusJobProcessor>();

// Register handlers
services.AddScoped<IJobHandler, DocumentProcessingJobHandler>();
services.AddScoped<IJobHandler, DocumentAnalysisJobHandler>();
```

---

## Usage in Endpoints

```csharp
app.MapPost("/api/analysis/enqueue", async (
    AnalysisRequest request,
    JobSubmissionService jobs,
    CancellationToken ct) =>
{
    var job = new JobContract
    {
        JobId = Guid.NewGuid(),
        JobType = "ai-analyze",
        SubjectId = request.DocumentId.ToString(),
        CorrelationId = Activity.Current?.Id ?? Guid.NewGuid().ToString(),
        IdempotencyKey = $"analysis-{request.DocumentId}-{request.Type}",
        Payload = JsonSerializer.SerializeToElement(request)
    };

    await jobs.SubmitJobAsync(job, ct);

    return Results.Accepted(new { job.JobId, statusUrl = $"/api/jobs/{job.JobId}/status" });
});
```

---

## Related Patterns

- [Service Registration](service-registration.md) - Worker DI setup
- [Error Handling](error-handling.md) - Job failure responses

---

**Lines**: ~175

