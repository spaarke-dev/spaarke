using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.ReviewMemo;
using Sprk.Bff.Api.Services.Compose;

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
/// <para>
/// <b>READ path (FR-14, task 051)</b>: two GET siblings added in the SAME file (still one feature —
/// "the Review Summary Memo" — not one file per HTTP verb): <c>GET .../review-memo</c> (JSON, for the
/// "Email memo" toolbar action's prefill) and <c>GET .../review-memo/docx</c> (the rendered, downloadable
/// file, for the "Generate memo" toolbar action). Both derive from the SAME persisted record
/// (render-from-persisted, project-binding) via the ONE new
/// <see cref="AnalysisResultPersistence.GetReviewMemoWithMetadataAsync"/> read method, which itself adds
/// exactly ONE new Dataverse read primitive (<see cref="Spaarke.Dataverse.IAnalysisDataverseService.GetLatestAnalysisOutputByNameAsync"/>)
/// — the smallest read surface that satisfies "how to read the memo back" (POML task 051). No memo
/// generated yet → both GETs return 404 ProblemDetails ("generate the review memo first"), never an
/// empty 200 (negative-path acceptance criterion).
/// </para>
/// </remarks>
public static class ReviewMemoEndpoints
{
    /// <summary>The rendered memo's download MIME type (OOXML WordprocessingML — matches the existing
    /// <c>.docx</c> constant used elsewhere in <c>Api/Ai/*.cs</c>, e.g. <c>ChatDocumentEndpoints</c>).</summary>
    private const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

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

