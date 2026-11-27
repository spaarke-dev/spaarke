using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Documents;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Search;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Playbooks;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 030 (spec FR-05, FR-13, FR-14, NFR-02, NFR-05; ADR-013, ADR-015, ADR-028).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10). A thin read-only MCP adapter inside the BFF over IPlaybookRunAi.ReadAsCallerAsync: no
// package, no DI registration of its own, no background work.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: GET /api/ai/playbooks/runs/{runId} (IPlaybookOrchestrationService.GetRunStatusAsync: in memory, then task 005's durable
//     record for its starter), reached through IPlaybookRunAi with the detached runs of this process first.
//   Extension: reused; the tool adds the slim model-visible result and the read-side "interrupted" mapping (D-030-run-model condition 7).
//   Cost of doing nothing: a run started from Copilot can never be read back (FR-05, FR-14 fail).
// =====================================================================================================================

/// <summary>
/// <c>spaarke_get_playbook_run</c> (FR-05, FR-14): the status, progress and a short result summary of a JPS playbook run the signed-in
/// caller started. Read-only. Another caller's run and an unknown id give the same not-found.
/// </summary>
/// <remarks>
/// <para><b>Interrupted.</b> A run read from its durable record that still says running although it started more than
/// <see cref="PlaybookRunLimits.MaxRunDuration"/> plus <see cref="PlaybookRunLimits.InterruptedMargin"/> ago can no longer be running (the
/// cap ends every run): it was lost to a restart or a scale-in, so it is reported as <c>failed</c> with outcome <c>interrupted</c>. This
/// mapping is the tool's alone; the REST status route's bytes are unchanged.</para>
/// <para><b>Model-visible (ADR-015).</b> Run id, JPS playbook id, status, progress, node counts and a counts-only summary of at most
/// <see cref="SummaryMaxChars"/> characters; never node output or document content.</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class GetPlaybookRunTool : McpToolDefinition
{
    internal const string ToolName = "spaarke_get_playbook_run";

    /// <summary>The widget the tool's descriptor points at (<c>_meta.ui.resourceUri</c>, task 037c).</summary>
    internal const string WidgetUri = PlaybookRunWidgetResource.Uri;

    internal const string RunIdArgument = "runId";

    /// <summary>The longest result summary, in characters (POML constraint ADR-015).</summary>
    internal const int SummaryMaxChars = 1_000;

    /// <summary>The longest reason code passed through, in characters.</summary>
    private const int ReasonCodeMaxChars = 64;

    /// <summary>A status read that faulted: this host's own code (the REST route answers a bare 500).</summary>
    internal const string GetFailedReasonCode = "playbook.run-read-failed";

    internal static McpToolError GetFailed { get; } =
        new(GetFailedReasonCode, "The JPS playbook run could not be read right now. Try again shortly.", StatusCodes.Status503ServiceUnavailable);

    internal const string Description =
        "Use for: the status, progress and result summary of a Spaarke JPS playbook (AI analysis recipe) run that the signed-in user "
        + "started with spaarke_run_playbook. Needs the runId that spaarke_run_playbook returned. A run that is still running says so: "
        + "call this again shortly. A finished run reports completed or failed with node counts; a run that was interrupted or stopped "
        + "says so and can be started again. Not for: starting a run (use spaarke_run_playbook); reading an analysis record (use "
        + "spaarke_get_analysis); legal or contract-review playbooks (Markdown or Microsoft contract-review playbooks).";

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [RunIdArgument] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The runId (a GUID) that spaarke_run_playbook returned.",
            },
        },
        ["required"] = new JsonArray(RunIdArgument),
        ["additionalProperties"] = false,
    });

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            GetAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = ToolName,
                Title = "Get a JPS playbook run",
                Description = Description,
                ReadOnly = true,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
                // MCP Apps (task 037c): the playbook run widget renders the run. Only the descriptor changes; the text and structuredContent
                // are unchanged (frozen in PlaybookRunToolsContractTests).
                Meta = new JsonObject
                {
                    ["ui"] = new JsonObject { ["resourceUri"] = WidgetUri },
                },
            });

        tool.ProtocolTool.InputSchema = InputSchema;
        return tool;
    }

    /// <summary>The handler the SDK binds: <see cref="RunAsync"/> inside the one per-call telemetry line (NFR-05; rows = 1 when a run was returned).</summary>
    internal static Task<CallToolResult> GetAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        IPlaybookRunAi runs,
        TimeProvider time,
        ILogger<GetPlaybookRunTool> logger,
        CancellationToken cancellationToken) =>
        DecisionToolCall.LogAroundAsync(
            logger, time, ToolName, accessor.HttpContext,
            () => RunAsync(request.Params?.Arguments, accessor.HttpContext, runs, time, logger, cancellationToken),
            result => result.IsError == true ? 0 : 1);

    internal static async Task<CallToolResult> RunAsync(
        IDictionary<string, JsonElement>? arguments,
        HttpContext? http,
        IPlaybookRunAi runs,
        TimeProvider time,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // ADR-028 A10: no caller, no read.
        if (!PlanAndLookupToolSupport.HasCaller(http) || string.IsNullOrEmpty(CallerResolution.ResolveObjectId(http!.User)))
        {
            return DocumentToolSupport.Failure(DocumentToolSupport.UserContextRequired, http);
        }

        if (!DocumentToolSupport.TryReadStringArguments(arguments, ToolName, [RunIdArgument], http, out var values, out var argumentError))
        {
            return argumentError!;
        }

        if (!Guid.TryParse(values[0], out var runId) || runId == Guid.Empty)
        {
            return DocumentToolSupport.Failure(
                new McpToolError(DocumentToolSupport.InvalidIdReasonCode, $"'{RunIdArgument}' must be the run's id (a GUID).", StatusCodes.Status400BadRequest), http);
        }

        PlaybookRunView? run;
        try
        {
            run = await runs.ReadAsCallerAsync(http!, runId, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // ADR-015: the exception type only. A fault is "could not answer", never the not-found.
            logger.LogWarning("MCP tool {ToolName}: the read faulted ({ExceptionType}).", ToolName, ex.GetType().Name);
            return DocumentToolSupport.Failure(GetFailed, http);
        }

        // Another caller's run and an unknown id: the one uniform not-found (no existence oracle).
        return run is null ? DocumentToolSupport.Failure(DocumentToolSupport.NotFound, http) : StatusResult(run, time.GetUtcNow());
    }

    /// <summary>
    /// True when a durable record still says running past the cap plus the margin: the run was lost (condition 7). Only when the record
    /// has a start time: a record with no <c>sprk_startedon</c> reads <see cref="DateTimeOffset.MinValue"/>, which is not a known start,
    /// so its stored status is reported as it is.
    /// </summary>
    internal static bool IsInterrupted(PlaybookRunView run, DateTimeOffset now) =>
        run.FromDurableRecord
        && run.Phase == PlaybookRunPhase.Running
        && run.StartedAt != DateTimeOffset.MinValue
        && now - run.StartedAt > PlaybookRunLimits.MaxRunDuration + PlaybookRunLimits.InterruptedMargin;

    /// <summary>The status result for a run (pure): <see cref="McpLongRunning.Status"/> with the slim result and one text line.</summary>
    internal static CallToolResult StatusResult(PlaybookRunView run, DateTimeOffset now)
    {
        var interrupted = IsInterrupted(run, now);
        var (state, outcome) = interrupted
            ? (McpRunState.Failed, "interrupted")
            : run.Phase switch
            {
                PlaybookRunPhase.Running => (McpRunState.Running, "running"),
                PlaybookRunPhase.Completed => (McpRunState.Completed, "completed"),
                PlaybookRunPhase.Cancelled => (McpRunState.Failed, "cancelled"),
                _ => (McpRunState.Failed, "failed"),
            };

        var reasonCode = interrupted ? PlaybookRunReasonCodes.Interrupted : Short(run.ReasonCode, ReasonCodeMaxChars);
        var counts = $"Nodes: {run.CompletedNodes} completed, {run.FailedNodes} failed, {run.SkippedNodes} skipped.";
        var sentence = outcome switch
        {
            "running" => run.Progress is { } p ? $"The run is running ({p}% done)." : "The run is running.",
            "completed" => "The run completed.",
            "interrupted" => "The run was interrupted (Spaarke restarted while it ran) and did not finish; start it again.",
            "cancelled" => "The run was stopped before it finished; start it again if it is still needed.",
            _ => "The run failed.",
        };
        var summary = SearchToolSupport.Clip($"{sentence} {counts}", SummaryMaxChars)!;

        var body = new RunResult(
            run.PlaybookId.ToString("D"),
            outcome,
            reasonCode,
            Timestamp(run.StartedAt),
            run.CompletedAt is { } done ? Timestamp(done) : null,
            run.CompletedNodes,
            run.FailedNodes,
            run.SkippedNodes,
            summary);

        var line = $"JPS playbook run {run.RunId:D}: {summary}"
                   + (state == McpRunState.Running ? $" Call {ToolName} again shortly." : string.Empty);
        return McpLongRunning.Status(run.RunId.ToString("D"), state, state == McpRunState.Running ? run.Progress : null, body, McpText.SingleLine(line));
    }

    private static string? Short(string? value, int maxChars) =>
        string.IsNullOrWhiteSpace(value) ? null : SearchToolSupport.Clip(McpText.SingleLine(value).Trim(), maxChars);

    private static string Timestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>The <c>result</c> of a status: what the model may see (ADR-015: ids, state, counts, a counts-only summary).</summary>
    internal sealed record RunResult(
        string PlaybookId,
        string Outcome,
        string? ReasonCode,
        string StartedAt,
        string? CompletedAt,
        int CompletedNodes,
        int FailedNodes,
        int SkippedNodes,
        string Summary);
}
