# Task 074 — apply-template If-Match/ETag + ApiError-typed 404 (FR-12) — Item 1 DONE, Item 2 ESCALATED (§6.5)

> Phase 7 · sonnet@high · FULL rigor · 2026-08-17 · client-only (Item 1). Item 2 requires a shared-facade
> decision — escalated. No BFF bytes changed this task.

## Step 0 — file-touch confirmation (escalation trigger #1)

R7 **still owns** the apply-template path: `handleApplyTemplate` (`ComposeWorkspace.tsx` ~2062) + the server
`ApplyTemplate` endpoint (`ComposeEndpoints.cs` ~1701) + `ComposeService.ApplyTemplateAsync` (~372) are all
present and wired. The r8 template split did NOT move this path. Trigger #1 does not fire — proceed as
in-R7 hardening (not standalone, not moved to r8).

## Item 1 — ApiError-typed 404, replacing the dead `response.ok` idiom — ✅ DONE (client-only)

**Confirmed dead idiom**: `authenticatedFetch` (`@spaarke/auth/authenticatedFetch.ts`) THROWS a typed
`ApiError` (`.status` + `.problemDetails`) on any non-2xx/non-401 and only RETURNS on `response.ok`
(lines 45-46 / 70-76). So the apply handler's `if (!response.ok) { … }` block was **unreachable** — the
throw happens first. The same fact is already documented at the memo/draft handlers in this very file
(e.g. ~2635/2709 "authenticatedFetch throws ApiError on non-2xx — never returns a non-ok Response") and the
typed-404 pattern is already used at ~2713 (`err instanceof ApiError && err.status === 404`). The apply
handler simply missed it.

**Fix**: removed the dead `if (!response.ok)` block; the `catch (err)` now branches on
`err instanceof ApiError` → `err.status === 404` surfaces the "template not found" copy (from
`problemDetails.detail`/`title`), else a typed `HTTP {status}` message; a non-ApiError keeps the generic
fallback. This is a 1:1 application of the shipped, tested pattern (ADR-019 ProblemDetails/ApiError).

- **tsc**: the standalone run shows +4 `TS18046 'err' is of type unknown` at the new lines — the KNOWN
  `@spaarke/*`-unresolved artifact (standalone tsc can't resolve `ApiError`, so `err instanceof ApiError`
  doesn't narrow). The pre-existing catch blocks at ~2418/2445/2713 show the identical artifact and are
  shipped, CI-passing code; in CI (workspaces linked) `ApiError` resolves and the narrow is valid.
- **jest**: 650/0 (unchanged — the handler is CI-only). The typed-404 behavior mirrors the proven
  `compose-outputs-404` ApiError path; a handler-level mount test is CI-only.

## Item 2 — If-Match/ETag on the apply-template replace — 🔔 ESCALATED (CLAUDE.md §6.5)

Investigating the real TOCTOU surfaced a conflict between the POML's premise and the SHIPPED, DELIBERATE
ComposeService concurrency architecture. Escalating rather than shipping a no-op or unilaterally expanding
into a shared facade.

**The genuine TOCTOU is server-side, inside `ApplyTemplateAsync`** (`ComposeService.cs` 372-475):
download CURRENT bytes (T1) → merge template → **blind `ReplaceFileContentAsUserAsync` (T2) with NO
If-Match**. A concurrent sibling-tab save landing between T1 and T2 is overwritten by the merged-T1 write
(the prior version is retrievable via SPE version history — the "FR-07 safety net", 436-437 — so it is a
head-version clobber, recoverable, not an unrecoverable data loss).

**The conflict**: the POML asks to "add If-Match/ETag" (implying a client-supplied ETag). But ComposeService
**deliberately rejects client-supplied preconditions**:
- 1177-1184: *"Every save … fetches the LIVE SPE eTag (never a client-supplied precondition — the client
  cannot assert its own currency), and asserts it [against the persisted stamp] … INSTEAD of blindly
  overwriting or throwing an eTag 500. AUTO … re-anchors apply …"*
- 1482-1485: *"Upgrading this write to a Graph-level If-Match precondition is a further-hardening candidate,
  **not required** by this behavior + write-path signature."*

So a client-supplied `If-Match` header on the apply POST would be **ignored by the server** (no protection —
misleading to ship) and would introduce a concurrency model INCONSISTENT with the save path.

**The architecture-consistent fix** (server-side If-Match using the SERVER's read-time eTag — the server
asserting its own read's currency, which the 1482-1485 comment names as the candidate) requires:
1. A metadata read at T1 to capture the eTag (the save path already does this at ~1196).
2. Adding an optional `ifMatch` param to **`SpeFileStore.ReplaceFileContentAsUserAsync`** — a SHARED
   ADR-007 facade used across many endpoints — so the Graph PUT sends `If-Match` and returns 412 on a
   between-read-and-write change.
3. `ApplyTemplateAsync` passing the read eTag; the endpoint catching 412 → typed ProblemDetails.
4. Client catching the typed 412 → "the document changed in another tab; reload and re-apply."
5. Test ripple across `SpeFileStore` mocks (task-013-class ripple).

That is a shared-facade BFF change on a hot-path (§10 gates: Placement Justification, publish ≤60 MB vs
44.96, CVE, /conflict-check) with real blast radius — well beyond this task's "bounded, sonnet@high, ~2h"
scope, and it revises a deliberately-deferred design point. Per §6.5 + the "autonomous where SAFE" rule,
this is a decision for the owner, not a silent expansion.

### 🔔 ADR/Design Conflict — Resolution Required (for the owner)

- **In question**: ComposeService concurrency model (client-If-Match rejection, 1177-1184) + the
  deferred-by-design Graph If-Match precondition (1482-1485).
- **Rule challenged**: apply-template's replace has no write-time precondition → a server-side read→write
  TOCTOU vs a concurrent save (mitigated by SPE version history + the client's non-dirty/non-transient
  apply guard).
- **Options**:
  - **Path A (documented exception — RECOMMENDED for R7)**: accept that apply-template rides the SAME
    server-side model as save (no client If-Match, per the deliberate design), document the residual TOCTOU
    + its existing mitigations (SPE version history retains the clobbered save; apply is guarded to a
    saved, non-dirty doc), and DEFER the server-side If-Match hardening to a scoped follow-up. Item 1 (the
    typed-404) still ships. Lowest risk; consistent with the shipped architecture.
  - **Path B/C (implement the server-side If-Match)**: do the 5-part change above (shared SpeFileStore
    facade + ComposeService + endpoint + client + test ripple) under §10 BFF gates. Correct + complete, but
    a materially larger BFF change than the task scoped, touching a shared ADR-007 facade.
- **Recommendation**: **Path A for R7** — ship Item 1 now, file the server-side If-Match as a scoped
  BFF-hardening follow-up (via `/defer`), and keep R7's UX-layer framing. The residual risk is a
  recoverable head-clobber on a rare concurrent-apply-vs-save race, already backstopped by SPE version
  history.

## Status

- **Item 1 (typed-404)**: DONE + committed. code-review PASS, adr-check PASS (ADR-019 typed ApiError; §11
  modify-only; NFR-06 intact; no BFF bytes).
- **Item 2 (If-Match)**: awaiting owner decision (Path A vs B/C). 074 is NOT marked ✅ until resolved.
- Scope did NOT expand into r8 template storage/picker.
