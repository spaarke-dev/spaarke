using System.Text;
using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Options;
using MimeKit;
using MsgReader.Outlook;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Service for extracting text from documents.
/// Handles native text formats directly; PDF/DOCX require Document Intelligence (Task 060).
/// </summary>
public class TextExtractorService : ITextExtractor
{
    private readonly DocumentIntelligenceOptions _options;
    private readonly ILogger<TextExtractorService> _logger;

    public TextExtractorService(IOptions<DocumentIntelligenceOptions> options, ILogger<TextExtractorService> logger)
    {
        _options = options.Value;
        _logger = logger;
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

            _logger.LogDebug("Starting Document Intelligence extraction for {FileName}", fileName);

            // Create client
            var credential = new AzureKeyCredential(_options.DocIntelKey);
            var client = new DocumentIntelligenceClient(new Uri(_options.DocIntelEndpoint), credential);

            // Read stream to BinaryData (required by SDK)
            using var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream, cancellationToken);
            var binaryData = BinaryData.FromBytes(memoryStream.ToArray());

            // Analyze document using prebuilt-read model
            var operation = await client.AnalyzeDocumentAsync(
                WaitUntil.Completed,
                "prebuilt-read",
                binaryData,
                cancellationToken: cancellationToken);

            var result = operation.Value;

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
        catch (RequestFailedException ex) when (ex.Status == 400)
        {
            _logger.LogWarning(ex, "Document Intelligence could not process {FileName} - invalid or unsupported format", fileName);
            return TextExtractionResult.Failed(
                "The document format is invalid or unsupported by Document Intelligence.",
                TextExtractionMethod.DocumentIntelligence);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Document Intelligence API error for {FileName}: {Status} {Code}",
                fileName, ex.Status, ex.ErrorCode);
            return TextExtractionResult.Failed(
                $"Document Intelligence service error: {ex.Message}",
                TextExtractionMethod.DocumentIntelligence);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract text from {FileName} using Document Intelligence", fileName);
            return TextExtractionResult.Failed(
                $"Failed to extract text: {ex.Message}",
                TextExtractionMethod.DocumentIntelligence);
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
