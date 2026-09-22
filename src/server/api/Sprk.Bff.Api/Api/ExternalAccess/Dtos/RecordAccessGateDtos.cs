namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

// The caller's own standing on one record, for the Manage Access affordance (unified-access-control-r2
// task 118, owner decision D-1 option C). One file, because it is one question.
//
// The record is named ONLY by recordType + recordId — the same shape as task 063's share routes, and for
// the same reason: the delegation check authorizes exactly the record the answer is about.

/// <summary>
/// Query string for <c>GET /api/v1/external-access/can-manage-access?recordType=&amp;recordId=</c>.
/// </summary>
/// <param name="RecordType"><c>project</c> | <c>matter</c> | <c>workassignment</c> (case-insensitive). Required.</param>
/// <param name="RecordId">The record the caller is asking about. Required.</param>
public record RecordAccessGateQuery(
    string? RecordType,
    Guid? RecordId);

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
public record RecordAccessGateResponse(
    Guid RecordId,
    bool CanManageAccess);
