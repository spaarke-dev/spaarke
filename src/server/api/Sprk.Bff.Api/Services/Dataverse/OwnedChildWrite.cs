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
/// <para><b>The create half is gone (field-mapping-server-write-path-r1 task 017, 2d-5).</b> Every owned create — the
/// child-records route, the chat <c>dataverse.create_record</c> owned path, <c>email.draft</c>'s filed draft and the budget
/// inquiry — runs through <see cref="Sprk.Bff.Api.Services.RecordCreation.RecordCreationService"/>, which ported this file's
/// create (the as-the-caller checks, the resolver's owner, the isolated root create) step for step. What stays here is the
/// re-file half (<see cref="RefileAsync"/>, unified-access-control-r2's) and the rules both halves read (the write path,
/// the "for" columns, the regarding pair, the invoice-family step). The remarks below describe the G5 design those rules
/// implement.</para>
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
    /// (<see cref="Sprk.Bff.Api.Services.RecordCreation.ServerDefault.CopiedFrom"/>, applied by <see cref="Sprk.Bff.Api.Services.RecordCreation.RecordCreationService"/>).
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

    /// <summary>What a caller check (<see cref="CallerWriteAuthorization"/>) answered. Field-mapping-server-write-path-r1 task
    /// 017 (2d-5): the owned create that also answered through this record (its created id, owner, plan, secure-filing and
    /// invoice-family refusals) was deleted; <see cref="Sprk.Bff.Api.Services.RecordCreation.RecordCreationService"/> creates
    /// and answers with its own outcome.</summary>
    internal sealed record Outcome
    {
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

        /// <summary>A question asked as the caller failed at Dataverse — the caller's own error.</summary>
        public DataverseUserResponse? ClientFailure { get; init; }

        /// <summary>
        /// What the row is filed under cannot be told from its ADR-024 pair — a pair id that is not a record id, a pair id
        /// with no record type that repeats no lookup the row binds, or a type that names no table. Decided from the
        /// payload and the record-type catalog only (never from the record the pair names), so it says nothing about that
        /// record. A validation answer (HTTP 400); nothing is created.
        /// </summary>
        public string? Invalid { get; init; }

        public bool Succeeded => Denied is null && ClientFailure is null && Invalid is null;

        public static readonly Outcome Allowed = new();
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
    /// #1616: prepares an update's ADR-024 pair BEFORE anything is checked or written, AS THE CALLER (one read of the row,
    /// only when it applies), so the checks and the write see one and the same pair:
    /// <list type="bullet">
    /// <item><b>A BFF-managed pair</b> (a project, <see cref="BffManagedPairColumnsOf"/>) written by one half: the other half is
    /// added as the row holds it, so the pair rule, the secure-root gate and the application's write
    /// (<see cref="ProjectMatterFiling.FileAsync"/>) all act on exactly the same (record, type).</item>
    /// <item><b>Every other pair table</b> (task 045b): the update planner (<see cref="KeepRegardingPairWholeAsync"/>) — a
    /// type-only pair filled or cleared, a cleared id taking its type with it, and a pair kept in step with the agreement
    /// lookup this update moves or clears (the work assignment's #1616 rule, widened to the 18 pair tables).</item>
    /// </list>
    /// </summary>
    /// <returns>The item to check and write (unchanged when nothing applies), or the caller's own failure — nothing written.</returns>
    internal static async Task<(DataverseWriteItemMapper.MappedItem Item, RefileOutcome? Refusal)> KeepPairInStepAsync(
        IDataverseUserClient user, string table, string entitySetName, Guid recordId, DataverseWriteItemMapper.MappedItem item,
        CancellationToken ct, ILogger? logger = null)
    {
        if (BffManagedPairColumnsOf(table).Count == 0)
            return await KeepRegardingPairWholeAsync(user, table, entitySetName, recordId, item, logger, ct).ConfigureAwait(false);

        // Task 045f: a sent record URL names the record the pair names after the write (the stored half read as the caller).
        var url = await KeepRegardingRecordUrlAsync(user, table, entitySetName, recordId, item, ct).ConfigureAwait(false);
        if (url.Refusal is not null)
            return url;
        item = url.Item;

        var pair = PairOf(item);
        if (!pair.SetsEither || (pair.SetsId && pair.SetsType))
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
        if (!pair.SetsId)
            extra[SecureRootInheritance.PairIdColumn] = rowId;
        if (!pair.SetsType)
            extra[SecureRootInheritance.PairTypeColumn] = rowType is { } t ? new { relatedTable = RecordTypeTable, recordId = t } : null;

        return await MergeExtraAsync(user, table, item, extra, ct).ConfigureAwait(false);
    }

    /// <summary>Maps <paramref name="extra"/> as the caller and merges it over <paramref name="item"/> (its columns win).</summary>
    private static async Task<(DataverseWriteItemMapper.MappedItem Item, RefileOutcome? Refusal)> MergeExtraAsync(
        IDataverseUserClient user, string table, DataverseWriteItemMapper.MappedItem item, Dictionary<string, object?> extra,
        CancellationToken ct)
    {
        var mapped = await DataverseWriteItemMapper.MapAsync(user, table, JsonSerializer.SerializeToElement(extra), ct)
            .ConfigureAwait(false);
        if (mapped.ClientFailure is { } mapFailure)
            return (item, new RefileOutcome { ClientFailure = mapFailure });
        if (mapped.Item is not { } added)
            return (item, new RefileOutcome { Invalid = mapped.ValidationError ?? $"What this {table} is filed under could not be written." });

        return (Merge(item, added), null);
    }

    /// <summary>The pair's display columns, which describe the record the pair names: re-derived on a move, cleared with the
    /// pair (task 045b, owner ruling Q7).</summary>
    internal static readonly string[] PairDisplayColumns = ["sprk_regardingrecordname", "sprk_regardingrecordnumber", "sprk_regardingrecordurl"];

    // ── Task 045b: the update planner (design §2.2; owner rulings Q1, Q2, Q4, Q5, Q7 of 2026-11-07) ─────────────────────

    /// <summary>
    /// The 18 tables whose rows carry the whole ADR-024 pair — the type lookup to <c>sprk_recordtype_ref</c>, the id as text —
    /// and its display columns (owner ruling Q1 (a); the solution's <c>Entity.xml</c>). Not here: the matter's lone type column,
    /// the notification outbox's text type, and the thread's own anchor columns, which are not a pair.
    /// </summary>
    internal static readonly IReadOnlySet<string> RegardingPairTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_analysis", "sprk_billingevent", "sprk_communication", "sprk_decisionrecord", "sprk_event", "sprk_externalidentifier",
        "sprk_invoice", "sprk_invoiceapproval", "sprk_invoicecorrection", "sprk_invoicesubmission", "sprk_kpiassessment",
        "sprk_memo", "sprk_project", "sprk_reportcard", "sprk_servicerequest", "sprk_signal", "sprk_todo", "sprk_workassignment",
    };

    /// <summary>The pair type's navigation property on each pair table (the relationship's referencing attribute schema name,
    /// SpaarkeMaster <c>Other/Relationships/sprk_RecordType_Ref.xml</c>). Web API names are case-sensitive, and the invoice's is
    /// lower-case; every other pair table's is <c>sprk_RegardingRecordType</c>. Pinned against the solution for each of the
    /// <see cref="RegardingPairTables"/> by <c>RegardingPairUpdatePlannerTests.ThePairTypeNavigationProperties_MatchTheSolution</c>.</summary>
    internal static string RegardingRecordTypeNavigationPropertyOf(string table) =>
        string.Equals(table.Trim(), "sprk_invoice", StringComparison.OrdinalIgnoreCase)
            ? InvoiceRegardingRecordTypeNavigationProperty
            : RegardingRecordTypeNavigationProperty;

    private const string RegardingRecordTypeNavigationProperty = "sprk_RegardingRecordType";
    private const string InvoiceRegardingRecordTypeNavigationProperty = "sprk_regardingrecordtype";

    /// <summary>The structured log event an update that kept its pair whole writes (owner ruling Q5 (a): the response is unchanged).</summary>
    internal static readonly EventId RegardingPairKeptWholeEvent = new(45201, "RegardingPairKeptWholeOnUpdate");

    /// <summary>What an update writes about the pair, its display columns and the agreement lookups — the same view whichever
    /// shape the path's payload has (a mapped item, or a Web API dictionary).</summary>
    internal sealed record RegardingPairChange(
        bool SetsId, string? IdText, bool SetsType, Guid? TypeId, bool Unreadable,
        IReadOnlyDictionary<string, Guid> LookupsSet, IReadOnlySet<string> LookupsCleared, IReadOnlySet<string> DisplaySet)
    {
        public bool WritesPair => SetsId || SetsType;
    }

    /// <summary>The row's pair and agreement lookups, as the path's own reader read them.</summary>
    internal sealed record RegardingPairRow(string? IdText, Guid? TypeId, IReadOnlyDictionary<string, Guid> Lookups);

    /// <summary>A catalog row: the table its type names and the column its reference number is in.</summary>
    internal sealed record RegardingPairType(string? Table, string? NumberColumn);

    /// <summary>One read's answer: the value, or the status it failed with (and the caller's own response, on a caller read).</summary>
    internal readonly record struct RegardingPairRead<T>(T? Value, int Status, DataverseUserResponse? Failure) where T : class
    {
        public static RegardingPairRead<T> Ok(T value) => new(value, 200, null);
    }

    /// <summary>The reads the planner asks: each path passes the reader it already has (the caller's client, or the app-only
    /// reader of the playbook and push paths), so no path gains a reader (design §2.2, item 4). <c>MovedRecord</c>, when set,
    /// is the reader of the display values of a pair the update itself moves (rule 3) only; rules 2 and 4 keep
    /// <c>Record</c>, and without it rule 3 uses <c>Record</c> too.</summary>
    internal sealed record RegardingPairReaders(
        Func<CancellationToken, Task<RegardingPairRead<RegardingPairRow>>> Row,
        Func<Guid, CancellationToken, Task<RegardingPairRead<RegardingPairType>>> Type,
        Func<string, Guid, IReadOnlyList<string>, CancellationToken, Task<RegardingPairRead<IReadOnlyDictionary<string, string?>>>> Record,
        Func<string, Guid, IReadOnlyList<string>, CancellationToken, Task<RegardingPairRead<IReadOnlyDictionary<string, string?>>>>? MovedRecord = null);

    /// <summary>The planner's answer: the columns to write over the update's own (the id as text or <c>null</c>, the type as a
    /// catalog row id or <c>null</c>, each display column as text or <c>null</c>), what it did, or the read that failed.</summary>
    internal sealed record RegardingPairPlan(IReadOnlyDictionary<string, object?> Columns, string? Outcome, int? FailedStatus,
        DataverseUserResponse? Failure)
    {
        public static readonly RegardingPairPlan Nothing = new(new Dictionary<string, object?>(), null, null, null);

        public bool Failed => FailedStatus is not null;

        public bool ClearsThePair => Columns.TryGetValue(SecureRootInheritance.PairTypeColumn, out var type) && type is null;
    }

    /// <summary>The update's pair view of a mapped item.</summary>
    internal static RegardingPairChange PairChangeOf(string table, DataverseWriteItemMapper.MappedItem item)
    {
        var pair = PairOf(item);
        var agreement = PairAgreementColumns(table);
        var set = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var lookup in item.Lookups.Where(l => agreement.ContainsKey(l.Column)))
            set[lookup.Column] = lookup.RecordId;
        var cleared = item.ClearedColumns.Where(agreement.ContainsKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var display = item.Columns.Concat(item.ClearedColumns)
            .Where(c => PairDisplayColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new RegardingPairChange(pair.SetsId, pair.IdText, pair.SetsType, pair.TypeId, pair.InvalidId, set, cleared, display);
    }

    /// <summary>The update's pair view of a Web API dictionary (a lookup as an <c>@odata.bind</c> path or an
    /// <see cref="Microsoft.Xrm.Sdk.EntityReference"/>; <c>null</c> clears). A pair value it cannot read makes the view
    /// <see cref="RegardingPairChange.Unreadable"/>: the planner then leaves the write to its own checks.</summary>
    internal static RegardingPairChange PairChangeOf(string table, IEnumerable<KeyValuePair<string, object?>> payload)
    {
        var agreement = PairAgreementColumns(table);
        var set = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var cleared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var display = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool setsId = false, setsType = false, unreadable = false;
        string? idText = null;
        Guid? typeId = null;
        foreach (var (key, raw) in payload)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;
            var column = SecureRootInheritance.NormalizeColumn(key);
            var value = raw is JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } ? null : raw;
            if (string.Equals(column, SecureRootInheritance.PairIdColumn, StringComparison.OrdinalIgnoreCase))
            {
                setsId = true;
                idText = value switch
                {
                    null => null,
                    string s => s,
                    Guid g => g.ToString("D"),
                    JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
                    _ => null,
                };
                unreadable |= value is not null && idText is null;
            }
            else if (string.Equals(column, SecureRootInheritance.PairTypeColumn, StringComparison.OrdinalIgnoreCase))
            {
                setsType = true;
                typeId = ReferencedId(value);
                unreadable |= value is not null && typeId is null;
            }
            else if (agreement.ContainsKey(column))
            {
                if (value is null)
                    cleared.Add(column);
                else if (ReferencedId(value) is { } id)
                    set[column] = id;
            }
            else if (PairDisplayColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
            {
                display.Add(column);
            }
        }

        return new RegardingPairChange(setsId, idText, setsType, typeId, unreadable, set, cleared, display);
    }

    /// <summary>The record a lookup value names: an <see cref="Microsoft.Xrm.Sdk.EntityReference"/>, or a bind path
    /// (<c>/sprk_matters(…)</c>) as text. <c>null</c> for anything else.</summary>
    private static Guid? ReferencedId(object? value)
    {
        var text = value switch
        {
            Microsoft.Xrm.Sdk.EntityReference r => r.Id == Guid.Empty ? null : r.Id.ToString("D"),
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
            _ => null,
        };
        if (text is null)
            return null;
        var open = text.LastIndexOf('(');
        var close = text.LastIndexOf(')');
        var inner = open >= 0 && close > open ? text[(open + 1)..close] : text;
        return Guid.TryParse(inner.Trim(), out var id) && id != Guid.Empty ? id : null;
    }

    /// <summary>
    /// Task 045b, the update planner (design §2.2): what an update of a pair table (<see cref="RegardingPairTables"/>, not a
    /// BFF-managed pair) must add so the pair it leaves is whole and in step. It reads the row once, and only when the update
    /// writes a pair column or sets or clears an agreement lookup (<see cref="PairAgreementColumns"/>):
    /// <list type="number">
    /// <item><b>A cleared id</b> (the update clears the id and sets no type): the type and the display columns go with it.</item>
    /// <item><b>A type with no id after the write</b> (owner ruling Q2 (a)): the catalog row is read; exactly one record of its
    /// table named by the agreement lookups the row will hold (the update's own and the row's kept ones; an I-1 stamp column
    /// never while an intermediate source is bound; a mapped lookup never) → the id is filled; none or several, or a catalog
    /// read answered 403 (a type naming no table the reader can see) → the type is cleared with the display columns. A
    /// catalog read answered 404 leaves the write as sent (its own checks answer as before); any other failure is the read's
    /// own failure and nothing is written.</item>
    /// <item><b>A pair the update moves</b> (a new id, or a new type over the same id): its display columns are re-derived
    /// (owner ruling Q7 (b)), or cleared when the catalog row or the record answers 403 or 404 — never left describing the old
    /// record. Any other failed read is the read's own, and nothing is written. On the app-only paths the display values are
    /// read as the person the write runs for (<see cref="RegardingPairReaders.MovedRecord"/>); with no person they are cleared.</item>
    /// <item><b>A lookup the pair repeated</b> (same id, the type naming that lookup's table), moved or cleared by an update that
    /// writes no pair column, and no other lookup still repeating it: the pair moves with it (display columns re-derived) or is
    /// cleared with it (owner ruling Q4 (b)). A catalog read that fails refuses, as the work assignment's #1616 rule did.</item>
    /// </list>
    /// A whole pair, an empty pair and an id-only pair the update leaves alone are untouched. Columns the update itself sends
    /// are never overwritten, except a type-only pair's own type and id, which rule 2 completes or clears. The planner checks
    /// nothing and grants nothing: every path still asks its own checks of the pair as it will be written.
    /// </summary>
    /// <param name="lookupsAreMapped">The update's lookups are field-mapped values (the push): rule 2 never fills from them
    /// (owner ruling Q2, as 045a's step 4½ excludes a mapped candidate), only from the lookups the row keeps.</param>
    internal static async Task<RegardingPairPlan> PlanRegardingPairAsync(
        string table, RegardingPairChange change, RegardingPairReaders reads, CancellationToken ct, bool lookupsAreMapped = false)
    {
        var key = table.Trim().ToLowerInvariant();
        if (!RegardingPairTables.Contains(key) || BffManagedPairColumnsOf(key).Count > 0 || change.Unreadable
            || (!change.WritesPair && change.LookupsSet.Count == 0 && change.LookupsCleared.Count == 0))
        {
            return RegardingPairPlan.Nothing;
        }

        var rowRead = await reads.Row(ct).ConfigureAwait(false);
        if (rowRead.Value is not { } row)
            return new RegardingPairPlan(new Dictionary<string, object?>(), null, rowRead.Status, rowRead.Failure);

        var agreement = PairAgreementColumns(key);
        var after = new Dictionary<string, Guid>(row.Lookups, StringComparer.OrdinalIgnoreCase);
        foreach (var column in change.LookupsCleared)
            after.Remove(column);
        foreach (var (column, id) in change.LookupsSet)
            after[column] = id;

        var columns = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (change.WritesPair)
        {
            var idAfter = change.SetsId ? change.IdText : row.IdText;
            var typeAfter = change.SetsType ? change.TypeId : row.TypeId;

            // Rule 1: the update clears the id and names no type — the type and the display columns go with it.
            if (change.SetsId && string.IsNullOrWhiteSpace(change.IdText) && !(change.SetsType && change.TypeId is not null))
            {
                if (typeAfter is not null)
                    columns[SecureRootInheritance.PairTypeColumn] = null;
                ClearDisplay(columns, change);
                return Planned(columns, "the id was cleared; its type and display columns were cleared with it");
            }

            // Rule 2: a type with no id after the write.
            if (typeAfter is { } type && string.IsNullOrWhiteSpace(idAfter))
            {
                // 404: left as sent (the write's own AppendTo on the catalog row answers its uniform not-found). 403: the reader
                // may not see which table the type names — a type naming no table, cleared (as 045a's step 4½, fix round 2), never
                // stored type-only. Any other failure is the read's own; nothing is written.
                var typeRead = await reads.Type(type, ct).ConfigureAwait(false);
                if (typeRead.Value is null && typeRead.Status == 404)
                    return RegardingPairPlan.Nothing;
                if (typeRead.Value is null && typeRead.Status != 403)
                    return new RegardingPairPlan(new Dictionary<string, object?>(), null, typeRead.Status, typeRead.Failure);
                var named = typeRead.Value ?? new RegardingPairType(null, null);

                var fillFrom = lookupsAreMapped
                    ? row.Lookups.Where(l => !change.LookupsSet.ContainsKey(l.Key) && !change.LookupsCleared.Contains(l.Key))
                        .ToDictionary(l => l.Key, l => l.Value, StringComparer.OrdinalIgnoreCase)
                    : after;
                // The intermediate test is asked of the row as it will be (the update's own lookups too, mapped or not): a
                // mapped intermediate makes a stamp column no candidate, though only the kept lookups are candidates.
                var records = named.Table is null ? [] : FillRecords(key, fillFrom, after, named.Table);
                if (records.Count == 1)
                {
                    columns[SecureRootInheritance.PairIdColumn] = records[0].ToString("D");
                    var filled = await DeriveDisplayAsync(columns, change, reads, named.Table!, named.NumberColumn, records[0], ct).ConfigureAwait(false);
                    return filled.Failed ?? Planned(columns, "a type with no id; the id was filled from the one agreeing lookup" + filled.Suffix);
                }

                columns[SecureRootInheritance.PairIdColumn] = null;
                columns[SecureRootInheritance.PairTypeColumn] = null;
                ClearDisplay(columns, change);
                return Planned(columns, "a type with no id and no single agreeing lookup; the type was cleared");
            }

            // Rule 3: the update moves the pair itself — a new id, or a new type (re-typing names another record) — and its
            // display columns follow.
            if (Guid.TryParse(idAfter?.Trim(), out var movedTo) && movedTo != Guid.Empty
                && (!(Guid.TryParse(row.IdText?.Trim(), out var before) && before == movedTo) || typeAfter != row.TypeId)
                && PairDisplayColumns.Any(c => !change.DisplaySet.Contains(c)))
            {
                // A catalog row that is missing (404), that the reader may not see (403) or that names no table: the display
                // columns are cleared, never left describing the old record. Any other failure is the read's own; nothing is
                // written (as rule 2).
                var movedRead = typeAfter is { } movedType
                    ? await reads.Type(movedType, ct).ConfigureAwait(false)
                    : new RegardingPairRead<RegardingPairType>(null, 404, null);
                if (movedRead.Value is null && movedRead.Status is not (403 or 404))
                    return new RegardingPairPlan(new Dictionary<string, object?>(), null, movedRead.Status, movedRead.Failure);
                if (movedRead.Value is { Table: { } movedTable } movedNamed)
                {
                    var moved = await DeriveDisplayAsync(
                        columns, change, reads, movedTable, movedNamed.NumberColumn, movedTo, ct, reads.MovedRecord ?? reads.Record).ConfigureAwait(false);
                    return moved.Failed ?? Planned(columns, "the pair was moved" + moved.Suffix);
                }

                ClearDisplay(columns, change);
                return Planned(columns, "the pair was moved; its type names no table this reader can see, so its display columns were cleared");
            }

            return RegardingPairPlan.Nothing;
        }

        // Rule 4: the update moves or clears a lookup the pair repeated, and writes no pair column.
        if (!Guid.TryParse(row.IdText?.Trim(), out var pairId) || pairId == Guid.Empty || row.TypeId is not { } rowType)
            return RegardingPairPlan.Nothing;

        var inStep = agreement.Keys.Where(c => row.Lookups.TryGetValue(c, out var held) && held == pairId).ToArray();
        var changed = inStep.Where(c => change.LookupsSet.ContainsKey(c) || change.LookupsCleared.Contains(c)).ToArray();
        if (changed.Length == 0 || inStep.Any(c => !changed.Contains(c) || after.TryGetValue(c, out var still) && still == pairId))
            return RegardingPairPlan.Nothing; // not in step with what moved, or a lookup still repeats it

        var rowTypeRead = await reads.Type(rowType, ct).ConfigureAwait(false);
        if (rowTypeRead.Value is not { } rowNamed)
            return new RegardingPairPlan(new Dictionary<string, object?>(), null, rowTypeRead.Status, rowTypeRead.Failure);

        var following = changed.Where(c => string.Equals(agreement[c], rowNamed.Table, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (following.Length == 0)
            return RegardingPairPlan.Nothing; // the type names another table: the pair did not repeat that lookup

        var movedIds = following.Where(change.LookupsSet.ContainsKey).Select(c => change.LookupsSet[c]).Distinct().ToArray();
        if (movedIds.Length == 1)
        {
            // The type is unchanged, so it is not written again: only the id and the display columns move.
            columns[SecureRootInheritance.PairIdColumn] = movedIds[0].ToString("D");
            var followed = await DeriveDisplayAsync(columns, change, reads, rowNamed.Table!, rowNamed.NumberColumn, movedIds[0], ct).ConfigureAwait(false);
            return followed.Failed ?? Planned(columns, "the pair moved with the lookup it repeated" + followed.Suffix);
        }

        columns[SecureRootInheritance.PairIdColumn] = null;
        columns[SecureRootInheritance.PairTypeColumn] = null;
        ClearDisplay(columns, change);
        return Planned(columns, "the lookup the pair repeated was cleared or split; the pair was cleared with it");

        static RegardingPairPlan Planned(Dictionary<string, object?> columns, string outcome) => new(columns, outcome, null, null);
    }

    /// <summary>Rule 2's candidates (pure; 045a's step 4½ rule on the row as it will be): the distinct records of
    /// <paramref name="pairTable"/> named in the table's agreement columns, without an I-1 stamp column while an intermediate
    /// source is bound (<see cref="CoreAncestorResolver.ClassifyStampSource"/> would read it as a direct root link).</summary>
    /// <param name="lookups">The candidates' columns.</param>
    /// <param name="rowAfter">Every lookup the row will hold (the update's own included), which the intermediate test reads.</param>
    private static IReadOnlyList<Guid> FillRecords(
        string table, IReadOnlyDictionary<string, Guid> lookups, IReadOnlyDictionary<string, Guid> rowAfter, string pairTable)
    {
        var agreement = PairAgreementColumns(table);
        var bindsAnIntermediate = CoreAncestorResolver.StampSourceColumns.TryGetValue(table, out var sources)
                                  && rowAfter.Keys.Any(c => sources.Any(s => string.Equals(s.Column, c, StringComparison.OrdinalIgnoreCase)));
        return lookups
            .Where(l => agreement.TryGetValue(l.Key, out var target)
                        && string.Equals(target, pairTable, StringComparison.OrdinalIgnoreCase)
                        && !(bindsAnIntermediate && CoreAncestorResolver.CoreAncestorLookups.Any(
                            s => string.Equals(s.LookupAttribute, l.Key, StringComparison.OrdinalIgnoreCase))))
            .Select(l => l.Value)
            .Distinct()
            .ToArray();
    }

    /// <summary>Clears every display column the update does not send itself.</summary>
    private static void ClearDisplay(Dictionary<string, object?> columns, RegardingPairChange change)
    {
        foreach (var display in PairDisplayColumns.Where(c => !change.DisplaySet.Contains(c)))
            columns[display] = null;
    }

    /// <summary>What <see cref="DeriveDisplayAsync"/> did: the display columns were derived or cleared (the outcome's
    /// <see cref="Suffix"/>), or the read failed (<see cref="Failed"/>, the plan to return: nothing is written).</summary>
    private readonly record struct DisplayDerivation(string Suffix, RegardingPairPlan? Failed);

    /// <summary>Owner ruling Q7 (b): the display columns of the record the pair now names — its name (the one display-name
    /// map), its number (the column its catalog row names) and its url — for each one the update does not send. A record that
    /// is missing (404) or that the reader may not read (403) has its display columns cleared, never left stale; any other
    /// failed read is the read's own, and nothing is written.</summary>
    private static async Task<DisplayDerivation> DeriveDisplayAsync(
        Dictionary<string, object?> columns, RegardingPairChange change, RegardingPairReaders reads, string pairTable,
        string? numberColumn, Guid recordId, CancellationToken ct,
        Func<string, Guid, IReadOnlyList<string>, CancellationToken, Task<RegardingPairRead<IReadOnlyDictionary<string, string?>>>>? readRecord = null)
    {
        if (PairDisplayColumns.All(change.DisplaySet.Contains))
            return new DisplayDerivation("", null);

        var nameColumn = RegardingRecordType.GetPrimaryNameField(pairTable);
        var asked = new[] { nameColumn, numberColumn }.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        IReadOnlyDictionary<string, string?>? values = null;
        if (asked.Length > 0)
        {
            var read = await (readRecord ?? reads.Record)(pairTable, recordId, asked, ct).ConfigureAwait(false);
            if (read.Value is null && read.Status is 403 or 404)
            {
                ClearDisplay(columns, change);
                return new DisplayDerivation("; the record it names could not be read, so its display columns were cleared", null);
            }

            if (read.Value is null)
                return new DisplayDerivation("", new RegardingPairPlan(new Dictionary<string, object?>(), null, read.Status, read.Failure));
            values = read.Value;
        }

        string? ValueOf(string? column) =>
            column is not null && values is not null && values.TryGetValue(column, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        var name = ValueOf(nameColumn);
        Set("sprk_regardingrecordname", name is { Length: > InvoiceFamilyCoreRecordGuard.RegardingRecordNameMaxLength }
            ? name[..InvoiceFamilyCoreRecordGuard.RegardingRecordNameMaxLength]
            : name);
        Set("sprk_regardingrecordnumber", ValueOf(numberColumn));
        Set("sprk_regardingrecordurl", RegardingRecordType.BuildRecordUrl(pairTable, recordId.ToString("D")));
        return new DisplayDerivation("; its display columns were re-derived", null);

        void Set(string column, string? value)
        {
            if (!change.DisplaySet.Contains(column))
                columns[column] = value;
        }
    }

    /// <summary>Logs what the planner did (owner ruling Q5 (a)): the table, the row and the outcome — never the record the pair
    /// names (042c coordinator condition 2). A clear is a warning; a fill or a move is information.</summary>
    internal static void LogRegardingPairPlan(ILogger? logger, string path, string table, Guid recordId, RegardingPairPlan plan)
    {
        if (logger is null || plan.Outcome is null)
            return;
        logger.Log(plan.ClearsThePair ? LogLevel.Warning : LogLevel.Information, RegardingPairKeptWholeEvent,
            "[{Path}] the regarding pair of {Table} {RecordId} was kept whole on update: {Outcome}",
            path, table, recordId, plan.Outcome);
    }

    /// <summary>The planner's reads AS THE CALLER (the chat tool and the browser re-file routes): the row and the catalog
    /// through <see cref="CallerWriteAuthorization.PairTypeTableAsync"/>, the regarded record's name and number.</summary>
    internal static RegardingPairReaders CallerReaders(IDataverseUserClient user, string table, string entitySetName, Guid recordId) => new(
        async ct =>
        {
            var row = await user.GetAsync($"{entitySetName}({recordId:D})", ct).ConfigureAwait(false);
            if (!row.IsSuccess)
                return new RegardingPairRead<RegardingPairRow>(null, row.StatusCode, row);
            if (row.Body is not { ValueKind: JsonValueKind.Object } body)
            {
                return new RegardingPairRead<RegardingPairRow>(null, row.StatusCode, DataverseUserResponse.Fail(row.StatusCode,
                    DataverseUserClientErrorCodes.BadRequest, "The record could not be read, so what it is filed under cannot be checked."));
            }

            static Guid? GuidAt(JsonElement body, string property) =>
                body.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g)
                    ? g
                    : null;

            var lookups = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in PairAgreementColumns(table).Keys)
            {
                if (GuidAt(body, $"_{column}_value") is { } id)
                    lookups[column] = id;
            }

            var idText = body.TryGetProperty(SecureRootInheritance.PairIdColumn, out var pairId) && pairId.ValueKind == JsonValueKind.String
                ? pairId.GetString()
                : null;
            return RegardingPairRead<RegardingPairRow>.Ok(
                new RegardingPairRow(idText, GuidAt(body, $"_{SecureRootInheritance.PairTypeColumn}_value"), lookups));
        },
        async (typeId, ct) =>
        {
            var (typeRow, pairTable) = await PairTypeTableAsync(user, typeId, null, ct).ConfigureAwait(false);
            if (!typeRow.IsSuccess)
                return new RegardingPairRead<RegardingPairType>(null, typeRow.StatusCode, typeRow);
            var number = pairTable is null
                ? null
                : await user.GetAsync($"{RecordTypeEntitySet}({typeId:D})?$select={RegardingRecordType.RecordNumberFieldColumn}", ct)
                    .ConfigureAwait(false) is { IsSuccess: true, Body: { } numberBody }
                  && numberBody.TryGetProperty(RegardingRecordType.RecordNumberFieldColumn, out var field)
                  && field.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(field.GetString())
                    ? field.GetString()!.Trim()
                    : null;
            return RegardingPairRead<RegardingPairType>.Ok(new RegardingPairType(pairTable, number));
        },
        CallerRecordDisplay(user));

    /// <summary>
    /// A record's display columns read AS THE CALLER (<paramref name="user"/>'s own client): the record, or the caller's failed
    /// read (403 when they may not read it, 404 when it is missing), or 500 for an answer that is not a row. The planner's
    /// <see cref="CallerReaders"/> and the create's step 5½ derive (task 045d fix round 5) read with it.
    /// </summary>
    internal static Func<string, Guid, IReadOnlyList<string>, CancellationToken, Task<RegardingPairRead<IReadOnlyDictionary<string, string?>>>>
        CallerRecordDisplay(IDataverseUserClient user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return async (recordTable, id, asked, ct) =>
        {
            var set = await user.GetAsync($"EntityDefinitions(LogicalName='{recordTable}')?$select=EntitySetName", ct).ConfigureAwait(false);
            if (!set.IsSuccess)
                return new RegardingPairRead<IReadOnlyDictionary<string, string?>>(null, set.StatusCode, set);
            if (set.Body is not { } meta || !meta.TryGetProperty("EntitySetName", out var name) || name.ValueKind != JsonValueKind.String)
                return new RegardingPairRead<IReadOnlyDictionary<string, string?>>(null, 500, null);

            var record = await user.GetAsync($"{name.GetString()}({id:D})?$select={string.Join(",", asked)}", ct).ConfigureAwait(false);
            if (!record.IsSuccess)
                return new RegardingPairRead<IReadOnlyDictionary<string, string?>>(null, record.StatusCode, record);
            if (record.Body is not { ValueKind: JsonValueKind.Object } values)
                return new RegardingPairRead<IReadOnlyDictionary<string, string?>>(null, 500, null);
            return RegardingPairRead<IReadOnlyDictionary<string, string?>>.Ok(asked.ToDictionary(c => c,
                c => values.TryGetProperty(c, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null,
                StringComparer.OrdinalIgnoreCase));
        };
    }

    /// <summary>The planner's reads with the APP-ONLY reader the playbook and push paths already use for their own guards
    /// (design §2.2, item 4). A row or catalog row that does not exist answers 404; any other fault answers 500.</summary>
    internal static RegardingPairReaders AppOnlyReaders(IGenericEntityService entities, string table, Guid recordId) => new(
        async ct =>
        {
            var agreement = PairAgreementColumns(table).Keys.ToArray();
            var read = await AppOnlyAsync(() => entities.RetrieveAsync(
                table, recordId, [SecureRootInheritance.PairIdColumn, SecureRootInheritance.PairTypeColumn, .. agreement], ct), ct)
                .ConfigureAwait(false);
            if (read.Value is not { } row)
                return new RegardingPairRead<RegardingPairRow>(null, read.Status, null);
            var lookups = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in agreement)
            {
                if (row.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>(column) is { Id: var id } && id != Guid.Empty)
                    lookups[column] = id;
            }

            return RegardingPairRead<RegardingPairRow>.Ok(new RegardingPairRow(
                row.GetAttributeValue<string>(SecureRootInheritance.PairIdColumn),
                row.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>(SecureRootInheritance.PairTypeColumn)?.Id,
                lookups));
        },
        async (typeId, ct) =>
        {
            var read = await AppOnlyAsync(() => entities.RetrieveAsync(
                RecordTypeTable, typeId, [RecordTypeLogicalNameColumn, RegardingRecordType.RecordNumberFieldColumn], ct), ct)
                .ConfigureAwait(false);
            if (read.Value is not { } row)
                return new RegardingPairRead<RegardingPairType>(null, read.Status, null);
            var logical = row.GetAttributeValue<string>(RecordTypeLogicalNameColumn)?.Trim().ToLowerInvariant();
            return RegardingPairRead<RegardingPairType>.Ok(new RegardingPairType(
                string.IsNullOrEmpty(logical) ? null : logical, RegardingRecordType.RecordNumberFieldOf(row)));
        },
        async (recordTable, id, asked, ct) =>
        {
            var read = await AppOnlyAsync(() => entities.RetrieveAsync(recordTable, id, asked.ToArray(), ct), ct).ConfigureAwait(false);
            return read.Value is { } row
                ? RegardingPairRead<IReadOnlyDictionary<string, string?>>.Ok(
                    asked.ToDictionary(c => c, c => (string?)row.GetAttributeValue<string>(c), StringComparer.OrdinalIgnoreCase))
                : new RegardingPairRead<IReadOnlyDictionary<string, string?>>(null, read.Status, null);
        });

    /// <summary>
    /// The field-mapping push's display read: the push derives display values as the caller. The record the pair names is
    /// read with the caller's own query (<c>MSCRMCallerID</c>); a record the caller may not read, or that is missing, answers
    /// 403 or 404 (its display columns are cleared); any other fault, or no caller query, answers 500 (that child is refused).
    /// </summary>
    internal static Func<string, Guid, IReadOnlyList<string>, CancellationToken, Task<RegardingPairRead<IReadOnlyDictionary<string, string?>>>>
        CallerDisplayReader(
            Sprk.Bff.Api.Services.Communication.IImpersonatedCommunicationQuery? callerQuery, IGenericEntityService? entities,
            Guid callerSystemUserId) =>
        async (recordTable, id, asked, ct) =>
        {
            if (callerQuery is null || entities is null || callerSystemUserId == Guid.Empty)
                return new RegardingPairRead<IReadOnlyDictionary<string, string?>>(null, 500, null);
            try
            {
                var entitySet = await entities.GetEntitySetNameAsync(recordTable, ct).ConfigureAwait(false);
                var rows = await callerQuery.QueryAsync(
                    entitySet, $"$select={string.Join(",", asked)}&$filter={recordTable}id eq {id:D}", callerSystemUserId, ct)
                    .ConfigureAwait(false);
                if (rows.Count == 0)
                    return new RegardingPairRead<IReadOnlyDictionary<string, string?>>(null, 404, null);
                var row = rows[0];
                return RegardingPairRead<IReadOnlyDictionary<string, string?>>.Ok(asked.ToDictionary(c => c,
                    c => row.TryGetValue(c, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null,
                    StringComparer.OrdinalIgnoreCase));
            }
            catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.NotFound)
            {
                return new RegardingPairRead<IReadOnlyDictionary<string, string?>>(null, (int)ex.StatusCode!.Value, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return new RegardingPairRead<IReadOnlyDictionary<string, string?>>(null, 500, null);
            }
        };

    /// <summary>
    /// The display read of a pair an app-only update moves (rule 3): as the person the write runs for
    /// (<see cref="CallerDisplayReader"/>: a record they may not read, or that is missing, answers 403 or 404 and its display
    /// columns are cleared; any other fault, or no query, answers 500 and the write is refused). With no person (null or empty)
    /// it answers 403 without reading, so the display columns are cleared.
    /// </summary>
    internal static Func<string, Guid, IReadOnlyList<string>, CancellationToken, Task<RegardingPairRead<IReadOnlyDictionary<string, string?>>>>
        MovedDisplayReader(
            Sprk.Bff.Api.Services.Communication.IImpersonatedCommunicationQuery? personQuery, IGenericEntityService? entities,
            Guid? person) =>
        person is { } id && id != Guid.Empty
            ? CallerDisplayReader(personQuery, entities, id)
            : (_, _, _, _) => Task.FromResult(new RegardingPairRead<IReadOnlyDictionary<string, string?>>(null, 403, null));

    /// <summary>An app-only read: a row, or 404 (it does not exist) or 500 (any other fault). The caller's own cancellation
    /// propagates.</summary>
    private static async Task<RegardingPairRead<Microsoft.Xrm.Sdk.Entity>> AppOnlyAsync(
        Func<Task<Microsoft.Xrm.Sdk.Entity>> read, CancellationToken ct)
    {
        try
        {
            return await read().ConfigureAwait(false) is { } row
                ? RegardingPairRead<Microsoft.Xrm.Sdk.Entity>.Ok(row)
                : new RegardingPairRead<Microsoft.Xrm.Sdk.Entity>(null, 404, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new RegardingPairRead<Microsoft.Xrm.Sdk.Entity>(null,
                Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver.IsRecordNotFound(ex) ? 404 : 500, null);
        }
    }

    /// <summary>
    /// Task 045b: the update planner (<see cref="PlanRegardingPairAsync"/>) on a CALLER path's mapped item (the chat tool and
    /// the browser re-file routes), before anything is checked or written, so every check sees the pair as it will be written.
    /// The planned columns replace the item's own (its type-only pair, a display column) and are mapped as the caller.
    /// </summary>
    /// <remarks>Task 045d (owner ruling Q8 (a)): then an invoice-family row's five resolver fields, when the update sets or
    /// clears one of its own core lookups (<see cref="KeepInvoiceFamilyResolverFieldsAsync"/>).</remarks>
    /// <returns>The item to check and write (unchanged when nothing applies), or the caller's own failure — nothing written.</returns>
    internal static async Task<(DataverseWriteItemMapper.MappedItem Item, RefileOutcome? Refusal)> KeepRegardingPairWholeAsync(
        IDataverseUserClient user, string table, string entitySetName, Guid recordId, DataverseWriteItemMapper.MappedItem item,
        ILogger? logger, CancellationToken ct)
    {
        item = WithCanonicalFamilyPairId(table, item);
        var sentUrl = RegardingRecordUrlRuleApplies(table) && PairWriteOf(item).SetsUrl; // not a url the planner derives
        var kept = await KeepRegardingPairWholeCoreAsync(user, table, entitySetName, recordId, item, logger, ct).ConfigureAwait(false);
        if (kept.Refusal is null && sentUrl) // task 045f: a sent record URL names the record the pair names after the write
            kept = await KeepRegardingRecordUrlAsync(user, table, entitySetName, recordId, kept.Item, ct).ConfigureAwait(false);
        return kept.Refusal is not null
            ? kept
            : await KeepInvoiceFamilyResolverFieldsAsync(user, table, entitySetName, recordId, kept.Item, logger, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Task 045e (fix round 1, F2): on an invoice-family table, a pair id the caller sends that reads as an id is written in
    /// canonical form (lower-case "D", <see cref="InvoiceFamilyCoreRecordGuard.CanonicalPairId"/>), so the D-68 alternate key
    /// never takes another spelling of the same id. Whether that id may be written is the guard's agreement check, later in the
    /// re-file core; text that is not an id is left for it to refuse. Other tables: unchanged.
    /// Fix round 4: a record URL the caller sends that names a record (<see cref="RegardingPairWrite.TryReadRecordUrl"/>) is
    /// written in canonical form (<see cref="InvoiceFamilyCoreRecordGuard.CanonicalRecordUrl"/>) of the table and id it names, so
    /// the agreement check decides the same record and an admitted URL is stored as the server writes it; a URL that names no
    /// record is left for the check to refuse.
    /// </summary>
    internal static DataverseWriteItemMapper.MappedItem WithCanonicalFamilyPairId(string table, DataverseWriteItemMapper.MappedItem item)
    {
        if (InvoiceFamilyCoreRecordGuard.ColumnsToRead(table) is null)
            return item;
        var columns = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var pair = PairOf(item);
        if (pair.SetsId && pair.IdText is { } text && Guid.TryParse(text.Trim(), out var id)
            && InvoiceFamilyCoreRecordGuard.CanonicalPairId(id) is var canonical
            && !string.Equals(text, canonical, StringComparison.Ordinal))
        {
            columns[SecureRootInheritance.PairIdColumn] = canonical;
        }

        var url = PairWriteOf(item);
        if (url.SetsUrl && RegardingPairWrite.TryReadRecordUrl(url.UrlText, out var urlTable, out var urlId)
            && InvoiceFamilyCoreRecordGuard.CanonicalRecordUrl(urlTable, urlId) is var canonicalUrl
            && !string.Equals(url.UrlText, canonicalUrl, StringComparison.Ordinal))
        {
            columns[RegardingPairWrite.RecordUrlColumn] = canonicalUrl;
        }

        return columns.Count == 0 ? item : WithColumnsSet(table, item, columns);
    }

    /// <summary>
    /// Task 045d (owner ruling Q8 (a)), the update half on the caller paths (the child-record PATCH, the chat update tool): an
    /// update that sets or clears one of an invoice-family row's own core lookups (<see cref="InvoiceFamilyCoreRecordGuard.ColumnsToRead"/>)
    /// writes, in the same PATCH, the five resolver fields of the core record the row will name
    /// (<see cref="InvoiceFamilyCoreRecordGuard.DeriveResolverFieldsAsync"/>, all five or none), over any the update or the planner
    /// set. Everything is read AS THE CALLER (the row's stored core lookups, the catalog row, the record), so Dataverse decides
    /// what the caller may read. A row the guard's Check refuses is left to the re-file core, which refuses it with its code, in
    /// its own order (NFR-07).
    /// Fail closed: a read that fails, or a derive that cannot complete, writes nothing — the caller's own failure, the guard's
    /// code through the route's invoice-family mapping, and a record the caller cannot read the uniform not-found.
    /// </summary>
    internal static async Task<(DataverseWriteItemMapper.MappedItem Item, RefileOutcome? Refusal)> KeepInvoiceFamilyResolverFieldsAsync(
        IDataverseUserClient user, string table, string entitySetName, Guid recordId, DataverseWriteItemMapper.MappedItem item,
        ILogger? logger, CancellationToken ct)
    {
        if (InvoiceFamilyCoreRecordGuard.ColumnsToRead(table) is not { } coreColumns
            || !item.Lookups.Select(l => l.Column).Concat(item.ClearedColumns).Any(c => coreColumns.Contains(c, StringComparer.OrdinalIgnoreCase)))
        {
            return (item, null);
        }

        // The record the row will name: the guard's own Check over the stored core lookups (read as the caller) overlaid with the
        // change, the first question the re-file core's guard asks. A row it refuses is left to the re-file core, which refuses it
        // with its code in its own order (NFR-07); the guard's other questions (supersedes, lines, cycle) stay there, asked once.
        var guarded = InvoiceFamilyCoreRecordGuard.GuardedLookups(table)!
            .Where(g => coreColumns.Contains(g.Column, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        StoredLookups read;
        try
        {
            read = await ReadLookupsAsCallerAsync(user, entitySetName, recordId, guarded, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            read = new StoredLookups(null, null, ex.GetType().Name);
        }

        if (read.Failure is { } failure)
            return (item, new RefileOutcome { ClientFailure = failure });
        if (read.Values is not { } stored)
            return (item, new RefileOutcome { InvoiceFamilyRefusal = FiledUnderUnreadable(table, read.Problem) });

        var effective = EffectiveLookups(stored, item);
        var decided = InvoiceFamilyCoreRecordGuard.Check(table, column => effective.TryGetValue(column, out var value) ? value : null);
        if (decided.IsRefused)
            return (item, null);

        // An update that re-names the record the row already names (the same table and id) moves nothing: the row keeps the
        // fields it has, as the pair planner keeps a pair that does not move (045b rule 4).
        if (InvoiceFamilyCoreRecordGuard.NamesTheSameRecord(decided,
                InvoiceFamilyCoreRecordGuard.Check(table, column => stored.TryGetValue(column, out var kept) ? kept : null)))
        {
            return (item, null);
        }

        var derived = await InvoiceFamilyCoreRecordGuard.DeriveGenericResolverFieldsAsync(
            decided, CallerRecordReader(user, table, entitySetName, recordId), CallerRecordTypeReader(user), ct).ConfigureAwait(false);
        if (derived is not { IsRefused: false, ResolverFields: { } fields })
        {
            return (item, derived.RefusalCode == InvoiceFamilyCoreRecordGuard.RecordNotFoundCode
                ? new RefileOutcome { Denied = NotAvailableToFileUnder, ParentUnavailable = true }
                : new RefileOutcome
                {
                    InvoiceFamilyRefusal = derived.IsRefused
                        ? derived
                        : InvoiceFamilyCoreRecord.Refused(table, InvoiceFamilyCoreRecordGuard.ReadFailedCode,
                            "The invoice-family check gave no answer; nothing was written."),
                });
        }

        logger?.LogInformation(
            "[UPDATE] the resolver fields of {Table} {RecordId} were derived from the core record it will name", table, recordId);
        return (WithColumnsSet(table, item, fields.ToGenericFields()), null);
    }

    /// <summary>The derive's record read AS THE CALLER (the planner's own, <see cref="CallerReaders"/>): a record they may not
    /// read, or that is missing, is not found; any other failure is a fault (the guard's <see cref="InvoiceFamilyCoreRecordGuard.ReadFailedCode"/>).</summary>
    private static InvoiceFamilyCoreRecordGuard.RecordLookup CallerRecordReader(
        IDataverseUserClient user, string table, string entitySetName, Guid recordId)
    {
        var record = CallerReaders(user, table, entitySetName, recordId).Record;
        return async (recordTable, id, columns, ct) =>
        {
            var read = await record(recordTable, id, columns, ct).ConfigureAwait(false);
            if (read.Value is not { } values)
            {
                return read.Status is 403 or 404
                    ? null
                    : throw new InvalidOperationException($"the record read answered {read.Status}");
            }

            var row = new Microsoft.Xrm.Sdk.Entity(recordTable, id);
            foreach (var (column, value) in values)
            {
                if (value is not null)
                    row[column] = value;
            }

            return row;
        };
    }

    /// <summary>The derive's catalog read AS THE CALLER: the table's active <c>sprk_recordtype_ref</c> row (its id and number
    /// column), <c>null</c> when there is none; a failed read is a fault. The table is a core table the guard names.</summary>
    private static InvoiceFamilyCoreRecordGuard.RecordTypeLookup CallerRecordTypeReader(IDataverseUserClient user) =>
        async (recordTable, ct) =>
        {
            var response = await user.GetAsync(
                $"{RecordTypeEntitySet}?$select={RecordTypeTable}id,{RegardingRecordType.RecordNumberFieldColumn}" +
                $"&$filter={RecordTypeLogicalNameColumn} eq '{recordTable}' and statecode eq 0&$top=1", ct).ConfigureAwait(false);
            if (!response.IsSuccess)
                throw new InvalidOperationException($"the catalog read answered {response.StatusCode}");
            if (response.Body is not { ValueKind: JsonValueKind.Object } body
                || !body.TryGetProperty("value", out var rows) || rows.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("the catalog read gave no list");
            }

            foreach (var found in rows.EnumerateArray())
            {
                if (!found.TryGetProperty($"{RecordTypeTable}id", out var idValue) || idValue.ValueKind != JsonValueKind.String
                    || !Guid.TryParse(idValue.GetString(), out var typeId))
                {
                    throw new InvalidOperationException("the catalog row has no id");
                }

                var row = new Microsoft.Xrm.Sdk.Entity(RecordTypeTable, typeId);
                if (found.TryGetProperty(RegardingRecordType.RecordNumberFieldColumn, out var number) && number.ValueKind == JsonValueKind.String)
                    row[RegardingRecordType.RecordNumberFieldColumn] = number.GetString();
                return row;
            }

            return null;
        };

    /// <summary>The 045b planner on a caller path's mapped item (see <see cref="KeepRegardingPairWholeAsync"/>).</summary>
    private static async Task<(DataverseWriteItemMapper.MappedItem Item, RefileOutcome? Refusal)> KeepRegardingPairWholeCoreAsync(
        IDataverseUserClient user, string table, string entitySetName, Guid recordId, DataverseWriteItemMapper.MappedItem item,
        ILogger? logger, CancellationToken ct)
    {
        var plan = await PlanRegardingPairAsync(table, PairChangeOf(table, item), CallerReaders(user, table, entitySetName, recordId), ct)
            .ConfigureAwait(false);
        if (plan.Failed)
        {
            return (item, new RefileOutcome
            {
                ClientFailure = plan.Failure ?? DataverseUserResponse.Fail(plan.FailedStatus!.Value, DataverseUserClientErrorCodes.BadRequest,
                    "The record could not be read, so what it is filed under cannot be checked."),
            });
        }

        if (plan.Columns.Count == 0)
            return (item, null);

        // A clear of the type THIS update sends removes it before the re-file checks, so they would no longer ask AppendTo on
        // its catalog row: asked here, with their own question and answer (as 045a's step 4½, fix round 1 F4) — a caller who may
        // not append to the type, or whose type row is missing, still gets the uniform not-found, never a cleared write.
        var sentType = item.Lookups
            .Where(l => string.Equals(l.Column, SecureRootInheritance.PairTypeColumn, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (plan.ClearsThePair && sentType.Length > 0)
        {
            var me = await WhoAmIAsync(user, ct).ConfigureAwait(false);
            if (me.Failure is { } whoAmIFailure)
                return (item, new RefileOutcome { ClientFailure = whoAmIFailure });
            var appendTo = await CheckCallerMayAppendToAsync(user, me.SystemUserId, sentType, ct).ConfigureAwait(false);
            if (!appendTo.Succeeded)
                return (item, new RefileOutcome { Denied = appendTo.Denied, ParentUnavailable = appendTo.ParentUnavailable });
        }

        LogRegardingPairPlan(logger, "UPDATE", table, recordId, plan);
        var extra = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (column, value) in plan.Columns)
        {
            extra[column] = string.Equals(column, SecureRootInheritance.PairTypeColumn, StringComparison.OrdinalIgnoreCase) && value is Guid type
                ? new { relatedTable = RecordTypeTable, recordId = type }
                : value;
        }

        var merged = await MergeExtraAsync(user, table, WithoutColumns(item, plan.Columns.Keys), extra, ct).ConfigureAwait(false);
        if (merged.Refusal is not null)
            return merged;

        // A display column the planner clears is text, never a lookup: it is written as a null, but not listed as a cleared
        // column, which the re-file core reads as a candidate parent clear (RefileAsync) — so clearing the display never turns
        // an ordinary update into a re-file. A display clear the caller sent itself is kept as the caller's mapper listed it.
        return (merged.Item with
        {
            ClearedColumns = merged.Item.ClearedColumns
                .Where(c => !PairDisplayColumns.Contains(c, StringComparer.OrdinalIgnoreCase)
                            || item.ClearedColumns.Contains(c, StringComparer.OrdinalIgnoreCase))
                .ToArray(),
        }, null);
    }

    /// <summary>
    /// Task 045b: the update planner on an APP-ONLY path's Web API dictionary (the playbook Update Record node and confirmed
    /// proposals, the playbook output mapping, the field-mapping push), with the app-only reader the path already has. The
    /// planner adds no check (UAC-r2, 2026-11-07): a row or catalog row that does not exist leaves the pair as sent (the write
    /// answers its own not-found); any other failed read refuses, nothing written (fail closed).
    /// </summary>
    /// <remarks>Task 045f: then a record URL the payload sends is decided (<see cref="KeepRegardingRecordUrlAppOnlyAsync"/>): it
    /// must name the record the pair names after the write. On a write run as a person, the stored row is read as that person
    /// first, before the planner, whatever halves the write sends: a row they cannot read or that is not there for them is
    /// refused (read failed) and nothing is written. On an app-only write, the stored row is read only when the write does not
    /// send both pair halves (on the thread, does not set the deciding lookup), and a row that does not exist is then refused.
    /// A URL the planner derives is not decided again.</remarks>
    /// <returns>The payload to write: <paramref name="payload"/> itself when nothing applies, otherwise a copy.</returns>
    /// <exception cref="RecordOwnerUnresolvedException">A read the planner needs failed, or a sent record URL does not name the
    /// row's regarding record; nothing was written.</exception>
    /// <param name="storedRowAsPerson">Task 045f: on a write run AS a person, the reader of the stored row as that person
    /// (<see cref="PersonStoredRowReader"/>), so a sent URL is decided on what they can read; null on an app-only write.</param>
    internal static async Task<Dictionary<string, object?>> KeepRegardingPairWholeAppOnlyAsync(
        IGenericEntityService? entities, string table, Guid recordId, Dictionary<string, object?> payload, string path,
        ILogger? logger, CancellationToken ct, bool lookupsAreMapped = false,
        Func<string, Guid, IReadOnlyList<string>, CancellationToken, Task<RegardingPairRead<IReadOnlyDictionary<string, string?>>>>? displayRecord = null,
        Func<string, Guid, IReadOnlyList<string>, CancellationToken, Task<RegardingPairRead<IReadOnlyDictionary<string, string?>>>>? movedDisplayRecord = null,
        Func<CancellationToken, Task<RegardingPairRead<RegardingPairRow>>>? storedRowAsPerson = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var sent = RegardingPairWrite.Of(table, payload);
        var sentUrl = RegardingRecordUrlRuleApplies(table) && sent.SetsUrl; // not a derived one

        // Task 045f fix round 3: on a write run as a person that sends a URL, the stored row is read as that person FIRST,
        // whatever pair halves the write sends — before the planner fills a half from what it reads app-only. A row they
        // cannot read, or that is not there for them, is the read-failed refusal, so the answer never turns on a row they
        // cannot see. A readable row is decided as before (the one read is reused).
        if (sentUrl && storedRowAsPerson is not null && !string.IsNullOrWhiteSpace(sent.UrlText))
        {
            var asPerson = await storedRowAsPerson(ct).ConfigureAwait(false);
            if (asPerson.Value is null)
            {
                throw new RecordOwnerUnresolvedException(table, RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUndetermined,
                    "what it is filed under could not be read, so it was not written"));
            }

            storedRowAsPerson = _ => Task.FromResult(asPerson);
        }

        var planned = await KeepRegardingPairWholeAppOnlyCoreAsync(
                entities, table, recordId, payload, path, logger, ct, lookupsAreMapped, displayRecord, movedDisplayRecord)
            .ConfigureAwait(false);
        return sentUrl
            ? await KeepRegardingRecordUrlAppOnlyAsync(entities, table, recordId, planned, ct, storedRowAsPerson).ConfigureAwait(false)
            : planned;
    }

    /// <summary>The 045b planner on an app-only path's payload (see <see cref="KeepRegardingPairWholeAppOnlyAsync"/>).</summary>
    private static async Task<Dictionary<string, object?>> KeepRegardingPairWholeAppOnlyCoreAsync(
        IGenericEntityService? entities, string table, Guid recordId, Dictionary<string, object?> payload, string path,
        ILogger? logger, CancellationToken ct, bool lookupsAreMapped,
        Func<string, Guid, IReadOnlyList<string>, CancellationToken, Task<RegardingPairRead<IReadOnlyDictionary<string, string?>>>>? displayRecord,
        Func<string, Guid, IReadOnlyList<string>, CancellationToken, Task<RegardingPairRead<IReadOnlyDictionary<string, string?>>>>? movedDisplayRecord)
    {
        if (!RegardingPairPlannerApplies(table, payload))
            return payload;
        var change = PairChangeOf(table, payload);

        if (entities is null)
        {
            throw new RecordOwnerUnresolvedException(table, RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUndetermined,
                "what it is filed under cannot be read here, so it was not written"));
        }

        // displayRecord: the reader of the regarded record's display values when the path reads them otherwise (the push, as
        // the caller: CallerDisplayReader); the row and the catalog are still read with the path's app-only reader.
        // movedDisplayRecord: the display values of a pair the update itself moves (rule 3) are read as the person the write
        // runs for (<see cref="MovedDisplayReader"/>); left out, they are cleared, never read app-only. Rules 2 and 4 follow a
        // lookup the row holds and keep the app-only reader. A path that passes displayRecord reads every rule's display so.
        var readers = AppOnlyReaders(entities, table, recordId);
        readers = displayRecord is not null
            ? readers with { Record = displayRecord }
            : readers with { MovedRecord = movedDisplayRecord ?? MovedDisplayReader(null, entities, null) };
        var plan = await PlanRegardingPairAsync(table, change, readers, ct, lookupsAreMapped).ConfigureAwait(false);
        if (plan.Failed)
        {
            if (plan.FailedStatus == 404)
                return payload;
            throw new RecordOwnerUnresolvedException(table, RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUndetermined,
                "what it is filed under could not be read, so it was not written"));
        }

        if (plan.Columns.Count == 0)
            return payload;

        LogRegardingPairPlan(logger, path, table, recordId, plan);
        var written = new Dictionary<string, object?>(payload, payload.Comparer);
        foreach (var key in written.Keys.Where(k => plan.Columns.ContainsKey(SecureRootInheritance.NormalizeColumn(k))).ToList())
            written.Remove(key);
        foreach (var (column, value) in plan.Columns)
        {
            if (string.Equals(column, SecureRootInheritance.PairTypeColumn, StringComparison.OrdinalIgnoreCase))
                written[$"{RegardingRecordTypeNavigationPropertyOf(table)}@odata.bind"] = value is Guid type ? $"/{RecordTypeEntitySet}({type:D})" : null;
            else
                written[column] = value;
        }

        return written;
    }

    /// <summary>Whether an app-only payload is one the update planner reads anything for (a pair table, not BFF-managed, whose
    /// payload writes a pair column or sets or clears an agreement lookup), so a path resolves its reader only when it is.</summary>
    internal static bool RegardingPairPlannerApplies(string table, IEnumerable<KeyValuePair<string, object?>> payload)
    {
        if (!RegardingPairTables.Contains(table.Trim()) || BffManagedPairColumnsOf(table).Count > 0)
            return false;
        var change = PairChangeOf(table, payload);
        return !change.Unreadable && (change.WritesPair || change.LookupsSet.Count > 0 || change.LookupsCleared.Count > 0);
    }

    // ── Task 045f: the regarding record URL names the row's regarding record, on every pair table and the thread ───────────

    /// <summary>Task 045f: the communication thread. It carries no ADR-024 pair, but it is filed under the record its typed
    /// regarding lookups name (<see cref="Sprk.Bff.Api.Services.Communication.ThreadResolver.ThreadRegardingColumns"/>, in
    /// ADR-024 priority order), and it stores that record's URL.</summary>
    internal const string ThreadTable = "sprk_communicationthread";

    private static bool IsThread(string table) => string.Equals(table.Trim(), ThreadTable, StringComparison.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, Guid?> NoSentLookups = new Dictionary<string, Guid?>();

    /// <summary>
    /// Task 045f: the tables whose record URL <see cref="DecideRegardingRecordUrlAsync"/> decides — every pair table
    /// (<see cref="RegardingPairTables"/>) except the six invoice-family tables, whose URL is decided against the core record
    /// (<see cref="InvoiceFamilyCoreRecordGuard.CheckRegardingPairAgreesAsync"/>, task 045e), and the communication thread
    /// (<see cref="ThreadTable"/>), against its typed regarding.
    /// </summary>
    internal static bool RegardingRecordUrlRuleApplies(string table) =>
        IsThread(table)
        || (RegardingPairTables.Contains(table.Trim())
            && !InvoiceFamilyCoreRecordGuard.CoreRecordLookups.ContainsKey(table.Trim().ToLowerInvariant()));

    /// <summary>
    /// Task 045f: whether an app-only path that asks <see cref="KeepRegardingPairWholeAppOnlyAsync"/> only when it applies must
    /// ask it for <paramref name="payload"/> — the update planner applies (<see cref="RegardingPairPlannerApplies"/>), or the
    /// payload sends a record URL the 045f rule decides (<see cref="RegardingRecordUrlRuleApplies"/>).
    /// </summary>
    internal static bool RegardingPairWriteApplies(string table, IEnumerable<KeyValuePair<string, object?>> payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var pairs = payload as IReadOnlyCollection<KeyValuePair<string, object?>> ?? payload.ToList();
        return RegardingPairPlannerApplies(table, pairs)
               || (RegardingRecordUrlRuleApplies(table) && RegardingPairWrite.Of(table, pairs).SetsUrl);
    }

    /// <summary>What <see cref="DecideRegardingRecordUrlAsync"/> answered: nothing to do, the URL to store (canonical), a
    /// refusal (<see cref="RegardingPairWrite.RecordUrlMismatchCode"/>), or the read that failed (nothing is written).</summary>
    internal readonly record struct RegardingRecordUrlDecision(
        string? CanonicalUrl, bool Refused, int? FailedStatus, DataverseUserResponse? Failure)
    {
        public static RegardingRecordUrlDecision Unchanged => default;

        public static RegardingRecordUrlDecision Refuse => new(null, true, null, null);

        public bool Failed => FailedStatus is not null;
    }

    /// <summary>
    /// Task 045f, the ONE decision every BFF write path asks (the child-record PATCH, the chat update tool, the playbook Update
    /// Record node, the output orchestrator, the create): on a table the rule decides (<see cref="RegardingRecordUrlRuleApplies"/>),
    /// a record URL the write sends names the record the row is filed under after the write — on a pair table the pair the same
    /// write sends, else the stored one; on the thread the first typed regarding lookup, in priority order, that the row holds
    /// after the write (a lookup the write sets or clears replaces the stored value of that column only, so a stored
    /// higher-priority lookup still beats a lower one the write sets) — read with the path's own reader.
    /// <list type="bullet">
    /// <item>No URL sent, or a clear (null or blank): unchanged, nothing read.</item>
    /// <item>A URL that names no record (<see cref="RegardingPairWrite.TryReadRecordUrl"/>), a row that names none after the
    /// write (no id, an id that is not one, an unreadable pair value, a type naming no table the reader can see, no typed
    /// regarding), or a URL naming another table or record: refused.</item>
    /// <item>Otherwise admitted, and stored in canonical form (<see cref="RegardingPairWrite.TryAdmitRecordUrl"/>).</item>
    /// <item>Fail closed: a read that fails is returned as failed (nothing is written); the caller's cancellation propagates.</item>
    /// </list>
    /// </summary>
    /// <param name="written">What the write sends of the pair and the URL, after the update planner.</param>
    /// <param name="storedRow">The stored row (its pair, and on the thread its typed regarding lookups); null on a create.</param>
    /// <param name="pairType">The catalog reader: the table a pair type names.</param>
    /// <param name="sentLookups">On the thread: the typed regarding lookups the write sets (an id) or clears (null).</param>
    internal static async Task<RegardingRecordUrlDecision> DecideRegardingRecordUrlAsync(
        string table, RegardingPairWrite written,
        Func<CancellationToken, Task<RegardingPairRead<RegardingPairRow>>>? storedRow,
        Func<Guid, CancellationToken, Task<RegardingPairRead<RegardingPairType>>> pairType,
        CancellationToken ct,
        IReadOnlyDictionary<string, Guid?>? sentLookups = null)
    {
        ArgumentNullException.ThrowIfNull(pairType);
        if (!RegardingRecordUrlRuleApplies(table) || !written.SetsUrl || string.IsNullOrWhiteSpace(written.UrlText))
            return RegardingRecordUrlDecision.Unchanged;
        if (written.Unreadable || !RegardingPairWrite.TryReadRecordUrl(written.UrlText, out _, out _))
            return RegardingRecordUrlDecision.Refuse;
        if (IsThread(table))
            return await DecideThreadRecordUrlAsync(written.UrlText, sentLookups ?? NoSentLookups, storedRow, ct).ConfigureAwait(false);

        var idText = written.SetsId ? written.IdText : null;
        var typeId = written.SetsType ? written.TypeId : null;
        if ((!written.SetsId || !written.SetsType) && storedRow is not null)
        {
            var read = await storedRow(ct).ConfigureAwait(false);
            if (read.Value is not { } stored)
                return new RegardingRecordUrlDecision(null, false, read.Status, read.Failure);
            if (!written.SetsId)
                idText = stored.IdText;
            if (!written.SetsType)
                typeId = stored.TypeId;
        }

        if (!Guid.TryParse(idText?.Trim(), out var pairId) || pairId == Guid.Empty)
            return RegardingRecordUrlDecision.Refuse;

        string? pairTable = null;
        if (typeId is { } type && type != Guid.Empty)
        {
            var named = await pairType(type, ct).ConfigureAwait(false);
            if (named.Value is null)
            {
                return named.Status is 403 or 404
                    ? RegardingRecordUrlDecision.Refuse // a type naming no table this reader can see names no record
                    : new RegardingRecordUrlDecision(null, false, named.Status, named.Failure);
            }

            if (named.Value.Table is not { } namedTable)
                return RegardingRecordUrlDecision.Refuse;
            pairTable = namedTable;
        }

        return Admit(written.UrlText, pairTable, pairId);
    }

    private static RegardingRecordUrlDecision Admit(string? url, string? table, Guid id) =>
        RegardingPairWrite.TryAdmitRecordUrl(url, table, id, out var canonical)
            ? new RegardingRecordUrlDecision(canonical, false, null, null)
            : RegardingRecordUrlDecision.Refuse;

    /// <summary>The thread's half of <see cref="DecideRegardingRecordUrlAsync"/>: the record it is filed under after the write
    /// is its first typed regarding lookup, in ADR-024 priority order, that the write sets, else that the row holds (a lookup the
    /// write clears holds nothing). The stored row is read only when the write does not decide it.</summary>
    private static async Task<RegardingRecordUrlDecision> DecideThreadRecordUrlAsync(
        string? url, IReadOnlyDictionary<string, Guid?> sentLookups,
        Func<CancellationToken, Task<RegardingPairRead<RegardingPairRow>>>? storedRow, CancellationToken ct)
    {
        IReadOnlyDictionary<string, Guid>? stored = null;
        foreach (var (recordTable, column) in Sprk.Bff.Api.Services.Communication.Engine.RegardingFieldMap.All)
        {
            if (!Sprk.Bff.Api.Services.Communication.ThreadResolver.ThreadRegardingColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
                continue;
            if (sentLookups.TryGetValue(column, out var sentId))
            {
                if (sentId is { } id && id != Guid.Empty)
                    return Admit(url, recordTable, id);
                continue; // cleared by this write
            }

            if (stored is null)
            {
                if (storedRow is null)
                {
                    stored = new Dictionary<string, Guid>();
                }
                else
                {
                    var read = await storedRow(ct).ConfigureAwait(false);
                    if (read.Value is not { } row)
                        return new RegardingRecordUrlDecision(null, false, read.Status, read.Failure);
                    stored = row.Lookups;
                }
            }

            if (stored.TryGetValue(column, out var storedId) && storedId != Guid.Empty)
                return Admit(url, recordTable, storedId);
        }

        return RegardingRecordUrlDecision.Refuse; // filed under nothing: the URL names no record of the row
    }

    /// <summary>Task 045f: the thread's typed regarding lookups a mapped item sets (an id) or clears (null); empty elsewhere.</summary>
    internal static IReadOnlyDictionary<string, Guid?> SentThreadLookups(string table, DataverseWriteItemMapper.MappedItem item)
    {
        if (!IsThread(table))
            return NoSentLookups;
        var columns = Sprk.Bff.Api.Services.Communication.ThreadResolver.ThreadRegardingColumns;
        var sent = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);
        foreach (var lookup in item.Lookups.Where(l => columns.Contains(l.Column, StringComparer.OrdinalIgnoreCase)))
            sent[lookup.Column] = lookup.RecordId;
        foreach (var cleared in item.ClearedColumns.Where(c => columns.Contains(c, StringComparer.OrdinalIgnoreCase)))
            sent[cleared] = null;
        return sent;
    }

    /// <summary>Task 045f: the thread's typed regarding lookups a Web API dictionary sets (a reference or a bind path) or clears
    /// (null); empty elsewhere. A value that names no record is a clear.</summary>
    internal static IReadOnlyDictionary<string, Guid?> SentThreadLookups(string table, IEnumerable<KeyValuePair<string, object?>> payload)
    {
        if (!IsThread(table))
            return NoSentLookups;
        var columns = Sprk.Bff.Api.Services.Communication.ThreadResolver.ThreadRegardingColumns;
        var sent = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, raw) in payload)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;
            var column = SecureRootInheritance.NormalizeColumn(key).Trim();
            if (columns.Contains(column, StringComparer.OrdinalIgnoreCase))
                sent[column] = raw is null or JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } ? null : ReferencedId(raw);
        }

        return sent;
    }

    /// <summary>The catalog read AS THE CALLER of <see cref="DecideRegardingRecordUrlAsync"/>: the table a pair type names
    /// (<see cref="CallerWriteAuthorization.PairTypeTableAsync"/>), or the caller's failed read.</summary>
    internal static Func<Guid, CancellationToken, Task<RegardingPairRead<RegardingPairType>>> CallerPairTypeTable(IDataverseUserClient user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return async (typeId, ct) =>
        {
            var (typeRow, pairTable) = await PairTypeTableAsync(user, typeId, null, ct).ConfigureAwait(false);
            return typeRow.IsSuccess
                ? RegardingPairRead<RegardingPairType>.Ok(new RegardingPairType(pairTable, null))
                : new RegardingPairRead<RegardingPairType>(null, typeRow.StatusCode, typeRow);
        };
    }

    /// <summary>Task 045f: the thread's stored typed regarding lookups AS THE CALLER (one read of the row), or the caller's
    /// failed read.</summary>
    private static Func<CancellationToken, Task<RegardingPairRead<RegardingPairRow>>> CallerThreadRow(
        IDataverseUserClient user, string entitySetName, Guid recordId) =>
        async ct =>
        {
            var row = await user.GetAsync($"{entitySetName}({recordId:D})", ct).ConfigureAwait(false);
            if (!row.IsSuccess)
                return new RegardingPairRead<RegardingPairRow>(null, row.StatusCode, row);
            if (row.Body is not { ValueKind: JsonValueKind.Object } body)
            {
                return new RegardingPairRead<RegardingPairRow>(null, row.StatusCode, DataverseUserResponse.Fail(row.StatusCode,
                    DataverseUserClientErrorCodes.BadRequest, "The record could not be read, so what it is filed under cannot be checked."));
            }

            return RegardingPairRead<RegardingPairRow>.Ok(new RegardingPairRow(null, null,
                ThreadLookupsOf(c => body.TryGetProperty($"_{c}_value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null)));
        };

    /// <summary>The thread's typed regarding lookups a row holds, read by <paramref name="valueOf"/> (the column → its id text).</summary>
    private static Dictionary<string, Guid> ThreadLookupsOf(Func<string, string?> valueOf)
    {
        var lookups = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in Sprk.Bff.Api.Services.Communication.ThreadResolver.ThreadRegardingColumns)
        {
            if (Guid.TryParse(valueOf(column), out var id) && id != Guid.Empty)
                lookups[column] = id;
        }

        return lookups;
    }

    /// <summary>
    /// Task 045f (fix round 1, F1c): the stored row AS THE PERSON an app-only path's write runs as (the playbook node or the
    /// output orchestrator impersonating a user), so a sent record URL is decided only on a row that person can read — never
    /// on what the app read, which could tell them about a row they cannot. Null when the write is not run as a person. A row
    /// the person cannot read, or that is not there, answers 403 or 404; any other fault, or no person query, 500 — the path's
    /// read-failed answer, never the URL refusal. The caller's cancellation propagates.
    /// </summary>
    internal static Func<CancellationToken, Task<RegardingPairRead<RegardingPairRow>>>? PersonStoredRowReader(
        Sprk.Bff.Api.Services.Communication.IImpersonatedCommunicationQuery? query, IGenericEntityService? entities,
        Guid? person, string table, Guid recordId)
    {
        if (person is not { } personId || personId == Guid.Empty)
            return null;

        return async ct =>
        {
            if (query is null || entities is null)
                return new RegardingPairRead<RegardingPairRow>(null, 500, null);
            var thread = IsThread(table);
            var select = thread
                ? string.Join(",", Sprk.Bff.Api.Services.Communication.ThreadResolver.ThreadRegardingColumns.Select(c => $"_{c}_value"))
                : $"{SecureRootInheritance.PairIdColumn},_{SecureRootInheritance.PairTypeColumn}_value";
            try
            {
                var entitySet = await entities.GetEntitySetNameAsync(table, ct).ConfigureAwait(false);
                var rows = await query.QueryAsync(
                    entitySet, $"$select={select}&$filter={table.Trim().ToLowerInvariant()}id eq {recordId:D}", personId, ct)
                    .ConfigureAwait(false);
                if (rows.Count == 0)
                    return new RegardingPairRead<RegardingPairRow>(null, 404, null);
                var row = rows[0];
                string? Text(string column) =>
                    row.TryGetValue(column, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                return RegardingPairRead<RegardingPairRow>.Ok(thread
                    ? new RegardingPairRow(null, null, ThreadLookupsOf(c => Text($"_{c}_value")))
                    : new RegardingPairRow(
                        Text(SecureRootInheritance.PairIdColumn),
                        Guid.TryParse(Text($"_{SecureRootInheritance.PairTypeColumn}_value"), out var type) && type != Guid.Empty ? type : null,
                        new Dictionary<string, Guid>()));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                var status = ex is HttpRequestException { StatusCode: { } code } ? (int)code : 500;
                return new RegardingPairRead<RegardingPairRow>(null, status is 403 or 404 ? status : 500, null);
            }
        };
    }

    /// <summary>
    /// Task 045f on a CALLER update path's mapped item (the child-record PATCH, the chat update tool), after the update planner:
    /// <see cref="DecideRegardingRecordUrlAsync"/> with the stored row and the catalog read as the caller. An admitted URL is set
    /// in canonical form; a refusal is the path's existing invalid-pair form with <see cref="RegardingPairWrite.RecordUrlMismatchCode"/>;
    /// a failed read is the caller's own failure. Nothing is written on either.
    /// </summary>
    internal static async Task<(DataverseWriteItemMapper.MappedItem Item, RefileOutcome? Refusal)> KeepRegardingRecordUrlAsync(
        IDataverseUserClient user, string table, string entitySetName, Guid recordId, DataverseWriteItemMapper.MappedItem item,
        CancellationToken ct)
    {
        var written = PairWriteOf(item);
        if (!RegardingRecordUrlRuleApplies(table) || !written.SetsUrl)
            return (item, null);

        var decided = await DecideRegardingRecordUrlAsync(
                table, written,
                IsThread(table) ? CallerThreadRow(user, entitySetName, recordId) : CallerReaders(user, table, entitySetName, recordId).Row,
                CallerPairTypeTable(user), ct, SentThreadLookups(table, item))
            .ConfigureAwait(false);
        if (decided.Refused)
            return (item, new RefileOutcome { Invalid = RegardingRecordUrlRefusalText });
        if (decided.Failed)
        {
            return (item, new RefileOutcome
            {
                ClientFailure = decided.Failure ?? DataverseUserResponse.Fail(decided.FailedStatus!.Value,
                    DataverseUserClientErrorCodes.BadRequest, "The record could not be read, so what it is filed under cannot be checked."),
            });
        }

        return decided.CanonicalUrl is { } canonical && !string.Equals(written.UrlText, canonical, StringComparison.Ordinal)
            ? (WithColumnsSet(table, item, new Dictionary<string, object?> { [RegardingPairWrite.RecordUrlColumn] = canonical }), null)
            : (item, null);
    }

    /// <summary>
    /// Task 045f on an APP-ONLY path's payload (the playbook Update Record node, the output orchestrator), after the update
    /// planner: <see cref="DecideRegardingRecordUrlAsync"/> with the stored row and the catalog read app-only (a row that does
    /// not exist holds nothing) — or, on a write run as a person, the stored row read as that person
    /// (<paramref name="storedRowAsPerson"/>, <see cref="PersonStoredRowReader"/>), where a row they cannot read or that is not
    /// there is the read-failed answer. An admitted URL is set in canonical form; a refusal is thrown with
    /// <see cref="RegardingPairWrite.RecordUrlMismatchCode"/>, a failed read as the planner's own refusal. Nothing is written on
    /// either.
    /// </summary>
    /// <exception cref="RecordOwnerUnresolvedException">The URL is refused, or a read failed.</exception>
    internal static async Task<Dictionary<string, object?>> KeepRegardingRecordUrlAppOnlyAsync(
        IGenericEntityService? entities, string table, Guid recordId, Dictionary<string, object?> payload, CancellationToken ct,
        Func<CancellationToken, Task<RegardingPairRead<RegardingPairRow>>>? storedRowAsPerson = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var written = RegardingPairWrite.Of(table, payload);
        if (!RegardingRecordUrlRuleApplies(table) || !written.SetsUrl || string.IsNullOrWhiteSpace(written.UrlText))
            return payload;

        if (entities is null)
        {
            throw new RecordOwnerUnresolvedException(table, RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUndetermined,
                "what it is filed under cannot be read here, so it was not written"));
        }

        var readers = AppOnlyReaders(entities, table, recordId);
        var appOnlyRow = readers.Row;
        if (IsThread(table))
        {
            appOnlyRow = async token =>
            {
                var read = await AppOnlyAsync(() => entities.RetrieveAsync(
                    ThreadTable, recordId, Sprk.Bff.Api.Services.Communication.ThreadResolver.ThreadRegardingColumns, token), token)
                    .ConfigureAwait(false);
                return read.Value is { } row
                    ? RegardingPairRead<RegardingPairRow>.Ok(new RegardingPairRow(null, null,
                        ThreadLookupsOf(c => row.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>(c)?.Id.ToString("D"))))
                    : new RegardingPairRead<RegardingPairRow>(null, read.Status, null);
            };
        }

        var decided = await DecideRegardingRecordUrlAsync(
                table, written,
                storedRowAsPerson ?? (async token => await appOnlyRow(token).ConfigureAwait(false) is var row && row is { Value: null, Status: 404 }
                    ? RegardingPairRead<RegardingPairRow>.Ok(new RegardingPairRow(null, null, new Dictionary<string, Guid>()))
                    : row),
                readers.Type, ct, SentThreadLookups(table, payload))
            .ConfigureAwait(false);
        if (decided.Refused)
            throw new RecordOwnerUnresolvedException(table, RegardingPairWrite.RecordUrlMismatchCode, $"The update was NOT written: {RegardingRecordUrlRefusalText}.");
        if (decided.Failed)
        {
            throw new RecordOwnerUnresolvedException(table, RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUndetermined,
                "what it is filed under could not be read, so it was not written"));
        }

        return decided.CanonicalUrl is { } canonical && !string.Equals(written.UrlText, canonical, StringComparison.Ordinal)
            ? WithColumnsSet(table, payload, new Dictionary<string, object?> { [RegardingPairWrite.RecordUrlColumn] = canonical })
            : payload;
    }

    /// <summary>The refusal text of <see cref="RegardingPairWrite.RecordUrlMismatchCode"/>: its fixed reason and the code.</summary>
    internal static string RegardingRecordUrlRefusalText =>
        $"{RegardingPairWrite.RecordUrlMismatchReason} ({RegardingPairWrite.RecordUrlMismatchCode})";

    /// <summary><paramref name="item"/> without <paramref name="columns"/> — its body keys (a column, or a lookup's
    /// <c>{nav}@odata.bind</c>), columns, lookups and clears — so planned columns replace the item's own.</summary>
    private static DataverseWriteItemMapper.MappedItem WithoutColumns(DataverseWriteItemMapper.MappedItem item, IEnumerable<string> columns)
    {
        var drop = columns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var body = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        using (var doc = JsonDocument.Parse(item.JsonBody))
        {
            foreach (var property in doc.RootElement.EnumerateObject().Where(p => !drop.Contains(SecureRootInheritance.NormalizeColumn(p.Name))))
                body[property.Name] = property.Value.Clone();
        }

        return item with
        {
            JsonBody = JsonSerializer.Serialize(body),
            Columns = item.Columns.Where(c => !drop.Contains(SecureRootInheritance.NormalizeColumn(c))).ToArray(),
            Lookups = item.Lookups.Where(l => !drop.Contains(l.Column)).ToArray(),
            ClearedColumns = item.ClearedColumns.Where(c => !drop.Contains(c)).ToArray(),
        };
    }

    /// <summary>
    /// Task 045d: <paramref name="item"/> with <paramref name="columns"/> set over its own (the invoice family's five resolver
    /// fields, <see cref="InvoiceFamilyResolverFields.ToFields"/>): each column the item already writes, binds or clears is
    /// replaced. A text value is written as is; the pair type (an <see cref="Microsoft.Xrm.Sdk.EntityReference"/> to its catalog
    /// row) is bound on the table's own navigation property (<see cref="RegardingRecordTypeNavigationPropertyOf"/>, pinned
    /// against the solution) and listed as a lookup, as the mapper lists one. Pure: no read.
    /// </summary>
    /// <exception cref="ArgumentException">A lookup other than the pair type, or a value that is neither text nor a reference.</exception>
    internal static DataverseWriteItemMapper.MappedItem WithColumnsSet(
        string table, DataverseWriteItemMapper.MappedItem item, IReadOnlyDictionary<string, object?> columns)
    {
        if (columns.Count == 0)
            return item;

        var kept = WithoutColumns(item, columns.Keys);
        var body = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        using (var doc = JsonDocument.Parse(kept.JsonBody))
        {
            foreach (var property in doc.RootElement.EnumerateObject())
                body[property.Name] = property.Value.Clone();
        }

        var lookups = kept.Lookups.ToList();
        foreach (var (key, value) in columns)
        {
            var column = key.Trim().ToLowerInvariant();
            switch (value)
            {
                case Microsoft.Xrm.Sdk.EntityReference type when IsPairType(column)
                                                                && string.Equals(type.LogicalName, RecordTypeTable, StringComparison.OrdinalIgnoreCase):
                    body[$"{RegardingRecordTypeNavigationPropertyOf(table)}@odata.bind"] =
                        JsonSerializer.SerializeToElement($"/{RecordTypeEntitySet}({type.Id:D})");
                    lookups.Add(new DataverseWriteItemMapper.MappedLookup(column, RecordTypeTable, RecordTypeEntitySet, type.Id));
                    break;
                case string text:
                    body[column] = JsonSerializer.SerializeToElement(text);
                    break;
                case null:
                    body[column] = JsonSerializer.SerializeToElement<string?>(null); // a clear (option (b): a blank name or number)
                    break;
                default:
                    throw new ArgumentException($"'{column}' cannot be set here: only text and the pair type are.", nameof(columns));
            }
        }

        return new DataverseWriteItemMapper.MappedItem(
            JsonSerializer.Serialize(body),
            kept.Columns.Concat(columns.Keys.Select(k => k.Trim().ToLowerInvariant())).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
        {
            Lookups = lookups,
            ClearedColumns = kept.ClearedColumns,
        };
    }

    /// <summary>
    /// Task 045d: the app-only paths' shape of the same helper — a copy of <paramref name="payload"/> (the playbook update's, the
    /// output orchestrator's or the push's dictionary) with <paramref name="columns"/> set over its own, in the form the app-only
    /// pair planner writes (<see cref="KeepRegardingPairWholeAppOnlyAsync"/>): every key the payload holds for one of the columns
    /// (a column, or its <c>{nav}@odata.bind</c>) is replaced; the pair type is bound on the table's own navigation property;
    /// text and null (a clear) are written as they are. The caller's dictionary is not changed. Pure: no read.
    /// </summary>
    /// <exception cref="ArgumentException">A lookup other than the pair type, or a value that is neither text nor a reference.</exception>
    internal static Dictionary<string, object?> WithColumnsSet(
        string table, Dictionary<string, object?> payload, IReadOnlyDictionary<string, object?> columns)
    {
        var written = new Dictionary<string, object?>(payload, payload.Comparer);
        if (columns.Count == 0)
            return written;

        var drop = columns.Keys.Select(k => k.Trim().ToLowerInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in written.Keys.Where(k => drop.Contains(SecureRootInheritance.NormalizeColumn(k))).ToList())
            written.Remove(key);
        foreach (var (key, value) in columns)
        {
            var column = key.Trim().ToLowerInvariant();
            switch (value)
            {
                case Microsoft.Xrm.Sdk.EntityReference type when IsPairType(column)
                                                                && string.Equals(type.LogicalName, RecordTypeTable, StringComparison.OrdinalIgnoreCase):
                    written[$"{RegardingRecordTypeNavigationPropertyOf(table)}@odata.bind"] = $"/{RecordTypeEntitySet}({type.Id:D})";
                    break;
                case string or null:
                    written[column] = value;
                    break;
                default:
                    throw new ArgumentException($"'{column}' cannot be set here: only text and the pair type are.", nameof(columns));
            }
        }

        return written;
    }

    /// <summary>
    /// Task 045d (owner ruling Q8 (a); owner, 2026-11-12, option (b)): the app-only update half on one path — the five resolver
    /// fields of the core record an invoice-family row will name (<see cref="InvoiceFamilyCoreRecordGuard.DeriveForGenericUpdateAsync"/>:
    /// the stored row and the catalog app-only, the name and number through <paramref name="displayRecord"/>) and set over
    /// <paramref name="payload"/> for the same write
    /// (<see cref="WithColumnsSet(string, Dictionary{string, object?}, IReadOnlyDictionary{string, object?})"/>).
    /// The payload unchanged when nothing is derived; the guard's refusal (its code) when the derive cannot be done, which the
    /// path answers through the refusal form it uses for <see cref="InvoiceFamilyCoreRecordGuard.CheckGenericUpdateAsync"/>.
    /// </summary>
    /// <param name="displayRecord">The path's display read, the one its pair planner uses for a moved pair: the push's caller
    /// (<see cref="CallerDisplayReader"/>), or on the two handlers the person the run acts as (<see cref="MovedDisplayReader"/>,
    /// which answers 403 with no person, so the name and number are cleared). Through <see cref="CallerDisplayRecords"/>.
    /// Required, so no path falls back to an app-only read of them.</param>
    internal static async Task<(Dictionary<string, object?> Payload, string? RefusalCode)> KeepInvoiceFamilyResolverFieldsAppOnlyAsync(
        IGenericEntityService? entities, string table, Guid recordId, Dictionary<string, object?> payload, string path,
        Func<string, Guid, IReadOnlyList<string>, CancellationToken, Task<RegardingPairRead<IReadOnlyDictionary<string, string?>>>> displayRecord,
        ILogger? logger, CancellationToken ct, bool writeIsImpersonated = false)
    {
        ArgumentNullException.ThrowIfNull(displayRecord);

        // Task 045e (fix round 1, K1/K2/F2): first the regarding-pair gate on the payload as it will be written (after the 045b
        // planner the path ran just before): a pair and its display are removed when the core record moves (only the derive
        // below writes them), and otherwise must name the row's core record, its id written in canonical form.
        var gated = await InvoiceFamilyCoreRecordGuard.KeepRegardingPairAgreeingAsync(table, recordId, payload, entities, ct,
                writeIsImpersonated)
            .ConfigureAwait(false);
        if (gated.Refusal is { } pairRefusal)
            return (payload, pairRefusal.RefusalCode ?? InvoiceFamilyCoreRecordGuard.ReadFailedCode);
        payload = gated.Payload;

        var derived = await InvoiceFamilyCoreRecordGuard.DeriveForGenericUpdateAsync(table, recordId, payload, entities,
                CallerDisplayRecords(entities, displayRecord), ct, writeIsImpersonated)
            .ConfigureAwait(false);
        if (derived is null)
            return (payload, null);
        if (derived is not { IsRefused: false, ResolverFields: { } fields })
            return (payload, derived.RefusalCode ?? InvoiceFamilyCoreRecordGuard.ReadFailedCode);

        logger?.LogInformation(
            "[{Path}] the resolver fields of {Table} {RecordId} were derived from the core record it will name", path, table, recordId);
        return (WithColumnsSet(table, payload, fields.ToGenericFields()), null);
    }

    /// <summary>
    /// Task 045d (fix rounds 3-5): a generic writer's record reader for the derive — the name and number as the person the
    /// write is for sees them (<paramref name="displayRecord"/>): the push's caller, the person a handler's run acts as (as the
    /// 045b planner reads a moved pair's display values on that path), or the caller of a create (<see cref="CallerRecordDisplay"/>).
    /// A record they may not read, or cannot find (403 or 404), is checked to exist app-only: it exists, so its name and number
    /// are cleared (owner, 2026-11-12, option (b)); it does not, so the derive refuses as for any missing record. Any other
    /// failed read, or no app-only reader for that check, throws, which the derive refuses as a read failure.
    /// </summary>
    internal static InvoiceFamilyCoreRecordGuard.RecordLookup CallerDisplayRecords(
        IGenericEntityService? entities,
        Func<string, Guid, IReadOnlyList<string>, CancellationToken, Task<RegardingPairRead<IReadOnlyDictionary<string, string?>>>> displayRecord)
    {
        ArgumentNullException.ThrowIfNull(displayRecord);
        return async (table, id, columns, ct) =>
        {
            var shown = await displayRecord(table, id, columns, ct).ConfigureAwait(false);
            if (shown.Value is { } values)
            {
                var row = new Microsoft.Xrm.Sdk.Entity(table, id);
                foreach (var column in columns)
                    row[column] = values.TryGetValue(column, out var value) ? value : null;
                return row;
            }

            if (shown.Status is 403 or 404 && entities is not null)
            {
                return await entities.RetrieveAsync(table, id, columns, ct).ConfigureAwait(false) is null
                    ? null
                    : new Microsoft.Xrm.Sdk.Entity(table, id);
            }

            throw new InvalidOperationException("The record's name and number could not be read as the caller.");
        };
    }

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
            return new RefileOutcome
            {
                ClientFailure = DataverseUserResponse.Fail(row.StatusCode, DataverseUserClientErrorCodes.BadRequest,
                "The record could not be read, so what it is filed under cannot be checked.")
            };
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

        /// <summary>The ADR-024 pair the update leaves disagrees with itself (<see cref="Outcome.Invalid"/>), or the update names a
        /// column fixed after create (<c>RecordTableRule.FixedAfterCreateColumns</c>) — nothing written.</summary>
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
        IGenericEntityService? appOnlyEntities = null,
        Sprk.Bff.Api.Services.Communication.Access.DirectMessageOwnerStep? directOwner = null)
    {
        // The core record pair is fixed after create on BFF update paths (RecordTableRule.FixedAfterCreateColumns): asked on the
        // item's shape alone, before any read or write, so the re-file core holds it for any table a caller passes. Nothing written.
        if (Sprk.Bff.Api.Services.RecordCreation.RecordTableRules.RefuseFixedAfterCreateColumns(table, item.Columns) is { } fixedRefusal)
            return new RefileOutcome { Invalid = $"{fixedRefusal.Reason} ({fixedRefusal.RefusalCode})" };

        // Task 048 fix round 1 (K1): a Signal, a Decision Record and a Budget Revision are changed only by their writers
        // (RecordTableRule.CreatedOnlyByItsWriter); asked on the table alone, before any read or write, so the re-file core
        // holds it for any table a caller passes. Nothing written.
        if (Sprk.Bff.Api.Services.RecordCreation.RecordTableRules.RefuseWriterOnlyTableUpdate(table) is { } writerOnly)
            return new RefileOutcome { Invalid = writerOnly.Reason };

        // ebilling-r1 task 165d fix round (EB-D-207; uac-r2 U1 option A): and a column only the table's typed writer sets (the invoice's AP
        // export claim), on the item's shape alone, before any read or write. The re-file core applies the caller's own PATCH, so only the
        // column check is asked here, never RefuseAppOnlyWrite. Nothing written.
        if (Sprk.Bff.Api.Services.RecordCreation.RecordTableRules.RefuseWriterOnlyColumns(table, item.Columns) is { } writerOnlyColumn)
            return new RefileOutcome { Invalid = $"{writerOnlyColumn.Reason} ({writerOnlyColumn.RefusalCode})" };

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
                    // fm task 054 piece 3b: the one unfile rule every re-file writer takes (an eligible Direct message returns
                    // to the Direct team; any other communication linked to nothing goes to its creator's team or keeps its owner, #2722).
                    WhenUnfiled = Sprk.Bff.Api.Services.Communication.Access.DirectMessageOwnerStep.UnfiledOwnershipFor(
                        table, actsForAPerson: true, directOwner),
                    // Piece 3b: the typed regarding lookups the update sets that file nothing (a contact, an account): a message
                    // still linked to one after the update is not unfiled.
                    RegardingLinksSet = RecordReparent.RegardingLinksSetIn(
                        table,
                        item.Lookups.Select(l => KeyValuePair.Create(
                            l.Column, (object?)new Microsoft.Xrm.Sdk.EntityReference(l.RelatedTable, l.RecordId)))),
                    // Piece 3b: and every other column the unfile rule reads that the update writes (its type, its stamped pair),
                    // so the rule judges the row as the update leaves it.
                    UnfileRuleColumnsWritten = RecordReparent.UnfileRuleColumnsWrittenIn(table, WrittenFieldsOf(item)),
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

            // Piece 3b: an unfiled Direct message returns to the Direct team now, before the host's step after the re-file.
            if (directOwner is not null && !resolution.IsRefused)
                await directOwner.AfterReparentAsync(recordId, resolution, CancellationToken.None).ConfigureAwait(false);

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

    /// <summary>
    /// fm task 054 piece 3b: every column a mapped update writes, as a generic payload: each lookup it binds as an
    /// <see cref="Microsoft.Xrm.Sdk.EntityReference"/>, each column it clears as <c>null</c>, and each other column of its body as
    /// the body holds it. The input of <see cref="RecordReparent.UnfileRuleColumnsWrittenIn{TValue}"/>. A body that is not a JSON
    /// object adds no plain column.
    /// </summary>
    internal static IEnumerable<KeyValuePair<string, object?>> WrittenFieldsOf(DataverseWriteItemMapper.MappedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var fields = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var body = System.Text.Json.JsonDocument.Parse(item.JsonBody);
            if (body.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var property in body.RootElement.EnumerateObject())
                {
                    if (!property.Name.EndsWith("@odata.bind", StringComparison.OrdinalIgnoreCase))
                        fields[property.Name] = property.Value.Clone();
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Not a JSON object: only the lookups and clears the mapper read are written columns here.
        }

        foreach (var column in item.ClearedColumns)
            fields[column] = null;
        foreach (var lookup in item.Lookups)
            fields[lookup.Column] = new Microsoft.Xrm.Sdk.EntityReference(lookup.RelatedTable, lookup.RecordId);
        return fields;
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
    /// (<see cref="Sprk.Bff.Api.Services.RecordCreation.RecordCreationService"/>'s pre-write step, since task 017 replaced the
    /// owned create that called it here) and the update / re-file (<see cref="RefileAsync"/>) — invokes
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
        (string EntitySetName, Guid RecordId)? stored, CancellationToken ct, IGenericEntityService? appOnlyEntities = null) =>
        (await DecideInvoiceFamilyAsync(user, table, item, stored, ct, appOnlyEntities).ConfigureAwait(false)).Refusal;

    /// <summary>
    /// <see cref="CheckInvoiceFamilyAsync"/>'s answer with the record the guard decided when it admits the row (task 045d: the
    /// create derives the five resolver fields from it). <see cref="InvoiceFamilyDecision.Admitted"/> is set only when the guard
    /// ran and admitted; both are <c>null</c> when it had nothing to decide (a table outside the family, an update that leaves
    /// every guarded lookup as stored).
    /// </summary>
    internal readonly record struct InvoiceFamilyDecision(InvoiceFamilyStep? Refusal, InvoiceFamilyCoreRecord? Admitted)
    {
        public static InvoiceFamilyDecision Proceed => default;

        public static InvoiceFamilyDecision Refused(InvoiceFamilyStep refusal) => new(refusal, null);
    }

    /// <summary>Task 045e: what <paramref name="item"/> sends of the regarding pair, for the guard's agreement check.</summary>
    internal static RegardingPairWrite PairWriteOf(DataverseWriteItemMapper.MappedItem item)
    {
        var pair = PairOf(item);
        var written = new RegardingPairWrite(pair.SetsId, pair.IdText, pair.SetsType, pair.TypeId, pair.InvalidId);
        using var body = JsonDocument.Parse(item.JsonBody);
        foreach (var property in body.RootElement.EnumerateObject())
        {
            if (!string.Equals(property.Name, RegardingPairWrite.RecordUrlColumn, StringComparison.OrdinalIgnoreCase))
                continue;
            written = written with
            {
                SetsUrl = true,
                UrlText = property.Value.ValueKind switch
                {
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    JsonValueKind.String => property.Value.GetString(),
                    _ => property.Value.GetRawText(),
                },
            };
        }

        return written;
    }

    /// <summary>See <see cref="CheckInvoiceFamilyAsync"/>; also returns the admitted decision (task 045d).</summary>
    /// <param name="recordTypes">Task 045e: the APP-ONLY catalog reader the guard's regarding-pair agreement check reads the
    /// type with (<see cref="InvoiceFamilyCoreRecordGuard.CheckRegardingPairAgreesAsync"/>): the create's own; on an update,
    /// <see cref="InvoiceFamilyCoreRecordGuard.AppOnlyRecordTypes"/> over <paramref name="appOnlyEntities"/> when not given.
    /// None fails closed when the type is checked.</param>
    internal static async Task<InvoiceFamilyDecision> DecideInvoiceFamilyAsync(
        IDataverseUserClient user, string table, DataverseWriteItemMapper.MappedItem item,
        (string EntitySetName, Guid RecordId)? stored, CancellationToken ct, IGenericEntityService? appOnlyEntities = null,
        InvoiceFamilyCoreRecordGuard.RecordTypeLookup? recordTypes = null)
    {
        var guarded = InvoiceFamilyCoreRecordGuard.GuardedLookups(table);
        if (guarded is null)
            return InvoiceFamilyDecision.Proceed;

        recordTypes ??= appOnlyEntities is null ? null : InvoiceFamilyCoreRecordGuard.AppOnlyRecordTypes(appOnlyEntities);
        var pair = PairWriteOf(item);
        var guardedColumns = guarded.Select(g => g.Column).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var written = item.Columns.Concat(item.ClearedColumns).Concat(item.Lookups.Select(l => l.Column))
            .Where(guardedColumns.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (stored is { } pairOnly && written.Count == 0)
        {
            // An update that leaves every guarded lookup as stored: only the regarding pair it sends is decided (task 045e), on
            // the stored core lookups read as the caller; the guard's other questions do not change and are not asked.
            return pair.DecidesAnything
                ? await DecidePairOnlyUpdateAsync(user, table, pairOnly, pair, recordTypes, ct).ConfigureAwait(false)
                : InvoiceFamilyDecision.Proceed;
        }

        var notALookup = written.FirstOrDefault(c =>
            !item.Lookups.Any(l => string.Equals(l.Column, c, StringComparison.OrdinalIgnoreCase))
            && !item.ClearedColumns.Contains(c, StringComparer.OrdinalIgnoreCase));
        if (notALookup is not null)
        {
            return InvoiceFamilyDecision.Refused(new InvoiceFamilyStep(InvoiceFamilyCoreRecord.Refused(table, InvoiceFamilyCoreRecordGuard.LookupNotACoreRecordCode,
                $"{table}.{notALookup} is a lookup; it is set by a bind or cleared, never written as a value. Nothing was written."),
                null, false));
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
                return InvoiceFamilyDecision.Refused(new InvoiceFamilyStep(null, failure, false));
            if (read.Values is not { } values)
                return InvoiceFamilyDecision.Refused(new InvoiceFamilyStep(FiledUnderUnreadable(table, read.Problem), null, false));

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
            return InvoiceFamilyDecision.Refused(new InvoiceFamilyStep(null, null, true));
        if (supersededFailure is { } readFailure)
            return InvoiceFamilyDecision.Refused(new InvoiceFamilyStep(null, readFailure, false));
        if (decided.IsRefused)
            return InvoiceFamilyDecision.Refused(new InvoiceFamilyStep(decided, null, false));

        // Task 045e: the regarding pair the write sends agrees with the core record the row will name. On a create, before step
        // 5½'s derive (which writes the pair from the core record). On an update, after the planner and 045d's derive, so a
        // core-record move is checked too (fix round 1, K1): the pair is the derived one, or — when the derive found nothing to
        // move — the one sent, which must then name the record the row will name.
        var disagrees = await InvoiceFamilyCoreRecordGuard.CheckRegardingPairAgreesAsync(table, written, pair, decided,
                stored is null ? null : InvoiceFamilyCoreRecordGuard.Check(table, column => before.TryGetValue(column, out var kept) ? kept : null),
                recordTypes, ct, aCoreMoveIsChecked: stored is not null)
            .ConfigureAwait(false);
        return disagrees is not null
            ? InvoiceFamilyDecision.Refused(new InvoiceFamilyStep(disagrees, null, false))
            : new InvoiceFamilyDecision(null, decided);
    }

    /// <summary>
    /// Task 045e: an update that writes the regarding pair and no guarded lookup — the stored core lookups, read as the caller
    /// (the guard's own read on this path), decide the record the pair must name. A read that fails is the caller's own failure
    /// or <see cref="InvoiceFamilyCoreRecordGuard.ReadFailedCode"/>, never "agrees". Admitted is <see cref="InvoiceFamilyDecision.Proceed"/>
    /// (the guard decided nothing new about the row's core record).
    /// </summary>
    private static async Task<InvoiceFamilyDecision> DecidePairOnlyUpdateAsync(
        IDataverseUserClient user, string table, (string EntitySetName, Guid RecordId) stored, RegardingPairWrite pair,
        InvoiceFamilyCoreRecordGuard.RecordTypeLookup? recordTypes, CancellationToken ct)
    {
        var coreColumns = InvoiceFamilyCoreRecordGuard.ColumnsToRead(table)!;
        var columns = InvoiceFamilyCoreRecordGuard.GuardedLookups(table)!
            .Where(g => coreColumns.Contains(g.Column, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        StoredLookups read;
        try
        {
            read = await ReadLookupsAsCallerAsync(user, stored.EntitySetName, stored.RecordId, columns, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            read = new StoredLookups(null, null, ex.GetType().Name);
        }

        if (read.Failure is { } failure)
            return InvoiceFamilyDecision.Refused(new InvoiceFamilyStep(null, failure, false));
        if (read.Values is not { } values)
            return InvoiceFamilyDecision.Refused(new InvoiceFamilyStep(FiledUnderUnreadable(table, read.Problem), null, false));

        var kept = InvoiceFamilyCoreRecordGuard.Check(table, column => values.TryGetValue(column, out var value) ? value : null);
        var disagrees = await InvoiceFamilyCoreRecordGuard.CheckRegardingPairAgreesAsync(
                table, Array.Empty<string>(), pair, kept, kept, recordTypes, ct)
            .ConfigureAwait(false);
        return disagrees is not null
            ? InvoiceFamilyDecision.Refused(new InvoiceFamilyStep(disagrees, null, false))
            : InvoiceFamilyDecision.Proceed;
    }

    /// <summary>The guard's refusal when a family row's stored lookups, read as the caller, could not be used.</summary>
    private static InvoiceFamilyCoreRecord FiledUnderUnreadable(string table, string? problem) =>
        InvoiceFamilyCoreRecord.Refused(table, InvoiceFamilyCoreRecordGuard.ReadFailedCode,
            $"What this {table} is filed under could not be read ({problem}); nothing was written.");

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
