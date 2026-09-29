# Task 012 — Save lifecycle hardening (FR-S03 / S04 / S05)

> Completed 2026-08-20. Client-only: `ComposeEditor.tsx`, `ComposeWorkspace.tsx` + 2 new test suites.
> No BFF change → no publish-size measurement applies.

---

## What was actually wrong, precisely

### FR-S03 — the dirty flag was cleared before the POST

`buildContentModel()` on the editor handle cleared `dirtyRef` as a side effect of BUILDING the save
payload. The sibling `buildImportedContentModel()` already did not (R6 review F5) — so the bug lived
on the born-in-editor path only, and whichever path a tester used decided whether they saw it.

**Blast radius, stated accurately** (the exposure is narrower than "all six affordances", and the
report should say so):

| Affordance | Source | Born-in-editor **create** (no `speDriveItemId`) | Born-in-editor **re-save** |
|---|---|---|---|
| Save button | `isDirty \|\| hasTransientDraft` | still enabled (transient fallback) | **disabled — exposed** |
| `beforeunload` | `isDirty \|\| !speDriveItemId` | still armed (no SPE id) | **disarmed — exposed** |
| unmount flush | same ref | still armed | **disarmed — exposed** |
| toolbar `hasUnsavedEdits` | `isDirty` | **wrong — exposed** | **wrong — exposed** |
| draft autosave tick | `handle.isDirty()` | **stopped — exposed** | **stopped — exposed** |
| Ctrl+S | `state.status === 'loaded'` | never gated on dirty — **was always live** | always live |

So: a failed save of a re-saved born-in-editor document left the user with no Save button, no
unload warning, no unmount flush and no local draft snapshots. The create case was partly covered
by the transient-draft fallback. Ctrl+S was never affected — do not claim it as a fix.

### FR-S05 — no deadline, no signal, no guard

The save had none of the three. A hung request stranded `status === 'saving'` permanently (the
reducer only leaves `saving` on `saveSucceeded` / `saveFailed`), and the only escape was a reload,
which discards the document. Separately, `triggerSave` closes over `state`, so two calls in the same
tick both read the pre-dispatch `'loaded'` — the status could not serialize them.

### FR-S04 — already real

