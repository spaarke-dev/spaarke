using System.Runtime.ExceptionServices;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// R5 task 014 / D2-04 — direct BFF endpoint for the chat-driven Summarize vertical slice.
/// <c>POST /api/ai/chat/sessions/{sessionId}/summarize</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Convergence (spec FR-01 + FR-08 + SC-08)</b>: this endpoint is the HTTP entry point for
/// the dual-path convention described in spec §4 — the slash command <c>/summarize</c>
/// (task 019 / D2-10) calls this endpoint directly, while the agent-tool
/// <c>InvokeSummarizePlaybookTool</c> (task 015 / D2-05) reaches the same destination via
/// the LLM tool-call path. Both legs converge on
/// <see cref="SessionSummarizeOrchestrator.SummarizeSessionFilesAsync"/> (task 012 / D2-03)
/// so behavior is identical regardless of entry — including the new <c>FieldDelta</c>
/// variant (task 005 / D1-05) emitted progressively as Structured Outputs streams.
/// </para>
/// <para>
/// <b>Placement Justification (per <c>.claude/constraints/bff-extensions.md</c> + R5 CLAUDE.md §10)</b>:
/// this endpoint belongs in BFF — not in CRUD code, not in a shared library, not as a
/// direct PCF/Code-Page call. Reasons:
/// <list type="number">
///   <item><b>SSE streaming requires a long-lived HTTP connection</b> — only the BFF
///         server-side surface can hold the connection open while
///         <see cref="SessionSummarizeOrchestrator"/> emits incremental
///         <see cref="AnalysisChunk"/> deltas.</item>
///   <item><b>Server-side AI orchestration</b> — the orchestrator composes
///         <see cref="IRagService"/> + <see cref="IOpenAiClient"/> + Structured Outputs +
///         the JPS prompt loaded from <c>sprk_analysisaction</c>. None of these can be
///         safely composed in a client.</item>
///   <item><b>OBO auth</b> — the orchestrator's downstream Graph + AI Search calls run on
///         a fresh bearer token resolved from the request principal per Auth v2 (ADR-028).
///         Frontend code cannot resolve OBO tokens.</item>
///   <item><b>Rate limiting + correlation ID</b> — the <c>ai-context</c> policy (ADR-016)
///         and per-request <c>X-Correlation-Id</c> propagation only function at the BFF
///         boundary.</item>
/// </list>
/// </para>
/// <para>
/// <b>Auth + rate-limit (ADR-008 + ADR-016)</b>: <c>RequireAuthorization()</c> at the route
/// group (token validation) + <c>AddAiAuthorizationFilter()</c> resource-level endpoint
/// filter (read-access check) + <c>RequireRateLimiting("ai-context")</c> shared AI policy.
/// The <c>ai-context</c> policy is verified to exist in <c>RateLimitingModule.cs</c>
/// (sliding-window 60 req/min/user). No new rate-limit policy is defined.
/// </para>
/// <para>
/// <b>ProblemDetails (ADR-019)</b>: every error path returns ProblemDetails with a stable
/// <c>errorCode</c> string extension AND a <c>correlationId</c> extension set to
/// <see cref="HttpContext.TraceIdentifier"/>. Detail strings NEVER leak document content,
/// prompts, or model output (orchestrator exception messages are mapped to stable
/// error-codes; raw exception text is logged but not returned).
/// </para>
/// <para>
/// <b>Fresh OBO token per request (ADR-028)</b>: the handler never snapshots the bearer
/// token into a closure. The orchestrator + its downstream <see cref="IRagService"/> /
/// <see cref="IOpenAiClient"/> dependencies resolve their tokens via DI inside the
/// per-request scope — verified by reading task 012's <c>SessionSummarizeOrchestrator</c>
/// constructor (no string token parameter).
/// </para>
/// <para>
/// <b>Asymmetric-registration (R5 CLAUDE.md §10 F.1)</b>: this endpoint maps
/// UNCONDITIONALLY (no <c>if (flag)</c> guard). <see cref="SessionSummarizeOrchestrator"/>
/// is registered UNCONDITIONALLY in <c>AnalysisServicesModule.AddAnalysisOrchestrationServices</c>
/// (task 012, line 336). Asymmetric-registration rule satisfied.
/// </para>
/// </remarks>
public static class SummarizeSessionEndpoint
{
    /// <summary>JSON serialization options for SSE event payloads (camelCase; matches sibling Chat endpoints).</summary>
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ─── Stable errorCode extensions (ADR-019) ──────────────────────────────────────
    // These strings are CONTRACT — frontend clients switch on them to render
    // feature-specific UX. Do NOT rename without a corresponding contract update.
    private const string ErrorCodeTenantMissing       = "auth.tid-missing";
    private const string ErrorCodeOidMissing          = "auth.oid-missing";
    private const string ErrorCodeSessionIdRequired   = "sessionId.required";
    private const string ErrorCodeSessionIdInvalid    = "sessionId.invalid";
    private const string ErrorCodeTooManyFiles        = "summarize.too-many-files";
    private const string ErrorCodeSessionNotFound     = "summarize.session-not-found";
    private const string ErrorCodeFeatureDisabled     = "summarize.feature-disabled";
    private const string ErrorCodeInternalError       = "summarize.internal-error";

