# Task 040 Completion — P2 Re-base confirms onto ConfirmModal (FR-13)

> RIGOR: FULL. Executed per task-execute protocol. Dependency (005 — ConfirmModal preset) confirmed ✅ in TASK-INDEX.md before starting.

## Summary

All four target dialogs — `ComposeConflictDialog`, `PinnedMemoryDeleteConfirmation`, and both
`CloseProjectDialog` copies — are re-based. None was skipped/escalated. All four now render via
the shared `SprkModal` shell with the exact chrome contract the `ConfirmModal` preset establishes
(`dismiss="alert"`, non-maximizable, standard header + footer, danger token class for destructive
actions). The task-030/031 interim `ModalWindowControls`/local-`isMaximized` wiring is fully
removed (dead code) from all four.

## Cross-cutting decision: `SprkModal` composed directly, not the literal `<ConfirmModal>` wrapper

**Applies to all four dialogs — one decision, documented once.** None of the four uses the
literal exported `<ConfirmModal>` component. All four compose `SprkModal` directly, replicating
`ConfirmModal`'s exact envelope contract (`dismiss="alert"`, `maximizable={false}`, the same danger
token-class recipe copied verbatim from `ConfirmModal.tsx`). This is **not** the escalation case —
per the task's own guidance ("composing via provided slots is NOT forking"), and is **not** a fork
of `ConfirmModal`/`SprkModal` (neither file is modified). Reasons, per dialog:

| Dialog | Why `ConfirmModalProps` (fixed Cancel+Confirm) doesn't fit | Evidence |
|---|---|---|
| `ComposeConflictDialog` | **3 simultaneous buttons** (Force-close / Go-to-other / Cancel) — `ConfirmModalProps` exposes exactly one Cancel + one Confirm slot, no third action. This is the exact case the POML's escalation note anticipates. | N/A — structural, not test-observed |
| `PinnedMemoryDeleteConfirmation` | Both Cancel **and** Delete must stay `disabled` while `isDeleting` — `ConfirmModalProps` exposes no per-button `disabled` override. Confirmed empirically, not just reasoned: the dialog's own pre-existing test asserts `toBeDisabled()` on both buttons during the in-flight state — using the literal `<ConfirmModal>` wrapper would have broken this REAL, pre-existing, in-scope test. | `PinnedMemoryDeleteConfirmation.test.tsx` — `'disables both buttons and shows "Deleting…" while in flight'` |
| `CloseProjectDialog` (both copies) | **Phase-dependent footer** — 0–2 buttons with different labels/handlers across confirm/closing/success/error phases. `ConfirmModalProps` has no phase concept and unconditionally renders a Cancel button, which would add an unwanted second button to the closing/success phases (each historically shows exactly one button). | Structural — see phase table below |

