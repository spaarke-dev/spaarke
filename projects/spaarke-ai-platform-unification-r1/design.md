# Spaarke AI Platform Unification — Research Analysis & Design

> **Project**: spaarke-ai-platform-unification-r1
> **Status**: Research / Design
> **Priority**: High (Strategic)
> **Last Updated**: 2026-05-15

---

## 1. Executive Summary

Unify Spaarke's AI capabilities into a cohesive platform that can be deployed across three surfaces — embedded in the Model-Driven App (current), as a standalone Code Page app, and integrated with Microsoft AI Foundry + Agent Framework for enhanced capabilities and broader reach. This builds on the existing SprkChat agent, playbook engine, and Analysis Workspace rather than replacing them.

---

## 2. Current Spaarke AI Architecture (Inventory)

### What We Have Today

**BFF AI Services (27 files in `Services/Ai/Chat/`):**

| Component | Purpose | Status |
|-----------|---------|--------|
| `SprkChatAgent` | Core conversational AI agent | Production |
| `SprkChatAgentFactory` | Creates agent instances per session | Production |
| `ISprkChatAgent` interface | Decorator/middleware pattern | Production |
| `ChatSessionManager` | Session lifecycle, history | Production |
| `ChatHistoryManager` | Message history persistence | Production |
| `ChatContextMappingService` | Resolves context per surface | Production |
| `AnalysisChatContextResolver` | Analysis-specific context | Production |
| `PlaybookChatContextProvider` | Playbook-scoped context | Production |
| `PlaybookDispatcher` | Routes to playbook execution | Production |
| `DynamicCommandResolver` | Slash command resolution | Production |
| `CompoundIntentDetector` | Multi-tool intent detection | Production |
| `PendingPlanManager` | User approval for compound actions | Production |

**Agent Middleware Pipeline:**

| Middleware | Purpose |
|-----------|---------|
| `AgentTelemetryMiddleware` | Request/response logging, latency tracking |
| `AgentCostControlMiddleware` | Token budget enforcement |
| `AgentContentSafetyMiddleware` | Content filtering |

**7 Tool Categories:**

| Tool | Capabilities |
|------|-------------|
| `AnalysisExecutionTools` | Run playbook analysis, execute scoped analysis |
| `AnalysisQueryTools` | Query analysis results, search analyses |
| `DocumentSearchTools` | Semantic search, find similar documents |
| `KnowledgeRetrievalTools` | RAG retrieval from knowledge bases |
| `TextRefinementTools` | Rewrite, expand, summarize selected text |
| `WebSearchTools` | Scope-guided web search |
| `WorkingDocumentTools` | Read/write to the analysis editor |

**Playbook Engine (12 node executors):**

| ActionType | Executor | Status |
|------------|----------|--------|
| 0 | AiAnalysis | Production |
| 1 | AiCompletion | Production |
| 20 | CreateTask | Production |
| 21 | SendEmail | Production |
| 22 | UpdateRecord | Production |
| 30 | Condition | Production |
| 40 | DeliverOutput | Production |
| 41 | DeliverToIndex | Production |
| 50 | CreateNotification | Production |
| — | QueryDataverse | Production |

**Microsoft SDK Dependencies (already in BFF):**
- `Microsoft.Extensions.AI` 10.3.0 — `IChatClient` abstraction
- `Microsoft.Extensions.AI.OpenAI` 10.3.0 — OpenAI bridge
- `Microsoft.Agents.Hosting.AspNetCore` 1.0.1 — M365 Agents SDK
- `Microsoft.Agents.AI` 1.0.0-rc1 — Agent AI abstractions

**Existing Azure AI Infrastructure:**
- Azure AI Foundry Hub (`sprkspaarkedev-aif-hub`)
- Azure AI Foundry Project (`sprkspaarkedev-aif-proj`)
- Prompt Flows (analysis-execute, analysis-continue)
- Azure OpenAI (`spaarke-openai-dev`) — GPT-4o, embeddings
- Azure AI Search (`spaarke-search-dev`) — semantic index
- Document Intelligence (`spaarke-docintel-dev`)

**Frontend Components:**
- `SprkChat` — shared React component in `@spaarke/ui-components`
- `AnalysisWorkspace` — unified 3-panel Code Page (editor + viewer + chat)
- `PlaybookBuilder` — React Flow canvas for playbook authoring
- `PlaybookLibrary` — playbook catalog and management

---

## 3. Microsoft AI Platform Landscape (March 2026)

### Azure AI Foundry (formerly Azure AI Studio)

| Capability | What It Does | Spaarke Relevance |
|-----------|-------------|-------------------|
| **AI Foundry Hub** | Central management for AI projects, connections, deployments | Already provisioned (`sprkspaarkedev-aif-hub`) |
| **AI Foundry Project** | Scoped workspace for prompt flows, evaluations, models | Already provisioned (`sprkspaarkedev-aif-proj`) |
| **Model Catalog** | Deploy OpenAI, Phi, Mistral, Llama, custom models | Currently using GPT-4o; could add specialized models |
| **Prompt Flow** | Visual prompt orchestration (DAG of LLM calls) | Already have 2 flows; could replace/augment playbook nodes |
| **AI Agent Service** | Managed agent runtime (threads, tools, file search, code interpreter) | **Key integration target** — could host SprkChat agent logic |
| **Evaluation** | Automated quality evaluation (groundedness, relevance, coherence) | Needed for playbook quality assurance |
| **Content Safety** | Content filtering, prompt injection detection | Already have middleware; could move to managed service |
| **Tracing** | OpenTelemetry-based AI trace collection | Needed for production debugging |

### Azure AI Agent Service (GA March 2026)

This is the most relevant new capability. It provides a **managed agent runtime** similar to OpenAI Assistants API but with enterprise features:

```
Azure AI Agent Service
├── Agent Definition
│   ├── Model (GPT-4o, custom)
│   ├── Instructions (system prompt)
│   ├── Tools
│   │   ├── Function calling (your BFF functions)
│   │   ├── Code Interpreter (Python sandbox)
│   │   ├── File Search (built-in RAG)
│   │   └── Bing Grounding (web search)
│   └── Knowledge (uploaded files, indexes)
│
├── Thread (conversation session)
│   ├── Messages (user + assistant turns)
│   └── Annotations (citations, file refs)
│
├── Run (execution of agent on thread)
│   ├── Streaming (SSE)
│   ├── Tool calls (function invocation)
│   └── Run steps (observable execution trace)
│
└── Enterprise Features
    ├── Managed Identity (no secrets in code)
    ├── VNet integration
    ├── Content Safety (built-in)
    ├── Tracing (OpenTelemetry)
    └── Multi-model (swap models without code change)
```

**Key question**: Should SprkChat's `SprkChatAgent` be replaced by or integrated with Azure AI Agent Service?

### Microsoft.Extensions.AI (Already Using)

You're already on this abstraction — `IChatClient` decouples from specific providers:

