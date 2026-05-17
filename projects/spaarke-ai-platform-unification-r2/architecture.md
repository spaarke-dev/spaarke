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
| **ManifestRefreshService** | BackgroundService | Webhook endpoint + 15-min polling backstop; rebuilds manifest | `Services/Ai/Capabilities/ManifestRefreshService.cs` |
| **CapabilityRouter** | Service | Three-layer router: keyword → GPT-4o-mini → broad superset | `Services/Ai/Capabilities/CapabilityRouter.cs` |
| **OrchestratorPromptBuilder** | Service | Builds stable prompt prefix + per-turn tool schemas | `Services/Ai/Chat/OrchestratorPromptBuilder.cs` |
| **ISprkAgent** | Interface | Abstraction boundary — single impl (R2), multi-agent (R3) | `Services/Ai/Chat/ISprkAgent.cs` |
| **DirectOpenAiAgent** | Service | R2 implementation of ISprkAgent — direct Azure OpenAI call | `Services/Ai/Chat/DirectOpenAiAgent.cs` |
| **PromptShieldService** | Service | Calls Azure AI Content Safety Prompt Shields API | `Services/Ai/Safety/PromptShieldService.cs` |
| **GroundednessCheckService** | Service | Calls Azure AI Content Safety Groundedness API | `Services/Ai/Safety/GroundednessCheckService.cs` |
| **CitationVerificationService** | Service | Custom — verifies legal citations against index | `Services/Ai/Safety/CitationVerificationService.cs` |
| **ConfidenceScoringService** | Service | Scores AI output confidence (high/medium/low) based on source passage count | `Services/Ai/Safety/ConfidenceScoringService.cs` |
| **AuditLogService** | Service | Append-only compliance log (user, action, docs, response hash, safety results) | `Services/Ai/Audit/AuditLogService.cs` |
| **MatterMemoryService** | Service | Persistent per-matter structured facts (parties, deadlines, prior analyses) | `Services/Ai/Memory/MatterMemoryService.cs` |
| **FeedbackService** | Service | Thumbs up/down + text feedback per response, stored in Cosmos | `Services/Ai/Feedback/FeedbackService.cs` |
| **CompareDocumentsTool** | AI Tool | Produces diff/redline between two document versions | `Services/Ai/Chat/Tools/CompareDocumentsTool.cs` |
| **RedlineViewerWidget** | Client widget | Side-by-side diff with AI-narrated material differences | `@spaarke/ai-widgets` |
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
| **SprkChatAgentFactory** | Dynamic tool injection | Router provides tools[] per-turn; factory passes to Azure OpenAI |
| **ChatSessionManager** | Write-through persistence | Every message written to both Redis (hot) AND Cosmos (durable). No idle-flush. |
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
│ 1. Load session from Redis (history, matter context)    │
│ 2. Write message to Cosmos (write-through audit)        │
└───────────────────────────────┬─────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────┐
│ CapabilityRouter (PRE-selects tools[] — NO LLM here)    │
│                                                         │
│ 3. Layer 1: Keyword classify user message (~0ms)        │
│    → If confident: select tools[]                       │
│    → If uncertain: Layer 2                              │
│                                                         │
│ 4. Layer 2: GPT-4o-mini classify (~200-500ms)           │
│    → Input: message + capability index (~600 tokens)    │
│    → Output: ["CodeInterpreter", "QueryEntities"]       │
│    → If classified: select tools[]                      │
│    → If ambiguous: Layer 3 (broad superset)             │
│                                                         │
│ 5. Validate selections against CapabilityManifest:      │
│    - Capability exists? ✓                               │
│    - User has permission? ✓                             │
│    - Tenant toggle enabled? ✓                           │
│    - Kill switch? ✓                                     │
│                                                         │
│ 6. Output: final tools[] for this turn                  │
└───────────────────────────────┬─────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────┐
│ Retrieval + Safety (if tools include search)            │
│                                                         │
│ 7. Execute retrieval tools (SearchDocuments, etc.)      │
│    - Privilege filter applied (user group_ids)          │
│                                                         │
│ 8. Prompt Shields check on retrieved documents          │
│    - If injection detected → BLOCK, return error SSE    │
└───────────────────────────────┬─────────────────────────┘
                                │ (safe)
                                ▼
