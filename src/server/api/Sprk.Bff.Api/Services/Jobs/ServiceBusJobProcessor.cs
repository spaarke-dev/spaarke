using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Jobs;

/// <summary>
/// Generic Service Bus processor that handles jobs using the ADR-004 job contract.
/// Routes jobs to appropriate IJobHandler implementations based on JobType.
/// Implements idempotency, retry logic, and dead-letter queue handling.
/// </summary>
public class ServiceBusJobProcessor : BackgroundService, IHealthCheck
{
    /// <summary>
    /// Consecutive authorization failures tolerated before <c>/healthz</c> reports Unhealthy.
    /// </summary>
    /// <remarks>
    /// Not 1: a single failure can be MI token-propagation lag or a transient 401 on link
    /// refresh, and flapping the health endpoint on those would be worse than the disease
    /// (FAILURE-MODES AP-7 — converting a silent fallback into fail-fast has unbounded blast
    /// radius). Three consecutive failures with no message processed in between is not transient.
    /// </remarks>
    internal const int AuthFailureUnhealthyThreshold = 3;

    /// <summary>
    /// The logical delivery count at which a message whose processing threw (not a handler <c>Failure</c>) is dead-lettered as
    /// <c>ProcessingError</c>.
    /// </summary>
    internal const int MaxUnexpectedErrorDeliveries = 3;

    private readonly ServiceBusClient _serviceBusClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ServiceBusJobProcessor> _logger;
    private readonly TimeProvider _time;
    private readonly string _queueName;
    private readonly int _maxConcurrentCalls;
    private ServiceBusProcessor? _processor;

    // ISS-025: the sender of scheduled retry copies to the processor's own queue, created on the first retry (lock-guarded).
    private readonly object _retrySenderGate = new();
    private ServiceBusSender? _retrySender;

    // Auth-failure state, read by CheckHealthAsync on the health-check thread.
    private int _consecutiveAuthFailures;
    private volatile string? _lastAuthFailureDescription;

    public ServiceBusJobProcessor(
        ServiceBusClient serviceBusClient,
        IServiceProvider serviceProvider,
        ILogger<ServiceBusJobProcessor> logger,
        IOptions<ServiceBusOptions> serviceBusOptions,
        TimeProvider? timeProvider = null)
    {
        _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;

        var options = serviceBusOptions?.Value ?? throw new ArgumentNullException(nameof(serviceBusOptions));
        _queueName = options.QueueName;
        _maxConcurrentCalls = options.MaxConcurrentCalls;

        // Log the queue name at construction to help diagnose configuration issues
        _logger.LogInformation(
            "ServiceBusJobProcessor configured with queue '{QueueName}' and {MaxConcurrentCalls} concurrent calls",
            _queueName, _maxConcurrentCalls);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Service Bus Job Processor starting for queue {QueueName} with {MaxConcurrentCalls} concurrent calls...",
            _queueName, _maxConcurrentCalls);

        try
        {
            _processor = _serviceBusClient.CreateProcessor(_queueName, new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = _maxConcurrentCalls,
                AutoCompleteMessages = false,
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(10),
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });

            _processor.ProcessMessageAsync += ProcessMessageAsync;
            _processor.ProcessErrorAsync += ProcessErrorAsync;

            await _processor.StartProcessingAsync(stoppingToken);
            _logger.LogInformation("Service Bus Job Processor started successfully");