```csharp
// Current: Azure OpenAI via IChatClient
IChatClient chatClient = new AzureOpenAIClient(...)
    .GetChatClient("gpt-4o")
    .AsIChatClient();

// Future: Could swap to AI Foundry Agent, local model, or Semantic Kernel
IChatClient chatClient = new AgentServiceChatClient(agentId, threadId);
```

### Microsoft 365 Agents SDK (Already Using)

You have `Microsoft.Agents.Hosting.AspNetCore` 1.0.1 and the M365 Copilot integration project. The Custom Engine Agent (`SpaarkeAgentHandler`) exposes your AI capabilities through M365 surfaces.

### Semantic Kernel

| Feature | What It Adds | Already Have Equivalent? |
|---------|-------------|------------------------|
| Plugins (functions) | Typed tool definitions | Yes — `Tools/*.cs` with `[Description]` attributes |
| Planners | Multi-step tool orchestration | Yes — `PlaybookOrchestrationService` with DAG |
| Memory | Conversation + semantic memory | Partially — `ChatHistoryManager` + AI Search |
| Connectors | Pre-built integrations (Graph, Search, etc.) | Custom implementations in BFF |
| Agents (experimental) | Multi-agent orchestration | Not yet — future possibility |

**Assessment**: Semantic Kernel would be a **lateral move**, not an upgrade. You've already built the equivalent capabilities purpose-built for legal operations. However, SK's agent orchestration layer (multi-agent) could be valuable for future complex workflows.

---

## 4. Strategic Options Analysis

### Option A: Keep Current Architecture, Enhance Incrementally

```
SprkChat (current) ──► Add surfaces (standalone app, M365)
PlaybookEngine (current) ──► Add node types, improve scheduling
BFF API (current) ──► Add agent gateway endpoints
```

**Pros**: Low risk, preserves investment, predictable timeline
**Cons**: Miss platform capabilities (code interpreter, built-in RAG, multi-agent)

### Option B: Hybrid — Keep Core, Integrate AI Foundry Agent Service for Enhanced Capabilities

```
SprkChat ──► Hosts in both MDA and standalone
  │
  ├── Simple queries ──► Current IChatClient (direct Azure OpenAI)
  │
  └── Complex tasks ──► Route to AI Agent Service
      ├── Code Interpreter (Python analysis, chart generation)
      ├── File Search (managed RAG — alternative to your AI Search)
      ├── Multi-model (GPT-4o for reasoning, Phi for classification)
      └── Your BFF functions registered as agent tools
```

**Pros**: Best of both — keep your specialized legal ops AI, gain platform capabilities for complex tasks
**Cons**: Two runtime paths to maintain, complexity in routing decisions

### Option C: Migrate Core to AI Agent Service

```
AI Agent Service hosts the "Spaarke Legal AI Agent"
  ├── Agent definition includes all 7 tool categories
  ├── Thread = chat session
  ├── Run = message processing
  ├── Built-in: code interpreter, file search, content safety
  └── BFF becomes thin adapter (no LLM orchestration)
```

**Pros**: Managed infrastructure, built-in features, Microsoft roadmap alignment
**Cons**: High migration risk, lose custom middleware, playbook engine doesn't fit Agent Service model

### Recommendation: Option B (Hybrid)

**Keep your core (SprkChat agent, playbook engine, tools) and add AI Agent Service as a capability provider for enhanced tasks.** Here's why:

1. **Your playbook engine has no equivalent in Agent Service** — DAG orchestration, parallel batching, node executor framework, notification generation. Agent Service does single-agent, single-thread. Your engine does multi-node, multi-step, multi-output.

2. **Your tool framework is more sophisticated** — compound intent detection, pending plan approval, context-scoped tool registration. Agent Service has basic function calling.

3. **Agent Service adds capabilities you don't have** — code interpreter (Python sandbox for data analysis, chart generation), managed file search (could supplement your AI Search), and built-in content safety without your middleware.

4. **The integration is clean** — register an `AgentServiceNodeExecutor` (new node type) that delegates to AI Agent Service for specific tasks. Your playbook engine orchestrates when to call it.

---

## 5. What We Leverage vs What We Build

### Leverage Matrix (Existing → Reuse)

Every item in this column is **production code that carries forward unchanged**:

| Existing Component | How It's Used in This Project | Changes Needed |
|-------------------|------------------------------|----------------|
| **SprkChat React component** | Same component renders in standalone Code Page (Surface 2) | **None** — accepts props, renders chat |
| **SprkChatAgent + middleware pipeline** | Handles all chat messages, tool dispatch, streaming | **None** — standalone surface calls same BFF endpoints |
| **7 tool categories** (search, analysis, refinement, etc.) | Available in standalone mode just as they are in Analysis Workspace | **None** — tools are context-scoped by BFF, not by frontend |
| **PlaybookOrchestrationService** (DAG engine) | Playbook execution from standalone context | **None** — add new node executor, engine unchanged |
| **ChatSessionManager + ChatHistoryManager** | Session persistence for standalone conversations | **None** — sessions are already surface-agnostic |
| **DynamicCommandResolver** | Slash commands work in standalone mode | **None** — commands resolve from playbook + context |
| **CompoundIntentDetector + PendingPlanManager** | Multi-tool approval works in standalone | **None** — approval flow is in BFF, not frontend |
| **QuickActionChips + SlashCommandMenu** | Render in standalone header area | **None** — driven by context mapping response |
| **SprkChatUploadZone** | Document upload in standalone | **None** — already a standalone React component |
| **ChatEndpoints** (SSE streaming) | Frontend calls same endpoints | **None** — surface-agnostic |
| **`@spaarke/auth` bootstrap** | Auth initialization for standalone Code Page | **None** — same pattern as all other Code Pages |
| **Playbook Library** | User selects playbook in standalone header | **None** — fetch from existing `/api/ai/playbooks` endpoint |
| **AI Search semantic index** | Document search from standalone chat | **None** — called via existing DocumentSearchTools |
| **Azure OpenAI** | LLM calls for chat, analysis, refinement | **None** — IChatClient abstraction unchanged |
| **Azure AI Foundry Hub + Project** | Already provisioned infrastructure for Agent Service | **None** — add agent definition to existing project |

**Summary**: ~80% of the work is **wiring existing components to new surfaces**, not building new capabilities.

### Build Matrix (New Code)

