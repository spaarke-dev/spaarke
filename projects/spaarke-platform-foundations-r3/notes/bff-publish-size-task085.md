# BFF Publish-Size Measurement — Task 085 (2026-06-22)

> **Task**: `085-membership-reconciliation-job-real-logic.poml`
> **Scope**: Add `MembershipReconciliationJob` (`IScheduledJob` — real-logic
> nightly reconciliation per FR-2P2.7) + `MembershipReconciliationOptions`;
> extend `MembershipModule` DI with the job singleton + IScheduledJob
> forwarding + a startup bootstrap hosted service that seeds the
> `membership-reconciliation` BackgroundJobDefinition row + registers the
> handler with `ScheduledJobRegistry`.

## Measurement

| Metric | Value |
|---|---|
| Compressed publish size | **46.23 MB** |
| Prior baseline (task 084) | 46.22 MB |
| Delta | **+0.01 MB** (rounding noise) |
| NFR-01 ceiling (per task) | +1 MB |
| NFR-01 hard ceiling (cumulative) | 60 MB |
| Within budget | **YES** |

## Command

```pwsh
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
Compress-Archive -Path deploy/api-publish/* -DestinationPath deploy/api-publish.zip -Force
(Get-Item deploy/api-publish.zip).Length / 1MB
```

## Rationale for delta

This task added **2 source files** to `Services/Ai/Membership/`
(`MembershipReconciliationJob.cs` + `MembershipReconciliationOptions.cs`) +
**1 test file** + **DI module extension** (recon job registration + bootstrap
hosted service inside `MembershipModule`) + **0 new NuGet packages**. All
transitive deps were already present from prior tasks
(`Microsoft.Xrm.Sdk` via Spaarke.Dataverse, `Spaarke.Scheduling`,
`Microsoft.Extensions.Options`). The +0.01 MB delta is publish-environment
noise (release determinism + zip compression alignment).

## CVE

```
Microsoft.Kiota.Abstractions 1.21.2 — HIGH (GHSA-7j59-v9qr-6fq9)
```

This is a **pre-existing transitive dependency** documented in 14+ prior R3
task notes (most recently task 084). **No new HIGH CVE** introduced by this
task.

## Notes

- The recon job is INDEPENDENT of the Service Bus topic deploy gate
  (task 071). It writes the junction directly via
  `IMembershipJunctionUpdater` (task 084's handler) and does NOT publish to
  the topic — so it ships enabled-by-default and provides the 24h-max-
  staleness backstop for maker-portal-only mutation paths
  (`sprk_assigned*`, `sprk_task`, `sprk_opportunity` per event-source
  inventory §3A / §3D / §3E).
- Lifetime pattern: Singleton + `IServiceScopeFactory.CreateScope()` per
  `ExecuteAsync`, matching PlaybookSchedulerJob (task 023). The
  `IMembershipJunctionUpdater` is Scoped (per task 084's registration); the
  recon job's per-tick scope keeps lifetime semantics correct without
  forcing the handler to be Singleton.
- Algorithm: discover → fetch parents (paginated, NotNull-OR filter) →
  dispatch Updated events for every populated lookup (handler's idempotent
  Update-or-Create self-heals missing rows) → scan junction for orphans
  (no longer present in expected-set) → dispatch Removed events.
- Cron schedule: `0 2 * * *` (02:00 UTC daily) by default — configurable
  via `Membership:Reconciliation:CronSchedule`. Honors NFR-07 30s drain on
  host shutdown.
- 27 unit tests pass (full coverage of discovery, parent dispatch, orphan
  dispatch, idempotency, cancellation, per-row error tolerance, metadata
  surface).
- Full BFF test sweep: 7557 passed, 110 skipped, 0 failed.
