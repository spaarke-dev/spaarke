// -----------------------------------------------------------------------------
// IB2BInvitationClient.cs
//
// L2 abstraction over the B2BGuest (D6) branch of H11: send a Microsoft Graph
// B2B guest invitation (POST /invitations) for a customer user. Distinct from
// IGraphUserProvisioner — B2B guests are provisioned by invitation, not by a
// direct POST /users (see H11UserProvisioningHandler.cs file header "BRANCH
// SEMANTICS" for the full rationale).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;

/// <summary>
/// Sends a Microsoft Graph B2B guest invitation. Domain outcomes (invitation
/// failed) return typed results; only unexpected infrastructure errors should
/// throw.
/// </summary>
public interface IB2BInvitationClient
{
    /// <summary>
    /// Invites <paramref name="entry"/> as a B2B guest into the Spaarke
    /// tenant. Idempotent — re-inviting an already-invited email resends the
    /// invitation and returns the same <c>invitedUser.id</c>.
    /// </summary>
    Task<B2BInvitationOutcome> InviteAsync(
        UserProvisioningEntry entry,
        string tenantId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of one <see cref="IB2BInvitationClient.InviteAsync"/> invocation.
/// Exhaustive: <see cref="Success"/> | <see cref="Failure"/>.
/// </summary>
public abstract record B2BInvitationOutcome
{
    private B2BInvitationOutcome() { }

    /// <summary>Invitation sent (or re-sent — idempotent).</summary>
    /// <param name="InvitedUserId">Entra ID object id of the invited guest (<c>invitedUser.id</c>).</param>
    /// <param name="InvitationId">Graph invitation resource id.</param>
    public sealed record Success(string InvitedUserId, string InvitationId) : B2BInvitationOutcome;

    /// <summary>Invitation failed.</summary>
    /// <param name="Diagnostic">Human-readable diagnostic (HTTP status + body where applicable).</param>
    public sealed record Failure(string Diagnostic) : B2BInvitationOutcome;
}
