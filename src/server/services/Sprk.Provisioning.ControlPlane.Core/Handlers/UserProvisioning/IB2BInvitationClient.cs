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
    /// Makes <paramref name="entry"/> a B2B guest of the run's tenant. Idempotent without a second email (task 232):
    /// an existing guest with that address is reused and NOT re-invited; only an unknown address is invited.
    /// An address that belongs to a member of the tenant is refused.
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

    /// <summary>The user is a guest of the tenant — invited now, or already a guest (reused, no email).</summary>
    /// <param name="InvitedUserId">Entra ID object id of the guest.</param>
    /// <param name="InvitationId">Graph invitation resource id; <c>null</c> when an existing guest was reused.</param>
    public sealed record Success(string InvitedUserId, string? InvitationId) : B2BInvitationOutcome;

    /// <summary>Invitation failed.</summary>
    /// <param name="Diagnostic">Human-readable diagnostic (HTTP status + body where applicable).</param>
    public sealed record Failure(string Diagnostic) : B2BInvitationOutcome;
}
