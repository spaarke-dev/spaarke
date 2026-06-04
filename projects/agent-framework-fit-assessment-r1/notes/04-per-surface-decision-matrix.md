# Per-Surface Decision Matrix — Microsoft.Agents.AI fit for S1-S8

> **Project**: agent-framework-fit-assessment-r1 · **Task**: 004
> **Captured at**: 2026-06-03
> **Executor**: Claude Code (task-execute STANDARD rigor)
> **Purpose**: Apply the SPEC §4 decision-criteria framework (technical fit · Agent Framework value · migration cost · deployment model) to each Spaarke AI surface S1-S7 plus the two S8 discoveries from task 001. Produce one ADOPT / DON'T ADOPT / PARTIAL recommendation per surface, grounded in concrete evidence from notes/01, notes/02, notes/03 and the binding constraints (ADR-013, bff-extensions, bff-extraction-assessment).
> **Read-only**: no `.cs` files modified.
> **Tonal model**: [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) — honest, citation-backed, willing to weigh structural signal against operational reality.

---

## §0. Method note (read before scanning verdicts)

### How to read the per-surface sections

Every section S1-S8 has the same five subsections:

1. **Technical fit evaluation** — SPEC §4a criteria. Each criterion answered explicitly, including N/A.
2. **Agent Framework value** — for each of F1-F12 in notes/03, does this surface actually use it? Concrete, not abstract.
3. **Migration cost** — SPEC §4c criteria (rewrite scope, package impact, test impact, learning curve, observability impact, reversibility).
4. **Recommendation** — bold verdict: `ADOPT` / `DON'T ADOPT` / `PARTIAL`.
5. **Rationale + open questions** — ≥2 concrete evidence citations; ≥1 open question.

For ADOPT/PARTIAL surfaces, a sixth subsection applies the SPEC §4d deployment-model decision (in-process BFF / MCP server / Azure Function / Hosted Foundry).

### Anti-bias guard rail applied

The default question I asked at each surface was *"what would I write if I were arguing AGAINST adoption here?"* — applied before writing the verdict. The matrix surfaces three DON'T ADOPT verdicts (S2 wholesale, S6 swap-in, S8a) and three PARTIAL verdicts (S1, S3, S5 shipped wrapper), demonstrating the assessment is not "framework is good, therefore adopt everywhere."

### Carry-forward from upstream tasks

- **Issue #6268** (from task 000 baseline; carry-forward at notes/01 §S1 "RED FLAG"): ".NET ChatClientAgent.RunStreamingAsync ends with no assistant text on multi-tool turns" (opened 2026-06-02, status `bug / needs-maintainer-triage` as of 2026-06-03). This is **S1's canonical workload pattern** — gating any S1 lift.
- **Workflow HITL is in the framework**, not Foundry-exclusive (notes/02 §1 carry-forward; notes/03 §F7 + §F11). This narrows S5's exclusivity claim.
- **`Microsoft.Agents.AI` package is referenced but unused** in `Sprk.Bff.Api.csproj` (notes/01 top-level finding) — the package is a transitive carry-along, not a current dependency. Adopting Agents.AI does not change the publish-size budget materially.

---

## §S1. SprkChat — conversational agent

### S1.1 Technical fit evaluation (SPEC §4a)

