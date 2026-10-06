# Task 105 — Post-save button feedback + resizable Profile Summary (UAT round 7 items 3-4)

## Item 3 — button feedback (`SaveFlow.tsx`)
- Copy Link: check icon + "Copied" for 2 s (`BUTTON_FEEDBACK_MS`), polite "Link copied" announcement; clipboard failure -> "Couldn't copy" + assertive "Failed to copy link".
- View Document: `openDocumentRecord` now returns `result.opened`; `handleViewDocument` shows "Opened" (check icon) + polite announce, or "Couldn't open" + assertive announce. Duplicate card's View Existing Document unchanged.
- `savedBarButton` minWidth 8.5rem keeps width stable; timers cleared on unmount / re-click.

## Item 4 — shared handle
- `components/ResizeHandle.tsx` (new): ARIA separator, pointer capture drag, Up/Down/Home/End -> `onKeyAction('decrease'|'increase'|'min'|'max')`; hosts own meaning/clamping/persistence.
- Used by `FindSplitPane` (ratio; behaviour + 7 tests unchanged) and `DocumentProfileSection` (Summary pixel height: default 220 max-height until adjusted, then fixed height, min 80 / max 640, step 24, key `sprk.office-addin.profile.summaryHeight`, try/catch).
- No shared splitter in `@spaarke/ui-components` (see task 102 note) — local component, no alias.

## Tests
New: `SaveFlow.buttonFeedback.test.tsx` (4), `DocumentProfileSection.resize.test.tsx` (6). FindSplitPane/FindResultsList/FindView/SaveFlow*/DocumentProfileSection suites unchanged and green (184 tests, 16 suites).

## Open
Live check after deploy (button confirmations; handle drag in both hosts).
