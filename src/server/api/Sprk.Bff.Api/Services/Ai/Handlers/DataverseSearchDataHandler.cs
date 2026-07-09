using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.Handlers;

/// <summary>
/// Chat-side typed handler for the <c>dataverse.search_data</c> tool — keyword search over
/// Dataverse records via the Dataverse Search API under the user's OBO token
/// (spaarke-ai-architecture-redesign-r1 task 008, FR-P0-07 read half).
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-039 contract freeze</b>: name + argument shape mirror the GA Dataverse MCP
/// <c>search_data</c> tool — <c>search_data(query, scope)</c> returning filesystem-style
/// record paths (<c>tables/{table}/records/{guid}</c>). See <see cref="DataverseToolNames"/>
/// for the frozen-name citation.
/// </para>
/// <para>
/// <b>Documented deviation (native transport)</b>: GA MCP requires <c>scope</c> (an MCP scope
/// name discovered via <c>describe('scopes/')</c>). The native transport has no MCP scope
/// registry, so <c>scope</c> is OPTIONAL here and doubles as a comma-separated list of table
/// logical names to restrict the search (e.g. <c>"account,contact"</c>); when omitted, all
/// search-enabled tables are searched. The property NAME and position are frozen, so a future
/// MCP transport swap only tightens the schema (a Dataverse data edit, not a code change).
/// </para>
/// <para>
/// <b>User-OBO ONLY (spec MUST rule)</b>: the search executes through
/// <see cref="IDataverseUserClient"/> — Dataverse Search applies the calling user's security
/// roles + row-level security to results. No app-only client is reachable (task-012 audit).
/// </para>
/// <para>
/// <b>ADR-015 / NFR-07</b>: telemetry carries query LENGTH + entity filter + result count +
/// duration — never the query text (user-authored) and never result content.
/// </para>
/// </remarks>
public sealed partial class DataverseSearchDataHandler : IToolHandler
{
    private const string HandlerIdValue = nameof(DataverseSearchDataHandler);

    /// <summary>Result cap sent to the Dataverse Search API (ADR-016 quota posture).</summary>
    internal const int SearchTop = 25;

    [GeneratedRegex(@"^[a-z][a-z0-9_]*$")]
    private static partial Regex LogicalNameRegex();

    private readonly IDataverseUserClient _dataverse;
    private readonly ILogger<DataverseSearchDataHandler> _logger;

