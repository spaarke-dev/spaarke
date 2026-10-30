using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;

namespace Sprk.Bff.Api.Services.RecordCreation;

/// <summary>
/// Task 014 (field-mapping-server-write-path-r1, spec FR-06 steps 3 to 5, FR-08, NFR-01): field mapping applied INSIDE the
/// create, and the caller checks run on the merged payload.
/// </summary>
/// <remarks>
/// <para><b>Step 3 — fill-only, from a source the caller may read.</b> The source is resolved
/// (<see cref="MappingSourceResolver"/>: the explicit <c>mappingSource</c>, else the regarding pair, else the single typed
/// parent), the profile is read (keyed by the two TABLES only), the caller is probed for Read on the source BEFORE it is
/// read (<see cref="MappingSourceReadProbe"/>), and <see cref="CreateTimeFieldMapping.ApplyFillOnly"/> fills only what the
/// caller did not supply, with the table's Money columns from its metadata (DI-001 / #2124). A missing, denied or unreadable
/// source is ONE outcome — nothing mapped, one warning, never a refusal — so it is not an existence oracle. What the rules
/// wrote is the ORIGIN set (<c>mappedColumns</c>): every other column is the caller's. Each mapped value is checked against
/// the column it targets (exists, takes a value on create, of a type that fits) before it is merged; one that does not fit
/// is dropped with a warning rather than failing the create at Dataverse.</para>
/// <para><b>Step 4 — one merged item.</b> The caller's item and the mapped values are mapped together as the caller
/// (<see cref="DataverseWriteItemMapper.MapAsync"/>), so every mapped lookup has its entity set (ebilling-r1's review of
/// 013, item 2) and the body binds it by the table's own navigation property.</para>
/// <para><b>Step 5 — the merged payload, by origin</b> (owner decision 2026-10-19; FR-08 amendment 2026-10-20). The
/// caller's own values are asked first, alone, exactly as with no mapping: a failure refuses unchanged, a fault propagates.
/// Then the merged row is asked whole. If that fails or faults, the CALLER's values — together with every mapped FILING lookup
/// (<see cref="IsFilingLookup(string, DataverseWriteItemMapper.MappedLookup)"/>), which files the row as a caller's lookup would — are asked: a failure refuses with the
/// unchanged uniform outcome, and a fault propagates (refused, NFR-01). Every other mapped value is then asked on its own:
/// one that fails AppendTo, Append or field security, or whose check faults, is dropped with a warning and never written.
/// The row as it will be written is asked whole once more; a failure then is no single value's, so it refuses. With
/// nothing mapped, step 5 is the one check it was (task 013), unchanged.</para>
/// <para><b>ADR-008 path A:</b> every check is <see cref="CallerWriteAuthorization"/>'s, unchanged, asked here. <b>ADR-010:</b>
/// no registration — the collaborators are static or the service's own. <b>ADR-019:</b> every warning is a
/// <see cref="RecordCreationWarning"/> with a stable code, the column when there is one, and no source id, name or value.</para>
/// </remarks>
internal sealed partial class RecordCreationService
{
    /// <summary>The Office create's words for a profile that could not be read (its <c>ApplyFieldMappingsAsync</c>).</summary>
    internal const string MappingConfigurationUnreadable =
        "Field-mapping configuration could not be read, so no fields were copied from the related record.";

    /// <summary>The words for a mapping step that faulted: nothing was mapped, the caller's row is still created.</summary>
    internal const string MappingFaulted =
        "Fields could not be copied from the related record, so none were; the record was created with the values you gave.";

    /// <summary>The words for a target table whose column metadata could not be read as the caller.</summary>
    internal const string MappingTargetUnreadable =
        "The new record's columns could not be read, so no fields were copied from the related record.";

    /// <summary>Why a mapped value was not written (<see cref="Dropped"/>): words for the caller, never the value.</summary>
    private const string NoSuchColumn = "the new record has no such column that can be set when it is created";
    private const string CannotTakeValue = "the column cannot take the mapped value";
    private const string DateBehaviourUnknown = "how the column stores a date could not be read";
    private const string CouldNotBeChecked = "it could not be checked";
    private const string NotPermitted = "you do not have permission to set it";
    private const string NotUsable = "it was not found, or you do not have permission to use it";

    private static readonly IReadOnlyDictionary<string, JsonElement> NoMappedValues = new Dictionary<string, JsonElement>();

