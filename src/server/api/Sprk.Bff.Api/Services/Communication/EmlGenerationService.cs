using MimeKit;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Generates RFC 2822 .eml files from communication data for archival.
/// </summary>
public sealed class EmlGenerationService
{
    private readonly ILogger<EmlGenerationService> _logger;

    public EmlGenerationService(ILogger<EmlGenerationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Generates .eml content from a communication request and response.
    /// </summary>
    public EmlResult GenerateEml(
        SendCommunicationRequest request,
        SendCommunicationResponse response,
        IReadOnlyList<EmlAttachment>? attachments = null)
    {
        var message = new MimeMessage();

        // Set From
        message.From.Add(new MailboxAddress(response.From, response.From));

        // Set To
        foreach (var to in request.To)
            message.To.Add(new MailboxAddress(to, to));

        // Set CC
        if (request.Cc is { Length: > 0 })
            foreach (var cc in request.Cc)
                message.Cc.Add(new MailboxAddress(cc, cc));

        // Set BCC
        if (request.Bcc is { Length: > 0 })
            foreach (var bcc in request.Bcc)
                message.Bcc.Add(new MailboxAddress(bcc, bcc));

        // Set headers
        message.Subject = request.Subject;
        message.Date = response.SentAt;
        // Message-Id: prefer the Graph id, then the correlation id, else a fresh id. Coalesce EMPTY (not just
        // null) — MimeKit's MessageId setter rejects an empty string with ArgumentException, and the on-demand
        // archive path reconstructs GraphMessageId as `sprk_graphmessageid ?? string.Empty` (a record with no
        // stored message id, e.g. a draft, yields ""). Empty-tolerance keeps the .eml generatable for any comm.
        message.MessageId = FirstNonEmpty(response.GraphMessageId, response.CorrelationId)
                            ?? Guid.NewGuid().ToString("N");

        // Build body
        var bodyPart = request.BodyFormat == BodyFormat.HTML
            ? new TextPart("html") { Text = request.Body }
            : new TextPart("plain") { Text = request.Body };

        if (attachments is { Count: > 0 })
        {
            // Multipart with attachments
            var multipart = new Multipart("mixed");
            multipart.Add(bodyPart);

            foreach (var att in attachments)
            {
                var mimePart = new MimePart(att.ContentType ?? "application/octet-stream")
                {
                    Content = new MimeContent(new MemoryStream(att.Content)),
                    ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
                    ContentTransferEncoding = ContentEncoding.Base64,
                    FileName = att.FileName
                };
                multipart.Add(mimePart);
            }

            message.Body = multipart;
        }
        else
        {
            // Simple body only
            message.Body = bodyPart;
        }

        // Serialize to bytes
        using var stream = new MemoryStream();
        message.WriteTo(stream);
        var bytes = stream.ToArray();

        // Generate filename. This value becomes an SPE upload path verbatim (CommunicationService
        // .ArchiveToSpeAsync -> "{communicationId:N}_{FileName}"), so it goes through the ONE sanitizer:
        // Infrastructure/Graph/SpeUploadPath. The local copy that used to live here — same Windows-strict
        // character set, its own 50-char truncation — was consolidated 2026-08-29 (root CLAUDE.md §11).
        var sanitizedSubject = SpeUploadPath.SanitizeFileName(request.Subject, maxLength: 50);
        var dateStr = response.SentAt.ToString("yyyyMMdd_HHmmss");
        var fileName = $"{sanitizedSubject}_{dateStr}.eml";

        return new EmlResult(bytes, fileName);
    }

    /// <summary>Returns the first non-null, non-empty value, or null when both are empty (empty-tolerant coalesce).</summary>
    private static string? FirstNonEmpty(string? a, string? b) =>
        !string.IsNullOrEmpty(a) ? a : !string.IsNullOrEmpty(b) ? b : null;
}

/// <summary>
/// Result of .eml generation containing the file bytes and suggested filename.
/// </summary>
public sealed record EmlResult(byte[] Content, string FileName);

/// <summary>
/// Attachment data for inclusion in the .eml file.
/// </summary>
public sealed record EmlAttachment
{
    public required string FileName { get; init; }
    public required byte[] Content { get; init; }
    public string? ContentType { get; init; }
}
