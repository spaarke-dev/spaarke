using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Communication endpoints for sending emails via Graph API.
/// POST /send: Single email send. POST /send-bulk: Bulk send to multiple recipients.
/// GET /{id}/status: Communication status lookup.
/// POST /accounts/{id}/verify: Mailbox verification.
/// POST /incoming-webhook: Graph change notification receiver for inbound emails.
/// </summary>
public static class CommunicationEndpoints
{
    /// <summary>
    /// Job type for processing incoming email notifications from Graph webhooks.
    /// </summary>
    private const string JobTypeIncomingCommunication = "IncomingCommunication";

    /// <summary>
    /// In-memory deduplication cache for Graph notification IDs.
    /// Prevents processing the same notification twice when Graph retries delivery.
    /// Entries expire after 10 minutes (Graph retry window is typically under 5 minutes).
    /// </summary>
    private static readonly ConcurrentDictionary<string, DateTimeOffset> _recentNotifications = new();

    /// <summary>
    /// How long to keep notification IDs in the deduplication cache.
    /// </summary>
    private static readonly TimeSpan DeduplicationWindow = TimeSpan.FromMinutes(10);

    public static IEndpointRouteBuilder MapCommunicationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/communications")
            .RequireAuthorization()
            .WithTags("Communications");

        group.MapPost("/send", SendCommunicationAsync)
            .AddEndpointFilter<CommunicationAuthorizationFilter>()
            .WithName("SendCommunication")
            .WithDescription("Send an email communication via Microsoft Graph API")
            .Produces<SendCommunicationResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        group.MapPost("/send-bulk", SendBulkCommunicationAsync)
            .AddEndpointFilter<CommunicationAuthorizationFilter>()
            .WithName("SendBulkCommunication")
            .WithDescription("Send an email communication to multiple recipients via Microsoft Graph API")
            .Produces<BulkSendResponse>(StatusCodes.Status200OK)
            .Produces<BulkSendResponse>(207)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        group.MapGet("/{id:guid}/status", GetCommunicationStatusAsync)
            .WithName("GetCommunicationStatus")
            .WithDescription("Get the status of a sent communication")
            .Produces<CommunicationStatusResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapPost("/accounts/{id:guid}/verify", VerifyCommunicationAccountAsync)
            .AddEndpointFilter<CommunicationAuthorizationFilter>()
            .WithName("VerifyCommunicationAccount")
            .WithDescription("Verify a communication account's mailbox capabilities (send and/or read)")
            .Produces<VerificationResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // POST /api/communications/incoming-webhook - Graph webhook receiver (AllowAnonymous with clientState validation)
        // Registered on app (not group) to avoid RequireAuthorization from the group.
        // Graph webhook delivery does not carry Bearer tokens; clientState validation replaces standard auth (ADR-008).
        app.MapPost("/api/communications/incoming-webhook", HandleIncomingWebhookAsync)
            .AllowAnonymous()
            .WithName("CommunicationIncomingWebhook")
            .WithTags("Communications")
            .WithDescription("Receive Microsoft Graph change notifications for new inbound emails")
            .Produces<IncomingWebhookResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task<IResult> SendCommunicationAsync(
        SendCommunicationRequest request,
        CommunicationService communicationService,
        ILogger<CommunicationService> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var response = await communicationService.SendAsync(request, context, ct);
        return TypedResults.Ok(response);
    }

    /// <summary>
    /// Maximum number of recipients allowed in a single bulk send request.
    /// </summary>
    private const int MaxBulkRecipients = 50;

    /// <summary>
    /// Delay in milliseconds between sequential sends for Graph API rate awareness.
    /// </summary>
    private const int InterSendDelayMs = 100;

    private static async Task<IResult> SendBulkCommunicationAsync(
        BulkSendRequest request,
        CommunicationService communicationService,
        ILogger<CommunicationService> logger,
        CancellationToken ct)
    {
        // Validate request
        if (request.Recipients is not { Length: > 0 })
        {
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: "At least one recipient is required.",
                statusCode: 400);
        }

