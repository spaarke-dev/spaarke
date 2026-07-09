using System.Text;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Daily briefing AI endpoints following ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// Provides AI-generated prioritized briefing summaries from structured notification data.
/// Extends BFF per ADR-013 — no separate AI microservice.
/// </summary>
public static class DailyBriefingEndpoints
{
    public static IEndpointRouteBuilder MapDailyBriefingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/daily-briefing")
            .RequireAuthorization()
            .WithTags("AI Daily Briefing");

        // POST /api/ai/daily-briefing/summarize — Generate prioritized briefing from notification data
        group.MapPost("/summarize", Summarize)
            .RequireRateLimiting("ai-batch")
            .WithName("SummarizeDailyBriefing")
            .WithSummary("Generate AI-powered daily briefing summary")
            .WithDescription(
                "Accepts structured notification data (counts per category, top priority items) " +
                "and returns a 3-4 sentence prioritized briefing via Azure OpenAI.")
            .Produces<DailyBriefingSummaryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(429)
            .ProducesProblem(503);

        // POST /api/ai/daily-briefing/narrate — Generate narrative briefing with per-channel bullets
        group.MapPost("/narrate", HandleNarrate)
            .RequireRateLimiting("ai-batch")
            .WithName("NarrateDailyBriefing")
            .WithSummary("Generate AI-powered narrative briefing with per-channel detail")
            .WithDescription(
                "Accepts structured notification data plus per-channel items, " +
                "and returns a TL;DR briefing with narrative bullets per channel via Azure OpenAI.")
            .Produces<DailyBriefingNarrateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(429)
            .ProducesProblem(503);

        // POST /api/ai/daily-briefing/render — Single-call live briefing render.
        // FR-P3-04 (spaarke-ai-architecture-redesign-r1 task 043): dispatches as the FIRST
        // full `coded` composite Action — DailyBriefingCompositeService resolves the Binding
        // (daily-briefing-narrate/default), executes the Binding's coded workflow via the
        // ICodedWorkflow registry, writes the session-ledger entries BEFORE rendering
        // (ADR-040), then returns the renderable response. No request body — discovers the
        // user from the OBO token and resolves their systemuserid to drive collector queries.
        group.MapPost("/render", HandleRender)
            .RequireRateLimiting("ai-batch")
            .WithName("RenderDailyBriefing")
            .WithSummary("Render full Daily Briefing via the coded composite Action (live Dataverse queries)")
            .WithDescription(
                "Runs live FetchXML queries against Dataverse for the calling user's events, " +
                "then executes the catalog-resolved coded briefing workflow (Binding decides — ADR-039). " +
                "Returns the same DailyBriefingNarrateResponse shape as /narrate.")
            .Produces<DailyBriefingNarrateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(401)
            .ProducesProblem(429)
            .ProducesProblem(500)
            .ProducesProblem(503);

        // POST /api/ai/daily-briefing/email — Email delivery leg (FR-P3-04 / UC-D-1).
        // Executes the SAME coded composite via the `email` Binding (consumerCode=email,
        // disposition=email): collect → narrate → ledger write → Communication-service
        // delivery to the calling user (SendMode.User OBO). The scheduled trigger is
        // declared on the Binding row's sprk_oneventbindings (briefing_scheduled); the
        // scheduler invokes THIS route per user at the per-user time.
        group.MapPost("/email", HandleEmail)
            .RequireRateLimiting("ai-batch")
            .WithName("EmailDailyBriefing")
            .WithSummary("Render and email the Daily Briefing to the calling user via the coded composite Action")
            .Produces<DailyBriefingNarrateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(401)
            .ProducesProblem(429)
            .ProducesProblem(500)
            .ProducesProblem(503);

