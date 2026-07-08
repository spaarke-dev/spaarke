using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;

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
    internal sealed record MappedItem(string JsonBody, IReadOnlyList<string> Columns);

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
    /// <paramref name="dataverse"/> ONLY when the item contains lookup objects — simple items
    /// map without any extra round-trip.
    /// </summary>
    public static async Task<MapOutcome> MapAsync(
        IDataverseUserClient dataverse,
        string tableLogicalName,
        JsonElement item,
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

        // Resolve lookup navigation properties + related entity sets only when needed.
        var lookupBinds = new List<(string NavigationProperty, string RelatedEntitySet, Guid RecordId)>();
        if (lookups.Count > 0)
        {
            var relationshipsResponse = await dataverse.GetAsync(
                $"EntityDefinitions(LogicalName='{tableLogicalName}')?$select=LogicalName" +
                "&$expand=ManyToOneRelationships($select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity)",
                cancellationToken).ConfigureAwait(false);
            if (!relationshipsResponse.IsSuccess)
            {
                return MapOutcome.Failed(relationshipsResponse);
            }

            var navigationByAttributeAndTarget = BuildNavigationPropertyMap(relationshipsResponse.Body);
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
                writer.WritePropertyName(column);
                value.WriteTo(writer);
            }
            foreach (var (navigationProperty, relatedEntitySet, recordId) in lookupBinds)
            {
                writer.WriteString($"{navigationProperty}@odata.bind", $"/{relatedEntitySet}({recordId:D})");
            }
            writer.WriteEndObject();
        }

        return MapOutcome.Ok(new MappedItem(Encoding.UTF8.GetString(stream.ToArray()), columns));
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
