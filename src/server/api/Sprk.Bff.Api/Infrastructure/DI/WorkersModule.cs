using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;

namespace Sprk.Bff.Api.Infrastructure.DI;

public static class WorkersModule
{
    public static IServiceCollection AddWorkersModule(this IServiceCollection services, IConfiguration configuration)
    {
        // NOTE: Job processing (ServiceBusJobProcessor, JobSubmissionService) is registered in Program.cs
        // This module only handles DocumentEventProcessor.

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

        // Register batch job status store for tracking batch processing progress (Task 041)
        services.AddScoped<BatchJobStatusStore>();

        // Register DLQ service for viewing and re-driving dead-lettered messages (Task 043)
        services.AddScoped<DeadLetterQueueService>();

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
