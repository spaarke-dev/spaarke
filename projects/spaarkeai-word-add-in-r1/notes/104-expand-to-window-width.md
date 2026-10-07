# Task 104 - Expand sizes from the window, not the screen (UAT round 7, item 1)

## Rule (taskPaneWidthService.ts `expandCandidateWidths`)
- Window width = `window.outerWidth`, used only when it is >= 1.5x the pane's current width (`WINDOW_WIDTH_MIN_RATIO`).
- Then: cap = min(3x platform default, floor(50% of window)); candidates = cap, 40% and 30% of window, and 500 on web
  when <= cap; nothing above cap is ever requested; only widths wider than the pane + 4 px; widest first, step down as in 103.
- Otherwise (outerWidth missing/0/about the pane): the task-103 screen-fraction behaviour, unchanged.
- Collapse, capability gating, never-throws: unchanged.

## Platform evidence
- MDN `Window.outerWidth`: "the width of the outside of the browser window ... including sidebar, window chrome and
  resizing borders". It is a top-level-window property, not frame-relative, so inside the Word-on-the-web task-pane iframe
  it is the browser window. (MDN does not state iframe behaviour explicitly - inference; verify live.)
- Desktop WebView2 task pane: not documented; expected to be about the pane/webview size, hence the 1.5x guard and screen
  fallback. UNVERIFIED - live check needed.
- 50% of window: Office desktop limit as documented in Office.TaskPane docs (cited in the 095 note); used as the web cap too.
- Observed live (103 round): web accepted widths above its documented 500 cap, so a window-relative cap is needed.

## Tests (`taskPaneExpand.test.ts`, 27 tests across the width suites pass)
Web 1200 px window / 2560 px screen -> [600, 500, 480, 360], max 600; cap = min(3x, 50%); desktop outerWidth ~pane -> screen
fallback; window wins over screen; `expandTaskPane` end-to-end requests only 600. Existing 103 tests pin `outerWidth` 0.

## Open
- Live: web in a half-width window expands to ~half the window; desktop within the Word window; what `outerWidth` reports
  in WebView2.
- Pre-existing tsc errors in `DocumentProfileSection.tsx` / `SaveFlow.tsx` belong to task 105 work in flight.