┌─────────────────────────────────────────────────────────┐
│ OrchestratorPromptBuilder + Azure OpenAI (SINGLE call)  │
│                                                         │
│ 9. Build prompt:                                        │
│    - Stable prefix (persona + capability index)         │
│    - Selected tool schemas (from step 6)                │
│    - Conversation history (last 25 msgs or summary)     │
│    - Retrieved context (from step 7)                    │
│                                                         │
│ 10. Send to Azure OpenAI with tools[] + stream:true     │
│     - LLM calls tools as needed (QueryEntities, etc.)   │
│     - Stream tokens to client via SSE                   │
│                                                         │
│ 11. Emit SSE events as response streams:                │
│     - token → Conversation pane                         │
│     - workspace_widget → Workspace pane (JSON schema)   │
│     - context_update → Context pane                     │
└───────────────────────────────┬─────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────┐
│ Post-LLM Safety (async, after stream completes)         │
│                                                         │
│ 12. Groundedness check (buffered response vs sources)   │
│     → Emit annotation SSE: { ungrounded: [segments] }   │
│                                                         │
│ 13. Citation verification (parse + index lookup)        │
│     → Emit annotation SSE: { citations: [verified/not]} │
│                                                         │
│ 14. Confidence scoring (source passage count)           │
│     → Emit annotation SSE: { confidence: "high" }       │
│                                                         │
│ 15. Write to audit log (Cosmos, append-only):           │
│     user, timestamp, tools called, docs accessed,       │
│     response hash, safety check results                 │
└─────────────────────────────────────────────────────────┘
```

---

## 4. Data Flow: Session Lifecycle

```
┌─────────────┐     ┌─────────────┐     ┌──────────────┐     ┌─────────────┐
│  New Session │     │  Active Work │     │  Redis Evict  │     │  Restore    │
│             │     │             │     │  (24h TTL)   │     │             │
│ • Redis key │────▶│ • Redis     │────▶│ • Redis gone │     │ • Load from │
│   created   │     │   (hot)     │     │ • Cosmos has │◀────│   Cosmos DB │
│ • Cosmos    │     │ • Cosmos    │     │   everything │     │ • Rebuild   │
│   session   │     │   (write-   │     │ • No data    │     │   context   │
│   record    │     │   through)  │     │   loss       │     │ • Summarize │
│ • Router    │     │ • Audit log │     │              │     │   if >25msg │
│   selects   │     │   (every    │     │              │     │ • Restore   │
│   tools     │     │   turn)     │     │              │     │   widgets   │
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

## 6. Latency Architecture

### 6.1 The Three-Layer Routing Pipeline

The core latency optimization: avoid LLM round-trips for capability routing wherever possible.

```
User message arrives at BFF
         │
         ▼
┌─────────────────────────────────────────────────────────────┐
│  LAYER 1: KEYWORD CLASSIFIER (0ms, no LLM)                 │
│                                                             │
│  • Extends AgentServiceRoutingMiddleware (already exists)   │
│  • Pattern matches user message against capability          │
│    descriptions in CapabilityManifest                       │
│  • Confidence threshold: if score > 0.8, activate directly  │
│  • Handles: ~60-70% of turns                               │
│                                                             │
│  IF confident → activate capabilities → single LLM call    │
│  IF uncertain → pass to Layer 2                             │
└───────────────────────────────┬─────────────────────────────┘
                                │ (uncertain)
                                ▼
┌─────────────────────────────────────────────────────────────┐
│  LAYER 2: GPT-4o-MINI PRE-CHECK (200-500ms)                │
│                                                             │
│  Prompt (~600 tokens):                                      │
│    "Given this user message and capability catalog,         │
│     which capabilities are needed?"                         │
│    + user message                                           │
│    + capability index (names + descriptions only)           │
│                                                             │
│  Returns: ["CodeInterpreter", "QueryEntities"]              │
│  Handles: ~20-25% of turns                                  │
│                                                             │
│  IF classified → activate capabilities → single LLM call   │
│  IF ambiguous → pass to Layer 3                             │
└───────────────────────────────┬─────────────────────────────┘
                                │ (ambiguous, rare)
                                ▼
┌─────────────────────────────────────────────────────────────┐
│  LAYER 3: BROAD SUPERSET FALLBACK (0ms, no LLM)            │
│                                                             │
│  • When Layers 1-2 can't classify confidently               │
│  • Use active playbook's full tool set, OR "general"        │
│    superset (core tools + most common extras)               │
│  • More tools in prompt is cheaper than a double LLM call   │
│  • Handles: ~5-10% of turns                                 │
│                                                             │
│  NEVER double-call. Single main LLM call on every turn.     │
└─────────────────────────────────────────────────────────────┘
```

