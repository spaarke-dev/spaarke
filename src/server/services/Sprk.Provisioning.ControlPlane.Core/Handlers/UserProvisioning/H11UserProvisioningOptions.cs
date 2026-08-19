// -----------------------------------------------------------------------------
// H11UserProvisioningOptions.cs
//
// Bound options for the H11 handler's collaborators (Graph user provisioner +
// B2B invitation client + B2B consent verifier). Loaded from the
// "H11UserProvisioningOptions" configuration section by Program.cs.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

/// <summary>
/// Bound options for <see cref="H11UserProvisioningHandler"/> collaborators.
/// Configuration key: <c>H11UserProvisioningOptions</c>.
/// </summary>
public sealed class H11UserProvisioningOptions
{
    /// <summary>
    /// UPN domain suffix for NativeAccount users (e.g. <c>spaarke-acme.onmicrosoft.com</c>
    /// or a verified custom domain). Configured per customer.
    /// </summary>
    public string AccountDomain { get; set; } = "spaarke.onmicrosoft.com";

    /// <summary>Power Apps Plan 2 Trial license SKU id. Null/empty skips this SKU.</summary>
    public string? PowerAppsPlan2TrialSkuId { get; set; }

    /// <summary>Fabric (Free) license SKU id. Null/empty skips this SKU.</summary>
    public string? FabricFreeSkuId { get; set; }

    /// <summary>Power Automate (Free) license SKU id. Null/empty skips this SKU.</summary>
    public string? PowerAutomateFreeSkuId { get; set; }

    /// <summary>
    /// <c>inviteRedirectUrl</c> for B2B guest invitations — where the guest
    /// lands after redeeming the invitation.
    /// </summary>
    public string InvitationRedirectUrl { get; set; } = "https://myapps.microsoft.com";

    /// <summary>Per-request timeout for Microsoft Graph REST HTTP calls.</summary>
    public TimeSpan GraphRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
