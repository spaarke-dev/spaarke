# Task 020 — G1 Origin Routing — Deviations & Notes

> **Status**: ✅ COMPLETE · 2026-07-29 · FULL rigor · sonnet/high (executed on Opus session — meets tier)
> **Commits**: `658818601` (impl) + seam tests (this task) + merge `5b81a8ea4` (sync master-with-email-r5)

## What shipped

**Server** (`ComposeService.cs`, `IComposeService.cs`, `ComposeEndpoints.cs`):
- `ComposeOrigin` enum — `Authored=100000000`, `Imported=100000001` (as-built, notes/g1-origin-field-asbuilt.md).
- `LoadAsync` reads `sprk_composeorigin` via `IGenericEntityService.RetrieveAsync` on Path A (existing record); best-effort (read failure / legacy row → `null`, never fails Load).
- `SaveAsync` resolves origin from the **`request.ContentModel is not null`** discriminant (the SAME signal the born-in-editor render branch already uses) — never SPE-id/content inference (NFR-02/I-7).
- Origin **persisted onto `sprk_composeorigin` ONLY at create-on-save** (`PromoteIfEphemeralAsync`); a replace-path save reports but does not mutate it.
- `origin` surfaced on `LoadComposeDocumentResponse` + `SaveComposeDocumentResponse` (trailing/optional → backward compatible).

**Client** (`compose-contracts.ts`, `ComposeWorkspace.types.ts`, `ComposeWorkspace.tsx`):
- `ComposeDocumentOrigin` mirror type; `origin` in workspace state + reducer (loadSucceeded / saveSucceeded / mountTransient / AI-seed).
- `routeClean = bornInEditor || (state.origin === 'authored')` → a reopened authored doc takes the clean contentModel payload, not the op-log/tracked path. Imported/null stay tracked (REQ-2 not regressed).

**Tests** — `tests/integration/seam/Compose/ComposeOriginRoutingSeamTests.cs` (6 through-the-wire slices, ADR-038 KEEP path): authored/imported/legacy-null load reads; Path-B no-inference negative (`RetrieveAsync` `Times.Never`); imported save stays tracked (w:ins survives); born-in-editor create-on-save resolves Authored + persists `OptionSetValue(100000000)` on the new row. **6/6 green.**

## Verification
- Seam: **6/6** new + **274/274** Compose seam/unit suite (incl. corpus byte-diff harness — no regression, NFR-01).
- Publish: **46.75 MB compressed** vs 46.70 baseline → **+0.05 MB** (≤60 ceiling; under +5 threshold). **Zero new runtime package.**
- BFF build: 0 errors. Quality gates: adr-check **clean** (8 compliant / 0 violations); code-review **PASS** (0 critical / 0 warning).
- `/conflict-check`: only `projects/INDEX.md` overlaps (PR #694, registry row — resolved in merge); no source conflict.

## Deviations / decisions
1. **Scope boundary (per POML notes):** this task guarantees a reopened authored doc **lands on the clean payload (renderer)**. The engine **clean-apply mode** (fidelity of that re-author) is **task 021 (G2)**. Code comments mark the seam as "ORIGIN PLUMBING for task 021." Not a gap — the intended split.
2. **Client jest suite not run in this worktree:** 13/14 client suites fail on `Cannot find module '@spaarke/auth'` — the monorepo workspace siblings aren't linked in this isolated worktree checkout (known env limitation, root CLAUDE.md §12; identical on master, **not** caused by this change). The binding DoD is the **server seam** (green). The client change is type-only additions + one boolean branch extending the existing `bornInEditor` selection; standalone `tsc` shows **no new** error at the edited lines (all reported errors are pre-existing `@spaarke/*` resolution + implicit-any outside my hunks).
3. **Code-review suggestions (both optional/deferred, non-blocking):**
   - (a) `(ComposeOrigin)originOptionSet.Value` casts any int to the enum; Dataverse constrains the choice field so an out-of-range value can't occur in practice. A defensive "unknown → Imported" would match the documented invariant exactly. Left as-is.
   - (b) Behavioral watch for **task 042 UAT**: confirm a reopened authored doc's content survives the renderer path (fidelity is task 021's deliverable).

## PR obligations (carry to the BFF PR)
- **Placement Justification (§10):** origin read/write stays in `Services/Compose/ComposeService.cs`; extends existing save/load orchestration + `IGenericEntityService` — no new component (root §11).
- Run `/conflict-check` before the PR (overlaps compose-r1/r2/r3 + ai-architecture-redesign-r2); watch #266 (OpenXml 3.5.1 — re-run byte-diff if it merges first).
