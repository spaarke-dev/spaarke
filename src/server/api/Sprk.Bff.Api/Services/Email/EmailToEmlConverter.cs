using System.Text.RegularExpressions;
using Azure.Core;
using Microsoft.Extensions.Options;
using MimeKit;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Email;

/// <summary>
/// Parses RFC 5322 compliant .eml file streams and extracts their attachments using MimeKit.
/// </summary>
/// <remarks>
/// The single live consumer is <see cref="Sprk.Bff.Api.Workers.Office.UploadFinalizationWorker"/>,
/// which resolves this per-operation (from a created scope) to call <see cref="ExtractAttachments"/>
/// on a .eml stream downloaded from SharePoint Embedded.
/// The former Dataverse-fetch / .eml-builder half (ConvertToEmlAsync, GenerateEmlFileNameAsync,
/// BuildMimeMessage, Fetch*) was removed 2026-08-14 as dead code (zero production callers). The two
/// live .eml builders are <c>EmlGenerationService</c> and <c>GraphMessageToEmlConverter</c>
/// (Services/Communication) — they are intentionally distinct and unaffected.
/// </remarks>
public class EmailToEmlConverter : IEmailToEmlConverter
{
    private readonly EmailProcessingOptions _options;
    private readonly ILogger<EmailToEmlConverter> _logger;

    /// <summary>
    /// Creates a new EmailToEmlConverter. The <paramref name="httpClient"/>,
    /// <paramref name="configuration"/> and <paramref name="credential"/> parameters are retained
    /// for DI + constructor-contract compatibility; the live parser path
    /// (<see cref="ExtractAttachments"/>) requires only the processing options and logger.
    /// </summary>
    public EmailToEmlConverter(
        HttpClient httpClient,
        IOptions<EmailProcessingOptions> options,
        IConfiguration configuration,
        TokenCredential credential,
        ILogger<EmailToEmlConverter> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    private (bool shouldCreate, string? skipReason) EvaluateAttachment(
        string fileName, string mimeType, long sizeBytes)
    {
        // Check blocked extensions
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant() ?? string.Empty;
        if (_options.BlockedAttachmentExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return (false, $"Blocked extension: {extension}");
        }

        // Check max size
        if (sizeBytes > _options.MaxAttachmentSizeBytes)
        {
            return (false, $"Exceeds max size: {sizeBytes / 1024 / 1024}MB > {_options.MaxAttachmentSizeMB}MB");
        }

        // Check signature image patterns
        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            // Check size threshold for images
            if (sizeBytes < _options.MinImageSizeKB * 1024)
            {
                return (false, $"Image too small ({sizeBytes / 1024}KB), likely signature/spacer");
            }

            // Check filename patterns
            foreach (var pattern in _options.SignatureImagePatterns)
            {
                if (Regex.IsMatch(fileName, pattern, RegexOptions.IgnoreCase))
                {
                    return (false, $"Matches signature pattern: {pattern}");
                }
            }
        }

        return (true, null);
    }

