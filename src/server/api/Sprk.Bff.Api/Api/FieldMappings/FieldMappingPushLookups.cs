using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.FieldMapping;

namespace Sprk.Bff.Api.Api.FieldMappings;

/// <summary>
/// A lookup value read from the push SOURCE, AS THE CALLER: the referenced record's id, the table it lives in when that
/// is unambiguous (<see langword="null"/> for a lookup that can reference several tables), and its display name.
/// Stored in the source-values map under the rule's own source-field key, so the rule engine finds it where it always
/// looked.
/// </summary>
internal sealed record SourceLookupValue(Guid Id, string? ReferencedEntity, string? DisplayName);

/// <summary>
/// One rule's lookup write, decided once per push (the source value is the same for every child): either the
/// <c>{navigationProperty}@odata.bind</c> key and its <c>/{entitySet}({id})</c> value, or why the rule cannot be
/// written. A rule whose source value is null carries neither (the engine skips it before looking).
/// </summary>
internal sealed record LookupBindPlan(string? BindKey, string? BindValue, string? Problem)
{
    public static LookupBindPlan NoValue { get; } = new(null, null, null);

    public static LookupBindPlan Refused(string problem) => new(null, null, problem);
}

/// <summary>
/// The lookup-aware half of <c>POST /api/v1/field-mappings/push</c> (unified-access-control-r2, class (a) defect,
/// 2026-10-06): how a source lookup is READ and how a target lookup is WRITTEN, from Dataverse metadata.
/// </summary>
/// <remarks>
/// <para><b>What was wrong.</b> Every rule's source field went into <c>$select</c> by its logical name. A lookup is not
/// a property under that name — its OData property is <c>_{name}_value</c> — so one lookup rule made Dataverse reject
/// the whole source read (400 0x80060888 "Could not find a property named …") and the push 500ed for every active
/// "Attorney Matrix" profile (FAILURE-MODES G-13: one bad element poisons the batch). The write was equally wrong:
/// a lookup cannot be PATCHed by logical name with a raw value; Dataverse needs
/// <c>{ReferencingEntityNavigationPropertyName}@odata.bind = "/{referencedEntitySet}({id})"</c>.</para>
/// <para><b>Metadata decides, not the rule's configured type.</b> Live rules carry field-type values the rule DTO maps
/// inconsistently (e.g. a lookup target stored as <c>1</c> or <c>100000001</c>), so whether a column is a lookup, and
/// whether it can be selected at all, is read from the SOURCE's attribute metadata and the TARGET's many-to-one
/// relationships. A rule field that is not a readable Web API attribute of the source never enters <c>$select</c> —
/// its rule is skipped, and the other rules still run.</para>
/// <para><b>Mirrors the client engine</b> (<c>Spaarke.UI.Components/src/services/FieldMappingService.ts</c>,
/// <c>applyCopyLookup</c>): select <c>_x_value</c>, bind through the target's navigation property, skip a null lookup
/// silently, and skip (never throw) a lookup whose referent cannot be resolved. The client reads the referent from the
/// <c>lookuplogicalname</c> annotation; the impersonated seam here does not request that annotation, so the referent
/// comes from the source lookup's relationship metadata instead — exactly one relationship names it. A lookup that can
/// reference several tables (Customer, Owner, multi-table) is therefore skipped with a stated reason.</para>
/// <para>All metadata is read through the same impersonated seam the source and child reads use (as the caller).
/// No new service, DI registration or Dataverse client: this is a static helper beside the endpoint it serves.</para>
/// </remarks>
internal static class FieldMappingPushLookups
{
    /// <summary>The source's attribute catalog: its logical name, type and whether it is a Web API property.</summary>
    internal static string AttributeMetadataPath(string entity) => $"EntityDefinitions(LogicalName='{entity}')/Attributes";

    /// <summary>Filtered in memory, so no metadata <c>$filter</c> support is assumed.</summary>
    internal const string AttributeMetadataQuery = "$select=LogicalName,AttributeType,IsValidODataAttribute";

    /// <summary>
    /// The many-to-one relationship columns read for the parent lookup AND for lookup writes — one read of the target's
    /// relationships serves both. <c>ReferencingEntityNavigationPropertyName</c> is the <c>@odata.bind</c> key.
    /// </summary>
    internal const string RelationshipMetadataQuery =
        "$select=ReferencingAttribute,ReferencedEntity,ReferencingEntityNavigationPropertyName";

    /// <summary>The annotation the impersonated seam already requests; a lookup's display name rides on it.</summary>
    internal const string FormattedValueAnnotation = "@OData.Community.Display.V1.FormattedValue";

