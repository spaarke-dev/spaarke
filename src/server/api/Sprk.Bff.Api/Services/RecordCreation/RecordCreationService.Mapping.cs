using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
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
/// (<see cref="IsFilingLookup"/>), which files the row as a caller's lookup would — are asked: a failure refuses with the
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

    private static readonly IReadOnlyDictionary<string, JsonElement> NoMappedValues = new Dictionary<string, JsonElement>();

    private static readonly IReadOnlyDictionary<string, TargetAttribute> NoAttributes =
        new Dictionary<string, TargetAttribute>(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex LogicalNamePattern = new("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant);

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

    /// <summary>A child's lineage or ownership-parent lookup (the K-2 guard's set; see <see cref="ServerSetFilingColumn"/>).</summary>
    private static bool IsChildFilingLookup(string table, string column) =>
        (SecureChildLineage.Children.TryGetValue(table, out var lineage) && lineage.Lookups.ContainsKey(column))
        || RecordReparent.FilingParentOf(table, column) is not null;

    /// <summary>
    /// UAC-r2's review of 013 (K-2): a lookup the SERVER set costs no AppendTo at step 5
    /// (<see cref="RecordCreationRequest.ServerSetLookupColumns"/>), so a lineage or ownership-parent lookup must never be one
    /// — it would file the row under a record nobody asked about. The first such column, or <c>null</c>. A work assignment's
    /// or project's own filing column is not refused here: the secure-root plan asks AppendTo on it when the record is
    /// SECURE (task 013's callback), and <see cref="ServerSetRootFilingLookups"/> has it asked after the plan otherwise.
    /// </summary>
    private static string? ServerSetFilingColumn(string table, IReadOnlySet<string>? serverSetLookupColumns) =>
        serverSetLookupColumns?.FirstOrDefault(c => IsChildFilingLookup(table, c.Trim().ToLowerInvariant()));

    /// <summary>
    /// UAC-r2's review of 013 (K-2), task 014 fix round 1: the lookups of <paramref name="item"/> that file a work assignment
    /// or project (<see cref="SecureRootInheritance.FilingColumnsOf"/>) and that the SERVER set. Step 5 skipped their
    /// AppendTo and the plan asks it on a secure record only, so step 6 asks it, as the caller, whatever the record — none
    /// is ever filed under unasked. Empty for any other table, and when the server set nothing.
    /// </summary>
    private static DataverseWriteItemMapper.MappedLookup[] ServerSetRootFilingLookups(
        string table, DataverseWriteItemMapper.MappedItem item, IReadOnlySet<string>? serverSetLookupColumns)
    {
        if (serverSetLookupColumns is not { Count: > 0 } || !SecureRootInheritance.Inherits(table))
            return [];

        var serverSet = serverSetLookupColumns.Select(c => c.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filing = SecureRootInheritance.FilingColumnsOf(table);
        return item.Lookups.Where(l => serverSet.Contains(l.Column) && filing.Contains(l.Column)).ToArray();
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
    /// lookups the server set (K-2: never filled by a mapped value the AppendTo check would then skip).
    /// </summary>
    private static IReadOnlySet<string> ProtectedFromMapping(
        string table, RecordTableRule rule, IReadOnlySet<string>? serverSetLookupColumns)
    {
        var columns = new HashSet<string>(RecordTableRules.CommonServerOwnedColumns, StringComparer.OrdinalIgnoreCase);
        columns.UnionWith(rule.TableServerOwnedColumns);
        columns.UnionWith(rule.MappingProtectedColumns);
        if (rule.AutonumberColumn is { } number)
            columns.Add(number);
        columns.Add(RecordCreatorPerson.Column);
        if (SecureRootInheritance.Inherits(table))
        {
            columns.Add(AccessInheritance.Column);
            columns.Add(AccessFollowsParent.SecureFlagColumn);
        }

        if (serverSetLookupColumns is not null)
            columns.UnionWith(serverSetLookupColumns.Select(c => c.Trim()));
        return columns;
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

        // FR-04: Read as the caller FIRST. Missing, denied, unmapped type, a failed probe or read: ONE outcome — nothing
        // mapped (not even a Default rule), one warning that names nothing.
        var read = await MappingSourceReadProbe.ReadAsync(
                mapping.Reader, source, CreateTimeFieldMapping.CollectSourceColumns(rules), _entities, _logger, ct)
            .ConfigureAwait(false);
        if (!read.Readable)
        {
            warnings.Add(new RecordCreationWarning(RecordCreationWarning.MappingSourceUnavailableCode, null,
                read.Warning ?? MappingSourceReadProbe.SourceUnavailableWarning));
            return unmapped;
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
            rules, target, read.Record, ProtectedFromMapping(table, rule, request.ServerSetLookupColumns), engineWarnings, _logger,
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
        IReadOnlyDictionary<string, JsonElement> values, List<RecordCreationWarning> warnings, CancellationToken ct)
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
                warnings.Add(Dropped(column, one.ClientFailure is null ? CannotTakeValue : CouldNotBeChecked));
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
            warnings.Add(Dropped(column, remapped.ClientFailure is null ? CannotTakeValue : CouldNotBeChecked));
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
        IReadOnlySet<string>? serverSetLookupColumns, string noun, List<RecordCreationWarning> warnings, CancellationToken ct)
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
            .Where(l => mappedValues.ContainsKey(l.Column) && IsFilingLookup(table, l.Column))
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
                warnings.Add(Dropped(column, notWritten));
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

    /// <summary>A dropped mapped value's warning: the column, why, and nothing of the value or its source (ADR-019).</summary>
    private static RecordCreationWarning Dropped(string column, string why) => new(
        RecordCreationWarning.MappedValueDroppedCode, column, $"The mapped value for '{column}' was not written: {why}.");

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