            // Keep service running
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Service Bus Job Processor stopping...");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Service Bus Job Processor failed to start: {Error}", ex.Message);
            throw;
        }
        finally
        {
            if (_processor != null)
            {
                try
                {
                    await _processor.StopProcessingAsync();
                }
                catch (ObjectDisposedException)
                {
                    // Processor already disposed - this is fine during shutdown
                    _logger.LogDebug("Processor was already disposed during shutdown");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping processor during shutdown");
                }

                try
                {
                    await _processor.DisposeAsync();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed - this is fine
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing processor during shutdown");
                }
            }

            // After the processor has stopped, so no in-flight message is still sending a retry copy.
            await DisposeRetrySenderAsync();
            _logger.LogInformation("Service Bus Job Processor stopped");
        }
    }

    /// <remarks>
    /// <para><b>Retry with back-off (ISS-025 #2876, DEF-055 #2600).</b> A retryable failure is not abandoned (the broker would
    /// redeliver it at once, spending every delivery within seconds, so an outage of a minute dead-lettered the job). The processor
    /// sends a COPY of the message to its own queue, scheduled <see cref="JobContract.RetryDelay"/> later and carrying the logical
    /// delivery count in <see cref="JobContract.PriorDeliveryCountProperty"/>, then completes the original
    /// (<see cref="RetryLaterAsync"/>). <see cref="JobContract.DeliveryCount"/> is that logical count, so
    /// <see cref="JobContract.IsFinalDelivery"/> and the dead-letter rules below count every delivery of the job, not of the copy.</para>
    /// </remarks>
    internal async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        // Receiving a message at all proves the credential and its RBAC are working.
        Interlocked.Exchange(ref _consecutiveAuthFailures, 0);
        _lastAuthFailureDescription = null;

        using var scope = _serviceProvider.CreateScope();
        var startTime = DateTime.UtcNow;

        // The logical delivery: what earlier copies of this job spent, plus this message's own broker deliveries.
        var deliveryCount = ReadPriorDeliveryCount(args.Message) + args.Message.DeliveryCount;

        try
        {
            // Deserialize job contract
            var messageBody = args.Message.Body.ToString();
            var job = JsonSerializer.Deserialize<JobContract>(messageBody, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (job == null)
            {
                _logger.LogError("Failed to deserialize job from message {MessageId}", args.Message.MessageId);
                await args.DeadLetterMessageAsync(args.Message,
                    "InvalidFormat",
                    "Failed to deserialize job contract from message body",
                    args.CancellationToken);
                return;
            }

            // The logical delivery count reaches the handler, so a handler's "last chance" logic (the inbound email HOLD)
            // sees the same dead-letter condition this processor applies below (task 146 r2; logical since ISS-025).
            job.DeliveryCount = deliveryCount;

            _logger.LogInformation(
                "Processing job {JobId} of type {JobType}, attempt {Attempt}/{MaxAttempts}, delivery count {DeliveryCount} (broker delivery {BrokerDeliveryCount})",
                job.JobId, job.JobType, job.Attempt, job.MaxAttempts, deliveryCount, args.Message.DeliveryCount);

            // Find appropriate handler with detailed diagnostics
            List<IJobHandler> handlers;
            try
            {
                handlers = scope.ServiceProvider.GetServices<IJobHandler>().ToList();
                _logger.LogInformation(
                    "📋 Found {Count} job handlers: [{HandlerTypes}]",
                    handlers.Count,
                    string.Join(", ", handlers.Select(h => h.JobType)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "❌ Failed to enumerate IJobHandler implementations. This typically means a handler's dependency failed to resolve. Error: {Error}",
                    ex.Message);

                // Log the inner exception chain for more detail
                var innerEx = ex.InnerException;
                while (innerEx != null)
                {
                    _logger.LogError("  → Inner exception: {InnerError}", innerEx.Message);
                    innerEx = innerEx.InnerException;
                }

                await args.DeadLetterMessageAsync(args.Message,
                    "HandlerResolutionFailed",
                    $"Failed to resolve job handlers: {ex.Message}",
                    args.CancellationToken);
                return;
            }

            var handler = handlers.FirstOrDefault(h => h.JobType == job.JobType);

            if (handler == null)
            {
                _logger.LogError(
                    "No handler found for job type {JobType}. Available handlers: {AvailableHandlers}",
                    job.JobType,
                    string.Join(", ", handlers.Select(h => h.JobType)));

                await args.DeadLetterMessageAsync(args.Message,
                    "NoHandler",
                    $"No handler registered for job type '{job.JobType}'",
                    args.CancellationToken);
                return;
            }

            // Process job
            var outcome = await handler.ProcessAsync(job, args.CancellationToken);

            // Handle outcome
            if (outcome.Status == JobStatus.Completed)
            {
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                _logger.LogInformation(
                    "Job {JobId} completed successfully in {Duration}ms",
                    job.JobId, outcome.Duration.TotalMilliseconds);
            }
            else if (outcome.Status == JobStatus.Poisoned || job.IsFinalDelivery)
            {
                // Dead-letter: poisoned, max attempts, or delivery count exceeded
                await args.DeadLetterMessageAsync(args.Message,
                    outcome.Status == JobStatus.Poisoned ? "Poisoned" : "MaxRetriesExceeded",
                    outcome.ErrorMessage ?? "Job failed after maximum attempts",
                    args.CancellationToken);

                _logger.LogError(
                    "Job {JobId} dead-lettered after {Attempts} attempts (delivery count: {DeliveryCount}): {Error}",
                    job.JobId, job.Attempt, deliveryCount, outcome.ErrorMessage);
            }
            else
            {
                // Retry later: a scheduled copy, then complete this message (ISS-025).
                _logger.LogWarning(
                    "Job {JobId} failed on attempt {Attempt}, will retry (delivery count: {DeliveryCount}): {Error}",
                    job.JobId, job.Attempt, deliveryCount, outcome.ErrorMessage);
                await RetryLaterAsync(args, deliveryCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing message {MessageId}: {Error}",
                args.Message.MessageId, ex.Message);

            // Dead-letter after 3 logical deliveries
            if (deliveryCount >= MaxUnexpectedErrorDeliveries)
            {
                await args.DeadLetterMessageAsync(args.Message,
                    "ProcessingError",
                    $"Unexpected error: {ex.Message}",
                    args.CancellationToken);

                _logger.LogError("Message {MessageId} dead-lettered after {DeliveryCount} delivery attempts",
                    args.Message.MessageId, deliveryCount);
            }
            else
            {
                // Retry later, as for a handler Failure (ISS-025)
                _logger.LogWarning("Message {MessageId} failed, will retry (delivery count: {DeliveryCount})",
                    args.Message.MessageId, deliveryCount);
                await RetryLaterAsync(args, deliveryCount);
            }
        }
    }

    /// <summary>
    /// ISS-025 / DEF-055: retries <paramref name="args"/>' message after <see cref="JobContract.RetryDelay"/> for
    /// <paramref name="deliveryCount"/>. In order: (1) send a copy of the message — the SAME body, MessageId, CorrelationId,
    /// SessionId, Subject, ContentType, time-to-live and application properties, via the SDK's received-message copy — scheduled for
    /// then, with <see cref="JobContract.PriorDeliveryCountProperty"/> = <paramref name="deliveryCount"/>; (2) complete the original.
    /// </summary>
    /// <remarks>
    /// <para><b>The send fails</b> → the original is abandoned, as before ISS-025: the broker redelivers it at once and counts the
    /// delivery, so the job loses its back-off, never a delivery or the job itself.</para>
    /// <para><b>The complete fails after the send</b> → logged, not thrown: the copy is already queued, and the original is
    /// redelivered when its lock expires, so the job may run twice. That is safe because every <see cref="IJobHandler"/> is
    /// idempotent under at-least-once delivery (ADR-004): it takes an atomic per-job claim and writes a completion marker, keyed
    /// on <see cref="JobContract.IdempotencyKey"/>, which the copy carries unchanged in its body.</para>
    /// </remarks>
    private async Task RetryLaterAsync(ProcessMessageEventArgs args, int deliveryCount)
    {
        var delay = JobContract.RetryDelay(deliveryCount);
        var dueAt = _time.GetUtcNow() + delay;

        try
        {
            var copy = new ServiceBusMessage(args.Message)
            {
                ScheduledEnqueueTime = dueAt
            };
            copy.ApplicationProperties[JobContract.PriorDeliveryCountProperty] = deliveryCount;

            await GetRetrySender().SendMessageAsync(copy, args.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not schedule the retry of message {MessageId} (delivery count: {DeliveryCount}); abandoning it for immediate redelivery instead",
                args.Message.MessageId, deliveryCount);
            await args.AbandonMessageAsync(args.Message, null, args.CancellationToken);
            return;
        }

        try
        {
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Retry of message {MessageId} is scheduled for {DueAt:o}, but the original could not be completed; it is redelivered " +
                "when its lock expires and the job may run twice (handlers are idempotent, ADR-004)",
                args.Message.MessageId, dueAt);
            return;
        }

        _logger.LogWarning(
            "Message {MessageId} retry scheduled for {DueAt:o} (in {RetryDelay}) after delivery {DeliveryCount} of {MaxDeliveryCount}",
            args.Message.MessageId, dueAt, delay, deliveryCount, JobContract.MaxDeliveryCount);
    }

    /// <summary>
    /// The deliveries earlier copies of this job spent (<see cref="JobContract.PriorDeliveryCountProperty"/>): 0 when the property is
    /// absent or not a whole number, and never negative. Bounded, so adding the broker's own count cannot overflow.
    /// </summary>
    internal static int ReadPriorDeliveryCount(ServiceBusReceivedMessage message)
    {
        if (!message.ApplicationProperties.TryGetValue(JobContract.PriorDeliveryCountProperty, out var raw) || raw is null)
        {
            return 0;
        }

        long value = raw switch
        {
            int i => i,
            long l => l,
            short s => s,
            string text when long.TryParse(text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0
        };

        return (int)Math.Clamp(value, 0, 10_000);
    }

    private ServiceBusSender GetRetrySender()
    {
        lock (_retrySenderGate)
        {
            return _retrySender ??= _serviceBusClient.CreateSender(_queueName);
        }
    }

    private async Task DisposeRetrySenderAsync()
    {
        ServiceBusSender? sender;
        lock (_retrySenderGate)
        {
            sender = _retrySender;
            _retrySender = null;
        }

        if (sender is null)
        {
            return;
        }

        try
        {
            await sender.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing the retry sender during shutdown");
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        // auth-v4 task 051 (FR-E2): an authorization failure here is NOT an ordinary processor
        // error. The processor keeps retrying forever, /healthz keeps returning 200, and the queue
        // silently stops draining — jobs are accepted and never run. That is not hypothetical: on
        // 2026-08-23 both spaarke-bff-dev slots looped on InvalidSignature for ~40 minutes while
        // reporting healthy, which is what made the outage hard to see. Classify it, say what to
        // fix, and let CheckHealthAsync surface it once it is clearly not transient.
        if (IsAuthorizationFailure(args.Exception))
        {
            var failures = Interlocked.Increment(ref _consecutiveAuthFailures);
            _lastAuthFailureDescription =
                $"{args.Exception.GetType().Name}: {args.Exception.Message} " +
                $"(entityPath={args.EntityPath}, source={args.ErrorSource}, consecutiveFailures={failures})";

            _logger.LogCritical(args.Exception,
                "Service Bus AUTHORIZATION failure on {EntityPath} (source: {ErrorSource}, consecutive: {Failures}). " +
                "Jobs are NOT being processed. On the managed-identity path this means the identity lacks " +
                "'Azure Service Bus Data Receiver' (receive) or 'Azure Service Bus Data Sender' (send) at " +
                "NAMESPACE scope — an entity-scoped grant does not cover the job queues, and Owner/Contributor " +
                "do not imply either. On the SAS path it means the connection string's key has been rotated or " +
                "revoked. Check EVERY deployment slot, not just production: slots hold their own app settings " +
                "and report the same cloud_RoleName. Error: {Error}",
                args.EntityPath, args.ErrorSource, failures, args.Exception.Message);

            return Task.CompletedTask;
        }

        _logger.LogError(args.Exception,
            "Service Bus processor error in {EntityPath} (source: {ErrorSource}): {Error}",
            args.EntityPath, args.ErrorSource, args.Exception.Message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// True when an exception from the Service Bus processor means "the credential was rejected"
    /// rather than an ordinary transient fault.
    /// </summary>
    /// <remarks>
    /// <para><b>There is deliberately no <see cref="ServiceBusFailureReason"/> check here, because
    /// the SDK has no authorization member to check.</b> Verified against
    /// <c>Azure.Messaging.ServiceBus 7.18.1</c> by reflecting the enum: its thirteen members are
    /// GeneralError, MessagingEntityNotFound, MessageLockLost, MessageNotFound, MessageSizeExceeded,
    /// MessagingEntityDisabled, QuotaExceeded, ServiceBusy, ServiceTimeout,
    /// ServiceCommunicationProblem, SessionCannotBeLocked, SessionLockLost and
    /// MessagingEntityAlreadyExists — none of them denotes "credential rejected".</para>
    /// <para>So authorization failures reach us in exactly two shapes:
    /// <see cref="UnauthorizedAccessException"/>, or a <see cref="ServiceBusException"/> carrying
    /// <c>GeneralError</c> whose message is the real signal. A rotated SAS key produces the latter
    /// with the text <c>InvalidSignature</c> — precisely the 2026-08-23 dev outage — so the string
    /// match is not a belt-and-braces extra, it is the only thing that catches that case.</para>
    /// <para>Matching on message text is normally a smell. It is accepted here because the
    /// alternative is no detection at all, and because the cost of a false positive is bounded: a
    /// mis-classified error logs at Critical and can degrade <c>/healthz</c>, but changes no
    /// message-handling behaviour and drops nothing.</para>
    /// </remarks>
    internal static bool IsAuthorizationFailure(Exception exception)
    {
        if (exception is UnauthorizedAccessException)
        {
            return true;
        }

        var message = exception.Message;
        return message.Contains("InvalidSignature", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
            || message.Contains("claim is empty or token is invalid", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reports Unhealthy once Service Bus authorization has failed
    /// <see cref="AuthFailureUnhealthyThreshold"/> times consecutively with no message processed
    /// in between — the state in which the app is running normally but draining no jobs.
    /// </summary>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var failures = Volatile.Read(ref _consecutiveAuthFailures);

        if (failures >= AuthFailureUnhealthyThreshold)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Service Bus authorization is failing for queue '{_queueName}': {failures} consecutive " +
                $"failures with no message processed. Background jobs are queued but NOT running. " +
                $"Last failure — {_lastAuthFailureDescription}"));
        }

        if (failures > 0)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                $"Service Bus authorization failed {failures} time(s) for queue '{_queueName}' without a " +
                $"successful receive since. Last failure — {_lastAuthFailureDescription}"));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            $"Service Bus job processing is authorized for queue '{_queueName}'."));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Service Bus Job Processor...");

        if (_processor != null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
