namespace Spaarke.ArchTests;

/// <summary>
/// The MCP tool ledger, Finance area (spaarkeai-mcp-bff-api-r2 task 020): the rows of <c>McpTools</c> for this area's tools. The task
/// that adds a tool adds its row here, in the same diff. The rules, the guard and the concatenation of every area's rows live in
/// <c>RouteAuthorizationGuardTests.Mcp.cs</c>, so parallel tool tasks never edit one list.
/// </summary>
public partial class RouteAuthorizationGuardTests
{
    private static readonly IReadOnlyList<McpTool> McpToolsFinance = new[]
    {
        // --- task 033 (spaarke_get_matter_finance) ---
        new McpTool("GetMatterFinanceTool", "", "IFinanceSummaryService",
            "FR-11, model-visible, read-only, no widget: one matter's finance summary through IFinanceSummaryService.GetSummaryAsync, the call "
            + "GET /api/finance/matters/{matterId}/summary makes, after FinanceAuthorizationFilter.AuthorizeMatterFinanceReadAsync (task 008): step 0 "
            + "(internal callers only) then finance.read on the matter as the caller, the route's own rule. A refusal carries the route's reasonCode; "
            + "the tool adds no check and offers no invoice workflow (EB-D-194). Bare [Authorize]."),
        // --- end task 033 ---
    };
}
