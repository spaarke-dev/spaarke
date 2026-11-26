using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Documents;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Playbooks;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 030 (spec FR-05, FR-14, NFR-05, NFR-06; ADR-013, ADR-015, ADR-028; owner decision D-030-run-model).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md; spec "Placement & New Components"). A thin MCP
// adapter inside the BFF on the /mcp/legal-operations host over IPlaybookRunAi (Services/Ai/PublicContracts): no package, no DI
// registration of its own (found by [McpArea]), no background work here (the run is the helper's, ADR-052 amendment 2026-11-26).
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: POST /api/ai/playbooks/{id}/execute (PlaybookRunEndpoints), task 007's PlaybookAuthorizationFilter
//     .AuthorizePlaybookRunAsync, McpIdempotency (task 031's retry key), McpLongRunning (task 020's start/status shapes).
//   Extension: all reused; the tool adds the argument check, the input caps 007's verifier required, the retry entry and the result.
//   Cost of doing nothing: Copilot users cannot run a JPS playbook (FR-05 fails) and FR-14's start/status pattern has no first user.
// =====================================================================================================================

/// <summary>
/// <c>spaarke_run_playbook</c> (FR-05, FR-14): starts a JPS playbook run on documents for the signed-in caller and answers within seconds
/// with <c>{ runId, status, consoleLink }</c>. The run continues in Spaarke after the reply, acting as the caller; the model reads it with
/// <c>spaarke_get_playbook_run</c>. Model-callable reversible write (NFR-06): not destructive, idempotent with a required
/// <c>idempotencyKey</c>.
/// </summary>
/// <remarks>
/// <para><b>One authorization.</b> <see cref="IPlaybookRunAi.StartAsCallerAsync"/> decides with task 007's shared method (the REST execute
/// route's own rule) before anything runs; a refusal is an <c>isError</c> result with the route's <c>reasonCode</c> and starts nothing. The
/// tool adds no check of its own: its caps bound the input before that decision, so a call cannot make the decision's sequential
/// caller-scoped Dataverse checks outrun the answer budget (007 verifier pass 2, constraint 3).</para>
/// <para><b>Retry key.</b> <see cref="McpIdempotency"/>: per caller and key, a retry with the same inputs answers the original run id and
/// starts nothing; different inputs under the same key are refused.</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class RunPlaybookTool : McpToolDefinition
{
    internal const string ToolName = "spaarke_run_playbook";

    internal const string PlaybookIdArgument = "playbookId";

    internal const string DocumentIdsArgument = "documentIds";

    internal const string ParametersArgument = "parameters";

    internal const string IdempotencyKeyArgument = McpToolAuthorization.IdempotencyKeyArgument;

    /// <summary>The longest retry key accepted, in characters (task 031's bound).</summary>
    internal const int IdempotencyKeyMaxLength = 200;

    /// <summary>The most documents one run takes (constraint 3: bounds 007's sequential per-document checks).</summary>
    internal const int MaxDocuments = 20;

    /// <summary>The most parameters one run takes (constraint 3).</summary>
    internal const int MaxParameters = 20;

    /// <summary>The longest parameter name, in characters; also the length a refused name is cut to in the result (constraint 4).</summary>
    internal const int MaxParameterKeyLength = 64;

    /// <summary>The longest parameter value, in characters (constraint 3).</summary>
    internal const int MaxParameterValueLength = 2_000;

    internal const string RetryResource = "mcp-run-playbook";

    internal const string LockResource = "mcp-run-playbook-lock";

    private static readonly McpIdempotencyScope Scope = new(ToolName, RetryResource, LockResource);

    private static readonly string[] Arguments = [PlaybookIdArgument, DocumentIdsArgument, ParametersArgument, IdempotencyKeyArgument];

    /// <summary>A start that faulted before the run was accepted: this host's own code (the REST route answers a bare 500).</summary>
    internal const string StartFailedReasonCode = "playbook.run-start-failed";

    internal static McpToolError StartFailed { get; } =
        new(StartFailedReasonCode, "The JPS playbook run could not be started. Try again shortly.", StatusCodes.Status500InternalServerError);

    /// <summary>
    /// The description the model reads (at most 1,024 characters, "use for / not for"; D-209 qualifies "playbook": a JPS playbook is an AI
    /// analysis recipe, not a legal or contract-review playbook).
    /// </summary>
    internal const string Description =
        "Use for: running a Spaarke JPS playbook (AI analysis recipe) on documents the signed-in user may read. It starts the run and "
        + "answers at once with a runId; the run continues in Spaarke for up to 50 minutes, acting as the user. Then call "
        + "spaarke_get_playbook_run with the runId to see progress and the result summary. Needs the JPS playbook's id (from "
        + "spaarke_list_playbooks), 1 to 20 document ids (from spaarke_list_documents or spaarke_search_documents), optional text "
        + "parameters the playbook takes, and an idempotencyKey you make up once per run and send again unchanged on a retry, so a retry "
        + "never starts a second run. consoleLink only opens the documents in Spaarke; the user does not need it. Not for: reading a "
        + "run (use spaarke_get_playbook_run); creating an analysis record (use spaarke_start_analysis); legal or contract-review "
        + "playbooks (Markdown or Microsoft contract-review playbooks).";

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [PlaybookIdArgument] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The id (GUID) of the JPS playbook (AI analysis recipe) to run, from spaarke_list_playbooks.",
            },
            [DocumentIdsArgument] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["minItems"] = 1,
                ["maxItems"] = MaxDocuments,
                ["description"] = "The ids (GUIDs) of the documents to run it on, from spaarke_list_documents or spaarke_search_documents.",
            },
            [ParametersArgument] = new JsonObject
            {
                ["type"] = "object",
                ["maxProperties"] = MaxParameters,
                ["additionalProperties"] = new JsonObject { ["type"] = "string", ["maxLength"] = MaxParameterValueLength },
                ["description"] = "Optional: text parameters the JPS playbook takes, as name and value pairs.",
            },
            [IdempotencyKeyArgument] = new JsonObject
            {
                ["type"] = "string",
                ["maxLength"] = IdempotencyKeyMaxLength,
                ["description"] = "A value you make up once for this run (for example a GUID) and send again unchanged on a retry.",
            },
        },
        ["required"] = new JsonArray(PlaybookIdArgument, DocumentIdsArgument, IdempotencyKeyArgument),
        ["additionalProperties"] = false,
    });

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            StartAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = ToolName,
                Title = "Run a JPS playbook",
                Description = Description,
                ReadOnly = false,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
            });

        // The input schema is stated, not inferred; the handler reads the raw arguments against the same closed set.
        tool.ProtocolTool.InputSchema = InputSchema;
        return tool;
    }

    /// <summary>The handler the SDK binds: <see cref="RunAsync"/> inside the one per-call telemetry line (NFR-05; rows = 1 when a run was returned).</summary>
    internal static Task<CallToolResult> StartAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        ITenantCache cache,
        IPlaybookRunAi runs,
        TimeProvider time,
        ILogger<RunPlaybookTool> logger,
        CancellationToken cancellationToken) =>
        DecisionToolCall.LogAroundAsync(
            logger, time, ToolName, accessor.HttpContext,
            () => RunAsync(request.Params?.Arguments, accessor.HttpContext, cache, runs, time, logger, cancellationToken),
            result => result.IsError == true ? 0 : 1);

    internal static async Task<CallToolResult> RunAsync(
        IDictionary<string, JsonElement>? arguments,
        HttpContext? http,
        ITenantCache cache,
        IPlaybookRunAi runs,
        TimeProvider time,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // ADR-028 A10: no caller, no run. Checked before anything else; 007's method checks it again.
        if (!PlanAndLookupToolSupport.HasCaller(http) || string.IsNullOrEmpty(CallerResolution.ResolveObjectId(http!.User)))
        {
            return DocumentToolSupport.Failure(DocumentToolSupport.UserContextRequired, http);
        }

        if (!TryReadArguments(arguments, http, out var input, out var argumentError))
        {
            return argumentError!;
        }

        RetryEntry? replayed = null;
        var result = await McpIdempotency.RunOnceAsync<RetryEntry>(
            Scope, cache, http!, input.IdempotencyKey, FingerprintOf(input),
            replay: earlier =>
            {
                replayed = earlier;
                return ReplayMarker;
            },
            run: async () =>
            {
                PlaybookRunStartResult started;
                try
                {
                    // The exact documentIds and parameters instances below are what 007's method decides on and what the run gets.
                    started = await runs.StartAsCallerAsync(http!, input.PlaybookId, input.DocumentIds, input.Parameters, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    // ADR-015: the exception type only.
                    logger.LogWarning("MCP tool {ToolName}: the start faulted ({ExceptionType}).", ToolName, ex.GetType().Name);
                    return (DocumentToolSupport.Failure(StartFailed, http), null);
                }

                if (started is not { Outcome: PlaybookRunStartOutcome.Started, Run: { } run })
                {
                    return (DocumentToolSupport.Failure(ErrorOf(started), http), null);
                }

                // The run is live: its retry entry is built first and always returned, so nothing after this line can leave a run with
                // no stored key (a retry would then start a second run).
                var firstDocument = input.DocumentIds[0];
                var entry = new RetryEntry(run.RunId, firstDocument, FingerprintOf(input));
                try
                {
                    return (StartedResult(run, runs.RunConsoleLink(http!, firstDocument), replayed: false, time.GetUtcNow()), entry);
                }
                catch (Exception ex)
                {
                    // ADR-015: the exception type only. The run is answered by its status, without the link (no filter: whatever the
                    // fault, the entry below must reach the store).
                    logger.LogWarning("MCP tool {ToolName}: building the start result faulted ({ExceptionType}).", ToolName, ex.GetType().Name);
                    return (GetPlaybookRunTool.StatusResult(run, time.GetUtcNow()), entry);
                }
            },
            logger, cancellationToken);

        if (!ReferenceEquals(result, ReplayMarker) || replayed is null)
        {
            return result;
        }

        // The same key and inputs again: the original run, as it is now; nothing new is started.
        PlaybookRunView? now = null;
        try
        {
            now = await runs.ReadAsCallerAsync(http!, replayed.RunId, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("MCP tool {ToolName}: the replay read faulted ({ExceptionType}).", ToolName, ex.GetType().Name);
        }

        var link = runs.RunConsoleLink(http!, replayed.FirstDocumentId);
        if (now is not null)
        {
            return StartedResult(now, link, replayed: true, time.GetUtcNow());
        }

        // The run cannot be read right now (another instance, the record not yet written): answer the id, as running.
        return link is null
            ? McpLongRunning.Status(replayed.RunId.ToString("D"), McpRunState.Running, summary: ReplayLine(replayed.RunId))
            : McpLongRunning.Started(replayed.RunId.ToString("D"), McpRunState.Running, link, ReplayLine(replayed.RunId));
    }

    /// <summary>Stands for "replay this entry" inside <see cref="McpIdempotency.RunOnceAsync{TEntry}"/>, whose replay is synchronous.</summary>
    private static readonly CallToolResult ReplayMarker = new();

    private static string ReplayLine(Guid runId) =>
        $"A JPS playbook run was already started with this idempotencyKey: {runId:D}. Nothing new was started. "
        + $"Call {GetPlaybookRunTool.ToolName} with this runId for its status.";

    /// <summary>
    /// The answer for an accepted run: <see cref="McpLongRunning.Started"/> while it runs (with the console link), or the status result when
    /// it has already finished (a start result is never a finished state).
    /// </summary>
    internal static CallToolResult StartedResult(PlaybookRunView run, string? consoleLink, bool replayed, DateTimeOffset now)
    {
        if (run.Phase != PlaybookRunPhase.Running || consoleLink is null)
        {
            return GetPlaybookRunTool.StatusResult(run, now);
        }

        var line = replayed
            ? ReplayLine(run.RunId)
            : $"Started JPS playbook run {run.RunId:D}. It continues in Spaarke after this reply; call {GetPlaybookRunTool.ToolName} "
              + "with this runId to see its progress and result.";
        return McpLongRunning.Started(run.RunId.ToString("D"), McpRunState.Running, consoleLink, line);
    }

    /// <summary>The tool's answer for a start that did not run: 007's REST code and status for a refusal, this host's codes otherwise.</summary>
    internal static McpToolError ErrorOf(PlaybookRunStartResult started) =>
        started.Outcome switch
        {
            PlaybookRunStartOutcome.Refused => ErrorOf(started.Authorization ?? PlaybookRunAuthorization.UserContextRequired),
            PlaybookRunStartOutcome.AuthUnavailable => new McpToolError(
                PlaybookRunReasonCodes.AuthUnavailable,
                "Spaarke could not get access on the signed-in user's behalf to run this JPS playbook. Try again shortly; if it keeps failing, sign in again.",
                StatusCodes.Status503ServiceUnavailable),
            PlaybookRunStartOutcome.NotAccepted => new McpToolError(
                PlaybookRunReasonCodes.NotAccepted,
                "The JPS playbook run did not start in time and was stopped. Nothing is running; try again with a new idempotencyKey.",
                StatusCodes.Status503ServiceUnavailable),
            PlaybookRunStartOutcome.Unavailable => new McpToolError(
                PlaybookRunReasonCodes.Unavailable,
                "JPS playbook runs are not available right now. Try again later.",
                StatusCodes.Status503ServiceUnavailable),
            PlaybookRunStartOutcome.RunLimit => new McpToolError(
                PlaybookRunReasonCodes.RunLimit,
                "The signed-in user already has the most JPS playbook runs running at once. Nothing new was started. Wait for a run to "
                + $"finish (check it with {GetPlaybookRunTool.ToolName}), then try again.",
                StatusCodes.Status429TooManyRequests),
            _ => StartFailed,
        };

    /// <summary>A refusal of task 007's method as a tool error: the REST reasonCode and status, fixed text (ADR-015).</summary>
    internal static McpToolError ErrorOf(PlaybookRunAuthorization refusal) =>
        refusal.Outcome switch
        {
            PlaybookRunAuthorizationOutcome.UserContextRequired => DocumentToolSupport.UserContextRequired,
            PlaybookRunAuthorizationOutcome.DocumentIdsRequired => new McpToolError(
                refusal.ReasonCode!, $"'{DocumentIdsArgument}' must list at least one document id.", StatusCodes.Status400BadRequest),
            PlaybookRunAuthorizationOutcome.ParameterRejected => new McpToolError(
                refusal.ReasonCode!,
                $"The parameter '{Cut(refusal.RejectedParameterKey)}' was refused: {Cut(refusal.RejectedParameterReason, 200)}. "
                + "Send the call again without it, or with a value the JPS playbook accepts.",
                StatusCodes.Status400BadRequest),
            PlaybookRunAuthorizationOutcome.PlaybookUnavailable => new McpToolError(
                refusal.ReasonCode!, "The JPS playbook was not found, or the signed-in user may not use it.", StatusCodes.Status404NotFound),
            _ => new McpToolError(
                refusal.ReasonCode ?? FinanceAuthorizationFilter.InsufficientRightsReasonCode,
                "The signed-in user may not run this JPS playbook on these documents or records.",
                StatusCodes.Status403Forbidden),
        };

    /// <summary>One line of at most <paramref name="max"/> characters (constraint 4: a refused key is cut to 64).</summary>
    private static string Cut(string? value, int max = MaxParameterKeyLength)
    {
        var line = McpText.SingleLine(value).Trim();
        return line.Length <= max ? line : line[..max];
    }

    /// <summary>What the retry key keeps: the run it started and the fingerprint of the inputs (no key, no parameter values).</summary>
    internal sealed record RetryEntry(Guid RunId, Guid FirstDocumentId, string Fingerprint) : IMcpRetryEntry;

    internal sealed record RunInput(Guid PlaybookId, Guid[] DocumentIds, IReadOnlyDictionary<string, string>? Parameters, string IdempotencyKey);

    /// <summary>The inputs a retry must repeat: the playbook, the documents in order, and the parameters by name.</summary>
    internal static string FingerprintOf(RunInput input) =>
        McpIdempotency.FingerprintOf(
            input.PlaybookId.ToString("D"),
            string.Join(',', input.DocumentIds.Select(d => d.ToString("D"))),
            input.Parameters is null
                ? string.Empty
                : string.Join('\n', input.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}")));

    /// <summary>
    /// The closed argument set and constraint 3's caps: <c>playbookId</c> a GUID; <c>documentIds</c> 1 to <see cref="MaxDocuments"/> GUIDs;
    /// <c>parameters</c> optional, at most <see cref="MaxParameters"/> text values of at most <see cref="MaxParameterValueLength"/>
    /// characters under names of at most <see cref="MaxParameterKeyLength"/>, no two names differing only in letter case;
    /// <c>idempotencyKey</c> required. Anything else is refused with the fix, never ignored.
    /// </summary>
    internal static bool TryReadArguments(
        IDictionary<string, JsonElement>? arguments, HttpContext? http, out RunInput input, out CallToolResult? error)
    {
        input = null!;
        error = null;
        var fix = $"{ToolName} takes {PlaybookIdArgument} (text), {DocumentIdsArgument} (1 to {MaxDocuments} ids), {IdempotencyKeyArgument} "
                  + $"(text) and optional {ParametersArgument} (name and text value pairs). Send the call again with just those.";
        if (arguments is null || arguments.Keys.Any(k => !Arguments.Contains(k, StringComparer.Ordinal)))
        {
            error = DocumentToolSupport.InvalidArgument(fix, http);
            return false;
        }

        if (!TryReadText(arguments, PlaybookIdArgument, out var playbookText) || !TryReadText(arguments, IdempotencyKeyArgument, out var key))
        {
            error = DocumentToolSupport.InvalidArgument(fix, http);
            return false;
        }

        if (!Guid.TryParse(playbookText, out var playbookId) || playbookId == Guid.Empty)
        {
            error = InvalidId($"'{PlaybookIdArgument}' must be the JPS playbook's id (a GUID).", http);
            return false;
        }

        if (key.Length > IdempotencyKeyMaxLength)
        {
            error = DocumentToolSupport.InvalidArgument($"'{IdempotencyKeyArgument}' must be at most {IdempotencyKeyMaxLength} characters.", http);
            return false;
        }

        if (!arguments.TryGetValue(DocumentIdsArgument, out var documents) || documents.ValueKind != JsonValueKind.Array
            || documents.GetArrayLength() is 0 or > MaxDocuments)
        {
            error = DocumentToolSupport.InvalidArgument($"'{DocumentIdsArgument}' must list 1 to {MaxDocuments} document ids.", http);
            return false;
        }

        var documentIds = new Guid[documents.GetArrayLength()];
        var i = 0;
        foreach (var document in documents.EnumerateArray())
        {
            if (document.ValueKind != JsonValueKind.String || !Guid.TryParse(document.GetString(), out var documentId) || documentId == Guid.Empty)
            {
                error = InvalidId($"Each entry of '{DocumentIdsArgument}' must be a document's id (a GUID).", http);
                return false;
            }

            documentIds[i++] = documentId;
        }

        Dictionary<string, string>? parameters = null;
        if (arguments.TryGetValue(ParametersArgument, out var parameterObject) && parameterObject.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (parameterObject.ValueKind != JsonValueKind.Object)
            {
                error = DocumentToolSupport.InvalidArgument($"'{ParametersArgument}' must be an object of name and text value pairs.", http);
                return false;
            }

            parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var parameter in parameterObject.EnumerateObject())
            {
                if (parameters.Count == MaxParameters)
                {
                    error = DocumentToolSupport.InvalidArgument($"'{ParametersArgument}' takes at most {MaxParameters} parameters.", http);
                    return false;
                }

                if (parameter.Name.Length is 0 or > MaxParameterKeyLength || !seen.Add(parameter.Name))
                {
                    error = DocumentToolSupport.InvalidArgument(
                        $"Each parameter name in '{ParametersArgument}' must be 1 to {MaxParameterKeyLength} characters, and no two names may differ only in letter case.", http);
                    return false;
                }

                if (parameter.Value.ValueKind != JsonValueKind.String || parameter.Value.GetString()!.Length > MaxParameterValueLength)
                {
                    error = DocumentToolSupport.InvalidArgument(
                        $"Each parameter value in '{ParametersArgument}' must be text of at most {MaxParameterValueLength} characters.", http);
                    return false;
                }

                parameters[parameter.Name] = parameter.Value.GetString()!;
            }
        }

        input = new RunInput(playbookId, documentIds, parameters, key);
        return true;
    }

    private static CallToolResult InvalidId(string message, HttpContext? http) =>
        DocumentToolSupport.Failure(new McpToolError(DocumentToolSupport.InvalidIdReasonCode, message, StatusCodes.Status400BadRequest), http);

    private static bool TryReadText(IDictionary<string, JsonElement> arguments, string name, out string value)
    {
        value = string.Empty;
        if (!arguments.TryGetValue(name, out var element) || element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
        {
            return false;
        }

        value = element.GetString()!.Trim();
        return true;
    }
}
