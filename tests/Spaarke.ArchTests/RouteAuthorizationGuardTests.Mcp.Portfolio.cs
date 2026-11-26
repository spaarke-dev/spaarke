namespace Spaarke.ArchTests;

/// <summary>
/// The MCP tool ledger, Portfolio area (spaarkeai-mcp-bff-api-r2 task 020): the rows of <c>McpTools</c> for this area's tools. The task
/// that adds a tool adds its row here, in the same diff. The rules, the guard and the concatenation of every area's rows live in
/// <c>RouteAuthorizationGuardTests.Mcp.cs</c>, so parallel tool tasks never edit one list.
/// </summary>
public partial class RouteAuthorizationGuardTests
{
    private static readonly IReadOnlyList<McpTool> McpToolsPortfolio = new[]
    {
        // --- task 026 (spaarke_get_portfolio) ---
        new McpTool("GetPortfolioTool", "", "PortfolioService",
            "FR-08, model-visible, read-only, no widget: the caller's matter portfolio summary through PortfolioService.GetPortfolioSummaryAsync, "
            + "the call GET /api/workspace/portfolio makes. The REST access check is WorkspaceAuthorizationFilter, which only resolves the caller's "
            + "oid; the tool makes the same CallerResolution.ResolveObjectId call on IHttpContextAccessor.HttpContext's User and answers "
            + "UserContextRequired with none. Matter-level access stays inside the service (people-targeted set, read as the caller). Bare [Authorize]."),
        // --- end task 026 ---
    };
}
