using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai.Jobs;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Core communication service that validates requests, resolves approved senders, and orchestrates the
/// send lifecycle (Dataverse tracking record, SPE archival, enrichment). Channel-specific transmit and
/// archival are delegated to the channel seams (ADR-045 rule 4) via <see cref="CommunicationChannelDispatcher"/>,
/// so this orchestrator is free of provider (Microsoft.Graph) types — a future channel is added by
/// registering a new sender/archiver, with no change here (NFR-04).
/// </summary>
public sealed class CommunicationService
{
    private readonly CommunicationChannelDispatcher _channelDispatcher;
    private readonly ApprovedSenderValidator _senderValidator;
    private readonly ICommunicationDataverseService _communicationService;
    private readonly IGenericEntityService _genericEntityService;
    private readonly IDocumentDataverseService _documentService;
    private readonly SpeFileStore _speFileStore;
    private readonly CommunicationAccountService _accountService;
    private readonly JobSubmissionService _jobSubmissionService;
    private readonly ICommunicationEnrichmentService _enrichmentService;
    private readonly IThreadResolver? _threadResolver;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IDirectThreadAccessService? _directThreadAccess;
    private readonly CommunicationOptions _options;
    private readonly ILogger<CommunicationService> _logger;

    /// <param name="threadResolver">
    /// Direction-symmetric thread resolver (task 040 / FR-06) — optional so existing unit constructions that
    /// predate it keep compiling; production DI always supplies it. Invoked best-effort on send to join a
    /// reply's parent thread (email ancestry) or create a fresh thread (NFR-02, non-fatal).
    /// </param>
    /// <param name="scopeFactory">
    /// Scope factory used to reach the Scoped <see cref="IIdempotencyService"/> from this Singleton service
    /// (the codebase's canonical Singleton→Scoped bridge). Used on the outbound MESSAGE path (task 051 / FR-04)
    /// to mark our own ACS message id processed so the Event Grid echo of our outbound message is a no-op inbound
    /// (echo-dedup). Optional so existing unit constructions keep compiling; production DI always supplies it.
    /// </param>
    /// <param name="directThreadAccess">
    /// Direct 1:1 thread access mechanics (task 043 / FR-09) — invoked best-effort right after an outbound
    /// MESSAGE persists into a Direct-topology thread, to grant that ONE message Read access to the thread's
    /// explicit participants (the CRITICAL RISK task 043 closes: <c>sprk_communication</c> is User-level and
    /// every row is owned by the app-only identity, so without this grant neither participant can read a
    /// Direct-thread message via the impersonated read, task 050). No-op for a non-Direct thread. Optional so
    /// existing unit constructions keep compiling; production DI always supplies it.
    /// </param>
    public CommunicationService(
        CommunicationChannelDispatcher channelDispatcher,
        ApprovedSenderValidator senderValidator,
        ICommunicationDataverseService communicationService,
        IGenericEntityService genericEntityService,
        IDocumentDataverseService documentService,
        SpeFileStore speFileStore,
        CommunicationAccountService accountService,
        JobSubmissionService jobSubmissionService,
        ICommunicationEnrichmentService enrichmentService,
        IOptions<CommunicationOptions> options,
        ILogger<CommunicationService> logger,
        IThreadResolver? threadResolver = null,
        IServiceScopeFactory? scopeFactory = null,
        IDirectThreadAccessService? directThreadAccess = null)
    {
        _channelDispatcher = channelDispatcher;
        _senderValidator = senderValidator;
        _communicationService = communicationService;
        _genericEntityService = genericEntityService;
        _documentService = documentService;
        _speFileStore = speFileStore;
        _accountService = accountService;
        _jobSubmissionService = jobSubmissionService;
        _enrichmentService = enrichmentService;
        _threadResolver = threadResolver;
        _scopeFactory = scopeFactory;
        _directThreadAccess = directThreadAccess;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Archives an EXISTING communication on demand (task 044 — the "Save to SharePoint" action).
    /// Reuses the same archival path as send/receive: generates the <c>.eml</c> via the channel
    /// archiver seam, uploads it to SPE, and creates the archive <c>sprk_document</c> plus a
    /// <c>sprk_document</c> for each attachment that does not already have one. Reconstructs the
    /// <see cref="SendCommunicationRequest"/>/<see cref="SendCommunicationResponse"/> from the stored
    /// record (there is no live Graph message on this path).
    ///
    /// Idempotent: if an email-archive Document already exists for this communication (the auto-archival
    /// created it on send/receive), returns <see cref="ArchiveCommunicationResult.AlreadyArchived"/> = true
    /// without creating duplicates. Attachment Documents are only created for attachments that lack one.
    /// </summary>
    public async Task<ArchiveCommunicationResult> ArchiveExistingAsync(Guid communicationId, CancellationToken ct)
    {
        // 1. Load the persisted communication.
        DataverseEntity record;
        try
        {
            record = await _genericEntityService.RetrieveAsync(
                "sprk_communication",
                communicationId,
                new[] { "sprk_subject", "sprk_from", "sprk_to", "sprk_cc", "sprk_body", "sprk_communicationtype", "sprk_direction", "sprk_sentat", "sprk_graphmessageid", "statuscode" },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Communication {CommunicationId} not found for archive", communicationId);
            throw new SdapProblemException(
                code: "COMMUNICATION_NOT_FOUND",
                title: "Communication not found",
                detail: $"Communication with ID '{communicationId}' does not exist.",
                statusCode: 404);
        }

        // 2. Archive any attachments that lack a Document — run INDEPENDENTLY of the .eml
        // gate so an attachment added after a prior archive is still picked up on a later
        // call (code-review W3/S5). Each created Document is linked back onto its attachment.
        var attachmentsCreated = await ArchiveExistingAttachmentsAsync(communicationId, ct);

        // 3. Idempotency for the .eml archive — already archived? Return the existing archive
        // Document (plus any newly-archived attachments above), no .eml duplicate.
        var existingArchiveId = await FindExistingArchiveDocumentAsync(communicationId, ct);
        if (existingArchiveId.HasValue)
        {
            _logger.LogInformation(
                "Communication {CommunicationId} .eml already archived (Document {DocumentId}); returning idempotently",
                communicationId, existingArchiveId);
            return new ArchiveCommunicationResult
            {
                CommunicationId = communicationId,
                ArchiveDocumentId = existingArchiveId,
                AlreadyArchived = true,
                AttachmentDocumentsCreated = attachmentsCreated,
            };
        }

        // 4. Reconstruct the request/response the archiver expects from the stored record.
        var to = SplitRecipients(record.GetAttributeValue<string>("sprk_to"));
        var cc = SplitRecipients(record.GetAttributeValue<string>("sprk_cc"));
        var from = record.GetAttributeValue<string>("sprk_from") ?? string.Empty;
        var typeValue = record.GetAttributeValue<OptionSetValue>("sprk_communicationtype")?.Value ?? (int)CommunicationType.Email;
        var commType = Enum.IsDefined(typeof(CommunicationType), typeValue) ? (CommunicationType)typeValue : CommunicationType.Email;
        var sentAtDt = record.GetAttributeValue<DateTime?>("sprk_sentat");
        var sentAt = sentAtDt.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(sentAtDt.Value, DateTimeKind.Utc), TimeSpan.Zero)
            : DateTimeOffset.UtcNow;
        var statusValue = record.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? (int)CommunicationStatus.Draft;

        // Map communication direction (Incoming=100000000) → archive-Document email direction
        // (Received=100000000 / Sent=100000001) so an on-demand archive of an INBOUND email is
        // not mislabeled "Sent" (code-review W1). Default (no direction / outbound) → Sent.
        var directionValue = record.GetAttributeValue<OptionSetValue>("sprk_direction")?.Value;
        var emailDirection = directionValue == 100000000 ? 100000000 : 100000001;

        var request = new SendCommunicationRequest
        {
            To = to.Length > 0 ? to : new[] { string.Empty },
            Cc = cc.Length > 0 ? cc : null,
            Subject = record.GetAttributeValue<string>("sprk_subject") ?? "(No Subject)",
            Body = record.GetAttributeValue<string>("sprk_body") ?? string.Empty,
            BodyFormat = BodyFormat.HTML,
            FromMailbox = string.IsNullOrEmpty(from) ? null : from,
            CommunicationType = commType,
        };
        var response = new SendCommunicationResponse
        {
            CommunicationId = communicationId,
            GraphMessageId = record.GetAttributeValue<string>("sprk_graphmessageid") ?? string.Empty,
            Status = Enum.IsDefined(typeof(CommunicationStatus), statusValue) ? (CommunicationStatus)statusValue : CommunicationStatus.Delivered,
            SentAt = sentAt,
            From = from,
        };

        // 5. Archive the .eml (reuses the same path as send-side archival; ADR-045 archiver seam),
        // stamping the correct direction on the archive Document.
        Guid archiveDocumentId;
        try
        {
            archiveDocumentId = await ArchiveToSpeAsync(request, response, communicationId, ct, emailDirection);
        }
        catch (InvalidOperationException ex)
        {
            throw new SdapProblemException(
                code: "ARCHIVE_NOT_CONFIGURED",
                title: "Archival not configured",
                detail: ex.Message,
                statusCode: 500);
        }

        return new ArchiveCommunicationResult
        {
            CommunicationId = communicationId,
            ArchiveDocumentId = archiveDocumentId,
            AlreadyArchived = false,
            AttachmentDocumentsCreated = attachmentsCreated,
        };
    }

    /// <summary>Split a Dataverse recipient string (";"- or ","-separated) into trimmed, non-empty addresses.</summary>
    private static string[] SplitRecipients(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? Array.Empty<string>()
            : raw.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Reconstructs the channel-neutral <see cref="NormalizedMessage"/> envelope + <see cref="AssociationContext"/>
    /// from a STORED <c>sprk_communication</c> record (task 074, Path C — the on-demand suggestion path). Reuses
    /// the same record-read + <see cref="SplitRecipients"/> plumbing as <see cref="ArchiveExistingAsync"/>; there
    /// is no live Graph message here. <b>Read-only</b> — never writes. Throws <see cref="SdapProblemException"/>
    /// (404) when the record does not exist (mirrors the archive/status not-found mapping).
    /// </summary>
    /// <param name="communicationId">The <c>sprk_communication</c> record to reconstruct from.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<(NormalizedMessage Message, AssociationContext Context)> ReconstructEnvelopeAsync(
        Guid communicationId, CancellationToken ct)
    {
        DataverseEntity record;
        try
        {
            // Only columns that exist on sprk_communication (verified against IncomingCommunicationProcessor
            // + CreateDataverseRecord*). sprk_conversationid is NOT persisted yet (see ThreadContinuityRung),
            // and there is no sprk_references column — so thread continuity re-evaluates via In-Reply-To /
            // Internet-Message-Id only, exactly as the inbound path stamped them.
            record = await _genericEntityService.RetrieveAsync(
                "sprk_communication",
                communicationId,
                new[]
                {
                    "sprk_subject", "sprk_from", "sprk_to", "sprk_cc", "sprk_body", "sprk_bodyformat",
                    "sprk_communicationtype", "sprk_direction", "sprk_sentat",
                    "sprk_internetmessageid", "sprk_inreplyto", "sprk_graphmessageid",
                },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Communication {CommunicationId} not found for suggestion", communicationId);
            throw new SdapProblemException(
                code: "COMMUNICATION_NOT_FOUND",
                title: "Communication not found",
                detail: $"Communication with ID '{communicationId}' does not exist.",
                statusCode: 404);
        }

        var to = SplitRecipients(record.GetAttributeValue<string>("sprk_to"));
        var cc = SplitRecipients(record.GetAttributeValue<string>("sprk_cc"));

        // Direction: Outgoing when explicitly stamped outbound; otherwise Incoming (the engine decision is
        // direction-symmetric — direction is recorded in provenance, not branched on).
        var directionValue = record.GetAttributeValue<OptionSetValue>("sprk_direction")?.Value;
        var direction = directionValue == (int)CommunicationDirection.Outgoing
            ? CommunicationDirection.Outgoing
            : CommunicationDirection.Incoming;

        var body = record.GetAttributeValue<string>("sprk_body");
        var isHtml = record.GetAttributeValue<OptionSetValue>("sprk_bodyformat")?.Value == (int)BodyFormat.HTML;

        var sentAtDt = record.GetAttributeValue<DateTime?>("sprk_sentat");
        var sentAt = sentAtDt.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(sentAtDt.Value, DateTimeKind.Utc), TimeSpan.Zero)
            : (DateTimeOffset?)null;

        var message = new NormalizedMessage
        {
            Direction = direction,
            From = record.GetAttributeValue<string>("sprk_from"),
            To = to,
            Cc = cc,
            Subject = record.GetAttributeValue<string>("sprk_subject"),
            BodyText = isHtml ? null : body,
            BodyHtml = isHtml ? body : null,
            InternetMessageId = record.GetAttributeValue<string>("sprk_internetmessageid"),
            InReplyTo = record.GetAttributeValue<string>("sprk_inreplyto"),
            SentAt = sentAt,
        };

        // Mirror IncomingCommunicationProcessor's AssociationContext construction. On this stored-record path
        // there is no live mailbox account or tenant key to hand the engine, so both are left null (Account is
        // unused by the deterministic rungs; a null TenantKey ⇒ the global auto-file kill-switch applies).
        var context = new AssociationContext();

        return (message, context);
    }

    /// <summary>
    /// Returns the id of an existing email-archive <c>sprk_document</c> for the communication, or null.
    /// Used for idempotent on-demand archival (task 044).
    /// </summary>
    private async Task<Guid?> FindExistingArchiveDocumentAsync(Guid communicationId, CancellationToken ct)
    {
        var query = new QueryExpression("sprk_document")
        {
            ColumnSet = new ColumnSet("sprk_documentid"),
            TopCount = 1,
            Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("sprk_communication", ConditionOperator.Equal, communicationId),
                    new ConditionExpression("sprk_isemailarchive", ConditionOperator.Equal, true),
                },
            },
        };

