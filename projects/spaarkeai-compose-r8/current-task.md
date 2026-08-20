# Current Task State — spaarkeai-compose-r8

> **Last Updated**: 2026-08-19 (project initialized via `/project-pipeline`)
> **Recovery**: Read "Quick Recovery" first
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Git**: branch `work/spaarkeai-compose-r8` @ `a7874030d`, 0 behind master, clean

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **011** — Concurrency: last-writer-wins + warning (retire the 412), `If-Match` at the storage boundary |
| **Status** | not-started. Task **010 ✅ complete** (2026-08-20) — the client save-error contract now routes on `ApiError.status`. |
| **Next Action** | Begin Step 0 of task 011 (`tasks/011-concurrency-last-writer-wins.poml`). It supersedes the transitional 412 branch task 010 left in `ComposeWorkspace.tsx`'s save catch. |
| **Blocked on** | Nothing. Phase 2+ is gated on **PR #690** (Git-LFS corpus fixtures in CI) landing — owner decision 2026-08-19. Track S is NOT blocked. |

### Carried forward from task 010 (read before 011)

- The save catch in `ComposeWorkspace.tsx` holds a **transitional 412 branch**, marked as such inline.
  Task 011 retires it with the server-side refusal — do not build on it.
- **Escalation trigger fired, handed to task 016**: `ApiError.status` does not cover every failure class.
  `AuthError` (401 budget exhausted) carries `code`, not `status`; a `fetch` rejection never reaches an
  HTTP status. FR-S06's outcome contract needs a transport-failure member that is not a status code.
- **`Spaarke.Compose.Components` is not in CI** and ~39 of its 88 jest suites cannot run without a prior
  SharedLibs `dist/` build; the suite is also flaky under parallel workers. Task 017 / wrap-up material.
- New save-path tests must drive the **thrown** `ApiError` — never a resolved `{ok:false}`.

---

## Project shape

Six tracks, sequenced. **36 tasks / 9 phases** (re-cut 2026-08-20 by file-pass; all 36 POMLs written).

| Phase | Track | Status |
|---|---|---|
| 0 (001–002) | Coordination + PR #690 dependency | 🔲 |
| 1 (010–017) | **Track S — save reliability (P0, ships alone)** | 🔄 010 ✅ · 011–017 🔲 |
| 2 (020–023) | Oracle + corpus (measures today's loss as the control) | 🔲 |
| 3 (030–031) | **Model proof — THE GATE** | 🔲 |
| 4 (040–045) | Track A — faithful save *(POMLs provisional — amendable by 031)* | 🔲 |
| 5 (050–053) | Track C — AI edit placement | 🔲 |
| 6 (060–063) | Track B — durable session files *(only parallel-safe track)* | 🔲 |
| 7 (070–074) | Track D — god-class removal | 🔲 |
| 8 (090) | Wrap-up | 🔲 |

**Phase 4 does not start until Phase 3's gate passes.** A miss is an owner escalation, not an improvisation.

---

## Owner decisions on record (2026-08-19)

1. **Track S ships alone, immediately** — own PR + dev deploy ahead of everything.
2. **Capability gate = read-only + "Edit a copy"** — original never written to; user never blocked.
3. **Track D keeps all five files** — under 2,000 lines, all waivers deleted.
4. **Branch from master** — R7 confirmed fully merged and archived.
5. **Track C stays in R8**, P1, "MUST be completely addressed".
6. **Durable file bytes → blob** (infrastructure already provisioned).
7. **Concurrency = last-writer-wins with warning** (supersedes the 412 shipped 2026-08-18).
8. **PR #690 lands first**, then the gate is built.
9. **Initialize-only** — no auto-execution.

---

## Decisions / context not derivable from code

- The "fix fidelity and it will save" framing that shaped R5–R7 **inverts the causality**. R6 sacrificed
  fidelity *to protect* saves; the "content simplified" banners are the receipt for that trade.
- The prose-matching AI edit path is **R2-era code** that predates the R4 paraId anchor model and was never
  migrated — ADR-049 already specifies paraId-referencing operations.
- `ComposeShadowPatchEngine` was already demoted by R6 to the transitional path; R8 finishes that retirement.

---

## Files modified this task

_(task 011 not started — none yet)_

### Task 010 (complete) — files changed

| File | Purpose |
|---|---|
| `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx` | Deleted the unreachable `!response.ok` save block; added `classifySaveFailure` + `saveFailureMessage`; status routing in the save catch; `savePersisted` guard against reporting a post-2xx throw as a failed save |
| `.../widgets/ComposeWorkspace.saveErrorRouting.test.tsx` | NEW — 13 tests: every routed status + 4 negative cases |
| `.../widgets/ComposeWorkspace.saveOpLogPreservation.test.tsx` | 422 now THROWN, not resolved as `{ok:false}`; `virtual: true` mocks |
| `.../widgets/ComposeWorkspace.renderOnSave.test.tsx` | create-on-save failure mock converted to a thrown `ApiError` |
