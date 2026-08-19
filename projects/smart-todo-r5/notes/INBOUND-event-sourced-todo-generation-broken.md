# INBOUND (from r3 RED-4): event-sourced To Do generation is silently broken

> **From**: code-quality-and-assurance-r3 Dataverse access-layer hardening (RED-4) · **Date**: 2026-08-15
> **To**: smart-todo-r5 (active To Do project) — this needs a To Do-domain decision + fix.
> **Severity**: latent correctness bug (silent — no error, just missing To Dos).

## TL;DR

`TodoGenerationService` (the nightly background generator) has **5 generation rules**. **2 of them —
"Overdue events" and "Deadline within N days" — silently produce ZERO To Dos**, because they query
`sprk_event` through the wrong Dataverse interface and always get an empty result. The other 3 rules
(budget, invoices, assigned tasks) work. This intersects the r3 To Do decoupling, so it's yours to decide
+ fix.

## What's actually happening

`TodoGenerationService` correctly **outputs `sprk_todo`** records (the first-class model from
`smart-todo-decoupling-r3` D-1 — the old `sprk_event`+`sprk_todoflag` model was removed in r3 Phase 1). ✅
But two rules still **read `sprk_event` as a source**, and they read it wrong:

| Rule | Method | Source query | Status |
|---|---|---|---|
| 1 — Overdue events | `ProcessOverdueEventsAsync` (`TodoGenerationService.cs:322`) | `_dataverse!.QueryEventsAsync` (`:334`) | **🐞 always empty** |
| 3 — Deadline proximity | `ProcessDeadlineProximityAsync` (`:~460`) | `QueryEventsAsync` | **🐞 always empty** |
| 2 — Budget >85% | `QueryMattersOverBudgetAsync` (`:789`) | FetchXML via SDK `OrganizationService` | ✅ works |
| 4 — Pending invoices | `QueryPendingInvoicesAsync` (`:824`) | FetchXML | ✅ works |
| 5 — Assigned tasks | `QueryAssignedTasksAsync` (`:858`) | FetchXML | ✅ works |

**Root cause**: `_dataverse` is the composite `IDataverseService` (`:213`), which resolves to the SDK impl
`DataverseServiceClientImpl`. On that impl, `QueryEventsAsync` is a **silent-empty stub** — it returns
`Array.Empty<EventEntity>()` + a `LogWarning`. The *real* event query lives on `DataverseWebApiService`,
reachable only via **`IEventDataverseService`**. (For contrast, `EventEndpoints` injects
`IEventDataverseService` and gets real events — so the events data itself is fine; only TodoGeneration
mis-routes.) Full routing map: `docs/architecture/DATAVERSE-ACCESS-LAYER-ROUTING.md`.

## Why this is a To Do-domain decision (not just a routing fix)

The r3 decoupling made To Dos **independent records** with regarding-to via RegardingResolver (ADR-024) —
they are no longer "part of Events." So the real question is: **in the current model, should the nightly
generator still create To Dos FROM overdue/upcoming `sprk_event` records?**

- **Option A — event-sourcing is still wanted** (events remain a legitimate trigger for a To Do, regarding
  the `sprk_event`): **fix the routing** — inject `IEventDataverseService` into `TodoGenerationService` and
  use it for Rules 1 & 3. ⚠ **Behavior change**: those two rules start creating real To Dos on the next run —
  validate volume, the same-name-not-Dismissed dedupe (`:97`), and any notification side effects before
  enabling. Recommend gating the first run / dry-run count.
- **Option B — event-sourcing is legacy** (a remnant of the pre-decoupling design that should have been
  dropped): **remove Rules 1 & 3** from `TodoGenerationService` (and the `DeadlineWindowDays` option),
  leaving budget/invoice/task generation.

Either is a small change; the **decision** is the To Do team's, since it's about intended To Do behavior.

## ⏱ Coordination — please resolve before the hardening lands

The r3 `dataverse-access-hardening` will **convert the silent-empty SDK stubs to `throw`** (so future
mis-routes fail loudly instead of silently). That change is **sequenced AFTER this is resolved** — if the
stubs start throwing while Rules 1 & 3 still call them via the composite, the nightly generator will crash
that pass. So:
- **Option A** → do the reroute first; then the stub can safely throw.
- **Option B** → remove the rules first; then the stub can safely throw.

Tracked as **DEF-1** in `projects/code-quality-and-assurance-r3/notes/defer-issues.md` (file a GitHub issue
on Epic #427). Ping the r3 hardening owner when resolved so the stub→throw step can proceed.

## Evidence / pointers

- `src/server/api/Sprk.Bff.Api/Services/Workspace/TodoGenerationService.cs:213,272-288,322-350,~460`
- `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` (QueryEventsAsync silent-empty stub)
- `docs/architecture/DATAVERSE-ACCESS-LAYER-ROUTING.md` (routing map + trap #1)
- `projects/code-quality-and-assurance-r3/notes/red-item-analyses/RED-4-dataverse-two-stack-ASSESSMENT.md`
