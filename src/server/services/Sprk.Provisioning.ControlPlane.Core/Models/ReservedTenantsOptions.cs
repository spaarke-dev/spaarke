// -----------------------------------------------------------------------------
// ReservedTenantsOptions.cs
//
// Task 255 (INCOMING-141 from unified-access-control-r2): the Entra tenants the L2 control plane KNOWS are
// Spaarke's own and therefore are never a customer's workforce tenant — Spaarke's workforce tenant and the
// Entra External ID (CIAM) tenant(s) external contacts sign in to. CustomerWorkforceTenantsRule refuses
// either one as a `customerWorkforceTenantIds` value: listed on a stamp, Spaarke's tenant would let the BFF
// email-bind Spaarke's own staff into a customer's environment, and a CIAM tenant makes the stamp BFF refuse
// to start (WorkforceIdentityOptionsValidator rejects Ciam:TenantId). An L2-owned value, so a validated
// option on BOTH hosts (the API applies the rule at POST /api/runs, the Worker in H4b and H13) — never a run
// parameter (.claude/constraints/provisioning.md "Run-context contract").
//
// Set by modules/controlplane-app-service.bicep (Api) and modules/controlplane-worker-app-service.bicep
// (Worker): ReservedTenants__SpaarkeTenantId = tenant().tenantId (the control plane is deployed in Spaarke's
// tenant), ReservedTenants__CiamTenantIds__N = platform-controlplane.bicep's `ciamTenantIds` (no default).
//
// §11 justification — Existing: EntraAppRegOptions.SpaarkeTenantId (Worker only, H3's FIC issuer; the Api host
// has no EntraAppRegOptions) and nothing names the CIAM tenant in L2. Extension: binding EntraAppRegOptions on
// the Api would drag H3's Graph settings into the intake host for one GUID, and it has no CIAM field. Cost of
// doing nothing: POST /api/runs cannot refuse the CIAM or Spaarke tenant (hand-off §4.1), so a stamp is built
// whose BFF refuses to start, or that admits Spaarke's staff as the customer's members.
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sprk.Provisioning.ControlPlane.Core.Models;

/// <summary>
/// Tenants that are never a customer's workforce tenant (task 255). Bound from <c>ReservedTenants</c>; validated
/// when the Api and the Worker start.
/// </summary>
public sealed class ReservedTenantsOptions
{
    /// <summary>Configuration section name (<c>ReservedTenants__SpaarkeTenantId</c> as an app setting).</summary>
    public const string SectionName = "ReservedTenants";

    /// <summary>Spaarke's own Entra (workforce) tenant id — the tenant the control plane is deployed in. Required GUID.</summary>
    public string SpaarkeTenantId { get; set; } = string.Empty;

    /// <summary>
    /// The Entra External ID (CIAM) tenant id(s) Spaarke operates for external contacts (today one:
    /// <c>spaarkeextid.onmicrosoft.com</c>, config/spaarke-resources.yaml <c>external_identity.ciam_tenant</c>).
    /// Required, at least one GUID.
    /// </summary>
    public List<string> CiamTenantIds { get; set; } = [];

    /// <summary>Every reserved tenant, parsed. Call only after <see cref="Validate"/> has passed (the hosts guarantee it at startup).</summary>
    public ReservedTenants Parsed()
        => new(Guid.Parse(SpaarkeTenantId.Trim()), CiamTenantIds.Select(v => Guid.Parse(v.Trim())).ToHashSet());

    /// <summary>Startup validation. Throws <see cref="InvalidOperationException"/> naming the setting.</summary>
    public void Validate()
    {
        if (!Guid.TryParse(SpaarkeTenantId?.Trim(), out var spaarke) || spaarke == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"{SectionName}:SpaarkeTenantId must be Spaarke's own Entra tenant id (a non-zero GUID; got " +
                $"'{SpaarkeTenantId}'). The control-plane Bicep sets it to the deployment's tenant. POST /api/runs, H4b " +
                "and H13 refuse it as a customer workforce tenant (task 255).");
        }

        if (CiamTenantIds is null || CiamTenantIds.Count == 0)
        {
            throw new InvalidOperationException(
                $"{SectionName}:CiamTenantIds must list the Entra External ID (CIAM) tenant id(s) Spaarke operates " +
                "(platform-controlplane.bicep `ciamTenantIds`) — a stamp BFF refuses to start when its workforce " +
                "tenant list names its CIAM tenant, so L2 must know which tenants those are (task 255).");
        }

        for (var i = 0; i < CiamTenantIds.Count; i++)
        {
            if (!Guid.TryParse(CiamTenantIds[i]?.Trim(), out var ciam) || ciam == Guid.Empty)
            {
                throw new InvalidOperationException(
                    $"{SectionName}:CiamTenantIds:{i} = '{CiamTenantIds[i]}' is not a tenant id (a non-zero GUID).");
            }
            if (ciam == spaarke)
            {
                throw new InvalidOperationException(
                    $"{SectionName}:CiamTenantIds:{i} equals {SectionName}:SpaarkeTenantId — a CIAM tenant is a separate " +
                    "Entra External ID tenant, never Spaarke's workforce tenant.");
            }
        }
    }
}

/// <summary>The reserved tenants, parsed (see <see cref="ReservedTenantsOptions"/>).</summary>
/// <param name="SpaarkeTenantId">Spaarke's own workforce tenant.</param>
/// <param name="CiamTenantIds">The CIAM tenant(s).</param>
public sealed record ReservedTenants(Guid SpaarkeTenantId, IReadOnlySet<Guid> CiamTenantIds);

/// <summary>DI registration for <see cref="ReservedTenantsOptions"/> — called by the Api and the Worker composition roots.</summary>
public static class ReservedTenantsServiceCollectionExtensions
{
    /// <summary>Binds and validates <see cref="ReservedTenantsOptions"/> at host start (ADR-010 ValidateOnStart).</summary>
    public static IServiceCollection AddReservedTenants(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ReservedTenantsOptions>()
            .Bind(configuration.GetSection(ReservedTenantsOptions.SectionName))
            .Validate(o =>
            {
                o.Validate();
                return true;
            }, "ReservedTenantsOptions failed validation — see inner exception (Validate throws).")
            .ValidateOnStart();
        return services;
    }
}
