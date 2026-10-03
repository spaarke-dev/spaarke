using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Services.ExternalAccess;

// unified-access-control-r2 task 142 (GitHub #1065) — the provenance ledger behind the Assigned-To auto-grants
// (owner round 2 item 5 + Q5, round 3 A1/A2/R3): which "Assigned *" subject on which root the invariant owner
// (AssignedAccessMaterializer) granted, shared, found covered, is waiting on, skipped, or was told to leave alone.

/// <summary>
/// The state of one ledger row — one (root, source field, subject). Option values of the <c>sprk_state</c> choice on
/// <c>sprk_assignedaccess</c> (schema: <c>src/solutions/SpaarkeCore/entities/sprk_assignedaccess/entity-schema.md</c>).
/// </summary>
public enum AssignedAccessState
{
    /// <summary>The materializer wrote (or raised) a <c>sprk_externalrecordaccess</c> grant for this subject.</summary>
    Granted = 100000000,

    /// <summary>The materializer wrote (or raised) a POA share to the subject's linked internal systemuser.</summary>
    Shared = 100000001,

    /// <summary>The subject already held equal or higher access the materializer did not create; nothing written.</summary>
    CoveredByExisting = 100000002,

    /// <summary>A SECURE root: the operator is asked (owner answer A3 = prompt). Grant / Dismiss in Manage Access.</summary>
    PendingConfirmation = 100000003,

    /// <summary>Not written, for the reason in <c>sprk_reason</c>. Re-evaluated on every pass (never sticky).</summary>
    Skipped = 100000004,

    /// <summary>An operator removed it (or dismissed the suggestion). Sticky while the assignment persists.</summary>
    Declined = 100000005,

    /// <summary>A manual grant or share landed on the subject. Never revoked by the Assigned-To rule.</summary>
    Adopted = 100000006,

    /// <summary>The assignment ended (field cleared or changed). <c>sprk_reason</c> says whether access was removed.</summary>
    Revoked = 100000007,
}

/// <summary>Stable <c>sprk_reason</c> values (ledger) and outcome reasons (sync / job report).</summary>
public static class AssignedAccessReason
{
    /// <summary>The record is Restricted: no contact or organization access (owner round 2 item 3).</summary>
    public const string Restricted = "restricted";

    /// <summary>An organization on a SECURE record: only named, direct grants count (owner round 2 item 3).</summary>
    public const string OrganizationOnSecure = "organization-on-secure";

    /// <summary>An organization on a LIMITED record: named, direct grants only (task 138).</summary>
    public const string OrganizationOnLimited = "organization-on-limited";

    /// <summary>The record is inactive (closed): it confers nothing contact-sourced (task 137).</summary>
    public const string RootInactive = "root-inactive";

    /// <summary>On the record's No Access list (FR-23 / owner Q4), or the list could not be checked.</summary>
    public const string NoAccess = "no-access";

    /// <summary>
    /// A No Access check could not be completed: task 143's secure-record wall guard answered Unverifiable, or (task 142
    /// r3) a contact/organization deny-list check THREW — the latter also reported as a
    /// <c>deny-list-unreadable</c> failure. Never a grant, never "no-access" (an entry).
    /// </summary>
    public const string NoAccessUnverifiable = "no-access-unverifiable";

    /// <summary>The auto share was removed by task 143's No Access enforcer: restored once the wall is lifted.</summary>
    public const string RemovedByNoAccess = "removed-by-no-access";

    /// <summary>The root's access flags could not be read: no write this pass (ADR-003).</summary>
    public const string FlagsUnreadable = "flags-unreadable";

    /// <summary>The linked systemuser is disabled or is not a person: no share, no grant.</summary>
    public const string Ineligible = "ineligible";

    /// <summary>The user↔contact link could not be read (task 141): nothing written.</summary>
    public const string LinkUnreadable = "link-unreadable";

    /// <summary>The contact represents more than one systemuser: never pick one of two (task 141 §5).</summary>
    public const string LinkAmbiguous = "link-ambiguous";

    /// <summary>The named contact or organization is inactive.</summary>
    public const string SubjectInactive = "subject-inactive";

    /// <summary>The named contact or organization does not exist.</summary>
    public const string SubjectNotFound = "subject-not-found";

    /// <summary>The organization's state could not be read.</summary>
    public const string SubjectUnreadable = "subject-unreadable";

