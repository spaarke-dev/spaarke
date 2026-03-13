using System.Diagnostics;
using System.Text;
using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using MimeKit;
using MsgReader.Outlook;
using Polly;
using Polly.CircuitBreaker;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Resilience;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Telemetry;
using ResilienceCircuitState = Sprk.Bff.Api.Infrastructure.Resilience.CircuitState;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Service for extracting text from documents.
/// Handles native text formats directly; PDF/DOCX require Document Intelligence (Task 060).
/// Document Intelligence calls are protected by a configurable timeout and Polly circuit breaker.
/// Supports Redis caching of extracted text with ETag-versioned keys (ADR-009).
/// </summary>
public class TextExtractorService : ITextExtractor
{
    private readonly DocumentIntelligenceOptions _options;
    private readonly ILogger<TextExtractorService> _logger;
    private readonly ICircuitBreakerRegistry? _circuitRegistry;
    private readonly IDistributedCache? _cache;
    private readonly CacheMetrics? _cacheMetrics;
    private readonly AsyncCircuitBreakerPolicy _docIntelCircuitBreaker;

    /// <summary>
    /// Cache key prefix for extracted document text.
    /// Full key format: sdap:ai:text:{driveId}:{itemId}:v{etag}
    /// </summary>
    private const string CacheKeyPrefix = "sdap:ai:text";

    /// <summary>
    /// Cache type identifier for metrics tracking.
    /// </summary>
    private const string CacheType = "text-extraction";

    /// <summary>
    /// Maximum extracted text size to cache (1 MB). Larger results are skipped to avoid
    /// Redis memory pressure. Documents exceeding this threshold are always re-extracted.
    /// </summary>
    private const int MaxCacheableTextBytes = 1_048_576;

