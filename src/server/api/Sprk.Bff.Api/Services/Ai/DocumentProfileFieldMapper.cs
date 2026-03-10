using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Maps Document Profile output types to sprk_document field names.
/// </summary>
/// <remarks>
/// Output type names are case-insensitive. Field names use Dataverse naming convention (sprk_*).
/// </remarks>
public static class DocumentProfileFieldMapper
{
    /// <summary>
    /// Maps output type name to sprk_document field name.
    /// </summary>
    /// <param name="outputTypeName">Output type name (e.g., "TL;DR", "Summary", "Keywords")</param>
    /// <returns>Dataverse field name (e.g., "sprk_filetldr", "sprk_filesummary") or null if no mapping exists.</returns>
    /// <remarks>
    /// Supports both legacy output type names ("TL;DR", "Document Type") and
    /// JPS structured output field names ("tldr", "documentType").
    /// Field names use the sprk_file* prefix from the original schema (sprk_filesummary, sprk_filetldr, sprk_filekeywords).
    /// The sprk_entities field was added later for extracted entities JSON.
    /// </remarks>
    public static string? GetFieldName(string? outputTypeName)
    {
        return outputTypeName?.ToLowerInvariant() switch
        {
            // Legacy output type names
            "tl;dr" => "sprk_filetldr",
            "summary" => "sprk_filesummary",
            "keywords" => "sprk_filekeywords",
            "document type" => "sprk_documenttype",
            "entities" => "sprk_entities",
            // JPS structured output field names
            "tldr" => "sprk_filetldr",
            "documenttype" => "sprk_documenttype",
            "searchprofile" => "sprk_searchprofile",
            _ => null
        };
    }

    /// <summary>
    /// Prepares output value for storage in sprk_document field.
    /// Handles JSON serialization for structured outputs like Entities.
    /// </summary>
    /// <param name="outputTypeName">Output type name</param>
    /// <param name="value">Raw output value</param>
    /// <returns>Prepared value for storage (may be JSON-serialized)</returns>
    public static object? PrepareValue(string? outputTypeName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Entities output type should be stored as JSON (supports both legacy "Entities" and JPS "entities")
        if (outputTypeName?.Equals("Entities", StringComparison.OrdinalIgnoreCase) == true
            || outputTypeName?.Equals("entities", StringComparison.Ordinal) == true)
        {
            // If value is already JSON, validate and return as-is
            // If not, wrap in a simple structure
            try
            {
                // Validate it's JSON
                JsonDocument.Parse(value);
                return value;
            }
            catch (JsonException)
            {
                // Not valid JSON - wrap in simple structure
                return JsonSerializer.Serialize(new { raw = value });
            }
        }

        // For other types, return value as-is
        return value;
    }

    /// <summary>
    /// Creates a field mapping dictionary for Document Profile outputs.
    /// Maps output type names to their corresponding sprk_document field values.
    /// Automatically builds a search profile from collected outputs.
    /// </summary>
    /// <param name="outputs">Dictionary of output type name → value</param>
    /// <param name="parentEntityName">Optional parent entity display name for search profile enrichment</param>
    /// <param name="parentEntityType">Optional parent entity type for search profile enrichment</param>
    /// <param name="fileName">Optional file name for search profile enrichment</param>
    /// <returns>Dictionary of sprk_document field name → prepared value</returns>
    public static Dictionary<string, object?> CreateFieldMapping(
        Dictionary<string, string?> outputs,
        string? parentEntityName = null,
        string? parentEntityType = null,
        string? fileName = null)
    {
        var fields = new Dictionary<string, object?>();

        foreach (var output in outputs)
        {
            var fieldName = GetFieldName(output.Key);
            if (fieldName != null)
            {
                var preparedValue = PrepareValue(output.Key, output.Value);
                if (preparedValue != null)
                {
                    fields[fieldName] = preparedValue;
                }
            }
        }

        // Build search profile from collected outputs (deterministic post-processing, no AI calls)
        var nonNullOutputs = new Dictionary<string, string>();
        foreach (var kvp in outputs)
        {
            if (kvp.Value != null)
                nonNullOutputs[kvp.Key] = kvp.Value;
        }

        var searchProfile = BuildSearchProfile(nonNullOutputs, parentEntityName, parentEntityType, fileName);
        if (searchProfile != null)
        {
            fields["sprk_searchprofile"] = searchProfile;
        }

        return fields;
    }

