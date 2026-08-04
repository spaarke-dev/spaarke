# Task 041 Completion — P2 Re-base ChoiceDialog onto ChoiceModal (ADR-023, FR-13)

> RIGOR: FULL. Executed per task-execute protocol. Dependency (005 — ChoiceModal preset) confirmed ✅ in TASK-INDEX.md before starting.

## Summary

`ChoiceDialog.tsx` is now a thin, backward-compatible adapter over the `ChoiceModal` preset
(`SprkModal/presets/ChoiceModal`, built task 005). All chrome (envelope, header, window
controls, footer) **and** per-choice rendering (icon/label/description buttons) are now
supplied by `ChoiceModal`; `ChoiceDialog` itself contains no JSX styling/rendering logic of
its own — only the prop-shape translation. The public `IChoiceDialogProps` /
`IChoiceDialogOption` contract is unchanged (all fields kept; two new **optional** fields
added: `uiScale`, and `cancelText` retained as a documented no-op — see Deviations).

## ADR-023 (choice-dialog-pattern) behavior checklist — preserved, with evidence

| Behavior (from `.claude/patterns/ui/choice-dialog-pattern.md` + task constraint) | Preserved? | Evidence |
|---|---|---|
| 2-4 mutually exclusive rich choices render | ✅ | `ChoiceDialog.test.tsx`: "renders 2 choices…", "renders 3 choices", "renders 4 choices (the ADR-023 upper bound)" — all pass |
| Each choice shows icon + title + description | ✅ | Same 3 tests assert `option.title` + `option.description` text present; icon passed through unchanged (`option.icon` → `choice.icon`, same `React.ReactNode`) |
| Stack vertically | ✅ | Unchanged — `ChoiceModal`'s `choices` container is `flexDirection: 'column'` (only source of choice layout now) |
| `Button appearance="outline"` for options | ✅ | Unchanged — implemented inside `ChoiceModal` (`appearance="outline"` on each choice button), not forked |
| Cancel always present, no auto-selection | ✅ | "Cancel invokes onDismiss and selects nothing" test; no default/pre-selected choice exists in either the old or new implementation |
| Selection returns the chosen key via `onSelect` | ✅ | "selecting a choice calls onSelect with exactly that option id, never onDismiss" — passes with the ORIGINAL `onSelect: (optionId: string) => void` signature, unchanged |
| Cancel invokes `onDismiss`, no selection | ✅ | Same test class; also "the × close control invokes onDismiss (same handler as Cancel)" — both explicit-exit paths converge on the same original callback |
| Disabled option does not fire onSelect | ✅ | "a disabled option does not fire onSelect" — passes |
| Keyboard-operable (Enter) | ✅ | "is keyboard-operable: focusing a choice and pressing Enter selects it" — real `@testing-library/user-event` keyboard test, passes. (Space + Enter both already covered exhaustively at the `ChoiceModal` preset level — not re-duplicated here to avoid redundant coverage.) |
| Semantic tokens only (ADR-021), dark-mode parity | ✅ | "renders correctly under the dark theme" test; zero styling code remains in `ChoiceDialog.tsx` itself (100% delegated to `ChoiceModal`, which is itself token-only, verified at task 005) |
| Max 4 options | ⚪ not runtime-enforced | Same as pre-re-base — neither the old nor new implementation asserts/clamps count at runtime; this was always a documented convention, not a guard clause. No regression. |

**All ADR-023-locked behaviors preserved exactly.** New `ChoiceDialog.test.tsx` (12 tests) + inherited `ChoiceModal.test.tsx` (9 tests) = 21/21 passing.

## Consumers — grep + compile-safety audit

Repo-wide grep for `ChoiceDialog` (44 files touched project docs/POMLs/ADRs — not consumers). Real production consumers of the **React component**:

