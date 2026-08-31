# Task 024 — SmartTodoWidget default = side-by-side columns (FR-09 / U-2)

**Status**: Complete
**Date**: 2026-08-15
**Files touched** (concurrency-scoped, only these 2):
- `src/client/shared/Spaarke.SmartTodo.Components/src/widgets/SmartTodoWidget/SmartTodoWidget.tsx`
- `src/client/shared/Spaarke.SmartTodo.Components/__tests__/SmartTodoWidget.test.tsx`

## Confirmed enum mapping (U-2 resolved)

`orientation='horizontal'` produces **side-by-side columns** (left/center/right);
`orientation='vertical'` produces **stacked rows**. Confirmed by reading the CSS
in `Spaarke.UI.Components/src/components/Kanban/KanbanBoard.tsx`:

- The base `board` Griffel style (applied when NOT vertical) is
  `display: 'flex', flexDirection: 'row', overflowX: 'auto', overflowY: 'hidden'`
  — a horizontal flex row = side-by-side columns.
- `boardVertical` (merged in only when `orientation === 'vertical'`, via
  `mergeClasses(styles.board, isVertical && styles.boardVertical)` at
  `KanbanBoard.tsx:226`) overrides to
  `flexDirection: 'column', overflowY: 'auto', overflowX: 'hidden'` — stacked rows.
- `KanbanBoardInner`'s own prop default is `orientation = 'horizontal'`
  (`KanbanBoard.tsx:221`), and `KanbanBoard.test.tsx` independently asserts
  `data-orientation="horizontal"` is the board's default state.

This matches the POML's pre-investigation exactly (the "ambiguous enum naming"
caveat in spec.md/CLAUDE.md Assumptions is resolved — no further ambiguity
remains). No conflict with U-2; nothing escalated.

## Default change

`SmartTodoWidget.tsx`'s local `orientation` state (`React.useState<Orientation>`,
previously seeded `'vertical'`) now defaults to a new exported constant:

```ts
export const SMART_TODO_WIDGET_DEFAULT_ORIENTATION: Orientation = 'horizontal';
...
const [orientation, setOrientation] = React.useState<Orientation>(SMART_TODO_WIDGET_DEFAULT_ORIENTATION);
```

The constant is exported (not just an inline literal) specifically so the
smoke-test file can assert the default without a React renderer — this
peer package (`@spaarke/smart-todo-components`) has no Jest config yet
(`build`/`lint` scripts are both `tsc --noEmit`; confirmed via
`package.json` + absence of a `jest.config.*` in this package, unlike 10
sibling shared-lib packages that do have one).

The stale "UAT 2026-06-20: WIDGET default is 'vertical' ... stacked rows"
comment block was rewritten to state the new default, cite U-2 / task 024 /
2026-08-15, and document the confirmed `KanbanBoard` CSS mapping inline so a
future reader doesn't have to re-derive it.

Scope discipline: the Code Page's own orientation default (already
`'horizontal'` via `useUserPreferences`) was **not** touched — grep confirmed
`SmartTodoWidget.tsx` has exactly one `orientation` state declaration and one
consumer (`<OrientationToggle>` + the `<SmartTodoKanban orientation={...}>`
prop pass-through at line ~1166); no other default-override path exists in
this file.

## Narrow-pane escalation check (POML `<escalation>` trigger) — NOT fired

The POML's escalation trigger was: does the new `'horizontal'` default cause
visible column overflow/clipping in narrow workspace panes that the CSS-only
swap doesn't already handle? Reviewed `KanbanBoard.tsx`'s `board` style
comments (2026-06-19 / UAT 2026-06-20 round 4 history): the board already
handles narrow-pane overflow via `overflowX: 'auto'` (horizontal scroll) —
this was an explicit prior fix ("replaced `overflow: hidden` with
`overflowX: auto`... Now: columns still shrink to fit when possible; when
they can't, the user can scroll horizontally to reveal them"). The original
widget-local `'vertical'` default was a **UX preference** for the widget's
typical narrow-pane context, not a workaround for a broken/clipping
horizontal layout — the horizontal layout was already safe. No escalation
required; the orientation toggle remains available in the toolbar for users
who still prefer the stacked view on very narrow panes.

## Drag-drop + selection preservation (NFR-03)

Unchanged by this task — the CSS-only orientation-flip mechanism itself
(`KanbanBoard`'s `mergeClasses` class-swap with no React tree re-creation)
was not touched, only the widget's **initial** state value. The flip
mechanism's existing regression coverage
(`Spaarke.UI.Components/.../Kanban/__tests__/KanbanBoard.test.tsx`, "keeps
the same column structure across orientation flips (no DOM re-creation
contract)") continues to cover it. A widget-level render test exercising a
live drag interaction + flip (per the POML's UI-test #2) requires Jest +
jsdom + `@hello-pangea/dnd` test setup, which this package doesn't have yet
(tracked as smart-todo-r5 task 040) — documented as a known gap in the test
file's header comment rather than faked with a non-executing test.

## Test coverage added

`SmartTodoWidget.test.tsx` (pure-value smoke test, matching the file's
existing Jest-less `assert()`-based pattern, gated behind
`SMART_TODO_WIDGET_SMOKE=1`):

```ts
export function runOrientationDefaultSmokeTest(): void {
  assert(
    SMART_TODO_WIDGET_DEFAULT_ORIENTATION === 'horizontal',
    "SmartTodoWidget's default orientation must be 'horizontal' ..."
  );
}
```

Wired into the same module-eval gate as the existing query-builder smoke
tests. This satisfies the "fresh mount defaults to side-by-side" acceptance
criterion at the value level (the single source of truth the widget's
`useState` seeds from); the render-level assertion (mount + inspect
`data-orientation` on the rendered `KanbanBoard` region) is deferred to task
040 per the honesty note above.

## Verification

- `cd src/client/shared/Spaarke.SmartTodo.Components && npx tsc --noEmit` →
  **exit 0**, zero errors.
- No Jest runner exists in this package (confirmed: `package.json`
  `build`/`lint` are both `tsc --noEmit`; no `jest.config.*` file present,
  unlike `Spaarke.UI.Components`, `Spaarke.AI.Widgets`, etc.) — so the new
  `runOrientationDefaultSmokeTest()` was verified by type-check + manual
  code inspection only, not executed. This matches the pre-existing state of
  every other test in this file (none of them run today either).
- Code review + ADR check (task-execute Step 9.5, FULL rigor): 0 Critical,
  0 Warning, 0 Suggestion. ADR-021 (Fluent v9 tokens, no hex literals),
  ADR-012 (no relative reach-in; barrel import untouched) both compliant.
- Scoring formula (`todoScoring.ts`) not touched — out of scope, not
  referenced by this change.
- Only the 2 concurrency-scoped files were modified; nothing committed.
