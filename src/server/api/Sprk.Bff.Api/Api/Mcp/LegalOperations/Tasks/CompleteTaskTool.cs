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

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Tasks;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 036 (spec FR-07 write half, FR-15, NFR-05, NFR-06, NFR-07; ADR-002, ADR-015, ADR-028).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md; spec "Placement & New Components"). A thin MCP
// adapter inside the BFF over IEventsCapability (task 006), the one owner of the event complete rules, on the /mcp/legal-operations host:
// no package, no DI registration (found by [McpArea]), no background work, no AI-internal type.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: POST /api/v1/events/{id}/complete (the record-route filter's Write decision on sprk_events({id}), then the one
//     status change) and the capability's instance CompleteAsync, which makes the same decision; the capability's GetAsync;
//     McpIdempotency (task 031's retry key), TaskToolSupport (task 025).
//   Extension: this IS the extension: the capability and the retry helper are reused unchanged; the new logic is the mapping of
//     "already completed" to success (REST answers a 400 for it) and the capability-outcome to tool-error mapping for a complete.
//   Cost of doing nothing: Copilot users cannot complete a Spaarke to-do (FR-07 write half fails), and a model that retries a complete
//     is told it failed.
// =====================================================================================================================

/// <summary>
/// <c>spaarke_complete_task</c> (FR-07): marks one Spaarke to-do or task (an event) completed, for a caller who may write it. A
/// model-callable reversible write (NFR-06): not destructive, idempotent, with a required <c>idempotencyKey</c>.
/// </summary>
/// <remarks>
/// <para><b>One decision, one answer.</b> The tool calls <see cref="IEventsCapability.CompleteAsync"/> with exactly
/// <c>IHttpContextAccessor.HttpContext</c> and copies no check. The capability asks the caller's Write on the event (the decision the REST
/// route's record filter asks): an event the caller may not read and one that does not exist give the same not-found
/// (<see cref="TaskToolSupport.NotFound"/>), so another user's private task is never confirmed (FR-15).</para>
/// <para><b>Idempotent twice over.</b> A completed task is success without a second write: the capability refuses a complete of a task that
/// is not open work (REST: a 400), and only then does the tool read the task back through <see cref="IEventsCapability.GetAsync"/> (the
/// caller's own Read) and answer success when its status is Completed; a cancelled or otherwise closed task keeps the refusal. And
/// <see cref="McpIdempotency"/> remembers the key per caller: the same key and task again replays the first answer without calling the
/// capability.</para>
/// <para><b>Result.</b> <c>structuredContent</c> is <c>{ id, status, link, alreadyCompleted, replayed }</c> (ADR-015: no subject or
/// description). Logs carry the tool name, latency, outcome or reason code and correlation id only.</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class CompleteTaskTool : McpToolDefinition
{
    internal const string ToolName = "spaarke_complete_task";

    internal const string TaskIdArgument = "taskId";
    internal const string IdempotencyKeyArgument = McpToolAuthorization.IdempotencyKeyArgument;

    /// <summary>The longest retry key accepted, in characters (task 031's bound).</summary>
    internal const int IdempotencyKeyMaxLength = 200;

    internal const string RetryResource = "mcp-complete-task";

    internal const string LockResource = "mcp-complete-task-lock";

    /// <summary>The <c>reasonCode</c> of a complete that REST refuses with a 400 (the task is not open work): this host's own, REST carries none.</summary>
    internal const string InvalidTransitionReasonCode = "events.invalid_transition";

    /// <summary>The <c>reasonCode</c> of a complete that faulted (REST: a bare 500).</summary>
    internal const string CompleteFailedReasonCode = "events.complete_failed";

    private const string FailedMessage = "The task could not be completed. Try again shortly.";

    private static readonly McpIdempotencyScope Scope = new(ToolName, RetryResource, LockResource);

    private static readonly string[] Arguments = [TaskIdArgument, IdempotencyKeyArgument];

    internal static McpToolError Failed { get; } =
        new(CompleteFailedReasonCode, FailedMessage, StatusCodes.Status500InternalServerError);

    /// <summary>The description the model reads (at most 1,024 characters, "use for / not for").</summary>
    internal const string Description =
        "Use for: marking one Spaarke to-do or task (a Spaarke event) the signed-in user may change as completed. Needs the task's id, "
        + "from spaarke_list_tasks or spaarke_create_task, and an idempotencyKey you make up once per call and send again unchanged on a "
        + "retry. Completing a task that is already completed succeeds and changes nothing. A task that does not exist and one the user "
        + "may not see return the same not-found answer. Not for: creating a task (use spaarke_create_task); reopening, cancelling or "
        + "deleting a task; listing or opening tasks (use spaarke_list_tasks, spaarke_get_task); calendar meetings or Outlook events.";

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [TaskIdArgument] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The id (GUID) of the task, from spaarke_list_tasks or spaarke_create_task.",
            },
            [IdempotencyKeyArgument] = new JsonObject
            {
                ["type"] = "string",
                ["maxLength"] = IdempotencyKeyMaxLength,
                ["description"] = "A value you make up once for this call (for example a GUID) and send again unchanged on a retry.",
            },
        },
        ["required"] = new JsonArray(TaskIdArgument, IdempotencyKeyArgument),
        ["additionalProperties"] = false,
    });

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            CompleteAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = ToolName,
                Title = "Complete a task",
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

    /// <summary>The handler the SDK binds: <see cref="RunAsync"/> inside the one per-call telemetry line (NFR-05; rows = 1 when a task was answered).</summary>
    internal static Task<CallToolResult> CompleteAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        ITenantCache cache,
        IEventsCapability events,
        IOptions<DataverseOptions> dataverse,
        TimeProvider time,
        ILogger<CompleteTaskTool> logger,
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

        if (!TryReadArguments(arguments, http, out var taskId, out var key, out var argumentError))
        {
            return argumentError!;
        }

        var consoleBaseUrl = DecisionsTextSummary.ConsoleBaseUrl(environmentUrl);
        var fingerprint = McpIdempotency.FingerprintOf(taskId.ToString("D"));

        return await McpIdempotency.RunOnceAsync<RetryEntry>(
            Scope, cache, http, key, fingerprint,
            replay: _ => ResultOf(taskId, consoleBaseUrl, alreadyCompleted: false, replayed: true),
            run: async () =>
            {
                CompleteOutcome outcome;
                try
                {
                    outcome = await CompleteOnceAsync(events, http, taskId, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    // ADR-015: the exception type only. A caller who has gone away rethrows.
                    logger.LogWarning("MCP tool {ToolName}: the capability faulted ({ExceptionType}).", ToolName, ex.GetType().Name);
                    return (TaskToolSupport.Failure(Failed, http), null);
                }

                if (outcome.Error is null)
                {
                    // The task is completed: its entry is built first and always returned.
                    var entry = new RetryEntry(fingerprint);
                    try
                    {
                        return (ResultOf(taskId, consoleBaseUrl, outcome.AlreadyCompleted, replayed: false), entry);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning("MCP tool {ToolName}: building the result faulted ({ExceptionType}).", ToolName, ex.GetType().Name);
                        return (TaskToolSupport.Failure(Failed, http), entry);
                    }
                }

                // A call the caller abandoned is not a fault.
                cancellationToken.ThrowIfCancellationRequested();
                return (TaskToolSupport.Failure(outcome.Error, http), null);
            },
            logger, cancellationToken);
    }

    /// <summary>
    /// The capability's complete, with a task that is already completed answered as success (the capability and REST refuse it as an invalid
    /// transition): only that one refusal is looked into, by reading the task back through the capability as the caller.
    /// </summary>
    internal static async Task<CompleteOutcome> CompleteOnceAsync(
        IEventsCapability events, HttpContext http, Guid taskId, CancellationToken cancellationToken)
    {
        var result = await events.CompleteAsync(http, taskId, cancellationToken);
        if (result.Status == EventCapabilityStatus.Ok)
        {
            return new CompleteOutcome(null, AlreadyCompleted: false);
        }

        if (result.Status == EventCapabilityStatus.InvalidTransition)
        {
            var current = await events.GetAsync(http, taskId, cancellationToken);
            if (current is { Status: EventCapabilityStatus.Ok, Value.StatusCode: EventStatusCode.Completed })
            {
                return new CompleteOutcome(null, AlreadyCompleted: true);
            }
        }

        return new CompleteOutcome(ErrorOf(result), AlreadyCompleted: false);
    }

    /// <summary>
    /// The tool's answer for a complete that did not run: the REST route's <c>reasonCode</c> and status for the same outcome, with fixed text
    /// (never a record name or id, ADR-015). Fail closed: an outcome a complete does not produce is the fault answer.
    /// </summary>
    internal static McpToolError ErrorOf(EventCapabilityResult<EventActionResponse> result) =>
        result.Status switch
        {
            EventCapabilityStatus.UserContextRequired => TaskToolSupport.UserContextRequired,
            EventCapabilityStatus.NotFound => TaskToolSupport.NotFound,
            EventCapabilityStatus.Forbidden => new McpToolError(
                result.ReasonCode ?? RecordRouteAccessAuthorizationFilter.InsufficientRightsReasonCode,
                "The signed-in user may not complete this task.",
                StatusCodes.Status403Forbidden),
            // REST's 400: the capability's text names the task's status and the statuses a task can be completed from, never a record.
            EventCapabilityStatus.InvalidTransition => new McpToolError(
                InvalidTransitionReasonCode,
                string.IsNullOrWhiteSpace(result.Message) ? "This task cannot be completed from its current status." : result.Message,
                StatusCodes.Status400BadRequest),
            _ => Failed,
        };

    /// <summary>The success result: the task's id, its status and its open link. Pure.</summary>
    internal static CallToolResult ResultOf(Guid taskId, string? consoleBaseUrl, bool alreadyCompleted, bool replayed)
    {
        var status = EventStatusCode.GetDisplayName(EventStatusCode.Completed);
        var body = new CompleteBody(taskId.ToString("D"), status, TaskToolSupport.OpenLink(consoleBaseUrl, taskId), alreadyCompleted, replayed);
        var text = (alreadyCompleted
                       ? $"The task was already completed (id {body.Id}). Nothing changed."
                       : replayed
                           ? $"The task was already completed with this idempotencyKey (id {body.Id}). Nothing changed."
                           : $"Completed the task (id {body.Id}).")
                   + (body.Link is { } link ? $"\nOpen in Spaarke: {link}" : string.Empty);
        return DecisionToolCall.Success(text, body);
    }

    /// <summary>What the retry key keeps: the fingerprint of the task it completed (the task id is the fingerprint's input; no key is kept).</summary>
    internal sealed record RetryEntry(string Fingerprint) : IMcpRetryEntry;

    /// <summary><paramref name="Error"/> is null when the task is completed (now, or already).</summary>
    internal sealed record CompleteOutcome(McpToolError? Error, bool AlreadyCompleted);

    /// <summary>The closed argument set: <c>taskId</c> (a GUID) and <c>idempotencyKey</c>, both required text. Anything else is refused with the fix.</summary>
    internal static bool TryReadArguments(
        IDictionary<string, JsonElement>? arguments, HttpContext? http, out Guid taskId, out string key, out CallToolResult? error)
    {
        taskId = Guid.Empty;
        key = string.Empty;
        error = null;
        var fix = $"{ToolName} takes exactly these arguments: {TaskIdArgument} (the task's id) and {IdempotencyKeyArgument} (text). "
                  + "Send the call again with just those.";
        if (arguments is null || arguments.Keys.Any(k => !Arguments.Contains(k, StringComparer.Ordinal)))
        {
            error = TaskToolSupport.InvalidArgument(fix, http);
            return false;
        }

        if (!TryText(arguments, TaskIdArgument, out var idText) || !TryText(arguments, IdempotencyKeyArgument, out var keyText))
        {
            error = TaskToolSupport.InvalidArgument(fix, http);
            return false;
        }

        if (!Guid.TryParse(idText, out taskId) || taskId == Guid.Empty)
        {
            error = TaskToolSupport.Failure(
                new McpToolError(DocumentToolSupport.InvalidIdReasonCode, $"'{TaskIdArgument}' must be the task's id (a GUID).", StatusCodes.Status400BadRequest),
                http);
            return false;
        }

        if (keyText.Length > IdempotencyKeyMaxLength)
        {
            error = TaskToolSupport.InvalidArgument($"'{IdempotencyKeyArgument}' must be at most {IdempotencyKeyMaxLength} characters.", http);
            return false;
        }

        key = keyText;
        return true;
    }

    private static bool TryText(IDictionary<string, JsonElement> arguments, string name, out string value)
    {
        value = string.Empty;
        if (!arguments.TryGetValue(name, out var element) || element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
        {
            return false;
        }

        value = element.GetString()!.Trim();
        return true;
    }

    /// <summary><c>structuredContent</c> of <c>spaarke_complete_task</c>: the task's id, status and link.</summary>
    internal sealed record CompleteBody(string Id, string Status, string? Link, bool AlreadyCompleted, bool Replayed);
}
