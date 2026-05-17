# Spaarke AI Platform R2 — System Architecture

> **Status**: Draft — Review before /design-to-spec
> **Date**: 2026-05-17
> **Purpose**: Component interaction map confirming all required components for R2 delivery

---

## 1. High-Level System Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        BROWSER (SpaarkeAi Code Page)                         │
│                                                                             │
│  ┌──────────────┐  ┌─────────────────────┐  ┌───────────────────────────┐  │
│  │ CONVERSATION │  │     WORKSPACE       │  │        CONTEXT            │  │
│  │              │  │                     │  │                           │  │
│  │  SprkChat    │  │  WorkspaceWidget    │  │  ContextWidgetRegistry    │  │
│  │  component   │  │  Registry           │  │  (playbook gallery,       │  │
│  │              │  │  (tabs, max 3)      │  │   entity info, sources,   │  │
│  │  SSE stream  │  │                     │  │   progress)               │  │
│  │  ←──────────────┤  Cross-pane events  ├──┤                           │  │
│  └──────┬───────┘  └──────────┬──────────┘  └─────────────┬─────────────┘  │
│         │                     │                            │                │
│         └─────────────────────┼────────────────────────────┘                │
│                               │ SSE connection                              │
└───────────────────────────────┼─────────────────────────────────────────────┘
                                │ HTTPS
┌───────────────────────────────┼─────────────────────────────────────────────┐
│                        BFF API (Sprk.Bff.Api)                               │
│                                                                             │
│  ┌────────────────────────────────────────────────────────────────────────┐ │
│  │                      ORCHESTRATION LAYER                               │ │
│  │                                                                        │ │
│  │  ┌──────────────────┐    ┌──────────────────┐    ┌─────────────────┐  │ │
│  │  │  ChatEndpoints   │    │ SprkChatAgent     │    │  Capability     │  │ │
│  │  │  (SSE streaming) │───▶│ Factory           │───▶│  Manifest       │  │ │
│  │  └──────────────────┘    │                   │    │  (in-memory)    │  │ │
│  │                          │ • Builds system   │    │                 │  │ │
│  │                          │   prompt per-turn │    │ • Actions index │  │ │
│  │                          │ • Injects active  │    │ • Tools index   │  │ │
│  │                          │   tool schemas    │    │ • Skills index  │  │ │
│  │                          │ • Manages context │    │ • Knowledge idx │  │ │
│  │                          └────────┬──────────┘    │ • Playbook idx  │  │ │
│  │                                   │               └────────┬────────┘  │ │
│  │                                   │                        │           │ │
│  │  ┌────────────────────────────────┼────────────────────────┼────────┐  │ │
│  │  │         CAPABILITY ACTIVATION (per-turn)                │        │  │ │
│  │  │                                │                        │        │  │ │
│  │  │  activate_capabilities() ◀─────┘    refresh() ◀─────────┘        │  │ │
│  │  │         │                            (startup + periodic)         │  │ │
│  │  │         ▼                                                        │  │ │
│  │  │  ┌─────────────────────────────────────────────────────────────┐ │  │ │
│  │  │  │              ACTIVE TOOL SET (5-8 per turn)                 │ │  │ │
│  │  │  │                                                             │ │  │ │
│  │  │  │  Layer 1 (always): Capability index (~500 tokens)           │ │  │ │
│  │  │  │  Layer 2 (dynamic): Full schemas for active tools only      │ │  │ │
│  │  │  └─────────────────────────────────────────────────────────────┘ │  │ │
│  │  └──────────────────────────────────────────────────────────────────┘  │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
│                                                                             │
│  ┌────────────────────────────────────────────────────────────────────────┐ │
│  │                      CAPABILITY LIBRARY (Tier 2)                       │ │
│  │                                                                        │ │
│  │  ACTIONS           TOOLS              SKILLS           KNOWLEDGE       │ │
│  │  ─────────         ─────              ──────           ─────────       │ │
│  │  SearchDocuments   CodeInterpreter    CiteAllSources   MatterDocs      │ │
│  │  ExtractClauses    WebSearch          PlainLanguage    ContractSchemas  │ │
│  │  QueryEntities     LegalResearch      LegalPrecision   StatuteDB       │ │
│  │  CreateRecord      BingGrounding      FormalTone       FinancialData   │ │
│  │  GetEntityDetail   DataAnalysis                        CaseLaw         │ │
│  │  RefineText                                                            │ │
│  │  GenerateSummary                                                       │ │
│  │                                                                        │ │
│  │  PLAYBOOKS (pre-composed bundles)                                      │ │
│  │  ──────────────────────────────────                                    │ │
│  │  Contract Review = [Search, Extract, LegalResearch, ContractSchemas]   │ │
│  │  Financial Analysis = [Search, QueryEntities, CodeInterpreter, Data]   │ │
│  │  Case Research = [Search, WebSearch, LegalResearch, CaseLaw, Cite]     │ │
│  │  General Assistant = [Search, Summarize, RefineText, QueryEntities]    │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
│                                                                             │
│  ┌────────────────────────────────────────────────────────────────────────┐ │
│  │                      SSE EVENT EMITTER                                 │ │
│  │                                                                        │ │
│  │  token → Conversation pane (streaming text)                            │ │
│  │  workspace_widget → Workspace pane (load/update widget + data)         │ │
│  │  context_update → Context pane (entity info, sources, progress)        │ │
│  │  context_highlight → Context pane (scroll to citation)                 │ │
│  │  suggestions → Conversation pane (action chips)                        │ │
│  │  workspace_action → Workspace pane (trigger action on existing widget) │ │
│  │  capability_change → Internal (tools activated/deactivated this turn)  │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
│                                                                             │
│  ┌────────────────────────────────────────────────────────────────────────┐ │
│  │                      SESSION & PERSISTENCE                             │ │
│  │                                                                        │ │
│  │  ChatSessionManager                                                    │ │
│  │  • Active session state (Redis, 24h TTL)                               │ │
│  │  • Work history persistence (Cosmos DB, 90 day)                        │ │
│  │  • Workspace state snapshots (widget data, tab config)                 │ │
│  │  • Active capabilities per session                                     │ │
│  │  • LLM summarization at token threshold                                │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
│                                                                             │
│  ┌────────────────────────────────────────────────────────────────────────┐ │
│  │                      MANIFEST REFRESH SERVICE                          │ │
│  │                                                                        │ │
│  │  BackgroundService: queries Dataverse on startup + periodic refresh     │ │
│  │  Builds CapabilityManifest from:                                       │ │
│  │    • sprk_analysisplaybook (playbook definitions)                      │ │
│  │    • sprk_aichatcontextmap (context mappings)                          │ │
│  │    • sprk_aiscope / scope-model-index (capability definitions)         │ │
│  │    • Tenant-level capability toggles (on/off flags)                    │ │
│  │  Output: singleton CapabilityManifest (in-memory, <1ms access)         │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────┘
                                │
                    ┌───────────┼───────────────────────┐
                    │           │                       │
                    ▼           ▼                       ▼
