using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Documents;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Search;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 022 (spec FR-02, FR-15, NFR-02, NFR-05, NFR-07; design §3; decisions notes/022-decisions.md).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md; spec "Placement & New Components"). A thin MCP
// adapter inside the BFF over the document search route's own access decision (SemanticSearchAuthorizationFilter.
// SearchDocumentsAsCallerAsync, task 027) on the /mcp/legal-operations host: no package, no DI registration (found by [McpArea]), no
// background work, no AI-internal type (the filter resolves the index itself; ADR-013, Path C).
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: POST /api/ai/search (SemanticSearchEndpoints + SemanticSearchAuthorizationFilter, whose decision task 027 made a shared method).
//   Extension: this IS the extension: it builds the route's request, calls the shared method and projects the answer to slim hits. It
//     copies no check.
//   Cost of doing nothing: with the legacy agent removed, Copilot users cannot search Spaarke documents (FR-02).
// =====================================================================================================================

/// <summary>
/// <c>spaarke_search_documents</c> (FR-02): a semantic search over the content of Spaarke documents, returning the documents the signed-in
/// user may open, best match first, in slim hits. Read-only; visible to the model and the search results widget (task 035).
/// </summary>
/// <remarks>
/// <para><b>One decision, one answer.</b> The tool calls <see cref="SemanticSearchAuthorizationFilter.SearchDocumentsAsCallerAsync"/> with
/// the request's own <c>HttpContext</c>: the decision <c>POST /api/ai/search</c> makes (tenant, caller, scope, the parent's Read, the
/// per-row parent check on <c>scope=all</c>, the readable-document trim, pointer stripping), run by the one implementation, with the same
/// Dataverse questions in the same order. The tool never calls a phase method and copies no check (an ArchTest forbids the former).</para>
/// <para><b>Scope.</b> Without <c>entityType</c> and <c>entityId</c> the scope is <c>all</c>; with both it is <c>entity</c>: one matter,
/// project or invoice. Both or neither; the tool does not offer <c>documentIds</c>.</para>
/// <para><b>No counts.</b> The tool returns no total and no "N more" (D-022-counts): the entity scope's total still counts documents the
/// caller cannot open inside a readable parent (#1533). <c>hasMore</c> is "the page came back full" and only inside one record (over every
/// record <c>offset</c> does not continue a list, so no offset is advised and <c>hasMore</c> is false); false is not the end of the list.</para>
/// <para><b>Model-visible output (ADR-015, NFR-02).</b> A hit is id, name, file type, document type, the parent record's type and name, an
/// excerpt of at most 200 characters and an open link; never a storage pointer, a score, an author or a summary beyond the excerpt. Hits
/// are kept only while the result stays inside <see cref="SearchToolSupport.RowBudgetBytes"/>, so the result is under 64 KiB at 50 hits whatever
/// the text; a cut list says so (<c>truncated</c>). Every user-authored value in a line of text goes through <see cref="McpText.SingleLine"/>.</para>
/// <para><b>Faults.</b> An exception from the shared method or the index (an <c>SdapProblemException</c> included) is an <c>isError</c>
/// <c>SEMANTIC_SEARCH_FAILED</c> (500) with fixed text, never the exception's; only the caller's own cancellation propagates. Logs carry
/// the tool name, latency, row count, outcome and correlation id, never the query (NFR-05).</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class SearchDocumentsTool : McpToolDefinition
{
    internal const string ToolName = "spaarke_search_documents";

    /// <summary>The widget the tool's descriptor points at (<c>_meta.ui.resourceUri</c>).</summary>
    public const string WidgetUri = SearchResultsWidgetResource.Uri;

    internal const string EntityTypeArgument = "entityType";

    internal const string EntityIdArgument = "entityId";

    /// <summary>The parent types the tool offers: the entries of the route's authorizable set a user can name.</summary>
    internal static readonly string[] EntityTypes = ["matter", "project", "invoice"];

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [SearchToolSupport.QueryArgument] = new JsonObject
            {
                ["type"] = "string",
                ["maxLength"] = SearchToolSupport.MaxQueryLength,
                ["description"] = "What to look for in the documents: a topic, clause, party or phrase.",
            },
            [EntityTypeArgument] = new JsonObject
            {
                ["type"] = "string",
                ["enum"] = new JsonArray(EntityTypes.Select(t => (JsonNode?)t).ToArray()),
                ["description"] = "Optional, with entityId. Search only the documents filed under one matter, project or invoice.",
            },
            [EntityIdArgument] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional, with entityType. The id (GUID) of that matter, project or invoice.",
            },
            [SearchToolSupport.LimitArgument] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = SearchToolSupport.MinLimit,
                ["maximum"] = SearchToolSupport.MaxLimit,
                ["description"] = $"Optional. How many documents to return, up to {SearchToolSupport.MaxLimit}. Default: {SearchToolSupport.DefaultLimit}.",
            },
            [SearchToolSupport.OffsetArgument] = new JsonObject
            {
                ["type"] = "integer",
                ["minimum"] = SearchToolSupport.MinOffset,
                ["maximum"] = SearchToolSupport.MaxOffset,
                ["description"] = "Optional. How many documents to skip, to continue a list. It continues a list only inside one record "
                    + "(entityType + entityId); without them each page is a fresh search and a later offset can repeat hits. Default: 0.",
            },
        },
        ["required"] = new JsonArray(SearchToolSupport.QueryArgument),
        ["additionalProperties"] = false,
    });

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            SearchAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = ToolName,
                Title = "Search documents",
                Description =
                    "Use for: finding Spaarke documents by what they say: a topic, clause, party or phrase. It returns the documents the signed-in "
                    + "user may open, best match first, each with its id, name, file type, the matter or project it is filed under, a short "
                    + "excerpt and a link to open it. Set entityType and entityId to search inside one matter, project or invoice; only there does "
                    + "offset continue a list, and hasMore means the page came back full (false is not the end of the list). "
                    + "Not for: finding a matter, project or invoice itself by name or number (use spaarke_find_records); "
                    + "listing the documents of a matter (use spaarke_list_documents); opening one document (use spaarke_get_document); "
                    + "summarising or analysing a document.",
                ReadOnly = true,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
                // MCP Apps (task 035): the search results widget renders the result. Only the descriptor changes; the text and structuredContent are 022's.
                Meta = new JsonObject
                {
                    ["ui"] = new JsonObject { ["resourceUri"] = WidgetUri },
                },
            });

        // The input schema is stated, not inferred; the handler reads the raw arguments against the same closed set.
        tool.ProtocolTool.InputSchema = InputSchema;
        return tool;
    }

    /// <summary>The handler the SDK binds: <see cref="RunAsync"/> inside the one per-call telemetry line (NFR-05).</summary>
    internal static Task<CallToolResult> SearchAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        IOptions<DataverseOptions> dataverse,
        TimeProvider time,
        ILogger<SearchDocumentsTool> logger,
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

        if (!SearchToolSupport.TryReadCommon(arguments, ToolName, [EntityTypeArgument, EntityIdArgument], http, out var query, out var limit, out var offset, out var argumentError))
        {
            return argumentError!;
        }

        if (!TryReadScope(arguments!, http, out var entityType, out var entityId, out var scopeError))
        {
            return scopeError!;
        }

        var searchRequest = new SemanticSearchRequest
        {
            Query = query,
            Scope = entityType is null ? SearchScope.All : SearchScope.Entity,
            EntityType = entityType,
            EntityId = entityId,
            Options = new SearchOptions { Limit = limit, Offset = offset },
        };

        SemanticSearchAccess access;
        try
        {
            access = await SemanticSearchAuthorizationFilter.SearchDocumentsAsCallerAsync(http, searchRequest, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return SearchToolSupport.Fault(ex, ToolName, SearchErrorCodes.SemanticSearchFailed, http, logger);
        }

        if (access is not { Outcome: SemanticSearchAccessOutcome.Allowed, Response: { } response })
        {
            return DocumentToolSupport.Failure(SearchToolSupport.ErrorOf(access), http);
        }

        return ResultOf(response, limit, offset, DecisionsTextSummary.ConsoleBaseUrl(environmentUrl), offsetContinues: entityType is not null);
    }

    /// <summary>Both <c>entityType</c> and <c>entityId</c> (a known type and a GUID in canonical form), or neither.</summary>
    private static bool TryReadScope(
        IDictionary<string, JsonElement> arguments, HttpContext? http, out string? entityType, out string? entityId, out CallToolResult? error)
    {
        entityType = null;
        entityId = null;
        error = null;

        var hasType = TryGetText(arguments, EntityTypeArgument, out var type);
        var hasId = TryGetText(arguments, EntityIdArgument, out var id);
        if (!hasType && !hasId)
        {
            return true;
        }

        var known = EntityTypes.FirstOrDefault(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));
        if (!hasType || !hasId || known is null || !Guid.TryParse(id, out var guid) || guid == Guid.Empty)
        {
            error = DocumentToolSupport.InvalidArgument(
                $"'{EntityTypeArgument}' and '{EntityIdArgument}' go together: send both, or neither to search every document you can open. "
                + $"'{EntityTypeArgument}' is one of {string.Join(", ", EntityTypes)}; '{EntityIdArgument}' is that record's id (a GUID).", http);
            return false;
        }

        entityType = known;
        entityId = guid.ToString("D");
        return true;
    }

    /// <summary>The trimmed text of an argument; false when it is absent, JSON null, not text or blank.</summary>
    private static bool TryGetText(IDictionary<string, JsonElement> arguments, string name, out string? text)
    {
        text = null;
        if (!arguments.TryGetValue(name, out var element) || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        // A value of the wrong kind still counts as "sent", so it is refused with the fix instead of being ignored: report it as text that no
        // known value matches.
        text = element.ValueKind == JsonValueKind.String ? element.GetString()?.Trim() : element.ToString();
        return true;
    }

    /// <summary>
    /// The success result for the shared method's authorized response: slim hits, their lines of text, the paging facts. Pure.
    /// <paramref name="offsetContinues"/> is true only inside one record (<c>scope=entity</c>): over every record the shared method reads a wider
    /// window of the index (3 times the limit) from <c>offset</c> than it returns, so "offset + limit" would repeat hits and is never advised.
    /// </summary>
    internal static CallToolResult ResultOf(SemanticSearchResponse response, int limit, int offset, string? consoleBaseUrl, bool offsetContinues)
    {
        var hits = response.Results
            .Select(r => new DocumentHit(
                r.DocumentId ?? string.Empty,
                SearchToolSupport.Clip(r.Name, SearchToolSupport.NameChars) ?? string.Empty,
                r.FileType,
                r.DocumentType,
                r.ParentEntityType,
                SearchToolSupport.Clip(r.ParentEntityName, SearchToolSupport.ParentNameChars),
                SearchToolSupport.CleanText(r.Highlights?.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h)) ?? r.Tldr, SearchToolSupport.SnippetChars),
                SearchToolSupport.OpenLink(consoleBaseUrl, SearchToolSupport.DocumentTable, r.DocumentId)))
            .ToList();

        var (kept, lines, truncated) = SearchToolSupport.Fit(hits, LineOf);

        // The only paging facts: "the page came back full" (hasMore, true only where an offset continues the list) and the offset the text
        // advises (nextOffset). No total and no remainder (D-022-counts, #1533). hasMore = false is NOT "the end of the list".
        var warnings = response.Metadata.Warnings?.Select(w => w.Code).Distinct(StringComparer.Ordinal).ToList();
        var partial = warnings?.Contains(SearchWarningCode.PartialResults) == true;

        var page = SearchToolSupport.DescribePage(
            "document",
            "No documents you can open matched that search.",
            partial ? " The results may be incomplete." : string.Empty,
            response.Results.Count, kept.Count, truncated, limit, offset, offsetContinues);

        var text = new System.Text.StringBuilder()
            .Append(page.Header)
            .Append('\n')
            .Append(lines)
            .ToString();

        return DecisionToolCall.Success(
            text, new SearchDocumentsContent(kept, limit, offset, page.HasMore, truncated, page.NextOffset, warnings is { Count: > 0 } ? warnings : null));
    }

    private static string LineOf(DocumentHit hit)
    {
        var details = new List<string>(3);
        if (!string.IsNullOrEmpty(hit.FileType))
        {
            details.Add(McpText.SingleLine(hit.FileType));
        }

        if (!string.IsNullOrEmpty(hit.ParentName))
        {
            details.Add(string.IsNullOrEmpty(hit.ParentType)
                ? $"in {McpText.SingleLine(hit.ParentName)}"
                : $"in {McpText.SingleLine(hit.ParentType)} {McpText.SingleLine(hit.ParentName)}");
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

    /// <summary>One document hit (ADR-015): no storage pointer, score, author or summary beyond the excerpt.</summary>
    internal sealed record DocumentHit(
        string Id, string Name, string? FileType, string? DocumentType, string? ParentType, string? ParentName, string? Snippet, string? OpenLink);

    /// <summary>
    /// <c>structuredContent</c> of <c>spaarke_search_documents</c>: the hits, the page asked for, whether that page came back full inside one
    /// record (<c>hasMore</c>: not "the end of the list" when false), whether hits were left out to keep the result inside the size bound, the
    /// offset the text advises (<c>nextOffset</c>, written as null when it advises none) and the index's warning codes (codes only). No total.
    /// </summary>
    internal sealed record SearchDocumentsContent(
        IReadOnlyList<DocumentHit> Items,
        int Limit,
        int Offset,
        bool HasMore,
        bool Truncated,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? NextOffset,
        IReadOnlyList<string>? Warnings);
}