    private static readonly IReadOnlyDictionary<string, TargetAttribute> NoAttributes =
        new Dictionary<string, TargetAttribute>(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex LogicalNamePattern = new(@"^[a-z][a-z0-9_]*\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// What step 3 (or step 5) produced: the item the next steps read, and each mapped column's value as the create's item
    /// value — the origin set (its keys). Or a refusal.
    /// </summary>
    private sealed record MappingStep(
        DataverseWriteItemMapper.MappedItem? Item, IReadOnlyDictionary<string, JsonElement> Values, RecordCreationRefusal? Refusal)
    {
        public static MappingStep Unmapped(DataverseWriteItemMapper.MappedItem caller) => new(caller, NoMappedValues, null);

        public static MappingStep Refused(RecordCreationRefusal refusal) => new(null, NoMappedValues, refusal);
    }

    /// <summary>One target column as the table's metadata declares it (read as the caller).</summary>
    private sealed record TargetAttribute(string Type, bool ValidForCreate);

    /// <summary>
    /// True for a lookup of <paramref name="table"/> that FILES the row under a record (FR-08 amendment, owner 2026-10-20,
    /// unified-access-control-r2 condition c): a lineage lookup (<see cref="SecureChildLineage"/>), an ownership-parent
    /// lookup (<see cref="RecordReparent.FilingParentOf"/>), or a work assignment's or project's filing column
    /// (<see cref="SecureRootInheritance.FilingColumnsOf"/>). A MAPPED one is checked as the caller's own: AppendTo it fails
    /// refuses, never drops — dropping it would file the row somewhere other than where its source says.
    /// </summary>
    internal static bool IsFilingLookup(string table, string column) =>
        IsChildFilingLookup(table, column)
        || (SecureRootInheritance.Inherits(table) && SecureRootInheritance.FilingColumnsOf(table).Contains(column));

    /// <summary>
    /// Task 014 fix round 3 (UAC-r2 review, F-B; closes review K-5): <see cref="IsFilingLookup(string, string)"/>'s column
    /// lists, OR the owner decision's own test (<see cref="FilesUnderOwnershipParent"/>) — so every lookup step 7 files the row
    /// under is a filing lookup here too, whatever its column (e.g. <c>sprk_workassignment.sprk_regardingcommunication</c>,
    /// which no list names). A union, not a replacement: a lineage lookup that is no ownership parent (a message's thread)
    /// stays a filing lookup, as before.
    /// </summary>
    internal static bool IsFilingLookup(string table, DataverseWriteItemMapper.MappedLookup lookup) =>
        IsFilingLookup(table, lookup.Column) || FilesUnderOwnershipParent(table, lookup);

    /// <summary>
    /// Whether the owner decision (step 7, <see cref="OwnedChildWrite.ParentsOf"/>) files a row of <paramref name="table"/>
    /// under <paramref name="lookup"/>'s record: a lookup to an ownership-parent table that does not merely cite it. Asked of
    /// <see cref="OwnedChildWrite.ParentsOf"/> itself, never a copy of its predicate, so the two cannot drift.
    /// </summary>
    private static bool FilesUnderOwnershipParent(string table, DataverseWriteItemMapper.MappedLookup lookup) =>
        OwnedChildWrite.ParentsOf(table, new DataverseWriteItemMapper.MappedItem("{}", [lookup.Column]) { Lookups = [lookup] })
            .Count > 0;

    /// <summary>A child's lineage or ownership-parent lookup (the K-2 guard's set; see <see cref="ServerSetFilingColumn"/>).</summary>
    private static bool IsChildFilingLookup(string table, string column) =>
        (SecureChildLineage.Children.TryGetValue(table, out var lineage) && lineage.Lookups.ContainsKey(column))
        || RecordReparent.FilingParentOf(table, column) is not null;

    /// <summary>
    /// UAC-r2's review of 013 (K-2): a lookup the SERVER set costs no AppendTo at step 5
    /// (<see cref="RecordCreationRequest.ServerSetLookupColumns"/>), so a lineage or ownership-parent lookup must never be one
    /// — it would file the row under a record nobody asked about. The first such column, or <c>null</c>. Fix round 3 (F-B):
    /// on any table other than a work assignment or project, a server-set lookup of <paramref name="item"/> that the owner
    /// decision files the row under (<see cref="FilesUnderOwnershipParent"/>) is refused too, whatever its column. A work
    /// assignment's or project's own filing lookup is not refused here: <see cref="ServerSetRootFilingLookups"/> has it asked
    /// AppendTo, as the caller, at step 6 before the plan (refused in the plan's own not-found shape, which task 013's
    /// secure-parent test pins). Server-set columns are never mapped (<see cref="ProtectedFromMapping"/>), so the caller's
    /// item and the merged one carry the same server-set lookups.
    /// </summary>
    private static string? ServerSetFilingColumn(
        string table, DataverseWriteItemMapper.MappedItem item, IReadOnlySet<string>? serverSetLookupColumns)
    {
        if (serverSetLookupColumns is not { Count: > 0 })
            return null;

        // The set is the request's, already trimmed and lower-cased (RecordCreationRequest.ServerSetLookupColumns, R3-1).
        if (serverSetLookupColumns.FirstOrDefault(c => IsChildFilingLookup(table, c)) is { } listed)
            return listed;

        if (SecureRootInheritance.Inherits(table))
            return null;

        return item.Lookups.FirstOrDefault(l => serverSetLookupColumns.Contains(l.Column) && FilesUnderOwnershipParent(table, l))?.Column;
    }

    /// <summary>
    /// UAC-r2's review of 013 (K-2), task 014 review rounds 1 and 2: the lookups of <paramref name="item"/> that file a work
    /// assignment or project (<see cref="SecureRootInheritance.FilingColumnsOf"/>, and — fix round 3, F-B — every lookup the
    /// owner decision files it under, <see cref="FilesUnderOwnershipParent"/>) and that the SERVER set. Step 5 skipped
    /// their AppendTo and the plan asks it on a secure record only, so step 6 asks it, as the caller, whatever the record and
    /// before the plan reads it — none is ever filed under unasked. Empty for any other table, and when the server set
    /// nothing.
    /// </summary>
    private static DataverseWriteItemMapper.MappedLookup[] ServerSetRootFilingLookups(
        string table, DataverseWriteItemMapper.MappedItem item, IReadOnlySet<string>? serverSetLookupColumns)
    {
        if (serverSetLookupColumns is not { Count: > 0 } || !SecureRootInheritance.Inherits(table))
            return [];

        var filing = SecureRootInheritance.FilingColumnsOf(table);
        return item.Lookups
            .Where(l => serverSetLookupColumns.Contains(l.Column) && (filing.Contains(l.Column) || FilesUnderOwnershipParent(table, l)))
            .ToArray();
    }

    private static RecordCreationRefusal ServerSetFilingRefused(string column, string noun) => new(
        RecordCreationRefusalKind.Undecided, RecordCreationRefusal.ServerSetFilingLookupCode,
        $"The {noun} was not created: the server set '{column}', which files it under a record, without asking whether you " +
        "may file under that record. Nothing was written.");

    /// <summary>
    /// The columns a mapping rule may not write on <paramref name="table"/>: every column a caller's payload may not name
    /// (step 2: the common server-owned columns, the table's own, its autonumber and the creator person), the registry's
    /// mapping-protected columns (<see cref="RecordTableRule.MappingProtectedColumns"/>), the access record and secure flag
    /// a work assignment or project create is refused for (<see cref="SecureRootFilingGate.PlanCreateAsync"/>), and the
    /// lookups the server set (K-2: never filled by a mapped value the AppendTo check would then skip), and the contact-plane
    /// creator and requester (<c>sprk_createdbycontact</c>, <c>sprk_requestedby</c>, task 018b-K2). What an app-only write is
    /// refused is dropped separately, by <see cref="DropAppOnlyRefusedRules"/>.
    /// <c>internal</c> (was private) so the secure-root parity test (#2220) reads the very set the service applies.
    /// </summary>
    internal static IReadOnlySet<string> ProtectedFromMapping(
        string table, RecordTableRule rule, IReadOnlySet<string>? serverSetLookupColumns)
    {
        var columns = new HashSet<string>(RecordTableRules.CommonServerOwnedColumns, StringComparer.OrdinalIgnoreCase);
        columns.UnionWith(rule.TableServerOwnedColumns);
        columns.UnionWith(rule.MappingProtectedColumns);
        if (rule.AutonumberColumn is { } number)
            columns.Add(number);
        columns.Add(RecordCreatorPerson.Column);
        // Task 018b-K2 (UAC-r2 condition (b)): the owner and the creator columns (#2247: the creator person, the contact-plane
        // creator and the requester) are the server's alone. A rule that filled the contact ones app-only would give that
        // contact creator or requester rights on the external plane. The sets are the app-only refusal's own, so they never drift.
        columns.UnionWith(InheritedAccessPermission.OwnerColumns);
        columns.UnionWith(InheritedAccessPermission.CreatorColumns);
        if (SecureRootInheritance.Inherits(table))
        {
            columns.Add(AccessInheritance.Column);
            columns.Add(AccessFollowsParent.SecureFlagColumn);
        }

        if (serverSetLookupColumns is not null)
            columns.UnionWith(serverSetLookupColumns);
        return columns;
    }

    /// <summary>
    /// Task 018b-K2: the one target <see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/> refuses that create-time mapping
    /// still fills: a document's Access Permission (<see cref="InheritedAccessPermission.Column"/>). UAC-r2 D7 decided it stays
    /// fill-only at create (<c>RecordTableRules.cs</c>, the <c>sprk_document</c> entry, lines 195-201): on a CREATE a document has
    /// no own narrowing for a mapped value to lower, so a fill is safe. The refusal guards UPDATE writers, which could lower an
    /// existing narrowing.
    /// </summary>
    private static bool IsFillOnlyAtCreateExemption(string table, string target) =>
        string.Equals(table.Trim(), ExternalDocumentNarrowing.DocumentTable, StringComparison.OrdinalIgnoreCase)
        && string.Equals(target.Trim(), InheritedAccessPermission.Column, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Task 018b-K2: drops each rule whose target <see cref="InheritedAccessPermission.RefuseAppOnlyWrite"/> refuses for
    /// <paramref name="table"/> (mapping writes app-only, as the create's origin set, so it may write nothing that refusal
    /// guards), with a <see cref="RecordCreationWarning.MappingRuleSkippedCode"/> warning that names the column and the
    /// refusal code and no value. Keeps the refusal and this list from drifting apart. Never refuses the create (FR-08).
    /// </summary>
    private static List<FieldMappingRuleEntity> DropAppOnlyRefusedRules(
        string table, List<FieldMappingRuleEntity> rules, List<RecordCreationWarning> warnings)
    {
        var kept = new List<FieldMappingRuleEntity>(rules.Count);
        foreach (var rule in rules)
        {
            var target = rule.TargetField;
            if (!string.IsNullOrWhiteSpace(target)
                && !IsFillOnlyAtCreateExemption(table, target)
                && InheritedAccessPermission.RefuseAppOnlyWrite(
                    table, [target], InheritedAccessPermission.WriteIdentity.AppOnly) is { } refused)
            {
                var code = refused.RefusalCode ?? InheritedAccessPermission.AppOnlyDocumentWriteReasonCode;
                warnings.Add(new RecordCreationWarning(RecordCreationWarning.MappingRuleSkippedCode, target,
                    $"Field-mapping rule targeting \"{target}\" was skipped: that field is managed by the server and cannot be " +
                    $"set by a field-mapping rule ({code})."));
                continue;
            }

            kept.Add(rule);
        }

        return kept;
    }

    /// <summary>
    /// Step 3 (and the merge of step 4) for a caller. Never refuses: whatever cannot be mapped is not mapped, with a
    /// warning, and the caller's own item goes on. A fault propagates to step 3's caller, which maps nothing (fail closed).
    /// </summary>
    private async Task<MappingStep> MapFieldsAsync(
        string table, RecordTableRule rule, RecordCreationRequest request, RecordCreationMapping mapping,
        IDataverseUserClient user, DataverseWriteItemMapper.MappedItem callerItem,
        List<RecordCreationWarning> warnings, CancellationToken ct)
    {
        var unmapped = MappingStep.Unmapped(callerItem);
        var target = CallerEntity(table, callerItem);

        // FR-03: which record — from the payload and the record-type catalog (read as the caller) only.
        var resolution = await MappingSourceResolver.ResolveAsync(
                table, mapping.SourceTable, mapping.SourceId, target.Attributes.Where(a => a.Value is not null).ToArray(),
                (typeId, token) => RecordTypeTableAsync(user, typeId, token), ct)
            .ConfigureAwait(false);
        if (resolution.Source is not { } source)
        {
            if (resolution.Warning is { } undetermined)
                warnings.Add(new RecordCreationWarning(RecordCreationWarning.MappingSourceUndeterminedCode, null, undetermined));
            return unmapped;
        }

        // The profile is keyed by the two TABLES, so whether it exists says nothing about the source record.
        FieldMappingProfileEntity? profile;
        try
        {
            profile = await _appOnly
                .GetFieldMappingProfileWithRulesAsync(source.EntityLogicalName, table, activeRulesOnly: true, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RECORD-CREATE] the field-mapping profile {Source} -> {Table} could not be read; nothing was mapped",
                source.EntityLogicalName, table);
            warnings.Add(new RecordCreationWarning(RecordCreationWarning.MappingUnavailableCode, null, MappingConfigurationUnreadable));
            return unmapped;
        }

        if (profile is null)
            return unmapped; // no profile for the two tables: the silent no-op

        var rules = (profile.Rules ?? []).Select(NormalizedRule).ToList();
        if (rules.Count == 0)
        {
            warnings.Add(new RecordCreationWarning(RecordCreationWarning.MappingRuleSkippedCode, null,
                $"Field-mapping profile \"{profile.Name}\" has no rules; nothing was applied."));
            return unmapped;
        }

        // Task 018b-K2: a rule aimed at a column an app-only write is refused (InheritedAccessPermission.RefuseAppOnlyWrite) is
        // dropped with a warning that names the column and the refusal code. Mapping never refuses (FR-08).
        rules = DropAppOnlyRefusedRules(table, rules, warnings);
        if (rules.Count == 0)
            return unmapped;

        // FR-04: Read as the caller FIRST. Missing, denied, unmapped type, a failed probe or read: ONE outcome — nothing
        // mapped (not even a Default rule), one warning that names nothing.
        var sourceColumns = CreateTimeFieldMapping.CollectSourceColumns(rules);
        var read = await MappingSourceReadProbe.ReadAsync(
                mapping.Reader, source, sourceColumns, _entities, _logger, ct)
            .ConfigureAwait(false);
        if (!read.Readable)
        {
            warnings.Add(new RecordCreationWarning(RecordCreationWarning.MappingSourceUnavailableCode, null,
                read.Warning ?? MappingSourceReadProbe.SourceUnavailableWarning));
            return unmapped;
        }

        // #2188 (DI-004): the app-only read ignores field-level security, so it is masked down to the columns the CALLER can
        // read (one caller read of the same row) before any rule sees it. A caller read that cannot be made or answered makes
        // the source unavailable — the same single warning as an unreadable source, never the app-read values (FR-04).
        // A Default-only profile read nothing, so there is nothing to mask.
        Entity? masked = null;
        if (read.Record is not null)
        {
            masked = await MappingSourceCallerMask
                .ApplyAsync(user, source.EntityLogicalName, source.RecordId, read.Record, sourceColumns, _logger, ct)
                .ConfigureAwait(false);
            if (masked is null)
            {
                warnings.Add(new RecordCreationWarning(RecordCreationWarning.MappingSourceUnavailableCode, null,
                    MappingSourceReadProbe.SourceUnavailableWarning));
                return unmapped;
            }
        }

        // The target columns as the table declares them, read as the caller: its Money columns (DI-001 / #2124), and which
        // exist and take a value on create.
        var targetColumns = rules
            .Select(r => r.TargetField)
            .Where(c => c is not null && LogicalNamePattern.IsMatch(c))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var attributes = targetColumns.Length == 0
            ? NoAttributes
            : await TargetAttributesAsync(user, table, targetColumns, ct).ConfigureAwait(false);
        if (attributes is null)
        {
            warnings.Add(new RecordCreationWarning(RecordCreationWarning.MappingUnavailableCode, null, MappingTargetUnreadable));
            return unmapped;
        }

        var engineWarnings = new List<string>();
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CreateTimeFieldMapping.ApplyFillOnly(
            rules, target, masked, ProtectedFromMapping(table, rule, request.ServerSetLookupColumns), engineWarnings, _logger,
            moneyAttributes: attributes.Where(a => a.Value.Type == "Money").Select(a => a.Key).ToHashSet(StringComparer.OrdinalIgnoreCase),
            mappedColumns: written);
        warnings.AddRange(engineWarnings.Select(w => new RecordCreationWarning(RecordCreationWarning.MappingRuleSkippedCode, null, w)));
        if (written.Count == 0)
            return unmapped;

        // Each mapped value as the create's item value — or not written, with its column named.
        // Only a logical name reaches the metadata filter (a rule's target is admin-written text).
        var dateColumns = written
            .Where(c => LogicalNamePattern.IsMatch(c) && target.Attributes.TryGetValue(c, out var v) && v is DateTime)
            .ToArray();
        var dateBehaviours = dateColumns.Length == 0
            ? null
            : await DateBehavioursAsync(user, table, dateColumns, ct).ConfigureAwait(false);

        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var column in written.OrderBy(c => c, StringComparer.Ordinal))
        {
            target.Attributes.TryGetValue(column, out var value);
            string? dateBehaviour = null;
            string? notWritten = null;
            if (!attributes.TryGetValue(column, out var attribute) || !attribute.ValidForCreate)
                notWritten = NoSuchColumn;
            else if (!Fits(attribute.Type, value))
                notWritten = CannotTakeValue;
            else if (value is DateTime && (dateBehaviours is null || !dateBehaviours.TryGetValue(column, out dateBehaviour)))
                notWritten = DateBehaviourUnknown;
            else if (ToItemValue(value, dateBehaviour) is { } itemValue)
                values[column] = itemValue;
            else
                notWritten = CannotTakeValue;

            if (notWritten is not null)
                warnings.Add(Dropped(column, notWritten));
        }

        return values.Count == 0
            ? unmapped
            : await MapMergedAsync(user, table, callerItem, values, warnings, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Step 4: the caller's item and the mapped values mapped together, AS THE CALLER. When the whole does not map, each
    /// mapped value is mapped on its own: one the table cannot take as written (a lookup whose column does not reference
    /// the record's table) or whose mapping Dataverse did not answer is dropped, named. Never a refusal — the caller's own
    /// item mapped at step 1, and a mapped value only ever costs itself.
    /// </summary>
    private static async Task<MappingStep> MapMergedAsync(
        IDataverseUserClient user, string table, DataverseWriteItemMapper.MappedItem caller,
        IReadOnlyDictionary<string, JsonElement> values, List<RecordCreationWarning> warnings, CancellationToken ct,
        IReadOnlySet<string>? defaultColumns = null)
    {
        var whole = await DataverseWriteItemMapper.MapAsync(user, table, MergedItem(caller, values.Keys, values), ct)
            .ConfigureAwait(false);
        if (whole.Item is { } merged)
            return new MappingStep(merged, values, null);

        var kept = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (column, value) in values.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var one = await DataverseWriteItemMapper.MapAsync(
                    user, table, MergedItem(caller, values.Keys, [new KeyValuePair<string, JsonElement>(column, value)]), ct)
                .ConfigureAwait(false);
            if (one.Item is null)
            {
                warnings.Add(Dropped(column, one.ClientFailure is null ? CannotTakeValue : CouldNotBeChecked, defaultColumns));
                continue;
            }

            kept[column] = value;
        }

        if (kept.Count == 0)
            return MappingStep.Unmapped(caller);

        var remapped = await DataverseWriteItemMapper.MapAsync(user, table, MergedItem(caller, kept.Keys, kept), ct)
            .ConfigureAwait(false);
        if (remapped.Item is { } item)
            return new MappingStep(item, kept, null);

        // Each mapped on its own but not together: none is written (which one cannot be told).
        foreach (var column in kept.Keys)
            warnings.Add(Dropped(column, remapped.ClientFailure is null ? CannotTakeValue : CouldNotBeChecked, defaultColumns));
        return MappingStep.Unmapped(caller);
    }

    /// <summary>The caller's item with <paramref name="kept"/> merged in, mapped as the caller (the caller's own when none is kept).</summary>
    private static async Task<MappingStep> RemapAsync(
        IDataverseUserClient user, string table, DataverseWriteItemMapper.MappedItem caller,
        IReadOnlyDictionary<string, JsonElement> kept, string noun, CancellationToken ct)
    {
        if (kept.Count == 0)
            return MappingStep.Unmapped(caller);

        var mapped = await DataverseWriteItemMapper.MapAsync(user, table, MergedItem(caller, kept.Keys, kept), ct).ConfigureAwait(false);
        return mapped switch
        {
            { Item: { } item } => new MappingStep(item, kept, null),
            { ClientFailure: { } failure } => MappingStep.Refused(Upstream(failure, noun)),
            _ => MappingStep.Refused(new RecordCreationRefusal(RecordCreationRefusalKind.Invalid,
                RecordCreationRefusal.InvalidPayloadCode, mapped.ValidationError ?? $"The {noun} could not be mapped.")),
        };
    }

    /// <summary>
    /// Step 5 with mapped values (see the remarks): the caller's own values alone, as with no mapping (refuse); the merged
    /// row whole; on a failure or fault, the caller's values with the mapped filing lookups (refuse), then each other mapped
    /// value on its own (drop), then the row as written (refuse).
    /// </summary>
    private async Task<MappingStep> CheckMergedPayloadAsync(
        string table, IDataverseUserClient user, Guid me, DataverseWriteItemMapper.MappedItem callerItem,
        DataverseWriteItemMapper.MappedItem merged, IReadOnlyDictionary<string, JsonElement> mappedValues,
        IReadOnlySet<string>? serverSetLookupColumns, string noun, List<RecordCreationWarning> warnings, CancellationToken ct,
        IReadOnlySet<string>? defaultColumns = null)
    {
        // The caller's own values first, exactly task 013's one check: a failure refuses unchanged and a fault propagates
        // (NFR-01). Fix round 1 (review K-3): asked whole first, a mapped lookup could complete a caller's untyped pair that
        // is refused without mapping — no mapped value may make the caller's own payload pass.
        var callersOwn = await CallerWriteAuthorization.CheckCallerMayCreateAsync(user, me, table, callerItem, serverSetLookupColumns, ct)
            .ConfigureAwait(false);
        if (!callersOwn.Succeeded)
            return MappingStep.Refused(CallerCheckRefused(callersOwn, noun));

        OwnedChildWrite.Outcome? whole;
        try
        {
            whole = await CallerWriteAuthorization.CheckCallerMayCreateAsync(user, me, table, merged, serverSetLookupColumns, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Not a pass and not yet a refusal: which value's check faulted is asked below (NFR-01 per origin).
            _logger.LogWarning(ex, "[RECORD-CREATE] the merged {Table} payload's caller check faulted; each origin is asked on its own", table);
            whole = null;
        }

        if (whole is { Succeeded: true })
            return new MappingStep(merged, mappedValues, null);

        // The caller's values, with every mapped FILING lookup (FR-08 amendment): a failure refuses, as with no mapping; a
        // fault propagates and is refused (NFR-01). With no mapped filing lookup this is the check above, already passed.
        var filing = merged.Lookups
            .Where(l => mappedValues.ContainsKey(l.Column) && IsFilingLookup(table, l))
            .ToArray();
        if (filing.Length > 0)
        {
            var callers = new DataverseWriteItemMapper.MappedItem(
                callerItem.JsonBody,
                callerItem.Columns.Concat(filing.Select(l => l.Column)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
            {
                Lookups = callerItem.Lookups.Concat(filing).ToArray(),
                ClearedColumns = callerItem.ClearedColumns,
            };
            var callersCheck = await CallerWriteAuthorization.CheckCallerMayCreateAsync(user, me, table, callers, serverSetLookupColumns, ct)
                .ConfigureAwait(false);
            if (!callersCheck.Succeeded)
                return MappingStep.Refused(CallerCheckRefused(callersCheck, noun));
        }

        // Every other mapped value on its own: one that fails, or whose check faults, is dropped — never written.
        var filingColumns = filing.Select(l => l.Column).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kept = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (column, value) in mappedValues.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (filingColumns.Contains(column))
            {
                kept[column] = value;
                continue;
            }

            var alone = new DataverseWriteItemMapper.MappedItem("{}", [column])
            {
                Lookups = merged.Lookups.Where(l => string.Equals(l.Column, column, StringComparison.OrdinalIgnoreCase)).ToArray(),
            };
            string? notWritten;
            try
            {
                var answer = await CallerWriteAuthorization.CheckCallerMayCreateAsync(user, me, table, alone, serverSetLookupColumns, ct)
                    .ConfigureAwait(false);
                notWritten = answer.Succeeded ? null
                    : answer.ClientFailure is not null ? CouldNotBeChecked
                    : NotPermitted;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RECORD-CREATE] the check of the mapped {Table}.{Column} faulted; the value is dropped", table, column);
                notWritten = CouldNotBeChecked;
            }

            if (notWritten is null)
                kept[column] = value;
            else
                warnings.Add(Dropped(column, notWritten, defaultColumns));
        }

        // The row as it will be written, asked whole once more: a failure now is no single value's, so it refuses.
        var final = kept.Count == mappedValues.Count
            ? new MappingStep(merged, mappedValues, null)
            : await RemapAsync(user, table, callerItem, kept, noun, ct).ConfigureAwait(false);
        if (final.Refusal is not null)
            return final;

        var finalCheck = await CallerWriteAuthorization.CheckCallerMayCreateAsync(user, me, table, final.Item!, serverSetLookupColumns, ct)
            .ConfigureAwait(false);
        return finalCheck.Succeeded ? final : MappingStep.Refused(CallerCheckRefused(finalCheck, noun));
    }

    /// <summary>
    /// A dropped mapped value's warning: the column, why, and nothing of the value or its source (ADR-019). A column in
    /// <paramref name="defaultColumns"/> is a post-mapping default (task 018a, K-7), named as one.
    /// </summary>
    private static RecordCreationWarning Dropped(string column, string why, IReadOnlySet<string>? defaultColumns = null) =>
        defaultColumns?.Contains(column) == true
            ? new(RecordCreationWarning.DefaultValueDroppedCode, column, $"The default value for '{column}' was not written: {why}.")
            : new(RecordCreationWarning.MappedValueDroppedCode, column, $"The mapped value for '{column}' was not written: {why}.");

    // ── Task 018a: references (K-3) and post-mapping defaults (K-7) ───────────────────────────────────────────────────────

    private static readonly IReadOnlySet<string> NoDefaultColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, PostMappingDefault> NoDefaults = new Dictionary<string, PostMappingDefault>();

    /// <summary>
    /// Task 018a fix round (F4-1): the columns that confer access on a row — never an optional reference, whatever a table
    /// declares (the secure flag, the access record, the security business unit, the SPE container).
    /// </summary>
    private static readonly IReadOnlySet<string> AccessConferringColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        AccessFollowsParent.SecureFlagColumn, SecurableEntityRegistry.SecureFlagAttribute, InheritedAccessPermission.Column,
        AccessInheritance.Column, SecurableEntityRegistry.SecurityBusinessUnitAttribute, "sprk_containerid",
    };

    /// <summary>A post-mapping default: the SDK value the adapter gave (its type is its FR-02 kind) and its item form.</summary>
    private sealed record PostMappingDefault(object Value, JsonElement ItemValue);

    /// <summary>
    /// Task 018a fix round (F4-1): whether <paramref name="column"/> of <paramref name="table"/> may NEVER be an optional
    /// reference — the "for" person (<see cref="RecordTableRule.ForPerson"/>), a column field mapping never writes
    /// (<see cref="ProtectedFromMapping"/>), the regarding pair and its resolver columns, a filing lookup
    /// (<see cref="IsFilingLookup(string, string)"/>), an access-conferring column, or any Assigned* column. Such a lookup is
    /// the caller's own: one they may not use is refused, never dropped. Asked of every registry entry
    /// (<see cref="RecordTableRule.OptionalReferenceLookups"/>) by the registry tests and of every request by
    /// <see cref="ValidateReferenceLookupColumns"/>.
    /// </summary>
    internal static bool IsNeverAReference(string table, RecordTableRule rule, string column)
    {
        var name = column.Trim();
        return string.Equals(rule.ForPerson?.Column, name, StringComparison.OrdinalIgnoreCase)
            || ProtectedFromMapping(table, rule, serverSetLookupColumns: null).Contains(name)
            || CreateTimeFieldMapping.RegardingPairAndResolverColumns.Contains(name)
            || IsFilingLookup(table, name.ToLowerInvariant())
            || AccessConferringColumns.Contains(name)
            || name.StartsWith("sprk_assigned", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Task 018a fix round (F4-1): a request may mark as references only the lookups its table declares
    /// (<see cref="RecordTableRule.OptionalReferenceLookups"/>) and never one <see cref="IsNeverAReference"/> names. Any
    /// other is the adapter's defect: an <see cref="ArgumentException"/>, thrown before anything is read.
    /// </summary>
    private static void ValidateReferenceLookupColumns(string table, RecordTableRule rule, RecordCreationRequest request)
    {
        if (request.ReferenceLookupColumns is not { Count: > 0 } references)
            return;

        foreach (var reference in references)
        {
            var column = reference?.Trim() ?? string.Empty;
            if (!rule.OptionalReferenceLookups.Contains(column) || IsNeverAReference(table, rule, column))
            {
                throw new ArgumentException(
                    $"'{reference}' is not an optional reference of a {NounOf(table)}: only " +
                    (rule.OptionalReferenceLookups.Count == 0 ? "none" : string.Join(", ", rule.OptionalReferenceLookups.Order(StringComparer.Ordinal))) +
                    " may be dropped with a warning.", nameof(request));
            }
        }
    }

    /// <summary>
    /// The request's post-mapping defaults (<see cref="RecordCreationRequest.PostMappingDefaults"/>), keyed by logical name
    /// (trimmed, lower-case). Each is the adapter's defect, an <see cref="ArgumentException"/> thrown before anything is
    /// read, when: its table maps nothing (<see cref="RecordTableRule.MappingExclusion"/>, as field mapping); two keys name
    /// the same column; it is aimed at a column field mapping may never write (<see cref="ProtectedFromMapping"/>, the
    /// regarding pair and its resolver columns); or its value is in a form a mapping rule could not write either (no Web API
    /// form; a date, whose storage behaviour a default cannot state).
    /// </summary>
    private static IReadOnlyDictionary<string, PostMappingDefault> PostMappingDefaultValues(
        string table, RecordTableRule rule, RecordCreationRequest request)
    {
        if (request.PostMappingDefaults is not { Count: > 0 } supplied)
            return NoDefaults;

        if (rule.MappingExclusion is { } exclusion)
        {
            throw new ArgumentException(
                $"A {NounOf(table)} takes no post-mapping default: field mapping does not apply to it ({exclusion.Name}).", nameof(request));
        }

        var protectedColumns = ProtectedFromMapping(table, rule, request.ServerSetLookupColumns);
        var values = new Dictionary<string, PostMappingDefault>(StringComparer.Ordinal);
        foreach (var (key, value) in supplied)
        {
            var column = key?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(column) || !LogicalNamePattern.IsMatch(column))
                throw new ArgumentException($"A post-mapping default names '{key}', which is not a column's logical name.", nameof(request));

            if (values.ContainsKey(column))
                throw new ArgumentException($"Two post-mapping defaults name '{column}'.", nameof(request));

            if (protectedColumns.Contains(column) || CreateTimeFieldMapping.RegardingPairAndResolverColumns.Contains(column))
            {
                throw new ArgumentException(
                    $"A post-mapping default may not set '{column}' of a {NounOf(table)}: field mapping never writes it.", nameof(request));
            }

            if (value is null or DateTime || ToItemValue(value, dateBehaviour: null) is not { } itemValue)
            {
                throw new ArgumentException(
                    $"The post-mapping default for '{column}' is not a value a mapping rule writes.", nameof(request));
            }

            values[column] = new PostMappingDefault(value, itemValue);
        }

        return values;
    }

    /// <summary>
    /// Task 037 (FR-36): <paramref name="defaults"/> plus the table's copied defaults on the row as mapped
    /// (<see cref="RecordTableRules.CopiedDefaultsFor"/>): each lookup the row binds in the source column, as-is, for a target
    /// the row leaves UNSET — so an explicit null the caller sent is never filled (it is in the row's columns). A default the
    /// request already names for that column is the adapter's and is kept. A source the row leaves unset adds nothing and
    /// is reported as a structured warning: the column stays blank — unless a default the request names fills that source
    /// (task 017, 037 review K2: the copy runs after the defaults merge), which is then copied, as the caller's own.
    /// </summary>
    /// <returns>The defaults, and each (column, source column) pair a copied default was added for — so a copy of a
    /// SERVER-set source can be server-set too (the owned create's rule, <c>OwnedChildWrite.CreateAsync</c>).</returns>
    private (IReadOnlyDictionary<string, PostMappingDefault> Defaults, IReadOnlyList<(string Column, string Source)> Copied) WithCopiedDefaults(
        string table, RecordTableRule rule, DataverseWriteItemMapper.MappedItem item,
        IReadOnlyDictionary<string, PostMappingDefault> defaults)
    {
        var fills = RecordTableRules.CopiedDefaultsFor(rule, item);
        if (fills.Count == 0)
            return (defaults, []);

        var copied = new List<(string Column, string Source)>();
        var merged = new Dictionary<string, PostMappingDefault>(defaults, StringComparer.Ordinal);
        foreach (var fill in fills)
        {
            var column = fill.Default.Column.Trim().ToLowerInvariant();
            if (fill.Source is null
                && fill.Default.CopiedFrom?.Trim().ToLowerInvariant() is { } sourceColumn
                && defaults.TryGetValue(sourceColumn, out var sourceDefault)
                && sourceDefault.Value is EntityReference { Id: var defaultId } defaultSource
                && defaultId != Guid.Empty)
            {
                // Task 017 (037 review K2): the copy runs AFTER the adapter's post-mapping defaults are merged — a source the
                // row leaves unset but an adapter default fills is copied too, so the target names the record the source will.
                // Such a copy is never server-set: a default may not name a server-set column (PostMappingDefaultValues), so it
                // is not added to the copies the request's server-set set is widened by; step 5 asks it as the caller's own.
                var copy = new EntityReference(defaultSource.LogicalName, defaultId);
                if (ToItemValue(copy, dateBehaviour: null) is { } copyValue)
                    merged.TryAdd(column, new PostMappingDefault(copy, copyValue));
                continue;
            }

            if (fill.Source is not { } source)
            {
                _logger.LogWarning(
                    "copied_default_source_unset: entity={Entity} column={Column} source={Source} — the create leaves both " +
                    "unset, so the column stays blank (task 037, FR-36)",
                    table, column, fill.Default.CopiedFrom);
                continue;
            }

            var value = new EntityReference(source.RelatedTable, source.RecordId);
            // A default the request names for the column is the adapter's: it is kept, and is never made server-set.
            if (ToItemValue(value, dateBehaviour: null) is { } itemValue && merged.TryAdd(column, new PostMappingDefault(value, itemValue)))
                copied.Add((column, source.Column));
        }

        return (merged, copied);
    }

    /// <summary>
    /// The defaults the row still lacks after step 3 — FR-02's "not supplied", as <see cref="CreateTimeFieldMapping.ApplyFillOnly"/>
    /// tests it: a flag or a choice (single or multi-select) is supplied whenever its key is present, an explicit null
    /// included; any other column is supplied unless absent, null, blank text, or a lookup with no id. A caller's value or a
    /// mapped one always wins.
    /// </summary>
    private static Dictionary<string, PostMappingDefault> DefaultsToFill(
        string table, DataverseWriteItemMapper.MappedItem item, IReadOnlyDictionary<string, PostMappingDefault> defaults)
    {
        var row = CallerEntity(table, item);
        var unfilled = new Dictionary<string, PostMappingDefault>(StringComparer.Ordinal);
        foreach (var (column, value) in defaults)
        {
            var present = row.Attributes.TryGetValue(column, out var held);
            var supplied = value.Value is bool or OptionSetValue or OptionSetValueCollection
                ? present
                : present && held switch
                {
                    null => false,
                    string text => !string.IsNullOrWhiteSpace(text),
                    EntityReference reference => reference.Id != Guid.Empty,
                    _ => true,
                };
            if (!supplied)
                unfilled[column] = value;
        }

        return unfilled;
    }

    /// <summary>
    /// Task 018a fix round (F4-3): the unfilled defaults the table can take, judged by field mapping's own checks on the
    /// target columns read as the caller (<see cref="TargetAttributesAsync"/>, <see cref="Fits"/>): a column that does not
    /// exist or takes no value on create, or a value of the wrong kind, is dropped with
    /// <see cref="RecordCreationWarning.DefaultValueDroppedCode"/> — never a fault that fails the create. Metadata that
    /// cannot be read drops every default ("it could not be checked").
    /// </summary>
    private async Task<Dictionary<string, JsonElement>> FittingDefaultsAsync(
        IDataverseUserClient user, string table, IReadOnlyDictionary<string, PostMappingDefault> unfilled,
        IReadOnlySet<string> defaultColumns, List<RecordCreationWarning> warnings, CancellationToken ct)
    {
        IReadOnlyDictionary<string, TargetAttribute>? attributes;
        try
        {
            attributes = await TargetAttributesAsync(user, table, unfilled.Keys.ToArray(), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RECORD-CREATE] the {Table} columns of the post-mapping defaults could not be read; none is written",
                table);
            attributes = null;
        }

        var fitting = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (column, value) in unfilled.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var notWritten = attributes is null ? CouldNotBeChecked
                : !attributes.TryGetValue(column, out var attribute) || !attribute.ValidForCreate ? NoSuchColumn
                : !Fits(attribute.Type, value.Value) ? CannotTakeValue
                : null;
            if (notWritten is null)
                fitting[column] = value.ItemValue;
            else
                warnings.Add(Dropped(column, notWritten, defaultColumns));
        }

        return fitting;
    }

    /// <summary>
    /// Task 018a (K-3): each lookup of <paramref name="item"/> that <paramref name="references"/> names, asked ALONE as the
    /// caller (the check a mapped value gets, <see cref="CallerWriteAuthorization.CheckCallerMayCreateAsync"/>). One the caller
    /// may not use, or whose check faulted or could not be answered, is dropped with a warning naming the column — the same
    /// words for "not permitted" and "does not exist", so a dropped reference tells nothing about the record. A FILING lookup
    /// (<see cref="IsFilingLookup(string, DataverseWriteItemMapper.MappedLookup)"/>) and a server-set lookup are never
    /// references: they are left for step 5 (and step 6) to judge as the caller's own. What is left is mapped again as the
    /// caller; a mapping Dataverse refuses then is that refusal.
    /// </summary>
    private async Task<MappingStep> DropUnusableReferencesAsync(
        string table, IDataverseUserClient user, Guid me, DataverseWriteItemMapper.MappedItem item, IReadOnlySet<string> references,
        IReadOnlySet<string>? serverSetLookupColumns, string noun, List<RecordCreationWarning> warnings, CancellationToken ct)
    {
        var named = references.Select(c => c.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var serverSet = serverSetLookupColumns ?? NoDefaultColumns;
        var dropped = new List<string>();
        foreach (var lookup in item.Lookups)
        {
            if (!named.Contains(lookup.Column) || serverSet.Contains(lookup.Column) || IsFilingLookup(table, lookup))
                continue;

            var alone = new DataverseWriteItemMapper.MappedItem("{}", [lookup.Column]) { Lookups = [lookup] };
            string? notWritten;
            try
            {
                var answer = await CallerWriteAuthorization.CheckCallerMayCreateAsync(user, me, table, alone, serverSetLookupColumns, ct)
                    .ConfigureAwait(false);
                notWritten = answer.Succeeded ? null
                    : answer.ClientFailure is not null ? CouldNotBeChecked
                    : NotUsable;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[RECORD-CREATE] the check of the reference {Table}.{Column} faulted; the value is dropped",
                    table, lookup.Column);
                notWritten = CouldNotBeChecked;
            }

            if (notWritten is null)
                continue;

            dropped.Add(lookup.Column);
            warnings.Add(new RecordCreationWarning(RecordCreationWarning.ReferenceValueDroppedCode, lookup.Column,
                $"The value you chose for '{lookup.Column}' was not written: {notWritten}; the {noun} was created without it."));
        }

        if (dropped.Count == 0)
            return new MappingStep(item, NoMappedValues, null);

        var remapped = await DataverseWriteItemMapper.MapAsync(user, table, MergedItem(item, dropped, NoMappedValues), ct)
            .ConfigureAwait(false);
        return remapped switch
        {
            { Item: { } kept } => new MappingStep(kept, NoMappedValues, null),
            { ClientFailure: { } failure } => MappingStep.Refused(Upstream(failure, noun)),
            _ => MappingStep.Refused(new RecordCreationRefusal(RecordCreationRefusalKind.Invalid,
                RecordCreationRefusal.InvalidPayloadCode, remapped.ValidationError ?? $"The {noun} could not be mapped.")),
        };
    }

    /// <summary>
    /// The caller's item as SDK attributes — lookups as <see cref="EntityReference"/>, the pair id as text, clears as
    /// <c>null</c> — the fill-only snapshot's and the source resolver's input.
    /// </summary>
    private static Entity CallerEntity(string table, DataverseWriteItemMapper.MappedItem item)
    {
        var entity = new Entity(table);
        using (var body = JsonDocument.Parse(item.JsonBody))
        {
            foreach (var property in body.RootElement.EnumerateObject())
            {
                if (property.Name.Contains('@', StringComparison.Ordinal))
                    continue; // a lookup's bind or clear: taken from Lookups and ClearedColumns below

                entity[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    JsonValueKind.Number => property.Value.TryGetDecimal(out var number) ? number : property.Value.GetRawText(),
                    _ => property.Value.GetRawText(),
                };
            }
        }

        foreach (var lookup in item.Lookups)
            entity[lookup.Column] = new EntityReference(lookup.RelatedTable, lookup.RecordId);
        foreach (var cleared in item.ClearedColumns)
        {
            if (!entity.Attributes.ContainsKey(cleared))
                entity[cleared] = null;
        }

        return entity;
    }

    /// <summary>
    /// The item <see cref="DataverseWriteItemMapper.MapAsync"/> maps: the caller's values except <paramref name="notFromCaller"/>
    /// (the columns the mapped values fill — a column the caller left empty or cleared), then <paramref name="mapped"/>.
    /// </summary>
    private static JsonElement MergedItem(
        DataverseWriteItemMapper.MappedItem caller, IEnumerable<string> notFromCaller, IEnumerable<KeyValuePair<string, JsonElement>> mapped)
    {
        var skip = new HashSet<string>(notFromCaller, StringComparer.OrdinalIgnoreCase);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            var plain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var body = JsonDocument.Parse(caller.JsonBody))
            {
                foreach (var property in body.RootElement.EnumerateObject())
                {
                    if (property.Name.Contains('@', StringComparison.Ordinal))
                        continue; // a lookup's bind or clear: written from Lookups and ClearedColumns below
                    plain.Add(property.Name);
                    if (!skip.Contains(property.Name))
                        property.WriteTo(writer);
                }
            }

            foreach (var lookup in caller.Lookups.Where(l => !skip.Contains(l.Column)))
            {
                writer.WriteStartObject(lookup.Column);
                writer.WriteString("relatedTable", lookup.RelatedTable);
                writer.WriteString("recordId", lookup.RecordId.ToString("D"));
                writer.WriteEndObject();
            }

            foreach (var cleared in caller.ClearedColumns.Where(c => !plain.Contains(c) && !skip.Contains(c)))
                writer.WriteNull(cleared); // a lookup's clear (its bind is the navigation property's, re-derived)

            foreach (var (column, value) in mapped)
            {
                writer.WritePropertyName(column);
                value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        using var merged = JsonDocument.Parse(stream.ToArray());
        return merged.RootElement.Clone();
    }

    /// <summary>The record-type catalog read AS THE CALLER (the resolver's delegate): the table a type row names, or <c>null</c>.</summary>
    private static async Task<string?> RecordTypeTableAsync(IDataverseUserClient user, Guid typeId, CancellationToken ct)
    {
        var row = await user.GetAsync(
                $"{OwnedChildWrite.RecordTypeEntitySet}({typeId:D})?$select={OwnedChildWrite.RecordTypeLogicalNameColumn}", ct)
            .ConfigureAwait(false);
        return row.IsSuccess
               && row.Body is { } body
               && body.TryGetProperty(OwnedChildWrite.RecordTypeLogicalNameColumn, out var name)
               && name.ValueKind == JsonValueKind.String
            ? name.GetString()
            : null;
    }

    /// <summary>The target columns' type and create-validity, read AS THE CALLER; <c>null</c> when Dataverse did not answer.</summary>
    private static async Task<IReadOnlyDictionary<string, TargetAttribute>?> TargetAttributesAsync(
        IDataverseUserClient user, string table, IReadOnlyCollection<string> columns, CancellationToken ct)
    {
        var filter = string.Join(" or ", columns.Select(c => $"LogicalName eq '{c}'"));
        var response = await user.GetAsync(
                $"EntityDefinitions(LogicalName='{table}')/Attributes?$select=LogicalName,AttributeType,IsValidForCreate" +
                $"&$filter={Uri.EscapeDataString(filter)}", ct)
            .ConfigureAwait(false);
        if (!response.IsSuccess || response.Body is not { } body
            || !body.TryGetProperty("value", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var attributes = new Dictionary<string, TargetAttribute>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in list.EnumerateArray())
        {
            if (attribute.TryGetProperty("LogicalName", out var name) && name.ValueKind == JsonValueKind.String
                && attribute.TryGetProperty("AttributeType", out var type) && type.ValueKind == JsonValueKind.String)
            {
                attributes[name.GetString()!] = new TargetAttribute(type.GetString()!,
                    attribute.TryGetProperty("IsValidForCreate", out var valid) && valid.ValueKind == JsonValueKind.True);
            }
        }

        return attributes;
    }

    /// <summary>
    /// How each date column stores a date (<c>DateTimeBehavior</c>: <c>DateOnly</c>, <c>UserLocal</c>,
    /// <c>TimeZoneIndependent</c>), read AS THE CALLER — a date-only column takes <c>yyyy-MM-dd</c> on the Web API, any other
    /// a UTC timestamp. <c>null</c> when Dataverse did not answer.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, string>?> DateBehavioursAsync(
        IDataverseUserClient user, string table, IReadOnlyCollection<string> columns, CancellationToken ct)
    {
        var filter = string.Join(" or ", columns.Select(c => $"LogicalName eq '{c}'"));
        var response = await user.GetAsync(
                $"EntityDefinitions(LogicalName='{table}')/Attributes/Microsoft.Dynamics.CRM.DateTimeAttributeMetadata" +
                $"?$select=LogicalName,DateTimeBehavior&$filter={Uri.EscapeDataString(filter)}", ct)
            .ConfigureAwait(false);
        if (!response.IsSuccess || response.Body is not { } body
            || !body.TryGetProperty("value", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var behaviours = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in list.EnumerateArray())
        {
            if (attribute.TryGetProperty("LogicalName", out var name) && name.ValueKind == JsonValueKind.String
                && attribute.TryGetProperty("DateTimeBehavior", out var behaviour) && behaviour.ValueKind == JsonValueKind.Object
                && behaviour.TryGetProperty("Value", out var kind) && kind.ValueKind == JsonValueKind.String)
            {
                behaviours[name.GetString()!] = kind.GetString()!;
            }
        }

        return behaviours;
    }

    /// <summary>Whether a mapped SDK value fits a column of <paramref name="attributeType"/> (the Web API's <c>AttributeType</c>).</summary>
    internal static bool Fits(string attributeType, object? value) => attributeType switch
    {
        "String" or "Memo" => value is string,
        "Integer" => value is int,
        "BigInt" => value is int or long,
        "Decimal" or "Double" or "Money" => value is int or long or decimal or double or Money,
        "Boolean" => value is bool,
        "Picklist" or "State" or "Status" => value is OptionSetValue,
        "Virtual" => value is OptionSetValueCollection, // a multi-select choice
        "DateTime" => value is DateTime,
        "Lookup" or "Customer" => value is EntityReference,
        _ => false,
    };

    /// <summary>
    /// A mapped SDK value as the create's item value (<see cref="DataverseWriteItemMapper"/>'s contract): a lookup as
    /// <c>{relatedTable, recordId}</c>, a choice as its number, money as its amount, a multi-select choice as its
    /// comma-separated numbers, a date as the column stores it. <c>null</c> for a value the Web API has no form for.
    /// </summary>
    internal static JsonElement? ToItemValue(object? value, string? dateBehaviour)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            switch (value)
            {
                case string text:
                    writer.WriteStringValue(text);
                    break;
                case bool flag:
                    writer.WriteBooleanValue(flag);
                    break;
                case int whole:
                    writer.WriteNumberValue(whole);
                    break;
                case long big:
                    writer.WriteNumberValue(big);
                    break;
                case decimal number:
                    writer.WriteNumberValue(number);
                    break;
                case double real when double.IsFinite(real):
                    writer.WriteNumberValue(real);
                    break;
                case Money money:
                    writer.WriteNumberValue(money.Value);
                    break;
                case OptionSetValue option:
                    writer.WriteNumberValue(option.Value);
                    break;
                case OptionSetValueCollection options when options.Count > 0:
                    writer.WriteStringValue(string.Join(",", options.Select(o => o.Value.ToString(CultureInfo.InvariantCulture))));
                    break;
                case DateTime date:
                    writer.WriteStringValue(string.Equals(dateBehaviour, "DateOnly", StringComparison.Ordinal)
                        ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : (date.Kind == DateTimeKind.Local ? date.ToUniversalTime() : date)
                            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
                    break;
                case EntityReference reference when reference.Id != Guid.Empty && !string.IsNullOrWhiteSpace(reference.LogicalName):
                    writer.WriteStartObject();
                    writer.WriteString("relatedTable", reference.LogicalName.Trim().ToLowerInvariant());
                    writer.WriteString("recordId", reference.Id.ToString("D"));
                    writer.WriteEndObject();
                    break;
                default:
                    return null;
            }
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    /// <summary>
    /// A rule with its target as a logical name (trimmed, lower-case): the caller's columns are logical names, so a rule
    /// spelled <c>"  SPRK_Name "</c> sees the caller's <c>sprk_name</c> as supplied (fill-only), and one aimed at a protected
    /// column is refused whatever its spelling. A copy: the profile reader's rules are never changed.
    /// </summary>
    internal static FieldMappingRuleEntity NormalizedRule(FieldMappingRuleEntity rule) => new()
    {
        Id = rule.Id,
        Name = rule.Name,
        ProfileId = rule.ProfileId,
        SourceField = rule.SourceField,
        SourceFieldType = rule.SourceFieldType,
        TargetField = rule.TargetField?.Trim().ToLowerInvariant()!,
        TargetFieldType = rule.TargetFieldType,
        MappingType = rule.MappingType,
        CompatibilityMode = rule.CompatibilityMode,
        IsRequired = rule.IsRequired,
        DefaultValue = rule.DefaultValue,
        Expression = rule.Expression,
        IsCascadingSource = rule.IsCascadingSource,
        ExecutionOrder = rule.ExecutionOrder,
        IsActive = rule.IsActive,
        SyncMode = rule.SyncMode,
    };
}
