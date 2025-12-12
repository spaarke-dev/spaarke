namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Result of a non-streaming document analysis operation.
/// </summary>
public record AnalysisResult
{
    /// <summary>
    /// The generated summary text. Null if analysis failed.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Whether analysis was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if analysis failed. Null on success.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Number of characters in the source document.
    /// </summary>
    public int SourceCharacterCount { get; init; }

    /// <summary>
    /// The text extraction method used.
    /// </summary>
    public TextExtractionMethod ExtractionMethod { get; init; }

    /// <summary>
    /// Structured analysis result when StructuredOutputEnabled is true.
    /// Contains TL;DR, keywords, and extracted entities.
    /// </summary>
    public DocumentAnalysisResult? StructuredResult { get; init; }

    /// <summary>
    /// Create a successful analysis result with raw summary only.
    /// </summary>
    public static AnalysisResult Succeeded(
        string summary,
        int sourceCharacterCount,
        TextExtractionMethod extractionMethod) => new()
        {
            Summary = summary,
            Success = true,
            SourceCharacterCount = sourceCharacterCount,
            ExtractionMethod = extractionMethod
        };

    /// <summary>
    /// Create a successful analysis result with structured data.
    /// </summary>
    public static AnalysisResult SucceededWithStructuredResult(
        DocumentAnalysisResult structuredResult,
        int sourceCharacterCount,
        TextExtractionMethod extractionMethod) => new()
        {
            Summary = structuredResult.Summary,
            Success = true,
            SourceCharacterCount = sourceCharacterCount,
            ExtractionMethod = extractionMethod,
            StructuredResult = structuredResult
        };

    /// <summary>
    /// Create a failed analysis result.
    /// </summary>
    public static AnalysisResult Failed(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}
