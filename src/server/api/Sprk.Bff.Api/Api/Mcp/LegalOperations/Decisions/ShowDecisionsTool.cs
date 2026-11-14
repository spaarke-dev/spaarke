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
/// <para><b>Reads.</b> As the caller, through <see cref="IDecisionCapability"/>: the whole lane (<c>ListDecideAsync</c>) for the counts,
/// which must equal the Console's cards over every item, and page 1 (<c>ListDecidePageAsync</c>), whose privilege flags the capability
/// derives (task 018). Two reads of the lane; a failure of either is an <c>isError</c> result through <see cref="DecisionResultMapper"/>,
/// never a partial answer (the worklist's 503, <c>too_many_signals</c> included, is <c>ontology.signal.worklist_unavailable</c>).</para>
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
            var result = await RunAsync(request.Params?.Arguments, http, decisions, dataverse.Value.EnvironmentUrl, cancellationToken);
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
        CancellationToken cancellationToken)
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

        var lane = await decisions.ListDecideAsync(http.User, cancellationToken);
        var laneError = DecisionResultMapper.ToToolError(lane);
        if (laneError != null)
        {
            return (McpToolResults.FromError(laneError, http?.TraceIdentifier), 0);
        }

        var page = await decisions.ListDecidePageAsync(http.User, null, cancellationToken);
        var pageError = DecisionResultMapper.ToToolError(page);
        if (pageError != null)
        {
            return (McpToolResults.FromError(pageError, http?.TraceIdentifier), 0);
        }

        var cards = DecideLaneCategories.Partition(
            lane.Response!.Lanes.SelectMany(l => l.Groups).SelectMany(g => g.Items).Select(DecideLaneMember.From));
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
            baseUrl);
        return (DecisionToolCall.Success(text, structured), page.Items.Count);
    }
}

/// <summary>
/// <c>structuredContent</c> of <c>spaarke_show_decisions</c>: model-visible, so reduced (FR-04). <see cref="Items"/> are the non-privileged
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
    string? ConsoleBaseUrl);

/// <summary>The FR-04 reduction of page 1: how many items were taken out, the line that stands for them, and its <c>scope</c> ('firstPage' when the lane continues after page 1, so the lane may hold more; 'lane' when page 1 is the whole lane).</summary>
internal sealed record PrivilegedCount(int Count, string Text, string Scope);
