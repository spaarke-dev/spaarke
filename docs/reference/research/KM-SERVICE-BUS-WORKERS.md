# Azure Service Bus Worker Templates for Spaarke

## Overview
This guide provides templates and patterns for implementing Azure Service Bus workers as BackgroundService instances in ASP.NET Core, aligned with ADR-001 and ADR-004.

## NuGet Packages Required
```xml
<PackageReference Include="Azure.Messaging.ServiceBus" Version="7.17.0" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
<PackageReference Include="Polly" Version="8.3.0" />
<PackageReference Include="Microsoft.ApplicationInsights.WorkerService" Version="2.22.0" />
```

## Base Worker Template

### Job Contract (per ADR-004)
```csharp
public record JobEnvelope
{
    public required Guid JobId { get; init; }
    public required string JobType { get; init; }
    public required Guid SubjectId { get; init; }  // Document/Matter ID
    public required string CorrelationId { get; init; }
    public required string IdempotencyKey { get; init; }
    public required int Attempt { get; init; }
    public required int MaxAttempts { get; init; }
    public DateTimeOffset EnqueuedAt { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
    
    // Job-specific payload
    public required string PayloadJson { get; init; }
    
    public T DeserializePayload<T>() => 
        JsonSerializer.Deserialize<T>(PayloadJson);
}

public record JobOutcome
{
    public required Guid JobId { get; init; }
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
    public TimeSpan Duration { get; init; }
}
```

### Base Background Service Worker
```csharp
public abstract class ServiceBusWorker<TPayload> : BackgroundService
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly ITelemetryClient _telemetry;
    private ServiceBusProcessor? _processor;
    
    protected abstract string TopicName { get; }
    protected abstract string SubscriptionName { get; }
    protected virtual int MaxConcurrentCalls => 10;
    protected virtual TimeSpan MaxAutoLockRenewal => TimeSpan.FromMinutes(10);
    
    protected ServiceBusWorker(
        ServiceBusClient serviceBusClient,
        IServiceProvider serviceProvider,
        ILogger logger,
        ITelemetryClient telemetry)
    {
        _serviceBusClient = serviceBusClient;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _telemetry = telemetry;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = _serviceBusClient.CreateProcessor(
            TopicName, 
            SubscriptionName,
            new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = MaxConcurrentCalls,
                AutoCompleteMessages = false,
                MaxAutoLockRenewalDuration = MaxAutoLockRenewal,
                PrefetchCount = MaxConcurrentCalls * 2
            });
        
        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;
        
        await _processor.StartProcessingAsync(stoppingToken);
        
        // Keep running until cancellation
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
    
    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var envelope = args.Message.Body.ToObjectFromJson<JobEnvelope>();
        using var activity = Activity.StartActivity($"ProcessJob.{envelope.JobType}");
        activity?.SetTag("job.id", envelope.JobId);
        activity?.SetTag("correlation.id", envelope.CorrelationId);
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Check idempotency
            using var scope = _serviceProvider.CreateScope();
            if (await IsAlreadyProcessedAsync(envelope.IdempotencyKey, scope))
            {
                _logger.LogInformation(
                    "Job {JobId} already processed (idempotency key: {Key})",
                    envelope.JobId, envelope.IdempotencyKey);
                await args.CompleteMessageAsync(args.Message);
                return;
            }
            
            // Process with timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            var payload = envelope.DeserializePayload<TPayload>();
            
            await ProcessJobAsync(envelope, payload, scope, cts.Token);
            
            // Mark complete
            await MarkProcessedAsync(envelope.IdempotencyKey, scope);
            await args.CompleteMessageAsync(args.Message);
            
            // Emit success event
            await EmitJobOutcomeAsync(envelope, true, null, stopwatch.Elapsed);
            
            _telemetry.TrackMetric($"job.{envelope.JobType}.duration", 
                stopwatch.ElapsedMilliseconds);
        }
        catch (TransientException ex)
        {
            _logger.LogWarning(ex, 
                "Transient error processing job {JobId}, attempt {Attempt}/{Max}",
                envelope.JobId, envelope.Attempt, envelope.MaxAttempts);
            
            if (envelope.Attempt < envelope.MaxAttempts)
            {
                // Retry with backoff
                await args.AbandonMessageAsync(args.Message, 
                    new Dictionary<string, object>
                    {
                        ["attempt"] = envelope.Attempt + 1,
                        ["lastError"] = ex.Message
                    });
            }
            else
            {
                // Max retries exhausted - dead letter
                await args.DeadLetterMessageAsync(args.Message,
                    deadLetterReason: "MaxRetriesExhausted",
                    deadLetterErrorDescription: ex.ToString());
                    
                await EmitJobOutcomeAsync(envelope, false, ex.Message, stopwatch.Elapsed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Permanent error processing job {JobId}", envelope.JobId);
            
            // Send to dead letter immediately
            await args.DeadLetterMessageAsync(args.Message,
                deadLetterReason: ex.GetType().Name,
                deadLetterErrorDescription: ex.ToString());
                
            await EmitJobOutcomeAsync(envelope, false, ex.Message, stopwatch.Elapsed);
        }
    }
    
    protected abstract Task ProcessJobAsync(
        JobEnvelope envelope, 
        TPayload payload, 
        IServiceScope scope,
        CancellationToken cancellationToken);
    
    private async Task<bool> IsAlreadyProcessedAsync(string idempotencyKey, IServiceScope scope)
    {
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        var result = await cache.GetAsync($"job:processed:{idempotencyKey}");
        return result != null;
    }
    
    private async Task MarkProcessedAsync(string idempotencyKey, IServiceScope scope)
    {
        var cache = scope.ServiceProvider.GetRequiredService<IDistributedCache>();
        await cache.SetAsync(
            $"job:processed:{idempotencyKey}",
            Encoding.UTF8.GetBytes("1"),
            new DistributedCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromDays(7)
            });
    }
    
    private async Task EmitJobOutcomeAsync(
        JobEnvelope envelope, bool success, string? error, TimeSpan duration)
    {
        var outcome = new JobOutcome
        {
            JobId = envelope.JobId,
            Success = success,
            ErrorMessage = error,
            CompletedAt = DateTimeOffset.UtcNow,
            Duration = duration
        };
        
        var sender = _serviceBusClient.CreateSender("job-outcomes");
        await sender.SendMessageAsync(new ServiceBusMessage(
            JsonSerializer.Serialize(outcome))
        {
            Subject = envelope.JobType,
            CorrelationId = envelope.CorrelationId
        });
    }
    
    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "Service Bus processing error. Source: {Source}, Namespace: {Namespace}",
            args.ErrorSource, args.FullyQualifiedNamespace);
            
        _telemetry.TrackException(args.Exception, new Dictionary<string, string>
        {
            ["error.source"] = args.ErrorSource.ToString(),
            ["namespace"] = args.FullyQualifiedNamespace
        });
        
        return Task.CompletedTask;
    }
    
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor != null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }
        
        await base.StopAsync(cancellationToken);
    }
}
```

