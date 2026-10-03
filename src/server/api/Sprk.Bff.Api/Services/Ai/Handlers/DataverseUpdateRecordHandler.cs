using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;

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
/// <b>User-OBO (spec MUST rule)</b>: executes through <see cref="IDataverseUserClient"/>
/// under the calling user's exchanged token; privilege-denied updates surface the user's own
/// access error.
/// </para>
/// <para>
/// <b>A RE-FILE re-derives the owner (unified-access-control-r2 task 146 r2, verifier item 2).</b> An update that sets or
/// clears a lookup to a project, matter, work assignment or another ownership parent on a CHILD table moves the row into
/// or out of that record. Its owner is re-derived through the one
/// <see cref="Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver.ReparentAsync"/>: the caller's PATCH (still run as
/// the caller — Dataverse authorizes it) is applied only once the owner is decided, then the owner is assigned in a
/// separate write and read back — the same re-file every other BFF writer makes. Before anything is decided, the caller
/// must see the row and hold AppendTo on each record it is moved under, so a refusal never answers questions about
/// records they cannot see. A row of any other table moved under a SECURE record is refused (it cannot be re-owned here).
/// The app-only steps are the resolver's reads, the owner assignment and, if that assignment fails, the restore of the
/// filing columns the PATCH moved (owner S1 / G5: "owned by the team, never the user"). They run under CLAUDE.md §6.5
/// PATH A, task 146 note §12c, which stays in force for this tool. spaarke-ai-architecture-redesign-r1 spec Amendment
/// A-UAC146 (path B, owner round 7 item 3) covers the two CREATE tools only, and records this exception beside it.
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
    private readonly Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver _ownership;

    public DataverseUpdateRecordHandler(
        IDataverseUserClient dataverse,
        ILogger<DataverseUpdateRecordHandler> logger,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // Task 146 r2: unconditionally registered (MetadataServiceExtensions) — no asymmetric registration (§10 F.1).
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Dataverse Update Record",
        // FR-A-01 (AIR2-020): mirror of the authored sprk_description in infra/dataverse/sprk_analysistool-dataverse-update-record-row.json — keep byte-equal; edit the JSON, not this literal.
        Description: @"Updates columns on one existing Dataverse record under the calling user's permissions. Call dataverse.describe first if the table's schema is unknown — do NOT guess column logical names from display names. Only the columns present in 'item' are changed. Values: strings/numbers/booleans for simple fields; numeric option values for choice columns; comma-separated numeric values for multi-select choice; lookup fields as {""relatedTable"": ""..."", ""recordId"": ""guid""}. Update-only: a record that does not exist or is not visible to the user fails with not-found — it is never created. WRITE tool: executes with the user's own privileges. Spaarke entity map: 'matter' = sprk_matter (name column sprk_mattername), 'project' = sprk_project (sprk_projectname), 'document' = sprk_document (sprk_documentname); people = contact, companies = account.",
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
            var primaryIdAttribute = GetString(metaResponse.Body!.Value, "PrimaryIdAttribute");
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

            // Task 146 r2 (verifier item 2): a change to the records the row is FILED under is a re-file.
            var refile = await RefileIfFiledAsync(
                tool, tablename, recordId, entitySetName, primaryIdAttribute, mapped.Item!, context, startedAt, cancellationToken)
                .ConfigureAwait(false);
            if (refile.Result is { } refileResult)
            {
                return LogOutcome(context, tablename, recordId, refileResult, stopwatch);
            }

            if (!refile.Written)
            {
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
            }

            var result = ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new
                {
                    tool = DataverseToolNames.UpdateRecord,
                    tablename,
                    recordId = recordId.ToString("D"),
                    path = DataverseRecordCitations.RecordPath(tablename, recordId),
                    columnsUpdated = mapped.Item!.Columns,
                    columnCount = mapped.Item.Columns.Count
                },
                summary: $"Updated {mapped.Item.Columns.Count} column(s) on record {recordId:D} in '{tablename}' (under the calling user's permissions).",
                confidence: 1.0,
                execution: Timed(startedAt)) with
            {
                Metadata = new Dictionary<string, object?>
                {
                    [ToolResultMetadataKeys.Citations] = new[] { DataverseRecordCitations.ForRecord(tablename, recordId) },
                    // R4-6/R4-3 (2026-07-07): user-facing outcome + record reference for the
                    // gate-resume transcript message and its clickable MDA link.
                    [ToolResultMetadataKeys.UserSummary] =
                        $"Record updated in '{tablename}' ({mapped.Item.Columns.Count} column(s) changed).",
                    [ToolResultMetadataKeys.CreatedRecord] = new ToolCreatedRecord(tablename, recordId)
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

    // ── re-file (task 146 r2, verifier item 2) ─────────────────────────────────

    /// <summary>What the re-file step decided: a finished tool <c>Result</c> (a refusal or the caller's own error), or
    /// <c>Written</c> when the caller's PATCH already ran inside the re-file. Neither = an ordinary update.</summary>
    private readonly record struct RefileStep(ToolResult? Result, bool Written);

    /// <summary>The caller's PATCH was refused by Dataverse inside the re-file — carried out so no owner is assigned.</summary>
    private sealed class CallerWriteFailedException(DataverseUserResponse response) : Exception("The caller's update was refused.")
    {
        public DataverseUserResponse Response { get; } = response;
    }

    /// <summary>
    /// Task 146 r2 (verifier item 2). For a CHILD table, an update that sets or clears a lookup to an ownership parent is a
    /// re-file: the caller must see the row and hold AppendTo on each record it is moved under (asked as the caller, so no
    /// refusal answers questions about records they cannot see); then <c>ReparentAsync</c> decides the owner, applies the
    /// caller's own PATCH, assigns the owner separately and reads it back. A refusal writes nothing. For a table outside the
    /// ownership set (not a root), a row moved under a SECURE record is refused: it cannot be re-owned here. A root's own
    /// ownership is provisioning's (owner S6), so a root's lookups re-own nothing — as for every generic writer.
    /// </summary>
    private async Task<RefileStep> RefileIfFiledAsync(
        AnalysisTool tool, string tablename, Guid recordId, string entitySetName, string? primaryIdAttribute,
        DataverseWriteItemMapper.MappedItem item, ChatInvocationContext context, DateTimeOffset startedAt, CancellationToken ct)
    {
        var isChild = Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver.IsReparentableChild(tablename);
        if (!isChild && Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver.IsOwnershipParent(tablename))
        {
            return default; // a root
        }

        var newParents = item.Lookups
            .Where(l => Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver.IsOwnershipParent(l.RelatedTable))
            .ToArray();
        var changes = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?>(StringComparer.OrdinalIgnoreCase);
        foreach (var lookup in newParents)
        {
            changes[lookup.Column] = new Microsoft.Xrm.Sdk.EntityReference(lookup.RelatedTable, lookup.RecordId);
        }

        if (isChild)
        {
            // A cleared column may be a cleared lookup — the row cannot tell a generic writer which (ReparentAsync reads it).
            foreach (var column in item.ClearedColumns)
            {
                changes.TryAdd(column, null);
            }
        }

        if (changes.Count == 0 || (!isChild && newParents.Length == 0))
        {
            return default;
        }

        var me = await OwnedChildWrite.WhoAmIAsync(_dataverse, ct).ConfigureAwait(false);
        if (me.Failure is { } whoAmIFailure)
        {
            return new RefileStep(MapClientError(tool, whoAmIFailure, startedAt), false);
        }

        var appendTo = await OwnedChildWrite.CheckCallerMayAppendToAsync(_dataverse, me.SystemUserId, newParents, ct)
            .ConfigureAwait(false);
        if (appendTo.Denied is { } denied)
        {
            return new RefileStep(Error(tool, denied, DataverseUserClientErrorCodes.AccessDenied, startedAt), false);
        }

        var callerOid = Guid.TryParse(context.UserId, out var oid) && oid != Guid.Empty ? oid : (Guid?)null;

        if (!isChild)
        {
            var owner = await _ownership.ResolveOwnerAsync(
                Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext.ForParents(
                    newParents.Select(l => new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipParent(l.RelatedTable, l.RecordId)),
                    callerOid, me.SystemUserId),
                ct).ConfigureAwait(false);
            return owner.IsRefused || owner.IsSecureOwner
                ? new RefileStep(Error(tool,
                    $"A '{tablename}' record cannot be filed under a secure record from chat; the update was NOT written" +
                    (owner.IsRefused ? $" ({owner.RefusalCode})." : ".") + " Change it from the record itself.",
                    owner.RefusalCode ?? ToolErrorCodes.ValidationFailed, startedAt), false)
                : default;
        }

        // The caller must see the row before anything is decided about it (their own 404/403 otherwise).
        var row = await _dataverse.GetAsync(
            $"{entitySetName}({recordId:D})?$select={primaryIdAttribute ?? tablename + "id"}", ct).ConfigureAwait(false);
        if (!row.IsSuccess)
        {
            return new RefileStep(MapClientError(tool, row, startedAt), false);
        }

        try
        {
            var resolution = await _ownership.ReparentAsync(
                new Sprk.Bff.Api.Services.Dataverse.RecordReparent
                {
                    EntityLogicalName = tablename,
                    RecordId = recordId,
                    ParentChanges = changes,
                    CallerObjectId = callerOid,
                    CallerSystemUserId = me.SystemUserId,
                    // E1: a communication filed under nothing keeps its creator.
                    WhenUnfiled = string.Equals(tablename, "sprk_communication", StringComparison.OrdinalIgnoreCase)
                        ? Sprk.Bff.Api.Services.Dataverse.UnfiledOwnership.KeepCreator
                        : Sprk.Bff.Api.Services.Dataverse.UnfiledOwnership.ActingUserTeam,
                    // Owner round 10 item 7 (task 146 c1): a re-file that moves the row OUT of a secure root is an
                    // un-secure. F3 is asked AS THE CALLER — WhoAmI above, RetrievePrincipalAccess under their own token —
                    // before anything is written.
                    SecureExitCaller = new Sprk.Bff.Api.Services.Access.SecureRemovalCaller(
                        _ => Task.FromResult<Guid?>(me.SystemUserId),
                        (record, token) => OwnedChildWrite.RightsOnAsync(
                            _dataverse,
                            me.SystemUserId,
                            Sprk.Bff.Api.Services.Access.SecureDesignationRemoval.EntitySetFor(record.EntityLogicalName),
                            record.RecordId,
                            token)),
                },
                async token =>
                {
                    // PATCH carries If-Match: * (update-only), run AS THE CALLER — Dataverse authorizes the change itself.
                    var response = await _dataverse.PatchAsync($"{entitySetName}({recordId:D})", item.JsonBody, token)
                        .ConfigureAwait(false);
                    if (!response.IsSuccess)
                        throw new CallerWriteFailedException(response);
                },
                ct).ConfigureAwait(false);

            return resolution switch
            {
                // F3 (owner round 10 item 7): the caller may not move the row out of a secure root — the unsecure
                // endpoint's message and reason code, not an owner refusal.
                { IsForbidden: true } => new RefileStep(Error(tool,
                    $"The update was NOT written. {resolution.Reason}",
                    resolution.RefusalCode ?? DataverseUserClientErrorCodes.AccessDenied, startedAt), false),
                { IsRefused: true } => new RefileStep(Error(tool,
                    $"The update was NOT written: the record's owner could not be decided — {resolution.Reason} ({resolution.RefusalCode}).",
                    resolution.RefusalCode ?? ToolErrorCodes.ValidationFailed, startedAt), false),
                _ => new RefileStep(null, true),
            };
        }
        catch (CallerWriteFailedException failed)
        {
            // Privilege-denied update surfaces the USER's own access error — never escalates; no owner was assigned.
            return new RefileStep(MapClientError(tool, failed.Response, startedAt), false);
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