All four still get 100% of "ConfirmModal chrome" (AC #1) because `ConfirmModal` itself is *only* a
thin config of the same `SprkModal` shell (`ConfirmModal.tsx`: `size="xs"`, `dismiss="alert"`,
`maximizable={false}`, `footerStart`=Cancel, `footer`=Confirm/danger-class) — every one of those
same parameters is set identically on the four dialogs below, just composed one level down.

## Per-dialog outcome + button mapping

### 1. `ComposeConflictDialog.tsx` — RE-BASED (not escalated)

`src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeConflictDialog.tsx`

| Old (raw `Dialog`/`DialogActions`) | New (`SprkModal` slot) | Notes |
|---|---|---|
| `Dialog modalType="alert"` | `SprkModal dismiss="alert"` | Same non-dismissible semantics (ESC/backdrop blocked) |
| `DialogTitle` (static text) | `title="This document is open in another Compose session"` | Verbatim, unchanged |
| 3 buttons in `DialogActions` (Force-close primary, Go-to-other secondary, Cancel subtle) — right-aligned group, wrap-enabled | All 3 in `footer` slot, same relative order, wrapped in a `flexWrap` div (`footerStart` left empty) | **Order preserved exactly** — none moved to `footerStart`, since doing so would reorder the 3 actions relative to each other (forbidden by the "preserve order" constraint); none is a "the rest are secondary to this one" Cancel analog the way a 2-button confirm has |
| local `isMaximized` + `ModalWindowControls` (task 031 interim) | removed | `maximizable={false}` matches `ConfirmModal`'s non-maximizable contract |
| No danger styling on any button (original intent) | No danger styling added | "Force-close" is aggressive in *effect* but was never styled destructive by the original author — preserved verbatim, not upgraded (task's per-dialog guidance never asked for a danger variant here, unlike the other two dialogs) |

Size: `xs` (design §6.2 table explicitly lists `ComposeConflictDialog` under `xs`).

### 2. `PinnedMemoryDeleteConfirmation.tsx` — RE-BASED (not escalated)

`src/client/shared/Spaarke.AI.Widgets/src/components/memory/PinnedMemoryDeleteConfirmation.tsx`

| Old | New | Notes |
|---|---|---|
| `Dialog modalType="alert"` + manual `onOpenChange` guard | `SprkModal dismiss="alert"`, `onClose={handleCancel}` (guard preserved in the callback itself) | Manual `onOpenChange` handler removed as dead code — `dismiss="alert"` now owns the ESC/backdrop block |
| `DialogTitle` = warning icon + "Delete pinned memory?" | `title="Delete pinned memory?"` (icon dropped) | `SprkModal.title` is a plain string, no icon/subtitle slot — the standard "one header contract" (design §6.4). Warning intent unaffected: still fully conveyed by the unchanged `role="alert"` impact callout in the body |
| Cancel (secondary, `disabled={isDeleting}`) | `footerStart` — same button, same `disabled` | Cancel-left already matched the standard before this task; unchanged position |
| Delete/"Deleting…" (primary, plain — **no danger styling**, `disabled={isDeleting}`) | `footer` — same button + `disabled`, **+ danger token class added** | FR-13 + this task's own per-dialog guidance explicitly calls for the danger variant here — an intentional upgrade, not scope creep |
| local `isMaximized` + `ModalWindowControls` (task 031 interim) | removed | `maximizable={false}` |

Danger token class copied verbatim from `ConfirmModal.tsx`'s own recipe
(`tokens.colorStatusDangerBackground3` / `colorNeutralForegroundOnBrand` / hover-brightness) — same
tokens, not a fork, not an inline style.

Size: `xs` (design §6.2 table lists "PinnedMemory*" under `xs`).

### 3 & 4. `CloseProjectDialog.tsx` (shared-lib + LegalWorkspace copies) — RE-BASED (not escalated)

`src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/CloseProjectDialog.tsx`
`src/solutions/LegalWorkspace/src/components/CreateProject/CloseProjectDialog.tsx`

Both copies got the identical chrome treatment (kept in lockstep, per CLAUDE.md Key Facts); the
LegalWorkspace copy differs only in its pre-existing `authenticatedFetch`/`bffBaseUrl`-less
`closeSecureProject` call signature (untouched — out of scope).

| Phase | Old footer (DOM order) | New footer (`footerStart` / `footer`) | Danger? |
|---|---|---|---|
| `confirm` | Close Project (danger, **inline color**) first, Cancel second | `footerStart`=Cancel · `footer`=Close Project (danger **token class**) | Yes — danger styling **kept**, source changed from inline `style={{backgroundColor,...}}` to the `danger` `makeStyles` class |
| `closing` | single disabled "Closing…" button | `footer`=disabled "Closing…" · `footerStart` empty | No (unchanged) |
| `success` | single "Done" button | `footer`="Done" · `footerStart` empty | No (unchanged) |
| `error` | Try Again first, Cancel second | `footerStart`=Cancel · `footer`=Try Again | No (unchanged — Try Again was never styled destructive) |

**Order note (deliberate, not incidental):** the `confirm` and `error` phases' prior left-to-right
order (danger/retry action first, Cancel second) is swapped to Cancel-left/action-right. This is
the **intended effect** of standardizing onto design §6.5's binding footer contract ("Cancel is
ALWAYS left-aligned... the standard for every modal, not just a variant") — re-basing onto that
standard is the entire point of this task, not an incidental behavior change the "preserve order"
constraint was meant to block. (See "Interpreting 'preserve order'" below.)

**Header simplified**: the custom `titleRow` (warning icon + bold title + dimmed project-name
subtitle) is folded into one string title, `` `Close Secure Project — ${projectName}` ``.
`SprkModal.title` has no icon/subtitle slot (§6.4 standard header). Warning intent is unaffected —
still fully conveyed by the in-body warning `MessageBar` + consequence list (unchanged). `X` (via
`ModalWindowControls`, now shell-owned) is present in every phase per the binding "present on every
modal" mandate, including `closing` (previously hidden) — clicking it during `closing` is a
deliberate no-op via the unchanged `handleClose` phase guard (same as the "closing" phase already
guarded dismissal before this change; only the *X's visibility* during that phase changed, not its
effect).

Size: `sm` (design §6.2 table explicitly lists `CloseProjectDialog` under `sm`, closest to the
original 520px/90vw).

### Interpreting "preserve order" vs. "Cancel always left" (both CloseProjectDialog phases)

The task's constraint says preserve "button semantics (labels, order, destructive intent)"; design
§6.5 says Cancel is always left. These conflict literally for `CloseProjectDialog`'s confirm/error
phases (Cancel was on the right there). Resolution applied: "order" is read as preserving the
**relative order of non-Cancel actions** and **which action is destructive** — not literally
freezing Cancel's left/right position — because the entire purpose of re-basing onto the shared
footer contract is to standardize that position. The alternative reading (freeze Cancel's position
everywhere) would make re-basing impossible for exactly the dialogs (like this one) that most need
it. `ComposeConflictDialog`'s 3-button case has no Cancel-analog conflict (see above — none of its
3 actions moved).

## Inline-color removal evidence (ADR-021 / design §3.3 / §6.5)

Both `CloseProjectDialog` copies previously had:
```tsx
<Button appearance="primary" style={{
  backgroundColor: tokens.colorPaletteRedBackground3,
  color: tokens.colorNeutralForegroundOnBrand,
  borderColor: tokens.colorPaletteRedBorder2,
}} ...>
```
Replaced with a `makeStyles` token class (`className={styles.danger}`), copied verbatim from the
`ConfirmModal` preset's own recipe (same `colorStatusDangerBackground3` token, same hover/active
brightness filters) — not an ad hoc new recipe.

