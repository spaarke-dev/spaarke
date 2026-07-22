using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Notifications;
using Sprk.Bff.Api.Services.Notifications.Envelopes;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// FR-22 producer — emits the notification-spine <see cref="NotificationKind.CommunicationArrived"/> kind
/// for a just-persisted <c>sprk_communication</c> so eligible recipients get an unread-badge + toast
/// AWARENESS signal (spec FR-22 / NFR-03). This is the "producer (task 024)" the
/// <see cref="CommunicationFanOutTargetingService"/> XML doc anticipates — it COMPOSES the three existing
/// spine primitives and adds NO parallel notification mechanism (root CLAUDE.md §10 / §11):
/// <list type="number">
///   <item><see cref="CommunicationFanOutTargetingService"/> → the deduped eligible <c>systemuserid</c>s
///     (ZERO new access logic; internal-only + private-thread gates applied there, fail-closed);</item>
///   <item><see cref="OutboxService.WriteAsync{TEnvelope}"/> → the durable per-user outbox row
///     (Layer B — the source of truth the poll fallback delivers, ADR-041/043);</item>
///   <item><see cref="SignalRDeliveryService.PingUserAsync"/> → the best-effort, at-most-once, SIGNAL-ONLY
///     live ping AFTER the outbox write (write-before-ping is structural — an empty row id throws).</item>
/// </list>
///
/// <para>
/// <b>Awareness only (NFR-03).</b> The emitted <see cref="CommunicationEnvelope"/> carries IDs + minimal
/// DISPLAY metadata + <c>BadgeDelta=+1</c> — never a message body. <see cref="CommunicationEnvelope.Snippet"/>
/// is left NULL (fail-closed): the spine never becomes the content channel; clients keep loading message
/// content via the existing ~5s poll. The live SignalR push is signal-only regardless (no envelope on the wire).
/// </para>
///
/// <para>
/// <b>Best-effort / non-fatal (NFR-02).</b> <see cref="NotifyArrivalAsync"/> never throws — a fan-out,
/// outbox, or ping failure is logged and swallowed so it can NEVER fail the capture/send path that already
/// persisted the record (mirrors <see cref="CommunicationParticipantIndexer"/>'s never-throw contract). Each
/// recipient is emitted independently so one failure does not drop the rest.
/// </para>
///
/// <para>
/// <b>Direction-agnostic.</b> Wired at the inbound capture paths (email + chat) for FR-22, but the envelope's
/// <see cref="CommunicationEnvelope.Direction"/> supports outbound too, so a future task can call this from
/// the outbound send path with no change here (additive).
/// </para>
/// </summary>
public sealed class CommunicationArrivalNotifier
{
    private const string CommunicationEntity = "sprk_communication";
    private const string ThreadEntity = "sprk_communicationthread";

    private const string ThreadLookupField = "sprk_communicationthread";
    private const string InternalOnlyField = "sprk_isinternalonly";
    private const string CreatedOnField = "createdon";
    private const string DirectionField = "sprk_direction";
    private const string TypeField = "sprk_communicationtype";
    private const string FromField = "sprk_from";
    private const string PrivacyStateField = "sprk_privacystate";

    // Message projection: the two fan-out-required security columns (sprk_isinternalonly + createdon), the
    // thread lookup, plus the display/routing fields the envelope needs — and the ADR-024 regarding lookups.
    private static readonly string[] MessageColumns =
        new[] { ThreadLookupField, InternalOnlyField, CreatedOnField, DirectionField, TypeField, FromField }
            .Concat(RegardingFieldMap.AllRegardingFields)
            .ToArray();

    private static readonly string[] ThreadColumns = { PrivacyStateField };

    private readonly IGenericEntityService _entityService;
    private readonly CommunicationFanOutTargetingService _fanOut;
    private readonly OutboxService _outbox;
    private readonly SignalRDeliveryService _signalR;
    private readonly ILogger<CommunicationArrivalNotifier> _logger;

