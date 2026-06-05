# Phase 2 Analysis — Category 7: Node Executors

> **Authored by**: Phase 2 W1 Sub-Agent D
> **Pinned to**: commit `357e6936` (inventory snapshot)
> **HEAD at analysis time**: `12275b10` (zero drift on executor surface — verified)
> **Scope boundary**: executor inventory + ActionType registry audit + DI lifetime; out-of-scope = classifier/search/prompt-builder choice migrations (defer to W2/W3)

---

## §1 Phase 1 baseline (verbatim from inventory.md §2.7 + §7.7)

### §1.1 Inventory §2.7 — 16 (actually 17/18) registered executors via `INodeExecutor` registry

From `c:\tmp\inventory-snapshot.md` lines 273-298: ADR-010 `INodeExecutor` registry with executors discoverable via `NodeExecutorRegistry`. Table listed executors with ActionType assignments (most `(default)`; numeric values 60/70/80/90/100/110/120/130/140 for Foundry + Insights Engine blocks). All Singletons, auto-discovered. Dispatch uniform but executors call HEAVILY into other categories (LLM, search, Dataverse).

### §1.2 Inventory §7.7 — Open question
> "Is the `ActionType` numeric assignment (60/70/80/.../140) ad-hoc or registry-coordinated? Risk of collision across teams."

---

## §2 Empirical reproduction

### §2.1 Executor count at HEAD: **18 confirmed** (corrects inventory's "16")

`Grep ": INodeExecutor"` returned 18 implementation hits (excluding registry interface declaration). Reconciles the inventory's "16 vs 17 vs 18" ambiguity (header text said 16; table listed 18; HEAD confirms 18).

Full list (path → class):

| # | File path (relative to `src/server/api/Sprk.Bff.Api/`) | Class |
|---|---|---|
| 1 | `Services/Ai/Insights/Nodes/ObservationEmitterNodeExecutor.cs` | `ObservationEmitterNodeExecutor` |
| 2 | `Services/Ai/Insights/Nodes/SanitizerNodeExecutor.cs` | `SanitizerNodeExecutor` |
| 3 | `Services/Ai/Nodes/ConditionNodeExecutor.cs` | `ConditionNodeExecutor` |
| 4 | `Services/Ai/Nodes/DeclineToFindNode.cs` | `DeclineToFindNode` |
| 5 | `Services/Ai/Nodes/CreateNotificationNodeExecutor.cs` | `CreateNotificationNodeExecutor` |
| 6 | `Services/Ai/Nodes/CreateTaskNodeExecutor.cs` | `CreateTaskNodeExecutor` |
| 7 | `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` | `AiAnalysisNodeExecutor` |
| 8 | `Services/Ai/Nodes/GroundingVerifyNode.cs` | `GroundingVerifyNode` |
| 9 | `Services/Ai/Nodes/AgentServiceNodeExecutor.cs` | `AgentServiceNodeExecutor` |
| 10 | `Services/Ai/Nodes/EvidenceSufficiencyNode.cs` | `EvidenceSufficiencyNode` |
| 11 | `Services/Ai/Nodes/IndexRetrieveNode.cs` | `IndexRetrieveNode` |
| 12 | `Services/Ai/Nodes/DeliverToIndexNodeExecutor.cs` | `DeliverToIndexNodeExecutor` |
| 13 | `Services/Ai/Nodes/DeliverOutputNodeExecutor.cs` | `DeliverOutputNodeExecutor` |
| 14 | `Services/Ai/Nodes/LiveFactNode.cs` | `LiveFactNode` |
| 15 | `Services/Ai/Nodes/ReturnInsightArtifactNode.cs` | `ReturnInsightArtifactNode` |
| 16 | `Services/Ai/Nodes/QueryDataverseNodeExecutor.cs` | `QueryDataverseNodeExecutor` |
| 17 | `Services/Ai/Nodes/UpdateRecordNodeExecutor.cs` | `UpdateRecordNodeExecutor` |
| 18 | `Services/Ai/Nodes/SendEmailNodeExecutor.cs` | `SendEmailNodeExecutor` |

**ZERO executors declare multi-ActionType `SupportedActionTypes`** — every one of the 18 has a single-element list. The multi-action-type capability of the interface is unused.

### §2.2 ActionType numeric audit — THE HEADLINE FINDING

**The `ActionType` enum IS the central registry**, declared in `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs:78-207`. It is a single-source-of-truth, but it is co-located with the registry interface rather than in a dedicated catalog folder, and **there is NO supporting allocation-tracking document**.

Complete enum audit:

