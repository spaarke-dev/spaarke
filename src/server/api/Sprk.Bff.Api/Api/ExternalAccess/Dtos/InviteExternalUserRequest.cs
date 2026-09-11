namespace Sprk.Bff.Api.Api.ExternalAccess.Dtos;

/// <summary>
/// Request body for inviting an external user to a Secure Project via Azure AD B2B.
/// </summary>
/// <param name="Email">The external user's email address. Used to send the Azure AD B2B invitation and look up / create their Contact record.</param>
/// <param name="ProjectId">The Dataverse Project ID the user is being invited to access.</param>
/// <param name="AccessLevel">Access level to grant (100000000=ViewOnly, 100000001=Collaborate, 100000002=FullAccess).</param>
/// <param name="FirstName">Optional first name for Contact creation if the Contact does not yet exist.</param>
/// <param name="LastName">Optional last name for Contact creation if the Contact does not yet exist.</param>
/// <param name="ExpiryDate">
/// Used by <c>/invite-and-grant</c> only — <c>/invite</c> writes no grant and ignores it. Same rule as
/// <c>GrantAccessRequest.ExpiryDate</c> (spec FR-33, task 097): a date before today is rejected (400) before
/// any onboarding happens; when omitted, the grant keeps its existing expiry, else gets today + 90 days.
/// (This line used to say "No expiry if not specified" — true until task 097, and the reason FR-33 exists.)
/// </param>
/// <param name="OrganizationId">
/// Optional grantee firm/organization — a <c>sprk_organization</c> id (NOT the OOB <c>account</c>) —
/// written to the grant's <c>sprk_Organization</c> lookup for firm-level scoping by <c>/invite-and-grant</c>.
/// </param>
/// <param name="RecordType">
/// Optional polymorphic grant root type for <c>/invite-and-grant</c>: <c>project</c> | <c>matter</c> |
/// <c>workassignment</c> (case-insensitive). When supplied, <paramref name="RecordId"/> is required and
/// <paramref name="ProjectId"/> is ignored for the grant. Unused by <c>/invite</c> (which only onboards).
/// </param>
/// <param name="RecordId">The GUID of the root record identified by <paramref name="RecordType"/>.</param>
public record InviteExternalUserRequest(
    string Email,
    Guid ProjectId,
    int AccessLevel,
    string? FirstName,
    string? LastName,
    DateOnly? ExpiryDate,
    Guid? OrganizationId,
    string? RecordType = null,
    Guid? RecordId = null);
