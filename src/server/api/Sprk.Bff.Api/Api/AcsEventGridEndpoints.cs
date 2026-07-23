using System.Text;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Services.Communication.Acs;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Public webhook ingress for Azure Event Grid deliveries of ACS chat events
/// (messaging-communication-app-r1 task 030 / FR-02).
///
/// <para>POST /api/communications/acs/eventgrid — receives the ACS system-topic subscription deliveries. It
/// answers the Event Grid <b>subscription-validation handshake</b> synchronously (echo <c>validationCode</c>)
/// and, for validated chat events, enqueues an <c>IncomingMessaging</c> job onto the existing
/// <c>Services/Jobs/</c> Service Bus contract. All authenticity + parsing logic lives in
/// <see cref="AcsEventGridIngressService"/> (unit-testable without HTTP); this endpoint only reads the body +
/// <c>?sig=</c> secret and maps the outcome to the wire response.</para>
///
/// <para><b>Security boundary (task 030):</b> the endpoint is <c>AllowAnonymous</c> (Event Grid presents no
/// bearer token) but is NOT unauthenticated — the ingress service enforces the subscription-validation handshake,
/// a fail-closed topic-origin allow-list, and an optional shared-secret query parameter. An unvalidated,
/// wrong-topic, or malformed delivery never enqueues a job. Rate-limited via the shared <c>webhook-graph</c>
/// policy (per-source-IP) as defense in depth. Errors return ADR-019 ProblemDetails.</para>
/// </summary>
public static class AcsEventGridEndpoints
{
    public static IEndpointRouteBuilder MapAcsEventGridEndpoints(this IEndpointRouteBuilder app)
    {
        // Registered on `app` (not the authorized /api/communications group) so it stays AllowAnonymous —
        // Event Grid cannot present an OAuth token. Authenticity is enforced inside the ingress service.
        app.MapPost("/api/communications/acs/eventgrid", HandleAcsEventGridAsync)
            .AllowAnonymous()
            .RequireRateLimiting("webhook-graph")
            .WithName("AcsEventGridIngress")
            .WithTags("Communications")
            .WithDescription(
                "Azure Event Grid ingress for ACS chat events: answers the subscription-validation handshake and " +
                "enqueues a Service Bus job per validated chat event (task 030).")
            .Produces(StatusCodes.Status200OK)                                       // subscription-validation handshake
            .Produces<AcsEventGridIngressResponse>(StatusCodes.Status202Accepted)    // events enqueued
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)               // malformed payload
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)             // untrusted / bad secret
            .Produces<ProblemDetails>(StatusCodes.Status429TooManyRequests)
            .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task<IResult> HandleAcsEventGridAsync(
        HttpRequest request,
        AcsEventGridIngressService ingressService,
        ILogger<AcsEventGridIngressService> logger,
        CancellationToken ct)
    {
        var traceId = request.HttpContext.TraceIdentifier;

        try
        {
            using var reader = new StreamReader(request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(ct);

            var providedSecret = request.Query.TryGetValue("sig", out var sig) ? sig.ToString() : null;

            var outcome = await ingressService.ProcessAsync(body, providedSecret, ct);

            return outcome.Action switch
            {
                // Event Grid expects HTTP 200 with { "validationResponse": "<code>" } to activate the subscription.
                AcsEventGridIngressAction.ValidationHandshake =>
                    Results.Ok(new AcsEventGridValidationResponse { ValidationResponse = outcome.ValidationCode! }),

                AcsEventGridIngressAction.Accepted =>
                    Results.Accepted(value: new AcsEventGridIngressResponse
                    {
                        Accepted = true,
                        EventsReceived = outcome.ReceivedCount,
                        EventsEnqueued = outcome.EnqueuedCount,
                        CorrelationId = outcome.CorrelationId
                    }),

                AcsEventGridIngressAction.Rejected =>
                    Results.Problem(title: "Unauthorized", detail: outcome.Detail,
                        statusCode: StatusCodes.Status401Unauthorized),

                AcsEventGridIngressAction.BadRequest =>
                    Results.Problem(title: "Invalid Payload", detail: outcome.Detail,
                        statusCode: StatusCodes.Status400BadRequest),

                AcsEventGridIngressAction.Misconfigured =>
                    Results.Problem(title: "Server Misconfigured", detail: outcome.Detail,
                        statusCode: StatusCodes.Status500InternalServerError),

                _ => Results.Problem(title: "Internal Server Error",
                        statusCode: StatusCodes.Status500InternalServerError)
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing ACS Event Grid ingress, TraceId={TraceId}", traceId);
            return Results.Problem(
                title: "Internal Server Error",
                detail: "An unexpected error occurred processing the Event Grid delivery.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
        }
    }
}

/// <summary>Event Grid subscription-validation handshake response body: <c>{ "validationResponse": "&lt;code&gt;" }</c>.</summary>
public sealed class AcsEventGridValidationResponse
{
    public required string ValidationResponse { get; init; }
}

/// <summary>202 response body for an accepted ACS Event Grid batch.</summary>
public sealed class AcsEventGridIngressResponse
{
    public bool Accepted { get; init; }
    public int EventsReceived { get; init; }
    public int EventsEnqueued { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
}
