using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.Tools;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Jobs.Handlers;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI module for the Communication Service (ADR-010: feature module pattern).
/// Registers communication services and configuration.
/// </summary>
public static class CommunicationModule
{
    public static IServiceCollection AddCommunicationModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind CommunicationOptions from "Communication" section
        services.Configure<CommunicationOptions>(configuration.GetSection(CommunicationOptions.SectionName));

        // Core services (singleton: all dependencies are singleton or options)
        services.AddSingleton<CommunicationAccountService>();
        services.AddSingleton<ApprovedSenderValidator>();
        services.AddSingleton<CommunicationService>();
        services.AddSingleton<EmlGenerationService>();
        services.AddSingleton<GraphMessageToEmlConverter>();
        services.AddSingleton<MailboxVerificationService>();
        services.AddSingleton<IncomingAssociationResolver>();
        services.AddSingleton<IncomingCommunicationProcessor>();

        // AI tool handler (IAiToolHandler — not auto-discovered by ToolFramework which scans IAnalysisToolHandler only)
        services.AddSingleton<SendCommunicationToolHandler>();

        // Job handler: processes incoming email notifications from Graph webhooks (Task 072)
        // Extracts message details from Graph, creates sprk_communication record, handles attachments.
        // JobType: "IncomingCommunication" — processed by dedicated CommunicationJobProcessor (not shared queue).
        // Registered as concrete type for direct resolution (no GetServices<IJobHandler> enumeration).
        services.AddScoped<IncomingCommunicationJobHandler>();

        // Background service: dedicated processor for the sdap-communication queue
        // Isolates email job processing from the shared sdap-jobs queue to prevent cross-domain failures.
        services.AddHostedService<CommunicationJobProcessor>();

        // Background service: manages Graph webhook subscriptions for inbound email monitoring (ADR-001)
        services.AddHostedService<GraphSubscriptionManager>();

        // Background service: backup polling for missed webhooks (ADR-001)
        services.AddHostedService<InboundPollingBackupService>();

        // Background service: reset daily send counts at midnight UTC (ADR-001)
        services.AddHostedService<DailySendCountResetService>();

        return services;
    }
}
