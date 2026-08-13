# Task 055a — Job B apply-override endpoint (FR-E4 backend) — COMPLETE (2026-08-07)

**Rigor**: FULL · BFF · Opus 4.8. **Split from 055 (Option A, owner-approved 2026-08-07)**: land the apply-endpoint contract change on its own, reviewable, before the editable Fields tab (055) rides on it.
**Result**: BFF build 0 errors; apply seam 9/9 (6 existing + 3 new); Communication slice 1040/0. §10 verified. Step 9.5 + conflict-check clean.

## Why 055a exists
FR-E4 requires the reviewer to **edit the matched value before Accept**, and Accept writes the edited value. The shipped Job B apply endpoint (`POST /api/communications/proposals/{reviewLogId}/apply`) took **no request body** — `ApplyAsync(reviewLogId, caller, ct)` applied the *stored* AI value. So the client edit had nowhere to go. This task adds the override contract (mirroring the sibling create-task apply, which already takes `ApplyCreateTaskRequest`).

## What shipped (extends the EXISTING endpoint + service — no new endpoint/service/DI/package)
- **`ApplyProposalRequest(string? OverrideValue)`** DTO (optional body).
- **`ICommunicationProposalApplyService.ApplyAsync` 4-arg overload** `(reviewLogId, ApplyProposalRequest?, caller, ct)`; the 3-arg overload delegates with `request: null` → **existing callers/tests unchanged** (backward-compatible).
- **Override injection at step 7 only**: `effectiveValue = override ?? stored`. The human value flows through the **IDENTICAL guards** — caller resolution (403 fail-closed), allow-list re-validation (403), citation re-verify NFR-06 (422), fail-loud coercion (422), impersonated write (`MSCRMCallerID` = the confirming user). **An override changes only the VALUE — never a guard, never the write path.**
- **Audit**: an actual override (non-empty + differs from the stored value) writes a `Overriden` audit row (action 100000003, already defined) instead of `Applied`; the stored `sprk_aisuggestion` is augmented with `appliedValue` + `overridden:true` so the row is self-contained (AI proposed X / human applied Y). An override equal to the stored value is treated as a plain `Applied`.
- **Endpoint**: `ApplyProposalAsync(reviewLogId, [FromBody] ApplyProposalRequest?, …)` → passes the body; `.Accepts<ApplyProposalRequest>`. Optional body ⇒ a no-body POST still applies the stored value (unchanged behavior).

## Tests (seam KEEP path — `tests/integration/seam/Communication/`)
3 new + 6 existing (unchanged, backward-compat proof): override applies the human value + `Overriden` audit + self-contained `appliedValue`; override == proposed → plain `Applied`; **override does NOT bypass the allow-list (403, never writes)** — the correctness guard.

## §10 BFF hygiene
- Publish size: **~47 MB compressed incl PDBs** (≤60 MB ceiling; baseline ~49.63) — **no delta** (no assemblies/packages added, only recompiled existing DLLs).
- CVE: `dotnet list package --vulnerable --include-transitive` → **no vulnerable packages**.
- Placement: extends the existing apply endpoint + service; reuses the blessed `IActionSeam` write core + `EmailUpdateFieldCoercion`/`CitationVerifier`; no new CRUD→AI dependency (ADR-013 unchanged).
- Contended `Services/Communication`: conflict-check clean (no open PR overlap; all 6 contending Communication worktrees clean on `CommunicationEndpoints.cs` + `CommunicationProposalApplyService.cs`).

## Next
055 (frontend Fields reconcile tab) consumes this: the edited value → `POST …/apply` body `{ overrideValue }`. Deploy of the endpoint rides Pillar-C/D BFF deploys (026/035, paused) or Pillar-E deploy (059).
