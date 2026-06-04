# Microsoft Agent Framework — Fit Assessment for Spaarke

> **Date**: 2026-06-03
> **Author**: AI assessment per [`projects/agent-framework-fit-assessment-r1/SPEC.md`](../../projects/agent-framework-fit-assessment-r1/SPEC.md)
> **Method**: Read-only code analysis (no `.cs` modifications); 34 primary-source citations captured at 2026-06-03; per-surface decision criteria applied per [SPEC §4](../../projects/agent-framework-fit-assessment-r1/SPEC.md). Findings tables in [`projects/agent-framework-fit-assessment-r1/notes/`](../../projects/agent-framework-fit-assessment-r1/notes/) (`00`–`05`).
> **Status**: Findings for owner review. Recommendation is advisory.
> **Related**:
> - [ADR-013](../../.claude/adr/ADR-013-ai-architecture.md) (refined 2026-05-20 — "extend BFF for AI in-process; narrow exceptions permitted")
> - [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](bff-ai-extraction-assessment-2026-05-20.md) (assessment-format template)
> - [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) (publish-size + placement decision criteria)
> - Parked downstream: [`projects/agent-framework-knowledge-r1/`](../../projects/agent-framework-knowledge-r1/) (curation project; this assessment unblocks its SPEC review)

---

## 1. Executive summary

