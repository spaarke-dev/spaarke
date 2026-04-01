namespace Sprk.Bff.Api.Api.Reporting;

/// <summary>
/// Privilege levels for the Reporting module, derived from Dataverse security role claims.
/// Stored in HttpContext.Items["ReportingPrivilegeLevel"] by ReportingAuthorizationFilter
/// for use by individual endpoint handlers without re-checking claims.
/// </summary>
/// <remarks>
/// Privilege levels map to Dataverse security roles as follows:
/// <list type="bullet">
///   <item><c>Viewer</c> — sprk_ReportingAccess role only; may embed and view reports</item>
///   <item><c>Author</c> — sprk_ReportingAuthor role; may create and edit reports</item>
///   <item><c>Admin</c> — sprk_ReportingAdmin role; may manage workspaces and catalog</item>
/// </list>
/// The Viewer level is the minimum required level — all authenticated users with
/// sprk_ReportingAccess implicitly have at least Viewer privilege.
/// </remarks>
public enum ReportingPrivilegeLevel
{
    /// <summary>
    /// User may view and interact with embedded reports.
    /// Requires the sprk_ReportingAccess Dataverse security role.
    /// </summary>
    Viewer,

    /// <summary>
    /// User may view reports and save new reports to the catalog.
    /// Requires the sprk_ReportingAuthor Dataverse security role.
    /// </summary>
    Author,

    /// <summary>
    /// User may view, author, and administer workspaces and the report catalog.
    /// Requires the sprk_ReportingAdmin Dataverse security role.
    /// </summary>
    Admin
}
