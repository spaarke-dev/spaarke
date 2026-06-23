# BFF Publish-Size Measurement — Task 084 (2026-06-22)

> **Task**: `084-membership-junction-updater-handler.poml`
> **Scope**: Add `MembershipJunctionUpdater` + `MembershipJunctionUpdaterHost`
> + `NullMembershipJunctionUpdaterHost` + `IMembershipJunctionUpdater` +
> `MembershipJunctionUpdaterOptions`; extend `MembershipModule` DI;
> append `Membership:JunctionUpdater` to the dev appsettings template.

## Measurement

| Metric | Value |
|---|---|
| Compressed publish size | **46.22 MB** |
| Prior baseline (task 081) | 46.21 MB |
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

This task added **5 source files** to `Services/Ai/Membership/` (interface,
real updater, real host, Null host, options) + **1 test file** + **DI module
extension** + **0 new NuGet packages** (Azure.Messaging.ServiceBus +
Azure.Identity already present from task 081's publisher work + the existing
JobProcessingModule). The +0.01 MB delta is publish-environment noise (release
determinism + zip compression alignment).

## CVE

```
Microsoft.Kiota.Abstractions 1.21.2 — HIGH (GHSA-7j59-v9qr-6fq9)
```

This is a **pre-existing transitive dependency** documented in 14+ prior R3
task notes (most recently task 072, task 081). **No new HIGH CVE** introduced
by this task.

## Notes

- Real Service Bus consumer host is feature-gated via ADR-032 Null-Object
  hosted-service-peer pattern. Default `Membership:JunctionUpdater:Enabled=false`
  registers `NullMembershipJunctionUpdaterHost` — no Service Bus client
  construction at startup. Operator flips the flag (and populates
  `ServiceBusNamespace`) after deploying task 071's topic.
- `IMembershipJunctionUpdater` is registered Scoped (matches `IDataverseService`
  lifetime) and is reused directly by task 085's `MembershipReconciliationJob`
  through the same contract.
- NFR-07 30-second drain cap on `StopAsync` is locked at the type-system
  level via `MembershipJunctionUpdaterHost.DrainTimeout` (TimeSpan constant)
  + a dedicated unit test asserts the value.
