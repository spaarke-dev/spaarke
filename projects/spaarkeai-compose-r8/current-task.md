# Current Task State — spaarkeai-compose-r8

> **Last Updated**: 2026-08-21 (task 016 complete — **Track S is code-complete; 017 is the deploy**)
> **Recovery**: Read "Quick Recovery" first
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)
> **Git**: branch `work/spaarkeai-compose-r8`. Draft **PR #806** open.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **017** — Track S deploy + owner UAT. **Steps 1-5 DONE. Steps 6, 6.5, 7 are the OWNER'S.** |
| **Status** | **in-progress — blocked on owner UAT.** Do NOT mark 017 complete until the checklist is filled in and a GO/NO-GO is recorded. |
| **Next Action** | Owner runs [`notes/track-s-uat.md`](notes/track-s-uat.md) against dev, records observations per row, and writes GO/NO-GO. Then task 017 closes and Phase 2 (020) starts. |
| **Complete** | 001 · 002 · 010 · 011 · 012 · 013 · 014 · 015 · 016 · 018 · 050 ✅ |

### Deployed 2026-08-21 (task 017 steps 1-5)

Both artifacts from **`e5815e862`**, same window (NFR-05 anti-clobber pairing):

| Artifact | Target | Evidence |
|---|---|---|
| BFF | `spaarke-bff-dev` (`DOTNETCORE\|10.0`) | 44.98 MB package · **SHA-256 verified on 4 critical files** · `/healthz` 200 · Compose + checkout routes **401 = registered** |
| `sprk_spaarkeai` | `spaarkedev1`, resource `5206a442-3451-f111-bec7-7ced8d1dc988` | 5,694 KB published · **7 Track S strings + the per-document draft key verified present in the built HTML** |

**Branch tip is now `7e6b17a71`, one commit ahead of what was deployed.** The delta is
`2366b4ccd` — CI's Prettier bot re-wrapping a 2-line type union in `ComposeEditor.tsx`. **Formatting
only; the deployed artifact is functionally identical.** No redeploy needed for UAT.

**Gates run before deploying** (task 017 step 3 asked for evidence they ran in 010-016; there was none,
so they were run against the cumulative diff rather than asserted): `adr-check` **0 violations /
0 warnings** · NetArchTest **36/36** · `code-review` no Critical, no Warning, **one Suggestion**
(the metadata refresh writes unconditionally — see the UAT note) · publish **43.68 MB** compressed
(−1.28 MB vs baseline) · **0 HIGH/CRITICAL CVE**.

### Resolved since the last handoff

| # | Decision | Status |
|---|---|---|
| **A** | Task 015 does NOT route to the chunked upload path | ✅ **RATIFIED by the owner 2026-08-21**. Do not re-litigate. |
| **B** | `If-Match` on `PUT .../content` undocumented | 🟡 **OPEN** — see above. |
| **C** | God-class ratchet RED on `DataverseServiceClientImpl.cs` | ✅ **MOOT** — `origin/master` **retired the LOC ratchet entirely** on 2026-08-20 (`866f9c101`), replacing it with `docs/standards/COMPONENT-COMPLEXITY.md` + a non-blocking report. `GodClassGuardTests.cs` is expected to disappear on merge. |

### Merge note (master moved)

`origin/master` is **11 commits ahead** of this branch's base. File overlap with our diff is exactly
three: `tests/Spaarke.ArchTests/GodClassGuardTests.cs` (master DELETES the gate — resolve in master's
favour; our two re-baselines simply go away), `projects/INDEX.md`, and
`.claude/agent-memory/researcher/MEMORY.md`. **None of the Compose source files collide.**

### Working tree

Clean except `src/server/api/Sprk.Bff.Api/.claude/agent-memory/researcher/**` (untracked) — researcher
subagent memory from task 015, in an odd spot inside the BFF project. Harmless; not part of any
deliverable.

---

## Traps that will bite a fresh session

1. **Every EXISTING-item save takes the 6-arg `ReplaceFileContentAsUserAsync`** (with `If-Match`). A new
   save-path test that mocks only the 5-arg overload fails with a MISLEADING
   `404 "SPE drive-item ... was not found or could not be written"`. Mock the 6-arg overload — or both.
2. **A 200 does not mean the document was written.** Read `payload.outcome`. `storage-failed` AND
   `partially-recorded` both arrive on a 200 (those paths return rather than throw).
3. **`authenticatedFetch` THROWS on every non-2xx** — it never returns `{ok:false}`. A new client test
   mocking a resolved `{ok:false}` is testing a shape the transport cannot produce; that is exactly how
   dead code passed its tests for three releases. **Task 016 found two MORE instances** (the load path
   and the review-memo path) and deliberately did NOT fix them — see
   [`notes/honest-failure-set.md`](notes/honest-failure-set.md) "Beyond the eight".
