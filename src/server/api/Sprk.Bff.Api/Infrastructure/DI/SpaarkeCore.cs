using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
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

        // Step 2: Decorate with CachedAccessDataSource (ADR-009: Redis-first caching for auth data)
        // Caches authorization DATA (roles, teams, resource access) while decisions are computed fresh.
        // TTLs: roles/teams = 2 min, resource access = 60s (ADR-003 compliance)
        services.AddScoped<IAccessDataSource>(sp =>
        {
            var inner = sp.GetRequiredService<DataverseAccessDataSource>();
            var cache = sp.GetRequiredService<IDistributedCache>();
            var logger = sp.GetRequiredService<ILogger<CachedAccessDataSource>>();
            // FR-02 of spaarke-redis-cache-remediation-r2: CacheMetrics is now a static class
            // (Sprk.Bff.Api.Telemetry.CacheMetrics). The consumer references it directly; no DI.
            return new CachedAccessDataSource(inner, cache, logger);
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