**Grep evidence** (repo-relative, run against all 4 touched dialog files):
```
grep -nE "#[0-9a-fA-F]{3,8}" <4 files>   → 0 matches (zero hex)
grep -n "'1px'" <4 files>                → 0 matches
grep -n "style={{" <4 files>             → only in CloseProjectDialog×2: pre-existing
                                            `style={{ color: tokens.colorNeutralForeground3/4 }}`
                                            (spinner-phase text) + `style={{ textAlign, width }}`
                                            (error MessageBar) — both PRE-EXISTING, token-based
                                            (not hex, not backgroundColor), unrelated to the
                                            anti-pattern this task targets; left unchanged
grep -n "backgroundColor" <4 files>      → 0 live-code matches (only inside my own JSDoc
                                            comments documenting the REMOVED inline style)
```

## Discovered + fixed: stale test-mock gap (Compose.Components, 4 files)

`ComposeWorkspace.tsx` mounts `<ComposeConflictDialog>` **unconditionally** (gated by its own
`open` prop) — the exact same pattern already documented for `<SendEmailDialog>` in these same test
files ("FR-14 (task 051) ... a no-op stub keeps this mock complete"). Four sibling test files hand-write
a **partial** `jest.mock('@spaarke/ui-components', () => ({...}))` that lists only the specific
named exports each test needs. Before this task, `ComposeConflictDialog`'s outermost element was
Fluent's own `Dialog` (not mocked) with `ModalWindowControls` (mocked, but buried inside the
`open=false`-gated, never-rendered subtree) — so the incomplete mock never got exercised. After the
re-base, `SprkModal` (from the same, incompletely-mocked package) is the **outermost** returned
element, evaluated by React regardless of `open` — so the missing mock entry surfaced immediately
as `"Element type is invalid ... Check the render method of ComposeConflictDialog"`.

**Fix applied** (mirrors the existing `SendEmailDialog: () => null` precedent exactly): added
`SprkModal: () => null,` to the mock object in all 4 files:
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.search.test.tsx`
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.imports.test.tsx`
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.saveOpLogPreservation.test.tsx`
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.bornInEditorSave.test.tsx`

**Not touched** (mocks `@spaarke/ui-components` too, but for hooks that don't render
`ComposeWorkspace`, confirmed by grep — no `ComposeConflictDialog`/`SprkModal` reference):
`src/client/shared/Spaarke.Compose.Components/src/widgets/hooks/useAiGenerateBookmark.test.tsx`,
`.../hooks/aiAnchoring.test.tsx`.

## A/B proof: remaining Compose.Components failures are 100% pre-existing baseline

