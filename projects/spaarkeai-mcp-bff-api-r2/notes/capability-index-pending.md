# CAPABILITY-INDEX rows pending (main session applies to `.claude/patterns/CAPABILITY-INDEX.md`)

Sub-agents cannot write `.claude/` (root CLAUDE.md §3). Each task that adds a capability proposes its row here; the main session
copies it into the index (next free `#`) and marks it applied.

## Task 006 — events capability (status: applied as row 34, 2026-11-25)

| # | Capability | Synonyms | Canonical component | Symbol | Governing ADR | Extension point |
|---|---|---|---|---|---|---|
| (next) | List, get, create or complete an event (to-do / task) for the signed-in caller, from REST or an MCP tool | events, tasks, to-do, complete task, my events, event list, caller-scoped events | `src/server/api/Sprk.Bff.Api/Services/Events/EventsCapability.cs` | `IEventsCapability` (`ListAsync`, `GetAsync`, `CreateAsync`, `CompleteAsync`) | `.claude/adr/ADR-008-endpoint-filters.md`, `.claude/adr/ADR-028-spaarke-auth-architecture.md`, `.claude/adr/ADR-002-thin-plugins.md` | Inject `IEventsCapability` and pass the request's `HttpContext` (fails closed with `UserContextRequired`); map `EventCapabilityResult<T>.Status` / `ReasonCode` to the caller's contract (never render `Refusal.Detail` or `RefusedColumn`). Never call the static `*CoreAsync` cores: they run no gate (guard: `tests/Spaarke.ArchTests/EventsCapabilityCoreCallerGuardTests.cs`). Never copy its checks: the per-record decisions are `RecordRouteAccessAuthorizationFilter.DecideRouteRecordAsync` / `DecideDeclaredTargetAsync`. REST adapters: `src/server/api/Sprk.Bff.Api/Api/Events/EventEndpoints.cs` |

## Task 005 — durable run status (applied as row 35, 2026-11-25; source notes/005-schema.md §5)

## Task 020 — MCP tool plumbing (status: pending)

### CAPABILITY-INDEX row

| # | Capability | Synonyms | Canonical component | Symbol | Governing ADR | Extension point |
|---|---|---|---|---|---|---|
| (next) | Return an MCP tool's domain failure, or a long-running tool's start and status results (work over 30 s) | MCP error result, isError, reasonCode, start then status, runId, consoleLink, long-running tool, 30 second budget, FR-14 | `src/server/api/Sprk.Bff.Api/Api/Mcp/McpToolResults.cs`, `src/server/api/Sprk.Bff.Api/Api/Mcp/McpLongRunning.cs` | `McpToolResults.FromError(McpToolError, correlationId)`, `McpLongRunning.Started`, `McpLongRunning.Status`, `McpRunState`, `McpLongRunning.Budget` | `.claude/adr/ADR-019-problemdetails.md`, `.claude/adr/ADR-052-workload-placement.md`, `.claude/adr/ADR-013-ai-architecture.md` | Map a failure to `McpToolError` with the REST route's `reasonCode` and status (decision tools pass `DecisionToolError` to its overload). Work that can exceed `McpLongRunning.Budget` starts through its service and returns `Started(runId, Queued or Running, consoleLink, summary)`; a status tool returns `Status(runId, state, progress?, result?, summary?)`. The helper builds results only: no polling, storage or background work. `consoleLink` comes from an existing deep-link builder through the tool's public-contract seam (a tool cannot inject `HandoffUrlBuilder`, ADR-013). Recipe: `docs/guides/HOW-TO-ADD-AN-MCP-TOOL.md` Step 2 items 7-8 |

### `.claude/skills/mcp-tool-handler/SKILL.md` edits

1. Line 5 (`appliesTo`): add `"**/RouteAuthorizationGuardTests.Mcp.*.cs"` after `"**/RouteAuthorizationGuardTests.Mcp.cs"`, so the skill loads when a tool task edits its area's ledger partial.
2. Workflow step 4, replace with: `4. **Add the ledger entry** in your area's partial, `RouteAuthorizationGuardTests.Mcp.{Area}.cs` (`McpTools{Area}`: policy, caller seam, reason), never in `RouteAuthorizationGuardTests.Mcp.cs` (it concatenates the partials). The ArchTest fails on any tagged tool missing from every partial or listed twice.`
3. Workflow step 3, append: `Errors: `McpToolResults.FromError(new McpToolError(reasonCode, message, status, partial?), http?.TraceIdentifier)` with the REST route's `reasonCode`. Work that can take over 30 s (`McpLongRunning.Budget`): return `McpLongRunning.Started(...)` and add a status tool that returns `McpLongRunning.Status(...)` (FR-14).`
