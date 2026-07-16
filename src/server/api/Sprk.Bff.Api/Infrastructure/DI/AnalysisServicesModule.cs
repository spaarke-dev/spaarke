using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Insights;
using Sprk.Bff.Api.Services.Ai.Insights.Routing;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
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
        // FR-P3-05 (task 044): the R5 Summarize telemetry singleton was deleted with its last
        // emitters (the engine shell's chat-summarize path + the summarize orchestrator shell).
        // The dispatch-seam path carries its own loop/dispatch telemetry.

        // R6 Pillar 6c (FR-37 / task 063) — IContextEventEmitter for context.* execution-trace
        // events (tool_call_started/completed, knowledge_retrieved, playbook_node_executing/completed,
        // decision_made). Registered unconditionally at the top of the module (unconditional singleton)
        // so emission sites in PlaybookOrchestrationService / ToolHandlerToAIFunctionAdapter
        // can resolve it regardless of feature flags. ADR-015 binding: the implementation is structurally
        // constrained to deterministic IDs only — see ContextEventEmitter.cs class header.
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Telemetry.IContextEventEmitter,
            Sprk.Bff.Api.Services.Ai.Telemetry.ContextEventEmitter>();

        // R6 DEF-001 / task 095 Phase 3 — IContextSseRelay is the per-request scoped bridge
        // from the singleton ContextEventEmitter to the chat SSE stream. ChatEndpoints.SendMessageAsync
        // assigns the relay's Writer at SSE stream start (writes "context_event" frames) and clears
        // it in finally. The singleton emitter resolves this scoped relay via IHttpContextAccessor
        // on each emission. Unconditional registration mirrors IContextEventEmitter above — outside
        // an active HTTP context, Writer is null and emissions are silent no-ops. ADR-015 / ADR-030 /
        // ADR-033 inherited via ContextSseEventDto / ContextSseRelay headers.
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Telemetry.IContextSseRelay,
            Sprk.Bff.Api.Services.Ai.Telemetry.ContextSseRelay>();

        // Insights Engine Widgets r1 telemetry (project ai-spaarke-insights-engine-widgets-r1 task 050).
        // Meter "Sprk.Bff.Api.InsightWidgets" per Q-U8 evidence resolution (matches all 9 existing BFF
        // meter `Sprk.Bff.Api.<Feature>` convention). Unconditional registration mirrors the telemetry-singleton
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

        // FR-P1-03 (ai-architecture-redesign-r1 task 022) — Event-path BOUNDS infrastructure,
        // TRULY UNCONDITIONAL. These three registrations have NO AI dependencies:
        //  - EventRulesOptions: platform-setting bounds (daily cap, M4 threshold, opt-out TTL).
        //    Bounds are policy, NOT routing — event routing lives exclusively in
        //    sprk_playbookconsumer.sprk_oneventbindings (ADR-039).
        //  - IEventPathUserState: per-user budget counter + opt-out marker over ITenantCache
        //    (unconditional via CacheModule). Consumed by the UNCONDITIONALLY-mapped
        //    GET/PUT /api/ai/chat/event-rules/opt-out routes → must resolve on the AI-OFF
        //    path too (§F.1 asymmetric-registration rule).
        //  - EventRulesTelemetry: NFR-09 "enforced AND telemetered" meter (pattern:
        //    telemetry singletons — unconditional).
        services.Configure<Sprk.Bff.Api.Services.Ai.EventRules.EventRulesOptions>(
            configuration.GetSection(Sprk.Bff.Api.Services.Ai.EventRules.EventRulesOptions.SectionName));
        services.AddScoped<Sprk.Bff.Api.Services.Ai.EventRules.IEventPathUserState,
                           Sprk.Bff.Api.Services.Ai.EventRules.EventPathUserState>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.EventRules.EventRulesTelemetry>();

        // AiTelemetry — TRULY UNCONDITIONAL (moved out of the DocumentIntelligence gate by
        // spaarke-ai-architecture-redesign-r1 task 054, FR-P4-05). It is a pure Meter wrapper
        // with zero AI dependencies, consumed by unconditionally-mapped chat endpoints for the
        // per-tenant metering counters — the §F.1 asymmetric-registration rule requires the
        // unconditional registration (pattern precedent: EventRulesTelemetry above).
        services.AddSingleton<Sprk.Bff.Api.Telemetry.AiTelemetry>();

        var documentIntelligenceEnabled = configuration.GetValue<bool>("DocumentIntelligence:Enabled");
        if (documentIntelligenceEnabled)
        {
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

            // ai-architecture-redesign-r1 task 006 (FR-P0-05, 2026-07-05) — Linear AI
            // Consumer library (R7 Wave 12) moved from unconditional Program.cs registration
            // to INSIDE this compound AI gate so the whole prompted-executor stack
            // (ActionResolver + DocumentTextSource + SessionFileTextSource + ActionRunner +
            // the endpoint-absorbed consumer pipelines, task 044) toggles as one unit. Every
            // primitive's ctor graph is compound-ON-only anyway (IOpenAiClient,
            // PromptSchemaRenderer, IScopeResolverService, AnalysisDocumentLoader,
            // ITextExtractor, IRagService).
            //
            // §F.1 asymmetric-registration audit (static scan run 2026-07-05):
            //  - IActionResolver/IActionRunner — consumed by WorkspaceFileEndpoints.HandleSummarize
            //    (MapWorkspaceFileEndpoints is UNCONDITIONAL) → Null executor-primitive peers
            //    registered in AddNullObjectsForCompoundOff (P3 subclass).
            //  - the document-profile pipeline — consumed only by AnalysisEndpoints
            //    (MapAnalysisEndpoints is INSIDE the same compound gate) → symmetric; no peer.
            //  - IActionResolver / IActionRunner — consumed by Matter/ProjectPreFillService as
            //    OPTIONAL nullable ctor params (= null default) → ADR-032 "optional-via-null-
            //    tolerance" exemption; no peer.
            //  - IDocumentTextSource / ISessionFileTextSource — transitively conditional
            //    (consumed only by gated LinearConsumers services + compound-ON
            //    the dispatch seam) → no peer.
            services.AddLinearConsumers();
            Console.WriteLine("✓ Linear AI Consumer library enabled (gated under compound AI gate — FR-P0-05)");

            // AddBuilderServices + AddTestingServices removed 2026-07-07 (spaarke-ai-architecture-redesign-r1
            // task 050, FR-P4-04 server leg): the AiPlaybookBuilder canvas/graph estate was deleted after
            // task 053 removed its sole client caller. IModelSelector survives the estate (registered below)
            // because ModelSelectorOptions remains a live config surface (ClauseAnalyzerHandler).
            services.AddSingleton<IModelSelector, ModelSelector>();
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

        // FR-P0-06 (ai-architecture-redesign-r1 task 007) \u2014 ICodedWorkflow assembly-scan
        // discovery + class-ref registry (mirrors AddToolFramework's handler scan, E-1).
        // MUST run after the gate branches above: the scan's factory bindings defer to the
        // concrete-type registrations those branches made (real narrator/collector when the
        // compound AI gate is ON; ADR-032 Null-Object peers when OFF), so kill-switch
        // behavior flows through class-ref resolution unchanged.
        services.AddCodedWorkflows();

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
        // (deps: IGenericEntityService, ILogger — all unconditional).
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
            cache: sp.GetRequiredService<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>(),
            dataverseRepository: sp.GetRequiredService<IChatDataverseRepository>(),
            logger: sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ChatSessionManager>>(),
            persistence: sp.GetService<Sprk.Bff.Api.Services.Ai.Sessions.ISessionPersistenceService>(),
            cleanupSignal: sp.GetService<Sprk.Bff.Api.Services.Ai.Chat.ISessionFilesCleanupSignal>()));

        // B5 — ChatHistoryManager (deps: ChatSessionManager + IChatDataverseRepository + ILogger — all unconditional).
        services.AddScoped<ChatHistoryManager>();

        // AIR2-038 / FR-A1-09 — ISessionTraceReader (decision-traceability read facade, PublicContracts).
        // Registered UNCONDITIONALLY alongside ChatSessionManager: the GET /trace endpoint is mapped
        // unconditionally (EndpointMappingExtensions.MapChatEndpoints, line ~169) and injects
        // ISessionTraceReader, and its only dependency (ChatSessionManager) is unconditional here — so
        // symmetric registration holds (§F.1 asymmetric-registration rule; no ADR-032 kill-switch needed).
        // Read-only projection over the ADR-040 ledger — no new store (ADR-040), facade boundary (ADR-013).
        services.AddScoped<Sprk.Bff.Api.Services.Ai.PublicContracts.ISessionTraceReader,
            Sprk.Bff.Api.Services.Ai.PublicContracts.SessionTraceReader>();

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
        // pattern as ChatContextMappingService + DocxExportService. Promoted under D-02 cluster
        // exception. ADR-010.
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

        // ── ai-architecture-redesign-r1 task 006 (FR-P0-05, 2026-07-05) ──────────────────
        // Null peers for the two playbook services moved out of FinanceModule into
        // AddPlaybookServices (compound-ON), plus the LinearConsumers stack moved under the
        // compound gate. See the §F.1 audit note beside services.AddLinearConsumers(...).

        // IPlaybookLookupService (P3 Fail-Fast on GetByIdAsync; quiet no-op cache-clears).
        // Real impl registered in AddPlaybookServices. Unconditional consumers:
        // ChatEndpoints.ExecutePlaybookAsync, WorkspaceFileEndpoints.HandleSummarize,
        // WorkspaceAiService + Matter/ProjectPreFillService, InvoiceExtractionJobHandler.
        services.AddScoped<IPlaybookLookupService, NullPlaybookLookupService>();

        // IOutputOrchestratorService (P3 Fail-Fast). Real impl registered in
        // AddPlaybookServices. Keeps unconditional InvoiceExtractionJobHandler (IJobHandler
        // enumeration) resolvable under compound-OFF; dequeue fails fast per ADR-018.
        services.AddScoped<IOutputOrchestratorService, NullOutputOrchestratorService>();

        // IActionResolver + IActionRunner (P3 Fail-Fast peers — FR-P3-05 task 044 wrapper
        // absorption). WorkspaceFileEndpoints.HandleSummarize injects the executor
        // primitives directly and MapWorkspaceFileEndpoints is UNCONDITIONAL — without
        // these peers, minimal-API parameter inference aborts host startup when the
        // compound AI gate is OFF. The endpoint's catch (FeatureDisabledException) emits
        // the SSE error chunk / 503 pattern.
        services.AddSingleton<IActionResolver, NullActionResolver>();
        services.AddSingleton<IActionRunner, NullActionRunner>();

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
        // SendMessageAsync catches and emits SSE error chunks per ADR-018.
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

        // SessionDispatchOrchestrator (P3 Fail-Fast subclass) — FR-P1-04, ai-architecture-
        // redesign-r1 task 023b. The Click dispatch endpoint (POST /api/ai/chat/sessions/
        // {sessionId}/dispatch) AND the direct Summarize endpoint (POST /api/ai/chat/
        // sessions/{sessionId}/summarize — converged onto the ONE dispatch seam by
        // FR-P3-05 task 044) are mapped UNCONDITIONALLY in EndpointMappingExtensions and
        // inject the concrete SessionDispatchOrchestrator. Real impl registered scoped in
        // AddAnalysisOrchestrationServices (compound-ON only — its graph needs IActionRunner
        // + IScopeResolverService + ISessionFileTextSource + IOutputRouter). This Null peer
        // throws FeatureDisabledException (ai.dispatch.disabled) at first MoveNextAsync();
        // the endpoints' pre-stream probes map it to the canonical 503 (ADR-018 + ADR-019).
        // Canonical pattern siblings: NullSprkChatAgentFactory (B2), NullPendingPlanManager (B3).
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Chat.SessionDispatchOrchestrator>(sp =>
            new Sprk.Bff.Api.Services.Ai.Chat.NullSessionDispatchOrchestrator(
                sp.GetRequiredService<ILogger<Sprk.Bff.Api.Services.Ai.Chat.SessionDispatchOrchestrator>>()));

        // R7 Wave 12 post-T135 CI fix (2026-06-30) — DailyBriefingNarrator + DailyBriefingCollector
        // P3 Fail-Fast Null-Objects. Both /api/ai/daily-briefing/render and
        // /api/ai/daily-briefing/narrate are mapped UNCONDITIONALLY by EndpointMappingExtensions,
        // and their handlers inject the concrete narrator/collector. The real registrations live
        // inside AddAnalysisOrchestrationServices (compound-ON only — they depend on
        // AnalysisActionService typed HttpClient + IOpenAiClient + IEntityNameScrubber, all
        // compound-gated). Without these Null mirrors, minimal-API parameter inference fails at
        // host startup with "Failure to infer one or more parameters" (observed in PR #520 CI run
        // 28482755126 — failing parameter: `narrator`). The Null subclasses throw
        // FeatureDisabledException on first call; both consuming endpoints have generic
        // try/catch wrappers that surface a 500 ProblemDetails. ADR-032 §F.1 / CLAUDE.md §10 F.1.
        // Canonical pattern sibling: NullSessionDispatchOrchestrator (above).
        // Also registers IEntityNameScrubber as a Null peer — pure algorithm, no AI deps, but
        // it lives in the same Narrators namespace and the real registration sits inside the
        // gate (line 516). Registering the real EntityNameScrubber here as the "Null" path is
        // safe (it has no AI deps) and keeps the Narrators namespace contract uniform.
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Narrators.IEntityNameScrubber,
                              Sprk.Bff.Api.Services.Ai.Narrators.EntityNameScrubber>();
        services.AddTransient<Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingNarrator>(sp =>
            new Sprk.Bff.Api.Services.Ai.Narrators.NullDailyBriefingNarrator(
                sp.GetRequiredService<ILogger<Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingNarrator>>()));
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingCollector>(sp =>
            new Sprk.Bff.Api.Services.Ai.Narrators.NullDailyBriefingCollector(
                sp.GetRequiredService<ILogger<Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingCollector>>()));

        // DailyBriefingCompositeService Null peer (FR-P3-04, task 043) — the /render,
        // /narrate, /email endpoints map unconditionally and inject the concrete composite;
        // this mirror keeps minimal-API parameter inference valid on the compound-OFF path
        // and throws FeatureDisabledException (ai.briefing.disabled) on first call, which
        // the endpoints map to the canonical 503. Canonical pattern siblings: the narrator
        // + collector Null peers directly above.
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingCompositeService>(sp =>
            new Sprk.Bff.Api.Services.Ai.Narrators.NullDailyBriefingCompositeService(
                sp.GetRequiredService<ILogger<Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingCompositeService>>()));

        // IEventRulesService (P3 Fail-Fast) — FR-P1-03, ai-architecture-redesign-r1 task 022.
        // The document_uploaded event route (POST /api/ai/chat/sessions/{id}/events/
        // document-uploaded) is mapped UNCONDITIONALLY in ChatDocumentEndpoints and injects
        // IEventRulesService. Real impl registered in AddAnalysisOrchestrationServices
        // (compound-ON only — its graph needs IActionRunner + IScopeResolverService +
        // ISessionFileTextSource + IOutputRouter). This Null peer throws
        // FeatureDisabledException on first MoveNextAsync(); the endpoint's pre-stream probe
        // maps it to the canonical 503 ProblemDetails (ADR-018 + ADR-019).
        // Canonical pattern siblings: NullSessionDispatchOrchestrator (above).
        services.AddScoped<Sprk.Bff.Api.Services.Ai.EventRules.IEventRulesService,
                           Sprk.Bff.Api.Services.Ai.EventRules.NullEventRulesService>();
    }

    private static void AddAnalysisOrchestrationServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AnalysisOptions>(configuration.GetSection(AnalysisOptions.SectionName));
        // FR-P2-01 (spaarke-ai-architecture-redesign-r1 task 030): agent-turn loop
        // contract settings — per-turn tool-call budget (default 8; NFR-09/ADR-016
        // tunable platform setting). Options-only Configure — no service registration,
        // no ADR-032 Null peer needed (consumed via GetService<IOptions<...>> with an
        // in-place default when unbound).
        services.Configure<Sprk.Bff.Api.Services.Ai.Chat.AgentTurnOptions>(
            configuration.GetSection(Sprk.Bff.Api.Services.Ai.Chat.AgentTurnOptions.SectionName));
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

        // spaarkeai-compose-r2 (UAT #7b): OBO-capable document-profile facade. ComposeService
        // (CRUD) injects ONLY this PublicContracts facade (ADR-013). The impl invokes the
        // "Document Profiler" Action (ACT-011) directly on the ADR-043 completion-engine spine —
        // IActionResolver (document-profile Binding) → IDocumentTextSource (OBO download) →
        // IActionRunner (ActionRunner) → DocumentProfileOutputMapper → UpdateDocumentFieldsAsync —
        // NOT the retired playbook/node engine. Registered here inside the compound AI gate
        // alongside AddLinearConsumers, so its (optional) AI execution seams resolve to real impls;
        // the seams are nullable ctor params (ADR-032 optional-via-null-tolerance) so a compound-OFF
        // host resolves ComposeService's optional IDocumentProfileAi to null and skips profiling
        // cleanly. Scoped: runs in the OBO request scope. CANONICAL facade (not a stand-in) —
        // resolved 2026-07-15 (#615 / PE-D4); redesign-r2 adopts this as-is if it resumes. See ADR
        // Tensions in projects/spaarkeai-compose-r2/design.md.
        services.AddScoped<Sprk.Bff.Api.Services.Ai.PublicContracts.IDocumentProfileAi,
                           Sprk.Bff.Api.Services.Ai.PublicContracts.DocumentProfileAi>();

        // DailyBriefingNarrator — the platform's first `coded` composite workflow
        // (FR-P3-04, ai-architecture-redesign-r1 task 043; ICodedWorkflow retrofit in
        // task 007). Dispatched exclusively by class reference from its Action row
        // (sprk_workflowclass) via DailyBriefingCompositeService below — the R7 spike
        // flag + playbook-engine fallback were DELETED (NFR-08 hard cutover).
        //
        // Lifetime: Transient — depends on AnalysisActionService (typed HttpClient = Transient)
        // and IOpenAiClient (Singleton) and IEntityNameScrubber (Singleton). Transient is the
        // safe choice given the HttpClient dependency.
        //
        // Scrubber: Singleton — pure algorithm, no state, no per-request data.
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Narrators.IEntityNameScrubber,
                              Sprk.Bff.Api.Services.Ai.Narrators.EntityNameScrubber>();
        services.AddTransient<Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingNarrator>();

        // R7 Wave 11 T118 (2026-06-30) — DailyBriefingCollector: live-query collector that
        // backs the new POST /api/ai/daily-briefing/render endpoint. Bypasses appnotification
        // entirely; runs FetchXML directly via IGenericEntityService (Scoped — matches lifetime).
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingCollector>();

        // DailyBriefingCompositeService — the briefing's coded-composite dispatch boundary
        // (FR-P3-04, task 043). Resolves the Binding (ADR-039 single routing surface),
        // executes the coded workflow via ICodedWorkflowRegistry, writes the session-ledger
        // entries BEFORE rendering/emailing via IOutputRouter (ADR-040), routes by the
        // Binding's disposition. Concrete class (ADR-010); Scoped — wraps IOutputRouter +
        // ICodedWorkflowRegistry + DailyBriefingCollector (all Scoped).
        //
        // §F.1 asymmetric-registration audit: the /render, /narrate, /email endpoints map
        // unconditionally and inject the concrete type; the compound-OFF branch registers
        // the NullDailyBriefingCompositeService mirror in AddNullObjectsForCompoundOff.
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingCompositeService>();
        Console.WriteLine("✓ DailyBriefingCompositeService registered (FR-P3-04 first coded composite; Binding-decided dispatch; ledger-before-render)");

        // IEmailDispositionSender — the OutputRouter email-disposition delivery seam
        // (FR-P3-04, task 043). Delegates to the Communication (Email) service
        // (unconditional singleton) under SendMode.User OBO. Scoped to match its consumer
        // (OutputRouter). See IEmailDispositionSender remarks for the ADR-010/§11
        // interface justification.
        services.AddScoped<Sprk.Bff.Api.Services.Ai.IEmailDispositionSender,
                           Sprk.Bff.Api.Services.Ai.CommunicationEmailDispositionSender>();
        Console.WriteLine("✓ EmailDispositionSender registered (FR-P3-04 email disposition leg → Communication service)");

        // IWorkProductRecordPersister — the OutputRouter work_product-disposition
        // persistence seam (FR-P3-08, task 047; same optional-dependency shape as the
        // email sender above). Generalizes the widgets-r1 topic-registry pattern: resolves
        // the capability's sprk_aitopicregistry target mapping (topic = the Binding's
        // capability code) and PATCHes the stored ledger envelope onto the session's host
        // record under user-OBO (IDataverseUserClient — unconditional typed HttpClient,
        // fail-closed). Scoped to match its consumer (OutputRouter). See
        // IWorkProductRecordPersister remarks for the ADR-010/§11 interface justification.
        services.AddScoped<Sprk.Bff.Api.Services.Ai.IWorkProductRecordPersister,
                           Sprk.Bff.Api.Services.Ai.TopicRegistryWorkProductPersister>();
        Console.WriteLine("✓ WorkProductRecordPersister registered (FR-P3-08 work_product disposition leg → host-record persistence)");

        // SessionDispatchOrchestrator — THE dispatch seam (FR-P1-04 task 023b; FR-P3-05
        // task 044 convergence). Resolves a Binding row BY ID (chips carry binding_id —
        // ADR-039 D4: the id IS the routing decision) via
        // IConsumerRoutingService.GetBindingByIdAsync and executes the catalog stack
        // (prompted executor → IOutputRouter ledger-before-render). Callers: the loop's
        // BindingCapabilityTool, chip clicks (POST /sessions/{id}/dispatch), gate-resolve,
        // and the direct Summarize endpoint (POST /sessions/{id}/summarize — the former
        // summarize-named orchestrator shell was deleted per NFR-08). Concrete class
        // (ADR-010); Scoped to match the lifetime of its dependencies (ChatSessionManager +
        // IScopeResolverService + ISessionFileTextSource + IConsumerRoutingService are
        // Scoped; IActionRunner is Singleton — Scoped respects every wrapped lifetime).
        //
        // §F.1 asymmetric-registration audit: registration is unconditional within the
        // already-gated outer block; both endpoints map unconditionally with
        // the NullSessionDispatchOrchestrator mirror in AddNullObjectsForCompoundOff.
        // IContextBinder — THE platform input-resolution seam (ADR-043 Move 1 / E-10; re-scopes
        // task 053). Resolves an Action's declared inputs into grounding context (ContextEnvelope) +
        // the typed operand (## Input / ## Document), and writes the ContextEnvelope fingerprint via
        // ChatSessionManager.AppendContextFingerprintAsync (task-038's dark seam, now live).
        // Scoped: wraps ChatSessionManager (Scoped) — matches the SessionDispatchOrchestrator that
        // consumes it. §F.1 asymmetric-registration audit: consumed ONLY by the concrete
        // SessionDispatchOrchestrator ctor (registered in THIS compound-ON block); the compound-OFF
        // path resolves NullSessionDispatchOrchestrator (logger-only ctor) which never touches this
        // seam → transitively conditional; no ADR-032 Null peer needed.
        // IOrganizationalContextProvider — read-only INBOUND Organizational-scope provider seam (task 060,
        // FR-B-11). Spaarke receives organizational context through this interface; NO outbound/push
        // path — not an MCP-server surface. r2 ships ONLY the ADR-032 P2 quiet no-op Null-Object (no real
        // provider — the Work IQ runtime integration + researcher spike are deferred per owner ruling
        // 2026-07-08). §F.1 asymmetric-registration audit: the sole consumer is ContextBinder (registered
        // in THIS compound-ON block, immediately below); the compound-OFF path resolves
        // NullSessionDispatchOrchestrator, which never touches ContextBinder, so this seam is never
        // resolved in the OFF path either — transitively conditional, same rationale as IContextBinder.
        // ContextBinder also self-defaults to NullOrganizationalContextProvider internally, so omitting
        // this registration would still be safe — it is registered anyway so a future Work IQ provider is
        // a one-line DI swap without reopening ContextBinder.
        services.AddScoped<Sprk.Bff.Api.Services.Ai.PublicContracts.IOrganizationalContextProvider,
                           Sprk.Bff.Api.Services.Ai.PublicContracts.NullOrganizationalContextProvider>();
        Console.WriteLine("✓ IOrganizationalContextProvider registered (task 060 FR-B-11; Null-Object default — Work IQ provider deferred)");

        // ICallerContactResolver — deterministic claims→Dataverse-contact resolver (task 055, FR-B-06).
        // Maps the caller's AAD oid claim to a Dataverse contact via the
        // contact.azureactivedirectoryobjectid cross-reference (ADR-028) so "assign it to me" resolves
        // server-side, never a model guess. Scoped: wraps IDataverseService (Singleton) and is consumed
        // by ContextBinder (Scoped, registered immediately below). §F.1 asymmetric-registration audit:
        // sole consumer is ContextBinder in THIS compound-ON block; the compound-OFF path's
        // NullSessionDispatchOrchestrator never touches ContextBinder, so this seam is never resolved
        // OFF-path either — transitively conditional, same rationale as IContextBinder.
        // ContextBinder also self-defaults to NullCallerContactResolver internally, so omitting this
        // registration would still be safe (honest no-contact result rather than a null-reference failure).
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Context.ICallerContactResolver,
                           Sprk.Bff.Api.Services.Ai.Context.CallerContactResolver>();
        Console.WriteLine("✓ ICallerContactResolver registered (task 055 FR-B-06; claims→contact, feeds ContextEnvelope User slice)");

        // ICallerSystemUserResolver — deterministic claims→Dataverse-systemuser resolver (F-2 user-memory
        // recall). Keys the User slice's user-memory RECALL fragment (systemuserid, operator ruling
        // 2026-07-09 (c)). Mirrors ICallerContactResolver exactly (same IDataverseService facade, same
        // one-hop oid cross-reference) and is consumed ONLY by ContextBinder in THIS compound-ON block —
        // same transitively-conditional rationale as ICallerContactResolver; ContextBinder also
        // self-defaults to NullCallerSystemUserResolver internally, so omitting it stays safe.
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Context.ICallerSystemUserResolver,
                           Sprk.Bff.Api.Services.Ai.Context.CallerSystemUserResolver>();
        Console.WriteLine("✓ ICallerSystemUserResolver registered (F-2 user-memory recall; claims→systemuser, keys the User slice recall fragment)");

        // IStatedProfileReader — User-scope STATED-profile reader (task 030, FR-E2). Reads the caller's typed
        // sprk_userprofile row (keyed by the resolved systemuserid) + its N:N sprk_practicearea_ref names;
        // ContextBinder renders it and folds the block into the User slice's userFragment AHEAD of the
        // memory-recall block. Mirrors ICallerSystemUserResolver exactly (same IDataverseService facade, same
        // one-hop read, same soft-fail posture) and is consumed ONLY by ContextBinder in THIS compound-ON
        // block — same transitively-conditional rationale as ICallerSystemUserResolver; ContextBinder also
        // self-defaults to NullStatedProfileReader internally (ADR-032 P2 quiet no-op), so omitting it stays
        // safe. ADR-042: STATED typed profile, NOT a memory store. ADR-039: preference-only, never grounding.
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Context.IStatedProfileReader,
                           Sprk.Bff.Api.Services.Ai.Context.StatedProfileReader>();
        Console.WriteLine("✓ IStatedProfileReader registered (task 030 FR-E2; stated sprk_userprofile → User slice userFragment, ahead of memory recall)");

        services.AddScoped<Sprk.Bff.Api.Services.Ai.Context.IContextBinder,
                           Sprk.Bff.Api.Services.Ai.Context.ContextBinder>();
        Console.WriteLine("✓ ContextBinder registered (ADR-043 E-10 input-resolution seam; ContextEnvelope + operand; task-038 fingerprint writer)");

        services.AddScoped<Sprk.Bff.Api.Services.Ai.Chat.SessionDispatchOrchestrator>();
        Console.WriteLine("✓ SessionDispatchOrchestrator registered (FR-P1-04 Click path; binding-id catalog dispatch; ADR-040 ledger-before-render)");

        // IOutputRouter — the universal ledger write-before-render seam (ADR-040 / FR-P1-02,
        // ai-architecture-redesign-r1 task 021). Every capability execution writes an
        // addressable SessionOutput ({bindingId}@t{n}) through this seam BEFORE rendering,
        // then routing follows the Binding's declared disposition (informational live at P1;
        // email live since task 043; work_product live since task 047; overlay/record/
        // notification remain loud NotSupported stubs — no silent fallback).
        // Scoped: wraps ChatSessionManager (Scoped).
        //
        // §F.1 asymmetric-registration audit (static scan 2026-07-05; refreshed by task 044
        // 2026-07-06): IOutputRouter is consumed by the concrete
        // SessionDispatchOrchestrator, EventRulesService, EngineOutputLedgerAdapter, and
        // DailyBriefingCompositeService ctors — ALL registered in this same compound-ON block.
        // No endpoint handler injects IOutputRouter directly
        // (rg "[\s,(]IOutputRouter\s" src/server/api/Sprk.Bff.Api/Api/ → zero hits), and the
        // compound-OFF path resolves the logger-only Null peers of every consumer, which never
        // touch this seam → transitively conditional; no ADR-032 Null peer needed.
        services.AddScoped<Sprk.Bff.Api.Services.Ai.IOutputRouter,
                           Sprk.Bff.Api.Services.Ai.OutputRouter>();
        Console.WriteLine("✓ OutputRouter registered (FR-P1-02 universal ledger write-before-render; disposition routing per ADR-040)");

        // IEngineOutputLedgerAdapter — the E-2 engine-output→ledger adapter (ADR-040 /
        // FR-P1-05, ai-architecture-redesign-r1 task 024; re-homed by FR-P3-05 task 044).
        // Converts the FROZEN engine's chat-invoked composite outputs (the analysis.rerun
        // typed-handler leg) into addressable SessionOutput ledger entries via the
        // IOutputRouter write path. Boundary shim on OUR side of the ADR-039 freeze line —
        // zero changes inside PlaybookOrchestrationService/nodes; attaches in
        // AnalysisExecutionHandler (the sole surviving chat-session-attached engine leg
        // after the task-044 F-1 deletions). FR-P3-08 (task 047) landed record persistence
        // as the OutputRouter work_product leg — session-scoped by design; session-less
        // engine runs keep their playbook-node persistence (widgets-r1 pattern).
        // Scoped: wraps ChatSessionManager (Scoped) + IOutputRouter (Scoped).
        //
        // §F.1 asymmetric-registration audit (task 024; refreshed task 044): consumed ONLY
        // by the ctor of AnalysisExecutionHandler, which is auto-discovered by
        // AddToolFramework — invoked INSIDE this same compound-ON block. Compound-OFF
        // constructs no tool handlers (AddToolFramework never runs), so the registration is
        // transitively conditional exactly like IOutputRouter above; no ADR-032 Null peer needed.
        services.AddScoped<Sprk.Bff.Api.Services.Ai.IEngineOutputLedgerAdapter,
                           Sprk.Bff.Api.Services.Ai.EngineOutputLedgerAdapter>();
        Console.WriteLine("✓ EngineOutputLedgerAdapter registered (FR-P1-05 E-2 frozen-engine composite outputs → session ledger)");

        // IEventRulesService — the THIN Event entry path (ADR-039 path 1 of 3; FR-P1-03,
        // ai-architecture-redesign-r1 task 022). Resolves document_uploaded to the ordered
        // members declared in sprk_playbookconsumer.sprk_oneventbindings and executes them
        // through IActionRunner → IOutputRouter under the FR-P1-03 bounds (daily cap,
        // opt-out, bulk top-1, explicit-command supersede, empty-attachments precondition).
        // Scoped: wraps ChatSessionManager + IScopeResolverService + ISessionFileTextSource
        // + IOutputRouter (all Scoped).
        //
        // §F.1 asymmetric-registration audit (task 022): the document_uploaded event route
        // is mapped UNCONDITIONALLY in ChatDocumentEndpoints and injects IEventRulesService
        // → NullEventRulesService peer registered in AddNullObjectsForCompoundOff (throws
        // FeatureDisabledException on first MoveNextAsync; endpoint probe maps it to 503).
        services.AddScoped<Sprk.Bff.Api.Services.Ai.EventRules.IEventRulesService,
                           Sprk.Bff.Api.Services.Ai.EventRules.EventRulesService>();
        Console.WriteLine("✓ EventRulesService registered (FR-P1-03 Event path; document_uploaded → ordered Binding members with bounds)");

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
        // the generic playbook dispatcher (deleted by task 044). The chat-side path no longer
        // requires a typed HttpClient — the InsightsIntentClassifier playbook-vs-RAG
        // routing happens inside the orchestration layer the (since-deleted) generic facade
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

        // ai-architecture-redesign-r1 task 006 (FR-P0-05, 2026-07-05) — moved OUT of
        // FinanceModule (misplaced-registration debt from earlier Finance accretion).
        //
        // IPlaybookLookupService: cached stable-ID alt-key (sprk_playbookid) playbook lookups
        // for SaaS multi-environment portability (IMemoryCache, 1h TTL). Playbook-domain
        // service — belongs beside IPlaybookService. Null peer (P3 Fail-Fast) registered in
        // AddNullObjectsForCompoundOff because its consumers include UNCONDITIONAL surfaces:
        // ChatEndpoints.ExecutePlaybookAsync, WorkspaceFileEndpoints.HandleSummarize,
        // WorkspaceAiService / Matter/ProjectPreFillService (WorkspaceModule, unconditional),
        // and InvoiceExtractionJobHandler (FinanceModule IJobHandler, unconditional).
        services.AddScoped<IPlaybookLookupService, PlaybookLookupService>();

        // IOutputOrchestratorService: applies playbook outputMapping field updates to
        // Dataverse (delegates to IDataverseUpdateHandler, which stays unconditional in
        // FinanceModule). Sole consumer today is InvoiceExtractionJobHandler (unconditional
        // IJobHandler) → Null peer (P3 Fail-Fast) in AddNullObjectsForCompoundOff keeps
        // IJobHandler enumeration resolvable under compound-OFF; on dequeue the job fails
        // fast with FeatureDisabledException and Service Bus retry/DLQ handles it (ADR-018).
        services.AddScoped<IOutputOrchestratorService, OutputOrchestratorService>();
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

        // FR-P3-05 (spaarke-ai-architecture-redesign-r1 task 044): the generic playbook-
        // invocation facade triangle and the engine shell that backed the loop's playbook
        // dispatch were DELETED with their sole consumer (the app-only legacy tool-handler
        // leg closed per audit F-1). Insights synthesis invokes the frozen
        // IPlaybookOrchestrationService directly (see InsightsOrchestrator).

        // IInsightsAi → InsightsOrchestrator — the only Zone-A surface Zone B code may
        // import per SPEC §3.5. Wraps IOpenAiClient +
        // IInsightsPlaybookExecutionCache (D-P13) + IPlaybookOrchestrationService — all
        // compound-AI-ON dependencies. Previously registered UNCONDITIONALLY in
        // InsightsFacadeModule, which created the LATENT BUG #1 narrative: under compound-OFF,
        // DI resolution threw InvalidOperationException at endpoint-handler invocation
        // (500 instead of the contract-specified 503). Null peer (NullInsightsAi) is
        // registered in AddNullObjectsForCompoundOff. Scoped to match transitive lifetime.
        services.AddScoped<IInsightsAi, InsightsOrchestrator>();
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
        // ExecutorType.DeliverComposite (= 42) paired with NodeType.DeliverComposite (= 100_000_004).
        // Existing DeliverOutputNodeExecutor for ExecutorType.DeliverOutput is UNCHANGED
        // (backward-compat invariant — single-action Output Node behavior preserved).
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.DeliverCompositeNodeExecutor>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.ConditionNodeExecutor>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.AiAnalysisNodeExecutor>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.CreateNotificationNodeExecutor>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.QueryDataverseNodeExecutor>();

        // AgentServiceNodeExecutor — ExecutorType.AgentService = 60 (Phase 2, ADR-010, AIPU-061).
        // Requires AgentServiceClient singleton (AIPU-060). Kill switch: AgentService:Enabled.
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Foundry.AgentServiceClient>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.AgentServiceNodeExecutor>();

        // LookupUserMembershipNodeExecutor — ExecutorType.LookupUserMembership = 52 (R3 Part 1, FR-1B.1, task 041).
        // Singleton+Scoped DI pattern: injects IServiceScopeFactory to resolve the Scoped
        // IMembershipResolverService per execution. In-process call (NOT HTTP round-trip).
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.LookupUserMembershipNodeExecutor>();

        // AiCompletionNodeExecutor — ExecutorType.AiCompletion = 1
        // (R7 spaarke-ai-platform-unification-r7 / FR-12, task 002).
        // Closes R4 /narrate graduation gate. Singleton: ILogger + IOpenAiClient
        // (both Singleton-safe); no Scoped deps, no IServiceScopeFactory indirection.
        // Prompt-only structured LLM call (no Tool, no Document required) — distinguishes
        // from AiAnalysisNodeExecutor's Tool+Document contract per FR-13. UNCONDITIONAL
        // registration per CLAUDE.md §F.1 asymmetric-registration governance.
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.AiCompletionNodeExecutor>();

        // EntityNameValidatorNodeExecutor — ExecutorType.EntityNameValidator = 141
        // (R4 spaarke-daily-update-service-r4 / FR-3, task 003).
        // Singleton: pure string analysis, no external deps beyond ILogger; matches the
        // SanitizerNodeExecutor / GroundingVerifyNode shape. Post-LLM scrubber that strips
        // hallucinated entity names not present in the input-derived allow-list and emits
        // a structured `hallucination_detected` warning per removal (App Insights query
        // target per docs/guides/AI-MONITORING-DASHBOARD.md). UNCONDITIONAL registration
        // per CLAUDE.md §F.1 asymmetric-registration governance (no feature gate).
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.EntityNameValidatorNodeExecutor>();

        // StartNodeExecutor — ExecutorType.Start = 33 (R4 spaarke-daily-update-service-r4,
        // post canonical-truth deploy UAT 2026-06-25). First-class entry-point executor:
        // binds the dispatching wrapper's payload (Parameters[payloadKey]) as JsonElement
        // into the playbook scope under node.OutputVariable (default "start"). Optional
        // input-contract validation gated by configJson.validateOnExecute. Singleton:
        // pure ConfigJson + Parameters read, ILogger only; no Scoped deps. UNCONDITIONAL
        // registration per CLAUDE.md §10 BFF Hygiene §F.1.
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.StartNodeExecutor>();

        // LoadKnowledgeNodeExecutor — ExecutorType.LoadKnowledge = 142 (R4 spaarke-daily-update-service-r4,
        // UAT 2026-06-26 same failure class as Start). Pass-through placeholder for the
        // R5 AI Search knowledge-source binding. Reads configJson.passthroughBinding
        // (optional name→template map), renders templates against scope, binds resolved
        // object to node.OutputVariable (default "channelRegistry"). Singleton: pure
        // ConfigJson + scope read via ITemplateEngine + ILogger; no Scoped deps.
        // UNCONDITIONAL registration per CLAUDE.md §10 BFF Hygiene §F.1.
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.LoadKnowledgeNodeExecutor>();

        // ReturnResponseNodeExecutor — ExecutorType.ReturnResponse = 143 (R4 spaarke-daily-update-service-r4,
        // UAT 2026-06-26 same failure class as Start). Terminal node — projects upstream
        // node outputs into the run's return value via configJson.responseBinding (optional
        // _validationMetadata sidecar). Bound to node.OutputVariable (default "response").
        // Singleton: pure ConfigJson + scope read via ITemplateEngine + ILogger; no Scoped
        // deps. UNCONDITIONAL registration per CLAUDE.md §10 BFF Hygiene §F.1.
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.ReturnResponseNodeExecutor>();

        // CodeInterpreterBridge — thin wrapper around AgentServiceClient for Code Interpreter sandbox
        // invocations (AIPU-070). Singleton: stateless, thread-safe. Kill switch: CodeInterpreter:Enabled.
        // CodeInterpreterHandler resolves this bridge via DI when catalog rows expose the sandbox tools
        // (ADR-010: no per-tool DI registrations beyond the handler assembly scan).
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Foundry.CodeInterpreterBridge>();

        // GroundingVerifier — D-P9 / D-47 / LAVERN ADR 10.6 platform primitive (Insights Engine Phase 1).
        // Mechanical zero-LLM citation verifier (substring + sliding-window, 10K-char DoS cap).
        // Singleton: stateless, thread-safe; shared across Insights synthesis (D-P14) and Action Engine consumers.
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.CitationVerification.IGroundingVerifier,
            Sprk.Bff.Api.Services.Ai.CitationVerification.GroundingVerifier>();

        // GroundingVerifyNode — D-P9 + D-P12 node executor (ExecutorType.GroundingVerify = 70).
        // Wraps IGroundingVerifier as INodeExecutor for the node-based playbook system.
        // Singleton matches the other INodeExecutor registrations above (executors are stateless).
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor,
            Sprk.Bff.Api.Services.Ai.Nodes.GroundingVerifyNode>();

        // D-P12 task 022 — Five new Insights-mode node executors (ExecutorType 80–120).
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
    /// frozen-engine (<see cref="IPlaybookOrchestrationService"/>) calls in a Redis layer per ADR-009.
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
        // Dependencies: IDataverseService (Singleton) + IServiceScopeFactory (Singleton;
        // resolves the Scoped IConsumerRoutingService per reverse lookup — FR-P3-01
        // replaced the config-map reverse scan); lifetime parity verified.
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

        // FR-B-12 (task 061) — ISemanticScopeProvider WRAPS whichever IRagService was just
        // registered above (real RagService when AI Search keys are configured, NullRagService
        // otherwise) via constructor injection — no conditional branching needed here. The
        // provider forwards every call through IRagService.SearchAsync, so it inherits both the
        // real hybrid-search behavior AND the NullRagService P3 Fail-Fast behavior automatically
        // (CLAUDE.md §11 default-to-reuse: wrap, don't duplicate the Null-Object pattern).
        // ADR-032 §F.1 inspection: nothing unconditional resolves this service today — the live
        // per-turn ContextBinder Semantic-slice call site is a deliberately deferred follow-on
        // (task 060, the Organizational-scope provider, lands in the SAME M-parallel wave and is
        // likely to touch the same shared ContextBinder.cs / ContextBindingRequest files; wiring
        // both providers into that seam in one pass avoids two concurrent agents racing on it).
        // Registering the provider now means it is DI-ready the moment that follow-on wiring
        // lands. No separate Null-Object peer is needed even then, per the note above.
        services.AddSingleton<ISemanticScopeProvider, SemanticScopeProvider>();
        Console.WriteLine("✓ Semantic-scope provider registered (FR-B-12; wraps IRagService, preserves PrivilegeFilterBuilder ACL)");
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