    /// <summary>
    /// Checks if an output type should be mapped to a document field.
    /// </summary>
    public static bool IsMappable(string? outputTypeName)
    {
        return GetFieldName(outputTypeName) != null;
    }

    /// <summary>
    /// Gets all supported output type names for Document Profile.
    /// </summary>
    public static IReadOnlyList<string> SupportedOutputTypes { get; } = new[]
    {
        "TL;DR",
        "Summary",
        "Keywords",
        "Document Type",
        "Entities",
        "searchprofile"
    };

    /// <summary>
    /// Deterministically assembles a BM25-optimized search profile string from Document Profile outputs.
    /// </summary>
    /// <param name="outputs">Dictionary of output type name → value (case-insensitive keys expected)</param>
    /// <param name="parentEntityName">Optional parent entity display name (e.g., "Acme Corp")</param>
    /// <param name="parentEntityType">Optional parent entity type (e.g., "account")</param>
    /// <param name="fileName">Optional file name (extension will be stripped)</param>
    /// <returns>
    /// Pipe-delimited search profile string for BM25 tokenization, or null if fewer than 2 parts are assembled.
    /// </returns>
    public static string? BuildSearchProfile(
        Dictionary<string, string> outputs,
        string? parentEntityName = null,
        string? parentEntityType = null,
        string? fileName = null)
    {
        var parts = new List<string>();

        // 1. Document Type
        var documentType = GetOutputValue(outputs, "documenttype", "document type");
        if (!string.IsNullOrWhiteSpace(documentType))
            parts.Add(documentType.Trim());

        // 2. TL;DR
        var tldr = GetOutputValue(outputs, "tldr", "tl;dr");
        if (!string.IsNullOrWhiteSpace(tldr))
            parts.Add(tldr.Trim());

        // 3. Entities (extract names from JSON array)
        var entitiesJson = GetOutputValue(outputs, "entities");
        var entityNames = ExtractEntityNames(entitiesJson);
        if (!string.IsNullOrWhiteSpace(entityNames))
            parts.Add(entityNames);

        // 4. Keywords
        var keywords = GetOutputValue(outputs, "keywords");
        if (!string.IsNullOrWhiteSpace(keywords))
            parts.Add(keywords.Trim());

        // 5. Parent Entity
        if (!string.IsNullOrWhiteSpace(parentEntityType) && !string.IsNullOrWhiteSpace(parentEntityName))
            parts.Add($"{parentEntityType.Trim()}: {parentEntityName.Trim()}");

        // 6. File Name (without extension)
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName).Trim();
            if (!string.IsNullOrWhiteSpace(nameWithoutExtension))
                parts.Add(nameWithoutExtension);
        }

        return parts.Count >= 2 ? string.Join(" | ", parts) : null;
    }

    /// <summary>
    /// Extracts entity names from a JSON entity output string.
    /// Expects a JSON array of objects with a "name" property.
    /// </summary>
    /// <param name="entityJson">JSON string (e.g., [{"name":"Acme","type":"Organization"}])</param>
    /// <returns>Comma-separated entity names, or null if parsing fails or no names found.</returns>
    private static string? ExtractEntityNames(string? entityJson)
    {
        if (string.IsNullOrWhiteSpace(entityJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(entityJson);
            var names = new List<string>();

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (element.TryGetProperty("name", out var nameProp) &&
                        nameProp.ValueKind == JsonValueKind.String)
                    {
                        var name = nameProp.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                            names.Add(name);
                    }
                }
            }

            return names.Count > 0 ? string.Join(", ", names) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets a value from the outputs dictionary, trying multiple key variants (case-insensitive).
    /// </summary>
    private static string? GetOutputValue(Dictionary<string, string> outputs, params string[] keys)
    {
        foreach (var key in keys)
        {
            foreach (var kvp in outputs)
            {
                if (kvp.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }
        }
        return null;
    }
}
