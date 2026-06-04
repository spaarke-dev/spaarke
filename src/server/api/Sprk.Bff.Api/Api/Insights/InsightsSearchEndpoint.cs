using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Insights.LiveFacts;

namespace Sprk.Bff.Api.Api.Insights;

/// <summary>
/// Public hybrid-retrieval endpoint for the Spaarke Insights Engine (D-P15-06 / FR-04 /
/// SC-04, Wave E task 040). Companion to <c>POST /api/insights/ask</c> (playbook
/// synthesis path) — this endpoint serves open-ended natural-language queries via
/// generic RAG over <c>spaarke-insights-index</c> with LLM-synthesized grounded
/// citations.
/// </summary>
/// <remarks>
/// <para>
/// <b>Boundary placement</b>: Zone B per SPEC §3.5 — this file imports only
/// <see cref="IInsightsAi"/> (the single Zone-A surface Zone B may consume),
/// <see cref="ISubjectParser"/> (Zone B service registered by InsightsModule), and
/// Zone B POCOs (<see cref="InsightsSearchRequest"/>, <see cref="InsightsSearchResponse"/>,
/// <see cref="InsightsSearchFacadeRequest"/>, <see cref="InsightsSearchFacadeResult"/>).
/// The §3.5.4 forbidden-imports grep is asserted against <c>Api/Insights/</c> +
/// <c>Models/Insights/</c> before merge.
/// </para>
/// <para>
/// <b>Endpoint</b>: <c>POST /api/insights/search</c> — accepts
/// <c>{query, subject, top?, filter?}</c> and returns 200 OK with ranked Insights
/// results + LLM summary, OR a structured error envelope per ADR-019.
/// </para>
/// <para>
/// <b>Auth model</b>: <see cref="Microsoft.AspNetCore.Builder.AuthorizationEndpointConventionBuilderExtensions.RequireAuthorization{TBuilder}(TBuilder)"/>
/// only — any authenticated tenant user. The handler reads <c>tid</c> and <c>oid</c>
/// claims from <see cref="HttpContext.User"/>. Missing <c>tid</c> → 401 ProblemDetails;
/// missing <c>oid</c> → 401 ProblemDetails. <see cref="HttpContext.User"/> is forwarded
/// to <see cref="IInsightsAi.SearchAsync"/> for AIPU2-027 privilege filtering inside
/// the underlying <c>IRagService</c>.
/// </para>
/// <para>
/// <b>Rate limit</b>: <c>ai-context</c> policy (60/min sliding window per caller) per
/// ADR-016, matching the <c>/api/insights/ask</c> endpoint. RAG retrieval is read-heavy
/// context resolution — the same policy semantics apply.
/// </para>
/// <para>
/// <b>Kill-switch behavior (ADR-032 P3)</b>: when AI is disabled,
/// <c>NullRagService</c> throws <see cref="FeatureDisabledException"/> which propagates
/// through <see cref="IInsightsAi.SearchAsync"/>; the handler catches it FIRST (before
/// generic <see cref="Exception"/>) and returns 503 ProblemDetails via
/// <see cref="FeatureDisabledResults.AsFeatureDisabled503"/>.
/// </para>
/// <para>
/// <b>Wire response shape</b>: 200 OK with <see cref="InsightsSearchResponse"/>; the
/// summary carries <c>[n]</c> citations indexing into the result list. Empty results
/// also return 200 (with empty <c>Results</c> + empty <c>Summary</c>) so the UI can
/// render "no matches found" cleanly. ADR-019 ProblemDetails is reserved for true
/// failures (400 validation, 401/403 auth, 429 rate limit, 503 disabled, 500 internal).
/// </para>
/// </remarks>
public static class InsightsSearchEndpoint
{
    /// <summary>
    /// HTTP response header carrying the orchestrator-measured wall time in milliseconds.
    /// Matches the convention used by <c>InsightEndpoints.Ask</c>.
    /// </summary>
    private const string ElapsedHeader = "X-Insights-Elapsed-Ms";

    /// <summary>
    /// HTTP response header carrying the result count for quick log scanning.
    /// </summary>
    private const string HitCountHeader = "X-Insights-Hit-Count";

