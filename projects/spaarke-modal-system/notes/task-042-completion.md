# Task 042 Completion — P2 Retire ActionConfirmationDialog Overlay → ConfirmModal (FR-13)

> RIGOR: FULL. Executed per task-execute protocol. Dependency (005 — ConfirmModal preset) confirmed present and used as-is (not forked).

## Summary

`ActionConfirmationDialog.tsx` — a hand-rolled `position:absolute` div (despite an in-file
comment wrongly claiming it used a Fluent Dialog / was "not a custom modal") — is **deleted**.
Its sole consumer, `SprkChat.tsx`'s HITL action-confirmation flow (`pendingAction` state, set
by the `action_confirmation` SSE event), is re-routed onto the shared `ConfirmModal` preset
(`SprkModal/presets/ConfirmModal`, built task 005), used unmodified. A11y (focus trap, ESC,
`aria-modal`/`alertdialog`) now comes from the Fluent `Dialog` inside `ConfirmModal` instead of
the overlay's bespoke (and broken) listeners.

## Consumer inventory + re-route decision

**Repo-wide grep for `ActionConfirmationDialog`** (before deletion) found exactly **one real
code consumer**: `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx`
(import + one render call at the end of the component, plus 3 JSDoc comments referencing it by
name). It was **never exported** from `SprkChat/index.ts` or the top-level `components/index.ts`
barrel (confirmed by reading both files) — so there was no public/external entry point to keep
stable; the type `IActionConfirmationDialogProps` was also never exported outside its own file.

All other repo hits were non-code: project markdown (specs/notes/TASK-INDEX for
*this* project and several unrelated historical projects), and three files clearly **outside
this task's write boundary** that reference the dialog only in prose comments (not imports):
`src/solutions/SpaarkeAi/src/components/compose/{gateAssociationContract.ts,
CreateOnSaveAssociationPrompt.tsx, CreateOnSaveAssociationGateDialog.tsx}` (one of these
literally says in its own header comment: "OWNED BY A DIFFERENT SURFACE than this task's write
boundary") and three BFF C# files (`GateDecisionV2.cs`, `SideEffectGateAIFunction.cs`,
`ChatEndpoints.cs`) — server-side, out of scope for this client-only task (NFR-05). None of
these import the component or the deleted file; left untouched.

**Call-contract preserved via `ConfirmModal`'s existing public props** (no fork):

