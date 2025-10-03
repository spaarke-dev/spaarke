using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Spe.Bff.Api.Configuration;
using Spe.Bff.Api.Services.BackgroundServices;
using Spe.Bff.Api.Services.Jobs;
using Spe.Bff.Api.Services.Jobs.Handlers;

namespace Spe.Bff.Api.Infrastructure.DI;

public static class WorkersModule
{
    public static IServiceCollection AddWorkersModule(this IServiceCollection services, IConfiguration configuration)
    {
        // NOTE: Job processing (JobProcessor, ServiceBusJobProcessor, JobSubmissionService) is registered in Program.cs
        // based on Jobs:UseServiceBus configuration. This module only handles DocumentEventProcessor.

        // Register Service Bus client for DocumentEventProcessor (only if connection string is configured)
        var serviceBusConnectionString = configuration.GetValue<string>("ServiceBus:ConnectionString");
        if (!string.IsNullOrWhiteSpace(serviceBusConnectionString))
        {
            services.AddSingleton(sp => new ServiceBusClient(serviceBusConnectionString));
        }

        // Register document event processor options
        services.Configure<DocumentEventProcessorOptions>(
            configuration.GetSection("DocumentEventProcessor"));

        // Register idempotency service for event deduplication (ADR-004)
        services.AddScoped<IIdempotencyService, IdempotencyService>();

        // Register document event processor background service (only if Service Bus is configured)
        if (!string.IsNullOrWhiteSpace(serviceBusConnectionString))
        {
            services.AddHostedService<DocumentEventProcessor>();
        }

        // Register document event handler
        services.AddScoped<IDocumentEventHandler, DocumentEventHandler>();

        return services;
    }
}
