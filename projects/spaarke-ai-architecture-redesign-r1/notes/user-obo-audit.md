# User-OBO Audit — AI-Reachable Dataverse Flows (Task 012 / FR-P0-10)

> **Date**: 2026-07-05 · **Task**: 012 (STANDARD rigor, read-only) · **Gate material for**: task 014 (G-P0)
> **Scope**: every path from AI code (`Services/Ai/Handlers/**`, `Services/Ai/Chat/Tools/**`, `Services/Ai/LinearConsumers/**`, AI services on the request path) to a Dataverse client, classified user-OBO / user-delegated / app-only / no-Dataverse, with file:line evidence.
> All paths relative to `src/server/api/Sprk.Bff.Api/` unless prefixed. NFR-07: identifiers and code locations only — no tokens, no record content.

---

## 1. Methodology

1. **Enumerate** — listed all 30 handler classes in `Services/Ai/Handlers/`, all 11 legacy chat-tool classes in `Services/Ai/Chat/Tools/`, the LinearConsumers executor path, and grepped `Services/Ai/**` for every Dataverse client type (`IDataverseUserClient`, `IDataverseService`, `IGenericEntityService`, `IDocumentDataverseService`, `IAnalysisDataverseService`, `IFieldMappingDataverseService`, `DataverseWebApiService`, `DataverseServiceClientImpl`, `TokenCredential`, `IOrganizationService`) — 26 matching files, all traced.
2. **Resolve identity mode at the client** — established the auth mode of each client implementation once (§2), then classified flows by which client their call graph terminates in.
3. **Trace reachability** — for each app-only client usage, determined whether it is reachable from an LLM-initiated tool call (tool plane), from the AI request path (infrastructure), or only from background jobs (legitimately app-only per ADR-028).
4. **Classify + escalate** — PASS (user-OBO / user-delegated), FINDING (app-only reachable from AI), or ACCEPTED (background / config-plane, explicitly noted).

## 2. Identity-mode reference — the Dataverse clients

| Client | Auth mode | Evidence |
|---|---|---|
| `IDataverseUserClient` → `DataverseUserClient` | **User-OBO only, fail-closed.** MSAL `AcquireTokenOnBehalfOf` with the caller's bearer assertion for `{env}/.default`; no user context or no OBO config ⇒ error result, never a fallback credential | `Services/Ai/Handlers/Dataverse/DataverseUserClient.cs:178-183` (OBO exchange), `:100-106` (fail-closed, no MI/client-credentials fallback), `:156-172` (no HttpContext / no bearer ⇒ `UserContextRequired`) |
| `IDataverseService` → `DataverseServiceClientImpl` | **App-only.** `AuthType=ClientSecret` ServiceClient connection string | `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs:59-64`; registered `Infrastructure/DI/GraphModule.cs:46-51` |
| `IGenericEntityService`, `IDocumentDataverseService`, `IAnalysisDataverseService` | **App-only** (forwarding registrations to `IDataverseService`) | `Infrastructure/DI/GraphModule.cs:68-70` |
| `IFieldMappingDataverseService` → `DataverseWebApiService` | **App-only.** `ClientSecretCredential` | `src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs:56`; registered `GraphModule.cs:78` |
| `PlaybookService`, `ScopeResolverService`, `AnalysisToolService`, `AnalysisActionService` (direct Web API) | **App-only.** Injected `TokenCredential` | `Services/Ai/PlaybookService.cs:20`, `Services/Ai/ScopeResolverService.cs:27`, `Services/Ai/AnalysisToolService.cs:18`, `Services/Ai/AnalysisActionService.cs:16` |
| `DataverseQueryTools` (self-contained) | **User-delegated pass-through.** Forwards the caller's inbound bearer token as-is; returns an error when `HttpContext` is null — no app-only fallback | `Services/Ai/Chat/Tools/DataverseQueryTools.cs:216-218`, `:190-194` |

## 3. Per-flow verdict table

### 3A. The six `dataverse.*` handlers (tasks 008/009 — audit item 1)

