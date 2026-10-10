// -----------------------------------------------------------------------------
// IGraphAppRoleParityVerifier.cs
//
// T3 SILENT-FAIL TRAP OWNER (spec.md FR-33 + design.md §4B): independently
// re-queries the UAMI service principal's appRoleAssignments AFTER
// IGraphAppRoleGranter reports success, and asserts ALL expected role IDs are
// present (VerifyAsync) and — task 261 / G31 — that NO other Microsoft Graph app
// role is (FindUnexpectedRolesAsync). Deliberately a fresh HTTP round-trip (not a
// reuse of the granter's in-memory result) — per the POML escalation trigger, a
// still-partial parity result despite a "successful" grant loop most often means
// a GraphAppRoles.cs GUID VALUE is wrong (not just null), which is a distinct
// diagnosis path from a plain grant-call failure.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;

/// <summary>
/// Verifies the T3 post-condition: the UAMI service principal's Graph-scoped
/// <c>appRoleAssignments</c> include every role in the expected set
/// (<see cref="VerifyAsync"/>) and nothing outside the allowed set
/// (<see cref="FindUnexpectedRolesAsync"/>).
/// </summary>
public interface IGraphAppRoleParityVerifier
{
    Task<GraphAppRoleParityResult> VerifyAsync(
        string uamiServicePrincipalObjectId,
        string tenantId,
        IReadOnlyList<GraphAppRoleEntry> expectedRoles,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists the Microsoft Graph app roles <paramref name="uamiServicePrincipalObjectId"/> holds that are NOT in
    /// <paramref name="allowedRoles"/>. Never throws for an HTTP or token failure — that is
    /// <see cref="GraphAppRoleExtrasResult.Unknown"/>, never "none".
    /// </summary>
    Task<GraphAppRoleExtrasResult> FindUnexpectedRolesAsync(
        string uamiServicePrincipalObjectId,
        string tenantId,
        IReadOnlyList<GraphAppRoleEntry> allowedRoles,
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

/// <summary>
/// Result of one <see cref="IGraphAppRoleParityVerifier.FindUnexpectedRolesAsync"/> invocation.
/// Exhaustive: <see cref="None"/> | <see cref="Found"/> | <see cref="Unknown"/>.
/// </summary>
public abstract record GraphAppRoleExtrasResult
{
    private GraphAppRoleExtrasResult() { }

    /// <summary>The identity holds no Microsoft Graph app role outside the allowed set.</summary>
    public sealed record None : GraphAppRoleExtrasResult;

    /// <summary>The identity holds these Graph app roles outside the allowed set (role values; the id when Graph lists no value).</summary>
    public sealed record Found(IReadOnlyList<string> RoleValues) : GraphAppRoleExtrasResult;

    /// <summary>The assignments could not be read — no verdict.</summary>
    public sealed record Unknown(string Diagnostic) : GraphAppRoleExtrasResult;
}
