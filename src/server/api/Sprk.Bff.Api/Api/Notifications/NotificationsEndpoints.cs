using Sprk.Bff.Api.Infrastructure.Authentication;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Services.Notifications;
using Sprk.Bff.Api.Services.Notifications.Envelopes;

namespace Sprk.Bff.Api.Api.Notifications;

/// <summary>
/// Notification-spine Layer-C endpoints (spec FR-04) plus the kind-generic pending/poll fallback
/// (spec FR-06 / NFR-04, ADR-032 degrade path, task 022).
/// POST /api/notifications/negotiate — authenticated; issues Azure SignalR connection info scoped to
/// the CALLING user's own oid only.
/// GET /api/notifications/pending — authenticated; returns the CALLING user's pending outbox rows
/// (task 012) in envelope shape (task 013), regardless of SignalR state.
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

        group.MapGet("/pending", GetPendingAsync)
            .WithName("NotificationsPending")
            .WithDescription("Kind-generic pending/poll fallback (spec FR-06/NFR-04, ADR-032 degrade path): returns the calling user's pending outbox rows (task 012) in envelope shape (task 013). Behavior does NOT branch on SignalR state — SignalR-agnostic by design. Optional ?kind= query filter (e.g. 'communication-arrived'); defaults to all active kinds. Derives identity server-side from the JWT; accepts no target-user parameter.")
            .Produces<NotificationsPendingResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden);

        group.MapPost("/{outboxRowId:guid}/dismiss", DismissAsync)
            .WithName("NotificationsDismiss")
            .WithDescription("Dismiss one of the CALLING user's pending outbox rows (stamps sprk_dismissed via OutboxService.DismissAsync so it no longer appears in /pending). Ownership is enforced server-side: the row must be in the caller's own owner-scoped pending set, so a caller can NEVER dismiss another user's notification (ADR-028, no cross-user writes). A row not owned by the caller (or already dismissed / expired / non-existent) returns 404 — indistinguishable from not-found, so no cross-user existence is disclosed. Derives identity from the JWT; accepts no target-user parameter.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

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
        var oid = CallerResolution.ResolveObjectId(context.User);

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

    /// <summary>
    /// Kind-generic pending/poll fallback handler (task 022, spec FR-06/NFR-04). This IS the
    /// ADR-032 degrade path — it does not branch on whether SignalR/task 020 is enabled or reachable;
    /// it always serves whatever is currently pending in the task-012 outbox for the caller. Marked
    /// <c>internal</c> (not <c>private</c>) so the task-022 seam test
    /// (<c>tests/integration/seam/Notifications/PendingPollFallbackSeamTests.cs</c>) can invoke this
    /// PRODUCTION handler directly per <c>InternalsVisibleTo("Sprk.Bff.Api.Tests")</c> — the same
    /// "exercise production types, double only the transport/data boundary" seam-test doubling
    /// convention already used by <c>OutboxServiceSeamTests</c> / <c>SignalRDeliverySeamTests</c>
    /// (ADR-038 vertical-slice-seam category).
    /// </summary>
    internal static async Task<IResult> GetPendingAsync(
        HttpContext context,
        OutboxService outboxService,
        ISystemUserIdentityResolver identityResolver,
        string? kind,
        CancellationToken ct)
    {
        // Derive the caller's identity SERVER-SIDE — the oid claim of the validated JWT ONLY. Mirrors
        // NegotiateAsync above. MUST NOT accept a target userId/oid parameter (no cross-user reads,
        // ADR-028) — there is no such parameter on this handler's signature.
        var oid = CallerResolution.ResolveObjectId(context.User);

        if (string.IsNullOrWhiteSpace(oid))
        {
            throw new SdapProblemException(
                code: "OID_NOT_RESOLVED",
                title: "Identity Not Resolved",
                detail: "The caller's oid claim could not be resolved from the token; cannot resolve pending notifications.",
                statusCode: 403);
        }

        NotificationKind? kindFilter = null;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            try
            {
                kindFilter = JsonSerializer.Deserialize<NotificationKind>($"\"{kind}\"");
            }
            catch (JsonException)
            {
                return Results.Problem(
                    detail: $"Unrecognized 'kind' query value '{kind}'. See NotificationKind for the closed taxonomy.",
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid Kind Filter");
            }
        }

        // oid -> Dataverse systemuserid (the outbox row-owner key). A caller with no Dataverse user
        // mapping has no possible outbox rows — 200 empty, NOT an error (spec: null resolution is not
        // a failure state).
        var systemUserId = await identityResolver.ResolveSystemUserIdAsync(oid, ct);
        if (systemUserId is null)
        {
            return TypedResults.Ok(new NotificationsPendingResponse
            {
                Items = Array.Empty<PendingNotificationItem>()
            });
        }

        // SignalR-agnostic: this call never inspects/branches on SignalR delivery state (task 020) —
        // it always reflects the durable outbox's current pending set for the caller (ADR-032).
        var pending = await outboxService.GetPendingAsync(systemUserId.Value, ct);

        var items = pending
            .Where(row => kindFilter is null || row.Kind == kindFilter)
            .Select(ToPendingItem)
            .ToList();

        return TypedResults.Ok(new NotificationsPendingResponse { Items = items });
    }

    /// <summary>
    /// Dismiss handler (UAT 2026-07-22). Stamps <c>sprk_dismissed</c> on ONE of the caller's own
    /// pending outbox rows so it no longer appears in <see cref="GetPendingAsync"/>. Ownership is
    /// enforced by intersecting the requested id with the caller's OWNER-SCOPED pending set
    /// (<see cref="OutboxService.GetPendingAsync"/>) before dismissing — a caller can never dismiss a
    /// row they do not own (ADR-028: no cross-user writes). A miss (not owned / already dismissed /
    /// expired / non-existent) returns 404, disclosing no cross-user existence. Marked
    /// <c>internal</c> for the seam test, mirroring <see cref="GetPendingAsync"/>.
    /// </summary>
    internal static async Task<IResult> DismissAsync(
        Guid outboxRowId,
        HttpContext context,
        OutboxService outboxService,
        ISystemUserIdentityResolver identityResolver,
        CancellationToken ct)
    {
        // Derive the caller's identity SERVER-SIDE — the oid claim of the validated JWT ONLY (mirrors
        // GetPendingAsync). No target-user parameter exists on this signature (ADR-028).
        var oid = CallerResolution.ResolveObjectId(context.User);

        if (string.IsNullOrWhiteSpace(oid))
        {
            throw new SdapProblemException(
                code: "OID_NOT_RESOLVED",
                title: "Identity Not Resolved",
                detail: "The caller's oid claim could not be resolved from the token; cannot dismiss a notification.",
                statusCode: 403);
        }

        var systemUserId = await identityResolver.ResolveSystemUserIdAsync(oid, ct);
        if (systemUserId is null)
        {
            // No Dataverse user mapping ⇒ the caller owns no rows ⇒ nothing to dismiss.
            return TypedResults.NotFound();
        }

        // Ownership gate: the row MUST be in the caller's own owner-scoped pending set. Reusing
        // GetPendingAsync (owner + undismissed + unexpired) means a caller can only ever dismiss a row
        // they own — no separate ownership column read, no cross-user write path.
        var pending = await outboxService.GetPendingAsync(systemUserId.Value, ct);
        if (!pending.Any(row => row.Id == outboxRowId))
        {
            return TypedResults.NotFound();
        }

        await outboxService.DismissAsync(outboxRowId, ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Projects an <see cref="OutboxNotification"/> row to the wire shape: the outbox row id (so a
    /// future dismiss call can target it) plus the task-013 envelope EXACTLY as the producer wrote
    /// it. The envelope is parsed to a raw <see cref="JsonElement"/> rather than re-typed per kind
    /// here, keeping this endpoint kind-generic. NFR-02/NFR-03 (no body/privileged content/action
    /// token) is enforced at envelope-CONSTRUCTION time by <see cref="CommunicationEnvelope.Validate"/>
    /// / <see cref="SuggestionEnvelope.Validate"/> — this projection carries that shape through
    /// unchanged, it does not re-derive or loosen it.
    /// </summary>
    private static PendingNotificationItem ToPendingItem(OutboxNotification row)
    {
        using var envelopeDoc = JsonDocument.Parse(row.EnvelopeJson);
        return new PendingNotificationItem
        {
            OutboxRowId = row.Id,
            Kind = row.Kind,
            Envelope = envelopeDoc.RootElement.Clone()
        };
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

/// <summary>
/// Response for GET /api/notifications/pending — the calling user's pending outbox rows (task 012),
/// kind-generic (spec FR-06). Every active <see cref="NotificationKind"/> appears in the same
/// response shape; the client discriminates via <see cref="PendingNotificationItem.Kind"/>.
/// </summary>
public sealed record NotificationsPendingResponse
{
    /// <summary>
    /// The caller's pending rows. Empty (NOT an error) both when the caller has no Dataverse user
    /// mapping and when the caller genuinely has zero pending rows.
    /// </summary>
    public required IReadOnlyList<PendingNotificationItem> Items { get; init; }
}

/// <summary>
/// A single pending row: the outbox row id (so a future dismiss call — task 012's
/// <see cref="OutboxService.DismissAsync"/> — can target it) plus the task-013 envelope exactly as
/// the producer wrote it. Carries IDs + minimal display metadata only — never a message body,
/// privileged content, or a pre-authorized action token (NFR-02/NFR-03).
/// </summary>
public sealed record PendingNotificationItem
{
    /// <summary>The <c>sprk_notificationoutboxid</c>.</summary>
    public required Guid OutboxRowId { get; init; }

    /// <summary>Discriminator — which envelope shape <see cref="Envelope"/> carries.</summary>
    public required NotificationKind Kind { get; init; }

    /// <summary>
    /// The envelope exactly as the producer wrote it (task-013 <see cref="CommunicationEnvelope"/> /
    /// <see cref="SuggestionEnvelope"/> shape). Parsed to a raw JSON element rather than re-typed
    /// here, so this endpoint stays kind-generic without a switch over every envelope type future
    /// kinds add.
    /// </summary>
    public required JsonElement Envelope { get; init; }
}
