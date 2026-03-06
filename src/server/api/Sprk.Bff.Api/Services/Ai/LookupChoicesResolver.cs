using System.Text.Json;

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

            var values = await ResolveReferenceAsync(choicesRef, fieldName, cancellationToken);
            if (values != null)
            {
                result[choicesRef] = values;
            }
        }

        return result;
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

        // Dataverse OData entity set name = logical name + 's'
        var entitySetName = entityLogicalName + "s";

        try
        {
            var values = await _scopeResolver.QueryLookupValuesAsync(
                entitySetName, selectField, cancellationToken);

            if (values.Length == 0)
            {
                _logger.LogWarning(
                    "$choices lookup for field '{FieldName}': no values found in {Entity}.{Field}",
                    fieldName, entityLogicalName, selectField);
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
            return null;
        }
    }
}