    /// <summary>An operator revoked the grant or removed the share in Manage Access.</summary>
    public const string RemovedByOperator = "removed-by-operator";

    /// <summary>An operator dismissed the suggestion on a secure record.</summary>
    public const string Dismissed = "dismissed";

    /// <summary>The grant or share vanished outside the BFF (MDA form, grid, Web API, flow, OOB Share dialog).</summary>
    public const string RemovedOutOfBand = "removed-out-of-band";

    /// <summary>The subject's access was removed while another row of the same subject was already declined.</summary>
    public const string DeclinedOnAnotherField = "declined-on-another-field";

    /// <summary>A manual /grant or /share-user landed on the subject.</summary>
    public const string ManualGrant = "manual-grant";

    /// <summary>The assignment ended and the auto access was removed.</summary>
    public const string AccessRemoved = "access-removed";

    /// <summary>The assignment ended and a raised grant/share was put back to the level it had before.</summary>
    public const string PriorLevelRestored = "prior-level-restored";

    /// <summary>
    /// The assignment ended; a raised grant's earlier level AND date were put back, but that date has passed, so the grant
    /// confers nothing — no access was restored (ADR-003: never reported as done). Either the grant had already lapsed, or
    /// the rule's own renewal had kept it alive past the operator's date and putting the date back ended the access (the
    /// entry's action then says <c>revoked</c>).
    /// </summary>
    public const string PriorLevelRestoredLapsed = "prior-level-restored-lapsed";

    /// <summary>The assignment ended; another registry column still names the subject, so access is kept.</summary>
    public const string KeptOtherField = "kept-other-field";

    /// <summary>The assignment ended; the auto access was changed since (level/expiry/mask), so it is kept.</summary>
    public const string KeptModified = "kept-modified";

    /// <summary>The assignment ended; the access was adopted by a manual grant, so it is kept.</summary>
    public const string KeptAdopted = "kept-adopted";

    /// <summary>The assignment ended on a SECURE record: a share is never removed by this rule there (S5 safety).</summary>
    public const string KeptSecureRecord = "kept-secure-record";

    /// <summary>The assignment ended; there was no auto access to remove.</summary>
    public const string AssignmentEnded = "assignment-ended";

    /// <summary>
    /// Prefix of a reason recording what a raised grant had before: its level AND the date the subject's access ran until
    /// (<c>raised-from:100000000@2026-10-13</c>, written by <see cref="RaisedFromLevel"/>). Both are put back when the
    /// assignment ends — the rule renews a raised grant like its own while the assignment lasts (owner A5), so restoring
    /// only the level would leave the operator's grant extended by the rule (task 142 r2, finding 1).
    /// </summary>
    public const string RaisedFromLevelPrefix = "raised-from:";

    /// <summary>The reason recording a raise: the earlier level and the earlier expiry (<see cref="RaisedFromLevelPrefix"/>).</summary>
    public static string RaisedFromLevel(int level, DateOnly expiry)
        => RaisedFromLevelPrefix + level.ToString(CultureInfo.InvariantCulture) + "@"
           + expiry.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// Prefix of an OUTCOME reason (never written to the ledger): a raised grant's assignment ended, but the grant core
    /// refuses to write this grantee kind on the record now (<c>restore-pending:sdap.access.grant.…</c> — Restricted, an
    /// organization on a Secure or Limited record, or the No Access list). Nothing is written and nothing is exposed (the
    /// read path suppresses the same grant); the ledger row stays <see cref="AssignedAccessState.Granted"/>, so every pass
    /// tries again and the restore happens once the record's policy allows it (task 142 r2, finding 2).
    /// </summary>
    public const string RestorePendingPrefix = "restore-pending:";

    /// <summary>Prefix of a reason recording the share mask a raised share had before (<c>raised-from-mask:1</c>).</summary>
    public const string RaisedFromMaskPrefix = "raised-from-mask:";

    /// <summary>The grant was converted to a POA share because the contact is now linked to an internal user (141).</summary>
    public const string ConvertedFromGrant = "converted-from-grant";

    /// <summary>Prefix of a reason carrying a grant-core refusal code (<c>grant-refused:sdap.access.grant.…</c>).</summary>
    public const string GrantRefusedPrefix = "grant-refused:";
}

/// <summary>Who a ledger row is about: a contact or an organization named in an "Assigned *" column.</summary>
public enum AssignedSubjectKind
{
    Contact,
    Organization,
}

