# Spaarke AI Platform — R2 Design Document

> **Status**: Draft — Review Before design-to-spec
> **Date**: 2026-05-17
> **Author**: Ralph Schroeder + Claude (AI-assisted design)
> **Builds on**: R1 implementation (spaarke-ai-platform-unification-r1)
> **Reference**: notes/ux-design-brainstorm.md, notes/session-continuity-research.md, notes/distribution-research.md

---

## 1. Vision

Evolve Spaarke from a database-oriented CRUD application with bolted-on AI chat into an **AI-directed experience** where 80% of users (consumers, not data entry operators) accomplish their work through conversation and intelligent workflows.

The user expresses intent. The AI orchestrates data, tools, and workflows. The traditional system of record remains essential — but most users never interact with it directly.

**Before**: Navigate to Matter → Open Documents → Find contract → Click "Analyze" → Read results
**After**: "Review the Smith contract for liability exposure" → AI loads the contract, runs analysis, presents findings, highlights key clauses

---

## 2. The Three-Pane Architecture

### 2.1 Pane Definitions

| Pane | Name | Purpose | Widget Surface? |
|------|------|---------|----------------|
| Left | **Conversation** | User and AI converse. Specialized per loaded playbook/agent. Drives the workflow through suggestions, action chips, confirmations, and status updates. | No — dedicated to SprkChat |
| Center | **Workspace** | Active work surface where results, editors, reports, and tools are presented. AI and user co-work on content. Supports tabbed multi-item layout. | Yes — renders from WorkspaceWidgetRegistry |
| Right | **Context** | Adaptive intelligence panel. Transforms based on workflow state: playbook gallery → entity info → sources/citations → progress tracker → related items. | Yes — renders from ContextWidgetRegistry |

### 2.2 Pane Coordination Protocol

All three panes are coordinated through a structured SSE event system. The Conversation pane (chat/AI) is the orchestrator — it drives state changes in the Workspace and Context panes via SSE events.

**Event Flow**:
```
User types in Conversation
  → BFF receives message, runs AI agent with registered tools
  → AI decides to call SearchDocuments tool
  → Tool executes, returns results
  → BFF emits SSE events:
      1. Text tokens → Conversation pane (streaming response)
      2. workspace_widget event → Workspace pane (SearchResults widget with structured data)
      3. context_update event → Context pane (entity info, related docs, citations)
  → All three panes update simultaneously
```

**SSE Event Types for Pane Control**:

| Event | Target Pane | Payload | Purpose |
|-------|-------------|---------|---------|
| `token` | Conversation | text chunk | Streaming AI response text |
| `workspace_widget` | Workspace | { widgetType, widgetData, tabId? } | Load/update a widget in the workspace |
| `context_update` | Context | { contextType, contextData } | Update the context panel content |
| `context_highlight` | Context | { citationId, selectionRef } | Scroll/highlight a specific reference |
| `suggestions` | Conversation | { chips[] } | Show action chips in chat |
| `workspace_action` | Workspace | { action, targetWidgetId, params } | Trigger an action on an existing widget |

**Cross-Pane Interactions**:

| Interaction | How It Works |
|------------|--------------|
| User clicks citation in Workspace → Context highlights source | Workspace widget dispatches `CrossPaneLinkEvent`; Context pane subscribes and navigates |
| User selects text in Workspace → Chat offers refinement | Workspace widget dispatches selection event; Conversation pane shows "Refine this?" suggestion |
| User clicks playbook in Context → Chat specializes | Context panel dispatches playbook selection; Conversation loads new agent; Workspace clears or keeps tabs |
| AI finishes analysis → Context shows findings | BFF emits `context_update` with findings widget data |
| User switches Workspace tab → Context adapts | Tab change dispatches event; Context shows data relevant to active tab |

### 2.3 Pane Lifecycle (Stage Flow)

