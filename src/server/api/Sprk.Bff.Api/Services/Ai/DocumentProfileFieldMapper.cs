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
    /// <returns>Dataverse field name (e.g., "sprk_tldr", "sprk_summary") or null if no mapping exists.</returns>
    public static string? GetFieldName(string? outputTypeName)
    {
        return outputTypeName?.ToLowerInvariant() switch
        {
            "tl;dr" => "sprk_tldr",
            "summary" => "sprk_summary",
            "keywords" => "sprk_keywords",
            "document type" => "sprk_documenttype",
            "entities" => "sprk_entities",
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

        // Entities output type should be stored as JSON
        if (outputTypeName?.Equals("Entities", StringComparison.OrdinalIgnoreCase) == true)
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
    /// </summary>
    /// <param name="outputs">Dictionary of output type name → value</param>
    /// <returns>Dictionary of sprk_document field name → prepared value</returns>
    public static Dictionary<string, object?> CreateFieldMapping(Dictionary<string, string?> outputs)
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
        "Entities"
    };
}
