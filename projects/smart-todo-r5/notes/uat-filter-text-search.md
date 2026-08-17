# UAT decision — replace structured Filter pane with expanding text search

**Date**: 2026-08-17
**Trigger**: Operator UAT decision (2026-08-17), delivered as a direct implementation
brief (not a `task-XXX.poml`).
**Scope**: `src/solutions/SmartTodo/**` only.

## Decision

REPLACE the SmartTodo Code Page's structured Filter pane (task 021, FR-06 / F-3 —
Priority / Status / Due-date / Assigned-To categories, built via a Fluent
`Accordion` + `TagFilter` + Xrm.WebApi contact typeahead) with a single expanding
free-text search. Clicking the Header's "Filter" pill (task 020, unchanged —
`isFilterPaneOpen` / `onToggleFilterPane`) now expands a `SearchBox` instead of the
structured pane. Typing filters the Kanban client-side by case-insensitive substring
match against the To Do's **name**, **description**, and **assigned-to** display
name (plus the pre-existing regarding-record name/number match from DEF-11 Part 3,
preserved unchanged).

The operator explicitly chose "replace" over "add search alongside the structured
pane" or "keep Completed toggle, drop the rest."

## What changed

| Area | Before (task 021) | After (this change) |
|---|---|---|
| UI component | `components/FilterPane/` (Accordion: Priority/Status/Due/Assigned-To) | `components/SearchFilter/` (single `SearchBox`) |
| Predicate location | Server-side (`buildTodoItemsQuery` filter clauses) | Client-side (`SmartToDo.tsx` `displayItems` memo, via `utils/todoSearchUtils.ts::matchesTodoSearchQuery`) |
| Match fields | Priority (choice), Status (multi-select), Due-date (bucket), Assigned-To (contact typeahead) | name, description, regarding-record name/number, assigned-to display name (free text) |
| State shape | `ITodoFilterState` (4 fields) lifted to `SmartTodoApp.tsx` | `searchQuery: string` lifted to `SmartTodoApp.tsx` (was already declared as a dead `const ""` since task 020 removed its old producer) |
| Toggle wiring | `isFilterPaneOpen` / `onToggleFilterPane` (task 020, Header) | UNCHANGED — same props, same Header, same pill |

## FR-07 regression (accepted, not a bug)

Removing the structured Status filter also removes the **"Show Completed" toggle**
(FR-07 / task 022), which lived exclusively in the Filter pane's Status checkboxes
(`Completed` option). There is no other UI surface offering this toggle in the
SmartTodo Code Page after this change — completed to-dos are simply not shown in
the Kanban (matches the pre-task-021 default: `statuscode` in {Open, In Progress}).