| Criterion | Evaluation |
|---|---|
| **Conversational vs. deterministic** | Strongly conversational. `SprkChatAgent.SendMessageAsync` returns `IAsyncEnumerable<ChatResponseUpdate>` — open-ended chat with LLM-driven tool selection ([notes/01 §S1(e)](01-spaarke-ai-surfaces-inventory.md#s1-sprkchat--conversational-agent)). Canonical `ChatClientAgent` use case. |
| **Latency budget** | <500ms TTFB requirement (per ADR-013 §"Decision" — streaming TTFB <500ms is the keep-in-BFF criterion). The compound-intent gate adds ~200-400ms when triggered ([notes/01 §S1(e) lines 166-168 of `SprkChatAgent.cs`](01-spaarke-ai-surfaces-inventory.md#e-streaming-behavior)). Adoption MUST preserve or improve this. |
| **State + transactional coupling** | High. Per-turn citation context reset, plan-preview state via `PendingPlanManager` (Redis), session token-budget counter, ChatHistoryManager — all share the request lifecycle ([notes/01 §S1(f)](01-spaarke-ai-surfaces-inventory.md#f-state-management)). |
| **Durability** | Not required. Conversations are HTTP-request-bounded; survival across process restart is delegated to Redis-stored history + Dataverse-stored audit records. |
| **Tool composition** | 12+ tool classes registered per session via `AIFunctionFactory.Create`; tools are injected dynamically via `ICapabilityRouter` per turn ([notes/01 §S1(c)](01-spaarke-ai-surfaces-inventory.md#c-spaarke-specific-wrapping-abstractions)). Dynamic tool registration + LLM-driven routing IS the workload. |
| **External consumer** | No today; SprkChat is the in-product agent for Spaarke clients (PCFs, Code Pages) — not exposed externally. |
| **Streaming** | Yes — token-level via `IAsyncEnumerable<ChatResponseUpdate>`. SSE event types like `plan_preview`, `capability_change`, `output_pane` are Spaarke-defined ([notes/01 §S1(e)](01-spaarke-ai-surfaces-inventory.md#e-streaming-behavior)). |

### S1.2 Agent Framework value (notes/03 features)

| Feature | Would S1 use it? | Notes |
|---|---|---|
| **F1 `AIAgent` / `ChatClientAgent`** | Yes — directly replaces the hand-rolled `ISprkChatAgent` interface, unlocking A2A proxy + agent-as-tool composition. Caveat: Issue #6268 (notes/03 §F1) gates this. |
| **F2 `AgentSession`** | Yes — could replace `ChatHistoryManager` (notes/03 §F2). Reconciling with Spaarke's Redis-externalized history pattern (notes/01 §S1 observation 7) is non-trivial but tractable. |
| **F3 Context providers** | Yes — `KnowledgeRetrievalTools` and `DocumentSearchTools` could lift to context providers, making RAG happen before tool-call planning. notes/03 §F3 flags "evidence-thin" — no standalone Learn page within recency floor. |
| **F4 Middleware framework** | Yes — direct mapping of three hand-rolled middlewares (`AgentTelemetryMiddleware` → Agent Run; `AgentContentSafetyMiddleware` → IChatClient; `AgentCostControlMiddleware` → Agent Run) per notes/03 §F4. |
| **F5 Structured outputs** | Yes — `CompoundIntentDetector`'s ad-hoc JSON parsing replaced by `RunAsync<CompoundIntent>` (notes/03 §F5). Marginal benefit (existing parser works), but cleaner. |
| **F6 Tools + `AsAIFunction()`** | Yes (tools already use `AIFunctionFactory`); `AsAIFunction` is value-add for breaking the monolithic chat into composed specialists ([notes/03 §F6](03-agent-framework-feature-map.md#f6-tools--aifunctionfactory-description-agent-as-tool-hosted-mcp-code-execution)). |
| **F7 Workflows** | No — SprkChat is single-agent conversational. Workflows is for multi-step orchestration; S2 territory. |
| **F8 MCP client** | Yes (optional) — could consume `learn.microsoft.com/api/mcp` or other external MCP via Hosted MCP. Not a current Spaarke requirement. |
| **F9 A2A** | No today; could expose SprkChat over A2A in the future but not a current need. |
| **F10 Observability** | Yes — `WithOpenTelemetry()` replaces hand-rolled telemetry (notes/03 §F10). Per the duplication warning, agent-level (NOT chat-client level) is the right tier here. |
| **F11 Tool Approval (HITL)** | Yes — `ApprovalRequiredAIFunction` + `FunctionApprovalRequestContent` is a direct replacement candidate for the `CompoundIntentDetector` + raw-client + function-invoking-client split (notes/03 §F11). Caveat: Spaarke's compound-intent policy is richer than "ask human" — the framework handles routing; Spaarke policy still decides which functions to wrap. |
| **F12 Hosting helpers** | Marginal — `builder.AddAIAgent(...)` simplifies wiring but the current `ISprkChatAgent` registration already works. |

### S1.3 Migration cost (SPEC §4c)

| Criterion | Evaluation |
|---|---|
| **Rewrite scope** | Medium-high. Touches `Services/Ai/Chat/SprkChatAgent.cs`, `SprkChatAgentFactory.cs`, all three middleware classes (`Middleware/Agent*Middleware.cs`), `CompoundIntentDetector.cs`, `ChatHistoryManager.cs`, `ChatSessionManager.cs`, and the `AiModule.cs` DI registrations ([notes/01 §S1(a)+(g)](01-spaarke-ai-surfaces-inventory.md#a-primary-classservice)). Endpoint layer (`Api/Ai/ChatEndpoints.cs`) and bespoke SSE event types likely preserved. |
| **Package + publish-size impact** | Zero net — `Microsoft.Agents.AI 1.0.0-rc1` is already referenced ([notes/01 top-level finding](01-spaarke-ai-surfaces-inventory.md#top-level-finding-microsoftextensionsai-vs-microsoftagentsai)). Adoption activates code paths in an already-shipped assembly. |
| **Test impact** | Substantial. The mock surface shifts from `IChatClient` → `AIAgent`. ~2,800 AI tests exist per the bff-extraction assessment §5; SprkChat-specific tests will need re-keying. Per-tool unit tests largely unaffected (`AIFunctionFactory` keeps the tool surface). |
| **Team learning curve** | Low-medium. Engineers already familiar with Extensions.AI primitives. Agents.AI builds on top — the new types are `ChatClientAgent`, `AgentSession`, the three middleware-tier registration helpers, and `ApprovalRequiredAIFunction`. Notes/03 §F1-§F4 + §F10-§F11 cover the surface. |
| **Observability impact** | Improved. Framework `WithOpenTelemetry()` emits standardized GenAI Semantic Conventions attributes (notes/03 §F10) which Spaarke's hand-rolled `AgentTelemetryMiddleware` does not. |
| **Reversibility** | Medium. The hand-rolled interface (`ISprkChatAgent`) could be kept as a facade over the framework agent, allowing rollback. The middleware migration is mostly mechanical; reverting is cheap per middleware class. |

### S1.4 Recommendation

**PARTIAL — gated on Issue #6268 resolution.**

The structural fit is the strongest of any surface in this assessment. Every Agents.AI feature except F7 (Workflows) and F9 (A2A) maps cleanly to existing Spaarke code that already approximates the framework idiom. The middleware composition (notes/01 cross-cutting observation 3) is the most visible signal — Spaarke is decorating `ISprkChatAgent` instead of `IChatClient`, exactly the kind of hand-rolled equivalent that the framework subsumes.

But: **SprkChat's canonical workload is multi-tool streaming**. The compound-intent detector + `UseFunctionInvocation` pipeline is multi-tool by construction; SSE token streaming is the user-facing requirement. **GitHub Issue #6268 (opened 2026-06-02) reports the exact failure mode**: `ChatClientAgent.RunStreamingAsync` ends with no assistant text on multi-tool turns. As of synthesis date (2026-06-03) the issue is `needs-maintainer-triage` — meaning the bug is acknowledged but not fixed. Adopting `ChatClientAgent` before #6268 resolves would ship a regression in the most-trafficked SprkChat code path.

### S1.5 Rationale (≥2 concrete evidence pieces)

- **Evidence 1**: notes/01 §S1(b) — Spaarke uses `Microsoft.Extensions.AI` primitives directly (`IChatClient`, `AIFunction`, `ChatResponseUpdate`, `FunctionCallContent`); zero use of `ChatClientAgent` or `AIAgent`. Lifting to the framework changes the abstraction, not the dependency set.
- **Evidence 2**: notes/01 §S1 carry-forward + notes/03 §F1 primary source — Issue #6268 `https://github.com/microsoft/agent-framework/issues/6268` (fetched 2026-06-03) reports streaming + multi-tool turns ends with no assistant text. This is S1's exact workload.
- **Evidence 3**: notes/01 cross-cutting observation 3 — Spaarke's middleware decorates `ISprkChatAgent`, NOT `IChatClient`, signaling the framework idiom is approximated but not adopted. Lift cost is mechanical, not structural.

**Agent Framework features S1 would use post-lift**: F1, F2, F4, F5, F6 (`AsAIFunction` only), F10, F11. F7/F8/F9/F12 either non-applicable or marginal.

### S1.6 Deployment model (SPEC §4d)

**In-process BFF** — ADR-013 default. All four ADR-013 §"Constraints" keep-in-BFF criteria (latency, state, streaming, no duplication) hold. No exception to claim.

### S1.7 Open questions for human decision

1. **Wait or fork?** Should Spaarke wait for Issue #6268 to land in a shipped Agent Framework 1.x release before lifting S1, or pilot the lift now with a feature flag and a fallback to the hand-rolled path? (Decision criterion: how much of SprkChat traffic is multi-tool — if >50%, wait; if <20%, pilot.)
2. **Compound-intent gate**: Does Spaarke's compound-intent policy fit cleanly into `ApprovalRequiredAIFunction` (framework-level routing) + Spaarke-level policy on which functions to wrap, or are there policy edge cases (e.g., conditional approval based on user role, scope, document) that don't map to the framework's binary approve/reject? Owner-level decision since it determines whether F11 is "swap-in" or "framework + Spaarke policy code".
3. **Session externalization**: How does Spaarke reconcile its Redis-externalized chat history with `AgentSession`'s in-memory + remote-conversation-id model (notes/01 cross-cutting observation 7)? The framework supports `CreateSessionAsync(conversationId)` for remote-thread binding, but does this work cleanly when the "remote thread" is just Spaarke's own Redis cache?

---

## §S2. AnalysisOrchestration + JPS playbooks

### S2.1 Technical fit evaluation (SPEC §4a)

| Criterion | Evaluation |
|---|---|
| **Conversational vs. deterministic** | Strongly deterministic. JPS defines a fixed-graph node executor pipeline whose steps are known at playbook definition time ([notes/01 §S2(a)+(c)](01-spaarke-ai-surfaces-inventory.md#s2-analysisorchestration--jps-playbooks--deterministic-multi-step-pipelines)). No LLM-driven step routing. |
| **Latency budget** | High. `IPlaybookOrchestrationService` streams `PlaybookStreamEvent` over `IAsyncEnumerable` ([notes/01 §S2(e)](01-spaarke-ai-surfaces-inventory.md#e-streaming-behavior-1)). Per-token throughput preserved via `IOpenAiClient.StreamCompletionAsync` (Spaarke wrapper). Replacing this with framework primitives requires preserving streaming SLAs. |
| **State + transactional coupling** | High. Dataverse `sprk_analysisoutput` records + Redis-cached `AnalysisInternalModel` + per-stream working-document updates (`AnalysisOrchestrationService.cs:230-234`) all coupled to the BFF request lifecycle ([notes/01 §S2(f)](01-spaarke-ai-surfaces-inventory.md#f-state-management-1)). |
| **Durability** | Partial — playbook continuation via `ContinueAnalysisAsync` re-hydrates from Dataverse + Redis. Not "survives process restart in the middle of a stream" durability; it's "survives stream interruption and can be resumed." |
| **Tool composition** | Static. JPS nodes register `IAiToolHandler` implementations resolved per-node via `IToolHandlerRegistry` ([notes/01 §S2(c)](01-spaarke-ai-surfaces-inventory.md#c-spaarke-specific-wrapping-abstractions-1)). Not LLM-driven routing — playbook-author-defined. |
| **External consumer** | No today; consumed by Spaarke clients via `/api/ai/analysis/*` and `/api/ai/playbooks/*` per ADR-013 §"Architecture Overview". |
| **Streaming** | Yes — Spaarke-defined `AnalysisStreamChunk` and `PlaybookStreamEvent` types. Not framework streaming primitives. |

### S2.2 Agent Framework value (notes/03 features)

| Feature | Would S2 use it? | Notes |
|---|---|---|
| **F1 `AIAgent` / `ChatClientAgent`** | No. JPS doesn't have an "agent loop" to wrap. Per-node LLM calls go through `IOpenAiClient` (Spaarke wrapper, NOT `IChatClient`) ([notes/01 §S2(b)](01-spaarke-ai-surfaces-inventory.md#b-microsoft-abstractions-used)). |
| **F2 `AgentSession`** | No. JPS state is graph-shaped (topological ordering), not chat-shaped (notes/01 §S2(f)). |
| **F3 Context providers** | No. JPS context is built via `IAnalysisContextBuilder` from playbook-defined inputs, not provider-injected. |
| **F4 Middleware framework** | No. S2 has no middleware composition (notes/01 §S2(g) — "None. S2 has no middleware pipeline."). |
| **F5 Structured outputs** | No net win. JPS nodes already produce structured outputs via raw OpenAI `response_format`; relifting through `RunAsync<T>` requires switching to `ChatClientAgent` which doesn't fit the per-node model (notes/03 §F5 Spaarke applicability). |
| **F6 Tools** | No. JPS tools are JPS-graph-resolved, not OpenAI-tool-call-resolved (notes/03 §F6 Spaarke applicability — "JPS tools are JPS-graph-resolved, not OpenAI-tool-call-resolved"). |
| **F7 Workflows** | **The only material candidate.** JPS's `IPlaybookExecutionEngine` + `ExecutionGraph` + node executors maps conceptually to `WorkflowBuilder` + `Executor<TIn,TOut>` + edges (notes/03 §F7 Spaarke applicability). But this is a **wholesale replacement of JPS**, not an additive lift. |
| **F8 MCP client** | No. JPS isn't tool-call shaped. |
| **F9 A2A** | No. |
| **F10 Observability** | No — JPS uses raw OpenAI SDK; framework instrumentation requires `IChatClient` or `AIAgent` (notes/03 §F10 Spaarke applicability). |
| **F11 Tool Approval** | No — no agent loop to gate. |
| **F12 Hosting helpers** | No — not agent-shaped. |

### S2.3 Migration cost (SPEC §4c)

| Criterion | Evaluation |
|---|---|
| **Rewrite scope** | **Catastrophic if attempted as a swap-in.** JPS spans 12 node executors under `Services/Ai/Nodes/`, the orchestration service (`AnalysisOrchestrationService.cs`), the playbook service (`IPlaybookService`), the execution engine (`IPlaybookExecutionEngine`), the topological graph (`ExecutionGraph`), the tool handler registry (`IToolHandlerRegistry`), all 10+ supporting handler implementations, plus the Dataverse playbook entity schema and the JPS schema validation pipeline ([notes/01 §S2(a)+(c)](01-spaarke-ai-surfaces-inventory.md#s2-analysisorchestration--jps-playbooks--deterministic-multi-step-pipelines)). The JPS schema is also consumed by `Sprk.Dataverse.Plugins/PlaybookValidationPlugin` (outside `Sprk.Bff.Api`). |
| **Package + publish-size impact** | Minimal direct — `Microsoft.Agents.AI.Workflows` adds nominal weight. However, the larger risk is the cascading rewrite touching ~30+ files across `Services/Ai/`. |
| **Test impact** | Catastrophic — every node executor test, every playbook integration test, every JPS schema validation test re-keys. |
| **Team learning curve** | High. Workflows is a new paradigm; the team is fluent in JPS today. |
| **Observability impact** | Mixed — framework standardized GenAI conventions vs. existing JPS-tailored telemetry. |
| **Reversibility** | Poor. JPS playbook records persisted in Dataverse encode the JPS schema; a workflow rewrite would create a forward-only data migration. |

### S2.4 Recommendation

**DON'T ADOPT.**

JPS is a working production system with its own DSL, executor model, persistence layer, validation pipeline, and Dataverse-stored playbook records ([notes/01 §S2(a)+(f)](01-spaarke-ai-surfaces-inventory.md#s2-analysisorchestration--jps-playbooks--deterministic-multi-step-pipelines)). `Microsoft.Agents.AI.Workflows` is conceptually competitive — it has executors, edges, supersteps, checkpoints, multi-agent orchestration patterns (notes/03 §F7) — and structurally it could host most of what JPS does. But "could host most" is not adoption-grade evidence.

The binary nature of this decision matters: there is **no incremental adoption path** for S2. Workflows replaces the orchestration engine; you can't "lift one node to a Workflow executor" because the surrounding `ExecutionGraph` + `IToolHandlerRegistry` + `INodeService` types don't know how to host one. Either JPS stays, or JPS becomes Workflows wholesale.

Migration cost (catastrophic across 30+ files + Dataverse schema + plugins) is grossly disproportionate to the benefit. JPS-specific features that Workflows does not natively provide — JPS schema validation, the Dataverse-persisted playbook records, the `IAiToolHandler` contract that's purpose-built for playbook-driven orchestration (NOT interactive analysis per `IAiToolHandler.cs:11` doc-comment), `ActionType.AgentService = 60` for Foundry routing — would need to be re-implemented or re-exposed on top of Workflows.

### S2.5 Rationale (≥2 concrete evidence pieces)

- **Evidence 1**: notes/01 §S2(b) — `AnalysisOrchestrationService` and all 12 node executors under `Services/Ai/Nodes/` use NEITHER `Microsoft.Extensions.AI` NOR `Microsoft.Agents.AI` types. The "agent framework adoption" path is rebuilding the entire surface on a different abstraction, not lifting an existing one.
- **Evidence 2**: notes/01 §S2 cross-cutting note + `IAiToolHandler.cs:11` doc-comment cited there — JPS tooling is "playbook-driven orchestration, not interactive document analysis." This is a different shape from what Workflows targets (multi-agent orchestration with HITL).
- **Evidence 3**: notes/03 §F7 — Workflows is "the only material candidate" for S2, but the section explicitly names migration cost as the deciding question. The framework can express most JPS executors; the cost to do so is structurally large.

**Specific reasons DON'T ADOPT** (per task instruction):
1. JPS is JPS-driven/deterministic; LLM-driven routing is not the workload (so F1, F6, F11 don't add value).
2. Workflows is competitively functional but migration cost ≫ benefit (rewrite of 30+ files + schema + plugins, no incremental path).
3. JPS-Dataverse schema integration is forward-only-migratable; rollback path is poor.
4. S2 already meets ADR-013 latency + transactional coupling requirements in-BFF; no operational pressure to change.

### S2.6 Open questions for human decision

1. **JPS-vs-Workflows long-term**: This assessment recommends "don't adopt now." Does the team want a forward-looking decision on whether JPS becomes the long-term home of multi-step analysis in Spaarke, or whether JPS is a transitional pattern that should be replaced (whether by Workflows or otherwise) when the team has bandwidth? This is an architecture-group decision, not assessment-grade.
2. **Selective Workflows piloting**: Are there NEW workflow-shaped use cases (NOT existing JPS playbooks) where Workflows could be introduced alongside JPS as a parallel path? The shipped Foundry wrapper's planned canonical durable surface (S5 below) is one candidate.

---

## §S3. Builder agent — playbook builder

### S3.1 Technical fit evaluation (SPEC §4a)

| Criterion | Evaluation |
|---|---|
| **Conversational vs. deterministic** | Conversational (multi-round tool-routing). `BuilderAgentService.ExecuteAsync` implements a hand-written agentic loop bounded by `MaxToolRounds = 10` ([notes/01 §S3(c)](01-spaarke-ai-surfaces-inventory.md#c-spaarke-specific-wrapping-abstractions-2)). Canonical `ChatClientAgent` use case. |
| **Latency budget** | Soft. Builder is a builder-tool agent (playbook construction), not a user-facing chat surface. Per-round latency tolerated; multi-round non-streaming completions ([notes/01 §S3(e)](01-spaarke-ai-surfaces-inventory.md#e-streaming-behavior-2)). |
| **State + transactional coupling** | Low. `CanvasState` passed in per request; canvas operations accumulated and returned ([notes/01 §S3(f)](01-spaarke-ai-surfaces-inventory.md#f-state-management-2)). No session or audit coupling. |
| **Durability** | Not required. Single-request bounded. |
| **Tool composition** | Hand-rolled. `BuilderToolDefinitions` + `BuilderToolExecutor` are bespoke; `ExecuteToolCallAsync` does manual JSON serialization ([notes/01 §S3(c)](01-spaarke-ai-surfaces-inventory.md#c-spaarke-specific-wrapping-abstractions-2)). |
| **External consumer** | No. |
| **Streaming** | No — single `Task<BuilderAgentResult>` return. |

### S3.2 Agent Framework value (notes/03 features)

| Feature | Would S3 use it? | Notes |
|---|---|---|
| **F1 `AIAgent` / `ChatClientAgent`** | Yes — replaces the hand-rolled agentic loop with the framework-standard `RunAsync` pattern. notes/03 §F1 Spaarke applicability: "BuilderAgentService already does intent-classification + tool-routing; this is the canonical `ChatClientAgent` use case." |
| **F2 `AgentSession`** | Marginal — Builder is largely single-turn per request (notes/03 §F2). |
| **F3 Context providers** | Yes — relevant for scope catalog grounding (notes/03 §F3 Spaarke applicability). Currently injected via prompt templates. |
| **F4 Middleware framework** | Applies if Builder lifts to `ChatClientAgent`; currently Builder runs against `OpenAI.Chat` SDK types directly so middleware doesn't compose (notes/03 §F4 Spaarke applicability). |
| **F5 Structured outputs** | Yes, strong fit. Builder's intent-classification + tool-routing produces structured envelopes; `RunAsync<BuilderIntent>` is idiomatic (notes/03 §F5 Spaarke applicability: "strong fit"). |
| **F6 Tools** | Yes — `AIFunctionFactory.Create` replaces the bespoke `BuilderToolDefinitions` + `BuilderToolExecutor` (notes/03 §F6 Spaarke applicability: "lifting to `AIFunctionFactory.Create` is a clear win for consistency"). |
| **F7 Workflows** | Partial — Builder is a single tool-routing agent today; if it evolves to "interpret intent, then run a sub-workflow", Workflows would fit. Not current scope. |
| **F8 MCP client** | No current need. |
| **F9 A2A** | No current need. |
| **F10 Observability** | Yes after F1 lift. |
| **F11 Tool Approval** | Possible future fit (e.g., "create this scope?"); not current. |
| **F12 Hosting helpers** | Marginal. |

### S3.3 Migration cost (SPEC §4c)

| Criterion | Evaluation |
|---|---|
| **Rewrite scope** | Small. `BuilderAgentService.cs`, `BuilderToolDefinitions.cs`, `BuilderToolExecutor.cs`, plus DI registration. ~3-5 files. The handwritten agentic `for` loop is replaced by `agent.RunAsync(...)`; the manual JSON serialization is replaced by `AIFunctionFactory.Create`. |
| **Package + publish-size impact** | Zero net. Builder currently uses `OpenAI.Chat` SDK types directly ([notes/01 §S3(b)](01-spaarke-ai-surfaces-inventory.md#b-microsoft-abstractions-used-1)); lifting to `ChatClientAgent` activates already-shipped `Microsoft.Agents.AI` code paths. |
| **Test impact** | Small. Builder is non-streaming, single-method-entry-point — easier to mock at the agent level than the OpenAI SDK level. |
| **Team learning curve** | Low. Builder is the canonical onboarding surface for the framework — it's exactly the example most Learn pages use. |
| **Observability impact** | Improved. Framework standardized telemetry vs. inline logging at the agentic loop. |
| **Reversibility** | High. ~3-5 file surface; rollback is git-revert-cheap. |

### S3.4 Recommendation

**PARTIAL — adopt at next significant Builder maintenance window.**

Builder is structurally the cleanest candidate for `ChatClientAgent` adoption. The hand-written `for` loop bounded by `MaxToolRounds = 10` and the manual `OpenAI.Chat` SDK juggling ([notes/01 §S3(c)](01-spaarke-ai-surfaces-inventory.md#c-spaarke-specific-wrapping-abstractions-2)) are exactly what `ChatClientAgent` + `RunAsync` + `AIFunctionFactory.Create` collapses.

Why PARTIAL and not full ADOPT: Builder is **in flight** (notes/01 §S3 doesn't mark it production-only). Adopting Agents.AI on a moving target adds change cost; the lift makes more sense at the next natural maintenance window (e.g., the first major Builder feature addition, or when Issue #6268 lands and the team is already touching `Microsoft.Agents.AI` code for S1). PARTIAL captures: "yes adopt, not now."

There is **one ADOPT-blocker for S3 specifically**: Builder uses the **OpenAI.Chat SDK directly**, not Extensions.AI's `IChatClient`. Migrating to `ChatClientAgent` requires routing through an `IChatClient` provider helper (e.g., `OpenAIClient.AsAIAgent(...)` or wiring Azure OpenAI via `AddChatClient(...)`). This is a one-time DI change but worth flagging — Builder's current pattern is further from the framework than SprkChat's.

### S3.5 Rationale (≥2 concrete evidence pieces)

- **Evidence 1**: notes/01 §S3(c) — `BuilderAgentService.cs:97` implements an explicit `for` loop bounded by `MaxToolRounds = 10` with hand-written tool routing via `ExecuteToolCallAsync` at line 134. This is the workload `ChatClientAgent.RunAsync` provides.
- **Evidence 2**: notes/03 §F1 Spaarke applicability — "BuilderAgentService already does intent-classification + tool-routing; this is the canonical `ChatClientAgent` use case. Adoption value is highest here after S1."
- **Evidence 3**: notes/01 §S3(b) — Builder uses `OpenAI.Chat` SDK directly with hardcoded `"gpt-4o"` model name (`BuilderAgentService.cs:50`). Standardizing on Extensions.AI / Agents.AI removes the SDK-direct dependency.

**Agent Framework features S3 would use post-lift**: F1, F5, F6, F10. F4 (middleware) optional. F11 future-relevant.

### S3.6 Deployment model (SPEC §4d)

**In-process BFF** — ADR-013 default. Builder is the playbook-construction surface for the Spaarke product; latency/state coupling minor but consumer is the in-product client. No exception to claim.

### S3.7 Open questions for human decision

1. **Timing**: Is there value in adopting S3 first (before S1, before #6268 resolves) to validate the framework in a low-risk, non-user-facing surface? Or does the team prefer to wait for the S1 lift to drive S3 along with it?
2. **OpenAI SDK migration**: Does Builder's lift require pre-work to route LLM calls through `IChatClient` (rather than `IOpenAiClient` Spaarke wrapper which itself wraps OpenAI.Chat)? Decision affects scope estimate.

---

## §S4. Background AI jobs

### S4.1 Technical fit evaluation (SPEC §4a)

| Criterion | Evaluation |
|---|---|
| **Conversational vs. deterministic** | Strongly deterministic. Each job handler is a single-purpose `ProcessAsync(JobContract)` returning `Task<JobOutcome>` ([notes/01 §S4(b)+(e)](01-spaarke-ai-surfaces-inventory.md#s4-background-ai-jobs--service-bus-driven-ai-work)). No agentic loop, no tool calling. |
| **Latency budget** | None against BFF state. Service Bus polls the BFF process internally; no synchronous user wait. |
| **State + transactional coupling** | Low. Idempotency keys via `IIdempotencyService` (Redis per ADR-009); per-message state persisted in Dataverse + Azure AI Search ([notes/01 §S4(f)](01-spaarke-ai-surfaces-inventory.md#f-state-management-3)). No HTTP-request coupling. |
| **Durability** | Yes — but via Service Bus + idempotency, not via framework workflows. Job handlers are crash-safe today. |
| **Tool composition** | None — single-purpose handlers. |
| **External consumer** | No — internal to BFF. |
| **Streaming** | No. |

### S4.2 Agent Framework value (notes/03 features)

| Feature | Would S4 use it? | Notes |
|---|---|---|
| **F1 `AIAgent`** | No. notes/03 §F1: "S4 — not applicable in general; individual job handlers are deterministic pipelines, not agent loops." |
| **F2-F6** | No. Same reasoning. |
| **F7 Workflows** | Speculative future — durable workflow hosting (`04-hosting/DurableWorkflows` sample) could re-host the S4 pipelines, but S4 doesn't currently use Agent Framework primitives, so this is "future architectural option" not "swap-in win" (notes/03 §F7 Spaarke applicability). |
| **F8-F12** | No. |

### S4.3 Migration cost (SPEC §4c)

| Criterion | Evaluation |
|---|---|
| **Rewrite scope** | Large if `EmbeddingMigrationService` (the only S4 handler with direct `IOpenAiClient` injection per notes/01 §S4) were lifted to `ChatClientAgent`. For the others (which delegate to `IAppOnlyAnalysisService` or `IFileIndexingService`), lift is N/A — the wrapped services would lift first if at all (and those are S2 territory). |
| **Package + publish-size impact** | Zero. |
| **Test impact** | N/A — nothing to test if nothing changes. |
| **Team learning curve** | N/A. |
| **Observability impact** | Same OTel + Application Insights wiring as today. |
| **Reversibility** | N/A. |

### S4.4 Recommendation

**DON'T ADOPT.**

S4 is not agent-shaped. The 13+ job handlers are single-purpose `ProcessAsync` methods — closer to "function" than "agent" in the framework sense. The framework's value-add (agent base, sessions, middleware composition, tool routing, HITL gates) doesn't apply.

`EmbeddingMigrationService` is the one S4 handler that directly injects `IOpenAiClient` (notes/01 §S4(b)) — it's a candidate for routing through Extensions.AI's `IChatClient` if Spaarke wants to consolidate on Extensions.AI as the LLM-call abstraction, but that's not Agents.AI adoption, that's Extensions.AI consolidation. Not in scope for this assessment.

The job-handler framework itself (`IJobHandler`, `JobContract`, `JobOutcome`, `IIdempotencyService`, `ServiceBusJobProcessor`) is Spaarke-defined and works. Replacing it with `Microsoft.Agents.AI.Workflows` durable hosting would mean abandoning a working Service Bus + Redis + Idempotency contract for a framework feature S4 doesn't need.

### S4.5 Rationale (≥2 concrete evidence pieces)

- **Evidence 1**: notes/01 §S4(a)+(b) — "No `Microsoft.Extensions.AI` types in any S4 handler" + handlers delegate to a thicker service (`IAppOnlyAnalysisService`, `IFileIndexingService`). The AI work is one level deeper than the handler.
- **Evidence 2**: notes/03 §F1 Spaarke applicability — "S4 Background jobs — not applicable in general; individual job handlers are deterministic pipelines, not agent loops."
- **Evidence 3**: ADR-013 §"Constraints" — "MUST use Job Contract for background AI work (ADR-004) ... MUST NOT host AI BFF synthesis/streaming endpoints in Azure Functions." S4 already conforms; the framework doesn't change this and isn't operationally pressuring.

**Specific reasons DON'T ADOPT** (per task instruction):
1. Handlers are deterministic single-purpose pipelines, not agent loops (so F1 has no fit).
2. AIAgent base provides nothing that Service Bus + a direct LLM call doesn't already provide. The framework doesn't replace Service Bus + idempotency.
3. Workflows could re-host the pipelines but at the cost of re-implementing the Job Contract + Idempotency layer.

### S4.6 Open questions for human decision

1. **Extensions.AI consolidation**: Should `EmbeddingMigrationService`'s direct `IOpenAiClient` injection be rerouted through `IChatClient` for consistency with S1 and the broader Extensions.AI consolidation pattern, independent of any Agents.AI adoption? (This is a Spaarke-internal hygiene question, not framework adoption.)
2. **Future durable-workflow re-host**: If S5 canonical durable HITL workflows (below) get implemented as `Microsoft.Agents.AI.Workflows`, does that open a path to re-hosting some S4 pipelines (e.g., the email-to-document pipeline `ProfileSummaryJobHandler` chains via Service Bus) as Workflows? If yes, when? This is multi-quarter future-state and out of current scope.

---

## §S5. Foundry Agent Service overlap — BIMODAL

Per notes/02 §2 — S5 is two distinct surfaces with two distinct decisions.

### S5A. Shipped in-BFF Foundry wrapper

#### S5A.1 Technical fit evaluation (SPEC §4a)

| Criterion | Evaluation |
|---|---|
| **Conversational vs. deterministic** | Conversational backend; routing layer is keyword-classified. `AgentServiceClient` wraps `Azure.AI.Projects.AgentsClient` for chat-backend routing ([notes/02 §2.2](02-non-bff-ai-touchpoints-inventory.md#22-what-the-in-bff-foundry-code-actually-is)). |
| **Latency budget** | Same as SprkChat — coexists in the SprkChat pipeline as `AgentServiceRoutingMiddleware`. |
| **State + transactional coupling** | Medium. Redis-cached thread IDs keyed by `agent-thread:{tenantId}` (ADR-009 + sliding 60min TTL per [notes/02 §2.3(f)](02-non-bff-ai-touchpoints-inventory.md#23-s5--six-required-fields)). |
| **Durability** | None — synchronous request/response. |
| **Tool composition** | Foundry-side, opaque to BFF — the agent definition (model + tools + instructions) is pre-provisioned in Foundry; BFF just routes threads to it. |
| **External consumer** | No (internal SprkChat routing decision). |
| **Streaming** | Internally yes (Foundry response streams back through the wrapper to the SprkChat SSE stream). |

#### S5A.2 Agent Framework value

| Feature | Would S5A use it? | Notes |
|---|---|---|
| **F1 `AIAgent`** | Yes — `AIProjectClient.AsAIAgent(...)` returns an `AIAgent` typed wrapper around the Foundry agent (notes/03 §F1 Spaarke applicability). The shipped `AgentServiceClient` bypasses this and uses `AgentsClient` directly. |
| **F2 `AgentSession`** | Yes — `ChatClientAgent.CreateSessionAsync(conversationId)` is "exactly the bridge between local agents and the Foundry-stored thread; the current `Services/Ai/Foundry/` wrapper does this manually" (notes/03 §F2 Spaarke applicability). |
| **F6 Tools (hosted MCP)** | Yes when Foundry agent definition includes hosted MCP tools — declarable via `MCPToolDefinition` + `MCPToolResource` (notes/03 §F6, §F8). |
| **F10 Observability** | Yes after F1 lift. |
| **F11 Tool Approval** | Yes — Foundry-side approval via `MCPApproval` propagates to the framework's `FunctionApprovalRequestContent` model. |

#### S5A.3 Migration cost

Small. Replace `AgentServiceClient.cs`'s direct `AgentsClient` usage with `AIProjectClient.AsAIAgent(...)`. ~1 file primary change; `AgentServiceRoutingMiddleware` and `AgentServiceNodeExecutor` consumers preserved with minor signature updates. Test impact small (the wrapper is feature-flagged off by default per ADR-018, so the test surface is the smoke-test + intent-routing path).

#### S5A.4 Recommendation

**PARTIAL — adopt only as part of an S1 lift (not in isolation).**

S5A is structurally a candidate for adoption — `AsAIAgent` simplifies the wrapper, `AgentSession.CreateSessionAsync(conversationId)` replaces the manual Redis-cached-thread-id manipulation, hosted MCP tools become declarable. But:

- S5A consumes from `SprkChatAgent`-shaped contexts ([notes/02 §2.2](02-non-bff-ai-touchpoints-inventory.md#22-what-the-in-bff-foundry-code-actually-is) — `AgentServiceRoutingMiddleware` is the SprkChat pipeline). Lifting S5A without S1 means mixing framework `AIAgent` (Foundry side) with hand-rolled `ISprkChatAgent` (SprkChat side) — a more complex intermediate state than either pure choice.
- S5A is **default-OFF per ADR-018**. Adoption value is low until / unless the kill switch is flipped on; otherwise the lift improves code that doesn't run.

Recommendation: **wait for S1 lift, then lift S5A in the same PR set**. Standalone S5A lift is not productive.

#### S5A.5 Rationale (≥2 concrete evidence pieces)

- **Evidence 1**: notes/02 §2.2 — `AgentServiceClient` connects to a pre-provisioned Foundry Agent endpoint, manages threads via direct SDK calls, uses Redis for thread persistence. The framework's `AsAIAgent(...)` + `CreateSessionAsync(conversationId)` collapse this to two API calls.
- **Evidence 2**: notes/02 §2.3(c) — wrapper is "default-OFF (ADR-018), opt-in only." Low operational pressure.
- **Evidence 3**: notes/03 §F1 Spaarke applicability — "Adopting `AIAgent` would simplify the wrapper" (explicit S5 fit statement).

#### S5A.6 Deployment model

In-process BFF (unchanged) — wrapper, not Foundry-hosted agent itself.

---

### S5B. Planned canonical durable / HITL legal workflows (no Spaarke code yet)

#### S5B.1 Technical fit evaluation (SPEC §4a)

| Criterion | Evaluation |
|---|---|
| **Conversational vs. deterministic** | Mixed — multi-agent durable workflows over legal documents, with both deterministic steps (extraction, comparison) and agentic steps (negotiation review, regulatory monitoring) per `knowledge/foundry-agent-service/NOTES.md` use cases. |
| **Latency budget** | None — multi-day workflows by design ([notes/02 §2.3(f)](02-non-bff-ai-touchpoints-inventory.md#23-s5--six-required-fields)). |
| **State + transactional coupling** | High durability state, low BFF transactional coupling. |
| **Durability** | HIGH — survive process restart, span days, wait on external HITL signals. ADR-013 keep-in-BFF criterion FAILS (rightly so). |
| **Tool composition** | Multi-agent with handoff / A2A composition; MCP tools (hosted). |
| **External consumer** | A2A peer agents potentially yes. |
| **Streaming** | Workflow events stream out; not user-facing token streaming. |

#### S5B.2 Agent Framework value

| Feature | Would S5B use it? | Notes |
|---|---|---|
| **F1 `AIAgent`** | Yes. |
| **F2 `AgentSession`** | Yes. |
| **F7 Workflows** | **Central.** `WorkflowBuilder` + checkpoints + supersteps + multi-agent orchestration are exactly the multi-day legal workflow story (notes/03 §F7 Spaarke applicability). |
| **F8 MCP (hosted + local)** | Yes — Foundry-hosted agents consume Spaarke MCP servers (S7 territory) and external MCP servers. |
| **F9 A2A** | Yes — durable workflows expose / consume A2A peers. |
| **F11 Tool Approval / Workflow HITL (`RequestPort`)** | **Central** — exactly the canonical durable HITL story (notes/02 §5 observation 2: "Agent Framework Workflows themselves now ship `RequestPort` / `RequestInfoEvent` HITL primitives"). |
| **F12 Hosting (Foundry-hosted agents)** | Central if Spaarke chooses Foundry-hosted-agent deployment (notes/03 §F12 Spaarke applicability: "`FoundryHostedAgents` sample category is the canonical hosting pattern for the curated HITL legal workflows"). |

#### S5B.3 Migration cost

N/A — there is no Spaarke code to migrate. This is greenfield implementation cost, not migration cost. The relevant questions: (a) does the team have bandwidth to author multi-day legal workflows; (b) which deployment model — Workflows in BFF / Workflows in Function / Foundry-hosted agents.

#### S5B.4 Recommendation

**ADOPT — when the canonical durable legal workflows surface gets a project SPEC.**

S5B is the highest-fit surface in the entire assessment. Every Agents.AI feature with HITL or durability flavor (F7 Workflows, F11 Tool Approval / `RequestPort`, F12 Foundry hosting) is purpose-built for what `knowledge/foundry-agent-service/NOTES.md` §1 §2 describes: multi-day legal workflows with HITL gates (NDA negotiation, full-matter diligence, regulatory monitoring).

The critical nuance is what notes/02 §5 observation 2 names: **HITL is no longer Foundry-exclusive**. The framework's `RequestPort` + `RequestInfoEvent` + workflow checkpoints provide pause/resume HITL semantics. Foundry's residual differentiation (per-session VM sandboxes, per-agent Entra identity, A2A endpoint exposure, Foundry-hosted MCP tools, Foundry memory) is real but narrower than "Foundry owns durable + HITL."

So the S5B decision splits into two sub-decisions:
- **Adoption of `Microsoft.Agents.AI` itself**: YES — for any durable legal workflow Spaarke ships, Workflows is the framework choice over a hand-rolled equivalent.
- **Deployment**: depends on whether VM isolation + per-agent Entra identity + A2A exposure are required. If yes → Foundry-hosted. If no → BFF-hosted Workflows or Function-hosted Workflows per ADR-013 §"Decision".

#### S5B.5 Rationale

- **Evidence 1**: notes/02 §2.3(f) (planned canonical) — "HIGH" durability + HITL territory; NOTES.md explicitly names "full-matter diligence (multi-day), NDA negotiation chain (multi-week, term-acceptance gates), regulatory monitoring (continuous, publish-to-firm gates)."
- **Evidence 2**: notes/03 §F11 — "Workflow `RequestPort` + checkpoints is exactly the durable-pause mechanism for multi-day legal review" + "this IS the surface S5's canonical (non-shipped) HITL story uses."
- **Evidence 3**: notes/02 §5 observation 2 — Agent Framework Workflows HITL primitives shrink S5's Foundry-exclusivity. Foundry retains hosting + identity differentiation but does NOT retain HITL exclusivity.

**Agent Framework features S5B would use**: F1, F2, F6 (hosted MCP), F7 (Workflows — central), F8, F9 (A2A), F11 (HITL), F12 (durable / Foundry hosting).

#### S5B.6 Deployment model (SPEC §4d)

**Mixed depending on workflow**: 
- Workflows-in-BFF for short-running HITL approvals (under ADR-013 default).
- Workflows-in-Function or Hosted Foundry Agent for multi-day workflows that need state survival across process restarts beyond Redis TTL. Hosted Foundry Agent specifically if VM isolation + per-agent Entra identity + A2A endpoint exposure required.
- ADR-013 §"Exceptions" criteria (no latency coupling, no transactional coupling, bounded surface, no duplication) all hold for the multi-day case.

#### S5B.7 Open questions

1. **VM isolation requirement**: Do Spaarke legal workflows actually require per-session VM-isolated sandboxes (`$HOME` + `/files` persistence, 15-min idle → resume) per `knowledge/foundry-agent-service/docs/hosted-agents.md`? If yes, Foundry-hosted is the deployment; if no, framework-only HITL covers the requirement and Foundry can be deferred. Material to the choice of deployment.
2. **Per-agent Entra identity**: A2A composition between Foundry-hosted agents (e.g., "negotiation agent" + "diligence agent") benefits from per-agent identity for cross-system auth. Is this on the roadmap, or is single-shared-system-identity sufficient?
3. **Foundry SKU costs**: Foundry-hosted agents have a per-session cost (preview SKU, pricing UNKNOWN per notes/02 §2.3 and §6 U5). Is the per-session cost acceptable for Spaarke's expected concurrency profile? Owner-level decision.
4. **Project SPEC trigger**: When should the canonical durable legal workflows surface get a project SPEC, given the assessment recommends adoption? This is a roadmap question outside the assessment scope.

---

## §S6. M365 Copilot / Declarative Agent surface

### S6.1 Technical fit evaluation (SPEC §4a)

| Criterion | Evaluation |
|---|---|
| **Conversational vs. deterministic** | Conversational on the Copilot side; the BFF gateway side is request/response adapter ([notes/02 §3.1(d)+(e)](02-non-bff-ai-touchpoints-inventory.md#31-s6--six-required-fields)). |
| **Latency budget** | API plugin response timeout assumed <30s (exact UNKNOWN per notes/02 §6 U6); not <500ms-TTFB-coupled to BFF state since user perceives latency between Copilot and themselves. |
| **State + transactional coupling** | Low — gateway endpoints are adapters; session state stays in BFF. |
| **Durability** | Not required for R1 (long-running playbooks deflect to async / deep-link per notes/02 §3.1(f)). |
| **Tool composition** | API Plugin functions defined via OpenAPI spec — declarative, not LLM-driven routing on BFF side. |
| **External consumer** | Yes — M365 Copilot is the external runtime. |
| **Streaming** | No — API plugin is request/response. |

### S6.2 Agent Framework value (notes/03 features)

| Feature | Would S6 use it? | Notes |
|---|---|---|
| **F1 `AIAgent`** | No for the R1 scope. The S6 design ([notes/02 §3.1(c)](02-non-bff-ai-touchpoints-inventory.md#31-s6--six-required-fields)) explicitly chose "thin adapter facades over existing BFF services — no new AI orchestration logic." Adding `AIAgent` plumbing would contradict the adapter principle. |
| **F2-F5** | Same — no framework abstractions in the R1 scope. |
| **F6 Tools (hosted MCP)** | Future-relevant if the design adds Foundry-hosted agents BEHIND the API plugin for orchestrating tool calls. Not R1 scope. |
| **F7 Workflows** | No. |
| **F8 MCP** | Future-relevant; R2 (Tier 3 MCP server is deferred per design.md §244-275). |
| **F9 A2A** | "Copilot's plugin ecosystem may use A2A as a forward-compatible alternative to MCP; relevant to monitor but not actionable today" (notes/03 §F9 Spaarke applicability). |
| **F10-F12** | No. |

### S6.3 Migration cost

| Criterion | Evaluation |
|---|---|
| **Rewrite scope** | If asked to swap M365 Agents SDK for Agent Framework agents: would require rebuilding `SpaarkeAgentHandler.cs` and ~14 .cs files under `Api/Agent/` ([notes/02 §3.1(a)](02-non-bff-ai-touchpoints-inventory.md#31-s6--six-required-fields)), plus re-validating the entire Bot Framework integration with Azure Bot Service + M365 Copilot. |
| **Package + publish-size impact** | Adds redundant agent stack alongside existing `Microsoft.Agents.Builder` / `Microsoft.Agents.Builder.Compat` / `Microsoft.Agents.Core.Models` (notes/02 §3.1(c)). |
| **Test impact** | Substantial — replaces the test surface for an in-flight project. |
| **Team learning curve** | Adds a second agent SDK to an already-learning-new-territory project. |
| **Observability impact** | Disruptive mid-flight. |
| **Reversibility** | Poor — once Copilot is shipping via a particular agent SDK, swapping mid-stream affects M365-side manifests. |

### S6.4 Recommendation

**DON'T ADOPT (as a swap-in for the M365 Agents SDK at the Copilot integration layer).**

S6 uses **`Microsoft.Agents.Builder` / `Microsoft.Agents.Builder.Compat` / `Microsoft.Agents.Core.Models`** — the **M365 Agents SDK** (formerly Bot Framework) — for the agent-channel side ([notes/02 §3.1(c)](02-non-bff-ai-touchpoints-inventory.md#31-s6--six-required-fields)). This is a DIFFERENT SDK from `Microsoft.Agents.AI` (the Agent Framework). They are not interchangeable; the M365 Agents SDK is the canonical SDK for building agents that consume from M365 Copilot and Azure Bot Service channels.

The S6 design ([notes/02 §3.1(d)+(e)](02-non-bff-ai-touchpoints-inventory.md#31-s6--six-required-fields)) explicitly chose Path A (direct API Plugin + manifests, BFF as adapter) over Copilot Studio. The agent gateway endpoints are "THIN ADAPTERS — MUST reuse existing BFF services per spec.md MUST NOT create new AI orchestration logic." Adding `Microsoft.Agents.AI.AIAgent` plumbing as the integration backend would:

1. Duplicate the agent abstraction (M365 Agents SDK already covers it for the Copilot channel).
2. Contradict the adapter principle (Agent Framework agents = orchestration logic).
3. Add a third agent SDK to the project on top of the existing two.

The correct nuance: this is NOT a vote against Agent Framework. It's a vote that Agent Framework should not be the Copilot integration backend. If, separately, BFF AI surfaces (S1, S3, S5B) lift to `AIAgent`, the M365 Agents SDK can invoke them via existing facade types — no `Microsoft.Agents.AI` adoption at the Copilot integration boundary is needed for that to work.

### S6.5 Rationale (≥2 concrete evidence pieces)

- **Evidence 1**: notes/02 §3.1(c) — "**NOT planned for R1**: `Microsoft.Agents.AI.*` (the Agent Framework). The README and spec.md describe the gateway as 'thin adapter facades over existing BFF services — no new AI orchestration logic.' Agent Framework hosting of this surface is not in the R1 scope."
- **Evidence 2**: notes/02 §3.1(c) — `Microsoft.Agents.Builder` + `Microsoft.Agents.Builder.Compat` + `Microsoft.Agents.Core.Models` (the M365 Agents SDK) is already wired at `SpaarkeAgentHandler.cs:2-4`. Two SDKs solve the same problem; swapping mid-flight has zero functional benefit.
- **Evidence 3**: notes/02 §5 observation 1 — S6 is "fundamentally an external-consumer adapter surface" with "bounded, well-defined integration surface" as the design driver. Agent Framework value-add (F1-F12) is misaligned with adapter-surface needs.

**Specific reasons DON'T ADOPT** (per task instruction):
1. Surface already uses a different SDK (M365 Agents SDK at the Copilot channel boundary).
2. Migration would force breaking changes to a working production-bound integration path with no proportionate gain.
3. Project SPEC explicitly excludes Agent Framework from R1 scope.
4. The framework's value-add (agent loops, sessions, HITL, workflows) is in BFF AI surfaces (S1/S3/S5B), not in the Copilot adapter boundary.

### S6.6 Open questions for human decision

1. **R2 MCP server**: When the deferred R2 MCP server (Tier 3) is implemented, does it host Agent Framework agents internally (the question becomes "does S7 host Agent Framework agents" — see S7 below)? S6's decision flips from "Agent Framework not the Copilot backend" to "Agent Framework may live in the MCP server consumed BY Copilot." 
2. **Future A2A interop**: If Copilot's plugin ecosystem standardizes A2A as an alternative to MCP, does Spaarke want to expose SprkChat / Insights Agent over A2A as part of the M365 integration? This is monitor-territory, not actionable today (notes/03 §F9 Spaarke applicability).

---

## §S7. Insights Engine MCP server

### S7.1 Technical fit evaluation (SPEC §4a)

| Criterion | Evaluation |
|---|---|
| **Conversational vs. deterministic** | The Insights Agent (in-BFF, D-A9) is conversational. The MCP server (deferred to Phase 2) exposes capability over an MCP transport — deterministic from the consumer's perspective (tool calls return results). |
| **Latency budget** | Insights Agent: subject to BFF latency budgets. MCP server: depends on whether consumer expects sync response (yes, per the canonical Foundry MCP sample at `knowledge/foundry-agent-service/samples/mcp-tool-binding/` per notes/02 §4.1(f)). |
| **State + transactional coupling** | Insights Agent in BFF — yes (per design.md §5.1 "custom BFF agent, not Foundry-hosted"). MCP server — bounded by tool boundary. |
| **Durability** | LOW for query path; not relevant for MCP server itself (per notes/02 §4.1(f)). |
| **Tool composition** | Insights Agent uses tools per D-A24 (`IDeclineToFindTool` etc.). MCP server exposes tools to external consumers. |
| **External consumer** | Yes for MCP server. No for in-BFF Insights Agent. |
| **Streaming** | Insights Agent: yes (matches SprkChat pattern). MCP server: tool calls are typically request/response. |

### S7.2 Agent Framework value (notes/03 features)

**For the in-BFF Insights Agent (D-A9, scope-of-this-assessment-irrelevant)**: Same fit profile as S1 — F1, F2, F4, F5, F6, F10, F11. Decision tracks S1.

**For the MCP server (Phase 2 deferred)**:

| Feature | Would S7 use it? | Notes |
|---|---|---|
| **F1 `AIAgent`** | Depends on what the MCP server hosts. If MCP server is a thin transport over `IInsightsAi` facade calls (read pattern), `AIAgent` is overkill. If MCP server itself hosts agents that orchestrate between tools, `AIAgent` is the right primitive. |
| **F6 Tools** | Yes — MCP server BY DEFINITION exposes tools; the question is whether they're declared via `MCPToolDefinition` or hand-rolled. |
| **F8 MCP client** | N/A — S7 is the server, not the client. (Framework MCP client features help OTHER agents consume S7; don't help build S7.) |
| **F9 A2A** | Possible add-on — `MapA2A(...)` could expose the Insights Agent over A2A alongside MCP (notes/03 §F9 Spaarke applicability: "the Insights Engine could expose both surfaces simultaneously"). |
| **F11 Tool Approval** | Yes potentially — destructive Insights write-back paths (Phase 2+ per SPEC §3.6) could require tool-level approval. |
| **F12 Hosting helpers** | If MCP server is a separate `Sprk.Insights.Mcp` deployable, `AddAIAgent` + `MapA2A` are the framework integration. |

### S7.3 Migration cost

N/A — Phase 2 deferred; no Spaarke code yet. Greenfield design cost. The framework decision is "do we author the MCP server using Agent Framework agents internally or as a plain MCP transport over `IInsightsAi`?"

### S7.4 Recommendation

**PARTIAL — assess at MCP server contract authoring time (D-A20), not now.**

S7 spans two parts and only one is in scope for "should we adopt Agent Framework":

1. **In-BFF Insights Agent (D-A9)** — tracks S1 (same fit profile). If S1 adopts post-#6268, the in-BFF Insights Agent should adopt alongside it; if S1 doesn't, Insights Agent stays on Extensions.AI alone. **Decision: follow S1.**

2. **MCP server (D-A20 contract document; implementation Phase-2-deferred)** — the assessment cannot give a definitive verdict because **the contract doesn't exist yet** ([notes/02 §6 U1/U2/U3](02-non-bff-ai-touchpoints-inventory.md#6-unknowns-no-invention) explicitly flag this as UNKNOWN). Three concrete UNKNOWNs material to the decision:
   - U1: specific `Microsoft.*` host library (e.g., `ModelContextProtocol.AspNetCore` vs Agent Framework MCP host) — not committed
   - U2: deployment model (separate `Sprk.Insights.Mcp` vs embedded in BFF) — not committed
   - U3: BFF integration seam (wraps `/api/insights/ask` vs directly wraps `IInsightsAi`) — not committed

The assessment's substantive guidance for when the D-A20 contract is authored: **prefer Agent Framework primitives IF the MCP server is a separate deployable AND hosts agents internally (i.e., agents that orchestrate Insights tool calls or call back to BFF capabilities)**. If the MCP server is a thin transport over a single `IInsightsAi` facade call per tool, plain `ModelContextProtocol` library hosting is simpler and the framework adds nothing.

### S7.5 Rationale (≥2 concrete evidence pieces)

- **Evidence 1**: notes/02 §4.1(a)+(d) — "MCP IMPLEMENTATION DEFERRED TO PHASE 2" with the contract document (D-A20) as a Phase 1 deliverable. Pre-emptive Agent Framework adoption commitment is design-by-assumption, not design-by-contract.
- **Evidence 2**: notes/02 §6 U1/U2/U3 — three concrete UNKNOWNs about host library, deployment model, and BFF seam. None are answerable without the contract document existing.
- **Evidence 3**: notes/03 §F8 Spaarke applicability — "this surface IS an MCP server. The framework helps OTHER agents consume it; doesn't change S7's server-side construction directly." Confirms framework value is asymmetric for S7.

**Agent Framework features S7 MIGHT use** (conditional on contract): F1, F6, F9, F11, F12.

### S7.6 Deployment model (SPEC §4d)

UNKNOWN per notes/02 §6 U2. Decision deferred to D-A20 authoring. Candidates: separate `Sprk.Insights.Mcp` deployable per ADR-013's "MCP server exposing AI capabilities to external consumers" example, OR MCP endpoints embedded in BFF.

### S7.7 Open questions for human decision

1. **Phase 1 D-A20 contract**: When the contract document is authored as part of Phase 1 W1.5, the contract MUST explicitly answer (a) which host library, (b) which deployment model, (c) which BFF seam. The assessment cannot pre-commit answers; only flag that the decision is binding. Owner-level action: ensure the D-A20 contract addresses these.
2. **Agent Framework as Insights Agent backend**: Independent of MCP, should the in-BFF Insights Agent (D-A9) be authored on Agent Framework primitives from day one (Phase 1, parallel to Insights Engine Track A implementation), or remain on Extensions.AI? Decision tracks S1.
3. **MCP server as separate deployable**: Per ADR-013, separate deployable requires all four exception criteria (no latency coupling, no transactional coupling, bounded surface, no duplication). Does S7 MCP server materially meet all four? Bounded surface YES; the other three need verification at contract time.

---

## §S8a. SessionSummarizationService (Sessions/ directory)

### S8a.1 Technical fit evaluation (SPEC §4a)

| Criterion | Evaluation |
|---|---|
| **Conversational vs. deterministic** | Deterministic single-call structured-output extraction ([notes/01 §S8a](01-spaarke-ai-surfaces-inventory.md#s8a-sessionsummarizationservice-sessions)). |
| **Latency budget** | Fire-and-forget from chat session lifecycle (per `SessionSummarizationService.cs:24` scoped + lifecycle note). No SSE blocking. |
| **State + transactional coupling** | Low — feeds session summary to Redis / Dataverse downstream of an HTTP call. |
| **Durability** | Not required. |
| **Tool composition** | None (single LLM call with prompt + JSON parsing). |
| **External consumer** | No. |
| **Streaming** | No. |

### S8a.2 Agent Framework value

| Feature | Would S8a use it? | Notes |
|---|---|---|
| **F1 `AIAgent`** | No — `SessionSummarizationService` is a single-purpose `IChatClient` consumer. Wrapping it in `ChatClientAgent` adds the agent abstraction with no agent loop. |
| **F2-F4** | No. |
| **F5 Structured outputs** | Marginal — `RunAsync<SessionSummary>` would replace ad-hoc JSON parsing of the JSON-embedded-in-narrative response (notes/01 §S8a "Structured-output extraction: JSON block embedded in narrative response"). But the existing prompt is tuned for legal-context preservation; switching response_format to strict JSON might compromise the qualitative output that justifies using GPT-4o vs GPT-4o-mini. |
| **F6-F12** | No. |

### S8a.3 Migration cost

Small in scope. ~1 file (`SessionSummarizationService.cs`). Risk: the qualitative response shape might change if structured-output mode is enabled, regressing the legal-context preservation that justifies the model upgrade.

### S8a.4 Recommendation

**DON'T ADOPT.**

S8a is the textbook anti-fit for Agent Framework: a single-purpose `IChatClient` consumer with no agent loop, no tool calling, no session, no streaming. Wrapping it in `ChatClientAgent` adds the agent abstraction without using any of the framework's value-add features.

The structured-output candidacy (F5) is marginal — replacing ad-hoc JSON parsing with `RunAsync<SessionSummary>` is a code-quality improvement, not a structural fit improvement. It might also regress the qualitative output (the "legal context preservation" rationale at `SessionSummarizationService.cs:12-18`) if structured-output mode constrains the model's narrative quality.

### S8a.5 Rationale

- **Evidence 1**: notes/01 §S8a — `SessionSummarizationService` uses `IChatClient` directly + GPT-4o with explicit rationale ("legal context preservation"). The single LLM call is not agent-shaped.
- **Evidence 2**: notes/01 §S8a — "Scoped lifetime — fire-and-forget from chat session lifecycle to avoid blocking SSE." This is a service consumer pattern, not an agent pattern.

**Specific reasons DON'T ADOPT**: 
1. Single-call structured-output extraction — no agent loop, no tools, no session.
2. Migration introduces qualitative regression risk (structured-output mode vs narrative response shape).
3. `IChatClient` already provides everything needed; `AIAgent` adds the agent abstraction with no agent-loop content.

### S8a.6 Open questions

1. **Structured output marginal lift**: If the team wants to consolidate JSON parsing patterns across SprkChat surfaces, is the marginal F5 benefit worth ~30 minutes of work + qualitative regression testing? Owner judgment call; not assessment-grade.

---

## §S8b. CapabilityRouter (Capabilities/ directory)

### S8b.1 Technical fit evaluation (SPEC §4a)

| Criterion | Evaluation |
|---|---|
| **Conversational vs. deterministic** | Deterministic — multi-layer classification with optional LLM-based Layer 2 ([notes/01 §S8b](01-spaarke-ai-surfaces-inventory.md#s8b-capabilityrouter-capabilities)). |
| **Latency budget** | Tight — classification runs per turn in the SprkChat pipeline; <50ms target per ADR-013 §"Decision" routing budget. |
| **State + transactional coupling** | Sub-component of SprkChat; shares per-turn context. |
| **Durability** | Not required. |
| **Tool composition** | None — classifier emits decisions, doesn't invoke tools. |
| **External consumer** | No. |
| **Streaming** | No. |
| **Optional dependency pattern** | `[FromKeyedServices("raw")] IChatClient?` — the constructor accepts nullable; Layer 2 disabled if not injected (notes/01 §S8b "The 3-param constructor overload (without `IChatClient`) disables Layer 2 entirely"). |

### S8b.2 Agent Framework value

| Feature | Would S8b use it? | Notes |
|---|---|---|
| **F1 `AIAgent`** | No — classifier is not an agent. |
| **F5 Structured outputs** | Marginal — classification responses could be schema-bound. `RunAsync<CapabilityClassification>` would replace ad-hoc parsing (similar shape to S1's `CompoundIntentDetector`). |
| **F11 Tool Approval** | No. |
| Others | No. |

### S8b.3 Migration cost

Small in scope. Lift would unify the S8b classifier prompt-builder pattern with the in-flight S1 `CompoundIntentDetector` (notes/01 §S1: `CompoundIntentDetector` is conceptually adjacent and also does JSON parsing of LLM classification). If both adopt `RunAsync<T>`, the pattern unifies.

### S8b.4 Recommendation

**PARTIAL — adopt structured-output (F5) only, alongside S1 lift.**

S8b is a sub-component of S1 (called from `SprkChatAgentFactory.CreateAgentAsync` per [notes/01 §S8b](01-spaarke-ai-surfaces-inventory.md#s8b-capabilityrouter-capabilities)). Treating it as independent is artificial — it should track S1's verdict.

For the structured-output aspect specifically: when S1 adopts `RunAsync<T>` for `CompoundIntentDetector`, S8b's classifier should adopt the same pattern for consistency. This is "PARTIAL" rather than "ADOPT" because (a) the broader Agent Framework features (F1, F2, F4, etc.) don't apply to a router, and (b) the change is contingent on S1 lift timing.

### S8b.5 Rationale

- **Evidence 1**: notes/01 §S8b — `CapabilityRouter` consumes a KEYED `[FromKeyedServices("raw")] IChatClient?` — the same `"raw"` keyed singleton SprkChat's `CompoundIntentDetector` uses (notes/01 §S1 cross-cutting observation 4). The two classifier patterns are coupled by construction.
- **Evidence 2**: notes/01 §S8b "Should be folded into S1's perimeter for the task 004 decision matrix" — task 001 author flagged this surface as effectively part of S1.

**Specific Agent Framework features S8b would use**: F5 only (alongside S1).

### S8b.6 Open questions

1. **S1 lift coupling**: If S1 adopts `RunAsync<CompoundIntent>`, does S8b adopt `RunAsync<CapabilityClassification>` in the same PR set, or sequentially? Owner-level decision since S1 lift is itself gated on Issue #6268.

---

## §R. Recommendation summary table

| Surface | Recommendation | Top 2 reasons | Top open question |
|---|---|---|---|
| **S1 SprkChat** | **PARTIAL** (gated on Issue #6268) | (1) Structural fit is the highest of any surface — middleware decorates a Spaarke interface that mirrors the framework idiom (notes/01 obs 3). (2) Issue #6268 ("ChatClientAgent.RunStreamingAsync ends with no assistant text on multi-tool turns") affects S1's canonical workload (multi-tool streaming). | Wait for #6268 to land in a shipped 1.x, or pilot with a feature flag + fallback? Depends on % of multi-tool traffic. |
| **S2 AnalysisOrchestration + JPS** | **DON'T ADOPT** | (1) JPS is deterministic/playbook-driven; no agent loop to lift. (2) Wholesale Workflows migration touches 30+ files + Dataverse schema + plugins — catastrophic rewrite scope vs working production. | Should JPS remain the long-term home, or is it a transitional pattern? Architecture-group decision. |
| **S3 Builder agent** | **PARTIAL** (adopt at next maintenance window) | (1) Hand-written agentic loop bounded by `MaxToolRounds = 10` + manual `OpenAI.Chat` SDK usage — exactly what `ChatClientAgent.RunAsync` collapses. (2) Lowest-risk lift (~3-5 files, non-streaming, no end-user latency budget). | Adopt S3 first to validate the framework in a low-risk surface, or wait for S1 lift? |
| **S4 Background AI jobs** | **DON'T ADOPT** | (1) Handlers are single-purpose `ProcessAsync` methods — not agent-shaped. (2) `AIAgent` provides nothing Service Bus + IIdempotencyService + a direct LLM call doesn't already provide; replacing the Job Contract with Workflows is regression. | Should `EmbeddingMigrationService` be rerouted from `IOpenAiClient` to `IChatClient` for Extensions.AI consolidation? (Independent of Agents.AI.) |
| **S5A Foundry wrapper (shipped)** | **PARTIAL** (adopt only with S1) | (1) `AsAIAgent` + `CreateSessionAsync(conversationId)` simplify the wrapper. (2) Default-OFF (ADR-018) — low operational pressure to lift standalone. | Standalone S5A lift not productive; bundle with S1 lift. |
| **S5B Foundry canonical durable HITL (planned)** | **ADOPT** (when project SPEC lands) | (1) F7 Workflows + F11 `RequestPort` HITL + F12 Foundry hosting are purpose-built for multi-day legal workflows. (2) Greenfield, no migration cost. | Do Spaarke legal workflows require VM isolation + per-agent Entra identity + A2A exposure? Determines Foundry-hosted vs Workflows-in-BFF/Function. |
| **S6 M365 Copilot** | **DON'T ADOPT** (as swap-in) | (1) Surface uses M365 Agents SDK (`Microsoft.Agents.Builder`) — different SDK from Agent Framework, both Microsoft, both valid for their respective surfaces. (2) Spec explicitly excludes Agent Framework: "thin adapter facades — no new AI orchestration logic." | When the deferred R2 MCP server (Tier 3) is implemented, does it host Agent Framework agents internally? (Question flips to S7.) |
| **S7 Insights Engine MCP** | **PARTIAL** (defer to D-A20 contract authoring) | (1) Contract doesn't exist yet (Phase-2-deferred per SPEC §3.6); three concrete UNKNOWNs (notes/02 §6 U1/U2/U3) on host library, deployment model, BFF seam. (2) In-BFF Insights Agent (D-A9) tracks S1's verdict; MCP server itself is separate decision. | Phase 1 D-A20 contract MUST address: (a) host library, (b) deployment model, (c) BFF seam. |
| **S8a SessionSummarizationService** | **DON'T ADOPT** | (1) Single-purpose `IChatClient` consumer; no agent loop, tools, session. (2) F5 marginal lift has qualitative regression risk (structured-output vs narrative legal-context). | Marginal F5 benefit worth qualitative regression testing? |
| **S8b CapabilityRouter** | **PARTIAL** (F5 only, with S1) | (1) Sub-component of S1; shares the `"raw"` keyed `IChatClient` registration (notes/01 obs 4). (2) Only F5 (structured outputs) materially applies — classifier is not agent-shaped. | If S1 adopts `RunAsync<T>`, does S8b adopt same-PR or sequentially? |

---

## §A. Acceptance criteria verification

Per the task POML §acceptance-criteria:

- [x] Every surface S1-S7 has its own section + ADOPT/DON'T ADOPT/PARTIAL recommendation. S8a + S8b also covered as task 001 discoveries.
- [x] Every recommendation cites at least 2 concrete pieces of evidence from notes/01-03. Verified per-surface in subsection .5 of each.
- [x] Every surface has at least 1 open-question-for-human-decision item. Verified per-surface in subsection .7 (or final subsection where numbered differently).
- [x] Closing summary table lists all surfaces in one place. §R above.
- [x] **At least one DON'T ADOPT or PARTIAL recommendation surfaces** — **the matrix contains 3 DON'T ADOPT (S2, S4, S6, S8a — actually four DON'T ADOPT) and 5 PARTIAL (S1, S3, S5A, S7, S8b) and 1 ADOPT (S5B)**. Anti-bias sanity check passes overwhelmingly.

Distribution: **4 DON'T ADOPT · 5 PARTIAL · 1 ADOPT** out of 10 surface evaluations.

---

## §S. Sign-off

This matrix satisfies the task 004 acceptance criteria. Downstream consumers:

- **Task 005** (deployment + migration) reads §S1.6, §S3.6, §S5A.6, §S5B.6, §S7.6 for surface-by-surface deployment-model recommendations.
- **Task 006** (synthesis) consumes the entire matrix as §5 of the assessment document and all "Open questions for human decision" subsections as §8.
- **Task 007** (adversarial review) re-reads each surface's recommendation and applies the "argue against" challenge. Watch for: S1 PARTIAL (is the #6268 gate over-cautious?), S2 DON'T ADOPT (is Workflows really not worth the migration cost?), S5B ADOPT (is the assessment too optimistic about greenfield cost?), S6 DON'T ADOPT (does the M365 Agents SDK vs Agent Framework distinction hold under scrutiny?).
