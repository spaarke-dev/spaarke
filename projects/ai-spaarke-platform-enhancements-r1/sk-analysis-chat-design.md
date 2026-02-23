# Spaarke AI Chat Experience - Design Document

> **Project**: AI Document Analysis Enhancements
> **Created**: February 20, 2026
> **Updated**: February 20, 2026
> **Status**: Design
> **Related**: ENH-001, ENH-002, ENH-003, ENH-004, R5 (RAG Pipeline), AI Platform Strategy
> **Framework**: Microsoft Agent Framework (RC → GA Q1 2026) — successor to Semantic Kernel + AutoGen
> **Depends On**: Microsoft Agent Framework adoption (Phase 1 of AI Platform Strategy)

---

## 1. Executive Summary

### The Problem

The current analysis chat experience has fundamental limitations that reduce accuracy and constrain the user experience:

1. **Flat prompt construction** — The continuation prompt is a single monolithic text block concatenating system prompt + document text + working document + chat history + user message. There is no structured reasoning, no tool use, no iterative self-correction.

2. **No context switching** — Chat can only reference the raw document. Users cannot ask questions about the analysis output itself (ENH-001).

3. **Naive chat history** — All messages stored as a flat JSON array in a single Dataverse text field. No summarization, no relevance filtering, no memory management. History is truncated at 20 messages regardless of relevance.

4. **No grounded retrieval** — Chat has no access to the RAG knowledge base. The AI responds only from what's in the prompt window, with no ability to pull relevant standards, precedents, or organizational knowledge.

5. **Read-only analysis output** — Users cannot select text in the analysis and request AI refinement (ENH-004). There is no highlight-and-refine workflow.

6. **In-memory session state** — Analysis context lives in a server-side dictionary (`_analysisStore`). Lost on restart, no multi-instance support.

7. **Tightly coupled to AnalysisWorkspace** — Chat is embedded directly in the AnalysisWorkspace PCF control. It cannot be reused in other contexts (Workspace, Document Studio, Word add-in, Matter page, etc.).

### The Solution

Build **Spaarke Chat** (`SprkChat`) — a **modular, reusable AI agent-powered chat component** that can be deployed as a side pane in any Spaarke surface. This is not an enhancement to the existing AnalysisWorkspace chat; it is a new, standalone component built from the ground up with **Microsoft Agent Framework** (the production successor to Semantic Kernel + AutoGen).

> **Pre-launch note**: Spaarke is in pre-launch development with no existing client deployments. There is no migration or backward compatibility requirement. We build the new implementation directly, replacing the current chat wholesale.

> **Framework note**: Microsoft Agent Framework reached Release Candidate on February 19, 2026 with GA targeted for end of Q1 2026. Semantic Kernel and AutoGen have entered maintenance mode (bug fixes only, no new features). Since we are building greenfield with no existing SK code, we target Agent Framework directly — getting simplified APIs, multi-provider support, graph-based workflows, and A2A/MCP protocol support from day one.

**Core design principles:**

- **Modular** — A shared component (React + BFF service) that surfaces in any context via configuration
- **Context-driven** — The same component adapts its behavior based on what it's embedded in (Analysis, Workspace, Word Studio, Matter page)
- **Tool-composed** — Agent tools are dynamically registered based on context, playbook, and user permissions
- **Multi-surface** — Works as a PCF side pane, Workspace panel, Document Studio panel, Word add-in chat, and Power Apps side pane
- **Multi-provider** — Agent Framework supports Azure OpenAI, OpenAI, Anthropic Claude, and AWS Bedrock through a unified `IChatClient` interface — critical for two-tier deployment model

**Surfaces where SprkChat will be used:**

```
┌─────────────────────────────────────────────────────────────────┐
│                    SprkChat Component                           │
│            (Shared React + Shared BFF Service)                  │
│                                                                 │
│  Surfaces:                                                      │
│                                                                 │
│  ┌─────────────┐ ┌─────────────┐ ┌──────────────┐ ┌──────────┐│
│  │ Analysis    │ │ Workspace   │ │ Document     │ │ Word     ││
│  │ Side Pane   │ │ Chat Panel  │ │ Studio Panel │ │ Add-in   ││
│  │             │ │             │ │              │ │ Sidebar  ││
│  │ Context:    │ │ Context:    │ │ Context:     │ │ Context: ││
│  │ Document +  │ │ Matter +    │ │ DOCX +       │ │ Document ││
│  │ Analysis    │ │ Documents + │ │ Analysis +   │ │ (quick)  ││
│  │ Output      │ │ Activities  │ │ Redlines     │ │          ││
│  └─────────────┘ └─────────────┘ └──────────────┘ └──────────┘│
│                                                                 │
│  Future:                                                        │
│  ┌─────────────┐ ┌─────────────┐ ┌──────────────┐             │
│  │ Matter Page │ │ Project     │ │ Any Dataverse│             │
│  │ Side Pane   │ │ Dashboard   │ │ Form Pane    │             │
│  │             │ │ Panel       │ │              │             │
│  │ Context:    │ │ Context:    │ │ Context:     │             │
│  │ Matter +    │ │ Project +   │ │ Record +     │             │
│  │ All Docs    │ │ Tasks +     │ │ Related Docs │             │
│  │             │ │ Documents   │ │              │             │
│  └─────────────┘ └─────────────┘ └──────────────┘             │
└─────────────────────────────────────────────────────────────────┘
```

**Key capabilities:**

- **Structured reasoning** via Agent Framework with tools, middleware, and memory
- **Tool use** — the agent can call tools (search knowledge base, extract entities, compare clauses) during conversation
- **Context-aware chat** with switchable contexts (document vs. analysis vs. matter)
- **Intelligent memory** — conversation summarization, relevance-weighted history, persistent storage
- **Highlight-and-refine** — select text from any hosting surface and invoke targeted AI refinement
- **RAG-grounded responses** — every answer can draw from organizational knowledge sources
- **Playbook-aware prompting** — chat behavior adapts based on the active playbook's scopes
- **Multi-provider support** — Azure OpenAI, OpenAI, Anthropic Claude, AWS Bedrock via unified `IChatClient`
- **Protocol standards** — A2A (Agent-to-Agent), MCP (Model Context Protocol) for future interoperability

---

## 2. Current State Assessment

### What Exists Today

```
┌─────────────────────────────────────────────────────────────┐
│  Current Architecture (AnalysisOrchestrationService)        │
│                                                             │
│  User Message ──► AnalysisContextBuilder                    │
│                   ├── System Prompt (Action + Skills)       │
│                   ├── Document Text (truncated 100K chars)  │
│                   ├── Working Document (analysis output)    │
│                   ├── Chat History (last 20 messages)       │
│                   └── User Message                          │
│                        │                                    │
│                        ▼                                    │
│                   Single OpenAI Call                         │
│                   (no tools, no retrieval, no reasoning)    │
│                        │                                    │
│                        ▼                                    │
│                   Streamed Response                          │
│                   (appended to working document)            │
└─────────────────────────────────────────────────────────────┘
```

### Key Files

| Component | File | Role |
|-----------|------|------|
| API Endpoint | `Api/Ai/AnalysisEndpoints.cs` | `POST /{analysisId}/continue` — SSE stream |
| Orchestration | `Services/Ai/AnalysisOrchestrationService.cs` | `ContinueAnalysisAsync()` — loads context, calls OpenAI, updates state |
| Prompt Builder | `Services/Ai/AnalysisContextBuilder.cs` | `BuildContinuationPromptWithContext()` — concatenates all context into one prompt |
| Configuration | `Configuration/AnalysisOptions.cs` | `MaxChatHistoryMessages=20`, `MaxDocumentContextLength=100000` |
| Session Store | In-memory `Dictionary<Guid, AnalysisInternalModel>` | Lost on restart; Task 032 will move to Dataverse |
| PCF Component | `AnalysisWorkspace/.../AnalysisWorkspaceApp.tsx` | Chat UI, SSE streaming, auto-save |
| SSE Hook | `AnalysisWorkspace/.../hooks/useSseStream.ts` | fetch + ReadableStream SSE parsing |
| Data Model | `Spaarke.Dataverse/Models.cs` — `AnalysisEntity` | `ChatHistory` (JSON string), `WorkingDocument` (markdown) |

### Current Prompt Structure

```
BuildContinuationPromptWithContext():

  # System Instructions
  {action.SystemPrompt}
  Skills: {skill.PromptFragment for each skill}

  # Original Document
  {documentText, truncated to 100K chars}

  # Current Analysis Output
  {workingDocument}

  # Conversation History
  User: {message1}
  Assistant: {response1}
  User: {message2}
  Assistant: {response2}
  ... (last 20 messages)

  # New Request
  User: {userMessage}

  Please update the analysis based on this feedback.
  Use the original document content and current analysis
  to provide accurate, document-specific responses.
  Provide the complete updated analysis, not just the changes.
```

### Limitations

| Limitation | Impact | Agent Framework Solution |
|------------|--------|------------|
| Single monolithic prompt | Token waste, context dilution | Agent Framework ChatHistory with automatic management |
| No tool use | Cannot search, compare, or reason step-by-step | Agent tools with auto function calling |
| No RAG integration in chat | Responses not grounded in org knowledge | Agent tools + RAG search for retrieval |
| Truncated history (20 msgs) | Loses important early context | Agent conversation summarization |
| In-memory state | Lost on restart, no HA | Agent sessions + Dataverse/Redis persistence |
| No context switching | Cannot chat about analysis output | Agent with switchable system prompts |
| No highlight-and-refine | Analysis output is effectively read-only | Agent targeted refinement tool |
| Same prompt for all playbooks | Suboptimal for specialized workflows | Agent dynamic tool composition from scopes |

---

## 3. Agent Framework Chat Architecture

### 3.0 Why Microsoft Agent Framework (not Semantic Kernel)

**Critical timing**: Microsoft Agent Framework reached **Release Candidate** on February 19, 2026 (the day before this design was written) with **1.0 GA targeted end of Q1 2026**. It is the official production successor to both Semantic Kernel and AutoGen:

| Framework | Status (Feb 2026) | New Features? | Our Action |
|-----------|-------------------|---------------|------------|
| **Microsoft Agent Framework** | **Release Candidate → GA Q1 2026** | **All new investment** | **Target this** |
| Semantic Kernel | Maintenance mode | Bug fixes + security only | Do not build on |
| AutoGen | Maintenance mode | Bug fixes + security only | Do not use |

**Why this is ideal for Spaarke:**

1. **We have zero existing SK code** — no migration cost, we build directly on Agent Framework APIs
2. **Simplified API** — no `Kernel` object coupling; agents created directly from AI providers via `chatClient.AsAIAgent()`
3. **Multi-provider support** — `IChatClient` interface supports Azure OpenAI, OpenAI, Anthropic Claude, AWS Bedrock. Critical for two-tier deployment where customers may have their own AI subscriptions
4. **Unified agent type** — `AIAgent` replaces `ChatCompletionAgent`, `OpenAIAssistantAgent`, `AzureAIAgent` with a single type backed by any `IChatClient` implementation
5. **Simpler tool registration** — plain methods with `[Description]` attributes, no `[KernelFunction]` decorator or plugin factory boilerplate
6. **Graph-based workflows** — sequential, concurrent, handoff, and group chat patterns for multi-agent orchestration (future: multi-agent document review)
7. **Protocol standards** — A2A (Agent-to-Agent) and MCP (Model Context Protocol) support built in, future-proofing for agent interoperability
8. **Backward compatibility** — existing `KernelFunction` instances can be converted via `.as_agent_framework_tool()` if we ever need to leverage SK-era community plugins

**Key API mapping (SK → Agent Framework):**

| Semantic Kernel (old) | Agent Framework (what we build) |
|----------------------|-------------------------------|
| `using Microsoft.SemanticKernel` | `using Microsoft.Extensions.AI` + `using Microsoft.Agents.AI` |
| `Kernel` object (central to everything) | **Eliminated** — agents created from providers directly |
| `ChatCompletionAgent` | `AIAgent` via `chatClient.AsAIAgent()` |
| `[KernelFunction("name")]` | `[Description("description")]` on plain methods |
| `KernelPluginFactory.CreateFromType<T>()` | `AIFunctionFactory.Create(method)` |
| `agent.InvokeStreamingAsync()` | `agent.RunStreamingAsync()` |
| `AgentThread` (caller creates by type) | `AgentSession` (agent creates via `agent.CreateSessionAsync()`) |
| `KernelArguments(OpenAIPromptExecutionSettings)` | `ChatClientAgentRunOptions` |
| `ToolCallBehavior.AutoInvokeKernelFunctions` | Tools registered at agent creation or per-run |

