using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Workers.Office;

namespace Sprk.Bff.Api.Infrastructure.DI;

public static class WorkersModule
{
    public static IServiceCollection AddWorkersModule(this IServiceCollection services, IConfiguration configuration)
    {
        // NOTE: Job processing (ServiceBusJobProcessor, JobSubmissionService) is registered in Program.cs
        // This module registers shared services used by job handlers.

        // ServiceBusClient is NOT registered here (auth-v4 task 051 / FR-E2).
        //
        // This module used to register one, gated on ServiceBus:ConnectionString being non-empty.
        // Two problems, both now fixed at the single canonical site in JobProcessingModule:
        //   1. It was DEAD. JobProcessingModule registers the same singleton later in Program.cs
        //      (:196 vs :75), and last-registration-wins, so nothing ever resolved this instance.
        //      It read a DIFFERENT config key (ServiceBus:ConnectionString) than the one that
        //      actually took effect (ConnectionStrings:ServiceBus), which is how the two-key
        //      fan-out survived unnoticed.
        //   2. Gating a registration on a credential's presence is the ADR-032 asymmetric-
        //      registration anti-pattern (CLAUDE.md §10 F.1) — removing the credential silently
        //      un-registers the service instead of failing where an operator can read it.

        // Register Office workers (upload finalization, profile summary, indexing)
        services.AddOfficeWorkers(configuration);


        // Register idempotency service for event deduplication (ADR-004)
        services.AddScoped<IIdempotencyService, IdempotencyService>();

        // Register batch job status store for tracking batch processing progress (Task 041)
        services.AddScoped<BatchJobStatusStore>();

        // Register DLQ service for viewing and re-driving dead-lettered messages (Task 043)
        services.AddScoped<DeadLetterQueueService>();

        return services;
    }
}
