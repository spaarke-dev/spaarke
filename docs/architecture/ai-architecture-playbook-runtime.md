# AI Architecture — Playbook Runtime

> **Last reviewed**: 2026-06-26
> **Authored by**: canonical-truth loop step 3 (spaarke-daily-update-service-r4)
> **Status**: Canonical. Supersedes runtime sections of `playbook-architecture.md` (now a redirect) and the Tool Handler / Scope Resolution / Known Pitfalls sections of `AI-ARCHITECTURE.md` (now stripped).
> **Scope**: The load-bearing runtime contract for how BFF executes Spaarke playbooks — dispatch shapes, mode detection, action lookup, config-column ownership, scope semantics, empty-payload behaviour, the two parallel orchestrators, and the Legacy-mode log catalog.
> **NOT in scope**: JPS schema reference (see `ai-guide-jps-authoring.md`), maker recipes (see `ai-guide-playbook-author.md`), consumer dispatch (see `ai-architecture-consumer-routing.md`), config-bag boundaries (see `ai-architecture-actions-nodes-scopes.md`), deploy procedure (see `ai-guide-playbook-deploy-recipe.md`).

---

## 1. Component model — five runtime layers

The BFF playbook runtime decomposes into five layers. File:line references are against the work-branch state at 2026-06-26.

| Layer | Responsibility | Canonical sources |
|---|---|---|
| **A. HTTP entry points** | Dispatch a playbook by some URL contract | `Api/Ai/DailyBriefingEndpoints.cs:18-53` (`/api/ai/daily-briefing/{summarize,narrate}`); `Api/Ai/AnalysisEndpoints.cs` (`/api/ai/analysis/*`); `Api/Ai/ChatEndpoints.cs:266` → `ExecutePlaybookAsync` at `:2202`; `Api/Ai/PlaybookEndpoints.cs` (CRUD + canvas); `Api/Agent/PlaybookInvocationService.cs:139` (M365 Copilot adapter) |
| **B. Facade boundary** (`Services/Ai/PublicContracts/`) | ADR-013 facade: external CRUD code consumes AI only through this layer | `IInvokePlaybookAi.cs` + `InvokePlaybookAi.cs` (non-streaming aggregator); `IConsumerRoutingService.cs` + `ConsumerRoutingService.cs:53` (5-min IMemoryCache); `IBriefingAi.cs` + `BriefingAi.cs` (legacy /summarize single-LLM-call); `ConsumerTypes.cs` (compile-time constants e.g. `DailyBriefingNarrate`); all have `Null*` siblings per ADR-032 P3 kill-switch |
| **C. Orchestration** (`Services/Ai/`) | Decide Legacy vs NodeBased, execute the node graph, stream SSE | `AnalysisOrchestrationService.cs` (LEGACY — `ExecuteAnalysisAsync` + `ExecutePlaybookAsync:702`); `PlaybookOrchestrationService.cs` (NEW node-based — `ExecuteAsync:81`, `ExecuteAppOnlyAsync:129`, mode branch at `:246-253`); `PlaybookExecutionEngine.cs` (chat-summarize-only — see §10); `PlaybookSchedulerJob.cs` (notification fan-out, `sprk_playbooktype=2`) |
| **D. Data services** | Dataverse row CRUD + N:N + scope arrays | `PlaybookService.cs` (sprk_analysisplaybook); `NodeService.cs:15` (sprk_playbooknodes — entity set name); `ScopeResolverService.cs` (`ResolvePlaybookScopesAsync:161`, `ResolveNodeScopesAsync:42`, `GetActionAsync:52`); per-entity services `AnalysisActionService`, `AnalysisSkillService`, `AnalysisKnowledgeService`, `AnalysisToolService` |
| **E. Node executors** (`Services/Ai/Nodes/`) | One executor per ActionType — does the actual work | `INodeExecutor.cs:20-45` (interface); `NodeExecutorRegistry.cs:22-109` (indexed by ActionType; `GetExecutor(actionType):40`); 18+ concrete executors including `AiAnalysisNodeExecutor`, `LookupUserMembershipNodeExecutor`, `CreateNotificationNodeExecutor`, `QueryDataverseNodeExecutor`, `ConditionNodeExecutor`, `DeliverCompositeNodeExecutor`, `DeliverOutputNodeExecutor`, `DeclineToFindNode`, `EntityNameValidatorNodeExecutor`, others |

