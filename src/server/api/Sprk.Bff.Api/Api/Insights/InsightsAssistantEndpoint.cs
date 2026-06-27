using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Insights.LiveFacts;

namespace Sprk.Bff.Api.Api.Insights;

/// <summary>
/// Unified Spaarke Assistant tool-call endpoint (Wave E3 task 042 / FR-05). Companion
/// to <c>POST /api/insights/ask</c> (playbook path) and <c>POST /api/insights/search</c>
/// (RAG path) — this endpoint is the single tool surface the Spaarke Assistant invokes;
/// the BFF makes the routing decision internally per the Wave E2 intent classifier
/// (or an Assistant-supplied <c>forceMode</c> override) and returns a uniform response
/// shape.
/// </summary>
/// <remarks>
/// <para>
/// <b>Boundary placement</b>: Zone B per SPEC §3.5 — this file imports only
/// <see cref="IInsightsAi"/> (the single Zone-A surface Zone B may consume),
/// <see cref="ISubjectParser"/> (Zone B service), and Zone B POCOs
/// (<see cref="InsightsAssistantQueryRequest"/>,
/// <see cref="InsightsAssistantQueryResponse"/>,
/// <see cref="AssistantQueryFacadeRequest"/>, <see cref="AssistantQueryFacadeResult"/>).
/// The §3.5.4 forbidden-imports grep is asserted against <c>Api/Insights/</c> +
/// <c>Models/Insights/</c> before merge.
/// </para>
/// <para>
/// <b>Endpoint</b>: <c>POST /api/insights/assistant/query</c> — accepts
/// <c>{query, subject, forceMode?, conversationContext?}</c> per contract §3 and
/// returns 200 OK with a uniform <see cref="InsightsAssistantQueryResponse"/>
/// envelope per contract §4. Errors per ADR-019 ProblemDetails.
/// </para>
/// <para>
/// <b>Auth + rate-limit</b>: <see cref="Microsoft.AspNetCore.Builder.AuthorizationEndpointConventionBuilderExtensions.RequireAuthorization{TBuilder}(TBuilder)"/>
/// + <c>ai-context</c> policy per ADR-016 — matches <c>/ask</c> + <c>/search</c>
/// semantics so the Assistant aggregate budget is shared across all three endpoints.
/// </para>
/// <para>
/// <b>Kill-switch behavior (ADR-032 P3)</b>: when classifier / RAG / playbook layer is
/// OFF, <see cref="FeatureDisabledException"/> propagates from the facade; the handler
/// catches it FIRST (before generic <see cref="Exception"/>) and returns 503
/// ProblemDetails via <see cref="FeatureDisabledResults.AsFeatureDisabled503"/>. The
/// <c>errorCode</c> extension carries the stable feature key
/// (<c>ai.insights.disabled</c> | <c>ai.rag.disabled</c> |
/// <c>ai.intent-classification.disabled</c>) the Assistant uses to drive UI.
/// </para>
/// <para>
/// <b>Contract anchor</b>: <c>projects/ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md</c>.
/// </para>
/// </remarks>
public static class InsightsAssistantEndpoint
{
    /// <summary>HTTP response header carrying the orchestrator-measured wall time in ms.</summary>
    private const string ElapsedHeader = "X-Insights-Elapsed-Ms";

    /// <summary>HTTP response header carrying the path taken: <c>playbook</c> | <c>rag</c>.</summary>
    private const string PathHeader = "X-Insights-Path";

    /// <summary>HTTP response header carrying the intent source: <c>classifier</c> |
    /// <c>forceMode</c> | <c>classifier-fallback</c>.</summary>
    private const string IntentSourceHeader = "X-Insights-Intent-Source";

    /// <summary>HTTP response header carrying the playbook D-P13 cache outcome.</summary>
    private const string CacheHeader = "X-Insights-Cache";

    /// <summary>HTTP response header carrying the RAG hit count (or playbook citation count).</summary>
    private const string HitCountHeader = "X-Insights-Hit-Count";

