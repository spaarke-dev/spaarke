using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Compose;
using static Sprk.Bff.Api.Api.ComposeEndpoints;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Compose <b>SPE change-detection</b> routes: the unauthenticated Graph notification receiver
/// (<c>POST /api/compose/webhooks/spe-doc-changed</c>) and its authenticated poll fallback
/// (<c>POST /document/{documentSpeId}/check-changes</c>).
///
/// <para><b>Reason to change</b>: the Microsoft Graph change-notification contract and the
/// subscription/etag substrate behind it — including the only fail-closed UNAUTHENTICATED
/// surface in Compose (HMAC signature filter + constant-time clientState), whose security posture
/// must be reviewable in one place.</para>
/// </summary>
internal static class ComposeSyncEndpoints
{
    /// <summary>Maps this cluster's routes onto the shared <c>/api/compose</c> group.</summary>
    internal static RouteGroupBuilder MapComposeSyncEndpoints(this RouteGroupBuilder group, IEndpointRouteBuilder routes)
    {
        // (11) POST /api/compose/webhooks/spe-doc-changed — FR-26 (task 053): the Graph
        // change-notification receiver for the SPE subscription task 052's SpeSyncOrchestrator
        // creates on `drives/{driveId}/root`. Registered on `routes` (NOT the authenticated
        // `group` above) because Graph's subscription-validation handshake + notification
        // delivery are unauthenticated by Graph's own contract (AllowAnonymous). Mirrors the
        // existing Communication Graph webhook receiver's two-layer defense exactly (task 044 /
        // CommunicationEndpoints.HandleIncomingWebhookAsync):
        //   1. WebhookSignatureFilter validates X-Hub-Signature-256 (HMAC-SHA256 over the raw
        //      body) using Compose:Webhook:SigningKey; the validationToken handshake probe
        //      bypasses HMAC (Graph does not sign that probe) via the filter's built-in check.
        //   2. The handler validates the body-level clientState in constant time against
        //      Compose:Webhook:ClientState — the SAME config key SpeSyncOrchestrator already
        //      reads when creating the subscription, so what we verify here is exactly what we
        //      told Graph to echo back.
        // Both checks are mandatory; there is no DEVELOPMENT_MODE bypass (fail-closed).
        routes.MapPost("/api/compose/webhooks/spe-doc-changed", HandleSpeDocChangedWebhookAsync)
            .AllowAnonymous()
            .RequireWebhookSignature(
                signatureHeader: WebhookSignatureFilter.DefaultSignatureHeader,
                signingKeyAccessor: sp => sp.GetRequiredService<IConfiguration>()["Compose:Webhook:SigningKey"],
                filterName: "Compose")
            .RequireRateLimiting("webhook-graph")
            .WithName("ComposeSpeDocChangedWebhook")
            .WithTags("Compose")
            .WithSummary("Receive Microsoft Graph change notifications for SPE document changes (HMAC-signed + clientState-verified)")
            .Produces<SpeDocChangedWebhookResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status500InternalServerError);