    /// <inheritdoc />
    public IReadOnlyList<EmailAttachmentInfo> ExtractAttachments(Stream emlStream)
    {
        ArgumentNullException.ThrowIfNull(emlStream);

        if (!emlStream.CanRead)
        {
            throw new ArgumentException("Stream must be readable", nameof(emlStream));
        }

        // Reset stream position if possible
        if (emlStream.CanSeek)
        {
            emlStream.Position = 0;
        }

        var attachments = new List<EmailAttachmentInfo>();

        try
        {
            // Parse the .eml file using MimeKit
            var message = MimeMessage.Load(emlStream);

            _logger.LogInformation(
                "[ExtractDebug] Loaded MimeMessage. Body type: {BodyType}, Subject: {Subject}",
                message.Body?.GetType().Name ?? "null", message.Subject);

            var partCount = 0;
            var skippedParts = 0;
            var oversizedParts = 0;

            // Iterate through all MIME parts to find attachments
            foreach (var part in IterateMimeParts(message.Body))
            {
                partCount++;

                if (part is MimePart mimePart)
                {
                    // Check if this is an attachment (either explicit attachment or inline with content)
                    var isAttachment = mimePart.IsAttachment ||
                                       mimePart.ContentDisposition?.Disposition == ContentDisposition.Attachment;
                    var isInline = mimePart.ContentDisposition?.Disposition == ContentDisposition.Inline &&
                                   !string.IsNullOrEmpty(mimePart.ContentId);

                    // Skip parts that are not attachments and not inline images with Content-ID
                    if (!isAttachment && !isInline)
                    {
                        skippedParts++;
                        continue;
                    }

                    // Get content to memory stream
                    if (mimePart.Content is null)
                    {
                        skippedParts++;
                        continue;
                    }
                    var contentStream = new MemoryStream();
                    mimePart.Content.DecodeTo(contentStream);
                    contentStream.Position = 0;

                    var sizeBytes = contentStream.Length;

                    // Check max size (NFR-05: 250MB)
                    if (sizeBytes > _options.MaxAttachmentSizeBytes)
                    {
                        oversizedParts++;
                        contentStream.Dispose();
                        continue;
                    }

                    // Determine filename
                    var fileName = mimePart.FileName;
                    if (string.IsNullOrEmpty(fileName))
                    {
                        // Generate a filename based on content type
                        var mimeType = mimePart.ContentType?.MimeType ?? "application/octet-stream";
                        var extension = GetExtensionForMimeType(mimeType);
                        fileName = $"attachment_{attachments.Count + 1}{extension}";
                    }

                    // Get content ID (strip angle brackets if present)
                    var contentId = mimePart.ContentId;
                    if (!string.IsNullOrEmpty(contentId))
                    {
                        contentId = contentId.Trim('<', '>');
                    }

                    // Evaluate if this attachment should be processed as a document
                    var attachmentMimeType = mimePart.ContentType?.MimeType ?? "application/octet-stream";
                    var (shouldCreate, skipReason) = EvaluateAttachment(
                        fileName,
                        attachmentMimeType,
                        sizeBytes);

                    attachments.Add(new EmailAttachmentInfo
                    {
                        AttachmentId = Guid.Empty, // No Dataverse ID for extracted attachments
                        FileName = fileName,
                        MimeType = attachmentMimeType,
                        Content = contentStream,
                        SizeBytes = sizeBytes,
                        IsInline = isInline,
                        ContentId = contentId,
                        ShouldCreateDocument = shouldCreate,
                        SkipReason = skipReason
                    });
                }
            }

            _logger.LogInformation(
                "Extraction complete: {PartCount} parts scanned, {SkippedParts} non-attachment parts skipped, " +
                "{OversizedParts} oversized, {AttachmentCount} attachments found, {InlineCount} inline, " +
                "{SkipCount} will be skipped",
                partCount, skippedParts, oversizedParts,
                attachments.Count,
                attachments.Count(a => a.IsInline),
                attachments.Count(a => !a.ShouldCreateDocument));

            return attachments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract attachments from .eml stream");

            // Dispose any already-extracted streams on error
            foreach (var attachment in attachments)
            {
                attachment.Content?.Dispose();
            }

            throw;
        }
    }

    /// <summary>
    /// Recursively iterate through all MIME parts in a message body.
    /// </summary>
    private static IEnumerable<MimeEntity> IterateMimeParts(MimeEntity? entity)
    {
        if (entity == null)
            yield break;

        if (entity is Multipart multipart)
        {
            foreach (var child in multipart)
            {
                foreach (var part in IterateMimeParts(child))
                {
                    yield return part;
                }
            }
        }
        else
        {
            yield return entity;
        }
    }

    /// <summary>
    /// Get a file extension for a MIME type.
    /// </summary>
    private static string GetExtensionForMimeType(string mimeType)
    {
        return mimeType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            "image/webp" => ".webp",
            "application/pdf" => ".pdf",
            "application/msword" => ".doc",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            "application/vnd.ms-excel" => ".xls",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
            "application/vnd.ms-powerpoint" => ".ppt",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation" => ".pptx",
            "text/plain" => ".txt",
            "text/html" => ".html",
            "text/csv" => ".csv",
            "application/zip" => ".zip",
            "application/x-zip-compressed" => ".zip",
            _ => ".bin"
        };
    }
}
