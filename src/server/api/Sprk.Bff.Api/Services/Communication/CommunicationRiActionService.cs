using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Notifications;
using Sprk.Bff.Api.Services.Notifications.Envelopes;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// The comms Responsive-Intelligence action orchestrator (spec FR-13). When task 041's policy gate
/// AUTHORIZES an assessed communication, this executes the configured RI action end-to-end by CONVERGING
/// four already-built seams — it builds NO new infrastructure, only composes existing ones in the correct
/// order:
/// <list type="number">
///   <item>the Layer-A action seam (task 031 <see cref="IActionSeam"/>, ADR-013) creates the domain
///     record (a follow-up <c>task</c>) — never a direct Dataverse write;</item>
///   <item>the Layer-B outbox (task 012 <see cref="OutboxService"/>) durably records a
///     <c>kind=communication-assessed</c> row FIRST;</item>
///   <item>the Layer-C delivery leg (task 020 <see cref="SignalRDeliveryService"/>) best-effort pings
///     AFTER the outbox write (outbox-before-ping, ADR-041/043);</item>
///   <item>the ONE platform appnotification writer (<see cref="NotificationService.CreateNotificationAsync"/>)
///     mirrors the action so it surfaces in Daily Briefing (spec Success Criterion 2).</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>Called ONLY on authorize.</b> <see cref="RuleGatedAssessedConsumer"/> runs the task-041 gate and
/// invokes this service exclusively inside its <c>if (decision.Authorize)</c> branch. A DENY short-circuits
/// upstream — no seam call, no outbox row, no ping, no appnotification occur for a denied assessment
/// (spec constraint / acceptance criterion). This service additionally re-checks
/// <see cref="CommunicationRuleDecision.Authorize"/> defensively and returns immediately if false.
/// </para>
/// <para>
/// <b>Recipient = the assessed communication's owner.</b> The <see cref="CommunicationAssessedSignal"/> carries
/// no recipient, so this proving producer targets <c>sprk_communication.ownerid</c> — the user responsible for
/// the communication receives the RI task + the Daily-Briefing notification. A richer matter-team fan-out (as in
/// task 023/024's <c>communication-arrived</c> path) is a deliberate future enhancement, out of scope for this
/// convergence task (POML: "do not build new infrastructure").
/// </para>
/// <para>
/// <b>Seam action = a <c>task</c>, mirror = the appnotification.</b> The domain action is created via
/// <see cref="IActionSeam.CreateTaskAsync"/> (a distinct record from the appnotification), and the visibility
/// mirror via <see cref="NotificationService.CreateNotificationAsync"/> — the ONE appnotification writer
/// platform-wide. Choosing the task (not the seam's <see cref="IActionSeam.CreateNotificationAsync"/>) for the
/// domain action keeps the two writes distinct and avoids a second appnotification write path.
/// </para>
/// <para>
/// <b>Non-fatal + spine-is-dumb-transport.</b> The whole execution is wrapped so any failure (re-read, seam,
/// outbox, ping, mirror) is logged and swallowed — it NEVER propagates to the enrichment persist path that
/// triggered the assessment (NFR-05), mirroring <see cref="CommunicationArrivedProducer"/>. The outbox envelope
/// carries IDENTIFIERS + minimal display metadata + <c>regardingRecordId</c> ONLY — no body, no privileged
/// content, no pre-authorized action token (NFR-02/NFR-03).
/// </para>
/// <para>
/// <b>Placement Justification (root §10 / §11).</b> New component — nothing else executes the comms-RI action
/// path at the authorize point. It cannot extend <see cref="RuleGatedAssessedConsumer"/> (that type is the gate
/// consumer; keeping the multi-seam execution in a dedicated collaborator keeps the consumer a thin decide-then-
/// delegate). Cost-of-doing-nothing: an authorized assessment produces no observable outcome (no record, no
/// Daily-Briefing notification) — spec Success Criterion 2 fails. It lives in <c>Services/Communication/</c>
/// beside the gate + the arrived producer, depends "up" into the Notifications spine infra + the ADR-013
/// <see cref="IActionSeam"/> facade (never AI-internal types — ADR-013 clean), and adds ZERO new access logic and
/// ZERO new Dataverse write path (the seam + <see cref="NotificationService"/> are the only writers). Concrete
/// singleton (ADR-010) — all deps are singletons.
/// </para>
/// </remarks>
public sealed class CommunicationRiActionService
{
    private const string CommunicationEntity = "sprk_communication";

    private const string OwnerField = "ownerid";
    private const string ThreadLookupField = "sprk_communicationthread";
    private const string CommunicationTypeField = "sprk_communicationtype";
    private const string RegardingIdField = "sprk_regardingrecordid";
    private const string RegardingTypeField = "sprk_regardingrecordtype";

