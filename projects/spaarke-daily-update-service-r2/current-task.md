# Current Task State - Spaarke Daily Update Service R2

> **Last Updated**: 2026-06-23 18:30 UTC (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first
> **Branch**: `master` (⚠ uncommitted BFF changes present — see "Files Modified" + "Critical Context")

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Daily Briefing producer pipeline — 6 bugs diagnosed in series; correct architectural fix identified (refactor to `Spaarke.Dataverse.IGenericEntityService`); audit complete; about to start refactor |
| **Step** | After audit, before refactor. User asked for /compact at this boundary. |
| **Status** | blocked-on-compact-then-refactor |
| **Next Action** | After /compact: refactor 3 broken node executors (`QueryDataverseNodeExecutor`, `CreateNotificationNodeExecutor`, `CreateTaskNodeExecutor`) to inject `Spaarke.Dataverse.IGenericEntityService` instead of the orphan-named `IHttpClientFactory.CreateClient("DataverseApi")`. Then build + zipdeploy + force-tick + verify appnotifications appear for Ralph. Per user principle: "correct, not fastest" — also flag/refactor the 4 OTHER services with same antipattern (EmailTemplateService, BulkRagIndexingJobHandler, SessionRestoreService, EmailAssociationService) as follow-up. |
| **BFF runtime state** | Diag-instrumented build deployed (hash matches), with `AnalysisActionService.cs:33` SELECT fix included. App Insights captures `[DBG-DAILY]` markers. App-only path enters `ExecuteNodeAsync` then fails on HttpClient missing BaseAddress. |
| **Dataverse state** | All Phase A fetchXml patches still live (Ch.1–5). All R2.2 BFF + code-page changes still live. Plus today's wiring patches (see "Live Dataverse mutations" below). |

### Files Modified This Session (UNCOMMITTED ⚠)

- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisActionService.cs` — **Real fix.** Removed `sprk_actiontype` from the `$select` clause (line 33). That field doesn't exist on the `sprk_analysisaction` entity (a 2026-06-02 comment in the same file documented the empirical confirmation but the SELECT was never updated). Including it caused ALL `GetActionAsync` calls to return HTTP 400 → broke Insights AND Daily Briefing AND any future ActionId-based dispatch.
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs` — **Diagnostic instrumentation.** Added 6 `[DBG-DAILY]` `LogWarning` markers at: AppOnlyInternal entry/post-GetNodesAsync/post-ExecuteNodeBasedModeAsync, NodeBased entry/post-graph-build/post-batches/per-batch, ExecuteNodeAsync entry/executor-lookup/calling-executor/executor-returned. Decision pending: leave as `LogInformation` (permanent observability) or revert.

### Live Dataverse mutations this session (all on spaarkedev1)

**10 sprk_playbooknode records PATCHed**:
- `sprk_isactive = true` on all 20 nodes across 5 in-scope playbooks (Ch.1–5). Previously all 20 were `false` — `ExecutionGraph.IsActive` filter dropped them → `totalNodes=0` → no work.
- `__actionType: 33` added to configjson of 5 Start nodes — triggers `isStartNode` passthrough in orchestrator line 1023.
- `_sprk_actionid_value` (FK) set on 5 Query nodes → SYS-QUERY-DV Action.
- `_sprk_actionid_value` (FK) set on 5 Notify nodes → SYS-CREATE-NOTIF Action.

**2 sprk_analysisaction rows CREATED**:
- `ef7747ca-2b6f-f111-ab0e-7ced8ddc4cc6` SYS-QUERY-DV (executoractiontype=51 via lookup)
- `f97747ca-2b6f-f111-ab0e-7ced8ddc4cc6` SYS-CREATE-NOTIF (executoractiontype=50 via lookup)

**1 sprk_analysisactiontype row CREATED**:
- `6fb58650-2e6f-f111-ab0e-70a8a590c51c` "50 - Create Notification" (executor=50). The registry already had "51 - Query Dataverse" (`f9dd8bf5-4865-f111-ab0c-7ced8ddc4a05`).

### Critical Context

**Daily Briefing producer is STILL not creating notifications**, but we've eliminated 5 of 6 known bugs and pivoted to the correct architectural fix instead of a parallel-implementation bypass.

**The 6 bugs found in series** (each fix revealed the next):

| # | Bug | Layer | Status |
|---|---|---|---|
| 1 | `sprk_isactive=false` on all 10 deployed nodes → ExecutionGraph filtered them out → totalNodes=0 | Data | ✅ Fixed via PATCH |
| 2 | NodeType=AIAnalysis nodes had no ActionId → "AI node requires Action" error | Data + Arch | ✅ Fixed via PATCH (created SYS Actions + wired FKs) |
| 3 | Start node configjson missing `__actionType` → fell to Control→Condition fallback → "Condition expression required" error | Data | ✅ Fixed via PATCH |
| 4 | BFF `AnalysisActionService.GetActionAsync` SELECTs `sprk_actiontype` field that doesn't exist on entity → ALL Action lookups return 400 | Code | ✅ Fixed (uncommitted, deployed) |
| 5 | Missing `sprk_analysisactiontype` registry row for executor=50 (CreateNotification) | Data | ✅ Fixed via CREATE |
| 6 | `QueryDataverseNodeExecutor` / `CreateNotificationNodeExecutor` / `CreateTaskNodeExecutor` use `IHttpClientFactory.CreateClient("DataverseApi")` — **named client NEVER registered in DI** → returns default HttpClient with no BaseAddress + no auth handler → "invalid request URI" | Code (architectural) | ❌ NOT FIXED — this is the next refactor |

**Audit found 4 ADDITIONAL services with the same antipattern** (different orphan names, all unregistered):
- `EmailTemplateService` → `"Dataverse"` (2 sites)
- `BulkRagIndexingJobHandler` → `"DataverseBatch"`
- `SessionRestoreService` → `"DataverseETagCheck"`
- `EmailAssociationService` → `"DataverseAssociation"`

Plus `"DataversePolling"` (EmailServicesModule:50) is registered-but-bare. So **7 services total** have this defect class.

**The architectural alignment**: The codebase already has the correct pattern: `Spaarke.Dataverse.IGenericEntityService` (shared library, registered via `GraphModule.cs:71` as composite forwarding). Used correctly by 20+ services including `PlaybookSchedulerService` itself (line 217 calls `entityService.RetrieveMultipleAsync(query, ct)`). It handles BaseAddress + TokenCredential + Bearer auth + token refresh automatically. The runtime impl is `DataverseServiceClientImpl` which supports `FetchExpression` (the SDK type for FetchXml).

**Method shapes the 3 node executors need**:
```csharp
Task<EntityCollection> RetrieveMultipleAsync(FetchExpression fetch, CancellationToken ct);  // for fetchXml queries
Task<Guid> CreateAsync(Entity entity, CancellationToken ct);  // for appnotification + task creation
```

**User's explicit principle (load-bearing for any future decision)**: "We MUST always take the correct technical approach not the fastest/least effort approach." Earlier I made the mistake of recommending a parallel BFF endpoint (bypass). User correctly rejected that as architectural drift. Confirmed approach: refactor the broken executors to use the existing shared library — slower but architecturally aligned.

---

## Full State (Detailed)

### Session arc (chronological)

1. Resumed from prior session (Phase A + Phase B already shipped: Channels 1–5 fetchXml patches live, TTL=7d live, bulk-dismiss fix live).
2. User reported Daily Briefing showing zero items despite having qualifying events as owner.
3. Diagnosed producer pipeline through cascading bugs (the 6 above).
4. Briefly fell back to "bypass" recommendation after frustration; user correctly pushed back on architectural shortcuts.
5. Identified `Spaarke.Dataverse.IGenericEntityService` as the canonical shared pattern.
6. Audit confirmed 7 services across BFF use the broken named-client antipattern.
7. About to start refactor; user requested /context-handoff first.

### Key code paths to know

- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs:168` — `ExecuteAppOnlyInternalAsync`, app-only entry, currently has DBG-DAILY markers
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs:635` — `ExecuteNodeBasedModeAsync` (batched parallel)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs:885` — `ExecuteNodeAsync` (per-node dispatch, executor lookup, Validate, ExecuteAsync)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs:1023` — `isStartNode` early-return passthrough check
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs:1067` — `if (node.ActionId != Guid.Empty)` Action-FK branch
- `src/server/api/Sprk.Bff.Api/Services/Ai/ExecutionGraph.cs:?` — constructor filters `nodes.Where(n => n.IsActive)`
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisActionService.cs:33` — the SELECT fix (uncommitted)
- `src/server/api/Sprk.Bff.Api/Services/Ai/DataverseHttpServiceBase.cs` — pattern used by AnalysisActionService etc. (constructor sets BaseAddress + headers, EnsureAuthenticatedAsync attaches Bearer)
- `src/server/shared/Spaarke.Dataverse/IGenericEntityService.cs` — **canonical Dataverse access interface; refactor target**
- `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` — runtime impl (handles FetchExpression)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/GraphModule.cs:71` — `IGenericEntityService` registration (composite forwarding to `IDataverseService`)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` — node executor registrations (need to add `IGenericEntityService` dep when refactoring)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/UpdateRecordNodeExecutor.cs` — **reference implementation** — the ONE node executor that correctly uses `IGenericEntityService`. Mirror this pattern when refactoring the 3 broken ones.

### Resumption protocol (do this on /compact resume)

1. **Read this Quick Recovery section** (you are reading it now)
2. **Check uncommitted state**: `git status --short` — should show 2 modified files (`AnalysisActionService.cs`, `PlaybookOrchestrationService.cs`). If clean, the changes got committed or reverted; investigate before proceeding.
3. **Confirm branch**: `git branch --show-current` — should be `master` (we did NOT branch off; uncommitted changes are on master).
4. **Decide on commit strategy**: 
    - Option A: branch off + commit before refactor (cleaner history)
    - Option B: stash, do refactor on master, commit everything together
    - Recommend Option A.
5. **Read `UpdateRecordNodeExecutor.cs`** (the reference implementation) to internalize the correct pattern.
6. **Refactor in this order**:
   - `QueryDataverseNodeExecutor` — uses `RetrieveMultipleAsync(FetchExpression)` — the most critical for Daily Briefing
   - `CreateNotificationNodeExecutor` — uses `CreateAsync(Entity)` — also critical for Daily Briefing
   - `CreateTaskNodeExecutor` — uses `CreateAsync(Entity)` — same pattern
7. **Update DI** in `AnalysisServicesModule.cs` — these executors are registered as singletons; need to verify they can take `IGenericEntityService` dep (which may be scoped — confirm).
8. **Build + zipdeploy** (use stop → kudu zipdeploy → start cycle to bypass file locks).
9. **Force-tick** (NULL `sprk_lastrundate` on 5 playbooks + restart). Verify `appnotification` records appear for Ralph in spaarkedev1.
10. **If green**: revert diag logging from orchestrator + redeploy clean. Commit + PR.
11. **Follow-up backlog**: 4 other services with same antipattern — same refactor pattern.

### Verification queries

- App Insights for orchestrator behavior: `traces | where timestamp > ago(15m) | where message contains "DBG-DAILY" or message contains "Playbook" | order by timestamp`
- Appnotifications: `appnotifications?$filter=createdon ge {ISO-timestamp}&$select=title,createdon,ttlinseconds,partitionid&$orderby=createdon desc&$top=30`
- Playbook last-run dates: `sprk_analysisplaybooks?$filter=sprk_playbooktype eq 2 and statecode eq 0&$select=sprk_name,sprk_lastrundate`

### Deferred (do NOT lose sight of)

- Channel 6 (Matter/Project Activity) and Channel 7 (Work Assignments) — design captured in `notes/channel-7-work-assignments-design.md`.
- The 4 other services with broken named-client pattern — should be refactored in same wave to prevent future bug reports.
- "DataversePolling" bare registration in `EmailServicesModule.cs:50` — needs proper config or migration to shared lib.
- Decision: should `_sprk_actionid_value` always be required on every node (eliminate the structural-NodeType fallback)? Discussed; user agreed it would be cleaner. R3 / R4 work item.
- R3 PR #415 still pending merge — DOES NOT fix this bug class (R3's `PlaybookSchedulerJob` calls the same broken `ExecuteAppOnlyAsync` path). Our refactor benefits R3 too.

---

*Checkpoint written 2026-06-23 18:30 UTC. Ready for /compact + resume.*
