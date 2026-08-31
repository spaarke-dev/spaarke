using System.Text.Json.Serialization;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Identifies ONE logical external grant: a root record plus exactly one grantee.
/// </summary>
/// <remarks>
/// <para>The grantee is a Contact <b>or</b> an Organization, never both-as-identity. A row may carry an
/// <c>sprk_Organization</c> alongside a Contact — that is the contact's firm, recorded as association
/// metadata — but the row's IDENTITY is the Contact. An organization grant is the row with <b>no</b>
/// Contact at all, which is precisely how the read path distinguishes them
/// (<c>ExternalParticipationService</c>: <c>… and _sprk_contact_value eq null …</c>).</para>
///
/// <para>Consequence, and the reason this is a type rather than an inline string: a person grant and an
/// org grant on the SAME root are DISTINCT logical grants and must never revoke each other.</para>
/// </remarks>
internal readonly record struct ExternalGrantKey
{
    private ExternalGrantKey(ExternalGrantRootType rootType, Guid rootId, Guid? contactId, Guid? organizationId)
    {
        RootType = rootType;
        RootId = rootId;
        ContactId = contactId;
        OrganizationId = organizationId;
    }

    public ExternalGrantRootType RootType { get; }
    public Guid RootId { get; }

    /// <summary>The grantee contact, or <c>null</c> for an organization grant.</summary>
    public Guid? ContactId { get; }

    /// <summary>The grantee organization — meaningful only when <see cref="ContactId"/> is null.</summary>
    public Guid? OrganizationId { get; }

    public bool IsOrganizationGrant => ContactId is null;

    /// <summary>A grant held by one Contact on one root.</summary>
    public static ExternalGrantKey ForContact(ExternalGrantRootType rootType, Guid rootId, Guid contactId)
        => new(rootType, rootId, contactId, null);

    /// <summary>A grant held by one Organization on one root — every active member inherits at check time.</summary>
    public static ExternalGrantKey ForOrganization(ExternalGrantRootType rootType, Guid rootId, Guid organizationId)
        => new(rootType, rootId, null, organizationId);

    /// <summary>
    /// The OData <c>$filter</c> selecting every ACTIVE row for this logical grant.
    /// </summary>
    /// <remarks>
    /// Deliberately mirrors <c>ExternalParticipationService</c>'s read filters term for term, including
    /// the <c>_sprk_contact_value eq null</c> clause that isolates organization grants. If the write side
    /// and the read side ever disagree about what constitutes "the same grant", revocation stops matching
    /// what the participation surface displays — which is finding A-11's shape.
    /// </remarks>
    public string ToActiveRowsFilter()
    {
        var rootColumn = ExternalGrantRoot.ValueColumnFor(RootType);

        var granteeClause = IsOrganizationGrant
            // An org grant is identified by the ABSENCE of a contact. Without the null clause this would
            // also match every person grant whose contact happens to belong to that organization.
            ? $"_sprk_organization_value eq {OrganizationId} and _sprk_contact_value eq null"
            : $"_sprk_contact_value eq {ContactId}";

        return $"{rootColumn} eq {RootId} and {granteeClause} and statecode eq 0";
    }

    public override string ToString() => IsOrganizationGrant
        ? $"{RootType}:{RootId}/org:{OrganizationId}"
        : $"{RootType}:{RootId}/contact:{ContactId}";
}

/// <summary>
/// One <c>sprk_externalrecordaccess</c> row, in the shape both the grant upsert and the revoke sweep read.
/// </summary>
internal sealed class ExternalGrantRow
{
    [JsonPropertyName("sprk_externalrecordaccessid")]
    public Guid Id { get; set; }

    [JsonPropertyName("sprk_accesslevel")]
    public int? AccessLevel { get; set; }

    [JsonPropertyName("statecode")]
    public int? StateCode { get; set; }

    [JsonPropertyName("_sprk_contact_value")]
    public Guid? ContactId { get; set; }

    [JsonPropertyName("_sprk_organization_value")]
    public Guid? OrganizationId { get; set; }

    [JsonPropertyName("_sprk_project_value")]
    public Guid? ProjectId { get; set; }

    [JsonPropertyName("_sprk_matter_value")]
    public Guid? MatterId { get; set; }

    [JsonPropertyName("_sprk_workassignment_value")]
    public Guid? WorkAssignmentId { get; set; }

    /// <summary>Dataverse active state for this table.</summary>
    public bool IsActive => StateCode is null or 0;
}

/// <summary>
/// Shared read/write operations on <c>sprk_externalrecordaccess</c> rows, used by BOTH
/// <c>/external-access/grant</c> and <c>/external-access/revoke</c>.
/// </summary>
/// <remarks>
/// <para><b>Why this exists</b> (unified-access-control-r2 task 010, spec FR-09, finding A-11).
/// Grant and revoke each spoke to Dataverse directly and shared no notion of what "the same grant"
/// means — which is exactly how they came to disagree: <c>/grant</c> created unconditionally while
/// <c>/revoke</c> deactivated one row by id. Two identical grants produced two active rows, and revoking
/// one left the other standing. The read path then hid it: <c>QueryGrantSetAsync</c> collapses duplicates
/// with <c>GroupBy(root).Max(level)</c> and never surfaces access-record ids, so no effective-access view
/// could reveal that N active rows backed one logical grant.</para>
///
/// <para><b>§11 justification.</b> <i>Existing</i>: nothing — there was no shared grant-row abstraction,
/// which is the defect. <i>Extension</i>: <see cref="ExternalGrantRoot"/> is extended with
/// <c>ValueColumnFor</c> rather than duplicated. <i>Cost of doing nothing</i>: the upsert-match filter and
/// the revoke-sweep filter would be built independently, and a single divergent predicate — say revoke
/// omitting <c>_sprk_contact_value eq null</c> — would make revoking an organization grant silently sweep
/// a person's grant on the same root. Concrete, silent, and a privilege change.</para>
/// </remarks>
internal static class ExternalGrantLifecycle
{
    internal const string EntitySet = "sprk_externalrecordaccesses";

