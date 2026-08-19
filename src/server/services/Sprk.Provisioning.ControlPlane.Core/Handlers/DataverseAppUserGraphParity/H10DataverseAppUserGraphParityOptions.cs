// -----------------------------------------------------------------------------
// H10DataverseAppUserGraphParityOptions.cs
//
// Bound options for the H10 handler's collaborators (Dataverse App User
// creator/verifier + Graph app-role granter/verifier). Loaded from the
// "H10DataverseAppUserGraphParityOptions" configuration section by Program.cs.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <summary>
/// Bound options for <see cref="H10DataverseAppUserGraphParityHandler"/>
/// collaborators. Configuration key: <c>H10DataverseAppUserGraphParityOptions</c>.
/// </summary>
public sealed class H10DataverseAppUserGraphParityOptions
{
    /// <summary>
    /// Dataverse security role name granted to both App Users. Per spec.md
    /// FR-13 + design.md §9.3: "System Administrator" at the root business
    /// unit. Configurable so a future minimum-privilege custom role can be
    /// substituted without a code change.
    /// </summary>
    public string SecurityRoleName { get; set; } = "System Administrator";

    /// <summary>Per-request timeout for Dataverse Web API HTTP calls.</summary>
    public TimeSpan DataverseRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Per-request timeout for Microsoft Graph REST HTTP calls.</summary>
    public TimeSpan GraphRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