        return app;
    }

    /// <summary>
    /// FR-P3-04 (task 043): live-query render path via the coded composite. Resolves the
    /// caller's systemuserid from the OBO token's AAD oid claim, then delegates to
    /// <see cref="Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingCompositeService.RenderAsync"/>
    /// (Binding-resolved coded workflow; ledger write-before-render per ADR-040).
    /// No body required — the briefing is self-contained.
    /// </summary>
    private static async Task<IResult> HandleRender(
        ILoggerFactory loggerFactory,
        Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingCompositeService composite,
        Spaarke.Dataverse.IGenericEntityService entityService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("DailyBriefingEndpoints");

        var identity = await ResolveSystemUserIdAsync(entityService, httpContext, logger, "render", cancellationToken)
            .ConfigureAwait(false);
        if (identity.Failure is not null)
        {
            return identity.Failure;
        }

        try
        {
            var response = await composite.RenderAsync(
                identity.SystemUserId, GetTenantId(httpContext), cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(response);
        }
        catch (FeatureDisabledException ex)
        {
            logger.LogDebug(
                "Daily briefing render called while AI feature disabled. ErrorCode={ErrorCode}", ex.ErrorCode);
            return ex.AsFeatureDisabled503();
        }
        catch (Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingDispatchUnconfiguredException ex)
        {
            logger.LogWarning(ex, "Daily briefing render dispatch unconfigured.");
            return Results.Problem(statusCode: 503, title: "Service Unavailable", detail: ex.Message);
        }
        catch (OpenAiCircuitBrokenException ex)
        {
            logger.LogWarning(
                "OpenAI circuit breaker open for daily briefing render. RetryAfter={RetryAfter}s",
                ex.RetryAfter.TotalSeconds);
            return ProblemDetailsHelper.AiUnavailable(
                "AI briefing service is temporarily unavailable.", httpContext.TraceIdentifier);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Daily briefing render failed for systemuserid {SystemUserId}", identity.SystemUserId);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to render daily briefing.");
        }
    }

    /// <summary>
    /// FR-P3-04 (task 043) email leg: executes the coded composite via the <c>email</c>
    /// Binding and delivers the briefing to the calling user through the Communication
    /// (Email) service (OutputRouter email disposition — store precedes send, ADR-040).
    /// </summary>
    private static async Task<IResult> HandleEmail(
        ILoggerFactory loggerFactory,
        Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingCompositeService composite,
        Spaarke.Dataverse.IGenericEntityService entityService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("DailyBriefingEndpoints");

        var identity = await ResolveSystemUserIdAsync(entityService, httpContext, logger, "email", cancellationToken)
            .ConfigureAwait(false);
        if (identity.Failure is not null)
        {
            return identity.Failure;
        }

        // Recipient = the acting user (same claim cascade the Communication service uses
        // for its SendMode.User sender resolution).
        var recipientEmail = httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? httpContext.User?.FindFirst("preferred_username")?.Value
            ?? httpContext.User?.FindFirst("email")?.Value
            ?? httpContext.User?.FindFirst("upn")?.Value
            ?? httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.Upn)?.Value
            ?? httpContext.User?.FindFirst("unique_name")?.Value;
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Could not resolve the caller's email address from authentication claims.");
        }

        try
        {
            var response = await composite.EmailAsync(
                identity.SystemUserId, GetTenantId(httpContext), recipientEmail, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(response);
        }
        catch (FeatureDisabledException ex)
        {
            logger.LogDebug(
                "Daily briefing email called while AI feature disabled. ErrorCode={ErrorCode}", ex.ErrorCode);
            return ex.AsFeatureDisabled503();
        }
        catch (Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingDispatchUnconfiguredException ex)
        {
            logger.LogWarning(ex, "Daily briefing email dispatch unconfigured.");
            return Results.Problem(statusCode: 503, title: "Service Unavailable", detail: ex.Message);
        }
        catch (OpenAiCircuitBrokenException ex)
        {
            logger.LogWarning(
                "OpenAI circuit breaker open for daily briefing email. RetryAfter={RetryAfter}s",
                ex.RetryAfter.TotalSeconds);
            return ProblemDetailsHelper.AiUnavailable(
                "AI briefing service is temporarily unavailable.", httpContext.TraceIdentifier);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Daily briefing email failed for systemuserid {SystemUserId}", identity.SystemUserId);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to email daily briefing.");
        }
    }

    /// <summary>
    /// Shared caller-identity resolution for the /render and /email legs: AAD oid claim →
    /// Dataverse <c>systemuser.azureactivedirectoryobjectid</c> lookup. Returns either the
    /// resolved systemuserid or a ready-to-return ProblemDetails failure.
    /// </summary>
    private static async Task<(Guid SystemUserId, IResult? Failure)> ResolveSystemUserIdAsync(
        Spaarke.Dataverse.IGenericEntityService entityService,
        HttpContext httpContext,
        ILogger logger,
        string leg,
        CancellationToken cancellationToken)
    {
        var aadOidRaw = httpContext.User?.FindFirst("oid")?.Value
                     ?? httpContext.User?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        // Defense-in-depth (task-043 review): the oid claim is interpolated into FetchXML —
        // require a well-formed GUID even though AAD token validation guarantees it in practice.
        if (string.IsNullOrEmpty(aadOidRaw) || !Guid.TryParse(aadOidRaw, out var aadOidGuid))
        {
            logger.LogWarning("Daily briefing {Leg}: token has no valid AAD oid claim; cannot resolve systemuserid", leg);
            return (Guid.Empty, Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Caller AAD object id (oid claim) is required."));
        }
        var aadOid = aadOidGuid.ToString("D");

        try
        {
            var lookupFxml = $@"
                <fetch top=""1"">
                  <entity name=""systemuser"">
                    <attribute name=""systemuserid""/>
                    <attribute name=""fullname""/>
                    <filter>
                      <condition attribute=""azureactivedirectoryobjectid"" operator=""eq"" value=""{aadOid}""/>
                      <condition attribute=""isdisabled"" operator=""eq"" value=""0""/>
                    </filter>
                  </entity>
                </fetch>";
            var lookup = await entityService.RetrieveMultipleAsync(
                new Microsoft.Xrm.Sdk.Query.FetchExpression(lookupFxml), cancellationToken).ConfigureAwait(false);
            if (lookup.Entities.Count == 0)
            {
                logger.LogWarning("Daily briefing {Leg}: no systemuser found for AAD oid {AadOid}", leg, aadOid);
                return (Guid.Empty, Results.Problem(
                    statusCode: 403,
                    title: "Forbidden",
                    detail: "Caller is not a Dataverse user in this environment."));
            }
            return (lookup.Entities[0].GetAttributeValue<Guid>("systemuserid"), null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Daily briefing {Leg}: systemuser lookup failed for AAD oid {AadOid}", leg, aadOid);
            return (Guid.Empty, Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to resolve caller identity."));
        }
    }

    /// <summary>Tenant id from the caller's token (empty when absent — dev tolerance).</summary>
    private static string GetTenantId(HttpContext httpContext) =>
        httpContext.User?.FindFirst("tid")?.Value
        ?? httpContext.User?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
        ?? string.Empty;

    /// <summary>
    /// Generate a prioritized briefing summary from structured notification data.
    /// Uses non-streaming OpenAI completion (briefing is short, no need for SSE).
    /// </summary>
    private static async Task<IResult> Summarize(
        DailyBriefingSummaryRequest request,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        IBriefingAi? briefingAi = null)
    {
        var logger = loggerFactory.CreateLogger("DailyBriefingEndpoints");

        // Fail fast when AI is disabled — daily briefing has no non-AI fallback.
        if (briefingAi is null)
        {
            return Results.Problem(
                statusCode: 503,
                title: "Service Unavailable",
                detail: "Daily briefing requires AI features. Set 'Analysis:Enabled=true' AND 'DocumentIntelligence:Enabled=true' to enable.");
        }

        // Validate request has at least some data to summarize
        if (request.Categories.Length == 0 && request.PriorityItems.Length == 0)
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Request must include at least one category or priority item.");
        }

        logger.LogInformation(
            "Generating daily briefing summary: Categories={CategoryCount}, PriorityItems={PriorityCount}",
            request.Categories.Length, request.PriorityItems.Length);

        try
        {
            var prompt = BuildBriefingPrompt(request);

            var briefingText = await briefingAi.GenerateNarrativeAsync(
                prompt,
                maxOutputTokens: 300,
                cancellationToken: cancellationToken);

            logger.LogDebug(
                "Daily briefing generated: ResponseLength={Length}",
                briefingText.Length);

            return TypedResults.Ok(new DailyBriefingSummaryResponse
            {
                Briefing = briefingText.Trim(),
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                CategoryCount = request.Categories.Length,
                PriorityItemCount = request.PriorityItems.Length
            });
        }
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 2 (D-09 §2 L1): NullBriefingAi surfaced.
            logger.LogDebug(
                "Daily briefing summarize called while AI feature disabled. ErrorCode={ErrorCode}",
                ex.ErrorCode);
            return ex.AsFeatureDisabled503();
        }
        catch (OpenAiCircuitBrokenException ex)
        {
            logger.LogWarning(
                "OpenAI circuit breaker open for daily briefing. RetryAfter={RetryAfter}s",
                ex.RetryAfter.TotalSeconds);

            return ProblemDetailsHelper.AiUnavailable(
                "AI briefing service is temporarily unavailable.",
                httpContext.TraceIdentifier);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate daily briefing summary");

            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to generate daily briefing summary.");
        }
    }

    /// <summary>
    /// Build a structured prompt for the briefing summarizer.
    /// Instructs the model to produce a concise 3-4 sentence prioritized narrative.
    /// </summary>
    internal static string BuildBriefingPrompt(DailyBriefingSummaryRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a concise executive assistant. Summarize the user's daily notifications into a prioritized briefing of 3-4 sentences.");
        sb.AppendLine("Focus on what requires immediate attention first, then provide context on volume and trends.");
        sb.AppendLine("Do NOT use bullet points. Write in natural prose. Be specific about counts and categories.");
        sb.AppendLine();
        sb.AppendLine("=== Notification Summary ===");

        if (request.Categories.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Categories:");
            foreach (var cat in request.Categories)
            {
                sb.AppendLine($"- {cat.Name}: {cat.Count} notification(s){(cat.UnreadCount > 0 ? $" ({cat.UnreadCount} unread)" : "")}");
            }
        }

        if (request.PriorityItems.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Top Priority Items:");
            foreach (var item in request.PriorityItems)
            {
                sb.AppendLine($"- [{item.Category}] {item.Title}{(item.DueDate.HasValue ? $" (due {item.DueDate.Value:yyyy-MM-dd})" : "")}");
            }
        }

        if (request.TotalNotificationCount > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Total notifications: {request.TotalNotificationCount}");
        }

        sb.AppendLine();
        sb.AppendLine("Write a 3-4 sentence briefing:");

        return sb.ToString();
    }

    /// <summary>
    /// Generate a narrative briefing with TL;DR and per-channel narrative bullets.
    /// FR-P3-04 (spaarke-ai-architecture-redesign-r1 task 043) HARD CUTOVER: the R4
    /// playbook-engine dispatch AND the R7 narrator parallel-run feature flag are
    /// DELETED (NFR-08). Dispatch is decided by the Binding table
    /// only (ADR-039): <see cref="Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingCompositeService"/>
    /// resolves <c>daily-briefing-narrate/default</c> and executes its coded workflow,
    /// writing the ledger entries before this endpoint renders (ADR-040). All prompt
    /// content stays hot-editable in the BRIEF-NARRATE-* Action rows.
    /// </summary>
    /// <remarks>
    /// Response shape (<see cref="DailyBriefingNarrateResponse"/>) is preserved for
    /// backward compatibility — the widget parser at <c>useBriefingNarration.ts</c>
    /// consumes the exact same JSON shape (R3 contract; AC-12b binding).
    /// </remarks>
    private static async Task<IResult> HandleNarrate(
        DailyBriefingNarrateRequest request,
        ILoggerFactory loggerFactory,
        Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingCompositeService composite,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("DailyBriefingEndpoints");

        // Empty-payload tolerance: the frontend `useDailyBriefing` hook may send a request
        // with all collections empty when the user has no notifications, no priority items,
        // and no channel content to narrate (e.g., fresh inbox, no overdue work).
        // Treat this as a normal "nothing to narrate" condition and return 200 with an empty
        // bullets/channels response — the client renders an empty state (per FR-16 /
        // task 035 graceful-empty UX). Returning 400 here would force the hook into its
        // 400-special-case branch and surface as a misleading "Bad Request" in App Insights.
        // This branch MUST short-circuit BEFORE playbook dispatch so we never burn an LLM
        // call when there is nothing to narrate.
        if (request.Categories.Length == 0 && request.PriorityItems.Length == 0 && request.Channels.Length == 0)
        {
            logger.LogInformation(
                "Empty narrate request — returning empty bullets (no notifications to narrate).");

            return TypedResults.Ok(new DailyBriefingNarrateResponse
            {
                Tldr = new TldrResult
                {
                    Summary = string.Empty,
                    KeyTakeaways = [],
                    TopAction = string.Empty,
                    CategoryCount = 0,
                    PriorityItemCount = 0
                },
                ChannelNarratives = [],
                GeneratedAtUtc = DateTimeOffset.UtcNow
            });
        }

        try
        {
            // FR-P3-04 HARD CUTOVER (NFR-08): the Binding decides — coded composite only.
            // The R4 playbook-engine dispatch (the since-deleted generic facade + result projection) and
            // the R7 narrator feature flag were DELETED by task 043; there is no fallback.
            logger.LogInformation(
                "Dispatching daily briefing narration via coded composite: Categories={CategoryCount}, PriorityItems={PriorityCount}, Channels={ChannelCount}",
                request.Categories.Length, request.PriorityItems.Length, request.Channels.Length);

            var response = await composite.NarrateAsync(
                request, GetTenantId(httpContext), cancellationToken).ConfigureAwait(false);

            return TypedResults.Ok(response);
        }
        catch (FeatureDisabledException ex)
        {
            // P3 Fail-Fast (ADR-032 Null-Object peers): AI kill-switch is OFF.
            logger.LogDebug(
                "Daily briefing narrate called while AI feature disabled. ErrorCode={ErrorCode}",
                ex.ErrorCode);
            return ex.AsFeatureDisabled503();
        }
        catch (Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingDispatchUnconfiguredException ex)
        {
            // Service-availability fail-fast: no Binding row → dispatch unconfigured.
            logger.LogWarning(
                "No sprk_playbookconsumer row matched for {ConsumerType} — daily briefing dispatch unconfigured.",
                ConsumerTypes.DailyBriefingNarrate);
            return Results.Problem(
                statusCode: 503,
                title: "Service Unavailable",
                detail: ex.Message);
        }
        catch (OpenAiCircuitBrokenException ex)
        {
            logger.LogWarning(
                "OpenAI circuit breaker open for daily briefing narration. RetryAfter={RetryAfter}s",
                ex.RetryAfter.TotalSeconds);

            return ProblemDetailsHelper.AiUnavailable(
                "AI briefing service is temporarily unavailable.",
                httpContext.TraceIdentifier);
        }
        catch (OperationCanceledException)
        {
            // Caller cancellation — propagate cleanly without logging as error.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to dispatch daily briefing narration");

            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to generate daily briefing narration.");
        }
    }

    // ────────────────────────────────────────────────────────────────
    // FR-P3-04 (task 043): playbook-engine dispatch REMOVED.
    //
    // Prior implementations of the `/narrate` engine-default path
    // (the playbook-result → response projection helper, its serializer options, the
    // generic-facade invocation + parameter serialization) and the R7
    // R7 narrator feature-flag branch previously lived here.
    // Dispatch is now decided by the Binding table only (ADR-039); execution runs
    // through DailyBriefingCompositeService → ICodedWorkflow (task-007 convention)
    // → IOutputRouter (ledger write-before-render, ADR-040). Prompt content stays
    // hot-editable in the BRIEF-NARRATE-* Action rows read by DailyBriefingNarrator.
    // ────────────────────────────────────────────────────────────────
}

