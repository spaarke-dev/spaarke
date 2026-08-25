using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Services.Ai.Audit;
using Sprk.Bff.Api.Services.Ai.Feedback;
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
    /// The System.Text.Json options the CosmosClient serializes EVERY AI-persistence document with
    /// (sessions, memory-items, memory/pins, audit, prompts, feedback). Exposed so tests assert the
    /// EXACT production serializer behavior rather than raw <c>JsonSerializer</c> defaults.
    ///
    /// <para><b>Why STJ, and why <see cref="JsonIgnoreCondition.WhenWritingNull"/> is load-bearing:</b>
    /// the models carry <c>System.Text.Json</c> attributes (<c>[JsonPropertyName]</c>,
    /// <c>[JsonIgnore(WhenWritingNull)]</c>). The Cosmos SDK's DEFAULT serializer (configured via
    /// <c>WithSerializerOptions(CosmosSerializationOptions)</c>) is Newtonsoft-based and IGNORES those
    /// STJ attributes — it happened to produce the right camelCase names by convention but wrote
    /// <c>null</c>-valued properties. That silently emitted <c>"ttl": null</c> for every UNFILED
    /// session (<see cref="Sessions.StoredSession.Ttl"/>) and every retention-classless memory item
    /// (<see cref="Memory.MemoryItemDocument.Ttl"/>). Because the <c>sessions</c> and <c>memory-items</c>
    /// containers have TTL ENABLED, Cosmos rejects <c>ttl: null</c> with HTTP 400 — and the write path
    /// swallows it at Warning, so History + memory writes silently stopped landing (dev: 2026-08-07).
    /// Serializing with STJ + <see cref="JsonIgnoreCondition.WhenWritingNull"/> honors the models'
    /// intent (null optional fields are OMITTED, not written as <c>null</c>), fixing the whole class
    /// across every container. No enums / custom converters exist on the persisted models, and every
    /// field has an explicit <c>[JsonPropertyName]</c>, so existing documents round-trip byte-for-byte.
    /// </para>
    /// </summary>
    internal static readonly JsonSerializerOptions CosmosJsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

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
        // TokenCredential (UAMI-pinned) injected from DI singleton; no connection strings (ADR-015).
        // Serializer: System.Text.Json via WithSystemTextJsonSerializerOptions (NOT the SDK default
        // Newtonsoft serializer) so the models' [JsonPropertyName] / [JsonIgnore(WhenWritingNull)]
        // attributes are HONORED — critically, null optional fields (e.g. an unfiled session's ttl)
        // are OMITTED instead of written as "null", which a TTL-enabled container rejects with HTTP 400.
        // See CosmosJsonSerializerOptions above for the full incident rationale (dev write-stoppage 2026-08-07).
        services.AddSingleton(sp =>
        {
            var credential = sp.GetRequiredService<TokenCredential>();
            return new CosmosClientBuilder(endpoint, credential)
                .WithSystemTextJsonSerializerOptions(CosmosJsonSerializerOptions)
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

        // AIPU2-034 → AIR2-050: MemoryItemStore — generalized Record (entityType, entityId) + User
        // (userId) structured AI memory over the NEW subject-partitioned `memory-items` container
        // (ADR-015 Tier 3, GDPR erasure supported; FR-B-01). Replaces the retired matter-only
        // MatterMemoryService; legacy `memory`-container docs are left in place (fresh-container
        // ruling 2026-07-09 — that container is shared with pins + workspace tabs).
        // Scoped: CosmosClient is thread-safe singleton; the store reads ETags per request.
        // AIR2-052: the store now also emits a Tier-2 memory-WRITE audit event via the EXISTING
        // IAuditLogService (NFR-07, identifiers/counts only — no new audit component). Registered above.
        services.AddScoped<IMemoryItemStore>(sp => new MemoryItemStore(
            cosmosClient: sp.GetRequiredService<CosmosClient>(),
            databaseName: databaseName,
            logger: sp.GetRequiredService<ILogger<MemoryItemStore>>(),
            auditLog: sp.GetRequiredService<IAuditLogService>()));

        // compose-r2 FR-30 (#629): IComposeMemoryCapture — canonical ADR-013 facade that captures durable
        // Record-scope insights (defined terms today) distilled from a Compose session into the SHARED
        // IMemoryItemStore above. No forked store; the untrusted-origin gate (TrustLevel) is DEFERRED to
        // the memory-governance project (#629), carried inert. Scoped: the store it delegates to is Scoped.
        services.AddScoped<Sprk.Bff.Api.Services.Ai.PublicContracts.IComposeMemoryCapture,
            Sprk.Bff.Api.Services.Ai.PublicContracts.ComposeMemoryCapture>();

        // FR-08 (task 031): IPreferenceMemoryCapture — the E3 feedback→memory seam. The canonical ADR-013
        // CRUD-safe facade through which FeedbackService persists a governed per-user `Preference` memory
        // item (task 030 fact type) into the SHARED IMemoryItemStore above. No forked store, no second
        // memory write path (§11). Resolves the caller's AAD oid → canonical Dataverse systemuserid via the
        // Singleton ISystemUserIdentityResolver (registered in NotificationsModule) so the preference recalls
        // under the same key chat-side user memory uses. Per-user ONLY: never mutates the ADR-039 global
        // catalog; `trustLevel` carried inert (#616). Scoped: the store it delegates to is Scoped.
        services.AddScoped<Sprk.Bff.Api.Services.Ai.PublicContracts.IPreferenceMemoryCapture,
            Sprk.Bff.Api.Services.Ai.PublicContracts.PreferenceMemoryCapture>();

        // AIR2-052: memory-governance authorization port (FR-B-03). Thin seam over the existing
        // IDataversePrivilegeChecker (record-read alignment — caller-derived, no parallel ACL) +
        // NotificationService (AAD oid → systemuserid). Scoped: both dependencies are Singletons, so
        // Scoped is safe; per-request usage from the governance endpoints.
        services.AddScoped<IMemoryAccessAuthorizer, MemoryAccessAuthorizer>();

        // AIPU2-035: PromptLibraryService — Personal + Team template CRUD (Cosmos DB prompts container).
        // Scoped: one instance per HTTP request; shares the singleton CosmosClient.
        // Org + System template tiers are deferred to AIPU2-036 (Dataverse integration).
        services.AddScoped<IPromptLibraryService, PromptLibraryService>();

        // AIPU2-036: FeedbackService — per-response thumbs up/down storage and aggregation.
        // Scoped: one instance per HTTP request; shares the singleton CosmosClient.
        // Cosmos DB feedback container, partition key /tenantId (ADR-015 Tier 3, 90-day retention).
        services.AddScoped<IFeedbackService, FeedbackService>();

        // AIPU2-031: SessionRestoreService — loads a persisted session, checks Dataverse entity
        // staleness via parallel ETag comparisons, and reconstructs the LLM context window.
        // Scoped: depends on ISessionPersistenceService (scoped); one instance per HTTP request.
        // IHttpClientFactory is registered by the host (AddHttpClient); HttpClient is not injected
        // directly to allow per-call header isolation (bearer token set per request).
        services.AddScoped<ISessionRestoreService, SessionRestoreService>();

        // AIPU2-032: SessionSummarizationService — GPT-4o summarization at 25-message / 8K-token threshold.
        // Scoped: IChatClient is singleton/thread-safe; scoped lifetime consistent with this module's
        // other per-request services. IChatClient is registered by AddAiModule (prerequisite).
        // The summary is written alongside verbatim messages — no messages are ever deleted.
        services.AddScoped<ISessionSummarizationService, SessionSummarizationService>();

        return services;
    }
}
