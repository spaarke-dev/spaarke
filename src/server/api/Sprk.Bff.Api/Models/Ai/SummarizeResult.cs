namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Result of a non-streaming summarization operation.
/// </summary>
public record SummarizeResult
{
    /// <summary>
    /// The generated summary text. Null if summarization failed.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Whether summarization was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if summarization failed. Null on success.
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
    /// Create a successful summarization result.
    /// </summary>
    public static SummarizeResult Succeeded(
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
    /// Create a failed summarization result.
    /// </summary>
    public static SummarizeResult Failed(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}