┌──────────────────────┐  ┌──────────┐  ┌──────────────────────────┐
│   Azure OpenAI       │  │  Redis   │  │  Azure Cosmos DB         │
│                      │  │          │  │                          │
│  • gpt-4o (chat)     │  │  • Hot   │  │  • Work history (warm)   │
│  • System prompt +   │  │    session│  │  • Session restore       │
│    active tool       │  │  • OBO   │  │  • Prompt library         │
│    schemas per-turn  │  │    cache │  │  • Agent thread storage   │
│  • Streaming tokens  │  │  • Rate  │  │    (Foundry BYO)         │
│                      │  │    limits│  │                          │
└──────────────────────┘  └──────────┘  └──────────────────────────┘
          │
          │ (when orchestrator activates Foundry-backed tools)
          ▼
┌──────────────────────────────────────────────────┐
│   Azure AI Foundry Agent Service                 │
│                                                  │
│  • CodeInterpreter (Python sandbox)              │
│  • Bing Grounding (web search with citations)    │
│  • Agent threads (stateful multi-turn)           │
│  • Knowledge retrieval (Foundry-managed RAG)     │
└──────────────────────────────────────────────────┘

┌──────────────────────────┐  ┌────────────────────────────┐
│   Azure AI Search        │  │   Dataverse                │
│                          │  │                            │
│  • spaarke-docs-index    │  │  • Entity records (CRUD)   │
│    (SPE doc content)     │  │  • Playbook definitions    │
│  • spaarke-records-index │  │  • Scope/capability defs   │
│    (entity discovery)    │  │  • Context mappings        │
│  • spaarke-references    │  │  • Tenant config (toggles) │
│    (knowledge base)      │  │  • Source of truth for     │
│                          │  │    CapabilityManifest      │
└──────────────────────────┘  └────────────────────────────┘
```

---

## 2. Component Inventory

### 2.1 New Components (R2)

| Component | Type | Purpose | Location |
|-----------|------|---------|----------|
| **CapabilityManifest** | Singleton service | In-memory catalog of all available capabilities | `Services/Ai/Capabilities/CapabilityManifest.cs` |
| **ManifestRefreshService** | BackgroundService | Loads manifest from Dataverse at startup + periodic refresh | `Services/Ai/Capabilities/ManifestRefreshService.cs` |
| **CapabilityActivationTool** | AI Tool | Meta-tool the orchestrator calls to load/unload capabilities | `Services/Ai/Chat/Tools/CapabilityActivationTool.cs` |
| **OrchestratorPromptBuilder** | Service | Builds system prompt with capability index + active schemas | `Services/Ai/Chat/OrchestratorPromptBuilder.cs` |
| **WorkspaceWidgetRegistry** | Client library | Registry for workspace pane widgets (lazy-load) | `@spaarke/ai-widgets` or extend `@spaarke/ai-outputs` |
| **ContextWidgetRegistry** | Client library | Registry for context pane widgets (lazy-load) | `@spaarke/ai-widgets` or extend `@spaarke/ai-outputs` |
| **SessionPersistenceService** | Service | Writes work history to Cosmos DB (messages, widgets, artifacts) | `Services/Ai/Sessions/SessionPersistenceService.cs` |
| **SessionRestoreService** | Service | Loads + reconstructs session from Cosmos DB | `Services/Ai/Sessions/SessionRestoreService.cs` |
| **DataverseQueryTools** | AI Tool | OData-based entity query/detail tool | `Services/Ai/Chat/Tools/DataverseQueryTools.cs` |
| **WorkspaceTabManager** | Client component | Manages up to 3 workspace tabs | `SpaarkeAi/src/components/WorkspaceTabManager.tsx` |
| **ContextPaneController** | Client component | Adapts context pane based on workflow state | `SpaarkeAi/src/components/ContextPaneController.tsx` |
| **PromptLibraryService** | Service | CRUD for saved prompt templates (Cosmos DB) | `Services/Ai/PromptLibrary/PromptLibraryService.cs` |
| **RecordSyncJob** | BackgroundService | Syncs Dataverse records to spaarke-records-index | `Services/Jobs/RecordSyncJob.cs` |

### 2.2 Extended Components (from R1)

| Component | Extension | What Changes |
|-----------|-----------|--------------|
| **SprkChatAgentFactory** | Dynamic tool injection | Accepts `activeCapabilities` set per-turn instead of static playbook capabilities |
| **ChatSessionManager** | Cosmos DB tier | Flush to Cosmos on session idle/close; restore from Cosmos |
| **ChatEndpoints** | SSE event types | New event types: workspace_widget, context_update, workspace_action |
| **SprkChat** (UI) | Pane coordination | Dispatches/subscribes cross-pane events |
| **ThreePaneLayout** | Tab support | Workspace pane supports tabbed multi-widget layout (max 3) |
| **AgentServiceRoutingMiddleware** | Orchestrator integration | Routing decisions informed by capability manifest |

### 2.3 Existing Components (Unchanged)

| Component | Role in R2 |
|-----------|-----------|
| AgentServiceClient | Called when Foundry-backed tools are activated |
| AgentServiceNodeExecutor | Playbook pipeline execution (structured mode) |
| CodeInterpreterTools / CodeInterpreterBridge | Available as capability in library |
| LegalResearchTools | Available as capability in library |
| WebSearchTools | Available as capability in library |
| RagService | Backend for SearchDocuments / knowledge retrieval |
| SpeFileStore | Document operations facade |
| StandaloneChatContextProvider | Context resolution for standalone launch |

---

## 3. Data Flow: Capability Activation

```
User sends message: "Compare indemnity caps to our Q2 budget"
                            │
                            ▼
