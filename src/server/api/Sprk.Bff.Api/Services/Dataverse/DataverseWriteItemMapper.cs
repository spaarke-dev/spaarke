using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// Maps the GA-Dataverse-MCP <c>item</c> argument (key/value pairs keyed by column logical
/// name) to a Dataverse Web API OData payload. Shared by the task-009 write handlers
/// (<c>dataverse.create_record</c> + <c>dataverse.update_record</c>) so the lookup-binding and
/// key-validation rules cannot drift between the two mutations.
/// </summary>
/// <remarks>
/// <para>
/// <b>GA MCP <c>item</c> contract (frozen per ADR-039)</b>: values are strings, numbers, or
/// booleans for simple fields; choice columns take the numeric option value; multi-select
/// choice takes a comma-separated value string (e.g. <c>"100000002,100000004"</c> — passed
/// through verbatim; the Web API accepts it); lookup/customer columns take an object
/// <c>{"relatedTable": "account", "name": "…", "recordId": "guid"}</c>.
/// </para>
/// <para>
/// <b>Documented native-transport deviation</b>: lookups REQUIRE <c>recordId</c> here. The GA
/// MCP server may resolve a lookup by <c>name</c>; the native transport does not guess — a
/// lookup without <c>recordId</c> returns a validation error directing the model to find the
/// id via <c>dataverse.search_data</c> / <c>dataverse.read_query</c> first. <c>name</c> is
/// accepted and ignored (contract-shape compatibility).
/// </para>
/// <para>
/// Lookup binds resolve the referencing navigation property from table metadata
/// (<c>ManyToOneRelationships</c>) under the CALLING USER's token — custom lookups' navigation
/// property casing differs from the column logical name, so <c>{logicalName}@odata.bind</c>
/// alone is not reliable. Polymorphic lookups (e.g. customer columns) are disambiguated by
/// matching BOTH <c>ReferencingAttribute</c> and <c>ReferencedEntity</c>.
/// </para>
/// <para>
/// Keys are validated against the logical-name grammar and rejected when they carry <c>@</c> /
/// <c>.</c> — the LLM cannot smuggle raw OData annotations (e.g. a hand-built
/// <c>@odata.bind</c> to an arbitrary path) through <c>item</c>.
/// </para>
/// </remarks>
internal static partial class DataverseWriteItemMapper
{
    [GeneratedRegex(@"^[a-z][a-z0-9_]*$")]
    private static partial Regex LogicalNameRegex();

    /// <summary>Successful mapping: the OData JSON body + the column logical names it sets (identifiers only — safe to log counts/names per NFR-07).</summary>
    internal sealed record MappedItem(string JsonBody, IReadOnlyList<string> Columns)
    {
        /// <summary>
        /// Every lookup the body binds — column, target table, target entity set, target id (unified-access-control-r2
        /// task 146 r2: the records a row is FILED under decide its owner, and each costs the caller AppendTo).
        /// </summary>
        public IReadOnlyList<MappedLookup> Lookups { get; init; } = Array.Empty<MappedLookup>();

        /// <summary>The columns the body sets to <c>null</c> — a clear, which for a lookup column moves the row OUT of
        /// that record (task 146 r2).</summary>
        public IReadOnlyList<string> ClearedColumns { get; init; } = Array.Empty<string>();
    }

    /// <summary>One lookup a mapped body binds.</summary>
    internal sealed record MappedLookup(string Column, string RelatedTable, string RelatedEntitySet, Guid RecordId);

    /// <summary>
    /// Tri-state outcome: exactly one of <see cref="Item"/> (success),
    /// <see cref="ValidationError"/> (bad LLM arguments), or <see cref="ClientFailure"/>
    /// (metadata lookup failed under the user's token — flows out with the user's own error).
    /// </summary>
    internal sealed record MapOutcome(MappedItem? Item, string? ValidationError, DataverseUserResponse? ClientFailure)
    {
        public static MapOutcome Ok(MappedItem item) => new(item, null, null);
        public static MapOutcome Invalid(string error) => new(null, error, null);
        public static MapOutcome Failed(DataverseUserResponse response) => new(null, null, response);
    }

