using Microsoft.Graph.Models;
using MimeKit;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Converts a Microsoft Graph Message (with expanded attachments) to RFC 2822 .eml format.
/// Pure transformation - no I/O, no Dataverse calls.
/// </summary>
public sealed class GraphMessageToEmlConverter
{
    private const string FallbackAddress = "unknown@unknown.com";

    /// <summary>
    /// Converts a Microsoft Graph Message to an EmlResult containing RFC 2822 .eml bytes and a suggested filename.
    /// The Graph message should have attachments expanded (i.e., fetched with $expand=attachments).
    /// </summary>
    public EmlResult ConvertToEml(Message graphMessage)
    {
        ArgumentNullException.ThrowIfNull(graphMessage);

        var mimeMessage = BuildMimeMessage(graphMessage);

        // Serialize to bytes
        using var stream = new MemoryStream();
        mimeMessage.WriteTo(stream);
        var bytes = stream.ToArray();

        // Generate filename
        var subject = graphMessage.Subject ?? "No Subject";
        var sanitizedSubject = SanitizeFileName(subject);
        var date = graphMessage.SentDateTime ?? graphMessage.ReceivedDateTime ?? DateTimeOffset.UtcNow;
        var dateStr = date.ToString("yyyyMMdd_HHmmss");
        var fileName = $"{sanitizedSubject}_{dateStr}.eml";

        return new EmlResult(bytes, fileName);
    }

    private static MimeMessage BuildMimeMessage(Message graphMessage)
    {
        var message = new MimeMessage();

        // From
        var from = graphMessage.From?.EmailAddress;
        if (from != null && !string.IsNullOrEmpty(from.Address))
        {
            message.From.Add(new MailboxAddress(from.Name ?? from.Address, from.Address));
        }
        else
        {
            message.From.Add(new MailboxAddress(FallbackAddress, FallbackAddress));
        }

        // To
        if (graphMessage.ToRecipients is { Count: > 0 })
        {
            foreach (var recipient in graphMessage.ToRecipients)
            {
                AddRecipient(message.To, recipient);
            }
        }

        // Cc
        if (graphMessage.CcRecipients is { Count: > 0 })
        {
            foreach (var recipient in graphMessage.CcRecipients)
            {
                AddRecipient(message.Cc, recipient);
            }
        }

        // Bcc
        if (graphMessage.BccRecipients is { Count: > 0 })
        {
            foreach (var recipient in graphMessage.BccRecipients)
            {
                AddRecipient(message.Bcc, recipient);
            }
        }

        // Subject
        message.Subject = graphMessage.Subject ?? string.Empty;

        // Date
        message.Date = graphMessage.SentDateTime ?? graphMessage.ReceivedDateTime ?? DateTimeOffset.UtcNow;

        // Message-ID
        message.MessageId = graphMessage.InternetMessageId
            ?? $"{graphMessage.Id ?? Guid.NewGuid().ToString("N")}@graph.microsoft.com";

        // Threading headers from internetMessageHeaders
        if (graphMessage.InternetMessageHeaders is { Count: > 0 })
        {
            var inReplyTo = graphMessage.InternetMessageHeaders
                .FirstOrDefault(h => string.Equals(h.Name, "In-Reply-To", StringComparison.OrdinalIgnoreCase));
            if (inReplyTo?.Value is not null)
            {
                message.InReplyTo = inReplyTo.Value;
            }

            var references = graphMessage.InternetMessageHeaders
                .FirstOrDefault(h => string.Equals(h.Name, "References", StringComparison.OrdinalIgnoreCase));
            if (references?.Value is not null)
            {
                foreach (var refId in references.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    message.References.Add(refId);
                }
            }
        }

        // Build body
        var bodyPart = BuildBodyPart(graphMessage);

        // Handle attachments
        var fileAttachments = graphMessage.Attachments?
            .OfType<FileAttachment>()
            .Where(a => a.ContentBytes is { Length: > 0 })
            .ToList();

        if (fileAttachments is { Count: > 0 })
        {
            var hasInline = fileAttachments.Any(a => a.IsInline == true);
            var hasRegular = fileAttachments.Any(a => a.IsInline != true);

            if (hasInline && !hasRegular)
            {
                // Only inline attachments: use multipart/related
                var related = new Multipart("related");
                related.Add(bodyPart);
                foreach (var att in fileAttachments)
                {
                    related.Add(BuildAttachmentPart(att));
                }
                message.Body = related;
            }
            else if (hasInline && hasRegular)
            {
                // Both inline and regular: multipart/mixed with multipart/related for body + inline
                var related = new Multipart("related");
                related.Add(bodyPart);
                foreach (var att in fileAttachments.Where(a => a.IsInline == true))
                {
                    related.Add(BuildAttachmentPart(att));
                }

                var mixed = new Multipart("mixed");
                mixed.Add(related);
                foreach (var att in fileAttachments.Where(a => a.IsInline != true))
                {
                    mixed.Add(BuildAttachmentPart(att));
                }
                message.Body = mixed;
            }
            else
            {
                // Only regular attachments: multipart/mixed
                var mixed = new Multipart("mixed");
                mixed.Add(bodyPart);
                foreach (var att in fileAttachments)
                {
                    mixed.Add(BuildAttachmentPart(att));
                }
                message.Body = mixed;
            }
        }
        else
        {
            // No attachments: simple body
            message.Body = bodyPart;
        }

        return message;
    }

    private static TextPart BuildBodyPart(Message graphMessage)
    {
        if (graphMessage.Body is null || string.IsNullOrEmpty(graphMessage.Body.Content))
        {
            return new TextPart("plain") { Text = string.Empty };
        }

        var subtype = graphMessage.Body.ContentType == BodyType.Html ? "html" : "plain";
        return new TextPart(subtype) { Text = graphMessage.Body.Content };
    }

    private static MimePart BuildAttachmentPart(FileAttachment attachment)
    {
        var contentType = attachment.ContentType ?? "application/octet-stream";

        var mimePart = new MimePart(contentType)
        {
            Content = new MimeKit.MimeContent(new MemoryStream(attachment.ContentBytes!)),
            ContentTransferEncoding = ContentEncoding.Base64,
            FileName = attachment.Name ?? "attachment"
        };

        if (attachment.IsInline == true)
        {
            mimePart.ContentDisposition = new ContentDisposition(ContentDisposition.Inline);
            if (!string.IsNullOrEmpty(attachment.ContentId))
            {
                mimePart.ContentId = attachment.ContentId;
            }
        }
        else
        {
            mimePart.ContentDisposition = new ContentDisposition(ContentDisposition.Attachment);
        }

        return mimePart;
    }

    private static void AddRecipient(InternetAddressList list, Recipient? recipient)
    {
        var email = recipient?.EmailAddress;
        if (email != null && !string.IsNullOrEmpty(email.Address))
        {
            list.Add(new MailboxAddress(email.Name ?? email.Address, email.Address));
        }
    }

    private static string SanitizeFileName(string input)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(input.Where(c => !invalid.Contains(c)).ToArray());
        return sanitized.Length > 50 ? sanitized[..50] : sanitized;
    }
}
