namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// The polymorphic root that a Tier-2 external grant (<c>sprk_externalrecordaccess</c>) can be held at.
/// Write-side mirror of the read-side roots in <see cref="AccessibleRecordSetService"/>
/// (Project · Matter · WorkAssignment). Task 070 (companion to task 028's polymorphic reads).
/// </summary>
public enum ExternalGrantRootType
{
    Project,
    Matter,
    WorkAssignment
}

/// <summary>
/// Maps an <see cref="ExternalGrantRootType"/> to the Dataverse <c>@odata.bind</c> navigation property
/// and entity set used when WRITING a grant row, and parses the wire <c>recordType</c> token.
///
/// <para>
/// Nav-property convention verified on <c>sprk_externalrecordaccess</c> (task 070): a lookup attribute
/// <c>sprk_X</c> exposes the single-valued navigation property <c>sprk_Xid</c>. Proven live on
/// <c>sprk_contact → sprk_contactid</c> and <c>sprk_project → sprk_projectid</c> (both already bound by
/// the shipped grant path); <c>sprk_matter</c>/<c>sprk_workassignment</c> follow the same owner-created
/// shape (owner-confirmed). Read/filter value fields are <c>_sprk_project_value</c> /
/// <c>_sprk_matter_value</c> / <c>_sprk_workassignment_value</c> (task 028).
/// </para>
/// </summary>
internal static class ExternalGrantRoot
{
    /// <summary>
    /// The <c>@odata.bind</c> navigation property + target entity set for a grant root type.
    /// </summary>
    public static (string NavigationProperty, string EntitySet) BindFor(ExternalGrantRootType type) => type switch
    {
        ExternalGrantRootType.Project => ("sprk_projectid", "sprk_projects"),
        ExternalGrantRootType.Matter => ("sprk_matterid", "sprk_matters"),
        ExternalGrantRootType.WorkAssignment => ("sprk_workassignmentid", "sprk_workassignments"),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown external grant root type.")
    };

    /// <summary>
    /// Parses a wire <c>recordType</c> token (case-insensitive) into an <see cref="ExternalGrantRootType"/>.
    /// Accepts <c>project</c> | <c>matter</c> | <c>workassignment</c> (hyphen/underscore spellings of the
    /// last are also accepted). Returns <c>false</c> for null/empty/unknown so callers reject fail-closed.
    /// </summary>
    public static bool TryParse(string? raw, out ExternalGrantRootType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var normalized = raw.Trim().ToLowerInvariant().Replace("-", string.Empty).Replace("_", string.Empty);
        switch (normalized)
        {
            case "project":
                type = ExternalGrantRootType.Project;
                return true;
            case "matter":
                type = ExternalGrantRootType.Matter;
                return true;
            case "workassignment":
                type = ExternalGrantRootType.WorkAssignment;
                return true;
            default:
                return false;
        }
    }
}