// ────────────────────────────────────────────────────────────────
// Request / Response DTOs
// ────────────────────────────────────────────────────────────────

/// <summary>
/// Request DTO for daily briefing summarization.
/// Contains structured notification data for AI summarization.
/// </summary>
public record DailyBriefingSummaryRequest
{
    /// <summary>Notification counts grouped by category (e.g., "Tasks Overdue", "New Documents").</summary>
    [JsonPropertyName("categories")]
    public NotificationCategoryDto[] Categories { get; init; } = [];

    /// <summary>Top priority items that need immediate attention.</summary>
    [JsonPropertyName("priorityItems")]
    public PriorityItemDto[] PriorityItems { get; init; } = [];

    /// <summary>Total notification count across all categories.</summary>
    [JsonPropertyName("totalNotificationCount")]
    public int TotalNotificationCount { get; init; }
}

/// <summary>
/// A notification category with count and unread count.
/// </summary>
public record NotificationCategoryDto
{
    /// <summary>Category display name (e.g., "Tasks Overdue", "New Documents").</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Total notification count in this category.</summary>
    [JsonPropertyName("count")]
    public int Count { get; init; }

    /// <summary>Number of unread notifications in this category.</summary>
    [JsonPropertyName("unreadCount")]
    public int UnreadCount { get; init; }
}

