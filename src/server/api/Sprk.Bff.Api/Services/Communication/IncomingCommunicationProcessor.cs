using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Email;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Processes incoming email notifications by fetching full message details from Graph,
/// creating a sprk_communication record with Direction=Incoming, resolving associations
/// via IncomingAssociationResolver, processing attachments, and archiving the .eml file.
///
/// Registered as concrete type in AddCommunicationModule() per ADR-010.
/// Uses GraphClientFactory.ForApp() for fetching messages per ADR-007.
/// </summary>
public sealed class IncomingCommunicationProcessor
{
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly ICommunicationDataverseService _communicationService;
    private readonly IGenericEntityService _genericEntityService;
    private readonly CommunicationAccountService _accountService;
    private readonly IncomingAssociationResolver _associationResolver;
    private readonly IEmailAttachmentProcessor _attachmentProcessor;
    private readonly GraphMessageToEmlConverter _emlConverter;
    private readonly SpeFileStore _speFileStore;
    private readonly JobSubmissionService _jobSubmissionService;
    private readonly NotificationService _notificationService;
    private readonly CommunicationOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IncomingCommunicationProcessor> _logger;

    /// <summary>
    /// Matches a GUID pattern — Graph webhook resource paths use user object IDs (GUIDs)
    /// instead of email addresses, e.g. "users/e2e9000e-ce35-4f33-b0de-9c203fd5087a/messages/..."
    /// </summary>
    private static readonly Regex GuidPattern = new(
        @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        RegexOptions.Compiled);

    public IncomingCommunicationProcessor(
        IGraphClientFactory graphClientFactory,
        ICommunicationDataverseService communicationService,
        IGenericEntityService genericEntityService,
        CommunicationAccountService accountService,
        IncomingAssociationResolver associationResolver,
        IEmailAttachmentProcessor attachmentProcessor,
        GraphMessageToEmlConverter emlConverter,
        SpeFileStore speFileStore,
        JobSubmissionService jobSubmissionService,
        NotificationService notificationService,
        IOptions<CommunicationOptions> options,
        IConfiguration configuration,
        ILogger<IncomingCommunicationProcessor> logger)
    {
        _graphClientFactory = graphClientFactory;
        _communicationService = communicationService;
        _genericEntityService = genericEntityService;
        _accountService = accountService;
        _associationResolver = associationResolver;
        _attachmentProcessor = attachmentProcessor;
        _emlConverter = emlConverter;
        _speFileStore = speFileStore;
        _jobSubmissionService = jobSubmissionService;
        _notificationService = notificationService;
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Processes an incoming email: fetches from Graph, creates Dataverse record,
    /// handles attachments, and archives .eml.
    /// </summary>
    /// <param name="mailboxEmail">The shared mailbox email or Azure AD GUID that received the message.</param>
    /// <param name="graphMessageId">The Graph message ID to fetch.</param>
    /// <param name="subscriptionId">Graph subscription ID from the notification (used to resolve the correct account).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ProcessAsync(string mailboxEmail, string graphMessageId, string? subscriptionId = null, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Processing incoming communication | Mailbox: {Mailbox}, GraphMessageId: {GraphMessageId}, SubscriptionId: {SubscriptionId}",
            mailboxEmail, graphMessageId, subscriptionId);

        // ── Step 1: Deduplication check ──────────────────────────────────────────
        // Query sprk_communication where sprk_graphmessageid == graphMessageId.
        // If a record already exists, log and skip to prevent duplicates.
        if (await ExistsByGraphMessageIdAsync(graphMessageId, ct))
        {
            _logger.LogInformation(
                "Duplicate detected for GraphMessageId {GraphMessageId}. Skipping.",
                graphMessageId);
            return;
        }

        // ── Step 2: Get account details ──────────────────────────────────────────
        // Check if the receiving mailbox has sprk_autocreaterecords enabled.
        // Graph webhook resource paths contain user object IDs (GUIDs) instead of
        // email addresses. When a GUID is detected, look up the account directly
        // from the receive-enabled accounts list (shared mailboxes can't be resolved
        // via Graph Users API).
        var account = await GetReceiveAccountAsync(mailboxEmail, ct);

