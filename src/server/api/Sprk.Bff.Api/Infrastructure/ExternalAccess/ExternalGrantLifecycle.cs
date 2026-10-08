using System.Globalization;
using System.Text.Json;
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
    /// <para><b>⚠️ Its WIRE shape is not <c>yyyy-MM-dd</c></b> (task 140, read live 2026-10-05). The column's
    /// <i>format</i> is DateOnly but its <i>behaviour</i> is <b>TimeZoneIndependent</b> (task 098 §2.3), and the Web API
    /// returns a TimeZoneIndependent value as a full timestamp — <c>"2026-12-10T00:00:00Z"</c>. System.Text.Json's own
    /// <see cref="DateOnly"/> converter accepts only <c>yyyy-MM-dd</c> and THROWS on that, so every read of a row carrying
    /// an expiry (which, since task 097, is every row the BFF writes) failed with a <see cref="JsonException"/>: the grant
    /// core's re-grant and every contact-side grant (its expiry-cap read), <c>/revoke</c>, the contact revoke and list,
    /// and <c>set-record-share-expiry</c>. <see cref="DataverseDateOnlyJsonConverter"/> reads both shapes.</para>
    /// </remarks>
    [JsonPropertyName("sprk_expiresdate")]
    [JsonConverter(typeof(DataverseDateOnlyJsonConverter))]
    public DateOnly? ExpiresDate { get; set; }

    /// <summary>
    /// The row's version as it was read — <c>@odata.etag</c>, <c>W/"&lt;versionnumber&gt;"</c>, which the Web API returns on
    /// every row of a read without being asked (live-verified 2026-10-05). A write that must not land over a change made
    /// since the read sends it as <c>If-Match</c> (<see cref="DataverseWebApiClient.UpdateIfMatchAsync"/>): the contact-side
    /// grant's PATCH and the contact revoke's deactivation (task 140, session 27 round 42 item 1).
    /// </summary>
    [JsonPropertyName("@odata.etag")]
    public string? ETag { get; set; }

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

    /// <summary>
    /// The SYSTEMUSER who issued the grant (<c>sprk_grantedby</c>), when the issuer was an internal user.
    /// </summary>
    [JsonPropertyName("_sprk_grantedby_value")]
    public Guid? GrantedBySystemUserId { get; set; }

    /// <summary>
    /// The CONTACT who issued the grant (<c>sprk_grantedbycontact</c>, unified-access-control-r2 task 140) — set only
    /// on a grant a contact made through the external SPA. It is the issuer the contact-side routes scope to: a contact
    /// may change or revoke only a row whose value is themselves, so a row anyone else issued is never theirs to alter.
    /// </summary>
    [JsonPropertyName("_sprk_grantedbycontact_value")]
    public Guid? GrantedByContactId { get; set; }

    /// <summary>
    /// The issuing contact's id as TEXT (<c>sprk_grantedbycontactid</c>, session 27 round 50 item 2) — the grant's provenance,
    /// which survives the contact's deletion. The lookup above cannot: its relationship's Delete cascade is RemoveLink, so
    /// deleting the contact empties it. Written in the same write as the lookup, every time (set on a contact grant, cleared
    /// on an internal take-over), so "recorded but the lookup is empty" means exactly "the issuer was deleted".
    /// </summary>
    /// <remarks>Read as text, never parsed on the wire: a value that is not a GUID must not fail every grant-row read (the
    /// lesson of the TimeZoneIndependent expiry shape, task 140 note §12). The column is BFF-written (field-secured).</remarks>
    [JsonPropertyName("sprk_grantedbycontactid")]
    public string? GrantedByContactProvenance { get; set; }

    /// <summary>
    /// The row was issued by a CONTACT: its issuer lookup is set, or — once that contact was deleted — its recorded provenance
    /// is. What an internal change takes over (both columns cleared in one write).
    /// </summary>
    [JsonIgnore]
    public bool IsContactIssued => GrantedByContactId is not null || !string.IsNullOrWhiteSpace(GrantedByContactProvenance);

    /// <summary>Dataverse active state for this table.</summary>
    public bool IsActive => StateCode is null or 0;
}

