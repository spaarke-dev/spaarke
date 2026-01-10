using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Polly;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Api.Admin;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Authorization;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Infrastructure.Startup;
using Sprk.Bff.Api.Infrastructure.Validation;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration Validation ----

// Register and validate configuration options with fail-fast behavior
builder.Services
    .AddOptions<GraphOptions>()
    .Bind(builder.Configuration.GetSection(GraphOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<DataverseOptions>()
    .Bind(builder.Configuration.GetSection(DataverseOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<ServiceBusOptions>()
    .Bind(builder.Configuration.GetSection(ServiceBusOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<RedisOptions>()
    .Bind(builder.Configuration.GetSection(RedisOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Document Intelligence Options - Azure OpenAI and Document Intelligence configuration
// Secrets: ai-openai-endpoint, ai-openai-key, ai-docintel-endpoint, ai-docintel-key (KeyVault)
// Note: Uses custom DocumentIntelligenceOptionsValidator for conditional validation (only validates when Enabled=true)
builder.Services
    .AddOptions<DocumentIntelligenceOptions>()
    .Bind(builder.Configuration.GetSection(DocumentIntelligenceOptions.SectionName))
    .ValidateOnStart();

// Analysis Options - AI-driven document analysis with Prompt Flow and hybrid RAG
// Supports multi-tenant deployment via Dataverse Environment Variables
// Maps to: sprk_EnableAiFeatures, sprk_EnableMultiDocumentAnalysis, sprk_PromptFlowEndpoint, etc.
builder.Services
    .AddOptions<AnalysisOptions>()
    .Bind(builder.Configuration.GetSection(AnalysisOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Custom validation for conditional requirements
builder.Services.AddSingleton<IValidateOptions<GraphOptions>, GraphOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<DocumentIntelligenceOptions>, DocumentIntelligenceOptionsValidator>();

// Add startup health check to validate configuration
builder.Services.AddHostedService<StartupValidationService>();

// ---- Services Registration ----

// Application Insights telemetry - captures ILogger output
builder.Services.AddApplicationInsightsTelemetry();

// Cross-cutting concerns
// builder.Services.AddProblemDetails(); // Requires newer version


// Core module (AuthorizationService, RequestCache)
builder.Services.AddSpaarkeCore();

// Data Access Layer - Document storage resolution (Phase 2 v1.0.5 implementation)
builder.Services.AddScoped<Sprk.Bff.Api.Infrastructure.Dataverse.IDocumentStorageResolver, Sprk.Bff.Api.Infrastructure.Dataverse.DocumentStorageResolver>();

// ============================================================================
// AUTHENTICATION - Azure AD JWT Bearer Token Validation
// ============================================================================
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

// Register authorization handler (Scoped to match AuthorizationService dependency)
builder.Services.AddScoped<IAuthorizationHandler, ResourceAccessHandler>();

// Authorization policies - granular operation-level policies matching SPE/Graph API operations
// Each policy maps to a specific OperationAccessPolicy operation with required Dataverse AccessRights
builder.Services.AddAuthorization(options =>
{
    // ====================================================================================
    // DRIVEITEM CONTENT OPERATIONS (most common user operations)
    // ====================================================================================
    options.AddPolicy("canpreviewfiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.preview")));

    options.AddPolicy("candownloadfiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.content.download")));

    options.AddPolicy("canuploadfiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.content.upload")));

    options.AddPolicy("canreplacefiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.content.replace")));

    // ====================================================================================
    // DRIVEITEM METADATA OPERATIONS
    // ====================================================================================
    options.AddPolicy("canreadmetadata", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.get")));

    options.AddPolicy("canupdatemetadata", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.update")));

    options.AddPolicy("canlistchildren", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.list.children")));

    // ====================================================================================
    // DRIVEITEM FILE MANAGEMENT
    // ====================================================================================
    options.AddPolicy("candeletefiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.delete")));

    options.AddPolicy("canmovefiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.move")));

    options.AddPolicy("cancopyfiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.copy")));

    options.AddPolicy("cancreatefolders", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.create.folder")));

    // ====================================================================================
    // DRIVEITEM SHARING & PERMISSIONS
    // ====================================================================================
    options.AddPolicy("cansharefiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.createlink")));

    options.AddPolicy("canmanagefilepermissions", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.permissions.add")));

    // ====================================================================================
    // DRIVEITEM VERSIONING
    // ====================================================================================
    options.AddPolicy("canviewversions", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.versions.list")));

    options.AddPolicy("canrestoreversions", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.versions.restore")));

    // ====================================================================================
    // CONTAINER OPERATIONS
    // ====================================================================================
    options.AddPolicy("canlistcontainers", p =>
        p.Requirements.Add(new ResourceAccessRequirement("container.list")));

    options.AddPolicy("cancreatecontainers", p =>
        p.Requirements.Add(new ResourceAccessRequirement("container.create")));

    options.AddPolicy("candeletecontainers", p =>
        p.Requirements.Add(new ResourceAccessRequirement("container.delete")));

    options.AddPolicy("canupdatecontainers", p =>
        p.Requirements.Add(new ResourceAccessRequirement("container.update")));

    options.AddPolicy("canmanagecontainerpermissions", p =>
        p.Requirements.Add(new ResourceAccessRequirement("container.permissions.add")));

    // ====================================================================================
    // ADVANCED OPERATIONS (less common, admin-level)
    // ====================================================================================
    options.AddPolicy("cansearchfiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.search")));

    options.AddPolicy("cantrackchanges", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.delta")));

    options.AddPolicy("canmanagecompliancelabels", p =>
        p.Requirements.Add(new ResourceAccessRequirement("driveitem.sensitivitylabel.assign")));

    // ====================================================================================
    // LEGACY COMPATIBILITY (backward compatible with old operation names)
    // ====================================================================================
    options.AddPolicy("canreadfiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("preview_file")));

    options.AddPolicy("canwritefiles", p =>
        p.Requirements.Add(new ResourceAccessRequirement("upload_file")));

    options.AddPolicy("canmanagecontainers", p =>
        p.Requirements.Add(new ResourceAccessRequirement("create_container")));
});

// Documents module (endpoints + filters)
builder.Services.AddDocumentsModule();

// Workers module (Service Bus + BackgroundService)
builder.Services.AddWorkersModule(builder.Configuration);

// ============================================================================
// DISTRIBUTED CACHE - Redis for production, in-memory for local dev (ADR-004, ADR-009)
// ============================================================================
var redisEnabled = builder.Configuration.GetValue<bool>("Redis:Enabled");
if (redisEnabled)
{
    var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
        ?? builder.Configuration["Redis:ConnectionString"];

    if (string.IsNullOrWhiteSpace(redisConnectionString))
    {
        throw new InvalidOperationException(
            "Redis is enabled but no connection string found. " +
            "Set 'ConnectionStrings:Redis' or 'Redis:ConnectionString' in configuration.");
    }

    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = builder.Configuration["Redis:InstanceName"] ?? "sdap:";

        // Connection resilience options for production reliability
        options.ConfigurationOptions = StackExchange.Redis.ConfigurationOptions.Parse(redisConnectionString);
        options.ConfigurationOptions.AbortOnConnectFail = false;  // Don't crash if Redis temporarily unavailable
        options.ConfigurationOptions.ConnectTimeout = 5000;       // 5 second connection timeout
        options.ConfigurationOptions.SyncTimeout = 5000;          // 5 second operation timeout
        options.ConfigurationOptions.ConnectRetry = 3;            // Retry connection 3 times
        options.ConfigurationOptions.ReconnectRetryPolicy = new StackExchange.Redis.ExponentialRetry(1000);  // Exponential backoff (1s base)
    });

    builder.Logging.AddSimpleConsole().Services.Configure<Microsoft.Extensions.Logging.Console.SimpleConsoleFormatterOptions>(options =>
    {
        options.TimestampFormat = "HH:mm:ss ";
    });

    var logger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger("Program");
    logger.LogInformation(
        "Distributed cache: Redis enabled with instance name '{InstanceName}'",
        builder.Configuration["Redis:InstanceName"] ?? "sdap:");
}
else
{
    // Use in-memory cache for local development only
    builder.Services.AddDistributedMemoryCache();

    var logger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger("Program");
    logger.LogWarning(
        "Distributed cache: Using in-memory cache (not distributed). " +
        "This should ONLY be used in local development.");
}

