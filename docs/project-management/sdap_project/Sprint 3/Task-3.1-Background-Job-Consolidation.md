# Task 3.1: Background Job Consolidation - Unify Job Processing Strategy

**Priority:** MEDIUM (Sprint 3, Phase 3)
**Estimated Effort:** 2-3 days
**Status:** IMPROVES ARCHITECTURE
**Dependencies:** Task 1.2 (Configuration - Service Bus setup)

---

## Context & Problem Statement

The application has **two competing background job processing systems** that coexist but don't integrate:

1. **JobProcessor** (In-Memory Queue):
   - File: `C:\code_files\spaarke\src\api\Spe.Bff.Api\Services\BackgroundServices\JobProcessor.cs`
   - Uses `ConcurrentQueue<JobContract>` for in-memory job storage
   - Implements ADR-004 job contract (JobContract, JobOutcome, IJobHandler)
   - Lines 18-24: In-memory queue and processed jobs dictionary
   - Line 32: EnqueueJob method for manual job submission
   - Not durable - jobs lost on restart

2. **DocumentEventProcessor** (Service Bus):
   - File: `C:\code_files\spaarke\src\api\Spe.Bff.Api\Services\Jobs\DocumentEventProcessor.cs`
   - Uses Azure Service Bus for durable messaging
   - Lines 14-39: ServiceBusProcessor with PeekLock mode
   - Implements idempotency, retry, dead-letter queue
   - Production-ready with distributed processing support

**Problem**: Both systems exist but serve similar purposes, creating confusion about which to use and when.

---

## Goals & Outcomes

### Primary Goals
1. Choose **Service Bus as the primary job processing mechanism** for MVP
2. Either remove in-memory `JobProcessor` or refactor it as a local development fallback
3. Ensure all background jobs use consistent job contract (ADR-004)
4. Integrate `IJobHandler` pattern with Service Bus processing
5. Document the unified job processing strategy

### Success Criteria
- [ ] Single, clear job processing strategy documented
- [ ] Service Bus used for production async jobs
- [ ] In-memory processor either removed or marked as dev-only
- [ ] All job handlers implement `IJobHandler` interface
- [ ] Job submission always goes through Service Bus (or dev queue)
- [ ] Integration tests validate end-to-end job flow
- [ ] ADR-004 job contract fully implemented

### Non-Goals
- Advanced scheduling (cron jobs, delayed execution) - Sprint 4+
- Job orchestration/workflows - Sprint 4+
- Job prioritization - Sprint 4+

---

## Architecture & Design

### Current State (Sprint 2) - Dual Systems
```
┌──────────────────────┐
│  Application Code    │
└──────┬───────────────┘
       │
       ├────────────────────────────┐
       │                            │
       v                            v
┌──────────────────┐      ┌─────────────────────┐
│   JobProcessor   │      │ DocumentEvent       │
│   (In-Memory)    │      │ Processor           │
│                  │      │ (Service Bus)       │
│ ConcurrentQueue  │      │ ServiceBusProcessor │
│ EnqueueJob()     │      │ ProcessMessageAsync │
└──────┬───────────┘      └──────┬──────────────┘
       │                         │
       v                         v
┌──────────────────┐      ┌─────────────────────┐
│ IJobHandler      │      │ IDocumentEvent      │
│ chain            │      │ Handler             │
└──────────────────┘      └─────────────────────┘
```

### Target State (Sprint 3) - Unified with Service Bus Primary
```
┌──────────────────────┐
│  Application Code    │
└──────┬───────────────┘
       │
       v
┌─────────────────────────┐
│ JobSubmissionService    │ ← Single entry point
│ - SubmitJobAsync()      │
└──────┬──────────────────┘
       │
       v
┌─────────────────────────┐
│ Azure Service Bus       │ ← Durable, distributed
│ - Queue: sdap-jobs      │
│ - Dead Letter Queue     │
└──────┬──────────────────┘
       │
       v
┌─────────────────────────┐
│ ServiceBusJobProcessor  │ ← Unified processor
│ - Receives messages     │
│ - Routes to IJobHandler │
│ - Idempotency tracking  │
└──────┬──────────────────┘
       │
       v
┌─────────────────────────┐
│ IJobHandler Registry    │
│ - DocumentUploadHandler │
│ - DocumentDeleteHandler │
│ - ...                   │
└─────────────────────────┘

Alternative (Dev-Only):
┌─────────────────────────┐
│ In-Memory Job Processor │ ← Fallback for local dev
│ (Feature Flag: UseSB)   │
└─────────────────────────┘
```

