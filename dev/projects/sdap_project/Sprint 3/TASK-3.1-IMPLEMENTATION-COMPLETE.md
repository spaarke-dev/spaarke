# Task 3.1: Background Job Consolidation - COMPLETE ✅

**Date Completed**: October 1, 2025
**Sprint**: Sprint 3 - Phase 3 (Architecture Cleanup)
**Estimated Effort**: 2-3 days
**Actual Implementation**: Completed in session

## Summary

Successfully unified background job processing strategy by creating a **single entry point** (JobSubmissionService) that routes to either **Service Bus** (production) or **in-memory queue** (development) based on configuration. Implemented generic ServiceBusJobProcessor that adheres to ADR-004 job contract, eliminating confusion about which processing system to use.

## Architecture Achievement

### Before: Dual Competing Systems ❌
```
Application Code
       │
       ├─────────────────────┬──────────────────────┐
       │                     │                      │
   JobProcessor        DocumentEventProcessor      ???
   (in-memory)         (Service Bus)           (confusion)
```

### After: Unified Strategy with Feature Flag ✅
```
Application Code
       │
       ↓
JobSubmissionService ← Single entry point
       │
       ├─ Jobs:UseServiceBus = true  → ServiceBusJobProcessor (Production)
       │                                     ↓
       │                               Azure Service Bus → IJobHandler routing
       │
       └─ Jobs:UseServiceBus = false → JobProcessor (Development Only)
                                             ↓
                                       In-Memory Queue → IJobHandler routing

DocumentEventProcessor (Coexists for document-events queue from Dataverse plugins)
```

## Changes Implemented

### 1. Created JobSubmissionService (Unified Entry Point)

**New File**: [JobSubmissionService.cs](../../../src/api/Spe.Bff.Api/Services/Jobs/JobSubmissionService.cs)

**Purpose**: Single point of entry for all job submissions, automatically routing to the correct processor.

**Key Features**:
- **Feature flag driven**: `Jobs:UseServiceBus` (true = production, false = dev)
- **Fail-fast validation**: Throws clear errors if dependencies not configured
- **Dual mode support**: Service Bus or in-memory queue
- **ADR-004 compliant**: Uses JobContract for all submissions
- **Comprehensive logging**: Tracks which mode is active and all submissions

**Code Highlights**:
```csharp
public async Task SubmitJobAsync(JobContract job, CancellationToken ct = default)
{
    ArgumentNullException.ThrowIfNull(job);

    if (job.JobId == Guid.Empty)
        throw new ArgumentException("Job.JobId cannot be empty", nameof(job));

    if (_useServiceBus)
    {
        await SubmitToServiceBusAsync(job, ct);
    }
    else
    {
        SubmitToInMemoryQueue(job);
    }
}
```

### 2. Created ServiceBusJobProcessor (Generic ADR-004 Processor)

**New File**: [ServiceBusJobProcessor.cs](../../../src/api/Spe.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs)

**Purpose**: Generic Service Bus processor that routes jobs to IJobHandler implementations based on JobType.

**Key Features**:
- **Generic routing**: Finds IJobHandler by matching `handler.JobType == job.JobType`
- **ADR-004 compliant**: Deserializes JobContract from Service Bus messages
- **Comprehensive error handling**:
  - 404 handler → Dead-letter
  - Max attempts → Dead-letter
  - Retryable failures → Abandon (redelivery)
  - Unexpected errors → Dead-letter after 3 attempts
- **Idempotency support**: Respects JobContract.IdempotencyKey
- **Configurable concurrency**: `Jobs:ServiceBus:MaxConcurrentCalls`

**Code Highlights**:
```csharp
// Find appropriate handler
var handlers = scope.ServiceProvider.GetServices<IJobHandler>().ToList();
var handler = handlers.FirstOrDefault(h => h.JobType == job.JobType);

if (handler == null)
{
    await args.DeadLetterMessageAsync(args.Message, "NoHandler",
        $"No handler registered for job type '{job.JobType}'");
    return;
}

// Process job
var outcome = await handler.ProcessAsync(job, args.CancellationToken);

if (outcome.Status == JobStatus.Completed)
{
    await args.CompleteMessageAsync(args.Message);
}
else if (outcome.Status == JobStatus.Poisoned || job.IsAtMaxAttempts)
{
    await args.DeadLetterMessageAsync(args.Message, "MaxRetriesExceeded", outcome.ErrorMessage);
}
else
{
    await args.AbandonMessageAsync(args.Message); // Retry
}
```

### 3. Marked JobProcessor as Development-Only

