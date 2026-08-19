// -----------------------------------------------------------------------------
// IGraphUserProvisioner.cs
//
// L2 abstraction over the NativeAccount (D6) branch of H11: create an Entra
// ID user (correct UPN pattern) + assign the configured demo/production
// license SKUs. L2 port of the BFF's GraphUserService.CreateUserAsync +
// AssignLicensesAsync shape (r1 FR-11 pattern) — L2 cannot reference the BFF
// assembly (ADR-010), so this is an independent re-implementation using raw
// Graph REST calls (see GraphRestUserProvisioner.cs header).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

/// <summary>
/// Creates Entra ID users (NativeAccount branch) + assigns licenses.
/// Domain outcomes (creation failed, license failed) return typed results;
/// only unexpected infrastructure errors should throw.
/// </summary>
public interface IGraphUserProvisioner
{
    /// <summary>
    /// Creates (or, if a user with the generated UPN already exists,
    /// idempotently reuses) an Entra ID user for <paramref name="entry"/>.
    /// </summary>
    Task<UserCreationOutcome> CreateUserAsync(
        UserProvisioningEntry entry,
        string tenantId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Assigns the configured license SKUs to the given user. Idempotent —
    /// re-invoking after a prior partial/failed assignment is safe.
    /// </summary>
    Task<LicenseAssignmentOutcome> AssignLicenseAsync(
        string userId,
        string tenantId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of one <see cref="IGraphUserProvisioner.CreateUserAsync"/> invocation.
/// Exhaustive: <see cref="Success"/> | <see cref="Failure"/>.
/// </summary>
public abstract record UserCreationOutcome
{
    private UserCreationOutcome() { }

    /// <summary>User created (or already existed — UPN alt-key idempotency).</summary>
    /// <param name="UserId">Entra ID object id.</param>
    /// <param name="Upn">The generated (or matched-existing) user principal name.</param>
    public sealed record Success(string UserId, string Upn) : UserCreationOutcome;

    /// <summary>User creation failed.</summary>
    /// <param name="Diagnostic">Human-readable diagnostic (HTTP status + body where applicable).</param>
    public sealed record Failure(string Diagnostic) : UserCreationOutcome;
}

/// <summary>
/// Result of one <see cref="IGraphUserProvisioner.AssignLicenseAsync"/> invocation.
/// Exhaustive: <see cref="Success"/> | <see cref="Failure"/>.
/// </summary>
public abstract record LicenseAssignmentOutcome
{
    private LicenseAssignmentOutcome() { }

    /// <summary>Licenses assigned (or none configured — treated as a no-op success).</summary>
    public sealed record Success() : LicenseAssignmentOutcome;

    /// <summary>License assignment failed (e.g. insufficient licenses in tenant).</summary>
    /// <param name="Diagnostic">Human-readable diagnostic (HTTP status + body where applicable).</param>
    public sealed record Failure(string Diagnostic) : LicenseAssignmentOutcome;
}
