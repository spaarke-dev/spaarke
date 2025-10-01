using Azure.Messaging.ServiceBus;
using Spe.Bff.Api.Services.BackgroundServices;
using Spe.Bff.Api.Services.Jobs;
using Spe.Bff.Api.Services.Jobs.Handlers;

namespace Spe.Bff.Api.Infrastructure.DI;

public static class WorkersModule
{
    public static IServiceCollection AddWorkersModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Register job processor background service (for in-memory job processing)
        services.AddHostedService<JobProcessor>();

        // Register job handlers (scan for IJobHandler implementations)
        var assembly = typeof(WorkersModule).Assembly;
        var handlerTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IJobHandler).IsAssignableFrom(t));

        foreach (var handlerType in handlerTypes)
        {
            services.AddScoped(typeof(IJobHandler), handlerType);
        }

        // Register Service Bus document event processing
        var serviceBusConnectionString = configuration.GetConnectionString("ServiceBus");
        if (!string.IsNullOrEmpty(serviceBusConnectionString))
        {
            // Register Service Bus client
            services.AddSingleton(provider =>
            {
                return new ServiceBusClient(serviceBusConnectionString);
            });

            // Register document event processor options
            services.Configure<DocumentEventProcessorOptions>(
                configuration.GetSection("DocumentEventProcessor"));

            // Register idempotency service for event deduplication (ADR-004)
            services.AddScoped<IIdempotencyService, IdempotencyService>();

            // Register document event processor background service
            services.AddHostedService<DocumentEventProcessor>();

            // Register document event handler
            services.AddScoped<IDocumentEventHandler, DocumentEventHandler>();
        }

        return services;
    }
}