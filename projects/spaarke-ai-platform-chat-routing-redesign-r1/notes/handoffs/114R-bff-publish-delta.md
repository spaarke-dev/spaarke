# Task 114R — BFF Publish-Size Delta

**Date**: 2026-06-25
**Task**: 114R — `NodeType.DeliverComposite` extension to `PlaybookExecutionEngine` (FR-52)
**ADR**: ADR-029 (BFF Publish Hygiene) + spec NFR-01 (≤60 MB compressed ceiling)

## Measurement

| Snapshot | Compressed size | Notes |
|----------|-----------------|-------|
| Task 115 baseline (from `current-task.md` line 18) | 47.91 MB | Prior task in same project lineage |
| **Task 114R post-change** | **47.92 MB** | Measured via `dotnet publish -c Release` + `tar -caf` |
| **Delta vs 115** | **+0.01 MB** | Negligible — within measurement noise |
| Headroom vs NFR-01 ceiling (60 MB) | ~12.08 MB | No escalation needed |

## What changed (publish surface)

- **+1 file**: `Services/Ai/Nodes/DeliverCompositeNodeExecutor.cs` (~360 lines C#, ~10 KB IL)
- **2 enum additions**: `NodeType.DeliverComposite` (= 100_000_004) + `ActionType.DeliverComposite` (= 42) in existing `INodeExecutor.cs`
- **3 small edits**: `PlaybookOrchestrationService.cs` (added one switch arm), `NodeService.cs` (added two canvas-type switch arms), `Infrastructure/DI/AnalysisServicesModule.cs` (one new `AddSingleton`)
- **No new NuGet packages**, no new transitive deps, no new file types in the assembly footprint

## Verdict

✅ **Within budget**. No new NuGet additions, no new transitive deps. The +0.01 MB delta is within measurement noise (compression jitter on identical-looking output sets). No escalation per CLAUDE.md §10 thresholds.

## Procedure used

```bash
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish-114R/
cd deploy && tar -caf api-publish-114R.tar.gz -C api-publish-114R .
ls -la api-publish-114R.tar.gz | awk '{printf "%.2f MB\n", $5/1048576}'
```