### 6.2 Latency Budget

```
TTLT = TTFT + (TBT × Tokens Generated)

Target prompt budget: ~9000 tokens total
  ├── Capability index:       500 tokens (always present)
  ├── Active tool schemas:  3000 tokens (6-8 tools max)
  ├── System prompt/skills: 1500 tokens
  └── Conversation history: 4000 tokens (summarize beyond)

Expected TTFT at 9000 tokens (GPT-4o): ~800ms-1.2s
Expected TBT (GPT-4o): ~20-30ms per token
```

### 6.3 Model Deployment Map

```
┌──────────────────────────────────────────────────┐
│  Azure OpenAI Resource: spaarke-openai-dev        │
│                                                  │
│  Deployment: "gpt-4o" (main)                     │
│    • Main conversation + tool calling            │
│    • ~9000 token prompts                         │
│    • Streaming enabled                           │
│                                                  │
│  Deployment: "gpt-4o-mini" (utility)             │
│    • Layer 2 capability classification (~600 tok)│
│    • Session summarization (~4000 tok input)     │
│    • Separated to avoid workload mixing          │
│    • Lower cost, lower latency                   │
└──────────────────────────────────────────────────┘
```

### 6.4 Prompt Token Management

| Mechanism | Trigger | Action |
|-----------|---------|--------|
| History summarization | >15 messages OR >4000 history tokens | GPT-4o-mini summarizes older messages; keep last 10 verbatim |
| Tool deactivation | Tool unused for 3 consecutive turns | Remove schema from active set (saves ~400 tokens/tool) |
| Playbook pre-load | User selects playbook OR Layer 1 matches | Load all playbook tools at once (no incremental activation) |
| max_tokens tuning | Per response type | Conversational: 500, tool-heavy: 1000, analysis: 2000 |

---

## 7. Infrastructure Dependencies

| Service | Purpose | Exists Today? | R2 Changes |
|---------|---------|---------------|------------|
| Azure OpenAI (gpt-4o) | Main conversation + tool calling | Yes | Per-turn dynamic tool list |
| Azure OpenAI (gpt-4o-mini) | Capability pre-classification + summarization | **Deployment exists, new usage** | Layer 2 routing + session summarization |
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

## 8. Security Boundaries

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

## 9. AI Safety & Governance Pipeline

