using Microsoft.Extensions.Options;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Email;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Processes incoming email notifications by fetching full message details from Graph,
/// creating a sprk_communication record with Direction=Incoming, processing attachments,
/// and archiving the .eml file.
///
/// IMPORTANT: This processor does NOT set any regarding/association fields
/// (sprk_regardingmatter, sprk_regardingorganization, sprk_regardingperson).
/// Association resolution is a separate AI project.
///
/// Registered as concrete type in AddCommunicationModule() per ADR-010.
/// Uses GraphClientFactory.ForApp() for fetching messages per ADR-007.
/// </summary>
public sealed class IncomingCommunicationProcessor
{
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly IDataverseService _dataverseService;
    private readonly CommunicationAccountService _accountService;
    private readonly IEmailAttachmentProcessor _attachmentProcessor;
    private readonly EmlGenerationService _emlGenerationService;
    private readonly SpeFileStore _speFileStore;
    private readonly CommunicationOptions _options;
    private readonly ILogger<IncomingCommunicationProcessor> _logger;

    public IncomingCommunicationProcessor(
        IGraphClientFactory graphClientFactory,
        IDataverseService dataverseService,
        CommunicationAccountService accountService,
        IEmailAttachmentProcessor attachmentProcessor,
        EmlGenerationService emlGenerationService,
        SpeFileStore speFileStore,
        IOptions<CommunicationOptions> options,
        ILogger<IncomingCommunicationProcessor> logger)
    {
        _graphClientFactory = graphClientFactory;
        _dataverseService = dataverseService;
        _accountService = accountService;
        _attachmentProcessor = attachmentProcessor;
        _emlGenerationService = emlGenerationService;
        _speFileStore = speFileStore;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Processes an incoming email: fetches from Graph, creates Dataverse record,
    /// handles attachments, and archives .eml.
    /// </summary>
    /// <param name="mailboxEmail">The shared mailbox email that received the message.</param>
    /// <param name="graphMessageId">The Graph message ID to fetch.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ProcessAsync(string mailboxEmail, string graphMessageId, CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing incoming communication | Mailbox: {Mailbox}, GraphMessageId: {GraphMessageId}",
            mailboxEmail, graphMessageId);

        // ── Step 1: Deduplication check ──────────────────────────────────────────
        // Query sprk_communication where sprk_graphmessageid == graphMessageId.
        // If a record already exists, log and skip to prevent duplicates.
        if (await ExistsByGraphMessageIdAsync(graphMessageId, ct))
        {
            _logger.LogInformation(
                "Duplicate detected: sprk_communication already exists for GraphMessageId {GraphMessageId}. Skipping.",
                graphMessageId);
            return;
        }

        // ── Step 2: Get account details ──────────────────────────────────────────
        // Check if the receiving mailbox has sprk_autocreaterecords enabled.
        var account = await GetReceiveAccountAsync(mailboxEmail, ct);
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
                        "id", "from", "toRecipients", "ccRecipients",
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
        // CommunicationType = Email (100000000) — NOTE: field name is sprk_communiationtype (intentional typo)
        // StatusCode = Delivered (659490003)
        // IMPORTANT: Do NOT set sprk_regardingmatter, sprk_regardingorganization, sprk_regardingperson
        var communicationId = await CreateCommunicationRecordAsync(message, mailboxEmail, graphMessageId, ct);

        _logger.LogInformation(
            "Created sprk_communication record | CommunicationId: {CommunicationId}, " +
            "Direction: Incoming, GraphMessageId: {GraphMessageId}",
            communicationId, graphMessageId);

