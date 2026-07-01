# Spaarke AI Platform Unification R7 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-06-28
> **Source**: [design.md](design.md) v0.6 (552 lines, 6 review rounds)
> **Owner**: ralph.schroeder@hotmail.com
> **Project key**: `spaarke-ai-platform-unification-r7`

---

## Executive Summary

R7 unifies the Spaarke playbook dispatch model. Today, dispatch (which executor handles a node) is split across three storage layers (C# enum, Action lookup table, plain INT column) with no enforcement, producing recurring per-task assumption drift. R7 collapses dispatch to a single typed Choice column on `sprk_playbooknode` (`sprk_executortype`), promotes `sprk_playbookconsumer` to first-class status (consumer-driven authoring), introduces typed config schemas per executor (Invariant 5), updates the Playbook Builder UI for the reform, and migrates all existing playbooks to the new model. R7 also builds the missing `AiCompletionNodeExecutor` (closing R4 `/narrate` end-to-end) and the maker-facing consumer-wiring guide. Scope is dispatch + schema + Playbook Builder UI + documentation cleanup; agent-level concepts (Action Engine, Spaarke Claw, Tool Registry classification, gate resolvers) are out of scope and owned by `ai-spaarke-action-engine-r1` which holds until R7 completes.

---

## Scope

### In Scope

**Dispatch reform** (core foundation):
- Add `sprk_executortype` (Choice) column to `sprk_playbooknode`; populate from global Choice set `sprk_playbookexecutortype` (33 values; created by owner 2026-06-27)
- Delete `sprk_nodetype` (6-value Choice column) — its dispatch role collapses into `sprk_executortype`
- Update `PlaybookOrchestrationService.ExecuteNodeAsync` to read dispatch from `node.sprk_executortype` directly (single-hop)
- Remove the structural fallback ladder (`IsDeployedStartNode` / `IsDeployedLoadKnowledgeNode` / `IsDeployedReturnResponseNode` and the `ExtractActionTypeFromConfig` helper) — nodes now declare dispatch explicitly
- Remove the "Action ActionType is canonical regardless of NodeType" override branch (Insights Engine r2 Wave B legacy)
- Rename C# enum `ActionType` → `ExecutorType` across all BFF references (full refactor)
- Rename `INodeExecutor.SupportedActionTypes` → `INodeExecutor.SupportedExecutorTypes`
- Deprecate and DELETE `AnalysisOrchestrationService.ExecuteAnalysisAsync` (legacy direct-invocation path) and its callers
- Action FK on node becomes optional (required only for prompt-driven executors that need a prompt template)

**`AiCompletionNodeExecutor` build** (closes R4 graduation gate):
- New `AiCompletionNodeExecutor` class implementing `INodeExecutor` for `ExecutorType.AiCompletion = 1`
- Reuses `PromptSchemaOverrideMerger` so node-level Role/Task/Constraints/Output Fields overrides apply (per Q2 KEEP per-node overrides)
- Calls `IOpenAiClient.GetStructuredCompletionRawAsync` with Action's SystemPrompt + OutputSchema + Temperature
- Does NOT require Tool or Document (unlike `AiAnalysisNodeExecutor`)
- Registered in `AnalysisServicesModule.AddNodeExecutors` as Singleton
- xUnit tests (~15-20) covering payload binding, schema rendering, template substitution, temperature override, error paths
- Repoint `BRIEF-NARRATE-TLDR` + `BRIEF-NARRATE-CHANNEL` Action rows to `sprk_executortype = 1` (AiCompletion)
- Verify `/narrate` end-to-end through `DAILY-BRIEFING-NARRATE` playbook

**Typed config schemas (Invariant 5)**:
- Each executor declares a config schema (TypeScript-like shape: field types, defaults, descriptions, required/optional)
- New interface method `INodeExecutor.GetConfigSchema()` (or static attribute alternative — to be picked in implementation)
- New BFF endpoint serving the schema set to PlaybookBuilder canvas
- Schema-to-form renderer in PlaybookBuilder replaces free-form JSON editing

**Consumer-driven model** (`sprk_playbookconsumer` promotion):
- Confirm `sprk_playbookconsumer` schema is current; cite canonical doc `docs/architecture/ai-architecture-consumer-routing.md`
- Wire Playbook Library Code Page modal into all consumer surfaces (spaarke-ai chat, briefing widget, ad-hoc launchers)
- Migrate `chat-summarize` consumer from legacy direct path (`AnalysisOrchestrationService.ExecuteAnalysisAsync`) to playbook dispatch via `IConsumerRoutingService` + `IInvokePlaybookAi`
- Document in spec how a new consumer is wired (covers existing 6 consumers + future ones)

**Playbook Builder UI updates** (existing PlaybookBuilder Code Page; per §11 of design.md):
- Replace Node Type field with `sprk_executortype` Choice selector in model-driven Playbook Node form
- Replace canvas "Node Types" left-panel with full 33-executor categorized selector
- Add description + tier prefix per executor entry (sourced from C# enum XML doc comments)
- Render typed config form per executor (driven by Invariant 5 schemas)
- Promote Action selection from Overview tab to dedicated "Action" tab in node properties
- KEEP Prompt tab + per-node overrides via `PromptSchemaOverrideMerger`
- Replace all `sprk_nodetype` references in canvas state with `sprk_executortype`

**Schema migration**:
- `sprk_analysisaction.sprk_actiontypeid` (lookup field) — DROP
- `sprk_analysisaction.sprk_executoractiontype` (INT column) — DROP
- `sprk_analysisactiontype` lookup table — KEEP (repurposed as decorative maker categorization per Q4)
- Update `Deploy-Playbook.ps1` to write `sprk_executortype` on each node row (no more name-detection or `__actionType` injection workarounds)

**Existing playbook migration** (94 `sprk_playbooknode` rows in spaarkedev1):
- Manual per-node review by owner (per 2026-06-28 confirmation — small scale)
- Build a review tool that lists every node with its current Action lookup value + suggested `sprk_executortype` + space for owner override
- Apply reviewed values via idempotent PowerShell migration script

**Documentation**:
- DELETE outdated sections in R4 canonical-truth docs once superseded by R7 reality:
  - `docs/architecture/ai-architecture-playbook-runtime.md` §5 action lookup precedence ladder
  - `docs/architecture/ai-architecture-playbook-runtime.md` structural-fallback section
  - `docs/architecture/ai-architecture-actions-nodes-scopes.md` 4-Home decision tree (rewrites for new node-dispatch model)
  - `docs/guides/ai-guide-playbook-deploy-recipe.md` Control-flow-name-detection steps
- UPDATE in place where appropriate (don't add SUPERSEDED markers or redirect stubs)
- CREATE `docs/guides/ai-guide-consumer-wiring.md` (maker-facing tutorial for "how to wire a new consumer")
- DO NOT modify `docs/architecture/ai-architecture-consumer-routing.md` (owned by chat-routing-redesign-r1 follow-on)
- DO NOT create `docs/data-model/sprk_playbookconsumer.md` (owned by chat-routing-redesign-r1 follow-on)

**Skill updates** (developer-facing tooling):
- Update `.claude/skills/jps-action-create`, `jps-playbook-design`, `jps-playbook-audit`, `jps-validate`, `jps-scope-refresh` to reflect node-first dispatch model
- Skills validate against `sprk_executortype` not Action lookup
- Skill body documents the WHY (R3.1 history) so future authors learn the lesson

### Out of Scope

**Action Engine R1 territory** (`ai-spaarke-action-engine-r1` owns; project holds until R7 ships):
- "Action" concept (Action → Playbook FK), `ActionTemplate`, `ActionInstance`, `ActionRun`, `Monitor` entities
- Tool Registry classification (Deterministic / AI / Hybrid; CostClass; LatencyClass; PhaseRestrictions)
- Three meta-tools: `FindResources`, `GetResourceDetail`, `InvokeResource`
- `IGateResolver` (EthicsCritical / MeaningCritical / FinalDelivery / EngagementAcceptance / TeamSelection / Custom) + `sprk_gate_approval` entity
- Phase deny-tools enforcement at `IToolHandlerRegistry` dispatch
- Three starter Action Templates (Summarize Matter / Weekly Task Digest / Find Similar Matters)
- "Spaarke Claw" branding + customer-facing agent UX
- Agent concept (consumer-of-playbooks abstraction)

**Polished maker UX (deferred to Action Engine or future work)**:
- Mega menu with fuzzy search across executors + Tools + scopes
- AI-assisted authoring copilot inside the canvas
- Templates browser ("clone Summarize playbook")
- Quick-start wizards
- Customer-facing playbook tutorials (beyond the consumer-wiring guide)

**Multi-tenant + future scope**:
- Multi-tenant rollout (R7 ships to spaarkedev1 only; customer-tenant migration handled separately)
- Per-user Action variants (Q11 — Action Engine territory)
- Multi-tenant Playbook Library scoping (Q13 — Action Engine territory)
- Cross-tenant routing rules

**External documentation work**:
- Updates to `docs/architecture/ai-architecture-consumer-routing.md` (chat-routing-redesign-r1 follow-on owns)
- `docs/data-model/sprk_playbookconsumer.md` data model entry (different ownership)
- Moving `playbookconsumer-matchconditions.schema.json` from project-local to `docs/data-model/` (different ownership)

**Backward compatibility**:
- NO transition mode (Q6 RESOLVED: backward-compat is NOT a concern). No legacy compatibility shim. Old dispatch paths get DELETED.

### Affected Areas

**BFF source**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs` — enum rename + new `GetConfigSchema()` method
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/NodeExecutorRegistry.cs` — dispatch by new column
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiCompletionNodeExecutor.cs` — NEW
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/*NodeExecutor.cs` — each adds `GetConfigSchema()`
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs` — dispatch simplification + structural fallback removal (~150 LOC delete)
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisActionService.cs` — Action read path simplifies (no ActionType from lookup)
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` — DELETE `ExecuteAnalysisAsync` + cascading dead code
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` — register new AiCompletionNodeExecutor; cleanup deprecated registrations
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` — chat-summarize migration to consumer routing
- New endpoint: `GET /api/ai/playbook-builder/executor-config-schemas` (or similar) — serves typed config schemas to canvas

**PCF / Code Page**:
- `src/client/code-pages/PlaybookBuilder/src/**` — Node Types panel update; Action tab; Executor selector; typed-config-form renderer
- `src/client/code-pages/PlaybookBuilder/src/components/properties/*` — replace free-form JSON editor with typed forms per executor
- `src/client/code-pages/PlaybookLibrary/src/**` — wire into consumer surfaces
- Playbook Node model-driven form (Power Apps) — replace Node Type field with Executor Type

**Dataverse**:
- `sprk_playbooknode` — ✅ add Choice column `sprk_executortype` (DONE 2026-06-27 by owner) — ✅ delete Choice column `sprk_nodetype` (DONE 2026-06-28)
- `sprk_analysisaction` — drop fields `sprk_actiontypeid` (lookup), `sprk_executoractiontype` (INT) (Wave 4 — REMAINING)
- `sprk_playbooknode` rows (94 in spaarkedev1) — populate new column via manual-review backfill (Wave 5 — REMAINING)

**Documentation**:
- `docs/architecture/ai-architecture-playbook-runtime.md` (DELETE sections + UPDATE)
- `docs/architecture/ai-architecture-actions-nodes-scopes.md` (MAJOR UPDATE)
- `docs/guides/ai-guide-playbook-deploy-recipe.md` (UPDATE)
- `.claude/constraints/bff-extensions.md` §G (UPDATE)
- `docs/guides/JPS-AUTHORING-GUIDE.md` (MAJOR UPDATE)
- `docs/guides/PLAYBOOK-AUTHOR-GUIDE.md` (MAJOR UPDATE)
- `docs/guides/ai-guide-consumer-wiring.md` (CREATE — R7-owned maker tutorial)

**Skills**:
- `.claude/skills/jps-action-create/SKILL.md` (REWRITE)
- `.claude/skills/jps-playbook-design/SKILL.md` (REWRITE)
- `.claude/skills/jps-playbook-audit/SKILL.md` (REWRITE)
- `.claude/skills/jps-validate/SKILL.md` (REWRITE)
- `.claude/skills/jps-scope-refresh/SKILL.md` (MINOR UPDATE)

**Scripts**:
- `scripts/dataverse/Migrate-PlaybookNodes-to-ExecutorType.ps1` (NEW)
- `scripts/dataverse/Review-PlaybookNodes-Dispatch.ps1` (NEW — owner manual-review tool)
- `Deploy-Playbook.ps1` (UPDATE — writes node executor type explicitly)

---

## Requirements

### Functional Requirements

#### Data model + schema (FR-01 to FR-06)

**FR-01**: ✅ **DONE 2026-06-27 by owner** — Schema portion. Add `sprk_executortype` (Choice column) on `sprk_playbooknode`. Choice draws values from existing global Choice set `sprk_playbookexecutortype`. Column required (no null) for any new node row written. Existing rows populated via FR-19 backfill (still pending). — **Acceptance** (schema): Dataverse describe shows column on entity ✅; values match C# enum ✅. **Required-flag** enforcement deferred to Wave 2 once backfill (FR-19) completes — until then column is nullable for transitional safety.

**FR-02**: DELETE `sprk_nodetype` Choice column on `sprk_playbooknode`. Split into two parts:
- **Schema removal** — ✅ **DONE 2026-06-27 by owner** (column removed from Dataverse entity definition)
- **Code/UI/skill cleanup** — REMAINING. Grep all references to `sprk_nodetype` in `src/`, `.claude/`, `docs/`, and `tests/` and update to `sprk_executortype`. Includes: PlaybookBuilder canvas state, deserialization code, NodeService.cs, any test fixtures that set Node Type, all skill bodies that reference the field.
— **Acceptance**: column removed from Dataverse ✅; post-Wave-2 grep for `sprk_nodetype` (literal string) in `src/server/api/Sprk.Bff.Api/`, `src/client/code-pages/PlaybookBuilder/`, `.claude/skills/jps-*/`, and `docs/architecture/ai-architecture-*` returns zero hits (except historical commit messages). C# enum `NodeType` may survive as an internal graph-traversal hint per design.md §3 (not load-bearing for dispatch); spec scope does NOT mandate its removal but doesn't preserve it either.

**FR-03**: DELETE `sprk_analysisaction.sprk_actiontypeid` (lookup field). The Action no longer carries dispatch identity. — **Acceptance**: field removed from Dataverse; Action read path (AnalysisActionService) no longer references it.

**FR-04**: DELETE `sprk_analysisaction.sprk_executoractiontype` (INT column). — **Acceptance**: field removed; no code reads it.

**FR-05**: KEEP `sprk_analysisactiontype` (lookup table). Decorative role (maker categorization). `sprk_executoractiontype` field on lookup table rows remains but is documented as advisory only (not load-bearing). — **Acceptance**: table preserved; doc note added that runtime ignores the field.

**FR-06**: Action FK on node (`sprk_actionid`) becomes optional. Required ONLY for prompt-driven executors (AiAnalysis, AiCompletion, AiEmbedding) — enforced at executor validation, not at schema level. Pure executors (Condition, Start, ReturnResponse, EntityNameValidator, CreateNotification, QueryDataverse, LookupUserMembership, etc.) prohibit Action FK at validation. — **Acceptance**: schema allows null; each executor's `Validate()` method enforces FK presence/absence per its semantics.

#### Dispatch + orchestrator (FR-07 to FR-11)

**FR-07**: `PlaybookOrchestrationService.ExecuteNodeAsync` reads dispatch from `node.sprk_executortype` directly. Lookup-chain (`node.actionid → Action.actiontypeid → lookup_row.executoractiontype`) is removed from runtime. — **Acceptance**: dispatch path is single-hop; unit test confirms no fallback to Action lookup; integration test runs DAILY-BRIEFING-NARRATE end-to-end via the new path.

**FR-08**: REMOVE structural fallback ladder. Delete: `IsDeployedStartNode`, `IsDeployedLoadKnowledgeNode`, `IsDeployedReturnResponseNode`, `ExtractActionTypeFromConfig` helpers (PlaybookOrchestrationService.cs lines ~863-1079). — **Acceptance**: code grep shows zero references; build passes; tests pass.

**FR-09**: REMOVE "Action ActionType is canonical regardless of NodeType" override branch at PlaybookOrchestrationService.cs lines 1241-1278 (Insights Engine r2 Wave B legacy). — **Acceptance**: code removed; integration tests for Insights pipeline pass on new dispatch path.

**FR-10**: RENAME C# enum `ActionType` → `ExecutorType` across all BFF references. Full refactor (~1000+ references). `INodeExecutor.SupportedActionTypes` → `SupportedExecutorTypes`. `NodeExecutorRegistry` dispatch by `ExecutorType`. — **Acceptance**: grep for `ActionType` in `src/server/api/Sprk.Bff.Api/` returns zero hits (except historical comments); build passes; all tests pass; no behavior change.

**FR-11**: DELETE `AnalysisOrchestrationService.ExecuteAnalysisAsync` (legacy direct invocation path). Audit + migrate all callers to `PlaybookOrchestrationService.ExecuteAsync` via degenerate 3-node playbook OR consumer-routing dispatch. — **Acceptance**: method removed; grep for `ExecuteAnalysisAsync` returns zero hits; chat-summarize (FR-17) and any other identified callers migrated; integration tests green.

#### AiCompletionNodeExecutor (FR-12 to FR-15)

**FR-12**: NEW `AiCompletionNodeExecutor` class implementing `INodeExecutor` for `ExecutorType.AiCompletion = 1`. Located at `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiCompletionNodeExecutor.cs`. Pattern: read Action's SystemPrompt + OutputSchema + Temperature; apply node-level overrides via `PromptSchemaOverrideMerger`; resolve template variables from scope; call `IOpenAiClient.GetStructuredCompletionRawAsync`; parse response to JsonElement; bind to `node.OutputVariable`. — **Acceptance**: executor compiles; class structure mirrors EntityNameValidatorNodeExecutor (sibling pattern); registered in DI module.

**FR-13**: AiCompletionNodeExecutor validation: requires Action FK (must source the prompt template); does NOT require Tool; does NOT require Document. Validation errors are surfaced via `NodeValidationResult` for fast-fail. — **Acceptance**: unit tests cover all validation paths (missing Action, malformed JPS, missing required output schema, etc.).

**FR-14**: AiCompletionNodeExecutor xUnit tests (~15-20 tests) at `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Nodes/AiCompletionNodeExecutorTests.cs`. Cover: payload binding, schema rendering, template substitution, temperature override (from Action vs from config), per-node prompt override merging, error paths (missing prompt, malformed JSON, LLM error). — **Acceptance**: tests pass; coverage >85% of executor LOC; mocked `IOpenAiClient` in tests.

**FR-15**: Update `BRIEF-NARRATE-TLDR` + `BRIEF-NARRATE-CHANNEL` Action rows to dispatch via AiCompletion. With FR-07 in place, this means setting `node.sprk_executortype = 1` (AiCompletion) on the nodes referencing these Actions in `DAILY-BRIEFING-NARRATE` playbook. Verify `/narrate` end-to-end via Daily Briefing widget UAT in spaarkedev1. — **Acceptance**: `/narrate` returns valid narrative response; LLM output matches structured schema; no 503; closes R4 graduation gate.

#### Typed config schemas (FR-16)

**FR-16**: Each `INodeExecutor` implementation declares a typed config schema. Mechanism: new `INodeExecutor.GetConfigSchema()` method returning a TypeScript-like schema descriptor (field types, required/optional, defaults, descriptions). New BFF endpoint `GET /api/ai/playbook-builder/executor-config-schemas` returns the full registry of schemas. PlaybookBuilder canvas consumes the schemas to render typed forms (per FR-22). — **Acceptance**: endpoint exists; all 33 executors return schemas; PlaybookBuilder renders typed forms for at least 5 executors (AI Analysis, AI Completion, Condition, EntityNameValidator, CreateNotification) in initial implementation; remaining executors get schemas in a follow-on wave but DO have a schema (even if empty).

#### Consumer-driven model (FR-17 to FR-20)

**FR-17**: Migrate `chat-summarize` consumer from legacy direct path to playbook dispatch. Today `SessionSummarizeOrchestrator` calls `AnalysisOrchestrationService.ExecuteAnalysisAsync` directly. Target: dispatch via `IConsumerRoutingService.ResolveAsync("chat-summarize")` + `IInvokePlaybookAi.InvokePlaybookAsync`. — **Acceptance**: `chat-summarize` consumer row in `sprk_playbookconsumer` table; SessionSummarizeOrchestrator uses Path A.5; integration test confirms chat summarization works end-to-end through playbook dispatch.

**FR-18**: Wire Playbook Library Code Page modal into consumer surfaces. Today the modal exists but isn't routed from consumers (spaarke-ai chat, briefing widget, ad-hoc launchers). Target: every consumer surface can launch the Library to browse + invoke playbooks. — **Acceptance**: at least 3 consumer surfaces (spaarke-ai chat, briefing widget, one ad-hoc launcher) have a "Browse Playbooks" affordance that opens the Library modal; modal lists every playbook with its consumer mapping; clicking a playbook invokes it through proper Path A.5 routing.

**FR-19**: Manual per-node review backfill of `sprk_executortype` for all 94 existing `sprk_playbooknode` rows in spaarkedev1. Build review tool `scripts/dataverse/Review-PlaybookNodes-Dispatch.ps1` that lists every node with: current Action lookup value, suggested `sprk_executortype`, node name, playbook name, owner-decision field. Owner reviews + sets each value. Idempotent migration script `scripts/dataverse/Migrate-PlaybookNodes-to-ExecutorType.ps1` writes reviewed values. — **Acceptance**: review tool produces complete list; owner-decision applied to every row; migration script runs cleanly; post-migration audit confirms no node has null `sprk_executortype`.

**FR-20**: Update `Deploy-Playbook.ps1` (or successor) to write `node.sprk_executortype` explicitly on every node deploy. No more name-detection workarounds; no more `__actionType` injection in `configJson`. Validates against `sprk_playbookexecutortype` Choice values (33 known values + future). Lint rejects deployment with unknown executor type. — **Acceptance**: deploy script writes the column on every node; lint test fires on invalid executor type; existing playbook deploys (Daily Briefing 5 playbooks, Insights pipeline ~5 playbooks, chat playbooks, others) all redeploy cleanly through new script.

#### Playbook Builder UI updates (FR-21 to FR-27 — per §11 of design.md)

**FR-21**: Model-driven Playbook Node form — replace Node Type field with Executor Type Choice selector. Selector lists all 33 values from `sprk_playbookexecutortype` global Choice; default to a sensible value (e.g., AiCompletion) on new-row create. — **Acceptance**: form opens without Node Type field; Executor Type dropdown shows 33 values; save persists the value.

**FR-22**: Canvas "Node Types" left panel — replace current ~11 named tiles with full 33-executor categorized selector. Categories (6 tiers using integer prefixes): AI (0-9), Compute (10-19), Mutations (20-29), Control (30-39), Delivery (40-49), Capability (50+). Each entry shows: tier prefix + name + 1-line description (sourced from C# enum XML doc comment or parallel metadata source). — **Acceptance**: canvas left panel shows 33 entries in 6 tiers; each entry has name + description; drag-drop creates node with correct `sprk_executortype`.

**FR-23**: Typed config form per executor — driven by FR-16 schemas. Canvas reads each executor's schema; renders typed form (text inputs, dropdowns, JSON sub-editors as needed); validates input against schema; replaces free-form JSON editing of `sprk_configjson`. — **Acceptance**: canvas renders typed form for at least 5 executors initially; form-state syncs to `sprk_configjson` (legacy field kept for backward compat with deployed playbook records); per-field validation works.

**FR-24**: Promote Action selection from Overview tab to dedicated **Action tab** in node properties. Action tab includes: Action lookup dropdown, Executor Type Choice selector. Side-by-side so maker sees the relationship. — **Acceptance**: Overview tab no longer shows Action; new Action tab exists; tab order: Overview, Action, Prompt, Skills, Knowledge, Tools, Configuration.

**FR-25**: KEEP existing Prompt tab + per-node overrides via `PromptSchemaOverrideMerger`. Confirm wiring still works after dispatch reform. Prompt tab UI unchanged. — **Acceptance**: per-node Role/Task/Constraints/Output Fields overrides still apply to runtime prompt; UAT confirms.

**FR-26**: Replace `sprk_nodetype` references in canvas state with `sprk_executortype`. Includes: node serialization (`@spaarke/legal-workspace/playbook-builder/types`), canvas store, graph traversal helpers, and any per-node decision logic that branched on Node Type. — **Acceptance**: grep returns zero `sprk_nodetype` references in `src/client/code-pages/PlaybookBuilder/`; tests pass; canvas serializes new schema correctly.

**FR-27**: Canvas should not silently fail when encountering unknown executor types (e.g., a future executor not yet shipped). Behavior: show node with "Unknown Executor Type {N}" warning state and disable editing until user picks a known executor type. — **Acceptance**: invalid executor type shows warning state; known types render normally.

#### Documentation (FR-28 to FR-31)

**FR-28**: DELETE outdated R4 canonical-truth content once R7 lands. Specific sections (enumerated at Wave 6 execution time, not in spec, per discipline-not-enumeration default):
- `docs/architecture/ai-architecture-playbook-runtime.md` §5 (action lookup precedence ladder)
- `docs/architecture/ai-architecture-playbook-runtime.md` structural-fallback section
- `docs/architecture/ai-architecture-actions-nodes-scopes.md` 4-Home decision tree (rewrites for new model)
- `docs/guides/ai-guide-playbook-deploy-recipe.md` Control-flow name-detection workaround steps
- Other sections discovered during Wave 6 audit
— **Acceptance**: Wave 6 PR shows the specific deletions; no SUPERSEDED markers; no redirect stubs; outdated content REMOVED.

**FR-29**: UPDATE `.claude/constraints/bff-extensions.md` §G (config boundary). Reframe to describe new model: node carries dispatch decision; Action carries prompt (when prompt-driven); lookup table is decorative. — **Acceptance**: §G updated; PR review confirms new framing.

**FR-30**: UPDATE `docs/guides/JPS-AUTHORING-GUIDE.md` + `docs/guides/PLAYBOOK-AUTHOR-GUIDE.md` to reflect new authoring model (node-first dispatch; Action is prompt template). — **Acceptance**: guides updated; no contradictions with R7 architecture; PR review confirms.

**FR-31**: CREATE `docs/guides/ai-guide-consumer-wiring.md` (NEW — maker-facing tutorial). Covers: what a consumer is, how to add one, the `ConsumerTypes.cs` + `sprk_playbookconsumer` row + endpoint pattern, examples from existing consumers, troubleshooting. Cite `docs/architecture/ai-architecture-consumer-routing.md` for runtime details. — **Acceptance**: doc exists; covers all 6 existing consumers + the R7-added chat-summarize migration as case study; PR review confirms readability for non-developer makers.

#### Skill updates (FR-32 to FR-33)

**FR-32**: REWRITE jps-action-create, jps-playbook-design, jps-playbook-audit, jps-validate skills to reflect node-first dispatch model. Each skill body explicitly references the dispatch reform; validates against `node.sprk_executortype` not Action lookup; cites the WHY (§3.1 history) so future authors understand. — **Acceptance**: all 4 skill files updated; spec includes brief context for each rewrite.

**FR-33**: MINOR UPDATE jps-scope-refresh to reflect renamed enum + new schema. — **Acceptance**: file updated; scope catalog generation accurate.

### Non-Functional Requirements

**NFR-01**: BFF publish-size impact ≤ +2 MB (compressed). R7 adds AiCompletionNodeExecutor + config-schema endpoint + removes legacy direct-path code. Net size impact should be small or negative. Measure per `.claude/constraints/azure-deployment.md` "BFF Publish-Size Per-Task Verification Rule (NFR-01)." — **Acceptance**: per-task publish-size verified; cumulative ceiling 60 MB not breached; baseline ~46 MB.

**NFR-02**: No new HIGH-severity CVE from R7 changes. — **Acceptance**: `dotnet list package --vulnerable --include-transitive` reports no new HIGH; pre-existing transitive CVEs (Kiota 1.21.x) carried forward unchanged.

**NFR-03**: Dispatch latency: single-hop `node.sprk_executortype` read is <1ms cached, <10ms uncached (Dataverse Web API roundtrip). No regression vs current lookup-chain dispatch. — **Acceptance**: micro-benchmark confirms; integration test 95th percentile dispatch <10ms.

**NFR-04**: Consumer routing cache: 5-min TTL preserved per `docs/architecture/ai-architecture-consumer-routing.md` §7. Invalidation hook NOT in R7 scope (deferred to R8+). — **Acceptance**: behavior unchanged from chat-routing-redesign-r1; documented in canonical doc.

**NFR-05**: Test coverage for new code (AiCompletionNodeExecutor + dispatch path): >85% line coverage. Migration scripts: idempotent + dry-run mode. — **Acceptance**: dotnet coverage report; manual idempotent rerun confirmed.

**NFR-06**: Backward compatibility — NOT a concern (per Q6 RESOLVED). Big-bang cutover. — **Acceptance**: no compatibility shims added; old paths fully removed.

**NFR-07**: Rollout scope — spaarkedev1 (dev → test → prod) only. Customer tenant migration handled separately under different ownership. — **Acceptance**: R7 ships only to Spaarke-internal environments; customer migration plan documented as out-of-scope dependency.

**NFR-08**: Documentation discipline — DELETE, don't deprecate. Every overlapping doc has explicit disposition: DELETE (remove file or section) OR UPDATE (rewrite in place). No SUPERSEDED markers, no redirect stubs, no archive folders. PR review gates documentation drift. — **Acceptance**: post-R7 grep for "deprecated" / "superseded" in `docs/` shows no new instances.

---

## Technical Constraints

### Applicable ADRs

- **ADR-010** — DI Minimalism. AiCompletionNodeExecutor registration follows the existing module pattern (Singleton, no extra abstraction layers).
- **ADR-013** — BFF AI Architecture. R7 reform aligns with the facade principle; `IInvokePlaybookAi` triangle stays canonical. R7 deepens the existing model rather than introducing a new dispatch surface.
- **ADR-014** — Caching. `IConsumerRoutingService` cache semantics unchanged (5-min TTL per docs/architecture/ai-architecture-consumer-routing.md §7).
- **ADR-029** — BFF Publish Hygiene. Per-task publish-size + CVE verification on every R7-touching task.
- **ADR-037** — Multinode Output Composition. R7 dispatch reform does NOT affect composite output semantics; `DeliverComposite` (ExecutorType=42) continues to work as today.

### MUST Rules

- ✅ **MUST** dispatch from `node.sprk_executortype` directly — no fallback to Action lookup chain
- ✅ **MUST** validate Action FK presence/absence at executor's `Validate()` method (prompt-driven require; pure prohibit)
- ✅ **MUST** preserve per-node prompt overrides via `PromptSchemaOverrideMerger` (Q2 KEEP)
- ✅ **MUST** support the 33 existing Choice values in `sprk_playbookexecutortype` plus future additions via codegen/sync mechanism
- ✅ **MUST** update Deploy-Playbook.ps1 to write node executor type explicitly (no name-detection)
- ❌ **MUST NOT** add SUPERSEDED markers, deprecation banners, or redirect stubs to outdated docs — DELETE instead
- ❌ **MUST NOT** preserve any legacy direct-invocation path (`ExecuteAnalysisAsync` and all callers go away)
- ❌ **MUST NOT** ship transition mode / backward-compat shim
- ❌ **MUST NOT** modify `docs/architecture/ai-architecture-consumer-routing.md` (different ownership)
- ❌ **MUST NOT** introduce mega menu / fuzzy search / AI-assisted authoring (Action Engine scope)

### Existing Patterns to Follow

- AiCompletionNodeExecutor implementation pattern: model on `EntityNameValidatorNodeExecutor` (R4 sibling — simple ConfigJson read + per-execution Validate + ILogger only, no Scoped deps, Singleton registration). See `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/EntityNameValidatorNodeExecutor.cs`.
- Consumer routing: model on the R4 `/narrate` endpoint case study at `src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs:201-374`.
- Typed config schema declaration: follow `.claude/patterns/ai/node-executor-authoring.md` once updated.
- Migration script pattern: model on `scripts/dataverse/Add-EntityNameValidatorNodeTypeOption.ps1` (R4-era Web API + PublishXml).

---

## Success Criteria

1. [ ] Every `sprk_playbooknode` row in spaarkedev1 has `sprk_executortype` populated and accurate (verified by post-migration audit script) — Verify: SQL query `SELECT count(*) FROM sprk_playbooknodes WHERE sprk_executortype IS NULL` returns 0
2. [ ] `PlaybookOrchestrationService.ExecuteNodeAsync` reads dispatch from `node.sprk_executortype` only (no fallback ladder, no Action override) — Verify: code inspection + integration tests for Daily Briefing + Insights pipeline + chat
3. [ ] Every AI dispatch goes through `PlaybookOrchestrationService.ExecuteAsync` (no remaining callers of legacy `ExecuteAnalysisAsync`) — Verify: `grep` for legacy method returns zero hits
4. [ ] `AiCompletionNodeExecutor` exists, is registered, and handles prompt-only LLM calls. `/narrate` works end-to-end through `DAILY-BRIEFING-NARRATE` — Verify: spaarkedev1 UAT — open Daily Briefing widget → `/narrate` returns narrative response with TL;DR + per-channel bullets
5. [ ] PlaybookBuilder canvas shows "Executor Type" dropdown with 33 values + descriptions + tier grouping on every AI node — Verify: open PlaybookBuilder → drag node → see typed selector
6. [ ] PlaybookBuilder canvas renders typed config forms for at least 5 executors (AI Analysis, AI Completion, Condition, EntityNameValidator, CreateNotification) — Verify: drop each → see typed form
7. [ ] Action authoring surface (Power Apps form) — simplified to prompt + schema + temperature only — Verify: open sprk_analysisaction form → no Action Type field, no Executor Action Type field
8. [ ] Canonical-truth docs reflect new model. R4 docs reviewed; outdated sections DELETED, current sections UPDATED. Single source of truth — Verify: PR review confirms no SUPERSEDED markers in `docs/`; grep returns no overlap (e.g., dispatch ladder mentioned in two docs)
9. [ ] Skill bodies (jps-action-create, jps-playbook-design, jps-playbook-audit, jps-validate, jps-scope-refresh) align with new model — Verify: skill file review; each cites the WHY (§3.1 history) so future authors learn
10. [ ] Deploy-Playbook.ps1 sets executor type per node; no more name-detection hacks; no more `__actionType` injection workarounds — Verify: script source review; redeploy of existing playbooks confirms clean
11. [ ] C# enum + Choice set kept in lockstep via codegen or CI check — Verify: PR check fires if enum grows without Choice update
12. [ ] Documentation tech debt list (§6 of design.md) removed or superseded — no dual content — Verify: end-of-project audit
13. [ ] `docs/guides/ai-guide-consumer-wiring.md` exists and covers all 6 consumers + chat-summarize case study — Verify: doc review by non-developer maker
14. [ ] `chat-summarize` consumer routes through `IConsumerRoutingService` (Path A.5) — Verify: integration test
15. [ ] Playbook Library wired into ≥3 consumer surfaces — Verify: manual click-through

---

## Dependencies

### Prerequisites

**Already complete** (in spaarkedev1):
- ✅ Global Choice set `sprk_playbookexecutortype` (33 values) — DONE 2026-06-27 by owner
- ✅ `sprk_executortype` Choice column added on `sprk_playbooknode` — DONE 2026-06-27 by owner
- ✅ `sprk_nodetype` Choice column removed from `sprk_playbooknode` — DONE (confirmed by owner 2026-06-28)

**Active gates** (holds before R7 work begins):
- R4 branch (`work/spaarke-daily-update-service-r4`) holds open until R7 ships (per R4 graduation decision 2026-06-28)
- Action Engine R1 holds at Phase 0 spike until R7 ships (Q14 confirmed 2026-06-28)

### External Dependencies

- `docs/data-model/sprk_playbookconsumer.md` data model entry — eventually created by chat-routing-redesign-r1 follow-on (different ownership; R7 does not block on it but will reference it once exists)
- `docs/architecture/ai-architecture-consumer-routing.md` updates from chat-routing-redesign-r1 follow-on (not blocking; R7 references current version)
- Power Platform Maker portal for any Choice/Choice-set/Lookup edits (owner manual operations)

---

## Owner Clarifications

Captured across 6 design.md review rounds (2026-06-26 through 2026-06-28) + spec interview 2026-06-28.

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Action-FK auto-synthesis | Should consumers be able to dispatch by `actionCode` alone with runtime-synthesized playbook? | NO — uniformity wins; every consumer dispatches a real playbook | Playbook Library + every consumer is structured |
| Per-node prompt overrides | Drop them ("create a new Action") OR keep them? | KEEP — per Q2 resolution. Personalization layer. | Prompt tab retained alongside new Action tab |
| Tool/Handler tier scope | Narrowed to AiAnalysisNodeExecutor OR available to all executors? | KEEP GENERAL — available to any executor that needs delegation | Tool/Handler isn't dispatch-axis; it's a capability available to any executor |
| Lookup table fate | DROP `sprk_analysisactiontype` OR REPURPOSE? | REPURPOSE — decorative for maker browsing | Lookup table preserved; loses load-bearing role |
| Migration strategy | Big-bang OR phased compatibility mode? | BIG BANG — no transition mode | Simpler dispatch + simpler tests + simpler docs |
| Backward compatibility | Required OR not concern? | NOT a concern | Legacy paths fully DELETED |
| Field naming | What goes where? | Column on `sprk_playbooknode` = `sprk_executortype`; Global Choice = `sprk_playbookexecutortype` | All references updated to these names |
| Schema source-of-truth | JPS embedded `output.fields` OR typed `sprk_outputschemajson` column? | TYPED FIELD WINS — better for tooling | Output schema queryable + validatable independently |
| Customer-facing tutorials | R7 scope OR follow-on? | YES — R7 includes maker-facing consumer-wiring guide (FR-31) | docs/guides/ai-guide-consumer-wiring.md is R7 deliverable |
| Canvas UX minimum | Flat dropdown OR categorized? | Categorized dropdown with descriptions (Q10 RESOLVED) | FR-22 specifies the format |
| R4 graduation | Revert /narrate to R3 inline OR hold R4 for R7? | HOLD R4 until R7 wires /narrate | R7 closes R4 via AiCompletion + FR-15 |
| Action Engine timing | Continue parallel OR hold for R7? | HOLD until R7 completes | Action Engine R1 pauses until R7 ships |
| Consumer documentation gap | Who owns? | R7 owns maker-facing tutorial (FR-31). External canonical doc + data-model entry owned by chat-routing-redesign-r1 follow-on. | Scope boundary clear |
| Spaarke Claw scope | Include in R7 OR Action Engine? | Action Engine R1 owns (Spaarke Claw branding + agent-builder UX); R7 owns Playbook Builder UI updates required by dispatch reform | §11 of design.md scoped correctly |
| Playbook Builder UI updates | Out of scope OR R7? | **R7 SCOPE** (reclaimed v0.4) — existing Code Page canvas needs updates required by dispatch reform | FR-21 through FR-27 |
| Backfill source-of-truth | Trust + audit OR manual review OR hybrid? | **MANUAL per-node review** — 94 rows is small; owner reviews each one | FR-19 |
| C# rename `ActionType` → `ExecutorType` | Full rename OR keep C# legacy OR defer? | **FULL rename across BFF** | FR-10 |
| Production rollout | Single-tenant OR multi-tenant? | **spaarkedev1 only** (Spaarke-internal dev → test → prod) | NFR-07; customer migration is separate |
| AiCompletion + R4 close in R7 | Include OR follow-on? | **R7 includes** AiCompletion executor + /narrate close | FR-12 through FR-15 |

---

## Assumptions

Proceeding with these assumptions (owner did not explicitly specify; will validate during build):

- **Tool typed config schemas**: Tools (sprk_analysistool) do NOT need typed config schemas in R7. Tool config stays in node.configJson. Asymmetric vs Invariant 5. If symmetry surfaces as authoring pain, address in follow-on.
- **Wave 6 doc enumeration**: spec.md does NOT enumerate every doc/section to DELETE. Wave 6 tasks enumerate specifically at execution time, guided by PR-review gate on documentation drift.
- **C# enum sync mechanism**: a CI check OR codegen step enforces that C# `ExecutorType` enum values match the `sprk_playbookexecutortype` Choice set. R7 ships either approach; spec doesn't lock which one.
- **Per-executor schema endpoint**: `GET /api/ai/playbook-builder/executor-config-schemas` is the working name; could change to align with existing PlaybookBuilder endpoint conventions.
- **Skill-update wave**: jps-* skills rewritten as a single coordinated batch (Wave 7 or similar), not piecemeal.
- **Playbook Library wiring**: minimum 3 consumer surfaces wired (spaarke-ai chat, briefing widget, one ad-hoc launcher). Additional consumers wired as time permits.
- **Test scaffolding**: existing test fixtures (chat-routing-redesign-r1's seed scripts, Daily Briefing R4 fixtures) reused where possible; new tests follow xUnit pattern from `tests/unit/Sprk.Bff.Api.Tests/`.

---

## Unresolved Questions

Still need answers during implementation (not blocking spec but flagged for build phase):

- [ ] **Q11 — Per-user Action variants**: should Spaarke Claw / Action Engine layer per-user prompt customizations on top of system Actions? Likely Action Engine territory; R7 doesn't decide. Blocks: agent-personalization UX in Action Engine R2/R3.
- [ ] **Q13 — Multi-tenant Playbook Library scoping**: which playbooks are system / per-tenant / per-user discovery rules? Likely Action Engine territory. Blocks: customer-tenant rollout post-R7.
- [ ] **Tool typed config schemas**: should AiAnalysisNodeExecutor's Tool selection render typed forms (symmetric with Invariant 5) OR stay free-form JSON? Default: free-form for now; revisit if surfaces. Blocks: nothing immediate.
- [ ] **C# ↔ Choice sync mechanism**: CI check vs codegen step vs build-time validation? Pick during Wave 1 build. Blocks: nothing immediate; either works.
- [ ] **Insights pipeline ActionType migration**: the universal-ingest / matter-health-* playbooks heavily use ActionType=0 with specific Tool/Handler dispatch. Confirm migration doesn't change Insights dispatch semantics. Likely no change since dispatch is per-node, but worth validation in Wave 5.

---

## Cross-cutting concerns

### Risk register (from design.md §9, condensed)

- **Migration affects many existing playbooks** — mitigated by manual per-node review (FR-19) + idempotent script + dry-run mode
- **Chat-summarize migration could regress chat** — smoke-test chat first; FR-17 integration test
- **Maker-facing doc rewrite is large** — tracked as Wave 6 deliverables; PR review gates drift
- **R4 docs already shipped to master become tech debt** — FR-28 explicitly DELETES outdated sections in Wave 6
- **C# rename creates large diff** — FR-10 accepts the diff; merge conflict risk mitigated by holding Action Engine + scheduling rename early in Wave 2

### Test strategy

- Unit tests: ≥85% line coverage for AiCompletionNodeExecutor + dispatch path changes (NFR-05)
- Integration tests: Daily Briefing /narrate (FR-15), chat-summarize migration (FR-17), Insights pipeline regression sweep
- Migration tests: idempotent rerun confirms no row corruption; dry-run mode confirms preview
- E2E: spaarkedev1 UAT covers all 15 success criteria; owner sign-off gates ship

### Deployment strategy

- R7 deploys per existing BFF deploy convention (Wave 1 + 2 ship together once schema is migrated; Wave 4 cleans up old columns; Wave 5 backfills via owner review)
- Dev → test → prod rollout within spaarkedev1
- No multi-tenant rollout in R7

---

*AI-optimized specification. Original design: [design.md](design.md). Generated 2026-06-28 by /design-to-spec skill.*