**Notable dependency edges**:
- `DailyBriefingEndpoints.HandleNarrate` injects `IConsumerRoutingService` + `IInvokePlaybookAi`.
- `InvokePlaybookAi.cs:37` injects `IPlaybookOrchestrationService`.
- `AnalysisOrchestrationService.cs:822-823` resolves `IPlaybookOrchestrationService` **lazily** from `httpContext.RequestServices` — to break a DI cycle (R6-era comment). This matters when reading delegation flow.
- `PlaybookOrchestrationService.cs:62-78` injects `INodeService`, `INodeExecutorRegistry`, `IScopeResolverService`, `IAnalysisOrchestrationService _legacyOrchestrator` (reused for fall-through), `IInsightsActionRouter`.

---

## 2. Four dispatch shapes

Three runtime grammars coexist plus one facade non-streaming wrapper. Path A.5 is the canonical R4 surface for `/narrate`.

| Path | Entry point | Document-centric? | Streaming? | Owns scope resolution? |
|---|---|---|---|---|
| **Path A (legacy doc-bound)** | `AnalysisOrchestrationService.ExecutePlaybookAsync` (`:702`) | YES — accepts `PlaybookExecuteRequest.DocumentIds` | Yes (`AnalysisStreamChunk` IAsyncEnumerable) | YES — at `:977` `_scopeResolver.ResolvePlaybookScopesAsync` runs ONLY when no nodes are present (legacy branch) |
| **Path B (node-based OBO)** | `PlaybookOrchestrationService.ExecuteAsync` (`:81`) | NO — accepts `PlaybookRunRequest` with optional `DocumentIds[]` + `Parameters` | Yes (`PlaybookStreamEvent` IAsyncEnumerable) | Per-node via `ResolveNodeScopesAsync` (`:1074`) |
| **Path C (node-based app-only)** | `PlaybookOrchestrationService.ExecuteAppOnlyAsync` (`:129`) | NO | Yes | Same as Path B |
| **Path A.5 (facade non-streaming)** | `IInvokePlaybookAi.InvokePlaybookAsync` (`InvokePlaybookAi.cs:42`) | NO — passes `Array.Empty<Guid>()` at `:71` | NO — aggregates SSE into `PlaybookInvocationResult` | Delegates to Path B (`_orchestrator.ExecuteAsync` at `:86`) |

R4's `/narrate` endpoint at `DailyBriefingEndpoints.cs:201` is the canonical Path A.5 consumer: it resolves the playbook via `ConsumerRoutingService.ResolveAsync` (`:250`) and invokes via `IInvokePlaybookAi.InvokePlaybookAsync` (`:303`), passing only `parameters` (briefingPayload + scalars) — never `DocumentIds`. For consumer-side wiring see `ai-architecture-consumer-routing.md`.

---

## 3. Mode-is-emergent contract (binding)

**There is NO `sprk_playbookmode` column read at runtime.** Mode is detected solely by `sprk_playbooknode` row presence.

The Legacy-mode log fires at `PlaybookOrchestrationService.cs:246-253`:

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

The detection key is **the presence of `sprk_playbooknode` rows** with `_sprk_playbookid_value` matching the playbook (queried at `NodeService.cs:84` with `_sprk_playbookid_value eq {playbookId}` ordered by `sprk_executionorder`). This is empirical, not declarative. The deploy script comment in `predict-matter-cost.playbook.json` is load-bearing: *"the current Deploy-Playbook.ps1 does not read `sprk_playbookmode` so adding it here would be a no-op."*

**Implication for every project**: if you want NodeBased execution, you must deploy node rows. There is no mode flag to flip. The R4 UAT defect was a missing `sprk_playbooknode` set for `DAILY-BRIEFING-NARRATE`; the playbook fell through to Legacy mode despite being declared "node-based" in repo JSON.

### Legacy-mode log site catalog (three locations with subtly different wording)

| Site | Wording | When it fires |
|---|---|---|
| `PlaybookOrchestrationService.cs:250` | `"Playbook {PlaybookId} has no nodes - using Legacy mode"` | OBO entry path (`ExecuteAsync`) when `GetNodesAsync.Length == 0` |
| `AnalysisOrchestrationService.cs:970` | `"DEPRECATED Legacy mode: No nodes found for playbook ..."` | Deeper warning emitted by the legacy orchestrator after JIT canvas sync also yields zero nodes |
| `AppOnlyAnalysisService.cs:530` | `"Playbook ... has no nodes — using legacy sequential tool execution"` | App-only equivalent invoked by `PlaybookSchedulerJob` notification fan-out |

