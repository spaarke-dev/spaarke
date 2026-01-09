using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Sprk.Bff.Api.Models.Jobs;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Jobs;

/// <summary>
/// Service for managing dead-lettered messages in the Service Bus DLQ.
/// Provides capabilities to list, inspect, and re-drive failed job messages.
/// </summary>
public class DeadLetterQueueService
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly EmailTelemetry _telemetry;
    private readonly ILogger<DeadLetterQueueService> _logger;
    private readonly string _queueName;
    private readonly string _dlqPath;

    public DeadLetterQueueService(
        ServiceBusClient serviceBusClient,
        EmailTelemetry telemetry,
        IConfiguration configuration,
        ILogger<DeadLetterQueueService> logger)
    {
        _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _queueName = configuration["Jobs:ServiceBus:QueueName"] ?? "sdap-jobs";
        _dlqPath = $"{_queueName}/$deadletterqueue";
    }

    /// <summary>
    /// Gets a summary of the DLQ status including message count and breakdown by reason.
    /// </summary>
    public async Task<DlqSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Getting DLQ summary for queue {QueueName}", _queueName);

        await using var receiver = _serviceBusClient.CreateReceiver(
            _queueName,
            new ServiceBusReceiverOptions
            {
                SubQueue = SubQueue.DeadLetter,
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });

        var summary = new DlqSummary
        {
            QueueName = _queueName,
            MessageCount = 0,
            SizeBytes = 0,
            OldestMessageTimestamp = null,
            ReasonCounts = []
        };

        var reasonCounts = new Dictionary<string, int>();
        DateTime? oldestTimestamp = null;
        long totalSize = 0;
        int count = 0;

        // Peek messages to build summary (max 100 for performance)
        var messages = await receiver.PeekMessagesAsync(100, cancellationToken: ct);

        foreach (var message in messages)
        {
            count++;
            totalSize += message.Body.ToMemory().Length;

            var timestamp = message.EnqueuedTime.UtcDateTime;
            if (oldestTimestamp == null || timestamp < oldestTimestamp)
            {
                oldestTimestamp = timestamp;
            }

            var reason = message.DeadLetterReason ?? "Unknown";
            reasonCounts[reason] = reasonCounts.GetValueOrDefault(reason, 0) + 1;
        }

        return new DlqSummary
        {
            QueueName = _queueName,
            MessageCount = count,
            SizeBytes = totalSize,
            OldestMessageTimestamp = oldestTimestamp,
            ReasonCounts = reasonCounts
        };
    }

    /// <summary>
    /// Lists messages in the DLQ with pagination support.
    /// </summary>
    public async Task<DlqListResponse> ListMessagesAsync(
        int maxMessages = 50,
        long fromSequenceNumber = 0,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Listing DLQ messages for queue {QueueName}, max {MaxMessages}, from sequence {FromSequence}",
            _queueName, maxMessages, fromSequenceNumber);

        await using var receiver = _serviceBusClient.CreateReceiver(
            _queueName,
            new ServiceBusReceiverOptions
            {
                SubQueue = SubQueue.DeadLetter,
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });

        var messages = new List<DlqMessage>();
        var summary = await GetSummaryAsync(ct);

        // Peek messages starting from sequence number
        IReadOnlyList<ServiceBusReceivedMessage> peekedMessages;
        if (fromSequenceNumber > 0)
        {
            peekedMessages = await receiver.PeekMessagesAsync(
                maxMessages + 1,
                fromSequenceNumber,
                ct);
        }
        else
        {
            peekedMessages = await receiver.PeekMessagesAsync(maxMessages + 1, cancellationToken: ct);
        }

        var hasMore = peekedMessages.Count > maxMessages;
        var messagesToProcess = peekedMessages.Take(maxMessages);

        foreach (var message in messagesToProcess)
        {
            messages.Add(MapToDlqMessage(message));
        }

        _telemetry.RecordDlqListOperation(messages.Count);

        return new DlqListResponse
        {
            Summary = summary,
            Messages = messages,
            TotalCount = summary.MessageCount,
            HasMore = hasMore
        };
    }

    /// <summary>
    /// Gets a specific DLQ message by its sequence number.
    /// </summary>
    public async Task<DlqMessage?> GetMessageAsync(long sequenceNumber, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Getting DLQ message {SequenceNumber} from queue {QueueName}",
            sequenceNumber, _queueName);

        await using var receiver = _serviceBusClient.CreateReceiver(
            _queueName,
            new ServiceBusReceiverOptions
            {
                SubQueue = SubQueue.DeadLetter,
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });

        var messages = await receiver.PeekMessagesAsync(1, sequenceNumber, ct);
        var message = messages.FirstOrDefault();

        if (message == null || message.SequenceNumber != sequenceNumber)
        {
            _logger.LogWarning("DLQ message {SequenceNumber} not found", sequenceNumber);
            return null;
        }

        return MapToDlqMessage(message);
    }

    /// <summary>
    /// Re-drives messages from DLQ back to the main queue for reprocessing.
    /// </summary>
    public async Task<RedriveResponse> RedriveMessagesAsync(
        RedriveRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Re-driving messages from DLQ for queue {QueueName}: SequenceNumbers={SequenceNumbers}, MaxMessages={MaxMessages}, ReasonFilter={ReasonFilter}",
            _queueName,
            request.SequenceNumbers?.Count ?? 0,
            request.MaxMessages,
            request.ReasonFilter);

        var response = new RedriveResponse
        {
            SuccessCount = 0,
            FailureCount = 0,
            Errors = []
        };

        await using var receiver = _serviceBusClient.CreateReceiver(
            _queueName,
            new ServiceBusReceiverOptions
            {
                SubQueue = SubQueue.DeadLetter,
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });

        await using var sender = _serviceBusClient.CreateSender(_queueName);

        var messagesToRedrive = new List<ServiceBusReceivedMessage>();

        if (request.SequenceNumbers != null && request.SequenceNumbers.Count > 0)
        {
            // Re-drive specific messages by sequence number
            foreach (var seqNum in request.SequenceNumbers.Take(request.MaxMessages))
            {
                try
                {
                    var message = await receiver.ReceiveDeferredMessageAsync(seqNum, ct);
                    if (message != null)
                    {
                        messagesToRedrive.Add(message);
                    }
                    else
                    {
                        // Try to receive by peeking and matching
                        var peeked = await receiver.PeekMessagesAsync(1, seqNum, ct);
                        var target = peeked.FirstOrDefault(m => m.SequenceNumber == seqNum);
                        if (target != null)
                        {
                            // Need to receive it properly
                            var received = await ReceiveBySequenceNumberAsync(receiver, seqNum, ct);
                            if (received != null)
                            {
                                messagesToRedrive.Add(received);
                            }
                            else
                            {
                                response.FailureCount++;
                                response.Errors.Add(new RedriveError
                                {
                                    SequenceNumber = seqNum,
                                    Error = "Could not receive message for re-drive"
                                });
                            }
                        }
                        else
                        {
                            response.FailureCount++;
                            response.Errors.Add(new RedriveError
                            {
                                SequenceNumber = seqNum,
                                Error = "Message not found in DLQ"
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    response.FailureCount++;
                    response.Errors.Add(new RedriveError
                    {
                        SequenceNumber = seqNum,
                        Error = ex.Message
                    });
                    _logger.LogWarning(ex, "Failed to retrieve DLQ message {SequenceNumber}", seqNum);
                }
            }
        }
        else
        {
            // Re-drive all messages (up to MaxMessages) optionally filtered by reason
            var received = await receiver.ReceiveMessagesAsync(
                request.MaxMessages,
                TimeSpan.FromSeconds(5),
                ct);

            foreach (var message in received)
            {
                // Apply reason filter if specified
                if (!string.IsNullOrEmpty(request.ReasonFilter) &&
                    !string.Equals(message.DeadLetterReason, request.ReasonFilter, StringComparison.OrdinalIgnoreCase))
                {
                    // Don't re-drive, abandon to keep in DLQ
                    await receiver.AbandonMessageAsync(message, cancellationToken: ct);
                    continue;
                }

                messagesToRedrive.Add(message);
            }
        }

        // Re-drive each message
        foreach (var message in messagesToRedrive)
        {
            try
            {
                // Parse job contract to update attempt count
                var bodyString = message.Body.ToString();
                var job = JsonSerializer.Deserialize<JobContract>(bodyString, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                if (job != null)
                {
                    // Reset attempt count for re-drive
                    var newJob = new JobContract
                    {
                        JobId = job.JobId,
                        JobType = job.JobType,
                        SubjectId = job.SubjectId,
                        CorrelationId = job.CorrelationId,
                        IdempotencyKey = job.IdempotencyKey,
                        Attempt = 0, // Reset attempt count for re-drive
                        MaxAttempts = job.MaxAttempts,
                        Payload = job.Payload,
                        CreatedAt = job.CreatedAt
                    };
                    var newBody = JsonSerializer.Serialize(newJob, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });

                    var newMessage = new ServiceBusMessage(newBody)
                    {
                        MessageId = Guid.NewGuid().ToString(),
                        CorrelationId = job.CorrelationId,
                        ContentType = "application/json",
                        Subject = job.JobType
                    };

                    // Copy application properties
                    newMessage.ApplicationProperties["OriginalMessageId"] = message.MessageId;
                    newMessage.ApplicationProperties["OriginalSequenceNumber"] = message.SequenceNumber;
                    newMessage.ApplicationProperties["RedriveTimestamp"] = DateTime.UtcNow.ToString("O");
                    newMessage.ApplicationProperties["OriginalDeadLetterReason"] = message.DeadLetterReason ?? "";

                    await sender.SendMessageAsync(newMessage, ct);
                    await receiver.CompleteMessageAsync(message, ct);

                    response.SuccessCount++;
                    _logger.LogInformation(
                        "Re-drove message {OriginalSequenceNumber} as new message {NewMessageId}",
                        message.SequenceNumber, newMessage.MessageId);
                }
                else
                {
                    // Not a valid job contract, re-send as-is
                    var newMessage = new ServiceBusMessage(message.Body)
                    {
                        MessageId = Guid.NewGuid().ToString(),
                        ContentType = message.ContentType,
                        Subject = message.Subject
                    };

                    await sender.SendMessageAsync(newMessage, ct);
                    await receiver.CompleteMessageAsync(message, ct);

                    response.SuccessCount++;
                }
            }
            catch (Exception ex)
            {
                response.FailureCount++;
                response.Errors.Add(new RedriveError
                {
                    SequenceNumber = message.SequenceNumber,
                    Error = ex.Message
                });

                // Abandon message to keep in DLQ
                try
                {
                    await receiver.AbandonMessageAsync(message, cancellationToken: ct);
                }
                catch
                {
                    // Ignore abandon errors
                }

                _logger.LogWarning(ex, "Failed to re-drive DLQ message {SequenceNumber}", message.SequenceNumber);
            }
        }

        response.Message = $"Re-drive completed: {response.SuccessCount} succeeded, {response.FailureCount} failed";

        _telemetry.RecordDlqRedriveOperation(response.SuccessCount, response.FailureCount);

        _logger.LogInformation(
            "DLQ re-drive completed for queue {QueueName}: {SuccessCount} succeeded, {FailureCount} failed",
            _queueName, response.SuccessCount, response.FailureCount);

        return response;
    }

    /// <summary>
    /// Attempts to receive a message by sequence number by receiving messages until found.
    /// </summary>
    private async Task<ServiceBusReceivedMessage?> ReceiveBySequenceNumberAsync(
        ServiceBusReceiver receiver,
        long targetSequenceNumber,
        CancellationToken ct)
    {
        // Receive a batch of messages and look for our target
        var messages = await receiver.ReceiveMessagesAsync(50, TimeSpan.FromSeconds(2), ct);

        foreach (var message in messages)
        {
            if (message.SequenceNumber == targetSequenceNumber)
            {
                // Found it - abandon all others
                foreach (var other in messages.Where(m => m.SequenceNumber != targetSequenceNumber))
                {
                    await receiver.AbandonMessageAsync(other, cancellationToken: ct);
                }
                return message;
            }
        }

        // Not found in this batch - abandon all and return null
        foreach (var message in messages)
        {
            await receiver.AbandonMessageAsync(message, cancellationToken: ct);
        }

        return null;
    }

    /// <summary>
    /// Maps a Service Bus message to a DlqMessage model.
    /// </summary>
    private DlqMessage MapToDlqMessage(ServiceBusReceivedMessage message)
    {
        DlqJobInfo? jobInfo = null;
        string? rawBody = null;

        try
        {
            var bodyString = message.Body.ToString();
            var job = JsonSerializer.Deserialize<JobContract>(bodyString, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (job != null)
            {
                jobInfo = new DlqJobInfo
                {
                    JobId = job.JobId,
                    JobType = job.JobType,
                    SubjectId = job.SubjectId,
                    CorrelationId = job.CorrelationId,
                    IdempotencyKey = job.IdempotencyKey,
                    Attempt = job.Attempt,
                    MaxAttempts = job.MaxAttempts
                };
            }
            else
            {
                rawBody = bodyString;
            }
        }
        catch
        {
            rawBody = message.Body.ToString();
        }

        var properties = new Dictionary<string, object?>();
        foreach (var prop in message.ApplicationProperties)
        {
            properties[prop.Key] = prop.Value;
        }

        return new DlqMessage
        {
            MessageId = message.MessageId,
            SequenceNumber = message.SequenceNumber,
            EnqueuedTime = message.EnqueuedTime.UtcDateTime,
            DeadLetterReason = message.DeadLetterReason,
            DeadLetterErrorDescription = message.DeadLetterErrorDescription,
            DeliveryCount = message.DeliveryCount,
            Job = jobInfo,
            RawBody = rawBody,
            Properties = properties
        };
    }
}