| New Component | What It Does | Estimated Effort |
|--------------|-------------|-----------------|
| **`sprk_spaarkeai` Code Page** | New React Code Page — layout shell, header, context provider | 2-3 days |
| **`StandaloneAiContext`** | React context provider that resolves entity context from URL params (no editor) | 1-2 days |
| **`StandaloneChatContextProvider`** | BFF service — resolves playbooks/actions/knowledge for non-analysis context | 2-3 days |
| **`GET /api/ai/chat/context-mappings/standalone`** | New BFF endpoint — returns context for standalone surface | 1 day |
| **`AgentServiceClient`** | Wrapper for Azure.AI.Projects SDK | 2-3 days |
| **`AgentServiceNodeExecutor` (AT 60)** | New playbook node executor — delegates to AI Agent Service | 1-2 days |
| **`CodeInterpreterBridge`** | Routes data analysis requests to Agent Service code interpreter | 2-3 days |
| **`CodeInterpreterTools.cs`** | New SprkChat tool — `analyze_data`, `generate_chart` | 1-2 days |
| **`LegalResearchTools.cs`** | New SprkChat tool — `research_case_law`, `research_company` | 1-2 days |
| **`AgentServiceRoutingMiddleware`** | Decides when to route to Agent Service vs direct | 2-3 days |
| **`ResultsPanel` component** | Slide-out panel for rich results (charts, research) | 2-3 days |
| **`ChatHistoryPanel` component** | Session history list for standalone mode | 1-2 days |
| **Agent definition + tool mapping** | Deploy Spaarke Legal AI Agent to Foundry | 1-2 days |
| **Evaluation pipeline** | Foundry evaluation config + legal metrics | 2-3 days |
| **Tracing integration** | OpenTelemetry spans for agent routing | 1-2 days |

**Total new code**: ~3-4 weeks for Phase 1 (standalone) + ~3-4 weeks for Phase 2 (Foundry integration)

---

## 6. User Experience Vision

### What Does This Look Like?

#### Surface 1: Analysis Workspace (Current — No Change)

The user opens a document for deep AI analysis. The three-panel layout stays exactly as built:

```
┌──────────────────────────────┬──────────────┬────────────────────────┐
│ Analysis Editor              │ Document     │ SprkChat               │
│                              │ Viewer       │                        │
│ [AI-generated analysis       │              │ "Summarize the key     │
│  output, editable,           │ [Original    │  risks in this NDA"    │
│  streaming write target]     │  document    │                        │
│                              │  reference]  │ 🤖 Based on the NDA,  │
│ Risk Assessment:             │              │ there are 3 key risks: │
│ 1. Non-compete clause...     │              │ 1. Non-compete is...   │
│ 2. Indemnification...        │              │ 2. Indemnification...  │
│                              │              │                        │
│ [Save] [Export] [Copy]       │              │ [/search] [/refine]    │
└──────────────────────────────┴──────────────┴────────────────────────┘
```

**No changes here.** SprkChat is embedded via `AnalysisAiContext` with editor integration, streaming write-back, and inline toolbar. This is the "deep work" surface.

#### Surface 2: Standalone Spaarke AI — Three-Pane Layout (NEW)

The standalone app uses the same three-pane pattern as the Analysis Workspace, but the panes serve different roles and are context-adaptive. The three panes are:

1. **Chat Pane** (left) — conversational AI, always visible
2. **Output/Work Pane** (center) — rich interactive components matched to the current task
3. **Research/Source Pane** (right) — reference material, source documents, web research — collapsible

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│ ⚡ Spaarke AI       Acme Corp Matter ▾     Risk Assessment ▾      [History] [⋮]  │
├────────────────────┬───────────────────────────────┬─────────────────────────────┤
│ CHAT               │ OUTPUT / WORK                 │ RESEARCH / SOURCE      [◀]  │
│                    │                               │                             │
│ (always visible)   │ (purpose-built widgets        │ (source documents,          │
│                    │  matched to current task)     │  web search results,        │
│                    │                               │  legal library,             │
│                    │                               │  collapsible)               │
├────────────────────┼───────────────────────────────┼─────────────────────────────┤
│ Draggable splitter │ Draggable splitter            │                             │
└────────────────────┴───────────────────────────────┴─────────────────────────────┘
```

**Key principles:**
- All three panes are **reachable by the chat** — the AI can write to the output pane, load a document in the source pane, or update both simultaneously
- The output pane renders **purpose-built React components** from a registry, not markdown — these are equivalent to MCP App widgets but built natively with Fluent UI v9
- The research/source pane collapses when not needed (e.g., simple Q&A) and expands when the AI needs to show reference material
- Panes use the same `PanelSplitter` component from `@spaarke/ui-components` as the Analysis Workspace — draggable, keyboard-accessible, collapsible

**How the panes map across tasks:**

| User Task | Chat Pane | Output Pane | Source Pane |
|-----------|-----------|-------------|-------------|
| **General Q&A** | Conversation | (collapsed — not needed) | (collapsed) |
| **Document analysis** | AI discussion | Analysis output (editable) | Source document viewer |
| **Contract drafting** | AI drafting assistant | Working document (editable) | Reference documents, templates |
| **Budget review** | AI narrative + alerts | Budget dashboard, charts | Invoice details, historical data |
| **Legal research** | AI research discussion | Research report (structured) | Web sources, case law citations |
| **Document comparison** | AI analysis narrative | Side-by-side diff view | (collapsed — diff IS the output) |
| **Invoice approval** | AI explanation of items | Approval form (interactive) | Supporting documents |
| **Playbook execution** | Streaming playbook output | Structured results, export | Source documents used in analysis |

**Example: Document Analysis with all three panes active:**

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│ ⚡ Spaarke AI       Acme Corp Matter ▾     Risk Assessment ▾      [History] [⋮]  │
├────────────────────┬───────────────────────────────┬─────────────────────────────┤
│                    │                               │                             │
│  👤 "Analyze the   │  Risk Assessment              │  📄 NDA_Acme_v3.pdf        │
│  key risks in the │  ─────────────────             │  ─────────────────          │
│  Acme NDA"        │                               │  Page 4 of 12              │
│                    │  1. NON-COMPETE CLAUSE        │                             │
│  🤖 I've analyzed │     The non-compete in        │  ┌─────────────────────┐    │
│  the NDA and found│     Section 7.2 restricts     │  │ 7.2 Non-Competition │    │
│  3 key risk areas.│     competition for 24        │  │ During the term of  │    │
│  The analysis is  │     months post-termination.  │  │ this Agreement and  │    │
│  in the output    │     Risk: Overly broad        │  │ for 24 months after │    │
│  pane with source │     geographic scope.         │  │ termination, the    │    │
│  references.      │     [See source: Section 7.2] │  │ Receiving Party...  │    │
│                    │                               │  └─────────────────────┘    │
│  Would you like me│  2. INDEMNIFICATION           │                             │
│  to suggest       │     Unlimited indemnification │  [Highlight cited text]      │
│  revisions to the │     in Section 12.1 creates   │                             │
│  non-compete      │     uncapped liability.       │                             │
│  clause?          │     [See source: Section 12.1]│                             │
│                    │                               │                             │
│  [/revise] [/comp]│  3. GOVERNING LAW             │                             │
│                    │     Delaware law (Sec 15.3)   │                             │
│  💬 _____________ │     may conflict with CA ops. │                             │
│                    │                               │                             │
│                    │  [Save] [Export] [Copy]       │  [Download] [Open Full]     │
└────────────────────┴───────────────────────────────┴─────────────────────────────┘
```