┌─────────────────────────────────────────────────────────┐
│ ChatEndpoints.SendMessageAsync                          │
│                                                         │
│ 1. Load session from Redis (active capabilities, history)│
│ 2. Call SprkChatAgentFactory.CreateAgentAsync            │
└───────────────────────────────┬─────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────┐
│ SprkChatAgentFactory                                    │
│                                                         │
│ 3. Build system prompt:                                 │
│    - Capability INDEX (compact, always present)         │
│    - Active tool SCHEMAS (from session state)           │
│    - Conversation history (last N messages + summary)   │
│                                                         │
│ 4. Send to Azure OpenAI                                 │
└───────────────────────────────┬─────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────┐
│ Azure OpenAI Response                                   │
│                                                         │
│ 5. LLM decides: "I need CodeInterpreter + QueryEntities │
│    but they're not in my active tools"                  │
│                                                         │
│ 6. LLM calls: activate_capabilities(                    │
│      ["CodeInterpreter", "QueryEntities"])              │
└───────────────────────────────┬─────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────┐
│ CapabilityActivationTool.Execute                        │
│                                                         │
│ 7. Validate against CapabilityManifest:                 │
│    - Capability exists? ✓                               │
│    - User has permission? ✓                             │
│    - Tenant toggle enabled? ✓                           │
│    - Kill switch (feature flag)? ✓                      │
│                                                         │
│ 8. Update session active capabilities in Redis          │
│ 9. Return confirmation to LLM                           │
└───────────────────────────────┬─────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────┐
│ SprkChatAgentFactory (SECOND LLM call this turn)        │
│                                                         │
│ 10. Rebuild system prompt with updated tool schemas:    │
│     - Now includes CodeInterpreter + QueryEntities      │
│     - May deactivate LegalResearch (no longer relevant) │
│                                                         │
│ 11. LLM executes with new tools:                        │
│     - Calls QueryEntities("sprk_budget", filter)        │
│     - Calls CodeInterpreter(comparison code)            │
│                                                         │
│ 12. Emit SSE events:                                    │
│     - token → streaming comparison text                 │
│     - workspace_widget → BudgetDashboard widget         │
│     - context_update → financial data summary           │
└─────────────────────────────────────────────────────────┘
```

---

## 4. Data Flow: Session Lifecycle

```
┌─────────────┐     ┌─────────────┐     ┌──────────────┐     ┌─────────────┐
│  New Session │     │  Active Work │     │  Session Idle │     │  Restore    │
│             │     │             │     │  (>5 min)    │     │             │
│ • Redis key │────▶│ • Redis     │────▶│ • Flush to   │     │ • Load from │
│   created   │     │ • Messages  │     │   Cosmos DB  │◀────│   Cosmos DB │
│ • Core tools│     │ • Tool calls│     │ • Summarize  │     │ • Rebuild   │
│   active    │     │ • Widget    │     │   if >15 msgs│     │   context   │
│ • No history│     │   state     │     │ • Snapshot   │     │ • Restore   │
│             │     │ • Active    │     │   workspace  │     │   widgets   │
│             │     │   caps      │     │ • Keep Redis │     │ • Resume    │
│             │     │             │     │   24h TTL    │     │   agent     │
└─────────────┘     └─────────────┘     └──────────────┘     └─────────────┘
```

---

## 5. Data Flow: Cross-Pane Coordination

```
User clicks citation [3] in Workspace widget
         │
         ▼
