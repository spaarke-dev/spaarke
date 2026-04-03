using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.RecordSearch;
using Sprk.Bff.Api.Services.Ai.SemanticSearch;

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

            services.AddAiModule(configuration);
            Console.WriteLine("\u2713 AI Platform Foundation module enabled (DocumentParserRouter, SemanticDocumentChunker, RagQueryBuilder)");

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
        return services;
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
        services.AddSingleton<Sprk.Bff.Api.Services.NotificationService>();
        services.AddHostedService<Sprk.Bff.Api.Services.PlaybookSchedulerService>();
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
