using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Dedicated Service Bus processor for communication/email jobs.
/// Reads from the "sdap-communication" queue and routes directly to
/// IncomingCommunicationJobHandler, isolated from the shared job queue.
///
/// This prevents cross-domain failures (e.g., a broken finance handler DI
/// registration) from blocking email processing. Each domain owns its queue.
/// </summary>
public class CommunicationJobProcessor : BackgroundService
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CommunicationJobProcessor> _logger;
    private readonly TimeProvider _time;
    private readonly string _queueName;
    private readonly int _maxConcurrentCalls;
    private ServiceBusProcessor? _processor;

    // ISS-025 fix round 1 (F1-a): the sender of scheduled retry copies to THIS processor's queue (sdap-communication).
    private readonly JobMessageRetry.RetrySender _retrySender;

    public CommunicationJobProcessor(
        ServiceBusClient serviceBusClient,
        IServiceProvider serviceProvider,
        ILogger<CommunicationJobProcessor> logger,
        IOptions<ServiceBusOptions> serviceBusOptions,
        TimeProvider? timeProvider = null)
    {
        _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;

        var options = serviceBusOptions?.Value ?? throw new ArgumentNullException(nameof(serviceBusOptions));
        _queueName = options.CommunicationQueueName;
        _retrySender = new JobMessageRetry.RetrySender(_serviceBusClient, _queueName);
        // Communication jobs are lighter weight; use lower concurrency
        _maxConcurrentCalls = Math.Max(1, options.MaxConcurrentCalls / 2);

        _logger.LogInformation(
            "CommunicationJobProcessor configured with queue '{QueueName}' and {MaxConcurrentCalls} concurrent calls",
            _queueName, _maxConcurrentCalls);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Communication Job Processor starting for queue {QueueName}...", _queueName);

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
            _logger.LogInformation("Communication Job Processor started successfully");

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Communication Job Processor stopping...");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Communication Job Processor failed to start: {Error}", ex.Message);
            throw;
        }
        finally
        {
            if (_processor != null)
            {
                try { await _processor.StopProcessingAsync(); }
                catch (ObjectDisposedException) { }
                catch (Exception ex) { _logger.LogWarning(ex, "Error stopping communication processor"); }

                try { await _processor.DisposeAsync(); }
                catch (ObjectDisposedException) { }
                catch (Exception ex) { _logger.LogWarning(ex, "Error disposing communication processor"); }
            }

            // After the processor has stopped, so no in-flight message is still scheduling a retry copy.
            await _retrySender.DisposeAsync(_logger);
            _logger.LogInformation("Communication Job Processor stopped");
        }
    }

    /// <remarks>
    /// ISS-025 fix round 1 (F1-a): the same rules as <see cref="ServiceBusJobProcessor"/>, through the shared
    /// <see cref="JobMessageRetry"/>. <see cref="JobContract.DeliveryCount"/> is set to the LOGICAL delivery before the handler runs,
    /// so <c>IncomingCommunicationJobHandler</c>'s HOLD sees its final delivery (<see cref="JobContract.IsFinalDelivery"/>) and alerts
    /// the administrators; before, it was never set on this queue, and a held email was dead-lettered by delivery count silently. A
    /// retryable failure is retried by a copy scheduled on THIS queue (<c>sdap-communication</c>); settlement failures are logged,
    /// never retried; a lost lock is not retried.
    /// </remarks>
    internal async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        using var scope = _serviceProvider.CreateScope();

        // The logical delivery: what earlier copies of this job spent, plus this message's own broker deliveries.
        var deliveryCount = JobMessageRetry.LogicalDeliveryCount(args.Message);

        try
        {
            var messageBody = args.Message.Body.ToString();
            var job = JsonSerializer.Deserialize<JobContract>(messageBody, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (job == null)
            {
                _logger.LogError("Failed to deserialize communication job from message {MessageId}", args.Message.MessageId);
                await DeadLetterAsync(args, "InvalidFormat", "Failed to deserialize job contract");
                return;
            }

            // The logical delivery count reaches the handler, so the inbound HOLD's last-chance branch sees the same final delivery
            // this processor dead-letters on (ISS-025 F1-a).
            job.DeliveryCount = deliveryCount;

            _logger.LogInformation(
                "Processing communication job {JobId} of type {JobType}, delivery count {DeliveryCount} (broker delivery {BrokerDeliveryCount})",
                job.JobId, job.JobType, deliveryCount, args.Message.DeliveryCount);

            // Route by JobType, resolving ONLY the matching handler — NO broad GetServices<IJobHandler>
            // enumeration (keeps a broken cross-domain handler from failing this queue) and no wasteful
            // construction of the unused handler's object graph (the email handler pulls a heavy dependency
            // graph). The communication queue carries two job types: "IncomingCommunication" (email inbound,
            // Graph webhook) and "IncomingMessaging" (ACS chat inbound, Event Grid — task 030/031).
            IJobHandler? handler = job.JobType switch
            {
                IncomingCommunicationJobHandler.JobTypeName =>
                    scope.ServiceProvider.GetRequiredService<IncomingCommunicationJobHandler>(),
                IncomingMessagingJobHandler.JobTypeName =>
                    scope.ServiceProvider.GetRequiredService<IncomingMessagingJobHandler>(),
                _ => null
            };

            if (handler is null)
            {
                _logger.LogError(
                    "Unexpected job type {JobType} on communication queue (expected {Expected1} or {Expected2})",
                    job.JobType,
                    IncomingCommunicationJobHandler.JobTypeName,
                    IncomingMessagingJobHandler.JobTypeName);
                await DeadLetterAsync(args, "WrongQueue", $"Job type '{job.JobType}' does not belong on the communication queue");
                return;
            }

            var outcome = await handler.ProcessAsync(job, args.CancellationToken);

            if (outcome.Status == JobStatus.Completed)
            {
                if (await JobMessageRetry.TrySettleAsync(
                        () => args.CompleteMessageAsync(args.Message, args.CancellationToken), "complete", args.Message, _logger))
                {
                    _logger.LogInformation(
                        "Communication job {JobId} completed in {Duration}ms",
                        job.JobId, outcome.Duration.TotalMilliseconds);
                }
            }
            else if (outcome.Status == JobStatus.Poisoned || job.IsFinalDelivery)
            {
                if (await DeadLetterAsync(args,
                        outcome.Status == JobStatus.Poisoned ? "Poisoned" : "MaxRetriesExceeded",
                        outcome.ErrorMessage ?? "Job failed after maximum attempts"))
                {
                    _logger.LogError(
                        "Communication job {JobId} dead-lettered after {DeliveryCount} deliveries: {Error}",
                        job.JobId, deliveryCount, outcome.ErrorMessage);
                }
            }
            else
            {
                _logger.LogWarning(
                    "Communication job {JobId} failed, will retry (delivery {DeliveryCount}): {Error}",
                    job.JobId, deliveryCount, outcome.ErrorMessage);
                await JobMessageRetry.RetryLaterAsync(args, _retrySender, deliveryCount, _time, _logger);
            }
        }
        catch (Exception ex) when (JobMessageRetry.IsLockLost(ex))
        {
            // This receiver no longer holds the message: no settlement and no retry copy can follow. The broker redelivers it.
            _logger.LogWarning(ex,
                "Lost the lock on communication message {MessageId} (delivery count: {DeliveryCount}); the broker redelivers it",
                args.Message.MessageId, deliveryCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing communication message {MessageId}: {Error}",
                args.Message.MessageId, ex.Message);

            if (deliveryCount >= ServiceBusJobProcessor.MaxUnexpectedErrorDeliveries)
            {
                await args.DeadLetterMessageAsync(args.Message, "ProcessingError",
                    $"Unexpected error: {ex.Message}", args.CancellationToken);
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
            "Communication processor error in {EntityPath} (source: {ErrorSource}): {Error}",
            args.EntityPath, args.ErrorSource, args.Exception.Message);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Communication Job Processor...");
        if (_processor != null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }
        await base.StopAsync(cancellationToken);
    }
}
