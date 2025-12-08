using System.Text.Json;
using Azure.Messaging.ServiceBus;

namespace Sprk.Bff.Api.Services.Jobs;

/// <summary>
/// Generic Service Bus processor that handles jobs using the ADR-004 job contract.
/// Routes jobs to appropriate IJobHandler implementations based on JobType.
/// Implements idempotency, retry logic, and dead-letter queue handling.
/// </summary>
public class ServiceBusJobProcessor : BackgroundService
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ServiceBusJobProcessor> _logger;
    private readonly string _queueName;
    private readonly int _maxConcurrentCalls;
    private ServiceBusProcessor? _processor;

    public ServiceBusJobProcessor(
        ServiceBusClient serviceBusClient,
        IServiceProvider serviceProvider,
        ILogger<ServiceBusJobProcessor> logger,
        IConfiguration configuration)
    {
        _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _queueName = configuration["Jobs:ServiceBus:QueueName"] ?? "sdap-jobs";
        _maxConcurrentCalls = configuration.GetValue<int>("Jobs:ServiceBus:MaxConcurrentCalls", 5);
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
                await _processor.StopProcessingAsync();
                await _processor.DisposeAsync();
            }
            _logger.LogInformation("Service Bus Job Processor stopped");
        }
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        using var scope = _serviceProvider.CreateScope();
        var startTime = DateTime.UtcNow;

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

            _logger.LogInformation(
                "Processing job {JobId} of type {JobType}, attempt {Attempt}/{MaxAttempts}, delivery count {DeliveryCount}",
                job.JobId, job.JobType, job.Attempt, job.MaxAttempts, args.Message.DeliveryCount);

            // Find appropriate handler
            var handlers = scope.ServiceProvider.GetServices<IJobHandler>().ToList();
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
            else if (outcome.Status == JobStatus.Poisoned || job.IsAtMaxAttempts || args.Message.DeliveryCount >= 5)
            {
                // Dead-letter: poisoned, max attempts, or delivery count exceeded
                await args.DeadLetterMessageAsync(args.Message,
                    outcome.Status == JobStatus.Poisoned ? "Poisoned" : "MaxRetriesExceeded",
                    outcome.ErrorMessage ?? "Job failed after maximum attempts",
                    args.CancellationToken);

                _logger.LogError(
                    "Job {JobId} dead-lettered after {Attempts} attempts (delivery count: {DeliveryCount}): {Error}",
                    job.JobId, job.Attempt, args.Message.DeliveryCount, outcome.ErrorMessage);
            }
            else
            {
                // Retry - abandon message (will be redelivered)
                await args.AbandonMessageAsync(args.Message, null, args.CancellationToken);
                _logger.LogWarning(
                    "Job {JobId} failed on attempt {Attempt}, will retry (delivery count: {DeliveryCount}): {Error}",
                    job.JobId, job.Attempt, args.Message.DeliveryCount, outcome.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing message {MessageId}: {Error}",
                args.Message.MessageId, ex.Message);

            // Dead-letter after 3 delivery attempts
            if (args.Message.DeliveryCount >= 3)
            {
                await args.DeadLetterMessageAsync(args.Message,
                    "ProcessingError",
                    $"Unexpected error: {ex.Message}",
                    args.CancellationToken);

                _logger.LogError("Message {MessageId} dead-lettered after {DeliveryCount} delivery attempts",
                    args.Message.MessageId, args.Message.DeliveryCount);
            }
            else
            {
                // Retry
                await args.AbandonMessageAsync(args.Message, null, args.CancellationToken);
                _logger.LogWarning("Message {MessageId} abandoned, will retry (delivery count: {DeliveryCount})",
                    args.Message.MessageId, args.Message.DeliveryCount);
            }
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "Service Bus processor error in {EntityPath} (source: {ErrorSource}): {Error}",
            args.EntityPath, args.ErrorSource, args.Exception.Message);
        return Task.CompletedTask;
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
