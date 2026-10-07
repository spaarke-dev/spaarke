// -----------------------------------------------------------------------------
// H11UserProvisioningOptions.cs
//
// Bound options for the H11 handler and its collaborators (Graph user
// provisioner, B2B invitation client, consent verifier, environment security
// group client, Dataverse guest-user writer). Loaded from the
// "H11UserProvisioningOptions" configuration section by Program.cs.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

/// <summary>
/// Bound options for <see cref="H11UserProvisioningHandler"/> and its collaborators.
/// Configuration key: <c>H11UserProvisioningOptions</c>.
/// </summary>
public sealed class H11UserProvisioningOptions
{
    /// <summary>
    /// UPN domain suffix for NativeAccount users (e.g. <c>spaarke-acme.onmicrosoft.com</c>
    /// or a verified custom domain). Configured per customer.
    /// </summary>
    public string AccountDomain { get; set; } = "spaarke.onmicrosoft.com";

    /// <summary>Power Apps Plan 2 Trial license SKU id (NativeAccount). Null/empty skips this SKU.</summary>
    public string? PowerAppsPlan2TrialSkuId { get; set; }

    /// <summary>Fabric (Free) license SKU id (NativeAccount). Null/empty skips this SKU.</summary>
    public string? FabricFreeSkuId { get; set; }

    /// <summary>Power Automate (Free) license SKU id (NativeAccount). Null/empty skips this SKU.</summary>
    public string? PowerAutomateFreeSkuId { get; set; }

    /// <summary>
    /// The configured NativeAccount licence SKUs. Empty → H11 refuses a NativeAccount run before creating anyone
    /// (task 232 — it used to create unlicensed users and report success). B2BGuest users get no licence: Spaarke pays
    /// for their access pay-as-you-go on the stamp subscription (owner 2026-10-07, prerequisite PRQ-C-11).
    /// </summary>
    public IReadOnlyList<string> LicenseSkuIds =>
        new[] { PowerAppsPlan2TrialSkuId, FabricFreeSkuId, PowerAutomateFreeSkuId }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToList();

    /// <summary>
    /// <c>inviteRedirectUrl</c> for B2B guest invitations — where the guest
    /// lands after redeeming the invitation.
    /// </summary>
    public string InvitationRedirectUrl { get; set; } = "https://myapps.microsoft.com";

    /// <summary>
    /// Task 232: Dataverse security roles (root business unit, by name) each B2B guest receives. The roles ship in the
    /// Spaarke solution (H6; the package is being redefined by T218) — a name the environment lacks stops H11 with
    /// <c>userprov-security-role-not-found</c> naming it. At least one.
    /// </summary>
    public List<string> GuestSecurityRoleNames { get; set; } = ["Spaarke Basic User"];

    /// <summary>Per-request timeout for Microsoft Graph REST HTTP calls.</summary>
    public TimeSpan GraphRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Per-request timeout for Dataverse Web API calls (task 232 guest users).</summary>
    public TimeSpan DataverseRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