        if (account is null && GuidPattern.IsMatch(mailboxEmail))
        {
            _logger.LogInformation(
                "Mailbox identifier is a GUID ({Identifier}), resolving to email address",
                mailboxEmail);

            // Graph webhook notifications use Azure AD object IDs (GUIDs) instead of email addresses.
            // Strategy: 1) query Graph subscription to extract email from resource path,
            //           2) match by stored subscription ID, 3) single-account fallback.
            var allAccounts = await _accountService.QueryReceiveEnabledAccountsAsync(ct);

            // Primary: query the Graph subscription object to get the original resource path
            // which contains the email address (e.g., "users/mailbox@domain.com/mailFolders/Inbox/messages")
            if (account is null && !string.IsNullOrEmpty(subscriptionId))
            {
                try
                {
                    var graphClient = _graphClientFactory.ForApp();
                    var sub = await graphClient.Subscriptions[subscriptionId].GetAsync(cancellationToken: ct);
                    var resourceEmail = ExtractMailboxFromResource(sub?.Resource);

                    if (!string.IsNullOrEmpty(resourceEmail))
                    {
                        mailboxEmail = resourceEmail;
                        account = allAccounts.FirstOrDefault(a =>
                            string.Equals(a.EmailAddress, resourceEmail, StringComparison.OrdinalIgnoreCase));

                        _logger.LogInformation(
                            "Resolved GUID via Graph subscription {SubscriptionId} → email {Email}, account matched: {Matched}",
                            subscriptionId, resourceEmail, account is not null);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to query Graph subscription {SubscriptionId} for GUID resolution",
                        subscriptionId);
                }
            }

            // Fallback: match by stored subscription ID on Dataverse accounts
            if (account is null && !string.IsNullOrEmpty(subscriptionId))
            {
                account = allAccounts.FirstOrDefault(a =>
                    string.Equals(a.SubscriptionId, subscriptionId, StringComparison.OrdinalIgnoreCase));
            }

            // Final fallback: single-account if only one exists
            account ??= allAccounts.Length == 1 ? allAccounts[0] : null;

            if (account is not null)
            {
                mailboxEmail = account.EmailAddress;
                _logger.LogInformation(
                    "Resolved GUID to account {Email} (from {Count} receive-enabled accounts)",
                    account.EmailAddress, allAccounts.Length);
            }
            else if (GuidPattern.IsMatch(mailboxEmail))
            {
                _logger.LogWarning(
                    "Could not resolve GUID {MailboxGuid} to any account or email. " +
                    "SubscriptionId: {SubscriptionId}, Accounts: {Count}.",
                    mailboxEmail, subscriptionId, allAccounts.Length);
            }
        }

        if (account is null)
        {
            _logger.LogWarning(
                "No receive-enabled communication account found for mailbox {Mailbox}. " +
                "Processing will continue but attachment handling may be skipped.",
                mailboxEmail);
        }

        // ── Step 3: Fetch full message from Graph ────────────────────────────────
        // Using GraphClientFactory.ForApp() per ADR-007.
        Message message;
        try
        {
            var graphClient = _graphClientFactory.ForApp();

            message = await graphClient.Users[mailboxEmail]
                .Messages[graphMessageId]
                .GetAsync(config =>
                {
                    config.QueryParameters.Select = new[]
                    {
                        "id", "internetMessageId", "from", "toRecipients", "ccRecipients",
                        "subject", "body", "uniqueBody",
                        "receivedDateTime", "hasAttachments"
                    };
                    // Expand attachments inline to avoid a second call
                    config.QueryParameters.Expand = new[] { "attachments" };
                }, ct) ?? throw new InvalidOperationException(
                    $"Graph returned null for message {graphMessageId} in mailbox {mailboxEmail}");
        }
        catch (ODataError ex)
        {
            _logger.LogError(
                ex,
                "Failed to fetch message from Graph | Mailbox: {Mailbox}, " +
                "GraphMessageId: {GraphMessageId}, StatusCode: {StatusCode}, ErrorCode: {ErrorCode}",
                mailboxEmail, graphMessageId,
                ex.ResponseStatusCode, ex.Error?.Code);
            throw;
        }

