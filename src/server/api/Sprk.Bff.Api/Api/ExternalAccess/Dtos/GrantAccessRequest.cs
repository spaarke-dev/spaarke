using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Request body for granting external access to a Contact for a specific root record.
///
/// <para>
/// Polymorphic root (task 070): a grant is held at exactly ONE root — a Project, Matter, OR Work
/// Assignment — expressed by <paramref name="RecordType"/> + <paramref name="RecordId"/>. The legacy
/// <paramref name="ProjectId"/> field is retained as back-compat shorthand for a project root
/// (recordType = project); pre-071 clients that send only <c>ProjectId</c> keep working unchanged.
/// </para>
/// </summary>
/// <param name="ContactId">The Dataverse Contact ID to grant access to.</param>
/// <param name="ProjectId">
/// Legacy shorthand for a Project root (bound to <c>sprk_projectid</c>). Retained for back-compat until
/// task 071 updates the modal to send <paramref name="RecordType"/> + <paramref name="RecordId"/>.
/// Ignored when <paramref name="RecordType"/> is supplied.
/// </param>
/// <param name="AccessLevel">The access level to grant (ViewOnly, Collaborate, or FullAccess).</param>
/// <param name="ExpiryDate">Optional expiry date for the access grant.</param>
/// <param name="OrganizationId">
/// Optional grantee firm/organization — a <c>sprk_organization</c> id (NOT the OOB <c>account</c>).
/// When supplied it is written to the grant's <c>sprk_Organization</c> lookup for firm-level scoping.
/// </param>
/// <param name="RecordType">
/// The polymorphic root type: <c>project</c> | <c>matter</c> | <c>workassignment</c> (case-insensitive).
/// When supplied, <paramref name="RecordId"/> is required and <paramref name="ProjectId"/> is ignored.
/// </param>
/// <param name="RecordId">The GUID of the root record identified by <paramref name="RecordType"/>.</param>
public record GrantAccessRequest(
    Guid ContactId,
    Guid ProjectId,
    ExternalAccessLevel AccessLevel,
    DateOnly? ExpiryDate,
    Guid? OrganizationId,
    string? RecordType = null,
    Guid? RecordId = null);
