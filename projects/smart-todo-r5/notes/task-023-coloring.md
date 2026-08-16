# Task 023 — Subtle Channel Coloring + Yellow-Contrast Audit (FR-08 / U-1 + F-1)

**Status**: Completed 2026-08-16
**Rigor**: FULL

## What changed

### 1. `KanbanBoard.tsx` (`src/client/shared/Spaarke.UI.Components/src/components/Kanban/KanbanBoard.tsx`)

Split the single `columnInlineStyle` closure (which previously composed `accentColor` — a
3px top-border — AND `tintColor` — a full-column `backgroundColor` wash — into one object
applied to the whole column container) into two separate, narrowly-scoped styles:

- `columnInlineStyle` — now carries **only** the `accentColor` top-border (unchanged: 3px
  solid border using the column's `Border2`-tier token). Still applied to the column
  container div.
- `columnHeaderTintStyle` — **new**: `column.tintColor` (unchanged Background1-tier token
  values) is now applied as `backgroundColor` on the `columnHeader` div only (the title /
  subtitle / count-pill strip, ~44px tall), composed with the pre-existing `cursor: pointer`
  inline style for the collapse-toggle affordance.

Net effect: the card-list body (where most of the visible column area is) no longer carries
any background wash. The urgency cue is now: (a) a thin 3px accent-colored top border
spanning the full column height, and (b) a lightly tinted header strip. Both options are
explicitly named in spec FR-08 ("thin accent bar OR lightly tinted header") — this
implementation keeps **both**, since the accent bar was already thin/subtle and removing it
entirely would have weakened scannability more than necessary.

### 2. `useKanbanColumns.ts` (`src/client/shared/Spaarke.SmartTodo.Components/src/hooks/useKanbanColumns.ts`)

Comment-only change. The `bucketTodoItems()` doc comment above the column definitions was
rewritten to describe the new header-only application (owned by `KanbanBoard.tsx`) instead
of the old full-column wash. **No token values changed** — `accentColor`, `tintColor`, and
`countTextColor` per column are bit-for-bit identical to before this task. This keeps the
change additive: any other `IKanbanColumn` consumer that reads `column.tintColor` for its
own purposes sees the same values as before; only `KanbanBoard.tsx`'s interpretation of
where to render it changed.

### 3. `useKanbanColumns.test.ts` (`src/client/shared/Spaarke.SmartTodo.Components/__tests__/useKanbanColumns.test.ts`)

Added one new test function, `runSubtleColoringTokenMappingTest()` (additive — the four
existing task-022 tests are untouched), asserting:
- Semantic mapping preserved: `Today.accentColor === colorPaletteRedBorder2`,
  `Tomorrow.accentColor === colorPaletteYellowBorder2`,
  `Future.accentColor === colorPaletteGreenBorder2`.
- Token tier preserved: `tintColor` for each column is still the `Background1` tier of the
  matching hue.
- F-1 fix stays pinned: `Tomorrow.countTextColor === colorNeutralForeground1`;
  Today/Future have no `countTextColor` override (keep the default
  `colorNeutralForegroundOnBrand`, which is safe against their more-saturated `Border2`
  pill background).
- Zero hex/rgb literals in any emitted token value (regex-verified).

The new test was executed (not just type-checked) via a throwaway compile-and-run harness
(transpiled with the `typescript` package already in `node_modules`, executed with Node,
then deleted) since this peer package has no Jest wiring yet (tracked separately as task
040). All 5 tests (4 existing + 1 new) pass.

## Token tiers chosen (final)

| Column | `accentColor` (top border, container) | `tintColor` (header background) | `countTextColor` |
|---|---|---|---|
| Today | `tokens.colorPaletteRedBorder2` | `tokens.colorPaletteRedBackground1` | *(unset → `colorNeutralForegroundOnBrand`)* |
| Tomorrow | `tokens.colorPaletteYellowBorder2` | `tokens.colorPaletteYellowBackground1` | `tokens.colorNeutralForeground1` |
| Future | `tokens.colorPaletteGreenBorder2` | `tokens.colorPaletteGreenBackground1` | *(unset → `colorNeutralForegroundOnBrand`)* |

None of these values changed from the pre-task-023 state — only where `tintColor` renders
(header strip vs. full column body) changed.

## Contrast reasoning (why this is safe in both themes)

Fluent v9's `colorPalette<Hue>Background1` token is generated from the hue's shared-color
ramp differently per theme (confirmed by reading the installed
`@fluentui/tokens` package source, `lib/alias/{light,dark}ColorPalette.js`):

- **Light theme**: `Background1 = tint60` (a very light/pale tint of the hue — e.g. pale
  yellow). `colorNeutralForeground1` (used by `columnTitle`) resolves near-black in light
  theme. Pale background + near-black text → high contrast.
- **Dark theme**: `Background1 = shade40` (a *dark*, desaturated shade of the hue — e.g. a
  dark olive/brown for yellow, NOT a bright yellow). `colorNeutralForeground1` resolves
  near-white in dark theme. Dark background + near-white text → high contrast.

Because `colorNeutralForeground1` is itself theme-adaptive (dark in light theme, light in
dark theme) and `Background1` inverts lightness the same way between themes, the existing
`columnTitle` text (`color: tokens.colorNeutralForeground1`, unchanged by this task) stays
high-contrast against the header tint in both themes automatically — no per-theme override
needed.

This is the same reasoning that already justified the codebase's established
`colorPaletteYellowBackground3` + `colorNeutralForeground1` pairing (grep-confirmed
identical in `KanbanCard.tsx`, `DismissedSection.tsx` (both packages), and
`TodoDetailPane.tsx` — 9 call sites across the Code Page and the widget, ALL already
correct). The step-1 grep sweep (`colorPaletteYellow` across both
`Spaarke.SmartTodo.Components` and `src/solutions/SmartTodo`) found **zero** remaining
white-on-yellow surfaces — F-1 was already fully fixed prior to this task, exactly as
spec.md's Open Question log predicted ("F-1 white-on-yellow: Already fixed in code /
deployment — FR-08 downgraded to a verification sweep").

## Reconciliation with the widget's "existing tints"

The orchestrator's task context flagged: *"the widget already applies column tints via
`colorPaletteRed/Yellow/GreenBackground1` — reconcile with that; don't double-apply or
fight it."*

Investigated: `SmartTodoWidget.tsx`'s header doc comment (lines 18-23) describes "COLUMN
TINTS... `colorPaletteRedBackground1` / `YellowBackground1` / `GreenBackground1`" — but this
is **documentation of the same mechanism**, not a second, independent tint application. The
widget does not apply its own tint; it consumes `useKanbanColumns()`'s `columns` output
(which carries `tintColor`) and passes it straight through to the shared `<KanbanBoard>`
component, which is the ONLY place `tintColor` is ever rendered. There is no double-apply
risk — there was only ever one rendering site (`KanbanBoard.tsx`), and this task changed
that single site's scoping. `SmartTodoWidget.tsx` itself was **not modified** (out of this
task's 3-file scope) — its doc comment is now slightly stale (still says "column
background tint" rather than "column header tint"), which is a minor doc-drift item, not a
functional issue; noted here rather than silently left unflagged.

## Consumer backward-compatibility

Grep-confirmed (2026-08-16, re-verified per this task's constraint) that `KanbanBoard.tsx`'s
only consumers are the SmartTodo family:
- `src/solutions/SmartTodo/src/components/SmartToDo.tsx` (Code Page)
- `src/client/shared/Spaarke.SmartTodo.Components/src/widgets/SmartTodoWidget/SmartTodoWidget.tsx` (SpaarkeAi widget)
- `src/client/shared/Spaarke.SmartTodo.Components/src/components/SmartToDo/SmartToDo.tsx` and
  `SmartTodoKanban.tsx` (peer-package variants)

No other consumer sets `tintColor`/`accentColor` on an `IKanbanColumn`, and both fields
remain optional (`undefined` → no style applied, unchanged behavior) — so this change is
safe for any future consumer that doesn't opt in.

## Verification performed

- `npx tsc --noEmit` in `Spaarke.SmartTodo.Components` — 0 errors.
- `npx tsc --noEmit` in `Spaarke.UI.Components` — 0 errors in `KanbanBoard.tsx` (3
  pre-existing, unrelated `@spaarke/auth` / `@spaarke/sdap-client` module-resolution errors
  elsewhere in the package, confirmed pre-existing and out of scope).
- `npx jest src/components/Kanban/__tests__/KanbanBoard.test.tsx` — 12/12 passed (existing
  suite, unmodified — confirms zero DnD/layout/a11y regression).
- Ad-hoc compile+run of `useKanbanColumns.test.ts` (transpiled via the `typescript` package,
  executed with Node, harness deleted afterward) — 5/5 passed, including the new
  `runSubtleColoringTokenMappingTest`.
- `grep` for hex/rgb/hsl literals across all 3 changed files — 0 matches.
- `grep` for `colorPaletteYellow` across both `Spaarke.SmartTodo.Components` and
  `src/solutions/SmartTodo` — every match already pairs with a dark neutral foreground
  (`colorNeutralForeground1`); 0 white-on-yellow surfaces found.
- `code-review` + `adr-check` quality gates (Step 9.5, FULL rigor) — both clean; see task
  POML `<notes>` for the summary.

## Escalation trigger — not fired

The POML's escalation trigger ("if reducing the column tint makes the 3-column urgency
mapping genuinely hard to distinguish at a glance") did **not** fire. The primary urgency
cue (3px accent-colored top border, spanning the full column height) is unchanged from
before this task, and the count pill (colored with the same saturated `Border2`/`Foreground`
tier as the border) remains a strong, immediately-visible per-column color signal. The header
tint is a secondary reinforcement on top of both, not the sole cue — so removing the
full-column wash does not meaningfully reduce scannability.