    /// <summary>
    /// Columns re-read off the assessed <c>sprk_communication</c> row — exactly what the RI action + the
    /// task-013 envelope need (owner target, thread grouping, channel label, regarding pointer). Direction
    /// comes from the signal, not a re-read.
    /// </summary>
    private static readonly string[] CommunicationColumns =
    {
        OwnerField, ThreadLookupField, CommunicationTypeField, RegardingIdField, RegardingTypeField,
    };

    private readonly IActionSeam _seam;
    private readonly OutboxService _outbox;
    private readonly SignalRDeliveryService _delivery;
    private readonly NotificationService _notifications;
    private readonly IGenericEntityService _entityService;
    private readonly ILogger<CommunicationRiActionService> _logger;

    public CommunicationRiActionService(
        IActionSeam seam,
        OutboxService outbox,
        SignalRDeliveryService delivery,
        NotificationService notifications,
        IGenericEntityService entityService,
        ILogger<CommunicationRiActionService> logger)
    {
        _seam = seam ?? throw new ArgumentNullException(nameof(seam));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the RI action path for an AUTHORIZED assessed communication: seam record → outbox row →
    /// best-effort ping → appnotification mirror (in that order). Fully non-fatal (NFR-05): never throws.
    /// A non-authorize <paramref name="decision"/> is a defensive no-op (the caller already short-circuits).
    /// </summary>
    /// <param name="signal">The assessed-communication signal (task 040) that the gate authorized.</param>
    /// <param name="decision">The task-041 gate decision — MUST be <see cref="CommunicationRuleDecision.Authorize"/> true.</param>
    /// <param name="ct">Cancellation token from the enrichment path.</param>
    public async Task ExecuteAuthorizedActionsAsync(
        CommunicationAssessedSignal signal,
        CommunicationRuleDecision decision,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(decision);

        // Belt-and-suspenders: DENY produces NO side effect (the caller only calls on authorize; this guards
        // against a future caller wiring it unconditionally).
        if (!decision.Authorize)
        {
            return;
        }

        try
        {
            if (signal.CommunicationId == Guid.Empty)
            {
                return;
            }

            // 1) Re-read the assessed communication for the owner (recipient), thread (envelope grouping),
            //    channel (display label), and regarding pointer. A read failure aborts the whole action
            //    (non-fatal) — better no action than a partial/mis-targeted one.
            Entity communication;
            try
            {
                communication = await _entityService
                    .RetrieveAsync(CommunicationEntity, signal.CommunicationId, CommunicationColumns, ct)
                    .ConfigureAwait(false);
                communication.Id = signal.CommunicationId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[comms-ri] could not re-read assessed communication {CommunicationId}; no RI action taken (non-fatal).",
                    signal.CommunicationId);
                return;
            }

            var ownerRef = communication.GetAttributeValue<EntityReference>(OwnerField);
            if (ownerRef is null || ownerRef.Id == Guid.Empty)
            {
                _logger.LogInformation(
                    "[comms-ri] assessed communication {CommunicationId} has no owner; cannot target an RI action (non-fatal).",
                    signal.CommunicationId);
                return;
            }

            var recipientSystemUserId = ownerRef.Id;
            var threadRef = communication.GetAttributeValue<EntityReference>(ThreadLookupField);
            var threadId = threadRef?.Id ?? Guid.Empty;
            var channel = MapChannel(communication.GetAttributeValue<OptionSetValue>(CommunicationTypeField));
            var regardingId = communication.GetAttributeValue<string>(RegardingIdField);
            var regardingType = communication.GetAttributeValue<string>(RegardingTypeField);

            var subject = BuildActionSubject(signal);

            // 2) Layer-A seam (ADR-013): create the domain RI action (a follow-up task regarding the
            //    communication, owned by the responsible user). A Spaarke task is a sprk_event (event type = Task),
            //    which regards the communication via its typed sprk_regardingcommunication lookup. Never a direct
            //    Dataverse write. Degraded success (TaskId == Guid.Empty) is logged but does not abort below.
            var taskResult = await _seam.CreateTaskAsync(
                new CreateTaskRequest
                {
                    Subject = subject,
                    Description = BuildActionDescription(signal, decision),
                    RegardingObjectId = signal.CommunicationId,
                    RegardingObjectType = CommunicationEntity,
                    OwnerId = recipientSystemUserId,
                },
                ct).ConfigureAwait(false);

            if (!taskResult.Success || taskResult.TaskId == Guid.Empty)
            {
                _logger.LogWarning(
                    "[comms-ri] Layer-A seam task create returned degraded/failed for communication {CommunicationId} (Success={Success}, Error={Error}); continuing to surface the assessment.",
                    signal.CommunicationId, taskResult.Success, taskResult.Error);
            }

            // 3) Layer-B outbox FIRST (kind=communication-assessed). IDs + minimal display metadata +
            //    regardingRecordId only (NFR-02/03). Must precede the ping (ordering is structural —
            //    PingUserAsync requires the outboxRowId this write returns).
            var envelope = BuildEnvelope(signal, threadId, channel, regardingId);

            var outboxRowId = await _outbox.WriteAsync(
                recipientSystemUserId,
                NotificationKind.CommunicationAssessed,
                envelope,
                regardingRecordId: string.IsNullOrWhiteSpace(regardingId) ? null : regardingId,
                regardingRecordType: string.IsNullOrWhiteSpace(regardingType) ? null : regardingType,
                expiresAt: null,
                cancellationToken: ct).ConfigureAwait(false);

            // 4) Layer-C ping AFTER the outbox write. Best-effort: a delivery failure never rolls back the
            //    durable outbox row or the created records (the poll fallback carries the truth).
            await _delivery
                .PingUserAsync(outboxRowId, recipientSystemUserId, NotificationKind.CommunicationAssessed, ct)
                .ConfigureAwait(false);

            // 5) Appnotification mirror via the ONE platform writer (NotificationService), so the RI action is
            //    visible in Daily Briefing (Success Criterion 2). Explicit call — the narrator does NOT write
            //    appnotification (substrate correction 2026-07-20).
            await _notifications.CreateNotificationAsync(
                userId: recipientSystemUserId,
                title: subject,
                body: BuildActionDescription(signal, decision),
                category: "communication",
                priority: 200000000, // Informational
                actionUrl: null,
                regardingId: signal.CommunicationId,
                aiMetadata: null,
                cancellationToken: ct).ConfigureAwait(false);

            _logger.LogInformation(
                "[comms-ri] executed authorized RI action for communication {CommunicationId} (rule {RuleId}) → task {TaskId}, outbox {OutboxRowId}, appnotification mirrored to owner {OwnerId}; privilegeFlagged={PrivilegeFlagged}.",
                signal.CommunicationId, decision.MatchedRuleId, taskResult.TaskId, outboxRowId,
                recipientSystemUserId, decision.PrivilegeFlagged);
        }
        catch (Exception ex)
        {
            // NFR-05: the RI action path is non-fatal — a failure here NEVER fails the enrichment persist that
            // produced the assessment.
            _logger.LogWarning(ex,
                "[comms-ri] RI action path failed (non-fatal) for communication {CommunicationId}.",
                signal.CommunicationId);
        }
    }