        _logger.LogInformation(
            "Fetched message from Graph | Subject: '{Subject}', From: {From}, " +
            "ReceivedAt: {ReceivedAt}, HasAttachments: {HasAttachments}",
            message.Subject,
            message.From?.EmailAddress?.Address,
            message.ReceivedDateTime,
            message.HasAttachments);

        // ── Step 4: Create sprk_communication record ─────────────────────────────
        // Direction = Incoming (100000000)
        // CommunicationType = Email (100000000)
        // StatusCode = Delivered (659490003)
        // Note: Regarding fields are set in step 4.5 by IncomingAssociationResolver
        var communicationId = await CreateCommunicationRecordAsync(message, mailboxEmail, graphMessageId, ct);

        _logger.LogInformation(
            "Created sprk_communication record | CommunicationId: {CommunicationId}, " +
            "Direction: Incoming, GraphMessageId: {GraphMessageId}",
            communicationId, graphMessageId);

        // ── Step 4.5: Resolve associations (non-fatal) ────────────────────────────
        try
        {
            await _associationResolver.ResolveAsync(
                communicationId, mailboxEmail, graphMessageId, message, account, ct);
        }
        catch (Exception ex)
        {
            // Association resolution failure is non-fatal
            _logger.LogWarning(
                ex,
                "Association resolution failed (non-fatal) | CommunicationId: {CommunicationId}, " +
                "GraphMessageId: {GraphMessageId}",
                communicationId, graphMessageId);
        }

        // ── Step 5: Process attachments ──────────────────────────────────────────
        // Process attachments when: account has AutoCreateRecords enabled, OR account
        // could not be resolved (default to processing rather than silently dropping).
        var shouldProcessAttachments = account is null || account.AutoCreateRecords;
        if (shouldProcessAttachments && message.HasAttachments == true
            && message.Attachments is { Count: > 0 })
        {
            try
            {
                await ProcessIncomingAttachmentsAsync(
                    message.Attachments, communicationId, ct);
            }
            catch (Exception ex)
            {
                // Attachment processing failure is non-fatal
                _logger.LogWarning(
                    ex,
                    "Attachment processing failed (non-fatal) | CommunicationId: {CommunicationId}, " +
                    "GraphMessageId: {GraphMessageId}",
                    communicationId, graphMessageId);
            }
        }

        // ── Step 6: Archive .eml to SPE (best-effort, if opt-in) ─────────────────
        // Default to archiving if ArchiveIncomingOptIn is not set (null) or is true.
        if (account?.ArchiveIncomingOptIn != false)
        {
            try
            {
                await ArchiveEmlAsync(message, mailboxEmail, communicationId, ct);
            }
            catch (Exception ex)
            {
                // Archival failure is non-fatal
                _logger.LogWarning(
                    ex,
                    "EML archival failed (non-fatal) | CommunicationId: {CommunicationId}, " +
                    "GraphMessageId: {GraphMessageId}",
                    communicationId, graphMessageId);
            }
        }
        else
        {
            _logger.LogInformation(
                "EML archival skipped: ArchiveIncomingOptIn is disabled for account {Mailbox} | " +
                "CommunicationId: {CommunicationId}",
                mailboxEmail, communicationId);
        }

        // ── Step 7: Optionally mark message as read in Graph ─────────────────────
        try
        {
            await MarkAsReadAsync(mailboxEmail, graphMessageId, ct);
        }
        catch (Exception ex)
        {
            // Non-fatal; log and continue
            _logger.LogWarning(
                ex,
                "Failed to mark message as read (non-fatal) | Mailbox: {Mailbox}, " +
                "GraphMessageId: {GraphMessageId}",
                mailboxEmail, graphMessageId);
        }

        // ── Step 8: Inline notification (non-fatal) ────────────────────────────
        // Notify the matter owner when a new email is received for their matter.
        // Requires a resolved regarding matter with an owning user.
        try
        {
            await SendEmailReceivedNotificationAsync(communicationId, message, ct);
        }
        catch (Exception ex)
        {
            // Notification failure is non-fatal
            _logger.LogWarning(
                ex,
                "Email-received notification failed (non-fatal) | CommunicationId: {CommunicationId}, " +
                "GraphMessageId: {GraphMessageId}",
                communicationId, graphMessageId);
        }

