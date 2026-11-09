using System.Globalization;
using System.Text.Json;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using FamilyKind = Sprk.Bff.Api.Services.Dataverse.RecordFamilyEdges.FamilyKind;

namespace Sprk.Bff.Api.Api.FieldMappings;

/// <summary>
/// field-mapping-server-write-path-r1 task 040 (FR-34): how a push finds its children through the family, and the preview of
/// what it would write.
/// </summary>
/// <remarks>
/// <para><b>Children.</b> A push that previews, or that names the children it confirmed, finds them through
/// <see cref="RecordFamilyReader.ReadChildrenAsync"/>: a Filing edge from the source to the target (a typed lookup, or the
/// regarding pair of a project under a matter), read AS THE CALLER, a child listed only when the source is its PRIMARY parent,
/// and a detail table never. The server re-derives the children itself and intersects them with the confirmed ids; an id the
/// caller's own walk did not derive is refused per id and never written.</para>
/// <para>A confirmed id the walk did not derive is never previewed: the preview returns it in <c>refusedChildIds</c>, so the
/// person is told it will not be updated (the apply refuses it per id with <see cref="ChildNotInPushMessage"/>).</para>
/// <para><b>Preview.</b> Nothing is written. Each child the caller may write, and each column a rule would change, is listed
/// with what it holds now and would hold, and the Assigned-To policies the change meets. A child the caller cannot write is
/// counted, not listed. Values are read AS THE CALLER, so a field-secured value reads as empty.</para>
/// <para>Static helpers beside the endpoint they serve, like <see cref="FieldMappingPushLookups"/> (ADR-010).</para>
/// </remarks>
internal static class FieldMappingPushFamily
{
    /// <summary>The fixed text of a confirmed id the caller's own walk did not derive. Names no table and no id.</summary>
    internal const string ChildNotInPushMessage = "This record is not one of the records this push updates; it was left unchanged.";

    /// <summary>The most children one request asks Dataverse about at a time (keeps the OData URL short).</summary>
    private const int IdsPerQuery = 50;

    /// <summary>
    /// The confirmed children: what the caller's own walk derived, intersected with <paramref name="confirmed"/>. An id the walk
    /// did not derive is returned in <c>Refused</c> (duplicates collapse).
    /// </summary>
    internal static (IReadOnlyList<RecordFamilyMember> Children, IReadOnlyList<Guid> Refused) Confirm(
        IReadOnlyList<RecordFamilyMember> derived, IReadOnlyCollection<Guid>? confirmed)
    {
        if (confirmed is null)
            return (derived, Array.Empty<Guid>());

        var wanted = confirmed.Distinct().ToList();
        var byId = derived.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        return (wanted.Where(byId.ContainsKey).Select(id => byId[id]).ToList(), wanted.Where(id => !byId.ContainsKey(id)).ToList());
    }

    /// <summary>What a preview needs besides the services.</summary>
    internal sealed record PreviewInput(
        string TargetEntity,
        IReadOnlyList<RecordFamilyMember> Children,
        bool Truncated,
        FieldMappingRuleDto[] Rules,
        Dictionary<string, object?> SourceValues,
        IReadOnlyDictionary<FieldMappingRuleDto, LookupBindPlan> LookupBinds,
        int SkippedRuleCount,
        Func<string, string, bool> IsAssignedToColumn,
        FieldMappingPushSourceGate SourceGate,
        IReadOnlyList<Guid>? RefusedChildIds = null);

    /// <summary>One value a rule would write: the same for every child, so computed once.</summary>
    private sealed record PlannedChange(FieldMappingRuleDto Rule, string Key, object? Wire, string To, Guid? LookupId, bool IsAssignedTo);

