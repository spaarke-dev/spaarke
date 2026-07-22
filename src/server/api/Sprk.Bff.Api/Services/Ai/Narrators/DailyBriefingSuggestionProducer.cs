using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Notifications;
using Sprk.Bff.Api.Services.Notifications.Envelopes;

namespace Sprk.Bff.Api.Services.Ai.Narrators;

/// <summary>
/// The Daily-Briefing <c>kind=suggestion</c> producer (spec FR-15 / NFR-03) — the proactive leg of
/// the briefing, a SIBLING of <see cref="DailyBriefingNarrator"/> (NOT inside it — the narrator stays
/// narration-only per the verified substrate correction). It reads the SAME data the composite already
/// collected (the high-priority items) and, for each candidate, applies TWO gates BEFORE any write:
/// <list type="number">
///   <item><b>Grounding (ADR-039)</b> — a candidate is admissible only if every fact it asserts traces
///     to real collected source data: a high-priority item with a non-empty <c>EntityType</c>, a
///     parseable record <c>EntityId</c>, and a <c>Name</c>. Nothing is invented or inferred beyond the
///     collector's output.</item>
///   <item><b>Gate (ADR-041, <c>origin=proactive</c>)</b> — a declared-metadata admit decision. Per the
///     task-041 precedent (owner decision 2026-07-22), this reuses the gate DISCIPLINE (like
///     <c>PendingPlanManager.RequiresConfirmation</c>: a decision from declared metadata) WITHOUT
///     re-entering that gate's chat/SSE Redis suspend-resume machinery (there is no live session). Admit
///     iff the proactive kill-switch is on (<see cref="SuggestionGateOptions.Enabled"/>) AND the item is
///     confirm-worthy by its DECLARED reason (<c>HighPriority</c> or <c>Monitor</c> — why the collector
///     surfaced it). This producer introduces NO second/bespoke scoring decider.</item>
/// </list>
/// Only a candidate that passes BOTH gates yields exactly one <c>kind=suggestion</c> outbox row (task 013
/// <see cref="SuggestionEnvelope"/>) written EXPLICITLY via the task-012 outbox service, followed by a
/// best-effort Layer-C ping (task 020, outbox-before-ping). A candidate that fails EITHER gate produces
/// ZERO rows and the no-action decision is logged (mirrors the FR-12 no-match/below-threshold precedent).
/// </summary>
/// <remarks>
/// <para>
/// <b>Non-fatal + spine-is-dumb-transport.</b> The whole run is wrapped so any failure is logged and
/// swallowed — it NEVER fails the briefing render that invoked it. The envelope carries IDENTIFIERS +
/// minimal display metadata + <c>regardingRecordId</c> only (NFR-02/03); <c>Snippet</c> is null and
/// <c>ActionHint</c> is never a pre-authorized token — the client re-grounds via the BFF at action time.
/// </para>
/// <para>
/// <b>Placement Justification (root §10 / §11).</b> New component — nothing else evaluates proactive
/// suggestions at briefing time. It cannot extend <see cref="DailyBriefingNarrator"/> (narration-only, no
/// write surface — the substrate correction) nor <see cref="DailyBriefingCompositeService"/> (the dispatch
/// boundary; a grounded+gated write producer is a distinct responsibility). Cost-of-doing-nothing: FR-15's
/// proactive leg does not exist — the briefing only narrates, never proposes an action. It lives in
/// <c>Services/Ai/Narrators/</c> beside its data source and depends "up" into the Notifications spine infra
/// (outbox + delivery); ZERO AI-internal injection (ADR-013 clean — it reads already-collected view models,
/// not <c>Services/Ai/</c> internals). Scoped (matches the Scoped composite that invokes it; its outbox +
/// delivery deps are singletons — safe to inject into a Scoped consumer).
/// </para>
/// </remarks>
public sealed class DailyBriefingSuggestionProducer
{
    private const string SuggestionSource = "daily-briefing";
    private const string ReviewActionHint = "review";

    private readonly OutboxService _outbox;
    private readonly SignalRDeliveryService _delivery;
    private readonly SuggestionGateOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DailyBriefingSuggestionProducer> _logger;

