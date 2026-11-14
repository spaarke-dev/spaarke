using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r1 task 022 (spec FR-05, FR-04, NFR-02, NFR-05; notes/decisions.md 2026-11-06 "FR-05 paging").
//
// PLACEMENT JUSTIFICATION: as ShowDecisionsTool (a thin MCP adapter over IDecisionCapability on the task-020 host; nothing registered).
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: IDecisionCapability.ListDecidePageAsync (task 015) pages the lane; spaarke_show_decisions returns page 1, reduced.
//   Extension: a cursor parameter on spaarke_show_decisions would break its closed model-visible schema (FR-03, FR-05), so the plan
//     chose a fifth, app-only tool; it is a pass-through to the same capability member.
//   Cost of doing nothing: the widget cannot page past 50 items or see a privileged item's detail (FR-05, FR-04).
// =====================================================================================================================

/// <summary>
/// <c>spaarke_list_decision_page</c> (FR-05; final name fixed by task 022 under naming note §1, <c>spaarke_{verb}_{object}</c>): one page
/// of the caller's Decide lane with FULL row detail, privileged rows included, for the decisions widget. App-only (hidden from the model),
/// read-only; input is the cursor only.
/// </summary>
/// <remarks>
/// <para><b>Contract.</b> No cursor (or null) is page 1; otherwise the <c>nextCursor</c> of the previous page, from
/// <c>spaarke_show_decisions</c> or from this tool. <c>structuredContent</c> is the page's slim rows (at most 50, each at most 600
/// bytes), their core records and the next cursor (absent on the last page), exactly as <see cref="IDecisionCapability.ListDecidePageAsync"/>
/// returns them. A stale cursor returns what remains without duplicates (the capability's rule); one that does not decode is
/// <c>ontology.worklist.cursor_invalid</c>.</para>
/// <para><b>Why full detail is allowed here (FR-04).</b> App-only results go to the widget, not the model. <c>content</c> therefore says only
/// how many rows the page holds: no record text, so even a host that showed it to the model would show no detail.</para>
/// <para><b>Caller (FR-10).</b> As <see cref="ShowDecisionsTool"/>: <c>IHttpContextAccessor.HttpContext</c>, unchanged; none or
/// unauthenticated is <c>UserContextRequired</c> before any capability call.</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class ListDecisionPageTool : McpToolDefinition
{
    public const string Name = "spaarke_list_decision_page";

    /// <summary>The one argument.</summary>
    public const string CursorArgument = "cursor";

    public const string Description =
        "Use for: the Spaarke decisions widget fetching one page (up to 50 Work Items) of the signed-in user's Decide lane with full row "
        + "detail, from the cursor returned with the previous page; no cursor returns the first page. "
        + "Not for: the model; showing decisions to the user in chat (use spaarke_show_decisions); any change to a Work Item.";

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [CursorArgument] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The nextCursor of the previous page, unchanged. Leave it out for the first page.",
            },
        },
        ["additionalProperties"] = false,
    });

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            ListPageAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = Name,
                Title = "List a page of decisions",
                Description = Description,
                ReadOnly = true,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
                // MCP Apps tool visibility: app-only, hidden from the model, callable by the widget (Phase 0 T3: honoured in Chat).
                Meta = new JsonObject { ["ui"] = new JsonObject { ["visibility"] = new JsonArray("app") } },
            });

        tool.ProtocolTool.InputSchema = InputSchema;
        return tool;
    }

    private static async Task<CallToolResult> ListPageAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        IDecisionCapability decisions,
        TimeProvider time,
        ILogger<ListDecisionPageTool> logger,
        CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        var http = accessor.HttpContext;
        var outcome = DecisionToolCall.Exception;
        var rows = 0;
        try
        {
            var result = await RunAsync(request.Params?.Arguments, http, decisions, cancellationToken);
            rows = result.Rows;
            outcome = DecisionToolCall.OutcomeOf(result.Result);
            return result.Result;
        }
        finally
        {
            DecisionToolCall.LogCompleted(logger, Name, outcome, rows, time.GetElapsedTime(started), http?.TraceIdentifier);
        }
    }

    internal static async Task<(CallToolResult Result, int Rows)> RunAsync(
        IDictionary<string, JsonElement>? arguments, HttpContext? http, IDecisionCapability decisions, CancellationToken cancellationToken)
    {
        if (!DecisionToolCall.TryReadSingleArgument(arguments, Name, CursorArgument, http?.TraceIdentifier, out var argument, out var argumentError))
        {
            return (argumentError!, 0);
        }

        // A cursor that is not a string, or is empty or blank, cannot be one this server wrote ("no cursor" is the argument left out or
        // null): the same answer as one that does not decode. (The capability itself treats "" as page 1, so the tool must refuse it
        // before the call.) Null checks are written == / != here, not as patterns: the MCP guard's AI-internal-type scan reads the keyword as a type name.
        if (argument.HasValue
            && (argument.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(argument.Value.GetString())))
        {
            return (McpToolResults.FromError(DecisionResultMapper.ToToolError(DecidePageResult.CursorInvalid)!, http?.TraceIdentifier), 0);
        }

        if (http == null || !DecisionCapability.HasSignedInCaller(http.User))
        {
            return (McpToolResults.FromError(DecisionResultMapper.ToToolError(DecidePageResult.UserContextRequired)!, http?.TraceIdentifier), 0);
        }

        var page = await decisions.ListDecidePageAsync(http.User, argument?.GetString(), cancellationToken);
        var pageError = DecisionResultMapper.ToToolError(page);
        if (pageError != null)
        {
            return (McpToolResults.FromError(pageError, http?.TraceIdentifier), 0);
        }

        var text = page.NextCursor == null
            ? $"A page of {page.Items.Count} Decide-lane items; this is the last page."
            : $"A page of {page.Items.Count} Decide-lane items; more follow (pass nextCursor for the next page).";
        return (DecisionToolCall.Success(text, new DecisionPageContent(page.Items, page.Cores, page.NextCursor)), page.Items.Count);
    }
}

/// <summary><c>structuredContent</c> of <c>spaarke_list_decision_page</c>: the page as the capability returns it (app-only; full detail).</summary>
internal sealed record DecisionPageContent(
    IReadOnlyList<WorklistItemSummary> Items,
    IReadOnlyDictionary<Guid, WorklistCoreSummary> Cores,
    string? NextCursor);
