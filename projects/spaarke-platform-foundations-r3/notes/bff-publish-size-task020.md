# BFF Publish Size — Task 020 (Admin Jobs List + Status Endpoints)

> **Task**: `020-admin-jobs-list-status-endpoints.poml`
> **Date**: 2026-06-21
> **Rule**: per `.claude/constraints/azure-deployment.md` BFF Publish-Size Per-Task Verification Rule (NFR-01)

## Measurement

```
dotnet publish -c Release src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -o deploy/api-publish-020
Compress-Archive -Path deploy/api-publish-020/* -DestinationPath deploy/api-publish-020.zip -Force
```

| Metric | Value |
|---|---|
| Compressed size (zip) | **46.18 MB** (48,426,604 bytes) |
| Baseline (task 031 / 034) | 46.16 MB |
| Delta vs baseline | **+0.02 MB** (≈ 20 KB) |
| Per-task threshold | +5 MB (HIGH) / +1 MB (spec NFR-01) |
| Cumulative ceiling | 60 MB HARD STOP |
| Headroom remaining | 13.82 MB |

## What contributed to the delta

- `Api/Admin/JobsEndpoints.cs` (~280 lines including doc comments)
- `Api/Admin/Models/JobStatusSummary.cs` + `JobStatusDetail.cs` + `JobRunDetail.cs` (records, ~70 lines combined)
- `Infrastructure/DI/SchedulingModule.cs` (~50 lines)
- Edits: `IBackgroundJobStore.cs` (+GetRecentRunsAsync + BackgroundJobRunRecord record),
  `InMemoryBackgroundJobStore.cs` (+~50 lines impl + projection), `Program.cs` (+1 call),
  `EndpointMappingExtensions.cs` (+1 call)

## NuGet additions

**None.** Cronos 0.13.0 is already pulled by `Spaarke.Scheduling` (task 010). Reusing.

## CVE check

```
dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable --include-transitive
```

| Package | Severity | Status |
|---|---|---|
| Microsoft.Kiota.Abstractions 1.21.2 | High | **Pre-existing** (GHSA-7j59-v9qr-6fq9) — tracked at project level, not introduced by task 020 |

No new HIGH-severity CVEs introduced.

## bff-extensions.md §A pre-merge checklist

| # | Requirement | Status |
|---|---|---|
| A.1 | Placement decision stated (in/outside BFF) | ✅ — admin inspection of in-process Spaarke.Scheduling registry; MUST live in BFF (the registry is in-process; no cross-service network call possible). |
| A.2 | ADRs cited | ✅ — ADR-001 (Minimal API + BackgroundService), ADR-008 (endpoint-filter auth, not global middleware), ADR-010 (concretes; `IBackgroundJobStore` justified by ≥2 impls). |
| A.3 | Publish size verified | ✅ — 46.18 MB (delta +0.02 MB). |
| A.4 | No new CRUD→AI direct dep | ✅ — endpoints inject only Spaarke.Scheduling abstractions + ILogger. |
| A.5 | Feature-module DI | ✅ — `SchedulingModule.AddSchedulingModule()` (unconditional per §F.1). |

## §F.1 asymmetric-registration compliance

The `/api/admin/jobs/*` endpoints map UNCONDITIONALLY (in `EndpointMappingExtensions.cs`).
`SchedulingModule.AddSchedulingModule()` registers `ScheduledJobRegistry` +
`InMemoryBackgroundJobStore` (as `IBackgroundJobStore`) UNCONDITIONALLY — no feature flag,
no `if (flag) { ... }` block. Compliant with the §F.1 rule + ADR-032 N/A here (the services
are real services, not kill-switched null-object placeholders).

## Tests

11 new tests in `tests/unit/Sprk.Bff.Api.Tests/Api/Admin/JobsEndpointsTests.cs`:

- **Auth (AC-2.5)**: 401 unauthenticated × 2 endpoints, 403 non-admin × 2 endpoints
- **List**: empty registry → empty list, seeded job appears with status, orphan-handler shows
  Enabled=false + empty cron, last-run status surfaces newest run
- **Detail**: 200 with 10-run cap (12 seeded → 10 returned, newest-first), 404 unknown jobId,
  failed-run ErrorMessage surfaces via RecentRuns[0] (AC-2.7)

All 11/11 pass (133ms). Spaarke.Scheduling regression: 42/42 still pass.
