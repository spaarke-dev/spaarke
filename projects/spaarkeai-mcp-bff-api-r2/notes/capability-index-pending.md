# CAPABILITY-INDEX rows pending (main session applies to `.claude/patterns/CAPABILITY-INDEX.md`)

Sub-agents cannot write `.claude/` (root CLAUDE.md §3). Each task that adds a capability proposes its row here; the main session
copies it into the index (next free `#`) and marks it applied.

## Task 006 — events capability (status: applied as row 34, 2026-11-25)

| # | Capability | Synonyms | Canonical component | Symbol | Governing ADR | Extension point |
|---|---|---|---|---|---|---|
| (next) | List, get, create or complete an event (to-do / task) for the signed-in caller, from REST or an MCP tool | events, tasks, to-do, complete task, my events, event list, caller-scoped events | `src/server/api/Sprk.Bff.Api/Services/Events/EventsCapability.cs` | `IEventsCapability` (`ListAsync`, `GetAsync`, `CreateAsync`, `CompleteAsync`) | `.claude/adr/ADR-008-endpoint-filters.md`, `.claude/adr/ADR-028-spaarke-auth-architecture.md`, `.claude/adr/ADR-002-thin-plugins.md` | Inject `IEventsCapability` and pass the request's `HttpContext` (fails closed with `UserContextRequired`); map `EventCapabilityResult<T>.Status` / `ReasonCode` to the caller's contract (never render `Refusal.Detail` or `RefusedColumn`). Never call the static `*CoreAsync` cores: they run no gate (guard: `tests/Spaarke.ArchTests/EventsCapabilityCoreCallerGuardTests.cs`). Never copy its checks: the per-record decisions are `RecordRouteAccessAuthorizationFilter.DecideRouteRecordAsync` / `DecideDeclaredTargetAsync`. REST adapters: `src/server/api/Sprk.Bff.Api/Api/Events/EventEndpoints.cs` |

## Task 005 — durable run status (applied as row 35, 2026-11-25; source notes/005-schema.md §5)