All six inject **only** `IDataverseUserClient` + `ILogger` (no second Dataverse client, no fallback path in any handler). The client is registered symmetrically with the handler assembly scan (`Services/Ai/ToolFrameworkExtensions.cs:43` and `:75`) as user-OBO only.

| Flow (tool) | Entry point | Identity mode | Evidence (ctor → acquisition) | Verdict |
|---|---|---|---|---|
| `dataverse.describe` | `Handlers/DataverseDescribeHandler.cs` | user-OBO | ctor `:56-58` → `DataverseUserClient.cs:178-183` | **PASS** |
| `dataverse.read_query` | `Handlers/DataverseReadQueryHandler.cs` | user-OBO | ctor `:44-46` → same | **PASS** |
| `dataverse.search_data` | `Handlers/DataverseSearchDataHandler.cs` | user-OBO | ctor `:51-53` → same | **PASS** |
| `dataverse.create_record` | `Handlers/DataverseCreateRecordHandler.cs` | user-OBO | ctor `:51-53` → same | **PASS** |
| `dataverse.update_record` | `Handlers/DataverseUpdateRecordHandler.cs` | user-OBO | ctor `:51-53` → same (`If-Match: *` update-only, `DataverseUserClient.cs:207-211`) | **PASS** |
| `dataverse.delete_record` | `Handlers/DataverseDeleteRecordHandler.cs` | user-OBO | ctor `:49-51` → same | **PASS** |

Helpers in `Handlers/Dataverse/` (`DataverseSqlQueryTranslator`, `DataverseWriteItemMapper`, `DataverseRecordCitations`, `DataverseToolNames`) hold no Dataverse client — pure translation/mapping. **No fallback path exists**: `DataverseUserClient` contains no `TokenCredential`, `DefaultAzureCredential`, or client-credentials flow (verified by read of the full file), and missing config/user context fails closed (`:100-106`, `:142-172`).

### 3B. All other tool handlers in `Services/Ai/Handlers/**` (audit item 2)