    /// <summary>Default top-K when caller does not specify.</summary>
    private const int DefaultTopK = 10;

    /// <summary>Min top-K accepted (post-clamp).</summary>
    private const int MinTopK = 1;

    /// <summary>Max top-K accepted (post-clamp).</summary>
    private const int MaxTopK = 20;

    /// <summary>
    /// Registers <c>POST /api/insights/search</c> on the supplied endpoint route builder.
    /// Called from <c>EndpointMappingExtensions.MapDomainEndpoints</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapInsightsSearchEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/insights")
            .RequireAuthorization()
            .RequireRateLimiting("ai-context")
            .WithTags("Insights");

        group.MapPost("/search", Search)
            .WithName("InsightsSearch")
            .WithSummary("Hybrid RAG retrieval + LLM-synthesized grounded summary (D-P15-06 / FR-04)")
            .WithDescription(
                "Accepts {query, subject, top?, filter?} and routes through IInsightsAi.SearchAsync. " +
                "Returns 200 OK with ranked Observations/Precedents from spaarke-insights-index " +
                "plus an LLM-synthesized summary carrying grounded [n] citations whose n indexes " +
                "into the result list (1-based). Subject scopes the retrieval to a specific " +
                "matter/project/invoice; optional filter narrows further by artifactType and/or " +
                "predicate. Empty results also return 200 (with empty Summary) — the orchestrator " +
                "does NOT fabricate a summary without grounding. Per SPEC §3.5 Zone B placement — " +
                "the endpoint consumes IInsightsAi only.")
            .Produces<InsightsSearchResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    /// <summary>
    /// <c>POST /api/insights/search</c> handler.
    /// </summary>
    private static async Task<IResult> Search(
        [FromBody] InsightsSearchRequest? request,
        HttpContext httpContext,
        IInsightsAi insightsAi,
        // [FromServices] is required because ISubjectParser exposes a TryParse(string,
        // out ParsedSubject, out string) instance method whose shape collides with the
        // minimal-API parameter-binding heuristic that would otherwise try to bind
        // ISubjectParser from query/route. Marking as service-resolved avoids the
        // "TryParse method found on ISubjectParser with incorrect format" host-startup
        // crash. (Discovered 2026-06-03 by InsightsSearchEndpointTests host-build failure.)
        [FromServices] ISubjectParser subjectParser,
        ILogger<InsightsSearchRequest> logger,
        CancellationToken ct)
    {
        // -----------------------------------------------------------------
        // Validation — ADR-019 ProblemDetails for every error
        // -----------------------------------------------------------------
        if (request is null)
            return BadRequest("Request body is required.");

        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest("'query' is required and cannot be empty.");

        if (string.IsNullOrWhiteSpace(request.Subject))
            return BadRequest("'subject' is required and cannot be empty.");

        // Wave E2 (FR-05) forceMode plumbing. The /search endpoint IS the canonical RAG
        // dispatcher — caller-declared "rag" intent is consistent; "playbook" intent is
        // a wrong-endpoint mismatch and gets rejected with 400 ProblemDetails so the caller
        // (or the future E3 Assistant) can re-dispatch to /api/insights/ask. Null = no
        // override = normal RAG behavior. The classifier itself is not invoked from this
        // endpoint in E2; the field exists for forward-compat with E3 Spaarke Assistant.
        if (!string.IsNullOrWhiteSpace(request.ForceMode))
        {
            if (string.Equals(request.ForceMode, "playbook", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(
                    "'forceMode' is 'playbook' but this endpoint serves the RAG path. " +
                    "Use POST /api/insights/ask for playbook dispatch.");
            }
            if (!string.Equals(request.ForceMode, "rag", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(
                    $"'forceMode' must be either 'playbook' or 'rag' (received: '{request.ForceMode}'). " +
                    "Omit the field to use the default intent-classifier dispatch (Wave E3+).");
            }
        }

        // Parse subject using the Wave D5 catalog-driven parser. Unlike /ask (Phase 1
        // matter-only), /search supports all registered schemes (matter/project/invoice
        // by default; extensible via Insights:Subject:Schemes config).
        if (!subjectParser.TryParse(request.Subject, out var parsedSubject, out var subjectError))
        {
            return BadRequest($"'subject' is invalid: {subjectError}");
        }

        // Clamp Top to a safe range. Per FR-04 the canonical default is 10.
        var topK = Math.Clamp(request.Top ?? DefaultTopK, MinTopK, MaxTopK);

        // -----------------------------------------------------------------
        // Auth context — derive tenantId from claims (mirrors /ask)
        // -----------------------------------------------------------------
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

        // -----------------------------------------------------------------
        // Delegate to IInsightsAi facade — Zone B → Zone A boundary crossing
        // -----------------------------------------------------------------
        // Forward HttpContext.User so the underlying IRagService can apply AIPU2-027
        // privilege-group filtering (see RagService Step 0 / PrivilegeGroupResolver).
        var facadeRequest = new InsightsSearchFacadeRequest(
            Query: request.Query!,
            ParentEntityType: parsedSubject.EntityType,
            ParentEntityId: parsedSubject.EntityId.ToString(),
            ArtifactType: request.Filter?.ArtifactType,
            Predicate: request.Filter?.Predicate,
            TopK: topK,
            TenantId: tenantId,
            CallerPrincipal: httpContext.User,
            ForceMode: request.ForceMode);

        InsightsSearchFacadeResult facadeResult;
        try
        {
            facadeResult = await insightsAi.SearchAsync(facadeRequest, ct);
        }
        // ADR-032 P3: NullRagService surfaced — MUST be caught BEFORE generic catch
        // so 503 takes precedence over fall-through 500.
        catch (FeatureDisabledException ex)
        {
            logger.LogDebug(
                "[INSIGHTS-SEARCH] AI feature disabled. ErrorCode={ErrorCode} TenantId={TenantId} Subject={Subject}",
                ex.ErrorCode, tenantId, request.Subject);
            return ex.AsFeatureDisabled503();
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled or request aborted — propagate so Kestrel records it
            // accurately rather than returning a synthetic 500.
            throw;
        }
        catch (ArgumentException ex)
        {
            // The facade's defensive parameter validation reached here — surface as 400.
            return BadRequest(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Per ADR-019: never leak document content / prompts / model output.
            logger.LogError(ex,
                "[INSIGHTS-SEARCH] SearchAsync failed for tenant {TenantId} subject {Subject} caller {CallerOid}",
                tenantId, request.Subject, callerOid);

            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to perform Insights search. See server logs for details.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "INSIGHTS_SEARCH_INTERNAL_ERROR",
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        // -----------------------------------------------------------------
        // Observability headers — set BEFORE writing the response body
        // -----------------------------------------------------------------
        httpContext.Response.Headers[ElapsedHeader] = facadeResult.DurationMs.ToString();
        httpContext.Response.Headers[HitCountHeader] = facadeResult.Results.Count.ToString();

        // -----------------------------------------------------------------
        // Project facade result → wire shape (Zone B POCO)
        // -----------------------------------------------------------------
        var responseBody = new InsightsSearchResponse(
            Query: facadeResult.Query,
            Results: facadeResult.Results.Select(r => new InsightsSearchResultItem(
                ChunkId: r.ChunkId,
                ObservationId: r.ObservationId,
                DocumentName: r.DocumentName,
                Snippet: r.Snippet,
                Predicate: r.Predicate,
                Confidence: r.Confidence)).ToList(),
            Summary: facadeResult.Summary,
            DurationMs: facadeResult.DurationMs);

        logger.LogInformation(
            "[INSIGHTS-SEARCH] Success tenant={TenantId} subject={Scheme}:{Id} hits={HitCount} hasSummary={HasSummary} elapsedMs={ElapsedMs}",
            tenantId, parsedSubject.EntityType, parsedSubject.EntityId,
            responseBody.Results.Count, !string.IsNullOrEmpty(responseBody.Summary), facadeResult.DurationMs);

        return Results.Ok(responseBody);
    }

    /// <summary>400 ProblemDetails helper — matches <c>InsightEndpoints.BadRequest</c>.</summary>
    private static IResult BadRequest(string detail)
        => Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            detail: detail,
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
}
