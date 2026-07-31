using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.ReviewMemo;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// FR-13 (ai-advanced-capabilities-agreements-r1 task 050) — the Review Summary Memo endpoint.
/// Assembles + persists the memo described in spec FR-13 / Decisions #3/#4: for each changed/flagged
/// section, {location, before(original), after(final disposition), why, golden-ref}, derived at
/// GENERATION TIME from the caller-supplied section list (no per-accept event capture) and persisted
/// to <c>sprk_analysisoutput</c> — the ONLY review artifact that survives <c>DELETE /sessions</c>
/// (ADR-015; the Cosmos session ledger is erased on delete, Dataverse is not).
/// </summary>
/// <remarks>
/// <para>
/// <b>Placement Justification (CLAUDE.md §10 / bff-extensions.md)</b>: extends the existing "Analysis
/// endpoints" family (fork/promote already live at <c>/api/ai/analysis/*</c>; this endpoint is
/// SESSION-scoped like <c>ChatEndpoints</c>'s <c>/api/ai/chat/sessions/{sessionId}/*</c> siblings — it
/// needs the session's <see cref="ChatHostContext"/> to resolve the bound <c>sprk_analysis</c>, the same
/// sentinel-aware lookup <c>ChatDataverseRepository</c>/<c>AnalysisEndpoints</c>/<c>ChatSessionManager</c>
/// already perform). A new file (not appended to the already-1400+-line <c>AnalysisEndpoints.cs</c>)
/// keeps the addition reviewable, matching the existing one-feature-per-file convention
/// (<c>ChatEndpoints.cs</c>/<c>DocumentIntelligenceEndpoints.cs</c>/<c>SemanticSearchEndpoints.cs</c>).
/// Follows ADR-001 (Minimal API), ADR-008 (endpoint filter auth — <see cref="AiAuthorizationFilterExtensions.AddAiAuthorizationFilter{TBuilder}"/>),
/// ADR-019 (ProblemDetails). No new NuGet package. Mapped INSIDE the SAME
/// <c>Analysis:Enabled &amp;&amp; DocumentIntelligence:Enabled</c> compound gate as
/// <c>MapAnalysisEndpoints</c> (bff-extensions.md §F.1 asymmetric-registration rule) because this
/// endpoint's <see cref="AnalysisResultPersistence"/> dependency is registered ONLY inside that gate.
/// </para>
/// <para>
/// <b>Assembly placement (§11 reuse-first)</b>: the pure Decision #4 logic lives in
/// <see cref="ReviewMemoAssembler"/> (<c>Services/Ai/ReviewMemo/</c>) — a NEW sibling file, not an
/// edit to the already-conflicted <c>AnalysisResultPersistence.cs</c> (a stale branch,
/// <c>multi-container-multi-index-r1</c>, carries an unmerged 71-line refactor of that file; this
/// task's edit to it is a single additive method). Persistence is ONE new method on the EXISTING
/// KEEP-list <see cref="AnalysisResultPersistence"/> — no new persistence mechanism.
/// </para>
/// </remarks>
public static class ReviewMemoEndpoints
{
    public static IEndpointRouteBuilder MapReviewMemoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/chat/sessions")
            .RequireAuthorization()
            .WithTags("AI Analysis");

