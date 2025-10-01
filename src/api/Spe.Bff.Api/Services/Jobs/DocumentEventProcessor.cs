using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Spe.Bff.Api.Services.Jobs.Handlers;
using System.Text.Json;

namespace Spe.Bff.Api.Services.Jobs;

/// <summary>
/// Background service that processes document events from Azure Service Bus.
/// Implements the async processing pipeline for document operations.
/// </summary>
public class DocumentEventProcessor : BackgroundService
{
    private readonly ServiceBusProcessor _processor;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DocumentEventProcessor> _logger;
    private readonly DocumentEventProcessorOptions _options;

    public DocumentEventProcessor(
        ServiceBusClient serviceBusClient,
        IServiceProvider serviceProvider,
        ILogger<DocumentEventProcessor> logger,
        IOptions<DocumentEventProcessorOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;

        _processor = serviceBusClient.CreateProcessor(_options.QueueName, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = _options.MaxConcurrentCalls,
            AutoCompleteMessages = false,
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(10),
            ReceiveMode = ServiceBusReceiveMode.PeekLock
        });

        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Document Event Processor starting...");

        try
        {
            await _processor.StartProcessingAsync(stoppingToken);
            _logger.LogInformation("Document Event Processor started successfully");

            // Keep the service running
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Document Event Processor stopping...");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Document Event Processor failed to start: {Error}", ex.Message);
            throw;
        }
        finally
        {
            await _processor.StopProcessingAsync();
            _logger.LogInformation("Document Event Processor stopped");
        }
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        using var scope = _serviceProvider.CreateScope();
        var correlationId = args.Message.CorrelationId ?? Guid.NewGuid().ToString();

        using var activity = DocumentEventTelemetry.StartActivity("ProcessMessage", correlationId);
        var startTime = DateTime.UtcNow;

        try
        {
            var documentEvent = DeserializeMessage(args.Message);
            var idempotencyService = scope.ServiceProvider.GetRequiredService<IIdempotencyService>();

            // Check if event was already processed (idempotency)
            if (await idempotencyService.IsEventProcessedAsync(documentEvent.EventId, args.CancellationToken))
            {
                _logger.LogWarning("Event {EventId} already processed, skipping (idempotency)", documentEvent.EventId);
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                return;
            }

            // Acquire processing lock to prevent concurrent processing
            if (!await idempotencyService.TryAcquireProcessingLockAsync(documentEvent.EventId, cancellationToken: args.CancellationToken))
            {
                _logger.LogWarning("Event {EventId} is being processed by another instance, abandoning", documentEvent.EventId);
                await args.AbandonMessageAsync(args.Message, null, args.CancellationToken);
                return;
            }

            try
            {
                if (_options.EnableDetailedLogging)
                {
                    _logger.LogInformation("Processing document event {EventId} for operation {Operation} on document {DocumentId}",
                        documentEvent.EventId, documentEvent.Operation, documentEvent.DocumentId);
                }

                var handler = scope.ServiceProvider.GetRequiredService<IDocumentEventHandler>();
                await handler.HandleEventAsync(documentEvent, args.CancellationToken);

                // Mark event as processed (idempotency tracking)
                await idempotencyService.MarkEventAsProcessedAsync(documentEvent.EventId, cancellationToken: args.CancellationToken);

                // Complete the message (remove from queue)
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);

                var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
                DocumentEventTelemetry.RecordEventProcessed(documentEvent.Operation, success: true);
                DocumentEventTelemetry.RecordProcessingDuration(duration, documentEvent.Operation);

                _logger.LogInformation("Successfully processed event {EventId}", documentEvent.EventId);
            }
            finally
            {
                // Release processing lock
                await idempotencyService.ReleaseProcessingLockAsync(documentEvent.EventId, args.CancellationToken);
            }
        }
        catch (Exception ex)
        {
            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            DocumentEventTelemetry.RecordEventProcessed("Unknown", success: false);
            DocumentEventTelemetry.RecordProcessingDuration(duration, "Unknown");

            _logger.LogError(ex, "Failed to process message {MessageId}: {Error}",
                args.Message.MessageId, ex.Message);

            await HandleProcessingError(args, ex);
        }
    }

    private async Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception, "Service Bus processor error in {EntityPath}: {Error}",
            args.EntityPath, args.Exception.Message);

        // Add alerting/notification logic here if needed
        await NotifyProcessorError(args.Exception);
    }

    private DocumentEvent DeserializeMessage(ServiceBusReceivedMessage message)
    {
        try
        {
            var json = message.Body.ToString();
            var documentEvent = JsonSerializer.Deserialize<DocumentEvent>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (documentEvent == null)
            {
                throw new InvalidOperationException("Failed to deserialize document event - result was null");
            }

            return documentEvent;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid message format: {ex.Message}", ex);
        }
    }

    private async Task HandleProcessingError(ProcessMessageEventArgs args, Exception exception)
    {
        var deliveryCount = args.Message.DeliveryCount;
        var maxRetries = _options.MaxRetryAttempts;

        if (deliveryCount < maxRetries)
        {
            // Abandon message for retry
            await args.AbandonMessageAsync(args.Message, null, args.CancellationToken);
            _logger.LogWarning("Message {MessageId} abandoned for retry (attempt {Attempt}/{MaxRetries})",
                args.Message.MessageId, deliveryCount, maxRetries);
        }
        else
        {
            // Dead letter message after max retries
            await args.DeadLetterMessageAsync(
                args.Message,
                new Dictionary<string, object>
                {
                    ["DeadLetterReason"] = "MaxRetriesExceeded",
                    ["DeadLetterErrorDescription"] = exception.Message
                },
                "MaxRetriesExceeded",
                exception.Message,
                args.CancellationToken);

            _logger.LogError("Message {MessageId} moved to dead letter queue after {MaxRetries} attempts",
                args.Message.MessageId, maxRetries);
        }
    }

    private async Task NotifyProcessorError(Exception exception)
    {
        // Implement alerting logic (email, Teams, Application Insights, etc.)
        // For now, just log the critical error
        _logger.LogCritical("Document Event Processor encountered critical error: {Error}", exception.Message);
        await Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Document Event Processor...");

        if (_processor != null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}