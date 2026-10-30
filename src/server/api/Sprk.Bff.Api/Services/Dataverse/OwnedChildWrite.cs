using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using static Sprk.Bff.Api.Services.Dataverse.CallerWriteAuthorization;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// How a chat tool writes a row FILED under a project, matter or work assignment (unified-access-control-r2 task 146 r2,
/// verifier items 2 and 6): the owner decision belongs to <see cref="IRecordOwnershipResolver"/> — the named Secure team
/// for a child of a secure record, the record's business-unit team otherwise — never the calling user.
/// </summary>
/// <remarks>
/// <para><b>Owner decision S1, option (1)</b> (session-27 round 3, accepted as recommended; it names "AI email draft, AI
/// create-record" explicitly), <b>refined by G5</b> (round 3b): the BFF checks AS THE USER that they could make the row
/// themselves, then the APPLICATION creates it OWNED BY THE TEAM — "same rule that assigned to the bu/team not
/// individual". A run-as-user create leaves the row owned by the caller in their own business unit, readable by every
/// colleague with ordinary depth even when it is filed to a secure record. Create-then-assign (option 2) leaves that
/// window open; widening user roles with prvAssign (option 3) is forbidden.</para>
/// <para><b>CLAUDE.md §6.5 path B — owner round 7 item 3 (2026-10-02), superseding the r2 path-A exception.</b>
/// spaarke-ai-architecture-redesign-r1 bound its tool plane "MUST run user-OBO for all Dataverse tool access"
/// (FR-P0-10, the handlers' "User-OBO ONLY" rule). The owner amended that rule for <c>dataverse.create_record</c> and
/// <c>email.draft</c>: "apply the G5 pattern — check the caller's rights AS THE USER, create AS THE APP owned by the team
/// (the named secure team under a secure parent; otherwise the RecordOwnershipResolver team), record the person in the
/// table's Assigned-To / 'for' column where one exists" (amendment recorded in that project's spec and in the task 146
/// note §13). <c>dataverse.create_record</c> therefore takes this path for EVERY create (<see cref="PathFor"/>), except
/// where the resolver keeps the creator (unfiled communications/threads, E1/E2), per-user tables, and tables with no
/// user/team ownership. <c>email.draft</c> takes it for a filed draft (an unfiled draft keeps its creator, E1).
/// Reads and deletes stay user-OBO, and so does an update's PATCH. The update tool's re-file owner assignment
/// (<c>DataverseUpdateRecordHandler</c> → <c>ReparentAsync</c>) is app-only too, and since owner round 13 item 7
/// (2026-10-03) it is folded into the same path-B amendment ("as round 8 did for 156's re-stamp"); the path-A record of
/// task 146 note §12c is superseded for it.</para>
/// <para><b>Who asked (task 146 c1-r1, owner round 13 item 9).</b> The app-only create records the caller as the row's
/// creator person (<c>sprk_createdbyperson</c>), so F3's "or the creator" branch admits them; a caller-supplied value for
/// that column is refused like the owner.</para>
/// <para><b>What "as the user" covers</b> — everything a run-as-user create would have had Dataverse check, so the
/// app-only create grants nothing the caller lacks: (1) the table's Create privilege, and Append when the row sets a
/// lookup, by the privilege names the table's own metadata declares (activity tables share <c>prvCreateActivity</c>);
/// (2) AppendTo on every record a lookup names — and on the record the ADR-024 regarding pair names, which is text a
/// run-as-user create would never have checked but which files the row exactly as a lookup does (see
/// <see cref="PairTargetAsync"/>) — (<see cref="CheckCallerMayAppendToAsync"/>): <c>RetrievePrincipalAccess</c> for a
/// user- or team-owned target; for an ORGANIZATION-OWNED target (the ADR-024 record-type catalog <c>sprk_recordtype_ref</c>,
/// the matter-type and practice-area reference tables), which that message refuses (400 0x80040800), the table's AppendTo
/// PRIVILEGE plus a read of the row as the caller (<see cref="CallerMayAppendToOrganizationOwnedAsync"/>) — what a
/// run-as-user create checks there; for a BUSINESS-OWNED target (<c>systemuser</c>, <c>team</c>, <c>businessunit</c>),
/// which RetrievePrincipalAccess also refuses, that privilege at a DEPTH covering the row's business unit, plus a read of
/// the row as the caller (<see cref="CallerMayAppendToBusinessOwnedAsync"/>, ontology task 126, uac-r2's rule). A target no
/// path can answer is denied, and a missing row answers like an unappendable one
/// (the uniform not-found); (3) no column under field-level security
/// (refused: an app-only write would pass the caller's column security); (4) no owner or audit column (the server sets
/// the owner). Every question is asked through <see cref="IDataverseUserClient"/> under the caller's own token, so the
/// identity is the credential, not data. Any failure denies.</para>
/// <para><b>Component justification (CLAUDE.md §11).</b> Existing — <c>CallerRecordAccessProbe</c> answers the same
/// questions from a raw bearer token the tool handlers do not hold, and <c>FinanceAuthorizationFilter</c> is a route
/// filter; neither runs inside a tool call. Extension — the probe's pure parsers (<c>ResponseGrantsPrivilege</c>,
/// <c>DataverseAccessRightsMapper</c>) are reused, not copied. Cost of doing nothing — the two run-as-user create tools
/// file a secure record's child in an ordinary business unit (verifier item 6), and the update tool re-files one with no
/// owner re-derivation (item 2).</para>
/// </remarks>
internal static class OwnedChildWrite
{
    /// <summary>Columns the server owns on the app-only path; a request that sets one is refused, never honoured.</summary>
    /// <remarks>Field-mapping-server-write-path-r1 task 012: a view over
    /// <see cref="Sprk.Bff.Api.Services.RecordCreation.RecordTableRules.CommonServerOwnedColumns"/>, the one source; deleted
    /// when task 013 switches its readers to the registry.</remarks>
    internal static readonly IReadOnlySet<string> ServerOwnedColumns =
        Sprk.Bff.Api.Services.RecordCreation.RecordTableRules.CommonServerOwnedColumns;

    /// <summary>
    /// Task 158: every column a mapped write sets, as <see cref="SecureRootFilingGate.CheckAsync"/> reads them — each lookup
    /// as an <see cref="Microsoft.Xrm.Sdk.EntityReference"/>, each cleared column as <c>null</c>, each plain value as written
    /// (the polymorphic pair's id is a text column) — so the gate can tell what a work assignment or project will be filed
    /// under.
    /// </summary>
    internal static IReadOnlyList<KeyValuePair<string, object?>> WritesOf(DataverseWriteItemMapper.MappedItem item)
    {
        var writes = new List<KeyValuePair<string, object?>>();
        foreach (var lookup in item.Lookups)
            writes.Add(new(lookup.Column, new Microsoft.Xrm.Sdk.EntityReference(lookup.RelatedTable, lookup.RecordId)));
        foreach (var column in item.ClearedColumns)
            writes.Add(new(column, null));

        using var body = JsonDocument.Parse(item.JsonBody);
        foreach (var property in body.RootElement.EnumerateObject())
        {
            if (property.Name.Contains('@') || property.Value.ValueKind == JsonValueKind.Null)
                continue; // a lookup bind (taken above) or a clear (taken above)

            writes.Add(new(property.Name, property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : property.Value.GetRawText()));
        }

        return writes;
    }

    /// <summary>The ownership parents a mapped row of <paramref name="table"/> is filed under (resolver's definition — no per-table
    /// list). #2084: a lookup that CITES rather than files (<see cref="RecordOwnershipResolver.IsCitation"/>, the one predicate the
    /// resolver's own <c>ParentsOf</c> applies) is left out, so a Signal created naming its rule is never filed under the rule.
    /// UAC task 194b fix round F4: in the table's primary order (<see cref="RecordOwnershipContext.InPrimaryOrder"/>, the order the
    /// reconciler asks the row in once it exists), never the order the caller's payload named the lookups in — the first is the
    /// primary, whose business unit decides an ordinary row's owner.</summary>
    internal static IReadOnlyList<RecordOwnershipParent> ParentsOf(string table, DataverseWriteItemMapper.MappedItem item) =>
        OwnershipOf(table, item).Parents; // 194b R (B3): the one builder's parents, so the two cannot drift

    /// <summary>UAC task 194b fix round R (B3): the ownership question for a mapped row of <paramref name="table"/> — the one
    /// builder (<see cref="RecordOwnershipContext.ForRow"/>) over the lookups the item writes and its regarding pair id.</summary>
    internal static RecordOwnershipContext OwnershipOf(
        string table, DataverseWriteItemMapper.MappedItem item, Guid? callerObjectId = null, Guid? callerSystemUserId = null) =>
        RecordOwnershipContext.ForRow(table, LookupsOf(item), PairIdOf(item.JsonBody), callerObjectId, callerSystemUserId);

    /// <summary>UAC task 194b R4: every lookup the mapped item writes, column → the record it names (the builder filters).</summary>
    internal static IEnumerable<KeyValuePair<string, RecordOwnershipParent?>> LookupsOf(DataverseWriteItemMapper.MappedItem item) =>
        item.Lookups.Select(l => KeyValuePair.Create(l.Column, (RecordOwnershipParent?)new RecordOwnershipParent(l.RelatedTable, l.RecordId)));