**Example: Budget dashboard (output pane only, source pane collapsed):**

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│ ⚡ Spaarke AI       All Matters ▾              General ▾           [History] [⋮]  │
├────────────────────┬─────────────────────────────────────────────────────────────┤
│                    │                                                             │
│  👤 "Show me a     │  📊 Matter Budget Dashboard                                │
│  budget dashboard │  ──────────────────────────                                 │
│  for all active   │                                                             │
│  matters"         │  [Overview] [By Matter] [Timeline] [Alerts]                 │
│                    │                                                             │
│  🤖 Here's your   │  ┌─── Budget vs Spend ────────────────────────────────┐     │
│  budget dashboard.│  │ Acme Corp    ████████████████████░░░ 90% ($225K)   │     │
│  2 matters are    │  │ Smith IP     ████████████████████░░░ 90% ($180K)   │     │
│  above 80% burn.  │  │ Globex       ████████████░░░░░░░░░░ 60% ($90K)    │     │
│                    │  │ Morrison     ████████░░░░░░░░░░░░░░ 40% ($80K)    │     │
│  I can drill into │  └────────────────────────────────────────────────────┘     │
│  any matter or    │                                                             │
│  show the trend   │  ⚠️ ALERTS                                                  │
│  over time.       │  ┌──────────────────────────────────────────────────┐       │
│                    │  │ Acme Corp — 90% burn, 40% work remaining        │       │
│  💬 _____________ │  │ Smith IP — 90% burn, projected to exceed budget  │       │
│                    │  └──────────────────────────────────────────────────┘       │
│                    │                                                             │
│                    │  [Export PDF] [Download CSV] [Open in Reporting ▶]          │
│                    │                                                     [▶ src] │
└────────────────────┴─────────────────────────────────────────────────────────────┘
```

Note the `[▶ src]` collapse indicator — the source/research pane is collapsed because this task doesn't need reference material. If the user says "Show me the invoices for Acme Corp", the source pane would expand with the invoice detail list.

**Example: Legal research (all three panes):**

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│ ⚡ Spaarke AI       Acme Corp Matter ▾     General ▾               [History] [⋮]  │
├────────────────────┬───────────────────────────────┬─────────────────────────────┤
│                    │                               │                             │
│  👤 "Research 9th  │  🔬 Research Report           │  🌐 Sources                 │
│  Circuit patent    │  ─────────────────             │  ──────────                 │
│  eligibility under│                               │                             │
│  Alice"           │  KEY FINDINGS                 │  [1] TechFlow v. DataSync   │
│                    │                               │      9th Cir. 2026          │
│  🤖 I found 5     │  The 9th Circuit is trending  │      law.justia.com/...     │
│  relevant rulings.│  toward eligibility for       │      [Open] [Save to Docs]  │
│  The report is in │  software patents that claim  │                             │
│  the output pane  │  specific technical           │  [2] Innovate v. USPTO      │
│  with sources on  │  improvements...              │      9th Cir. 2025          │
│  the right.       │                               │      cafc.uscourts.gov/...  │
│                    │  CASE SUMMARIES               │      [Open] [Save to Docs]  │
│  Want me to draft │                               │                             │
│  a memo applying  │  1. TechFlow v. DataSync [1]  │  [3] PharmaCo v. BioTech    │
│  these to the     │     Held: Software patent     │      9th Cir. 2025          │
│  Acme patent?     │     eligible under "specific  │      [Open] [Save to Docs]  │
│                    │     technical improvement"    │                             │
│                    │     test. Court distinguished │  📄 Your Documents          │
│  💬 _____________ │     from Alice abstract idea. │  ──────────────             │
│                    │                               │  Patent_Filing_Acme.pdf     │
│                    │  2. Innovate v. USPTO [2]     │  Prior_Art_Analysis.docx    │
│                    │     Narrowed abstract idea    │                             │
│                    │     for AI/ML patents...      │                             │
│                    │                               │                             │
│                    │  [Save Report] [Export] [Cite]│  [Save All Sources]         │
└────────────────────┴───────────────────────────────┴─────────────────────────────┘
```

**Output Pane Component Registry:**

The output pane renders purpose-built React components matched to the tool/task output type:

```typescript
// Output component registry — maps tool output types to React components
const outputRegistry: Record<string, React.ComponentType<OutputPaneProps>> = {
  // Analysis & Document
  "analysis-result":       AnalysisResultEditor,    // editable rich text (reuse from AnalysisWorkspace)
  "document-comparison":   DocumentComparisonView,  // side-by-side diff
  "document-draft":        DocumentDraftEditor,     // editable draft with AI suggestions

  // Data & Visualization
  "budget-dashboard":      BudgetDashboard,         // matter budget charts + alerts
  "chart":                 ChartRenderer,           // Code Interpreter output
  "data-table":            InteractiveDataTable,    // sortable, filterable, exportable
  "timeline":              MatterTimeline,          // chronological event view

  // Research
  "research-report":       ResearchReportView,      // structured findings with citations
  "search-results":        SearchResultsGrid,       // document search results

  // Action
  "invoice-approval":      InvoiceApprovalList,     // approve/reject with notes
  "approval-form":         ApprovalForm,            // generic record approval
  "playbook-result":       PlaybookResultViewer,    // structured playbook output

  // Preview
  "document-preview":      DocumentPreview,         // file viewer (PDF, DOCX)
};
```

**Source Pane Component Registry:**

```typescript
const sourceRegistry: Record<string, React.ComponentType<SourcePaneProps>> = {
  "document-viewer":       DocumentViewer,          // SPE file viewer with highlighting
  "web-sources":           WebSourceList,           // research citations with links
  "search-sources":        SearchSourcePanel,       // AI Search results used as context
  "related-documents":     RelatedDocumentList,     // similar/related docs from matter
  "invoice-detail":        InvoiceDetailPanel,      // supporting invoice data
  "template-library":      TemplateSelector,        // document templates for drafting
};
```

**SSE events control both panes:**

```typescript
// Chat stream events (existing)
{ type: "text_delta", content: "I found 3 key risks..." }
{ type: "plan_preview", tools: [...] }

// Output pane events (new)
{ type: "output_pane", component: "analysis-result", data: { ... }, action: "replace" }
{ type: "output_pane", component: "budget-dashboard", data: { ... }, action: "replace" }

// Source pane events (new)
{ type: "source_pane", component: "document-viewer", data: { fileId: "...", page: 4 }, action: "open" }
{ type: "source_pane", component: "web-sources", data: { sources: [...] }, action: "replace" }

// Cross-pane linking (output references source)
{ type: "source_highlight", sourceRef: "section-7.2", scroll: true }
```

**Relationship to Analysis Workspace:**

The Analysis Workspace becomes a **specific configuration** of this three-pane pattern:

| Pane | Analysis Workspace (current) | Standalone Spaarke AI (new) |
|------|-----------------------------|-----------------------------|
| Left | Rich text editor (fixed) | Chat (fixed) |
| Center | Document viewer (fixed) | Output pane (dynamic — component registry) |
| Right | SprkChat (fixed) | Source pane (dynamic — component registry, collapsible) |

