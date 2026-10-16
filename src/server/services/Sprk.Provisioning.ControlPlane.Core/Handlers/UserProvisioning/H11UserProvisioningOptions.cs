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
    /// (task 232 — it used to create unlicensed users and report success). B2BGuest users do not get this set: each
    /// guest gets <see cref="GuestLicenseSkuId"/> (owner 2026-10-16, T186 L51 — Dataverse will not add an unlicensed
    /// guest; the pay-as-you-go link of PRQ-C-11 still covers Dataverse capacity, not user access).
    /// </summary>
    public IReadOnlyList<string> LicenseSkuIds =>
        new[] { PowerAppsPlan2TrialSkuId, FabricFreeSkuId, PowerAutomateFreeSkuId }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToList();

    /// <summary>
    /// The licence SKU id every B2B guest receives: Microsoft Power Apps Plan 2 Trial (part number POWERAPPS_VIRAL) —
    /// Microsoft's global product SKU id, the same in every tenant. Owner 2026-10-16 (T186 L51): Dataverse will not add
    /// an unlicensed guest. Null/empty → H11 refuses a B2BGuest run before inviting anyone.
    /// </summary>
    public string? GuestLicenseSkuId { get; set; } = "dcb1a3ae-b33f-4487-846a-a640262fadf4";

    /// <summary>
    /// Usage location set on a guest that has none (a licence cannot be assigned without one). Native users already get
    /// "US" at creation (<see cref="GraphRestUserProvisioner"/>).
    /// </summary>
    public string GuestUsageLocation { get; set; } = "US";

    /// <summary>
    /// <c>inviteRedirectUrl</c> for B2B guest invitations — where the guest
    /// lands after redeeming the invitation.
    /// </summary>
    public string InvitationRedirectUrl { get; set; } = "https://myapps.microsoft.com";

    /// <summary>The role every B2B guest receives when <see cref="GuestSecurityRoleNames"/> is not configured.</summary>
    public const string DefaultGuestSecurityRoleName = "Spaarke Basic User";

    /// <summary>
    /// Task 232: Dataverse security roles (by name; T259: the copy in the customer's business unit, where every guest is
    /// placed) each B2B guest receives. The roles ship in the
    /// Spaarke solution (H6; the package is being redefined by T218) — a name the environment lacks stops H11 with
    /// <c>userprov-security-role-not-found</c> naming it. Empty by default (the configuration binder APPENDS to an
    /// initialised list, so a default here could never be replaced) — read <see cref="EffectiveGuestSecurityRoleNames"/>.
    /// </summary>
    public List<string> GuestSecurityRoleNames { get; set; } = [];

    /// <summary>The configured roles, or <see cref="DefaultGuestSecurityRoleName"/> when none is configured.</summary>
    public IReadOnlyList<string> EffectiveGuestSecurityRoleNames =>
        GuestSecurityRoleNames.Count == 0 ? [DefaultGuestSecurityRoleName] : GuestSecurityRoleNames;

    /// <summary>Per-request timeout for Microsoft Graph REST HTTP calls.</summary>
    public TimeSpan GraphRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Per-request timeout for Dataverse Web API calls (task 232 guest users).</summary>
    public TimeSpan DataverseRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// T186 audit: attempts for H11's calls about a user or guest created or invited moments ago — the licence
    /// assignment of a new native user, the security-group add of a guest, and Dataverse's read of a guest whose
    /// membership has not propagated. Each waits out Entra/Dataverse replication (404, or 400 "does not reference a
    /// valid object") and no other error.
    /// </summary>
    public int ReplicationRetryCount { get; set; } = 6;

    /// <summary>Delay between those attempts.</summary>
    public TimeSpan ReplicationRetryDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Attempts for Dataverse's alternate-key read of a newly licensed guest who is a member of the environment security
    /// group. Dataverse adds the user on this read only after its user sync catches up, which takes minutes (live T186:
    /// more than 4 minutes) — far longer than Entra replication (<see cref="ReplicationRetryCount"/>).
    /// </summary>
    public int DataverseUserSyncRetryCount { get; set; } = 30;

    /// <summary>Delay between those attempts.</summary>
    public TimeSpan DataverseUserSyncRetryDelay { get; set; } = TimeSpan.FromSeconds(10);
}
