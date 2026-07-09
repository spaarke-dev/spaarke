using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat-side typed handler for the <c>dataverse.delete_record</c> tool — deletes one row from
/// a Dataverse table over the user-OBO Web API
/// (spaarke-ai-architecture-redesign-r1 task 009, FR-P0-07 write half).
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-039 contract freeze</b>: name + argument shape mirror the GA Dataverse MCP
/// <c>delete_record</c> tool — <c>delete_record(tablename, hasUserApproved, recordId)</c>.
/// <c>hasUserApproved</c> is REQUIRED-AND-TRUE by the GA contract itself ("Proceed solely on
/// explicit user consent") and is enforced here as frozen argument semantics so a future
/// transport swap behaves identically. This argument check is NOT the platform confirmation
/// gate: the ONE gate (FR-P2-02, task 031) suspends/resumes this tool by its declared
/// <c>sprk_sideeffectclass</c>, entirely outside this handler.
/// </para>
/// <para>
/// <b>SIDE-EFFECT tool</b>: the <c>sprk_analysistool</c> row declares
/// <c>sprk_sideeffectclass = Write (100000001)</c>. NO gating/suspension logic lives here —
/// when invoked (post-gate at P2), it executes.
/// </para>
/// <para>
/// <b>User-OBO ONLY (spec MUST rule)</b>: executes through <see cref="IDataverseUserClient"/>
/// under the calling user's exchanged token. A delete the user lacks privileges for fails with
/// the user's own access error; a table/record invisible to the user 404s. No app-only client
/// is reachable from this class (task-012 audit).
/// </para>
/// <para>
/// <b>ADR-015 / NFR-07</b>: telemetry carries table logical name, record id, outcome,
/// duration — never record content (the handler never reads the row).
/// </para>
/// </remarks>
public sealed partial class DataverseDeleteRecordHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(DataverseDeleteRecordHandler);

    [GeneratedRegex(@"^[a-z][a-z0-9_]*$")]
    private static partial Regex LogicalNameRegex();

    private readonly IDataverseUserClient _dataverse;
    private readonly ILogger<DataverseDeleteRecordHandler> _logger;

    public DataverseDeleteRecordHandler(
        IDataverseUserClient dataverse,
        ILogger<DataverseDeleteRecordHandler> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Dataverse Delete Record",
        // FR-A-01 (AIR2-020): mirror of the authored sprk_description in infra/dataverse/sprk_analysistool-dataverse-delete-record-row.json — keep byte-equal; edit the JSON, not this literal.
        Description: @"Deletes one row from a Dataverse table under the calling user's permissions. Proceed solely on explicit user consent: set hasUserApproved to true only after the user has affirmatively confirmed the deletion. The delete is permanent and executes with the user's own privileges; if the user cannot delete the record, the call fails with their access error. WRITE tool. Spaarke entity map: 'matter' = sprk_matter (name column sprk_mattername), 'project' = sprk_project (sprk_projectname), 'document' = sprk_document (sprk_documentname); people = contact, companies = account.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition(
                "tablename",
                "The logical (schema) name of the table to delete a record from (e.g. 'account', 'sprk_event').",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "hasUserApproved",
                "Set this to true only after asking the user if they are ok with the deletion and you have " +
                "received an affirmative response.",
                ToolParameterType.Boolean,
                Required: true),
            new ToolParameterDefinition(
                "recordId",
                "The GUID of the record to delete.",
                ToolParameterType.String,
                Required: true)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Chat;

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool) =>
        ToolValidationResult.Failure(
            "DataverseDeleteRecordHandler is chat-context-only (agent-loop tool). Playbook-context invocation is unsupported.");

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, AnalysisTool tool, CancellationToken cancellationToken) =>
        Task.FromResult(ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "DataverseDeleteRecordHandler is chat-context-only (agent-loop tool). Playbook-context invocation is unsupported.",
            ToolErrorCodes.ValidationFailed));

    /// <inheritdoc />
    public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required.");

        if (!TryParseArgs(context.ToolArgumentsJson, out _, out _, out _, out var error))
            return ToolValidationResult.Failure(error!);

        return ToolValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteChatAsync(
        ChatInvocationContext context,
        AnalysisTool tool,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        if (!TryParseArgs(context.ToolArgumentsJson, out var tablename, out var recordId, out var hasUserApproved, out var parseError))
        {
            return Error(tool, parseError!, ToolErrorCodes.ValidationFailed, startedAt);
        }

        // GA-contract argument semantics (frozen): delete proceeds solely on explicit user
        // consent carried by the LLM. This is argument validation, not the platform gate —
        // the P2 confirmation gate (task 031) keys on sprk_sideeffectclass upstream.
        if (!hasUserApproved)
        {
            return Error(tool,
                "Deletion requires explicit user approval: ask the user to confirm, then call again with " +
                "hasUserApproved = true.",
                ToolErrorCodes.ValidationFailed, startedAt);
        }

        try
        {
            // Entity-set resolution under the USER's token (read-handler pattern): a table
            // invisible to the user 404s here, BEFORE any delete is attempted.
            var metaResponse = await _dataverse.GetAsync(
                $"EntityDefinitions(LogicalName='{tablename}')?$select=EntitySetName",
                cancellationToken).ConfigureAwait(false);
            if (!metaResponse.IsSuccess)
            {
                return LogOutcome(context, tablename, recordId, MapClientError(tool, metaResponse, startedAt), stopwatch);
            }
            var entitySetName = GetString(metaResponse.Body!.Value, "EntitySetName");
            if (entitySetName is null)
            {
                return LogOutcome(context, tablename, recordId,
                    Error(tool, $"Table '{tablename}' has no entity-set name.", ToolErrorCodes.InternalError, startedAt),
                    stopwatch);
            }

            var response = await _dataverse.DeleteAsync($"{entitySetName}({recordId:D})", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                // Privilege-denied delete surfaces the USER's own access error — never escalates.
                return LogOutcome(context, tablename, recordId, MapClientError(tool, response, startedAt), stopwatch);
            }

            // No citation metadata: the record no longer exists, so the tables/…/records/{id}
            // path is not replayable (unlike create/update, which cite the surviving record).
            var result = ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new
                {
                    tool = DataverseToolNames.DeleteRecord,
                    tablename,
                    recordId = recordId.ToString("D"),
                    deleted = true
                },
                summary: $"Deleted record {recordId:D} from '{tablename}' (under the calling user's permissions).",
                confidence: 1.0,
                execution: Timed(startedAt)) with
            {
                // R4-6 (2026-07-07): user-facing outcome for the gate-resume transcript
                // message. No record reference — the record no longer exists to link to.
                Metadata = new Dictionary<string, object?>
                {
                    [ToolResultMetadataKeys.UserSummary] = $"Record deleted from '{tablename}'."
                }
            };

            return LogOutcome(context, tablename, recordId, result, stopwatch);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Error(tool, "dataverse.delete_record was cancelled.", ToolErrorCodes.Cancelled, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[dataverse.delete_record] failed decisionId={DecisionId}: {ErrorType}",
                context.DecisionId, ex.GetType().Name);
            return Error(tool, "dataverse.delete_record failed unexpectedly.", ToolErrorCodes.InternalError, startedAt);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private ToolResult LogOutcome(ChatInvocationContext context, string tablename, Guid recordId, ToolResult result, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        // ADR-015 / NFR-07: entity + record id + outcome + duration + deterministic IDs only.
        _logger.LogInformation(
            "[dataverse.delete_record][ADR-015] entity={Entity} recordId={RecordId} outcome={Outcome} decisionId={DecisionId} durationMs={DurationMs}",
            tablename, recordId, result.Success ? "ok" : result.ErrorCode, context.DecisionId, stopwatch.ElapsedMilliseconds);
        return result;
    }

    private ToolResult MapClientError(AnalysisTool tool, DataverseUserResponse response, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            response.ErrorMessage ?? "Dataverse request failed.",
            response.ErrorCode,
            Timed(startedAt));

    private ToolResult Error(AnalysisTool tool, string message, string code, DateTimeOffset startedAt) =>
        ToolResult.Error(HandlerId, tool.Id, tool.Name, message, code, Timed(startedAt));

    private static ToolExecutionMetadata Timed(DateTimeOffset startedAt) =>
        new() { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow };

    internal static bool TryParseArgs(string? argsJson, out string tablename, out Guid recordId, out bool hasUserApproved, out string? error)
    {
        tablename = string.Empty;
        recordId = Guid.Empty;
        hasUserApproved = false;
        error = null;

        if (string.IsNullOrWhiteSpace(argsJson))
        {
            error = "Tool arguments JSON is required (expected { \"tablename\": \"…\", \"hasUserApproved\": true, \"recordId\": \"…\" }).";
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(argsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Tool arguments must be a JSON object.";
                return false;
            }

            if (!doc.RootElement.TryGetProperty("tablename", out var tableProp) ||
                tableProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(tableProp.GetString()))
            {
                error = "Tool arguments must include a non-empty 'tablename' string (the table's logical name).";
                return false;
            }
            tablename = tableProp.GetString()!.Trim().ToLowerInvariant();
            if (!LogicalNameRegex().IsMatch(tablename))
            {
                error = $"'{tablename}' is not a valid table logical name.";
                return false;
            }

            if (!doc.RootElement.TryGetProperty("hasUserApproved", out var approvedProp) ||
                approvedProp.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                error = "Tool arguments must include a boolean 'hasUserApproved' (GA delete_record contract).";
                return false;
            }
            hasUserApproved = approvedProp.ValueKind == JsonValueKind.True;

            if (!doc.RootElement.TryGetProperty("recordId", out var idProp) ||
                idProp.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(idProp.GetString(), out recordId))
            {
                error = "Tool arguments must include a 'recordId' GUID.";
                return false;
            }

            return true;
        }
        catch (JsonException ex)
        {
            error = $"Tool arguments JSON is malformed: {ex.Message}";
            return false;
        }
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
