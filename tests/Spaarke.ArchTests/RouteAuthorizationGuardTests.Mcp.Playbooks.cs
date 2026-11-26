namespace Spaarke.ArchTests;

/// <summary>
/// The MCP tool ledger, Playbooks area (spaarkeai-mcp-bff-api-r2 task 020): the rows of <c>McpTools</c> for this area's tools. The task
/// that adds a tool adds its row here, in the same diff. The rules, the guard and the concatenation of every area's rows live in
/// <c>RouteAuthorizationGuardTests.Mcp.cs</c>, so parallel tool tasks never edit one list.
/// </summary>
public partial class RouteAuthorizationGuardTests
{
    private static readonly IReadOnlyList<McpTool> McpToolsPlaybooks = new[]
    {
        // --- task 024 (spaarke_list_playbooks) ---
        new McpTool("ListPlaybooksTool", "", "PlaybookEndpoints",
            "spaarke_list_playbooks (FR-04, model-visible, read-only): the owned and public JPS playbook lists through "
            + "PlaybookEndpoints.ListPlaybooksForCallerAsync, the two list routes' own rules (owner = the caller's systemuserid by WhoAmI "
            + "over OBO, PublicOnly, paging), with exactly IHttpContextAccessor.HttpContext; no caller or no systemuser is "
            + "UserContextRequired. Bare [Authorize]: an A10 workforce user, like the REST playbook group."),
        // --- end task 024 ---
    };
}