        var result = await _genericEntityService.RetrieveMultipleAsync(query, ct);
        return result.Entities.Count > 0 ? result.Entities[0].Id : null;
    }

    /// <summary>
    /// Creates a <c>sprk_document</c> for each attachment of the communication that lacks one (has an SPE
    /// item id but no linked document), using the attachment's OWN drive id (code-review W2), links the
    /// created Document back onto the attachment so a later archive skips it (idempotency, code-review W3),
    /// and enqueues AI analysis for parity with the send path. Returns the number archived. Per-attachment
    /// failures are non-fatal.
    /// </summary>
    private async Task<int> ArchiveExistingAttachmentsAsync(Guid communicationId, CancellationToken ct)
    {
        var query = new QueryExpression("sprk_communicationattachment")
        {
            ColumnSet = new ColumnSet("sprk_name", "sprk_graphitemid", "sprk_graphdriveid", "sprk_document"),
            Criteria =
            {
                Conditions = { new ConditionExpression("sprk_communication", ConditionOperator.Equal, communicationId) },
            },
        };

        var attachments = await _genericEntityService.RetrieveMultipleAsync(query, ct);
        var correlationId = Guid.NewGuid().ToString();
        var created = 0;

        foreach (var att in attachments.Entities)
        {
            // Skip attachments that already have a Document, or that were never uploaded to SPE.
            if (att.GetAttributeValue<EntityReference>("sprk_document") is not null) continue;
            var itemId = att.GetAttributeValue<string>("sprk_graphitemid");
            if (string.IsNullOrEmpty(itemId)) continue;

            // Prefer the attachment's own drive id; fall back to the archive container.
            var driveId = att.GetAttributeValue<string>("sprk_graphdriveid");
            if (string.IsNullOrEmpty(driveId)) driveId = _options.ArchiveContainerId;
            if (string.IsNullOrEmpty(driveId)) continue;

            var fileName = att.GetAttributeValue<string>("sprk_name") ?? "Attachment";

            try
            {
                var attachmentDoc = new DataverseEntity("sprk_document")
                {
                    ["sprk_documentname"] = TruncateTo(fileName, 200),
                    ["sprk_filename"] = fileName, // AI analyzer reads this for file type detection
                    ["sprk_documenttype"] = new OptionSetValue(100000006), // Email
                    ["sprk_sourcetype"] = new OptionSetValue(659490004), // Email Attachment
                    ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
                    ["sprk_graphitemid"] = itemId,
                    ["sprk_graphdriveid"] = driveId,
                };

                var documentId = await _genericEntityService.CreateAsync(attachmentDoc, ct);

                // Link the Document back onto the attachment so a later archive skips it (W3).
                await _genericEntityService.UpdateAsync(
                    "sprk_communicationattachment",
                    att.Id,
                    new Dictionary<string, object> { ["sprk_document"] = new EntityReference("sprk_document", documentId) },
                    ct);

                // Enqueue AI analysis (parity with the send-side attachment archival).
                await EnqueueDocumentAnalysisAsync(documentId, correlationId, ct);
                created++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to archive attachment '{FileName}' for communication {CommunicationId} (non-fatal)",
                    fileName, communicationId);
            }
        }

        return created;
    }

    /// <summary>
    /// Builds the normalized envelope (ADR-045 / FR-09) for an outbound send, so enrichment operates
    /// over <see cref="NormalizedMessage"/> — never a channel-specific type. Skeleton mapping for task 010;
    /// task 011 finalizes the envelope shape.
    /// </summary>
    private static NormalizedMessage BuildOutboundEnvelope(SendCommunicationRequest request, string fromEmail)
        => new()
        {
            Direction = CommunicationDirection.Outgoing,
            From = fromEmail,
            To = request.To ?? Array.Empty<string>(),
            Cc = request.Cc ?? Array.Empty<string>(),
            Subject = request.Subject,
            BodyText = request.BodyFormat == BodyFormat.PlainText ? request.Body : null,
            BodyHtml = request.BodyFormat == BodyFormat.PlainText ? null : request.Body,
            // NOTE: reply-thread fields (InReplyTo/References/ConversationId) are left null here — the
            // FR-06 send-path reply-thread columns are not present on this worktree branch (see ESCALATION
            // E6). Task 011 finalizes the envelope; the Association Engine will populate thread rung (1) then.
            SentAt = DateTimeOffset.UtcNow,
        };

    /// <summary>
    /// Resolves (find-or-create) the outbound message's thread and stamps <c>sprk_communicationthread</c>
    /// (task 040 / FR-06) — best-effort, non-fatal (NFR-02). For EMAIL, a reply (<c>InReplyToMessageId</c>
    /// set) joins its parent's thread and a fresh email creates one; the thread anchors to the record's
    /// regarding (mapped at create time, ADR-024 reuse). For MESSAGE (chat), the authoritative ACS
    /// <c>ChatThreadId</c> is carried by the inbound echo path (task 031/051), not surfaced here in R1, so
    /// the resolver skips — no duplicate thread. MUST NOT fail the send.
    /// </summary>
    private async Task ResolveOutboundThreadAsync(
        Guid communicationId, SendCommunicationRequest request, string correlationId, CancellationToken ct)
    {
        if (_threadResolver is null)
            return;

        try
        {
            var envelope = new NormalizedMessage
            {
                Direction = CommunicationDirection.Outgoing,
                Subject = request.Subject,
                InReplyTo = string.IsNullOrWhiteSpace(request.InReplyToMessageId) ? null : request.InReplyToMessageId,
            };

            await _threadResolver.ResolveAndAssignThreadAsync(
                new ThreadResolutionRequest
                {
                    CommunicationId = communicationId,
                    ChannelType = request.CommunicationType,
                    Direction = CommunicationDirection.Outgoing,
                    Message = envelope,
                    AcsThreadId = null, // outbound ACS thread id is assigned by the inbound echo path in R1
                },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Thread resolution failed (non-fatal) | CorrelationId: {CorrelationId}, CommunicationId: {CommunicationId}",
                correlationId, communicationId);
        }
    }

    /// <summary>
    /// Outbound MESSAGE (ACS Chat) send path — persist-on-send + echo-dedup (task 051 / FR-04 / ADR-045 rule 3).
    /// Dispatches through the EXISTING <see cref="CommunicationChannelDispatcher"/> (NOT a forked send path) to the
    /// messaging <see cref="Channels.ICommunicationChannelSender"/>, which posts server-side over ACS Chat AS the
    /// sending participant using a server-minted chat token (NFR-05 — no token reaches the client). It then persists
    /// exactly ONE <c>sprk_communication</c> (type=Message, Direction=Outgoing) recording the ACS message id
    /// (<c>sprk_acsmessageid</c>) + thread id (<c>sprk_acsthreadid</c>), and MARKS that ACS message id processed on
    /// the shared <see cref="IIdempotencyService"/> using the SAME key the inbound handler dedupes on
    /// (<see cref="IncomingMessagingJobHandler.IdempotencyKeyFor"/>) — so the Event Grid echo of our own outbound
    /// message is a no-op inbound (the load-bearing exactly-once property). Thread resolution (task 040) and
    /// enrichment are best-effort / non-fatal (NFR-02): a failure there never fails an already-sent + persisted
    /// message. No ACS token or admin capability is returned to the caller (NFR-04/NFR-05).
    /// </summary>
    private async Task<SendCommunicationResponse> SendMessageAsync(
        SendCommunicationRequest request,
        HttpContext? httpContext,
        string correlationId,
        CancellationToken ct)
    {
        // A message must carry content. Recipients/subject are optional for chat — the ACS thread is the
        // addressing unit and a chat thread may have no subject (the sender defaults a topic).
        if (string.IsNullOrWhiteSpace(request.Body))
        {
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: "Message body is required.",
                statusCode: 400,
                extensions: new Dictionary<string, object> { ["correlationId"] = correlationId });
        }

        // The BFF posts AS the authenticated user (server-minted token — NFR-05). A message has no shared-mailbox
        // concept, so an authenticated user context is required to identify the sending participant.
        if (httpContext is null)
        {
            throw new SdapProblemException(
                code: "OBO_CONTEXT_REQUIRED",
                title: "HttpContext Required",
                detail: "Sending a message requires an authenticated HttpContext to resolve the sending participant.",
                statusCode: 400,
                extensions: new Dictionary<string, object> { ["correlationId"] = correlationId });
        }

        var (senderParticipant, senderEmail, senderDisplayName) =
            await ResolveMessageSenderAsync(httpContext, correlationId, ct);

        try
        {
            // Dispatch by CommunicationType through the EXISTING dispatcher (ADR-045 — no second send path). The
            // messaging sender resolves the sender's ACS identity (task 010), mints their chat token, resolves or
            // creates the ACS thread (task 011), posts the message, and returns the ACS message id
            // (ProviderMessageId = the echo-dedup key, FR-04) + ACS thread id (ProviderThreadId).
            var sendResult = await _channelDispatcher.ResolveSender(CommunicationType.Message).SendAsync(
                new ChannelSendRequest
                {
                    Communication = request,
                    FromAddress = senderEmail,
                    FromDisplayName = senderDisplayName,
                    UserMode = false,
                    CorrelationId = correlationId,
                    SenderParticipant = senderParticipant,
                    // R1 has no live client to supply a thread; the messaging sender creates/looks up the ACS
                    // thread and returns it as ProviderThreadId (persisted + used for thread grouping below).
                    AcsThreadId = null
                },
                ct);

            // Persist-on-send (ADR-045 rule 3): exactly ONE sprk_communication, recording the ACS correlation ids.
            Guid? communicationId = null;
            try
            {
                communicationId = await CreateMessageDataverseRecordAsync(
                    request, senderEmail, sendResult, correlationId, ct);
            }
            catch (Exception dvEx)
            {
                // Non-fatal: the message reached ACS. Without a persisted record we deliberately do NOT mark the
                // echo processed below, so the inbound echo persists it instead (at-least-once backstop — still
                // exactly one record). This mirrors the inbound handler's persist-before-mark ordering.
                _logger.LogWarning(
                    dvEx,
                    "Message Dataverse record creation failed (non-fatal) | CorrelationId: {CorrelationId}",
                    correlationId);
            }

            // Echo-dedup mark (FR-04 / NFR-03) — the load-bearing exactly-once seam. Mark the SAME key the inbound
            // handler dedupes on so the Event Grid echo of our own outbound message is a no-op inbound. Marked
            // AFTER a successful persist so a failed persist lets the echo capture the message rather than dropping
            // it. Best-effort: a mark failure MUST NOT fail an already-sent + persisted message (NFR-02).
            if (communicationId.HasValue && !string.IsNullOrWhiteSpace(sendResult.ProviderMessageId))
            {
                await MarkOutboundEchoProcessedAsync(sendResult.ProviderMessageId!, correlationId, ct);
            }

            // Thread resolution (task 040 / FR-06) — best-effort, non-fatal (NFR-02). Unlike the email path the
            // outbound message HAS its authoritative ACS thread id (ProviderThreadId), so the messaging key
            // strategy can group by it now (find-or-create sprk_communicationthread + channel-ref row).
            if (communicationId.HasValue)
            {
                await ResolveOutboundMessageThreadAsync(
                    communicationId.Value, request, sendResult.ProviderThreadId, correlationId, ct);
            }

            // Direction-symmetric enrichment (ADR-045 / FR-08) — best-effort, non-fatal (NFR-02).
            if (communicationId.HasValue)
            {
                try
                {
                    await _enrichmentService.EnrichAsync(
                        communicationId.Value,
                        CommunicationDirection.Outgoing,
                        BuildOutboundEnvelope(request, senderEmail),
                        archivedDocumentId: null,
                        ct);
                }
                catch (Exception enrichEx)
                {
                    _logger.LogWarning(
                        enrichEx,
                        "Enrichment failed (non-fatal) | CorrelationId: {CorrelationId}, CommunicationId: {CommunicationId}",
                        correlationId, communicationId.Value);
                }
            }

            // The response carries only the tracking record id + status — NO ACS token or admin capability
            // reaches the caller (NFR-04/NFR-05).
            return new SendCommunicationResponse
            {
                CommunicationId = communicationId,
                GraphMessageId = correlationId,
                Status = CommunicationStatus.Send,
                SentAt = DateTimeOffset.UtcNow,
                From = senderEmail,
                CorrelationId = correlationId
            };
        }
        catch (Exception ex) when (ex is not SdapProblemException)
        {
            _logger.LogError(
                ex,
                "Unexpected error sending message | CorrelationId: {CorrelationId}",
                correlationId);

            throw new SdapProblemException(
                code: "CHANNEL_SEND_FAILED",
                title: "Message Send Failed",
                detail: $"Unexpected error: {ex.Message}",
                statusCode: 500,
                extensions: new Dictionary<string, object> { ["correlationId"] = correlationId });
        }
    }

    /// <summary>
    /// Resolves the outbound message's sending participant from the authenticated user's claims: the Entra
    /// <c>oid</c> → Dataverse <c>systemuserid</c> (the durable key <see cref="Acs.IAcsIdentityService"/> maps to
    /// an ACS <c>communicationUserId</c>). Returns the participant plus the user's email + display name for the
    /// persisted record + sender display. Throws RFC 7807 <see cref="SdapProblemException"/> (400) when the
    /// claims or the systemuser mapping cannot be resolved — a message cannot be sent without a sending identity.
    /// </summary>
    private async Task<(ParticipantReference Participant, string Email, string? DisplayName)> ResolveMessageSenderAsync(
        HttpContext httpContext, string correlationId, CancellationToken ct)
    {
        var userEmail = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? httpContext.User.FindFirst("preferred_username")?.Value
            ?? httpContext.User.FindFirst("email")?.Value
            ?? httpContext.User.FindFirst("upn")?.Value
            ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Upn)?.Value
            ?? httpContext.User.FindFirst("unique_name")?.Value;

        var userObjectId = httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

        if (string.IsNullOrWhiteSpace(userObjectId))
        {
            throw new SdapProblemException(
                code: "SENDER_NOT_RESOLVED",
                title: "Sender Not Resolved",
                detail: "Could not resolve the sending user's object id (oid) from authentication claims.",
                statusCode: 400,
                extensions: new Dictionary<string, object> { ["correlationId"] = correlationId });
        }

        Guid? systemUserId;
        try
        {
            systemUserId = await _communicationService.QuerySystemUserByAzureAdOidAsync(userObjectId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to resolve systemuser for message sender (oid={UserObjectId}) | CorrelationId: {CorrelationId}",
                userObjectId, correlationId);
            throw new SdapProblemException(
                code: "SENDER_NOT_RESOLVED",
                title: "Sender Not Resolved",
                detail: "Failed to resolve the sending user's Dataverse identity.",
                statusCode: 400,
                extensions: new Dictionary<string, object> { ["correlationId"] = correlationId });
        }

        if (!systemUserId.HasValue)
        {
            throw new SdapProblemException(
                code: "SENDER_NOT_RESOLVED",
                title: "Sender Not Resolved",
                detail: "The sending user has no Dataverse systemuser record; cannot act as an ACS participant.",
                statusCode: 400,
                extensions: new Dictionary<string, object> { ["correlationId"] = correlationId });
        }

        var displayName = httpContext.User.FindFirst("name")?.Value
            ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;

        return (ParticipantReference.SystemUser(systemUserId.Value),
                string.IsNullOrWhiteSpace(userEmail) ? userObjectId : userEmail,
                displayName);
    }

    /// <summary>
    /// Persists the outbound message as exactly one <c>sprk_communication</c> (type=Message, Direction=Outgoing),
    /// recording the ACS correlation ids under the AS-BUILT schema names — <c>sprk_acsmessageid</c> (the
    /// echo-dedup key) and <c>sprk_acsthreadid</c>. Mirrors <see cref="Channels.MessagingIngestor"/>'s persist
    /// shape for the inbound direction so both directions land one comparable record.
    /// </summary>
    private async Task<Guid> CreateMessageDataverseRecordAsync(
        SendCommunicationRequest request,
        string senderEmail,
        Channels.ChannelSendResult sendResult,
        string correlationId,
        CancellationToken ct)
    {
        var subject = string.IsNullOrWhiteSpace(request.Subject) ? "(No Subject)" : request.Subject;

        var communication = new DataverseEntity("sprk_communication")
        {
            ["sprk_name"] = $"Message: {TruncateTo(subject, 200)}",
            ["sprk_communicationtype"] = new OptionSetValue((int)CommunicationType.Message),   // 100000004
            ["statuscode"] = new OptionSetValue((int)CommunicationStatus.Send),
            ["statecode"] = new OptionSetValue(0), // Active
            ["sprk_direction"] = new OptionSetValue((int)CommunicationDirection.Outgoing),      // 100000001
            ["sprk_bodyformat"] = new OptionSetValue((int)request.BodyFormat),
            ["sprk_to"] = request.To is { Length: > 0 } ? string.Join("; ", request.To) : string.Empty,
            ["sprk_from"] = senderEmail,
            ["sprk_subject"] = subject,
            ["sprk_body"] = request.Body,
            ["sprk_sentat"] = DateTimeOffset.UtcNow.DateTime,
            ["sprk_correlationid"] = correlationId
        };

        // ACS transport ids (AS-BUILT schema names): the dedupe key + denormalized thread id. The
        // sprk_communicationthread LOOKUP (grouping key) is set by task 040's resolver, NOT here.
        if (!string.IsNullOrWhiteSpace(sendResult.ProviderMessageId))
            communication["sprk_acsmessageid"] = sendResult.ProviderMessageId;
        if (!string.IsNullOrWhiteSpace(sendResult.ProviderThreadId))
            communication["sprk_acsthreadid"] = sendResult.ProviderThreadId;

        if (request.Cc is { Length: > 0 })
            communication["sprk_cc"] = string.Join("; ", request.Cc);

        // Map primary association (regarding lookup + denormalized fields) — same ADR-024 mechanism as email.
        MapAssociationFields(communication, request.Associations, _logger);

        var recordId = await _genericEntityService.CreateAsync(communication, ct);

        _logger.LogInformation(
            "Persisted outbound message | CommunicationId: {RecordId}, AcsMessageId: {AcsMessageId}, AcsThreadId: {AcsThreadId}, CorrelationId: {CorrelationId}",
            recordId, sendResult.ProviderMessageId, sendResult.ProviderThreadId, correlationId);

        return recordId;
    }

    /// <summary>
    /// Marks the outbound ACS message id processed on the shared <see cref="IIdempotencyService"/> using the
    /// canonical key (<see cref="IncomingMessagingJobHandler.IdempotencyKeyFor"/>), so the Event Grid echo of our
    /// own message hits the inbound handler's fast-path dedupe and is a no-op — exactly-once persistence (FR-04 /
    /// NFR-03). Reaches the Scoped idempotency service from this Singleton via the scope factory (the codebase's
    /// canonical Singleton→Scoped bridge). Best-effort / non-fatal (NFR-02): a mark failure never fails an
    /// already-sent + persisted message (a rare double-persist on echo is preferable to failing the send).
    /// </summary>
    private async Task MarkOutboundEchoProcessedAsync(string acsMessageId, string correlationId, CancellationToken ct)
    {
        var key = IncomingMessagingJobHandler.IdempotencyKeyFor(acsMessageId);
        try
        {
            if (_scopeFactory is null)
            {
                _logger.LogWarning(
                    "No scope factory available to mark echo-dedup key {Key} (non-fatal) | CorrelationId: {CorrelationId}",
                    key, correlationId);
                return;
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var idempotency = scope.ServiceProvider.GetRequiredService<IIdempotencyService>();
            await idempotency.MarkEventAsProcessedAsync(key, expiration: null, ct);

            _logger.LogInformation(
                "Marked outbound ACS message {AcsMessageId} processed for echo-dedup (key={Key}) | CorrelationId: {CorrelationId}",
                acsMessageId, key, correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Echo-dedup mark failed (non-fatal) for ACS message {AcsMessageId} | CorrelationId: {CorrelationId}",
                acsMessageId, correlationId);
        }
    }

    /// <summary>
    /// Resolves (find-or-create) the outbound message's <c>sprk_communicationthread</c> via the task-040
    /// <see cref="IThreadResolver"/>, passing the authoritative ACS thread id so the messaging key strategy
    /// groups by it. Best-effort / non-fatal (NFR-02) — a resolve failure MUST NOT fail the send. Returns the
    /// resolved thread id (or null on failure/no resolver) so the caller can chain the task-043 Direct-thread
    /// message access grant.
    /// </summary>
    private async Task<Guid?> ResolveOutboundMessageThreadAsync(
        Guid communicationId, SendCommunicationRequest request, string? acsThreadId, string correlationId, CancellationToken ct)
    {
        if (_threadResolver is null)
            return null;

        try
        {
            var envelope = new NormalizedMessage
            {
                Direction = CommunicationDirection.Outgoing,
                Subject = request.Subject,
            };

            var threadId = await _threadResolver.ResolveAndAssignThreadAsync(
                new ThreadResolutionRequest
                {
                    CommunicationId = communicationId,
                    ChannelType = CommunicationType.Message,
                    Direction = CommunicationDirection.Outgoing,
                    Message = envelope,
                    AcsThreadId = string.IsNullOrWhiteSpace(acsThreadId) ? null : acsThreadId,
                },
                ct);

            // task 043 CRITICAL RISK fix: sprk_communication is User-level and this message is owned by the
            // app-only identity, so a Direct-thread message is invisible to BOTH participants under the
            // impersonated read (task 050) unless explicitly shared. Best-effort inside GrantMessageAccessAsync
            // itself (no-op for a non-Direct thread; failures are logged and swallowed).
            if (threadId.HasValue && _directThreadAccess is not null)
            {
                await _directThreadAccess.GrantMessageAccessAsync(communicationId, threadId.Value, ct);
            }

            return threadId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Thread resolution failed (non-fatal) | CorrelationId: {CorrelationId}, CommunicationId: {CommunicationId}",
                correlationId, communicationId);
            return null;
        }
    }

    /// <summary>
    /// Sends a communication via Microsoft Graph sendMail API.
    /// Validates the request, resolves the sender, constructs a Graph Message, and sends.
    /// On failure, throws SdapProblemException immediately (no retry).
    /// When SendMode is User, uses OBO flow to send as the authenticated user.
    /// </summary>
    /// <param name="request">The send communication request.</param>
    /// <param name="httpContext">HttpContext for OBO token resolution (required when SendMode is User).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>SendCommunicationResponse on success.</returns>
    /// <exception cref="SdapProblemException">Thrown for validation errors, invalid sender, or Graph failures.</exception>
    public async Task<SendCommunicationResponse> SendAsync(
        SendCommunicationRequest request,
        HttpContext? httpContext = null,
        CancellationToken cancellationToken = default)
    {
        var correlationId = request.CorrelationId ?? Guid.NewGuid().ToString("N");

        _logger.LogInformation(
            "Sending communication | CorrelationId: {CorrelationId}, To: {RecipientCount}, Type: {Type}, SendMode: {SendMode}",
            correlationId,
            request.To.Length,
            request.CommunicationType,
            request.SendMode);

        // Branch: MESSAGE (ACS Chat) sends take the persist-on-send + echo-dedup path (task 051 / FR-04).
        // Dispatch still flows through the SAME CommunicationChannelDispatcher (ADR-045 — no second send path);
        // this branch only supplies the messaging-specific sender-identity + persistence shape (participant, not
        // mailbox; ACS correlation ids; echo-dedup mark), mirroring the SendMode.User branch below.
        if (request.CommunicationType == CommunicationType.Message)
        {
            return await SendMessageAsync(request, httpContext, correlationId, cancellationToken);
        }

        // Branch: User mode sends via OBO as the authenticated user
        if (request.SendMode == SendMode.User)
        {
            return await SendAsUserAsync(request, httpContext!, correlationId, cancellationToken);
        }

        // Step 1: Validate request (shared mailbox path)
        ValidateRequest(request, correlationId);

        // Step 1b: Download and validate attachments (if any)
        List<ChannelAttachment>? fileAttachments = null;
        if (request.AttachmentDocumentIds is { Length: > 0 })
        {
            fileAttachments = await DownloadAndBuildAttachmentsAsync(
                request.AttachmentDocumentIds, correlationId, cancellationToken);
        }

        // Step 2: Resolve sender
        var senderResult = _senderValidator.Resolve(request.FromMailbox);
        if (!senderResult.IsValid)
        {
            _logger.LogWarning(
                "Sender validation failed | CorrelationId: {CorrelationId}, ErrorCode: {ErrorCode}, FromMailbox: {FromMailbox}",
                correlationId,
                senderResult.ErrorCode,
                request.FromMailbox);

            throw new SdapProblemException(
                code: senderResult.ErrorCode!,
                title: "Invalid Sender",
                detail: senderResult.ErrorDetail,
                statusCode: 400,
                extensions: new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId
                });
        }

        // Step 2b: Check daily send limit
        CommunicationAccount? senderAccount = null;
        try
        {
            senderAccount = await _accountService.GetSendAccountByEmailAsync(senderResult.Email!, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to retrieve sender account for daily limit check (proceeding without limit check) | CorrelationId: {CorrelationId}",
                correlationId);
        }

        if (senderAccount is { DailySendLimit: > 0 } && senderAccount.SendsToday >= senderAccount.DailySendLimit)
        {
            _logger.LogWarning(
                "Daily send limit reached | CorrelationId: {CorrelationId}, AccountId: {AccountId}, SendsToday: {SendsToday}, DailySendLimit: {DailySendLimit}",
                correlationId, senderAccount.Id, senderAccount.SendsToday, senderAccount.DailySendLimit);

            throw new SdapProblemException(
                code: "DAILY_SEND_LIMIT_REACHED",
                title: "Daily Send Limit Reached",
                detail: $"This mailbox has reached its daily send limit of {senderAccount.DailySendLimit}. Sends will reset at midnight UTC.",
                statusCode: 429,
                extensions: new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId,
                    ["sendsToday"] = senderAccount.SendsToday,
                    ["dailySendLimit"] = senderAccount.DailySendLimit.Value
                });
        }

        // Step 3+4: Send via the email channel seam (dispatch by CommunicationType — ADR-045 rule 4).
        // The sender owns Graph message construction, transport, and provider-error mapping; a provider
        // failure surfaces as SdapProblemException (GRAPH_SEND_FAILED) and propagates unchanged.
        try
        {
            var sendResult = await _channelDispatcher.ResolveSender(request.CommunicationType).SendAsync(
                new ChannelSendRequest
                {
                    Communication = request,
                    FromAddress = senderResult.Email!,
                    FromDisplayName = senderResult.DisplayName,
                    UserMode = false,
                    Attachments = fileAttachments,
                    CorrelationId = correlationId
                },
                cancellationToken);

            // Step 4b: Increment daily send count (best-effort — don't fail the send)
            if (senderAccount is not null)
            {
                try
                {
                    await _accountService.IncrementSendCountAsync(senderAccount.Id, cancellationToken);
                }
                catch (Exception incEx)
                {
                    _logger.LogWarning(
                        incEx,
                        "Failed to increment daily send count (non-fatal) | CorrelationId: {CorrelationId}, AccountId: {AccountId}",
                        correlationId, senderAccount.Id);
                }
            }

            // Step 5: Create Dataverse communication record (best-effort)
            Guid? communicationId = null;
            try
            {
                communicationId = await CreateDataverseRecordAsync(request, senderResult, correlationId, cancellationToken);
            }
            catch (Exception dvEx)
            {
                _logger.LogWarning(
                    dvEx,
                    "Dataverse record creation failed (non-fatal) | CorrelationId: {CorrelationId}",
                    correlationId);
            }

            // Step 5b: Persist the Internet-Message-Id the channel sender captured post-send (FR-06 —
            // feeds W1 thread rung). Channel-agnostic Dataverse stamp; best-effort.
            if (communicationId.HasValue && sendResult.ProviderMessageId is not null)
            {
                await StampInternetMessageIdAsync(
                    communicationId.Value, sendResult.ProviderMessageId, correlationId, cancellationToken);
            }

            // Step 5c: Resolve the thread (task 040 / FR-06) — best-effort, non-fatal (NFR-02).
            if (communicationId.HasValue)
            {
                await ResolveOutboundThreadAsync(communicationId.Value, request, correlationId, cancellationToken);
            }

            // Step 6: Archive to SPE if requested (best-effort)
            // Check ArchiveOutgoingOptIn on the sender's CommunicationAccount (default true if not set)
            Guid? archivedDocumentId = null;
            string? archivalWarning = null;
            bool archiveOutgoingOptIn = true;
            if (request.ArchiveToSpe && senderResult.Email is not null)
            {
                try
                {
                    var archiveAccount = await _accountService.GetSendAccountByEmailAsync(senderResult.Email, cancellationToken);
                    if (archiveAccount is not null)
                    {
                        archiveOutgoingOptIn = archiveAccount.ArchiveOutgoingOptIn ?? true;
                    }
                }
                catch (Exception optInEx)
                {
                    _logger.LogWarning(
                        optInEx,
                        "Failed to check ArchiveOutgoingOptIn for sender {SenderEmail} (defaulting to true) | CorrelationId: {CorrelationId}",
                        senderResult.Email, correlationId);
                }
            }

            if (request.ArchiveToSpe && !archiveOutgoingOptIn)
            {
                archivalWarning = "Archival skipped: ArchiveOutgoingOptIn is disabled for this sender account.";
                _logger.LogInformation(
                    "SPE archival skipped — ArchiveOutgoingOptIn is false for sender {SenderEmail} | CorrelationId: {CorrelationId}",
                    senderResult.Email, correlationId);
            }
            else if (request.ArchiveToSpe && communicationId.HasValue && archiveOutgoingOptIn)
            {
                // Build a partial response for .eml generation (before archival fields are known)
                var partialResponse = new SendCommunicationResponse
                {
                    CommunicationId = communicationId,
                    GraphMessageId = correlationId,
                    Status = CommunicationStatus.Send,
                    SentAt = DateTimeOffset.UtcNow,
                    From = senderResult.Email!,
                    CorrelationId = correlationId
                };

                try
                {
                    archivedDocumentId = await ArchiveToSpeAsync(request, partialResponse, communicationId.Value, cancellationToken);

                    // Enqueue AI analysis for the archived .eml document (best-effort)
                    await EnqueueDocumentAnalysisAsync(archivedDocumentId.Value, correlationId, cancellationToken);
                }
                catch (Exception archEx)
                {
                    archivalWarning = $"Email sent successfully but archival failed: {archEx.Message}";
                    _logger.LogWarning(
                        archEx,
                        "SPE archival failed (non-fatal) | CorrelationId: {CorrelationId}",
                        correlationId);
                }

                // Archive outbound attachments as child sprk_document records (best-effort)
                if (archivedDocumentId.HasValue && request.AttachmentDocumentIds is { Length: > 0 })
                {
                    try
                    {
                        var driveId = _options.ArchiveContainerId;
                        var attNames = fileAttachments?
                            .Select(fa => fa.Name ?? "Unknown")
                            .ToArray() ?? Array.Empty<string>();

                        await ArchiveOutboundAttachmentsAsync(
                            communicationId.Value, request.AttachmentDocumentIds, attNames,
                            driveId!, correlationId, cancellationToken);
                    }
                    catch (Exception attArchEx)
                    {
                        _logger.LogWarning(
                            attArchEx,
                            "Outbound attachment archival failed (non-fatal) | CommunicationId: {CommunicationId}, CorrelationId: {CorrelationId}",
                            communicationId.Value, correlationId);
                    }
                }
            }
            else if (request.ArchiveToSpe && !communicationId.HasValue)
            {
                archivalWarning = "Email sent successfully but archival skipped: Dataverse communication record was not created.";
                _logger.LogWarning(
                    "SPE archival skipped because Dataverse record creation failed | CorrelationId: {CorrelationId}",
                    correlationId);
            }

            // Step 7: Create sprk_communicationattachment records (best-effort)
            string? attachmentRecordWarning = null;
            if (request.AttachmentDocumentIds is { Length: > 0 } && communicationId.HasValue)
            {
                // Extract file names from the downloaded attachments for display names
                var attachmentNames = fileAttachments?
                    .Select(fa => fa.Name ?? "Unknown")
                    .ToArray() ?? Array.Empty<string>();

                try
                {
                    await CreateAttachmentRecordsAsync(
                        communicationId.Value,
                        request.AttachmentDocumentIds,
                        attachmentNames,
                        correlationId,
                        cancellationToken);
                }
                catch (Exception attEx)
                {
                    attachmentRecordWarning = $"Email sent successfully but attachment record creation failed: {attEx.Message}";
                    _logger.LogWarning(
                        attEx,
                        "Attachment record creation failed (non-fatal) | CommunicationId: {CommunicationId}, CorrelationId: {CorrelationId}",
                        communicationId.Value,
                        correlationId);
                }
            }
            else if (request.AttachmentDocumentIds is { Length: > 0 } && !communicationId.HasValue)
            {
                attachmentRecordWarning = "Email sent successfully but attachment records skipped: Dataverse communication record was not created.";
                _logger.LogWarning(
                    "Attachment record creation skipped because Dataverse record creation failed | CorrelationId: {CorrelationId}",
                    correlationId);
            }

            // Step 8: Direction-agnostic enrichment (ADR-045 / FR-08) — best-effort, non-fatal (NFR-06).
            // Closes the previously-missing OUTBOUND RAG-indexing half; full association/analysis
            // direction-symmetry lands in task 011. EnrichAsync is itself non-fatal; the guard is defense-in-depth.
            if (communicationId.HasValue)
            {
                try
                {
                    await _enrichmentService.EnrichAsync(
                        communicationId.Value,
                        CommunicationDirection.Outgoing,
                        BuildOutboundEnvelope(request, senderResult.Email!),
                        archivedDocumentId,
                        cancellationToken);
                }
                catch (Exception enrichEx)
                {
                    _logger.LogWarning(
                        enrichEx,
                        "Enrichment failed (non-fatal) | CorrelationId: {CorrelationId}, CommunicationId: {CommunicationId}",
                        correlationId, communicationId.Value);
                }
            }

            return new SendCommunicationResponse
            {
                CommunicationId = communicationId,
                GraphMessageId = correlationId, // Graph sendMail doesn't return a message ID; using correlationId as tracking reference
                Status = CommunicationStatus.Send,
                SentAt = DateTimeOffset.UtcNow,
                From = senderResult.Email!,
                CorrelationId = correlationId,
                ArchivedDocumentId = archivedDocumentId,
                ArchivalWarning = archivalWarning,
                AttachmentCount = request.AttachmentDocumentIds?.Length ?? 0,
                AttachmentRecordWarning = attachmentRecordWarning
            };
        }
        catch (Exception ex) when (ex is not SdapProblemException)
        {
            _logger.LogError(
                ex,
                "Unexpected error sending communication | CorrelationId: {CorrelationId}",
                correlationId);

            throw new SdapProblemException(
                code: "GRAPH_SEND_FAILED",
                title: "Email Send Failed",
                detail: $"Unexpected error: {ex.Message}",
                statusCode: 500,
                extensions: new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId
                });
        }
    }

    /// <summary>
    /// Sends an email as the authenticated user via On-Behalf-Of (OBO) flow.
    /// Skips ApprovedSenderValidator — the user sends as themselves.
    /// Uses GraphClientFactory.ForUserAsync() and calls /me/sendMail.
    /// </summary>
    private async Task<SendCommunicationResponse> SendAsUserAsync(
        SendCommunicationRequest request,
        HttpContext httpContext,
        string correlationId,
        CancellationToken ct)
    {
        // Step 1: Validate request (same basic validation as shared path)
        ValidateRequest(request, correlationId);

        // Step 1b: Validate HttpContext is available
        if (httpContext is null)
        {
            throw new SdapProblemException(
                code: "OBO_CONTEXT_REQUIRED",
                title: "HttpContext Required",
                detail: "SendMode.User requires an authenticated HttpContext for OBO token resolution.",
                statusCode: 400,
                extensions: new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId
                });
        }

        // Step 1c: Download and validate attachments (if any)
        List<ChannelAttachment>? fileAttachments = null;
        if (request.AttachmentDocumentIds is { Length: > 0 })
        {
            fileAttachments = await DownloadAndBuildAttachmentsAsync(
                request.AttachmentDocumentIds, correlationId, ct);
        }

        // Step 2: Resolve user email from claims (no ApprovedSenderValidator for user mode)
        // Check multiple claim types: v2.0 tokens use preferred_username/email,
        // v1.0 tokens use upn/unique_name, ClaimTypes.Email maps to xmlsoap email claim
        var userEmail = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? httpContext.User.FindFirst("preferred_username")?.Value
            ?? httpContext.User.FindFirst("email")?.Value
            ?? httpContext.User.FindFirst("upn")?.Value
            ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Upn)?.Value
            ?? httpContext.User.FindFirst("unique_name")?.Value;

        if (string.IsNullOrWhiteSpace(userEmail))
        {
            throw new SdapProblemException(
                code: "USER_EMAIL_NOT_FOUND",
                title: "User Email Not Found",
                detail: "Could not resolve user email from authentication claims. Ensure the token includes 'email' or 'preferred_username' claim.",
                statusCode: 400,
                extensions: new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId
                });
        }

        // Resolve user object ID for sprk_sentby (Azure AD oid claim)
        var userObjectId = httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

        _logger.LogInformation(
            "Sending as user (OBO) | CorrelationId: {CorrelationId}, UserEmail: {UserEmail}",
            correlationId,
            userEmail);

        // Step 3+4: Send via the email channel seam in user (OBO) mode — dispatch by CommunicationType.
        // The sender resolves the OBO Graph client and sends as /me; provider failures surface as
        // SdapProblemException (GRAPH_SEND_FAILED, sendMode=User) and propagate unchanged.
        try
        {
            var sendResult = await _channelDispatcher.ResolveSender(request.CommunicationType).SendAsync(
                new ChannelSendRequest
                {
                    Communication = request,
                    FromAddress = userEmail,
                    UserMode = true,
                    HttpContext = httpContext,
                    Attachments = fileAttachments,
                    CorrelationId = correlationId
                },
                ct);

            // Step 5: Create Dataverse communication record (best-effort)
            Guid? communicationId = null;
            try
            {
                communicationId = await CreateDataverseRecordForUserAsync(
                    request, userEmail, userObjectId, correlationId, ct);
            }
            catch (Exception dvEx)
            {
                _logger.LogWarning(
                    dvEx,
                    "Dataverse record creation failed (non-fatal) | CorrelationId: {CorrelationId}",
                    correlationId);
            }

            // Step 5b: Persist the Internet-Message-Id the channel sender captured post-send (FR-06 —
            // feeds W1 thread rung). Channel-agnostic Dataverse stamp; best-effort.
            if (communicationId.HasValue && sendResult.ProviderMessageId is not null)
            {
                await StampInternetMessageIdAsync(
                    communicationId.Value, sendResult.ProviderMessageId, correlationId, ct);
            }

            // Step 5c: Resolve the thread (task 040 / FR-06) — best-effort, non-fatal (NFR-02).
            if (communicationId.HasValue)
            {
                await ResolveOutboundThreadAsync(communicationId.Value, request, correlationId, ct);
            }

            // Step 6: Archive to SPE if requested (best-effort)
            // For user mode, check ArchiveOutgoingOptIn via FromMailbox account if available (default true)
            Guid? archivedDocumentId = null;
            string? archivalWarning = null;
            bool archiveOutgoingOptIn = true;
            if (request.ArchiveToSpe && request.FromMailbox is not null)
            {
                try
                {
                    var senderAccount = await _accountService.GetSendAccountByEmailAsync(request.FromMailbox, ct);
                    if (senderAccount is not null)
                    {
                        archiveOutgoingOptIn = senderAccount.ArchiveOutgoingOptIn ?? true;
                    }
                }
                catch (Exception optInEx)
                {
                    _logger.LogWarning(
                        optInEx,
                        "Failed to check ArchiveOutgoingOptIn for user sender {FromMailbox} (defaulting to true) | CorrelationId: {CorrelationId}",
                        request.FromMailbox, correlationId);
                }
            }

            if (request.ArchiveToSpe && !archiveOutgoingOptIn)
            {
                archivalWarning = "Archival skipped: ArchiveOutgoingOptIn is disabled for this sender account.";
                _logger.LogInformation(
                    "SPE archival skipped — ArchiveOutgoingOptIn is false for user sender {UserEmail} | CorrelationId: {CorrelationId}",
                    userEmail, correlationId);
            }
            else if (request.ArchiveToSpe && communicationId.HasValue && archiveOutgoingOptIn)
            {
                var partialResponse = new SendCommunicationResponse
                {
                    CommunicationId = communicationId,
                    GraphMessageId = correlationId,
                    Status = CommunicationStatus.Send,
                    SentAt = DateTimeOffset.UtcNow,
                    From = userEmail,
                    CorrelationId = correlationId
                };

                try
                {
                    archivedDocumentId = await ArchiveToSpeAsync(request, partialResponse, communicationId.Value, ct);

                    // Enqueue AI analysis for the archived .eml document (best-effort)
                    await EnqueueDocumentAnalysisAsync(archivedDocumentId.Value, correlationId, ct);
                }
                catch (Exception archEx)
                {
                    archivalWarning = $"Email sent successfully but archival failed: {archEx.Message}";
                    _logger.LogWarning(
                        archEx,
                        "SPE archival failed (non-fatal) | CorrelationId: {CorrelationId}",
                        correlationId);
                }

                // Archive outbound attachments as child sprk_document records (best-effort)
                if (archivedDocumentId.HasValue && request.AttachmentDocumentIds is { Length: > 0 })
                {
                    try
                    {
                        var driveId = _options.ArchiveContainerId;
                        var attNames = fileAttachments?
                            .Select(fa => fa.Name ?? "Unknown")
                            .ToArray() ?? Array.Empty<string>();

                        await ArchiveOutboundAttachmentsAsync(
                            communicationId.Value, request.AttachmentDocumentIds, attNames,
                            driveId!, correlationId, ct);
                    }
                    catch (Exception attArchEx)
                    {
                        _logger.LogWarning(
                            attArchEx,
                            "Outbound attachment archival failed (non-fatal) | CommunicationId: {CommunicationId}, CorrelationId: {CorrelationId}",
                            communicationId.Value, correlationId);
                    }
                }
            }
            else if (request.ArchiveToSpe && !communicationId.HasValue)
            {
                archivalWarning = "Email sent successfully but archival skipped: Dataverse communication record was not created.";
                _logger.LogWarning(
                    "SPE archival skipped because Dataverse record creation failed | CorrelationId: {CorrelationId}",
                    correlationId);
            }

            // Step 7: Create sprk_communicationattachment records (best-effort)
            string? attachmentRecordWarning = null;
            if (request.AttachmentDocumentIds is { Length: > 0 } && communicationId.HasValue)
            {
                var attachmentNames = fileAttachments?
                    .Select(fa => fa.Name ?? "Unknown")
                    .ToArray() ?? Array.Empty<string>();

                try
                {
                    await CreateAttachmentRecordsAsync(
                        communicationId.Value,
                        request.AttachmentDocumentIds,
                        attachmentNames,
                        correlationId,
                        ct);
                }
                catch (Exception attEx)
                {
                    attachmentRecordWarning = $"Email sent successfully but attachment record creation failed: {attEx.Message}";
                    _logger.LogWarning(
                        attEx,
                        "Attachment record creation failed (non-fatal) | CommunicationId: {CommunicationId}, CorrelationId: {CorrelationId}",
                        communicationId.Value,
                        correlationId);
                }
            }
            else if (request.AttachmentDocumentIds is { Length: > 0 } && !communicationId.HasValue)
            {
                attachmentRecordWarning = "Email sent successfully but attachment records skipped: Dataverse communication record was not created.";
                _logger.LogWarning(
                    "Attachment record creation skipped because Dataverse record creation failed | CorrelationId: {CorrelationId}",
                    correlationId);
            }

            // Step 8: Direction-agnostic enrichment (ADR-045 / FR-08) — best-effort, non-fatal (NFR-06).
            // Closes the previously-missing OUTBOUND RAG-indexing half; full association/analysis
            // direction-symmetry lands in task 011. EnrichAsync is itself non-fatal; the guard is defense-in-depth.
            if (communicationId.HasValue)
            {
                try
                {
                    await _enrichmentService.EnrichAsync(
                        communicationId.Value,
                        CommunicationDirection.Outgoing,
                        BuildOutboundEnvelope(request, userEmail),
                        archivedDocumentId,
                        ct);
                }
                catch (Exception enrichEx)
                {
                    _logger.LogWarning(
                        enrichEx,
                        "Enrichment failed (non-fatal) | CorrelationId: {CorrelationId}, CommunicationId: {CommunicationId}",
                        correlationId, communicationId.Value);
                }
            }

            return new SendCommunicationResponse
            {
                CommunicationId = communicationId,
                GraphMessageId = correlationId,
                Status = CommunicationStatus.Send,
                SentAt = DateTimeOffset.UtcNow,
                From = userEmail,
                CorrelationId = correlationId,
                ArchivedDocumentId = archivedDocumentId,
                ArchivalWarning = archivalWarning,
                AttachmentCount = request.AttachmentDocumentIds?.Length ?? 0,
                AttachmentRecordWarning = attachmentRecordWarning
            };
        }
        catch (Exception ex) when (ex is not SdapProblemException)
        {
            _logger.LogError(
                ex,
                "Unexpected error sending communication (OBO) | CorrelationId: {CorrelationId}",
                correlationId);

            throw new SdapProblemException(
                code: "GRAPH_SEND_FAILED",
                title: "Email Send Failed",
                detail: $"Unexpected error (OBO): {ex.Message}",
                statusCode: 500,
                extensions: new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId,
                    ["sendMode"] = "User"
                });
        }
    }

    /// <summary>
    /// Creates a Dataverse sprk_communication record for user-mode sends.
    /// Sets sprk_from to user's email and sprk_sentby to user's Azure AD object ID.
    /// </summary>
    private async Task<Guid> CreateDataverseRecordForUserAsync(
        SendCommunicationRequest request,
        string userEmail,
        string? userObjectId,
        string correlationId,
        CancellationToken ct)
    {
        var communication = new DataverseEntity("sprk_communication")
        {
            ["sprk_name"] = $"Email: {TruncateTo(request.Subject, 200)}",
            ["sprk_communicationtype"] = new OptionSetValue((int)request.CommunicationType),
            ["statuscode"] = new OptionSetValue((int)CommunicationStatus.Send),
            ["statecode"] = new OptionSetValue(0), // Active
            ["sprk_direction"] = new OptionSetValue((int)CommunicationDirection.Outgoing),
            ["sprk_bodyformat"] = new OptionSetValue((int)request.BodyFormat),
            ["sprk_to"] = string.Join("; ", request.To),
            ["sprk_from"] = userEmail,
            ["sprk_subject"] = request.Subject,
            ["sprk_body"] = request.Body,
            ["sprk_graphmessageid"] = correlationId,
            ["sprk_sentat"] = DateTimeOffset.UtcNow.DateTime,
            ["sprk_correlationid"] = correlationId
        };

        // Resolve Azure AD oid → Dataverse systemuserid for sprk_sentby lookup
        if (!string.IsNullOrEmpty(userObjectId))
        {
            try
            {
                var systemUserId = await _communicationService.QuerySystemUserByAzureAdOidAsync(userObjectId, ct);
                if (systemUserId.HasValue)
                {
                    communication["sprk_sentby"] = new EntityReference("systemuser", systemUserId.Value);
                }
                else
                {
                    _logger.LogWarning(
                        "Could not resolve systemuser for Azure AD OID {UserObjectId}; sprk_sentby will be null",
                        userObjectId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to resolve systemuser for sprk_sentby (Azure AD OID: {UserObjectId}); continuing without",
                    userObjectId);
            }
        }

        // Only set CC/BCC if provided
        if (request.Cc is { Length: > 0 })
            communication["sprk_cc"] = string.Join("; ", request.Cc);
        if (request.Bcc is { Length: > 0 })
            communication["sprk_bcc"] = string.Join("; ", request.Bcc);

        // Reply-thread continuity (FR-06): stamp the parent's Internet-Message-Id when provided.
        if (!string.IsNullOrWhiteSpace(request.InReplyToMessageId))
            communication["sprk_inreplyto"] = request.InReplyToMessageId;

        // Set attachment fields if attachments were included
        if (request.AttachmentDocumentIds is { Length: > 0 })
        {
            communication["sprk_hasattachments"] = true;
            communication["sprk_attachmentcount"] = request.AttachmentDocumentIds.Length;
        }

        // Map primary association (regarding lookup + denormalized fields)
        MapAssociationFields(communication, request.Associations, _logger);

        var recordId = await _genericEntityService.CreateAsync(communication, ct);

        _logger.LogInformation(
            "Dataverse communication record created (user mode) | Id: {RecordId}, UserEmail: {UserEmail}, CorrelationId: {CorrelationId}",
            recordId,
            userEmail,
            correlationId);

        return recordId;
    }

    private static void ValidateRequest(SendCommunicationRequest request, string correlationId)
    {
        if (request.To.Length == 0)
        {
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: "At least one recipient (To) is required.",
                statusCode: 400,
                extensions: new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId
                });
        }

        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: "Subject is required.",
                statusCode: 400,
                extensions: new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId
                });
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: "Body is required.",
                statusCode: 400,
                extensions: new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId
                });
        }
    }

    /// <summary>
    /// Creates a Dataverse sprk_communication record to track the sent email.
    /// </summary>
    private async Task<Guid> CreateDataverseRecordAsync(
        SendCommunicationRequest request,
        ApprovedSenderResult sender,
        string correlationId,
        CancellationToken ct)
    {
        var communication = new DataverseEntity("sprk_communication")
        {
            ["sprk_name"] = $"Email: {TruncateTo(request.Subject, 200)}",
            ["sprk_communicationtype"] = new OptionSetValue((int)request.CommunicationType),
            ["statuscode"] = new OptionSetValue((int)CommunicationStatus.Send),
            ["statecode"] = new OptionSetValue(0), // Active
            ["sprk_direction"] = new OptionSetValue((int)CommunicationDirection.Outgoing),
            ["sprk_bodyformat"] = new OptionSetValue((int)request.BodyFormat),
            ["sprk_to"] = string.Join("; ", request.To),
            ["sprk_from"] = sender.Email,
            ["sprk_subject"] = request.Subject,
            ["sprk_body"] = request.Body,
            ["sprk_graphmessageid"] = correlationId,
            ["sprk_sentat"] = DateTimeOffset.UtcNow.DateTime,
            ["sprk_correlationid"] = correlationId
        };

        // Only set CC/BCC if provided
        if (request.Cc is { Length: > 0 })
            communication["sprk_cc"] = string.Join("; ", request.Cc);
        if (request.Bcc is { Length: > 0 })
            communication["sprk_bcc"] = string.Join("; ", request.Bcc);

        // Reply-thread continuity (FR-06): stamp the parent's Internet-Message-Id when provided.
        if (!string.IsNullOrWhiteSpace(request.InReplyToMessageId))
            communication["sprk_inreplyto"] = request.InReplyToMessageId;

        // Set attachment fields if attachments were included
        if (request.AttachmentDocumentIds is { Length: > 0 })
        {
            communication["sprk_hasattachments"] = true;
            communication["sprk_attachmentcount"] = request.AttachmentDocumentIds.Length;
        }

        // Map primary association (regarding lookup + denormalized fields)
        MapAssociationFields(communication, request.Associations, _logger);

        var recordId = await _genericEntityService.CreateAsync(communication, ct);

        _logger.LogInformation(
            "Dataverse communication record created | Id: {RecordId}, CorrelationId: {CorrelationId}",
            recordId,
            correlationId);

        return recordId;
    }

    /// <summary>
    /// Maps from EntityType to (Dataverse lookup field name, target entity set name).
    /// </summary>
    private static readonly Dictionary<string, (string LookupField, string EntitySetName)> RegardingLookupMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sprk_matter"] = ("sprk_regardingmatter", "sprk_matters"),
        ["sprk_organization"] = ("sprk_regardingorganization", "sprk_organizations"),
        ["contact"] = ("sprk_regardingperson", "contacts"),
        ["sprk_project"] = ("sprk_regardingproject", "sprk_projects"),
        ["sprk_analysis"] = ("sprk_regardinganalysis", "sprk_analysises"),
        ["sprk_budget"] = ("sprk_regardingbudget", "sprk_budgets"),
        ["sprk_invoice"] = ("sprk_regardinginvoice", "sprk_invoices"),
        ["sprk_workassignment"] = ("sprk_regardingworkassignment", "sprk_workassignments"),
        ["sprk_servicerequest"] = ("sprk_regardingservicerequest", "sprk_servicerequests"),
        ["sprk_event"] = ("sprk_regardingevent", "sprk_events"),
        ["account"] = ("sprk_regardingaccount", "accounts"),
    };

    /// <summary>
    /// Maps the primary association (associations[0]) to Dataverse regarding lookup
    /// and denormalized text fields on the sprk_communication entity.
    /// </summary>
    private static void MapAssociationFields(
        DataverseEntity communication,
        CommunicationAssociation[]? associations,
        ILogger logger)
    {
        if (associations is not { Length: > 0 })
            return;

        communication["sprk_associationcount"] = associations.Length;

        var primary = associations[0];

        // Set the regarding lookup field for the primary association
        if (RegardingLookupMap.TryGetValue(primary.EntityType, out var mapping))
        {
            communication[mapping.LookupField] = new EntityReference(primary.EntityType, primary.EntityId);
        }
        else
        {
            logger.LogWarning(
                "Unknown entity type for association lookup mapping: {EntityType}. Regarding lookup will not be set.",
                primary.EntityType);
        }

        // Set denormalized regarding fields
        if (primary.EntityName is not null)
            communication["sprk_regardingrecordname"] = TruncateTo(primary.EntityName, 100);

        communication["sprk_regardingrecordid"] = TruncateTo(primary.EntityId.ToString(), 100);

        if (primary.EntityUrl is not null)
            communication["sprk_regardingrecordurl"] = TruncateTo(primary.EntityUrl, 200);
    }

    /// <summary>
    /// Archives the sent communication as a .eml file in SharePoint Embedded
    /// and creates a sprk_document record linking to the archived file.
    /// </summary>
    private async Task<Guid> ArchiveToSpeAsync(
        SendCommunicationRequest request,
        SendCommunicationResponse partialResponse,
        Guid communicationId,
        CancellationToken ct,
        int emailDirection = 100000001) // default Sent — send-path callers are always outbound
    {
        // 1. Generate the archival artifact via the channel archiver seam (dispatch by
        // CommunicationType — ADR-045 rule 4). Email resolves to .eml generation.
        var emlResult = _channelDispatcher.ResolveArchiver(request.CommunicationType)
            .GenerateEml(request, partialResponse);

        // 2. Upload to SPE at /communications/{commId:N}/{fileName}.eml
        var driveId = _options.ArchiveContainerId;
        if (string.IsNullOrWhiteSpace(driveId))
        {
            throw new InvalidOperationException("ArchiveContainerId not configured for SPE archival");
        }

        var spePath = $"/communications/{communicationId:N}/{emlResult.FileName}";

        using var stream = new MemoryStream(emlResult.Content);
        var fileHandle = await _speFileStore.UploadSmallAsync(driveId, spePath, stream, ct);

        _logger.LogInformation(
            "Archived communication .eml to SPE | CommunicationId: {CommunicationId}, Path: {Path}",
            communicationId, spePath);

        // 3. Create sprk_document record linking to the archived file
        var document = new DataverseEntity("sprk_document")
        {
            ["sprk_documentname"] = $"Archived: {TruncateTo(request.Subject, 180)}",
            ["sprk_filename"] = emlResult.FileName, // AI analyzer reads this for file type detection
            ["sprk_documenttype"] = new OptionSetValue(100000006), // Email
            ["sprk_sourcetype"] = new OptionSetValue(659490003), // Email Archive
            ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
            ["sprk_graphitemid"] = fileHandle?.Id,
            ["sprk_graphdriveid"] = driveId,
            ["sprk_isemailarchive"] = true,
            ["sprk_emailsubject"] = request.Subject,
            ["sprk_emailfrom"] = partialResponse.From,
            ["sprk_emailto"] = string.Join("; ", request.To),
            ["sprk_emaildirection"] = new OptionSetValue(emailDirection), // 100000001 Sent (default) / 100000000 Received
            ["sprk_emaildate"] = partialResponse.SentAt.DateTime,
        };

        var documentId = await _genericEntityService.CreateAsync(document, ct);

        _logger.LogInformation(
            "Created sprk_document record for archived .eml | DocumentId: {DocumentId}, CommunicationId: {CommunicationId}",
            documentId, communicationId);

        return documentId;
    }

    /// <summary>
    /// Creates sprk_communicationattachment records in Dataverse to link each attached
    /// document to the communication record. Records are created sequentially.
    /// </summary>
    /// <param name="communicationId">The Dataverse sprk_communication record ID.</param>
    /// <param name="attachmentDocumentIds">SPE document IDs that were attached.</param>
    /// <param name="attachmentNames">File names corresponding to each document ID (from metadata fetched during download).</param>
    /// <param name="correlationId">Correlation ID for tracing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of attachment records successfully created.</returns>
    private async Task<int> CreateAttachmentRecordsAsync(
        Guid communicationId,
        string[] attachmentDocumentIds,
        string[] attachmentNames,
        string correlationId,
        CancellationToken ct)
    {
        var createdCount = 0;

        for (var i = 0; i < attachmentDocumentIds.Length; i++)
        {
            var documentId = attachmentDocumentIds[i];
            var documentName = i < attachmentNames.Length ? attachmentNames[i] : $"Attachment {i + 1}";

            var attachment = new DataverseEntity("sprk_communicationattachment")
            {
                ["sprk_name"] = TruncateTo(documentName, 200),
                ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
                ["sprk_document"] = new EntityReference("sprk_document", Guid.Parse(documentId)),
                ["sprk_attachmenttype"] = new OptionSetValue(100000000) // File
            };

            var attachmentId = await _genericEntityService.CreateAsync(attachment, ct);
            createdCount++;

            _logger.LogDebug(
                "Created sprk_communicationattachment record | Id: {AttachmentId}, CommunicationId: {CommunicationId}, DocumentId: {DocumentId}, Name: {Name}, CorrelationId: {CorrelationId}",
                attachmentId, communicationId, documentId, documentName, correlationId);
        }

        _logger.LogInformation(
            "Attachment records created | Count: {Count}, CommunicationId: {CommunicationId}, CorrelationId: {CorrelationId}",
            createdCount, communicationId, correlationId);

        return createdCount;
    }

    /// <summary>
    /// Creates child sprk_document records for each outbound attachment and enqueues AI analysis jobs.
    /// Each attachment gets a document record linked to the communication, with source type CommunicationArchive.
    /// </summary>
    private async Task ArchiveOutboundAttachmentsAsync(
        Guid communicationId,
        string[] attachmentDocumentIds,
        string[] attachmentNames,
        string driveId,
        string correlationId,
        CancellationToken ct)
    {
        for (var i = 0; i < attachmentDocumentIds.Length; i++)
        {
            var speItemId = attachmentDocumentIds[i];
            var fileName = i < attachmentNames.Length ? attachmentNames[i] : $"Attachment {i + 1}";

            try
            {
                var attachmentDoc = new DataverseEntity("sprk_document")
                {
                    ["sprk_documentname"] = TruncateTo(fileName, 200),
                    ["sprk_filename"] = fileName, // AI analyzer reads this for file type detection
                    ["sprk_documenttype"] = new OptionSetValue(100000006), // Email
                    ["sprk_sourcetype"] = new OptionSetValue(659490004), // Email Attachment
                    ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
                    ["sprk_graphitemid"] = speItemId,
                    ["sprk_graphdriveid"] = driveId,
                };

                var documentId = await _genericEntityService.CreateAsync(attachmentDoc, ct);

                _logger.LogInformation(
                    "Created sprk_document for outbound attachment | DocumentId: {DocumentId}, FileName: {FileName}, CommunicationId: {CommunicationId}, CorrelationId: {CorrelationId}",
                    documentId, fileName, communicationId, correlationId);

                // Enqueue AI analysis for the attachment document (best-effort)
                await EnqueueDocumentAnalysisAsync(documentId, correlationId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to archive outbound attachment {Index}/{Total} (non-fatal) | SpeItemId: {SpeItemId}, FileName: {FileName}, CommunicationId: {CommunicationId}, CorrelationId: {CorrelationId}",
                    i + 1, attachmentDocumentIds.Length, speItemId, fileName, communicationId, correlationId);
            }
        }
    }

    /// <summary>
    /// Enqueues an AppOnlyDocumentAnalysis job for a document record (best-effort).
    /// Failures are logged but do not propagate.
    /// </summary>
    private async Task EnqueueDocumentAnalysisAsync(Guid documentId, string correlationId, CancellationToken ct)
    {
        try
        {
            var aiJob = new JobContract
            {
                JobId = Guid.NewGuid(),
                JobType = AppOnlyDocumentAnalysisJobHandler.JobTypeName,
                SubjectId = documentId.ToString(),
                CorrelationId = correlationId,
                IdempotencyKey = $"DocAnalysis:{documentId}",
                Attempt = 1,
                MaxAttempts = 2,
                CreatedAt = DateTimeOffset.UtcNow,
                Payload = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    DocumentId = documentId,
                    Source = "OutboundEmailArchival",
                    EnqueuedAt = DateTimeOffset.UtcNow
                }))
            };

            await _jobSubmissionService.SubmitJobAsync(aiJob, ct);

            _logger.LogInformation(
                "Enqueued AI analysis job {JobId} for archived document {DocumentId} | CorrelationId: {CorrelationId}",
                aiJob.JobId, documentId, correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to enqueue AI analysis job for document {DocumentId} (non-fatal) | CorrelationId: {CorrelationId}",
                documentId, correlationId);
        }
    }

    /// <summary>
    /// Maximum number of attachments allowed per email (Graph API limit).
    /// </summary>
    private const int MaxAttachmentCount = 150;

    /// <summary>
    /// Maximum total size of all attachments in bytes (35 MB).
    /// </summary>
    private const long MaxTotalAttachmentSizeBytes = 36_700_160;

    /// <summary>
    /// Downloads files from SPE, validates attachment count and total size limits,
    /// and builds a list of channel-neutral <see cref="Channels.ChannelAttachment"/> objects for the sender.
    /// </summary>
    /// <exception cref="SdapProblemException">
    /// Thrown when attachment count exceeds 150, total size exceeds 35MB,
    /// or an individual file download fails.
    /// </exception>
    private async Task<List<ChannelAttachment>> DownloadAndBuildAttachmentsAsync(
        string[] attachmentDocumentIds,
        string correlationId,
        CancellationToken ct)
    {
        // Validate count limit
        if (attachmentDocumentIds.Length > MaxAttachmentCount)
        {
            throw new SdapProblemException(
                code: "ATTACHMENT_LIMIT_EXCEEDED",
                title: "Too Many Attachments",
                detail: $"Maximum {MaxAttachmentCount} attachments allowed. Received {attachmentDocumentIds.Length}.",
                statusCode: 400,
                extensions: new Dictionary<string, object>
                {
                    ["correlationId"] = correlationId,
                    ["maxAttachments"] = MaxAttachmentCount,
                    ["requestedAttachments"] = attachmentDocumentIds.Length
                });
        }

        // Each input is a sprk_document GUID. Look it up to get the actual SPE
        // driveId + itemId. Containers in Spaarke are per Business Unit (not per
        // matter), so each document carries its own sprk_graphdriveid (BU's
        // container) and sprk_graphitemid (file in that container).
        // The legacy _options.ArchiveContainerId is no longer used here.
        var attachments = new List<ChannelAttachment>(attachmentDocumentIds.Length);
        long totalSize = 0;

        for (var i = 0; i < attachmentDocumentIds.Length; i++)
        {
            var sprkDocumentId = attachmentDocumentIds[i];

            // Resolve sprk_document → (graphdriveid, graphitemid)
            DocumentEntity? docRecord;
            try
            {
                docRecord = await _documentService.GetDocumentAsync(sprkDocumentId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to load sprk_document {SprkDocumentId} for attachment lookup | CorrelationId: {CorrelationId}",
                    sprkDocumentId, correlationId);

                throw new SdapProblemException(
                    code: "ATTACHMENT_LOOKUP_FAILED",
                    title: "Attachment Lookup Failed",
                    detail: $"Failed to load Dataverse record for document '{sprkDocumentId}': {ex.Message}",
                    statusCode: 502,
                    extensions: new Dictionary<string, object>
                    {
                        ["correlationId"] = correlationId,
                        ["failedDocumentId"] = sprkDocumentId,
                        ["attachmentIndex"] = i
                    });
            }

            if (docRecord is null)
            {
                throw new SdapProblemException(
                    code: "ATTACHMENT_NOT_FOUND",
                    title: "Document Not Found",
                    detail: $"sprk_document '{sprkDocumentId}' was not found.",
                    statusCode: 404,
                    extensions: new Dictionary<string, object>
                    {
                        ["correlationId"] = correlationId,
                        ["failedDocumentId"] = sprkDocumentId,
                        ["attachmentIndex"] = i
                    });
            }

            var driveId = docRecord.GraphDriveId;
            var itemId = docRecord.GraphItemId;
            if (string.IsNullOrWhiteSpace(driveId) || string.IsNullOrWhiteSpace(itemId))
            {
                throw new SdapProblemException(
                    code: "ATTACHMENT_MISSING_SPE_REF",
                    title: "Document Missing SPE Reference",
                    detail: $"sprk_document '{sprkDocumentId}' is missing graphdriveid or graphitemid — file is not stored in SPE.",
                    statusCode: 422,
                    extensions: new Dictionary<string, object>
                    {
                        ["correlationId"] = correlationId,
                        ["failedDocumentId"] = sprkDocumentId,
                        ["attachmentIndex"] = i,
                        ["hasGraphDriveId"] = !string.IsNullOrWhiteSpace(driveId),
                        ["hasGraphItemId"] = !string.IsNullOrWhiteSpace(itemId)
                    });
            }

            _logger.LogDebug(
                "Downloading attachment {Index}/{Total} | SprkDocId: {SprkDocumentId}, DriveId: {DriveId}, ItemId: {ItemId}, CorrelationId: {CorrelationId}",
                i + 1, attachmentDocumentIds.Length, sprkDocumentId, driveId, itemId, correlationId);

            // Get file metadata for name and size
            FileHandleDto? metadata;
            try
            {
                metadata = await _speFileStore.GetFileMetadataAsync(driveId, itemId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to get metadata for attachment | ItemId: {ItemId}, CorrelationId: {CorrelationId}",
                    itemId, correlationId);

                throw new SdapProblemException(
                    code: "ATTACHMENT_DOWNLOAD_FAILED",
                    title: "Attachment Download Failed",
                    detail: $"Failed to retrieve metadata for document '{itemId}': {ex.Message}",
                    statusCode: 502,
                    extensions: new Dictionary<string, object>
                    {
                        ["correlationId"] = correlationId,
                        ["failedDocumentId"] = itemId,
                        ["attachmentIndex"] = i
                    });
            }

            if (metadata is null)
            {
                throw new SdapProblemException(
                    code: "ATTACHMENT_NOT_FOUND",
                    title: "Attachment Not Found",
                    detail: $"Document '{itemId}' was not found in SPE.",
                    statusCode: 404,
                    extensions: new Dictionary<string, object>
                    {
                        ["correlationId"] = correlationId,
                        ["failedDocumentId"] = itemId,
                        ["attachmentIndex"] = i
                    });
            }

            // Track total size and validate before downloading content
            totalSize += metadata.Size ?? 0;
            if (totalSize > MaxTotalAttachmentSizeBytes)
            {
                throw new SdapProblemException(
                    code: "ATTACHMENT_LIMIT_EXCEEDED",
                    title: "Attachments Too Large",
                    detail: $"Total attachment size exceeds {MaxTotalAttachmentSizeBytes / (1024 * 1024)}MB limit. " +
                            $"Total size so far: {totalSize} bytes (at attachment {i + 1} of {attachmentDocumentIds.Length}).",
                    statusCode: 400,
                    extensions: new Dictionary<string, object>
                    {
                        ["correlationId"] = correlationId,
                        ["maxTotalSizeBytes"] = MaxTotalAttachmentSizeBytes,
                        ["currentTotalSizeBytes"] = totalSize,
                        ["failedAtIndex"] = i
                    });
            }

            // Download file content
            Stream? contentStream;
            try
            {
                contentStream = await _speFileStore.DownloadFileAsync(driveId, itemId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to download attachment content | ItemId: {ItemId}, CorrelationId: {CorrelationId}",
                    itemId, correlationId);

                throw new SdapProblemException(
                    code: "ATTACHMENT_DOWNLOAD_FAILED",
                    title: "Attachment Download Failed",
                    detail: $"Failed to download document '{itemId}': {ex.Message}",
                    statusCode: 502,
                    extensions: new Dictionary<string, object>
                    {
                        ["correlationId"] = correlationId,
                        ["failedDocumentId"] = itemId,
                        ["attachmentIndex"] = i
                    });
            }

            if (contentStream is null)
            {
                throw new SdapProblemException(
                    code: "ATTACHMENT_DOWNLOAD_FAILED",
                    title: "Attachment Download Failed",
                    detail: $"Download returned no content for document '{itemId}'.",
                    statusCode: 502,
                    extensions: new Dictionary<string, object>
                    {
                        ["correlationId"] = correlationId,
                        ["failedDocumentId"] = itemId,
                        ["attachmentIndex"] = i
                    });
            }

            // Read stream to byte array for base64 encoding
            byte[] contentBytes;
            await using (contentStream)
            {
                using var ms = new MemoryStream();
                await contentStream.CopyToAsync(ms, ct);
                contentBytes = ms.ToArray();
            }

            attachments.Add(new ChannelAttachment
            {
                Name = metadata.Name ?? "attachment",
                ContentType = InferContentType(metadata.Name),
                Content = contentBytes
            });

            _logger.LogDebug(
                "Attachment {Index}/{Total} prepared | Name: {Name}, Size: {Size} bytes, CorrelationId: {CorrelationId}",
                i + 1, attachmentDocumentIds.Length, metadata.Name, contentBytes.Length, correlationId);
        }

        _logger.LogInformation(
            "All attachments prepared | Count: {Count}, TotalSize: {TotalSize} bytes, CorrelationId: {CorrelationId}",
            attachments.Count, totalSize, correlationId);

        return attachments;
    }

    /// <summary>
    /// Infers a MIME content type from a file name's extension.
    /// Falls back to application/octet-stream for unknown extensions.
    /// </summary>
    private static string InferContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".html" or ".htm" => "text/html",
            ".xml" => "application/xml",
            ".json" => "application/json",
            ".zip" => "application/zip",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            ".eml" => "message/rfc822",
            ".msg" => "application/vnd.ms-outlook",
            _ => "application/octet-stream"
        };
    }

    private static string TruncateTo(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>
    /// Persists the RFC-2822 Internet-Message-Id that the channel sender captured post-send (FR-06),
    /// so inbound replies can thread-match (W1 rung). Channel-agnostic Dataverse write — the Graph
    /// Sent-Items lookup that produces the id lives in <see cref="Channels.EmailChannelSender"/>.
    /// MUST NOT fail the send — any error is logged and swallowed (NFR-06).
    /// </summary>
    private async Task StampInternetMessageIdAsync(
        Guid communicationId,
        string internetMessageId,
        string correlationId,
        CancellationToken ct)
    {
        try
        {
            await _genericEntityService.UpdateAsync(
                "sprk_communication",
                communicationId,
                new Dictionary<string, object> { ["sprk_internetmessageid"] = internetMessageId },
                ct);

            _logger.LogInformation(
                "Stamped Internet-Message-Id on communication {CommunicationId} | CorrelationId: {CorrelationId}",
                communicationId, correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Internet-Message-Id stamp failed (non-fatal) for communication {CommunicationId} | CorrelationId: {CorrelationId}",
                communicationId, correlationId);
        }
    }
}