| Flow (handler) | Dataverse leg | Identity mode | Evidence | Verdict |
|---|---|---|---|---|
| `AnalysisQueryHandler` | `IAnalysisOrchestrationService.GetAnalysisAsync` → `AnalysisResultPersistence`/`AnalysisDocumentLoader` → `IAnalysisDataverseService`+`IDocumentDataverseService` | **app-only** | `Handlers/AnalysisQueryHandler.cs:303`; `Services/Ai/AnalysisResultPersistence.cs:19-20`; `Services/Ai/AnalysisDocumentLoader.cs:14-15`; `GraphModule.cs:68-70` | **FINDING F-2** |
| `InvokePlaybookHandler` | `IPlaybookService` (config read) + lazy `IInvokePlaybookAi` → `IPlaybookOrchestrationService` → `IAnalysisOrchestrationService` → node executors | **app-only** (engine + config) | `Handlers/InvokePlaybookHandler.cs:113-116, 134-135`; node evidence under F-1 | **FINDING F-1** |
| `WorkingDocumentHandler` | `IWorkingDocumentService` → `IGenericEntityService` | **app-only** (writes) | `Handlers/WorkingDocumentHandler.cs:135`; `Services/Ai/WorkingDocumentService.cs:17` | **FINDING F-2** |
| `RecallSessionFileHandler` | `ChatSessionManager` → `IChatDataverseRepository` → `IGenericEntityService` | **app-only** (own-session-keyed reads) | `Handlers/RecallSessionFileHandler.cs:213`; `Chat/ChatSessionManager.cs:42`; `Chat/ChatDataverseRepository.cs:23,28` | **FINDING F-2 (low)** |
| `GenericAnalysisHandler` | `IScopeResolverService` (scope/config resolution) | **app-only** (config-plane) | `Handlers/GenericAnalysisHandler.cs:36`; `ScopeResolverService.cs:27` | **F-3 (config-plane)** |
| `DocumentSearchHandler`, `KnowledgeRetrievalHandler` | none — `IRagService` (Azure AI Search) | no-Dataverse | `Handlers/DocumentSearchHandler.cs:103`; `Handlers/KnowledgeRetrievalHandler.cs:87` | PASS (n/a) |
| `ClauseAnalyzerHandler`, `EntityExtractorHandler`, `RiskDetectorHandler`, `InvoiceExtractionToolHandler` | none — `IOpenAiClient` + `ITenantCache` | no-Dataverse | ctor fields, e.g. `Handlers/ClauseAnalyzerHandler.cs:96-98` | PASS (n/a) |
| `DateExtractorHandler`, `FinancialCalculatorHandler`, `FinancialCalculationToolHandler`, `TemplateHandler`, `ClauseComparisonHandler`, `TextRefinementHandler` | none (LLM/pure computation) | no-Dataverse | zero hits in Dataverse-client grep across `Handlers/**` (26-file result set) | PASS (n/a) |
| `GetWorkspaceTabContentHandler`, `UpdateWorkspaceTabHandler`, `CloseWorkspaceTabHandler`, `SendWorkspaceArtifactHandler` | `IWorkspaceStateService` → **Cosmos DB** | no-Dataverse | `Services/Workspace/WorkspaceStateService.cs:76-79` (`CosmosClient`) | PASS (n/a) |
| `ManagePinnedContextHandler` | `IPinnedContextRepository` → **Cosmos DB** | no-Dataverse | `Services/Ai/Memory/PinnedContextRepository.cs:62` (`Container`) | PASS (n/a) |
| `WebSearchHandler`, `LegalResearchHandler`, `CodeInterpreterHandler`, `VerifyCitationsHandler` | none — external HTTP / Agent Service / internal index | no-Dataverse | `Handlers/WebSearchHandler.cs:132-133`; `Handlers/LegalResearchHandler.cs:161-162`; `Handlers/CodeInterpreterHandler.cs:129-130`; `Services/Ai/Safety/Citations/CitationVerificationService.cs:26` | PASS (n/a) |

### 3C. Legacy chat tools `Services/Ai/Chat/Tools/**` (audit item 3 — LIVE now, deleted at P2 task 036)

| Flow (tool class) | Dataverse leg | Identity mode | Evidence | Verdict |
|---|---|---|---|---|
| `DataverseQueryTools` | direct OData GET with caller's bearer token; errors when no HttpContext | **user-delegated** (no app-only path) | `Chat/Tools/DataverseQueryTools.cs:190-194, 216-218, 363-365` | **PASS** (+ observation O-1) |
| `AnalysisExecutionTools` | `IAnalysisOrchestrationService.ExecutePlaybookAsync` → full engine incl. Dataverse nodes + result persistence | **app-only** | `Chat/Tools/AnalysisExecutionTools.cs:44, 167`; wired live in `Chat/SprkChatAgentFactory.cs:964-988` | **FINDING F-1** |
| `WorkingDocumentTools` | `IWorkingDocumentService` → `IGenericEntityService` | **app-only** (writes) | `Chat/Tools/WorkingDocumentTools.cs:20, 458`; `WorkingDocumentService.cs:17`; wired `SprkChatAgentFactory.cs:924-941` | **FINDING F-2** |
| `CompareDocumentsTool` | `IDocumentDataverseService` (doc metadata by LLM-supplied ids) | **app-only** (reads) | `Chat/Tools/CompareDocumentsTool.cs:96` → `GraphModule.cs:68` | **FINDING F-2** |
| `DocumentSearchTools`, `KnowledgeRetrievalTools` | `IRagService` (AI Search) | no-Dataverse | `Chat/Tools/DocumentSearchTools.cs:33`; `Chat/Tools/KnowledgeRetrievalTools.cs:32` | PASS (n/a) |
| `CodeInterpreterTools`, `LegalResearchTools`, `TextRefinementTools`, `VerifyCitationsTool`, `WebSearchTools` | none | no-Dataverse | ctor fields (`Chat/Tools/*.cs`, grep-verified) | PASS (n/a) |