When triaging a UAT incident, the FIRST site is the canonical R4 signal. The SECOND fires later in the same request after the JIT canvas-to-node sync (see §6) has been attempted and produced nothing. The THIRD is the app-only sibling, surfaced by background scheduler runs.

---

## 4. Three config columns — no overlap

The runtime uses three distinct Memo columns to carry config. **They do not overlap, and the orchestrator reads only one at runtime.**

| Column | Owner entity | Read by | Written by | Authoring shape |
|---|---|---|---|---|
| `sprk_analysisplaybook.sprk_canvaslayoutjson` | Playbook | `PlaybookService.GetCanvasLayoutAsync:810` → `AnalysisOrchestrationService.cs:772` JIT sync | `PlaybookService.SaveCanvasLayoutAsync:857`; `Deploy-Playbook.ps1:1044` | React Flow JSON: `{viewport, nodes[], edges[], version}` |
| `sprk_analysisplaybook.sprk_configjson` | Playbook | **No runtime read site located in BFF**. Comment at `Deploy-Playbook.ps1:364`. | `Deploy-Playbook.ps1:618` writes IF present in definition JSON | Playbook-level blob — currently advisory only |
| `sprk_playbooknode.sprk_configjson` | Node | Per-executor `Validate` / `ExecuteAsync` via `context.Node.ConfigJson`; orchestrator extracts `__actionType` at `PlaybookOrchestrationService.cs:867` | `NodeService.CreateNodeAsync:170`; `Deploy-Playbook.ps1:831` | Per-node JSON: `__actionType`, executor-specific fields, routing (destination, widgetType — see `node-routing-config.schema.json`) |

**Runtime read order**:
1. `PlaybookOrchestrationService.cs:244` calls `_nodeService.GetNodesAsync(playbookId)` and reads node rows directly.
2. `sprk_canvaslayoutjson` is consulted ONLY in the LEGACY orchestrator's JIT sync (`AnalysisOrchestrationService.cs:772`). When the legacy path runs, it pre-syncs canvas → node rows so the executor path can pick up nodes. Once nodes exist, the canvas JSON is informational only (UI rehydration on next builder open).
3. `sprk_analysisplaybook.sprk_configjson` is written by Deploy but **no BFF read site has been located** as of 2026-06-26. Treat it as advisory and avoid putting load-bearing config here. (R4 open question §13 line item: confirm with PCF + code-pages grep before R5.)

### What this means for the R4 deploy bug

In the work-branch R4 UAT, the `DAILY-BRIEFING-NARRATE` playbook had been deployed with `sprk_canvaslayoutjson` populated (canvas-only state) but no `sprk_playbooknode` rows. The orchestrator at `PlaybookOrchestrationService.cs:244` got `Length == 0`, logged Legacy mode, called `ExecuteLegacyModeAsync` → `_legacyOrchestrator.ExecutePlaybookAsync` → Index Out Of Range at `AnalysisOrchestrationService.cs:730` (now guarded — see §7). The fix is two-fold: (a) hotfix the IOORE (done, commit `64784a3ba`); (b) actually deploy node rows for the playbook (R4 task 010-013).

---

## 6. Scope arrays are ADVISORY, not enforcing

**Binding rule**: at the BFF runtime layer, scope arrays (`playbook.scopes.{actions,skills,knowledge,tools}` and `node.scopes.{skills,knowledge,tools}`) are advisory — they pre-fetch resources for the executor but **do not gate** which actions or skills can be invoked.

Evidence:
- `ScopeResolverService.ResolvePlaybookScopesAsync:161` loads playbook.ToolIds/SkillIds/KnowledgeIds and returns `ResolvedScopes`. It does NOT cross-check that a node's action matches any of the playbook-scoped Tools.
- The legacy path (`AnalysisOrchestrationService.cs:977`) is the **only** place playbook-level scope arrays gate execution — `ResolvePlaybookScopesAsync` is called once and `scopes` is handed to a sequential tool loop; that loop is bounded by the resolved Tools.
- In NodeBased mode (`PlaybookOrchestrationService.cs:1074`), the orchestrator resolves scopes PER NODE via `ResolveNodeScopesAsync(node.Id, …)` — pulling from `sprk_playbooknode_{skill|knowledge|tool}` N:N relationships, not the playbook-level scopes.
- `PlaybookOrchestrationService.ValidateAsync:285` checks for cycles, missing actions, dependency validity, and duplicate output variables — but NOT scope-array consistency.

