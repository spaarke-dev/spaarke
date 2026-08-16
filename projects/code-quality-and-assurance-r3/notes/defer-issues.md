# Deferred Work / Issues — code-quality-and-assurance-r3

> Two-write tracking (per `/project-defer-issue-tracking`): source of truth here + GitHub Issue on Epic #427.

| ID | Title | GitHub Issue | Status |
|----|-------|--------------|--------|
| DEF-1 | `TodoGenerationService` — 2 of 5 rules (Overdue events, Deadline) silently generate zero To Dos (event query → SDK silent-empty stub) | {URL — to file} | ROUTED → smart-todo-r5 |

## DEF-1 — event-sourced To Do generation silently broken (LATENT BUG → routed to smart-todo-r5)

**Discovered**: 2026-08-15 during the RED-4 Dataverse access-layer hardening investigation.
**Owner**: **smart-todo-r5** (the active To Do project) — see the detailed handoff
`projects/smart-todo-r5/notes/INBOUND-event-sourced-todo-generation-broken.md`.

**Defect**: `TodoGenerationService` outputs `sprk_todo` correctly (r3 decoupling), but **2 of its 5 rules —
"Overdue events" (`ProcessOverdueEventsAsync:322`, `QueryEventsAsync:334`) and "Deadline proximity"
(`ProcessDeadlineProximityAsync:~460`) — query `sprk_event` via the composite `IDataverseService`
(→ SDK `DataverseServiceClientImpl`), whose `QueryEventsAsync` is a silent-empty stub** (returns empty +
`LogWarning`; the real impl is on `DataverseWebApiService`, reached only via `IEventDataverseService`). So
those two rules always produce zero To Dos. Rules 2/4/5 (budget, invoices, tasks) use FetchXML via the SDK
and work.

**Architectural context (why it's a To Do-domain call)**: the r3 decoupling made To Dos independent records
(regarding via RegardingResolver, ADR-024) — no longer "part of Events." So the fix is a **decision**:
(A) event-sourcing still wanted → inject `IEventDataverseService` for Rules 1 & 3 (behavior change — validate
volume/dedupe before enabling); or (B) legacy → remove Rules 1 & 3. Small change either way; the decision is
the To Do team's.

**Coordination**: the `dataverse-access-hardening` stub→throw step is sequenced AFTER this resolves (else the
throw crashes the generator pass). Resolve in smart-todo-r5, then ping the hardening owner.

**Evidence**: `docs/architecture/DATAVERSE-ACCESS-LAYER-ROUTING.md` §Known traps #1;
`projects/smart-todo-r5/notes/INBOUND-event-sourced-todo-generation-broken.md`.
