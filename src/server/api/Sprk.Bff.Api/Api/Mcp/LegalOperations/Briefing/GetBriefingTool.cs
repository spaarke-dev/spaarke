using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Briefing;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 034 (spec FR-09, FR-14, NFR-02, NFR-05; decision D-034-render in notes/034-decisions.md).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md; spec "Placement & New Components"). A thin MCP
// adapter inside the BFF over the render rules of POST /api/ai/daily-briefing/render (DailyBriefingEndpoints.RenderForCallerAsync), on the
// existing /mcp/legal-operations host: no package, no DI registration (the catalog finds the class by [McpArea]), no background work, no
// AI-internal type (it never names the composite service; ADR-013).
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: POST /api/ai/daily-briefing/render (DailyBriefingEndpoints, over DailyBriefingCompositeService), the one call the Console's
//     DailyBriefingApp makes for the briefing (useBriefingRender -> fetchBriefingLive).
//   Extension: this IS the extension: the route's rules were extracted into one method both hosts call (REST bytes unchanged, proven by
//     the existing route tests plus a parity test); the tool's own logic is the argument check, the 30 s budget, the short text and the
//     size bound.
//   Cost of doing nothing: Copilot users cannot see their Daily Briefing (FR-09 fails), and the widget has nothing to render.
// =====================================================================================================================

