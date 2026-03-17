using Sprk.Bff.Api.Infrastructure.ExternalAccess;
// Note: PortalTokenValidator removed — external users now authenticate via Azure AD B2B (MSAL).

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration for external access (Secure Project) services.
/// Registers participation data and SPE membership services.
///
/// External user authentication uses Azure AD B2B guest accounts (MSAL in the SPA).
/// JWT validation is handled by ASP.NET Core's standard authentication middleware.
///
/// ADR-010: Concrete type registrations — no unnecessary interfaces.
/// ADR-009: ExternalParticipationService uses Redis via IDistributedCache (60s TTL).
/// </summary>
public static class ExternalAccessModule
{
    /// <summary>
    /// Adds external access services: participation data loading and SPE membership management.
    /// </summary>
    public static IServiceCollection AddExternalAccess(this IServiceCollection services)
    {
        // Participation service — queries contacts + sprk_externalrecordaccess with Redis caching
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
