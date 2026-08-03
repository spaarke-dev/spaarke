# Wave B (Group B / P0 presets) — Completion Note

> **Date**: 2026-08-01
> **Tasks**: 005 (Confirm+Choice), 006 (Form), 007 (Preview+Browse), 008 (Wizard)
> **Executed**: 4 parallel general-purpose sub-agents (Sonnet), consolidated + verified in main session

## Outcome

Six presets built as thin `SprkModal` configs under `SprkModal/presets/`. Consolidated
shared-lib `tsc` build **green**; **81/81** tests pass across 10 suites (sizes, scaledTheme,
SprkModal, ModalWindowControls + 6 presets); eslint clean; zero hex / `'1px'` literals.

| Task | Files | Targeted tests |
|------|-------|----------------|
| 005 | `presets/ConfirmModal.tsx`, `presets/ChoiceModal.tsx` (+2 tests) | 15 |
| 006 | `presets/FormModal.tsx` (+1 test) | 7 |
| 007 | `presets/PreviewModal.tsx`, `presets/BrowseModal.tsx` (+2 tests) | 15 |
| 008 | `presets/WizardModal.tsx` (+1 test) | 14 |

## ⚠️ Decision worth owner review — Task 007 BrowseModal composition

The task 007 POML said BrowseModal "MUST COMPOSE `RecordNavigationModalShell` for browse
chrome + the cross-frame dirty-check." But the prototype's BrowseModal is `PreviewModal` +
`SprkModal`'s own `nav` prop, and nesting `RecordNavigationModalShell`'s Dialog envelope inside
`SprkModal` would render **two headers/counters** — the exact anti-pattern the POML's own
escalation trigger names ("If composing … produces a DOUBLE header, STOP").

**Settled decision (made in main session, handed to the agent as an unambiguous spec — not
escalated):**
- `SprkModal`'s header is the SINGLE title/counter source (design §6.4). BrowseModal forwards
  `nav={{index,total,onNavigate}}` to `SprkModal` for the visible browse chrome. It does NOT
  nest `RecordNavigationModalShell`.
- The "compose the dirty-check, don't fork it" mandate is honored via a **seam**: BrowseModal
  exposes `onBeforeNavigate?: (dir) => boolean | Promise<boolean>`; its internal navigate handler
  calls the guard first and only invokes `onNavigate(dir)` when it resolves truthy. The P4
  conversion (task 060) wires `RecordNavigationModalShell`'s cross-frame dirty-check / discard-
  confirm through this guard — no double chrome, no forked nav header.
- Rationale: preserves the single-header contract + the prototype visual, keeps net component
  count down, and defers the actual dirty-check wiring to where the real record-set lives (P4).
- **If the owner wants literal RecordNavigationModalShell composition instead**, it is a small
  change to BrowseModal (preset API is new, no consumers yet) — flag at review.

## Other deviations (minor)

- **005 ChoiceModal** (built fresh — not in prototype): `dismiss="explicit"`, `size="xs"`
  (design §3.3 maps ChoiceDialog to xs), added `disabled?` per choice to mirror
  `ChoiceDialog`'s option contract. Prop shape `{ open, onClose, title, message?, choices:
  {id,label,description,icon?,disabled?}[], onSelect, uiScale? }` — maps 1:1 to `ChoiceDialog`
  so the P2 re-base (task 041) is mechanical. ADR-023 contract preserved (keyboard Enter/Space
  selection, no default selection, 2-4 rich choices) — verified by tests. Escalation NOT hit.
  Note: `SprkModal` always renders a × (via `ModalWindowControls`) whereas legacy `ChoiceDialog`
  had none — this is the base shell's standardization (task 004), orthogonal to the choice contract.
- **006 FormModal**: scoped `useStyles` to just the `formBody` class (the prototype's shared
  `useStyles` bundled all presets' classes; each preset is now a separate file). Functionally identical.
- **007 PreviewModal**: exports `PreviewGridBody` (shared grid + styles) which BrowseModal imports
  (no style duplication); stage content slot is `children` (falls back to a placeholder); added
  `data-testid` hooks on internal grid cells for deterministic layout assertions (not public API).
- **008 WizardModal**: verbatim prototype port; no deviation.

## Barrel wiring is deferred to task 009

None of these presets are exported from `SprkModal/index.ts` or `components/index.ts` yet — task
009 (barrel + a11y snapshot + dual-React verify) wires the exports. Presets import the shell via
`../SprkModal` directly, so they build + test without the barrel.
