namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration module for AI Persistence services (ADR-010: feature module pattern).
/// </summary>
/// <remarks>
/// Registers the Cosmos DB persistence services introduced in Spaarke AI Platform Unification R2.
/// All stores use write-through Cosmos DB (decision D-06: no idle-flush).
///
/// UNCONDITIONAL registrations (planned — registered by future AIPU2 tasks):
///   1. CosmosSessionStore   — Durable AI session storage (write-through, ADR-015 governed)
///   2. CosmosPromptStore    — Prompt and completion audit log
///   3. CosmosAuditStore     — Safety evaluation audit records
///   4. CosmosMemoryStore    — Long-term semantic memory for agents
///   5. CosmosFeedbackStore  — User feedback and thumbs-up/down records
///
/// Prerequisites (must already be registered before calling AddAiPersistenceModule):
///   - <c>IConfiguration</c>  — registered by the host
///   - <c>ILogger&lt;T&gt;</c> — registered via <c>AddLogging</c> (implicit in WebApplication.CreateBuilder)
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
    /// <param name="configuration">Application configuration (Cosmos DB endpoint and key).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAiPersistenceModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // TODO AIPU2-xxx: Register CosmosSessionStore, CosmosPromptStore, CosmosAuditStore, CosmosMemoryStore, CosmosFeedbackStore

        return services;
    }
}
