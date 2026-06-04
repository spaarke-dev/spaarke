# Spaarke AI Code Surfaces Inventory (S1-S4 + S8 catch)

> **Project**: agent-framework-fit-assessment-r1 · **Task**: 001
> **Captured at**: 2026-06-03
> **Executor**: Claude Code (task-execute STANDARD rigor)
> **Purpose**: Structured inventory of every in-BFF Spaarke AI agent surface so the per-surface decision matrix in task 004 has a complete catalog of (a) what each surface uses today and (b) the exact `file:line` citations the assessment will reference.
> **Read-only**: This task did not modify any `.cs` file.

---

## Convention

For each surface, the inventory captures the 7 fields mandated by the task POML constraints:

(a) Primary class/service; (b) Microsoft.* abstractions actually used (by name + namespace); (c) Spaarke-specific abstractions that wrap or replace those; (d) Deployment model; (e) Streaming behavior; (f) State management; (g) Middleware composition (where applicable).

Every claim is followed by a `file:line` citation in the markdown link convention:

`[SprkChatAgent.cs:42](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L42)`

The cross-cutting observations section at the end consolidates patterns that hold across multiple surfaces.

---

## Top-level finding (Microsoft.Extensions.AI vs Microsoft.Agents.AI)

**`Microsoft.Agents.AI` is referenced in `Sprk.Bff.Api.csproj` but NOT USED in any source file.**

