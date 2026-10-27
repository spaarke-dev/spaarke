using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using static Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// The caller checks of the app-only write path, asked AS THE CALLER through <see cref="IDataverseUserClient"/> under their
/// own token (ADR-028): whether they could create a row themselves (<see cref="CheckCallerMayCreateAsync"/>), AppendTo on
/// every record a lookup or the ADR-024 regarding pair names (<see cref="CheckCallerMayAppendToAsync"/>,
/// <see cref="CheckCallerMayAppendToRecordAsync"/>, and the organization-owned and business-owned answers), what the pair
/// names (<see cref="PairTargetAsync"/>), what an update files a root under (<see cref="CheckCallerMayFileRootUnderAsync"/>),
/// their rights on a record (<see cref="RightsOnAsync"/>) and who they are (<see cref="WhoAmIAsync"/>). Any failure denies.
/// </summary>
/// <remarks>
/// <para><b>Moved unchanged from <see cref="OwnedChildWrite"/></b> (field-mapping-server-write-path-r1 task 011, spec FR-08
/// move part): the same checks in the same order, with the same messages and the same uniform not-found. The "class
/// remarks" <see cref="CheckCallerMayCreateAsync"/> points to — the four questions "as the user" covers — are
/// <see cref="OwnedChildWrite"/>'s, beside the G5 create they guard. The answer shapes (<c>Outcome</c>,
/// <c>RefileOutcome</c>), the payload readers shared with the create and re-file halves (<c>ServerOwnedColumns</c>,
/// <c>PairOf</c>, <c>BffManagedPairColumnsOf</c>, <c>PairAgreementColumns</c>, <c>IsPairType</c>) and the re-file
/// half's own caller checks (<c>CheckCallerMayRefileByPairAsync</c>, <c>CallerMayFileRootUnderSecure</c>) stay in
/// <see cref="OwnedChildWrite"/>, as does <c>RefileAsync</c> (unified-access-control-r2's). The two files import each
/// other with <c>using static</c>, so every moved body reads exactly as it did. A member declared in a class wins over
/// an imported one, so never give a member of either class the name of a member of the other: the calls in that file
/// would bind to it silently.</para>
/// <para><b>ADR-008, path A</b> (this project's spec, "ADR Tensions"): route-level authorization stays in endpoint filters;
/// these checks depend on the request body and also serve non-HTTP callers (chat tools, decision cores), so the write
/// cores call them. <b>ADR-010:</b> a static class — no DI registration, no interface.</para>
/// <para><b>Component justification (CLAUDE.md §11).</b> Existing — the helpers lived only inside
/// <see cref="OwnedChildWrite"/>, a file that mixes create, re-file and pair rules. Extension — they are moved, not copied.
/// Cost of doing nothing — the unified create service (task 013) would either reference <see cref="OwnedChildWrite"/>
/// (circular once its create half moves out) or copy the checks, leaving two authorization cores on the security
/// boundary.</para>
/// </remarks>
internal static class CallerWriteAuthorization
{
    /// <summary>
    /// AS THE CALLER, whether they could create this row themselves (see the class remarks for the four questions).
    /// </summary>
    internal static async Task<Outcome> CheckCallerMayCreateAsync(
        IDataverseUserClient user,
        Guid me,
        string table,
        DataverseWriteItemMapper.MappedItem item,
        IReadOnlySet<string>? serverSetLookupColumns,
        CancellationToken ct)
    {
        // The creator person (task 133 / 146 c1-r1) is the server's to stamp, like the owner: a caller never chooses who
        // created a record.
        var serverOwned = item.Columns.FirstOrDefault(c => ServerOwnedColumns.Contains(c) || RecordCreatorPerson.NamesColumn(c));
        if (serverOwned is not null)
        {
            return new Outcome
            {
                Denied = $"Column '{serverOwned}' is set by the server on a record the application creates for you — omit it.",
            };
        }

        // (1) The table's Create (and Append, when the row sets a lookup) privilege, by the names its metadata declares.
        var metadata = await user.GetAsync($"EntityDefinitions(LogicalName='{table}')?$select=LogicalName,Privileges", ct)
            .ConfigureAwait(false);
        if (!metadata.IsSuccess)
            return new Outcome { ClientFailure = metadata };

        var needed = new List<string>();
        if (PrivilegeNamed(metadata.Body, "Create", 1) is not { } create)
            return new Outcome { Denied = $"Table '{table}' declares no Create privilege; it cannot be created here." };
        needed.Add(create);
        if (item.Lookups.Count > 0)
        {
            if (PrivilegeNamed(metadata.Body, "Append", 7) is not { } append)
                return new Outcome { Denied = $"Table '{table}' declares no Append privilege; its lookups cannot be set here." };
            needed.Add(append);
        }

        var names = Uri.EscapeDataString(JsonSerializer.Serialize(needed));
        var held = await user.GetAsync(
            $"systemusers({me:D})/Microsoft.Dynamics.CRM.RetrieveUserSetOfPrivilegesByNames(PrivilegeNames=@p1)?@p1={names}", ct)
            .ConfigureAwait(false);
        if (!held.IsSuccess)
            return new Outcome { ClientFailure = held };

        var heldBody = held.Body?.GetRawText();
        var missing = needed.FirstOrDefault(name => !CallerRecordAccessProbe.ResponseGrantsPrivilege(heldBody, name));
        if (missing is not null)
            return new Outcome { Denied = $"You do not have permission to create records in '{table}' ({missing})." };

        // (3) No column under field-level security: an app-only write would pass the caller's column security. #1616: except
        // a BFF-managed pair (SecureRootInheritance.BffManagedPairTables) — it is secured so that ONLY this server writes it,
        // and what this server asks before writing it is the pair rule in (2) below (AppendTo AS THE CALLER on the record it
        // names), so the column security it would pass is the one that forces every write of it through here.
        var bffManaged = BffManagedPairColumnsOf(table);
        var asked = item.Columns.Where(c => !bffManaged.Contains(c)).ToArray();
        var columnFilter = string.Join(" or ", asked.Select(c => $"LogicalName eq '{c}'"));
        if (asked.Length > 0 || item.Columns.Count == 0)
        {
            var attributes = await user.GetAsync(
                $"EntityDefinitions(LogicalName='{table}')/Attributes?$select=LogicalName,IsSecured&$filter={Uri.EscapeDataString(columnFilter)}", ct)
                .ConfigureAwait(false);
            if (!attributes.IsSuccess)
                return new Outcome { ClientFailure = attributes };

            if (SecuredColumnIn(attributes.Body) is { } secured)
            {
                return new Outcome
                {
                    Denied = $"Column '{secured}' is field-secured and cannot be set on a record the application creates for you.",
                };
            }
        }

        // (2) AppendTo on every record a lookup names (the server-set ones name the caller) and on the record the ADR-024
        // pair names, whether or not it is secure (the pair's readers file the row under it): a missing, unreadable or
        // unappendable one is the same not-found as an unappendable lookup. The pair's record is ALWAYS asked — never
        // skipped because its id equals some lookup's — so it is resolved first and asked with the lookups in one pass.
        var checkedLookups = item.Lookups.Where(l => serverSetLookupColumns?.Contains(l.Column) != true).ToArray();
        var pair = PairOf(item);
        if (pair.InvalidId)
            return new Outcome { Invalid = $"'{SecureRootInheritance.PairIdColumn}' must hold the id of the record this row is filed under." };
        var target = await PairTargetAsync(user, table, pair.IdText, pair.TypeId, pair.TypeEntitySet, checkedLookups, ct)
            .ConfigureAwait(false);
        if (target.Refusal is { } refusal)
            return refusal;

        return await CheckCallerMayAppendToAsync(
                user, me, target.Lookup is { } named ? checkedLookups.Append(named) : checkedLookups, ct)
            .ConfigureAwait(false);
    }

