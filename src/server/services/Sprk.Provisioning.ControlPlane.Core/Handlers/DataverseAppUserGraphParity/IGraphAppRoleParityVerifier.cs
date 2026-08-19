// -----------------------------------------------------------------------------
// IGraphAppRoleParityVerifier.cs
//
// T3 SILENT-FAIL TRAP OWNER (spec.md FR-33 + design.md §4B): independently
// re-queries the UAMI service principal's appRoleAssignments AFTER
// IGraphAppRoleGranter reports success, and asserts ALL expected role IDs are
// present. Deliberately a fresh HTTP round-trip (not a reuse of the granter's
// in-memory result) — per the POML escalation trigger, a still-partial parity
// result despite a "successful" grant loop most often means a GraphAppRoles.cs
// GUID VALUE is wrong (not just null), which is a distinct diagnosis path
// from a plain grant-call failure.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <summary>
/// Verifies the T3 post-condition: the UAMI service principal's Graph-scoped
/// <c>appRoleAssignments</c> include every role in <paramref name="expectedRoles"/>
/// (via <see cref="VerifyAsync"/>).
/// </summary>
public interface IGraphAppRoleParityVerifier
{
    Task<GraphAppRoleParityResult> VerifyAsync(
        string uamiServicePrincipalObjectId,
        string tenantId,
        IReadOnlyList<GraphAppRoleEntry> expectedRoles,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of one <see cref="IGraphAppRoleParityVerifier.VerifyAsync"/>
/// invocation. Exhaustive: <see cref="Verified"/> | <see cref="Partial"/>.
/// </summary>
public abstract record GraphAppRoleParityResult
{
    private GraphAppRoleParityResult() { }

    /// <summary>All expected roles observed on the UAMI SP.</summary>
    public sealed record Verified(int GrantedCount) : GraphAppRoleParityResult;

    /// <summary>
    /// One or more expected roles are still absent. <paramref name="MissingRoleValues"/>
    /// names them (operator diagnostic — do NOT require log-diving to know
    /// which role is missing).
    /// </summary>
    public sealed record Partial(
        IReadOnlyList<string> MissingRoleValues,
        int GrantedCount,
        int ExpectedCount) : GraphAppRoleParityResult;
}