/// <summary>
/// A high-priority notification item requiring attention.
/// </summary>
public record PriorityItemDto
{
    /// <summary>Category this item belongs to (e.g., "Tasks", "Events").</summary>
    [JsonPropertyName("category")]
    public required string Category { get; init; }

    /// <summary>Brief title/description of the priority item.</summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>Optional due date for time-sensitive items.</summary>
    [JsonPropertyName("dueDate")]
    public DateTimeOffset? DueDate { get; init; }
}

/// <summary>
/// Response DTO containing the AI-generated briefing.
/// </summary>
public record DailyBriefingSummaryResponse
{
    /// <summary>AI-generated 3-4 sentence prioritized briefing narrative.</summary>
    [JsonPropertyName("briefing")]
    public required string Briefing { get; init; }

    /// <summary>UTC timestamp when the briefing was generated.</summary>
    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; }

    /// <summary>Number of categories that were summarized.</summary>
    [JsonPropertyName("categoryCount")]
    public int CategoryCount { get; init; }

    /// <summary>Number of priority items that were included.</summary>
    [JsonPropertyName("priorityItemCount")]
    public int PriorityItemCount { get; init; }
}

// ────────────────────────────────────────────────────────────────
// Narrate Request / Response DTOs
// ────────────────────────────────────────────────────────────────

