using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat-side typed handler for the <c>dataverse.create_record</c> tool — inserts one row into
/// a Dataverse table over the user-OBO Web API
/// (spaarke-ai-architecture-redesign-r1 task 009, FR-P0-07 write half).
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-039 contract freeze</b>: name + argument shape mirror the GA Dataverse MCP
/// <c>create_record</c> tool — <c>create_record(tablename, item)</c> where <c>item</c> is a
/// key/value object of column logical names (lookups as
/// <c>{"relatedTable","name","recordId"}</c>; choice columns as numeric option values;
/// multi-select as comma-separated values). See <see cref="DataverseToolNames"/> for the
/// frozen-name citation and <see cref="DataverseWriteItemMapper"/> for the item contract.
/// </para>
/// <para>
/// <b>SIDE-EFFECT tool</b>: the <c>sprk_analysistool</c> row declares
/// <c>sprk_sideeffectclass = Write (100000001)</c>. The P2 confirmation gate (FR-P2-02,
/// task 031) suspends/resumes this tool BY THAT DECLARED CLASS — deliberately, NO gating or
/// confirmation logic lives in this handler; when invoked, it executes.
/// </para>
/// <para>
/// <b>User-OBO ONLY (spec MUST rule)</b>: the insert executes through
/// <see cref="IDataverseUserClient"/> under the CALLING USER's exchanged token. A create the
/// user lacks privileges for fails with the user's own access error (403 →
/// <see cref="DataverseUserClientErrorCodes.AccessDenied"/>); a table invisible to the user
/// 404s at metadata resolution BEFORE any write is attempted. No app-only client is reachable
/// from this class (task-012 audit).
/// </para>
/// <para>
/// <b>ADR-015 / NFR-07</b>: telemetry carries table logical name, column COUNT, outcome,
/// duration, and the created record id — never column values.
/// </para>
/// </remarks>
public sealed partial class DataverseCreateRecordHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(DataverseCreateRecordHandler);

    /// <summary>
    /// G-P3 UAT round-5 R5-E (2026-07-07): HARD block on <c>sprk_document</c> creates.
    /// Round-3's R3-4 fix banned fileless document creation at the DESCRIPTION level only —
    /// the model ignored the guidance and created a bare, broken sprk_document row (no SPE
    /// file, empty profile, Similar Documents errors). Guidance is not enforcement: this
    /// handler now REJECTS the table outright (<see cref="ToolErrorCodes.ValidationFailed"/>,
    /// before any Dataverse call) in BOTH <see cref="ValidateChat"/> (the gate-resume leg
    /// runs it before <see cref="ExecuteChatAsync"/>) and <see cref="ExecuteChatAsync"/>
    /// (defense in depth). Spaarke documents require the full ingestion pipeline (file
    /// upload → SPE storage → document profile → indexing) that only the Document Upload
    /// wizard drives. Exposed internal for the enforcement tests.
    /// </summary>
    internal const string BlockedTableLogicalName = "sprk_document";

    /// <summary>
    /// The single rejection message for blocked <c>sprk_document</c> creates. Serves BOTH
    /// audiences on purpose: the gate failure leg persists this text verbatim into the ❌
    /// transcript message (user-facing honesty) AND into conversation history the model
    /// reads on the next turn (model-facing remediation — never retry, offer alternatives).
    /// </summary>
    internal const string SprkDocumentCreateBlockedMessage =
        "Spaarke document records can't be created from chat — this create was REJECTED and nothing " +
        "was written. Documents need the full ingestion pipeline (file upload → secure storage → " +
        "document profile) that the Document Upload wizard drives; a row created here would be a " +
        "broken, fileless document. Use the Document Upload wizard to add a document, or upload the " +
        "file into this chat and I can work with it in-session. Do NOT retry this create — " +
        "sprk_document is blocked on this tool unconditionally and no argument change makes it " +
        "valid; offer an alternative instead (Document Upload wizard, create a task, or draft an email).";

    [GeneratedRegex(@"^[a-z][a-z0-9_]*$")]
    private static partial Regex LogicalNameRegex();

    private readonly IDataverseUserClient _dataverse;
    private readonly ILogger<DataverseCreateRecordHandler> _logger;

    public DataverseCreateRecordHandler(
        IDataverseUserClient dataverse,
        ILogger<DataverseCreateRecordHandler> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Dataverse Create Record",
        // FR-A-01 (AIR2-020): mirror of the authored sprk_description in infra/dataverse/sprk_analysistool-dataverse-create-record-row.json — keep byte-equal; edit the JSON, not this literal.
        Description: @"Inserts one row into a Dataverse table under the calling user's permissions and returns the created record id as a citable path (tables/{table}/records/{guid}). Call dataverse.describe first if the table's schema is unknown — do NOT guess column logical names from display names. Values: strings/numbers/booleans for simple fields; numeric option values for choice columns (NEVER labels — use dataverse.describe to find the numeric values); comma-separated numeric values for multi-select choice; lookup fields as {""relatedTable"": ""..."", ""recordId"": ""guid""} — recordId is REQUIRED: resolve the target record's GUID FIRST via dataverse.search_data or dataverse.read_query IN THE SAME TURN you draft the proposal, BEFORE asking the user to confirm; a lookup without recordId is rejected. NEVER use OData annotations as item keys — no '@odata.bind', no '@' or '.' in any key: the ONLY way to set a lookup on this transport is the {relatedTable, recordId} object form, and choice columns take plain numeric values. If you cannot resolve a GUID for an optional lookup or the numeric value for an optional choice, OMIT that column and mention the value in a text column instead — never guess. sprk_matter write contract (from live metadata — verify anything else with dataverse.describe): sprk_mattername (text, REQUIRED); sprk_matternumber (text); sprk_matterdescription (multiline text); sprk_mattertype is a LOOKUP to sprk_mattertype_ref and sprk_practicearea is a LOOKUP to sprk_practicearea_ref — resolve the reference row's GUID via dataverse.read_query first and send {""relatedTable"":""sprk_mattertype_ref"",""recordId"":""guid""} (same pattern for practice area with sprk_practicearea_ref), or OMIT the column and put the value in sprk_matterdescription. Records you create belong to the calling user automatically — never set owner/assignee columns for the requesting user themselves. HARD RULE — sprk_document creates are BLOCKED: this tool REJECTS any create on the sprk_document table before anything is written (VALIDATION_FAILED). Spaarke document records require the full ingestion pipeline (file upload → SharePoint Embedded storage → document profile → indexing) that only the Document Upload wizard drives; no chat tool can upload file content. Never attempt or retry a sprk_document create — no argument change makes it valid. If asked to create a document or save chat output 'to documents', say honestly that documents can't be created from chat and point the user to the Document Upload wizard (or offer an alternative: work with a file uploaded into this chat, create a task, draft an email, or open a workspace tab). WRITE tool: executes with the user's own privileges; if the user cannot create rows in the table, the call fails with their access error. Spaarke entity map: 'matter' = sprk_matter (name column sprk_mattername), 'project' = sprk_project (sprk_projectname), 'document' = sprk_document (sprk_documentname — creation BLOCKED on this tool, Document Upload wizard only); people = contact, companies = account.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition(
                "tablename",
                "The logical (schema) name of the table to insert into (e.g. 'account', 'sprk_event'). " +
                "Use dataverse.describe with path 'tables/' to find logical names.",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "item",
                "Record field values as key-value pairs. Keys are PLAIN column logical names from dataverse.describe — " +
                "NEVER OData annotations ('sprk_column@odata.bind' is invalid on this transport and is rejected). " +
                "Values: strings, numbers, or booleans for simple fields. For lookup/customer fields use an object: " +
                "{\"relatedTable\": \"account\", \"name\": \"Contoso Ltd\", \"recordId\": \"guid\"} — recordId is " +
                "REQUIRED on this transport (resolve it first via dataverse.search_data / dataverse.read_query; " +
                "never send a lookup with only a name). For choice fields use the numeric option value, never the " +
                "label. For multi-select choice use comma-separated values like \"100000002,100000004\". Omit any " +
                "optional column whose value you cannot resolve precisely rather than guessing.",
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
            "DataverseCreateRecordHandler is chat-context-only (agent-loop tool). Playbook-context invocation is unsupported.");

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, AnalysisTool tool, CancellationToken cancellationToken) =>
        Task.FromResult(ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "DataverseCreateRecordHandler is chat-context-only (agent-loop tool). Playbook-context invocation is unsupported.",
            ToolErrorCodes.ValidationFailed));

    /// <inheritdoc />
    public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required.");

        if (!TryParseArgs(context.ToolArgumentsJson, out var tablename, out _, out var error))
            return ToolValidationResult.Failure(error!);

        // R5-E HARD RULE: sprk_document creates never execute — the gate-resume leg
        // (TypedHandlerResumeExecutor) runs ValidateChat BEFORE ExecuteChatAsync, so this
        // rejection fires before any Dataverse wire call on every path.
        if (string.Equals(tablename, BlockedTableLogicalName, StringComparison.Ordinal))
            return ToolValidationResult.Failure(SprkDocumentCreateBlockedMessage);

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

        if (!TryParseArgs(context.ToolArgumentsJson, out var tablename, out var item, out var parseError))
        {
            return Error(tool, parseError!, ToolErrorCodes.ValidationFailed, startedAt);
        }

        // R5-E HARD RULE (defense in depth — ValidateChat already rejects): sprk_document
        // creates are blocked BEFORE any Dataverse call. TryParseArgs lower-cases the
        // table name, so casing variants cannot slip past.
        if (string.Equals(tablename, BlockedTableLogicalName, StringComparison.Ordinal))
        {
            return LogOutcome(context, tablename,
                Error(tool, SprkDocumentCreateBlockedMessage, ToolErrorCodes.ValidationFailed, startedAt),
                stopwatch);
        }

        try
        {
            // Entity-set + primary-id resolution under the USER's token (read-handler pattern):
            // a table invisible to the user 404s here, BEFORE any write is attempted.
            var metaResponse = await _dataverse.GetAsync(
                $"EntityDefinitions(LogicalName='{tablename}')?$select=EntitySetName,PrimaryIdAttribute",
                cancellationToken).ConfigureAwait(false);
            if (!metaResponse.IsSuccess)
            {
                return LogOutcome(context, tablename, MapClientError(tool, metaResponse, startedAt), stopwatch);
            }
            var entitySetName = GetString(metaResponse.Body!.Value, "EntitySetName");
            var primaryIdAttribute = GetString(metaResponse.Body!.Value, "PrimaryIdAttribute");
            if (entitySetName is null || primaryIdAttribute is null)
            {
                return LogOutcome(context, tablename,
                    Error(tool, $"Table '{tablename}' has no entity-set / primary-id metadata.", ToolErrorCodes.InternalError, startedAt),
                    stopwatch);
            }

            var mapped = await DataverseWriteItemMapper.MapAsync(_dataverse, tablename, item, cancellationToken).ConfigureAwait(false);
            if (mapped.ValidationError is not null)
            {
                return LogOutcome(context, tablename, Error(tool, mapped.ValidationError, ToolErrorCodes.ValidationFailed, startedAt), stopwatch);
            }
            if (mapped.ClientFailure is not null)
            {
                return LogOutcome(context, tablename, MapClientError(tool, mapped.ClientFailure, startedAt), stopwatch);
            }

            // Prefer: return=representation so the created row (incl. primary id) comes back
            // without a second GET — task-008 review knob on PostAsync.
            var response = await _dataverse.PostAsync(
                $"/api/data/v9.2/{entitySetName}",
                mapped.Item!.JsonBody,
                preferRepresentation: true,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                // Privilege-denied create surfaces the USER's own access error — never escalates.
                return LogOutcome(context, tablename, MapClientError(tool, response, startedAt), stopwatch);
            }

            var warnings = new List<string>();
            Guid? createdId = null;
            if (response.Body is { } body &&
                body.ValueKind == JsonValueKind.Object &&
                body.TryGetProperty(primaryIdAttribute, out var idProp) &&
                idProp.ValueKind == JsonValueKind.String &&
                Guid.TryParse(idProp.GetString(), out var parsedId))
            {
                createdId = parsedId;
            }
            else
            {
                warnings.Add("The record was created but Dataverse did not echo the created row; the record id could not be determined. " +
                             "Use dataverse.search_data or dataverse.read_query to locate the new record.");
            }

            var citationPath = createdId.HasValue
                ? DataverseRecordCitations.RecordPath(tablename, createdId.Value)
                : null;

            var result = ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new
                {
                    tool = DataverseToolNames.CreateRecord,
                    tablename,
                    recordId = createdId?.ToString("D"),
                    path = citationPath,
                    columnsSet = mapped.Item.Columns,
                    columnCount = mapped.Item.Columns.Count
                },
                summary: createdId.HasValue
                    ? $"Created record {createdId:D} in '{tablename}' ({mapped.Item.Columns.Count} columns set, under the calling user's permissions)."
                    : $"Created a record in '{tablename}' ({mapped.Item.Columns.Count} columns set); id not echoed.",
                confidence: 1.0,
                execution: Timed(startedAt),
                warnings: warnings);

            if (createdId.HasValue)
            {
                result = result with
                {
                    Metadata = new Dictionary<string, object?>
                    {
                        [ToolResultMetadataKeys.Citations] = new[] { DataverseRecordCitations.ForRecord(tablename, createdId.Value) },
                        // R4-6: user-facing outcome sentence (the transcript ✅ message renders
                        // this verbatim — no instruction-to-model text). R4-3: the record
                        // reference the gate-resume seam composes the clickable MDA link from.
                        [ToolResultMetadataKeys.UserSummary] =
                            $"Record created in '{tablename}' (id {createdId:D}).",
                        [ToolResultMetadataKeys.CreatedRecord] = new ToolCreatedRecord(tablename, createdId.Value)
                    }
                };
            }

            return LogOutcome(context, tablename, result, stopwatch);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Error(tool, "dataverse.create_record was cancelled.", ToolErrorCodes.Cancelled, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[dataverse.create_record] failed decisionId={DecisionId}: {ErrorType}",
                context.DecisionId, ex.GetType().Name);
            return Error(tool, "dataverse.create_record failed unexpectedly.", ToolErrorCodes.InternalError, startedAt);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private ToolResult LogOutcome(ChatInvocationContext context, string tablename, ToolResult result, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        // ADR-015 / NFR-07: entity + outcome + duration + deterministic IDs only — never values.
        _logger.LogInformation(
            "[dataverse.create_record][ADR-015] entity={Entity} outcome={Outcome} decisionId={DecisionId} durationMs={DurationMs}",
            tablename, result.Success ? "ok" : result.ErrorCode, context.DecisionId, stopwatch.ElapsedMilliseconds);
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

    internal static bool TryParseArgs(string? argsJson, out string tablename, out JsonElement item, out string? error)
    {
        tablename = string.Empty;
        item = default;
        error = null;

        if (string.IsNullOrWhiteSpace(argsJson))
        {
            error = "Tool arguments JSON is required (expected { \"tablename\": \"…\", \"item\": { … } }).";
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
