# Phase 0 desk items I-1 to I-4 (spec FR-12)

> **Task**: 007. **Written**: 2026-11-26.
> **Code base**: this worktree at `0258d8b2c9` (merged with origin/master on 2026-11-26). origin/master has since moved to `152397ab11`. `git diff --name-only HEAD origin/master` touches none of the files cited here (only Office / Communication files changed), so every citation holds on current master too.
> **Paths** are relative to `src/server/api/Sprk.Bff.Api/` unless they start with `.claude/`, `docs/`, `src/dataverse/` or `projects/`.
> **Method**: read-only code reading. Nothing in the tenant, Dataverse, Azure or the BFF was touched. A claim without `file:line` is marked *unverified*. A claim that depends on live state is marked *needs live check*.

## Summary

| Item | Answer |
|---|---|
| I-1 | The profile governs the **MCP catalogue** on `/mcp/legal-operations`. That is the surface Copilot agents see, and the tools it lists call Actions through a facade. The chat loop's `IToolHandler` catalogue (the old `IAnalysisToolHandler`) is governed only if a profile is later applied in Console chat. `IAiToolHandler` is out of scope. **`SideEffectGateAIFunction` does not apply on the MCP path.** |
| I-2 | **Yes**, as long as the ratchet is an escalate-only DATA input to the existing tier rule (`RiskTierResolver`) and Binding-risk check. It is **not** a second gate. Today no policy input exists: `RequiresConfirmation` takes only the side-effect class, Binding risk and dispatch uncertainty. |
| I-3 | **Decided** (owner, 2026-11-25): `sprk_playbookrun` ("AI Run") is the one AI-run record, written only through `PlaybookRunRecordStore` (mcp-r2 task 005). That store is not on master yet. Confirmed on master: the Action path carries no run id, `sprk_playbookid` is still required, and nothing in the BFF writes the table. The session ledger is not the durable record. |
| I-4 | UAC gives one per-user trim after retrieval, `IRetrievalAccessTrim`: the caller's own Dataverse Read on `sprk_document` or the parent record. It applies to **document-index** retrieval. It does **not** apply to the Action path's reference-knowledge grounding. That search is filtered by tenant (plus `system`) and knowledge-source id only. |

---

## I-1 — Which tool surface a profile governs, and whether the chat gate applies on MCP

### Finding

Master has three separate tool surfaces, and none of them references another.

| # | Surface | What uses it | Evidence |
|---|---|---|---|
| 1 | **MCP catalogue**: `McpToolDefinition` classes tagged `[McpArea]`, discovered once at startup and partitioned by area | Copilot / declarative agents through `/mcp/legal-operations`. **This is the surface the Phase 0 spike tools (`spike_*`) join.** | `Api/Mcp/McpToolCatalog.cs:34-42` (definition), `:70-122` (built once, validated), `:125-126` (`ToolsFor(area)`). Route: `Api/Mcp/McpEndpoints.cs:58-65`. The per-request tool set is chosen in `Infrastructure/DI/McpModule.cs:59-73` (`ConfigureSessionOptions`, which runs on every request in stateless mode, `:15-17`) |
| 2 | **`IToolHandler`** (`IAnalysisToolHandler` is a global alias for it). Catalogue rows are `sprk_analysistool`; handlers are found through `ToolHandlerRegistry` | The chat agent loop (Console / Assistant) and the node engine | Interface: `Services/Ai/IToolHandler.cs:29`. Alias: `GlobalUsings.cs:11`. Assembly scan: `Services/Ai/ToolFrameworkExtensions.cs:153-170` (33 implementing classes by grep). Registry: `Services/Ai/ToolHandlerRegistry.cs:25`. Chat projection, exclusively from `sprk_analysistool`: `Services/Ai/Chat/AgentToolCatalogProjector.cs:8-16`, with its deterministic narrowing seam (the `sprk_requiredcapability` gate) at `:283` and handler lookup at `:304` |
| 3 | **`IAiToolHandler`** | The playbook engine only. It has one implementation | `Services/Ai/IAiToolHandler.cs:3-13` ("called by playbook workflows"); `Services/Finance/Tools/FinancialCalculationToolHandler.cs:15`; listing endpoint `Api/Ai/HandlerEndpoints.cs:76` |

