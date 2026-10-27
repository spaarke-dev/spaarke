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

    /// <summary>
    /// Task 261: how many times the T3 "nothing extra remains" re-read is attempted while it still lists a role H10
    /// just removed. Microsoft Graph is eventually consistent — a deleted appRoleAssignment can still be listed for a
    /// few seconds — so one early read must not decide.
    /// </summary>
    public int ExtrasRecheckAttempts { get; set; } = 5;

    /// <summary>Task 261: delay before each re-read after the first (multiplied by the attempt number).</summary>
    public TimeSpan ExtrasRecheckDelay { get; set; } = TimeSpan.FromSeconds(4);

    /// <summary>
    /// T186 audit: how many times H10 looks for an object it (or the platform) created seconds earlier before judging it
    /// absent — a just-granted Graph app role in the T3 parity read, a just-created application user in the T2 re-query,
    /// the System Administrator copy in a just-created business unit (Dataverse copies roles in asynchronously). Applies to
    /// "not visible yet" answers only; a real mismatch is reported unchanged once the attempts are used.
    /// </summary>
    public int PropagationRetryAttempts { get; set; } = 6;

    /// <summary>T186 audit: delay before each further look, multiplied by the attempt number (3s, 6s, 9s, … by default).</summary>
    public TimeSpan PropagationRetryDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// T186 audit: how many times a verification read is attempted while the platform answers 429, 5xx, times out or drops
    /// the connection. A 401 / 403 / 400 is never retried.
    /// </summary>
    public int TransientRetryAttempts { get; set; } = 4;

    /// <summary>T186 audit: delay before each retry of a transient fault, multiplied by the attempt number.</summary>
    public TimeSpan TransientRetryDelay { get; set; } = TimeSpan.FromSeconds(2);
}
