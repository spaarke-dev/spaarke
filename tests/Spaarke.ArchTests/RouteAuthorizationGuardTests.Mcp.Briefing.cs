namespace Spaarke.ArchTests;

/// <summary>
/// The MCP tool ledger, Briefing area (spaarkeai-mcp-bff-api-r2 task 020): the rows of <c>McpTools</c> for this area's tools. The task
/// that adds a tool adds its row here, in the same diff. The rules, the guard and the concatenation of every area's rows live in
/// <c>RouteAuthorizationGuardTests.Mcp.cs</c>, so parallel tool tasks never edit one list.
/// </summary>
public partial class RouteAuthorizationGuardTests
{
    private static readonly IReadOnlyList<McpTool> McpToolsBriefing = new[]
    {
        // --- task 034 (spaarke_get_briefing) ---
        new McpTool("GetBriefingTool", "", "DailyBriefingEndpoints.RenderForCallerAsync",
            "FR-09, model-visible and app-visible, read-only, opens ui://spaarke/briefing.html: the caller's AI Daily Briefing through the render "
            + "rules of POST /api/ai/daily-briefing/render (DailyBriefingEndpoints.RenderForCallerAsync, the same method the route's handler runs): "
            + "the caller's systemuserid from the token's oid, the same windows clamp, the same composite, every row read as the caller. The tool "
            + "passes IHttpContextAccessor.HttpContext to it and answers UserContextRequired with no caller; it copies no authorization and names no "
            + "Services/Ai type. Model-visible text is the TL;DR and counts only. Bare [Authorize]."),
        // --- end task 034 ---
    };
}
