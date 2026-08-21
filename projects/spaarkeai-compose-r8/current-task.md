# Current Task State — spaarkeai-compose-r8

> **Last Updated**: 2026-08-20 (012 · 014 · 018 complete; 015 server half landed — client half remains)
> **Recovery**: Read "Quick Recovery" first
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Git**: branch `work/spaarkeai-compose-r8`, 12 ahead of `origin/master`, 0 behind, working tree CLEAN.
> Draft **PR #806** is open for the branch. Nothing is uncommitted; nothing is at risk across compaction.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **015 (finish the client half)**, then **016**. Both own `ComposeWorkspace.tsx` — one pass over that file does both. |
| **Status** | 015 is **PARTIAL**: the server half is committed (`06b370995`); the client pre-flight is not written. The design for it is already decided — see [`notes/document-size-ceilings.md`](notes/document-size-ceilings.md) "What remains". |
| **Next Action** | `Read projects/spaarkeai-compose-r8/current-task.md, then finish task 015's client pre-flight` — or start **016**, which owns the same file. Doing both in ONE pass over `ComposeWorkspace.tsx` is cheaper than two. |
| **Blocked on** | Nothing. |
| **Complete** | **001 ✅ · 002 ✅ · 010 ✅ · 011 ✅ · 012 ✅ · 013 ✅ · 014 ✅ · 018 ✅ · 050 ✅** · 015 🔄 partial. Track S remainder: **015-client, 016**, then **017** (deploy). |

### Working tree

Clean except `src/server/api/Sprk.Bff.Api/.claude/agent-memory/researcher/**` (untracked) — researcher
subagent memory written during task 015, in an odd location inside the BFF project. Harmless; commit or
delete as you prefer.

### The client test suite IS in CI now (task 018) — what changed for you

- **All 90 suites run** (was 51 of 90); 1,103 tests (was 802). Verified in the main session at CI
  concurrency AND serially before commit.