4. **`SaveComposeDocumentResult.Outcome` is `required`** — a new construction site is a COMPILE error
   until it says what happened. Intentional; set it, do not work around it.
5. **The client suite needs the sibling `dist/` built** — `Spaarke.Auth → Spaarke.SdapClient →
   Spaarke.UI.Components → Spaarke.DocumentOperations`, in that order. With dists built: **91 suites,
   1,121 tests**. Without: 38 suites run.
6. **Never write `jest.mock(..., { virtual: true })` in `Spaarke.Compose.Components`** — it poisons
   jest's SHARED resolver, so one suite changes how a LATER suite resolves the same module.
7. **Publish size must be measured COMPRESSED** (zip the publish dir). Raw bytes read ~137 MB; the real
   figure is **43.68 MB** against a 60 MB ceiling.
8. **Seam tests live in `tests/unit/Sprk.Bff.Api.Tests/`**, not the integration project —
   `tests/integration/seam/**` is compiled INTO it. `dotnet test tests/integration/...` reports
   "No test matches" and looks like a passing run.
9. **`ComposeServiceImportedRenderSaveTests` uses `MockBehavior.Strict`.** Any NEW Dataverse call the
   save path makes must be `Setup(...)` there, or the strict mock throws, a best-effort catch swallows
   it, and the symptom presents as a spurious degradation warning — a fixture gap wearing a production
   defect's clothes. (`bff-extensions.md` § F.2 exists for exactly this.)
10. **`git stash push -- <paths>` on files with NO local changes stashes NOTHING** — and a following
    `git stash pop` then pops somebody ELSE'S pre-existing stash. This worktree shares a stash list with
    28 other entries from other projects. To A/B a committed change, use
    `git checkout <commit> -- <paths>`, never stash.

---

### Carried forward from tasks 014 + 015

Full write-ups: [`notes/reanchor-baseline-integrity.md`](notes/reanchor-baseline-integrity.md) ·
[`notes/document-size-ceilings.md`](notes/document-size-ceilings.md).

- **A stale-base re-anchor that cannot re-download the current bytes now REFUSES** —
  `ComposeStaleBaselineUnavailableException` → 409 + `refused-stale` + cause `baseline-download`. The
  old fallback persisted the LOAD-TIME baseline over a version already known to be newer. Do not
  reintroduce any "proceed with what we have" branch on that path.
- **`ComposeSaveLimits.MaxDocumentBytes` (25 MB) is THE limit.** It drives the request-body cap, the
  server refusal, the number in the message, AND the `maxDocumentBytes` field the Load/Upload responses
  advertise to the client. **Never add a second size constant** — especially not a client-side copy.
  The client pre-flights against the advertised number, and when none was advertised it does NO numeric
  check (`state.maxDocumentBytes === null` means "do not guess", never "unlimited").
- **The 4 MB Graph guard is gone** from `UploadSmallAsUserAsync`. Simple upload is 250 MB (since Oct
  2023, SPE-confirmed). If you see a 4 MB threshold anywhere else, it is stale too.
- **`refused-invalid` = "retrying this unchanged cannot succeed"**; `refused-stale` = "the base moved and
  we could not rebase; nothing written, nothing overwritten"; `storage-failed` = "the write itself
  failed". Pick on that axis, not on which subsystem threw.