builder.Services.AddMemoryCache();

// Graph API Resilience Configuration (Task 4.1)
builder.Services
    .AddOptions<Sprk.Bff.Api.Configuration.GraphResilienceOptions>()
    .Bind(builder.Configuration.GetSection(Sprk.Bff.Api.Configuration.GraphResilienceOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Register GraphHttpMessageHandler for centralized resilience (retry, circuit breaker, timeout)
builder.Services.AddTransient<Sprk.Bff.Api.Infrastructure.Http.GraphHttpMessageHandler>();

// Configure named HttpClient for Graph API with resilience handler
builder.Services.AddHttpClient("GraphApiClient")
    .AddHttpMessageHandler<Sprk.Bff.Api.Infrastructure.Http.GraphHttpMessageHandler>()
    .ConfigureHttpClient(client =>
    {
        client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
        client.DefaultRequestHeaders.Add("Accept", "application/json");
    });

// Singleton GraphServiceClient factory (now uses IHttpClientFactory with resilience handler)
builder.Services.AddSingleton<IGraphClientFactory, Sprk.Bff.Api.Infrastructure.Graph.GraphClientFactory>();

// Dataverse service - Singleton lifetime for ServiceClient connection reuse (eliminates 500ms initialization overhead)
// ServiceClient is thread-safe and designed for long-lived use with internal connection pooling
builder.Services.AddSingleton<IDataverseService>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<DataverseServiceClientImpl>>();
    return new DataverseServiceClientImpl(configuration, logger);
});

// ============================================================================
// DOCUMENT INTELLIGENCE SERVICES - Azure OpenAI client for document analysis (ADR-013)
// ============================================================================
// Singleton: AzureOpenAIClient is thread-safe and benefits from connection reuse
// Only register if Document Intelligence is enabled in configuration
var documentIntelligenceEnabled = builder.Configuration.GetValue<bool>("DocumentIntelligence:Enabled");
if (documentIntelligenceEnabled)
{
    // Telemetry: Singleton for AI metrics (OpenTelemetry-compatible)
    builder.Services.AddSingleton<Sprk.Bff.Api.Telemetry.AiTelemetry>();

    builder.Services.AddSingleton<Sprk.Bff.Api.Services.Ai.OpenAiClient>();
    builder.Services.AddSingleton<Sprk.Bff.Api.Services.Ai.IOpenAiClient>(sp => sp.GetRequiredService<Sprk.Bff.Api.Services.Ai.OpenAiClient>());
    builder.Services.AddSingleton<Sprk.Bff.Api.Services.Ai.TextExtractorService>();
    builder.Services.AddSingleton<Sprk.Bff.Api.Services.Ai.ITextExtractor>(sp => sp.GetRequiredService<Sprk.Bff.Api.Services.Ai.TextExtractorService>());
    Console.WriteLine("✓ Document Intelligence services enabled");
}
else
{
    Console.WriteLine("⚠ Document Intelligence services disabled (DocumentIntelligence:Enabled = false)");
}

