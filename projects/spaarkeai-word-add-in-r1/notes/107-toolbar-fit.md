# 107 — Toolbar fits its width (UAT round 9, item 2)

## Defect
The tabs container may shrink (`minWidth: 0`), so in a ~400 px Word pane the labelled tabs (Save · To Do · Find · Email/Send) plus Expand and "⋮" overflowed: Expand rendered on top of the last tab.

## Fix
`hooks/useToolbarFit.ts` measures the tab container's overflow and steps the row down one level per layout pass:
0 everything inline → 1 Expand/Collapse moves into "⋮" (as a menu item) → 2 tabs and Send become icon-only (tooltip + accessible name kept).
It steps back up only once the header is wider than the width the previous level needed when it overflowed (no flicker). Re-measured when the row's items change (`contentKey`) and on a resize (ResizeObserver on the toolbar).

Icons: Expand/Collapse use `PanelRightExpandRegular` / `PanelRightContractRegular` (the right-docked pane growing left) instead of the maximize arrows.

Not possible (recorded again): a button in the host's title row. The icon the owner saw in Outlook's top row is Outlook's own; Outlook has no TaskPaneApi 1.1, so Spaarke's Expand never shows there.

## Tests / gates
- New `TaskPaneToolbar.fit.test.tsx` (added to `ci-gated-suites.txt`): the pure step function (down, cap, hysteresis on the way up) and the toolbar at each width — labelled + inline Expand; narrow → Expand in "⋮" (clicking it calls the handler) and icon-only tabs that keep their names; widened again → back to labelled + inline.
- Test note: opening Fluent's menu leaves a different `ResizeObserver` global in place, so the test installs its own in `beforeEach`.
- Full jest 83 suites / 1,130 tests; lint 0; typecheck 0 production; prettier clean.
- Live check (next UAT): Word at default width shows no overlap; Expand from "⋮" widens the pane and the inline Collapse button returns.
