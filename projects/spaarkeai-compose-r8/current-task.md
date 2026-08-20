# Current Task State — spaarkeai-compose-r8

> **Last Updated**: 2026-08-19 (project initialized via `/project-pipeline`)
> **Recovery**: Read "Quick Recovery" first
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Git**: branch `work/spaarkeai-compose-r8` @ `a7874030d`, 0 behind master, clean

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **012** — Save lifecycle hardening (dirty flag survives a failed POST · timeout + `AbortSignal` + in-flight guard · working 423 recovery) |
| **Status** | not-started. |
| **Next Action** | Begin Step 0 of task 012 (`tasks/012-save-lifecycle-hardening.poml`). Also startable: **014**, **015**, **016** (015/016 unblocked by 013), **018**. |
| **Blocked on** | Nothing. |
| **Complete** | **001 ✅ · 002 ✅ · 010 ✅ · 011 ✅ · 013 ✅ · 050 ✅** (all 2026-08-20) |

### Carried forward from task 011 (read before 012 / 013)

- Every EXISTING-item save now takes the **6-arg `ReplaceFileContentAsUserAsync`** (with `If-Match`);
  the etag-less overload serves only saves with no resolved version. **A new save-path test that mocks
  the 5-arg overload will fail with a misleading 404** ("drive-item not found or could not be written").
- **The save route returns no 412.** A sustained concurrent writer is **409 Conflict** after one rebase
  retry, and task 013 mapped it to **`refused-stale`** — correcting the guess left here after task 011
  (which said `storage-failed` and that `refused-stale` had no producer). It is a refusal on staleness
  grounds; nothing in storage failed.
- The concurrency warning is `concurrent-external-change`, carried on `degradationWarnings` and rendered
  as its own banner row (`compose-workspace-concurrency-banner`).

### Phase-0 results (do not re-derive)

- **PR #690 is REDUNDANT — GO for Phase 2.** It never merged, but the `lfs: true` line it proposes is
  already on master via commit `f7ec5b928` (2026-08-12, from `email-communication-intelligence-r2`),
  confirmed an ancestor of this worktree. All 10 corpus `.docx` resolve to real bytes (2,666–27,986 B);
  Compose seam tests 101/101 green on master run `32313454003`. **Recommend closing #690 as superseded.**
- **Publish baseline: 44.97 MB incl. PDBs / 44.07 MB excl.** (+0.01 vs the 44.96 MB reference), TFM
  `net10.0`, SDK 10.0.101, at commit `b182f1687`. Far below the 55 MB review threshold.
- **`/conflict-check`: clean** across 24 open PRs and every other worktree, scoped to the full Compose spine.
- **PR #266 (OpenXml 3.4.1→3.5.1): HOLD** until after task 031. Keeps the Phase-2 control (023) and the
  Phase-3 gate (030) on the same serializer so any drift stays attributable.

### Task 050 results — binding on Track C (051–053)

**ADR-043 orthogonal (Path C stands)**; **FR-C05 is NOT an ADR-041 Gate**; **FR-A07 is out of ADR-041**
(shape it via FR-A07 + ADR-050, task 043); **ADR-013 clean**. Full reasoning + citations:
[`notes/adr-043-041-assessment.md`](notes/adr-043-041-assessment.md). Constraints **C-1…C-7** there are
binding — the two most consequential:

- **C-1** — do NOT extend `ContextBinder`'s closed operand vocabulary. The client **already sends**
  `selectionAnchorStart`/`selectionAnchorEnd`/`targetParaId`, so FR-C01 is a *response + apply* change,
  not a new request channel.
- **C-5** — FR-C05's re-ask hazard is **live today** (reopen re-materializes the highest-turn edit; the
  guard is React state that dies on refresh; accept writes nothing to the ledger). Fix via ADR-040
  residency, obligations O-1…O-6 — not via the gate engine.

### Carried forward from task 010 (read before 011)

- The save catch in `ComposeWorkspace.tsx` holds a **transitional 412 branch**, marked as such inline.
  Task 011 retires it with the server-side refusal — do not build on it.
- **Escalation trigger fired, handed to task 016**: `ApiError.status` does not cover every failure class.
  `AuthError` (401 budget exhausted) carries `code`, not `status`; a `fetch` rejection never reaches an
  HTTP status. FR-S06's outcome contract needs a transport-failure member that is not a status code.
- **`Spaarke.Compose.Components` is not in CI** and ~39 of its 88 jest suites cannot run without a prior
  SharedLibs `dist/` build; the suite is also flaky under parallel workers (2 / 3 / 14 failures across
  identical runs). → **task 018**, added 2026-08-20 by owner decision; runs BEFORE 017 so Track S ships
  with a gate on the client save contract. Only two CI jobs are actually blocking today (`eval-gate`,
  `compose-fidelity-gate`) and both cover the server half.
- New save-path tests must drive the **thrown** `ApiError` — never a resolved `{ok:false}`.

---

## Project shape

Six tracks, sequenced. **36 tasks / 9 phases** (re-cut 2026-08-20 by file-pass; all 36 POMLs written).