    /// <summary>The body's <c>sprk_regardingrecordid</c> (the regarding pair's id, a text column), or <c>null</c>.</summary>
    private static string? PairIdOf(string jsonBody)
    {
        if (string.IsNullOrWhiteSpace(jsonBody))
            return null;
        try
        {
            using var body = JsonDocument.Parse(jsonBody);
            return body.RootElement.ValueKind == JsonValueKind.Object
                   && body.RootElement.TryGetProperty(CoreAncestorResolver.RegardingRecordIdColumn, out var pair)
                   && pair.ValueKind == JsonValueKind.String
                ? pair.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// True when an <c>email.draft</c> takes the owned path: a draft (a <c>sprk_communication</c>, a CHILD in the ownership
    /// set) that names a parent. An unfiled draft keeps its creator — the resolver's own answer for an unfiled
    /// communication (escalation E1: the per-user master thread and Direct-thread privacy rest on it).
    /// </summary>
    internal static bool AppliesTo(string table, DataverseWriteItemMapper.MappedItem item) =>
        RecordOwnershipResolver.IsReparentableChild(table) && ParentsOf(table, item).Count > 0;

    // ── Owner round 7 item 3 (2026-10-02): G5 for EVERY create of dataverse.create_record ────────────────────────────

    /// <summary>Which way a <c>dataverse.create_record</c> create is written.</summary>
    internal enum WritePath
    {
        /// <summary>G5: checked AS THE CALLER, created by the APPLICATION owned by the resolver's team.</summary>
        Owned,

        /// <summary>Created as the caller (their own create; Dataverse authorizes it and the caller owns it).</summary>
        RunAsUser,
    }

    /// <summary>
    /// Tables whose rows are per-user by design and stay owned by their user (task 146 constraint "per-user artifacts"):
    /// a team owner would show one person's notification, layout, navigation pins, preferences or chat history to their
    /// whole business unit.
    /// </summary>
    internal static readonly IReadOnlySet<string> PerUserTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "appnotification", "sprk_notificationoutbox", "sprk_workspacelayout", "sprk_navitem",
        "sprk_userpreferences", "sprk_userprofile", "sprk_aichatmessage", "sprk_aichatsummary",
    };

    /// <summary>
    /// Owner round 7 item 3: the path a <c>dataverse.create_record</c> create of <paramref name="table"/> takes. Every
    /// create is G5 (<see cref="WritePath.Owned"/>) — owned by the team <see cref="IRecordOwnershipResolver"/> names, never
    /// the individual — except where the resolver's own rules keep the creator:
    /// <list type="bullet">
    /// <item>a table whose metadata declares no user/team ownership (<paramref name="ownershipType"/> is neither
    /// <c>UserOwned</c> nor <c>TeamOwned</c> — organization-owned rows have no owner to decide);</item>
    /// <item>a per-user table (<see cref="PerUserTables"/>);</item>
    /// <item>an UNFILED communication or thread (escalation E1 / E2: <see cref="UnfiledOwnership.KeepCreator"/>).</item>
    /// </list>
    /// A run-as-user create still refuses a filing under a SECURE record (the handler's secure-filing check).
    /// </summary>
    internal static WritePath PathFor(string table, DataverseWriteItemMapper.MappedItem item, string? ownershipType)
    {
        if (ownershipType is not ("UserOwned" or "TeamOwned"))
            return WritePath.RunAsUser;
        if (PerUserTables.Contains(table))
            return WritePath.RunAsUser;
        if (KeepsItsCreatorWhenUnfiled(table) && ParentsOf(table, item).Count == 0)
            return WritePath.RunAsUser;
        return WritePath.Owned;
    }

    /// <summary>Communications and threads filed under nothing keep their creator (E1 / E2) — the resolver's
    /// <see cref="UnfiledOwnership.KeepCreator"/> answer, honoured here rather than overridden.</summary>
    internal static bool KeepsItsCreatorWhenUnfiled(string table) =>
        string.Equals(table, "sprk_communication", StringComparison.OrdinalIgnoreCase)
        || string.Equals(table, "sprk_communicationthread", StringComparison.OrdinalIgnoreCase);

    /// <summary>The column a table names the person a record is FOR in, and the table that column looks up.</summary>
    internal sealed record ForPersonColumn(string Column, string RelatedTable);

    /// <summary>
    /// Owner round 7 item 3: "record the person in the table's Assigned-To / 'for' column where one exists". Each entry
    /// follows the precedent already shipped for that table, so one table never has two "for" columns:
    /// <c>sprk_todo</c> / <c>sprk_event</c> → <c>sprk_assignedto</c> (contact; task 152, #1044);
    /// <c>sprk_matter</c> / <c>sprk_project</c> / <c>sprk_workassignment</c> → <c>sprk_assignedtointernal</c> (contact;
    /// owner A7, Office quick-create);
    /// <c>sprk_communication</c> → <c>sprk_sentby</c> (systemuser; S1, the email draft). <c>sprk_workassignment</c> is the
    /// third root and carries the same Assigned To (Internal) / (External) pair as the other two (live metadata,
    /// 2026-10-02). Task 152 (<see cref="AssignedToDefaults.ResponsibleContactColumns"/>) already reads its
    /// <c>sprk_assignedtointernal</c> as the work assignment's responsible internal person, so that column follows the
    /// matter/project precedent. Its extra <c>sprk_assignedto</c> is not a "for" column (b2-r2, verifier b2-r1 item 4; task
    /// note §15b); by owner decision 2026-10-27 (field-mapping-server-write-path-r1 task 037, FR-36, #2354) it is a copied
    /// default instead — the new row's own <c>sprk_assignedtointernal</c>, when the create leaves it unset
    /// (<see cref="Sprk.Bff.Api.Services.RecordCreation.ServerDefault.CopiedFrom"/>, applied by <see cref="CreateAsync"/>).
    /// A table not listed has no "for" column.
    /// </summary>
    /// <remarks>Field-mapping-server-write-path-r1 task 012: a view over the registry's
    /// <see cref="Sprk.Bff.Api.Services.RecordCreation.RecordTableRule.ForPerson"/> entries, the one source; deleted when
    /// task 013 switches its readers to the registry.</remarks>
    internal static readonly IReadOnlyDictionary<string, ForPersonColumn> ForPersonColumns =
        Sprk.Bff.Api.Services.RecordCreation.RecordTableRules.All
            .Where(rule => rule.ForPerson is not null)
            .ToDictionary(
                rule => rule.Table,
                rule => new ForPersonColumn(rule.ForPerson!.Column, rule.ForPerson.RelatedTable),
                StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <paramref name="item"/> with <paramref name="column"/> set to a lookup of <paramref name="relatedTable"/>
    /// (<paramref name="recordId"/>) in the write mapper's object form — so the mapper resolves its navigation property
    /// from metadata, as for every other lookup. The caller adds it only when the item does not set the column itself (a
    /// supplied value is never overwritten).
    /// </summary>
    internal static JsonElement WithLookup(JsonElement item, string column, string relatedTable, Guid recordId)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in item.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            writer.WritePropertyName(column);
            writer.WriteStartObject();
            writer.WriteString("relatedTable", relatedTable);
            writer.WriteString("recordId", recordId.ToString("D"));
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    /// <summary>True when <paramref name="item"/> sets <paramref name="column"/> itself (any value, including null).</summary>
    internal static bool Sets(JsonElement item, string column) =>
        item.ValueKind == JsonValueKind.Object
        && item.EnumerateObject().Any(p => string.Equals(p.Name.Trim(), column, StringComparison.OrdinalIgnoreCase));

    /// <summary>What the owned create (or a check) answered.</summary>
    internal sealed record Outcome
    {
        public Guid? CreatedId { get; init; }

        /// <summary>
        /// Task 037 (fix round 1): with <see cref="CreatedId"/>, the columns the created row was written with — the
        /// caller's own plus any copied default this create filled (a work assignment's Assigned To), so a caller reports
        /// what was actually set rather than what it sent. <c>null</c> when nothing was created.
        /// </summary>
        public IReadOnlyList<string>? WrittenColumns { get; init; }

        /// <summary>Refused by the caller's own rights — surfaced as the user's access error.</summary>
        public string? Denied { get; init; }

        /// <summary>
        /// Task 147 r1: the denial is about a record the row names (no AppendTo on it, or it does not exist) — the two are
        /// indistinguishable on purpose, so an HTTP caller answers them with ONE uniform not-found.
        /// </summary>
        public bool ParentUnavailable { get; init; }

        /// <summary>
        /// Task 018b-1s (G5): with <see cref="ParentUnavailable"/>, the lookup column whose record was refused — the logical
        /// column name without <c>@odata.bind</c> (<c>sprk_regardingmatter</c>), or <c>sprk_regardingrecordid</c> for the
        /// ADR-024 regarding pair's record. <c>null</c> for every other refusal. A structured answer so no caller parses
        /// <see cref="Denied"/>'s text; it names a column of the caller's own payload, never the record.
        /// </summary>
        public string? RefusedColumn { get; init; }

        /// <summary>Refused by the owner decision — the stable <see cref="RecordOwnerRefusal"/> code and its reason.</summary>
        public RecordOwnerResolution? OwnerRefusal { get; init; }

        /// <summary>A question asked as the caller failed at Dataverse — the caller's own error.</summary>
        public DataverseUserResponse? ClientFailure { get; init; }

        /// <summary>
        /// What the row is filed under cannot be told from its ADR-024 pair — a pair id that is not a record id, a pair id
        /// with no record type that repeats no lookup the row binds, or a type that names no table. Decided from the
        /// payload and the record-type catalog only (never from the record the pair names), so it says nothing about that
        /// record. A validation answer (HTTP 400); nothing is created.
        /// </summary>
        public string? Invalid { get; init; }

        /// <summary>
        /// The row would be owned by the Secure team, but its table is not one that team may own from here — a matter (a
        /// root, made secure only through its own Make Secure; a work assignment or project is instead created as an
        /// ordinary row and secured by provisioning, task 158) or a table outside the ownership set (the Secure Record
        /// Owner role holds no Read on it). Nothing is created.
        /// </summary>
        public string? SecureFilingRefused { get; init; }

        /// <summary>
        /// Task 158 r1 (owner round 31): the secure-create plan's refusal — the stable code (an unreadable parent flag, the
        /// caller walled off a secure parent or the record, the named team unresolved) and its reason. Nothing is created.
        /// </summary>
        public RecordOwnerResolution? PlanRefusal { get; init; }

        /// <summary>Task 158 r1: the row was created INTO isolation by this plan — the writer completes it.</summary>
        public SecureRootCreatePlan? Isolated { get; init; }

        /// <summary>
        /// Task 158 r1: the person an isolated create was made for (the caller, by the WhoAmI this create made — its
        /// <c>sprk_createdbyperson</c>), whom completing it shares the row to; never asked a second time.
        /// </summary>
        public Guid? IsolatedFor { get; init; }

        /// <summary>
        /// ebilling-r1 FR-05 (ISS-010, #1973): the invoice-family guard refused the row as it would be written — its stable
        /// <c>invoice_family_*</c> code (<see cref="InvoiceFamilyCoreRecordGuard"/>). Nothing is created.
        /// </summary>
        public InvoiceFamilyCoreRecord? InvoiceFamilyRefusal { get; init; }

        public bool Succeeded =>
            CreatedId is not null
            || (Denied is null && OwnerRefusal is null && ClientFailure is null && Invalid is null && SecureFilingRefused is null
                && PlanRefusal is null && InvoiceFamilyRefusal is null);

        public static readonly Outcome Allowed = new();
    }

    /// <summary>
    /// Creates a FILED child row (S1 / G5): as the caller, checks they could create it themselves; resolves its owner
    /// through the one resolver (secure-if-any); then the application creates it owned by that team, by PATCHing a fresh
    /// id — the create-by-upsert <c>InvoiceReviewService</c> uses for the G5 invoice. Nothing is written on any refusal; a
    /// Dataverse fault propagates.
    /// </summary>
    /// <param name="serverSetLookupColumns">Lookups the SERVER added to the row (e.g. the email draft's sender) — they
    /// name the caller themselves and cost no AppendTo check.</param>
    /// <param name="planSecureCreate">Task 158 r1 (owner round 31): for a work assignment or project, the secure-create plan
    /// (<see cref="SecureRootFilingGate.PlanCreateAsync"/>) — asked AFTER the caller's own G5 check above and given its
    /// AppendTo check on the secure parents (the polymorphic pair is text, not a lookup the mapper checks), so a caller who
    /// may not file under a secure record learns nothing more about it. A refusal creates nothing; an isolated plan creates
    /// the row owned by the plan's named Secure Record Owners team with <c>sprk_issecure = true</c> in the create itself.
    /// <c>null</c> for every other create.</param>
    /// <param name="logger">Task 037: where a copied default that could not be applied is reported (a structured
    /// warning); <c>null</c> reports nothing.</param>
    internal static async Task<Outcome> CreateAsync(
        IDataverseUserClient user,
        IRecordOwnershipResolver ownership,
        IFieldMappingDataverseService appOnly,
        string table,
        DataverseWriteItemMapper.MappedItem item,
        IReadOnlySet<string>? serverSetLookupColumns,
        Guid? callerObjectId,
        CancellationToken ct,
        SecureRootFilingGate? planSecureCreate = null,
        ILogger? logger = null)
    {
        var me = await WhoAmIAsync(user, ct).ConfigureAwait(false);
        if (me.Failure is not null)
            return new Outcome { ClientFailure = me.Failure };

        // Task 037 (FR-36): the table's copied defaults (a work assignment's Assigned To from its own Assigned To Internal),
        // fill-only, on the row BEFORE anything is checked or written — so the value is checked as the caller exactly like
        // a supplied one, written in the create itself, and read back by the "Assigned *" materialisation after it. A copy of
        // a SERVER-set source (the chat create's "for" person, the caller's own contact) names the same server-chosen record,
        // so it is server-set too: it costs no more AppendTo than its source did.
        var defaulted = await WithCopiedDefaultsAsync(user, table, item, logger, ct).ConfigureAwait(false);
        if (defaulted.ClientFailure is { } defaultFailure)
            return new Outcome { ClientFailure = defaultFailure };
        item = defaulted.Item;
        var serverSetCopies = defaulted.Copied
            .Where(c => serverSetLookupColumns?.Contains(c.Source) == true)
            .Select(c => c.Column)
            .ToArray();
        if (serverSetCopies.Length > 0)
        {
            serverSetLookupColumns = new HashSet<string>(serverSetLookupColumns!.Concat(serverSetCopies), StringComparer.OrdinalIgnoreCase);
        }

        var check = await CheckCallerMayCreateAsync(user, me.SystemUserId, table, item, serverSetLookupColumns, ct)
            .ConfigureAwait(false);
        if (!check.Succeeded)
            return check;

        // ebilling-r1 FR-05 (ISS-010, #1973; unified-access-control-r2): an invoice-family row names exactly one core record,
        // and an invoice supersedes only an invoice of that record — asked here, in the one create core, after AppendTo on
        // every record the row binds, so every route that creates such a row is guarded.
        if (await CheckInvoiceFamilyAsync(user, table, item, stored: null, ct).ConfigureAwait(false) is { } family)
        {
            return new Outcome
            {
                InvoiceFamilyRefusal = family.Refusal,
                ClientFailure = family.ClientFailure,
                Denied = family.ParentUnavailable ? NotAvailableToFileUnder : null,
                ParentUnavailable = family.ParentUnavailable,
            };
        }

        if (planSecureCreate is not null && SecureRootInheritance.Inherits(table))
        {
            // G5 for the secure parents themselves: AppendTo AS THE CALLER on each one the row will be filed under - asked by
            // the plan before it says anything about them. Every record a typed lookup or the pair names was already
            // checked above, secure or not (so this refuses nothing the check above let through: it is the plan's own
            // guard, kept for any parent the plan names that the payload did not). A parent counts as checked only when a
            // lookup the check above ASKED about names the same record — the same TABLE and id: a contact whose id was
            // chosen equal to a matter's says nothing about that matter. A parent the pair names is asked again here.
            var checkedRecords = item.Lookups
                .Where(l => serverSetLookupColumns?.Contains(l.Column) != true)
                .Select(l => (Table: l.RelatedTable.ToLowerInvariant(), l.RecordId))
                .ToHashSet();
            var plan = await planSecureCreate.PlanCreateAsync(table, WritesOf(item), me.SystemUserId, ct,
                async (parents, token) =>
                {
                    var parentLookups = parents
                        .Where(p => !checkedRecords.Contains((p.Table.ToLowerInvariant(), p.Id)))
                        .Select(p => new DataverseWriteItemMapper.MappedLookup(
                            "filed under", p.Table, SecureDesignationRemoval.EntitySetFor(p.Table), p.Id))
                        .ToArray();
                    var appendTo = await CheckCallerMayAppendToAsync(user, me.SystemUserId, parentLookups, token).ConfigureAwait(false);
                    return appendTo.Succeeded
                        ? null
                        : RecordOwnerResolution.Refused(DataverseUserClientErrorCodes.AccessDenied,
                            appendTo.Denied ?? "you do not have permission to file this record under the secure record it names");
                }).ConfigureAwait(false);

            if (plan.Refusal is { } refusal)
                return new Outcome { PlanRefusal = refusal };

            if (plan is { Isolated: true, SecureOwnerTeamId: { } secureTeam })
            {
                var created = await CreateIntoIsolationAsync(appOnly, table, item, secureTeam, me.SystemUserId, ct).ConfigureAwait(false);
                return created with { Isolated = plan, IsolatedFor = me.SystemUserId };
            }
        }

        // Task 146 c1-r1 (owner round 13 item 9): the caller asked for this row, so the app-only create records them as its
        // creator person — F3's "or the creator" branch can then admit them, as it admits a run-as-user row's createdby.
        var owner = await ownership.ResolveOwnerAsync(
            OwnershipOf(table, item, callerObjectId, me.SystemUserId) with
            {
                RequestedBy = RecordRequester.Of(me.SystemUserId),
            },
            ct).ConfigureAwait(false);
        if (!owner.IsOwned)
            return new Outcome { OwnerRefusal = owner.IsRefused ? owner : RecordOwnerResolution.Refused(RecordOwnerRefusal.NoOwnerSource, owner.Reason ?? "no owner was resolved") };

        // The Secure team may own only the CHILD tables of the ownership set (each in the codified Secure Record Owner role
        // set, config/secure-record-owner-role.json). A work assignment or project filed under a secure record is created
        // into isolation above (task 158 r1, owner round 31 item 2) — reaching here with a Secure owner means the plan
        // found no secure parent while the resolver did: the two cannot be reconciled now, so nothing is created (fail
        // closed). Any other table would be refused by Dataverse (no Read).
        if (owner.IsSecureOwner && SecureRootInheritance.Inherits(table))
        {
            return new Outcome
            {
                SecureFilingRefused = $"Whether this '{table}' is filed under a secure record could not be decided consistently " +
                                      "(the records it names changed while it was being created). It was NOT created. Try again.",
            };
        }
        else if (owner.IsSecureOwner && !RecordOwnershipResolver.IsReparentableChild(table))
        {
            return new Outcome
            {
                SecureFilingRefused = RecordOwnershipResolver.IsOwnershipParent(table)
                    ? $"A '{table}' record cannot be created under a secure record from chat: it would itself have to be "
                      + "made secure (its own owner team, container and sharing — the record's Make Secure path). It was "
                      + "NOT created. Create it from the secure record itself."
                    : $"A '{table}' record cannot be filed under a secure record from chat, and was NOT created. Create it "
                      + "from the record itself.",
            };
        }

        var fields = BodyFields(item.JsonBody);
        fields["ownerid@odata.bind"] = $"/teams({owner.OwningTeamId!.Value})";
        owner.StampCreatorOn(fields, table);

        var id = Guid.NewGuid();
        await appOnly.UpdateRecordFieldsAsync(table, id, fields, ct).ConfigureAwait(false);
        return new Outcome { CreatedId = id, WrittenColumns = item.Columns };
    }

    /// <summary>
    /// Task 158 r1 (owner round 31 item 2): the app-only create of a work assignment or project — a ROOT — INTO isolation:
    /// owned by the named Secure Record Owners team provisioning's own topology names (resolved by
    /// <see cref="SecureRootInheritance.PlanCreateAsync"/>), flagged in the create itself. A root's own ownership is
    /// provisioning's (task 144), never a child's resolver answer; provisioning's re-entry steps then complete it.
    /// </summary>
    private static async Task<Outcome> CreateIntoIsolationAsync(
        IFieldMappingDataverseService appOnly, string table, DataverseWriteItemMapper.MappedItem item, Guid secureTeam,
        Guid creatorPerson, CancellationToken ct)
    {
        var fields = BodyFields(item.JsonBody);
        fields["ownerid@odata.bind"] = $"/teams({secureTeam})";
        fields["sprk_issecure"] = true;
        // Batch-4 integration (146 c1-r1 x 158): the person who asked for it, stamped in the create itself as on every other
        // app-only create (task 146 moved the stamp off the handlers into this writer), so provisioning's completion and any
        // later resume share to that person.
        if (RecordCreatorPerson.IsStamped(table))
            RecordCreatorPerson.Bind(fields, creatorPerson);

        var id = Guid.NewGuid();
        await appOnly.UpdateRecordFieldsAsync(table, id, fields, ct).ConfigureAwait(false);
        return new Outcome { CreatedId = id, WrittenColumns = item.Columns };
    }

    /// <summary>
    /// Task 037 (FR-36): <paramref name="item"/> with the table's copied defaults
    /// (<see cref="Sprk.Bff.Api.Services.RecordCreation.RecordTableRules.CopiedDefaultsFor"/>) filled — each lookup the row
    /// binds in the source column, bound as-is in the target the row leaves unset, mapped as the caller (navigation property
    /// from metadata, as every other lookup) and merged in. A target the row sets (an explicit null included) is never
    /// touched. A source the row leaves unset leaves the target blank, with a structured warning. A default the table's
    /// metadata cannot bind is left out with a warning (the create goes on); the caller's own failure reading that
    /// metadata is returned, and nothing is written. A table with no copied default costs nothing.
    /// </summary>
    /// <returns>The item to check and write, each (column, source column) pair it filled, or the caller's own failure.</returns>
    private static async Task<(DataverseWriteItemMapper.MappedItem Item, IReadOnlyList<(string Column, string Source)> Copied,
        DataverseUserResponse? ClientFailure)> WithCopiedDefaultsAsync(
        IDataverseUserClient user, string table, DataverseWriteItemMapper.MappedItem item, ILogger? logger, CancellationToken ct)
    {
        var fills = Sprk.Bff.Api.Services.RecordCreation.RecordTableRules.CopiedDefaultsFor(
            Sprk.Bff.Api.Services.RecordCreation.RecordTableRules.Find(table), item);
        if (fills.Count == 0)
            return (item, [], null);

        var extra = new Dictionary<string, object?>(StringComparer.Ordinal);
        var copied = new List<(string Column, string Source)>();
        foreach (var fill in fills)
        {
            if (fill.Source is { } source)
            {
                extra[fill.Default.Column] = new { relatedTable = source.RelatedTable, recordId = source.RecordId };
                copied.Add((fill.Default.Column, source.Column));
                continue;
            }

            logger?.LogWarning(
                "copied_default_source_unset: entity={Entity} column={Column} source={Source} — the create leaves both unset, " +
                "so the column stays blank (task 037, FR-36)",
                table, fill.Default.Column, fill.Default.CopiedFrom);
        }

        if (extra.Count == 0)
            return (item, [], null);

        var mapped = await DataverseWriteItemMapper.MapAsync(user, table, JsonSerializer.SerializeToElement(extra), ct)
            .ConfigureAwait(false);
        if (mapped.ClientFailure is { } mapFailure)
            return (item, [], mapFailure);
        if (mapped.Item is not { } added)
        {
            logger?.LogWarning(
                "copied_default_not_bound: entity={Entity} columns={Columns} — the table's metadata cannot bind them, so they " +
                "are left blank: {Reason} (task 037, FR-36)",
                table, string.Join(",", extra.Keys), mapped.ValidationError);
            return (item, [], null);
        }

        return (Merge(item, added), copied, null);
    }

    // ── The ADR-024 regarding pair (sprk_regardingrecordid + sprk_regardingrecordtype) ─────────────────────────────

    /// <summary>The record-type catalog's entity set (the pair's type lookup target, <c>sprk_recordtype_ref</c>).</summary>
    internal const string RecordTypeEntitySet = "sprk_recordtype_refs";

    /// <summary>The catalog column that names the table a record type is (ADR-024).</summary>
    internal const string RecordTypeLogicalNameColumn = "sprk_recordlogicalname";

    /// <summary>What a mapped write says about the pair: whether it sets each half, and to what.</summary>
    internal readonly record struct PairWrite(
        bool SetsId, string? IdText, bool InvalidId, bool SetsType, Guid? TypeId, string? TypeEntitySet)
    {
        public bool SetsEither => SetsId || SetsType;
    }

    /// <summary>The pair columns <paramref name="item"/> writes. A non-string pair id is read as invalid, never as absent.</summary>
    internal static PairWrite PairOf(DataverseWriteItemMapper.MappedItem item)
    {
        bool setsId = false, invalidId = false;
        string? idText = null;
        using (var body = JsonDocument.Parse(item.JsonBody))
        {
            foreach (var property in body.RootElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, SecureRootInheritance.PairIdColumn, StringComparison.OrdinalIgnoreCase))
                    continue;
                setsId = true;
                switch (property.Value.ValueKind)
                {
                    case JsonValueKind.Null:
                        idText = null;
                        break;
                    case JsonValueKind.String:
                        idText = property.Value.GetString();
                        break;
                    default:
                        invalidId = true;
                        break;
                }
            }
        }

        var type = item.Lookups.FirstOrDefault(l => IsPairType(l.Column));
        var setsType = type is not null || item.ClearedColumns.Any(IsPairType);
        return new PairWrite(setsId, idText, invalidId, setsType, type?.RecordId, type?.RelatedEntitySet);
    }