---

## Relevant ADRs

### ADR-004: Async Job Contract
- **JobContract**: Standard job envelope with idempotency key, retry count, payload
- **JobOutcome**: Success, Failure, or Poisoned outcomes
- **IJobHandler**: Single interface for all job handlers
- **Retry Logic**: Exponential backoff, max attempts (3-5)
- **Poison Queue**: Failed jobs after max retries

### ADR-001: Minimal API and Workers
- **Background Services**: Use IHostedService for long-running tasks
- **Scoped Dependencies**: Create scope per job for proper DI lifecycle

---

## Implementation Steps

### Step 1: Decision - Keep or Remove In-Memory JobProcessor

**Option A: Remove Entirely** (Recommended for production-first approach)
- Delete `JobProcessor.cs`
- Use Service Bus for all environments
- Local dev uses Azure Service Bus Emulator or shared dev namespace

**Option B: Keep as Development Fallback** (Recommended for easier local dev)
- Mark `JobProcessor` as dev-only via feature flag
- Use in-memory queue when Service Bus not configured
- Production always uses Service Bus

**Recommendation**: **Option B** - Keep both with feature flag for flexibility.

---

### Step 2: Create Unified Job Submission Service

**New File**: `C:\code_files\spaarke\src\api\Spe.Bff.Api\Services\Jobs\JobSubmissionService.cs`

```csharp
using Azure.Messaging.ServiceBus;
using Spe.Bff.Api.Services.BackgroundServices;
using System.Text.Json;

namespace Spe.Bff.Api.Services.Jobs;

/// <summary>
/// Unified job submission service that routes jobs to the appropriate processor
/// (Service Bus or in-memory queue based on configuration).
/// </summary>
public class JobSubmissionService
{
    private readonly ServiceBusClient? _serviceBusClient;
    private readonly JobProcessor? _inMemoryProcessor;
    private readonly ILogger<JobSubmissionService> _logger;
    private readonly bool _useServiceBus;

    public JobSubmissionService(
        IConfiguration configuration,
        ILogger<JobSubmissionService> logger,
        ServiceBusClient? serviceBusClient = null,
        JobProcessor? inMemoryProcessor = null)
    {
        _logger = logger;
        _serviceBusClient = serviceBusClient;
        _inMemoryProcessor = inMemoryProcessor;
        _useServiceBus = configuration.GetValue<bool>("Jobs:UseServiceBus", true);

        if (_useServiceBus && _serviceBusClient == null)
        {
            throw new InvalidOperationException(
                "Service Bus is enabled but ServiceBusClient is not registered. " +
                "Configure ServiceBus:ConnectionString or set Jobs:UseServiceBus=false.");
        }

        if (!_useServiceBus && _inMemoryProcessor == null)
        {
            throw new InvalidOperationException(
                "In-memory job processing is enabled but JobProcessor is not registered.");
        }

        _logger.LogInformation("Job submission configured with {Mode} mode",
            _useServiceBus ? "Service Bus" : "In-Memory");
    }

    /// <summary>
    /// Submits a job for asynchronous processing.
    /// Routes to Service Bus (production) or in-memory queue (development).
    /// </summary>
    public async Task SubmitJobAsync(JobContract job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (_useServiceBus)
        {
            await SubmitToServiceBusAsync(job, ct);
        }
        else
        {
            SubmitToInMemoryQueue(job);
        }
    }

    private async Task SubmitToServiceBusAsync(JobContract job, CancellationToken ct)
    {
        try
        {
            var queueName = "sdap-jobs"; // Could be from configuration
            var sender = _serviceBusClient!.CreateSender(queueName);

            var messageBody = JsonSerializer.Serialize(job);
            var message = new ServiceBusMessage(messageBody)
            {
                MessageId = job.JobId,
                CorrelationId = job.IdempotencyKey,
                ContentType = "application/json",
                Subject = job.JobType
            };

            await sender.SendMessageAsync(message, ct);

            _logger.LogInformation("Job {JobId} ({JobType}) submitted to Service Bus queue {QueueName}",
                job.JobId, job.JobType, queueName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit job {JobId} to Service Bus", job.JobId);
            throw;
        }
    }

    private void SubmitToInMemoryQueue(JobContract job)
    {
        _inMemoryProcessor!.EnqueueJob(job);
        _logger.LogInformation("Job {JobId} ({JobType}) submitted to in-memory queue",
            job.JobId, job.JobType);
    }

    /// <summary>
    /// Gets the current queue depth (for monitoring).
    /// Only works with in-memory queue.
    /// </summary>
    public int? GetQueueDepth()
    {
        if (!_useServiceBus && _inMemoryProcessor != null)
        {
            return _inMemoryProcessor.QueueDepth;
        }
        return null;
    }
}
```