**Stage 1: Landing (No Context)**
```
┌─────────────────┬──────────────────────────┬──────────────────┐
│  CONVERSATION    │      WORKSPACE           │    CONTEXT       │
│                 │                          │                  │
│  Welcome to     │  "What would you like    │  PLAYBOOKS       │
│  Spaarke AI     │   to work on?"           │                  │
│                 │                          │  Review Doc      │
│  [Prompt        │  Recent work:            │  Research        │
│   buttons]      │  • Smith contract review │  Financials      │
│                 │  • Q2 budget analysis    │  Draft           │
│  Or type...     │                          │  Case Mgmt       │
│                 │  [Open in Spaarke App]   │                  │
└─────────────────┴──────────────────────────┴──────────────────┘
```

**Stage 2: Playbook Selected, Gathering Context**
```
┌─────────────────┬──────────────────────────┬──────────────────┐
│  CONVERSATION    │      WORKSPACE           │    CONTEXT       │
│                 │                          │                  │
│  Review Doc     │  Select a document:      │  DOCUMENT INFO   │
│  ─────────────  │                          │                  │
│  AI: "Which     │  [Upload]  [Browse]      │  (waiting for    │
│  document?"     │                          │   selection)     │
│                 │  Recent documents:       │                  │
│  [Upload File]  │  • Smith Agreement.pdf   │                  │
│  [Browse Docs]  │  • Lease Draft v3.docx   │                  │
└─────────────────┴──────────────────────────┴──────────────────┘
```

**Stage 3: Active Work**
```
┌─────────────────┬──────────────────────────┬──────────────────┐
│  CONVERSATION    │      WORKSPACE           │    CONTEXT       │
│                 │                          │                  │
│  Review Doc     │  DOCUMENT VIEWER         │  FINDINGS        │
│  ─────────────  │  ┌──────────────────┐    │                  │
│  AI: "3 risk    │  │ Smith Agreement  │    │  Liability §7    │
│  areas found.   │  │ Section 7.2:     │    │  Indemnity §12   │
│  §7.2 has an    │  │ ████ highlighted │    │  Term OK         │
│  unusual..."    │  └──────────────────┘    │                  │
│                 │                          │  Related Docs    │
│  You: "Compare  │  [Edit] [Annotate]       │  Progress: 2/5   │
│  with standard" │  [Export] [Approve]       │                  │
└─────────────────┴──────────────────────────┴──────────────────┘
```

**Stage 4: Multi-Task (Workspace Tabs)**
```
┌─────────────────┬──────────────────────────┬──────────────────┐
│  CONVERSATION    │  [Contract] [Budget]     │    CONTEXT       │
│                 │  ┌──────────────────────┐ │                  │
│  Financials     │  │  Q2 Budget Report    │ │  FINANCIAL DATA  │
│  ─────────────  │  │  ┌────┐ ┌────┐      │ │                  │
│  AI: "Q2 is     │  │  │ ▌▌ │ │ ▌▌ │      │ │  Budget: $250K   │
│  15% over..."   │  │  └────┘ └────┘      │ │  Spent: $287K    │
│                 │  │  Jan  Feb  Mar       │ │  Invoices: 12    │
│  You: "Show     │  │  [Download] [Share]  │ │                  │
│  invoices"      │  └──────────────────────┘ │                  │
└─────────────────┴──────────────────────────┴──────────────────┘
```

---

## 3. Playbook Model

### 3.1 What a Playbook Configures

A playbook is not just a system prompt — it reconfigures the entire three-pane experience:

| Component | What the Playbook Defines |
|-----------|--------------------------|
| **AI Agent** | System prompt, personality, domain expertise |
| **Tools** | Which AI tools are available (search, analyze, write, compare) |
| **Knowledge** | Which knowledge sources and search indices are scoped |
| **Workspace default** | Initial widget for the Workspace pane (document viewer, report, editor) |
| **Context default** | Initial widget for the Context pane (findings panel, data panel, checklist) |
| **Actions** | Available action buttons on workspace widgets (approve, annotate, export) |
| **Suggestions** | Contextual suggestion chips the AI can offer |

### 3.2 Default Playbook

A "Spaarke AI General" playbook is auto-loaded when no specific playbook is selected:

| Property | Value |
|----------|-------|
| Name | Spaarke AI General |
| System Prompt | Comprehensive standalone prompt with tool guidance |
| Tools | SearchDocuments, SearchDiscovery, GetKnowledgeSource, RefineText, GenerateSummary, QueryEntities |
| Knowledge | Tenant's default AI Search indices |
| Workspace | Welcome/recent items view |
| Context | Playbook gallery |