    internal static bool IsPairType(string column) =>
        string.Equals(column, SecureRootInheritance.PairTypeColumn, StringComparison.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> NoColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> PairColumns =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SecureRootInheritance.PairIdColumn, SecureRootInheritance.PairTypeColumn };

    /// <summary>#1616: the pair columns of <paramref name="table"/> when its pair is BFF-managed (field-secured so that only
    /// this server writes it — <see cref="SecureRootInheritance.BffManagedPairTables"/>); empty for every other table.</summary>
    internal static IReadOnlySet<string> BffManagedPairColumnsOf(string table) =>
        SecureRootInheritance.PairNamesAParentOnItsOwn(table) ? PairColumns : NoColumns;

    /// <summary>
    /// #1616: the update tool's write of a row whose ADR-024 pair is BFF-managed (<see cref="BffManagedPairColumnsOf"/>): the
    /// pair columns are field-secured so that no user can set them through the Web API, so the caller's own PATCH of them
    /// would be refused. The pair rule has already been asked AS THE CALLER (<see cref="CheckCallerMayFileRootUnderAsync"/>:
    /// AppendTo on the record the pair will name); this asks, as the caller too, what a run-as-user PATCH of the pair would
    /// otherwise have had Dataverse check — Write on the row, and Append on it plus AppendTo on the record-type catalog row
    /// when the type is set — then sends the caller's own PATCH of every OTHER column (Dataverse authorizes it, field
    /// security included), and last the application writes the pair (update-only, never a create).
    /// </summary>
    /// <returns><c>null</c> when <paramref name="item"/> writes no BFF-managed pair column (the caller's own PATCH, as
    /// before); otherwise <see cref="RefileOutcome.Written"/> or the refusal. A refusal before the caller's PATCH writes nothing;
    /// a fault of the application's write after it propagates (the caller's other columns are then written).</returns>
    internal static async Task<RefileOutcome?> PatchWithBffManagedPairAsync(
        IDataverseUserClient user, IFieldMappingDataverseService? appOnly, string table, string entitySetName, Guid recordId,
        DataverseWriteItemMapper.MappedItem item, CancellationToken ct)
    {
        var managed = BffManagedPairColumnsOf(table);
        if (managed.Count == 0)
            return null;

        var pairFields = new Dictionary<string, object?>(StringComparer.Ordinal);
        string? callerBody = null;
        using (var body = JsonDocument.Parse(item.JsonBody))
        {
            using var stream = new MemoryStream();
            var rest = 0;
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var property in body.RootElement.EnumerateObject())
                {
                    if (managed.Contains(SecureRootInheritance.NormalizeColumn(property.Name)))
                    {
                        pairFields[property.Name] = property.Value.Clone();
                        continue;
                    }

                    property.WriteTo(writer);
                    rest++;
                }

                writer.WriteEndObject();
            }