| Phase | Track | Status |
|---|---|---|
| 0 (001–002) | Coordination + PR #690 dependency | ✅ |
| 1 (010–018) | **Track S — save reliability (P0, ships alone)** | 🔄 010 ✅ · 011 ✅ · 013 ✅ · 012, 014–016, **018**, then 017 🔲 |
| 2 (020–023) | Oracle + corpus (measures today's loss as the control) | 🔲 |
| 3 (030–031) | **Model proof — THE GATE** | 🔲 |
| 4 (040–045) | Track A — faithful save *(POMLs provisional — amendable by 031)* | 🔲 |
| 5 (050–053) | Track C — AI edit placement | 🔄 050 ✅ · 051–053 🔲 |
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

## Files modified — task 013 (complete)

| File | Change |
|---|---|
| `Services/Compose/IComposeService.cs` | NEW `ComposeSaveOutcome` closed enum + `ComposeSaveOutcomes` stable wire strings; `required Outcome` on `SaveComposeDocumentResult` |
| `Services/Compose/ComposeService.cs` | success-path outcome decision (severity-ordered); container-failure path reports `storage-failed` |
| `Api/ComposeEndpoints.cs` | `outcome` wire field; telemetry at every terminal state incl. all catches |
| `Telemetry/ComposeSaveTelemetry.cs` | NEW `compose.save_outcomes` counter (outcome + bounded cause) |
| `Infrastructure/DI/TelemetryModule.cs` | meter registration (unregistered = silently dropped from export) |
| `widgets/ComposeWorkspace.tsx` | client reads `outcome`, not the status; `savePersisted`/`saveReachedServer` split → indeterminate case |
| `ComposeCreateOnSaveEndpointContractTests.cs` | 2 new tests: 200+`storage-failed` (the defect) and 200+`persisted` (no false failures) |
| `ComposeEndpointsContractTests.cs` | construction site updated — the `required` field caught it at compile time |
| `ComposeWorkspace.saveErrorRouting.test.tsx` | 5 new FR-S06 tests; task 010's post-2xx-throw test updated to the indeterminate truth |

## Files modified — task 011 (complete)

| File | Change |
|---|---|
| `Services/Compose/ComposeService.cs` | 412 refusal DELETED → proceed + `concurrent-external-change` warning; new `ReplaceWithPreconditionAsync` (If-Match + retry-once-on-rebase) |
| `Api/ComposeEndpoints.cs` | `EtagPreconditionFailedException` maps 412 → **409 Conflict** ("try saving again"); no 412 remains on the save route |
| `widgets/ComposeBannerStack.tsx` | `concurrent-external-change` copy + its own banner row (partitioned out of the degradation set so that banner's title/trailer stay true) |
| `widgets/ComposeWorkspace.tsx` | client 412 branch DELETED (unreachable once the server stops refusing) |
| `ConcurrencySaveSeamTests.cs` | the 412-refusal test REWRITTEN to assert persist + warn + the exact If-Match value |
| `Def14_ComposeSaveLockedDocumentTests.cs` | two route-layer tests 412 → 409; translation-layer tests unchanged |
| `ComposeWorkspace.saveErrorRouting.test.tsx` | 412 test replaced by the concurrency-warning + no-refusal pair (NFR-08 client half) |

### Design decisions made (not re-derivable from the POML)

1. **If-Match value = `preWriteETag`**, the LIVE version read at save time — NOT the client's load-time
   ETag. Sending the load-time value would fail the precondition on every concurrent write and re-create
   the refusal this task removed. It is correct on both paths: the non-stale path merged against it
   because it equals the baseline; the stale path merged against it because `ReanchorStaleSaveAsync`
   re-downloaded exactly those bytes.
2. **Precondition failure → retry ONCE against a re-read version** (POML step 5's open question). A
   precondition failure carries nothing the user could act on, and the semantics are already
   last-writer-wins, so rebasing produces what they asked for. Unbounded retry could spin on a hot
   document; failing immediately would resurrect the dead end.
3. **409, not 412, for the exhausted case** — nothing about the caller's state is stale, so "reload and
   reapply" would be wrong advice. The honest instruction is "try again".
4. **The warning rides the existing `degradationWarnings` wire field** (no new field, no new prop) but
   renders as its own banner row, because the degradation banner's "Some formatting was simplified" title
   and "the original file is unchanged until you save" trailer are both FALSE of a concurrency notice.
5. **Only the ContentModel path warns.** The op-log path re-anchors ONTO the other writer's bytes — that
   is a merge, not a supersession, so "your save is now the current version" would be misleading there.

### Task 010 (complete) — files changed

| File | Purpose |
|---|---|
| `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx` | Deleted the unreachable `!response.ok` save block; added `classifySaveFailure` + `saveFailureMessage`; status routing in the save catch; `savePersisted` guard against reporting a post-2xx throw as a failed save |
| `.../widgets/ComposeWorkspace.saveErrorRouting.test.tsx` | NEW — 13 tests: every routed status + 4 negative cases |
| `.../widgets/ComposeWorkspace.saveOpLogPreservation.test.tsx` | 422 now THROWN, not resolved as `{ok:false}`; `virtual: true` mocks |
| `.../widgets/ComposeWorkspace.renderOnSave.test.tsx` | create-on-save failure mock converted to a thrown `ApiError` |