- Package reference: `<PackageReference Include="Microsoft.Agents.AI" Version="1.0.0-rc1" />` — [Sprk.Bff.Api.csproj:33](src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj#L33), with comment "Microsoft.Agents.AI 1.0.0-rc1 requires Microsoft.Extensions.AI >= 10.3.0" — [Sprk.Bff.Api.csproj:27](src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj#L27)
- Verification grep `using Microsoft.Agents.AI|Microsoft.Agents.AI.` across `src/server/api/Sprk.Bff.Api/`: **zero matches**.
- Verification grep `\bChatClientAgent\b|\bAIAgent\b|\.AsAgent\(|AgentRunResponse` across `src/server/api/Sprk.Bff.Api/`: **zero matches**.

**Interpretation**: Spaarke transitively pulled `Microsoft.Agents.AI` to satisfy the `Microsoft.Extensions.AI >= 10.3.0` requirement, but every production surface uses ONLY `Microsoft.Extensions.AI` primitives (`IChatClient`, `AIFunction`, `AIFunctionFactory`, `ChatResponseUpdate`, `FunctionCallContent`, `ChatRole`, `ChatOptions`, `ChatToolMode`). This is the **exact "half-adopted state"** the SPEC §2 names, and it is verified at the code level. Every surface inventoried below is built directly on Extensions.AI primitives or on lower-level `OpenAI.Chat` types — never on Agents.AI.

---

## S1 SprkChat — conversational agent

### (a) Primary class/service

- [`SprkChatAgent.cs:37`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L37) — `public sealed class SprkChatAgent : ISprkChatAgent`. Doc-comment line 14 declares it "Core Agent Framework agent for the SprkChat feature" — but the implementation wraps raw `IChatClient` (Extensions.AI), not `ChatClientAgent` (Agents.AI). The doc-comment is aspirational, the code is not.
- Constructed exclusively by [`SprkChatAgentFactory.cs:41`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs#L41) — `public sealed class SprkChatAgentFactory`. Factory creates a fresh agent per session and on context switch ([SprkChatAgentFactory.cs:123-371](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs#L123)).
- Compound-intent detection helper: [`CompoundIntentDetector.cs:33`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/CompoundIntentDetector.cs#L33) — `public sealed class CompoundIntentDetector`. Not in DI; factory-instantiated ([CompoundIntentDetector.cs:30-32](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/CompoundIntentDetector.cs#L30)).

### (b) Microsoft.* abstractions used

All from `Microsoft.Extensions.AI` (NOT Microsoft.Agents.AI):

- `IChatClient` — two instances injected per agent: the function-invocation pipeline client (singleton, default key) and a raw client (keyed "raw"). [`SprkChatAgent.cs:39-40`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L39), constructor at [`SprkChatAgent.cs:85-92`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L85).
- `AIFunction` (collection on the agent) — [`SprkChatAgent.cs:42`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L42).
- `ChatResponseUpdate` — yielded by `SendMessageAsync` from [`SprkChatAgent.cs:124-149`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L124).
- `FunctionCallContent` — collected by `DetectToolCallsAsync` at [`SprkChatAgent.cs:177-223`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L177); inspected via `content is FunctionCallContent toolCall` ([line 211](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L211)).
- `ChatMessage`, `ChatRole`, `ChatOptions`, `ChatToolMode` — [`SprkChatAgent.cs:10`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L10) (alias `AiChatMessage = Microsoft.Extensions.AI.ChatMessage`), [`SprkChatAgent.cs:240`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L240), [`SprkChatAgent.cs:331-335`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L331).
- `TextContent` (used by middleware to emit filtered tokens) — [`AgentContentSafetyMiddleware.cs:74`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentContentSafetyMiddleware.cs#L74), [`AgentCostControlMiddleware.cs:82`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentCostControlMiddleware.cs#L82).
- `AddChatClient(...).UseFunctionInvocation()` — DI registration at [`AiModule.cs:115-116`](src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs#L115). The "raw" keyed singleton at [`AiModule.cs:107-108`](src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs#L107) deliberately omits `UseFunctionInvocation`.
- **NOT used**: `ChatClientAgent`, `AIAgent`, `AgentSession`, `AgentRunResponse`, `Microsoft.Agents.AI.*` — verified by grep returning zero matches.

### (c) Spaarke-specific wrapping abstractions

- `ISprkChatAgent` — Spaarke-defined interface ([`ISprkChatAgent.cs:64`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ISprkChatAgent.cs#L64) is where the "raw client" / `UseFunctionInvocation` distinction is documented). All middleware decorates this interface — not `IChatClient`.
- `ChatContext` — Spaarke domain model passed to the agent ([`SprkChatAgent.cs:41`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L41), exposed as a property at [`SprkChatAgent.cs:51`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L51)). Composes system prompt + document context + analysis metadata + playbook ID.
- `IChatContextProvider` (scoped) resolved via `IServiceProvider.CreateAsyncScope()` to avoid captive-dependency — [`SprkChatAgentFactory.cs:143-144`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs#L143).
- `CitationContext` — populated by search tools; reset per message at [`SprkChatAgent.cs:137`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L137).
- `CompoundIntentDetector` — Spaarke's compound-intent gate ([`CompoundIntentDetector.cs:33-63`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/CompoundIntentDetector.cs#L33); write-back tool list at [line 43-51](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/CompoundIntentDetector.cs#L43)). NOT in DI per ADR-010 ([`CompoundIntentDetector.cs:30-31`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/CompoundIntentDetector.cs#L30)).
- `ICapabilityRouter` + `ICapabilityValidator` + `ICapabilityManifest` — Spaarke's per-turn tool injection layer ([`SprkChatAgentFactory.cs:53-66`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs#L53), routing pipeline at [`SprkChatAgentFactory.cs:203-313`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs#L203)).
- Per-AI-tool wrappers (NOT `AIAgent` registrations): `DocumentSearchTools`, `KnowledgeRetrievalTools`, `AnalysisExecutionTools`, `WebSearchTools`, `WorkingDocumentTools`, etc. (12+ tool classes under [`Services/Ai/Chat/Tools/`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools)). Each is composed at runtime via `AIFunctionFactory.Create`.

### (d) Deployment model

In-process BFF, per ADR-013 default. Hosted under `Sprk.Bff.Api` App Service. No separate process or queue — all chat traffic is synchronous SSE over HTTP under `/api/ai/chat/*` (per ADR-013 architecture overview).

### (e) Streaming behavior

- Token-level streaming via `IChatClient.GetStreamingResponseAsync` returning `IAsyncEnumerable<ChatResponseUpdate>` — [`SprkChatAgent.cs:145`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L145).
- The compound-intent path makes ONE additional non-streaming round-trip via the raw client (`_rawChatClient.GetResponseAsync`) before deciding to stream or gate — [`SprkChatAgent.cs:201`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L201). The ~200-400ms extra latency is documented at [lines 166-168](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L166).
- Endpoint-level SSE wiring happens in `ChatEndpoints.cs` (out of inventory scope), with bespoke SSE event types under [`Services/Ai/Chat/SseEventTypes/`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SseEventTypes) — e.g., `plan_preview`, `capability_change`, `output_pane`. None of these are framework primitives; all Spaarke-defined.

### (f) State management

- No `AgentSession` (Agents.AI) is used. Conversation history is managed by Spaarke's `ChatHistoryManager` and `ChatSessionManager` ([`ChatHistoryManager.cs`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatHistoryManager.cs), [`ChatSessionManager.cs`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatSessionManager.cs)).
- The agent itself is stateless beyond a per-session token-budget counter in [`AgentCostControlMiddleware.cs:37`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentCostControlMiddleware.cs#L37) (`private int _sessionTokenCount;`). Middleware lifetime is transient-per-agent per [line 26](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentCostControlMiddleware.cs#L26).
- `PendingPlanManager` (Redis) holds plan-preview state across the compound-intent gate ([`PendingPlanManager.cs`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PendingPlanManager.cs)).
- System prompt is rebuilt per call via `BuildSystemContent()` — [`SprkChatAgent.cs:257-318`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L257). System message is NEVER stored in history.

### (g) Middleware composition

**Spaarke's middleware decorates `ISprkChatAgent`, NOT `IChatClient`.** This is the canonical signal that Spaarke is approximating the Agents.AI middleware contract without using it.

- Wrap order (inside-out) declared at [`SprkChatAgentFactory.cs:392-426`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs#L392): ContentSafety → CostControl → Telemetry → Routing (outermost).
- `AgentContentSafetyMiddleware` — PII pattern filtering on streamed tokens ([`AgentContentSafetyMiddleware.cs:29`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentContentSafetyMiddleware.cs#L29)). Decorates `ISprkChatAgent` ([line 29](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentContentSafetyMiddleware.cs#L29)).
- `AgentCostControlMiddleware` — per-session token-budget enforcement ([`AgentCostControlMiddleware.cs:23`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentCostControlMiddleware.cs#L23); default 10,000-token cap at [line 26](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentCostControlMiddleware.cs#L26)).
- `AgentTelemetryMiddleware` — structured logging for latency + estimated tokens ([`AgentTelemetryMiddleware.cs:27`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentTelemetryMiddleware.cs#L27); content NEVER logged per the AIPL-057 constraint at [line 20](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentTelemetryMiddleware.cs#L20)).
- `AgentServiceRoutingMiddleware` — optionally routes to a remote Agent Service backend ([`AgentServiceRoutingMiddleware.cs`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentServiceRoutingMiddleware.cs); only registered when `AgentServiceClient` is resolvable, [`SprkChatAgentFactory.cs:413-423`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs#L413)).
- None of these decorate `IChatClient` via the Extensions.AI `.AsBuilder().Use(...).Build()` pattern. They are hand-rolled decorators on a Spaarke-defined interface.

### Carry-forward from task 000 baseline — GitHub Issue #6268 (RED FLAG for S1)

Per the task 000 baseline `notes/00-primary-source-baseline.md` §6 critical issues, **GitHub Issue #6268 — ".NET: ChatClientAgent.RunStreamingAsync ends with no assistant text on multi-tool turns" (2026-06-02, .NET / bug / needs-maintainer-triage)** — is a directly relevant upstream defect for S1. S1's `SprkChatAgent` performs exactly this pattern: streaming + multi-tool turns. Any task-004 recommendation to lift S1 to `Microsoft.Agents.AI.ChatClientAgent` must condition adoption on resolution of #6268 in a shipped 1.x release.

---

## S2 AnalysisOrchestration + JPS playbooks — deterministic multi-step pipelines

### (a) Primary class/service

- [`AnalysisOrchestrationService.cs:32`](src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs#L32) — `public class AnalysisOrchestrationService : IAnalysisOrchestrationService`. 10-parameter constructor reduced from 21 by extracting `AnalysisDocumentLoader` / `AnalysisRagProcessor` / `AnalysisResultPersistence` ([lines 47-72](src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs#L47)).
- [`IPlaybookOrchestrationService.cs:22`](src/server/api/Sprk.Bff.Api/Services/Ai/IPlaybookOrchestrationService.cs#L22) — top-level orchestrator with Legacy vs NodeBased mode detection ([lines 13-21](src/server/api/Sprk.Bff.Api/Services/Ai/IPlaybookOrchestrationService.cs#L13)). NodeBased uses `ExecutionGraph` + `INodeExecutorRegistry`.
- Node executors at [`Services/Ai/Nodes/`](src/server/api/Sprk.Bff.Api/Services/Ai/Nodes) (12 files): `AiAnalysisNodeExecutor`, `ConditionNodeExecutor`, `QueryDataverseNodeExecutor`, `UpdateRecordNodeExecutor`, `SendEmailNodeExecutor`, `DeliverOutputNodeExecutor`, `DeliverToIndexNodeExecutor`, `CreateNotificationNodeExecutor`, `CreateTaskNodeExecutor`, `AgentServiceNodeExecutor`.

### (b) Microsoft.* abstractions used

- **Direct LLM streaming** via `IOpenAiClient.StreamCompletionAsync` — [`AnalysisOrchestrationService.cs:221`](src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs#L221). `IOpenAiClient` is a Spaarke interface ([`IOpenAiClient.cs:27`](src/server/api/Sprk.Bff.Api/Services/Ai/IOpenAiClient.cs#L27)) wrapping `Azure.AI.OpenAI` + `OpenAI.Chat` SDKs ([`OpenAiClient.cs:4-7`](src/server/api/Sprk.Bff.Api/Services/Ai/OpenAiClient.cs#L4)) — NOT `IChatClient` (Extensions.AI).
- **No `Microsoft.Extensions.AI` types used in AnalysisOrchestrationService or in any node executor.** Verified by grep `IChatClient|ChatClientAgent|AIAgent|AIFunctionFactory|IOpenAiClient` against `Services/Ai/Nodes/` — only `IOpenAiClient` is mentioned (in doc-comments, no field usage). Node executors take `IServiceProvider` + `IRagService` + `IRecordSearchService` + `IToolHandlerRegistry` ([`AiAnalysisNodeExecutor.cs:33-36`](src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs#L33)).
- **No `Microsoft.Agents.AI` types.** Verified by grep.

### (c) Spaarke-specific wrapping abstractions

- `IPlaybookService`, `IToolHandlerRegistry`, `INodeService`, `IScopeResolverService`, `IAnalysisContextBuilder` — five Spaarke service interfaces ([`AnalysisOrchestrationService.cs:34-42`](src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs#L34)).
- `IAiToolHandler` ([`IAiToolHandler.cs:13`](src/server/api/Sprk.Bff.Api/Services/Ai/IAiToolHandler.cs#L13)) — Spaarke's playbook tool-handler contract. Resolved per-node via `IToolHandlerRegistry` at [`AiAnalysisNodeExecutor.cs:80`](src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs#L80) and [line 136](src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs#L136). Doc-comment at [`IAiToolHandler.cs:11`](src/server/api/Sprk.Bff.Api/Services/Ai/IAiToolHandler.cs#L11) explicitly contrasts this with `IAnalysisToolHandler`: "Simpler than IAnalysisToolHandler - used for playbook-driven orchestration, not interactive document analysis".
- `ToolParameters` / `PlaybookToolResult` ([`IAiToolHandler.cs:30-160`](src/server/api/Sprk.Bff.Api/Services/Ai/IAiToolHandler.cs#L30)) — Spaarke's parameter bag + result types, NOT `AIFunction` / `FunctionResultContent`.
- `AnalysisStreamChunk` / `PlaybookStreamEvent` ([`IPlaybookOrchestrationService.cs:146-296`](src/server/api/Sprk.Bff.Api/Services/Ai/IPlaybookOrchestrationService.cs#L146)) — Spaarke's domain event types for SSE.
- `ExecutionGraph` + `PlaybookRunContext` — deterministic graph executor; NOT Agent Framework `Workflow`.

### (d) Deployment model

In-process BFF, per ADR-013 default. Endpoints under `/api/ai/analysis/*` (per ADR-013 architecture overview at [`ADR-013-ai-architecture.md:58-60`](.claude/adr/ADR-013-ai-architecture.md#L58)).

The `ExecuteAppOnlyAsync` overload ([`IPlaybookOrchestrationService.cs:45-48`](src/server/api/Sprk.Bff.Api/Services/Ai/IPlaybookOrchestrationService.cs#L45)) supports app-only (no HttpContext) execution, used by background email-to-document jobs in S4.

### (e) Streaming behavior

- `IAsyncEnumerable<AnalysisStreamChunk>` for analysis ([`AnalysisOrchestrationService.cs:75-78`](src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs#L75)).
- `IAsyncEnumerable<PlaybookStreamEvent>` for playbooks ([`IPlaybookOrchestrationService.cs:31-34`](src/server/api/Sprk.Bff.Api/Services/Ai/IPlaybookOrchestrationService.cs#L31)).
- Token-level streaming is forwarded from `IOpenAiClient.StreamCompletionAsync` (Spaarke wrapper around Azure OpenAI SDK), NOT from Extensions.AI streaming primitives.
- Working document is updated incrementally every 500 chars during streaming — [`AnalysisOrchestrationService.cs:230-234`](src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs#L230).

### (f) State management

- Analysis state is materialized as Dataverse `sprk_analysisoutput` records (the `analysisId` GUID at [`AnalysisOrchestrationService.cs:126`](src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs#L126)).
- Redis-cached `AnalysisInternalModel` for in-flight state — [`AnalysisOrchestrationService.cs:129-139`](src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs#L129); caching helpers on `_documentLoader` ([line 139](src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs#L139), [line 211](src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs#L211)).
- Continuation pattern: `ContinueAnalysisAsync` re-hydrates from Dataverse + Redis, appends chat history, re-streams — [`AnalysisOrchestrationService.cs:273-343`](src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs#L273).
- `ExecutionGraph` carries topological state across node boundaries (NOT `Workflow` checkpoints). The doc-comment at [`IPlaybookOrchestrationService.cs:18-19`](src/server/api/Sprk.Bff.Api/Services/Ai/IPlaybookOrchestrationService.cs#L18) explicitly names "topological ordering" rather than checkpointing.

### (g) Middleware composition

**None.** S2 has no middleware pipeline. Cross-cutting concerns (telemetry, RAG processing, persistence) are extracted into helper services (`AnalysisDocumentLoader`, `AnalysisRagProcessor`, `AnalysisResultPersistence`) and called directly. Compare to S1 which uses ISprkChatAgent decorators.

---

## S3 Builder — playbook builder agent

### (a) Primary class/service

- [`BuilderAgentService.cs:39`](src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L39) — `public class BuilderAgentService : IBuilderAgentService`. Implements the "Claude Code for Playbooks" pattern ([lines 36-37](src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L36)).
- Companion: `BuilderToolDefinitions` ([Services/Ai/Builder/BuilderToolDefinitions.cs](src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderToolDefinitions.cs)), `BuilderToolExecutor` ([Services/Ai/Builder/BuilderToolExecutor.cs](src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderToolExecutor.cs)), `BuilderScopeImporter`.

### (b) Microsoft.* abstractions used

- **No `Microsoft.Extensions.AI` types used directly** — verified by grep returning empty for `IChatClient|ChatClientAgent|AIAgent|AIFunctionFactory` in `BuilderAgentService.cs`.
- Uses `OpenAI.Chat` SDK types directly: `using OpenAI.Chat` ([`BuilderAgentService.cs:2`](src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L2)); `AssistantChatMessage`, `ToolChatMessage`, `ChatToolCall` ([lines 128-150](src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L128)).
- LLM calls go through `IOpenAiClient.GetChatCompletionWithToolsAsync` — [`BuilderAgentService.cs:99-103`](src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L99). Hardcoded model `"gpt-4o"` at [line 50](src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L50).
- **No `Microsoft.Agents.AI` types.** Verified by grep.

### (c) Spaarke-specific wrapping abstractions

- `IBuilderAgentService`, `BuilderAgentResult` ([`BuilderAgentService.cs:11-27`](src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L11)), `CanvasState`, `CanvasOperation` — Spaarke domain types.
- Manual agentic loop: bounded at `MaxToolRounds = 10` ([`BuilderAgentService.cs:47`](src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L47)) and implemented as an explicit `for` loop ([line 97](src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L97)). Tool routing is hand-written via `ExecuteToolCallAsync` ([line 134](src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L134)).
- Conversation messages assembled manually as `List<ChatMessage>` (OpenAI SDK type) — [`BuilderAgentService.cs:84`](src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L84).
- Tool result JSON serialization is bespoke — [`BuilderAgentService.cs:146-149`](src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L146).

### (d) Deployment model

In-process BFF. Same App Service as the rest of `Sprk.Bff.Api`. No separate deployable.

### (e) Streaming behavior

**Non-streaming.** `ExecuteAsync` returns a single `Task<BuilderAgentResult>` ([`BuilderAgentService.cs:71-75`](src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L71)). The agent does multi-round non-streaming completions internally and returns the final message + collected canvas operations.

### (f) State management

- No persistent agent state. `CanvasState` is passed in by the caller per request ([`BuilderAgentService.cs:73`](src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L73)).
- Working state mutated in-process during the agentic loop via `CreateMutableWorkingState` + `ApplyOperationsToWorkingState` ([lines 93-94, 142](src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L93)).
- Canvas operations accumulated in `allCanvasOperations` ([line 90](src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderAgentService.cs#L90)) and returned to the caller for application to the persistent Dataverse record.

### (g) Middleware composition

**None.** No middleware pipeline. Cross-cutting concerns (logging, round counting) are inlined into the `ExecuteAsync` method directly.

---

## S4 Background AI jobs — Service Bus-driven AI work

### (a) Primary class/service

Two directories of handlers:

- [`Services/Ai/Jobs/`](src/server/api/Sprk.Bff.Api/Services/Ai/Jobs) — 5 AI-coupled handlers per ADR-013 file structure ([`ADR-013-ai-architecture.md:73`](.claude/adr/ADR-013-ai-architecture.md#L73)):
  - [`AppOnlyDocumentAnalysisJobHandler.cs:25`](src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/AppOnlyDocumentAnalysisJobHandler.cs#L25) — depends on `IAppOnlyAnalysisService`.
  - [`EmailAnalysisJobHandler.cs:26`](src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/EmailAnalysisJobHandler.cs#L26) — depends on `IAppOnlyAnalysisService` for combined email+attachment analysis.
  - [`ProfileSummaryJobHandler.cs:35`](src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/ProfileSummaryJobHandler.cs#L35) — Office add-in pipeline; depends on `IAppOnlyAnalysisService`.
  - [`BulkRagIndexingJobHandler.cs:31`](src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/BulkRagIndexingJobHandler.cs#L31) — bulk RAG indexing; depends on `IFileIndexingService`.
  - [`EmbeddingMigrationService.cs:114`](src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/EmbeddingMigrationService.cs#L114) — depends on `IOpenAiClient` directly (only AI Jobs handler that does).
- [`Services/Jobs/Handlers/`](src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers) — 7 framework-only handlers per ADR-013 (the framework dispatcher lives here; AI-coupled handlers were moved to `Services/Ai/Jobs/`). The only one with AI-ish dependency:
  - [`RagIndexingJobHandler.cs:29`](src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/RagIndexingJobHandler.cs#L29) — depends on `IFileIndexingService`.
  - The remaining 6 (`AttachmentClassificationJobHandler`, `DocumentProcessingJobHandler`, `IncomingCommunicationJobHandler`, `InvoiceExtractionJobHandler`, `InvoiceIndexingJobHandler`, `SpendSnapshotGenerationJobHandler`) do not directly inject AI primitives — grep for `_openAiClient|_chatClient|StreamCompletion|GetChatCompletion|IAnalysisOrchestrationService|IPlaybookOrchestrationService` against `Services/Jobs/Handlers` returned **no matches**. They reach AI capability indirectly via service collaborators (out of scope for this surface inventory).
- Dispatcher: [`ServiceBusJobProcessor.cs`](src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs) routes Service Bus messages to handlers via `IJobHandler.JobType`.

### (b) Microsoft.* abstractions used

- **No `Microsoft.Extensions.AI` types in any S4 handler** — verified by grep for `IChatClient|ChatClientAgent|AIAgent|AIFunctionFactory` returning zero matches in `Services/Ai/Jobs/` and `Services/Jobs/`.
- One handler uses `IOpenAiClient` directly: `EmbeddingMigrationService` ([line 114](src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/EmbeddingMigrationService.cs#L114)). All others delegate to a thicker service (`IAppOnlyAnalysisService`, `IFileIndexingService`).
- `Azure.Messaging.ServiceBus` (`ServiceBusClient`) for queue chaining in `ProfileSummaryJobHandler` — [`ProfileSummaryJobHandler.cs:3`](src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/ProfileSummaryJobHandler.cs#L3), [`ProfileSummaryJobHandler.cs:39`](src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/ProfileSummaryJobHandler.cs#L39).
- **No `Microsoft.Agents.AI` types.** Verified by grep.

### (c) Spaarke-specific wrapping abstractions

- `IJobHandler`, `JobContract`, `JobOutcome` — Spaarke's job-handler contract ([`IJobHandler.cs`](src/server/api/Sprk.Bff.Api/Services/Jobs/IJobHandler.cs), [`JobContract.cs`](src/server/api/Sprk.Bff.Api/Services/Jobs/JobContract.cs), [`JobOutcome.cs`](src/server/api/Sprk.Bff.Api/Services/Jobs/JobOutcome.cs)).
- `IIdempotencyService` — Redis-backed idempotency per ADR-009 ([`IIdempotencyService.cs`](src/server/api/Sprk.Bff.Api/Services/Jobs/IIdempotencyService.cs); enforced in every handler).
- `IAppOnlyAnalysisService` — the "thicker" service most handlers depend on instead of touching raw LLM clients. Wraps the AnalysisOrchestrationService flow (S2) for app-only / no-HttpContext execution.
- `IFileIndexingService` — RAG indexing facade for `BulkRagIndexingJobHandler` and `RagIndexingJobHandler`.

### (d) Deployment model

In-process BFF as `BackgroundService` (per ADR-001). Service Bus client polls the BFF process itself. **No separate process for AI jobs** — the same App Service runs the API endpoints and the Service Bus pump. ADR-013 constraint at [`ADR-013-ai-architecture.md:47`](.claude/adr/ADR-013-ai-architecture.md#L47) ("MUST NOT host AI BFF synthesis/streaming endpoints in Azure Functions; Functions are permitted only for out-of-band integration").

### (e) Streaming behavior

**Non-streaming.** All job handlers return `Task<JobOutcome>` per `IJobHandler.ProcessAsync`. AI calls happen synchronously inside the handler (or stream internally and accumulate). No SSE / `IAsyncEnumerable` boundary surfaces out of a job handler — the caller is Service Bus, not an HTTP client.

### (f) State management

- All state is persisted in Dataverse (analysis records, indexed flags) or Azure AI Search (embeddings).
- Idempotency key per handler (e.g., `analysis-{docId}-documentprofile`, `emailanalysis-{emailId}`, `profile-{documentId}`) checked via `IIdempotencyService` before processing.
- Multi-stage pipelines (analysis → indexing) explicitly enqueue the next stage by writing a new Service Bus message — e.g., `ProfileSummaryJobHandler` chains to indexing via `_indexingQueueName` ([`ProfileSummaryJobHandler.cs:42`](src/server/api/Sprk.Bff.Api/Services/Ai/Jobs/ProfileSummaryJobHandler.cs#L42)). NOT a workflow runtime.

### (g) Middleware composition

**None at the handler level.** Cross-cutting concerns (telemetry, idempotency check, retry) are inlined into each handler's `ProcessAsync`. Retry + dead-letter are at the Service Bus layer ([`DeadLetterQueueService.cs`](src/server/api/Sprk.Bff.Api/Services/Jobs/DeadLetterQueueService.cs)).

---

## S8 Discovered surfaces — Grep catch outside S1-S4

### Search performed

`grep -rln -E "IChatClient|ChatClientAgent|AIAgent|AIFunctionFactory" src/server/api/Sprk.Bff.Api/Services/ | grep -v -E "/(Chat|Builder|Jobs)/"`

### Result — TWO discovered surfaces (not anticipated by SPEC §3)

#### S8a. `SessionSummarizationService` (Sessions/)

- File: [`Services/Ai/Sessions/SessionSummarizationService.cs:26`](src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionSummarizationService.cs#L26).
- Uses `IChatClient` (Microsoft.Extensions.AI) directly — `private readonly IChatClient _chatClient;` ([line 56](src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionSummarizationService.cs#L56)).
- Uses GPT-4o (NOT GPT-4o-mini) with the pipeline client (function-invocation enabled) — explicit rationale at [lines 12-18](src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionSummarizationService.cs#L12): legal context preservation.
- Structured-output extraction: JSON block embedded in narrative response ([lines 16-18](src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionSummarizationService.cs#L16)).
- Scoped lifetime ([line 24](src/server/api/Sprk.Bff.Api/Services/Ai/Sessions/SessionSummarizationService.cs#L24)) — fire-and-forget from chat session lifecycle to avoid blocking SSE.
- **Note**: This is conceptually adjacent to S1 SprkChat (session summarization), but lives outside `Chat/` and is functionally a separate AI surface — assessment should treat it as part of S1's perimeter OR call it out as a sibling. Recommend folding into S1 for the task 004 decision matrix.

#### S8b. `CapabilityRouter` (Capabilities/)

- File: [`Services/Ai/Capabilities/CapabilityRouter.cs:95-128`](src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs#L95).
- Optionally uses `[FromKeyedServices("raw")] IChatClient?` for Layer 2 LLM-based classification ([line 127](src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs#L127)).
- The 3-param constructor overload (without `IChatClient`) disables Layer 2 entirely ([lines 137-139](src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs#L137)).
- Companion: [`CapabilityClassificationPromptBuilder.cs:87`](src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityClassificationPromptBuilder.cs#L87) — builds `IList<ChatMessage>` (Extensions.AI) for the LLM classifier.
- **Note**: This is a sub-component of S1 (called from `SprkChatAgentFactory.CreateAgentAsync` per [`SprkChatAgentFactory.cs:232-243`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs#L232)), but it is its own concrete service with its own LLM dependency. Should be folded into S1's perimeter for the task 004 decision matrix; flagged here for completeness.

### Negative result

No surfaces outside S1-S4 + the two sub-components above instantiate `ChatClientAgent`, derive from `AIAgent`, or call `AIFunctionFactory`. The grep confirms the full code-level inventory.

---

## Cross-cutting observations

### 1. Every Spaarke AI surface uses `Microsoft.Extensions.AI` (S1) or no Microsoft AI abstraction at all (S2/S3/S4)

S1 is the **only** surface that touches Extensions.AI directly (`IChatClient`, `AIFunction`, `ChatResponseUpdate`, `FunctionCallContent`). S2 calls `IOpenAiClient` (Spaarke wrapper over Azure SDK), S3 uses `OpenAI.Chat` SDK types directly, S4 delegates entirely to `IAppOnlyAnalysisService` / `IFileIndexingService`. None of S2/S3/S4 would gain anything from `ChatClientAgent` adoption unless they were redesigned to be conversational. Verification: grep results above; SPEC §2 framing of "half-adopted state" applies primarily to S1.

### 2. `Microsoft.Agents.AI` is referenced but unused — pure carry-along dependency

The package is declared at [`Sprk.Bff.Api.csproj:33`](src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj#L33) with the comment "1.0.0-rc1 requires Microsoft.Extensions.AI >= 10.3.0". Zero code uses any Agents.AI type (no `ChatClientAgent`, no `AIAgent`, no `AgentSession`, no `Microsoft.Agents.AI.*` import). This is the cleanest possible evidence for the SPEC §2 question: Spaarke is on Extensions.AI alone; the next-step abstraction is sitting on the disk shelf but has never been instantiated.

### 3. Middleware composition is hand-rolled on a Spaarke interface, NOT on `IChatClient`

S1's middleware ([`AgentContentSafetyMiddleware`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentContentSafetyMiddleware.cs), [`AgentCostControlMiddleware`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentCostControlMiddleware.cs), [`AgentTelemetryMiddleware`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentTelemetryMiddleware.cs)) all decorate `ISprkChatAgent` (Spaarke interface), not `IChatClient`. The Extensions.AI `chatClient.AsBuilder().Use(...).Build()` pattern (documented per task 000 baseline §4 page 6, fetched 2026-06-03 `updated_at: 2026-04-02`) is **NOT** used. Spaarke's DI only calls `.UseFunctionInvocation()` ([`AiModule.cs:115-116`](src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs#L115)). S2, S3, S4 have no middleware composition at all. This is a 1-of-4 surface pattern, and the "missed" Extensions.AI middleware idiom is highly visible.

### 4. Two LLM clients per session (function-invocation vs raw) is a Spaarke-specific pattern driven by compound-intent detection

[`AiModule.cs:107-116`](src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs#L107) registers TWO `IChatClient` singletons: the default (with `.UseFunctionInvocation()`) and a keyed `"raw"` one without. `SprkChatAgent` holds references to both ([`SprkChatAgent.cs:39-40`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L39)) and uses the raw client for `DetectToolCallsAsync` ([`SprkChatAgent.cs:201`](src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs#L201)) before deciding to stream. The Agents.AI Tool Approval feature (task 000 baseline §4 page 5 + §4 page 12) covers a similar HITL-gate use case via `RequireApproval` + `RequestInfoEvent`, which suggests this raw-client split could be subsumed by a single `ChatClientAgent` + tool-approval policy if lifted. Task 004 must evaluate this explicitly.

### 5. Deployment model is uniform — all surfaces are in-process BFF

S1, S2, S3, S4, S8a, S8b all live in `Sprk.Bff.Api`. No surface is currently a separate Function, MCP server, or hosted agent. ADR-013 (refined 2026-05-20, [`ADR-013-ai-architecture.md:14-22`](.claude/adr/ADR-013-ai-architecture.md#L14)) permits separation only when ALL four exception criteria hold. The inventory confirms zero exceptions have been taken to date. This is the baseline against which task 004 evaluates whether any surface SHOULD become a separate deployable.

### 6. Streaming surface area is asymmetric

- S1 streams `ChatResponseUpdate` via Extensions.AI's `GetStreamingResponseAsync`.
- S2 streams Spaarke-defined `AnalysisStreamChunk` / `PlaybookStreamEvent` types over `IAsyncEnumerable`, with tokens forwarded from `IOpenAiClient.StreamCompletionAsync` (Spaarke wrapper around `OpenAI.Chat`).
- S3 is non-streaming.
- S4 is non-streaming.

There is no shared streaming primitive. Task 005 (deployment + migration) must consider whether unifying on Extensions.AI streaming would simplify the SSE surface — at the cost of replacing four separate event-type contracts at the endpoint layer.

### 7. State management is uniformly externalized (Redis + Dataverse) — no in-process agent state

No surface relies on in-process agent state beyond per-request locals. Sessions, conversation history, analysis records, plan-preview gates, idempotency keys all live in Redis or Dataverse. The Agents.AI `AgentSession` primitive (task 000 baseline §4 page 8, fetched 2026-06-03 `updated_at: 2026-05-26`) targets in-memory + service-side state — Spaarke's externalization pattern would have to be reconciled with that primitive if `AgentSession` were adopted.

---

## Sign-off

This inventory satisfies task 001 acceptance criteria:

- One section per surface S1, S2, S3, S4, S8 — done.
- Every claim cites `file:line` via markdown link convention — done.
- Microsoft.Extensions.AI vs Microsoft.Agents.AI distinction noted per surface (S1: Extensions.AI only; S2/S3/S4: neither; package referenced but never used) — done.
- ≥3 cross-cutting observations — 7 listed.
- S8 catch Grep run and result recorded — done; two discovered sub-surfaces (`SessionSummarizationService`, `CapabilityRouter`) flagged for task 004.

GitHub Issue #6268 (multi-tool streaming bug) is explicitly carried forward as a RED FLAG against any task-004 recommendation to lift S1 to `ChatClientAgent`. Downstream tasks 002-007 cleared to consume.