This is a Dataverse record (`sprk_analysisplaybook`) — configurable by admins without code changes.

### 3.3 Playbook ↔ Microsoft Foundry/Agent Framework

**Decision**: Our playbook engine (PlaybookOrchestrationService, node executors) is the primary orchestration layer. Azure AI Foundry Agent Service is a **capability provider** called selectively:

| Capability | Provider | Why |
|-----------|----------|-----|
| Standard chat + search | Spaarke playbook engine | More mature, entity-scoped, legal-domain optimized |
| Code Interpreter (Python sandbox) | Foundry Agent Service | Azure-hosted sandbox, no Spaarke infra needed |
| Bing Grounding (legal research) | Foundry Agent Service | Web-scale search with citations |
| Multi-step node workflows | Spaarke playbook engine | Our node executor framework handles this |
| Knowledge retrieval (RAG) | Spaarke + AI Search | Custom indices, entity-scoped search |

Playbooks can include Foundry-backed nodes (ActionType 60) alongside standard nodes — the routing is transparent to the user.

---

## 4. Data Access — Hybrid Search

### 4.1 Current State (Post-R1)

| Data Source | Tool | Status |
|------------|------|--------|
| SPE documents (file content) | SearchDocuments via AI Search | **Working** — semantic search returns results |
| AI Search knowledge index | SearchKnowledgeBase, GetKnowledgeSource | **Working** |
| Dataverse entity records | None | **Gap** — no tool queries Dataverse directly |
| Dataverse records in AI Search | SearchDiscovery via spaarke-records-index | **Index exists but not syncing** |

### 4.2 Target Architecture (Hybrid)

```
AI Search = DISCOVERY  (find things, explore, "what's relevant to X?")
OData API = DETAIL + ACTION  (get full record, create, update, filter by exact criteria)
```

| Query Type | Tool | Backend | Example |
|-----------|------|---------|---------|
| Semantic discovery | SearchDiscovery | AI Search | "Find matters related to intellectual property" |
| Structured filter | QueryEntities | Dataverse OData | "Show all active matters" |
| Specific record | GetEntityDetail | Dataverse OData | "What's the budget on M-2024-0156?" |
| Document content | SearchDocuments | AI Search | "Find contracts mentioning indemnity" |
| Create/update record | CreateEntity / UpdateEntity | Dataverse OData | "Create a new matter for Client X" |

### 4.3 Implementation

**Phase 1: DataverseQueryTools** — OData-based tool for structured queries
- `QueryEntities(entityType, filter, top)` — list/filter records
- `GetEntityDetail(entityType, identifier)` — get single record by ID or key
- Entities: sprk_matter, sprk_project, contact, account, sprk_document

**Phase 2: Fix spaarke-records-index sync**
- Sync key entity fields from Dataverse to AI Search
- Background sync job (BFF BackgroundService)
- SearchDiscovery finds entities AND documents in one query

---

## 5. Work History — Session Persistence

### 5.1 Architecture: Redis + Cosmos DB (No Dataverse)

Session data uses Redis + Cosmos DB only. Dataverse is too expensive ($40/GB vs Cosmos $0.25/GB) and wrong-shaped for high-frequency message-level reads/writes.

| Tier | Store | What | TTL |
|------|-------|------|-----|
| Hot | **Redis** | Active session, streaming state, pending actions | 24h sliding |
| Warm | **Cosmos DB** | Work history — full context, all artifacts | 90 days |
| Cold | **Azure Blob** | Compliance archive | Configurable (years) |

Cosmos DB is provisioned alongside the AI Foundry Agent Service (which uses the same Cosmos DB for BYO thread storage).

### 5.2 Work History Model

This is **work history**, not chat history. A session captures the full context of what the user accomplished:

