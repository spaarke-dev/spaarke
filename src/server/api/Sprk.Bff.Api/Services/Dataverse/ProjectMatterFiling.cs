using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using static Sprk.Bff.Api.Services.Dataverse.CallerWriteAuthorization;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// unified-access-control-r2 task 195 (#2352, owner choice (a) 2026-10-27): the ONE server core that files a
/// <c>sprk_project</c> under a <c>sprk_matter</c> through its ADR-024 regarding pair (<c>sprk_regardingrecordid</c> +
/// <c>sprk_regardingrecordtype</c>) — the project's only filing. The pair is field-secured so that only the BFF's application
/// users write it; this is where they write it on an EXISTING project. Two callers share it: the browser route
/// <c>PATCH /api/v1/child-records/sprk_project/{id}</c> (the wizards and the backfill script) and the chat update tool
/// (<c>DataverseUpdateRecordHandler</c>). A create still files a project through <see cref="Sprk.Bff.Api.Services.RecordCreation.RecordCreationService"/>.
/// </summary>
/// <remarks>
/// <para><b>Order</b> (the task 195 design note, §2.2): the target is a matter (decided from the payload and the record-type
/// catalog, read as the caller) → the caller can read the project (uniform not-found) → the project's current pair and
/// version, read by the application → AS THE CALLER: Write (and Append) on the project, AppendTo on the matter and on the
/// record-type row (a matter the caller cannot use and one that does not exist answer alike) → the same pair already
/// there: nothing written → the secure-root gate (<see cref="SecureRootFilingGate.CheckAsync"/>, fail closed) → the
/// secure-record rule below → the caller's own PATCH of any other column (the chat tool only) → the application writes the
/// pair ONLY IF the row is still at the version read (<c>If-Match</c>; a changed row is
/// <see cref="ChangedConcurrentlyCode"/>, nothing written) → read back, the previous pair logged → the caller's after-write
/// step (the core-ancestor re-stamp) → <see cref="SecureRootFilingGate.SecureAfterWriteAsync"/> inline. Ownership then
/// follows through the root-owner step and the ordinary-moves pass of the existing jobs; nothing here writes an owner.</para>
/// <para><b>Re-file</b> (owner decision 2026-10-31): a pair that already names ANOTHER matter is re-filed through the same
/// steps. The N:N association (<c>sprk_Project_Matter_nn</c>) is never read or written here: it confers nothing.</para>
/// <para><b>Secure records</b> (the design note's open check, answered in its §1). Two rules, both refused BEFORE any
/// write with their own codes (fail closed), and both lifted only by a later decision:
/// <list type="bullet">
/// <item>Filing under a SECURE matter (<see cref="SecureMatterCode"/>): secure-if-any would make the project secure with a
/// container of its own (<c>ProvisionInheritedAsync</c>), and the storage resolver then sees two different secure roots
/// above every record filed under the project and refuses their content (<c>container_ancestor_ambiguous</c>, the
/// "stacked" case pinned by <c>ChildRecordContainerResolutionTests</c>). One container per secure family is task 178.</item>
/// <item>Re-filing AWAY from a secure matter or project (<see cref="LeavesSecureCode"/>): the owner's rule for this task is
/// that nothing is released from isolation, while the follow-parents step (task 175, "both ways") would take an
/// inherited-secure project out of isolation once its pair names an ordinary record. Refusing keeps both true.</item>
/// </list>
/// A flag that cannot be read is never "not secure": refused with <see cref="RecordOwnerRefusal.ParentUndetermined"/>.</para>
/// <para><b>Component justification (CLAUDE.md §11).</b> Existing — the chat update tool's project-pair sequence
/// (<c>OwnedChildWrite.PatchWithBffManagedPairAsync</c>, now removed) and <see cref="OwnedChildWrite.RefileAsync"/>, which
/// treats a root as "not a re-file". Extension — <c>RefileAsync</c> is the child re-file (owner re-derived through the
/// resolver); a root's pair write is a different sequence (conditional app-only write, secure-record rules), so it sits
/// beside it as one static helper, with no DI registration (ADR-010). Cost of doing nothing — the wizards cannot file a
/// project under its matter (the pair is field-secured), and the chat tool keeps writing the pair with <c>If-Match: *</c>
/// and no read-back.</para>
/// </remarks>
internal static class ProjectMatterFiling
{
    internal const string Table = SecureRootInheritance.Project;
    internal const string EntitySet = "sprk_projects";
    internal const string MatterTable = SecureRootInheritance.Matter;