**File Modified**: [JobProcessor.cs:10](../../../src/api/Spe.Bff.Api/Services/BackgroundServices/JobProcessor.cs#L10)

**Changes**:
```csharp
/// <summary>
/// In-memory background service that processes jobs using the ADR-004 job contract.
/// Provides idempotent processing, retry logic, and poison queue handling.
///
/// ⚠️ DEVELOPMENT ONLY: This processor uses an in-memory queue and is NOT durable.
/// Jobs are lost on application restart. Use ServiceBusJobProcessor for production.
///
/// Enable via configuration: Jobs:UseServiceBus = false
/// </summary>
public class JobProcessor : BackgroundService
```

**Purpose**: Clear documentation that this is not production-ready.

### 4. Added Feature Flag Configuration

**File Modified**: [appsettings.json](../../../src/api/Spe.Bff.Api/appsettings.json)
```json
{
  "Jobs": {
    "UseServiceBus": true,
    "ServiceBus": {
      "QueueName": "sdap-jobs",
      "MaxConcurrentCalls": 5
    }
  }
}
```

**File Modified**: [appsettings.Development.json](../../../src/api/Spe.Bff.Api/appsettings.Development.json)
```json
{
  "Jobs": {
    "UseServiceBus": false,
    "ServiceBus": {
      "QueueName": "sdap-jobs",
      "MaxConcurrentCalls": 2
    }
  }
}
```

**Result**:
- **Production**: Uses Service Bus automatically
- **Development**: Uses in-memory queue for easier local development

### 5. Updated DI Registration with Conditional Logic

**File Modified**: [Program.cs:197-232](../../../src/api/Spe.Bff.Api/Program.cs#L197)

**Changes**:
```csharp
// Background Job Processing (ADR-004) - Unified Strategy
var useServiceBus = builder.Configuration.GetValue<bool>("Jobs:UseServiceBus", true);

// Always register JobSubmissionService (unified entry point)
builder.Services.AddSingleton<JobSubmissionService>();

// Register job handlers (used by both processors)
builder.Services.AddScoped<IJobHandler, DocumentProcessingJobHandler>();
// TODO: Register additional IJobHandler implementations here

if (useServiceBus)
{
    // Production: Service Bus mode
    var serviceBusConnectionString = builder.Configuration.GetConnectionString("ServiceBus");
    if (!string.IsNullOrWhiteSpace(serviceBusConnectionString))
    {
        builder.Services.AddSingleton(sp => new ServiceBusClient(serviceBusConnectionString));
        builder.Services.AddHostedService<ServiceBusJobProcessor>();
        builder.Logging.AddConsole();
        Console.WriteLine("✓ Job processing configured with Service Bus (queue: sdap-jobs)");
    }
    else
    {
        throw new InvalidOperationException(
            "Jobs:UseServiceBus is true but ServiceBus:ConnectionString is not configured.");
    }
}
else
{
    // Development: In-memory mode
    builder.Services.AddSingleton<JobProcessor>();
    builder.Services.AddHostedService<JobProcessor>(sp =>
        sp.GetRequiredService<JobProcessor>());
    Console.WriteLine("⚠️ Job processing configured with In-Memory queue (DEVELOPMENT ONLY - not durable)");
}
```

**Features**:
- **Conditional registration**: Only one processor active based on configuration
- **Clear startup messages**: Console output shows which mode is active
- **Fail-fast validation**: Errors immediately if misconfigured
- **Shared handler registration**: IJobHandler implementations work with both processors

### 6. Documented DocumentEventProcessor Coexistence

**File Modified**: [DocumentEventProcessor.cs:8](../../../src/api/Spe.Bff.Api/Services/Jobs/DocumentEventProcessor.cs#L8)

**Changes**:
```csharp
/// <summary>
/// Background service that processes document events from Azure Service Bus.
/// Implements the async processing pipeline for document operations from Dataverse plugins.
///
/// NOTE: This processor is SPECIFIC to Document events from Dataverse plugins.
/// For general job processing, use ServiceBusJobProcessor with ADR-004 JobContract.
/// Both processors can coexist - this handles document-events queue, ServiceBusJobProcessor handles sdap-jobs queue.
/// </summary>
public class DocumentEventProcessor : BackgroundService
```

**Rationale**: DocumentEventProcessor serves a specific purpose (Dataverse plugin events) and uses a different queue. It's not competing with the unified job strategy - they coexist.

## Benefits Achieved

### 1. Eliminated Confusion
- **Before**: Two systems, unclear which to use
- **After**: Single entry point (`JobSubmissionService`), clear routing

### 2. Simplified Development
- **Before**: Manual queue management or complex Service Bus setup for local dev
- **After**: Feature flag switches between modes automatically

### 3. Production-Ready Architecture
- **Service Bus**: Durable, distributed, scalable
- **In-Memory**: Fast, simple, perfect for local testing
- **Clean separation**: No production code paths in dev-only processor

### 4. ADR-004 Compliance
- All jobs use `JobContract` standard envelope
- All handlers implement `IJobHandler` interface
- Consistent retry, idempotency, and poison queue handling

### 5. Maintainability
- Single place to add new job handlers (DI registration)
- Clear documentation of dev vs. production modes
- Feature flag makes deployment configuration trivial

## Build Verification

**Status**: ✅ **PASSED**

```
Build succeeded.
0 Error(s)
3 Warning(s) (existing warnings only - deferred to Task 4.3)
Time Elapsed 00:00:01.52
```

## Alignment with ADRs

| ADR | Compliance | Evidence |
|-----|-----------|----------|
| **ADR-004 (Async Job Contract)** | ✅ | JobContract used universally, IJobHandler pattern enforced |
| **ADR-001 (Minimal API)** | ✅ | Background services use IHostedService, proper DI scoping |
| **ADR-010 (DI Minimalism)** | ✅ | Clear, minimal DI registration, no ambiguity |

## Files Changed

### Created
1. **JobSubmissionService.cs** (150 lines) - Unified job submission entry point
2. **ServiceBusJobProcessor.cs** (200 lines) - Generic ADR-004 processor

### Modified
3. **JobProcessor.cs** - Added dev-only warning documentation
4. **DocumentEventProcessor.cs** - Added coexistence documentation
5. **appsettings.json** - Added `Jobs:UseServiceBus` configuration (production = true)
6. **appsettings.Development.json** - Added `Jobs:UseServiceBus` configuration (dev = false)
7. **Program.cs** - Added conditional DI registration with feature flag

### Total Impact
- **Lines Added**: ~350 lines of production code
- **Lines Modified**: ~30 lines of documentation updates
- **Configuration**: 2 new settings files updated

## Usage Examples

### Submitting a Job (Application Code)
```csharp
public class MyService
{
    private readonly JobSubmissionService _jobSubmission;

    public MyService(JobSubmissionService jobSubmission)
    {
        _jobSubmission = jobSubmission;
    }

    public async Task ProcessDocumentAsync(string documentId)
    {
        var job = new JobContract
        {
            JobType = "document-processing",
            SubjectId = documentId,
            IdempotencyKey = $"process-{documentId}",
            Payload = JsonDocument.Parse($"{{\"documentId\":\"{documentId}\"}}")
        };

        await _jobSubmission.SubmitJobAsync(job);
    }
}
```

### Creating a Job Handler
```csharp
public class MyJobHandler : IJobHandler
{
    public string JobType => "my-job-type";

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        try
        {
            // Process job
            await DoWorkAsync(job.Payload);

            return JobOutcome.Success();
        }
        catch (Exception ex)
        {
            return JobOutcome.Failure(ex.Message);
        }
    }
}

// Register in Program.cs
builder.Services.AddScoped<IJobHandler, MyJobHandler>();
```

## Next Steps

Task 3.1 is now complete. Ready to proceed with:
- ✅ **Task 1.1**: Authorization Implementation (COMPLETE)
- ✅ **Task 1.2**: Configuration & Deployment (COMPLETE)
- ✅ **Task 2.1**: OboSpeService Real Implementation (COMPLETE)
- ✅ **Task 2.2**: Dataverse Cleanup (COMPLETE)
- ✅ **Task 3.1**: Background Job Consolidation (COMPLETE)
- ⏭️ **Task 3.2**: SpeFileStore Refactoring (5-6 days) - Break up 604-line god class
- ⏭️ **Task 4.1**: Centralized Resilience (2-3 days)
- ⏭️ **Task 4.2**: Testing Improvements (4-5 days)
- ⏭️ **Task 4.3**: Code Quality & Consistency (2 days)

## Notes

- **DocumentEventProcessor remains unchanged**: It serves a specific purpose (Dataverse plugin events) and doesn't compete with the unified job strategy
- **Future job types**: Simply implement IJobHandler and register in DI
- **Queue naming**: `sdap-jobs` for generic jobs, `document-events` for Dataverse plugin events
- **Development workflow**: Set `Jobs:UseServiceBus=false` in appsettings.Development.json for local testing
- **Production deployment**: Ensure `Jobs:UseServiceBus=true` and `ServiceBus:ConnectionString` configured