### 3D. LinearConsumers executor path (audit item 4)

| Component | Dataverse leg | Identity mode | Evidence | Verdict |
|---|---|---|---|---|
| `ActionRunner` | **none** — `IOpenAiClient` + `PromptSchemaRenderer` + options only | no-Dataverse (direct) | `Services/Ai/LinearConsumers/ActionRunner.cs:24-27` | PASS (n/a) |
| `ActionResolver` | `IConsumerRoutingService` (Binding lookup) + `IScopeResolverService` (Action JPS resolution) | **app-only, config-plane** | `LinearConsumers/ActionResolver.cs:29-30`; `Services/Ai/PublicContracts/ConsumerRoutingService.cs:123` (`IGenericEntityService`); `ScopeResolverService.cs:27` (`TokenCredential`) | **F-3 (config-plane)** |
| Callers (`MatterPreFillService`, `ProjectPreFillService`) | no Dataverse client in the service; results returned to the HTTP caller | n/a | `Services/Workspace/MatterPreFillService.cs:36-52` | PASS (n/a) |
| `DailyBriefingNarrator` (coded workflow, background) | `AnalysisActionService` (app-only Action JPS read) | app-only, **background** | `Services/Ai/Narrators/DailyBriefingNarrator.cs:74`; `AnalysisActionService.cs:16` | ACCEPTED (ADR-028 background) |
| `FileSummarizeService`, `DocumentProfileService`, `SessionFileTextSource` | none (zero hits in Dataverse-client grep) | no-Dataverse | grep result set (§1 step 1) | PASS (n/a) |

**Verdict**: the LinearConsumers executor performs Dataverse access only for **configuration resolution** (Binding row, Action JPS, playbook template) under app identity; the LLM call itself and all record writes are the caller's responsibility. Where a LinearConsumers destination writes a record (e.g. pre-fill), the write happens in the calling surface, not inside ActionRunner.

### 3E. AI-request-path infrastructure services (audit item 5, in-scope non-tool code)

| Service | Dataverse leg | Identity mode | Reachable from LLM args? | Evidence | Verdict |
|---|---|---|---|---|---|
| `ChatSessionManager` / `ChatDataverseRepository` (session + message persistence) | `IGenericEntityService` | app-only | No — BFF-controlled parameters | `Chat/ChatSessionManager.cs:42`; `Chat/ChatDataverseRepository.cs:23,28` | **F-4 (infra, noted)** |
| `PlaybookChatContextProvider`, `ChatContextMappingService`, `AnalysisChatContextResolver`, `DynamicCommandResolver`, `SprkChatAgentFactory` (context/agent setup) | `IGenericEntityService` | app-only | No — host-context ids validated by session setup | `Chat/PlaybookChatContextProvider.cs:97`; `Chat/ChatContextMappingService.cs:51`; `Chat/AnalysisChatContextResolver.cs:125`; `Chat/DynamicCommandResolver.cs:59`; `Chat/SprkChatAgentFactory.cs:671` | **F-4 (infra, noted)** |
| `PlaybookService`, `PlaybookLookupService`, `ScopeResolverService`, `AnalysisToolService`, `AnalysisActionService`, `AnalysisSkillService`, `AnalysisKnowledgeService`, `AnalysisPersonaService`, `LookupChoicesResolver`, `ConsumerRoutingService` | direct Web API / `IGenericEntityService` | app-only | No — read AI **catalog config** (playbooks, actions, scopes, bindings), not user business data | §2 rows 5-6; `ToolFrameworkExtensions.cs:32` | **F-3 (config-plane, noted)** |
| `MembershipResolverService` + `Membership*` | `IDataverseService` | app-only | Background (`MembershipReconciliationJob`, `MembershipJunctionUpdaterHost`) + consumed by `NodeService`/`DailyBriefingCollector` inside the frozen engine | `Services/Ai/Membership/*.cs` | ACCEPTED (background) / folds into F-1 for the engine leg |
| `AppOnlyAnalysisService` | `IDocumentDataverseService`, `IAnalysisDataverseService` | app-only **by design** | **No** — callers are exclusively background job handlers: `AppOnlyDocumentAnalysisJobHandler`, `EmailAnalysisJobHandler`, `ProfileSummaryJobHandler` | `Services/Ai/AppOnlyAnalysisService.cs:26-27`; `Services/Ai/Jobs/AppOnlyDocumentAnalysisJobHandler.cs:26`; `Jobs/EmailAnalysisJobHandler.cs:27`; `Jobs/ProfileSummaryJobHandler.cs:36` | **ACCEPTED** (ADR-028 background app-only; not reachable from tool plane) |
| `ScopeManagementService` | `IDataverseService` (scaffolding — self-documented "no method calls yet") | app-only | No callers found (grep across `Api/`, `Services/`) | `Services/Ai/ScopeManagementService.cs:15-17` | Not reachable — no finding (Track-B deadwood candidate) |
| `TopicRegistryTtlLookup` (Insights) | `IDataverseService` | app-only | Only via `InsightsPlaybookExecutionCache` (Insights pipeline, not the chat tool plane) | `Services/Ai/Insights/TopicRegistryTtlLookup.cs` | ACCEPTED (background/Insights plane) |