**Separation (re-checked):** `grep` for `IAiToolHandler`, `IAnalysisToolHandler`, `ToolHandlerRegistry`, `SideEffectGate`, `ConfirmationPolicy`, `RequiresConfirmation`, `SessionGate` and `SideEffectClass` over `Api/Mcp/**` and `Infrastructure/DI/McpModule.cs` returns **no hits**.

**Does `SideEffectGateAIFunction` apply on the MCP path? No.**
- It is constructed in exactly one place: the chat agent factory. That factory wraps each projected `ToolHandlerToAIFunctionAdapter` whose declared class needs confirmation (`Services/Ai/Chat/SprkChatAgentFactory.cs:806-820`; construction at `:816`).
- The MCP transport hands the SDK `McpServerTool` objects straight from the catalogue (`Infrastructure/DI/McpModule.cs:67-73`). No adapter is involved, so nothing gets wrapped.
- On MCP, side effects are controlled in a different way:
  - **No model-callable write tool.** A tool that is not read-only must be app-only (`_meta.ui.visibility == ["app"]`), or startup fails (`Api/Mcp/McpToolAuthorization.cs:222-228`, rule O-14).
  - The one write tool, `spaarke_complete_decision`, is app-only. It is sent from the decision wizard's explicit Confirm step, and "the server-side checks are the only gate". The host shows no confirmation prompt for a widget-initiated write (`Api/Mcp/LegalOperations/Decisions/CompleteDecisionTool.cs:14-15`, `:19-23`, `:62-70`).
  - The tier that decision would have under the chat gate is **recorded** (`sprk_gatetier`), not used to open a gate. It is computed through `DecisionGateTier`, a session-less pure facade over the chat gate's own tier rule (`Services/Ai/PublicContracts/DecisionGateTier.cs:23-27`, `:37-49`; written at `Services/Signals/DecisionRecordWriter.cs:1763`).
- The chat gate's suspend/resume cannot run without a chat session, so it is not available on the stateless MCP route as built. `SuspendInvocationAsync` throws when the session's ledger marker cannot be written (`Services/Ai/Chat/PendingPlanManager.cs:240-250`).
- For the Phase 0 spike this does not matter. The spike tools are read-only and model-visible (project CLAUDE.md §2). Even if they were wired to the gate, it would not fire: `RequiresConfirmation` returns false for a Read class with Binding risk `None` (`Services/Ai/Chat/PendingPlanManager.cs:172-187`).
- *Needs live check:* whether Copilot shows its own approval card before calling a model-visible, read-only MCP tool. That is host behaviour, not BFF code; the catalogue comment only names the connector's approval card (`Api/Mcp/McpToolCatalog.cs:107`). Covered by the G3 and G1 runs (tasks 021 and 022).

**Which surface the profile governs:**
- **The MCP catalogue.** That is what the agents under test see, and the profile narrows it. ADR-039 allows only this kind of help: "deterministic pre-filtering of the tool list" (`.claude/adr/ADR-039-grounded-execution-closed-catalogs.md:66`). The per-request seam already exists (`Infrastructure/DI/McpModule.cs:59-73`).
- **Behind it, the functions the profile selects are Actions and Bindings**, run through `IActionRunner` behind a `PublicContracts` facade (owner decision 2026-11-25; `notes/run-path-review-2026-11-25.md`). They are not `IToolHandler` rows.
- `IToolHandler` comes into scope only if a profile is applied in Console chat. It has its own narrowing seam (`AgentToolCatalogProjector.cs:283`).
- `IAiToolHandler` is a frozen-engine surface (ADR-039 MUST NOT, `.claude/adr/ADR-039-grounded-execution-closed-catalogs.md:76`), so it is out of scope.