The operator was told this explicitly and chose "replace" anyway. This is flagged
here for operator sign-off, not fixed or worked around. `buildTodoItemsQuery`'s
`includeCompleted` parameter is left in place (unused by any live caller, same as
before task 021's `filterState` was added on top of it) — a future task can wire a
new producer for it without any query-layer change.

## Assigned-to search — gap check (closed, no query change needed)

The task brief asked me to verify whether the assigned-to display name was already
fetched for the Kanban card data, since the search needs to match it. It was:
`DataverseService.mapTodoFormattedValues` (in `services/DataverseService.ts`) already
maps `_sprk_assignedto_value@OData.Community.Display.V1.FormattedValue` onto
`ITodo.assignedToName` for every `getActiveTodos` call — this predates this task
(added for the KanbanCard's assignee display) and required no `$select`/`$expand`
change. The only change needed was extending the client-side match predicate to
also test `assignedToName`.

While verifying this, found and fixed a **pre-existing type gap**: `ITodo` (in
`types/entities.ts`) was missing `sprk_regardingrecordname` / `sprk_regardingrecordnumber`
even though `SmartToDo.tsx`'s search predicate already read them off `ITodo` items at
runtime (DEF-11 Part 3, 2026-07-04) — a latent `tsc` error (`TS2339`) that happened to
not block anything because nobody had run a clean `tsc --noEmit` on just this
package since. Added both fields to `ITodo` (they ARE always selected via
`queryHelpers.ts TODO_SELECT_FIELDS`). This fixed 2 pre-existing errors and avoided
introducing 2 new ones in the extracted `todoSearchUtils.ts`.

## Files changed

- **Removed**: `components/FilterPane/` (all 4 files — component, styles, barrel, test)
- **Added**: `components/SearchFilter/` (component, styles, barrel, test)
- **Added**: `utils/todoSearchUtils.ts` (+ test) — extracted the search predicate out
  of `SmartToDo.tsx` into a pure, directly-unit-testable function, since
  `SmartToDo.tsx` itself has no existing render-test harness to exercise the
  predicate through.
- **Modified**: `SmartTodoApp.tsx` — `searchQuery` is now real `useState` (was a
  dead `const ""`); `filterState`/`ITodoFilterState`/`DEFAULT_TODO_FILTER` removed;
  `<FilterPane>` → `<SearchFilter>`.
- **Modified**: `components/SmartToDo.tsx` — dropped the `filter` prop; extended
  `displayItems`'s search predicate via `matchesTodoSearchQuery`.
- **Modified**: `hooks/useTodoItems.ts` — dropped the `filter` option and its
  primitive-key destructuring/`filterRef` plumbing.
- **Modified**: `services/DataverseService.ts` — `getActiveTodos` dropped its
  `filter` parameter.
- **Modified**: `services/queryHelpers.ts` — removed `ITodoFilterState`,
  `DEFAULT_TODO_FILTER`, `TodoStatusFilterValue`, `TODO_STATUS_FILTER_STATUSCODE`,
  `TodoDueDateCategory`, `TODO_PRIORITY_CHOICE_VALUES`, `buildDueDateRangeClause`;
  `buildTodoItemsQuery` reverted to its pre-task-021 3-parameter shape
  (`contactId`, `regardingFilter?`, `includeCompleted?`).
- **Modified**: `types/entities.ts` — added the two `ITodo` fields (see above).
- **Replaced**: `services/__tests__/queryHelpers.test.ts` — the task-021 version
  tested ONLY the removed `filterState` branch; rewritten to cover
  `buildTodoItemsQuery`'s reverted default/regardingFilter/includeCompleted shape.
- **Untouched** (verified, out of scope): `components/Header/Header.tsx` (the Filter
  pill + `isFilterPaneOpen`/`onToggleFilterPane` contract is unchanged — this task
  only swapped what's mounted underneath it) and the LegalWorkspace solution's
  parallel `DataverseService.ts`/`useTodoItems.ts` (separate duplicate files, not
  imported from SmartTodo — confirmed via repo-wide grep before editing).

## Verification

- `npx tsc --noEmit` (in `src/solutions/SmartTodo`): 40 errors on baseline (git
  stash) → 38 after this change. Zero NEW errors; 2 pre-existing errors fixed
  (the `ITodo` regarding-record type gap above). All remaining 38 are pre-existing
  and unrelated (cross-package `@azure/msal-browser` / `ComponentFramework` /
  `DOMPurify` type-resolution gaps in `Spaarke.Auth`/`Spaarke.UI.Components`, plus
  one pre-existing `IWebApi` structural mismatch at `SmartToDo.tsx:424`).
- `npx jest` (in `src/solutions/SmartTodo`): 9 suites / 114 tests, all passing.
- hex/rgb/`'1px'` grep across every changed file: zero matches introduced by this
  diff (one pre-existing `shorthands.borderWidth("1px")` remains in `SmartToDo.tsx`
  at an untouched line, unrelated to this change).

---

## UAT pass 2 (2026-08-17) — relocate the search box inline, left of Filter; drop the label

**Trigger**: Operator UAT feedback on the pass-1 layout above (still delivered as a
direct implementation brief, not a `task-XXX.poml`). Pass 1 put the expanding
`SearchBox` on its own bordered row underneath the top bar, with a "Search" caption
and placeholder "Search by name, description, or assignee…". The operator's
follow-up ask, in full:

1. The search field must expand **INLINE, to the LEFT of the "Filter" button, on
   the SAME ROW** as Filter + "+ New Task" — not on a separate row below.
2. Remove the "Search" label entirely.
3. Change the placeholder to exactly: `Filter by name, description, assigned to...`

### What changed (pass 2, on top of pass 1)

| Area | Pass 1 | Pass 2 (this change) |
|---|---|---|
| Mount point | `SmartTodoApp.tsx`, rendered as a full-width bar BELOW `<Header>` | `Header.tsx`'s toolbar `rightGroup`, rendered as a flex sibling immediately BEFORE the Filter `<Button>`, in both the default cluster and the `hasSelection` (`<SelectionAwareToolbar>`) branch |
| Caption | `<Text>Search</Text>` to the left of the box | Removed entirely — no text node, placeholder-only affordance |
| Placeholder | `Search by name, description, or assignee…` | `Filter by name, description, assigned to...` (exact wording, ASCII ellipsis — not the unicode `…` pass 1 used) |
| `aria-label` | `Search to-do items by name, description, or assignee` | `Filter to-do items by name, description, assigned to` (kept in sync with the new placeholder wording) |
| Box sizing / chrome | Own row: `colorNeutralBackground2` bg + bottom border + bar padding, `maxWidth: 480px` | No row chrome (it's an inline toolbar sibling now); fixed `width: 240px` so it doesn't crowd Filter / + New Task / ⋮ on narrower viewports |
| `isOpen`/`value`/`onChange` contract | Owned by `SmartTodoApp.tsx`, passed straight to `<SearchFilter>` | UNCHANGED contract on `<SearchFilter>` itself — `SmartTodoApp.tsx` now threads `searchQuery`/`setSearchQuery` through two NEW required `Header` props (`searchQuery`, `onSearchQueryChange`) instead of rendering `<SearchFilter>` directly |
| Stay-mounted / `display:none` collapse | Yes (NFR-03 — text survives close/reopen; also keeps the collapsed box out of the tab order) | UNCHANGED — kept the same mechanism deliberately. Considered animating the collapse via `width` transition for a smoother "expand" but `display:none` was kept instead: animating `width` while keeping the box interactive when "closed" would require `visibility`/`pointer-events` tricks to preserve the NFR-03 tab-order guarantee, and the operator brief said smooth is "nice but not required" — not worth the a11y risk for this pass. |

### Files touched (pass 2)

- `components/SearchFilter/SearchFilter.tsx` — dropped the `Text` "Search" caption;
  changed placeholder + `aria-label` wording; updated module doc.
- `components/SearchFilter/SearchFilter.styles.ts` — dropped the bordered-bar
  `rootOpen` chrome (background/border/padding) and the now-unused `label` style;
  `searchBox` width changed from `maxWidth: 480px / width: 100%` (full-bleed row) to
  a fixed `240px` (inline toolbar sibling).
- `components/Header/Header.tsx` — **now imports and renders `<SearchFilter>`**
  (previously explicitly "untouched" per the pass-1 note above — that note is
  superseded by this pass). Added two new REQUIRED props, `searchQuery: string` and
  `onSearchQueryChange: (next: string) => void`; renders `<SearchFilter isOpen=
  {isFilterPaneOpen} value={searchQuery} onChange={onSearchQueryChange} />`
  immediately before the Filter `<Button>` in BOTH the default cluster and the
  `hasSelection` branch (so Filter's search-trigger role is available and
  positioned identically regardless of selection state). `isFilterPaneOpen` /
  `onToggleFilterPane` themselves are UNCHANGED — no new state was invented, only
  new pass-through props for the value/onChange the Header now needs to place its
  child.
- `SmartTodoApp.tsx` — removed the standalone `<SearchFilter isOpen=… value=…
  onChange=…/>` block that previously rendered below `<Header>`; removed the now-
  unused `SearchFilter` import; passes `searchQuery`/`onSearchQueryChange` to
  `<Header>` instead. `searchQuery` state (`useState`) and `isFilterPaneOpen` state
  are UNCHANGED — still owned here, still lifted the same way.
- `components/SearchFilter/__tests__/SearchFilter.test.tsx` — added 2 tests (no
  "Search" text node anywhere in the render; placeholder matches the exact new
  wording). Pre-existing tests (`aria-hidden` visibility, controlled value, typing,
  NFR-03 toggle-never-fires-onChange) needed no changes — the component's public
  contract (`isOpen`/`value`/`onChange`) did not change, only its internal JSX.
- `components/Header/__tests__/Header.test.tsx` — added a new `describe` block (7
  tests) covering: `<SearchFilter>` precedes the Filter button in DOM order (both
  branches), `aria-hidden` reflects `isFilterPaneOpen`, `searchQuery` reflects into
  the input's value, placeholder wording + no "Search" label, typing invokes
  `onSearchQueryChange`. Updated the pre-existing
  `render_DefaultNoSelection_ShowsFilterNewTaskOverflowInOrderOnly` test: its old
  assertion (`container.querySelector('input')` must be `null` — i.e., "no inline
  SearchBox exists at all", written against task 020's REMOVED toggle pattern) no
  longer holds now that a real, intentional `<SearchFilter>` is always mounted;
  replaced with an assertion that the mounted box is `aria-hidden="true"` in the
  default (`isFilterPaneOpen=false`) state. Also added `searchQuery` /
  `onSearchQueryChange` to the test harness's default props (both new required
  `HeaderProps` fields).

### Verification (pass 2)

- `npx tsc --noEmit` (in `src/solutions/SmartTodo`): 38 errors on baseline (`git
  stash` of this pass's 6 changed files) → 38 after. **Zero new errors** (diff of
  the two error listings is empty — same 38 pre-existing, cross-package errors as
  pass 1 left: `@azure/msal-browser` / `ComponentFramework` / `DOMPurify`
  type-resolution gaps in `Spaarke.Auth`/`Spaarke.UI.Components`, plus the one
  pre-existing `IWebApi` structural mismatch at `SmartToDo.tsx:424`).
- `npx jest` (in `src/solutions/SmartTodo`, full suite): **9 suites / 130 tests, all
  passing**. Baseline (via `git stash` of this pass's 6 changed files, same suite):
  9 suites / 121 tests. Net **+9 tests** = the 2 new `SearchFilter` assertions
  (no-label check, exact placeholder) + the 7 new `Header` assertions (the new
  `describe('Header — inline SearchFilter …')` block) — no regressions in any
  other suite.
- hex/rgb/`1px` grep across all 6 changed files (`SmartTodoApp.tsx`, `Header.tsx`,
  `Header.styles.ts`, `Header.test.tsx`, `SearchFilter.tsx`,
  `SearchFilter.styles.ts`, `SearchFilter.test.tsx`): **zero matches** — every style
  value is a Fluent v9 semantic token or a `shorthands.*(…tokens…)` call.
- Manual DOM-order check confirmed (`compareDocumentPosition`): `<SearchFilter>`'s
  `data-testid="search-filter"` element always precedes the Filter `<Button>` in
  `rightGroup`'s children, in both the default and `hasSelection` branches.

### Code-review / ADR-check self-note

- **ADR-021 (Fluent v9 tokens only)**: compliant — no hex/rgb/inline-px introduced;
  `SearchFilter.styles.ts`'s new fixed `240px` box width is a dimension, not a
  color/border, consistent with the pre-existing `maxWidth: '480px'` precedent it
  replaces (dimensions in `px` are not the ADR-021 hex/rgb/color-literal ban).
- **§11 Component Justification**: no new component/service/abstraction was
  introduced — this pass relocates an existing component's mount point and edits
  its existing props' owner (`Header` gained 2 pass-through props); `<SearchFilter>`
  itself, its `isOpen`/`value`/`onChange` contract, and the underlying
  `isFilterPaneOpen`/`onToggleFilterPane`/`searchQuery` state all pre-date this
  pass unchanged.
- **Scope boundary**: touched only `src/solutions/SmartTodo/**` (`SmartTodoApp.tsx`,
  `components/Header/{Header.tsx,__tests__/Header.test.tsx}`,
  `components/SearchFilter/{SearchFilter.tsx,SearchFilter.styles.ts,
  __tests__/SearchFilter.test.tsx}`) — no `.claude/`, RegardingResolver PCF,
  `smart-todo-components`, or `spaarke_insights` files touched.
- **A11y**: Filter pill keeps `aria-expanded={isFilterPaneOpen}` (disclosure
  semantics, unchanged); the inline box keeps `aria-hidden={!isOpen}` +
  `display:none`-when-closed, so the collapsed box stays out of both the
  accessibility tree and the tab order — no regression from the position change.
- **No git commit made** and neither `TASK-INDEX.md` nor `current-task.md` was
  touched, per this pass's explicit boundary (this is a direct UAT brief, not a
  `task-XXX.poml`).
