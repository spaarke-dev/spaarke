namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

// The external grants on one record, for Manage Access's Current Access list (unified-access-control-r2 task 099a,
// amendment 4 / #1010(a)). One file, because it is one read.
//
// The record is named ONLY by recordType + recordId — the same shape as /can-manage-access and /user-shares, and for the
// same reason: the delegation check authorizes exactly the record whose grants are listed.

/// <summary>
/// Query string for <c>GET /api/v1/external-access/record-grants?recordType=&amp;recordId=</c>.
/// </summary>
/// <param name="RecordType"><c>project</c> | <c>matter</c> | <c>workassignment</c> (case-insensitive). Required.</param>
/// <param name="RecordId">The record whose grants are listed. Required.</param>
public record RecordGrantsQuery(string? RecordType, Guid? RecordId);

/// <summary>The record's active external grants (<c>sprk_externalrecordaccess</c> rows with <c>statecode = 0</c>).</summary>
/// <param name="RecordId">
/// The record this answer is about, echoed back so a client bound to another record by the time it arrives can discard it
/// (the <c>/can-manage-access</c> discipline).
/// </param>
/// <param name="Grants">Every active grant row on the record, lapsed ones included (marked <see cref="RecordGrant.IsExpired"/>).</param>
/// <param name="Truncated">
/// <c>true</c> when the record carries more than <c>RecordGrantsEndpoint.MaxGrantsListed</c> active rows and only that many
/// are listed — a cap stated, never a silent under-listing (NFR-03).
/// </param>
public sealed record RecordGrantsResponse(Guid RecordId, IReadOnlyList<RecordGrant> Grants, bool Truncated);

/// <summary>One active grant row, in the shape the Current Access list shows.</summary>
/// <param name="AccessRecordId"><c>sprk_externalrecordaccessid</c> — what <c>/revoke</c> takes.</param>
/// <param name="ContactId">The grantee contact; <c>null</c> on an organization grant.</param>
/// <param name="ContactName">The contact's display name (Dataverse's formatted value), when it was returned.</param>
/// <param name="OrganizationId">The grantee organization on an organization grant (also set on some contact grants).</param>
/// <param name="OrganizationName">The organization's display name, when it was returned.</param>
/// <param name="AccessLevel">The raw <c>sprk_accesslevel</c> option value.</param>
/// <param name="GrantedByName">The system user who issued the grant (<c>sprk_grantedby</c>), by display name.</param>
/// <param name="GrantedByContactName">The contact who issued the grant from the external SPA (<c>sprk_grantedbycontact</c>).</param>
/// <param name="GrantedDate"><c>sprk_granteddate</c> (Date Only).</param>
/// <param name="ExpiresDate"><c>sprk_expiresdate</c> (Date Only); <c>null</c> on a legacy unbounded row.</param>
/// <param name="IsExpired">
/// The row confers no access today: <c>ExternalParticipationService.ConfersAccessOn</c> is false (the read path's own rule —
/// the expiry date itself still confers, and an unbounded row confers nothing since task 107).
/// </param>
public sealed record RecordGrant(
    Guid AccessRecordId,
    Guid? ContactId,
    string? ContactName,
    Guid? OrganizationId,
    string? OrganizationName,
    int? AccessLevel,
    string? GrantedByName,
    string? GrantedByContactName,
    DateOnly? GrantedDate,
    DateOnly? ExpiresDate,
    bool IsExpired);