    public CommunicationArrivalNotifier(
        IGenericEntityService entityService,
        CommunicationFanOutTargetingService fanOut,
        OutboxService outbox,
        SignalRDeliveryService signalR,
        ILogger<CommunicationArrivalNotifier> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _fanOut = fanOut ?? throw new ArgumentNullException(nameof(fanOut));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _signalR = signalR ?? throw new ArgumentNullException(nameof(signalR));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Emits <see cref="NotificationKind.CommunicationArrived"/> for <paramref name="communicationId"/>:
    /// loads the message (+ its thread) with the fan-out-required projections, computes the eligible
    /// recipients, then writes an outbox row and best-effort pings each. Never throws (see class remarks).
    /// </summary>
    /// <param name="communicationId">The persisted <c>sprk_communication</c> row id.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task NotifyArrivalAsync(Guid communicationId, CancellationToken ct = default)
    {
        try
        {
            if (communicationId == Guid.Empty)
            {
                _logger.LogWarning("[ARRIVAL] Empty communication id — nothing to notify.");
                return;
            }

            var message = await _entityService.RetrieveAsync(CommunicationEntity, communicationId, MessageColumns, ct);

            var threadRef = message.GetAttributeValue<EntityReference>(ThreadLookupField);
            var threadId = threadRef?.Id ?? Guid.Empty;
            if (threadId == Guid.Empty)
            {
                // No resolved thread yet — awareness fan-out groups by thread; without one there is no
                // grouping key and no reliable recipient set. Skip quietly (the record still exists; the
                // ~5s content poll surfaces it). This is a safe under-notify, never a leak.
                _logger.LogInformation(
                    "[ARRIVAL] communication {CommunicationId} has no resolved thread — awareness skipped (content poll still surfaces it).",
                    communicationId);
                return;
            }

            Entity? thread = null;
            try
            {
                thread = await _entityService.RetrieveAsync(ThreadEntity, threadId, ThreadColumns, ct);
            }
            catch (Exception ex)
            {
                // A missing/unreadable thread is treated as PRIVATE by the fan-out (fail closed → empty
                // fan-out under the default deny-all provider). Pass null and let it under-notify safely.
                _logger.LogWarning(ex,
                    "[ARRIVAL] Could not load thread {ThreadId} for communication {CommunicationId} — fan-out treats it as private (fail closed).",
                    threadId, communicationId);
            }

            var recipients = await _fanOut.GetEligibleRecipientsAsync(message, thread, ct);
            if (recipients.Count == 0)
            {
                _logger.LogInformation(
                    "[ARRIVAL] communication {CommunicationId} thread {ThreadId} — no eligible recipients; no signal emitted.",
                    communicationId, threadId);
                return;
            }

            var envelope = BuildEnvelope(communicationId, threadId, message);
            var (regardingRecordId, regardingRecordType) = ResolveRegarding(message);

            var emitted = 0;
            foreach (var recipientSystemUserId in recipients)
            {
                try
                {
                    // Write-before-ping (ADR-041/043): durable outbox row FIRST, then best-effort live ping.
                    var outboxRowId = await _outbox.WriteAsync(
                        recipientSystemUserId,
                        NotificationKind.CommunicationArrived,
                        envelope,
                        regardingRecordId: regardingRecordId,
                        regardingRecordType: regardingRecordType,
                        expiresAt: null,
                        cancellationToken: ct);

                    await _signalR.PingUserAsync(outboxRowId, recipientSystemUserId, NotificationKind.CommunicationArrived, ct);
                    emitted++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Per-recipient isolation — one failure never drops the rest (NFR-02).
                    _logger.LogWarning(ex,
                        "[ARRIVAL] Emit failed for recipient {RecipientSystemUserId} on communication {CommunicationId} (non-fatal).",
                        recipientSystemUserId, communicationId);
                }
            }

            _logger.LogInformation(
                "[ARRIVAL] Emitted communication-arrived for {Emitted}/{Total} recipient(s) | CommunicationId: {CommunicationId}, ThreadId: {ThreadId}",
                emitted, recipients.Count, communicationId, threadId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never-throw contract: awareness is best-effort and MUST NOT fail the persist path (NFR-02).
            _logger.LogWarning(ex,
                "[ARRIVAL] communication-arrived emit failed (non-fatal) | CommunicationId: {CommunicationId}",
                communicationId);
        }
    }

    private static CommunicationEnvelope BuildEnvelope(Guid communicationId, Guid threadId, Entity message)
    {
        var (regardingRecordId, _) = ResolveRegarding(message);

        return new CommunicationEnvelope
        {
            Kind = NotificationKind.CommunicationArrived,
            CommunicationId = communicationId,
            ThreadId = threadId,
            Channel = MapChannel(message),
            Direction = MapDirection(message),
            // Regarding record for client navigation; falls back to the thread id when no ADR-024 regarding
            // lookup is set (record-less Direct thread) — a required, non-content identifier only.
            RegardingRecordId = string.IsNullOrEmpty(regardingRecordId) ? threadId.ToString() : regardingRecordId,
            SenderDisplay = ToSenderDisplay(message.GetAttributeValue<string>(FromField)),
            // Awareness-only (NFR-02/03): NEVER a snippet/body. The spine is not the content channel.
            Snippet = null,
            BadgeDelta = 1,
        }.Validate();
    }

    /// <summary>Maps <c>sprk_communicationtype</c> → the envelope channel token (<c>"email"|"message"|"sms"</c>).</summary>
    private static string MapChannel(Entity message)
    {
        var type = message.GetAttributeValue<OptionSetValue>(TypeField)?.Value;
        return type switch
        {
            (int)CommunicationType.Email => "email",
            (int)CommunicationType.SMS => "sms",
            _ => "message", // Message / TeamsMessage / Notification / unknown → messaging channel
        };
    }

    /// <summary>Maps <c>sprk_direction</c> → the envelope direction token (<c>"inbound"|"outbound"</c>).</summary>
    private static string MapDirection(Entity message)
    {
        var direction = message.GetAttributeValue<OptionSetValue>(DirectionField)?.Value;
        return direction == (int)CommunicationDirection.Outgoing ? "outbound" : "inbound";
    }

    /// <summary>
    /// Resolves the first-present ADR-024 regarding lookup (priority order) → (recordId string, logicalName).
    /// Returns (null, null) when the message has no regarding (record-less Direct thread).
    /// </summary>
    private static (string? RecordId, string? RecordType) ResolveRegarding(Entity message)
    {
        foreach (var (entityLogicalName, regardingField) in RegardingFieldMap.All)
        {
            var reference = message.GetAttributeValue<EntityReference>(regardingField);
            if (reference is not null && reference.Id != Guid.Empty)
            {
                return (reference.Id.ToString(), entityLogicalName);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Produces a DISPLAY token for the sender WITHOUT leaking a routable address (envelope contract:
    /// "sender DISPLAY NAME ONLY — never an address"). For an email-style <c>from</c> we take the local
    /// part before '@'; a non-address value is used as-is; empty → a neutral placeholder.
    /// </summary>
    private static string ToSenderDisplay(string? from)
    {
        if (string.IsNullOrWhiteSpace(from))
        {
            return "New message";
        }

        var at = from.IndexOf('@');
        return at > 0 ? from[..at] : from;
    }
}