    private static readonly HashSet<string> LookupAttributeTypes =
        new(StringComparer.OrdinalIgnoreCase) { "Lookup", "Customer", "Owner" };

    /// <summary>A lookup-family attribute (its Web API property is <c>_{name}_value</c>).</summary>
    internal static bool IsLookupType(string? attributeType)
        => attributeType is not null && LookupAttributeTypes.Contains(attributeType);

    /// <summary>
    /// The source attributes a rule may select: logical name → <c>AttributeType</c>, excluding any attribute Dataverse
    /// says is not a Web API property (e.g. a lookup's <c>…name</c> shadow column, which also 400s a <c>$select</c>).
    /// </summary>
    internal static Dictionary<string, string> ReadableAttributes(IEnumerable<Dictionary<string, JsonElement>> rows)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (!TryGetString(row, "LogicalName", out var name) || !FieldMappingEndpoints.IsLogicalName(name))
                continue;
            if (row.TryGetValue("IsValidODataAttribute", out var valid) && valid.ValueKind == JsonValueKind.False)
                continue;
            result[name] = TryGetString(row, "AttributeType", out var type) ? type : string.Empty;
        }

        return result;
    }

    /// <summary>
    /// The <c>$select</c> columns for the rule fields: a lookup as <c>_{name}_value</c>, anything else by name, and a
    /// field the source does not have as a readable attribute not at all (so it cannot poison the read).
    /// </summary>
    internal static string[] SourceSelectColumns(IEnumerable<string> fields, IReadOnlyDictionary<string, string> attributes)
        => fields
            .Where(f => FieldMappingEndpoints.IsLogicalName(f) && attributes.ContainsKey(f))
            .Select(f => IsLookupType(attributes[f]) ? LookupValueProperty(f) : f)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>The Web API property a lookup is read through.</summary>
    internal static string LookupValueProperty(string field) => $"_{field}_value";

    /// <summary>
    /// The table each SINGLE-table source lookup references, from the source's many-to-one relationships. A Customer,
    /// Owner or multi-table lookup — or a lookup with no or several relationships — has no entry, because which table a
    /// given value lives in cannot be known without the record's own annotation.
    /// </summary>
    internal static Dictionary<string, string> SingleTableReferents(
        IEnumerable<Dictionary<string, JsonElement>> relationshipRows, IReadOnlyDictionary<string, string> attributes)
    {
        var referents = relationshipRows
            .Select(r => (Attribute: TryGetString(r, "ReferencingAttribute", out var a) ? a : null,
                          Referenced: TryGetString(r, "ReferencedEntity", out var e) ? e : null))
            .Where(r => r.Attribute is not null && r.Referenced is not null)
            .GroupBy(r => r.Attribute!, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Attribute: g.Key,
                          Referenced: g.Select(r => r.Referenced!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()))
            .Where(g => g.Referenced.Length == 1
                        && attributes.TryGetValue(g.Attribute, out var type)
                        && string.Equals(type, "Lookup", StringComparison.OrdinalIgnoreCase));

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (attribute, referenced) in referents)
            result[attribute] = referenced[0];
        return result;
    }

    /// <summary>The target's lookups: referencing attribute → each (referenced table, navigation property).</summary>
    internal static ILookup<string, (string ReferencedEntity, string NavigationProperty)> TargetLookups(
        IEnumerable<Dictionary<string, JsonElement>> relationshipRows)
        => relationshipRows
            .Where(r => TryGetString(r, "ReferencingAttribute", out _)
                        && TryGetString(r, "ReferencedEntity", out _)
                        && TryGetString(r, "ReferencingEntityNavigationPropertyName", out _))
            .Select(r => (Attribute: r["ReferencingAttribute"].GetString()!,
                          Referenced: r["ReferencedEntity"].GetString()!,
                          Navigation: r["ReferencingEntityNavigationPropertyName"].GetString()!))
            .ToLookup(
                r => r.Attribute,
                r => (ReferencedEntity: r.Referenced, NavigationProperty: r.Navigation),
                StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The source row as the rule engine reads it: a lookup as a <see cref="SourceLookupValue"/> (or null when empty),
    /// anything else converted by <paramref name="toClr"/>. A field the source does not have as a readable attribute
    /// is ABSENT, so its rule is skipped as "not found" — never selected, never a 400.
    /// </summary>
    internal static Dictionary<string, object?> MapSourceRow(
        Dictionary<string, JsonElement> row,
        IEnumerable<string> fields,
        IReadOnlyDictionary<string, string> attributes,
        IReadOnlyDictionary<string, string> referents,
        Func<JsonElement, object?> toClr)
    {
        var result = new Dictionary<string, object?>();
        foreach (var field in fields.Distinct())
        {
            if (!FieldMappingEndpoints.IsLogicalName(field) || !attributes.TryGetValue(field, out var type))
                continue;

            if (!IsLookupType(type))
            {
                result[field] = row.TryGetValue(field, out var value) ? toClr(value) : null;
                continue;
            }

            var property = LookupValueProperty(field);
            result[field] = TryGetString(row, property, out var text) && Guid.TryParse(text, out var id)
                ? new SourceLookupValue(
                    id,
                    referents.TryGetValue(field, out var referent) ? referent : null,
                    TryGetString(row, property + FormattedValueAnnotation, out var name) ? name : null)
                : null;
        }

        return result;
    }

    /// <summary>
    /// Plans the write of every rule whose TARGET field is a lookup on the target (from its relationship metadata): the
    /// bind key and value, or why it cannot be written. Never throws for one rule — an unresolvable referent, a
    /// non-lookup source or a failed entity-set lookup refuses THAT rule; the push and the other rules go on.
    /// </summary>
    internal static async Task<Dictionary<FieldMappingRuleDto, LookupBindPlan>> PlanLookupWritesAsync(
        IEnumerable<FieldMappingRuleDto> rules,
        IReadOnlyDictionary<string, object?> sourceValues,
        ILookup<string, (string ReferencedEntity, string NavigationProperty)> targetLookups,
        string targetEntity,
        IGenericEntityService entityService,
        CancellationToken ct)
    {
        var plans = new Dictionary<FieldMappingRuleDto, LookupBindPlan>(ReferenceEqualityComparer.Instance);
        foreach (var rule in rules)
        {
            if (!targetLookups.Contains(rule.TargetField))
                continue;

            plans[rule] = await PlanAsync(rule, sourceValues, targetLookups[rule.TargetField], targetEntity, entityService, ct);
        }

        return plans;
    }

    private static async Task<LookupBindPlan> PlanAsync(
        FieldMappingRuleDto rule,
        IReadOnlyDictionary<string, object?> sourceValues,
        IEnumerable<(string ReferencedEntity, string NavigationProperty)> targetRelationships,
        string targetEntity,
        IGenericEntityService entityService,
        CancellationToken ct)
    {
        if (!sourceValues.TryGetValue(rule.SourceField, out var value) || value is null)
            return LookupBindPlan.NoValue;

        if (value is not SourceLookupValue lookup)
            return LookupBindPlan.Refused(
                $"'{rule.TargetField}' is a lookup on '{targetEntity}'; it can only be copied from a lookup, and "
                + $"'{rule.SourceField}' is not one.");

        if (lookup.ReferencedEntity is null)
            return LookupBindPlan.Refused(
                $"'{rule.SourceField}' can reference more than one table, so which table its value is in cannot be "
                + "determined here; the lookup was not copied.");

        var navigation = targetRelationships
            .Where(r => string.Equals(r.ReferencedEntity, lookup.ReferencedEntity, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.NavigationProperty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (navigation.Length != 1 || !FieldMappingEndpoints.IsLogicalName(navigation[0]))
            return LookupBindPlan.Refused(navigation.Length == 0
                ? $"'{rule.TargetField}' on '{targetEntity}' does not reference '{lookup.ReferencedEntity}', so "
                  + $"'{rule.SourceField}' cannot be copied into it."
                : $"'{rule.TargetField}' on '{targetEntity}' has no single navigation property to "
                  + $"'{lookup.ReferencedEntity}'; the lookup was not copied.");

        string entitySet;
        try
        {
            entitySet = await entityService.GetEntitySetNameAsync(lookup.ReferencedEntity, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return LookupBindPlan.Refused(
                $"The table '{lookup.ReferencedEntity}' that '{rule.SourceField}' references could not be resolved; "
                + "the lookup was not copied.");
        }

        if (!FieldMappingEndpoints.IsLogicalName(entitySet))
            return LookupBindPlan.Refused(
                $"The table '{lookup.ReferencedEntity}' has no usable entity set; the lookup was not copied.");

        return new LookupBindPlan($"{navigation[0]}@odata.bind", $"/{entitySet}({lookup.Id:D})", null);
    }

    private static bool TryGetString(Dictionary<string, JsonElement> row, string key, out string value)
    {
        if (row.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String
            && element.GetString() is { Length: > 0 } text)
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
