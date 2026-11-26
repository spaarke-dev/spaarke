using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events.Dtos;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Services.Events;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Tasks;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 025 (spec FR-07 read half, FR-15, NFR-02, NFR-05, NFR-07; design §3).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md; spec "Placement & New Components"). A thin MCP
// adapter inside the BFF over IEventsCapability (task 006), the one owner of the event list rules, on the /mcp/legal-operations host:
// no package, no DI registration (found by [McpArea]), no background work, no AI-internal type.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: GET /api/v1/events (EventEndpoints.GetEventsAsync over EventsCapability.ListCoreAsync) and the capability's instance
//     ListAsync, which task 006 built for exactly this caller.
//   Extension: this IS the extension: the capability is reused unchanged and adds no filter of its own here; the only new logic is the
//     argument check, the slim row and the byte budget.
//   Cost of doing nothing: Copilot users cannot see their Spaarke to-dos (FR-07 read half fails).
// =====================================================================================================================

/// <summary>
/// <c>spaarke_list_tasks</c> (FR-07): the signed-in user's Spaarke to-dos and tasks (events), with the filters of
/// <c>GET /api/v1/events</c>. Read-only, model-visible, no widget.
/// </summary>
/// <remarks>
/// <para><b>One decision, one answer.</b> The tool calls <see cref="IEventsCapability.ListAsync"/> with exactly
/// <c>IHttpContextAccessor.HttpContext</c> and adds no filter, scope or check of its own (ADR-028, spec "MUST NOT copy authorization").
/// The capability runs the list impersonated AS the caller and narrowed to "my events" (owner, assigned contact or creator), fails
/// closed on a caller with no Dataverse user, and is the same method the REST route's core runs, so the tool's ids are the route's
/// ids for the same caller and filters (<c>TaskReadToolsContractTests</c>), and another user's private event is never listed
/// (<c>TaskReadToolsMustNotLeakTests</c>).</para>
/// <para><b>Rows (ADR-015, NFR-07).</b> <c>structuredContent</c> is <c>{ tasks, count, totalCount, pageNumber, pageSize, hasNextPage, truncated }</c>;
/// a task is <c>{ id, subject, dueDate, status, priority, regardingRecordName }</c>. No description, event type, owner or audit field.
/// The row has no link: a link is about as large as the rest of the row, which would pass 64 KiB at 200 tasks (NFR-02);
/// <c>spaarke_get_task</c> returns it. <c>content</c> is one line per task. Rows are kept while their measured cost fits
/// <see cref="TaskToolSupport.RowBudgetBytes"/>; a cut list says <c>truncated: true</c> and asks for a narrower filter.</para>
/// <para><b>Failures</b> are <c>isError</c> results with the REST route's <c>reasonCode</c> and status (a 400 for a request member
/// the route refuses, 403 <c>sdap.access.deny.caller_unresolved</c>, 401 <c>UserContextRequired</c>) or <c>events.list_failed</c> (REST:
/// a bare 500), with fixed text; a fault's message and the capability's detail never reach the model.</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class ListTasksTool : McpToolDefinition
{
    internal const string ToolName = "spaarke_list_tasks";

    internal const string StatusArgument = "status";
    internal const string PriorityArgument = "priority";
    internal const string DueDateFromArgument = "dueDateFrom";
    internal const string DueDateToArgument = "dueDateTo";
    internal const string RegardingRecordTypeArgument = "regardingRecordType";
    internal const string RegardingRecordIdArgument = "regardingRecordId";
    internal const string PageNumberArgument = "pageNumber";
    internal const string PageSizeArgument = "pageSize";

    /// <summary>The page size when none is sent: the route's own default.</summary>
    internal const int DefaultPageSize = 50;

    /// <summary>The largest page the route (and the capability) serves.</summary>
    internal const int MaxPageSize = 100;

    private const string FailedMessage = "The tasks could not be read. Try again shortly.";

    private static readonly string[] Statuses = ["open", "completed", "cancelled"];

    private static readonly string[] AllArguments =
    [
        StatusArgument, PriorityArgument, DueDateFromArgument, DueDateToArgument,
        RegardingRecordTypeArgument, RegardingRecordIdArgument, PageNumberArgument, PageSizeArgument,
    ];

    private static readonly McpToolError FailedError =
        new(TaskToolSupport.ListFailedReasonCode, FailedMessage, StatusCodes.Status500InternalServerError);

    /// <summary>The description the model reads (at most 1,024 characters, "use for / not for").</summary>
    internal const string Description =
        "Use for: listing the signed-in user's Spaarke to-dos and tasks (Spaarke events: work items with a due date, such as filings, "
        + "follow-ups and reviews that the user owns, is assigned or created): each task's subject, due date, status, priority and the "
        + "matter or project it is regarding, and its id to open it with spaarke_get_task. Filter by status (open, completed or "
        + "cancelled), priority, a due date range (yyyy-MM-dd), or the record it is regarding (regardingRecordType 1 for a matter, 0 for "
        + "a project, plus regardingRecordId); up to 100 per page, pageNumber for more. Not for: calendar meetings or Outlook events; "
        + "the Decide lane (use spaarke_show_decisions); creating or completing a task.";

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [StatusArgument] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray(Statuses.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()),
                ["description"] = "Only tasks in this state. Leave out for every state. Use 'open' for the user's outstanding to-dos.",
            },
            [PriorityArgument] = new JsonObject
            {
                ["type"] = "integer",
                ["enum"] = new JsonArray(EventPriority.All.Select(p => (JsonNode)JsonValue.Create(p.Value)!).ToArray()),
                ["description"] = "Only tasks of this priority: "
                    + string.Join(", ", EventPriority.All.Select(p => $"{p.Value} = {p.Label}")) + ".",
            },
            [DueDateFromArgument] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Only tasks due on or after this date, as yyyy-MM-dd.",
            },
            [DueDateToArgument] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Only tasks due on or before this date, as yyyy-MM-dd.",
            },
            [RegardingRecordTypeArgument] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = 0,
                ["maximum"] = 7,
                ["description"] = "The kind of record the tasks are filed under: 0 project, 1 matter, 2 invoice, 3 analysis, 4 account, 5 contact, 6 work assignment, 7 budget.",
            },
            [RegardingRecordIdArgument] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The id (GUID) of the record the tasks are filed under. Send it with regardingRecordType.",
            },
            [PageNumberArgument] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = 1,
                ["description"] = "The page to return, from 1. Default 1.",
            },
            [PageSizeArgument] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = 1,
                ["maximum"] = MaxPageSize,
                ["description"] = $"Tasks per page, 1 to {MaxPageSize}. Default {DefaultPageSize}.",
            },
        },
        ["additionalProperties"] = false,
    });

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            ListAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = ToolName,
                Title = "List my tasks",
                Description = Description,
                ReadOnly = true,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
            });

        // The input schema is stated, not inferred; the handler reads the raw arguments against the same closed set.
        tool.ProtocolTool.InputSchema = InputSchema;
        return tool;
    }

    /// <summary>The handler the SDK binds: <see cref="RunAsync"/> inside the one per-call telemetry line (NFR-05; rows = tasks returned).</summary>
    internal static Task<CallToolResult> ListAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        IEventsCapability events,
        TimeProvider time,
        ILogger<ListTasksTool> logger,
        CancellationToken cancellationToken) =>
        DecisionToolCall.LogAroundAsync(
            logger, time, ToolName, accessor.HttpContext,
            () => RunAsync(request.Params?.Arguments, accessor.HttpContext, events, logger, cancellationToken),
            RowsOf);

    internal static async Task<CallToolResult> RunAsync(
        IDictionary<string, JsonElement>? arguments,
        HttpContext? http,
        IEventsCapability events,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // ADR-028 A10: no caller, no read. Checked before anything else, with the test every tool applies; the capability checks it again.
        if (!PlanAndLookupToolSupport.HasCaller(http))
        {
            return TaskToolSupport.Failure(TaskToolSupport.UserContextRequired, http);
        }

        if (!TryReadQuery(arguments, http, out var query, out var argumentError))
        {
            return argumentError!;
        }

        EventCapabilityResult<EventListResponse> result;
        try
        {
            result = await events.ListAsync(http, query, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // ADR-015: the exception type only (a Dataverse fault message can carry record text). A caller who has gone away rethrows.
            logger.LogWarning("MCP tool {ToolName}: the capability faulted ({ExceptionType}).", ToolName, ex.GetType().Name);
            return TaskToolSupport.Failure(FailedError, http);
        }

        if (result.Status == EventCapabilityStatus.Ok && result.Value is { } page)
        {
            return ResultOf(page);
        }

        // The capability answers a fault as a status; a call the caller abandoned is not a fault.
        cancellationToken.ThrowIfCancellationRequested();
        return TaskToolSupport.Failure(
            TaskToolSupport.ErrorOf(result.Status, result.ReasonCode, result.Message, TaskToolSupport.ListFailedReasonCode, FailedMessage), http);
    }

    /// <summary>
    /// The success result for the capability's page: slim rows kept while they fit <see cref="TaskToolSupport.RowBudgetBytes"/>, one text
    /// line each. Pure: the size bound (NFR-02) is proved on it directly.
    /// </summary>
    internal static CallToolResult ResultOf(EventListResponse page)
    {
        var rows = new List<TaskRow>(page.Items.Length);
        var cost = 0;
        foreach (var task in page.Items)
        {
            var row = TaskToolSupport.RowOf(task, TaskToolSupport.ListSubjectBytes, TaskToolSupport.ListRegardingNameBytes);
            cost += TaskToolSupport.CostOf(row);
            if (cost > TaskToolSupport.RowBudgetBytes)
            {
                break;
            }

            rows.Add(row);
        }

        var truncated = rows.Count < page.Items.Length;
        var body = new ListBody(rows, rows.Count, page.TotalCount, page.PageNumber, page.PageSize, page.HasNextPage, truncated);

        var header = rows.Count == 0
            ? "No tasks match."
            : $"{rows.Count} task(s) on page {page.PageNumber} of {page.TotalPages} ({page.TotalCount} match)"
              + (truncated ? "; the list was cut to fit, so narrow it with status, a due date range or a smaller pageSize."
                  : page.HasNextPage ? $"; send pageNumber {page.PageNumber + 1} for more." : ".");
        var text = rows.Count == 0
            ? header
            : header + "\n" + string.Join("\n", rows.Select(r => "- " + TaskToolSupport.LineOf(r)));
        return DecisionToolCall.Success(text, body);
    }

    /// <summary>
    /// The capability's query for <paramref name="arguments"/>, or an error result when the call carries any other argument or a value of
    /// the wrong kind. The schema says <c>additionalProperties: false</c>; this is what makes it true, so an invented argument is refused
    /// with the fix, never ignored. Values the capability judges (a GUID's text, a priority's value, a regarding type's range) are passed
    /// on, so REST's rules and texts stay the only ones.
    /// </summary>
    internal static bool TryReadQuery(
        IDictionary<string, JsonElement>? arguments, HttpContext? http, out EventListQuery query, out CallToolResult? error)
    {
        query = new EventListQuery { PageSize = DefaultPageSize };
        error = null;
        var fix = $"{ToolName} takes only these optional arguments: {string.Join(", ", AllArguments)}. Send the call again with just those, each of the kind its schema says.";
        if (arguments is null)
        {
            return true;
        }

        if (arguments.Keys.Any(k => !AllArguments.Contains(k, StringComparer.Ordinal)))
        {
            error = TaskToolSupport.InvalidArgument(fix, http);
            return false;
        }

        string? status = null;
        string? regardingId = null;
        int? priority = null;
        int? regardingType = null;
        DateTime? from = null;
        DateTime? to = null;
        var pageNumber = 1;
        var pageSize = DefaultPageSize;

        foreach (var (name, element) in arguments)
        {
            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            var ok = true;
            switch (name)
            {
                case StatusArgument:
                    ok = TryText(element, out var s) && Statuses.Contains(s!.ToLowerInvariant());
                    status = ok ? s!.ToLowerInvariant() : null;
                    break;
                case RegardingRecordIdArgument:
                    ok = TryText(element, out regardingId);
                    break;
                case DueDateFromArgument:
                    ok = TryDate(element, out var f);
                    from = ok ? f : null;
                    break;
                case DueDateToArgument:
                    ok = TryDate(element, out var t);
                    to = ok ? t : null;
                    break;
                case PriorityArgument:
                    ok = TryInt(element, out var p);
                    priority = ok ? p : null;
                    break;
                case RegardingRecordTypeArgument:
                    ok = TryInt(element, out var rt);
                    regardingType = ok ? rt : null;
                    break;
                case PageNumberArgument:
                    ok = TryInt(element, out pageNumber) && pageNumber >= 1;
                    break;
                case PageSizeArgument:
                    ok = TryInt(element, out pageSize) && pageSize >= 1 && pageSize <= MaxPageSize;
                    break;
            }

            if (!ok)
            {
                error = TaskToolSupport.InvalidArgument($"'{name}' is not valid. {fix}", http);
                return false;
            }
        }

        query = new EventListQuery
        {
            Status = status,
            Priority = priority,
            RegardingRecordType = regardingType,
            RegardingRecordId = regardingId,
            DueDateFrom = from,
            DueDateTo = to,
            PageNumber = pageNumber,
            PageSize = pageSize,
        };
        return true;
    }

    private static bool TryText(JsonElement element, out string? text)
    {
        text = element.ValueKind == JsonValueKind.String ? element.GetString()?.Trim() : null;
        return !string.IsNullOrEmpty(text);
    }

    private static bool TryInt(JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }

    private static bool TryDate(JsonElement element, out DateTime date)
    {
        date = default;
        return TryText(element, out var text)
               && DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    /// <summary>The row count of a result for the telemetry line (a count only, never the rows).</summary>
    private static int RowsOf(CallToolResult result) =>
        result.IsError != true && result.StructuredContent is { ValueKind: JsonValueKind.Object } body
        && body.TryGetProperty("count", out var count) && count.TryGetInt32(out var n)
            ? n
            : 0;

    /// <summary><c>structuredContent</c> of <c>spaarke_list_tasks</c>.</summary>
    internal sealed record ListBody(
        IReadOnlyList<TaskRow> Tasks, int Count, int TotalCount, int PageNumber, int PageSize, bool HasNextPage, bool Truncated);
}
