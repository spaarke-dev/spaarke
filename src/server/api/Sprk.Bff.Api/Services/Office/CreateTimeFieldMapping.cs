using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// The pure half of create-time Field Mapping Framework application: given a profile's rules and the source record
/// already read, write each rule's value onto the new record's payload. No I/O — <see cref="RecordCreationService"/>
/// owns the one profile read and the one source read.
/// </summary>
/// <remarks>
/// <para>Extracted from <see cref="RecordCreationService"/> (task 030 Step 9.5 review, CLAUDE.md §11.5): the rule
/// engine changes when the Field Mapping Framework changes, the creation service changes when an entity's creation
/// rules change — two reasons to change, so two files. Internal static, not a DI registration (ADR-010), and reusable
/// as-is by task 031 (Project).</para>
///
/// <para><b>Semantics</b> mirror the client engine <c>FieldMappingService.applyFieldMappings</c>: rules in
/// <c>ExecutionOrder</c>; Copy (scalar + lookup), Default (literal), Concat / Template (one <c>{field}</c> resolver);
/// every failure a warning, never a throw; no <c>source == target</c> guard. <b>Server-side differences</b> (notes/030
/// §8), all because the SDK needs typed values where the client sends JSON: a Default literal is converted to the
/// target type's CLR type; a Copy into a Text/Memo target is rendered as text (option sets as their label, booleans
/// Yes/No, dates ISO-8601 — the push path's <c>TransformValue</c> conventions); a null source value is skipped rather
/// than copied as null (on a create that is the same outcome).</para>
/// </remarks>
internal static class CreateTimeFieldMapping
{
    // Field Mapping Framework option-set integers (docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md).
    internal const int MappingTypeCopy = 0;
    internal const int MappingTypeDefault = 1;
    internal const int MappingTypeConcat = 2;
    internal const int MappingTypeTemplate = 3;
    internal const int FieldTypeText = 0;
    internal const int FieldTypeLookup = 1;
    internal const int FieldTypeOptionSet = 2;
    internal const int FieldTypeNumber = 3;
    internal const int FieldTypeDateTime = 4;
    internal const int FieldTypeBoolean = 5;
    internal const int FieldTypeMemo = 6;

    /// <summary>The <c>{field}</c> placeholder token shared by Concat and Template (one resolver serves both).</summary>
    private static readonly Regex PlaceholderPattern =
        new(@"\{([^{}]+)\}", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// The source columns every rule needs — each Copy rule's source field plus every Concat/Template placeholder —
    /// so the caller reads the source record ONCE (never once per rule).
    /// </summary>
    internal static IReadOnlyCollection<string> CollectSourceColumns(IEnumerable<FieldMappingRuleEntity> rules)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            if (rule.MappingType == MappingTypeCopy && !string.IsNullOrWhiteSpace(rule.SourceField))
            {
                columns.Add(rule.SourceField.Trim());
            }
            else if (rule.MappingType is MappingTypeConcat or MappingTypeTemplate)
            {
                foreach (var field in ExtractPlaceholderFields(rule.Expression))
                {
                    columns.Add(field);
                }
            }
        }

