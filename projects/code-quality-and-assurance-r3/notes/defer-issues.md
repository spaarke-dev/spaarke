# Deferred Work / Issues — code-quality-and-assurance-r3

> Two-write tracking (per `/project-defer-issue-tracking`): source of truth here + GitHub Issue on Epic #427.

| ID | Title | GitHub Issue | Status |
|----|-------|--------------|--------|
| DEF-1 | `TodoGenerationService` silently gets zero events (composite `IDataverseService` → SDK silent-empty stub) | {URL — to file} | OPEN |

## DEF-1 — TodoGenerationService silent-empty event query (LATENT BUG)

**Discovered**: 2026-08-15 during the RED-4 Dataverse access-layer hardening investigation.

**Defect**: `TodoGenerationService` injects the composite `IDataverseService`
(`Services/Workspace/TodoGenerationService.cs:213`, `GetRequiredService<IDataverseService>()`) which resolves
to `DataverseServiceClientImpl` (SDK). It then calls `QueryEventsAsync`
(`TodoGenerationService.cs:334`) — but that method is a **silent-empty stub** on the SDK impl
(`DataverseServiceClientImpl.cs` — returns `Array.Empty<EventEntity>()` + `LogWarning`; the real impl is on
`DataverseWebApiService`, reached only via `IEventDataverseService`). **Result**: the overdue-events pass of
the background todo generator **always sees zero events**, so it never generates todos from overdue
`sprk_event` records.

**Contrast**: `EventEndpoints` correctly injects `IEventDataverseService` (→ WebApi, real events). Only the
composite-injection consumers silently miss.

**Fix**: route `TodoGenerationService`'s event query through `IEventDataverseService` (→ WebApi). ⚠ This is a
**behavior change** — it starts returning real overdue events, so the todo-generation side effects (volume,
dedupe, notifications) must be validated before enabling. Belongs to the `dataverse-access-hardening` work
(convert the silent-empty stubs to throw only AFTER this reroute, else it crashes this path loudly).

**Evidence**: `docs/architecture/DATAVERSE-ACCESS-LAYER-ROUTING.md` §Known traps #1.
