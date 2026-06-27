# Task 028a — BFF Publish-Size Delta

> **Status**: ✅ Within NFR-01
> **Baseline reference**: 46.28 MB compressed (W1 measurement 2026-06-24)

## Measurement

| Metric | Value |
|---|---|
| **Compressed publish size (post-028a)** | **46.28 MB** |
| Baseline (W1 measurement) | 46.28 MB |
| **Delta** | **+0.00 MB** (below 10 KB threshold visible at 2-decimal MB precision) |
| NFR-01 ceiling | 60.00 MB |
| Headroom | 13.72 MB |

## Why ~zero delta?

028a added ~3 small C# files (interface ~110 lines, impl ~280 lines, module ~30 lines) plus the matchconditions JSON schema (doc-only, not in publish output) and a single Program.cs `AddRoutingModule()` line. No new NuGet packages; no new transitive dependencies. The compressed delta is below the 10 KB threshold visible at 2-decimal MB precision.

## ADR-029 / NFR-01 status

- ✅ Well under +5 MB single-task threshold
- ✅ Cumulative 46.28 MB well under 55 MB architecture-review trigger
- ✅ No `<PublishTrimmed>` / `<PublishAot>` introduced
- ✅ No new package references

## Method

```powershell
Remove-Item -Recurse -Force src/server/api/Sprk.Bff.Api/publish -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force deploy/api-publish -ErrorAction SilentlyContinue
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
Compress-Archive -Path "deploy/api-publish/*" -DestinationPath "deploy/api-publish-028a.zip" -Force
(Get-Item "deploy/api-publish-028a.zip").Length / 1MB
```

## Forward-looking

Phase 1R tasks 028c (4 consumer migrations) + 028d (2 consumer migrations) will each modify existing service files without adding new types — expected delta also ~zero. The cumulative Phase 1R footprint should remain well under +1 MB.