// ============================================================================
// ANALYSIS SERVICES - AI-driven document analysis with configurable scopes
// ============================================================================
var analysisEnabled = builder.Configuration.GetValue<bool>("Analysis:Enabled", true);
if (analysisEnabled && documentIntelligenceEnabled)
{
    // Register Analysis configuration
    builder.Services.Configure<Sprk.Bff.Api.Configuration.AnalysisOptions>(
        builder.Configuration.GetSection(Sprk.Bff.Api.Configuration.AnalysisOptions.SectionName));

    // Analysis services - all scoped due to SpeFileStore dependency
    builder.Services.AddScoped<Sprk.Bff.Api.Services.Ai.IScopeResolverService, Sprk.Bff.Api.Services.Ai.ScopeResolverService>();
    builder.Services.AddScoped<Sprk.Bff.Api.Services.Ai.IAnalysisContextBuilder, Sprk.Bff.Api.Services.Ai.AnalysisContextBuilder>();
    builder.Services.AddScoped<Sprk.Bff.Api.Services.Ai.IWorkingDocumentService, Sprk.Bff.Api.Services.Ai.WorkingDocumentService>();

    // Export services - DOCX, PDF, and Email export for analysis results (R3 Phase 4)
    // PDF uses QuestPDF in-process generation (ADR-001 compliant)
    // Email uses Microsoft Graph /me/sendMail API
    builder.Services.AddHttpContextAccessor(); // Required for email export (OBO flow)
    builder.Services.AddScoped<Sprk.Bff.Api.Services.Ai.Export.IExportService, Sprk.Bff.Api.Services.Ai.Export.DocxExportService>();
    builder.Services.AddScoped<Sprk.Bff.Api.Services.Ai.Export.IExportService, Sprk.Bff.Api.Services.Ai.Export.PdfExportService>();
    builder.Services.AddScoped<Sprk.Bff.Api.Services.Ai.Export.IExportService, Sprk.Bff.Api.Services.Ai.Export.EmailExportService>();
    builder.Services.AddScoped<Sprk.Bff.Api.Services.Ai.Export.ExportServiceRegistry>();

    builder.Services.AddScoped<Sprk.Bff.Api.Services.Ai.IAnalysisOrchestrationService, Sprk.Bff.Api.Services.Ai.AnalysisOrchestrationService>();

    // Playbook Service - CRUD operations for analysis playbooks (R3 Phase 3)
    builder.Services.AddHttpClient<Sprk.Bff.Api.Services.Ai.IPlaybookService, Sprk.Bff.Api.Services.Ai.PlaybookService>();

    // Node Service - CRUD operations for playbook nodes (ai-node-playbook-builder project)
    builder.Services.AddHttpClient<Sprk.Bff.Api.Services.Ai.INodeService, Sprk.Bff.Api.Services.Ai.NodeService>();

    // Node Executor Registry - manages node type executors (ai-node-playbook-builder project)
    builder.Services.AddSingleton<Sprk.Bff.Api.Services.Ai.Nodes.INodeExecutorRegistry, Sprk.Bff.Api.Services.Ai.Nodes.NodeExecutorRegistry>();

    // Playbook Orchestration Service - multi-node playbook execution (ai-node-playbook-builder project)
    builder.Services.AddScoped<Sprk.Bff.Api.Services.Ai.IPlaybookOrchestrationService, Sprk.Bff.Api.Services.Ai.PlaybookOrchestrationService>();

    // Playbook Sharing Service - team/organization sharing for playbooks (R3 Phase 3 Task 023)
    builder.Services.AddHttpClient<Sprk.Bff.Api.Services.Ai.IPlaybookSharingService, Sprk.Bff.Api.Services.Ai.PlaybookSharingService>();

    // RAG Knowledge Deployment Service - manages SearchClient routing for deployment models (R3)
    // Uses same AI Search config as Record Matching (DocumentIntelligence:AiSearchEndpoint/Key)
    var docIntelOptions = builder.Configuration.GetSection(DocumentIntelligenceOptions.SectionName).Get<DocumentIntelligenceOptions>();
    if (!string.IsNullOrEmpty(docIntelOptions?.AiSearchEndpoint) && !string.IsNullOrEmpty(docIntelOptions?.AiSearchKey))
    {
        // SearchIndexClient for index management and creating SearchClients
        builder.Services.AddSingleton(sp =>
        {
            return new Azure.Search.Documents.Indexes.SearchIndexClient(
                new Uri(docIntelOptions.AiSearchEndpoint),
                new Azure.AzureKeyCredential(docIntelOptions.AiSearchKey));
        });

        // KnowledgeDeploymentService - Singleton for caching deployment configs
        builder.Services.AddSingleton<Sprk.Bff.Api.Services.Ai.IKnowledgeDeploymentService, Sprk.Bff.Api.Services.Ai.KnowledgeDeploymentService>();

        // EmbeddingCache - Redis-based caching for embeddings (ADR-009)
        builder.Services.AddSingleton<Sprk.Bff.Api.Services.Ai.IEmbeddingCache, Sprk.Bff.Api.Services.Ai.EmbeddingCache>();

        // RagService - Hybrid search service for RAG retrieval (keyword + vector + semantic ranking)
        builder.Services.AddSingleton<Sprk.Bff.Api.Services.Ai.IRagService, Sprk.Bff.Api.Services.Ai.RagService>();

        // VisualizationService - Document relationship visualization using vector similarity
        builder.Services.AddSingleton<Sprk.Bff.Api.Services.Ai.Visualization.IVisualizationService, Sprk.Bff.Api.Services.Ai.Visualization.VisualizationService>();
        Console.WriteLine("✓ RAG services enabled (hybrid search + embedding cache + visualization)");
    }
    else
    {
        Console.WriteLine("⚠ RAG services disabled (requires DocumentIntelligence:AiSearchEndpoint/Key)");
    }

    // Tool Framework - Dynamic tool loading for AI analysis tools
    var toolFrameworkOptions = builder.Configuration.GetSection(Sprk.Bff.Api.Configuration.ToolFrameworkOptions.SectionName);
    if (toolFrameworkOptions.GetValue<bool>("Enabled", true))
    {
        builder.Services.AddToolFramework(builder.Configuration);
        Console.WriteLine("✓ Tool framework enabled");
    }
    else
    {
        Console.WriteLine("⚠ Tool framework disabled (ToolFramework:Enabled = false)");
    }

    Console.WriteLine("✓ Analysis services enabled");
}
else if (!documentIntelligenceEnabled)
{
    Console.WriteLine("⚠ Analysis services disabled (requires DocumentIntelligence:Enabled = true)");
}
else
{
    Console.WriteLine("⚠ Analysis services disabled (Analysis:Enabled = false)");
}