| Original `IActionConfirmationDialogProps` behavior | `ConfirmModal` mapping |
|---|---|
| Title: `Confirm Action: {actionName}` | `title={`Confirm Action: ${pendingAction.actionName}`}` |
| Body: `summary` text | `message` includes `action.summary` verbatim |
| Body: parameters key/value box (shown only when `Object.entries(parameters).length > 0`) | Preserved — a small local helper (`renderActionConfirmationMessage`) composes `message` as `summary` + a token-styled parameters box (ported styles), passed through `ConfirmModal`'s `message: ReactNode` slot. This is real body copy (e.g. "Recipient: john@example.com") the user needs before confirming a side-effecting action, not decorative chrome — dropping it would have been a UX/safety regression, so it was NOT simplified away. |
| Confirm button: "Confirm" / "Confirming..." while dispatching | `confirmLabel={isConfirmingAction ? 'Confirming…' : 'Confirm'}` |
| Cancel button: "Cancel" | `cancelLabel="Cancel"` (ConfirmModal default, unchanged) |
| Both buttons `disabled={isConfirming}` while the async dispatch is in flight | **Preset gap** (see Deviations #1) — mitigated via guarded call-site wrappers, not a `ConfirmModal` prop |
| Never used destructive/danger styling (always `appearance="primary"`) | `destructive` prop omitted (default false) |
| `onConfirm(pendingAction)` / `onCancel()` | `handleConfirmModalConfirm` / `handleConfirmModalClose` wrappers → `handleActionConfirm(pendingAction)` / `handleActionCancel()` (both pre-existing, unchanged handler bodies) |

## Dismissal-semantics mapping — evidence

Read `ActionConfirmationDialog.tsx` in full to capture its **actual** (not commented) dismiss
behavior:

- **Backdrop click**: **not wired at all.** The outer `overlay` div had no `onClick` handler —
  clicking anywhere on the dim background did nothing. Confirmed by re-reading the full JSX;
  no `onClick` prop exists on `styles.overlay`.
- **ESC**: `onKeyDown` was attached to the outer `overlay` div (`role="dialog"`, no `tabIndex`),
  calling `onCancel()` on `Escape` — but only when the keydown event **bubbles from a focused
  descendant**. Since nothing auto-focused on mount (no focus trap) and the div itself isn't
  focusable, ESC only ever fired if the user had already Tab'd/clicked into the Cancel/Confirm
  button. This is incidental, not a deliberate light-dismiss guarantee.
- **Buttons**: both Cancel and Confirm were `disabled={isConfirming}` during the async dispatch.

Given no genuine backdrop-dismiss and only incidental ESC, this is functionally much closer to
`alert` (no light-dismiss) than to `light` — and semantically more correct for a HITL gate
guarding a side-effecting action (matches how the design's own inventory treats sibling `xs`
confirms — `PinnedMemoryDeleteConfirmation`, `ComposeConflictDialog` — as `Dialog(alert)`).
`ConfirmModal`'s own contract (task 005) is **hard-wired to `dismiss="alert"`** unconditionally
(confirmed by reading `ConfirmModal.tsx` and its test: `screen.getByRole('alertdialog')`), so no
per-call choice was needed or possible — using it as-is is the correct, safest match. Verified
with a new test (`escapeKey_DoesNotDismiss_AlertModalBlocksLightDismiss`): ESC no longer does
anything while a confirmation is pending — an a11y-predictability improvement, not a regression,
since the old ESC behavior was never reliable to begin with.

One **added** behavior, by design: `ConfirmModal`'s header now always shows a **×** (project-wide
P1 window-controls mandate, FR-03/FR-12 — "every modal" gets the standard maximize/× cluster; here
`maximizable={false}` so only × appears). The × calls `onClose` — wired to the SAME
`handleConfirmModalClose` as the Cancel button, i.e. × ≡ Cancel semantically. This is an
intentional project-wide addition (the original overlay had zero window controls), not a
gap to preserve against.

## Anchor / mount investigation (escalation trigger — result: CLEARED, no stop)

The task's escalation trigger requires stopping if the overlay's non-portal, in-tree mount was
relied on for message-relative positioning, in-flow layout, or scroll containment. Investigated:

- The overlay's own CSS was `position: 'absolute'` (**not** `fixed`) with `top/left/right/bottom:
  0`, rendered as a **direct child of `SprkChat`'s own root div** (`styles.root`), near the very
  end of `root`'s children — **after** the input zone and hidden file inputs, and **outside**
  `messageList` (the only div in this file that sets `position: 'relative'`, at line ~149). So the
  overlay was never anchored to a specific chat message or rendered in-flow inside the scrollable
  transcript.
- `SprkChat`'s `root` div does **not** set `position: relative/absolute` — only `display: flex,
  flexDirection: column, height: 100%, overflow: hidden`. Per CSS, the overlay's actual
  *containing block* (for its `top/left/right/bottom: 0` offsets) is therefore whichever ancestor
  **outside** `SprkChat` happens to be positioned (or the viewport, if none) — not something
  `SprkChat`'s own code deliberately establishes. Any apparent "confinement to the chat panel" in
  today's host layouts is a side effect of `root`'s `overflow: hidden` clipping the overlay's
  rendered box down to `root`'s visible rectangle — an accidental consequence of the CSS, not an
  intentional anchor, scroll-containment mechanism, or documented design requirement.
- Cross-checked against the design document's own inventory (design.md §3.3): `ActionConfirmationDialog`'s
  row documents **no** technical reason for the hand-roll (just "reinvents surface+backdrop").
  Contrast with the sibling overlay `ConversationModal` (a *different*, P5-scoped task), whose row
  explicitly documents "abandoned Fluent Dialog over a transform-ancestor centering bug" — a real,
  escalation-worthy reason that project deliberately schedules as its own high-risk phase. No such
  reason exists for this dialog. Design §6.2's own size-scale table also groups
  `ActionConfirmationDialog` under the same `xs` (confirms/deletes/HITL) row as `ChoiceDialog`
  default / `PinnedMemory*` / `ComposeConflictDialog` — all ordinary, full-viewport-centered Fluent
  `Dialog`s — confirming the design's own intent is for this dialog to become exactly that, not a
  panel-scoped special case.
- The component's own in-file JSDoc self-contradicts ("Does NOT use... custom modal (per ADR-021
  constraint)" while its markup patently is one) — evidence of an authoring oversight, not a
  deliberate architectural choice worth preserving.

**Conclusion: no escalation warranted.** Cleared to proceed with the portaled `ConfirmModal` as a
drop-in replacement.

## Grep proof (negative criterion)

```
# position:fixed / position:absolute anywhere in components/SprkChat/
src/.../SprkChatUploadZone.tsx:104:    position: 'absolute',       # drag-and-drop hint, role="region"
src/.../SprkChatActionMenu.tsx:67:     position: 'absolute',       # dropdown menu, no role attr
src/.../SprkChatInput.tsx:288:         (comment: SlashCommandMenu position:absolute)
src/.../SprkChatHighlightRefine.tsx:64: position: 'absolute',       # floating selection toolbar, role="toolbar"
src/.../SprkChat.tsx:62, 2916:         (my own doc comments referencing the RETIRED pattern)

# role="dialog" / role="alertdialog" in actual code (not comments) anywhere in components/SprkChat/
→ ZERO matches

# createPortal anywhere in components/SprkChat/
→ ZERO matches

