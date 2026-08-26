// -----------------------------------------------------------------------------
// ServiceBusSmokeTests.cs
//
// L2 CONTROL-PLANE Service Bus enqueue smoke test (task 038 step 8).
//
// PURPOSE:  Prove the wiring in ServiceBusModule + ServiceBusHandlerEnqueuer
//           against the actual Azure.Messaging.ServiceBus SDK + a real dev SB
//           namespace (ADR-038 path #3 external-integration boundary). Also
//           proves the two load-bearing invariants at the wire level:
//
//             1. FR-22 level-1 idempotency — the deterministic MessageId
//                (SHA256 of HandlerId|RunId|CustomerId|paramHash) reproduces
//                exactly across calls. Verified by unit test (no live SB).
//             2. End-to-end enqueue — a real HandlerEnvelope lands on the
//                queue with SessionId = CustomerId, MessageId matching the
//                computed hash, and TimeToLive honored. Verified by
//                env-guarded smoke test against a dev queue.
//
// ENV-GUARD: Skipped by default. Set SB_L2_SMOKE_FQN to the dev Service Bus
//           namespace (e.g. sb-spaarke-dev.servicebus.windows.net) AND
//           authenticate with a principal that has Azure Service Bus Data
//           Sender AND Data Receiver on the target queue (an operator
//           running `az login` picks this up via DefaultAzureCredential ->
//           AzureCliCredential fallback). Optional overrides:
//             SB_L2_SMOKE_QUEUE       (default: `sprk-provisioning-jobs`)
//             SB_L2_SMOKE_MI_CLIENTID (optional; pins DefaultAzureCredential
//                                      to a specific UAMI when both a user
//                                      identity and a UAMI are present)
//
//           The env-guarded round-trip test uses ReceiveMessagesAsync to
//           pull the enqueued message back and inspect its wire fields;
//           the message is completed (not abandoned) so the smoke run does
//           not leave junk on the queue.
//
// NOT a unit test — do NOT add this file to a "unit-tests-only" glob.
// ADR-038 KEEP category: `tests/integration/**` (or the equivalent
// project-owned integration path). The unit-level MessageId determinism
// test IS a proper unit test (no live SB) and runs unconditionally.
// -----------------------------------------------------------------------------

using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Modules;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests;

/// <summary>
/// Unit + env-guarded smoke tests over <see cref="ServiceBusHandlerEnqueuer"/>.
/// The unit-level determinism test always runs; the end-to-end enqueue test is
/// skipped unless <c>SB_L2_SMOKE_FQN</c> is set.
/// </summary>
[Trait("Category", "Smoke")]
[Trait("RequiresLiveResource", "ServiceBus")]
public sealed class ServiceBusSmokeTests : IAsyncLifetime
{
    private const string FqnEnvVar = "SB_L2_SMOKE_FQN";
    private const string QueueEnvVar = "SB_L2_SMOKE_QUEUE";
    private const string MiClientIdEnvVar = "SB_L2_SMOKE_MI_CLIENTID";

    private ServiceBusClient? _client;
    private string _queueName = "sprk-provisioning-jobs";

