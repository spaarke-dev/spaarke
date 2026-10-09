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
        // Only fetch if the body is missing and the request names the message in the caller's mailbox. Task 121: Graph
        // addresses a message by its Exchange item id, never by the RFC Message-ID, so the key is the item id — sent in
        // ExchangeItemId, or (an add-in older than task 121) in InternetMessageId. A request that carries only an RFC
        // Message-ID cannot be fetched and is returned unchanged.
        var graphMessageKey = request.Email is null ? null : ResolveExchangeItemId(request.Email);
        if (request.Email is null || !string.IsNullOrEmpty(request.Email.Body) || graphMessageKey is null)
        {
            return request; // Body already present or no item id
        }

        try
        {
            _logger.LogInformation(
                "Fetching email content from Graph API for message {MessageId}",
                graphMessageKey);

            // Get Graph client with OBO auth
            var graphClient = await _graphClientFactory.ForUserAsync(httpContext, cancellationToken);

            // Fetch message with body and attachments
            var message = await graphClient.Me.Messages[graphMessageKey]
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
                    graphMessageKey);
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
                    graphMessageKey);
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
                graphMessageKey);

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

        // The Message-ID header carries the RFC 5322 Message-ID (task 121: the task pane now sends it, as Quick Save
        // always did). Only when the request has none — an add-in older than task 121 sent the Exchange item id in
        // InternetMessageId, and a draft has no Message-ID — is the item id kept, in a custom header, for reference.
        var rfcMessageId = ResolveRfcMessageId(metadata);
        if (rfcMessageId is not null)
        {
            // Strip angle brackets if present, MimeKit will add them
            message.MessageId = rfcMessageId.StartsWith('<') && rfcMessageId.EndsWith('>')
                ? rfcMessageId[1..^1]
                : rfcMessageId;
        }
        else if (ResolveExchangeItemId(metadata) is { } exchangeItemId)
        {
            message.Headers.Add("X-Exchange-Item-Id", exchangeItemId);
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
    /// Generates the human-readable name for the .eml file.
    /// Format: YYYY-MM-DD_Subject.eml (subject max 80 chars, special chars removed). This is the name the document
    /// is SHOWN under (<c>sprk_documentname</c>); <see cref="GenerateEmlNames"/> gives the name it is STORED under.
    /// </summary>
    public static string GenerateEmlFileName(EmailMetadata metadata)
    {
        var datePrefix = metadata.SentDate?.ToString("yyyy-MM-dd") ?? DateTime.UtcNow.ToString("yyyy-MM-dd");

        // Subject capped at 80 chars to leave room for the date prefix and the extension.
        var sanitizedSubject = SpeUploadPath.SanitizeFileName(metadata.Subject, maxLength: 80);

        return $"{datePrefix}_{sanitizedSubject}.eml";
    }

    /// <summary>
    /// Task 046 (b): the two names an Office email save needs, from ONE generator.
    /// <c>DocumentName</c> is <see cref="GenerateEmlFileName"/>: the readable name, written to
    /// <c>sprk_documentname</c> and never suffixed. <c>StoredFileName</c> is the SPE upload path and
    /// <c>sprk_filename</c>. For a SYSTEM-DERIVED name (<see cref="EmailMetadata.IsNameSystemDerived"/>) it carries a
    /// short unique suffix, <c>{yyyy-MM-dd}_{subject}_{8 hex}.eml</c>; for a name the user typed it is
    /// <c>DocumentName</c>, unchanged.
    /// </summary>
    /// <remarks>
    /// <para><b>Why.</b> The create upload is PATH-keyed under <c>ConflictBehavior.Replace</c>, so two emails with
    /// the same subject and date in one container shared a path, and the second silently overwrote the first
    /// email's file.</para>
    /// <para><b>Why random, not derived from the message.</b> Like the attachment children's
    /// <c>{parentDocumentId:N}_</c> prefix, the suffix is unique per save. A per-message value would make the SAME
    /// email filed to two records in one container collide with itself. 8 hex digits = 32 bits, so two same-date,
    /// same-subject emails in one container share a name with probability 2^-32.</para>
    /// <para>The <c>.eml</c> extension stays last, so SPE still infers <c>message/rfc822</c>. The server-side
    /// inbound and outbound archives do not use this generator: they already fold the communication id into the
    /// stored name (<c>{communicationId:N}_…</c>), which makes their paths unique.</para>
    /// </remarks>
    public static (string DocumentName, string StoredFileName) GenerateEmlNames(EmailMetadata metadata)
    {
        var documentName = GenerateEmlFileName(metadata);
        if (!metadata.IsNameSystemDerived)
            return (documentName, documentName);

        var stem = documentName[..^".eml".Length];
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return (documentName, $"{stem}_{suffix}.eml");
    }

    // ── Task 121 (spaarkeai-word-add-in-r1): the email's two keys ────────────────────────────────────────────────────
    // An Outlook message has two identifiers with different jobs. The RFC 5322 Message-ID (item.internetMessageId) is
    // the same in every mailbox: it is the .eml's Message-ID header, sprk_emailmessageid, and the key that links a save
    // to the communication that captured the same email. The Exchange item id (item.itemId) names the message in ONE
    // mailbox: it is what Graph fetches by. Before task 121 the task pane sent the item id in InternetMessageId, so its
    // saves stored no Message-ID and never linked to their communication. The pane now sends each in its own field; a
    // request from an older pane is still read correctly, by the shape of the value.

    /// <summary>
    /// Whether <paramref name="value"/> is an RFC 5322 Message-ID rather than an Exchange item id. A Message-ID always
    /// has an <c>@</c> (<c>id-left "@" id-right</c>); an Exchange item id is base64 and never does, and the ids of mail
    /// items begin <c>AAMk</c> (or <c>AQMk</c>) — checked as well, defensively.
    /// </summary>
    private static bool IsRfcMessageId(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Contains('@')
        && !value.StartsWith("AAMk", StringComparison.Ordinal)
        && !value.StartsWith("AQMk", StringComparison.Ordinal);

    /// <summary>
    /// The email's RFC Message-ID, or <see langword="null"/> when the request carries none. A request that sends
    /// <see cref="EmailMetadata.ExchangeItemId"/> is from a task-121 client, which never puts the item id in
    /// <see cref="EmailMetadata.InternetMessageId"/>, so its value is taken as sent. Only a request without it — an
    /// older client — is read by the shape of the value.
    /// </summary>
    public static string? ResolveRfcMessageId(EmailMetadata email)
    {
        if (string.IsNullOrWhiteSpace(email.InternetMessageId))
            return null;

        return !string.IsNullOrWhiteSpace(email.ExchangeItemId) || IsRfcMessageId(email.InternetMessageId)
            ? email.InternetMessageId
            : null;
    }

    /// <summary>
    /// The message's Exchange item id: <see cref="EmailMetadata.ExchangeItemId"/>, or — from an add-in older than task
    /// 121 — an <see cref="EmailMetadata.InternetMessageId"/> that is not a Message-ID. <see langword="null"/> when
    /// neither is present.
    /// </summary>
    public static string? ResolveExchangeItemId(EmailMetadata email)
    {
        if (!string.IsNullOrWhiteSpace(email.ExchangeItemId))
            return email.ExchangeItemId;

        return !string.IsNullOrWhiteSpace(email.InternetMessageId) && !IsRfcMessageId(email.InternetMessageId)
            ? email.InternetMessageId
            : null;
    }

    /// <summary>
    /// The message id an email save STORES (<c>sprk_emailmessageid</c>, the email artifact) and keys its idempotency on:
    /// the RFC Message-ID whenever the request carries one. Without one (an add-in older than task 121, or a draft) it
    /// is the item id, exactly what such a save stored before — so <c>resolve-email-identity</c>, which matches either
    /// key, still finds it.
    /// </summary>
    public static string? ResolveStoredMessageId(EmailMetadata email) =>
        ResolveRfcMessageId(email) ?? ResolveExchangeItemId(email);

    // SanitizeFileName MOVED 2026-08-29 to Infrastructure/Graph/SpeUploadPath.SanitizeFileName, together
    // with its explicit Windows-strict character set. It was the only PUBLIC implementation of a function
    // seven other places had privately re-written, and callers now span Api/, Services/Communication/,
    // Services/Workspace/, Services/Ai/, Services/Compose/ and Workers/ — reaching into Services/Office
    // from all of those is the inverted dependency that produced the copies in the first place
    // (root CLAUDE.md §11). Read the rationale at the new site; do not re-add a local copy here.
}
