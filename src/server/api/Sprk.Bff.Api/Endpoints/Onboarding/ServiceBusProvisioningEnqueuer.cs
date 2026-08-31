using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Sprk.Bff.Api.Endpoints.Onboarding;

/// <summary>
/// Service Bus implementation of <see cref="IProvisioningEnqueuer"/> (task
/// 042). Reuses the BFF's DI-registered <see cref="ServiceBusClient"/>
/// singleton (currently registered by
/// <c>Sprk.Bff.Api.Infrastructure.DI.JobProcessingModule</c>) and sends to
/// the L2 queue named in <see cref="OnboardingOptions.QueueName"/> (default
/// <c>sprk-provisioning-jobs</c>, matching L2's <c>ServiceBusModule</c>).
///
/// <para>
/// <b>MessageId computation MIRRORS L2's <c>ServiceBusHandlerEnqueuer.ComputeMessageId</c></b>
/// (SHA256 hex of <c>HandlerId|RunId|CustomerId|paramHash</c>) so a message
/// enqueued from BFF and a message that L2 might enqueue with identical
/// inputs collapse at the same Service Bus dedup window. The paramHash is
/// SHA256 hex of the raw <c>parametersJson</c> string (BYTE-for-BYTE
/// consistency required — callers MUST NOT reformat the JSON between
/// idempotency-key computation and enqueue).
/// </para>
///
/// <para>
/// Message wire fields (mirrors L2 conventions for zero-friction receive):
/// <list type="bullet">
/// <item><c>MessageId</c> = deterministic hash → level-1 idempotency.</item>
/// <item><c>SessionId</c> = CustomerId → per-customer FIFO (§4D I5) when the queue is session-enabled.</item>
/// <item><c>Subject</c> = HandlerId → the L2 (or BFF fallback) dispatcher's routing key.</item>
/// <item><c>CorrelationId</c> = RunId → cross-service tracing correlation.</item>
/// <item><c>ContentType</c> = <c>application/json</c>.</item>
/// <item><c>ApplicationProperties</c> = { <c>HandlerId</c>, <c>JobType</c>, <c>RunId</c>, <c>EnqueuedAt</c> } for out-of-body routing.</item>
/// </list>
/// </para>
///
/// <para>
/// Logging: NEVER logs the parametersJson body (may carry tid + customerId
/// which are PII-adjacent). Logs only handlerId, runId, and MessageId.
/// </para>
/// </summary>
public sealed class ServiceBusProvisioningEnqueuer : IProvisioningEnqueuer, IAsyncDisposable
{
    private readonly ServiceBusSender _sender;
    private readonly ILogger<ServiceBusProvisioningEnqueuer> _logger;

    /// <summary>Constructs the enqueuer bound to the L2 provisioning queue.</summary>
    public ServiceBusProvisioningEnqueuer(
        ServiceBusClient serviceBusClient,
        IOptions<OnboardingOptions> options,
        ILogger<ServiceBusProvisioningEnqueuer> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceBusClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var queueName = string.IsNullOrWhiteSpace(options.Value.QueueName)
            ? "sprk-provisioning-jobs"
            : options.Value.QueueName;
        _sender = serviceBusClient.CreateSender(queueName);
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task EnqueueAsync(
        string handlerId,
        string runId,
        string customerId,
        string parametersJson,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handlerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parametersJson);

        var messageId = ComputeMessageId(handlerId, runId, customerId, parametersJson);
        var enqueuedAt = DateTimeOffset.UtcNow;

        // Wire-body wraps the same shape L2's HandlerEnvelope uses so the L2
        // receive-side (Wave C5) deserializes without a translation layer.
        var body = JsonSerializer.Serialize(new
        {
            handlerId,
            runId,
            customerId,
            parametersJson,
            enqueuedAt,
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        });

        var message = new ServiceBusMessage(body)
        {
            MessageId = messageId,
            SessionId = customerId,
            Subject = handlerId,
            ContentType = "application/json",
            CorrelationId = runId,
        };
        message.ApplicationProperties["JobType"] = handlerId;
        message.ApplicationProperties["HandlerId"] = handlerId;
        message.ApplicationProperties["RunId"] = runId;
        message.ApplicationProperties["EnqueuedAt"] = enqueuedAt.ToString("o");

        try
        {
            await _sender.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Enqueued provisioning dispatch: HandlerId={HandlerId} RunId={RunId} " +
                "CustomerIdHash={CustomerIdHash} MessageId={MessageId}",
                handlerId, runId, HashForLog(customerId), messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to enqueue provisioning dispatch: HandlerId={HandlerId} RunId={RunId} " +
                "CustomerIdHash={CustomerIdHash} MessageId={MessageId}",
                handlerId, runId, HashForLog(customerId), messageId);
            throw;
        }
    }

    /// <summary>
    /// Computes the deterministic Service Bus MessageId. MUST mirror L2's
    /// <c>ServiceBusHandlerEnqueuer.ComputeMessageId</c> byte-for-byte so
    /// cross-service dedup binds. Exposed internal for unit tests to assert
    /// format stability.
    /// </summary>
    internal static string ComputeMessageId(
        string handlerId, string runId, string customerId, string parametersJson)
    {
        var paramHash = Sha256Hex(parametersJson);
        var composite = $"{handlerId}|{runId}|{customerId}|{paramHash}";
        return Sha256Hex(composite);
    }

    private static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes); // 64-char upper-hex; fits SB 128-char cap.
    }

    private static string HashForLog(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).AsSpan(0, 8).ToString();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync().ConfigureAwait(false);
    }
}