    /// <summary>
    /// Registers <c>POST /api/ai/chat/sessions/{sessionId}/summarize</c> on the supplied
    /// endpoint route builder. Called from
    /// <c>EndpointMappingExtensions.MapDomainEndpoints</c> adjacent to
    /// <c>MapChatEndpoints()</c>. ZERO new lines in <c>Program.cs</c> (ADR-010 + R5 §3.3).
    /// </summary>
    public static IEndpointRouteBuilder MapSummarizeSessionEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/chat")
            .RequireAuthorization()
            .RequireRateLimiting("ai-context")
            .WithTags("AI Chat");

        group.MapPost("/sessions/{sessionId}/summarize", SummarizeAsync)
            .AddAiAuthorizationFilter()
            .WithName("SummarizeChatSession")
            .WithSummary("Summarize files uploaded into a chat session (R5 D2-04)")
            .WithDescription(
                "Direct entry point for the chat-driven Summarize vertical slice. Streams " +
                "AnalysisChunk SSE events including the additive FieldDelta variant so the " +
                "Workspace tab populates progressively (TL;DR first → summary → per-file " +
                "highlights). Convergence sibling: the agent-tool InvokeSummarizePlaybookTool " +
                "(task 015) reaches the same SessionSummarizeOrchestrator via the LLM " +
                "tool-call path — identical output regardless of entry point.")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    /// <summary>
    /// Handler for <c>POST /api/ai/chat/sessions/{sessionId}/summarize</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns <see cref="Task"/> (NOT <see cref="Task{IResult}"/>) — for SSE streaming we
    /// take control of <see cref="HttpResponse"/> directly: set <c>text/event-stream</c>
    /// content-type, write the response body, and never return an <see cref="IResult"/>
    /// for the happy path. For pre-stream validation errors we write a JSON ProblemDetails
    /// body to the response manually (cannot return <see cref="IResult"/> from a void
    /// handler).
    /// </para>
    /// <para>
    /// Pattern mirror: combines <see cref="Insights.InsightsAssistantEndpoint"/> (auth
    /// claim extraction + ProblemDetails errorCode shape) with
    /// <see cref="ChatEndpoints.SendMessageAsync"/> (SSE streaming write-loop).
    /// </para>
    /// </remarks>
    private static async Task SummarizeAsync(
        string sessionId,
        [FromBody] SummarizeSessionRequest? body,
        HttpContext httpContext,
        SessionSummarizeOrchestrator orchestrator,
        ILogger<SummarizeSessionRequest> logger)
    {
        // Use HttpContext.RequestAborted for cancellation — propagates client disconnects.
        // Per ADR-028 + R5 §10: NEVER snapshot a closure-captured token; the orchestrator
        // resolves OBO tokens via DI inside its dependencies on every call.
        var cancellationToken = httpContext.RequestAborted;
        var response = httpContext.Response;
        var correlationId = httpContext.TraceIdentifier;

        // ─── Pre-stream validation — ProblemDetails (ADR-019) ──────────────────────────
        // These errors are written as application/problem+json BEFORE we set SSE headers.

        // Route param empty/missing — defensive (routing already requires non-empty).
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            await WriteProblemDetailsAsync(
                response, StatusCodes.Status400BadRequest, "Bad Request",
                "'sessionId' route parameter is required.",
                ErrorCodeSessionIdRequired, correlationId, cancellationToken);
            return;
        }

