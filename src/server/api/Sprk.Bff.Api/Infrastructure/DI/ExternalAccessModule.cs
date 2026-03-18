using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration for external access (Secure Project Workspace) services.
/// Registers participation data and project data services used by the external SPA.
///
/// ADR-010: Concrete type registrations — no unnecessary interfaces.
/// ADR-009: ExternalParticipationService uses Redis via IDistributedCache (60s TTL).
/// </summary>
public static class ExternalAccessModule
{
    /// <summary>
    /// Adds external access services: participation data loading and project data queries.
    /// </summary>
    public static IServiceCollection AddExternalAccess(this IServiceCollection services)
    {
        // Participation service — queries sprk_externalrecordaccess with Redis caching (60s TTL).
        // Resolves Contact by email and loads their project access grants.
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

        // Data service — queries project data (projects, documents, events, contacts, organizations)
        // for external SPA users using managed identity app-only access.
        services.AddHttpClient<ExternalDataService>((sp, client) =>
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

        // SPE container membership — manages Graph API permissions for external users.
        services.AddScoped<SpeContainerMembershipService>();

        return services;
    }
}