/// <summary>One "Assigned *" subject: its kind and id.</summary>
public readonly record struct AssignedSubject(AssignedSubjectKind Kind, Guid Id)
{
    /// <summary>The ledger key segment (<c>contact:{id}</c> / <c>organization:{id}</c>).</summary>
    public string KeySegment => $"{(Kind == AssignedSubjectKind.Contact ? "contact" : "organization")}:{Id:D}";

    public override string ToString() => KeySegment;
}

/// <summary>One <c>sprk_assignedaccess</c> row, as read.</summary>
public sealed class AssignedAccessLedgerRow
{
    [JsonPropertyName("sprk_assignedaccessid")]
    public Guid Id { get; set; }

    [JsonPropertyName("sprk_ledgerkey")]
    public string? LedgerKey { get; set; }

    [JsonPropertyName("sprk_sourcefield")]
    public string? SourceField { get; set; }

    [JsonPropertyName("_sprk_project_value")]
    public Guid? ProjectId { get; set; }

    [JsonPropertyName("_sprk_matter_value")]
    public Guid? MatterId { get; set; }

    [JsonPropertyName("_sprk_workassignment_value")]
    public Guid? WorkAssignmentId { get; set; }

    [JsonPropertyName("_sprk_subjectcontact_value")]
    public Guid? SubjectContactId { get; set; }

    [JsonPropertyName("_sprk_subjectorganization_value")]
    public Guid? SubjectOrganizationId { get; set; }

    [JsonPropertyName("_sprk_subjectsystemuser_value")]
    public Guid? SystemUserId { get; set; }

    [JsonPropertyName("_sprk_externalrecordaccess_value")]
    public Guid? GrantId { get; set; }

    [JsonPropertyName("sprk_state")]
    public int? StateValue { get; set; }

