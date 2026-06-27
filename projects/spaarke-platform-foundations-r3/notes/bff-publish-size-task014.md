# BFF Publish-Size Measurement — R3 Task 014

> **Task**: 014 — Retry/backoff + idempotency in `ScheduledJobHost`
> **Date**: 2026-06-21
> **Per**: CLAUDE.md §10 BFF Hygiene + `.claude/constraints/azure-deployment.md` NFR-01 verification rule

---

## Inputs

| Field | Value |
|---|---|
| Build | `dotnet build Spaarke.sln` -> **succeeded** (0 errors; 16 pre-existing BFF warnings; 0 from `Spaarke.Scheduling`) |
| Publish command | `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` |
| Configuration | Release / linux-x64 / framework-dependent |
| Compression method | PowerShell `Compress-Archive` (matches deploy ZIP convention) |

---

## Measurements

| Metric | Value |
|---|---|
| Compressed publish ZIP | **46.16 MB** (48,397,039 bytes) |
| Publish entry count | 264 files |
| Baseline (task 013, 2026-06-21) | 46.14 MB |
| **Delta vs baseline** | **+0.02 MB (~16 KB)** |
| NFR-01 per-task ceiling | +1 MB |
| Hard ceiling | 60 MB |
| Status | OK **Within budget** (2% of per-task ceiling) |

### Per-asset contribution

| Asset | Size impact | Notes |
|---|---|---|
| `Spaarke.Scheduling.dll` | +~12 KB IL | New types: `JobRetryPolicy`; extended types: `IBackgroundJobStore` (added `HasRunForScheduledTimeAsync` + `scheduledFireUtc` parameter on `RecordRunStartAsync`), `InMemoryBackgroundJobStore` (matching impl + `SeedRunRecord` test surface), `ScheduledJobHost` (`ExecuteWithRetryAsync` + idempotency probe in `RunJobAsync`), `ScheduledJobHostOptions` (`RetryPolicy` property). |
| NuGet additions | 0 | No new packages — retry policy is in-house POCO, intentionally NOT Polly per the design rationale in `JobRetryPolicy.cs` XML doc. |

The lib is still not wired into BFF DI (task 023 PlaybookSchedulerService migration is the first registration); the dll is published because of the project-graph reference added in task 010.

---

## Vulnerability scan

```
dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable --include-transitive
```

| Severity | Package | Advisory | Status |
|---|---|---|---|
| High | `Microsoft.Kiota.Abstractions 1.21.2` | GHSA-7j59-v9qr-6fq9 | **Pre-existing** — unchanged from task 010 / 012 / 013 measurements. |

Result: **no NEW HIGH CVE introduced by this task**.

---

## Conclusion

OK **Within NFR-01 budget.** Task 014 adds retry-with-exponential-backoff + restart-idempotency to `ScheduledJobHost`. Zero new NuGet packages. Publish-size impact is +16 KB compressed IL. No new HIGH CVE.

The 46.16 MB number becomes the new baseline for task 015 (`sprk_backgroundjob` Dataverse entity, which is schema-only and SHOULD have zero size impact).
