using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;
using MimeKit;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Office;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// Handles email enrichment via Microsoft Graph and EML file construction via MimeKit.
/// Extracted from OfficeService to enforce single responsibility.
/// </summary>
public class OfficeEmailEnricher
{
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly ILogger<OfficeEmailEnricher> _logger;

    public OfficeEmailEnricher(
        IGraphClientFactory graphClientFactory,
        ILogger<OfficeEmailEnricher> logger)
    {
        _graphClientFactory = graphClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Enriches email metadata by fetching body and attachments from Graph API when missing.
    /// Uses OBO authentication to access user's mailbox via Microsoft Graph.
    /// </summary>
    public async Task<SaveRequest> EnrichEmailFromGraphAsync(
        SaveRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Only fetch if body is missing and we have an internet message ID
        if (!string.IsNullOrEmpty(request.Email?.Body) || string.IsNullOrEmpty(request.Email?.InternetMessageId))
        {
            return request; // Body already present or no message ID
        }

        try
        {
            _logger.LogInformation(
                "Fetching email content from Graph API for message {MessageId}",
                request.Email.InternetMessageId);

            // Get Graph client with OBO auth
            var graphClient = await _graphClientFactory.ForUserAsync(httpContext, cancellationToken);

            // Fetch message with body and attachments
            var message = await graphClient.Me.Messages[request.Email.InternetMessageId]
                .GetAsync(requestConfig =>
                {
                    requestConfig.QueryParameters.Select = new[]
                    {
                        "body",
                        "subject",
                        "from",
                        "toRecipients",
                        "ccRecipients",
                        "bccRecipients",
                        "hasAttachments",
                        "internetMessageId",
                        "sentDateTime"
                    };
                    requestConfig.QueryParameters.Expand = new[] { "attachments" };
                }, cancellationToken);

            if (message == null)
            {
                _logger.LogWarning(
                    "Graph API returned null message for {MessageId}",
                    request.Email.InternetMessageId);
                return request; // Graph API returned null, return original request
            }

            // Extract body content
            string? bodyContent = null;
            bool isBodyHtml = false;
            if (message.Body != null && !string.IsNullOrEmpty(message.Body.Content))
            {
                bodyContent = message.Body.Content;
                isBodyHtml = message.Body.ContentType == BodyType.Html;

                _logger.LogInformation(
                    "Retrieved email body from Graph API: Length={BodyLength}, IsHtml={IsHtml}",
                    bodyContent.Length,
                    isBodyHtml);
            }

            // Extract ALL attachments (for embedding in .eml file)
            // Note: Attachment selection only affects which ones become separate Documents, not what's in the .eml
            List<AttachmentReference>? attachmentReferences = null;
            if (message.HasAttachments == true && message.Attachments?.Any() == true)
            {
                attachmentReferences = new List<AttachmentReference>();

                foreach (var attachment in message.Attachments)
                {
                    if (attachment is FileAttachment fileAttachment && fileAttachment.ContentBytes != null)
                    {
                        var contentBase64 = Convert.ToBase64String(fileAttachment.ContentBytes);

                        attachmentReferences.Add(new AttachmentReference
                        {
                            AttachmentId = attachment.Id ?? Guid.NewGuid().ToString(),
                            FileName = fileAttachment.Name ?? "attachment",
                            Size = fileAttachment.Size,
                            ContentType = fileAttachment.ContentType ?? "application/octet-stream",
                            ContentBase64 = contentBase64,
                            IsInline = fileAttachment.IsInline ?? false,
                            ContentId = fileAttachment.ContentId
                        });
                    }
                }

                _logger.LogInformation(
                    "Retrieved {AttachmentCount} attachments from Graph API for message {MessageId} - all will be embedded in .eml",
                    attachmentReferences.Count,
                    request.Email.InternetMessageId);
            }

            // Create updated email metadata with Graph API content
            // EmailMetadata is a record with init-only properties, so we need to create a new instance
            if (bodyContent != null || attachmentReferences != null)
            {
                return request with
                {
                    Email = request.Email with
                    {
                        Body = bodyContent ?? request.Email.Body,
                        IsBodyHtml = bodyContent != null ? isBodyHtml : request.Email.IsBodyHtml,
                        Attachments = attachmentReferences ?? request.Email.Attachments
                    }
                };
            }

            return request; // No updates needed
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to fetch email content from Graph API for message {MessageId}",
                request.Email?.InternetMessageId);

            // Don't throw - continue with whatever content we have from the client
            // This allows fallback to client-provided data if Graph API fails
            return request;
        }
    }

