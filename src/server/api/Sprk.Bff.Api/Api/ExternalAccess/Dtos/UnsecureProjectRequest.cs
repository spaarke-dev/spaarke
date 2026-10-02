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
public record UnsecureProjectRequest(
    Guid ProjectId,
    Guid? ReassignToSystemUserId = null,
    string? RecordType = null,
    Guid? RecordId = null);