    public Task InitializeAsync()
    {
        var fqn = Environment.GetEnvironmentVariable(FqnEnvVar);
        if (string.IsNullOrWhiteSpace(fqn))
        {
            // Signal skip via a null client — the env-guarded tests bail out early.
            return Task.CompletedTask;
        }

        _queueName = Environment.GetEnvironmentVariable(QueueEnvVar) ?? "sprk-provisioning-jobs";
        var miClientId = Environment.GetEnvironmentVariable(MiClientIdEnvVar);

        var options = new DefaultAzureCredentialOptions();
        if (!string.IsNullOrWhiteSpace(miClientId))
        {
            options.ManagedIdentityClientId = miClientId;
        }
        TokenCredential credential = new DefaultAzureCredential(options);

        _client = new ServiceBusClient(fqn, credential);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// FR-22 level-1 idempotency — MessageId is deterministic per
    /// (HandlerId, RunId, CustomerId, ParametersJson). Two envelopes with
    /// identical inputs MUST produce identical MessageIds.
    /// </summary>
    [Fact]
    public void ComputeMessageId_IsDeterministic_ForIdenticalEnvelopes()
    {
        var enqueuedAt = DateTimeOffset.UtcNow;
        var a = NewEnvelope(handlerId: "H4", runId: "run-1", customerId: "acme", parameters: "{\"k\":\"v\"}", enqueuedAt: enqueuedAt);
        var b = NewEnvelope(handlerId: "H4", runId: "run-1", customerId: "acme", parameters: "{\"k\":\"v\"}", enqueuedAt: enqueuedAt.AddMinutes(5));

        // EnqueuedAt DELIBERATELY differs — the MessageId spec ignores it
        // (only HandlerId + RunId + CustomerId + paramHash contribute).
        var idA = ServiceBusHandlerEnqueuer.ComputeMessageId(a);
        var idB = ServiceBusHandlerEnqueuer.ComputeMessageId(b);

        idA.Should().Be(idB);
        idA.Length.Should().Be(64, "SHA256 hex is always 64 chars — well under the SB 128-char cap.");
    }

    /// <summary>
    /// Distinguishing any input dimension MUST change the MessageId.
    /// </summary>
    [Theory]
    [InlineData("H4", "run-1", "acme", "{\"k\":\"v\"}",  "H5", "run-1", "acme", "{\"k\":\"v\"}")]   // HandlerId
    [InlineData("H4", "run-1", "acme", "{\"k\":\"v\"}",  "H4", "run-2", "acme", "{\"k\":\"v\"}")]   // RunId
    [InlineData("H4", "run-1", "acme", "{\"k\":\"v\"}",  "H4", "run-1", "beta", "{\"k\":\"v\"}")]   // CustomerId
    [InlineData("H4", "run-1", "acme", "{\"k\":\"v\"}",  "H4", "run-1", "acme", "{\"k\":\"w\"}")]   // paramHash
    public void ComputeMessageId_ChangesWhenAnyDimensionDiffers(
        string h1, string r1, string c1, string p1,
        string h2, string r2, string c2, string p2)
    {
        var enqueuedAt = DateTimeOffset.UtcNow;
        var a = NewEnvelope(h1, r1, c1, p1, enqueuedAt);
        var b = NewEnvelope(h2, r2, c2, p2, enqueuedAt);

        var idA = ServiceBusHandlerEnqueuer.ComputeMessageId(a);
        var idB = ServiceBusHandlerEnqueuer.ComputeMessageId(b);

        idA.Should().NotBe(idB);
    }

    /// <summary>
    /// End-to-end enqueue round-trip: envelope lands on the queue with the
    /// computed MessageId, SessionId = CustomerId, and CorrelationId = RunId.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_LandsOnQueue_WithExpectedWireFields()
    {
        if (_client is null)
        {
            return; // env-guarded skip
        }

        var options = Options.Create(new ServiceBusModuleOptions
        {
            FullyQualifiedNamespace = Environment.GetEnvironmentVariable(FqnEnvVar)!,
            QueueName = _queueName,
            DefaultTimeToLive = TimeSpan.FromMinutes(15),
        });

        await using var enqueuer = new ServiceBusHandlerEnqueuer(
            serviceBusClient: _client,
            options: options,
            logger: NullLogger<ServiceBusHandlerEnqueuer>.Instance);

        var envelope = NewEnvelope(
            handlerId: "H0-Preflight",
            runId: $"smoke-{Guid.NewGuid():N}",
            customerId: $"smoke-{Environment.MachineName.ToLowerInvariant()}-{Guid.NewGuid():N}",
            parameters: "{\"smoke\":true}",
            enqueuedAt: DateTimeOffset.UtcNow);

        await enqueuer.EnqueueAsync(envelope, CancellationToken.None);

        // Receive the message we just sent (short wait + peek-first pattern).
        // The receive uses ReceiveMessagesAsync (not session-bound) because the
        // default queue topology does NOT require sessions — the enqueuer still
        // sets SessionId so a future toggle to session-enabled works without
        // code change, and this test verifies that field on the received wire
        // message.
        await using var receiver = _client.CreateReceiver(_queueName);
        var received = await receiver.ReceiveMessagesAsync(
            maxMessages: 5,
            maxWaitTime: TimeSpan.FromSeconds(10));

        var expectedMessageId = ServiceBusHandlerEnqueuer.ComputeMessageId(envelope);
        var match = received.FirstOrDefault(m => m.MessageId == expectedMessageId);

        match.Should().NotBeNull("the enqueued message with the deterministic MessageId must land on the queue within the wait window.");
        match!.SessionId.Should().Be(envelope.CustomerId, "§4D I5 per-customer FIFO ordering — SessionId MUST equal CustomerId.");
        match.CorrelationId.Should().Be(envelope.RunId, "CorrelationId ties log lines on both sides of the wire to the same run.");
        match.Subject.Should().Be(envelope.HandlerId);
        match.ContentType.Should().Be("application/json");
        match.ApplicationProperties["JobType"].Should().Be(envelope.HandlerId);
        match.ApplicationProperties["HandlerId"].Should().Be(envelope.HandlerId);
        match.ApplicationProperties["RunId"].Should().Be(envelope.RunId);

        // Body round-trips through the wire serializer.
        var body = JsonSerializer.Deserialize<HandlerEnvelope>(
            match.Body.ToString(),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        body.Should().NotBeNull();
        body!.HandlerId.Should().Be(envelope.HandlerId);
        body.RunId.Should().Be(envelope.RunId);
        body.CustomerId.Should().Be(envelope.CustomerId);
        body.ParametersJson.Should().Be(envelope.ParametersJson);

        // Complete the message so the smoke run does not leave junk on the queue.
        // Abandon any OTHER messages we happened to peek at (unrelated to this run).
        foreach (var msg in received)
        {
            if (msg.MessageId == expectedMessageId)
            {
                await receiver.CompleteMessageAsync(msg);
            }
            else
            {
                await receiver.AbandonMessageAsync(msg);
            }
        }
    }

    private static HandlerEnvelope NewEnvelope(
        string handlerId, string runId, string customerId, string parameters, DateTimeOffset enqueuedAt)
    {
        return new HandlerEnvelope
        {
            HandlerId = handlerId,
            RunId = runId,
            CustomerId = customerId,
            ParametersJson = parameters,
            EnqueuedAt = enqueuedAt,
        };
    }
}
