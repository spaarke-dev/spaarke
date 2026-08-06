# Task 041 — Execution Notes: Multi-select Review Notes + sequential batch AI action with progress

> Rigor: FULL · Model tier: sonnet @ xhigh · Step mode: directional · Status: complete

## Step 0 — dispatch trace (end-to-end, before designing anything)

Traced `ComposeCommentGutter.noteTools`/`onRunNoteTool` → `ConversationPane.dispatchComposeAction` →
`makeComposeEditControlsMessage` per the task's own pointer, and found the trace is more nuanced than the
one-line summary:

1. **`ComposeCommentGutter.tsx`**'s `onRunNoteTool?: (threadId, toolId) => void` prop is wired, in
   `ComposeEditor.tsx`, to `runNoteTool` (`:2492` pre-task) — the function that actually BUILDS the per-note
   dispatch request (resolves the thread's live clause span via `findCommentAnchorRange`, slices
   `selectionText`, assembles `slots`). This builder needs `editor` state and lives ONLY in `ComposeEditor.tsx`.
2. `runNoteTool` calls `enqueueComposeAction(request)` — a PROP on `ComposeEditor`, not a direct import of
   `ConversationPane`. The prop is threaded through `composeActionBridge.ts` (`useComposeActionBridge().enqueue`),
   which delegates to whatever `ConversationPane.dispatchComposeAction` registered via
   `useRegisterComposeActionDispatcher` (a non-bus, sibling-pane conduit — ADR-030 unaffected, per that
   module's own header).
3. **`ConversationPane.dispatchComposeAction`** (`:1047` pre-task) does NOT call `enqueueComposeAction`
   directly against the network — it calls `actionQueue.enqueue(...)`, where `actionQueue` is
   `useSerialActionQueue` (`ConversationPane.tsx` / `useSerialActionQueue.ts`, FR-18). **This queue is ALREADY
   a FIFO, at-most-one-in-flight serializer for EVERY Compose AI-action dispatch in the app** (its own header:
   "at most one dispatch is ever in flight, requests run in enqueue (arrival) order"). Only AFTER the queued
   dispatch's Promise settles does `dispatchComposeAction`'s `.then()` run — which is where the per-note
   Assistant confirmation (`makeComposeEditControlsMessage`) is emitted.
4. **Critical finding**: `runNoteTool` does NOT await `enqueueComposeAction(...)` — it fires-and-forgets
   (`void enqueueComposeAction({...}).catch(() => undefined)`). That is correct for a single click (nothing
   needs to block), but it means a batch loop CANNOT simply call `runNoteTool` N times sequentially and await
   its return — that promise resolves almost immediately, long before the underlying dispatch (and its
   confirmation) actually completes. A true sequential batch loop needs to await the REAL dispatch completion.

## Step 0.5 — seam decision (documented per directional-mode latitude, root CLAUDE.md §8.5)

**The POML's `<relevant-files>` list named `ConversationPane.tsx` as the "sequential batch loop" file. Step 0
shows this is not where the loop can live**: `ConversationPane.tsx` never held the per-note anchor/selection
data the request-builder needs (`findCommentAnchorRange`, `editor.state.doc`) — only `ComposeEditor.tsx` does.
Duplicating that data access into `ConversationPane.tsx` would be a **fork** of the per-note dispatch builder,
which the task's own constraint forbids ("Batch reuses the SHIPPED single-note dispatch verbatim per
iteration... a wrapper, not a fork").

**Decision: the sequential batch loop lives in `ComposeEditor.tsx`; `ConversationPane.tsx` is UNTOUCHED.**
Concretely:

- `runNoteTool`'s request-building body was extracted UNCHANGED into `dispatchNoteToolRequest(threadId, action,
  instruction)` — a `Promise<void>`-returning function shared by BOTH the single-note path (still
  fire-and-forget, `runNoteTool` unchanged in observable behavior) and the new batch path (awaited).
- The sequential LOOP MECHANICS (order, one-in-flight, failure isolation, live progress) are a new, standalone,
  pure module — `batchNoteToolRunner.ts` — so the ADR-016 guarantee is directly unit-testable without a TipTap
  editor harness. `ComposeEditor.tsx`'s `runBatchNoteToolAsync` is the thin adapter that supplies `runOne =
  (threadId) => dispatchNoteToolRequest(threadId, action, instruction)` to it.
- Because every batch note dispatch calls the literal SAME `dispatchNoteToolRequest` → `enqueueComposeAction`
  → (bridge) → `ConversationPane.dispatchComposeAction` → `actionQueue.enqueue(...)` chain a single-note run
  uses, **the "each note's outcome is byte-equivalent in form to an individually-run note's outcome" acceptance
  criterion holds by construction** — `ConversationPane.tsx`'s confirmation-rendering code (`makeComposeEditControlsMessage`)
  never needed to change, and didn't.
- The existing `actionQueue` (`useSerialActionQueue`) ALREADY guarantees at-most-one-dispatch-in-flight across
  the WHOLE app (including any concurrent single-note click during a batch run — belt-and-suspenders with the
  batch loop's own sequential `for...of`/`await`). The batch loop's explicit sequentiality is what makes the
  loop's OWN ordering/progress/failure-isolation directly assertable and testable — it does not depend on the
  queue's serialization to be correct, but is consistent with it.

Per CLAUDE.md §6.5, this is not an ADR conflict (no ADR rule is violated — ADR-016 asks for sequential dispatch
with a progress indicator, which this delivers) and not a HARD BOUNDARY violation (`ComposeEditor.tsx` is not
in this task's forbidden list — only 033's four files + `src/server/**`/`infra/**` are). It is a task-scope
adaptation under directional steps mode, flagged here per task 040's own precedent (that task's execution notes
document an analogous `ComposeEditor.tsx`-vs-anticipated-file deviation for the same underlying reason: the
data the wiring needs lives there, not where the POML's relevant-files list assumed).

## Step 1 — selection model + checkboxes (`ComposeCommentGutter.tsx`)

- Selection state (`checkedIds: ReadonlySet<string>`) is INTERNAL/uncontrolled to the gutter — matches
  design.md's own Component Justification framing ("Extend the gutter — add selection state + one
  sub-toolbar"). The host only learns the selection at the moment Run is clicked (`onRunBatchNoteTool`).
- Checkbox (Fluent `Checkbox`, keyboard-accessible via native input semantics) renders at the upper-left of
  `cardHeader`, before the location label. `stopPropagation` on its wrapping `<div>`'s click/keydown keeps
  checking a box from also triggering the card's own select/expand `activate()` handler — mirrors the existing
  ⋮-tools-button's own `stopPropagation` pattern in the same file.
- Checkbox rendering (and the whole sub-toolbar) is gated on `onRunBatchNoteTool` being wired — mirrors the
  established `noteTools`/`onRunNoteTool` gating convention (no prop wired ⇒ zero new UI, zero behavior change
  for standalone/library mounts, e.g. LegalWorkspace).
- Dispatch order = the gutter's OWN `threads` prop order (`orderedCheckedIds`), not checkbox-click order —
  deterministic, mirrors `resolveMatchingThreadId`'s/`layoutCommentGutterCards`'s own input-order-is-the-contract
  convention already established in this file.

## Step 2 — sub-toolbar + scoped dropdown + cap confirm

- The sub-toolbar's action dropdown reuses the EXISTING `noteTools` prop verbatim — the SAME
  `getToolsForSurface('review-note', activeWorkType)`-scoped list the ⋮ menu already uses. **No new prop for
  the action list, no batch-only actions** (constraint satisfied by construction — there is no second source of
  truth to drift).
- "Select all" renders only when exactly 1 note is checked (spec) and selects every thread currently in the
  gutter's `threads` prop.
- `BATCH_NOTE_TOOL_SOFT_CAP = 25` is an exported, named constant in `ComposeCommentGutter.tsx` (trivially
  tunable — a single edit, no config plumbing, per the task's own "cap value a constant that is trivially
  tunable" instruction).
- Cap confirm is a Fluent `Dialog` ("N notes — this will run sequentially and may take a while") that gates
  ONLY the Run click when the selection exceeds the cap — "select all" itself never confirms (matches the UI
  test scenario's literal sequence: select-all first, confirm only appears on Run).
- **Self-review fix applied before quality gates**: the sub-toolbar's displayed "N selected" count and the
  "select all" visibility condition originally read `checkedIds.size` (the raw Set). Fixed to read
  `orderedCheckedIds.length` (the `threads`-filtered, actually-dispatchable count) — avoids a stale/inflated
  count if `threads` shrinks out from under a checked id (e.g. the "Review Notes" visibility toggle turned off
  then back on while notes are checked).

## Step 3 — sequential loop + progress + failure isolation + summary

- **`batchNoteToolRunner.ts`** (new, pure, no editor/DOM dependency): `runBatchNoteTool(threadIds, runOne,
  onProgress)` — a plain `for...of` with `await runOne(threadId)` per iteration (never `Promise.all`/
  `allSettled`/fire-and-forget). A rejected `runOne` is caught, recorded as a failed outcome, and the loop
  CONTINUES (failure isolation). Reports progress before AND after each note. Directly unit-tested for the
  ADR-016 "never >1 in flight" guarantee via an explicit in-flight counter + a deferred-promise gate proving
  note N+1 is never even STARTED until note N settles.
- **`ComposeBatchNoteToolProgressModal.tsx`** (new): a persistent Dialog (ASSISTANT-UI-ELEMENT-CRITERIA
  "persistent operation indicator", not a chip) mirroring `NdaReviewProgressModal`'s established
  Dialog/DialogSurface/DialogBody/tokens/`modalType="alert"` structure — but LIGHTENED per the task's own
  "reuse or lighten" instruction: `NdaReviewProgressModal`'s horizontal step-chip track assumes a small fixed
  step count (it SYNTHESIZES fake progress for a single whole-document call with no real per-stage signal);
  a batch of up to 25+ notes has REAL per-note completion events, so a determinate `ProgressBar`
  (`completed/total`) + "Note X of N" is the honest, appropriately-sized representation instead. `AiProgressStepper`
  (`@spaarke/ui-components`) was evaluated and rejected for the same reason (its chip track does not scale to
  ~25 steps). An all-success run auto-dismisses after a short linger (mirrors `NdaReviewProgressModal`'s
  `COMPLETE_LINGER_MS`); a run with ANY failure stays open with an explicit Close button + a per-note failure
  list (location + error) so failure information is never missed.
- **ADR-041 compliance**: the modal renders ONLY the batch-level rollup (counts + failure list) — it never
  substitutes for a note's own Assistant confirmation, which continues to render via the UNCHANGED
  `dispatchComposeAction` → `makeComposeEditControlsMessage` path each time a note's dispatch resolves (Step 0.5).
  No new outcome shape, no second completion/session-state store.
- **`ComposeEditor.tsx` wiring**: `runBatchNoteToolAsync` guards (`editor`/`enqueueComposeAction`/`sessionId`/
  tool bindingId+surface, mirroring `runNoteTool`'s own guards), gathers a free-text `inputPrompt` instruction
  ONCE up front (applied to every selected note — a per-note prompt across up to 25 notes would be
  impractical; explicitly a directional-mode decision, noted here since the POML did not specify it),
  then drives `batchNoteToolRunner.runBatchNoteTool`. `runBatchNoteTool` (the prop wired to the gutter) is a
  thin `void`-returning wrapper, matching `onRunNoteTool`'s own fire-and-forget convention — the async
  lifecycle lives entirely in `ComposeEditor.tsx`'s `batchRun` state.

## Step 4 — tests (70 new/extended, all green)

- **`batchNoteToolRunner.test.ts`** (9 tests, pure): never >1 in flight (in-flight counter); explicit
  deferred-promise proof that note N+1 is not even started before note N settles; strict input-order execution
  regardless of which note would resolve fastest; mid-batch rejection isolated + loop continues; non-Error
  rejection reasons stringified; every-note-fails still resolves (never throws out of the batch); progress
  reported before/after each note; empty input is a no-op.
- **`ComposeBatchNoteToolProgressModal.test.tsx`** (4 tests, presentational): determinate progress + "Note X of
  N" while running; auto-dismiss on all-success; stays open + failure list + Close on any failure; ADR-021
  dark-mode/no-hex check.
- **`ComposeCommentGutter.test.tsx`** (+13 tests, extends the existing 37): no checkboxes/sub-toolbar when
  `onRunBatchNoteTool` is not wired (no regression for standalone mounts); checkboxes render + toggle without
  selecting/expanding the card; the checkbox is a REAL keyboard-accessible `role=checkbox`; "select all" shows
  only at exactly 1 selected and selects every thread; "Clear" empties selection; the dropdown offers exactly
  `noteTools` (no batch-only actions) and Run dispatches + clears selection; Run stays disabled until an action
  is chosen; selecting `cap+1` shows confirm BEFORE dispatch, Cancel dispatches nothing and preserves selection;
  "Run anyway" dispatches all `cap+1`; exactly `cap` dispatches with no confirm; `isBatchRunning` disables the
  dropdown/Run (checkboxes stay interactive); ADR-021 dark-mode/no-hex for the sub-toolbar + cap-confirm dialog.
- **New `ComposeEditor.batchNoteTool.test.tsx`** (4 tests, integration, against the REAL `ComposeEditor` — mirrors
  `ComposeEditor.advisoryComments.test.tsx`'s mount convention + `ComposeEditor.aiToolbarTriggers.test.tsx`'s
  `getEditorInstance`/`coordsAtPos`-stub pattern, since `ComposeCommentGutter` positions/renders a card only
  once `coordsAtPos` resolves — unreliable in jsdom without spying on the real editor instance, per
  `ComposeEditor.bidirectionalHighlight.test.tsx`'s own note):
  1. selecting 3 real, `placeAdvisoryComments`-seeded notes + Run calls the injected `enqueueComposeAction`
     prop exactly 3 times, with an in-flight counter proving max 1 in flight — the ADR-016 assertion at the
     full-component level, not just the pure-loop level;
  2. a batch request for a given thread is `.toEqual()`-identical in `args.slots` (and same `bindingId`/
     `documentSessionId`) to what a single-note ⋮-menu run builds for the SAME thread — proves the "byte
     equivalent" acceptance criterion empirically, not just by code-path inspection;
  3. a mid-batch rejection (2nd of 3 calls throws) still dispatches all 3, and the modal's terminal summary
     shows "2 succeeded" + "1 failed" + a Close button;
  4. zero notes selected renders no sub-toolbar and dispatches nothing.
  - **Test-authoring finding**: `placeAdvisoryComments` mints each thread's `id` internally
    (`useComposeCommentThreads.createThread`, not caller-supplied) — an early version of this suite assumed
    literal ids ("thread-1" etc., valid only in `ComposeCommentGutter.test.tsx`'s isolated fixture harness) and
    every test failed to find any checkbox. Fixed by reading the real ids back out of the DOM
    (`data-testid^="compose-comment-gutter-checkbox-"`) after `waitFor`-ing the post-`createThread` React state
    update to flush.

## Step 5 — quality gates (self-run, FULL rigor)

- **Typecheck**: `npx tsc --noEmit` in `Spaarke.Compose.Components` — clean, zero errors.
- **Tests — Compose.Components full suite**: `851 total, 836 passed, 15 failed`. The 15 failures are the EXACT
  pre-existing set the task brief named (`ComposeWorkspace.{bornInEditorSave,imports,saveOpLogPreservation,
  search}.test.tsx` + `stepOperationInterceptor.test.ts`), confirmed by suite-name match — zero new failures,
  zero regressions in any other suite.
- **Tests — SpaarkeAi full suite**: `89 suites / 826 tests, all green` — matches the documented baseline
  exactly. `ConversationPane.tsx` was never touched by this task, so this is a straightforward confirmation, not
  a risk area.
- **Lint**: `npm run lint` (ESLint) fails to run in this environment — repo-wide ESLint v9 flat-config migration
  gap ("ESLint couldn't find an eslint.config.js"), unrelated to this task's files; not attempted to fix
  (pre-existing environment state, pure infra issue, out of this task's scope — flagged for owner awareness).
- **ADR-016** (sequential batch, one dispatch in flight): proven at BOTH the pure-loop level
  (`batchNoteToolRunner.test.ts`) and the full-component level (`ComposeEditor.batchNoteTool.test.tsx`), via an
  explicit in-flight counter in each. Pass — matches design.md's own "ADR Tensions: ADR-016 — Path C, comply"
  entry (no amendment needed).
- **ADR-021** (Fluent v9, semantic tokens, dark mode): all new UI (`Checkbox`, `Dropdown`/`Option`, `Dialog`
  cap-confirm, `ComposeBatchNoteToolProgressModal`) uses `@fluentui/react-components` + `tokens.*` exclusively;
  automated no-hex-literal assertions cover both light and dark mode for every new surface. Pass.
- **ADR-041** (no new outcome shape, store-before-render): confirmed by construction in Step 0.5/Step 3 — every
  per-note outcome still renders via the unchanged `dispatchComposeAction`/`makeComposeEditControlsMessage`
  path; the batch modal is an additive, non-persisted, client-only rollup. Pass.
- **CLAUDE.md §11 (component justification)**: `batchNoteToolRunner.ts` — Existing: no sequential-with-failure-
  isolation loop exists today (`useSerialActionQueue` serializes but doesn't isolate/report per-item failure at
  the CALLER'S batch-of-N granularity). Extension: not possible to extend the queue itself without changing its
  single-dispatch contract for every OTHER caller. Cost-of-doing-nothing: a reviewer must run each note
  one-by-one — the "batch a review pass" ask is unmet (the literal design.md rationale). `ComposeBatchNoteToolProgressModal.tsx` —
  Existing: `NdaReviewProgressModal` assumes synthesized single-run steps, wrong shape for real per-note
  progress. Extension: forking its internals to support two incompatible progress models (synthesized-timer vs
  real-event) would blur one component's invariants; a second, purpose-built component is the reuse-first move
  (it still reuses the SHIPPED Dialog/tokens/dark-mode STRUCTURE verbatim, per the file's own header). Cost-of-
  doing-nothing: no persistent indicator for a batch that can take a while — the reviewer has no way to tell the
  run is progressing vs stuck.
- **Self-review finding + fix** (Step 2 above): the stale-count bug (`checkedIds.size` vs `orderedCheckedIds.length`)
  was found and fixed BEFORE this quality-gate pass, not left as a documented known issue.

## Acceptance criteria — evidence

| Criterion | Status | Evidence |
|---|---|---|
| N selected → N sequential dispatches, never >1 in flight, visible progress | ✅ Pass | `batchNoteToolRunner.test.ts` (pure) + `ComposeEditor.batchNoteTool.test.tsx` test 1 (full component) |
| Each note's outcome byte-equivalent in form to an individual run | ✅ Pass | `ComposeEditor.batchNoteTool.test.tsx` test 2 (`.toEqual()` on `args.slots`) — holds by construction (Step 0.5) |
| Mid-batch failure continues; summary reports success/failure per note | ✅ Pass | `batchNoteToolRunner.test.ts` failure-isolation tests + `ComposeEditor.batchNoteTool.test.tsx` test 3 + `ComposeBatchNoteToolProgressModal.test.tsx` |
| Action list = `getToolsForSurface` scoped set; no batch-only actions | ✅ Pass | by construction — the sub-toolbar reuses `noteTools` verbatim (Step 2); `ComposeCommentGutter.test.tsx` "no batch-only actions" test |
| Zero-selected → no sub-toolbar; >cap → confirm before any dispatch | ✅ Pass | `ComposeCommentGutter.test.tsx` (unit) + `ComposeEditor.batchNoteTool.test.tsx` test 4 (zero-selected, full component) |

**Deferred to 060/061 per the task brief**: live UI tests (real batch run against a deployed environment,
select-all + cap confirm click-through, dark-mode toggle mid-flow) — noted, not attempted here (no `--chrome`
session / deployed environment in this task's scope).

## Deviation / escalation summary

**One deviation from the POML's literal `<relevant-files>` list**: the sequential batch loop was built in
`ComposeEditor.tsx` instead of `ConversationPane.tsx` (Step 0.5 above). `ConversationPane.tsx` is completely
UNTOUCHED by this task — no risk to the 021→031→041→042 hot-file sequence's next consumer (042, "separated
location-labelled confirmations"), which still finds its own baseline commit exactly where it expects it.
Not a HARD BOUNDARY violation, not an ADR conflict (§6.5 N/A) — a task-scope adaptation under directional
mode, disclosed per root CLAUDE.md §8.5 and consistent with task 040's own precedent for the identical
underlying reason (the data a piece of wiring needs determines where it can honestly live).

No other deviations. No human-escalation triggers fired (root CLAUDE.md §6/§6.5) — no ambiguous requirement,
no security-sensitive surface, no ADR conflict, no breaking API/schema change, no scope expansion beyond the
task boundary.
