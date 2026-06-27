# 01 — Code Archaeology (Canonical-Truth Loop, Step 1)

> **Authored**: 2026-06-26
> **Lane**: CODE ONLY. Companion docs-survey output at `02-docs-survey.md`.
> **Scope**: BFF playbook runtime + orchestration layer + dispatch entry points + deploy scripts.
> **Goal**: feed step 3 (canonical-truth writer) with code-grounded answers to the 8 prompted questions + the 8 docs-survey cross-checks.

---

## 1. Component model + dependency graph

The R4-touched playbook runtime decomposes into five layers:

**Layer A — HTTP entry points** (file → endpoint):
- `Api/Ai/DailyBriefingEndpoints.cs:18-53` registers `POST /api/ai/daily-briefing/{summarize,narrate}`. `/narrate` is the R4 dispatch wrapper (lines 201-374).
- `Api/Ai/AnalysisEndpoints.cs` registers `/api/ai/analysis/*` (action-based analysis, the legacy path).
- `Api/Ai/ChatEndpoints.cs:266` registers `POST /api/ai/playbook-dispatch/execute` (`ExecutePlaybookAsync` at line 2202) — the chat-tool dispatch path.
- `Api/Ai/PlaybookEndpoints.cs` registers `GET/POST /api/ai/playbooks/*` (CRUD + canvas).
- `Api/Agent/PlaybookInvocationService.cs:139` exposes `InvokePlaybookAsync` for the M365 Copilot agent gateway.

**Layer B — Facade boundary (`Services/Ai/PublicContracts/`)**:
- `IInvokePlaybookAi.cs` + `InvokePlaybookAi.cs` (sealed class, lines 28-207) — non-streaming facade aggregating the SSE stream into a single `PlaybookInvocationResult`. ADR-013 facade boundary.
- `IConsumerRoutingService.cs` + `ConsumerRoutingService.cs:53` — `sprk_playbookconsumer` row lookup with 5-min `IMemoryCache` TTL.
- `ConsumerTypes.cs` — compile-time constants (e.g. `DailyBriefingNarrate`).
- `IBriefingAi.cs` + `BriefingAi.cs` — narrow facade for the legacy `/summarize` endpoint (a single OpenAI completion).
- All facades have `Null*` siblings (ADR-032 kill-switch P3); throwing `FeatureDisabledException` when AI is disabled.

**Layer C — Orchestration (`Services/Ai/`)**:
- `AnalysisOrchestrationService.cs` — the LEGACY orchestrator that owns `ExecuteAnalysisAsync` + `ExecutePlaybookAsync`. Document-centric. The latter at line 702 is the entry point that triggered R4's IOORE.
- `PlaybookOrchestrationService.cs` — the NEW node-based orchestrator (`ExecuteAsync` for OBO, `ExecuteAppOnlyAsync` for background). Owns the mode-detection branch at line 246.
- `PlaybookExecutionEngine.cs` — a separate orchestrator dedicated to `ChatSummarize` flows (NOT the same as `PlaybookOrchestrationService`).
- `PlaybookSchedulerJob.cs` — periodic notification fan-out (sprk_playbooktype=2).

**Layer D — Data services**:
- `IPlaybookService.cs` + `PlaybookService.cs` — `sprk_analysisplaybook` row CRUD, scope arrays via N:N, `GetCanvasLayoutAsync` / `SaveCanvasLayoutAsync` for `sprk_canvaslayoutjson`.
- `INodeService.cs` + `NodeService.cs:15` — `sprk_playbooknode` row CRUD (entity set `sprk_playbooknodes`), `SyncCanvasToNodesAsync`.
- `IScopeResolverService.cs` + `ScopeResolverService.cs` — playbook-level + node-level scope resolution (`ResolvePlaybookScopesAsync` at 161, `ResolveNodeScopesAsync` at 42 (interface), `GetActionAsync` at 52 (interface)).
- `AnalysisActionService.cs`, `AnalysisSkillService.cs`, `AnalysisKnowledgeService.cs`, `AnalysisToolService.cs` — per-entity focused services.

**Layer E — Node executors (`Services/Ai/Nodes/`)**:
- `INodeExecutor.cs:20-45` — interface (`SupportedActionTypes`, `Validate`, `ExecuteAsync`).
- `NodeExecutorRegistry.cs:22-109` — DI-driven registry, indexes by `ActionType`. `GetExecutor(actionType)` at line 40.
- 18+ concrete executors: `AiAnalysisNodeExecutor`, `AgentServiceNodeExecutor`, `CreateNotificationNodeExecutor`, `CreateTaskNodeExecutor`, `ConditionNodeExecutor`, `DeliverCompositeNodeExecutor`, `DeliverOutputNodeExecutor`, `DeliverToIndexNodeExecutor`, `DeclineToFindNode`, `EvidenceSufficiencyNode`, `GroundingVerifyNode`, `IndexRetrieveNode`, `LiveFactNode`, `LookupUserMembershipNodeExecutor`, `QueryDataverseNodeExecutor`, `ReturnInsightArtifactNode`, `SendEmailNodeExecutor`, `UpdateRecordNodeExecutor`, `EntityNameValidatorNodeExecutor`.

