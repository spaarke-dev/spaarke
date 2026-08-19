// -----------------------------------------------------------------------------
// HandlerEnvelope.cs
//
// L2 CONTROL-PLANE Service Bus enqueue envelope (task 038).
//
// PURPOSE:
//   Wire-shape record for a single handler dispatch message. Carries the
//   MINIMUM information the BFF's IJobHandler infrastructure needs to route
//   + execute a provisioning handler:
//
//     - HandlerId       — string handler identifier (e.g. "H4",
//                         "H5-DataverseEnvCreation"). Copied into the
//                         Service Bus message's ApplicationProperties["JobType"]
//                         and Subject so BFF's ServiceBusJobProcessor can
//                         dispatch to the right IJobHandler without
//                         deserializing the body.
//     - RunId           — Cosmos `runs` container document id (the tie back
//                         to spaarke-provisioning/runs/{RunId}). Handlers
//                         must NOT mutate anything if `runs/{RunId}` is
//                         absent — treat as a lost message per §4D I5.
//     - CustomerId      — partition key (§4D I3 / FR-30) + Service Bus
//                         SessionId source (per-customer FIFO ordering /
//                         same-customer serialization per §4D I5).
//     - ParametersJson  — opaque JSON string. The handler owns the schema.
//                         MUST NOT contain cleartext secrets — KV URI refs
//                         only (enforced by task 025 CosmosProvisioningSecretGuard
//                         ArchTest applied to the origin Cosmos row that
//                         feeds this envelope + the enqueuer's log-line
//                         redaction guard).
//     - EnqueuedAt      — UTC timestamp of enqueue. Copied into
//                         ApplicationProperties for BFF-side latency metrics
//                         + reconciler crash-recovery ordering.
//     - Attempt         — (task 107 / DS-2 §4-L1) int, defaults to 0 and is
//                         OMITTED from the wire payload at 0 ("first
//                         enqueue"). Participates in
//                         ServiceBusHandlerEnqueuer.ComputeMessageId so a
//                         §4C RetryableWithCleanup re-enqueue (which
//                         increments Attempt) produces a MessageId distinct
//                         from the original dispatch's — see DESIGN NOTES.
//
// WIRE FORMAT (System.Text.Json, camelCase policy — parity with CosmosModule):
//
//     {
//       "handlerId":     "H4",
//       "runId":         "01J7Q3ZP...",
//       "customerId":    "acme-corp",
//       "parametersJson": "{\"kvUri\":\"@Microsoft.KeyVault(SecretUri=...)\"}",
//       "enqueuedAt":    "2026-08-17T14:00:00Z",
//       "attempt":       1
//     }
//
// DESIGN NOTES:
//   - Attempt (task 107): added specifically to fix an interaction defect
//     between Service Bus level-1 duplicate-detection (task 108) and the
//     §4C RetryableWithCleanup auto-retry path. StateReconcilerService's
//     normal tick-driven ready-set enqueue ALWAYS passes attempt=0 (the
//     field is then omitted from the wire body) so the existing
//     tick-duplicate-suppression byte-stability contract (identical
//     ParametersJson -> identical MessageId across concurrent reconciler
//     instances) is UNCHANGED. Only StateReconcilerService's
//     ApplyHandlerOutcomeAsync re-enqueue path (fired when a handler
//     outcome classifies as FailureClass.RetryableWithCleanup) increments
//     Attempt — otherwise the retry would carry the identical MessageId as
//     the just-consumed original and Service Bus duplicate-detection would
//     silently drop it within the dedup window (spec.md MUST rule:
//     MessageId = SHA256(HandlerId|RunId|CustomerId|paramHash|attempt)).
//     This is NOT the same bookkeeping as Service Bus's own DeliveryCount
//     or the BFF handler's own attempt semantics (ADR-036) — those track
//     wire-level/handler-level redelivery; this field exists solely to keep
//     the deterministic MessageId hash distinguishable across an
//     application-level retry.
//   - Deliberately does NOT carry a CorrelationId. The Service Bus
//     `CorrelationId` field is set by the enqueuer to the RunId so the
//     receiver can log/trace by run without unwrapping the body.
//   - No mutable state — record init-only + required-init props. Handlers
//     receive it via JSON deserialization on the BFF side.
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Sprk.Provisioning.ControlPlane.Enqueue;

/// <summary>
/// Envelope for a single provisioning-handler dispatch enqueued via Service Bus.
/// Consumed by the BFF's <c>IJobHandler</c> infrastructure per spec.md FR-22.
/// </summary>
/// <remarks>
/// The wire format is stable across L2 + BFF releases — treat schema changes
/// as breaking + coordinate a two-sided deployment. New optional fields may
/// be added with defaults; renames + removals require a versioning strategy.
/// </remarks>
public sealed record HandlerEnvelope
{
    /// <summary>
    /// String handler identifier (e.g. <c>"H4"</c>, <c>"H5-DataverseEnvCreation"</c>).
    /// Copied into the Service Bus message's <c>ApplicationProperties["JobType"]</c>
    /// and <c>Subject</c> for dispatch-side routing.
    /// </summary>
    [JsonPropertyName("handlerId")]
    public required string HandlerId { get; init; }

    /// <summary>
    /// Cosmos <c>runs</c> container document id — the tie back to
    /// <c>spaarke-provisioning/runs/{RunId}</c> (task 037's repository).
    /// Also written into Service Bus <c>CorrelationId</c> for correlation.
    /// </summary>
    [JsonPropertyName("runId")]
    public required string RunId { get; init; }

    /// <summary>
    /// Cosmos partition key value (§4D I3 / FR-30) + Service Bus
    /// <c>SessionId</c> source for per-customer FIFO ordering (§4D I5).
    /// </summary>
    [JsonPropertyName("customerId")]
    public required string CustomerId { get; init; }

    /// <summary>
    /// Opaque JSON payload the handler deserializes. MUST NOT contain
    /// cleartext secrets — KV URI refs only.
    /// </summary>
    [JsonPropertyName("parametersJson")]
    public required string ParametersJson { get; init; }

    /// <summary>
    /// UTC timestamp of enqueue. Copied into
    /// <c>ApplicationProperties["EnqueuedAt"]</c> for latency metrics.
    /// </summary>
    [JsonPropertyName("enqueuedAt")]
    public required DateTimeOffset EnqueuedAt { get; init; }

    /// <summary>
    /// Re-enqueue counter (task 107 / DS-2 §4-L1). Defaults to 0 — the
    /// normal tick-driven first-enqueue path — and is OMITTED from the wire
    /// payload at 0 so first-enqueue byte-stability (the actual purpose of
    /// Service Bus level-1 dedup) is unaffected. Only
    /// <see cref="Reconciler.StateReconcilerService.ApplyHandlerOutcomeAsync"/>'s
    /// §4C <c>RetryableWithCleanup</c> re-enqueue path increments this value.
    /// Participates in <see cref="ServiceBusHandlerEnqueuer.ComputeMessageId"/>
    /// per spec.md's MUST rule so a retry's MessageId differs from the
    /// original dispatch's and survives the SB dedup window.
    /// </summary>
    [JsonPropertyName("attempt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Attempt { get; init; }
}
