using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;

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
/// <b>User-OBO (spec MUST rule)</b>: the caller's update executes through <see cref="IDataverseUserClient"/>
/// under the calling user's exchanged token; privilege-denied updates surface the user's own
/// access error. That is unchanged: no step of the caller's own write runs app-only.
/// </para>
/// <para>
/// <b>The "User-OBO ONLY" rule is AMENDED for ONE helper — owner decisions round 8 item 1 (2026-10-03), CLAUDE.md §6.5
/// path B</b> (unified-access-control-r2 task 156; the same reasoning as round 7 item 3, which amended the rule for the
/// two AI CREATE tools). An update can change what a to-do / event / communication / analysis is filed under, or the
/// matter / project of a record others are filed under, which leaves copies of that root stale. The core-ancestor
/// re-stamp is a SERVER-owned invariant written app-only (ADR-002 WP-1), and task 156 AC1 requires it IN THE SAME
/// OPERATION as the re-file. So, once the caller's PATCH has succeeded, this class calls
/// <see cref="Sprk.Bff.Api.Services.Dataverse.CoreAncestorAfterWriteRestamp"/> inline. That helper is the re-stamp's ONLY app-only
/// dependency (the re-file step below is the other, owner round 13 item 7), and it is narrow by construction: its ONE member re-stamps the copies the write just moved and writes
/// only stamp columns, with values derived from the data (never a value from the caller). It exposes no Dataverse
/// client and holds no <see cref="IServiceProvider"/>, so nothing can be resolved through it; the app-only client the
/// re-stamp writes with stays private to <c>CoreAncestorRestamper</c>. The queued path this tool used before
/// (an enqueue onto <c>CoreAncestorRestampQueue</c>, run by the background job seconds later) is removed; the queue stays
/// for the storage resolver's stale refusals. Record: the task 156 note (owner round 8 section) and the task 156 POML
/// execution block.
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
/// filing columns the PATCH moved (owner S1 / G5: "owned by the team, never the user"). <b>CLAUDE.md §6.5 PATH B — owner
/// round 13 item 7 (2026-10-03): "the update tool's re-file step is folded into ADR path B, as round 8 did for 156's
/// re-stamp."</b> spaarke-ai-architecture-redesign-r1 spec Amendment A-UAC146 now covers this step beside the two CREATE
/// tools; it supersedes the project-scoped path-A record (task 146 note §12c) for it. The caller's own PATCH stays
/// user-OBO; F3 on a move out of a secure root is asked AS THE CALLER (task 146 c1).
/// After whichever path wrote the caller's update (the re-file's PATCH or the plain PATCH), the task 156 re-stamp runs,
/// then the task 142 Assigned-To materializer (batch 4 integration).
/// <b>Also (unified-access-control-r2 task 158):</b>
/// (3) ACCEPTED (owner round 32, 2026-10-04 — unified-access-control-r2 task 158, owner rounds 6 and 31; the same reasoning
/// as (1) and (2), recorded as Amendment A-UAC158, an extension of A-UAC146, in spaarke-ai-architecture-redesign-r1's spec):
/// when the caller's own update files a work assignment or project under a SECURE matter or project, the record's recorded
/// creator is first checked against the No Access list of the record and of every secure parent (app-only reads; walled
/// or unverifiable refuses with nothing written), and after the caller's PATCH that record is secured in the same call
/// through <see cref="Sprk.Bff.Api.Services.Access.SecureRootFilingGate"/> — provisioning's own app-only steps, for the
/// person who created the record (never the caller's choice of anyone else); nothing of the caller's own write runs
/// app-only.
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
    private readonly Sprk.Bff.Api.Services.Dataverse.CoreAncestorAfterWriteRestamp _restamp;
    private readonly ILogger<DataverseUpdateRecordHandler> _logger;
    private readonly IServiceScopeFactory? _scopes;
    private readonly Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver _ownership;
    private readonly Sprk.Bff.Api.Services.Access.SecureRootFilingGate _rootFiling;

    /// <param name="scopes">Task 142 (L1): the scope the Assigned-To materializer is resolved from after an update that
    /// wrote a root's "Assigned *" column. Optional for the same reason as on <c>DataverseCreateRecordHandler</c>.</param>
    public DataverseUpdateRecordHandler(
        IDataverseUserClient dataverse,
        Sprk.Bff.Api.Services.Dataverse.CoreAncestorAfterWriteRestamp restamp,
        ILogger<DataverseUpdateRecordHandler> logger,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        Sprk.Bff.Api.Services.Access.SecureRootFilingGate rootFiling,
        IServiceScopeFactory? scopes = null)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        // Owner round 8 item 1: unconditionally registered beside the restamper (AddCoreAncestorResolver, which
        // AddToolFramework also calls), so this handler's registration gains no asymmetric dependency (§10 F.1).
        _restamp = restamp ?? throw new ArgumentNullException(nameof(restamp));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // Task 146 r2: unconditionally registered (MetadataServiceExtensions) — no asymmetric registration (§10 F.1).
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        // Task 158 (owner round 6): a work assignment or project re-filed under a secure record is secured in the same
        // call. Registered beside the restamper (AddCoreAncestorResolver, which AddToolFramework calls) — §10 F.1.
        _rootFiling = rootFiling ?? throw new ArgumentNullException(nameof(rootFiling));
        _scopes = scopes;
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

            // Task 158 (owner round 6): re-filing a work assignment or project — whether the record it will be filed under
            // is secure must be readable, or nothing is written (an unreadable flag is never "not secure").
            if (await _rootFiling.CheckAsync(tablename, recordId, OwnedChildWrite.WritesOf(mapped.Item!), cancellationToken)
                    .ConfigureAwait(false) is { } rootRefusal)
            {
                return LogOutcome(context, tablename, recordId,
                    Error(tool, $"The update was NOT written: {rootRefusal.Reason} ({rootRefusal.RefusalCode}).",
                        rootRefusal.RefusalCode ?? ToolErrorCodes.ValidationFailed, startedAt),
                    stopwatch);
            }

            // Task 147 r1c: a write that may move a CHILD row into or out of a secure record reads, BEFORE it, whether the
            // row is isolated — so a move OUT also takes the secure record's mirrored shares off (owner round 22) and
            // releases what is filed under it, exactly as the browser re-file routes do. One implementation:
            // SecureChildReconciler.AfterRefileAsync (round 36).
            using var shareScope = MayRefile(tablename, mapped.Item!) ? _scopes?.CreateScope() : null;
            var children = shareScope?.ServiceProvider.GetService<Sprk.Bff.Api.Services.Access.SecureChildReconciler>();
            Exception? isolationReadFault = null;
            var isolatedBefore = false;
            if (children is not null)
            {
                try
                {
                    isolatedBefore = await children.IsSecureTeamOwnedAsync(tablename, recordId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    isolationReadFault = ex;
                }
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

            // Task 156, owner round 8 item 1 (§6.5 path B — see the class remarks): the copies this write moved are
            // re-stamped NOW, in the same operation, after the caller's own update succeeded. A write that can move no
            // stamp reads nothing. Never thrown: a child that fails is logged and the reconciliation job repairs it; the
            // caller's update stands either way.
            var restamp = await _restamp
                .AfterWriteAsync(tablename, recordId, mapped.Item!.Columns)
                .ConfigureAwait(false);
            if (!restamp.Complete)
            {
                _logger.LogWarning(
                    "[dataverse.update_record] the core-ancestor re-stamp after the update of {Entity} {RecordId} did not finish "
                    + "(failures={Failures} truncated={Truncated}); the reconciliation job completes it within one cycle",
                    tablename, recordId, restamp.Failures.Count, restamp.Truncated);
            }

            // Task 147 r1c (round 36), AFTER the re-stamp (the stamps on the rows filed under it are lookups the ownership rule
            // reads): the re-filed row's mirror and task 148's pass over everything filed under it.
            if (refile.Written && children is not null)
            {
                await children.AfterRefileAsync(
                    tablename, recordId,
                    () => isolationReadFault is null ? Task.FromResult(isolatedBefore) : Task.FromException<bool>(isolationReadFault),
                    CancellationToken.None).ConfigureAwait(false);
            }

            // Task 158 (owner round 6; §6.5 path B — see the class remarks): a work assignment or project the caller's own
            // update filed under a secure matter or project is secured NOW, through provisioning's own steps, for the person
            // who created it. Never thrown; an incomplete securing is reported, not hidden.
            var secured = await _rootFiling
                .SecureAfterWriteAsync(tablename, recordId, mapped.Item!.Columns, context.DecisionId.ToString("N"))
                .ConfigureAwait(false);

            // Task 142 (L1, owner Q5 + A4): an update that wrote a root's "Assigned *" column grants the new subject and
            // removes the previous one's unmodified auto access now. After the PATCH committed; never throws, never fails
            // this update (a non-root or a non-registry column is a no-op).
            await Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer.RunAfterWriteAsync(
                _scopes, tablename, recordId, mapped.Item!.Columns, grantorOid: null, _logger, cancellationToken)
                .ConfigureAwait(false);

            if (secured is { IsComplete: false })
            {
                return LogOutcome(context, tablename, recordId,
                    Error(tool,
                        $"The update was written, but record {recordId:D} is now filed under a secure record and could not be made " +
                        $"secure yet ({secured.ReasonCode}). " +
                        (secured.CompletesAutomatically
                            ? "It is retried automatically within a few minutes."
                            : "Retrying will not change that on its own, so an administrator needs to review it."),
                        secured.ReasonCode ?? ToolErrorCodes.InternalError, startedAt),
                    stopwatch);
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

    /// <summary>
    /// Task 146 r2 (verifier item 2). The re-file itself is <see cref="OwnedChildWrite.RefileAsync"/> — since task 147 r1
    /// the ONE re-file core, shared with the browser re-file routes (owner round 28 item 1). This maps its outcome onto a
    /// tool result with the messages the tool has always returned.
    /// </summary>
    private async Task<RefileStep> RefileIfFiledAsync(
        AnalysisTool tool, string tablename, Guid recordId, string entitySetName, string? primaryIdAttribute,
        DataverseWriteItemMapper.MappedItem item, ChatInvocationContext context, DateTimeOffset startedAt, CancellationToken ct)
    {
        var callerOid = Guid.TryParse(context.UserId, out var oid) && oid != Guid.Empty ? oid : (Guid?)null;
        var outcome = await OwnedChildWrite.RefileAsync(
            _dataverse, _ownership, tablename, recordId, entitySetName, primaryIdAttribute, item, callerOid, ct)
            .ConfigureAwait(false);

        return outcome switch
        {
            { NotARefile: true } => default,
            { Written: true } => new RefileStep(null, true),
            { ClientFailure: { } failure } => new RefileStep(MapClientError(tool, failure, startedAt), false),
            { Denied: { } denied } => new RefileStep(Error(tool, denied, DataverseUserClientErrorCodes.AccessDenied, startedAt), false),
            { SecureFilingRefused: { } owner } => new RefileStep(Error(tool,
                $"A '{tablename}' record cannot be filed under a secure record from chat; the update was NOT written" +
                (owner.IsRefused ? $" ({owner.RefusalCode})." : ".") + " Change it from the record itself.",
                owner.RefusalCode ?? ToolErrorCodes.ValidationFailed, startedAt), false),
            // F3 (owner round 10 item 7): the caller may not move the row out of a secure root — the unsecure endpoint's
            // message and reason code, not an owner refusal.
            { Forbidden: { } forbidden } => new RefileStep(Error(tool,
                $"The update was NOT written. {forbidden.Reason}",
                forbidden.RefusalCode ?? DataverseUserClientErrorCodes.AccessDenied, startedAt), false),
            { OwnerRefusal: { } refused } => new RefileStep(Error(tool,
                $"The update was NOT written: the record's owner could not be decided — {refused.Reason} ({refused.RefusalCode}).",
                refused.RefusalCode ?? ToolErrorCodes.ValidationFailed, startedAt), false),
            _ => throw new InvalidOperationException("A re-file outcome with no decision."),
        };
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Task 147 r1c: whether the update could be a RE-FILE of a child row (a set or cleared lookup on a reparentable child
    /// table) — the case <see cref="OwnedChildWrite.RefileAsync"/> decides, and the only one worth the pre-write isolation read.
    /// </summary>
    private static bool MayRefile(string tablename, DataverseWriteItemMapper.MappedItem item) =>
        Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver.IsReparentableChild(tablename)
        && (item.Lookups.Any(l => Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver.IsOwnershipParent(l.RelatedTable))
            || item.ClearedColumns.Count > 0);

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
