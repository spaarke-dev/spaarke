using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Insights;
using Sprk.Bff.Api.Services.Ai.Insights.Routing;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Ai.RecordSearch;
using Sprk.Bff.Api.Services.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Workspace;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI module for Document Intelligence, Analysis, and AI services (ADR-010, ADR-013).
/// </summary>
public static class AnalysisServicesModule
{
    public static IServiceCollection AddAnalysisServicesModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // R5 Summarize telemetry (task 008, D1-08). Unconditional registration per R5 CLAUDE.md §3.2
        // (R5 introduces NO new feature flags; kill-switch coverage inherits from existing AI flags
        // via NullSprkChatAgentFactory). Registering this singleton outside the documentIntelligenceEnabled
        // gate is intentional: the telemetry surface is harmless when unused (zero events emitted) and
        // sidesteps the asymmetric-registration anti-pattern (CLAUDE.md §10 F.1) by removing the conditional
        // entirely. Downstream consumers (tasks 012 / 014 / 015) inject this singleton and call
        // RecordSummarizeInvocation; task 007 cleanup may call RecordSessionFilesIndexSize.
        services.AddSingleton<Sprk.Bff.Api.Telemetry.R5SummarizeTelemetry>();

        // R6 Pillar 6c (FR-37 / task 063) — IContextEventEmitter for context.* execution-trace
        // events (tool_call_started/completed, knowledge_retrieved, playbook_node_executing/completed,
        // decision_made). Registered unconditionally at the top of the module like R5SummarizeTelemetry
        // so emission sites in PlaybookOrchestrationService / ToolHandlerToAIFunctionAdapter
        // can resolve it regardless of feature flags. ADR-015 binding: the implementation is structurally
        // constrained to deterministic IDs only — see ContextEventEmitter.cs class header.
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Telemetry.IContextEventEmitter,
            Sprk.Bff.Api.Services.Ai.Telemetry.ContextEventEmitter>();

        // Insights Engine Widgets r1 telemetry (project ai-spaarke-insights-engine-widgets-r1 task 050).
        // Meter "Sprk.Bff.Api.InsightWidgets" per Q-U8 evidence resolution (matches all 9 existing BFF
        // meter `Sprk.Bff.Api.<Feature>` convention). Unconditional registration mirrors R5SummarizeTelemetry
        // precedent above — telemetry surface is harmless when unused and avoids the asymmetric-registration
        // anti-pattern (CLAUDE.md §10 F.1). Task 051 injects this singleton at the /api/insights/ask
        // invocation path and calls RecordInvocation with bounded tags {topic, mode, outcome, cacheHit, tenantId}.
        services.AddSingleton<Sprk.Bff.Api.Telemetry.InsightWidgetsTelemetry>();

        // multi-container-multi-index-r1 indexer-routing-fix (Tier 3) — TRULY UNCONDITIONAL.
        // ISearchIndexNameResolver is consumed by RagIndexingJobHandler / BulkRagIndexingJobHandler /
        // IndexingWorkerHostedService — all 3 are registered unconditionally as scoped IJobHandler / IHostedService.
        // The resolver delegates to IGenericEntityService (registered unconditionally via GraphModule).
        // Registered HERE at the top of the module (above the documentIntelligence/analysis conditionals)
        // so it resolves correctly on BOTH AI-ON and AI-OFF paths. Lifetime: scoped (matches consumer
        // expectations + Dataverse Web API client lifetime).
        services.AddScoped<ISearchIndexNameResolver, SearchIndexNameResolver>();

        // R6 Pillar 7 (task 065, D-C-18) — IPinnedContextRepository.
        // **Hotfix moved out of compound (Analysis:Enabled && DocumentIntelligence:Enabled) gate**
        // for asymmetric-registration compliance (CLAUDE.md §10 F.1). MapPinnedMemoryEndpoints
        // (EndpointMappingExtensions.cs) registers /api/memory/pins UNCONDITIONALLY at startup; if
        // IPinnedContextRepository is missing from the service collection at endpoint-registration
        // time, Minimal API parameter-binding inference treats the parameter as a body candidate,
        // and GET/DELETE handlers fail with "Body was inferred but the method does not allow inferred
        // body parameters" — which crashes host startup and fails every WebApplicationFactory
        // integration test (observed in PR #395 CI run).
        // Dependencies (CosmosClient + IConfiguration) are unconditionally registered upstream,
        // so this registration is safe outside the gate. The repository only does work when an
        // authenticated request actually hits the endpoints (rate-limit + auth filter unchanged).
        // Lifetime: Scoped (matches the WorkspaceStateService precedent in Pillar 6a).
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Memory.IPinnedContextRepository,
                           Sprk.Bff.Api.Services.Ai.Memory.PinnedContextRepository>();

        // multi-container-multi-index-r1 Phase G (task 102) — TRULY UNCONDITIONAL.
        // IAllowedIndexesProvider is consumed by KnowledgeDeploymentService (registered behind
        // the AI-Search-keys sub-gate in AddRagServices) to validate caller-supplied indexNames
        // against the sprk_aisearchindex catalog table. Singleton lifetime: the implementation
        // holds the IMemoryCache key + ttl as process-wide state and uses IServiceProvider.CreateScope
        // for per-load scoped IGenericEntityService resolution (no captive dependency).
        //
        // Registered HERE at the top of the module (above the AI conditionals) for the same reason
        // ISearchIndexNameResolver is — KnowledgeDeploymentService's optional ctor parameter resolves
        // on the AI-ON path, but having the registration available on the AI-OFF path keeps the DI
        // graph uniform and forward-compatible (if any other consumer is wired later).
        //
        // No new NuGet packages; IMemoryCache is already registered unconditionally via CacheModule.
        // AiSearchOptions binding is preserved by JobProcessingModule.
        services.AddSingleton<IAllowedIndexesProvider, DataverseAllowedIndexesProvider>();

        // multi-container-multi-index-r1 upload-indexing-centralization (scope extension) — TRULY UNCONDITIONAL.
        // IPostUploadIndexingEnqueuer is the single seam for post-upload RAG indexing.
        // Phase 3 (2026-06-08) — dispatches sync OBO indexing via IFileIndexingService.IndexFileAsync
        // (Pattern 4 — see sdap-auth-patterns.md). Scoped lifetime because IFileIndexingService is scoped.
        // See projects/spaarke-multi-container-multi-index-r1/notes/upload-indexing-centralization-design.md.
        services.Configure<Sprk.Bff.Api.Configuration.PostUploadIndexingOptions>(
            configuration.GetSection(Sprk.Bff.Api.Configuration.PostUploadIndexingOptions.SectionName));
        services.AddScoped<IPostUploadIndexingEnqueuer, PostUploadIndexingEnqueuer>();

        var documentIntelligenceEnabled = configuration.GetValue<bool>("DocumentIntelligence:Enabled");
        if (documentIntelligenceEnabled)
        {
            services.AddSingleton<Sprk.Bff.Api.Telemetry.AiTelemetry>();
            services.AddSingleton<OpenAiClient>();
            services.AddSingleton<IOpenAiClient>(sp => sp.GetRequiredService<OpenAiClient>());
            services.AddSingleton<TextExtractorService>();
            services.AddSingleton<ITextExtractor>(sp => sp.GetRequiredService<TextExtractorService>());
            Console.WriteLine("\u2713 Document Intelligence services enabled");
        }
        else
        {
            // L4 \u2014 NullTextExtractor (P3 Fail-Fast). Task 011 Phase 1b Tier 2, D-09 \u00a72 L4.
            // ITextExtractor is consumed unconditionally by WorkspaceFileEndpoints and
            // ChatDocumentEndpoints; registering a Null-Object here keeps DI param-inference
            // green when DocumentIntelligence:Enabled=false. Endpoint catches convert the
            // FeatureDisabledException to 503 ProblemDetails.
            services.AddSingleton<ITextExtractor, NullTextExtractor>();
            Console.WriteLine("\u26a0 Document Intelligence services disabled (DocumentIntelligence:Enabled = false) \u2014 NullTextExtractor registered");
        }