| Numeric | Enum member | Has executor? | Block |
|---|---|---|---|
| 0 | `AiAnalysis` | YES | 0-2 AI primitives |
| 1 | `AiCompletion` | NO (used by JPS playbooks for raw LLM) | 0-2 |
| 2 | `AiEmbedding` | NO | 0-2 |
| 10 | `RuleEngine` | NO | 10-12 computation |
| 11 | `Calculation` | NO | 10-12 |
| 12 | `DataTransform` | NO | 10-12 |
| 20 | `CreateTask` | YES | 20-24 external integration |
| 21 | `SendEmail` | YES | 20-24 |
| 22 | `UpdateRecord` | YES | 20-24 |
| 23 | `CallWebhook` | NO | 20-24 |
| 24 | `SendTeamsMessage` | NO | 20-24 |
| 30 | `Condition` | YES | 30-33 control flow |
| 31 | `Parallel` | NO | 30-33 |
| 32 | `Wait` | NO | 30-33 |
| 33 | `Start` | NO (intentional — "canvas anchor, pass-through") | 30-33 |
| 40 | `DeliverOutput` | YES | 40-41 output |
| 41 | `DeliverToIndex` | YES | 40-41 |
| 50 | `CreateNotification` | YES | 50-51 notification/query |
| 51 | `QueryDataverse` | YES | 50-51 |
| 60 | `AgentService` | YES | 60 Foundry (single value) |
| 70 | `GroundingVerify` | YES | 70-120 Insights Engine Phase 1 (D-P12) |
| 80 | `LiveFact` | YES | 70-120 |
| 90 | `IndexRetrieve` | YES | 70-120 |
| 100 | `EvidenceSufficiency` | YES | 70-120 |
| 110 | `DeclineToFind` | YES | 70-120 |
| 120 | `ReturnInsightArtifact` | YES | 70-120 |
| 130 | `Sanitization` | YES | 130-140 Insights Wave C1 universal-ingest (separate DI module) |
| 140 | `ObservationEmit` | YES | 130-140 |

**Audit findings**:
1. **NO COLLISIONS at HEAD**. The enum is the central registry; collisions would fail the build. `NodeExecutorRegistry.cs:89-97` also has runtime duplicate-detection defense-in-depth.
2. **Block organization is implicit but consistent**: 0-2 AI primitives, 10-12 computation (reserved), 20-29 external integration, 30-39 control, 40-49 output, 50-59 notification/query, 60-69 Foundry, 70-149 Insights Engine.
3. **NO `ACTION-TYPE-REGISTRY.md` exists**. Block-reservation rules and next-available-numeric are implicit. Parallel-project collision risk via concurrent branches is real (e.g., two teams claim 150 on separate worktrees).
4. **10 enum members lack executors**: `AiCompletion`, `AiEmbedding`, `RuleEngine`, `Calculation`, `DataTransform`, `CallWebhook`, `SendTeamsMessage`, `Parallel`, `Wait`, `Start`. Whether reserved-future-use, abandoned, or aspirational is undocumented.

### §2.3 Inbound consumer grep

Single inbound production consumer of `INodeExecutorRegistry`:

- `Services/Ai/PlaybookOrchestrationService.cs` (lines 43, 54, 1068) — `_executorRegistry.GetExecutor(actionType)`. All 18 executors dispatch through this single chokepoint.

### §2.4 DI registration mechanism

- **All 18 executors** registered as `AddSingleton<INodeExecutor, X>()` (multi-binding, no concrete type).
- **16 in** `AnalysisServicesModule.AddNodeExecutors` (lines 403-452), called from inside compound AI gate at line 51.
- **2 in** `InsightsIngestModule.RegisterInsightsIngest` (lines 111-112), registered **UNCONDITIONALLY** with documented Pattern P1 rationale per ADR-030 (lines 101-108).
- `INodeExecutorRegistry` Singleton at `AnalysisServicesModule.cs:308`.
- **Zero Scoped, Zero Transient executors.**

### §2.5 §F.1 Asymmetric registration audit — CLEAN

- **18/18 executors registered unconditionally** at their direct DI-line level.
- 16 are called from inside the compound AI gate; their consumer (`PlaybookOrchestrationService`) is gated the same way → no asymmetry.
- 2 are fully unconditional with matching unconditional transitive deps → no asymmetry.
- **NO `NullXxxNodeExecutor` peers** exist; none are needed.
- **`AgentServiceNodeExecutor` is NOT DI-kill-switched** despite inventory §2.7 label. The kill switch is RUNTIME — injected `AgentServiceClient` throws `FeatureDisabledException`, executor catches it at `AgentServiceNodeExecutor.cs:198-212`, returns structured `NodeOutput.Error(... NODE_AGENT_FEATURE_DISABLED ...)`. This is a distinct "Runtime Kill-Switch Pattern" peer to ADR-030 Null-Object; currently undocumented.