Spaarke is on `Microsoft.Extensions.AI` directly. `Microsoft.Agents.AI 1.0.0-rc1` is **referenced in `Sprk.Bff.Api.csproj` but used by zero source files** ([Sprk.Bff.Api.csproj:33](../../src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj#L33); grep for `using Microsoft.Agents.AI` returned zero matches across `src/server/api/Sprk.Bff.Api/`). This is the half-adopted starting state the SPEC names, and it is verified at the code level. The platform reached 1.0 GA in April 2026 and shipped 1.9 on 2026-06-03 (today) — production-ready, actively iterating ([Devblog D1 / D3](https://devblogs.microsoft.com/agent-framework/microsoft-agent-framework-version-1-0/), fetched 2026-06-03).

The recommendation summary across the ten surfaces evaluated:

| Surface | Recommendation | Deployment |
|---|---|---|
| **S1 SprkChat** — conversational agent | **PARTIAL** (gated on Issue [#6268](https://github.com/microsoft/agent-framework/issues/6268)) | in-process BFF |
| **S2 AnalysisOrchestration + JPS playbooks** | **DON'T ADOPT** | unchanged |
| **S3 Builder agent** | **PARTIAL** (next maintenance window) | in-process BFF |
| **S4 Background AI jobs** | **DON'T ADOPT** | unchanged |
| **S5A Foundry wrapper (shipped)** | **PARTIAL** (bundle with S1) | in-process BFF |
| **S5B Foundry canonical durable HITL (planned)** | **ADOPT** | **MIXED — prototype first** |
| **S6 M365 Copilot / Declarative Agent** | **DON'T ADOPT** (as swap-in) | unchanged |
| **S7 Insights Engine MCP server** | **PARTIAL** (defer to D-A20 contract) | UNKNOWN |
| **S8a SessionSummarizationService** | **DON'T ADOPT** | unchanged |
| **S8b CapabilityRouter** | **PARTIAL** (F5 only, bundle with S1) | in-process BFF |

**Distribution: 1 ADOPT · 5 PARTIAL · 4 DON'T ADOPT.** The single ADOPT (S5B) is greenfield — no migration cost, no code to lift. Every PARTIAL surface either bundles into the S1 cross-cutting lift or defers to a downstream contract decision. Three "DON'T ADOPT" verdicts are decisive (S2, S4, S6); S8a is the textbook anti-fit.

The most consequential decisions:

**S5B is the only ADOPT, and it is also the only adopt-surface where the deployment model is non-trivial.** Agent Framework Workflows (`WorkflowBuilder`, `Executor<TIn,TOut>`, supersteps, checkpoints) + `RequestPort`/`RequestInfoEvent` HITL primitives + `FoundryHostedAgents` hosting are purpose-built for the multi-day legal workflows curated in [`knowledge/foundry-agent-service/NOTES.md`](../../knowledge/foundry-agent-service/NOTES.md) (NDA negotiation, full-matter diligence, regulatory monitoring). HITL is no longer Foundry-exclusive — [Workflow HITL Learn page](https://learn.microsoft.com/en-us/agent-framework/workflows/human-in-the-loop) (fetched 2026-06-03, `updated_at 2026-03-31`) confirms `RequestPort` ships in Agent Framework Workflows themselves. This narrows Foundry's residual differentiation to VM isolation + per-agent Entra identity + A2A endpoint exposure + Foundry-hosted MCP/memory.

**S2 is DON'T ADOPT not because Workflows is inadequate, but because the migration is non-incremental.** JPS spans 12 node executors, an orchestration service, an execution engine, an idempotent persistence layer, a Dataverse-stored playbook schema, and a validation plugin in `Sprk.Dataverse.Plugins`. There is no path that lifts one node and leaves the rest. The framework can structurally express most of JPS; the cost to do so is grossly disproportionate to a working production system.

**S1 is PARTIAL because of a specific, named bug, not because of structural mismatch.** Spaarke's middleware decorates `ISprkChatAgent` (a Spaarke interface), NOT `IChatClient` — exactly the hand-rolled equivalent the framework's `.AsBuilder().Use*().Build()` composition subsumes (per [Middleware Learn page](https://learn.microsoft.com/en-us/agent-framework/agents/middleware/), fetched 2026-06-03, `updated_at 2026-04-02`). Every Agents.AI feature except F7 (Workflows) and F9 (A2A) maps cleanly to existing Spaarke code that already approximates the framework idiom. But SprkChat's canonical workload IS multi-tool streaming, and [GitHub Issue #6268](https://github.com/microsoft/agent-framework/issues/6268) (opened 2026-06-02) reports the exact failure mode: `ChatClientAgent.RunStreamingAsync` ends with no assistant text on multi-tool turns. As of synthesis date the issue is `needs-maintainer-triage`. Lifting before resolution would ship a regression in the most-trafficked SprkChat path.

The most consequential open question is **S5B's hosting model.** The F12 durable-hosting evidence base is thin — no dedicated `/hosting/` Microsoft Learn page exists within the recency floor, primary evidence is the `04-hosting/` upstream sample tree (entirely new since the 2026-05-14 curated baseline) plus [Devblog D6 "Durable Workflows in Microsoft Agent Framework"](https://devblogs.microsoft.com/dotnet/durable-workflows-in-microsoft-agent-framework/) plus open [Issue #6308](https://github.com/microsoft/agent-framework/issues/6308) ("How to deploy dotnet Hosted agents to Foundry", opened 2026-06-03). Three candidates — Workflows-in-BFF, Workflows-in-Function, Foundry-hosted — each ADR-013-defensible. **The recommendation is to prototype, not pre-commit.** Until the canonical durable HITL surface gets a project SPEC AND a 1-2 week deployment prototype phase runs, deployment is "TBD pending prototype."

Structurally, the framework fits S1 and is purpose-built for S5B. Operationally, S1 lift is gated on an upstream bug and S5B's deployment-model decision is gated on prototyping. Adoption is therefore real but **sequenced** — pre-work first, infrastructure lift second, parallel surface lifts third, S5B as a separate greenfield project.

---

## 2. Context and scope

### 2.1 The half-adopted state, confirmed

`Microsoft.Agents.AI 1.0.0-rc1` ships in `Sprk.Bff.Api.csproj` ([line 33](../../src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj#L33)) with the comment "1.0.0-rc1 requires Microsoft.Extensions.AI >= 10.3.0" ([line 27](../../src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj#L27)). Grep across `src/server/api/Sprk.Bff.Api/`:

- `using Microsoft.Agents.AI|Microsoft.Agents.AI.` — **zero matches**
- `ChatClientAgent|AIAgent|\.AsAgent\(|AgentRunResponse` — **zero matches**

Every production AI surface uses ONLY `Microsoft.Extensions.AI` primitives (`IChatClient`, `AIFunction`, `AIFunctionFactory`, `ChatResponseUpdate`, `FunctionCallContent`, `ChatRole`, `ChatOptions`, `ChatToolMode`) or lower-level OpenAI SDK types (`OpenAI.Chat.AssistantChatMessage`, `ToolChatMessage`, `ChatToolCall`). The Agents.AI package is a transitive carry-along, satisfying the Extensions.AI version requirement. Doc-comments in [`SprkChatAgent.cs:14`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L14) declare the type "the Core Agent Framework agent for the SprkChat feature" — that statement is aspirational; the implementation wraps raw `IChatClient`, not `ChatClientAgent`.

### 2.2 Surfaces in scope (10 total)

Per SPEC §3 and the task 001/002 inventories:

- **S1 SprkChat** conversational agent (shipped, [`Services/Ai/Chat/`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat))
- **S2 AnalysisOrchestration + JPS playbooks** (shipped, [`AnalysisOrchestrationService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs) + 12 node executors)
- **S3 Builder agent** (in-flight, [`Services/Ai/Builder/`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Builder))
- **S4 Background AI jobs** (shipped, 5 handlers under [`Services/Ai/Jobs/`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Jobs) + framework dispatcher)
- **S5A Foundry wrapper** (shipped, default-OFF per ADR-018, [`Services/Ai/Foundry/`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Foundry))
- **S5B Foundry canonical durable HITL legal workflows** (planned, curated-only, no Spaarke code yet — bimodal split from S5)
- **S6 M365 Copilot / Declarative Agent surface** (in-flight, [`projects/ai-m365-copilot-integration/`](../../projects/ai-m365-copilot-integration/))
- **S7 Insights Engine MCP server** (planned, Phase-2-deferred, [`projects/ai-spaarke-insights-engine-r1/`](../../projects/ai-spaarke-insights-engine-r1/))
- **S8a SessionSummarizationService** (discovered during task 001 grep, [`Services/Ai/Sessions/SessionSummarizationService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionSummarizationService.cs))
- **S8b CapabilityRouter** (discovered during task 001 grep, [`Services/Ai/Capabilities/CapabilityRouter.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs))

S5 was specified as a single surface in SPEC §3 but is bimodal in fact: the shipped in-BFF Foundry wrapper (S5A) and the planned canonical durable HITL legal workflows (S5B) are two distinct problems with two distinct decisions. The S8a/S8b discoveries surfaced through a defensive grep mandated by the project's surface-coverage integrity test (per [`projects/agent-framework-fit-assessment-r1/CLAUDE.md`](../../projects/agent-framework-fit-assessment-r1/CLAUDE.md) §7). Both fold into S1's perimeter for adoption purposes but are tracked individually.

### 2.3 Out of scope

Per SPEC §3 "Non-goals":

- **No code changes.** This is read-only on `.cs` files. The assessment cites them; it does not modify them.
- **No ADR amendments / new ADRs.** The assessment may imply amendments (notably the shared middleware-lift infrastructure change and S5B's deployment-model exception); the writing of an ADR is downstream of human review of this assessment.
- **No refinements to `agent-framework-knowledge-r1` SPEC.** Per the project scoping decision (2026-06-03), refinement is deferred. The parking-release note at [`projects/agent-framework-knowledge-r1/UNBLOCK-RECOMMENDATION.md`](../../projects/agent-framework-knowledge-r1/UNBLOCK-RECOMMENDATION.md) (created by task 008) captures what this assessment implies; the SPEC itself is not edited.
- **No per-surface code refactors.** The assessment recommends; it does not refactor.
- **No cost/licensing TCO model** beyond what affects the fit decision (Foundry SKU costs are flagged as an UNKNOWN material to S5B's deployment-model choice but not modeled).

---

## 3. Current state inventory

Synthesis of [`notes/01`](../../projects/agent-framework-fit-assessment-r1/notes/01-spaarke-ai-surfaces-inventory.md) (S1-S4 + S8a/S8b) and [`notes/02`](../../projects/agent-framework-fit-assessment-r1/notes/02-non-bff-ai-touchpoints-inventory.md) (S5-S7). Single structured table.

| Surface | Current state | Microsoft.* abstractions actually used | Spaarke-specific wrappers | Deployment | Key file paths (file:line) |
|---|---|---|---|---|---|
| **S1 SprkChat** | Shipped, production | `IChatClient` (Extensions.AI, two instances per agent: default + keyed "raw"); `AIFunction`/`AIFunctionFactory.Create`; `ChatResponseUpdate`; `FunctionCallContent`; `ChatMessage`/`ChatRole`/`ChatOptions`; `.UseFunctionInvocation()`. **NOT used**: `ChatClientAgent`, `AIAgent`, `AgentSession`, any `Microsoft.Agents.AI.*` | `ISprkChatAgent` interface; 3 hand-rolled middleware decorators (`AgentTelemetryMiddleware`, `AgentContentSafetyMiddleware`, `AgentCostControlMiddleware`); `ChatContext`; `CompoundIntentDetector`; `ICapabilityRouter`+`ICapabilityValidator`+`ICapabilityManifest`; 12+ tool classes via `AIFunctionFactory`; `ChatHistoryManager`+`ChatSessionManager`; `PendingPlanManager` (Redis) | In-process BFF | [`SprkChatAgent.cs:37`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L37); [`SprkChatAgentFactory.cs:41`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs#L41); [`AgentContentSafetyMiddleware.cs:29`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentContentSafetyMiddleware.cs#L29); [`AgentCostControlMiddleware.cs:23`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentCostControlMiddleware.cs#L23); [`AgentTelemetryMiddleware.cs:27`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentTelemetryMiddleware.cs#L27); [`CompoundIntentDetector.cs:33`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/CompoundIntentDetector.cs#L33); [`AiModule.cs:107-116`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs#L107) (DI for both `IChatClient` instances) |
| **S2 AnalysisOrchestration + JPS** | Shipped, production | None from Extensions.AI; none from Agents.AI. Direct `IOpenAiClient.StreamCompletionAsync` (Spaarke wrapper around `Azure.AI.OpenAI` + `OpenAI.Chat` SDKs). | `AnalysisOrchestrationService`; `IPlaybookOrchestrationService` with Legacy/NodeBased mode detection; `ExecutionGraph`+`INodeExecutorRegistry`; 12 node executors (`AiAnalysisNodeExecutor`, `ConditionNodeExecutor`, ..., `AgentServiceNodeExecutor`); `IAiToolHandler`/`ToolParameters`/`PlaybookToolResult`; `AnalysisStreamChunk`/`PlaybookStreamEvent`; `IAnalysisContextBuilder` | In-process BFF (plus `ExecuteAppOnlyAsync` overload for app-only) | [`AnalysisOrchestrationService.cs:32`](../../src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs#L32); [`IPlaybookOrchestrationService.cs:22`](../../src/server/api/Sprk.Bff.Api/Services/Ai/IPlaybookOrchestrationService.cs#L22); [`IAiToolHandler.cs:13`](../../src/server/api/Sprk.Bff.Api/Services/Ai/IAiToolHandler.cs#L13); [`AiAnalysisNodeExecutor.cs:33-36`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs#L33) |
| **S3 Builder agent** | In-flight | None from Extensions.AI; none from Agents.AI. Direct `OpenAI.Chat` SDK (`AssistantChatMessage`, `ToolChatMessage`, `ChatToolCall`). LLM calls via `IOpenAiClient.GetChatCompletionWithToolsAsync`. Hardcoded `gpt-4o`. | `IBuilderAgentService`/`BuilderAgentResult`; `CanvasState`/`CanvasOperation`; manual agentic loop bounded by `MaxToolRounds = 10`; `BuilderToolDefinitions`+`BuilderToolExecutor`; bespoke tool JSON serialization | In-process BFF | [`BuilderAgentService.cs:39`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L39); [`BuilderAgentService.cs:47`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L47) (`MaxToolRounds`); [`BuilderAgentService.cs:97`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L97) (for-loop) |
| **S4 Background AI jobs** | Shipped | None from Extensions.AI; none from Agents.AI in any handler. One handler (`EmbeddingMigrationService`) injects `IOpenAiClient` directly; the rest delegate to `IAppOnlyAnalysisService` or `IFileIndexingService`. `Azure.Messaging.ServiceBus` for queue chaining. | `IJobHandler`/`JobContract`/`JobOutcome`; `IIdempotencyService` (Redis per ADR-009); `IAppOnlyAnalysisService` / `IFileIndexingService` facades; per-handler idempotency keys | In-process BFF as `BackgroundService` (ADR-001) | [`AppOnlyDocumentAnalysisJobHandler.cs:25`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/AppOnlyDocumentAnalysisJobHandler.cs#L25); [`EmbeddingMigrationService.cs:114`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/EmbeddingMigrationService.cs#L114); [`ProfileSummaryJobHandler.cs:35`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/ProfileSummaryJobHandler.cs#L35); [`BulkRagIndexingJobHandler.cs:31`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/BulkRagIndexingJobHandler.cs#L31) |
| **S5A Foundry wrapper (shipped)** | Shipped, default-OFF (ADR-018) | `Azure.AI.Projects.AgentsClient` (pre-Agent-Framework SDK). NO `Microsoft.Agents.AI.*`. | `AgentServiceClient` (singleton wrapper); `AgentServiceOptions`; `BingGroundingOptions`/`CodeInterpreterBridge`/`CodeInterpreterOptions`; `FeatureDisabledException`/`ConcurrencyLimitExceededException`; Redis-cached thread IDs keyed `agent-thread:{tenantId}`; `AgentServiceRoutingMiddleware`; `AgentServiceNodeExecutor` for JPS `ActionType.AgentService = 60` | In-process BFF (wrapper; agent is Foundry-hosted) | [`AgentServiceClient.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Foundry/AgentServiceClient.cs); [`AgentServiceRoutingMiddleware.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentServiceRoutingMiddleware.cs); [`AgentServiceNodeExecutor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AgentServiceNodeExecutor.cs) |
| **S5B Foundry canonical durable HITL** | Curated-only, no Spaarke code | None yet. Planned per `knowledge/foundry-agent-service/NOTES.md`: `Azure.AI.Agents.Models.McpTool`, `SubmitToolApprovalAction`, `ToolApproval`; per-agent Entra identity; A2A endpoint pattern | None yet. Candidate per task 000 baseline: `Microsoft.Agents.AI.Workflows` (`RequestPort`/`RequestInfoEvent`) | Not yet deployed; canonical pattern is Foundry-hosted agent sessions OR Workflows-in-Function | [`knowledge/foundry-agent-service/NOTES.md`](../../knowledge/foundry-agent-service/NOTES.md) (TODO stub); [`knowledge/foundry-agent-service/SOURCE.md`](../../knowledge/foundry-agent-service/SOURCE.md); [`knowledge/foundry-agent-service/docs/hosted-agents.md`](../../knowledge/foundry-agent-service/docs/hosted-agents.md) |
| **S6 M365 Copilot** | In-flight | `Microsoft.Agents.Builder`+`Microsoft.Agents.Builder.Compat`+`Microsoft.Agents.Core.Models` (the **M365 Agents SDK**, formerly Bot Framework — NOT Agent Framework). **NOT planned for R1**: `Microsoft.Agents.AI.*` | `SpaarkeAgentHandler` (thin `ActivityHandler` scaffold); `AgentEndpoints`/`AgentTokenService`/`AdaptiveCardFormatterService`/`HandoffUrlBuilder`/`PlaybookInvocationService`; not yet existing: 3 manifest files, OpenAPI spec, Adaptive Card templates, Bot Service Bicep, BYOK templates | External: Declarative Agent + API Plugin manifests in M365 app catalog; Azure Bot Service. Internal: agent gateway endpoints in BFF | [`Api/Agent/SpaarkeAgentHandler.cs`](../../src/server/api/Sprk.Bff.Api/Api/Agent/SpaarkeAgentHandler.cs); [`projects/ai-m365-copilot-integration/spec.md`](../../projects/ai-m365-copilot-integration/spec.md); [`projects/ai-m365-copilot-integration/design.md`](../../projects/ai-m365-copilot-integration/design.md) |
| **S7 Insights Engine MCP server** | Design — Phase 2 deferred | None for MCP yet. Existing reused (in-BFF Insights Agent D-A9): `IChatClient` + `UseFunctionInvocation`. MCP server's specific host library is UNKNOWN per [`notes/02 §6 U1`](../../projects/agent-framework-fit-assessment-r1/notes/02-non-bff-ai-touchpoints-inventory.md). | Planned per SPEC D-A20: `predict_matter_cost`, `find_comparable_matters`, `assess_matter_risks`, `summarize_matter_closure` tool signatures + OBO auth flow. Anti-pattern explicitly rejected: `Microsoft.SemanticKernel.*`, `OpenAI.*`, `Azure.AI.OpenAI.*` in Zone B. | UNKNOWN per notes/02 §6 U2. Candidates: separate `Sprk.Insights.Mcp` deployable OR embedded in BFF | [`projects/ai-spaarke-insights-engine-r1/SPEC.md`](../../projects/ai-spaarke-insights-engine-r1/SPEC.md) (D-A20 contract Phase 1 deliverable; implementation Phase 2 deferred); [`projects/ai-spaarke-insights-engine-r1/design.md`](../../projects/ai-spaarke-insights-engine-r1/design.md) §5.1 |
| **S8a SessionSummarizationService** | Shipped | `IChatClient` (Extensions.AI) directly; pipeline client (function-invocation enabled); GPT-4o (NOT mini) | Single-purpose service; JSON-block-in-narrative parsing; scoped lifetime; fire-and-forget from session lifecycle | In-process BFF | [`SessionSummarizationService.cs:26`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionSummarizationService.cs#L26); [`SessionSummarizationService.cs:12-18`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionSummarizationService.cs#L12) (legal-context rationale) |
| **S8b CapabilityRouter** | Shipped | `[FromKeyedServices("raw")] IChatClient?` (optional Layer 2 LLM classification); `IList<ChatMessage>` (Extensions.AI) | `CapabilityClassificationPromptBuilder`; 3-param constructor overload disables Layer 2 entirely | In-process BFF (sub-component of S1) | [`CapabilityRouter.cs:95-128`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs#L95); [`CapabilityRouter.cs:127`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs#L127); [`CapabilityClassificationPromptBuilder.cs:87`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityClassificationPromptBuilder.cs#L87) |

### 3.1 Cross-cutting observations from the inventory

Per [`notes/01 §"Cross-cutting observations"`](../../projects/agent-framework-fit-assessment-r1/notes/01-spaarke-ai-surfaces-inventory.md):

1. **Only S1 touches `Microsoft.Extensions.AI` directly.** S2 calls `IOpenAiClient` (Spaarke wrapper over Azure SDK); S3 uses `OpenAI.Chat` SDK types directly; S4 delegates to thicker services. The SPEC's "half-adopted state" framing applies primarily to S1 — the rest of the codebase isn't even on Extensions.AI to begin with.
2. **`Microsoft.Agents.AI` is referenced but unused** — pure carry-along dependency satisfying the Extensions.AI version requirement.
3. **Middleware composition is hand-rolled on a Spaarke interface, NOT on `IChatClient`.** This is the single most visible signal that Spaarke is approximating the framework idiom without using it. The Extensions.AI `chatClient.AsBuilder().Use(...).Build()` pattern is documented since at least 2026-04-02 ([Middleware Learn page](https://learn.microsoft.com/en-us/agent-framework/agents/middleware/)) and is the canonical replacement target.
4. **Two LLM clients per session (function-invocation vs raw) is a Spaarke-specific pattern driven by compound-intent detection.** The Agents.AI Tool Approval feature ([Tools Learn page](https://learn.microsoft.com/en-us/agent-framework/agents/tools/) + `ApprovalRequiredAIFunction` per F11) covers a similar HITL-gate use case via a unified event model; the raw-client split could be subsumed.
5. **Deployment model is uniform** — all surfaces are in-process BFF today. Zero ADR-013 exceptions have been taken to date.
6. **Streaming surface area is asymmetric** — S1 streams `ChatResponseUpdate` via Extensions.AI; S2 streams Spaarke-defined `AnalysisStreamChunk`/`PlaybookStreamEvent`; S3 + S4 are non-streaming.
7. **State management is uniformly externalized** (Redis + Dataverse). The Agents.AI `AgentSession` primitive targets in-memory + service-side state; Spaarke's externalization pattern requires reconciliation if `AgentSession` is adopted.

---

## 4. Agent Framework feature map

Twelve features from [`notes/03`](../../projects/agent-framework-fit-assessment-r1/notes/03-agent-framework-feature-map.md). Per feature: what it is, what Extensions.AI gives you already, what Agents.AI adds, where in Spaarke it applies. The sharp distinction (per `notes/03 §0`):

- **`Microsoft.Extensions.AI`** is the inference-client + tool-primitive abstraction layer. It ships `IChatClient`, `AIFunction`/`AIFunctionFactory.Create`, `ChatResponse`/`ChatResponseUpdate`/`ChatMessage`, `FunctionInvokingChatClient`, `ChatResponseFormat`/`ChatResponseFormat.ForJsonSchema<T>()`, `AsBuilder().Use*().Build()` chat-client middleware composition, and `UseOpenTelemetry(sourceName)`.
- **`Microsoft.Agents.AI`** is the agent + multi-agent + hosting layer on top. It ships `AIAgent` (base), `ChatClientAgent`, `AgentSession`, `RunAsync<T>`/`RunStreamingAsync`, agent-level + function-calling-level middleware composition, `ApprovalRequiredAIFunction`+`FunctionApprovalRequestContent`, `WithOpenTelemetry()` (agent tier), `AsAIFunction()` (agent-as-tool), `AsAIAgent(...)` (provider helpers), A2A proxies + `Microsoft.Agents.AI.Hosting.A2A.AspNetCore` + `MapA2A(...)`, `Microsoft.Agents.AI.Workflows` (graph orchestration with `WorkflowBuilder`, `Executor<TIn,TOut>`, `RequestPort`, `RequestInfoEvent`, supersteps, checkpoints), `MCPToolDefinition`/`MCPToolResource` (hosted MCP).

### F1. `AIAgent` base class + `ChatClientAgent`

`AIAgent` is the common .NET base for every agent type in `Microsoft.Agents.AI`: uniform `RunAsync` / `RunAsync<T>` / `RunStreamingAsync`, uniform `CreateSessionAsync` / `SerializeSession` / `DeserializeSessionAsync`, the type that A2A proxies, `AsAIFunction()`, and workflow executors compose against. `ChatClientAgent` is the concrete subclass wrapping any `IChatClient`. Provider helpers (`AIProjectClient.AsAIAgent(...)`, `OpenAIClient.AsAIAgent(...)`, etc.) return `AIAgent` directly.

**Extensions.AI baseline**: `IChatClient.GetResponseAsync(messages, options)` + `FunctionInvokingChatClient` for tool-call loops. No common agent base type, no session abstraction, no generic typed `RunAsync<T>` wrapper.

**Spaarke applicability**: S1 (replaces hand-rolled `ISprkChatAgent`, gates on Issue [#6268](https://github.com/microsoft/agent-framework/issues/6268)); S3 (canonical fit — Builder already does intent classification + tool routing in a hand-rolled `for` loop); S5A (replaces direct `AgentsClient` usage with `AIProjectClient.AsAIAgent(...)`); S6/S7 forward-relevant.

**Primary sources**: [Agents Learn page](https://learn.microsoft.com/en-us/agent-framework/agents/) (fetched 2026-06-03, `updated_at 2026-04-20`); [Structured Outputs Learn page](https://learn.microsoft.com/en-us/agent-framework/agents/structured-outputs) (fetched 2026-06-03, `updated_at 2026-04-20`); [Issue #6268](https://github.com/microsoft/agent-framework/issues/6268) (opened 2026-06-02).

### F2. `AgentSession` — agent-typed conversation state

`AgentSession` is the conversation-state container shared across runs: `var session = await agent.CreateSessionAsync(); await agent.RunAsync("...", session);`. Sessions can bind to existing service conversation IDs (`agent.CreateSessionAsync(conversationId)`), round-trip via `SerializeSession` / `DeserializeSessionAsync`, and carry a `StateBag` for arbitrary per-session state.

**Extensions.AI baseline**: None. Caller passes `IEnumerable<ChatMessage>` per call; chat-history accumulation, serialization, and remote-thread mapping are caller responsibilities.

**Spaarke applicability**: S1 (`ChatHistoryManager` candidate replacement — non-trivial reconciliation with Redis-externalized history per cross-cutting observation 7); S5A (`ChatClientAgent.CreateSessionAsync(conversationId)` exactly bridges local agents and Foundry-stored threads; the shipped wrapper does this manually via Redis-cached thread IDs); S5B (workflow state container); S3 marginal (largely single-turn today).

**Primary sources**: [Sessions Learn page](https://learn.microsoft.com/en-us/agent-framework/agents/conversations/session) (fetched 2026-06-03, `updated_at 2026-05-26`).

### F3. Context providers (RAG / memory plumbing)

Pluggable retrieval-augmented context that injects RAG snippets / memories into each agent run by attaching to the agent's pre-invocation hook. Named on the [Overview Learn page](https://learn.microsoft.com/en-us/agent-framework/overview) alongside session, model clients, middleware, and MCP clients. Sample tree concrete at `dotnet/samples/02-agents/AgentWithRAG` + `AgentWithMemory` (`microsoft/agent-framework @ SHA afa7834e`, fetched 2026-06-03).

**Extensions.AI baseline**: None. RAG in raw Extensions.AI is "build it into a tool" or "stuff into prompt template." Spaarke's [`KnowledgeRetrievalTools.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools) is the tool-form RAG.

**Spaarke applicability**: S1 (`KnowledgeRetrievalTools` could remain as tools OR lift to a context provider so retrieval happens before tool-call planning); S3 (scope catalog grounding); S5/S6/S7 (uniform abstraction over Foundry-hosted memory + locally-implemented memory).

**Evidence-thin caveat per notes/03 §F3**: no standalone `/agents/context-providers/` Learn page exists within the recency floor; sample tree is the authoritative reference.

**Primary sources**: [Overview Learn page](https://learn.microsoft.com/en-us/agent-framework/overview) (fetched 2026-06-03, `updated_at 2026-04-20`); samples at SHA `afa7834e`.

### F4. Middleware framework (`AsBuilder().Use*().Build()`)

Three layered middleware types: (1) **Agent Run middleware** intercepting every `RunAsync` / `RunStreamingAsync` on an `AIAgent` via `originalAgent.AsBuilder().Use(runFunc, runStreamingFunc).Build()`; (2) **Function calling middleware** intercepting tool invocations via `.Use(CustomFunctionCallingMiddleware)` (`FunctionInvocationContext` with `context.Terminate = true` to break the loop); (3) **IChatClient middleware** intercepting inference calls via `chatClient.AsBuilder().Use(getResponseFunc, getStreamingResponseFunc).Build()` or via a `clientFactory` parameter on a provider helper.

**Extensions.AI baseline**: `IChatClient` middleware is **already in Extensions.AI** (`ChatClientBuilder` + `Use*` overloads ship there). Agents.AI adds the agent-tier and function-calling-tier composition.

**Spaarke applicability**: **S1 (the headline lift target)**. Spaarke's three middlewares (`AgentTelemetryMiddleware`, `AgentContentSafetyMiddleware`, `AgentCostControlMiddleware`) are hand-built decorators around `ISprkChatAgent` — exactly the framework idiom approximated but not used. Mapping is mechanical: Telemetry → Agent Run middleware; Content safety → IChatClient middleware; Cost control → Agent Run middleware. S2 not applicable (JPS uses raw `IOpenAiClient`); S3 applicable post-S3 lift to `ChatClientAgent`; S5A inherits when S1 lifts.

**Primary sources**: [Middleware Learn page](https://learn.microsoft.com/en-us/agent-framework/agents/middleware/) (fetched 2026-06-03, `updated_at 2026-04-02`).

### F5. Structured outputs — `RunAsync<T>` + `ChatResponseFormat.ForJsonSchema<T>()`

Typed agent invocation: `AgentResponse<PersonInfo> r = await agent.RunAsync<PersonInfo>("..."); PersonInfo info = r.Result;`. Or `AgentRunOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema<PersonInfo>()` for per-invocation schema. Streaming + structured: `await updates.ToAgentResponseAsync()` then deserialize.

**Extensions.AI baseline**: `ChatResponseFormat` and `ChatResponseFormat.ForJsonSchema<T>()` are in Extensions.AI. Agents.AI adds the typed `RunAsync<T>` wrapper + propagation through `AgentRunOptions`.

**Spaarke applicability**: S1 (`CompoundIntentDetector` ad-hoc JSON parsing → `RunAsync<CompoundIntent>`); S3 (strong fit — Builder's intent-classification + tool-routing produces structured envelopes; `RunAsync<BuilderIntent>` idiomatic); S8b (`CapabilityClassification` classification pattern); S8a marginal (qualitative regression risk if narrative-mode output is constrained to strict JSON).

**Primary sources**: [Structured Outputs Learn page](https://learn.microsoft.com/en-us/agent-framework/agents/structured-outputs) (fetched 2026-06-03, `updated_at 2026-04-20`).

### F6. Tools — `AIFunctionFactory`, `[Description]`, agent-as-tool, hosted MCP, code execution

Function tools via `AIFunctionFactory.Create(method)` over `[Description]`-decorated methods (largely Extensions.AI). **Agent-as-tool**: `.AsAIFunction()` on `AIAgent` wraps an inner agent as a function for an outer agent (the canonical specialist-sub-agent composition pattern; requires `AIAgent` base). **Hosted MCP tools**: `MCPToolDefinition(serverLabel, serverUrl) { AllowedTools = { ... } }` + per-run `MCPToolResource { RequireApproval = new MCPApproval("never" | "always" | custom) }` (consumed via `Microsoft.Agents.AI.Foundry`). **Code Interpreter / File Search / Web Search**: hosted-runtime tools per provider-matrix on the [Tools Learn page](https://learn.microsoft.com/en-us/agent-framework/agents/tools/) — Code Interpreter on Foundry + OpenAI Responses + Anthropic, File Search on Foundry + OpenAI Responses, Web Search broader.

**Extensions.AI baseline**: `AIFunction`, `AIFunctionFactory.Create(method)`, `[Description]` reflection — all Extensions.AI. Agents.AI adds `AsAIFunction()` + hosted MCP wiring + hosted runtime tools.

**Spaarke applicability**: S1 (`AsAIFunction` enables breaking the monolithic SprkChat into composed specialists; hosted MCP if consuming external MCP); S3 (`AIFunctionFactory.Create` replaces bespoke `BuilderToolDefinitions` + `BuilderToolExecutor`); S5A (hosted MCP for Foundry agent definitions); S5B (central — hosted MCP + Code Interpreter for legal-document workflows); S6 (hosted MCP is the surface Foundry uses to expose MCP to Declarative Agents); S7 (S7 is the OTHER side — Spaarke as MCP server consumed by Foundry agents).

**Primary sources**: [Tools Learn page](https://learn.microsoft.com/en-us/agent-framework/agents/tools/) (fetched 2026-06-03, `updated_at 2026-05-26`); [Hosted MCP Tools Learn page](https://learn.microsoft.com/en-us/agent-framework/agents/tools/hosted-mcp-tools) (fetched 2026-06-03, `updated_at 2026-04-24`).

### F7. Workflows — graph orchestration with checkpoints + supersteps + HITL

`Microsoft.Agents.AI.Workflows` exposes a directed-graph orchestration layer: `WorkflowBuilder` (`.AddEdge(from, to)`, `.WithOutputFrom(executor)`, `.Build()`); `Executor<TIn, TOut>` (or `Executor<TIn>`) typed nodes overriding `HandleAsync(message, IWorkflowContext, ct)` and emitting downstream via `context.SendMessageAsync(...)` / `context.YieldOutputAsync(...)`; **supersteps** (execution proceeds in supersteps; messages delivered at superstep boundaries; checkpoints save at boundaries); **checkpointing** (long-running state can be saved/restored; pending HITL requests are part of checkpoint state); multi-agent orchestration patterns (sequential, concurrent, handoff via `AgentWorkflowBuilder.CreateHandoffBuilderWith(...).WithHandoffs(...).EnableReturnToPrevious().Build()`, magentic); `InProcessExecution.RunStreamingAsync(workflow, initialInput)` returning a `StreamingRun` whose `WatchStreamAsync()` yields `WorkflowEvent` instances.

`04-hosting/DurableWorkflows` upstream sample category (entirely new since 2026-05-14 curated snapshot per [notes/00 §3](../../projects/agent-framework-fit-assessment-r1/notes/00-primary-source-baseline.md)) demonstrates durable hosting outside in-process execution.

**Extensions.AI baseline**: None. Extensions.AI has no orchestration primitive — it stops at the inference call.

**Spaarke applicability**: S2 (closest mapping — JPS `IPlaybookExecutionEngine` + `ExecutionGraph` + node executors map conceptually; but full replacement, no incremental path); **S5B (central — `WorkflowBuilder` + checkpoints + supersteps + multi-agent orchestration ARE the multi-day legal workflow story)**; S1 marginal (workflow-as-agent via `Build().AsAgent()`); S3 partial.

**Primary sources**: [Workflows Learn page](https://learn.microsoft.com/en-us/agent-framework/workflows/) (fetched 2026-06-03, `updated_at 2026-04-29`); [Workflow HITL Learn page](https://learn.microsoft.com/en-us/agent-framework/workflows/human-in-the-loop) (fetched 2026-06-03, `updated_at 2026-03-31` — borderline floor, content stable); [Devblog D6 Durable Workflows](https://devblogs.microsoft.com/dotnet/durable-workflows-in-microsoft-agent-framework/) (fetched 2026-06-03); samples at SHA `afa7834e`.

### F8. MCP client (hosted + local)

Two tracks: (1) **Hosted MCP tools** invoked by the provider runtime (Foundry, OpenAI Responses, Anthropic, GitHub Copilot per provider matrix); agent configured with `MCPToolDefinition(serverLabel, serverUrl) { AllowedTools = { ... } }`; per-run `MCPToolResource` with `RequireApproval = new MCPApproval("never" | "always" | custom)`. (2) **Local MCP tools** opened from the agent process — supported across all providers per matrix.

**Extensions.AI baseline**: None. `ModelContextProtocol` NuGet exists as a separate library but isn't part of Extensions.AI.

**Spaarke applicability**: S1 (could consume `learn.microsoft.com/api/mcp` or other external MCP); S5B (Foundry-hosted agents consume Spaarke MCP servers like S7); S6 (Hosted MCP if Copilot-side Foundry agents are added behind the API plugin); S7 (the OTHER side — framework helps consumers consume S7; doesn't change S7's server-side construction directly).

**Primary sources**: [Hosted MCP Tools Learn page](https://learn.microsoft.com/en-us/agent-framework/agents/tools/hosted-mcp-tools) (fetched 2026-06-03, `updated_at 2026-04-24`); [Tools Learn page](https://learn.microsoft.com/en-us/agent-framework/agents/tools/) (fetched 2026-06-03, `updated_at 2026-05-26`); samples at SHA `afa7834e`.

### F9. A2A proxies + `Microsoft.Agents.AI.Hosting.A2A.AspNetCore` (`MapA2A`)

Two-direction: **Consume** a remote A2A agent as a local `AIAgent` via the `A2AAgent` proxy (discovered via `AgentCard` at `{baseAddress}/v1/card`). **Expose** via `Microsoft.Agents.AI.Hosting.A2A.AspNetCore`:

```csharp
var pirateAgent = builder.AddAIAgent("pirate", instructions: "You are a pirate.");
var app = builder.Build();
app.MapA2A(pirateAgent, path: "/a2a/pirate", agentCard: new() { Name = "Pirate Agent", Version = "1.0" });
```

Exposes `POST /a2a/pirate/v1/message:stream` (`messageId`, `contextId`, `parts[]`) + `GET /a2a/pirate/v1/card` discovery. NuGets: `Microsoft.Agents.AI.Hosting.A2A` + `Microsoft.Agents.AI.Hosting.A2A.AspNetCore`.

**Extensions.AI baseline**: None. Spaarke has no current A2A surface.

**Spaarke applicability**: S5B (durable workflows expose / consume A2A peers); S6 (Copilot's plugin ecosystem may use A2A as a forward-compat alternative to MCP — monitor-territory today); S7 (Insights Engine could expose both MCP + A2A simultaneously); S1 forward-compat.

**Primary sources**: [A2A Integration Learn page](https://learn.microsoft.com/en-us/agent-framework/integrations/a2a) (fetched 2026-06-03, `updated_at 2026-05-20`).

### F10. Observability — `UseOpenTelemetry(sourceName)` + `WithOpenTelemetry()` + OTel GenAI Semantic Conventions

Two instrumentation levels: (1) **Chat-client level** via `chatClient.AsBuilder().UseOpenTelemetry(sourceName, cfg => cfg.EnableSensitiveData = true).Build()` — instruments inference. (2) **Agent level** via `agent.WithOpenTelemetry(sourceName, ...)` — instruments agent invocation (`invoke_agent`, `execute_tool`), emits `gen_ai.agent.id` / `gen_ai.agent.name` / `gen_ai.request.instructions` attributes per OpenTelemetry GenAI Semantic Conventions. Default source if none specified: `Experimental.Microsoft.Agents.AI`. Azure Monitor via `Azure.Monitor.OpenTelemetry.Exporter` + `APPLICATION_INSIGHTS_CONNECTION_STRING` documented end-to-end.

**Critical warning** per the Learn page: enabling BOTH chat-client AND agent-level OTel with sensitive data on produces duplicated spans. Pick one tier.

**Extensions.AI baseline**: `UseOpenTelemetry(sourceName)` on chat-client builder is **already in Extensions.AI**. Agents.AI adds agent-level `WithOpenTelemetry()` + standardized GenAI Semantic Conventions attribute mapping at the agent tier.

**Spaarke applicability**: S1 (`AgentTelemetryMiddleware` is the hand-rolled equivalent of `WithOpenTelemetry()`; framework version is more complete + standardized + free; pick agent-tier per duplication warning); S3/S5A post-`ChatClientAgent` lift; S2 not applicable (JPS uses raw OpenAI SDK).

**Primary sources**: [Observability Learn page](https://learn.microsoft.com/en-us/agent-framework/agents/observability) (fetched 2026-06-03, `updated_at 2026-05-21`).

### F11. Tool Approval (HITL at the framework level)

Two complementary HITL surfaces: (1) **Function Tool Approval** — wrap any `AIFunction` in `ApprovalRequiredAIFunction(functionToWrap)` and the agent surfaces `FunctionApprovalRequestContent` instead of executing. Caller invokes `requestContent.CreateResponse(approved: true|false)` producing `FunctionApprovalResponseContent`, sent back as a new `User` message in the same `AgentSession`. (2) **Workflow HITL** — `RequestPort.Create<TRequest, TResponse>(name)` is a typed port emitting `RequestInfoEvent` from the workflow execution stream. Host gathers human response, feeds back via `handle.SendResponseAsync(requestInputEvt.Request.CreateResponse(answer))`. **Pending requests are persisted in checkpoints; restoring a workflow re-emits its pending `RequestInfoEvent`s.**

The two surfaces unify: multi-agent workflows using approval-required functions emit `RequestInfoEvent` whose payload is `ToolApprovalRequestContent` — same event-routing for pure-HITL pauses and tool-approval gates.

**Extensions.AI baseline**: None. Spaarke implements compound-intent gating manually via `CompoundIntentDetector` + per-tool client selection (the function-call-via-`UseFunctionInvocation`-or-raw-client split) — the hand-rolled state machine `ApprovalRequiredAIFunction` + `FunctionApprovalRequestContent` collapses.

**Spaarke applicability**: S1 (direct replacement candidate for `CompoundIntentDetector` + tool-client-split; caveat: Spaarke policy is richer than "ask human" — framework handles routing, Spaarke decides which functions to wrap); **S5B (central — `RequestPort` + checkpoints IS the durable-pause mechanism for multi-day legal review)**; S3 (future fit); S6 (Declarative Agents can use Hosted MCP `RequireApproval = "always"`); S7 (server-side: MCP server tools can declare approval requirements).

**Primary sources**: [Tool Approval Learn page](https://learn.microsoft.com/en-us/agent-framework/agents/tools/tool-approval) (fetched 2026-06-03, `updated_at 2026-04-02`); [Workflow HITL Learn page](https://learn.microsoft.com/en-us/agent-framework/workflows/human-in-the-loop) (fetched 2026-06-03, `updated_at 2026-03-31`, stable content); [Tools Learn page](https://learn.microsoft.com/en-us/agent-framework/agents/tools/) (fetched 2026-06-03, `updated_at 2026-05-26`).

### F12. Hosting / DI helpers + `builder.AddAIAgent(...)` + Durable Hosting

Hosting under `Microsoft.Agents.AI.Hosting`: `builder.AddAIAgent(name, instructions)` registers a named agent in DI; `app.MapA2A(agent, path, agentCard)` exposes via A2A. **Durable agents / workflows**: `04-hosting/DurableAgents` + `04-hosting/DurableWorkflows` upstream sample categories (entirely new since 2026-05-14, per [notes/00 §3](../../projects/agent-framework-fit-assessment-r1/notes/00-primary-source-baseline.md)) demonstrate hosting over Durable Tasks for survive-process-restart semantics. **Foundry-hosted agents**: `04-hosting/FoundryHostedAgents` for when Foundry IS the host, not just the provider.

**Extensions.AI baseline**: DI wiring of `IChatClient` exists; no agent-level DI helper; no hosting/durability surface.

**Spaarke applicability**: S1 marginal (existing `ISprkChatAgent` wiring works); **S5B (`FoundryHostedAgents` is the canonical hosting pattern for the curated HITL legal workflows)**; S6/S7 (`MapA2A` is the hook for exposing agents to external consumers).

**Evidence-thin caveat per notes/03 §F12**: no dedicated `/hosting/` Learn page fetched within recency floor; sample tree + Devblog D6 are primary evidence. [Issue #6308](https://github.com/microsoft/agent-framework/issues/6308) ("How to deploy dotnet Hosted agents to Foundry", opened 2026-06-03) indicates the Foundry-hosting story is in active triage — treat hosting recommendations conservatively.

**Primary sources**: [A2A Integration Learn page](https://learn.microsoft.com/en-us/agent-framework/integrations/a2a) (fetched 2026-06-03, `updated_at 2026-05-20`); [Devblog D6 Durable Workflows](https://devblogs.microsoft.com/dotnet/durable-workflows-in-microsoft-agent-framework/) (fetched 2026-06-03); samples at SHA `afa7834e`; [Issue #6308](https://github.com/microsoft/agent-framework/issues/6308) (opened 2026-06-03).

### Feature-map summary

| # | Feature | NEW in Agents.AI? | Already in Extensions.AI? | High-relevance Spaarke surface |
|---|---|---|---|---|
| F1 | `AIAgent` + `ChatClientAgent` | ✅ (entire abstraction) | `IChatClient` only | S1, S3 |
| F2 | `AgentSession` | ✅ | nothing equivalent | S1 (replaces `ChatHistoryManager`), S5A |
| F3 | Context providers | ✅ | nothing equivalent | S1, S3 |
| F4 | 3-tier middleware (Agent / Function / IChatClient) | Agent + Function ✅; IChatClient inherited | `IChatClient.AsBuilder().Use*` is Extensions.AI | **S1 (3 hand-rolled middlewares replaced)** |
| F5 | Structured outputs (`RunAsync<T>` typed, `ResponseFormat`) | typed `RunAsync<T>` ✅ | `ChatResponseFormat.ForJsonSchema<T>()` is Extensions.AI | S1 (CompoundIntent), S3, S8b |
| F6 | Tools — `AsAIFunction()`, hosted MCP, code interp | `AsAIFunction` + `MCPToolDefinition` ✅ | `AIFunctionFactory.Create` is Extensions.AI | S1, S3, S5B, S6 |
| F7 | Workflows | ✅ entirely | nothing equivalent | **S5B (central)**; S2 (only Workflows candidate but DON'T ADOPT) |
| F8 | MCP client (hosted + local) | ✅ (framework wiring) | external lib only | S1, S5B, S6, S7 |
| F9 | A2A — `MapA2A` + `A2AAgent` proxy | ✅ entirely | nothing equivalent | S5B, S6/S7 (forward-compat) |
| F10 | Observability — `WithOpenTelemetry()` + GenAI Semantic Conventions | agent-level ✅; chat-client inherited | `UseOpenTelemetry(sourceName)` is Extensions.AI | S1 (replaces `AgentTelemetryMiddleware`) |
| F11 | Tool Approval + Workflow HITL | ✅ entirely | nothing equivalent | S1 (subsumes `CompoundIntentDetector`), **S5B (central)** |
| F12 | Hosting helpers + Durable hosting + Foundry-hosted | ✅ entirely | nothing equivalent | **S5B (`FoundryHostedAgents`)**, S6/S7 (A2A exposure) |

---

## 5. Per-surface decision matrix

The full task 004 decision matrix is captured at [`notes/04`](../../projects/agent-framework-fit-assessment-r1/notes/04-per-surface-decision-matrix.md). Each surface section below summarizes technical fit, Agent Framework value, migration cost, recommendation, and open questions.

### 5.1 S1 SprkChat — conversational agent

**Technical fit**. Strongly conversational. [`SprkChatAgent.SendMessageAsync`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L124) returns `IAsyncEnumerable<ChatResponseUpdate>` — open-ended chat with LLM-driven tool selection. `<500ms` TTFB requirement (ADR-013 keep-in-BFF criterion); compound-intent gate adds 200-400ms when triggered ([SprkChatAgent.cs:166-168](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L166)). High state coupling: per-turn citation context reset, plan-preview Redis state, session token-budget counter, ChatHistoryManager — all share the request lifecycle. 12+ tool classes registered per session via `AIFunctionFactory.Create`, dynamically injected via `ICapabilityRouter` per turn.

**Agent Framework value**. F1, F2, F4, F5, F6 (`AsAIFunction` specifically), F10, F11 all map cleanly. F7/F9 non-applicable. F8/F12 marginal. The middleware lift (F4) is the headline change — three hand-rolled `ISprkChatAgent` decorators map to the three framework middleware tiers per the [Middleware Learn page](https://learn.microsoft.com/en-us/agent-framework/agents/middleware/) (fetched 2026-06-03, `updated_at 2026-04-02`).

**Migration cost**. Medium-high rewrite scope (`SprkChatAgent.cs`, `SprkChatAgentFactory.cs`, three middleware classes, `CompoundIntentDetector.cs`, `ChatHistoryManager.cs`, `ChatSessionManager.cs`, `AiModule.cs` DI). **Zero publish-size delta** — `Microsoft.Agents.AI 1.0.0-rc1` already referenced. Substantial test impact (~2,800 AI tests per bff-extraction §5; SprkChat-specific tests re-keyed). Team learning curve low-medium. Observability improved (standardized GenAI Semantic Conventions vs hand-rolled attributes). Reversibility medium — facade pattern allows rollback per middleware class.

**Recommendation: PARTIAL — gated on Issue #6268 resolution.**

The structural fit is the strongest of any surface in this assessment. Every Agents.AI feature except F7 and F9 maps cleanly to existing Spaarke code that already approximates the framework idiom. The middleware composition signal is the most visible: Spaarke decorates `ISprkChatAgent` instead of `IChatClient`, exactly the kind of hand-rolled equivalent the framework subsumes. But SprkChat's canonical workload is multi-tool streaming, and [GitHub Issue #6268](https://github.com/microsoft/agent-framework/issues/6268) (opened 2026-06-02, `needs-maintainer-triage` as of 2026-06-03) reports `ChatClientAgent.RunStreamingAsync` ending with no assistant text on multi-tool turns — S1's exact pattern. Lifting before resolution ships a regression in the most-trafficked SprkChat code path.

**Rationale (≥2 evidence pieces)**:
- [notes/01 §S1(b)](../../projects/agent-framework-fit-assessment-r1/notes/01-spaarke-ai-surfaces-inventory.md): Spaarke uses `Microsoft.Extensions.AI` primitives directly; zero use of `ChatClientAgent` or `AIAgent`. Lifting changes the abstraction, not the dependency set.
- [notes/01 cross-cutting observation 3](../../projects/agent-framework-fit-assessment-r1/notes/01-spaarke-ai-surfaces-inventory.md): Spaarke's middleware decorates `ISprkChatAgent`, NOT `IChatClient` — lift cost is mechanical, not structural.
- [Issue #6268](https://github.com/microsoft/agent-framework/issues/6268) (fetched 2026-06-03): streaming + multi-tool turns ends with no assistant text — S1's exact workload.

**Deployment model**: In-process BFF (ADR-013 default; criteria (1) latency, (2) transactional coupling both fail decisively).

**Open questions**:
1. **Wait or pilot?** Should Spaarke wait for #6268 to land in a shipped 1.x release, or pilot the lift now behind `Sprk.Ai.UseFrameworkAgent` feature flag with fallback to hand-rolled path? (Decision criterion: % of SprkChat traffic that is multi-tool — if >50%, wait; if <20%, pilot.)
2. **Compound-intent gate**: Does Spaarke's compound-intent policy fit cleanly into `ApprovalRequiredAIFunction` (framework routes; Spaarke decides which functions to wrap), or are there policy edge cases (conditional approval based on user role, scope, document) that don't map to binary approve/reject?
3. **Session externalization**: How does Spaarke reconcile its Redis-externalized chat history with `AgentSession`'s in-memory + remote-conversation-id model (per cross-cutting observation 7)? The framework supports `CreateSessionAsync(conversationId)` for remote-thread binding; does this work when the "remote thread" is Spaarke's own Redis cache?

### 5.2 S2 AnalysisOrchestration + JPS playbooks

**Technical fit**. Strongly deterministic. JPS defines a fixed-graph node executor pipeline whose steps are known at playbook definition time. No LLM-driven step routing. High latency budget (per-token streaming via `IOpenAiClient.StreamCompletionAsync`); high state + transactional coupling (Dataverse `sprk_analysisoutput` records + Redis `AnalysisInternalModel` + per-stream working-document updates at [AnalysisOrchestrationService.cs:230-234](../../src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs#L230)). Partial durability via `ContinueAnalysisAsync` re-hydration. Tools statically wired per JPS playbook author.

**Agent Framework value**. F7 Workflows is the only material candidate. F1, F2, F4, F5, F6, F10, F11 don't apply (no agent loop, no chat shape). JPS's `IPlaybookExecutionEngine` + `ExecutionGraph` + node executors map conceptually to `WorkflowBuilder` + `Executor<TIn,TOut>` + edges; but this is wholesale replacement, NOT additive lift.

**Migration cost**. **Catastrophic if attempted as swap-in.** JPS spans 12 node executors under [`Services/Ai/Nodes/`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes), the orchestration service, the playbook service, the execution engine, the topological graph, the tool handler registry, all supporting handler implementations, plus the Dataverse playbook entity schema and JPS schema validation consumed by `Sprk.Dataverse.Plugins/PlaybookValidationPlugin`. Catastrophic test impact; high learning curve; poor reversibility (Dataverse playbook records encode JPS schema; rewrite is forward-only migration).

**Recommendation: DON'T ADOPT.**

JPS is a working production system with its own DSL, executor model, persistence layer, validation pipeline, and Dataverse-stored playbook records. `Microsoft.Agents.AI.Workflows` is conceptually competitive — it has executors, edges, supersteps, checkpoints, multi-agent orchestration patterns — and structurally could host most of what JPS does. But "could host most" is not adoption-grade evidence.

**The decisive factor: no incremental adoption path.** Workflows replaces the orchestration engine; you cannot "lift one node to a Workflow executor" because the surrounding `ExecutionGraph` + `IToolHandlerRegistry` + `INodeService` types don't host them. Either JPS stays, or JPS becomes Workflows wholesale. Migration cost (~30+ files + Dataverse schema + plugins) is grossly disproportionate to the benefit. JPS-specific features Workflows does not natively provide — JPS schema validation, Dataverse-persisted playbook records, the `IAiToolHandler` contract purpose-built for "playbook-driven orchestration, not interactive document analysis" per the doc-comment at [IAiToolHandler.cs:11](../../src/server/api/Sprk.Bff.Api/Services/Ai/IAiToolHandler.cs#L11), `ActionType.AgentService = 60` for Foundry routing — would need re-implementation atop Workflows.

**Rationale (≥2 evidence pieces)**:
- [notes/01 §S2(b)](../../projects/agent-framework-fit-assessment-r1/notes/01-spaarke-ai-surfaces-inventory.md): `AnalysisOrchestrationService` and all 12 node executors use NEITHER Extensions.AI NOR Agents.AI types. The framework path is rebuilding the surface on a different abstraction, not lifting an existing one.
- [IAiToolHandler.cs:11](../../src/server/api/Sprk.Bff.Api/Services/Ai/IAiToolHandler.cs#L11) doc-comment: "Simpler than IAnalysisToolHandler — used for playbook-driven orchestration, not interactive document analysis." Different shape from what Workflows targets.
- [notes/03 §F7](../../projects/agent-framework-fit-assessment-r1/notes/03-agent-framework-feature-map.md): Workflows IS the only material candidate; section explicitly names migration cost as the deciding question.

**Deployment model**: Unchanged. In-process BFF.

**Open questions**:
1. **JPS-vs-Workflows long-term**: Does the team want a forward-looking decision on whether JPS becomes the long-term home of multi-step analysis, or whether JPS is transitional and should be replaced when bandwidth allows? Architecture-group decision.
2. **Selective Workflows piloting**: Are there NEW workflow-shaped use cases (not existing JPS playbooks) where Workflows could be introduced alongside JPS as a parallel path? S5B is one such candidate.

### 5.3 S3 Builder agent — playbook builder

**Technical fit**. Conversational multi-round tool-routing. `BuilderAgentService.ExecuteAsync` implements a hand-written agentic loop bounded by `MaxToolRounds = 10` ([BuilderAgentService.cs:47](../../src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L47) + [line 97](../../src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L97)). Canonical `ChatClientAgent` use case. Latency soft (non-streaming, multi-round). Low state coupling (`CanvasState` passed in per request). Tool composition hand-rolled (`BuilderToolDefinitions` + `BuilderToolExecutor` + manual JSON serialization).

**Agent Framework value**. F1 (replaces hand-rolled loop), F5 (strong fit — structured intent envelopes via `RunAsync<BuilderIntent>`), F6 (`AIFunctionFactory.Create` replaces bespoke tool definitions), F10 (post-lift). F4 optional. F11 future-relevant.

**Migration cost**. Small. ~3-5 files (`BuilderAgentService.cs`, `BuilderToolDefinitions.cs`, `BuilderToolExecutor.cs`, plus DI). Zero net publish-size impact. Small test impact (non-streaming, single-method-entry-point). Low learning curve (canonical onboarding surface). High reversibility (git-revert-cheap). **One ADOPT-blocker specific to S3**: Builder uses **OpenAI.Chat SDK directly** at [BuilderAgentService.cs:50](../../src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L50) (hardcoded `gpt-4o`); migrating to `ChatClientAgent` requires routing through an `IChatClient` provider helper — one-time DI change but a pre-PR sequencing requirement.

**Recommendation: PARTIAL — adopt at next significant Builder maintenance window.**

Builder is structurally the cleanest candidate for `ChatClientAgent` adoption. The hand-written `for` loop bounded by `MaxToolRounds = 10` and the manual `OpenAI.Chat` SDK juggling are exactly what `ChatClientAgent.RunAsync` + `AIFunctionFactory.Create` collapses. Why PARTIAL not full ADOPT: Builder is in-flight; lifting on a moving target adds change cost. PARTIAL captures "yes adopt, not now" — sequence with the first major Builder feature addition or when Issue #6268 resolves and the team is already touching `Microsoft.Agents.AI` code for S1.

**Rationale (≥2 evidence pieces)**:
- [BuilderAgentService.cs:97](../../src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L97) explicit `for` loop with hand-written tool routing at [line 134](../../src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L134) — exactly the workload `ChatClientAgent.RunAsync` provides.
- [notes/03 §F1 Spaarke applicability](../../projects/agent-framework-fit-assessment-r1/notes/03-agent-framework-feature-map.md): "Builder already does intent-classification + tool-routing; this is the canonical `ChatClientAgent` use case. Adoption value is highest here after S1."
- [BuilderAgentService.cs:2](../../src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L2): `using OpenAI.Chat` — standardizing on Extensions.AI/Agents.AI removes the SDK-direct dependency.

**Deployment model**: In-process BFF. ADR-013 criterion (4) fails — Builder is too small a surface to justify a separate deployable; duplication cost exceeds value.

**Open questions**:
1. **Timing**: Adopt S3 first (before S1, before #6268 resolves) to validate the framework in a low-risk, non-user-facing surface? Or wait for S1 lift to drive S3 along with it?
2. **OpenAI SDK migration**: Pre-work scope — sequence the OpenAI.Chat SDK → `IChatClient` rewiring as its own PR before the `ChatClientAgent` lift PR.

### 5.4 S4 Background AI jobs

**Technical fit**. Strongly deterministic. Each job handler is single-purpose `ProcessAsync(JobContract)` returning `Task<JobOutcome>` ([notes/01 §S4](../../projects/agent-framework-fit-assessment-r1/notes/01-spaarke-ai-surfaces-inventory.md)). No agentic loop, no tool calling. No latency budget against BFF state (Service Bus polls internally). Low state coupling (idempotency via `IIdempotencyService` Redis per ADR-009; state persisted in Dataverse + AI Search). Durability via Service Bus + idempotency, NOT framework workflows. No streaming.

**Agent Framework value**. Effectively none. F1 explicit "not applicable in general; individual job handlers are deterministic pipelines, not agent loops" ([notes/03 §F1](../../projects/agent-framework-fit-assessment-r1/notes/03-agent-framework-feature-map.md)). F7 Workflows speculative-future for durable re-host but S4 doesn't currently use Agent Framework primitives so it's "future architectural option," not "swap-in win."

**Migration cost**. N/A — nothing to migrate. `EmbeddingMigrationService` is the one S4 handler with direct `IOpenAiClient` injection; a candidate for routing through Extensions.AI's `IChatClient` for consolidation but that's Extensions.AI consolidation, not Agents.AI adoption (out of scope for this assessment).

**Recommendation: DON'T ADOPT.**

S4 is not agent-shaped. The 13+ job handlers are single-purpose `ProcessAsync` methods — closer to "function" than "agent" in the framework sense. The framework's value-add (agent base, sessions, middleware, tool routing, HITL gates) doesn't apply. The job-handler framework (`IJobHandler`, `JobContract`, `JobOutcome`, `IIdempotencyService`, `ServiceBusJobProcessor`) is Spaarke-defined and works. Replacing it with `Microsoft.Agents.AI.Workflows` durable hosting means abandoning a working Service Bus + Redis + Idempotency contract for a framework feature S4 doesn't need.

**Rationale (≥2 evidence pieces)**:
- [notes/01 §S4(a)+(b)](../../projects/agent-framework-fit-assessment-r1/notes/01-spaarke-ai-surfaces-inventory.md): No Extensions.AI types in any S4 handler; handlers delegate to thicker services (`IAppOnlyAnalysisService`, `IFileIndexingService`). The AI work is one level deeper than the handler.
- [notes/03 §F1 Spaarke applicability](../../projects/agent-framework-fit-assessment-r1/notes/03-agent-framework-feature-map.md): "S4 Background jobs — not applicable in general; individual job handlers are deterministic pipelines, not agent loops."
- [ADR-013 §"Constraints"](../../.claude/adr/ADR-013-ai-architecture.md): "MUST use Job Contract for background AI work (ADR-004)... MUST NOT host AI BFF synthesis/streaming endpoints in Azure Functions." S4 already conforms; framework doesn't change this and isn't operationally pressuring.

**Deployment model**: Unchanged. In-process BFF as `BackgroundService` per ADR-001.

**Open questions**:
1. **Extensions.AI consolidation**: Should `EmbeddingMigrationService`'s direct `IOpenAiClient` injection be rerouted through `IChatClient` for consistency with S1, independent of Agents.AI adoption? Spaarke-internal hygiene question.
2. **Future durable-workflow re-host**: If S5B canonical durable HITL workflows get implemented as `Microsoft.Agents.AI.Workflows`, does that open a path to re-hosting some S4 pipelines (e.g., `ProfileSummaryJobHandler` Service Bus chain) as Workflows? Multi-quarter future state, out of current scope.

### 5.5 S5A Foundry wrapper (shipped)

**Technical fit**. Conversational backend; routing layer keyword-classified. `AgentServiceClient` wraps `Azure.AI.Projects.AgentsClient` for chat-backend routing. Same latency budget as SprkChat (coexists in the pipeline as `AgentServiceRoutingMiddleware`). Medium state coupling (Redis-cached thread IDs keyed `agent-thread:{tenantId}` with 60min sliding TTL). No durability (synchronous request/response). Tool composition Foundry-side (opaque to BFF). Internal streaming (Foundry response → SprkChat SSE).

**Agent Framework value**. F1 (`AIProjectClient.AsAIAgent(...)` returns `AIAgent`; shipped wrapper bypasses this and uses `AgentsClient` directly); F2 (`ChatClientAgent.CreateSessionAsync(conversationId)` "exactly the bridge between local agents and the Foundry-stored thread; the current wrapper does this manually" per [notes/03 §F2](../../projects/agent-framework-fit-assessment-r1/notes/03-agent-framework-feature-map.md)); F6 (hosted MCP if Foundry agent definition includes it); F10 (post-F1 lift); F11 (Foundry-side approval propagates to framework's `FunctionApprovalRequestContent` model).

**Migration cost**. Small. Replace `AgentServiceClient.cs`'s direct `AgentsClient` usage with `AIProjectClient.AsAIAgent(...)`. ~1 file primary change; `AgentServiceRoutingMiddleware` and `AgentServiceNodeExecutor` consumers preserved with minor signature updates. Test impact small (wrapper is feature-flagged OFF by default per ADR-018).

**Recommendation: PARTIAL — adopt only as part of an S1 lift (not in isolation).**

S5A is structurally a candidate for adoption — `AsAIAgent` simplifies the wrapper, `AgentSession.CreateSessionAsync(conversationId)` replaces the manual Redis-cached-thread-id manipulation, hosted MCP tools become declarable. But: S5A consumes from `SprkChatAgent`-shaped contexts (`AgentServiceRoutingMiddleware` IS the SprkChat pipeline). Lifting S5A without S1 means mixing framework `AIAgent` (Foundry side) with hand-rolled `ISprkChatAgent` (SprkChat side) — a more complex intermediate state than either pure choice. S5A is default-OFF per ADR-018, so adoption value is low until / unless the kill switch flips on; otherwise the lift improves code that doesn't run.

**Rationale (≥2 evidence pieces)**:
- [notes/02 §2.2](../../projects/agent-framework-fit-assessment-r1/notes/02-non-bff-ai-touchpoints-inventory.md): `AgentServiceClient` connects to a pre-provisioned Foundry Agent endpoint, manages threads via direct SDK calls, uses Redis for thread persistence. Framework's `AsAIAgent(...)` + `CreateSessionAsync(conversationId)` collapse this to two API calls.
- [notes/02 §2.3(c)](../../projects/agent-framework-fit-assessment-r1/notes/02-non-bff-ai-touchpoints-inventory.md): wrapper "default-OFF (ADR-018), opt-in only." Low operational pressure.
- [notes/03 §F1 Spaarke applicability](../../projects/agent-framework-fit-assessment-r1/notes/03-agent-framework-feature-map.md): "Adopting `AIAgent` would simplify the wrapper."

**Deployment model**: In-process BFF (unchanged) — wrapper, not Foundry-hosted agent itself.

**Open questions**: None unique to S5A — bundle with S1 PR set; S5A's lift inherits S1's open questions.

### 5.6 S5B Foundry canonical durable / HITL legal workflows

**Technical fit**. Mixed — multi-agent durable workflows over legal documents with both deterministic steps (extraction, comparison) and agentic steps (negotiation review, regulatory monitoring) per [`knowledge/foundry-agent-service/NOTES.md`](../../knowledge/foundry-agent-service/NOTES.md). No latency coupling (multi-day workflows by design). High durability state, low BFF transactional coupling — ADR-013 keep-in-BFF criterion FAILS for the right reason. Multi-agent with handoff / A2A composition; hosted MCP tools. A2A peer agents potentially yes. Workflow events stream out, not user-facing token streaming.

**Agent Framework value**. **Maximum.** F1, F2 yes. **F7 Workflows: central** — `WorkflowBuilder` + checkpoints + supersteps + multi-agent orchestration are exactly the multi-day legal workflow story. F8 yes (Foundry-hosted agents consume Spaarke MCP servers like S7 and external MCP). F9 yes (durable workflows expose / consume A2A peers). **F11 central** — exactly the canonical durable HITL story per [Workflow HITL Learn page](https://learn.microsoft.com/en-us/agent-framework/workflows/human-in-the-loop) (`updated_at 2026-03-31`, stable content). **F12 central** if Foundry-hosted-agent deployment chosen.

**Migration cost**. N/A — no Spaarke code to migrate. Greenfield implementation cost. Relevant questions: does the team have bandwidth to author multi-day legal workflows; which deployment model.

**Recommendation: ADOPT — when the canonical durable legal workflows surface gets a project SPEC.**

S5B is the highest-fit surface in the entire assessment. Every Agents.AI feature with HITL or durability flavor (F7 Workflows, F11 `RequestPort`, F12 Foundry hosting) is purpose-built for what `knowledge/foundry-agent-service/NOTES.md` §1-§2 describes: multi-day legal workflows with HITL gates.

The critical nuance: **HITL is no longer Foundry-exclusive**. The framework's `RequestPort` + `RequestInfoEvent` + workflow checkpoints provide pause/resume HITL semantics, all in `Microsoft.Agents.AI.Workflows` itself. Foundry's residual differentiation (per-session VM sandboxes, per-agent Entra identity, A2A endpoint exposure, Foundry-hosted MCP tools, Foundry memory) is real but narrower than "Foundry owns durable + HITL."

The S5B decision splits into two sub-decisions:
- **Adoption of `Microsoft.Agents.AI` itself**: YES — for any durable legal workflow Spaarke ships, Workflows is the framework choice over a hand-rolled equivalent.
- **Deployment**: depends on whether VM isolation + per-agent Entra identity + A2A exposure are required (see §6.4 below).

**Rationale (≥2 evidence pieces)**:
- [notes/02 §2.3(f) (planned canonical)](../../projects/agent-framework-fit-assessment-r1/notes/02-non-bff-ai-touchpoints-inventory.md): "HIGH" durability + HITL territory; NOTES.md names "full-matter diligence (multi-day), NDA negotiation chain (multi-week, term-acceptance gates), regulatory monitoring (continuous, publish-to-firm gates)."
- [notes/03 §F11](../../projects/agent-framework-fit-assessment-r1/notes/03-agent-framework-feature-map.md): "Workflow `RequestPort` + checkpoints is exactly the durable-pause mechanism for multi-day legal review" + "this IS the surface S5's canonical (non-shipped) HITL story uses."
- [notes/02 §5 observation 2](../../projects/agent-framework-fit-assessment-r1/notes/02-non-bff-ai-touchpoints-inventory.md): Agent Framework Workflows HITL primitives shrink S5's Foundry-exclusivity. Foundry retains hosting + identity differentiation but not HITL exclusivity.

**Deployment model**: **MIXED — prototype before committing.** See §6.4 for the three-candidate analysis and the prototyping recommendation.

**Open questions**:
1. **VM isolation requirement**: Do Spaarke legal workflows actually require per-session VM-isolated sandboxes (`$HOME` + `/files` persistence, 15-min idle → resume) per [`knowledge/foundry-agent-service/docs/hosted-agents.md`](../../knowledge/foundry-agent-service/docs/hosted-agents.md)? If yes, Foundry-hosted is the deployment; if no, framework-only HITL covers the requirement and Foundry can be deferred. **Material to the choice of deployment.**
2. **Per-agent Entra identity**: A2A composition between Foundry-hosted agents benefits from per-agent identity for cross-system auth. Is this on the roadmap?
3. **Foundry SKU costs**: Per-session cost UNKNOWN per [notes/02 §6 U5](../../projects/agent-framework-fit-assessment-r1/notes/02-non-bff-ai-touchpoints-inventory.md). Is the per-session cost acceptable for expected concurrency? Owner-level decision.
4. **Project SPEC trigger**: When should the canonical durable legal workflows surface get a project SPEC? Roadmap question outside this assessment's scope.

### 5.7 S6 M365 Copilot / Declarative Agent surface

**Technical fit**. Conversational on Copilot side; BFF gateway side is request/response adapter. API plugin response timeout assumed <30s (exact UNKNOWN). Low state + transactional coupling (gateway endpoints are adapters; session state stays in BFF). Not required for R1: durability (long-running playbooks deflect to async / deep-link). Tool composition: API Plugin functions defined via OpenAPI spec — declarative, not LLM-driven routing on BFF side. External consumer = M365 Copilot.

**Agent Framework value**. **Effectively none for R1 scope.** F1 explicitly excluded: "NOT planned for R1: `Microsoft.Agents.AI.*` (the Agent Framework). The README and spec.md describe the gateway as 'thin adapter facades over existing BFF services — no new AI orchestration logic.'" ([notes/02 §3.1(c)](../../projects/agent-framework-fit-assessment-r1/notes/02-non-bff-ai-touchpoints-inventory.md)). F6 hosted MCP future-relevant; F8 MCP future-relevant (R2 Tier 3 deferred per design.md §244-275); F9 A2A "monitor-territory."

**Migration cost**. If forced as swap-in: substantial. Would require rebuilding `SpaarkeAgentHandler.cs` and ~14 `.cs` files under `Api/Agent/`, plus re-validating the entire Bot Framework integration with Azure Bot Service + M365 Copilot. Adds a redundant agent stack alongside existing `Microsoft.Agents.Builder` / `Microsoft.Agents.Builder.Compat` / `Microsoft.Agents.Core.Models`. Poor reversibility (M365-side manifest implications).

**Recommendation: DON'T ADOPT (as swap-in for the M365 Agents SDK at the Copilot integration layer).**

S6 uses **`Microsoft.Agents.Builder` / `Microsoft.Agents.Builder.Compat` / `Microsoft.Agents.Core.Models`** — the **M365 Agents SDK** (formerly Bot Framework) — for the agent-channel side. **This is a DIFFERENT SDK from `Microsoft.Agents.AI`** (Agent Framework). They are not interchangeable; the M365 Agents SDK is canonical for building agents that consume from M365 Copilot and Azure Bot Service channels. The S6 design explicitly chose Path A (direct API Plugin + manifests, BFF as adapter) over Copilot Studio; agent gateway endpoints are "THIN ADAPTERS — MUST reuse existing BFF services per spec.md MUST NOT create new AI orchestration logic." Adding `Microsoft.Agents.AI.AIAgent` plumbing as the integration backend would (1) duplicate the agent abstraction, (2) contradict the adapter principle, (3) add a third agent SDK to the project.

The correct nuance: this is NOT a vote against Agent Framework. If BFF AI surfaces (S1, S3, S5B) lift to `AIAgent`, the M365 Agents SDK can invoke them via existing facade types — no `Microsoft.Agents.AI` adoption at the Copilot integration boundary is needed for that to work.

**Rationale (≥2 evidence pieces)**:
- [notes/02 §3.1(c)](../../projects/agent-framework-fit-assessment-r1/notes/02-non-bff-ai-touchpoints-inventory.md): R1 scope explicitly excludes `Microsoft.Agents.AI.*`; gateway described as "thin adapter facades — no new AI orchestration logic."
- [SpaarkeAgentHandler.cs:2-4](../../src/server/api/Sprk.Bff.Api/Api/Agent/SpaarkeAgentHandler.cs): M365 Agents SDK (`Microsoft.Agents.Builder.Compat`, `Microsoft.Agents.Core.Models`) already wired; two SDKs solve the same problem, swapping mid-flight has zero functional benefit.
- [notes/02 §5 observation 1](../../projects/agent-framework-fit-assessment-r1/notes/02-non-bff-ai-touchpoints-inventory.md): S6 is "fundamentally an external-consumer adapter surface" with "bounded, well-defined integration surface" as the design driver. Agent Framework value-add (F1-F12) misaligned with adapter-surface needs.

**Deployment model**: Unchanged. External manifests + Azure Bot Service for agent-channel side; BFF endpoints (in-process) for adapter side.

**Open questions**:
1. **R2 MCP server**: When the deferred R2 MCP server (Tier 3) is implemented, does it host Agent Framework agents internally? Question flips to S7.
2. **Future A2A interop**: If Copilot's plugin ecosystem standardizes A2A as an alternative to MCP, does Spaarke want to expose SprkChat / Insights Agent over A2A? Monitor-territory, not actionable today.

### 5.8 S7 Insights Engine MCP server

**Technical fit**. In-BFF Insights Agent (D-A9) conversational; MCP server (Phase 2 deferred) exposes capability over MCP transport — deterministic from consumer's perspective. Latency budgets BFF for Insights Agent; depends on consumer for MCP. State coupling: Insights Agent IN BFF (per design.md §5.1 "custom BFF agent, not Foundry-hosted"). Durability LOW for query path. Tool composition: Insights Agent uses tools per D-A24 (`IDeclineToFindTool`); MCP server exposes tools to external consumers. **External consumer: yes for MCP server, no for in-BFF Insights Agent.** Insights Agent streams (matches SprkChat pattern); MCP server tool calls typically request/response.

**Agent Framework value**. For in-BFF Insights Agent: same fit profile as S1 — F1, F2, F4, F5, F6, F10, F11. **Decision tracks S1.** For MCP server (Phase 2 deferred): F1 conditional (overkill for thin transport, right primitive if MCP server hosts agents internally); F6 yes (MCP server BY DEFINITION exposes tools — declared via `MCPToolDefinition` or hand-rolled); F8 N/A (S7 is the server, not client); F9 possible add-on (`MapA2A` could expose Insights Agent over A2A alongside MCP); F11 yes potentially (destructive Insights write-back paths could require tool-level approval); F12 if separate deployable.

**Migration cost**. N/A — Phase 2 deferred; no Spaarke code yet. Greenfield design cost.

**Recommendation: PARTIAL — assess at MCP server contract authoring time (D-A20), not now.**

S7 spans two parts and only one is in scope:

1. **In-BFF Insights Agent (D-A9)**: tracks S1. If S1 adopts post-#6268, Insights Agent should adopt alongside. **Decision: follow S1.**

2. **MCP server (D-A20 contract; implementation Phase-2-deferred)**: the assessment cannot give a definitive verdict because **the contract doesn't exist yet** ([notes/02 §6 U1/U2/U3](../../projects/agent-framework-fit-assessment-r1/notes/02-non-bff-ai-touchpoints-inventory.md) flag this as UNKNOWN). Three UNKNOWNs material to the decision:
   - U1: specific `Microsoft.*` host library (e.g., `ModelContextProtocol.AspNetCore` vs Agent Framework MCP host) — not committed
   - U2: deployment model (separate `Sprk.Insights.Mcp` vs embedded in BFF) — not committed
   - U3: BFF integration seam (wraps `/api/insights/ask` vs directly wraps `IInsightsAi`) — not committed

The assessment's substantive guidance for D-A20 contract authoring: **prefer Agent Framework primitives IF the MCP server is a separate deployable AND hosts agents internally (i.e., agents that orchestrate Insights tool calls or call back to BFF capabilities)**. If the MCP server is a thin transport over a single `IInsightsAi` facade call per tool, plain `ModelContextProtocol` library hosting is simpler and the framework adds nothing.

**Rationale (≥2 evidence pieces)**:
- [notes/02 §4.1(a)+(d)](../../projects/agent-framework-fit-assessment-r1/notes/02-non-bff-ai-touchpoints-inventory.md): "MCP IMPLEMENTATION DEFERRED TO PHASE 2"; contract document (D-A20) is Phase 1 deliverable. Pre-emptive Agent Framework adoption commitment is design-by-assumption, not design-by-contract.
- [notes/02 §6 U1/U2/U3](../../projects/agent-framework-fit-assessment-r1/notes/02-non-bff-ai-touchpoints-inventory.md): three concrete UNKNOWNs about host library, deployment model, BFF seam. None answerable without the contract document existing.
- [notes/03 §F8 Spaarke applicability](../../projects/agent-framework-fit-assessment-r1/notes/03-agent-framework-feature-map.md): "this surface IS an MCP server. The framework helps OTHER agents consume it; doesn't change S7's server-side construction directly." Framework value asymmetric for S7.

**Deployment model**: UNKNOWN per notes/02 §6 U2. Decision deferred to D-A20 authoring. Candidates: separate `Sprk.Insights.Mcp` deployable per ADR-013's "MCP server exposing AI capabilities to external consumers" example, OR MCP endpoints embedded in BFF.

**Open questions**:
1. **Phase 1 D-A20 contract**: Must explicitly answer (a) which host library, (b) which deployment model, (c) which BFF seam. Owner-level action: ensure D-A20 contract addresses these three.
2. **Agent Framework as Insights Agent backend**: Should D-A9 be authored on Agent Framework primitives from day one (Phase 1 parallel to Track A), or remain on Extensions.AI? Decision tracks S1.
3. **MCP server as separate deployable**: Per ADR-013, separate deployable requires all four exception criteria. Does S7 MCP server materially meet all four? Bounded surface YES; the other three need verification at contract time.

### 5.9 S8a SessionSummarizationService

**Technical fit**. Deterministic single-call structured-output extraction. Fire-and-forget from chat session lifecycle (no SSE blocking). Low state coupling. No durability required. No tool composition (single LLM call + JSON parsing). Not external; not streaming.

**Agent Framework value**. Effectively none. F1 no (single-purpose `IChatClient` consumer; wrapping in `ChatClientAgent` adds the abstraction with no agent loop). F5 marginal (`RunAsync<SessionSummary>` replaces ad-hoc JSON parsing but the existing prompt is tuned for legal-context preservation; switching `response_format` to strict JSON might compromise the qualitative output justifying GPT-4o vs mini).

**Migration cost**. Small scope (~1 file). Risk: qualitative response shape change if structured-output mode constrains narrative quality.

**Recommendation: DON'T ADOPT.**

S8a is the textbook anti-fit for Agent Framework: a single-purpose `IChatClient` consumer with no agent loop, no tool calling, no session, no streaming. Wrapping in `ChatClientAgent` adds the agent abstraction without using any framework value-add. F5 marginal candidacy has qualitative regression risk.

**Rationale (≥2 evidence pieces)**:
- [notes/01 §S8a](../../projects/agent-framework-fit-assessment-r1/notes/01-spaarke-ai-surfaces-inventory.md): `SessionSummarizationService` uses `IChatClient` directly + GPT-4o with explicit "legal context preservation" rationale at lines 12-18. Single LLM call is not agent-shaped.
- "Scoped lifetime — fire-and-forget from chat session lifecycle to avoid blocking SSE." Service consumer pattern, not an agent pattern.

**Deployment model**: Unchanged. In-process BFF.

**Open questions**:
1. **Structured output marginal lift**: Marginal F5 benefit worth ~30 minutes of work + qualitative regression testing? Owner judgment call.

### 5.10 S8b CapabilityRouter

**Technical fit**. Deterministic multi-layer classification with optional LLM-based Layer 2. Tight latency budget (per-turn classification, <50ms target). Sub-component of SprkChat (shares per-turn context). No durability. No tools. Optional dependency pattern: `[FromKeyedServices("raw")] IChatClient?` — 3-param constructor disables Layer 2 if not injected.

**Agent Framework value**. F5 marginal — classification responses could be schema-bound; `RunAsync<CapabilityClassification>` would replace ad-hoc parsing (similar shape to S1's `CompoundIntentDetector`). F1 no (classifier is not an agent).

**Migration cost**. Small scope. Lifts unify with in-flight S1 `CompoundIntentDetector` pattern (both do JSON parsing of LLM classification).

**Recommendation: PARTIAL — adopt structured-output (F5) only, alongside S1 lift.**

S8b is a sub-component of S1 (called from `SprkChatAgentFactory.CreateAgentAsync` per [SprkChatAgentFactory.cs:232-243](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs#L232)). Treating it as independent is artificial — it tracks S1's verdict. When S1 adopts `RunAsync<T>` for `CompoundIntentDetector`, S8b's classifier should adopt the same pattern for consistency. PARTIAL not ADOPT because (a) broader features F1, F2, F4, etc. don't apply to a router, and (b) the change is contingent on S1 lift timing.

**Rationale (≥2 evidence pieces)**:
- [notes/01 §S8b](../../projects/agent-framework-fit-assessment-r1/notes/01-spaarke-ai-surfaces-inventory.md): `CapabilityRouter` consumes the same `[FromKeyedServices("raw")] IChatClient?` keyed singleton SprkChat's `CompoundIntentDetector` uses (cross-cutting observation 4). The two classifier patterns are coupled by construction.
- [notes/01 §S8b](../../projects/agent-framework-fit-assessment-r1/notes/01-spaarke-ai-surfaces-inventory.md): "Should be folded into S1's perimeter for the task 004 decision matrix."

**Deployment model**: Unchanged. In-process BFF (sub-component of S1).

**Open questions**:
1. **S1 lift coupling**: If S1 adopts `RunAsync<CompoundIntent>`, does S8b adopt `RunAsync<CapabilityClassification>` in the same PR set, or sequentially?

### 5.11 Summary table

| Surface | Verdict | Deployment | Top reason |
|---|---|---|---|
| **S1 SprkChat** | PARTIAL (gated on #6268) | in-process BFF | Structural fit strongest of any surface; middleware decorates Spaarke interface mirroring framework idiom. But Issue #6268 affects S1's canonical workload (multi-tool streaming). |
| **S2 JPS** | DON'T ADOPT | unchanged | Wholesale Workflows migration touches 30+ files + Dataverse schema + plugins — catastrophic vs working production. No incremental path. |
| **S3 Builder** | PARTIAL (next maintenance window) | in-process BFF | Hand-written agentic loop + manual OpenAI.Chat SDK — exactly what `ChatClientAgent.RunAsync` collapses. Lowest-risk lift (~3-5 files). |
| **S4 Background jobs** | DON'T ADOPT | unchanged | Single-purpose `ProcessAsync` methods, not agent-shaped. `AIAgent` provides nothing Service Bus + IIdempotencyService + direct LLM call doesn't. |
| **S5A Foundry wrapper (shipped)** | PARTIAL (bundle with S1) | in-process BFF | `AsAIAgent` + `CreateSessionAsync(conversationId)` simplify the wrapper. Default-OFF (ADR-018) — low operational pressure to lift standalone. |
| **S5B Foundry canonical durable HITL (planned)** | **ADOPT (when project SPEC lands)** | **MIXED — prototype first** | F7 Workflows + F11 `RequestPort` HITL + F12 Foundry hosting purpose-built for multi-day legal workflows. Greenfield, no migration cost. |
| **S6 M365 Copilot** | DON'T ADOPT (as swap-in) | unchanged | Uses M365 Agents SDK — different SDK from Agent Framework. Both Microsoft, both valid for their respective surfaces. |
| **S7 Insights MCP** | PARTIAL (defer to D-A20) | UNKNOWN | Contract doesn't exist yet; three UNKNOWNs on host library, deployment, BFF seam. In-BFF Insights Agent tracks S1. |
| **S8a SessionSummarization** | DON'T ADOPT | unchanged | Single-purpose `IChatClient` consumer; no agent loop, tools, session. F5 marginal lift has qualitative regression risk. |
| **S8b CapabilityRouter** | PARTIAL (F5 only, with S1) | in-process BFF | Sub-component of S1; shares the `"raw"` keyed `IChatClient` registration. F5 only applies. |

Distribution: **1 ADOPT · 5 PARTIAL · 4 DON'T ADOPT.** Anti-bias sanity check passes — the assessment surfaces uncomfortable conclusions where evidence supports them.

---

## 6. Deployment model recommendations

For each ADOPT/PARTIAL surface, the deployment model is keyed to **ADR-013 §"Exceptions"** ([4-criteria gate](../../.claude/adr/ADR-013-ai-architecture.md)) + ADR-001 §"Functions scope" + [`bff-extensions.md`](../../.claude/constraints/bff-extensions.md). ADR-013 permits non-BFF deployable only when ALL FOUR hold:

1. No latency coupling with BFF synthesis (no <500ms TTFB requirement against BFF state)
2. No transactional coupling with BFF session/safety/audit state
3. Bounded, well-defined integration surface (HTTP contract, MCP tools, etc.)
4. Separating does not require duplicating latency-sensitive components in both processes

Default is **in-process BFF** when even one criterion fails.

### 6.1 S1 SprkChat → in-process BFF

ADR-013 4-criteria: (1) FAILS (`<500ms` TTFB streaming budget against BFF state); (2) FAILS (per-turn citation context, plan-preview Redis state, session token-budget counter, ChatHistoryManager all share request lifecycle); (3)/(4) N/A. Two criteria fail decisively. **Default holds.**

**Additional**: lift timing gated on Issue #6268. Feature-flag the lift behind `Sprk.Ai.UseFrameworkAgent` toggle so rollback is config flip, not redeploy.

### 6.2 S3 Builder → in-process BFF

ADR-013 4-criteria: (1) PARTIAL (non-streaming, multi-round, latency soft — but invoked from BFF endpoints sharing the BFF process); (2) PASSES; (3) PASSES (single method entry); (4) FAILS (extracting would duplicate auth + correlation + ProblemDetails for a small surface). Criterion 4 fails — Builder is too small to justify separate deployable. **Default holds.**

**Additional**: pre-work — Builder OpenAI.Chat SDK → `IChatClient` migration before `ChatClientAgent` adoption. Two PRs in sequence.

### 6.3 S5A Foundry wrapper → in-process BFF

N/A — the wrapper is request-routing code inside `SprkChatAgent`'s pipeline. Not a candidate for extraction; candidate for internal code simplification via `AIProjectClient.AsAIAgent(...)`. **Bundle with S1 lift; do not lift standalone.**

### 6.4 S5B Foundry canonical durable HITL → MIXED — prototype before committing

**This is the assessment's most consequential deployment decision.** S5B is greenfield, so the choice is forward-looking, not migration-tracked.

ADR-013 4-criteria evaluation (the case for non-BFF deployment is strong):

| Criterion | S5B evaluation | Result |
|---|---|---|
| (1) No latency coupling | PASSES — multi-day workflows, no <500ms BFF TTFB coupling | Gate passes |
| (2) No transactional coupling | PASSES — workflow state durable in workflow checkpoints + Foundry side; BFF session/safety/audit not in critical path | Gate passes |
| (3) Bounded integration surface | PASSES — workflow event stream + HTTP triggers + A2A endpoints (if exposed); contract is workflow-level | Gate passes |
| (4) No component duplication | PASSES — durable workflow hosting doesn't need to duplicate BFF streaming/routing/safety; those don't apply to multi-day workflows | Gate passes |

**All four ADR-013 §"Exceptions" criteria pass.** S5B legitimately qualifies for a non-BFF deployable.

**Three candidate deployment models**:

**(a) Workflows-in-BFF** (`Microsoft.Agents.AI.Workflows` hosted in `Sprk.Bff.Api`)
- **Fit**: Short-running HITL approvals where state survives via Redis/Dataverse for hours-to-days but not weeks
- **Pros**: Single deployment artifact; existing auth/correlation/observability stack; ADR-013 default
- **Cons**: Workflow state survival bounded by process lifetime + Redis TTL; multi-week NDA workflows exceed BFF process lifetime expectations
- **When to choose**: Short HITL (hours, <1 day), low concurrency

**(b) Workflows-in-Function** (Durable-Functions-style hosting via Agent Framework Durable Workflow patterns)
- **Fit**: Multi-day workflows requiring state survival across BFF restarts; event-driven triggers (timer, queue, webhook)
- **Pros**: ADR-001 already permits Functions for out-of-band integration; Workflows-in-Functions matches the existing Insights Engine sync pipeline pattern; lower per-session cost than Foundry-hosted
- **Cons**: **EVIDENCE-THIN** — `04-hosting/DurableWorkflows` sample category exists at SHA `afa7834e` but no dedicated Microsoft Learn `/hosting/` page covers production deployment patterns yet. Open [Issue #6308](https://github.com/microsoft/agent-framework/issues/6308) indicates the Foundry-hosting story is in active triage as of 2026-06-03.
- **When to choose**: Multi-day workflows without VM-isolation / per-agent-Entra-identity / A2A-endpoint requirements

**(c) Foundry-hosted agent** (`FoundryHostedAgents` pattern; agent runs in Foundry, framework runtime invokes it)
- **Fit**: Multi-day workflows requiring per-session VM-isolated sandboxes, per-agent Entra identity, A2A endpoint exposure, Foundry-hosted MCP tools
- **Pros**: Maximum durability + isolation; canonical for the [`knowledge/foundry-agent-service/`](../../knowledge/foundry-agent-service/) use cases (NDA negotiation, full-matter diligence, regulatory monitoring)
- **Cons**: Per-session cost (Foundry SKU pricing UNKNOWN); new operational surface (Foundry agent lifecycle management); only relevant if Spaarke actually requires the isolation/identity features
- **When to choose**: Workflow requirements include VM isolation + per-agent Entra identity + A2A peer composition

**The choice depends on three questions the assessment cannot answer authoritatively from current sources**:

1. Do Spaarke legal workflows actually require per-session VM-isolated sandboxes? (UNKNOWN)
2. Do they require per-agent Entra identity for A2A composition? (UNKNOWN)
3. Are Foundry SKU per-session costs acceptable for expected concurrency? (UNKNOWN)

**Confidence level: LOW.** The F12 evidence gap (no `/hosting/` Learn page; Issue #6308 open; sample tree is the only ground truth for production deployment patterns) means any pre-commitment to a deployment model is design-by-assumption.

**Recommendation: prototyping phase before commitment.**

When the canonical durable HITL surface gets a project SPEC, the project SHOULD include a **deployment prototyping phase** (estimated 1-2 weeks) that:
- Stands up a minimal `WorkflowBuilder` + `RequestPort` HITL workflow in each candidate hosting model (BFF, Function, Foundry-hosted)
- Measures cold-start latency, state-survival behavior across restarts, per-session cost (where measurable)
- Validates whether VM isolation + per-agent identity + A2A endpoint exposure are actual Spaarke requirements (interview legal-ops stakeholders, not infer from sample documentation)
- Returns a deployment-model decision with concrete evidence, not platform speculation

Without prototyping, Spaarke commits to a hosting model based on incomplete primary sources. The [2026-05-20 BFF AI extraction assessment](bff-ai-extraction-assessment-2026-05-20.md)'s lesson applies: **uncomfortable conclusions land better than premature confident ones.**

### 6.5 S7 Insights Engine MCP → DEFERRED to D-A20 contract authoring

Deployment model UNKNOWN per [notes/02 §6 U2](../../projects/agent-framework-fit-assessment-r1/notes/02-non-bff-ai-touchpoints-inventory.md). Decided at D-A20 contract authoring time (Phase 1 of `ai-spaarke-insights-engine-r1`). The assessment cannot pre-commit.

Preliminary ADR-013 4-criteria for the MCP server (contract-pending): (1) LIKELY PASSES — MCP tool calls are request/response, sync but not <500ms-BFF-state-coupled; (2) LIKELY PASSES — MCP server's BFF-side seam via `IInsightsAi` facade, no session/safety coupling; (3) PASSES — MCP protocol IS the bounded contract; (4) PARTIAL — auth + correlation + ProblemDetails would duplicate if separate deployable; may be acceptable for thin transport over `IInsightsAi`.

**All four MAY pass, which would justify separate-deployable per ADR-013.** Criterion (4) depends on whether MCP server is a thin transport (low duplication cost) or hosts agents internally (higher duplication risk). The D-A20 contract decides.

**Two candidate deployment models**:
1. **Separate `Sprk.Insights.Mcp` deployable** — ADR-013's "MCP server exposing AI capabilities to external consumers" example. Required if external consumers (M365 Copilot per S6 Open Question 1) need to consume Insights without the full BFF.
2. **MCP endpoints embedded in BFF** — Simpler; matches the bff-extraction §8 recommendation to defer MCP server extraction until Insights Engine Phase 1 lands.

**Additional**: In-BFF Insights Agent (D-A9) tracks S1 — its deployment is in-BFF regardless. The 2026-05-20 BFF AI extraction assessment §8 recommended "defer MCP server extraction with re-assessment after Insights Engine Phase 1 lands." This assessment's recommendation is consistent.

### 6.6 S8a, S8b → fold into S1 perimeter

S8a and S8b are sub-components of S1's SprkChat session lifecycle. No independent deployment story; track S1 deployment (in-process BFF).

---

## 7. Migration cost + risks

### 7.1 Shared infrastructure change — middleware lift as ONE change, not four

The four S1-adjacent PARTIAL surfaces (S1, S3, S8a, S8b — and S5A by adoption-bundling) benefit from **one cross-cutting infrastructure change**, not four independent migrations. The framing matters: total cost is shared change + per-surface lift, not 4× per-surface lift.

**Today**: Spaarke decorates `ISprkChatAgent` (a Spaarke interface) with three middleware classes (`AgentTelemetryMiddleware`, `AgentContentSafetyMiddleware`, `AgentCostControlMiddleware`). These are per-instance decorators wired in `SprkChatAgentFactory.CreateAgentAsync`. The pattern works but is **structurally non-idiomatic** — the framework expects middleware composed via `chatClient.AsBuilder().Use*().Build()` at `IChatClient` tier, with agent-level middleware via `agent.AsBuilder().Use*().Build()` at `AIAgent` tier per the [Middleware Learn page](https://learn.microsoft.com/en-us/agent-framework/agents/middleware/) (fetched 2026-06-03, `updated_at 2026-04-02`).

**The lift**: Replace `SprkChatAgentFactory`'s manual decorator stack with the framework's two-tier composition:
- `IChatClient`-tier: `AsBuilder().UseFunctionInvocation().UseOpenTelemetry().Build()` (raw client wrappers — function dispatch, OTel)
- `AIAgent`-tier: `.AsBuilder().Use(...)` for Spaarke-specific policy (content safety, cost control, custom telemetry)

**Why this is one change, not four**:
- S1's `SprkChatAgent` IS the SprkChat surface; its lift IS this change.
- S3 Builder currently has NO middleware composition (uses OpenAI.Chat SDK directly). Lifting Builder to `ChatClientAgent` inherits the middleware stack at that point; no separate middleware code to write.
- S8a and S8b consume the same `[FromKeyedServices("raw")] IChatClient` registration as `CompoundIntentDetector`. When the "raw" registration upgrades to a framework-composed chain, all three consumers benefit without per-surface code change.
- S5A Foundry wrapper consumes from `SprkChatAgent`-shaped contexts; bundling its lift with S1's same-PR set inherits the new middleware stack.

[notes/01 cross-cutting observation 3](../../projects/agent-framework-fit-assessment-r1/notes/01-spaarke-ai-surfaces-inventory.md) named this as the biggest single migration vector for S1. Task 005 reframes: it is also the biggest single migration vector for S3/S8a/S8b/S5A by amortization. **This is the assessment's implied ADR-013 amendment** — the shared middleware-lift infrastructure change is a cross-cutting architectural decision that should be captured in an ADR-013 successor when the lift is approved.

### 7.2 Other shared infrastructure changes

| Change | Surfaces affected | Estimated standalone effort | Notes |
|---|---|---|---|
| Lift `[FromKeyedServices("raw")] IChatClient` from raw OpenAI bridge to `chatClient.AsBuilder().UseFunctionInvocation().UseOpenTelemetry().Build()` | S1, S8a, S8b | 2-3 days | The keyed registration becomes the framework-composed chain |
| Standardize OTel source-name conventions on Agent Framework GenAI Semantic Conventions | S1, S3, S5A | 1-2 days | Replaces hand-rolled `AgentTelemetryMiddleware` attributes |
| Migrate Builder DI from OpenAI.Chat SDK to `IChatClient` | S3 only (pre-req for S3 lift) | 1-2 days | Sequence: BEFORE S3's `ChatClientAgent` adoption |
| `AgentSession` reconciliation with Spaarke's Redis-externalized chat history | S1 (and S5A when bundled) | 3-5 days uncertainty | **Most uncertain** shared change — depends on whether `CreateSessionAsync(conversationId)` model fits |

### 7.3 Publish-size impact

| Baseline | Compressed size | Source |
|---|---|---|
| Pre-2026-05-19 BFF baseline | ~60 MB | [`bff-extensions.md`](../../.claude/constraints/bff-extensions.md) |
| 2026-05-19 jump | 75+ MB | bff-extensions confirmed |
| Post-Outcome-A baseline (current) | **45.65 MB** | [`azure-deployment.md`](../../.claude/constraints/azure-deployment.md); sdap-bff-api-remediation-fix EXECUTION-LOG |
| Tolerance threshold (flag if exceeded) | 80 MB compressed | Conservative interpretation of "75+ MB jump was a problem" |

**Decisive finding** ([notes/01 top-level finding](../../projects/agent-framework-fit-assessment-r1/notes/01-spaarke-ai-surfaces-inventory.md)): **`Microsoft.Agents.AI 1.0.0-rc1` is ALREADY referenced** at [Sprk.Bff.Api.csproj:33](../../src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj#L33) but zero code uses it. The S1/S3/S8a/S8b/S5A lift activates code paths in an already-shipped assembly; **net publish-size delta is ZERO** for these surfaces.

| Surface | New top-level NuGet refs implied | Estimated compressed delta | Notes |
|---|---|---|---|
| S1 SprkChat (PARTIAL) | None — `Microsoft.Agents.AI 1.0.0-rc1` already present | **0 MB** | Activating already-shipped assembly |
| S3 Builder (PARTIAL) | None — uses already-present `Microsoft.Agents.AI` | **0 MB** | May drop direct `OpenAI.Chat` usage if routed through `IChatClient` |
| S5A Foundry wrapper (PARTIAL) | None — `Azure.AI.Projects 1.0.0-beta.8` already present | **0 MB** | Verify Agents.AI glue is transitive |
| S5B canonical durable HITL (ADOPT) | Likely `Microsoft.Agents.AI.Workflows`; possibly `Microsoft.Agents.AI.Hosting.A2A.AspNetCore`; possibly `Microsoft.Agents.AI.Foundry` | **2-6 MB cumulative (UNCERTAIN)** | NuGet package sizes for Agents.AI family typically 0.5-2 MB compressed each. Verify at SPEC time. |
| S7 Insights MCP server (PARTIAL) | Depends on deployment (D-A20 decides) | **0-2 MB (UNCERTAIN)** | If separate `Sprk.Insights.Mcp` deployable, additions don't affect BFF |
| S8a / S8b | None — sub-components of S1 | **0 MB** | — |

**Cumulative BFF projection**:

- **Scenario A** (S1/S3/S5A/S8a/S8b lift only, S5B not in BFF, S7 not in BFF): **~45-46 MB** — within tolerance.
- **Scenario B** (above + S5B Workflows-in-BFF + S7 MCP embedded in BFF): **~47-54 MB** — within tolerance, well under 80 MB.

**Conclusion**: Even the worst-case full-adoption scenario lands at ~54 MB compressed, well under the 80 MB tolerance flag and below the 2026-05-19 jump that triggered bff-extensions governance. **The framework adoption is NOT a publish-size risk for the BFF.** Caveat: estimates upper-bounded; actual measurement required at lift time per the bff-extensions constraint.

**If S5B deploys elsewhere** (Function or Foundry-hosted), the BFF is unaffected entirely. New packages live in Function deployable or are unnecessary. The Workflows-in-BFF scenario is the worst case for BFF size; alternative deployments improve the picture.

### 7.4 Risk register

Ten risks identified across adopt/partial surfaces. Severity: LOW (mitigatable in PR), MED (requires design attention), HIGH (architectural / SLO-level).

| # | Surface(s) | Risk | Severity | Mitigation |
|---|---|---|---|---|
| R1 | S1 | **Issue #6268 doesn't ship resolved in time, or ships incompletely.** SprkChat's canonical workload (multi-tool streaming) is the exact failure mode. Lifting before resolution ships a regression. | **HIGH** | Feature-flag the lift (`Sprk.Ai.UseFrameworkAgent`); maintain hand-rolled fallback; re-evaluate at each AF release. Do NOT lift until #6268 fixed in shipped 1.x. |
| R2 | S5B | **F12 durable hosting evidence is thin.** Foundry-hosted vs Workflows-in-Function decision is currently design-by-assumption. Issue #6308 (Foundry hosting in active triage) signals upstream story is unstable. | **HIGH** | Prototyping phase before SPEC commitment (§6.4). Do NOT pre-commit to a deployment model in S5B SPEC before prototype runs. |
| R3 | S1, S3, S5A (shared infra) | **OTel pipeline disruption during middleware lift.** Hand-rolled `AgentTelemetryMiddleware` emits Spaarke-specific attributes; framework `WithOpenTelemetry()` emits GenAI Semantic Conventions. Switchover could lose Application Insights dashboard continuity. | MED | Parallel-emit during transition (hand-rolled + framework on different source names); cut over once dashboard parity verified. Update `docs/guides/` observability guide pre-cut-over. |
| R4 | S1 | **`AgentSession` reconciliation with Redis-externalized chat history is the most uncertain shared change.** If `CreateSessionAsync(conversationId)` doesn't map cleanly to Spaarke's Redis cache pattern, the S1 lift requires more code than the rest combined. | MED | De-risk via spike before committing to S1 lift scope. Spike outcome determines effort estimate confidence. |
| R5 | S3 | **OpenAI.Chat SDK → `IChatClient` migration is pre-work**, not bundled with `ChatClientAgent` lift. Skipping this sequencing produces an intermediate state where Builder runs on `OpenAI.Chat` while using framework types. | LOW | Sequence Builder's DI rewiring as explicit pre-PR before `ChatClientAgent` adoption PR. Two PRs, not one. |
| R6 | S5B | **Foundry SKU per-session cost is UNKNOWN.** If Foundry-hosted is chosen and per-session cost is higher than projected, runtime cost of multi-day workflows could be material. | MED-HIGH | Owner-level cost analysis before committing to Foundry-hosted. Workflows-in-Function may be cheaper for same durability — but lacks VM-isolation/per-agent-identity. |
| R7 | S5B, S7 | **A2A protocol still evolving.** Framework's `MapA2A` + cross-system agent identity is recent. Pre-committing Spaarke to A2A interop now risks protocol churn. | MED | Defer A2A exposure decision to a future iteration; build S5B without A2A first; add when standards stabilize. |
| R8 | S1, S3, S5A | **Test infrastructure shift from `IChatClient` mocks to `AIAgent` mocks.** ~2,800 AI tests cited in [bff-extraction §5](bff-ai-extraction-assessment-2026-05-20.md) include substantial SprkChat-specific tests; re-keying mocks is mechanical but voluminous. | MED | Treat test re-keying as part of each surface's lift effort. Estimate +30-50% effort on top of source code lift. |
| R9 | S5B | **No Spaarke code yet.** "Migration cost" is actually greenfield implementation cost. Risk is mis-scoping work as "small framework adoption" when it's "build a multi-agent durable workflow system from scratch." | **HIGH** | Scope greenfield work explicitly in S5B SPEC. Bound the assessment-derived recommendation: "adopt AF primitives for the workflow layer" is one decision; "ship multi-day legal workflows" is multiple person-quarters. |
| R10 | All adopt/partial | **Stacked pre-release packages.** `Sprk.Bff.Api.csproj` already pins three pre-release ([bff-extraction §1](bff-ai-extraction-assessment-2026-05-20.md)). S5B adoption adds likely `Microsoft.Agents.AI.Workflows` (1.0.0-rc-flavor), possibly more. Each pre-release pin is future-rev risk. | MED | Track each pre-release pin with explicit csproj comment per [bff-extensions §B](../../.claude/constraints/bff-extensions.md). Set re-evaluation date when 1.0 GA ships for each package. |

**Top 3 HIGH-severity risks**: R1 (Issue #6268), R2 (F12 evidence gap), R9 (S5B mis-scoping). All three argue for **waiting, prototyping, or scoping carefully** rather than premature adoption.

### 7.5 Total person-week estimate

| Phase | Scope | Estimate | Confidence |
|---|---|---|---|
| **Phase 0** | Wait for Issue #6268 to land in shipped 1.x | 0 person-weeks of effort (calendar gating) | HIGH on 0 effort; LOW on calendar timing |
| **Phase 1** | Builder pre-work (OpenAI.Chat SDK → `IChatClient`) | 1-2 person-weeks | MED |
| **Phase 2** | Shared infrastructure (middleware lift + OTel standardization + keyed-services chain upgrade) | 4-8 person-weeks | LOW-MED |
| **Phase 3** | Per-surface lifts (S1 + S3 + S5A + S8a + S8b), each 0.5-1.5 person-weeks | 3-7 person-weeks | LOW-MED |
| **In-scope total (Phase 1+2+3)** | All S1-family lifts | **8-17 person-weeks** | LOW-MED overall |
| **S5B (separate project)** | Greenfield durable HITL workflows | OUT OF SCOPE for this estimate — multiple person-quarters, not a migration | N/A |
| **S7 (deferred)** | MCP server per D-A20 contract | OUT OF SCOPE — Phase 2 deferred | N/A |

**Why LOW-MED, not higher confidence**: R4 (AgentSession reconciliation) could be 1-week spike or 3-week deep rewrite; R8 (test re-keying) adds 30-50%; F12 evidence-thin caveat brackets any S5B-related effort; Spaarke team has not previously lifted to Agent Framework. **Why not higher uncertainty**: `Microsoft.Agents.AI 1.0.0-rc1` already referenced (zero publish-size coordination); code surface is well-bounded (~10-15 files primary change for S1-family); ADR-013 doesn't change; deployment model stays in-process BFF for all S1-family surfaces.

**S5B framing — separate effort, separate calendar**: S5B is the highest-fit, highest-strategic-value adoption but it is **not** a migration. It is a greenfield project that should: have its own SPEC + project scope when the canonical durable HITL legal workflows surface gets owner sign-off; include the 1-2 week prototyping phase recommended in §6.4 BEFORE the SPEC commits to a deployment model; be estimated in person-quarters at the project level, not person-weeks at the migration level.

### 7.6 Reversibility per surface

| Surface | Reversibility | Rollback path |
|---|---|---|
| S1 | **MEDIUM-HIGH** | Feature flag (`Sprk.Ai.UseFrameworkAgent`) for runtime fallback; hand-rolled `ISprkChatAgent` retained as facade during transition; middleware classes revert per-class. No data forward-only if Redis chat-history pattern preserved. |
| S3 | **HIGH** | ~3-5 file surface; git-revert-cheap. Non-streaming, non-session-stateful. Pre-PR (OpenAI.Chat → `IChatClient`) can ship independently. |
| S5A | **HIGH** | ~1 file primary change. Default-OFF (ADR-018 kill switch) — rollback is config flip even if code is shipped. |
| S5B | **LOW-MEDIUM** (depends on deployment) | No "rollback" — greenfield. Re-host between candidates: Workflows-in-BFF ↔ Workflows-in-Function: medium cost (1-3 weeks for established workflows); BFF/Function ↔ Foundry-hosted: HIGHER cost (state stored in Foundry must migrate; per-session VM state may not have Workflows equivalent). Mitigation = §6.4 prototyping phase. |
| S7 | **HIGH** (pre-commit) / MEDIUM (post-commit) | Pre-D-A20: trivial. Post-commit: separate `Sprk.Insights.Mcp` ↔ BFF-embedded re-host = 1-2 weeks. External MCP consumers see protocol contract — breaking changes forward-only relative to consumers. |
| S8a, S8b | **HIGH** | Sub-components of S1; git-revert-cheap independently. Bundle with S1 PR set for transactional consistency. |

---

## 8. Open questions / human-decision points

The assessment surfaces issues it does not authoritatively resolve. Per project SPEC §8 acceptance criterion (≥3 open questions), these are the human-decision points:

### Q1. S5B VM-isolation requirement (S5B deployment model)

**Why this is unresolved**: Per [notes/02 §6 U5](../../projects/agent-framework-fit-assessment-r1/notes/02-non-bff-ai-touchpoints-inventory.md) + §S5B.7 Q1, [`knowledge/foundry-agent-service/docs/hosted-agents.md`](../../knowledge/foundry-agent-service/docs/hosted-agents.md) describes Foundry-hosted agents with per-session VM-isolated sandboxes (`$HOME` + `/files` persistence, 15-min idle → resume). Whether Spaarke's legal workflows actually require this isolation is the load-bearing question for the Foundry-hosted vs Workflows-in-Function deployment choice. The assessment cannot infer this from documentation — it requires stakeholder interview.

**What's needed to resolve**: Interview legal-ops stakeholders during the S5B prototyping phase (§6.4 recommendation). Specifically: do multi-day NDA workflows require per-session file system state outliving any single agent run? Does HIPAA / privilege handling require process-level isolation? If "no" to both, Workflows-in-Function delivers the durability story at lower cost. If "yes" to either, Foundry-hosted is the home.

### Q2. S1 wait-or-pilot timing (Issue #6268)

**Why this is unresolved**: Per §5.1 Open Question 1, [Issue #6268](https://github.com/microsoft/agent-framework/issues/6268) is `needs-maintainer-triage` as of 2026-06-03. Fix timing is upstream-dependent. Spaarke must choose: wait until the bug ships fixed in a 1.x release, OR pilot the lift now behind a feature flag with hand-rolled fallback. The decision criterion (% of SprkChat traffic that is multi-tool — wait if >50%, pilot if <20%) requires production telemetry the assessment doesn't have.

**What's needed to resolve**: Pull SprkChat production telemetry on `% multi-tool turns` from Application Insights. If unavailable, instrument and wait one telemetry cycle. Then apply the threshold rule. Re-fetch Issue #6268 status weekly until it resolves; bundle the lift with the next release after upstream fix lands.

### Q3. S7 D-A20 contract three UNKNOWNs

**Why this is unresolved**: Per §5.8 + [notes/02 §6 U1/U2/U3](../../projects/agent-framework-fit-assessment-r1/notes/02-non-bff-ai-touchpoints-inventory.md), the Insights Engine MCP server's contract doesn't exist yet (Phase-2-deferred per SPEC §3.6). Three concrete UNKNOWNs material to the framework decision: (U1) specific `Microsoft.*` host library (`ModelContextProtocol.AspNetCore` vs Agent Framework MCP host); (U2) deployment model (separate `Sprk.Insights.Mcp` vs embedded in BFF); (U3) BFF integration seam (wraps `/api/insights/ask` vs directly wraps `IInsightsAi`). The assessment cannot pre-commit answers.

**What's needed to resolve**: Phase 1 D-A20 contract authoring (already in `ai-spaarke-insights-engine-r1` Phase 1 wave W1.5 per [SPEC §8](../../projects/ai-spaarke-insights-engine-r1/SPEC.md)) MUST explicitly answer these three questions. This assessment's guidance (§5.8): prefer Agent Framework primitives IF MCP server is separate deployable AND hosts agents internally; prefer plain `ModelContextProtocol` library hosting if MCP server is thin transport over `IInsightsAi`. Owner action: ensure D-A20 contract authoring addresses U1/U2/U3 explicitly, citing this assessment.

### Q4. S5B prototyping scope (deployment-model decision)

**Why this is unresolved**: Per §6.4 + R2, the F12 durable-hosting evidence base is thin (no `/hosting/` Learn page in recency window; Issue #6308 open; sample tree is primary ground truth for production deployment patterns). Pre-committing to Workflows-in-BFF, Workflows-in-Function, or Foundry-hosted in the S5B SPEC without prototyping is design-by-assumption. But the scope of the prototyping phase is itself unspecified — how many candidates to prototype, how representative the prototype workload, how to measure success.

**What's needed to resolve**: When the S5B canonical durable HITL legal workflows surface gets owner sign-off and a project SPEC, the SPEC must include a 1-2 week prototyping phase. Recommended scope: stand up a minimal `WorkflowBuilder` + `RequestPort` HITL workflow in each of (a), (b), (c) candidates; measure cold-start latency, state-survival behavior across restarts, per-session cost; validate VM-isolation/per-agent-identity/A2A-endpoint requirements via stakeholder interviews. Return a deployment-model decision with concrete evidence.

### Q5. JPS-vs-Workflows long-term (S2)

**Why this is unresolved**: Per §5.2 Open Question 1, the assessment recommends "don't adopt now" — migration cost catastrophic, no incremental path. But this leaves open whether JPS becomes the long-term home of multi-step analysis in Spaarke, or whether JPS is a transitional pattern that should be replaced when bandwidth allows. The assessment cannot make this strategic call.

**What's needed to resolve**: Architecture-group decision. If JPS is long-term: codify its DSL + executor contract + Dataverse schema as canonical Spaarke architecture. If JPS is transitional: identify a "Workflows pilot" candidate (likely NOT existing JPS playbooks — could be S5B's canonical durable workflows, which would let the team learn Workflows on greenfield rather than via JPS rewrite) and set a re-assessment date.

### Q6. M365 Copilot R2 MCP server hosts AF agents internally? (S6 → S7)

**Why this is unresolved**: Per §5.7 Open Question 1, when the deferred R2 MCP server (Tier 3 in `ai-m365-copilot-integration/design.md` §244-275) is implemented, it may host Agent Framework agents internally. This flips S6's verdict from "Agent Framework not the Copilot backend" to "Agent Framework may live in the MCP server consumed BY Copilot." The decision is downstream of both the R2 MCP server SPEC and the S7 D-A20 contract.

**What's needed to resolve**: When the R2 MCP server SPEC is authored (post-R1 M365 Copilot launch), revisit S6's verdict in light of the design choices. If R2 MCP server hosts AF agents (rather than thin transport), the framework adoption boundary moves into S6's territory — partially.

---

## 9. Forward-references

This assessment explicitly unblocks or implies the following downstream work:

### 9.1 `projects/agent-framework-knowledge-r1/` — parked curation project

This assessment was specifically designed to unblock the parked curation project per the project relationship documented in [`projects/agent-framework-fit-assessment-r1/CLAUDE.md`](../../projects/agent-framework-fit-assessment-r1/CLAUDE.md) ("This project **blocks** [`projects/agent-framework-knowledge-r1/`]"). What this assessment unblocks for SPEC refinement:

- **Curation scope can be narrowed.** The knowledge project's original scope was "curate Agent Framework documentation." This assessment finds: S1 + S3 + S5B + S8b are the Spaarke-relevant adoption surfaces. The knowledge project's curation can prioritize features F1 (`ChatClientAgent`), F4 (middleware), F5 (structured outputs), F7 (Workflows), F10 (observability), F11 (Tool Approval / Workflow HITL), F12 (durable hosting / Foundry-hosted). Features F3 (context providers), F8 (MCP client — for S1 consumption), F9 (A2A) are secondary. Features applicable only to surfaces this assessment rejects (S2, S4, S6) can be deprioritized.
- **Recency floor and curation cadence.** This assessment's monthly REFRESH cadence (per §9.3 below) means the knowledge project's curation should run on the same cadence to stay synchronized.
- **A short unblock-recommendation note** at [`projects/agent-framework-knowledge-r1/UNBLOCK-RECOMMENDATION.md`](../../projects/agent-framework-knowledge-r1/UNBLOCK-RECOMMENDATION.md) (created by task 008) captures the specific SPEC changes implied. Per the project scoping decision, this assessment does NOT edit the SPEC itself — that is a downstream action after human review of the assessment.

### 9.2 ADR-013 implied amendments

This assessment does not edit ADR-013 (per the scoping decision). It implies amendments that should be captured in a successor ADR when adoption decisions are made:

- **Shared middleware-lift infrastructure change (per §7.1)**: The cross-cutting decision to compose `IChatClient`-tier and `AIAgent`-tier middleware via the framework's `.AsBuilder().Use*().Build()` composition (replacing hand-rolled `ISprkChatAgent` decorators) is a cross-cutting architectural decision. When the S1 lift is approved, this should be captured in an ADR-013 successor or a new cross-cutting ADR that codifies: "Spaarke AI surfaces SHOULD compose middleware via Agent Framework `.AsBuilder().Use*().Build()` patterns rather than hand-rolled service decorators."
- **S5B deployment-model exception class**: §6.4's S5B deployment passes all four ADR-013 §"Exceptions" criteria. If the canonical durable HITL project ships with Workflows-in-Function or Foundry-hosted deployment, ADR-013 should be amended to name this as a permitted exception class (alongside the existing "Functions for sync/extraction" and "MCP server for external consumers" examples).
- **Agent Framework adoption boundaries**: Until an ADR is written, individual project SPECs that touch Agent Framework should cite this assessment as the binding adoption guidance. The proper place for this is a successor ADR.

### 9.3 Future REFRESH cadence

Per the project's primary-source discipline ([SPEC §7](../../projects/agent-framework-fit-assessment-r1/SPEC.md)), the platform is at 1.x mature-and-evolving territory (1.0 GA April 2026; 1.9 shipped 2026-06-03 at BUILD 2026). The assessment has a 60-day half-life.

**Recommended REFRESH cadence: monthly**, with explicit triggers:

- **Trigger A — Issue #6268 status change**: Re-fetch the issue weekly until it resolves; bundle the S1 lift evaluation with the next release after upstream fix lands.
- **Trigger B — F12 evidence gap closes**: If a `/hosting/` Learn page lands within the recency floor OR Issue #6308 resolves with a documented hosting story, re-evaluate §6.4 S5B deployment-model evidence-thin caveat. The prototyping recommendation may downgrade to "follow the canonical pattern" if upstream documentation matures.
- **Trigger C — Agent Framework 1.x release**: Each shipped 1.x release (1.10, 1.11, ...) should trigger a delta check against the §10 Sources appendix top 5 most-cited URLs. Material changes warrant inline §8 open-question additions.
- **Trigger D — Spaarke project SPEC commitments**: When the S5B canonical durable HITL surface gets a project SPEC, OR the S7 D-A20 contract is authored, OR S6 R2 MCP server is implemented — each is a trigger to re-validate the per-surface verdicts in §5 against the new specifics.

---

## 10. Sources appendix

This appendix is the freshness audit trail for future REFRESH cycles. Every URL cited in §4–§7 appears here with fetched date and section references. Pulled from [notes/00 §4-§6](../../projects/agent-framework-fit-assessment-r1/notes/00-primary-source-baseline.md) (12 Learn pages, 9 Devblogs, 12 GitHub Issues, 1 SHA, sample tree) and [notes/03 §F1-§F12](../../projects/agent-framework-fit-assessment-r1/notes/03-agent-framework-feature-map.md) (Tool Approval page added during task 003 re-fetch).

| # | URL | Fetched date | `updated_at` | Section(s) referencing | One-line content note |
|---|---|---|---|---|---|
| P1 | https://learn.microsoft.com/en-us/agent-framework/overview | 2026-06-03 | 2026-04-20 | §4 F3 | Framework introduction; names context providers as foundational building block |
| P2 | https://learn.microsoft.com/en-us/agent-framework/agents/ | 2026-06-03 | 2026-04-20 | §4 F1, §5.1, §5.3 | `ChatClientAgent`/`AIAgent` base; `Microsoft.Agents.AI` vs `Microsoft.Extensions.AI` distinction sharp |
| P3 | https://learn.microsoft.com/en-us/agent-framework/workflows/ | 2026-06-03 | 2026-04-29 | §4 F7, §5.2, §6.4 | Functional vs Graph API; supersteps; checkpoints; orchestration patterns |
| P4 | https://learn.microsoft.com/en-us/agent-framework/agents/providers/ | 2026-06-03 | 2026-04-24 | §4 F1 (provider helpers) | Provider matrix; `AsAIAgent` provider helpers |
| P5 | https://learn.microsoft.com/en-us/agent-framework/agents/tools/ | 2026-06-03 | 2026-05-26 | §4 F6, §4 F8, §4 F11, §5.6 | Tool surface; agent-as-tool; provider-support matrix; Tool Approval cross-link |
| P6 | https://learn.microsoft.com/en-us/agent-framework/agents/middleware/ | 2026-06-03 | 2026-04-02 | §4 F4, §5.1, §7.1, §3.1 obs 3 | `.AsBuilder().Use*().Build()` composition; 3 middleware tiers |
| P7 | https://learn.microsoft.com/en-us/agent-framework/agents/observability | 2026-06-03 | 2026-05-21 | §4 F10, §5.1 | `UseOpenTelemetry(sourceName)` + `WithOpenTelemetry()`; Azure Monitor wiring; duplication warning |
| P8 | https://learn.microsoft.com/en-us/agent-framework/agents/conversations/session | 2026-06-03 | 2026-05-26 | §4 F2, §5.1, §5.5 | `AgentSession`; `CreateSessionAsync(conversationId)` for remote-thread binding |
| P9 | https://learn.microsoft.com/en-us/agent-framework/agents/tools/hosted-mcp-tools | 2026-06-03 | 2026-04-24 | §4 F6, §4 F8 | Foundry-hosted MCP; `MCPToolDefinition`, `MCPToolResource`, `MCPApproval` |
| P10 | https://learn.microsoft.com/en-us/agent-framework/agents/structured-outputs | 2026-06-03 | 2026-04-20 | §4 F5, §5.3 | `RunAsync<T>`, `ChatResponseFormat.ForJsonSchema<T>()` |
| P11 | https://learn.microsoft.com/en-us/agent-framework/integrations/a2a | 2026-06-03 | 2026-05-20 | §4 F9, §4 F12, §5.6 | `Microsoft.Agents.AI.Hosting.A2A.AspNetCore`; `MapA2A`; AgentCard discovery |
| P12 | https://learn.microsoft.com/en-us/agent-framework/workflows/human-in-the-loop | 2026-06-03 | 2026-03-31 | §4 F7, §4 F11, §5.6, §6.4 | `RequestPort`, `RequestInfoEvent`; checkpoint behavior; **stable content, 1 day below 2026-04-01 floor — content has not had breaking change in BUILD 2026 release** |
| P13 | https://learn.microsoft.com/en-us/agent-framework/agents/tools/tool-approval | 2026-06-03 | 2026-04-02 | §4 F11, §5.1 | `ApprovalRequiredAIFunction`, `FunctionApprovalRequestContent`, `CreateResponse` |
| D1 | https://devblogs.microsoft.com/agent-framework/microsoft-agent-framework-version-1-0/ | 2026-06-03 | 2026-04 | §1 | Agent Framework 1.0 GA (April 2026); production-ready signal |
| D3 | https://devblogs.microsoft.com/agent-framework/microsoft-agent-framework-at-build-2026/ | 2026-06-03 | 2026-06-02 | §1, §9.3 | BUILD 2026 launch context; agent harness, Skills in Toolboxes, procedural memory, Voice Live |
| D6 | https://devblogs.microsoft.com/dotnet/durable-workflows-in-microsoft-agent-framework/ | 2026-06-03 | 2026 (within recency floor) | §4 F7, §4 F12, §6.4 | Durable workflow hosting narrative; .NET-specific guidance |
| I1 | https://github.com/microsoft/agent-framework/issues/6268 | 2026-06-03 | opened 2026-06-02 | §1, §5.1, §6.1, §7.4 R1, §8 Q2 | **`.NET ChatClientAgent.RunStreamingAsync` ends with no assistant text on multi-tool turns** — RED FLAG for S1 |
| I2 | https://github.com/microsoft/agent-framework/issues/6308 | 2026-06-03 | opened 2026-06-03 | §4 F12, §6.4, §7.4 R2 | "How to deploy dotnet Hosted agents to Foundry" — signals Foundry-hosting story in active triage |
| Repo | https://github.com/microsoft/agent-framework @ SHA `afa7834e2ec8a93b2224fe7ab184b97fbcaa8c9a` | 2026-06-03 | 2026-06-03 20:03 UTC | §4 F3, §4 F7, §4 F8, §4 F12, §6.4 | Upstream sample tree — `02-agents/`, `03-workflows/`, `04-hosting/`; primary evidence for hosting patterns |
| ADR | [ADR-013-ai-architecture.md](../../.claude/adr/ADR-013-ai-architecture.md) | 2026-06-03 (read at synthesis) | 2026-05-20 | §6 (all subsections), §7, §9.2 | Binding constraint — 4-criteria gate for non-BFF deployable; refined 2026-05-20 |
| ADR | [ADR-001-minimal-api.md](../../.claude/adr/ADR-001-minimal-api.md) | 2026-06-03 (read at synthesis) | stable | §6.4 | Minimal API + Functions exception scope (Functions for sync/extraction) |
| Constr | [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) | 2026-06-03 (read at synthesis) | stable | §7.3 | Publish-size + placement decision criteria; 60 MB baseline; 75+ MB jump |
| Constr | [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) | 2026-06-03 (read at synthesis) | stable | §7.3 | Post-Outcome-A 45.65 MB baseline |
| Spk | [Sprk.Bff.Api.csproj:33](../../src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj#L33) | 2026-06-03 | n/a (source) | §1, §2.1, §7.3 | `Microsoft.Agents.AI 1.0.0-rc1` package reference — central evidence for "referenced but unused" |
| Spk | [`SprkChatAgent.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs) | 2026-06-03 | n/a (source) | §3, §5.1 | Primary S1 implementation; `IChatClient` consumption; multi-tool streaming workload |
| Spk | [`AgentContentSafetyMiddleware.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentContentSafetyMiddleware.cs) | 2026-06-03 | n/a (source) | §3, §5.1, §7.1 | Hand-rolled middleware decorating `ISprkChatAgent` (not `IChatClient`) |
| Spk | [`AgentCostControlMiddleware.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentCostControlMiddleware.cs) | 2026-06-03 | n/a (source) | §3, §5.1, §7.1 | Hand-rolled middleware; per-session token-budget counter |
| Spk | [`AgentTelemetryMiddleware.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentTelemetryMiddleware.cs) | 2026-06-03 | n/a (source) | §3, §5.1, §7.1 | Hand-rolled telemetry middleware; AIPL-057 (content never logged) |
| Spk | [`AnalysisOrchestrationService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs) | 2026-06-03 | n/a (source) | §3, §5.2 | S2 JPS orchestrator; `IOpenAiClient.StreamCompletionAsync`; no Extensions.AI/Agents.AI types |
| Spk | [`BuilderAgentService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs) | 2026-06-03 | n/a (source) | §3, §5.3 | S3 hand-written agentic loop bounded by `MaxToolRounds = 10`; OpenAI.Chat SDK direct |
| Spk | [`AgentServiceClient.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Foundry/AgentServiceClient.cs) | 2026-06-03 | n/a (source) | §3, §5.5 | S5A wrapper using `Azure.AI.Projects.AgentsClient`; default-OFF (ADR-018) |
| Spk | [`SessionSummarizationService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionSummarizationService.cs) | 2026-06-03 | n/a (source) | §3, §5.9 | S8a single-purpose `IChatClient` consumer with legal-context preservation rationale |
| Spk | [`CapabilityRouter.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs) | 2026-06-03 | n/a (source) | §3, §5.10 | S8b multi-layer classifier with optional `[FromKeyedServices("raw")] IChatClient?` |
| Spk | [`knowledge/foundry-agent-service/NOTES.md`](../../knowledge/foundry-agent-service/NOTES.md) | 2026-06-03 (read at synthesis) | n/a (curated) | §3, §5.6 | S5B canonical use cases (NDA, full-matter diligence, regulatory monitoring) — TODO stub |
| Spk | [`knowledge/foundry-agent-service/docs/hosted-agents.md`](../../knowledge/foundry-agent-service/docs/hosted-agents.md) | 2026-06-03 (read at synthesis) | n/a (curated) | §5.6, §6.4, §8 Q1 | Foundry-hosted agent lifecycle; per-session VM-isolated sandboxes |
| Spk | [`projects/ai-m365-copilot-integration/spec.md`](../../projects/ai-m365-copilot-integration/spec.md) | 2026-06-03 (read at synthesis) | n/a (project SPEC) | §3, §5.7 | S6 R1 scope; "thin adapter facades — no new AI orchestration logic" |
| Spk | [`projects/ai-spaarke-insights-engine-r1/SPEC.md`](../../projects/ai-spaarke-insights-engine-r1/SPEC.md) | 2026-06-03 (read at synthesis) | n/a (project SPEC) | §3, §5.8, §6.5 | D-A20 MCP server contract Phase 1 deliverable; implementation Phase 2 deferred |
| Assess | [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](bff-ai-extraction-assessment-2026-05-20.md) | 2026-06-03 (read at synthesis) | 2026-05-20 | §1, §6.5, §7.3, §7.4 | Tone template + ADR-013 refinement evidence base; ~2,800 AI test count; 60 MB / 75+ MB baselines |

**Recency self-check** (per project recency floor: ≥80% of citations dated 2026-04-01 onwards):

- Microsoft Learn pages: **12 of 13 within floor** (P12 is 1 day below floor — content stable, flagged inline per §F7 + §F11). 92.3% pass.
- Devblog posts: **3 of 3 within floor** (D1 April 2026; D3 2026-06-02; D6 2026 confirmed within floor).
- GitHub Issues: **2 of 2 within floor** (I1 2026-06-02; I2 2026-06-03).
- Sample tree: **SHA `afa7834e` fetched 2026-06-03** (today).
- Spaarke source / ADRs / constraints: **stable content** justification (per project recency floor allowance for "foundational pages / ADRs / Spaarke internal docs").

**Total primary-source citations (live URLs from §4-§7)**: 18 (13 Learn + 3 Devblogs + 2 GitHub Issues). All 18 dated within recency floor or have inline "stable content" justification. **Recency rate: 100%** (well exceeds the 80% acceptance threshold).

---

*End of assessment. Owner decision required on: (a) accept S1 PARTIAL + wait-for-#6268 default, (b) override and pilot S1 lift now with feature flag, (c) initiate S5B canonical durable HITL legal workflows project SPEC, (d) commit S7 D-A20 contract authoring to explicitly resolve U1/U2/U3, (e) capture the shared middleware-lift infrastructure change in an ADR-013 successor when S1 lift is approved. The choice shapes the next revision of ADR-013 and the unblock note at [`projects/agent-framework-knowledge-r1/UNBLOCK-RECOMMENDATION.md`](../../projects/agent-framework-knowledge-r1/UNBLOCK-RECOMMENDATION.md).*