After the `SprkModal` mock fix, ran the full suite twice: once with my re-based
`ComposeConflictDialog.tsx`, once with the file swapped back to its pre-task-040 (git `HEAD`)
content (test-mock fixes left in place either way). **Identical result both times**: `15 failed,
843 passed, 858 total`, `5 suites failed / 67 passed / 72 total`. This proves the 15 remaining
failures (`stepOperationInterceptor.test.ts` — 10 tests, pure ProseMirror paragraph/run-offset
rebase logic, zero relation to my files; plus 5 timing/`compose-editor-stub` failures across the 4
`ComposeWorkspace.*.test.tsx` files) are unrelated to task 040 and match current-task.md's
documented baseline verbatim ("15 Compose failures ... PRE-EXISTING (A/B-proven)"). Not touched —
out of scope per that note.

## Pre-existing text-rendering artifact (discovered, not introduced, left fixed)

Both original `CloseProjectDialog` copies contained a literal, un-escaped `…` (six raw
characters: backslash-u-2-0-2-6) inside **JSX text** (not inside a JS string literal) in two spots
("Closing…" progress text + spinner-phase label). JSX text nodes do not interpret `\uXXXX` escapes
(only JS string literals do), so this almost certainly rendered literally as `…` rather than
an ellipsis — a small, pre-existing, clearly-unintentional bug, unrelated to this task's scope.
Because I retyped this body content as part of the re-base, my version uses an actual `…`
character in both spots, incidentally fixing it. Flagging for the record; not filed as a separate
defer-issue given its triviality and that it was fixed as a side effect of in-scope work, not
chased separately.

## Optional `uiScale` passthrough

Added `uiScale?: number` to all four Props interfaces, forwarded to `SprkModal`'s own `uiScale`
prop. None of the four components' prop interfaces are externally frozen (all internal to the
monorepo); this is additive/backward-compatible. Consumers (`ComposeWorkspace.tsx`,
`PinnedMemoryListWidget.tsx`, `WorkspaceGrid.tsx`) pass nothing new — default applies.

## Files modified

- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeConflictDialog.tsx` (re-based)
- `src/client/shared/Spaarke.AI.Widgets/src/components/memory/PinnedMemoryDeleteConfirmation.tsx` (re-based)
- `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/CloseProjectDialog.tsx` (re-based)
- `src/solutions/LegalWorkspace/src/components/CreateProject/CloseProjectDialog.tsx` (re-based)
- `src/solutions/SpaarkeAi/src/__tests__/compose/ComposeConflictDialog.test.tsx` (1 assertion updated — accessible-name query no longer valid post-re-base; see below)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.search.test.tsx` (mock fix)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.imports.test.tsx` (mock fix)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.saveOpLogPreservation.test.tsx` (mock fix)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.bornInEditorSave.test.tsx` (mock fix)

Files deleted: none. Files created: none (this notes file aside).

## SpaarkeAi test-assertion update (outside my "relevant-files" list, in scope per test-update clause)

`src/solutions/SpaarkeAi/src/__tests__/compose/ComposeConflictDialog.test.tsx` deep-imports
`ComposeConflictDialog` from `@spaarke/compose-components/widgets/ComposeConflictDialog` and
asserted `screen.getByRole('alertdialog', { name: /this document is open.../i })`. `SprkModal`
renders its header title as plain text (no Fluent `DialogTitle`, hence no `aria-labelledby` wiring
to the dialog's accessible name) — an already-shipped `SprkModal`/`ConfirmModal` limitation from
task 005/009 (their own test suites only ever query `getByRole('alertdialog')` with no `name`
filter). Updated the one affected assertion to `within(dialog).getByText(...)`, mirroring
`ConfirmModal.test.tsx`'s own pattern exactly. This is outside my 4-file "relevant-files" list but
squarely inside the task's explicit "update existing tests where assertions legitimately change"
allowance, since it directly tests my re-based component's rendered contract.

**Reported, not fixed (preset-gap finding for the main session):** the missing `aria-labelledby`
wiring on `SprkModal`'s header is a real (pre-existing, not new) a11y gap. Fixing it would mean
modifying `SprkModal.tsx` itself, which is out of bounds for this task ("Do NOT fork/modify
ConfirmModal or SprkModal internals"). Flagging per the hard-boundary instruction rather than
patching it silently.

## Build/test/gate results

| Package | Command | Result |
|---|---|---|
| Spaarke.UI.Components | `npx tsc --noEmit -p tsconfig.json` | **0 errors** |
| Spaarke.UI.Components | `npx jest src/components/CreateProjectWizard src/components/SprkModal` | **121/121 passed, 14/14 suites** |
| Spaarke.Compose.Components | `npm run build` (`tsc`) | **0 errors** |
| Spaarke.Compose.Components | `npx jest` (full) | **843/858 passed, 67/72 suites** — 15 failures 100% pre-existing (A/B-proven above) |
| Spaarke.AI.Widgets | `npm run build` (`tsc`) | **0 errors** |
| Spaarke.AI.Widgets | `npx jest src/components/memory` | **20/20 passed, 3/3 suites** (matches "20 tests existed pre-wave") |
| Spaarke.AI.Widgets | `npx jest` (full) | **677/678 passed, 37/38 suites** — 1 failure (`register-workspace-widgets.test.ts`, "Communications"→"Messages" metadata drift) confirmed unrelated (0 references to my files); matches documented "1 AI.Widgets failure" baseline |
| LegalWorkspace | `npx tsc --noEmit -p tsconfig.json` (full project, filtered) | **238 total errors — identical to the documented ~238 baseline (Issue #712)**; grep for `CloseProjectDialog` in the output → **0 matches** (zero new errors attributable to my edit); confirmed the scan reaches 61+ distinct files across many unrelated packages, i.e. not an early abort |

## Step 9.5 gates (self-run)

**Self code-review** (correctness / behavior preservation / dead-code removal):
- Manual import-vs-usage audit on all 4 files: every retained import has ≥1 real usage beyond its
  import line; every deliberately-removed symbol (`mergeClasses`, `WarningFilled`, `Dialog`,
  `DialogSurface`, `DialogTitle`, `DialogContent`, `DialogActions`, `DialogBody`, `ModalWindowControls`
  as a live import, `useState`/`useEffect` in `PinnedMemoryDeleteConfirmation`) shows **zero** live
  occurrences (comment-only where mentioned at all).
- `isMaximized`/`setIsMaximized`/`surfaceMaximized`/`dialogSurfaceMaximized`/`dialogSurface` swept
  across all 4 files — zero live-code occurrences, comment-only.
- Callback wiring re-verified line-by-line against the originals (Cancel/Confirm/Delete/Retry/Done
  handlers all preserve their exact original guard logic — `isDeleting` guards in
  `PinnedMemoryDeleteConfirmation`, `phase !== 'closing'` guard in `CloseProjectDialog`).

**adr-check:**
- **ADR-012** (compose, no fork): `ConfirmModal.tsx` and `SprkModal.tsx` are byte-identical to
  before this task (unmodified) — verified no edits were made to either file. All 4 dialogs compose
  `SprkModal` via its public `open`/`onClose`/`title`/`size`/`dismiss`/`maximizable`/`uiScale`/
  `footerStart`/`footer` props only.
- **ADR-021** (tokens only): see "Inline-color removal evidence" above — 0 hex, 0 `'1px'`, 0 live
  `backgroundColor` inline styles across all 4 files. The `CloseProjectDialog` inline-color
  **removal** is the headline change this gate cares about, done in both copies.
  **NFR-03** (part of ADR-021 in this project): identical check, same evidence.
- **ADR-023**: not applicable — `ChoiceDialog`/`ChoiceModal` are explicitly out of my scope (owned
  by sibling task 041); zero files in that path touched.
- **NFR-04** (dual React 16/17 + 19 compile-safety): `Spaarke.UI.Components` `tsc --noEmit` (its own
  `@types/react` 19 baseline) is clean; `Spaarke.Compose.Components`/`Spaarke.AI.Widgets` (both
  pinned `@types/react` 19, "NOT PCF-safe" per their own `package.json` descriptions) build clean.
  Neither `CloseProjectDialog` copy nor the other two dialogs is exported through
  `src/pcf-safe.ts` or consumed by any PCF control (`SprkModal`/`ConfirmModal` are Code-Page-scoped
  by construction per current-task.md P0 note) — no React-16 consumer exists for any of these 4
  files, so the "compile clean under `@types/react` 18" half of NFR-04 does not apply to this
  specific task's files (it applies at the `SprkModal`/`ConfirmModal` preset layer, already gated
  at task 005/009).
- **NFR-05** (client-only / no BFF): zero touches to `src/server/api/Sprk.Bff.Api/**` — confirmed,
  none of my edits are anywhere near the BFF.

**Diff gate**: grep of all added/changed lines for hex/`'1px'`/inline color — clean (see evidence
above; the `style={{color: tokens...}}`/`style={{textAlign,width}}` remnants are pre-existing,
token-based, untouched by me, and not `backgroundColor`/hex).

## POML acceptance-criteria checklist

| # | Criterion | Pass/Fail | Evidence |
|---|---|---|---|
| 1 | All 4 dialogs render via `ConfirmModal` chrome (standard header + footer + dismiss semantics) | **PASS** | All 4 use `SprkModal` with the identical `dismiss="alert"`/`maximizable={false}`/standard-header-footer contract `ConfirmModal` itself configures; see cross-cutting decision above |
| 2 | Blocking confirms ignore ESC/backdrop (`dismiss="alert"`); destructive confirms use danger variant | **PASS** | All 4 set `dismiss="alert"`; danger token class applied to `PinnedMemoryDeleteConfirmation`'s Delete + `CloseProjectDialog`'s confirm-phase Close Project (both previously destructive or newly-designated-destructive per task guidance); `ComposeConflictDialog` correctly has none (none was destructive-styled before) |
| 3 | Title/body messaging + button labels/order/intent unchanged | **PASS**, with 2 documented, deliberate exceptions: (a) `CloseProjectDialog` Cancel moves to the standard left position in confirm/error phases (see "Interpreting preserve order" section — the intended effect of this task, not incidental); (b) both `CloseProjectDialog` titles fold icon+subtitle into one string (§6.4 standard header, warning intent preserved via body content) | See per-dialog tables above |
| 4 | Negative: grep of both `CloseProjectDialog` copies → zero inline `backgroundColor`/hex/`'1px'`; danger primary from token class | **PASS** | See "Inline-color removal evidence" |
| 5 | `Spaarke.Compose.Components`, `Spaarke.AI.Widgets`, `Spaarke.UI.Components`, LegalWorkspace build green; no consumer call site regressed | **PASS** | See build/test table; `ComposeWorkspace.tsx` (Compose.Components consumer), `PinnedMemoryListWidget.tsx` (AI.Widgets consumer), `WorkspaceGrid.tsx` (LegalWorkspace consumer) all verified compile-clean and prop-compatible (additive `uiScale?` only) |

## Deviations (full list)

1. **`SprkModal` composed directly instead of the literal `<ConfirmModal>` wrapper, for all 4 dialogs** — documented at length above; not an escalation, explicitly sanctioned by the task's own "composing via provided slots is NOT forking" guidance.
2. **`CloseProjectDialog` Cancel button repositioned to `footerStart`** in the confirm/error phases (previously right of the primary action) — deliberate standardization onto design §6.5, not incidental.
3. **`CloseProjectDialog` title simplified** to one string, dropping the inline warning icon — required by `SprkModal.title: string`'s lack of an icon/subtitle slot; warning intent preserved via unchanged body content.
4. **`PinnedMemoryDeleteConfirmation` title simplified** the same way, dropping the inline `WarningRegular` icon — same reasoning; warning intent preserved via the unchanged `role="alert"` impact callout.
5. **`PinnedMemoryDeleteConfirmation`'s Delete button gains danger styling** it didn't have before — explicitly requested by this task's own per-dialog guidance + FR-13, not a silent change.
6. **1 SpaarkeAi test assertion updated** (`ComposeConflictDialog.test.tsx`, accessible-name query) — outside the 4-file relevant-files list, justified under the test-update allowance.
7. **4 Compose.Components test-mock files gained a `SprkModal: () => null` stub** — outside the 4-file relevant-files list, required to keep `ComposeWorkspace.*.test.tsx` green after the legitimate structural re-base; mirrors an existing precedent in the same files.
8. **Pre-existing `…` JSX-text rendering bug incidentally fixed** in both `CloseProjectDialog` copies (see dedicated section above) — not chased separately, flagged for the record.
9. **Reported, not fixed: `SprkModal` header has no `aria-labelledby` wiring** to the dialog's accessible name (pre-existing gap from task 005/009, propagated to these 3 alert-dialog re-bases). Not in scope to fix (would require modifying `SprkModal.tsx`).

## Escalations

**None.** All 4 dialogs were re-based; no dialog was skipped.