        _logger.LogInformation(
            "Incoming communication processed successfully | CommunicationId: {CommunicationId}, " +
            "GraphMessageId: {GraphMessageId}, Mailbox: {Mailbox}",
            communicationId, graphMessageId, mailboxEmail);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Private helpers
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks if a sprk_communication record already exists with the given Graph message ID.
    /// Uses QueryByAttribute for efficient server-side filtering.
    /// </summary>
    private async Task<bool> ExistsByGraphMessageIdAsync(string graphMessageId, CancellationToken ct)
    {
        // Deduplication strategy (multi-layer):
        //   Layer 1: In-memory ConcurrentDictionary in webhook endpoint (same-process dedup)
        //   Layer 2: ServiceBus IdempotencyKey (cross-process dedup)
        //   Layer 3: Dataverse query on sprk_graphmessageid (this method)
        //   Layer 4: Dataverse duplicate detection rule on sprk_graphmessageid (if configured)

        try
        {
            return await _communicationService.ExistsCommunicationByGraphMessageIdAsync(graphMessageId, ct);
        }
        catch (Exception ex)
        {
            // If the Dataverse query fails, log and return false to allow processing.
            // Other dedup layers (webhook cache, ServiceBus idempotency) still protect against duplicates.
            _logger.LogWarning(
                ex,
                "Dataverse dedup check failed for GraphMessageId {GraphMessageId}; " +
                "falling back to other dedup layers",
                graphMessageId);
            return false;
        }
    }

