# BFF Publish-Size — R3 Task 023

> **Task**: Migrate `PlaybookSchedulerService` to `Spaarke.Scheduling`
> **Date**: 2026-06-21
> **Branch**: `work/spaarke-platform-foundations-r3`
> **Rule**: per `.claude/constraints/azure-deployment.md` BFF Publish-Size Per-Task Verification Rule (NFR-01)

## Measurement

| Metric | Value |
|---|---|
| Prior baseline (post-task 036) | **46.19 MB** compressed |
| Post-task 023 | **46.20 MB** compressed |
| **Delta** | **+0.01 MB** |
| Cumulative delta vs Phase 5 Outcome A baseline (45.65 MB) | +0.55 MB |
| NFR-01 ≤+1 MB per-task threshold | PASS |
| Single-task escalation threshold (+5 MB) | PASS (well under) |
| Architecture-review threshold (55 MB cumulative) | PASS |
| Hard ceiling (60 MB) | PASS (well under) |

## Method

```powershell
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish-task023/
Compress-Archive -Path deploy/api-publish-task023/* -DestinationPath deploy/api-publish-task023.zip -CompressionLevel Optimal
```

## Source of delta

**Net +0.01 MB**:

- **NEW**: `Services/Ai/PlaybookSchedulerJob.cs` (~470 lines including XML docs) — implements
  `IScheduledJob`; ports discovery, schedule parsing, due-check, and parallel user fan-out from
  the legacy `PlaybookSchedulerService` while adding fresh per-child correlationId tracking +
  `JobRunResult.ResultJson` payload serialization (FR-2.8 / Q1).
- **NEW** (Spaarke.Scheduling): one optional positional record param on `JobRunResult`
  (`string? ResultJson = null`) + matching param on `BackgroundJobRunRecord` + one-line
  flow in `InMemoryBackgroundJobStore.ToPublicProjection`. Zero new package dependencies; pure POCO.
- **MODIFIED**: `Infrastructure/DI/SchedulingModule.cs` — extended with hosted-service
  registration of `ScheduledJobHost` + seed of `notification-playbook-scheduler` BackgroundJobDefinition
  via a new internal `SchedulingBootstrapHostedService` (inserted at index 0 of hosted services so
  it runs before the cron loop's first tick).
- **MODIFIED**: `Infrastructure/DI/AnalysisServicesModule.cs` — `services.AddHostedService<PlaybookSchedulerService>()`
  REMOVED + replaced with a comment pointer to `SchedulingModule`. Net code lines: -1 effective.
- **DELETED**: `Services/PlaybookSchedulerService.cs` (~487 lines). Net publish-size impact partially
  offsets the new job class additions.

No new NuGet packages.

## Pre-merge checklist (bff-extensions.md §A)

- [x] Placement Justification stated in code XML doc (PlaybookSchedulerJob lives under `Services/Ai/`
  alongside its AI-internal dependencies per ADR-013)
- [x] ADRs cited: ADR-001 (in-process), ADR-010 (concrete singleton, IScheduledJob is framework seam),
  ADR-013 (AI placement), ADR-029 (publish-size measured), ADR-032 N/A (host is unconditional)
- [x] Publish-size measured (+0.01 MB, well under +1 MB ceiling)
- [x] No new HIGH-severity CVE (single pre-existing `Microsoft.Kiota.Abstractions 1.21.2` HIGH)
- [x] Test update obligation (FR-22 / D-05): +27 new `PlaybookSchedulerJobTests` + 5 relocated
  inline-notification tests in `InlineNotificationIntegrationPointsTests`
- [x] Asymmetric-registration §F.1: `PlaybookSchedulerJob` + `ScheduledJobHost` are BOTH registered
  unconditionally (no `if (flag) { … }` block). The hosted-service forwarder
  `services.AddHostedService(sp => sp.GetRequiredService<ScheduledJobHost>())` preserves the
  singleton identity so admin trigger (task 021) and cron loop share `_inFlight` state.
- [x] §F.1 static-scan: scope of asymmetric-registration anti-pattern checked — no new conditional
  service introduced. The only handler-resolution call site is
  `ScheduledJobRegistry.Resolve(jobId)` from `ScheduledJobHost`, which is unchanged.
- [x] BFF csproj NOT modified (no new package references)

## Test inventory

- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PlaybookSchedulerJobTests.cs` (NEW) — 27 tests:
  - Identity contract (3): JobId / DisplayName / Description / constructor null-guards
  - Constructor null-guards (3)
  - No-active-playbook fan-out (1): success + processedItems=0 + empty children
  - Seven-playbook fan-out cardinality (1)
  - Q1 unique-per-child correlationId (1)
  - Q1 child-id ≠ parent-id (1)
  - ResultJson records playbookId + correlationId per child (1)
  - Schedule due-check skip (1) + IsPlaybookDue theory (6) + never-run case (1)
  - Per-playbook failure isolation (1)
  - Per-user fan-out param flow (1) + user-count surface (1)
  - Cancellation: pre-loop (1), between-playbooks (1) → NFR-07 coverage
  - Last-run persistence (1) + persistence-failure graceful (1)
  - Schedule fallbacks: null config (1) + invalid config (1)
- `tests/unit/Sprk.Bff.Api.Tests/Services/InlineNotificationIntegrationPointsTests.cs` (NEW)
  — 5 tests: relocated from the deleted `PlaybookSchedulerServiceTests` "Inline Notification
  Integration Points Verification" region. Verifies `UploadEndpoints`, `AnalysisEndpoints`,
  `IncomingCommunicationProcessor`, `WorkAssignmentEndpoints` all reference `NotificationService`.
- `tests/unit/Sprk.Bff.Api.Tests/Services/PlaybookSchedulerServiceTests.cs` (DELETED) —
  all useful test coverage migrated to the two files above.

**Test results**: 32/32 new pass; full BFF unit suite 7458/7568 pass (110 pre-existing skipped,
0 failed) — no regressions from this task. Spaarke.Scheduling regression 57/57 pass (zero impact
from the optional `ResultJson` additions to `JobRunResult` / `BackgroundJobRunRecord`).
