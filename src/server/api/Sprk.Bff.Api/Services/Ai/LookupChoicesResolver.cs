using System.Text.Json;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Resolves <c>$choices</c> references by querying Dataverse for valid option values.
/// Supports four reference prefixes for different Dataverse field types.
/// </summary>
/// <remarks>
/// <para>Supported reference formats:</para>
/// <list type="bullet">
/// <item><c>"lookup:{entityLogicalName}.{fieldName}"</c> — entity reference lookups.
/// Queries all active records and returns field values as enum.
/// Example: <c>"lookup:sprk_mattertype_ref.sprk_mattertypename"</c></item>
/// <item><c>"optionset:{entityLogicalName}.{attributeName}"</c> — single-select choice/picklist fields.
/// Queries PicklistAttributeMetadata for option labels.
/// Example: <c>"optionset:sprk_matter.sprk_matterstatus"</c></item>
/// <item><c>"multiselect:{entityLogicalName}.{attributeName}"</c> — multi-select picklist fields.
/// Queries MultiSelectPicklistAttributeMetadata for option labels.
/// Example: <c>"multiselect:sprk_matter.sprk_jurisdictions"</c></item>
/// <item><c>"boolean:{entityLogicalName}.{attributeName}"</c> — two-option boolean fields.
/// Queries BooleanAttributeMetadata for TrueOption/FalseOption labels.
/// Example: <c>"boolean:sprk_matter.sprk_isconfidential"</c></item>
/// </list>
/// <para>
/// Results are cached per reference string for the lifetime of the scoped service
/// instance (one HTTP request) to avoid duplicate Dataverse queries.
/// </para>
/// </remarks>
public sealed class LookupChoicesResolver
{
    private static readonly string[] SupportedPrefixes = ["lookup:", "optionset:", "multiselect:", "boolean:"];

    /// <summary>Prefix of the side-channel key that carries prompt guidance next to a reference's bare names.</summary>
    private const string GuidanceKeyPrefix = "guidance:";

    private const int MaxGuidanceChars = 1000;

    /// <summary>
    /// Total guidance characters per reference. Past it the remaining rows are listed by bare name, so the
    /// prompt stays bounded however many rows an admin adds. The enum is not affected.
    /// </summary>
    private const int MaxTotalGuidanceChars = 8000;

    /// <summary>
    /// Extra per-taxonomy row predicate (entity logical name to OData filter). A disabled row is invisible to the
    /// classifier (mvp-technical-spec.md section on enabled/disabled). Deliberately NOT global: an entity without
    /// the column would 400, and that 400 is swallowed into an empty enum.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string Column, bool Value)[]> AdditionalRowConditions =
        new Dictionary<string, (string Column, bool Value)[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_triagecategory"] = [("sprk_enabled", true)],
        };

    /// <summary>The extra row predicate for <paramref name="entityLogicalName"/> in OData form, or null.</summary>
    public static string? AdditionalFilterFor(string entityLogicalName) =>
        AdditionalRowConditions.TryGetValue(entityLogicalName, out var conditions)
            ? string.Join(" and ", conditions.Select(c => $"{c.Column} eq {(c.Value ? "true" : "false")}"))
            : null;

