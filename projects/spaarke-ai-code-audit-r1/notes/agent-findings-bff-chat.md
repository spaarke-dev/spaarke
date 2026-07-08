# Agent findings — BFF chat/agent/session (auditor 2/7, 2026-07-05)

Scope: `src/server/api/Sprk.Bff.Api/` chat side — `Services/Ai/Chat/**`, chat endpoints,
`Models/Ai/Chat/**`, session persistence, `Services/Ai/LinearConsumers/**`,
`Services/Ai/PublicContracts/ConsumerRoutingService`, handlers/tools, SSE plumbing.
All paths relative to `src/server/api/Sprk.Bff.Api/` unless noted. Audited against MASTER.

## HEADLINE: NINE distinct dispatch/intent mechanisms (not four)

Several run sequentially inside a single `SendMessageAsync` turn (`Api/Ai/ChatEndpoints.cs`, 3468 lines,
handler lines ~340-973 = the central orchestration funnel with ~5 sequential routing gates):

| # | Mechanism | Path | How triggered | Routes to |
|---|---|---|---|---|
| 1 | `CompoundIntentDetector` — keyword heuristic on proposed tool-call names | `Services/Ai/Chat/CompoundIntentDetector.cs` | after `agent.DetectToolCallsAsync` (~line 564) | plan_preview gate (`PendingPlanManager` + halt) when ≥2 tools OR name in hardcoded `WriteBackToolNames`/`ExternalActionToolNames` |
| 2 | `PlaybookDispatcher` — 2-stage vector (≥0.85 bypass) + LLM refinement | `Services/Ai/Chat/PlaybookDispatcher.cs` | non-attachment turns (~line 716) | `DispatchResult` → `PlaybookOutputHandler` |
| 3 | LLM agent tool loop (function calling, `ChatToolMode.Auto`) | `Services/Ai/Chat/SprkChatAgent.cs` | default streaming path | typed handlers incl. `invoke_playbook`, `recall_session_file` |
| 4 | `SoftSlashRouter` → `intentHint` bias | FRONTEND only (`decorateBody()`); BFF consumes `ChatSendMessageRequest.IntentHint` | slash-ish NL | biases `PlaybookDispatcher.RunPhaseBVectorMatchAsync` query. Closed vocab: summarize/draft/extract-entities/analyze |
| 5 | `AgentServiceRoutingMiddleware` — keyword classifier | `Services/Ai/Chat/Middleware/AgentServiceRoutingMiddleware.cs::ClassifyIntent` | outermost agent middleware (only when `AgentServiceClient` resolvable) | Foundry Agent Service vs direct pipeline |
| 6 | `IntentRerankerService` — LLM reranker (gpt-4o-mini, 800ms budget, FR-46) | `Services/Ai/Chat/IntentRerankerService.cs` | FR-49 attachment options flow | reranked top-3 for `playbook_options` (never auto-executes) |
| 7 | `PlaybookCandidateSelector` — top-N file-aware selection (FR-47/48) | `Services/Ai/Chat/PlaybookCandidateSelector.cs` | attachment branch (~line 633) | candidate list for `playbook_options` SSE |
| 8 | `ConsumerRoutingService` — consumer-key routing table (`sprk_playbookconsumer`) | `Services/Ai/PublicContracts/ConsumerRoutingService.cs` | `SessionSummarizeOrchestrator` resolve | Linear `FileSummarizeService` if action row exists; else playbook engine; else `WorkspaceOptions` fallback |
| 9 | `InvokePlaybookHandler` — LLM-chosen playbook dispatch (tool-mediated sub-mode of #3) | `Services/Ai/Handlers/InvokePlaybookHandler.cs` | LLM names playbook GUID | orchestration triangle, tenant-visibility gated |

**Note**: r7's `TryDetectExplicitConsumerType` regex is NOT in master (grep zero hits) — it's branch-only,
i.e. a 10th mechanism if r7 merges as-is.

## Component inventory (condensed; status all grounded in DI registration/endpoint mapping/tests)

### Endpoints
- `Api/Ai/ChatEndpoints.cs` — Dispatcher+Session+Widget-routing — session CRUD, message SSE, refine, history, context-switch, tabs, plan/approve, action/confirm, commands, playbook-dispatch/execute. **working**. Biggest file (3468 lines).
- `Api/Ai/SummarizeSessionEndpoint.cs` — Consumer entry — `POST /sessions/{id}/summarize` → `SessionSummarizeOrchestrator`. **working** (R5 D2-04).
- `Api/Ai/ChatDocumentEndpoints.cs` — Session — upload (PDF/DOCX/TXT/MD, 50MB), persist-to-SPE; extracted text in tenant Redis (`doc-upload-*`, 4h TTL). **working**.

### Agent core
- `SprkChatAgent` + `SprkChatAgentFactory` (2714 lines; singleton; unsealed for `NullSprkChatAgentFactory` kill-switch) — **working**. Factory also decides per-turn tool set (FR-23) = capability router; FR-24 dedup directive; factories `PlaybookDispatcher`, `DynamicCommandResolver`, `PlaybookOutputHandler`.
- Middleware pipeline (factory-instantiated per ADR-010): ContentSafety → CostControl → Telemetry → AgentServiceRouting. **working** (routing conditional).
- `ToolHandlerToAIFunctionAdapter` — bridges typed `IToolHandler` → `AIFunction`. Load-bearing since R6 W7-10 (FR-11 data-driven tools).

### Tools — typed handlers (live) vs legacy Chat/Tools (superseded)
- LIVE `Services/Ai/Handlers/`: `RecallSessionFileHandler` (T2+T5, citation-gated), `ManagePinnedContextHandler`, `InvokePlaybookHandler`, `DocumentSearchHandler`, `KnowledgeRetrievalHandler`, `AnalysisQueryHandler`, `WorkingDocumentHandler`, `TextRefinementHandler`, `WebSearchHandler`, `VerifyCitationsHandler`, `CodeInterpreterHandler`, `LegalResearchHandler`, workspace-tab handlers (`GetWorkspaceTabContent`, `UpdateWorkspaceTab`, `CloseWorkspaceTab`, `SendWorkspaceArtifact`). Auto-discovered via `ToolFrameworkExtensions.AddToolHandlersFromAssembly`, surfaced via `sprk_analysistool` Dataverse rows (Manifest).
- LEGACY `Services/Ai/Chat/Tools/*` — mostly dead, superseded per `SprkChatAgentFactory.ResolveTools` comments (lines 880-1013). Still live: `AnalysisExecutionTools` (`reanalyze`-gated) and `TextRefinementTools` (only for `RefineTextAsync` SSE path, not an LLM tool).

### Session state (3-tier)
- `Models/Ai/Chat/ChatSession.cs` — `ChatSessionFile` has 6 R5 fields + 8 enriched fields (SummaryText, ClassifiedDocType, Sections, TableMetadata, Citations, PageCount, Language, ClassifiedConfidence) from chat-routing-redesign-r1.
- `ChatSessionManager` — Redis hot (24h sliding) → Cosmos warm (`SessionPersistenceService`, write-through) → Dataverse cold (`sprk_aichatsummary` via `ChatDataverseRepository`). **CAVEAT**: Cosmos mapping DROPS `UploadedFiles`+`DocumentId` (intentional cleanup contract) — Cosmos-restored sessions lose the file manifest.
- `ChatHistoryManager` — summarize@15 / archive@50.
- `PendingPlanManager` (Redis 30-min TTL; Null-Object kill-switch) — compound-intent plan state.
- `SessionFilesCleanupJob/Signal` — IHostedService evicting session-files index docs on session end (NFR-02).
- Context providers: `PlaybookChatContextProvider` / `StandaloneChatContextProvider` / `AnalysisChatContextResolver` — resolve system prompt from playbook Action (ACT-*) records. Session+Manifest.
- `ChatContextMappingService` — `sprk_aichatcontextmap` (entityType+pageType → playbook), Redis-cached. Manifest.
- `DynamicCommandResolver` — slash-command catalog (system+playbook+scope), Redis 5-min TTL. Manifest.

### Consumers
- `SessionSummarizeOrchestrator` (+Null) — chat-summarize consumer, `SUM-CHAT@v1` schema. TWO runtime paths: Linear (`ExecuteLinearAsync` → `FileSummarizeService`) vs playbook engine (`IPlaybookOrchestrationService.ExecuteAsync`), each with own chunk translator. Runtime choice via `ResolveActionAsync`. Mid-migration double implementation.
- Linear consumer stack `Services/Ai/LinearConsumers/`: `FileSummarizeService`, `DocumentProfileService`, `ActionResolver`, `ActionRunner`, `LinearConsumersModule`, `SessionFileTextSource`. Newest path (Jul 2026), consumer-agnostic.

### SSE/output routing
- `ChatSseEventFactory` + typed events (`OutputPane`, `SourcePane`, `SourceHighlight`, `SectionStream`, `PlaybookOptions`, `LinearDispatch`); `R2SseEventEmitter` (6 R2 event types); `SseOutputGuard`. `PlaybookOutputHandler` routes typed playbook output (dialog/navigation/download/insert/workspace/form-prefill/side-effect).

## Duplicates / overlaps
1. **Two chat-summarize execution engines** inside `SessionSummarizeOrchestrator` (Linear vs playbook engine), each with own translator. Mid-migration state.
2. **Two summarize entry points**: `SummarizeSessionEndpoint` + NL agent path (agent-tool leg reserved: `SummarizeInvocationPath.AgentTool` unused).
3. **Vector matching duplicated**: `DispatchAsync` Stage-1 vs `RunPhaseBVectorMatchAsync` per-file — both hit `spaarke-playbook-embeddings`, divergent cache keys.
4. **Three gate-before-write surfaces**: CompoundIntentDetector/PendingPlanManager (`/plan/approve`) vs `/actions/{id}/confirm` HITL — two different pending-action stores.
5. **Legacy Chat/Tools vs typed Handlers** — same capabilities, two class families.
6. **Two agent abstractions**: `ISprkChatAgent` (live) vs `ISprkAgent`/`DirectOpenAiAgent` (registered, never consumed).

## Dead-code suspects
- **`DirectOpenAiAgent` + `ISprkAgent` cluster** — `AiChatModule.cs:61` registers it; nothing injects it. Includes DTOs `AgentRequest.cs`, `ConversationTurn.cs`, `SseEvent.cs`. Comment: "Phase 3 will introduce FoundryAgent…" — never landed. Has a test maintaining dead code (`DirectOpenAiAgentTests.cs`).
- `Services/Ai/Chat/SseEvent.cs` — superseded by `ChatSseEvent` in `Api/Ai`.
- Legacy `Chat/Tools/` classes (except the two live exceptions above).
- `SummarizeInvocationPath.AgentTool` — no caller.
- `CompoundIntentDetector` lines 97-98 — dead-then-overwritten `toolName` assignment (latent smell).
- `PlaybookDispatcher.RunPhaseBManifestPresentAsync` — self-documented forward-compat scaffolding; unreachable in prod (needs `ClassifiedDocType` which the deferred classifier never sets).

## Test coverage
Strong: dispatcher ×6, factory ×5, agent, orchestrator (+PathA5 integration), session managers, continuity,
reranker, candidate selector, options builder, output handler ×4, SSE factory, context providers, middleware.
**Gaps**: `CompoundIntentDetector` has NO dedicated test; `DirectOpenAiAgentTests` maintains dead code.
