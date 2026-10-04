namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Request body for POST /api/v1/external-access/unsecure-project — the reverse of secure provisioning
/// (design.md §5.1 "the designation is reversible", spec FR-28), for a project, matter or work assignment.
/// </summary>
/// <param name="ProjectId">
/// Legacy shorthand for a project root. Ignored when <paramref name="RecordType"/> is supplied.
/// </param>
/// <param name="ReassignToSystemUserId">
/// Optional. The <c>systemuser</c> to hand ownership to. When omitted the owner is taken from
/// configuration (<c>SecureRecord:UnsecureOwnerUserId</c>), and failing that the calling user — who
/// has already had to prove Write on the record to reach this route.
/// </param>
/// <param name="RecordType">
/// <c>project</c> | <c>matter</c> | <c>workassignment</c> (task 144). When supplied, <paramref name="RecordId"/> is
/// required and <paramref name="ProjectId"/> is ignored.
/// </param>
/// <param name="RecordId">The GUID of the record named by <paramref name="RecordType"/>.</param>
/// <param name="AlsoUnsecure">
/// unified-access-control-r2 task 158 (owner round 6: "but the user can unsecure any related records"): OPTIONAL — work
/// assignments and projects FILED UNDER this record that stayed secure (the response's <c>relatedSecureRecords</c>) to
/// unsecure in the same call, once this record is no longer secure. Each is checked on its own against F3 (a Full Access
/// holder on it, or the person who created it); one that fails the check, or is not filed under this record, is reported
/// in <c>relatedRecordsUnsecured</c> and left secure. Additive to the JSON contract.
/// </param>
public record UnsecureProjectRequest(
    Guid ProjectId,
    Guid? ReassignToSystemUserId = null,
    string? RecordType = null,
    Guid? RecordId = null,
    IReadOnlyList<RelatedRecordRef>? AlsoUnsecure = null);

/// <summary>A related record named by its type token (<c>project</c> | <c>workassignment</c>) and id (task 158).</summary>
public record RelatedRecordRef(string RecordType, Guid RecordId);