**Consequence**: if a node's action references a Skill not in `playbook.scopes.skills`, the BFF will execute the node anyway. **Customers and automation should treat scope arrays as the authoritative declaration of "what this playbook needs," even though the BFF does not enforce it.** Audit tooling (`jps-playbook-audit` skill) is the enforcement mechanism, not the runtime.

`AnalysisAction` (the record at `IScopeResolverService.cs:591-653`) has no `Code` field — Action lookup at runtime is by `Guid` via `ScopeResolverService.GetActionAsync`. Action-code → Guid resolution is a deploy-time concern (Deploy-Playbook.ps1 lines 419-430), not runtime.

---

## 7. Empty-payload contract

R4's `/narrate` endpoint passes empty `DocumentIds[]` because daily-briefing has no document. Whether the runtime tolerates this depends on which mode is selected.

| Mode | Empty `DocumentIds` behaviour | Status |
|---|---|---|
| NodeBased | **Handled correctly.** Node executors consume `context.Document` (may be null) or `context.Parameters` — none dereference `DocumentIds[0]` directly. | Working as designed. |
| Legacy (pre R4 hotfix) | **IndexOutOfRangeException** at `AnalysisOrchestrationService.cs:730` (`request.DocumentIds[0]`). | Closed by R4 hotfix at `:720-728` (commit `64784a3ba`) — returns operator-actionable error chunk before reaching `[0]`. |

**Defense-in-depth gap**: `AnalysisOrchestrationService.cs:117` (the action-based analysis branch in `ExecuteAnalysisAsync`) also indexes `DocumentIds[0]` unguarded. That path is reachable only when `PlaybookId is null AND DocumentIds.Length == 0`, which the endpoint contract for `/api/ai/analysis/*` is supposed to prevent. Treat this as a known follow-up.

`AiAnalysisNodeExecutor.cs:1089` and `DeclineToFindNode.cs:150` both have proper guards. `QueryDataverseNodeExecutor`, `LookupUserMembershipNodeExecutor`, and `CreateNotificationNodeExecutor` use null-safe `Parameters?.TryGetValue` and `FirstOrDefault` patterns.

**The IInvokePlaybookAi contract**: the comment at `InvokePlaybookAi.cs:64-67` is now true:

```csharp
// Construct the orchestration request. Note: the facade does NOT accept
// documentIds today — invoke_playbook callers pass parameters only. The
// orchestration service interprets an empty documentIds array as "no
// document context" (consistent with the existing M365 Copilot adapter path).
```

Pre-hotfix it was half-true (only NodeBased honoured the comment); post-hotfix the legacy fallback emits a clean error chunk → `PlaybookEventType.RunFailed` → `InvokePlaybookAi.cs:123-127` sets `ErrorCode = "PLAYBOOK_INVOCATION_FAILED"` → `DailyBriefingEndpoints.cs:309-321` returns 503 ProblemDetails. The R4 IOORE-class risk is closed; the "intended" path is to deploy the playbook with nodes.

---

## 8. Two parallel orchestrators

There are **TWO orchestrators with overlapping responsibilities**. The boundary is not perfectly clean, and the R4 canonical-truth loop surfaces this as residual architectural debt for R5 to resolve.

| Orchestrator | Owns | Why it exists | Boundary |
|---|---|---|---|
| `PlaybookOrchestrationService` | All NodeBased flows (Path B, Path C, Path A.5); Legacy fall-through via `_legacyOrchestrator` | The main entry. ADR-013 + R6 evolution. | Default for all new playbook dispatch. |
| `PlaybookExecutionEngine` | Chat-summarize flows only (`SessionSummarizeOrchestrator` family) | Predates `PlaybookOrchestrationService`'s alternate-key bypass; carries `sprk_actioncode` lookup for resilience when the FK chain is broken (per `SessionSummarizeOrchestrator.cs:23-58`). | Reachable only via the chat-summarize call sites. |

R4 open question (§13.5 of `01-code-archaeology.md`): when do call sites hit `PlaybookExecutionEngine` instead of `PlaybookOrchestrationService`? **Treat `PlaybookOrchestrationService` as canonical for new work.** Do not add new call sites to `PlaybookExecutionEngine`. R5 should evaluate retiring it once chat-summarize migrates to the alternate-key support in `PlaybookOrchestrationService` (if such support is added).

---

## 9. NodeType (5 values) vs ActionType (31 enum values) — two orthogonal axes

