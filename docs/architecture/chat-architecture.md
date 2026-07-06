# Chat Architecture

> **Last Updated**: 2026-07-06
> **Last Reviewed**: 2026-07-06
> **Reviewed By**: spaarke-ai-architecture-redesign-r1 (tasks 034/035/036 — FR-P2-05/06/07 hard cutover)
> **Status**: Current
> **Purpose**: Describes the SprkChat conversational AI subsystem — session management, the agent-turn loop (the ONE dispatch protocol, ADR-039), the unified confirmation gate, and the streaming response pipeline.

---

## Overview

SprkChat is the embedded conversational AI feature within the Spaarke platform. It provides playbook-driven, entity-scoped chat sessions where every NL utterance enters the agent-turn loop (`SprkChatAgent`) — the ONE dispatch protocol per ADR-039. Capabilities resolve through projected Binding tools from the closed catalog; write/communicate side effects suspend into the unified confirmation gate (`PendingPlanManager`) at the dispatch seam. The function-invocation `IChatClient` streams responses through a middleware pipeline (telemetry, cost control, content safety). The legacy classifier stack (compound-intent pre-pass, two-stage playbook dispatcher, intent reranker, candidate selector, playbook-embeddings index) was DELETED by ai-architecture-redesign-r1 tasks 034/035/036 (FR-P2-05/06/07).

