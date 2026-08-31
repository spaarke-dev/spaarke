using Sprk.Bff.Api.Services;
using static Sprk.Bff.Api.Api.ComposeEndpoints;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Compose <b>lock / checkout</b> routes: <c>.../checkout</c>, <c>.../checkin</c> (Phase 5 stubs)
/// and <c>POST /document/{documentId}/heartbeat</c>.
///
/// <para><b>Reason to change</b>: the document LOCK model — whether Compose acquires an SPE
/// check-out of its own and how a client keeps one alive. Independent of every content path: it is
/// the only cluster backed by <c>DocumentCheckoutService</c>, and the only one whose behaviour is
/// scheduled to change wholesale when Phase 5 wires real check-out.</para>
/// </summary>
internal static class ComposeCheckoutEndpoints
{
    /// <summary>Maps this cluster's routes onto the shared <c>/api/compose</c> group.</summary>
    internal static RouteGroupBuilder MapComposeCheckoutEndpoints(this RouteGroupBuilder group)
    {
        // (5) POST /api/compose/documents/{documentId}/checkout — Phase 5 stub
        group.MapPost("/documents/{documentId:guid}/checkout", Checkout)
            .WithName("ComposeCheckoutDocument")
            .WithSummary("Phase 5 stub: acquires SPE check-out (use /api/documents/{id}/checkout in R1)")
            .RequireRateLimiting("ai-context")
            .Produces(StatusCodes.Status501NotImplemented)
            .Produces(StatusCodes.Status401Unauthorized);

        // (6) POST /api/compose/documents/{documentId}/checkin — Phase 5 stub
        group.MapPost("/documents/{documentId:guid}/checkin", Checkin)
            .WithName("ComposeCheckinDocument")
            .WithSummary("Phase 5 stub: releases SPE check-out (use /api/documents/{id}/checkin in R1)")
            .RequireRateLimiting("ai-context")
            .Produces(StatusCodes.Status501NotImplemented)
            .Produces(StatusCodes.Status401Unauthorized);

        // (7) POST /api/compose/document/{documentId}/heartbeat — refresh SPE lock heartbeat
        group.MapPost("/document/{documentId:guid}/heartbeat", RefreshHeartbeat)
            .WithName("ComposeRefreshHeartbeat")
            .WithSummary("Refresh the heartbeat timestamp on the caller's active checkout")
            .RequireRateLimiting("ai-context")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        return group;
    }

    private static IResult Checkout(Guid documentId, ILoggerFactory loggerFactory, HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");
        logger.LogInformation(
            "Compose checkout stub called for documentId={DocumentId}. TraceId={TraceId}",
            documentId, httpContext.TraceIdentifier);

        return Results.Problem(
            statusCode: StatusCodes.Status501NotImplemented,
            title: "Not Implemented",
            detail: "Compose check-out is wired in Phase 5. In R1, call " +
                    "POST /api/documents/{documentId}/checkout directly.",
            type: "https://tools.ietf.org/html/rfc7231#section-6.6.2");
    }

    private static IResult Checkin(Guid documentId, ILoggerFactory loggerFactory, HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");
        logger.LogInformation(
            "Compose checkin stub called for documentId={DocumentId}. TraceId={TraceId}",
            documentId, httpContext.TraceIdentifier);

        return Results.Problem(
            statusCode: StatusCodes.Status501NotImplemented,
            title: "Not Implemented",
            detail: "Compose check-in is wired in Phase 5. In R1, call " +
                    "POST /api/documents/{documentId}/checkin directly.",
            type: "https://tools.ietf.org/html/rfc7231#section-6.6.2");
    }

    private static async Task<IResult> RefreshHeartbeat(
        Guid documentId,
        DocumentCheckoutService checkoutService,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (documentId == Guid.Empty) return BadRequest("documentId is required.");

        try
        {
            var refreshed = await checkoutService
                .RefreshHeartbeatAsync(documentId, httpContext.User, ct)
                .ConfigureAwait(false);

            if (refreshed)
            {
                logger.LogDebug(
                    "Compose heartbeat refreshed for documentId={DocumentId} TraceId={TraceId}",
                    documentId, httpContext.TraceIdentifier);
                return Results.NoContent();
            }

            // Doc missing, not checked out, or held by another user — all three collapse to 404.
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "No active checkout to refresh",
                detail: "The document was not found, is not checked out, or the caller does not own the active lock.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex,
                "Compose heartbeat: auth contract violation TraceId={TraceId}",
                httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "Caller's identity could not be resolved from claims.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Compose heartbeat: unexpected failure for documentId={DocumentId} TraceId={TraceId}",
                documentId, httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while refreshing the heartbeat.");
        }
    }
}
