// -----------------------------------------------------------------------------
// CrossTenantFicRefusedException.cs
//
// Task 205b (row A42, FR-C4) — C# port of master script
// `Register-EntraAppRegistrations.ps1`'s `Assert-SpaarkeFicTenancy` refusal
// (script :350-396). Closes silent-failure surface SF-5: before this port the
// C# FIC path (task 130) had NO cross-tenant refusal — a cross-tenant
// (app-registration, UAMI) pair CREATES successfully (Entra validates nothing
// at FIC create) and fails only weeks later at the customer's first OBO call
// as an opaque AADSTS error, in production, with no provisioning-time signal.
//
// This is a DISTINCT exception type (not InvalidOperationException) so
// H3EntraAppRegHandler can map it to the machine-stable rejection code
// EntraAppRegRejectionCodes.CrossTenantFicRefused and operators/reconcilers
// can route it without string-matching the diagnostic.
//
// TENANCY RULE (ADR-028 A4 + TENANCY-AND-CREDENTIALS.md §1): Entra requires
// the app registration and the UAMI issuing the federated assertion to live
// in the SAME tenant. Cross-tenant *resource* access is supported; a
// cross-tenant *FIC issuer* is not. §9.2 disposition (Q2, owner-ratified
// 2026-08-25, reading (a)): a customer-owned Model 2 stamp federates its OWN
// stamp UAMI (same tenant as its app-reg) — so under every sanctioned Spaarke
// shape the pair is intra-tenant and this guard is inert protection. If it
// fires, the run is misconfigured (e.g. a spaarke-hosted profile pointed at a
// customer tenant) — refusing loudly at provisioning time is the entire
// point. See notes/decisions/adr-028-a4-integration-conflict-resolution.md
// §9.2 contingency: even a hypothetical reading-(b) shape falls back to a KV
// CERTIFICATE (A4 standing guard), never to weakening this refusal.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;

/// <summary>
/// Thrown by <see cref="GraphAppRegistrationProvisioner"/> when a federated
/// identity credential would pair an app registration with a UAMI from a
/// DIFFERENT Entra tenant. Port of the master script's
/// <c>Assert-SpaarkeFicTenancy</c> refusal (SF-5 closure, row A42 / FR-C4).
/// The refusal is unconditional — Entra's same-tenant FIC rule has no
/// profile exception; the credential would create successfully and fail only
/// at token exchange, silently.
/// </summary>
public sealed class CrossTenantFicRefusedException : Exception
{
    /// <summary>Tenant the app registration lives in (the tenant H3's Graph client creates the app in).</summary>
    public string AppRegistrationTenantId { get; }

    /// <summary>Tenant the FIC-issuing UAMI lives in (derived per profile — see <see cref="GraphAppRegistrationProvisioner.ResolveUamiTenantId"/>).</summary>
    public string UamiTenantId { get; }

    /// <summary>The run's environment profile (<c>spaarke-hosted-model2</c> / <c>customer-owned-model2</c>) — diagnostic context only; it does NOT gate the refusal.</summary>
    public string Profile { get; }

    public CrossTenantFicRefusedException(
        string appRegistrationTenantId, string uamiTenantId, string profile)
        : base(BuildMessage(appRegistrationTenantId, uamiTenantId, profile))
    {
        AppRegistrationTenantId = appRegistrationTenantId;
        UamiTenantId = uamiTenantId;
        Profile = profile;
    }

    private static string BuildMessage(string appRegTenantId, string uamiTenantId, string profile) =>
        "CROSS-TENANT FEDERATED CREDENTIAL — REFUSED (not supported by Entra). " +
        $"App registration tenant: '{appRegTenantId}'; UAMI tenant: '{uamiTenantId}'; profile: '{profile}'. " +
        "Entra requires the app registration and the user-assigned managed identity to be in the SAME " +
        "tenant (ADR-028 A4; TENANCY-AND-CREDENTIALS.md §1). Refused rather than attempted because the " +
        "failure mode is silent: the credential would CREATE successfully and fail only at token " +
        "exchange, surfacing weeks later at the customer's first OBO call. Per §9.2 reading (a) " +
        "(owner-ratified 2026-08-25, Q2): a customer-owned Model 2 stamp must federate its OWN stamp " +
        "UAMI (same tenant); a shape that genuinely needed a cross-tenant trust would require the " +
        "ADR-028 A4 Key Vault CERTIFICATE alternative, never a cross-tenant FIC. " +
        "[SF-5 closure — task 205b row A42; parity: Register-EntraAppRegistrations.ps1 Assert-SpaarkeFicTenancy]";
}