---

### Step 3: Refactor Service Bus Processor to Use IJobHandler

**Current**: `DocumentEventProcessor` handles document-specific events.

**Target**: Generic `ServiceBusJobProcessor` that routes to `IJobHandler` based on job type.

**New File**: `C:\code_files\spaarke\src\api\Spe.Bff.Api\Services\Jobs\ServiceBusJobProcessor.cs`

```csharp
using Azure.Messaging.ServiceBus;
using System.Text.Json;

namespace Spe.Bff.Api.Services.Jobs;

/// <summary>
/// Generic Service Bus processor that handles jobs using the ADR-004 job contract.
/// Routes jobs to appropriate IJobHandler implementations based on JobType.
/// </summary>
public class ServiceBusJobProcessor : BackgroundService
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ServiceBusJobProcessor> _logger;
    private readonly string _queueName;
    private ServiceBusProcessor? _processor;

    public ServiceBusJobProcessor(
        ServiceBusClient serviceBusClient,
        IServiceProvider serviceProvider,
        ILogger<ServiceBusJobProcessor> logger,
        IConfiguration configuration)
    {
        _serviceBusClient = serviceBusClient;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _queueName = configuration["Jobs:ServiceBus:QueueName"] ?? "sdap-jobs";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Service Bus Job Processor starting for queue {QueueName}...", _queueName);

        try
        {
            _processor = _serviceBusClient.CreateProcessor(_queueName, new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = 5,
                AutoCompleteMessages = false,
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(10),
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });

            _processor.ProcessMessageAsync += ProcessMessageAsync;
            _processor.ProcessErrorAsync += ProcessErrorAsync;

            await _processor.StartProcessingAsync(stoppingToken);
            _logger.LogInformation("Service Bus Job Processor started successfully");

            // Keep service running
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Service Bus Job Processor stopping...");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Service Bus Job Processor failed to start: {Error}", ex.Message);
            throw;
        }
        finally
        {
            if (_processor != null)
            {
                await _processor.StopProcessingAsync();
                await _processor.DisposeAsync();
            }
            _logger.LogInformation("Service Bus Job Processor stopped");
        }
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        using var scope = _serviceProvider.CreateScope();
        var startTime = DateTime.UtcNow;

        try
        {
            // Deserialize job contract
            var messageBody = args.Message.Body.ToString();
            var job = JsonSerializer.Deserialize<JobContract>(messageBody);

            if (job == null)
            {
                _logger.LogError("Failed to deserialize job from message {MessageId}", args.Message.MessageId);
                await args.DeadLetterMessageAsync(args.Message, "InvalidFormat", "Failed to deserialize job", args.CancellationToken);
                return;
            }

            _logger.LogInformation("Processing job {JobId} of type {JobType}, attempt {Attempt}/{MaxAttempts}",
                job.JobId, job.JobType, job.Attempt, job.MaxAttempts);

            // Find appropriate handler
            var handlers = scope.ServiceProvider.GetServices<IJobHandler>();
            var handler = handlers.FirstOrDefault(h => h.JobType == job.JobType);

            if (handler == null)
            {
                _logger.LogError("No handler found for job type {JobType}", job.JobType);
                await args.DeadLetterMessageAsync(args.Message, "NoHandler", $"No handler for {job.JobType}", args.CancellationToken);
                return;
            }

            // Process job
            var outcome = await handler.ProcessAsync(job, args.CancellationToken);

            if (outcome.Status == JobStatus.Completed)
            {
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                _logger.LogInformation("Job {JobId} completed successfully in {Duration}ms",
                    job.JobId, outcome.Duration.TotalMilliseconds);
            }
            else if (outcome.Status == JobStatus.Poisoned || job.IsAtMaxAttempts)
            {
                await args.DeadLetterMessageAsync(args.Message, "MaxRetriesExceeded", outcome.ErrorMessage ?? "Unknown error", args.CancellationToken);
                _logger.LogError("Job {JobId} failed after {MaxAttempts} attempts: {Error}",
                    job.JobId, job.MaxAttempts, outcome.ErrorMessage);
            }
            else
            {
                // Retry - abandon message
                await args.AbandonMessageAsync(args.Message, null, args.CancellationToken);
                _logger.LogWarning("Job {JobId} failed on attempt {Attempt}, will retry: {Error}",
                    job.JobId, job.Attempt, outcome.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing message {MessageId}", args.Message.MessageId);

            if (args.Message.DeliveryCount >= 3)
            {
                await args.DeadLetterMessageAsync(args.Message, "ProcessingError", ex.Message, args.CancellationToken);
            }
            else
            {
                await args.AbandonMessageAsync(args.Message, null, args.CancellationToken);
            }
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Service Bus processor error in {EntityPath}: {Error}",
            args.EntityPath, args.Exception.Message);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Service Bus Job Processor...");
        if (_processor != null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }
        await base.StopAsync(cancellationToken);
    }
}
```

