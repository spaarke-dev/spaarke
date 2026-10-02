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

    /// <summary>
    /// The row's current expiry, or <c>null</c> for an unbounded grant.
    /// </summary>
    /// <remarks>
    /// <para><b>Added by task 023 (finding H1).</b> The upsert's match path could not read this column —
    /// it was not in <c>RowSelect</c> — so it could neither write a new expiry nor notice that the row it
    /// was "re-granting" had already expired. Both are silent wrong answers: the caller gets a 200 and a
    /// record id either way.</para>
    ///
    /// <para><c>sprk_expiresdate</c> is <b>Date Only</b> in live metadata, which is why this is
    /// <see cref="DateOnly"/> and not <see cref="DateTime"/> — and why the expiry read filter compares
    /// with bare <c>yyyy-MM-dd</c> (task 007's <c>ExpiryPredicate</c>).</para>
    /// </remarks>
    [JsonPropertyName("sprk_expiresdate")]
    public DateOnly? ExpiresDate { get; set; }

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

    /// <summary>The table's logical name — what an SDK write (<c>IGenericEntityService</c>) addresses, as opposed to the Web API <see cref="EntitySet"/>.</summary>
    internal const string EntityLogicalName = "sprk_externalrecordaccess";

    /// <summary>
    /// Days a grant lasts when the request names no expiry (spec FR-33; owner decision 2026-09-10,
    /// "Server fills +90").
    /// </summary>
    /// <remarks>
    /// A constant, not a setting — the owner removed the tenant cap. The Manage Access Expiration picker
    /// (task 099, not yet built) is specified to default to the same 90 days, so a grant from a surface
    /// with no date field (e.g. the TrackingFieldTrio PCF) matches what the picker would produce.
    /// </remarks>
    internal const int DefaultExpiryDays = 90;

    /// <summary>
    /// "Today" for every grant-expiry decision on the write path: the <b>UTC</b> calendar date — the same
    /// calendar the read filter compares against (task 007, <c>ExternalParticipationService.ExpiryPredicate</c>),
    /// so a grant the writer accepts as "expires today" is still live to the reader today.
    /// </summary>
    internal static DateOnly TodayUtc(TimeProvider timeProvider)
        => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    /// <summary>The expiry an absent request value becomes: <paramref name="today"/> + <see cref="DefaultExpiryDays"/>.</summary>
    internal static DateOnly DefaultExpiry(DateOnly today) => today.AddDays(DefaultExpiryDays);

    /// <summary>
    /// The expiry one row would carry after a request naming <paramref name="requestedExpiry"/> — the
    /// value BOTH the survivor election and the "confers no access" decision judge.
    /// </summary>
    /// <remarks>
    /// <para>ONE definition, two uses (task 106). The precedence mirrors the upsert's write rule exactly:
    /// an explicitly requested date wins; otherwise a date someone already set is KEPT (a re-grant from a
    /// surface with no date field must never move it — task 097); otherwise an unbounded row is bounded at
    /// the FR-33 default.</para>
    ///
    /// <para><b>This answers a per-request question and MUST NOT be used to rank rows</b> — see
    /// <see cref="ConferralRank"/> for why. Use it for the one elected row, to decide what that row will
    /// carry and whether it confers access.</para>
    /// </remarks>
    internal static DateOnly EffectiveExpiry(DateOnly? explicitExpiry, DateOnly? rowExpiry, DateOnly today)
        => explicitExpiry ?? rowExpiry ?? DefaultExpiry(today);

    /// <summary>
    /// A row's rank in the survivor election: the date it would confer access until if this request named
    /// none. <b>Deliberately independent of the request.</b>
    /// </summary>
    /// <remarks>
    /// <para><b>Why the request is excluded, and this is load-bearing</b> (task 106 review finding V1/F1,
    /// caught by BOTH Step 9.5 gates). An election parameterised by a per-request value is not a total
    /// order two callers share, and a shared order is the entire point — see
    /// <see cref="QueryActiveRowsAsync"/>. With rows A (lower id, expiring in 10 days) and B (200 days):
    /// a request naming NO date ranks B first, while a request naming 30 days makes both ranks equal so
    /// the id tie-break elects A. Two such requests reading the same rows then collapse onto DIFFERENT
    /// survivors and deactivate each other, leaving <b>ZERO active rows while both callers receive
    /// 200</b>. The grantee loses all access on the key, there is no conditional update to stop it, and it
    /// does not self-heal. That is strictly worse than the false 409 task 106 set out to fix: the original
    /// defect was inert, this one destroys access.</para>
    ///
    /// <para><b>Why the default and not <see cref="DateOnly.MaxValue"/>.</b> An unbounded row ranks at
    /// today + <see cref="DefaultExpiryDays"/>, NOT at infinity. Ranking <c>null</c> highest would elect
    /// the unbounded row, FR-33 would then bound it to that same default, and a sibling dated LATER would
    /// be collapsed away — access leaving the call SHORTER than it arrived.</para>
    ///
    /// <para><b>What it preserves.</b> A row confers access exactly when its rank is on or after
    /// <paramref name="today"/>, so a conferring row always outranks a non-conferring one. The elected row
    /// therefore confers whenever any row does — which is what lets the caller judge the whole key from
    /// the survivor alone, and what keeps the duplicate collapse from deactivating the row access rests
    /// on.</para>
    /// </remarks>
    internal static DateOnly ConferralRank(DateOnly? rowExpiry, DateOnly today)
        => rowExpiry ?? DefaultExpiry(today);

    /// <summary>
    /// Elects the row a grant upsert applies to: the one that will confer access LONGEST after this
    /// request, ties broken by ascending id. <paramref name="rows"/> must be non-empty.
    /// </summary>
    /// <remarks>
    /// <para><b>Task 106 (ISS-008 / #973).</b> The election was previously "lowest id", which produced two
    /// distinct wrong answers on a key carrying duplicates. (1) An expired lowest-id row returned 409
    /// <c>sdap.grant.expired_not_restored</c> — "still confers no access" — while a live duplicate meant the
    /// grantee DID have access. (2) <c>CollapseDuplicatesAsync</c> then deactivates every row but the
    /// survivor, so the naive fix — suppress the 409 and fall through — would have REVOKED the live access
    /// it had just correctly detected. The bug's current form is at least inert; that fix would not be.</para>
    ///
    /// <para>Electing by effective expiry makes both properties structural rather than checked: the elected
    /// row is a conferring row whenever ANY row confers, so an expired survivor PROVES every row on the key
    /// is expired — judging the survivor becomes judging the whole key — and the collapse cannot deactivate
    /// the row that confers access, because that row IS the survivor.</para>
    ///
    /// <para><b>Determinism</b> — the property <see cref="QueryActiveRowsAsync"/>'s ordering exists for.
    /// The rank comes from <see cref="ConferralRank"/>, which depends only on the ROW and
    /// <paramref name="today"/>, so every caller computes the same total order over the same rows and two
    /// racers cannot elect different survivors. ⚠️ It deliberately takes NO request value: an earlier
    /// version ranked by <see cref="EffectiveExpiry"/> and was therefore request-dependent, which let two
    /// concurrent grants deactivate each other and leave zero active rows (V1/F1). Request-independence is
    /// structural here — there is no parameter through which to reintroduce it. Do not add one.</para>
    /// </remarks>
    internal static ExternalGrantRow ElectSurvivor(IReadOnlyList<ExternalGrantRow> rows, DateOnly today)
    {
        // The precondition was prose-only until the review (F11/W7). Both call sites guard, so this is
        // unreachable today; a third would inherit an opaque InvalidOperationException instead of a clear
        // contract violation.
        ArgumentOutOfRangeException.ThrowIfZero(rows.Count);

        return rows
            .OrderByDescending(r => ConferralRank(r.ExpiresDate, today))
            .ThenBy(r => r.Id)
            .First();
    }

    // sprk_expiresdate added by task 023 (H1): without it the upsert's match path cannot see the row's
    // current expiry, so it could neither write a new one nor detect that it was "re-granting" a row
    // that had already expired. Verified DATE ONLY in live metadata (task 007).
    private const string RowSelect =
        "sprk_externalrecordaccessid,sprk_accesslevel,statecode,sprk_expiresdate," +
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
    /// <para><b>Since task 106 this ordering is the TIE-BREAK, not the election itself</b> —
    /// <see cref="ElectSurvivor"/> ranks by effective expiry first and falls back to ascending id. It is
    /// still exactly what makes the outcome deterministic, because two racers applying the same total order
    /// reach the same row; it is no longer, on its own, what decides which row survives.</para>
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
    /// The OData <c>$filter</c> selecting every ACTIVE row held at one root record — contact grants AND
    /// organization grants alike, since the grantee is deliberately not constrained (task 098).
    /// </summary>
    /// <remarks>
    /// The root half is the same value column <see cref="ExternalGrantKey.ToActiveRowsFilter"/> uses; the two
    /// must never disagree about which rows belong to a record.
    /// </remarks>
    internal static string ActiveRowsForRootFilter(ExternalGrantRootType rootType, Guid rootId)
        => $"{ExternalGrantRoot.ValueColumnFor(rootType)} eq {rootId} and statecode eq 0";

    /// <summary>
    /// Every ACTIVE row held at one root record, in the same shape as <see cref="QueryActiveRowsAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>WRITE-PATH ONLY.</b> It carries no expiry predicate — by design, so that lapsed shares are
    /// selected and can be renewed (task 098, owner decision 2026-09-11). A READ that decides who has access
    /// must use <c>ExternalParticipationService</c>'s filters, which apply <c>ExpiryPredicate</c>; reusing this
    /// one there would silently stop enforcing expiry.</para>
    /// <para>Unlike <see cref="QueryActiveRowsAsync"/>, rows without a usable id are NOT discarded here: a caller
    /// updating every row of a record must refuse rather than silently skip one (task 098). Exceptions
    /// propagate. <paramref name="top"/> bounds the single page <c>QueryAsync</c> reads, so an over-bound
    /// record is detectable instead of silently truncated.</para>
    /// </remarks>
    internal static Task<List<ExternalGrantRow>> QueryActiveRowsForRootAsync(
        DataverseWebApiClient dataverseClient, ExternalGrantRootType rootType, Guid rootId, int top, CancellationToken ct)
        => dataverseClient.QueryAsync<ExternalGrantRow>(
            EntitySet,
            filter: ActiveRowsForRootFilter(rootType, rootId),
            select: RowSelect,
            top: top,
            cancellationToken: ct);

    /// <summary>
    /// Shapes a <see cref="DateOnly"/> for an SDK write to <c>sprk_expiresdate</c> / <c>sprk_granteddate</c>:
    /// midnight, <see cref="DateTimeKind.Unspecified"/>.
    /// </summary>
    /// <remarks>
    /// <para>Both columns are <b>Format = DateOnly, Behavior = TimeZoneIndependent</b> (read from live metadata
    /// 2026-09-11, task 098): Dataverse stores the value it is given with no time-zone conversion. An
    /// UNSPECIFIED kind carries no offset for anything between here and Dataverse to act on — whereas a
    /// <c>Local</c> value would be converted to UTC on a machine west or east of UTC, and midnight can land on
    /// the neighbouring date. The Web API path writes the same column as a bare <c>yyyy-MM-dd</c> string
    /// (<c>GrantExternalAccessEndpoint.FormatDateOnly</c>); this is the SDK equivalent.</para>
    /// </remarks>
    internal static DateTime ToSdkDateOnly(DateOnly value) => value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

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

    // ── Write-time grant policy (task 138 · owner round 2 item 3) ────────────────────────────────

    /// <summary>Stable reason code: a contact or organization grantee (or an invitation) on a Restricted record.</summary>
    internal const string RecordRestrictedReasonCode = "sdap.access.grant.record_restricted";

    /// <summary>Stable reason code: an organization-wide grant on a Secure or Limited (direct-only) record.</summary>
    internal const string OrgGrantDirectOnlyReasonCode = "sdap.access.grant.org_grant_direct_only_record";

    /// <summary>Stable reason code: the record's access flags could not be read, so nothing was granted.</summary>
    internal const string PolicyUnreadableReasonCode = "sdap.access.grant.policy_unreadable";

    /// <summary>
    /// The ONE write-time grant-policy decision (task 138), shared by <c>/grant</c>, <c>/invite-and-grant</c>,
    /// <c>/invite</c> and the grant core <c>GrantExternalAccessEndpoint.CreateGrantAsync</c> — so every future
    /// writer that goes through the core (task 140's contact-side route, task 142's Assigned-To auto-grants)
    /// inherits it and cannot bypass it.
    /// </summary>
    /// <param name="flags">The batched flag read for the root (<c>ExternalParticipationService.GetRootRecordFlagsAsync</c>).</param>
    /// <param name="rootId">The root the grant targets.</param>
    /// <param name="granteeKind">Who would receive access.</param>
    /// <remarks>
    /// <para><b>The owner's model (round 2, 2026-09-30, binding).</b> Restricted = no contact-based access at
    /// all, internal users unaffected. Secure and Limited = contacts get access only through named, direct
    /// grants (Secure implies Limited). Standard = every grant type. Where Restricted and Secure/Limited are
    /// both present, Restricted wins.</para>
    /// <para><b>Order, and why it is load-bearing.</b> (1) Unreadable FIRST, so a fault is never reported as
    /// Restricted: <see cref="RootRecordFlags.Unreadable"/> also carries every restriction, and testing
    /// <c>IsRestricted</c> first would tell the operator a falsehood. (2) Restricted, for both grantee kinds.
    /// (3) Organization-wide grants on a direct-only record.</para>
    /// <para><b>⚠️ An ABSENT key is UNREADABLE here</b>, the opposite of the read path's "no veto". The flag
    /// read returns an empty map for any entity type that is not a flag-bearing root, so a wrong logical name
    /// would otherwise silently allow everything. This function never uses the read path's
    /// <c>TryGetValue(...) &amp;&amp; f.Is…</c> shape.</para>
    /// <para>Not a replacement for the read-time evaluator: rows written directly in Dataverse bypass this
    /// check, and <c>AccessibleRecordSetService</c> stays authoritative (D-1 / ADR-002 WP-5).</para>
    /// </remarks>
    internal static GrantPolicyDecision DecideGrantPolicy(
        IReadOnlyDictionary<Guid, RootRecordFlags> flags, Guid rootId, GrantGranteeKind granteeKind)
    {
        if (!flags.TryGetValue(rootId, out var f) || f.IsUnreadable)
            return GrantPolicyDecision.Unreadable;

        if (f.IsRestricted)
            return GrantPolicyDecision.Restricted;

        if (granteeKind == GrantGranteeKind.Organization && f.IsDirectOnly)
            return GrantPolicyDecision.OrgGrantOnDirectOnly(f.IsSecure);

        return GrantPolicyDecision.Allowed;
    }

    /// <summary>
    /// Reads the root's flags through the ONE existing flag reader and applies <see cref="DecideGrantPolicy"/>.
    /// Never throws: a fault of any kind is <see cref="GrantPolicyDecision.Unreadable"/> (fail closed, and a
    /// readable 503 rather than an unhandled 500).
    /// </summary>
    internal static async Task<GrantPolicyDecision> EvaluateGrantPolicyAsync(
        ExternalParticipationService participations,
        ExternalGrantRootType rootType,
        Guid rootId,
        GrantGranteeKind granteeKind,
        ILogger logger,
        CancellationToken ct)
    {
        IReadOnlyDictionary<Guid, RootRecordFlags> flags;
        try
        {
            // LOGICAL name (sprk_project), the key shape GetRootRecordFlagsAsync's sources use — an entity-set
            // name here would return an empty map, which DecideGrantPolicy treats as unreadable (never as "open").
            flags = await participations
                .GetRootRecordFlagsAsync(ExternalGrantRoot.LogicalNameFor(rootType), new[] { rootId }, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "[EXT-GRANT-POLICY] Flag read threw for {RootType} {RootId}. Refusing (policy unreadable); " +
                "nothing will be granted.", rootType, rootId);
            return GrantPolicyDecision.Unreadable;
        }

        var decision = DecideGrantPolicy(flags, rootId, granteeKind);
        if (!decision.IsAllowed)
        {
            logger.LogWarning(
                "[EXT-GRANT-POLICY] Refused a {GranteeKind} grant on {RootType} {RootId}: {ReasonCode}.",
                granteeKind, rootType, rootId, decision.ReasonCode);
        }

        return decision;
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

/// <summary>Who a grant would give access to — the input the write-time policy needs (task 138).</summary>
internal enum GrantGranteeKind
{
    /// <summary>One named contact (a <c>/grant</c> with a ContactId, <c>/invite-and-grant</c>, <c>/invite</c>).
    /// A row that also names an organization is still a contact grant — the organization is the firm.</summary>
    Contact,

    /// <summary>An organization-wide grant: no ContactId, every active member inherits at check time.</summary>
    Organization,
}

/// <summary>
/// The outcome of <see cref="ExternalGrantLifecycle.DecideGrantPolicy"/> — a typed value, never an exception,
/// so a refusal can never become the grant routes' catch-all 500 (task 138).
/// </summary>
/// <param name="IsAllowed">The grant may be written.</param>
/// <param name="ReasonCode">The binding reason code for a refusal; <c>null</c> when allowed.</param>
/// <param name="StatusCode">422 for a policy refusal, 503 for an unreadable policy; 200 when allowed.</param>
/// <param name="Detail">A human-readable sentence for the operator; <c>null</c> when allowed.</param>
internal sealed record GrantPolicyDecision(bool IsAllowed, string? ReasonCode, int StatusCode, string? Detail)
{
    /// <summary>The grant may be written.</summary>
    public static GrantPolicyDecision Allowed { get; } = new(true, null, StatusCodes.Status200OK, null);

    /// <summary>The record is Restricted: no contact or organization may be granted (or invited).</summary>
    public static GrantPolicyDecision Restricted { get; } = new(
        false,
        ExternalGrantLifecycle.RecordRestrictedReasonCode,
        StatusCodes.Status422UnprocessableEntity,
        "This record's Access Permission is Restricted, so only internal users can be given access. Contacts and " +
        "organizations cannot be granted access to it or invited to it. Nothing was granted. To share it with a " +
        "colleague, use + User; to give a contact access, change Access Permission first.");

    /// <summary>The flags could not be read: refuse truthfully, without claiming the record is Restricted.</summary>
    public static GrantPolicyDecision Unreadable { get; } = new(
        false,
        ExternalGrantLifecycle.PolicyUnreadableReasonCode,
        StatusCodes.Status503ServiceUnavailable,
        "The record's access settings could not be read; nothing was granted. Try again in a moment.");

    /// <summary>An organization-wide grant on a Secure or Limited record.</summary>
    /// <param name="isSecure">Names the flag that made the record direct-only, so the message is exact.</param>
    public static GrantPolicyDecision OrgGrantOnDirectOnly(bool isSecure) => new(
        false,
        ExternalGrantLifecycle.OrgGrantDirectOnlyReasonCode,
        StatusCodes.Status422UnprocessableEntity,
        (isSecure ? "This record is Secure" : "This record's Access Permission is Limited") +
        ", so contacts get access only through grants made to them by name. A whole organization cannot be " +
        "granted access. Nothing was granted. Grant the people who need access individually with + Contact.");
}