| File | Real consumer? | Props used | Affected by this change? |
|---|---|---|---|
| `src/solutions/SpaarkeAi/src/components/conversation/FileAttachSessionPrompt.tsx` | **Yes** | `open`, `title`, `message`, `options` (`id`/`icon`/`title`/`description`), `onSelect`, `onDismiss` | No — every field it uses is unchanged in shape and behavior |
| `src/solutions/SpeAdminApp/src/components/containers/PermissionPanel.tsx` | No — comment-only reference (explains why they used a plain `Dialog` instead, for a yes/no confirm) | — | — |
| `src/client/webresources/js/sprk_DocumentOperations.js` + `infrastructure/dataverse/ribbon/DocumentRibbons/WebResources/sprk_DocumentOperations.js` | No — independent hand-rolled vanilla-JS `showChoiceDialog`, does not import this React component (separate anti-pattern overlay, retired by a DIFFERENT task, 092) | — | — |
| `src/solutions/SpaarkeAi/.../__tests__/ConversationPane.file-attach-session-prompt.test.tsx` | Test, exercises `FileAttachSessionPrompt` (and transitively `ChoiceDialog`) | asserts button names "Start a new session" / "Add to current session" / "Cancel" | No — all assertions are on option titles / cancel text, all unchanged; does not assert on internal DOM/aria structure that changed |

**No consumer passes `cancelText` with a non-default value anywhere in `src/`** (verified via repo-wide grep for `cancelText` — the only hits are the prop declaration/default inside `ChoiceDialog.tsx` itself and the unrelated `sprk_DocumentOperations.js` vanilla-JS dialog).

### pcf-safe.ts finding

`ChoiceDialog` is **NOT exported from `src/pcf-safe.ts`** (grepped directly — no match). It is exported only from the main barrel (`components/index.ts` → `export * from './ChoiceDialog'`, consumed by `SprkModal`'s own barrel similarly). Repo-wide grep also confirms **zero files under `src/client/pcf/**` import `ChoiceDialog`.** This is a Code-Page-only component today; unlike other P2 sibling conversions, there is no PCF surface transitively affected by this specific file. (This is the inverse of the situation the task prompt flagged as "expected-OK" for other dialogs — here there's simply no PCF exposure to reason about.)

## Prop-contract mapping (old → adapter → preset)

| `IChoiceDialogProps` (public, unchanged) | Adapter behavior | `ChoiceModalProps` (preset) |
|---|---|---|
| `open: boolean` | passthrough | `open: boolean` |
| `title: string` | passthrough | `title: string` |
| `message: string \| React.ReactNode` | passthrough (`ReactNode` is a superset of `string`, no cast needed) | `message?: React.ReactNode` |
| `options: IChoiceDialogOption[]` | `.map()` → `{id, label: option.title, description, icon, disabled}` | `choices: ChoiceModalChoice[]` |
| `onSelect: (optionId: string) => void` | passthrough (identical signature) | `onSelect: (choiceId: string) => void` |
| `onDismiss: () => void` | renamed | `onClose: () => void` |
| `cancelText?: string` | **accepted, not wired through** — see Deviations | *(no equivalent prop exists)* |
| `uiScale?: number` *(new, additive)* | passthrough | `uiScale?: number` |

Per-option field mapping: `id`→`id` (direct), `title`→`label` (rename), `description`→`description` (direct), `icon`→`icon` (direct, required→optional is a safe widening), `disabled`→`disabled` (direct, both optional).

## Files modified

- `src/client/shared/Spaarke.UI.Components/src/components/ChoiceDialog/ChoiceDialog.tsx` — re-based (48 insertions / 134 deletions per `git diff --stat`). Removed: `Dialog`/`DialogSurface`/`DialogTitle`/`DialogBody`/`DialogActions`/`DialogContent`/`Button`/`Text`/`makeStyles`/`tokens` imports, the `ModalWindowControls` import + its `action` wiring (task 030 interim, now superseded), the local `useStyles` block (`surfaceMaximized`/`content`/`optionsContainer`/`optionButton`/`optionIcon`/`optionText`/`optionTitle`/`optionDescription` — all now owned by `ChoiceModal`), and the local `isMaximized` state + reset-on-close effect (task 030 interim). Added: `ChoiceModal`/`ChoiceModalChoice` import, the `choices` mapping, `uiScale` prop.
- `src/client/shared/Spaarke.UI.Components/src/components/ChoiceDialog/__tests__/ChoiceDialog.test.tsx` — **new** (no test file existed for `ChoiceDialog` pre-re-base). 12 tests: 2/3/4-choice render, selection returns correct id, disabled-option no-op, Cancel semantics, keyboard Enter, chrome-from-ChoiceModal proof (× distinct from Cancel, × invokes onDismiss), ReactNode message support, `cancelText` backward-compat no-op documentation, dark-theme parity.