| Component | What's Persisted | Purpose |
|-----------|-----------------|---------|
| Messages | User + AI messages with timestamps | Conversation context |
| Tool results | Search results, analysis outputs, data retrieved | Resume context |
| Workspace state | Active widgets, tab config, widget data snapshots | Visual restore |
| Decisions | User approvals, rejections, confirmations | Audit trail |
| Artifacts | Reports generated, documents drafted, exports | Work products |
| Entity context | Matter/project/document IDs + ETags | Staleness detection |
| Playbook config | Active playbook, loaded tools, knowledge sources | Agent restore |

### 5.3 Session Restore

When a user returns to a previous session:

1. **Load session** from Cosmos (session ID, playbook, entity context)
2. **Check entity staleness** — compare stored ETag against current Dataverse record
3. **Reconstruct context** — LLM summary of prior work + last 10 verbatim messages (not full replay)
4. **Restore workspace** — re-render widgets from workspace snapshot
5. **Resume agent** — load playbook with session's tools and knowledge

Target: < 500ms restore time.

**LLM Summarization**: At 15 messages or 8,000 tokens, summarize using gpt-4o-mini. On restore, send summary + last 10 messages. 80-90% token cost reduction vs. full replay.

### 5.4 Prompt Library

Users save favorite prompts as reusable templates:

| Property | Description |
|----------|-------------|
| Name | "Weekly matter status review" |
| Template | "Review all active matters for {clientName} and summarize status, budget, and upcoming deadlines" |
| Variables | `{clientName}` — typed, with entity-ref pickers auto-filled from context |
| Ownership | personal / team / organization / system (4-tier) |
| Storage | Cosmos DB (not Dataverse) |

---

## 6. Widget Framework

### 6.1 Registries

| Registry | Pane | Purpose |
|----------|------|---------|
| `WorkspaceWidgetRegistry` | Workspace (center) | Active work surfaces — editors, viewers, reports, forms, wizards |
| `ContextWidgetRegistry` | Context (right) | Intelligence — playbooks, findings, sources, progress, entity info |

Both registries live in `@spaarke/ai-outputs` (or a new `@spaarke/ai-widgets` library) using the same lazy-load pattern as the existing output/source registries.

### 6.2 Interactive Widget API

Widgets evolve from display-only (R1) to interactive (R2):

```typescript
interface WorkspaceWidget<TData, TActions> {
  // Display
  render(data: TData, isLoading: boolean): ReactElement;

  // Interaction — actions the user can take on this widget
  actions?: TActions;  // e.g., { approve(), reject(), export(), edit() }

  // Cross-pane communication
  onSelectionChange?: (selection: Selection) => void;
  onActionComplete?: (result: ActionResult) => void;

  // Persistence — for work history save/restore
  serializeState(): string;
  restoreState(state: string): void;
}
```

### 6.3 Embedded Wizards

Existing wizard library (`WizardDialog` in `@spaarke/ui-components`) adapts to render as workspace widgets:

| Wizard | Current | R2 |
|--------|---------|-----|
| Create Matter | Modal dialog | Embedded in Workspace, AI pre-fills fields |
| Document Upload | Modal with drag-drop | Embedded in Workspace, AI suggests metadata |
| Search & Select | Modal picker | Embedded in Workspace, AI filters suggestions |

The pattern: wizard renders in the Workspace slot. Chat guides the wizard steps. Context pane shows related info. All three panes coordinate.

---

## 7. Distribution

### 7.1 Primary: Model-Driven App (MDA)

The MDA-hosted version is canonical:
- Full Dataverse auth context (Xrm available)
- Site map navigation, entity form integration
- Side panel on Matter/Project forms
- Command bar buttons on entity forms

### 7.2 Direct URL Access (R1 — Working)

Bookmarkable URL with MSAL popup fallback:
- `/webresources/sprk_spaarkeai`
- Config from `/api/config/client` when Xrm unavailable
- localStorage caching for repeat visits

### 7.3 PWA via Azure Static Web App (R3)

Same codebase, separate build target (`npm run build:swa`):
- Installable desktop app (standalone window)
- Service worker for app-shell caching
- Custom domain (e.g., ai.spaarke.com)

### 7.4 Microsoft Teams Tab (R3)

Static personal tab with Nested App Authentication (NAA):
- No OBO exchange needed
- Works without third-party cookies
- TeamsJS SDK v2.19+ with `createNestablePublicClientApplication`

