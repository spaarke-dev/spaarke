// R3 Part 1 Phase 2 — Service Bus subscription consumer host (task 084).
//
// `BackgroundService` that subscribes to the `recon-junction-updater`
// subscription on the `sprk-membership-changes` topic and dispatches
// each `MembershipChangedEvent` to a Scoped `IMembershipJunctionUpdater`.
// Mirrors the design of `ServiceBusJobProcessor` (existing exemplar) but
// targets a subscription (topic+subscription) rather than a queue.
//
// Lifecycle invariants:
//
//   1. Connects by namespace via `ServiceBusClientFactory.CreateForNamespace`
//      with the DI-injected, ClientId-pinned TokenCredential — ADR-028
//      canonical outbound auth. (Corrected 2026-08-23, auth-v4 task 051:
//      this previously built `new DefaultAzureCredential()` inline, which
//      cannot disambiguate the five UAMIs in the dev subscription.) The
//      JobProcessingModule `ServiceBusClient` singleton is a distinct
//      client; no collision because we construct our own (Phase 2 is
//      additive).
//
//   2. Uses `ServiceBusProcessor` (high-throughput callback model) — NOT
//      `ServiceBusReceiver`. The processor's `ProcessMessageAsync` event
//      runs on the SDK's internal pump; we wire it to our message
//      handler. `MaxConcurrentCalls` caps in-flight processing.
//
//   3. Idempotency is handled inside `IMembershipJunctionUpdater` (per
//      spec FR-2P2.4) — the host does NOT attempt to dedupe; duplicate
//      delivery is expected under Service Bus at-least-once semantics
//      and is contract-safe.
//
//   4. Per-message error policy: success → `CompleteMessageAsync`
//      (acks the message off the subscription). Failure →
//      `AbandonMessageAsync` (lets the broker redeliver; Service Bus
//      max-delivery-count = 10 from task 071 Bicep handles dead-lettering).
//      Cancellation propagates as `OperationCanceledException` — the SDK
//      treats this as a transient and abandons the lock implicitly when
//      the processor is stopped.
//
//   5. **NFR-07 30-second drain on cancellation**: `StopAsync` calls
//      `_processor.StopProcessingAsync(linkedCts)` where the linked CTS
//      enforces a 30-second cap. Per Azure.Messaging.ServiceBus docs,
//      `StopProcessingAsync` returns only after all currently-running
//      callbacks have completed or the linked token fires. The
//      `StopAsync_DrainsWithin30s` unit test exercises this.
//
//   6. **ADR-032 Null-Object Kill-Switch** at the registration layer —
//      this real host is only registered when
//      `Membership:JunctionUpdater:Enabled=true`. Until task 071's topic
//      Bicep is deployed AND the operator flips the flag, the
//      `NullMembershipJunctionUpdaterHost` peer (sibling file) takes its
//      place and performs zero Service Bus work.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-2P2.4 +
//            NFR-07; .claude/adr/ADR-032-bff-nullobject-kill-switch.md;
//            src/server/api/Sprk.Bff.Api/Services/Jobs/ServiceBusJobProcessor.cs
//            (exemplar — queue-based pattern). Cross-task: handler is
//            reused by task 085 (MembershipReconciliationJob) directly
//            via the IMembershipJunctionUpdater contract.

using System.Text.Json;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Services.Ai.Membership.Events;

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Real Service Bus subscription consumer for the membership-change
/// event stream. Drains within 30 seconds on cancellation per spec
/// NFR-07.
/// </summary>
public sealed class MembershipJunctionUpdaterHost : BackgroundService
{
    /// <summary>
    /// Maximum drain duration honored on <see cref="StopAsync"/> per
    /// spec NFR-07 (cancellation propagates within 30 seconds).
    /// </summary>
    public static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MembershipJunctionUpdaterOptions _options;
    private readonly ILogger<MembershipJunctionUpdaterHost> _logger;
    private readonly Func<MembershipJunctionUpdaterOptions, ServiceBusClient> _clientFactory;

