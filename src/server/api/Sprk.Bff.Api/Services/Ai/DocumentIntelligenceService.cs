using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Document parsing service backed by Azure Document Intelligence (ADI).
/// Acts as the primary — and fallback — parser within <see cref="DocumentParserRouter"/>.
/// </summary>
/// <remarks>
/// <para>
/// This service wraps the lower-level <see cref="ITextExtractor"/> (which calls the Azure
/// Document Intelligence REST API) and projects its output into the unified
/// <see cref="ParsedDocument"/> contract consumed by the RAG pipeline.
/// </para>
/// <para>
/// <b>Usage guidance</b>: callers that require intelligent parser routing (LlamaParse for
/// complex documents, Azure Doc Intel for standard ones) should inject and call
/// <see cref="DocumentParserRouter.ParseDocumentAsync"/> instead of this service directly.
/// This service is intentionally kept as the Azure Doc Intel delegate within the router
/// to avoid circular dependencies.
/// </para>
/// <para>
/// ADR-007 constraint: all document bytes arrive here from the caller (SpeFileStore facade);
/// this service never fetches content from storage directly.
/// </para>
/// </remarks>
public class DocumentIntelligenceService
{
    private readonly ITextExtractor _textExtractor;
    private readonly ILogger<DocumentIntelligenceService> _logger;

    /// <summary>
    /// Initialises a new <see cref="DocumentIntelligenceService"/>.
    /// </summary>
    /// <param name="textExtractor">Azure Document Intelligence-backed extractor.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public DocumentIntelligenceService(
        ITextExtractor textExtractor,
        ILogger<DocumentIntelligenceService> logger)
    {
        _textExtractor = textExtractor ?? throw new ArgumentNullException(nameof(textExtractor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Parses a document using Azure Document Intelligence and returns a
    /// <see cref="ParsedDocument"/> with the extracted text, page count, and tables.
    /// </summary>
    /// <param name="content">Raw document bytes (PDF, DOCX, …).</param>
    /// <param name="fileName">File name used to determine the extraction method.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ParsedDocument"/> on success.
    /// Throws <see cref="InvalidOperationException"/> when extraction fails.
    /// </returns>
    /// <remarks>
    /// This overload is called by <see cref="DocumentParserRouter"/> as the primary / fallback
    /// parse path. For all new callers outside the router, prefer
    /// <see cref="ParseDocumentAsync(byte[], string, string, CancellationToken)"/> which
    /// accepts a MIME type and enables future routing decisions.
    /// </remarks>
    public virtual async Task<ParsedDocument> ParseDocumentAsync(
        byte[] content,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        _logger.LogDebug(
            "DocumentIntelligenceService: parsing {FileName} ({Bytes} bytes)",
            fileName, content.Length);

        using var stream = new MemoryStream(content, writable: false);
        var result = await _textExtractor.ExtractAsync(stream, fileName, cancellationToken);

        if (!result.Success)
        {
            var errorMessage = result.ErrorMessage ?? "Text extraction failed without a message.";
            _logger.LogWarning(
                "DocumentIntelligenceService: extraction failed for {FileName}: {Error}",
                fileName, errorMessage);
            throw new InvalidOperationException(
                $"Azure Document Intelligence failed to parse '{fileName}': {errorMessage}");
        }

        _logger.LogDebug(
            "DocumentIntelligenceService: successfully parsed {FileName} ({Chars} chars, parser=DocumentIntelligence)",
            fileName, result.CharacterCount);

        return new ParsedDocument
        {
            Text = result.Text ?? string.Empty,
            Pages = 0,           // ITextExtractor does not expose a page count today;
                                 // this will be populated when DocumentIntelligence Layout
                                 // model support is added (spec FR-A02).
            Tables = Array.Empty<IReadOnlyList<IReadOnlyList<string>>>(),
            ExtractedAt = DateTimeOffset.UtcNow,
            ParserUsed = DocumentParser.DocumentIntelligence
        };
    }

    /// <summary>
    /// Parses a document using Azure Document Intelligence.
    /// This overload accepts a MIME type for future routing compatibility and delegates
    /// to <see cref="ParseDocumentAsync(byte[], string, CancellationToken)"/>.
    /// </summary>
    /// <param name="content">Raw document bytes.</param>
    /// <param name="fileName">File name including extension.</param>
    /// <param name="mimeType">MIME type of the document (used for telemetry; routing is handled by DocumentParserRouter).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="ParsedDocument"/> on success.</returns>
    public virtual Task<ParsedDocument> ParseDocumentAsync(
        byte[] content,
        string fileName,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        // For router-enabled callers that need to pass MIME type context,
        // this delegates to DocumentParserRouter. However, to avoid circular
        // dependencies, callers that have DocumentParserRouter injected should
        // call DocumentParserRouter.ParseDocumentAsync directly.
        // This overload exists so that services can standardise on a three-arg
        // signature and migrate transparently.
        _logger.LogDebug(
            "DocumentIntelligenceService: ParseDocumentAsync(content, fileName, mimeType) called for {FileName} " +
            "(mimeType={MimeType}). Callers that need router-based selection should inject DocumentParserRouter.",
            fileName, mimeType);

        return ParseDocumentAsync(content, fileName, cancellationToken);
    }
}
