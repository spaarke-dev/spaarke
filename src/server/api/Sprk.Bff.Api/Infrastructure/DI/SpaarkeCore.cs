using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Core.Cache;
using Spaarke.Dataverse;
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
        services.AddHttpClient<IAccessDataSource, DataverseAccessDataSource>((sp, client) =>
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

        // Authorization rules
        // Single rule using granular AccessRights model - RetrievePrincipalAccess already
        // factors in team membership, security roles, and record sharing
        services.AddScoped<IAuthorizationRule, OperationAccessRule>();

        // Request cache for per-request memoization
        services.AddScoped<RequestCache>();

        return services;
    }
}
