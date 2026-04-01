using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Ai;

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

        return app;
    }

    /// <summary>
    /// Generate a prioritized briefing summary from structured notification data.
    /// Uses non-streaming OpenAI completion (briefing is short, no need for SSE).
    /// </summary>
    private static async Task<IResult> Summarize(
        DailyBriefingSummaryRequest request,
        IOpenAiClient openAiClient,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("DailyBriefingEndpoints");

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

            var briefingText = await openAiClient.GetCompletionAsync(
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
    /// Fires all AI prompts in parallel (TL;DR + each channel) via Task.WhenAll.
    /// </summary>
    private static async Task<IResult> HandleNarrate(
        DailyBriefingNarrateRequest request,
        IOpenAiClient openAiClient,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("DailyBriefingEndpoints");

        // Validate request has at least some data to narrate
        if (request.Categories.Length == 0 && request.PriorityItems.Length == 0 && request.Channels.Length == 0)
        {
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "Request must include at least one category, priority item, or channel.");
        }

        logger.LogInformation(
            "Generating daily briefing narration: Categories={CategoryCount}, PriorityItems={PriorityCount}, Channels={ChannelCount}",
            request.Categories.Length, request.PriorityItems.Length, request.Channels.Length);

        try
        {
            // Fire TL;DR prompt and all channel narration prompts in parallel
            var tldrTask = GetTldrAsync(request, openAiClient, logger, cancellationToken);

            var channelTasks = request.Channels.Select(channel =>
                GetChannelNarrationAsync(channel, openAiClient, logger, cancellationToken));

            var allTasks = new List<Task> { tldrTask };
            var channelTaskList = channelTasks.ToList();
            allTasks.AddRange(channelTaskList);

            await Task.WhenAll(allTasks);

            var tldrResult = await tldrTask;
            var channelResults = new List<ChannelNarrationResult>();
            foreach (var ct in channelTaskList)
            {
                var result = await ct;
                if (result is not null)
                {
                    channelResults.Add(result);
                }
            }

            logger.LogDebug(
                "Daily briefing narration generated: Channels={SuccessCount}/{TotalCount}",
                channelResults.Count, request.Channels.Length);

            return TypedResults.Ok(new DailyBriefingNarrateResponse
            {
                Tldr = tldrResult,
                ChannelNarratives = channelResults.ToArray(),
                GeneratedAtUtc = DateTimeOffset.UtcNow
            });
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate daily briefing narration");

            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to generate daily briefing narration.");
        }
    }

    /// <summary>
    /// Generate the TL;DR briefing (5-7 sentences) with top action identification.
    /// </summary>
    private static async Task<TldrResult> GetTldrAsync(
        DailyBriefingNarrateRequest request,
        IOpenAiClient openAiClient,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var prompt = BuildNarrateTldrPrompt(request);

        var briefingText = await openAiClient.GetCompletionAsync(
            prompt,
            maxOutputTokens: 500,
            cancellationToken: cancellationToken);

        // Extract top action from the briefing text (last sentence starting with "Your most important action today is...")
        var trimmed = briefingText.Trim();
        var topAction = "";
        var sentences = trimmed.Split(new[] { ". " }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var sentence in sentences)
        {
            if (sentence.TrimStart().StartsWith("Your most important action today is", StringComparison.OrdinalIgnoreCase))
            {
                topAction = sentence.TrimStart();
                if (!topAction.EndsWith('.'))
                    topAction += ".";
                break;
            }
        }

        return new TldrResult
        {
            Briefing = trimmed,
            TopAction = topAction,
            CategoryCount = request.Categories.Length,
            PriorityItemCount = request.PriorityItems.Length
        };
    }

    /// <summary>
    /// Generate narrative bullets for a single channel.
    /// Returns null if the AI call fails due to circuit breaker.
    /// </summary>
    private static async Task<ChannelNarrationResult?> GetChannelNarrationAsync(
        ChannelNarrationInput channel,
        IOpenAiClient openAiClient,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (channel.Items.Length == 0)
        {
            return new ChannelNarrationResult
            {
                Category = channel.Category,
                Bullets = []
            };
        }

        try
        {
            var prompt = BuildChannelNarrationPrompt(channel);

            var responseJson = await openAiClient.GetCompletionAsync(
                prompt,
                maxOutputTokens: 300,
                cancellationToken: cancellationToken);

            var bullets = ParseChannelBullets(responseJson, logger);

            return new ChannelNarrationResult
            {
                Category = channel.Category,
                Bullets = bullets
            };
        }
        catch (OpenAiCircuitBrokenException ex)
        {
            logger.LogWarning(
                "OpenAI circuit breaker open for channel narration. Channel={Channel}, RetryAfter={RetryAfter}s",
                channel.Category, ex.RetryAfter.TotalSeconds);
            return null;
        }
    }

    /// <summary>
    /// Build the TL;DR prompt for narrate endpoint.
    /// Instructs the model to produce a 5-7 sentence briefing ending with the most important action.
    /// </summary>
    internal static string BuildNarrateTldrPrompt(DailyBriefingNarrateRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a concise executive assistant. Summarize the user's daily notifications into a prioritized briefing of 5-7 sentences.");
        sb.AppendLine("Focus on what requires immediate attention first, then provide context on volume and trends.");
        sb.AppendLine("Do NOT use bullet points. Write in natural prose. Be specific about counts and categories.");
        sb.AppendLine("End with a sentence identifying the single most important action for today, starting with 'Your most important action today is...'");
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
        sb.AppendLine("Write a 5-7 sentence briefing:");

        return sb.ToString();
    }

    /// <summary>
    /// Build a prompt for narrating a single channel's items into grouped narrative bullets.
    /// </summary>
    internal static string BuildChannelNarrationPrompt(ChannelNarrationInput channel)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"You are a concise executive assistant. Convert the following '{channel.Label}' notification items into 1-4 narrative bullet points.");
        sb.AppendLine("Rules:");
        sb.AppendLine("- Group related items (e.g., multiple documents on the same matter → single bullet)");
        sb.AppendLine("- Write each bullet in natural prose (not a raw title)");
        sb.AppendLine("- Include entity names and dates where available");
        sb.AppendLine("- Lead with the most urgent item");
        sb.AppendLine("- Return ONLY a JSON array (no markdown, no code fences). Each element must have:");
        sb.AppendLine("  { \"narrative\": \"...\", \"itemIds\": [\"id1\", ...], \"primaryEntityType\": \"...\", \"primaryEntityId\": \"...\", \"primaryEntityName\": \"...\" }");
        sb.AppendLine();
        sb.AppendLine($"=== {channel.Label} Items ===");
        sb.AppendLine();

        foreach (var item in channel.Items)
        {
            var parts = new List<string> { item.Title };
            if (!string.IsNullOrEmpty(item.Body))
                parts.Add(item.Body);
            if (!string.IsNullOrEmpty(item.RegardingName))
                parts.Add($"regarding: {item.RegardingName} ({item.RegardingEntityType})");
            if (!string.IsNullOrEmpty(item.CreatedOn))
                parts.Add($"date: {item.CreatedOn}");
            if (item.Priority != "normal")
                parts.Add($"priority: {item.Priority}");

            sb.AppendLine($"- [id={item.Id}] {string.Join(" | ", parts)}");
        }

        sb.AppendLine();
        sb.AppendLine("JSON array:");

        return sb.ToString();
    }

    /// <summary>
    /// Parse the AI response JSON into NarrativeBulletDto array.
    /// Handles potential JSON extraction from markdown code fences.
    /// </summary>
    private static NarrativeBulletDto[] ParseChannelBullets(string responseJson, ILogger logger)
    {
        var json = responseJson.Trim();

        // Strip markdown code fences if present
        if (json.StartsWith("```"))
        {
            var firstNewline = json.IndexOf('\n');
            if (firstNewline > 0)
                json = json[(firstNewline + 1)..];
            if (json.EndsWith("```"))
                json = json[..^3];
            json = json.Trim();
        }

        try
        {
            var bullets = JsonSerializer.Deserialize<NarrativeBulletDto[]>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return bullets ?? [];
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse channel narration JSON. Response={Response}", json);
            return [];
        }
    }
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

    /// <summary>ISO 8601 timestamp when the item was created.</summary>
    [JsonPropertyName("createdOn")]
    public string CreatedOn { get; init; } = "";
}

