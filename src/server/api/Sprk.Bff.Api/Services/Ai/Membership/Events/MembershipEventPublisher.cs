// R3 Part 1 Phase 2 — Task 081 (2026-06-22)
// Production publisher that sends MembershipChangedEvent payloads to the
// Service Bus topic configured by MembershipEventPublisherOptions. Wraps
// the existing singleton ServiceBusClient (registered by
// JobProcessingModule) and creates a sender per-publish (Azure SDK best
// practice — senders are cheap; the underlying connection is pooled by
// the client). Fire-and-forget per spec FR-2P2.6 + Q2 — all transport /
// serialization failures are caught + logged as structured warnings; the
// task NEVER faults.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-2P2.6,
//            Q2, NFR-08, D3; projects/spaarke-platform-foundations-r3/notes/
//            event-source-inventory.md §5; sibling
//            src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs
//            (Azure.Messaging.ServiceBus usage convention).

using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Services.Ai.Membership.Events;

/// <summary>
/// Real <see cref="IMembershipEventPublisher"/> impl that publishes
/// <see cref="MembershipChangedEvent"/> payloads to the Service Bus topic
/// configured by <see cref="MembershipEventPublisherOptions.TopicName"/>.
/// Registered as Singleton when
/// <see cref="MembershipEventPublisherOptions.Enabled"/> is <c>true</c>;
/// otherwise <see cref="NullMembershipEventPublisher"/> is registered
/// (ADR-032 P2 Quiet no-op).
/// </summary>
public sealed class MembershipEventPublisher : IMembershipEventPublisher
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ILogger<MembershipEventPublisher> _logger;
    private readonly string _topicName;

    public MembershipEventPublisher(
        ServiceBusClient serviceBusClient,
        IOptions<MembershipEventPublisherOptions> options,
        ILogger<MembershipEventPublisher> logger)
    {
        _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var opts = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _topicName = string.IsNullOrWhiteSpace(opts.TopicName)
            ? "sprk-membership-changes"
            : opts.TopicName;

        _logger.LogInformation(
            "MembershipEventPublisher configured for topic '{TopicName}'",
            _topicName);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Fire-and-forget per FR-2P2.6 + Q2 — exceptions caught + logged at
    /// Warning level with structured properties (entityLogicalName,
    /// entityRecordId, personId, sourceField, correlationId). Never rethrows.
    /// </remarks>
    public async Task PublishAsync(MembershipChangedEvent evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);

        // NFR-08 short-circuit: correlationId is required on the wire
        // contract (System.Text.Json enforces it on deserialize) but the
        // publisher also defends against an empty value at the caller
        // boundary — log + return without publishing rather than emit a
        // payload that consumers would dead-letter.
        if (string.IsNullOrWhiteSpace(evt.CorrelationId))
        {
            _logger.LogWarning(
                "MembershipChangedEvent dropped — empty CorrelationId. " +
                "Entity={EntityLogicalName} Record={EntityRecordId} Person={PersonId} Field={SourceField}",
                evt.EntityLogicalName, evt.EntityRecordId, evt.PersonId, evt.SourceField);
            return;
        }

        ServiceBusSender? sender = null;
        try
        {
            sender = _serviceBusClient.CreateSender(_topicName);

            var body = JsonSerializer.Serialize(evt, MembershipChangedEvent.SerializerOptions);
            var message = new ServiceBusMessage(body)
            {
                ContentType = "application/json",
                CorrelationId = evt.CorrelationId,
                Subject = "MembershipChangedEvent",
            };

            // Surface schemaVersion as an ApplicationProperty so consumer
            // routing / dead-letter rules can filter on it without
            // deserializing the body (Azure Service Bus best practice).
            message.ApplicationProperties["schemaVersion"] = evt.SchemaVersion;
            message.ApplicationProperties["mutationType"] = evt.MutationType.ToString();
            message.ApplicationProperties["entityLogicalName"] = evt.EntityLogicalName;

            await sender.SendMessageAsync(message, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Published MembershipChangedEvent to topic '{TopicName}'. " +
                "Entity={EntityLogicalName} Record={EntityRecordId} Person={PersonId} " +
                "Field={SourceField} Mutation={MutationType} CorrelationId={CorrelationId}",
                _topicName, evt.EntityLogicalName, evt.EntityRecordId, evt.PersonId,
                evt.SourceField, evt.MutationType, evt.CorrelationId);
        }
        catch (Exception ex)
        {
            // FR-2P2.6 + Q2 (fire-and-forget): publish failure NEVER
            // propagates. Log structured warning so operators can
            // correlate via CorrelationId + the nightly recon job
            // (task 085) closes the gap.
            _logger.LogWarning(
                ex,
                "Failed to publish MembershipChangedEvent to topic '{TopicName}'. " +
                "Entity={EntityLogicalName} Record={EntityRecordId} Person={PersonId} " +
                "Field={SourceField} Mutation={MutationType} CorrelationId={CorrelationId}. " +
                "Recon job will reconcile.",
                _topicName, evt.EntityLogicalName, evt.EntityRecordId, evt.PersonId,
                evt.SourceField, evt.MutationType, evt.CorrelationId);
        }
        finally
        {
            if (sender is not null)
            {
                try
                {
                    await sender.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Sender dispose failed (non-fatal)");
                }
            }
        }
    }
}
