namespace Spaarke.ArchTests;

/// <summary>
/// The MCP tool ledger, Documents area (spaarkeai-mcp-bff-api-r2 task 020): the rows of <c>McpTools</c> for this area's tools. The task
/// that adds a tool adds its row here, in the same diff. The rules, the guard and the concatenation of every area's rows live in
/// <c>RouteAuthorizationGuardTests.Mcp.cs</c>, so parallel tool tasks never edit one list.
/// </summary>
public partial class RouteAuthorizationGuardTests
{
    // Task 023 (FR-03): spaarke_list_documents and spaarke_get_document, both read-only and visible to the model and the email widget.
    private static readonly IReadOnlyList<McpTool> McpToolsDocuments = new[]
    {
        new McpTool("ListDocumentsTool", "", "IDocumentDataverseService",
            "FR-03, read-only: a matter's documents through IDocumentDataverseService.GetDocumentsByMatterAsync, after Read on the matter as the "
            + "caller (RecordRouteAccessAuthorizationFilter.DecideRouteRecordAsync, the list route's owning-record gate) and the external document "
            + "narrowing over the rows (the list route's second gate); a forbidden and a missing matter are one not-found. Bare [Authorize]."),
        new McpTool("GetDocumentTool", "", "IDocumentDataverseService",
            "FR-03, read-only: one document's slim metadata (IDocumentDataverseService.GetDocumentAsync, the GET route's row) and its preview and "
            + "open links (the two routes' extracted link steps), after the routes' own per-document decision, Read as the caller then the external "
            + "narrowing, through InternalPlaneDocumentNarrowing.AuthorizeDocumentsAsync; forbidden, hidden and missing are one not-found. Bare [Authorize]."),
    };
}