In the future, the Analysis Workspace could be **refactored to use the same three-pane shell** with fixed component assignments — but that's an R2 consideration, not R1.

#### Surface 3: M365 Copilot (Separate Declarative Agent — Limited Scope)

M365 Copilot is **not a primary surface** in this release. It exists as a Declarative Agent (from the `ai-m365-copilot-integration-r1` project) with API Plugin access to BFF endpoints. Its role is lightweight lookups and handoff to the standalone app for complex work. Scope is constrained by what Declarative Agents can render (text + Adaptive Cards — no custom widgets).

The primary value of M365 Copilot in this architecture: **handoff**. When a user asks Copilot something complex, it generates a deep-link to open the standalone Spaarke AI with full context.

### How the Surfaces Connect

```
User's journey across surfaces:

1. Workspace → sees notification "NDA uploaded to Acme Corp"
   │
2. Clicks "Spaarke AI" button → Standalone AI opens with Acme Corp context
   │
3. "Find similar documents" → SprkChat uses existing DocumentSearchTools
   │
4. "Run risk assessment on the NDA" → PlaybookOrchestrationService executes
   │
5. Result opens in Analysis Workspace → deep editing, inline AI toolbar
   │
6. From Analysis Workspace chat: "Research 9th Circuit Alice rulings"
   │  → Routes to AI Agent Service (Bing Grounding) → citations appear in chat
   │
7. Back in M365 Copilot: "Summarize what I worked on today"
   │  → Custom Engine Agent queries chat history → Adaptive Card summary
```

Each surface has a natural role:

| Surface | Role | When Users Go There |
|---------|------|-------------------|
| **Workspace** | Overview, navigation, launch point | Start of session, matter overview |
| **Standalone Spaarke AI** | Quick AI interactions, research, queries | "I have a question" moments |
| **Analysis Workspace** | Deep document analysis, editing, structured output | "I need to produce something" moments |
| **M365 Copilot** | Quick lookups, status checks, cross-app commands | "Quick answer while I'm working" moments |

---

## 7. Proposed Architecture

### Layer 1: Unified SprkChat Frontend (Multi-Surface)

SprkChat already lives in `@spaarke/ui-components` as a shared React component. Make it deployable across three surfaces:

```
Surface 1: MDA Embedded (Current)
├── AnalysisWorkspace Code Page → SprkChat as right panel
├── Context: AnalysisAiContext (editor + viewer + chat)
└── Auth: Xrm OBO token flow

Surface 2: Standalone Code Page (New)
├── sprk_spaarkeai Code Page (full-page SprkChat)
├── Context: StandaloneAiContext (no editor, general-purpose)
├── Features: document upload, matter/project context, playbook selection
└── Auth: @spaarke/auth bootstrap (same as other Code Pages)

Surface 3: M365 Copilot / Teams (Existing M365 Project)
├── Custom Engine Agent → SpaarkeAgentHandler
├── Context: M365 activity context
├── Features: Adaptive Cards, handoff to Surface 1/2
└── Auth: M365 SSO → OBO → BFF
```

### Layer 2: BFF AI Gateway (Enhanced)

```
BFF API
├── ChatEndpoints (existing) ──► SprkChatAgent pipeline
│   ├── Middleware: telemetry, cost control, content safety
│   ├── Tools: 7 categories (analysis, search, documents, etc.)
│   ├── Context: playbook-scoped, analysis-scoped, or standalone
│   └── Streaming: SSE to frontend
│
├── AgentGatewayEndpoints (M365 project) ──► SpaarkeAgentHandler
│   ├── Activities from M365 Copilot/Teams
│   ├── Adaptive Card responses
│   └── Handoff deep-links
│
├── PlaybookOrchestration (existing) ──► DAG execution engine
│   ├── 12 node executors
│   ├── Scheduled execution (notification playbooks)
│   └── NEW: AgentServiceNodeExecutor (delegates to AI Foundry)
│
└── NEW: AI Foundry Integration Layer
    ├── AgentServiceClient (wraps Azure AI Agent Service SDK)
    ├── FoundryToolAdapter (maps BFF tools to Agent Service functions)
    └── CodeInterpreterBridge (pass data to Python sandbox, get results)
```

### Layer 3: AI Foundry Agent Service (Enhanced Capabilities)

```
Azure AI Foundry
├── Spaarke Legal AI Agent (managed)
│   ├── Model: GPT-4o (or newer)
│   ├── Instructions: Legal operations system prompt
│   ├── Tools:
│   │   ├── BFF Functions (registered as Agent Service tools)
│   │   │   ├── search_documents(query, filters)
│   │   │   ├── run_analysis(playbookId, documentIds)
│   │   │   ├── query_matters(filter)
│   │   │   └── ... (all current BFF AI tool capabilities)
│   │   ├── Code Interpreter (NEW — Python sandbox)
│   │   │   ├── Data analysis on matter financials
│   │   │   ├── Chart generation (budget burndown, timeline)
│   │   │   ├── Document statistics (word counts, comparison)
│   │   │   └── Custom calculations (deadline arithmetic)
│   │   ├── File Search (OPTIONAL — supplement AI Search)
│   │   │   ├── Upload documents to agent thread
│   │   │   └── Built-in chunking + retrieval
│   │   └── Bing Grounding (NEW — web search with citations)
│   │       ├── Legal research (case law, statutes)
│   │       └── Company research (counterparty intelligence)
│   └── Knowledge: Legal ops domain (uploaded reference docs)
│
├── Evaluation Pipeline
│   ├── Groundedness checks (is the response factually supported?)
│   ├── Relevance scoring (did it answer the question?)
│   ├── Completeness (did it address all parts?)
│   └── Legal-specific metrics (citation accuracy, jurisdiction awareness)
│
└── Tracing & Monitoring
    ├── OpenTelemetry traces for all agent runs
    ├── Token usage tracking per customer
    └── Content safety audit logs
```

### Routing Decision: When to Use What

```
User message arrives at BFF ChatEndpoints
  │
  ├── Simple chat / Q&A ──► Direct IChatClient (Azure OpenAI)
  │   └── No tools needed, fast response
  │
  ├── Tool-assisted query ──► SprkChatAgent (current pipeline)
  │   └── Document search, analysis, text refinement
  │   └── Uses existing 7 tool categories
  │
  ├── Complex computation ──► Route to AI Agent Service
  │   └── "Analyze the budget trend across all my matters"
  │   └── → Code Interpreter generates Python analysis + chart
  │   └── → Returns chart image + narrative to SprkChat
  │
  ├── Deep research ──► Route to AI Agent Service
  │   └── "Research case law on patent infringement in 9th Circuit"
  │   └── → Bing Grounding + your document search combined
  │   └── → Returns structured research with citations
  │
  └── Playbook execution ──► PlaybookOrchestrationService (existing)
      └── "Run the risk assessment on this contract"
      └── → Full DAG execution with streaming
```

---

## 6. Shared AI Libraries — Separation of Concerns

