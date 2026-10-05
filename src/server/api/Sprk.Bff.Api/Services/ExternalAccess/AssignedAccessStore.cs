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

    /// <summary>On the record's No Access list (FR-23 / owner Q4) — a matching entry. Since task 142 r4 a list that could
    /// not be checked is <see cref="NoAccessUnverifiable"/>, never this.</summary>
    public const string NoAccess = "no-access";

    /// <summary>
    /// A No Access check could not be completed: task 143's secure-record wall guard answered Unverifiable, or a
    /// contact/organization deny-list check answered Unverifiable or threw (task 142 r3/r4). Every one is also reported as
    /// a <c>deny-list-unreadable</c> failure, so the run fails (owner round 13 items 4 and 5). Never a grant, never
    /// "no-access" (an entry).
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
    /// Task 158 r1 (owner round 30): an inherited row whose principal already held the parent's mirror on the filed record
    /// when it was passed on — direct access the parent's unshare never removes.
    /// </summary>
    public const string CoveredByExistingShare = "covered-by-existing";

    /// <summary>Task 158 r1: the parent's share ended; the principal's access on the filed record was direct, so it is kept.</summary>
    public const string KeptDirectShare = "kept-direct";

    /// <summary>Task 158 r1: the parent's share ended; another secure parent of the filed record still passes it on, so it is kept.</summary>
    public const string KeptOtherSource = "kept-other-source";

    /// <summary>
    /// Task 158 r1: the parent's share ended; removing it would leave nobody able to open the record (S5), so it is kept. Since
    /// r1c-v1 the row stays <see cref="AssignedAccessState.Shared"/> with this reason (the share is still the one the rule
    /// passed on): every later pass tries again, and removes it once someone else can open the record.
    /// </summary>
    public const string KeptLastReader = "kept-last-reader";

    /// <summary>
    /// Task 158 r1c-v1 (verifier item 1 — write-ahead provenance): the reason of an inherited-share row recorded BEFORE its
    /// share is written (<see cref="AssignedAccessState.Shared"/>, <c>sprk_grantedlevel</c> = the mask about to be written,
    /// optionally followed by <c>;raised-from-mask:N</c>). Confirmed — this marker dropped — once the share reads back. A
    /// later pass that finds it unconfirmed decides from the live share: the mask in place → confirmed; the mask from before
    /// → the write never landed (written again, never read as a removal); anything else → changed by someone else.
    /// </summary>
    public const string SharePending = "share-pending";

    /// <summary>
    /// Task 158 r1c-v2 (main-session round 47 item 1 (2), E-158-v1-1): an Assigned-To row that was
    /// <see cref="AssignedAccessState.CoveredByExisting"/> is <see cref="AssignedAccessState.Skipped"/> with this reason when
    /// the secure-root inheritance removes the share that covered it (its parent no longer passes it on). A KNOWN cause —
    /// never <see cref="RemovedOutOfBand"/> → Declined — so the materializer decides the subject afresh at once: on a secure
    /// record the assignee is suggested (owner A3). Written ahead of the removal; Skipped is re-evaluated every pass, so a
    /// removal that then fails is recorded covered again.
    /// </summary>
    public const string CoveringShareEnded = "covering-share-ended";

    /// <summary>
    /// Task 158 r1: the filed record itself was UNSECURED — every share on it is revoked by the unsecure, so what its parents
    /// had passed on ends with it (a row left Shared would later read as an operator's removal when it is secured again).
    /// </summary>
    public const string RecordUnsecured = "record-unsecured";

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

    /// <summary>
    /// Task 158 r1 (owner round 30): the TEAM an inherited share was passed on to (a secure parent's team sharee). Read only
    /// by <see cref="AssignedAccessStore.ReadInheritedLedgerAsync"/> / <see cref="AssignedAccessStore.ReadInheritedLedgerByParentAsync"/>
    /// — the Assigned-To reads keep their own select, so they never depend on the column.
    /// </summary>
    [JsonPropertyName("_sprk_subjectteam_value")]
    public Guid? SubjectTeamId { get; set; }

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

    /// <summary>
    /// Batch-4 integration (round 47 item 2): the row's version as it was read. This is <c>@odata.etag</c>, which the Web API
    /// returns on every row whatever the <c>$select</c>. The inheritance pass's updates send it back as <c>If-Match</c>
    /// (<see cref="AssignedAccessStore.UpdateLedgerIfUnchangedAsync"/>). It is <c>null</c> on a row this process built or
    /// has already written, because that row's version is no longer the one that was read.
    /// </summary>
    [JsonPropertyName("@odata.etag")]
    public string? ETag { get; set; }

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

    /// <summary>
    /// Batch-4 integration (round 47 item 2, which closes E-158-v1-2). Updates <paramref name="row"/> ONLY IF the row still
    /// holds what the caller decided on. The write sends <c>If-Match</c> with the version the row was read at
    /// (<see cref="AssignedAccessLedgerRow.ETag"/>). It goes through task 140's
    /// <see cref="DataverseWebApiClient.UpdateIfMatchAsync"/>, the one conditional-write mechanism.
    /// <para>The row is READ AGAIN and decided again on a mismatch (412). The same happens when the row has no version to
    /// send, because this process built it or has already written it. If the row's state, reason, level and subject still
    /// match what <paramref name="row"/> holds in memory, the caller's decision still applies to what is stored. The write is
    /// then sent once more at the fresh version. If they differ (an operator's Declined marker, another pass's end, or a
    /// task 142 write), nothing is written and the method answers <c>false</c>. The caller decided on a row that has
    /// changed, so the next pass decides on the row as it is now. The write is never sent blind.</para>
    /// <para>On success, the write's values are applied to <paramref name="row"/> and its version is cleared, so a later
    /// write in the same pass re-reads the row first. A row that no longer exists, or that is deactivated, answers
    /// <c>false</c>. Any other fault propagates.</para>
    /// </summary>
    internal async Task<bool> UpdateLedgerIfUnchangedAsync(
        AssignedAccessLedgerRow row, AssignedAccessLedgerWrite write, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(write);

        if (!string.IsNullOrWhiteSpace(row.ETag)
            && await TryUpdateLedgerIfMatchAsync(row.Id, write, row.ETag, ct).ConfigureAwait(false))
        {
            ApplyWritten(row, write);
            return true;
        }

        var fresh = await ReadLedgerRowAsync(row.Id, ct).ConfigureAwait(false);
        if (fresh is null || string.IsNullOrWhiteSpace(fresh.ETag) || !SameDecisionFacts(fresh, row))
        {
            _logger.LogInformation(
                "[ASSIGNED-ACCESS] Ledger row {RowId} changed since it was read ({Then} -> {Now}); not written. The next pass " +
                "decides on it as it is.", row.Id, Describe(row), fresh is null ? "gone" : Describe(fresh));
            return false;
        }

        if (!await TryUpdateLedgerIfMatchAsync(row.Id, write, fresh.ETag, ct).ConfigureAwait(false))
        {
            _logger.LogInformation(
                "[ASSIGNED-ACCESS] Ledger row {RowId} changed again while it was being written; not written. The next pass " +
                "decides on it.", row.Id);
            return false;
        }

        ApplyWritten(row, write);
        return true;
    }

    /// <summary>
    /// One conditional PATCH (<see cref="DataverseWebApiClient.UpdateIfMatchAsync"/>). Answers <c>true</c> when it landed and
    /// <c>false</c> when the row changed since <paramref name="etag"/> (412) or no longer exists (404). Any other fault
    /// propagates.
    /// </summary>
    internal virtual async Task<bool> TryUpdateLedgerIfMatchAsync(
        Guid rowId, AssignedAccessLedgerWrite write, string etag, CancellationToken ct)
    {
        try
        {
            await _dataverse.UpdateIfMatchAsync(EntitySet, rowId, BuildUpdatePayload(write), etag, ct).ConfigureAwait(false);
            return true;
        }
        catch (System.Data.DBConcurrencyException)
        {
            return false;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    /// <summary>One live ledger row as it is now (with its version), or <c>null</c> when it is gone or deactivated. Exceptions propagate.</summary>
    internal virtual async Task<AssignedAccessLedgerRow?> ReadLedgerRowAsync(Guid rowId, CancellationToken ct)
    {
        var rows = await _dataverse.QueryAsync<AssignedAccessLedgerRow>(
            EntitySet,
            filter: $"sprk_assignedaccessid eq {rowId:D} and statecode eq 0",
            select: InheritedLedgerSelect,
            top: 1,
            cancellationToken: ct).ConfigureAwait(false);
        return rows.FirstOrDefault(r => r.Id == rowId);
    }

    /// <summary>Whether a row read now still holds what a decision was made on: the values a ledger write sets, and its subject.</summary>
    internal static bool SameDecisionFacts(AssignedAccessLedgerRow now, AssignedAccessLedgerRow decidedOn) =>
        now.StateValue == decidedOn.StateValue
        && string.Equals(now.Reason, decidedOn.Reason, StringComparison.Ordinal)
        && now.GrantedLevel == decidedOn.GrantedLevel
        && now.GrantedExpiry == decidedOn.GrantedExpiry
        && now.GrantId == decidedOn.GrantId
        && now.SystemUserId == decidedOn.SystemUserId
        && now.SubjectTeamId == decidedOn.SubjectTeamId
        && string.Equals(now.SourceField?.Trim(), decidedOn.SourceField?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>What a landed write leaves on the row, exactly as <see cref="BuildUpdatePayload"/> sets it. The version that was read is used up.</summary>
    private static void ApplyWritten(AssignedAccessLedgerRow row, AssignedAccessLedgerWrite write)
    {
        row.StateValue = (int)write.State;
        row.Reason = write.Reason is null ? null : Truncate(write.Reason, 100);
        if (write.GrantId is { } grantId && grantId != Guid.Empty) row.GrantId = grantId;
        if (write.SystemUserId is { } userId && userId != Guid.Empty) row.SystemUserId = userId;
        if (write.GrantedLevel is { } level) row.GrantedLevel = level;
        if (write.GrantedExpiry is { } expiry) row.GrantedExpiry = expiry;
        row.ETag = null;
    }

    private static string Describe(AssignedAccessLedgerRow row) =>
        string.Create(CultureInfo.InvariantCulture, $"{row.State}/{row.Reason}/{row.GrantedLevel}");

    // ── Inherited shares (task 158 r1, owner round 30 — the provenance of a share passed on to a filed secure root) ─────

    /// <summary>The source-field prefix of an inherited-share row: <c>inherited:{parentTable}:{parentId}</c>.</summary>
    internal const string InheritedSourcePrefix = "inherited:";

    private const string InheritedLedgerSelect = LedgerSelect + ",_sprk_subjectteam_value";

    /// <summary>The source field naming the secure parent a share was passed on from.</summary>
    public static string InheritedSourceField(string parentTable, Guid parentId)
        => string.Create(CultureInfo.InvariantCulture,
            $"{InheritedSourcePrefix}{parentTable.Trim().ToLowerInvariant()}:{parentId:D}");

    /// <summary>The parent an inherited-share row's source field names, or <c>null</c> for any other row.</summary>
    public static (string Table, Guid Id)? InheritedSourceOf(string? sourceField)
    {
        if (string.IsNullOrWhiteSpace(sourceField)
            || !sourceField.Trim().StartsWith(InheritedSourcePrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var parts = sourceField.Trim()[InheritedSourcePrefix.Length..].Split(':');
        return parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && Guid.TryParse(parts[1], out var id) && id != Guid.Empty
            ? (parts[0].ToLowerInvariant(), id)
            : null;
    }

    /// <summary>
    /// The ledger key of an inherited-share row: <c>{root}:{rootId}:inherited:{parentTable}:{parentId}:{systemuser|team}:{id}</c>
    /// — the same alternate key, so one row per (filed root, parent, principal).
    /// </summary>
    public static string InheritedLedgerKey(
        ExternalGrantRootType rootType, Guid rootId, string parentTable, Guid parentId, DataversePrincipalRef principal)
        => string.Create(CultureInfo.InvariantCulture,
            $"{ExternalGrantRoot.LogicalNameFor(rootType)}:{rootId:D}:{InheritedSourceField(parentTable, parentId)}:" +
            $"{(principal.Kind == DataversePrincipalKind.Team ? "team" : "systemuser")}:{principal.Id:D}");

    /// <summary>The principal an inherited-share row is about (a user or a team), or <c>null</c> for any other row.</summary>
    public static DataversePrincipalRef? InheritedPrincipalOf(AssignedAccessLedgerRow row)
        => InheritedSourceOf(row.SourceField) is null ? null
            : row.SystemUserId is { } user && user != Guid.Empty ? DataversePrincipalRef.User(user)
            : row.SubjectTeamId is { } team && team != Guid.Empty ? DataversePrincipalRef.Team(team)
            : null;

    /// <summary>The create payload of a new inherited-share row (the principal bound as a user or a team).</summary>
    internal static Dictionary<string, object?> BuildInheritedCreatePayload(
        ExternalGrantRootType rootType, Guid rootId, string parentTable, Guid parentId, DataversePrincipalRef principal,
        AssignedAccessLedgerWrite write)
    {
        var (_, rootNav) = RootLookupFor(rootType);
        var rootSet = ExternalGrantRoot.BindFor(rootType).EntitySet;
        var source = InheritedSourceField(parentTable, parentId);
        var payload = new Dictionary<string, object?>
        {
            ["sprk_name"] = Truncate($"{source} → {(principal.Kind == DataversePrincipalKind.Team ? "team" : "systemuser")}:{principal.Id:D}", 200),
            ["sprk_ledgerkey"] = InheritedLedgerKey(rootType, rootId, parentTable, parentId, principal),
            ["sprk_sourcefield"] = source,
            [$"{rootNav}@odata.bind"] = $"/{rootSet}({rootId:D})",
        };

        foreach (var (k, v) in BuildUpdatePayload(write with { SystemUserId = null }))
            payload[k] = v;

        if (principal.Kind == DataversePrincipalKind.Team)
            payload["sprk_SubjectTeam@odata.bind"] = $"/teams({principal.Id:D})";
        else
            payload["sprk_SubjectSystemUser@odata.bind"] = $"/systemusers({principal.Id:D})";

        return payload;
    }

    /// <summary>
    /// Every live inherited-share row held on one filed root (any parent, any state). Exceptions propagate: an unread
    /// provenance is never "nothing was inherited".
    /// </summary>
    internal virtual async Task<IReadOnlyList<AssignedAccessLedgerRow>> ReadInheritedLedgerAsync(
        ExternalGrantRootType rootType, Guid rootId, CancellationToken ct)
    {
        var (valueColumn, _) = RootLookupFor(rootType);
        var rows = await _dataverse.QueryAsync<AssignedAccessLedgerRow>(
            EntitySet,
            filter: $"{valueColumn} eq {rootId:D} and statecode eq 0 and startswith(sprk_sourcefield,'{InheritedSourcePrefix}')",
            select: InheritedLedgerSelect,
            cancellationToken: ct).ConfigureAwait(false);
        return rows.Where(r => r.Id != Guid.Empty && InheritedSourceOf(r.SourceField) is not null).ToList();
    }

    /// <summary>
    /// Every inherited-share row still IN FORCE passed on FROM one secure parent, on any filed root — the reverse fan-out of
    /// the parent's unshare, and its unsecure's Step 4.5. At most <see cref="MaxScanRows"/>; one more reports <c>Truncated</c>
    /// (never a silent prefix — the caller fails closed). Exceptions propagate.
    /// </summary>
    /// <remarks>Task 158 final round (main-session round 58 item 2, Step 4.5's by-parent ledger read): a
    /// <see cref="AssignedAccessState.Revoked"/> row — one the reverse rule already ended — is left out IN THE QUERY, so a
    /// parent's provenance history never counts toward the bound: a parent that once passed on more than the bound, all since
    /// ended, would otherwise read as truncated on every call and its unsecure could never complete. An EMPTY state reads as
    /// Skipped (re-evaluated, never trusted), so it stays in force. <c>AssignedAccessStoreODataTests</c> drives this filter over
    /// an in-memory Web API that evaluates it.</remarks>
    internal virtual async Task<(IReadOnlyList<AssignedAccessLedgerRow> Rows, bool Truncated)> ReadInheritedLedgerByParentAsync(
        string parentTable, Guid parentId, CancellationToken ct)
    {
        var rows = await _dataverse.QueryAsync<AssignedAccessLedgerRow>(
            EntitySet,
            filter: $"statecode eq 0 and sprk_sourcefield eq '{InheritedSourceField(parentTable, parentId)}' and " +
                    $"(sprk_state eq null or sprk_state ne {(int)AssignedAccessState.Revoked})",
            select: InheritedLedgerSelect,
            top: MaxScanRows + 1,
            cancellationToken: ct).ConfigureAwait(false);
        var live = rows.Where(r => r.Id != Guid.Empty).ToList();
        return live.Count > MaxScanRows ? (live.Take(MaxScanRows).ToList(), true) : (live, false);
    }

    /// <summary>
    /// Creates an inherited-share row and answers its id — or <c>null</c> when a concurrent pass created the row for the same
    /// (filed root, parent, principal) first (the alternate key answered 409/412 and the row reads back). Task 158 r1c-v1
    /// (verifier item 1): the loser NEVER writes over that row — not a confirmed or pending share with a "covered by existing"
    /// one, not an operator's Declined / Adopted with a share — because its decision was made on a read that did not see it.
    /// The caller treats <c>null</c> as "not recorded" (the pass is incomplete and the next one decides on the row as it is).
    /// Any other fault, or a conflict whose row cannot be read back, propagates.
    /// </summary>
    internal virtual async Task<Guid?> CreateInheritedLedgerAsync(
        ExternalGrantRootType rootType, Guid rootId, string parentTable, Guid parentId, DataversePrincipalRef principal,
        AssignedAccessLedgerWrite write, CancellationToken ct)
    {
        try
        {
            return await _dataverse.CreateAsync(
                EntitySet, BuildInheritedCreatePayload(rootType, rootId, parentTable, parentId, principal, write), ct)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.PreconditionFailed
                                                 or System.Net.HttpStatusCode.Conflict)
        {
            var key = InheritedLedgerKey(rootType, rootId, parentTable, parentId, principal);
            var existing = await _dataverse.QueryAsync<AssignedAccessLedgerRow>(
                EntitySet, filter: $"sprk_ledgerkey eq '{key}'", select: InheritedLedgerSelect, top: 1, cancellationToken: ct)
                .ConfigureAwait(false);
            if (existing.FirstOrDefault() is not { } row)
                throw;

            _logger.LogInformation(
                "[ASSIGNED-ACCESS] Inherited-share row {Key} was created concurrently ({RowId}); left as it is — the next pass " +
                "decides on it.", key, row.Id);
            return null;
        }
    }

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
    /// <remarks>
    /// Task 158 r1: the inherited-share provenance rows (<see cref="InheritedSourcePrefix"/>) share this table but are not
    /// Assigned-To rows — the materializer ignores them (no contact / organization subject), and they are left out of this
    /// scan so they never count toward its <see cref="MaxScanRows"/> bound (an environment with many inherited shares would
    /// otherwise truncate — and fail — the Assigned-To job's run). The OData filter is the ONE statement of that predicate
    /// (task 158 r1c-v1, verifier item 5: an in-memory copy of it was removed — it could never differ, so nothing pinned
    /// it); <c>AssignedAccessStoreODataTests</c> drives this method over an in-memory Web API that evaluates the filter.
    /// </remarks>
    internal virtual async Task<(IReadOnlyList<AssignedRootRef> Roots, bool Truncated)> ScanLedgerRootsAsync(CancellationToken ct)
    {
        var rows = await _dataverse.QueryAsync<AssignedAccessLedgerRow>(
            EntitySet,
            filter: $"statecode eq 0 and sprk_state ne {(int)AssignedAccessState.Revoked} and " +
                    $"(sprk_sourcefield eq null or not startswith(sprk_sourcefield,'{InheritedSourcePrefix}'))",
            select: "sprk_assignedaccessid,sprk_sourcefield,_sprk_project_value,_sprk_matter_value,_sprk_workassignment_value",
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