        var analysisEnabled = configuration.GetValue<bool>("Analysis:Enabled", true);
        if (analysisEnabled && documentIntelligenceEnabled)
        {
            AddAnalysisOrchestrationServices(services, configuration);
            AddPlaybookServices(services);
            AddBuilderServices(services);
            AddTestingServices(services, configuration);
            AddDeliveryServices(services);
            AddNodeExecutors(services);
            AddRagServices(services, configuration);
            AddToolFramework(services, configuration);

            services.AddSemanticSearch();
            Console.WriteLine("\u2713 Semantic search enabled");

            services.AddRecordSearch();
            Console.WriteLine("\u2713 Record search enabled (index: spaarke-records-index)");

            AddPublicContractsFacade(services);
            Console.WriteLine("\u2713 AI public-contracts facade enabled (Services/Ai/PublicContracts) \u2014 task 046, FR-E1");

            services.AddAiModule(configuration);
            Console.WriteLine("\u2713 AI Platform Foundation module enabled (DocumentParserRouter, SemanticDocumentChunker, RagQueryBuilder)");

            AddInsightsCache(services);
            Console.WriteLine("\u2713 Insights playbook execution cache enabled (D-P13, ADR-009)");

            AddInsightsIntentClassifier(services, configuration);
            Console.WriteLine("\u2713 Insights intent classifier enabled (Wave E2 task 041, FR-05)");

            // Wave E3 task 042 \u2014 Spaarke Assistant tool-call handler. Scoped because it is
            // consumed by InsightsOrchestrator (Scoped) and uses scoped delegate captures.
            // ADR-032 \u00a7F.1 inspection: the handler is consumed ONLY by InsightsOrchestrator
            // (an IInsightsAi impl registered behind the compound-AI-ON gate via
            // AddPublicContractsFacade below). When the compound gate is OFF, IInsightsAi
            // resolves to NullInsightsAi (registered in AddNullObjectsForCompoundOff per the
            // 2026-06-04 audit Migration PR #1 LATENT BUG #1 remediation), so this handler
            // is never resolved on the OFF path \u2014 no Null-Object mirror needed at this layer.
            services.AddScoped<Sprk.Bff.Api.Services.Ai.Insights.AssistantToolCallHandler>();
            Console.WriteLine("\u2713 Spaarke Assistant tool-call handler enabled (Wave E3 task 042, FR-05)");

            // Wave F task 052 \u2014 citation Href projection options for the Assistant
            // tool-call handler. BffBaseUrl is optional (unconfigured \u2192 Href = null).
            services.AddOptions<Sprk.Bff.Api.Configuration.AssistantCitationHrefOptions>()
                .BindConfiguration(Sprk.Bff.Api.Configuration.AssistantCitationHrefOptions.SectionName);
            Console.WriteLine("\u2713 Spaarke Assistant citation Href options bound (Wave F task 052, contract v1.1)");

            Console.WriteLine("\u2713 Analysis services enabled");
        }
        else if (!documentIntelligenceEnabled)
        {
            // L1/L3/B6/B7 Null-Objects for compound-OFF (DocumentIntelligence:Enabled=false branch).
            AddNullObjectsForCompoundOff(services);
            Console.WriteLine("\u26a0 Analysis services disabled (requires DocumentIntelligence:Enabled = true) \u2014 Null-Objects registered");
        }
        else
        {
            // L1/L3/B6/B7 Null-Objects for compound-OFF (Analysis:Enabled=false branch).
            AddNullObjectsForCompoundOff(services);
            Console.WriteLine("\u26a0 Analysis services disabled (Analysis:Enabled = false) \u2014 Null-Objects registered");
        }

        AddRecordMatchingServices(services, configuration);

        // R5 task 007 (D1-07) — bind cleanup-job options unconditionally so the
        // options graph is well-formed regardless of compound-gate state. The
        // hosted-service registration itself is still gated above (under the
        // compound AI gate via AddPlaybookServices).
        AddSessionFilesCleanupOptions(services, configuration);

        // R6 Pillar 6a (task 051, D-C-02) — WorkspaceStateService. Q4 hybrid persistence
        // (Redis hot 24h TTL + Cosmos durable on pin/matter-attach) for canonical
        // workspace-tab state. Per-tenant Redis key `workspace:{tenantId}:{sessionId}`
        // is BINDING per ADR-014 + NFR-16.
        //
        // §F.1 asymmetric-registration audit: UNCONDITIONAL registration. The consumers
        // are (a) GET /api/workspace/state endpoint (task 052, unconditional mapping in
        // R6 Pillar 6a) and (b) SprkChatAgentFactory per-turn snapshot (task 053). The
        // service has ZERO AI-internal constructor deps (cache + Cosmos + config + logger
        // only), so the asymmetric-registration anti-pattern does NOT apply — registration
        // is symmetric with endpoint mapping (both unconditional). No Null peer needed.
        //
        // §A.4 ADR-013 placement: workspace-state plumbing, NOT AI capability. Per refined
        // ADR-013, this service does NOT inject IOpenAiClient / IPlaybookService / any
        // AI-internal type. Placement-justification record:
        // `projects/spaarke-ai-platform-unification-r6/notes/task-051-placement-justification.md`.
        //
        // Lifetime: Scoped — matches IDistributedCache (Singleton) + CosmosClient
        // (Singleton) wrap pattern used by SessionPersistenceService + MatterMemoryService.
        // ZERO new Program.cs lines per ADR-010.
        services.AddScoped<IWorkspaceStateService, WorkspaceStateService>();

        // Unconditional chat-CRUD + notification services (task 011 Phase 1b Tier 1, D-09 §2 B1/B4/B5/L5).
        // These services have ZERO AI dependencies; their previous conditional registration was
        // misclassification (they were placed inside compound-gated helpers because AI features
        // CONSUME them, but their constructor deps are CRUD-only — IGenericEntityService,
        // IDistributedCache, IFieldMappingDataverseService, all unconditional per GraphModule).
        // Promotion-to-unconditional eliminates 8 startup metadata-gen abort sites and unblocks
        // ~36 currently-Skipped integration tests (RB-T028-03/04/05 + collateral RB-T028-06).
        // See projects/sdap.bff.api-test-suite-repair-r2/decisions/D-09-nullobject-design.md.
        AddUnconditionalChatAndNotificationServices(services);