**Dependency edges** (notable):
- `DailyBriefingEndpoints.HandleNarrate` injects `IConsumerRoutingService` + `IInvokePlaybookAi`.
- `InvokePlaybookAi.cs:37` injects `IPlaybookOrchestrationService`.
- `AnalysisOrchestrationService.cs:822-823` resolves `IPlaybookOrchestrationService` LAZILY from `httpContext.RequestServices` — to break a DI cycle (R6-era comment).
- `PlaybookOrchestrationService.cs:62-78` injects `INodeService`, `INodeExecutorRegistry`, `IScopeResolverService`, `IAnalysisOrchestrationService _legacyOrchestrator`, `IInsightsActionRouter`. The legacy orchestrator is reused for fall-through.

---

## 2. Dispatch entry points + grammars

Three runtime dispatch shapes coexist:

| Path | Entry | Document-centric? | Streaming? | Owns scope resolution? |
|---|---|---|---|---|
| Path A (legacy doc-bound) | `AnalysisOrchestrationService.ExecutePlaybookAsync` (line 702) | YES — accepts `PlaybookExecuteRequest.DocumentIds` | Yes (`AnalysisStreamChunk` IAsyncEnumerable) | YES — at line 977 `_scopeResolver.ResolvePlaybookScopesAsync` runs ONLY when no nodes are present (legacy branch) |
| Path B (node-based OBO) | `PlaybookOrchestrationService.ExecuteAsync` (line 81) | NO — accepts `PlaybookRunRequest` with optional `DocumentIds[]` + `Parameters` | Yes (`PlaybookStreamEvent` IAsyncEnumerable) | Per-node via `ResolveNodeScopesAsync` (line 1074) |
| Path C (node-based app-only) | `PlaybookOrchestrationService.ExecuteAppOnlyAsync` (line 129) | NO | Yes | Same as Path B |
| Path A.5 (facade non-streaming) | `IInvokePlaybookAi.InvokePlaybookAsync` (`InvokePlaybookAi.cs:42`) | NO — passes `Array.Empty<Guid>()` at line 71 | NO — aggregates SSE into `PlaybookInvocationResult` | Delegates to Path B (`_orchestrator.ExecuteAsync` at line 86) |

The R4 `/narrate` endpoint at `DailyBriefingEndpoints.cs:201` is the canonical Path A.5 consumer: it resolves the playbook via `ConsumerRoutingService.ResolveAsync` (line 250) and invokes via `IInvokePlaybookAi.InvokePlaybookAsync` (line 303), passing only `parameters` (briefingPayload + scalars) — never `DocumentIds`.

**Routing context entry** (Path A.5):
1. `IConsumerRoutingService.ResolveAsync(consumerType, code, ctx, env, ct)` queries `sprk_playbookconsumer` filtered by `sprk_consumertype` + `sprk_enabled=true`.
2. In-memory match selection by `(code, env, priority, matchConditions)` at `ConsumerRoutingService.SelectBestMatch`.
3. Returns the resolved playbook Guid, cached 5 minutes.

---

## 3. Playbook lifecycle + Mode transitions (the Legacy mode log condition)

The Legacy-mode log fires from `PlaybookOrchestrationService.cs:246-253`:

```csharp
var nodes = await _nodeService.GetNodesAsync(request.PlaybookId, cancellationToken);
if (nodes.Length == 0)
{
    _logger.LogInformation(
        "Playbook {PlaybookId} has no nodes - using Legacy mode", request.PlaybookId);
    await ExecuteLegacyModeAsync(request, context, writer, cancellationToken);
}
else { ... NodeBased mode ... }
```

The detection key is **the presence of `sprk_playbooknode` rows** with `_sprk_playbookid_value` matching the playbook (queried at `NodeService.cs:84` with `_sprk_playbookid_value eq {playbookId}` ordered by `sprk_executionorder`). There is NO `sprk_playbookmode` column read at runtime — the comment in `predict-matter-cost.playbook.json` confirms this: *"the current Deploy-Playbook.ps1 does not read sprk_playbookmode so adding it here would be a no-op."* The mode is **purely emergent from node-row presence**.

Legacy-mode body lives at `PlaybookOrchestrationService.ExecuteLegacyModeAsync` (line 565). It delegates to `_legacyOrchestrator.ExecutePlaybookAsync` (`AnalysisOrchestrationService.cs:702`), which:
- Loads `playbook` via `_playbookService.GetPlaybookAsync` (line 737).
- Loads `document` via `_documentLoader.GetDocumentAsync` (line 746) — REQUIRES a document.
- 3b at line 772 — JIT canvas-to-node sync: reads `sprk_canvaslayoutjson`, calls `_nodeService.SyncCanvasToNodesAsync` IF canvas has nodes.
- 3c at line 789 — RE-CHECKS `_nodeService.GetNodesAsync` after sync. If now non-zero, delegates BACK to `PlaybookOrchestrationService.ExecuteAsync` (line 822-823 lazy resolve). This is the **node-sync race**: a canvas-only playbook auto-materializes nodes on first execute.
- If nodes still empty after 3c, falls through to the DEPRECATED "sequential tool execution" path (line 970 warning) using playbook-level scopes (line 977).

