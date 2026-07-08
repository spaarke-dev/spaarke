// FR-P3-04 (spaarke-ai-architecture-redesign-r1 task 043) — Daily Briefing as the FIRST
// full `coded` composite Action on the catalog path. This service is the briefing's
// dispatch boundary: Binding-resolved (ADR-039 single routing surface), coded-workflow
// executed (task-007 ICodedWorkflow convention), ledger-written-before-render (ADR-040
// via IOutputRouter), disposition-routed (render vs email).

using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Narrators;

/// <summary>
/// The Daily Briefing coded-composite dispatch boundary (FR-P3-04). Sibling of
/// <see cref="Chat.SessionDispatchOrchestrator"/> (the prompted executor's chat-summarize
/// boundary): resolves the briefing Binding via <see cref="IConsumerRoutingService"/>,
/// executes the Binding's <c>coded</c> Action through the <see cref="ICodedWorkflowRegistry"/>
/// class-ref convention, writes the session-ledger entries (Output + ToolChain) BEFORE any
/// rendering, then routes by the Binding's declared disposition (informational → widget
/// render; email → Communication-service delivery via the OutputRouter email leg).
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-039 (Binding decides)</b>: no feature flag, no engine fallback — the resolved
/// Binding row's <c>kind/workflowclass</c> IS the dispatch decision. An unresolvable or
/// non-<c>coded</c> row fails loudly (<see cref="DailyBriefingDispatchUnconfiguredException"/>
/// / <see cref="InvalidOperationException"/>); there is no second path. This replaces the
/// R7 narrator parallel-run feature flag (deleted, NFR-08 — grep the task-043 notes for its name).
/// </para>
/// <para>
/// <b>ADR-040 ledger (documented decisions, task 043)</b>:
/// (1) The briefing is not chat-attached, so the composite mints a briefing-run
/// <see cref="ChatSession"/> in-memory and lets <see cref="IOutputRouter.RouteAsync"/>
/// persist it (Redis hot + Cosmos warm write-through) — entries are addressable and
/// Tier-3 erasable without creating a user-visible chat (the Dataverse cold chat record
/// is a chat-recovery artifact, not a ledger requirement).
/// (2) ONE Output entry per execution whose payload is SECTION-NAME-KEYED
/// (<c>sections.tldr</c> / <c>sections.channels.{category}</c> / <c>sections.highPriority</c>
/// — the amended-ADR-037 section-name contract carried in the payload shape) rather than
/// one entry per section: per-section entries under an email-disposition Binding would
/// route one email per section, and the composite's addressable unit is the briefing.
/// The ToolChain entry (collect + narrate call summaries, identifiers/counts only per
/// NFR-07) is attached to the same session write, so both entry classes are durably
/// stored before this method returns the renderable response.
/// </para>
/// <para>
/// <b>No new streaming shape (amended ADR-037)</b>: the briefing endpoints return a single
/// JSON response (existing widget contract via <c>useBriefingRender</c>); this service
/// introduces no SSE surface. <c>DeliverComposite</c> stays frozen and untouched.
/// </para>
/// </remarks>
public class DailyBriefingCompositeService
{
    /// <summary>Ledger ToolChain tool ids (identifiers only, NFR-07).</summary>
    internal const string CollectToolId = "briefing.collect";
    internal const string NarrateToolId = "briefing.narrate";

    private static readonly JsonSerializerOptions PayloadSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IConsumerRoutingService _routing;
    private readonly ICodedWorkflowRegistry _workflows;
    private readonly DailyBriefingCollector _collector;
    private readonly IOutputRouter _outputRouter;
    private readonly Sprk.Bff.Api.Telemetry.AiTelemetry _aiTelemetry;
    private readonly ILogger<DailyBriefingCompositeService> _logger;