---

## §3 Per-executor decision table — All 18 KEEP

- **KEEP (no concern)**: 15 — executors 1, 2, 3, 4, 5, 6, 8, 10, 12, 13, 14, 15, 16, 17, 18.
- **KEEP-with-CONCERN**: 3 — `AiAnalysisNodeExecutor` (#7; heaviest deps, ADR-010 concrete-vs-interface inconsistency, Cat 1+3 migration impact); `AgentServiceNodeExecutor` (#9; runtime kill-switch pattern undocumented); `IndexRetrieveNode` (#11; Cat 3 search substrate consumer).
- **CONSOLIDATE / DEPRECATE / DELETE**: 0. Each executor represents a distinct ActionType with non-overlapping responsibility.

---

## §4 Cross-cutting findings

### §4.1 ActionType central-registry mechanism

The enum-as-registry pattern is **functionally sound** (compile-time defense + runtime defense-in-depth via `NodeExecutorRegistry.cs:89`) but **process-fragile**: no allocation-tracking doc, no block-reservation contract, no owner-per-block, no deprecation policy for unimplemented enum members. Parallel-project collision risk is real per the `feedback-parallel-execution.md` memory.

### §4.2 Zone A/B segregation at executor level

Executors are Zone-A by REGISTRATION (`InsightsIngestModule`) but Zone-B-touching via DEPS (`LiveFactNode` injects Zone B `ILiveFactResolver`; `ObservationEmitterNodeExecutor` injects Zone B `IObservationMirror`). **Zone-segregated `INodeExecutor` interfaces are NOT needed** — the dispatch is uniform, and dependency injection already enforces Zone boundaries via service interfaces.

### §4.3 ADR-010 lifetime distribution

All 18 Singleton is appropriate. `AiAnalysisNodeExecutor` correctly uses `IServiceProvider.CreateScope()` to resolve Scoped `IToolHandlerRegistry` per execution.

### §4.4 Inventory §2.7 label corrections (PROPAGATE to other sub-agents)

The inventory §2.7 contains **3 non-trivial labeling errors**:

1. **"16 registered concrete executors"** in §2.7 header → actual count is **18**.
2. **"(default)"** ActionType label on first 9 executors → they have explicit numeric values (`AiAnalysis = 0`, `CreateTask = 20`, etc.), not defaults.
3. **"Singleton (kill-switched)"** on `AgentServiceNodeExecutor` → DI is unconditional; kill switch is runtime-only via injected `AgentServiceClient`.

---

## §5 Canonical naming candidates (Q-004 framing)

**No naming canonicalization needed on the executor surface.** The `INodeExecutor` / `NodeExecutorRegistry` / `ActionType` triple is already singular and canonical. Two NEW artifact candidates surfaced:
- `NodeActionTypeRegistry.md` (allocation-tracking doc — does not exist today).
- "Runtime Kill-Switch Pattern" (pattern doc — peer to ADR-030 Null-Object).

---

## §6 Drift report (`357e6936` vs HEAD)

**ZERO drift** on the executor surface:
- 18 executors at HEAD = 18 at snapshot.
- No new `ActionType` enum members.
- No new DI modules registering `INodeExecutor`.
- No collisions discovered.
- `AgentServiceNodeExecutor` kill-switch DI registration unchanged.

---

## §7 Open questions for owner review

- **Q-D-001**: Add `ACTION-TYPE-REGISTRY.md` with block reservations + next-available + owners? (DEFER to W4 — ADR candidate.)
- **Q-D-002**: `AgentServiceNodeExecutor` runtime kill-switch pattern — intentional peer to ADR-030 or transitional? (DEFER to W4 — ADR candidate.)
- **Q-D-003**: 10 unimplemented enum members — reserved, abandoned, or aspirational? (DEFER to owner.)
- **Q-D-004**: `AiAnalysisNodeExecutor` injects concrete + interfaces in same ctor — ADR-010 concrete-by-default violation? (DEFER to W2 Cat 3.)
- **Q-D-005**: ZERO multi-ActionType executors — simplify `SupportedActionTypes` to singular `SupportedActionType`? (DEFER to owner; low priority.)
- **Q-D-006**: Inventory §2.7 corrections — propagate or freeze? (DEFER to owner.)

---

## §8 ADR candidates (Q-005 surface-only)

- **ADR-CAND-D-01**: ActionType Central Registry + Allocation Contract — BIND (process risk is real, cost is low). Mandate block reservations, allocation doc, owner-per-block, deprecation policy.
- **ADR-CAND-D-02**: Runtime Kill-Switch Pattern (peer to ADR-030 Null-Object) — NON-BINDING (descriptive; codifies existing pattern). Distinguish DI kill-switch from runtime kill-switch; document when each applies.
- **ADR-CAND-D-03**: Simplify `SupportedActionType` to singular — LOW PRIORITY.
- **ADR-CAND-D-04**: `ActionType` enum member lifecycle policy — LOW PRIORITY.

---

## §9 W2 handoffs

- **W2 Cat 1 (Intent classification)**: `AiAnalysisNodeExecutor` (#7) is the bridge to legacy `IToolHandlerRegistry`. If Cat 1 consolidation re-routes dispatch, this executor's role may shift. Other 17 NOT impacted.
- **W2 Cat 3 (Search services)**: TWO executors impacted — `AiAnalysisNodeExecutor` (#7, uses `IRagService` + `IRecordSearchService`) and `IndexRetrieveNode` (#11/`spaarke-insights-index` wrapper). `DeliverToIndexNodeExecutor` (#12 outbound indexing) likely separate concern.
- **W3 Cat 5 (Prompts)**: NOT impacted — zero executors construct prompts directly; prompt construction happens inside deps.
- **W4 (cross-cutting)**: §8 ADR candidates + §4.4 inventory corrections to propagate.

---

## Key files referenced (absolute paths)

- `C:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2\src\server\api\Sprk.Bff.Api\Services\Ai\Nodes\INodeExecutor.cs` (lines 78-207 — `ActionType` enum, the central registry)
- `C:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2\src\server\api\Sprk.Bff.Api\Services\Ai\Nodes\NodeExecutorRegistry.cs` (registry impl + duplicate-detection defense)
- `C:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2\src\server\api\Sprk.Bff.Api\Services\Ai\Nodes\INodeExecutorRegistry.cs` (registry interface)
- `C:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2\src\server\api\Sprk.Bff.Api\Infrastructure\DI\AnalysisServicesModule.cs` (lines 51 dispatch gate, 308 registry registration, 401-453 `AddNodeExecutors`)
- `C:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2\src\server\api\Sprk.Bff.Api\Infrastructure\DI\InsightsIngestModule.cs` (lines 80-115 — 2 unconditional executors with P1 rationale)
- `C:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2\src\server\api\Sprk.Bff.Api\Services\Ai\Nodes\AgentServiceNodeExecutor.cs` (lines 198-212 — runtime kill-switch via `FeatureDisabledException` catch)
- `C:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2\src\server\api\Sprk.Bff.Api\Services\Ai\Nodes\AiAnalysisNodeExecutor.cs` (heaviest executor; ADR-010 concrete/interface inconsistency)
- `C:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2\src\server\api\Sprk.Bff.Api\Services\Ai\Nodes\SanitizerNodeExecutor.cs` (sampled — Insights Zone A pattern)
- `C:\code_files\spaarke-wt-ai-spaarke-insights-engine-r2\src\server\api\Sprk.Bff.Api\Services\Ai\PlaybookOrchestrationService.cs` (lines 43, 54, 1068 — single inbound consumer of registry)

---

# Sub-Agent D Final Status Report

1. **Status**: COMPLETE
2. **Output file path**: `projects/bff-ai-architecture-audit-r1/notes/phase2/analysis-node-executors.md`
3. **Executors analyzed**: **18** (corrects inventory's "16" claim)
4. **Decision distribution**: KEEP 15 + KEEP-with-CONCERN 3 + CONSOLIDATE 0 + DEPRECATE 0 + DELETE 0
5. **Drift findings**: ZERO drift on executor surface between snapshot `357e6936` and HEAD `12275b10`.
6. **Cross-cutting observations**: (1) ActionType enum IS the central registry — functionally sound, process-fragile (no allocation doc); (2) 18 (not 16) executors at HEAD; (3) `AgentServiceNodeExecutor` runtime kill-switch is undocumented peer to ADR-030 Null-Object; (4) zero §F.1 violations on executor surface; (5) inventory §2.7 has 3 labeling errors.
7. **Open questions surfaced**: 6 (Q-D-001 through Q-D-006), highest priority = allocation-tracking doc + runtime kill-switch pattern codification.
8. **Recommendations for W2**: Cat 1 sub-agent — `AiAnalysisNodeExecutor` is the bridge to legacy `IToolHandlerRegistry`; Cat 3 sub-agent — `AiAnalysisNodeExecutor` + `IndexRetrieveNode` impacted; Cat 5 sub-agent — NOT impacted.
