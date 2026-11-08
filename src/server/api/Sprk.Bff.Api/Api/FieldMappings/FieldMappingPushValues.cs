using System.Globalization;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.FieldMapping;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;

namespace Sprk.Bff.Api.Api.FieldMappings;

/// <summary>
/// A choice (option set) value read from the push SOURCE, AS THE CALLER: its number and, when Dataverse returned one, its
/// label. The label rides on the same formatted-value annotation a source lookup's display name does, and is what a Copy into
/// a Text target writes (the create-time convention).
/// </summary>
internal sealed record SourceChoiceValue(int Value, string? Label);

/// <summary>
/// field-mapping-server-write-path-r1 task 040 (FR-23): the two edges of the push's Copy — the source value in, the mapped
/// value out — around <see cref="CreateTimeFieldMapping.ApplyCopyOverwrite"/>, which is the ONE place a Copy value is computed.
/// </summary>
/// <remarks>
/// <para>The create path reads its source as a typed SDK <see cref="Entity"/> and writes a typed one; the push reads OData JSON
/// (numbers, strings, <see cref="SourceLookupValue"/>) and writes an OData body. These helpers translate at the edge only:
/// <see cref="ToSourceValue"/> turns the push's value into the SDK type the engine sees (an option set as an
/// <see cref="OptionSetValue"/> with its label, a date as a UTC <see cref="DateTime"/>, a lookup as an
/// <see cref="EntityReference"/>); <see cref="TryToWire"/> turns what the engine wrote back into the OData value. Neither decides
/// what a Copy means. A lookup TARGET is written through its planned <c>@odata.bind</c> (<see cref="FieldMappingPushLookups"/>),
/// which needs the target's navigation property; this class does not plan it.</para>
/// <para>Static helpers beside the endpoint they serve, like <see cref="FieldMappingPushLookups"/>: no DI registration, no new
/// service (ADR-010).</para>
/// </remarks>
internal static class FieldMappingPushValues
{
    /// <summary>The engine's field-type integer for a rule DTO's type name (an unknown name reads as Text, as the DTO maps it).</summary>
    internal static int FieldTypeOf(string? type) => type?.Trim().ToLowerInvariant() switch
    {
        "lookup" => CreateTimeFieldMapping.FieldTypeLookup,
        "optionset" => CreateTimeFieldMapping.FieldTypeOptionSet,
        "number" => CreateTimeFieldMapping.FieldTypeNumber,
        "datetime" => CreateTimeFieldMapping.FieldTypeDateTime,
        "boolean" => CreateTimeFieldMapping.FieldTypeBoolean,
        "memo" => CreateTimeFieldMapping.FieldTypeMemo,
        _ => CreateTimeFieldMapping.FieldTypeText,
    };

    /// <summary>The engine's mapping-type integer for a rule DTO's type name; -1 for a name the engine does not know.</summary>
    internal static int MappingTypeOf(string? type) => type?.Trim().ToLowerInvariant() switch
    {
        null or "" or "copy" => CreateTimeFieldMapping.MappingTypeCopy,
        "default" => CreateTimeFieldMapping.MappingTypeDefault,
        "concat" => CreateTimeFieldMapping.MappingTypeConcat,
        "template" => CreateTimeFieldMapping.MappingTypeTemplate,
        _ => -1,
    };

    /// <summary>Whether the DTO's rule is a Copy rule (an empty type reads as Copy, the DTO's default).</summary>
    internal static bool IsCopy(FieldMappingRuleDto rule) => MappingTypeOf(rule.MappingType) == CreateTimeFieldMapping.MappingTypeCopy;

    /// <summary>The rule as the engine reads it. Only what a Copy needs is carried.</summary>
    internal static FieldMappingRuleEntity ToEngineRule(FieldMappingRuleDto rule) => new()
    {
        Id = rule.Id,
        Name = rule.SourceField,
        SourceField = rule.SourceField,
        SourceFieldType = FieldTypeOf(rule.SourceFieldType),
        TargetField = rule.TargetField,
        TargetFieldType = FieldTypeOf(rule.TargetFieldType),
        MappingType = MappingTypeOf(rule.MappingType),
        ExecutionOrder = rule.Priority,
        IsActive = true,
    };

    /// <summary>
    /// A push source value as the value the engine reads off a typed source record. <paramref name="originalDateText"/> is the
    /// text a date arrived as, kept so an unchanged date goes back out as it came (a date-only column stays date-only).
    /// </summary>
    internal static object? ToSourceValue(object? value, string? sourceFieldType, out string? label, out string? originalDateText)
    {
        label = null;
        originalDateText = null;
        switch (value)
        {
            case SourceLookupValue lookup:
                return new EntityReference(lookup.ReferencedEntity ?? string.Empty, lookup.Id) { Name = lookup.DisplayName };

            case Guid id when FieldTypeOf(sourceFieldType) == CreateTimeFieldMapping.FieldTypeLookup:
                // A bare id, with no table or name: the reference the engine copies, naming no table.
                return new EntityReference(string.Empty, id);

            case SourceChoiceValue choice:
                label = choice.Label;
                return new OptionSetValue(choice.Value);

            case string text when FieldTypeOf(sourceFieldType) == CreateTimeFieldMapping.FieldTypeDateTime
                                  && DateTime.TryParse(
                                      text, CultureInfo.InvariantCulture,
                                      DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date):
                originalDateText = text;
                return date;

            case long whole when FieldTypeOf(sourceFieldType) == CreateTimeFieldMapping.FieldTypeOptionSet
                                 && whole is >= int.MinValue and <= int.MaxValue:
                return new OptionSetValue((int)whole);

            default:
                return value;
        }
    }

    /// <summary>
    /// What the engine wrote, as the OData value of the column: a choice as its number, money as its amount, a date as the
    /// column stores it (the text it arrived as when it is unchanged, else a UTC timestamp), numbers, booleans and text as
    /// they are. <see langword="false"/> for a value the Web API has no form for (a lookup goes through its bind plan instead).
    /// </summary>
    internal static bool TryToWire(object? written, string? originalDateText, out object? wire)
    {
        switch (written)
        {
            case null:
                wire = null;
                return true;
            case string or bool or int or long or decimal or double:
                wire = written;
                return true;
            case OptionSetValue option:
                wire = option.Value;
                return true;
            case Money money:
                wire = money.Value;
                return true;
            case DateTime date:
                wire = originalDateText is not null
                       && DateTime.TryParse(
                           originalDateText, CultureInfo.InvariantCulture,
                           DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var original)
                       && original == date
                    ? originalDateText
                    : (date.Kind == DateTimeKind.Local ? date.ToUniversalTime() : date)
                        .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                return true;
            default:
                wire = null;
                return false;
        }
    }
}
