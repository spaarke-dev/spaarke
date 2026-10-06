using Azure.Security.KeyVault.Secrets;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
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
///   2.  SecretClient            — Singleton  (shared Key Vault client — consumed by OTHER modules; see below)
///   3.  DataverseWebApiClient   — Singleton  (thread-safe REST client; used by SpeAuditService + SpeDashboardSyncService)
///   4.  SpeAdminGraphService    — Singleton  (Graph via IGraphClientFactory: BFF app-only identity + delegated OBO)
///   5.  SpeAuditService         — Scoped     (per-request, writes to sprk_speauditlog)
///   6.  SpeAdminTenantScope     — Scoped     (cross-customer boundary)
///   7.  SpeDashboardSyncService — Singleton + scheduled job (AddScheduledJob — ScheduledJobHost dispatches it;
///       ADR-036 / ADR-052; migrated from a hosted timer loop by unified-access-control-r2 task 165)
///   8.  BulkOperationService    — Singleton  (shared instance injected into bulk endpoints)
///   9.  BulkOperationService    — Hosted     (delegates to singleton instance for background execution)
///
/// SpeAdminTokenProvider (owning-app OBO with a Key Vault client secret) was removed 2026-10-04: it was
/// reachable only through an unused method, and SPE Admin no longer authenticates as owning apps at all.
/// </summary>
public static class SpeAdminModule
{
    public static IServiceCollection AddSpeAdminModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind SpeAdmin configuration from "SpeAdmin" section (appsettings.json).
        // task 061 fail-fast sweep: canonical AddOptions chain with ValidateOnStart. Behavior-neutral — the
        // only annotations are [Range] on DashboardSyncIntervalMinutes (15) and MaxContainersPerPage (100),
        // both in range by default, so an absent "SpeAdmin" section binds valid defaults and boots.
        services.AddOptions<SpeAdminOptions>()
            .Bind(configuration.GetSection(SpeAdminOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Azure Key Vault SecretClient — registered here for historical reasons and SHARED: the
        // credential provider (certificate fallback, AuthorizationModule) and ExternalAccessModule resolve
        // it. SpeAdminGraphService no longer uses it — it reads no secrets since 2026-10-04.
        // Singleton: SecretClient is thread-safe and designed for reuse.
        var keyVaultUri = configuration["SpeAdmin:KeyVaultUri"]
            ?? configuration["KeyVaultUri"]
            ?? throw new InvalidOperationException(
                "SpeAdmin:KeyVaultUri (or KeyVaultUri) configuration is required for SpeAdminModule.");

        // The credential MUST be pinned to the configured UAMI clientId. spaarke-bff-dev carries a
        // user-assigned identity and NO system-assigned one, so an unpinned DefaultAzureCredential has
        // no way to choose an identity and fails with "Unable to load the proper Managed Identity" —
        // precisely the failure ManagedIdentityCredentialFactory was created (2026-05-24) to prevent.
        //
        // This was the LAST bare `new DefaultAzureCredential()` in src/. It survived auth-v4's sweep
        // because auth-v4 scoped SpeAdmin out (ADR-028 E-1) — but E-1 covers the per-customer OWNING-APP
        // credentials this client goes on to fetch, NOT the BFF's own identity reaching Key Vault in
        // order to fetch them. Those are different identities at different layers; the exclusion was
        // applied one layer too wide. Consequence, measured in dev UAT 2026-08-25: every app-only SPE
        // Admin screen returned 500 (containers, search, recycle bin, security, dashboard sync) while
        // Container Types — the one screen on the delegated path, which never touches Key Vault —
        // rendered correctly. That split is the signature of this bug, not of a Graph problem.
        services.AddSingleton(_ => new SecretClient(
            new Uri(keyVaultUri),
            ManagedIdentityCredentialFactory.Create(configuration)));

        // Dataverse Web API REST client (pure HTTP, no System.ServiceModel dependency).
        // Singleton: thread-safe (SemaphoreSlim token refresh), reuses HttpClient connections.
        // Uses DefaultAzureCredential with ManagedIdentity. Distinct from IDataverseService
        // (SDK-based ServiceClient in GraphModule) — used here for direct REST POST to sprk_speauditlogs.
        services.AddSingleton<DataverseWebApiClient>();

        // SPE Admin Graph facade. Holds no credential: app-only work uses the BFF's own identity
        // (IGraphClientFactory.ForApp — the managed identity on Azure) and grant / container-type work is
        // delegated (IGraphClientFactory.ForUserAsync). Stateless singleton.
        // Full implementation: Infrastructure/Graph/SpeAdminGraphService.cs
        services.AddSingleton<SpeAdminGraphService>();

        // Per-request audit logging to sprk_speauditlog Dataverse table.
        // Scoped: captures HttpContext identity for the audit actor per request.
        services.AddScoped<SpeAuditService>();

        // Cross-customer boundary for the shared-BFF deployment model. Registered unconditionally:
        // SpeAdminTenantScopeFilter resolves it per request, and a missing registration would throw
        // at request time on every SPE Admin call rather than failing safe. Scoped, because it reads
        // the caller's identity and must never be shared across requests.
        services.AddScoped<SpeAdminTenantScope>();

        // Scheduled job: syncs dashboard metrics (container counts, storage usage per config) from Graph API into
        // IDistributedCache on a configurable interval (default 15 min).
        // Migrated to an IScheduledJob on ScheduledJobHost (ADR-036) by unified-access-control-r2 task 165, which changed
        // its behaviour (per-config storage, per-container attribution — owner round 25 item 5); ADR-052 §1 migrates a
        // timer service when it is next touched. One run per schedule across instances (distributed lease). The
        // singleton AddScheduledJob registers is the instance the dashboard endpoints read the cache through.
        var speAdminOptions = configuration.GetSection(SpeAdminOptions.SectionName).Get<SpeAdminOptions>() ?? new SpeAdminOptions();
        services.AddScheduledJob<SpeDashboardSyncService>(SpeDashboardSyncService.BuildCronSchedule(speAdminOptions));

        // Background service: processes bulk container operations (delete, permission assignment).
        // Runs in the BFF as a BackgroundService, governed by ADR-052.
        //
        // Registered as Singleton first so bulk endpoints can inject the same instance
        // to call EnqueueDelete / EnqueuePermissions / GetStatus. The hosted service
        // registration delegates to the singleton via factory lambda.
        services.AddSingleton<BulkOperationService>();
        services.AddHostedService(sp => sp.GetRequiredService<BulkOperationService>());

        return services;
    }
}
