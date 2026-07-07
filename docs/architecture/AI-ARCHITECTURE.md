# Spaarke AI Architecture

> **Last Updated**: 2026-07-07 (spaarke-ai-architecture-redesign-r1 task 052 — aligned to the shipped 2026-07 redesign: three entry paths, closed catalogs, session ledger, ONE confirmation gate; deleted dispatcher/engine-shell/Chat-Tools estates removed from this doc)
> **Last Reviewed**: 2026-07-07
> **Reviewed By**: spaarke-ai-architecture-redesign-r1 task 052 (FR-P4-03)
> **Status**: Current
> **Purpose**: Technical reference overview of the Spaarke AI platform — the three-path dispatch architecture (ADR-039), catalog + executor model, scope library, safety pipeline, Cosmos persistence, facade boundary, and Azure infrastructure.

---

## Scope of this document

This doc is the **platform overview**. Detail lives in companions:

- **THE canonical architecture + component design** (use cases, three entry paths, ledger, gate, manifest, decision register D1–D12) → [`SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md) — read that first for the shipped 2026-07 architecture
- **Chat / agent-turn loop runtime** (session management, loop contract, confirmation gate, SSE pipeline) → [`chat-architecture.md`](chat-architecture.md)
- **FROZEN playbook engine runtime** (node graph, executors, scope arrays — Insights family only; no new capability lands here per OQ-2/D11) → `ai-architecture-playbook-runtime.md`
- **Consumer/Binding routing history** (`sprk_playbookconsumer`, `IConsumerRoutingService`) → `ai-architecture-playbook-consumer-routing.md` (schema truth: [`docs/data-model/sprk-playbookconsumer.md`](../data-model/sprk-playbookconsumer.md))
- **Where new config fields belong** (Action vs Node vs Playbook decision tree — frozen-engine authoring) → `ai-architecture-actions-nodes-scopes.md`
- **Wiring a new capability** (Action + Binding authoring recipe) → [`docs/guides/ai-guide-consumer-wiring.md`](../guides/ai-guide-consumer-wiring.md)

---

## 🆕 The 2026-07 redesign (spaarke-ai-architecture-redesign-r1) — SHIPPED

The AI dispatch layer was redesigned and hard-cut-over in July 2026 (phases P0–P3 complete; P4 hardening in flight). The binding contract is **ADR-039 (Grounded Execution & Closed Catalogs, Accepted)** + **ADR-040 (Session Ledger, Accepted)**:

- **Three entry paths, nothing else** — **Event** (manifest `on_event` Binding rows fire on platform events, e.g. `document_uploaded → classify + summarize`), **Click** (`POST /api/ai/chat/sessions/{sessionId}/dispatch` with a Binding id — chips, ribbons, wizards; zero LLM), **Text** (the bounded agent-turn loop in `SprkChatAgent` — the ONLY probabilistic decider). Adding a second intent-detection mechanism anywhere is an ADR violation.
- **Two closed catalogs** — Actions+Bindings (`sprk_analysisaction` + `sprk_playbookconsumer`) and Tools (`sprk_analysistool` ↔ typed handlers, startup-health-checked bijection). The LLM never invokes an unlisted tool; off-catalog utterances get an honest refusal (REF-CHAT@v1 no_match_handler Binding) + `dispatch_refused` telemetry.
- **Session ledger before rendering** — every execution writes an addressable `SessionOutput` (`{bindingId}@t{n}` / `loop@t{n}`) BEFORE anything renders; `OutputRouter` routes by Binding-declared disposition (informational · email · work_product · overlay · …). Tool chains persist as ledger `ToolChain` entries (identifiers/filters/counts only — no content, NFR-07).
- **ONE confirmation gate** — `PendingPlanManager` unified pending store; loop-invoked tools declaring `side_effect_class ∈ {write, communicate}` suspend via `SideEffectGateAIFunction`; resume via `POST …/gates/{gateId}/resolve` (`TypedHandlerResumeExecutor`, user-OBO); gate markers + outcomes land in the ledger and the transcript. Gating by hardcoded tool-name lists is forbidden.
- **Executors** — **prompted** (`LinearConsumers/ActionRunner` + `PromptSchemaRenderer`) and **coded** (`ICodedWorkflow` via `CodedWorkflowRegistry`; first composite: `DailyBriefingCompositeService`). The node-graph engine (`PlaybookOrchestrationService`) is **FROZEN** — Insights family only.
- **Deleted estates (grep-zero)** — the classifier/dispatcher stack (`PlaybookDispatcher`, `IntentRerankerService`, `PlaybookCandidateSelector`, `CompoundIntentDetector`, the whole PlaybookEmbedding subsystem), `CapabilityRouter` (R2 three-tier classifier), legacy `Chat/Tools` (11 classes), `PlaybookExecutionEngine`, `SessionSummarizeOrchestrator`, `FileSummarize`/`DocumentProfile` wrappers, `intentHint`/`SoftSlashRouter`, `LinearConsumers`/`Workspace.*PlaybookId`/`Insights:Playbooks` config surfaces, FieldDelta dual-render. History: `projects/spaarke-ai-architecture-redesign-r1/`.
- **Quality discipline** — golden-utterance eval suite (62 cases, merge-blocking CI gate); catalog input schemas are CI-validated mirrors under `infra/dataverse/inputschemas/` (`OpenAiFunctionSchemaValidator` excludes invalid rows at projection; health check goes Degraded naming the row).

---

## 🆕 Audit findings — bff-ai-architecture-audit-r1 (2026-06-05)

The Spaarke BFF AI Architecture Audit r1 completed 2026-06-04. Four binding architectural decisions now apply to this doc; full evidence is in [`projects/bff-ai-architecture-audit-r1/notes/canonical-architecture-decisions.md`](../../projects/bff-ai-architecture-audit-r1/notes/canonical-architecture-decisions.md):

| Decision | Where codified | Migration PR |
|---|---|---|
| **Spaarke Public-Contracts Facade DI Fascia** — external CRUD code consumes AI only through `PublicContracts/` facades (per refined ADR-013) | [`.claude/patterns/ai/public-contracts-facade.md`](../../.claude/patterns/ai/public-contracts-facade.md) + [DR-003](../../projects/bff-ai-architecture-audit-r1/decisions/DR-003-public-contracts-facade.md) | PR #351 (LATENT BUG #1 fix + 4 Null peers) |
| **Endpoint↔DI Registration Conditionality Symmetry Rule** — NEW load-bearing rule preventing the LATENT BUG #1 anti-pattern (facade unconditional, transitive deps conditional → 500 instead of 503) | [`.claude/patterns/ai/endpoint-di-symmetry.md`](../../.claude/patterns/ai/endpoint-di-symmetry.md) + [DR-008](../../projects/bff-ai-architecture-audit-r1/decisions/DR-008-di-configuration.md) + [`.claude/constraints/bff-extensions.md` §F.1](../../.claude/constraints/bff-extensions.md) | PR #351 |
| **BFF Canonical Cache Stack** — `IDistributedCache` + `GetOrCreateAsync<T>` only; `EmbeddingCache` canonical model; `MemoryCache` requires explicit ADR-009 exception XML doc | [DR-002](../../projects/bff-ai-architecture-audit-r1/decisions/DR-002-cache-patterns.md) | Phased PR #5+ (26 sites; per-team) |
| **3140 LOC of dead AI code removed** — 3 lookup orphans, intent classifier cascade, 5th orphan, 3 Cat 10 tool handlers, PlaybookBuilderSystemPrompt 800-LOC dead bulk | — | PR #353 + PR #357 |

REJECTED options the audit explicitly considered and locked:
- Generic `IIntentClassifier<TResult>` interface — REJECTED (3 canonicals KEEP, no forced consolidation)
- 4-substrate search consolidation — REJECTED (each substrate justified by different index; KEEP all 4)
- Forced DI module consolidation — REJECTED (31 per-concern modules KEEP)

---

## Overview

Spaarke AI provides document analysis, knowledge retrieval, and conversational AI capabilities as an extension of the BFF API (ADR-013). The architecture separates reusable AI primitives (scopes) and catalog data (Actions, Bindings, Tools) from the execution machinery that runs them, enabling configuration-driven AI capabilities without code deployment: a maker ships a new prompted capability by authoring an Action row + a Binding row (see [`docs/guides/ai-guide-consumer-wiring.md`](../guides/ai-guide-consumer-wiring.md)). Control flow is code; behavior is data.

Two handler interface hierarchies coexist: `IAnalysisToolHandler` for the tool handler registry (analysis pipeline, frozen playbook nodes; projected into the agent loop via `ToolHandlerToAIFunctionAdapter`), and `IAiToolHandler` for playbook workflow tool handlers (simpler interface with `ToolName` + `ExecuteAsync`). Both are registered in DI and resolved at runtime.

---

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| AnalysisOrchestrationService | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` | Top-level orchestrator: routes to action-based or playbook-based execution, streams SSE |
| ToolHandlerRegistry | `src/server/api/Sprk.Bff.Api/Services/Ai/ToolHandlerRegistry.cs` | Indexes all `IAnalysisToolHandler` by HandlerId and ToolType; supports config-based disabling |
| ToolFrameworkExtensions | `src/server/api/Sprk.Bff.Api/Services/Ai/ToolFrameworkExtensions.cs` | DI registration: assembly-scans for handlers, registers registry as Scoped |
| GenericAnalysisHandler | `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/GenericAnalysisHandler.cs` | Configuration-driven handler (95% of tools); supports JPS, structured output, streaming |
| IAnalysisToolHandler | `src/server/api/Sprk.Bff.Api/Services/Ai/IAnalysisToolHandler.cs` | Handler interface: HandlerId, Metadata, Validate, ExecuteAsync |
| IStreamingAnalysisToolHandler | `src/server/api/Sprk.Bff.Api/Services/Ai/IStreamingAnalysisToolHandler.cs` | Opt-in streaming: `StreamExecuteAsync` yields `ToolStreamEvent.Token` then `Completed` |
| IAiToolHandler | `src/server/api/Sprk.Bff.Api/Services/Ai/IAiToolHandler.cs` | Simpler playbook tool interface: ToolName + ExecuteAsync(ToolParameters) |
| ScopeResolverService | `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs` | Loads scopes from Dataverse by ID, playbook, or node; CRUD; $choices resolution |
| AnalysisContextBuilder | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisContextBuilder.cs` | Prompt assembly: Action.SystemPrompt + Skill fragments + Knowledge + Document |
| AiAnalysisNodeExecutor | `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` | Bridges playbook nodes to IToolHandlerRegistry; L1/L2/L3 knowledge retrieval |
| AnalysisEndpoints | `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` | SSE streaming endpoints: execute, continue, save |
| HandlerEndpoints | `src/server/api/Sprk.Bff.Api/Api/Ai/HandlerEndpoints.cs` | Handler discovery: `/api/ai/handlers` (registry metadata) + `/api/ai/tools/handlers` (class names) |
| AnalysisDocumentLoader | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisDocumentLoader.cs` | Document retrieval, text extraction, analysis caching |
| AnalysisRagProcessor | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisRagProcessor.cs` | RAG search, cache key computation, tenant resolution |
| AnalysisResultPersistence | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisResultPersistence.cs` | Output storage, RAG indexing enqueue, working doc finalization |
| IOpenAiClient | `src/server/api/Sprk.Bff.Api/Services/Ai/IOpenAiClient.cs` | Azure OpenAI abstraction: streaming, structured, vision, embeddings, tool-calling |
| SprkChatAgent | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs` | The bounded agent-turn loop (Text path) — the only probabilistic decider (ADR-039) |
| PlaybookChatContextProvider | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs` | Chat context: playbook Action system prompt, knowledge scopes, host-record entity enrichment |
| ConsumerRoutingService | `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerRoutingService.cs` | Binding-table catalog reader — THE single routing surface (`sprk_playbookconsumer` full contract) |
| SessionDispatchOrchestrator | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionDispatchOrchestrator.cs` | Click path: GUID-resolved Binding dispatch, catalog+ledger-first (`POST /api/ai/chat/sessions/{id}/dispatch`) |
| BindingCapabilityTool | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/BindingCapabilityTool.cs` | Projects a Binding into the loop as a `capability_{type}` tool (generation only — writes go through gated tools) |
| PendingPlanManager | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PendingPlanManager.cs` | THE unified confirmation gate store + SessionGate ledger markers |
| SideEffectGateAIFunction | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SideEffectGateAIFunction.cs` | Fail-closed wrap on loop-invoked tools by declared `side_effect_class` — suspends into the gate |
| TypedHandlerResumeExecutor | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/TypedHandlerResumeExecutor.cs` | Gate confirm-resume: executes the suspended tool under user OBO, ledger write before render |
| OutputRouter | `src/server/api/Sprk.Bff.Api/Services/Ai/OutputRouter.cs` | Disposition-driven output routing (informational · email · work_product · …) after the ledger write |
| ActionRunner (LinearConsumers) | `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/ActionRunner.cs` | The prompted executor: single-LLM-call Action execution with `PromptSchemaRenderer` |
| CodedWorkflowRegistry | `src/server/api/Sprk.Bff.Api/Services/Ai/CodedWorkflowRegistry.cs` | Assembly-scanned `ICodedWorkflow` registry — the coded executor slot (e.g. `DailyBriefingCompositeService`) |
| WorkProductRecordPersister | `src/server/api/Sprk.Bff.Api/Services/Ai/WorkProductRecordPersister.cs` | work_product disposition leg: Binding → `sprk_aitopicregistry` → host-record persistence (user-OBO If-Match) |
| OpenAiFunctionSchemaValidator | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/OpenAiFunctionSchemaValidator.cs` | Projection-time validation of catalog input schemas (invalid row ⇒ tool excluded + health Degraded, never a loop 400) |

---

## Four-Tier Architecture

```
 ┌─────────────────────────────────────────────────────────────────────┐
 │  TIER 1: CATALOG + SCOPE LIBRARY (Spaarke IP)                      │
 │  Closed capability catalog + reusable AI primitives in Dataverse   │
 │  Actions (sprk_analysisaction) · Bindings (sprk_playbookconsumer)  │
 │  Tools (sprk_analysistool) · Skills · Knowledge · Personas         │
 ├─────────────────────────────────────────────────────────────────────┤
 │  TIER 2: DISPATCH (the three entry paths — ADR-039)                │
 │  Event (on_event Binding rows) · Click (dispatch by Binding id)    │
 │  Text (bounded agent-turn loop — the only probabilistic decider)   │
 ├─────────────────────────────────────────────────────────────────────┤
 │  TIER 3: EXECUTION RUNTIME                                         │
 │  Prompted executor (ActionRunner + PromptSchemaRenderer)           │
 │  Coded workflows (ICodedWorkflow registry)                         │
 │  Agent loop tool composition (typed handlers, budget ≤ 8)          │
 │  FROZEN node-graph engine (PlaybookOrchestrationService —          │
 │  Insights family only; no new capability lands here)               │
 ├─────────────────────────────────────────────────────────────────────┤
 │  TIER 4: AZURE INFRASTRUCTURE                                      │
 │  Cloud services backing everything                                 │
 │  Azure OpenAI · Azure AI Search · Document Intelligence            │
 │  Redis · Service Bus · Cosmos DB · Content Safety                  │
 └─────────────────────────────────────────────────────────────────────┘
```

### Key Architectural Principles

1. **Catalog data is the manifest** — Action + Binding rows (no new tables; "capability" is vocabulary, not schema). The Binding table is the ONLY routing surface; makers ship/tune/re-route capabilities as data.
2. **Scopes are independent primitives** — consumable by prompted Actions, the agent loop, the frozen engine, and background jobs.
3. **Control flow is code; behavior is data** (OQ-2/D11) — makers edit prompts, schemas, scopes, bindings, chips; never branches and loops. New composites are `coded` workflows (`ICodedWorkflow`), not node graphs.
4. **Grounded execution** (ADR-039) — every output is a cataloged-capability output, a cited tool-composed answer, a confirmation prompt, or an honest refusal. Free-form ungrounded completion has no code path.
5. **Storage precedes rendering** (ADR-040) — the session ledger is the composition backbone; disposition is the rendering contract.

---

## Data Flow

### Chat / capability dispatch (the three-path protocol)

Covered in [`chat-architecture.md`](chat-architecture.md) (loop contract, gate, SSE) and canonically in [`SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`](SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md) §7. Summary: Event rules and Click dispatch resolve a Binding deterministically; Text-path utterances enter the bounded agent turn, which composes catalog capability tools + primitive tools (≤ 8 calls/turn, deterministic context pre-filter, citation enforcement, ToolChain → ledger). Every execution writes its `SessionOutput` to the ledger BEFORE rendering; `OutputRouter` then routes by disposition.

### Analysis Execution (Action-Based, No Playbook)

1. `POST /api/ai/analysis/execute` receives `AnalysisExecuteRequest` with document IDs, action ID, and scope IDs
2. `AnalysisEndpoints` sets SSE response headers and iterates `AnalysisOrchestrationService.ExecuteAnalysisAsync`
3. If `PlaybookId` is set: delegates to `ExecutePlaybookAsync` (playbook path); otherwise continues with action-based path
4. `AnalysisDocumentLoader` retrieves document from Dataverse, downloads file from SPE via `ISpeFileOperations` (OBO auth), extracts text via `ITextExtractor`
5. `IScopeResolverService.ResolveScopesAsync` loads skills, knowledge, tools from Dataverse in parallel with `GetActionAsync`
6. `AnalysisRagProcessor.ProcessRagKnowledgeAsync` queries Azure AI Search for RAG knowledge sources
7. `AnalysisContextBuilder.BuildSystemPrompt` assembles: Action.SystemPrompt + Skill.PromptFragments; `BuildUserPromptAsync` assembles: document text + RAG context
8. `IOpenAiClient.StreamCompletionAsync` streams tokens; each token is yielded as `AnalysisStreamChunk.TextChunk` (SSE)
9. `AnalysisResultPersistence` writes working document periodically (every 500 chars) and finalizes to Dataverse
10. RAG indexing job is enqueued via Service Bus for post-analysis indexing

### Playbook-driven LLM Output Pattern (R7 Wave 11, narrative consumers)

For LLM-produces-structured-output-from-runtime-data consumers (Daily Briefing, Insight Engine matter summaries, work assignment briefings, project status, document review, and any future Workspace UX narrative output), see the canonical reference [`SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md`](SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md). It documents the two-layer architecture (Layer 1 orchestrator template resolution against NodeOutputs + Parameters + run metadata; Layer 2 PromptSchemaRenderer `## Input` section) that decouples prompt instructions from data shape. Maker tutorial at [`docs/guides/BUILD-A-NEW-NARRATIVE-OUTPUT-CONSUMER.md`](../guides/BUILD-A-NEW-NARRATIVE-OUTPUT-CONSUMER.md).

### Playbook Node Execution (via AiAnalysisNodeExecutor) — FROZEN ENGINE (Insights family only)

> The node-graph engine is **frozen** (OQ-2/D11, 2026-07): it continues to serve the Insights family unchanged, but no new capability may be built on it. New composites are `coded` workflows. Engine outputs bridge into the session ledger via `EngineOutputLedgerAdapter`.

1. `PlaybookOrchestrationService` topologically sorts the node graph and executes nodes in parallel batches
2. For AI nodes, `AiAnalysisNodeExecutor.ExecuteAsync` is called with `NodeExecutionContext`
3. Executor resolves `IToolHandlerRegistry` from a new DI scope (registry is Scoped, executor is Singleton)
4. Three-tier knowledge retrieval runs: L1 (ReferenceRetrievalService), L2 (IRagService, optional), L3 (IRecordSearchService, optional)
5. `LookupChoicesResolver.ResolveFromJpsAsync` pre-resolves `$choices` Dataverse lookups for constrained decoding
6. `ToolExecutionContext` is built with merged knowledge, resolved choices, and document text
7. Handler is looked up by `tool.HandlerClass` from registry; validated; then executed
8. **Streaming path**: if handler implements `IStreamingAnalysisToolHandler` and caller provided `OnTokenReceived` callback, uses `StreamExecuteAsync` yielding `ToolStreamEvent.Token` events
9. **Blocking path**: otherwise calls `IAnalysisToolHandler.ExecuteAsync` returning `ToolResult`

---

## Typed Executor Config Schemas (FR-16)

> **Added 2026-06-28 by `spaarke-ai-platform-unification-r7` Wave 3** — Invariant 5 / FR-16. Every `INodeExecutor` implementation declares a typed schema describing the maker-editable fields it reads from `sprk_playbooknode.sprk_configjson`. The Playbook Builder canvas (Wave 8 FR-23) consumes these schemas via a single endpoint and renders typed forms instead of free-text JSON editors. Reference design: [`projects/spaarke-ai-platform-unification-r7/notes/spikes/getconfigschema-design.md`](../../projects/spaarke-ai-platform-unification-r7/notes/spikes/getconfigschema-design.md).

### What

`INodeExecutor` exposes `ExecutorConfigSchema GetConfigSchema()` — a pure, deterministic, sync method returning a singleton-cached schema descriptor. The schema is the **maker contract** (what fields the canvas presents); `ConfigJson` is the **runtime contract** (what the executor deserializes). They MUST stay aligned — schema field names match the executor's private config record `[JsonPropertyName]` attributes.

C# DTO shape (in `Sprk.Bff.Api/Services/Ai/Nodes/ExecutorConfigSchema.cs`):

```csharp
public sealed record ExecutorConfigSchema(
    string ExecutorTypeName,        // "AiCompletion"
    int ExecutorTypeValue,          // 1
    string Description,             // "Prompt-only structured LLM completion"
    IReadOnlyList<ConfigSchemaField> Fields)
{
    public static ExecutorConfigSchema Empty(ExecutorType type, string description) => ...;
}

public sealed record ConfigSchemaField(
    string Name,
    SchemaFieldType Type,           // String | Number | Boolean | Object | Array | Enum
    bool Required,
    string Description,
    object? Default = null,
    IReadOnlyList<string>? EnumValues = null);
```

### Endpoint

```
GET /api/ai/playbook-builder/executor-config-schemas   →   200 OK
```

Authorized by the standard `RequireAuthorization()` on the PlaybookBuilder group. Response envelope (one entry per executor, ordered ascending by `ExecutorTypeValue` for deterministic diffs):

```json
{
  "schemas": [
    {
      "executorTypeName": "AiCompletion",
      "executorTypeValue": 1,
      "description": "Prompt-only structured LLM completion (FR-12).",
      "fields": [
        {
          "name": "templateParameters",
          "type": "Object",
          "required": false,
          "description": "Key→value map substituted into {{var}} bindings in the JPS instruction.",
          "default": null
        },
        {
          "name": "promptSchemaOverride",
          "type": "Object",
          "required": false,
          "description": "Per-node override merged into the Action's base JPS prompt schema (FR-25).",
          "default": null
        }
      ]
    },
    {
      "executorTypeName": "Start",
      "executorTypeValue": 33,
      "description": "Canvas anchor; pass-through with no execution logic.",
      "fields": []
    }
  ]
}
```

### Rich vs placeholder schemas

| Pattern | Count (Wave 3) | When |
|---|---|---|
| **Rich** (≥1 field) | 5 — `AiCompletion`, `AiAnalysis`, `AiEmbedding`, `EntityNameValidator`, `DeliverComposite` | Executor has maker-editable config (e.g., `templateParameters`, override JSON, deliver targets) |
| **Placeholder** (empty `fields: []`) | 20 — `Start`, `ReturnResponse`, `Condition`, `Parallel`, `Wait`, etc. | Executor takes no maker config; canvas renders a collapsed "no configuration required" hint |

The empty array IS the contract — it distinguishes "intentionally no config" from "we forgot to define it." Placeholder pattern:

```csharp
private static readonly ExecutorConfigSchema CachedSchema =
    ExecutorConfigSchema.Empty(ExecutorType.Start, "Canvas anchor; pass-through with no execution logic.");

public ExecutorConfigSchema GetConfigSchema() => CachedSchema;
```

### Author guidance — declaring a schema for a new executor

When adding a new `INodeExecutor`:

1. **Declare a `private static readonly ExecutorConfigSchema CachedSchema`** built once at class load (no DI, no I/O).
2. **Return it directly from `GetConfigSchema()`** — same reference every call.
3. **Pick `SchemaFieldType`** from `{ String, Number, Boolean, Object, Array, Enum }`. Nested arbitrarily-shaped JSON → `Object` (canvas renders a Monaco-style sub-editor). Typed enum → `Enum` + populate `EnumValues`.
4. **Use `Required = true` only** for fields the executor's `Validate()` actually requires. Don't pad required-ness to "guide" the maker — `Description` is the right place for guidance.
5. **Use `Default`** to communicate the executor's default behavior to the canvas (canvas pre-fills the form).
6. **Keep schema field names in lockstep** with the runtime config record's `[JsonPropertyName]` attributes. See [`.claude/patterns/ai/node-executor-authoring.md`](../../.claude/patterns/ai/node-executor-authoring.md) for the full executor pattern.

### Forward-compat — what the canvas does with unknown values

- **New `SchemaFieldType` enum value** (e.g., future `Date`, `Duration`): append to the C# enum tail — never insert mid-list (numeric values are wire contract). Older canvas builds that don't know the new string fall through to the same warning state used for unknown executor types per spec FR-27 (read-only JSON view + "unsupported field type — update Playbook Builder Code Page" hint).
- **New optional `ConfigSchemaField` property** (e.g., future `validationRegex`, `minLength`): add as `init` accessor with `[JsonIgnore(Condition = WhenWritingNull)]`. Older canvas builds ignore unknown JSON properties — additive, non-breaking.
- **Removing a field property or renaming `ExecutorTypeName`** is BREAKING — coordinate server + canvas in the same deploy or deprecate by marking `Required = false` first.

### Cross-references

- **Requirement**: spec.md FR-16 (typed schemas) + Invariant 5 (executor-as-schema-authority)
- **ADRs**: [ADR-010 DI Minimalism](../adr/ADR-010-di-minimalism.md) (singleton schemas, zero DI deps), [ADR-013 BFF AI Architecture](../adr/ADR-013-bff-ai-architecture.md) (endpoint placement on BFF, not a separate AI microservice)
- **Pattern**: [`.claude/patterns/ai/node-executor-authoring.md`](../../.claude/patterns/ai/node-executor-authoring.md) — full executor authoring pointer
- **Consumer (Wave 8 task 083)**: `src/client/code-pages/PlaybookBuilder/src/**` — canvas loads schemas at mount, caches in-memory for the session, renders typed forms per node configuration
- **Design history**: [`projects/spaarke-ai-platform-unification-r7/notes/spikes/getconfigschema-design.md`](../../projects/spaarke-ai-platform-unification-r7/notes/spikes/getconfigschema-design.md)

---

## Tool Handler Framework (Tier 3 Detail)

> **Moved**: Tool Handler Framework, handler registration, resolution chain, and `IAnalysisToolHandler` contract have been consolidated into the runtime canonical doc. See [`ai-architecture-playbook-runtime.md`](ai-architecture-playbook-runtime.md) §1 (Component model — Layer E) and §5 (Action lookup precedence) for canonical content. See `.claude/patterns/ai/` for code-pointer files.

---

## Scope Resolution

> **Moved**: scope resolution methods, scope types, ownership prefixes, and `$choices` resolution detail have been moved. Runtime semantics (advisory-not-enforcing) live in [`ai-architecture-playbook-runtime.md`](ai-architecture-playbook-runtime.md) §6. Config-bag boundary (where scopes belong: Home D N:N relationships, not inline JSON) lives in [`ai-architecture-actions-nodes-scopes.md`](ai-architecture-actions-nodes-scopes.md). `$choices` schema reference lives in [`ai-guide-jps-authoring.md`](../guides/ai-guide-jps-authoring.md).

---

## Knowledge-Augmented Execution

The `AiAnalysisNodeExecutor` retrieves tiered knowledge before calling the LLM:

```
AiAnalysisNodeExecutor
  ├── L1: ReferenceRetrievalService — curated domain knowledge (spaarke-rag-references index)
  ├── L2: IRagService — similar customer docs (spaarke-knowledge-index-v2, optional)
  ├── L3: IRecordSearchService — business entity metadata (optional)
  └── Merge → KnowledgeContext → Prompt assembly
```

Retrieval mode is configured per-node via `ConfigJson` (`auto`/`always`/`never`, default: auto with TopK=5).

### RAG Pipeline

**Search flow**: Query → EmbeddingCache (Redis, SHA256 keys, 7-day TTL) → Azure OpenAI (embedding, text-embedding-3-large, 3072-dim) → Azure AI Search (hybrid: BM25 + Vector + Semantic) → Security filter (tenantId) → Semantic reranking → Results.

**Search indexes**:
- `spaarke-knowledge-index-v2` — Customer documents (3072-dim, HNSW, cosine)
- `spaarke-rag-references` — Golden reference knowledge (3072-dim, HNSW, cosine)

---

## AI Search Consumer Map

> **Canonical source**: [`AI-SEARCH-INDEX-CATALOG.md`](AI-SEARCH-INDEX-CATALOG.md) — single source of truth for per-index schema, naming convention, property policy, vector config, retired-index history, and post-deploy invariants. **This section is the consumer-map view only**; it does NOT duplicate catalog content. If consumer info here disagrees with the catalog, the catalog wins — open a PR to update this table.

The seven active Spaarke AI Search indexes and their primary BFF consumers (services + endpoints) and data-flow direction. Flow direction = `inbound` (write-only from BFF), `outbound` (read-only from BFF), or `bidirectional`.

| Index name | Primary consumers (services + endpoints) | Data flow direction |
|---|---|---|
| `spaarke-files-index` | `RagService`, `RagIndexingPipeline`, `FileIndexingService`, `IndexRetrieveNode`, `KnowledgeBaseEndpoints`, `BulkRagIndexingJobHandler`, `RagIndexingJobHandler` · endpoints `POST /api/ai/rag/query`, `POST /api/ai/rag/index-file`, semantic search endpoints | bidirectional |
| `spaarke-records-index` | `DataverseIndexSyncService`, `RecordSyncJob`, `RecordSearchAuthorizationFilter` · endpoint `POST /api/ai/search` (scope=entity) | bidirectional |
| `spaarke-rag-references` | `ReferenceIndexingService`, `ReferenceRetrievalService` · ingestion via PowerShell `scripts/ai-search/Add-ReferenceToIndex.ps1` + `Index-AllReferences.ps1` (KNW-*.md golden references); read path via L1 knowledge retrieval | bidirectional |
| `spaarke-insights-index` | `PrecedentProjectionSync` + insights projection pipeline · endpoint `POST /api/ai/insights/search` | bidirectional |
| `spaarke-session-files` | `SessionFilesCleanupJob` (cleanup only) · schema-only in this project per FR-18 (no ingestion path) | outbound (cleanup reads only) |
| `spaarke-invoices-index` | `InvoiceIndexingJobHandler`, `InvoiceSearchService` · schema-only in this project per FR-18 (no ingestion) | outbound (search reads only) |
| `spaarke-playbook-embeddings` | **ORPHANED** — all code consumers (`PlaybookEmbeddingService`, `PlaybookIndexingService`, `PlaybookIndexingBackgroundService`, `PlaybookIndexDriftDetectionJob`) were DELETED with the dispatcher stack (redesign-r1 task 035); the Azure index awaits decommission per the Track-B P4 sweep (FR-P4-01) | none (pending decommission) |

---

## AI Public Contracts Facade Boundary (Phase 4 Outcome E, 2026-05-25)

Refined **ADR-013** (2026-05-20) requires external CRUD code to consume AI capabilities through a **stable, narrow facade**, not by directly injecting AI-internal types like `IOpenAiClient` or `IPlaybookService`. Boundary intent: AI internals stay AI-internal; CRUD-tier code consumes only what it needs through purpose-built interfaces.

### The Facade Interfaces (current)

Located in `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/`:

| Interface | Wraps / provides |
|---|---|
| `IConsumerRoutingService` | **The Binding-table catalog reader** — resolves `sprk_playbookconsumer` rows (full routing contract) for all three entry paths; boot-reconciled by `RoutingConsumerTypeHealthCheck` |
| `IBriefingAi` | `IOpenAiClient.GetCompletionAsync` (narrative generation) |
| `IInsightsAi` | Insights family Zone-A facade (frozen-engine cluster) |
| `IInvoiceAi` | `IPlaybookService.GetByNameAsync` + `IOpenAiClient.GetStructuredCompletionAsync<T>` + `IOpenAiClient.GenerateEmbeddingAsync` |
| `IRecordMatchingAi` | `IRecordSearchService.SearchAsync` |
| `IWorkspacePrefillAi` | Matter/project prefill |
| `IObservationMirror` | Insights observation mirroring |

> `IInvokePlaybookAi` (the R4 "Path A.5" non-streaming playbook facade) was **DELETED** by redesign-r1 task 044 with the engine shells — consumers dispatch via Binding rows instead.

### DI Registration

Facade registrations live in `Infrastructure/DI/AnalysisServicesModule.cs`; every gated interface has a Null-Object peer per ADR-032 (kill-switch pattern), so endpoints map unconditionally and degrade honestly when a feature flag is off.

### Migration Scope (Post-Facade)

10 consumer files migrated across Finance (3), Workspace (4), Jobs (1), Dataverse + Filters + Endpoints (2). Net reduction: **148 → 12 occurrences across 59 → 5 files** (92% reduction in direct `IOpenAiClient` / `IPlaybookService` injection from CRUD-side code).

### Documented Boundary Exceptions

5 files remain on direct injection because they ARE the AI API surface, not external CRUD consumers:

| File | Why direct injection is retained |
|---|---|
| `Api/Ai/ChatEndpoints.cs` | Chat API surface (raw AI exposure to clients) |
| `Api/Ai/PlaybookEndpoints.cs` | Playbook CRUD API — 10 handlers that wrap `IPlaybookService` 1:1; facade-wrapping would duplicate the surface |
| `Api/Ai/AiPlaybookBuilderEndpoints.cs` | AI-internal builder for constructing playbooks |
| `Api/Agent/AgentEndpoints.cs` | M365 Copilot agent gateway (playbook-discovery pattern) |
| `Api/Filters/PlaybookAuthorizationFilter.cs` | ADR-008 authorization filter using `IPlaybookService.GetPlaybookAsync(Guid)` for ownership checks |

### AI-Coupled Handler Relocation (FR-E3)

5 files moved from `Services/Jobs/{Handlers,}` → `Services/Ai/Jobs/`:

- `AppOnlyDocumentAnalysisJobHandler`
- `BulkRagIndexingJobHandler`
- `EmailAnalysisJobHandler`
- `ProfileSummaryJobHandler`
- `EmbeddingMigrationService`

Handlers with mixed AI + Dataverse coupling stay in `Services/Jobs/Handlers/` per the G1 reconciliation (AI-coupled = references `Sprk.Bff.Api.Services.Ai.*` AND does NOT require `Spaarke.Dataverse` / `Microsoft.Xrm.Sdk`).

### FR-C6 CI Guard (Task 082, Deferred)

A CI guard will codify the boundary by blocking any new direct `IOpenAiClient` or `IPlaybookService` injection in non-AI-internal modules. This converts Outcome E from a one-time refactor into a permanent architectural boundary.

**References**: [`projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md`](../../projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md) Phase 4 Outcome E (tasks 046–053) for evidence; [ADR-013](../../.claude/adr/ADR-013-ai-architecture.md) refined 2026-05-20 for binding rule.

---

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | SPE / Documents | `ISpeFileOperations` via `AnalysisDocumentLoader` | OBO auth for document download and text extraction |
| Depends on | Dataverse | `DataverseHttpServiceBase` (OData) | Scope loading, analysis persistence, record updates |
| Depends on | Azure OpenAI | `IOpenAiClient` | Completions, structured output, embeddings, vision |
| Depends on | Azure AI Search | `IRagService`, `ReferenceRetrievalService` | Hybrid search with security filters |
| Depends on | Redis | `IEmbeddingCache`, analysis caching | Embedding cache, analysis state cache |
| Depends on | Service Bus | `AnalysisResultPersistence` | Enqueues RAG indexing jobs post-analysis |
| Consumed by | Playbook System | `AiAnalysisNodeExecutor` → `IToolHandlerRegistry` | Node executor bridges playbook nodes to tool handlers |
| Consumed by | SprkChat | `PlaybookChatContextProvider` → `IChatContextProvider` | Resolves scopes to agent tools for conversational AI |
| Consumed by | PCF / Code Pages | `AnalysisEndpoints` (SSE) | Frontend consumes SSE token stream |
| Consumed by | Scope Config Editor | `HandlerEndpoints` | Handler discovery for dropdown population |
| Consumed by | **CRUD-tier consumers** (Finance, Workspace, Jobs, Dataverse) | **`Services/Ai/PublicContracts/` facade** (`IBriefingAi`, `IInvoiceAi`, `IRecordMatchingAi`, `IWorkspacePrefillAi`) | **Refined ADR-013 boundary — CRUD code MUST NOT inject `IOpenAiClient` / `IPlaybookService` directly** |
| Depends on | Cosmos DB | `CosmosClient` via `AiPersistenceModule` | Session, audit, feedback, memory, prompt persistence (R2); session ledger warm storage (ADR-040) |
| Depends on | Azure Content Safety | `PromptShieldService`, `GroundednessCheckService` | Prompt injection detection, groundedness annotation (R2) |
| Consumed by | Feedback | `FeedbackEndpoints` | Per-response quality feedback collection (R2) |

---

## Intent Routing — DELETED classifier stacks (history)

There is **no intent-classification component** in the platform. Two generations of classifier stacks were built and later deleted:

- The R2 `CapabilityRouter` (three-tier keyword → GPT-4o-mini → superset classifier) was removed by the bff-ai-architecture-audit-r1 dead-code sweep.
- The vector dispatcher stack (`PlaybookDispatcher`, `IntentRerankerService`, `PlaybookCandidateSelector`, `CompoundIntentDetector`, the PlaybookEmbedding subsystem) was DELETED by spaarke-ai-architecture-redesign-r1 tasks 034–036 (FR-P2-05/06/07) at the text-path hard cutover.

Under ADR-039 the **bounded agent-turn loop is the only probabilistic decider**: capability Bindings project into the loop as tools (with maker-editable `sprk_tooldescription` as the intent surface), and routing quality is governed by the golden-utterance eval suite (merge-blocking), not thresholds. A documented re-entry exists ONLY as an optimization: embedding retrieval as a tool-list pre-filter if the catalog exceeds ~100 entries — never as the decision-maker.

---

## Safety Pipeline (R2)

The safety perimeter comprises four services that run pre-LLM and post-LLM to detect prompt injection, verify groundedness, validate citations, and enforce privilege boundaries. All services are registered in `AiSafetyModule` (ADR-010 module pattern). Services fail open to preserve availability.

| Service | Path | Stage | Purpose |
|---------|------|-------|---------|
| PromptShieldService | `Services/Ai/Safety/PromptShieldService.cs` | Pre-LLM | Calls Azure AI Content Safety Prompt Shields API to detect prompt injection (user and document attacks). 100ms hard timeout; fail-open on 429/5xx/timeout. |
| GroundednessCheckService | `Services/Ai/Safety/GroundednessCheckService.cs` | Post-LLM | Retroactive groundedness annotation via Azure AI Content Safety. Scores claims against source documents. |
| CitationVerificationService | `Services/Ai/Safety/Citations/CitationVerificationService.cs` | Post-LLM | Verifies citation references against `IVerificationProvider` implementations (e.g. InternalIndexProvider for spaarke-rag-references). |
| PrivilegeGroupResolver | `Services/Ai/Security/PrivilegeGroupResolver.cs` | Pre-LLM | Resolves the user's Dataverse security role memberships to determine which tools and capabilities are authorized. |

**SafetyPipelineMiddleware** (`Services/Ai/Chat/Middleware/SafetyPipelineMiddleware.cs`) orchestrates the pipeline as a decorator on `ISprkChatAgent`. It runs PromptShield pre-LLM and GroundednessCheck + CitationVerification post-LLM.

**Cross-matter safety** (AIPU2-028): `MatterContextDetector` detects when a conversation crosses matter boundaries. `ConversationHistorySanitizer` strips prior matter context from the message history to prevent information leakage.

**Required configuration**:

| Setting | Description |
|---------|-------------|
| `AiSafety:ContentSafety:Endpoint` | Azure AI Content Safety endpoint (default: `https://spaarke-contentsafety-dev.cognitiveservices.azure.com/`) |
| `AiSafety:ContentSafety:ApiKey` | Content Safety API key (supports Key Vault rotation) |

---

## Cosmos DB Persistence (R2)

Session state, audit logs, feedback, memory, and prompt history are persisted to Azure Cosmos DB (serverless, RBAC-only auth via `DefaultAzureCredential`). Registered in `AiPersistenceModule` (ADR-010 module pattern).

**Access pattern**: Write-through (decision D-06: no idle-flush). Redis serves as the hot cache (24h TTL); Cosmos DB is warm storage (90-day retention for most containers, permanent for audit).

| Container | Partition Key | TTL | Purpose | Service |
|-----------|--------------|-----|---------|---------|
| `sessions` | `/userId` | 90 days | AI conversation sessions | SessionPersistenceService |
| `prompts` | `/sessionId` | 90 days | Individual prompt/completion pairs | PromptLibraryService |
| `audit` | `/tenantId` | None (permanent) | Immutable compliance audit trail (ADR-015 Tier 2) | AuditLogService |
| `memory` | `/userId` | 90 days | Per-matter structured AI memory snapshots | MatterMemoryService |
| `feedback` | `/tenantId` | 90 days | User feedback (thumbs up/down) on AI responses | FeedbackService |

**CosmosClient** is registered as Singleton (thread-safe, manages connection pool internally). Uses `CosmosClientBuilder` with `WithConnectionModeDirect()` and throttling retry (30s wait, 9 retries).

**Required configuration**:

| Setting | Description |
|---------|-------------|
| `CosmosPersistence:Endpoint` | Cosmos DB account endpoint URI |
| `CosmosPersistence:DatabaseName` | Target database name (default: `spaarke-ai`) |

---

## Feedback Collection (R2)

`FeedbackService` stores per-response user feedback (thumbs up/down with optional comment) in the Cosmos DB `feedback` container and provides aggregation queries for playbook and capability quality reporting (AIPU2-036).

| Method | Purpose |
|--------|---------|
| `SubmitAsync` | Writes a `FeedbackEntry` to Cosmos DB; enforces 500-char comment cap |
| `GetAggregateByPlaybookAsync` | Counts thumbs-up/down and retrieves top-10 negative comments for a playbook |
| `GetAggregateByCapabilityAsync` | Same aggregation scoped to a capability ID |

**Endpoint**: `POST /api/ai/feedback` / `GET /api/ai/feedback/playbook/{id}` / `GET /api/ai/feedback/capability/{id}` (registered in `FeedbackEndpoints.cs`).

All queries are tenant-scoped (partition key = `/tenantId`). Aggregation queries use Cosmos SQL with parameterized filters to prevent injection.

---

## Known Pitfalls

> **Moved**: tool-handler-specific pitfalls (HttpContext propagation, missing handler registration, SSE flush timing, Scoped-vs-Singleton DI captive dependency, `GenericAnalysisHandler` fallback) and playbook runtime pitfalls G1-G12 have been consolidated into [`ai-architecture-playbook-runtime.md`](ai-architecture-playbook-runtime.md) §10. G12 (Spaarke `sprk_event`/`sprk_communication` rule, not OOB activity entities) was added 2026-06-25.

---

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| AI as BFF extension | Extend Minimal API, no separate service | Reuse auth, DI, observability infrastructure | ADR-013 |
| Configuration-driven tools | GenericAnalysisHandler as default | New tools without code deployment | ADR-013 |
| Assembly-scanned handler registration | ToolFrameworkExtensions auto-discovers handlers | Eliminates manual registration, follows ADR-010 module pattern | ADR-010 |
| Scoped handler registry | ToolHandlerRegistry is Scoped, not Singleton | Handlers may inject scoped services (IScopeResolverService) | ADR-010 |
| Redis-first caching | EmbeddingCache with SHA256 keys, 7-day TTL | Avoid redundant embedding API calls | ADR-009 |
| Per-node error isolation | ToolResult captures errors without aborting playbook | Soft failure: other nodes continue executing | ADR-016 |
| Dual output paths | Analysis Output (RTF) + Document Fields (JSON) | Different consumers need different formats | ADR-014 |
| Endpoint filters for auth | AiAuthorizationFilter per endpoint | No global middleware; fine-grained resource checks | ADR-008 |
| Three-path dispatch, no classifiers | Event / Click / Text; bounded agent loop is the only probabilistic decider | Grounded execution; closed catalogs; eval suite replaces threshold tuning (supersedes the R2 three-tier router — deleted) | ADR-039 |
| Ledger before rendering | Universal `SessionOutput` write precedes any render/send/persist | Composition backbone; auditability; disposition = rendering contract | ADR-040 |
| One confirmation gate | `PendingPlanManager` store; gating by declared `side_effect_class` + Binding risk | HITL write-back; tool-name lists forbidden | ADR-039 |
| Fail-open safety perimeter | PromptShield returns FailOpen on timeout/429/5xx | Availability over blocking; safety events logged for review | AIPU2-020 |
| Write-through Cosmos persistence | Redis hot (24h) + Cosmos warm (90d) | No idle-flush complexity; dual-write guarantees durability | AIPU2-030 |
| RBAC-only Cosmos auth | DefaultAzureCredential, no connection strings | No secrets in app settings; managed identity only | AIPU2-002 |

---

## Constraints

- **MUST** extend BFF API for all AI endpoints; no separate AI microservice (ADR-013)
- **MUST NOT** leak Graph SDK types above SpeFileStore facade (ADR-007)
- **MUST** use endpoint filters for authorization, not global middleware (ADR-008)
- **MUST** use Redis as primary cache for embeddings and search results (ADR-009)
- **MUST** register tool handlers via ToolFrameworkExtensions; keep DI registrations minimal (ADR-010)
- **MUST** log at each execution step for observability (ADR-015)
- **MUST** isolate per-node errors; do not abort entire playbook on single node failure (ADR-016)
- **MUST NOT** hardcode model names; use ModelSelectorOptions configuration (ADR-013)
- **MUST** route every AI invocation through Event / Click / Text — no second intent-detection mechanism anywhere (ADR-039)
- **MUST** write every output + tool chain to the session ledger BEFORE rendering (ADR-040)
- **MUST** gate side effects via the ONE confirmation gate by declared `side_effect_class` — never by tool-name lists (ADR-039)
- **MUST** keep both catalogs closed; user-OBO for all Dataverse tool access; **MUST NOT** land new capability on the frozen engine (OQ-2/D11)
- **MUST NOT** add routing config outside the Binding table (`sprk_playbookconsumer`)

---

## Related

- [SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md](SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md) — THE canonical architecture + component design (shipped 2026-07 redesign)
- [Chat Architecture](chat-architecture.md) — agent-turn loop, confirmation gate, SSE pipeline
- [Playbook Architecture](playbook-architecture.md) — FROZEN node-graph engine (redirect to runtime doc)
- [`.claude/adr/ADR-013-ai-architecture.md`](../../.claude/adr/ADR-013-ai-architecture.md) — AI Tool Framework constraints
- [`.claude/patterns/ai/`](../../.claude/patterns/ai/) — Pattern pointers for AI code entry points
- [JPS Authoring Guide](../guides/JPS-AUTHORING-GUIDE.md) — JPS schema, $choices, structured output
- [Scope Configuration Guide](../guides/SCOPE-CONFIGURATION-GUIDE.md) — Scope CRUD, builder UI
- [Azure AI Resources](auth-AI-azure-resources.md) — Endpoints, models, CLI commands

---

## Changelog

| Date | Version | Change |
|------|---------|--------|
| 2026-07-07 | 6.0 | spaarke-ai-architecture-redesign-r1 task 052 (FR-P4-03): aligned to the SHIPPED redesign — added "2026-07 redesign" summary (three entry paths, closed catalogs, ledger, ONE gate, dispositions); tiers 1–3 redrawn (catalog manifest, dispatch, prompted/coded executors + frozen engine); component table extended with the dispatch/gate/ledger components; Capability Router section replaced with deleted-classifier history; `spaarke-playbook-embeddings` marked orphaned; facade table refreshed (IConsumerRoutingService, IInsightsAi, IObservationMirror added; IInvokePlaybookAi deleted); ADR-039/040 constraints added. |
| 2026-06-28 | 5.1 | R7 Wave 3 (FR-16): added "Typed Executor Config Schemas" section documenting `INodeExecutor.GetConfigSchema()`, `ExecutorConfigSchema`/`ConfigSchemaField` DTOs, `GET /api/ai/playbook-builder/executor-config-schemas` endpoint, rich vs placeholder pattern (5 priority + 20 placeholder), author guidance, forward-compat (FR-27 pattern reuse), Wave 8 task 083 canvas consumer. |
| 2026-05-17 | 5.0 | R2 additions: Capability Router (3-tier), Safety Pipeline (PromptShield, Groundedness, Citations, privilege filter), Cosmos DB persistence (5 containers, write-through), Feedback Collection. Updated Tier 4, integration points, design decisions. |
| 2026-04-05 | 4.0 | Restored depth: tool handler framework internals, handler registration, streaming paths, scope resolution, knowledge retrieval, integration points, known pitfalls. Restructured to mandatory architecture doc format. |
| 2026-03-13 | 3.4 | Added DeliverToIndex node (ActionType 41). |
| 2026-03-06 | 3.3 | Added JSON Prompt Schema (JPS) documentation: $choices dynamic enum resolution with 5 Dataverse prefix types. |
| 2026-03-03 | 3.2 | Updated for typed field mappings: UpdateRecord OData PATCH with typed coercion. |
| 2026-03-01 | 3.1 | Updated for Playbook Builder R5: three-level node type system, Code Page builder as primary. |
| 2026-02-21 | 3.0 | Created from consolidation of AI-PLAYBOOK-ARCHITECTURE.md (v2.0) and AI-ANALYSIS-PLAYBOOK-SCOPE-DESIGN.md (v2.0). |
