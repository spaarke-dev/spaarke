using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r1 task 022 (spec FR-03, FR-04, FR-05, FR-14, NFR-02, NFR-05; design §7.1, §9.5).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md; spec "Placement Justification"). A thin MCP
// adapter over IDecisionCapability inside the BFF, the decision slice's data owner (FR-24), on the /mcp/legal-operations host task 020
// added: no package, no DI registration (found by [McpArea], task 020), no background work, no AI-internal type.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: GET /api/v1/signals/worklist (SignalReadEndpoints) and the Console's laneCountFilters.ts; no MCP tool in the BFF.
//   Extension: this IS the extension: it calls the same capability as the REST route; the only new logic is the category partition
//     (DecideLaneCategories, parity-tested) and the FR-04 reduction (DecisionsTextSummary).
//   Cost of doing nothing: Copilot cannot list a user's Work Items (FR-03), and privileged detail would reach the model (FR-04).
//
// FR-13 "done today" (DEF-001, #2832) — component justification for the new `closedToday` member.
//   Existing: the Console's figure (WorklistWidget.doneToday): the caller's closedToday set of GET /api/v1/signals/worklist, which is
//     IDecisionCapability.ListDecideAsync(WorklistSet.ClosedToday); the widget has no tool that returns it.
//   Extension: show calls that same read and partitions it with DecideLaneCategories; no new service, route, query or column.
//   Cost of doing nothing: the widget's All card cannot show "n of m done today" (FR-13), a line the Console's card has.
// =====================================================================================================================

