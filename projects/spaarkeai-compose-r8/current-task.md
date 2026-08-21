# Current Task State — spaarkeai-compose-r8

> **Last Updated**: 2026-08-20 (by `context-handoff` — end of session; ready for `/compact`)
> **Recovery**: Read "Quick Recovery" first
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Git**: branch `work/spaarkeai-compose-r8` @ `18ebc525e`, **10 ahead of `origin/master`, 0 behind, working tree CLEAN**.
> Draft **PR #806** is open for the branch. Nothing is uncommitted; nothing is at risk across compaction.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **012** — Save lifecycle hardening (dirty flag survives a failed POST · timeout + `AbortSignal` + in-flight guard · working 423 recovery) |
| **Status** | **not-started** — no work in progress, no partial edits. A clean starting point. |
| **Next Action** | `Read projects/spaarkeai-compose-r8/current-task.md, then work on task 012` — which invokes `task-execute` on `tasks/012-save-lifecycle-hardening.poml` (FULL rigor · opus @ xhigh · steps `directional`). |
| **Blocked on** | Nothing. |
| **Complete** | **001 ✅ · 002 ✅ · 010 ✅ · 011 ✅ · 013 ✅ · 050 ✅** (all 2026-08-20) |

### Startable now — pick any; all are `parallel-safe: false`, so run them ONE AT A TIME

| # | Task | Why it is startable |
|---|---|---|
| **012** | Save lifecycle hardening | deps 010 ✅ — **recommended next** (see below) |
| **014** | Engine-side integrity — re-anchor download failure must never persist the stale baseline | deps none. The ONE Half-A defect in Track S |
| **015** | Document size ceilings — route to the existing chunked upload | deps 013 ✅ (**unblocked this session**) |
| **016** | Honest-failure set — the eight silent-drop modes | deps 010 ✅, 013 ✅ (**unblocked this session**) |
| **018** | Track S CI gate — run the Compose client suite in CI | deps 010 ✅. Must land BEFORE 017 |

**Why 012 first**: task 013 handed it a concrete input. Its `AbortSignal` work falls in the same class as
the transport-failure gap task 010 surfaced — an aborted request never reaches an HTTP status, so it needs
the non-status handling the save-outcome contract now has vocabulary for. Doing 012 next means that
handling is designed once. **Nothing forces this order** — 014/015/016/018 are equally valid.

**The entire Compose spine is `parallel-safe: false`** (project CLAUDE.md). Do NOT dispatch these to
parallel agents — 012/014/015/016 all touch `ComposeService.cs` and/or `ComposeWorkspace.tsx`.

### Traps that will bite a fresh session (all learned the hard way this session)

1. **Every EXISTING-item save now takes the 6-arg `ReplaceFileContentAsUserAsync`** (with `If-Match`).
   A new save-path test that mocks only the 5-arg overload fails with a MISLEADING
   `404 "SPE drive-item ... was not found or could not be written"`. Mock the 6-arg overload — or both.
2. **A 200 no longer means the document was written.** Read `payload.outcome`. `storage-failed` arrives
   on a 200 (the container-failure path returns rather than throws).
3. **`authenticatedFetch` THROWS on every non-2xx** — it never returns `{ok:false}`. Any new client test
   that mocks a resolved `{ok:false}` is testing a shape the transport cannot produce (this is exactly
   what let a dead code path pass its tests for three releases).
4. **`SaveComposeDocumentResult.Outcome` is `required`** — a new construction site is a COMPILE error
   until it says what happened. That is intentional; set it, do not work around it.
5. **`Spaarke.Compose.Components` is NOT in CI** and ~39 of its 88 jest suites cannot run without a prior
   SharedLibs `dist/` build. Locally: `npx jest` from that package dir; `--runInBand` for determinism
   (parallel workers produce 2–14 spurious failures). Task 018 fixes this.
6. **Two `renderOnSave.test.tsx` create-on-save tests FAIL on clean HEAD** — pre-existing, NOT caused by
   this session's work, reproduced by stashing. Do not chase them; task 018 root-causes them.
7. **Publish size must be measured COMPRESSED** (zip the publish dir). Raw bytes read ~137 MB and will
   look like a catastrophic regression; the real figure is **43.68 MB** against a 60 MB ceiling.

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

- **PR #690: CLOSED as superseded 2026-08-20 — GO for Phase 2.** Its `lfs: true` line was already on
  master via commit `f7ec5b928` (2026-08-12, from `email-communication-intelligence-r2`), confirmed an
  ancestor of this worktree. All 10 corpus `.docx` resolve to real bytes (2,666–27,986 B); Compose seam
  tests 101/101 green on master run `32313454003`. Closed with that evidence in the PR comment.
  **Phase 2 is no longer gated on anything.**
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
8. ~~**PR #690 lands first**, then the gate is built.~~ → **SATISFIED 2026-08-20**: #690 was closed as
   superseded (the fix was already on master via `f7ec5b928`). Phase 2 is ungated.
9. **Initialize-only** — no auto-execution.
10. **(2026-08-20) Task 018 added** — the Compose client suite gets a self-contained CI gate, sequenced
    BEFORE the 017 deploy, so Track S ships with enforcement on the client save contract rather than a
    promise of one.

---

## Session log — 2026-08-20

Six tasks completed: **001, 002, 010, 011, 013, 050**. One task authored: **018**. PR #690 closed.
Five commits on the branch (`a20943d7b`, `b182f1687`, `03cf36e2e`, `18ebc525e` + the earlier planning
commits); working tree clean; draft PR #806 open.

**The through-line.** Every defect fixed this session was the same shape: *the code said one thing and
the system did another, and nothing could tell the difference.* Task 010 found status-routing that could
never execute. Task 011 found a refusal whose client handler was dead the day it shipped. Task 013 found
a total write failure that rendered as "Saved ✓". Each had passing tests. This is the Half-B thesis
holding up under contact.

**Verification standard used** (keep it): every claim re-run in the main session rather than taken from
an agent report; `--runInBand` for determinism; baselines captured by stashing to a clean HEAD so
"pre-existing" is proven, not asserted.

**Two corrections made to my own earlier notes** — recorded because the pattern matters more than the
instances: after task 011 I wrote that the post-retry 409 should map to `storage-failed` and that
`refused-stale` had no producer. Task 013's terminal-state enumeration disproved both. A guess written
into a handoff reads exactly like a finding to the next session; these were corrected in place.

**Not deployed.** Track S deploys as a batch at task 017, after 018.

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