Three "Legacy" log sites with subtly different wording exist (per grep at top of investigation):
- `PlaybookOrchestrationService.cs:250` — "Playbook {PlaybookId} has no nodes - using Legacy mode" (the R4 UAT log).
- `AnalysisOrchestrationService.cs:970` — "DEPRECATED Legacy mode: No nodes found for playbook ..." (the deeper warning).
- `AppOnlyAnalysisService.cs:530` — "Playbook ... has no nodes — using legacy sequential tool execution" (app-only equivalent).

R4 UAT saw the FIRST log site fire because the `DAILY-BRIEFING-NARRATE` playbook had NOT been deployed with nodes — Path A.5 routed through `PlaybookOrchestrationService.ExecuteAsync` → mode detector → Legacy mode → `ExecuteLegacyModeAsync` → `_legacyOrchestrator.ExecutePlaybookAsync` → IOORE at line 730.

---

## 4. sprk_configjson vs sprk_playbooknode contract (Q1 ANSWER)

**Three columns carry config, with NO overlap**:

| Column | Owner entity | Read by | Written by | Authoring shape |
|---|---|---|---|---|
| `sprk_analysisplaybook.sprk_canvaslayoutjson` | Playbook | `PlaybookService.GetCanvasLayoutAsync` (line 810) → `AnalysisOrchestrationService.cs:772` JIT sync | `PlaybookService.SaveCanvasLayoutAsync` (line 857). Deploy script writes at `Deploy-Playbook.ps1:1044`. | React Flow JSON: `{viewport, nodes[], edges[], version}` |
| `sprk_analysisplaybook.sprk_configjson` | Playbook | Comment at `Deploy-Playbook.ps1:364`. No runtime read site located in BFF code path | `Deploy-Playbook.ps1:618` writes if present in definition JSON | Playbook-level config blob (purpose unclear in current code — appears advisory) |
| `sprk_playbooknode.sprk_configjson` | Node | `NodeExecutor.Validate`/`ExecuteAsync` per executor (read from `context.Node.ConfigJson`); orchestrator extracts `__actionType` at `PlaybookOrchestrationService.cs:867` | `NodeService.CreateNodeAsync` (line 170); `Deploy-Playbook.ps1:831` | Per-node JSON with: `__actionType`, executor-specific fields (FetchXml, entityType, sectionName, deliveryType, fieldMappings, …), routing fields (destination, widgetType — see `node-routing-config.schema.json`) |

**At runtime, the orchestrator reads node rows directly** — `PlaybookOrchestrationService.cs:244` → `_nodeService.GetNodesAsync(playbookId)`. `sprk_canvaslayoutjson` is only consulted in the LEGACY orchestrator's JIT sync (line 772) — when the legacy path runs, it pre-syncs canvas → node rows so the executor path can pick up nodes. Once nodes exist, the canvas JSON is informational only (UI rehydration on next builder open).

**Precedence inside a single node row** is established at `PlaybookOrchestrationService.cs:1070-1138`:
- If `node.ActionId != Guid.Empty` → action FK is canonical; `actionType = action.ActionType` (per Insights r2 Wave B 2026-06-02 decision, lines 1059-1069).
- ELSE if `NodeType.AIAnalysis` and no ActionId → error "AI node requires an Action" (line 1099).
- ELSE structural fallback: `ExtractActionTypeFromConfig(node.ConfigJson)` (line 1116) reads `__actionType` from the configJson; if absent, falls back to `NodeType` → ActionType default switch (line 1117-1127).

**Cross-check finding** (Q1 source-of-truth): the R4 spec's "Repo JSON files = canonical source-of-truth" decision is technically about the AUTHORING DSL — the deploy script's input contract. At runtime, **`sprk_playbooknode` rows are canonical**; `sprk_canvaslayoutjson` is build-time + UI; `sprk_configjson` on the playbook row is currently a near-no-op in the BFF read path.

---

## 5. Scope-array enforcement vs advisory (Q2 ANSWER)

The runtime treats scope arrays as **ADVISORY pre-fetch, not enforcement**:

- `ScopeResolverService.ResolvePlaybookScopesAsync` (line 161) loads playbook.ToolIds/SkillIds/KnowledgeIds and returns `ResolvedScopes`. It does NOT cross-check that a node's action matches any of the playbook-scoped Tools.
- The legacy path (`AnalysisOrchestrationService.cs:977`) calls `ResolvePlaybookScopesAsync` once and hands `scopes` to a sequential tool loop — here the playbook's Tool array IS consumed (it determines which tool handlers run). This is the only place playbook-level scope arrays gate execution.
- In NodeBased mode (`PlaybookOrchestrationService.cs:1074`), the orchestrator resolves SCOPES PER NODE via `ResolveNodeScopesAsync(node.Id, …)` — pulling from `sprk_playbooknode_{skill|knowledge|tool}` N:N relationships (not the playbook-level scopes).
- **No code path validates that** `node.scopes.skills ⊆ playbook.scopes.skills` or similar. The `validateAsync` method at `PlaybookOrchestrationService.cs:285` checks for cycles, missing actions, dependency validity, and duplicate output variables — but NOT scope-array consistency.

