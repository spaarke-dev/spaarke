# Chat Architecture

> **Last Updated**: 2026-07-07
> **Last Reviewed**: 2026-07-07
> **Reviewed By**: spaarke-ai-architecture-redesign-r1 task 052 (verified against code; added P3 + G-P3 hardening mechanisms — tasks 037/042 + the three G-P3 UAT fix waves). Prior review: tasks 034/035/036 (FR-P2-05/06/07 hard cutover, 2026-07-06)
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
| PendingPlanManager | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PendingPlanManager.cs` | THE unified confirmation gate (D12/FR-P2-02): Redis store for suspended invocations (30-min TTL) + SessionGate ledger markers (ADR-040). Gate-status vocabulary: `pending`/`confirmed`/`rejected`/`expired`/`superseded` + `confirmed-unexecutable` (G-P2) + `dispatch-failed` (G-P3 R2-A/R2-C) |
| SessionDispatchOrchestrator | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionDispatchOrchestrator.cs` | THE dispatch seam (ADR-039): resolves a Binding BY ID, executes its prompted Action via ActionRunner, ledger-writes output BEFORE the terminal chunk (ADR-040); shared by chip clicks, `BindingCapabilityTool`, and gate confirm-resume |
| BindingCapabilityTool | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/BindingCapabilityTool.cs` | Projects one catalog Binding into the loop as a `capability_{consumerType}` tool; validates declared required args BEFORE dispatch (elicitation suspend on missing) |
| SideEffectGateAIFunction | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SideEffectGateAIFunction.cs` | Loop-boundary gate wrap on typed-handler tools whose `sprk_analysistool` row declares `sprk_sideeffectclass` write/communicate — suspends into the unified gate instead of executing; fail-closed (task 037/FR-P2-08) |
| TypedHandlerResumeExecutor | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/TypedHandlerResumeExecutor.cs` | Confirm-RESUME seam for suspended typed-handler invocations: resolves the tool row + handler and executes under the confirming user's OBO, ledger-writing `loop@t{n}` SessionOutput + ToolChain before render (task 042/FR-P3-03) |
| OpenAiFunctionSchemaValidator | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/OpenAiFunctionSchemaValidator.cs` | Projection-time validation of catalog input schemas against the OpenAI function-parameters subset — an invalid schema excludes ONLY its own tool (G-P3 H1) |
| BindingInputSchemaValidator + ElicitationTurnRouter | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/BindingInputSchemaValidator.cs`, `ElicitationTurnRouter.cs` | FR-P2-03 loop-native elicitation: missing declared-required args suspend into an elicitation gate; router builds the clarify instruction or the `elicitation_modal` escape (`capture_mode: modal`) |
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

Suspended invocations resume through `POST /api/ai/chat/sessions/{sessionId}/gates/{gateId}/resolve` — the single gate-resolution surface (semantics in "The confirmation gate end-to-end" below).

### Context Mapping Resolution

1. **ChatContextMappingService** resolves playbook(s) using four-tier precedence: exact match (entityType + pageType) -> entity + any -> wildcard + pageType -> global fallback
2. Results cached in Redis with 30-minute sliding TTL

## The confirmation gate end-to-end (P3 + G-P3 hardening, 2026-07-07)

The unified gate described above was hardened by task 037 (FR-P2-08), task 042 (FR-P3-03), and the three G-P3 UAT fix waves (`projects/spaarke-ai-architecture-redesign-r1/notes/g-p3-uat-round{1,2,3}-findings.md`). The full walk:

### Suspend (loop tool-invocation boundary)

**`SideEffectGateAIFunction`** (task 037) wraps every loop-projected typed-handler tool whose `sprk_analysistool` row DECLARES `sprk_sideeffectclass` = `write` or `communicate` (e.g. `dataverse.create_record`, `email.draft`). The gate decision keys exclusively on the declared class — never tool-name lists (ADR-039); wrap-site selection in `SprkChatAgentFactory` applies `PendingPlanManager.RequiresConfirmation` over catalog metadata only. On invocation the wrapper suspends into the unified store — pending `SessionGate` ledger marker FIRST (ADR-040), resumable args payload in the Tier-3 Redis store only (never logged, NFR-07), then the `action_confirmation` SSE event renders the client dialog. **Fail-closed** (NFR-03): if the store is unavailable or suspension fails, the inner tool does NOT execute — the model gets an honest "cannot execute" instruction. The wrapper preserves the inner tool's name/description/schema verbatim (NFR-04 projection stability).

### Resolve — `POST /api/ai/chat/sessions/{sessionId}/gates/{gateId}/resolve`

Handler: `ChatEndpoints.ResolveGateAsync`. Reject closes the gate with a `rejected` marker. Confirm has two legs:

- **Binding-backed invocations** (elicitation gates, capability confirmations) resume through THE dispatch seam — `SessionDispatchOrchestrator.DispatchAsync` by Binding id.
- **Typed-handler invocations** (suspended by `SideEffectGateAIFunction`; no Binding id) execute through **`TypedHandlerResumeExecutor`** (task 042): the suspended `ToolId` resolves back to its `sprk_analysistool` row + registered handler (catalog declarations only — no allow-lists), and runs the SAME `ValidateChat` → `ExecuteChatAsync` handler contract the loop would have used, **under the confirming user's OBO scope** (the handler resolves from the gate-resolve request's DI scope — no app-only path is reachable). On success the seam ledger-writes an addressable `loop@t{n}` `SessionOutput` + a `SessionToolChain` audit entry BEFORE the result renders (ADR-040). Invocations with no resolvable target (row not chat-available, no handler, compound AI off) close honestly as `confirmed-unexecutable` with 422 `gate.no-binding-target`.

Concurrency: resume is get-then-delete; a double-confirm race yields 409 `gate.not-pending`.

### Failure semantics — 422 `gate.dispatch-failed` (NOT 502)

Handler-reported failures on a confirmed execution (write-mapper validation rejections, Dataverse 400s) are request-content problems, so the endpoint returns **422** ProblemDetails with the stable errorCode **`gate.dispatch-failed`** and the handler's instructive detail (G-P3 round-3 R3-2; the previous 502 falsely signaled a gateway fault). 5xx is reserved for genuinely unexpected exceptions. A `dispatch-failed` gate marker is appended AFTER the `confirmed` marker (append-only, correlated by gate id) so the ledger records the user's approval AND the execution failure.

### Outcome persistence — the model must see the truth

On BOTH outcomes the resolution is persisted as an **assistant transcript message** (G-P3 round-2 R2-A/R2-C): success → `✅ Confirmed action '{name}' executed. …` (+ ledger key when present); failure → `❌ Confirmed action '{name}' FAILED: … No record was created or modified by this confirmation.` This puts the real outcome into the next turn's conversation history (and survives page reload) — before this fix the model oscillated between "created" and "not found" because no gate outcome ever entered `session.Messages`.

## Loop hardening (G-P3 fix waves, 2026-07-07)

### Catalog schema validation at projection — `OpenAiFunctionSchemaValidator`

Azure OpenAI validates every known JSON-Schema keyword in every projected tool's `function.parameters` and rejects the ENTIRE request (`invalid_function_parameters`) if any one tool's schema is malformed — one bad catalog row (property-level `"required": true` on `CREATE-TASK@v1`) 400-failed every text-path turn on the tenant (G-P3 round-1 H1). Now: `OpenAiFunctionSchemaValidator` (pragmatic OpenAI function-parameters subset walk) runs at tool projection on BOTH catalog legs (Binding projection in `SprkChatAgentFactory`; `sprk_analysistool` leg in `ToolHandlerToAIFunctionAdapter`, after its Draft 2020-12 meta-schema check). An invalid schema excludes ONLY its own tool (Error log `[invalid-tool-schema]`, identifiers + keyword-path only), emits `ai.tool.schema_invalid` telemetry (`Telemetry/AiTelemetry.cs`), and surfaces via `RoutingConsumerTypeHealthCheck` as **Degraded** (never Unhealthy) naming the offending row. Authoring rule + CI-validated mirrors: `infra/dataverse/inputschemas/` + `CatalogInputSchemaContractTests`.

### Action honesty — `SideEffectHonestyDirective`

A deterministic "## Action Honesty" directive (`SprkChatAgentFactory.SideEffectHonestyDirective`) is appended to the system prompt of every tool-bearing session (G-P3 rounds 1–3, findings H6/R2-B/R2-D/R3-1/R3-2). Pins: never claim a record/task/email/tab was created, saved, sent, or opened unless a TOOL RESULT confirms it; `capability_*` tools only GENERATE drafts — creating still requires the separate write tool; ask for chat confirmation AT MOST ONCE, then IMMEDIATELY invoke the write tool (its confirmation dialog IS the approval step — never re-draft, never re-ask); resolve lookup references to record GUIDs BEFORE proposing a write; a SUSPENDED tool means the action has NOT happened. `BindingCapabilityTool`'s result text reinforces the same split ("finished GENERATING… did NOT create, save, send…").

### Elicitation (FR-P2-03)

Before dispatching a `capability_*` invocation, `BindingCapabilityTool` validates the model's arguments against the Binding's DECLARED input schema (`BindingInputSchemaValidator.FindMissingRequired`). Missing required args suspend into an elicitation gate (kind `elicitation`; marker first per ADR-040) and yield either a grounded clarifying-turn instruction (`ElicitationTurnRouter.BuildClarifyInstruction`) or — when the Binding declares `capture_mode: modal` and a chat SSE surface exists — an `elicitation_modal` SSE event routing the user to a wizard (`BuildModalNotice`). A successful later dispatch of the same Binding resolves the pending elicitation gate at the ONE dispatch seam (`PendingPlanManager.ResolveElicitationOnDispatchAsync`). Partial answers reuse the same gate id (one logical invocation across turns); elicitation-triggering calls count within the per-turn tool budget (NFR-09).

### `workspace_open_tab` context_event frame (G-P3 round-2 R2-D)

When the loop invokes the `send_workspace_artifact` tool with `widgetType: "Workspace"` (a named workspace layout, e.g. "Compose"), `SendWorkspaceArtifactHandler` emits a **`workspace_open_tab`** frame on the existing `context_event` SSE channel (fields on `ContextSseEventDto` / SprkChat `types.ts`: widget registry key, tab title, server-generated tab correlation id, serialized `widgetData`). Client side, `useContextEventBridge` (SpaarkeAi ConversationPane) republishes it on the `workspace` PaneEventBus channel and the workspace pane opens a real layout tab — closing the fabrication gap where the model claimed "opened in a workspace tab" with no mechanism behind it.

### Stable turn-failure error — `[chat.turn-failed]`

The SendMessage catch-all no longer interpolates raw exception text into the SSE error event (G-P3 round-1 H3: an upstream `ClientResultException` with `tools[28]` internals rendered verbatim in the user's transcript). One construction site (`ChatEndpoints.BuildTurnFailedErrorEvent()`, errorCode const `chat.turn-failed`) emits the stable message `[chat.turn-failed] The assistant hit a problem completing this turn. Please try again.` — exception detail stays in the server-side `LogError` (ADR-019).

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