/// <summary>
/// Request DTO for daily briefing narration.
/// Contains structured notification data plus per-channel items for narrative generation.
/// </summary>
public record DailyBriefingNarrateRequest
{
    /// <summary>Notification counts grouped by category.</summary>
    [JsonPropertyName("categories")]
    public NotificationCategoryDto[] Categories { get; init; } = [];

    /// <summary>Top priority items that need immediate attention.</summary>
    [JsonPropertyName("priorityItems")]
    public PriorityItemDto[] PriorityItems { get; init; } = [];

    /// <summary>Total notification count across all categories.</summary>
    [JsonPropertyName("totalNotificationCount")]
    public int TotalNotificationCount { get; init; }

    /// <summary>Per-channel notification items for narrative generation.</summary>
    [JsonPropertyName("channels")]
    public ChannelNarrationInput[] Channels { get; init; } = [];

    /// <summary>
    /// R5 task 013 (FR-A4): deterministically-computed TL;DR scaffolding — the ONLY ground-
    /// truth facts (counts, dates, record names) the TL;DR LLM call may assert. Computed by
    /// <see cref="Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingCollector.BuildTldrFacts"/>
    /// from Categories/PriorityItems/Channels/TotalNotificationCount above — never by the LLM.
    /// Optional on the wire (nullable) for backward compatibility with callers that supply the
    /// legacy request shape without it (e.g. the direct <c>/narrate</c> leg); <see
    /// cref="Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingNarrator"/> computes it
    /// deterministically as a fallback when absent — still pure C#, never delegated to the LLM.
    /// </summary>
    [JsonPropertyName("tldrFacts")]
    public TldrFactsDto? TldrFacts { get; init; }
}

/// <summary>
/// Input for a single notification channel containing items to narrate.
/// </summary>
public record ChannelNarrationInput
{
    /// <summary>Channel category key (e.g., "tasks", "documents").</summary>
    [JsonPropertyName("category")]
    public string Category { get; init; } = "";

    /// <summary>Human-readable channel label (e.g., "Tasks Overdue").</summary>
    [JsonPropertyName("label")]
    public string Label { get; init; } = "";

    /// <summary>Individual notification items in this channel.</summary>
    [JsonPropertyName("items")]
    public ChannelItemDto[] Items { get; init; } = [];
}

/// <summary>
/// A single notification item within a channel.
/// </summary>
public record ChannelItemDto
{
    /// <summary>Unique identifier for this item.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    /// <summary>Item title/subject line.</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    /// <summary>Item body/description text.</summary>
    [JsonPropertyName("body")]
    public string Body { get; init; } = "";

    /// <summary>Priority level: "normal", "high", "urgent".</summary>
    [JsonPropertyName("priority")]
    public string Priority { get; init; } = "normal";

    /// <summary>Name of the related/regarding entity.</summary>
    [JsonPropertyName("regardingName")]
    public string RegardingName { get; init; } = "";

    /// <summary>Entity type of the related record (e.g., "sprk_matter").</summary>
    [JsonPropertyName("regardingEntityType")]
    public string RegardingEntityType { get; init; } = "";

    /// <summary>Unique identifier of the related record.</summary>
    [JsonPropertyName("regardingId")]
    public string RegardingId { get; init; } = "";

    /// <summary>
    /// Source entity type for this item (e.g., "sprk_event", "sprk_document",
    /// "sprk_matter", "sprk_project", "sprk_todo"). Added by R7 Wave 12 task 135
    /// so EnrichBulletWithEntityRefs can fall back to the source entity for
    /// click-through navigation when the item has no regarding-matter populated
    /// (orphan items still need a working link to the source record).
    /// </summary>
    [JsonPropertyName("sourceEntityType")]
    public string SourceEntityType { get; init; } = "";

