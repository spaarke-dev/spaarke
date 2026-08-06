using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Registration;

namespace Sprk.Bff.Api.Infrastructure.DI;

/// <summary>
/// DI registration for external access (Secure Project Workspace) services.
/// Registers participation data and project data services used by the external SPA.
///
/// ADR-010: Concrete type registrations — no unnecessary interfaces.
/// ADR-009: ExternalParticipationService uses Redis via ITenantCache (60s TTL).
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

        // Workforce-token → principal resolver (ADR-028 Amendment A2 · teams-app-r1 FR-04, task 020).
        // Composes the existing AAD-oid→systemuser conversion (MembershipEndpoints.ResolveSystemUserIdAsync)
        // with the AAD-oid/verified-email→contact conversion (IIdentityNormalizationService) into one
        // principal (systemuser / contact-only / deny). Singleton is safe: all deps
        // (IIdentityNormalizationService, IDataverseService, ITenantCache) are singletons. The interface
        // is the testing seam (ADR-010, matching the IIdentityNormalizationService precedent). No Graph
        // SDK / AI-internal types are injected (broker-only, NFR-02).
        services.AddSingleton<IWorkforcePrincipalResolver, WorkforcePrincipalResolver>();

        // Standing-grant flag reader (teams-app-r1 task 022 — the task-051 composition seam). Reads the
        // FLS-secured contact.sprk_standinggrant boolean app-only via the already-registered
        // IDataverseService; gates a contact principal's standing-grant runtime membership term.
        // Interface is the ADR-010 testing seam. Singleton is safe (IDataverseService is a singleton).
        services.AddSingleton<IContactStandingGrantReader, ContactStandingGrantReader>();

        // Principal-agnostic caller resolution (teams-app-r1 task 025 · R2 FR-22 · Option A). The
        // reusable abstraction that lets the /api/v1/external collaboration endpoints serve BOTH the
        // CIAM external contact AND the workforce (Teams-host) user through ONE endpoint set. The two
        // strategies are registered as ICallerPrincipalStrategy; the resolver selects by token issuer.
        // A THIRD plane plugs in by adding one more ICallerPrincipalStrategy registration (R2 lifts
        // this into its module framework unchanged). Scoped: CiamContactPrincipalStrategy depends on
        // the scoped typed-HttpClient ExternalParticipationService; WorkforcePrincipalStrategy depends
        // on the scoped IAccessibleRecordSetService — so the strategies + resolver are scoped too.
        // Broker-only (NFR-02): app-only reads, no OBO, no Graph SDK / AI-internal types.
        services.AddScoped<ICallerPrincipalStrategy, CiamContactPrincipalStrategy>();
        services.AddScoped<ICallerPrincipalStrategy, WorkforcePrincipalStrategy>();
        services.AddScoped<ICallerPrincipalResolver, CallerPrincipalResolver>();

        // Accessible-record-set composition + enforcement gate (teams-app-r1 task 022, spec FR-06 /
        // design §5). Composes accessible(principal) = systemuser→ADR-034 membership (auto) ∪
        // contact→sprk_externalrecordaccess grants ∪ contact→standing-grant runtime membership, and is
        // the single record∈set enforcement point (consumed by the ADR-008 endpoint filter + task 030's
        // authz-before-stream broker gate). Interface is the ADR-010 testing seam. No Graph SDK /
        // AI-internal types (broker-only, NFR-02). Scoped: ExternalParticipationService is a typed
        // HttpClient (scoped), so this composer is scoped too.
        services.AddScoped<IAccessibleRecordSetService, AccessibleRecordSetService>();

        // Cross-tenant CIAM Graph client (app-only) for admin-initiated external-user provisioning
        // (task 022; consumed by the provisioner in task 025). Singleton: caches a per-authority MSAL
        // confidential client (one Key Vault certificate fetch); stateless otherwise.
        // Depends on the shared SecretClient (registered in SpeAdminModule) to load the provisioner
        // certificate from Key Vault, and the resilient "GraphApiClient" HttpClient (GraphModule).
        // ADR-028 A1: app-only client-credentials against the CIAM authority — never OBO (broker-only).
        services.AddSingleton<CiamGraphClientFactory>();

        // Admin-initiated CIAM user provisioner (task 025). Creates CIAM local accounts via the
        // cross-tenant client above; reuses PasswordGenerator (RegistrationModule). Singleton per ADR-010.
        services.AddSingleton<CiamUserProvisioningService>();

        return services;
    }
}