## Concrete Worker Implementation Examples

### Document Processing Worker
```csharp
public class DocumentProcessingWorker : ServiceBusWorker<DocumentProcessingPayload>
{
    protected override string TopicName => "document-events";
    protected override string SubscriptionName => "document-processor";
    protected override int MaxConcurrentCalls => 20;
    
    public DocumentProcessingWorker(
        ServiceBusClient serviceBusClient,
        IServiceProvider serviceProvider,
        ILogger<DocumentProcessingWorker> logger,
        ITelemetryClient telemetry)
        : base(serviceBusClient, serviceProvider, logger, telemetry)
    {
    }
    
    protected override async Task ProcessJobAsync(
        JobEnvelope envelope,
        DocumentProcessingPayload payload,
        IServiceScope scope,
        CancellationToken cancellationToken)
    {
        var documentService = scope.ServiceProvider.GetRequiredService<DocumentService>();
        var authService = scope.ServiceProvider.GetRequiredService<AuthorizationService>();
        
        // Check authorization
        var authResult = await authService.AuthorizeSystemAsync(
            payload.DocumentId, Operation.Process);
        if (!authResult.IsAuthorized)
        {
            throw new UnauthorizedException($"System cannot process document {payload.DocumentId}");
        }
        
        // Process based on operation type
        switch (payload.Operation)
        {
            case DocumentOperation.ExtractText:
                await ExtractTextAsync(payload.DocumentId, documentService, cancellationToken);
                break;
                
            case DocumentOperation.GeneratePreview:
                await GeneratePreviewAsync(payload.DocumentId, documentService, cancellationToken);
                break;
                
            case DocumentOperation.IndexForSearch:
                await IndexDocumentAsync(payload.DocumentId, scope, cancellationToken);
                break;
                
            default:
                throw new NotSupportedException($"Unknown operation: {payload.Operation}");
        }
    }
    
    private async Task ExtractTextAsync(
        Guid documentId, 
        DocumentService service,
        CancellationToken cancellationToken)
    {
        // Download from SPE
        var document = await service.GetDocumentAsync(documentId, cancellationToken);
        using var stream = await service.DownloadAsync(documentId, cancellationToken);
        
        // Extract text with Document Intelligence
        var extractedText = await ExtractWithDocumentIntelligence(stream, cancellationToken);
        
        // Save to Dataverse
        await service.UpdateDocumentTextAsync(documentId, extractedText, cancellationToken);
    }
}

public record DocumentProcessingPayload
{
    public required Guid DocumentId { get; init; }
    public required DocumentOperation Operation { get; init; }
    public Dictionary<string, string> Parameters { get; init; } = new();
}

public enum DocumentOperation
{
    ExtractText,
    GeneratePreview,
    IndexForSearch,
    ApplyRetention,
    VirusScan
}
```