These are not synonyms.

**NodeType** (5 values; option-set on `sprk_playbooknode`; `INodeExecutor.cs:59-91`):

| NodeType | Numeric | Purpose |
|---|---|---|
| AIAnalysis | 100_000_000 | Requires Action; resolves all scopes (skills, knowledge, tools) |
| Output | 100_000_001 | Structural; no Action or scopes |
| Control | 100_000_002 | Structural; no Action or scopes |
| Workflow | 100_000_003 | Future; rule-based, scope TBD |
| DeliverComposite | 100_000_004 | R6 multi-section delivery (ADR-037); no Action or scopes |

**ActionType** (33 enum values; on `sprk_analysisaction.sprk_actiontype` via `sprk_actiontype/sprk_executoractiontype`; `INodeExecutor.cs:97-280`): 0, 1, 2, 10, 11, 12, 20, 21, 22, 23, 24, 30, 31, 32, **33** (`Start` — first-class executor as of R4 2026-06-25; pairs with `StartNodeExecutor`), 40, 41, 42, 50, 51, 52, 60, 70, 80, 90, 100, 110, 120, 130, 140, 141, **142** (`LoadKnowledge` — canvas-only Control node; pairs with `LoadKnowledgeNodeExecutor`; R4 2026-06-26), **143** (`ReturnResponse` — canvas-only Control terminal node; pairs with `ReturnResponseNodeExecutor`; R4 2026-06-26).

**The two axes can be incongruent** — e.g., a `NodeType.Control` node can carry `ActionType.QueryDataverse` via `__actionType` in ConfigJson. The orchestrator accommodates by routing on `actionType` from the action FK if present, else from ConfigJson `__actionType`, else from `NodeType → default` mapping. The `NodeExecutorRegistry` indexes executors **BY `ActionType`**, not NodeType.

**The dispatch axis priority is**: action FK → ConfigJson `__actionType` → NodeType-default. The Deploy script lint pushes every dispatchable node to fall on the first rung (FK).

---

## 10. Known runtime pitfalls (moved from AI-ARCHITECTURE.md)

The 11 historical pitfalls G1-G11 documented in the former `playbook-architecture.md` "Known Pitfalls" section apply unchanged. They are preserved verbatim there (now a redirect — see §11 of this doc) until the next consolidation pass. Highlights relevant to R4 onwards:

- **G1 — Handlebars `??`** is not supported; use `{{default x y}}` (R3 helper).
- **G2 — Renaming an Action's `sprk_actioncode`** silently breaks every playbook that referenced it (FK is by Guid, but deploy scripts hash by code). Rename via successor row, not in-place.
- **G4 — `appnotification.ttlinseconds` MUST be set** to a non-zero value or notifications never appear. Canonical value: `604800` (7 days). Set at `CreateNotificationNodeExecutor.cs:490` per R3.
- **G5 — Membership-aware playbooks** require `LookupUserMembership` (ActionType 52) wired BEFORE the FetchXml query node, with `joinIds` helper to inject the resolved ID set.
- **G6 — Canvas↔server mapping drift** is enforced by CI test (`CanvasServerMappingDriftTests.cs` — exact path to confirm, open question §13.1 of code archaeology). New node types must update both sides.
- **G12 (NEW, R4 owner clarification 2026-06-25)** — Spaarke does NOT use OOB `task` / `email` / `appointment` activity entities. Tasks → `sprk_event` with event-type discriminator; emails → `sprk_communication` with type discriminator; events → `sprk_event`. Every NodeExecutor FetchXml MUST target Spaarke entities, not OOB.

---

## 11. Relationship to other canonical docs

| Question | Read |
|---|---|
| 4-tier AI platform overview, RAG, Cosmos, Safety pipeline, Capability Router | `AI-ARCHITECTURE.md` (now trimmed to overview-only) |
| How does `/narrate` route a playbook via `sprk_playbookconsumer`? | `ai-architecture-consumer-routing.md` |
| Where should this new config field live (Action vs Node vs Playbook)? | `ai-architecture-actions-nodes-scopes.md` |
| How do I deploy a playbook? | `ai-guide-playbook-deploy-recipe.md` |
| JPS schema features (`$ref`, `$choices`, override merge, structured output) | `ai-guide-jps-authoring.md` (trimmed) |
| Maker recipe for `sprk_event` notification playbooks | `PLAYBOOK-AUTHOR-GUIDE.md` |

`playbook-architecture.md` is now a one-line redirect to this doc.