    /// <summary>
    /// Gets the receive-enabled communication account for a mailbox email.
    /// </summary>
    private async Task<CommunicationAccount?> GetReceiveAccountAsync(
        string mailboxEmail, CancellationToken ct)
    {
        try
        {
            var accounts = await _accountService.QueryReceiveEnabledAccountsAsync(ct);
            return accounts.FirstOrDefault(a =>
                string.Equals(a.EmailAddress, mailboxEmail, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to query receive-enabled accounts for {Mailbox}",
                mailboxEmail);
            return null;
        }
    }

    /// <summary>
    /// Creates a sprk_communication record for the incoming email.
    /// Sets all required fields per schema; does NOT set any regarding fields.
    /// </summary>
    private async Task<Guid> CreateCommunicationRecordAsync(
        Message message, string mailboxEmail, string graphMessageId, CancellationToken ct)
    {
        // Determine body content: prefer uniqueBody (stripped of reply/forward content) over full body
        var bodyContent = message.UniqueBody?.Content ?? message.Body?.Content ?? string.Empty;
        var bodyContentType = message.UniqueBody?.ContentType ?? message.Body?.ContentType;

        // Map CC recipients to semicolon-separated string
        var ccRecipients = message.CcRecipients?
            .Where(r => r.EmailAddress?.Address is not null)
            .Select(r => r.EmailAddress!.Address!)
            .ToArray() ?? Array.Empty<string>();

        // Map To recipients (for display; mailboxEmail is the receiving mailbox)
        var toRecipients = message.ToRecipients?
            .Where(r => r.EmailAddress?.Address is not null)
            .Select(r => r.EmailAddress!.Address!)
            .ToArray() ?? Array.Empty<string>();

        var communication = new DataverseEntity("sprk_communication")
        {
            ["sprk_name"] = $"Email: {TruncateTo(message.Subject ?? "(No Subject)", 200)}",
            ["sprk_communicationtype"] = new OptionSetValue((int)CommunicationType.Email), // 100000000
            ["statuscode"] = new OptionSetValue((int)CommunicationStatus.Delivered), // 659490003
            ["statecode"] = new OptionSetValue(0), // Active
            ["sprk_direction"] = new OptionSetValue((int)CommunicationDirection.Incoming), // 100000000
            ["sprk_bodyformat"] = new OptionSetValue(
                bodyContentType == BodyType.Html
                    ? (int)BodyFormat.HTML   // 100000001
                    : (int)BodyFormat.PlainText), // 100000000
            ["sprk_from"] = message.From?.EmailAddress?.Address ?? "unknown",
            ["sprk_to"] = toRecipients.Length > 0
                ? string.Join("; ", toRecipients)
                : mailboxEmail,
            ["sprk_subject"] = message.Subject ?? "(No Subject)",
            ["sprk_body"] = bodyContent,
            ["sprk_graphmessageid"] = graphMessageId,
            ["sprk_internetmessageid"] = message.InternetMessageId,
            ["sprk_sentat"] = message.ReceivedDateTime?.UtcDateTime ?? DateTime.UtcNow,
            ["sprk_receiveddate"] = message.ReceivedDateTime?.UtcDateTime ?? DateTime.UtcNow,

            // Note: Regarding fields (sprk_regardingmatter, sprk_regardingorganization,
            // sprk_regardingperson) are set in step 4.5 by IncomingAssociationResolver.
        };

        // Set CC if present
        if (ccRecipients.Length > 0)
        {
            communication["sprk_cc"] = string.Join("; ", ccRecipients);
        }

        // Set attachment flags
        if (message.HasAttachments == true)
        {
            communication["sprk_hasattachments"] = true;
            var attachmentCount = message.Attachments?
                .Count(a => a is FileAttachment) ?? 0;
            if (attachmentCount > 0)
            {
                communication["sprk_attachmentcount"] = attachmentCount;
            }
        }

        return await _genericEntityService.CreateAsync(communication, ct);
    }

    /// <summary>
    /// Processes incoming email attachments: filters signature images, uploads to SPE,
    /// and creates sprk_communicationattachment records.
    /// Reuses EmailAttachmentProcessor for filtering and SPE upload logic.
    /// </summary>
    private async Task ProcessIncomingAttachmentsAsync(
        IList<Attachment> graphAttachments, Guid communicationId, CancellationToken ct)
    {
        var fileAttachments = graphAttachments
            .OfType<FileAttachment>()
            .Where(a => a.ContentBytes is { Length: > 0 })
            .ToList();

        if (fileAttachments.Count == 0)
        {
            _logger.LogDebug(
                "No file attachments to process for communication {CommunicationId}",
                communicationId);
            return;
        }

        _logger.LogInformation(
            "Processing {Count} file attachments for incoming communication {CommunicationId}",
            fileAttachments.Count, communicationId);

        var driveId = _options.ArchiveContainerId;
        if (string.IsNullOrWhiteSpace(driveId))
        {
            _logger.LogWarning(
                "ArchiveContainerId not configured; skipping attachment processing for {CommunicationId}",
                communicationId);
            return;
        }

        var processedCount = 0;

        foreach (var attachment in fileAttachments)
        {
            var fileName = attachment.Name ?? $"attachment_{processedCount + 1}";
            var contentType = attachment.ContentType ?? "application/octet-stream";
            var sizeBytes = attachment.ContentBytes?.Length ?? 0;

            // Use EmailAttachmentProcessor's filter logic to skip signature images etc.
            if (_attachmentProcessor.ShouldFilterAttachment(fileName, sizeBytes, contentType))
            {
                _logger.LogDebug(
                    "Filtered attachment '{FileName}' ({Size} bytes) for communication {CommunicationId}",
                    fileName, sizeBytes, communicationId);
                continue;
            }

            try
            {
                // Upload attachment to SPE
                var spePath = $"/communications/{communicationId:N}/attachments/{fileName}";
                using var stream = new MemoryStream(attachment.ContentBytes!);
                var fileHandle = await _speFileStore.UploadSmallAsync(driveId, spePath, stream, ct);

                // Create sprk_document record for the attachment (mirrors outbound pattern)
                Guid? attachmentDocumentId = null;
                if (fileHandle?.Id is not null)
                {
                    var attachmentDoc = new DataverseEntity("sprk_document")
                    {
                        ["sprk_documentname"] = TruncateTo(fileName, 200),
                        ["sprk_filename"] = fileName, // AI analyzer reads this for file type detection
                        ["sprk_documenttype"] = new OptionSetValue(100000006), // Email
                        ["sprk_sourcetype"] = new OptionSetValue(659490004), // Email Attachment
                        ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
                        ["sprk_graphitemid"] = fileHandle.Id,
                        ["sprk_graphdriveid"] = driveId,
                    };

                    attachmentDocumentId = await _genericEntityService.CreateAsync(attachmentDoc, ct);

                    _logger.LogInformation(
                        "Created sprk_document for inbound attachment | DocumentId: {DocumentId}, FileName: {FileName}, CommunicationId: {CommunicationId}",
                        attachmentDocumentId, fileName, communicationId);

                    // Enqueue AI analysis for the attachment (best-effort)
                    await EnqueueDocumentAnalysisAsync(attachmentDocumentId.Value, communicationId, ct);

                    // Enqueue RAG indexing for semantic search (best-effort)
                    await EnqueueRagIndexingAsync(driveId, fileHandle.Id, attachmentDocumentId.Value, fileName, communicationId, ct);
                }

                // Create sprk_communicationattachment record
                var attachmentRecord = new DataverseEntity("sprk_communicationattachment")
                {
                    ["sprk_name"] = TruncateTo(fileName, 200),
                    ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
                    ["sprk_attachmenttype"] = new OptionSetValue(100000000), // File
                };

                // Link to SPE if upload succeeded
                if (fileHandle?.Id is not null)
                {
                    attachmentRecord["sprk_graphitemid"] = fileHandle.Id;
                    attachmentRecord["sprk_graphdriveid"] = driveId;
                }

                // Link to document record if created
                if (attachmentDocumentId.HasValue)
                {
                    attachmentRecord["sprk_document"] = new EntityReference("sprk_document", attachmentDocumentId.Value);
                }

                await _genericEntityService.CreateAsync(attachmentRecord, ct);
                processedCount++;

                _logger.LogDebug(
                    "Processed attachment '{FileName}' for communication {CommunicationId}",
                    fileName, communicationId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to process attachment '{FileName}' for communication {CommunicationId} (non-fatal)",
                    fileName, communicationId);
                // Continue with remaining attachments
            }
        }

        _logger.LogInformation(
            "Attachment processing complete for communication {CommunicationId}: " +
            "{Processed}/{Total} attachments processed",
            communicationId, processedCount, fileAttachments.Count);
    }

    /// <summary>
    /// Archives the incoming email as a .eml file in SPE and creates a sprk_document record.
    /// Follows the same pattern as CommunicationService.ArchiveToSpeAsync.
    /// </summary>
    private async Task ArchiveEmlAsync(
        Message message, string mailboxEmail, Guid communicationId, CancellationToken ct)
    {
        var driveId = _options.ArchiveContainerId;
        if (string.IsNullOrWhiteSpace(driveId))
        {
            _logger.LogWarning(
                "ArchiveContainerId not configured; skipping .eml archival for {CommunicationId}",
                communicationId);
            return;
        }

        // Use GraphMessageToEmlConverter for proper RFC 2822 .eml with preserved headers
        // (InternetMessageId, In-Reply-To, References) and inline attachments
        var emlResult = _emlConverter.ConvertToEml(message);

        var spePath = $"/communications/{communicationId:N}/{emlResult.FileName}";
        using var emlStream = new MemoryStream(emlResult.Content);
        var fileHandle = await _speFileStore.UploadSmallAsync(driveId, spePath, emlStream, ct);

        _logger.LogInformation(
            "Archived incoming communication .eml to SPE | CommunicationId: {CommunicationId}, Path: {Path}",
            communicationId, spePath);

        // Create sprk_document record linking to the archived file
        var senderEmail = message.From?.EmailAddress?.Address;
        var recipientEmails = message.ToRecipients?
            .Where(r => r.EmailAddress?.Address is not null)
            .Select(r => r.EmailAddress!.Address!)
            .ToArray();

        var document = new DataverseEntity("sprk_document")
        {
            ["sprk_documentname"] = $"Archived: {TruncateTo(message.Subject ?? "(No Subject)", 180)}",
            ["sprk_filename"] = emlResult.FileName, // e.g., "email-2026-03-12.eml" — AI analyzer reads this for file type
            ["sprk_documenttype"] = new OptionSetValue(100000006), // Email
            ["sprk_sourcetype"] = new OptionSetValue(659490003), // Email Archive
            ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
            ["sprk_graphitemid"] = fileHandle?.Id,
            ["sprk_graphdriveid"] = driveId,
            ["sprk_isemailarchive"] = true,
            ["sprk_emailsubject"] = message.Subject ?? "(No Subject)",
            ["sprk_emaildirection"] = new OptionSetValue(100000000), // Received
        };

        if (!string.IsNullOrEmpty(senderEmail))
            document["sprk_emailfrom"] = senderEmail;
        if (recipientEmails is { Length: > 0 })
            document["sprk_emailto"] = string.Join("; ", recipientEmails);
        if (message.ReceivedDateTime.HasValue)
            document["sprk_emaildate"] = message.ReceivedDateTime.Value.DateTime;

        var documentId = await _genericEntityService.CreateAsync(document, ct);

        _logger.LogInformation(
            "Created sprk_document record for archived .eml | DocumentId: {DocumentId}, " +
            "CommunicationId: {CommunicationId}",
            documentId, communicationId);

        // Enqueue AI analysis for the archived .eml (best-effort)
        await EnqueueDocumentAnalysisAsync(documentId, communicationId, ct);

        // Enqueue RAG indexing for semantic search (best-effort)
        if (fileHandle?.Id is not null)
        {
            await EnqueueRagIndexingAsync(driveId, fileHandle.Id, documentId, emlResult.FileName, communicationId, ct);
        }
    }

    /// <summary>
    /// Marks the Graph message as read after processing.
    /// This prevents the message from being picked up again by backup polling.
    /// </summary>
    private async Task MarkAsReadAsync(string mailboxEmail, string graphMessageId, CancellationToken ct)
    {
        var graphClient = _graphClientFactory.ForApp();

        await graphClient.Users[mailboxEmail]
            .Messages[graphMessageId]
            .PatchAsync(new Message { IsRead = true }, cancellationToken: ct);

        _logger.LogDebug(
            "Marked message as read | Mailbox: {Mailbox}, GraphMessageId: {GraphMessageId}",
            mailboxEmail, graphMessageId);
    }

    /// <summary>
    /// Enqueues an AppOnlyDocumentAnalysis job for a document record (best-effort).
    /// Mirrors CommunicationService.EnqueueDocumentAnalysisAsync for inbound parity.
    /// </summary>
    private async Task EnqueueDocumentAnalysisAsync(Guid documentId, Guid communicationId, CancellationToken ct)
    {
        try
        {
            var aiJob = new JobContract
            {
                JobId = Guid.NewGuid(),
                JobType = AppOnlyDocumentAnalysisJobHandler.JobTypeName,
                SubjectId = documentId.ToString(),
                CorrelationId = communicationId.ToString("N"),
                IdempotencyKey = $"DocAnalysis:{documentId}",
                Attempt = 1,
                MaxAttempts = 2,
                CreatedAt = DateTimeOffset.UtcNow,
                Payload = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    DocumentId = documentId,
                    Source = "InboundEmailArchival",
                    EnqueuedAt = DateTimeOffset.UtcNow
                }))
            };