    [JsonPropertyName("sprk_reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("sprk_grantedlevel")]
    public int? GrantedLevel { get; set; }

    [JsonPropertyName("sprk_grantedexpiry")]
    public DateOnly? GrantedExpiry { get; set; }

    /// <summary>The state; an unknown or missing value reads as <see cref="AssignedAccessState.Skipped"/> — re-evaluated, never trusted.</summary>
    [JsonIgnore]
    public AssignedAccessState State =>
        StateValue is { } v && Enum.IsDefined(typeof(AssignedAccessState), v) ? (AssignedAccessState)v : AssignedAccessState.Skipped;

    /// <summary>The subject this row is about, or <c>null</c> for a row naming neither (malformed — ignored).</summary>
    [JsonIgnore]
    public AssignedSubject? Subject =>
        SubjectContactId is { } c && c != Guid.Empty ? new AssignedSubject(AssignedSubjectKind.Contact, c)
        : SubjectOrganizationId is { } o && o != Guid.Empty ? new AssignedSubject(AssignedSubjectKind.Organization, o)
        : null;
}

/// <summary>The values one ledger write sets. Every write SETS a value; none clears a lookup.</summary>
public sealed record AssignedAccessLedgerWrite(
    AssignedAccessState State,
    string? Reason,
    Guid? GrantId = null,
    Guid? SystemUserId = null,
    int? GrantedLevel = null,
    DateOnly? GrantedExpiry = null);

/// <summary>A root's registry-column values, as read (<c>null</c> value = column empty).</summary>
public sealed record AssignedRootSnapshot(IReadOnlyDictionary<string, Guid?> Values);

/// <summary>A systemuser that may be represented by an assigned contact — the reverse of task 141's link.</summary>
public sealed record AssignedLinkCandidate(
    Guid SystemUserId,
    Guid? PrimaryContactId,
    Guid? Oid,
    bool? IsDisabled,
    int? AccessMode,
    Guid? ApplicationId,
    bool? IsExternal);

/// <summary>A root a reconciliation scan found, and when it last changed.</summary>
public readonly record struct AssignedRootRef(ExternalGrantRootType RootType, Guid RootId, DateTimeOffset? ModifiedOn);

/// <summary>
/// The Dataverse reads and writes of the Assigned-To auto-grant ledger (unified-access-control-r2 task 142), app-only
/// over the BFF's <see cref="DataverseWebApiClient"/>. Grants and shares are NOT written here — they go through the
/// grant core and the POA share seam (CLAUDE.md §11 reuse); this class owns only the ledger and the reads the
/// materializer needs that no existing reader answers (a root's registry columns, the systemusers a contact represents,
/// an organization's state, the reconciliation scans).
/// </summary>
/// <remarks>
/// <para><b>Test seam</b>: every method is <c>internal virtual</c> (the <c>NoAccessListReader.QueryChunkAsync</c> /
/// <c>NoAccessEnforcementStore</c> convention, ADR-038: no HTTP doubles). Production reaches Dataverse only through
/// <see cref="DataverseWebApiClient"/>'s own virtual methods.</para>
/// <para><b>Uniqueness</b>: one row per (root, source field, subject), enforced by the alternate key on the BFF-computed
/// single string <c>sprk_ledgerkey</c> (<see cref="LedgerKey"/>) — not a key over three nullable typed lookups, whose
/// null handling the platform does not document for this shape. A create that loses a race to the key is re-read and
/// updated instead.</para>
/// <para><b>Who may write</b>: only the BFF application user; no Spaarke user role holds Create/Write/Delete on the table
/// (schema doc §Security). Users never read it directly — Manage Access reads it through the BFF.</para>
/// </remarks>
public class AssignedAccessStore
{
    internal const string EntitySet = "sprk_assignedaccesses";
    internal const string EntityLogicalName = "sprk_assignedaccess";

    /// <summary>Rows per scan; a scan that fills the page reports TRUNCATED rather than a silent prefix.</summary>
    internal const int MaxScanRows = 4999;

    private const string LedgerSelect =
        "sprk_assignedaccessid,sprk_ledgerkey,sprk_sourcefield,_sprk_project_value,_sprk_matter_value," +
        "_sprk_workassignment_value,_sprk_subjectcontact_value,_sprk_subjectorganization_value," +
        "_sprk_subjectsystemuser_value,_sprk_externalrecordaccess_value,sprk_state,sprk_reason,sprk_grantedlevel," +
        "sprk_grantedexpiry";

    private const string LinkCandidateSelect =
        "systemuserid,_sprk_primarycontact_value,azureactivedirectoryobjectid,isdisabled,accessmode,applicationid," +
        "sprk_isexternal";

    private readonly DataverseWebApiClient _dataverse;
    private readonly ILogger<AssignedAccessStore> _logger;

    public AssignedAccessStore(DataverseWebApiClient dataverse, ILogger<AssignedAccessStore> logger)
    {
        _dataverse = dataverse;
        _logger = logger;
    }

    // ── Keys and binds ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The ledger key: <c>{rootLogicalName}:{rootId}:{sourceField}:{subjectKind}:{subjectId}</c>, lower-case "D" GUIDs.
    /// The one place it is computed, so a create and a lookup can never disagree.
    /// </summary>
    public static string LedgerKey(ExternalGrantRootType rootType, Guid rootId, string sourceField, AssignedSubject subject)
        => string.Create(CultureInfo.InvariantCulture,
            $"{ExternalGrantRoot.LogicalNameFor(rootType)}:{rootId:D}:{sourceField.Trim().ToLowerInvariant()}:{subject.KeySegment}");

    /// <summary>The ledger's typed root lookup — its value column (filters) and its navigation property (binds).</summary>
    internal static (string ValueColumn, string NavigationProperty) RootLookupFor(ExternalGrantRootType type) => type switch
    {
        ExternalGrantRootType.Project => ("_sprk_project_value", "sprk_Project"),
        ExternalGrantRootType.Matter => ("_sprk_matter_value", "sprk_Matter"),
        ExternalGrantRootType.WorkAssignment => ("_sprk_workassignment_value", "sprk_WorkAssignment"),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown root type."),
    };

    /// <summary>The primary-key column of a root table (<c>sprk_projectid</c>).</summary>
    internal static string RootIdColumnFor(ExternalGrantRootType type) => ExternalGrantRoot.LogicalNameFor(type) + "id";

    /// <summary>The create payload of a new ledger row.</summary>
    internal static Dictionary<string, object?> BuildCreatePayload(
        ExternalGrantRootType rootType, Guid rootId, string sourceField, AssignedSubject subject, AssignedAccessLedgerWrite write)
    {
        var (_, rootNav) = RootLookupFor(rootType);
        var rootSet = ExternalGrantRoot.BindFor(rootType).EntitySet;
        var payload = new Dictionary<string, object?>
        {
            ["sprk_name"] = Truncate($"{sourceField} → {subject.KeySegment}", 200),
            ["sprk_ledgerkey"] = LedgerKey(rootType, rootId, sourceField, subject),
            ["sprk_sourcefield"] = sourceField,
            [$"{rootNav}@odata.bind"] = $"/{rootSet}({rootId:D})",
            [subject.Kind == AssignedSubjectKind.Contact ? "sprk_SubjectContact@odata.bind" : "sprk_SubjectOrganization@odata.bind"] =
                subject.Kind == AssignedSubjectKind.Contact ? $"/contacts({subject.Id:D})" : $"/sprk_organizations({subject.Id:D})",
        };

        foreach (var (k, v) in BuildUpdatePayload(write))
            payload[k] = v;

        return payload;
    }

    /// <summary>The update payload for a write: state and reason always; the optional values only when set.</summary>
    internal static Dictionary<string, object?> BuildUpdatePayload(AssignedAccessLedgerWrite write)
    {
        var payload = new Dictionary<string, object?>
        {
            ["sprk_state"] = (int)write.State,
            ["sprk_reason"] = write.Reason is null ? null : Truncate(write.Reason, 100),
        };

        if (write.GrantId is { } grantId && grantId != Guid.Empty)
            payload["sprk_ExternalRecordAccess@odata.bind"] = $"/{ExternalGrantLifecycle.EntitySet}({grantId:D})";
        if (write.SystemUserId is { } userId && userId != Guid.Empty)
            payload["sprk_SubjectSystemUser@odata.bind"] = $"/systemusers({userId:D})";
        if (write.GrantedLevel is { } level)
            payload["sprk_grantedlevel"] = level;
        if (write.GrantedExpiry is { } expiry)
            payload["sprk_grantedexpiry"] = expiry.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return payload;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    // ── Ledger ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Every ledger row of one root (any state). Exceptions propagate: an unread ledger is never "empty".</summary>
    internal virtual async Task<IReadOnlyList<AssignedAccessLedgerRow>> ReadLedgerAsync(
        ExternalGrantRootType rootType, Guid rootId, CancellationToken ct)
    {
        var (valueColumn, _) = RootLookupFor(rootType);
        var rows = await _dataverse.QueryAsync<AssignedAccessLedgerRow>(
            EntitySet,
            filter: $"{valueColumn} eq {rootId:D} and statecode eq 0",
            select: LedgerSelect,
            cancellationToken: ct).ConfigureAwait(false);
        return rows.Where(r => r.Id != Guid.Empty).ToList();
    }

    /// <summary>Creates a ledger row; a duplicate-key loss (a concurrent pass created it) re-reads it and updates instead.</summary>
    internal virtual async Task<Guid> CreateLedgerAsync(
        ExternalGrantRootType rootType, Guid rootId, string sourceField, AssignedSubject subject,
        AssignedAccessLedgerWrite write, CancellationToken ct)
    {
        try
        {
            return await _dataverse.CreateAsync(
                EntitySet, BuildCreatePayload(rootType, rootId, sourceField, subject, write), ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.PreconditionFailed
                                                 or System.Net.HttpStatusCode.Conflict)
        {
            var key = LedgerKey(rootType, rootId, sourceField, subject);
            var existing = await _dataverse.QueryAsync<AssignedAccessLedgerRow>(
                EntitySet, filter: $"sprk_ledgerkey eq '{key}'", select: LedgerSelect, top: 1, cancellationToken: ct)
                .ConfigureAwait(false);
            if (existing.FirstOrDefault() is not { } row)
                throw;

            _logger.LogInformation(
                "[ASSIGNED-ACCESS] Ledger row {Key} was created concurrently; updating {RowId} instead.", key, row.Id);
            await UpdateLedgerAsync(row.Id, write, ct).ConfigureAwait(false);
            return row.Id;
        }
    }

    /// <summary>Updates a ledger row. Exceptions propagate.</summary>
    internal virtual Task UpdateLedgerAsync(Guid rowId, AssignedAccessLedgerWrite write, CancellationToken ct)
        => _dataverse.UpdateAsync(EntitySet, rowId, BuildUpdatePayload(write), ct);

    // ── Reads the materializer needs ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A root's registry-column values (lookup ids), or <c>null</c> when no such root exists. Exceptions propagate.
    /// </summary>
    internal virtual async Task<AssignedRootSnapshot?> ReadRootAsync(
        ExternalGrantRootType rootType, Guid rootId, IReadOnlyCollection<string> fields, CancellationToken ct)
    {
        var idColumn = RootIdColumnFor(rootType);
        var select = string.Join(",", new[] { idColumn }.Concat(fields.Select(f => $"_{f}_value")));
        var rows = await _dataverse.QueryAsync<JsonElement>(
            ExternalGrantRoot.BindFor(rootType).EntitySet,
            filter: $"{idColumn} eq {rootId:D}",
            select: select,
            top: 1,
            cancellationToken: ct).ConfigureAwait(false);

        if (rows.Count == 0)
            return null;

        var values = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            values[field] = rows[0].TryGetProperty($"_{field}_value", out var v)
                            && v.ValueKind == JsonValueKind.String
                            && Guid.TryParse(v.GetString(), out var id) && id != Guid.Empty
                ? id
                : null;
        }

        return new AssignedRootSnapshot(values);
    }

    /// <summary>
    /// The systemusers that may be represented by <paramref name="contactId"/>: those whose task-141 link
    /// (<c>sprk_primarycontact</c>) names it, and those whose Entra oid the contact's binding carries. The caller decides
    /// which of them the contact really represents. Exceptions propagate.
    /// </summary>
    internal virtual async Task<IReadOnlyList<AssignedLinkCandidate>> ReadLinkCandidatesAsync(
        Guid contactId, Guid? boundOid, CancellationToken ct)
    {
        var filter = $"_sprk_primarycontact_value eq {contactId:D}";
        if (boundOid is { } oid && oid != Guid.Empty)
            filter += $" or azureactivedirectoryobjectid eq {oid:D}";

        var rows = await _dataverse.QueryAsync<LinkCandidateRow>(
            "systemusers", filter: filter, select: LinkCandidateSelect, top: 10, cancellationToken: ct).ConfigureAwait(false);

        return rows
            .Where(r => r.SystemUserId != Guid.Empty)
            .Select(r => new AssignedLinkCandidate(
                r.SystemUserId, r.PrimaryContactId, r.Oid, r.IsDisabled, r.AccessMode, r.ApplicationId, r.IsExternal))
            .ToList();
    }

    /// <summary>
    /// An organization's <c>statecode</c>: <c>0</c> active, anything else inactive; <c>null</c> when no such row exists.
    /// Exceptions propagate.
    /// </summary>
    internal virtual async Task<int?> ReadOrganizationStateAsync(Guid organizationId, CancellationToken ct)
    {
        var rows = await _dataverse.QueryAsync<JsonElement>(
            "sprk_organizations",
            filter: $"sprk_organizationid eq {organizationId:D}",
            select: "sprk_organizationid,statecode",
            top: 1,
            cancellationToken: ct).ConfigureAwait(false);

        if (rows.Count == 0)
            return null;

        return rows[0].TryGetProperty("statecode", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 1;
    }

    /// <summary>Display names for contacts and organizations (Manage Access suggestions). Display only: a failure is empty.</summary>
    internal virtual async Task<IReadOnlyDictionary<Guid, string>> ReadSubjectNamesAsync(
        IReadOnlyCollection<AssignedSubject> subjects, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string>();
        try
        {
            foreach (var batch in subjects.Where(s => s.Kind == AssignedSubjectKind.Contact).Select(s => s.Id).Distinct().Chunk(50))
            {
                var rows = await _dataverse.QueryAsync<JsonElement>(
                    "contacts", filter: string.Join(" or ", batch.Select(id => $"contactid eq {id:D}")),
                    select: "contactid,fullname", top: batch.Length, cancellationToken: ct).ConfigureAwait(false);
                foreach (var row in rows)
                    AddName(names, row, "contactid", "fullname");
            }

            foreach (var batch in subjects.Where(s => s.Kind == AssignedSubjectKind.Organization).Select(s => s.Id).Distinct().Chunk(50))
            {
                var rows = await _dataverse.QueryAsync<JsonElement>(
                    "sprk_organizations", filter: string.Join(" or ", batch.Select(id => $"sprk_organizationid eq {id:D}")),
                    select: "sprk_organizationid,sprk_name", top: batch.Length, cancellationToken: ct).ConfigureAwait(false);
                foreach (var row in rows)
                    AddName(names, row, "sprk_organizationid", "sprk_name");
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[ASSIGNED-ACCESS] Subject names could not be read; suggestions are listed without names.");
        }

        return names;
    }

    private static void AddName(Dictionary<Guid, string> names, JsonElement row, string idColumn, string nameColumn)
    {
        if (row.TryGetProperty(idColumn, out var id) && id.ValueKind == JsonValueKind.String
            && Guid.TryParse(id.GetString(), out var guid)
            && row.TryGetProperty(nameColumn, out var name) && name.ValueKind == JsonValueKind.String)
        {
            names[guid] = name.GetString()!;
        }
    }

    // ── Reconciliation scans (the job) ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Roots of one type with ANY registry column set, newest change first. <c>Truncated</c> when more exist than one
    /// scan reads. Exceptions propagate.
    /// </summary>
    internal virtual async Task<(IReadOnlyList<AssignedRootRef> Roots, bool Truncated)> ScanAssignedRootsAsync(
        ExternalGrantRootType rootType, IReadOnlyCollection<string> fields, CancellationToken ct)
    {
        if (fields.Count == 0)
            return (Array.Empty<AssignedRootRef>(), false);

        var idColumn = RootIdColumnFor(rootType);
        var rows = await _dataverse.QueryAsync<JsonElement>(
            ExternalGrantRoot.BindFor(rootType).EntitySet,
            filter: "(" + string.Join(" or ", fields.Select(f => $"_{f}_value ne null")) + ")",
            select: $"{idColumn},modifiedon",
            top: MaxScanRows + 1,
            cancellationToken: ct).ConfigureAwait(false);

        var roots = rows
            .Select(r => (Id: ReadGuid(r, idColumn), Modified: ReadDate(r, "modifiedon")))
            .Where(r => r.Id is not null)
            .Select(r => new AssignedRootRef(rootType, r.Id!.Value, r.Modified))
            .ToList();

        return roots.Count > MaxScanRows ? (roots.Take(MaxScanRows).ToList(), true) : (roots, false);
    }

    /// <summary>
    /// Roots that hold a live ledger row (any state but Revoked) — so an assignment cleared OUTSIDE the product (grid
    /// edit, import, flow) is still revisited. Exceptions propagate.
    /// </summary>
    internal virtual async Task<(IReadOnlyList<AssignedRootRef> Roots, bool Truncated)> ScanLedgerRootsAsync(CancellationToken ct)
    {
        var rows = await _dataverse.QueryAsync<AssignedAccessLedgerRow>(
            EntitySet,
            filter: $"statecode eq 0 and sprk_state ne {(int)AssignedAccessState.Revoked}",
            select: "sprk_assignedaccessid,_sprk_project_value,_sprk_matter_value,_sprk_workassignment_value",
            top: MaxScanRows + 1,
            cancellationToken: ct).ConfigureAwait(false);

        var truncated = rows.Count > MaxScanRows;
        var roots = rows.Take(MaxScanRows)
            .Select(RootOf)
            .Where(r => r is not null)
            .Select(r => r!.Value)
            .Distinct()
            .ToList();
        return (roots, truncated);
    }

    /// <summary>The root a ledger row is held at, or <c>null</c> for a row with none.</summary>
    internal static AssignedRootRef? RootOf(AssignedAccessLedgerRow row)
        => row.ProjectId is { } p ? new AssignedRootRef(ExternalGrantRootType.Project, p, null)
            : row.MatterId is { } m ? new AssignedRootRef(ExternalGrantRootType.Matter, m, null)
            : row.WorkAssignmentId is { } w ? new AssignedRootRef(ExternalGrantRootType.WorkAssignment, w, null)
            : null;

    private static Guid? ReadGuid(JsonElement row, string column)
        => row.TryGetProperty(column, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g)
            ? g
            : null;

    private static DateTimeOffset? ReadDate(JsonElement row, string column)
        => row.TryGetProperty(column, out var v) && v.ValueKind == JsonValueKind.String
           && DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d
            : null;

    private sealed class LinkCandidateRow
    {
        [JsonPropertyName("systemuserid")]
        public Guid SystemUserId { get; set; }

        [JsonPropertyName("_sprk_primarycontact_value")]
        public Guid? PrimaryContactId { get; set; }

        [JsonPropertyName("azureactivedirectoryobjectid")]
        public Guid? Oid { get; set; }

        [JsonPropertyName("isdisabled")]
        public bool? IsDisabled { get; set; }

        [JsonPropertyName("accessmode")]
        public int? AccessMode { get; set; }

        [JsonPropertyName("applicationid")]
        public Guid? ApplicationId { get; set; }

        [JsonPropertyName("sprk_isexternal")]
        public bool? IsExternal { get; set; }
    }
}