        // (12) POST /api/compose/document/{documentSpeId}/check-changes — FR-26 (task 053): the
        // explicit poll fallback for when the webhook is unreliable (Risk R6) or for testing.
        // Under the authenticated `group` (RequireAuthorization already applied above). Drives
        // the SAME etag-comparison substrate as the webhook (SpeSyncOrchestrator.
        // EnumerateChangesAsync) rather than a second ad hoc etag-comparison path, so "stored vs
        // current SPE etag" always means the one Redis-backed comparison task 052 built
        // (ADR-009); no Microsoft.Graph type crosses this endpoint (ADR-007).
        group.MapPost("/document/{documentSpeId}/check-changes", CheckDocumentChangesAsync)
            .WithName("ComposeCheckDocumentChanges")
            .WithSummary("Poll fallback: compare the stored SPE etag vs the current SPE etag for a Compose document (FR-26)")
            .RequireRateLimiting("ai-context")
            .Produces<CheckChangesResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status500InternalServerError);

        return group;
    }

    // FR-26 (task 053): Graph change-notification receiver for the SPE webhook subscription
    // task 052's SpeSyncOrchestrator creates on `drives/{driveId}/root`. Two request shapes:
    //   1. Subscription validation: Graph POSTs ?validationToken=<token>; echo it back as
    //      text/plain within 10s (Graph's hard requirement). WebhookSignatureFilter already lets
    //      this probe through unsigned (Graph does not sign it).
    //   2. Change notification batch: JSON body of notifications; each MUST carry a clientState
    //      matching Compose:Webhook:ClientState (constant-time compare) or the WHOLE batch is
    //      rejected 401 — mirrors CommunicationEndpoints.HandleIncomingWebhookAsync exactly.
    // A verified notification's `resource` (e.g. "drives/{driveId}/root") is reverse-resolved to
    // the tracked containerId (SpeSyncOrchestrator.ResolveContainerIdForDriveIdAsync — Graph
    // notifications never carry the SPE containerId itself), then EnumerateChangesAsync is
    // called to enumerate + persist net changes (the "enqueue/flag re-anchor work" task 054
    // consumes — the persisted Redis delta/etag state IS the queue; no separate job dispatch).
    private static async Task<IResult> HandleSpeDocChangedWebhookAsync(
        HttpRequest request,
        SpeSyncOrchestrator orchestrator,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");
        var traceId = request.HttpContext.TraceIdentifier;

        try
        {
            // ─── Step 1: Graph subscription validation handshake ───
            if (SpeWebhookNotificationVerifier.TryGetValidationToken(request.Query, out var validationToken))
            {
                logger.LogInformation(
                    "Compose webhook: Graph subscription validation request, echoing token. TraceId={TraceId}",
                    traceId);
                return Results.Text(validationToken!, "text/plain", statusCode: StatusCodes.Status200OK);
            }

            // ─── Step 2: read + parse the notification body ───
            request.EnableBuffering();
            using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            var requestBody = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(requestBody))
            {
                logger.LogWarning("Compose webhook: empty payload received. TraceId={TraceId}", traceId);
                return BadRequest("Webhook payload is empty.");
            }

            GraphChangeNotificationCollection? notifications;
            try
            {
                notifications = JsonSerializer.Deserialize<GraphChangeNotificationCollection>(requestBody);
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Compose webhook: failed to parse notification payload. TraceId={TraceId}", traceId);
                return BadRequest($"Failed to parse notification payload: {ex.Message}");
            }

            if (notifications?.Value is not { Length: > 0 })
            {
                logger.LogWarning("Compose webhook: payload contains no notifications. TraceId={TraceId}", traceId);
                return BadRequest("Notification payload contains no notifications.");
            }

            // ─── Step 3: verify clientState on every notification (fail-closed, constant-time) ───
            var expectedClientState = configuration["Compose:Webhook:ClientState"];
            if (string.IsNullOrEmpty(expectedClientState))
            {
                logger.LogError(
                    "Compose webhook: Compose:Webhook:ClientState not configured — rejecting batch. TraceId={TraceId}",
                    traceId);
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Server Misconfigured",
                    detail: "Webhook clientState validation is not configured on this server.");
            }

            if (!SpeWebhookNotificationVerifier.VerifyClientState(notifications.Value, expectedClientState, out var invalidNotification))
            {
                logger.LogWarning(
                    "Compose webhook: invalid clientState on notification for subscription {SubscriptionId}. Rejecting batch. TraceId={TraceId}",
                    invalidNotification?.SubscriptionId, traceId);
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized",
                    detail: "Invalid clientState in notification.");
            }

            // ─── Step 4: resolve each notification's driveId -> tracked containerId, enumerate ───
            var processedContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var totalChanges = 0;

            foreach (var notification in notifications.Value)
            {
                var resource = notification.Resource ?? string.Empty;
                if (!SpeWebhookNotificationVerifier.TryExtractDriveIdFromResource(resource, out var driveId) || driveId is null)
                {
                    logger.LogWarning(
                        "Compose webhook: could not extract driveId from resource '{Resource}'. Skipping notification. TraceId={TraceId}",
                        resource, traceId);
                    continue;
                }

                var containerId = await orchestrator.ResolveContainerIdForDriveIdAsync(driveId, ct).ConfigureAwait(false);
                if (containerId is null)
                {
                    logger.LogWarning(
                        "Compose webhook: no tracked container for driveId {DriveId} (resource {Resource}); notification ignored. TraceId={TraceId}",
                        driveId, resource, traceId);
                    continue;
                }

                if (!processedContainers.Add(containerId))
                {
                    // A batch may repeat the same container across multiple changed items;
                    // EnumerateChangesAsync already enumerates the whole container in one call.
                    continue;
                }

                var netChanges = await orchestrator.EnumerateChangesAsync(containerId, ct).ConfigureAwait(false);
                totalChanges += netChanges.Count;

                logger.LogInformation(
                    "Compose webhook: container {ContainerId} drive {DriveId} — {Count} net changes enumerated (re-anchor work flagged for task 054). TraceId={TraceId}",
                    containerId, driveId, netChanges.Count, traceId);
            }

            // ─── Step 5: fast 202 Accepted (Graph requires a quick response) ───
            return Results.Accepted(value: new SpeDocChangedWebhookResponse(
                NotificationsReceived: notifications.Value.Length,
                ContainersProcessed: processedContainers.Count,
                ChangesDetected: totalChanges,
                CorrelationId: traceId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Compose webhook: unexpected failure. TraceId={TraceId}", traceId);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while processing the webhook.");
        }
    }

    // FR-26 (task 053): explicit poll fallback comparing the stored SPE etag vs the current SPE
    // etag for a single document. Delegates to SpeSyncOrchestrator.EnumerateChangesAsync — the
    // SAME Redis-backed delta/etag substrate the webhook receiver drives (task 052) — rather than
    // a second etag-comparison mechanism, so "poll" and "webhook" always agree on what "changed"
    // means. No Microsoft.Graph type crosses this endpoint (ADR-007); etag state is Redis via the
    // orchestrator (ADR-009).
    private static async Task<IResult> CheckDocumentChangesAsync(
        string documentSpeId,
        [FromBody] CheckChangesBody? body,
        SpeSyncOrchestrator orchestrator,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (string.IsNullOrWhiteSpace(documentSpeId)) return BadRequest("documentSpeId is required.");
        if (body is null) return BadRequest("Request body is required.");
        if (string.IsNullOrWhiteSpace(body.ContainerId)) return BadRequest("containerId is required in the request body.");

        try
        {
            var netChanges = await orchestrator.EnumerateChangesAsync(body.ContainerId, ct).ConfigureAwait(false);
            var match = netChanges.FirstOrDefault(c => string.Equals(c.ItemId, documentSpeId, StringComparison.OrdinalIgnoreCase));
            var changed = match is not null;

            logger.LogInformation(
                "Compose check-changes: container={ContainerId} item={DocumentSpeId} changed={Changed} TraceId={TraceId}",
                body.ContainerId, documentSpeId, changed, httpContext.TraceIdentifier);

            return Results.Ok(new CheckChangesResponse(
                DocumentSpeId: documentSpeId,
                ContainerId: body.ContainerId,
                Changed: changed,
                Deleted: match?.Deleted ?? false,
                ETag: match?.ETag,
                Name: match?.Name,
                CorrelationId: httpContext.TraceIdentifier));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Compose check-changes: unexpected failure for documentSpeId={DocumentSpeId} TraceId={TraceId}",
                documentSpeId, httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while checking for changes.");
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Request / response DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Response shape for <c>POST /api/compose/webhooks/spe-doc-changed</c> (FR-26) — a
/// same-request summary of how many notifications were verified and how many net SPE changes
/// were enumerated across the (deduplicated) containers they resolved to.</summary>
public sealed record SpeDocChangedWebhookResponse(
    [property: JsonPropertyName("notificationsReceived")] int NotificationsReceived,
    [property: JsonPropertyName("containersProcessed")] int ContainersProcessed,
    [property: JsonPropertyName("changesDetected")] int ChangesDetected,
    [property: JsonPropertyName("correlationId")] string CorrelationId);

/// <summary>Request body for <c>POST /api/compose/document/{id}/check-changes</c> (FR-26). The
/// SPE containerId is required to key the Redis-backed <c>SpeSyncOrchestrator</c> state (task
/// 052) — driveId is resolved internally by the orchestrator, so callers do not supply it.</summary>
public sealed record CheckChangesBody(
    [property: JsonPropertyName("containerId")] string ContainerId);

/// <summary>Response shape for <c>POST /api/compose/document/{id}/check-changes</c> (FR-26) —
/// whether the document's SPE etag differs from the last-observed (Redis-stored) etag, plus the
/// changed item's metadata when it does.</summary>
public sealed record CheckChangesResponse(
    [property: JsonPropertyName("documentSpeId")] string DocumentSpeId,
    [property: JsonPropertyName("containerId")] string ContainerId,
    [property: JsonPropertyName("changed")] bool Changed,
    [property: JsonPropertyName("deleted")] bool Deleted,
    [property: JsonPropertyName("eTag")] string? ETag,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("correlationId")] string CorrelationId);