### Orchestration Worker with Polly
```csharp
public class MatterOrchestrationWorker : ServiceBusWorker<MatterWorkflowPayload>
{
    private readonly IAsyncPolicy _retryPolicy;
    
    protected override string TopicName => "matter-events";
    protected override string SubscriptionName => "matter-orchestrator";
    
    public MatterOrchestrationWorker(
        ServiceBusClient serviceBusClient,
        IServiceProvider serviceProvider,
        ILogger<MatterOrchestrationWorker> logger,
        ITelemetryClient telemetry)
        : base(serviceBusClient, serviceProvider, logger, telemetry)
    {
        _retryPolicy = Policy
            .Handle<TransientException>()
            .WaitAndRetryAsync(
                3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    logger.LogWarning(exception,
                        "Retry {RetryCount} after {Delay}s for {Operation}",
                        retryCount, timeSpan.TotalSeconds, context["operation"]);
                });
    }
    
    protected override async Task ProcessJobAsync(
        JobEnvelope envelope,
        MatterWorkflowPayload payload,
        IServiceScope scope,
        CancellationToken cancellationToken)
    {
        var steps = GetWorkflowSteps(payload.WorkflowType);
        var context = new WorkflowContext
        {
            MatterId = payload.MatterId,
            CorrelationId = envelope.CorrelationId,
            Metadata = envelope.Metadata
        };
        
        foreach (var step in steps)
        {
            await _retryPolicy.ExecuteAsync(
                async () => await ExecuteStepAsync(step, context, scope, cancellationToken),
                new Context { ["operation"] = step.Name });
            
            // Checkpoint after each step
            await SaveCheckpointAsync(envelope.JobId, step.Name, scope);
        }
    }
    
    private async Task ExecuteStepAsync(
        WorkflowStep step,
        WorkflowContext context,
        IServiceScope scope,
        CancellationToken cancellationToken)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<MatterOrchestrationWorker>>();
        logger.LogInformation("Executing step {Step} for matter {MatterId}",
            step.Name, context.MatterId);
        
        var result = await step.ExecuteAsync(context, scope, cancellationToken);
        
        if (!result.Success)
        {
            throw new WorkflowException($"Step {step.Name} failed: {result.Error}");
        }
        
        // Update context with step outputs
        foreach (var (key, value) in result.Outputs)
        {
            context.Metadata[key] = value;
        }
    }
}
```

## Service Bus Configuration

### Dependency Injection Setup
```csharp
// Program.cs
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new ServiceBusClient(
        config["ServiceBus:ConnectionString"],
        new ServiceBusClientOptions
        {
            RetryOptions = new ServiceBusRetryOptions
            {
                Mode = ServiceBusRetryMode.Exponential,
                MaxRetries = 5,
                Delay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(30)
            }
        });
});

// Register workers
builder.Services.AddHostedService<DocumentProcessingWorker>();
builder.Services.AddHostedService<MatterOrchestrationWorker>();
builder.Services.AddHostedService<NotificationWorker>();

// Add health checks
builder.Services.AddHealthChecks()
    .AddAzureServiceBusTopic(
        connectionString: builder.Configuration["ServiceBus:ConnectionString"],
        topicName: "document-events",
        name: "servicebus-documents")
    .AddCheck<WorkerHealthCheck>("workers");
```

