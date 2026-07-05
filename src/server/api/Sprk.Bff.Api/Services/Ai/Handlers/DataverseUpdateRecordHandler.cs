using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat-side typed handler for the <c>dataverse.update_record</c> tool — updates one existing
/// row in a Dataverse table over the user-OBO Web API
/// (spaarke-ai-architecture-redesign-r1 task 009, FR-P0-07 write half).
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-039 contract freeze</b>: name + argument shape mirror the GA Dataverse MCP
/// <c>update_record</c> tool — <c>update_record(tablename, recordId, item)</c> with the same
/// <c>item</c> value contract as <c>create_record</c> (see
/// <see cref="DataverseWriteItemMapper"/>). See <see cref="DataverseToolNames"/> for the
/// frozen-name citation.
/// </para>
/// <para>
/// <b>Update-only</b>: the PATCH is issued with <c>If-Match: *</c> (see
/// <see cref="IDataverseUserClient.PatchAsync"/>) so a missing/invisible record yields the
/// user's own 404 instead of the Web API's default upsert-create.
/// </para>
/// <para>
/// <b>SIDE-EFFECT tool</b>: the <c>sprk_analysistool</c> row declares
/// <c>sprk_sideeffectclass = Write (100000001)</c>. The P2 confirmation gate (FR-P2-02,
/// task 031) gates by that declared class — NO gating/confirmation logic lives here.
/// </para>
/// <para>
/// <b>User-OBO ONLY (spec MUST rule)</b>: executes through <see cref="IDataverseUserClient"/>
/// under the calling user's exchanged token; privilege-denied updates surface the user's own
/// access error. No app-only client is reachable from this class (task-012 audit).
/// </para>
/// <para>
/// <b>ADR-015 / NFR-07</b>: telemetry carries table logical name, record id, column COUNT,
/// outcome, duration — never column values.
/// </para>
/// </remarks>
public sealed partial class DataverseUpdateRecordHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(DataverseUpdateRecordHandler);

    [GeneratedRegex(@"^[a-z][a-z0-9_]*$")]
    private static partial Regex LogicalNameRegex();

    private readonly IDataverseUserClient _dataverse;
    private readonly ILogger<DataverseUpdateRecordHandler> _logger;

    public DataverseUpdateRecordHandler(
        IDataverseUserClient dataverse,
        ILogger<DataverseUpdateRecordHandler> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Dataverse Update Record",
        Description: "Updates columns on one existing Dataverse record under the calling user's permissions. " +
                     "Call dataverse.describe first if the table's schema is unknown — do NOT guess column " +
                     "logical names from display names. Choice columns require numeric option values, not labels. " +
                     "Only the columns present in 'item' are changed. Update-only: a record that does not exist " +
                     "or is not visible to the user fails with not-found — it is never created. " +
                     "SIDE-EFFECT tool (write): executed with the user's own privileges.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition(
                "tablename",
                "The logical (schema) name of the table to update a record in (e.g. 'account', 'sprk_event').",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "recordId",
                "The GUID of the record to update.",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "item",
                "Properties to update as key-value pairs. Keys are column logical names from dataverse.describe. " +
                "Values: strings, numbers, or booleans for simple fields. For lookup/customer fields use an object: " +
                "{\"relatedTable\": \"account\", \"name\": \"Contoso Ltd\", \"recordId\": \"guid\"} (recordId required " +
                "on this transport). For choice fields use the numeric option value. For multi-select choice use " +
                "comma-separated values like \"100000002,100000004\".",
                ToolParameterType.Object,
                Required: true)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Chat;

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool) =>
        ToolValidationResult.Failure(
            "DataverseUpdateRecordHandler is chat-context-only (agent-loop tool). Playbook-context invocation is unsupported.");

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, AnalysisTool tool, CancellationToken cancellationToken) =>
        Task.FromResult(ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "DataverseUpdateRecordHandler is chat-context-only (agent-loop tool). Playbook-context invocation is unsupported.",
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

        if (!TryParseArgs(context.ToolArgumentsJson, out var tablename, out var recordId, out var item, out var parseError))
        {
            return Error(tool, parseError!, ToolErrorCodes.ValidationFailed, startedAt);
        }

        try
        {
            // Entity-set resolution under the USER's token (read-handler pattern): a table
            // invisible to the user 404s here, BEFORE any write is attempted.
            var metaResponse = await _dataverse.GetAsync(
                $"EntityDefinitions(LogicalName='{tablename}')?$select=EntitySetName,PrimaryIdAttribute",
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

            var mapped = await DataverseWriteItemMapper.MapAsync(_dataverse, tablename, item, cancellationToken).ConfigureAwait(false);
            if (mapped.ValidationError is not null)
            {
                return LogOutcome(context, tablename, recordId, Error(tool, mapped.ValidationError, ToolErrorCodes.ValidationFailed, startedAt), stopwatch);
            }
            if (mapped.ClientFailure is not null)
            {
                return LogOutcome(context, tablename, recordId, MapClientError(tool, mapped.ClientFailure, startedAt), stopwatch);
            }

            // PATCH carries If-Match: * (update-only) — see IDataverseUserClient.PatchAsync.
            var response = await _dataverse.PatchAsync(
                $"{entitySetName}({recordId:D})",
                mapped.Item!.JsonBody,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                // Privilege-denied update surfaces the USER's own access error — never escalates.
                return LogOutcome(context, tablename, recordId, MapClientError(tool, response, startedAt), stopwatch);
            }

            var result = ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new
                {
                    tool = DataverseToolNames.UpdateRecord,
                    tablename,
                    recordId = recordId.ToString("D"),
                    path = DataverseRecordCitations.RecordPath(tablename, recordId),
                    columnsUpdated = mapped.Item.Columns,
                    columnCount = mapped.Item.Columns.Count
                },
                summary: $"Updated {mapped.Item.Columns.Count} column(s) on record {recordId:D} in '{tablename}' (under the calling user's permissions).",
                confidence: 1.0,
                execution: Timed(startedAt)) with
            {
                Metadata = new Dictionary<string, object?>
                {
                    [ToolResultMetadataKeys.Citations] = new[] { DataverseRecordCitations.ForRecord(tablename, recordId) }
                }
            };

            return LogOutcome(context, tablename, recordId, result, stopwatch);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Error(tool, "dataverse.update_record was cancelled.", ToolErrorCodes.Cancelled, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[dataverse.update_record] failed decisionId={DecisionId}: {ErrorType}",
                context.DecisionId, ex.GetType().Name);
            return Error(tool, "dataverse.update_record failed unexpectedly.", ToolErrorCodes.InternalError, startedAt);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private ToolResult LogOutcome(ChatInvocationContext context, string tablename, Guid recordId, ToolResult result, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        // ADR-015 / NFR-07: entity + record id + outcome + duration + deterministic IDs only.
        _logger.LogInformation(
            "[dataverse.update_record][ADR-015] entity={Entity} recordId={RecordId} outcome={Outcome} decisionId={DecisionId} durationMs={DurationMs}",
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

    internal static bool TryParseArgs(string? argsJson, out string tablename, out Guid recordId, out JsonElement item, out string? error)
    {
        tablename = string.Empty;
        recordId = Guid.Empty;
        item = default;
        error = null;

        if (string.IsNullOrWhiteSpace(argsJson))
        {
            error = "Tool arguments JSON is required (expected { \"tablename\": \"…\", \"recordId\": \"…\", \"item\": { … } }).";
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

            if (!doc.RootElement.TryGetProperty("recordId", out var idProp) ||
                idProp.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(idProp.GetString(), out recordId))
            {
                error = "Tool arguments must include a 'recordId' GUID.";
                return false;
            }

            if (!doc.RootElement.TryGetProperty("item", out var itemProp) || itemProp.ValueKind != JsonValueKind.Object)
            {
                error = "Tool arguments must include an 'item' object of column logical names to values.";
                return false;
            }
            item = itemProp.Clone();
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
