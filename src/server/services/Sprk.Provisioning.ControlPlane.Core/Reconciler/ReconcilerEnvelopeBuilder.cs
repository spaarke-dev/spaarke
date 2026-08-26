// -----------------------------------------------------------------------------
// ReconcilerEnvelopeBuilder.cs
//
// L2 CONTROL-PLANE shared deterministic-envelope construction (task 104,
// Phase C'' Wave G-1). Extracted out of StateReconcilerService.BuildEnvelope
// so BOTH StateReconcilerService (the normal tick-driven ready-set enqueue
// path, attempt=0 default) AND HandlerOutcomeApplier (the §4C
// RetryableWithCleanup auto-retry re-enqueue path, attempt>0) build
// byte-identical envelopes from the SAME single implementation -- avoiding a
// second hand-copy of the ParametersJson serializer options + payload shape
// that would silently drift the two paths' MessageId derivation apart
// (CLAUDE.md §11 -- one component doing this well beats two that partially
// overlap).
//
// CALLERS:
//   - StateReconcilerService.BuildEnvelope(handlerId, run, attempt = 0) --
//     unchanged public signature; now a one-line delegate.
//   - HandlerOutcomeApplier's §4C RetryableWithCleanup re-enqueue path.
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;

namespace Sprk.Provisioning.ControlPlane.Reconciler;

/// <summary>
/// Builds deterministic <see cref="HandlerEnvelope"/> instances for
/// reconciler-initiated dispatches (both the normal tick-driven path and the
/// §4C auto-retry path). Internal -- consumed only from within this project's
/// Reconciler namespace.
/// </summary>
internal static class ReconcilerEnvelopeBuilder
{
    /// <summary>
    /// Serializer for the enqueue payload. camelCase parity with
    /// <see cref="ServiceBusHandlerEnqueuer"/> + Cosmos wire -- required so
    /// the deterministic ParametersJson (an ingredient of the MessageId hash)
    /// is stable across L2 instances.
    /// </summary>
    private static readonly JsonSerializerOptions ParametersJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>
    /// Builds the dispatch envelope for a reconciler-initiated handler
    /// enqueue. The payload is deterministic: same run + same handler ID +
    /// same <paramref name="attempt"/> produces identical ParametersJson
    /// bytes, so <see cref="ServiceBusHandlerEnqueuer.ComputeMessageId"/>
    /// yields the same MessageId across concurrent reconciler instances --
    /// Service Bus dedup collapses them into one message (level-1
    /// idempotency).
    /// </summary>
    /// <param name="handlerId">Handler to dispatch.</param>
    /// <param name="run">Run supplying RunId/CustomerId for the envelope.</param>
    /// <param name="timeProvider">Clock for <see cref="HandlerEnvelope.EnqueuedAt"/> -- NEVER DateTime.UtcNow (docs/standards/TEST-ARCHITECTURE.md §4).</param>
    /// <param name="attempt">
    /// Task 107 / DS-2 §4-L1: 0 for the normal tick-driven ready-set enqueue
    /// path (default -- preserves existing byte-stability / tick-duplicate-
    /// suppression). A value &gt; 0 ONLY on the §4C <c>RetryableWithCleanup</c>
    /// re-enqueue path, so the retry's MessageId differs from the original
    /// dispatch's and survives Service Bus level-1 duplicate detection.
    /// </param>
    public static HandlerEnvelope Build(
        string handlerId,
        ProvisioningRun run,
        TimeProvider timeProvider,
        int attempt = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerId);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var payload = new ReconcilerEnqueuePayload
        {
            CustomerId = run.CustomerId,
            RunId = run.RunId,
            Action = "reconciler-advance",
            HandlerId = handlerId,
        };

        return new HandlerEnvelope
        {
            HandlerId = handlerId,
            RunId = run.RunId,
            CustomerId = run.CustomerId,
            ParametersJson = JsonSerializer.Serialize(payload, ParametersJsonOptions),
            EnqueuedAt = timeProvider.GetUtcNow(),
            Attempt = attempt,
        };
    }

    /// <summary>
    /// Deterministic envelope payload for reconciler-initiated dispatches.
    /// Handlers read run parameters from Cosmos via
    /// <see cref="Repositories.IProvisioningRunRepository"/> (not from the
    /// envelope) -- this payload is only routing/observability metadata.
    /// Byte-stable so the derived MessageId is stable. Moved verbatim from
    /// the pre-extraction <c>StateReconcilerService.ReconcilerEnqueuePayload</c>
    /// nested record (task 104).
    /// </summary>
    internal sealed record ReconcilerEnqueuePayload
    {
        [JsonPropertyName("customerId")]
        public string CustomerId { get; init; } = string.Empty;

        [JsonPropertyName("runId")]
        public string RunId { get; init; } = string.Empty;

        [JsonPropertyName("action")]
        public string Action { get; init; } = string.Empty;

        [JsonPropertyName("handlerId")]
        public string HandlerId { get; init; } = string.Empty;
    }
}
