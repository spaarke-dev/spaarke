using System.Diagnostics;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Services.Workspace;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat-side typed handler for the <c>spaarke.daily_briefing_overview</c> tool — the FR-07
/// (spaarkeai-assistant-enhancements-r3 task 021) parity-tool backing for the Daily Briefing
/// layout tab. Wraps the EXISTING <see cref="BriefingService"/> (Lane 3 — "composed domain
/// services", per <c>design.md</c> §5) directly; there is no new query semantics, no new
/// aggregate endpoint, and no per-grid-style handler.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a separate handler from <see cref="GridOverviewHandler"/> (§11 reuse-first)</b>: Daily
/// Briefing's counts (active matters, matters at risk, overdue events, spend/budget utilization)
/// are NOT a single Dataverse saved query — <see cref="BriefingService.GetBriefingAsync"/>
/// aggregates <c>PortfolioService</c> data plus a membership-resolved top-priority-matter
/// heuristic and an optional AI-enhanced narrative. <see cref="GridOverviewHandler"/>'s contract
/// (execute ONE saved FetchXML by configId) cannot express this, so per the design doc's Lane 3
/// guidance — "a parity tool wraps the service directly — no reinvention" — this handler is a
/// THIN wrapper: it calls <see cref="BriefingService.GetBriefingAsync"/> and reshapes the
/// existing <c>BriefingResponse</c> into a <see cref="ToolResult"/>. No new counts are computed
/// here.
/// </para>
/// <para>
/// <b>Grounding (ADR-039 / ADR-015)</b>: every returned number comes directly from
/// <c>BriefingService</c>'s deterministic portfolio aggregation (never a prompt snapshot). When a
/// top-priority matter is present its Dataverse record id is echoed as a citation
/// (<c>tables/sprk_matter/records/{id}</c>) so the narrated answer is traceable to a record.
/// Telemetry carries the user id + counts + duration ONLY — never the narrative text (which may
/// be AI-generated free text) and never matter names.
/// </para>
/// <para>
/// <b>User-OBO ONLY (ADR-028)</b>: <see cref="BriefingService"/> resolves the caller's briefing
/// from the chat principal's AAD <c>oid</c> (<see cref="ChatInvocationContext.UserId"/>) — the
/// SAME identity the Quick Summary card / briefing dialog use. No app-only fallback; a missing
/// user id fails closed with a validation error rather than guessing or querying tenant-wide.
/// </para>
/// </remarks>
public sealed class DailyBriefingOverviewHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(DailyBriefingOverviewHandler);

    /// <summary>LLM-facing tool id. Spaarke-specific capability (not a GA Dataverse MCP shape).</summary>
    public const string ToolId = "spaarke.daily_briefing_overview";

    /// <summary>Dataverse entity logical name for the top-priority-matter citation.</summary>
    private const string MatterEntityLogicalName = "sprk_matter";

    private readonly BriefingService _briefingService;
    private readonly ILogger<DailyBriefingOverviewHandler> _logger;

    public DailyBriefingOverviewHandler(
        BriefingService briefingService,
        ILogger<DailyBriefingOverviewHandler> logger)
    {
        _briefingService = briefingService ?? throw new ArgumentNullException(nameof(briefingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Daily Briefing Overview",
        // Mirror of the authored sprk_description in
        // infra/dataverse/sprk_analysistool-daily-briefing-overview-row.json — keep byte-equal;
        // edit the JSON (same discipline as GridOverviewHandler / D-4).
        Description: @"Answers Daily Briefing overview questions (active matter count, matters at risk, overdue events, portfolio spend/budget utilization, and the current narrative) for the calling user, sourced from the SAME BriefingService the Quick Summary card and briefing dialog use — never from a prompt snapshot. Use this whenever the user asks to summarize their daily briefing counts, portfolio health, or 'what needs my attention'. Takes NO arguments — the briefing is always for the calling user. Returns { activeMatters, mattersAtRisk, overdueEvents, totalSpend, totalBudget, utilizationPercent, topPriorityMatter, narrative } — narrate the counts and cite the top-priority matter's record id when present; do not invent numbers.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: Array.Empty<ToolParameterDefinition>());

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Chat;

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool) =>
        ToolValidationResult.Failure(
            "DailyBriefingOverviewHandler is chat-context-only (agent-loop tool). Playbook-context invocation is unsupported.");

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, AnalysisTool tool, CancellationToken cancellationToken) =>
        Task.FromResult(ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "DailyBriefingOverviewHandler is chat-context-only (agent-loop tool). Playbook-context invocation is unsupported.",
            ToolErrorCodes.ValidationFailed));

    /// <inheritdoc />
    public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required.");

        if (string.IsNullOrWhiteSpace(context.UserId))
            return ToolValidationResult.Failure(
                "The calling user could not be identified (no authenticated principal); the Daily Briefing is per-user and cannot be resolved anonymously.");

        return ToolValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteChatAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(context.UserId))
        {
            return Error(tool,
                "The calling user could not be identified; the Daily Briefing is per-user and cannot be resolved anonymously.",
                ToolErrorCodes.ValidationFailed, startedAt);
        }

        try
        {
            var briefing = await _briefingService.GetBriefingAsync(context.UserId, cancellationToken).ConfigureAwait(false);

            var citations = new List<ToolResultCitation>();
            object? topPriorityMatter = null;
            if (briefing.TopPriorityMatter is { } top)
            {
                citations.Add(DataverseRecordCitations.ForRecord(MatterEntityLogicalName, top.MatterId));
                topPriorityMatter = new
                {
                    matterId = top.MatterId.ToString("D"),
                    name = top.Name,
                    deadline = top.Deadline,
                    reason = top.Reason,
                    // ADR-039/ADR-015 grounding: citable record-id path alongside the friendly id.
                    citationPath = DataverseRecordCitations.RecordPath(MatterEntityLogicalName, top.MatterId)
                };
            }

            stopwatch.Stop();
            // ADR-015: user id + counts + duration ONLY. NEVER the narrative text (may be
            // AI-generated free text) and NEVER matter names.
            _logger.LogInformation(
                "[spaarke.daily_briefing_overview][ADR-015] activeMatters={ActiveMatters} mattersAtRisk={MattersAtRisk} overdueEvents={OverdueEvents} hasTopMatter={HasTopMatter} isAiEnhanced={IsAiEnhanced} decisionId={DecisionId} durationMs={DurationMs}",
                briefing.ActiveMatters, briefing.MattersAtRisk, briefing.OverdueEvents,
                briefing.TopPriorityMatter is not null, briefing.IsAiEnhanced,
                context.DecisionId, stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new
                {
                    tool = ToolId,
                    activeMatters = briefing.ActiveMatters,
                    mattersAtRisk = briefing.MattersAtRisk,
                    overdueEvents = briefing.OverdueEvents,
                    totalSpend = briefing.TotalSpend,
                    totalBudget = briefing.TotalBudget,
                    utilizationPercent = briefing.UtilizationPercent,
                    topPriorityMatter,
                    narrative = briefing.Narrative,
                    generatedAt = briefing.GeneratedAt,
                    citations = citations.Select(c => new { path = c.ChunkId, entity = c.SourceName })
                },
                summary: $"Daily Briefing: {briefing.ActiveMatters} active matter(s), {briefing.MattersAtRisk} at risk, {briefing.OverdueEvents} overdue event(s).",
                confidence: 1.0,
                execution: Timed(startedAt)) with
            {
                Metadata = citations.Count > 0
                    ? new Dictionary<string, object?> { [ToolResultMetadataKeys.Citations] = citations }
                    : null
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Error(tool, "spaarke.daily_briefing_overview was cancelled.", ToolErrorCodes.Cancelled, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[spaarke.daily_briefing_overview] failed decisionId={DecisionId}: {ErrorType}",
                context.DecisionId, ex.GetType().Name);
            return Error(tool, "spaarke.daily_briefing_overview failed unexpectedly.", ToolErrorCodes.InternalError, startedAt);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private ToolResult Error(AnalysisTool tool, string message, string code, DateTimeOffset startedAt) =>
        ToolResult.Error(HandlerId, tool.Id, tool.Name, message, code, Timed(startedAt));

    private static ToolExecutionMetadata Timed(DateTimeOffset startedAt) =>
        new() { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow };
}