### 3.1 High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  Agent Framework Architecture                                    │
│                                                                 │
│  User Message ──► AIAgent (via IChatClient)                      │
│                   │                                             │
│                   ├── Instructions (dynamic, context-aware)      │
│                   │   └── Built from Playbook Scopes            │
│                   │                                             │
│                   ├── AgentSession (managed, summarized)         │
│                   │   └── Our Dataverse + Redis persistence      │
│                   │                                             │
│                   ├── Tools (function calling)                   │
│                   │   ├── DocumentSearchTool  (search document) │
│                   │   ├── AnalysisQueryTool   (query findings)  │
│                   │   ├── KnowledgeTool       (RAG retrieval)   │
│                   │   ├── RefinementTool      (rewrite text)    │
│                   │   ├── ComparisonTool      (clause compare)  │
│                   │   └── EntityTool          (entity lookup)   │
│                   │                                             │
│                   ├── Middleware (pre/post processing)           │
│                   │   ├── TelemetryMiddleware  (token tracking)  │
│                   │   ├── CostControlMiddleware(budget limits)   │
│                   │   ├── ContentSafetyMiddleware(guardrails)    │
│                   │   └── AuditMiddleware      (compliance log)  │
│                   │                                             │
│                   └── Memory                                    │
│                       ├── Session History (Dataverse + Redis)   │
│                       ├── DocumentContext (cached extraction)    │
│                       └── AnalysisContext (structured findings)  │
│                            │                                    │
│                            ▼                                    │
│                       IChatClient Provider                       │
│                       (Azure OpenAI / OpenAI / Claude / Bedrock)│
│                       with function calling enabled             │
│                            │                                    │
│                            ▼                                    │
│                       Streamed Response (AgentResponseUpdate)    │
│                       (may include tool calls mid-stream)       │
└─────────────────────────────────────────────────────────────────┘
```

### 3.2 Why AIAgent with IChatClient (Agent Type Decision)

Microsoft Agent Framework consolidates all agent types into a single `AIAgent` abstraction backed by `IChatClient`. This replaces the multi-agent-type decision from Semantic Kernel:

**Old world (Semantic Kernel) — 3 agent types:**

| SK Agent Type | Underlying API | State Management |
|---------------|---------------|------------------|
| `ChatCompletionAgent` | Chat Completion API | We manage state |
| `OpenAIAssistantAgent` | Assistants API (deprecated) | OpenAI manages threads |
| `AzureAIAgent` | Azure AI Agent Service | Azure manages threads |

**New world (Agent Framework) — 1 agent type, multiple providers:**

| Agent Framework | Provider | State Management |
|----------------|----------|------------------|
| `AIAgent` via `chatClient.AsAIAgent()` | Any `IChatClient` implementation | **We manage** via `AgentSession` |

**How we use it:**

```csharp
// Agent Framework: Create agent from any IChatClient provider
IChatClient chatClient = new AzureOpenAIChatClient(endpoint, credential, "gpt-4o");
AIAgent agent = chatClient.AsAIAgent(
    instructions: systemPrompt,
    tools: [searchDocument, queryAnalysis, searchKnowledge, refineText]
);
```

**Why this is better for Spaarke:**

1. **We own the state** — AgentSession backed by our Dataverse + Redis, not vendor's thread system. Critical for data governance and two-tier deployment.
2. **Provider-agnostic** — Swap `AzureOpenAIChatClient` for `OpenAIChatClient`, `AnthropicChatClient`, or `BedrockChatClient` with zero agent code changes. Model 2 (customer-hosted) customers choose their provider.
3. **Tool flexibility** — Register plain .NET methods as tools. No plugin factory or Kernel coupling.
4. **Streaming control** — `RunStreamingAsync()` returns `AgentResponseUpdate` objects with richer metadata than SK's streaming.
5. **Multi-model per surface** — Different `IChatClient` instances per context (gpt-4o for complex analysis, gpt-4o-mini for summaries, Claude for certain tenants).

### 3.3 Component Map

**Server-side** — The BFF API chat service is context-agnostic. The same service handles chat for Analysis, Workspace, Document Studio, or any surface:

```
Sprk.Bff.Api/
├── Services/Ai/Chat/
│   ├── SprkChatAgent.cs               ← AIAgent wrapper (Agent Framework)
│   ├── SprkChatAgentFactory.cs        ← Creates agent per chat session
│   ├── IChatContextProvider.cs          ← Interface: surfaces provide their context
│   ├── ChatSessionManager.cs           ← Session state (Dataverse + Redis)
│   ├── ChatHistoryManager.cs           ← History summarization + persistence
│   ├── ContextSwitcher.cs              ← Document vs. Analysis vs. custom context
│   └── HighlightRefinementService.cs   ← Targeted text refinement
│
├── Services/Ai/Chat/Contexts/          ← Context providers per surface
│   ├── AnalysisChatContext.cs          ← Context: document + analysis output
│   ├── WorkspaceChatContext.cs         ← Context: matter + multiple documents
│   ├── DocumentStudioChatContext.cs    ← Context: DOCX + redlines + analysis
│   ├── MatterChatContext.cs            ← Context: matter + all related docs
│   └── GenericDocumentChatContext.cs   ← Context: single document (Word add-in)
│
├── Services/Ai/Chat/Tools/              ← Agent tools (plain methods, no [KernelFunction])
│   ├── DocumentSearchTools.cs          ← Search within document text
│   ├── AnalysisQueryTools.cs           ← Query structured analysis findings
│   ├── KnowledgeRetrievalTools.cs      ← RAG search against knowledge base
│   ├── TextRefinementTools.cs          ← Rewrite/improve text passages
│   ├── ClauseComparisonTools.cs        ← Compare to standard terms
│   ├── EntityLookupTools.cs            ← Look up extracted entities
│   ├── MatterTools.cs                  ← Query matter-level data (Workspace)
│   └── ExportTools.cs                  ← Format and export results
│
├── Services/Ai/Chat/Middleware/         ← Agent middleware (replaces SK Filters)
│   ├── TelemetryMiddleware.cs          ← Token usage tracking
│   ├── CostControlMiddleware.cs        ← Per-session budget enforcement
│   ├── ContentSafetyMiddleware.cs      ← PII detection, guardrails
│   └── AuditMiddleware.cs              ← Compliance logging
│
├── Services/Ai/Chat/Memory/
│   ├── DataverseChatStore.cs           ← Persistent chat storage
│   ├── RedisChatCache.cs               ← Hot cache for active sessions
│   └── ConversationSummarizer.cs       ← Compress old messages
│
└── Api/Ai/
    └── ChatEndpoints.cs                ← Unified chat API endpoints
```

**Client-side** — A shared React component (`SprkChat`) lives in the shared component library and is consumed by all surfaces:

```
src/client/shared/Spaarke.UI.Components/src/components/
├── SprkChat/
│   ├── SprkChat.tsx                   ← Main chat component (the reusable piece)
│   ├── SprkChatProvider.tsx           ← Context provider + API client setup
│   ├── ChatMessage.tsx                 ← Individual message rendering
│   ├── ChatInput.tsx                   ← Input with predefined prompts
│   ├── ChatContextSwitch.tsx           ← Context mode toggle
│   ├── PredefinedPrompts.tsx           ← Copilot-style suggestion chips
│   ├── RefinementToolbar.tsx           ← Floating toolbar for highlight-and-refine
│   ├── RefinementPreview.tsx           ← Diff preview with accept/reject
│   ├── CitationLink.tsx                ← Clickable citation rendering
│   ├── ToolCallIndicator.tsx           ← "Searching..." / "Analyzing..." display
│   ├── hooks/
│   │   ├── useSprkChat.ts             ← Core chat state + SSE streaming
│   │   ├── useChatContext.ts           ← Context switching logic
│   │   ├── usePredefinedPrompts.ts     ← Prompt suggestions
│   │   ├── useRefinement.ts            ← Highlight-and-refine state
│   │   └── useChatHistory.ts           ← History pagination + search
│   ├── types.ts                        ← ISprkChatProps, IChatMessage, etc.
│   └── index.ts                        ← Public exports
│
└── index.ts                            ← Exports SprkChat alongside DataGrid, etc.
```

**Usage in each surface:**

```typescript
// AnalysisWorkspace PCF — right panel
import { SprkChat } from "@spaarke/ui-components";

<SprkChat
  sessionId={analysisId}
  contextType="analysis"
  contextData={{ documentId, analysisId, playbookId }}
  apiBaseUrl={bffApiUrl}
  onRefinement={handleRefinement}        // Analysis panel handles text updates
  onDocumentReference={scrollToClause}   // Document preview scrolls to citation
/>

// Workspace — chat panel
<SprkChat
  sessionId={workspaceSessionId}
  contextType="workspace"
  contextData={{ matterId, documentIds, activityIds }}
  apiBaseUrl={bffApiUrl}
/>

// Document Studio — right panel
<SprkChat
  sessionId={studioSessionId}
  contextType="document-studio"
  contextData={{ driveId, itemId, analysisId, editorRef }}
  apiBaseUrl={bffApiUrl}
  onRefinement={applyRedline}            // TipTap editor applies as track change
  onDocumentReference={scrollToPosition} // Editor scrolls to referenced text
/>

// Word Add-in — sidebar (compact mode)
<SprkChat
  sessionId={wordSessionId}
  contextType="document"
  contextData={{ documentId }}
  apiBaseUrl={bffApiUrl}
  compact={true}                         // Reduced UI for 350px sidebar
/>
```

### 3.4 Agent Construction

```csharp
// SprkChatAgentFactory.cs
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;

public class SprkChatAgentFactory
{
    private readonly IChatClient _chatClient;  // No Kernel — Agent Framework eliminates it
    private readonly IEnumerable<IChatContextProvider> _contextProviders;
    private readonly IChatSessionManager _sessionManager;
    private readonly DocumentSearchTools _documentSearchTools;
    private readonly TextRefinementTools _textRefinementTools;

    /// <summary>
    /// Creates an AIAgent for any chat session.
    /// The context provider (resolved by contextType) determines
    /// system prompt, tools, and available modes.
    /// </summary>
    public async Task<AIAgent> CreateAgentAsync(
        ChatSession session,
        CancellationToken ct)
    {
        // 1. Resolve the context provider for this surface
        var contextProvider = _contextProviders
            .First(p => p.ContextType == session.ContextType);

        // 2. Get tool configuration from the context provider
        //    (which may resolve playbook scopes internally)
        var toolConfig = await contextProvider.ResolveToolsAsync(session, ct);

        // 3. Build tool list — always-available + context-specific
        var tools = new List<AIFunction>
        {
            // Always available (plain methods, no [KernelFunction] needed)
            AIFunctionFactory.Create(_documentSearchTools.SearchDocumentAsync),
            AIFunctionFactory.Create(_documentSearchTools.GetDocumentMetadataAsync),
            AIFunctionFactory.Create(_textRefinementTools.RefineTextAsync),
        };

        // Context-specific tools
        tools.AddRange(toolConfig.Tools);

        // 4. Build system prompt from context provider
        var systemPrompt = await contextProvider.BuildSystemPromptAsync(session, ct);

        // 5. Create AIAgent directly from IChatClient
        //    No Kernel object — Agent Framework simplifies this
        //    We manage state ourselves via AgentSession backed by Dataverse + Redis
        var agent = _chatClient.AsAIAgent(
            instructions: systemPrompt,
            tools: tools,
            defaultOptions: new ChatClientAgentRunOptions(new()
            {
                Temperature = 0.3f,        // Low temp for accuracy
                MaxOutputTokens = 4096
            })
        );

        return agent;
    }
}

// DI Registration — no Kernel required
// In Program.cs / service configuration:
services.AddSingleton<IChatClient>(sp =>
{
    var config = sp.GetRequiredService<IOptions<AiOptions>>().Value;
    return new AzureOpenAIChatClient(
        new Uri(config.Endpoint),
        new DefaultAzureCredential(),
        config.DeploymentName);
});

