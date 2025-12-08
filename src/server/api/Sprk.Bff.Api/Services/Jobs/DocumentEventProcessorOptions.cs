namespace Sprk.Bff.Api.Services.Jobs;

/// <summary>
/// Configuration options for the Document Event Processor background service.
/// </summary>
public class DocumentEventProcessorOptions
{
    /// <summary>
    /// Name of the Service Bus queue to process events from.
    /// </summary>
    public string QueueName { get; set; } = "document-events";

    /// <summary>
    /// Maximum number of messages to process concurrently.
    /// </summary>
    public int MaxConcurrentCalls { get; set; } = 5;

    /// <summary>
    /// Maximum number of retry attempts before dead-lettering a message.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Duration to hold message lock for processing.
    /// </summary>
    public TimeSpan MessageLockDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether to enable dead-lettering for failed messages.
    /// </summary>
    public bool EnableDeadLettering { get; set; } = true;

    /// <summary>
    /// Whether to enable detailed diagnostic logging.
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = true;
}