---

## 4. Findings

### F-1 — CRITICAL: LLM-initiated tool calls reach app-only Dataverse **write** nodes via the legacy playbook engine

**Call chains** (two live entry points into the same engine):

1. `AnalysisExecutionTools.ExecutePlaybookAsync` (`Chat/Tools/AnalysisExecutionTools.cs:167`, wired live into the chat agent at `Chat/SprkChatAgentFactory.cs:964-988`) → `AnalysisOrchestrationService` (`Services/Ai/AnalysisOrchestrationService.cs:34-50`) → `INodeService` → node executors.
2. `InvokePlaybookHandler` (tool plane, `Handlers/InvokePlaybookHandler.cs:134-135`) → `IInvokePlaybookAi` → `IPlaybookOrchestrationService` → `IAnalysisOrchestrationService` → same node executors (chain documented at `Handlers/InvokePlaybookHandler.cs:106-112`).

**App-only terminal writes** (all via `GraphModule.cs:46-70` → `DataverseServiceClientImpl.cs:59-64` ClientSecret, or `GraphModule.cs:78` → `DataverseWebApiService.cs:56`):

- `CreateTaskNodeExecutor` — `IGenericEntityService` (`Services/Ai/Nodes/CreateTaskNodeExecutor.cs:38,43`)
- `CreateNotificationNodeExecutor` — `IGenericEntityService` (`Nodes/CreateNotificationNodeExecutor.cs:65,70`)
- `UpdateRecordNodeExecutor` — PATCH via `IDataverseService` + `IFieldMappingDataverseService` (`Nodes/UpdateRecordNodeExecutor.cs:11,52,248`)
- Result persistence — `AnalysisResultPersistence` via `IAnalysisDataverseService`/`IDocumentDataverseService` (`Services/Ai/AnalysisResultPersistence.cs:19-21`)

**Impact**: an LLM-initiated playbook invocation can create/update Dataverse records under the BFF's application identity, bypassing the calling user's security roles and row-level security. Whether writes actually occur depends on playbook content, but the *capability* is reachable from the tool plane today.

