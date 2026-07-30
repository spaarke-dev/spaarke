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
    /// Structured email metadata. Only populated for email files (.eml, .msg).
    /// Contains sender, recipients, subject, date, body, and attachments.
    /// </summary>
    public EmailMetadata? EmailMetadata { get; init; }

    /// <summary>
    /// Per-page extracted text, preserving the page structure Azure Document Intelligence returns.
    /// Populated ONLY on the <see cref="TextExtractionMethod.DocumentIntelligence"/> path (PDFs / Office
    /// docs); <c>null</c> for Native / Email / unsupported paths (single-blob sources have no page structure).
    /// ADDITIVE (email-communication-intelligence-r1 task 041 / FR-13): existing callers read
    /// <see cref="Text"/> and are byte-unchanged; this parallel representation lets the attachment-grounded
    /// action extractor CODE-DERIVE a machine-verified page locator (locate the verbatim cited span in a
    /// specific page's text) rather than trust a model-asserted page. The Redis extraction cache stores
    /// <see cref="Text"/> only, so a cache HIT yields <c>null</c> Pages — the FR-13 attachment path uses the
    /// direct (non-cached) <c>ExtractAsync</c> entry, so it always sees populated Pages on a live extraction.
    /// </summary>
    public IReadOnlyList<ExtractedPage>? Pages { get; init; }

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
    /// Returns true if this result contains email metadata.
    /// </summary>
    public bool IsEmail => Method == TextExtractionMethod.Email && EmailMetadata != null;

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
    /// Create a successful email extraction result with structured metadata.
    /// </summary>
    /// <param name="text">The formatted email text for AI analysis.</param>
    /// <param name="emailMetadata">Structured email metadata (from, to, subject, etc.).</param>
    public static TextExtractionResult SucceededWithEmail(string text, EmailMetadata emailMetadata) => new()
    {
        Text = text,
        Success = true,
        Method = TextExtractionMethod.Email,
        EmailMetadata = emailMetadata
    };

    /// <summary>
    /// Returns true if this file should be processed by vision model directly (images).
    /// </summary>
    public bool IsVisionRequired => Method == TextExtractionMethod.VisionOcr && Success && Text == null;
}

/// <summary>
/// One page of extracted document text, preserving the page boundary Azure Document Intelligence reports.
/// ADDITIVE (task 041 / FR-13) — used only for code-derived machine-verified attachment-locator citation.
/// </summary>
/// <param name="PageNumber">1-based page number as reported by the extractor.</param>
/// <param name="Text">Plain text of this page (line contents joined). May be empty for a blank page.</param>
public sealed record ExtractedPage(int PageNumber, string Text);

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
    /// Email parsing for .eml and .msg files.
    /// </summary>
    Email,

    /// <summary>
    /// File type not supported.
    /// </summary>
    NotSupported,

    /// <summary>
    /// File type exists but is disabled in configuration.
    /// </summary>
    Disabled
}