So for an Action in a node's executor: if the action references a Skill not in `playbook.scopes.skills`, nothing in the BFF rejects it. The validation is **purely informational** at the playbook level, and **purely additive** at the node level.

`AnalysisAction` (the record at `IScopeResolverService.cs:591-653`) has no `Code` field — Action lookup at runtime is by `Guid` via `ScopeResolverService.GetActionAsync`. Action-code → Guid resolution is a DEPLOY-TIME concern (Deploy-Playbook.ps1 lines 419-430), not runtime.

---

## 6. Action lookup grammar (Q3 ANSWER)

**Two distinct lookup paths exist depending on which dispatch shape called**:

### A. Per-node lookup (the canonical case)
`PlaybookOrchestrationService.ExecuteNodeAsync` (line 1070): node carries `node.ActionId` (FK Guid). The orchestrator calls `_scopeResolver.GetActionAsync(node.ActionId, ct)` (line 1077). Resolution is FK-only — there is no `actionCode` lookup in the runtime hot path.

Implication: at runtime, the canonical question "what is THE Action row for this node?" is answered by the FK that the deploy script wrote at `Deploy-Playbook.ps1:835` (`sprk_actionid@odata.bind = "sprk_analysisactions({actionGuid})"`).

### B. Per-code lookup (FK-bypass for chat-summarize + Insights routing)
- `PlaybookExecutionEngine.cs:475` (chat-summarize FK chain): looks up an action by `sprk_actioncode` (e.g., "SUM-CHAT@v1") using `KeyAttributeCollection`. Documented in `SessionSummarizeOrchestrator.cs:23` as "the alternate-key bypass... when the playbook FK chain is broken."
- `InsightsActionRouter.LoadActionByCodeAsync` (`InsightsActionRouter.cs:255`): per-area/per-pair routing for `universal-ingest@v1`. The router CAN OVERRIDE the resolved action at `PlaybookOrchestrationService.cs:1178` via `ApplyInsightsRoutingAsync`. This is the only documented runtime swap of a FK-resolved Action for a code-resolved Action.

### C. Action-by-actionType (no canonical row)
**There is NO `IsDefault` or `IsCanonical` flag on `sprk_analysisaction`.** The orchestrator never searches "all Actions where ActionType=X, pick the canonical one." When a node has only `actionType=52` (LookupUserMembership) with no FK, the structural fallback (line 1116-1127) builds a SYNTHETIC `AnalysisAction { Id=Guid.Empty, ActionType=…, Name=node.Name }`. The executor then dispatches by ActionType from the registry (line 1141). The synthetic action carries NO SystemPrompt — which is fine for non-AI nodes (Workflow / Output / Control) but is a silent gap for AI nodes that ASSUME a SystemPrompt is available.

In other words: the W1 playbook pattern of "ActionType only, no FK" is supported for non-AI executors (LookupUserMembership, CreateNotification, QueryDataverse — they read configJson, not SystemPrompt). For AI executors, the orchestrator surfaces `"AI node '{node.Name}' requires an Action but has no ActionId"` (line 1099).

The Deploy-Playbook.ps1 lint at line 331-356 enforces every dispatchable node carries `actionCode`, with `DeliverComposite` as the only documented exemption (line 333). So in practice EVERY deployed node has an FK; the synthetic fallback is a defensive in-code safety net, not the expected path.

---

## 7. Config-bag fields audit + overuse boundary (Q4 ANSWER)

### sprk_analysisaction (per AnalysisActionService.cs columns)
- `sprk_actioncode` (alternate key, string code — deploy-time + chat-summarize bypass)
- `sprk_name`, `sprk_description`
- `sprk_systemprompt` (Memo) — **JPS prompt primitive** per R5 CLAUDE.md "BINDING gotcha"; carries both system prompt + (sometimes) the output schema as one blob
- `sprk_temperature` (Decimal 0.0–2.0; null = 0.0 default)
- `sprk_ActionTypeId` (lookup) → `sprk_actiontype` row with `sprk_executoractiontype` (int, dispatch axis)
- `sprk_outputschemajson` (referenced in comments; in playbook JSON files — but `AnalysisActionService.cs:37` does NOT $select it on read. **NOT read in this BFF code path today**.)
- **No `sprk_configurationjson` column on `sprk_analysisaction`** — repo-wide grep returns ZERO C# references. The owner's terminology refers to `sprk_configjson` (which lives on the node + playbook, not the action).

### sprk_analysisplaybook (per PlaybookService.cs + PlaybookDto.cs)
- `sprk_name`, `sprk_description`, `sprk_ispublic`
- `sprk_playbooktype` (option-set; 0=AiAnalysis, 2=Notification; `PlaybookSchedulerJob.cs:86`)
- `sprk_issystemplaybook` (bool)
- `sprk_playbookcapabilities` (multi-select option-set → 7 capabilities; mapped at `PlaybookService.cs:783-798`)
- `sprk_canvaslayoutjson` (Memo) — React Flow JSON
- `sprk_configjson` (Memo) — playbook-level blob; written by Deploy-Playbook.ps1 at line 618 IF present in definition; **no runtime read site located**
- `sprk_jps_matching_metadata` (Memo, R1 chat-routing) — `PlaybookDto.cs:108`
- `sprk_indexstatus` / `sprk_indexhash` / `sprk_lastindexedat` / `sprk_lastindexerror` (R1 chat-routing index lifecycle)
- N:N relationships: `sprk_analysisplaybook_action`, `sprk_playbook_{skill,knowledge,tool}` (constants at `PlaybookService.cs:29-30`)
- `triggerPhrases`, `recordType`, `entityType` (R2 routing filter columns)

