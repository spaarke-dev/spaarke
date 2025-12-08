namespace Sprk.Bff.Api.Infrastructure.Exceptions;

/// <summary>
/// Exception for AI summarization failures that maps to RFC 7807 Problem Details.
/// Provides stable error codes for client-side handling and correlation IDs for tracing.
///
/// Stable error codes:
/// - ai_disabled: AI features are disabled via configuration
/// - file_not_found: Document not found in SharePoint Embedded
/// - file_download_failed: Failed to download file content from SPE
/// - extraction_failed: Text extraction failed (unsupported format, corrupt file, etc.)
/// - extraction_not_configured: Extraction method not available (e.g., DocIntel not configured)
/// - vision_not_configured: Vision model not configured for image files
/// - openai_error: General OpenAI API error
/// - openai_rate_limit: OpenAI rate limit exceeded (429)
/// - openai_timeout: OpenAI request timed out
/// - openai_content_filter: Content filtered by Azure OpenAI safety system
/// - file_too_large: File exceeds maximum size limit
/// - unsupported_file_type: File type not supported for summarization
/// </summary>
public sealed class SummarizationException : Exception
{
    /// <summary>
    /// Stable error code for client-side handling.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Error title (short description).
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Detailed error message for logging/debugging.
    /// </summary>
    public string? Detail { get; }

    /// <summary>
    /// HTTP status code (400, 404, 429, 500, 503, etc.)
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Correlation ID for request tracing.
    /// </summary>
    public string? CorrelationId { get; }

    /// <summary>
    /// Additional extensions (optional).
    /// </summary>
    public Dictionary<string, object>? Extensions { get; }

    public SummarizationException(
        string code,
        string title,
        string? detail = null,
        int statusCode = 500,
        string? correlationId = null,
        Dictionary<string, object>? extensions = null,
        Exception? innerException = null)
        : base($"{code}: {title}", innerException)
    {
        Code = code;
        Title = title;
        Detail = detail;
        StatusCode = statusCode;
        CorrelationId = correlationId;
        Extensions = extensions;
    }

    // Factory methods for common error scenarios

    /// <summary>
    /// AI features are disabled via configuration.
    /// </summary>
    public static SummarizationException AiDisabled(string? correlationId = null)
        => new("ai_disabled", "AI Unavailable",
            "AI summarization is currently disabled.",
            statusCode: 503, correlationId: correlationId);

    /// <summary>
    /// Document not found in SharePoint Embedded.
    /// </summary>
    public static SummarizationException FileNotFound(
        string driveId, string itemId, string? correlationId = null)
        => new("file_not_found", "Document Not Found",
            "The requested document was not found in SharePoint Embedded.",
            statusCode: 404, correlationId: correlationId,
            extensions: new Dictionary<string, object> { ["driveId"] = driveId, ["itemId"] = itemId });

    /// <summary>
    /// Failed to download file content from SPE.
    /// </summary>
    public static SummarizationException FileDownloadFailed(
        string driveId, string itemId, string? correlationId = null, Exception? inner = null)
        => new("file_download_failed", "Download Failed",
            "Failed to download the document content from storage.",
            statusCode: 502, correlationId: correlationId,
            extensions: new Dictionary<string, object> { ["driveId"] = driveId, ["itemId"] = itemId },
            innerException: inner);

    /// <summary>
    /// Text extraction failed (unsupported format, corrupt file, etc.)
    /// </summary>
    public static SummarizationException ExtractionFailed(
        string fileName, string reason, string? correlationId = null, Exception? inner = null)
        => new("extraction_failed", "Extraction Failed",
            $"Failed to extract text from '{fileName}': {reason}",
            statusCode: 422, correlationId: correlationId,
            extensions: new Dictionary<string, object> { ["fileName"] = fileName },
            innerException: inner);

    /// <summary>
    /// Extraction method not available (e.g., DocIntel not configured).
    /// </summary>
    public static SummarizationException ExtractionNotConfigured(
        string method, string? correlationId = null)
        => new("extraction_not_configured", "Extraction Unavailable",
            $"The {method} extraction service is not configured.",
            statusCode: 503, correlationId: correlationId);

    /// <summary>
    /// Vision model not configured for image files.
    /// </summary>
    public static SummarizationException VisionNotConfigured(string? correlationId = null)
        => new("vision_not_configured", "Vision Unavailable",
            "Image summarization requires a vision model which is not configured.",
            statusCode: 503, correlationId: correlationId);

    /// <summary>
    /// General OpenAI API error.
    /// </summary>
    public static SummarizationException OpenAiError(
        string message, string? correlationId = null, Exception? inner = null)
        => new("openai_error", "AI Service Error",
            $"The AI service encountered an error: {message}",
            statusCode: 502, correlationId: correlationId, innerException: inner);

    /// <summary>
    /// OpenAI rate limit exceeded (429).
    /// </summary>
    public static SummarizationException OpenAiRateLimit(
        int? retryAfterSeconds = null, string? correlationId = null)
        => new("openai_rate_limit", "Rate Limit Exceeded",
            "The AI service is currently overloaded. Please try again later.",
            statusCode: 429, correlationId: correlationId,
            extensions: retryAfterSeconds.HasValue
                ? new Dictionary<string, object> { ["retryAfterSeconds"] = retryAfterSeconds.Value }
                : null);

    /// <summary>
    /// OpenAI request timed out.
    /// </summary>
    public static SummarizationException OpenAiTimeout(string? correlationId = null, Exception? inner = null)
        => new("openai_timeout", "Request Timeout",
            "The AI service request timed out. Please try again.",
            statusCode: 504, correlationId: correlationId, innerException: inner);

    /// <summary>
    /// Content filtered by Azure OpenAI safety system.
    /// </summary>
    public static SummarizationException ContentFiltered(string? correlationId = null)
        => new("openai_content_filter", "Content Blocked",
            "The document content was blocked by the content safety system.",
            statusCode: 422, correlationId: correlationId);

    /// <summary>
    /// File exceeds maximum size limit.
    /// </summary>
    public static SummarizationException FileTooLarge(
        long fileSize, long maxSize, string? correlationId = null)
        => new("file_too_large", "File Too Large",
            $"File size ({fileSize / 1024 / 1024}MB) exceeds maximum allowed ({maxSize / 1024 / 1024}MB).",
            statusCode: 413, correlationId: correlationId,
            extensions: new Dictionary<string, object> { ["fileSize"] = fileSize, ["maxSize"] = maxSize });

    /// <summary>
    /// File type not supported for summarization.
    /// </summary>
    public static SummarizationException UnsupportedFileType(
        string extension, string? correlationId = null)
        => new("unsupported_file_type", "Unsupported File Type",
            $"The file type '{extension}' is not supported for summarization.",
            statusCode: 415, correlationId: correlationId,
            extensions: new Dictionary<string, object> { ["extension"] = extension });

    /// <summary>
    /// Circuit breaker is open - service temporarily unavailable (Task 072).
    /// </summary>
    public static SummarizationException CircuitBreakerOpen(
        int retryAfterSeconds, string? correlationId = null)
        => new("ai_circuit_open", "Service Temporarily Unavailable",
            "The AI service is temporarily unavailable due to repeated failures. Please try again later.",
            statusCode: 503, correlationId: correlationId,
            extensions: new Dictionary<string, object> { ["retryAfterSeconds"] = retryAfterSeconds });
}
