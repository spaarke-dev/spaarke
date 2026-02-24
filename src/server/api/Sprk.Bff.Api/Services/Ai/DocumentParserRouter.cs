using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Routes document-parsing requests to the optimal parser based on document characteristics
/// and feature-flag configuration.
/// </summary>
/// <remarks>
/// Parser selection logic (evaluated in order):
///
/// 1. If <see cref="LlamaParseOptions.Enabled"/> is <c>false</c> (default) →
///    always use <see cref="DocumentIntelligenceService"/> (Azure Document Intelligence).
///
/// 2. If the document is "complex" (page count &gt; 30 or the MIME type / file extension
///    indicates a scanned image-based PDF) → prefer <see cref="LlamaParseClient"/> (LlamaParse).
///
/// 3. Otherwise → use <see cref="DocumentIntelligenceService"/> (Azure Document Intelligence).
///
/// 4. If <see cref="LlamaParseClient"/> throws for any reason → log a warning and
///    fall back silently to <see cref="DocumentIntelligenceService"/>.
///
/// LlamaParse is an ENHANCEMENT — the system must work perfectly without it.
///
/// ADR-007: document bytes arrive from SpeFileStore via the caller; this service never
/// fetches content from storage directly.
/// ADR-010: registered as concrete singleton; no unnecessary interface layer.
/// </remarks>
public class DocumentParserRouter
{
    private readonly DocumentIntelligenceService _docIntelService;
    private readonly LlamaParseClient _llamaParseClient;
    private readonly LlamaParseOptions _options;
    private readonly ILogger<DocumentParserRouter> _logger;

    // Documents above this page-count threshold are considered "complex" and routed to LlamaParse
    // when LlamaParse is enabled.
    private const int LargeDocumentPageThreshold = 30;

