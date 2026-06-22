# BFF Publish Size — Task 042 (Canvas-Server Mapping Update)

> **Task**: `042-canvas-server-mapping-update.poml`
> **Date**: 2026-06-21
> **Rule**: per `.claude/constraints/azure-deployment.md` BFF Publish-Size Per-Task Verification Rule (NFR-01)

## Measurement

```
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish-task042/
Compress-Archive -Path deploy/api-publish-task042/* -DestinationPath deploy/api-publish-task042.zip -CompressionLevel Optimal
```

| Metric | Value |
|---|---|
| Compressed (Optimal zip) | **46.21 MB** |
| Prior baseline (task 036) | 46.19 MB |
| **Delta vs prior task** | **+0.02 MB** |
| Cumulative delta vs Phase 5 Outcome A baseline (45.65 MB) | +0.56 MB |
| NFR-01 +/-1 MB per-task threshold | PASS |
| Hard ceiling 60 MB | PASS (well under) |

## Source of delta

Task 042 modified ONE existing server file (no new files, no new NuGet packages):

**Modified**
- `src/server/api/Sprk.Bff.Api/Services/Ai/NodeService.cs` — added `"lookupUserMembership"` arm to BOTH `MapCanvasTypeToNodeType` (-> Workflow) and `MapCanvasTypeToActionType` (-> ActionType.LookupUserMembership). Two switch-expression additions, +~6 lines of IL total.

The +0.02 MB delta sits inside zip-tool variance noise; effectively zero growth.

## CVE check

```
dotnet list src/server/api/Sprk.Bff.Api/ package --vulnerable --include-transitive
```

| Package | Severity | Source | Introduced by task 042? |
|---|---|---|---|
| `Microsoft.Kiota.Abstractions 1.21.2` | High | GHSA-7j59-v9qr-6fq9 | No -- pre-existing across all tasks 013/020/030/031/035/036 |

**No new HIGH-severity CVE introduced by task 042.**

## Pre-merge checklist (bff-extensions.md Section A)

- [x] **Placement justification**: Modification stays inside `Services/Ai/NodeService.cs` where the existing canvas-string -> Dataverse-mapping table lives. No new endpoint, service, or DI registration. Mirrors the existing `"createTask" => ActionType.CreateTask` pattern verbatim per Q5 owner directive (extend, do not invent).
- [x] **ADR citations**: ADR-013 (extend existing AI/node-executor framework -- no new pattern), ADR-010 (no new interface, no new DI registration -- modifies existing concrete service in place). Pairs with task 041's `LookupUserMembershipNodeExecutor` (ADR-010 Singleton-with-Scoped via `IServiceScopeFactory`).
- [x] No new direct package references (NuGet graph unchanged).
- [x] No new CRUD->AI dependency (the change lives entirely inside `Services/Ai/` -- the inbound side is unaffected).
- [x] Feature-module DI: no new DI registrations. The two switch expressions are static helpers on a class that already gets registered.
- [x] No new HIGH-severity CVE introduced.
- [x] **Test update obligation (Section F)**: No existing tests reference `MapCanvasTypeToNodeType` / `MapCanvasTypeToActionType` (they are `private static` helpers reached only via `SyncCanvasToNodesAsync`). The `NodeServiceTests` suite (32 tests) covers the public surface and passes unchanged. The drift integration test is the canonical coverage and is filed as task 065 in the index per project plan; deferring per the spec's H3 workstream structure rather than back-filling now.
- [x] **Asymmetric-registration check (Section F.1)**: N/A -- no DI changes; no `if (flag) { ... }` block introduced.
- [x] **Fixture-config-FIRST (Section F.2)**: N/A -- existing tests still pass.
- [x] **Empirical-reproduction-FIRST (Section F.3)**: N/A -- no ledger entry consulted.

## AC verification

- **FR-1B.3** (Canvas can emit `lookupUserMembership` nodes; server maps them to `ActionType.LookupUserMembership`) -- verified: server-side switch returns `ActionType.LookupUserMembership` for canvas type `"lookupUserMembership"`; client `playbookNodeSync.buildConfigJson` emits `__actionType: 52` plus the three config fields (entityType, roles, includeRelated).
- **Spec FR-3H3.1** (canvas-server drift test will catch missed mapping) -- the drift integration test in task 065 will validate this once written. Spot-checked: client `ActionType.LookupUserMembership = 52` matches server `ActionType.LookupUserMembership = 52` in `INodeExecutor.cs`; canvas string `"lookupUserMembership"` is consumed by both sides.

## Test outcome

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~NodeService" --no-build
Passed! - Failed: 0, Passed: 32, Skipped: 0, Total: 32, Duration: 22 ms
```