    /// <summary>Stable error-code surface for the contract §5.1 default-playbook unconfigured 503.</summary>
    private const string DefaultPlaybookUnconfiguredErrorCode = "ai.assistant-default-playbook.unconfigured";

    /// <summary>
    /// Registers <c>POST /api/insights/assistant/query</c> on the supplied endpoint route
    /// builder. Called from <c>EndpointMappingExtensions.MapDomainEndpoints</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapInsightsAssistantEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/insights/assistant")
            .RequireAuthorization()
            .RequireRateLimiting("ai-context")
            .WithTags("Insights");

        group.MapPost("/query", AssistantQuery)
            .WithName("InsightsAssistantQuery")
            .WithSummary("Unified Spaarke Assistant tool-call entry point (Wave E3 / FR-05)")
            .WithDescription(
                "Accepts {query, subject, forceMode?, conversationContext?} and routes through " +
                "the Wave E2 intent classifier (or the Assistant-supplied forceMode override) to " +
                "the playbook OR RAG path. Returns 200 OK with a uniform " +
                "InsightsAssistantQueryResponse envelope carrying answer + citations + structured " +
                "result + routing diagnostics. Per SPEC §3.5 Zone B placement — endpoint consumes " +
                "IInsightsAi only. Contract anchor: " +
                "projects/ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md.")
            .Produces<InsightsAssistantQueryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    /// <summary>
    /// MIME-type identifier for Server-Sent Events negotiation (Wave F task 051 v1.1).
    /// </summary>
    private const string SseMediaType = "text/event-stream";

    /// <summary>SSE terminator sentinel per R5 §2.2 contract.</summary>
    private const string SseDoneFrame = "data: [DONE]\n\n";