The AI platform splits into **three shared libraries** — separating UX components, AI context/services, and AI output widgets:

### Library Architecture

```
@spaarke/auth (existing — no changes)
  └── MSAL auth, token acquisition, runtime config

@spaarke/ui-components (existing — keeps SprkChat shell + layout primitives)
  ├── SprkChat (chat message list, input, streaming, chips, commands)
  ├── PanelSplitter (draggable, keyboard-accessible splitter)
  ├── ThreePaneLayout (new — configurable three-pane shell)
  ├── WizardDialog, SidePanel, DataGrid, etc.
  └── NO AI service code, NO context providers

@spaarke/ai-context (NEW — AI context providers and service clients)
  ├── context/
  │   ├── IAiContextProvider.ts         — interface for all context providers
  │   ├── StandaloneAiContext.tsx        — standalone (matter/project/doc scope)
  │   ├── AnalysisAiContext.tsx          — moved from AnalysisWorkspace
  │   └── useAiContext.ts               — hook to consume active context
  ├── services/
  │   ├── chatApiClient.ts              — BFF chat endpoints (create session, send, stream)
  │   ├── playbackApiClient.ts          — BFF playbook endpoints
  │   ├── contextMappingClient.ts       — BFF context resolution
  │   └── documentApiClient.ts          — SPE file operations via BFF
  ├── hooks/
  │   ├── useChatSession.ts             — session lifecycle (moved from SprkChat)
  │   ├── useChatContextMapping.ts      — context resolution (moved from SprkChat)
  │   ├── useChatPlaybooks.ts           — playbook discovery (moved from SprkChat)
  │   ├── useOutputPane.ts              — output pane state management
  │   ├── useSourcePane.ts              — source pane state management
  │   └── useStreamingResponse.ts       — SSE stream with rich output events
  └── types/
      ├── aiContext.ts                  — IAiContext, IToolScope, IEntityContext
      ├── chatTypes.ts                  — message types, SSE event types
      └── paneTypes.ts                  — output/source pane event types

@spaarke/ai-outputs (NEW — purpose-built output and source widgets)
  ├── outputs/                          — Output Pane components
  │   ├── AnalysisResultEditor.tsx      — editable rich text (reused from AnalysisWorkspace)
  │   ├── BudgetDashboard.tsx           — matter budget charts + alerts
  │   ├── ChartRenderer.tsx             — Code Interpreter chart output
  │   ├── DocumentComparisonView.tsx    — side-by-side diff
  │   ├── DocumentDraftEditor.tsx       — editable draft with AI suggestions
  │   ├── InteractiveDataTable.tsx      — sortable, filterable, exportable
  │   ├── InvoiceApprovalList.tsx       — approve/reject with notes
  │   ├── MatterTimeline.tsx            — chronological event view
  │   ├── PlaybookResultViewer.tsx      — structured playbook output
  │   ├── ResearchReportView.tsx        — structured findings with citations
  │   └── SearchResultsGrid.tsx         — document search results
  ├── sources/                          — Source Pane components
  │   ├── DocumentViewer.tsx            — SPE file viewer with highlighting
  │   ├── InvoiceDetailPanel.tsx        — supporting invoice data
  │   ├── RelatedDocumentList.tsx       — similar/related docs
  │   ├── SearchSourcePanel.tsx         — AI Search results used as context
  │   ├── TemplateSelector.tsx          — document templates for drafting
  │   └── WebSourceList.tsx             — research citations with links
  ├── registry/
  │   ├── OutputComponentRegistry.ts    — maps output types → React components
  │   └── SourceComponentRegistry.ts    — maps source types → React components
  └── types/
      └── outputTypes.ts                — component prop types, data contracts

```

### Dependency Chain

```
Code Page (thin shell — 50-100 lines)
  ├── @spaarke/ai-outputs     (output/source pane widgets)
  ├── @spaarke/ai-context     (context providers, service clients, hooks)
  ├── @spaarke/ui-components  (SprkChat shell, PanelSplitter, ThreePaneLayout)
  └── @spaarke/auth           (token acquisition)
```

### Why Three Libraries (Not Two)

| Separation | Rationale |
|-----------|-----------|
| **`ai-context`** separate from **`ui-components`** | AI services and context providers evolve on a different cadence than UI components. AI context depends on BFF API contracts; UI components depend on Fluent UI. |
| **`ai-outputs`** separate from **`ai-context`** | Output widgets are visual (React + Fluent UI) while context/services are non-visual (API clients, state management). A BFF change to the chat API shouldn't require rebuilding output widgets. |
| **`ai-outputs`** separate from **`ui-components`** | Output widgets are AI-specific (chart renderers, research views, approval forms). General UI components (DataGrid, StatusBadge, WizardDialog) don't need AI dependencies. |

### What Moves Where

| Current Location | Moves To | Why |
|-----------------|----------|-----|
| `SprkChat/hooks/useChatContextMapping.ts` | `@spaarke/ai-context` | AI service concern, not UI |
| `SprkChat/hooks/useChatPlaybooks.ts` | `@spaarke/ai-context` | AI service concern |
| `SprkChat/hooks/useChatSession.ts` | `@spaarke/ai-context` | AI service concern |
| `AnalysisWorkspace/context/AnalysisAiContext.tsx` | `@spaarke/ai-context` | Shared context provider |
| `AnalysisWorkspace/components/RichTextEditor` | `@spaarke/ai-outputs` | Output widget |
| `SprkChat` (shell component) | **Stays** in `@spaarke/ui-components` | It's a UI component |
| `PanelSplitter` | **Stays** in `@spaarke/ui-components` | Layout primitive |

---

## 7. Standalone Code Page — Three-Pane Architecture

### Architecture

```
sprk_spaarkeai Code Page
├── React 19 + Vite single-file build
├── FluentProvider (theme from shared themeStorage)
│
├── SpaarkeAiApp.tsx
│   ├── StandaloneAiContext (from @spaarke/ai-context)
│   │   ├── Entity context from URL params (?matterId=, ?projectId=, ?documentId=)
│   │   ├── outputPaneRef — chat can push components to output pane
│   │   ├── sourcePaneRef — chat can load documents/sources in source pane
│   │   └── Auth token from @spaarke/auth bootstrap
│   │
│   ├── AppHeader
│   │   ├── Context selector (matter/project dropdown)
│   │   ├── Playbook selector
│   │   ├── Chat history button
│   │   └── Settings
│   │
│   ├── ThreePaneLayout (from @spaarke/ui-components)
│   │   ├── PanelSplitter (existing, reused from Analysis Workspace)
│   │   ├── Left: ChatPane (always visible)
│   │   │   └── SprkChat (from @spaarke/ui-components)
│   │   ├── Center: OutputPane (dynamic component from registry)
│   │   │   └── Renders purpose-built widget from @spaarke/ai-outputs
│   │   └── Right: SourcePane (collapsible, dynamic)
│   │       └── Renders reference material from @spaarke/ai-outputs/sources
│   │
│   └── ChatHistoryDrawer (slide-out from left)
│       └── Session list with search
│
└── Launch points:
    ├── Workspace command bar "Spaarke AI" button
    ├── Entity form command bar (matter, project)
    ├── M365 Copilot handoff deep-link
    └── Direct URL: sprk_spaarkeai?matterId={id}
```

