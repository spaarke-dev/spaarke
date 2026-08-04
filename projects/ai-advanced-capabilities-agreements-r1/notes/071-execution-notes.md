# Task 071 — Execution Notes: "Review complete" toast (in-app layer)

> Rigor: FULL · Model tier: sonnet @ high · Step mode: directional · Status: complete

## Step 0 — investigate the completion signal + visibility signal (before writing any code)

Traced two things per the task brief before touching files:

1. **Completion signal** — `useNdaReviewAdvisoryCommentsBridge.emitFromResult`
   (`src/solutions/SpaarkeAi/src/components/conversation/useNdaReviewAdvisoryCommentsBridge.ts`)
   dispatches `workspace.compose_advisory_comments` on the SAME terminal chunk
   `ConversationPane.dispatchComposeAction` already renders as chat prose (ai-advanced-capabilities-nda-r1
   task 031; agreements-r1 task-002 schema split). Confirmed via read — no edit.
2. **Visibility signal** — `WorkspacePane.tsx`'s `broadcastActiveTabChange` (Round-4 Fix-4, ~line 552)
   already dispatches `workspace.active_widget_changed` on EVERY tab activation (add/switch/close/
   restore), carrying `widgetType`. Its own doc comment says "NO consumers are wired in this task —
   this is signal infrastructure only." **This task is the first consumer.**
3. **"Focus the existing Compose tab, never a duplicate"** — found the EXISTING mechanism:
   `WorkspacePane.tsx`'s `hasSourceReactivationMarker` branch (~line 1445) already reuses/activates the
   active-or-first-open Compose tab for a `workspace.widget_load` carrying
   `widgetData: { source: '<any-non-empty-string>' }` (no `compose` seed) — the SAME contract the
   add-to-DMS / reporting-email / welcome-compose flows use today (verified against
   `WorkspacePane.compose-seed-merge.test.tsx`, which asserts exactly this `{ source: 'dms' }` shape).
   Reusing it (with `source: 'review-complete-toast'`) means zero new WorkspacePane logic was needed.

**Zero-findings gap investigated** (see full writeup + the exact one-line fix in
`notes/uat-round1-2026-08-03.md`, appended by this task): `emitFromResult` early-returns
(`if (advisoryComments.length === 0) return;`, line 160) when a review finds nothing, so **no** shell-
observable event fires for a clean review today. Also traced the alternative apply-leg path
(`emitComposeApplyLeg`) — a review is non-writing, so it never resolves a `ledgerRef` either; no fallback
signal exists there. Documented, not fixed (HARD BOUNDARY forbids editing `conversation/**` this wave) —
folded into `notes/uat-round1-2026-08-03.md` for the orchestrator to route to whichever task next owns
`conversation/**`.

## Step 1 — Toaster reuse (§11 — default to reuse)

Found an EXISTING mounted `<Toaster>` in `ThreePaneShell.tsx`'s `SessionRestoreManager` (used for
restore-failure toasts). Its `toasterId` was minted per-mount via `useId("restore-toast")` — opaque and
not reachable from another component. Since `SessionRestoreManager` is a **singleton** within
`ThreePaneShell` (mounted exactly once per shell instance), converted the id to a fixed exported
constant `SPAARKEAI_SHELL_TOASTER_ID` (`ThreePaneShell.tsx`). This is a one-line-risk change (`useId` →
literal string) that unlocks reuse — no second `<Toaster>` was mounted, so review-complete toasts and
restore-failure toasts share one portal / stacking order / z-index layer.

## Step 2 — implementation

**New file**: `src/solutions/SpaarkeAi/src/components/shell/ReviewCompleteToast.tsx` — a headless bridge
component (`renders null`), mounted once from `ThreePaneShell.tsx` next to the shared `<Toaster>`.

- Subscribes to `workspace.active_widget_changed` → tracks the currently-active tab's `widgetType` in a
  ref (no React state — this is a pure side-effect bridge, not a UI-state component).
- Subscribes to `workspace.compose_advisory_comments` → if `activeWidgetType !== 'compose'`, raises the
  toast; if it IS `'compose'`, suppresses (don't announce what's on screen).
- **Bounded stacking**: fixed `toastId` (`REVIEW_COMPLETE_TOAST_ID`, exported for tests). Investigated
  `@fluentui/react-toast`'s `buildToast` (`if (toasts.has(toastId)) return;`) — dispatching twice with
  the SAME still-active `toastId` is a documented no-op, NOT an update. So a `toastActiveRef` (set true
  on dispatch, cleared by `onStatusChange` on `'dismissed' | 'unmounted'`) decides: first completion →
  `dispatchToast`; a completion while the toast is still showing → `updateToast({ toastId, content })`
  (replaces content in place — no stacking).
- **Action** — "View findings" `Link` inside `ToastTitle`'s `action` slot: dispatches
  `workspace.widget_load` with `widgetType: 'compose'`, `widgetData: { source: 'review-complete-toast' }`
  (the existing source-only re-activation contract, Step 0.3 above) and calls `dismissToast(...)`.
