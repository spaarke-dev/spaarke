using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Documents;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Search;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 022 (spec FR-02, FR-15, NFR-02, NFR-05, NFR-07; design §3; decisions notes/022-decisions.md).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md; spec "Placement & New Components"). A thin MCP
// adapter inside the BFF over the record search route's own access decision (RecordSearchAuthorizationFilter.SearchRecordsAsCallerAsync,
// task 027) on the /mcp/legal-operations host: no package, no DI registration (found by [McpArea]), no background work, no AI-internal
// type (the index is resolved in SearchToolSupport, outside any tool class; ADR-013).
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: POST /api/ai/search/records (RecordSearchEndpoints + RecordSearchAuthorizationFilter, whose decision task 027 made a shared
//     method). Task 009 proved no OData or FetchXML is built from the raw query text (FR-15 gate).
//   Extension: this IS the extension: it builds the route's request, calls the shared method and projects the answer to slim hits. It
//     copies no check.
//   Cost of doing nothing: with the legacy agent removed, Copilot users cannot find a matter, project or invoice by name or number (FR-02).
// =====================================================================================================================

/// <summary>
/// <c>spaarke_find_records</c> (FR-02): a search over Spaarke matters, projects and invoices, returning the records the signed-in user may
/// open, best match first, in slim hits. Read-only; visible to the model and the search results widget (task 035).
/// </summary>
/// <remarks>
/// <para><b>One decision, one answer.</b> The tool calls <see cref="RecordSearchAuthorizationFilter.SearchRecordsAsCallerAsync"/> with the
/// request's own <c>HttpContext</c>: the decision <c>POST /api/ai/search/records</c> makes (tenant, caller, record types, then every
/// returned record checked as the caller, with the route's 50-check budget), run by the one implementation. The tool copies no check.</para>
/// <para><b>Record types</b> are named as a user does (<c>matter</c>, <c>project</c>, <c>invoice</c>; default: all three) and sent as the
/// route's <c>sprk_*</c> types. <b>No counts</b> (D-022-counts): the tool returns no <c>totalCount</c> and no remainder; <c>hasMore</c> is
/// only "the page was full". Hits carry id, name, type, an excerpt of at most 200 characters, up to three reference numbers and an open
/// link; never the organizations, people or keywords the index extracted, and never a score.</para>
/// <para><b>Faults.</b> An exception from the shared method or the index is an <c>isError</c> <c>RECORD_SEARCH_FAILED</c> (500) with fixed
/// text; only the caller's own cancellation propagates. Logs never carry the query (NFR-05). Every user-authored value in a line of text
/// goes through <see cref="McpText.SingleLine"/>.</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class FindRecordsTool : McpToolDefinition
{
    internal const string ToolName = "spaarke_find_records";

    internal const string RecordTypesArgument = "recordTypes";

    /// <summary>The names a user gives the record types, and the route's type each stands for. The order is the default order.</summary>
    internal static readonly IReadOnlyDictionary<string, string> RecordTypeNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["matter"] = RecordEntityType.Matter,
            ["project"] = RecordEntityType.Project,
            ["invoice"] = RecordEntityType.Invoice,
        };

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [SearchToolSupport.QueryArgument] = new JsonObject
            {
                ["type"] = "string",
                ["maxLength"] = SearchToolSupport.MaxQueryLength,
                ["description"] = "The name, reference or invoice number, party or topic to look for.",
            },
            [RecordTypesArgument] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray(RecordTypeNames.Keys.Select(t => (JsonNode?)t).ToArray()),
                },
                ["minItems"] = 1,
                ["maxItems"] = RecordTypeNames.Count,
                ["description"] = "Optional. Which kinds of record to search. Default: matters, projects and invoices.",
            },
            [SearchToolSupport.LimitArgument] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = SearchToolSupport.MinLimit,
                ["maximum"] = SearchToolSupport.MaxLimit,
                ["description"] = $"Optional. How many records to return, up to {SearchToolSupport.MaxLimit}. Default: {SearchToolSupport.DefaultLimit}.",
            },
            [SearchToolSupport.OffsetArgument] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = SearchToolSupport.MinOffset,
                ["maximum"] = SearchToolSupport.MaxOffset,
                ["description"] = "Optional. How many records to skip, to continue a list. Default: 0.",
            },
        },
        ["required"] = new JsonArray(SearchToolSupport.QueryArgument),
        ["additionalProperties"] = false,
    });

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            FindAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = ToolName,
                Title = "Find matters, projects and invoices",
                Description =
                    "Use for: finding Spaarke matters, projects or invoices by name, reference or invoice number, party or people involved, or "
                    + "topic. It returns the records the signed-in user may open, best match first, each with its id, name, type, a short "
                    + "description, reference numbers and a link to open it. recordTypes narrows to matter, project or invoice (default: all "
                    + "three); use offset to continue a list. Not for: searching what documents say (use spaarke_search_documents); listing "
                    + "the documents of a matter (use spaarke_list_documents); tasks (use spaarke_list_tasks); totals, counts or reports "
                    + "across records.",
                ReadOnly = true,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
            });

        // The input schema is stated, not inferred; the handler reads the raw arguments against the same closed set.
        tool.ProtocolTool.InputSchema = InputSchema;
        return tool;
    }

    /// <summary>The handler the SDK binds: <see cref="RunAsync"/> inside the one per-call telemetry line (NFR-05).</summary>
    internal static Task<CallToolResult> FindAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        IOptions<DataverseOptions> dataverse,
        TimeProvider time,
        ILogger<FindRecordsTool> logger,
        CancellationToken cancellationToken) =>
        DecisionToolCall.LogAroundAsync(
            logger, time, ToolName, accessor.HttpContext,
            () => RunAsync(request.Params?.Arguments, accessor.HttpContext, dataverse.Value.EnvironmentUrl, logger, cancellationToken),
            SearchToolSupport.RowCount);

    internal static async Task<CallToolResult> RunAsync(
        IDictionary<string, JsonElement>? arguments, HttpContext? http, string? environmentUrl, ILogger logger, CancellationToken cancellationToken)
    {
        // ADR-028 A10: no caller, no search. Checked before anything else with the test every tool applies; the shared method checks it again.
        if (!PlanAndLookupToolSupport.HasCaller(http))
        {
            return DocumentToolSupport.Failure(DocumentToolSupport.UserContextRequired, http);
        }

        if (!SearchToolSupport.TryReadCommon(arguments, ToolName, [RecordTypesArgument], http, out var query, out var limit, out var offset, out var argumentError))
        {
            return argumentError!;
        }

        if (!TryReadRecordTypes(arguments!, http, out var recordTypes, out var typesError))
        {
            return typesError!;
        }

        var searchRequest = new RecordSearchRequest
        {
            Query = query,
            RecordTypes = recordTypes,
            Options = new RecordSearchOptions { Limit = limit, Offset = offset },
        };

        RecordSearchAccess access;
        try
        {
            access = await RecordSearchAuthorizationFilter.SearchRecordsAsCallerAsync(
                http, searchRequest, SearchToolSupport.RecordIndex(http), cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return SearchToolSupport.Fault(ex, ToolName, SearchErrorCodes.RecordSearchFailed, http, logger);
        }

        if (access is not { Outcome: RecordSearchAccessOutcome.Allowed, Response: { } response })
        {
            return DocumentToolSupport.Failure(SearchToolSupport.ErrorOf(access), http);
        }

        return ResultOf(response, limit, offset, DecisionsTextSummary.ConsoleBaseUrl(environmentUrl));
    }

    /// <summary>The route's record types for the names sent, de-duplicated in the order sent; all three when none are sent.</summary>
    private static bool TryReadRecordTypes(
        IDictionary<string, JsonElement> arguments, HttpContext? http, out IReadOnlyList<string> recordTypes, out CallToolResult? error)
    {
        error = null;
        recordTypes = [.. RecordTypeNames.Values];
        if (!arguments.TryGetValue(RecordTypesArgument, out var element) || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        var mapped = new List<string>();
        var valid = element.ValueKind == JsonValueKind.Array && element.GetArrayLength() is >= 1 and <= 3;
        if (valid)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || !RecordTypeNames.TryGetValue(item.GetString()?.Trim() ?? string.Empty, out var type))
                {
                    valid = false;
                    break;
                }

                if (!mapped.Contains(type, StringComparer.Ordinal))
                {
                    mapped.Add(type);
                }
            }
        }

        if (!valid)
        {
            error = DocumentToolSupport.InvalidArgument(
                $"'{RecordTypesArgument}' must be a list of one to three of: {string.Join(", ", RecordTypeNames.Keys)}. Leave it out to search all three.", http);
            return false;
        }

        recordTypes = mapped;
        return true;
    }

    /// <summary>The success result for the shared method's authorized response: slim hits, their lines of text, the paging facts. Pure.</summary>
    internal static CallToolResult ResultOf(RecordSearchResponse response, int limit, int offset, string? consoleBaseUrl)
    {
        var hits = response.Results
            .Select(r => new RecordHit(
                r.RecordId,
                SearchToolSupport.Clip(r.RecordName, SearchToolSupport.NameChars) ?? string.Empty,
                TypeNameOf(r.RecordType),
                SearchToolSupport.CleanText(r.RecordDescription, SearchToolSupport.SnippetChars),
                ReferencesOf(r.ReferenceNumbers),
                SearchToolSupport.OpenLink(consoleBaseUrl, RecordTypeNames.Values.Contains(r.RecordType, StringComparer.Ordinal) ? r.RecordType : null, r.RecordId)))
            .ToList();

        var (kept, lines, truncated) = SearchToolSupport.Fit(hits, LineOf);

        // "The page was full": the only paging fact shown. No totalCount and no remainder (D-022-counts).
        var hasMore = response.Results.Count >= limit;

        var text = new System.Text.StringBuilder()
            .Append(kept.Count == 0
                ? "No records you can open matched that search."
                : $"{kept.Count} record(s) matched, best first.")
            .Append(truncated ? " The list was cut to fit; narrow the search." : string.Empty)
            .Append(hasMore ? $" The page was full; send offset {offset + limit} for the next page." : string.Empty)
            .Append('\n')
            .Append(lines)
            .ToString();

        return DecisionToolCall.Success(text, new FindRecordsContent(kept, limit, offset, hasMore, truncated));
    }

    private static string TypeNameOf(string recordType) =>
        RecordTypeNames.FirstOrDefault(p => string.Equals(p.Value, recordType, StringComparison.Ordinal)).Key ?? recordType;

    private static IReadOnlyList<string>? ReferencesOf(IReadOnlyList<string>? numbers)
    {
        var kept = numbers?
            .Select(n => SearchToolSupport.CleanText(n, SearchToolSupport.ReferenceNumberChars))
            .Where(n => n is not null)
            .Take(SearchToolSupport.MaxReferenceNumbers)
            .Select(n => n!)
            .ToList();
        return kept is { Count: > 0 } ? kept : null;
    }

    private static string LineOf(RecordHit hit)
    {
        var details = new List<string> { McpText.SingleLine(hit.Type) };
        if (hit.ReferenceNumbers is { Count: > 0 } references)
        {
            details.Add("ref " + string.Join(", ", references.Select(McpText.SingleLine)));
        }

        details.Add($"id {McpText.SingleLine(hit.Id)}");
        var line = $"- {McpText.SingleLine(hit.Name)} ({string.Join("; ", details)})";
        if (hit.Snippet is not null)
        {
            line += $" \"{McpText.SingleLine(hit.Snippet)}\"";
        }

        if (hit.OpenLink is not null)
        {
            line += $" Open: {hit.OpenLink}";
        }

        return line + "\n";
    }

    /// <summary>One record hit (ADR-015): no organizations, people, keywords, score or dates.</summary>
    internal sealed record RecordHit(
        string Id, string Name, string Type, string? Snippet, IReadOnlyList<string>? ReferenceNumbers, string? OpenLink);

    /// <summary>
    /// <c>structuredContent</c> of <c>spaarke_find_records</c>: the hits, the page asked for, whether that page was full and whether hits
    /// were left out to keep the result inside the size bound. No total.
    /// </summary>
    internal sealed record FindRecordsContent(IReadOnlyList<RecordHit> Items, int Limit, int Offset, bool HasMore, bool Truncated);
}