    /// <summary>ISO 8601 timestamp when the item was created.</summary>
    [JsonPropertyName("createdOn")]
    public string CreatedOn { get; init; } = "";
}

/// <summary>
/// Deterministically-computed ground-truth facts for the TL;DR LLM call (R5 task 013, FR-A4).
/// Every field is computed in C# from source records — the LLM composes prose and prioritizes
/// OVER these facts; it must never invent a count, date, or name absent from this payload. This
/// IS the input payload the TL;DR call receives (replaces the earlier raw
/// categories/priorityItems/channels dump — see <see
/// cref="Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingCollector.BuildTldrFacts"/>). See
/// docs/architecture/SPAARKE-PLAYBOOK-LLM-OUTPUT-PATTERN.md for the two-layer pattern this
/// implements (Layer 1 = this scaffolding; Layer 2 = the "## Input" prompt section).
/// </summary>
public sealed record TldrFactsDto
{
    /// <summary>Total notification count across all channels — the "you have N ..." figure.</summary>
    [JsonPropertyName("totalNotificationCount")]
    public int TotalNotificationCount { get; init; }

    /// <summary>Per-category notification counts (same values as the request's top-level Categories field — aggregated, not itemized).</summary>
    [JsonPropertyName("categoryCounts")]
    public NotificationCategoryDto[] CategoryCounts { get; init; } = [];

    /// <summary>Count of priority (most-urgent) items surfaced to the briefing.</summary>
    [JsonPropertyName("priorityItemCount")]
    public int PriorityItemCount { get; init; }

    /// <summary>Bounded set of labeled dates (record name + date) the TL;DR may reference for urgency framing. Sourced from PriorityItems' due dates only — already curated to the most urgent items.</summary>
    [JsonPropertyName("keyDates")]
    public TldrKeyDateDto[] KeyDates { get; init; } = [];

    /// <summary>
    /// Bounded, deduplicated set of record names the TL;DR is permitted to reference by name.
    /// Capped (see <see cref="Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingCollector.TldrFactsMaxRecordNames"/>)
    /// so a large channel does not dump every record into the TL;DR call's token budget
    /// (ADR-015 data-minimization / aggregation).
    /// </summary>
    [JsonPropertyName("recordNames")]
    public string[] RecordNames { get; init; } = [];
}

/// <summary>A single labeled due-date fact: which record, and when it's due.</summary>
public sealed record TldrKeyDateDto
{
    /// <summary>Name of the record the date belongs to (verbatim from the source record).</summary>
    [JsonPropertyName("recordName")]
    public string RecordName { get; init; } = "";

    /// <summary>The due date itself.</summary>
    [JsonPropertyName("date")]
    public DateTimeOffset Date { get; init; }
}

/// <summary>
/// Response DTO containing the AI-generated narrative briefing with per-channel detail.
/// </summary>
public record DailyBriefingNarrateResponse
{
    /// <summary>TL;DR executive summary (2-3 sentences + 3-5 key-takeaway bullets + top action).</summary>
    [JsonPropertyName("tldr")]
    public TldrResult Tldr { get; init; } = new();

    /// <summary>Per-channel narrative bullet results.</summary>
    [JsonPropertyName("channelNarratives")]
    public ChannelNarrationResult[] ChannelNarratives { get; init; } = [];

