# Task 030 Completion — P1 Window Controls into Spaarke.UI.Components Dialogs (FR-12)

> RIGOR: STANDARD. Executed per task-execute protocol. Dependency (003 — glyph reconciliation) confirmed ✅ in TASK-INDEX.md before starting; `ModalWindowControls.tsx` already ships `FullScreenMaximize20Regular`/`FullScreenMinimize20Regular` (verified by reading the shipped source — not modified this task).

## Adapter shape used (consistent across all dialogs)

- **Header slot**: where the dialog used Fluent `DialogTitle`, added `action={<ModalWindowControls .../>}`. Where the dialog had a custom header `div` (not `DialogTitle`), appended `<ModalWindowControls/>` at the end of that div's flex row, removing the dialog's own ad hoc close button (never both — see "× consolidation" below).
- **Close (×)**: always wired to the dialog's existing close/cancel handler — never a new path. Where the dialog already had its own ad hoc "×" button (`FindSimilarDialog` legacy, `FilePreviewDialog`, `CloseProjectDialog` ×2, `WizardShell`), that ad hoc button was **replaced** by `ModalWindowControls`' `onClose` wired to the SAME handler — this is the "standardize the × cluster" the task asks for, not an addition of a second close affordance. Where no close-X existed at all (`ChoiceDialog`, `NewThreadModal`), the cluster is a pure addition.
- **Maximize/restore**: new `isMaximized` local `useState(false)` per dialog, reset to `false` whenever `open` becomes `false` (mirrors the established `EmailComposer/wrappers/SendEmailDialog.tsx` precedent — "always restore to default on reopen"). Toggling swaps the surface's size to `100vw`/`100vh` (per design.md §6.4 `full` size row) and back to the dialog's pre-existing, UNCHANGED default size. Dialogs with a consumer-driven sizing mechanism already in place (`WizardShell`'s `maxWidth`/`height` props + inline-style bypass) reuse that exact mechanism rather than introducing a parallel one.
- **Import route**: shared-lib dialogs import `ModalWindowControls` via the established sibling-relative path `'../ModalWindowControls'` (matches `EmailComposer.tsx`'s precedent). The LegalWorkspace solution-local `CloseProjectDialog.tsx` copy imports it via the package barrel `'@spaarke/ui-components'` (matches how LegalWorkspace already imports `SendEmailDialog`, `AiProgressStepper`, etc. from the same barrel).
- **Token discipline**: zero new hex values, `'1px'` literals, or inline color styles introduced. `mergeClasses` used (already an existing Fluent export) wherever a maximize class needed composing with a pre-existing surface class.

## Per-dialog outcome table

| Dialog | Outcome | Notes |
|---|---|---|
| `components/ChoiceDialog/ChoiceDialog.tsx` | **wired** | No prior close-X (Fluent `DialogTitle`'s default action only renders when `modalType="non-modal"` — verified against installed `@fluentui/react-dialog` source; this dialog's default `modalType` never triggered it). Added `action` slot + `surfaceMaximized` style. Footer `Cancel` unchanged. |
| `components/FindSimilarDialog/FindSimilarDialog.tsx` (legacy iframe dialog) | **wired** | Custom `titleBar` div. Replaced the ad hoc Close (`DismissRegular`) button with `ModalWindowControls`; kept the "Open in new tab" (`ArrowExpandRegular`) button as a preserved per-surface action to its left. `embedded` mode still hides the whole title bar (unchanged). |
| `components/FindSimilar/FindSimilarDialog.tsx` (2-step wizard dialog) | **inherits — no edit needed** | This component owns no header of its own; it renders `<WizardShell title=… onClose=… .../>` without `embedded`, so once `WizardShell`'s title bar shows the cluster (see below), this dialog shows it automatically. Confirmed by reading the file: zero header markup exists here to modify. |
| `src/solutions/LegalWorkspace/src/components/FindSimilar/FindSimilarDialog.tsx` | **inherits — no edit needed** | Thin adapter that renders the shared `FindSimilar/FindSimilarDialog` above (which itself inherits from `WizardShell`). No header of its own. |
| `components/SendEmailDialog/SendEmailDialog.tsx` (legacy) | **SKIPPED — escalation** | See "Escalations" below. Evidence found for the "v1.1.59 no-X decision." |
| `components/EmailComposer/wrappers/SendEmailDialog.tsx` | **verified-already-consuming — no edit** | The wrapper itself renders no header; `EmailComposer.tsx` (the engine, line ~1287) already renders `<ModalWindowControls isMaximized={props.isMaximized} onToggleMaximize={props.onToggleMaximize} onClose={props.onCancel}/>` importing from `'../ModalWindowControls'` — the single canonical source, already reconciled to the `FullScreenMaximize/Minimize` glyph by task 003. No double-wiring: exactly one `<ModalWindowControls>` instance renders for this dialog. |
| `components/NewThreadModal/NewThreadModal.tsx` | **wired** | Added `action` slot on `DialogTitle`. Close wired to `handleCancel` (the SAME handler the footer's own Cancel button uses — respects the `submitting` guard already in that handler, not a new path). |
| `components/CreateProjectWizard/CloseProjectDialog.tsx` | **wired** | Replaced the existing ad hoc `action` Button (`aria-label="Close dialog"`) with `ModalWindowControls`, preserving the `phase !== 'closing'` guard (cluster fully hidden — no × or maximize — while the async closure call is in-flight, unchanged behavior). |
| `src/solutions/LegalWorkspace/src/components/CreateProject/CloseProjectDialog.tsx` | **wired** | Identical edit to the shared copy above (known duplicate per project CLAUDE.md "Key Facts"), imported via the `@spaarke/ui-components` barrel. |
| `components/FilePreview/RichFilePreviewDialog.tsx` | **wired (both render paths)** | Nav-enabled path: cluster passed via `RecordNavigationModalShell`'s existing `actionBar` slot (already designed for exactly this). Non-nav path: new optional `onClose`/`isMaximized`/`onToggleMaximize` props forwarded into `RichFilePreview` (see next row). Footer's `Close` button (`DialogActions`) is UNCHANGED. |
| `components/FilePreview/RichFilePreview.tsx` | **collateral edit (necessary, additive)** | Not in the POML's file list, but this is where the ACTUAL title-bar markup for `RichFilePreviewDialog`'s dominant (non-nav) consumer path lives (extracted renderer, R5 task 013). Added 3 new OPTIONAL props (`onClose`, `isMaximized`, `onToggleMaximize`) + renders `<ModalWindowControls>` at the end of `titleActions`. Fully backward-compatible: non-modal consumers (`Spaarke.AI.Widgets`' `DocumentViewerWidget.tsx` / `FilePreviewContextWidget.tsx`) never pass these three props (confirmed by grep), so `ModalWindowControls` renders `null` there — zero visual/behavioral change for them. |
| `components/FilePreview/FilePreviewDialog.tsx` (deprecated) | **wired** | Custom `titleBar` div. Replaced the ad hoc Tooltip+Close-button with `ModalWindowControls` (both already used the same "Close" wording/aria-label, so zero accessible-name change here). |
| `components/Wizard/WizardShell.tsx` | **wired (standard mode); close-only (embedded mode)** | Replaced the raw `Dismiss24Regular` close Button with `ModalWindowControls`. Maximize is gated on `!embedded` — an `embedded` mount (Dataverse dialog iframe / PCF host) already fills its host container 100%×100% with no independent viewport to expand into, so only × is offered there (same reasoning `FindSimilarDialog`'s existing `embedded` flag already applies to hide chrome entirely — here it's a partial hide, not a full one, since Close must still work in both modes). Reused the EXISTING consumer-driven `maxWidth`/`height` sizing mechanism (inline-style bypass, v1.1.63) rather than adding a parallel one: `effectiveMaxWidth`/`effectiveHeight` swap to `100vw`/`100vh` when maximized, else fall back to the unchanged consumer props. Removed the now-dead `closeButton` style (was only used by the removed raw Button). |

**Acceptance-criterion dialog coverage** — all 12 dialogs named in the POML's acceptance criteria are accounted for: 8 wired directly, 2 inherit transitively via `WizardShell`, 1 verified-already-consuming, 1 skipped with a documented escalation.

## Escalation

### `components/SendEmailDialog/SendEmailDialog.tsx` (legacy) — SKIPPED

Per the task's escalation trigger, checked history/comments for the "v1.1.59 no-X decision" before touching this file. Found explicit, binding evidence:

- Import comment (line 49-50): `// v1.1.59 — Dismiss24Regular import removed alongside the title-bar // X close button (per UAT request for cross-modal consistency).`
- Inline comment directly above `<DialogTitle>{title}</DialogTitle>` (line ~315-322): `/* v1.1.59 — title-bar X close icon removed per UAT. The Cancel button in the footer is the single close affordance, matching FilePreviewDialog's pattern (v1.1.46) for consistency across our shared modals. ... */`

This is a documented, deliberate UAT decision to REMOVE the title-bar × in favor of a single footer Cancel affordance — the exact scenario the escalation trigger names. Per instruction, this dialog was **not** touched (neither × nor maximize added) rather than silently overriding a prior UAT decision. This tension (2026-07 "no-X" decision vs. the 2026-07-31 "standardize maximize/restore + × on all modals" mandate) is a legitimate ADR/decision conflict per root CLAUDE.md §6.5 and should be resolved by the project owner via one of the three paths (A: keep as documented exception since this dialog is already slated for retirement in P3/FR-14 "legacy SendEmailDialog retired"; B: amend the v1.1.59 decision; C: n/a — pivoting to comply is literally what's being asked, so this is really an A-vs-B choice). Given FR-14 already schedules this dialog's retirement in P3 (task 051), recommendation is **path A** (accept as a time-boxed exception, expires at retirement) — but this is the owner's call, not mine to silently decide.

No other escalations fired. `WizardShell`'s custom title bar (the other named example in the escalation trigger) was assessed and found NOT to require escalation: `ModalWindowControls` slots into the existing `titleBar` flex row (title left / controls right) without changing that layout contract, and the file's pre-existing `maxWidth`/`height` prop-driven sizing mechanism was reused (not replaced) for the maximize target — see the WizardShell row above for the embedded-mode reasoning.

## Pre-existing literals noted (left in place, not touched — per NFR-03 carve-out for pre-existing literals)

- `FindSimilarDialog` (legacy): `titleBar` style has `borderBottom: \`1px solid ${tokens.colorNeutralStroke2}\`` — pre-existing `'1px'` literal, not adjacent to my edit (it's the outer border, not the button cluster), left in place.
- `FilePreviewDialog` (deprecated): `titleBar` / `toolbar` styles both use `borderBottomWidth: '1px'` — pre-existing, left in place.
- `WizardShell`: `titleBar` / `footer` styles use `borderBottomWidth: '1px'` / `borderTopWidth: '1px'`; `surface` uses `border: \`1px solid ${tokens.colorNeutralStroke1}\`` — all pre-existing, left in place.
- `CloseProjectDialog` (both copies): inline `style={{ backgroundColor: tokens.colorPaletteRedBackground3, ... }}` on the destructive "Close Project" footer button — pre-existing (token-based, not a hex literal, but an inline style per NFR-03's letter); this is the "CloseProjectDialog anti-pattern" design.md §6.5 explicitly calls out for later-phase remediation (P2, task 040/041/042 range) — not touched this task since it is not adjacent to the window-controls cluster.

No NEW hex values, `'1px'` literals, or inline color styles were introduced by this task's edits.

## Builds

| Build | Command | Result |
|---|---|---|
| Shared lib (`Spaarke.UI.Components`) | `npm run build` (tsc) | **PASS** — zero type errors, exit 0 |
| Shared lib lint (8 touched files) | `npx eslint <8 files>` | **PASS** — zero errors/warnings |
| PCF consumer — `SemanticSearchControl` | `npm install --legacy-peer-deps --no-audit --no-fund` then `npm run build:prod` | **PASS** — `[build] Succeeded`. Chosen because it deep-imports BOTH `FindSimilarDialog` (legacy, `@spaarke/ui-components/dist/components/FindSimilarDialog`) and `RichFilePreviewDialog` (via its local `components/FilePreviewDialog.tsx` re-export of `.../dist/components/FilePreview/RichFilePreviewDialog`) — exercises 2 of my 9 touched shared-lib files directly under React 16/17. 17 pre-existing ESLint warnings (unused vars in files I didn't touch) + normal PCF bundle-size webpack warnings (749 KiB, pre-existing bundle-size profile) — zero errors. |
| Code Page consumer — `LegalWorkspace` | `npm install --legacy-peer-deps --no-audit --no-fund` then `npm run build` (vite build) | **FAILED — pre-existing, unrelated to this task** (see below) |

### LegalWorkspace build failure — root-caused as pre-existing/out-of-scope

`vite build` transforms all 2530 modules successfully, then fails at Rollup's dynamic-import resolution stage:

```
[vite]: Rollup failed to resolve import "@spaarke/ai-outputs/output-widgets/BudgetDashboardWidget"
from "src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts".
```

Root-caused (read-only investigation, no fix attempted — both packages are outside this task's scope, and `Spaarke.AI.Widgets` is explicitly reserved for the sibling task-031 agent):
- `Spaarke.AI.Outputs/package.json` has **no `exports` field**, and the built widget files live at `dist/output-widgets/BudgetDashboardWidget.js` — there is no `output-widgets/` folder at the package root. The import path used by `register-workspace-widgets.ts` (`@spaarke/ai-outputs/output-widgets/...`, missing `/dist/`) cannot resolve under plain Node/bundler resolution rules.
- `register-workspace-widgets.ts` is **unmodified from HEAD** (confirmed via `git status` — not in the modified-files list), so this is a pre-existing defect in the committed codebase, not something introduced by concurrent parallel work this session, and unrelated to any of my 10 changed files.
- Confirmed via `npx tsc --noEmit` (LegalWorkspace's own tsconfig, `moduleResolution: "bundler"`, `noEmit: true`): 265 pre-existing type errors across the package (missing `@types/jest`, unrelated `@spaarke/ai-widgets` module-resolution errors, unrelated property-shape mismatches in other components, etc.) — **zero of these 265 errors reference either of my two touched LegalWorkspace files** (`CreateProject/CloseProjectDialog.tsx`, `FindSimilar/FindSimilarDialog.tsx` — grepped explicitly, no matches). This package's `tsc --noEmit` is evidently not a clean gate independent of this task.
- The Vite build's own progress log ("2530 modules transformed" before the Rollup-stage failure) further corroborates that my edited files parsed/transformed without error; the failure is isolated to one specific cross-package dynamic-import specifier unrelated to window-controls wiring.

Per the task's explicit guidance ("if a consumer build fails for reasons PRE-EXISTING in the fresh worktree ... document precisely and move on; do not rabbit-hole environment repair beyond `npm install` + building sibling dists it needs") this was documented and not further chased — `Spaarke.AI.Outputs`'s `dist` already exists and is fully populated (not a missing-build-step issue; it is a packaging/path defect in code outside this task's file list). Flagging for the main session's attention at wave close, since a `/project-defer-issue-tracking` entry may be warranted (not filed by this sub-agent — no `.claude/`/GitHub-issue write access, and the sibling task-031 agent may hit and report the identical issue independently).

## Tests

- Full `Spaarke.UI.Components` jest suite: **185 passed / 11 failed suites** (2444 passed / 22 failed tests of 2466 total). All 11 failing suites were verified as pre-existing/unrelated to this task:
  - 10 of the 11 failing suites are in files I never touched (`toolbarLaunchDefaults`, `buildDynamicWorkspaceConfig`, `surfaceLaunchRegistry`, `XrmDataverseClient`, `EntityCreationService.cascade`, `ConversationView.forward`, `TimelineComposeBox`, `SendEmailDialog.characterize` [EmailComposer — confirmed zero diff in that whole folder via `git diff --stat`], `ConversationView.emailInFlow`, `recordHeader.integration`).
  - The 11th, `FilePreview/__tests__/RichFilePreview.test.tsx`, IS in a file I touched — but `git diff` proves my only changes there are a new import + 3 new OPTIONAL props + one new `<ModalWindowControls>` render that is a no-op (`null`) whenever `onClose`/`onToggleMaximize` are both undefined, which is true for every existing test in that file (verified: none of them pass those 3 new props). The 2 failing assertions in that file (a keyboard-nav `contentEditable`-guard call-count mismatch, and a "multiple elements with text NDA" ambiguity between the pre-existing Tags-chip and Details "Type" row) are both in code paths my diff never touches.
- `SprkModal` + `ModalWindowControls` suites (the P0 gate named in the task instructions): **86/86 passing** (11 suites), run in isolation — exact match to the "86 tests" figure.
- `NewThreadModal.test.tsx`: **PASS** (unaffected — no test in that file queries the accessible name "Close" or "Maximize dialog").
- `RichFilePreviewDialog.test.tsx`: **PASS** — required one test update (see below).

### Test updated: `FilePreview/__tests__/RichFilePreviewDialog.test.tsx`

The `'Close button dispatches onClose'` test previously did `screen.getByRole('button', { name: 'Close' })`, which uniquely matched the footer's `Close` button. Since this task adds the header × (also accessibly named "Close" — `ModalWindowControls`'s fixed, un-overridable aria-label) to the SAME (non-nav) render path, there are now legitimately two "Close"-named buttons. Updated the test to assert there are exactly 2, and to click the LAST one (the footer's, DOM-order-stable) — preserving the original assertion's intent (clicking dispatches `onClose`) while accounting for the new, intentional second affordance. No other existing test in the shared lib needed updating (confirmed via repo-wide grep for `'Close dialog'` — the exact string `WizardShell`/`CloseProjectDialog` previously used as their aria-label, which now changes to `ModalWindowControls`'s fixed "Close" — zero test files anywhere in `src/` assert on that exact accessible name via `getByRole`).

## Acceptance-criteria checklist (from the POML)

| # | Criterion | Pass/Fail |
|---|---|---|
| 1 | Every listed dialog's header renders the FullScreen maximize/restore + × cluster from the shared `ModalWindowControls` | **PASS** — 8 wired directly + 2 inherit transitively via `WizardShell` + 1 (EmailComposer wrapper) verified pre-existing = 11 of 12; 1 (legacy `SendEmailDialog`) legitimately skipped per the named escalation trigger, evidence documented above |
| 2 | `EmailComposer/wrappers/SendEmailDialog.tsx` verified to already consume the reconciled control, not double-wired | **PASS** — verified via `EmailComposer.tsx` source read; exactly one `<ModalWindowControls>` instance for this dialog |
| 3 | Each converted dialog's prior behavior/messaging/props/size/footer unchanged; only the window-control cluster added/standardized | **PASS** — every footer left untouched; every default (unmaximized) size path left untouched (maximize only ever ADDS a new toggled state, never alters the default); the only "removed" markup anywhere was each dialog's own ad hoc close button, replaced 1:1 by the canonical component wired to the identical existing handler |
| 4 | Negative: no dialog introduces a new hex value, `'1px'` literal, or inline color style; no fork/copy of `ModalWindowControls` created | **PASS** — confirmed by review of every diff hunk; `ModalWindowControls` is imported, never copied; see "Pre-existing literals noted" section for what was left in place (none touched, none added) |
| 5 | Shared-lib build, one PCF consumer (`@types/react` 18), one Code Page consumer (React 19) all build green | **PARTIAL** — shared-lib **PASS**; PCF (`SemanticSearchControl`) **PASS**; Code Page (`LegalWorkspace`) **FAILS for a pre-existing, unrelated, out-of-scope reason** (documented in detail above, with `tsc --noEmit` + `git diff` + `git status` evidence that none of the 10 files this task touched are implicated) |

## Deviations from POML steps

- Step 2/3 named `WizardShell` and both `FindSimilar` copies as separate wiring targets; in practice `FindSimilar/FindSimilarDialog.tsx` (both the shared and LegalWorkspace copies) required **zero code changes** — they render no header of their own and inherit the cluster transitively once `WizardShell` (a step-2 target) was wired. Documented as "inherits" rather than "wired" in the outcome table rather than forcing a no-op edit.
- Added a collateral edit to `components/FilePreview/RichFilePreview.tsx`, not named in the POML's `relevant-files` list, because it is the file that actually owns the title-bar markup for `RichFilePreviewDialog`'s dominant (non-navigation) consumer path (the renderer was extracted from the dialog in an earlier project, R5 task 013). Necessary to satisfy the acceptance criterion for `RichFilePreviewDialog` itself; kept strictly additive/optional so non-modal consumers of `RichFilePreview` (outside this task's scope) are unaffected.
- Updated one existing test assertion (`RichFilePreviewDialog.test.tsx`) per the explicit instruction to "update ONLY assertions that legitimately reflect the new cluster."
- Did not attempt to repair the LegalWorkspace/`Spaarke.AI.Outputs` packaging defect that blocks the Code Page consumer build — out of scope (owned by sibling task 031 / adjacent package), and explicitly outside the "npm install + build sibling dists it needs" repair boundary given `Spaarke.AI.Outputs`'s dist already exists and is fully populated.
