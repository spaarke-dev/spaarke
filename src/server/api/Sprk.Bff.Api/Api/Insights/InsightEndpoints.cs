using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Api.Insights;

/// <summary>
/// Public-facing tenant-user endpoint for the Spaarke Insights Engine (D-P15, task 061).
/// </summary>
/// <remarks>
/// <para>
/// <b>Boundary placement</b>: Zone B per SPEC §3.5 — this file imports
/// <see cref="IInsightsAi"/> (the only Zone-A surface Zone B may consume) plus the
/// Zone B POCOs (<see cref="InsightArtifact"/>, <see cref="DeclineResponse"/>,
/// <see cref="InsightAskRequest"/>, <see cref="InsightsAgentRequest"/>,
/// <see cref="InsightsAgentResult"/>). The §3.5.4 forbidden-imports grep is asserted
/// against <c>Api/Insights/</c> + <c>Models/Insights/</c> before merge.
/// </para>
/// <para>
/// <b>Endpoints</b>:
/// <list type="bullet">
///   <item><c>POST /api/insights/ask</c> — synthesize an Insights-mode answer
///   (Inference) or return a structured <see cref="DeclineResponse"/> per D-49.</item>
/// </list>
/// </para>
/// <para>
/// <b>Auth model</b>: <see cref="Microsoft.AspNetCore.Builder.AuthorizationEndpointConventionBuilderExtensions.RequireAuthorization{TBuilder}(TBuilder)"/>
/// only — any authenticated tenant user. The handler reads <c>tid</c> and <c>oid</c>
/// claims from <see cref="HttpContext.User"/>. Missing <c>tid</c> → 401 ProblemDetails
/// (token is invalid for Insights Engine purposes); missing <c>oid</c> → 401
/// ProblemDetails. There is NO role gate — Insights synthesis is a tenant-user
/// capability (per D-P15 task POML "regular tenant user, not admin role").
/// </para>
/// <para>
/// <b>Rate limit</b>: <c>ai-context</c> policy (60 requests/minute sliding window per
/// caller <c>oid</c>) per ADR-016. Matches the task POML target of "60 req/min per
/// caller" and is semantically appropriate — Insights synthesis is read-heavy context
/// resolution. Exceeded limit returns 429 ProblemDetails with <c>Retry-After</c>
/// header (centralised in <c>RateLimitingModule.OnRejected</c>).
/// </para>
/// <para>
/// <b>Wire response shape</b>: both success and decline return 200 OK with body
/// <see cref="InsightAskResponse"/>. Decline is NOT an error — the playbook executed
/// successfully and produced a structured insufficient-evidence response per D-49.
/// ADR-019 ProblemDetails is reserved for true failures (400 validation, 401/403
/// auth, 429 rate limit, 500 internal error).
/// </para>
/// </remarks>
public static class InsightEndpoints
{
    /// <summary>
    /// Registers the <c>POST /api/insights/ask</c> route on the supplied endpoint
    /// route builder. Called from
    /// <c>EndpointMappingExtensions.MapDomainEndpoints</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapInsightsAskEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/insights")
            .RequireAuthorization()
            .RequireRateLimiting("ai-context")
            .WithTags("Insights");

        group.MapPost("/ask", Ask)
            .WithName("AskInsights")
            .WithSummary("Synthesize an Insights-mode answer or return a structured decline (D-P15)")
            .WithDescription(
                "Accepts {question, subject, parameters} and routes through IInsightsAi.AnswerQuestionAsync. " +
                "Returns 200 OK with an Inference InsightArtifact on success, OR 200 OK with a structured " +
                "DeclineResponse (D-49) when evidence is insufficient. Both branches share the " +
                "InsightAskResponse envelope shape ({artifact, decline}). Observability headers " +
                "X-Insights-Cache and X-Insights-Elapsed-Ms reflect the D-P13 cache outcome and " +
                "orchestrator-measured wall time. Per SPEC §3.5 Zone B placement — the endpoint " +
                "consumes IInsightsAi only.")
            .Produces<InsightAskResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    /// <summary>
    /// HTTP response header carrying the D-P13 cache outcome
    /// (<c>true</c> on hit, <c>false</c> on miss).
    /// </summary>
    private const string CacheHeader = "X-Insights-Cache";

    /// <summary>
    /// HTTP response header carrying the orchestrator-measured wall time in milliseconds.
    /// </summary>
    private const string ElapsedHeader = "X-Insights-Elapsed-Ms";

    /// <summary>
    /// Phase 1 accepted subject scheme — only <c>matter:</c> is supported per task POML
    /// guidance. Other schemes return 400 ProblemDetails.
    /// </summary>
    private const string MatterSubjectPrefix = "matter:";