    /// <summary>
    /// TTL for cached extracted text. 24 hours balances cache hit rate with freshness.
    /// ETag in the key ensures stale content is never served when documents change.
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    public TextExtractorService(
        IOptions<DocumentIntelligenceOptions> options,
        ILogger<TextExtractorService> logger,
        ICircuitBreakerRegistry? circuitRegistry = null,
        IDistributedCache? cache = null,
        CacheMetrics? cacheMetrics = null)
    {
        _options = options.Value;
        _logger = logger;
        _circuitRegistry = circuitRegistry;
        _cache = cache;
        _cacheMetrics = cacheMetrics;

        // Register circuit breaker for Document Intelligence
        _circuitRegistry?.RegisterCircuit(CircuitBreakerRegistry.DocumentIntelligence);

        // Build Polly circuit breaker: opens after N consecutive failures, breaks for M seconds
        _docIntelCircuitBreaker = Policy
            .Handle<Exception>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: _options.DocIntelCircuitBreakerThreshold,
                durationOfBreak: TimeSpan.FromSeconds(_options.DocIntelCircuitBreakerBreakSeconds),
                onBreak: (exception, duration) =>
                {
                    _logger.LogError(
                        "Document Intelligence circuit breaker OPENED after {Threshold} consecutive failures. Breaking for {Duration}s. Last error: {Error}",
                        _options.DocIntelCircuitBreakerThreshold,
                        duration.TotalSeconds,
                        exception.Message);
                    _circuitRegistry?.RecordStateChange(
                        CircuitBreakerRegistry.DocumentIntelligence,
                        ResilienceCircuitState.Open,
                        duration);
                },
                onReset: () =>
                {
                    _logger.LogInformation("Document Intelligence circuit breaker RESET - service recovered");
                    _circuitRegistry?.RecordStateChange(
                        CircuitBreakerRegistry.DocumentIntelligence,
                        ResilienceCircuitState.Closed);
                },
                onHalfOpen: () =>
                {
                    _logger.LogInformation("Document Intelligence circuit breaker HALF-OPEN - testing availability");
                    _circuitRegistry?.RecordStateChange(
                        CircuitBreakerRegistry.DocumentIntelligence,
                        ResilienceCircuitState.HalfOpen);
                });
    }

    /// <summary>
    /// Extract text from a file stream.
    /// </summary>
    /// <param name="fileStream">The file content stream.</param>
    /// <param name="fileName">The file name (used to determine extraction method).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Extraction result with text or error message.</returns>
    public async Task<TextExtractionResult> ExtractAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;

        _logger.LogDebug("Extracting text from file {FileName} (extension: {Extension})", fileName, extension);

        // Check if file type is supported
        if (!_options.SupportedFileTypes.TryGetValue(extension, out var fileTypeConfig))
        {
            _logger.LogWarning("File type {Extension} is not supported", extension);
            return TextExtractionResult.NotSupported(extension);
        }

        // Check if file type is enabled
        if (!fileTypeConfig.Enabled)
        {
            _logger.LogWarning("File type {Extension} is disabled", extension);
            return TextExtractionResult.Disabled(extension);
        }

        // Route to appropriate extraction method
        return fileTypeConfig.Method switch
        {
            ExtractionMethod.Native => await ExtractNativeAsync(fileStream, fileName, cancellationToken),
            ExtractionMethod.DocumentIntelligence => await ExtractViaDocIntelAsync(fileStream, fileName, cancellationToken),
            ExtractionMethod.VisionOcr => HandleVisionOcrFile(fileName),
            ExtractionMethod.Email => await ExtractEmailAsync(fileStream, fileName, cancellationToken),
            _ => TextExtractionResult.NotSupported(extension)
        };
    }

    /// <summary>
    /// Extract text from a file stream with Redis cache support (ADR-009).
    /// When driveId, itemId, and etag are provided, extracted text is cached in Redis
    /// with key <c>sdap:ai:text:{driveId}:{itemId}:v{etag}</c> and 24-hour TTL.
    /// Cache hit skips extraction entirely. ETag in key ensures auto-invalidation on document change.
    /// </summary>
    public async Task<TextExtractionResult> ExtractAsync(
        Stream fileStream,
        string fileName,
        string? driveId,
        string? itemId,
        string? etag,
        CancellationToken cancellationToken = default)
    {
        // If cache identifiers are incomplete, fall back to non-cached extraction
        if (string.IsNullOrEmpty(driveId) || string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(etag) || _cache == null)
        {
            _logger.LogDebug(
                "Cache identifiers incomplete or cache unavailable, extracting without cache (DriveId={DriveId}, ItemId={ItemId}, HasETag={HasETag})",
                driveId ?? "(null)", itemId ?? "(null)", !string.IsNullOrEmpty(etag));
            return await ExtractAsync(fileStream, fileName, cancellationToken);
        }

        // Sanitize ETag (remove surrounding quotes if present, common in HTTP headers)
        var sanitizedEtag = etag.Trim('"');
        var cacheKey = $"{CacheKeyPrefix}:{driveId}:{itemId}:v{sanitizedEtag}";

        // Try cache lookup first (cache-aside pattern per ADR-009)
        var cachedResult = await TryGetFromCacheAsync(cacheKey, cancellationToken);
        if (cachedResult != null)
        {
            return cachedResult;
        }

        // Cache miss — perform extraction
        var result = await ExtractAsync(fileStream, fileName, cancellationToken);

        // Cache successful results (skip failures, vision-required, and oversized text)
        if (result.Success && result.Text != null && !result.IsVisionRequired)
        {
            await TrySetInCacheAsync(cacheKey, result, cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Try to retrieve cached extraction result from Redis.
    /// Returns null on cache miss or error (graceful degradation).
    /// </summary>
    private async Task<TextExtractionResult?> TryGetFromCacheAsync(
        string cacheKey,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var cachedText = await _cache!.GetStringAsync(cacheKey, cancellationToken);
            sw.Stop();

            if (cachedText != null)
            {
                _logger.LogDebug(
                    "Text extraction cache HIT for key {CacheKey} ({CharCount} chars, {LatencyMs:F1}ms)",
                    cacheKey, cachedText.Length, sw.Elapsed.TotalMilliseconds);
                _cacheMetrics?.RecordHit(sw.Elapsed.TotalMilliseconds, CacheType);

                // Reconstruct a successful result from cached text.
                // We don't cache EmailMetadata — email extraction is fast and metadata is rarely reused.
                return TextExtractionResult.Succeeded(cachedText, TextExtractionMethod.Native);
            }

            _logger.LogDebug(
                "Text extraction cache MISS for key {CacheKey} ({LatencyMs:F1}ms)",
                cacheKey, sw.Elapsed.TotalMilliseconds);
            _cacheMetrics?.RecordMiss(sw.Elapsed.TotalMilliseconds, CacheType);
            return null;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "Error reading text extraction cache for key {CacheKey}, proceeding with extraction",
                cacheKey);
            _cacheMetrics?.RecordMiss(sw.Elapsed.TotalMilliseconds, CacheType);
            return null; // Graceful degradation — cache failure should not block extraction
        }
    }

    /// <summary>
    /// Try to store extraction result in Redis cache.
    /// Skips documents with extracted text exceeding 1 MB to avoid Redis memory pressure.
    /// Errors are logged but do not propagate (caching is optimization, not requirement).
    /// </summary>
    private async Task TrySetInCacheAsync(
        string cacheKey,
        TextExtractionResult result,
        CancellationToken cancellationToken)
    {
        if (result.Text == null) return;

        // Skip caching for large documents (> 1 MB) to avoid Redis memory pressure
        var textByteSize = Encoding.UTF8.GetByteCount(result.Text);
        if (textByteSize > MaxCacheableTextBytes)
        {
            _logger.LogDebug(
                "Skipping cache for key {CacheKey}: text size {SizeKB:F0}KB exceeds {MaxKB}KB limit",
                cacheKey, textByteSize / 1024.0, MaxCacheableTextBytes / 1024);
            return;
        }

        try
        {
            await _cache!.SetStringAsync(
                cacheKey,
                result.Text,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheTtl
                },
                cancellationToken);

            _logger.LogDebug(
                "Cached extracted text for key {CacheKey} ({CharCount} chars, {SizeKB:F0}KB, TTL={TtlHours}h)",
                cacheKey, result.Text.Length, textByteSize / 1024.0, CacheTtl.TotalHours);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Error caching extracted text for key {CacheKey}, extraction result still returned",
                cacheKey);
            // Don't throw — caching is optimization, not requirement
        }
    }

    /// <summary>
    /// Handle image files that require vision model processing.
    /// Returns a special result indicating the file should be processed directly by vision model.
    /// </summary>
    private TextExtractionResult HandleVisionOcrFile(string fileName)
    {
        // Check if vision model is configured
        if (string.IsNullOrEmpty(_options.ImageSummarizeModel))
        {
            _logger.LogWarning(
                "Vision model not configured. Cannot process image file {FileName}. " +
                "Set Ai:ImageSummarizeModel in configuration.",
                fileName);
            return TextExtractionResult.Failed(
                "Vision model is not configured. Image summarization is unavailable.",
                TextExtractionMethod.VisionOcr);
        }

        _logger.LogDebug(
            "Image file {FileName} will be processed by vision model {Model}",
            fileName, _options.ImageSummarizeModel);

        // Return special result indicating vision processing is required
        return TextExtractionResult.RequiresVision();
    }

    /// <summary>
    /// Extract text from email files (.eml and .msg formats).
    /// Uses MimeKit for .eml (MIME) and MsgReader for .msg (Outlook) formats.
    /// Returns both formatted text for AI and structured metadata for Dataverse.
    /// </summary>
    private async Task<TextExtractionResult> ExtractEmailAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;

        try
        {
            // Check file size
            if (fileStream.CanSeek && fileStream.Length > _options.MaxFileSizeBytes)
            {
                var sizeMb = fileStream.Length / (1024.0 * 1024.0);
                var maxMb = _options.MaxFileSizeBytes / (1024.0 * 1024.0);
                return TextExtractionResult.Failed(
                    $"File size ({sizeMb:F1}MB) exceeds maximum allowed ({maxMb:F1}MB).",
                    TextExtractionMethod.Email);
            }

            string text;
            EmailMetadata metadata;

            if (extension == ".eml")
            {
                (text, metadata) = await ExtractFromEmlAsync(fileStream, cancellationToken);
            }
            else if (extension == ".msg")
            {
                (text, metadata) = ExtractFromMsg(fileStream);
            }
            else
            {
                return TextExtractionResult.Failed(
                    $"Unsupported email format: {extension}",
                    TextExtractionMethod.Email);
            }

            // Check for empty content
            if (string.IsNullOrWhiteSpace(text))
            {
                return TextExtractionResult.Failed(
                    "Email is empty or contains no readable text.",
                    TextExtractionMethod.Email);
            }

            // Check estimated token count against limit
            var estimatedTokens = text.Length / 4;
            if (estimatedTokens > _options.MaxInputTokens)
            {
                _logger.LogWarning(
                    "Email {FileName} has ~{EstimatedTokens} tokens, exceeding limit of {MaxTokens}. Text will be truncated.",
                    fileName, estimatedTokens, _options.MaxInputTokens);

                var maxChars = _options.MaxInputTokens * 4;
                text = text[..Math.Min(text.Length, maxChars)];
                text += "\n\n[Content truncated due to size limits]";
            }

            // Truncate email body in metadata if needed (max 10K chars for Dataverse field)
            const int maxBodyChars = 10000;
            if (metadata.Body?.Length > maxBodyChars)
            {
                metadata.Body = metadata.Body[..maxBodyChars] + "\n\n[Content truncated]";
            }

            _logger.LogDebug(
                "Successfully extracted {CharCount} characters from email {FileName} ({AttachmentCount} attachments)",
                text.Length, fileName, metadata.Attachments.Count);

            return TextExtractionResult.SucceededWithEmail(text, metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract text from email {FileName}", fileName);
            return TextExtractionResult.Failed(
                $"Failed to extract email content: {ex.Message}",
                TextExtractionMethod.Email);
        }
    }

    /// <summary>
    /// Extract text and metadata from .eml (MIME) email file using MimeKit.
    /// </summary>
    private static async Task<(string Text, EmailMetadata Metadata)> ExtractFromEmlAsync(
        Stream fileStream,
        CancellationToken cancellationToken)
    {
        var message = await MimeMessage.LoadAsync(fileStream, cancellationToken);

        var body = message.TextBody ?? StripHtml(message.HtmlBody);

        // Extract attachments
        var attachments = new List<EmailAttachment>();
        foreach (var attachment in message.Attachments)
        {
            var mimeAttachment = attachment as MimePart;
            if (mimeAttachment != null)
            {
                attachments.Add(new EmailAttachment
                {
                    Filename = mimeAttachment.FileName ?? "unnamed",
                    MimeType = mimeAttachment.ContentType?.MimeType,
                    ContentId = mimeAttachment.ContentId,
                    IsInline = mimeAttachment.ContentDisposition?.Disposition == ContentDisposition.Inline
                });
            }
        }

        // Also check body parts for inline attachments
        if (message.Body is Multipart multipart)
        {
            ExtractAttachmentsFromMultipart(multipart, attachments);
        }

        var metadata = new EmailMetadata
        {
            Subject = message.Subject,
            From = message.From?.ToString(),
            To = message.To?.ToString(),
            Cc = message.Cc?.ToString(),
            Date = message.Date.LocalDateTime,
            Body = body,
            Attachments = attachments.DistinctBy(a => a.Filename).ToList()
        };

        var text = FormatEmailContent(
            metadata.Subject, metadata.From, metadata.To, metadata.Cc, metadata.Date, body);

        return (text, metadata);
    }

    /// <summary>
    /// Recursively extract attachments from multipart MIME structure.
    /// </summary>
    private static void ExtractAttachmentsFromMultipart(Multipart multipart, List<EmailAttachment> attachments)
    {
        foreach (var part in multipart)
        {
            if (part is Multipart nested)
            {
                ExtractAttachmentsFromMultipart(nested, attachments);
            }
            else if (part is MimePart mimePart)
            {
                // Skip text/plain and text/html body parts
                if (mimePart.ContentType.MimeType == "text/plain" ||
                    mimePart.ContentType.MimeType == "text/html")
                {
                    if (mimePart.ContentDisposition?.Disposition != ContentDisposition.Attachment)
                        continue;
                }

                var attachment = new EmailAttachment
                {
                    Filename = mimePart.FileName ?? mimePart.ContentId ?? "unnamed",
                    MimeType = mimePart.ContentType?.MimeType,
                    ContentId = mimePart.ContentId,
                    IsInline = mimePart.ContentDisposition?.Disposition == ContentDisposition.Inline,
                    SizeBytes = 0 // Size unknown at extraction time
                };

                attachments.Add(attachment);
            }
        }
    }

    /// <summary>
    /// Extract text and metadata from .msg (Outlook) email file using MsgReader.
    /// </summary>
    private static (string Text, EmailMetadata Metadata) ExtractFromMsg(Stream fileStream)
    {
        using var msg = new Storage.Message(fileStream);

        // Format all recipients
        string? recipients = null;
        if (msg.Recipients != null && msg.Recipients.Count > 0)
        {
            var recipientList = msg.Recipients
                .Select(r => r.Email ?? r.DisplayName)
                .Where(s => !string.IsNullOrEmpty(s));
            recipients = string.Join(", ", recipientList);
        }

        var body = msg.BodyText ?? StripHtml(msg.BodyHtml);

        // Extract attachments from MSG
        var attachments = new List<EmailAttachment>();
        if (msg.Attachments != null)
        {
            foreach (var attachment in msg.Attachments)
            {
                if (attachment is Storage.Attachment msgAttachment)
                {
                    attachments.Add(new EmailAttachment
                    {
                        Filename = msgAttachment.FileName ?? "unnamed",
                        MimeType = msgAttachment.MimeType,
                        SizeBytes = msgAttachment.Data?.Length ?? 0,
                        ContentId = msgAttachment.ContentId,
                        IsInline = msgAttachment.IsInline
                    });
                }
                else if (attachment is Storage.Message embeddedMsg)
                {
                    // Embedded email as attachment
                    attachments.Add(new EmailAttachment
                    {
                        Filename = embeddedMsg.Subject ?? "embedded-email.msg",
                        MimeType = "message/rfc822",
                        IsInline = false
                    });
                }
            }
        }

        var metadata = new EmailMetadata
        {
            Subject = msg.Subject,
            From = msg.Sender?.Email ?? msg.Sender?.DisplayName,
            To = recipients,
            Cc = null, // MsgReader Recipients includes all; To/CC not easily distinguishable
            Date = msg.SentOn?.DateTime, // MsgReader 6.x returns DateTimeOffset?
            Body = body,
            Attachments = attachments
        };

        var text = FormatEmailContent(
            metadata.Subject, metadata.From, metadata.To, metadata.Cc, metadata.Date, body);

        return (text, metadata);
    }

    /// <summary>
    /// Format email content into structured text for AI analysis.
    /// </summary>
    private static string FormatEmailContent(
        string? subject,
        string? from,
        string? to,
        string? cc,
        DateTime? date,
        string? body)
    {
        var sb = new StringBuilder();

        sb.AppendLine("=== EMAIL MESSAGE ===");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(subject))
            sb.AppendLine($"Subject: {subject}");

        if (!string.IsNullOrEmpty(from))
            sb.AppendLine($"From: {from}");

        if (!string.IsNullOrEmpty(to))
            sb.AppendLine($"To: {to}");

        if (!string.IsNullOrEmpty(cc))
            sb.AppendLine($"CC: {cc}");

        if (date.HasValue)
            sb.AppendLine($"Date: {date.Value:yyyy-MM-dd HH:mm}");

        sb.AppendLine();
        sb.AppendLine("=== EMAIL BODY ===");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(body))
        {
            sb.Append(body.Trim());
        }
        else
        {
            sb.AppendLine("[No body content]");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Strip HTML tags from content to extract plain text.
    /// Simple implementation for email HTML bodies.
    /// </summary>
    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return null;

        // Remove script and style blocks
        var text = System.Text.RegularExpressions.Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", "", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Replace <br>, <p>, <div> with newlines
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<br\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"</?(p|div)[^>]*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Remove remaining HTML tags
        text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>", "");

        // Decode HTML entities
        text = System.Net.WebUtility.HtmlDecode(text);

        // Clean up excessive whitespace
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"[ \t]+", " ");

        return text.Trim();
    }

    /// <summary>
    /// Extract text from native text files (TXT, MD, JSON, CSV, XML, HTML).
    /// Uses encoding detection to handle UTF-8, UTF-16 with BOM.
    /// </summary>
    private async Task<TextExtractionResult> ExtractNativeAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check file size
            if (fileStream.CanSeek && fileStream.Length > _options.MaxFileSizeBytes)
            {
                var sizeMb = fileStream.Length / (1024.0 * 1024.0);
                var maxMb = _options.MaxFileSizeBytes / (1024.0 * 1024.0);
                return TextExtractionResult.Failed(
                    $"File size ({sizeMb:F1}MB) exceeds maximum allowed ({maxMb:F1}MB).",
                    TextExtractionMethod.Native);
            }

            // Read with encoding detection (handles BOM for UTF-8, UTF-16 LE/BE)
            using var reader = new StreamReader(
                fileStream,
                encoding: Encoding.UTF8, // Default fallback
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: true);

            var text = await reader.ReadToEndAsync(cancellationToken);

            // Check for empty content
            if (string.IsNullOrWhiteSpace(text))
            {
                return TextExtractionResult.Failed(
                    "File is empty or contains only whitespace.",
                    TextExtractionMethod.Native);
            }

            // Check estimated token count against limit
            var estimatedTokens = text.Length / 4;
            if (estimatedTokens > _options.MaxInputTokens)
            {
                _logger.LogWarning(
                    "File {FileName} has ~{EstimatedTokens} tokens, exceeding limit of {MaxTokens}. Text will be truncated.",
                    fileName, estimatedTokens, _options.MaxInputTokens);

                // Truncate to approximately MaxInputTokens
                var maxChars = _options.MaxInputTokens * 4;
                text = text[..Math.Min(text.Length, maxChars)];
                text += "\n\n[Content truncated due to size limits]";
            }

            _logger.LogDebug(
                "Successfully extracted {CharCount} characters from {FileName}",
                text.Length, fileName);

            return TextExtractionResult.Succeeded(text, TextExtractionMethod.Native);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract text from {FileName}", fileName);
            return TextExtractionResult.Failed(
                $"Failed to extract text: {ex.Message}",
                TextExtractionMethod.Native);
        }
    }

    /// <summary>
    /// Extract text from PDF/DOCX files using Azure Document Intelligence.
    /// Uses the prebuilt-read model for general document text extraction.
    /// Protected by a configurable timeout (default 30s) and Polly circuit breaker.
    /// </summary>
    private async Task<TextExtractionResult> ExtractViaDocIntelAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken)
    {
        // Check if Document Intelligence is configured
        if (string.IsNullOrEmpty(_options.DocIntelEndpoint) || string.IsNullOrEmpty(_options.DocIntelKey))
        {
            _logger.LogWarning(
                "Document Intelligence not configured. Cannot extract text from {FileName}. " +
                "Set Ai:DocIntelEndpoint and Ai:DocIntelKey in configuration.",
                fileName);
            return TextExtractionResult.Failed(
                "Document Intelligence is not configured. PDF/DOCX extraction is unavailable.",
                TextExtractionMethod.DocumentIntelligence);
        }

        // Check circuit breaker state before making the call
        if (_docIntelCircuitBreaker.CircuitState == Polly.CircuitBreaker.CircuitState.Open)
        {
            _logger.LogWarning(
                "Document Intelligence circuit breaker is OPEN. Skipping extraction for {FileName}",
                fileName);
            return TextExtractionResult.Failed(
                "Document text extraction is temporarily unavailable due to repeated service failures. Please try again in a few minutes.",
                TextExtractionMethod.DocumentIntelligence);
        }

        try
        {
            // Execute within circuit breaker policy
            return await _docIntelCircuitBreaker.ExecuteAsync(async (ct) =>
            {
                return await ExtractViaDocIntelCoreAsync(fileStream, fileName, ct);
            }, cancellationToken);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning(
                "Document Intelligence circuit breaker rejected request for {FileName}",
                fileName);
            return TextExtractionResult.Failed(
                "Document text extraction is temporarily unavailable due to repeated service failures. Please try again in a few minutes.",
                TextExtractionMethod.DocumentIntelligence);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout from linked token, not user cancellation
            _logger.LogWarning(
                "Document Intelligence extraction timed out after {Timeout}s for {FileName}",
                _options.DocIntelTimeoutSeconds, fileName);
            _circuitRegistry?.RecordFailure(CircuitBreakerRegistry.DocumentIntelligence);
            return TextExtractionResult.Failed(
                $"Document text extraction took too long (exceeded {_options.DocIntelTimeoutSeconds}s timeout). The document may be very large or complex. Please try again later.",
                TextExtractionMethod.DocumentIntelligence);
        }
    }

    /// <summary>
    /// Core Document Intelligence extraction logic, called within circuit breaker and timeout protection.
    /// </summary>
    private async Task<TextExtractionResult> ExtractViaDocIntelCoreAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check file size before processing
            if (fileStream.CanSeek && fileStream.Length > _options.MaxFileSizeBytes)
            {
                var sizeMb = fileStream.Length / (1024.0 * 1024.0);
                var maxMb = _options.MaxFileSizeBytes / (1024.0 * 1024.0);
                return TextExtractionResult.Failed(
                    $"File size ({sizeMb:F1}MB) exceeds maximum allowed ({maxMb:F1}MB).",
                    TextExtractionMethod.DocumentIntelligence);
            }

            _logger.LogDebug(
                "Starting Document Intelligence extraction for {FileName} (timeout: {Timeout}s)",
                fileName, _options.DocIntelTimeoutSeconds);

            // Create client
            var credential = new AzureKeyCredential(_options.DocIntelKey!);
            var client = new DocumentIntelligenceClient(new Uri(_options.DocIntelEndpoint!), credential);

            // Read stream to BinaryData (required by SDK)
            using var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream, cancellationToken);
            var binaryData = BinaryData.FromBytes(memoryStream.ToArray());

            // Create linked CancellationToken with configurable timeout
            using var timeoutCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(_options.DocIntelTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeoutCts.Token);

            // Analyze document using prebuilt-read model with timeout-protected token
            var operation = await client.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                "prebuilt-read",
                binaryData,
                cancellationToken: linkedCts.Token);

            var result = operation.Value;

            // Record success with circuit breaker registry
            _circuitRegistry?.RecordSuccess(CircuitBreakerRegistry.DocumentIntelligence);

            // Extract text - prefer Content (for native DOCX/digital PDFs) over Pages/Lines (for scanned docs)
            string text;
            if (!string.IsNullOrWhiteSpace(result.Content))
            {
                // Native digital documents (DOCX, digital PDFs) have text in Content property
                text = result.Content.Trim();
                _logger.LogDebug(
                    "Extracted {CharCount} chars from {FileName} using Content property",
                    text.Length, fileName);
            }
            else
            {
                // Scanned documents have OCR text in Pages/Lines
                var textBuilder = new StringBuilder();
                foreach (var page in result.Pages)
                {
                    foreach (var line in page.Lines)
                    {
                        textBuilder.AppendLine(line.Content);
                    }
                    // Add page break between pages
                    if (result.Pages.Count > 1)
                    {
                        textBuilder.AppendLine();
                    }
                }
                text = textBuilder.ToString().Trim();
                _logger.LogDebug(
                    "Extracted {CharCount} chars from {FileName} using Pages/Lines ({PageCount} pages)",
                    text.Length, fileName, result.Pages.Count);
            }

            // Check for empty content
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("Document Intelligence returned no text for {FileName}", fileName);
                return TextExtractionResult.Failed(
                    "No text could be extracted from the document. The file may be empty, image-only, or corrupted.",
                    TextExtractionMethod.DocumentIntelligence);
            }

            // Check estimated token count against limit
            var estimatedTokens = text.Length / 4;
            if (estimatedTokens > _options.MaxInputTokens)
            {
                _logger.LogWarning(
                    "File {FileName} has ~{EstimatedTokens} tokens, exceeding limit of {MaxTokens}. Text will be truncated.",
                    fileName, estimatedTokens, _options.MaxInputTokens);

                var maxChars = _options.MaxInputTokens * 4;
                text = text[..Math.Min(text.Length, maxChars)];
                text += "\n\n[Content truncated due to size limits]";
            }

            _logger.LogInformation(
                "Successfully extracted {CharCount} characters from {FileName} using Document Intelligence ({PageCount} pages)",
                text.Length, fileName, result.Pages.Count);

            return TextExtractionResult.Succeeded(text, TextExtractionMethod.DocumentIntelligence);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout from the linked token — rethrow so outer handler catches it
            _logger.LogWarning(
                "Document Intelligence extraction timed out after {Timeout}s for {FileName}",
                _options.DocIntelTimeoutSeconds, fileName);
            _circuitRegistry?.RecordFailure(CircuitBreakerRegistry.DocumentIntelligence);
            throw;
        }
        catch (RequestFailedException ex) when (ex.Status == 400)
        {
            // 400 errors are client-side (bad document) — don't count toward circuit breaker
            _logger.LogWarning(ex, "Document Intelligence could not process {FileName} - invalid or unsupported format", fileName);
            return TextExtractionResult.Failed(
                "The document format is invalid or unsupported by Document Intelligence.",
                TextExtractionMethod.DocumentIntelligence);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Document Intelligence API error for {FileName}: {Status} {Code}",
                fileName, ex.Status, ex.ErrorCode);
            _circuitRegistry?.RecordFailure(CircuitBreakerRegistry.DocumentIntelligence);
            // Rethrow so circuit breaker tracks it
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract text from {FileName} using Document Intelligence", fileName);
            _circuitRegistry?.RecordFailure(CircuitBreakerRegistry.DocumentIntelligence);
            // Rethrow so circuit breaker tracks it
            throw;
        }
    }

    /// <summary>
    /// Check if a file extension is supported for extraction.
    /// </summary>
    public bool IsSupported(string extension)
    {
        var ext = extension.StartsWith('.') ? extension : $".{extension}";
        return _options.IsFileTypeSupported(ext.ToLowerInvariant());
    }

    /// <summary>
    /// Get the extraction method for a file extension.
    /// </summary>
    public ExtractionMethod? GetMethod(string extension)
    {
        var ext = extension.StartsWith('.') ? extension : $".{extension}";
        return _options.GetExtractionMethod(ext.ToLowerInvariant());
    }
}
