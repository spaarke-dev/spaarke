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
    private readonly string _queueName;
    private readonly int _maxConcurrentCalls;
    private ServiceBusProcessor? _processor;

    public CommunicationJobProcessor(
        ServiceBusClient serviceBusClient,
        IServiceProvider serviceProvider,
        ILogger<CommunicationJobProcessor> logger,
        IOptions<ServiceBusOptions> serviceBusOptions)
    {
        _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var options = serviceBusOptions?.Value ?? throw new ArgumentNullException(nameof(serviceBusOptions));
        _queueName = options.CommunicationQueueName;
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
            _logger.LogInformation("Communication Job Processor stopped");
        }
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        using var scope = _serviceProvider.CreateScope();

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
                await args.DeadLetterMessageAsync(args.Message, "InvalidFormat",
                    "Failed to deserialize job contract", args.CancellationToken);
                return;
            }

            _logger.LogInformation(
                "Processing communication job {JobId} of type {JobType}, delivery count {DeliveryCount}",
                job.JobId, job.JobType, args.Message.DeliveryCount);

            // Resolve only the communication handler — no GetServices<IJobHandler> enumeration
            var handler = scope.ServiceProvider.GetRequiredService<IncomingCommunicationJobHandler>();

            if (job.JobType != handler.JobType)
            {
                _logger.LogError(
                    "Unexpected job type {JobType} on communication queue (expected {Expected})",
                    job.JobType, handler.JobType);
                await args.DeadLetterMessageAsync(args.Message, "WrongQueue",
                    $"Job type '{job.JobType}' does not belong on the communication queue",
                    args.CancellationToken);
                return;
            }

            var outcome = await handler.ProcessAsync(job, args.CancellationToken);

            if (outcome.Status == JobStatus.Completed)
            {
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                _logger.LogInformation(
                    "Communication job {JobId} completed in {Duration}ms",
                    job.JobId, outcome.Duration.TotalMilliseconds);
            }
            else if (outcome.Status == JobStatus.Poisoned || job.IsAtMaxAttempts || args.Message.DeliveryCount >= 5)
            {
                await args.DeadLetterMessageAsync(args.Message,
                    outcome.Status == JobStatus.Poisoned ? "Poisoned" : "MaxRetriesExceeded",
                    outcome.ErrorMessage ?? "Job failed after maximum attempts",
                    args.CancellationToken);
                _logger.LogError(
                    "Communication job {JobId} dead-lettered after {DeliveryCount} deliveries: {Error}",
                    job.JobId, args.Message.DeliveryCount, outcome.ErrorMessage);
            }
            else
            {
                await args.AbandonMessageAsync(args.Message, null, args.CancellationToken);
                _logger.LogWarning(
                    "Communication job {JobId} failed, will retry (delivery {DeliveryCount}): {Error}",
                    job.JobId, args.Message.DeliveryCount, outcome.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing communication message {MessageId}: {Error}",
                args.Message.MessageId, ex.Message);

            if (args.Message.DeliveryCount >= 3)
            {
                await args.DeadLetterMessageAsync(args.Message, "ProcessingError",
                    $"Unexpected error: {ex.Message}", args.CancellationToken);
            }
            else
            {
                await args.AbandonMessageAsync(args.Message, null, args.CancellationToken);
            }
        }
    }

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
