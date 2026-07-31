using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Notifications;
using Sprk.Bff.Api.Services.Notifications.Envelopes;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// The single, spine-owned <c>communication-arrived</c> producer (spec FR-09 / NFR-05 / NFR-08). Emits the
/// Layer-C "a new communication persisted; surfaces should refresh" signal for EVERY <c>sprk_communication</c>
/// write — inbound capture (email + messaging) and outbound send (email + messaging) — identically.
/// messaging-r3's task 045 consumes THIS event; it MUST NOT wire its own producer (spec Owner Clarification —
/// the spine emits, R3 consumes only).
/// </summary>
/// <remarks>
/// <para>
/// <b>Emit point = post-association, NOT the raw <c>CreateAsync</c> (task-024 deviation, POML step 2/7).</b>
/// The naive "wire at each <c>_genericEntityService.CreateAsync(communication)</c> site" is WRONG: at the raw
/// create the <c>sprk_communicationthread</c> lookup + regarding are not yet stamped (resolvers run afterward)
/// and — decisively — the <c>sprk_communicationparticipant</c> junction that
/// <see cref="CommunicationFanOutTargetingService"/> reads is still EMPTY, so fan-out would return zero and the
/// signal would silently reach no one. Each of the five persist orchestrators therefore calls this producer
/// AFTER its participant-index step (email: <c>IncomingCommunicationProcessor</c> step 4.7; messaging:
/// <c>MessagingIngestor</c>; outbound: <c>CommunicationService.Send{Message,,AsUser}Async</c> after
/// <c>WriteParticipantIndexAsync</c>). See <c>notes/024-communication-arrived-producer-notes.md</c>.
/// </para>
/// <para>
/// <b>Order of operations per recipient:</b> compute the fan-out (task 023) → build the task-013 envelope →
/// for each recipient write the durable task-012 outbox row FIRST, THEN best-effort ping (task 020). The
/// outbox-BEFORE-ping ordering is structural — <see cref="SignalRDeliveryService.PingUserAsync"/> requires the
/// <c>outboxRowId</c> that only exists after the write (ADR-041/043 store-before-render, root CLAUDE.md
/// "outbox BEFORE ping").
/// </para>
/// <para>
/// <b>Non-fatal + spine-is-dumb-transport.</b> The whole emit is wrapped so an exception (re-read, fan-out,
/// outbox write, or ping) is logged and swallowed — it NEVER propagates to or fails the persist call that
/// triggered it (NFR-05), mirroring <see cref="CommunicationParticipantIndexer"/>'s never-throw contract. The
/// envelope carries IDENTIFIERS + minimal display metadata only — no body, no address, no privileged content
/// (NFR-02/NFR-03); clients re-fetch details through the access-checked BFF poll endpoint (task 022).
/// </para>
/// <para>
/// <b>Placement Justification (root §10 / §11).</b> New component — nothing else emits <c>communication-arrived</c>
/// at persistence. It cannot extend <see cref="CommunicationEnrichmentService"/> (that path is assessment-gated and
/// single-call; <c>communication-arrived</c> MUST fire at persistence with NO assessment prerequisite and fans out
/// per-recipient — that is task 040's separate <c>communication_assessed</c> concern). Cost-of-doing-nothing:
/// messaging-r3 task 045 has no spine event to consume and would wire a forbidden second producer. It lives in
/// <c>Services/Communication/</c> beside its fan-out dependency (task 023) and depends "up" into the Notifications
/// spine infra (outbox + delivery) — the correct direction. ZERO new access logic and ZERO AI dependency
/// (ADR-013 clean). Concrete singleton (ADR-010) — all deps are singletons; injected into the three singleton
/// persist orchestrators.
/// </para>
/// </remarks>
public sealed class CommunicationArrivedProducer
{
    private const string CommunicationEntity = "sprk_communication";
    private const string ThreadEntity = "sprk_communicationthread";

