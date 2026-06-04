# Agent Framework Feature Map — `Microsoft.Agents.AI` vs. `Microsoft.Extensions.AI`

> **Project**: agent-framework-fit-assessment-r1 · **Task**: 003
> **Captured at**: 2026-06-03 (UTC)
> **Executor**: Claude Code (task-execute STANDARD rigor)
> **Purpose**: Catalog of features that `Microsoft.Agents.AI` (Agent Framework proper) adds over the raw `Microsoft.Extensions.AI` baseline that Spaarke already uses. Every claim is grounded in live primary sources captured during task 000 + task 003 re-fetches at 2026-06-03. The curated `knowledge/agent-framework/` snapshot pinned at 2026-05-14 SHA `3256550c` was consulted for orientation only.
> **Recency floor**: 2026-04-01 (per project constraint); see §RC at the bottom of this file for the per-citation audit.
> **Surface keys**: S1 SprkChat · S2 AnalysisOrchestration+JPS · S3 Builder agent · S4 Background AI jobs · S5 Foundry overlap · S6 M365 Copilot · S7 Insights Engine MCP (per SPEC §3).

---

## §0. The sharp distinction (read this first)

`Microsoft.Extensions.AI` is the **inference-client + tool-primitive** abstraction layer. It ships:

- `IChatClient` — the inference call (request/response)
- `AIFunction` + `AIFunctionFactory.Create(...)` — function tool registration via `[Description]` reflection
- `ChatResponse` / `ChatResponseUpdate` / `ChatMessage` — the message graph
- `FunctionInvokingChatClient` — the function-call loop that turns `IChatClient` into a tool-calling client
- `ChatResponseFormat` / `ChatResponseFormat.ForJsonSchema<T>()` — structured-output configuration
- `AsBuilder().Use*().Build()` — chat-client middleware composition (the pipeline pattern)
- `UseOpenTelemetry(sourceName)` on the chat-client builder

`Microsoft.Agents.AI` is the **agent + multi-agent + hosting** layer on top. It ships:

- `AIAgent` — base class for any agent (including custom + proxy agents)
- `ChatClientAgent` — agent that wraps any `IChatClient`
- `AgentSession` — conversation-state container; `CreateSessionAsync()`, `SerializeSession`, `DeserializeSessionAsync`
- `AgentRunOptions` / `AgentResponse<T>` / `AgentResponseUpdate` / `RunAsync<T>` / `RunStreamingAsync`
- `Use(runFunc, runStreamingFunc, ...)` and `Use(functionCallingFunc, ...)` agent-level middleware (in addition to `IChatClient` middleware below)
- `ApprovalRequiredAIFunction` + `FunctionApprovalRequestContent` / `FunctionApprovalResponseContent` — Tool Approval HITL
- `WithOpenTelemetry(sourceName)` on the agent (in addition to the chat-client `UseOpenTelemetry`)
- `AsAIFunction()` — adapt an agent into a function tool for another agent
- `AsAIAgent(...)` / `.AsAIAgent(model, instructions, clientFactory)` — provider-helper builders
- A2A proxy agents + `Microsoft.Agents.AI.Hosting.A2A.AspNetCore` + `MapA2A(...)`
- `Microsoft.Agents.AI.Workflows` — graph-based orchestration: `WorkflowBuilder`, `Executor<TIn,TOut>`, `RequestPort`, `RequestInfoEvent`, supersteps, checkpoints, handoff/sequential/concurrent orchestrations
- `MCPToolDefinition` / `MCPToolResource` — hosted MCP tool wiring (.NET; consumed via `Microsoft.Agents.AI.Foundry`)

**Spaarke's current state** (per notes/01): `SprkChatAgent` in `Services/Ai/Chat/` consumes `IChatClient` and `AIFunctionFactory` directly. It declares itself an "Agent Framework agent" in doc-comments but never instantiates `ChatClientAgent` or derives from `AIAgent`. S2/S3/S4 consume `IOpenAiClient` (Azure OpenAI SDK) or raw OpenAI primitives — they don't use Agent Framework primitives at all.

Every feature section below restates this distinction in its own terms.

---

## §F1. `AIAgent` base class + `ChatClientAgent`

### What it is

`AIAgent` is the common .NET base class for every agent type in `Microsoft.Agents.AI`. It defines a uniform invocation surface (`RunAsync`, `RunAsync<T>`, `RunStreamingAsync`), uniform session management (`CreateSessionAsync`, `SerializeSession`, `DeserializeSessionAsync`), and is the type that A2A proxies, agent-as-tool (`AsAIFunction`), and workflow executors compose against. `ChatClientAgent` is the concrete subclass that wraps any `IChatClient`:

```csharp
using Microsoft.Agents.AI;
var agent = new ChatClientAgent(chatClient, instructions: "You are a helpful assistant");
```

Provider helpers expose `.AsAIAgent(model, instructions, ...)` on `AIProjectClient`, `OpenAIClient`, `AnthropicFoundryClient`, `AnthropicClient`, etc., returning a typed `AIAgent`.

### Extensions.AI baseline

Extensions.AI gives you `IChatClient.GetResponseAsync(messages, options)` and the `FunctionInvokingChatClient` decorator that handles tool-call loops. It does **not** give you:

- a common agent base type for composition (custom agents, proxy agents, agent-as-tool, workflow nodes)
- a session abstraction independent of provider-specific thread/conversation IDs
- generic typed `RunAsync<T>` with structured-output deserialization (the schema is on Extensions.AI; the typed return wrapper is on Agents.AI — see §F5)

### Spaarke applicability