        if (request.Recipients.Length > MaxBulkRecipients)
        {
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: $"Maximum {MaxBulkRecipients} recipients allowed per bulk request. Received {request.Recipients.Length}.",
                statusCode: 400);
        }

        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: "Subject is required.",
                statusCode: 400);
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: "Body is required.",
                statusCode: 400);
        }

        logger.LogInformation(
            "Starting bulk send | RecipientCount: {RecipientCount}, Subject: {Subject}",
            request.Recipients.Length,
            request.Subject);

        var results = new List<BulkSendResult>(request.Recipients.Length);

        for (var i = 0; i < request.Recipients.Length; i++)
        {
            var recipient = request.Recipients[i];

            // Build a SendCommunicationRequest for this individual recipient
            var individualRequest = new SendCommunicationRequest
            {
                To = new[] { recipient.To },
                Cc = recipient.Cc,
                Subject = request.Subject,
                Body = request.Body,
                BodyFormat = request.BodyFormat,
                FromMailbox = request.FromMailbox,
                CommunicationType = request.CommunicationType,
                AttachmentDocumentIds = request.AttachmentDocumentIds,
                ArchiveToSpe = request.ArchiveToSpe,
                Associations = request.Associations
            };

            try
            {
                var sendResponse = await communicationService.SendAsync(individualRequest, httpContext: null, ct);

                results.Add(new BulkSendResult
                {
                    RecipientEmail = recipient.To,
                    Status = "sent",
                    CommunicationId = sendResponse.CommunicationId
                });

                logger.LogDebug(
                    "Bulk send {Index}/{Total} succeeded | Recipient: {Recipient}",
                    i + 1, request.Recipients.Length, recipient.To);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new BulkSendResult
                {
                    RecipientEmail = recipient.To,
                    Status = "failed",
                    Error = ex.Message
                });

                logger.LogWarning(
                    ex,
                    "Bulk send {Index}/{Total} failed | Recipient: {Recipient}",
                    i + 1, request.Recipients.Length, recipient.To);
            }

            // Graph API rate awareness: delay between sends (skip after last)
            if (i < request.Recipients.Length - 1)
            {
                await Task.Delay(InterSendDelayMs, ct);
            }
        }

        var succeeded = results.Count(r => r.Status == "sent");
        var failed = results.Count(r => r.Status == "failed");

        var bulkResponse = new BulkSendResponse
        {
            TotalRecipients = request.Recipients.Length,
            Succeeded = succeeded,
            Failed = failed,
            Results = results.ToArray()
        };

        logger.LogInformation(
            "Bulk send completed | Total: {Total}, Succeeded: {Succeeded}, Failed: {Failed}",
            bulkResponse.TotalRecipients, succeeded, failed);

        // 200 if all succeeded, 207 Multi-Status if partial success/failure
        if (failed == 0)
        {
            return TypedResults.Ok(bulkResponse);
        }

        return Results.Json(bulkResponse, statusCode: 207);
    }

    private static async Task<IResult> GetCommunicationStatusAsync(
        Guid id,
        IDataverseService dataverseService,
        ILogger<CommunicationService> logger,
        CancellationToken ct)
    {
        Entity entity;
        try
        {
            entity = await dataverseService.RetrieveAsync(
                "sprk_communication",
                id,
                new[] { "statuscode", "sprk_graphmessageid", "sprk_sentat", "sprk_from" },
                ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Communication {CommunicationId} not found in Dataverse", id);
            throw new SdapProblemException(
                code: "COMMUNICATION_NOT_FOUND",
                title: "Communication not found",
                detail: $"Communication with ID '{id}' does not exist.",
                statusCode: 404);
        }

        var statusCodeValue = entity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1;
        var status = (CommunicationStatus)statusCodeValue;

        var sentAtDateTime = entity.GetAttributeValue<DateTime?>("sprk_sentat");
        DateTimeOffset? sentAt = sentAtDateTime.HasValue
            ? new DateTimeOffset(sentAtDateTime.Value, TimeSpan.Zero)
            : null;

        var response = new CommunicationStatusResponse
        {
            CommunicationId = id,
            Status = status,
            GraphMessageId = entity.GetAttributeValue<string>("sprk_graphmessageid"),
            SentAt = sentAt,
            From = entity.GetAttributeValue<string>("sprk_from")
        };

        return TypedResults.Ok(response);
    }

    private static async Task<IResult> VerifyCommunicationAccountAsync(
        Guid id,
        MailboxVerificationService verificationService,
        CancellationToken ct)
    {
        var result = await verificationService.VerifyAsync(id, ct);

        if (result is null)
        {
            throw new SdapProblemException(
                code: "ACCOUNT_NOT_FOUND",
                title: "Communication account not found",
                detail: $"Communication account with ID '{id}' does not exist.",
                statusCode: 404);
        }

        return TypedResults.Ok(result);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Incoming Webhook Handler (Graph Change Notifications)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Handle Microsoft Graph change notification webhook.
    /// Two request types:
    ///   1. Subscription validation: Graph sends validationToken query parameter during subscription creation.
    ///      Must return 200 OK with the token as text/plain.
    ///   2. Change notification: Graph sends a JSON body with notification array.
    ///      Must validate clientState, enqueue jobs, and return 202 Accepted quickly.
    /// </summary>
    private static async Task<IResult> HandleIncomingWebhookAsync(
        HttpRequest request,
        JobSubmissionService jobSubmissionService,
        IConfiguration configuration,
        ILogger<CommunicationService> logger,
        CancellationToken ct)
    {
        var traceId = request.HttpContext.TraceIdentifier;
        var correlationId = Guid.NewGuid().ToString();

        try
        {
            // ─── Step 1: Handle Graph subscription validation ───
            // When creating a subscription, Graph POSTs with ?validationToken=<token>
            // and expects 200 OK with the token echoed back as text/plain.
            if (request.Query.TryGetValue("validationToken", out var validationToken)
                && !string.IsNullOrEmpty(validationToken))
            {
                logger.LogInformation(
                    "Received Graph subscription validation request, returning validationToken, " +
                    "TraceId={TraceId}",
                    traceId);

                return Results.Text(validationToken!, "text/plain", statusCode: 200);
            }

            // ─── Step 2: Read notification body ───
            request.EnableBuffering();
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            var requestBody = await reader.ReadToEndAsync(ct);

            if (string.IsNullOrWhiteSpace(requestBody))
            {
                logger.LogWarning("Empty webhook payload received, TraceId={TraceId}", traceId);
                return Results.Problem(
                    title: "Invalid Payload",
                    detail: "Webhook payload is empty",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            // ─── Step 3: Parse notifications ───
            GraphChangeNotificationCollection? notifications;
            try
            {
                notifications = JsonSerializer.Deserialize<GraphChangeNotificationCollection>(requestBody);
            }
            catch (JsonException ex)
            {
                logger.LogError(ex,
                    "Failed to parse Graph notification payload, TraceId={TraceId}", traceId);
                return Results.Problem(
                    title: "Invalid Payload",
                    detail: $"Failed to parse notification payload: {ex.Message}",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (notifications?.Value is not { Length: > 0 })
            {
                logger.LogWarning(
                    "Webhook payload contains no notifications, TraceId={TraceId}", traceId);
                return Results.Problem(
                    title: "Invalid Payload",
                    detail: "Notification payload contains no notifications",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            // ─── Step 4: Validate clientState on each notification ───
            var expectedClientState = configuration["Communication:WebhookClientState"];

            if (string.IsNullOrEmpty(expectedClientState))
            {
                logger.LogWarning(
                    "Communication:WebhookClientState not configured - " +
                    "skipping clientState validation (DEVELOPMENT MODE), TraceId={TraceId}",
                    traceId);
            }

            var enqueued = 0;

            foreach (var notification in notifications.Value)
            {
                // Validate clientState if configured
                if (!string.IsNullOrEmpty(expectedClientState)
                    && !string.Equals(notification.ClientState, expectedClientState, StringComparison.Ordinal))
                {
                    logger.LogWarning(
                        "Invalid clientState on notification for subscription {SubscriptionId}, " +
                        "rejecting, TraceId={TraceId}",
                        notification.SubscriptionId, traceId);

                    // Per Graph webhook spec: if ANY notification in the batch has invalid
                    // clientState, reject the entire batch.
                    return Results.Problem(
                        title: "Unauthorized",
                        detail: "Invalid clientState in notification",
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                // ─── Step 5: Deduplication ───
                // Build a dedup key from subscriptionId + resource + changeType to catch retries
                var dedupKey = $"{notification.SubscriptionId}:{notification.Resource}:{notification.ResourceData?.Id}";

                // Prune expired entries periodically (every time we process a batch)
                PruneExpiredNotifications();

                if (!_recentNotifications.TryAdd(dedupKey, DateTimeOffset.UtcNow))
                {
                    logger.LogDebug(
                        "Duplicate notification skipped | SubscriptionId={SubscriptionId}, " +
                        "Resource={Resource}, DedupKey={DedupKey}",
                        notification.SubscriptionId, notification.Resource, dedupKey);
                    continue;
                }

                // ─── Step 6: Extract mailbox and messageId from resource path ───
                // Resource format: "users/{mailbox}/mailFolders/{folder}/messages/{messageId}"
                //               or "users/{mailbox}/messages/{messageId}"
                var resource = notification.Resource ?? string.Empty;
                var messageId = notification.ResourceData?.Id ?? ExtractLastSegment(resource);

                logger.LogInformation(
                    "Processing Graph notification | SubscriptionId={SubscriptionId}, " +
                    "ChangeType={ChangeType}, Resource={Resource}, MessageId={MessageId}, " +
                    "CorrelationId={CorrelationId}",
                    notification.SubscriptionId, notification.ChangeType,
                    resource, messageId, correlationId);

                // ─── Step 7: Enqueue IncomingCommunicationJob ───
                var jobPayload = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    SubscriptionId = notification.SubscriptionId,
                    Resource = resource,
                    MessageId = messageId,
                    ChangeType = notification.ChangeType,
                    TenantId = notification.TenantId,
                    TriggerSource = "GraphWebhook"
                }));

                var job = new JobContract
                {
                    JobType = JobTypeIncomingCommunication,
                    SubjectId = messageId ?? notification.SubscriptionId ?? "unknown",
                    CorrelationId = correlationId,
                    IdempotencyKey = $"IncomingComm:{notification.SubscriptionId}:{messageId}",
                    Payload = jobPayload,
                    MaxAttempts = 3
                };

                await jobSubmissionService.SubmitJobAsync(job, ct);
                enqueued++;

                logger.LogInformation(
                    "Enqueued IncomingCommunicationJob {JobId} | SubscriptionId={SubscriptionId}, " +
                    "MessageId={MessageId}, IdempotencyKey={IdempotencyKey}",
                    job.JobId, notification.SubscriptionId, messageId, job.IdempotencyKey);
            }

            // ─── Step 8: Return 202 Accepted quickly (Graph requires fast response) ───
            logger.LogInformation(
                "Webhook processed: {Total} notifications received, {Enqueued} enqueued, " +
                "CorrelationId={CorrelationId}",
                notifications.Value.Length, enqueued, correlationId);

            return Results.Accepted(
                value: new IncomingWebhookResponse
                {
                    Accepted = true,
                    NotificationsReceived = notifications.Value.Length,
                    NotificationsEnqueued = enqueued,
                    CorrelationId = correlationId
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error processing incoming webhook, TraceId={TraceId}", traceId);
            return Results.Problem(
                title: "Internal Server Error",
                detail: "An unexpected error occurred processing the webhook",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
        }
    }

    /// <summary>
    /// Extracts the last path segment from a Graph resource path.
    /// E.g., "users/user@domain.com/mailFolders/Inbox/messages/AAMkAGI2" -> "AAMkAGI2"
    /// </summary>
    private static string? ExtractLastSegment(string resourcePath)
    {
        if (string.IsNullOrEmpty(resourcePath))
            return null;

        var lastSlash = resourcePath.LastIndexOf('/');
        return lastSlash >= 0 && lastSlash < resourcePath.Length - 1
            ? resourcePath[(lastSlash + 1)..]
            : null;
    }

    /// <summary>
    /// Removes expired entries from the notification deduplication cache.
    /// Called during webhook processing to prevent unbounded memory growth.
    /// </summary>
    private static void PruneExpiredNotifications()
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(DeduplicationWindow);

        foreach (var kvp in _recentNotifications)
        {
            if (kvp.Value < cutoff)
            {
                _recentNotifications.TryRemove(kvp.Key, out _);
            }
        }
    }
}
