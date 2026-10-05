namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

// The caller's own standing on one record, for the Manage Access affordance (unified-access-control-r2
// task 118, owner decision D-1 option C). One file, because it is one question.
//
// The record is named ONLY by recordType + recordId — the same shape as task 063's share routes, and for
// the same reason: the delegation check authorizes exactly the record the answer is about.

/// <summary>
/// Query string for <c>GET /api/v1/external-access/can-manage-access?recordType=&amp;recordId=[&amp;includeOwner=true]</c>.
/// </summary>
/// <param name="RecordType"><c>project</c> | <c>matter</c> | <c>workassignment</c> (case-insensitive). Required.</param>
/// <param name="RecordId">The record the caller is asking about. Required.</param>
/// <param name="IncludeOwner">
/// Task 150 (round 46 item 4; round 53 item 2): <c>true</c> also reports who OWNS the record — its owning team, whether
/// that is the Secure Record Owners team, and whether that team owns it inside the Secure Record business unit — for the
/// Access ribbon's "did this record's secure transition finish?" question. Omitted or <c>false</c>: no owner read is made
/// (the Manage Access gates' form-load path stays one rights probe). It changes nothing about the delegation answer.
/// </param>
public record RecordAccessGateQuery(
    string? RecordType,
    Guid? RecordId,
    bool? IncludeOwner = null);

/// <summary>
/// The answer: whether the caller may change who can access this record.
/// </summary>
/// <param name="RecordId">
/// The record this answer is about, echoed back. Not decoration: the client asks asynchronously while the
/// form may rebind to another record, so an answer that does not name its subject cannot be discarded when
/// it arrives for the wrong one. The record TYPE is deliberately not echoed — the caller supplied it, and
/// the id alone identifies the subject.
/// </param>
/// <param name="CanManageAccess">
/// Always <c>true</c> on a 200 — and that is the whole design, not an oversight. The value is decided by
/// <c>DelegationRuleFilter</c> on the way in, so a caller who may NOT manage access never reaches the
/// handler; they receive the filter's 403 with a <c>sdap.access.deny.delegation_*</c> reason code. The field
/// exists so the client reads a NAMED answer rather than inferring meaning from a bare 200, and so a future
/// answer with more than two states has somewhere to go. Callers MUST treat anything other than
/// <c>200 + canManageAccess: true</c> as "no" — see the endpoint's remarks.
/// </param>
/// <param name="OwningTeamId">
/// Only with <c>includeOwner=true</c> (round 46 item 4): the team that owns the record, or <c>null</c> when a user owns
/// it — or when the owner could not be read (then <see cref="OwnedBySecureOwnerTeam"/> is <c>null</c> too).
/// </param>
/// <param name="OwnedBySecureOwnerTeam">
/// Only with <c>includeOwner=true</c>: <c>true</c> when the record is owned by the Secure Record Owners team (the named
/// owner team provisioning assigns secure records to), <c>false</c> when it is owned by a user or by any other team, and
/// <c>null</c> when that could not be told — the owner could not be read, or which team is the Secure Record Owners
/// team could not be established (absent, ambiguous or unreadable). A client MUST treat <c>null</c> as unknown, never
/// as either answer.
/// </param>
/// <param name="OwningTeamInSecureBusinessUnit">
/// Only with <c>includeOwner=true</c> (round 53 item 2), for a record a TEAM owns: <c>true</c> when that team owns it
/// inside the Secure Record business unit — the Secure Record Owners team, or ANOTHER team there (the retired default team
/// before task 144's migration: the record is already isolated, and provisioning refuses it 409
/// <c>sdap.provision.owned_by_other_secure_team</c>) — <c>false</c> when the team owns it in another business unit (a
/// reassignment outside Spaarke: its secure transition did not finish). <c>null</c> when a user owns the record (see
/// <see cref="OwningTeamId"/>) or when it could not be told. A client MUST treat <c>null</c> as unknown, never as either
/// answer.
/// </param>
public record RecordAccessGateResponse(
    Guid RecordId,
    bool CanManageAccess,
    Guid? OwningTeamId = null,
    bool? OwnedBySecureOwnerTeam = null,
    bool? OwningTeamInSecureBusinessUnit = null);
