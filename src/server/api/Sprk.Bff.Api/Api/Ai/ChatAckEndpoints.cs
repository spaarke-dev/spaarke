using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Client-ack endpoint for UI-affecting tool results (D-F3 / FR-A1-08 / task AIR2-037).
/// <c>POST /api/ai/chat/sessions/{sessionId}/ack</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this closes</b>: R2-D — the assistant claiming a UI action ("I opened the tab")
/// with no backing client event. UI-affecting tools (open tab, open Compose, navigation) emit
/// an SSE frame carrying a server-issued frame id and then WAIT
/// (<see cref="IUiActionAckCoordinator.WaitForAckAsync"/>) for the client to call this endpoint
/// referencing that SAME frame id — the tool result completes on ack, or fails honestly on
/// timeout. See <c>SendWorkspaceArtifactHandler.ExecuteOpenWorkspaceTabAsync</c> for the first
/// (and reference) ack-gated tool.
/// </para>
/// <para>
/// <b>Placement Justification (per <c>.claude/constraints/bff-extensions.md</c> + CLAUDE.md
/// §10)</b>: this endpoint belongs in BFF, not a separate surface — it resolves a pending
/// in-process wait (<see cref="IUiActionAckCoordinator"/>) that the SAME BFF instance's tool
/// call registered moments earlier in the SSE stream; there is no cross-service concern to
/// externalize.
/// </para>
/// <para>
/// <b>Auth (ADR-008)</b>: <c>RequireAuthorization()</c> at the route group + resource-level
/// <c>AddAiAuthorizationFilter()</c>, mirroring every other <c>/api/ai/chat/sessions/*</c>
/// endpoint (<see cref="ChatEndpoints"/>, <see cref="SummarizeSessionEndpoint"/>).
/// </para>
/// <para>
/// <b>Always 200</b>: whether or not a pending waiter was found, the endpoint returns 200 with
/// <c>{ acknowledged: bool }</c> — the client fire-and-forgets this call after rendering the
/// frame and does not need to branch on the result (a "no pending waiter" ack is benign: the
/// wait already timed out, or this is a duplicate/late ack).
/// </para>
/// </remarks>
public static class ChatAckEndpoints
{
    /// <summary>
    /// Registers <c>POST /api/ai/chat/sessions/{sessionId}/ack</c>. Called from
    /// <c>EndpointMappingExtensions.MapDomainEndpoints</c> adjacent to
    /// <c>MapChatEndpoints()</c>. ZERO new lines in <c>Program.cs</c> (ADR-010 + R5 §3.3).
    /// </summary>
    public static IEndpointRouteBuilder MapChatAckEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/chat")
            .RequireAuthorization()
            .WithTags("AI Chat");

        group.MapPost("/sessions/{sessionId}/ack", HandleAsync)
            .AddAiAuthorizationFilter()
            // ADR-016 / bff-extensions.md §C: same policy as the sibling /summarize
            // endpoint — sliding-window 60/min/user. Cheap in-process dictionary lookup,
            // but still authenticated user-triggerable traffic; rate-limited per policy.
            .RequireRateLimiting("ai-context")
            .WithName("AckUiAction")
            .WithSummary("Acknowledge a UI-affecting tool's SSE frame (D-F3 / FR-A1-08)")
            .WithDescription(
                "The client calls this AFTER it has actually rendered a UI-affecting frame " +
                "(e.g. opened a workspace tab from a workspace_open_tab context_event), passing " +
                "back the SAME frameId the frame carried. Resolves the tool call's pending ack " +
                "wait so the tool result can complete truthfully instead of on a timeout.")
            .Produces<UiActionAckResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static IResult HandleAsync(
        string sessionId,
        [FromBody] UiActionAckRequest? body,
        IUiActionAckCoordinator ackCoordinator)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Results.Problem(
                detail: "'sessionId' route parameter is required.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request");
        }

        if (body is null || string.IsNullOrWhiteSpace(body.FrameId))
        {
            return Results.Problem(
                detail: "Request body must include a non-empty 'frameId'.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request");
        }

        // Coordinator is keyed by the internal ChatSessionId.ToString("N") form the tool
        // handler used when registering the wait (see SendWorkspaceArtifactHandler). The route
        // sessionId is the SAME session id the client already holds — no reformatting needed
        // as long as both sides use the identical string form; normalize defensively here by
        // stripping dashes so "N" and "D" formatted GUIDs from either side still match.
        var normalizedSessionId = sessionId.Replace("-", string.Empty, StringComparison.Ordinal);

        var acknowledged = ackCoordinator.TryAcknowledge(normalizedSessionId, body.FrameId);
        return Results.Ok(new UiActionAckResponse(acknowledged));
    }
}