The key architectural decision is the **Agent Framework pattern** — each chat session gets a transient `SprkChatAgent` instance created by a factory, with system prompts sourced from playbook Action records in Dataverse rather than hardcoded.

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| SprkChatAgent | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs` | Core agent: the agent-turn loop — streaming responses via IChatClient, projected tool registration, citation enforcement |
| SprkChatAgentFactory | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` | Constructs agents with context, tools, middleware pipeline; manages context switching |
| ISprkChatAgent | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ISprkChatAgent.cs` | Agent interface enabling middleware decorator pattern |
| ChatSessionManager | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatSessionManager.cs` | Session lifecycle (create/get/delete); Redis hot cache with 24h TTL, Dataverse cold storage |
| ChatHistoryManager | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatHistoryManager.cs` | Message persistence, summarisation at 15 messages, archive at 50 messages |
| PendingPlanManager | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PendingPlanManager.cs` | THE unified confirmation gate (D12/FR-P2-02): Redis store for suspended invocations (30-min TTL) + SessionGate ledger markers (ADR-040) |
| PlaybookChatContextProvider | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs` | Resolves ChatContext from playbook Action record, knowledge scopes, entity enrichment |
| DynamicCommandResolver | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/DynamicCommandResolver.cs` | Metadata-driven command catalog from system + playbook + scope capability sources |
| AnalysisChatContextResolver | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/AnalysisChatContextResolver.cs` | Resolves analysis-scoped context from sprk_analysisoutput and related records |
| ChatContextMappingService | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatContextMappingService.cs` | Four-tier playbook resolution by entityType + pageType from sprk_aichatcontextmapping |
| ChatDataverseRepository | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatDataverseRepository.cs` | Dataverse persistence for sprk_aichatsummary and sprk_aichatmessage entities |
| DocumentContextService | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/DocumentContextService.cs` | Document-level context resolution for chat sessions |
| AgentTelemetryMiddleware | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentTelemetryMiddleware.cs` | Outermost middleware: logs session ID, token count estimates, latency |
| AgentCostControlMiddleware | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentCostControlMiddleware.cs` | Enforces per-session token budget (default 10,000 tokens) |
| AgentContentSafetyMiddleware | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Middleware/AgentContentSafetyMiddleware.cs` | Scans response tokens for PII patterns (SSN, credit card, email); replaces with "[content filtered]" |

## Data Flow

### Standard Message Flow

1. **ChatEndpoints** receives POST `/api/ai/chat/sessions/{id}/messages` with user message
2. **ChatSessionManager** retrieves session from Redis (hot path) or Dataverse (cold path)
3. **SprkChatAgentFactory** creates/retrieves the agent with playbook context and the projected closed-catalog tool set (`AgentToolCatalogProjector`)
4. **SprkChatAgent.SendMessageAsync** streams the agent-turn loop via the function-invocation client — capabilities invoke through projected `BindingCapabilityTool`s; write/communicate dispositions suspend into the unified gate (`PendingPlanManager`) at the dispatch seam (`SessionDispatchOrchestrator`); off-catalog requests refuse via the `no_match_handler` Binding (`RefusalCapabilityTool`)
5. **Middleware pipeline** (telemetry -> cost control -> content safety) wraps the streaming response
6. **ChatHistoryManager** persists the message to Dataverse and updates Redis cache; the turn's ToolChain is ledger-written BEFORE rendering (ADR-040)
7. **Summarisation** triggers at 15 messages; **archive** triggers at 50 messages

Suspended invocations resume through `POST /api/ai/chat/sessions/{sessionId}/gates/{gateId}/resolve` — the single gate-resolution surface.

### Context Mapping Resolution

1. **ChatContextMappingService** resolves playbook(s) using four-tier precedence: exact match (entityType + pageType) -> entity + any -> wildcard + pageType -> global fallback
2. Results cached in Redis with 30-minute sliding TTL

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | Playbook System | `IPlaybookService`, `IScopeResolverService` | Loads Action records, scope definitions |
| Depends on | Azure OpenAI | `IChatClient` | Function-invocation client (agent-turn loop) |
| Depends on | Redis | `IDistributedCache` | Sessions, plans, context mappings, command catalogs |
| Depends on | Dataverse | `IChatDataverseRepository`, `IGenericEntityService` | Cold storage, entity queries |
| Consumed by | ChatEndpoints | SSE streaming | `/api/ai/chat/sessions/{id}/messages` |
| Consumed by | AiToolAgent PCF | WebSocket/SSE | Embedded chat UI component |

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| ONE dispatch protocol | Agent-turn loop is the sole text-path dispatcher; classifier stack deleted | No second intent-detection mechanism anywhere | ADR-039 |
| Redis-first session storage | 24h sliding TTL, Dataverse fallback | Low-latency reads; Dataverse for audit trail | ADR-009, ADR-014 |
| Middleware pipeline via decorator | ISprkChatAgent chain | Telemetry, cost control, content safety without modifying core agent | ADR-013 |
| Tenant-scoped cache keys | `chat:session:{tenantId}:{sessionId}` | Multi-tenant isolation | ADR-014 |
| ONE confirmation gate | Side effects suspend into PendingPlanManager by declared `side_effect_class`; ledger markers precede gate UI | Storage precedes rendering | ADR-040, ADR-039 |

## Constraints

- **MUST**: System prompts originate from playbook Action (ACT-*) records, not hardcoded
- **MUST**: Write/communicate-declared invocations suspend into the unified gate before execution (FR-P2-02)
- **MUST**: Cache keys include tenantId for multi-tenant isolation (ADR-014)
- **MUST**: Content safety middleware never logs matched content, only pattern type
- **MUST NOT**: Exceed 10,000 tokens per session without explicit budget override
- **MUST NOT**: Store PendingPlan inside ChatSession (avoids inflating every session read)

## Known Pitfalls

- **Dataverse entity deployment**: ChatSessionManager and ChatHistoryManager handle `InvalidOperationException` gracefully when chat entities (sprk_aichatsummary, sprk_aichatmessage) are not yet deployed. Redis continues to function as the primary store.
- **Gate resume race**: The two-step get+delete in PendingPlanManager's invocation store is not truly atomic via IDistributedCache. This is acceptable because gate resolution is a low-frequency deliberate user action and the second racer finds no key (409).
- **Summarisation placeholder**: Phase 1 summarisation generates a placeholder summary. Real LLM-based summarisation is deferred to AIPL-054.

## R2: ConversationPane as Chat Host

In R2, the `ConversationPane` component replaces the direct SprkChat mounting used in R1 (LeftPane + ChatPanel). ConversationPane is the left slot of the `ThreePaneShell` and provides:

| Responsibility | R1 | R2 |
|---------------|----|----|
| Chat host | ChatPanel mounts SprkChat directly | ConversationPane wraps SprkChat with tab bar, playbook header, selection chip |
| Session state | `useStandaloneAi()` (StandaloneAiProvider) | `useAiSession()` (AiSessionProvider) |
| SSE event routing | Single-subscriber ref (`streaming?.onPaneEvent`) | `PaneEventBus` multi-subscriber channels via `streaming.onPaneEvent` |
| Playbook selection | In-SprkChat only | PlaybookGalleryWidget dispatches `playbook-selected` on conversation channel |
| Text selection refinement | Not supported | `selection_changed` event on workspace channel drives "Refine this?" chip |

**Source**: `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx`

---

## R2: PaneEventBus Cross-Pane Communication

R2 replaces R1's single-listener DOM CustomEvent bus with a typed, multi-subscriber, DOM-free `PaneEventBus`. Each channel carries a discriminated union of event payloads with compile-time type checking.

**4 channels**:

| Channel | Purpose | Event Types |
|---------|---------|-------------|
| `workspace` | Widget lifecycle, tab navigation, text selection, wizard control | `widget_load`, `widget_update`, `widget_action`, `tab_change`, `tab_count_change`, `selection_changed`, `tabs_clear`, `wizard_step`, `entity_resolved`, `session_reset` |
| `context` | Document context updates, citation highlights | `context_update`, `context_highlight`, `stage_change` |
| `conversation` | User input, playbook changes, first message | `suggestion`, `playbook_change`, `playbook-selected`, `refine_request`, `first_message` |
| `safety` | Groundedness annotations, capability changes | `safety_annotation`, `capability_change` |

**Total**: 20 event types across 4 channels.

**Key source files**:

| File | Path |
|------|------|
| PaneEventBus | `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventBus.ts` |
| PaneEventTypes | `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` |
| usePaneEvent (subscribe hook) | `src/client/shared/Spaarke.AI.Widgets/src/events/usePaneEvent.ts` |
| useDispatchPaneEvent (dispatch hook) | `src/client/shared/Spaarke.AI.Widgets/src/events/useDispatchPaneEvent.ts` |
| PaneEventBusContext (React provider) | `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventBusContext.tsx` |

**Design**: A single shared `PaneEventBus` instance is created and provided to the React tree via `PaneEventBusContext`. Components interact through `usePaneEvent` (subscribe) and `useDispatchPaneEvent` (dispatch). Subscribers are stored in per-channel `Set` collections for O(1) add/delete and automatic deduplication.

---

## R2: Three-Pane Lifecycle Stages

The `ThreePaneShell` manages a four-stage lifecycle that controls pane visibility and widget rendering. Stage transitions are driven by PaneEventBus events and direct function calls.

| Stage | Name | Trigger (entry) | Description |
|-------|------|-----------------|-------------|
| Stage 1 | `welcome` | Initial load / `session_reset` | Landing: no session or playbook. ConversationPane shows WelcomePanel; ContextPane shows PlaybookGalleryWidget. |
| Stage 2 | `loading` | `playbook_change` / `first_message` / `playbook-selected` | Playbook selected: gathering context. ConversationPane mounts SprkChat; ContextPane shows entity info or loading spinner. |
| Stage 3 | `active-chat` | `widget_load` (first tab) / `entity_resolved` | Active work: first document/widget loaded. Full three-pane working mode. |
| Stage 4 | `review` | `tab_count_change` with `tabCount >= 2` | Multi-task: two or more workspace tabs open. Review-oriented layout. |

**Transitions**:
```
welcome → loading        conversation/playbook_change OR conversation/first_message
loading → active-chat    workspace/widget_load (first resolved tab)
active-chat → review     workspace/tab_count_change with tabCount >= 2
review → active-chat     workspace/tab_count_change with tabCount === 1
any → welcome            workspace/session_reset (session cleared/deleted)
```

**ShellStageManager** subscribes to PaneEventBus events, maintains a `SessionState` snapshot, and recomputes the stage on each event. Stage is propagated to all child panes via `ShellStageContext`.

**Source**: `src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx`

---

## Related

- [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) -- Four-tier AI framework overview
- [playbook-architecture.md](playbook-architecture.md) -- Playbook system, node types, JPS definitions