    /// <summary>
    /// The preview: per child the caller may write, the columns that would change. Writes nothing. A fault propagates (the
    /// route's 500); nothing is read app-only.
    /// </summary>
    internal static async Task<PushFieldMappingsResponse> PreviewAsync(
        PreviewInput input,
        string? callerToken,
        Guid callerSystemUserId,
        CallerRecordAccessProbe probe,
        IImpersonatedCommunicationQuery impersonatedQuery,
        IGenericEntityService entityService,
        EffectiveFlagsReader? effectiveFlags,
        CancellationToken ct,
        Microsoft.Extensions.Logging.ILogger? logger = null)
    {
        var planned = Plan(input, logger);

        // What the apply would write to a child (the same rules, in the same order: a later rule's key wins), so the source
        // rule is asked of the preview with the payload the apply asks it with.
        var pushPayload = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var change in planned)
            pushPayload[change.Key] = change.Wire;

        var changes = new List<PushPreviewChange>();
        var notWritten = new List<PushFieldMappingsError>();
        var skippedNoWrite = 0;

        if (input.Children.Count > 0)
        {
            var childSet = await entityService.GetEntitySetNameAsync(input.TargetEntity, ct);

            // Only a child the caller may write is listed; the rest are counted. The rights are the caller's own, asked in one
            // batch; an answer that cannot be had reads as no Write (fail closed).
            var rights = await probe.GetCallerRightsForRecordsAsync(
                callerToken, input.Children.Select(c => (childSet, c.Id)).ToList(), ct);
            var writable = input.Children.Where((_, i) => OperationAccessPolicy.HasRequiredRights(rights[i], "write")).ToList();
            skippedNoWrite = input.Children.Count - writable.Count;

            if (writable.Count > 0 && planned.Count > 0)
            {
                var attributes = FieldMappingPushLookups.ReadableAttributes(await impersonatedQuery.QueryAsync(
                    FieldMappingPushLookups.AttributeMetadataPath(input.TargetEntity),
                    FieldMappingPushLookups.AttributeMetadataQuery,
                    callerSystemUserId,
                    ct));
                var rows = await ReadCurrentValuesAsync(
                    impersonatedQuery, childSet, input.TargetEntity, writable, planned, attributes, callerSystemUserId, ct);
                var flags = planned.Any(p => p.IsAssignedTo)
                    ? await ReadFlagsAsync(effectiveFlags, input.TargetEntity, writable, ct)
                    : new Dictionary<Guid, RootRecordFlags>();

                foreach (var child in writable)
                {
                    rows.TryGetValue(child.Id, out var row);
                    var childChanges = new List<PushPreviewChange>();
                    foreach (var change in planned)
                    {
                        var column = change.Rule.TargetField;
                        var isLookup = attributes.TryGetValue(column, out var type) && FieldMappingPushLookups.IsLookupType(type);
                        var (from, same) = isLookup
                            ? CurrentLookup(row, column, change.LookupId)
                            : Current(row, column, change.Wire);
                        if (same)
                            continue;

                        childChanges.Add(new PushPreviewChange
                        {
                            RecordId = child.Id,
                            Name = child.Name,
                            Column = column,
                            From = from,
                            To = change.To,
                            Flags = change.IsAssignedTo ? FlagNames(flags.GetValueOrDefault(child.Id, Unreadable)) : [],
                        });
                    }

                    // Task 022 fix round 4 (the mapping-source rule): the same question the apply asks before it writes. A child
                    // the apply would not write is not listed with values; it is returned with its code instead.
                    if (childChanges.Count > 0
                        && await input.SourceGate.RefusalForChildAsync(input.TargetEntity, child.Id, pushPayload, ct).ConfigureAwait(false) is { } refusal)
                    {
                        notWritten.Add(new PushFieldMappingsError
                        {
                            RecordId = child.Id,
                            Error = FieldMappingPushSourceGate.MessageFor(refusal),
                            ReasonCode = refusal,
                        });
                        continue;
                    }

                    changes.AddRange(childChanges);
                }
            }
        }

