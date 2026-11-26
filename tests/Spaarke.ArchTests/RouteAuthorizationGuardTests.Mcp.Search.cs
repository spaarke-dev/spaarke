namespace Spaarke.ArchTests;

/// <summary>
/// The MCP tool ledger, Search area (spaarkeai-mcp-bff-api-r2 task 020): the rows of <c>McpTools</c> for this area's tools. The task
/// that adds a tool adds its row here, in the same diff. The rules, the guard and the concatenation of every area's rows live in
/// <c>RouteAuthorizationGuardTests.Mcp.cs</c>, so parallel tool tasks never edit one list.
/// </summary>
public partial class RouteAuthorizationGuardTests
{
    // Task 022 (FR-02): spaarke_search_documents and spaarke_find_records, both read-only and visible to the model and the search results widget.
    private static readonly IReadOnlyList<McpTool> McpToolsSearch = new[]
    {
        new McpTool("SearchDocumentsTool", "", "SemanticSearchAuthorizationFilter",
            "FR-02, read-only: semantic search over document content as the caller, through SemanticSearchAuthorizationFilter.SearchDocumentsAsCallerAsync "
            + "(task 027), the one implementation of POST /api/ai/search's access decision: the caller's tenant and token, the parent's Read on "
            + "scope=entity, the per-row parent check on scope=all, then the readable-document trim. No check is copied; the index is resolved outside "
            + "the tool class. Bare [Authorize]."),
        new McpTool("FindRecordsTool", "", "RecordSearchAuthorizationFilter",
            "FR-02, read-only: search over matters, projects and invoices as the caller, through RecordSearchAuthorizationFilter.SearchRecordsAsCallerAsync "
            + "(task 027), the one implementation of POST /api/ai/search/records' access decision: the caller's tenant and token, the record types, then "
            + "every returned record checked as the caller with the route's 50-check budget. No check is copied; the index is resolved outside the "
            + "tool class. Bare [Authorize]."),
    };
}
