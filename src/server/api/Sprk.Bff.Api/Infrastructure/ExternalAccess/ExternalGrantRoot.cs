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
/// Nav-property names verified against LIVE Dataverse <c>$metadata</c>
/// (<c>EntityDefinitions('sprk_externalrecordaccess')/ManyToOneRelationships</c>, task 070): the
/// single-valued navigation property for a lookup attribute <c>sprk_x</c> is the PascalCase
/// <c>sprk_X</c> (e.g. <c>sprk_project → sprk_Project</c>), NOT <c>sprk_xid</c>. The <c>sprk_xid</c> form
/// the original (never-live-tested) teams-app-r1 grant path used was wrong and 400'd every grant. The
/// bind VALUE uses the plural entity-set name (<c>/sprk_projects({id})</c>). Read/filter value fields are
/// <c>_sprk_project_value</c> / <c>_sprk_matter_value</c> / <c>_sprk_workassignment_value</c> (task 028).
/// </para>
/// </summary>
internal static class ExternalGrantRoot
{
    /// <summary>
    /// The <c>@odata.bind</c> navigation property (PascalCase, verified live) + target entity set
    /// (plural collection name) for a grant root type.
    /// </summary>
    public static (string NavigationProperty, string EntitySet) BindFor(ExternalGrantRootType type) => type switch
    {
        ExternalGrantRootType.Project => ("sprk_Project", "sprk_projects"),
        ExternalGrantRootType.Matter => ("sprk_Matter", "sprk_matters"),
        ExternalGrantRootType.WorkAssignment => ("sprk_WorkAssignment", "sprk_workassignments"),
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
