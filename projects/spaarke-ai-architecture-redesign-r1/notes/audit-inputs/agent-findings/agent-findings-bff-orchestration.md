# Agent findings — BFF playbook/orchestration (auditor 3/7, 2026-07-05)

Scope: `src/server/api/Sprk.Bff.Api/Services/Ai/` (excl. Chat/), `Api/Ai/` playbook+analysis endpoints,
`Models/Ai/`, DI modules. Liveness grounded against `AnalysisServicesModule.cs`, `AiModule.cs`,
`RoutingModule.cs`, `FinanceModule.cs`, `Program.cs`, `EndpointMappingExtensions.cs`. Audited against MASTER.

## Master gating fact
Nearly the whole playbook/analysis subsystem sits behind compound gate `Analysis:Enabled && DocumentIntelligence:Enabled`
(`AnalysisServicesModule.cs:131`); OFF swaps P3 Fail-Fast Null-Objects (line 334). **Unconditional exceptions**:
LinearConsumers (`Program.cs:135`), Narrators/Collector real registrations, and the FinanceModule-registered strays (below).

## 1. Orchestration core
| Component | Category | Status / notes |
|---|---|---|
| `PlaybookOrchestrationService` | Dispatcher+Consumer | **Working** — canonical node-graph engine (ExecutionGraph, INodeExecutorRegistry, parallel batches ADR-016, SSE PlaybookStreamEvent). Legacy mode delegates to AnalysisOrchestrationService. Consumers: AnalysisEndpoints, PlaybookRunEndpoints, WorkspacePrefillAi, InvokePlaybookAi. Tested. |
| `AnalysisOrchestrationService` | Consumer | **Working** — LEGACY single-doc analysis (continue/save/export/resume). Partially superseded; transitional dual path. |
| `PlaybookExecutionEngine` | Consumer+Dispatcher | **Working but narrow** — "unified" batch (delegates back to PlaybookOrchestrationService!) + conversational + chat-session summarize. Callers: InsightsOrchestrator, PlaybookInvocationService (Agent gateway), SessionSummarizeOrchestrator. **Biggest consolidation target** — thin wrapper with overlapping execute-a-playbook semantics. |
| `AiPlaybookBuilderService` | Consumer | Working — AI-assisted playbook AUTHORING (maker/builder agent), not runtime. |
| `ExecutionGraph` | Infra | Working — topological node graph + batching. |

## 2. Node executors (`Services/Ai/Nodes/`) — ALL live, `AddSingleton<INodeExecutor,…>` in `AddNodeExecutors` (module lines 911-1018)
Start(33), AiCompletion(1, R7 FR-12), AiAnalysis, Condition, CreateTask, CreateNotification, SendEmail,
UpdateRecord, QueryDataverse, DeliverOutput (ADR-037), DeliverComposite(42, FR-52), DeliverToIndex,
LookupUserMembership(52, R3), EntityNameValidator(141, R4 hallucination scrubber), **LoadKnowledge(142 —
partial/scaffolding: R4 pass-through placeholder for R5 knowledge binding that never landed)**,
ReturnResponse(143), AgentService(60, kill-switch `AgentService:Enabled`), GroundingVerify(70),
LiveFact(80), IndexRetrieve(90), EvidenceSufficiency(100), DeclineToFind(110), ReturnInsightArtifact(120)
+ Insights-owned ObservationEmitter/Sanitizer. Registry: `NodeExecutorRegistry` (indexes by ExecutorType).
Categories: write-shape executors (CreateTask/SendEmail/UpdateRecord/QueryDataverse/CreateNotification) = **Tool**;
Deliver* / ReturnResponse = **Widget/Output-routing**; AiCompletion/AiAnalysis/AgentService = **Consumer**.