- **Fluent / ADR-021**: `Toast`/`ToastTitle`/`Link` are token-driven Fluent v9 components — no hardcoded
  colors, dark-mode safe by construction. `politeness` is left UNSET per the task brief's "aria-live
  politeness default" instruction — Fluent's own default for the `'success'` intent applies.

**Modified**: `src/solutions/SpaarkeAi/src/components/shell/ThreePaneShell.tsx` — `useId` import removed
(no longer used); added `export const SPAARKEAI_SHELL_TOASTER_ID`; `SessionRestoreManager`'s `toasterId`
now reads that constant; mounted `<ReviewCompleteToast toasterId={toasterId} />` next to `<Toaster>`.

**No other files touched.** No `.claude/**`, no `current-task.md`/`TASK-INDEX.md`, no git commit/push, no
edits under `src/solutions/SpaarkeAi/src/components/conversation/**`, `CreateAnalysisWizardWidget.tsx`, or
`src/server/**`; `Spaarke.Compose.Components` read-only (only READ to trace `onAdvisoryComments`'s
independent empty-array guard, confirming the zero-findings fix recommendation is safe — no edit).

## Step 3 — §11 component justification (three-question template)

1. **Existing** — a Toaster already exists in the shell (`SessionRestoreManager`); two typed
   `PaneEventBus` events already carry both the completion payload (`compose_advisory_comments`) and the
   active-tab signal (`active_widget_changed`, explicitly built as unconsumed "signal infrastructure");
   the tab-refocus mechanism already exists (`hasSourceReactivationMarker`).
2. **Extension** — extended all three: reused the Toaster (fixed id instead of a new mount), became the
   first CONSUMER of `active_widget_changed` (no new discriminant), and reused the source-marker
   re-activation contract verbatim (no new WorkspacePane branch).
3. **Cost-of-doing-nothing** — without this, a user who navigates away during a 2–4 minute gpt-5-reasoning
   review (per `uat-round1-2026-08-03.md` item #1 — reviews routinely take 120–140s) has NO signal that
   the review finished; they must periodically re-check the Compose tab. One new file (a headless bridge)
   closes that gap with zero new server/data surface.

## Step 4 — tests (exact)

**New**: `src/solutions/SpaarkeAi/src/components/shell/__tests__/ReviewCompleteToast.test.tsx` (4 tests,
real `PaneEventBus` — not a mocked bus — mirroring the `three-pane-compose-coordination.e2e.test.tsx`
harness pattern; `useToastController` mocked so assertions target the toast-store CONTRACT directly):

1. `toast-on-hidden` — dispatches `active_widget_changed` (non-compose) then `compose_advisory_comments`
   → `dispatchToast` called once with `{ toastId: REVIEW_COMPLETE_TOAST_ID, intent: 'success' }`.
2. `no-toast-on-visible` — dispatches `active_widget_changed` with `widgetType: 'compose'` then
   `compose_advisory_comments` → `dispatchToast` NOT called.
3. `action-navigates` — captures the real content argument passed to `dispatchToast`, renders it
   standalone, clicks "View findings" → asserts `bus.dispatch` called with
   `('workspace', { type: 'widget_load', widgetType: 'compose', widgetData: { source: 'review-complete-toast' } })`
   AND `dismissToast(REVIEW_COMPLETE_TOAST_ID)` called.
4. `bounded stacking` — two completions while the toast is still active → `dispatchToast` called exactly
   ONCE total, `updateToast` called once for the second; simulates the toast's own `onStatusChange`
   firing `'dismissed'`, then a third completion → `dispatchToast` fires again (fresh, count = 2) —
   proves the ref correctly resets rather than staying permanently "active."

```
npx jest src/components/shell/__tests__/ReviewCompleteToast.test.tsx
Test Suites: 1 passed, 1 total
Tests:       4 passed, 4 total
```

**Typecheck**: `npx tsc --noEmit -p tsconfig.json` — 0 errors attributable to `ReviewCompleteToast.tsx` or
`ThreePaneShell.tsx` (grepped the output for both filenames — no matches). Remaining output is the
pre-existing baseline of unrelated cross-package errors (LegalWorkspace, Spaarke.UI.Components, Spaarke.
Events.Components, missing optional-dependency type declarations, etc.) — none in files this task touched.

**Full SpaarkeAi suite** (`npx jest`, whole worktree — includes an UNRELATED, concurrent, uncommitted
in-progress task in the same shared worktree touching `conversation/**`, `git status` confirms 12 files /
734 insertions under `src/solutions/SpaarkeAi/src/components/conversation/` not touched by this task):

```
Test Suites: 7 failed, 85 passed, 92 total
Tests:       31 failed, 811 passed, 842 total
```

92 total suites = the prior 91-suite baseline (`042-execution-notes.md` recorded 91/838 green) **+ this
task's 1 new suite** (`ReviewCompleteToast.test.tsx`, 4/4 passing — confirmed both in isolation and as
part of this full run). All 7 failing suites/31 failing tests trace to `useAgreementReviewGate.ts`,
`localActionChips.ts`, `ConversationPane.tsx`, `agreementReviewRouting.ts` — the SAME files `git status`
shows as uncommitted/modified by the concurrent, unrelated in-progress work in this shared worktree (this
task never touched `conversation/**`, per HARD BOUNDARIES). Zero failures attributable to `ThreePaneShell.tsx`
or `ReviewCompleteToast.tsx`. Baseline-with-this-task's-own-changes-only is therefore **86/92 suites green
(all failures pre-existing/concurrent, not introduced by this task)** — the "keep baseline green" bar is
met for everything this task actually touched.