        return new PushFieldMappingsResponse
        {
            Success = true,
            TargetEntity = input.TargetEntity,
            TotalRecords = input.Children.Count,
            UpdatedCount = 0,
            FailedCount = 0,
            SkippedCount = 0,
            Preview = true,
            Changes = changes.ToArray(),
            Errors = notWritten.ToArray(),
            SkippedNoWriteCount = skippedNoWrite,
            Truncated = input.Truncated,
            SkippedRuleCount = input.SkippedRuleCount,
            RefusedChildIds = [.. input.RefusedChildIds ?? []],
        };
    }

    private static readonly RootRecordFlags Unreadable = RootRecordFlags.Unreadable;

    /// <summary>The Assigned-To policies a change meets, by name. An unreadable record is treated as meeting all three.</summary>
    internal static string[] FlagNames(RootRecordFlags flags)
    {
        var names = new List<string>();
        if (flags.IsUnreadable || flags.IsSecure)
            names.Add("secure");
        if (flags.IsUnreadable || flags.IsRestricted)
            names.Add("restricted");
        if (flags.IsUnreadable || flags.IsLimited)
            names.Add("limited");
        return names.ToArray();
    }

    /// <summary>The effective flags of a batch of records of one table: <c>ExternalParticipationService.GetEffectiveRootRecordFlagsAsync</c>.</summary>
    internal delegate Task<IReadOnlyDictionary<Guid, RootRecordFlags>> EffectiveFlagsReader(
        string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct);

    private static async Task<IReadOnlyDictionary<Guid, RootRecordFlags>> ReadFlagsAsync(
        EffectiveFlagsReader? reader, string targetEntity, IReadOnlyList<RecordFamilyMember> children, CancellationToken ct)
    {
        // A host with no flag reader cannot say what a change meets: every child reads as unreadable, never as unflagged.
        if (reader is null)
            return children.ToDictionary(c => c.Id, _ => Unreadable);

        return await reader(targetEntity, children.Select(c => c.Id).ToList(), ct);
    }

    /// <summary>The values the rules would write, decided once: the source value is the same for every child.</summary>
    private static List<PlannedChange> Plan(PreviewInput input, Microsoft.Extensions.Logging.ILogger? logger)
    {
        var result = new List<PlannedChange>();
        foreach (var rule in input.Rules.OrderBy(r => r.Priority))
        {
            if (!FieldMappingEndpoints.PushMayWrite(input.TargetEntity, rule.TargetField))
                continue;

            var scratch = new Dictionary<string, object?>();
            if (FieldMappingEndpoints.ApplyMappingRule(rule, input.SourceValues, scratch, input.LookupBinds, logger).Status != FieldMappingStatus.Mapped
                || scratch.Count != 1)
            {
                continue;
            }

            var (key, wire) = scratch.Single();
            var assignedTo = input.IsAssignedToColumn(input.TargetEntity, rule.TargetField);
            if (key.EndsWith("@odata.bind", StringComparison.Ordinal))
            {
                input.SourceValues.TryGetValue(rule.SourceField, out var source);
                var id = LookupIdOf(wire as string);
                result.Add(new PlannedChange(rule, key, wire, (source as SourceLookupValue)?.DisplayName ?? id?.ToString("D") ?? string.Empty, id, assignedTo));
                continue;
            }

            input.SourceValues.TryGetValue(rule.SourceField, out var value);
            result.Add(new PlannedChange(rule, key, wire, value is SourceChoiceValue choice ? choice.Label ?? choice.Value.ToString(CultureInfo.InvariantCulture) : TextOf(wire), null, assignedTo));
        }

        return result;
    }

    /// <summary>The id in a bind value <c>/{entitySet}({id})</c>.</summary>
    private static Guid? LookupIdOf(string? bindValue)
    {
        var open = bindValue?.LastIndexOf('(') ?? -1;
        var close = bindValue?.LastIndexOf(')') ?? -1;
        return open >= 0 && close > open && Guid.TryParse(bindValue![(open + 1)..close], out var id) ? id : null;
    }

    private static string TextOf(object? wire) => wire switch
    {
        null => string.Empty,
        bool flag => flag ? "true" : "false",
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => wire.ToString() ?? string.Empty,
    };

    /// <summary>The children's current values for the planned columns, read AS THE CALLER, by child id.</summary>
    private static async Task<Dictionary<Guid, Dictionary<string, JsonElement>>> ReadCurrentValuesAsync(
        IImpersonatedCommunicationQuery impersonatedQuery,
        string childSet,
        string targetEntity,
        IReadOnlyList<RecordFamilyMember> children,
        IReadOnlyList<PlannedChange> planned,
        IReadOnlyDictionary<string, string> attributes,
        Guid callerSystemUserId,
        CancellationToken ct)
    {
        var idColumn = $"{targetEntity}id";
        var columns = FieldMappingPushLookups.SourceSelectColumns(planned.Select(p => p.Rule.TargetField), attributes)
            .Append(idColumn)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var result = new Dictionary<Guid, Dictionary<string, JsonElement>>();
        foreach (var chunk in children.Select(c => c.Id).Chunk(IdsPerQuery))
        {
            var filter = string.Join(" or ", chunk.Select(id => $"{idColumn} eq {id:D}"));
            var rows = await impersonatedQuery.QueryAsync(
                childSet, $"$select={string.Join(',', columns)}&$filter=({filter})&$top={chunk.Length}", callerSystemUserId, ct);
            foreach (var row in rows)
            {
                if (row.TryGetValue(idColumn, out var id) && id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out var childId))
                    result[childId] = row;
            }
        }

        return result;
    }

    /// <summary>What a lookup column holds now (its display name, else its id), and whether it already names the new record.</summary>
    private static (string? From, bool Same) CurrentLookup(Dictionary<string, JsonElement>? row, string column, Guid? newId)
    {
        var property = FieldMappingPushLookups.LookupValueProperty(column);
        string? raw = null;
        string? name = null;
        if (row is not null)
        {
            raw = row.TryGetValue(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            name = row.TryGetValue(property + FieldMappingPushLookups.FormattedValueAnnotation, out var label)
                   && label.ValueKind == JsonValueKind.String
                ? label.GetString()
                : null;
        }

        return (name ?? raw, newId is { } id && Guid.TryParse(raw, out var current) && current == id);
    }

    /// <summary>What a plain column holds now (its formatted text, else its value), and whether it already equals what a rule would write.</summary>
    private static (string? From, bool Same) Current(Dictionary<string, JsonElement>? row, string column, object? wire)
    {
        JsonElement? value = row is not null && row.TryGetValue(column, out var found) && found.ValueKind != JsonValueKind.Null ? found : null;
        string? label = row is not null
                        && row.TryGetValue(column + FieldMappingPushLookups.FormattedValueAnnotation, out var annotation)
                        && annotation.ValueKind == JsonValueKind.String
            ? annotation.GetString()
            : null;
        var from = label ?? (value is { } v ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText() : null);
        return (from, Equal(value, wire));
    }

    /// <summary>Whether a column's current value is what the rule would write (dates by instant, numbers by value).</summary>
    private static bool Equal(JsonElement? current, object? wire)
    {
        if (wire is null)
            return current is null;
        if (current is not { } value)
            return false;

        switch (wire)
        {
            case bool flag:
                return value.ValueKind == (flag ? JsonValueKind.True : JsonValueKind.False);
            case string text:
                if (value.ValueKind != JsonValueKind.String)
                    return false;
                var held = value.GetString();
                return string.Equals(held, text, StringComparison.Ordinal)
                       || (DateTimeOffset.TryParse(held, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var a)
                           && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var b)
                           && a == b);
            default:
                return value.ValueKind == JsonValueKind.Number
                       && decimal.TryParse(TextOf(wire), NumberStyles.Float, CultureInfo.InvariantCulture, out var wanted)
                       && value.TryGetDecimal(out var have)
                       && have == wanted;
        }
    }
}