---

### Step 4: Update DI Registration

**File**: `C:\code_files\spaarke\src\api\Spe.Bff.Api\Program.cs`

```csharp
// Configuration
var useServiceBus = builder.Configuration.GetValue<bool>("Jobs:UseServiceBus", true);

// Job handlers (always register)
builder.Services.AddScoped<IJobHandler, DocumentUploadHandler>();
builder.Services.AddScoped<IJobHandler, DocumentDeleteHandler>();
// ... register all handlers

// Job submission service (unified entry point)
builder.Services.AddSingleton<JobSubmissionService>();

if (useServiceBus)
{
    // Service Bus mode (production)
    var serviceBusConnectionString = builder.Configuration["ServiceBus:ConnectionString"];
    builder.Services.AddSingleton(sp => new ServiceBusClient(serviceBusConnectionString));
    builder.Services.AddHostedService<ServiceBusJobProcessor>();

    builder.Logging.AddConsole(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Information;
    });

    Console.WriteLine("✓ Job processing configured with Service Bus");
}
else
{
    // In-memory mode (development)
    builder.Services.AddSingleton<JobProcessor>();
    builder.Services.AddHostedService<JobProcessor>();

    Console.WriteLine("⚠ Job processing configured with In-Memory queue (development only)");
}
```

**Configuration** (`appsettings.json`):
```json
{
  "Jobs": {
    "UseServiceBus": true,
    "ServiceBus": {
      "QueueName": "sdap-jobs"
    }
  }
}
```

**Development** (`appsettings.Development.json`):
```json
{
  "Jobs": {
    "UseServiceBus": false
  }
}
```

---

### Step 5: Deprecate DocumentEventProcessor (Optional)

**Option A**: Replace entirely with `ServiceBusJobProcessor`

**Option B**: Keep for document-specific events, use `ServiceBusJobProcessor` for generic jobs

**Recommendation**: Replace with generic processor to unify strategy.

---

## AI Coding Prompts