// ============================================================================
// RECORD MATCHING SERVICES - Azure AI Search for document-to-record matching (Phase 2)
// ============================================================================
var recordMatchingEnabled = builder.Configuration.GetValue<bool>("DocumentIntelligence:RecordMatchingEnabled");
if (recordMatchingEnabled)
{
    builder.Services.AddHttpClient<Sprk.Bff.Api.Services.RecordMatching.DataverseIndexSyncService>();
    builder.Services.AddSingleton<Sprk.Bff.Api.Services.RecordMatching.IDataverseIndexSyncService>(sp =>
        sp.GetRequiredService<Sprk.Bff.Api.Services.RecordMatching.DataverseIndexSyncService>());
    builder.Services.AddSingleton<Sprk.Bff.Api.Services.RecordMatching.IRecordMatchService,
        Sprk.Bff.Api.Services.RecordMatching.RecordMatchService>();
    Console.WriteLine("✓ Record Matching services enabled (index: {0})", builder.Configuration["DocumentIntelligence:AiSearchIndexName"] ?? "spaarke-records-index");
}
else
{
    Console.WriteLine("⚠ Record Matching services disabled (DocumentIntelligence:RecordMatchingEnabled = false)");
}

// ============================================================================
// EMAIL-TO-DOCUMENT CONVERSION SERVICES (Email-to-Document Automation project)
// ============================================================================
// Email processing stats service - in-memory stats readable via API (admin monitoring PCF)
builder.Services.AddSingleton<Sprk.Bff.Api.Services.Email.EmailProcessingStatsService>();

// Email telemetry - OpenTelemetry-compatible metrics for email processing (delegates to stats service)
builder.Services.AddSingleton<Sprk.Bff.Api.Telemetry.EmailTelemetry>(sp =>
    new Sprk.Bff.Api.Telemetry.EmailTelemetry(sp.GetService<Sprk.Bff.Api.Services.Email.EmailProcessingStatsService>()));

// Register Email Processing configuration
builder.Services.Configure<Sprk.Bff.Api.Configuration.EmailProcessingOptions>(
    builder.Configuration.GetSection(Sprk.Bff.Api.Configuration.EmailProcessingOptions.SectionName));

// Email-to-EML converter - uses HttpClient for Dataverse Web API calls
builder.Services.AddHttpClient<Sprk.Bff.Api.Services.Email.EmailToEmlConverter>();
builder.Services.AddScoped<Sprk.Bff.Api.Services.Email.IEmailToEmlConverter>(sp =>
    sp.GetRequiredService<Sprk.Bff.Api.Services.Email.EmailToEmlConverter>());

// Email filter service - evaluates rules from Dataverse with Redis caching (NFR-06: 5min TTL)
builder.Services.AddScoped<Sprk.Bff.Api.Services.Email.IEmailFilterService,
    Sprk.Bff.Api.Services.Email.EmailFilterService>();

// Email rule seed service - seeds default exclusion rules to Dataverse
builder.Services.AddScoped<Sprk.Bff.Api.Services.Email.EmailRuleSeedService>();

// Email association service - determines Matter/Account/Contact associations with confidence scoring
builder.Services.AddScoped<Sprk.Bff.Api.Services.Email.IEmailAssociationService,
    Sprk.Bff.Api.Services.Email.EmailAssociationService>();

// Email attachment processor - creates separate document records for email attachments
builder.Services.AddScoped<Sprk.Bff.Api.Services.Email.IEmailAttachmentProcessor,
    Sprk.Bff.Api.Services.Email.EmailAttachmentProcessor>();

// HttpClient for email polling backup service (Dataverse queries)
builder.Services.AddHttpClient("DataversePolling")
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });

Console.WriteLine("✓ Email-to-Document conversion services registered");

// Background Job Processing (ADR-004) - Service Bus Strategy
// Always register JobSubmissionService (unified entry point)
builder.Services.AddSingleton<Sprk.Bff.Api.Services.Jobs.JobSubmissionService>();

// Register job handlers
builder.Services.AddScoped<Sprk.Bff.Api.Services.Jobs.IJobHandler, Sprk.Bff.Api.Services.Jobs.Handlers.DocumentProcessingJobHandler>();
builder.Services.AddScoped<Sprk.Bff.Api.Services.Jobs.IJobHandler, Sprk.Bff.Api.Services.Jobs.Handlers.EmailToDocumentJobHandler>();
builder.Services.AddScoped<Sprk.Bff.Api.Services.Jobs.IJobHandler, Sprk.Bff.Api.Services.Jobs.Handlers.BatchProcessEmailsJobHandler>();
// Also register EmailToDocumentJobHandler as concrete type for BatchProcessEmailsJobHandler dependency
builder.Services.AddScoped<Sprk.Bff.Api.Services.Jobs.Handlers.EmailToDocumentJobHandler>();
// DocumentAnalysisJobHandler removed - background AI analysis is now triggered from PCF (requires user context)

// Configure Service Bus job processing
var serviceBusConnectionString = builder.Configuration.GetConnectionString("ServiceBus");
if (string.IsNullOrWhiteSpace(serviceBusConnectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:ServiceBus is required. " +
        "For local development, use Service Bus emulator (see docs/README-Local-Development.md) " +
        "or configure a dev Service Bus namespace.");
}

