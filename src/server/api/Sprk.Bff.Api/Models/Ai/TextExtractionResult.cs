namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Result of text extraction from a document.
/// </summary>
public record TextExtractionResult
{
    /// <summary>
    /// The extracted text content. Null if extraction failed.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Whether extraction was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if extraction failed. Null on success.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// The extraction method used.
    /// </summary>
    public TextExtractionMethod Method { get; init; }

    /// <summary>
    /// Character count of extracted text. 0 if extraction failed.
    /// </summary>
    public int CharacterCount => Text?.Length ?? 0;

    /// <summary>
    /// Estimated token count (rough approximation: chars / 4).
    /// Used for checking against MaxInputTokens limit.
    /// </summary>
    public int EstimatedTokenCount => CharacterCount / 4;

    /// <summary>
    /// Create a successful extraction result.
    /// </summary>
    public static TextExtractionResult Succeeded(string text, TextExtractionMethod method) => new()
    {
        Text = text,
        Success = true,
        Method = method
    };

    /// <summary>
    /// Create a failed extraction result.
    /// </summary>
    public static TextExtractionResult Failed(string errorMessage, TextExtractionMethod method) => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
        Method = method
    };

    /// <summary>
    /// Create a result for unsupported file types.
    /// </summary>
    public static TextExtractionResult NotSupported(string extension) => new()
    {
        Success = false,
        ErrorMessage = $"File type '{extension}' is not supported for text extraction.",
        Method = TextExtractionMethod.NotSupported
    };

    /// <summary>
    /// Create a result for disabled file types.
    /// </summary>
    public static TextExtractionResult Disabled(string extension) => new()
    {
        Success = false,
        ErrorMessage = $"File type '{extension}' is currently disabled for summarization.",
        Method = TextExtractionMethod.Disabled
    };

    /// <summary>
    /// Create a result indicating the file requires vision model (direct image analysis).
    /// This is a successful result but with no text - the caller should use vision model.
    /// </summary>
    public static TextExtractionResult RequiresVision() => new()
    {
        Success = true, // Not an error - just a different processing path
        Text = null,
        Method = TextExtractionMethod.VisionOcr
    };

    /// <summary>
    /// Returns true if this file should be processed by vision model directly (images).
    /// </summary>
    public bool IsVisionRequired => Method == TextExtractionMethod.VisionOcr && Success && Text == null;
}

/// <summary>
/// Methods for extracting text from documents.
/// </summary>
public enum TextExtractionMethod
{
    /// <summary>
    /// Direct text read for plain text files (TXT, MD, JSON, CSV, etc.)
    /// </summary>
    Native,

    /// <summary>
    /// Azure Document Intelligence for PDFs and Office documents.
    /// </summary>
    DocumentIntelligence,

    /// <summary>
    /// Azure Vision / GPT-4 Vision for images.
    /// </summary>
    VisionOcr,

    /// <summary>
    /// File type not supported.
    /// </summary>
    NotSupported,

    /// <summary>
    /// File type exists but is disabled in configuration.
    /// </summary>
    Disabled
}
