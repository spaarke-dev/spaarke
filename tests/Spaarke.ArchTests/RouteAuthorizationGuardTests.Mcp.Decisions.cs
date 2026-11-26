namespace Spaarke.ArchTests;

/// <summary>
/// The MCP tool ledger, Decisions area (spaarkeai-mcp-bff-api-r1 tasks 022-024): the Decision tools' rows of <c>McpTools</c>, moved
/// verbatim from <c>RouteAuthorizationGuardTests.Mcp.cs</c> by spaarkeai-mcp-bff-api-r2 task 020. The rules, the guard and the
/// concatenation of every area's rows live in that file; this file holds rows only.
/// </summary>
public partial class RouteAuthorizationGuardTests
{
    // Tasks 022-024 add spaarke_show_decisions, spaarke_list_decision_page, spaarke_get_decision_plan, spaarke_search_decision_lookup and
    // spaarke_complete_decision, each with its entry here, in the diff that adds the tool.
    private static readonly IReadOnlyList<McpTool> McpToolsDecisions = new[]
    {
        // --- task 024 (spaarke_complete_decision) ---
        new McpTool("CompleteDecisionTool", "", "IDecisionCapability",
            "The one write tool, app-only (task 024, FR-08): a thin adapter that calls IDecisionCapability.CommitAsync with exactly "
            + "IHttpContextAccessor.HttpContext and returns the commit result; DecisionCommitService stays the single owner of Confirm, "
            + "idempotency, the invoice-review refusal and the caller's rights, so the tool adds no check of its own."),
        // --- end task 024 ---
        // spaarkeai-mcp-bff-api-r1 task 023 (one contiguous block; tasks 022 and 024 add their own entries beside it).
        new McpTool("GetDecisionPlanTool", "", "IDecisionCapability",
            "FR-06, app-only and read-only: the Signal's decision plan, suppression and full row as the caller through IDecisionCapability "
            + "(GetPlanAndSuppressionAsync: the REST plan and suppression routes' answers with one Signal access check, NFR-01; it also writes "
            + "the plan-read stamp), then the row through GetWorkItemAsync (the caller's lanes read narrowed to that Signal). Bare [Authorize]: an A10 workforce user, like the REST Signals group."),
        new McpTool("SearchDecisionLookupTool", "", "IDecisionLookupSearch",
            "FR-07, app-only and read-only: the wizard's person, budget and firm search as the caller through IDecisionLookupSearch (the REST "
            + "lookup route's service); the plan names the table and scope. Bare [Authorize]: an A10 workforce user, like the REST Signals group."),
        // ── task 022 ──
        new McpTool("ShowDecisionsTool", "", "IDecisionCapability",
            "spaarke_show_decisions (FR-03/FR-04, model-visible, read-only): the caller's Decide lane through ListDecidePageAsync (one "
            + "read; the counts come from the lanes response the page carries, NFR-01) with IHttpContextAccessor.HttpContext's User; "
            + "privileged rows reduced to a count before the model sees them."),
        new McpTool("ListDecisionPageTool", "", "IDecisionCapability",
            "spaarke_list_decision_page (FR-05, app-only, read-only): one page of the caller's Decide lane through ListDecidePageAsync "
            + "with IHttpContextAccessor.HttpContext's User; the cursor is the only input."),
        // ── end task 022 ──
    };
}