    /// <summary>
    /// Builds the <see cref="CommunicationEnvelope"/> (kind=communication-assessed) — IDs + minimal display
    /// metadata only (NFR-02/03). <c>Snippet</c> is always null (content is never placed on the spine).
    /// </summary>
    private static CommunicationEnvelope BuildEnvelope(
        CommunicationAssessedSignal signal, Guid threadId, string channel, string? regardingId)
        => new CommunicationEnvelope
        {
            Kind = NotificationKind.CommunicationAssessed,
            CommunicationId = signal.CommunicationId,
            ThreadId = threadId,
            Channel = channel,
            Direction = MapDirection(signal.Direction),
            RegardingRecordId = regardingId ?? string.Empty,
            SenderDisplay = SenderDisplayFor(channel),
            Snippet = null, // NFR-02/03: content is NEVER placed on the spine.
            BadgeDelta = 1, // one assessed communication warrants attention.
        }.Validate();

    /// <summary>Short, address-free action title (NFR-02/03 — display metadata, never content).</summary>
    private static string BuildActionSubject(CommunicationAssessedSignal signal)
        => string.IsNullOrWhiteSpace(signal.Subject)
            ? "Assessed communication needs review"
            : $"Review assessed communication: {signal.Subject}";

    private static string BuildActionDescription(CommunicationAssessedSignal signal, CommunicationRuleDecision decision)
        => $"An inbound communication was assessed and authorized for a responsive action " +
           $"(confidence {decision.Confidence:0.00} ≥ threshold {decision.Threshold:0.00}). Open the communication to review.";

    /// <summary>Maps <c>sprk_communicationtype</c> → the envelope channel label. Falls back to "message" for
    /// an unclassifiable type (so a valid authorized assessment still surfaces; the label is display-only and
    /// the client re-fetches real details via the access-gated BFF).</summary>
    private string MapChannel(OptionSetValue? type)
    {
        switch (type?.Value)
        {
            case (int)CommunicationType.Email:
                return "email";
            case (int)CommunicationType.Message:
            case (int)CommunicationType.TeamsMessage:
                return "message";
            case (int)CommunicationType.SMS:
                return "sms";
            default:
                _logger.LogInformation(
                    "[comms-ri] communication has an unclassifiable sprk_communicationtype ({Type}); using channel label 'message' (display-only).",
                    type?.Value);
                return "message";
        }
    }

    private static string MapDirection(CommunicationDirection direction)
        => direction == CommunicationDirection.Outgoing ? "outbound" : "inbound";

    /// <summary>
    /// NFR-02/03: <c>senderDisplay</c> is a privacy-safe channel label, never an address (matching
    /// <see cref="CommunicationArrivedProducer"/> — <c>sprk_communication</c> has no display-name column).
    /// </summary>
    private static string SenderDisplayFor(string channel)
        => channel == "email" ? "Assessed email" : "Assessed message";
}