builder.Services.AddSingleton(sp => new Azure.Messaging.ServiceBus.ServiceBusClient(serviceBusConnectionString));
builder.Services.AddHostedService<Sprk.Bff.Api.Services.Jobs.ServiceBusJobProcessor>();
builder.Services.AddHostedService<Sprk.Bff.Api.Services.Jobs.EmailPollingBackupService>();

// DocumentVector backfill service - one-time migration to populate documentVector field
builder.Services.Configure<Sprk.Bff.Api.Services.Jobs.DocumentVectorBackfillOptions>(
    builder.Configuration.GetSection(Sprk.Bff.Api.Services.Jobs.DocumentVectorBackfillOptions.SectionName));
builder.Services.AddHostedService<Sprk.Bff.Api.Services.Jobs.DocumentVectorBackfillService>();

// Embedding migration service - migrates embeddings from 1536 to 3072 dimensions (Phase 5b)
builder.Services.Configure<Sprk.Bff.Api.Services.Jobs.EmbeddingMigrationOptions>(
    builder.Configuration.GetSection(Sprk.Bff.Api.Services.Jobs.EmbeddingMigrationOptions.SectionName));
builder.Services.AddHostedService<Sprk.Bff.Api.Services.Jobs.EmbeddingMigrationService>();

builder.Logging.AddConsole();
Console.WriteLine("✓ Job processing configured with Service Bus (queue: sdap-jobs)");
Console.WriteLine("✓ Email polling backup service configured");
Console.WriteLine("✓ Document vector backfill service registered (enable via config)");
Console.WriteLine("✓ Embedding migration service registered (enable via config)");

// ============================================================================
// HEALTH CHECKS - Redis availability monitoring
// ============================================================================
//
// TECHNICAL DEBT WARNING: This health check uses BuildServiceProvider() in a lambda,
// which triggers ASP0000 warning. This is intentional technical debt with low risk.
//
// WHY THIS PATTERN EXISTS:
// - Health checks are registered BEFORE app.Build() is called
// - We need to test actual Redis connection, not just configuration
// - Lambda health checks don't support DI constructor injection
// - The alternative (IHealthCheck classes) requires more boilerplate
//
// WHY THIS IS NOT "ZOMBIE CODE":
// ✅ Executes on every /healthz endpoint call
// ✅ Used by Kubernetes liveness/readiness probes
// ✅ Used by load balancers for traffic routing
// ✅ Has detected Redis outages in production
// ✅ No alternative implementation exists
//
// KNOWN ISSUE:
// - BuildServiceProvider() creates a second service provider instance
// - For Singleton services: same instance used (no duplication)
// - For Scoped services: would create duplicate (but we only use Singleton IDistributedCache)
// - Memory impact: ~1KB per health check execution
//
// PROPER FIX (deferred - medium complexity, low priority):
// - Refactor to use IHealthCheck interface with constructor DI
// - Create RedisHealthCheck class that injects IDistributedCache
// - Requires ~50 lines of code + test updates
// - Risk: medium (test infrastructure changes needed)
// - Priority: low (current pattern works, no production issues)
//
// DECISION: Document the pattern, defer refactoring until higher-priority work complete
// See: tasks/phase-2-task-9-document-health-check.md for refactoring guide
// ============================================================================
builder.Services.AddHealthChecks()
    .AddCheck("redis", () =>
    {
        if (!redisEnabled)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(
                "Redis is disabled (using in-memory cache for development)");
        }

        try
        {
            // NOTE: BuildServiceProvider() usage explained in comment block above
#pragma warning disable ASP0000 // Do not call 'IServiceCollection.BuildServiceProvider' - documented exception
            var cache = builder.Services.BuildServiceProvider().GetRequiredService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
#pragma warning restore ASP0000
            var testKey = "_health_check_";
            var testValue = DateTimeOffset.UtcNow.ToString("O");

            cache.SetString(testKey, testValue, new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10)
            });

            var retrieved = cache.GetString(testKey);
            cache.Remove(testKey);

            if (retrieved == testValue)
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Redis cache is available and responsive");
            }

            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Degraded("Redis cache returned unexpected value");
        }
        catch (Exception ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Redis cache is unavailable", ex);
        }
    });

// ============================================================================
// CORS - Secure, fail-closed configuration
// ============================================================================
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();

// Helper to check if running in non-production test environment
var isTestOrDevelopment = builder.Environment.IsDevelopment() ||
                          builder.Environment.EnvironmentName == "Testing";

// Validate configuration
if (allowedOrigins == null || allowedOrigins.Length == 0)
{
    // In development/testing, allow localhost as fallback
    if (isTestOrDevelopment)
    {
        var logger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger("Program");
        logger.LogWarning(
            "CORS: No allowed origins configured. Falling back to localhost (development only).");

        allowedOrigins = new[]
        {
            "http://localhost:3000",
            "http://localhost:3001",
            "http://127.0.0.1:3000"
        };
    }
    else
    {
        // FAIL-CLOSED: Throw exception in non-development environments
        throw new InvalidOperationException(
            $"CORS configuration is missing or empty in {builder.Environment.EnvironmentName} environment. " +
            "Configure 'Cors:AllowedOrigins' with explicit origin URLs. " +
            "CORS will NOT fall back to AllowAnyOrigin for security reasons.");
    }
}

// Reject wildcard configuration (security violation)
if (allowedOrigins.Contains("*"))
{
    throw new InvalidOperationException(
        "CORS: Wildcard origin '*' is not allowed. " +
        "Configure explicit origin URLs in 'Cors:AllowedOrigins'.");
}

// Validate origin URLs
foreach (var origin in allowedOrigins)
{
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
    {
        throw new InvalidOperationException(
            $"CORS: Invalid origin URL '{origin}'. Must be absolute URL (e.g., https://example.com).");
    }

    if (uri.Scheme != "https" && !isTestOrDevelopment)
    {
        throw new InvalidOperationException(
            $"CORS: Non-HTTPS origin '{origin}' is not allowed in {builder.Environment.EnvironmentName} environment. " +
            "Use HTTPS URLs for security.");
    }
}