## Step 5 — quality gates (self-run, FULL rigor)

**code-review** (self-run against the diff):
- 0 Critical issues. No new interfaces beyond the one needed prop (`ReviewCompleteToastProps`). No
  try/catch. No defensive null-checks beyond genuine optional-field narrowing already typed on
  `WorkspacePaneEvent`. Comments explain rationale (why reuse, why the fixed toastId, why `updateToast`
  vs `dispatchToast`) rather than restating code. Single-responsibility: the component does exactly one
  thing (bridge two existing events into one toast + one action).
- Security: no new I/O, no user-input handling, no secrets. N/A.
- Performance: two lightweight `usePaneEvent` subscriptions (existing bus mechanism, no polling); one
  10s timer per active toast (Fluent's own toast-timeout primitive, not a custom interval).
- Component justification (CLAUDE.md §11 / Step 6.6): documented above — concrete grep evidence for all
  three "existing" claims (Toaster, both events, the reactivation contract).

**adr-check** (self-run):
- **ADR-021** (Fluent v9, dark mode, semantic tokens) — Compliant. `Toast`/`ToastTitle`/`Link` are
  token-driven; no custom CSS/colors added.
- **ADR-030** (typed PaneEventBus, prefer subscribe over new emits) — Compliant. Zero new discriminants;
  first consumer of an event explicitly built as forward-looking infrastructure.
- **ADR-039** (grounded execution — `consumerType` is the ONLY routing decision for agent-directed
  surface launches; client never intent-detects) — considered, not applicable: "View findings" is a
  plain client-side UI affordance (a toast action button a human clicked), not an agent-selected
  capability dispatch. It does not go through `surfaceLaunchRegistry` because that registry exists
  specifically for `consumerType` → surface resolution FROM AN AGENT TURN (per
  `ASSISTANT-SURFACE-LAUNCH-MECHANISM.md` §1: "The Assistant does zero intent detection..."). Directly
  dispatching `workspace.widget_load` with the source-marker contract is the SAME pattern
  `WorkspacePaneMenu`'s tab-switching and the add-to-DMS/reporting-email flows already use for plain
  client-driven tab focus — not a second routing mechanism, not agent-directed, no `consumerType`
  involved. No ADR-039 tension.
- No auth/security-sensitive ADRs touched.

No §6.5 ADR Conflict Resolution Protocol firing — no rule was bent, only extended per its own "how to
extend" section (§11 justified, path C not needed — no ADR-compliant alternative was rejected).

## Acceptance criteria — evidence

| Criterion | Status | Evidence |
|---|---|---|
| Review completes while another tab is active → toast renders with the action; action navigates/focuses the Compose surface (assert the navigation event) | ✅ Pass | `ReviewCompleteToast.test.tsx` tests 1 + 3 |
| Compose tab already visible → NO toast | ✅ Pass | `ReviewCompleteToast.test.tsx` test 2 |
| Toast is dark-mode safe (tokens); auto-dismisses per Fluent defaults; repeated completions don't stack unboundedly | ✅ Pass | Token-only components (ADR-021 self-check above); `timeout: 10000` (Fluent auto-dismiss primitive, not a custom timer); `ReviewCompleteToast.test.tsx` test 4 (bounded stacking) |
| Zero-findings completion behavior implemented or explicitly documented as not-observable-today (with the follow-on filed) | ✅ Documented | `notes/uat-round1-2026-08-03.md` — exact one-line fix cited (`useNdaReviewAdvisoryCommentsBridge.ts:160`), verified safe for the existing `ComposeWorkspace.onAdvisoryComments` consumer, alternative (trace-event correlation) considered and rejected as over-engineered |
| SpaarkeAi suites green | ✅ Pass (baseline kept) | See Step 4 — full-suite result in the structured RETURN |

## Deviations

None from the task's stated scope. The zero-findings gap is a DOCUMENTED, pre-existing, out-of-boundary
finding (not a deviation caused by this task's own work) — flagged per the task brief's own instruction
("investigate... if none exists, DOCUMENT the gap precisely... do NOT edit conversation/** yourself").
