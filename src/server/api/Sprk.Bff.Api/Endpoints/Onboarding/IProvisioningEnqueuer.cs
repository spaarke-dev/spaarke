using Azure.Messaging.ServiceBus;

namespace Sprk.Bff.Api.Endpoints.Onboarding;

/// <summary>
/// Enqueues a single H0.5 dispatch message onto the L2 provisioning queue
/// (task 042). Kept as a narrow abstraction so the endpoint handler is
/// unit-testable without a live Service Bus namespace.
///
/// <para>
/// Implementations are expected to produce a Service Bus message whose
/// <c>MessageId</c> is deterministic per
/// (<paramref name="handlerId"/>, <paramref name="runId"/>,
/// <paramref name="customerId"/>, <paramref name="parametersJson"/>) so
/// duplicate enqueues collapse at the wire (level-1 idempotency; parity
/// with L2's <c>ServiceBusHandlerEnqueuer.ComputeMessageId</c>).
/// </para>
/// </summary>
public interface IProvisioningEnqueuer
{
    /// <summary>
    /// Enqueues a provisioning-pipeline handler dispatch envelope onto the
    /// L2 fleet-scoped queue.
    /// </summary>
    /// <param name="handlerId">Handler identifier (e.g. <c>"H0.5"</c>, <c>"H0"</c>).</param>
    /// <param name="runId">Run identifier (server-assigned GUID string).</param>
    /// <param name="customerId">Customer short-id (partition key value).</param>
    /// <param name="parametersJson">Serialized handler-specific payload (opaque JSON string).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A task that completes when the send has been acknowledged by the
    /// Service Bus broker.
    /// </returns>
    /// <exception cref="ArgumentException">Any required parameter is null / whitespace.</exception>
    /// <exception cref="ServiceBusException">Broker rejected the send (transient errors surfaced unchanged).</exception>
    Task EnqueueAsync(
        string handlerId,
        string runId,
        string customerId,
        string parametersJson,
        CancellationToken cancellationToken);
}