// Log allowed origins for audit trail
{
    var logger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger("Program");
    logger.LogInformation(
        "CORS: Configured with {OriginCount} allowed origins: {Origins}",
        allowedOrigins.Length,
        string.Join(", ", allowedOrigins));
}

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        // Support both explicit origins from config and Dataverse/PowerApps wildcard patterns
        policy.SetIsOriginAllowed(origin =>
        {
            // Check explicit allowed origins from configuration
            if (allowedOrigins.Contains(origin))
                return true;

            // Allow Dataverse origins (*.dynamics.com)
            if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            {
                if (uri.Host.EndsWith(".dynamics.com", StringComparison.OrdinalIgnoreCase) ||
                    uri.Host == "dynamics.com")
                    return true;

                // Allow PowerApps origins (*.powerapps.com)
                if (uri.Host.EndsWith(".powerapps.com", StringComparison.OrdinalIgnoreCase) ||
                    uri.Host == "powerapps.com")
                    return true;
            }

            return false;
        })
              .AllowCredentials()
              .AllowAnyMethod()
              .WithHeaders(
                  "Authorization",
                  "Content-Type",
                  "Accept",
                  "X-Requested-With",
                  "X-Correlation-Id")
              .WithExposedHeaders(
                  "request-id",
                  "client-request-id",
                  "traceparent",
                  "X-Correlation-Id",
                  "X-Pagination-TotalCount",
                  "X-Pagination-HasMore")
              .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
    });
});

// ============================================================================
// RATE LIMITING - Per-user/per-IP traffic control (ADR-009)
// ============================================================================
builder.Services.AddRateLimiter(options =>
{
    // 1. Graph Read Operations - High volume, sliding window
    options.AddPolicy("graph-read", context =>
    {
        var userId = context.User?.FindFirst("oid")?.Value
                     ?? context.User?.FindFirst("sub")?.Value
                     ?? context.Connection.RemoteIpAddress?.ToString()
                     ?? "unknown";

        return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 100,
            QueueLimit = 10,
            SegmentsPerWindow = 6 // 10-second segments
        });
    });

    // 2. Graph Write Operations - Lower volume, token bucket for burst
    options.AddPolicy("graph-write", context =>
    {
        var userId = context.User?.FindFirst("oid")?.Value
                     ?? context.User?.FindFirst("sub")?.Value
                     ?? context.Connection.RemoteIpAddress?.ToString()
                     ?? "unknown";

        return RateLimitPartition.GetTokenBucketLimiter(userId, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 20,
            TokensPerPeriod = 10,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 5
        });
    });

    // 3. Dataverse Query Operations - Moderate volume, sliding window
    options.AddPolicy("dataverse-query", context =>
    {
        var userId = context.User?.FindFirst("oid")?.Value
                     ?? context.User?.FindFirst("sub")?.Value
                     ?? context.Connection.RemoteIpAddress?.ToString()
                     ?? "unknown";

        return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 50,
            QueueLimit = 5,
            SegmentsPerWindow = 4 // 15-second segments
        });
    });

    // 3b. Metadata Query Operations - Very high volume with L1 cache (Phase 7)
    options.AddPolicy("metadata-query", context =>
    {
        var userId = context.User?.FindFirst("oid")?.Value
                     ?? context.User?.FindFirst("sub")?.Value
                     ?? context.Connection.RemoteIpAddress?.ToString()
                     ?? "unknown";

        return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 200, // Higher limit due to L1 cache
            QueueLimit = 10,
            SegmentsPerWindow = 6 // 10-second segments
        });
    });

    // 4. Heavy Operations - File uploads, strict concurrency
    options.AddPolicy("upload-heavy", context =>
    {
        var userId = context.User?.FindFirst("oid")?.Value
                     ?? context.User?.FindFirst("sub")?.Value
                     ?? context.Connection.RemoteIpAddress?.ToString()
                     ?? "unknown";

        return RateLimitPartition.GetConcurrencyLimiter(userId, _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = 5,
            QueueLimit = 10
        });
    });

    // 5. Job Submission - Rate-sensitive, fixed window
    options.AddPolicy("job-submission", context =>
    {
        var userId = context.User?.FindFirst("oid")?.Value
                     ?? context.User?.FindFirst("sub")?.Value
                     ?? context.Connection.RemoteIpAddress?.ToString()
                     ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(userId, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 10,
            QueueLimit = 2
        });
    });

    // 6. Anonymous/Unauthenticated - Very restrictive, fixed window
    options.AddPolicy("anonymous", context =>
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(ipAddress, _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 10,
            QueueLimit = 0 // No queueing for anonymous
        });
    });

    // 7. AI Streaming - Strict limit for costly AI operations (Task 072)
    options.AddPolicy("ai-stream", context =>
    {
        var userId = context.User?.FindFirst("oid")?.Value
                     ?? context.User?.FindFirst("sub")?.Value
                     ?? context.Connection.RemoteIpAddress?.ToString()
                     ?? "unknown";

        return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 10, // 10 streaming requests/minute per user
            QueueLimit = 2,   // Allow small queue for burst tolerance
            SegmentsPerWindow = 6 // 10-second segments for smooth limiting
        });
    });

    // 8. AI Batch - Moderate limit for background summarization enqueue
    options.AddPolicy("ai-batch", context =>
    {
        var userId = context.User?.FindFirst("oid")?.Value
                     ?? context.User?.FindFirst("sub")?.Value
                     ?? context.Connection.RemoteIpAddress?.ToString()
                     ?? "unknown";

        return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 20, // 20 batch requests/minute per user
            QueueLimit = 5,
            SegmentsPerWindow = 4 // 15-second segments
        });
    });

    // ProblemDetails JSON response for rate limit rejections
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/problem+json";

        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue)
            ? retryAfterValue.TotalSeconds
            : 60;

        context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();

        var problemDetails = new
        {
            type = "https://tools.ietf.org/html/rfc6585#section-4",
            title = "Too Many Requests",
            status = 429,
            detail = "Rate limit exceeded. Please retry after the specified duration.",
            instance = context.HttpContext.Request.Path.Value,
            retryAfter = $"{retryAfter} seconds"
        };

        await context.HttpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        // Log rate limit rejection for monitoring
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(
            "Rate limit exceeded for {Path} by {User} (IP: {IP}). Retry after {RetryAfter}s",
            context.HttpContext.Request.Path,
            context.HttpContext.User?.Identity?.Name ?? "anonymous",
            context.HttpContext.Connection.RemoteIpAddress,
            retryAfter);
    };
});