/// <summary>
/// Response DTO containing the AI-generated narrative briefing with per-channel detail.
/// </summary>
public record DailyBriefingNarrateResponse
{
    /// <summary>TL;DR executive summary (5-7 sentences).</summary>
    [JsonPropertyName("tldr")]
    public TldrResult Tldr { get; init; } = new();

    /// <summary>Per-channel narrative bullet results.</summary>
    [JsonPropertyName("channelNarratives")]
    public ChannelNarrationResult[] ChannelNarratives { get; init; } = [];

    /// <summary>UTC timestamp when the narration was generated.</summary>
    [JsonPropertyName("generatedAtUtc")]
    public DateTimeOffset GeneratedAtUtc { get; init; }
}

/// <summary>
/// TL;DR executive summary with top action identification.
/// </summary>
public record TldrResult
{
    /// <summary>AI-generated 5-7 sentence prioritized briefing narrative.</summary>
    [JsonPropertyName("briefing")]
    public string Briefing { get; init; } = "";

    /// <summary>The single most important action for today, extracted from the briefing.</summary>
    [JsonPropertyName("topAction")]
    public string TopAction { get; init; } = "";

    /// <summary>Number of categories that were summarized.</summary>
    [JsonPropertyName("categoryCount")]
    public int CategoryCount { get; init; }

    /// <summary>Number of priority items that were included.</summary>
    [JsonPropertyName("priorityItemCount")]
    public int PriorityItemCount { get; init; }
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
}