```
┌─────────────────────────────────────────────────────────────────────┐
│                   REQUEST PROCESSING PIPELINE                        │
│                                                                     │
│  User message                                                       │
│       │                                                             │
│       ▼                                                             │
│  ┌─────────────────────────────────────────────┐                    │
│  │  ROUTER (Layer 1/2/3 — selects tools[])     │                    │
│  └──────────────────────┬──────────────────────┘                    │
│                         │                                           │
│                         ▼                                           │
│  ┌─────────────────────────────────────────────┐                    │
│  │  RETRIEVAL (SearchDocuments, QueryEntities)  │                    │
│  │                                              │                    │
│  │  ✓ PRIVILEGE FILTER applied to every query   │ ← Security        │
│  │    (user group_ids in AI Search filter)      │                    │
│  └──────────────────────┬──────────────────────┘                    │
│                         │ retrieved docs                             │
│                         ▼                                           │
│  ┌─────────────────────────────────────────────┐                    │
│  │  PROMPT SHIELDS (Azure AI Content Safety)    │ ← Pre-LLM check  │
│  │                                              │                    │
│  │  Scans: user message + retrieved documents   │                    │
│  │  Detects: indirect prompt injection (XPIA)   │                    │
│  │  Action: BLOCK if injection detected         │                    │
│  │  Latency: +50-100ms                          │                    │
│  └──────────────────────┬──────────────────────┘                    │
│                         │ (safe)                                     │
│                         ▼                                           │
│  ┌─────────────────────────────────────────────┐                    │
│  │  AZURE OPENAI (chat/completions)             │                    │
│  │  • Stable prefix (index + persona + skills)  │                    │
│  │  • tools[] selected by router                │                    │
│  │  • Streaming response                        │                    │
│  └──────────────────────┬──────────────────────┘                    │
│                         │ streamed response                          │
│                         ▼                                           │
│  ┌─────────────────────────────────────────────┐                    │
│  │  GROUNDEDNESS CHECK (Content Safety API)     │ ← Post-LLM check │
│  │                                              │                    │
│  │  Input: model response + source documents    │                    │
│  │  Output: grounded / ungrounded segments      │                    │
│  │  Action: annotate ungrounded claims in UI    │                    │
│  │  Latency: +100-200ms (buffered check)        │                    │
│  └──────────────────────┬──────────────────────┘                    │
│                         │                                           │
│                         ▼                                           │
│  ┌─────────────────────────────────────────────┐                    │
│  │  CITATION VERIFICATION (custom service)      │ ← Post-LLM check │
│  │                                              │                    │
│  │  Parse legal citations from response         │                    │
│  │  Check each against statute/case index       │                    │
│  │  Annotate: ✓ verified / ⚠️ unverified        │                    │
│  │  Latency: +50-100ms (async, non-blocking)    │                    │
│  └──────────────────────┬──────────────────────┘                    │
│                         │                                           │
│                         ▼                                           │
│  Stream to client with safety annotations                           │
└─────────────────────────────────────────────────────────────────────┘
```

### Safety Components

| Component | Type | Service | New? |
|-----------|------|---------|------|
| PrivilegeFilterMiddleware | Pre-retrieval | Custom (AI Search filter) | Extend existing |
| PromptShieldService | Pre-LLM | Azure AI Content Safety API | **NEW** |
| GroundednessCheckService | Post-LLM | Azure AI Content Safety API | **NEW** |
| CitationVerificationService | Post-LLM | Custom (AI Search lookup) | **NEW** |

### Infrastructure

| Resource | Purpose | Exists? |
|----------|---------|---------|
| Azure AI Content Safety (S0) | Prompt Shields + Groundedness API | Check — may already exist alongside OpenAI |
| AI Search `privilege_group_ids` field | Per-document security filter | **No — index schema update needed** |
| Citation entries in spaarke-references index | Statute/case verification data | **Partial — needs population** |

---

## 10. Component Dependency Graph

```
                    CapabilityManifest
                    (singleton, in-memory)
                           ▲
                           │ webhook + polling refresh
                           │
                  ManifestRefreshService ◀── Dataverse (source of truth)
                           │
                           │ read by
                           ▼
              ┌─── CapabilityRouter ────────────────────────┐
              │    (keyword → mini → superset fallback)     │
              │                                             │
              │    Output: selected tools[] for this turn   │
              └──────────────────┬──────────────────────────┘
                                 │
                                 ▼
              ┌─── SprkChatAgentFactory (produces ISprkAgent) ───┐
              │                                                  │
              ▼                                                  ▼
   OrchestratorPromptBuilder                      PromptShieldService
   (stable prefix + tools[])                      (pre-LLM safety check)
              │                                                  │
              ▼                                                  │
       Azure OpenAI API (single call) ◀──────────────────────────┘
              │
              ├── Tool calls ──▶ Capability Library
              │                         │
              │                         ├──▶ RagService (+ privilege filter)
              │                         ├──▶ AgentServiceClient (Foundry)
              │                         ├──▶ DataverseService (OData)
              │                         └──▶ SpeFileStore (Graph)
              │
              ├── Response ──▶ GroundednessCheckService (post-LLM)
              │                         │
              │                         ▼
              │               CitationVerificationService (post-LLM)
              │                         │
              │                         ▼
              └── SSE events ──▶ Client (with safety annotations)
                                        │
                                        ├──▶ WorkspaceTabManager
                                        ├──▶ ContextPaneController
                                        └──▶ SprkChat (text + suggestions)
```