    public DailyBriefingCompositeService(
        IConsumerRoutingService routing,
        ICodedWorkflowRegistry workflows,
        DailyBriefingCollector collector,
        IOutputRouter outputRouter,
        Sprk.Bff.Api.Telemetry.AiTelemetry aiTelemetry,
        ILogger<DailyBriefingCompositeService> logger)
    {
        _routing = routing ?? throw new ArgumentNullException(nameof(routing));
        _workflows = workflows ?? throw new ArgumentNullException(nameof(workflows));
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _outputRouter = outputRouter ?? throw new ArgumentNullException(nameof(outputRouter));
        _aiTelemetry = aiTelemetry ?? throw new ArgumentNullException(nameof(aiTelemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Protected ctor for the <see cref="NullDailyBriefingCompositeService"/> kill-switch
    /// subclass (ADR-032) — mirrors <see cref="DailyBriefingNarrator"/>'s protected ctor.
    /// </summary>
    protected DailyBriefingCompositeService(ILogger<DailyBriefingCompositeService> logger)
    {
        _routing = null!;
        _workflows = null!;
        _collector = null!;
        _outputRouter = null!;
        _aiTelemetry = null!;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Render leg (widget path — <c>POST /api/ai/daily-briefing/render</c>): live-collect the
    /// caller's channels + high-priority items, execute the coded composite via its Binding
    /// (<c>consumerCode = default</c>, informational disposition), return the renderable
    /// response AFTER the ledger writes.
    /// </summary>
    public virtual async Task<DailyBriefingNarrateResponse> RenderAsync(
        Guid systemUserId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var (payload, highPriorityItems, collectMs) =
            await CollectAsync(systemUserId, cancellationToken).ConfigureAwait(false);

        if (payload.Channels.Length == 0 && payload.PriorityItems.Length == 0 && payload.Categories.Length == 0)
        {
            // Nothing to narrate — no LLM call, no dispatch, no ledger entry (the ledger
            // carries capability OUTPUTS, and an empty briefing produced none). High-priority
            // items STILL surface (operator design decision — structured list, not narrative).
            _logger.LogInformation(
                "Daily briefing composite: no notable channel items for systemuserid={SystemUserId} — empty response (highPriorityItems={HighPriorityCount})",
                systemUserId, highPriorityItems.Length);
            return EmptyResponse(highPriorityItems);
        }

        return await ExecuteAsync(
            consumerCode: "default",
            request: payload,
            systemUserId: systemUserId,
            tenantId: tenantId,
            recipientEmail: null,
            highPriorityItems: highPriorityItems,
            collectDurationMs: collectMs,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Narrate leg (caller-supplied payload — <c>POST /api/ai/daily-briefing/narrate</c>,
    /// legacy widget contract): execute the coded composite via the default Binding with
    /// the caller's pre-collected payload. Empty-payload short-circuiting is the endpoint's
    /// concern (it predates dispatch).
    /// </summary>
    public virtual Task<DailyBriefingNarrateResponse> NarrateAsync(
        DailyBriefingNarrateRequest request,
        string tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(
            consumerCode: "default",
            request: request,
            systemUserId: null,
            tenantId: tenantId,
            recipientEmail: null,
            highPriorityItems: null,
            collectDurationMs: null,
            cancellationToken);
    }

    /// <summary>
    /// Email leg (<c>POST /api/ai/daily-briefing/email</c>; UC-D-1): live-collect, execute the
    /// coded composite via the <c>email</c> Binding (email disposition), which routes the
    /// section-keyed output through the OutputRouter's Communication-service delivery leg.
    /// The briefing email is sent to <paramref name="recipientEmail"/> (the acting user).
    /// Empty briefings send nothing (no content → no email → no ledger entry).
    /// </summary>
    public virtual async Task<DailyBriefingNarrateResponse> EmailAsync(
        Guid systemUserId,
        string tenantId,
        string recipientEmail,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientEmail);

        var (payload, highPriorityItems, collectMs) =
            await CollectAsync(systemUserId, cancellationToken).ConfigureAwait(false);

        if (payload.Channels.Length == 0 && payload.PriorityItems.Length == 0 && payload.Categories.Length == 0)
        {
            _logger.LogInformation(
                "Daily briefing email: nothing to brief for systemuserid={SystemUserId} — no email sent",
                systemUserId);
            return EmptyResponse(highPriorityItems);
        }

        return await ExecuteAsync(
            consumerCode: "email",
            request: payload,
            systemUserId: systemUserId,
            tenantId: tenantId,
            recipientEmail: recipientEmail,
            highPriorityItems: highPriorityItems,
            collectDurationMs: collectMs,
            cancellationToken).ConfigureAwait(false);
    }

    // ── Core: Binding → coded workflow → ledger → disposition ────────────────────────────

    private async Task<DailyBriefingNarrateResponse> ExecuteAsync(
        string consumerCode,
        DailyBriefingNarrateRequest request,
        Guid? systemUserId,
        string tenantId,
        string? recipientEmail,
        HighPriorityItemDto[]? highPriorityItems,
        long? collectDurationMs,
        CancellationToken cancellationToken)
    {
        // 1. Resolve the Binding — THE routing decision (ADR-039). No flag, no fallback.
        var binding = await _routing.ResolveBindingAsync(
            ConsumerTypes.DailyBriefingNarrate,
            consumerCode,
            context: null,
            environment: null,
            cancellationToken).ConfigureAwait(false);

        if (binding is null)
        {
            throw new DailyBriefingDispatchUnconfiguredException(
                $"No enabled sprk_playbookconsumer row matched consumerType='{ConsumerTypes.DailyBriefingNarrate}' " +
                $"consumerCode='{consumerCode}' — daily briefing dispatch is unconfigured for this environment.");
        }

        // 2. Enforce the coded contract — the Binding's Action row must declare kind=coded
        //    with a registered workflow class ref (closed catalog; unknown refs are admin
        //    errors, never a fallback — ADR-039).
        if (binding.ActionKind != ActionKind.Coded || string.IsNullOrWhiteSpace(binding.WorkflowClass))
        {
            throw new InvalidOperationException(
                $"Daily briefing Binding {binding.BindingId} (consumerCode='{consumerCode}') must target a " +
                $"coded-kind Action with sprk_workflowclass set; found kind='{binding.ActionKind}', " +
                $"workflowClass='{binding.WorkflowClass ?? "<null>"}'. Fix the catalog row — there is no engine fallback (NFR-08).");
        }

        var workflow = _workflows.GetWorkflow(binding.WorkflowClass)
            ?? throw new InvalidOperationException(
                $"Daily briefing Binding {binding.BindingId} names workflow class '{binding.WorkflowClass}', " +
                $"which is not in the coded-workflow registry (registered: " +
                $"{string.Join(", ", _workflows.GetRegisteredWorkflowClassRefs())}). Fix the Action row or the registration.");

        _logger.LogInformation(
            "Daily briefing composite dispatch: bindingId={BindingId} consumerCode={ConsumerCode} workflowClass={WorkflowClass} disposition={Disposition} channels={ChannelCount}",
            binding.BindingId, consumerCode, binding.WorkflowClass, binding.Disposition, request.Channels.Length);

        // 3. Execute the coded workflow (FeatureDisabledException from the ADR-032 Null peer
        //    propagates unchanged — kill-switch flows through class-ref resolution).
        //    FR-P4-05 (task 054): the coded entry path's metering scope — the narrator's
        //    executor LLM calls (observed in OpenAiClient) and the capability-invocation
        //    counter below are dimensioned per tenant/user/entry-path=coded. The user
        //    dimension is the Dataverse systemuserid (an opaque identifier — NFR-07);
        //    the narrate leg has no acting-user id and omits the dimension.
        using var meteringScope = Sprk.Bff.Api.Telemetry.AiMeteringContext.Begin(
            tenantId, systemUserId?.ToString(), Sprk.Bff.Api.Telemetry.AiMeteringContext.EntryPathCoded);

        var narrateStart = DateTimeOffset.UtcNow;
        var workflowSucceeded = false;
        object? workflowResult;
        try
        {
            workflowResult = await workflow.ExecuteAsync(
                new CodedWorkflowContext
                {
                    ArgumentsJson = JsonSerializer.Serialize(request, PayloadSerializerOptions),
                    UserId = systemUserId,
                },
                cancellationToken).ConfigureAwait(false);
            workflowSucceeded = true;
        }
        finally
        {
            // FR-P4-05: one capability-invocation increment per coded-composite execution
            // (identifiers/counts only, NFR-07). Failure still counts — the LLM spend happened.
            _aiTelemetry.RecordCapabilityInvocation(
                tenantId,
                userId: systemUserId?.ToString(),
                entryPath: Sprk.Bff.Api.Telemetry.AiMeteringContext.EntryPathCoded,
                capability: binding.Ucid ?? binding.ConsumerType,
                outcome: workflowSucceeded ? "success" : "failed");
        }
        var narrateMs = (long)(DateTimeOffset.UtcNow - narrateStart).TotalMilliseconds;

        if (workflowResult is not DailyBriefingNarrateResponse response)
        {
            throw new InvalidOperationException(
                $"Coded workflow '{binding.WorkflowClass}' returned " +
                $"'{workflowResult?.GetType().Name ?? "<null>"}' — the daily briefing composite requires a " +
                $"{nameof(DailyBriefingNarrateResponse)}.");
        }

        // Attach the (non-LLM) high-priority structured list BEFORE the ledger write so the
        // stored section-keyed payload AND the email rendering carry it — the widget response,
        // the ledger entry, and the emailed briefing must agree (render follows store).
        if (highPriorityItems is { Length: > 0 })
        {
            response = response with { HighPriorityItems = highPriorityItems };
        }

        // 4. Ledger write BEFORE render/email (ADR-040 D2/D8). The briefing-run session is
        //    minted here (see class remarks decision 1) carrying the ToolChain entry; the
        //    router appends the section-keyed Output entry and persists both in one write.
        //    For the email disposition, RouteAsync ALSO performs delivery (store → send).
        var session = BuildBriefingRunSession(tenantId, request, collectDurationMs, narrateMs, systemUserId);
        var ledgerPayload = BuildSectionKeyedPayload(response, binding, recipientEmail);

        var routed = await _outputRouter.RouteAsync(session, binding, ledgerPayload, sourceRefs: null, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Daily briefing composite stored + routed: key={Key} disposition={Disposition} tenant={TenantId} session={SessionId}",
            routed.Entry.Key, routed.Entry.Disposition, tenantId, session.SessionId);

        // 5. Render follows store — the response the surface renders.
        return response;
    }

    private async Task<(DailyBriefingNarrateRequest Payload, HighPriorityItemDto[] HighPriority, long CollectMs)> CollectAsync(
        Guid systemUserId,
        CancellationToken cancellationToken)
    {
        // R7 W12 feedback item 9: fan out the collect + high-priority scan in parallel —
        // disjoint Dataverse queries; combined latency ≈ the slower of the two.
        var collectStart = DateTimeOffset.UtcNow;
        var payloadTask = _collector.CollectAsync(systemUserId, cancellationToken);
        var highPriorityTask = _collector.CollectHighPriorityAsync(systemUserId, cancellationToken);
        await Task.WhenAll(payloadTask, highPriorityTask).ConfigureAwait(false);
        var collectMs = (long)(DateTimeOffset.UtcNow - collectStart).TotalMilliseconds;

        return (await payloadTask.ConfigureAwait(false), await highPriorityTask.ConfigureAwait(false), collectMs);
    }

    /// <summary>
    /// Mint the briefing-run <see cref="ChatSession"/> (ledger carrier) with the ToolChain
    /// entry attached — identifiers/filters/counts ONLY (NFR-07; ToolChain entries are
    /// Tier-2-compatible metadata). Persisted by the router's ledger write.
    /// </summary>
    private static ChatSession BuildBriefingRunSession(
        string tenantId,
        DailyBriefingNarrateRequest request,
        long? collectDurationMs,
        long narrateDurationMs,
        Guid? systemUserId)
    {
        var now = DateTimeOffset.UtcNow;
        var calls = new List<SessionToolCall>();
        if (collectDurationMs is not null)
        {
            calls.Add(new SessionToolCall
            {
                ToolId = CollectToolId,
                ArgsSummary = systemUserId is null ? null : $"systemUserId={systemUserId}",
                ResultCount = request.Channels.Sum(c => c.Items.Length) + request.PriorityItems.Length,
                DurationMs = collectDurationMs,
            });
        }
        calls.Add(new SessionToolCall
        {
            ToolId = NarrateToolId,
            ArgsSummary = $"channels={request.Channels.Length}; categories={request.Categories.Length}; priorityItems={request.PriorityItems.Length}",
            ResultCount = request.Channels.Length,
            DurationMs = narrateDurationMs,
        });

        return new ChatSession(
            SessionId: Guid.NewGuid().ToString("N"),
            TenantId: tenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: now,
            LastActivity: now,
            Messages: Array.Empty<ChatMessage>())
        {
            ToolChains = new[]
            {
                new SessionToolChain { Turn = 1, Calls = calls, CreatedAt = now },
            },
        };
    }

    /// <summary>
    /// Build the SECTION-NAME-KEYED ledger payload (amended ADR-037 section contract carried
    /// in the payload shape): <c>sections.tldr</c>, <c>sections.channels.{category}</c>,
    /// plus the <c>email</c> envelope when the Binding declares the email disposition
    /// (the capability supplies presentation; the router supplies delivery).
    /// </summary>
    internal static JsonElement BuildSectionKeyedPayload(
        DailyBriefingNarrateResponse response,
        Binding binding,
        string? recipientEmail)
    {
        var channelSections = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var channel in response.ChannelNarratives)
        {
            var key = string.IsNullOrWhiteSpace(channel.Category) ? $"channel-{channelSections.Count + 1}" : channel.Category;
            channelSections[key] = channel;
        }

        object? email = null;
        if (binding.Disposition == BindingDisposition.Email)
        {
            if (string.IsNullOrWhiteSpace(recipientEmail))
            {
                throw new InvalidOperationException(
                    $"Daily briefing Binding {binding.BindingId} declares the email disposition but no recipient " +
                    "was supplied — the email leg requires the acting user's address.");
            }

            email = new
            {
                to = new[] { recipientEmail },
                subject = $"Your Daily Briefing — {DateTimeOffset.UtcNow:yyyy-MM-dd}",
                htmlBody = BuildEmailHtml(response),
            };
        }

        return JsonSerializer.SerializeToElement(
            new
            {
                sections = new
                {
                    tldr = response.Tldr,
                    channels = channelSections,
                    highPriority = response.HighPriorityItems,
                },
                generatedAtUtc = response.GeneratedAtUtc,
                email,
            },
            PayloadSerializerOptions);
    }

    /// <summary>
    /// Minimal HTML rendering of the briefing for the email leg. Presentation only —
    /// all narrative content comes from the coded workflow's LLM output (prompt-controlled
    /// via the BRIEF-NARRATE-* Action rows; ADR-039 grounded execution).
    /// </summary>
    internal static string BuildEmailHtml(DailyBriefingNarrateResponse response)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("<html><body style=\"font-family:Segoe UI,Arial,sans-serif;color:#242424;\">");
        sb.Append("<h2 style=\"margin-bottom:4px;\">Daily Briefing</h2>");
        sb.Append("<p style=\"color:#616161;margin-top:0;\">")
          .Append(System.Net.WebUtility.HtmlEncode(response.GeneratedAtUtc.ToString("f")))
          .Append("</p>");

        if (!string.IsNullOrWhiteSpace(response.Tldr.Summary))
        {
            sb.Append("<h3>TL;DR</h3><p>")
              .Append(System.Net.WebUtility.HtmlEncode(response.Tldr.Summary))
              .Append("</p>");
        }

        if (response.Tldr.KeyTakeaways.Length > 0)
        {
            sb.Append("<ul>");
            foreach (var takeaway in response.Tldr.KeyTakeaways)
            {
                sb.Append("<li>").Append(System.Net.WebUtility.HtmlEncode(takeaway)).Append("</li>");
            }
            sb.Append("</ul>");
        }

        if (!string.IsNullOrWhiteSpace(response.Tldr.TopAction))
        {
            sb.Append("<p><strong>Top action:</strong> ")
              .Append(System.Net.WebUtility.HtmlEncode(response.Tldr.TopAction))
              .Append("</p>");
        }

        if (response.HighPriorityItems.Length > 0)
        {
            sb.Append("<h3>High Priority</h3><ul>");
            foreach (var item in response.HighPriorityItems)
            {
                sb.Append("<li>").Append(System.Net.WebUtility.HtmlEncode(item.Name));
                if (item.DueDate is not null)
                {
                    sb.Append(" — due ").Append(System.Net.WebUtility.HtmlEncode(item.DueDate.Value.ToString("d")));
                }
                sb.Append("</li>");
            }
            sb.Append("</ul>");
        }

        foreach (var channel in response.ChannelNarratives)
        {
            if (channel.Bullets.Length == 0)
            {
                continue;
            }

            sb.Append("<h3>").Append(System.Net.WebUtility.HtmlEncode(channel.Category)).Append("</h3><ul>");
            foreach (var bullet in channel.Bullets)
            {
                sb.Append("<li>").Append(System.Net.WebUtility.HtmlEncode(bullet.Narrative)).Append("</li>");
            }
            sb.Append("</ul>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static DailyBriefingNarrateResponse EmptyResponse(HighPriorityItemDto[] highPriorityItems) => new()
    {
        Tldr = new TldrResult { Summary = string.Empty, KeyTakeaways = [], TopAction = string.Empty },
        ChannelNarratives = [],
        GeneratedAtUtc = DateTimeOffset.UtcNow,
        HighPriorityItems = highPriorityItems,
    };
}

/// <summary>
/// Thrown when no enabled Binding row resolves for the daily briefing (admin/config error).
/// The endpoints map this to 503 Service Unavailable with actionable detail — distinct from
/// 500-class execution failures. (CLAUDE.md §11 justification: the concrete contract that
/// fails without it is the 503-vs-500 ProblemDetails distinction the pre-cutover endpoint
/// already guaranteed via its null-playbookId branch.)
/// </summary>
public sealed class DailyBriefingDispatchUnconfiguredException : InvalidOperationException
{
    public DailyBriefingDispatchUnconfiguredException(string message) : base(message)
    {
    }
}