    /// <summary>JSON options for SSE frame serialization (camelCase, omit nulls).</summary>
    private static readonly JsonSerializerOptions SseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// <c>POST /api/insights/assistant/query</c> handler. Negotiates SSE vs single-shot via
    /// <c>Accept</c> header per R5 §2.1: <c>text/event-stream</c> → SSE; absent or
    /// <c>application/json</c> → existing v1.0 single-shot response. v1.0 clients are
    /// unaffected.
    /// </summary>
    private static async Task<IResult> AssistantQuery(
        [FromBody] InsightsAssistantQueryRequest? request,
        HttpContext httpContext,
        IInsightsAi insightsAi,
        // [FromServices] avoids the minimal-API binder confusing ISubjectParser's TryParse
        // shape for a query/route binding — same workaround as InsightsSearchEndpoint.
        [FromServices] ISubjectParser subjectParser,
        ILogger<InsightsAssistantQueryRequest> logger,
        CancellationToken ct)
    {
        // ─── Validation — ADR-019 ProblemDetails ──────────────────────────────────────
        if (request is null)
        {
            return BadRequest("Request body is required.", "query.required");
        }

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest("'query' is required and cannot be empty.", "query.required");
        }

        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            return BadRequest("'subject' is required and cannot be empty.", "subject.required");
        }

        // forceMode: optional; when supplied must be 'playbook' or 'rag' (case-insensitive).
        if (!string.IsNullOrWhiteSpace(request.ForceMode))
        {
            if (!string.Equals(request.ForceMode, "playbook", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(request.ForceMode, "rag", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(
                    $"'forceMode' must be either 'playbook' or 'rag' (received: '{request.ForceMode}'). " +
                    "Omit the field to let the intent classifier decide.",
                    "forceMode.invalid");
            }
        }

        // conversationContext.previousTurnSummary length is enforced by [StringLength(2000)]
        // — request binding rejects > 2000 chars with a default validation 400. We do NOT
        // emit a custom errorCode for that case (binder owns it); contract §5.1 lists
        // conversationContext.invalid as the codified error and a future binding-error
        // shape adapter can surface it. Phase 1.5 is read-only telemetry.

        // Subject parsing per Wave D5.
        if (!subjectParser.TryParse(request.Subject, out var parsedSubject, out var subjectError))
        {
            return BadRequest($"'subject' is invalid: {subjectError}", "subject.invalid");
        }

        // ─── Auth context — derive tenantId + oid from claims (mirrors /ask + /search) ──
        var tenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "Tenant identity ('tid' claim) not found in authentication token.",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        var callerOid = httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(callerOid))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "User identity ('oid' claim) not found in authentication token.",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        // ─── Delegate to IInsightsAi facade (Zone B → Zone A) ─────────────────────────
        var facadeRequest = new AssistantQueryFacadeRequest(
            Query: request.Query!,
            ParentEntityType: parsedSubject.EntityType,
            ParentEntityId: parsedSubject.EntityId.ToString(),
            Subject: request.Subject!,
            ForceMode: request.ForceMode,
            ConversationId: request.ConversationContext?.ConversationId,
            PreviousTurnSummary: request.ConversationContext?.PreviousTurnSummary,
            TenantId: tenantId,
            CallerOid: callerOid,
            CallerPrincipal: httpContext.User);

        // ─── Wave F task 051 — Accept-header negotiation (v1.1 streaming) ─────────────
        // Per R5 §2.1: clients request streaming via `Accept: text/event-stream`. Otherwise
        // the v1.0 single-shot JSON path is preserved unchanged. Detect via the Accept header
        // values collection rather than a substring match so multipart values like
        // `text/event-stream, application/json;q=0.5` are honored.
        if (ClientAcceptsServerSentEvents(httpContext.Request))
        {
            // Streaming branch — returns IResult that writes SSE frames directly.
            return await StreamAssistantQuery(
                httpContext, insightsAi, facadeRequest, tenantId, request.Subject!, callerOid, logger, ct)
                .ConfigureAwait(false);
        }

        AssistantQueryFacadeResult facadeResult;
        try
        {
            facadeResult = await insightsAi.AssistantQueryAsync(facadeRequest, ct);
        }
        // ADR-032 P3 kill-switch — MUST be caught BEFORE generic catch.
        catch (FeatureDisabledException ex)
        {
            logger.LogDebug(
                "[INSIGHTS-ASSISTANT] AI feature disabled. ErrorCode={ErrorCode} TenantId={TenantId} Subject={Subject}",
                ex.ErrorCode, tenantId, request.Subject);
            return ex.AsFeatureDisabled503();
        }
        // Contract §5.1 default-playbook unconfigured — explicit 503 with specific errorCode.
        catch (InvalidOperationException ex) when (ex.Message.Contains("Default playbook", StringComparison.Ordinal))
        {
            logger.LogError(ex,
                "[INSIGHTS-ASSISTANT] Default playbook unconfigured for forceMode=playbook. TenantId={TenantId}",
                tenantId);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Service Unavailable",
                detail: ex.Message,
                type: "https://errors.spaarke.com/feature-disabled",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = DefaultPlaybookUnconfiguredErrorCode,
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message, "INSIGHTS_ASSISTANT_VALIDATION");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Per ADR-019: never leak document content / prompts / model output.
            logger.LogError(ex,
                "[INSIGHTS-ASSISTANT] AssistantQueryAsync failed for tenant {TenantId} subject {Subject} caller {CallerOid}",
                tenantId, request.Subject, callerOid);

            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to complete Insights Assistant query. See server logs for details.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "INSIGHTS_ASSISTANT_INTERNAL_ERROR",
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        // ─── Observability headers — set BEFORE writing the response body ─────────────
        httpContext.Response.Headers[ElapsedHeader] = facadeResult.DurationMs.ToString();
        httpContext.Response.Headers[PathHeader] = facadeResult.Path;
        httpContext.Response.Headers[IntentSourceHeader] = facadeResult.IntentSource;
        httpContext.Response.Headers[CacheHeader] = facadeResult.CacheHit ? "true" : "false";
        httpContext.Response.Headers[HitCountHeader] = facadeResult.HitCount.ToString();

        // ─── Project facade result → wire shape (Zone B POCO) ─────────────────────────
        // The envelope JSON is already serialized by the handler; we parse it back into
        // a JsonElement so the response serialiser emits it as a native sub-object rather
        // than a JSON-encoded string.
        JsonElement envelope;
        try
        {
            using var doc = JsonDocument.Parse(facadeResult.StructuredEnvelopeJson);
            envelope = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            // Defensive: handler-produced envelope should always be valid JSON. If we hit
            // this, the handler is broken — log + emit empty envelope so the Assistant has
            // a well-formed envelope to render. We do NOT 500 — the answer is still useful.
            logger.LogError(ex,
                "[INSIGHTS-ASSISTANT] StructuredEnvelopeJson malformed; surfacing empty envelope. TenantId={TenantId}",
                tenantId);
            using var fallback = JsonDocument.Parse("{}");
            envelope = fallback.RootElement.Clone();
        }

        var responseBody = new InsightsAssistantQueryResponse(
            Path: facadeResult.Path,
            Answer: facadeResult.Answer,
            Citations: facadeResult.Citations
                .Select(c => new InsightsAssistantCitation(
                    N: c.N,
                    Source: c.Source,
                    Excerpt: c.Excerpt,
                    ObservationId: c.ObservationId,
                    ChunkId: c.ChunkId,
                    Href: c.Href))
                .ToList(),
            Confidence: facadeResult.Confidence,
            PlaybookId: facadeResult.PlaybookId,
            StructuredResult: new InsightsAssistantStructuredResult(
                Kind: facadeResult.StructuredKind,
                Envelope: envelope),
            Diagnostics: new InsightsAssistantDiagnostics(
                IntentSource: facadeResult.IntentSource,
                ClassifierBelowThreshold: facadeResult.ClassifierBelowThreshold,
                ElapsedMs: facadeResult.DurationMs,
                CacheHit: facadeResult.CacheHit));

        logger.LogInformation(
            "[INSIGHTS-ASSISTANT] Success tenant={TenantId} subject={Subject} path={Path} intentSource={IntentSource} hits={HitCount} elapsedMs={ElapsedMs}",
            tenantId, request.Subject, facadeResult.Path, facadeResult.IntentSource, facadeResult.HitCount, facadeResult.DurationMs);

        return Results.Ok(responseBody);
    }

    /// <summary>
    /// True when the request's <c>Accept</c> header includes <c>text/event-stream</c>.
    /// Honors comma-separated multi-value Accept and ignores q-parameters. Returns false
    /// when the header is absent (defaults to v1.0 single-shot JSON per R5 §2.6 back-compat).
    /// </summary>
    /// <remarks>
    /// We deliberately do NOT short-circuit on a wildcard (<c>*/*</c>) — only explicit
    /// <c>text/event-stream</c> opts the client into the streaming response. This matches
    /// R5's contract §2.6 invariant: v1.0 clients that send <c>Accept: */*</c> (the default
    /// for many HTTP libraries) MUST continue to receive single-shot JSON.
    /// </remarks>
    private static bool ClientAcceptsServerSentEvents(HttpRequest request)
    {
        var accept = request.Headers.Accept;
        if (accept.Count == 0)
        {
            return false;
        }

        foreach (var value in accept)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;

            // Header value can be multi-typed: "text/event-stream, application/json;q=0.5"
            // — split on comma; check each segment for a media-type prefix match.
            foreach (var segment in value.Split(','))
            {
                var trimmed = segment.Trim();
                // Strip any q-parameter (";q=0.x") before comparing.
                var semicolon = trimmed.IndexOf(';');
                var mediaType = semicolon >= 0 ? trimmed[..semicolon].Trim() : trimmed;
                if (string.Equals(mediaType, SseMediaType, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// SSE branch of <see cref="AssistantQuery"/> (Wave F task 051 / FR-05 v1.1). Negotiated
    /// via <c>Accept: text/event-stream</c>. Writes the response directly to
    /// <see cref="HttpContext.Response"/> as a sequence of SSE frames, terminating with
    /// the <c>data: [DONE]\n\n</c> sentinel per R5 §2.2.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Kill-switch ordering</b> (per ADR-032 + spike Section A): pre-stream errors
    /// (<see cref="FeatureDisabledException"/>, default-playbook-unconfigured,
    /// <see cref="ArgumentException"/>) MUST return 503/400 ProblemDetails with NO SSE body.
    /// To enforce this invariant we drain the FIRST chunk of the async enumerable in a
    /// try/catch BEFORE flipping the response to SSE mode. Once the first chunk is consumed
    /// without exception, we set SSE headers + write the body; subsequent exceptions become
    /// mid-stream <c>error</c> frames per mini-plan §6 decision 4.
    /// </para>
    /// <para>
    /// <b>Header invariant (R5 §2.5)</b>: per-request observability headers
    /// (<c>X-Insights-Path</c>, etc.) are NOT carried on SSE responses — they reflect
    /// post-completion state that's only known after the stream finishes. Clients SHOULD
    /// inspect the terminal <c>result</c> chunk's facade-result payload for these values
    /// (path / intentSource / durationMs / cacheHit / hitCount are all in
    /// <see cref="AssistantQueryFacadeResult"/>). This matches the R5 §2.5 simplification
    /// for the v1.1 streaming surface.
    /// </para>
    /// </remarks>
    private static async Task<IResult> StreamAssistantQuery(
        HttpContext httpContext,
        IInsightsAi insightsAi,
        AssistantQueryFacadeRequest facadeRequest,
        string tenantId,
        string subject,
        string callerOid,
        ILogger logger,
        CancellationToken ct)
    {
        // Drain the first chunk eagerly so pre-stream errors surface as ProblemDetails
        // BEFORE we set SSE headers. This preserves the R5 §2.5 + ADR-032 invariant:
        // 503 + 400 + 500 responses never carry an SSE body.
        var enumerator = insightsAi.AssistantQueryStreamAsync(facadeRequest, ct).GetAsyncEnumerator(ct);
        try
        {
            bool hasFirst;
            try
            {
                hasFirst = await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            catch (FeatureDisabledException ex)
            {
                logger.LogDebug(
                    "[INSIGHTS-ASSISTANT-STREAM] AI feature disabled pre-stream. ErrorCode={ErrorCode} TenantId={TenantId} Subject={Subject}",
                    ex.ErrorCode, tenantId, subject);
                return ex.AsFeatureDisabled503();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Default playbook", StringComparison.Ordinal))
            {
                logger.LogError(ex,
                    "[INSIGHTS-ASSISTANT-STREAM] Default playbook unconfigured. TenantId={TenantId}", tenantId);
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Service Unavailable",
                    detail: ex.Message,
                    type: "https://errors.spaarke.com/feature-disabled",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = DefaultPlaybookUnconfiguredErrorCode,
                        ["correlationId"] = httpContext.TraceIdentifier
                    });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message, "INSIGHTS_ASSISTANT_VALIDATION");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "[INSIGHTS-ASSISTANT-STREAM] Pre-stream failure for tenant {TenantId} subject {Subject} caller {CallerOid}",
                    tenantId, subject, callerOid);
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Internal Server Error",
                    detail: "Failed to initialize Insights Assistant stream. See server logs for details.",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = "INSIGHTS_ASSISTANT_INTERNAL_ERROR",
                        ["correlationId"] = httpContext.TraceIdentifier
                    });
            }

            // ─── Begin SSE body ──────────────────────────────────────────────────────
            // Set SSE headers + disable response buffering. After this point, mid-stream
            // errors emit `error` chunks rather than ProblemDetails.
            var response = httpContext.Response;
            response.ContentType = SseMediaType + "; charset=utf-8";
            response.Headers.CacheControl = "no-cache";
            response.Headers[HeaderNames.Connection] = "keep-alive";
            response.Headers["X-Accel-Buffering"] = "no"; // disable proxy buffering

            try
            {
                // Write the first chunk we already drained.
                if (hasFirst)
                {
                    await WriteSseChunkAsync(response, enumerator.Current, ct).ConfigureAwait(false);
                }

                // Drain the rest.
                while (await enumerator.MoveNextAsync().ConfigureAwait(false))
                {
                    await WriteSseChunkAsync(response, enumerator.Current, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Client disconnected mid-stream. No error frame — the connection is gone.
                logger.LogInformation(
                    "[INSIGHTS-ASSISTANT-STREAM] Client cancelled mid-stream. TenantId={TenantId} Subject={Subject}",
                    tenantId, subject);
            }
            catch (FeatureDisabledException ex)
            {
                // Kill-switch tripped mid-stream — emit `error` frame per mini-plan §6 decision 4.
                logger.LogDebug(
                    "[INSIGHTS-ASSISTANT-STREAM] Feature disabled mid-stream. ErrorCode={ErrorCode} TenantId={TenantId}",
                    ex.ErrorCode, tenantId);
                await WriteSseErrorAsync(response, ex.ErrorCode, ex.Message, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Mid-stream failure — emit `error` frame, do NOT throw further (response is
                // already started; throwing would corrupt the wire). Per ADR-019: never leak
                // internal exception details to the wire — surface a stable error code.
                logger.LogError(ex,
                    "[INSIGHTS-ASSISTANT-STREAM] Mid-stream failure for tenant {TenantId} subject {Subject}",
                    tenantId, subject);
                await WriteSseErrorAsync(
                    response,
                    "INSIGHTS_ASSISTANT_STREAM_ERROR",
                    "An error occurred while streaming the response. See server logs for details.",
                    ct).ConfigureAwait(false);
            }
            finally
            {
                // Always terminate with [DONE] sentinel per R5 §2.2.
                try
                {
                    await response.WriteAsync(SseDoneFrame, ct).ConfigureAwait(false);
                    await response.Body.FlushAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { /* client gone — ignore */ }
                catch (Exception ex)
                {
                    logger.LogDebug(ex,
                        "[INSIGHTS-ASSISTANT-STREAM] Failed to write DONE sentinel; client likely disconnected. TenantId={TenantId}",
                        tenantId);
                }
            }

            logger.LogInformation(
                "[INSIGHTS-ASSISTANT-STREAM] Completed streaming response. TenantId={TenantId} Subject={Subject}",
                tenantId, subject);
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        // Return Empty — we've already written the response body directly.
        return Results.Empty;
    }

    /// <summary>
    /// Write a single SSE frame: <c>event: {type}\ndata: {json}\n\n</c>. Flushes the
    /// response after each frame so the client sees streaming behavior immediately
    /// (not buffered).
    /// </summary>
    private static async Task WriteSseChunkAsync(
        HttpResponse response,
        AssistantQueryChunk chunk,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(chunk, SseJsonOptions);
        var frame = $"event: {chunk.Type}\ndata: {json}\n\n";
        await response.WriteAsync(frame, ct).ConfigureAwait(false);
        await response.Body.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Write a mid-stream <c>error</c> SSE frame. The frame uses the same format as
    /// <see cref="WriteSseChunkAsync"/> with a synthetic <see cref="AssistantQueryChunk"/>
    /// carrying the error envelope.
    /// </summary>
    private static async Task WriteSseErrorAsync(
        HttpResponse response,
        string errorCode,
        string detail,
        CancellationToken ct)
    {
        var errorChunk = new AssistantQueryChunk
        {
            Type = "error",
            Error = new AssistantQueryError(errorCode, detail)
        };
        try
        {
            await WriteSseChunkAsync(response, errorChunk, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* client gone — ignore */ }
    }

    /// <summary>
    /// 400 ProblemDetails helper with the stable errorCode extension per contract §5.1.
    /// </summary>
    private static IResult BadRequest(string detail, string errorCode)
        => Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            detail: detail,
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode
            });
}