    private ServiceBusClient? _client;
    private ServiceBusProcessor? _processor;

    /// <param name="credential">
    /// The DI-injected managed-identity credential (Program.cs:46). Nullable with a null default so
    /// existing fixtures keep compiling, per the shape CLAUDE.md prescribes; in production DI always
    /// supplies it, and the default client factory below fails loudly if it is missing.
    /// </param>
    public MembershipJunctionUpdaterHost(
        IServiceScopeFactory scopeFactory,
        IOptions<MembershipJunctionUpdaterOptions> options,
        ILogger<MembershipJunctionUpdaterHost> logger,
        Azure.Core.TokenCredential? credential = null)
        : this(scopeFactory, options, logger, clientFactory: null, credential: credential)
    {
    }

    /// <summary>
    /// Test seam — allows unit tests to inject a fake
    /// <see cref="ServiceBusClient"/> factory without depending on a
    /// real Azure namespace. Production code uses the public constructor,
    /// which builds a real client through
    /// <see cref="Infrastructure.Auth.ServiceBusClientFactory"/> using the
    /// DI-injected credential.
    /// </summary>
    internal MembershipJunctionUpdaterHost(
        IServiceScopeFactory scopeFactory,
        IOptions<MembershipJunctionUpdaterOptions> options,
        ILogger<MembershipJunctionUpdaterHost> logger,
        Func<MembershipJunctionUpdaterOptions, ServiceBusClient>? clientFactory,
        Azure.Core.TokenCredential? credential = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // auth-v4 task 051 (FR-E2): this used to build `new DefaultAzureCredential()` inline. That
        // is the deviation task 051's constraints name explicitly, and it is not cosmetic — five
        // user-assigned identities exist in the dev subscription and one is named like the BFF's
        // without being attached to it, so an unpinned DefaultAzureCredential can silently
        // authenticate as the wrong principal. Construction now goes through
        // ServiceBusClientFactory with the ClientId-pinned singleton from DI.
        _clientFactory = clientFactory ?? (opts =>
        {
            if (string.IsNullOrWhiteSpace(opts.ServiceBusNamespace))
            {
                throw new InvalidOperationException(
                    "Membership:JunctionUpdater:ServiceBusNamespace is required when Enabled=true. " +
                    "Set it to the FQDN of the Service Bus namespace (e.g., spaarkesb-dev.servicebus.windows.net).");
            }

            if (credential is null)
            {
                throw new InvalidOperationException(
                    "MembershipJunctionUpdaterHost requires the DI-injected TokenCredential to connect " +
                    "to Service Bus by namespace. It is registered as a singleton in Program.cs; if this " +
                    "throws, the host was constructed outside DI without supplying either a credential " +
                    "or a test client factory.");
            }

            return Infrastructure.Auth.ServiceBusClientFactory.CreateForNamespace(
                opts.ServiceBusNamespace, credential);
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "MembershipJunctionUpdaterHost starting (topic={TopicName}, subscription={SubscriptionName}, namespace={Namespace}, maxConcurrentCalls={MaxConcurrent})",
            _options.TopicName,
            _options.SubscriptionName,
            _options.ServiceBusNamespace,
            _options.MaxConcurrentCalls);

        try
        {
            _client = _clientFactory(_options);
            _processor = _client.CreateProcessor(
                _options.TopicName,
                _options.SubscriptionName,
                new ServiceBusProcessorOptions
                {
                    MaxConcurrentCalls = _options.MaxConcurrentCalls,
                    AutoCompleteMessages = false,
                    MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(10),
                    ReceiveMode = ServiceBusReceiveMode.PeekLock,
                });

            _processor.ProcessMessageAsync += OnMessageAsync;
            _processor.ProcessErrorAsync += OnErrorAsync;

            await _processor.StartProcessingAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation("MembershipJunctionUpdaterHost started successfully");

            // Block until cancellation. The SDK pump runs the message
            // handlers on its own threads.
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("MembershipJunctionUpdaterHost stopping (cancellation requested)");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex,
                "MembershipJunctionUpdaterHost failed to start: {Error}", ex.Message);
            throw;
        }
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        MembershipChangedEvent? evt;
        try
        {
            var body = args.Message.Body.ToString();
            evt = JsonSerializer.Deserialize<MembershipChangedEvent>(
                body, MembershipChangedEvent.SerializerOptions);

            if (evt is null)
            {
                _logger.LogError(
                    "Failed to deserialize MembershipChangedEvent from message {MessageId} (body was null)",
                    args.Message.MessageId);

                await args.DeadLetterMessageAsync(
                    args.Message,
                    "InvalidFormat",
                    "Deserialized MembershipChangedEvent was null",
                    args.CancellationToken).ConfigureAwait(false);
                return;
            }
        }
        catch (JsonException jx)
        {
            _logger.LogError(jx,
                "JSON deserialization failed for message {MessageId}; dead-lettering",
                args.Message.MessageId);

            await args.DeadLetterMessageAsync(
                args.Message,
                "InvalidFormat",
                jx.Message,
                args.CancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            // Resolve the Scoped handler per message via a fresh scope —
            // mirrors ServiceBusJobProcessor's Singleton-with-Scoped
            // pattern. IDataverseService is Scoped; opening a scope per
            // message provides proper lifetime semantics.
            await using var scope = _scopeFactory.CreateAsyncScope();
            var updater = scope.ServiceProvider
                .GetRequiredService<IMembershipJunctionUpdater>();

            await updater.HandleAsync(evt, args.CancellationToken).ConfigureAwait(false);

            await args.CompleteMessageAsync(args.Message, args.CancellationToken)
                .ConfigureAwait(false);

            _logger.LogDebug(
                "Processed MembershipChangedEvent (mutationType={MutationType}, correlationId={CorrelationId}, deliveryCount={DeliveryCount})",
                evt.MutationType, evt.CorrelationId, args.Message.DeliveryCount);
        }
        catch (OperationCanceledException)
        {
            // Drain semantics — let the lock expire naturally; the
            // broker will redeliver.
            _logger.LogInformation(
                "MembershipChangedEvent processing canceled (correlationId={CorrelationId}, deliveryCount={DeliveryCount})",
                evt.CorrelationId, args.Message.DeliveryCount);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "MembershipChangedEvent processing failed (correlationId={CorrelationId}, deliveryCount={DeliveryCount}); abandoning for retry",
                evt.CorrelationId, args.Message.DeliveryCount);

            // Abandon → broker redelivers. Max delivery count (10 per
            // task 071 Bicep) gates dead-letter.
            try
            {
                await args.AbandonMessageAsync(args.Message, propertiesToModify: null, args.CancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception abandonEx)
            {
                _logger.LogWarning(abandonEx,
                    "Failed to abandon message {MessageId} after processing failure",
                    args.Message.MessageId);
            }
        }
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "MembershipJunctionUpdaterHost processor error in {EntityPath} (source={ErrorSource}): {Error}",
            args.EntityPath, args.ErrorSource, args.Exception.Message);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping MembershipJunctionUpdaterHost — 30s drain");

        // NFR-07: cap drain at 30 seconds. If the host's caller passes
        // a token that fires sooner, honor that too — linked CTS.
        using var drainCts = new CancellationTokenSource(DrainTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, drainCts.Token);

        if (_processor is not null)
        {
            try
            {
                await _processor.StopProcessingAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning(
                    "MembershipJunctionUpdaterHost drain exceeded {DrainSeconds}s — forced stop",
                    DrainTimeout.TotalSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping membership processor: {Error}", ex.Message);
            }

            try
            {
                await _processor.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Membership processor dispose threw (likely already disposed)");
            }
            _processor = null;
        }

        if (_client is not null)
        {
            try
            {
                await _client.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Membership ServiceBusClient dispose threw");
            }
            _client = null;
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("MembershipJunctionUpdaterHost stopped");
    }
}