The 423 lock banner + working Retry shipped with task 010 and is covered end-to-end by
`ComposeWorkspace.saveErrorRouting.test.tsx` ("423 renders the Word-lock banner with a working
Retry"), which asserts the retry re-issues the save and succeeds once the lock clears. Nothing was
added here; duplicating that test would have been the §11 anti-pattern.

---

## The mechanism chosen

**One clearing site.** Every capture method now WATERMARKS instead of clearing:
`serializeOperationLog()`, `buildContentModel()` and `buildImportedContentModel()` each record the
op-log high-water mark **and** `docRevisionRef` (a counter incremented in `onUpdate`, outside the
dirty guard). `commitSaved()` — called only after a confirmed successful save — is the sole site
that clears, recomputing:

```
stillDirty = opLog.size > 0 || (capturedRevision !== null && docRevision !== capturedRevision)
```

**Why the revision counter and not just `opLog.size`** (the pre-existing mechanism): a deferred /
unrepresentable / refused-atom transaction appends NO op-log entry, so an edit of that class typed
during an in-flight save is invisible to the size check. On the ContentModel paths the whole
document is captured, so such an edit is real work the save did not carry; clearing dirty on it
discards it silently. This also closes the same hole on the imported path, which had it already.

The watermark is consumed by each `commitSaved()` (reset to null), so a stale capture point cannot
report dirty forever.

**Remaining `dirtyRef.current = false` sites** (ComposeEditor.tsx ~2380, 2388, 2400, 2432, 2454,
2486) are ALL in the `docxBytes` mount/load effect — a fresh document is clean by definition. That
is a different lifecycle event from a save. The save path has exactly one clear.

**The commit gate stayed a gate.** `commitSaved()` now also fires for born-in-editor saves
(`sentEditorContentModel`), but NOT for a clean byte-identical passthrough save, which captures
nothing and must touch no editor state (renderOnSave review F3). Making it unconditional broke that
test — correctly; the test was defending a real invariant and was left as authored.

**Timeout: 120 s**, `AbortController` (not `AbortSignal.timeout`) so the timer is cleared the moment
the exchange finishes. The signal rides `authenticatedFetch`'s existing `RequestInit` (ADR-028), and
because that function spreads `init` onto every attempt, the deadline bounds the 401 retry loop
rather than restarting with it.

**In-flight guard sited after the synchronous setup, immediately before the first `await`.** Sync
code cannot interleave, so nothing above needs guarding — and a synchronous throw in that setup
would otherwise latch the guard forever, silently killing saving for the session. That is a worse
failure than the double-POST the guard prevents.

---

## Escalation trigger — checked, did NOT fire

The POML's trigger: *"if the dirty flag is load-bearing for something other than save enablement —
e.g. it gates content-model construction, so deferring the clear changes what a retry POSTs — stop
and surface it."*

The flag IS load-bearing for payload shape. Two reads gate it:

- `ComposeWorkspace.tsx:1828` — `editorIsDirty && (!isTransientCreate || !!state.docxBytes)` decides
  whether the op-log is serialized.
- `ComposeWorkspace.tsx:1916` — the imported model-path probe.

Neither changes the born-in-editor POST body:

- **Create** (transient, no retained bytes): both conjuncts of 1828 are false regardless of the
  flag; 1916 requires `state.docxBytes`, which is null. No change.
- **Re-save**: 1828 is now true on a retry where it was previously false — so `operationLog` gets
  computed — but the born-in-editor replace body (shape 1) does not include `operationLog`. The
  posted body is byte-identical. 1916 still requires `docxBytes`. No change.

Retries therefore post exactly what they posted before. The trigger's condition is not met.

---

## Verification standard used

- **Anti-self-confirming**: both new suites were run against the unfixed files. Editor suite 4 of 5
  fail on HEAD `ComposeEditor.tsx`; workspace suite 4 of 5 fail on HEAD `ComposeWorkspace.tsx`. The
  one that passes in each case is the NEGATIVE no-regression guard, which is the point of it.
- **Baseline by stashing, not assertion**: full package on clean HEAD = 39 failed / 49 passed
  suites, **2 failed** / 790 passed tests. After = 39 failed / **51 passed** suites, **2 failed** /
  **800 passed** tests. Same two pre-existing `renderOnSave` failures (trap #6), +2 suites,
  +10 tests. `--runInBand` throughout.
- `npx tsc --noEmit`: 18 errors before and after — the identical pre-existing
  unresolved-sibling-module set (trap #5). Zero from this change.
- ESLint is **not configured** in this package (no `eslint.config.js`); task 018 scopes it out.

---

## Finding for task 018 (test-infrastructure, NOT a product defect)

A `jest.mock('@spaarke/auth', …, { virtual: true })` in a suite that ALSO loads the real
`ComposeEditor` graph (which imports `@spaarke/auth` for real) **corrupts resolution of that module
for later suites in the same run**: `useComposeWordShuttle.test.tsx`, whose own ordinary mock is
correct, began failing with `AuthError: Auth not initialized` — from
`../Spaarke.Auth/dist/useAuth.js`, i.e. the real module, not its mock. Reproduced deterministically:

```
npx jest --runInBand --runTestsByPath \
  src/widgets/ComposeEditor.saveLifecycleDirty.test.tsx \
  src/widgets/useComposeWordShuttle.test.tsx     # 2 failures with the virtual mock, 0 without
```

Worked around here by making the new suite's `@spaarke/auth` mock non-virtual (matching
`ComposeEditor.dirtyOnMount` / `aiToolbarTriggers`) and by mocking `./ComposeAiToolbar`, the only
`useAuth()` consumer in the editor's graph. That trade costs fresh-clone runnability: the suite now
needs `Spaarke.Auth/dist` built — the same condition the other ~39 unrunnable suites already have.

**The systemic fix belongs to 018**: add `@spaarke/auth` to `jest.config.js` `moduleNameMapper`,
pointing at `<rootDir>/../Spaarke.Auth/src`, exactly as `@spaarke/ai-widgets` already is. That would
make the real-editor suites runnable with no dist build and make both mock styles unnecessary.

Note also that adding test files **reorders** the run (jest sorts by file size), which is how this
latent defect surfaced. Any 018 determinism work should assume order-dependence exists until proven
otherwise.

---

## Files changed

| File | Change |
|---|---|
| `widgets/ComposeEditor.tsx` | `docRevisionRef` + `capturedRevisionRef`; `onUpdate` increments the revision outside the dirty guard; `buildContentModel` watermarks instead of clearing; `buildImportedContentModel` + `serializeOperationLog` also watermark; `commitSaved` is the single clear, revision-aware; handle JSDoc corrected on `buildContentModel` + `commitSaved` |
| `widgets/ComposeWorkspace.tsx` | `COMPOSE_SAVE_TIMEOUT_MS`; `aborted` failure class + its honest message; `saveInFlightRef` guard + `finishSaveAttempt`/`failEarly`; `AbortController` + `signal` through `authenticatedFetch`; `finally` clears the timer and releases the guard; commit gate extended with `sentEditorContentModel` |
| `widgets/ComposeEditor.saveLifecycleDirty.test.tsx` | NEW — 5 tests against the REAL editor + handle (FR-S03) |
| `widgets/ComposeWorkspace.saveLifecycle.test.tsx` | NEW — 5 tests: timeout, retry-after-timeout, in-flight guard, guard release, unaffected happy path (FR-S05) |

## Note for Track D

`ComposeWorkspace.tsx` is now ~4,835 lines and `ComposeEditor.tsx` ~3,760. The god-class ratchet
covers `src/server/**/*.cs` only, so neither is gated — but Track D's rationale applies to them just
as much, and this task added ~100 lines to the larger one.