### 7.5 "Open in Spaarke" Link

Header bar link → full model-driven app. Deep-links to active entity if context available.

---

## 8. Dynamic Capability Orchestration

### 8.1 The Problem

Static playbook selection forces users to know what workflow they need before they start. In practice:
- Users often don't know which playbook fits their intent
- Work naturally crosses capability boundaries ("review this contract... now compare those numbers to our budget")
- Requiring users to manually switch playbooks breaks flow and contradicts the AI-directed vision

### 8.2 Two Operating Modes of SprkChat

| | Mode 1: General Companion | Mode 2: Focused Work |
|--|--|--|
| **When** | Default on launch, between tasks | User selects a playbook OR orchestrator auto-loads one |
| **Behavior** | Answers questions, discovers intent, suggests/triggers playbooks | Domain-specialized conversation + workspace coordination |
| **Tools** | Core tools + capability discovery | Dynamically composed per user intent |
| **Transitions** | → Mode 2 when intent matches a known workflow | → Mode 1 when user exits or work completes |

These modes are NOT mutually exclusive. In Mode 2, the user can still ask general questions. The playbook provides focus — it doesn't restrict the AI to only playbook-related responses.

### 8.3 Capability Library (The Real Asset)

Playbooks are pre-composed bundles. The underlying asset is the capability library:

| Type | What It Is | Example | How Activated |
|------|-----------|---------|---------------|
| **Action** | A discrete operation the AI can invoke | SearchDocuments, CreateRecord, ExtractClauses | Tool definition injected into prompt |
| **Tool** | A computational capability (may call external service) | CodeInterpreter, WebSearch, BingGrounding | Tool definition + service connection |
| **Skill** | Behavioral instructions that change AI approach | "Use plain language", "Cite all sources" | System prompt augmentation |
| **Knowledge** | Grounding data the AI can reference via retrieval | MatterDocuments, StatuteDatabase, ContractSchemas | RAG scope expanded |

A playbook is just a named combination: "Contract Review" = [SearchDocuments + ExtractClauses + LegalResearch + ContractSchemas + "Cite all sources"].

But the orchestrator can compose ad hoc combinations when the user's needs don't fit a pre-built playbook.

### 8.4 Architecture: Two-Tier Orchestration

```
┌─────────────────────────────────────────────────────────────────┐
│  TIER 1: ORCHESTRATOR (always running, lightweight)             │
│                                                                 │
│  Has: Capability INDEX (name + 1-line description, ~500 tokens) │
│  Can: activate_capabilities(names[]), deactivate_capabilities() │
│  Decides: "Do my current tools handle this turn?"               │
│           YES → proceed with current tools                      │
│           NO  → activate what's needed, then proceed            │
│                                                                 │
│  Also knows: pre-built playbooks as "recipes"                   │
│  Can: recognize a pattern and load a full playbook at once      │
├─────────────────────────────────────────────────────────────────┤
│  TIER 2: CAPABILITY LIBRARY (loaded on demand)                  │
│                                                                 │
│  Actions: SearchDocuments, ExtractClauses, QueryEntities...     │
│  Tools: CodeInterpreter, WebSearch, LegalResearch...            │
│  Skills: CiteAllSources, PlainLanguage, LegalPrecision...      │
│  Knowledge: MatterDocs, StatuteDB, ContractSchemas...           │
│  Playbooks: ContractReview, FinancialAnalysis, CaseResearch...  │
└─────────────────────────────────────────────────────────────────┘
```

### 8.5 Capability Manifest (Runtime Catalog)

The orchestrator needs a fast, in-memory catalog of what's available. NOT `.claude/catalogs/` (dev-time) and NOT direct Dataverse queries (too slow per-turn).

**Source of truth:** Dataverse (`sprk_analysisplaybook`, `sprk_aichatcontextmap`, scope/action definitions)

**Runtime representation:** In-memory manifest cached in the BFF, refreshed on deployment or periodic interval.