**Not modified** (per hard boundary): `ChoiceDialog/index.ts` (no export-name changes needed — `ChoiceDialog`, `IChoiceDialogProps`, `IChoiceDialogOption`, default export all unchanged), `ChoiceModal.tsx`, `SprkModal.tsx`, `TASK-INDEX.md`, `current-task.md`, `.claude/**`, `pcf-safe.ts`.

## Verification

| Check | Command | Result |
|---|---|---|
| TypeScript (shared lib) | `npx tsc --noEmit -p tsconfig.json` | **PASS** — exit 0, zero errors (run twice, before and after a test-quality fix) |
| Scoped Jest | `npx jest src/components/ChoiceDialog src/components/SprkModal/presets/__tests__/ChoiceModal` | **PASS** — 2 suites, **21/21 tests** (12 new `ChoiceDialog` + 9 existing `ChoiceModal`) |
| ESLint (touched files) | `npx eslint src/components/ChoiceDialog/ChoiceDialog.tsx src/components/ChoiceDialog/__tests__/ChoiceDialog.test.tsx` | **PASS** — zero errors/warnings (only a pre-existing, unrelated `eslint.config.js` module-type Node warning) |
| `npm run build` (shared lib) | — | **Intentionally NOT run** per this wave's build discipline (3 parallel agents share `Spaarke.UI.Components`'s `dist/`) — main session runs the consolidated build + full suite after the wave |
| PCF consumer build | — | **N/A for this file** — `ChoiceDialog` has zero PCF consumers (see pcf-safe finding above); main session's post-wave PCF build (covering sibling tasks' files) still applies to the wave as a whole |
| Code Page consumer build (`SpaarkeAi`, `FileAttachSessionPrompt.tsx`) | — | Deferred to main session's post-wave consolidated build per this wave's explicit discipline note; verified by prop-shape audit instead (table above) — every prop `FileAttachSessionPrompt` passes is unchanged in type and behavior |

## Step 9.5 gates (FULL rigor, self-run)

**Self code-review of the diff:**
- Dead code fully removed: task-030 interim `ModalWindowControls`/`isMaximized` wiring, all local styles, all Fluent `Dialog*` imports no longer used. No orphaned imports remain (`tsc`/`eslint` both clean).
- No new abstractions introduced; the component is now a pure translation layer (map props, render one child).
- No behavior branches added/removed beyond the prop mapping itself.