**Phase 1 implication:** narrow the MCP tool list per caller at the existing `McpModule` `ConfigureSessionOptions` seam, as a deterministic pre-filter. Any gated side effect stays app-only behind a server-side Confirm, as `spaarke_complete_decision` is, because MCP has no chat-gate wrapper.

---

## I-2 — Can `RequiresConfirmation` take a policy (ratchet) input without forking the one gate?

### Finding

**Yes, without a fork, provided the ratchet is escalate-only DATA fed into the existing rule. No such input exists today.**

What the one gate consists of on master:
- **The boolean pre-check**: `PendingPlanManager.RequiresConfirmation(ToolSideEffectClass?, BindingRisk, bool dispatchUncertain)`. It has three inputs and no policy parameter (`Services/Ai/Chat/PendingPlanManager.cs:154-187`; its doc says gating comes "exclusively by DECLARED catalog metadata", `:155-158`). It has two production callers:
  - the chat loop's wrap decision (`Services/Ai/Chat/SprkChatAgentFactory.cs:809`);
  - the Binding-risk check on Click/Text dispatch (`Services/Ai/Chat/SessionDispatchOrchestrator.cs:340-341`).
- **The outcome engine**: `ConfirmationPolicyEngine.Evaluate(PolicyEvaluationContext)`. It maps tier × origin × completeness × overlays × ledger state to one `GateDecisionV2` (`Services/Ai/Chat/Gate/ConfirmationPolicyEngine.cs:98-127`). It states that it "builds ON the ONE existing Confirmation Gate … it is NOT a second gate" (`:65-67`). Every field of its context is structural DATA, and model opinion is not expressible (`:6-12`, `:13-59`). Its only caller is `SideEffectGateAIFunction.EvaluatePolicy` (`Services/Ai/Chat/SideEffectGateAIFunction.cs:395-425`).
- **The tier rule**: `RiskTierResolver.Resolve` picks "the stricter of the declared tier and the factor floor (fail-closed escalation, never a down-rank)" (`Services/Ai/Chat/Gate/RiskTierResolver.cs:36-45`). Two callers share it:
  - the chat engine (`ConfirmationPolicyEngine.cs:102`);
  - the session-less decision facade (`Services/Ai/PublicContracts/DecisionGateTier.cs:41`).