services.AddSingleton<SprkChatAgentFactory>();
```

**Multi-provider support for two-tier deployment:**

```csharp
// Model 1 (Spaarke-hosted): Azure OpenAI
IChatClient chatClient = new AzureOpenAIChatClient(endpoint, credential, "gpt-4o");

// Model 2 (Customer-hosted): Customer's provider of choice
IChatClient chatClient = tenantConfig.Provider switch
{
    "azure-openai" => new AzureOpenAIChatClient(tenantConfig.Endpoint, ...),
    "openai"       => new OpenAIChatClient(tenantConfig.ApiKey, "gpt-4o"),
    "anthropic"    => new AnthropicChatClient(tenantConfig.ApiKey, "claude-sonnet-4-5"),
    "bedrock"      => new BedrockChatClient(tenantConfig.Region, ...),
    _ => throw new NotSupportedException($"Provider {tenantConfig.Provider} not supported")
};

// Same agent code works regardless of provider
AIAgent agent = chatClient.AsAIAgent(instructions: prompt, tools: tools);
```

### 3.5 LlamaParse Integration (Document Parsing Layer)

> **Key insight**: Chat accuracy depends on the quality of the upstream document text. If the parser misses a nested table or garbles a multi-column layout, no amount of agent sophistication will fix it. LlamaParse is the industry's best parser for complex documents — and it's a REST API call, not a framework migration.

#### Why LlamaParse

For a legal AI platform, the accuracy delta between standard OCR/parsing and AI-native parsing is a **competitive differentiator**:

| Capability | Azure Document Intelligence (Current) | LlamaParse (Addition) |
|-----------|---------------------------------------|----------------------|
| Standard PDF text | Good | Good |
| Nested tables (indemnification schedules) | Struggles — flattens structure | Purpose-built — preserves hierarchy |
| Multi-column legal layouts | Basic | "Agentic" tier handles spatial text |
| Contract clause detection | Generic section headings | Legal-aware clause boundaries |
| Scanned document quality | OCR-based | Multimodal LLM-based extraction |
| Downstream LLM accuracy | ~60-70% pass-through | ~90%+ pass-through |
| 130+ document formats | Limited format support | Broad format coverage |

#### Architecture: Dual-Parser Router

LlamaParse does **not replace** Azure Document Intelligence — it augments it. A router sends complex documents to LlamaParse and simple documents to Azure Doc Intel (faster, cheaper):

```
┌─────────────────────────────────────────────────────────────────┐
│  Document Parsing Pipeline (Enhanced)                            │
│                                                                 │
│  File Upload → SPE Storage → Document Parser Router              │
│                               │                                  │
│                   ┌───────────┴───────────┐                      │
│                   │                       │                      │
│            Simple Documents         Complex Documents             │
│            (plain text PDFs,        (legal contracts,            │
│             basic letters)           nested tables,              │
│                   │                  multi-column,               │
│                   ▼                  scanned docs)               │
│            Azure Document                 │                      │
│            Intelligence                   ▼                      │
│            (fast, cheap)           LlamaParse API                │
│                   │                (higher accuracy)             │
│                   │                       │                      │
│                   └───────────┬───────────┘                      │
│                               ▼                                  │
│                    Unified ParsedDocument                         │
│                    (text + structure + metadata)                  │
│                               │                                  │
│                   ┌───────────┴───────────┐                      │
│                   ▼                       ▼                      │
│            AI Analysis              RAG Chunking                 │
│            (Chat Agent)             (R5 Pipeline)                │
│            Higher accuracy          Better chunk                 │
│            from better text         boundaries                   │
└─────────────────────────────────────────────────────────────────┘
```

#### Implementation

```csharp
// Services/Ai/Parsing/DocumentParserRouter.cs
using Microsoft.Extensions.AI;

public class DocumentParserRouter
{
    private readonly DocumentIntelligenceService _azureDocIntel;
    private readonly LlamaParseClient _llamaParse;
    private readonly IOptions<ParsingOptions> _options;

    /// <summary>
    /// Routes document parsing to the optimal parser based on document characteristics.
    /// LlamaParse for complex legal docs; Azure Doc Intel for simple docs.
    /// </summary>
    public async Task<ParsedDocument> ParseDocumentAsync(
        DocumentMetadata metadata, Stream content, CancellationToken ct)
    {
        if (ShouldUseLlamaParse(metadata))
        {
            return await ParseWithLlamaParseAsync(metadata, content, ct);
        }

        return await _azureDocIntel.ExtractAsync(content, ct);
    }

    private bool ShouldUseLlamaParse(DocumentMetadata meta)
    {
        // Route to LlamaParse when accuracy matters most
        return meta.DocumentType is "contract" or "lease" or "agreement"
                                 or "amendment" or "financial-statement"
            || meta.HasTables                    // Tables need LlamaParse
            || meta.IsScanned                    // Scanned docs need LLM-based OCR
            || meta.PageCount > 30               // Long complex docs
            || meta.PlaybookRequiresHighAccuracy; // Playbook flag
    }

    private async Task<ParsedDocument> ParseWithLlamaParseAsync(
        DocumentMetadata metadata, Stream content, CancellationToken ct)
    {
        var result = await _llamaParse.ParseAsync(new ParseRequest
        {
            Content = content,
            FileName = metadata.FileName,
            Tier = metadata.HasTables ? "agentic" : "cost_effective",
            OutputFormat = "markdown",        // Markdown preserves structure for LLM
            ExtractImages = false,            // Skip for now, add if needed
            Language = metadata.Language ?? "en"
        }, ct);

        return new ParsedDocument
        {
            Text = result.Markdown,
            Sections = result.Sections,       // Parsed section boundaries
            Tables = result.Tables,           // Structured table data
            Metadata = result.Metadata,       // Extracted entities, dates
            ParserUsed = "llamaparse",
            Confidence = result.Confidence
        };
    }
}

// Services/Ai/Parsing/LlamaParseClient.cs
/// <summary>
/// Thin HTTP client for LlamaParse REST API.
/// No Python dependency — direct REST calls from .NET.
/// </summary>
public class LlamaParseClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

    private const string BaseUrl = "https://api.cloud.llamaindex.ai/api/v2";

    public async Task<LlamaParseResult> ParseAsync(
        ParseRequest request, CancellationToken ct)
    {
        // 1. Upload document
        using var form = new MultipartFormDataContent();
        form.Add(new StreamContent(request.Content), "file", request.FileName);
        form.Add(new StringContent(request.Tier), "tier");
        form.Add(new StringContent(request.OutputFormat), "output_format");

        var uploadResponse = await _http.PostAsync(
            $"{BaseUrl}/parse/upload", form, ct);
        var job = await uploadResponse.Content.ReadFromJsonAsync<ParseJob>(ct);

        // 2. Poll for completion (typically 5-30 seconds)
        return await PollForResultAsync(job!.Id, ct);
    }
}
```

#### Component Map Addition

```
Sprk.Bff.Api/
├── Services/Ai/Parsing/                   ← NEW: Document parsing layer
│   ├── DocumentParserRouter.cs            ← Routes to optimal parser
│   ├── LlamaParseClient.cs               ← REST client for LlamaParse API
│   ├── ParsedDocument.cs                  ← Unified output model
│   └── ParsingOptions.cs                  ← Configuration (tiers, routing rules)
```

#### Deployment Model Impact

| Deployment | LlamaParse Approach | Fallback |
|-----------|-------------------|----------|
| **Model 1** (Spaarke-hosted) | LlamaParse cloud API (shared Spaarke API key) | Azure Doc Intel |
| **Model 2** (Customer-hosted) | LlamaParse private VPC in customer's cloud — OR customer opts out | Azure Doc Intel only |
| **Air-gapped** | Azure Doc Intel only (no external API calls) | N/A |

The router pattern means LlamaParse is **optional per deployment** — if a customer can't use external APIs, the system falls back gracefully to Azure Document Intelligence with no code changes.

#### Cost Model

| Parser | Per-Page Cost (approx) | When Used |
|--------|----------------------|-----------|
| Azure Doc Intel | ~$0.01/page (read model) | Simple documents, fallback |
| LlamaParse "Cost Effective" | ~$0.005/page | Text-heavy docs without tables |
| LlamaParse "Agentic" | ~$0.02/page | Complex legal docs, nested tables |
| LlamaParse "Agentic Plus" | ~$0.04/page | Scanned docs, spatial layouts |

For a typical legal analysis of a 20-page contract: **~$0.40-0.80 additional cost** for LlamaParse vs Azure Doc Intel alone. Negligible compared to the Azure OpenAI costs for the analysis itself (~$0.50-2.00 per analysis run).

#### Impact on Chat Accuracy

LlamaParse directly improves the `DocumentSearchTools` that power chat:

```
Without LlamaParse:
  User: "What are the payment terms in the schedule?"
  → DocumentSearchTools searches document text
  → Azure Doc Intel missed the nested table in Schedule B
  → Agent can't find payment terms → "I don't see specific payment terms in the document"

With LlamaParse:
  User: "What are the payment terms in the schedule?"
  → DocumentSearchTools searches document text
  → LlamaParse preserved the nested table as structured markdown
  → Agent finds: "Schedule B, Section 3: Net 30, 1.5% late fee"
  → "According to Schedule B, Section 3, payment terms are Net 30..."
```

This is the kind of accuracy improvement that makes a legal AI platform trustworthy.

---

## 4. Core Capabilities

### 4.1 Higher Accuracy Results

The single biggest factor in accuracy is **how context reaches the model**. Two layers matter: (1) **document parsing quality** — LlamaParse ensures complex documents are extracted correctly (Section 3.5), and (2) **how that context is structured for the LLM** — Agent Framework enables structured, targeted delivery.

#### 4.1.1 Structured System Prompts

Instead of one concatenated prompt, use structured `ChatMessage` objects from `Microsoft.Extensions.AI` with role-separated messages:

```csharp
// Current: One giant string
var prompt = $"""
# System Instructions
{systemPrompt}

# Original Document
{documentText}

# Current Analysis
{workingDocument}

# History
{chatHistory}

# Request
{userMessage}
""";

// Agent Framework: Structured messages with roles
var messages = new List<ChatMessage>();

// System message — focused instructions only
messages.Add(new ChatMessage(ChatRole.System, systemPrompt));

// Document context as a dedicated system message
messages.Add(new ChatMessage(ChatRole.System, $"""
    <document name="{documentName}" type="{documentType}">
    {documentText}
    </document>
    """));

// Analysis context (if in Analysis mode)
if (context == ChatContext.Analysis)
{
    messages.Add(new ChatMessage(ChatRole.System, $"""
        <analysis playbook="{playbookName}">
        {structuredAnalysisJson}
        </analysis>
        """));
}

// Restored conversation history (summarized if long)
messages.AddRange(managedHistory);

// Current user message
messages.Add(new ChatMessage(ChatRole.User, userMessage));
```

**Why this is more accurate:**
- Model sees clear role boundaries (system vs user vs assistant)
- Document content is tagged with XML-like markers for grounding
- Analysis output is structured JSON, not prose — model can reference specific findings
- Chat history is managed (summarized, not truncated)

#### 4.1.2 Tool-Grounded Responses

Instead of the model guessing from context, it can **call tools** to retrieve precise information:

```csharp
// Agent Framework: Plain methods with [Description] — no [KernelFunction] needed
[Description("Search the original document for specific text, clauses, or sections")]
public async Task<string> SearchDocumentAsync(
    [Description("Search query — what to look for in the document")]
    string query,
    [Description("Maximum number of results to return")]
    int maxResults = 3)
{
    // Semantic search within the document's chunks
    var results = await _ragService.SearchAsync(
        indexName: $"doc-{_session.DocumentId}",
        query: query,
        maxResults: maxResults);

    return FormatSearchResults(results);
}

// Register as tool — no plugin factory or Kernel coupling
AIAgent agent = chatClient.AsAIAgent(
    tools: [AIFunctionFactory.Create(searchTools.SearchDocumentAsync)]
);
```

**Example interaction:**
```
User: "What does the termination clause say about notice periods?"

Without tools (current):
  Model scans 100K chars of document text in context window.
  May hallucinate or miss the specific clause.