**adr-check:**
- **ADR-023** (choice-dialog-pattern): contract preserved exactly — see behavior checklist above, each row backed by a passing test.
- **ADR-012** (compose, don't fork): `ChoiceModal.tsx` and `SprkModal.tsx` are untouched (`git status` shows only `ChoiceDialog.tsx` modified + the new test file) — composed via import + render only.
- **ADR-021** (tokens only): `ChoiceDialog.tsx` now contains **zero** styling code (no `makeStyles`, no tokens, no colors) — 100% delegated. `git diff` grep for hex / `'1px'` / inline-color on added lines: **zero matches**.
- **NFR-04** (dual-React compile): `tsc --noEmit` green under the shared lib's own config (which types against `@types/react` per its `tsconfig.json`); no PCF consumer exists for this specific file (see above) so the React-16/17 boundary isn't independently exercised by this change, but nothing in the diff introduces a React 18+-only API.
- **NFR-05** (client-only): trivially satisfied — no BFF files touched.

**Diff gate:** `git diff` on added lines, grepped for `#[0-9a-fA-F]{3,8}`, `rgb(`/`rgba(`, `'1px'`/`"1px"`, `style={{` → **zero matches**.

## POML acceptance-criteria checklist

| # | Criterion | Pass/Fail |
|---|---|---|
| 1 | Chrome (envelope + header + window controls + footer) supplied by `ChoiceModal`, not a local Dialog | **PASS** — `ChoiceDialog.tsx` renders only `<ChoiceModal>`; zero local `Dialog`/`DialogSurface` JSX remains. Test: "chrome (window controls) comes from ChoiceModal/SprkModal, not a local Dialog." |
| 2 | 2/3/4 choices: selection + cancel identical to pre-re-base ADR-023 behavior | **PASS** — see behavior checklist; `onSelect`/`onDismiss` signatures and call semantics unchanged |
| 3 | Existing consumers compile + behave unchanged; public prop/call contract backward-compatible | **PASS** — sole real consumer (`FileAttachSessionPrompt.tsx`) uses only fields whose shape/behavior is unchanged; `tsc` clean |
| 4 | Negative: no hex/`'1px'`/inline color; no fork of `ChoiceModal` | **PASS** — see diff gate + adr-check above |
| 5 | Shared-lib build, one PCF consumer, one Code Page consumer build green (React 18 / React 19) | **PARTIAL, by design** — shared-lib `tsc` **PASS**; PCF **N/A** (zero PCF consumers of this file); Code Page consumer build **deferred to main session's post-wave consolidated build** per this wave's explicit build-discipline instructions (prop-shape audited instead, see Verification table) |

## Deviations / escalations

None rise to a hard STOP per the task's escalation trigger (the ADR-023 2-4 rich-choice contract — choice count, per-choice rendering, selection/return value, cancel behavior — is fully preservable through `ChoiceModal` unchanged, and is preserved). Three lower-severity items are flagged for owner visibility:

1. **`cancelText` becomes a documented no-op.** The pre-re-base `ChoiceDialog` accepted an optional `cancelText` to override the Cancel button's label. `ChoiceModal` (task 005) hardcodes a fixed "Cancel" label with no override slot — and this task's hard boundary forbids forking/modifying `ChoiceModal` to add one. Notably, its **sibling** preset `ConfirmModal` (same task 005) *does* expose an equivalent `cancelLabel?: string`, so this is an asymmetry between the two presets rather than a deliberate ChoiceModal design choice as far as I can tell from its source comments. Zero real consumers in the repo set a non-default `cancelText` today (verified by repo-wide grep), so there is no observable behavior change for any existing caller. Kept the prop in `IChoiceDialogProps` (accepted, not destructured) for structural backward compatibility, documented the no-op in its JSDoc, and added a test asserting the current (accept-but-ignore) behavior explicitly. **Recommendation**: if a future caller needs a custom cancel label, the cheap fix is adding `cancelLabel?: string` to `ChoiceModal` mirroring `ConfirmModal` — a small, non-breaking preset change for the task-005 owner/a follow-up, not this task.
2. **`dismiss="explicit"` removes ESC/backdrop-click dismissal.** The pre-re-base `ChoiceDialog` used a plain Fluent `Dialog` with default dismiss behavior, so pressing ESC or clicking the backdrop DID call `onDismiss()`. `ChoiceModal` (task 005, already ✅) hardcodes `dismiss="explicit"`, so with this re-base, ESC/backdrop no longer dismiss — only the × or the Cancel button do. This is an intentional, already-documented design decision made and reasoned through in task 005's `ChoiceModal.tsx` source comment (explicit dismiss "strengthens — rather than degrades" ADR-023's "force a conscious choice, no accidental dismissal" rule), not a new decision introduced by this task. Flagging here for visibility since it IS a literal behavior change from the prior implementation, even though it's a sibling task's already-settled call that this task's job is to build on top of, not re-litigate.
3. **Maximize control is removed** (present only briefly, since task 030's interim wiring). `ChoiceModal` sets `maximizable={false}` (by design — `xs` size class is documented as "small decision surface"). This re-base removes the maximize toggle that task 030 had added as an explicitly-labeled *interim* measure ("this re-base supersedes that interim wiring" — POML's own notes). Not a regression relative to the ADR-023 pattern itself (which never had a maximize concept); purely returns `ChoiceDialog` to a state consistent with the `xs` size class's intended design.

No fork of `ChoiceModal`/`SprkModal` was needed or performed. No `.claude/**`, `TASK-INDEX.md`, or `current-task.md` files were touched (main session to update `TASK-INDEX.md` 041 → ✅ per the wave's established pattern).
