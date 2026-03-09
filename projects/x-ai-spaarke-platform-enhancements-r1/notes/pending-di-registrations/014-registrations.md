# Pending DI Registrations — AIPL-014

> **Task**: AIPL-014 — Implement RagIndexingJobHandler — Service Bus Background Indexing
> **Created**: 2026-02-23
> **Status**: All registrations already present in Program.cs from prior tasks; no new inline registrations required.

---

## Summary

AIPL-014 completes the Service Bus background indexing flow by:

1. **Creating** `Models/Jobs/RagIndexingJob.cs` — job message contract
2. **Creating** unit tests for `RagIndexingJobHandler`
3. **Modifying** `AnalysisOrchestrationService` — emits `RagIndexingJob` after analysis/playbook completes
4. **Verifying** that `RagIndexingJobHandler` and `JobSubmissionService` are already registered in Program.cs

---

## DI Status: No New Registrations Required

### RagIndexingJobHandler — Already Registered (Program.cs line 664)

```csharp
// Program.cs — already present from prior work
builder.Services.AddScoped<Sprk.Bff.Api.Services.Jobs.IJobHandler,
    Sprk.Bff.Api.Services.Jobs.Handlers.RagIndexingJobHandler>();
```

### JobSubmissionService — Already Registered (Program.cs line 645)

```csharp
// Program.cs — already present
builder.Services.AddSingleton<Sprk.Bff.Api.Services.Jobs.JobSubmissionService>();
```

### AnalysisOrchestrationService — JobSubmissionService injection

`AnalysisOrchestrationService` now accepts an **optional** `JobSubmissionService?` parameter
(last in constructor, defaulting to `null`). The DI container resolves it automatically because
`JobSubmissionService` is already registered as a singleton.

No changes to `Program.cs` are required for this injection — the optional parameter pattern
ensures backward compatibility and safe fallback when the service is unavailable (e.g. unit tests).

---

## ADR-010 DI Count Impact

| Before AIPL-014 | Registrations Added | After AIPL-014 |
|-----------------|---------------------|----------------|
| 94              | 0 (all already registered) | 94 |

---

## Files Created / Modified by AIPL-014

| File | Change |
|------|--------|
| `src/server/api/Sprk.Bff.Api/Models/Jobs/RagIndexingJob.cs` | **Created** — job message contract (DocumentId, TenantId, AnalysisId, DocumentUrl, TriggeredAt) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` | **Modified** — injects optional `JobSubmissionService`; emits `RagIndexingJob` after analysis and playbook completion |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Jobs/Handlers/RagIndexingJobHandlerTests.cs` | **Created** — 10 unit tests (all passing) |

---

## AnalysisOrchestrationService Changes

The `AnalysisOrchestrationService` now emits a `RagIndexingJob` to Service Bus at two points:

1. **After `ExecuteAnalysisAsync` completes** — before the final `Completed` chunk is yielded
2. **After `ExecutePlaybookAsync` completes** — before the final `Completed` chunk is yielded

**Idempotency key**: `"{tenantId}:{documentId}"` per ADR-004.

**Soft failure**: if `JobSubmissionService` is unavailable or enqueueing fails, a warning is logged
but the analysis result is still returned to the caller. The `ScheduledRagIndexingService` provides
a catch-up backfill for any documents that were not indexed on analysis completion.

---

## Prerequisites (Must Be Registered Before This Task)

| Dependency | Where Registered | Notes |
|------------|-----------------|-------|
| `JobSubmissionService` | `Program.cs` line 645 | Already present |
| `RagIndexingJobHandler` | `Program.cs` line 664 | Already present |
| `IIdempotencyService` | Workers module | Via `AddWorkersModule` |
| `IFileIndexingService` | Workers module / Program.cs | Via `AddWorkersModule` |
| `RagTelemetry` | `Program.cs` line 605 | Already present |