---

## 12. Completeness Checklist

| Requirement | Component | Status |
|-------------|-----------|--------|
| AI orchestrates capabilities dynamically | CapabilityManifest + Router + per-turn tools[] injection | Designed |
| User never needs to select a playbook | Router auto-selects tools based on intent | Designed |
| Playbooks work as accelerators | Pre-composed bundles loadable by router | Designed |
| Single LLM call per turn always | Router pre-selects tools[]; no meta-tools, no double-call | Designed |
| Work persists across sessions | Cosmos DB warm tier + session restore | Designed |
| Three panes coordinate via events | SSE event types + client-side routing | Designed (extends R1) |
| Workspace supports multiple items | Tab manager, max 3 tabs | Designed |
| Context pane adapts to workflow | ContextWidgetRegistry + stage-based rendering | Designed |
| Hybrid search (semantic + structured) | AI Search (discovery) + OData (detail/action) | Designed |
| Tenant can disable capabilities | Boolean toggles in Dataverse, cached in manifest | Designed |
| Sessions restore quickly (<500ms) | Cosmos read + LLM summary + widget snapshot | Designed |
| Prompt library for reuse | Cosmos DB storage, 4-tier ownership | Designed |
| Capability catalog stays current | ManifestRefreshService (webhook + polling backstop) | Designed |
| Latency stays acceptable (<3s typical) | Router pre-selects; single LLM call; prompt caching | Designed |
| Model tiering reduces cost + latency | GPT-4o-mini for classification + summarization | Designed |
| Prompt stays within budget (~9000 tokens) | Token budget per component + auto-summarization | Designed |
| Latency is monitored | Azure Monitor: TTFT, TBT, TTLT, prompt token tracking | Designed |
| Prompt injection defense | PromptShieldService (Azure AI Content Safety) | Designed |
| Groundedness verification | GroundednessCheckService (Content Safety API) | Designed |
| Citation verification | CitationVerificationService (custom, deterministic) | Designed |
| Privilege-aware retrieval | Security filter on AI Search queries (group_ids) | Designed |
| Multi-agent boundary (R3 prep) | ISprkAgent interface; DirectOpenAiAgent impl | Designed |

### Potential Gaps / Items to Confirm

| # | Question | Impact if Missing |
|---|----------|-------------------|
| 1 | **Cosmos DB provisioning** — who provisions and when? | Blocks session persistence development |
| 2 | **spaarke-records-index sync** — how to trigger initial population? | DataverseQueryTools can work without it (OData direct), but discovery is degraded |
| 3 | **Widget state serialization** — do all R1 widgets already support serialize/restore? | May need widget updates for session restore |
| 4 | **Azure AI Content Safety** — verify resource exists or provision. Check regional availability for Groundedness API. | Blocks safety perimeter implementation |
| 5 | **Citation index population** — what statute/case data to seed into spaarke-references? | Citation verification has no data to check against |
| 6 | **Layer 2 latency validation** — is GPT-4o-mini classification truly 200-500ms in practice? Need to benchmark. | If slower, Layer 1 keyword coverage must be broader |
| 7 | **Separate Azure OpenAI deployments** — should GPT-4o-mini have its own deployment? (Microsoft recommends separation) | Latency degradation from mixed workloads |
| 8 | **Privilege group mapping** — how do we map user → matter access → group_ids for AI Search filter? | Privilege filtering has no enforcement data |

---

*Draft architecture document — 2026-05-17. Review alongside design.md before running /design-to-spec.*
