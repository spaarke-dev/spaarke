namespace Spaarke.ArchTests;

/// <summary>
/// The MCP tool ledger, Analysis area (spaarkeai-mcp-bff-api-r2 task 020): the rows of <c>McpTools</c> for this area's tools. The task
/// that adds a tool adds its row here, in the same diff. The rules, the guard and the concatenation of every area's rows live in
/// <c>RouteAuthorizationGuardTests.Mcp.cs</c>, so parallel tool tasks never edit one list.
/// </summary>
public partial class RouteAuthorizationGuardTests
{
    // Task 031 (FR-06, NFR-06): spaarke_start_analysis (a model-callable reversible write) and spaarke_get_analysis (read-only), both
    // through caller-facing seams (ADR-013: the tools name no Services/Ai type outside PublicContracts). Task 037a: the start's create moved
    // from AnalysisEndpoints into the PublicContracts facade IAnalysisStartAi (AnalysisStartService), unchanged.
    private static readonly IReadOnlyList<McpTool> McpToolsAnalysis = new[]
    {
        new McpTool("StartAnalysisTool", "", "IAnalysisStartAi",
            "FR-06, reversible write: IAnalysisStartAi.StartAsCallerAsync with exactly IHttpContextAccessor.HttpContext runs the "
            + "route's create decision (AnalysisAuthorizationFilter.AuthorizeAnalysisCreateAsync: Create on sprk_analysis, Read and AppendTo "
            + "on the document, playbook use) as the caller, then the route's one RecordCreationService create. The tool adds no check and "
            + "runs nothing; its idempotencyKey is per caller. Bare [Authorize]."),
        new McpTool("GetAnalysisTool", "", "AnalysisEndpoints",
            "FR-06, read-only: AnalysisEndpoints.ReadAnalysisAsCallerAsync with exactly IHttpContextAccessor.HttpContext asks the route "
            + "filter's analysis-read rule (AnalysisAuthorizationFilter.IsAnalysisReadableAsync) as the caller before the handler's read; "
            + "unreadable, hidden and absent are one not-found. The tool adds no check. Bare [Authorize]."),
    };
}