    /// <summary>
    /// Maps <paramref name="item"/> to an OData payload for <paramref name="tableLogicalName"/>.
    /// Performs metadata reads (navigation properties + related entity sets) through
    /// <paramref name="dataverse"/> ONLY when the item contains lookup objects or a <c>null</c> — items of non-null simple
    /// values map without any extra round-trip.
    /// </summary>
    /// <remarks>
    /// <para><b>A <c>null</c> on a lookup column is a clear, written as its navigation property's null bind</b>
    /// (unified-access-control-r2 task 147 r1c-v1, verifier item 1). The Web API declares a lookup only as its
    /// single-valued navigation property (<c>sprk_RegardingMatter</c>) and the read-only <c>_sprk_regardingmatter_value</c>;
    /// the logical name <c>sprk_regardingmatter</c> is NOT a property of the entity type, and a body that names it is
    /// refused ("Could not find a property named 'sprk_regardingmatter' on type 'Microsoft.Dynamics.CRM.sprk_todo'", live
    /// metadata, spaarkedev1). So the column is cleared through <c>{NavigationProperty}@odata.bind: null</c> — the
    /// metadata's own navigation property, the form the browser writers sent through <c>Xrm.WebApi</c> before task 147. A
    /// polymorphic lookup (several navigation properties, one per target table) is cleared through ONE of them — the first
    /// by ordinal name, so the body is deterministic: every one of them binds the same column. (On the browser routes'
    /// tables the only polymorphic lookup is <c>ownerid</c>, which those routes refuse — live metadata, 2026-10-05.)
    /// <see cref="MappedItem.ClearedColumns"/> stays keyed by the column's logical name; a <c>null</c> on any other column is
    /// written as that column's null.</para>
    /// </remarks>
    public static Task<MapOutcome> MapAsync(
        IDataverseUserClient dataverse,
        string tableLogicalName,
        JsonElement item,
        CancellationToken cancellationToken) =>
        MapCoreAsync(dataverse, tableLogicalName, item, prefetchedRelationships: null, cancellationToken);