```
BFF Startup / Refresh:
  1. Query Dataverse for all active capabilities, playbooks, knowledge sources
  2. Build CapabilityManifest (compact index)
  3. Cache in memory (singleton)
  4. Orchestrator's system prompt includes the index

Manifest structure:
  capabilities:
    - name: "SearchDocuments"
      type: action
      description: "Find documents in SPE containers by keyword, metadata, or semantic similarity"
      requires: [authenticated, entity-context]
    - name: "CodeInterpreter"
      type: tool
      description: "Run Python calculations on structured data in Azure sandbox"
      requires: [authenticated, foundry-enabled]
    - name: "ContractSchemas"
      type: knowledge
      description: "Standard clause taxonomy and contract structure definitions"
      scope: "spaarke-references-index"
    ...
  playbooks:
    - name: "Contract Review"
      description: "Review legal documents for risk, compliance, and key terms"
      capabilities: [SearchDocuments, ExtractClauses, LegalResearch, ContractSchemas, CiteAllSources]
    ...
```

### 8.6 Per-Turn Dynamic Tool Injection

The LLM context manages capability loading efficiently:

```
Layer 1 (always present): Capability INDEX
  → ~30 capabilities × 1 line each = ~500 tokens
  → Orchestrator can always "see" what exists and decide what to activate

Layer 2 (dynamic per-turn): Active capability SCHEMAS
  → Only 5-8 full tool definitions loaded = ~1500 tokens
  → These are the tools the LLM can actually CALL this turn
```

**Per-turn flow:**
```
Turn 1: User asks "look at the Smith contract for liability issues"
  → Orchestrator reads index
  → Activates: [SearchDocuments, ExtractClauses, LegalResearch]
  → Knowledge: [MatterDocuments, ContractSchemas]
  → System prompt rebuilt with 3 tool schemas + 2 knowledge scopes

Turn 5: User pivots "compare those indemnity caps to our Q2 budget"
  → Orchestrator: current tools don't cover financial analysis
  → Activates: [CodeInterpreter, QueryEntities]
  → Deactivates: [LegalResearch] (no longer relevant)
  → Knowledge adds: [FinancialData]
  → System prompt rebuilt with updated tool set

Turn 8: User asks "what time is my meeting tomorrow?"
  → Orchestrator: no tool needed, answer conversationally
  → No capability changes
```

**Conversation history is preserved** — only the available tool definitions change. The LLM can still reference earlier analysis results; it just can't re-invoke deactivated tools without re-activating them.

### 8.7 The BFF as Gatekeeper

Even though the orchestrator "requests" capabilities, the BFF validates every activation:

| Check | Purpose |
|-------|---------|
| User permission | Does this user's role allow this capability? |
| Kill switch | Is this capability enabled in the environment? |
| Context compatibility | Does this capability make sense for the current entity/session? |
| Concurrency limits | Would activating this exceed rate limits? |

The orchestrator proposes; the BFF disposes.

### 8.8 Where Playbooks Fit

Playbooks serve three roles in this model:

| Role | Description |
|------|-------------|
| **Accelerator** | User explicitly selects "Contract Review" → loads a proven combination instantly (skip orchestrator discovery) |
| **Template** | Orchestrator recognizes a pattern and auto-loads the matching playbook ("this looks like contract review work") |
| **Guardrail** | For regulated/audited workflows where the capability set MUST be fixed (compliance requirement) — playbook is enforced |

The orchestrator is always free to ADD capabilities beyond what a playbook declares (unless the playbook is marked as a guardrail/exclusive). This means mid-workflow pivots ("now do a financial comparison") don't require exiting the playbook — the orchestrator extends the active set.

### 8.9 Implementation Approach

| Component | What | Where |
|-----------|------|-------|
| CapabilityManifest | In-memory catalog, refreshed from Dataverse | New: `Services/Ai/Capabilities/CapabilityManifest.cs` |
| ManifestRefreshService | Background refresh on interval + deployment hook | New: `Services/Ai/Capabilities/ManifestRefreshService.cs` |
| OrchestratorSystemPrompt | Builds system prompt with index + active schemas | Extend: `SprkChatAgentFactory` |
| activate_capabilities tool | Meta-tool the orchestrator calls to load capabilities | New: `Tools/CapabilityActivationTool.cs` |
| Per-turn tool injection | Rebuild tool list based on active set before each LLM call | Extend: `SprkChatAgentFactory.BuildToolsAsync` |
| Playbook auto-detection | Orchestrator matches intent to known playbook patterns | Extend: `AgentServiceRoutingMiddleware` or new |