    /// <summary>The pair names a record that is not a matter (400). Nothing read about that record; nothing written.</summary>
    internal const string NotAMatterCode = "project_filing.not_a_matter";

    /// <summary>The matter is secure (409): see the class remarks. Nothing written.</summary>
    internal const string SecureMatterCode = "project_filing.secure_matter_unsupported";

    /// <summary>The project is filed under a secure record now (409): see the class remarks. Nothing written.</summary>
    internal const string LeavesSecureCode = "project_filing.leaves_secure_record";

    /// <summary>The row changed between the read and the conditional write (409, nothing written) — the existing code of the
    /// same condition in the follow-parents step.</summary>
    internal const string ChangedConcurrentlyCode = SecureRootInheritance.ReasonChangedConcurrently;

    private const string VersionColumn = "versionnumber";
    private const string IsSecureColumn = "sprk_issecure";
    private static readonly string PairTypeValue = $"_{SecureRootInheritance.PairTypeColumn}_value";

    /// <summary>What the core decided.</summary>
    internal enum OutcomeKind
    {
        /// <summary>The pair was written (and, with other columns, the caller's own PATCH).</summary>
        Filed,

        /// <summary>The pair already named that matter: the pair was not written (other columns, if any, were).</summary>
        Unchanged,

        /// <summary>The project, the matter it names or the record type is not the caller's to use, or does not exist.</summary>
        NotFound,

        /// <summary>The caller's own rights on the project refused it.</summary>
        Denied,

        /// <summary>The payload is not a whole pair.</summary>
        Invalid,

        /// <summary><see cref="NotAMatterCode"/>.</summary>
        NotAMatter,

        /// <summary>The secure-root gate refused it (<see cref="Outcome.Refusal"/>), or a flag could not be read.</summary>
        Refused,

        /// <summary><see cref="SecureMatterCode"/>.</summary>
        SecureMatter,

        /// <summary><see cref="LeavesSecureCode"/>.</summary>
        LeavesSecure,

        /// <summary><see cref="ChangedConcurrentlyCode"/>.</summary>
        Conflict,

        /// <summary>A question asked as the caller failed at Dataverse (<see cref="Outcome.ClientFailure"/>).</summary>
        ClientFailure,

        /// <summary>The application could not read or write here (no writer composed, a read that failed): nothing written.</summary>
        Unavailable,
    }

    /// <summary>The core's answer. Everything but <see cref="OutcomeKind.Filed"/> and <see cref="OutcomeKind.Unchanged"/>
    /// wrote nothing.</summary>
    internal sealed record Outcome(OutcomeKind Kind, string? Detail = null)
    {
        public DataverseUserResponse? ClientFailure { get; init; }

        public RecordOwnerResolution? Refusal { get; init; }

        /// <summary>The securing that followed the write (null when nothing needed securing or it was not run).</summary>
        public SecureRootInheritResult? Secured { get; init; }

        /// <summary>The pair the project held before the write (its id text and record type), for the log.</summary>
        public (string? Id, Guid? Type) Previous { get; init; }

        /// <summary>The pair read back after the write equals the pair written.</summary>
        public bool ReadBackMatches { get; init; }

        public bool Wrote => Kind is OutcomeKind.Filed or OutcomeKind.Unchanged;
    }

    /// <summary>The pair and version the application reads on the project.</summary>
    private sealed record PairRead(bool Exists, string? Id, Guid? Type, long? Version);

