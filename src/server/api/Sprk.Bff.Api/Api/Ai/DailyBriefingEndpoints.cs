using System.Text;
using System.Text.Json;
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
        // R7 Wave 11 T118 narrator spike (2026-06-30): bypasses the appnotification
        // dependency by running live Dataverse queries via DailyBriefingCollector,
        // then narrating via DailyBriefingNarrator. No request body — discovers the
        // user from the OBO token and resolves their systemuserid to drive queries.
        // POC scope: sprk_event only, 4 channels (Tasks Due Soon / Overdue /
        // Recent Matter Activity / My Recent Updates).
        group.MapPost("/render", HandleRender)
            .RequireRateLimiting("ai-batch")
            .WithName("RenderDailyBriefing")
            .WithSummary("Render full Daily Briefing from live Dataverse queries (no appnotification dependency)")
            .WithDescription(
                "Runs live FetchXML queries against Dataverse for the calling user's events " +
                "(tasks due soon, overdue, recent matter activity, your recent updates), " +
                "builds the NarrateRequest payload, then narrates via Azure OpenAI. " +
                "Returns the same DailyBriefingNarrateResponse shape as /narrate.")
            .Produces<DailyBriefingNarrateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(401)
            .ProducesProblem(429)
            .ProducesProblem(500)
            .ProducesProblem(503);

        return app;
    }

    /// <summary>
    /// R7 Wave 11 T118 narrator spike (2026-06-30): live-query render path.
    /// Resolves the caller's systemuserid from the OBO token's AAD oid claim,
    /// runs the collector to populate the request payload, then hands off to the
    /// existing narrator. No body required — the briefing is self-contained.
    /// </summary>
    private static async Task<IResult> HandleRender(
        ILoggerFactory loggerFactory,
        Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingCollector collector,
        Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingNarrator narrator,
        Spaarke.Dataverse.IGenericEntityService entityService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("DailyBriefingEndpoints");

        // Resolve the caller's Dataverse systemuserid from their AAD oid claim.
        // The "oid" claim on the OBO token is the Azure AD object id; Dataverse's
        // systemuser table stores it in azureactivedirectoryobjectid.
        var aadOid = httpContext.User?.FindFirst("oid")?.Value
                  ?? httpContext.User?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        if (string.IsNullOrEmpty(aadOid))
        {
            logger.LogWarning("Daily briefing render: token has no AAD oid claim; cannot resolve systemuserid");
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Caller AAD object id (oid claim) is required.");
        }

        Guid systemUserId;
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
                logger.LogWarning("Daily briefing render: no systemuser found for AAD oid {AadOid}", aadOid);
                return Results.Problem(
                    statusCode: 403,
                    title: "Forbidden",
                    detail: "Caller is not a Dataverse user in this environment.");
            }
            systemUserId = lookup.Entities[0].GetAttributeValue<Guid>("systemuserid");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Daily briefing render: systemuser lookup failed for AAD oid {AadOid}", aadOid);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to resolve caller identity.");
        }

        try
        {
            var payload = await collector.CollectAsync(systemUserId, cancellationToken).ConfigureAwait(false);
            if (payload.Channels.Length == 0 && payload.PriorityItems.Length == 0 && payload.Categories.Length == 0)
            {
                logger.LogInformation("Daily briefing render: no notable items for systemuserid={SystemUserId} — returning empty narrative", systemUserId);
                return TypedResults.Ok(new DailyBriefingNarrateResponse
                {
                    Tldr = new TldrResult { Summary = string.Empty, KeyTakeaways = [], TopAction = string.Empty },
                    ChannelNarratives = [],
                    GeneratedAtUtc = DateTimeOffset.UtcNow,
                });
            }

            var response = await narrator.NarrateAsync(payload, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(response);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Daily briefing render failed for systemuserid {SystemUserId}", systemUserId);
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Failed to render daily briefing.");
        }
    }

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
    /// R4 FR-12 / task 031 (Path A.5): the body of this endpoint is a thin dispatch
    /// wrapper that resolves the <c>DAILY-BRIEFING-NARRATE</c> playbook via
    /// <see cref="IConsumerRoutingService"/> and invokes it via the
    /// <see cref="IInvokePlaybookAi"/> facade. No inline LLM prompt strings remain
    /// in this method or its helpers — all prompt content lives in the playbook +
    /// associated Action rows (BRIEF-NARRATE-TLDR / BRIEF-NARRATE-CHANNEL /
    /// BRIEF-VALIDATE-ENTITY-NAMES). See <c>projects/spaarke-daily-update-service-r4/
    /// notes/decisions/030-dispatch-path.md</c> for the path decision.
    /// </summary>
    /// <remarks>
    /// Response shape (<see cref="DailyBriefingNarrateResponse"/>) is preserved for
    /// backward compatibility — the widget parser at <c>useBriefingNarration.ts</c>
    /// consumes the exact same JSON shape (R3 contract; AC-12b binding).
    /// </remarks>
    private static async Task<IResult> HandleNarrate(
        DailyBriefingNarrateRequest request,
        ILoggerFactory loggerFactory,
        IConsumerRoutingService routing,
        IInvokePlaybookAi invokePlaybookAi,
        IConfiguration configuration,
        Sprk.Bff.Api.Services.Ai.Narrators.DailyBriefingNarrator narrator,
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

        // R7 Wave 11 T116 narrator spike (2026-06-30) — feature-flag branch.
        // When Features:NarrateUseCodeBasedNarrator=true, bypass the playbook engine
        // and execute /narrate via DailyBriefingNarrator (direct C# calls). Flag off
        // (default) preserves existing playbook-engine path. Plan:
        //   projects/spaarke-ai-platform-unification-r7/notes/spikes/narrator-spike-plan.md
        var useCodeNarrator = configuration.GetValue<bool>(
            "Features:NarrateUseCodeBasedNarrator", defaultValue: false);

        if (useCodeNarrator)
        {
            logger.LogInformation(
                "Dispatching daily briefing narration via CODE-BASED narrator (spike): Categories={CategoryCount}, PriorityItems={PriorityCount}, Channels={ChannelCount}",
                request.Categories.Length, request.PriorityItems.Length, request.Channels.Length);

            try
            {
                var response = await narrator.NarrateAsync(request, cancellationToken).ConfigureAwait(false);
                return TypedResults.Ok(response);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Code-based narrator failed for /narrate (spike). Falling through to playbook-engine path.");
                // Fall through to the playbook path below for resilience during the spike.
            }
        }

        logger.LogInformation(
            "Dispatching daily briefing narration via PLAYBOOK ENGINE: Categories={CategoryCount}, PriorityItems={PriorityCount}, Channels={ChannelCount}",
            request.Categories.Length, request.PriorityItems.Length, request.Channels.Length);

        try
        {
            // 1. Resolve playbook GUID via the canonical sprk_playbookconsumer routing
            //    facade. Uses the ConsumerTypes.DailyBriefingNarrate compile-time constant
            //    (hardening per chat-routing-redesign-r1 code-review S-5 — never a literal
            //    string). Path A.5 binding per task 030 decision.
            var playbookId = await routing.ResolveAsync(
                ConsumerTypes.DailyBriefingNarrate,
                consumerCode: "default",
                context: null,
                environment: null,
                cancellationToken).ConfigureAwait(false);

            // Service-availability fail-fast (analogous to the prior briefingAi-null 503):
            // no sprk_playbookconsumer row → dispatch is unconfigured for this environment.
            if (playbookId is null)
            {
                logger.LogWarning(
                    "No sprk_playbookconsumer row matched for {ConsumerType} — daily briefing dispatch unconfigured.",
                    ConsumerTypes.DailyBriefingNarrate);

                return Results.Problem(
                    statusCode: 503,
                    title: "Service Unavailable",
                    detail: "Daily briefing dispatch is unconfigured. Ensure a sprk_playbookconsumer row exists for 'daily-briefing-narrate'.");
            }

            // 2. Serialize the structured request payload into the IInvokePlaybookAi
            //    parameter dictionary. The facade's parameter contract is
            //    IReadOnlyDictionary<string,string> (template substitution); the
            //    playbook's Start node binds {{json start}} and {{start.*}} from these
            //    parameter entries. We serialize the full request as one JSON-string
            //    parameter keyed "briefingPayload" plus convenience scalars for
            //    template-condition checks.
            var serializedPayload = JsonSerializer.Serialize(request, NarrateSerializerOptions);
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["briefingPayload"] = serializedPayload,
                ["totalNotificationCount"] = request.TotalNotificationCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["categoryCount"] = request.Categories.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["priorityItemCount"] = request.PriorityItems.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["channelCount"] = request.Channels.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };

            var tenantId =
                httpContext.User?.FindFirst("tid")?.Value
                ?? httpContext.User?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
                ?? string.Empty;

            var invocationContext = new PlaybookInvocationContext
            {
                TenantId = tenantId,
                HttpContext = httpContext,
                CorrelationId = httpContext.TraceIdentifier
            };

            // 3. Invoke the playbook via the existing IInvokePlaybookAi facade.
            //    The facade aggregates the orchestration SSE stream into a single
            //    PlaybookInvocationResult — non-streaming, single typed result.
            var playbookResult = await invokePlaybookAi.InvokePlaybookAsync(
                playbookId.Value,
                parameters,
                invocationContext,
                cancellationToken).ConfigureAwait(false);

            if (!playbookResult.Success)
            {
                logger.LogWarning(
                    "Daily briefing narrate playbook reported failure. PlaybookId={PlaybookId}, RunId={RunId}, ErrorCode={ErrorCode}, Error={ErrorMessage}",
                    playbookId.Value,
                    playbookResult.RunId,
                    playbookResult.ErrorCode,
                    playbookResult.ErrorMessage);

                return ProblemDetailsHelper.AiUnavailable(
                    playbookResult.ErrorMessage ?? "AI briefing service is temporarily unavailable.",
                    httpContext.TraceIdentifier);
            }

            // 4. Project the playbook result into the existing DailyBriefingNarrateResponse
            //    contract. The playbook's ReturnResponse node binds tldr/channelNarratives
            //    into StructuredData (per repo source-of-truth daily-briefing-narrate.json
            //    "responseBinding"). When StructuredData parsing fails (e.g., model drift),
            //    fall back to a TL;DR-only response carrying TextContent — graceful
            //    degradation rather than 500.
            var response = ProjectPlaybookResultToNarrateResponse(
                playbookResult,
                request,
                logger);

            logger.LogDebug(
                "Daily briefing narration dispatched: RunId={RunId}, PlaybookId={PlaybookId}, Channels={ChannelCount}",
                playbookResult.RunId,
                playbookId.Value,
                response.ChannelNarratives.Length);

            return TypedResults.Ok(response);
        }
        catch (FeatureDisabledException ex)
        {
            // P3 Fail-Fast (ADR-032 / NullInvokePlaybookAi): AI kill-switch is OFF.
            logger.LogDebug(
                "Daily briefing narrate called while AI feature disabled. ErrorCode={ErrorCode}",
                ex.ErrorCode);
            return ex.AsFeatureDisabled503();
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

    /// <summary>
    /// Cached JSON serializer options for the IInvokePlaybookAi parameter payload.
    /// camelCase property naming matches the playbook node graph's template references
    /// ({{start.categories}}, {{start.channels}}, etc.).
    /// </summary>
    private static readonly JsonSerializerOptions NarrateSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Project the playbook invocation result into the
    /// <see cref="DailyBriefingNarrateResponse"/> shape consumed by the widget parser.
    /// Reads the playbook's terminal StructuredData (per ReturnResponse node binding);
    /// falls back to a TL;DR-only response when StructuredData is absent or malformed
    /// (graceful degradation per FR-16).
    /// </summary>
    internal static DailyBriefingNarrateResponse ProjectPlaybookResultToNarrateResponse(
        PlaybookInvocationResult playbookResult,
        DailyBriefingNarrateRequest request,
        ILogger logger)
    {
        var generatedAtUtc = DateTimeOffset.UtcNow;
        var tldr = new TldrResult
        {
            Summary = string.Empty,
            KeyTakeaways = [],
            TopAction = string.Empty,
            CategoryCount = request.Categories.Length,
            PriorityItemCount = request.PriorityItems.Length
        };
        ChannelNarrationResult[] channelNarratives = [];

        if (playbookResult.StructuredData is JsonElement data && data.ValueKind == JsonValueKind.Object)
        {
            try
            {
                if (data.TryGetProperty("tldr", out var tldrElement) && tldrElement.ValueKind == JsonValueKind.Object)
                {
                    var deserializedTldr = tldrElement.Deserialize<TldrResult>(NarrateSerializerOptions);
                    if (deserializedTldr is not null)
                    {
                        tldr = deserializedTldr with
                        {
                            CategoryCount = request.Categories.Length,
                            PriorityItemCount = request.PriorityItems.Length
                        };
                    }
                }

                if (data.TryGetProperty("channelNarratives", out var channelsElement) && channelsElement.ValueKind == JsonValueKind.Array)
                {
                    channelNarratives = channelsElement.Deserialize<ChannelNarrationResult[]>(NarrateSerializerOptions)
                        ?? [];
                }
            }
            catch (JsonException ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to deserialize StructuredData fields from playbook result. Falling back to TextContent-only TL;DR. RunId={RunId}",
                    playbookResult.RunId);
            }
        }
        else if (!string.IsNullOrWhiteSpace(playbookResult.TextContent))
        {
            // Fallback path: playbook returned TextContent but no StructuredData.
            // Treat as a raw TL;DR summary — preserves response contract without
            // hallucinated bullets / topAction.
            tldr = tldr with { Summary = playbookResult.TextContent!.Trim() };
        }

        return new DailyBriefingNarrateResponse
        {
            Tldr = tldr,
            ChannelNarratives = channelNarratives,
            GeneratedAtUtc = generatedAtUtc
        };
    }

    // ────────────────────────────────────────────────────────────────
    // R4 task 031 (FR-12 Path A.5): inline LLM-prompt helpers REMOVED.
    //
    // Prior implementations of `BuildNarrateTldrPrompt`, `BuildChannelNarrationPrompt`,
    // `ParseTldrResponse`, `ParseChannelBullets`, `BuildAllowedRegardingIdSet`,
    // `ValidateBulletPrimaryEntityIds`, `GetTldrAsync`, `GetChannelNarrationAsync`,
    // and the inner `TldrJsonPayload` DTO previously lived here.
    //
    // ALL prompt construction + entity-name validation now lives in the playbook
    // (`DAILY-BRIEFING-NARRATE`) + its Action rows:
    //   - BRIEF-NARRATE-TLDR     (TL;DR prompt + JSON shape)
    //   - BRIEF-NARRATE-CHANNEL  (per-channel narration prompt + JSON shape)
    //   - BRIEF-VALIDATE-ENTITY-NAMES (post-LLM scrub of hallucinated names)
    //
    // The endpoint is now a thin dispatch wrapper — see HandleNarrate above.
    // Removing these helpers also removes the `IBriefingAi` parameter from
    // HandleNarrate; the Summarize endpoint above continues to inject
    // `IBriefingAi?` for the prioritized-briefing-summary path (not affected by
    // R4 FR-12 / task 031).
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