        // GUID-format validation. Session IDs are GUIDs per task 004 ChatSession.SessionId.
        if (!Guid.TryParse(sessionId, out _))
        {
            await WriteProblemDetailsAsync(
                response, StatusCodes.Status400BadRequest, "Bad Request",
                "'sessionId' must be a valid GUID.",
                ErrorCodeSessionIdInvalid, correlationId, cancellationToken);
            return;
        }

        // NFR-02 defense-in-depth — 20-file cap. The orchestrator also enforces; this
        // surfaces the violation as a 400 with stable errorCode (orchestrator would throw
        // ArgumentException which we'd otherwise have to map).
        if (body?.FileIds is { Count: > 20 })
        {
            await WriteProblemDetailsAsync(
                response, StatusCodes.Status400BadRequest, "Bad Request",
                $"Too many fileIds in request: {body.FileIds.Count}. The per-session cap is 20.",
                ErrorCodeTooManyFiles, correlationId, cancellationToken);
            return;
        }

        // ─── Auth context — derive tenantId + oid from claims (mirror InsightsAssistantEndpoint) ──
        var tenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            await WriteProblemDetailsAsync(
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
            await WriteProblemDetailsAsync(
                response, StatusCodes.Status401Unauthorized, "Unauthorized",
                "User identity ('oid' claim) not found in authentication token.",
                ErrorCodeOidMissing, correlationId, cancellationToken);
            return;
        }

        // ─── Build orchestrator request (convergence contract — task 012) ─────────────
        // The endpoint passes SummarizeInvocationPath.DirectEndpoint to drive the
        // telemetry 'path' dimension. The agent-tool path (task 015) will pass
        // SummarizeInvocationPath.AgentTool. Output is byte-identical for the same
        // (TenantId, SessionId, FileIds, StyleHint) tuple per the orchestrator contract.
        var request = new SummarizeSessionFilesRequest(
            TenantId: tenantId,
            SessionId: sessionId,
            FileIds: body?.FileIds,
            StyleHint: body?.Style,
            Path: SummarizeInvocationPath.DirectEndpoint,
            CorrelationId: correlationId);

        logger.LogInformation(
            "[SUMMARIZE-SESSION] Start tenant={TenantId} session={SessionId} oid={Oid} fileIds={FileIdCount} style={Style} correlationId={CorrelationId}",
            tenantId, sessionId, callerOid, body?.FileIds?.Count ?? 0, body?.Style, correlationId);

        // ─── Probe orchestrator: catch early synchronous-failure modes BEFORE setting ──
        // SSE headers so we can return a normal JSON ProblemDetails. The orchestrator
        // SHOULD yield AnalysisChunk.FromError instead of throwing for runtime errors,
        // but:
        //   - InvalidOperationException("session not found") is documented (line 188)
        //   - FeatureDisabledException can propagate through the orchestrator's deps if
        //     a downstream Null-Object service is wired up (ADR-018/ADR-032)
        //   - ArgumentException (NFR-02 cap) is defensive; we filter > 20 above so this
        //     should only fire for orchestrator-internal validation drift
        //   - Generic Exception: log + 500
        //
        // We MoveNext() once to detect synchronous throws, then stream from there.
        IAsyncEnumerator<AnalysisChunk>? enumerator = null;
        bool hasFirst = false;
        AnalysisChunk first = default!;
        ExceptionDispatchInfo? earlyFailure = null;