    /// <summary>
    /// Files project <paramref name="projectId"/> under the matter <paramref name="item"/>'s pair names (or, from the chat
    /// tool, clears the pair). <paramref name="item"/> carries BOTH pair columns; any other column in it is sent as the
    /// caller's own PATCH after every check (the route accepts the pair only). See the class remarks for the order.
    /// </summary>
    /// <param name="appOnly">The application's reader and conditional writer; null fails closed (nothing written).</param>
    /// <param name="afterWrite">Run after the write and its read-back, before the securing step (the caller's re-stamp).</param>
    internal static async Task<Outcome> FileAsync(
        IDataverseUserClient user,
        IFieldMappingDataverseService? appOnly,
        SecureRootFilingGate rootFiling,
        Guid projectId,
        DataverseWriteItemMapper.MappedItem item,
        ILogger logger,
        string? traceId,
        CancellationToken ct,
        Func<Task>? afterWrite = null)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(rootFiling);
        ArgumentNullException.ThrowIfNull(item);

        // ── 1. The pair as a whole, naming a matter — from the payload and the catalog only ─────────────────────────
        var pair = OwnedChildWrite.PairOf(item);
        if (!pair.SetsId || !pair.SetsType || pair.InvalidId)
        {
            return new Outcome(OutcomeKind.Invalid,
                "What a project is filed under is written as a whole: its record and its record type. Nothing was saved.");
        }

        var clears = string.IsNullOrWhiteSpace(pair.IdText) && pair.TypeId is null;
        if (!clears && (string.IsNullOrWhiteSpace(pair.IdText) || pair.TypeId is null))
        {
            return new Outcome(OutcomeKind.Invalid,
                "What a project is filed under is written as a whole: its record and its record type. Nothing was saved.");
        }

        DataverseWriteItemMapper.MappedLookup? matter = null;
        if (!clears)
        {
            var target = await PairTargetAsync(user, Table, pair.IdText, pair.TypeId, pair.TypeEntitySet, item.Lookups, ct)
                .ConfigureAwait(false);
            if (target.Refusal is { } refusal)
                return FromCallerCheck(refusal);
            if (target.Lookup is not { } named)
                return new Outcome(OutcomeKind.Invalid, "The record this project is filed under could not be told. Nothing was saved.");
            if (!string.Equals(named.RelatedTable, MatterTable, StringComparison.OrdinalIgnoreCase))
                return new Outcome(OutcomeKind.NotAMatter, "A project is filed only under a matter. Nothing was saved.");
            matter = named;
        }

        if (appOnly is null)
        {
            return new Outcome(OutcomeKind.Unavailable,
                "What a project is filed under is written only by Spaarke, which cannot write it here, so nothing was saved.");
        }

        // ── 2. The caller can read the project (a row they cannot read and a missing row answer alike) ──────────────
        var visible = await user.GetAsync($"{EntitySet}({projectId:D})?$select={Table}id", ct).ConfigureAwait(false);
        if (!visible.IsSuccess)
        {
            return visible.StatusCode is 403 or 404
                ? new Outcome(OutcomeKind.NotFound, "The project was not found.")
                : new Outcome(OutcomeKind.ClientFailure) { ClientFailure = visible };
        }

        // ── 3. The current pair and version, read by the application ───────────────────────────────────────────────
        var before = await ReadPairAsync(appOnly, projectId, logger, ct).ConfigureAwait(false);
        if (before is null)
            return new Outcome(OutcomeKind.Unavailable, "The project could not be read, so nothing was saved.");
        if (!before.Exists)
            return new Outcome(OutcomeKind.NotFound, "The project was not found.");
        if (before.Version is null)
            return new Outcome(OutcomeKind.Unavailable, "The project's version could not be read, so nothing was saved.");

        // ── 4. AS THE CALLER: Write (and Append) on the project, AppendTo on the matter and the record-type row ─────
        var me = await WhoAmIAsync(user, ct).ConfigureAwait(false);
        if (me.Failure is { } whoAmIFailure)
            return new Outcome(OutcomeKind.ClientFailure) { ClientFailure = whoAmIFailure };

        var typeLookups = item.Lookups.Where(l => OwnedChildWrite.IsPairType(l.Column)).ToArray();
        var needed = typeLookups.Length > 0 ? AccessRights.Write | AccessRights.Append : AccessRights.Write;
        var rights = await RightsOnAsync(user, me.SystemUserId, EntitySet, projectId, ct).ConfigureAwait(false);
        if ((rights & needed) != needed)
            return new Outcome(OutcomeKind.Denied, "You do not have permission to change what this project is filed under.");