    private const string ThreadLookupField = "sprk_communicationthread";
    private const string CommunicationTypeField = "sprk_communicationtype";
    private const string DirectionField = "sprk_direction";
    private const string IsInternalOnlyField = "sprk_isinternalonly";
    private const string CreatedOnField = "createdon";
    private const string RegardingIdField = "sprk_regardingrecordid";
    private const string RegardingTypeField = "sprk_regardingrecordtype";
    private const string ThreadPrivacyStateField = "sprk_privacystate";
    private const string ThreadNameField = "sprk_name";  // thread name for the enriched notification (round-8.4 item 2)
    private const string FromField = "sprk_from";        // sender ("who sent it") for the enriched notification

    // Q2 app-notification mirror. Category is load-bearing for idempotency (recipient + category + regarding=thread).
    private const string CommunicationNotificationCategory = "communication";
    // appnotification.toasttype: Timed (200000000) — emits the clickable "Open" data.actions[]; Hidden suppresses it.
    private const int ToastTypeTimed = 200_000_000;

    /// <summary>
    /// Columns re-read off the persisted <c>sprk_communication</c> row — exactly what the task-013 envelope +
    /// the task-023 fan-out CONTRACT need (fan-out reads <c>sprk_isinternalonly</c> + <c>createdon</c> off the
    /// message entity; a missing projection fail-closes to under-fan, never over-fan).
    /// </summary>
    private static readonly string[] CommunicationColumns = BuildCommunicationColumns();

    private static string[] BuildCommunicationColumns()
    {
        // Base envelope/fan-out columns + the TYPED ADR-024 regarding lookups (RegardingFieldMap) so the Q2
        // app-notification deep-link can resolve the regarding entity's REAL logical name. NOTE: the message's
        // sprk_regardingrecordtype is a LOOKUP to sprk_recordtype_ref, NOT a usable entity-type string (this is
        // the same field ThreadResolver stopped reading as text), so the deep-link must come from the typed
        // lookup, not RegardingTypeField.
        var cols = new List<string>
        {
            ThreadLookupField, CommunicationTypeField, DirectionField,
            IsInternalOnlyField, CreatedOnField,
            RegardingIdField, RegardingTypeField, FromField,
        };
        cols.AddRange(RegardingFieldMap.AllRegardingFields);
        return cols.ToArray();
    }

    // Round-8.4 item 2: the thread carries the ADR-024 regarding lookups + name. Loading them lets the deep-link open
    // the REGARDING record (which hosts the conversation PCF) instead of falling back to the non-navigable
    // sprk_communicationthread record (that fallback was the source of the "Open" → generic MDA error), and lets the
    // notification name the thread.
    private static readonly string[] ThreadColumns =
        new[] { ThreadPrivacyStateField, ThreadNameField }.Concat(RegardingFieldMap.AllRegardingFields).ToArray();

    private readonly IGenericEntityService _entityService;
    private readonly CommunicationFanOutTargetingService _targeting;
    private readonly OutboxService _outbox;
    private readonly SignalRDeliveryService _delivery;
    private readonly IActionSeam _actionSeam;
    private readonly ILogger<CommunicationArrivedProducer> _logger;

