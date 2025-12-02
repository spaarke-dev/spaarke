using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Spe.Bff.Api.Services.Jobs;

/// <summary>
/// Telemetry and metrics for document event processing.
/// Provides observability into the async processing pipeline.
/// </summary>
public static class DocumentEventTelemetry
{
    private static readonly ActivitySource ActivitySource = new("Spaarke.DocumentEvents", "1.0.0");
    private static readonly Meter Meter = new("Spaarke.DocumentEvents", "1.0.0");

    private static readonly Counter<int> EventsProcessed =
        Meter.CreateCounter<int>("document_events.processed.total", "events", "Total number of document events processed");

    private static readonly Counter<int> EventsFailed =
        Meter.CreateCounter<int>("document_events.failed.total", "events", "Total number of document events that failed");

    private static readonly Histogram<double> ProcessingDuration =
        Meter.CreateHistogram<double>("document_events.processing.duration", "ms", "Duration of event processing");

    private static readonly Counter<int> ProcessorErrors =
        Meter.CreateCounter<int>("document_events.processor.errors.total", "errors", "Total number of processor errors");

    /// <summary>
    /// Starts a new activity for distributed tracing.
    /// </summary>
    public static Activity? StartActivity(string name, string? correlationId = null)
    {
        var activity = ActivitySource.StartActivity(name);
        if (!string.IsNullOrEmpty(correlationId))
        {
            activity?.SetTag("correlation_id", correlationId);
        }
        return activity;
    }

    /// <summary>
    /// Records a processed event (success or failure).
    /// </summary>
    public static void RecordEventProcessed(string operation, bool success)
    {
        if (success)
        {
            EventsProcessed.Add(1, new KeyValuePair<string, object?>("operation", operation));
        }
        else
        {
            EventsFailed.Add(1, new KeyValuePair<string, object?>("operation", operation));
        }
    }

    /// <summary>
    /// Records a failed event.
    /// </summary>
    public static void RecordEventFailed(string operation, string errorType)
    {
        EventsFailed.Add(1,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("error_type", errorType));
    }

    /// <summary>
    /// Records the duration of event processing.
    /// </summary>
    public static void RecordProcessingDuration(double milliseconds, string operation)
    {
        ProcessingDuration.Record(milliseconds, new KeyValuePair<string, object?>("operation", operation));
    }

    /// <summary>
    /// Records a processor error.
    /// </summary>
    public static void RecordProcessorError(string errorType)
    {
        ProcessorErrors.Add(1, new KeyValuePair<string, object?>("error_type", errorType));
    }
}