    // ── The ADR-024 regarding pair (sprk_regardingrecordid + sprk_regardingrecordtype) ─────────────────────────────

    private static readonly System.Text.RegularExpressions.Regex LogicalName = new(@"^[a-z][a-z0-9_]*\z",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// #1616: whether a typed filing lookup of <paramref name="table"/> (<see cref="SecureRootInheritance.TypedFilingColumns"/>)
    /// names (<paramref name="pairTable"/>, <paramref name="pairId"/>) — one this write sets, else one the row keeps.
    /// </summary>
    private static bool RepeatsTypedFilingLookup(
        string table, string pairTable, Guid pairId, IReadOnlyCollection<DataverseWriteItemMapper.MappedLookup> lookups,
        IReadOnlyCollection<(string Column, Guid Id)>? keptOnTheRow)
    {
        if (!SecureRootInheritance.TypedFilingColumns.TryGetValue(table, out var filing))
            return false;
        var columns = filing.Where(f => string.Equals(f.ParentTable, pairTable, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Column)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return lookups.Any(l => l.RecordId == pairId && columns.Contains(l.Column)
                                && string.Equals(l.RelatedTable, pairTable, StringComparison.OrdinalIgnoreCase))
               || (keptOnTheRow ?? Array.Empty<(string, Guid)>()).Any(k => k.Id == pairId && columns.Contains(k.Column));
    }

    /// <summary>The answer of <see cref="PairTargetAsync"/>: the record to check AppendTo on, or why the pair is refused.</summary>
    internal readonly record struct PairTarget(DataverseWriteItemMapper.MappedLookup? Lookup, Outcome? Refusal);

    /// <summary>
    /// The record the ADR-024 regarding pair names, read AS THE CALLER, as a lookup the caller must hold AppendTo on — or
    /// <c>null</c> when the pair names nothing. The rule is how the pair's readers read it — SecureRootInheritance
    /// (<c>NameParentsAsync</c>), RecordContainerResolver and CoreAncestorResolver (<c>ClassifyStampSource</c>): by its TYPE
    /// when it has one, otherwise only by agreement with a lookup the row carries.
    /// <list type="number">
    /// <item>No id → it names nothing (or clears it): <c>null</c>.</item>
    /// <item>An id that is not a record id → <see cref="Outcome.Invalid"/>.</item>
    /// <item>A TYPE → the catalog row is read as the caller (unreadable → the uniform not-found its own lookup's AppendTo
    /// check gives) and must name a table (otherwise <see cref="Outcome.Invalid"/>); the pair's record in that table is
    /// returned. It is ALWAYS returned — never skipped because a lookup has the same id (a lookup in another table, or one
    /// nobody checked, says nothing about it), nor because it is ordinary, missing or unreadable. A pair naming a different
    /// record than a typed lookup is therefore checked too: secure-if-any reads both, so both cost AppendTo.</item>
    /// <item>No type → only a lookup in one of the row's agreement columns (<see cref="PairAgreementColumns"/>) with the
    /// same id says what it names — one this write sets (<paramref name="lookups"/>), else one the row keeps
    /// (<paramref name="keptOnTheRow"/>, an update) — and THAT record is returned to be checked (the regarding builders'
    /// and the email draft's shape: the typed lookup and the untyped pair together). None → <see cref="Outcome.Invalid"/>
    /// (what it names cannot be told, so it is never "nothing").</item>
    /// </list>
    /// Nothing here reads the record the pair names, so no answer depends on whether it exists or is secure; the caller
    /// asks AppendTo on the returned lookup before anything reads it app-only.
    /// </summary>
    internal static async Task<PairTarget> PairTargetAsync(
        IDataverseUserClient user, string table, string? idText, Guid? typeId, string? typeEntitySet,
        IReadOnlyCollection<DataverseWriteItemMapper.MappedLookup> lookups, CancellationToken ct,
        IReadOnlyCollection<(string Column, Guid Id)>? keptOnTheRow = null)
    {
        if (string.IsNullOrWhiteSpace(idText))
            return default; // names nothing (or clears it)

        if (!Guid.TryParse(idText.Trim(), out var pairId) || pairId == Guid.Empty)
            return Refuse($"'{SecureRootInheritance.PairIdColumn}' must hold the id of the record this row is filed under.");

        if (typeId is not { } type)
        {
            var agreement = PairAgreementColumns(table);
            if (lookups.FirstOrDefault(l => l.RecordId == pairId && agreement.ContainsKey(l.Column)) is { } written)
                return new PairTarget(written, null); // the lookup this write sets beside it — asked like any lookup

            foreach (var (column, id) in keptOnTheRow ?? Array.Empty<(string, Guid)>())
            {
                if (id != pairId || !agreement.TryGetValue(column, out var keptTable))
                    continue;
                var keptSet = await EntitySetOfAsync(user, keptTable, ct).ConfigureAwait(false);
                if (keptSet.Failure is { } keptFailure)
                    return new PairTarget(null, keptFailure);
                return new PairTarget(new DataverseWriteItemMapper.MappedLookup(column, keptTable, keptSet.EntitySet!, pairId), null);
            }

            return Refuse($"'{SecureRootInheritance.PairIdColumn}' names a record without its record type " +
                          $"('{SecureRootInheritance.PairTypeColumn}') or a lookup of this row that names it, so what it is " +
                          "filed under cannot be told.");
        }

        var typeRow = await user.GetAsync(
                $"{typeEntitySet ?? RecordTypeEntitySet}({type:D})?$select={RecordTypeLogicalNameColumn}", ct)
            .ConfigureAwait(false);
        if (!typeRow.IsSuccess)
        {
            return typeRow.StatusCode is 403 or 404
                ? new PairTarget(null, new Outcome
                {
                    Denied = "You do not have permission to file this record under the record type it names.",
                    ParentUnavailable = true,
                    RefusedColumn = SecureRootInheritance.PairIdColumn,
                })
                : new PairTarget(null, new Outcome { ClientFailure = typeRow });
        }

        var pairTable = typeRow.Body is { } typeBody
                    && typeBody.TryGetProperty(RecordTypeLogicalNameColumn, out var logical)
                    && logical.ValueKind == JsonValueKind.String
            ? logical.GetString()?.Trim().ToLowerInvariant()
            : null;
        if (string.IsNullOrEmpty(pairTable) || !LogicalName.IsMatch(pairTable))
            return Refuse($"The record type '{SecureRootInheritance.PairTypeColumn}' names no table, so what this row is filed under cannot be told.");

        // #1616: on a work assignment (an inheriting table whose pair is NOT BFF-managed) the pair names a root — a matter, a
        // project or another work assignment — only by repeating a typed filing lookup: the readers' one rule
        // (SecureRootInheritance.PairNamesAParentOnItsOwn; the container resolver refuses any other), so this server never
        // writes a filing they would read as undetermined. A work assignment has no typed lookup to another work assignment,
        // so such a pair is always refused. Decided from the payload, the row's kept lookups and the catalog only, never
        // from the record the pair names.
        if (SecureRootInheritance.Inherits(table)
            && !SecureRootInheritance.PairNamesAParentOnItsOwn(table)
            && (SecureRootInheritance.IsParent(pairTable) || SecureRootInheritance.Inherits(pairTable))
            && !RepeatsTypedFilingLookup(table, pairTable, pairId, lookups, keptOnTheRow))
        {
            return Refuse($"It was not written: {SecureRootInheritance.PairWithoutTypedLookupReason(table, pairTable)}. " +
                          "Set that lookup to the same record.");
        }

        var entitySet = await EntitySetOfAsync(user, pairTable, ct).ConfigureAwait(false);
        if (entitySet.Failure is { } failure)
            return new PairTarget(null, failure);

        return new PairTarget(
            new DataverseWriteItemMapper.MappedLookup(SecureRootInheritance.PairIdColumn, pairTable, entitySet.EntitySet!, pairId), null);

        static PairTarget Refuse(string why) => new(null, new Outcome { Invalid = why });
    }

    /// <summary>A table's entity set, from its metadata read AS THE CALLER (a failure is theirs; no set is invalid).</summary>
    private static async Task<(string? EntitySet, Outcome? Failure)> EntitySetOfAsync(
        IDataverseUserClient user, string table, CancellationToken ct)
    {
        var metadata = await user.GetAsync($"EntityDefinitions(LogicalName='{table}')?$select=EntitySetName", ct)
            .ConfigureAwait(false);
        if (!metadata.IsSuccess)
            return (null, new Outcome { ClientFailure = metadata });
        var entitySet = metadata.Body is { } meta && meta.TryGetProperty("EntitySetName", out var set) && set.ValueKind == JsonValueKind.String
            ? set.GetString()
            : null;
        return string.IsNullOrEmpty(entitySet)
            ? (null, new Outcome { Invalid = $"Table '{table}' has no entity set, so what this row is filed under cannot be told." })
            : (entitySet, null);
    }

    /// <summary>
    /// AS THE CALLER, AppendTo on one record named by table and id (<see cref="CheckCallerMayAppendToAsync"/>) — for a
    /// writer that must ask before it reads that record app-only (the email draft's regarding, whose ancestry is derived
    /// app-only). A missing record, one the caller cannot see and one they may not append to answer alike.
    /// </summary>
    internal static async Task<Outcome> CheckCallerMayAppendToRecordAsync(
        IDataverseUserClient user, Guid me, string table, Guid recordId, CancellationToken ct)
    {
        var entitySet = await EntitySetOfAsync(user, table, ct).ConfigureAwait(false);
        if (entitySet.Failure is { } failure)
            return failure;
        return await CheckCallerMayAppendToAsync(
                user, me, new[] { new DataverseWriteItemMapper.MappedLookup("regarding", table, entitySet.EntitySet!, recordId) }, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// AS THE CALLER, before an update of a work assignment or project (a root that inherits security from what it is
    /// filed under, <see cref="SecureRootInheritance.Inherits"/>) asks the secure-root gate anything: AppendTo on every
    /// record the update files it under — its typed regarding lookups and the record its pair will name. The gate reads
    /// app-only, so this comes first: a caller who may not file under a record learns nothing about it. <c>null</c>: the
    /// update moves the row under nothing, or only under records the caller may append to.
    /// </summary>
    internal static async Task<RefileOutcome?> CheckCallerMayFileRootUnderAsync(
        IDataverseUserClient user, string table, string entitySetName, Guid recordId, DataverseWriteItemMapper.MappedItem item,
        CancellationToken ct)
    {
        var filing = SecureRootInheritance.FilingColumnsOf(table);
        var typed = item.Lookups.Where(l => filing.Contains(l.Column) && !IsPairType(l.Column)).ToArray();
        if (typed.Length == 0 && !PairOf(item).SetsEither)
            return null;

        var me = await WhoAmIAsync(user, ct).ConfigureAwait(false);
        if (me.Failure is { } failure)
            return new RefileOutcome { ClientFailure = failure };

        var appendTo = await CheckCallerMayAppendToAsync(user, me.SystemUserId, typed, ct).ConfigureAwait(false);
        if (appendTo.Denied is { } denied)
            return new RefileOutcome { Denied = denied, ParentUnavailable = appendTo.ParentUnavailable };

        return await CheckCallerMayRefileByPairAsync(user, me.SystemUserId, table, entitySetName, recordId, item, ct).ConfigureAwait(false);
    }

    // ── AppendTo and rights on a record, asked as the caller ───────────────────────────────────────────────────────

    /// <summary>
    /// AS THE CALLER, AppendTo on each record in <paramref name="lookups"/> (<c>RetrievePrincipalAccess</c>) — the right a
    /// lookup onto a record costs. Shared by the create path and the update tool's re-file (verifier item 2), so a re-file
    /// onto a record the caller cannot see is denied before the owner decision is asked about it.
    /// </summary>
    internal static async Task<Outcome> CheckCallerMayAppendToAsync(
        IDataverseUserClient user, Guid me, IEnumerable<DataverseWriteItemMapper.MappedLookup> lookups, CancellationToken ct)
    {
        foreach (var lookup in lookups.DistinctBy(l => (l.RelatedEntitySet, l.RecordId)))
        {
            var rights = await RightsOnAsync(user, me, lookup.RelatedEntitySet, lookup.RecordId, ct).ConfigureAwait(false);

            // An ORGANIZATION-OWNED target (the ADR-024 record-type catalog sprk_recordtype_ref, the matter-type and
            // practice-area reference tables) has no record-level access: RetrievePrincipalAccess refuses the table (live,
            // spaarkedev1 2026-10-07: 400 0x80040800 "The 'RetrievePrincipalAccess' method does not support entities of type
            // 'sprk_recordtype_ref'"), so RightsOnAsync reads None and every ADR-024 resolver payload was refused as
            // "not found". What a run-as-user create checks there is the table's AppendTo PRIVILEGE, and the row must exist.
            // Ontology task 126 (#1399; uac-r2's rule, #1355 comment 6089838200 §6, D-113): a BUSINESS-OWNED target
            // (systemuser, team, businessunit) is refused the same way; there the privilege's DEPTH must cover the row's
            // business unit (CallerMayAppendToBusinessOwnedAsync).
            if (!rights.HasFlag(AccessRights.AppendTo)
                && !await CallerMayAppendToByPrivilegeAsync(user, me, lookup, ct).ConfigureAwait(false))
            {
                return new Outcome
                {
                    Denied = $"You do not have permission to file this record under the {lookup.RelatedTable} it names.",
                    ParentUnavailable = true,
                    RefusedColumn = lookup.Column,
                };
            }
        }

        return Outcome.Allowed;
    }

    /// <summary>
    /// AS THE CALLER, for a lookup target RetrievePrincipalAccess cannot answer: the target table's own metadata says how it
    /// is owned and names its AppendTo privilege, and the answer is that ownership's — ORGANIZATION-OWNED
    /// (<see cref="CallerMayAppendToOrganizationOwnedAsync"/>) or BUSINESS-OWNED (<see cref="CallerMayAppendToBusinessOwnedAsync"/>).
    /// Anything else — a user/team-owned table (RetrievePrincipalAccess answered it), no AppendTo privilege declared, a
    /// question Dataverse does not answer — is <c>false</c> (fail closed; the caller keeps the uniform not-found, so a
    /// missing row and an unappendable one still look alike).
    /// </summary>
    private static async Task<bool> CallerMayAppendToByPrivilegeAsync(
        IDataverseUserClient user, Guid me, DataverseWriteItemMapper.MappedLookup lookup, CancellationToken ct)
    {
        var metadata = await user.GetAsync(
                $"EntityDefinitions(LogicalName='{lookup.RelatedTable}')?$select=LogicalName,OwnershipType,PrimaryIdAttribute,Privileges", ct)
            .ConfigureAwait(false);
        if (!metadata.IsSuccess
            || metadata.Body is not { } body
            || !body.TryGetProperty("OwnershipType", out var ownership)
            || ownership.ValueKind != JsonValueKind.String
            || PrivilegeNamed(body, "AppendTo", 8) is not { } appendTo)
        {
            return false;
        }

        return ownership.GetString() switch
        {
            "OrganizationOwned" => await CallerMayAppendToOrganizationOwnedAsync(user, me, lookup, body, appendTo, ct).ConfigureAwait(false),
            "BusinessOwned" => await CallerMayAppendToBusinessOwnedAsync(user, me, lookup, appendTo, ct).ConfigureAwait(false),
            _ => false,
        };
    }

    /// <summary>
    /// AS THE CALLER, an ORGANIZATION-OWNED target: <c>true</c> only when the caller holds that table's AppendTo privilege
    /// (any depth: an organization-owned row has no business unit to reach) and the row exists for them (read under their
    /// token). Anything else is <c>false</c>.
    /// </summary>
    private static async Task<bool> CallerMayAppendToOrganizationOwnedAsync(
        IDataverseUserClient user, Guid me, DataverseWriteItemMapper.MappedLookup lookup, JsonElement body, string appendTo,
        CancellationToken ct)
    {
        var names = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { appendTo }));
        var held = await user.GetAsync(
                $"systemusers({me:D})/Microsoft.Dynamics.CRM.RetrieveUserSetOfPrivilegesByNames(PrivilegeNames=@p1)?@p1={names}", ct)
            .ConfigureAwait(false);
        if (!held.IsSuccess || !CallerRecordAccessProbe.ResponseGrantsPrivilege(held.Body?.GetRawText(), appendTo))
            return false;

        var primaryId = body.TryGetProperty("PrimaryIdAttribute", out var pk) && pk.ValueKind == JsonValueKind.String
            ? pk.GetString()
            : $"{lookup.RelatedTable}id";
        var row = await user.GetAsync($"{lookup.RelatedEntitySet}({lookup.RecordId:D})?$select={primaryId}", ct).ConfigureAwait(false);
        return row.IsSuccess;
    }

    /// <summary>
    /// The business-owned tables a lookup may name (Dataverse's own three), and the column a row of each, read as the
    /// caller, reports its business unit in. A business unit is its own business unit.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> BusinessUnitColumnOf = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["systemuser"] = "_businessunitid_value",
        ["team"] = "_businessunitid_value",
        ["businessunit"] = "businessunitid",
    };

    /// <summary>
    /// AS THE CALLER, a BUSINESS-OWNED target (<c>systemuser</c>, <c>team</c>, <c>businessunit</c>) — ontology task 126
    /// (#1399), uac-r2's rule (#1355 comment 6089838200 §6, D-113), mirroring what a create run as the user checks:
    /// <c>true</c> only when the caller holds the table's AppendTo privilege
    /// (<c>RetrieveUserSetOfPrivilegesByNames</c>, which returns each grant's depth and the business unit it is anchored
    /// at), the row is readable as the caller, and some grant covers the ROW's business unit FROM ITS OWN ANCHOR — the way
    /// Dataverse evaluates it (a privilege inherited through a team applies at the team's business unit; D-113 as amended
    /// by uac-r2's review of PR #2012):
    /// <list type="bullet">
    /// <item>Global — any business unit;</item>
    /// <item>Deep — the anchor business unit or one under it (<c>UnderOrEqual</c>, the Web API's EqOrUnder, on
    /// <c>businessunitid</c>, asked as the caller);</item>
    /// <item>Local — the anchor business unit only;</item>
    /// <item>Basic — only a <c>systemuser</c> target that is the caller (<paramref name="me"/>, which every caller takes
    /// from WhoAmI under the caller's own token); a team or business-unit target is denied.</item>
    /// </list>
    /// A grant with no anchor reported is not counted, at any depth (D-148). Anything unanswered — another business-owned table,
    /// an unknown depth, an unreadable or missing row, no business unit on it, a question Dataverse does not answer — is
    /// <c>false</c>, and the caller gets the uniform not-found.
    /// </summary>
    /// <remarks>The depth meanings repeat <c>SecureBuRoleDepthAssertion.Reaches</c> on purpose: that census evaluator walks a
    /// business-unit tree it reads app-only, where this question must be asked as the caller.</remarks>
    private static async Task<bool> CallerMayAppendToBusinessOwnedAsync(
        IDataverseUserClient user, Guid me, DataverseWriteItemMapper.MappedLookup lookup, string appendTo, CancellationToken ct)
    {
        if (!BusinessUnitColumnOf.TryGetValue(lookup.RelatedTable, out var businessUnitColumn))
            return false;

        var names = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { appendTo }));
        var held = await user.GetAsync(
                $"systemusers({me:D})/Microsoft.Dynamics.CRM.RetrieveUserSetOfPrivilegesByNames(PrivilegeNames=@p1)?@p1={names}", ct)
            .ConfigureAwait(false);
        if (!held.IsSuccess)
            return false;
        var grants = PrivilegeGrantsIn(held.Body, appendTo);
        if (grants.Count == 0)
            return false;

