using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat-side typed handler for the <c>dataverse.describe</c> tool — table catalog, table
/// schema, and single-record description over the user-OBO Dataverse Web API
/// (spaarke-ai-architecture-redesign-r1 task 008, FR-P0-07 read half).
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-039 contract freeze</b>: name + argument shape mirror the GA Dataverse MCP
/// <c>describe</c> tool — <c>describe(path, scope?)</c> with filesystem-style paths
/// (<c>tables/</c>, <c>tables/{table}</c>, <c>tables/{table}/records/{guid}</c>). See
/// <see cref="DataverseToolNames"/> for the frozen-name citation. GA path segments this
/// native transport does NOT serve (<c>skills/…</c>, <c>scopes/…</c> — Business Skills are an
/// MCP-server feature) return a structured error naming the limitation; a future transport
/// swap (task-010 spike) gains them without a contract change.
/// </para>
/// <para>
/// <b>User-OBO ONLY (spec MUST rule)</b>: all Dataverse access goes through
/// <see cref="IDataverseUserClient"/> — the caller's identity, exchanged OBO; metadata and
/// record visibility are whatever Dataverse grants THAT user. No app-only client is
/// reachable from this class (task-012 audit), except the one below.
/// </para>
/// <para>
/// <b>The "User-OBO ONLY" rule is AMENDED for ONE dependency — unified-access-control-r2 task 188, main-session decision
/// 2026-10-20 (CLAUDE.md §6.5 path B; APPROVED, owner round 111, 2026-10-21)</b>: an EXTERNAL caller's <c>sprk_document</c> rows
/// are decided by THE external document narrowing through <see cref="AppOnlyDocumentNarrowing"/>, which re-reads those
/// documents BY ID under the BFF's app-only identity (the external plane's reader) and returns ONLY a hide decision: a
/// link field security masks for the user can never read as "no link". It can only HIDE a row the user's own read returned;
/// no app-only data reaches the result, and nothing else of the app-only reader is reachable (a delegate). Every query whose
/// rows reach the model still runs as the user.
/// </para>
/// <para>
/// <b>Registration</b>: <c>sprk_analysistool</c> row
/// (<c>infra/dataverse/sprk_analysistool-dataverse-describe-row.json</c>) with
/// <c>sprk_toolid = dataverse.describe</c>, <c>sprk_namespace = dataverse</c>,
/// <c>sprk_sideeffectclass = Read</c>, <c>sprk_permissionscope = dataverse-user-context</c>.
/// Auto-discovered via <c>ToolFrameworkExtensions.AddToolHandlersFromAssembly</c> (ADR-010 —
/// zero manual DI lines for the handler itself).
/// </para>
/// <para>
/// <b>ADR-015 / NFR-07</b>: telemetry carries the path KIND (table_list / table_schema /
/// record), entity logical name, status + counts + duration — never attribute values or row
/// content.
/// </para>
/// <para>
/// <b>ISS-010 quick fix (external-access r3 task 078)</b>: on the RECORD path, an EXTERNAL caller (systemuser flagged
/// <c>sprk_isexternal</c>, or unresolvable — fail closed) is refused any communication-family record but a
/// <c>sprk_communication</c>, before any Dataverse request; a <c>sprk_communication</c> record they may not see under the
/// task 065 rule, or a <c>sprk_document</c> whose related communication they may not see, answers with the same code and
/// message as a missing one (a guarded not-found is normalised to that answer). Table list and schema paths carry no
/// row content and are unchanged. Task 188 slice C (F-C1, OD-C1): an <c>sprk_analysis</c> whose source document or output
/// file is hidden or undecidable, and an <c>sprk_analysisoutput</c> whose analysis is, answer the same way.
/// See <see cref="ExternalCallerCommunicationGuard"/>. Internal callers are unchanged.
/// </para>
/// </remarks>
public sealed partial class DataverseDescribeHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(DataverseDescribeHandler);

    /// <summary>Client-side cap on the number of tables returned for the <c>tables/</c> listing.</summary>
    internal const int MaxTableListEntries = 300;

    [GeneratedRegex(@"^[a-z][a-z0-9_]*\z")]
    private static partial Regex LogicalNameRegex();

    private readonly IDataverseUserClient _dataverse;
    private readonly ILogger<DataverseDescribeHandler> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICallerSystemUserResolver _callerResolver;
    private readonly ISystemUserIdentityResolver _identityResolver;
    /// <summary>Task 188: THE document narrowing, app-only by id (see the remarks: the one amended dependency).</summary>
    private readonly AppOnlyDocumentNarrowing _narrowDocuments;

    public DataverseDescribeHandler(
        IDataverseUserClient dataverse,
        ILogger<DataverseDescribeHandler> logger,
        IHttpContextAccessor httpContextAccessor,
        ICallerSystemUserResolver callerResolver,
        ISystemUserIdentityResolver identityResolver,
        AppOnlyDocumentNarrowing narrowDocuments)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _callerResolver = callerResolver ?? throw new ArgumentNullException(nameof(callerResolver));
        _identityResolver = identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
        _narrowDocuments = narrowDocuments ?? throw new ArgumentNullException(nameof(narrowDocuments));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Dataverse Describe",
        // FR-A-01 (AIR2-020): mirror of the authored sprk_description in infra/dataverse/sprk_analysistool-dataverse-describe-row.json — keep byte-equal; edit the JSON, not this literal.
        Description: @"Describes Dataverse structure and records under the calling user's permissions (read-only). Path forms: 'tables/' lists all tables; 'tables/{logicalName}' returns the table schema with column logical names and types; 'tables/{logicalName}/records/{guid}' returns one full record. Use this FIRST to discover table and column logical names before calling dataverse.read_query, and to read full record details for paths returned by dataverse.search_data. Results respect the current user's Dataverse security roles and row-level security. Spaarke entity map (use these logical names): 'matter'/'case'/'deal' = sprk_matter (key columns sprk_mattername, sprk_matternumber, sprk_matterdescription, statuscode; lookups sprk_practicearea, sprk_mattertype, sprk_assignedattorney1 to contact, sprk_externalaccount to account). 'project'/'workstream' = sprk_project (sprk_projectname, sprk_projectnumber). 'document'/'contract'/'file' = sprk_document (sprk_documentname, sprk_filename, sprk_documenttype, sprk_filesummary; lookups sprk_matter, sprk_project). People = contact; client companies = account; law firms/vendors = sprk_organization.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition(
                "path",
                "Filesystem-style describe path: 'tables/', 'tables/{logicalName}', or 'tables/{logicalName}/records/{guid}'. " +
                "(GA MCP also serves 'skills/' and 'scopes/' — not available on the native transport.)",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "scope",
                "Optional scope filter (GA MCP compatibility). The native transport does not implement MCP scopes; " +
                "when supplied it is acknowledged with a warning and the unfiltered result is returned.",
                ToolParameterType.String,
                Required: false)
        });

    /// <inheritdoc />
    public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

    /// <inheritdoc />
    public InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Chat;

    /// <inheritdoc />
    public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool) =>
        ToolValidationResult.Failure(
            "DataverseDescribeHandler is chat-context-only (agent-loop tool). Playbook-context invocation is unsupported.");

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, AnalysisTool tool, CancellationToken cancellationToken) =>
        Task.FromResult(ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "DataverseDescribeHandler is chat-context-only (agent-loop tool). Playbook-context invocation is unsupported.",
            ToolErrorCodes.ValidationFailed));

    /// <inheritdoc />
    public ToolValidationResult ValidateChat(ChatInvocationContext context, AnalysisTool tool)
    {
        if (string.IsNullOrWhiteSpace(context.TenantId))
            return ToolValidationResult.Failure("TenantId is required.");

        if (!TryParseArgs(context.ToolArgumentsJson, out _, out _, out var error))
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

        if (!TryParseArgs(context.ToolArgumentsJson, out var path, out var scope, out var parseError))
        {
            return Error(tool, parseError!, ToolErrorCodes.ValidationFailed, startedAt);
        }

        var warnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(scope))
        {
            warnings.Add("The 'scope' argument is accepted for GA MCP contract compatibility but the native transport does not implement MCP scopes; the result is unfiltered.");
        }

        try
        {
            var segments = path.Trim().Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0 || !segments[0].Equals("tables", StringComparison.OrdinalIgnoreCase))
            {
                return Error(tool,
                    $"Path '{path}' is not served by the native transport. Supported: 'tables/', 'tables/{{logicalName}}', " +
                    "'tables/{logicalName}/records/{guid}'. ('skills/' and 'scopes/' exist only on the MCP transport.)",
                    ToolErrorCodes.ValidationFailed, startedAt);
            }

            ToolResult result;
            string kind;
            switch (segments.Length)
            {
                case 1:
                    kind = "table_list";
                    result = await DescribeTableListAsync(tool, path, warnings, startedAt, cancellationToken).ConfigureAwait(false);
                    break;
                case 2:
                    kind = "table_schema";
                    result = await DescribeTableSchemaAsync(tool, path, segments[1], warnings, startedAt, cancellationToken).ConfigureAwait(false);
                    break;
                case 4 when segments[2].Equals("records", StringComparison.OrdinalIgnoreCase):
                    kind = "record";
                    result = await DescribeRecordAsync(tool, path, segments[1], segments[3], warnings, startedAt, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    return Error(tool,
                        $"Path '{path}' has an unrecognized shape. Supported: 'tables/', 'tables/{{logicalName}}', 'tables/{{logicalName}}/records/{{guid}}'.",
                        ToolErrorCodes.ValidationFailed, startedAt);
            }

            stopwatch.Stop();
            // ADR-015 / NFR-07: kind + outcome + duration + deterministic IDs only.
            _logger.LogInformation(
                "[dataverse.describe][ADR-015] kind={Kind} outcome={Outcome} decisionId={DecisionId} durationMs={DurationMs}",
                kind, result.Success ? "ok" : result.ErrorCode, context.DecisionId, stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Error(tool, "dataverse.describe was cancelled.", ToolErrorCodes.Cancelled, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[dataverse.describe] failed decisionId={DecisionId}: {ErrorType}",
                context.DecisionId, ex.GetType().Name);
            return Error(tool, "dataverse.describe failed unexpectedly.", ToolErrorCodes.InternalError, startedAt);
        }
    }

    // ── tables/ ───────────────────────────────────────────────────────────────

    private async Task<ToolResult> DescribeTableListAsync(
        AnalysisTool tool, string path, List<string> warnings, DateTimeOffset startedAt, CancellationToken ct)
    {
        var response = await _dataverse.GetAsync(
            "EntityDefinitions?$select=LogicalName,EntitySetName,DisplayName,Description,IsPrivate",
            ct).ConfigureAwait(false);

        if (!response.IsSuccess)
        {
            return MapClientError(tool, response, startedAt);
        }

        var tables = new List<object>();
        if (response.Body is { } body && body.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
        {
            foreach (var entity in value.EnumerateArray())
            {
                if (entity.TryGetProperty("IsPrivate", out var isPrivate) && isPrivate.ValueKind == JsonValueKind.True)
                    continue;

                var logicalName = GetString(entity, "LogicalName");
                if (logicalName is null) continue;

                tables.Add(new
                {
                    path = DataverseRecordCitations.TablePath(logicalName),
                    logicalName,
                    entitySetName = GetString(entity, "EntitySetName"),
                    displayName = GetLocalizedLabel(entity, "DisplayName"),
                    description = GetLocalizedLabel(entity, "Description")
                });

                if (tables.Count >= MaxTableListEntries)
                {
                    warnings.Add($"Table list truncated to {MaxTableListEntries} entries. Use dataverse.describe with 'tables/{{logicalName}}' for a specific table.");
                    break;
                }
            }
        }

        return ToolResult.Ok(
            HandlerId, tool.Id, tool.Name,
            data: new { tool = DataverseToolNames.Describe, path, kind = "table_list", tables, count = tables.Count },
            summary: $"Listed {tables.Count} Dataverse tables visible to the current user.",
            confidence: 1.0,
            execution: Timed(startedAt),
            warnings: warnings);
    }

    // ── tables/{logicalName} ──────────────────────────────────────────────────

    private async Task<ToolResult> DescribeTableSchemaAsync(
        AnalysisTool tool, string path, string logicalName, List<string> warnings, DateTimeOffset startedAt, CancellationToken ct)
    {
        logicalName = logicalName.ToLowerInvariant();
        if (!LogicalNameRegex().IsMatch(logicalName))
        {
            return Error(tool, $"'{logicalName}' is not a valid table logical name.", ToolErrorCodes.ValidationFailed, startedAt);
        }

        var response = await _dataverse.GetAsync(
            $"EntityDefinitions(LogicalName='{logicalName}')?$select=LogicalName,EntitySetName,DisplayName,Description,PrimaryIdAttribute,PrimaryNameAttribute" +
            "&$expand=Attributes($select=LogicalName,AttributeType,DisplayName,Description,IsPrimaryId,IsPrimaryName,RequiredLevel)",
            ct).ConfigureAwait(false);

        if (!response.IsSuccess)
        {
            return MapClientError(tool, response, startedAt);
        }

        var body = response.Body!.Value;
        var columns = new List<object>();
        if (body.TryGetProperty("Attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Array)
        {
            foreach (var attr in attrs.EnumerateArray())
            {
                var colName = GetString(attr, "LogicalName");
                if (colName is null) continue;
                columns.Add(new
                {
                    logicalName = colName,
                    type = GetString(attr, "AttributeType"),
                    displayName = GetLocalizedLabel(attr, "DisplayName"),
                    description = GetLocalizedLabel(attr, "Description"),
                    isPrimaryId = attr.TryGetProperty("IsPrimaryId", out var pid) && pid.ValueKind == JsonValueKind.True,
                    isPrimaryName = attr.TryGetProperty("IsPrimaryName", out var pn) && pn.ValueKind == JsonValueKind.True
                });
            }
        }

        return ToolResult.Ok(
            HandlerId, tool.Id, tool.Name,
            data: new
            {
                tool = DataverseToolNames.Describe,
                path,
                kind = "table_schema",
                table = new
                {
                    logicalName = GetString(body, "LogicalName"),
                    entitySetName = GetString(body, "EntitySetName"),
                    displayName = GetLocalizedLabel(body, "DisplayName"),
                    description = GetLocalizedLabel(body, "Description"),
                    primaryIdAttribute = GetString(body, "PrimaryIdAttribute"),
                    primaryNameAttribute = GetString(body, "PrimaryNameAttribute")
                },
                columns,
                columnCount = columns.Count
            },
            summary: $"Described table '{logicalName}' ({columns.Count} columns).",
            confidence: 1.0,
            execution: Timed(startedAt),
            warnings: warnings);
    }

    // ── tables/{logicalName}/records/{guid} ───────────────────────────────────

    private async Task<ToolResult> DescribeRecordAsync(
        AnalysisTool tool, string path, string logicalName, string recordIdRaw, List<string> warnings, DateTimeOffset startedAt, CancellationToken ct)
    {
        logicalName = logicalName.ToLowerInvariant();
        if (!LogicalNameRegex().IsMatch(logicalName))
        {
            return Error(tool, $"'{logicalName}' is not a valid table logical name.", ToolErrorCodes.ValidationFailed, startedAt);
        }
        if (!Guid.TryParse(recordIdRaw, out var recordId))
        {
            return Error(tool, $"'{recordIdRaw}' is not a valid record GUID.", ToolErrorCodes.ValidationFailed, startedAt);
        }

        // ISS-010 (task 078): externality is decided ONLY for a communication-family, sprk_document or document-derived
        // (task 188 slice C) record, so every other request is byte-identical. An external caller is refused any family
        // table but sprk_communication BEFORE any Dataverse request (the metadata GET included); a sprk_document is checked
        // by its related communication (review round 1, F1).
        var guardCommunication = false;
        var guardDocument = false;
        var guardDerived = false;
        if ((ExternalCallerCommunicationGuard.IsCommunicationFamily(logicalName) ||
             ExternalCallerCommunicationGuard.IsDocumentTable(logicalName) ||
             ExternalCallerCommunicationGuard.IsDocumentDerivedTable(logicalName)) &&
            await ExternalCallerCommunicationGuard.IsExternalCallerAsync(
                _httpContextAccessor, _callerResolver, _identityResolver, ct).ConfigureAwait(false))
        {
            if (ExternalCallerCommunicationGuard.IsDocumentTable(logicalName))
            {
                guardDocument = true;
            }
            else if (ExternalCallerCommunicationGuard.IsDocumentDerivedTable(logicalName))
            {
                // Task 188 slice C (F-C1): an analysis or analysis output follows the documents it derives from.
                guardDerived = true;
            }
            else if (!ExternalCallerCommunicationGuard.IsCommunicationTable(logicalName))
            {
                _logger.LogInformation(
                    "[dataverse.describe] " + ExternalCallerCommunicationGuard.LogEvent + " entity={Entity} reason={Reason}",
                    logicalName, "family_table_refused");
                return Error(tool, ExternalCallerCommunicationGuard.RefusalMessage,
                    DataverseUserClientErrorCodes.AccessDenied, startedAt);
            }
            else
            {
                guardCommunication = true;
            }
        }

        // Resolve the entity-set name from metadata (logical name ≠ OData collection name).
        var metaResponse = await _dataverse.GetAsync(
            $"EntityDefinitions(LogicalName='{logicalName}')?$select=EntitySetName,PrimaryIdAttribute",
            ct).ConfigureAwait(false);
        if (!metaResponse.IsSuccess)
        {
            return MapClientError(tool, metaResponse, startedAt);
        }
        var entitySetName = GetString(metaResponse.Body!.Value, "EntitySetName");
        if (entitySetName is null)
        {
            return Error(tool, $"Table '{logicalName}' has no entity-set name.", ToolErrorCodes.InternalError, startedAt);
        }

        // Row-level security: Dataverse itself decides whether THIS user can read the record.
        var recordResponse = await _dataverse.GetAsync($"{entitySetName}({recordId:D})", ct).ConfigureAwait(false);
        if (!recordResponse.IsSuccess)
        {
            // ISS-010 review K4: on a guarded path a real not-found answers with the SAME code and message as a hidden
            // record below, so the answer never tells an external caller that a hidden record exists.
            if ((guardCommunication || guardDocument || guardDerived) &&
                recordResponse.ErrorCode == DataverseUserClientErrorCodes.NotFound)
            {
                return RecordNotVisible(tool, startedAt);
            }
            return MapClientError(tool, recordResponse, startedAt);
        }

        // ISS-010 (task 078): the task 065 rule on the one record — flag explicitly false AND a thread that reads Open
        // now; for a document, THE external document predicate (task 188). A hidden record answers with
        // the same code and message as a guarded not-found above; a read fault throws (no data).
        if (guardCommunication || guardDocument || guardDerived)
        {
            var record = recordResponse.Body!.Value;
            bool visible;
            if (guardCommunication)
            {
                var threadId = ExternalCallerCommunicationGuard.ThreadIdOf(record);
                visible = ExternalCallerCommunicationGuard.IsExplicitlyShareable(record) && threadId != Guid.Empty &&
                    (await CommunicationOpenThreadGate.ReadOpenThreadIdsAsync(
                        new[] { threadId }, ExternalCallerCommunicationGuard.ThreadQueryOver(_dataverse), ct).ConfigureAwait(false))
                    .Contains(threadId);
            }
            else if (guardDerived)
            {
                // Task 188 slice C (F-C1, OD-C1): an analysis is hidden when its source document OR its output file is
                // hidden or undecidable; an output follows its analysis. The links are read as the user; only the hide
                // decision on the documents runs app-only (the hide-only overload). A read fault throws (no data).
                var sources = await ExternalCallerCommunicationGuard.ReadSourceDocumentIdsAsync(
                    _dataverse, logicalName, new[] { recordId }, ct).ConfigureAwait(false);
                var hidden = await _narrowDocuments.HiddenDocumentIdsAsync(
                    sources.Values.SelectMany(documents => documents), ct).ConfigureAwait(false);
                visible = !ExternalCallerCommunicationGuard.IsDerivedRowHidden(recordId, sources, hidden, out _);
            }
            else
            {
                // Task 188 (D7, #2126): THE external document predicate (own or ancestor Restricted; the related
                // communication of the document or, on a bounded parent walk, of its ancestor, under the 065 rule), on the
                // document re-read by id APP-ONLY (main-session decision 2026-10-20), never on the record the user read.
                visible = (await _narrowDocuments(new[] { recordId }, ct).ConfigureAwait(false)).IsVisible(recordId);
            }

            if (!visible)
            {
                _logger.LogInformation(
                    "[dataverse.describe] " + ExternalCallerCommunicationGuard.LogEvent + " entity={Entity} reason={Reason}",
                    logicalName, "record_hidden");
                return RecordNotVisible(tool, startedAt);
            }
        }

        // Strip OData annotations; keep business columns.
        var fields = new Dictionary<string, object?>();
        foreach (var property in recordResponse.Body!.Value.EnumerateObject())
        {
            if (property.Name.StartsWith("@odata", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("@OData.Community", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            fields[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => property.Value.Clone()
            };
        }

        return ToolResult.Ok(
            HandlerId, tool.Id, tool.Name,
            data: new
            {
                tool = DataverseToolNames.Describe,
                path = DataverseRecordCitations.RecordPath(logicalName, recordId),
                kind = "record",
                entity = logicalName,
                recordId = recordId.ToString("D"),
                fields,
                citations = new[] { new { path = DataverseRecordCitations.RecordPath(logicalName, recordId), entity = logicalName, recordId = recordId.ToString("D") } }
            },
            summary: $"Described record {recordId:D} from '{logicalName}' ({fields.Count} fields, read under the calling user's permissions).",
            confidence: 1.0,
            execution: Timed(startedAt),
            warnings: warnings) with
        {
            Metadata = new Dictionary<string, object?>
            {
                [ToolResultMetadataKeys.Citations] = new[] { DataverseRecordCitations.ForRecord(logicalName, recordId) }
            }
        };
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private ToolResult MapClientError(AnalysisTool tool, DataverseUserResponse response, DateTimeOffset startedAt) =>
        ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            response.ErrorMessage ?? "Dataverse request failed.",
            response.ErrorCode,
            Timed(startedAt));

    private ToolResult Error(AnalysisTool tool, string message, string code, DateTimeOffset startedAt) =>
        ToolResult.Error(HandlerId, tool.Id, tool.Name, message, code, Timed(startedAt));

    /// <summary>ISS-010 review K4: the one answer for a hidden record and for a guarded not-found.</summary>
    private ToolResult RecordNotVisible(AnalysisTool tool, DateTimeOffset startedAt) =>
        Error(tool, ExternalCallerCommunicationGuard.RecordNotVisibleMessage, DataverseUserClientErrorCodes.NotFound, startedAt);

    private static ToolExecutionMetadata Timed(DateTimeOffset startedAt) =>
        new() { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow };

    internal static bool TryParseArgs(string? argsJson, out string path, out string? scope, out string? error)
    {
        path = string.Empty;
        scope = null;
        error = null;

        if (string.IsNullOrWhiteSpace(argsJson))
        {
            error = "Tool arguments JSON is required (expected { \"path\": \"tables/…\" }).";
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
            if (!doc.RootElement.TryGetProperty("path", out var pathProp) ||
                pathProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(pathProp.GetString()))
            {
                error = "Tool arguments must include a non-empty 'path' string (e.g. 'tables/', 'tables/account').";
                return false;
            }
            path = pathProp.GetString()!;

            if (doc.RootElement.TryGetProperty("scope", out var scopeProp) && scopeProp.ValueKind == JsonValueKind.String)
            {
                scope = scopeProp.GetString();
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

    /// <summary>Reads a Dataverse metadata Label (<c>{"UserLocalizedLabel":{"Label":"…"}}</c>).</summary>
    private static string? GetLocalizedLabel(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var label) || label.ValueKind != JsonValueKind.Object)
            return null;
        if (label.TryGetProperty("UserLocalizedLabel", out var ull) && ull.ValueKind == JsonValueKind.Object &&
            ull.TryGetProperty("Label", out var text) && text.ValueKind == JsonValueKind.String)
        {
            return text.GetString();
        }
        return null;
    }
}
