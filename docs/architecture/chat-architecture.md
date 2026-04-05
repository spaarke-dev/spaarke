# Chat Architecture

> **Last Updated**: 2026-04-05
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: New
> **Purpose**: Describes the SprkChat conversational AI subsystem — session management, playbook dispatch, compound intent detection, and streaming response pipeline.

---

## Overview

SprkChat is the embedded conversational AI feature within the Spaarke platform. It provides playbook-driven, entity-scoped chat sessions with tool execution, compound intent gating, and five typed output channels (text, dialog, navigation, download, insert). The system uses a dual-client architecture: a function-invocation client for tool execution and a raw client for intent detection, connected through a middleware pipeline (telemetry, cost control, content safety).

The key architectural decision is the **Agent Framework pattern** — each chat session gets a transient `SprkChatAgent` instance created by a factory, with system prompts sourced from playbook Action records in Dataverse rather than hardcoded.

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| SprkChatAgent | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs` | Core agent: streaming responses via IChatClient, tool registration, compound intent detection |
| SprkChatAgentFactory | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` | Constructs agents with context, tools, middleware pipeline; manages context switching |
| ISprkChatAgent | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ISprkChatAgent.cs` | Agent interface enabling middleware decorator pattern |
| ChatSessionManager | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatSessionManager.cs` | Session lifecycle (create/get/delete); Redis hot cache with 24h TTL, Dataverse cold storage |
| ChatHistoryManager | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatHistoryManager.cs` | Message persistence, summarisation at 15 messages, archive at 50 messages |
| CompoundIntentDetector | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/CompoundIntentDetector.cs` | Detects multi-tool or write-back intents; builds PendingPlan for user approval |
| PendingPlanManager | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PendingPlanManager.cs` | Redis storage for pending plans (30-min TTL); atomic get+delete for approval |
| PlaybookDispatcher | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs` | Two-stage playbook matching: vector similarity + LLM refinement (2s budget) |
| PlaybookOutputHandler | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookOutputHandler.cs` | Routes dispatch results to 5 output types: text, dialog, navigation, download, insert |
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
3. **SprkChatAgentFactory** creates/retrieves the agent with playbook context and tool registration
4. **CompoundIntentDetector** runs via `DetectToolCallsAsync` using the raw client (no function invocation) to inspect tool call intentions
5. **Decision**: If compound intent (2+ tools, write-back tool, or external action) -> build PendingPlan, store in Redis, emit `plan_preview` SSE event, halt. If not -> proceed to step 6
6. **SprkChatAgent.SendMessageAsync** streams response via the function-invocation client, executing tools automatically
7. **Middleware pipeline** (telemetry -> cost control -> content safety) wraps the streaming response
8. **ChatHistoryManager** persists the message to Dataverse and updates Redis cache
9. **Summarisation** triggers at 15 messages; **archive** triggers at 50 messages

### Playbook Dispatch Flow

1. **PlaybookDispatcher.DispatchAsync** receives user message and host context
2. **Stage 1** (1.5s budget): Vector similarity search via PlaybookEmbeddingService against `playbook-embeddings` AI Search index, pre-filtered by entityType
3. **Decision**: If single candidate with score >= 0.85 -> skip Stage 2, build result directly
4. **Stage 2** (0.5s budget): LLM refinement selects best match from candidates and extracts parameters
5. **PlaybookOutputHandler.HandleOutputAsync** routes to the appropriate output type based on JPS DeliverOutput node

### Context Mapping Resolution

1. **ChatContextMappingService** resolves playbook(s) using four-tier precedence: exact match (entityType + pageType) -> entity + any -> wildcard + pageType -> global fallback
2. Results cached in Redis with 30-minute sliding TTL

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | Playbook System | `IPlaybookService`, `IScopeResolverService` | Loads Action records, scope definitions |
| Depends on | RAG / AI Search | `PlaybookEmbeddingService` | Vector search for playbook dispatch |
| Depends on | Azure OpenAI | `IChatClient` (two instances) | Function-invocation and raw clients |
| Depends on | Redis | `IDistributedCache` | Sessions, plans, context mappings, command catalogs |
| Depends on | Dataverse | `IChatDataverseRepository`, `IGenericEntityService` | Cold storage, entity queries |
| Consumed by | ChatEndpoints | SSE streaming | `/api/ai/chat/sessions/{id}/messages` |
| Consumed by | AiToolAgent PCF | WebSocket/SSE | Embedded chat UI component |

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Dual IChatClient instances | Function-invocation + raw | Enables compound intent detection without executing tools | ADR-013 |
| Redis-first session storage | 24h sliding TTL, Dataverse fallback | Low-latency reads; Dataverse for audit trail | ADR-009, ADR-014 |
| Factory-instantiated components | PlaybookDispatcher, CompoundIntentDetector not DI-registered | Keeps DI registration count within budget | ADR-010 |
| Middleware pipeline via decorator | ISprkChatAgent chain | Telemetry, cost control, content safety without modifying core agent | ADR-013 |
| Tenant-scoped cache keys | `chat:session:{tenantId}:{sessionId}` | Multi-tenant isolation | ADR-014 |
| Plan preview for write-backs | Compound intent gating with user approval | No write-back executes without user Proceed confirmation (FR-11) | Spec |

## Constraints

- **MUST**: System prompts originate from playbook Action (ACT-*) records, not hardcoded
- **MUST**: All write-back tools require plan_preview gating before execution
- **MUST**: Cache keys include tenantId for multi-tenant isolation (ADR-014)
- **MUST**: Content safety middleware never logs matched content, only pattern type
- **MUST NOT**: Exceed 10,000 tokens per session without explicit budget override
- **MUST NOT**: Store PendingPlan inside ChatSession (avoids inflating every session read)

## Known Pitfalls

- **Dataverse entity deployment**: ChatSessionManager and ChatHistoryManager handle `InvalidOperationException` gracefully when chat entities (sprk_aichatsummary, sprk_aichatmessage) are not yet deployed. Redis continues to function as the primary store.
- **Stage 2 timeout fallback**: When PlaybookDispatcher LLM refinement times out (500ms budget), it falls back to the top Stage 1 vector search candidate rather than failing.
- **PendingPlan race condition**: The two-step get+delete in PendingPlanManager is not truly atomic via IDistributedCache. This is acceptable because plan approval is a low-frequency user action and planId validation provides an additional idempotency check.
- **Summarisation placeholder**: Phase 1 summarisation generates a placeholder summary. Real LLM-based summarisation is deferred to AIPL-054.

## Related

- [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) -- Four-tier AI framework overview
- [playbook-architecture.md](playbook-architecture.md) -- Playbook system, node types, JPS definitions
