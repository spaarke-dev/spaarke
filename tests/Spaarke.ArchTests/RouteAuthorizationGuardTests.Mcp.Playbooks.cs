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

        // --- task 030 (spaarke_run_playbook, spaarke_get_playbook_run) ---
        new McpTool("RunPlaybookTool", "", "IPlaybookRunAi",
            "FR-05/FR-14, reversible write: IPlaybookRunAi.StartAsCallerAsync with exactly IHttpContextAccessor.HttpContext decides with "
            + "task 007's PlaybookAuthorizationFilter.AuthorizePlaybookRunAsync (the execute route's rule: document ids, parameter "
            + "policy, playbook use, every document and record parameter, the run's user) as the caller before anything runs, then "
            + "starts the engine's person-run entry as a detached caller run (ADR-052 amendment 2026-11-26) with the authorized inputs and "
            + "007's RunUserId. The tool adds no check (input caps only); its idempotencyKey is per caller. Bare [Authorize]."),
        new McpTool("GetPlaybookRunTool", "", "IPlaybookRunAi",
            "FR-05/FR-14, read-only: IPlaybookRunAi.ReadAsCallerAsync with exactly IHttpContextAccessor.HttpContext returns a run only to "
            + "the caller whose oid started it (this process's run table, then GetRunStatusAsync: in memory, then task 005's durable "
            + "record by StartedByOid); another caller's run and an unknown id are one not-found. Bare [Authorize]."),
        // --- end task 030 ---

        // --- task 037b (spaarke_get_playbook_run_results) ---
        new McpTool("GetPlaybookRunResultsTool", "", "IPlaybookRunAi",
            "037b, read-only and app-only (McpToolAuthorization.AppOnlyTools): IPlaybookRunAi.ReadResultsAsCallerAsync with exactly "
            + "IHttpContextAccessor.HttpContext answers only the caller whose oid started the run (ReadAsCallerAsync, then the record's "
            + "owner check), follows only the server-written regarding link to the run's analysis, applies the analysis-read rule and "
            + "reads the working document and document names as the caller over IDataverseUserClient (OBO). Another caller's run, an "
            + "unknown id, an unreadable subject or analysis: one not-found. Bare [Authorize]."),
        // --- end task 037b ---
    };
}