### Prompt 1: Create JobSubmissionService
```
Create unified job submission service that routes to Service Bus or in-memory queue:

Context:
- Need single entry point for job submission
- Support Service Bus (production) and in-memory (dev) modes
- Use feature flag Jobs:UseServiceBus

Requirements:
1. Create JobSubmissionService with SubmitJobAsync method
2. Constructor takes ServiceBusClient? and JobProcessor? (both optional)
3. Read Jobs:UseServiceBus from configuration
4. Route to Service Bus if enabled, else in-memory queue
5. Serialize JobContract to JSON for Service Bus messages
6. Log submission with job ID and type
7. Throw clear error if dependencies not registered

Code Quality:
- Senior C# developer standards
- ArgumentNullException.ThrowIfNull for validation
- Comprehensive logging
- Async all the way
- Follow ADR-004 job contract

Files to Create:
- C:\code_files\spaarke\src\api\Spe.Bff.Api\Services\Jobs\JobSubmissionService.cs
```

### Prompt 2: Create ServiceBusJobProcessor
```
Create generic Service Bus job processor using IJobHandler pattern:

Context:
- Replace DocumentEventProcessor with generic job processor
- Route to IJobHandler based on job.JobType
- Implement ADR-004 job contract fully

Requirements:
1. Create ServiceBusJobProcessor : BackgroundService
2. Deserialize ServiceBusMessage to JobContract
3. Find IJobHandler by job.JobType
4. Process job and handle outcome (Completed, Failure, Poisoned)
5. Complete message on success, abandon on retry, dead-letter on poison
6. Respect MaxAttempts and delivery count
7. Comprehensive error handling and logging

Code Quality:
- Senior C# developer standards
- Create DI scope per message
- Dispose resources properly
- Handle OperationCanceledException
- Follow ADR-004 patterns

Files to Create:
- C:\code_files\spaarke\src\api\Spe.Bff.Api\Services\Jobs\ServiceBusJobProcessor.cs
```

### Prompt 3: Update DI Registration with Feature Flag
```
Update Program.cs to conditionally register job processors:

Context:
- Support both Service Bus and in-memory modes
- Use feature flag Jobs:UseServiceBus
- Production uses Service Bus, dev can use in-memory

Requirements:
1. Read Jobs:UseServiceBus from configuration (default true)
2. If true: Register ServiceBusClient and ServiceBusJobProcessor
3. If false: Register JobProcessor as singleton and hosted service
4. Always register JobSubmissionService
5. Always register all IJobHandler implementations
6. Log which mode is active at startup

Code Quality:
- Clear console messages showing configuration
- Fail fast if Service Bus enabled but connection string missing
- Development-friendly (easy to switch modes)

Files to Modify:
- C:\code_files\spaarke\src\api\Spe.Bff.Api\Program.cs
```

---

## Testing Strategy

### Unit Tests
1. **JobSubmissionService**:
   - Test Service Bus mode submission
   - Test in-memory mode submission
   - Test error handling when dependencies missing

2. **ServiceBusJobProcessor**:
   - Mock ServiceBusMessage deserialization
   - Test handler resolution by job type
   - Test complete/abandon/dead-letter logic
   - Test max attempts handling

### Integration Tests
1. **End-to-End Job Flow**:
   - Submit job via JobSubmissionService
   - Verify job processed by handler
   - Check job outcome persisted

2. **Service Bus Integration**:
   - Use Azure Service Bus Emulator
   - Test job submission and processing
   - Verify dead-letter queue behavior

3. **Feature Flag**:
   - Test with UseServiceBus=true
   - Test with UseServiceBus=false
   - Verify correct processor active

---

## Validation Checklist

Before marking this task complete, verify:

- [ ] JobSubmissionService created and registered
- [ ] ServiceBusJobProcessor created (generic handler router)
- [ ] Feature flag Jobs:UseServiceBus implemented
- [ ] DI registration conditional based on flag
- [ ] All job handlers implement IJobHandler
- [ ] In-memory JobProcessor marked as dev-only (or removed)
- [ ] DocumentEventProcessor replaced or deprecated
- [ ] Configuration documented (appsettings.json)
- [ ] Integration tests pass in both modes
- [ ] Code review completed

---

## Completion Criteria

Task is complete when:
1. Single job submission entry point (JobSubmissionService)
2. Service Bus primary, in-memory optional for dev
3. All jobs use ADR-004 contract
4. Tests validate both modes
5. Documentation updated
6. Code review approved

**Estimated Completion: 2-3 days**
