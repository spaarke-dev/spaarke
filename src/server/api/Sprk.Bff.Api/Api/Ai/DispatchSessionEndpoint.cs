using System.Runtime.ExceptionServices;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// The Click entry path's server surface (FR-P1-04, spaarke-ai-architecture-redesign-r1
/// task 023b / ADR-039): <c>POST /api/ai/chat/sessions/{sessionId}/dispatch</c>,
/// body <c>{ bindingId, args }</c> → SSE stream of <see cref="AnalysisChunk"/> events.
/// </summary>
/// <remarks>
/// <para>
/// <b>Contract</b> (consumed by the client's ONE dispatch helper,
/// <c>@spaarke/ui-components services/dispatchConsumer.ts</c> — task 023):
/// <list type="bullet">
///   <item><c>bindingId</c> — the <c>sprk_playbookconsumer</c> row GUID carried by the
///         clicked chip. THE resolution rule: every chip emitter (task 022's Event Rules
///         contextual chips AND the <c>sprk_chiptransitions</c> Binding column) sends the
///         row GUID; a non-GUID value is rejected 400 <c>dispatch.binding-id-invalid</c>;
///         a GUID with no enabled row is rejected 404 <c>dispatch.binding-not-found</c>
///         (ADR-039: the server resolves the Binding; unknown ids get a clean error, no
///         fallback).</item>
///   <item><c>args</c> — the chip's args forwarded VERBATIM (optional). The server-side
///         boundary owns the typed parse; P1 vocabulary: <c>fileIds</c> string array.</item>
///   <item>200 → <c>text/event-stream</c> of AnalysisChunk frames
///         (<c>data: {"type":"delta"|"complete"|"error"|"text",...}\n\n</c>, camelCase)
///         — serialized by the SAME writer as <see cref="SummarizeSessionEndpoint"/> so
///         there is ONE wire shape.</item>
///   <item>non-OK → ProblemDetails with stable <c>errorCode</c> extension (ADR-019).</item>
/// </list>
/// </para>
/// <para>
/// <b>Placement Justification (per <c>.claude/constraints/bff-extensions.md</c> +
/// CLAUDE.md §10)</b>: identical reasoning to the sibling
/// <see cref="SummarizeSessionEndpoint"/> — SSE streaming needs a long-lived server
/// connection; server-side AI execution (catalog resolution + prompted executor +
/// ledger write) cannot be composed in a client; OBO auth resolves per-request in the
/// BFF only; <c>ai-context</c> rate limiting + correlation propagation exist only at
/// this boundary. No new package, no new DI abstraction beyond the orchestrator +
/// one routing-service method extension.
/// </para>
/// <para>
/// <b>Auth + rate-limit (ADR-008 + ADR-016)</b>: <c>RequireAuthorization()</c> at the
/// route group + <c>AddAiAuthorizationFilter()</c> + <c>RequireRateLimiting("ai-context")</c>
/// — mirrors the sibling chat endpoints exactly. Kill-switch semantics (ADR-032):
/// mapped UNCONDITIONALLY; <see cref="SessionDispatchOrchestrator"/> has a Null-Object
/// mirror (<see cref="NullSessionDispatchOrchestrator"/>) registered on the
/// compound-OFF branch which throws <see cref="FeatureDisabledException"/>
/// (<c>ai.dispatch.disabled</c>) at the pre-stream probe → canonical 503.
/// </para>
/// <para>
/// <b>ADR-028</b>: no token snapshot — the orchestrator's dependencies resolve OBO
/// tokens via DI inside the per-request scope (verified: no <c>string</c> token
/// parameter anywhere on the orchestrator surface).
/// </para>
/// </remarks>
public static class DispatchSessionEndpoint
{
    // ─── Stable errorCode extensions (ADR-019) ──────────────────────────────────────
    // CONTRACT strings — the client dispatchConsumer surfaces them via
    // mapDispatchHttpError. Do NOT rename without a corresponding contract update.
    private const string ErrorCodeTenantMissing = "auth.tid-missing";
    private const string ErrorCodeOidMissing = "auth.oid-missing";
    private const string ErrorCodeSessionIdRequired = "sessionId.required";
    private const string ErrorCodeSessionIdInvalid = "sessionId.invalid";
    private const string ErrorCodeBindingRequired = "dispatch.binding-required";
    private const string ErrorCodeBindingIdInvalid = "dispatch.binding-id-invalid";
    private const string ErrorCodeInvalidArgs = "dispatch.invalid-args";
    private const string ErrorCodeSessionNotFound = "dispatch.session-not-found";
    private const string ErrorCodeInternalError = "dispatch.internal-error";

