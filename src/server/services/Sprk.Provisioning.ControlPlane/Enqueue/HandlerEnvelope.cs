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
//
// WIRE FORMAT (System.Text.Json, camelCase policy — parity with CosmosModule):
//
//     {
//       "handlerId":     "H4",
//       "runId":         "01J7Q3ZP...",
//       "customerId":    "acme-corp",
//       "parametersJson": "{\"kvUri\":\"@Microsoft.KeyVault(SecretUri=...)\"}",
//       "enqueuedAt":    "2026-08-17T14:00:00Z"
//     }
//
// DESIGN NOTES:
//   - Deliberately does NOT carry an "Attempt" counter. Service Bus's own
//     DeliveryCount + the BFF handler's own attempt semantics (per ADR-036)
//     own retry accounting. Adding a client-side counter here would double-
//     bookkeep.
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
}