/// <summary>
/// Reads a Dataverse date-only column in BOTH shapes the Web API returns: <c>yyyy-MM-dd</c> (a column whose behaviour is
/// DateOnly) and <c>yyyy-MM-ddT00:00:00Z</c> (a DateOnly-FORMAT column whose behaviour is TimeZoneIndependent —
/// <c>sprk_externalrecordaccess.sprk_expiresdate</c>, live-verified 2026-10-05). Writes <c>yyyy-MM-dd</c>.
/// </summary>
/// <remarks>
/// <para><b>The calendar date is the leading ten characters, as written.</b> A TimeZoneIndependent value is stored and
/// returned with no time-zone conversion — the <c>Z</c> is how the Web API renders it, not an instant to convert — so the
/// stored date is exactly the date part. Converting to a local date would move a midnight value to the previous day
/// anywhere west of UTC. The SDK path reads the same column as a <see cref="DateTime"/> and takes its date the same
/// way (<c>ExternalAccessReconciliationJob</c>, <c>GrantExpiryReminderJob</c>).</para>
/// <para>Anything else — a number, a malformed string — is a <see cref="JsonException"/>, never a guessed date.</para>
/// <para>§11: <i>Existing</i> — System.Text.Json's own <see cref="DateOnly"/> converter, which reads only
/// <c>yyyy-MM-dd</c> and is the defect. <i>Extension</i> — it cannot be configured to accept the timestamp shape, and
/// changing the property to <see cref="DateTime"/> would change every consumer of a value that is a date. <i>Cost of doing
/// nothing</i> — every read of a dated grant row throws (unified-access-control-r2 task 140 note §12).</para>
/// </remarks>
internal sealed class DataverseDateOnlyJsonConverter : JsonConverter<DateOnly?>
{
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override DateOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"A Dataverse date must be a JSON string, not {reader.TokenType}.");

        var text = reader.GetString() ?? string.Empty;
        if (text.Length >= 10
            && (text.Length == 10 || text[10] == 'T')
            && DateOnly.TryParseExact(text.AsSpan(0, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        throw new JsonException($"'{text}' is not a Dataverse date (yyyy-MM-dd, or yyyy-MM-ddT… from a TimeZoneIndependent column).");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DateOnly? value, JsonSerializerOptions options)
    {
        if (value is { } date)
            writer.WriteStringValue(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        else
            writer.WriteNullValue();
    }
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
    /// <para>Owner round 80: the upsert passes the date it will WRITE as the explicit one (the requested date, or a
    /// lapsed re-add's restored default), so a restored row is judged on the date it will carry.</para>
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
    //
    // The two ISSUER columns (task 140): sprk_grantedby (systemuser, long-standing) and sprk_grantedbycontact (contact,
    // added by scripts/Deploy-ExternalRecordAccessContactGrantor.ps1). The contact-side routes decide "is this row the
    // caller's?" from the second, and /grant re-stamps a row a contact issued when a systemuser changes it, so both read
    // paths need it. Beside them, sprk_grantedbycontactid (session 27 round 50 item 2): the contact issuer's id as text, which
    // outlives the contact — so an internal change takes over a row whose issuing contact was deleted too. ⚠️ DEPLOY ORDER:
    // both columns must exist in the environment BEFORE a BFF carrying this select is deployed — Dataverse answers 400 to a
    // $select naming an unknown attribute (task 140 notes §live gate).
    private const string RowSelect =
        "sprk_externalrecordaccessid,sprk_accesslevel,statecode,sprk_expiresdate," +
        "_sprk_contact_value,_sprk_organization_value," +
        "_sprk_project_value,_sprk_matter_value,_sprk_workassignment_value," +
        "_sprk_grantedby_value,_sprk_grantedbycontact_value," + GrantedByContactIdAttribute;

    /// <summary>
    /// The <c>@odata.bind</c> navigation property of the contact-typed issuer lookup <c>sprk_grantedbycontact</c>
    /// (task 140). The schema script creates the lookup with SchemaName <c>sprk_GrantedByContact</c>, so — like every
    /// other lookup on this table (task 070) — its navigation property is the PascalCase schema name.
    /// </summary>
    internal const string GrantedByContactNavigationProperty = "sprk_GrantedByContact";

    /// <summary>The <c>@odata.bind</c> navigation property of the systemuser issuer lookup <c>sprk_grantedby</c>.</summary>
    internal const string GrantedByNavigationProperty = "sprk_GrantedBy";

    /// <summary>The LOGICAL name of the contact-typed issuer lookup — what an SDK write (<c>IGenericEntityService</c>) addresses.</summary>
    internal const string GrantedByContactAttribute = "sprk_grantedbycontact";

    /// <summary>
    /// The contact issuer's id as TEXT — <c>sprk_grantedbycontactid</c> (session 27 round 50 item 2), a plain column, so its
    /// logical name is also its Web API property and its <c>$select</c> name. Set and cleared in the SAME write as
    /// <see cref="GrantedByContactAttribute"/>, every time; only a deletion of the contact (RemoveLink) empties the lookup
    /// alone, which is how the reconciliation job knows the issuer was deleted. Created by
    /// <c>scripts/Deploy-ExternalRecordAccessContactGrantor.ps1</c>.
    /// </summary>
    internal const string GrantedByContactIdAttribute = "sprk_grantedbycontactid";

    /// <summary>The LOGICAL name of the systemuser issuer lookup — what an SDK write addresses.</summary>
    internal const string GrantedByAttribute = "sprk_grantedby";

    /// <summary>The ONE text form of a contact issuer's id in <see cref="GrantedByContactIdAttribute"/>: lower-case, hyphenated (<c>D</c>).</summary>
    internal static string ContactIssuerProvenance(Guid issuerContactId)
        => issuerContactId.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>
    /// The SDK-shaped (logical-name) fields that make an internal user's change of a CONTACT-issued row take it over
    /// (session 27 round 34 item 3): the contact issuer is CLEARED — the lookup AND its recorded provenance, in the same write
    /// (round 50 item 2) — and <c>sprk_grantedby</c> is stamped with the changing systemuser, so the contact can no longer
    /// revoke, or re-lengthen through its own re-grant, a decision an internal user made. The grant core's Web API path does
    /// the same with <c>@odata.bind</c> (<c>GrantExternalAccessEndpoint.CreateGrantAsync</c>, default mode).
    /// </summary>
    /// <remarks>The systemuser is stamped when it resolves; an audit field never blocks the write (the core's rule) —
    /// the contact stamp is cleared either way, because that is what protects the internal decision. Applies equally to a
    /// row whose issuing contact was deleted (<see cref="ExternalGrantRow.IsContactIssued"/>): it is the internal user's
    /// from now on, so its provenance no longer names a contact.</remarks>
    internal static void AddInternalTakeOverFields(IDictionary<string, object> fields, Guid? changingSystemUserId)
    {
        ArgumentNullException.ThrowIfNull(fields);
        fields[GrantedByContactAttribute] = DBNull.Value; // IGenericEntityService: DBNull.Value CLEARS the column
        fields[GrantedByContactIdAttribute] = DBNull.Value;
        if (changingSystemUserId is { } systemUserId && systemUserId != Guid.Empty)
            fields[GrantedByAttribute] = new Microsoft.Xrm.Sdk.EntityReference("systemuser", systemUserId);
    }

    /// <summary>
    /// The Web API shape of <see cref="AddInternalTakeOverFields"/> — what the grant core's default mode adds to its PATCH when
    /// it CHANGES a contact-issued row (<see cref="ExternalGrantRow.IsContactIssued"/>): the contact lookup unbound (only when
    /// it is still bound — a deleted issuer's is already empty), its provenance cleared in the same write (round 50 item 2),
    /// and <c>sprk_grantedby</c> bound to the changing systemuser when it resolved.
    /// </summary>
    internal static void AddInternalTakeOverBinds(IDictionary<string, object?> update, ExternalGrantRow row, Guid? changingSystemUserId)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(row);
        if (row.GrantedByContactId is not null)
            update[$"{GrantedByContactNavigationProperty}@odata.bind"] = null;
        update[GrantedByContactIdAttribute] = null;
        if (changingSystemUserId is { } systemUserId && systemUserId != Guid.Empty)
            update[$"{GrantedByNavigationProperty}@odata.bind"] = $"/systemusers({systemUserId})";
    }

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
    /// Every ACTIVE row on one record that a given CONTACT issued (<c>sprk_grantedbycontact</c>, task 140) — what the
    /// contact-side list shows the caller. Same row shape and root half as <see cref="ActiveRowsForRootFilter"/>; the
    /// issuer half is the only addition. No expiry predicate: the caller's own lapsed grants are listed so they can see
    /// and revoke them. Exceptions propagate.
    /// </summary>
    internal static Task<List<ExternalGrantRow>> QueryActiveRowsIssuedByContactAsync(
        DataverseWebApiClient dataverseClient, ExternalGrantRootType rootType, Guid rootId, Guid issuerContactId,
        CancellationToken ct)
        => dataverseClient.QueryAsync<ExternalGrantRow>(
            EntitySet,
            filter: $"{ActiveRowsForRootFilter(rootType, rootId)} and _sprk_grantedbycontact_value eq {issuerContactId}",
            select: RowSelect,
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

    // ── Grantor ceiling, never-lower and deny-list refusals (task 139 · owner C4 / Q1) ──────────────

    /// <summary>
    /// Stable reason code (403): the grantor's own rights on the record allow granting nothing — or could not be
    /// established, which <c>CallerRecordAccessProbe</c> reports the same way (it answers None, it does not throw).
    /// Distinct from <c>/share-user</c>'s <c>sdap.access.user_share.caller_cannot_grant</c>, which predates it.
    /// </summary>
    internal const string CallerCannotGrantReasonCode = "sdap.access.grant.caller_cannot_grant";

    /// <summary>
    /// Stable reason code (409): the request was narrowed to the grantor's level, and the grantee already holds MORE
    /// than that — writing would lower access someone else gave. Used by <c>/grant</c>, <c>/invite-and-grant</c> and
    /// <c>/share-user</c>; task 140 reuses it verbatim.
    /// </summary>
    internal const string WouldLowerExistingReasonCode = "sdap.access.grant.would_lower_existing";

    /// <summary>
    /// Stable reason code (422): the grantee — the contact, one of its active organizations, or the organization of an
    /// organization-wide grant — is on this record's No Access list (a matching entry). Task 140 reuses it verbatim.
    /// Since task 142 r4 a list that could not be read is NOT this code: it is
    /// <see cref="GranteeNoAccessUnverifiableReasonCode"/>.
    /// </summary>
    internal const string GranteeDeniedReasonCode = "sdap.access.grant.grantee_denied";

    /// <summary>
    /// Stable reason code (503, task 142 r4 · owner round 13 item 4): whether the grantee is on this record's No Access
    /// list could not be checked — a read fault (Dataverse 5xx, throttling, a timeout, unreadable memberships or
    /// referenced organizations, a fail-closed deny-list read). Nothing was granted (fail closed); retryable. The grant
    /// routes' sibling of <c>/share-user</c>'s <c>sdap.access.user_share.no_access_unverifiable</c>. Task 140 reuses it
    /// verbatim.
    /// </summary>
    internal const string GranteeNoAccessUnverifiableReasonCode = "sdap.access.grant.no_access_unverifiable";

    /// <summary>Stable reason code (500): the grantor's own rights on the record could not be read (the probe threw).</summary>
    internal const string CallerRightsUnreadableReasonCode = "sdap.access.grant.caller_rights_unreadable";

    /// <summary>
    /// Stable reason code (409, task 140 · no proxy revocation or extension): a CONTACT grantor asked to grant someone
    /// who already holds an active row on the record that somebody ELSE issued (a systemuser, the Assigned-To rule, or
    /// another contact). The row is left exactly as it is — re-stamping it would let the contact later revoke it, and
    /// extending it would override the issuer's own time bound.
    /// </summary>
    internal const string ContactGrantManagedElsewhereReasonCode = "sdap.access.contact_grant.managed_elsewhere";

    /// <summary>
    /// The lower of the requested level and the ceiling — levels are ordered by their option-set values
    /// (View Only 100000000 &lt; Collaborate 100000001 &lt; Full Access 100000002).
    /// </summary>
    internal static ExternalAccessLevel CapAt(ExternalAccessLevel requested, ExternalAccessLevel ceiling)
        => (int)requested <= (int)ceiling ? requested : ceiling;

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
            // Task 174 (owner round 84; #1442): the EFFECTIVE flags — a work assignment or project filed under a secure,
            // Limited or Restricted parent refuses as its parent would; an undecidable filing is unreadable (503).
            flags = await participations
                .GetEffectiveRootRecordFlagsAsync(ExternalGrantRoot.LogicalNameFor(rootType), new[] { rootId }, ct)
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

    /// <summary>
    /// Deactivates rows ONLY IF each is still at the version it was read with (<see cref="ExternalGrantRow.ETag"/> as
    /// <c>If-Match</c>, <see cref="DataverseWebApiClient.UpdateIfMatchAsync"/>). A row that changed since the read is NOT
    /// deactivated: it is returned in <see cref="ConditionalDeactivation.ChangedSinceRead"/>, and what that means is the
    /// caller's decision — there is no retry.
    /// </summary>
    /// <remarks>
    /// <para><b>Why (session 27 round 42 item 1).</b> A CONTACT may deactivate only rows it issued, and decides "issued by
    /// me" from a read. An internal user can take such a row over (round 34 item 3) between that read and the write; an
    /// unconditional write would then end a decision the internal user just made. Every contact-path deactivation — the
    /// contact revoke and the contact-issuer mode's duplicate collapses — goes through here.</para>
    /// <para>Every other fault propagates exactly as in <see cref="DeactivateAsync"/> (a partial sweep surfaces as a
    /// failure, never as success), including a row with no version (nothing is sent for it) and a row that no longer
    /// exists.</para>
    /// </remarks>
    internal static async Task<ConditionalDeactivation> DeactivateIfUnchangedAsync(
        DataverseWebApiClient dataverseClient,
        IEnumerable<ExternalGrantRow> rows,
        ILogger logger,
        CancellationToken ct)
    {
        var deactivated = new List<Guid>();
        var changed = new List<Guid>();

        foreach (var row in rows)
        {
            try
            {
                await dataverseClient.UpdateIfMatchAsync(EntitySet, row.Id, new { statecode = 1, statuscode = 2 }, row.ETag ?? string.Empty, ct);
            }
            catch (System.Data.DBConcurrencyException)
            {
                changed.Add(row.Id);
                logger.LogWarning(
                    "[EXT-GRANT-LIFECYCLE] Access record {AccessRecordId} changed since it was read ({ETag}); it was NOT " +
                    "deactivated — it is no longer certainly the reader's to end.", row.Id, row.ETag);
                continue;
            }

            deactivated.Add(row.Id);
            logger.LogInformation("[EXT-GRANT-LIFECYCLE] Deactivated access record {AccessRecordId} (If-Match {ETag})", row.Id, row.ETag);
        }

        return new ConditionalDeactivation(deactivated, changed);
    }

    /// <summary>
    /// Re-reads each row that changed between a contact's "issued by me" read and its conditional write (session 27 round
    /// 42 item 1: "the row is re-read for the response") and logs what it now is — never writing it again (no blind
    /// retry). A re-read that fails is logged and yields <c>null</c> for that row; the caller's answer does not depend on it.
    /// </summary>
    /// <returns>The rows as they are now, in <paramref name="rowIds"/> order (<c>null</c> where the re-read failed or the row
    /// is gone).</returns>
    internal static async Task<IReadOnlyList<ExternalGrantRow?>> ReReadChangedRowsAsync(
        DataverseWebApiClient dataverseClient,
        IEnumerable<Guid> rowIds,
        Guid callerContactId,
        ILogger logger,
        CancellationToken ct)
    {
        var current = new List<ExternalGrantRow?>();
        foreach (var rowId in rowIds)
        {
            ExternalGrantRow? row;
            try
            {
                row = await RetrieveRowAsync(dataverseClient, rowId, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex,
                    "[EXT-GRANT-LIFECYCLE] Access record {AccessRecordId} changed after contact {ContactId} checked it was " +
                    "theirs, and could not be re-read; nothing was written over it.", rowId, callerContactId);
                current.Add(null);
                continue;
            }

            logger.LogWarning(
                "[EXT-GRANT-LIFECYCLE] Access record {AccessRecordId} changed after contact {ContactId} checked it was theirs; " +
                "nothing was written over it. Now: active {Active}, level {Level}, expiry {Expiry}, issued by contact " +
                "{IssuerContactId} / systemuser {IssuerSystemUserId}.",
                rowId, callerContactId, row?.IsActive, row?.AccessLevel, row?.ExpiresDate,
                row?.GrantedByContactId, row?.GrantedBySystemUserId);
            current.Add(row);
        }

        return current;
    }

    // ── A contact's own access on one record (task 140 · owner G2 (i); session 27 round 42 item 2) ────────────

    /// <summary>
    /// The grant rows that can carry a CONTACT's own access to one record at <paramref name="minimumLevel"/> or above — the
    /// ONE definition of "the contact's own grant" behind task 140's expiry rule. The contact-side grant route caps the
    /// expiry a contact may issue at the latest of them (owner decision G2 (i): a grantor cannot hand out time they do not
    /// hold), and the reconciliation job caps — or ends — a contact-issued row that carries no expiry by the same rows
    /// (session 27 round 42 item 2: a contact-issued grant may never outlive its issuer's own access).
    /// </summary>
    /// <remarks>
    /// <para><b>Which rows</b> — exactly the rows the read path lets confer (<c>ExternalParticipationService</c>): the
    /// contact's own active rows on the record; and, unless the record is Secure or Limited (direct grants only, FR-22),
    /// the active organization-wide rows of its CONFERRING organizations (<paramref name="conferringOrganizationIds"/> —
    /// task 109's conferring set). Flags that are absent or unreadable read as direct-only: the fail-closed reading, with
    /// fewer sources and so an earlier date. Only rows at <paramref name="minimumLevel"/> or above count, and only rows
    /// <paramref name="mayConfer"/> keeps. A direct row naming a firm confers only while that firm is active (ISS-026): a
    /// firm that is one of the contact's conferring organizations is active by construction; any other is read, and one
    /// that no longer exists (404) means the row does not count.</para>
    /// <para><b>Faults propagate</b> — a read that could not be completed is never "no rows".</para>
    /// </remarks>
    /// <param name="mayConfer">Rows it rejects are dropped BEFORE any firm is read. The route keeps only rows that confer
    /// today; the job also keeps undated rows, whose date the same run may be about to stamp.</param>
    internal static async Task<ContactHeldGrants> ReadContactHeldGrantsAsync(
        Guid contactId,
        IReadOnlyCollection<Guid> conferringOrganizationIds,
        ExternalGrantRootType rootType,
        Guid rootId,
        ExternalAccessLevel minimumLevel,
        Func<ExternalGrantRow, bool> mayConfer,
        DataverseWebApiClient dataverseClient,
        ExternalParticipationService participations,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(mayConfer);

        // Task 174: the EFFECTIVE flags, as the read path folds them — the rows that count are the rows the read path lets confer.
        var flags = await participations.GetEffectiveRootRecordFlagsAsync(ExternalGrantRoot.LogicalNameFor(rootType), new[] { rootId }, ct);
        var directOnly = !flags.TryGetValue(rootId, out var f) || f.IsUnreadable || f.IsDirectOnly;

        var rows = new List<ExternalGrantRow>(
            await QueryActiveRowsAsync(dataverseClient, ExternalGrantKey.ForContact(rootType, rootId, contactId), ct));

        if (!directOnly)
        {
            foreach (var organizationId in conferringOrganizationIds)
            {
                rows.AddRange(await QueryActiveRowsAsync(
                    dataverseClient, ExternalGrantKey.ForOrganization(rootType, rootId, organizationId), ct));
            }
        }

        var held = new List<ExternalGrantRow>();
        foreach (var row in rows.Where(r => (r.AccessLevel ?? 0) >= (int)minimumLevel && mayConfer(r)))
        {
            if (row.ContactId is not null && row.OrganizationId is { } firm && firm != Guid.Empty
                && !conferringOrganizationIds.Contains(firm))
            {
                ExternalParticipationService.OrganizationStateRow? organization;
                try
                {
                    organization = await dataverseClient.RetrieveAsync<ExternalParticipationService.OrganizationStateRow>(
                        "sprk_organizations", firm, "statecode", ct);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    organization = null; // a deleted firm confers nothing — the row does not count
                }

                if (!ExternalParticipationService.OrganizationIsActive(organization))
                    continue;
            }

            held.Add(row);
        }

        return new ContactHeldGrants(directOnly, held);
    }
}

/// <summary>What <see cref="ExternalGrantLifecycle.DeactivateIfUnchangedAsync"/> did.</summary>
/// <param name="Deactivated">Rows that were still at their read version and are now inactive.</param>
/// <param name="ChangedSinceRead">Rows that changed since they were read (HTTP 412) and were left exactly as they are.</param>
internal sealed record ConditionalDeactivation(IReadOnlyList<Guid> Deactivated, IReadOnlyList<Guid> ChangedSinceRead);

/// <summary>What <see cref="ExternalGrantLifecycle.ReadContactHeldGrantsAsync"/> found.</summary>
/// <param name="DirectOnly">The record admits only direct grants for contacts (Secure, Limited, or flags that could not be
/// read), so organization-wide rows were not read.</param>
/// <param name="Rows">The rows that can carry the contact's access, as read — each with its own expiry, possibly none.</param>
internal sealed record ContactHeldGrants(bool DirectOnly, IReadOnlyList<ExternalGrantRow> Rows);

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
/// so a refusal can never become the grant routes' catch-all 500 (task 138). Since task 139 it also carries the grant
/// core's other write-time refusals: the grantor ceiling (403), never-lower (409) and the No Access list (422).
/// </summary>
/// <param name="IsAllowed">The grant may be written.</param>
/// <param name="ReasonCode">The binding reason code for a refusal; <c>null</c> when allowed.</param>
/// <param name="StatusCode">422 for a policy or deny-list refusal, 503 for an unreadable policy, 403 when the grantor
/// may grant nothing, 409 when the grant would lower existing access; 200 when allowed.</param>
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

    /// <summary>
    /// Task 139: the grantor's own access on the record allows granting nothing (or could not be established — the
    /// probe answers None for both, deliberately). 403.
    /// </summary>
    public static GrantPolicyDecision CallerCannotGrant { get; } = new(
        false,
        ExternalGrantLifecycle.CallerCannotGrantReasonCode,
        StatusCodes.Status403Forbidden,
        "You can only give someone the access you have on this record, and your own access to it could not be " +
        "confirmed. Nothing was granted. Try again; if it persists, ask someone with access to the record.");

    /// <summary>
    /// Task 139: the request was narrowed to the grantor's own level and the grantee already holds more. 409 — writing
    /// would LOWER access someone else gave while the caller asked for more.
    /// </summary>
    /// <param name="grantedLevel">The level the request was narrowed to.</param>
    public static GrantPolicyDecision WouldLowerExisting(ExternalAccessLevel? grantedLevel) => new(
        false,
        ExternalGrantLifecycle.WouldLowerExistingReasonCode,
        StatusCodes.Status409Conflict,
        "They already have more access than you can grant" +
        (grantedLevel is { } level ? $" (you can grant up to {DisplayName(level)})" : string.Empty) +
        ". Nothing was changed, so their existing access stays as it is.");

    /// <summary>
    /// Task 139 (FR-23 at write time): the grantee is on this record's No Access list (a matching entry). 422. The detail
    /// never names the entry or its reason (task 143's rule for refusal messages). A list that could not be checked is
    /// <see cref="GranteeDenyListUnreadable"/> since task 142 r4, so this answer now means an entry and nothing else.
    /// </summary>
    public static GrantPolicyDecision GranteeDenied { get; } = new(
        false,
        ExternalGrantLifecycle.GranteeDeniedReasonCode,
        StatusCodes.Status422UnprocessableEntity,
        "This contact or organization cannot be given access to this record: it is on the record's No Access list. " +
        "Nothing was granted.");

    /// <summary>
    /// The No Access check could not be completed (task 142 r3 for a throw; r4 · owner round 13 item 4 for every read
    /// fault, through <see cref="NoAccessCheckAnswer.Unverifiable"/>): refused, fail closed, and REPORTED as a fault —
    /// 503 <see cref="ExternalGrantLifecycle.GranteeNoAccessUnverifiableReasonCode"/>, retryable — never absorbed into
    /// <see cref="GranteeDenied"/>. <see cref="IsDenyListReadFault"/> lets an in-process caller tell it apart without
    /// comparing codes: the Assigned-To materializer waits on an entry (a policy hold) and must report a fault.
    /// </summary>
    /// <remarks>
    /// Before r4 this carried <see cref="GranteeDenied"/>'s code, 422 and detail, and the faults the deny-veto code
    /// absorbed itself (an unreadable membership read, a fail-closed deny-list read) reached the grant routes as a plain
    /// "denied" — an outage told the operator the person was on the list. The tri-state answer removes both.
    /// </remarks>
    public static GrantPolicyDecision GranteeDenyListUnreadable { get; } = new(
        false,
        ExternalGrantLifecycle.GranteeNoAccessUnverifiableReasonCode,
        StatusCodes.Status503ServiceUnavailable,
        "Whether this contact or organization is on the record's No Access list could not be checked, so nothing was " +
        "granted. Try again in a moment.")
    {
        IsDenyListReadFault = true,
    };

    /// <summary>
    /// Task 140: a CONTACT grantor's request names a grantee who already holds an active row somebody else issued. 409;
    /// nothing is changed. The contact-side handler restates the detail with the grantee's name.
    /// </summary>
    public static GrantPolicyDecision ManagedElsewhere { get; } = new(
        false,
        ExternalGrantLifecycle.ContactGrantManagedElsewhereReasonCode,
        StatusCodes.Status409Conflict,
        "This person already has access to this record that was granted by someone else; ask them or the record's " +
        "team to change it. Nothing was changed.");

    /// <summary>
    /// The refusal is the No Access check's read FAULT, not an entry (<see cref="GranteeDenyListUnreadable"/>). In-process
    /// only: never part of a response (<c>PolicyRefusalProblem</c> maps the code, status and detail explicitly).
    /// </summary>
    public bool IsDenyListReadFault { get; init; }

    /// <summary>The level's name as the Manage Access dialog shows it.</summary>
    internal static string DisplayName(ExternalAccessLevel level) => level switch
    {
        ExternalAccessLevel.ViewOnly => "View Only",
        ExternalAccessLevel.Collaborate => "Collaborate",
        ExternalAccessLevel.FullAccess => "Full Access",
        _ => level.ToString(),
    };
}

/// <summary>
/// The highest level a grant may be written at — a REQUIRED input of the one grant-writing core
/// (<c>GrantExternalAccessEndpoint.CreateGrantAsync</c>), unified-access-control-r2 task 139 (WP-1, owner Q1).
/// </summary>
/// <remarks>
/// <para><b>Why the core takes it, rather than each handler applying it.</b> The ceiling, the never-lower rule and
/// the write-time refusals are enforced INSIDE the method every grant writer calls, so no writer can persist a grant
/// without stating its ceiling: <c>/grant</c> and <c>/invite-and-grant</c> pass the human grantor's own level, task
/// 140's contact-side route will pass the contact grantor's effective level, and task 142's Assigned-To auto-grants
/// will pass an explicit, documented Collaborate ceiling (owner rule 5: uncapped Collaborate). A new writer adds a
/// NAMED factory here, with its basis documented — there is no public constructor, so a ceiling cannot be invented
/// inline. The ArchTest <c>GrantCeilingGuardTests</c> pins that every call site supplies one.</para>
/// <para><b>Never-lower for a writer with no human grantor (task 142).</b> The core refuses to lower an existing
/// higher grant only when the ceiling NARROWED the request; an auto-grant asking for exactly Collaborate is not
/// narrowed, so task 142 must check the existing level itself (or add that rule here) rather than downgrade a Full
/// Access grant someone made by hand.</para>
/// </remarks>
internal sealed class GrantCeiling
{
    private GrantCeiling(ExternalAccessLevel? level, string basis)
    {
        Level = level;
        Basis = basis;
    }

    /// <summary>The highest level that may be written, or <c>null</c> when nothing may be granted.</summary>
    public ExternalAccessLevel? Level { get; }

    /// <summary>Where the ceiling came from — logged with every narrowing and refusal.</summary>
    public string Basis { get; }

    /// <summary>
    /// The ceiling of a HUMAN grantor: their own rights on the record, freshly probed as them over OBO, through
    /// the one ceiling table <see cref="ExternalAccessLevels.GrantCeilingFor"/>.
    /// </summary>
    public static GrantCeiling FromGrantorRights(AccessRights grantorRights)
        => new(ExternalAccessLevels.GrantCeilingFor(grantorRights), $"grantor rights {grantorRights}");

    /// <summary>
    /// The ceiling of a CONTACT grantor (unified-access-control-r2 task 140, owner C4 / Q1, settled round 3b): the
    /// contact's EFFECTIVE post-veto rights on the record — the evaluator's answer carried on <c>CallerPrincipal</c>,
    /// on either sign-in plane — through the same ceiling table as a human systemuser grantor
    /// (<see cref="ExternalAccessLevels.GrantCeilingFor"/>).
    /// </summary>
    /// <remarks>
    /// <para><b>Why a contact's rights, not a probe.</b> A contact is not a Dataverse principal, so there is no
    /// <c>RetrievePrincipalAccess</c> answer to probe (and the CIAM token cannot be exchanged OBO). The evaluator's answer
    /// is already rights-based (task 033), vetoed (Restricted, the No Access list, inactive roots — tasks 135/137) and
    /// Read-gated (task 136), and on a Secure or Limited record it counts the contact's DIRECT grants only (FR-22): exactly
    /// the level the owner's rule is about (decision G3 (a), round 3: the level a contact HOLDS).</para>
    /// <para>A contact's rights map is always one of R / R|C|W / R|C|W|D (<see cref="ExternalAccessLevels.ToAccessRights"/>)
    /// or a union of those, so the table maps them back exactly.</para>
    /// </remarks>
    public static GrantCeiling FromContactGrantorRights(AccessRights contactGrantorRights)
        => new(
            ExternalAccessLevels.GrantCeilingFor(contactGrantorRights),
            $"contact grantor effective rights {contactGrantorRights}");

    /// <summary>
    /// The ceiling of an Assigned-To auto-grant (unified-access-control-r2 task 142): exactly Collaborate, with NO
    /// grantor-level cap. Owner round 3 A1 and round 3b: "Assigned-To auto-grants follow rule 5: always Collaborate,
    /// uncapped" — the grantor is the owner's rule, not a person, so there is no person's level to cap at (the cap stays
    /// for MANUAL Grant Access). Used ONLY by <c>AssignedAccessMaterializer</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Never-lower is the materializer's</b> (this type's remarks): a request for exactly Collaborate is not
    /// narrowed, so the core's never-lower rule would not fire. The materializer therefore reads the key's active rows
    /// first and writes nothing over a row at Collaborate or above (CoveredByExisting); it calls the core only to create,
    /// to raise a lower row, to renew its OWN unmodified row, or to put a raised row back to its prior level.</para>
    /// </remarks>
    public static GrantCeiling AssignedToRule { get; } =
        new(ExternalAccessLevel.Collaborate, "owner rule 5 (task 142): Assigned-To auto-grant, uncapped Collaborate");

    public override string ToString() => $"{Level?.ToString() ?? "none"} ({Basis})";
}

/// <summary>
/// The CONTACT who is issuing a grant through the contact-side routes (unified-access-control-r2 task 140). Passing one
/// to the grant core switches it into the contact-issuer mode: the row is stamped with <c>sprk_grantedbycontact</c>
/// (and <c>sprk_grantedby</c> stays empty), a grantee holding a row anybody else issued is refused
/// (<see cref="GrantPolicyDecision.ManagedElsewhere"/>), the caller's own row is never LOWERED (any lower request,
/// narrowed or not, is <see cref="GrantPolicyDecision.WouldLowerExisting"/>) and its expiry is never SHORTENED, and the
/// written expiry is capped at <see cref="ExpiryCap"/>.
/// </summary>
/// <param name="ContactId">The grantor contact — the caller.</param>
/// <param name="ExpiryCap">
/// The latest date the grantor's own qualifying access lasts (owner decision G2 (i), session 27 round 3: a grantor
/// cannot hand out time they do not hold), or <c>null</c> when the grantor's level does not rest on a dated grant
/// (a standing-grant or organization-expansion term, which carries no date). The written expiry is the requested one
/// (or today + 90) capped at this date.
/// </param>
internal sealed record ContactGrantIssuer(Guid ContactId, DateOnly? ExpiryCap);

/// <summary>Who a grant check is about — the shape the write-time checks need (task 139).</summary>
/// <param name="Kind">The grantee kind the record's access policy judges.</param>
/// <param name="Key">The logical grant key whose existing rows the never-lower rule reads; <c>null</c> for a
/// contact that does not exist yet (<c>/invite-and-grant</c> before onboarding), which has no rows.</param>
/// <param name="ContactId">The contact checked against the No Access list as a direct subject, if any.</param>
/// <param name="OrganizationIds">Organizations checked as deny subjects besides the contact's own memberships: the
/// organization of an organization-wide grant, or the firm a contact grant names.</param>
internal sealed record GrantGrantee(
    GrantGranteeKind Kind,
    ExternalGrantKey? Key,
    Guid? ContactId,
    IReadOnlyCollection<Guid> OrganizationIds)
{
    /// <summary>The grantee of an existing grant key (a named contact, or an organization-wide grant).</summary>
    /// <param name="key">The key the upsert writes.</param>
    /// <param name="firmOrganizationId">The firm a contact grant names (association metadata, but a deny subject).</param>
    public static GrantGrantee ForKey(ExternalGrantKey key, Guid? firmOrganizationId)
    {
        var orgs = new List<Guid>();
        if (key.IsOrganizationGrant && key.OrganizationId is { } grantOrg)
            orgs.Add(grantOrg);
        else if (firmOrganizationId is { } firm && firm != Guid.Empty)
            orgs.Add(firm);

        return new GrantGrantee(
            key.IsOrganizationGrant ? GrantGranteeKind.Organization : GrantGranteeKind.Contact,
            key,
            key.ContactId,
            orgs);
    }

    /// <summary>
    /// A contact that does not exist yet: the person <c>/invite-and-grant</c> would onboard. It holds no grant, so
    /// only the policy, the ceiling and the deny list (on the request's firm) apply.
    /// </summary>
    public static GrantGrantee ProspectiveContact(Guid? firmOrganizationId)
        => new(
            GrantGranteeKind.Contact,
            Key: null,
            ContactId: null,
            firmOrganizationId is { } firm && firm != Guid.Empty ? new[] { firm } : Array.Empty<Guid>());
}