        var appendTo = await CheckCallerMayAppendToAsync(
                user, me.SystemUserId, matter is null ? typeLookups : typeLookups.Prepend(matter), ct)
            .ConfigureAwait(false);
        if (appendTo.ClientFailure is { } appendFailure)
            return new Outcome(OutcomeKind.ClientFailure) { ClientFailure = appendFailure };
        if (!appendTo.Succeeded)
            return FromCallerCheck(appendTo);

        // ── 5. The same pair already there ─────────────────────────────────────────────────────────────────────────
        var restBody = BodyWithout(item.JsonBody, OwnedChildWrite.BffManagedPairColumnsOf(Table));
        var same = clears
            ? string.IsNullOrWhiteSpace(before.Id) && before.Type is null
            : Guid.TryParse(before.Id?.Trim(), out var beforeId) && beforeId == matter!.RecordId && before.Type == pair.TypeId;
        if (same && restBody is null)
        {
            logger.LogInformation("[PROJECT-FILING] {Table} {Id}: already filed as asked; nothing written.", Table, projectId);
            return new Outcome(OutcomeKind.Unchanged) { Previous = (before.Id, before.Type), ReadBackMatches = true };
        }

        // ── 6. The secure-root gate (fail closed; it also refuses the secure flag and access columns in the rest) ─────
        if (await rootFiling.CheckAsync(Table, projectId, OwnedChildWrite.WritesOf(item), ct,
                    OwnedChildWrite.CallerMayFileRootUnderSecure(user))
                .ConfigureAwait(false) is { } gateRefusal)
        {
            return new Outcome(OutcomeKind.Refused, gateRefusal.Reason) { Refusal = gateRefusal };
        }

        // ── 7. The secure-record rules (class remarks) ─────────────────────────────────────────────────────────────
        if (!same)
        {
            if (matter is not null)
            {
                switch (await ReadSecureFlagAsync(appOnly, MatterTable, matter.RecordId, logger, ct).ConfigureAwait(false))
                {
                    case true:
                        return new Outcome(OutcomeKind.SecureMatter,
                            "This matter is secure, and a project cannot be filed under a secure matter here yet. Nothing was saved.");
                    case null:
                        return Undetermined("whether the matter is secure could not be read");
                }
            }

            if (!string.IsNullOrWhiteSpace(before.Id))
            {
                var leaves = await CurrentParentIsSecureAsync(appOnly, before, logger, ct).ConfigureAwait(false);
                if (leaves is null)
                    return Undetermined("whether the record it is filed under now is secure could not be read");
                if (leaves == true)
                {
                    return new Outcome(OutcomeKind.LeavesSecure,
                        "This project is filed under a secure record, and it cannot be moved away from it here. Nothing was saved.");
                }
            }
        }

        // ── 8. The caller's own PATCH of every other column (the chat tool) — then the version the write is conditional on ─
        var version = before.Version.Value;
        if (restBody is not null)
        {
            var patch = await user.PatchAsync($"{EntitySet}({projectId:D})", restBody, ct).ConfigureAwait(false);
            if (!patch.IsSuccess)
                return new Outcome(OutcomeKind.ClientFailure) { ClientFailure = patch };

            if (!same)
            {
                // The caller's own write moved the version; the pair the checks decided on must still be the pair now.
                var afterPatch = await ReadPairAsync(appOnly, projectId, logger, ct).ConfigureAwait(false);
                if (afterPatch is not { Exists: true, Version: { } patchedVersion }
                    || !string.Equals(afterPatch.Id?.Trim(), before.Id?.Trim(), StringComparison.OrdinalIgnoreCase)
                    || afterPatch.Type != before.Type)
                {
                    return new Outcome(OutcomeKind.Conflict,
                        "What this project is filed under changed while it was being saved; its other changes were saved. Reload it and try again.");
                }

                version = patchedVersion;
            }
        }

