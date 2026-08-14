# Task 055b — Job B dismiss endpoint (FR-E4 "Reject" backend) — COMPLETE (2026-08-07)

**Rigor**: FULL · BFF · Opus 4.8. **Split from 055 (Option A, owner-approved 2026-08-07)** — exactly mirroring the 055a split: land the "Reject = terminal-dismiss" apply-contract change on its own, reviewable, before the Fields tab (055) rides on it.
**Result**: BFF build 0 errors; apply/dismiss seam 13/13 (9 existing 055a + 4 new); §10 verified; Step 9.5 clean (see ArchTest note below); /conflict-check clean.

## Why 055b exists
055's `Reject → terminal-dismiss` had **no backend**. `ReviewActionDismissed (100000004)` was a *recognized* terminal state — the apply guard 409s on it and the queue-feed closes proposals that carry a Dismissed row — but **nothing wrote it**. There was no `POST …/proposals/{reviewLogId}/dismiss`. So a frontend "Reject" had nowhere to persist; a rejected proposal would reappear on next queue-feed load, failing AC #2 ("Reject terminal-dismisses"). This is the same gap-shape 055a fixed for override. (Hold = "leave Proposed" is correctly a client-only no-op — it writes nothing and the proposal deliberately reappears; only Reject needed a backend.)

## What shipped (extends the EXISTING endpoint group + service — no new endpoint group/service/DI/package)
- **`ICommunicationProposalApplyService.DismissAsync(reviewLogId, caller, ct)`** + **`DismissProposalResult(ReviewLogId, AuditLogId, TargetEntity, TargetField)`** (no `FieldsUpdated` — a dismiss makes no record change).
- **Reuses the apply path's exact guards**: caller resolution (403 fail-closed — a rejection is still an attributed human decision, never app-only), `LoadReviewLogRowAsync` (404), the Proposed-row check (409), and `EnsureStillOpenAsync` (409 idempotency — a proposal already applied/dismissed/superseded is not the open pending row).
- **Deliberately does NOT** re-validate the allow-list, re-verify the citation (NFR-06), coerce a value, or write the target record. A rejection is a decision *not* to write — safe regardless of allow-list or citation drift (indeed drift is a reason to reject). This is the property that distinguishes dismiss from apply, and it is asserted by a test.
- **Audit**: `WriteDismissedAuditRowAsync` writes EXACTLY ONE append-only `Dismissed` row (actor = the rejecting human), carrying the AI suggestion forward so the row is self-contained (what the AI proposed / that a human rejected it). `sprk_targetrecordid` is included only when the proposal carried a parseable one.
- **No try/catch around the audit write** (unlike apply): apply wraps because a record was *already mutated* (mutate-without-audit protection → 500 + Critical log). Dismiss has no prior mutation — if the single `CreateAsync` fails, the exception propagates cleanly as 500 via middleware and nothing was mutated. A plain throw is the correct, simpler design, not a gap.
- **Endpoint**: `POST /api/communications/proposals/{reviewLogId}/dismiss` (no body) → `DismissProposalAsync` → `applyService.DismissAsync(reviewLogId, context.User, ct)`. Same `AddEndpointFilter<CommunicationAuthorizationFilter>()` + `Produces<…>` decorations as the sibling apply endpoint; registered unconditionally (ADR-010/032).

## Tests (seam KEEP path — `tests/integration/seam/Communication/`)
4 new (13 total in the file): dismiss writes ONE Dismissed audit row + never writes the record; **dismiss STILL succeeds when the field is no longer allow-listed AND the cited text is gone** (the correctness differentiator — asserts the envelope reader is never even reached); unresolved caller → 403 never writes; already-resolved → 409 never writes. Boundary mocks only (ADR-038; no `Mock<HttpMessageHandler>`, no class-under-test collaborator mocking).

## §10 BFF hygiene
- Publish size: **47.06 MB compressed incl PDBs** (≤60 MB ceiling; = the 055a baseline — **no delta**, no assemblies/packages added, only recompiled existing DLLs).
- CVE: `dotnet list package --vulnerable --include-transitive` → **no vulnerable packages**.
- Placement: extends the existing apply endpoint group + `CommunicationProposalApplyService`; reuses its blessed guards + `IGenericEntityService` audit write. No new CRUD→AI dependency (ADR-013 facade unchanged — dismiss touches no AI type).
- **ArchTest (Step 9.5 adr-check)**: the assembly has 4 pre-existing NetArchTest failures (ADR-007 Graph isolation on `FileAccessEndpoints`/`Graph*Materializer`/Office ProblemDetails; ADR-010 single-impl interfaces incl. the Communication service family that exists for the ADR-032 Null-Object seam). **Verified pre-existing** by stashing 055b and re-running: identical 4 fail / 24 pass with and without 055b. 055b adds ZERO new violations (it added a *method* to the already-existing `ICommunicationProposalApplyService`, no new interface/Graph/DI/package).
- Contended `Services/Communication` + `Api/CommunicationEndpoints.cs`: /conflict-check clean (only "Communication" hit across 22 open PRs is PR #526's docs assessment file).

## Next
055 (frontend Fields reconcile tab) consumes BOTH backend endpoints: Accept → `POST …/apply` body `{ overrideValue }` (055a); Reject → `POST …/dismiss` (055b); Hold → no API call (leave Proposed). Deploy of both endpoints rides Pillar-C/D BFF deploys (026/035, paused) or Pillar-E deploy (059).