// OpenTelemetry configuration for AI metrics and distributed tracing
// Metrics are collected via custom meters and exported to Application Insights
// Custom meters: Sprk.Bff.Api.Ai (AI operations) and Sprk.Bff.Api.Cache (caching)
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("Sprk.Bff.Api.Ai");   // AI telemetry (summarization, RAG, tools, export)
        metrics.AddMeter("Sprk.Bff.Api.Cache"); // Cache metrics (hits, misses, latency)
        metrics.AddMeter("Sprk.Bff.Api.CircuitBreaker"); // Circuit breaker metrics
    })
    .WithTracing(tracing =>
    {
        tracing.AddSource("Sprk.Bff.Api.Ai"); // AI distributed tracing
    });

// ============================================================================
// CIRCUIT BREAKER REGISTRY - Centralized monitoring of all circuit breakers
// ============================================================================
builder.Services.AddSingleton<Sprk.Bff.Api.Infrastructure.Resilience.ICircuitBreakerRegistry,
    Sprk.Bff.Api.Infrastructure.Resilience.CircuitBreakerRegistry>();

// AI Search Resilience Options
builder.Services
    .AddOptions<Sprk.Bff.Api.Configuration.AiSearchResilienceOptions>()
    .Bind(builder.Configuration.GetSection(Sprk.Bff.Api.Configuration.AiSearchResilienceOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Resilient Search Client (for AI Search circuit breaker protection)
builder.Services.AddSingleton<Sprk.Bff.Api.Infrastructure.Resilience.IResilientSearchClient,
    Sprk.Bff.Api.Infrastructure.Resilience.ResilientSearchClient>();
Console.WriteLine("✓ Circuit breaker registry enabled");

var app = builder.Build();

// ---- Middleware Pipeline ----

// Cross-cutting: CORS
app.UseCors();
app.UseMiddleware<Sprk.Bff.Api.Api.SecurityHeadersMiddleware>();

// ============================================================================
// STATIC FILES - Serve playbook-builder SPA from wwwroot
// ============================================================================
// Serves static files from wwwroot/ directory. The playbook-builder React app
// is deployed to wwwroot/playbook-builder/ and accessed at /playbook-builder/
//
// Note: dotnet publish creates a wwwroot/ folder in the output, but Azure App Service
// deploys to site/wwwroot/ which IS the web root. This results in files being at
// site/wwwroot/wwwroot/ instead of site/wwwroot/. We handle this by adding a second
// static file provider that looks in the nested wwwroot folder.
app.UseStaticFiles();

// Handle Azure deployment where static files end up in nested wwwroot/wwwroot/
var nestedWwwroot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(nestedWwwroot))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(nestedWwwroot)
    });
}

// ============================================================================
// GLOBAL EXCEPTION HANDLER - RFC 7807 Problem Details
// ============================================================================
// Catches all unhandled exceptions and converts them to structured Problem Details JSON
// with correlation IDs for tracing. Must come early in pipeline to catch all errors.
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async ctx =>
    {
        var exception = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
        var traceId = ctx.TraceIdentifier;

        var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();

        // Map exception to Problem Details (status, code, title, detail)
        (int status, string code, string title, string detail) = exception switch
        {
            // SDAP validation/business logic errors
            SdapProblemException sp => (sp.StatusCode, sp.Code, sp.Title, sp.Detail ?? sp.Message),

            // MSAL OBO token acquisition failures
            MsalServiceException ms => (
                401,
                "obo_failed",
                "OBO Token Acquisition Failed",
                $"Failed to exchange user token for Graph API token: {ms.Message}"
            ),

            // Graph API errors (ODataError is the base exception type in Graph SDK v5)
            Microsoft.Graph.Models.ODataErrors.ODataError gs => (
                (int?)gs.ResponseStatusCode ?? 500,
                "graph_error",
                "Graph API Error",
                gs.Error?.Message ?? gs.Message
            ),

            // Unexpected errors
            _ => (
                500,
                "server_error",
                "Internal Server Error",
                "An unexpected error occurred. Please check correlation ID in logs."
            )
        };

        // Log the error with correlation ID
        logger.LogError(exception,
            "Request failed with {StatusCode} {Code}: {Detail} | CorrelationId: {CorrelationId}",
            status, code, detail, traceId);

        // Return RFC 7807 Problem Details response
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/problem+json";

        await ctx.Response.WriteAsJsonAsync(new
        {
            type = $"https://spaarke.com/errors/{code}",
            title,
            detail,
            status,
            extensions = new Dictionary<string, object?>
            {
                ["code"] = code,
                ["correlationId"] = traceId
            }
        });
    });
});

// ============================================================================
// AUTHENTICATION & AUTHORIZATION
// ============================================================================
// CRITICAL: Authentication must come before Authorization
app.UseAuthentication();
app.UseAuthorization();

// ============================================================================
// RATE LIMITING
// ============================================================================
// CRITICAL: Rate limiting must come after Authentication (to access User claims for partitioning)
app.UseRateLimiter();

