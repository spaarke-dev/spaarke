# Task 034 — Job C apply endpoint + create-task queue-feed discriminator (COMPLETE)

> **FR-D5** (backs FR-E5). opus·high. Rigor FULL. Completed 2026-08-06.
> Sibling of the Job B apply (`CommunicationProposalApplyService`). Facade UNCHANGED (ADR-013).

## What shipped

1. **`QueueFeedItemKinds.CreateTask = "create-task"`** + 8 nullable `Task*` fields on `QueueFeedItem`
   (`TaskName`/`TaskDescription`/`TaskBaseDate`/`TaskDueDate`/`TaskFinalDueDate`/`TaskAssignedTo`/`TaskStatus`/`TaskCompletedDate`).
   Only name/description/due are extracted by Job C; the rest are null in the feed and supplied by the human at
   apply time (task 056).
2. **`CommunicationQueueFeedService`** — discriminates the `__create_task__:` sentinel `sprk_targetfield` (mirrored
   const, matching how the option-set ints are mirrored) inside the SAME open/closed walk, emitting a `create-task`
   item (regarding = `sprk_targetentity`/`sprk_targetrecordid`) instead of a `pending-proposal`. Ranking unchanged
   (D-08 — parent communication's triage priority + RI-confidence).
3. **NEW `CommunicationCreateTaskApplyService` + `POST /api/communications/proposals/{reviewLogId}/create-task/apply`**
   — mirrors the Job B 9-step apply contract (caller-resolve→403, load→404, not-create-task/malformed/citation→422,
   pending→409, still-open walk→409), then **Path B**: create the `sprk_event` via `IActionSeam.CreateTaskAsync`,
   PATCH the FR-E5 fields via impersonated `IActionSeam.UpdateRecordAsync`, one append-only Applied audit row.
4. **Unconditional DI** in `CommunicationModule` (ADR-010/032). Endpoint via the Map extension (ADR-001/008).

## Key decisions

### ADR-013 reconciliation — "create under impersonation" (criterion #3) vs. do-not-widen-the-facade

**Decision (Path B, honoring the operator's explicit "do NOT widen the facade" directive in the POML):** the two
writes are split by facade capability:

- **CREATE is app-only.** `CreateTaskRequest` carries no impersonation field, and ADR-013 + the POML forbid widening
  the facade. The facade exposes no impersonated create, so `CreateTaskAsync` runs app-only. The confirming human is
  attributed via `ownerid` (assigned-to, set at create), the impersonated PATCH (`modifiedby`), and the append-only
  Applied audit row (`sprk_actor` = the confirming user).
- **PATCH is impersonated.** `status`/`completed-date`/`base-date`/`final-due-date` are PATCHed via
  `UpdateRecordAsync` under `MSCRMCallerID` impersonation (the only facade method that supports it — added for Job B).

**Why this is a documented reconciliation, not a violation:** acceptance-criterion #3 (as literally written) says
"`CreateTaskAsync` is called under that caller's MSCRMCallerID impersonation." That is not satisfiable without
widening the facade, which the POML explicitly forbids ("Do NOT widen the facade (ADR-013) as a workaround"). So the
binding ADR-013 constraint wins over the criterion text; the reconciliation is: **app-only create + impersonated
audited PATCH under one audit row**. The POML escalation trigger did **NOT** fire — no deadline-bearing field is
dropped (`due` set at create; `base`/`final-due` at PATCH; a PATCH coercion failure is surfaced **loud** as 422
*after* the audit row is written, never silently) and the facade is not widened. Directional step-mode + the explicit
`ADR-013` constraint gave the authority to reconcile without stalling. The only residual gap is `createdby` = the BFF
app user (not the confirming human), which is the direct, documented consequence of the facade not exposing an
impersonated create.

**Reviewer note:** please confirm this Path B reconciliation at PR review (§6.5 Path A — documented project-scoped
reconciliation of a closed-set criterion forced by a binding ADR constraint; facade discipline is *honored*, not
bypassed).

### Apply request body

The stored Job C proposal carries only subject/description/dueDate/regarding/citation — the FR-E5 fields
(base-date/final-due-date/assigned-to/status/completed-date) are **not extracted**. So the apply endpoint accepts an
optional `ApplyCreateTaskRequest` body (the reconcile tab, task 056, collects the human's values). This is what makes
criterion #3's PATCH meaningful — without a body there is nothing to PATCH. `assigned-to` → `ownerid` at create (the
POML's explicit "Owner" mapping); the four PATCH fields go through impersonated `UpdateRecordAsync` as String
mappings (metadata-driven coercion: dates verbatim on the Date-Only columns, `sprk_eventstatus` fail-loud Choice).

### Test placement (ADR-038 deviation from the POML output list)

The POML listed a separate unit test file `CommunicationCreateTaskApplyServiceTests.cs`. Per ADR-038 + `tests/CLAUDE.md`
(the apply service crosses module boundaries: caller resolver / generic-entity seam / action seam / envelope reader),
and mirroring the Job B sibling (which is a **seam** test), the apply coverage lives in
`tests/integration/seam/Communication/CreateTaskApplySeamTests.cs` (a KEEP path). A separate unit file would mock the
same boundaries = duplication / B7-B15 antipattern. The queue-feed discriminator coverage extends the existing
`CommunicationQueueFeedServiceTests`. This is a directional-mode deviation, justified by ADR-038.

### NFR-10 association-confirmation

The task's regarding is the proposal's stored `sprk_targetentity`/`sprk_targetrecordid` (the association Job C
resolved). As with the Job B sibling, association-confirmation is enforced **upstream** (the queue-feed / reconcile
tab only offers a create-task apply for a communication whose association is resolved); this service adds no second
association re-check.

## Verification

- Build 0-err. **996 Communication tests green** (9 new create-task apply seam tests + 2 new queue-feed tests), 8
  pre-existing skips, 0 fail.
- Publish **47 MB compressed incl PDBs** (baseline 48.30 MB → Δ≈0; ≤60 MB ceiling). No new HIGH CVE. No new NuGet.
- `/adr-check`: 0 violations, 1 justified warning (ADR-010 single-impl interface — mirrors shipped sibling).
- `/code-review`: 0 critical, 2 justified warnings (interface + linear apply method, both Job B parity). Clean.

## Files

- `src/server/api/Sprk.Bff.Api/Services/Communication/Models/QueueFeedModels.cs` (modify)
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationQueueFeedService.cs` (modify)
- `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationCreateTaskApplyService.cs` (new)
- `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs` (modify)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/CommunicationModule.cs` (modify)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationQueueFeedServiceTests.cs` (extend)
- `tests/integration/seam/Communication/CreateTaskApplySeamTests.cs` (new)

## Downstream

- **Task 056** (Tasks reconcile tab) consumes the `create-task` queue-feed kind + POSTs to the create-task apply
  endpoint with the human-supplied FR-E5 fields.
- **Task 035** (Pillar D BFF deploy) — deps include 034.
