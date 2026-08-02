# Task 031 — Window controls into Compose/AI.Widgets/SpaarkeAi dialogs — Completion Note

> **Date**: 2026-08-01
> **Task**: 031 (P1 window-controls rollout, FR-12)
> **Rigor**: STANDARD
> **Executed**: sub-agent (parallel-group P1, alongside sibling task 030 in the same shared worktree)

## Outcome

All four owned dialogs now render the shared `ModalWindowControls` (FullScreen maximize/restore
+ ×) in their header via Fluent `DialogTitle`'s `action` slot. `ConversationModal` (PCF) verified
as already-consuming per project discovery — left untouched. Zero forks of `ModalWindowControls`;
zero new hex/`'1px'`/inline-color literals. All three owned packages build green; full test
suites are green modulo pre-existing, proven-unrelated failures (details below).

## Adapter shape used (identical convention across all four; matches sibling task 030's approach
and the one pre-existing reference implementation, `EmailComposer`/`SendEmailDialog`)

1. **Control placement** — all four dialogs already use Fluent's `<DialogTitle>`, so each was
   wired via `DialogTitle`'s native `action?: Slot<'div'>` slot (`action={<ModalWindowControls .../>}`)
   rather than a custom flex-row header div. This is the *simpler* of the two adapter routes the
   task allowed ("where the dialog uses Fluent `DialogTitle`, prefer its action slot") — verified by
   reading `@fluentui/react-dialog`'s own `useDialogTitle.js`/`useDialogTitleStyles.styles.js`
   source: the `action` slot renders as a CSS-grid sibling of the title `<h2>` (`grid-column-start:3;
   justify-self:end`), i.e. top-right, automatically — no manual positioning CSS needed. It also
   confirmed the `action` slot's content is NOT part of the `<h2>` that supplies the dialog's
   accessible name (`aria-labelledby` → the h2's `id` only), so adding the cluster cannot change
   any `getByRole('dialog'/'alertdialog', { name: ... })` test assertion. None of the four dialogs
   previously passed an `action` prop (Fluent only renders a *default* close action when
   `modalType==='non-modal'`, which none of these are), so there was no pre-existing action-slot
   content to conflict with.
2. **Maximize/restore** — none of the four dialogs had ANY prior maximize mechanism (unlike
   `SendEmailDialog`, which already had one). Added a local `isMaximized` boolean
   (`React.useState`) + a `React.useEffect` that resets it to `false` whenever the dialog's `open`
   prop transitions to `false` — mirroring the `SendEmailDialog` convention exactly (the one other
   Fluent `Dialog` already wired to `ModalWindowControls`, task 021/FR-12, pre-existing). The
   maximize target is `{ width: '100%', height: '100%', maxWidth: '100%' }`, applied via a new
   `surfaceMaximized` makeStyles class (three dialogs, no prior `className` conflict) or, for
   `QuickStartModal` (which already carries an inline `style={{maxWidth:'720px',width:'720px'}}`
   redundant with its own `styles.surface` class), by making that SAME inline style object
   conditional on `isMaximized` — the non-maximized branch is byte-identical to the pre-existing
   object, so default sizing is unchanged. All dimension-only (no color) — ADR-021/NFR-03 safe.
3. **Close (×) wiring** — routed to each dialog's EXISTING close/cancel-equivalent handler, never
   a new path:
   - `ComposeConflictDialog` → `onCancel` (the prop backing the existing "Cancel — close this tab"
     button — the dialog's documented, only cancel-equivalent escape hatch).
   - `PinnedMemoryEditDialog` → `handleCancel` (the SAME guarded callback the "Cancel" button uses;
     it internally no-ops while `isSubmitting`).
   - `PinnedMemoryDeleteConfirmation` → `handleCancel` (same pattern; no-ops while `isDeleting`).
   - `QuickStartModal` → `onClose` (the modal's own top-level close prop; this dialog is
     `modalType="modal"`, i.e. already dismissible via Escape/backdrop, so no alert-semantics
     concern here at all).

## Per-dialog outcome table

| Dialog | Package | Outcome | Alert/non-dismissible semantics preserved? |
|---|---|---|---|
| `ComposeConflictDialog.tsx` | `Spaarke.Compose.Components` | **Wired** | Yes — `modalType="alert"` untouched (still blocks Esc/backdrop); × → existing `onCancel`, not a new dismiss path |
| `PinnedMemoryEditDialog.tsx` | `Spaarke.AI.Widgets` | **Wired** | N/A (`modalType="modal"`, was already dismissible) |
| `PinnedMemoryDeleteConfirmation.tsx` | `Spaarke.AI.Widgets` | **Wired** | Yes — `modalType="alert"` untouched; × → existing guarded `handleCancel`, same handler as the visible "Cancel" button |
| `QuickStartModal.tsx` | `src/solutions/SpaarkeAi` | **Wired** | N/A (`modalType="modal"`, was already dismissible) |
| `ConversationModal.tsx` | `CommunicationConversationPanel` PCF | **Verified already-consuming** — untouched | N/A |

## ConversationModal verification (work item 1 — read-only, no modification)

Confirmed: `ConversationModal.tsx` imports `ModalWindowControls` (+ its type
`IModalWindowControlsProps`) via the **barrel import** `from '@spaarke/ui-components'` (same
route as its other shared-lib imports — `ConversationWorkspace`, `ConversationView`,
`createXrmNavigationService`, etc.), then re-casts at the PCF React-16 type boundary:
`const ModalWindowControlsR16 = ModalWindowControls as unknown as
React.ComponentType<IModalWindowControlsProps>;` (same cast pattern applied to the other two
shared components in that file — the documented ADR-022 "shared-library React-version drift"
workaround). It renders `<ModalWindowControlsR16 isMaximized={expanded}
onToggleMaximize={() => setExpanded(v => !v)} onClose={onClose} />` inside a `windowControls` div
pinned `position:absolute; top; right` on its custom overlay surface. This is a **non-escalation**
— discovery is confirmed correct; task proceeded with the rest of the work items. No PCF-specific
deep-import route was needed for my three React-19 packages (Compose.Components / AI.Widgets /
SpaarkeAi are Code-Page-style consumers on `@types/react@19`, matching `@spaarke/ui-components`'s
own React-19 authoring — no cast required there, confirmed by the pre-existing convention of
importing many other components/types directly from the `@spaarke/ui-components` barrel
throughout all three packages).

## Escalations

**None.** `ConversationModal` verification matched discovery exactly (no contradiction). Both
non-dismissible dialogs (`ComposeConflictDialog`, `PinnedMemoryDeleteConfirmation`) had a genuine,
documented cancel-equivalent prop (`onCancel`) already wired to a visible button — no dialog
required inventing a new close path, so no escalation trigger fired.

## Conflict-check citation (SpaarkeAi hot-path)

Per the task prompt: "SpaarkeAi hot-path conflict-check was already run by the main session:
soft-pass, zero open-PR file overlap" — cited as instructed; not re-run by this agent.

## Build results (owned packages only — `Spaarke.UI.Components` / `LegalWorkspace` NOT
rebuilt, per hard boundary; sibling task 030 owns those concurrently in this same shared worktree)

| Package | `npm run build` | Notes |
|---|---|---|
| `Spaarke.AI.Widgets` | **PASS** (tsc exit 0) | Required building 3 "needed sibling dists" first (see below) |
| `Spaarke.Compose.Components` | **PASS** (tsc exit 0) | Required building 1 additional sibling dist (`Spaarke.DocumentOperations`) |
| `src/solutions/SpaarkeAi` (Code Page) | **PASS** — full pipeline: `check-html-css-reset` → `tsc-surface-gate` (0 surface-owned errors; 73 pre-existing shared-lib errors deferred, by the gate script's own by-design mechanism) → `vite build` (4019 modules, single-file HTML, gzip 1.46 MB) → rename → `build:ribbon` (4 ribbon bundles) | Full production build succeeded end-to-end, including `@spaarke/legal-workspace` (a deliberately source-only, dist-less package per its own `package.json` description — resolves fine at Vite-bundle time even without a `dist/`) |

### Fresh-worktree environment setup performed (npm installs + "needed sibling dist" builds — in
scope per the task's own framing; none of these touch `Spaarke.UI.Components`/`LegalWorkspace`)

- `npm install --legacy-peer-deps --no-audit --no-fund` run in: `Spaarke.AI.Widgets`,
  `Spaarke.Communication.Components`, `Spaarke.AI.Outputs`, `Spaarke.AI.Context`,
  `Spaarke.DocumentOperations`, `Spaarke.Compose.Components`, `src/solutions/SpaarkeAi` (all had
  no `node_modules` in this fresh worktree).
- Built (`npm run build`, clean) the following sibling dists, each a genuine compile-time blocker
  for one of my three owned packages, none touched by my source edits:
  - `Spaarke.Communication.Components` — NOT actually a "missing dist" issue: its `package.json`
    deliberately points `main`/`types` at `./src/index.ts` (source-only by design), so its raw
    `.tsx` source gets pulled directly into any consumer's `tsc` program; it just needed its OWN
    `node_modules` installed so its `react`/`@fluentui/*` imports resolve. Blocked `Spaarke.AI.Widgets`.
  - `Spaarke.AI.Outputs`, `Spaarke.AI.Context` — real dist-based packages, missing `dist/` in this
    fresh worktree; both build clean standalone (no further cascading gaps). Blocked
    `Spaarke.AI.Widgets`.
  - `Spaarke.DocumentOperations` — real dist-based package, missing `dist/`; builds clean
    standalone. Blocked `Spaarke.Compose.Components`.
- Did **not** build: `Spaarke.UI.Components`, `Spaarke.LegalWorkspace` (hard boundary — sibling
  task 030 owns these concurrently) or the several OTHER `SpaarkeAi` `file:` deps whose dist is
  also missing in this fresh worktree (`Spaarke.DailyBriefing.Components`,
  `Spaarke.Events.Components`, `Spaarke.Notifications`, `Spaarke.SmartTodo.Components`) — these
  turned out to be unnecessary: `tsc-surface-gate.mjs` (the project's own pre-existing,
  purpose-built gate script, established 2026-06-09) explicitly defers ALL shared-lib errors as
  long as `src/**` (SpaarkeAi's own surface code, which includes my `QuickStartModal.tsx` edit) has
  zero errors — confirmed `Surface-owned: 0. ✓`. The subsequent `vite build` also succeeded outright
  without needing those dists built.

## Test results

| Suite | Result | Notes |
|---|---|---|
| `Spaarke.AI.Widgets` — `src/components/memory/**` (both edited dialogs) | **20/20 pass** | Ran isolated first for fast signal |
| `Spaarke.AI.Widgets` — full suite | **677/678 pass** (37/38 suites) | 1 pre-existing failure, unrelated: `register-workspace-widgets.test.ts` — `communications-list` widget metadata `displayName` expected `"Communications"`, actual `"Messages"` (a product-label drift in an entirely different subsystem; zero relation to memory dialogs or window controls) |
| `Spaarke.Compose.Components` — full suite | **843/858 pass** (67/72 suites) | 15 pre-existing failures across 5 suites (`ComposeWorkspace.bornInEditorSave/imports/saveOpLogPreservation/search.test.tsx`, `stepOperationInterceptor.test.ts`) — **proven unrelated via A/B test**: `git stash`'d my `ComposeConflictDialog.tsx` edit back to HEAD and re-ran the two spot-checked failing suites; identical failures persisted with my change fully reverted (symptom: `ComposeWorkspace`'s render tree throws "Element type is invalid ... got: undefined" for an unrelated child, pre-dating this task). Re-applied my edit after confirming (`git stash pop`) |
| `src/solutions/SpaarkeAi` — `QuickStartModal.test.tsx` + `ComposeConflictDialog.test.tsx` (the SpaarkeAi-side duplicate test that imports `ComposeConflictDialog` via the `@spaarke/compose-components/widgets/ComposeConflictDialog` deep path) | **25/25 pass** | Both directly verify my edited components end-to-end through TWO different import routes |
| `src/solutions/SpaarkeAi` — full suite | **838/838 pass** (91/91 suites) | Zero failures — fully green |

No test file was modified — all listed results are from PRE-EXISTING suites, run as-is per the
task's "keep them green" instruction. No new assertions were added for the window-controls
affordance itself (the POML's own `<ui-tests>` section frames verification as manual/harness
inspection, not new unit tests, and rigor is STANDARD); flagging this as a documented choice, not
a gap — a reviewer may want a follow-up test-coverage pass alongside the eventual P2+ `SprkModal`
re-base that supersedes this interim adapter.

## Deviations from the POML text

None of substance. The POML's `~100vw/100vh` phrasing for the maximize target was implemented as
the equivalent, codebase-proven `{width:'100%', height:'100%', maxWidth:'100%'}` recipe (identical
to the shipped `SendEmailDialog.surfaceMaximized`), since Fluent's `Dialog` already portals to
`document.body` and resolves percentage dimensions against the viewport — this is the "reuse any
existing maximize mechanism" instruction applied to the one pre-existing precedent in this exact
codebase, rather than introducing a second/different sizing convention.

## Files modified

- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeConflictDialog.tsx`
- `src/client/shared/Spaarke.AI.Widgets/src/components/memory/PinnedMemoryEditDialog.tsx`
- `src/client/shared/Spaarke.AI.Widgets/src/components/memory/PinnedMemoryDeleteConfirmation.tsx`
- `src/solutions/SpaarkeAi/src/components/conversation/QuickStartModal.tsx`

(`ConversationModal.tsx` read-only verified, not modified. No other files touched — the additional
modified files visible in `git status` in this shared worktree belong to sibling task 030's
concurrent `Spaarke.UI.Components` work, not this task.)
