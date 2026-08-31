using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for background job processing services (ADR-004, ADR-010).
/// Registers job handlers, Service Bus client, background services, and AI platform options.
/// </summary>
public static class JobProcessingModule
{
    /// <summary>
    /// Adds job submission, job handlers, Service Bus client, background hosted services,
    /// and AI platform foundation options.
    /// </summary>
    public static IServiceCollection AddJobProcessingModule(
        this IServiceCollection services,
        IConfiguration configuration,
        ILoggingBuilder logging)
    {
        // Job submission (unified entry point)
        services.AddSingleton<Sprk.Bff.Api.Services.Jobs.JobSubmissionService>();

        // Job handlers — split by feature gate.
        // Handlers that depend on AI/RAG services (IFileIndexingService, IOpenAiClient, SearchIndexClient)
        // must only register when DocumentIntelligence:Enabled=true, since those services are gated
        // there (see AnalysisServicesModule). Otherwise IJobHandler enumeration fails at startup.
        var documentIntelligenceEnabled = configuration.GetValue<bool>("DocumentIntelligence:Enabled");

        // Unconditional handlers (no AI dependencies)
        services.AddScoped<Sprk.Bff.Api.Services.Jobs.IJobHandler, Sprk.Bff.Api.Services.Jobs.Handlers.DocumentProcessingJobHandler>();
        // AI-coupled handlers relocated to Services/Ai/Jobs/ per task 051 (FR-E3); JobType strings unchanged
        services.AddScoped<Sprk.Bff.Api.Services.Jobs.IJobHandler, Sprk.Bff.Api.Services.Ai.Jobs.AppOnlyDocumentAnalysisJobHandler>();
        services.AddScoped<Sprk.Bff.Api.Services.Jobs.IJobHandler, Sprk.Bff.Api.Services.Ai.Jobs.EmailAnalysisJobHandler>();

        // Insights Engine Phase 1 — D-P8 SPE-upload consumer (task 050).
        // Zone B IJobHandler per SPEC §3.5 (Services/Jobs/Insights/) — injects
        // IInsightsAi only (the Zone-A → Zone-B facade) and routes JobType
        // "InsightsUniversalIngest" through the existing sdap-jobs queue.
        // Opt-in via AiProcessingOptions.InsightsIngest=true (Phase 1 default off);
        // queued from UploadFinalizationWorker.QueueNextStageAsync.
        services.AddScoped<Sprk.Bff.Api.Services.Jobs.IJobHandler, Sprk.Bff.Api.Services.Jobs.Insights.InsightsIngestJobHandler>();

        // AI-dependent handlers (require IFileIndexingService and/or IOpenAiClient)
        // Mixed handlers (RagIndexing references Dataverse) stay in Services/Jobs/Handlers/;
        // pure-AI handlers (ProfileSummary, BulkRagIndexing) relocated per task 051 (FR-E3)
        if (documentIntelligenceEnabled)
        {
            services.AddScoped<Sprk.Bff.Api.Services.Jobs.IJobHandler, Sprk.Bff.Api.Services.Jobs.Handlers.RagIndexingJobHandler>();
            services.AddScoped<Sprk.Bff.Api.Services.Jobs.IJobHandler, Sprk.Bff.Api.Services.Ai.Jobs.ProfileSummaryJobHandler>();
            services.AddScoped<Sprk.Bff.Api.Services.Jobs.IJobHandler, Sprk.Bff.Api.Services.Ai.Jobs.BulkRagIndexingJobHandler>();
        }

        // Service Bus client — THE single registration for the whole BFF (auth-v4 task 051 / FR-E2).
        //
        // Previously three modules each registered a ServiceBusClient singleton (WorkersModule at
        // Program.cs:75, OfficeWorkersModule at :124, and this one at :196). .NET DI resolves
        // last-registration-wins, so this was the only one anything ever received; the other two
        // were shadowed. They are now deleted and point here.
        //
        // Registration is UNCONDITIONAL and the credential decision is deferred to resolution time,
        // per ADR-032 / CLAUDE.md §10 F.1: the old shape gated registration on a credential being
        // present, so clearing the connection string would have silently un-registered the client
        // and every hosted service that injects it — the asymmetric-registration anti-pattern.
        // Failure now surfaces where it can be read (ServiceBusClientFactory.Create) instead of as
        // an unresolvable-dependency error naming a type nobody configured.
        services.AddSingleton(sp => Sprk.Bff.Api.Infrastructure.Auth.ServiceBusClientFactory.Create(
            sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value,
            sp.GetRequiredService<Azure.Core.TokenCredential>()));

        // Registered as a singleton FIRST, then handed to the hosted-service and health-check
        // pipelines, so all three share ONE instance. AddHostedService<T>() alone registers only
        // IHostedService->T, and AddCheck<T>() would then construct a SECOND, stateless T via
        // ActivatorUtilities — a health check that can never observe the processor's auth-failure
        // state. (RoutingConsumerTypeHealthCheck has that same shape and gets away with it only
        // because its check re-derives everything from scratch.)
        services.AddSingleton<Sprk.Bff.Api.Services.Jobs.ServiceBusJobProcessor>();
        services.AddHostedService(sp => sp.GetRequiredService<Sprk.Bff.Api.Services.Jobs.ServiceBusJobProcessor>());

        // auth-v4 task 051 (FR-E2): make "authorized but processing nothing" visible. Persistent
        // Service Bus authorization failure previously left /healthz returning 200 while the queue
        // silently stopped draining.
        services.AddHealthChecks()
            .AddCheck<Sprk.Bff.Api.Services.Jobs.ServiceBusJobProcessor>(
                "servicebus-job-processing",
                failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy,
                tags: new[] { "jobs", "servicebus", "auth" });

        // Background hosted services
        services.Configure<Sprk.Bff.Api.Services.Ai.Jobs.EmbeddingMigrationOptions>(
            configuration.GetSection(Sprk.Bff.Api.Services.Ai.Jobs.EmbeddingMigrationOptions.SectionName));
        services.AddHostedService<Sprk.Bff.Api.Services.Ai.Jobs.EmbeddingMigrationService>();

        services.Configure<Sprk.Bff.Api.Services.Jobs.ScheduledRagIndexingOptions>(
            configuration.GetSection(Sprk.Bff.Api.Services.Jobs.ScheduledRagIndexingOptions.SectionName));
        services.AddHostedService<Sprk.Bff.Api.Services.Jobs.ScheduledRagIndexingService>();

        // RecordSyncJob — incremental Dataverse to AI Search record sync (AIPU2-041)
        services.Configure<Sprk.Bff.Api.Services.Jobs.RecordSyncOptions>(
            configuration.GetSection(Sprk.Bff.Api.Services.Jobs.RecordSyncOptions.SectionName));
        services.AddHostedService<Sprk.Bff.Api.Services.Jobs.RecordSyncJob>();

        services.Configure<ReindexingOptions>(
            configuration.GetSection(ReindexingOptions.SectionName));

        // AI Platform Foundation Options
        services.Configure<LlamaParseOptions>(configuration.GetSection("LlamaParse"));
        services.Configure<AiSearchOptions>(configuration.GetSection("AiSearch"));

        logging.AddConsole();
        // D9-01 (config-deployment): removed 6 Console.WriteLine startup echoes that bypassed the
        // ILogger/OTel pipeline. These ran at service-registration time (pre-host-build) with no
        // ILogger available; the AddHostedService registrations above are the source of truth.

        return services;
    }
}