    private const string RowSelect =
        "sprk_externalrecordaccessid,sprk_accesslevel,statecode," +
        "_sprk_contact_value,_sprk_organization_value," +
        "_sprk_project_value,_sprk_matter_value,_sprk_workassignment_value";

    /// <summary>
    /// Every ACTIVE row for one logical grant, ordered deterministically (ascending id).
    /// </summary>
    /// <remarks>
    /// <para>The ordering is load-bearing for the concurrent-grant race: two racers that both observe the
    /// same duplicate set must elect the SAME survivor, or they would deactivate each other's row and
    /// leave zero grants. Ascending id is stable and clock-independent, unlike <c>createdon</c> which can
    /// tie.</para>
    ///
    /// <para><b>Rows without a usable id are discarded.</b> A row whose
    /// <c>sprk_externalrecordaccessid</c> did not materialise is not addressable — it cannot be updated
    /// or deactivated — so treating it as an existing grant would make the upsert a silent no-op that
    /// still reports success, and would aim an update at <see cref="Guid.Empty"/>. Discarding is the
    /// fail-safe reading: the caller creates a real row instead of adopting an unusable one.</para>
    /// </remarks>
    internal static async Task<List<ExternalGrantRow>> QueryActiveRowsAsync(
        DataverseWebApiClient dataverseClient, ExternalGrantKey key, CancellationToken ct)
    {
        var rows = await dataverseClient.QueryAsync<ExternalGrantRow>(
            EntitySet,
            filter: key.ToActiveRowsFilter(),
            select: RowSelect,
            cancellationToken: ct);

        return rows
            .Where(r => r.Id != Guid.Empty)
            .OrderBy(r => r.Id)
            .ToList();
    }

    /// <summary>
    /// Reads one row by id, or <c>null</c> when it does not exist.
    /// </summary>
    internal static Task<ExternalGrantRow?> RetrieveRowAsync(
        DataverseWebApiClient dataverseClient, Guid accessRecordId, CancellationToken ct)
        => dataverseClient.RetrieveAsync<ExternalGrantRow>(EntitySet, accessRecordId, RowSelect, ct);

    /// <summary>
    /// Derives the logical key a row belongs to.
    /// </summary>
    /// <returns><c>null</c> when the row carries no root lookup, so no key can be derived.</returns>
    /// <remarks>
    /// A null return is NOT recoverable on the revoke path. Per this task's ADR-003 constraint — "/revoke
    /// must never report success while any matching active row remains unqueried" — a row whose siblings
    /// cannot be identified must fail the revoke loudly rather than degrade to deactivating only the
    /// target, which would be the silent partial revocation A-11 already describes.
    /// </remarks>
    internal static ExternalGrantKey? DeriveKey(ExternalGrantRow row)
    {
        ExternalGrantRootType rootType;
        Guid rootId;

        if (row.ProjectId is { } projectId)
        {
            rootType = ExternalGrantRootType.Project;
            rootId = projectId;
        }
        else if (row.MatterId is { } matterId)
        {
            rootType = ExternalGrantRootType.Matter;
            rootId = matterId;
        }
        else if (row.WorkAssignmentId is { } workAssignmentId)
        {
            rootType = ExternalGrantRootType.WorkAssignment;
            rootId = workAssignmentId;
        }
        else
        {
            return null;
        }

        // Contact identity wins: a row with BOTH a contact and an organization is a person grant whose
        // firm is recorded as metadata, not an org grant.
        if (row.ContactId is { } contactId)
            return ExternalGrantKey.ForContact(rootType, rootId, contactId);

        if (row.OrganizationId is { } organizationId)
            return ExternalGrantKey.ForOrganization(rootType, rootId, organizationId);

        // A root but no grantee at all — same unrecoverable class as no root.
        return null;
    }

    /// <summary>
    /// Deactivates rows (statecode=1, statuscode=2). Exceptions propagate: a partial sweep must surface
    /// as a failure, never as a success with rows still active.
    /// </summary>
    internal static async Task<int> DeactivateAsync(
        DataverseWebApiClient dataverseClient,
        IEnumerable<Guid> accessRecordIds,
        ILogger logger,
        CancellationToken ct)
    {
        var deactivated = 0;

        foreach (var id in accessRecordIds)
        {
            await dataverseClient.UpdateAsync(EntitySet, id, new { statecode = 1, statuscode = 2 }, ct);
            deactivated++;
            logger.LogInformation("[EXT-GRANT-LIFECYCLE] Deactivated access record {AccessRecordId}", id);
        }

        return deactivated;
    }
}