        // POST /api/ai/chat/sessions/{sessionId}/review-memo
        group.MapPost("/{sessionId}/review-memo", GenerateReviewMemo)
            .AddAiAuthorizationFilter()
            .RequireRateLimiting("ai-batch")
            .WithName("GenerateReviewMemo")
            .WithSummary("Assemble and persist the Review Summary Memo for the session's bound Analysis")
            .WithDescription(
                "Assembles the FR-13 Review Summary Memo from the caller-supplied section list " +
                "(final tracked-change disposition state at generation time — Decision #4: before= " +
                "original, after=final disposition, no per-accept event capture) and persists it to " +
                "sprk_analysisoutput under the session's bound sprk_analysis (sentinel-aware resolution " +
                "via HostContext.EntityId). The memo is the ONLY review artifact that survives DELETE " +
                "/sessions/{id} — everything needed to read it stands alone (ADR-015).")
            .Produces<GenerateReviewMemoResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task<IResult> GenerateReviewMemo(
        string sessionId,
        GenerateReviewMemoRequest request,
        ChatSessionManager sessionManager,
        AnalysisResultPersistence persistence,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "Tenant Required",
                "Tenant ID not found in token claims or X-Tenant-Id header.");
        }

        // Negative acceptance criterion (spec FR-13 / task 050): generation with no completed review
        // (no flagged sections supplied) returns a clear error and persists nothing. Checked BEFORE
        // any session lookup so an empty/malformed request never reaches the write path.
        if (request.Sections is null || request.Sections.Count == 0)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "No Completed Review",
                "The review has no flagged sections to memo-ize. Generate a memo only after a completed review with at least one flagged section.");
        }

        var session = await sessionManager.GetSessionAsync(tenantId, sessionId, cancellationToken);
        if (session is null)
        {
            return Problem(
                StatusCodes.Status404NotFound,
                "Session Not Found",
                $"Session {sessionId} was not found.");
        }

        // Sentinel-aware analysisId resolution (BINDING footgun, project CLAUDE.md): HostContext
        // carries the sentinel literal EntityType == ChatSessionManager.AnalysisHostContextEntityType
        // ("sprk_analysisoutput") while EntityId is the sprk_analysis GUID — never the output row id.
        // Referencing the EXISTING internal constant (not re-typing the literal) per the binding rule.
        if (session.HostContext is null ||
            session.HostContext.EntityType != ChatSessionManager.AnalysisHostContextEntityType ||
            !Guid.TryParse(session.HostContext.EntityId, out var analysisId))
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "Session Not Bound To Analysis",
                $"Session {sessionId} is not bound to an Analysis — there is no completed review to memo-ize.");
        }

        // Assembly (pure, no second LLM call) — partial/invalid assembly never reaches persistence:
        // ReviewMemoAssembler.Assemble is total over a non-empty, already-validated Sections list.
        var memo = ReviewMemoAssembler.Assemble(request);

        var outputId = await persistence.PersistReviewMemoAsync(analysisId, memo, cancellationToken);

        return Results.Created(
            $"/api/ai/analysis/{analysisId}",
            new GenerateReviewMemoResponse(analysisId, outputId, memo.SectionCount));
    }

    /// <summary>Builds a canonical ProblemDetails result (ADR-019).</summary>
    private static IResult Problem(int statusCode, string title, string detail) => Results.Problem(
        statusCode: statusCode,
        title: title,
        detail: detail,
        type: statusCode switch
        {
            StatusCodes.Status400BadRequest => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            StatusCodes.Status404NotFound => "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            _ => "https://tools.ietf.org/html/rfc7231#section-6.6.1",
        });

    /// <summary>
    /// Extracts the tenant ID from the JWT <c>tid</c> claim (ADR-014) with an <c>X-Tenant-Id</c> header
    /// fallback for service-to-service calls. Mirrors <c>AnalysisEndpoints.ExtractTenantId</c> /
    /// <c>ChatEndpoints.ExtractTenantId</c> (each endpoint file carries its own copy per existing
    /// convention — see the ~14 sibling private implementations across <c>Api/Ai/*.cs</c>).
    /// </summary>
    private static string? ExtractTenantId(HttpContext httpContext)
    {
        var tenantId = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
        if (string.IsNullOrEmpty(tenantId))
        {
            tenantId = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        }
        return tenantId;
    }
}

/// <summary>Response for <c>POST /api/ai/chat/sessions/{sessionId}/review-memo</c>.</summary>
public sealed record GenerateReviewMemoResponse(Guid AnalysisId, Guid OutputId, int SectionCount);
