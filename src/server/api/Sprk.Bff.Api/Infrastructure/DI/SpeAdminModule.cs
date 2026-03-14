using Azure.Security.KeyVault.Secrets;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.SpeAdmin;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI module for the SPE Admin application (ADR-010: feature module pattern).
/// Registers all SPE Admin-related services: Graph client, audit logging,
/// dashboard background sync, bulk operation processing, and configuration options.
///
/// Non-framework DI registrations: 10 (within ADR-010 ≤15 limit)
///   1.  SpeAdminOptions         — Configure  (options pattern, bound from "SpeAdmin" section)
///   2.  SecretClient            — Singleton  (Azure Key Vault client for fetching Graph credentials)
///   3.  DataverseWebApiClient   — Singleton  (thread-safe REST client; used by SpeAuditService + SpeDashboardSyncService)
///   4.  SpeAdminTokenProvider   — Singleton  (Phase 3: OBO token acquisition and per-app caching)
///   5.  SpeAdminGraphService    — Singleton  (multi-config Graph client; app-only + OBO via TokenProvider)
///   6.  SpeAuditService         — Scoped     (per-request, writes to sprk_speauditlog)
///   7.  SpeDashboardSyncService — Singleton  (shared instance injected into dashboard endpoints)
///   8.  SpeDashboardSyncService — Hosted     (delegates to singleton instance for background execution)
///   9.  BulkOperationService    — Singleton  (shared instance injected into bulk endpoints)
///   10. BulkOperationService    — Hosted     (delegates to singleton instance for background execution)
/// </summary>
public static class SpeAdminModule
{
    public static IServiceCollection AddSpeAdminModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind SpeAdmin configuration from "SpeAdmin" section (appsettings.json).
        // Phase 1 uses app-only tokens; Phase 3 adds OBO via SpeAdminTokenProvider.
        services.Configure<SpeAdminOptions>(configuration.GetSection(SpeAdminOptions.SectionName));

        // Azure Key Vault SecretClient — used by SpeAdminGraphService to fetch per-config client secrets.
        // Singleton: SecretClient is thread-safe and designed for reuse.
        // Uses DefaultAzureCredential (managed identity in Azure, environment variables in dev).
        var keyVaultUri = configuration["SpeAdmin:KeyVaultUri"]
            ?? configuration["KeyVaultUri"]
            ?? throw new InvalidOperationException(
                "SpeAdmin:KeyVaultUri (or KeyVaultUri) configuration is required for SpeAdminModule.");

        services.AddSingleton(_ => new SecretClient(new Uri(keyVaultUri), new Azure.Identity.DefaultAzureCredential()));

        // Dataverse Web API REST client (pure HTTP, no System.ServiceModel dependency).
        // Singleton: thread-safe (SemaphoreSlim token refresh), reuses HttpClient connections.
        // Uses DefaultAzureCredential with ManagedIdentity. Distinct from IDataverseService
        // (SDK-based ServiceClient in GraphModule) — used here for direct REST POST to sprk_speauditlogs.
        services.AddSingleton<DataverseWebApiClient>();

        // Phase 3: OBO token provider — acquires per-owning-app tokens via MSAL OBO exchange.
        // Singleton: stateless except for the thread-safe OBO token cache and MSAL app cache.
        // Injected optionally into SpeAdminGraphService to enable multi-app scenarios.
        // Single-app configs continue to use app-only tokens without this provider.
        services.AddSingleton<SpeAdminTokenProvider>();

        // Multi-config SPE Graph client (app-only + OBO for multi-app configs).
        // Singleton: stateless except for the in-memory client caches (ConcurrentDictionary + TTL).
        // Resolves Graph credentials from Dataverse + Key Vault; caches GraphServiceClient by configId.
        // Full implementation: Infrastructure/Graph/SpeAdminGraphService.cs
        services.AddSingleton<SpeAdminGraphService>();

        // Per-request audit logging to sprk_speauditlog Dataverse table.
        // Scoped: captures HttpContext identity for the audit actor per request.
        services.AddScoped<SpeAuditService>();

        // Background service: syncs dashboard metrics (container counts, storage usage)
        // from Graph API into IDistributedCache on a configurable interval (default 15 min).
        // ADR-001: BackgroundService, not Azure Functions.
        //
        // Registered as Singleton first so the same instance can be injected into dashboard
        // endpoints (for ReadCachedMetricsAsync and TriggerRefreshAsync). The hosted service
        // registration delegates to the singleton instance via factory lambda — ensuring both
        // the endpoint injection and the background runner share the same object.
        services.AddSingleton<SpeDashboardSyncService>();
        services.AddHostedService(sp => sp.GetRequiredService<SpeDashboardSyncService>());

        // Background service: processes bulk container operations (delete, permission assignment).
        // ADR-001: BackgroundService, not Azure Functions.
        //
        // Registered as Singleton first so bulk endpoints can inject the same instance
        // to call EnqueueDelete / EnqueuePermissions / GetStatus. The hosted service
        // registration delegates to the singleton via factory lambda.
        services.AddSingleton<BulkOperationService>();
        services.AddHostedService(sp => sp.GetRequiredService<BulkOperationService>());

        return services;
    }
}