/// <summary>
/// <c>spaarke_get_briefing</c> (FR-09): the signed-in user's AI Daily Briefing, the one the Spaarke Console shows them. Read-only; visible to
/// the model and the app; inputs are the two optional windows the Console's Display Parameters set. <c>_meta.ui</c> points at the
/// Daily Briefing widget (<see cref="BriefingWidgetResource"/>), which renders <c>structuredContent</c> with the Console's component and asks
/// for a fresh briefing through this same tool (its Refresh button).
/// </summary>
/// <remarks>
/// <para><b>Which call (D-034-render).</b> <c>POST /api/ai/daily-briefing/render</c>: it is the only briefing call the Console's component makes
/// (<c>useBriefingRender</c> -> <c>fetchBriefingLive</c>; <c>/summarize</c> and <c>/narrate</c> are not called by it, and <c>/narrate</c> takes
/// a payload a tool cannot supply), so <c>structuredContent</c> is the <see cref="DailyBriefingNarrateResponse"/> that route answers, and the
/// Console and Copilot show the same briefing for the same user and windows. The briefing is regenerated on each call, as the Console's
/// Refresh does: it is not cached.</para>
/// <para><b>Model-visible output (ADR-015, NFR-02).</b> <c>content</c> is the TL;DR (summary and key takeaways) and counts only, in at
/// most <see cref="MaxTextLength"/> characters, every value on one line. The per-channel detail (record names, descriptions) is in
/// <c>structuredContent</c> for the widget. When the response would not fit under 64 KiB the descriptions, then the trailing highlights, are left
/// out (<see cref="BriefingSizeBound"/>) and the text says how many; a briefing that fits is returned exactly as the route answers it.</para>
/// <para><b>Caller (ADR-028, FR-10).</b> The request context is <c>IHttpContextAccessor.HttpContext</c>, passed through unchanged to the route's
/// rules: no caller is <c>UserContextRequired</c> before anything is read, and nothing falls back to app-only.</para>
/// <para><b>30 seconds (FR-14).</b> The rules run under a budget of <see cref="McpLongRunning.Budget"/> minus a margin. A briefing that has not
/// rendered by then is an <c>isError</c> <see cref="TimeoutReason"/> result that says to ask again, never a hung call. The render has no run
/// record to start and poll (it is one synchronous composite call, not a playbook run), so FR-14's start/status is not used; whether real
/// users' briefings exceed the budget is measured at the next dev deploy (notes/defer-issues.md).</para>
/// <para><b>Failures.</b> Every failure is an <c>isError</c> result with fixed text: nothing of an exception or of the route's own 503 detail
/// reaches the model.</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class GetBriefingTool : McpToolDefinition
{
    public const string Name = "spaarke_get_briefing";

    /// <summary>The Daily Briefing widget (built by this task).</summary>
    public const string WidgetUri = BriefingWidgetResource.Uri;

    public const string DueWithinDaysArgument = "dueWithinDays";

    public const string RecencyHoursArgument = "recencyHours";

    /// <summary>The route's clamp: due-soon look-ahead in days, 1 to 31 (<c>DailyBriefingEndpoints.BuildWindowOptions</c>).</summary>
    public const int MaxDueWithinDays = 31;

    /// <summary>The route's clamp: recency look-back in hours, 1 to 744.</summary>
    public const int MaxRecencyHours = 744;

    /// <summary>The longest <c>content</c> text (ADR-015, spec FR-09: TL;DR at most 1,000 characters).</summary>
    public const int MaxTextLength = 1000;

    /// <summary>A refused argument's <c>reasonCode</c> (the route has no argument errors: it clamps).</summary>
    public const string InvalidParameterReason = "briefing.invalid_parameter";

    /// <summary><c>reasonCode</c> of an AI briefing that is off, unconfigured or whose provider is unavailable (the route's 503).</summary>
    public const string UnavailableReason = "briefing.unavailable";

    /// <summary><c>reasonCode</c> of any other fault (the route's 500).</summary>
    public const string FailedReason = "briefing.failed";

    /// <summary><c>reasonCode</c> of a briefing that did not render inside the tool's 30 s budget.</summary>
    public const string TimeoutReason = "briefing.timeout";

    /// <summary>How long the render may run: the 30 s tool budget less a margin for the answer to reach the host.</summary>
    public static readonly TimeSpan GenerationBudget = McpLongRunning.Budget - TimeSpan.FromSeconds(3);

    /// <summary>The description the model reads (at most 1,024 characters, "use for / not for").</summary>
    public const string Description =
        "Use for: showing the signed-in user's Spaarke AI Daily Briefing, the same briefing the Spaarke Console shows them: a short summary "
        + "and key takeaways, and per section (tasks, documents, matters and so on) the items that need attention. Where the host shows widgets, "
        + "the briefing widget lists every section. Use it when the user asks what needs their attention today, what is due or overdue, or what "
        + "changed in their matters. The optional dueWithinDays (1 to 31, default 5) and recencyHours (1 to 744, default 120) set how far ahead and "
        + "back it looks. It can take up to half a minute. "
        + "Not for: one matter's summary or finances; searching records or documents; the Decide lane (use spaarke_show_decisions); "
        + "emailing or changing the briefing.";

    private static readonly JsonSerializerOptions Json = DecisionToolCall.Json;

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [DueWithinDaysArgument] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = 1,
                ["maximum"] = MaxDueWithinDays,
                ["description"] = "Optional. How many days ahead to look for due items. Default: 5.",
            },
            [RecencyHoursArgument] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = 1,
                ["maximum"] = MaxRecencyHours,
                ["description"] = "Optional. How many hours back to look for changed records. Default: 120.",
            },
        },
        ["additionalProperties"] = false,
    });

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            GetAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = Name,
                Title = "Get my daily briefing",
                Description = Description,
                // The Console's own load of the briefing is the same call: it reads the caller's records and writes only the capability
                // ledger entry and the proactive suggestions the route has always written (FR-15), never a user-visible change.
                ReadOnly = true,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
                // MCP Apps: the widget renders the result; visible to the model and to the app (the widget's Refresh calls this tool).
                Meta = new JsonObject
                {
                    ["ui"] = new JsonObject { ["resourceUri"] = WidgetUri, ["visibility"] = new JsonArray("model", "app") },
                },
            });

        // The input schema is stated, not inferred; the handler reads the raw arguments against the same closed set.
        tool.ProtocolTool.InputSchema = InputSchema;
        return tool;
    }

    private static Task<CallToolResult> GetAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        TimeProvider time,
        ILogger<GetBriefingTool> logger,
        CancellationToken cancellationToken)
    {
        var http = accessor.HttpContext;
        return DecisionToolCall.LogAroundAsync(
            logger, time, Name, http,
            () => RunAsync(request.Params?.Arguments, http, GenerationBudget, cancellationToken),
            RowCount);
    }

    internal static async Task<CallToolResult> RunAsync(
        IDictionary<string, JsonElement>? arguments, HttpContext? http, TimeSpan budget, CancellationToken cancellationToken)
    {
        var correlationId = http?.TraceIdentifier;
        if (!TryReadArguments(arguments, correlationId, out var windows, out var argumentError))
        {
            return argumentError!;
        }

        // The route's rules, as the caller. No caller is the method's UserContextRequired, before anything is read.
        DailyBriefingRenderResult rendered;
        using var budgeted = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budgeted.CancelAfter(budget);
        try
        {
            rendered = await DailyBriefingEndpoints.RenderForCallerAsync(http, windows, budgeted.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The budget ran out, not the client's request: say so, with a way forward.
            return McpToolResults.FromError(
                new McpToolError(
                    TimeoutReason,
                    "The daily briefing took longer than expected to prepare. Ask for it again in a moment, or open it in the Spaarke Console.",
                    StatusCodes.Status504GatewayTimeout),
                correlationId);
        }

        if (rendered.Outcome != DailyBriefingRenderOutcome.Rendered || rendered.Briefing == null)
        {
            return Error(rendered, correlationId);
        }

        var bounded = BriefingSizeBound.Fit(rendered.Briefing);
        return DecisionToolCall.Success(BuildText(bounded.Briefing, bounded.Omitted), bounded.Briefing);
    }

    /// <summary>
    /// The model-visible text: a counts line, then the TL;DR summary and key takeaways, every value on one line (user and record text can
    /// carry line breaks) and the whole text at most <see cref="MaxTextLength"/> characters. Nothing from the per-channel detail.
    /// </summary>
    internal static string BuildText(DailyBriefingNarrateResponse briefing, int omittedHighlights)
    {
        var inv = CultureInfo.InvariantCulture;
        var sections = briefing.ChannelNarratives.Count(c => c.Bullets.Length > 0);
        var highlights = briefing.ChannelNarratives.Sum(c => c.Bullets.Length);
        var failed = briefing.FailedChannels
            .Select(c => McpText.SingleLine(c).Trim())
            .Where(c => c.Length > 0)
            .ToArray();

        var head = new StringBuilder();
        if (sections == 0 && failed.Length == 0 && string.IsNullOrWhiteSpace(briefing.Tldr.Summary) && briefing.Tldr.KeyTakeaways.Length == 0)
        {
            return "Your daily briefing is empty: nothing needs attention right now.";
        }

        head.Append(string.Create(inv, $"Daily briefing: {sections} section(s), {highlights} highlight(s)."));
        if (omittedHighlights > 0)
        {
            head.Append(string.Create(inv, $" {omittedHighlights} more were left out to keep the result small; open the Spaarke Console for all of them."));
        }

        if (failed.Length > 0)
        {
            head.Append(" Could not load: ").Append(string.Join(", ", failed)).Append('.');
        }

        var text = new StringBuilder(Clip(head.ToString(), MaxTextLength));
        void Append(string line)
        {
            var room = MaxTextLength - text.Length - 1;
            if (room < 20)
            {
                return;
            }

            text.Append('\n').Append(Clip(line, room));
        }

        var summary = McpText.SingleLine(briefing.Tldr.Summary).Trim();
        if (summary.Length > 0)
        {
            Append(summary);
        }

        foreach (var takeaway in briefing.Tldr.KeyTakeaways.Select(t => McpText.SingleLine(t).Trim()).Where(t => t.Length > 0))
        {
            Append("- " + takeaway);
        }

        return text.ToString();
    }

    /// <summary>The value cut to at most <paramref name="max"/> characters, with an ellipsis when it was cut (never splitting a surrogate pair).</summary>
    private static string Clip(string value, int max)
    {
        if (value.Length <= max)
        {
            return value;
        }

        var end = max - 1;
        if (end > 0 && char.IsHighSurrogate(value[end - 1]))
        {
            end--;
        }

        return string.Concat(value.AsSpan(0, end), "…");
    }

    private static CallToolResult Error(DailyBriefingRenderResult result, string? correlationId)
    {
        var error = result.Outcome switch
        {
            DailyBriefingRenderOutcome.UserContextRequired or DailyBriefingRenderOutcome.NotADataverseUser => new McpToolError(
                DataverseUserClientErrorCodes.UserContextRequired,
                "Spaarke could not confirm your Spaarke user account. If you are signed in with a licensed Spaarke account, try again shortly.",
                result.Status),
            DailyBriefingRenderOutcome.Unavailable => new McpToolError(
                UnavailableReason,
                "The AI daily briefing is not available right now. Try again shortly.",
                StatusCodes.Status503ServiceUnavailable),
            _ => new McpToolError(
                FailedReason,
                "The daily briefing could not be prepared. Try again shortly.",
                StatusCodes.Status500InternalServerError),
        };

        return McpToolResults.FromError(error, correlationId);
    }

    /// <summary>The two optional windows, or an <see cref="InvalidParameterReason"/> error naming the fix (never an argument value).</summary>
    private static bool TryReadArguments(
        IDictionary<string, JsonElement>? arguments, string? correlationId, out RenderDailyBriefingRequest? windows, out CallToolResult? error)
    {
        windows = null;
        error = null;
        if (arguments == null || arguments.Count == 0)
        {
            return true;
        }

        if (arguments.Keys.Any(k => k is not (DueWithinDaysArgument or RecencyHoursArgument)))
        {
            error = InvalidArgument($"{Name} takes only the optional arguments '{DueWithinDaysArgument}' and '{RecencyHoursArgument}'. Send the call again without the others.", correlationId);
            return false;
        }

        if (!TryReadWindow(arguments, DueWithinDaysArgument, MaxDueWithinDays, correlationId, out var dueWithinDays, out error)
            || !TryReadWindow(arguments, RecencyHoursArgument, MaxRecencyHours, correlationId, out var recencyHours, out error))
        {
            return false;
        }

        windows = dueWithinDays == null && recencyHours == null
            ? null
            : new RenderDailyBriefingRequest { DueWithinDays = dueWithinDays, RecencyHours = recencyHours };
        return true;
    }

    private static bool TryReadWindow(
        IDictionary<string, JsonElement> arguments, string name, int max, string? correlationId, out int? value, out CallToolResult? error)
    {
        value = null;
        error = null;
        if (!arguments.TryGetValue(name, out var element) || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var number) || number < 1 || number > max)
        {
            error = InvalidArgument($"'{name}' must be a whole number from 1 to {max}. Leave it out for the default.", correlationId);
            return false;
        }

        value = number;
        return true;
    }

    private static CallToolResult InvalidArgument(string message, string? correlationId) =>
        McpToolResults.FromError(new McpToolError(InvalidParameterReason, message, StatusCodes.Status400BadRequest), correlationId);

    /// <summary>The highlights in a successful result (a count only); 0 for an error.</summary>
    private static int RowCount(CallToolResult result)
    {
        if (result.IsError == true
            || result.StructuredContent is not { ValueKind: JsonValueKind.Object } body
            || !body.TryGetProperty("channelNarratives", out var channels)
            || channels.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return channels.EnumerateArray().Sum(c => c.TryGetProperty("bullets", out var b) && b.ValueKind == JsonValueKind.Array ? b.GetArrayLength() : 0);
    }

    /// <summary>The serialized size of <paramref name="briefing"/> as the result writes it (the default, escape-everything encoder).</summary>
    internal static int SerializedSize(DailyBriefingNarrateResponse briefing) => JsonSerializer.SerializeToUtf8Bytes(briefing, Json).Length;
}