### Pane Interaction Model

The chat drives both output and source panes via SSE events from the BFF:

```
User: "Analyze the risks in the Acme NDA"
  │
  BFF SprkChatAgent processes:
  │
  ├── SSE: { type: "text_delta", content: "I found 3 key risks..." }
  │   └── Chat pane: renders streaming text
  │
  ├── SSE: { type: "output_pane", component: "analysis-result", data: {...} }
  │   └── Output pane: renders AnalysisResultEditor with structured findings
  │
  ├── SSE: { type: "source_pane", component: "document-viewer", data: { fileId, page: 4 } }
  │   └── Source pane: opens NDA PDF at page 4, expands if collapsed
  │
  └── SSE: { type: "source_highlight", ref: "section-7.2" }
      └── Source pane: scrolls to and highlights Section 7.2
```

The user can also **manually** control panes:
- Click a citation in the output pane → source pane navigates to that reference
- Drag splitters to resize
- Collapse/expand source pane via toggle button
- Click "Open Full" on any output to expand it to full width

### SPE + AI Search Integration Through Panes

The three-pane layout surfaces SPE documents and AI Search results naturally:

| User Action | Chat Pane | Output Pane | Source Pane | Services Used |
|-------------|-----------|-------------|-------------|---------------|
| "Find similar docs" | Narrative results | SearchResultsGrid (ranked list) | DocumentViewer (preview on click) | AI Search (vector similarity), SPE (file content) |
| "Analyze this contract" | Streaming analysis | AnalysisResultEditor | DocumentViewer (source PDF) | SPE (file retrieval), Azure OpenAI (analysis) |
| "Compare NDA v2 vs v3" | Diff summary | DocumentComparisonView (side-by-side) | (collapsed — diff IS the output) | SPE (both file versions), Azure OpenAI (diff analysis) |
| "Upload and classify" | Classification result | InteractiveDataTable (metadata) | DocumentViewer (uploaded file) | SPE (upload), Doc Intelligence (OCR), Azure OpenAI (classification) |
| "Search for patent filings" | Search narrative | SearchResultsGrid | WebSourceList (external) + RelatedDocumentList (internal) | AI Search (internal), Bing Grounding (external) |

**Key rule**: SPE file content flows through the BFF's `SpeFileStore` (Graph OBO). The source pane's `DocumentViewer` loads files via `GET /api/obo/containers/{id}/files/{fileId}/content` — same endpoint the Analysis Workspace uses today. No new SPE integration needed.

---

## 7. AI Foundry Integration Implementation

### New BFF Components

#### AgentServiceClient

```csharp
// Wraps Azure.AI.Projects SDK for AI Agent Service interaction
public class AgentServiceClient
{
    private readonly AIProjectClient _projectClient;

    public async Task<AgentThread> CreateThreadAsync(string agentId)
    {
        return await _projectClient.GetAgentClient()
            .CreateThreadAsync();
    }

    public async IAsyncEnumerable<StreamingUpdate> RunAgentAsync(
        string agentId, string threadId, string message,
        CancellationToken ct)
    {
        // Add message to thread
        await _projectClient.GetAgentClient()
            .CreateMessageAsync(threadId, MessageRole.User, message);

        // Create streaming run
        var run = _projectClient.GetAgentClient()
            .CreateRunStreamingAsync(threadId, agentId, cancellationToken: ct);

        await foreach (var update in run)
        {
            yield return update;
        }
    }
}
```

#### AgentServiceNodeExecutor (ActionType 60)

```csharp
// New playbook node: delegates complex tasks to AI Agent Service
public class AgentServiceNodeExecutor : INodeExecutor
{
    public ActionType ActionType => ActionType.AgentService;

    public async Task<NodeOutput> ExecuteAsync(
        NodeExecutionContext context, CancellationToken ct)
    {
        var config = context.Node.ConfigJson;
        var agentId = config.AgentId;
        var prompt = context.ResolveTemplate(config.Prompt);

        // Create ephemeral thread for this node execution
        var thread = await _agentClient.CreateThreadAsync(agentId);

        // Stream agent response
        var result = new StringBuilder();
        await foreach (var update in _agentClient.RunAgentAsync(
            agentId, thread.Id, prompt, ct))
        {
            if (update is MessageContentUpdate content)
                result.Append(content.Text);
        }

        return new NodeOutput
        {
            Text = result.ToString(),
            Metadata = new { agentId, threadId = thread.Id }
        };
    }
}
```

#### CodeInterpreterBridge

```csharp
// Routes requests that need Python computation to Agent Service
public class CodeInterpreterBridge
{
    public async Task<CodeInterpreterResult> ExecuteAsync(
        string instruction, byte[]? csvData = null,
        CancellationToken ct = default)
    {
        // Create agent with code interpreter enabled
        var agent = await _agentClient.CreateAgentAsync(
            model: "gpt-4o",
            instructions: "You are a data analyst. Analyze the provided data.",
            tools: new[] { new CodeInterpreterToolDefinition() });

        var thread = await _agentClient.CreateThreadAsync();

        // Upload data file if provided
        if (csvData != null)
        {
            var file = await _agentClient.UploadFileAsync(csvData, "data.csv");
            await _agentClient.CreateMessageAsync(
                thread.Id, MessageRole.User, instruction,
                attachments: new[] { new MessageAttachment(file.Id, new[] { "code_interpreter" }) });
        }
        else
        {
            await _agentClient.CreateMessageAsync(
                thread.Id, MessageRole.User, instruction);
        }

        // Run and collect results (text + generated images)
        var run = await _agentClient.CreateRunAsync(thread.Id, agent.Id);
        // ... collect response, extract images, return structured result
    }
}
```

---

## 8. Deployment Model Alignment

### Multi-Tenant Considerations

| Component | Spaarke Multi-Customer | Spaarke Dedicated | Customer Tenant |
|-----------|----------------------|-------------------|-----------------|
| BFF SprkChatAgent | Shared BFF, tenant-scoped context | Dedicated BFF instance | Customer's BFF instance |
| AI Foundry Hub | Spaarke-owned hub | Spaarke-owned hub | Customer provisions hub |
| AI Foundry Agent | Shared agent, tenant-scoped tools | Per-customer agent (optional) | Customer provisions agent |
| Azure OpenAI | Shared deployment | Shared or dedicated deployment | Customer provisions deployment |
| AI Search | Shared index, security-filtered | Per-customer index | Customer provisions index |

**BYOK support**: All AI Foundry resource IDs and connection strings via environment variables. No hardcoded resource names.

