using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Spaarke.Core.Auth;
using Spaarke.Core.Auth.Rules;
using Spaarke.Core.Cache;
using Spaarke.Dataverse;
using System.Net.Http.Headers;

namespace Spe.Bff.Api.Infrastructure.DI;

public static class SpaarkeCore
{
    public static IServiceCollection AddSpaarkeCore(this IServiceCollection services)
    {
        // Don't add authorization here since it's already in Program.cs

        // SDAP Authorization services
        services.AddScoped<Spaarke.Core.Auth.AuthorizationService>();

        // Register HttpClient for DataverseAccessDataSource (shares Dataverse base URL configuration)
        services.AddHttpClient<IAccessDataSource, DataverseAccessDataSource>((sp, client) =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var dataverseUrl = configuration["Dataverse:ServiceUrl"];

            if (!string.IsNullOrEmpty(dataverseUrl))
            {
                var apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";
                client.BaseAddress = new Uri(apiUrl);
                client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
                client.DefaultRequestHeaders.Add("OData-Version", "4.0");
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            }

            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Authorization rules (registered in order of execution)
        // Using granular AccessRights model with OperationAccessRule
        services.AddScoped<IAuthorizationRule, OperationAccessRule>();  // Primary rule: checks operation-specific permissions
        services.AddScoped<IAuthorizationRule, TeamMembershipRule>();   // Fallback: team-based access

        // Request cache for per-request memoization
        services.AddScoped<RequestCache>();

        return services;
    }
}