        return columns;
    }

    /// <summary>
    /// Applies every rule, in <c>ExecutionOrder</c>, onto <paramref name="target"/>. A rule targeting one of
    /// <paramref name="protectedAttributes"/> is skipped with a warning. Never throws.
    /// </summary>
    /// <param name="source">The source record, or null when it could not be read (Copy/Concat/Template then skip
    /// silently — the caller already recorded the single read-failure warning).</param>
    internal static void Apply(
        IEnumerable<FieldMappingRuleEntity> rules,
        Entity target,
        Entity? source,
        IReadOnlySet<string> protectedAttributes,
        List<string> warnings,
        ILogger logger)
    {
        foreach (var rule in rules.OrderBy(r => r.ExecutionOrder))
        {
            try
            {
                ApplyRule(rule, target, source, protectedAttributes, warnings);
            }
            catch (Exception ex)
            {
                // One bad rule never aborts the rest and never escapes. The user-facing warning deliberately
                // carries no exception text; the detail goes to the log.
                logger.LogWarning(ex,
                    "[RECORD-CREATE] Field-mapping rule {RuleId} ({SourceField} -> {TargetField}) threw; skipped.",
                    rule.Id, rule.SourceField, rule.TargetField);
                warnings.Add($"Field-mapping rule \"{rule.SourceField}\" -> \"{rule.TargetField}\" failed and was skipped.");
            }
        }
    }

    private static void ApplyRule(
        FieldMappingRuleEntity rule,
        Entity target,
        Entity? source,
        IReadOnlySet<string> protectedAttributes,
        List<string> warnings)
    {
        // Rule fields come off the Dataverse wire through a mapper, so `required` is not a runtime guarantee here.
        var targetField = rule.TargetField?.Trim();
        if (string.IsNullOrWhiteSpace(targetField))
        {
            warnings.Add($"Field-mapping rule \"{rule.Name}\" has no target field; skipped.");
            return;
        }

        if (protectedAttributes.Contains(targetField))
        {
            warnings.Add(
                $"Field-mapping rule targeting \"{targetField}\" was skipped: that field is set by the server when a "
                + "record is created and cannot be changed by a field-mapping rule.");
            return;
        }

        switch (rule.MappingType)
        {
            case MappingTypeCopy:
                ApplyCopy(rule, targetField, target, source, warnings);
                return;

            case MappingTypeDefault:
                ApplyDefault(rule, targetField, target, warnings);
                return;

            case MappingTypeConcat:
            case MappingTypeTemplate:
                ApplyExpression(rule, targetField, target, source, warnings);
                return;

            default:
                warnings.Add(
                    $"Field-mapping rule for \"{targetField}\" has an unknown mapping type ({rule.MappingType}); skipped.");
                return;
        }
    }

    private static void ApplyCopy(
        FieldMappingRuleEntity rule, string targetField, Entity target, Entity? source, List<string> warnings)
    {
        var sourceField = rule.SourceField?.Trim();
        if (source is null || string.IsNullOrEmpty(sourceField))
        {
            return; // no source read (its single warning is already recorded) or a rule with no source field
        }

        // The SDK omits null attributes, so "absent" means "the source record has no value" — the normal case (e.g.
        // only one attorney assigned). Nothing to copy; skip silently, as the client does for an empty lookup.
        if (!source.Attributes.TryGetValue(sourceField, out var value) || value is null)
        {
            return;
        }

        if (rule.TargetFieldType == FieldTypeLookup)
        {
            if (value is EntityReference reference && reference.Id != Guid.Empty)
            {
                target[targetField] = new EntityReference(reference.LogicalName, reference.Id);
                return;
            }

            warnings.Add($"Copy rule \"{sourceField}\" -> \"{targetField}\" skipped: the source value is not a lookup.");
            return;
        }

        target[targetField] = rule.TargetFieldType is FieldTypeText or FieldTypeMemo
            ? ToText(value, source, sourceField)
            : value;
    }

    private static void ApplyDefault(FieldMappingRuleEntity rule, string targetField, Entity target, List<string> warnings)
    {
        if (string.IsNullOrEmpty(rule.DefaultValue))
        {
            warnings.Add($"Default rule for \"{targetField}\" skipped: it has no default value.");
            return;
        }

        if (!TryConvertLiteral(rule.DefaultValue, rule.TargetFieldType, out var converted))
        {
            warnings.Add($"Default rule for \"{targetField}\" skipped: its value cannot be written to a field of that type.");
            return;
        }

        target[targetField] = converted;
    }

    private static void ApplyExpression(
        FieldMappingRuleEntity rule, string targetField, Entity target, Entity? source, List<string> warnings)
    {
        var label = rule.MappingType == MappingTypeConcat ? "Concat" : "Template";

        if (rule.TargetFieldType == FieldTypeLookup)
        {
            warnings.Add($"{label} rule for \"{targetField}\" skipped: a format string cannot set a lookup.");
            return;
        }

        if (string.IsNullOrEmpty(rule.Expression))
        {
            warnings.Add($"{label} rule for \"{targetField}\" skipped: it has no expression.");
            return;
        }

        if (ExtractPlaceholderFields(rule.Expression).Count > 0 && source is null)
        {
            return; // the single source-read warning is already recorded
        }

        var unresolved = new List<string>();
        var resolved = PlaceholderPattern.Replace(rule.Expression, match =>
        {
            var field = match.Groups[1].Value.Trim();
            if (source is not null && source.Attributes.TryGetValue(field, out var value) && value is not null)
            {
                return ToText(value, source, field);
            }

            unresolved.Add(field);
            return string.Empty; // never leave the literal token in the record
        });

        foreach (var field in unresolved)
        {
            warnings.Add(
                $"{label} rule for \"{targetField}\": \"{{{field}}}\" has no value on the related record and was left blank.");
        }

        target[targetField] = resolved;
    }

    private static IReadOnlyList<string> ExtractPlaceholderFields(string? expression)
    {
        if (string.IsNullOrEmpty(expression))
        {
            return Array.Empty<string>();
        }

        return PlaceholderPattern.Matches(expression)
            .Select(match => match.Groups[1].Value.Trim())
            .Where(field => field.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Text rendering for Text/Memo targets and placeholders — the push path's <c>TransformValue</c> conventions
    /// (ISO-8601 dates, Yes/No booleans), with option sets rendered as their label when Dataverse returned one.
    /// </summary>
    private static string ToText(object value, Entity source, string field)
    {
        if (value is OptionSetValue or OptionSetValueCollection
            && source.FormattedValues.TryGetValue(field, out var label)
            && !string.IsNullOrEmpty(label))
        {
            return label;
        }

        return value switch
        {
            string text => text,
            OptionSetValue option => option.Value.ToString(CultureInfo.InvariantCulture),
            Money money => money.Value.ToString(CultureInfo.InvariantCulture),
            EntityReference reference => reference.Name ?? reference.Id.ToString("D"),
            DateTime date => date.ToString("o", CultureInfo.InvariantCulture),
            bool flag => flag ? "Yes" : "No",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    /// <summary>
    /// Converts a Default-rule literal to the CLR type the SDK requires for the target field type. Returns false
    /// (the rule is skipped with a warning) rather than writing a value Dataverse would reject.
    /// </summary>
    private static bool TryConvertLiteral(string literal, int targetFieldType, out object? converted)
    {
        var trimmed = literal.Trim();
        converted = null;

        switch (targetFieldType)
        {
            case FieldTypeLookup:
                return false; // a literal cannot bind a lookup

            case FieldTypeOptionSet:
                if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var option))
                {
                    converted = new OptionSetValue(option);
                    return true;
                }

                return false;

            case FieldTypeNumber:
                if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
                {
                    converted = whole;
                    return true;
                }

                if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var fractional))
                {
                    converted = fractional;
                    return true;
                }

                return false;

            case FieldTypeDateTime:
                if (DateTime.TryParse(
                        trimmed,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                        out var date))
                {
                    converted = date;
                    return true;
                }

                return false;

            case FieldTypeBoolean:
                if (bool.TryParse(trimmed, out var flag))
                {
                    converted = flag;
                    return true;
                }

                if (trimmed is "1" || trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase))
                {
                    converted = true;
                    return true;
                }

                if (trimmed is "0" || trimmed.Equals("no", StringComparison.OrdinalIgnoreCase))
                {
                    converted = false;
                    return true;
                }

                return false;

            default: // Text, Memo, and any unrecognised type → the literal as written (client behaviour)
                converted = literal;
                return true;
        }
    }
}
