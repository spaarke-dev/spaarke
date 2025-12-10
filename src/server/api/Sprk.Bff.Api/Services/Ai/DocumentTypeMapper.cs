namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Maps AI-extracted document type strings to Dataverse choice field values.
/// See: docs/ai-knowledge/guides/SPAARKE-AI-ARCHITECTURE.md Section 5.1
/// </summary>
public static class DocumentTypeMapper
{
    /// <summary>
    /// Mapping of AI document type strings to Dataverse choice values.
    /// Keys are case-insensitive.
    /// </summary>
    private static readonly Dictionary<string, int> AiToDataverseMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["contract"] = 100000000,
        ["invoice"] = 100000001,
        ["proposal"] = 100000002,
        ["report"] = 100000003,
        ["letter"] = 100000004,
        ["memo"] = 100000005,
        ["email"] = 100000006,
        ["agreement"] = 100000007,
        ["statement"] = 100000008,
        ["other"] = 100000009
    };

    /// <summary>
    /// Default value when document type is null, empty, or unrecognized.
    /// </summary>
    public const int DefaultValue = 100000009; // "Other"

    /// <summary>
    /// Converts an AI-extracted document type string to a Dataverse choice value.
    /// </summary>
    /// <param name="aiDocumentType">The document type string from AI extraction (e.g., "contract", "invoice").</param>
    /// <returns>The corresponding Dataverse choice value, or null if input is null/empty.</returns>
    public static int? ToDataverseValue(string? aiDocumentType)
    {
        if (string.IsNullOrWhiteSpace(aiDocumentType))
            return null;

        return AiToDataverseMap.TryGetValue(aiDocumentType.Trim(), out var value)
            ? value
            : DefaultValue;
    }

    /// <summary>
    /// Gets all valid document type values that the AI should return.
    /// Used for constructing AI prompts.
    /// </summary>
    public static IReadOnlyCollection<string> ValidDocumentTypes => AiToDataverseMap.Keys;
}
