using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Complete result of AI document analysis including summary, keywords, and extracted entities.
/// Returned from the Document Intelligence service after processing a document.
/// </summary>
public class DocumentAnalysisResult
{
    /// <summary>
    /// Multi-sentence summary of the document content (2-4 sentences).
    /// </summary>
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Brief one-line summary points (TL;DR style).
    /// Array of 1-3 bullet points capturing the key takeaways.
    /// </summary>
    [JsonPropertyName("tldr")]
    public string[] TlDr { get; set; } = [];

    /// <summary>
    /// Comma-separated keywords extracted from the document.
    /// Used for search indexing and categorization.
    /// </summary>
    [JsonPropertyName("keywords")]
    public string Keywords { get; set; } = string.Empty;

    /// <summary>
    /// Named entities extracted from the document including organizations, people, amounts, dates, and references.
    /// </summary>
    [JsonPropertyName("entities")]
    public ExtractedEntities Entities { get; set; } = new();

    /// <summary>
    /// Raw JSON response from the AI model before parsing.
    /// Stored for debugging and fallback purposes.
    /// </summary>
    [JsonPropertyName("rawResponse")]
    public string? RawResponse { get; set; }

    /// <summary>
    /// Indicates whether the AI response was successfully parsed into structured fields.
    /// If false, RawResponse contains the unparsed content.
    /// </summary>
    [JsonPropertyName("parsedSuccessfully")]
    public bool ParsedSuccessfully { get; set; }

    /// <summary>
    /// Email metadata extracted from email files (.eml, .msg).
    /// Only populated when the document is an email file.
    /// Contains structured fields: from, to, cc, subject, date, body, attachments.
    /// </summary>
    [JsonPropertyName("emailMetadata")]
    public EmailMetadata? EmailMetadata { get; set; }

    /// <summary>
    /// Creates a successful analysis result with parsed structured data.
    /// </summary>
    public static DocumentAnalysisResult Success(
        string summary,
        string[] tldr,
        string keywords,
        ExtractedEntities entities,
        string? rawResponse = null) => new()
    {
        Summary = summary,
        TlDr = tldr,
        Keywords = keywords,
        Entities = entities,
        RawResponse = rawResponse,
        ParsedSuccessfully = true
    };

    /// <summary>
    /// Creates a fallback result when parsing fails, preserving the raw response.
    /// </summary>
    public static DocumentAnalysisResult Fallback(string rawResponse, string summary = "") => new()
    {
        Summary = summary,
        TlDr = [],
        Keywords = string.Empty,
        Entities = new ExtractedEntities(),
        RawResponse = rawResponse,
        ParsedSuccessfully = false
    };
}
