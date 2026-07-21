using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Notifications;

namespace Sprk.Bff.Api.Api.Notifications;

/// <summary>
/// Notification-spine Layer-C endpoints (spec FR-04).
/// POST /api/notifications/negotiate — authenticated; issues Azure SignalR connection info scoped to
/// the CALLING user's own oid only.
/// </summary>
/// <remarks>
/// <para>
/// On the SERVER this is a normal authenticated Minimal API endpoint (ADR-028): the enumerated
/// ADR-028 raw-fetch exception applies only on the CLIENT side (task 021, the <c>// Auth v2 (D-AUTH-7):</c>
/// negotiate fetch) — no exception applies here.
/// </para>
/// <para>
/// The endpoint derives the target user's identity SERVER-SIDE from the validated JWT's <c>oid</c>
/// claim and returns connection info scoped to THAT user only. It accepts NO request body and NO
/// query parameter — so there is no target userId/oid a caller could supply, and any body a caller
/// sends is ignored: a spoofed "target user" field cannot exist because nothing binds it (spec
/// acceptance criterion — a spoofed target is ignored server-side).
/// </para>
/// </remarks>
public static class NotificationsEndpoints
{
    public static IEndpointRouteBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications")
            .RequireAuthorization()
            .WithTags("Notifications");

        group.MapPost("/negotiate", NegotiateAsync)
            .WithName("NotificationsNegotiate")
            .WithDescription("Issue Azure SignalR connection info (client access URL + token) scoped to the calling user's own oid. Derives identity server-side from the JWT; accepts no target-user parameter.")
            .Produces<NotificationsNegotiateResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    /// <summary>
    /// Negotiate handler. Resolves the caller's oid from the validated JWT (never client-supplied),
    /// asks the delivery service for connection info scoped to that oid, and returns it. When SignalR
    /// is disabled (Null-Object), the service throws <see cref="FeatureDisabledException"/> and the
    /// caller gets a 503 so it falls back to the poll endpoint (FR-06).
    /// </summary>
    private static async Task<IResult> NegotiateAsync(
        SignalRDeliveryService deliveryService,
        HttpContext context,
        CancellationToken ct)
    {
        // Derive the target identity SERVER-SIDE — the oid claim of the validated JWT ONLY.
        var oid = context.User.FindFirst("oid")?.Value
            ?? context.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

        if (string.IsNullOrWhiteSpace(oid))
        {
            throw new SdapProblemException(
                code: "OID_NOT_RESOLVED",
                title: "Identity Not Resolved",
                detail: "The caller's oid claim could not be resolved from the token; cannot issue a scoped SignalR connection.",
                statusCode: 403);
        }

        try
        {
            var info = await deliveryService.NegotiateAsync(oid, ct);
            return TypedResults.Ok(new NotificationsNegotiateResponse
            {
                Url = info.Url,
                AccessToken = info.AccessToken
            });
        }
        catch (FeatureDisabledException ex)
        {
            return ex.AsFeatureDisabled503();
        }
    }
}

/// <summary>
/// Response for POST /api/notifications/negotiate — the client access URL + short-lived access token
/// the client uses to open its SignalR connection. Scoped server-side to the calling user's oid.
/// </summary>
public sealed record NotificationsNegotiateResponse
{
    /// <summary>Azure SignalR client access URL.</summary>
    public required string Url { get; init; }

    /// <summary>Short-lived client access token minted by the Management SDK, scoped to the caller's oid.</summary>
    public required string AccessToken { get; init; }
}