        // ── 9. The application writes the pair, only if the row is still at the version read ───────────────────────
        if (!same)
        {
            var pairFields = PairFields(item.JsonBody);
            try
            {
                await appOnly.UpdateRecordFieldsIfUnchangedAsync(Table, projectId, pairFields, version, ct).ConfigureAwait(false);
            }
            catch (System.Data.DBConcurrencyException)
            {
                logger.LogWarning("[PROJECT-FILING] {Table} {Id} changed after it was read (version {Version}); nothing written ({Code}).",
                    Table, projectId, version, ChangedConcurrentlyCode);
                return new Outcome(OutcomeKind.Conflict,
                    "This project was changed by someone else while it was being filed. Nothing was saved. Reload it and try again.");
            }
            catch (KeyNotFoundException)
            {
                return new Outcome(OutcomeKind.NotFound, "The project was not found.");
            }
        }

        // ── 10. Read back; the previous pair logged ────────────────────────────────────────────────────────────────
        var readBack = same ? before : await ReadPairAsync(appOnly, projectId, logger, ct).ConfigureAwait(false);
        var matches = readBack is { Exists: true }
                      && (clears
                          ? string.IsNullOrWhiteSpace(readBack.Id) && readBack.Type is null
                          : Guid.TryParse(readBack.Id?.Trim(), out var readId) && readId == matter!.RecordId && readBack.Type == pair.TypeId);
        if (!same)
        {
            if (matches)
            {
                logger.LogInformation(
                    "[PROJECT-FILING] {Table} {Id} filed under {NewTable} {NewId} (type {NewType}); previous pair: {PreviousId} (type {PreviousType}).",
                    Table, projectId, clears ? "(none)" : MatterTable, matter?.RecordId, pair.TypeId, before.Id ?? "(none)", before.Type);
            }
            else
            {
                logger.LogWarning(
                    "[PROJECT-FILING] {Table} {Id}: the pair written does not read back as written (another writer, or the read " +
                    "failed); previous pair: {PreviousId} (type {PreviousType}). The secure-root job decides from what the row holds.",
                    Table, projectId, before.Id ?? "(none)", before.Type);
            }
        }

        if (afterWrite is not null)
            await afterWrite().ConfigureAwait(false);

        // ── 11. Secure inline (provisioning's own steps through the inheritance) ───────────────────────────────────
        var secured = await rootFiling.SecureAfterWriteAsync(Table, projectId, item.Columns, traceId).ConfigureAwait(false);