            await _jobSubmissionService.SubmitJobAsync(aiJob, ct);

            _logger.LogInformation(
                "Enqueued AI analysis job {JobId} for inbound document {DocumentId} | CommunicationId: {CommunicationId}",
                aiJob.JobId, documentId, communicationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to enqueue AI analysis for inbound document {DocumentId} (non-fatal) | CommunicationId: {CommunicationId}",
                documentId, communicationId);
        }
    }

    /// <summary>
    /// Enqueues a RAG indexing job for a document to enable semantic search.
    /// Mirrors UploadFinalizationWorker.EnqueueRagIndexingAsync pattern.
    /// </summary>
    private async Task EnqueueRagIndexingAsync(
        string driveId, string itemId, Guid documentId, string fileName,
        Guid communicationId, CancellationToken ct)
    {
        try
        {
            var tenantId = _configuration["TENANT_ID"] ?? _configuration["AzureAd:TenantId"] ?? "";

            var indexingJob = new JobContract
            {
                JobId = Guid.NewGuid(),
                JobType = RagIndexingJobHandler.JobTypeName,
                SubjectId = documentId.ToString(),
                CorrelationId = communicationId.ToString("N"),
                IdempotencyKey = $"rag-index-{driveId}-{itemId}",
                Attempt = 1,
                MaxAttempts = 3,
                CreatedAt = DateTimeOffset.UtcNow,
                Payload = JsonDocument.Parse(JsonSerializer.Serialize(new RagIndexingJobPayload
                {
                    TenantId = tenantId,
                    DriveId = driveId,
                    ItemId = itemId,
                    FileName = fileName,
                    DocumentId = documentId.ToString(),
                    Source = "InboundEmail",
                    EnqueuedAt = DateTimeOffset.UtcNow
                }))
            };

            await _jobSubmissionService.SubmitJobAsync(indexingJob, ct);

            _logger.LogInformation(
                "Enqueued RAG indexing job {JobId} for inbound document {DocumentId} (file: {FileName}) | CommunicationId: {CommunicationId}",
                indexingJob.JobId, documentId, fileName, communicationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to enqueue RAG indexing for inbound document {DocumentId} (non-fatal) | CommunicationId: {CommunicationId}",
                documentId, communicationId);
        }
    }

    /// <summary>
    /// Sends an in-app notification to the matter owner when a new email is received.
    /// Retrieves the communication record to find the resolved regarding matter,
    /// then queries the matter for its owning user.
    /// </summary>
    private async Task SendEmailReceivedNotificationAsync(
        Guid communicationId, Message message, CancellationToken ct)
    {
        // Retrieve the communication record to check if a matter was resolved
        var commRecord = await _genericEntityService.RetrieveAsync(
            "sprk_communication", communicationId,
            ["sprk_regardingmatter"], ct);

        var matterRef = commRecord.GetAttributeValue<EntityReference>("sprk_regardingmatter");
        if (matterRef is null)
        {
            _logger.LogDebug(
                "No regarding matter on communication {CommunicationId}; skipping email-received notification",
                communicationId);
            return;
        }

        // Retrieve the matter to get the owning user
        var matterRecord = await _genericEntityService.RetrieveAsync(
            "sprk_matter", matterRef.Id,
            ["ownerid", "sprk_mattername"], ct);

        var ownerRef = matterRecord.GetAttributeValue<EntityReference>("ownerid");
        if (ownerRef is null || ownerRef.LogicalName != "systemuser")
        {
            _logger.LogDebug(
                "Matter {MatterId} has no systemuser owner; skipping email-received notification",
                matterRef.Id);
            return;
        }

        var sender = message.From?.EmailAddress?.Address ?? "unknown sender";
        var subject = message.Subject ?? "(No Subject)";
        var matterName = matterRecord.GetAttributeValue<string>("sprk_mattername") ?? "a matter";

        var title = $"New email received for {matterName}";
        var body = $"From: {sender}\nSubject: {subject}";

        await _notificationService.CreateNotificationAsync(
            userId: ownerRef.Id,
            title: title,
            body: body,
            category: "email",
            regardingId: communicationId,
            cancellationToken: ct);

        _logger.LogInformation(
            "Email-received notification sent | CommunicationId: {CommunicationId}, " +
            "MatterId: {MatterId}, UserId: {UserId}",
            communicationId, matterRef.Id, ownerRef.Id);
    }

    /// <summary>
    /// Extracts the mailbox email from a Graph resource path.
    /// Resource format: "users/{email}/mailFolders/{folder}/messages/{id}"
    ///               or "users/{email}/messages/{id}"
    /// </summary>
    private static string? ExtractMailboxFromResource(string? resource)
    {
        if (string.IsNullOrEmpty(resource))
            return null;

        const string prefix = "users/";
        var startIndex = resource.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
            return null;

        var emailStart = startIndex + prefix.Length;
        var nextSlash = resource.IndexOf('/', emailStart);

        var extracted = nextSlash > emailStart
            ? resource[emailStart..nextSlash]
            : resource[emailStart..];

        // Only return if it looks like an email (not a GUID)
        return extracted.Contains('@') ? extracted : null;
    }

    private static string TruncateTo(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