    /// <summary>
    /// Adds to <paramref name="criteria"/> the SAME row predicate the <c>$choices</c> read applies to
    /// <paramref name="entityLogicalName"/>: active (<c>statecode = 0</c>) plus the per-taxonomy extra conditions
    /// (the QueryExpression form of <see cref="AdditionalFilterFor"/>, from the one definition above). Anything that
    /// resolves a name the classifier emitted back to a row must use this, so a row the classifier was never offered
    /// (inactive, or disabled) can never be saved.
    /// </summary>
    public static void AddOfferedRowPredicate(Microsoft.Xrm.Sdk.Query.FilterExpression criteria, string entityLogicalName)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        criteria.AddCondition("statecode", Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal, 0);
        if (AdditionalRowConditions.TryGetValue(entityLogicalName, out var conditions))
        {
            foreach (var (column, value) in conditions)
                criteria.AddCondition(column, Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal, value);
        }
    }

    /// <summary>
    /// Lookup taxonomies that carry authored classifier guidance: entity logical name to guidance column.
    /// Adding a taxonomy here is the whole extension; a reference to any other entity is unaffected and
    /// issues no extra query.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> GuidanceColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_triagecategory"] = "sprk_classifierguidance",
        };

    /// <summary>
    /// The key under which <see cref="ResolveFromJpsAsync"/> places the prompt-facing "name — guidance" lines
    /// for <paramref name="choicesRef"/>. The value at <paramref name="choicesRef"/> itself stays the bare names
    /// (the constrained-decoding enum), so the schema enum can never carry guidance text.
    /// </summary>
    public static string GuidanceKey(string choicesRef) => GuidanceKeyPrefix + choicesRef;

    /// <summary>True when <paramref name="key"/> is a guidance side-channel key rather than a reference.</summary>
    public static bool IsGuidanceKey(string key) => key.StartsWith(GuidanceKeyPrefix, StringComparison.Ordinal);

    private readonly IScopeResolverService _scopeResolver;
    private readonly ILogger<LookupChoicesResolver> _logger;

    /// <summary>
    /// Per-request cache to avoid duplicate Dataverse queries for the same reference.
    /// </summary>
    private readonly Dictionary<string, string[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    public LookupChoicesResolver(
        IScopeResolverService scopeResolver,
        ILogger<LookupChoicesResolver> logger)
    {
        _scopeResolver = scopeResolver;
        _logger = logger;
    }

    /// <summary>
    /// Scans JPS output fields for <c>$choices</c> references with supported prefixes
    /// and resolves them by querying Dataverse.
    /// </summary>
    /// <param name="rawPrompt">The raw JPS JSON string (Action system prompt).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Dictionary mapping <c>$choices</c> reference strings to resolved enum values.
    /// Empty dictionary if no resolvable references found or all resolutions failed.
    /// </returns>
    public async Task<IReadOnlyDictionary<string, string[]>> ResolveFromJpsAsync(
        string? rawPrompt,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(rawPrompt))
            return result;

        // Quick check: does it contain any supported prefix?
        var hasAnyPrefix = false;
        foreach (var prefix in SupportedPrefixes)
        {
            if (rawPrompt.Contains($"\"{prefix}", StringComparison.OrdinalIgnoreCase))
            {
                hasAnyPrefix = true;
                break;
            }
        }
        if (!hasAnyPrefix)
            return result;

        // Parse JPS to extract output fields with $choices
        List<(string fieldName, string choicesRef)> refs;
        try
        {
            refs = ExtractChoicesReferences(rawPrompt);
        }
        catch (JsonException)
        {
            _logger.LogWarning("Failed to parse JPS for $choices extraction");
            return result;
        }

        if (refs.Count == 0)
            return result;

        _logger.LogDebug(
            "Found {Count} $choices references to resolve: [{Refs}]",
            refs.Count, string.Join(", ", refs.Select(r => r.choicesRef)));

        // Resolve each unique reference
        foreach (var (fieldName, choicesRef) in refs)
        {
            if (result.ContainsKey(choicesRef))
                continue; // Already resolved (multiple fields referencing same source)

            await ResolveIntoAsync(result, choicesRef, fieldName, cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Resolves ONE <c>$choices</c> reference that is not declared in a JPS document, with the same result shape
    /// as <see cref="ResolveFromJpsAsync"/>: the bare names under <paramref name="choicesRef"/> and, for a
    /// taxonomy with a guidance column, the "name — guidance" lines under <see cref="GuidanceKey"/>. Used by a
    /// code-constant prompt that classifies against the same editable taxonomy as a JPS Action (rung 5,
    /// spaarke-ontology-platform-r1 D-117(b)), so both read the same rows through the same filter and budget.
    /// </summary>
    /// <returns>Empty when the reference has an unsupported prefix or resolves to no values.</returns>
    public async Task<IReadOnlyDictionary<string, string[]>> ResolveChoicesReferenceAsync(
        string choicesRef,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(choicesRef)
            || !SupportedPrefixes.Any(p => choicesRef.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            return result;
        }

        await ResolveIntoAsync(result, choicesRef, choicesRef, cancellationToken);
        return result;
    }

    /// <summary>Resolves one reference (names, then guidance lines when the taxonomy has them) into <paramref name="result"/>.</summary>
    private async Task ResolveIntoAsync(
        Dictionary<string, string[]> result, string choicesRef, string fieldName, CancellationToken cancellationToken)
    {
        var values = await ResolveReferenceAsync(choicesRef, fieldName, cancellationToken);
        if (values == null)
            return;

        result[choicesRef] = values;

        var guidanceLines = await ResolveGuidanceLinesAsync(choicesRef, values, cancellationToken);
        if (guidanceLines != null)
        {
            result[GuidanceKey(choicesRef)] = guidanceLines;
        }
    }

    /// <summary>
    /// For a <c>lookup:</c> reference to a taxonomy with a guidance column, builds the prompt-facing
    /// "name — guidance" lines, one per resolved name in enum order. A name with no guidance, a name past the
    /// total budget, and every name when the guidance read fails are emitted bare, so the prompt still lists the
    /// categories (a structured-output Action otherwise lists none). Returns null only when the reference has no
    /// guidance column.
    /// </summary>
    private async Task<string[]?> ResolveGuidanceLinesAsync(
        string choicesRef, string[] names, CancellationToken cancellationToken)
    {
        if (!choicesRef.StartsWith("lookup:", StringComparison.OrdinalIgnoreCase)
            || !TryParseReference(choicesRef, "lookup:", "guidance", out var entity, out var nameField)
            || !GuidanceColumns.TryGetValue(entity, out var guidanceField))
        {
            return null;
        }

        IReadOnlyDictionary<string, string>? guidance;
        try
        {
            guidance = await _scopeResolver.QueryLookupGuidanceAsync(
                ToEntitySetName(entity), nameField, guidanceField, AdditionalFilterFor(entity), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "$choices guidance read failed for {Ref}; the prompt lists bare category names (enum unaffected)", choicesRef);
            ChoicesResolutionTelemetry.RecordFailure(choicesRef, ChoicesResolutionTelemetry.ReasonGuidanceReadFailed);
            return names;
        }

        // Bound to the names already behind the enum: guidance for a row that is not in the enum is dropped,
        // so the prompt can never offer a category the schema would reject.
        var lines = new string[names.Length];
        var used = 0;
        for (var i = 0; i < names.Length; i++)
        {
            lines[i] = names[i];
            if (guidance is null || !guidance.TryGetValue(names[i], out var g) || string.IsNullOrWhiteSpace(g))
                continue;

            var text = Normalize(g);
            if (used + text.Length > MaxTotalGuidanceChars)
            {
                _logger.LogWarning(
                    "$choices guidance for {Ref} exceeds the {Budget}-char budget; '{Name}' and later rows are listed bare",
                    choicesRef, MaxTotalGuidanceChars, names[i]);
                continue;
            }

            used += text.Length;
            lines[i] = $"{names[i]} — {text}";
        }

        return lines;
    }

    private static string Normalize(string guidance)
    {
        var flat = string.Join(' ', guidance.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length <= MaxGuidanceChars)
            return flat;

        // Do not cut a surrogate pair in half.
        var cut = char.IsHighSurrogate(flat[MaxGuidanceChars - 1]) ? MaxGuidanceChars - 1 : MaxGuidanceChars;
        return flat[..cut].TrimEnd() + "…";
    }

    /// <summary>
    /// Extracts field name + $choices reference pairs from JPS output fields
    /// that use any supported prefix.
    /// </summary>
    private static List<(string fieldName, string choicesRef)> ExtractChoicesReferences(string rawPrompt)
    {
        var refs = new List<(string, string)>();

        using var doc = JsonDocument.Parse(rawPrompt);
        var root = doc.RootElement;

        if (!root.TryGetProperty("output", out var output))
            return refs;

        if (!output.TryGetProperty("fields", out var fields) ||
            fields.ValueKind != JsonValueKind.Array)
            return refs;

        foreach (var field in fields.EnumerateArray())
        {
            if (!field.TryGetProperty("$choices", out var choices))
                continue;

            var choicesRef = choices.GetString();
            if (string.IsNullOrWhiteSpace(choicesRef))
                continue;

            // Check if reference uses any supported prefix (skip "downstream:" — handled by renderer)
            var isSupported = false;
            foreach (var prefix in SupportedPrefixes)
            {
                if (choicesRef.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    isSupported = true;
                    break;
                }
            }
            if (!isSupported)
                continue;

            var fieldName = field.TryGetProperty("name", out var nameProp)
                ? nameProp.GetString() ?? "unknown"
                : "unknown";

            refs.Add((fieldName, choicesRef));
        }

        return refs;
    }

    /// <summary>
    /// Routes a $choices reference to the appropriate Dataverse query based on its prefix.
    /// </summary>
    private async Task<string[]?> ResolveReferenceAsync(
        string choicesRef,
        string fieldName,
        CancellationToken cancellationToken)
    {
        // Check per-request cache
        if (_cache.TryGetValue(choicesRef, out var cached))
            return cached;

        // Parse the entity.field portion (shared by all prefixes)
        string[]? values = null;

        if (choicesRef.StartsWith("lookup:", StringComparison.OrdinalIgnoreCase))
        {
            values = await ResolveLookupAsync(choicesRef, "lookup:", fieldName, cancellationToken);
        }
        else if (choicesRef.StartsWith("optionset:", StringComparison.OrdinalIgnoreCase))
        {
            values = await ResolveOptionSetAsync(choicesRef, "optionset:", fieldName, isMultiSelect: false, cancellationToken);
        }
        else if (choicesRef.StartsWith("multiselect:", StringComparison.OrdinalIgnoreCase))
        {
            values = await ResolveOptionSetAsync(choicesRef, "multiselect:", fieldName, isMultiSelect: true, cancellationToken);
        }
        else if (choicesRef.StartsWith("boolean:", StringComparison.OrdinalIgnoreCase))
        {
            values = await ResolveBooleanAsync(choicesRef, "boolean:", fieldName, cancellationToken);
        }

        if (values != null)
        {
            _cache[choicesRef] = values;
        }

        return values;
    }

    /// <summary>
    /// Derives the Dataverse OData entity-set (collection) name from a logical name using the platform's
    /// default English pluralization. Only special-cases the endings where a naive <c>"+ s"</c> is wrong
    /// (and therefore already 404s): consonant+<c>y</c> → <c>ies</c>; <c>s</c>/<c>x</c>/<c>z</c>/<c>ch</c>/<c>sh</c>
    /// → <c>es</c>. Every other name keeps the naive <c>"+ s"</c>, so no previously-resolving lookup can
    /// regress. (A custom, non-default EntitySetName would still need metadata resolution — not observed in
    /// this codebase's action catalog.)
    /// </summary>
    private static string ToEntitySetName(string logicalName)
    {
        if (string.IsNullOrEmpty(logicalName))
            return logicalName;

        if (logicalName.Length >= 2
            && logicalName.EndsWith('y')
            && "aeiou".IndexOf(char.ToLowerInvariant(logicalName[^2])) < 0)
        {
            return logicalName[..^1] + "ies";
        }

        if (logicalName.EndsWith('s')
            || logicalName.EndsWith('x')
            || logicalName.EndsWith('z')
            || logicalName.EndsWith("ch", StringComparison.Ordinal)
            || logicalName.EndsWith("sh", StringComparison.Ordinal))
        {
            return logicalName + "es";
        }

        return logicalName + "s";
    }

    /// <summary>
    /// Parses a "prefix:entity.field" reference into its entity and field parts.
    /// </summary>
    private bool TryParseReference(string choicesRef, string prefix, string fieldName, out string entity, out string field)
    {
        entity = string.Empty;
        field = string.Empty;

        var refBody = choicesRef[prefix.Length..];
        var dotIndex = refBody.IndexOf('.');
        if (dotIndex <= 0 || dotIndex >= refBody.Length - 1)
        {
            _logger.LogWarning(
                "$choices reference on field '{FieldName}' has invalid format (expected '{Prefix}{{entity}}.{{field}}'): {Ref}",
                fieldName, prefix, choicesRef);
            return false;
        }

        entity = refBody[..dotIndex];
        field = refBody[(dotIndex + 1)..];
        return true;
    }

    /// <summary>
    /// Resolves a <c>lookup:</c> reference by querying entity records.
    /// </summary>
    private async Task<string[]?> ResolveLookupAsync(
        string choicesRef, string prefix, string fieldName, CancellationToken cancellationToken)
    {
        if (!TryParseReference(choicesRef, prefix, fieldName, out var entityLogicalName, out var selectField))
            return null;

        // Dataverse OData entity set (collection) name. The default plural follows standard English
        // rules — NOT a naive "+ s". `sprk_triagecategory` → `sprk_triagecategories` (y → ies), not
        // `sprk_triagecategorys` (which 404s, silently blanking the resolved $choices — the exact bug
        // that left every email-triage category unresolved, email-communication-intelligence-r2 2026-09-03).
        var entitySetName = ToEntitySetName(entityLogicalName);

        try
        {
            var additionalFilter = AdditionalFilterFor(entityLogicalName);
            var values = additionalFilter is null
                ? await _scopeResolver.QueryLookupValuesAsync(entitySetName, selectField, cancellationToken)
                : await _scopeResolver.QueryLookupValuesAsync(entitySetName, selectField, additionalFilter, cancellationToken);

            if (values.Length == 0)
            {
                _logger.LogWarning(
                    "$choices lookup for field '{FieldName}': no values found in {Entity}.{Field}",
                    fieldName, entityLogicalName, selectField);
                ChoicesResolutionTelemetry.RecordFailure(choicesRef, ChoicesResolutionTelemetry.ReasonNoValues);
                return null;
            }

            _logger.LogInformation(
                "$choices lookup resolved for field '{FieldName}': {Count} values from {Entity}.{Field}",
                fieldName, values.Length, entityLogicalName, selectField);

            return values;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "$choices lookup for field '{FieldName}' failed querying {Entity}.{Field}",
                fieldName, entityLogicalName, selectField);
            if (ex is not OperationCanceledException)
                ChoicesResolutionTelemetry.RecordFailure(choicesRef, ChoicesResolutionTelemetry.ReasonReadFailed);
            return null;
        }
    }

    /// <summary>
    /// Resolves an <c>optionset:</c> or <c>multiselect:</c> reference by querying attribute metadata.
    /// </summary>
    private async Task<string[]?> ResolveOptionSetAsync(
        string choicesRef, string prefix, string fieldName, bool isMultiSelect, CancellationToken cancellationToken)
    {
        if (!TryParseReference(choicesRef, prefix, fieldName, out var entityLogicalName, out var attributeName))
            return null;

        try
        {
            var values = await _scopeResolver.QueryOptionSetLabelsAsync(
                entityLogicalName, attributeName, isMultiSelect, cancellationToken);

            if (values.Length == 0)
            {
                _logger.LogWarning(
                    "$choices {Prefix} for field '{FieldName}': no options found in {Entity}.{Attribute}",
                    prefix.TrimEnd(':'), fieldName, entityLogicalName, attributeName);
                ChoicesResolutionTelemetry.RecordFailure(choicesRef, ChoicesResolutionTelemetry.ReasonNoValues);
                return null;
            }

            _logger.LogInformation(
                "$choices {Prefix} resolved for field '{FieldName}': {Count} options from {Entity}.{Attribute}",
                prefix.TrimEnd(':'), fieldName, values.Length, entityLogicalName, attributeName);

            return values;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "$choices {Prefix} for field '{FieldName}' failed querying {Entity}.{Attribute}",
                prefix.TrimEnd(':'), fieldName, entityLogicalName, attributeName);
            if (ex is not OperationCanceledException)
                ChoicesResolutionTelemetry.RecordFailure(choicesRef, ChoicesResolutionTelemetry.ReasonReadFailed);
            return null;
        }
    }

    /// <summary>
    /// Resolves a <c>boolean:</c> reference by querying boolean attribute metadata.
    /// </summary>
    private async Task<string[]?> ResolveBooleanAsync(
        string choicesRef, string prefix, string fieldName, CancellationToken cancellationToken)
    {
        if (!TryParseReference(choicesRef, prefix, fieldName, out var entityLogicalName, out var attributeName))
            return null;

        try
        {
            var values = await _scopeResolver.QueryBooleanLabelsAsync(
                entityLogicalName, attributeName, cancellationToken);

            if (values.Length == 0)
            {
                _logger.LogWarning(
                    "$choices boolean for field '{FieldName}': no labels found in {Entity}.{Attribute}",
                    fieldName, entityLogicalName, attributeName);
                ChoicesResolutionTelemetry.RecordFailure(choicesRef, ChoicesResolutionTelemetry.ReasonNoValues);
                return null;
            }

            _logger.LogInformation(
                "$choices boolean resolved for field '{FieldName}': [{Labels}] from {Entity}.{Attribute}",
                fieldName, string.Join(", ", values), entityLogicalName, attributeName);

            return values;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "$choices boolean for field '{FieldName}' failed querying {Entity}.{Attribute}",
                fieldName, entityLogicalName, attributeName);
            if (ex is not OperationCanceledException)
                ChoicesResolutionTelemetry.RecordFailure(choicesRef, ChoicesResolutionTelemetry.ReasonReadFailed);
            return null;
        }
    }
}
