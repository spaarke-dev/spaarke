using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration for external access (Power Pages / Secure Project) services.
/// Registers portal token validation and participation data services.
///
/// ADR-010: Concrete type registrations — no unnecessary interfaces.
/// ADR-009: ExternalParticipationService uses Redis via IDistributedCache (60s TTL).
/// </summary>
public static class ExternalAccessModule
{
    /// <summary>
    /// Adds external access services: portal token validation and participation data loading.
    /// </summary>
    public static IServiceCollection AddExternalAccess(this IServiceCollection services)
    {
        // Portal token validator — uses memory cache for public key (1-hour TTL)
        // HttpClient registered as named client for the Power Pages portal endpoint
        services.AddHttpClient<PortalTokenValidator>((sp, client) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var portalUrl = config["PowerPages:BaseUrl"];
            if (!string.IsNullOrEmpty(portalUrl))
            {
                client.BaseAddress = new Uri(portalUrl.TrimEnd('/') + "/");
            }
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        // Participation service — queries sprk_externalrecordaccess with Redis caching
        // Uses its own HttpClient instance with app-only Dataverse authentication
        services.AddHttpClient<ExternalParticipationService>((sp, client) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var dataverseUrl = config["Dataverse:ServiceUrl"];
            if (!string.IsNullOrEmpty(dataverseUrl))
            {
                client.BaseAddress = new Uri($"{dataverseUrl.TrimEnd('/')}/api/data/v9.2/");
                client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
                client.DefaultRequestHeaders.Add("OData-Version", "4.0");
            }
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // SPE container membership — manages Graph API permissions for external users
        // Scoped because it uses IGraphClientFactory (singleton-safe) and performs per-request operations
        services.AddScoped<SpeContainerMembershipService>();

        return services;
    }
}