/// <summary>
/// <c>spaarke_show_decisions</c> (FR-03): the signed-in user's Decide lane for the model and the widget. Read-only; visible to the model
/// and the app; input is one optional closed <c>category</c>.
/// </summary>
/// <remarks>
/// <para><b>Result.</b> <c>content</c> is the short text summary (<see cref="DecisionsTextSummary"/>): counts per category, the top
/// non-privileged items with links, the privileged count line. <c>structuredContent</c> is page 1 of the slim rows with privileged rows
/// taken out (FR-04), the next cursor, the per-category counts and <c>consoleBaseUrl</c>; <c>_meta.ui</c> on the tool points at the widget.
/// The widget fetches full detail, page 1 included, through the app-only <see cref="ListDecisionPageTool"/>.</para>
/// <para><b>Reads.</b> As the caller, through <see cref="IDecisionCapability"/>: ONE read of the lane (<c>ListDecidePageAsync</c>, page 1,
/// whose privilege flags the capability derives, task 018). The counts come from the whole lanes response that page was cut from
/// (<see cref="DecidePageResult.Lane"/>, the same response <c>ListDecideAsync</c> returns), so they equal the Console's cards over every item
/// (NFR-01, 2026-11-25: the lane was read twice before, ~2 s each). One further read, of the caller's closed-today set, runs beside it
/// (<see cref="ReadClosedTodayAsync"/>, FR-13): it gives <c>closedToday</c>, the count per category of the items closed today, and its
/// failure only leaves that member out. A failure of the lane is an <c>isError</c> result through
/// <see cref="DecisionResultMapper"/>, never a partial answer (the worklist's 503, <c>too_many_signals</c> included, is
/// <c>ontology.signal.worklist_unavailable</c>); a page without its lane is the same 503, never counts of zero.</para>
/// <para><b>Caller (FR-10).</b> The request context is <c>IHttpContextAccessor.HttpContext</c>, passed through unchanged; a null context or an
/// unauthenticated caller is <c>UserContextRequired</c> before any capability call, and nothing falls back to app-only.</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class ShowDecisionsTool : McpToolDefinition
{
    public const string Name = "spaarke_show_decisions";

    /// <summary>The decisions widget (naming note §1; built by task 030).</summary>
    public const string WidgetUri = "ui://spaarke/decisions.html";

    /// <summary>The one argument.</summary>
    public const string CategoryArgument = "category";

    /// <summary>The description the model reads (FR-03: at most 1,024 characters, "use for / not for").</summary>
    public const string Description =
        "Use for: showing the signed-in user's Spaarke Decide lane, the Work Items waiting for their decision: the count per category "
        + "(as the Spaarke Console's category cards show them) and the top items, each with a link to open it in Spaarke. Where the host "
        + "shows widgets, the decisions widget lists the whole lane and is where items are decided. The optional category narrows the top "
        + "items to one card: all, invoiceDecisions, askOutsideCounsel, approveOrRebudget, chaseReply or other. Privileged items are only "
        + "counted, as 'N privileged items — open to review'; they are reviewed in the widget or in Spaarke. "
        + "Not for: completing, dismissing or changing a decision (that happens in the widget or in Spaarke); the Do lane or items already "
        + "decided or closed; searching documents, matters or email; the details of one item.";

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [CategoryArgument] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray([.. DecideLaneCategories.Keys.Select(k => (JsonNode)JsonValue.Create(k)!)]),
                ["description"] = "Optional. Narrow the top items to one category card of the Decide lane. Default: all.",
            },
        },
        ["additionalProperties"] = false,
    });

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            ShowAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = Name,
                Title = "Show my decisions",
                Description = Description,
                ReadOnly = true,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
                // MCP Apps: the widget renders the result; visible to the model and to the app.
                Meta = new JsonObject
                {
                    ["ui"] = new JsonObject { ["resourceUri"] = WidgetUri, ["visibility"] = new JsonArray("model", "app") },
                },
            });

        // The input schema is stated, not inferred: one optional closed enum, nothing else (FR-03). The handler reads the raw arguments
        // against the same closed set, so the schema and the check cannot disagree.
        tool.ProtocolTool.InputSchema = InputSchema;
        return tool;
    }

    private static async Task<CallToolResult> ShowAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        IDecisionCapability decisions,
        IOptions<DataverseOptions> dataverse,
        TimeProvider time,
        ILogger<ShowDecisionsTool> logger,
        CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        var http = accessor.HttpContext;
        var outcome = DecisionToolCall.Exception;
        var rows = 0;
        try
        {
            var result = await RunAsync(request.Params?.Arguments, http, decisions, dataverse.Value.EnvironmentUrl, cancellationToken, logger);
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
        IDictionary<string, JsonElement>? arguments,
        HttpContext? http,
        IDecisionCapability decisions,
        string? environmentUrl,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        if (!DecisionToolCall.TryReadSingleArgument(arguments, Name, CategoryArgument, http?.TraceIdentifier, out var argument, out var argumentError))
        {
            return (argumentError!, 0);
        }

        var category = DecideLaneCategories.All;
        if (argument.HasValue)
        {
            var value = argument.Value;
            if (value.ValueKind != JsonValueKind.String || !DecideLaneCategories.Keys.Contains(value.GetString(), StringComparer.Ordinal))
            {
                return (DecisionToolCall.InvalidArgument(
                    $"'{CategoryArgument}' must be one of: {string.Join(", ", DecideLaneCategories.Keys)}. Leave it out to show all categories.", http?.TraceIdentifier), 0);
            }

            category = value.GetString()!;
        }

        // FR-10: no caller, no read. Checked here, before any capability call, with the capability's own test.
        // (Null checks are written == / != here, not as patterns: the MCP guard's AI-internal-type scan reads the keyword as a type name.)
        if (http == null || !DecisionCapability.HasSignedInCaller(http.User))
        {
            return (McpToolResults.FromError(DecisionResultMapper.ToToolError(DecidePageResult.UserContextRequired)!, http?.TraceIdentifier), 0);
        }

        // One read of the lane (NFR-01): page 1 carries the whole lanes response it was cut from, and the counts come from that.
        // The closed-today read starts beside it (independent reads; the same caller) and is awaited with it on every path, so no read
        // outlives the call. It never fails the call: see ReadClosedTodayAsync.
        var pageRead = decisions.ListDecidePageAsync(http.User, null, cancellationToken);
        var closedRead = ReadClosedTodayAsync(decisions, http.User, logger, http.TraceIdentifier, cancellationToken);
        await Task.WhenAll(pageRead, closedRead);
        var page = await pageRead;
        var closedToday = await closedRead;
        var pageError = DecisionResultMapper.ToToolError(page)
            // An Ok page without its lane cannot give the counts; it is the worklist's 503, never counts of zero.
            ?? (page.Lane == null ? DecisionResultMapper.ToToolError(DecidePageResult.ReadFailed(SignalReadResult.Unavailable("fault"))) : null);
        if (pageError != null)
        {
            return (McpToolResults.FromError(pageError, http?.TraceIdentifier), 0);
        }

        var cards = DecideLaneCategories.Partition(
            page.Lane!.Lanes.SelectMany(l => l.Groups).SelectMany(g => g.Items).Select(DecideLaneMember.From));
        var resolved = DecideLaneCategories.Resolve(cards, category);
        var (visible, cores, privileged) = DecisionsTextSummary.Reduce(page.Items, page.Cores);
        var baseUrl = DecisionsTextSummary.ConsoleBaseUrl(environmentUrl);

        var text = DecisionsTextSummary.Build(new DecisionsTextSummary.Input(
            cards, resolved, resolved == category ? null : category, visible, cores, page.Items.Count, privileged, page.NextCursor != null, baseUrl));

        var structured = new ShowDecisionsContent(
            cards.First(c => c.Key == DecideLaneCategories.All).Count,
            resolved,
            cards,
            visible,
            cores,
            page.Items.Count,
            new PrivilegedCount(
                privileged,
                DecisionsTextSummary.PrivilegedLine(privileged, page.Items.Count, page.NextCursor != null),
                DecisionsTextSummary.PrivilegedScope(page.NextCursor != null)),
            page.NextCursor,
            baseUrl,
            closedToday);
        return (DecisionToolCall.Success(text, structured), page.Items.Count);
    }

    /// <summary>
    /// The Console's "done today" source, as counts per category (FR-13): the caller's <see cref="WorklistSet.ClosedToday"/> read, which
    /// the Console reads for the same figure (<c>WorklistWidget.doneToday</c>: the items of the lane in that set). It is the same
    /// <see cref="IDecisionCapability.ListDecideAsync"/> as the lane's, as the caller, so it holds what the caller may read and no more
    /// (core-record access, the Do-lane "mine" rule) and "today" is each item's own day (D-25 as amended by D-129): nothing is redefined
    /// here. The counts are partitioned by <see cref="DecideLaneCategories"/>, like the cards.
    /// <para>A read that does not answer Ok, or that throws (anything but the caller's own cancellation), is null, so the figure is absent and
    /// the widget shows no line: what the Console does when its closed-today set did not load (<c>doneToday</c> undefined). The lane's own
    /// answer is never held back for it.</para>
    /// <para><b>Privileged items (FR-04).</b> They are counted here exactly as the lane's <c>Categories</c> count them: both partition
    /// <see cref="DecideLaneMember"/>, which carries no privilege flag, so a privileged item is one more in its category's number in both,
    /// and neither names it. Counts only.</para>
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, int>?> ReadClosedTodayAsync(
        IDecisionCapability decisions, ClaimsPrincipal user, ILogger? logger, string? correlationId, CancellationToken cancellationToken)
    {
        SignalReadResult read;
        try
        {
            read = await decisions.ListDecideAsync(user, cancellationToken, WorklistSet.ClosedToday);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A thrown read (an unexpected Dataverse or HTTP fault, not an outcome) is the same as a failed one: the member is left out and
            // the lane is answered. Task.WhenAll would otherwise rethrow it and fail the whole call. Only the exception type is logged:
            // its message may carry request data. The caller's own cancellation still propagates.
            logger?.LogWarning(
                "MCP tool {ToolName}: the closed-today read threw {ExceptionType}; closedToday is left out. Correlation {CorrelationId}.",
                Name, ex.GetType().Name, correlationId);
            return null;
        }

        if (read.Outcome != SignalReadOutcome.Ok || read.Response == null)
        {
            logger?.LogInformation(
                "MCP tool {ToolName}: the closed-today read answered {Outcome}; closedToday is left out. Correlation {CorrelationId}.",
                Name, read.Outcome, correlationId);
            return null;
        }

        return DecideLaneCategories.CountsByKey(
            read.Response.Lanes.SelectMany(l => l.Groups).SelectMany(g => g.Items).Select(DecideLaneMember.From));
    }
}