    /// <summary>
    /// POST /api/insights/ask
    /// </summary>
    private static async Task<IResult> Ask(
        [FromBody] InsightAskRequest? request,
        HttpContext httpContext,
        IInsightsAi insightsAi,
        IOptionsSnapshot<InsightsPlaybookNameMapOptions> nameMapOptions,
        ILogger<InsightAskRequest> logger,
        CancellationToken ct)
    {
        // ---------------------------------------------------------------
        // Validation — ADR-019 ProblemDetails for every error
        // ---------------------------------------------------------------
        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest("'question' is required and cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(request.Subject))
        {
            return BadRequest("'subject' is required and cannot be empty.");
        }

        // Wave E2 (FR-05) forceMode plumbing. The /ask endpoint IS the canonical playbook
        // dispatcher — caller-declared "playbook" intent is consistent; "rag" intent is a
        // wrong-endpoint mismatch and gets rejected with 400 ProblemDetails so the caller
        // (or the future E3 Assistant) can re-dispatch to /api/insights/search. Null = no
        // override = normal playbook behavior. The classifier itself is not invoked from
        // this endpoint in E2; the field exists for forward-compat with E3 Spaarke Assistant.
        if (!string.IsNullOrWhiteSpace(request.ForceMode))
        {
            if (string.Equals(request.ForceMode, "rag", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(
                    "'forceMode' is 'rag' but this endpoint serves the playbook path. " +
                    "Use POST /api/insights/search for RAG dispatch.");
            }
            if (!string.Equals(request.ForceMode, "playbook", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(
                    $"'forceMode' must be either 'playbook' or 'rag' (received: '{request.ForceMode}'). " +
                    "Omit the field to use the default intent-classifier dispatch (Wave E3+).");
            }
        }

        // Resolve Question → Guid. Two acceptable inputs (in priority order):
        //   1. A raw Guid (advanced/direct path — original Phase 1 contract; still works).
        //   2. A canonical playbook name (e.g., "predict-matter-cost@v1") registered in
        //      the InsightsPlaybookNameMapOptions config map. Lets PCFs / code pages /
        //      external clients use a stable name across Dev / Test / Prod without
        //      hard-coding the env-specific Dataverse Guid.
        // The Guid attempt comes first so existing Guid callers see no behavior change.
        if (!Guid.TryParse(request.Question, out var playbookId) || playbookId == Guid.Empty)
        {
            playbookId = nameMapOptions.Value.ResolveOrDefault(request.Question);
            if (playbookId == Guid.Empty)
            {
                var registered = nameMapOptions.Value.Map.Count == 0
                    ? "<none — Insights:Playbooks:Map is empty in this environment's config>"
                    : string.Join(", ", nameMapOptions.Value.Map.Keys);

                return BadRequest(
                    "'question' must be either a valid playbook Guid id OR a canonical name " +
                    $"registered in '{InsightsPlaybookNameMapOptions.SectionName}:Map' configuration. " +
                    $"Received: '{request.Question}'. " +
                    $"Configured names in this environment: {registered}.");
            }
        }

        // Phase 1 subject contract: matter:{id}. Other schemes are out of scope per task POML.
        if (!request.Subject.StartsWith(MatterSubjectPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(
                $"'subject' must begin with '{MatterSubjectPrefix}' in Phase 1 " +
                "(e.g., 'matter:{id}'). Other schemes are not yet supported.");
        }

        var subjectId = request.Subject.Substring(MatterSubjectPrefix.Length).Trim();
        if (string.IsNullOrEmpty(subjectId))
        {
            return BadRequest($"'subject' is missing an identifier after '{MatterSubjectPrefix}'.");
        }

        // ---------------------------------------------------------------
        // Auth context — derive tenantId + caller oid from claims.
        // ---------------------------------------------------------------
        // 'tid' is the Entra ID tenant claim (mapped from 'http://schemas.microsoft.com/identity/claims/tenantid'
        // by Microsoft Identity Web's default claim mapping settings; we check both for robustness).
        var tenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            // The auth pipeline normally rejects tokens without 'tid', but we guard explicitly
            // so a malformed token reaching this handler produces a clean 401 ProblemDetails
            // rather than an opaque 500 when IInsightsAi rejects an empty TenantId.
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

        // ---------------------------------------------------------------
        // AccessibleScopeHash (Phase 1) — derived from tenant + caller.
        // ---------------------------------------------------------------
        // Per DEP-3 (decisions.md): within-tenant access trimming source is open. Under D-52
        // single-tenant, tenant scope dominates; Phase 1.5 will refine when matter-scope
        // claims arrive on the principal. Hashing tenant + oid gives a stable cache-key
        // discriminator that invalidates per-caller — acceptable for Phase 1 acceptance.
        var accessibleScopeHash = ComputeAccessibleScopeHash(tenantId, callerOid);

        // ---------------------------------------------------------------
        // Delegate to IInsightsAi facade — Zone B → Zone A boundary crossing.
        // ---------------------------------------------------------------
        var facadeRequest = new InsightsAgentRequest(
            Question: playbookId,
            Subject: request.Subject,
            Parameters: request.Parameters,
            TenantId: tenantId,
            AccessibleScopeHash: accessibleScopeHash);

        InsightsAgentResult result;
        try
        {
            result = await insightsAi.AnswerQuestionAsync(facadeRequest, ct);
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled or request aborted — propagate so Kestrel records it
            // accurately rather than returning a synthetic 500.
            throw;
        }
        catch (ArgumentException ex)
        {
            // The facade contract throws ArgumentException for validation faults that
            // slipped past our pre-checks (e.g., a future facade-side rule we don't yet
            // mirror here). Surface as 400 with the message — facade messages are
            // designed to be user-safe per ADR-019 (no content leakage).
            return BadRequest(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Per ADR-019: never leak document content / prompts / model output. Generic
            // 500 with correlation id; details are in the structured log.
            logger.LogError(ex,
                "[INSIGHTS-ASK] AnswerQuestionAsync failed for playbook {PlaybookId} subject {Subject} tenant {TenantId} caller {CallerOid}",
                playbookId, request.Subject, tenantId, callerOid);

            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to produce insight. See server logs for details.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "INSIGHTS_INTERNAL_ERROR",
                    ["correlationId"] = httpContext.TraceIdentifier
                });
        }

        // ---------------------------------------------------------------
        // Observability headers — set BEFORE writing the response body.
        // ---------------------------------------------------------------
        httpContext.Response.Headers[CacheHeader] = result.CacheHit ? "true" : "false";
        httpContext.Response.Headers[ElapsedHeader] = result.ProcessingTimeMs.ToString();

        // ---------------------------------------------------------------
        // Response — 200 OK for both branches; envelope discriminates.
        // ---------------------------------------------------------------
        if (result.Artifact is not null)
        {
            logger.LogInformation(
                "[INSIGHTS-ASK] Success playbook {PlaybookId} subject {Subject} tenant {TenantId} cacheHit={CacheHit} elapsedMs={ElapsedMs}",
                playbookId, request.Subject, tenantId, result.CacheHit, result.ProcessingTimeMs);

            return Results.Ok(new InsightAskResponse(Artifact: result.Artifact, Decline: null));
        }

        if (result.Decline is not null)
        {
            logger.LogInformation(
                "[INSIGHTS-ASK] Declined playbook {PlaybookId} subject {Subject} tenant {TenantId} reason={Reason} cacheHit={CacheHit} elapsedMs={ElapsedMs}",
                playbookId, request.Subject, tenantId, result.Decline.Reason, result.CacheHit, result.ProcessingTimeMs);

            return Results.Ok(new InsightAskResponse(Artifact: null, Decline: result.Decline));
        }

        // Defensive: IInsightsAi contract guarantees exactly one of Artifact/Decline is
        // populated. If both null, the facade impl is broken — surface as 500 so we don't
        // hide a contract violation behind an empty envelope.
        logger.LogError(
            "[INSIGHTS-ASK] IInsightsAi returned a result with neither Artifact nor Decline (contract violation). " +
            "playbook={PlaybookId} subject={Subject} tenant={TenantId}",
            playbookId, request.Subject, tenantId);

        return Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Internal Server Error",
            detail: "Facade returned an empty result. See server logs.",
            type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = "INSIGHTS_FACADE_EMPTY_RESULT",
                ["correlationId"] = httpContext.TraceIdentifier
            });
    }

    /// <summary>
    /// 400 ProblemDetails helper to keep the handler readable.
    /// </summary>
    private static IResult BadRequest(string detail)
        => Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            detail: detail,
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");

    /// <summary>
    /// Compute the Phase 1 <c>AccessibleScopeHash</c> from the tenant + caller oid.
    /// Returns a lowercase hex SHA-256 (64 chars). Stable for a given (tenant, caller)
    /// pair; collisions are negligible at the cache-key cardinality we expect.
    /// </summary>
    /// <remarks>
    /// Phase 1.5 will replace this with a proper accessible-scope projection (matter set,
    /// practice area set, etc.) once those claims arrive on the principal or are
    /// queryable from a unified access-control service (DEP-3 resolution).
    /// </remarks>
    private static string ComputeAccessibleScopeHash(string tenantId, string callerOid)
    {
        var input = $"tid:{tenantId}|oid:{callerOid}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