    public DataverseSearchDataHandler(
        IDataverseUserClient dataverse,
        ILogger<DataverseSearchDataHandler> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string HandlerId => HandlerIdValue;

    /// <inheritdoc />
    public ToolHandlerMetadata Metadata { get; } = new(
        Name: "Dataverse Search Data",
        // FR-A-01 (AIR2-020): mirror of the authored sprk_description in infra/dataverse/sprk_analysistool-dataverse-search-data-row.json — keep byte-equal; edit the JSON, not this literal.
        Description: @"Searches Dataverse records for keywords under the calling user's permissions and returns matching records as citable paths (tables/{table}/records/{guid}) with relevance scores. Use this when the user asks to find records by name, keyword, or phrase and the exact table or column is unknown. For structured filtering on known columns use dataverse.read_query instead. Use dataverse.describe on a returned path to read the full record. Results respect the current user's Dataverse security roles and row-level security. Spaarke entity map (use these logical names): 'matter'/'case'/'deal' = sprk_matter (key columns sprk_mattername, sprk_matternumber, sprk_matterdescription, statuscode; lookups sprk_practicearea, sprk_mattertype, sprk_assignedattorney1 to contact, sprk_externalaccount to account). 'project'/'workstream' = sprk_project (sprk_projectname, sprk_projectnumber). 'document'/'contract'/'file' = sprk_document (sprk_documentname, sprk_filename, sprk_documenttype, sprk_filesummary; lookups sprk_matter, sprk_project). People = contact; client companies = account; law firms/vendors = sprk_organization.",
        Version: "1.0.0",
        SupportedInputTypes: new[] { "text/plain" },
        Parameters: new[]
        {
            new ToolParameterDefinition(
                "query",
                "Search keywords or phrase.",
                ToolParameterType.String,
                Required: true),
            new ToolParameterDefinition(
                "scope",
                "Optional search scope (GA MCP contract compatibility). On the native transport: a comma-separated " +
                "list of table logical names to restrict the search (e.g. 'sprk_matter,account'; common short names " +
                "like 'matter'/'project'/'invoice' are accepted and normalized). Omit to search all " +
                "search-enabled tables.",
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
            "DataverseSearchDataHandler is chat-context-only (agent-loop tool). Playbook-context invocation is unsupported.");

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(ToolExecutionContext context, AnalysisTool tool, CancellationToken cancellationToken) =>
        Task.FromResult(ToolResult.Error(
            HandlerId, tool.Id, tool.Name,
            "DataverseSearchDataHandler is chat-context-only (agent-loop tool). Playbook-context invocation is unsupported.",
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

        if (!TryParseArgs(context.ToolArgumentsJson, out var query, out var scope, out var parseError))
        {
            return Error(tool, parseError!, ToolErrorCodes.ValidationFailed, startedAt);
        }

        // Parse scope into a table logical-name filter (native-transport interpretation).
        // G-P3 UAT round-1 H8 (2026-07-07): the model frequently passes CANONICAL entity
        // names ("matter", "projects") instead of Dataverse logical names — those pass the
        // shape regex, reach the Search API as entities:["matter"], and the API rejects the
        // WHOLE query, which surfaced to the operator as "a technical issue with the
        // search". Canonical/plural forms now normalize to their logical names.
        var entityFilter = new List<string>();
        var warnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(scope))
        {
            foreach (var candidate in scope.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var normalized = ToTableLogicalName(candidate.ToLowerInvariant());
                if (LogicalNameRegex().IsMatch(normalized))
                {
                    entityFilter.Add(normalized);
                }
                else
                {
                    warnings.Add($"Scope entry '{candidate}' is not a table logical name and was ignored (native transport does not implement MCP scopes).");
                }
            }
        }

        try
        {
            // Dataverse Search API v2 — executes under the USER's OBO token; results respect
            // the user's security roles and row-level security (Dataverse-enforced).
            var requestBody = new Dictionary<string, object?>
            {
                ["search"] = query,
                ["top"] = SearchTop
            };
            if (entityFilter.Count > 0)
            {
                // v2.0 contract: entities is a JSON-array-as-string.
                requestBody["entities"] = JsonSerializer.Serialize(entityFilter);
            }

            var response = await _dataverse.PostAsync(
                "/api/search/v2.0/query",
                JsonSerializer.Serialize(requestBody),
                cancellationToken).ConfigureAwait(false);

            // G-P3 UAT round-1 H8: when a SCOPED query fails (typically an entity filter the
            // Search API rejects — a name that is not a search-enabled table), retry ONCE
            // without the filter instead of failing the whole search. This is exactly the
            // recovery the model attempted manually in the incident transcript ("retry
            // without the scope limitation") — doing it deterministically server-side turns
            // a user-visible "technical issue" into a working (broader) search + warning.
            if (!response.IsSuccess &&
                response.ErrorCode != DataverseUserClientErrorCodes.NotFound &&
                entityFilter.Count > 0)
            {
                _logger.LogWarning(
                    "[dataverse.search_data][H8] scoped search rejected by the Search API " +
                    "(errorCode={ErrorCode}, entityFilter={EntityFilter}); retrying once without the entity filter. " +
                    "decisionId={DecisionId}",
                    response.ErrorCode,
                    string.Join(",", entityFilter),
                    context.DecisionId);
                warnings.Add(
                    $"The scope filter ({string.Join(", ", entityFilter)}) was rejected by the search service " +
                    "(entries must be search-enabled table logical names, e.g. sprk_matter); the search ran " +
                    "across all search-enabled tables instead.");

                requestBody.Remove("entities");
                entityFilter.Clear();
                response = await _dataverse.PostAsync(
                    "/api/search/v2.0/query",
                    JsonSerializer.Serialize(requestBody),
                    cancellationToken).ConfigureAwait(false);
            }

            if (!response.IsSuccess)
            {
                // A 404 from the search endpoint means Dataverse Search is not provisioned in
                // the environment — a dependency problem, not a data problem.
                if (response.ErrorCode == DataverseUserClientErrorCodes.NotFound)
                {
                    return Error(tool,
                        "Dataverse Search is not enabled in this environment; dataverse.search_data is unavailable. " +
                        "Use dataverse.read_query for structured lookups instead.",
                        ToolErrorCodes.DependencyUnavailable, startedAt);
                }
                return MapClientError(tool, response, startedAt);
            }

            var results = new List<object>();
            var citations = new List<ToolResultCitation>();
            var value = FindResultsArray(response.Body);
            if (value is { } resultsArray)
            {
                foreach (var hit in resultsArray.EnumerateArray())
                {
                    var entityName = GetString(hit, "entityname") ?? GetString(hit, "EntityName");
                    var objectIdRaw = GetString(hit, "objectid") ?? GetString(hit, "ObjectId") ?? GetString(hit, "id") ?? GetString(hit, "Id");
                    if (entityName is null || !Guid.TryParse(objectIdRaw, out var recordId))
                    {
                        continue;
                    }

                    double? score = null;
                    if (hit.TryGetProperty("@search.score", out var scoreProp) && scoreProp.ValueKind == JsonValueKind.Number)
                    {
                        score = scoreProp.GetDouble();
                    }

                    // GA search_data returns 'content' excerpts for file-column matches; the
                    // v2 search API surfaces highlights — forward when present (record text
                    // the USER is entitled to read; carried in Data, never logged).
                    string? matchedContent = null;
                    if (hit.TryGetProperty("highlights", out var highlights) && highlights.ValueKind == JsonValueKind.Object)
                    {
                        matchedContent = string.Join(" … ",
                            highlights.EnumerateObject()
                                .SelectMany(h => h.Value.ValueKind == JsonValueKind.Array
                                    ? h.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString())
                                    : Enumerable.Empty<string?>())
                                .Where(s => !string.IsNullOrWhiteSpace(s))
                                .Take(3)!);
                        if (string.IsNullOrWhiteSpace(matchedContent)) matchedContent = null;
                    }

                    results.Add(new
                    {
                        path = DataverseRecordCitations.RecordPath(entityName, recordId),
                        entity = entityName,
                        recordId = recordId.ToString("D"),
                        score,
                        content = matchedContent
                    });
                    citations.Add(DataverseRecordCitations.ForRecord(entityName, recordId));
                }
            }

            stopwatch.Stop();
            // ADR-015 / NFR-07: query LENGTH + entity filter + counts + duration ONLY.
            // NEVER the query text (user-authored) or result content.
            _logger.LogInformation(
                "[dataverse.search_data][ADR-015] queryLen={QueryLen} entityFilter={EntityFilter} results={ResultCount} decisionId={DecisionId} durationMs={DurationMs}",
                query.Length,
                entityFilter.Count > 0 ? string.Join(",", entityFilter) : "(all)",
                results.Count,
                context.DecisionId,
                stopwatch.ElapsedMilliseconds);

            return ToolResult.Ok(
                HandlerId, tool.Id, tool.Name,
                data: new
                {
                    tool = DataverseToolNames.SearchData,
                    results,
                    count = results.Count,
                    citations = citations.Select(c => new { path = c.ChunkId, entity = c.SourceName })
                },
                summary: results.Count > 0
                    ? $"Found {results.Count} matching record(s) visible to the current user. Use dataverse.describe on a result path to read the full record."
                    : "No matching records were found (within the records visible to the current user).",
                confidence: 1.0,
                execution: Timed(startedAt),
                warnings: warnings) with
            {
                Metadata = citations.Count > 0
                    ? new Dictionary<string, object?> { [ToolResultMetadataKeys.Citations] = citations }
                    : null
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Error(tool, "dataverse.search_data was cancelled.", ToolErrorCodes.Cancelled, startedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[dataverse.search_data] failed decisionId={DecisionId}: {ErrorType}",
                context.DecisionId, ex.GetType().Name);
            return Error(tool, "dataverse.search_data failed unexpectedly.", ToolErrorCodes.InternalError, startedAt);
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    internal static bool TryParseArgs(string? argsJson, out string query, out string? scope, out string? error)
    {
        query = string.Empty;
        scope = null;
        error = null;

        if (string.IsNullOrWhiteSpace(argsJson))
        {
            error = "Tool arguments JSON is required (expected { \"query\": \"…\" }).";
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
            if (!doc.RootElement.TryGetProperty("query", out var queryProp) ||
                queryProp.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(queryProp.GetString()))
            {
                error = "Tool arguments must include a non-empty 'query' string (search keywords or phrase).";
                return false;
            }
            query = queryProp.GetString()!;

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

    /// <summary>
    /// Locates the results array in a Dataverse Search API response. The v2.0 endpoint has
    /// shipped both <c>{"value":[…]}</c> and <c>{"response":{"value":[…]}}</c> shapes — accept
    /// either so a service-side format change degrades to empty results, not an exception.
    /// </summary>
    internal static JsonElement? FindResultsArray(JsonElement? body)
    {
        if (body is not { } root || root.ValueKind != JsonValueKind.Object) return null;

        if (root.TryGetProperty("value", out var direct) && direct.ValueKind == JsonValueKind.Array)
            return direct;

        if (root.TryGetProperty("response", out var nested))
        {
            if (nested.ValueKind == JsonValueKind.Object &&
                nested.TryGetProperty("value", out var nestedValue) && nestedValue.ValueKind == JsonValueKind.Array)
            {
                return nestedValue;
            }
            // Some SKUs return the nested response as a serialized JSON string.
            if (nested.ValueKind == JsonValueKind.String)
            {
                try
                {
                    using var doc = JsonDocument.Parse(nested.GetString()!);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                        doc.RootElement.TryGetProperty("value", out var stringValue) && stringValue.ValueKind == JsonValueKind.Array)
                    {
                        return stringValue.Clone();
                    }
                }
                catch (JsonException)
                {
                    return null;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// G-P3 UAT round-1 H8 — maps the CANONICAL entity vocabulary the model naturally
    /// uses (matter / project / invoice, singular or plural — the same short forms
    /// <c>ChatHostContext</c>/<c>EntityTypeNormalizer</c> put in front of it) to the
    /// Dataverse table logical names the Search API requires. Unknown values pass
    /// through unchanged (already-correct logical names, future tables).
    /// </summary>
    internal static string ToTableLogicalName(string candidate) => candidate switch
    {
        "matter" or "matters" => "sprk_matter",
        "project" or "projects" => "sprk_project",
        "invoice" or "invoices" => "sprk_invoice",
        "document" or "documents" => "sprk_document",
        _ => candidate,
    };

    private ToolResult MapClientError(AnalysisTool tool, DataverseUserResponse response, DateTimeOffset startedAt) =>
        ToolResult.Error(HandlerId, tool.Id, tool.Name,
            response.ErrorMessage ?? "Dataverse request failed.",
            response.ErrorCode,
            Timed(startedAt));

    private ToolResult Error(AnalysisTool tool, string message, string code, DateTimeOffset startedAt) =>
        ToolResult.Error(HandlerId, tool.Id, tool.Name, message, code, Timed(startedAt));

    private static ToolExecutionMetadata Timed(DateTimeOffset startedAt) =>
        new() { StartedAt = startedAt, CompletedAt = DateTimeOffset.UtcNow };

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
