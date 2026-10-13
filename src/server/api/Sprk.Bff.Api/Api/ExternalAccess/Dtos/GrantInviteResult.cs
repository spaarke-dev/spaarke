namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Task 185 slice E (design note §11 option (c), §12 I-1) — what a grant did about the grantee's CIAM invite, carried as the
/// ADDITIVE <c>invite</c> field of a grant response. A grant whose automatic invite failed still succeeds: the grant is the
/// internal user's decision, and with no code there is no bind, so it fails closed. The response never carries the code.
/// </summary>
/// <param name="Status">One of <see cref="GrantInviteStatus"/>'s values.</param>
/// <param name="ErrorCode">
/// The failure's reason code, only when <paramref name="Status"/> is <see cref="GrantInviteStatus.Failed"/>: the invite core's
/// own code (<c>sdap.access.invite.*</c>) or <see cref="GrantInviteStatus.InviteFailedReasonCode"/>. The user can retry with
/// Invite.
/// </param>
public sealed record GrantInviteResult(string Status, string? ErrorCode = null);

/// <summary>Task 185 slice E — the values of <see cref="GrantInviteResult.Status"/> (§12 I-1 and I-4).</summary>
public static class GrantInviteStatus
{
    /// <summary>The grantee was unbound: a CIAM account was created and the onboarding email, with the code, was sent.</summary>
    public const string Sent = "Sent";

    /// <summary>The grantee was unbound and already had a CIAM account: the "sign in with your existing account" email, with the code, was sent.</summary>
    public const string ExistingAccount = "ExistingAccount";

    /// <summary>The grantee holds a current (under 30 days) invite: no new code was made (§12 I-2).</summary>
    public const string AlreadyPending = "AlreadyPending";

    /// <summary>
    /// No invite is needed: the grantee is already bound to a sign-in, or represents an internal user. A bound contact keeps
    /// the existing "you were given access" notification.
    /// </summary>
    public const string NotNeeded = "NotNeeded";

    /// <summary>The grantee is unbound and has no email address, so nobody can be invited.</summary>
    public const string NoEmail = "NoEmail";

    /// <summary>The invite was attempted and did not complete; <see cref="GrantInviteResult.ErrorCode"/> says why. The grant stands.</summary>
    public const string Failed = "Failed";

    /// <summary>
    /// §12 I-4 (owner round 98b): a contact-side (task 140) grant to an unbound colleague with an email. Contacts never invite,
    /// so no invite was sent; the record's internal owner was told the colleague needs one.
    /// </summary>
    public const string NeedsInternalInvite = "NeedsInternalInvite";

    /// <summary>The invite failed for a reason the invite core has no code for (an unexpected fault, or no usable portal URL).</summary>
    public const string InviteFailedReasonCode = "sdap.access.invite.failed";

    /// <summary>The grantee is not an active contact, so it was not invited.</summary>
    public const string ContactNotActiveReasonCode = "sdap.access.invite.contact_not_active";

    /// <summary>The grantee's email resolves to another contact (or to none), so the invite could not be sent to the grantee.</summary>
    public const string ContactMismatchReasonCode = "sdap.access.invite.contact_mismatch";

    /// <summary>The account was made (or found), but the invite email carrying the code did not send.</summary>
    public const string EmailNotSentReasonCode = "sdap.access.invite.email_not_sent";
}