/// <summary>
/// <c>structuredContent</c> of <c>spaarke_show_decisions</c>: model-visible, so reduced (FR-04). <see cref="ClosedToday"/> is a count per
/// <see cref="DecideLaneCategories.Keys"/> key (<c>all</c> is the Decide lane's total, the Console's "done today"), counts only, and is
/// absent when that read did not answer. <see cref="Items"/> are the non-privileged
/// rows of page 1 in Console order and <see cref="Cores"/> their core records; <see cref="PageItemCount"/> is how many items page 1 holds,
/// privileged ones included; <see cref="NextCursor"/> continues after page 1 through the paging tool; <see cref="ConsoleBaseUrl"/> is the
/// base the widget builds link-outs on (absent when the environment URL is not usable).
/// </summary>
internal sealed record ShowDecisionsContent(
    int Total,
    string Category,
    IReadOnlyList<DecideLaneCategory> Categories,
    IReadOnlyList<WorklistItemSummary> Items,
    IReadOnlyDictionary<Guid, WorklistCoreSummary> Cores,
    int PageItemCount,
    PrivilegedCount Privileged,
    string? NextCursor,
    string? ConsoleBaseUrl,
    IReadOnlyDictionary<string, int>? ClosedToday = null);

/// <summary>The FR-04 reduction of page 1: how many items were taken out, the line that stands for them, and its <c>scope</c> ('firstPage' when the lane continues after page 1, so the lane may hold more; 'lane' when page 1 is the whole lane).</summary>
internal sealed record PrivilegedCount(int Count, string Text, string Scope);
