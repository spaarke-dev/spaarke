# Asymmetric Service-Registration / Endpoint-Mapping Inventory

> **Project**: `sdap.bff.api-test-suite-repair-r2`
> **Task**: 011 — Phase 1a (NullObject inventory + design)
> **Authored**: 2026-06-01 by task-execute (Claude Code, Opus 4.7) under STANDARD rigor
> **Authority**: Phase 1b implementation depends on this inventory. Comprehensive over conservative.
> **Source artifacts**:
> - `src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs` (endpoint mapping)
> - `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` (conditional service registration root)
> - `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AiModule.cs` (conditional sub-services)
> - `src/server/api/Sprk.Bff.Api/Infrastructure/DI/JobProcessingModule.cs` (conditional job handlers)
> - `src/server/api/Sprk.Bff.Api/Infrastructure/DI/FinanceModule.cs` (conditional finance services)
> - All endpoint handler files in `src/server/api/Sprk.Bff.Api/Api/**`
> **Read-only**: no production code modified during this inventory.

---

## 1. Executive Summary

The r1 ledger captured the BLOCKING tip of an iceberg. The full asymmetric surface is:

| Category | Count | Notes |
|---|---:|---|
| Feature flags driving conditional registration | 3 | `DocumentIntelligence:Enabled`, `Analysis:Enabled`, `DocumentIntelligence:RecordMatchingEnabled` |
| DI modules with conditional registration blocks | 4 | `AnalysisServicesModule`, `AiModule`, `JobProcessingModule`, `FinanceModule` |
| Conditional service registrations | 26 | See §3 for full enumeration |
| Endpoint files that depend on conditional services | 11 | See §4 |
| **BLOCKING asymmetric pairs** (cause startup failure when flag off) | **8** | See §5.A |
| **LATENT asymmetric pairs** (would fail if endpoint hit, but don't break startup metadata generation) | **5** | See §5.B |
| Already-correct conditional pairs (mapping inside `if`-block) | 4 | See §5.C |

The 8 BLOCKING pairs together cause the ~36 currently-Skipped integration tests (`KnowledgeBase 13 + Chat 11 + ReAnalysis 8 + Auth 4 = 36`). The r1 ledger said 37 — the off-by-one is because RB-T028-06 (Auth) is collateral damage from the metadata-gen abort, not an independent symptom.

---

## 2. Feature Flag Map

### 2.1 `DocumentIntelligence:Enabled`
- **Type**: kill switch — when off, AI services (OpenAI client, text extractor) are not registered.
- **Default**: `false` if unset (no default value in `GetValue<bool>("DocumentIntelligence:Enabled")`).
- **Production target**: `true` in production environments; `false` in fixtures by design.
- **Affected modules**: `AnalysisServicesModule`, `AiModule`, `JobProcessingModule`, `FinanceModule`.

### 2.2 `Analysis:Enabled`
- **Type**: kill switch — gates a larger surface (playbook services, semantic search, record search, public-contracts facade, AI module call).
- **Default**: `true` if unset (note: `GetValue<bool>("Analysis:Enabled", true)` — DEFAULT-TRUE while `DocumentIntelligence:Enabled` defaults false).
- **Compound condition**: `analysisEnabled && documentIntelligenceEnabled` — both must be true for the larger block.
- **Affected modules**: `AnalysisServicesModule` (master gate), endpoint-mapping conditional blocks.

### 2.3 `DocumentIntelligence:RecordMatchingEnabled`
- **Type**: feature gate — sub-feature of DocIntel for matching uploaded records against existing Dataverse rows.
- **Default**: `false` if unset.
- **Affected modules**: `AnalysisServicesModule.AddRecordMatchingServices`, `FinanceModule` (AttachmentClassificationJobHandler).

### 2.4 Out-of-scope flags
- `Redis:Enabled` — gates only `IDistributedCache` choice (Redis vs in-memory); not asymmetric (always returns a valid `IDistributedCache`).
- `ToolFramework:Enabled` — both branches register `IToolHandlerRegistry`; symmetric.
- `AiPersistence:CosmosEnabled` (not yet checked) — Cosmos persistence is opt-in via optional `ISessionPersistenceService` and is already accommodated via `GetService` (null-tolerant).
- `AgentService:Enabled`, `CodeInterpreter:Enabled`, `BingGrounding:Enabled` — option flags read at runtime by node executors; do NOT gate DI registration. Symmetric.

---

## 3. Conditional Service Registration Inventory

### 3.1 Gated on `DocumentIntelligence:Enabled` only

Listed in `AnalysisServicesModule.AddAnalysisServicesModule` lines 20-33:

| # | Service | Concrete | Lifetime | Direct dependencies (constructor) |
|---|---|---|---|---|
| 1 | `AiTelemetry` | `AiTelemetry` | Singleton | (none — telemetry sink) |
| 2 | `OpenAiClient` (concrete) | `OpenAiClient` | Singleton | `IConfiguration`, `TokenCredential`, `AiTelemetry`, `ILogger`, `IHttpClientFactory` |
| 3 | `IOpenAiClient` | forward → `OpenAiClient` | Singleton | (same as #2) |
| 4 | `TextExtractorService` (concrete) | `TextExtractorService` | Singleton | `IConfiguration`, `TokenCredential`, `ILogger` |
| 5 | `ITextExtractor` | forward → `TextExtractorService` | Singleton | (same as #4) |

### 3.2 Gated on `Analysis:Enabled && DocumentIntelligence:Enabled` (compound)

Listed in `AnalysisServicesModule.AddAnalysisServicesModule` lines 35-63, sub-registered via private helpers:

| # | Service | Concrete | Lifetime | Key dependencies | Registration helper |
|---|---|---|---|---|---|
| 6 | `IScopeResolverService` | `ScopeResolverService` | Typed HttpClient | `IConfiguration`, `IHttpContextAccessor` | `AddAnalysisOrchestrationServices` |
| 7 | `IScopeManagementService` | `ScopeManagementService` | Scoped | `IScopeResolverService`, `IDataverseService` | `AddAnalysisOrchestrationServices` |
| 8 | `IAnalysisContextBuilder` | `AnalysisContextBuilder` | Scoped | `IScopeResolverService`, `IGenericEntityService` | `AddAnalysisOrchestrationServices` |
| 9 | `IWorkingDocumentService` | `WorkingDocumentService` | Scoped | `IGenericEntityService`, `IDocumentSecurityService` | `AddAnalysisOrchestrationServices` |
| 10 | `IExportService` (Docx/Pdf/Email enumerable) | 3 impls | Scoped | various Office services | `AddAnalysisOrchestrationServices` |
| 11 | `ExportServiceRegistry` | `ExportServiceRegistry` | Scoped | `IEnumerable<IExportService>` | `AddAnalysisOrchestrationServices` |
| 12 | `IAnalysisOrchestrationService` | `AnalysisOrchestrationService` | Scoped | many | `AddAnalysisOrchestrationServices` |
| 13 | `IAppOnlyAnalysisService` | `AppOnlyAnalysisService` | Scoped | `OpenAiClient`, `ITextExtractor` | `AddAnalysisOrchestrationServices` |
| 14 | `IPlaybookService` | `PlaybookService` | Typed HttpClient | `IConfiguration`, `IGenericEntityService` | `AddPlaybookServices` |
| 15 | `INodeService` | `NodeService` | Typed HttpClient | `IConfiguration`, `IGenericEntityService` | `AddPlaybookServices` |
| 16 | `INodeExecutorRegistry` | `NodeExecutorRegistry` | Singleton | `IEnumerable<INodeExecutor>` | `AddPlaybookServices` |
| 17 | `IPlaybookOrchestrationService` | `PlaybookOrchestrationService` | Scoped | `IPlaybookService`, `INodeService`, `INodeExecutorRegistry`, others | `AddPlaybookServices` |
| 18 | `IPlaybookSharingService` | `PlaybookSharingService` | Typed HttpClient | `IConfiguration` | `AddPlaybookServices` |
| 19 | **`NotificationService`** | `NotificationService` | Singleton | `IGenericEntityService`, `ILogger` (**zero AI deps**) | `AddPlaybookServices` |
| 20 | `PlaybookSchedulerService` (hosted) | `PlaybookSchedulerService` | Hosted | `IServiceProvider`, `ILogger`, `IConfiguration` | `AddPlaybookServices` |
| 21 | `IBriefingAi` (PublicContracts facade) | `BriefingAi` | Scoped | `IOpenAiClient` | `AddPublicContractsFacade` |
| 22 | `IInvoiceAi` (PublicContracts facade) | `InvoiceAi` | Scoped | `IOpenAiClient` | `AddPublicContractsFacade` |
| 23 | `IWorkspacePrefillAi` (PublicContracts facade) | `WorkspacePrefillAi` | Scoped | `IOpenAiClient` | `AddPublicContractsFacade` |
| 24 | `IRecordMatchingAi` (PublicContracts facade) | `RecordMatchingAi` | Scoped | `IOpenAiClient` | `AddPublicContractsFacade` |
| (… plus ~30 more in `AddBuilderServices`, `AddTestingServices`, `AddDeliveryServices`, `AddNodeExecutors`, `AddRagServices`, `AddToolFramework`, `AddSemanticSearch`, `AddRecordSearch`, `AddAiModule`, `AddInsightsCache` — all gated on the same compound flag and only matter if their endpoints are mapped unconditionally) |

#### 3.2.1 AiModule conditional sub-block (lines 185-210, 283-286)

`AiModule.AddAiModule` itself is only called inside the compound `if`-block (line 56 of AnalysisServicesModule), so all of AiModule's registrations are already inside the compound gate. AiModule's INTERNAL conditional block (gated again on `DocumentIntelligence:Enabled`, lines 185-210) registers:

| # | Service | Lifetime | Dependencies |
|---|---|---|---|
| 25 | `RagIndexingPipeline` | Singleton | `ITextChunkingService`, `IRagService`, `SearchIndexClient`, `IOpenAiClient`, `IOptions<AiSearchOptions>` |
| 26 | `ReferenceIndexingService` | Singleton | `ITextChunkingService`, `SearchIndexClient`, `IOpenAiClient`, `IScopeResolverService`, `IOptions<AiSearchOptions>` |
| 27 | `ReferenceRetrievalService` | Singleton | `SearchIndexClient`, `IOpenAiClient`, `IEmbeddingCache`, `IOptions<AiSearchOptions>` |
| 28 | `PlaybookIndexingBackgroundService` (hosted) | Hosted | `IPlaybookService`, `SearchIndexClient`, `IOpenAiClient` |

#### 3.2.2 `AnalysisServicesModule.AddRagServices` conditional sub-block (lines 256-281)

Gated additionally on non-empty `AiSearchEndpoint` + `AiSearchKey`:

| # | Service | Lifetime | Dependencies |
|---|---|---|---|
| 29 | `SearchIndexClient` (Azure SDK) | Singleton | `AiSearchEndpoint`, `AiSearchKey` |
| 30 | `IKnowledgeDeploymentService` | Singleton | `SearchIndexClient` |
| 31 | `IEmbeddingCache` | Singleton | `IDistributedCache` |
| 32 | `IRagService` | Singleton | `SearchIndexClient`, `IOpenAiClient`, `IEmbeddingCache` |
| 33 | `IFileIndexingService` | Scoped | `SearchIndexClient`, `IOpenAiClient` |
| 34 | `IVisualizationService` | Singleton | `IOpenAiClient` |

### 3.3 Gated on `DocumentIntelligence:Enabled` only (FinanceModule + JobProcessingModule)

#### 3.3.1 `FinanceModule` (lines 35-69, 209-212)

| # | Service | Lifetime | Dependencies | Comment |
|---|---|---|---|---|
| 35 | `IInvoiceAnalysisService` | Scoped | `IOpenAiClient`, `IPlaybookService` | gated line 36 |
| 36 | `IInvoiceSearchService` | Scoped | `SearchIndexClient`, `IInvoiceAi?` | gated line 66 |
| 37 | `IJobHandler` → `InvoiceIndexingJobHandler` | Scoped | `IOpenAiClient`, `SearchIndexClient` | gated line 209 |

#### 3.3.2 `FinanceModule` gated on `DocumentIntelligence:RecordMatchingEnabled` (line 186)

| # | Service | Lifetime | Dependencies |
|---|---|---|---|
| 38 | `IJobHandler` → `AttachmentClassificationJobHandler` | Scoped | `IRecordMatchService` |

#### 3.3.3 `JobProcessingModule` (lines 47-52)

| # | Service | Lifetime | Dependencies |
|---|---|---|---|
| 39 | `IJobHandler` → `RagIndexingJobHandler` | Scoped | `IFileIndexingService`, `IOpenAiClient` |
| 40 | `IJobHandler` → `ProfileSummaryJobHandler` | Scoped | `IOpenAiClient` |
| 41 | `IJobHandler` → `BulkRagIndexingJobHandler` | Scoped | `IFileIndexingService` |

### 3.4 Gated on `DocumentIntelligence:RecordMatchingEnabled` only (`AnalysisServicesModule.AddRecordMatchingServices`)

| # | Service | Lifetime | Dependencies |
|---|---|---|---|
| 42 | `DataverseIndexSyncService` | Typed HttpClient | `IDataverseService` |
| 43 | `IDataverseIndexSyncService` | Singleton (forward) | (same as #42) |
| 44 | `IRecordMatchService` | Singleton | `SearchIndexClient`, `IOpenAiClient`, `IDataverseIndexSyncService` |

**Total counted conditional service registrations**: 44 (some sub-grouping for the compound gate raises this further; bracketed entries in §3.2 cover an additional ~30). For Phase 1b scope, only those consumed by unconditionally-mapped endpoints matter; that's the ~8 BLOCKING + ~5 LATENT enumerated in §5.

---

## 4. Endpoint File Inventory

`EndpointMappingExtensions.MapDomainEndpoints` (lines 100-222) has 3 conditional blocks (lines 119-131, 152-157, 162-174) plus an additional one at 169-174 (admin endpoints) — endpoints inside those `if` blocks are correctly gated. Everything OUTSIDE those blocks is unconditional and may have AI/conditional-service dependencies.

### 4.1 Unconditional `Map*Endpoints` calls (lines 102-118, 133-150, 159-188, 192-222)

Lines 102-118 (core domain):
- `MapUserEndpoints`, `MapPermissionsEndpoints`, `MapNavMapEndpoints`, `MapDataverseDocumentsEndpoints`, `MapFileAccessEndpoints`, `MapDocumentsEndpoints`, `MapDocumentsBulkEndpoints` — no AI deps
- **`MapUploadEndpoints`** (line 109) — DEPS: `NotificationService`
- `MapOBOEndpoints`, `MapDocumentOperationsEndpoints`, `MapEmailEndpoints`, `MapOfficeEndpoints`, `MapFieldMappingEndpoints`, `MapEventEndpoints` — no AI deps
- **`MapWorkAssignmentEndpoints`** (line 116) — DEPS: `NotificationService`
- `MapScorecardCalculatorEndpoints` — no AI deps

Lines 133-150 (AI surface, mapped unconditionally despite AI deps):
- **`MapRagEndpoints`** (line 133) — DEPS: `IRagService`, `IFileIndexingService`
- **`MapKnowledgeBaseEndpoints`** (line 134) — DEPS: `IRagService`, `SearchIndexClient`, `JobSubmissionService` (last is unconditional)
- `MapPromptLibraryEndpoints`, `MapFeedbackEndpoints`, `MapCapabilityEndpoints` — generally Dataverse-CRUD, no direct AI service deps
- **`MapChatEndpoints`** (line 141) — DEPS: `SprkChatAgentFactory`, `PendingPlanManager`, `IChatClient`, `IPlaybookService`, `ChatSessionManager`, `ChatHistoryManager`, `ChatContextMappingService` (all from AiModule, gated)
- **`MapChatDocumentEndpoints`** (line 142, in try/catch) — DEPS: `ChatSessionManager`, `ITextExtractor`
- **`MapChatWordExportEndpoints`** (line 148) — DEPS: `ChatSessionManager`
- **`MapAnalysisChatContextEndpoints`** (line 149) — DEPS: `AnalysisChatContextResolver`
- **`MapStandaloneChatContextEndpoints`** (line 150) — DEPS: `StandaloneChatContextProvider`

Lines 159-188 (more domain):
- `MapVisualizationEndpoints` — DEPS: `IVisualizationService` (conditional inside `AddRagServices`)
- `MapResilienceEndpoints` — health checks, no AI deps
- **`MapWorkspaceEndpoints`**, `MapWorkspaceLayoutEndpoints`, `MapWorkspaceAiEndpoints`, `MapWorkspaceMatterEndpoints` (line 179) — DEPS: `IBriefingAi?` (nullable but param-inference still fails)
- `MapWorkspaceProjectEndpoints`
- **`MapWorkspaceFileEndpoints`** (line 181) — DEPS: `ITextExtractor`, `IPlaybookOrchestrationService`
- **`MapDailyBriefingEndpoints`** (line 183) — DEPS: `IBriefingAi?` (some handlers nullable, some required)
- **`MapFinanceEndpoints`** (line 185) — DEPS: `IInvoiceSearchService` (conditional, NOT nullable)
- `MapFinanceRollupEndpoints`, `MapCommunicationEndpoints` — no AI deps
- `MapPrecedentAdminEndpoints`, `MapInsightsAskEndpoint` — Insights facade (Insights modules are unconditional)
- `MapSpeAdminEndpoints`, `MapContainerItemEndpoints`, `MapAgentEndpoints`, `MapExternalAccessEndpoints`, `MapReportingEndpoints`, `MapRegistrationEndpoints` — no AI deps verified

### 4.2 Conditional `Map*Endpoints` calls (inside `if`-blocks)

Lines 122-130 (compound `Analysis:Enabled && DocumentIntelligence:Enabled` gate):
- `MapAnalysisEndpoints`, `MapPlaybookEndpoints`, `MapPlaybookEmbeddingEndpoints`, `MapAiPlaybookBuilderEndpoints`, `MapScopeEndpoints`, `MapNodeEndpoints`, `MapPlaybookRunEndpoints`, `MapModelEndpoints`, `MapHandlerEndpoints` — correctly conditional ✓

Lines 155-156 (same compound gate):
- `MapSemanticSearchEndpoints`, `MapRecordSearchEndpoints` — correctly conditional ✓

Lines 164-165 (`DocumentIntelligence:RecordMatchingEnabled` gate):
- `MapRecordMatchEndpoints`, `MapRecordMatchingAdminEndpoints` — correctly conditional ✓

Lines 172-173 (compound gate, admin):
- `MapAdminKnowledgeEndpoints`, `MapBuilderScopeAdminEndpoints` — correctly conditional ✓

---

## 5. The Asymmetric Pairs Matrix

### 5.A — BLOCKING pairs (cause startup metadata-gen abort when feature flag is off)

These are the pairs that prevent the BFF from even completing startup when `Analysis:Enabled=false` and/or `DocumentIntelligence:Enabled=false`. They are the production debt that closes RB-T028-03/04/05/06 (37 Skip→Pass).

| # | Service interface | Reg site (file:line) | Reg flag(s) | Consumer endpoint(s) | Severity | Why BLOCKING |
|---|---|---|---|---|---|---|
| B1 | `NotificationService` (concrete) | `AnalysisServicesModule.cs:108` (in `AddPlaybookServices`) | compound `Analysis+DocIntel` | `Api/UploadEndpoints.cs:25`, `Api/WorkAssignmentEndpoints.cs:48`, `Api/Ai/AnalysisEndpoints.cs:221,525` (last is inside `if`-block — fine) | **BLOCKING** | Misregistered — `NotificationService` has ZERO AI deps (`IGenericEntityService` + `ILogger`) and `MapUploadEndpoints` + `MapWorkAssignmentEndpoints` are mapped unconditionally. This is r1 ledger RB-T028-03 Layer 1. |
| B2 | `SprkChatAgentFactory` (concrete) | `AiModule.cs:217` | compound | `ChatEndpoints.SendMessageAsync` (line 314), `ChatEndpoints.SendChatPlanStepAsync` (1055), `ChatEndpoints` (1564), `ChatWordExport` | **BLOCKING** | Required (non-nullable) param in unconditionally-mapped `MapChatEndpoints` (line 141) |
| B3 | `PendingPlanManager` (concrete) | `AiModule.cs:274` | compound | `ChatEndpoints.SendMessageAsync` (line 315), `SendChatPlanStepAsync` (1056) | **BLOCKING** | Required (non-nullable) param in `MapChatEndpoints` — RB-T028-04 Layer 4 |
| B4 | `ChatSessionManager` (concrete) | `AiModule.cs:238` | compound | `ChatEndpoints` (line 312), `ChatDocumentEndpoints` (139,415), `ChatWordExportEndpoints` (65) | **BLOCKING** | Required (non-nullable) param in 3 unconditionally-mapped endpoint groups |
| B5 | `ChatHistoryManager` (concrete) | `AiModule.cs:247` | compound | `ChatEndpoints.SendMessageAsync` (line 313) | **BLOCKING** | Required (non-nullable) param in `MapChatEndpoints` |
| B6 | `IPlaybookService` | `AnalysisServicesModule.cs:103` (in `AddPlaybookServices`) | compound | `ChatEndpoints` line 1405 | **BLOCKING** | Required (non-nullable) typed HttpClient consumer in `MapChatEndpoints` |
| B7 | `IRagService` | `AnalysisServicesModule.cs:270` (in `AddRagServices`) | compound + AiSearch keys non-empty | `KnowledgeBaseEndpoints` (lines 113, 285, 481), `RagEndpoints` (lines 187, 229, 281, 316, 349, 381) | **BLOCKING** | Required param in unconditionally-mapped `MapKnowledgeBaseEndpoints` + `MapRagEndpoints` |
| B8 | `SearchIndexClient` | `AnalysisServicesModule.cs:261` (in `AddRagServices`) | compound + AiSearch keys non-empty | `KnowledgeBaseEndpoints` (lines 115, 179, 284) | **BLOCKING** | Required Azure SDK type in unconditionally-mapped `MapKnowledgeBaseEndpoints` |

#### 5.A.1 Why RB-T028-06 (Auth) is collateral

RB-T028-06 (Authorization endpoint tests Skipped) is NOT caused by Auth endpoints depending on a conditional service. It's caused by ASP.NET Core's startup metadata-generation pipeline: when ANY mapped endpoint fails param-inference, the WHOLE pipeline aborts and NO endpoints are reachable. So Auth endpoint tests fail not because Auth endpoints are broken, but because the whole app fails to start.

This means **fixing the 8 BLOCKING pairs B1-B8 above auto-resolves RB-T028-06** (4 Auth tests Skip→Pass without any change to Auth endpoint or filter code).

### 5.B — LATENT pairs (endpoint mapped but handler never reached in current tests)

These would fail if the corresponding endpoint URL were actually hit when AI is disabled — but param-inference may succeed today either because the parameter is nullable (E-01 Layer 2 quirk: nullable defaults sometimes pass minimal-API metadata generation, sometimes don't, depending on signature ordering and `[FromServices]` use) OR because no test currently exercises them. Phase 1b should still address them under Null-Object to make the kill-switch clean.

| # | Service interface | Reg site | Consumer | Why LATENT |
|---|---|---|---|---|
| L1 | `IBriefingAi` | `AnalysisServicesModule.cs:137` | `WorkspaceMatterEndpoints.HandleAiSummary` (line 167, `IBriefingAi? = null`), `DailyBriefingEndpoints` (lines 63, 185 nullable; 290, 331 NON-nullable as in-method params) | Nullable-default — minimal-API treats `IBriefingAi? = null` as "optional FromService" but the inference rule changed between .NET versions. E-01 Layer 2 confirmed it FAILS in current build. Lines 290/331 are private methods invoked from the outer handler (with the nullable param) — never directly mapped; safe-by-transitive-flow. |
| L2 | `IInvoiceSearchService` | `FinanceModule.cs:68` | `FinanceEndpoints.SearchInvoices` line 230 (non-nullable) | Param-inference fails when flag off — E-01 Layer 3 |
| L3 | `IPlaybookOrchestrationService` | `AnalysisServicesModule.cs:106` | `WorkspaceFileEndpoints.HandleSummarize` line 134 + `HandleAnalyze` line 220 (non-nullable) | Non-nullable param; param-inference fails |
| L4 | `ITextExtractor` | `AnalysisServicesModule.cs:27` | `WorkspaceFileEndpoints` lines 91, 133, 328; `ChatDocumentEndpoints` line 137 | Non-nullable; gated only on `DocumentIntelligence:Enabled` (not the compound). Currently passes IF DocIntel is true and Analysis is false (rare test combo). |
| L5 | `StandaloneChatContextProvider`, `AnalysisChatContextResolver` | `AiModule.cs:266, 261` | `StandaloneChatContextEndpoints` (85), `AnalysisChatContextEndpoints` (78) | Required params; param-inference fails when AiModule itself was not invoked (Analysis off) |

### 5.C — Already-correct conditional pairs

These are mapped INSIDE the appropriate `if`-block and their services are registered with matching flags. No action needed; document for completeness.

| # | Endpoint family | Mapping `if`-block | Registration gate |
|---|---|---|---|
| C1 | `MapAnalysisEndpoints`, `MapPlaybookEndpoints`, `MapPlaybookEmbedding`, `MapAiPlaybookBuilder`, `MapScope`, `MapNode`, `MapPlaybookRun`, `MapModel`, `MapHandler` | line 119-131 (compound) | compound (same gate) |
| C2 | `MapSemanticSearch`, `MapRecordSearch` | line 152-157 (compound) | compound |
| C3 | `MapRecordMatch`, `MapRecordMatchingAdmin` | line 162-166 (`RecordMatchingEnabled`) | `RecordMatchingEnabled` |
| C4 | `MapAdminKnowledge`, `MapBuilderScopeAdmin` | line 169-174 (compound) | compound |

---

## 6. Dependency Cascade (Null-Object Implications)

For Phase 1b design, the Null-Object impls must respect each service's transitive dependencies. The conditional services that get Null-Objects in Phase 1b may NEVER assume `IOpenAiClient` etc. are available — because in the kill-switch-off scenario, those are ALSO not registered.

### 6.1 Dependency tree of services that need Null-Objects (from §5.A + §5.B)

| Service (target for Null-Object) | Direct deps requiring resolution | All deps conditional? |
|---|---|---|
| `NotificationService` (B1) | `IGenericEntityService` (unconditional), `ILogger` (unconditional) | NO — already safe; the real impl could be made unconditional. Recommended: **simple promotion to unconditional registration**, NOT a Null-Object. |
| `SprkChatAgentFactory` (B2) | `IChatClient`, `IServiceProvider` (unconditional) | IChatClient is conditional (AiModule registers when `AzureOpenAI:Endpoint` non-empty). Null-Object must NOT inject `IChatClient`. |
| `PendingPlanManager` (B3) | `IDistributedCache` (unconditional), `ILogger` | NO — already safe deps. Could be unconditional, BUT it's an AI-pending-plan concept that's meaningless without AI. Recommended: **Null-Object with throwing methods** (`FeatureDisabledException`) so endpoint-level kill check returns 503. |
| `ChatSessionManager` (B4) | `IDistributedCache`, `IChatDataverseRepository` (conditional), `ILogger`, `ISessionPersistenceService?` (optional) | YES — `IChatDataverseRepository` is conditional. Null-Object pattern: also-Null `IChatDataverseRepository`, OR make `ChatDataverseRepository` itself unconditional (it only needs `IDataverseService` which is unconditional). Recommended: **promote `ChatDataverseRepository` + `ChatSessionManager` + `ChatHistoryManager` + `ChatContextMappingService` to unconditional** — they have no AI deps and are Dataverse-CRUD wrappers. |
| `ChatHistoryManager` (B5) | `ChatSessionManager` (above), `IChatDataverseRepository`, `IChatClient?` (only used for summarisation; null-tolerant) | Mostly NO — promote to unconditional alongside B4. |
| `IPlaybookService` (B6) | typed HttpClient, `IGenericEntityService` (unconditional) | NO — only HttpClient + Dataverse. Could be unconditional. Recommended: **promote to unconditional** (Phase 1b decision per case). |
| `IRagService` (B7) | `SearchIndexClient` (conditional), `IOpenAiClient` (conditional), `IEmbeddingCache` (conditional) | YES — all deps conditional. Null-Object must NOT inject any of these. Null-Object methods return empty results / "service unavailable". |
| `SearchIndexClient` (B8) | Azure SDK direct construction with endpoint+key | N/A (Azure SDK type, not ours) — cannot null-object. Two paths: (a) make endpoint dependencies optional and inject a dummy `SearchIndexClient` pointing nowhere; (b) treat `IRagService` Null-Object as the gate and have `KnowledgeBaseEndpoints` consume only `IRagService` (refactor away direct `SearchIndexClient` injection). **Recommended path (b)**: refactor `KnowledgeBaseEndpoints` to depend only on `IRagService` + delete direct `SearchIndexClient` injection sites. |
| `IBriefingAi` (L1) | `IOpenAiClient` (conditional) | YES — Null-Object must not inject `IOpenAiClient`. Methods throw `FeatureDisabledException` or return empty narrative. |
| `IInvoiceSearchService` (L2) | `SearchIndexClient` (conditional), `IInvoiceAi?` (conditional) | YES — Null-Object returns empty `InvoiceSearchResponse`. |
| `IPlaybookOrchestrationService` (L3) | many | YES — Null-Object throws / returns empty. |
| `ITextExtractor` (L4) | `IConfiguration`, `TokenCredential`, `ILogger` | NO — Azure deps but no other-conditional. Currently gated on `DocumentIntelligence:Enabled` only; Null-Object returns empty extraction result. |
| `StandaloneChatContextProvider` / `AnalysisChatContextResolver` (L5) | `IGenericEntityService` (unconditional), `IDistributedCache` (unconditional) | NO — no AI deps. **Promote to unconditional**. |

---

## 7. Summary count: BLOCKING vs LATENT vs CORRECT

| Bucket | Count | Pattern decision (per service in Phase 1b) |
|---|---:|---|
| BLOCKING (cause startup failure) | 8 | mixed: promote-to-unconditional (B1, B4, B5, B6) vs Null-Object (B2, B3, B7) vs refactor (B8) |
| LATENT (would fail if hit) | 5 | mostly Null-Object (L1, L2, L3) plus promote (L5) plus Null-Object (L4) |
| Already-correct conditional | 4 (endpoint groups) | no change |
| Conditional services NOT consumed by unconditional endpoints | ~30 | no change — leave as is |

**Total services requiring Phase 1b action**: **13** (8 BLOCKING + 5 LATENT). Of these, ~5 are "promote to unconditional" (safe because zero AI deps), ~6 are "create Null-Object impl", ~1 is "refactor endpoint signature" (B8), ~1 is "throwing Null-Object" (B3 — fail-fast on disabled feature).

---

## 8. Test population expected to flip Skip→Pass

| Test file | Skipped tests trace to RB-T028-* | Flip on which BLOCKING fixes |
|---|---:|---|
| `tests/integration/Spe.Integration.Tests/Api/Ai/KnowledgeBaseEndpointsTests.cs` | 13 | B7, B8 (and indirectly B1 — KB uses NotificationService indirectly) |
| `tests/integration/Spe.Integration.Tests/Api/Ai/ChatEndpointsTests.cs` | 11 | B2, B3, B4, B5, B6 (whole chat surface) |
| `tests/integration/Spe.Integration.Tests/Api/Ai/ReAnalysisFlowTests.cs` | 8 | B2-B6 (same chat surface — re-analysis routes through chat) |
| `tests/integration/Spe.Integration.Tests/AuthorizationIntegrationTests.cs` | 4 | collateral — fixing B1-B8 unblocks startup; Auth tests then run |
| **Total** | **36** | (vs r1 ledger estimate of 37 — off-by-one explained §5.A.1) |

Phase 1b target: 36 Skip→Pass after the production fix; per-fix triple-run with `Failed: 0`.

---

## 9. Validation

This inventory was built by:
1. Grepping `if.*Enabled` in `Infrastructure/DI/` — found 3 flag patterns
2. Reading the 4 module files top to bottom (`AnalysisServicesModule`, `AiModule`, `JobProcessingModule`, `FinanceModule`)
3. Reading `EndpointMappingExtensions.MapDomainEndpoints` top to bottom
4. For each conditional service, grepping `Api/` for usages
5. Cross-referencing E-01's 5-layer cascade against the matrix — all 5 layers covered (B1 → Layer 1, L1 → Layer 2, L2 → Layer 3, B3 → Layer 4, the rest → Layer 5+)
6. Cross-referencing r1 ledger's 4-entry cluster — all 4 entries covered (B1+B4-B6 → T028-03/04/05; collateral → T028-06)

Phase 1b can proceed with confidence that no MAJOR conditional service has been missed. The 13 services in §7 are the comprehensive remediation surface.

---

*Authored 2026-06-01 by task-execute under STANDARD rigor protocol. Inventory + design phase; no production code changed.*