### Carried forward from task 012

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
| 1 (010–018) | **Track S — save reliability (P0, ships alone)** | 🔄 010–015 ✅ · 018 ✅ · **016** 🔲, then **017** (deploy) 🔲 |
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
10. **(2026-08-21) Task 015's POML deviation RATIFIED** — Compose does NOT route to the chunked upload
    path. Graph's simple upload has been 250 MB since Oct 2023 (so it is unnecessary) and the chunked
    path cannot carry an end-to-end `If-Match` (so it would have weakened task 011's guarantee). Owner:
    "your changes are fine". Binding — do not restore chunked routing without a new decision.
11. **(2026-08-20) Task 018 added** — the Compose client suite gets a self-contained CI gate, sequenced
    BEFORE the 017 deploy, so Track S ships with enforcement on the client save contract rather than a
    promise of one.

---

## Session log — 2026-08-20 / 08-21

**Five tasks completed: 012, 014, 015, 018** (plus the earlier 001/002/010/011/013/050). Track S now has
exactly **016** left before the **017** deploy. Seven commits; working tree clean.

**The through-line held, and widened.** Every Track S defect has been the same shape — *the code said one
thing and the system did another, and nothing could tell the difference.* This session found three more
instances, in three different layers:

- **012 (client state)**: the dirty flag was cleared before the POST, so a failed save left the editor
  believing it was clean — Save disabled, `beforeunload` disarmed, work one tab-close from gone.
- **014 (engine)**: a re-anchor whose download failed persisted the LOAD-TIME baseline over a version it
  had *already observed* to be newer, and reported 200. The only data-destroying path in Track S. Its own
  comment claimed it "failed closed" — true of the OPS, false of the BYTES.
- **015 (platform assumption)**: a 4 MB guard enforcing a Graph limit that stopped existing in Oct 2023,
  and a transport-layer body cap that rejected large saves before any handler could explain why.

And 018 found the meta-instance: **a deliberate product change shipped 2026-08-18, two tests went red the
same day, and nobody saw it for two days — because nothing ran them.** The project's thesis reproducing
itself inside the task built to stop it.

**Verification standard used (keep it).** Every claim re-run in the main session rather than taken from an
agent's report — 018's numbers were re-measured independently before its work was committed. Baselines
captured by stashing to clean HEAD so "pre-existing" is proven, not asserted (that is how the god-class
ratchet was shown red BEFORE task 014). New tests run against the unfixed code first: 012's suites fail
4/5 on HEAD, 014's reproduced a real 1,286-byte `.docx` being written over a newer version, 015's
pre-flight test fails when the pre-flight is reverted.

**Parallelism, honestly scoped.** Only 018 was safe to run alongside another task — verified by comparing
actual file sets, not by trusting the `parallel-safe` flags. 015 and 016 collide on two files each; 014
and 016 collide on `ComposeService.cs`. A researcher subagent (read-only) settled the Graph platform facts
that redirected 015.

**One correction to my own work**: I first made `commitSaved()` unconditional in 012, which broke a test
asserting that a clean passthrough save touches no editor state. That test was defending a real invariant;
I tightened the gate instead of rewriting the test.

**Not deployed.** Track S deploys as a batch at 017, after 016.

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

## Files modified — task 018 (complete)

| File | Change |
|---|---|
| `.github/workflows/sdap-ci.yml` | NEW `compose-client-gate` job (append-only, single hunk; the four owned jobs untouched). ADVISORY with a written flip condition |
| `scripts/ci/summarize-jest-results.js` | NEW — step-summary table + `::error::` annotations naming each failed suite |
| `Spaarke.Compose.Components/jest.config.js` | comment-only: the sibling-resolution contract + the no-`virtual` rule (mapper table byte-identical) |
| 8 × `*.test.tsx` | all 16 `{ virtual: true }` arguments removed; `renderOnSave` + `bornInEditorSave` fixed to drive the name modal (assertions 127 → 134) |
| `projects/INDEX.md` | narrative refresh (`ci-workflows: Y` was already declared) |

Runnable suites **51 → 90 of 90**; tests **802 → 1,103** (1,106 after 015's client tests).

## Files modified — task 015 (complete)

| File | Change |
|---|---|
| `Services/Compose/IComposeService.cs` | NEW `ComposeSaveLimits` — the ONE limit (25 MB) + the derived body cap + the single display-formatting site |
| `Infrastructure/Graph/UploadSessionManager.cs` | the stale 4 MB guard DELETED (Graph simple upload is 250 MB since Oct 2023); XML doc corrected |
| `Api/ComposeEndpoints.cs` | oversize gate on the SHARED save path → `refused-invalid` + cause `too-large` + a ProblemDetails naming the limit; `RequestSizeLimitAttribute` on both save routes; `maxDocumentBytes` advertised on the Load + Upload responses |
| `Telemetry/ComposeSaveTelemetry.cs` | NEW bounded cause `too-large` |
| `widgets/ComposeWorkspace.types.ts` | `maxDocumentBytes` state + action field, set atomically at mount |
| `widgets/ComposeWorkspace.tsx` | the pre-flight — against the SERVER's advertised number, never a compiled-in copy; absent limit ⇒ no numeric check |
| `ConcurrencySaveSeamTests.cs` | oversize refused with the stated limit + nothing written; a 6 MB document saves and reaches storage |
| `ComposeWorkspace.saveLifecycle.test.tsx` | 3 client tests incl. "no advertised limit → the client does not invent one" |

## Files modified — task 014 (complete)

| File | Change |
|---|---|
| `Services/Compose/ComposeService.cs` | both destructive `return (originalBaseline, …)` fallbacks DELETED → typed refusal throw |
| `Services/Compose/IComposeService.cs` | NEW `ComposeStaleBaselineUnavailableException`; `RefusedStale` doc corrected to list BOTH producers |
| `Api/ComposeEndpoints.cs` | catch → telemetry(`refused-stale`, `baseline-download`) → 409 ProblemDetails |
| `Telemetry/ComposeSaveTelemetry.cs` | NEW bounded cause `baseline-download` |
| `ConcurrencySaveSeamTests.cs` | NEW `[Theory]` — both download-failure modes; asserts no write, 409, honest detail |
| `tests/Spaarke.ArchTests/GodClassGuardTests.cs` | two **Compose** waivers re-baselined WITH reasons pointing at Track D 070/073 |

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
