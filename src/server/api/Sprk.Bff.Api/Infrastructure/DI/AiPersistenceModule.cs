using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Services.Ai.Audit;
using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Services.Ai.PromptLibrary;
using Sprk.Bff.Api.Services.Ai.Sessions;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for AI Persistence services (ADR-010: feature module pattern).
/// </summary>
/// <remarks>
/// Registers the Cosmos DB persistence services introduced in Spaarke AI Platform Unification R2.
/// All stores use write-through Cosmos DB (decision D-06: no idle-flush).
///
/// UNCONDITIONAL registrations:
///   1. CosmosClient               — Singleton; uses DefaultAzureCredential (no connection strings)
///   2. SessionPersistenceService  — Scoped; Redis + Cosmos DB dual-write (AIPU2-030)
///
/// Planned (registered by future AIPU2 tasks):
///   3. CosmosPromptStore    — Prompt and completion audit log
///   4. CosmosAuditStore     — Safety evaluation audit records
///   5. CosmosMemoryStore    — Long-term semantic memory for agents
///   6. CosmosFeedbackStore  — User feedback and thumbs-up/down records
///
/// Prerequisites (must already be registered before calling AddAiPersistenceModule):
///   - <c>IConfiguration</c>  — registered by the host
///   - <c>IDistributedCache</c> — registered by <c>AddCacheModule</c> (Redis or in-memory)
///   - <c>ILogger&lt;T&gt;</c> — registered via <c>AddLogging</c> (implicit in WebApplication.CreateBuilder)
///
/// Required configuration keys:
///   - <c>CosmosPersistence:Endpoint</c>    — Cosmos DB account endpoint URI
///   - <c>CosmosPersistence:DatabaseName</c> — Target database name
///
/// Usage in Program.cs:
/// <code>
/// builder.Services.AddAiPersistenceModule(builder.Configuration);
/// </code>
/// </remarks>
public static class AiPersistenceModule
{
    /// <summary>
    /// Registers AI Persistence (Cosmos DB) services with the DI container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration (Cosmos DB endpoint and database name).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAiPersistenceModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var endpoint = configuration["CosmosPersistence:Endpoint"]
            ?? throw new InvalidOperationException(
                "CosmosPersistence:Endpoint is not configured. " +
                "Add this setting to appsettings.json or Azure App Service configuration.");

        // CosmosClient: singleton — thread-safe, manages connection pool internally.
        // DefaultAzureCredential: no connection strings in code or config (ADR-015).
        // SerializerOptions: use System.Text.Json for consistency with the rest of the BFF.
        services.AddSingleton(_ =>
        {
            var credential = new DefaultAzureCredential();
            return new CosmosClientBuilder(endpoint, credential)
                .WithSerializerOptions(new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                })
                .WithConnectionModeDirect()
                .WithThrottlingRetryOptions(maxRetryWaitTimeOnThrottledRequests: TimeSpan.FromSeconds(30), maxRetryAttemptsOnThrottledRequests: 9)
                .Build();
        });

        // SessionPersistenceService: scoped — one instance per HTTP request.
        // Dual-write: Redis (hot, 24h TTL) + Cosmos DB sessions container (warm, 90-day retention).
        // ADR-015 Tier 3; ADR-009 Redis-first; D-06 write-through.
        services.AddScoped<ISessionPersistenceService, SessionPersistenceService>();

        // AIPU2-033: AuditLogService — append-only compliance log (ADR-015 Tier 2, 7-year retention).
        // Singleton: CosmosClient and Container are thread-safe and designed for long-lived reuse.
        // Reads CosmosPersistence:DatabaseName; defaults to "spaarke-ai" if not configured.
        var databaseName = configuration["CosmosPersistence:DatabaseName"] ?? "spaarke-ai";
        services.AddSingleton<IAuditLogService>(sp => new AuditLogService(
            cosmosClient: sp.GetRequiredService<CosmosClient>(),
            databaseName: databaseName,
            logger: sp.GetRequiredService<ILogger<AuditLogService>>()));

        // AIPU2-034: MatterMemoryService — per-matter structured AI memory (ADR-015 Tier 3, GDPR erasure supported).
        // Scoped: CosmosClient is thread-safe singleton; MatterMemoryService reads ETag per request.
        // Uses the same CosmosClient singleton and databaseName resolved above.
        services.AddScoped<IMatterMemoryService>(sp => new MatterMemoryService(
            cosmosClient: sp.GetRequiredService<CosmosClient>(),
            databaseName: databaseName,
            logger: sp.GetRequiredService<ILogger<MatterMemoryService>>()));

        // AIPU2-035: PromptLibraryService — Personal + Team template CRUD (Cosmos DB prompts container).
        // Scoped: one instance per HTTP request; shares the singleton CosmosClient.
        // Org + System template tiers are deferred to AIPU2-036 (Dataverse integration).
        services.AddScoped<IPromptLibraryService, PromptLibraryService>();

        // TODO AIPU2-xxx: Register CosmosFeedbackStore

        return services;
    }
}