- **The risk data**: `GateRiskProfile` (declared tier plus five factors, parsed from the catalog row's `sprk_configuration.riskProfile`; `Services/Ai/Chat/Gate/GateRiskProfile.cs:33-51`, `:68-93`) and `BindingRisk` (`None` / `ConfirmWhenUncertain` / `AlwaysConfirm`; `Services/Ai/PublicContracts/Binding.cs:197-207`).

Why a ratchet fits without forking:
- The design's ratchet is one-way: lower layers "may narrow authority and tighten gates; they can never widen them" (`projects/specialist-legal-agents-r1/design.md:136`).
- That matches the resolver's existing rule: take the stricter value, never lower it (`RiskTierResolver.cs:44`).
- A profile-version floor would be one more escalating input to the same `Resolve`. For the boolean pre-check, it would raise the effective `BindingRisk` (for example to `AlwaysConfirm`) before `RequiresConfirmation` is called.
- Both changes stay inside the existing types, so every caller keeps one rule and one outcome table. `DecisionGateTier` is the precedent: it reaches the same resolver from outside the chat module without copying it (`DecisionGateTier.cs:23-34`).

The limits are stated, not proposed:
- A ratchet that could **lower** a tier or skip the gate would break the resolver's fail-closed invariant. That is a design defect, not an option.
- A gate owned by the profile, outside `PendingPlanManager` / `ConfirmationPolicyEngine` / `RiskTierResolver`, would be a second confirmation mechanism (ADR-039 MUST, `.claude/adr/ADR-039-grounded-execution-closed-catalogs.md:52-54`). This note does not propose one.
- ADR-039's MUST names two inputs: "the tool's `side_effect_class` and the Binding's `risk` — declared in catalog data" (`:52-54`). A profile floor is declared data, but it is a third source. Phase 1's `adr-check` has to confirm that it reads as Path C (compliant: escalate-only catalog-like data) and needs no Path B wording change. *Unverified until that check.*
- The ratchet input reaches the gate only where the gate runs: the chat loop and session dispatch. The MCP path has no gate wrapper (I-1). So on MCP the ratchet can only narrow the tool list, or raise the recorded tier through the `DecisionGateTier`-style facade.

**Phase 1 implication:** add the profile ratchet as an escalate-only floor into `RiskTierResolver` / the effective `BindingRisk`, extending the existing types. Never build a profile-side gate. Have `adr-check` confirm that ADR-039's "declared in catalog data" covers a profile-version input.

---

## I-3 — The durable run record for Action-path runs

### Decided design (owner, 2026-11-25)

Source: `notes/playbook-concept-and-run-record-2026-11-25.md`, "Writer agreed with mcp-r2"; project CLAUDE.md §2–§3.

- **Record:** **`sprk_playbookrun`** ("AI Run") is the one AI-run record for engine, `prompted` Action and `coded` workflow runs.
  - `sprk_playbookid` becomes optional.
  - It gains Action, Binding, profile-version and parent-run lookups, a run kind, and a gate-decision reference.
  - `sprk_playbooknoderun` records coded-workflow steps.
- **Writer:** **`PlaybookRunRecordStore`** (mcp-r2 task 005, `Services/Ai/`, AI module only) is the only writer. It owns the row shape and the fail-safe rule: a failed write logs `playbook_run_persist_failed` and never fails the run.
  - Engine runs call it with run kind `engine`.
  - This program's Phase 1 calls it from the `PublicContracts` facade and the coded-workflow seam (run kind `prompted` / `coded`).
- **Phase 1 columns:** Phase 1 adds the **profile-version** and **gate-decision** columns **to that store**. It never writes the table directly.
- **Open point for Phase 1 design:** a gated side-effect run may need a **"required" write mode** that fails closed when gate attribution cannot persist. The alternative is to record that attribution in the Decision Record. mcp-r2 keeps the write call extensible but builds no mode now.
- **Ledger:** **the ADR-040 session ledger is not the durable run record.** It is "Tier 3 (user-owned, GDPR-erasable, tenant-partitioned)" (`.claude/adr/ADR-040-session-ledger.md:45-46`), so attribution written there can be erased.
- **Decision Record:** decision **authority** goes to the Decision Record, a sibling of `SessionGate` (ADR-040 amendment 2026-10-03). It holds decisions, not runs.

### Confirmed from code (master today)

- **The Action path carries no run id.** `LinearRunContext` has exactly four members:
  - `ConsumerType` (`Services/Ai/LinearConsumers/LinearRunContext.cs:28`);
  - `CorrelationId`, the trace id (`:35`);
  - `TenantId` (`:42`);
  - `KnowledgeSourceIds` (`:56`), added by agreements-r1 task 021.

  It has no run id, caller, profile version, parent run or gate decision (`:21-57`). It is "deliberately minimal" (`:16-19`). The run-path review lists three members; the fourth, `KnowledgeSourceIds`, was added since and is not a run identity.
- **`IActionRunner` returns only the output.** Both overloads return `Task<JsonElement>` (`Services/Ai/LinearConsumers/IActionRunner.cs:40-44`, `:55-59`), so no run handle comes back to the caller.
- **The facade pattern builds the context with type and tenant only.** `InvoiceAi.RunCapabilityAsync` sets `ConsumerType` and `TenantId` and calls `RunAsync` (`Services/Ai/PublicContracts/InvoiceAi.cs:347-355`). That is the seam where Phase 1's `PlaybookRunRecordStore` call goes.
- **Coded workflows carry no run id either.** `CodedWorkflowContext` has `ArgumentsJson`, `UserId` and `ActingUserEmail` only (`Services/Ai/ICodedWorkflow.cs:71-92`).
- **`sprk_playbookid` is still required on master** (`src/dataverse/solutions/SpaarkeMaster/Entities/sprk_playbookrun/Entity.xml:566-570`, `RequiredLevel=required`). The entity's display name is still "Playbook Run" (same file `:3`, `:7`); the "AI Run" relabel is part of the decided change, not yet on master.
- **Nothing writes the table today.**
  - `grep` for `sprk_playbookrun` over `src/server/**/*.cs` finds no hits.
  - The engine keeps its runs in memory: "In-memory run tracking (Phase 1) - replaced with Dataverse in Phase 2" (`Services/Ai/PlaybookOrchestrationService.cs:61-62`).
- **`PlaybookRunRecordStore` is not on master.** `git grep PlaybookRunRecordStore origin/master -- src` returns nothing. It lands with mcp-r2 task 005.

**Phase 1 implication:** depend on mcp-r2 task 005 merging first. Then add the profile-version and gate-decision columns through `PlaybookRunRecordStore` from the facade / coded-workflow seam (a run id passed in or returned there, since `LinearRunContext` has none), and settle the "required" write mode for gated side-effect runs in Phase 1 design.

---

## I-4 — Per-user knowledge trimming UAC provides today

### Finding

**UAC (unified-access-control-r2 task 176, #1511) provides one per-user trim after retrieval: `IRetrievalAccessTrim`.** A retrieved row survives only if the caller can Read its `sprk_document` (or, through `TrimByRecordAsync`, its matter, project, invoice or work assignment), as Dataverse itself decides through user OBO.
- Contract and rationale: `Services/Ai/PublicContracts/IRetrievalAccessTrim.cs:3-31`. It fails closed: no verified caller or a failed read returns no rows (`:28-31`).
- Methods: `:40`, `:50-54`, `:62-66`.
- Parent-record kinds: `:75-82`.
- Batch size: `MaxIdsPerRead = 20` (`Services/Ai/PublicContracts/RetrievalAccessTrim.cs:47`).
- External callers also pass the external document narrowing and the external visibility predicate (`IRetrievalAccessTrim.cs:19-27`).
- Registered scoped: `Infrastructure/DI/AnalysisServicesModule.cs:1416`.

**Where it applies** (constructor or resolve sites found by grep):
- semantic search endpoints (`Api/Ai/SemanticSearchEndpoints.cs:115`, `:248`);
- `DocumentSearchHandler` (`Services/Ai/Handlers/DocumentSearchHandler.cs:115`);
- `KnowledgeRetrievalHandler` (`Services/Ai/Handlers/KnowledgeRetrievalHandler.cs:96`);
- `SemanticSearchToolHandler` (`Services/Ai/Tools/SemanticSearchToolHandler.cs:46`);
- `AnalysisRagProcessor` (`Services/Ai/AnalysisRagProcessor.cs:27`);
- node-engine retrieval (`Services/Ai/Nodes/AiAnalysisNodeExecutor.cs:1000`, `:1117`; `Services/Ai/Nodes/IndexRetrieveNode.cs:317`);
- Insights (`Services/Ai/Insights/InsightsOrchestrator.cs:106`);
- `CommunicationTriageAi` (`Services/Ai/PublicContracts/CommunicationTriageAi.cs:52`);
- `SemanticScopeProvider` (`Services/Ai/SemanticScopeProvider.cs:23`);
- the shared helper `ReadableRagSearch.SearchReadableAsync` (`Services/Ai/ReadableRagSearch.cs:17`).

**Why the trim is needed: the index carries no usable per-user ACL.**
- `RagService` always adds a tenant filter (`Services/Ai/RagService.cs:1172`), plus a parent-entity filter when scoped (`:1303-1307`) and a privilege-group filter (`:1309-1313`).
- No indexer stamps `privilege_group_ids`. `RagIndexingPipeline` sets it to null (`Services/Ai/RagIndexingPipeline.cs:475`), and `RecordSyncJob` writes an empty list (`Services/Jobs/RecordSyncJob.cs:561`). So the privilege filter treats every chunk as public, as the trim's own doc says (`IRetrievalAccessTrim.cs:9-12`, finding A-21).
- *Needs live check* only if someone wants to confirm the live index content. The trim does not depend on it.

**Where it does not apply: the Action path's reference-knowledge grounding.**
- `ActionRunner` grounds an `AllowsKnowledge` Action from `spaarke-rag-references` (`Services/Ai/LinearConsumers/ActionRunner.cs:146-153`, `:263`).
- That search is filtered only by tenant, the shared `system` tenant, and the optional `KnowledgeSourceIds` from `LinearRunContext` (`ActionRunner.cs:298-314`; tenant-or-system rule in `Services/Ai/ReferenceRetrievalService.cs:36-38`, `:301-308`).
- `ActionRunner` has no `IRetrievalAccessTrim`: it is absent from the grep list above.
- This is by design for a firm-wide standards corpus (KNW-001..011). It means **there is no per-user or per-attorney trimming of reference knowledge today**. Scoping is by knowledge-source id only.

**The Action's operand document gets no access check inside the runner.**
- `ContextBinder` passes a caller-supplied `FileDocument` straight through (`Services/Ai/Context/ContextBinder.cs:297-305`).
- On session dispatch, that text comes from the session's own uploaded files (`Services/Ai/Chat/SessionDispatchOrchestrator.cs:870-872`).
- A facade or MCP tool that loads a document for an Action must load it under the caller's access itself.

**Phase 1 implication:**
- Scope an attorney's own knowledge (profile layers L3/L4) by knowledge-source id through `LinearRunContext.KnowledgeSourceIds`, resolved from the caller's own profile. Any matter or document material must go through `IRetrievalAccessTrim`.
- Before Phase 1 relies on per-user privacy for personal knowledge, file as a gap that the reference corpus has no per-user trim.

---

## Deviations and notes for the main session

These go into `notes/decisions.md`. Under this wave's rules the main session writes that file; this task did not.

- **POML steps 6–7 not run here:** the `TASK-INDEX.md` update, the `check-task-status-drift.ps1` run and the `notes/decisions.md` line. The wave rules give them to the main session. Only this POML's `<status>` was set to completed.
- **Correction to `notes/run-path-review-2026-11-25.md`:**
  - `LinearRunContext` now has four members. `KnowledgeSourceIds` (`LinearRunContext.cs:56`) was added by agreements-r1 task 021. It still has no run id.
  - The review's open risk "How `Api/Mcp/**` does this [calls the gate] today was not traced" is now answered: it does not call the gate (I-1).
- **Spec lag:**
  - spec FR-12's I-3 text still says I-3 "compares the options". The question is decided (spec Unresolved Questions line 335; project CLAUDE.md §3).
  - design §8's I-3 line ("Decision Record vs the ADR-040 session ledger") is superseded by the 2026-11-25 run-record decision.
- **origin/master moved** after this worktree's 2026-11-26 merge (`0258d8b2c9` → `152397ab11`). None of the cited files changed.
- **Possible gap to file** (not filed by this task: no write outside the output file): there is no per-user trim on `spaarke-rag-references` reference grounding (I-4). It is not a defect today, because the corpus is firm-wide. It becomes a requirement once attorney-layer knowledge lives in that corpus.
