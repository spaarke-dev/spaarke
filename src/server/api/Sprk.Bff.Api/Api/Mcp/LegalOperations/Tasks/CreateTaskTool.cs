using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events.Dtos;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Documents;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Events;
using Sprk.Bff.Api.Services.Signals;
using ApiCreateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.CreateEventRequest;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Tasks;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 036 (spec FR-07 write half, FR-15, NFR-05, NFR-06, NFR-07; ADR-002, ADR-015, ADR-028).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md; spec "Placement & New Components"). A thin MCP
// adapter inside the BFF over IEventsCapability (task 006), the one owner of the event create rules, on the /mcp/legal-operations host:
// no package, no DI registration (found by [McpArea]), no background work, no AI-internal type.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: POST /api/v1/events (the route's shape filter, then the record-route filter's Create privilege and Read + AppendTo on
//     the regarding record, then the one create) and the capability's instance CreateAsync, which makes the same decisions;
//     McpIdempotency (task 031's retry key), TaskToolSupport (task 025), McpToolResults.
//   Extension: this IS the extension: the capability and the retry helper are reused unchanged; the new logic is the argument check,
//     the capability-outcome to tool-error mapping for a create, and the retry entry.
//   Cost of doing nothing: Copilot users cannot create a Spaarke to-do (FR-07 write half fails) and "a retried create with the same
//     key creates one record" cannot be met.
// =====================================================================================================================