        return services;
    }

    /// <summary>
    /// Registers chat-CRUD + notification services UNCONDITIONALLY (task 011, D-09 §2 B1/B4/B5/L5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// These 6 services were previously registered inside the compound
    /// <c>Analysis:Enabled &amp;&amp; DocumentIntelligence:Enabled</c> gate but have ZERO AI
    /// dependencies in their constructors. Promotion is per ADR-010 (DI minimalism favors
    /// unconditional registration when feature gating adds no value) and ADR-018 (kill switches
    /// must not gate CRUD-only services that AI features happen to consume).
    /// </para>
    /// <para>
    /// Items promoted:
    /// <list type="bullet">
    /// <item>B1: <see cref="Services.NotificationService"/> — was AnalysisServicesModule.AddPlaybookServices line 108</item>
    /// <item>B4: <see cref="IChatDataverseRepository"/> + <see cref="ChatDataverseRepository"/> — was AiModule line 230</item>
    /// <item>B4: <see cref="ChatSessionManager"/> — was AiModule lines 238–242</item>
    /// <item>B5: <see cref="ChatHistoryManager"/> — was AiModule line 247</item>
    /// <item>L5: <see cref="AnalysisChatContextResolver"/> — was AiModule line 261</item>
    /// <item>L5: <see cref="StandaloneChatContextProvider"/> — was AiModule line 266</item>
    /// </list>
    /// </para>
    /// </remarks>
    private static void AddUnconditionalChatAndNotificationServices(IServiceCollection services)
    {
        // B1 — NotificationService (deps: IGenericEntityService, ILogger — both unconditional).
        services.AddSingleton<Sprk.Bff.Api.Services.NotificationService>();

        // B4 — IChatDataverseRepository + ChatDataverseRepository
        // (deps: IGenericEntityService, IFieldMappingDataverseService, ILogger — all unconditional).
        services.AddScoped<IChatDataverseRepository, ChatDataverseRepository>();

        // B4 — ChatSessionManager (deps: IDistributedCache, IChatDataverseRepository,
        // ILogger, optional ISessionPersistenceService, optional ISessionFilesCleanupSignal —
        // both nullable injections are null-tolerant via GetService).
        //
        // R5 task 007 (D1-07) — ISessionFilesCleanupSignal is registered inside the
        // compound AI gate (AddPlaybookServices). When AI is OFF, GetService returns
        // null and ChatSessionManager's fire-and-forget signal call short-circuits.
        // Back-compat preserved for existing call sites and unit tests.
        services.AddScoped<ChatSessionManager>(sp => new ChatSessionManager(
            cache: sp.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(),
            dataverseRepository: sp.GetRequiredService<IChatDataverseRepository>(),
            logger: sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ChatSessionManager>>(),
            persistence: sp.GetService<Sprk.Bff.Api.Services.Ai.Sessions.ISessionPersistenceService>(),
            cleanupSignal: sp.GetService<Sprk.Bff.Api.Services.Ai.Chat.ISessionFilesCleanupSignal>()));

        // B5 — ChatHistoryManager (deps: ChatSessionManager + IChatDataverseRepository + ILogger — all unconditional).
        services.AddScoped<ChatHistoryManager>();

        // Tier 1.5 residual — ChatContextMappingService (deps: IDistributedCache + IGenericEntityService +
        // ILogger + optional IConnectionMultiplexer — all unconditional). Originally classified as
        // compound-gated in D-09; Phase 1c triage 2026-06-01 surfaced ChatEndpoints.GetContextMappingsAsync
        // + EvictContextMappingsCacheAsync inject this unconditionally → metadata-gen abort when AI flags off.
        // Promoted under D-02 cluster exception (still attributed to RB-T028-04 cluster fix). ADR-010 (AIPL-053).
        services.AddScoped<ChatContextMappingService>();

        // Tier 1.5 round 2 residual — DocxExportService (deps: ILogger + IOptions<AnalysisOptions> —
        // AnalysisOptions is bound unconditionally in ConfigurationModule.cs:55-59). Originally registered
        // inside AddAnalysisOrchestrationServices (conditional); Phase 1c re-triage 2026-06-01 surfaced
        // ChatWordExportEndpoints.ExportToWordAsync injects the concrete DocxExportService unconditionally
        // → metadata-gen abort when Analysis:Enabled=false. Same root cause as ChatContextMappingService.
        // Promoted under D-02 cluster exception. ADR-010.
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Export.DocxExportService>();

        // Tier 1.5 round 3 residual — IWorkingDocumentService (deps: IGenericEntityService + IServiceProvider +
        // IOptions<AnalysisOptions> + ILogger — all unconditional). Originally registered inside
        // AddAnalysisOrchestrationServices (conditional); Phase 1c re-re-triage 2026-06-01 surfaced
        // ChatEndpoints.SendMessageAsync line 318 injects IWorkingDocumentService as a hard [FromServices]
        // parameter → DI resolve failure (500 NoServiceFound) when Analysis:Enabled=false. Same root cause
        // pattern as ChatContextMappingService + DocxExportService. Note: ChatEndpoints.ApprovePlanAsync
        // line 1334 uses defensive RequestServices.GetService<>() — that path was tolerant; SendMessageAsync
        // was not. Promoted under D-02 cluster exception. ADR-010.
        services.AddScoped<IWorkingDocumentService, WorkingDocumentService>();

        // L5 — AnalysisChatContextResolver (deps: IGenericEntityService + IDistributedCache + ILogger).
        services.AddScoped<AnalysisChatContextResolver>();

        // L5 — StandaloneChatContextProvider (deps: IDistributedCache + ILogger).
        services.AddScoped<StandaloneChatContextProvider>();
    }

    /// <summary>
    /// Registers P3 Fail-Fast Null-Objects for compound-AI-OFF state (task 011 Phase 1b Tier 2,
    /// D-09 §2 L1/L3/B6/B7). Called from BOTH compound-off branches (DocIntel-off + Analysis-off).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each Null-Object throws <see cref="Configuration.FeatureDisabledException"/> on every
    /// public method; consumer endpoints catch this and convert to 503 ProblemDetails per
    /// ADR-018 + ADR-019. Logger-only deps keep these Null-Objects safe to register even when
    /// AI deps (<see cref="IOpenAiClient"/>, etc.) are absent.
    /// </para>
    /// <para>
    /// Per D-09 §8 Risks: <see cref="NullPlaybookService"/> is registered via plain
    /// <c>AddSingleton</c> (NOT <c>AddHttpClient</c>); the real <c>PlaybookService</c> uses
    /// typed HttpClient but the Null-Object has no need for HttpClient machinery.
    /// </para>
    /// </remarks>
    private static void AddNullObjectsForCompoundOff(IServiceCollection services)
    {
        // L1 — IBriefingAi (P3 Fail-Fast). Real impl registered in AddPublicContractsFacade.
        services.AddScoped<IBriefingAi, NullBriefingAi>();

        // ── L1 (cont.) — 2026-06-04 audit Migration PR #1 ────────────────────────────────
        // The four PublicContracts facade Null peers. Closes the LATENT BUG #1 gap
        // (bff-ai-architecture-audit-r1 W4 §4.5 + DR-003) where IInsightsAi was registered
        // unconditionally in InsightsFacadeModule while its transitive ctor deps were
        // conditional, and the other three PublicContracts facades (IInvoiceAi,
        // IWorkspacePrefillAi, IRecordMatchingAi) had no compound-OFF fallback at all.
        // All four real impls are now registered in AddPublicContractsFacade (compound-ON
        // only); the Null peers below complete the symmetric pair per the Endpoint↔DI
        // Registration Conditionality Symmetry Rule (audit W4 §4.1).

        // L1 — IInvoiceAi (P3 Fail-Fast). Real impl registered in AddPublicContractsFacade.
        // Consumed by Finance flows (InvoiceAnalysisService, InvoiceSearchService,
        // InvoiceIndexingJobHandler) which are unconditional; this Null peer keeps their
        // DI resolution green under compound-OFF and surfaces 503 ProblemDetails to callers.
        services.AddScoped<IInvoiceAi, NullInvoiceAi>();

        // L1 — IWorkspacePrefillAi (P3 Fail-Fast). Real impl registered in AddPublicContractsFacade.
        // Consumed by MatterPreFillService (Create-Matter wizard pre-fill). Stream-pre-stream
        // invariant: NullWorkspacePrefillAi throws synchronously BEFORE returning the
        // IAsyncEnumerable so the endpoint converts to 503 (no SSE body).
        services.AddScoped<IWorkspacePrefillAi, NullWorkspacePrefillAi>();

        // L1 — IRecordMatchingAi (P3 Fail-Fast). Real impl registered in AddPublicContractsFacade.
        // No CRUD-external consumers yet (per Phase 4 FR-C6 CI guard); pre-registered so the
        // compound-OFF DI graph remains uniform across all four PublicContracts facades.
        services.AddScoped<IRecordMatchingAi, NullRecordMatchingAi>();

        // L1 — IInvokePlaybookAi (P3 Fail-Fast). Real impl registered in AddPublicContractsFacade.
        // R6 Pillar 3 / Q11 / task 020 — generic playbook-invocation facade for the chat-tool
        // dispatch path (task 021 InvokePlaybookHandler), the M365 Copilot agent gateway, and
        // future R7+ consumers. Symmetric registration with the real impl per the
        // asymmetric-registration anti-pattern guard (CLAUDE.md §10 F.1).
        services.AddScoped<IInvokePlaybookAi, NullInvokePlaybookAi>();

        // L1 — IInsightsAi (P3 Fail-Fast). Real impl (InsightsOrchestrator) registered in
        // AddPublicContractsFacade. Consumed by /api/insights/ask + /api/insights/search +
        // /api/insights/assistant/query endpoints (Zone B) AND by the D-P8 SPE-upload
        // consumer + D-P4 Precedent projection sync (Zone B substrate writers). All callers
        // are unconditional; this Null peer ensures they see a contract-specified 503
        // FeatureDisabledException under compound-OFF instead of the prior 500
        // InvalidOperationException at DI resolution time. Stream-pre-stream invariant on
        // AssistantQueryStreamAsync per ADR-032 P3 kill-switch ordering.
        services.AddScoped<IInsightsAi, NullInsightsAi>();

        // L3 — IPlaybookOrchestrationService (P3 Fail-Fast). Real impl registered in AddPlaybookServices.
        services.AddScoped<IPlaybookOrchestrationService, NullPlaybookOrchestrationService>();

        // B6 — IPlaybookService (P3 Fail-Fast). Real impl registered in AddPlaybookServices as typed HttpClient.
        services.AddSingleton<IPlaybookService, NullPlaybookService>();

        // B7 — IRagService (P3 Fail-Fast). Real impl registered in AddRagServices behind AI Search keys gate.
        services.AddSingleton<IRagService, NullRagService>();

        // ── Tier 1.5 round 4 — flushed by Step 9.5 latent-bug scan 2026-06-01 ─────────────────
        // Two additional P3 Fail-Fast Null-Objects surfaced by the same anti-pattern that the
        // 3 prior Tier 1.5 rounds fixed: unconditional endpoint mappings whose handlers inject
        // services that AddRagServices registers behind a compound + AI Search keys sub-gate.
        // Absorbed under D-02 cluster exception per user approval. Same root cause pattern as
        // the prior residuals (ChatContextMappingService, DocxExportService, IWorkingDocumentService).
        //
        // IVisualizationService — consumed by VisualizationEndpoints (EndpointMappingExtensions.cs:159
        //   app.MapVisualizationEndpoints() — unconditional). Real impl registered AddRagServices line 423.
        //   Lifetime: singleton (matches real VisualizationService).
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Visualization.IVisualizationService, NullVisualizationService>();

        // IFileIndexingService — consumed by RagEndpoints handlers IndexFile + SendToIndex
        //   (EndpointMappingExtensions.cs:133 app.MapRagEndpoints() — unconditional) AND by
        //   IndexingWorkerHostedService / RagIndexingJobHandler / BulkRagIndexingJobHandler. Real
        //   impl registered AddRagServices line 422. Lifetime: scoped (matches real FileIndexingService).
        services.AddScoped<IFileIndexingService, NullFileIndexingService>();

        // ⚠ NOTE: ISearchIndexNameResolver was incorrectly placed HERE (inside
        // AddNullObjectsForCompoundOff) — that method only runs on the AI-OFF path,
        // so RagIndexingJobHandler / BulkRagIndexingJobHandler / IndexingWorkerHostedService
        // failed at startup in the AI-ON live env with
        // "Unable to resolve service for type ISearchIndexNameResolver".
        // FIXED 2026-06-08: registration moved to the TOP of AddAnalysisServicesModule
        // (above the documentIntelligence/analysis conditionals) so it's truly unconditional.

        // B2 — SprkChatAgentFactory (P3 Fail-Fast subclass). Task 011 Phase 1b Tier 3, D-09 §2 B2.
        // Real impl registered unconditionally inside AddAiModule (only invoked on compound-ON path).
        // The Null subclass uses the protected base ctor that bypasses AI deps; consumed unconditionally
        // by ChatEndpoints (MapChatEndpoints) which catches FeatureDisabledException → SSE error / 503.
        services.AddSingleton<SprkChatAgentFactory>(sp =>
            new NullSprkChatAgentFactory(sp.GetRequiredService<ILogger<SprkChatAgentFactory>>()));

        // B3 — PendingPlanManager (P3 Fail-Fast subclass). Task 011 Phase 1b Tier 3, D-09 §2 B3.
        // Real impl registered scoped inside AddAiModule (compound-ON only). The Null subclass
        // surfaces compound-intent plan operations as FeatureDisabledException; ChatEndpoints
        // SendMessageAsync + ApprovePlanAsync catch and emit SSE error chunks per ADR-018.
        services.AddScoped<PendingPlanManager>(sp =>
            new NullPendingPlanManager(sp.GetRequiredService<ILogger<PendingPlanManager>>()));

        // Insights intent classifier (Wave E2 task 041 / FR-05) — P3 Fail-fast Null-Object
        // per ADR-032 + task 041 POML constraint. The classifier is a query/computation
        // service (returns a routing decision); a P2 Quiet no-op would silently mis-route
        // every query to the RAG path under disabled state and mislead observability.
        // Mirrors the IRagService P3 pattern shipped 2026-06-01.
        //
        // ADR-032 §F.1 inspection: registered here in the compound-OFF else-branch alongside
        // the real registration in AddInsightsIntentClassifier (compound-ON only). Forward-
        // compat with Wave E3 Spaarke Assistant integration which will inject IInsightsIntentClassifier
        // into a (potentially unconditionally-mapped) Assistant endpoint. Pre-registering the
        // Null-Object now prevents the asymmetric-registration anti-pattern from being introduced
        // when E3 lands.
        services.AddSingleton<IInsightsIntentClassifier>(sp =>
            new NullInsightsIntentClassifier(sp.GetRequiredService<ILogger<InsightsIntentClassifier>>()));

        // R5 task 014 / D2-04 — SessionSummarizeOrchestrator (P3 Fail-Fast subclass).
        // The R5 Summarize endpoint (POST /api/ai/chat/sessions/{sessionId}/summarize) is
        // mapped UNCONDITIONALLY in EndpointMappingExtensions and injects the concrete
        // SessionSummarizeOrchestrator. Real impl is registered scoped inside
        // AddAnalysisOrchestrationServices (compound-ON only). Without this Null mirror,
        // minimal-API parameter inference fails at host startup ("Failure to infer one
        // or more parameters") because IRagService + IOpenAiClient + IGenericEntityService
        // are unavailable when compound AI is OFF. The Null subclass throws
        // FeatureDisabledException at first MoveNextAsync(); SummarizeSessionEndpoint
        // catches it and emits a canonical 503 ProblemDetails per ADR-018 + ADR-019.
        // Canonical pattern siblings: NullSprkChatAgentFactory (B2), NullPendingPlanManager (B3).
        services.AddScoped<SessionSummarizeOrchestrator>(sp =>
            new NullSessionSummarizeOrchestrator(
                sp.GetRequiredService<ILogger<SessionSummarizeOrchestrator>>()));
    }

    private static void AddAnalysisOrchestrationServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AnalysisOptions>(configuration.GetSection(AnalysisOptions.SectionName));
        services.AddHttpClient<AnalysisActionService>();
        services.AddHttpClient<AnalysisSkillService>();
        services.AddHttpClient<AnalysisKnowledgeService>();
        services.AddHttpClient<AnalysisToolService>();
        // R6 Pillar 1 (D-A-02) — AnalysisPersonaService registered as typed HttpClient
        // sibling to the 4 canonical Analysis* services. Registration is INSIDE the compound
        // `Analysis:Enabled && DocumentIntelligence:Enabled` gate that wraps this method, so
        // it is symmetric with the consuming ScopeResolverService registration directly below
        // AND symmetric with the GET /api/ai/scopes/personas endpoint, which is mapped INSIDE
        // the same compound gate via EndpointMappingExtensions.MapScopeEndpoints. The
        // asymmetric-registration anti-pattern (CLAUDE.md §10 F.1) is verified compliant —
        // both the DI registration and the endpoint mapping share the same gate; no new
        // unconditional consumer of AnalysisPersonaService exists.
        services.AddHttpClient<AnalysisPersonaService>();
        services.AddHttpClient<IScopeResolverService, ScopeResolverService>();
        services.AddScoped<IScopeManagementService, ScopeManagementService>();
        services.AddScoped<IAnalysisContextBuilder, AnalysisContextBuilder>();
        // IWorkingDocumentService promoted to unconditional (task 011 Phase 1b Tier 1.5 round 3,
        // RB-T028-04 cluster residual — 2026-06-01). Phase 1c re-re-triage surfaced
        // ChatEndpoints.SendMessageAsync line 318 injects IWorkingDocumentService as a hard
        // [FromServices] parameter → DI resolve failure (500 NoServiceFound) when Analysis:Enabled=false.
        // See AddUnconditionalChatAndNotificationServices below.
        services.AddHttpContextAccessor();
        // DocxExportService promoted to unconditional (task 011 Phase 1b Tier 1.5 round 2, RB-T028-04
        // cluster residual — 2026-06-01). Phase 1c re-triage surfaced ChatWordExportEndpoints.ExportToWordAsync
        // injects the concrete DocxExportService unconditionally → metadata-gen abort when Analysis:Enabled=false.
        // See AddUnconditionalChatAndNotificationServices below.
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Export.IExportService, Sprk.Bff.Api.Services.Ai.Export.DocxExportService>();
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Export.IExportService, Sprk.Bff.Api.Services.Ai.Export.PdfExportService>();
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Export.IExportService, Sprk.Bff.Api.Services.Ai.Export.EmailExportService>();
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Export.ExportServiceRegistry>();
        // Extracted focused services from AnalysisOrchestrationService (ADR-010: constructor ≤10 params)
        services.AddScoped<AnalysisDocumentLoader>();
        services.AddScoped<AnalysisRagProcessor>();
        services.AddScoped<AnalysisResultPersistence>();
        services.AddScoped<IAnalysisOrchestrationService, AnalysisOrchestrationService>();
        services.AddScoped<IAppOnlyAnalysisService, AppOnlyAnalysisService>();

        // R5 task 012 (D2-03) — SessionSummarizeOrchestrator. Concrete sealed class (no
        // interface per ADR-010); registered Scoped to match the lifetime of its dependencies
        // (ChatSessionManager + IGenericEntityService are both Scoped; IRagService + IOpenAiClient
        // are Singleton; R5SummarizeTelemetry is Singleton — Scoped is the safe lifetime that
        // respects every wrapped lifetime).
        //
        // ZERO new Program.cs lines per R5 CLAUDE.md §3.3. ZERO new feature flags per R5
        // CLAUDE.md §3.2 — kill-switch coverage inherits from the parent compound gate
        // (Analysis:Enabled && DocumentIntelligence:Enabled) that wraps this method.
        //
        // §F.1 asymmetric-registration audit: this registration is unconditional within the
        // already-gated outer block; task 014 (endpoint) maps unconditionally and task 015
        // (tool handler) registers behind the same compound gate via SprkChatAgentFactory.
        // No new `if (R5Flag)` block introduced. Forward-compat: if a future fine-grained
        // R5 kill-switch is required, follow ADR-030 Null-Object pattern (not flag-conditional
        // registration).
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Chat.SessionSummarizeOrchestrator>();
        Console.WriteLine("✓ R5 SessionSummarizeOrchestrator registered (task 012; ADR-010 concrete; chat-session Summarize convergence)");

        // R6 Pillar 7 (task 064, D-C-17) — SummarizationCompressionService. Sliding-window
        // compression primitive: folds the oldest M chat turns into a single System-role
        // summary message when the conversation exceeds the NFR-10 8K system-prompt budget.
        // Foundation for task 067 (hierarchical memory composition); task 068 wires it into
        // SprkChatAgentFactory's per-turn prompt-assembly path.
        //
        // §F.1 asymmetric-registration audit: this registration is INSIDE the compound
        // (Analysis:Enabled && DocumentIntelligence:Enabled) gate. The only consumer in R6
        // is task 068's SprkChatAgentFactory wiring, which is itself inside the same
        // compound gate via the unconditional NullSprkChatAgentFactory peer (B2 pattern).
        // The Null-Object kill-switch posture is intrinsic to the service: it returns null
        // (P2 Quiet) when SummarizationCompression:Enabled=false or the OpenAI circuit is
        // broken, so the caller short-circuits to the raw window. No separate Null peer
        // needed at the DI layer.
        //
        // Options binding uses BindConfiguration; the B-G11 hardening pattern means the
        // options class does NOT decorate use-site-conditional fields with [Required], so
        // an app start with no SummarizationCompression section in appsettings is allowed
        // (defaults take over, kill switch defaults to true).
        //
        // Lifetime: Scoped — matches IOpenAiClient (Singleton) wrap pattern used elsewhere
        // in this module (Scoped is the safe lifetime that respects the wrapped singleton).
        services.AddOptions<Sprk.Bff.Api.Services.Ai.Memory.SummarizationCompressionOptions>()
            .BindConfiguration(Sprk.Bff.Api.Services.Ai.Memory.SummarizationCompressionOptions.SectionName);
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Memory.ISummarizationCompressionService,
                           Sprk.Bff.Api.Services.Ai.Memory.SummarizationCompressionService>();
        Console.WriteLine("✓ R6 Pillar 7 SummarizationCompressionService registered (task 064, D-C-17; sliding-window compression foundation)");

        // R6 Pillar 7 (task 065, D-C-18) — PinnedContextRepository. Cosmos-backed repository
        // for the user-curated PinnedContextItem "memory anchor" entity (spec FR-42:
        // pinned items NEVER drop from system-prompt assembly). Cosmos container `memory`
        // is reused (same partition key `/tenantId` as MatterMemoryService + workspace-tab
        // durable rows); document discriminator `documentType = "pinned-context"` +
        // id prefix `pinned-context_` co-exist with the other documentTypes on the same
        // partition without id collision.
        //
        // §F.1 asymmetric-registration audit: this registration is INSIDE the compound
        // (Analysis:Enabled && DocumentIntelligence:Enabled) gate matching the surrounding
        // Memory services. Consumers (task 067 hierarchical memory composition; task 070
        // Q7 Pinned Memory UI) are themselves inside the same compound gate.
        //
        // Placement (CLAUDE.md §10 / ADR-013): memory plumbing only. The repository injects
        // CosmosClient + IConfiguration only — no AI-internal collaborators
        // (IOpenAiClient, IPlaybookService, etc.). AI-internal callers consume this
        // repository directly per the 2026-05-20 refined ADR-013 boundary.
        //
        // Lifetime: Scoped — matches the WorkspaceStateService precedent (R6 Pillar 6a).
        // CosmosClient itself is Singleton (injected); the scoped wrapper is stateless.
        //
        // **PR #395 HOTFIX 2026-06-18**: the actual AddScoped registration was MOVED to the top
        // of this module (above the compound gate) to satisfy CLAUDE.md §10 F.1 asymmetric-
        // registration compliance. MapPinnedMemoryEndpoints (EndpointMappingExtensions.cs) is
        // unconditional; if the repository were registered only inside this gate, Minimal API
        // parameter-binding inference would treat IPinnedContextRepository as a body candidate
        // at endpoint-registration time when flags are OFF — crashing host startup with
        // "Body was inferred but the method does not allow inferred body parameters" on the
        // GET / DELETE handlers, which fails every WebApplicationFactory integration test.
        // The registration moved upward is unchanged in shape (same Scoped lifetime, same
        // interface→impl mapping); only the location changed.
        Console.WriteLine("✓ R6 Pillar 7 PinnedContextRepository registered earlier in module (unconditional; hotfix per PR #395)");

        // R6 Pillar 7 (task 066, D-C-19) — PinnedContextRecallService. Embedding-based
        // selective recall: ranks the user's pinned-context items by cosine similarity of
        // their content embedding against the current user-message embedding and returns
        // the top-K most relevant pins. Reuses the EXISTING IEmbeddingCache + IOpenAiClient
        // pipeline per the spec FR-43 rule ("use the existing IEmbeddingCache
        // infrastructure — do NOT introduce a new embedding service"). Foundation for task
        // 067 (hierarchical memory composition) when the matter has more pins than fit
        // the NFR-10 8K system-prompt budget.
        //
        // §F.1 asymmetric-registration audit: this registration is INSIDE the compound
        // (Analysis:Enabled && DocumentIntelligence:Enabled) gate matching the surrounding
        // Memory services. The only consumer in R6 is task 067's memory-composition
        // wiring, which is itself inside the same compound gate. The Null-Object
        // kill-switch posture is intrinsic to the service: it returns an empty list (P2
        // Quiet) when PinnedContextRecall:Enabled=false, no pins exist, or the embedding
        // pipeline fails; the caller (task 067) treats empty as "no recall — proceed with
        // unranked or skip recall". No separate Null peer needed at the DI layer.
        //
        // Options binding uses BindConfiguration; the B-G11 hardening pattern means the
        // options class does NOT decorate use-site-conditional fields with [Required], so
        // an app start with no PinnedContextRecall section in appsettings is allowed
        // (defaults take over, kill switch defaults to true).
        //
        // Placement (CLAUDE.md §10 / ADR-013): memory plumbing only. NO PublicContracts
        // facade because the only consumers are AI-internal callers per the refined
        // 2026-05-20 ADR-013 boundary rule.
        //
        // Lifetime: Scoped — matches the SummarizationCompressionService precedent (R6
        // task 064) and the IPinnedContextRepository it depends on (R6 task 065).
        services.AddOptions<Sprk.Bff.Api.Services.Ai.Memory.PinnedContextRecallOptions>()
            .BindConfiguration(Sprk.Bff.Api.Services.Ai.Memory.PinnedContextRecallOptions.SectionName);
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Memory.IPinnedContextRecallService,
                           Sprk.Bff.Api.Services.Ai.Memory.PinnedContextRecallService>();
        Console.WriteLine("✓ R6 Pillar 7 PinnedContextRecallService registered (task 066, D-C-19; embedding-based selective recall over pinned items)");

        // R6 Pillar 7 (task 067, D-C-20) — MemoryCompositionService. Hierarchical
        // memory composition orchestrator: produces a single tagged four-layer memory
        // block (recent verbatim / compressed mid-distance / retrieved old via
        // similarity / pinned context grouped by pinType) consumed by the chat
        // prompt-assembly path (task 068). Composes the three Pillar 7 primitives:
        //   - ISummarizationCompressionService (task 064) for the mid-distance summary
        //   - IPinnedContextRepository (task 065) for the always-included pinned tier
        //   - IPinnedContextRecallService (task 066) for the relevance-ranked
        //     retrieved-old tier
        // under the NFR-10 8K total budget. Layer drop priority on overflow:
        //   retrieved-old → compressed-mid → recent-verbatim oldest-first
        // Pinned tier is NEVER dropped (FR-42 invariant); when pinned alone exceeds
        // the budget, the service returns pinned-only and logs a warning so the
        // chat prompt builder (task 068) can apply the final hard guard.
        //
        // §F.1 asymmetric-registration audit: this registration is INSIDE the compound
        // (Analysis:Enabled && DocumentIntelligence:Enabled) gate matching the
        // surrounding Memory services (SummarizationCompressionService,
        // PinnedContextRepository, PinnedContextRecallService). The only consumer in
        // R6 is task 068's SprkChatAgentFactory wiring, itself inside the same
        // compound gate via the unconditional NullSprkChatAgentFactory peer (B2
        // pattern). The Null-Object kill-switch posture is intrinsic to the service:
        // it returns MemoryComposition.Empty (P2 Quiet) when
        // MemoryComposition:Enabled=false or when both the conversation and the pin
        // set are empty. No separate Null peer needed at the DI layer.
        //
        // Options binding uses BindConfiguration; the B-G11 hardening pattern means
        // the options class does NOT decorate use-site-conditional fields with
        // [Required], so an app start with no MemoryComposition section in
        // appsettings is allowed (defaults take over, kill switch defaults to true,
        // total budget defaults to 8000 per NFR-10).
        //
        // Placement (CLAUDE.md §10 / ADR-013): memory plumbing only. NO PublicContracts
        // facade because the only consumers are AI-internal callers per the refined
        // 2026-05-20 ADR-013 boundary rule.
        //
        // Lifetime: Scoped — matches the SummarizationCompressionService (task 064)
        // and PinnedContextRecallService (task 066) precedents it depends on.
        services.AddOptions<Sprk.Bff.Api.Services.Ai.Memory.MemoryCompositionOptions>()
            .BindConfiguration(Sprk.Bff.Api.Services.Ai.Memory.MemoryCompositionOptions.SectionName);
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Memory.IMemoryCompositionService,
                           Sprk.Bff.Api.Services.Ai.Memory.MemoryCompositionService>();
        Console.WriteLine("✓ R6 Pillar 7 MemoryCompositionService registered (task 067, D-C-20; hierarchical 4-layer memory composition with NFR-10 budget enforcement)");

        // R6 Pillar 7 (task 068, D-C-22 / FR-46) — PromptBudgetTracker. Shared per-turn
        // token-budget tracker that centralises the NFR-10 8K system-prompt budget across
        // the four chat prompt-assembly subsystems (factory blocks, document context,
        // knowledge inline content, memory composition). Each subsystem calls
        // TryReserve(layer, requestedTokens, sessionId, tenantId) before appending its
        // fragment; truncation telemetry is emitted on the `false` path so operators see
        // which layers were truncated and why. Reads its budget ceiling from
        // MemoryCompositionOptions.TotalTokenBudget (same 8K physical ceiling per NFR-10).
        //
        // §F.1 asymmetric-registration audit: registration is INSIDE the compound
        // (Analysis:Enabled && DocumentIntelligence:Enabled) gate matching the surrounding
        // Memory services. The Null-Object kill-switch posture is intrinsic: when the
        // compound AI gate is OFF, the tracker is never resolved because the chat factory
        // itself is the NullSprkChatAgentFactory. No separate Null peer needed at the DI
        // layer.
        //
        // Lifetime: Scoped — one tracker per HTTP request / per chat turn. Singleton
        // lifetime would leak budget across requests and is structurally wrong. Matches
        // the surrounding Pillar 7 services (MemoryCompositionService, recall, etc.).
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Memory.IPromptBudgetTracker,
                           Sprk.Bff.Api.Services.Ai.Memory.PromptBudgetTracker>();
        Console.WriteLine("✓ R6 Pillar 7 PromptBudgetTracker registered (task 068, D-C-22; shared 8K system-prompt budget across factory / document / knowledge / memory subsystems)");

        // --- InvokeInsightsQueryTool typed HttpClient ---
        // REMOVED in R6 Wave 10 / task 023 (D-A-15, Pillar 3 cleanup): the specialized
        // InvokeInsightsQueryTool C# bridge class was deleted in favor of the generic
        // InvokePlaybookHandler (R6 Pillar 3 / task 021). The chat-side path no longer
        // requires a typed HttpClient — the InsightsIntentClassifier playbook-vs-RAG
        // routing happens inside the orchestration layer the IInvokePlaybookAi facade
        // wraps (per FR-24 + docs/guides/INSIGHTS-PLAYBOOK-VS-RAG-DECISION-TREE.md).
        //
        // Zone B boundary preservation: the /api/insights/assistant/query endpoint
        // itself is unchanged and continues to enforce its own kill-switches (503
        // ai.insights.disabled / ai.rag.disabled / ai.intent-classification.disabled).
        // Any future chat-side caller that needs to invoke the Insights endpoint directly
        // can re-add an IHttpClientFactory registration here — the boundary pattern is
        // documented in the legacy InvokeInsightsQueryTool class (recoverable via
        // `git show HEAD~1:src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/InvokeInsightsQueryTool.cs`).
    }
    private static void AddPlaybookServices(IServiceCollection services)
    {
        services.AddHttpClient<IPlaybookService, PlaybookService>();
        services.AddHttpClient<INodeService, NodeService>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutorRegistry, Sprk.Bff.Api.Services.Ai.Nodes.NodeExecutorRegistry>();

        // Insights Engine r2 Wave D4 (task 033) — runtime per-(area, type) routing for
        // universal-ingest@v1. Consumed unconditionally by PlaybookOrchestrationService.
        // Scoped lifetime: matches PlaybookOrchestrationService + IScopeResolverService
        // (which the router depends on for action resolution). The router holds an
        // IMemoryCache reference, but the cache itself is a Singleton; the router's
        // ConcurrentDictionary<string, byte> for log-once miss reporting is process-wide
        // when promoted to Singleton in a future iteration. For now Scoped is sufficient
        // — cache lookups are cheap, and per-request instances avoid captive-dependency
        // concerns with the Scoped IGenericEntityService.
        //
        // ADR-032 §F.1 inspection: unconditional registration; consumer
        // (PlaybookOrchestrationService) is also unconditional. The asymmetric-registration
        // anti-pattern does NOT apply. Static-scan recipe verified compliant — no new
        // `if (flag) { ... }` block introduced.
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Insights.Routing.IInsightsActionRouter,
                           Sprk.Bff.Api.Services.Ai.Insights.Routing.InsightsActionRouter>();

        services.AddScoped<IPlaybookOrchestrationService, PlaybookOrchestrationService>();
        services.AddHttpClient<IPlaybookSharingService, PlaybookSharingService>();
        // NotificationService promoted to unconditional registration (task 011 Phase 1b Tier 1, D-09 §2 B1).
        // See AddUnconditionalChatAndNotificationServices below.

        // R3 task 023 (FR-2.8 / D2 / Q1): the legacy PlaybookSchedulerService BackgroundService has
        // been DELETED. Its discovery + fan-out logic is now PlaybookSchedulerJob (IScheduledJob),
        // registered + seeded in SchedulingModule.AddSchedulingModule(). The ScheduledJobHost
        // (Spaarke.Scheduling) drives the cron tick on the same 1h cadence (NFR-04 preserved).
        // Do NOT re-add an AddHostedService<PlaybookSchedulerService> here — that path was the
        // migration target.

        // R5 task 007 (D1-07) — Session-files cleanup hosted service per spec NFR-02
        // "Aggressive cleanup on session-end". Scheduled sweep (every IntervalHours;
        // default 6) + on-session-end immediate trigger via in-process channel;
        // idempotent. Inherits kill-switch from this compound AI gate per
        // R5 CLAUDE.md §3.2 (no new feature flag). ZERO new top-level Program.cs
        // lines per R5 CLAUDE.md §3.3 + ADR-010.
        //
        // ADR-010 single-seam justification: ISessionFilesCleanupSignal is the
        // single allowed interface seam in this addition — it exists solely to
        // keep ChatSessionManager unit-testable in isolation (mirrors the
        // ISessionPersistenceService nullable-injection convention). The
        // concrete SessionFilesCleanupSignal is the actual singleton owning
        // the Channel<SessionEndSignal>; the interface registration is a
        // forwarding alias (no new instance).
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Chat.SessionFilesCleanupSignal>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Chat.ISessionFilesCleanupSignal>(
            sp => sp.GetRequiredService<Sprk.Bff.Api.Services.Ai.Chat.SessionFilesCleanupSignal>());
        services.AddHostedService<Sprk.Bff.Api.Services.Ai.Chat.SessionFilesCleanupJob>();
        Console.WriteLine("✓ Session-files cleanup hosted service enabled (R5 task 007, NFR-02)");
    }

    /// <summary>
    /// R5 task 007 (D1-07) — bind <see cref="Sprk.Bff.Api.Services.Ai.Chat.SessionFilesCleanupOptions"/>
    /// to the <c>SessionFilesCleanup</c> configuration section. Called from the
    /// top of <see cref="AddAnalysisServicesModule"/> so the options graph is
    /// constructed regardless of compound-gate state (the hosted-service
    /// registration itself remains gated).
    /// </summary>
    private static void AddSessionFilesCleanupOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<Sprk.Bff.Api.Services.Ai.Chat.SessionFilesCleanupOptions>(
            configuration.GetSection(Sprk.Bff.Api.Services.Ai.Chat.SessionFilesCleanupOptions.SectionName));
    }

    /// <summary>
    /// Registers the <c>Services/Ai/PublicContracts/</c> facade introduced by task 046
    /// (sdap-bff-api-remediation-fix, FR-E1) and required by refined ADR-013 (2026-05-20).
    /// </summary>
    /// <remarks>
    /// <para>
    /// External CRUD code (Finance, Workspace, Jobs handlers outside <c>Services/Ai/</c>,
    /// Filters, non-AI Endpoints) MUST consume AI through these facades rather than
    /// injecting <see cref="IOpenAiClient"/> / <see cref="IPlaybookService"/> /
    /// <see cref="IPlaybookOrchestrationService"/> / <see cref="RecordSearch.IRecordSearchService"/>
    /// directly. See <c>.claude/constraints/bff-extensions.md</c> §A.4 for the binding
    /// pre-merge checklist and ADR-007 for the canonical facade pattern.
    /// </para>
    /// <para>
    /// Lifetimes: scoped uniformly. Constrained by <see cref="IPlaybookService"/>
    /// (transient typed HttpClient) and <see cref="IPlaybookOrchestrationService"/>
    /// (scoped). Scoped is the safe choice that respects every wrapped lifetime.
    /// </para>
    /// <para>
    /// Consumer migration (tasks 047–050) is OUT OF SCOPE for task 046: this method
    /// adds registrations only. No existing registrations are removed.
    /// </para>
    /// </remarks>
    private static void AddPublicContractsFacade(IServiceCollection services)
    {
        services.AddScoped<IBriefingAi, BriefingAi>();
        services.AddScoped<IInvoiceAi, InvoiceAi>();
        services.AddScoped<IWorkspacePrefillAi, WorkspacePrefillAi>();
        services.AddScoped<IRecordMatchingAi, RecordMatchingAi>();

        // R6 Pillar 3 / Q11 / task 020 — IInvokePlaybookAi facade. Consumed by task 021
        // InvokePlaybookHandler (chat-tool dispatch path) + future M365 Copilot agent
        // gateway + future R7+ consumers. Wraps IPlaybookOrchestrationService — same
        // pattern + same lifetime as IWorkspacePrefillAi above. ADR-013 facade boundary:
        // the implementation is the only allowed translation point between the
        // orchestration-internal PlaybookStreamEvent / NodeOutput / PlaybookRunMetrics
        // and the domain-shape PlaybookInvocationResult consumed by CRUD-side callers.
        // Null peer (NullInvokePlaybookAi) registered in AddNullObjectsForCompoundOff.
        services.AddScoped<IInvokePlaybookAi, InvokePlaybookAi>();

        // ── 2026-06-04 audit Migration PR #1 — relocated from InsightsFacadeModule ────────
        // IPlaybookExecutionEngine — Scoped (transitively consumes Scoped
        // IPlaybookOrchestrationService). Previously registered UNCONDITIONALLY in
        // InsightsFacadeModule. Its only consumer (InsightsOrchestrator via IInsightsAi
        // below) is conditional behind this compound-AI-ON gate, so the engine itself
        // must also be conditional per the Endpoint↔DI Registration Conditionality
        // Symmetry Rule (audit W4 §4.1).
        services.AddScoped<IPlaybookExecutionEngine, PlaybookExecutionEngine>();

        // IInsightsAi → InsightsOrchestrator — the only Zone-A surface Zone B code may
        // import per SPEC §3.5. Wraps IPlaybookExecutionEngine (above) + IOpenAiClient +
        // IInsightsPlaybookExecutionCache (D-P13) + IPlaybookOrchestrationService — all
        // compound-AI-ON dependencies. Previously registered UNCONDITIONALLY in
        // InsightsFacadeModule, which created the LATENT BUG #1 narrative: under compound-OFF,
        // DI resolution threw InvalidOperationException at endpoint-handler invocation
        // (500 instead of the contract-specified 503). Null peer (NullInsightsAi) is
        // registered in AddNullObjectsForCompoundOff. Scoped to match transitive lifetime.
        services.AddScoped<IInsightsAi, InsightsOrchestrator>();
    }

    private static void AddBuilderServices(IServiceCollection services)
    {
        services.AddScoped<IAiPlaybookBuilderService, AiPlaybookBuilderService>();
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Builder.BuilderToolExecutor>();
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Builder.IBuilderAgentService, Sprk.Bff.Api.Services.Ai.Builder.BuilderAgentService>();
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Builder.BuilderScopeImporter>();
        services.AddSingleton<IModelSelector, ModelSelector>();
        services.AddScoped<IEntityResolutionService, EntityResolutionService>();
    }

    private static void AddTestingServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Testing.IMockDataGenerator, Sprk.Bff.Api.Services.Ai.Testing.MockDataGenerator>();
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Testing.IMockTestExecutor, Sprk.Bff.Api.Services.Ai.Testing.MockTestExecutor>();

        var storageConnectionString = configuration.GetConnectionString("BlobStorage")
            ?? configuration["AzureStorage:ConnectionString"];
        if (!string.IsNullOrEmpty(storageConnectionString))
        {
            services.AddSingleton(sp => new Azure.Storage.Blobs.BlobServiceClient(storageConnectionString));
            services.AddSingleton<Sprk.Bff.Api.Services.Ai.Testing.ITempBlobStorageService, Sprk.Bff.Api.Services.Ai.Testing.TempBlobStorageService>();
            services.AddScoped<Sprk.Bff.Api.Services.Ai.Testing.IQuickTestExecutor, Sprk.Bff.Api.Services.Ai.Testing.QuickTestExecutor>();
        }

        services.AddScoped<Sprk.Bff.Api.Services.Ai.Testing.IProductionTestExecutor, Sprk.Bff.Api.Services.Ai.Testing.ProductionTestExecutor>();
    }

    private static void AddDeliveryServices(IServiceCollection services)
    {
        services.AddSingleton<ITemplateEngine, TemplateEngine>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Delivery.IWordTemplateService, Sprk.Bff.Api.Services.Ai.Delivery.WordTemplateService>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Delivery.IEmailTemplateService, Sprk.Bff.Api.Services.Ai.Delivery.EmailTemplateService>();
    }

    private static void AddNodeExecutors(IServiceCollection services)
    {
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.CreateTaskNodeExecutor>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.SendEmailNodeExecutor>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.UpdateRecordNodeExecutor>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.DeliverOutputNodeExecutor>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.DeliverToIndexNodeExecutor>();
        // FR-52 / Phase 5R Wave 5-C task 114R: composite delivery node executor.
        // ActionType.DeliverComposite (= 42) paired with NodeType.DeliverComposite (= 100_000_004).
        // Existing DeliverOutputNodeExecutor for ActionType.DeliverOutput is UNCHANGED
        // (backward-compat invariant — single-action Output Node behavior preserved).
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.DeliverCompositeNodeExecutor>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.ConditionNodeExecutor>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.AiAnalysisNodeExecutor>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.CreateNotificationNodeExecutor>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.QueryDataverseNodeExecutor>();

        // AgentServiceNodeExecutor — ActionType.AgentService = 60 (Phase 2, ADR-010, AIPU-061).
        // Requires AgentServiceClient singleton (AIPU-060). Kill switch: AgentService:Enabled.
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Foundry.AgentServiceClient>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.AgentServiceNodeExecutor>();

        // LookupUserMembershipNodeExecutor — ActionType.LookupUserMembership = 52 (R3 Part 1, FR-1B.1, task 041).
        // Singleton+Scoped DI pattern: injects IServiceScopeFactory to resolve the Scoped
        // IMembershipResolverService per execution. In-process call (NOT HTTP round-trip).
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.LookupUserMembershipNodeExecutor>();

        // CodeInterpreterBridge — thin wrapper around AgentServiceClient for Code Interpreter sandbox
        // invocations (AIPU-070). Singleton: stateless, thread-safe. Kill switch: CodeInterpreter:Enabled.
        // CodeInterpreterTools are NOT registered here — they are factory-instantiated by SprkChatAgentFactory
        // following the WebSearchTools pattern (ADR-010: no unnecessary DI registrations).
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Foundry.CodeInterpreterBridge>();

        // GroundingVerifier — D-P9 / D-47 / LAVERN ADR 10.6 platform primitive (Insights Engine Phase 1).
        // Mechanical zero-LLM citation verifier (substring + sliding-window, 10K-char DoS cap).
        // Singleton: stateless, thread-safe; shared across Insights synthesis (D-P14) and Action Engine consumers.
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.CitationVerification.IGroundingVerifier,
            Sprk.Bff.Api.Services.Ai.CitationVerification.GroundingVerifier>();

        // GroundingVerifyNode — D-P9 + D-P12 node executor (ActionType.GroundingVerify = 70).
        // Wraps IGroundingVerifier as INodeExecutor for the node-based playbook system.
        // Singleton matches the other INodeExecutor registrations above (executors are stateless).
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor,
            Sprk.Bff.Api.Services.Ai.Nodes.GroundingVerifyNode>();

        // D-P12 task 022 — Five new Insights-mode node executors (ActionType 80–120).
        // All five are stateless and follow the GroundingVerifyNode singleton pattern.
        // - LiveFactNode (80)           — wraps ILiveFactResolver; emits FactArtifact
        // - IndexRetrieveNode (90)      — config-driven AI Search query against spaarke-insights-index
        // - EvidenceSufficiencyNode (100) — deterministic rule evaluator (D-49 LAVERN Pattern #7)
        // - DeclineToFindNode (110)     — emits typed DeclineResponse (D-49)
        // - ReturnInsightArtifactNode (120) — final node; serializes envelope + D-A23/D-48 EvidenceGuard
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor,
            Sprk.Bff.Api.Services.Ai.Nodes.LiveFactNode>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor,
            Sprk.Bff.Api.Services.Ai.Nodes.IndexRetrieveNode>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor,
            Sprk.Bff.Api.Services.Ai.Nodes.EvidenceSufficiencyNode>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor,
            Sprk.Bff.Api.Services.Ai.Nodes.DeclineToFindNode>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor,
            Sprk.Bff.Api.Services.Ai.Nodes.ReturnInsightArtifactNode>();
    }

    /// <summary>
    /// Registers the D-P13 Insights playbook execution cache (SPEC §3.1) wrapping
    /// <see cref="IPlaybookExecutionEngine"/> calls in a Redis layer per ADR-009.
    /// </summary>
    /// <remarks>
    /// Two singletons:
    /// <list type="bullet">
    /// <item><see cref="InsightsCacheMetrics"/> — OpenTelemetry meter for cache hit/miss/eviction
    /// counters. Singleton because the underlying <see cref="System.Diagnostics.Metrics.Meter"/>
    /// is intended to be long-lived.</item>
    /// <item><see cref="IInsightsPlaybookExecutionCache"/> — stateless wrapper over
    /// <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/> (singleton)
    /// + <see cref="InsightsCacheMetrics"/> (singleton). Singleton is the correct lifetime
    /// per ADR-010 (no per-request state). The future <c>InsightsOrchestrator</c> facade
    /// (task 042 D-P9) will consume it.</item>
    /// </list>
    /// </remarks>
    private static void AddInsightsCache(IServiceCollection services)
    {
        services.AddSingleton<InsightsCacheMetrics>();

        // r1 Insights Widgets task 052 / FR-21: per-topic TTL plumbing. In-process mirror
        // of sprk_aitopicregistry rows that supplies sprk_cachettlminutes to the cache when
        // the per-call Ttl override is null. NOT a new interface seam (audit DR-002 / ADR-010);
        // registered as a singleton POCO alongside the cache so the Endpoint↔DI Symmetry rule
        // (audit DR-008) holds — both inside the compound-AI-ON gate that wraps AddInsightsCache.
        // Dependencies (IDataverseService, IOptionsMonitor<InsightsPlaybookNameMapOptions>) are
        // both Singleton; lifetime parity verified.
        services.AddSingleton<TopicRegistryTtlLookup>();
        services.AddSingleton<IInsightsPlaybookExecutionCache, InsightsPlaybookExecutionCache>();
    }

    /// <summary>
    /// Registers the Wave E2 Insights intent classifier (FR-05). Cheap LLM-based routing
    /// between the playbook synthesis path (<c>/api/insights/ask</c>) and the open-ended
    /// RAG retrieval path (<c>/api/insights/search</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Lifetime</b>: Singleton — the classifier holds no per-request state. Its
    /// dependencies are <see cref="IOpenAiClient"/> (Singleton),
    /// <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/> (Singleton), and
    /// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/> for
    /// <see cref="InsightsIntentClassifierOptions"/> (Singleton). No captive-dependency
    /// concerns.
    /// </para>
    /// <para>
    /// <b>Fine-grained kill-switch</b>: when <see cref="InsightsIntentClassifierOptions.Enabled"/>
    /// is false, the Null-Object is registered instead — same P3 Fail-fast semantics as the
    /// compound-AI-OFF path. This lets operators ship classifier code without enabling it.
    /// </para>
    /// <para>
    /// <b>ADR-032 §F.1</b>: the real classifier is registered here in the compound-AI-ON
    /// path; the Null-Object is registered in <see cref="AddNullObjectsForCompoundOff"/>.
    /// Wave E2 does NOT yet have an unconditionally-mapped consumer (the
    /// <c>/api/insights/ask</c> and <c>/api/insights/search</c> endpoints accept
    /// <c>forceMode</c> on their wire DTO for E3 forward-compat but do not invoke the
    /// classifier in E2). The asymmetric-registration anti-pattern is forward-mitigated by
    /// pre-registering the Null-Object so Wave E3 (Assistant integration) doesn't have to
    /// retrofit the DI layer.
    /// </para>
    /// </remarks>
    private static void AddInsightsIntentClassifier(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<InsightsIntentClassifierOptions>()
            .BindConfiguration(InsightsIntentClassifierOptions.SectionName);

        // Fine-grained opt-out independent of the compound AI gate. Bound directly from
        // configuration here (rather than via IOptions) because the registration choice is
        // made at startup — IOptions reload binding wouldn't switch the registered type.
        var classifierEnabled = configuration.GetValue<bool>(
            $"{InsightsIntentClassifierOptions.SectionName}:Enabled", defaultValue: true);

        if (classifierEnabled)
        {
            // Real classifier — LLM-backed, memory-cached. Singleton (matches IMemoryCache +
            // IOpenAiClient lifetimes; no per-request state).
            services.AddSingleton<IInsightsIntentClassifier, InsightsIntentClassifier>();
            Console.WriteLine("✓ Insights intent classifier: real LLM-backed impl");
        }
        else
        {
            // Operator opted out at fine grain. Register the same P3 Fail-fast Null-Object
            // used by the compound-AI-OFF branch so consumers see consistent behavior.
            services.AddSingleton<IInsightsIntentClassifier>(sp =>
                new NullInsightsIntentClassifier(sp.GetRequiredService<ILogger<InsightsIntentClassifier>>()));
            Console.WriteLine("⚠ Insights intent classifier: disabled at fine-grain (Insights:IntentClassifier:Enabled=false) — NullInsightsIntentClassifier registered");
        }
    }

    private static void AddRagServices(IServiceCollection services, IConfiguration configuration)
    {
        var docIntelOptions = configuration.GetSection(DocumentIntelligenceOptions.SectionName).Get<DocumentIntelligenceOptions>();
        if (!string.IsNullOrEmpty(docIntelOptions?.AiSearchEndpoint) && !string.IsNullOrEmpty(docIntelOptions?.AiSearchKey))
        {
            services.AddSingleton(sp =>
            {
                return new Azure.Search.Documents.Indexes.SearchIndexClient(
                    new Uri(docIntelOptions.AiSearchEndpoint),
                    new Azure.AzureKeyCredential(docIntelOptions.AiSearchKey));
            });

            services.AddSingleton<IKnowledgeDeploymentService, KnowledgeDeploymentService>();
            services.AddSingleton<IEmbeddingCache, EmbeddingCache>();
            services.AddSingleton<IRagService, RagService>();
            services.AddScoped<IFileIndexingService, FileIndexingService>();
            services.AddSingleton<Sprk.Bff.Api.Services.Ai.Visualization.IVisualizationService, Sprk.Bff.Api.Services.Ai.Visualization.VisualizationService>();
            Console.WriteLine("\u2713 RAG services enabled (hybrid search + embedding cache + visualization + file indexing)");
        }
        else
        {
            // B7 fallback \u2014 compound gate ON but AI Search keys missing. Register Null-Object
            // so IRagService consumers (RagEndpoints, KnowledgeBaseEndpoints TestSearch + delete)
            // can still resolve their DI graph. Endpoint catches convert FeatureDisabledException
            // to 503 ProblemDetails. Task 011 Phase 1b Tier 2, D-09 \u00a72 B7.
            services.AddSingleton<IRagService, NullRagService>();

            // Tier 1.5 round 4 (2026-06-01) \u2014 IVisualizationService + IFileIndexingService share
            // the same AI-Search-keys sub-gate as IRagService. Mirror the fallback registration
            // so the AI-Search-keys-missing branch also resolves these consumers' DI graph.
            services.AddSingleton<Sprk.Bff.Api.Services.Ai.Visualization.IVisualizationService, NullVisualizationService>();
            services.AddScoped<IFileIndexingService, NullFileIndexingService>();

            Console.WriteLine("\u26a0 RAG services disabled (requires DocumentIntelligence:AiSearchEndpoint/Key) \u2014 NullRagService + NullVisualizationService + NullFileIndexingService registered");
        }

        services.AddSingleton<ITextChunkingService, TextChunkingService>();
    }

    private static void AddToolFramework(IServiceCollection services, IConfiguration configuration)
    {
        var toolFrameworkOptions = configuration.GetSection(ToolFrameworkOptions.SectionName);
        if (toolFrameworkOptions.GetValue<bool>("Enabled", true))
        {
            services.AddToolFramework(configuration);
            Console.WriteLine("\u2713 Tool framework enabled");
        }
        else
        {
            services.Configure<ToolFrameworkOptions>(
                configuration.GetSection(ToolFrameworkOptions.SectionName));
            services.AddScoped<IToolHandlerRegistry, ToolHandlerRegistry>();
            Console.WriteLine("\u26a0 Tool framework disabled (ToolFramework:Enabled = false), but IToolHandlerRegistry registered for job handlers");
        }
    }

    private static void AddRecordMatchingServices(IServiceCollection services, IConfiguration configuration)
    {
        var recordMatchingEnabled = configuration.GetValue<bool>("DocumentIntelligence:RecordMatchingEnabled");
        if (recordMatchingEnabled)
        {
            services.AddHttpClient<Sprk.Bff.Api.Services.RecordMatching.DataverseIndexSyncService>();
            services.AddSingleton<Sprk.Bff.Api.Services.RecordMatching.IDataverseIndexSyncService>(sp =>
                sp.GetRequiredService<Sprk.Bff.Api.Services.RecordMatching.DataverseIndexSyncService>());
            services.AddSingleton<Sprk.Bff.Api.Services.RecordMatching.IRecordMatchService,
                Sprk.Bff.Api.Services.RecordMatching.RecordMatchService>();
            Console.WriteLine("\u2713 Record Matching services enabled (index: {0})", configuration["DocumentIntelligence:AiSearchIndexName"] ?? "spaarke-records-index");
        }
        else
        {
            Console.WriteLine("\u26a0 Record Matching services disabled (DocumentIntelligence:RecordMatchingEnabled = false)");
        }
    }
}
