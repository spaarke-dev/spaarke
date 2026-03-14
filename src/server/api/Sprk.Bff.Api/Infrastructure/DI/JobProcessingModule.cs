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

        // Job handlers
        services.AddScoped<Sprk.Bff.Api.Services.Jobs.IJobHandler, Sprk.Bff.Api.Services.Jobs.Handlers.DocumentProcessingJobHandler>();
        services.AddScoped<Sprk.Bff.Api.Services.Jobs.IJobHandler, Sprk.Bff.Api.Services.Jobs.Handlers.AppOnlyDocumentAnalysisJobHandler>();
        services.AddScoped<Sprk.Bff.Api.Services.Jobs.IJobHandler, Sprk.Bff.Api.Services.Jobs.Handlers.EmailAnalysisJobHandler>();
        services.AddScoped<Sprk.Bff.Api.Services.Jobs.IJobHandler, Sprk.Bff.Api.Services.Jobs.Handlers.RagIndexingJobHandler>();
        services.AddScoped<Sprk.Bff.Api.Services.Jobs.IJobHandler, Sprk.Bff.Api.Services.Jobs.Handlers.ProfileSummaryJobHandler>();
        services.AddScoped<Sprk.Bff.Api.Services.Jobs.IJobHandler, Sprk.Bff.Api.Services.Jobs.Handlers.BulkRagIndexingJobHandler>();

        // Service Bus client
        var serviceBusConnectionString = configuration.GetConnectionString("ServiceBus");
        if (string.IsNullOrWhiteSpace(serviceBusConnectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:ServiceBus is required. " +
                "For local development, use Service Bus emulator (see docs/README-Local-Development.md) " +
                "or configure a dev Service Bus namespace.");
        }

        services.AddSingleton(sp => new Azure.Messaging.ServiceBus.ServiceBusClient(serviceBusConnectionString));
        services.AddHostedService<Sprk.Bff.Api.Services.Jobs.ServiceBusJobProcessor>();

        // Background hosted services
        services.Configure<Sprk.Bff.Api.Services.Jobs.DocumentVectorBackfillOptions>(
            configuration.GetSection(Sprk.Bff.Api.Services.Jobs.DocumentVectorBackfillOptions.SectionName));
        services.AddHostedService<Sprk.Bff.Api.Services.Jobs.DocumentVectorBackfillService>();

        services.Configure<Sprk.Bff.Api.Services.Jobs.EmbeddingMigrationOptions>(
            configuration.GetSection(Sprk.Bff.Api.Services.Jobs.EmbeddingMigrationOptions.SectionName));
        services.AddHostedService<Sprk.Bff.Api.Services.Jobs.EmbeddingMigrationService>();

        services.Configure<Sprk.Bff.Api.Services.Jobs.ScheduledRagIndexingOptions>(
            configuration.GetSection(Sprk.Bff.Api.Services.Jobs.ScheduledRagIndexingOptions.SectionName));
        services.AddHostedService<Sprk.Bff.Api.Services.Jobs.ScheduledRagIndexingService>();

        services.Configure<ReindexingOptions>(
            configuration.GetSection(ReindexingOptions.SectionName));

        // AI Platform Foundation Options
        services.Configure<LlamaParseOptions>(configuration.GetSection("LlamaParse"));
        services.Configure<AiSearchOptions>(configuration.GetSection("AiSearch"));

        logging.AddConsole();
        Console.WriteLine("\u2713 Job processing configured with Service Bus (queue: sdap-jobs)");
        Console.WriteLine("\u2713 Email polling backup service configured");
        Console.WriteLine("\u2713 Document vector backfill service registered (enable via config)");
        Console.WriteLine("\u2713 Embedding migration service registered (enable via config)");
        Console.WriteLine("\u2713 Scheduled RAG indexing service registered (enable via config)");

        return services;
    }
}