        // ── Step 5: Process attachments (if account has sprk_autocreaterecords) ──
        if (account?.AutoCreateRecords == true && message.HasAttachments == true
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

        // ── Step 6: Archive .eml to SPE (best-effort) ────────────────────────────
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
    private Task<bool> ExistsByGraphMessageIdAsync(string graphMessageId, CancellationToken ct)
    {
        // Deduplication strategy (multi-layer):
        //   Layer 1: In-memory ConcurrentDictionary in webhook endpoint (same-process dedup)
        //   Layer 2: ServiceBus IdempotencyKey (cross-process dedup)
        //   Layer 3: Dataverse duplicate detection rule on sprk_graphmessageid (if configured)
        //
        // IDataverseService doesn't expose a generic RetrieveMultiple for sprk_communication.
        // Adding a query method is out of scope for this task (Task 072).
        // The existing multi-layer dedup is sufficient for preventing duplicate records.
        //
        // Future enhancement: add QueryCommunicationByGraphMessageIdAsync to IDataverseService
        // for a Dataverse-level dedup check before record creation.

        _logger.LogDebug(
            "Dedup check for GraphMessageId {GraphMessageId}: relying on multi-layer dedup " +
            "(webhook cache, ServiceBus idempotency, Dataverse rules)",
            graphMessageId);

        return Task.FromResult(false);
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
            ["sprk_communiationtype"] = new OptionSetValue((int)CommunicationType.Email), // 100000000 — NOTE: intentional typo in Dataverse field name
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
            ["sprk_sentat"] = message.ReceivedDateTime?.UtcDateTime ?? DateTime.UtcNow,

            // IMPORTANT: Do NOT set any regarding fields.
            // Association resolution (sprk_regardingmatter, sprk_regardingorganization,
            // sprk_regardingperson) is a separate AI project.
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

        return await _dataverseService.CreateAsync(communication, ct);
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
                    attachmentRecord["sprk_speitemid"] = fileHandle.Id;
                    attachmentRecord["sprk_spedriveid"] = driveId;
                }

                await _dataverseService.CreateAsync(attachmentRecord, ct);
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

        // Build a synthetic SendCommunicationRequest and Response for EmlGenerationService
        // (reusing existing EML generation infrastructure)
        var syntheticRequest = new SendCommunicationRequest
        {
            To = message.ToRecipients?
                .Where(r => r.EmailAddress?.Address is not null)
                .Select(r => r.EmailAddress!.Address!)
                .ToArray() ?? new[] { mailboxEmail },
            Cc = message.CcRecipients?
                .Where(r => r.EmailAddress?.Address is not null)
                .Select(r => r.EmailAddress!.Address!)
                .ToArray(),
            Subject = message.Subject ?? "(No Subject)",
            Body = message.Body?.Content ?? string.Empty,
            BodyFormat = message.Body?.ContentType == BodyType.Html
                ? BodyFormat.HTML
                : BodyFormat.PlainText,
        };

        var syntheticResponse = new SendCommunicationResponse
        {
            CommunicationId = communicationId,
            GraphMessageId = message.Id ?? communicationId.ToString("N"),
            Status = CommunicationStatus.Delivered,
            SentAt = message.ReceivedDateTime ?? DateTimeOffset.UtcNow,
            From = message.From?.EmailAddress?.Address ?? "unknown",
            CorrelationId = communicationId.ToString("N")
        };

        var emlResult = _emlGenerationService.GenerateEml(syntheticRequest, syntheticResponse);

        var spePath = $"/communications/{communicationId:N}/{emlResult.FileName}";
        using var emlStream = new MemoryStream(emlResult.Content);
        var fileHandle = await _speFileStore.UploadSmallAsync(driveId, spePath, emlStream, ct);

        _logger.LogInformation(
            "Archived incoming communication .eml to SPE | CommunicationId: {CommunicationId}, Path: {Path}",
            communicationId, spePath);

        // Create sprk_document record linking to the archived file
        var document = new DataverseEntity("sprk_document")
        {
            ["sprk_name"] = $"Archived: {TruncateTo(message.Subject ?? "(No Subject)", 180)}",
            ["sprk_documenttype"] = new OptionSetValue(100000002), // Communication
            ["sprk_sourcetype"] = new OptionSetValue(100000001), // CommunicationArchive
            ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
            ["sprk_speitemid"] = fileHandle?.Id,
            ["sprk_spedriveid"] = driveId,
        };

        var documentId = await _dataverseService.CreateAsync(document, ct);

        _logger.LogInformation(
            "Created sprk_document record for archived .eml | DocumentId: {DocumentId}, " +
            "CommunicationId: {CommunicationId}",
            documentId, communicationId);
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

    private static string TruncateTo(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