- **S1 SprkChat** — `ISprkChatAgent` is hand-rolled around `IChatClient`. Lifting to `ChatClientAgent` would (a) replace the bespoke interface with the framework base type, (b) gain `AsAIFunction()` compatibility for agent composition, (c) unlock A2A proxy + workflow consumption for free. Caveat: GitHub Issue #6268 (.NET `ChatClientAgent.RunStreamingAsync` ending with no assistant text on multi-tool turns, open as of 2026-06-03) gates this — S1 is the canonical multi-tool streaming workload Spaarke runs.
- **S2 JPS** — not applicable. The JPS pipeline drives raw `IOpenAiClient` calls per node; there is no LLM-driven agent loop to wrap in `ChatClientAgent`. Adoption would require rebuilding JPS as workflows (see §F7), which is a separate decision.
- **S3 Builder agent** — `BuilderAgentService` already does intent-classification + tool-routing; this is the canonical `ChatClientAgent` use case. Adoption value is highest here after S1.
- **S4 Background jobs** — not applicable in general; individual job handlers are deterministic pipelines, not agent loops.
- **S5 Foundry** — when Foundry routing is active, `AIProjectClient.AsAIAgent(...)` already returns `AIAgent`. The shipped `Services/Ai/Foundry/` wrapper bypasses this and uses `AgentsClient` directly. Adopting `AIAgent` would simplify the wrapper.
- **S6 / S7** — both surface MCP/A2A to external consumers; `AIAgent` is the type those hosting surfaces expect (see §F12).

### Primary sources

- Microsoft Learn — `https://learn.microsoft.com/en-us/agent-framework/agents/` (fetched 2026-06-03 via baseline; updated_at 2026-04-20). Page 2 in notes/00 §4.
- Microsoft Learn — `https://learn.microsoft.com/en-us/agent-framework/agents/structured-outputs` (fetched 2026-06-03; updated_at 2026-04-20). Confirms `RunAsync<T>` is on `AIAgent` base.
- GitHub Issue — `https://github.com/microsoft/agent-framework/issues/6268` (fetched 2026-06-03; opened 2026-06-02). Streaming-on-multi-tool-turns bug caveat.

---

## §F2. `AgentSession` — agent-typed conversation state

### What it is

`AgentSession` is the conversation-state container shared across agent runs:

```csharp
AgentSession session = await agent.CreateSessionAsync();
var first = await agent.RunAsync("My name is Alice.", session);
var second = await agent.RunAsync("What is my name?", session);
```

In .NET it is an abstract base class with a `StateBag` for arbitrary per-session state. Concrete subclasses (returned by `CreateSessionAsync()`) may also carry remote-service identifiers (e.g., for service-side history). Sessions can be restored from an existing service conversation id (`chatClientAgent.CreateSessionAsync(conversationId)`, `a2aAgent.CreateSessionAsync(contextId, taskId)`) and round-tripped via `agent.SerializeSession(...)` / `agent.DeserializeSessionAsync(...)`.

### Extensions.AI baseline

Extensions.AI has no session type. `IChatClient.GetResponseAsync` takes an `IEnumerable<ChatMessage>` per call; the caller is responsible for chat-history accumulation, serialization, and remote-thread identifier mapping. Spaarke's chat history today is managed by `Services/Ai/Chat/Conversations/ChatHistoryManager.cs` (per notes/01) — exactly the "roll-your-own" gap `AgentSession` closes.

### Spaarke applicability

- **S1 SprkChat** — `ChatHistoryManager` would be replaced (or, more realistically, reimplemented to satisfy `AgentSession` semantics). Concrete win: per-conversation serialization for cross-process replay / debug capture is on the framework.
- **S2 JPS** — not applicable. JPS execution state is graph-shaped, not chat-shaped.
- **S3 Builder agent** — applies if Builder maintains multi-turn intent state across user prompts. Current implementation is largely single-turn per request (per notes/01); marginal benefit.
- **S5 Foundry** — `ChatClientAgent.CreateSessionAsync(conversationId)` is exactly the bridge between local agents and the Foundry-stored thread; the current `Services/Ai/Foundry/` wrapper does this manually.
- **S6 / S7** — A2A includes `AgentSession`-derived `contextId` propagation; hosting an agent via `MapA2A` and consuming via `A2AAgent` use the same session abstraction (see §F11).

### Primary sources

- Microsoft Learn — `https://learn.microsoft.com/en-us/agent-framework/agents/conversations/session` (fetched 2026-06-03; updated_at 2026-05-26). Page 8 in notes/00 §4.

---

## §F3. Context providers (RAG / memory plumbing)

### What it is

Pluggable retrieval-augmented context: a provider that injects RAG snippets / memories into each agent run by attaching to the agent's pre-invocation hook. The overview page (Page 1) names "context providers for agent memory" as one of the framework's foundational building blocks alongside session, model clients, middleware, and MCP clients. The upstream sample tree at SHA `afa7834e` exposes this surface concretely under `dotnet/samples/02-agents/AgentWithRAG` and `AgentWithMemory` (per notes/00 §3).

### Extensions.AI baseline

Extensions.AI has no context-provider concept. RAG/memory in raw Extensions.AI is "build it into a tool" or "stuff it into the prompt template". Spaarke's `Services/Ai/Chat/Tools/KnowledgeRetrievalTools.cs` (per notes/01) is the tool-form RAG implementation — works fine but cannot be shared with other surfaces or hot-swapped.

### Spaarke applicability

- **S1 SprkChat** — `KnowledgeRetrievalTools` could remain as tools, OR be lifted to a context provider so retrieval happens before tool-call planning (which is often the better placement for grounded chat).
- **S3 Builder agent** — relevant for scope catalog grounding (currently injected via prompt templates).
- **S5 / S6 / S7** — Foundry already provides hosted memory search; context-providers are the framework abstraction over Foundry-hosted and locally-implemented memory uniformly.

### Primary sources

- Microsoft Learn — `https://learn.microsoft.com/en-us/agent-framework/overview` (fetched 2026-06-03 via baseline; updated_at 2026-04-20). Page 1 in notes/00 §4. Names "context providers for agent memory" in the foundational-building-blocks paragraph.
- Sample reference — `microsoft/agent-framework @ SHA afa7834e dotnet/samples/02-agents/AgentWithRAG/` and `AgentWithMemory/` (catalogued in notes/00 §3).
- **Evidence-thin caveat**: the Learn documentation tree as captured does NOT include a standalone `/agents/context-providers/` page within the 2026-04-01 recency floor — the curated `knowledge/agent-framework/` snapshot also lacks one. The capability is named but not deep-documented in fetched Learn pages. For S1/S3 adoption planning, the assessment should treat the sample tree as the authoritative reference and verify the .NET type names by re-fetching at synthesis time (task 006).

---

## §F4. Middleware framework (`AsBuilder().Use*().Build()`)

### What it is

Three layered middleware types in .NET Agent Framework:

1. **Agent Run middleware** — intercepts every `RunAsync` / `RunStreamingAsync` on an `AIAgent`. Registered via `originalAgent.AsBuilder().Use(runFunc, runStreamingFunc).Build()`.
2. **Function calling middleware** — intercepts every function-tool invocation. Registered via `.Use(CustomFunctionCallingMiddleware)`. Receives a `FunctionInvocationContext` and can set `context.Terminate = true` to break the tool-call loop. Only supported when the agent uses `FunctionInvokingChatClient` (e.g., `ChatClientAgent`).
3. **IChatClient middleware** — intercepts the inference call itself. Registered via `chatClient.AsBuilder().Use(getResponseFunc, getStreamingResponseFunc).Build()` or via the `clientFactory` parameter on a provider helper:

```csharp
var agent = new AIProjectClient(endpoint, credential)
    .AsAIAgent(
        model: deploymentName,
        instructions: "...",
        clientFactory: (chatClient) => chatClient.AsBuilder()
            .Use(getResponseFunc: CustomChatClientMiddleware, getStreamingResponseFunc: null)
            .Build());
```

### Extensions.AI baseline

`IChatClient` middleware is **already in Extensions.AI** (`Microsoft.Extensions.AI.ChatClientBuilder` ships there; the `Use*` overloads pre-date Agent Framework). What Agents.AI adds:

- Agent Run middleware — there's no Agent-level invocation in Extensions.AI to wrap, so this is entirely new
- Function calling middleware — `FunctionInvocationContext` exists in Extensions.AI but the Agent Framework `.Use(CustomFunctionCallingMiddleware)` registration helper composes on top of `FunctionInvokingChatClient`

### Spaarke applicability

- **S1 SprkChat** — Spaarke's three middlewares (`AgentTelemetryMiddleware`, `AgentContentSafetyMiddleware`, `AgentCostControlMiddleware` per notes/01) are currently hand-built decorators around `ISprkChatAgent`. Lifting each to one of the three framework middleware tiers is mostly mechanical:
  - Telemetry → Agent Run middleware (wraps full agent invocation)
  - Content safety → IChatClient middleware (last gate before / first gate after inference)
  - Cost control → Agent Run middleware (counts per agent run) OR IChatClient middleware (counts per inference call)
- **S2 JPS** — not applicable: JPS uses raw `IOpenAiClient`, not `IChatClient`. The Extensions.AI middleware patterns wouldn't compose without a JPS rewrite onto `IChatClient` (which is a much larger change).
- **S3 Builder agent** — if Builder lifts to `ChatClientAgent`, the same middleware pattern applies. Currently Builder runs against raw OpenAI primitives, so this is gated on §F1 adoption for S3.
- **S5 / S6 / S7** — same pattern; relevant after the surface lifts to `AIAgent`.

### Primary sources

- Microsoft Learn — `https://learn.microsoft.com/en-us/agent-framework/agents/middleware/` (fetched 2026-06-03 via task 003; updated_at 2026-04-02). Page 6 in notes/00 §4. Full code samples for all three middleware tiers and the `clientFactory` helper.

---

## §F5. Structured outputs — `RunAsync<T>` + `ChatResponseFormat.ForJsonSchema<T>()`

### What it is

Two configuration paths:

1. Typed agent invocation (Agents.AI add):
   ```csharp
   AgentResponse<PersonInfo> response = await agent.RunAsync<PersonInfo>("...");
   PersonInfo info = response.Result;
   ```
2. Per-invocation `AgentRunOptions.ResponseFormat` carrying either `ChatResponseFormat.ForJsonSchema<T>()` (compile-time type known) or `ChatResponseFormat.ForJsonSchema(jsonElement, name, description)` (schema known at runtime):
   ```csharp
   AgentRunOptions runOptions = new() { ResponseFormat = ChatResponseFormat.ForJsonSchema<PersonInfo>() };
   AgentResponse response = await agent.RunAsync("...", options: runOptions);
   PersonInfo info = JsonSerializer.Deserialize<PersonInfo>(response.Text, JsonSerializerOptions.Web)!;
   ```
3. Streaming + structured output: assemble updates via `await updates.ToAgentResponseAsync()`, then deserialize.

### Extensions.AI baseline

