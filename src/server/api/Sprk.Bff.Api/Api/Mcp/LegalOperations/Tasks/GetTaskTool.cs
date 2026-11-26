using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Events.Dtos;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Documents;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Events;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Tasks;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 025 (spec FR-07 read half, FR-15, NFR-02, NFR-05, NFR-07; design §3).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md; spec "Placement & New Components"). A thin MCP
// adapter inside the BFF over IEventsCapability (task 006), the one owner of the event get rules, on the /mcp/legal-operations host:
// no package, no DI registration (found by [McpArea]), no background work, no AI-internal type.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: GET /api/v1/events/{id} (the record-route filter's Read decision, then EventsCapability.GetCoreAsync) and the
//     capability's instance GetAsync, which makes the same decision through RecordRouteAccessAuthorizationFilter.DecideRouteRecordAsync.
//   Extension: this IS the extension: the capability is reused unchanged; the only new logic is the slim projection and the open link.
//   Cost of doing nothing: Copilot users cannot open one of their Spaarke to-dos by id (FR-07 read half fails).
// =====================================================================================================================

/// <summary>
/// <c>spaarke_get_task</c> (FR-07): one Spaarke to-do or task (event) by id, for a caller who may read it. Read-only, model-visible, no widget.
/// </summary>
/// <remarks>
/// <para><b>One decision, one answer.</b> The tool calls <see cref="IEventsCapability.GetAsync"/> with exactly
/// <c>IHttpContextAccessor.HttpContext</c> and copies no check. The capability asks the caller's Read on the event (the decision the REST
/// route's record filter asks); an event the caller may not read and one that does not exist give the same <c>isError</c> not-found
/// (<see cref="TaskToolSupport.NotFound"/>), as REST's uniform 404 does (FR-15).</para>
/// <para><b>Result.</b> <c>structuredContent</c> is the slim event: <c>{ id, subject, dueDate, status, priority, regardingRecordName,
/// regardingRecordType, regardingRecordId, link }</c> (ADR-015: no description, owner or audit field). <c>regardingRecordName</c> is the
/// value the event row stores, the one <c>GET /api/v1/events/{id}</c> returns too (notes/025-decisions.md D4). <c>link</c> is the record
/// form on <c>Dataverse:EnvironmentUrl</c> (absent when that is not an https URL).</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class GetTaskTool : McpToolDefinition
{
    internal const string ToolName = "spaarke_get_task";

    internal const string TaskIdArgument = "taskId";

    private const string FailedMessage = "The task could not be read. Try again shortly.";

    private static readonly McpToolError FailedError =
        new(TaskToolSupport.GetFailedReasonCode, FailedMessage, StatusCodes.Status500InternalServerError);

    /// <summary>The description the model reads (at most 1,024 characters, "use for / not for").</summary>
    internal const string Description =
        "Use for: opening one Spaarke to-do or task (a Spaarke event) the signed-in user may read, by its id: its subject, due date, "
        + "status, priority, the matter or project it is regarding, and a link that opens it in Spaarke. Needs the task's id, from "
        + "spaarke_list_tasks. Not for: listing or searching tasks (use spaarke_list_tasks); calendar meetings or Outlook events; "
        + "creating or completing a task. A task that does not exist and one the user may not read return the same not-found answer.";

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [TaskIdArgument] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The id (GUID) of the task, from spaarke_list_tasks.",
            },
        },
        ["required"] = new JsonArray(TaskIdArgument),
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
                Title = "Get a task",
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

    /// <summary>The handler the SDK binds: <see cref="RunAsync"/> inside the one per-call telemetry line (NFR-05; rows = 1 when a task was returned).</summary>
    internal static Task<CallToolResult> GetAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        IEventsCapability events,
        IOptions<DataverseOptions> dataverse,
        TimeProvider time,
        ILogger<GetTaskTool> logger,
        CancellationToken cancellationToken) =>
        DecisionToolCall.LogAroundAsync(
            logger, time, ToolName, accessor.HttpContext,
            () => RunAsync(request.Params?.Arguments, accessor.HttpContext, events, dataverse.Value.EnvironmentUrl, logger, cancellationToken),
            result => result.IsError == true ? 0 : 1);

    internal static async Task<CallToolResult> RunAsync(
        IDictionary<string, JsonElement>? arguments,
        HttpContext? http,
        IEventsCapability events,
        string? environmentUrl,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // ADR-028 A10: no caller, no read. Checked before anything else, with the test every tool applies; the capability checks it again.
        if (!PlanAndLookupToolSupport.HasCaller(http))
        {
            return TaskToolSupport.Failure(TaskToolSupport.UserContextRequired, http);
        }

        if (!DocumentToolSupport.TryReadStringArguments(arguments, ToolName, [TaskIdArgument], http, out var values, out var argumentError))
        {
            return argumentError!;
        }

        if (!Guid.TryParse(values[0], out var taskId) || taskId == Guid.Empty)
        {
            return TaskToolSupport.Failure(
                new McpToolError(DocumentToolSupport.InvalidIdReasonCode, $"'{TaskIdArgument}' must be the task's id (a GUID).", StatusCodes.Status400BadRequest), http);
        }

        EventCapabilityResult<EventDto> result;
        try
        {
            result = await events.GetAsync(http, taskId, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // ADR-015: the exception type only. A caller who has gone away rethrows.
            logger.LogWarning("MCP tool {ToolName}: the capability faulted ({ExceptionType}).", ToolName, ex.GetType().Name);
            return TaskToolSupport.Failure(FailedError, http);
        }

        if (result.Status == EventCapabilityStatus.Ok && result.Value is { } task)
        {
            return ResultOf(task, DecisionsTextSummary.ConsoleBaseUrl(environmentUrl));
        }

        // The capability answers a fault as a status; a call the caller abandoned is not a fault.
        cancellationToken.ThrowIfCancellationRequested();
        return TaskToolSupport.Failure(
            TaskToolSupport.ErrorOf(result.Status, result.ReasonCode, result.Message, TaskToolSupport.GetFailedReasonCode, FailedMessage), http);
    }

    /// <summary>The success result for the capability's event: the slim event, its one text line and its open link. Pure.</summary>
    internal static CallToolResult ResultOf(EventDto task, string? consoleBaseUrl)
    {
        var row = TaskToolSupport.RowOf(task, TaskToolSupport.GetSubjectBytes, TaskToolSupport.GetRegardingNameBytes);
        var regardingType = task.RegardingRecordTypeName is { Length: > 0 } typeName && task.RegardingRecordType is not null ? typeName : null;
        var body = new GetBody(
            row.Id, row.Subject, row.DueDate, row.Status, row.Priority, row.RegardingRecordName,
            regardingType, string.IsNullOrWhiteSpace(task.RegardingRecordId) ? null : task.RegardingRecordId,
            TaskToolSupport.OpenLink(consoleBaseUrl, task.Id));

        var text = TaskToolSupport.LineOf(row)
                   + (row.RegardingRecordName is { } regarding ? $"\nRegarding {McpText.SingleLine(regardingType ?? "record").ToLowerInvariant()}: {McpText.SingleLine(regarding)}" : string.Empty)
                   + (body.Link is { } link ? $"\nOpen in Spaarke: {link}" : string.Empty);
        return DecisionToolCall.Success(text, body);
    }

    /// <summary><c>structuredContent</c> of <c>spaarke_get_task</c>: the slim event and its link.</summary>
    internal sealed record GetBody(
        string Id, string Subject, string? DueDate, string Status, string? Priority, string? RegardingRecordName,
        string? RegardingRecordType, string? RegardingRecordId, string? Link);
}