            if (rest > 0)
                callerBody = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        if (pairFields.Count == 0)
            return null; // no pair column in the body: the caller's own PATCH, as before

        if (appOnly is null)
        {
            return new RefileOutcome
            {
                ClientFailure = DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.ServiceError,
                    $"What this {table} is filed under is written only by Spaarke, which cannot write it here, so nothing was written."),
            };
        }

        var me = await WhoAmIAsync(user, ct).ConfigureAwait(false);
        if (me.Failure is { } failure)
            return new RefileOutcome { ClientFailure = failure };

        var typeLookups = item.Lookups.Where(l => IsPairType(l.Column)).ToArray();
        var needed = typeLookups.Length > 0 ? AccessRights.Write | AccessRights.Append : AccessRights.Write;
        var rights = await RightsOnAsync(user, me.SystemUserId, entitySetName, recordId, ct).ConfigureAwait(false);
        if ((rights & needed) != needed)
            return new RefileOutcome { Denied = $"You do not have permission to change what this {table} is filed under." };

        var typeAppendTo = await CheckCallerMayAppendToAsync(user, me.SystemUserId, typeLookups, ct).ConfigureAwait(false);
        if (typeAppendTo.ClientFailure is { } typeFailure)
            return new RefileOutcome { ClientFailure = typeFailure };
        if (!typeAppendTo.Succeeded)
        {
            return new RefileOutcome
            {
                Denied = typeAppendTo.Denied ?? "You do not have permission to file this record under the record type it names.",
                ParentUnavailable = typeAppendTo.ParentUnavailable,
            };
        }

        // The FULL pair the rule checked: the update tool completes a half-pair write from the row BEFORE anything is
        // checked (KeepPairInStepAsync), so the item carries both halves, the rule (CheckCallerMayRefileByPairAsync) and the
        // gate decided exactly them, and exactly them is what the application writes. An item that still sets one half was
        // not prepared that way: refused (fail closed), never completed from a second read.
        if (PairOf(item) is not { SetsId: true, SetsType: true })
        {
            return new RefileOutcome
            {
                Invalid = $"What this {table} is filed under is written as a whole (its record and its record type), so nothing was written.",
            };
        }

        if (callerBody is not null)
        {
            // If-Match: * (update-only), AS THE CALLER — every column but the pair, authorized by Dataverse itself.
            var response = await user.PatchAsync($"{entitySetName}({recordId:D})", callerBody, ct).ConfigureAwait(false);
            if (!response.IsSuccess)
                return new RefileOutcome { ClientFailure = response };
        }

        await appOnly.UpdateExistingRecordFieldsAsync(table, recordId, pairFields, ct).ConfigureAwait(false);
        return new RefileOutcome { Written = true };
    }

    /// <summary>
    /// #1616: prepares an update's ADR-024 pair BEFORE anything is checked or written, AS THE CALLER (one read of the row,
    /// only when it applies), so the checks and the write see one and the same pair:
    /// <list type="bullet">
    /// <item><b>A BFF-managed pair</b> (a project, <see cref="BffManagedPairColumnsOf"/>) written by one half: the other half is
    /// added as the row holds it, so the pair rule, the secure-root gate and the application's write
    /// (<see cref="PatchWithBffManagedPairAsync"/>) all act on exactly the same (record, type).</item>
    /// <item><b>A work assignment</b> (its pair is not BFF-managed) whose update changes or clears a typed filing lookup
    /// (<see cref="SecureRootInheritance.TypedLookupsOf"/>: a matter or project, or #1630 an invoice or event) the pair was IN
    /// STEP with — same id, a type naming that
    /// lookup's table — and does not write the pair itself: the pair is kept in step in the same caller PATCH (moved to the
    /// lookup's new record, or cleared with its display columns when the lookup is cleared). Otherwise a BFF re-file would
    /// leave a pair no typed lookup repeats, which holds the record's sharees and every loosening until a repair. The pair
    /// rule then asks AppendTo on the new record exactly as for any pair write. A pair that was not in step is left alone.</item>
    /// </list>
    /// </summary>
    /// <returns>The item to check and write (unchanged when nothing applies), or the caller's own failure — nothing written.</returns>
    internal static async Task<(DataverseWriteItemMapper.MappedItem Item, RefileOutcome? Refusal)> KeepPairInStepAsync(
        IDataverseUserClient user, string table, string entitySetName, Guid recordId, DataverseWriteItemMapper.MappedItem item,
        CancellationToken ct)
    {
        var pair = PairOf(item);
        var managed = BffManagedPairColumnsOf(table).Count > 0;
        // #1630: the intermediate lookups too (an invoice or event files a work assignment as its matter or project lookup
        // does), so a re-file from one invoice to another moves the pair with it.
        var filing = SecureRootInheritance.TypedLookupsOf(table.Trim().ToLowerInvariant())
            .Select(t => (Column: t.Column, ParentTable: t.Table))
            .ToArray();
        var changedFiling = filing
            .Where(f => item.Lookups.Any(l => string.Equals(l.Column, f.Column, StringComparison.OrdinalIgnoreCase))
                        || item.ClearedColumns.Contains(f.Column, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var completes = managed && pair.SetsEither && !(pair.SetsId && pair.SetsType);
        var follows = !managed && SecureRootInheritance.Inherits(table) && !pair.SetsEither && changedFiling.Length > 0;
        if (!completes && !follows)
            return (item, null);

        var row = await user.GetAsync($"{entitySetName}({recordId:D})", ct).ConfigureAwait(false);
        if (!row.IsSuccess)
            return (item, new RefileOutcome { ClientFailure = row });
        if (row.Body is not { ValueKind: JsonValueKind.Object } body)
        {
            return (item, new RefileOutcome
            {
                ClientFailure = DataverseUserResponse.Fail(row.StatusCode, DataverseUserClientErrorCodes.BadRequest,
                    "The record could not be read, so what it is filed under cannot be checked."),
            });
        }

        var rowId = body.TryGetProperty(SecureRootInheritance.PairIdColumn, out var idValue) && idValue.ValueKind == JsonValueKind.String
            ? idValue.GetString()
            : null;
        Guid? rowType = body.TryGetProperty($"_{SecureRootInheritance.PairTypeColumn}_value", out var typeValue)
                        && typeValue.ValueKind == JsonValueKind.String && Guid.TryParse(typeValue.GetString(), out var parsedType)
            ? parsedType
            : null;

        var extra = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (completes)
        {
            if (!pair.SetsId)
                extra[SecureRootInheritance.PairIdColumn] = rowId;
            if (!pair.SetsType)
                extra[SecureRootInheritance.PairTypeColumn] = rowType is { } t ? new { relatedTable = RecordTypeTable, recordId = t } : null;
        }
        else
        {
            if (!Guid.TryParse(rowId?.Trim(), out var pairId) || pairId == Guid.Empty || rowType is not { } pairType)
                return (item, null); // no typed pair to keep in step

            // The typed lookup the pair was in step with: same id on the row, and the pair's type names its table.
            (string Column, string ParentTable)? inStep = null;
            foreach (var f in changedFiling)
            {
                if (body.TryGetProperty($"_{f.Column}_value", out var current) && current.ValueKind == JsonValueKind.String
                    && Guid.TryParse(current.GetString(), out var currentId) && currentId == pairId)
                {
                    inStep = f;
                    break;
                }
            }

            if (inStep is not { } step)
                return (item, null);

            var typeRow = await user.GetAsync($"{RecordTypeEntitySet}({pairType:D})?$select={RecordTypeLogicalNameColumn}", ct)
                .ConfigureAwait(false);
            if (!typeRow.IsSuccess)
                return (item, new RefileOutcome { ClientFailure = typeRow });
            var pairTable = typeRow.Body is { } typeBody && typeBody.TryGetProperty(RecordTypeLogicalNameColumn, out var logical)
                                                         && logical.ValueKind == JsonValueKind.String
                ? logical.GetString()?.Trim()
                : null;
            if (!string.Equals(pairTable, step.ParentTable, StringComparison.OrdinalIgnoreCase))
                return (item, null);

            var moved = item.Lookups.FirstOrDefault(l => string.Equals(l.Column, step.Column, StringComparison.OrdinalIgnoreCase));
            if (moved is not null)
            {
                extra[SecureRootInheritance.PairIdColumn] = moved.RecordId.ToString("D");
                extra[SecureRootInheritance.PairTypeColumn] = new { relatedTable = RecordTypeTable, recordId = pairType };
            }
            else
            {
                extra[SecureRootInheritance.PairIdColumn] = null;
                extra[SecureRootInheritance.PairTypeColumn] = null;
                foreach (var display in PairDisplayColumns)
                    extra[display] = null;
            }
        }

        var mapped = await DataverseWriteItemMapper.MapAsync(user, table, JsonSerializer.SerializeToElement(extra), ct)
            .ConfigureAwait(false);
        if (mapped.ClientFailure is { } mapFailure)
            return (item, new RefileOutcome { ClientFailure = mapFailure });
        if (mapped.Item is not { } added)
            return (item, new RefileOutcome { Invalid = mapped.ValidationError ?? $"What this {table} is filed under could not be written." });

        return (Merge(item, added), null);
    }

    /// <summary>The pair's display columns (a work assignment's; cleared with the pair).</summary>
    private static readonly string[] PairDisplayColumns = ["sprk_regardingrecordname", "sprk_regardingrecordnumber", "sprk_regardingrecordurl"];

    /// <summary>One mapped item carrying <paramref name="item"/>'s writes and <paramref name="added"/>'s (which win).</summary>
    private static DataverseWriteItemMapper.MappedItem Merge(DataverseWriteItemMapper.MappedItem item, DataverseWriteItemMapper.MappedItem added)
    {
        var body = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var json in new[] { item.JsonBody, added.JsonBody })
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var property in doc.RootElement.EnumerateObject())
                body[property.Name] = property.Value.Clone();
        }

        return new DataverseWriteItemMapper.MappedItem(
            JsonSerializer.Serialize(body),
            item.Columns.Concat(added.Columns).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
        {
            Lookups = item.Lookups.Concat(added.Lookups).ToArray(),
            ClearedColumns = item.ClearedColumns.Concat(added.ClearedColumns).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
        };
    }

    private const string RecordTypeTable = "sprk_recordtype_ref";

    /// <summary>
    /// The columns of a <paramref name="table"/> row whose lookup the pair's readers match the pair's ID against (each
    /// with the table it names): the work assignment's typed filing columns (<c>SecureRootInheritance.NameParentsAsync</c>),
    /// the child's followed, intermediate, carrier and party links (<c>RecordContainerResolver</c>, rule 3), and its stamp
    /// sources and root links (<c>CoreAncestorResolver.ClassifyStampSource</c>). An untyped pair whose id equals none of
    /// them names nothing its readers can tell. Empty for a table no reader reads the pair of.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> PairAgreementColumns(string table)
    {
        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(IEnumerable<(string Column, string Target)> links)
        {
            foreach (var (column, target) in links)
            {
                if (!IsPairType(column))
                    columns.TryAdd(column, target);
            }
        }

        var key = table.Trim().ToLowerInvariant();
        if (SecureRootInheritance.TypedFilingColumns.TryGetValue(key, out var filing))
            Add(filing);
        if (RecordContainerResolver.ChildAncestorLinks.For(key) is { } links)
        {
            Add(links.FollowedLinks.Select(l => (l.LinkColumn, l.Target)));
            Add(links.IntermediateColumns);
            Add(links.CarrierColumns);
        }
        if (CoreAncestorResolver.PartyRegardingColumns.TryGetValue(key, out var parties))
            Add(parties);
        if (CoreAncestorResolver.StampSourceColumns.TryGetValue(key, out var sources))
        {
            Add(sources);
            Add(CoreAncestorResolver.CoreAncestorLookups.Select(l => (l.LookupAttribute, l.EntityType)));
        }

        return columns;
    }

    /// <summary>
    /// AS THE CALLER, the pair rule (<see cref="PairTargetAsync"/>) for an UPDATE of an existing row — the re-file core
    /// (<see cref="RefileAsync"/>) and the update tool, which asks it before its secure-root gate. The pair after the write is
    /// what the update sets, completed from the row as the caller reads it (one read, only when the update sets a pair
    /// column): the half the update leaves (a re-typed pair names another record), and the lookups in the row's agreement
    /// columns it keeps (an untyped pair id moved onto one of them names that record — which is then asked too). Whatever
    /// record the pair is left naming costs AppendTo here, before any app-only read. <c>null</c>: the update sets no pair
    /// column, or the pair it leaves names nothing or a record the caller may append to.
    /// </summary>
    internal static async Task<RefileOutcome?> CheckCallerMayRefileByPairAsync(
        IDataverseUserClient user, Guid? me, string table, string entitySetName, Guid recordId,
        DataverseWriteItemMapper.MappedItem item, CancellationToken ct)
    {
        var pair = PairOf(item);
        if (!pair.SetsEither)
            return null;
        if (pair.InvalidId)
            return new RefileOutcome { Invalid = $"'{SecureRootInheritance.PairIdColumn}' must hold the id of the record this row is filed under." };

        // The row as the caller reads it: the pair half the update leaves, and the regarding lookups it keeps.
        var row = await user.GetAsync($"{entitySetName}({recordId:D})", ct).ConfigureAwait(false);
        if (!row.IsSuccess)
            return new RefileOutcome { ClientFailure = row };

        var (idText, typeId, typeSet) = (pair.IdText, pair.TypeId, pair.TypeEntitySet);
        var typeValue = $"_{SecureRootInheritance.PairTypeColumn}_value";
        var kept = new List<(string Column, Guid Id)>();
        if (row.Body is { ValueKind: JsonValueKind.Object } body)
        {
            if (!pair.SetsId)
            {
                idText = body.TryGetProperty(SecureRootInheritance.PairIdColumn, out var id) && id.ValueKind == JsonValueKind.String
                    ? id.GetString()
                    : null;
            }

            if (!pair.SetsType)
            {
                typeId = body.TryGetProperty(typeValue, out var t) && t.ValueKind == JsonValueKind.String
                         && Guid.TryParse(t.GetString(), out var parsed)
                    ? parsed
                    : null;
                typeSet = null;
            }

            // _{column}_value for each agreement column: a lookup the row keeps (unless this update clears or rebinds it).
            var changed = new HashSet<string>(item.ClearedColumns.Concat(item.Lookups.Select(l => l.Column)), StringComparer.OrdinalIgnoreCase);
            foreach (var column in PairAgreementColumns(table).Keys)
            {
                if (changed.Contains(column)
                    || !body.TryGetProperty($"_{column}_value", out var value)
                    || value.ValueKind != JsonValueKind.String
                    || !Guid.TryParse(value.GetString(), out var keptId))
                {
                    continue;
                }

                kept.Add((column, keptId));
            }
        }
        else if (!pair.SetsId || !pair.SetsType)
        {
            return new RefileOutcome { ClientFailure = DataverseUserResponse.Fail(row.StatusCode, DataverseUserClientErrorCodes.BadRequest,
                "The record could not be read, so what it is filed under cannot be checked.") };
        }

        var target = await PairTargetAsync(user, table, idText, typeId, typeSet, item.Lookups, ct, kept).ConfigureAwait(false);
        if (target.Refusal is { } refusal)
        {
            return new RefileOutcome
            {
                Invalid = refusal.Invalid,
                Denied = refusal.Denied,
                ParentUnavailable = refusal.ParentUnavailable,
                ClientFailure = refusal.ClientFailure,
            };
        }

        if (target.Lookup is not { } named)
            return null;

        var caller = me;
        if (caller is null)
        {
            var whoAmI = await WhoAmIAsync(user, ct).ConfigureAwait(false);
            if (whoAmI.Failure is { } failure)
                return new RefileOutcome { ClientFailure = failure };
            caller = whoAmI.SystemUserId;
        }

        var appendTo = await CheckCallerMayAppendToAsync(user, caller.Value, new[] { named }, ct).ConfigureAwait(false);
        return appendTo.Denied is { } denied
            ? new RefileOutcome { Denied = denied, ParentUnavailable = appendTo.ParentUnavailable }
            : null;
    }

    /// <summary>
    /// #1630: the update tool's AS-THE-CALLER check handed to the secure-root gate (<see cref="SecureRootFilingGate.CheckAsync"/>)
    /// for the secure matters and projects a re-file reaches only THROUGH an invoice or event it sets — records no lookup of the
    /// write names, so <see cref="CheckCallerMayFileRootUnderAsync"/> asked only about the invoice or event. AppendTo on each,
    /// as the create plan asks of every secure parent (create parity): a caller with AppendTo on an invoice but not on the
    /// secure matter it belongs to cannot file a work assignment under that matter by way of the invoice.
    /// </summary>
    internal static Func<IReadOnlyList<SecureFilingParent>, CancellationToken, Task<RecordOwnerResolution?>> CallerMayFileRootUnderSecure(
        IDataverseUserClient user) =>
        async (parents, ct) =>
        {
            var me = await WhoAmIAsync(user, ct).ConfigureAwait(false);
            if (me.Failure is { } failure)
            {
                return RecordOwnerResolution.Refused(failure.ErrorCode ?? DataverseUserClientErrorCodes.UserContextRequired,
                    "you could not be identified, so whether you may file this record under the secure record it would belong to " +
                    "could not be checked");
            }

            var lookups = parents
                .Select(p => new DataverseWriteItemMapper.MappedLookup(
                    "filed under", p.Table, SecureDesignationRemoval.EntitySetFor(p.Table), p.Id))
                .ToArray();
            var appendTo = await CheckCallerMayAppendToAsync(user, me.SystemUserId, lookups, ct).ConfigureAwait(false);
            return appendTo.Succeeded
                ? null
                : RecordOwnerResolution.Refused(DataverseUserClientErrorCodes.AccessDenied,
                    appendTo.Denied ?? "you do not have permission to file this record under the secure record it would belong to");
        };

    // ── Re-file (task 146 r2; shared since task 147 r1) ─────────────────────────────────────────────────────────────

    /// <summary>What a re-file decided (task 147 r1: one outcome shape for the update tool and the HTTP routes).</summary>
    internal sealed record RefileOutcome
    {
        /// <summary>The update changes no lookup to an ownership parent: an ordinary update — nothing was written.</summary>
        public bool NotARefile { get; init; }

        /// <summary>The caller's PATCH ran inside the re-file and the owner is decided and applied.</summary>
        public bool Written { get; init; }

        /// <summary>
        /// #2091: <see cref="Written"/>, but the reparent found it moved nothing the row is filed under
        /// (<see cref="RecordOwnerResolution.ParentsUnchanged"/>: every lookup it sets already names that record in that column,
        /// and every column it clears held no parent) — no re-file, so a writer runs no step after one.
        /// </summary>
        public bool ParentsUnchanged { get; init; }

        /// <summary>
        /// <see cref="Written"/>: the reparent's answer, which the step after the re-file takes
        /// (<c>SecureChildReconciler.AfterRefileAsync</c>; #2159 slice 2 — its F3 permission, <see cref="RecordOwnerResolution.SecureExitPermitted"/>).
        /// </summary>
        public RecordOwnerResolution? Resolution { get; init; }

        /// <summary>The caller's own rights refused it (no AppendTo on a record named) — nothing written.</summary>
        public string? Denied { get; init; }

        /// <summary>The denial is about a record the row is moved under (uniform not-found on HTTP).</summary>
        public bool ParentUnavailable { get; init; }

        /// <summary>F3 (owner round 10 item 7): the caller may not move the row out of a secure record — nothing written.</summary>
        public RecordOwnerResolution? Forbidden { get; init; }

        /// <summary>The owner could not be decided — nothing written.</summary>
        public RecordOwnerResolution? OwnerRefusal { get; init; }

        /// <summary>A non-child table moved under a SECURE record (it cannot be re-owned here) — nothing written.</summary>
        public RecordOwnerResolution? SecureFilingRefused { get; init; }

        /// <summary>A question or the PATCH, asked as the caller, failed at Dataverse — the caller's own error.</summary>
        public DataverseUserResponse? ClientFailure { get; init; }

        /// <summary>The ADR-024 pair the update leaves disagrees with itself (<see cref="Outcome.Invalid"/>) — nothing written.</summary>
        public string? Invalid { get; init; }

        /// <summary>
        /// ebilling-r1 FR-05 (ISS-010): the invoice-family guard refused the row as the update would leave it (the stored
        /// lookups overlaid with the change) — its stable code. Nothing written.
        /// </summary>
        public InvoiceFamilyCoreRecord? InvoiceFamilyRefusal { get; init; }

        /// <summary>
        /// #2143 round 7 (F1): with <see cref="Written"/>, the person the re-file's F3 check acted for — what the writer hands the
        /// step after the re-file (<see cref="Sprk.Bff.Api.Services.Access.SecureChildReconciler.AfterRefileAsync(string, Guid, Func{Task{bool}}, Sprk.Bff.Api.Services.Access.RefileThreadStep, Sprk.Bff.Api.Services.Access.SecureRemovalCaller?, CancellationToken)"/>),
        /// so the thread that follows the filing moves on the same rights.
        /// </summary>
        public Sprk.Bff.Api.Services.Access.SecureRemovalCaller? SecureExitCaller { get; init; }
    }

    /// <summary>The caller's PATCH was refused by Dataverse inside the re-file — carried out so no owner is assigned.</summary>
    private sealed class CallerWriteFailedException(DataverseUserResponse response) : Exception("The caller's update was refused.")
    {
        public DataverseUserResponse Response { get; } = response;
    }

    /// <summary>
    /// Task 146 r2 (verifier item 2), shared by the update tool and the browser re-file routes since task 147 r1 (owner
    /// round 28 item 1: "re-files go through the existing families" — ONE re-file core). For a CHILD table, an update that
    /// sets or clears a lookup to an ownership parent is a re-file: the caller must see the row and hold AppendTo on each
    /// record it is moved under (asked as the caller, so no refusal answers questions about records they cannot see);
    /// then <c>ReparentAsync</c> decides the owner, applies the caller's own PATCH (AS THE CALLER — Dataverse authorizes
    /// the change, field security included), assigns the owner separately and reads it back. F3 is asked AS THE CALLER
    /// before a move out of a secure record. A refusal writes nothing. A root's own ownership is provisioning's (owner S6),
    /// so a root's lookups re-own nothing; any other table moved under a SECURE record is refused. An update that sets the
    /// ADR-024 pair, on ANY table, costs AppendTo on the record the pair will name (<see cref="CheckCallerMayRefileByPairAsync"/>)
    /// — the same uniform not-found as a lookup's — whether or not the update is otherwise a re-file.
    /// </summary>
    internal static async Task<RefileOutcome> RefileAsync(
        IDataverseUserClient user,
        IRecordOwnershipResolver ownership,
        string table,
        Guid recordId,
        string entitySetName,
        string? primaryIdAttribute,
        DataverseWriteItemMapper.MappedItem item,
        Guid? callerObjectId,
        CancellationToken ct,
        IGenericEntityService? appOnlyEntities = null)
    {
        // ebilling-r1 FR-05 (ISS-010, #1973; unified-access-control-r2): BEFORE anything else — re-file or ordinary update —
        // an update that sets or clears an invoice-family row's guarded lookups is decided on the stored lookups overlaid
        // with the change (read as the caller, once). A refusal writes nothing. D-96: an invoice's lines are counted with
        // appOnlyEntities (the app); without it an invoice core-lookup change fails closed.
        if (await CheckInvoiceFamilyAsync(user, table, item, (entitySetName, recordId), ct, appOnlyEntities).ConfigureAwait(false) is { } family)
        {
            return new RefileOutcome
            {
                InvoiceFamilyRefusal = family.Refusal,
                ClientFailure = family.ClientFailure,
                Denied = family.ParentUnavailable ? NotAvailableToFileUnder : null,
                ParentUnavailable = family.ParentUnavailable,
            };
        }

        var isChild = RecordOwnershipResolver.IsReparentableChild(table);
        if (!isChild && RecordOwnershipResolver.IsOwnershipParent(table))
        {
            // A root: its own ownership is provisioning's, but the record its pair is moved under still costs AppendTo.
            return await CheckCallerMayRefileByPairAsync(user, null, table, entitySetName, recordId, item, ct).ConfigureAwait(false)
                   ?? new RefileOutcome { NotARefile = true };
        }

        var newParents = item.Lookups.Where(l => RecordOwnershipResolver.IsOwnershipParent(l.RelatedTable)).ToArray();
        var changes = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?>(StringComparer.OrdinalIgnoreCase);
        foreach (var lookup in newParents)
            changes[lookup.Column] = new Microsoft.Xrm.Sdk.EntityReference(lookup.RelatedTable, lookup.RecordId);

        if (isChild)
        {
            // A cleared column may be a cleared lookup — the row cannot tell a generic writer which (ReparentAsync reads it).
            foreach (var column in item.ClearedColumns)
                changes.TryAdd(column, null);

            // #2143 round 7c: a message's thread set by the update re-files it (RecordReparent.IsThreadFilingColumn), so a
            // thread-only update is decided by the reparent (F3 included), never sent as an ordinary PATCH.
            foreach (var lookup in item.Lookups.Where(l => RecordReparent.IsThreadFilingColumn(table, l.Column)))
                changes.TryAdd(lookup.Column, new Microsoft.Xrm.Sdk.EntityReference(lookup.RelatedTable, lookup.RecordId));
        }

        if (changes.Count == 0 || (!isChild && newParents.Length == 0))
        {
            // No ownership lookup changes — an ordinary update, unless it moves the ADR-024 pair, whose record costs AppendTo
            // exactly as a lookup's does (Dataverse checks only the lookups of the caller's own PATCH).
            return await CheckCallerMayRefileByPairAsync(user, null, table, entitySetName, recordId, item, ct).ConfigureAwait(false)
                   ?? new RefileOutcome { NotARefile = true };
        }

        var me = await WhoAmIAsync(user, ct).ConfigureAwait(false);
        if (me.Failure is { } whoAmIFailure)
            return new RefileOutcome { ClientFailure = whoAmIFailure };

        var appendTo = await CheckCallerMayAppendToAsync(user, me.SystemUserId, newParents, ct).ConfigureAwait(false);
        if (appendTo.Denied is { } denied)
            return new RefileOutcome { Denied = denied, ParentUnavailable = appendTo.ParentUnavailable };

        if (await CheckCallerMayRefileByPairAsync(user, me.SystemUserId, table, entitySetName, recordId, item, ct).ConfigureAwait(false)
            is { } pairRefusal)
        {
            return pairRefusal;
        }

        if (!isChild)
        {
            // UAC task 194b R3b-2: through the one builder, keyed by the columns the update writes.
            var owner = await ownership.ResolveOwnerAsync(
                RecordOwnershipContext.ForRow(
                    table,
                    newParents.Select(l => KeyValuePair.Create(l.Column, (RecordOwnershipParent?)new RecordOwnershipParent(l.RelatedTable, l.RecordId))),
                    PairIdOf(item.JsonBody), callerObjectId, me.SystemUserId),
                ct).ConfigureAwait(false);
            return owner.IsRefused || owner.IsSecureOwner
                ? new RefileOutcome { SecureFilingRefused = owner }
                : new RefileOutcome { NotARefile = true };
        }

        // #2110 (ontology K3): a lineage lookup the update sets that files the row under no ownership parent rides along, so the
        // reparent can tell whether this change moved it (RecordOwnerResolution.ParentsUnchanged). It decides no owner, and —
        // but for a message's thread (#2143 round 7c, above) — it made no update a re-file.
        foreach (var lookup in item.Lookups.Where(l => RecordReparent.LineageOnlyTargetOf(table, l.Column) is not null))
            changes.TryAdd(lookup.Column, new Microsoft.Xrm.Sdk.EntityReference(lookup.RelatedTable, lookup.RecordId));

        // The caller must see the row before anything is decided about it (their own 404/403 otherwise).
        var row = await user.GetAsync(
            $"{entitySetName}({recordId:D})?$select={primaryIdAttribute ?? table + "id"}", ct).ConfigureAwait(false);
        if (!row.IsSuccess)
            return new RefileOutcome { ClientFailure = row };

        // Owner round 10 item 7 (task 146 c1): a re-file that moves the row OUT of a secure root is an un-secure. F3 is asked AS
        // THE CALLER — WhoAmI above, RetrievePrincipalAccess under their own token — before anything is written.
        var secureExitCaller = new Sprk.Bff.Api.Services.Access.SecureRemovalCaller(
            _ => Task.FromResult<Guid?>(me.SystemUserId),
            (record, token) => RightsOnAsync(
                user,
                me.SystemUserId,
                Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.EntitySetFor(record.EntityLogicalName),
                record.RecordId,
                token));

        try
        {
            var resolution = await ownership.ReparentAsync(
                new RecordReparent
                {
                    EntityLogicalName = table,
                    RecordId = recordId,
                    ParentChanges = changes,
                    CallerObjectId = callerObjectId,
                    CallerSystemUserId = me.SystemUserId,
                    // E1: a communication filed under nothing keeps its creator.
                    WhenUnfiled = string.Equals(table, "sprk_communication", StringComparison.OrdinalIgnoreCase)
                        ? UnfiledOwnership.KeepCreator
                        : UnfiledOwnership.ActingUserTeam,
                    SecureExitCaller = secureExitCaller,
                },
                async token =>
                {
                    // PATCH carries If-Match: * (update-only), run AS THE CALLER — Dataverse authorizes the change itself.
                    var response = await user.PatchAsync($"{entitySetName}({recordId:D})", item.JsonBody, token)
                        .ConfigureAwait(false);
                    if (!response.IsSuccess)
                        throw new CallerWriteFailedException(response);
                },
                ct).ConfigureAwait(false);

            return resolution switch
            {
                { IsForbidden: true } => new RefileOutcome { Forbidden = resolution },
                { IsRefused: true } => new RefileOutcome { OwnerRefusal = resolution },
                _ => new RefileOutcome
                {
                    Written = true,
                    ParentsUnchanged = resolution.ParentsUnchanged,
                    SecureExitCaller = secureExitCaller,
                    Resolution = resolution,
                },
            };
        }
        catch (CallerWriteFailedException failed)
        {
            // Privilege-denied update surfaces the USER's own access error — never escalates; no owner was assigned.
            return new RefileOutcome { ClientFailure = failed.Response };
        }
    }

    // ── ebilling-r1 FR-05: the invoice-family guard, invoked by the write core (ISS-010 / #1973, unified-access-control-r2) ──

    /// <summary>
    /// The uniform not-found of a record a write names that the caller cannot read, or that does not exist — the one copy,
    /// <see cref="Sprk.Bff.Api.Services.RecordCreation.RecordCreationService.NotAvailableToFileUnder"/> (task 017).
    /// </summary>
    private const string NotAvailableToFileUnder = Sprk.Bff.Api.Services.RecordCreation.RecordCreationService.NotAvailableToFileUnder;

    /// <summary>
    /// What the invoice-family step refused: the guard's decision (its stable code), the caller's own Dataverse failure on a
    /// read the guard needed, or a record the row names that the caller cannot read (the uniform not-found).
    /// </summary>
    internal sealed record InvoiceFamilyStep(InvoiceFamilyCoreRecord? Refusal, DataverseUserResponse? ClientFailure, bool ParentUnavailable);

    /// <summary>The invoice line table, and the invoice table's entity set (the one table a line's invoice lookup targets).</summary>
    private const string LineTable = "sprk_billingevent";

    private const string InvoiceEntitySet = "sprk_invoices";

    /// <summary>
    /// ebilling-r1 FR-05 (ISS-010, #1973), as unified-access-control-r2 answered it: the write core — the create
    /// (<see cref="CreateAsync"/>) and the update / re-file (<see cref="RefileAsync"/>) — invokes
    /// <see cref="InvoiceFamilyCoreRecordGuard"/> for every write of an invoice-family table, keyed by table, so every route
    /// that writes one is guarded (the child-record routes and the chat create / update tools). The guard owns the rule;
    /// this only assembles the row as it will be and reads what the rule needs, AS THE CALLER.
    /// <list type="bullet">
    /// <item><b>Create</b> (<paramref name="stored"/> null): the payload's lookups are the row.</item>
    /// <item><b>Update</b>: only when the change sets or clears a guarded lookup (<see cref="InvoiceFamilyCoreRecordGuard.GuardedLookups"/>)
    /// — an update that leaves them as stored cannot change the answer. The stored lookups are read once, as the caller, and
    /// overlaid with the change (<see cref="EffectiveLookups"/>) BEFORE the guard runs.</item>
    /// <item><b>Supersedes, and a line's invoice</b> (task 026): the other invoice's core lookups are read as the caller; a
    /// row they cannot read (403) and one that does not exist (404) are the uniform not-found.</item>
    /// </list>
    /// Fail closed: a read that fails is the caller's own error and an answer that lacks a column the guard asked for is
    /// <see cref="InvoiceFamilyCoreRecordGuard.ReadFailedCode"/> — never "not set". A guarded column written as anything but
    /// a lookup bind or a clear is <see cref="InvoiceFamilyCoreRecordGuard.LookupNotACoreRecordCode"/>.
    /// </summary>
    /// <param name="appOnlyEntities">D-96: on an update that writes an invoice's core lookup, the APP-only reader its lines
    /// are counted with (<see cref="InvoiceFamilyCoreRecordGuard.LinesOfAnotherCoreRecord"/>); null then fails closed.</param>
    /// <returns><c>null</c> when the write may proceed (or the table is not in the family); otherwise the refusal.</returns>
    internal static async Task<InvoiceFamilyStep?> CheckInvoiceFamilyAsync(
        IDataverseUserClient user, string table, DataverseWriteItemMapper.MappedItem item,
        (string EntitySetName, Guid RecordId)? stored, CancellationToken ct, IGenericEntityService? appOnlyEntities = null)
    {
        var guarded = InvoiceFamilyCoreRecordGuard.GuardedLookups(table);
        if (guarded is null)
            return null;

        var guardedColumns = guarded.Select(g => g.Column).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var written = item.Columns.Concat(item.ClearedColumns).Concat(item.Lookups.Select(l => l.Column))
            .Where(guardedColumns.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (stored is not null && written.Count == 0)
            return null; // an update that leaves every guarded lookup as stored

        var notALookup = written.FirstOrDefault(c =>
            !item.Lookups.Any(l => string.Equals(l.Column, c, StringComparison.OrdinalIgnoreCase))
            && !item.ClearedColumns.Contains(c, StringComparer.OrdinalIgnoreCase));
        if (notALookup is not null)
        {
            return new InvoiceFamilyStep(InvoiceFamilyCoreRecord.Refused(table, InvoiceFamilyCoreRecordGuard.LookupNotACoreRecordCode,
                $"{table}.{notALookup} is a lookup; it is set by a bind or cleared, never written as a value. Nothing was written."),
                null, false);
        }

        IReadOnlyDictionary<string, Microsoft.Xrm.Sdk.EntityReference?> before =
            new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?>(StringComparer.OrdinalIgnoreCase);
        if (stored is { } row)
        {
            StoredLookups read;
            try
            {
                read = await ReadLookupsAsCallerAsync(user, row.EntitySetName, row.RecordId, guarded, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // A transport fault (a timeout included) is never "not set"; its own text stays out of the reason.
                read = new StoredLookups(null, null, ex.GetType().Name);
            }

            if (read.Failure is { } failure)
                return new InvoiceFamilyStep(null, failure, false);
            if (read.Values is not { } values)
            {
                return new InvoiceFamilyStep(InvoiceFamilyCoreRecord.Refused(table, InvoiceFamilyCoreRecordGuard.ReadFailedCode,
                    $"What this {table} is filed under could not be read ({read.Problem}); nothing was written."), null, false);
            }

            before = values;
        }

        var effective = EffectiveLookups(before, item);
        DataverseUserResponse? supersededFailure = null;
        var supersededUnavailable = false;
        var decided = await InvoiceFamilyCoreRecordGuard.CheckWriteAsync(
            table,
            column => effective.TryGetValue(column, out var value) ? value : null,
            async (invoice, token) =>
            {
                // The other invoice is the one an invoice supersedes or the one a line belongs to (task 026). The bind names
                // its entity set; one kept from the stored row is in the invoice set — for an invoice, the written table's.
                var entitySet = item.Lookups.FirstOrDefault(l =>
                                    (string.Equals(l.Column, InvoiceFamilyCoreRecordGuard.SupersedesInvoiceColumn, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(l.Column, InvoiceFamilyCoreRecordGuard.LineInvoiceColumn, StringComparison.OrdinalIgnoreCase))
                                    && l.RecordId == invoice.Id)?.RelatedEntitySet
                                ?? (stored is null ? null
                                    : string.Equals(table, LineTable, StringComparison.OrdinalIgnoreCase) ? InvoiceEntitySet
                                    : stored.Value.EntitySetName);
                if (entitySet is null)
                {
                    return InvoiceFamilyCoreRecord.Refused(invoice.LogicalName, InvoiceFamilyCoreRecordGuard.ReadFailedCode,
                        "The other invoice's table could not be told; nothing was written.");
                }

                var other = await ReadLookupsAsCallerAsync(
                        user, entitySet, invoice.Id,
                        InvoiceFamilyCoreRecordGuard.CoreRecordLookups[invoice.LogicalName].Select(l => (l.Column, l.CoreEntity)).ToArray(),
                        token)
                    .ConfigureAwait(false);
                if (other.Failure is { } failure)
                {
                    if (failure.StatusCode is 403 or 404)
                        supersededUnavailable = true;
                    else
                        supersededFailure = failure;
                    return InvoiceFamilyCoreRecord.Refused(invoice.LogicalName,
                        supersededUnavailable ? InvoiceFamilyCoreRecordGuard.RecordNotFoundCode : InvoiceFamilyCoreRecordGuard.ReadFailedCode,
                        "The other invoice could not be read; nothing was written.");
                }

                return other.Values is { } otherValues
                    ? InvoiceFamilyCoreRecordGuard.Check(invoice.LogicalName,
                        column => otherValues.TryGetValue(column, out var value) ? value : null)
                    : InvoiceFamilyCoreRecord.Refused(invoice.LogicalName, InvoiceFamilyCoreRecordGuard.ReadFailedCode,
                        $"The other invoice could not be read ({other.Problem}); nothing was written.");
            },
            ct,
            // D-96: an update that moves an invoice to another core record must not leave its lines behind (counted as the app).
            stored is { } invoiceRow && InvoiceFamilyCoreRecordGuard.WritesCoreLookup(table, written)
                ? InvoiceFamilyCoreRecordGuard.LinesOfAnotherCoreRecord(appOnlyEntities, invoiceRow.RecordId)
                : null,
            // UAC-r2 ruling (RecordFamilyEdges, 2026-10-28): nor be filed under a work assignment that is filed under it (as the app).
            stored is { } refiled && InvoiceFamilyCoreRecordGuard.WritesCoreLookup(table, written)
                ? InvoiceFamilyCoreRecordGuard.WorkAssignmentFiledUnderInvoice(appOnlyEntities, refiled.RecordId)
                : null).ConfigureAwait(false);

        if (supersededUnavailable)
            return new InvoiceFamilyStep(null, null, true);
        if (supersededFailure is { } readFailure)
            return new InvoiceFamilyStep(null, readFailure, false);
        return decided.IsRefused ? new InvoiceFamilyStep(decided, null, false) : null;
    }

    /// <summary>
    /// The lookups a row will hold after a write (unified-access-control-r2's merge for ISS-010): the stored row's lookups
    /// overlaid with the change — a bind replaces the column's value, a clear sets it to <c>null</c>, every other column
    /// keeps its stored value. Pure.
    /// </summary>
    internal static IReadOnlyDictionary<string, Microsoft.Xrm.Sdk.EntityReference?> EffectiveLookups(
        IReadOnlyDictionary<string, Microsoft.Xrm.Sdk.EntityReference?> stored, DataverseWriteItemMapper.MappedItem change)
    {
        var effective = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (column, value) in stored)
            effective[column] = value;
        foreach (var column in change.ClearedColumns)
            effective[column] = null;
        foreach (var lookup in change.Lookups)
            effective[lookup.Column] = new Microsoft.Xrm.Sdk.EntityReference(lookup.RelatedTable, lookup.RecordId);
        return effective;
    }

    /// <summary>What <see cref="ReadLookupsAsCallerAsync"/> read: each column's value, the caller's own failure, or why the
    /// answer could not be used.</summary>
    internal sealed record StoredLookups(
        IReadOnlyDictionary<string, Microsoft.Xrm.Sdk.EntityReference?>? Values, DataverseUserResponse? Failure, string? Problem);

    /// <summary>
    /// AS THE CALLER, one row's single-target lookups (<c>_{column}_value</c>, each a reference to its column's one table).
    /// Dataverse answers every <c>$select</c>ed column (null when unset), so a column missing from the answer, or a value that
    /// is not an id, makes the read unusable (<see cref="StoredLookups.Problem"/>) — never "not set".
    /// </summary>
    internal static async Task<StoredLookups> ReadLookupsAsCallerAsync(
        IDataverseUserClient user, string entitySetName, Guid recordId, IReadOnlyCollection<(string Column, string Table)> columns,
        CancellationToken ct)
    {
        var select = string.Join(",", columns.Select(c => $"_{c.Column}_value"));
        var response = await user.GetAsync($"{entitySetName}({recordId:D})?$select={select}", ct).ConfigureAwait(false);
        if (!response.IsSuccess)
            return new StoredLookups(null, response, null);
        if (response.Body is not { ValueKind: JsonValueKind.Object } body)
            return new StoredLookups(null, null, "the row came back empty");

        var values = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (column, target) in columns)
        {
            if (!body.TryGetProperty($"_{column}_value", out var value))
                return new StoredLookups(null, null, $"'{column}' was not returned");

            switch (value.ValueKind)
            {
                case JsonValueKind.Null:
                    values[column] = null;
                    break;
                case JsonValueKind.String when Guid.TryParse(value.GetString(), out var id):
                    values[column] = id == Guid.Empty ? null : new Microsoft.Xrm.Sdk.EntityReference(target, id);
                    break;
                default:
                    return new StoredLookups(null, null, $"'{column}' is not a record id");
            }
        }

        return new StoredLookups(values, null, null);
    }

    /// <summary>The mapped JSON body as the field dictionary the app-only PATCH serializes (values kept as JSON).</summary>
    internal static Dictionary<string, object?> BodyFields(string jsonBody)
    {
        using var document = JsonDocument.Parse(jsonBody);
        return document.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => (object?)p.Value.Clone(), StringComparer.Ordinal);
    }
}