        // GET /api/ai/chat/sessions/{sessionId}/review-memo (FR-14, task 051)
        group.MapGet("/{sessionId}/review-memo", GetReviewMemo)
            .AddAiAuthorizationFilter()
            .WithName("GetReviewMemo")
            .WithSummary("Read the persisted Review Summary Memo for the session's bound Analysis")
            .WithDescription(
                "Reads the most recently persisted Review Summary Memo (task 050) plus its analysis/" +
                "document display names, for the 'Email memo' toolbar action's prefill. 404 when no " +
                "memo has been generated yet for this session's bound Analysis.")
            .Produces<ReviewMemoReadResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // GET /api/ai/chat/sessions/{sessionId}/review-memo/docx (FR-14, task 051)
        group.MapGet("/{sessionId}/review-memo/docx", GetReviewMemoDocx)
            .AddAiAuthorizationFilter()
            .WithName("GetReviewMemoDocx")
            .WithSummary("Render the persisted Review Summary Memo as a downloadable .docx")
            .WithDescription(
                "Renders the persisted Review Summary Memo (task 050) via ComposeDocumentRenderer — " +
                "title, doc/analysis metadata, a per-section table {location, before, after, why, " +
                "golden-ref} — for the 'Generate memo' toolbar action. 404 when no memo has been " +
                "generated yet for this session's bound Analysis (never an empty download).")
            .Produces(StatusCodes.Status200OK, contentType: DocxContentType)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

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
        // NOTE (agreements-r1 UAT round-1 #2): this is the "no completed review" condition — DISTINCT
        // from the "session not bound to an Analysis" condition below. The two used to share one
        // conflated message; they are now split (distinct titles + `code` extensions) so a client can
        // tell "you have no review yet" apart from "promote this session first" (a completed review is
        // NOT lost — see ResolveBoundAnalysisIdAsync).
        if (request.Sections is null || request.Sections.Count == 0)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                "No Completed Review",
                "The review has no flagged sections to memo-ize. Generate a memo only after a completed review with at least one flagged section.",
                code: "no-completed-review");
        }

        var (analysisId, resolveError) = await ResolveBoundAnalysisIdAsync(sessionId, tenantId, sessionManager, cancellationToken);
        if (resolveError is not null)
        {
            return resolveError;
        }

        // Assembly (pure, no second LLM call) — partial/invalid assembly never reaches persistence:
        // ReviewMemoAssembler.Assemble is total over a non-empty, already-validated Sections list.
        var memo = ReviewMemoAssembler.Assemble(request);

        var outputId = await persistence.PersistReviewMemoAsync(analysisId, memo, cancellationToken);

        return Results.Created(
            $"/api/ai/analysis/{analysisId}",
            new GenerateReviewMemoResponse(analysisId, outputId, memo.SectionCount));
    }

    /// <summary>GET /api/ai/chat/sessions/{sessionId}/review-memo (FR-14, task 051).</summary>
    private static async Task<IResult> GetReviewMemo(
        string sessionId,
        ChatSessionManager sessionManager,
        AnalysisResultPersistence persistence,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Problem(StatusCodes.Status400BadRequest, "Tenant Required",
                "Tenant ID not found in token claims or X-Tenant-Id header.");
        }

        var (analysisId, resolveError) = await ResolveBoundAnalysisIdAsync(sessionId, tenantId, sessionManager, cancellationToken);
        if (resolveError is not null)
        {
            return resolveError;
        }

        var result = await persistence.GetReviewMemoWithMetadataAsync(analysisId, cancellationToken);
        if (result is null)
        {
            return NoMemoProblem();
        }

        return Results.Ok(new ReviewMemoReadResponse(analysisId, result.AnalysisName, result.DocumentName, result.Memo));
    }

    /// <summary>GET /api/ai/chat/sessions/{sessionId}/review-memo/docx (FR-14, task 051).</summary>
    private static async Task<IResult> GetReviewMemoDocx(
        string sessionId,
        ChatSessionManager sessionManager,
        AnalysisResultPersistence persistence,
        ComposeDocumentRenderer documentRenderer,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            return Problem(StatusCodes.Status400BadRequest, "Tenant Required",
                "Tenant ID not found in token claims or X-Tenant-Id header.");
        }

        var (analysisId, resolveError) = await ResolveBoundAnalysisIdAsync(sessionId, tenantId, sessionManager, cancellationToken);
        if (resolveError is not null)
        {
            return resolveError;
        }

        var result = await persistence.GetReviewMemoWithMetadataAsync(analysisId, cancellationToken);
        if (result is null)
        {
            return NoMemoProblem();
        }

        var model = ReviewMemoDocumentBuilder.Build(result.Memo, result.DocumentName, result.AnalysisName);
        var bytes = documentRenderer.SynthesizeDocument(model, author: string.Empty);

        return Results.File(bytes, DocxContentType, BuildMemoFileName(result.AnalysisName));
    }

    /// <summary>
    /// Sentinel-aware analysisId resolution (BINDING footgun, project CLAUDE.md): HostContext carries
    /// the sentinel literal EntityType == ChatSessionManager.AnalysisHostContextEntityType
    /// ("sprk_analysisoutput") while EntityId is the sprk_analysis GUID — never the output row id.
    /// Referencing the EXISTING internal constant (not re-typing the literal) per the binding rule.
    /// Shared by all three review-memo handlers (§11 reuse — extracted in task 051 without changing
    /// <see cref="GenerateReviewMemo"/>'s original tenant-check→sections-check→session-lookup order).
    /// </summary>
    private static async Task<(Guid AnalysisId, IResult? Error)> ResolveBoundAnalysisIdAsync(
        string sessionId,
        string tenantId,
        ChatSessionManager sessionManager,
        CancellationToken cancellationToken)
    {
        var session = await sessionManager.GetSessionAsync(tenantId, sessionId, cancellationToken);
        if (session is null)
        {
            return (Guid.Empty, Problem(
                StatusCodes.Status404NotFound,
                "Session Not Found",
                $"Session {sessionId} was not found."));
        }

        if (session.HostContext is null ||
            session.HostContext.EntityType != ChatSessionManager.AnalysisHostContextEntityType ||
            !Guid.TryParse(session.HostContext.EntityId, out var analysisId))
        {
            // agreements-r1 UAT round-1 #2 — SPLIT the two formerly-conflated conditions. This branch is
            // ONLY "the session is not bound to an Analysis" (the direct-Compose door — a session minted
            // by ComposeService.LoadAsync / direct-compose has no Analysis-owned HostContext; only the
            // wizard door binds durably). It is NOT "there is no completed review": the memo endpoint
            // cannot see review-completion state here (per task 050, disposition lives client-side), so
            // claiming "no completed review" was inaccurate — a review may well have completed. The memo
            // is unusable only because there is nowhere durable to persist it. Guide the user to the
            // EXISTING promote affordance (Assistant → History → "Promote to Analysis…", tasks 023/033),
            // which binds the session durably; NO silent auto-creation (ADR-041). The distinct `code`
            // lets the client surface a promote-first affordance instead of a dead-end error.
            return (Guid.Empty, Problem(
                StatusCodes.Status400BadRequest,
                "Session Not Bound To Analysis",
                "This session isn't linked to an Analysis, so there's nowhere to save a Review Summary Memo. If a review was completed here it is not lost — promote this session to an Analysis first (in the Assistant, open History and choose \"Promote to Analysis…\"), then generate the memo.",
                code: "session-not-bound"));
        }

        return (analysisId, null);
    }

    /// <summary>The negative-path 404 shared by both READ handlers (POML acceptance criterion: never
    /// surface an empty export — surface this clear message instead).</summary>
    private static IResult NoMemoProblem() => Problem(
        StatusCodes.Status404NotFound,
        "No Review Memo",
        "No Review Summary Memo has been generated for this session's Analysis yet. Generate the review memo first.",
        code: "no-memo");

    /// <summary>Sanitizes the analysis name into a safe Content-Disposition filename; falls back to a
    /// stable default when the name is blank or sanitizes to empty.</summary>
    private static string BuildMemoFileName(string? analysisName)
    {
        const string suffix = "Review Summary Memo.docx";
        if (string.IsNullOrWhiteSpace(analysisName))
        {
            return suffix;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(analysisName.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return sanitized.Length == 0 ? suffix : $"{sanitized} - {suffix}";
    }

    /// <summary>
    /// Builds a canonical ProblemDetails result (ADR-019). The optional <paramref name="code"/> is
    /// emitted as a machine-readable ProblemDetails extension member (agreements-r1 UAT round-1 #2) so
    /// a client can tell the split negative conditions apart — <c>session-not-bound</c> (promote first),
    /// <c>no-completed-review</c> (POST with no flagged sections), <c>no-memo</c> (nothing persisted
    /// yet) — instead of string-matching the human detail text.
    /// </summary>
    private static IResult Problem(int statusCode, string title, string detail, string? code = null) => Results.Problem(
        statusCode: statusCode,
        title: title,
        detail: detail,
        type: statusCode switch
        {
            StatusCodes.Status400BadRequest => "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            StatusCodes.Status404NotFound => "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            _ => "https://tools.ietf.org/html/rfc7231#section-6.6.1",
        },
        extensions: code is null ? null : new Dictionary<string, object?> { ["code"] = code });

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

/// <summary>Response for <c>GET /api/ai/chat/sessions/{sessionId}/review-memo</c> (FR-14, task 051) —
/// the persisted memo plus the display metadata the "Email memo" toolbar action needs for its subject
/// line ("Review Summary Memo — {AnalysisName}") without a second round-trip.</summary>
public sealed record ReviewMemoReadResponse(Guid AnalysisId, string? AnalysisName, string? DocumentName, ReviewMemoDocument Memo);
