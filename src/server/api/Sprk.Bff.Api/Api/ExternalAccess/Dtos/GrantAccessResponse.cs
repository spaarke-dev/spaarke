using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Response returned after granting external access to a Contact.
/// </summary>
/// <param name="AccessRecordId">The ID of the created sprk_externalrecordaccess record.</param>
/// <param name="SpeContainerMembershipGranted">Whether the Contact was successfully added to the SPE container.</param>
/// <param name="GrantedAccessLevel">
/// The level actually written — the requested level capped at the caller's own level on the record (task 139, owner
/// Q1: every grant is capped at the grantor's level). Additive; older clients ignore it.
/// </param>
/// <param name="Narrowed">
/// <c>true</c> when the caller's own level was below the requested one, so the grant was written at
/// <paramref name="GrantedAccessLevel"/> instead (task 139). Additive.
/// </param>
/// <param name="NotificationFailed">
/// <c>true</c> when the grant gave access to a contact that represents an internal user and that user could not be sent
/// the in-app notification (task 181, owner round 89). The grant itself stands. Additive.
/// </param>
/// <param name="Invite">
/// Task 185 slice E (§12 I-1): what the grant did about the contact's CIAM invite — an unbound contact with an email is
/// invited automatically. <c>null</c> for an organization grant. Additive; no other field changes.
/// </param>
/// <param name="SharedWithUser">
/// Task 193 (#2338): set when the contact represents exactly ONE internal user, so the record was SHARED with that user
/// instead of granted to the contact. Then no grant row was written: <paramref name="AccessRecordId"/> is
/// <see cref="Guid.Empty"/>, <paramref name="Invite"/> is <c>null</c>, and <paramref name="GrantedAccessLevel"/>,
/// <paramref name="Narrowed"/> and <paramref name="NotificationFailed"/> describe the share. <c>null</c> for a grant. Additive.
/// </param>
/// <param name="ExpiryNotApplied">
/// Task 193: <c>true</c> when the record was SHARED with <paramref name="SharedWithUser"/> and the request carried an expiry:
/// a share has no end date (as when the colleague is picked directly), so the expiry was not applied. Absent otherwise
/// (never sent as <c>false</c>). Since task 193 G1 it speaks for the SHARE only: the paired workforce-app grant takes the
/// requested date (<paramref name="WorkforceApp"/>).
/// </param>
/// <param name="WorkforceApp">
/// Task 193 G1 (owner F1-b, 2026-10-28): set whenever the record was SHARED with an internal colleague: whether their
/// workforce-app access was given too (a contact grant on the contact that represents them, paired with the share), until
/// when, and — when it was not — why. Absent otherwise. Additive.
/// </param>
public record GrantAccessResponse(
    Guid AccessRecordId,
    bool SpeContainerMembershipGranted,
    ExternalAccessLevel? GrantedAccessLevel = null,
    bool Narrowed = false,
    bool NotificationFailed = false,
    GrantInviteResult? Invite = null,
    SharedWithUser? SharedWithUser = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    bool? ExpiryNotApplied = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    WorkforceAppAccess? WorkforceApp = null);
