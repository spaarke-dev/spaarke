namespace Spaarke.ArchTests;

/// <summary>
/// The MCP tool ledger, Tasks area (spaarkeai-mcp-bff-api-r2 task 020): the rows of <c>McpTools</c> for this area's tools. The task
/// that adds a tool adds its row here, in the same diff. The rules, the guard and the concatenation of every area's rows live in
/// <c>RouteAuthorizationGuardTests.Mcp.cs</c>, so parallel tool tasks never edit one list.
/// </summary>
public partial class RouteAuthorizationGuardTests
{
    // Task 025 (FR-07 read half): spaarke_list_tasks and spaarke_get_task, both read-only and model-visible.
    // Task 036 (FR-07 write half): spaarke_create_task and spaarke_complete_task, model-callable reversible writes.
    private static readonly IReadOnlyList<McpTool> McpToolsTasks = new[]
    {
        new McpTool("ListTasksTool", "", "IEventsCapability",
            "FR-07, read-only: the caller's tasks (events) through IEventsCapability.ListAsync with exactly IHttpContextAccessor.HttpContext: "
            + "the capability runs the list impersonated AS the caller, narrowed to my events, and fails closed with no caller or no Dataverse "
            + "user (the route's own core). The tool adds no filter and no check. Bare [Authorize]."),
        new McpTool("GetTaskTool", "", "IEventsCapability",
            "FR-07, read-only: one task (event) through IEventsCapability.GetAsync with exactly IHttpContextAccessor.HttpContext: the "
            + "capability asks the caller's Read on the event (RecordRouteAccessAuthorizationFilter.DecideRouteRecordAsync, the route filter's "
            + "decision); no Read and an absent event are one not-found. The tool adds no check. Bare [Authorize]."),
        new McpTool("CreateTaskTool", "", "IEventsCapability",
            "FR-07, reversible write: one task (event) through IEventsCapability.CreateAsync with exactly IHttpContextAccessor.HttpContext: the "
            + "capability asks, AS THE CALLER, the Create privilege on sprk_event and Read + AppendTo on the regarding record "
            + "(event.attach_regarding, #3181), validates the shape with the route's own rules, then makes the one create "
            + "(RecordCreationService). The tool adds no check (argument shape and length caps only); its idempotencyKey is per caller "
            + "(McpIdempotency). Bare [Authorize]."),
        new McpTool("CompleteTaskTool", "", "IEventsCapability",
            "FR-07, reversible write: one task (event) through IEventsCapability.CompleteAsync with exactly IHttpContextAccessor.HttpContext: "
            + "the capability asks the caller's Write on the event (RecordRouteAccessAuthorizationFilter.DecideRouteRecordAsync, the route "
            + "filter's decision); no Read and an absent event are one not-found. An already-completed task is answered as success after "
            + "reading it back through IEventsCapability.GetAsync (the caller's Read); nothing is written twice. The tool adds no check; "
            + "its idempotencyKey is per caller (McpIdempotency). Bare [Authorize]."),
    };
}
