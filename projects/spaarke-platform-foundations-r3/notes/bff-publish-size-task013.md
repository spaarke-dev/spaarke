# BFF Publish-Size Measurement — R3 Task 013

> **Task**: 013 — `ScheduledJobHost : BackgroundService` (cron dispatch + run-record write)
> **Date**: 2026-06-21
> **Per**: CLAUDE.md §10 BFF Hygiene + `.claude/constraints/azure-deployment.md` NFR-01 verification rule

---

## Inputs

| Field | Value |
|---|---|
| Build | `dotnet build Spaarke.sln` → **succeeded** (0 errors; 18 pre-existing warnings in BFF, none from `Spaarke.Scheduling`) |
| Publish command | `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` |
| Configuration | Release / linux-x64 / framework-dependent |
| Compression method | PowerShell `Compress-Archive` (matches deploy ZIP convention) |

---

## Measurements

| Metric | Value |
|---|---|
| Compressed publish ZIP | **46.14 MB** (48,381,338 bytes) |
| Publish entry count | 264 files |
| Baseline (task 012, 2026-06-21) | 46.13 MB |
| **Delta vs baseline** | **+0.01 MB (~10 KB)** |
| NFR-01 per-task ceiling | +1 MB |
| Hard ceiling | 60 MB |
| Status | ✅ **Within budget** (1% of per-task ceiling) |

### Per-asset contribution

| Asset | Size impact | Notes |
|---|---|---|
| `Spaarke.Scheduling.dll` (already referenced from task 010) | new types added: `ScheduledJobHost`, `ScheduledJobRegistry`, `ScheduledJobHostOptions`, `IBackgroundJobStore`, `InMemoryBackgroundJobStore`, `BackgroundJobDefinition` | ~6 KB IL; the project ref + Cronos package were already paid for in task 010. |
| `Cronos.dll` | unchanged | Brought in by task 010; already in publish output. |
| `Microsoft.Extensions.Hosting.Abstractions.dll` | unchanged | Already part of BFF dependency closure. |

No new NuGet packages added in this task. `Sprk.Bff.Api.csproj` unchanged. The `Spaarke.Scheduling` library is not yet wired into BFF DI (task 023 PlaybookSchedulerService migration will be the first registration); the dll is published because it sits in the same `Spaarke.sln` graph via the project reference added in task 010.

---

## Vulnerability scan

```
dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable --include-transitive
```

| Severity | Package | Advisory | Status |
|---|---|---|---|
| High | `Microsoft.Kiota.Abstractions 1.21.2` | GHSA-7j59-v9qr-6fq9 | **Pre-existing** — already present in task 010 + task 012 measurements; not introduced by this task. |

Result: **no NEW HIGH CVE introduced by this task**.

- The single pre-existing HIGH advisory is unchanged from prior task measurements.
- `Spaarke.Scheduling` introduces no transitive NuGet dependencies beyond what task 010 already added (Cronos 0.13.0, `Microsoft.Extensions.Hosting.Abstractions` 8.0.0, `Microsoft.Extensions.Logging.Abstractions` 8.0.3, `Microsoft.Extensions.Options` 8.0.2 — all already in BFF closure).

---

## Conclusion

✅ **Within NFR-01 budget.** Task 013 ships the `ScheduledJobHost` + persistence abstraction + in-memory store. No new packages. Publish-size impact is +10 KB (compiled IL for the new types). No new HIGH CVE.

The 46.14 MB number becomes the new baseline for task 014 (retry/backoff + idempotency).