        try
        {
            enumerator = orchestrator.SummarizeSessionFilesAsync(request, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
            hasFirst = await enumerator.MoveNextAsync().ConfigureAwait(false);
            if (hasFirst)
            {
                first = enumerator.Current;
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnect / cancellation BEFORE any byte was written. Nothing to
            // surface — just dispose and return.
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

        // ─── Map early synchronous failures to ProblemDetails (BEFORE SSE headers set) ──
        if (earlyFailure is not null)
        {
            // Dispose the enumerator if we got one before the throw.
            if (enumerator is not null)
            {
                try { await enumerator.DisposeAsync().ConfigureAwait(false); } catch { /* cleanup-tail */ }
            }

            var ex = earlyFailure.SourceException;

            // FeatureDisabledException FIRST per ADR-032 P3 pattern (mirrors
            // InsightsAssistantEndpoint line 205).
            if (ex is FeatureDisabledException fde)
            {
                logger.LogDebug(
                    "[SUMMARIZE-SESSION] Feature disabled. errorCode={ErrorCode} tenant={TenantId} session={SessionId}",
                    fde.ErrorCode, tenantId, sessionId);
                // Use the canonical helper for kill-switch shape (ADR-018/ADR-019).
                await WriteIResultAsync(httpContext, fde.AsFeatureDisabled503());
                return;
            }

            // Orchestrator throws InvalidOperationException for session-not-found
            // (SessionSummarizeOrchestrator.cs line 188-190). Map to 404.
            if (ex is InvalidOperationException ioe && ioe.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation(
                    "[SUMMARIZE-SESSION] Session not found tenant={TenantId} session={SessionId}",
                    tenantId, sessionId);
                await WriteProblemDetailsAsync(
                    response, StatusCodes.Status404NotFound, "Not Found",
                    "The chat session was not found.",
                    ErrorCodeSessionNotFound, correlationId, cancellationToken);
                return;
            }

            // ArgumentException from orchestrator input validation (NFR-02 cap, empty
            // tenant, empty session). Map to 400 with a generic shape — we already
            // pre-validated GUID + 20-file cap, so this is defense-in-depth.
            if (ex is ArgumentException)
            {
                logger.LogWarning(ex,
                    "[SUMMARIZE-SESSION] Orchestrator argument validation failed tenant={TenantId} session={SessionId}",
                    tenantId, sessionId);
                await WriteProblemDetailsAsync(
                    response, StatusCodes.Status400BadRequest, "Bad Request",
                    "Summarize request validation failed.",
                    ErrorCodeSessionIdInvalid, correlationId, cancellationToken);
                return;
            }

            // Catch-all 500 — log full exception detail; do NOT leak to wire (ADR-019).
            logger.LogError(ex,
                "[SUMMARIZE-SESSION] Orchestrator failed (pre-stream) tenant={TenantId} session={SessionId}",
                tenantId, sessionId);
            await WriteProblemDetailsAsync(
                response, StatusCodes.Status500InternalServerError, "Internal Server Error",
                "Failed to start the Summarize stream. See server logs for details.",
                ErrorCodeInternalError, correlationId, cancellationToken);
            return;
        }

        // ─── SSE response headers — set BEFORE writing the response body ──────────────
        // X-Accel-Buffering: no — prevent reverse-proxy buffering so each chunk reaches
        // the client immediately (matches ChatEndpoints.SendMessageAsync line 371).
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";

        // ─── Stream the (already-pulled) first chunk + remaining chunks ───────────────
        try
        {
            if (hasFirst)
            {
                await WriteSseChunkAsync(response, first, cancellationToken);
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
                    // Client disconnected mid-stream. No further bytes to write.
                    break;
                }
                catch (Exception ex)
                {
                    // Mid-stream exception escaped the orchestrator's per-token try-catch
                    // (which yields FromError + terminates). Defensive: emit an error
                    // chunk + terminate. The orchestrator's invariant should prevent
                    // this, but we never let the SSE stream just die without a marker.
                    logger.LogError(ex,
                        "[SUMMARIZE-SESSION] Mid-stream exception escaped orchestrator tenant={TenantId} session={SessionId}",
                        tenantId, sessionId);
                    try
                    {
                        await WriteSseChunkAsync(response,
                            AnalysisChunk.FromError("The summarization stream was interrupted."),
                            CancellationToken.None);
                    }
                    catch { /* response may already be in an unwritable state */ }
                    break;
                }

                if (!moveNext) break;
                await WriteSseChunkAsync(response, enumerator.Current, cancellationToken);
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
            "[SUMMARIZE-SESSION] Stream complete tenant={TenantId} session={SessionId} correlationId={CorrelationId}",
            tenantId, sessionId, correlationId);
    }

    // ─── SSE write helper — `data: {json}\n\n` per SSE format ──────────────────────
    private static async Task WriteSseChunkAsync(
        HttpResponse response,
        AnalysisChunk chunk,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(chunk, s_jsonOptions);
        var sse = $"data: {json}\n\n";
        await response.WriteAsync(sse, cancellationToken).ConfigureAwait(false);
        await response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    // ─── ProblemDetails writer — for pre-stream validation/auth errors ─────────────
    // Pre-stream we haven't set SSE headers; write a standard application/problem+json
    // body. ADR-019 conformance: stable errorCode + correlationId extensions; no PII.
    private static async Task WriteProblemDetailsAsync(
        HttpResponse response,
        int statusCode,
        string title,
        string detail,
        string errorCode,
        string correlationId,
        CancellationToken cancellationToken)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/problem+json";

        var problem = new
        {
            type = ResolveRfcType(statusCode),
            title,
            status = statusCode,
            detail,
            errorCode,
            correlationId
        };

        var json = JsonSerializer.Serialize(problem, s_jsonOptions);
        await response.WriteAsync(json, cancellationToken).ConfigureAwait(false);
    }

    // ─── IResult writer for FeatureDisabledException 503 path ──────────────────────
    // The canonical helper FeatureDisabledResults.AsFeatureDisabled503 returns IResult.
    // Our handler is void Task (so we can take ownership of SSE writes), so we manually
    // execute the IResult against the HttpContext to write its body.
    private static async Task WriteIResultAsync(HttpContext httpContext, IResult result)
    {
        await result.ExecuteAsync(httpContext).ConfigureAwait(false);
    }

    private static string ResolveRfcType(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
        StatusCodes.Status401Unauthorized => "https://tools.ietf.org/html/rfc7235#section-3.1",
        StatusCodes.Status403Forbidden => "https://tools.ietf.org/html/rfc7231#section-6.5.3",
        StatusCodes.Status404NotFound => "https://tools.ietf.org/html/rfc7231#section-6.5.4",
        StatusCodes.Status500InternalServerError => "https://tools.ietf.org/html/rfc7231#section-6.6.1",
        StatusCodes.Status503ServiceUnavailable => "https://errors.spaarke.com/feature-disabled",
        _ => "about:blank"
    };
}

/// <summary>
/// Optional request body for <c>POST /api/ai/chat/sessions/{sessionId}/summarize</c>.
/// </summary>
/// <param name="FileIds">
/// Optional subset of <see cref="Models.Ai.Chat.ChatSession.UploadedFiles"/> to summarize
/// (max 20 per NFR-02). When omitted, the orchestrator defaults to ALL files in the session
/// manifest (FR-08).
/// </param>
/// <param name="Style">
/// Optional natural-language style hint passed through to the system prompt
/// (e.g., <c>executive</c>, <c>detailed</c>, <c>bullet-points</c>).
/// </param>
public sealed record SummarizeSessionRequest(
    IReadOnlyList<string>? FileIds = null,
    string? Style = null);