    public DailyBriefingSuggestionProducer(
        OutboxService outbox,
        SignalRDeliveryService delivery,
        IOptions<SuggestionGateOptions> options,
        ILogger<DailyBriefingSuggestionProducer> logger,
        TimeProvider? timeProvider = null)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Evaluates each collected high-priority item as a proactive-suggestion candidate and writes a
    /// <c>kind=suggestion</c> outbox row for each that passes BOTH grounding (ADR-039) and the proactive
    /// gate (ADR-041). Fully non-fatal (NFR-03/05): never throws back into the briefing run.
    /// </summary>
    /// <param name="systemUserId">The briefing recipient's Dataverse systemuserid (the outbox row owner).</param>
    /// <param name="highPriorityItems">The collector's already-computed high-priority items (the grounding source).</param>
    /// <param name="ct">Cancellation token from the briefing run.</param>
    /// <returns>The number of <c>kind=suggestion</c> rows written (0 when disabled / nothing grounded+gated).</returns>
    public async Task<int> ProduceAsync(
        Guid systemUserId,
        IReadOnlyList<HighPriorityItemDto> highPriorityItems,
        CancellationToken ct = default)
    {
        try
        {
            if (systemUserId == Guid.Empty || highPriorityItems is null || highPriorityItems.Count == 0)
            {
                return 0;
            }

            var written = 0;
            foreach (var item in highPriorityItems)
            {
                if (written >= _options.MaxPerRun)
                {
                    _logger.LogInformation(
                        "[suggestion] per-run cap {MaxPerRun} reached for systemuserid={SystemUserId}; remaining candidates skipped.",
                        _options.MaxPerRun, systemUserId);
                    break;
                }

                // Gate 1 — GROUNDING (ADR-039): the candidate must trace to real collected source data.
                if (!IsGrounded(item, out var recordId))
                {
                    _logger.LogInformation(
                        "[suggestion] candidate ungrounded (entityType='{EntityType}', entityId='{EntityId}', name-empty={NameEmpty}) — no row (ADR-039).",
                        item.EntityType, item.EntityId, string.IsNullOrWhiteSpace(item.Name));
                    continue;
                }

                // Gate 2 — PROACTIVE GATE (ADR-041, origin=proactive): declared-metadata admit decision.
                if (!IsProactivelyAdmitted(item, out var denyReason))
                {
                    _logger.LogInformation(
                        "[suggestion] candidate for {EntityType}:{EntityId} denied by proactive gate (origin=proactive, reason={Reason}) — no row (ADR-041).",
                        item.EntityType, recordId, denyReason);
                    continue;
                }

                // Grounded + gated → write exactly one kind=suggestion row (outbox FIRST), then best-effort ping.
                var envelope = BuildEnvelope(item, recordId);

                var outboxRowId = await _outbox.WriteAsync(
                    systemUserId,
                    NotificationKind.Suggestion,
                    envelope,
                    regardingRecordId: recordId.ToString(),
                    regardingRecordType: string.IsNullOrWhiteSpace(item.EntityType) ? null : item.EntityType,
                    expiresAt: envelope.ExpiresAt,
                    cancellationToken: ct).ConfigureAwait(false);

                await _delivery
                    .PingUserAsync(outboxRowId, systemUserId, NotificationKind.Suggestion, ct)
                    .ConfigureAwait(false);

                written++;
                _logger.LogInformation(
                    "[suggestion] wrote grounded+gated suggestion {SuggestionId} (source={Source}, actionHint={ActionHint}) for {EntityType}:{EntityId} → outbox {OutboxRowId}, owner {SystemUserId}.",
                    envelope.SuggestionId, envelope.Source, envelope.ActionHint, item.EntityType, recordId, outboxRowId, systemUserId);
            }

            return written;
        }
        catch (Exception ex)
        {
            // NFR-03/05: the suggestion producer is non-fatal — it never fails the briefing render.
            _logger.LogWarning(ex,
                "[suggestion] producer failed (non-fatal) for systemuserid={SystemUserId}.", systemUserId);
            return 0;
        }
    }

    /// <summary>ADR-039 grounding: a candidate is grounded iff it maps to a real collected item with a
    /// non-empty entity type, a parseable record id, and a display name (nothing invented).</summary>
    private static bool IsGrounded(HighPriorityItemDto item, out Guid recordId)
    {
        recordId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(item.EntityType) || string.IsNullOrWhiteSpace(item.Name))
        {
            return false;
        }

        return Guid.TryParse(item.EntityId, out recordId) && recordId != Guid.Empty;
    }

    /// <summary>ADR-041 proactive gate (declared-metadata admit decision, <c>origin=proactive</c>): admit
    /// iff the proactive kill-switch is on AND the item is confirm-worthy by its DECLARED reason
    /// (HighPriority or Monitor — the collector's stated reason for surfacing it). No bespoke scoring.</summary>
    private bool IsProactivelyAdmitted(HighPriorityItemDto item, out string denyReason)
    {
        if (!_options.Enabled)
        {
            denyReason = "proactive-suggestions-disabled";
            return false;
        }

        if (!item.HighPriority && !item.Monitor)
        {
            denyReason = "not-confirm-worthy";
            return false;
        }

        denyReason = string.Empty;
        return true;
    }

    /// <summary>Builds the <see cref="SuggestionEnvelope"/> (kind=suggestion) — IDs + minimal display
    /// metadata only (NFR-02/03). <c>Snippet</c> is null; <c>ActionHint</c> is a renderer hint, never a token.</summary>
    private SuggestionEnvelope BuildEnvelope(HighPriorityItemDto item, Guid recordId)
        => new SuggestionEnvelope
        {
            Kind = NotificationKind.Suggestion,
            SuggestionId = Guid.NewGuid(),
            Source = SuggestionSource,
            RegardingRecordId = recordId.ToString(),
            Title = $"Review {item.Name}",
            Snippet = null, // NFR-02/03: content is never placed on the spine.
            ActionHint = ReviewActionHint,
            ExpiresAt = _timeProvider.GetUtcNow().AddHours(_options.TtlHours),
        }.Validate();
}
