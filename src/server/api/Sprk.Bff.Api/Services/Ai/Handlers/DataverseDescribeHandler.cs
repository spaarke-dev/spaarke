using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;

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
/// reachable from this class (task-012 audit).
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
/// </remarks>
public sealed partial class DataverseDescribeHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(DataverseDescribeHandler);

    /// <summary>Client-side cap on the number of tables returned for the <c>tables/</c> listing.</summary>
    internal const int MaxTableListEntries = 300;

    [GeneratedRegex(@"^[a-z][a-z0-9_]*$")]
    private static partial Regex LogicalNameRegex();

    private readonly IDataverseUserClient _dataverse;
    private readonly ILogger<DataverseDescribeHandler> _logger;

    public DataverseDescribeHandler(
        IDataverseUserClient dataverse,
        ILogger<DataverseDescribeHandler> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Dataverse Describe",
        Description: "Describes Dataverse structure and records under the calling user's permissions. " +
                     "Path forms (GA Dataverse MCP contract): 'tables/' lists tables; 'tables/{logicalName}' " +
                     "returns the table schema (columns, types); 'tables/{logicalName}/records/{guid}' returns " +
                     "one full record. Read-only; results respect the user's Dataverse security roles and " +
                     "row-level security.",
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
            return MapClientError(tool, recordResponse, startedAt);
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