// ---- Health Endpoints ----

// Health checks endpoints (anonymous for monitoring)
app.MapHealthChecks("/healthz").AllowAnonymous();

// Dataverse connection test endpoint
app.MapGet("/healthz/dataverse", TestDataverseConnectionAsync);

// Dataverse CRUD operations test endpoint
app.MapGet("/healthz/dataverse/crud", TestDataverseCrudOperationsAsync);

// Lightweight ping endpoint for warm-up agents (Task 021)
// Must be fast (<100ms), unauthenticated, and expose no sensitive info
app.MapGet("/ping", () => Results.Text("pong"))
    .AllowAnonymous()
    .WithTags("Health")
    .WithDescription("Lightweight health check for warm-up agents. Returns 'pong' without authentication.");

// Detailed status endpoint with service metadata
app.MapGet("/status", () =>
{
    return TypedResults.Json(new
    {
        service = "Sprk.Bff.Api",
        version = "1.0.0",
        timestamp = DateTimeOffset.UtcNow
    });
})
    .AllowAnonymous()
    .WithTags("Health")
    .WithDescription("Service status with metadata (no sensitive info).");

// ---- Endpoint Groups ----

// User identity and capabilities endpoints
app.MapUserEndpoints();

// Permissions endpoints (for UI to query user capabilities)
app.MapPermissionsEndpoints();

// Navigation metadata endpoints (Phase 7)
app.MapNavMapEndpoints();

// Dataverse document CRUD endpoints (Task 1.3)
app.MapDataverseDocumentsEndpoints();

// File access endpoints (SPE preview, download, Office viewer - Nov 2025 Microsoft guidance)
app.MapFileAccessEndpoints();

// Document and container management endpoints (SharePoint Embedded)
app.MapDocumentsEndpoints();

// Upload endpoints for file operations
app.MapUploadEndpoints();

// OBO endpoints (user-enforced CRUD)
app.MapOBOEndpoints();

// Document checkout/checkin/discard operations (document-checkout-viewer project)
app.MapDocumentOperationsEndpoints();

// Email-to-document conversion endpoints (Email-to-Document Automation project)
app.MapEmailEndpoints();

// Analysis endpoints (if enabled)
if (app.Configuration.GetValue<bool>("DocumentIntelligence:Enabled") &&
    app.Configuration.GetValue<bool>("Analysis:Enabled", true))
{
    app.MapAnalysisEndpoints();
    app.MapPlaybookEndpoints();
    app.MapScopeEndpoints();
    app.MapNodeEndpoints();
    app.MapPlaybookRunEndpoints();
}

// RAG endpoints for knowledge base operations (R3)
app.MapRagEndpoints();

// Visualization endpoints for document relationship discovery
app.MapVisualizationEndpoints();

// ============================================================================
// PLAYBOOK BUILDER SPA FALLBACK - Client-side routing support
// ============================================================================
// Catch-all for /playbook-builder/* routes that don't match static files.
// IMPORTANT: MapFallbackToFile creates a route that matches the pattern, so we need to
// ensure it doesn't match requests for static assets (.js, .css, .map, .svg, etc).
// The fallback only triggers for non-file paths (client-side routes).
app.MapFallback(context =>
{
    var path = context.Request.Path.Value ?? "";
    // Only serve index.html for /playbook-builder/* paths that are NOT static files
    if (path.StartsWith("/playbook-builder/", StringComparison.OrdinalIgnoreCase) &&
        !Path.HasExtension(path))
    {
        context.Request.Path = "/playbook-builder/index.html";
        return context.RequestServices.GetRequiredService<IWebHostEnvironment>()
            .WebRootFileProvider
            .GetFileInfo("playbook-builder/index.html")
            .Exists
            ? Results.File(
                Path.Combine(context.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootPath!, "playbook-builder/index.html"),
                "text/html").ExecuteAsync(context)
            : Results.NotFound().ExecuteAsync(context);
    }
    return Results.NotFound().ExecuteAsync(context);
});

// Resilience monitoring endpoints (circuit breaker status)
app.MapResilienceEndpoints();

// Record Matching endpoints (Phase 2 - only if enabled)
if (app.Configuration.GetValue<bool>("DocumentIntelligence:RecordMatchingEnabled"))
{
    app.MapRecordMatchEndpoints();
    app.MapRecordMatchingAdminEndpoints();
}

app.Run();

// Health check endpoint handlers
static async Task<IResult> TestDataverseConnectionAsync(IDataverseService dataverseService)
{
    try
    {
        var isConnected = await dataverseService.TestConnectionAsync();
        if (isConnected)
        {
            return TypedResults.Ok(new { status = "healthy", message = "Dataverse connection successful" });
        }
        else
        {
            return TypedResults.Problem(
                detail: "Dataverse connection test failed",
                statusCode: 503,
                title: "Service Unavailable"
            );
        }
    }
    catch (Exception ex)
    {
        return TypedResults.Problem(
            detail: ex.Message,
            statusCode: 503,
            title: "Dataverse Connection Error"
        );
    }
}

static async Task<IResult> TestDataverseCrudOperationsAsync(IDataverseService dataverseService)
{
    try
    {
        var testPassed = await dataverseService.TestDocumentOperationsAsync();
        if (testPassed)
        {
            return TypedResults.Ok(new { status = "healthy", message = "Dataverse CRUD operations successful" });
        }
        else
        {
            return TypedResults.Problem(
                detail: "Dataverse CRUD operations test failed",
                statusCode: 503,
                title: "Service Unavailable"
            );
        }
    }
    catch (Exception ex)
    {
        return TypedResults.Problem(
            detail: ex.Message,
            statusCode: 503,
            title: "Dataverse CRUD Test Error"
        );
    }
}

// expose Program for WebApplicationFactory in tests
public partial class Program
{
}
