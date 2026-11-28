using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Intake.Engine;

/// <summary>
/// Dedicated Service Bus processor for the intake engine's completions (spaarke-intake-pipeline-r1 task 014; ADR-004 A2, ADR-052:
/// queue work is an ADR-004 consumer; the per-domain pattern of <c>CommunicationJobProcessor</c>). Reads
/// <c>intake-engine-completions</c> and routes to <see cref="IntakeEngineCompletionJobHandler"/> only.
/// </summary>
/// <remarks>
/// <para><b>Why its own queue and processor.</b> The engine container parses untrusted documents and runs as its OWN identity with
/// Sender on the completion queue only. A message does not carry its sender's identity, and Service Bus Data Sender is per queue, so
/// send rights on <c>sdap-jobs</c> would let a compromised engine enqueue any BFF job type. A closed switch here means whatever lands
/// on this queue can reach exactly one handler; any other JobType is dead-lettered <c>WrongQueue</c> and no handler is resolved.</para>
/// <para><b>Registration.</b> Only when <c>ServiceBus:IntakeEngineCompletionQueueName</c> is non-empty (ADR-032, conditional
/// registration): a stamp without an engine runs no consumer for a queue that does not exist there.</para>
/// <para>Retry, settlement and lock-loss rules are the shared <see cref="JobMessageRetry"/> (ISS-025), identical to the other two
/// ADR-004 consumers. ADR-015: nothing from a message body is logged or put in a dead-letter description beyond closed codes.</para>
/// <para>§11: <i>Existing</i> - <c>CommunicationJobProcessor</c> and <c>ServiceBusJobProcessor</c>. <i>Extension</i> - neither can
/// consume a second, differently-trusted queue without widening its routing (the isolation above is the point). <i>Cost of doing
/// nothing</i> - no completion would reach the run: every engine run would end at the hard timeout.</para>
/// </remarks>
public class IntakeEngineCompletionProcessor : BackgroundService
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IntakeEngineCompletionProcessor> _logger;
    private readonly TimeProvider _time;
    private readonly string _queueName;
    private readonly int _maxConcurrentCalls;
    private readonly JobMessageRetry.RetrySender _retrySender;
    private ServiceBusProcessor? _processor;

    public IntakeEngineCompletionProcessor(
        ServiceBusClient serviceBusClient,
        IServiceProvider serviceProvider,
        ILogger<IntakeEngineCompletionProcessor> logger,
        IOptions<ServiceBusOptions> serviceBusOptions,
        TimeProvider? timeProvider = null)
    {
        _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;

        var options = serviceBusOptions?.Value ?? throw new ArgumentNullException(nameof(serviceBusOptions));
        _queueName = options.IntakeEngineCompletionQueueName;
        if (string.IsNullOrWhiteSpace(_queueName))
        {
            throw new InvalidOperationException("ServiceBus:IntakeEngineCompletionQueueName is not set; this processor is registered only when it is.");
        }

        _retrySender = new JobMessageRetry.RetrySender(_serviceBusClient, _queueName);
        _maxConcurrentCalls = Math.Max(1, options.MaxConcurrentCalls / 2);

        _logger.LogInformation(
            "IntakeEngineCompletionProcessor configured with queue '{QueueName}' and {MaxConcurrentCalls} concurrent calls",
            _queueName, _maxConcurrentCalls);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Intake engine completion processor starting for queue {QueueName}...", _queueName);

        try
        {
            _processor = _serviceBusClient.CreateProcessor(_queueName, new ServiceBusProcessorOptions
            {
                MaxConcurrentCalls = _maxConcurrentCalls,
                AutoCompleteMessages = false,
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5),
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });

            _processor.ProcessMessageAsync += ProcessMessageAsync;
            _processor.ProcessErrorAsync += ProcessErrorAsync;

            await _processor.StartProcessingAsync(stoppingToken);
            _logger.LogInformation("Intake engine completion processor started successfully");

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Intake engine completion processor stopping...");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Intake engine completion processor failed to start");
            throw;
        }
        finally
        {
            if (_processor != null)
            {
                try { await _processor.StopProcessingAsync(); }
                catch (ObjectDisposedException) { }
                catch (Exception ex) { _logger.LogWarning(ex, "Error stopping the intake engine completion processor"); }

                try { await _processor.DisposeAsync(); }
                catch (ObjectDisposedException) { }
                catch (Exception ex) { _logger.LogWarning(ex, "Error disposing the intake engine completion processor"); }
            }

            // After the processor has stopped, so no in-flight message is still scheduling a retry copy.
            await _retrySender.DisposeAsync(_logger);
            _logger.LogInformation("Intake engine completion processor stopped");
        }
    }

    /// <remarks>
    /// The same rules as <c>CommunicationJobProcessor</c>, through <see cref="JobMessageRetry"/>: the logical delivery count reaches the
    /// handler, a retryable failure is retried by a copy scheduled on THIS queue, settlement failures are logged and never retried, a
    /// lost lock is not retried. The body is untrusted: a body that is not a Job Contract, and a JobType other than
    /// <see cref="IntakeEngineJobTypes.Completed"/>, are dead-lettered with a closed reason and nothing from the body.
    /// </remarks>
    internal async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        using var scope = _serviceProvider.CreateScope();
        var deliveryCount = JobMessageRetry.LogicalDeliveryCount(args.Message);

        try
        {
            JobContract? job;
            try
            {
                job = JsonSerializer.Deserialize<JobContract>(args.Message.Body.ToString(), new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            }
            catch (JsonException)
            {
                job = null;
            }

            if (job == null)
            {
                _logger.LogError("Message {MessageId} on the intake engine completion queue is not a job contract", args.Message.MessageId);
                await DeadLetterAsync(args, "InvalidFormat", "The message is not a job contract");
                return;
            }

            job.DeliveryCount = deliveryCount;

            // The engine writes attempt 1 of maxAttempts 1 on every completion (envelope.py): taken as sent, IsFinalDelivery would be
            // true on the FIRST delivery and a retryable outcome (a completion that is ahead of the orchestrator's record, a run a live
            // delivery holds) would be dead-lettered instead of retried, leaving the run to wait for the hard timeout. The delivery
            // count is the broker's and the retry copies', never the sender's.
            job.Attempt = 1;
            job.MaxAttempts = JobContract.MaxDeliveryCount;

            // Route by JobType, resolving ONLY the matching handler: the queue carries exactly one job type.
            if (job.JobType != IntakeEngineJobTypes.Completed)
            {
                _logger.LogError(
                    "Message {MessageId} on the intake engine completion queue is not a completion (expected {Expected}); dead-lettered",
                    args.Message.MessageId, IntakeEngineJobTypes.Completed);
                await DeadLetterAsync(args, "WrongQueue", "Only completions belong on the intake engine completion queue");
                return;
            }

            _logger.LogInformation(
                "Processing engine completion {JobId}, delivery count {DeliveryCount} (broker delivery {BrokerDeliveryCount})",
                job.JobId, deliveryCount, args.Message.DeliveryCount);

            var handler = scope.ServiceProvider.GetRequiredService<IntakeEngineCompletionJobHandler>();
            var outcome = await handler.ProcessAsync(job, args.CancellationToken);

            if (outcome.Status == JobStatus.Completed)
            {
                if (await JobMessageRetry.TrySettleAsync(
                        () => args.CompleteMessageAsync(args.Message, args.CancellationToken), "complete", args.Message, _logger))
                {
                    _logger.LogInformation("Engine completion {JobId} handled in {Duration}ms", job.JobId, outcome.Duration.TotalMilliseconds);
                }
            }
            else if (outcome.Status == JobStatus.Poisoned || job.IsFinalDelivery)
            {
                if (await DeadLetterAsync(args,
                        outcome.Status == JobStatus.Poisoned ? "Poisoned" : "MaxRetriesExceeded",
                        outcome.Status == JobStatus.Poisoned ? "The completion is not valid" : "The completion could not be handled in the allowed deliveries"))
                {
                    _logger.LogError("Engine completion {JobId} dead-lettered after {DeliveryCount} deliveries: {Error}", job.JobId, deliveryCount, outcome.ErrorMessage);
                }
            }
            else
            {
                _logger.LogWarning("Engine completion {JobId} will retry (delivery {DeliveryCount}): {Error}", job.JobId, deliveryCount, outcome.ErrorMessage);
                await JobMessageRetry.RetryLaterAsync(args, _retrySender, deliveryCount, _time, _logger);
            }
        }
        catch (Exception ex) when (JobMessageRetry.IsLockLost(ex))
        {
            // This receiver no longer holds the message: no settlement and no retry copy can follow. The broker redelivers it.
            _logger.LogWarning(ex,
                "Lost the lock on engine completion message {MessageId} (delivery count: {DeliveryCount}); the broker redelivers it",
                args.Message.MessageId, deliveryCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing engine completion message {MessageId}", args.Message.MessageId);

            if (deliveryCount >= ServiceBusJobProcessor.MaxUnexpectedErrorDeliveries)
            {
                // Closed text: an exception message may carry content.
                await DeadLetterAsync(args, "ProcessingError", "Unexpected error");
            }
            else
            {
                await JobMessageRetry.RetryLaterAsync(args, _retrySender, deliveryCount, _time, _logger);
            }
        }
    }

    private Task<bool> DeadLetterAsync(ProcessMessageEventArgs args, string reason, string description) =>
        JobMessageRetry.TrySettleAsync(
            () => args.DeadLetterMessageAsync(args.Message, reason, description, args.CancellationToken),
            $"dead-letter ({reason})", args.Message, _logger);

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "Intake engine completion processor error in {EntityPath} (source: {ErrorSource})",
            args.EntityPath, args.ErrorSource);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping the intake engine completion processor...");
        if (_processor != null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
