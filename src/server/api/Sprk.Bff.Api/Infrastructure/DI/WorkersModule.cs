using Azure.Messaging.ServiceBus;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Workers.Office;

namespace Sprk.Bff.Api.Infrastructure.DI;

public static class WorkersModule
{
    public static IServiceCollection AddWorkersModule(this IServiceCollection services, IConfiguration configuration)
    {
        // NOTE: Job processing (ServiceBusJobProcessor, JobSubmissionService) is registered in Program.cs
        // This module registers shared services used by job handlers.

        // Register Service Bus client (only if connection string is configured)
        var serviceBusConnectionString = configuration.GetValue<string>("ServiceBus:ConnectionString");
        if (!string.IsNullOrWhiteSpace(serviceBusConnectionString))
        {
            services.AddSingleton(sp => new ServiceBusClient(serviceBusConnectionString));
        }

        // Register Office workers (upload finalization, profile summary, indexing)
        services.AddOfficeWorkers();


        // Register idempotency service for event deduplication (ADR-004)
        services.AddScoped<IIdempotencyService, IdempotencyService>();

        // Register batch job status store for tracking batch processing progress (Task 041)
        services.AddScoped<BatchJobStatusStore>();

        // Register DLQ service for viewing and re-driving dead-lettered messages (Task 043)
        services.AddScoped<DeadLetterQueueService>();

        return services;
    }
}
