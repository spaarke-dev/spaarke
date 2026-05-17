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

## 8. Technical Debt & Hardening (from R1)

| Item | Priority | Description |
|------|----------|-------------|
| Per-tool error isolation | High | One broken tool should not kill all tools. Catch per-tool in ResolveTools. |
| Default playbook in Dataverse | High | Create "Spaarke AI General" playbook record. Auto-load for standalone. |
| Deploy-AllWebResources.ps1 entry | Low | Add sprk_spaarkeai to the deployment script. |
| spaarke-records-index sync | High | Get Dataverse entity records indexed into AI Search. |

---

## 9. Open Questions

1. **Pane names**: Are "Conversation", "Workspace", "Context" the right names?
2. **Mobile**: Three panes don't work on mobile. Chat-only with swipe to Workspace?
3. **Tab limits**: How many Workspace tabs can be open simultaneously?
4. **Playbook switching**: Should the AI proactively suggest switching playbooks mid-session?
5. **Collaboration**: Can two users share a session? (Future)

---

## 10. Implementation Phases

### R2 Scope (This Project)

| Area | Work Items |
|------|-----------|
| **Hybrid search** | DataverseQueryTools (OData), spaarke-records-index sync pipeline |
| **Adaptive Context pane** | ContextWidgetRegistry, playbook gallery → entity info → sources → progress |
| **Workspace tabs** | Tab management in center pane, multiple active widgets |
| **Default playbook** | Dataverse record, auto-load in standalone mode |
| **Work history** | Cosmos DB warm tier, full session persistence, session restore |
| **Prompt library** | Cosmos DB storage, 4-tier ownership, variable templates |
| **Interactive widgets** | Action callbacks, edit mode, serialize/restore state |
| **Embedded wizards** | Adapt WizardDialog for Workspace rendering |
| **Per-tool error isolation** | Catch per-tool in SprkChatAgentFactory.ResolveTools |
| **Pane interaction protocol** | Formal SSE event contract (workspace_widget, context_update, etc.) |
| **"Open in Spaarke" link** | Header bar deep-link to MDA |

### R3 Scope (Future)

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