**Remediation mapping**: chain 1 is removed by **task 034** (FR-P2-05 hard cutover chat NL → loop) + **task 036** (FR-P2-07 DELETE `Chat/Tools/**`). Chain 2 (`InvokePlaybookHandler`) lives in the go-forward Handlers plane and the engine it calls is **frozen but retained** (task 044 deletes shells only, nodes untouched per plan §risk "044"). **Gap**: no P1/P2 task explicitly severs or OBO-migrates the `InvokePlaybookHandler → app-only engine` leg. → **Recommendation**: at gate 014, decide whether `invoke_playbook` stays in the ADR-039 closed catalog post-P2; if yes, file a follow-up task ("exclude Dataverse-writing nodes from loop-invocable playbooks, or migrate node executors to user-OBO") anchored to W-P2 (before task 037's injection/compound eval).

### F-2 — HIGH: LLM-initiated tool calls reach app-only Dataverse **reads/writes** with LLM-controlled parameters

| # | Chain | Evidence | Notes / mitigation | Remediation |
|---|---|---|---|---|
| F-2a | `QueryDataverseNodeExecutor` — arbitrary (playbook-authored) FetchXML executed via `IGenericEntityService.RetrieveMultipleAsync` under app identity; reachable via both F-1 chains | `Nodes/QueryDataverseNodeExecutor.cs:31,143-154,239` | FetchXML comes from playbook config (not raw LLM text), but the *invocation* and parameters are LLM-initiated | same as F-1 |
| F-2b | `CompareDocumentsTool` → `IDocumentDataverseService` — document metadata reads by LLM-supplied document ids under app identity | `Chat/Tools/CompareDocumentsTool.cs:96-97` | SPE content fetch may be user-scoped, but the Dataverse metadata read is not | deleted by **task 036** |
| F-2c | `AnalysisQueryHandler` → `GetAnalysisAsync(analysisGuid)` — reads analysis records by LLM-supplied GUID under app identity (no user-visibility check at the Dataverse layer) | `Handlers/AnalysisQueryHandler.cs:303`; `AnalysisResultPersistence.cs:19-20` | Go-forward Handlers plane — **survives P2** | no covering task → **follow-up needed** (gate 014 decision; candidate: migrate to `IDataverseUserClient` or drop from closed catalog) |
| F-2d | `WorkingDocumentHandler` + legacy `WorkingDocumentTools` → `IWorkingDocumentService` → `IGenericEntityService` — creates/updates working-document records under app identity | `Handlers/WorkingDocumentHandler.cs:135`; `WorkingDocumentService.cs:17`; `Chat/Tools/WorkingDocumentTools.cs:20,458` | Write target resolved from session metadata (limits blast radius to session-linked records), but identity is still app-only | Tools leg deleted by **task 036**; Handler leg **survives P2** → same gate-014 follow-up as F-2c |
| F-2e (LOW) | `RecallSessionFileHandler` → `ChatSessionManager` → `ChatDataverseRepository` — app-only session-file metadata reads | `Handlers/RecallSessionFileHandler.cs:213`; `Chat/ChatDataverseRepository.cs:23` | Keyed to the caller's own session id (BFF-controlled), so no cross-user reach in practice | fold into F-4 acceptance or the F-2c follow-up |

### F-3 — MEDIUM (accept-with-note candidate): config-plane app-only reads reachable from AI

`PlaybookService.cs:20`, `ScopeResolverService.cs:27`, `AnalysisToolService.cs:18`, `AnalysisActionService.cs:16` (all `TokenCredential`), `ConsumerRoutingService.cs:123` (`IGenericEntityService`), plus `GenericAnalysisHandler` (`:36`) and `ActionResolver` (`:29-30`) as consumers. These read **AI catalog configuration** (playbooks, Action JPS, scopes, tool/skill/persona definitions, Binding rows) — tenant-shared config, not user business data. App-only is arguably the *correct* identity for catalog reads (config must resolve identically regardless of caller), but it is technically an app-only Dataverse path reachable from AI code. **Recommendation**: record an explicit project-scoped acceptance at gate 014 ("config-plane catalog reads are app-only by design; user business data access is user-OBO"), so the MUST rule has a documented boundary instead of a silent exception.

### F-4 — LOW (infrastructure, noted): chat session/context persistence is app-only

`ChatSessionManager.cs:42` → `ChatDataverseRepository.cs:23,28` (session + message persistence) and the context-resolution services (`PlaybookChatContextProvider.cs:97`, `ChatContextMappingService.cs:51`, `AnalysisChatContextResolver.cs:125`, `DynamicCommandResolver.cs:59`, `SprkChatAgentFactory.cs:671`). Parameters are BFF-controlled (session ids, validated host-context ids) — not LLM-steerable — so this is infrastructure plumbing, not a tool-plane privilege escalation. Note for the ledger/ADR-040 work (P1) which will re-home session persistence.

### O-1 — Observation (not a finding): `DataverseQueryTools` pass-through token is not a true OBO exchange

`Chat/Tools/DataverseQueryTools.cs:216-218` forwards the **inbound** bearer token (audience = BFF API) directly to the Dataverse Web API instead of exchanging it. That is user-delegated and fail-closed (no app-only fallback), so it satisfies the MUST rule; but the token audience mismatch means the call likely 401s at Dataverse unless the client supplied a Dataverse-audience token. Functional concern only; class is deleted at task 036 and superseded by the six `dataverse.*` handlers, which do the exchange correctly.

---

## 5. Escalation (CLAUDE.md §6 — security-sensitive)

🔔 **Human Input Required** (consumed at gate task 014)

- **Situation**: The six new `dataverse.*` handlers fully satisfy the user-OBO MUST rule (fail-closed, no fallback). However, four **pre-existing** tool-plane flows reach app-only Dataverse: the legacy playbook engine via `AnalysisExecutionTools`/`InvokePlaybookHandler` (F-1, Critical — app-only writes), and `CompareDocumentsTool`/`AnalysisQueryHandler`/`WorkingDocumentHandler` (F-2, High — app-only reads/writes with LLM-controlled parameters).
- **Options**: (1) Accept F-1a/F-2a/F-2b as known-legacy debt remediated on schedule by tasks 034/036, AND file a follow-up task for the surviving Handlers-plane legs (F-1 chain 2, F-2c, F-2d); (2) pull the app-only handlers (`invoke_playbook`, `analysis_query`, `working_document`) from the live tool catalog now; (3) accelerate OBO migration of `IWorkingDocumentService`/analysis reads into P1.
- **Recommendation**: Option 1 — the P2 hard cutover already deletes the chat-tool legs; the gate should bind the surviving legs to an explicit task (ADR-039 catalog membership decision + OBO migration or exclusion), and record the F-3 config-plane acceptance boundary.
- Per task constraint, nothing was fixed in this task.

## 6. Attestation (G-P0 gate material for task 014)

Every flow the **new** AI tool plane exposes — the six `dataverse.*` handlers introduced by tasks 008/009 — runs user-OBO exclusively: each handler's only Dataverse dependency is `IDataverseUserClient` (§3A ctor citations), whose sole implementation acquires tokens via MSAL `AcquireTokenOnBehalfOf` with the calling user's assertion (`DataverseUserClient.cs:178-183`) and fails closed with no managed-identity or client-credentials fallback (`:100-106`, `:142-172`); no fallback path exists in the type, its interface contract, or its DI registration (`ToolFrameworkExtensions.cs:43,75`). Across the wider AI surface, all 30 handlers, 11 legacy chat tools, the LinearConsumers executor, and every AI-request-path service were enumerated and traced to their client acquisition (§2-3); app-only Dataverse access reachable from AI exists **only** in pre-existing legacy flows — the frozen playbook engine reachable via `AnalysisExecutionTools`/`InvokePlaybookHandler` and three legacy read/write tools — recorded as findings F-1/F-2 with call-chain evidence and mapped to their P2 remediation tasks (034/036) plus one identified gap (Handlers-plane legs surviving P2) escalated in §5 for a gate-014 decision, alongside two explicitly-bounded acceptances: config-plane catalog reads (F-3) and BFF-controlled session persistence (F-4). Background app-only processing (`AppOnlyAnalysisService`, briefing narrators, membership reconciliation) is reachable only from job handlers, which is legitimate per ADR-028. There are **zero unexplained app-only Dataverse paths** reachable from AI code.

---

*Task 012 · spaarke-ai-architecture-redesign-r1 · read-only audit · no code changed.*
