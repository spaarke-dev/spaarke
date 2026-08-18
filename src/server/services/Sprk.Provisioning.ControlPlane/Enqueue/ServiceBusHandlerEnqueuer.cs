// -----------------------------------------------------------------------------
// ServiceBusHandlerEnqueuer.cs
//
// L2 CONTROL-PLANE Service Bus enqueue implementation (task 038).
//
// SPEC / ADR references:
//   - spec.md FR-22:  Fire-and-forget execution model. L2 REST endpoints
//                     enqueue via Service Bus + return 202 Accepted <100ms;
//                     handlers run in BFF's IJobHandler infrastructure;
//                     3-level idempotency (this class implements LEVEL 1 —
//                     Service Bus MessageId dedup).
//   - spec.md §4D I5: Same-customer serialization. SessionId = CustomerId
//                     enables per-customer FIFO ordering when the queue is
//                     session-enabled (queue-config concern; enqueue side
//                     always sets SessionId so a future toggle to
//                     requiresSession=true works without code change).
//   - spec.md §4D I3: Cross-tenant leak prevention. CustomerId is the
//                     Cosmos partition key (task 037) AND the Service Bus
//                     SessionId — the two invariants align by design.
//   - ADR-028:        No account-key credential — DefaultAzureCredential
//                     (UAMI at runtime) only. Enforced by ServiceBusModule
//                     via the ServiceBusClient(fqn, TokenCredential) ctor.
//   - ADR-036:        Reuse the IJobHandler infrastructure — this class
//                     MIRRORS BFF's Services/Jobs/JobSubmissionService.cs
//                     patterns (MessageId dedup via ToSafeMessageId,
//                     ApplicationProperties for out-of-body routing, Subject
//                     = handler identifier) without compile-depending on the
//                     BFF assembly.
//
// LEVEL-1 IDEMPOTENCY (Service Bus MessageId):
//
//   MessageId = SHA256_hex( "{HandlerId}|{RunId}|{CustomerId}|{paramHash}" )
//   paramHash = SHA256_hex( ParametersJson )
//
//   Deterministic: two calls with the SAME envelope produce the SAME
//   MessageId. Service Bus duplicate detection (queue-configured) will
//   drop the duplicate within the dedup window. This is the FIRST level
//   of the 3-level idempotency stack:
//
//     Level 1 (this class):     Service Bus MessageId dedup (wire-level).
//     Level 2 (BFF-side):       Redis IdempotencyService (per-process lock).
//     Level 3 (handler body):   Dataverse alt-key upsert (durable dedup).
//
//   The SHA256 hex length is always 64 chars — well within the Service Bus
//   MessageId 128-char cap.
//
// TTL:
//   Per-handler override via ServiceBus:HandlerTimeToLive:{HandlerId}
//   (TimeSpan format, e.g. "00:30:00" or "01:00:00"). Falls back to
//   ServiceBus:DefaultTimeToLive (default 30 minutes) — matches spec.md
//   §4.2 30-min handler tolerance.
//
// LOGGING:
//   The full envelope body is NEVER logged. Only HandlerId, RunId, hashed
//   CustomerId (first 8 chars of SHA256), MessageId, and Queue are logged.
//   ParametersJson may carry KV URI refs which are not secrets themselves
//   but the redaction posture stays conservative per task 025's ArchTest
//   discipline (no cleartext scan-triggerable strings in log output).
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Modules;

namespace Sprk.Provisioning.ControlPlane.Enqueue;

/// <summary>
/// Service Bus implementation of <see cref="IHandlerEnqueuer"/>. Reuses BFF's
/// <c>JobSubmissionService</c> conventions (MessageId dedup, ApplicationProperties
/// for out-of-body routing) without compile-depending on the BFF assembly per
/// the L2 project MUST rule.
/// </summary>
public sealed class ServiceBusHandlerEnqueuer : IHandlerEnqueuer, IAsyncDisposable, IDisposable
{
    private readonly ServiceBusSender _sender;
    private readonly ILogger<ServiceBusHandlerEnqueuer> _logger;
    private readonly ServiceBusModuleOptions _options;

    // Serialize the envelope body with camelCase — parity with CosmosModule +
    // BFF's JobSubmissionService. Reused across calls (thread-safe once frozen).
    private static readonly JsonSerializerOptions BodySerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Constructs the enqueuer bound to the fleet-scoped queue named by
    /// <see cref="ServiceBusModuleOptions.QueueName"/>. The
    /// <see cref="ServiceBusClient"/> owns the AMQP connection; this class
    /// owns the sender it opens for the configured queue.
    /// </summary>
    public ServiceBusHandlerEnqueuer(
        ServiceBusClient serviceBusClient,
        IOptions<ServiceBusModuleOptions> options,
        ILogger<ServiceBusHandlerEnqueuer> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceBusClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
        _sender = serviceBusClient.CreateSender(_options.QueueName);
    }