/// <summary>
/// Keeps <c>spaarke_get_briefing</c>'s result under 64 KiB (NFR-02) at the route's worst case (up to 50 rows in each of the channels the
/// collector reads): a briefing that fits is returned untouched; one that does not loses the bullets' source descriptions first (the widget
/// only truncates them beneath the title), then trailing highlights, the same number from every section so no section disappears, until it fits.
/// </summary>
internal static class BriefingSizeBound
{
    /// <summary>
    /// The bytes the structured briefing may take (the envelope and the text are far below the 8 KiB left to 64 KiB), measured as the
    /// result is written: the default, escape-everything encoder counts a non-ASCII character as up to 6 bytes.
    /// </summary>
    public const int BudgetBytes = 56 * 1024;

    /// <summary>The briefing that goes in the result and how many highlights were left out to make it fit (0 when it fit as it is).</summary>
    public static (DailyBriefingNarrateResponse Briefing, int Omitted) Fit(DailyBriefingNarrateResponse briefing)
    {
        if (GetBriefingTool.SerializedSize(briefing) <= BudgetBytes)
        {
            return (briefing, 0);
        }

        // 1. The source descriptions.
        var trimmed = briefing with
        {
            ChannelNarratives = [.. briefing.ChannelNarratives.Select(c => c with { Bullets = [.. c.Bullets.Select(b => b with { Description = string.Empty })] })],
        };
        if (GetBriefingTool.SerializedSize(trimmed) <= BudgetBytes)
        {
            return (trimmed, 0);
        }

        // 2. Trailing highlights: the first N of every section, N halving until it fits (N = 0 leaves the summary and the sections' names).
        var original = briefing.ChannelNarratives.Sum(c => c.Bullets.Length);
        var keep = trimmed.ChannelNarratives.Select(c => c.Bullets.Length).DefaultIfEmpty(0).Max();
        while (true)
        {
            keep /= 2;
            var cut = trimmed with
            {
                ChannelNarratives = [.. trimmed.ChannelNarratives.Select(c => c with { Bullets = [.. c.Bullets.Take(keep)] })],
            };
            if (keep == 0 || GetBriefingTool.SerializedSize(cut) <= BudgetBytes)
            {
                return (cut, original - cut.ChannelNarratives.Sum(c => c.Bullets.Length));
            }
        }
    }
}