- **The suite needs the sibling `dist/` built** — `Spaarke.Auth → Spaarke.SdapClient →
  Spaarke.UI.Components → Spaarke.DocumentOperations`, in that order. The mapper-to-`src` shortcut was
  tried and REJECTED (it needs ~11 undeclared packages in Compose's `package.json`). On a fresh clone
  without the build, 38 suites run.
- **Never write `jest.mock(..., { virtual: true })` in this package.** The flag registers the specifier
  in jest's shared resolver, so one suite's registration changes how a LATER suite resolves the same
  module. All 16 were removed; the rule is in `jest.config.js`.
- **`compose-client-gate` is ADVISORY**, with a written flip condition in the job comment: three green
  runs on `ubuntu-latest` (all evidence so far is a Windows dev box). Flipping it = delete one
  `continue-on-error: true` line.
- The 2 `renderOnSave` failures were a **test bug**, root-caused to `cdb1dbcb4` (UAT-03 name-gate, which
  shipped touching no test). Fixed by driving the modal; assertions went 127 → 134.

### Startable now — all `parallel-safe: false`; run ONE AT A TIME

| # | Task | Why |
|---|---|---|
| **015 (client half)** | the pre-flight; design already decided | `ComposeWorkspace.tsx` |
| **016** | honest-failure set — the eight silent-drop modes | same file + `ComposeEndpoints.cs` + `ComposeService.cs` |
| **018** | verify/finish whatever the background agent left | CI YAML + client tests |

**Do NOT run 015-client and 016 in parallel** — both own `ComposeWorkspace.tsx`. 018 IS file-disjoint
from both (that is why it was safe to run it alongside 014, which was server-only).

### Owner decisions needed at review (do not let these pass silently)

1. **Task 015 deviates from its POML.** It does NOT route to the chunked upload path. Reason: Graph's
   simple upload has been **250 MB since Oct 2023** (the 4 MB guard was enforcing a limit that no longer
   exists), and the chunked path **cannot carry an end-to-end `If-Match`** — so routing there would have
   silently weakened task 011's concurrency guarantee for exactly the large documents it was meant to
   help. Full reasoning + citations: [`notes/document-size-ceilings.md`](notes/document-size-ceilings.md).
2. **`If-Match` on `PUT .../content` is UNDOCUMENTED in the Graph v1.0 reference.** Task 011's
   concurrency guarantee rests on it. Worth an empirical probe (stale `If-Match` → expect 412) against a
   real SPE container **before the 017 deploy**. One test settles it.
3. **The god-class ratchet is RED on `DataverseServiceClientImpl.cs`** (2,975 vs frozen 2,864) — NOT this
   project's file (`a76e7e714` / `e3e72af91`). It was red before task 014 and still is. The two Compose
   waivers WERE re-baselined with documented reasons pointing at Track D 070/073. This one needs an owner.

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

### Carried forward from tasks 014 + 015 (read before 016)

Full write-ups: [`notes/reanchor-baseline-integrity.md`](notes/reanchor-baseline-integrity.md) ·
[`notes/document-size-ceilings.md`](notes/document-size-ceilings.md).

- **A stale-base re-anchor that cannot re-download the current bytes now REFUSES** —
  `ComposeStaleBaselineUnavailableException` → 409 + `refused-stale` + cause `baseline-download`. The
  old fallback persisted the LOAD-TIME baseline over a version already known to be newer. Do not
  reintroduce any "proceed with what we have" branch on that path.
- **`ComposeSaveLimits.MaxDocumentBytes` (25 MB) is THE limit.** It drives the request-body cap, the
  refusal, and the number in the message. **Never add a second size constant** — especially not a
  client-side copy; the client must receive the number from the server or do no numeric pre-flight.
- **The 4 MB Graph guard is gone** from `UploadSmallAsUserAsync`. Simple upload is 250 MB (since Oct
  2023, SPE-confirmed). If you see a 4 MB threshold anywhere else, it is stale too.
- **`refused-invalid` = "retrying this unchanged cannot succeed"**; `refused-stale` = "the base moved and
  we could not rebase; nothing written, nothing overwritten"; `storage-failed` = "the write itself
  failed". Pick on that axis, not on which subsystem threw.

### Carried forward from task 012 (read before 014 / 015 / 016 / 018)

Full write-up: [`notes/save-lifecycle-hardening.md`](notes/save-lifecycle-hardening.md).

- **The editor's dirty flag is now cleared in exactly ONE place on the save path**: `commitSaved()`,
  after a confirmed success. Every capture method (`serializeOperationLog`, `buildContentModel`,
  `buildImportedContentModel`) only WATERMARKS. The six `dirtyRef.current = false` sites that remain
  in `ComposeEditor.tsx` are all in the load/mount effect — a different lifecycle. **Do not add a
  seventh on the save path.**
- **`commitSaved()` fires on every successful save that CAPTURED something** — including
  born-in-editor now — but deliberately NOT on a clean byte-identical passthrough save, which must
  touch no editor state (renderOnSave review F3 defends this; making the call unconditional breaks
  that test, correctly).
- **The save carries a 120 s deadline** (`COMPOSE_SAVE_TIMEOUT_MS`) via an `AbortSignal` passed
  through `authenticatedFetch`'s `RequestInit`. An abort classifies as the new `aborted` failure
  kind (read `.name` structurally — `AbortError` / `TimeoutError`), which is a FAILED save: dirty
  flag intact, no `commitSaved`.
- **A ref-based in-flight guard** blocks a concurrent second save. It is claimed AFTER the
  synchronous setup, immediately before the first `await` — sync code cannot interleave, and a sync
  throw above that line would otherwise latch the guard and kill saving for the session.
- **FR-S04 needed no work**: the 423 lock banner + working Retry shipped with task 010 and is
  already covered end-to-end in `ComposeWorkspace.saveErrorRouting.test.tsx`.
- **Task 018 finding, reproducible**: a `jest.mock('@spaarke/auth', …, { virtual: true })` in a suite
  that also loads the REAL `ComposeEditor` graph corrupts `@spaarke/auth` resolution for LATER
  suites in the same run (`useComposeWordShuttle` started failing with "Auth not initialized" from
  the real dist). Repro + the systemic fix (a `moduleNameMapper` entry to `Spaarke.Auth/src`,
  mirroring `@spaarke/ai-widgets`) are in the notes. Also: **adding test files reorders the run**
  (jest sorts by file size), which is how this surfaced.

### Carried forward from task 011 (read before 013)

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
| 1 (010–018) | **Track S — save reliability (P0, ships alone)** | 🔄 010 ✅ · 011 ✅ · 012 ✅ · 013 ✅ · 014–016, **018**, then 017 🔲 |
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

## Files modified — task 012 (complete)

| File | Change |
|---|---|
| `widgets/ComposeEditor.tsx` | `docRevisionRef` + `capturedRevisionRef`; the three capture methods WATERMARK instead of clearing; `commitSaved` is the single save-path clear, now revision-aware (catches mid-flight edits the op-log cannot represent); handle JSDoc corrected |
| `widgets/ComposeWorkspace.tsx` | 120 s `AbortController` deadline through `authenticatedFetch`'s `RequestInit`; new `aborted` failure class + honest message; ref-based in-flight guard claimed before the first `await`, released in `finally`; commit gate extended to born-in-editor (`sentEditorContentModel`) while still excluding clean passthrough saves |
| `widgets/ComposeEditor.saveLifecycleDirty.test.tsx` | NEW — 5 tests against the REAL editor + handle (FR-S03) |
| `widgets/ComposeWorkspace.saveLifecycle.test.tsx` | NEW — 5 tests: timeout, retry-after-timeout, in-flight guard, guard release, unaffected happy path (FR-S05) |
| `notes/save-lifecycle-hardening.md` | NEW — blast-radius table, mechanism rationale, escalation-trigger analysis, the 018 finding |

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