    // MIME types that indicate scanned/image-heavy documents which benefit from LlamaParse.
    private static readonly HashSet<string> ScannedDocumentMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/tiff", "image/tif", "image/png", "image/jpeg", "image/jpg", "image/bmp"
    };

    // File extensions that typically indicate scanned image-based PDFs or image files.
    private static readonly HashSet<string> ScannedDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tif", ".tiff", ".bmp"
    };

    /// <summary>
    /// Initialises a new <see cref="DocumentParserRouter"/>.
    /// </summary>
    /// <param name="docIntelService">Azure Document Intelligence parser (primary / fallback).</param>
    /// <param name="llamaParseClient">LlamaParse API client (optional enhancement).</param>
    /// <param name="options">LlamaParse feature flag and configuration options.</param>
    /// <param name="logger">Logger for routing decisions and fallback warnings.</param>
    public DocumentParserRouter(
        DocumentIntelligenceService docIntelService,
        LlamaParseClient llamaParseClient,
        IOptions<LlamaParseOptions> options,
        ILogger<DocumentParserRouter> logger)
    {
        _docIntelService = docIntelService ?? throw new ArgumentNullException(nameof(docIntelService));
        _llamaParseClient = llamaParseClient ?? throw new ArgumentNullException(nameof(llamaParseClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Parses a document using the optimal parser for the given content, routing to LlamaParse
    /// for complex documents when enabled, and falling back to Azure Document Intelligence
    /// on any LlamaParse failure.
    /// </summary>
    /// <param name="content">Raw document bytes (must not be null or empty).</param>
    /// <param name="fileName">File name including extension.</param>
    /// <param name="mimeType">
    /// MIME type of the document. Used alongside <paramref name="fileName"/> to detect
    /// scanned/image-based documents.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="ParsedDocument"/> produced by the selected parser.</returns>
    public async Task<ParsedDocument> ParseDocumentAsync(
        byte[] content,
        string fileName,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        // Rule 1: LlamaParse disabled — always use Azure Document Intelligence.
        if (!_options.Enabled)
        {
            _logger.LogDebug(
                "DocumentParserRouter: LlamaParse disabled; routing {FileName} to DocumentIntelligenceService",
                fileName);
            return await _docIntelService.ParseDocumentAsync(content, fileName, cancellationToken);
        }

        // Rule 2 / 3: LlamaParse is enabled — decide based on document characteristics.
        var preferLlamaParse = IsComplexDocument(content, fileName, mimeType);

        if (!preferLlamaParse)
        {
            _logger.LogDebug(
                "DocumentParserRouter: {FileName} does not meet complexity threshold; routing to DocumentIntelligenceService",
                fileName);
            return await _docIntelService.ParseDocumentAsync(content, fileName, cancellationToken);
        }

        // Rule 4: attempt LlamaParse with silent fallback on failure.
        _logger.LogDebug(
            "DocumentParserRouter: {FileName} meets complexity threshold; routing to LlamaParse",
            fileName);

        try
        {
            return await _llamaParseClient.ParseDocumentAsync(content, fileName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "DocumentParserRouter: LlamaParse failed for {FileName}; falling back to DocumentIntelligenceService",
                fileName);

            return await _docIntelService.ParseDocumentAsync(content, fileName, cancellationToken);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns <c>true</c> when the document is considered "complex" and should be routed
    /// to LlamaParse (when it is enabled).
    ///
    /// Complexity heuristics (any match → complex):
    /// - MIME type indicates a scanned / image-based document.
    /// - File extension indicates a scanned / image-based document.
    /// - Estimated page count exceeds <see cref="LargeDocumentPageThreshold"/> (30).
    /// </summary>
    private bool IsComplexDocument(byte[] content, string fileName, string mimeType)
    {
        // Check MIME type.
        if (!string.IsNullOrWhiteSpace(mimeType) && ScannedDocumentMimeTypes.Contains(mimeType))
        {
            _logger.LogDebug(
                "DocumentParserRouter: {FileName} identified as scanned document via MIME type '{MimeType}'",
                fileName, mimeType);
            return true;
        }

        // Check file extension.
        var ext = Path.GetExtension(fileName);
        if (!string.IsNullOrWhiteSpace(ext) && ScannedDocumentExtensions.Contains(ext))
        {
            _logger.LogDebug(
                "DocumentParserRouter: {FileName} identified as scanned document via extension '{Ext}'",
                fileName, ext);
            return true;
        }

        // Estimate page count for PDFs using a simple heuristic:
        // count the number of "/Page " markers in the file header region.
        // This is intentionally approximate to avoid loading the full PDF SDK here;
        // the actual page count is reported by the parser after extraction.
        var estimatedPages = EstimatePageCount(content, fileName);
        if (estimatedPages > LargeDocumentPageThreshold)
        {
            _logger.LogDebug(
                "DocumentParserRouter: {FileName} estimated at {Pages} pages (threshold {Threshold}); routing to LlamaParse",
                fileName, estimatedPages, LargeDocumentPageThreshold);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Quickly estimates the number of pages in a document without a full parse.
    /// For PDFs this counts occurrences of the <c>/Type /Page</c> dictionary entry.
    /// Returns 0 for non-PDF formats (page count not estimable without a full parse).
    /// </summary>
    private static int EstimatePageCount(byte[] content, string fileName)
    {
        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
        if (ext != ".pdf")
        {
            return 0;
        }

        // Scan the raw bytes for the PDF page-object marker.
        // This is a fast O(n) scan that works for standard PDFs.
        // Cross-reference-compressed PDFs may under-count, but the estimate is
        // sufficient for the > 30 page routing heuristic.
        const string marker = "/Type /Page";
        var markerBytes = System.Text.Encoding.ASCII.GetBytes(marker);
        var count = 0;
        var searchFrom = 0;

        while (true)
        {
            var idx = IndexOf(content, markerBytes, searchFrom);
            if (idx < 0) break;
            count++;
            searchFrom = idx + markerBytes.Length;
        }

        return count;
    }

    /// <summary>
    /// Searches for <paramref name="pattern"/> in <paramref name="source"/> starting at
    /// <paramref name="startIndex"/> and returns the index of the first match, or -1.
    /// </summary>
    private static int IndexOf(byte[] source, byte[] pattern, int startIndex)
    {
        for (var i = startIndex; i <= source.Length - pattern.Length; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Length; j++)
            {
                if (source[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
    }
}