## 3. Manifest layer (playbook config, scope, model, prompt-schema)
| Component | Status / notes |
|---|---|
| `PlaybookService` (+Null) | Working — CRUD `sprk_analysisplaybook` + N:N scopes; by-name cache. |
| `PlaybookLookupService` | Working — cached alt-key lookup. **Registered in FinanceModule.cs:115 (placement smell)** — escapes compound AI gate, no Null peer, yet consumed by Workspace pre-fill / AppOnly / PlaybookEndpoints. |
| `NodeService` | Working — CRUD `sprk_playbooknode`. |
| `ScopeResolverService` / `ScopeManagementService` | Working — resolve/manage actions/skills/knowledge/tools/personas. |
| `AnalysisActionService` / `AnalysisSkillService` / `AnalysisKnowledgeService` / `AnalysisToolService` / `AnalysisPersonaService` | Working — per-scope CRUD (`sprk_analysisaction`, `sprk_aipersona`, etc.). |
| `ModelSelector` | Working — OperationType→model deployment. |
| `FallbackScopeCatalog` / `FallbackPrompts` | Partial — hardcoded static fallbacks when Dataverse catalog unavailable; removal candidates. |
| `PromptSchemaRenderer` | Working — pure JPS→RenderedPrompt renderer, SHARED by ActionRunner (Linear) + AiCompletionNodeExecutor (node). Tested. |
| `PromptSchemaOverrideMerger` | Working — pure static base+override merge. Tested. |
| `JpsRefResolver` / `LookupChoicesResolver` / `PlaybookTemplateContextBuilder` / `EntityResolutionService` | Working support. |
| `ScopeGapDetector` | **Dead-code suspect** — no DI registration found. |

