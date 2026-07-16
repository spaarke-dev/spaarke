using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Models;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Services.Communication.Channels;

/// <summary>
/// Messaging (ACS Chat) implementation of <see cref="ICommunicationChannelIngestor"/> — the inbound-capture
/// counterpart to <see cref="MessagingChannelSender"/> / <see cref="MessagingArchiver"/> and the FIRST
/// implementation of the net-new ingestor seam (ADR-045 rule 4 / NFR-04). It is the persist-side of the
/// inbound ACS path: given a <see cref="NormalizedMessage"/> already mapped from an ACS event at the pipeline
/// boundary (task 031's normalizer), it creates a <c>sprk_communication</c> (type=Message, Direction=Incoming)
/// record via the canonical <see cref="IGenericEntityService"/>, then invokes shared enrichment best-effort
/// (NFR-02). It REUSES the same persist + enrichment services as the email inbound path
/// (<see cref="IncomingCommunicationProcessor"/>) so inbound capture is NOT forked per channel.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope (messaging-communication-app-r1 task 021):</b> the seam contract + the persist/enrich SHAPE only.
/// The Event Grid ingress + subscription-validation handshake (task 030), and the Service Bus job +
/// ACS-event → envelope normalizer + idempotent dedupe (on <c>sprk_acsmessageid</c>, NFR-03) + DLQ (task 031)
/// call INTO <see cref="IngestAsync"/> — they are NOT built here.
/// </para>
/// <para>
/// <b>Transport-agnostic (ADR-045):</b> this class knows nothing about ACS transport — no
/// <c>Azure.Communication.*</c> type crosses the seam. The already-normalized envelope arrives via
/// <see cref="ChannelIngestRequest"/>; the ACS message/thread ids arrive as the channel-neutral
/// <see cref="ChannelIngestRequest.ProviderMessageId"/> / <see cref="ChannelIngestRequest.ProviderThreadId"/>.
/// </para>
/// <para>
/// <b>Thread assignment is task 040:</b> this ingestor sets only the denormalized <c>sprk_acsthreadid</c>
/// transport id; the <c>sprk_communicationthread</c> LOOKUP (the grouping key) is assigned later by
/// <c>IThreadResolver</c> (task 040) on the shared path.
/// </para>
/// </remarks>
public sealed class MessagingIngestor : ICommunicationChannelIngestor
{
    private readonly IGenericEntityService _genericEntityService;
    private readonly ICommunicationEnrichmentService _enrichmentService;
    private readonly ILogger<MessagingIngestor> _logger;

    public MessagingIngestor(
        IGenericEntityService genericEntityService,
        ICommunicationEnrichmentService enrichmentService,
        ILogger<MessagingIngestor> logger)
    {
        _genericEntityService = genericEntityService;
        _enrichmentService = enrichmentService;
        _logger = logger;
    }

    public CommunicationType SupportedType => CommunicationType.Message;

    public async Task<ChannelIngestResult> IngestAsync(ChannelIngestRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ── Persist the inbound message as sprk_communication (Dataverse is the record) ──
        // Mirrors IncomingCommunicationProcessor.CreateCommunicationRecordAsync's field mapping, but the
        // envelope is already normalized (task 031 supplies it) — no provider re-fetch here. Idempotent
        // dedupe on sprk_acsmessageid is task 031's concern (it wires ChannelIngestResult.WasDuplicate).
        var communicationId = await PersistAsync(request, cancellationToken);

        _logger.LogInformation(
            "Ingested inbound message | CommunicationId: {CommunicationId}, AcsMessageId: {AcsMessageId}, CorrelationId: {CorrelationId}",
            communicationId, request.ProviderMessageId, request.CorrelationId);

        // ── Shared enrichment/association — BEST-EFFORT, NON-FATAL (NFR-02) ──
        // Same entry point the email inbound path invokes, so messaging capture is not forked. A thrown
        // enrichment error MUST NOT fail the persist — the sprk_communication record already exists.
        try
        {
            await _enrichmentService.EnrichAsync(
                communicationId,
                CommunicationDirection.Incoming,
                request.Message,
                archivedDocumentId: null,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Enrichment failed (non-fatal) | CommunicationId: {CommunicationId}, CorrelationId: {CorrelationId}",
                communicationId, request.CorrelationId);
        }

        return new ChannelIngestResult { CommunicationId = communicationId, WasDuplicate = false };
    }

    private async Task<Guid> PersistAsync(ChannelIngestRequest request, CancellationToken ct)
    {
        var message = request.Message;

        // Prefer HTML body when present (mirrors the email inbound content-type decision).
        var bodyContent = message.BodyHtml ?? message.BodyText ?? string.Empty;
        var isHtml = !string.IsNullOrEmpty(message.BodyHtml);

        var sentAt = message.SentAt?.UtcDateTime ?? DateTime.UtcNow;

        var communication = new DataverseEntity("sprk_communication")
        {
            ["sprk_name"] = $"Message: {TruncateTo(message.Subject ?? "(No Subject)", 200)}",
            ["sprk_communicationtype"] = new OptionSetValue((int)CommunicationType.Message),   // 100000004
            ["statuscode"] = new OptionSetValue((int)CommunicationStatus.Delivered),           // 659490003
            ["statecode"] = new OptionSetValue(0),                                             // Active
            ["sprk_direction"] = new OptionSetValue((int)CommunicationDirection.Incoming),     // 100000000
            ["sprk_bodyformat"] = new OptionSetValue(
                isHtml ? (int)BodyFormat.HTML : (int)BodyFormat.PlainText),
            ["sprk_from"] = message.From ?? "unknown",
            ["sprk_to"] = message.To.Count > 0 ? string.Join("; ", message.To) : string.Empty,
            ["sprk_subject"] = message.Subject ?? "(No Subject)",
            ["sprk_body"] = bodyContent,
            ["sprk_sentat"] = sentAt,
            ["sprk_receiveddate"] = sentAt,
        };

        // ACS transport ids (AS-BUILT schema names): dedupe key + denormalized thread id.
        // The sprk_communicationthread LOOKUP (grouping key) is set later by task 040, NOT here.
        if (!string.IsNullOrWhiteSpace(request.ProviderMessageId))
            communication["sprk_acsmessageid"] = request.ProviderMessageId;
        if (!string.IsNullOrWhiteSpace(request.ProviderThreadId))
            communication["sprk_acsthreadid"] = request.ProviderThreadId;

        if (message.Cc.Count > 0)
            communication["sprk_cc"] = string.Join("; ", message.Cc);

        if (message.Attachments is { Count: > 0 })
        {
            communication["sprk_hasattachments"] = true;
            communication["sprk_attachmentcount"] = message.Attachments.Count;
        }

        return await _genericEntityService.CreateAsync(communication, ct);
    }

    private static string TruncateTo(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
