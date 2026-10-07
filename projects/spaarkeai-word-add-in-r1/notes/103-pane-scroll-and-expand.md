# Task 103 - Pane scroll bar hidden + expand/collapse button (UAT round 6, items 5-6)

## Item 5 - scroll bar
`TaskPaneShell.tsx` `content` style (the `<main>`): `scrollbarWidth: 'none'` + `'::-webkit-scrollbar': { display: 'none' }`.
`overflow: auto` is kept, so wheel, touch and keyboard scrolling still work. Only the main container is affected;
Find's inner regions (task 102) are separate elements and keep their own bars.

## Item 6 - expand / collapse
- **Capability**: `isTaskPaneResizeSupported()` in `taskPaneWidthService.ts` = TaskPaneApi 1.1 supported AND
  `extensionLifeCycle.taskpane.setWidth` exists AND the platform default is documented (NFR-10: requirement set, never host type).
  **Deviation**: not added to `HostCapabilities` - the capability depends on a runtime requirement set the adapters do
  not model, and adding a required field breaks ~8 full-`HostCapabilities` test fixtures. Outlook reports TaskPaneApi 1.1
  unsupported, so the button is absent there without any host check.
- **Button**: `TaskPaneToolbar` renders it just left of the "more" menu only when `onToggleExpand` is passed (the shell passes it
  only when supported). Icon `ArrowMaximizeRegular` / `ArrowMinimizeRegular`; accessible name "Expand pane" / "Collapse pane";
  `aria-pressed` = expanded; disabled while an expand is stepping down.
- **Expand** (`expandTaskPane`): candidates widest-first = 3x platform default, 50% / 40% / 30% of `screen.availWidth`,
  and 500 on web; only those wider than the current pane. For each: `setWidth(w)`, wait for a `resize` event or 300 ms,
  accept if `window.innerWidth` changed by more than 4 px. Returns the width that took, or null (state stays collapsed).
- **Collapse** (`collapseTaskPane`): `setWidth(preferredTaskPaneWidth)` (default + 75).
- All paths never throw.

## Widths per platform
| Platform | Default | Collapse (round 4) | Expand first try (3x) |
|---|---|---|---|
| Word on the web | 330 | 405 | 990 -> steps down to 500 (cap) |
| Windows | 320 | 395 | 960 -> 50/40/30% of screen |
| Mac | 270 | 345 | 810 -> 50/40/30% of screen |

## Tests (new suites to add to ci-gated-suites.txt)
- `shared/taskpane/services/__tests__/taskPaneExpand.test.ts`
- `shared/taskpane/components/__tests__/TaskPaneExpand.test.tsx`

## Open (live checks)
- Word desktop + web: pane widens (web to 500) and narrows back; confirm setWidth after user drag behaves.
- On desktop, whether 3x (960) is accepted depends on the Word window (limit 50% of the window); the step-down covers it.
- Expanded state is per-session in React; not persisted, and a manual drag leaves the button state stale until pressed.