    public CommunicationArrivedProducer(
        IGenericEntityService entityService,
        CommunicationFanOutTargetingService targeting,
        OutboxService outbox,
        SignalRDeliveryService delivery,
        IActionSeam actionSeam,
        ILogger<CommunicationArrivedProducer> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _targeting = targeting ?? throw new ArgumentNullException(nameof(targeting));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
        _actionSeam = actionSeam ?? throw new ArgumentNullException(nameof(actionSeam));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Emits <c>communication-arrived</c> for the just-persisted <paramref name="communicationId"/>. Fully
    /// non-fatal (NFR-05): never throws — every failure mode is logged and swallowed so the triggering persist
    /// call always completes.
    /// </summary>
    /// <param name="communicationId">The <c>sprk_communication</c> id whose persist just succeeded (post-association).</param>
    /// <param name="ct">Cancellation token from the persist path.</param>
    public async Task EmitCommunicationArrivedAsync(Guid communicationId, CancellationToken ct = default)
    {
        try
        {
            if (communicationId == Guid.Empty)
            {
                return;
            }

            // 1) Re-read the persisted communication with exactly the projection the envelope + fan-out need.
            //    Centralizing the projection here (not at each of the five heterogeneous persist sites) is why
            //    the producer takes only an id — one spine-owned emit, one contract.
            Entity message;
            try
            {
                message = await _entityService.RetrieveAsync(CommunicationEntity, communicationId, CommunicationColumns, ct);
                message.Id = communicationId; // defensive: fan-out requires a non-empty message.Id.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[communication-arrived] could not re-read communication {CommunicationId}; nothing emitted (non-fatal).",
                    communicationId);
                return;
            }

            // 2) A thread lookup is required — the envelope's threadId is required, and a null thread routes
            //    fan-out through the private-thread deny-all gate (empty) anyway. No thread yet ⇒ skip (the poll
            //    fallback + the next event refresh the surface); do NOT fabricate a thread id.
            var threadRef = message.GetAttributeValue<EntityReference>(ThreadLookupField);
            if (threadRef is null || threadRef.Id == Guid.Empty)
            {
                _logger.LogInformation(
                    "[communication-arrived] communication {CommunicationId} has no thread yet; skipping emit (envelope requires threadId; fan-out would be empty).",
                    communicationId);
                return;
            }

            // 2b) Thread privacy posture for fan-out (sprk_privacystate). A read failure ⇒ null thread ⇒ fan-out
            //     treats it as private/unknown (fail closed), never over-fans.
            Entity? thread = null;
            try
            {
                thread = await _entityService.RetrieveAsync(ThreadEntity, threadRef.Id, ThreadColumns, ct);
                thread.Id = threadRef.Id;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[communication-arrived] could not read thread {ThreadId} for communication {CommunicationId}; treating as private (fail closed).",
                    threadRef.Id, communicationId);
                thread = null;
            }

            // 3) Fan-out recipients (task 023) — fail-closed; composes the existing access primitives, no new logic.
            var recipients = await _targeting.GetEligibleRecipientsAsync(message, thread, ct);
            if (recipients.Count == 0)
            {
                _logger.LogInformation(
                    "[communication-arrived] communication {CommunicationId} thread {ThreadId}: 0 eligible recipients; nothing to deliver.",
                    communicationId, threadRef.Id);
                return;
            }

            // 4) Build the task-013 envelope ONCE (it describes the communication, identical for every recipient).
            var envelope = BuildEnvelope(communicationId, threadRef.Id, message);
            if (envelope is null)
            {
                return; // unclassifiable channel — logged in BuildEnvelope.
            }

            var regardingId = message.GetAttributeValue<string>(RegardingIdField);
            var regardingType = message.GetAttributeValue<string>(RegardingTypeField);

            // Q2 (2026-07-28) + round-8.4 item 2: deep-link target for the MDA bell notification = the REGARDING record
            // (it hosts the Communications conversation panel). Resolve from the TYPED ADR-024 lookup for a reliable
            // entity logical name — first from the MESSAGE (auto-threaded messages may not have it stamped yet), then
            // from the THREAD (which carries the regarding), and only THEN fall back to the thread record. The thread
            // fallback opened `sprk_communicationthread`, which has no navigable app form → the "Open" click errored.
            var (linkEntityType, linkId) =
                ResolveTypedRegarding(message)
                ?? (thread is not null ? ResolveTypedRegarding(thread) : null)
                ?? (ThreadEntity, threadRef.Id);
            var actionUrl = BuildRecordDeepLink(linkEntityType, linkId);

            // Enriched notification content (round-8.4 item 2): name the thread + who sent it. Thread name + sender are
            // metadata (not body/address content) so this stays within the signal-only posture (NFR-02/03).
            var threadName = thread?.GetAttributeValue<string>(ThreadNameField);
            var senderFrom = message.GetAttributeValue<string>(FromField);
            var notifyTitle = string.IsNullOrWhiteSpace(threadName)
                ? envelope.SenderDisplay
                : $"{envelope.SenderDisplay} · {threadName}";
            var notifyBody = string.IsNullOrWhiteSpace(senderFrom)
                ? "Open to view the conversation."
                : $"From {senderFrom} — open to view the conversation.";

            // 5) Per recipient: durable outbox row FIRST, then best-effort ping. Ordering is structural
            //    (PingUserAsync requires the outboxRowId that only exists after the write). Then mirror a
            //    persistent, clickable Dataverse app-notification (Q2) via the Layer-A IActionSeam facade.
            foreach (var recipientSystemUserId in recipients)
            {
                var outboxRowId = await _outbox.WriteAsync(
                    recipientSystemUserId,
                    NotificationKind.CommunicationArrived,
                    envelope,
                    regardingRecordId: string.IsNullOrWhiteSpace(regardingId) ? null : regardingId,
                    regardingRecordType: string.IsNullOrWhiteSpace(regardingType) ? null : regardingType,
                    expiresAt: null,
                    cancellationToken: ct);

                await _delivery.PingUserAsync(outboxRowId, recipientSystemUserId, NotificationKind.CommunicationArrived, ct);

                // App-notification MIRROR (Q2, mirrors the sanctioned CommunicationRiActionService pattern). A
                // persistent, clickable bell notification in the MDA notification center, deep-linked to the
                // regarding record. Reuses the ONE Layer-A dispatch path (IActionSeam, ADR-013); dedups per
                // (recipient + "communication" + thread) so a busy thread yields ONE unread bell, not one per
                // message. Signal-only (NFR-02/03): title is a channel label, body is a generic prompt — no
                // address/content. Non-fatal: IActionSeam.CreateNotificationAsync never throws (returns a result),
                // and the outer try/catch swallows anything else so the persist path is never affected (NFR-05).
                var notify = await _actionSeam.CreateNotificationAsync(
                    new CreateNotificationRequest
                    {
                        Title = notifyTitle, // "New message · {thread name}" (round-8.4 item 2)
                        Body = notifyBody,   // "From {sender} — open to view the conversation." (round-8.4 item 2)
                        RecipientId = recipientSystemUserId,
                        Category = CommunicationNotificationCategory,
                        RegardingId = threadRef.Id,      // idempotency key = thread → one unread bell per thread
                        RegardingType = ThreadEntity,
                        ToastType = ToastTypeTimed,      // Timed → emits the clickable "Open" action (NOT Hidden)
                        ActionUrl = actionUrl,
                        Source = CommunicationNotificationCategory,
                        CorrelationId = communicationId.ToString(),
                    },
                    ct);

                if (notify is { Success: false, Skipped: false })
                {
                    _logger.LogDebug(
                        "[communication-arrived] app-notification not created for recipient {Recipient} on thread {ThreadId}: {Error}",
                        recipientSystemUserId, threadRef.Id, notify.Error);
                }
            }

            _logger.LogInformation(
                "[communication-arrived] emitted for communication {CommunicationId} thread {ThreadId} channel {Channel} to {RecipientCount} recipient(s).",
                communicationId, threadRef.Id, envelope.Channel, recipients.Count);
        }
        catch (Exception ex)
        {
            // NFR-05: the producer is non-fatal — an exception here NEVER fails the persist call that triggered it.
            _logger.LogWarning(ex,
                "[communication-arrived] producer failed (non-fatal) for communication {CommunicationId}.",
                communicationId);
        }
    }

    /// <summary>
    /// Builds the <see cref="CommunicationEnvelope"/> from the re-read row — IDs + minimal display metadata only.
    /// Returns null when the channel cannot be classified (nothing emitted).
    /// </summary>
    private CommunicationEnvelope? BuildEnvelope(Guid communicationId, Guid threadId, Entity message)
    {
        var channel = MapChannel(message.GetAttributeValue<OptionSetValue>(CommunicationTypeField));
        if (channel is null)
        {
            _logger.LogInformation(
                "[communication-arrived] communication {CommunicationId} has an unclassifiable sprk_communicationtype; skipping emit.",
                communicationId);
            return null;
        }

        return new CommunicationEnvelope
        {
            Kind = NotificationKind.CommunicationArrived,
            CommunicationId = communicationId,
            ThreadId = threadId,
            Channel = channel,
            Direction = MapDirection(message.GetAttributeValue<OptionSetValue>(DirectionField)),
            RegardingRecordId = message.GetAttributeValue<string>(RegardingIdField) ?? string.Empty,
            SenderDisplay = SenderDisplayFor(channel),
            Snippet = null, // NFR-02/03: content is NEVER placed on the spine in this task (privacy-conservative).
            BadgeDelta = 1, // one new communication arrived.
        }.Validate();
    }

    private static string? MapChannel(OptionSetValue? type) => type?.Value switch
    {
        (int)CommunicationType.Email => "email",
        (int)CommunicationType.Message => "message",
        (int)CommunicationType.TeamsMessage => "message",
        (int)CommunicationType.SMS => "sms",
        _ => null,
    };

    private static string MapDirection(OptionSetValue? direction) =>
        direction?.Value == (int)CommunicationDirection.Outgoing ? "outbound" : "inbound";

    /// <summary>
    /// NFR-02/03: <c>senderDisplay</c> is "display NAME only — never an address". <c>sprk_communication</c> has
    /// no display-name column and <c>NormalizedMessage.From</c> is an address, so we emit a privacy-safe channel
    /// label rather than leak the address; the client re-fetches the real sender via the access-gated BFF. If a
    /// display-name column is added later, populate it here.
    /// </summary>
    private static string SenderDisplayFor(string channel) => channel == "email" ? "New email" : "New message";

    /// <summary>
    /// Resolves the message's regarding record as a (entity-logical-name, id) pair from the TYPED ADR-024
    /// lookups (<see cref="RegardingFieldMap"/>), in family priority order — the reliable source of the regarding
    /// entity type for the Q2 deep-link. Returns null for a record-less message (no typed lookup populated), so
    /// the caller falls back to linking the thread record.
    /// </summary>
    private static (string EntityType, Guid Id)? ResolveTypedRegarding(Entity message)
    {
        foreach (var (entityLogicalName, field) in RegardingFieldMap.All)
        {
            if (message.GetAttributeValue<EntityReference>(field) is { } er && er.Id != Guid.Empty)
            {
                return (string.IsNullOrWhiteSpace(er.LogicalName) ? entityLogicalName : er.LogicalName, er.Id);
            }
        }

        return null;
    }

    /// <summary>
    /// Builds a model-driven-app record deep-link (the canonical <c>appnotification.data.actions[].data.url</c>
    /// form) for the notification's clickable "Open" action. The URL is a plain navigation target — NOT a
    /// pre-authorized token (NFR-02/03); the click re-enters an access-checked record surface.
    /// <para>
    /// Round-7 item 11: appends <c>sprk_openconversation=1</c> so the <c>CommunicationConversationPanel</c> PCF on
    /// the target record form auto-opens the Messages modal on load (the PCF reads this off the top-window URL).
    /// This is best-effort: if the MDA strips the custom param, the click still lands on the record (graceful
    /// degradation — the panel just isn't auto-opened). Navigating to the record + opening the conversation is far
    /// more useful than deep-linking to a single message (operator decision, round 7).
    /// </para>
    /// </summary>
    private static string BuildRecordDeepLink(string entityType, Guid id) =>
        $"/main.aspx?pagetype=entityrecord&etn={entityType}&id={id:D}&sprk_openconversation=1";
}