    /// <param name="prefetchedRelationships">The table's <c>ManyToOneRelationships</c> body when the caller already read it
    /// (as the caller), so it is not read twice.</param>
    private static async Task<MapOutcome> MapCoreAsync(
        IDataverseUserClient dataverse,
        string tableLogicalName,
        JsonElement item,
        JsonElement? prefetchedRelationships,
        CancellationToken cancellationToken)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return MapOutcome.Invalid("'item' must be a JSON object of column logical names to values.");
        }

        var columns = new List<string>();
        var simpleValues = new List<(string Column, JsonElement Value)>();
        var lookups = new List<(string Column, string RelatedTable, Guid RecordId)>();

        foreach (var property in item.EnumerateObject())
        {
            var key = property.Name.Trim().ToLowerInvariant();
            if (!LogicalNameRegex().IsMatch(key))
            {
                return MapOutcome.Invalid(
                    $"'{property.Name}' is not a valid column logical name. Use column logical names from " +
                    "dataverse.describe; for lookup columns use the object form " +
                    "{\"relatedTable\": \"…\", \"recordId\": \"…\"} instead of OData annotations.");
            }

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    if (!TryParseLookup(property.Value, key, out var relatedTable, out var recordId, out var lookupError))
                    {
                        return MapOutcome.Invalid(lookupError!);
                    }
                    lookups.Add((key, relatedTable!, recordId));
                    break;

                case JsonValueKind.Array:
                    return MapOutcome.Invalid(
                        $"Column '{key}': arrays are not supported. For multi-select choice columns pass a " +
                        "comma-separated string of numeric option values (e.g. \"100000002,100000004\").");

                default:
                    simpleValues.Add((key, property.Value));
                    break;
            }

            columns.Add(key);
        }

        if (columns.Count == 0)
        {
            return MapOutcome.Invalid("'item' must contain at least one column.");
        }

        // One value per column: a column named twice (e.g. a browser payload's plain key AND a bind of the same lookup)
        // would leave Dataverse to pick one.
        if (columns.GroupBy(c => c, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } twice)
        {
            return MapOutcome.Invalid($"Column '{twice.Key}' is set more than once.");
        }

        // A null is a clear. On a lookup column it is written as a navigation property's null bind (see the remarks), so
        // which nulls are lookups is read from the same metadata the binds use.
        var clearedColumns = simpleValues.Where(v => v.Value.ValueKind == JsonValueKind.Null).Select(v => v.Column).ToArray();
        var clearBinds = new Dictionary<string, string>(StringComparer.Ordinal);

        // Resolve lookup navigation properties + related entity sets only when needed.
        var lookupBinds = new List<(string NavigationProperty, string RelatedEntitySet, Guid RecordId)>();
        var mappedLookups = new List<MappedLookup>();
        if (lookups.Count > 0 || clearedColumns.Length > 0)
        {
            JsonElement? relationshipsBody = prefetchedRelationships;
            if (relationshipsBody is null)
            {
                var relationshipsResponse = await dataverse.GetAsync(
                    $"EntityDefinitions(LogicalName='{tableLogicalName}')?$select=LogicalName" +
                    "&$expand=ManyToOneRelationships($select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity)",
                    cancellationToken).ConfigureAwait(false);
                if (!relationshipsResponse.IsSuccess)
                {
                    return MapOutcome.Failed(relationshipsResponse);
                }

                relationshipsBody = relationshipsResponse.Body;
            }

            var navigationByAttributeAndTarget = BuildNavigationPropertyMap(relationshipsBody);
            foreach (var column in clearedColumns)
            {
                // Every navigation property of a polymorphic lookup binds the same column; the first by ordinal name keeps
                // the body deterministic.
                var navigation = navigationByAttributeAndTarget
                    .Where(kv => string.Equals(kv.Key.Attribute, column, StringComparison.Ordinal))
                    .Select(kv => kv.Value)
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (navigation is not null)
                    clearBinds[column] = navigation;
            }

            var entitySetByTable = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var (column, relatedTable, recordId) in lookups)
            {
                if (!navigationByAttributeAndTarget.TryGetValue((column, relatedTable), out var navigationProperty))
                {
                    return MapOutcome.Invalid(
                        $"Column '{column}' on table '{tableLogicalName}' is not a lookup that references " +
                        $"table '{relatedTable}'. Use dataverse.describe to check the column's type and target table.");
                }

                if (!entitySetByTable.TryGetValue(relatedTable, out var relatedEntitySet))
                {
                    var relatedMetaResponse = await dataverse.GetAsync(
                        $"EntityDefinitions(LogicalName='{relatedTable}')?$select=EntitySetName",
                        cancellationToken).ConfigureAwait(false);
                    if (!relatedMetaResponse.IsSuccess)
                    {
                        return MapOutcome.Failed(relatedMetaResponse);
                    }
                    relatedEntitySet = GetString(relatedMetaResponse.Body, "EntitySetName");
                    if (relatedEntitySet is null)
                    {
                        return MapOutcome.Invalid($"Related table '{relatedTable}' has no entity-set name.");
                    }
                    entitySetByTable[relatedTable] = relatedEntitySet;
                }

                lookupBinds.Add((navigationProperty, relatedEntitySet, recordId));
                mappedLookups.Add(new MappedLookup(column, relatedTable, relatedEntitySet, recordId));
            }
        }

        // Serialize with Utf8JsonWriter so simple values pass through with their original
        // JSON types (string/number/boolean/null) untouched.
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (column, value) in simpleValues)
            {
                if (value.ValueKind == JsonValueKind.Null && clearBinds.TryGetValue(column, out var clearNavigation))
                {
                    // A lookup clear: the navigation property's null bind — never the logical name, which the Web API
                    // does not declare.
                    writer.WriteNull($"{clearNavigation}{BindSuffix}");
                    continue;
                }

                writer.WritePropertyName(column);
                value.WriteTo(writer);
            }
            foreach (var (navigationProperty, relatedEntitySet, recordId) in lookupBinds)
            {
                writer.WriteString($"{navigationProperty}{BindSuffix}", $"/{relatedEntitySet}({recordId:D})");
            }
            writer.WriteEndObject();
        }

        return MapOutcome.Ok(new MappedItem(Encoding.UTF8.GetString(stream.ToArray()), columns)
        {
            Lookups = mappedLookups,
            // Keyed by the column's logical name (a lookup's too): the re-file core asks which ownership lookups were cleared.
            ClearedColumns = clearedColumns,
        });
    }

    [GeneratedRegex(@"^/?(?<set>[A-Za-z_][A-Za-z0-9_]*)\(\{?(?<id>[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})\}?\)$")]
    private static partial Regex BindValueRegex();

    private const string BindSuffix = "@odata.bind";

    /// <summary>
    /// unified-access-control-r2 task 147 r1 (owner round 28 item 1): maps a BROWSER writer's Dataverse Web API payload —
    /// the exact object a client passes to <c>Xrm.WebApi.createRecord</c> / <c>updateRecord</c> — onto the same
    /// <see cref="MappedItem"/> the chat tools produce, so the browser's writes go through the ONE G5 core
    /// (<see cref="OwnedChildWrite"/>) unchanged.
    /// </summary>
    /// <remarks>
    /// <para>Plain keys (column logical names) are kept. Each <c>{NavigationProperty}@odata.bind</c> is translated through
    /// the table's metadata — read AS THE CALLER — into the lookup column it binds and the table it targets, and its value
    /// <c>/{entitySet}({id})</c> must name exactly that table's entity set (a bind can never be re-pointed at another
    /// table). A bind to <c>null</c> is a clear of that column: the column is listed in
    /// <see cref="MappedItem.ClearedColumns"/> by its logical name, and the body sent to Dataverse keeps the clear as
    /// <c>{NavigationProperty}@odata.bind: null</c> on the metadata's own navigation property (task 147 r1c-v1, verifier
    /// item 1: the logical name is not a Web API property, so a body naming it is refused). A column bound or cleared more
    /// than once is refused. Any other OData annotation is refused, as the chat mapper refuses it: the server builds every
    /// bind itself.</para>
    /// </remarks>
    public static async Task<MapOutcome> MapWebApiPayloadAsync(
        IDataverseUserClient dataverse,
        string tableLogicalName,
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return MapOutcome.Invalid("The request body must be a JSON object of column names to values.");

        var binds = payload.EnumerateObject()
            .Where(p => p.Name.EndsWith(BindSuffix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Dictionary<string, (string Attribute, string Target)>? byNavigationProperty = null;
        JsonElement? relationshipsBody = null;
        if (binds.Count > 0)
        {
            var relationships = await dataverse.GetAsync(
                $"EntityDefinitions(LogicalName='{tableLogicalName}')?$select=LogicalName" +
                "&$expand=ManyToOneRelationships($select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity)",
                cancellationToken).ConfigureAwait(false);
            if (!relationships.IsSuccess)
                return MapOutcome.Failed(relationships);

            // Read once: the item mapping below resolves every bind and clear from the same body.
            relationshipsBody = relationships.Body;
            byNavigationProperty = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            foreach (var ((attribute, target), navigationProperty) in BuildNavigationPropertyMap(relationships.Body))
                byNavigationProperty.TryAdd(navigationProperty, (attribute, target));
        }

        var expectedSets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in payload.EnumerateObject())
            {
                if (property.Name.EndsWith(BindSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    var navigationProperty = property.Name[..^BindSuffix.Length];
                    if (!byNavigationProperty!.TryGetValue(navigationProperty, out var lookup))
                    {
                        return MapOutcome.Invalid(
                            $"'{property.Name}' does not bind a lookup of table '{tableLogicalName}'.");
                    }

                    // A column bound or cleared twice (navigation properties differing only in case, or a plain key beside
                    // its bind) reaches the item twice and is refused there — MapCoreAsync's one-value-per-column rule.
                    if (property.Value.ValueKind == JsonValueKind.Null)
                    {
                        // A clear. The item names the column (so the re-file core sees which lookup was cleared); the body
                        // clears it through the metadata's own navigation property (MapCoreAsync) — never this logical name.
                        writer.WriteNull(lookup.Attribute);
                        continue;
                    }

                    var match = property.Value.ValueKind == JsonValueKind.String
                        ? BindValueRegex().Match(property.Value.GetString()!.Trim())
                        : Match.Empty;
                    if (!match.Success || !Guid.TryParse(match.Groups["id"].Value, out var recordId) || recordId == Guid.Empty)
                    {
                        return MapOutcome.Invalid(
                            $"'{property.Name}' must be '/<entity set>(<id>)' or null.");
                    }

                    expectedSets[lookup.Attribute] = match.Groups["set"].Value;

                    writer.WritePropertyName(lookup.Attribute);
                    writer.WriteStartObject();
                    writer.WriteString("relatedTable", lookup.Target);
                    writer.WriteString("recordId", recordId.ToString("D"));
                    writer.WriteEndObject();
                    continue;
                }

                if (property.Name.Contains('@', StringComparison.Ordinal) || property.Name.Contains('.', StringComparison.Ordinal))
                {
                    return MapOutcome.Invalid(
                        $"'{property.Name}' is an OData annotation; only column values and '@odata.bind' lookups are accepted.");
                }

                property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        using var item = JsonDocument.Parse(stream.ToArray());
        var mapped = await MapCoreAsync(
                dataverse, tableLogicalName, item.RootElement, relationshipsBody, cancellationToken)
            .ConfigureAwait(false);
        if (mapped.Item is null)
            return mapped;

        // The bind named an entity set; the server bound the lookup's own target. They must be the same table.
        foreach (var lookup in mapped.Item.Lookups)
        {
            if (expectedSets.TryGetValue(lookup.Column, out var set)
                && !string.Equals(set, lookup.RelatedEntitySet, StringComparison.OrdinalIgnoreCase))
            {
                return MapOutcome.Invalid(
                    $"Column '{lookup.Column}' binds '/{set}(…)', but its lookup targets '{lookup.RelatedEntitySet}'.");
            }
        }

        return mapped;
    }

    private static bool TryParseLookup(
        JsonElement value, string column, out string? relatedTable, out Guid recordId, out string? error)
    {
        relatedTable = null;
        recordId = Guid.Empty;
        error = null;

        if (!value.TryGetProperty("relatedTable", out var relatedTableProp) ||
            relatedTableProp.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(relatedTableProp.GetString()))
        {
            error = $"Column '{column}': lookup objects must include 'relatedTable' (the target table's logical name).";
            return false;
        }

        relatedTable = relatedTableProp.GetString()!.Trim().ToLowerInvariant();
        if (!LogicalNameRegex().IsMatch(relatedTable))
        {
            error = $"Column '{column}': '{relatedTable}' is not a valid table logical name.";
            return false;
        }

        if (!value.TryGetProperty("recordId", out var recordIdProp) ||
            recordIdProp.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(recordIdProp.GetString(), out recordId))
        {
            // Documented deviation: GA MCP may resolve lookups by 'name'; the native transport
            // never guesses record identity.
            error = $"Column '{column}': lookup objects require a 'recordId' GUID on the native transport. " +
                    "Find the record id first via dataverse.search_data or dataverse.read_query.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Builds (referencingAttribute, referencedEntity) → navigation-property map from an
    /// <c>EntityDefinitions…$expand=ManyToOneRelationships</c> response. Matching on BOTH keys
    /// disambiguates polymorphic lookups (customer columns reference account AND contact).
    /// </summary>
    private static Dictionary<(string Attribute, string Target), string> BuildNavigationPropertyMap(JsonElement? body)
    {
        var map = new Dictionary<(string, string), string>();
        if (body is not { } root ||
            !root.TryGetProperty("ManyToOneRelationships", out var relationships) ||
            relationships.ValueKind != JsonValueKind.Array)
        {
            return map;
        }

        foreach (var relationship in relationships.EnumerateArray())
        {
            var attribute = GetString(relationship, "ReferencingAttribute");
            var navigationProperty = GetString(relationship, "ReferencingEntityNavigationPropertyName");
            var target = GetString(relationship, "ReferencedEntity");
            if (attribute is null || navigationProperty is null || target is null)
            {
                continue;
            }
            map[(attribute, target)] = navigationProperty;
        }
        return map;
    }

    private static string? GetString(JsonElement? element, string property) =>
        element is { } e && e.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
}