# createElement('div', ...) anywhere in components/SprkChat/
src/.../SprkChatMessageRenderer.tsx:440   # plain markdown-root div, not an overlay
src/.../SprkChatActionMenu.tsx:622        # plain list-container div, not an overlay
```

None of the remaining `position:absolute` usages carry `role="dialog"`/`role="alertdialog"` (verified
per-file: `role="region"`, no role, `role="toolbar"` respectively) — **no hand-rolled overlay
pattern remains in `components/SprkChat/`.**

## Files modified / deleted

- **DELETED**: `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/ActionConfirmationDialog.tsx`
- **MODIFIED**: `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx`
  — swapped the import for `ConfirmModal` (`../SprkModal/presets/ConfirmModal`); added 4 ported,
  token-only style keys (`actionConfirmParameters`, `actionConfirmParameterRow`,
  `actionConfirmParameterLabel`, `actionConfirmParameterValue`) to the existing `useStyles` object;
  added the module-scope `renderActionConfirmationMessage` helper; added two guarded `useCallback`
  wrappers (`handleConfirmModalClose`, `handleConfirmModalConfirm`); replaced the
  `<ActionConfirmationDialog>` render block with `<ConfirmModal>`.
- **MODIFIED** (stale-comment hygiene, in-scope collaborators of the flow just re-routed):
  `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/types.ts` (one JSDoc line on
  `IPendingAction`) and `.../hooks/useActionHandlers.ts` (two JSDoc lines on
  `dispatchConfirmedAction`/`rejectPendingAction`) — updated stale "ActionConfirmationDialog"
  prose references to name `ConfirmModal` + point at this task, since both files are direct
  collaborators in the confirm/cancel flow this task modified. No logic changed in either file.
- **NEW**: `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/__tests__/actionConfirmationIntegration.test.tsx`
  — 5 tests (see Verification). No dedicated `ActionConfirmationDialog.test.tsx` existed to
  "update" (verified — none exists), and `SprkChat.test.tsx` never covered this flow either, so
  this is new, purpose-built coverage for the re-routed behavior, modeled on the existing
  `actionOutcomeIntegration.test.tsx` harness (same SSE-mock-driven end-to-end render pattern).
- **Not modified** (per hard boundary): `ConfirmModal.tsx`, `SprkModal.tsx`, `SprkChat/index.ts`,
  `components/index.ts`, `pcf-safe.ts` (grepped — no `ActionConfirmationDialog` reference existed
  there to remove), `TASK-INDEX.md`, `current-task.md`, `.claude/**`.

## Verification

| Check | Command | Result |
|---|---|---|
| TypeScript (shared lib) | `npx tsc --noEmit` (from `Spaarke.UI.Components/`) | **PASS** — exit 0, zero errors (run twice: after the SprkChat.tsx edit, and again after the two comment-only hygiene edits) |
| Scoped Jest | `npx jest src/components/SprkChat` | **PASS** — 29 suites, **357/357 tests**, zero failures, zero new regressions (includes the 5 new `actionConfirmationIntegration.test.tsx` tests + the pre-existing 352) |
| `npm run build` (shared lib) | — | **Intentionally NOT run** per this wave's build discipline (3 parallel agents share `Spaarke.UI.Components`'s `dist/`) — main session runs the consolidated build + full suite + consumer builds after the wave |
| PCF / Code Page consumer builds | — | Deferred to main session's post-wave consolidated build per explicit wave instructions. Note: `SprkChat` is documented (ADR-012 inventory) as **Code-Pages-only** today — no PCF control imports it (confirmed: zero `src/client/pcf/**` hits for `SprkChat` or `ActionConfirmationDialog`) — so there is no PCF consumer surface for this specific change to independently exercise |

New test file (`actionConfirmationIntegration.test.tsx`) exercises, end-to-end (real SSE mock → real
`useSseStream` → real `SprkChat` → real `ConfirmModal`/Fluent `Dialog`):
1. `action_confirmation` SSE event renders the alertdialog with title, summary, and the parameters
   box (body-copy preservation).
2. Confirm click → POSTs `{approved: true}` to the correct gate-resolve URL, closes the dialog,
   renders the ✅ completion message in the transcript.
3. Cancel click → closes the dialog immediately, POSTs `{approved: false}` to gate-resolve, and
   does **not** render any completion message (action never executed).
4. ESC does not dismiss (`dismiss="alert"` blocks light-dismiss) — a11y parity check.
5. Dark-theme smoke render (title + parameters box visible under `webDarkTheme`).

## Step 9.5 gates (FULL rigor, self-run)

**Self code-review of the diff:**
- Dead code fully removed: the overlay's `overlay`/`dialog`/`header`/`headerIcon`/`title`/
  `summary`/`parametersSection`/`parameterRow`/`parameterLabel`/`parameterValue`/`actions` styles,
  its bespoke `handleKeyDown` ESC listener, and its `Divider`/`Spinner`/`CheckmarkRegular`/
  `DismissRegular`/`ShieldCheckmarkRegular` icon usage are all gone with the file. No dangling
  imports remain anywhere in the package (`tsc --noEmit` is whole-package and clean).
- No new abstractions introduced — `ConfirmModal` is used exactly as published by task 005.

**adr-check:**
- **ADR-012** (compose, don't fork): `ConfirmModal.tsx`/`SprkModal.tsx` untouched (`git status`
  shows only the 3 SprkChat-folder files + the new test); consumed via import + render only.
- **ADR-021** (tokens only): the deleted overlay was, on inspection, **already token-clean** —
  zero hex colors and zero `'1px'` border-width literals in its own styles (it correctly used
  `tokens.colorBackgroundOverlay`, `tokens.colorNeutralBackground1`, etc. throughout; its only
  literal-px values were `maxWidth:'480px'`/`fontSize:'24px'`/`minWidth:'100px'` dimensional
  sizing, not border/color). So there is **nothing to report as hex/'1px'/inline-color removed**
  — this retirement's ADR-021 relevance is purely structural (a non-Fluent envelope bypassing
  `Dialog`), not a token cleanup. My added styles are equally token-only (verified below).
- **NFR-04** (dual React compile): scoped `tsc --noEmit` runs under this package's own
  `@types/react@^19.0.0` pin (Code-Page side) and is clean. `SprkChat` has no PCF consumer today
  (ADR-012 inventory + verified by grep), so the React-16/17 boundary isn't independently
  exercised by this specific change; nothing in the diff uses a React-18/19-only API.
- **NFR-05** (client-only): trivially satisfied — no BFF files touched.

**Diff gate:** `git diff` on added (`+`) lines across the 3 modified source files, grepped for
`#[0-9a-fA-F]{3,8}`, `'1px'`/`"1px"`, `style={{[^}]*color` → **zero real matches** (the one hit was
my own doc-comment text quoting the rule itself, and a `GitHub #234` issue-number false positive
on the hex regex — not an actual style violation).

## POML acceptance-criteria checklist

| # | Criterion | Pass/Fail |
|---|---|---|
| 1 | Action confirmation renders via `ConfirmModal` (Fluent `Dialog`), not a hand-rolled overlay, unchanged confirm/cancel behavior + messaging | **PASS** — verified end-to-end by `actionConfirmationIntegration.test.tsx` |
| 2 | `ActionConfirmationDialog.tsx` deleted, removed from barrel/exports, no dangling import | **PASS** — file deleted; it was never barrel-exported to begin with; `tsc --noEmit` clean |
| 3 | Negative grep: no `position:fixed`/`position:absolute` + `role="dialog"` (or `createElement`/`createPortal` overlay hand-rolls) in `components/SprkChat/` | **PASS** — see Grep proof above |
| 4 | Working focus trap + `aria-modal` + correct ESC from Fluent `Dialog`; no bespoke dismissal listener; no hex/`'1px'`/inline color remains from the overlay | **PASS** — Fluent `Dialog`/`alertdialog` role confirmed by test; bespoke `handleKeyDown` deleted with the file; overlay had no hex/'1px'/inline-color to begin with (see adr-check) |
| 5 | Shared-lib build + SprkChat consumers build green under `@types/react` 18 and React 19 | **PARTIAL, by design** — shared-lib `tsc` **PASS**; full cross-surface consumer build **deferred to main session's post-wave consolidated build** per this wave's explicit build discipline (SprkChat has no PCF consumer to independently verify against regardless) |

## Deviations / escalations

No hard STOP. One preset-gap deviation, reported per the task's own instruction ("preset gap =
escalate/report"), mitigated at the call site rather than by forking:

1. **No busy/disabled affordance on `ConfirmModal`'s buttons.** The retired overlay disabled
   *both* Cancel and Confirm (visually grayed + inert) for the duration of the async gate-resolve
   dispatch, and swapped the Confirm icon for a spinner with a "Confirming..." label. `ConfirmModal`
   (task 005) exposes no `disabled`/busy prop on either footer button, and forking it is out of
   bounds for this task. Mitigation: `handleConfirmModalClose`/`handleConfirmModalConfirm` guard
   against re-entrancy (`if (isConfirmingAction) return;`) at the call site, so double-submitting
   Confirm or cancelling mid-dispatch is a functional no-op — the underlying HITL correctness (one
   dispatch, not a race) is preserved. The `confirmLabel` text still swaps to "Confirming…" (using
   `ConfirmModal`'s existing public prop, no fork), so the user gets a textual busy signal; what's
   lost is the visual grayed-out/disabled button state and the spinner glyph — a minor, low-risk
   cosmetic gap, not a functional regression. **Recommendation**: if this pattern recurs across
   other confirms being re-based this wave, an optional `busy?: boolean` prop on `ConfirmModal`
   (disabling both footer buttons + optionally swapping the Confirm icon) would be a small,
   non-breaking, additive fix for the task-005 owner/a follow-up — not this task.

No other deviations. No fork of `ConfirmModal`/`SprkModal` was needed or performed. No `.claude/**`,
`TASK-INDEX.md`, or `current-task.md` files were touched (main session to update `TASK-INDEX.md`
042 → ✅ per the wave's established pattern).

## Post-completion follow-up (2026-08-02): full-suite flake in `actionConfirmationIntegration.test.tsx` — root cause + fix

**Symptom** (coordinator report): the suite passed 5/5 standalone every time, but under the FULL
`Spaarke.UI.Components` jest run (199 suites, parallel workers) 1-2 tests failed
nondeterministically — which test varied per run (observed across runs: tests 2, 3, 4, and 5;
test 1 never) — presenting as "`findByRole('alertdialog')` times out; user message + empty
assistant bubble in the DOM; ConfirmModal never opens". The coordinator's timeout bumps
(`findByRole` 5000ms / `waitFor` 3000ms) did not help.

**Root cause — TWO independent, load-only mechanisms, peeled in sequence:**

**Layer 1 — jest's default 5s whole-test timeout.** The first reproduced failures were literally
`thrown: "Exceeded timeout of 5000 ms for a test."` (jest's per-test budget; no `jest.setTimeout`
exists anywhere in `src/`, verified) — NOT the assertion-timeout form. Unlike the sibling
`actionOutcomeIntegration` suite, every test here mounts a Fluent `Dialog` portal (Tabster focus
trap) and interacts inside it; under parallel-worker CPU contention the cumulative wall time
(render + waitFor + typing + click + dialog mount) stretched past the fixed 5s budget, killing
the test mid-flight while it was legitimately waiting. This also explains why bumping `findByRole`
to 5000ms could never work: a 5s assertion wait cannot fit inside a 5s whole-test budget already
partly consumed — the whole-test timeout always fired first, reading as "dialog never appears".

**Layer 2 — a React 19 scheduler race dropping the dialog leg of the update cascade.** With the
test budget raised, the failures CHANGED FORM into real `Unable to find role="alertdialog"`
assertion timeouts — the dialog genuinely never opened with seconds of budget available (the
coordinator's original read was right for this layer). Successive instrumented failing runs
(temporary harness diagnostics: reader-lifecycle trace, TextDecoder output trace, fetch-URL log,
SSE-FIFO depth, composer-state probe, act-flush rescue probe) pinned the break precisely:
- the `/messages` POST was made exactly once and the queued SSE Response was consumed;
- the stream reader ran to completion (`getReader → read:chunk(269B) → read:done`);
- `TextDecoder.decode` yielded the full 269-character payload — the frames parsed and the
  setStates (`setPendingActionEvent` + `setIsDone` + `setIsStreaming(false)`) were invoked;
- the state BATCH itself **committed** — the composer textarea re-enabled (`disabled: false`),
  proving `isStreaming=false` landed in the DOM;
- yet the `pendingActionEvent` DISPATCH EFFECT never fired: no `setPendingAction`, no dialog, no
  gate-resolve call, no error banner, zero React act() warnings;
- and NOTHING could recover it after the fact: a later microtask-only `act(async () => {})`
  failed; ONE post-send act+macrotask flush failed (FAIL/PASS/FAIL over 3 full runs); ~10s of
  REPEATED act+macrotask polling per wait failed too (FAIL/PASS/FAIL again). Because the
  dispatch effect's deps never change after the drop, no later nudge can re-trigger it — rescue
  is impossible; only prevention works.

Environment facts (probed empirically): this jest jsdom environment has NO `ReadableStream`
(both suites' identical polyfill installs), NO `MessageChannel`, and NO `setImmediate` — so
React 19's concurrent scheduler falls back to `setTimeout`. The mocked SSE chain resolves
entirely in microtasks, so WHERE its work lands is timing-dependent: inside a user-event/RTL act
block it flushes synchronously (passing-run stack traces show `flushActQueue` — the happy path);
when parallel-worker load shifts it into the gap where no act block is open, it routes to the
concurrent scheduler, where the deferred passive-effect flush for the committed batch is dropped
under contention. Which test lost the race varied with load — matching every observed symptom
(test 1 never failed; later tests failed randomly).

Ruled out with evidence along the way: fetch-router/FIFO misalignment (`loadHistory` runs only
on the resume path; nothing else matches `/messages`); a session-null drop in
`handleActionConfirmationEvent` (`handleSend` guards `!session`, `/sessions` POSTed exactly once
— no remount — and `setSession(null)` exists only in `deleteSession`); stream errors (no error
banner; the silent-abort path can't fire — the mock ignores the AbortSignal); dialog
opened-then-closed (would have left a `/gates/…/resolve` POST — none); `useDynamicSlashCommands`
404 noise (caught + swallowed; identical in passing sibling runs); native-vs-polyfill stream
delta (neither env has native; both files install the identical polyfill).

**Fix (this one test file only; assertions untouched; component code untouched). Final shape —
ALL-LEGS PREVENTION, after two rescue-based variants were empirically disproven under load
(one post-send flush: FAIL/PASS/FAIL; ~10s repeated poll-with-flush: FAIL/PASS/FAIL — the
dropped dispatch effect is unrescuable) and a first prevention variant left one hole (a fixed
2s in-act release window could miss a late-arriving fetch, parking its response forever —
PASS/PASS/FAIL):**
1. **Deferred mock responses + release inside every flush (the primary fix, Layer 2).** The
   fetch router PARKS the `/messages` and `/gates/…/resolve` responses on a pending promise
   (call-time recording unchanged). `flushPendingWork()` — the core determinism primitive —
   releases anything parked INSIDE an open `act()` block and yields one real macrotask there,
   so the released chain (response → reader loop → state batch → passive-effect cascade →
   dialog render) runs entirely with the act queue open and never touches React's real
   concurrent scheduler. It runs after the send / confirm / cancel clicks AND once per poll
   iteration of every wait, so even a fetch that reaches the router late is released in-act on
   the next iteration — no parked response can ever strand a wait.
2. **Release-capable waits.** `findAlertDialogWithFlush(10s)` (poll + flush per iteration;
   falls through to `getByRole` at deadline for RTL's standard error + DOM dump) replaces the
   bare `findByRole` calls; `waitForWithFlush(10s)` (same shape, same assertion errors)
   replaces the bare `waitFor`s on the confirm/cancel legs.
3. **Held-open mount act.** The initial render's `act()` stays open for two macrotask turns so
   the mount's session-create chain (`setSession`) lands in-act — a stranded `setSession`
   would leave `handleSend`'s `!session` guard silently no-op'ing forever (same race, one leg
   earlier).
4. `TEST_TIMEOUT_MS = 30000` per `it()` (Layer 1), with the 10s/3s wait ceilings below it so
   genuine regressions fail fast with an assertion error + DOM dump.
5. `TYPED_MESSAGE = 'Go'` instead of a 21-char message (content never asserted; each char is a
   full userEvent roundtrip with a macrotask yield — pure wall-time cost under loaded workers).
   Standalone per-test time: ~110-350ms.
6. Header comment documents both root causes + the probe evidence for future readers. All
   temporary diagnostics (reader trace, decoder trace, DIAG dumps, composer/act-flush probes)
   removed from the final file — verified by grep.

**Verification (final)** — `npx tsc --noEmit` clean; standalone 5/5 (~110-350ms/test); scoped
`npx jest src/components/SprkChat` **357/357** (zero regressions from the harness changes); full
`npx jest` (199 suites, parallel workers) **3 consecutive runs GREEN**:

| Run | actionConfirmationIntegration | Full-suite totals |
|---|---|---|
| 1 | **PASS** (9.7s) | 11 failed suites / 22 failed tests — exact pre-existing baseline |
| 2 | **PASS** (9.0s) | 11 / 22 — exact baseline |
| 3 | **PASS** (10.5s) | 11 / 22 — exact baseline |

Every green run landed at the EXACT pre-existing baseline (the 11 unrelated failing suites the
coordinator instructed to ignore) — i.e., with this suite fixed, the full run's failure set is
precisely the known baseline, nothing more. For contrast, every earlier failing iteration showed
12 / 23-24 (baseline + this suite).
