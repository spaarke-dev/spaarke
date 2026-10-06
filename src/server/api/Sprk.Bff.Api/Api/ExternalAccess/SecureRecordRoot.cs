using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// The three tables that carry <c>sprk_issecure</c> — and therefore the three a secure/unsecure request can
/// target — described ONCE (task 144). Entity set and logical name come from <see cref="ExternalGrantRoot"/>, the
/// existing root table for the same three types; this adds only what provisioning needs on top: the id and name
/// columns and the container label.
/// </summary>
/// <remarks>
/// Column names verified against live metadata 2026-10-01: all three tables carry <c>sprk_issecure</c>,
/// <c>sprk_containerid</c>, <c>sprk_securitybu</c>, <c>owningteam</c>, <c>owninguser</c> and
/// <c>owningbusinessunit</c>. The NAME column differs per table — <c>sprk_projectname</c>,
/// <c>sprk_mattername</c>, <c>sprk_name</c> (the same three <c>GrantExpiryReminderJob.Roots</c> uses) — which is
/// why it lives in this table rather than being derived.
/// </remarks>
internal sealed record SecureRecordRoot(
    ExternalGrantRootType Type,
    string NameColumn,
    string DisplayLabel)
{
    /// <summary>Dataverse entity set (plural) — the URL segment and the delegation filter's target.</summary>
    public string EntitySet => ExternalGrantRoot.BindFor(Type).EntitySet;

    /// <summary>Dataverse logical name — what the POA share reads address.</summary>
    public string LogicalName => ExternalGrantRoot.LogicalNameFor(Type);

    /// <summary>The primary key column, <c>{logicalname}id</c>.</summary>
    public string IdColumn => $"{LogicalName}id";

    /// <summary>The wire token for this type — the same one <see cref="ExternalGrantRoot.TryParse"/> accepts.</summary>
    public string WireToken => Type switch
    {
        ExternalGrantRootType.Project => "project",
        ExternalGrantRootType.Matter => "matter",
        ExternalGrantRootType.WorkAssignment => "workassignment",
        _ => throw new ArgumentOutOfRangeException(nameof(Type), Type, "Unknown secure root type.")
    };

    /// <summary>
    /// The columns provisioning reads in Step 1. Every name here exists on all three tables (live metadata). Pinned
    /// for the project table by <c>ProjectProvisioningSelect_NamesOnlyColumnsThatExistOnTheTable</c>.
    /// </summary>
    /// <remarks>
    /// Task 133 added <c>_owninguser_value</c> (with <c>_owningteam_value</c>, the owner a failed provisioning moves the
    /// record back to) and <c>_createdby_value</c> (the person a resumed provisioning shares to when it is a usable person;
    /// otherwise <see cref="CreatorPersonSelect"/>'s column).
    /// </remarks>
    public string ProvisioningSelect =>
        $"{IdColumn},{NameColumn},sprk_issecure,sprk_containerid," +
        "_sprk_securitybu_value,_owningteam_value,_owninguser_value,_owningbusinessunit_value,_createdby_value";

    /// <summary>
    /// The resume's read of the server-stamped creator person (task 133, owner round 7 item 2) — deliberately NOT part of
    /// <see cref="ProvisioningSelect"/>: the column is created by <c>scripts/Set-RecordCreatorPersonSchema.ps1</c>, and a
    /// Step-1 select naming it would 400 every provisioning in an environment where that script has not run yet.
    /// </summary>
    public string CreatorPersonSelect => $"{IdColumn},{RecordCreatorPerson.ValueColumn}";

    /// <summary>The SPE container display name for a record of this type.</summary>
    public string ContainerDisplayName(string recordName) => $"Secure {DisplayLabel} — {recordName}";

    /// <summary>The SPE container description for a record of this type.</summary>
    public string ContainerDescription(string recordName) =>
        $"Isolated document container for Secure {DisplayLabel}: {recordName}";

    public static readonly SecureRecordRoot Project = new(ExternalGrantRootType.Project, "sprk_projectname", "Project");
    public static readonly SecureRecordRoot Matter = new(ExternalGrantRootType.Matter, "sprk_mattername", "Matter");
    public static readonly SecureRecordRoot WorkAssignment =
        new(ExternalGrantRootType.WorkAssignment, "sprk_name", "Work Assignment");

    /// <summary>
    /// The three roots, in a fixed order (task 133: a container already recorded on one is checked against all three
    /// before provisioning keeps it).
    /// </summary>
    public static readonly IReadOnlyList<SecureRecordRoot> All = new[] { Project, Matter, WorkAssignment };

    /// <summary>The descriptor for a root type. Exhaustive; an unknown type throws rather than guessing.</summary>
    public static SecureRecordRoot For(ExternalGrantRootType type) => type switch
    {
        ExternalGrantRootType.Project => Project,
        ExternalGrantRootType.Matter => Matter,
        ExternalGrantRootType.WorkAssignment => WorkAssignment,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown secure root type.")
    };

    /// <summary>
    /// Resolves the ONE record a secure/unsecure request targets: an explicit <c>RecordType</c> + <c>RecordId</c>
    /// wins; otherwise the legacy <c>ProjectId</c> maps to a project. Delegates to
    /// <see cref="GrantExternalAccessEndpoint.ResolveGrantRoot"/> so these routes accept exactly the shape the
    /// grant routes do, and fail closed the same way (unknown type, explicit type without id, no root at all).
    /// The delegation filter and the handler both call this, so the record authorized is the record re-owned.
    /// </summary>
    public static GrantExternalAccessEndpoint.GrantRootResolution ResolveTarget(
        Guid legacyProjectId, string? recordType, Guid? recordId)
        => GrantExternalAccessEndpoint.ResolveGrantRoot(new Dtos.GrantAccessRequest(
            ContactId: Guid.Empty,          // irrelevant to root resolution
            ProjectId: legacyProjectId,
            AccessLevel: default,
            ExpiryDate: null,
            OrganizationId: null,
            RecordType: recordType,
            RecordId: recordId));
}

