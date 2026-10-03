using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Core.Cache;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Caching;
using Sprk.Bff.Api.Infrastructure.Resilience;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Infrastructure.DI;

public static class SpaarkeCore
{
    public static IServiceCollection AddSpaarkeCore(this IServiceCollection services)
    {
        // Don't add authorization here since it's already in Program.cs

        // SDAP Authorization services
        // Register both concrete and interface for compatibility:
        // - Concrete: Used by DocumentAuthorizationFilter and ResourceAccessHandler
        // - Interface: Used by legacy code paths
        services.AddScoped<Spaarke.Core.Auth.AuthorizationService>();
        services.AddScoped<Spaarke.Core.Auth.IAuthorizationService>(sp => sp.GetRequiredService<Spaarke.Core.Auth.AuthorizationService>());

        // AI Authorization service (FullUAC mode)
        // Used by AiAuthorizationFilter and AnalysisAuthorizationFilter for document access checks
        services.AddScoped<IAiAuthorizationService, AiAuthorizationService>();

        // Storage retry policy for Dataverse operations
        // Handles replication lag scenarios with exponential backoff (2s, 4s, 8s)
        services.AddScoped<IStorageRetryPolicy, StorageRetryPolicy>();

        // Register HttpClient for DataverseAccessDataSource (handles its own authentication)
        // Step 1: Register the concrete DataverseAccessDataSource with its typed HttpClient
        //
        // FR-A2 (auth-v4 task 011) — this registration STAYS as AddHttpClient (transient), by
        // decision. Task 011 was authored as "change this registration accordingly". Two reasons not
        // to; the second is the one that actually forbids it.
        //
        //   1. Promoting a typed HttpClient to singleton pins one HttpMessageHandler for process
        //      lifetime, defeating the handler rotation and DNS refresh IHttpClientFactory provides.
        //      (On its own this is arguable — PooledConnectionLifetime can address it.)
        //   2. DECISIVE: DataverseAccessDataSource holds MUTABLE, NON-THREAD-SAFE PER-INSTANCE AUTH
        //      STATE — the _currentToken field and _httpClient.DefaultRequestHeaders.Authorization,
        //      both written in EnsureAuthenticatedAsync. A singleton would share one Authorization
        //      header across all concurrent requests, which is a data race that can BLEED A TOKEN
        //      BETWEEN USERS. Not a performance tradeoff — a correctness and security defect.
        //      Do not promote this registration without first removing that per-instance state.
        //
        // The DI-lifetime hazard the task targets is the credential objects rebuilt per resolution —
        // fixed inside the class by static (tenant|client|secret-fingerprint) caches for both the OBO
        // confidential client and the app-only ClientSecretCredential, the same shape
        // DataverseUserClient (also a transient typed HttpClient) already uses.
        //
        // Note on the app-only path: in the MANAGED-IDENTITY branch the credential is the DI-injected
        // singleton TokenCredential (Program.cs:46) and was never per-request. In the SECRET branch it
        // was, and is now cached. An earlier version of this comment claimed the app-only path had no
        // per-request rebuild at all — true of the MI branch only (code-review finding W-3).
        services.AddHttpClient<DataverseAccessDataSource>((sp, client) =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var dataverseUrl = configuration["Dataverse:ServiceUrl"];

            if (!string.IsNullOrEmpty(dataverseUrl))
            {
                var apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2/";
                client.BaseAddress = new Uri(apiUrl);
                client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
                client.DefaultRequestHeaders.Add("OData-Version", "4.0");
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }

            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // 🔴 UNCONDITIONAL — the delegated (user-OBO) Dataverse client.
        //
        // MOVED HERE by unified-access-control-r2 task 126 from AddToolFramework
        // (Services/Ai/ToolFrameworkExtensions.cs), which runs INSIDE the compound AI gate. That made it an
        // asymmetrically-registered dependency the moment a non-AI caller needed it. The load-bearing
        // non-AI consumer is /api/office/communications/* (task 127, #1020): three handlers that read
        // through this client under the caller's security context, on a group that maps UNCONDITIONALLY —
        // so with the AI gate off, they could not bind.
        //
        // (Task 126 made the move for GET /api/office/search/entities, which spaarkeai-word-add-in-r1 task
        // 062 later reimplemented as an app-only impersonated read — that route no longer uses this client.
        // A reader who follows only that history might conclude the registration can go back behind the
        // gate. It cannot: the communications routes depend on it.)
        //
        // That is exactly the Tier-1.5 anti-pattern in CLAUDE.md §10 F.1 / RB-T028-03..06:
        // "endpoints that map unconditionally must have unconditional service registration."
        //
        // The AI tool handlers still resolve it — strictly MORE available than before, never less —
        // which is why the registration could move rather than being duplicated. A second
        // AddHttpClient registration would have left two competing descriptors for one interface.
        services.AddHttpClient<Sprk.Bff.Api.Infrastructure.Dataverse.IDataverseUserClient,
                               Sprk.Bff.Api.Infrastructure.Dataverse.DataverseUserClient>();

        // Step 2: Decorate with CachedAccessDataSource (ADR-009: Redis-first caching for auth data)
        // Caches authorization DATA (the per-document and per-record access snapshots) while decisions are computed
        // fresh. TTL: 60 s (ADR-003 A1). Unified-access-control-r2 task 132 (C12): keys are tenant-scoped through
        // ITenantCache (ADR-009 path C) and a FAULTED snapshot is never cached; the former 2-minute roles/teams keys
        // were write-only and are deleted.
        services.AddScoped<IAccessDataSource>(sp =>
        {
            var inner = sp.GetRequiredService<DataverseAccessDataSource>();
            var cache = sp.GetRequiredService<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>();
            var httpContextAccessor = sp.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
            var logger = sp.GetRequiredService<ILogger<CachedAccessDataSource>>();
            // FR-02 of spaarke-redis-cache-remediation-r2: CacheMetrics is now a static class
            // (Sprk.Bff.Api.Telemetry.CacheMetrics). The consumer references it directly; no DI.
            return new CachedAccessDataSource(inner, cache, httpContextAccessor, logger);
        });

        // Authorization rules
        // Single rule using granular AccessRights model - RetrievePrincipalAccess already
        // factors in team membership, security roles, and record sharing
        services.AddScoped<IAuthorizationRule, OperationAccessRule>();

        // Request cache for per-request memoization
        services.AddScoped<RequestCache>();

        return services;
    }
}
