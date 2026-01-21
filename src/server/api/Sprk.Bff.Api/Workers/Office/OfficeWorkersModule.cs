using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

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
    /// - (Future) ProfileWorker: AI summary generation
    /// - (Future) IndexingWorker: Search indexing
    /// </para>
    /// </remarks>
    public static IServiceCollection AddOfficeWorkers(this IServiceCollection services)
    {
        // Register job handlers as singleton (stateless handlers)
        services.AddSingleton<IOfficeJobHandler, UploadFinalizationWorker>();

        // Register the background service
        services.AddHostedService<UploadFinalizationWorker>(sp =>
        {
            // Resolve the same instance registered as IOfficeJobHandler
            var handlers = sp.GetServices<IOfficeJobHandler>();
            return handlers.OfType<UploadFinalizationWorker>().First();
        });

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

        // Register Service Bus client as singleton
        services.AddSingleton<ServiceBusClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ServiceBusOptions>>();

            if (string.IsNullOrEmpty(options.Value.ConnectionString))
            {
                throw new InvalidOperationException(
                    "ServiceBus:ConnectionString is required for Office workers");
            }

            return new ServiceBusClient(options.Value.ConnectionString);
        });

        return services;
    }
}