### sprk_playbooknode (per NodeService.cs)
- `sprk_name`, `sprk_nodetype` (option-set: AIAnalysis=100000000, Output=100000001, Control=100000002, Workflow=100000003, DeliverComposite=100000004 — see `INodeExecutor.cs:59-91`)
- `sprk_executionorder` (int)
- `sprk_isactive` (bool — MUST be set true; default false; Deploy-Playbook.ps1:823 comment is load-bearing)
- `sprk_playbookid` (lookup → playbook)
- `sprk_actionid` (lookup → action; the canonical dispatch FK)
- `sprk_modeldeploymentid` (lookup → `sprk_aimodeldeployment`)
- `sprk_outputvariable` (string — downstream consumers reference via name)
- `sprk_dependsonjson` (JSON array of node Guids; written in Deploy-Playbook.ps1 second-pass at line 891)
- `sprk_conditionjson` (JSON; conditional execution guard — Phase 5 per orchestrator comment at line 1049)
- `sprk_configjson` (Memo) — **the per-node config blob**; carries `__actionType` + executor-specific fields + routing (destination, widgetType)
- `sprk_timeoutseconds`, `sprk_retrycount`, `sprk_position_x`, `sprk_position_y` (UI metadata)
- N:N relationships: `sprk_playbooknode_{skill,knowledge,tool}` (constants at `NodeService.cs:27-29`)

### Overlap + winners

The only conceptual overlap is `__actionType` in `sprk_playbooknode.sprk_configjson` vs `node.ActionId` FK → action.ActionType. The orchestrator picks the FK if present (line 1070), with `__actionType` as the structural-fallback only (line 1116). The Deploy-Playbook.ps1 lint says: NEVER deploy a dispatchable node without an actionCode (FK) — the structural fallback is for emergency cases only and exposes the Designer-clobbering risk (Deploy script comment, line 321).

The owner's "overuse boundary" concern (Q4 prompt) maps onto this: the temptation to stuff dispatch/routing/scope info into `sprk_playbooknode.sprk_configjson` instead of pulling those concerns out to first-class columns. Examples in the wild:
- Routing config (destination, widgetType, deliveryType) currently lives in configJson per `node-routing-config.schema.json` comment ("additive properties on the existing node-config blob"). It's NOT a first-class set of columns. Validation is gate-enforced at Deploy-Playbook.ps1:789 via JSON Schema (FR-14e).
- DeliverComposite's `sections[]` array (ADR-037) lives in configJson — there is no `sprk_deliverysectionjson` column. The Deploy script lint exempts these nodes from the actionCode requirement at line 333 because the executor is registered by ActionType, not FK.

---

## 8. Empty-payload defenses (Q5 ANSWER)

Comprehensive sweep of `DocumentIds[0]` / `.First()` / `[0]` in the orchestrator + node executors:

| Site | Code | Defense | Risk |
|---|---|---|---|
| `AnalysisOrchestrationService.cs:117` | `var documentId = request.DocumentIds[0];` (action-based analysis branch in `ExecuteAnalysisAsync`) | NONE. Empty array → IOORE. | Pre-existing. Path: `AnalysisEndpoints` → `ExecuteAnalysisAsync` with no PlaybookId + empty DocumentIds. Likely defended at endpoint level. |
| `AnalysisOrchestrationService.cs:730` | `var documentId = request.DocumentIds[0];` (legacy-mode playbook execute) | **GUARDED** at lines 720-728 with R4 hotfix (2026-06-26): returns an error chunk with operator-actionable message before reaching `[0]`. | Fixed. The R4 IOORE root cause is closed. |
| `AiAnalysisNodeExecutor.cs:1089` | `var entityRecord = searchResponse.Results[0];` (L3 entity context retrieval) | Guarded at line 1081 — `if (Results.Count == 0) return null` | Safe |
| `DeclineToFindNode.cs:150` | `var primary = gaps[0];` | Guarded at line 141 — `if (gaps.Count == 0) return ...` | Safe |
| Various `Parameters?.TryGetValue(...)` / `FirstOrDefault` in QueryDataverseNodeExecutor, LookupUserMembershipNodeExecutor, CreateNotificationNodeExecutor | All use null-safe pattern (no `[0]` on potentially-empty collections) | n/a | Safe |

The R4 IOORE-class risk is now confined to `AnalysisOrchestrationService.cs:117` — but that path is reachable ONLY from `ExecuteAnalysisAsync` with `PlaybookId is null` AND `request.DocumentIds.Length == 0`. The endpoint contract requires DocumentIds for non-playbook analysis, so this is a defense-in-depth gap rather than an active bug.