WorkspaceWidget dispatches CrossPaneLinkEvent({ citationId: 3 })
         │
         ▼
ThreePaneLayout event bus routes to Context pane
         │
         ▼
ContextPaneController receives event
         │
         ▼
ContextWidgetRegistry scrolls to citation [3] in source viewer

─────────────────────────────────────────────────────────────

AI tool emits workspace_widget SSE event
         │
         ▼
SprkChat SSE handler routes event by type
         │
         ├──▶ workspace_widget → WorkspaceTabManager
         │    • Creates or updates tab with widget type + data
         │    • Max 3 tabs (oldest auto-closed if exceeded)
         │
         ├──▶ context_update → ContextPaneController
         │    • Replaces or updates context widget
         │    • Adapts based on workflow stage
         │
         └──▶ suggestions → SprkChat suggestion chips
              • Rendered as InteractionTag components
```

---

## 6. Infrastructure Dependencies

| Service | Purpose | Exists Today? | R2 Changes |
|---------|---------|---------------|------------|
| Azure OpenAI (gpt-4o) | Chat completions + tool calling | Yes | Per-turn dynamic tool list |
| Redis | Hot session cache | Yes | Add active capabilities field |
| Azure Cosmos DB | Work history + prompt library | **No — NEW** | Provision serverless instance |
| Azure AI Search | Document + record discovery | Yes | Fix spaarke-records-index sync |
| Azure AI Foundry | CodeInterpreter, Bing, agent threads | Yes | No changes (called via existing AgentServiceClient) |
| Dataverse | Entity CRUD + capability definitions | Yes | Add tenant capability toggles |
| Azure App Service | BFF API host | Yes | No infra changes |

### New Infrastructure to Provision

| Resource | SKU | Estimated Cost | Purpose |
|----------|-----|---------------|---------|
| **Cosmos DB account** | Serverless | ~$5-15/month at law firm scale | Work history, prompt library, session restore |
| **Cosmos DB database** | `spaarke-ai` | (included) | Container for session data |
| **Cosmos DB containers** | `sessions`, `prompts` | (included) | Partitioned by tenantId/userId |

---

## 7. Security Boundaries

| Boundary | Enforcement | Notes |
|----------|-------------|-------|
| User → BFF | MSAL token (Azure AD) | Existing — no change |
| BFF → Azure OpenAI | Managed Identity / API key | Existing — no change |
| BFF → Cosmos DB | Managed Identity (RBAC) | New — use DefaultAzureCredential |
| BFF → Dataverse | OBO token | Existing — no change |
| BFF → AI Foundry | Managed Identity | Existing — no change |
| Capability activation | BFF validates per-request | Checks: user role, tenant toggle, kill switch |
| Tenant isolation | Cosmos partition key = tenantId | Sessions isolated by tenant; no cross-tenant queries |
| Capability toggles | Tenant-level boolean flags | Admin-configurable in Dataverse; cached in manifest |

---

## 8. Component Dependency Graph

```
                    CapabilityManifest
                    (singleton, in-memory)
                           ▲
                           │ refreshes from
                           │
                  ManifestRefreshService ◀── Dataverse (source of truth)
                           │
                           │ read by
                           ▼
              ┌─── SprkChatAgentFactory ───┐
              │                            │
              ▼                            ▼
   OrchestratorPromptBuilder      CapabilityActivationTool
              │                            │
              ▼                            ▼
       Azure OpenAI API            Session active caps (Redis)
              │
              ├── Tool calls ──▶ Capability Library (Actions, Tools)
              │                         │
              │                         ├──▶ RagService (AI Search)
              │                         ├──▶ AgentServiceClient (Foundry)
              │                         ├──▶ DataverseService (OData)
              │                         └──▶ SpeFileStore (Graph)
              │
              └── SSE events ──▶ Client (pane routing)
                                        │
                                        ├──▶ WorkspaceTabManager
                                        ├──▶ ContextPaneController
                                        └──▶ SprkChat (text + suggestions)