## 4. Output composition / delivery (ADR-037)
- `OutputOrchestratorService` — applies playbook `outputMapping` → Dataverse updates. **Registered in FinanceModule.cs:104 (placement smell)**; only consumer = InvoiceExtractionJobHandler.
- `TemplateEngine`, `WordTemplateService`/`EmailTemplateService` (Delivery/), Export/* (Docx/Pdf/Email/Registry) — all working.

## 5. Narrators / Daily Briefing (server)
- `DailyBriefingNarrator` — code-defined /narrate workflow (R7 W11 spike; TLDR + channel narratives + entity scrub). Registered unconditionally; **request-time flag `Features:NarrateUseCodeBasedNarrator` default FALSE** → prod default is still playbook engine. Dual-path.
- `DailyBriefingCollector` — live 6-entity Dataverse query → briefing payload (R7 W12 T131). Working.
- `EntityNameScrubber` — pure algorithm. Null peers for both narrator+collector.
- `DailyBriefingEndpoints`: `/summarize` → IBriefingAi; `/narrate` → IInvokePlaybookAi OR narrator (flag); `/render` → Collector+Narrator. Mapped unconditionally.

## 6. LinearConsumers (`Services/Ai/LinearConsumers/`) — registered UNCONDITIONALLY (Program.cs:135)
`ActionResolver` (Manifest+Dispatcher: consumerType→Action GUID via ConsumerRoutingService), `ActionRunner`
(wraps GetStructuredCompletionRawAsync + PromptSchemaRenderer), `DocumentProfileService` (**live replacement**
for playbook-engine Document Profile — AnalysisEndpoints.cs:318,332 branches to it), `FileSummarizeService`
(generic text→structured summary; serves file-summary + chat-summarize), `DocumentTextSource`,
`SessionFileTextSource`, `LinearConsumersOptions` (**ActionIds map retired W12.3; PlaybookIds/ModelDeployments/
MaxOutputTokens residue remains**). All R7 Wave 12.x — the newest, target-architecture-shaped path.

## 7. PublicContracts facade (ADR-013 boundary) + routing
- `ConsumerRoutingService` — canonical routing surface (`sprk_playbookconsumer`, FR-1R-03, 5-min cache). RoutingModule.cs:63. Tested.
- `RoutingConsumerTypeHealthCheck` — startup validation hosted-service.
- Facades (all Working, Null peers): `InvokePlaybookAi` (canonical invocation, R6 P3), `WorkspacePrefillAi`,
  `BriefingAi`, `InvoiceAi`, `RecordMatchingAi`, `InsightsAi`→`InsightsOrchestrator`.
- `InvokePlaybookHandler` — chat-tool → IInvokePlaybookAi (replaced specialized bridges).

## 8. Endpoints (Api/Ai/, all mapped in EndpointMappingExtensions)
AnalysisEndpoints (execute branches: DocumentProfile→Linear, else→engine), PlaybookEndpoints (CRUD/share/canvas),
PlaybookRunEndpoints (validate/execute/status/stream/cancel/history), NodeEndpoints, ScopeEndpoints,
ModelEndpoints, HandlerEndpoints, PlaybookEmbeddingEndpoints, DailyBriefingEndpoints, SummarizeSessionEndpoint,
FeedbackEndpoints, VisualizationEndpoints, RecordSearch/RecordMatch (gated), PromptLibraryEndpoints. All working+tested.

## Duplicates / overlaps (7)
1. **Two orchestration engines**: PlaybookOrchestrationService vs PlaybookExecutionEngine (thin wrapper, delegates batch back). Biggest consolidation target.
2. **Legacy vs node-based analysis**: AnalysisOrchestrationService vs PlaybookOrchestrationService Legacy-mode delegation.
3. **Document Profile dual path**: LinearConsumer vs engine, branched in AnalysisEndpoints (Wave 12 migration state).
4. **Summarize duplication**: FileSummarizeService (Linear) vs SessionSummarizeOrchestrator (Chat, via engine) — overlapping capability across subtrees.
5. **LLM-call wrapping duplicated 4×**: ActionRunner / AiCompletionNodeExecutor / BriefingAi / InvokePlaybookAi (rendering unified via PromptSchemaRenderer, invocation not).
6. **Narrate dual-path**: engine vs code narrator, flag-toggled.
7. **Routing residue**: ConsumerRoutingService canonical, but LinearConsumersOptions config maps still reverse-lookup in AnalysisEndpoints.

## Dead-code suspects / smells
- `ScopeGapDetector` — no DI registration.
- `FallbackScopeCatalog`/`FallbackPrompts` — static fallback removal candidates.
- `LoadKnowledgeNodeExecutor` (142) — R4 placeholder scaffolding, still registered.
- `AiCompletionNodeExecutor` — verify a live playbook actually references ExecutorType 1 (claimed R4 gate closer; /narrate default doesn't use it).
- **Placement smells**: `PlaybookLookupService` + `OutputOrchestratorService` in FinanceModule; LinearConsumers in Program.cs — all escape the compound AI gate, no Null peers → resolve even when Analysis:Enabled=false. Mis-gating risk.
- `LinearConsumersOptions.ActionIds` residue; removed-but-referenced-in-comments bridges (`InvokeInsightsQueryTool`, `InvokeSummarizePlaybookTool`).
- Lifetime asymmetry: NullPlaybookService AddSingleton vs real typed-HttpClient (intentional D-09).

## Kill-switch inventory
Null-Object peers exist for: IPlaybookOrchestrationService, IPlaybookService, IRagService, IBriefingAi, IInvoiceAi,
IWorkspacePrefillAi, IRecordMatchingAi, IInvokePlaybookAi, IInsightsAi, IVisualizationService, IFileIndexingService,
SprkChatAgentFactory, PendingPlanManager, SessionSummarizeOrchestrator, DailyBriefingNarrator, DailyBriefingCollector,
IInsightsIntentClassifier. Fine-grained flags: AgentService:Enabled, CodeInterpreter:Enabled,
Insights:IntentClassifier:Enabled, Features:NarrateUseCodeBasedNarrator (default false),
DocumentIntelligence:RecordMatchingEnabled, ToolFramework:Enabled, AI-Search-keys sub-gate.