**Upstream caller audit**: `IPlaybookOrchestrationService.ExecuteAsync` accepts `PlaybookRunRequest.DocumentIds = Array.Empty<Guid>()` and passes through to node executors that all consume `context.Document` (which may be null) or `context.Parameters` — none of the per-node executors dereference `DocumentIds[0]` directly. The chat-routing-redesign R1 / R4 invocation pattern (Path A.5) deliberately passes empty DocumentIds.

---

## 9. Deploy-Playbook.ps1 contract (Q6 ANSWER)

**Input file format** (1069 lines; required: `playbook { name }` + `nodes[]`):
```jsonc
{
  "$comment": "...",
  "actions": [ /* optional: actions to upsert before playbook */ ],
  "playbook": {
    "name": "...",
    "description": "...",
    "isPublic": true,
    "isSystemPlaybook": false,
    "sprk_playbooktype": 0,
    "sprk_configjson": { /* optional playbook-level blob */ },
    "code": "...",                    // optional; playbookCode for error messages
    "scopes": { "actions": ["..."], "skills": [...], "knowledge": [...], "tools": [...] }
    // OR flat: "actions": [], "skills": [], etc.
  },
  "nodes": [
    {
      "name": "...",
      "nodeType": "AIAnalysis|Output|Control|Workflow|DeliverComposite",
      "actionCode": "...",            // REQUIRED for all non-DeliverComposite nodes (lint at 331-356)
      "model": "...",                 // optional; resolved against sprk_aimodeldeployments
      "outputVariable": "...",
      "positionX": 100, "positionY": 200,
      "configJson": { "__actionType": 51, ... },  // validated against node-routing-config schema (FR-14e gate at line 789)
      "dependsOn": ["nodeName1", ...],
      "scopes": { "skills": [...], "knowledge": [...], "tools": [...] }
    }
  ]
}
```

**Sequence of Dataverse operations** (the 12-step procedure narrated by the script):
1. Parse + validate definition (line 296-356, includes the actionCode lint).
2. Collect all scope codes from playbook-level + node-level.
3. Resolve scope codes → Guids via `sprk_analysisactions[sprk_actioncode]`, `sprk_analysisskills[sprk_skillcode]`, `sprk_analysisknowledges[sprk_externalid]`, `sprk_analysistools[sprk_toolcode]` (lines 419-477).
4. Resolve model deployments by name.
5. Check for existing playbook by name. Without `-Force`: SKIP if exists (line 587-590). With `-Force`: delete + recreate.
6. POST `sprk_analysisplaybooks` row (line 621) with: name, description, ispublic, optional sprk_playbooktype, optional sprk_issystemplaybook, optional sprk_configjson.
7. Associate playbook-level N:N scopes (`sprk_analysisplaybook_action`, `sprk_playbook_skill`, `sprk_playbook_knowledge`, `sprk_playbook_tool`).
8. Per-node loop (line 765-853): validate `sprk_configjson` against schema (FR-14e, line 789) → POST `sprk_playbooknodes` row with `sprk_isactive=true` (line 823 comment is load-bearing — default false on the column), `sprk_playbookid@odata.bind`, optional `sprk_actionid@odata.bind`, optional `sprk_modeldeploymentid@odata.bind`, optional `sprk_configjson`.
9. Second pass: PATCH each node with `sprk_dependsonjson` (built from resolved Guids).
10. Associate node-level N:N scopes (`sprk_playbooknode_{skill,knowledge,tool}`).
11. Build canvas layout JSON (nodes + edges in React Flow shape) → PATCH playbook with `sprk_canvaslayoutjson` (line 1044).
12. Summary print.

**Mode setting**: NOT explicit. The script does NOT write a `sprk_playbookmode` column. The playbook becomes "node-based" purely by virtue of having `sprk_playbooknode` rows — the orchestrator's empirical detection (§3).

**Idempotency**: skip-by-name (line 587). With `-Force`, delete + recreate — NOT a true upsert. Scope/Action rows are read-only (must already exist via `Seed-JpsActions.ps1`).

**Failure recovery**: each step throws; no rollback. A partially-deployed playbook (e.g., nodes created but canvas layout PATCH fails) leaves Dataverse inconsistent. The script's `-DryRun` mode at lines 412-421 + 644-656 lets the operator preview before commit.

**Cross-reference**: Insights playbooks (universal-ingest, predict-matter-cost, matter-health-single) note this script does NOT read `sprk_playbookmode`. R4's "deploy data as code" rule is implemented by this script's `-Force` toggle + the deploy-time lint (line 331-356).

---

## 10. IInvokePlaybookAi call chain (Q7 ANSWER)

Owner's claim was: "Task 030's 'IInvokePlaybookAi handles empty documentIds' claim was based on code-comment interpretation rather than empirical test." The code-comment in question is at `IInvokePlaybookAi.cs:64-67`:

```csharp
// Construct the orchestration request. Note: the facade does NOT accept
// documentIds today — invoke_playbook callers pass parameters only. The
// orchestration service interprets an empty documentIds array as "no
// document context" (consistent with the existing M365 Copilot adapter path).
var request = new PlaybookRunRequest
{
    PlaybookId = playbookId,
    DocumentIds = Array.Empty<Guid>(),
    Parameters = parameters,
};
```

**The call chain from `InvokePlaybookAsync` with empty DocumentIds**:

1. `InvokePlaybookAi.InvokePlaybookAsync` at `InvokePlaybookAi.cs:42` builds `PlaybookRunRequest { DocumentIds = Array.Empty<Guid>() }` (line 68-73).
2. Calls `_orchestrator.ExecuteAsync(request, context.HttpContext, ct)` at line 86 — this is `IPlaybookOrchestrationService.ExecuteAsync` → `PlaybookOrchestrationService.ExecuteAsync` (line 81).
3. `PlaybookOrchestrationService.ExecuteAsync` → `ExecuteInternalAsync` (line 235).
4. `ExecuteInternalAsync` line 244: `_nodeService.GetNodesAsync(request.PlaybookId, ct)`. **This is the load-bearing branch**.
5. **If the playbook has nodes** (the intended R4 path once tasks 010-013 deploy `DAILY-BRIEFING-NARRATE`): line 256 logs NodeBased mode, calls `ExecuteNodeBasedModeAsync` (line 653). Node executors receive `PlaybookRunContext.DocumentIds = []` — none of them dereference `[0]`. Path succeeds. **Empty DocumentIds is HANDLED.**
6. **If the playbook has NO nodes** (R4 UAT state — DAILY-BRIEFING-NARRATE wasn't deployed): line 248 logs Legacy mode, calls `ExecuteLegacyModeAsync` (line 565). This forwards to `_legacyOrchestrator.ExecutePlaybookAsync` (line 597) → `AnalysisOrchestrationService.cs:702`. **As of 2026-06-26**, that path is GUARDED at lines 720-728 with the R4 hotfix — emits an error chunk → `PlaybookEventType.RunFailed` → `InvokePlaybookAi.cs:123-127` sets `ErrorCode = "PLAYBOOK_INVOCATION_FAILED"` → `DailyBriefingEndpoints.cs:309-321` returns 503 ProblemDetails.

**So the owner's empirical claim is correct** for the unfixed state: pre-hotfix, the chain WOULD blow up at `AnalysisOrchestrationService.cs:730` with `IndexOutOfRangeException`. Post-hotfix (in the current code), the chain produces a clean 503 with operator-actionable message. The "intended" path is to deploy the playbook with nodes; the empty-DocumentIds tolerance only works in NodeBased mode.

The facade comment is therefore HALF-correct: it accurately describes the intended NodeBased path, but glossed over the legacy fallback's structural document requirement. The R4 hotfix closes the runtime gap; the documented behavior is now true.

---

## 11. Docs-survey cross-checks (Q8 ANSWER — 8 sub-sections)

### 11.1 — "9 canvas types / 4 NodeType / 18 ActionType"
**FALSE in current code**. The actual counts:
- **NodeType**: 5 values (`INodeExecutor.cs:59-91`) — AIAnalysis, Output, Control, Workflow, DeliverComposite. The docs' "4" is stale by one (DeliverComposite added per ADR-037).
- **ActionType**: 24 documented values (`INodeExecutor.cs:97-252`) — 0, 1, 2, 10, 11, 12, 20, 21, 22, 23, 24, 30, 31, 32, 33, 40, 41, 42, 50, 51, 52, 60, 70, 80, 90, 100, 110, 120, 130, 140, 141. **Count is 31 enum values**, not 18 (or even 19). The docs are badly stale.
- **Canvas types**: not enumerated in INodeExecutor — handled by Deploy-Playbook.ps1 mapping at line 988-993 (`aiAnalysis`, `output`, `control`, default aiAnalysis). No 9-type table found in code.

### 11.2 — "NodeType.DeliverComposite = 100_000_004 is appended"
**CONFIRMED**. `INodeExecutor.cs:90` defines `DeliverComposite = 100_000_004`. ADR-037 wording matches.

### 11.3 — "G6 CanvasServerMappingDriftTests.cs CI drift test"
**Not located in this search** — file not surfaced by Glob. Should be confirmed in tests directory (not searched in this pass; flag as open question).

### 11.4 — "IInvokePlaybookAi.cs exists, non-document semantic at lines 67-72"
**CONFIRMED**. `InvokePlaybookAi.cs:64-73` carries the comment + `Array.Empty<Guid>()` construction. The comment's claim is consistent with the orchestrator's NodeBased branch but did not match the Legacy-mode fallback (now fixed per Q7).

### 11.5 — "ExecutorActionType 0/51/52/60/70/80/90/100/110/120/130/140; gap-analysis (no 141)"
**STALE — 141 IS in the enum.** `INodeExecutor.cs:251` defines `EntityNameValidator = 141`. The R4 PR has already merged this enum value. So the spec's "no 141 yet" claim is wrong against current `master`/work-branch; R4 W0 still needs the *Dataverse Action row* + executor wiring, but the enum constant is present.

### 11.6 — "AI-ARCHITECTURE.md Audit-findings 4 binding decisions cite .claude/patterns/ai/*.md"
Not verified in this code archaeology (a docs-level claim). Flag for cross-check.

### 11.7 — "INSIGHTS-PLAYBOOK-VS-RAG-DECISION-TREE.md broken link"
Not a code claim. Flag for docs-survey.

### 11.8 — "Deployed playbooks target `sprk_event` (not OOB task)"
**Cannot verify from C# source alone** — the FetchXml queries live in `sprk_playbooknode.sprk_configjson` blobs in the deployed Dataverse environment. The repo JSON files at `projects/spaarke-daily-update-service/notes/playbooks/` are what the deploy script would PUSH; comparing to what's actually deployed requires a Dataverse MCP query. Per the R4 CLAUDE.md note (2026-06-25): "deployed playbooks target `sprk_event`. R4 owner clarification BINDING." The code does not enforce this — `QueryDataverseNodeExecutor.Validate` checks `EntityLogicalName` is non-empty but doesn't validate against an allow-list.

---

## 12. NodeType vs ActionType — two axes

These are **two orthogonal dispatch axes**:

- **NodeType** (5 values; option-set on `sprk_playbooknode`) — coarse category determining scope-resolution behavior. Per `INodeExecutor.cs:50-91`:
  - `AIAnalysis` → requires Action + resolves all scopes (skills, knowledge, tools).
  - `Output` → structural; no Action or scopes needed.
  - `Control` → structural; no Action or scopes needed.
  - `Workflow` → future; rule-based, scope TBD.
  - `DeliverComposite` → R6 multi-section delivery; no Action or scopes.
- **ActionType** (24 enum values; on `sprk_analysisaction.sprk_actiontype` via `sprk_actiontype`/`sprk_executoractiontype` lookup) — fine-grained executor selection. The `NodeExecutorRegistry` indexes executors BY `ActionType`, not NodeType.

`PlaybookOrchestrationService.ExecuteNodeAsync` (line 1070-1138) resolves the effective `(action, actionType, scopes)` triplet using NodeType to GUARD which scopes to resolve and ActionType to PICK the executor. The two axes can be incongruent — e.g., a `NodeType.Control` node can carry `ActionType.QueryDataverse` via `__actionType` in ConfigJson. The orchestrator accommodates by routing on `actionType` from the action FK if present, else from ConfigJson `__actionType`, else from `NodeType → default` mapping.

The dispatch axis priority is: **action FK → ConfigJson __actionType → NodeType-default**. The Deploy script lint pushes every dispatchable node to fall on the first rung (FK).

---

## 13. Open questions

1. **G6 drift test location**: `CanvasServerMappingDriftTests.cs` — file path not located. Should be in `tests/unit/Sprk.Bff.Api.Tests/` or `tests/integration/`. Need targeted Glob to verify existence.
2. **sprk_analysisplaybook.sprk_configjson runtime read site**: the Deploy script writes it (line 618), but no BFF read path was located. Either it's used by a client (PCF/Code Page) or it's effectively dead. Worth a repo-wide `sprk_configjson` grep in PCF + code-pages folders.
3. **sprk_outputschemajson column**: referenced in playbook JSON files + R6 migration script comments (`Migrate-SumChatActionOutputSchema.ps1`), but `AnalysisActionService.cs` `$select` does not include it. Is this column populated at deploy time but consumed only by a specific node executor's bespoke read? Need to check `AiAnalysisNodeExecutor` for separate Dataverse round-trip.
4. **Deployed-vs-repo entity choices** (Q8 sub-8): cannot be answered from BFF code alone. Recommend MCP `read_query` on `sprk_playbooknode` filter by playbook name for the 7 R4 notification playbooks to compare FetchXml entity names against `sprk_event` / `sprk_communication` rule.
5. **`PlaybookExecutionEngine` vs `PlaybookOrchestrationService`**: there are TWO orchestrators with overlapping responsibilities (the former for chat-summarize; the latter for everything else). Boundary not fully traced in this pass — should be canonicalized in step 3.
6. **9 canvas types**: docs claim 9; code shows 4 (`Deploy-Playbook.ps1:988-993`). Where do the other 5 live? Possibly in the PCF Playbook Builder canvas-Designer source.
7. **Scope-array enforcement**: confirmed advisory. Should a strengthening proposal be filed (e.g., warn-on-mismatch in `ValidateAsync`)? Out of scope for archaeology.
8. **AppOnlyAnalysisService.cs:530 third "Legacy mode" log site**: not deeply traced. It's an APP-ONLY service that's a sibling to `AnalysisOrchestrationService` — when is it invoked vs the main orchestrator? Likely PlaybookSchedulerJob fan-out path.
9. **R5 `PlaybookExecutionEngine.cs:457` "chat-summarize FK chain is broken"**: the alternate-key bypass exists because of this fragility. Implies the FK chain CAN be broken in steady state (probably during migrations). The bypass mechanism is well-documented (`SessionSummarizeOrchestrator.cs:23-58`) but suggests architectural debt.

---

*Archaeology complete. Step 3 (canonical-truth writer) inputs: §1 component model (informs the rewritten ai-architecture-playbook-runtime.md), §4 + §7 configjson contract (informs the new ai-standards-playbook-design.md boundary doc), §6 action lookup grammar (closes docs gap 5.5 + 5.11), §9 Deploy-Playbook.ps1 contract (closes docs gap 5.4), §10 IInvokePlaybookAi chain (closes docs gap 5.6 — and feeds the new ai-architecture-consumer-routing.md). Open questions in §13 should be resolved during step 3 writing OR filed as residual work items.*