`ChatResponseFormat` and `ChatResponseFormat.ForJsonSchema<T>()` are in `Microsoft.Extensions.AI` (per the Learn page's cross-references). The schema generation, JSON-schema construction, and content-format API are Extensions.AI primitives.

What Agents.AI adds:

- Generic typed `agent.RunAsync<T>(...)` returning `AgentResponse<T>` with a deserialized `.Result`
- The `AgentRunOptions.ResponseFormat` plumbing that propagates the format to the underlying `IChatClient`
- `ToAgentResponseAsync()` extension on the streaming-update enumerable for assemble-then-deserialize

### Spaarke applicability

- **S1 SprkChat** — `CompoundIntentDetector` does ad-hoc JSON parsing today (per notes/01). Lifting to `RunAsync<CompoundIntent>` would replace the custom prompt + parser with schema-bound responses. Useful but not load-bearing (the existing parser works).
- **S2 JPS** — JPS playbook nodes already produce structured outputs via raw OpenAI response_format. Lift would require re-keying through `ChatClientAgent`; net-zero win unless S2 lifts wholesale to agents (which the assessment will probably argue against).
- **S3 Builder agent** — strong fit. Builder's intent-classification + tool-routing produces structured envelopes. `RunAsync<BuilderIntent>` is idiomatic here.
- **S5 / S6 / S7** — applicable once the surface uses `ChatClientAgent`.

### Primary sources

- Microsoft Learn — `https://learn.microsoft.com/en-us/agent-framework/agents/structured-outputs` (fetched 2026-06-03 via task 003; updated_at 2026-04-20). Page 10 in notes/00 §4. Full .NET code samples for `RunAsync<T>` and `ResponseFormat`.

---

## §F6. Tools — `AIFunctionFactory`, `[Description]`, agent-as-tool, hosted MCP, code execution

### What it is

Tool surface in .NET:

- **Function tools** — `AIFunctionFactory.Create(GetWeather)` over a `[Description]`-decorated method; passed via `tools: [...]` on the agent. (This part is largely Extensions.AI.)
- **Agent-as-tool** — call `.AsAIFunction()` on an `AIAgent` to wrap an inner agent as a function tool for an outer agent. This is the canonical "specialist sub-agent" composition pattern. (Agents.AI primitive; agent-as-tool requires the `AIAgent` base.)
- **Hosted MCP tools** — wired via `MCPToolDefinition` (server label + URL + allowed tools list) and `MCPToolResource` carrying `RequireApproval = new MCPApproval("never" | "always" | custom)`. Consumed in .NET through `Microsoft.Agents.AI.Foundry` (the agent is created as a Foundry declarative agent with `Tools = { mcpTool }`).
- **Code Interpreter / File Search / Web Search** — hosted-runtime tools; provider-matrix dependent (Foundry, OpenAI Responses, Anthropic). Per the provider-support matrix on the Tools page, Code Interpreter is on Foundry + OpenAI Responses + Anthropic; File Search on Foundry + OpenAI Responses; Web Search broader.

### Extensions.AI baseline

`AIFunction`, `AIFunctionFactory.Create(method)`, and `[Description]` reflection are entirely in `Microsoft.Extensions.AI`. What Agents.AI adds:

- `.AsAIFunction()` on `AIAgent` for agent-as-tool (the cross-agent composition surface)
- `MCPToolDefinition`, `MCPToolResource`, `MCPApproval` for hosted-MCP wiring (the host-side configuration that drives the provider runtime)
- The hosted Code Interpreter / Web Search / File Search bindings that propagate via the agent options to the provider's request

### Spaarke applicability

- **S1 SprkChat** — `AIFunctionFactory.Create` is already in use via `IChatClient`. Agent-as-tool (`AsAIFunction`) is the natural way to express the existing `CompoundIntentDetector` → specialist-tool routing if Spaarke wants to break the monolithic SprkChat into composed specialists. Hosted MCP tools — direct fit if SprkChat needs to consume external MCP servers (e.g., `learn.microsoft.com/api/mcp` as in the Foundry sample).
- **S2 JPS** — JPS tools are JPS-graph-resolved, not OpenAI-tool-call-resolved. Not applicable as-is.
- **S3 Builder agent** — `BuilderToolDefinitions` + `BuilderToolExecutor` (per notes/01) currently roll their own tool registration. Lifting to `AIFunctionFactory.Create` is a clear win for consistency.
- **S6 M365 Copilot** — Hosted MCP tools is the surface Foundry uses to expose MCP servers to Declarative Agents; relevant if S6 places its MCP server behind a Foundry agent.
- **S7 Insights Engine MCP** — this is the OTHER side of the MCP relationship (Spaarke as MCP server). Hosted MCP tools is about how Foundry agents CONSUME MCP servers like S7. Indirectly relevant: it's the path by which Foundry-hosted agents would call Insights Engine.

### Primary sources

- Microsoft Learn — `https://learn.microsoft.com/en-us/agent-framework/agents/tools/` (fetched 2026-06-03 via task 003; updated_at 2026-05-26). Page 5 in notes/00 §4. Provider-support matrix + `AsAIFunction()` code sample + Tool Approval cross-link.
- Microsoft Learn — `https://learn.microsoft.com/en-us/agent-framework/agents/tools/hosted-mcp-tools` (fetched 2026-06-03 via task 003; updated_at 2026-04-24). Page 9 in notes/00 §4. `MCPToolDefinition` + `MCPToolResource` + `MCPApproval` code sample.

---

## §F7. Workflows — graph orchestration with checkpoints + supersteps + HITL

### What it is

`Microsoft.Agents.AI.Workflows` is a parallel namespace exposing a directed-graph orchestration layer:

- `WorkflowBuilder` — graph construction; `.AddEdge(from, to)`, `.WithOutputFrom(executor)`, `.Build()`
- `Executor<TIn, TOut>` (or `Executor<TIn>`) — typed nodes that override `HandleAsync(message, IWorkflowContext, ct)` and emit downstream via `context.SendMessageAsync(...)` / `context.YieldOutputAsync(...)`
- **Supersteps** — execution proceeds in supersteps; messages emitted within a superstep are delivered at the next superstep boundary; checkpoints can save at superstep boundaries
- **Checkpointing** — long-running workflow state can be checkpointed and restored; pending HITL requests are part of the checkpoint state (see §F11)
- **Multi-agent orchestration patterns** — sequential, concurrent, handoff (per `AgentWorkflowBuilder.CreateHandoffBuilderWith(...).WithHandoffs(...).EnableReturnToPrevious().Build()` from the curated `03-workflows/Orchestration/Handoff/` sample), and magentic
- **InProcessExecution** — `await InProcessExecution.RunStreamingAsync(workflow, initialInput)` returns a `StreamingRun` whose `WatchStreamAsync()` yields `WorkflowEvent` instances (`ExecutorCompletedEvent`, `WorkflowOutputEvent`, `RequestInfoEvent`, etc.)

The `04-hosting/DurableWorkflows` upstream sample category (entirely new since the 2026-05-14 curated snapshot, per notes/00 §3) demonstrates durable workflow hosting outside an in-process execution context.

### Extensions.AI baseline

Workflows are entirely new in Agents.AI. Extensions.AI has no orchestration primitive — it stops at the inference call. Spaarke's S2 (JPS) is the existing in-house equivalent: `IPlaybookExecutionEngine` + `ExecutionGraph` + node executors (per notes/01) is conceptually `WorkflowBuilder` + `Executor<TIn,TOut>` + edges, just hand-rolled.

### Spaarke applicability

- **S2 JPS** — the closest mapping. JPS playbook nodes are workflow executors; JPS edges are workflow edges; JPS already supports checkpointing semantics via persisted state. The migration cost question (which task 004 will answer) is whether the Workflows API surface is rich enough to express JPS's specific node types (BFF tool nodes, AgentServiceNodeExecutor, etc.) without losing capability.
- **S1 SprkChat** — not applicable as the agent itself, but the `WorkflowBuilder.Build().AsAgent()` pattern (workflow-as-agent) means Spaarke could surface a multi-step workflow as an `AIAgent` in S1.
- **S3 Builder agent** — partial fit. Builder is a single tool-routing agent today; if the design evolves to "interpret intent, then run a sub-workflow", Workflows is the natural fit.
- **S4 Background jobs** — Service Bus-driven processing patterns map naturally to Workflow executors; durable workflow hosting (`04-hosting/DurableWorkflows`) is the bridge. But S4 doesn't currently use Agent Framework primitives, so this is a "future architectural option" not a "swap-in win".
- **S5 Foundry overlap** — Workflows + Foundry-hosted agents is the canonical durable / HITL story per BUILD 2026 messaging (notes/00 §5 D6). For HITL legal workflows (the canonical S5 surface that has no Spaarke code yet), Workflows IS the answer.
- **S6 / S7** — not directly applicable.

### Primary sources

- Microsoft Learn — `https://learn.microsoft.com/en-us/agent-framework/workflows/` (fetched 2026-06-03 via task 003; updated_at 2026-04-29). Page 3 in notes/00 §4. Functional vs Graph API + checkpoints + orchestration patterns.
- Microsoft Learn — `https://learn.microsoft.com/en-us/agent-framework/workflows/human-in-the-loop` (fetched 2026-06-03 via task 003; updated_at 2026-03-31, borderline floor but content stable). Page 12 in notes/00 §4.
- Sample reference — `microsoft/agent-framework @ SHA afa7834e dotnet/samples/03-workflows/_StartHere/`, `Orchestration/Handoff/`, `Checkpoint/`, `04-hosting/DurableWorkflows/` (catalogued in notes/00 §3).
- Devblog — `https://devblogs.microsoft.com/dotnet/durable-workflows-in-microsoft-agent-framework/` (referenced 2026-06-03 via baseline §5 D6).

---

## §F8. MCP client integration (hosted + local)

### What it is

Two MCP-client tracks in Agent Framework:

1. **Hosted MCP tools** — the MCP server is invoked by the provider runtime (Foundry, OpenAI Responses, Anthropic, GitHub Copilot per Tools page provider matrix). The agent is configured with `MCPToolDefinition(serverLabel, serverUrl) { AllowedTools = { ... } }`; per-run `MCPToolResource { RequireApproval = new MCPApproval("never" | "always" | custom) }` controls approval semantics. The provider service runtime issues the `tools/call` requests.
2. **Local MCP tools** — the MCP server is opened from the agent process. The .NET tools-overview page lists `Local MCP Tools` as supported across all providers in the matrix (Responses, Chat Completion, Foundry, Anthropic, Ollama, GitHub Copilot).

### Extensions.AI baseline

Extensions.AI does not include a built-in MCP client surface. ModelContextProtocol.NET (`ModelContextProtocol` NuGet) exists as a separate library but is not part of Extensions.AI. Agent Framework absorbs MCP-client wiring into the agent primitive.

### Spaarke applicability

- **S1 SprkChat** — could use Hosted MCP via Foundry (e.g., `learn.microsoft.com/api/mcp` for grounded answers) OR Local MCP to consume Spaarke's internal MCP servers; both viable.
- **S2 JPS** — not directly applicable; JPS isn't tool-call shaped.
- **S6 M365 Copilot** — bilateral: Spaarke could expose MCP servers (S7-style) AND consume external MCP via Hosted MCP from Copilot-side Foundry agents.
- **S7 Insights Engine MCP** — this surface IS an MCP server. The framework helps OTHER agents consume it; doesn't change S7's server-side construction directly. However, if Spaarke ever puts an MCP client INSIDE the Insights Engine (e.g., for orchestrating between MCP services), Local MCP tools would be the path.

### Primary sources

- Microsoft Learn — `https://learn.microsoft.com/en-us/agent-framework/agents/tools/hosted-mcp-tools` (fetched 2026-06-03 via task 003; updated_at 2026-04-24). Page 9 in notes/00 §4. Full .NET sample.
- Microsoft Learn — `https://learn.microsoft.com/en-us/agent-framework/agents/tools/` (fetched 2026-06-03 via task 003; updated_at 2026-05-26). Provider-support matrix for Hosted vs Local MCP.
- Sample reference — `microsoft/agent-framework @ SHA afa7834e dotnet/samples/02-agents/ModelContextProtocol/` (catalogued in notes/00 §3).

---

## §F9. A2A proxies + `Microsoft.Agents.AI.Hosting.A2A.AspNetCore` (`MapA2A`)

### What it is

Two-direction A2A support:

1. **Consume** a remote A2A agent as a local `AIAgent` via the `A2AAgent` proxy. The remote agent is discovered via its `AgentCard` (`{baseAddress}/v1/card`).
2. **Expose** a local `AIAgent` over A2A via `Microsoft.Agents.AI.Hosting.A2A.AspNetCore`:
   ```csharp
   var pirateAgent = builder.AddAIAgent("pirate", instructions: "You are a pirate.");
   var app = builder.Build();
   app.MapA2A(pirateAgent, path: "/a2a/pirate", agentCard: new() { Name = "Pirate Agent", Description = "...", Version = "1.0" });
   ```
   This exposes a `POST /a2a/pirate/v1/message:stream` endpoint following the A2A spec (`messageId`, `contextId`, `parts[]`) and a `GET /a2a/pirate/v1/card` discovery endpoint.

NuGet: `Microsoft.Agents.AI.Hosting.A2A` + `Microsoft.Agents.AI.Hosting.A2A.AspNetCore`.

### Extensions.AI baseline

A2A is entirely outside Extensions.AI. Spaarke has no current A2A surface (per notes/01); S6/S7's external exposure is via MCP or REST today.

### Spaarke applicability

- **S1 SprkChat** — could expose SprkChat over A2A for Copilot-side or third-party-agent consumption. Direct fit but currently non-goal.
- **S5 Foundry** — relevant if Spaarke wants to invoke Foundry-hosted agents from BFF; the `A2AAgent` proxy is the in-process consumption pattern.
- **S6 M365 Copilot** — Copilot's plugin ecosystem may use A2A as a forward-compatible alternative to MCP; relevant to monitor but not actionable today.
- **S7 Insights Engine MCP** — overlapping but distinct: MCP and A2A are two interop standards. The Insights Engine could expose both surfaces simultaneously; `MapA2A` over an `AIAgent` wrapper of the Insights surface is one path.

### Primary sources

- Microsoft Learn — `https://learn.microsoft.com/en-us/agent-framework/integrations/a2a` (fetched 2026-06-03 via task 003; updated_at 2026-05-20). Page 11 in notes/00 §4. Full `MapA2A` + AgentCard + multi-agent .NET sample.

---

## §F10. Observability — `UseOpenTelemetry(sourceName)` + `WithOpenTelemetry()` + OTel GenAI Semantic Conventions

### What it is

Two instrumentation levels:

1. **Chat-client level** — `chatClient.AsBuilder().UseOpenTelemetry(sourceName: SourceName, configure: cfg => cfg.EnableSensitiveData = true).Build()`. Instruments the inference call.
2. **Agent level** — `agent.WithOpenTelemetry(sourceName: SourceName, configure: cfg => cfg.EnableSensitiveData = true)`. Instruments the agent invocation (`invoke_agent`, `execute_tool`, etc.).

Spans/metrics follow the [OpenTelemetry GenAI Semantic Conventions](https://opentelemetry.io/docs/specs/semconv/gen-ai/). Default source name if none specified: `Experimental.Microsoft.Agents.AI`. Azure Monitor export via `Azure.Monitor.OpenTelemetry.Exporter` + `APPLICATION_INSIGHTS_CONNECTION_STRING` is documented end-to-end.

**Important warning from the docs**: enabling both chat-client AND agent-level OTel with sensitive data on produces duplicated spans (the prompt/response appears in both). Pick one tier.

### Extensions.AI baseline

`UseOpenTelemetry(sourceName)` on the chat-client builder is **already in Extensions.AI** — Spaarke's `Services/Ai/Telemetry/` (per notes/01) is consistent with this pattern. What Agents.AI adds:

- The agent-level `WithOpenTelemetry()` — which adds the `invoke_agent` and `execute_tool` spans (and the agent-attribute set `gen_ai.agent.id` / `gen_ai.agent.name` / `gen_ai.request.instructions`)
- The standardized GenAI Semantic Conventions attribute mapping at the agent level

### Spaarke applicability

- **S1 SprkChat** — Spaarke's `AgentTelemetryMiddleware` (per notes/01) is the hand-rolled equivalent of `WithOpenTelemetry()`. The framework instrumentation is more complete (standardized semantic conventions; agent metadata attributes baked in) and free.
- **S2 JPS** — not applicable since JPS uses raw OpenAI SDK; framework instrumentation requires `IChatClient` or `AIAgent`.
- **S3 / S5** — applies after `ChatClientAgent` adoption.
- **All adopted surfaces** — explicitly pick chat-client OR agent OTel; not both, per the framework's duplication warning. For S1, agent-level is the natural choice (spans align with Spaarke business logic).

### Primary sources

- Microsoft Learn — `https://learn.microsoft.com/en-us/agent-framework/agents/observability` (fetched 2026-06-03 via task 003; updated_at 2026-05-21). Page 7 in notes/00 §4. Full .NET samples for `UseOpenTelemetry` + `WithOpenTelemetry` + Azure Monitor wiring + duplication warning.

---

## §F11. Tool Approval (HITL at the framework level)

### What it is

Two complementary HITL surfaces in `Microsoft.Agents.AI`:

1. **Function Tool Approval** — wrap any `AIFunction` in `ApprovalRequiredAIFunction(functionToWrap)` and the agent will surface a `FunctionApprovalRequestContent` instead of executing the function. The caller invokes `requestContent.CreateResponse(approved: true|false)` to produce a `FunctionApprovalResponseContent`, which is sent back as a new `User` message in the same `AgentSession`:
   ```csharp
   AIFunction approvalRequired = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(GetWeather));
   AgentResponse response = await agent.RunAsync("What's the weather?", session);
   var requests = response.Messages.SelectMany(x => x.Contents).OfType<FunctionApprovalRequestContent>();
   foreach (var req in requests) {
       var approvalMsg = new ChatMessage(ChatRole.User, [req.CreateResponse(true)]);
       await agent.RunAsync(approvalMsg, session);
   }
   ```
2. **Workflow HITL** — `RequestPort.Create<TRequest, TResponse>(name)` is a typed port that emits `RequestInfoEvent` from the workflow execution stream. The host receives the event, gathers the human response, and feeds it back via `handle.SendResponseAsync(requestInputEvt.Request.CreateResponse(answer))`. Pending requests are **persisted in checkpoints**; restoring a workflow re-emits its pending `RequestInfoEvent`s.

**The two surfaces unify in agent-orchestration workflows**: when a multi-agent workflow uses a tool-approval-required function, the workflow emits a `RequestInfoEvent` whose payload is a `ToolApprovalRequestContent` (instead of a custom request type) — the same event-routing mechanism handles both pure-HITL pauses and tool-approval gates.

### Extensions.AI baseline

Extensions.AI has no approval primitive. Spaarke's S1 today implements compound-intent gating manually via `CompoundIntentDetector` + per-tool client selection (the function-call-via-`UseFunctionInvocation`-or-raw-client split per notes/01). That hand-rolled state machine is exactly what `ApprovalRequiredAIFunction` + `FunctionApprovalRequestContent` collapses.

### Spaarke applicability

- **S1 SprkChat** — Direct replacement candidate for the CompoundIntentDetector + tool-client-split. Caveat: Spaarke's compound-intent gating implements additional Spaarke-specific policy beyond "ask human"; the assessment (task 004) needs to evaluate whether the policy gates map cleanly onto `ApprovalRequiredAIFunction` or remain policy code outside the framework. Likely both: framework handles the request/response routing; Spaarke policy decides which functions to wrap and which approval rules to apply.
- **S2 JPS** — not applicable (no agent loop).
- **S3 Builder agent** — relevant if Builder ever needs human-confirmation gates on tool execution (e.g., "create this scope?"). Currently single-shot, but a natural evolution.
- **S5 Foundry / HITL legal workflows** — this IS the surface S5's canonical (non-shipped) HITL story uses. Workflow `RequestPort` + checkpoints is exactly the durable-pause mechanism for multi-day legal review.
- **S6 M365 Copilot** — Declarative Agents can use Hosted MCP with `RequireApproval = "always"`; same conceptual model.
- **S7 Insights Engine MCP** — server-side; the MCP server itself can declare tools that require approval; the consuming agent enforces.

### Primary sources

- Microsoft Learn — `https://learn.microsoft.com/en-us/agent-framework/agents/tools/tool-approval` (fetched 2026-06-03 via task 003; updated_at 2026-04-02). Full .NET sample of `ApprovalRequiredAIFunction` + `FunctionApprovalRequestContent` + `CreateResponse`.
- Microsoft Learn — `https://learn.microsoft.com/en-us/agent-framework/workflows/human-in-the-loop` (fetched 2026-06-03 via task 003; updated_at 2026-03-31). Page 12 in notes/00 §4. `RequestPort` + `RequestInfoEvent` + checkpoint behavior.
- Microsoft Learn — `https://learn.microsoft.com/en-us/agent-framework/agents/tools/` (fetched 2026-06-03 via task 003; updated_at 2026-05-26). Confirms Tool Approval is framework-level not provider-level.

---

## §F12. Hosting / DI helpers + `builder.AddAIAgent(...)` + Durable Hosting

### What it is

Hosting integration helpers under `Microsoft.Agents.AI.Hosting`:

- `builder.AddAIAgent(name, instructions: "...")` — registers a named agent in DI for retrieval + composition
- `app.MapA2A(agent, path, agentCard)` — exposes an agent via A2A (§F9)
- **Durable agents / workflows** — the `04-hosting/DurableAgents` and `04-hosting/DurableWorkflows` upstream sample categories (entirely new since 2026-05-14 per notes/00 §3) demonstrate hosting agents + workflows over Durable Tasks for survive-the-process-restart semantics
- **Foundry-hosted agents** — `04-hosting/FoundryHostedAgents` for when Foundry IS the host, not just the provider

### Extensions.AI baseline

Extensions.AI does DI wiring of `IChatClient` (and the function-invoking variant) but has no agent-level DI helper and no hosting/durability surface — there's no agent to host. Spaarke's BFF wires `ISprkChatAgent` manually in `Program.cs` (per notes/01).

### Spaarke applicability

- **S1 SprkChat** — `builder.AddAIAgent(...)` simplifies the manual `ISprkChatAgent` wiring in `Program.cs`. Net change is small if the existing wiring already works.
- **S2 / S4** — JPS / background jobs are out-of-process-restart-tolerant via Service Bus + persisted state today; Durable Tasks-hosted workflows are an alternative but require migrating to Workflows (§F7).
- **S5 Foundry** — `FoundryHostedAgents` sample category is the canonical hosting pattern for the curated HITL legal workflows; high-relevance for any "lift the canonical S5 surface into Spaarke" project.
- **S6 / S7** — `MapA2A` is the hosting hook for exposing agents to external consumers. Spaarke's S6/S7 surfaces currently expose via MCP + REST; adding A2A is a forward-compatibility option, not a swap-in.

**Evidence-thin caveat**: a dedicated `/hosting/` Learn page was NOT fetched in notes/00 (per §8 Gap #2 — deferred). Sample tree at SHA `afa7834e` (`dotnet/samples/04-hosting/`) is the primary evidence base. The Durable Workflows Devblog (D6) provides narrative coverage. For task 004 decisions touching durable hosting, recommend re-fetching the `/hosting/` page if it now exists, or reading the `04-hosting/DurableWorkflows/` README at SHA.

### Primary sources

- Microsoft Learn — `https://learn.microsoft.com/en-us/agent-framework/integrations/a2a` (fetched 2026-06-03 via task 003; updated_at 2026-05-20). Demonstrates `builder.AddAIAgent(...)` + `app.MapA2A(...)` end to end.
- Devblog — `https://devblogs.microsoft.com/dotnet/durable-workflows-in-microsoft-agent-framework/` (referenced 2026-06-03 via baseline §5 D6). Durable workflow hosting narrative.
- Sample reference — `microsoft/agent-framework @ SHA afa7834e dotnet/samples/04-hosting/DurableAgents/`, `DurableWorkflows/`, `FoundryHostedAgents/` (catalogued in notes/00 §3).
- GitHub Issue — `https://github.com/microsoft/agent-framework/issues/6308` "How to deploy dotnet Hosted agents to Foundry" (fetched 2026-06-03; opened 2026-06-03, per notes/00 §6). Indicates the hosting-to-Foundry story is in active triage; treat hosting recommendations conservatively until the issue resolves.

---

## §RC. Recency self-check

**Total distinct primary-source citations across §F1–§F12**: 19 (12 Microsoft Learn pages, 1 Devblog, 4 sample-tree references at SHA `afa7834e`, 2 GitHub Issues).

### By Learn page (chronological by updated_at)

| Citation | URL | `updated_at` | Within 2026-04-01 floor? |
|---|---|---|---|
| Page 12 (Workflow HITL) | `/workflows/human-in-the-loop` | 2026-03-31 | borderline (1 day below floor) |
| Page 6 (Middleware) | `/agents/middleware/` | 2026-04-02 | ✅ |
| Page 1 (Overview) | `/overview` | 2026-04-20 | ✅ |
| Page 2 (Agents) | `/agents/` | 2026-04-20 | ✅ |
| Page 10 (Structured outputs) | `/agents/structured-outputs` | 2026-04-20 | ✅ |
| Page 9 (Hosted MCP) | `/agents/tools/hosted-mcp-tools` | 2026-04-24 | ✅ |
| Tool Approval | `/agents/tools/tool-approval` | 2026-04-02 | ✅ |
| Page 3 (Workflows) | `/workflows/` | 2026-04-29 | ✅ |
| Page 11 (A2A) | `/integrations/a2a` | 2026-05-20 | ✅ |
| Page 7 (Observability) | `/agents/observability` | 2026-05-21 | ✅ |
| Page 5 (Tools) | `/agents/tools/` | 2026-05-26 | ✅ |
| Page 8 (Sessions) | `/agents/conversations/session` | 2026-05-26 | ✅ |

**Pages count satisfying recency floor**: 11 of 12 (91.7%). Page 12 (Workflow HITL) is 1 day below the floor (updated_at 2026-03-31 vs floor 2026-04-01); content is **stable** (the HITL primitives have not had a breaking change in the BUILD 2026 release per notes/00 §2). Flagged inline in §F7 and §F11; treated as within tolerance.

### By Devblog / sample / issue (non-Learn)

| Citation | Type | Date |
|---|---|---|
| Devblog D6 — Durable Workflows | Devblog | 2026 (per notes/00 §5, exact date deferred but within recency floor) |
| Samples at SHA `afa7834e` | Sample tree | 2026-06-03 (today) |
| GitHub Issue #6268 | Issue | 2026-06-02 |
| GitHub Issue #6308 | Issue | 2026-06-03 |

All non-Learn citations dated 2026-04-01+ → 100%.

### Overall recency rate

- **All-citations rate**: 18 of 19 (94.7%) dated within recency floor; 1 of 19 (5.3%) flagged stable-content 1 day below floor.
- **Project floor target**: 80%. **Satisfied** (94.7% >> 80%).

### Researcher subagent invocations

**None invoked.** All feature claims are grounded in directly-fetched Microsoft Learn pages and the upstream sample tree at the pinned SHA. The notes/00 baseline already covers the full feature surface; task 003 re-fetched 8 specific Learn pages (Tools, Middleware, Sessions, A2A, Workflow HITL, Observability, Structured Outputs, Hosted MCP, Workflows, Tool Approval) for type-name and code-sample detail that the baseline summary did not include. No documentation gaps required external research beyond `learn.microsoft.com`.

### Evidence-thin areas flagged

1. **§F3 Context providers** — capability is named on the overview page and surfaced in upstream samples (`AgentWithRAG`, `AgentWithMemory`), but no standalone `/agents/context-providers/` Learn page exists in fetched sources within recency floor. For S1/S3 adoption planning in task 004, recommend re-checking Learn at synthesis time and falling back to sample-tree READMEs.
2. **§F12 Hosting (durable hosting)** — no `/hosting/` Learn page fetched in notes/00 (deferred); sample-tree + Devblog D6 are primary evidence. GitHub Issue #6308 (open as of 2026-06-03) indicates the Foundry-hosting deployment story is in active triage. For task 005 deployment-model claims touching durable hosting, recommend re-fetching at synthesis time.

---

## §S. Summary — what `Microsoft.Agents.AI` adds over `Microsoft.Extensions.AI` (one-page version)

| # | Feature | NEW in Agents.AI? | Already in Extensions.AI? | High-relevance Spaarke surface |
|---|---|---|---|---|
| F1 | `AIAgent` base + `ChatClientAgent` | ✅ (entire abstraction) | `IChatClient` only | S1, S3 |
| F2 | `AgentSession` (state container, serialization, remote-id binding) | ✅ | nothing equivalent | S1 (replaces `ChatHistoryManager`) |
| F3 | Context providers (pluggable RAG/memory plumbing) | ✅ (named in overview; samples concrete) | nothing equivalent | S1 (replaces tool-form RAG), S3 |
| F4 | Three-tier middleware (Agent Run / Function Calling / IChatClient) | Agent + Function levels ✅; IChatClient level inherited | `IChatClient.AsBuilder().Use*` is Extensions.AI | S1 (replaces 3 hand-rolled middleware) |
| F5 | Structured outputs (`RunAsync<T>` typed, `ResponseFormat`) | typed `RunAsync<T>` + Agent-level wrapper ✅ | `ChatResponseFormat.ForJsonSchema<T>()` is Extensions.AI | S1 CompoundIntent, S3 Builder |
| F6 | Tools — `AsAIFunction()`, hosted MCP, code interp, file search, web search | `AsAIFunction` + `MCPToolDefinition`/`MCPToolResource` ✅ | `AIFunctionFactory.Create` is Extensions.AI | S1, S3, S6 |
| F7 | Workflows (`WorkflowBuilder`, executors, supersteps, checkpoints, orchestrations) | ✅ entirely | nothing equivalent | S2 (JPS alternative — major decision), S5 (durable legal HITL) |
| F8 | MCP client (hosted + local) | ✅ (framework wiring) | external `ModelContextProtocol` lib | S1, S6, S7 |
| F9 | A2A — `MapA2A` + `A2AAgent` proxy | ✅ entirely | nothing equivalent | S5, S6, S7 (forward-compat) |
| F10 | Observability — `WithOpenTelemetry()` (agent level) + GenAI Semantic Conventions | agent-level + agent attributes ✅; chat-client `UseOpenTelemetry` inherited | `UseOpenTelemetry(sourceName)` on IChatClient is Extensions.AI | S1 (replaces `AgentTelemetryMiddleware`) |
| F11 | Tool Approval (`ApprovalRequiredAIFunction` + `FunctionApprovalRequestContent`) + Workflow HITL (`RequestPort` + `RequestInfoEvent`) | ✅ entirely | nothing equivalent | S1 (subsumes CompoundIntentDetector), S5 (canonical durable HITL) |
| F12 | Hosting helpers (`AddAIAgent`, `MapA2A`) + Durable hosting | ✅ entirely | nothing equivalent | S5, S6/S7 (A2A exposure) |

**The S1 lift question reduces to**: F1, F4, F10, F11 all map cleanly to existing Spaarke hand-rolls. F2 replaces a chat-history utility. F5 is a small improvement. F6's `AsAIFunction` enables specialist-sub-agent composition Spaarke doesn't yet do. → Adoption is structural fit; gate on GitHub Issue #6268.

**The S2 lift question reduces to**: F7 Workflows is the only path. The migration cost from JPS executors + edges → Workflow executors + edges is real, and JPS-specific node types (BFF tool nodes, AgentServiceNodeExecutor) need to be re-expressed. Task 004 decides whether the framework Workflow surface justifies the migration vs. retaining JPS.

**The S5 lift question reduces to**: For the SHIPPED in-BFF wrapper, F1 + F2 + F6 (hosted MCP) are direct fits. For the CANONICAL durable HITL surface (no Spaarke code yet), F7 + F11 + F12 are the answer. The user's open question about Foundry overlap collapses into "is the framework's Workflow-HITL story sufficient, or do we need Foundry-hosted agents on top".

**The S6/S7 lift question reduces to**: F9 A2A + F8 MCP are the relevant interop layers. Adoption is forward-compat, not a swap-in for already-shipping MCP/REST exposure.