        return new Outcome(same ? OutcomeKind.Unchanged : OutcomeKind.Filed)
        {
            Secured = secured,
            Previous = (before.Id, before.Type),
            ReadBackMatches = matches,
        };
    }

    private static Outcome Undetermined(string reason) =>
        new(OutcomeKind.Refused, reason)
        {
            Refusal = RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUndetermined, reason + ", so it was not written"),
        };

    /// <summary>A caller check's refusal: a record the caller cannot use (or that does not exist) is the uniform not-found.</summary>
    private static Outcome FromCallerCheck(OwnedChildWrite.Outcome check) => check switch
    {
        { ClientFailure: { } failure } => new Outcome(OutcomeKind.ClientFailure) { ClientFailure = failure },
        { Invalid: { } invalid } => new Outcome(OutcomeKind.Invalid, invalid),
        { ParentUnavailable: true } => new Outcome(OutcomeKind.NotFound, check.Denied),
        { Denied: { } denied } => new Outcome(OutcomeKind.Denied, denied),
        _ => new Outcome(OutcomeKind.Invalid, "What this project is filed under could not be checked. Nothing was saved."),
    };

    /// <summary>
    /// The project's pair and version, read by the application (field security hides the pair from most users). <c>null</c>
    /// when the read failed (logged; never "not filed"); <c>Exists: false</c> when the row is gone.
    /// </summary>
    private static async Task<PairRead?> ReadPairAsync(
        IFieldMappingDataverseService appOnly, Guid projectId, ILogger logger, CancellationToken ct)
    {
        Dictionary<string, object?> row;
        try
        {
            row = await appOnly.RetrieveRecordFieldsAsync(
                    Table, projectId, [SecureRootInheritance.PairIdColumn, PairTypeValue, VersionColumn], ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "[PROJECT-FILING] {Table} {Id} could not be read; nothing written.", Table, projectId);
            return null;
        }

        if (row.Count == 0)
            return new PairRead(false, null, null, null);

        var id = row.GetValueOrDefault(SecureRootInheritance.PairIdColumn) as string;
        Guid? type = row.GetValueOrDefault(PairTypeValue) is string text && Guid.TryParse(text, out var parsed) && parsed != Guid.Empty
            ? parsed
            : null;
        return new PairRead(true, string.IsNullOrWhiteSpace(id) ? null : id, type, VersionOf(row.GetValueOrDefault(VersionColumn)));
    }

    private static long? VersionOf(object? value) => value switch
    {
        long l => l,
        int i => i,
        double d when d == Math.Floor(d) => (long)d,
        string s when long.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
        JsonElement { ValueKind: JsonValueKind.Number } n when n.TryGetInt64(out var fromJson) => fromJson,
        _ => null,
    };

    /// <summary><c>sprk_issecure</c> of one record, read by the application: <c>false</c> for a row that does not exist (it
    /// confers nothing); <c>null</c> when it cannot be read or reads empty (never "not secure").</summary>
    private static async Task<bool?> ReadSecureFlagAsync(
        IFieldMappingDataverseService appOnly, string table, Guid id, ILogger logger, CancellationToken ct)
    {
        try
        {
            var row = await appOnly.RetrieveRecordFieldsAsync(table, id, [IsSecureColumn], ct).ConfigureAwait(false);
            if (row.Count == 0)
                return false;
            return row.GetValueOrDefault(IsSecureColumn) as bool?;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "[PROJECT-FILING] The secure flag of {Table} {Id} could not be read.", table, id);
            return null;
        }
    }

    /// <summary>Whether the record the project's CURRENT pair names is secure — <c>false</c> when it names nothing a project
    /// inherits from, or a record that no longer exists; <c>null</c> when that cannot be told.</summary>
    private static async Task<bool?> CurrentParentIsSecureAsync(
        IFieldMappingDataverseService appOnly, PairRead before, ILogger logger, CancellationToken ct)
    {
        if (!Guid.TryParse(before.Id?.Trim(), out var parentId) || parentId == Guid.Empty || before.Type is not { } type)
            return null; // a pair the inheritance reads as undetermined: never "not secure"

        string? parentTable;
        try
        {
            var typeRow = await appOnly.RetrieveRecordFieldsAsync(
                    "sprk_recordtype_ref", type, [OwnedChildWrite.RecordTypeLogicalNameColumn], ct)
                .ConfigureAwait(false);
            parentTable = (typeRow.GetValueOrDefault(OwnedChildWrite.RecordTypeLogicalNameColumn) as string)?.Trim().ToLowerInvariant();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "[PROJECT-FILING] The record type {Type} could not be read.", type);
            return null;
        }

        if (string.IsNullOrEmpty(parentTable))
            return null;
        if (!SecureRootInheritance.IsParent(parentTable))
            return false;

        return await ReadSecureFlagAsync(appOnly, parentTable, parentId, logger, ct).ConfigureAwait(false);
    }

    /// <summary>The pair columns of the body, as the application's PATCH sends them (values kept as JSON).</summary>
    private static Dictionary<string, object?> PairFields(string jsonBody)
    {
        var managed = OwnedChildWrite.BffManagedPairColumnsOf(Table);
        return OwnedChildWrite.BodyFields(jsonBody)
            .Where(f => managed.Contains(SecureRootInheritance.NormalizeColumn(f.Key)))
            .ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
    }

    /// <summary>The body without <paramref name="columns"/>, or <c>null</c> when nothing else is left.</summary>
    private static string? BodyWithout(string jsonBody, IReadOnlySet<string> columns)
    {
        using var body = JsonDocument.Parse(jsonBody);
        using var stream = new MemoryStream();
        var rest = 0;
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in body.RootElement.EnumerateObject())
            {
                if (columns.Contains(SecureRootInheritance.NormalizeColumn(property.Name)))
                    continue;
                property.WriteTo(writer);
                rest++;
            }

            writer.WriteEndObject();
        }

        return rest > 0 ? System.Text.Encoding.UTF8.GetString(stream.ToArray()) : null;
    }
}
