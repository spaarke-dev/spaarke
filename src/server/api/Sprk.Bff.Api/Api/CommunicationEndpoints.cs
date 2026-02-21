using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Communication endpoints for sending emails via Graph API.
/// POST /send: Single email send. POST /send-bulk: Bulk send to multiple recipients.
/// GET /{id}/status: Communication status lookup.
/// </summary>
public static class CommunicationEndpoints
{
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

        return app;
    }

    private static async Task<IResult> SendCommunicationAsync(
        SendCommunicationRequest request,
        CommunicationService communicationService,
        ILogger<CommunicationService> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var response = await communicationService.SendAsync(request, ct);
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
                var sendResponse = await communicationService.SendAsync(individualRequest, ct);

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
}