        // The row, read as the caller: it must be readable, and it reports the business unit the depth must reach.
        var row = await user.GetAsync($"{lookup.RelatedEntitySet}({lookup.RecordId:D})?$select={businessUnitColumn}", ct)
            .ConfigureAwait(false);
        if (!row.IsSuccess || GuidIn(row.Body, businessUnitColumn) is not { } rowBusinessUnit)
            return false;

        foreach (var (depth, anchor) in grants)
        {
            var covers = depth switch
            {
                PrivilegeDepth.Global => anchor != Guid.Empty,
                PrivilegeDepth.Basic => anchor != Guid.Empty
                    && string.Equals(lookup.RelatedTable, "systemuser", StringComparison.Ordinal) && lookup.RecordId == me,
                PrivilegeDepth.Local => anchor != Guid.Empty && rowBusinessUnit == anchor,
                PrivilegeDepth.Deep => anchor != Guid.Empty
                    && (rowBusinessUnit == anchor
                        || await IsBusinessUnitUnderAsync(user, rowBusinessUnit, anchor, ct).ConfigureAwait(false)),
                _ => false,
            };
            if (covers)
                return true;
        }

        return false;
    }

    /// <summary>
    /// AS THE CALLER, whether business unit <paramref name="candidate"/> is <paramref name="ancestor"/> or one under it
    /// (Web API <c>UnderOrEqual</c> on <c>businessunitid</c>, live on spaarkedev1 2026-10-16). A business unit the caller
    /// cannot read, or a question Dataverse does not answer, is <c>false</c>.
    /// </summary>
    private static async Task<bool> IsBusinessUnitUnderAsync(
        IDataverseUserClient user, Guid candidate, Guid ancestor, CancellationToken ct)
    {
        var filter = $"businessunitid eq {candidate:D} and Microsoft.Dynamics.CRM.UnderOrEqual(PropertyName='businessunitid',PropertyValue='{ancestor:D}')";
        var answer = await user.GetAsync($"businessunits?$select=businessunitid&$filter={Uri.EscapeDataString(filter)}", ct)
            .ConfigureAwait(false);
        if (!answer.IsSuccess
            || answer.Body is not { } body
            || !body.TryGetProperty("value", out var rows)
            || rows.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return rows.EnumerateArray().Any(r => r.ValueKind == JsonValueKind.Object && GuidIn(r, "businessunitid") == candidate);
    }

    /// <summary>
    /// The grants of <paramref name="privilegeName"/> in a <c>RetrieveUserSetOfPrivilegesByNames</c> response, each with
    /// its depth and the business unit Dataverse anchors it at — live shape (spaarkedev1, 2026-10-16):
    /// <c>{"RolePrivileges":[{"Depth":"Local","BusinessUnitId":"…","PrivilegeName":"prvAppendToUser",…}]}</c>. A depth that
    /// is not one of the four names is dropped (fail closed); a missing anchor is <see cref="Guid.Empty"/>, which no grant
    /// counts with.
    /// </summary>
    internal static IReadOnlyList<(PrivilegeDepth Depth, Guid Anchor)> PrivilegeGrantsIn(JsonElement? body, string privilegeName)
    {
        var grants = new List<(PrivilegeDepth, Guid)>();
        if (body is not { } root
            || root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("RolePrivileges", out var privileges)
            || privileges.ValueKind != JsonValueKind.Array)
        {
            return grants;
        }

        foreach (var privilege in privileges.EnumerateArray())
        {
            if (privilege.ValueKind != JsonValueKind.Object
                || !privilege.TryGetProperty("PrivilegeName", out var name)
                || name.ValueKind != JsonValueKind.String
                || !string.Equals(name.GetString(), privilegeName, StringComparison.OrdinalIgnoreCase)
                || !privilege.TryGetProperty("Depth", out var depth)
                || depth.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            PrivilegeDepth? parsed = depth.GetString() switch
            {
                "Basic" => PrivilegeDepth.Basic,
                "Local" => PrivilegeDepth.Local,
                "Deep" => PrivilegeDepth.Deep,
                "Global" => PrivilegeDepth.Global,
                _ => null,
            };
            if (parsed is { } known)
                grants.Add((known, GuidIn(privilege, "BusinessUnitId") ?? Guid.Empty));
        }

        return grants;
    }

    /// <summary>A non-empty GUID held as a string property of <paramref name="body"/>, or <c>null</c>.</summary>
    private static Guid? GuidIn(JsonElement? body, string property) =>
        body is { ValueKind: JsonValueKind.Object } root
        && root.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && Guid.TryParse(value.GetString(), out var id)
        && id != Guid.Empty
            ? id
            : null;

    /// <summary>
    /// AS THE CALLER, their rights on one record (<c>RetrievePrincipalAccess</c> under their own token);
    /// <see cref="AccessRights.None"/> when Dataverse does not answer. Shared by the AppendTo check above and the update
    /// tool's F3 question on a secure root a re-file would leave (task 146 c1, owner round 10 item 7).
    /// </summary>
    internal static async Task<AccessRights> RightsOnAsync(
        IDataverseUserClient user, Guid me, string entitySet, Guid recordId, CancellationToken ct)
    {
        var target = Uri.EscapeDataString($"{{\"@odata.id\":\"{entitySet}({recordId:D})\"}}");
        var access = await user.GetAsync(
            $"systemusers({me:D})/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@p1)?@p1={target}", ct)
            .ConfigureAwait(false);

        return access.IsSuccess
            && access.Body is { } body
            && body.TryGetProperty("AccessRights", out var value)
            && value.ValueKind == JsonValueKind.String
                ? DataverseAccessRightsMapper.FromAccessRightsString(value.GetString())
                : AccessRights.None;
    }

    // ── Who the caller is; the metadata readers (table privileges, secured columns) ────────────────────────────────

    /// <summary>The caller's <c>systemuserid</c> — <c>WhoAmI()</c> under their own token, which cannot name anyone else.</summary>
    internal static async Task<(Guid SystemUserId, DataverseUserResponse? Failure)> WhoAmIAsync(
        IDataverseUserClient user, CancellationToken ct)
    {
        var response = await user.GetAsync("WhoAmI()", ct).ConfigureAwait(false);
        if (!response.IsSuccess)
            return (Guid.Empty, response);

        return response.Body is { } body
               && body.TryGetProperty("UserId", out var id)
               && id.ValueKind == JsonValueKind.String
               && Guid.TryParse(id.GetString(), out var userId)
               && userId != Guid.Empty
            ? (userId, null)
            : (Guid.Empty, DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.UserContextRequired,
                "The calling user could not be identified in Dataverse."));
    }

    /// <summary>The name of the table privilege of <paramref name="type"/> in an <c>EntityDefinitions?$select=Privileges</c>
    /// body; <c>PrivilegeType</c> arrives as its name or its number.</summary>
    private static string? PrivilegeNamed(JsonElement? body, string type, int typeNumber)
    {
        if (body is not { } root
            || !root.TryGetProperty("Privileges", out var privileges)
            || privileges.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var privilege in privileges.EnumerateArray())
        {
            if (!privilege.TryGetProperty("PrivilegeType", out var kind))
                continue;

            var matches = kind.ValueKind switch
            {
                JsonValueKind.String => string.Equals(kind.GetString(), type, StringComparison.OrdinalIgnoreCase),
                JsonValueKind.Number => kind.TryGetInt32(out var n) && n == typeNumber,
                _ => false,
            };

            if (matches && privilege.TryGetProperty("Name", out var name) && name.ValueKind == JsonValueKind.String)
                return name.GetString();
        }

        return null;
    }

    /// <summary>The first attribute marked <c>IsSecured</c> in an <c>Attributes</c> collection body, if any.</summary>
    private static string? SecuredColumnIn(JsonElement? body)
    {
        if (body is not { } root
            || !root.TryGetProperty("value", out var attributes)
            || attributes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var attribute in attributes.EnumerateArray())
        {
            if (attribute.TryGetProperty("IsSecured", out var secured) && secured.ValueKind == JsonValueKind.True
                && attribute.TryGetProperty("LogicalName", out var name))
            {
                return name.GetString();
            }
        }

        return null;
    }
}