### Health Check Implementation
```csharp
public class WorkerHealthCheck : IHealthCheck
{
    private readonly IEnumerable<IHostedService> _workers;
    
    public WorkerHealthCheck(IEnumerable<IHostedService> workers)
    {
        _workers = workers;
    }
    
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var unhealthyWorkers = _workers
            .OfType<BackgroundService>()
            .Where(w => !IsRunning(w))
            .ToList();
        
        if (unhealthyWorkers.Any())
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Workers not running: {string.Join(", ", unhealthyWorkers.Select(w => w.GetType().Name))}"));
        }
        
        return Task.FromResult(HealthCheckResult.Healthy("All workers running"));
    }
    
    private bool IsRunning(BackgroundService service)
    {
        // Check if ExecuteAsync task is still running
        var executingField = typeof(BackgroundService)
            .GetField("_executeTask", BindingFlags.NonPublic | BindingFlags.Instance);
        var task = executingField?.GetValue(service) as Task;
        return task?.IsCompleted == false;
    }
}
```

## Message Publishing Patterns

### Publishing with Correlation
```csharp
public class JobPublisher
{
    private readonly ServiceBusClient _client;
    private readonly IHttpContextAccessor _contextAccessor;
    
    public async Task PublishJobAsync<TPayload>(
        string topicName,
        string jobType,
        Guid subjectId,
        TPayload payload)
    {
        var correlationId = Activity.Current?.Id 
            ?? _contextAccessor.HttpContext?.Items["CorrelationId"]?.ToString()
            ?? Guid.NewGuid().ToString();
        
        var envelope = new JobEnvelope
        {
            JobId = Guid.NewGuid(),
            JobType = jobType,
            SubjectId = subjectId,
            CorrelationId = correlationId,
            IdempotencyKey = GenerateIdempotencyKey(jobType, subjectId, payload),
            Attempt = 1,
            MaxAttempts = 3,
            EnqueuedAt = DateTimeOffset.UtcNow,
            PayloadJson = JsonSerializer.Serialize(payload)
        };
        
        var sender = _client.CreateSender(topicName);
        var message = new ServiceBusMessage(JsonSerializer.Serialize(envelope))
        {
            Subject = jobType,
            CorrelationId = correlationId,
            MessageId = envelope.JobId.ToString(),
            ContentType = "application/json"
        };
        
        // Add custom properties for filtering
        message.ApplicationProperties["JobType"] = jobType;
        message.ApplicationProperties["Priority"] = GetPriority(jobType);
        
        await sender.SendMessageAsync(message);
    }
    
    private string GenerateIdempotencyKey<TPayload>(
        string jobType, Guid subjectId, TPayload payload)
    {
        var json = JsonSerializer.Serialize(new { jobType, subjectId, payload });
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(hash);
    }
}
```

## Testing Service Bus Workers

```csharp
[Fact]
public async Task Worker_ProcessesMessage_Successfully()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddSingleton<ILogger<DocumentProcessingWorker>>(NullLogger<DocumentProcessingWorker>.Instance);
    services.AddSingleton(Mock.Of<ITelemetryClient>());
    services.AddSingleton(Mock.Of<ServiceBusClient>());
    services.AddScoped<DocumentService>();
    services.AddScoped<AuthorizationService>();
    
    var provider = services.BuildServiceProvider();
    var worker = new DocumentProcessingWorker(
        provider.GetRequiredService<ServiceBusClient>(),
        provider,
        provider.GetRequiredService<ILogger<DocumentProcessingWorker>>(),
        provider.GetRequiredService<ITelemetryClient>());
    
    var envelope = new JobEnvelope
    {
        JobId = Guid.NewGuid(),
        JobType = "DocumentProcess",
        SubjectId = Guid.NewGuid(),
        CorrelationId = Guid.NewGuid().ToString(),
        IdempotencyKey = Guid.NewGuid().ToString(),
        Attempt = 1,
        MaxAttempts = 3,
        PayloadJson = JsonSerializer.Serialize(new DocumentProcessingPayload
        {
            DocumentId = Guid.NewGuid(),
            Operation = DocumentOperation.ExtractText
        })
    };
    
    // Act & Assert
    using var scope = provider.CreateScope();
    await worker.ProcessJobAsync(envelope, 
        envelope.DeserializePayload<DocumentProcessingPayload>(),
        scope,
        CancellationToken.None);
}
```

## Key Principles for Service Bus Workers

1. **Always use BackgroundService** - Not Azure Functions (per ADR-001)
2. **Implement idempotency** - Use cache-based deduplication
3. **Handle retries gracefully** - Distinguish transient vs permanent errors
4. **Track with correlation IDs** - Maintain traceability across services
5. **Emit job outcomes** - For monitoring and audit
6. **Use scoped services** - Create scope per message for DI
7. **Implement health checks** - Monitor worker status
8. **Test thoroughly** - Unit test job processing logic