/// <summary>
/// <c>spaarke_create_task</c> (FR-07): creates one Spaarke to-do or task (an event) for the signed-in caller, optionally filed under a
/// matter, project or another regarding record. A model-callable reversible write (NFR-06): not destructive, idempotent, with a required
/// <c>idempotencyKey</c>.
/// </summary>
/// <remarks>
/// <para><b>One decision, one answer.</b> The tool calls <see cref="IEventsCapability.CreateAsync"/> with exactly
/// <c>IHttpContextAccessor.HttpContext</c> and copies no check. The capability asks, as the caller, the Create privilege on
/// <c>sprk_event</c> and Read + AppendTo on the regarding record (<c>event.attach_regarding</c>, #3181), validates the shape with the
/// route's own rules and texts, and makes the one create. Every refusal is an <c>isError</c> result with the REST route's
/// <c>reasonCode</c> and writes nothing.</para>
/// <para><b>Retry key.</b> <see cref="McpIdempotency"/>: per caller and key, a retry with the same inputs answers the first event's id and
/// writes nothing; the same key with different inputs is refused; a different key is a new task. A create that was refused wrote nothing and is
/// not remembered, so it can be retried with the same key. A create that faulted (the row may exist) is remembered as "outcome unknown": a
/// retry answers "may or may not have been created, check spaarke_list_tasks" and creates nothing. Once the arguments are valid the create runs
/// with no caller token, so a caller who goes away cannot cancel it between the row and its audit write.</para>
/// <para><b>Result.</b> <c>structuredContent</c> is <c>{ id, subject, dueDate, status, link, replayed }</c> (ADR-015: no description, owner
/// or audit field). Logs carry the tool name, latency, outcome or reason code and correlation id only, never the subject or description.</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class CreateTaskTool : McpToolDefinition
{
    internal const string ToolName = "spaarke_create_task";

    internal const string SubjectArgument = "subject";
    internal const string DescriptionArgument = "description";
    internal const string DueDateArgument = "dueDate";
    internal const string PriorityArgument = "priority";
    internal const string RegardingRecordTypeArgument = "regardingRecordType";
    internal const string RegardingRecordIdArgument = "regardingRecordId";
    internal const string IdempotencyKeyArgument = McpToolAuthorization.IdempotencyKeyArgument;

    /// <summary>The longest retry key accepted, in characters (task 031's bound).</summary>
    internal const int IdempotencyKeyMaxLength = 200;

    /// <summary>The longest subject accepted, in characters: the length the get tool shows back.</summary>
    internal const int SubjectMaxLength = TaskToolSupport.GetSubjectBytes;

    /// <summary>The longest description accepted, in characters (the tool keeps the 2,000 characters of a playbook parameter value).</summary>
    internal const int DescriptionMaxLength = 2_000;

    internal const string RetryResource = "mcp-create-task";

    internal const string LockResource = "mcp-create-task-lock";

    /// <summary>The <c>reasonCode</c> of a create that faulted or was refused for a reason REST does not name (REST: a bare 500).</summary>
    internal const string CreateFailedReasonCode = "events.create_failed";

    private const string FailedMessage = "The task could not be created. Nothing was saved. Try again shortly.";

    /// <summary>
    /// What a create whose outcome is unknown answers, now and on every retry with the same key: the row may already exist (the app-only
    /// create can fault after it landed, and anything after it can throw), so neither "saved" nor "nothing was saved" is true.
    /// </summary>
    private const string OutcomeUnknownMessage =
        "The task may or may not have been created. Check the matter's tasks with spaarke_list_tasks before creating it again.";

    private static readonly McpIdempotencyScope Scope = new(ToolName, RetryResource, LockResource);

    private static readonly string[] Arguments =
    [
        SubjectArgument, DescriptionArgument, DueDateArgument, PriorityArgument,
        RegardingRecordTypeArgument, RegardingRecordIdArgument, IdempotencyKeyArgument,
    ];

    internal static McpToolError Failed { get; } =
        new(CreateFailedReasonCode, FailedMessage, StatusCodes.Status500InternalServerError);

    /// <summary>A create that faulted where a row may already exist: same code and status as <see cref="Failed"/>, text that claims nothing about what was saved.</summary>
    internal static McpToolError OutcomeUnknown { get; } =
        new(CreateFailedReasonCode, OutcomeUnknownMessage, StatusCodes.Status500InternalServerError);

    /// <summary>The description the model reads (at most 1,024 characters, "use for / not for").</summary>
    internal const string Description =
        "Use for: creating one Spaarke to-do or task (a Spaarke event) for the signed-in user, optionally filed under a matter or "
        + "project (regardingRecordType 1 for a matter, 0 for a project, plus regardingRecordId). Needs a subject and an idempotencyKey "
        + "you make up once per task and send again unchanged on a retry, so a retry never creates a second task. Optional: a "
        + "description, a dueDate (yyyy-MM-dd) and a priority. It answers the task's id and a link that opens it in Spaarke. The user "
        + "may only file a task under a record they can read and attach to. Not for: calendar meetings or Outlook events; listing or "
        + "opening tasks (use spaarke_list_tasks, spaarke_get_task); completing a task (use spaarke_complete_task).";

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [SubjectArgument] = new JsonObject
            {
                ["type"] = "string",
                ["maxLength"] = SubjectMaxLength,
                ["description"] = "What the task is, in a few words.",
            },
            [DescriptionArgument] = new JsonObject
            {
                ["type"] = "string",
                ["maxLength"] = DescriptionMaxLength,
                ["description"] = "Optional: more detail about the task.",
            },
            [DueDateArgument] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional: the day the task is due, as yyyy-MM-dd.",
            },
            [PriorityArgument] = new JsonObject
            {
                ["type"] = "integer",
                ["enum"] = new JsonArray(EventPriority.All.Select(p => (JsonNode)JsonValue.Create(p.Value)!).ToArray()),
                ["description"] = "Optional: the task's priority: "
                    + string.Join(", ", EventPriority.All.Select(p => $"{p.Value} = {p.Label}")) + ".",
            },
            [RegardingRecordTypeArgument] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = 0,
                ["maximum"] = 7,
                ["description"] = "Optional: the kind of record to file the task under: 0 project, 1 matter, 2 invoice, 3 analysis, 4 account, 5 contact, 6 work assignment, 7 budget. Send it with regardingRecordId.",
            },
            [RegardingRecordIdArgument] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional: the id (GUID) of the record to file the task under. Send it with regardingRecordType.",
            },
            [IdempotencyKeyArgument] = new JsonObject
            {
                ["type"] = "string",
                ["maxLength"] = IdempotencyKeyMaxLength,
                ["description"] = "A value you make up once for this task (for example a GUID) and send again unchanged on a retry.",
            },
        },
        ["required"] = new JsonArray(SubjectArgument, IdempotencyKeyArgument),
        ["additionalProperties"] = false,
    });

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            CreateAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = ToolName,
                Title = "Create a task",
                Description = Description,
                ReadOnly = false,
                Destructive = false,
                Idempotent = true,
                OpenWorld = false,
            });

        // The input schema is stated, not inferred; the handler reads the raw arguments against the same closed set.
        tool.ProtocolTool.InputSchema = InputSchema;
        return tool;
    }

    /// <summary>The handler the SDK binds: <see cref="RunAsync"/> inside the one per-call telemetry line (NFR-05; rows = 1 when a task was returned).</summary>
    internal static Task<CallToolResult> CreateAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        ITenantCache cache,
        IEventsCapability events,
        IOptions<DataverseOptions> dataverse,
        TimeProvider time,
        ILogger<CreateTaskTool> logger,
        CancellationToken cancellationToken) =>
        DecisionToolCall.LogAroundAsync(
            logger, time, ToolName, accessor.HttpContext,
            () => RunAsync(request.Params?.Arguments, accessor.HttpContext, cache, events, dataverse.Value.EnvironmentUrl, logger, cancellationToken),
            result => result.IsError == true ? 0 : 1);

    internal static async Task<CallToolResult> RunAsync(
        IDictionary<string, JsonElement>? arguments,
        HttpContext? http,
        ITenantCache cache,
        IEventsCapability events,
        string? environmentUrl,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // ADR-028 A10: no caller, no write. Checked before anything else; the capability checks it again. The retry key is per caller, so
        // a principal with no object id cannot hold one either.
        if (!PlanAndLookupToolSupport.HasCaller(http) || string.IsNullOrEmpty(CallerResolution.ResolveObjectId(http!.User)))
        {
            return TaskToolSupport.Failure(TaskToolSupport.UserContextRequired, http);
        }

        if (!TryReadArguments(arguments, http, out var input, out var argumentError))
        {
            return argumentError!;
        }

        var consoleBaseUrl = DecisionsTextSummary.ConsoleBaseUrl(environmentUrl);
        var fingerprint = FingerprintOf(input);

        return await McpIdempotency.RunOnceAsync<RetryEntry>(
            Scope, cache, http, input.IdempotencyKey, fingerprint,
            replay: earlier => earlier.OutcomeUnknown
                ? TaskToolSupport.Failure(OutcomeUnknown, http)
                : ResultOf(input.Request, earlier.EventId, consoleBaseUrl, replayed: true),
            run: async () =>
            {
                // Once the arguments are valid and the create is about to run, a caller who goes away must not cancel it in flight: a create
                // cancelled between the row and its audit write is a row with no answer (McpIdempotency.RememberAsync reasons the same way).
                // A caller already gone before the create starts keeps the caller's token, so nothing is written for them.
                var abandonedBeforeStart = cancellationToken.IsCancellationRequested;
                var createToken = abandonedBeforeStart ? cancellationToken : CancellationToken.None;

                EventCapabilityResult<CreateEventResponse> result;
                try
                {
                    result = await events.CreateAsync(http, input.Request, createToken);
                }
                catch (Exception ex) when (!abandonedBeforeStart)
                {
                    // ADR-015: the exception type only (a Dataverse fault message can carry record text). The create may have landed before
                    // it threw, so the key is remembered as "outcome unknown".
                    logger.LogWarning("MCP tool {ToolName}: the capability faulted ({ExceptionType}).", ToolName, ex.GetType().Name);
                    return (TaskToolSupport.Failure(OutcomeUnknown, http), new RetryEntry(Guid.Empty, fingerprint, OutcomeUnknown: true));
                }

                if (result.Status == EventCapabilityStatus.Ok && result.Value is { } created)
                {
                    // The task exists: its entry is built first and always returned, so nothing after this line can leave a task with no
                    // stored key (a retry would then create a second one).
                    var entry = new RetryEntry(created.Id, fingerprint);
                    try
                    {
                        return (ResultOf(input.Request, created.Id, consoleBaseUrl, replayed: false), entry);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning("MCP tool {ToolName}: building the result faulted ({ExceptionType}).", ToolName, ex.GetType().Name);
                        return (TaskToolSupport.Failure(OutcomeUnknown, http), entry);
                    }
                }

                // Failed (a fault after which the row may exist) and an Ok with no value are not answers: remember the key as "outcome
                // unknown" so a retry never creates a second task. Every other status (Refused, Forbidden, Invalid, ...) wrote nothing
                // and stays unremembered, so the same key can be retried.
                if (!abandonedBeforeStart && result.Status is (EventCapabilityStatus.Failed or EventCapabilityStatus.Ok))
                {
                    logger.LogWarning("MCP tool {ToolName}: the create's outcome is unknown ({Status}).", ToolName, result.Status);
                    return (TaskToolSupport.Failure(OutcomeUnknown, http), new RetryEntry(Guid.Empty, fingerprint, OutcomeUnknown: true));
                }

                // A call the caller abandoned is not a refusal.
                cancellationToken.ThrowIfCancellationRequested();
                return (TaskToolSupport.Failure(ErrorOf(result), http), null);
            },
            logger, cancellationToken);
    }

    /// <summary>
    /// The tool's answer for a create that did not run: the REST route's <c>reasonCode</c> and status for the same outcome, with fixed text
    /// (never a record name, id or the capability's refusal detail, ADR-015). Fail closed: an outcome a create does not produce is the fault answer.
    /// </summary>
    internal static McpToolError ErrorOf(EventCapabilityResult<CreateEventResponse> result) =>
        result.Status switch
        {
            EventCapabilityStatus.UserContextRequired => TaskToolSupport.UserContextRequired,
            EventCapabilityStatus.Invalid => new McpToolError(
                TaskToolSupport.InvalidArgumentReasonCode,
                string.IsNullOrWhiteSpace(result.Message) ? "A value is not valid. Send the call again with a valid value." : result.Message,
                StatusCodes.Status400BadRequest),
            EventCapabilityStatus.Forbidden => new McpToolError(
                result.ReasonCode ?? RecordRouteAccessAuthorizationFilter.InsufficientRightsReasonCode,
                "The signed-in user may not file a task under that record: they need to be able to read it and attach to it.",
                StatusCodes.Status403Forbidden),
            EventCapabilityStatus.InsufficientPrivilege => new McpToolError(
                result.ReasonCode ?? RecordRouteAccessAuthorizationFilter.InsufficientPrivilegeReasonCode,
                "The signed-in user may not create tasks.",
                StatusCodes.Status403Forbidden),
            // A refusal REST classifies keeps its code and status: no owner, 409; a stamp that cannot be derived, 500. Any other is the
            // route's bare 500, which names nothing.
            EventCapabilityStatus.Refused when result.ReasonCode is { } code => new McpToolError(
                code,
                code == EventsCapability.RegardingStampFailedErrorCode
                    ? "The task's regarding record could not be resolved for access inheritance, so nothing was saved."
                    : "The task was not saved: no owner could be resolved for it.",
                code == EventsCapability.RegardingStampFailedErrorCode ? StatusCodes.Status500InternalServerError : StatusCodes.Status409Conflict),
            _ => Failed,
        };

    /// <summary>
    /// The success result for a task: its id, the subject and due date as sent, the status a new task has, and its open link. Pure. A
    /// replay builds the same result from the same inputs and the first task's id.
    /// </summary>
    internal static CallToolResult ResultOf(ApiCreateEventRequest request, Guid eventId, string? consoleBaseUrl, bool replayed)
    {
        var subject = WorklistItemSummary.Clip(request.Subject.Trim(), TaskToolSupport.GetSubjectBytes) ?? string.Empty;
        var dueDate = TaskToolSupport.DateText(request.DueDate);
        var status = EventStatusCode.GetDisplayName(EventStatusCode.Open);
        var body = new CreateBody(eventId.ToString("D"), subject, dueDate, status, TaskToolSupport.OpenLink(consoleBaseUrl, eventId), replayed);

        var line = TaskToolSupport.LineOf(new TaskRow(body.Id, body.Subject, dueDate, status, null, null));
        var text = (replayed
                       ? $"A task was already created with this idempotencyKey: {line}. Nothing new was created."
                       : $"Created task: {line}.")
                   + (body.Link is { } link ? $"\nOpen in Spaarke: {link}" : string.Empty);
        return DecisionToolCall.Success(text, body);
    }

    /// <summary>
    /// What the retry key keeps: the event it created and the fingerprint of the inputs (no key, no subject, no description). When
    /// <paramref name="OutcomeUnknown"/> the create faulted where the row may exist: there is no id, and a retry answers the fixed "may or
    /// may not" text instead of creating a second task.
    /// </summary>
    internal sealed record RetryEntry(Guid EventId, string Fingerprint, bool OutcomeUnknown = false) : IMcpRetryEntry;

    /// <param name="Request">The create exactly as REST binds it.</param>
    internal sealed record CreateInput(ApiCreateEventRequest Request, string IdempotencyKey);

    /// <summary>The inputs a retry must repeat, as one hash (it holds no subject or description text, ADR-015).</summary>
    internal static string FingerprintOf(CreateInput input)
    {
        var r = input.Request;
        return McpIdempotency.FingerprintOf(
            r.Subject,
            r.Description ?? string.Empty,
            TaskToolSupport.DateText(r.DueDate) ?? string.Empty,
            r.Priority?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            r.RegardingRecordType?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            r.RegardingRecordId?.ToString("D") ?? string.Empty);
    }

    /// <summary>
    /// The closed argument set: <c>subject</c> and <c>idempotencyKey</c> required text; <c>description</c> optional text; <c>dueDate</c>
    /// optional <c>yyyy-MM-dd</c>; <c>priority</c> and <c>regardingRecordType</c> optional integers; <c>regardingRecordId</c> optional GUID text.
    /// Anything else is refused with the fix, never ignored. Values the capability judges (a priority's value, a regarding type's range, a
    /// type without an id) are passed on, so REST's rules and texts stay the only ones.
    /// </summary>
    internal static bool TryReadArguments(
        IDictionary<string, JsonElement>? arguments, HttpContext? http, out CreateInput input, out CallToolResult? error)
    {
        input = null!;
        error = null;
        var fix = $"{ToolName} takes {SubjectArgument} (text) and {IdempotencyKeyArgument} (text), and optionally {DescriptionArgument} (text), "
                  + $"{DueDateArgument} (yyyy-MM-dd), {PriorityArgument} (integer), {RegardingRecordTypeArgument} (integer) and "
                  + $"{RegardingRecordIdArgument} (id). Send the call again with just those.";
        if (arguments is null || arguments.Keys.Any(k => !Arguments.Contains(k, StringComparer.Ordinal)))
        {
            error = TaskToolSupport.InvalidArgument(fix, http);
            return false;
        }

        if (!TryText(arguments, SubjectArgument, required: true, out var subject)
            || !TryText(arguments, IdempotencyKeyArgument, required: true, out var key))
        {
            error = TaskToolSupport.InvalidArgument(fix, http);
            return false;
        }

        if (subject!.Length > SubjectMaxLength)
        {
            error = TaskToolSupport.InvalidArgument($"'{SubjectArgument}' must be at most {SubjectMaxLength} characters.", http);
            return false;
        }

        if (key!.Length > IdempotencyKeyMaxLength)
        {
            error = TaskToolSupport.InvalidArgument($"'{IdempotencyKeyArgument}' must be at most {IdempotencyKeyMaxLength} characters.", http);
            return false;
        }

        if (!TryText(arguments, DescriptionArgument, required: false, out var description))
        {
            error = TaskToolSupport.InvalidArgument($"'{DescriptionArgument}' must be text. {fix}", http);
            return false;
        }

        if (description is not null && description.Length > DescriptionMaxLength)
        {
            error = TaskToolSupport.InvalidArgument($"'{DescriptionArgument}' must be at most {DescriptionMaxLength} characters.", http);
            return false;
        }

        DateOnly? dueDate = null;
        if (IsPresent(arguments, DueDateArgument, out var dueElement))
        {
            if (dueElement.ValueKind != JsonValueKind.String
                || !DateOnly.TryParseExact(dueElement.GetString()?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                error = TaskToolSupport.InvalidArgument($"'{DueDateArgument}' must be a day as yyyy-MM-dd.", http);
                return false;
            }

            dueDate = parsed;
        }

        if (!TryInt(arguments, PriorityArgument, out var priority) || !TryInt(arguments, RegardingRecordTypeArgument, out var regardingType))
        {
            error = TaskToolSupport.InvalidArgument($"'{PriorityArgument}' and '{RegardingRecordTypeArgument}' must be whole numbers. {fix}", http);
            return false;
        }

        Guid? regardingId = null;
        if (IsPresent(arguments, RegardingRecordIdArgument, out var idElement))
        {
            if (idElement.ValueKind != JsonValueKind.String || !Guid.TryParse(idElement.GetString(), out var parsedId))
            {
                error = TaskToolSupport.Failure(
                    new McpToolError(
                        DocumentToolSupport.InvalidIdReasonCode,
                        $"'{RegardingRecordIdArgument}' must be the record's id (a GUID).",
                        StatusCodes.Status400BadRequest),
                    http);
                return false;
            }

            regardingId = parsedId;
        }

        input = new CreateInput(
            new ApiCreateEventRequest(
                subject,
                description,
                RegardingRecordId: regardingId,
                RegardingRecordType: regardingType,
                DueDate: dueDate,
                Priority: priority),
            key);
        return true;
    }

    private static bool IsPresent(IDictionary<string, JsonElement> arguments, string name, out JsonElement element) =>
        arguments.TryGetValue(name, out element) && element.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    /// <summary>Optional or required text; a present value of another kind, and a blank required value, fail.</summary>
    private static bool TryText(IDictionary<string, JsonElement> arguments, string name, bool required, out string? value)
    {
        value = null;
        if (!IsPresent(arguments, name, out var element))
        {
            return !required;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = element.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return !required;
        }

        // The subject and the key are used as sent apart from the edges; a description keeps its own line breaks.
        value = name == DescriptionArgument ? text : text.Trim();
        return true;
    }

    private static bool TryInt(IDictionary<string, JsonElement> arguments, string name, out int? value)
    {
        value = null;
        if (!IsPresent(arguments, name, out var element))
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var number))
        {
            return false;
        }

        value = number;
        return true;
    }

    /// <summary><c>structuredContent</c> of <c>spaarke_create_task</c>: the new task and its link.</summary>
    internal sealed record CreateBody(string Id, string Subject, string? DueDate, string Status, string? Link, bool Replayed);
}
