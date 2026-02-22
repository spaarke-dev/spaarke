using System.Diagnostics;
using System.Text.Json;
using Sprk.Bff.Api.Services.Communication;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Job handler for incoming email communication processing.
/// Extracts mailbox and message ID from the job payload, then delegates to
/// IncomingCommunicationProcessor for full message fetch, Dataverse record creation,
/// attachment processing, and .eml archival.
///
/// Follows ADR-004 for job contract patterns and idempotency.
/// The processor handles deduplication via sprk_graphmessageid checks.
/// </summary>
/// <remarks>
/// Job type: "IncomingCommunication"
/// Enqueued by: CommunicationEndpoints.HandleIncomingWebhookAsync
/// Payload fields: SubscriptionId, Resource, MessageId, ChangeType, TenantId, TriggerSource
///
/// The Resource field contains the Graph resource path:
///   "users/{mailbox}/mailFolders/{folder}/messages/{messageId}"
/// or "users/{mailbox}/messages/{messageId}"
///
/// Idempotency key pattern: IncomingComm:{subscriptionId}:{messageId}
/// </remarks>
public class IncomingCommunicationJobHandler : IJobHandler
{
    private readonly IncomingCommunicationProcessor _processor;
    private readonly ILogger<IncomingCommunicationJobHandler> _logger;

    /// <summary>
    /// Job type constant - must match JobTypeIncomingCommunication in CommunicationEndpoints.
    /// </summary>
    public const string JobTypeName = "IncomingCommunication";

    public IncomingCommunicationJobHandler(
        IncomingCommunicationProcessor processor,
        ILogger<IncomingCommunicationJobHandler> logger)
    {
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => JobTypeName;

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "Processing incoming communication job {JobId}, Attempt {Attempt}/{MaxAttempts}, " +
                "CorrelationId {CorrelationId}",
                job.JobId, job.Attempt, job.MaxAttempts, job.CorrelationId);

            // Parse payload
            var payload = ParsePayload(job.Payload);
            if (payload is null)
            {
                _logger.LogError(
                    "Invalid payload for incoming communication job {JobId}",
                    job.JobId);
                return JobOutcome.Poisoned(
                    job.JobId, JobType,
                    "Invalid job payload: could not extract MessageId or Resource",
                    job.Attempt, stopwatch.Elapsed);
            }

            var messageId = payload.MessageId;
            var mailboxEmail = ExtractMailboxFromResource(payload.Resource);

            if (string.IsNullOrEmpty(messageId))
            {
                _logger.LogError(
                    "Missing MessageId in incoming communication job {JobId} payload",
                    job.JobId);
                return JobOutcome.Poisoned(
                    job.JobId, JobType,
                    "Missing MessageId in job payload",
                    job.Attempt, stopwatch.Elapsed);
            }

            if (string.IsNullOrEmpty(mailboxEmail))
            {
                _logger.LogError(
                    "Could not extract mailbox email from Resource '{Resource}' in job {JobId}",
                    payload.Resource, job.JobId);
                return JobOutcome.Poisoned(
                    job.JobId, JobType,
                    $"Could not extract mailbox email from Resource: {payload.Resource}",
                    job.Attempt, stopwatch.Elapsed);
            }

            _logger.LogInformation(
                "Dispatching to IncomingCommunicationProcessor | Mailbox: {Mailbox}, " +
                "MessageId: {MessageId}, JobId: {JobId}",
                mailboxEmail, messageId, job.JobId);

            // Delegate to processor
            await _processor.ProcessAsync(mailboxEmail, messageId, ct);

            stopwatch.Stop();
            _logger.LogInformation(
                "Incoming communication job {JobId} completed in {Duration}ms | " +
                "Mailbox: {Mailbox}, MessageId: {MessageId}",
                job.JobId, stopwatch.ElapsedMilliseconds, mailboxEmail, messageId);

            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "Incoming communication job {JobId} failed on attempt {Attempt}: {Error}",
                job.JobId, job.Attempt, ex.Message);

            // Determine if retryable
            if (IsRetryableException(ex) && !job.IsAtMaxAttempts)
            {
                return JobOutcome.Failure(
                    job.JobId, JobType, ex.Message,
                    job.Attempt, stopwatch.Elapsed);
            }

            return JobOutcome.Poisoned(
                job.JobId, JobType, ex.Message,
                job.Attempt, stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Extracts the mailbox email from the Graph resource path.
    /// Resource format: "users/{mailboxEmail}/mailFolders/{folder}/messages/{messageId}"
    ///               or "users/{mailboxEmail}/messages/{messageId}"
    /// </summary>
    private static string? ExtractMailboxFromResource(string? resource)
    {
        if (string.IsNullOrEmpty(resource))
            return null;

        // Expected: "users/{email}/..." — extract the segment after "users/"
        const string prefix = "users/";
        var startIndex = resource.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
            return null;

        var emailStart = startIndex + prefix.Length;
        var nextSlash = resource.IndexOf('/', emailStart);

        return nextSlash > emailStart
            ? resource[emailStart..nextSlash]
            : resource[emailStart..]; // email is the last segment (unlikely but safe)
    }

    private IncomingCommunicationPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<IncomingCommunicationPayload>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse incoming communication job payload");
            return null;
        }
    }

    /// <summary>
    /// Determines if an exception represents a transient failure that should be retried.
    /// </summary>
    private static bool IsRetryableException(Exception ex)
    {
        // Graph API throttling, network issues, Dataverse transient errors
        if (ex is HttpRequestException or TaskCanceledException or TimeoutException)
            return true;

        // Microsoft.Graph.Models.ODataErrors.ODataError with specific status codes
        if (ex.GetType().Name.Contains("ODataError", StringComparison.OrdinalIgnoreCase))
        {
            var message = ex.Message;
            return message.Contains("429", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("503", StringComparison.OrdinalIgnoreCase) ||
                   message.Contains("504", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}

/// <summary>
/// Payload structure for incoming communication jobs.
/// Matches the anonymous object serialized in HandleIncomingWebhookAsync.
/// </summary>
public class IncomingCommunicationPayload
{
    /// <summary>Graph subscription ID that received the notification.</summary>
    public string? SubscriptionId { get; set; }

    /// <summary>Graph resource path (e.g., "users/{email}/messages/{id}").</summary>
    public string? Resource { get; set; }

    /// <summary>Graph message ID.</summary>
    public string? MessageId { get; set; }

    /// <summary>Change type (e.g., "created").</summary>
    public string? ChangeType { get; set; }

    /// <summary>Azure AD tenant ID.</summary>
    public string? TenantId { get; set; }

    /// <summary>Source of the trigger (e.g., "GraphWebhook").</summary>
    public string? TriggerSource { get; set; }
}
