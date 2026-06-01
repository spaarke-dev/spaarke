using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Insights;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Ai.RecordSearch;
using Sprk.Bff.Api.Services.Ai.SemanticSearch;
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
            Console.WriteLine("\u26a0 Document Intelligence services disabled (DocumentIntelligence:Enabled = false)");
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

            Console.WriteLine("\u2713 Analysis services enabled");
        }
        else if (!documentIntelligenceEnabled)
        {
            Console.WriteLine("\u26a0 Analysis services disabled (requires DocumentIntelligence:Enabled = true)");
        }
        else
        {
            Console.WriteLine("\u26a0 Analysis services disabled (Analysis:Enabled = false)");
        }

        AddRecordMatchingServices(services, configuration);

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
        // ILogger, optional ISessionPersistenceService — last is null-tolerant via GetService).
        services.AddScoped<ChatSessionManager>(sp => new ChatSessionManager(
            cache:                sp.GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(),
            dataverseRepository:  sp.GetRequiredService<IChatDataverseRepository>(),
            logger:               sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ChatSessionManager>>(),
            persistence:          sp.GetService<Sprk.Bff.Api.Services.Ai.Sessions.ISessionPersistenceService>()));

        // B5 — ChatHistoryManager (deps: ChatSessionManager + IChatDataverseRepository + ILogger — all unconditional).
        services.AddScoped<ChatHistoryManager>();

        // L5 — AnalysisChatContextResolver (deps: IGenericEntityService + IDistributedCache + ILogger).
        services.AddScoped<AnalysisChatContextResolver>();

        // L5 — StandaloneChatContextProvider (deps: IDistributedCache + ILogger).
        services.AddScoped<StandaloneChatContextProvider>();
    }

    private static void AddAnalysisOrchestrationServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AnalysisOptions>(configuration.GetSection(AnalysisOptions.SectionName));
        services.AddHttpClient<AnalysisActionService>();
        services.AddHttpClient<AnalysisSkillService>();
        services.AddHttpClient<AnalysisKnowledgeService>();
        services.AddHttpClient<AnalysisToolService>();
        services.AddHttpClient<IScopeResolverService, ScopeResolverService>();
        services.AddScoped<IScopeManagementService, ScopeManagementService>();
        services.AddScoped<IAnalysisContextBuilder, AnalysisContextBuilder>();
        services.AddScoped<IWorkingDocumentService, WorkingDocumentService>();
        services.AddHttpContextAccessor();
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Export.DocxExportService>();
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
    }
    private static void AddPlaybookServices(IServiceCollection services)
    {
        services.AddHttpClient<IPlaybookService, PlaybookService>();
        services.AddHttpClient<INodeService, NodeService>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutorRegistry, Sprk.Bff.Api.Services.Ai.Nodes.NodeExecutorRegistry>();
        services.AddScoped<IPlaybookOrchestrationService, PlaybookOrchestrationService>();
        services.AddHttpClient<IPlaybookSharingService, PlaybookSharingService>();
        // NotificationService promoted to unconditional registration (task 011 Phase 1b Tier 1, D-09 §2 B1).
        // See AddUnconditionalChatAndNotificationServices below.
        services.AddHostedService<Sprk.Bff.Api.Services.PlaybookSchedulerService>();
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
    }

    private static void AddBuilderServices(IServiceCollection services)
    {
        services.AddScoped<IAiPlaybookBuilderService, AiPlaybookBuilderService>();
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Builder.BuilderToolExecutor>();
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Builder.IBuilderAgentService, Sprk.Bff.Api.Services.Ai.Builder.BuilderAgentService>();
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Builder.BuilderScopeImporter>();
        services.AddSingleton<IModelSelector, ModelSelector>();
        services.AddScoped<IIntentClassificationService, IntentClassificationService>();
        services.AddScoped<IEntityResolutionService, EntityResolutionService>();
        services.AddScoped<IClarificationService, ClarificationService>();
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
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.ConditionNodeExecutor>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.AiAnalysisNodeExecutor>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.CreateNotificationNodeExecutor>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.QueryDataverseNodeExecutor>();

        // AgentServiceNodeExecutor — ActionType.AgentService = 60 (Phase 2, ADR-010, AIPU-061).
        // Requires AgentServiceClient singleton (AIPU-060). Kill switch: AgentService:Enabled.
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Foundry.AgentServiceClient>();
        services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutor, Sprk.Bff.Api.Services.Ai.Nodes.AgentServiceNodeExecutor>();

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
        services.AddSingleton<IInsightsPlaybookExecutionCache, InsightsPlaybookExecutionCache>();
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
            Console.WriteLine("\u26a0 RAG services disabled (requires DocumentIntelligence:AiSearchEndpoint/Key)");
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