With Agent Framework tools:
  1. Agent calls search_document("termination notice period")
  2. Tool returns exact clause text with paragraph reference
  3. Agent responds with grounded, cited answer
```

#### 4.1.3 RAG-Enhanced Responses

The `KnowledgeRetrievalTools` connects chat to the R5 Knowledge Base:

```csharp
[Description("Search organizational knowledge base for standards, best practices, or precedents")]
public async Task<string> SearchKnowledgeAsync(
    [Description("What to search for in the knowledge base")]
    string query,
    [Description("Filter by knowledge source type: 'standards', 'regulatory', 'best-practices', 'all'")]
    string sourceType = "all")
{
    var filters = sourceType != "all"
        ? new SearchFilter { SourceType = sourceType }
        : null;

    var results = await _ragService.SearchAsync(
        indexName: "spaarke-knowledge-index",
        query: query,
        filter: filters,
        maxResults: 5);

    return FormatKnowledgeResults(results);
}
```

**Example:**
```
User: "Is this indemnity clause standard?"

Agent reasoning:
  1. Call search_document("indemnity") → gets the clause text
  2. Call search_knowledge("standard indemnity clause commercial contract") → gets org standards
  3. Compare and respond: "This clause deviates from your standard terms in two ways..."
```

#### 4.1.4 Iterative Self-Correction via Middleware

Agent Framework middleware enables validation and correction loops:

```csharp
// Middleware that checks if the response references the actual document
// Agent Framework uses IChatClient middleware pipeline (similar to ASP.NET middleware)
public class GroundednessMiddleware : DelegatingChatClient
{
    public GroundednessMiddleware(IChatClient inner) : base(inner) { }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken ct = default)
    {
        var response = await base.GetResponseAsync(messages, options, ct);

        // After response, verify it references source material
        var text = response.Text;
        if (ShouldVerifyGrounding(text))
        {
            // Add a verification follow-up
            var verificationMessages = messages.Append(
                new ChatMessage(ChatRole.System,
                    "Verify your response is grounded in the document. " +
                    "If you made claims not supported by the source, correct them."));
            response = await base.GetResponseAsync(verificationMessages, options, ct);
        }

        return response;
    }
}
```

### 4.2 Chat Context Switching (ENH-001)

#### Context Modes

| Mode | System Prompt Focus | Available Plugins | Primary Content |
|------|-------------------|-------------------|-----------------|
| **Document** | "You are analyzing the original document" | DocumentSearch, EntityLookup, Knowledge | Raw document text |
| **Analysis** | "You are discussing the analysis findings" | AnalysisQuery, Refinement, Comparison | Structured analysis JSON |
| **Hybrid** (default) | "You have access to both document and analysis" | All plugins | Both contexts available |

#### Implementation

```csharp
// ContextSwitcher.cs — builds context messages using Microsoft.Extensions.AI types
using Microsoft.Extensions.AI;

public class ContextSwitcher
{
    public IList<ChatMessage> BuildContextForMode(
        ChatContext mode,
        AnalysisSession session)
    {
        var messages = new List<ChatMessage>();

        switch (mode)
        {
            case ChatContext.Document:
                messages.Add(new(ChatRole.System, BuildDocumentFocusedPrompt(session)));
                messages.Add(new(ChatRole.System, WrapDocumentContent(session.DocumentText)));
                break;

            case ChatContext.Analysis:
                messages.Add(new(ChatRole.System, BuildAnalysisFocusedPrompt(session)));
                messages.Add(new(ChatRole.System, WrapAnalysisContent(session.StructuredAnalysis)));
                // Include document as secondary reference
                messages.Add(new(ChatRole.System, WrapDocumentContent(session.DocumentText, secondary: true)));
                break;

            case ChatContext.Hybrid:
                messages.Add(new(ChatRole.System, BuildHybridPrompt(session)));
                messages.Add(new(ChatRole.System, WrapDocumentContent(session.DocumentText)));
                messages.Add(new(ChatRole.System, WrapAnalysisContent(session.StructuredAnalysis)));
                break;
        }

        return messages;
    }

    private string BuildAnalysisFocusedPrompt(AnalysisSession session)
    {
        return $"""
            You are an AI assistant discussing the analysis results for "{session.DocumentName}".

            The analysis was performed using the "{session.PlaybookName}" playbook.
            You have access to the structured analysis findings including:
            - Identified clauses and their risk levels
            - Extracted entities (parties, dates, amounts)
            - Deviations from standard terms
            - Risk assessments

            When the user asks about findings, reference specific clause IDs and risk levels.
            When the user asks to modify findings, use the refine_analysis tool.
            Always ground your responses in the actual analysis data.
            """;
    }
}
```

#### PCF Integration

```typescript
// ChatPanel.tsx — Context switch UI
interface IChatContextProps {
    context: "document" | "analysis" | "hybrid";
    onContextChange: (context: ChatContext) => void;
    analysisAvailable: boolean;
}

// Context switch triggers:
// 1. Clear the streaming state
// 2. Call POST /api/ai/analysis/{id}/switch-context
// 3. Server rebuilds agent with new system prompt + plugins
// 4. Chat history is preserved (context switch noted in history)
```

#### Context Switch API

```
POST /api/ai/analysis/{analysisId}/switch-context
Body: { "context": "analysis" }
Response: { "switched": true, "message": "Context switched to Analysis mode" }
```

The server inserts a system message into chat history noting the context switch, then rebuilds the agent with appropriate plugins and system prompt. Chat history is preserved across switches.

### 4.3 Talk to Document

The core "talk to document" experience improves dramatically with Agent Framework tools:

#### Document-Grounded Q&A

```csharp
// Agent Framework: plain methods with [Description] — registered via AIFunctionFactory.Create()
[Description("Find specific content in the document by searching through paragraphs and sections")]
public async Task<DocumentSearchResult> FindInDocumentAsync(
    [Description("What to search for")] string query,
    [Description("Type of search: 'exact' for literal text, 'semantic' for meaning-based")]
    string searchType = "semantic")
{
    if (searchType == "exact")
    {
        // Direct text search in cached document
        var matches = _documentIndex.FindExact(query);
        return new DocumentSearchResult(matches, "exact");
    }

    // Semantic search against document's chunk index
    var results = await _ragService.SearchAsync(
        indexName: $"analysis-{_session.AnalysisId}-doc",
        query: query,
        maxResults: 5);

    return new DocumentSearchResult(results, "semantic");
}

[Description("Retrieve a specific section or page range from the document")]
public Task<string> GetDocumentSectionAsync(
    [Description("Section identifier: heading text, page number, or paragraph range")]
    string sectionId)
{
    // Return the specific section from the parsed document structure
    var section = _documentStructure.GetSection(sectionId);
    return Task.FromResult(section?.Content ?? "Section not found");
}
```

#### Conversation Flow Example

```
User: "What are the payment terms?"

Agent (with Agent Framework):
  → Calls find_in_document("payment terms", "semantic")
  → Gets: "Section 5.2: Payment shall be made within 30 days of invoice..."
  → Responds: "According to Section 5.2, payment terms are Net 30 days from
    invoice date. The document specifies that late payments incur interest at
    1.5% per month (Section 5.3)."

User: "Is that standard for this type of agreement?"

Agent:
  → Calls search_knowledge("standard payment terms commercial agreement")
  → Gets: "Standard terms: Net 30-60 days, late interest 1-2% per month"
  → Responds: "Yes, Net 30 with 1.5% monthly interest is within standard range.
    Your organization's standard terms specify Net 45 with 1% interest, so this
    is slightly more aggressive. Consider negotiating to Net 45."
```

### 4.4 Modify Analysis Output Through Chat

Users can ask the AI to update the analysis working document through conversation:

```csharp
[Description("Update a specific section of the analysis output based on user feedback")]
public async Task<string> UpdateAnalysisAsync(
    [Description("Which section to update: 'summary', 'risks', 'entities', 'clauses', or a specific heading")]
    string section,
    [Description("What changes to make")]
    string instruction,
    [Description("Whether to replace the section entirely or append to it")]
    string mode = "replace")
{
    var currentContent = _session.GetAnalysisSection(section);

    // Generate updated content
    var updated = await _refinementService.RefineAsync(new RefinementRequest
    {
        OriginalText = currentContent,
        Instruction = instruction,
        Mode = mode == "replace" ? RefinementMode.Replace : RefinementMode.Append,
        Context = new RefinementContext
        {
            DocumentText = _session.DocumentText,
            PlaybookId = _session.PlaybookId
        }
    });

    // Apply to working document
    _session.UpdateAnalysisSection(section, updated);

    // Notify client of working document change
    return $"Updated '{section}' section. {updated.Summary}";
}
```

**Example interaction:**
```
User: "Add a section about regulatory compliance risks"

