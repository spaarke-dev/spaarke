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
/// <param name="InvitedTenantId">
/// Not accepted: any non-blank value is refused (400). The person's organisation is resolved by the server from the email's
/// domain at invite time, and staff never supply a tenant. Kept on the contract so a client that sends one is told, rather
/// than having it silently ignored.
/// </param>
/// <param name="SendAsPortalAccount">
/// Not accepted: any supplied value, <c>false</c> included, is refused (400). The kind of sign-in an invite is for (a work
/// account or a portal account) is decided by the server from the email's domain, never chosen by the request. Kept on the
/// contract so a client that sends one is told, rather than having it silently ignored.
/// </param>
public record InviteExternalUserRequest(
    string Email,
    Guid ProjectId,
    int AccessLevel,
    string? FirstName,
    string? LastName,
    DateOnly? ExpiryDate,
    Guid? OrganizationId,
    string? RecordType = null,
    Guid? RecordId = null,
    string? InvitedTenantId = null,
    bool? SendAsPortalAccount = null);