    /// <summary>
    /// Enriches attachment metadata by fetching content from Graph API.
    /// Uses OBO authentication to access user's mailbox and extract the specific attachment.
    /// </summary>
    public async Task<SaveRequest> EnrichAttachmentFromGraphAsync(
        SaveRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Only fetch if content is missing and we have parent email ID
        if (!string.IsNullOrEmpty(request.Attachment?.ContentBase64) || string.IsNullOrEmpty(request.Attachment?.ParentEmailId))
        {
            return request; // Content already present or no parent email ID
        }

        try
        {
            _logger.LogInformation(
                "Fetching attachment content from Graph API for attachment {FileName} from message {MessageId}",
                request.Attachment.FileName,
                request.Attachment.ParentEmailId);

            // Get Graph client with OBO auth
            var graphClient = await _graphClientFactory.ForUserAsync(httpContext, cancellationToken);

            // Fetch message with attachments
            var message = await graphClient.Me.Messages[request.Attachment.ParentEmailId]
                .GetAsync(requestConfig =>
                {
                    requestConfig.QueryParameters.Expand = new[] { "attachments" };
                }, cancellationToken);

            if (message == null || message.Attachments == null)
            {
                _logger.LogWarning(
                    "Graph API returned null message or no attachments for {MessageId}",
                    request.Attachment.ParentEmailId);
                return request;
            }

            // Find the matching attachment by filename (case-insensitive)
            FileAttachment? matchingAttachment = null;
            foreach (var attachment in message.Attachments)
            {
                if (attachment is FileAttachment fileAttachment &&
                    string.Equals(fileAttachment.Name, request.Attachment.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    matchingAttachment = fileAttachment;
                    break;
                }
            }

            if (matchingAttachment?.ContentBytes == null)
            {
                _logger.LogWarning(
                    "Attachment {FileName} not found in message {MessageId} or has no content",
                    request.Attachment.FileName,
                    request.Attachment.ParentEmailId);
                return request;
            }

            // Convert to base64
            var contentBase64 = Convert.ToBase64String(matchingAttachment.ContentBytes);

            _logger.LogInformation(
                "Retrieved attachment content from Graph API: {FileName}, Size={Size} bytes",
                request.Attachment.FileName,
                matchingAttachment.ContentBytes.Length);

            // Return updated request with attachment content
            return request with
            {
                Attachment = request.Attachment with
                {
                    ContentBase64 = contentBase64,
                    Size = request.Attachment.Size ?? matchingAttachment.ContentBytes.Length
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to fetch attachment content from Graph API for {FileName} from message {MessageId}",
                request.Attachment?.FileName,
                request.Attachment?.ParentEmailId);

            // Don't throw - return error to user
            throw new InvalidOperationException(
                $"Failed to retrieve attachment content: {ex.Message}. Please try again.",
                ex);
        }
    }

    /// <summary>
    /// Builds an RFC 5322 compliant .eml file from Office add-in email metadata.
    /// Uses MimeKit for proper MIME message construction.
    /// </summary>
    public static Stream BuildEmlFromMetadata(EmailMetadata metadata)
    {
        var message = new MimeMessage();

        // Set sender
        message.From.Add(new MailboxAddress(metadata.SenderName ?? "", metadata.SenderEmail));

        // Set recipients
        if (metadata.Recipients != null)
        {
            foreach (var recipient in metadata.Recipients)
            {
                var mailbox = new MailboxAddress(recipient.Name ?? "", recipient.Email);
                switch (recipient.Type)
                {
                    case RecipientType.To:
                        message.To.Add(mailbox);
                        break;
                    case RecipientType.Cc:
                        message.Cc.Add(mailbox);
                        break;
                    case RecipientType.Bcc:
                        message.Bcc.Add(mailbox);
                        break;
                }
            }
        }

        // Set subject
        message.Subject = metadata.Subject;

        // Set dates
        if (metadata.SentDate.HasValue)
        {
            message.Date = metadata.SentDate.Value;
        }

        // Set message ID if it's a valid RFC 2822 Message-ID format
        // Note: Exchange item IDs (AAMkA...) are NOT valid Message-IDs
        // Valid format: <something@something> or just something@something
        if (!string.IsNullOrEmpty(metadata.InternetMessageId))
        {
            // Check if it looks like an RFC 2822 Message-ID (contains @ and doesn't look like base64)
            var msgId = metadata.InternetMessageId;
            if (msgId.Contains('@') && !msgId.StartsWith("AAMk", StringComparison.OrdinalIgnoreCase))
            {
                // Strip angle brackets if present, MimeKit will add them
                if (msgId.StartsWith("<") && msgId.EndsWith(">"))
                {
                    msgId = msgId[1..^1];
                }
                message.MessageId = msgId;
            }
            // If it's an Exchange item ID, store it in a custom header for reference
            else
            {
                message.Headers.Add("X-Exchange-Item-Id", metadata.InternetMessageId);
            }
        }

        // Build body with attachments
        var bodyBuilder = new BodyBuilder();
        if (metadata.IsBodyHtml && !string.IsNullOrEmpty(metadata.Body))
        {
            bodyBuilder.HtmlBody = metadata.Body;
        }
        else if (!string.IsNullOrEmpty(metadata.Body))
        {
            bodyBuilder.TextBody = metadata.Body;
        }

        // Add attachments from client-side content
        if (metadata.Attachments != null)
        {
            foreach (var attachment in metadata.Attachments)
            {
                if (string.IsNullOrEmpty(attachment.ContentBase64))
                {
                    continue; // Skip attachments without content
                }

                try
                {
                    var contentBytes = Convert.FromBase64String(attachment.ContentBase64);
                    var contentType = MimeKit.ContentType.Parse(attachment.ContentType ?? "application/octet-stream");

                    if (attachment.IsInline && !string.IsNullOrEmpty(attachment.ContentId))
                    {
                        // Inline attachment (embedded image in HTML body)
                        var linkedResource = bodyBuilder.LinkedResources.Add(
                            attachment.FileName,
                            contentBytes,
                            contentType);
                        linkedResource.ContentId = attachment.ContentId;
                    }
                    else
                    {
                        // Regular attachment
                        bodyBuilder.Attachments.Add(
                            attachment.FileName,
                            contentBytes,
                            contentType);
                    }
                }
                catch (FormatException)
                {
                    // Skip invalid base64 content
                }
            }
        }

        message.Body = bodyBuilder.ToMessageBody();

        // Write to stream
        var stream = new MemoryStream();
        message.WriteTo(stream);
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Generates a sanitized filename for the .eml file.
    /// Format: YYYY-MM-DD_Subject.eml (max 100 chars, special chars removed)
    /// </summary>
    public static string GenerateEmlFileName(EmailMetadata metadata)
    {
        var datePrefix = metadata.SentDate?.ToString("yyyy-MM-dd") ?? DateTime.UtcNow.ToString("yyyy-MM-dd");
        var sanitizedSubject = SanitizeFileName(metadata.Subject);

        // Limit subject to 80 chars to leave room for date and extension
        if (sanitizedSubject.Length > 80)
        {
            sanitizedSubject = sanitizedSubject[..80];
        }

        return $"{datePrefix}_{sanitizedSubject}.eml";
    }

    /// <summary>
    /// Sanitizes a string for use as a filename.
    /// </summary>
    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "untitled";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "untitled" : sanitized.Trim();
    }
}