    /// <summary>UTC timestamp when the narration was generated.</summary>
    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; }

    /// <summary>
    /// R7 W12 feedback item 9 (2026-07-01) — HIGH PRIORITY items across the 7 flagged
    /// entities (sprk_matter, sprk_project, sprk_invoice, sprk_document,
    /// sprk_workassignment, sprk_event, sprk_todo). Populated by
    /// <c>DailyBriefingCollector.CollectHighPriorityAsync</c>, bypasses the narrator
    /// (no LLM call — plain structured list). Widget renders a subtle red banner section
    /// above the TL;DR when this array is non-empty. Ordered by due date ascending (undated
    /// items last). Empty array = no flagged items, widget hides the section.
    /// </summary>
    [JsonPropertyName("highPriorityItems")]
    public HighPriorityItemDto[] HighPriorityItems { get; init; } = [];

    /// <summary>
    /// Optional sidecar with post-LLM entity-name validation metadata. Added by R7
    /// Wave 11 narrator spike (2026-06-30) to mirror the original playbook design's
    /// <c>_validationMetadata</c> responseBinding. Null when no scrubbing occurred
    /// (i.e., the LLM emitted no hallucinated entity names — the common happy path).
    /// Widget treats this as optional observability metadata, not user-visible content.
    /// </summary>
    [JsonPropertyName("_validationMetadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValidationMetadataDto? ValidationMetadata { get; init; }
}

/// <summary>
/// A high-priority item from one of the 7 flagged entities. Assembled directly by
/// <c>DailyBriefingCollector.CollectHighPriorityAsync</c> — no LLM narration. Widget
/// renders as a compact list of clickable record refs with optional due-date badge.
/// </summary>
public record HighPriorityItemDto
{
    /// <summary>Dataverse logical name of the source entity (sprk_matter, sprk_event, etc.).</summary>
    [JsonPropertyName("entityType")]
    public string EntityType { get; init; } = "";

    /// <summary>GUID of the source record — used by widget's Xrm.Navigation modal open.</summary>
    [JsonPropertyName("entityId")]
    public string EntityId { get; init; } = "";

    /// <summary>Display name of the source record (primary name field per entity).</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>
    /// Due date (ISO 8601) when the entity has a meaningful due-date field. Omitted (null)
    /// for entities without a due date (matter, project, document, invoice). Widget uses
    /// this to render an overdue/due-today badge next to the entry.
    /// </summary>
    [JsonPropertyName("dueDate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? DueDate { get; init; }

    /// <summary>True if <c>sprk_highpriority</c> is Yes on the source record.</summary>
    [JsonPropertyName("highPriority")]
    public bool HighPriority { get; init; }

    /// <summary>True if <c>sprk_monitor</c> is Yes on the source record.</summary>
    [JsonPropertyName("monitor")]
    public bool Monitor { get; init; }

    /// <summary>
    /// Optional short entity-label the widget can render next to the name (e.g., "Matter",
    /// "Project", "Task"). Server-side rendering — widget just prints. Empty when the
    /// entity's kind is implicit from the parent context.
    /// </summary>
    [JsonPropertyName("kindLabel")]
    public string KindLabel { get; init; } = "";

    /// <summary>
    /// R7 W12 feedback (2026-07-01) — description / subject text from the source record.
    /// Empty when the entity has no description field or the field is blank. Widget
    /// truncates for compact display.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    /// <summary>
    /// R7 W12 feedback (2026-07-01) — computed action classification for the "action"
    /// column: <c>"Overdue"</c> / <c>"DueToday"</c> / <c>"DueSoon"</c> / <c>"Recent"</c> /
    /// <c>"None"</c>. Derived server-side from due-date proximity or modifiedon recency.
    /// Widget renders as a badge with intent color.
    /// </summary>
    [JsonPropertyName("action")]
    public string Action { get; init; } = "None";

    /// <summary>
    /// R7 W12 feedback (2026-07-01) — reason the item appears in High Priority. One of:
    /// <c>"HighPriority"</c> / <c>"Monitor"</c> / <c>"Both"</c>. Widget renders as a
    /// short "flag" hint so operator sees WHY each record is here.
    /// </summary>
    [JsonPropertyName("reason")]
    public string Reason { get; init; } = "";

    /// <summary>
    /// R7 W12 feedback (2026-07-01) — modifiedon timestamp for the "Recent" action fallback.
    /// Kept as JSON only; widget uses to compute relative age when action is Recent.
    /// </summary>
    [JsonPropertyName("modifiedOn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ModifiedOn { get; init; }
}

/// <summary>
/// Post-LLM entity-name validation outcome sidecar. Emitted on the narrate response only
/// when the scrubber removed one or more proper-noun spans not present in the allow-list.
/// </summary>
public record ValidationMetadataDto
{
    /// <summary>Post-scrub text (sentence-aggregate after hallucinated proper-noun sentences removed).</summary>
    [JsonPropertyName("scrubbedText")]
    public string ScrubbedText { get; init; } = string.Empty;

    /// <summary>Proper-noun spans that were not in the allow-list and were stripped.</summary>
    [JsonPropertyName("removedTerms")]
    public string[] RemovedTerms { get; init; } = [];
}

/// <summary>
/// TL;DR executive summary with key takeaways and top action identification.
/// R2.2: switched from a single 5-7 sentence narrative to a structured shape —
/// a 2-3 sentence executive summary + 3-5 key-takeaway bullets — so the client
/// can render an at-a-glance scannable summary instead of a paragraph block.
/// </summary>
public record TldrResult
{
    /// <summary>AI-generated 2-3 sentence executive summary.</summary>
    [JsonPropertyName("summary")]
    public string Summary { get; init; } = "";

    /// <summary>AI-generated 3-5 short key-takeaway bullet strings (no leading "- ").</summary>
    [JsonPropertyName("keyTakeaways")]
    public string[] KeyTakeaways { get; init; } = [];

    /// <summary>The single most important action for today.</summary>
    [JsonPropertyName("topAction")]
    public string TopAction { get; init; } = "";

    /// <summary>Number of categories that were summarized.</summary>
    [JsonPropertyName("categoryCount")]
    public int CategoryCount { get; init; }

    /// <summary>Number of priority items that were included.</summary>
    [JsonPropertyName("priorityItemCount")]
    public int PriorityItemCount { get; init; }

    /// <summary>
    /// R5 task 014 (FR-A5): anchor-to-item grounding for the TL;DR. Every named anchor
    /// (a record/matter/party name the LLM chose to call out in <see cref="Summary"/>,
    /// <see cref="KeyTakeaways"/>, or <see cref="TopAction"/>) MUST carry an <see
    /// cref="TldrItemRefDto.ItemId"/> pointing at a real source item. Resolution is BINARY —
    /// the widget resolves each <c>itemId</c> against the request's <c>items[]</c>
    /// (<c>ChannelItemDto.Id</c>) and DROPS any anchor that doesn't resolve (renders the
    /// anchor text as plain, unlinked prose — no residue, no warning). There is deliberately
    /// NO groundedness-score threshold and NO warn/withhold band (FR-A6 locks this posture);
    /// existence-by-itemId is the only signal. Empty array is valid (no anchors named — the
    /// common case for terse summaries) and is NOT itself a signal of low quality.
    /// </summary>
    [JsonPropertyName("itemRefs")]
    public TldrItemRefDto[] ItemRefs { get; init; } = [];
}

/// <summary>
/// A single TL;DR anchor-to-item grounding entry (R5 task 014, FR-A5). Pairs a verbatim text
/// span the TL;DR named (<see cref="AnchorText"/>) with the source item it claims to reference
/// (<see cref="ItemId"/> — a <c>ChannelItemDto.Id</c> from the same narrate request). The widget
/// is the sole enforcement point: it looks up <see cref="ItemId"/> against the items it has
/// available and links <see cref="AnchorText"/> (when found verbatim in the TL;DR text) to that
/// item's record — or drops the anchor entirely when the lookup misses. No probabilistic score
/// is attached to this DTO; resolution is exists-or-doesn't.
/// </summary>
public sealed record TldrItemRefDto
{
    /// <summary>
    /// The exact text span (record/party/matter name) the TL;DR named in <see
    /// cref="TldrResult.Summary"/>, one of <see cref="TldrResult.KeyTakeaways"/>, or <see
    /// cref="TldrResult.TopAction"/>. Matched case-insensitively against the TL;DR text by the
    /// widget (mirrors the <c>NarrativeCitedText.buildSegments</c> matching rule).
    /// </summary>
    [JsonPropertyName("anchorText")]
    public string AnchorText { get; init; } = "";

    /// <summary>
    /// The source item id (<c>ChannelItemDto.Id</c>) this anchor claims to reference. MUST
    /// resolve against the narrate request's <c>items[]</c> or the widget drops the anchor.
    /// </summary>
    [JsonPropertyName("itemId")]
    public string ItemId { get; init; } = "";
}

/// <summary>
/// Narrative result for a single notification channel.
/// </summary>
public record ChannelNarrationResult
{
    /// <summary>Channel category key matching the input.</summary>
    [JsonPropertyName("category")]
    public string Category { get; init; } = "";

    /// <summary>Grouped narrative bullets for this channel.</summary>
    [JsonPropertyName("bullets")]
    public NarrativeBulletDto[] Bullets { get; init; } = [];
}

/// <summary>
/// A single narrative bullet grouping one or more notification items.
/// </summary>
public record NarrativeBulletDto
{
    /// <summary>Natural prose narrative describing the grouped items.</summary>
    [JsonPropertyName("narrative")]
    public string Narrative { get; init; } = "";

    /// <summary>IDs of the original items grouped into this bullet.</summary>
    [JsonPropertyName("itemIds")]
    public string[] ItemIds { get; init; } = [];

    /// <summary>Entity type of the primary related record.</summary>
    [JsonPropertyName("primaryEntityType")]
    public string PrimaryEntityType { get; init; } = "";

    /// <summary>Unique identifier of the primary related record.</summary>
    [JsonPropertyName("primaryEntityId")]
    public string PrimaryEntityId { get; init; } = "";

    /// <summary>Display name of the primary related record.</summary>
    [JsonPropertyName("primaryEntityName")]
    public string PrimaryEntityName { get; init; } = "";

    /// <summary>
    /// R7 W12 feedback items 2/3/4 (2026-07-01): per-bullet entity references
    /// for widget-side citation rendering. Each entry maps to a specific record
    /// referenced by this bullet. Ordered by narrative appearance when possible.
    /// The widget renders these two ways:
    /// - Entries with <c>Mentioned=true</c>: replace <c>Name</c> in narrative
    ///   text with a clickable Link that opens the record modal.
    /// - Entries with <c>Mentioned=false</c>: append trailing <c>[N]</c>
    ///   superscript citations after the narrative text.
    /// Empty array = plain-text bullet (no interactive citations).
    /// </summary>
    [JsonPropertyName("references")]
    public NarrativeBulletReferenceDto[] References { get; init; } = [];
}

/// <summary>
/// A single reference from a narrative bullet to a Dataverse record. Drives the
/// widget's inline hyperlink / trailing citation rendering. Ordered by narrative
/// appearance when possible (post-processor scans narrative text left-to-right).
/// </summary>
public record NarrativeBulletReferenceDto
{
    /// <summary>1-based citation index for trailing <c>[N]</c> refs.</summary>
    [JsonPropertyName("index")]
    public int Index { get; init; }

    /// <summary>Dataverse logical name of the target entity (e.g., "sprk_matter").</summary>
    [JsonPropertyName("entityType")]
    public string EntityType { get; init; } = "";

    /// <summary>GUID of the target record.</summary>
    [JsonPropertyName("entityId")]
    public string EntityId { get; init; } = "";

    /// <summary>Display name of the target record.</summary>
    [JsonPropertyName("entityName")]
    public string EntityName { get; init; } = "";

    /// <summary>
    /// True if <c>EntityName</c> appears in the narrative text (widget replaces
    /// the name span with a clickable Link). False if the reference is only
    /// implicit (widget renders as trailing <c>[N]</c> citation).
    /// </summary>
    [JsonPropertyName("mentioned")]
    public bool Mentioned { get; init; }
}
