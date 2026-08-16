# Task 012 — Priority/effort per-card UI (FR-02/FR-03)

## Summary

Added the FR-02 priority glyph + FR-03 effort indicator to **both** live Kanban
card surfaces in `@spaarke/smart-todo-components`, and extended
`PriorityScoreCard`/`EffortScoreCard` (in place — no new component files) to
also surface the raw Choice selection alongside their existing score-derived
breakdown. Presentation-only; the composite score formula in `todoScoring.ts`
was not touched.

## Which card(s), and why both

The POML's `<relevant-files>`/`<outputs>` only named the widget card
(`components/KanbanCard/KanbanCard.tsx`) — a pre-hoist-era assumption. By the
time this task ran, task 002's hoist had landed a **second**, independently
live Kanban card:

- `components/KanbanCard/KanbanCard.tsx` — the flexbox widget card, bare
  `KanbanCard` export, consumed by `SmartTodoKanban.tsx` (→ the SpaarkeAi
  `SmartTodoWidget`).
- `components/SmartToDo/KanbanCard.tsx` — the `RecordCardShell`-based rich
  card, re-exported under the alias `SmartToDoKanbanCard`, consumed by
  `SmartToDo.tsx` (→ the LegalWorkspace SmartTodo Code Page).

Both are real, currently-rendered production surfaces (confirmed via grep of
`SmartTodoKanban.tsx` and `SmartToDo.tsx`). The task's own `<goal>` says the
glyph appears "on every card" and spec FR-02's acceptance criterion is
card-surface-agnostic ("card shows the priority indicator"). Leaving either
surface without the indicator would fail that intent even though the POML's
file list (authored pre-hoist) only named one. Both were updated with
functionally identical (per-file, see below) glyph/badge logic.

## PriorityScoreCard / EffortScoreCard — extended, not duplicated

Per the task's acceptance criterion ("No new component created for this
surface"), both cards were extended **in place**:

- `IPriorityScoreCardProps` / `IEffortScoreCardProps` gained a new **optional**
  prop (`priorityChoice?: number | null` / `effortChoice?: number | null`) —
  fully backward compatible, existing call sites (`TodoAISummaryDialog.tsx`)
  are unaffected and continue to compile/render unchanged.
- A new **internal, non-exported** sub-component (`PriorityChoiceBadge` /
  `EffortChoiceBadge`) was added to each file, reusing the existing
  `LevelBadge`/`EffortLevelBadge` colour-tier CSS classes (`levelBadgeUrgent`,
  `levelBadgeHigh`, `levelBadgeNormal`, `levelBadgeLow`, `levelBadgeMed`) —
  only 2 net-new CSS classes were added across both files
  (`levelBadgeChoiceHigh`, `levelBadgeChoiceNone` in `EffortScoreCard.tsx`,
  for the 2 choice tiers — "High" and "None" — that don't map onto an
  existing score-derived tier).
