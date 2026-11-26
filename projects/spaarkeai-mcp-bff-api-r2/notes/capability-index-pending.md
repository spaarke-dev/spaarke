# CAPABILITY-INDEX rows pending (main session applies to `.claude/patterns/CAPABILITY-INDEX.md`)

Sub-agents cannot write `.claude/` (root CLAUDE.md §3). Each task that adds a capability proposes its row here; the main session
copies it into the index (next free `#`) and marks it applied.

## Task 006 — events capability (status: applied as row 34, 2026-11-25)

| # | Capability | Synonyms | Canonical component | Symbol | Governing ADR | Extension point |
|---|---|---|---|---|---|---|
| (next) | List, get, create or complete an event (to-do / task) for the signed-in caller, from REST or an MCP tool | events, tasks, to-do, complete task, my events, event list, caller-scoped events | `src/server/api/Sprk.Bff.Api/Services/Events/EventsCapability.cs` | `IEventsCapability` (`ListAsync`, `GetAsync`, `CreateAsync`, `CompleteAsync`) | `.claude/adr/ADR-008-endpoint-filters.md`, `.claude/adr/ADR-028-spaarke-auth-architecture.md`, `.claude/adr/ADR-002-thin-plugins.md` | Inject `IEventsCapability` and pass the request's `HttpContext` (fails closed with `UserContextRequired`); map `EventCapabilityResult<T>.Status` / `ReasonCode` to the caller's contract (never render `Refusal.Detail` or `RefusedColumn`). Never call the static `*CoreAsync` cores: they run no gate (guard: `tests/Spaarke.ArchTests/EventsCapabilityCoreCallerGuardTests.cs`). Never copy its checks: the per-record decisions are `RecordRouteAccessAuthorizationFilter.DecideRouteRecordAsync` / `DecideDeclaredTargetAsync`. REST adapters: `src/server/api/Sprk.Bff.Api/Api/Events/EventEndpoints.cs` |

## Task 005 — durable run status (applied as row 35, 2026-11-25; source notes/005-schema.md §5)

## Task 021 — shared widget MCP transport (status: pending)

| # | Capability | Synonyms | Canonical component | Symbol | Governing ADR | Extension point |
|---|---|---|---|---|---|---|
| (next) | Call a BFF MCP tool from an MCP App widget (Copilot `ui://spaarke/*.html`) through the host bridge, read the host context and theme, or run a shared `@spaarke/ui-components` service over MCP | widget transport, callTool, callServerTool, MCP Apps host, host theme, widget fetch shim, McpToolCaller, ext-apps | `src/client/widgets/shared/mcp/` (a source folder, not a package; `src/client/widgets/shared/README.md`) | `McpHostProvider` / `useMcpHost` (connection, host context, theme); `createWidgetMcpTransport` / `useWidgetMcpTransport` (`callTool` that never rejects and times out; structurally an `McpToolCaller`); `createMcpFetch` (REST-to-tool `ResponseFetchFn`) | `.claude/adr/ADR-012-shared-components.md` (placement: widget-sandbox-only source folder, not ui-components), `.claude/adr/ADR-028-spaarke-auth-architecture.md` (no token in a widget), `.claude/adr/ADR-021-fluent-design-system.md` (theme from the host) | Import by relative path; add `../shared` to the widget's tsconfig `include` and map its bare imports (README). Never copy `mcpFetch.ts` into a widget, and never call `fetch` or MSAL in a widget (guard: `src/client/widgets/shared/mcp/__tests__/widgetMcpTransport.test.ts`). Route tables and consumer titles are data: `createMcpFetch(callTool, routes, { title })`. Reference consumer: `src/client/widgets/spaarke-decisions/src/mcp/`. |