    /// <summary>
    /// Registers <c>POST /api/ai/chat/sessions/{sessionId}/dispatch</c>. Called from
    /// <c>EndpointMappingExtensions.MapDomainEndpoints</c> adjacent to
    /// <c>MapSummarizeSessionEndpoint()</c>. ZERO new lines in <c>Program.cs</c> (ADR-010).
    /// </summary>
    public static IEndpointRouteBuilder MapDispatchSessionEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/chat")
            .RequireAuthorization()
            .RequireRateLimiting("ai-context")
            .WithTags("AI Chat");

        group.MapPost("/sessions/{sessionId}/dispatch", DispatchAsync)
            .AddAiAuthorizationFilter()
            .WithName("DispatchChatSessionBinding")
            .WithSummary("Dispatch a capability Binding by id for a chat session (Click path, FR-P1-04)")
            .WithDescription(
                "The Click entry path: chips carry a binding_id (sprk_playbookconsumer row GUID); the " +
                "server resolves the Binding, executes its prompted Action via ActionRunner + " +
                "PromptSchemaRenderer, writes the SessionOutput ledger entry BEFORE rendering " +
                "(ADR-040), and streams AnalysisChunk SSE events (terminal 'complete' chunk rendered " +
                "from the stored entry). Unknown binding ids are rejected with a clean stable error " +
                "(ADR-039 — no fallback).")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    /// <summary>
    /// Handler for <c>POST /api/ai/chat/sessions/{sessionId}/dispatch</c>. Thin SSE
    /// writer over <see cref="SessionDispatchOrchestrator.DispatchAsync"/> — no routing
    /// logic lives here (ADR-039: the Binding table routes). Probe-then-stream pattern
    /// mirrors <see cref="SummarizeSessionEndpoint"/> (returns <see cref="Task"/>, not
    /// <c>Task&lt;IResult&gt;</c>, for direct SSE response ownership).
    /// </summary>
    private static async Task DispatchAsync(
        string sessionId,
        [FromBody] DispatchSessionRequest? body,
        HttpContext httpContext,
        SessionDispatchOrchestrator orchestrator,
        ILogger<DispatchSessionRequest> logger)
    {
        var cancellationToken = httpContext.RequestAborted;
        var response = httpContext.Response;
        var correlationId = httpContext.TraceIdentifier;

        // ─── Pre-stream validation — ProblemDetails (ADR-019) ──────────────────────────
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            await SummarizeSessionEndpoint.WriteProblemDetailsAsync(
                response, StatusCodes.Status400BadRequest, "Bad Request",
                "'sessionId' route parameter is required.",
                ErrorCodeSessionIdRequired, correlationId, cancellationToken);
            return;
        }

        if (!Guid.TryParse(sessionId, out _))
        {
            await SummarizeSessionEndpoint.WriteProblemDetailsAsync(
                response, StatusCodes.Status400BadRequest, "Bad Request",
                "'sessionId' must be a valid GUID.",
                ErrorCodeSessionIdInvalid, correlationId, cancellationToken);
            return;
        }

        if (body is null || string.IsNullOrWhiteSpace(body.BindingId))
        {
            await SummarizeSessionEndpoint.WriteProblemDetailsAsync(
                response, StatusCodes.Status400BadRequest, "Bad Request",
                "'bindingId' is required in the request body.",
                ErrorCodeBindingRequired, correlationId, cancellationToken);
            return;
        }

        // THE binding-id resolution rule: chips carry the sprk_playbookconsumer row GUID
        // (every emitter — Event Rules contextual chips + sprk_chiptransitions — sends the
        // row id). A non-GUID token is a contract violation, rejected with a stable code
        // (ADR-039: no consumer-type fallback, no second resolution vocabulary).
        if (!Guid.TryParse(body.BindingId, out var bindingId) || bindingId == Guid.Empty)
        {
            await SummarizeSessionEndpoint.WriteProblemDetailsAsync(
                response, StatusCodes.Status400BadRequest, "Bad Request",
                "'bindingId' must be the Binding row GUID carried by the chip.",
                ErrorCodeBindingIdInvalid, correlationId, cancellationToken);
            return;
        }