    /// <inheritdoc />
    public async Task EnqueueAsync(HandlerEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.HandlerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.CustomerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.ParametersJson);

        var body = JsonSerializer.Serialize(envelope, BodySerializerOptions);
        var messageId = ComputeMessageId(envelope);
        var ttl = ResolveTimeToLive(envelope.HandlerId);

        var message = new ServiceBusMessage(body)
        {
            // Level-1 idempotency: deterministic per (HandlerId, RunId,
            // CustomerId, paramHash). Service Bus duplicate detection (queue-
            // configured) drops re-enqueues within the dedup window.
            MessageId = messageId,

            // Per-customer FIFO ordering when the queue is session-enabled
            // (§4D I5). Setting SessionId when the queue does NOT require
            // sessions is a no-op on the wire — no future breakage risk.
            SessionId = envelope.CustomerId,

            // Route + observability: BFF's ServiceBusJobProcessor reads Subject
            // or ApplicationProperties["JobType"] to dispatch to the right
            // IJobHandler without deserializing the body.
            Subject = envelope.HandlerId,
            ContentType = "application/json",

            // Ties log lines on both sides of the wire to the same run.
            CorrelationId = envelope.RunId,

            TimeToLive = ttl,
        };

        message.ApplicationProperties["JobType"] = envelope.HandlerId;
        message.ApplicationProperties["HandlerId"] = envelope.HandlerId;
        message.ApplicationProperties["RunId"] = envelope.RunId;
        message.ApplicationProperties["EnqueuedAt"] = envelope.EnqueuedAt.ToString("o");

        try
        {
            await _sender.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Enqueued handler dispatch: HandlerId={HandlerId} RunId={RunId} " +
                "CustomerIdHash={CustomerIdHash} MessageId={MessageId} Queue={Queue} TtlSeconds={TtlSeconds}",
                envelope.HandlerId,
                envelope.RunId,
                HashCustomerIdForLog(envelope.CustomerId),
                messageId,
                _options.QueueName,
                (int)ttl.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to enqueue handler dispatch: HandlerId={HandlerId} RunId={RunId} " +
                "CustomerIdHash={CustomerIdHash} MessageId={MessageId} Queue={Queue}",
                envelope.HandlerId,
                envelope.RunId,
                HashCustomerIdForLog(envelope.CustomerId),
                messageId,
                _options.QueueName);
            throw;
        }
    }

    /// <summary>
    /// Computes the deterministic Service Bus MessageId for the envelope.
    /// Exposed for smoke-test verification of level-1 idempotency.
    /// </summary>
    internal static string ComputeMessageId(HandlerEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var paramHash = Sha256Hex(envelope.ParametersJson);
        var composite = $"{envelope.HandlerId}|{envelope.RunId}|{envelope.CustomerId}|{paramHash}";
        return Sha256Hex(composite);
    }

    private TimeSpan ResolveTimeToLive(string handlerId)
    {
        if (_options.HandlerTimeToLive is not null
            && _options.HandlerTimeToLive.TryGetValue(handlerId, out var perHandler)
            && perHandler > TimeSpan.Zero)
        {
            return perHandler;
        }

        return _options.DefaultTimeToLive > TimeSpan.Zero
            ? _options.DefaultTimeToLive
            : TimeSpan.FromMinutes(30);
    }

    private static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes); // 64-char upper-hex string.
    }

    // First 8 hex chars of SHA256(customerId) — enough to correlate log lines
    // for the SAME customer without exposing the id itself.
    private static string HashCustomerIdForLog(string customerId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(customerId));
        return Convert.ToHexString(bytes).AsSpan(0, 8).ToString();
    }

    /// <summary>
    /// Disposes the sender. The parent <see cref="ServiceBusClient"/> is DI-
    /// owned as a singleton and is disposed by the container.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Sync <see cref="IDisposable"/> hook — the ASP.NET Core DI container falls
    /// back to sync <see cref="Dispose"/> when scope disposal happens on the
    /// synchronous path (e.g. during exception unwinding from an endpoint handler,
    /// or when the container itself is disposed synchronously). Without this,
    /// dev/prod BFF requests error at scope teardown with
    /// "'ServiceBusHandlerEnqueuer' type only implements IAsyncDisposable. Use
    /// DisposeAsync to dispose the container." — discovered during Phase F
    /// acceptance 2026-08-18 (customer-provisioning-orchestration-r1 Phase F).
    ///
    /// Bridges to DisposeAsync via GetAwaiter().GetResult() — the ServiceBusSender
    /// close is non-blocking work under the hood (idempotent AMQP link teardown)
    /// so blocking briefly here is safe. This is the pattern the .NET runtime
    /// itself uses in <see cref="System.IO.Stream"/> for the same shim.
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
