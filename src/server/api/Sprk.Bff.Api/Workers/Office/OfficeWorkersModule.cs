using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Email;

namespace Sprk.Bff.Api.Workers.Office;

/// <summary>
/// Extension methods for registering Office worker services.
/// </summary>
public static class OfficeWorkersModule
{
    /// <summary>
    /// Adds Office worker services to the service collection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per ADR-001, workers use BackgroundService (not Azure Functions).
    /// Per ADR-010, we minimize DI registrations and use concretes.
    /// </para>
    /// <para>
    /// Registered workers:
    /// - UploadFinalizationWorker: Processes file uploads, creates records
    /// - ProfileSummaryWorker: AI summary generation via IAppOnlyAnalysisService
    /// - IndexingWorkerHostedService: RAG indexing via IFileIndexingService
    /// </para>
    /// </remarks>
    public static IServiceCollection AddOfficeWorkers(this IServiceCollection services, IConfiguration configuration)
    {
        // Register worker dependencies
        // NOTE: IEmailToEmlConverter is registered (Scoped) in EmailServicesModule
        // (Infrastructure/DI) as the single canonical registration. The former Singleton
        // registration here was removed (code-quality-and-assurance-r3 task 022) — it
        // conflicted with the Scoped one, which the worker resolves per-operation from a
        // created scope in ProcessEmailAttachmentsAsync.
        services.AddSingleton<AttachmentFilterService>();

        // Register job handlers as singleton (stateless handlers)
        services.AddSingleton<IOfficeJobHandler, UploadFinalizationWorker>();
        services.AddSingleton<IOfficeJobHandler, ProfileSummaryWorker>();

        // Register the background services
        // UploadFinalizationWorker: Processes office-upload-finalization queue
        services.AddHostedService<UploadFinalizationWorker>(sp =>
        {
            // Resolve the same instance registered as IOfficeJobHandler
            var handlers = sp.GetServices<IOfficeJobHandler>();
            return handlers.OfType<UploadFinalizationWorker>().First();
        });

        // ProfileSummaryWorker: Processes office-profile queue
        services.AddHostedService<ProfileSummaryWorker>(sp =>
        {
            var handlers = sp.GetServices<IOfficeJobHandler>();
            return handlers.OfType<ProfileSummaryWorker>().First();
        });

        // IndexingWorkerHostedService: Processes office-indexing queue
        // Integrates with IFileIndexingService for RAG document indexing.
        // Depends on IFileIndexingService — gated on DocumentIntelligence:Enabled
        // (see AnalysisServicesModule.AddRagServices). Register conditionally so the host
        // does not crash at startup when AI is disabled.
        if (configuration.GetValue<bool>("DocumentIntelligence:Enabled"))
        {
            services.AddHostedService<IndexingWorkerHostedService>();
        }

        return services;
    }

    /// <summary>
    /// Adds Service Bus client for Office workers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    public static IServiceCollection AddOfficeServiceBus(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind Service Bus options
        services.Configure<ServiceBusOptions>(
            configuration.GetSection("ServiceBus"));

        // ServiceBusClient is NOT registered here (auth-v4 task 051 / FR-E2).
        //
        // This registration was shadowed: JobProcessingModule registers the same singleton later
        // in Program.cs (:196 vs :124) and last-registration-wins, so Office workers already
        // received that client, not this one. Its "ConnectionString is required for Office
        // workers" guard therefore never fired, and would have blocked the managed-identity
        // cutover if it had. The single canonical registration lives in JobProcessingModule and
        // routes through ServiceBusClientFactory, which accepts namespace + managed identity.

        return services;
    }
}