        // ─── Auth context — tid + oid claims (mirror SummarizeSessionEndpoint) ─────────
        var tenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            await SummarizeSessionEndpoint.WriteProblemDetailsAsync(
                response, StatusCodes.Status401Unauthorized, "Unauthorized",
                "Tenant identity ('tid' claim) not found in authentication token.",
                ErrorCodeTenantMissing, correlationId, cancellationToken);
            return;
        }

        var callerOid = httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(callerOid))
        {
            await SummarizeSessionEndpoint.WriteProblemDetailsAsync(
                response, StatusCodes.Status401Unauthorized, "Unauthorized",
                "User identity ('oid' claim) not found in authentication token.",
                ErrorCodeOidMissing, correlationId, cancellationToken);
            return;
        }

        // FR-P4-05 per-tenant metering scope (task 054): chip clicks are the Click entry
        // path — capability invocations at the dispatch seam and executor token usage
        // observed in OpenAiClient are dimensioned per tenant/user/entry-path=click via
        // the ambient AiMeteringContext (identifiers only, NFR-07).
        using var meteringScope = Sprk.Bff.Api.Telemetry.AiMeteringContext.Begin(
            tenantId, callerOid, Sprk.Bff.Api.Telemetry.AiMeteringContext.EntryPathClick);

        var request = new SessionDispatchRequest(
            TenantId: tenantId,
            SessionId: sessionId,
            BindingId: bindingId,
            Args: body.Args,
            CorrelationId: correlationId);

        // ADR-015: identifiers only — never log args content (chip args may carry
        // maker-authored values; file ids are identifiers, but the raw JSON is not parsed
        // here, so the COUNT-free structural log stays content-safe).
        logger.LogInformation(
            "[DISPATCH-SESSION] Start tenant={TenantId} session={SessionId} oid={Oid} bindingId={BindingId} hasArgs={HasArgs} correlationId={CorrelationId}",
            tenantId, sessionId, callerOid, bindingId, body.Args is not null, correlationId);

        // ─── Probe the first chunk BEFORE setting SSE headers ──────────────────────────
        // Catalog-resolution failures (unknown binding / unsupported kind / unsupported
        // disposition), session-not-found, args-cap violations, and the ADR-032 Null-peer
        // FeatureDisabledException all throw pre-stream and map to ProblemDetails here.
        IAsyncEnumerator<AnalysisChunk>? enumerator = null;
        bool hasFirst = false;
        AnalysisChunk first = default!;
        ExceptionDispatchInfo? earlyFailure = null;

        try
        {
            enumerator = orchestrator.DispatchAsync(request, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            hasFirst = await enumerator.MoveNextAsync().ConfigureAwait(false);
            if (hasFirst)
            {
                first = enumerator.Current;
            }
        }
        catch (OperationCanceledException)
        {
            if (enumerator is not null)
            {
                try { await enumerator.DisposeAsync().ConfigureAwait(false); } catch { /* cleanup-tail */ }
            }
            return;
        }
        catch (Exception ex)
        {
            earlyFailure = ExceptionDispatchInfo.Capture(ex);
        }

        if (earlyFailure is not null)
        {
            if (enumerator is not null)
            {
                try { await enumerator.DisposeAsync().ConfigureAwait(false); } catch { /* cleanup-tail */ }
            }

            switch (earlyFailure.SourceException)
            {
                // FeatureDisabledException FIRST per ADR-032 P3 (it derives from
                // InvalidOperationException — order matters).
                case FeatureDisabledException fde:
                    logger.LogDebug(
                        "[DISPATCH-SESSION] Feature disabled. errorCode={ErrorCode} tenant={TenantId} session={SessionId}",
                        fde.ErrorCode, tenantId, sessionId);
                    await fde.AsFeatureDisabled503().ExecuteAsync(httpContext);
                    return;

                // ADR-039 clean rejections: unknown binding (404), unsupported kind /
                // disposition (422) — stable codes the client surfaces verbatim.
                case DispatchRejectedException rejected:
                    logger.LogInformation(
                        "[DISPATCH-SESSION] Dispatch rejected. errorCode={ErrorCode} status={Status} tenant={TenantId} session={SessionId} bindingId={BindingId}",
                        rejected.ErrorCode, rejected.StatusCode, tenantId, sessionId, bindingId);
                    await SummarizeSessionEndpoint.WriteProblemDetailsAsync(
                        response, rejected.StatusCode,
                        rejected.StatusCode == StatusCodes.Status404NotFound ? "Not Found" : "Unprocessable Entity",
                        rejected.Message,
                        rejected.ErrorCode, correlationId, cancellationToken);
                    return;

                // Session-not-found contract: InvalidOperationException whose message
                // contains "not found" (reserved phrase — see SessionDispatchOrchestrator).
                case InvalidOperationException ioe when ioe.Message.Contains("not found", StringComparison.OrdinalIgnoreCase):
                    logger.LogInformation(
                        "[DISPATCH-SESSION] Session not found tenant={TenantId} session={SessionId}",
                        tenantId, sessionId);
                    await SummarizeSessionEndpoint.WriteProblemDetailsAsync(
                        response, StatusCodes.Status404NotFound, "Not Found",
                        "The chat session was not found.",
                        ErrorCodeSessionNotFound, correlationId, cancellationToken);
                    return;

                // Orchestrator input validation (NFR-02 args cap, empty ids).
                case ArgumentException:
                    logger.LogWarning(earlyFailure.SourceException,
                        "[DISPATCH-SESSION] Dispatch argument validation failed tenant={TenantId} session={SessionId}",
                        tenantId, sessionId);
                    await SummarizeSessionEndpoint.WriteProblemDetailsAsync(
                        response, StatusCodes.Status400BadRequest, "Bad Request",
                        "Dispatch request validation failed.",
                        ErrorCodeInvalidArgs, correlationId, cancellationToken);
                    return;

                // Catch-all 500 (incl. catalog misconfiguration such as an unloadable
                // Action row) — log full detail; never leak to the wire (ADR-019).
                default:
                    logger.LogError(earlyFailure.SourceException,
                        "[DISPATCH-SESSION] Dispatch failed (pre-stream) tenant={TenantId} session={SessionId} bindingId={BindingId}",
                        tenantId, sessionId, bindingId);
                    await SummarizeSessionEndpoint.WriteProblemDetailsAsync(
                        response, StatusCodes.Status500InternalServerError, "Internal Server Error",
                        "Failed to start the dispatch stream. See server logs for details.",
                        ErrorCodeInternalError, correlationId, cancellationToken);
                    return;
            }
        }

        // ─── SSE headers — set BEFORE writing the response body ────────────────────────
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";

        // ─── Stream the (already-pulled) first chunk + remaining chunks — the SHARED
        // AnalysisChunk writer (SummarizeSessionEndpoint.WriteSseChunkAsync) keeps ONE
        // wire serialization for both streams.
        try
        {
            if (hasFirst)
            {
                await SummarizeSessionEndpoint.WriteSseChunkAsync(response, first, cancellationToken);
            }

            while (enumerator is not null)
            {
                bool moveNext;
                try
                {
                    moveNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Mid-stream escape (the orchestrator's per-stage try-catch should
                    // prevent this) — never let the stream die without a terminal marker.
                    logger.LogError(ex,
                        "[DISPATCH-SESSION] Mid-stream exception escaped orchestrator tenant={TenantId} session={SessionId} bindingId={BindingId}",
                        tenantId, sessionId, bindingId);
                    try
                    {
                        await SummarizeSessionEndpoint.WriteSseChunkAsync(response,
                            AnalysisChunk.FromError("The dispatch stream was interrupted."),
                            CancellationToken.None);
                    }
                    catch { /* response may already be unwritable */ }
                    break;
                }

                if (!moveNext) break;
                await SummarizeSessionEndpoint.WriteSseChunkAsync(response, enumerator.Current, cancellationToken);
            }
        }
        finally
        {
            if (enumerator is not null)
            {
                try { await enumerator.DisposeAsync().ConfigureAwait(false); } catch { /* cleanup-tail */ }
            }
        }

        logger.LogInformation(
            "[DISPATCH-SESSION] Stream complete tenant={TenantId} session={SessionId} bindingId={BindingId} correlationId={CorrelationId}",
            tenantId, sessionId, bindingId, correlationId);
    }
}

/// <summary>
/// Request body for <c>POST /api/ai/chat/sessions/{sessionId}/dispatch</c> — the exact
/// shape the client <c>dispatchConsumer(bindingId, args)</c> helper POSTs
/// (<c>{ bindingId: string, args: Record&lt;string, unknown&gt; }</c>).
/// </summary>
/// <param name="BindingId">
/// The <c>sprk_playbookconsumer</c> row GUID from the clicked chip (string on the wire;
/// GUID-validated at the endpoint). Required.
/// </param>
/// <param name="Args">
/// The chip's args forwarded verbatim (optional; the server owns the typed parse —
/// P1 vocabulary: <c>fileIds</c> string array).
/// </param>
public sealed record DispatchSessionRequest(
    string? BindingId = null,
    JsonElement? Args = null);