- **Not wired to a live data source.** `TodoAISummaryDialog.tsx` (the only
  current consumer of `PriorityScoreCard`/`EffortScoreCard`) receives
  `ITodoScoringResult` (AI-summary/mock scoring data), not a raw `ITodo` with
  `sprk_priority`/`sprk_effort`. Wiring real Choice values through that call
  chain would require touching `SmartToDo.tsx` → `SmartToDoDialog.tsx` →
  `TodoAISummaryDialog.tsx`, none of which this task's scope named, and the
  whole AI-summary surface is explicitly mock-data-driven today ("Preview
  data — connect to BFF for live scoring", pre-existing `mockNotice`). The
  props are additive and inert until a future task wires a real value in —
  this is the intentionally correct "neutral no-op" state for the
  criterion ("unset → no crash, no misleading colour").

## Data typing — local structural casts, not shared-type edits

Neither `IKanbanCardTodo` (`types/kanban.ts`), `ITodo` (`types/entities.ts`),
nor `ITodoPriorityScore`/`ITodoEffortScore` (`types/todoScoringTypes.ts`)
declare `sprk_priority`/`sprk_effort` yet. This task's edit scope was
constrained to `src/components/**` only (I am 1 of several concurrent agents
in this worktree this wave; `src/types/**` was out of bounds). Rather than
widen those shared contracts, each `KanbanCard.tsx` declares a small **local,
file-private** structural-extension interface
(`IKanbanCardPriorityEffortFields`) and reads the two fields via a documented
`as unknown as` cast. This is the same trade-off already accepted elsewhere in
this codebase for reading Dataverse-shaped data ahead of its TS contract, and
is fully commented in both files with the rationale + scope-boundary reason.
**Follow-up recommendation**: a future task should widen `IKanbanCardTodo` /
`ITodo` to formally declare `sprk_priority?: number` / `sprk_effort?: number`
and remove these local casts — tracked here rather than silently left
undocumented.

## Colour + label mapping — locally duplicated per file (not shared)

Both `KanbanCard.tsx` files already independently duplicate their own
`DUE_BADGE_STYLE` colour map (confirmed identical values, confirmed via
`git diff`/read — pre-existing pattern, not introduced by this task). This
task followed that same established per-file-duplication convention for the
new `derivePriorityGlyph`/`deriveEffortBadge` maps, rather than introducing a
new shared module (CLAUDE.md §11 — extending an established in-file pattern
over adding new shared surface). The 4-tier priority palette and 5-tier
effort palette are documented inline in each file.

## Value → colour mapping (documented rationale)

- **Priority** (icon `Flag16Filled`, coloured via `style={{ color: token }}`):
  Urgent → `colorStatusDangerForeground1`, High → `colorStatusWarningForeground1`,
  Medium → `colorStatusSuccessForeground1` (reuses the "Normal" tier's tone —
  same semantic weight as the pre-existing score-derived level badge),
  Low → `colorNeutralForeground3`.
- **Effort** (compact pill, background+foreground pair, "quick-wins-first"
  semantics — extends the existing 3-tier `EffortScoreCard` danger/warning/
  success vocabulary to 5 tiers): None → neutral, Very High → danger,
  High → dark-orange (new intermediate tier, precedented by the due-badge's
  "3d" tier), Medium → warning, Low → success.

## Verification

- `cd src/client/shared/Spaarke.SmartTodo.Components && npx tsc --noEmit` →
  **exit 0**, zero errors (confirmed both before and after all edits).
- **No test runner in this package** (`package.json` scripts are `build`/`lint`
  = `tsc --noEmit` only; no Jest/vitest devDependency; confirmed via
  `package.json` read + `find __tests__`). Matching the existing precedent set
  by `useKanbanColumns.test.ts` / `SmartTodoWidget.test.tsx` (both explicitly
  documented as "Jest-less pure-function smoke tests... task 040 wires Jest
  in"), added `__tests__/priorityEffortCardUi.test.ts` — a dependency-free,
  `assert()`-based pure-function test exercising every exported mapping
  function (`derivePriorityGlyph`/`deriveEffortBadge` ×2 card variants,
  `priorityChoiceLabel`, `effortChoiceLabel`) across all defined Choice values
  + unset/out-of-range inputs. This is **not** a render test (no DOM/JSX
  assertions) — that gap is real and pre-existing in this package, not
  something this task could close without Jest (task 040). Verified the new
  test file type-checks cleanly against the full compiler options via a
  temporary scratch tsconfig (`include: ["src", "__tests__"]`, deleted
  immediately after the check) — zero errors from the new file or its
  imports; the only errors surfaced were 4 pre-existing, unrelated errors in
  `SmartTodoWidget.test.tsx` (a sibling agent's file, not touched by this
  task).
- `grep -nE "#[0-9a-fA-F]{3,8}\b|rgb\(|rgba\("` across all 5 modified files →
  zero matches (ADR-021 compliance).

## Files touched (all within `src/components/**` + this package's `__tests__/**`)

- `src/client/shared/Spaarke.SmartTodo.Components/src/components/KanbanCard/KanbanCard.tsx`
- `src/client/shared/Spaarke.SmartTodo.Components/src/components/KanbanCard/KanbanCard.styles.ts`
- `src/client/shared/Spaarke.SmartTodo.Components/src/components/SmartToDo/KanbanCard.tsx`
- `src/client/shared/Spaarke.SmartTodo.Components/src/components/SmartToDo/PriorityScoreCard.tsx`
- `src/client/shared/Spaarke.SmartTodo.Components/src/components/SmartToDo/EffortScoreCard.tsx`
- `src/client/shared/Spaarke.SmartTodo.Components/__tests__/priorityEffortCardUi.test.ts` (new)
- This file + the task POML (status update)

No files under `src/hooks/**`, `src/utils/todoScoring.ts`, `src/types/**`,
`Spaarke.UI.Components/**`, or `src/solutions/**` were touched. Nothing was
staged or committed.