Agent:
  → Calls find_in_document("regulatory compliance obligation requirement")
  → Calls search_knowledge("regulatory compliance risks commercial contracts")
  → Calls update_analysis("risks", "Add regulatory compliance subsection
    covering: data protection obligations, industry-specific regulations,
    reporting requirements", "append")
  → Responds: "I've added a Regulatory Compliance Risks subsection to the
    Risk Assessment. It covers 3 areas I identified: [lists them].
    The analysis output has been updated."

Client receives:
  SSE event: { type: "working-document-update", section: "risks", ... }
  → Analysis Output panel refreshes with new content
```

### 4.5 Highlight and Refine (ENH-004)

This is a key differentiator. Users select text in the Analysis Output panel, and AI tools operate on the selection.

#### Interaction Flow

```
┌──────────────────────────────────────────────────────────────┐
│  Analysis Output Panel                                       │
│                                                              │
│  ## Risk Assessment                                          │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐  │
│  │ ███████████████████████████████████████████████████████ │  │
│  │ The indemnification clause (Section 8.1) presents      │  │
│  │ moderate risk. The provision requires Customer to       │  │
│  │ indemnify Provider for all claims without limitation.   │  │
│  │ ███████████████████████████████████████████████████████ │  │
│  └────────────────────────────────────────────────────────┘  │
│                                                              │
│  ┌─────────────────────────────────────┐                     │
│  │  AI Actions for Selection:          │                     │
│  │  [Refine Wording] [Expand] [Cite]   │                     │
│  │  [Simplify] [Make Stronger] [Ask AI]│                     │
│  └─────────────────────────────────────┘                     │
└──────────────────────────────────────────────────────────────┘
```

#### API Endpoint

```
POST /api/ai/analysis/{analysisId}/refine
Content-Type: application/json

{
  "selectedText": "The indemnification clause (Section 8.1) presents moderate risk...",
  "action": "refine",           // refine | expand | simplify | strengthen | cite | custom
  "instruction": "Make this more specific about the financial exposure",
  "context": {
    "sectionHeading": "Risk Assessment",
    "paragraphIndex": 3
  }
}

Response (SSE):
event: refinement
data: {
  "type": "refinement",
  "originalText": "The indemnification clause...",
  "refinedText": "The indemnification clause (Section 8.1) presents HIGH risk with
    uncapped financial exposure. Provider can claim unlimited damages from Customer
    for any third-party claims, including consequential losses. Financial exposure:
    potentially unlimited. Recommendation: Negotiate mutual indemnification with an
    aggregate cap of 2x annual contract value.",
  "changesSummary": "Elevated risk level to HIGH, quantified financial exposure,
    added specific recommendation with cap amount",
  "accepted": false
}
```

#### Refinement Actions

| Action | Description | System Prompt Modifier |
|--------|-------------|----------------------|
| **Refine Wording** | Improve clarity and precision | "Rewrite for clarity and professional precision" |
| **Expand** | Add more detail and analysis | "Expand with additional relevant details and analysis" |
| **Simplify** | Make more accessible | "Simplify for a non-specialist audience" |
| **Make Stronger** | Strengthen the assertion | "Strengthen this finding with more specific evidence and firmer language" |
| **Cite Sources** | Add references to document sections | "Add specific citations to document sections that support this finding" |
| **Custom** | User provides specific instruction | User's instruction passed directly |

#### PCF Implementation

```typescript
// Analysis Output panel — selection handler
const handleTextSelection = (selectedText: string, selectionContext: SelectionContext) => {
    if (selectedText.length < 10) return; // Minimum selection

    // Show floating action toolbar near selection
    setRefinementTarget({
        text: selectedText,
        sectionHeading: selectionContext.nearestHeading,
        paragraphIndex: selectionContext.paragraphIndex,
        selectionRange: selectionContext.range
    });
    setShowRefinementToolbar(true);
};

const handleRefinementAction = async (action: RefinementAction, customInstruction?: string) => {
    const response = await apiClient.refineAnalysis(analysisId, {
        selectedText: refinementTarget.text,
        action,
        instruction: customInstruction,
        context: {
            sectionHeading: refinementTarget.sectionHeading,
            paragraphIndex: refinementTarget.paragraphIndex
        }
    });

    // Show diff preview: original vs. refined
    setRefinementPreview({
        original: refinementTarget.text,
        refined: response.refinedText,
        summary: response.changesSummary
    });
    // User can [Accept] or [Reject]
};
```

### 4.6 Chat History Management

#### Current Problem

Chat history is a JSON string in `sprk_analysis.ChatHistory`:
```json
[
  {"role":"user","content":"analyze the document","timestamp":"..."},
  {"role":"assistant","content":"# Document Analysis of ACME Corp...","timestamp":"..."},
  ...
]
```

Limitations:
- Single Dataverse text field (max ~1MB)
- No summarization — old messages just truncated at 20
- No search within history
- No structured metadata (which tools were called, what context was active)

#### Agent Framework History Management

```csharp
// ChatHistoryManager.cs
using Microsoft.Extensions.AI;

public class ChatHistoryManager
{
    private readonly IDataverseChatStore _store;
    private readonly IRedisChatCache _cache;
    private readonly IConversationSummarizer _summarizer;

    private const int ActiveWindowSize = 10;       // Recent messages kept in full
    private const int SummarizationThreshold = 15;  // Summarize when history exceeds this
    private const int MaxTotalMessages = 50;        // Hard cap before archiving

    /// <summary>
    /// Get managed chat history for prompt construction.
    /// Returns: summary of old messages + full recent messages.
    /// Uses Microsoft.Extensions.AI ChatMessage types (shared with Agent Framework).
    /// </summary>
    public async Task<IList<ChatMessage>> GetManagedHistoryAsync(
        Guid sessionId,
        CancellationToken ct)
    {
        var messages = new List<ChatMessage>();

        // 1. Check for conversation summary
        var summary = await _store.GetSummaryAsync(sessionId, ct);
        if (summary != null)
        {
            messages.Add(new ChatMessage(ChatRole.System, $"""
                <conversation-summary>
                The following is a summary of the earlier conversation:
                {summary.Content}
                Key topics discussed: {string.Join(", ", summary.Topics)}
                Key decisions made: {string.Join(", ", summary.Decisions)}
                </conversation-summary>
                """));
        }

        // 2. Load recent messages (full detail)
        var recentMessages = await _store.GetRecentMessagesAsync(
            sessionId, ActiveWindowSize, ct);

        foreach (var msg in recentMessages)
        {
            messages.Add(new ChatMessage(
                role: msg.Role == "user" ? ChatRole.User : ChatRole.Assistant,
                content: msg.Content));
        }

        return messages;
    }

    /// <summary>
    /// Add a message and trigger summarization if needed.
    /// </summary>
    public async Task AddMessageAsync(
        Guid analysisId,
        ChatMessageRecord message,
        CancellationToken ct)
    {
        // 1. Persist to Dataverse
        await _store.AddMessageAsync(analysisId, message, ct);

        // 2. Update Redis cache
        await _cache.AddMessageAsync(analysisId, message);

        // 3. Check if summarization needed
        var totalCount = await _store.GetMessageCountAsync(analysisId, ct);
        if (totalCount > SummarizationThreshold)
        {
            await SummarizeOldMessagesAsync(analysisId, ct);
        }
    }

    private async Task SummarizeOldMessagesAsync(
        Guid analysisId,
        CancellationToken ct)
    {
        // Get messages outside active window
        var oldMessages = await _store.GetMessagesBeforeAsync(
            analysisId,
            offset: ActiveWindowSize,
            ct);

        // Summarize using AI
        var summary = await _summarizer.SummarizeAsync(oldMessages, ct);

        // Store summary, archive old messages
        await _store.SaveSummaryAsync(analysisId, summary, ct);
        await _store.ArchiveMessagesAsync(analysisId, oldMessages.Select(m => m.Id), ct);
    }
}
```

#### Storage Model

```
┌──────────────────────────────────────────────────────────────┐
│  Chat History Storage (Multi-Layer)                          │
│                                                              │
│  Layer 1: Redis (Hot Cache)                                  │
│  ├── Key: chat:{analysisId}:messages                         │
│  ├── Last 10 messages (full content)                         │
│  ├── TTL: 1 hour (refreshed on activity)                     │
│  └── Used for: Active session fast reads                     │
│                                                              │
│  Layer 2: Dataverse (Persistent)                             │
│  ├── Entity: sprk_aichatmessage (NEW)                        │
│  │   ├── sprk_aichatmessageid (PK)                           │
│  │   ├── sprk_analysisid (FK → sprk_analysis)                │
│  │   ├── sprk_role (OptionSet: user/assistant/system/tool)   │
│  │   ├── sprk_content (multiline text)                       │
│  │   ├── sprk_metadata (JSON: tool calls, context, tokens)   │
│  │   ├── sprk_sequencenumber (int, ordering)                 │
│  │   ├── sprk_isarchived (bool)                              │
│  │   └── sprk_createdon (datetime)                           │
│  └── Used for: Full history, audit trail, resume             │
│                                                              │
│  Layer 3: Dataverse (Summary)                                │
│  ├── Entity: sprk_aichatsummary (NEW)                        │
│  │   ├── sprk_aichatsummaryid (PK)                           │
│  │   ├── sprk_analysisid (FK → sprk_analysis)                │
│  │   ├── sprk_content (multiline: summarized conversation)   │
│  │   ├── sprk_topics (multiline: key topics JSON)            │
│  │   ├── sprk_decisions (multiline: key decisions JSON)      │
│  │   ├── sprk_messagerange (start-end sequence numbers)      │
│  │   └── sprk_createdon (datetime)                           │
│  └── Used for: Efficient context loading for long sessions   │
│                                                              │
│  Migration: sprk_analysis.ChatHistory JSON field → read-only │
│  (keep for backward compat, stop writing after migration)    │
└──────────────────────────────────────────────────────────────┘
```

#### Benefits Over Current Approach

| Aspect | Current | Agent Framework |
|--------|---------|-----------|
| Storage | Single JSON field, ~1MB limit | Individual records, unlimited |
| Old messages | Truncated at 20 | Summarized, preserving key context |
| Search | Not possible | Query by topic, date, content |
| Metadata | None | Tool calls, context mode, token usage per message |
| Audit | None | Full compliance trail |
| Resume | Reload entire JSON blob | Load summary + recent 10 |
| Multi-session | Not supported | Multiple sessions per analysis |

### 4.7 Playbook-Aware Chat

The playbook's scopes (Actions, Skills, Knowledge, Tools) drive the chat agent's tool configuration:

```csharp
// Dynamic tool composition from playbook scopes — Agent Framework pattern
public async Task<ToolConfiguration> ResolveToolsAsync(PlaybookScopes scopes)
{
    var tools = new List<AIFunction>();

    // Always included — plain method registration, no plugin factory
    tools.Add(AIFunctionFactory.Create(_documentSearchTools.SearchDocumentAsync));
    tools.Add(AIFunctionFactory.Create(_documentSearchTools.GetDocumentMetadataAsync));

    // From Skills → adjust system prompt persona
    foreach (var skill in scopes.Skills)
    {
        // Skills modify the agent's personality and expertise
        // e.g., "NDA Review" skill adds NDA-specific knowledge
        _systemPromptBuilder.AddSkillContext(skill);
    }

    // From Knowledge → enable RAG tool with source filtering
    if (scopes.KnowledgeSources.Any())
    {
        var ragTools = new KnowledgeRetrievalTools(
            _ragService,
            sourceFilter: scopes.KnowledgeSources.Select(k => k.Id).ToArray());
        tools.Add(AIFunctionFactory.Create(ragTools.SearchKnowledgeAsync));
    }

    // From Tools → map existing tool handlers to Agent Framework tools
    foreach (var tool in scopes.Tools)
    {
        var handler = _toolHandlerRegistry.GetHandler(tool.HandlerName);
        if (handler != null)
        {
            // Wrap existing IAiToolHandler as AIFunction
            var aiFunction = WrapToolHandlerAsAIFunction(handler, tool.Configuration);
            tools.Add(aiFunction);
        }
    }

    return new ToolConfiguration(tools);
}
```

**Result**: A "Quick Contract Review" playbook gets `DocumentSearch` + `EntityLookup` tools. A "Full NDA Analysis" playbook gets all tools including `ClauseComparison` with NDA-specific standards and `KnowledgeRetrieval` filtered to NDA knowledge sources.

---

## 5. Predefined Prompts (ENH-002)

### 5.1 Prompt Categories

```typescript
interface IPredefinedPrompt {
    id: string;
    label: string;
    prompt: string;
    category: "quick-action" | "analysis" | "refinement" | "comparison";
    contextMode: "document" | "analysis" | "both";
    icon?: string;
    order: number;
}
```

### 5.2 Prompt Sources (Priority Order)

1. **Dynamic (AI-generated)** — Based on analysis results, the agent suggests next steps
2. **Playbook-defined** — Each playbook can define context-specific prompts
3. **User favorites** — Saved prompts from `sprk_aiprompttemplate` (ENH-003)
4. **Static defaults** — Always available regardless of context

### 5.3 Default Prompts by Context

**Document Context:**
| Prompt | Category |
|--------|----------|
| "Summarize this document" | quick-action |
| "List all parties mentioned" | quick-action |
| "What are the key dates?" | quick-action |
| "What type of document is this?" | quick-action |

**Analysis Context:**
| Prompt | Category |
|--------|----------|
| "Summarize the key findings" | analysis |
| "What are the highest risk items?" | analysis |
| "Are there any deviations from standard terms?" | comparison |
| "What entities were extracted?" | analysis |

**Refinement Context (after highlight):**
| Prompt | Category |
|--------|----------|
| "Refine this wording" | refinement |
| "Expand with more detail" | refinement |
| "Add source citations" | refinement |
| "Simplify this finding" | refinement |

### 5.4 Dynamic Prompt Generation

After analysis completes, the agent can suggest contextual follow-ups:

```csharp
[Description("Generate contextual prompt suggestions based on the current analysis state")]
public async Task<string[]> SuggestNextActionsAsync()
{
    var analysis = _session.StructuredAnalysis;

    var suggestions = new List<string>();

    // If high-risk clauses found
    if (analysis.Risks.Any(r => r.Level == "high"))
        suggestions.Add("Explain the high-risk findings in detail");

    // If entities extracted
    if (analysis.Entities.Any())
        suggestions.Add("Verify the extracted parties and dates");

    // If deviations detected
    if (analysis.Deviations.Any())
        suggestions.Add("Compare deviations to our standard terms");

    // If knowledge sources available
    if (_session.HasKnowledgeSources)
        suggestions.Add("What do our guidelines say about these issues?");

    return suggestions.ToArray();
}
```

---

## 6. Industry-Leading Innovations

### 6.1 Agentic Document Reasoning

Go beyond simple Q&A — the agent can perform **multi-step reasoning chains** when answering complex questions:

```
User: "Is this contract favorable to us?"

Agent reasoning (visible as thinking indicators):
  Step 1: search_document("material terms obligations") → extract key terms
  Step 2: search_knowledge("standard terms our organization") → get org standards
  Step 3: compare each material term against standard
  Step 4: assess_risk for deviations
  Step 5: synthesize overall favorability assessment

Response: "Overall Assessment: MODERATELY UNFAVORABLE (Score: 38/100)

Three key issues:
1. **Indemnification (Section 8)** — Unilateral, uncapped. Your standard requires mutual with 2x cap.
2. **Payment Terms (Section 5)** — Net 30, your standard is Net 45. Minor impact.
3. **Termination (Section 12)** — 90-day notice required by you, 30-day by them. Asymmetric.

Two favorable terms:
1. **IP Rights (Section 9)** — You retain all IP, which exceeds your standard position.
2. **Governing Law (Section 15)** — Your preferred jurisdiction.

Recommendation: Negotiate Sections 8 and 12 before signing."
```

### 6.2 Structured Analysis Output (JSON + Markdown)

Instead of only producing markdown prose, the analysis also outputs structured JSON that the UI can render as interactive elements:

```json
{
  "findings": [
    {
      "id": "finding-1",
      "type": "risk",
      "severity": "high",
      "title": "Uncapped Indemnification",
      "description": "Section 8.1 requires unilateral...",
      "documentReference": { "section": "8.1", "paragraph": 42, "text": "..." },
      "standardDeviation": { "expected": "Mutual, capped at 2x ACV", "actual": "Unilateral, uncapped" },
      "recommendation": "Negotiate mutual indemnification with aggregate cap"
    }
  ],
  "entities": [...],
  "summary": "...",
  "overallRisk": "medium-high",
  "score": 38
}
```

This structured output enables:
- Clickable clause references that scroll the document preview
- Risk dashboards with severity badges
- Deviation tables with expected vs. actual
- Entity relationship diagrams

### 6.3 Conversation Branching

Allow users to "branch" a conversation — explore a what-if scenario without losing the main thread:

```
Main conversation: analyzing NDA
  → User branches: "What if we accept the non-compete as-is?"
  → Branch explores implications without affecting main analysis
  → User merges useful insights back to main thread, or discards branch
```

Implementation: Fork the `ChatHistory` at the branch point, run a separate agent session, offer merge/discard.

### 6.4 Citation and Provenance

Every AI response includes clickable citations back to the source:

```markdown
The termination clause requires 90 days written notice [§12.1](doc:p-87).
This exceeds your standard of 60 days [KB:standard-terms#termination].
```

Where:
- `[§12.1](doc:p-87)` — links to paragraph 87 in the document preview
- `[KB:standard-terms#termination]` — links to the knowledge base source

The PCF renders these as clickable links that scroll the document panel or open the knowledge source.

### 6.5 Analysis Confidence Scoring

Each finding includes a confidence score, and the agent can explain its confidence:

```
User: "How confident are you in the risk assessment?"

Agent: "Confidence by finding:
- Indemnification risk (HIGH confidence 94%): Clause text is unambiguous
- Payment terms assessment (HIGH confidence 91%): Clear numeric terms
- IP rights interpretation (MEDIUM confidence 72%): Some clauses reference
  external agreements not provided. Consider uploading the Master Agreement
  referenced in Section 3.2 for a more complete analysis."
```

### 6.6 Proactive Insights

The agent can proactively surface important findings it notices during conversation:

```csharp
// ProactiveInsightMiddleware — runs after each response via IChatClient pipeline
public class ProactiveInsightMiddleware : DelegatingChatClient
{
    private readonly IAnalysisSessionStore _sessionStore;

    public ProactiveInsightMiddleware(IChatClient inner, IAnalysisSessionStore sessionStore)
        : base(inner)
    {
        _sessionStore = sessionStore;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken ct = default)
    {
        var response = await base.GetResponseAsync(messages, options, ct);

        // After responding, check if any important unmentioned findings exist
        var unmentionedHighRisks = GetUnmentionedHighRiskFindings(response.Text);
        if (unmentionedHighRisks.Any())
        {
            // Append a proactive note to the response
            var note = $"\n\n> **Note**: I also noticed {unmentionedHighRisks.Count} " +
                       $"high-priority items not yet discussed: {string.Join(", ", unmentionedHighRisks)}";
            response = response with { Text = response.Text + note };
        }

        return response;
    }
}
```

---

## 7. Deployment Model Considerations

### Model 1 (Spaarke-Hosted, Multi-Tenant)

| Component | Isolation | Notes |
|-----------|-----------|-------|
| IChatClient | Per-tenant (resolved from config) | AzureOpenAIChatClient with tenant rate limits |
| AIAgent | Per-request (stateless) | Built from tenant config + playbook |
| Chat History (Redis) | Tenant-prefixed keys | `chat:{tenantId}:{sessionId}:*` |
| Chat History (Dataverse) | Tenant-scoped by default | Dataverse security roles apply |
| Knowledge Tools | Per-tenant index filter | Same AI Search, filtered by tenant |
| AI calls | Shared endpoint, per-tenant rate limits | Cost tracking via TelemetryMiddleware |
| Tools | Same codebase, config-driven | Tool availability from tenant settings |

### Model 2 (Customer-Hosted, Dedicated)

| Component | Isolation | Notes |
|-----------|-----------|-------|
| IChatClient | Customer's provider of choice | Azure OpenAI, OpenAI, Claude, Bedrock — all via IChatClient |
| AIAgent | Per-request | Same agent code, different IChatClient |
| Chat History | Customer's Dataverse + Redis | Full data sovereignty |
| Knowledge Tools | Customer's AI Search | Dedicated index |
| AI calls | Customer's AI provider | Customer controls model, capacity, provider |
| Tools | Same codebase | Customer can configure which tools enabled |

**No code differences** between models — configuration-driven via `AiOptions` and environment variables. The `IChatClient` is resolved at request time from tenant configuration. Agent Framework's multi-provider support means Model 2 customers can choose Azure OpenAI, OpenAI, Anthropic Claude, or AWS Bedrock.

---

## 8. Modular Component Architecture

> **Key principle**: SprkChat is built as a reusable component from day one. There is no migration from the old chat — we build new and replace wholesale. Spaarke is pre-launch with no client deployments.

### 8.1 Context Provider Pattern

The server-side chat agent is context-agnostic. Each hosting surface provides an `IChatContextProvider` that tells the agent what it's chatting about:

```csharp
using Microsoft.Extensions.AI;

/// <summary>
/// Provides context for a SprkChat session.
/// Each surface (Analysis, Workspace, Document Studio, Word, Power Apps side pane)
/// implements this interface.
/// </summary>
public interface IChatContextProvider
{
    /// <summary>Surface type identifier.</summary>
    string ContextType { get; }  // "analysis", "workspace", "document-studio", "document", "matter"

    /// <summary>Build the system prompt (agent instructions) for this context.</summary>
    Task<string> BuildSystemPromptAsync(ChatSession session, CancellationToken ct);

    /// <summary>Get the document text(s) for grounding.</summary>
    Task<IReadOnlyList<DocumentContext>> GetDocumentContextsAsync(ChatSession session, CancellationToken ct);

    /// <summary>Get available context modes (what the user can switch between).</summary>
    IReadOnlyList<ChatContextMode> GetAvailableModes();

    /// <summary>Determine which tools are available for this context.
    /// Returns AIFunction instances for Agent Framework tool registration.</summary>
    Task<ToolConfiguration> ResolveToolsAsync(ChatSession session, CancellationToken ct);

    /// <summary>Get predefined prompts specific to this context.</summary>
    Task<IReadOnlyList<PredefinedPrompt>> GetPredefinedPromptsAsync(
        ChatSession session, ChatContextMode mode, CancellationToken ct);

    /// <summary>Handle refinement requests (surface-specific behavior).</summary>
    Task<RefinementResult> HandleRefinementAsync(
        RefinementRequest request, ChatSession session, CancellationToken ct);
}
```

### 8.2 Context Implementations

| Context Provider | Surface | Document Source | Available Tools | Context Modes |
|-----------------|---------|-----------------|-----------------|---------------|
| `AnalysisChatContext` | AnalysisWorkspace PCF / side pane | Single doc from SPE | DocumentSearch, AnalysisQuery, Knowledge, Refinement, Comparison, Entity | Document, Analysis, Hybrid |
| `WorkspaceChatContext` | Workspace chat panel | Multiple docs from Matter | DocumentSearch, MatterTools, Knowledge, Entity | Matter, Documents, Activities |
| `DocumentStudioChatContext` | Document Studio right panel | DOCX from TipTap editor | DocumentSearch, Knowledge, Refinement (→ track changes), Comparison | Document, Analysis, Redline |
| `MatterChatContext` | Matter page / Power Apps side pane | All Matter documents | DocumentSearch (multi-doc), MatterTools, Knowledge | Matter Overview, Documents |
| `GenericDocumentChatContext` | Word add-in sidebar | Single doc from Word API | DocumentSearch (compact) | Document only |

### 8.3 Unified Chat API

A single set of API endpoints serves all surfaces. The context type is specified in the session:

```
/api/ai/chat/
├── POST /sessions                      # Create chat session
│   Body: { contextType, contextData, playbookId? }
│   Response: { sessionId, availableModes, predefinedPrompts }
│
├── POST /sessions/{sessionId}/message  # Send message (SSE streaming)
│   Body: { message, contextMode? }
│   Response: SSE stream (tokens, tool calls, citations)
│
├── POST /sessions/{sessionId}/refine   # Highlight-and-refine (SSE streaming)
│   Body: { selectedText, action, instruction?, context }
│   Response: SSE stream (refinement suggestion)
│
├── POST /sessions/{sessionId}/context  # Switch context mode
│   Body: { mode: "document" | "analysis" | "hybrid" | ... }
│   Response: { switched, availablePrompts }
│
├── GET  /sessions/{sessionId}/suggestions  # Get predefined prompts
│   Response: { prompts[] }
│
├── GET  /sessions/{sessionId}/history  # Paginated chat history
│   Query: ?page=1&pageSize=20
│   Response: { messages[], totalCount, hasSummary }
│
├── POST /sessions/{sessionId}/history/search  # Search history
│   Body: { query }
│   Response: { results[] }
│
└── DELETE /sessions/{sessionId}        # End session, cleanup cache
```

**Endpoint registration (ADR-001 Minimal API):**

```csharp
// Api/Ai/ChatEndpoints.cs
public static class ChatEndpoints
{
    public static void MapChatEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/ai/chat")
            .RequireAuthorization()
            .WithTags("Chat");

        group.MapPost("/sessions", CreateSession);
        group.MapPost("/sessions/{sessionId:guid}/message", SendMessage)
            .AddEndpointFilter<RateLimitFilter>("ai-stream")
            .Produces(200, contentType: "text/event-stream");
        group.MapPost("/sessions/{sessionId:guid}/refine", RefineText)
            .AddEndpointFilter<RateLimitFilter>("ai-stream")
            .Produces(200, contentType: "text/event-stream");
        group.MapPost("/sessions/{sessionId:guid}/context", SwitchContext);
        group.MapGet("/sessions/{sessionId:guid}/suggestions", GetSuggestions);
        group.MapGet("/sessions/{sessionId:guid}/history", GetHistory);
        group.MapPost("/sessions/{sessionId:guid}/history/search", SearchHistory);
        group.MapDelete("/sessions/{sessionId:guid}", EndSession);
    }
}
```

### 8.4 Client Component Interface

```typescript
// SprkChat/types.ts

export interface ISprkChatProps {
    /** Unique session identifier (analysisId, workspaceId, etc.) */
    sessionId: string;

    /** Context type — determines server-side behavior */
    contextType: "analysis" | "workspace" | "document-studio" | "document" | "matter";

    /** Context-specific data passed to the server */
    contextData: Record<string, unknown>;

    /** BFF API base URL */
    apiBaseUrl: string;

    /** Optional playbook ID for scoped prompts */
    playbookId?: string;

    /** Callback when AI suggests a text refinement (host surface handles application) */
    onRefinement?: (refinement: IRefinementResult) => void;

    /** Callback when AI references a document location (host surface scrolls/highlights) */
    onDocumentReference?: (reference: IDocumentReference) => void;

    /** Callback when analysis output is updated via chat */
    onAnalysisUpdate?: (update: IAnalysisUpdate) => void;

    /** Compact mode for constrained surfaces (Word add-in ~350px) */
    compact?: boolean;

    /** Theme override (inherits from Fluent provider by default) */
    theme?: "light" | "dark" | "auto";

    /** Initial context mode */
    defaultContextMode?: string;

    /** External selection — host surface can push highlighted text into chat */
    externalSelection?: IExternalSelection | null;
}

/** Host surface pushes selected text for AI refinement */
export interface IExternalSelection {
    text: string;
    source: "analysis-output" | "document-editor" | "document-preview";
    metadata?: {
        sectionHeading?: string;
        paragraphIndex?: number;
        clauseId?: string;
    };
}
```

### 8.5 How Each Surface Uses SprkChat

#### AnalysisWorkspace (Current PCF — Right Panel)

The existing AnalysisWorkspace chat panel is replaced by `SprkChat`:

```
┌──────────────────────────────────────────────────────────┐
│  Analysis Output  │  Document Preview  │  SprkChat      │
│  (left panel)     │  (center panel)    │  (right panel)  │
│                   │                    │                  │
│  User selects     │                    │  Receives        │
│  text ──────────────────────────────────► externalSelection│
│                   │                    │                  │
│  ◄── onAnalysisUpdate (working doc) ──── AI modifies     │
│                   │                    │  analysis via    │
│                   │  ◄── onDocumentReference ── citations │
│                   │  scrolls to clause │                  │
└──────────────────────────────────────────────────────────┘
```

#### Workspace (New — Chat Panel)

The Workspace is a new surface for matter-centric work. SprkChat is its AI assistant:

```
┌──────────────────────────────────────────────────────────┐
│  Workspace                                                │
│  ┌──────────────┐  ┌───────────────┐  ┌───────────────┐ │
│  │ Matter Nav   │  │ Active View   │  │ SprkChat     │ │
│  │              │  │ (Documents,   │  │               │ │
│  │ • Documents  │  │  Activities,  │  │ Context:      │ │
│  │ • Activities │  │  Invoices,    │  │ Matter +      │ │
│  │ • Timeline   │  │  etc.)        │  │ Documents +   │ │
│  │ • Invoices   │  │               │  │ Activities    │ │
│  │              │  │               │  │               │ │
│  │              │  │               │  │ "Summarize    │ │
│  │              │  │               │  │  all open     │ │
│  │              │  │               │  │  items for    │ │
│  │              │  │               │  │  this matter" │ │
│  └──────────────┘  └───────────────┘  └───────────────┘ │
└──────────────────────────────────────────────────────────┘
```

#### Document Studio (Browser — Right Panel)

Same `SprkChat` component, with `onRefinement` wired to TipTap track changes:

```typescript
<SprkChat
  sessionId={studioSessionId}
  contextType="document-studio"
  contextData={{ driveId, itemId, analysisId }}
  onRefinement={(refinement) => {
    // Apply AI suggestion as TipTap track change
    editor.chain()
      .setTrackChangeInsertion(refinement.suggestedText)
      .setTrackChangeDeletion(refinement.originalText)
      .run();
  }}
  onDocumentReference={(ref) => {
    // Scroll TipTap editor to referenced paragraph
    editor.commands.scrollToParagraph(ref.paragraphId);
  }}
/>
```

#### Word Add-in (Sidebar — Compact Mode)

```typescript
<SprkChat
  sessionId={wordSessionId}
  contextType="document"
  contextData={{ documentId }}
  compact={true}  // Hides context switcher, reduces prompt chips, smaller messages
/>
```

### 8.6 Build Plan (Not Migration — Clean Build)

Since we have no client deployments, the approach is:

1. **Build `SprkChat` as a shared component** in `@spaarke/ui-components`
2. **Build the BFF chat service** with `IChatContextProvider` pattern
3. **Implement `AnalysisChatContext`** first (replaces current chat in AnalysisWorkspace)
4. **Implement `WorkspaceChatContext`** in parallel (for the new Workspace)
5. **Remove old chat code** from `AnalysisOrchestrationService.ContinueAnalysisAsync()`, `AnalysisContextBuilder.BuildContinuationPromptWithContext()`, and the inline chat state in `AnalysisWorkspaceApp.tsx`
6. **Implement additional contexts** as those surfaces come online (Document Studio, Word add-in, Matter page)

---

## 9. API Endpoints

SprkChat uses a unified API under `/api/ai/chat/` (see Section 8.3 for full specification). The old analysis-specific chat endpoints (`/api/ai/analysis/{id}/continue`, `/api/ai/analysis/{id}/resume`) are **replaced entirely** — not wrapped or proxied.

### Endpoints Summary

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/ai/chat/sessions` | POST | Create chat session (any context type) |
| `/api/ai/chat/sessions/{id}/message` | POST (SSE) | Send message with streaming response |
| `/api/ai/chat/sessions/{id}/refine` | POST (SSE) | Highlight-and-refine with streaming |
| `/api/ai/chat/sessions/{id}/context` | POST | Switch context mode |
| `/api/ai/chat/sessions/{id}/suggestions` | GET | Get predefined prompts |
| `/api/ai/chat/sessions/{id}/history` | GET | Paginated chat history |
| `/api/ai/chat/sessions/{id}/history/search` | POST | Search within history |
| `/api/ai/chat/sessions/{id}` | DELETE | End session, cleanup |

### Removed Endpoints (Old Code Deleted)

| Old Endpoint | Replacement |
|-------------|-------------|
| `POST /api/ai/analysis/{id}/continue` | `POST /api/ai/chat/sessions/{id}/message` |
| `POST /api/ai/analysis/{id}/resume` | `POST /api/ai/chat/sessions` (create with existing history) |
| Analysis-specific save (chat portion) | Handled automatically by `ChatSessionManager` |

---

## 10. Implementation Phases

> **Approach**: Clean build — no feature flags, no migration. Build new, replace old, delete legacy code.
> **Parallelism**: Phase 1 builds the shared foundation. Phases 2A (Analysis surface) and 2B (Workspace surface) run in parallel on separate branches.

### Phase 1: Shared Foundation — SprkChat Component + BFF Service (5-6 weeks)

| Task | Layer | Estimate |
|------|-------|----------|
| Agent Framework NuGet packages + `IChatClient` DI registration | API | 2 days |
| `SprkChatAgent` + `SprkChatAgentFactory` (AIAgent creation) | API | 1 week |
| `IChatContextProvider` interface + context provider pattern | API | 3 days |
| `ChatEndpoints.cs` — unified chat API (sessions, message, refine, history) | API | 1 week |
| `ChatSessionManager` (Dataverse + Redis) | API | 3 days |
| `ChatHistoryManager` with summarization | API | 3 days |
| `sprk_aichatmessage` + `sprk_aichatsummary` Dataverse entities | Dataverse | 2 days |
| Core Agent Tools: `DocumentSearchTools`, `TextRefinementTools` | API | 1 week |
| Agent Middleware: telemetry, cost control, content safety | API | 3 days |
| **LlamaParse: `LlamaParseClient` + `DocumentParserRouter` + config** | **API** | **2 days** |
| **LlamaParse: Wire into existing `DocumentIntelligenceService` pipeline** | **API** | **1 day** |
| `SprkChat` React component (shared library) | UI | 1 week |
| `useSprkChat` hook (SSE streaming, state management) | UI | 3 days |
| Chat UI: messages, input, predefined prompts, context switch, citations | UI | 1 week |

**Deliverable**: Shared `SprkChat` component + BFF chat service ready for any surface to consume. Component published in `@spaarke/ui-components`. Built on Microsoft Agent Framework (RC/GA). LlamaParse integrated for complex document parsing.

### Phase 2A: Analysis Surface (3-4 weeks, parallel with 2B)

| Task | Layer | Estimate |
|------|-------|----------|
| `AnalysisChatContext` context provider | API | 3 days |
| `AnalysisQueryTools` (query structured findings) | API | 3 days |
| Context switching (Document / Analysis / Hybrid modes) | API + UI | 3 days |
| Integrate `SprkChat` into AnalysisWorkspace PCF (replace old chat) | PCF | 1 week |
| Highlight-and-refine: text selection handler + floating toolbar | PCF | 3 days |
| Refinement preview (diff view) with accept/reject | PCF | 3 days |
| Delete old chat code: `AnalysisContextBuilder`, inline chat state, `useSseStream` chat paths | Cleanup | 2 days |

**Deliverable**: AnalysisWorkspace uses SprkChat with context switching, predefined prompts, highlight-and-refine.

### Phase 2B: Workspace Surface (3-4 weeks, parallel with 2A)

| Task | Layer | Estimate |
|------|-------|----------|
| `WorkspaceChatContext` context provider | API | 3 days |
| `MatterTools` (query matter data, activities, related documents) | API | 1 week |
| Multi-document context: chat across multiple matter documents | API | 3 days |
| Integrate `SprkChat` into Workspace as chat panel | Workspace | 1 week |
| Workspace-specific predefined prompts | API | 2 days |

**Deliverable**: Workspace has an AI chat panel that understands matter context, documents, and activities.

### Phase 3: Knowledge Integration + Advanced Plugins (3-4 weeks)

| Task | Layer | Estimate |
|------|-------|----------|
| `KnowledgeRetrievalTools` (RAG search) | API | 1 week |
| `ClauseComparisonTools` (deviation detection) | API | 1 week |
| `EntityLookupTools` | API | 3 days |
| Citation rendering in chat messages (clickable references) | UI | 3 days |
| Confidence scoring display | UI | 2 days |
| Proactive insights filter | API | 3 days |

**Deliverable**: RAG-grounded, citation-rich chat with tool-based reasoning across all surfaces.

### Phase 4: Document Studio + Word Add-in Surfaces (2-3 weeks)

| Task | Layer | Estimate |
|------|-------|----------|
| `DocumentStudioChatContext` context provider | API | 3 days |
| Wire `SprkChat.onRefinement` → TipTap track changes | Studio | 3 days |
| `GenericDocumentChatContext` for Word add-in | API | 2 days |
| Compact mode for 350px sidebar | UI | 2 days |
| Cross-surface testing (same session accessed from different surfaces) | Testing | 3 days |

**Deliverable**: Document Studio and Word add-in both use SprkChat.

### Phase 5: Polish + Advanced Features (2-3 weeks)

| Task | Layer | Estimate |
|------|-------|----------|
| Conversation branching (what-if exploration) | API + UI | 1 week |
| Export chat as formatted report | API + UI | 3 days |
| Dynamic prompt generation (AI suggests next questions) | API | 3 days |
| Performance optimization (large history, long documents) | All | 3 days |

**Deliverable**: Production-ready chat with advanced features.

### Total: 15-20 weeks (unchanged — LlamaParse absorbed into Phase 1)

```
Week:  1  2  3  4  5  6  7  8  9  10  11  12  13  14  15  16
       ├──────────────────────┤
       Phase 1: Shared Foundation + LlamaParse (5-6 weeks)
                              ├────────────────┤
                              Phase 2A: Analysis (3-4 weeks)
                              ├────────────────┤
                              Phase 2B: Workspace (3-4 weeks, PARALLEL)
                                                ├──────────────┤
                                                Phase 3: Knowledge (3-4 weeks)
                                                               ├──────────┤
                                                               Phase 4: Studio + Word (2-3 weeks)
                                                                          ├──────────┤
                                                                          Phase 5: Polish (2-3 weeks)
```

### Schedule Impact Assessment: LlamaParse

| Item | Impact | Why |
|------|--------|-----|
| **Added tasks** | +3 days (LlamaParseClient, DocumentParserRouter, wiring) | REST API client + router is straightforward .NET work |
| **Phase 1 duration** | **No change** (5-6 weeks) | 3 days absorbed into Phase 1 — runs parallel with other API tasks |
| **Overall timeline** | **No change** (15-20 weeks) | LlamaParse is upstream of chat; doesn't affect chat component work |
| **Architectural complexity** | **Minimal** | Router pattern, REST client, unified output model — all standard .NET patterns |
| **Risk** | **Low** | LlamaParse is a cloud API. If it's down, router falls back to Azure Doc Intel. No single point of failure. |
| **Dependencies** | **None on critical path** | LlamaParse can be integrated any time in Phase 1. Chat works with either parser. |

**Why no schedule impact**: LlamaParse is a thin REST client + a router. The `LlamaParseClient.cs` is ~50 lines (upload file, poll for result). The `DocumentParserRouter.cs` is ~30 lines (check doc type, route). This work runs in parallel with the Agent Framework setup and SprkChat component work. The 3 days fit within the existing Phase 1 buffer.

**Fallback guarantee**: Because the router pattern means LlamaParse is optional, we can ship Phase 1 with Azure Doc Intel only and add LlamaParse at any point. There is zero risk of LlamaParse blocking the schedule.

---

## 11. Success Criteria

| Metric | Current | Target | How Measured |
|--------|---------|--------|-------------|
| Answer accuracy (grounded in document) | ~70% | > 90% | Human evaluation on test set |
| Document parsing accuracy (tables, structure) | ~60-70% pass-through | > 90% (with LlamaParse) | Compare parser outputs on test corpus |
| Response includes source citation | 0% | > 80% | Automated citation check |
| User satisfaction (chat usefulness) | Baseline TBD | +40% improvement | In-app feedback |
| Context switch adoption | N/A | > 50% of sessions use it | Analytics |
| Highlight-and-refine adoption | N/A | > 30% of sessions use it | Analytics |
| Chat sessions per analysis | ~3 messages avg | > 8 messages avg | Analytics |
| History resumption success | ~60% (in-memory loss) | > 99% | Monitoring |
| Response latency (first token) | ~2s | < 2s (including tool calls) | P95 metrics |
| LlamaParse routing accuracy | N/A | > 95% correct routing | Monitor false positives (simple → LlamaParse) |

---

## 12. Open Questions

1. **Structured Analysis Output**: Should the initial analysis also output structured JSON alongside markdown? This would make the Analysis context mode much more powerful (agent can query specific findings by ID). The current working document is markdown only. **Recommendation**: Yes — dual output (markdown for display + JSON for agent queries).

2. **Tool Call Visibility**: Should users see when the agent calls tools (e.g., "Searching knowledge base..." indicator)? Harvey shows thinking steps; CoCounsel shows "Researching..." stages. Both approaches increase user trust. **Recommendation**: Yes — show tool call indicators. The `ToolCallIndicator` component is already in the design.

3. **Multi-Document Chat in Workspace**: The Workspace context inherently involves multiple documents per matter. How should the `DocumentSearchTools` handle searching across N documents? Should there be a per-matter aggregate index, or should the tools search each document's index sequentially?

4. **Playbook Prompt Templates**: Should playbooks define custom system prompt templates for chat? A playbook-specific chat prompt could dramatically improve accuracy for specialized workflows (e.g., NDA review chat vs. invoice processing chat). **Recommendation**: Yes — add a `sprk_aiplaybook.ChatSystemPrompt` field.

5. **Export Chat as Report**: Users may want to export a chat conversation as a formatted report (e.g., "Q&A Summary for [Document Name]"). **Recommendation**: Include in Phase 5 as part of the `ExportTools`.

6. **SprkChat Side Pane Invocation**: For Dataverse model-driven apps, should SprkChat be deployed as a custom page side pane (invocable via `Xrm.App.sidePanes.createPane()`), or embedded directly into form sections? Side pane is more flexible but requires the new Custom Pages feature.

7. **Session Sharing Across Surfaces**: If a user starts a chat in the AnalysisWorkspace, then opens the same analysis in Document Studio, should they see the same chat session? The `sessionId` pattern supports this, but we need to decide if it's desired behavior.

8. **Voice Input**: Should we plan for voice-to-text input in the chat? Several competitors (Harvey, CoCounsel) are adding voice interaction for hands-free document review.

9. **Agent Framework GA Timing**: Agent Framework RC shipped February 19, 2026 with GA targeted end of Q1 2026. Our Phase 1 starts immediately. If GA slips, the RC API surface is declared stable — risk is low but we should monitor the [GitHub releases](https://github.com/microsoft/agent-framework/releases).

10. **Multi-Provider Testing**: With Agent Framework's `IChatClient` supporting Azure OpenAI, OpenAI, Claude, and Bedrock — should we test chat quality across providers during Phase 1? Different models may handle tool calling and document grounding differently. **Recommendation**: Test at minimum Azure OpenAI (gpt-4o) and one alternative provider in Phase 1.

11. **LlamaParse Tier Selection**: The router currently uses document type and table presence to choose between "cost_effective" and "agentic" tiers. Should we allow playbooks to specify a parsing tier? An NDA Review playbook might always want "agentic" for highest accuracy, while a Quick Summary playbook might use "cost_effective" to save credits. **Recommendation**: Yes — add `ParsingTier` to playbook scope configuration.

12. **LlamaParse for Existing Documents**: Should we re-parse previously uploaded documents through LlamaParse to improve existing analysis quality? This would be a batch operation. **Recommendation**: Offer as an admin action ("Re-analyze with enhanced parsing") rather than automatic — respects customer cost preferences.

---

## Appendix A: Agent Framework Tool Examples

### DocumentSearchTools

```csharp
using Microsoft.Extensions.AI;

/// <summary>
/// Document search tools for the SprkChat agent.
/// Agent Framework: plain methods with [Description] — no [KernelFunction] or plugin factory needed.
/// Registered via AIFunctionFactory.Create(method) at agent creation time.
/// </summary>
public class DocumentSearchTools
{
    private readonly AnalysisSession _session;
    private readonly IRagService _ragService;

    [Description("Search the original document for specific text, clauses, or sections. " +
                 "Use this when the user asks about specific parts of the document.")]
    public async Task<string> SearchDocumentAsync(
        [Description("What to search for in the document")] string query,
        [Description("Number of results (1-10)")] int maxResults = 3)
    {
        var results = await _ragService.SearchAsync(
            indexName: $"analysis-doc-{_session.AnalysisId}",
            query: query,
            maxResults: maxResults);

        var sb = new StringBuilder();
        foreach (var result in results)
        {
            sb.AppendLine($"[Section: {result.Section}, Para: {result.ParagraphId}, " +
                         $"Score: {result.Score:F2}]");
            sb.AppendLine(result.Content);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    [Description("Get document metadata including title, type, parties, dates, and page count")]
    public Task<string> GetDocumentMetadataAsync()
    {
        var meta = _session.DocumentMetadata;
        return Task.FromResult($"""
            Title: {meta.Title}
            Type: {meta.DocumentType}
            Pages: {meta.PageCount}
            Parties: {string.Join(", ", meta.Parties)}
            Key Dates: {string.Join(", ", meta.KeyDates.Select(d => $"{d.Label}: {d.Value}"))}
            """);
    }
}

// Registration at agent creation — Agent Framework simplifies this dramatically:
var searchTools = new DocumentSearchTools(session, ragService);
AIAgent agent = chatClient.AsAIAgent(
    instructions: systemPrompt,
    tools: [
        AIFunctionFactory.Create(searchTools.SearchDocumentAsync),
        AIFunctionFactory.Create(searchTools.GetDocumentMetadataAsync),
        // ... other tools
    ]
);
```

### Agent Framework vs Semantic Kernel — Side-by-Side

```csharp
// ❌ OLD (Semantic Kernel) — what we are NOT building
var kernel = Kernel.CreateBuilder()
    .AddAzureOpenAIChatCompletion(deploymentName, endpoint, credential)
    .Build();
kernel.Plugins.AddFromType<DocumentSearchPlugin>();
var agent = new ChatCompletionAgent
{
    Name = "SprkAssistant",
    Instructions = systemPrompt,
    Kernel = kernel,
    Arguments = new KernelArguments(new OpenAIPromptExecutionSettings
    {
        ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
    })
};
await foreach (var update in agent.InvokeStreamingAsync(message, thread)) { ... }

// ✅ NEW (Agent Framework) — what we ARE building
IChatClient chatClient = new AzureOpenAIChatClient(endpoint, credential, deploymentName);
AIAgent agent = chatClient.AsAIAgent(
    instructions: systemPrompt,
    tools: [AIFunctionFactory.Create(searchTools.SearchDocumentAsync)]
);
AgentSession session = await agent.CreateSessionAsync();
await foreach (var update in agent.RunStreamingAsync(message, session)) { ... }
```

### TextRefinementTools

```csharp
public class TextRefinementTools
{
    [Description("Refine a passage of text from the analysis. " +
                 "Use when the user wants to improve, expand, simplify, or strengthen specific text.")]
    public async Task<string> RefineTextAsync(
        [Description("The text to refine")] string originalText,
        [Description("How to refine: 'improve_clarity', 'expand_detail', 'simplify', " +
                     "'strengthen', 'add_citations', 'make_formal', 'make_concise'")]
        string refinementType,
        [Description("Additional instructions from the user")] string? userInstruction = null)
    {
        // Implementation calls AI provider via IChatClient with targeted refinement prompt
        // Returns refined text with change explanation
    }

    [Description("Update a section of the analysis working document. " +
                 "Use when the user wants to modify the analysis output.")]
    public async Task<string> UpdateAnalysisSectionAsync(
        [Description("Section heading to update")] string sectionHeading,
        [Description("New content for the section")] string newContent,
        [Description("'replace' to overwrite, 'append' to add")] string mode = "replace")
    {
        // Updates the working document and returns confirmation
    }
}
```

### KnowledgeRetrievalTools

```csharp
public class KnowledgeRetrievalTools
{
    [Description("Search the organization's knowledge base for standards, best practices, " +
                 "regulatory requirements, or precedents. Use when comparing document terms " +
                 "against organizational standards or when the user asks about best practices.")]
    public async Task<string> SearchKnowledgeBaseAsync(
        [Description("What to search for")] string query,
        [Description("Filter: 'standards', 'regulatory', 'best-practices', 'precedents', 'all'")]
        string category = "all")
    {
        // Searches R5 knowledge index with optional category filter
        // Returns formatted results with source attribution
    }

    [Description("Compare a specific clause or term from the document against the " +
                 "organization's standard position. Returns the standard language " +
                 "and highlights deviations.")]
    public async Task<string> CompareToStandardAsync(
        [Description("The clause or term text to compare")] string clauseText,
        [Description("Type of clause: 'indemnification', 'termination', 'payment', " +
                     "'confidentiality', 'ip_rights', 'liability', 'other'")]
        string clauseType)
    {
        // Retrieves org standard for this clause type
        // Compares semantically and returns deviation analysis
    }
}
```

---

## Appendix B: Console Log Trace (Annotated)

Based on the provided console output, here's the current flow annotated with Agent Framework improvements:

```
CURRENT FLOW:
[AnalysisWorkspaceApp] Chat message sent
  → {id: 'msg-1771618595209', role: 'user', content: 'can you analyze the document'}

[useSseStream] Starting stream for analysis: 94e08fb1-...
  → POST /api/ai/analysis/{id}/continue
  → Body: { message: "can you analyze the document" }

[useSseStream] Auth token acquired
  → Bearer token from MSAL

[AnalysisWorkspaceApp] Auto-saving chat history (1 messages)
  → Writes JSON to sprk_analysis.ChatHistory
  → PROBLEM: Only 1 message at this point, saves before response

[useSseStream] Received done signal
  → 2408 chars received (entire response)
  → PROBLEM: No tool calls, no citation, no structured output

[AnalysisWorkspaceApp] Auto-saving chat history (2 messages)
  → Now saves user + assistant messages
  → PROBLEM: Entire history re-serialized and re-saved

AGENT FRAMEWORK FLOW (same console output format):
[AnalysisChatAgent] Chat message received
  → {id: 'msg-...', role: 'user', content: 'can you analyze the document'}

[AnalysisChatAgent] Building AIAgent for session
  → Tools: [DocumentSearch, AnalysisQuery, EntityLookup, KnowledgeRetrieval]
  → Context: Hybrid (document + analysis available)

[AnalysisChatAgent] Loading managed history
  → Summary: "No prior conversation" (new session)
  → Recent messages: 0

[AnalysisChatAgent] Executing with function calling enabled

[AnalysisChatAgent] Tool call: search_document("key terms entities parties")
  → Found 5 relevant sections

[AnalysisChatAgent] Tool call: get_document_metadata()
  → Type: Engagement Letter, Parties: ACME Corp, Ralph Schroeder

[AnalysisChatAgent] Streaming response (with citations)
  → 3200 chars, 8 citations, structured findings JSON

[ChatHistoryManager] Persisting message (sequence: 1, role: user)
  → Written to sprk_aichatmessage

[ChatHistoryManager] Persisting message (sequence: 2, role: assistant)
  → Written to sprk_aichatmessage
  → Metadata: { toolCalls: 2, citations: 8, tokens: { input: 1240, output: 890 } }

[AnalysisChatAgent] Suggestions generated:
  → "What are the key obligations for each party?"
  → "Summarize the scope of work"
  → "Are there any risk factors in this engagement?"
```

---

*End of Design Document*