### 8.10 Open Design Questions

1. **Manifest refresh frequency** — 15 min polling vs. webhook on Dataverse publish vs. deployment-only?
2. **Maximum active capabilities per turn** — 8? 12? What's the quality cliff for the LLM?
3. **Deactivation heuristics** — After N turns without using a tool, auto-deactivate? Or only on explicit orchestrator decision?
4. **Knowledge scope transitions** — When RAG scope changes mid-conversation, do earlier retrieved chunks stay in context or get invalidated?
5. **Playbook guardrail enforcement** — How does a regulated playbook prevent the orchestrator from adding capabilities? A simple boolean `exclusive` flag?

---

## 9. Technical Debt & Hardening (from R1)

| Item | Priority | Description |
|------|----------|-------------|
| Per-tool error isolation | High | One broken tool should not kill all tools. Catch per-tool in ResolveTools. |
| Default playbook in Dataverse | High | Create "Spaarke AI General" playbook record. Auto-load for standalone. |
| Deploy-AllWebResources.ps1 entry | Low | Add sprk_spaarkeai to the deployment script. |
| spaarke-records-index sync | High | Get Dataverse entity records indexed into AI Search. |

---

## 10. Resolved Design Decisions

| # | Question | Decision |
|---|----------|----------|
| 1 | Pane names | **Conversation, Workspace, Context** — confirmed |
| 2 | Mobile | **Out of scope for R2** |
| 3 | Workspace tab limit | **3 tabs maximum** |
| 4 | Playbook switching | **Orchestrator handles dynamically** (Section 8) |
| 5 | Collaboration | **Out of scope for R2** (future) |
| 6 | Orchestrator model | **Same LLM instance** as chat agent for simplicity; design for future ability to use a cheaper model if feasible (cost optimization) |
| 7 | Capability cost gating | **No explicit confirmation gate** — orchestrator activates as needed without asking user permission |
| 8 | Tenant customization | **Yes — simple on/off per capability at tenant level**; keep implementation lightweight (boolean flags, not complex rule engine) |

---

## 11. Implementation Phases

### R2 Scope (This Project)

| Area | Work Items |
|------|-----------|
| **Dynamic capability orchestration** | CapabilityManifest, per-turn tool injection, activate_capabilities meta-tool, orchestrator system prompt |
| **Capability library** | Define all actions/tools/skills/knowledge as catalog entries in Dataverse; build manifest refresh |
| **Hybrid search** | DataverseQueryTools (OData), spaarke-records-index sync pipeline |
| **Adaptive Context pane** | ContextWidgetRegistry, playbook gallery → entity info → sources → progress |
| **Workspace tabs** | Tab management in center pane, multiple active widgets |
| **Default playbook** | Dataverse record, auto-load in standalone mode (accelerator, not gate) |
| **Work history** | Cosmos DB warm tier, full session persistence, session restore |
| **Prompt library** | Cosmos DB storage, 4-tier ownership, variable templates |
| **Interactive widgets** | Action callbacks, edit mode, serialize/restore state |
| **Embedded wizards** | Adapt WizardDialog for Workspace rendering |
| **Per-tool error isolation** | Catch per-tool in SprkChatAgentFactory.ResolveTools |
| **Pane interaction protocol** | Formal SSE event contract (workspace_widget, context_update, etc.) |

### R3 Scope (Future — Distribution + Polish)

| Area | Work Items |
|------|-----------|
| PWA distribution | Azure Static Web App, manifest.json, service worker |
| Teams Tab | NAA auth, Teams manifest, sideload |
| Command palette | Ctrl+K overlay for power users |
| Adaptive complexity | Guided vs. expert mode based on usage |
| Notification integration | Background AI process completion alerts |
| LLM summarization | Automatic conversation condensation at token threshold |

---

*Draft design document — 2026-05-17. Review and refine before running /design-to-spec.*