```

---

## 9. Completeness Checklist

| Requirement | Component | Status |
|-------------|-----------|--------|
| AI orchestrates capabilities dynamically | CapabilityManifest + ActivationTool + per-turn injection | Designed |
| User never needs to select a playbook | Orchestrator auto-activates based on intent | Designed |
| Playbooks work as accelerators | Pre-composed bundles loadable by orchestrator | Designed |
| Work persists across sessions | Cosmos DB warm tier + session restore | Designed |
| Three panes coordinate via events | SSE event types + client-side routing | Designed (extends R1) |
| Workspace supports multiple items | Tab manager, max 3 tabs | Designed |
| Context pane adapts to workflow | ContextWidgetRegistry + stage-based rendering | Designed |
| Hybrid search (semantic + structured) | AI Search (discovery) + OData (detail/action) | Designed |
| Tenant can disable capabilities | Boolean toggles in Dataverse, cached in manifest | Designed |
| Sessions restore quickly (<500ms) | Cosmos read + LLM summary + widget snapshot | Designed |
| Prompt library for reuse | Cosmos DB storage, 4-tier ownership | Designed |
| Capability catalog stays current | ManifestRefreshService (startup + periodic) | Designed |

### Potential Gaps / Items to Confirm

| # | Question | Impact if Missing |
|---|----------|-------------------|
| 1 | **Cosmos DB provisioning** — who provisions and when? | Blocks session persistence development |
| 2 | **spaarke-records-index sync** — how to trigger initial population? | DataverseQueryTools can work without it (OData direct), but discovery is degraded |
| 3 | **Widget state serialization** — do all R1 widgets already support serialize/restore? | May need widget updates for session restore |
| 4 | **LLM summarization model** — gpt-4o-mini for cost, or same gpt-4o? | Cost vs quality tradeoff for summarization |
| 5 | **Deactivation heuristics** — when does the orchestrator unload unused tools? | Context bloat if tools accumulate without cleanup |
| 6 | **Multi-turn capability activation** — does activation cost an extra LLM round-trip? | Latency impact (one extra call when tools change) |

---

*Draft architecture document — 2026-05-17. Review alongside design.md before running /design-to-spec.*
