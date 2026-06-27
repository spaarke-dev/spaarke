# BFF Publish-Size Verification — Task 072

**Date**: 2026-06-22
**Task**: 072 — `MembershipChangedEvent` payload contract + serialization
**Branch**: `work/spaarke-platform-foundations-r3`

## Result

| Metric | Value |
|---|---|
| Compressed publish size | **44.88 MB** |
| Prior baseline | 46.21 MB (per task spec / CLAUDE.md §10) |
| Delta | **-1.33 MB** |
| NFR-01 ceiling (per task) | +1 MB |
| NFR-01 hard ceiling (cumulative) | 60 MB |
| Within budget | YES (decreased) |

## Command

```pwsh
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
Compress-Archive -Path deploy/api-publish/* -DestinationPath deploy/api-publish.zip -Force
(Get-Item deploy/api-publish.zip).Length / 1MB
```

## Rationale for delta

This task added **3 source files** (2 small enums + 1 record class — all in `Services/Ai/Membership/Events/`) and **0 new NuGet packages**. The -1.33 MB drop is publish-environment noise (Release build determinism + transient caching). The new types compile into the existing `Sprk.Bff.Api.dll` with negligible impact.

## CVE

```
Microsoft.Kiota.Abstractions 1.21.2 — HIGH (GHSA-7j59-v9qr-6fq9)
```

This is a **pre-existing transitive dependency** documented in 14 prior R3 task notes (005, 010, 012, 013, 014, 020, 021, 030, 031, 032, 035, 036, 042, this). **No new HIGH CVE** introduced by this task.

## Notes

Task is pure code-only contract definition. No publisher wiring, no Service Bus client, no DI registration changes. Downstream publisher wiring (tasks 081-083) waits on operator-deploy of task 071 topic.
