# Task 056b — Job C ad-hoc create-task endpoint (FR-E5 "+ New task" backend) — COMPLETE (2026-08-07)

**Rigor**: FULL · BFF · Opus 4.8. **Split from 056 (Option A, owner-approved 2026-08-07)** — mirrors the 055b split: land the ad-hoc create contract on its own, reviewable, before the Tasks reconcile tab (056) rides on it.
**Result**: BFF build 0 errors; create-task apply/ad-hoc seam **15/15** (9 existing + 6 new); §10 verified; Step 9.5 clean; /conflict-check clean.

## Why 056b exists
056's ad-hoc **"+ New task"** (a task the reviewer authors, NOT proposed by the engine) had no backend. The only create-task route — `POST /proposals/{reviewLogId}/create-task/apply` (task 034) — is **proposal-keyed**: it needs an existing `sprk_emailreviewlog` Proposed row + re-verifies the extracted citation. An ad-hoc task has neither. So the POML's "+ New task ... via the SAME create-task path — no second create mechanism" (AC #3) had nowhere to land. Same gap-shape as 055b.

## What shipped (extends the EXISTING service + endpoint group — no new service/DI/package)
- **`ICommunicationCreateTaskApplyService.CreateAdHocAsync(communicationId, CreateAdHocTaskRequest, caller, ct)`** + **`CreateAdHocTaskRequest`** (subject + regarding REQUIRED; description/dates/status/assigned-to mirror the FR-E5 field set) + **`CreateAdHocTaskResult`**.
- **The SAME create-task path as an applied proposal**, minus the proposal bits: `IActionSeam.CreateTaskAsync` (app-only create — the facade exposes no impersonated create; ADR-013 forbids widening it, same as the apply path) → caller-**impersonated** FR-E5 PATCH (`sprk_basedate`/`sprk_finalduedate`/`sprk_completeddate`/`sprk_eventstatus`) → **ONE** append-only `Applied` audit row (actor = the confirming human). No proposal-load, no citation re-verify, no open-walk.
- **Shared PATCH builder**: refactored `BuildPatchMappings` into a field-based overload used by BOTH the applied-proposal path and the ad-hoc path — identical FR-E5 field set, written identically.
- **Create-and-complete inline**: `Status=Completed(2)` + `CompletedDate` flow through the same impersonated PATCH (no separate silent complete call — the ADR-015 escalation trigger is satisfied for ad-hoc as it is for the applied path).
- **Audit row** carries `kind:"create-task-adhoc"` + `adhoc:true` + the created task id + patched fields (self-contained), keyed by `sprk_targetfield = "__create_task__:adhoc"` so it categorizes with the create-task rows without colliding with any proposal's `(communication, entity, sentinel)` key.
- **Endpoint**: `POST /api/communications/{communicationId}/create-task` (route-distinct from the `/{id}/archive|status|...` verbs; same `CommunicationAuthorizationFilter`, which is auth-only/route-agnostic). Handler 400s a null body; the service 403/422/500s via `SdapProblemException` → RFC 7807.

## Guards / correctness
- Caller resolved server-side, **403 fail-closed** (never app-only) — a reviewer-authored task is still an attributed human decision.
- **422** on blank subject or missing regarding (NFR-10: an ad-hoc task must attach to the confirmed record — never created record-less).
- **ADR-015**: nothing finalizes without the explicit POST; a post-create FR-E5 PATCH failure is surfaced **loudly (422)** — never a silent dropped deadline field — while the create is still audited (create + failure in the one Applied row). Audit-write failure after create → **500** + Critical log (no mutate-without-audit).

## ⚠️ Known posture (flagged for reviewer — NOT a blocker)
The ad-hoc **regarding is client-supplied** (unlike the applied path, where it is server-derived from the stored proposal). The service does **not** re-check that the caller can see the regarding record — matching the applied-proposal sibling's documented posture ("association-confirmation is enforced upstream ... this service does not add a second association re-check"). NFR-10 is the UI's gate. Blast radius if a caller supplies a foreign regarding GUID: a task links to a record they may not see (visible to those who can) — **no field write to the target, no privilege escalation** (the create is app-only; the FR-E5 PATCH is impersonated/row-level-gated). **Potential hardening (follow-up):** an impersonated read of the regarding before create (403 if unreadable). Left consistent with the shipped apply path rather than inventing a stricter model for one endpoint; surfaced here so the reviewer can decide.

## Tests (seam KEEP path)
6 new (15 total in `CreateTaskApplySeamTests`): ad-hoc create + impersonated PATCH + one ad-hoc audit row (asserts NO proposal-load / envelope-read); **create-and-complete** (status=Completed + completed-date patched); unresolved caller → 403; blank subject → 422; missing regarding → 422; post-create PATCH failure → 422 but still audited. Boundary mocks only (ADR-038).

## §10 BFF hygiene
- Publish size: **47.06 MB compressed incl PDBs** (≤60 ceiling) — **no delta** (no assemblies/packages added).
- CVE: **no vulnerable packages**. ADR-013 facade unchanged. ArchTests **4/24 pre-existing** (verified no new — 056b added no interface; extended the already-flagged `ICommunicationCreateTaskApplyService`).
- /conflict-check clean (Communication surface; only PR #526 docs-file "Communication" hit).

## Next
056 (frontend Tasks reconcile tab) consumes: proposal Accept → `POST …/proposals/{reviewLogId}/create-task/apply` (034); ad-hoc "+ New task" → `POST …/{communicationId}/create-task` (056b); Reject → `POST …/proposals/{reviewLogId}/dismiss` (055b); Hold → no-write. Add 056b to the COORD-058-01 endpoint list.
