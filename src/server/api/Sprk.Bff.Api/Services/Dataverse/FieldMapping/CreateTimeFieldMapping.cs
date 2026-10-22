using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;

namespace Sprk.Bff.Api.Services.Dataverse.FieldMapping;

/// <summary>
/// The pure half of create-time Field Mapping Framework application: given a profile's rules and the source record
/// already read, write each rule's value onto the new record's payload. No I/O — the calling creation service
/// (<see cref="Sprk.Bff.Api.Services.RecordCreation.RecordCreationService"/>, step 3) owns the one profile read and the one
/// source read.
/// </summary>
/// <remarks>
/// <para>Extracted from the Office creation core (task 030 Step 9.5 review, CLAUDE.md §11.5; that core was deleted by
/// field mapping task 018c): the rule engine changes when the Field Mapping Framework changes, the creation service
/// changes when an entity's creation rules change — two reasons to change, so two files. Internal static, not a DI
/// registration (ADR-010).</para>
///
/// <para><b>Semantics</b> mirror the client engine <c>FieldMappingService.applyFieldMappings</c>: rules in
/// <c>ExecutionOrder</c>; Copy (scalar + lookup), Default (literal), Concat / Template (one <c>{field}</c> resolver);
/// every failure a warning, never a throw; no <c>source == target</c> guard. <b>Server-side differences</b> (notes/030
/// §8), all because the SDK needs typed values where the client sends JSON: a Default literal is converted to the
/// target type's CLR type; a Copy into a Text/Memo target is rendered as text (option sets as their label, booleans
/// Yes/No, dates ISO-8601 — the push path's <c>TransformValue</c> conventions); a null source value is skipped rather
/// than copied as null (on a create that is the same outcome).</para>
///
/// <para><b>One entry point.</b> <see cref="ApplyFillOnly"/> is the FR-02 path: a column the caller supplied is never
/// overwritten (see <see cref="ApplyFillOnly"/> for the exact "not supplied" test), and a rule that targets the ADR-024
/// regarding pair or a resolver field is refused (FR-05(e), <see cref="RegardingPairAndResolverColumns"/>). It writes a
/// typed value — never a string — into a non-text target (DI-001 / #2124). The overwrite entry point (<c>Apply</c>, the
/// Office creation core's) went with that core (field mapping task 018c).</para>
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

    /// <summary>
    /// The ADR-024 polymorphic regarding association a mapping rule may never write: the pair
    /// (<c>sprk_regardingrecordid</c> + <c>sprk_regardingrecordtype</c>, <see cref="SecureRootInheritance.PairIdColumn"/> /
    /// <see cref="SecureRootInheritance.PairTypeColumn"/>) and the denormalised display resolver fields ADR-024 lists beside
    /// them (name, url, number — the same three <c>OwnedChildWrite</c> treats as the pair's display columns). The caller
    /// sets the pair; <c>CallerWriteAuthorization</c> checks it; a mapping rule that aimed at it could corrupt the
    /// association and its security filing (#1617).
    /// </summary>
    internal static readonly IReadOnlySet<string> RegardingPairAndResolverColumns =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SecureRootInheritance.PairIdColumn,
            SecureRootInheritance.PairTypeColumn,
            "sprk_regardingrecordname",
            "sprk_regardingrecordurl",
            "sprk_regardingrecordnumber",
        };

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
    /// The FR-02 fill-only entry point: applies every rule, in <c>ExecutionOrder</c>, onto <paramref name="target"/>, but a
    /// mapped value is written only to a column the caller did not supply. A faulting rule never escapes (it becomes a
    /// warning); the supplied-column snapshot taken first is not guarded, which holds because every caller passes a plain
    /// set. <paramref name="target"/> is the
    /// caller's payload; what it holds when this method is
    /// entered is "supplied" (snapshotted once, so one rule's output never blocks another rule, and rules aimed at the
    /// same column keep their last-rule-wins order). "Not supplied" means exactly:
    /// <list type="bullet">
    /// <item>scalars (Text, Memo, Number, DateTime): absent, null, or an empty/whitespace string — the whitespace string is
    /// normalised to null (removed from the payload) so an unmapped column does not keep it;</item>
    /// <item>lookups: no bind — absent, null, or an <see cref="EntityReference"/> with an empty id;</item>
    /// <item>booleans and option sets: absent only — <c>false</c> and option value <c>0</c> are supplied.</item>
    /// </list>
    /// A supplied column is skipped silently (an expected outcome, never a warning). The target-type test uses the
    /// rule's <c>TargetFieldType</c>. A rule targeting the regarding pair or a resolver field is refused with one warning
    /// (FR-05(e)); a rule targeting one of <paramref name="protectedAttributes"/> is skipped with a warning.
    /// </summary>
    /// <param name="source">The source record, or null when it could not be read (Copy/Concat/Template then skip
    /// silently — the caller already recorded the single read-failure warning).</param>
    /// <param name="moneyAttributes">Target columns the caller knows are currency columns, or null. The rule's
    /// "Number" type cannot say Money (whole number, decimal and money are one type), and the SDK rejects a decimal in a
    /// money column, so a Default / Concat / Template into a Number target listed here is written as a
    /// <see cref="Money"/>.</param>
    /// <param name="mappedColumns">When given, every target column a rule WROTE is added to it (the rule's trimmed target
    /// field) — the explicit origin set the unified create carries from step 3 to its caller checks (task 014, FR-08): a
    /// column in it is "mapped", every other column of <paramref name="target"/> is the caller's. A supplied, guarded or
    /// skipped column is never added.</param>
    internal static void ApplyFillOnly(
        IEnumerable<FieldMappingRuleEntity> rules,
        Entity target,
        Entity? source,
        IReadOnlySet<string> protectedAttributes,
        List<string> warnings,
        ILogger logger,
        IReadOnlySet<string>? moneyAttributes = null,
        ISet<string>? mappedColumns = null)
    {
        var ordered = rules.ToList();
        var callerSupplied = SnapshotSuppliedColumns(ordered, target, protectedAttributes);
        ApplyAll(ordered, target, source, protectedAttributes, warnings, logger, moneyAttributes, callerSupplied, mappedColumns);
    }

    private static void ApplyAll(
        IEnumerable<FieldMappingRuleEntity> rules,
        Entity target,
        Entity? source,
        IReadOnlySet<string> protectedAttributes,
        List<string> warnings,
        ILogger logger,
        IReadOnlySet<string>? moneyAttributes,
        IReadOnlySet<string> callerSupplied,
        ISet<string>? written)
    {
        foreach (var rule in rules.OrderBy(r => r.ExecutionOrder))
        {
            try
            {
                ApplyRule(rule, target, source, protectedAttributes, warnings, moneyAttributes, callerSupplied, written);
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

    /// <summary>
    /// The columns the caller supplied, per the FR-02 "not supplied" definition, among the columns the rules target.
    /// Whitespace-only scalar strings are removed from <paramref name="target"/> (normalised to null). Guarded targets
    /// (pair, resolver, protected) are left untouched — those rules are refused whatever the caller sent.
    /// </summary>
    private static HashSet<string> SnapshotSuppliedColumns(
        IReadOnlyList<FieldMappingRuleEntity> rules, Entity target, IReadOnlySet<string> protectedAttributes)
    {
        var supplied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            var column = rule.TargetField?.Trim();
            if (string.IsNullOrWhiteSpace(column)
                || RegardingPairAndResolverColumns.Contains(column)
                || protectedAttributes.Contains(column)
                || !target.Attributes.TryGetValue(column, out var value))
            {
                continue; // blank/guarded target, or the column is absent: not supplied
            }

            switch (rule.TargetFieldType)
            {
                case FieldTypeLookup:
                    if (value is EntityReference { Id: var id } && id != Guid.Empty)
                    {
                        supplied.Add(column);
                    }

                    break;

                case FieldTypeOptionSet:
                case FieldTypeBoolean:
                    supplied.Add(column); // absent only: a present false / 0 is a value the caller chose
                    break;

                default: // Text, Memo, Number, DateTime
                    if (value is string text && string.IsNullOrWhiteSpace(text))
                    {
                        target.Attributes.Remove(column); // empty/whitespace → null → not supplied
                    }
                    else if (value is not null)
                    {
                        supplied.Add(column);
                    }

                    break;
            }
        }

        return supplied;
    }

    private static void ApplyRule(
        FieldMappingRuleEntity rule,
        Entity target,
        Entity? source,
        IReadOnlySet<string> protectedAttributes,
        List<string> warnings,
        IReadOnlySet<string>? moneyAttributes,
        IReadOnlySet<string> callerSupplied,
        ISet<string>? written)
    {
        // Rule fields come off the Dataverse wire through a mapper, so `required` is not a runtime guarantee here.
        var targetField = rule.TargetField?.Trim();
        if (string.IsNullOrWhiteSpace(targetField))
        {
            warnings.Add($"Field-mapping rule \"{rule.Name}\" has no target field; skipped.");
            return;
        }

        // The unified service (task 014) takes the pair from the caller; the Office adapter derives it before the create.
        if (RegardingPairAndResolverColumns.Contains(targetField))
        {
            // FR-05(e) / ADR-024: the regarding pair and resolver fields are the caller's to set and the write path's to
            // check. One warning per refused rule, naming the rule and the target; refused even if the caller supplied it.
            warnings.Add(
                $"Field-mapping rule \"{rule.Name}\" targeting \"{targetField}\" was skipped: that field is part of the "
                + "regarding association and is set by the caller, never by a field-mapping rule.");
            return;
        }

        if (protectedAttributes.Contains(targetField))
        {
            // Deliberately does NOT claim the server sets the field: it does not set either number (both are left to
            // the planned on-create numbering component), so "is set by the server when a record is created" was
            // false for precisely the load-bearing attribute. "Managed by" is true of all three protected targets.
            warnings.Add(
                $"Field-mapping rule targeting \"{targetField}\" was skipped: that field is managed by the server and "
                + "cannot be set by a field-mapping rule.");
            return;
        }

        if (callerSupplied.Contains(targetField))
        {
            return; // fill-only: the caller's value wins — an expected outcome, no warning
        }

        var isMoney = moneyAttributes is not null && moneyAttributes.Contains(targetField);

        switch (rule.MappingType)
        {
            case MappingTypeCopy:
                ApplyCopy(rule, targetField, target, source, warnings, written);
                return;

            case MappingTypeDefault:
                ApplyDefault(rule, targetField, target, warnings, isMoney, written);
                return;

            case MappingTypeConcat:
            case MappingTypeTemplate:
                ApplyExpression(rule, targetField, target, source, warnings, isMoney, written);
                return;

            default:
                warnings.Add(
                    $"Field-mapping rule for \"{targetField}\" has an unknown mapping type ({rule.MappingType}); skipped.");
                return;
        }
    }

    private static void ApplyCopy(
        FieldMappingRuleEntity rule, string targetField, Entity target, Entity? source, List<string> warnings, ISet<string>? written)
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
                written?.Add(targetField);
                return;
            }

            warnings.Add($"Copy rule \"{sourceField}\" -> \"{targetField}\" skipped: the source value is not a lookup.");
            return;
        }

        target[targetField] = rule.TargetFieldType is FieldTypeText or FieldTypeMemo
            ? ToText(value, source, sourceField)
            : value;
        written?.Add(targetField);
    }

    private static void ApplyDefault(
        FieldMappingRuleEntity rule, string targetField, Entity target, List<string> warnings, bool isMoney, ISet<string>? written)
    {
        if (string.IsNullOrEmpty(rule.DefaultValue))
        {
            warnings.Add($"Default rule for \"{targetField}\" skipped: it has no default value.");
            return;
        }

        if (!TryConvertLiteral(rule.DefaultValue, rule.TargetFieldType, isMoney, out var converted))
        {
            warnings.Add($"Default rule for \"{targetField}\" skipped: its value cannot be written to a field of that type.");
            return;
        }

        target[targetField] = converted;
        written?.Add(targetField);
    }

    private static void ApplyExpression(
        FieldMappingRuleEntity rule, string targetField, Entity target, Entity? source, List<string> warnings, bool isMoney,
        ISet<string>? written)
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

        if (rule.TargetFieldType is FieldTypeText or FieldTypeMemo)
        {
            target[targetField] = resolved;
            written?.Add(targetField);
            return;
        }

        // DI-001 / #2124: a non-text, non-lookup target needs the SDK's typed value, never the resolved string. Same
        // conversion (and same conventions) as a Default literal; a result that does not convert is skipped with one
        // warning rather than written as a string Dataverse would reject at create.
        if (!TryConvertLiteral(resolved, rule.TargetFieldType, isMoney, out var converted))
        {
            warnings.Add($"{label} rule for \"{targetField}\" skipped: its result cannot be written to a field of that type.");
            return;
        }

        target[targetField] = converted;
        written?.Add(targetField);
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
    /// Converts a Default-rule literal (or a Concat/Template result bound for a non-text target) to the CLR type the
    /// SDK requires for the target field type. Returns false
    /// (the rule is skipped with a warning) rather than writing a value Dataverse would reject.
    /// </summary>
    private static bool TryConvertLiteral(string literal, int targetFieldType, bool money, out object? converted)
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
                if (money)
                {
                    // A currency column takes a Money (the SDK rejects a decimal there); it has no whole-number form.
                    if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                    {
                        converted = new Money(amount);
                        return true;
                    }

                    return false;
                }

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