```
AI_FOUNDRY_PROJECT_ENDPOINT=https://sprkspaarkedev-aif-proj.cognitiveservices.azure.com
AI_FOUNDRY_AGENT_ID=asst_abc123
AI_AGENT_SERVICE_ENABLED=true
CODE_INTERPRETER_ENABLED=true
```

---

## 9. Phased Implementation Plan

### Phase 1: Standalone SprkChat Code Page (4-6 weeks)

**Goal**: SprkChat works as a full-page app independent of Analysis Workspace

| Deliverable | Scope |
|-------------|-------|
| `sprk_spaarkeai` Code Page | React 19, Vite, full-page chat |
| `StandaloneAiContext` | New context provider (no editor dependency) |
| Entity context from URL | `?matterId=`, `?projectId=`, `?documentId=` |
| Playbook selector | Dropdown in header, filters by context |
| Document upload | Drag-drop files for quick analysis |
| Launch points | Workspace button, entity form buttons, deep-link |
| Dark mode | Unified theme support |

**Dependencies**: None — uses existing BFF endpoints and SprkChat component

### Phase 2: AI Foundry Agent Service Integration (4-6 weeks)

**Goal**: Route complex tasks to managed AI Agent Service for enhanced capabilities

| Deliverable | Scope |
|-------------|-------|
| `AgentServiceClient` | Wrapper for Azure.AI.Projects SDK |
| `AgentServiceNodeExecutor` (AT 60) | Playbook node that delegates to Agent Service |
| Code Interpreter integration | Budget analysis, chart generation, data analysis |
| Bing Grounding integration | Legal research, company research |
| Agent definition deployment | Spaarke Legal AI Agent in Foundry |
| Tool registration | Map BFF tools as Agent Service functions |
| Routing logic | Decision tree for when to use Agent Service vs direct |

**Dependencies**: F-SKU capacity for Agent Service, Bing Grounding provisioning

### Phase 3: Evaluation & Quality (2-3 weeks)

**Goal**: Automated quality assurance for AI responses

| Deliverable | Scope |
|-------------|-------|
| Evaluation pipeline | Foundry evaluation on playbook outputs |
| Legal-specific metrics | Citation accuracy, jurisdiction awareness, completeness |
| A/B testing framework | Compare direct vs Agent Service routing |
| Tracing integration | OpenTelemetry traces to Application Insights |
| Cost dashboard | Token usage per customer, per tool, per playbook |

### Phase 4: M365 Agent Enhancement (3-4 weeks)

**Goal**: M365 Custom Engine Agent gains Agent Service capabilities

| Deliverable | Scope |
|-------------|-------|
| `SpaarkeAgentHandler` enhanced | Route complex M365 queries through Agent Service |
| Code Interpreter in Copilot | "Generate a chart of my matter budgets" |
| Research in Copilot | "Research recent patent filings for Acme Corp" |
| Handoff to standalone app | "Open Spaarke AI for deep analysis" |

**Dependencies**: M365 Copilot integration R1 complete

---

## 10. Scope

### In Scope (R1)
- Standalone SprkChat Code Page (`sprk_spaarkeai`)
- `StandaloneAiContext` (entity-scoped, no editor)
- Launch points (workspace, entity forms, deep-link)
- AI Foundry Agent Service integration
- `AgentServiceClient` + `AgentServiceNodeExecutor` (AT 60)
- Code Interpreter bridge (data analysis, charts)
- Bing Grounding integration (research with citations)
- Agent definition deployment + tool registration
- Routing logic (direct vs Agent Service)
- Evaluation pipeline with legal-specific metrics
- Tracing integration (OpenTelemetry)
- Dark mode support
- BYOK-compatible configuration (env vars for all resources)

### Out of Scope (R2+)
- Multi-agent orchestration (agent-to-agent collaboration)
- Custom model fine-tuning in Foundry
- Semantic Kernel migration (lateral move, not needed)
- Agent marketplace (customer-authored agents)
- Voice input/output
- Real-time collaboration (multiple users in same chat)
- Offline / disconnected mode

---

## 11. Technical Constraints

### Applicable ADRs
- **ADR-001**: BFF Minimal API — all AI calls through BFF (no direct Foundry calls from frontend)
- **ADR-006**: Code Page for standalone SprkChat (not PCF)
- **ADR-008**: Endpoint filters for authorization
- **ADR-009**: Redis caching for agent sessions/threads
- **ADR-012**: SprkChat in shared component library
- **ADR-013**: AI features extend BFF, not separate service
- **ADR-021**: Fluent UI v9, dark mode
- **ADR-026**: Vite single-file Code Page build

### MUST Rules
- MUST use existing `SprkChatAgent` pipeline for standard queries — Agent Service is additive, not replacement
- MUST route through BFF — frontend never calls AI Foundry directly
- MUST use `IChatClient` abstraction — agent routing is transparent to consumers
- MUST support all 3 deployment models (env var configuration)
- MUST NOT require Semantic Kernel — existing playbook engine is more capable for legal ops
- MUST NOT break existing Analysis Workspace integration
- MUST label AI-generated charts/research clearly with source attribution

### Architecture Reference
- `docs/architecture/playbook-architecture.md` — Playbook engine, node executors
- `docs/architecture/AI-ARCHITECTURE.md` — AI platform overview
- `docs/guides/JPS-AUTHORING-GUIDE.md` — Playbook design
- `infrastructure/ai-foundry/README.md` — Foundry infrastructure

---

## 12. Success Criteria

1. [ ] Standalone SprkChat Code Page opens and functions without Analysis Workspace
2. [ ] Entity context (matter/project) loads from URL parameters
3. [ ] All existing SprkChat capabilities work in standalone mode
4. [ ] Code Interpreter generates charts/analysis from matter financial data
5. [ ] Bing Grounding returns legal research with citations
6. [ ] Agent Service routing is transparent — user doesn't know which path was used
7. [ ] Evaluation pipeline runs automatically on playbook outputs
8. [ ] Tracing captures full request lifecycle (frontend → BFF → Foundry → response)
9. [ ] Works in all 3 deployment models
10. [ ] No regression in Analysis Workspace SprkChat functionality
11. [ ] BYOK deployment works with customer-provisioned Foundry resources

---

## 13. Dependencies

### Prerequisites
- Azure AI Foundry Hub + Project (exists)
- Azure OpenAI deployment (exists)
- AI Search index (exists)
- `SprkChat` shared component (exists)
- `SprkChatAgent` + tool framework (exists)
- `PlaybookOrchestrationService` (exists)

### New Azure Resources
- AI Agent Service provisioning (part of Foundry project)
- Bing Grounding resource (if legal research feature enabled)
- F-SKU capacity for Agent Service (same capacity as existing Foundry project)

### Related Projects
- `ai-m365-copilot-integration-r1` — M365 surface for SprkChat
- `spaarke-daily-update-service` — notification playbooks use same engine
- `spaarke-powerbi-embedded-r1` — reporting Code Page pattern reuse
- `ai-sprk-chat-extensibility-r1` — context enrichment patterns

---

*Last updated: 2026-05-15*
