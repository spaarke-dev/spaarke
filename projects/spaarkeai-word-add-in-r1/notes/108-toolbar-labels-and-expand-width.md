# 108 — Toolbar labels by width, Expand right-most, smaller expand (UAT round 10)

## Changes

1. **Expand/Collapse position** (`TaskPaneToolbar.tsx`): always inline, the right-most item of the toolbar (after "⋮"), directly below the host's close button. Task 107's "move Expand into ⋮" level is removed — with icon-only tabs at normal width there is room. The host title row cannot hold an add-in button (no Office.js API); Outlook's icon there is Outlook's own.
2. **Labels by width, both hosts** (`hooks/useToolbarFit.ts`, rewritten): `showLabels` is false below `TOOLBAR_LABELS_MIN_WIDTH` (480 px toolbar width) and true at/above it when the labels fit. Task 107's overflow guard stays as a safety net (labels that would overflow stay hidden and return only once the toolbar is wider than they needed). Icon-only tabs and Send keep tooltip + accessible name.
3. **Expand width** (`taskPaneWidthService.ts`): `TASK_PANE_EXPAND_FACTOR` 3 → 2. Candidates: `top = min(2x default, 50% of the window when known (web) | 35% of the screen otherwise (desktop))`, then one 85% step, then the 500 web cap. Windows 395 → 640 (1920 screen); 1366 screen → 478; web in a 1200 px window → 600, 510, 500.

## Why 3x covered the window
In a desktop WebView2 pane `window.outerWidth` is the pane, so the screen fallback applied and the first request was 3 × 320 = 960. Word did not hold to its documented "50% of the window" limit and granted nearly all of it.

## Tests / gates
- `TaskPaneToolbar.fit.test.tsx` rewritten: the pure rule (below min → icons; at min and fitting → labels; overflow → hidden with the needed width recorded; return only past it) and the toolbar: Word and Outlook icon-only at 395; labels at 640; switching on resize; Expand right-most and still inline when labels overflow.
- `taskPaneExpand.test.ts` updated to the 2x / 35% / 85% candidates (+ a small-screen case).
- Full jest 83 suites / 1,133 tests; lint 0; typecheck 0 production; test-file type debt 68 (= pinned).
