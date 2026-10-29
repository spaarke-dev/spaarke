# Task 169 notes - Worklist lane subtitles and collapsible card list (D-195)

Rigor STANDARD (client-only, .tsx + tests; code-review and adr-check run inline, no sub-agents). Sonnet @ high.

## Changes
- `Worklist/WorklistLanePanel.tsx`: `LANE_COPY.subtitle` (exact D-195 text) in a `Caption1` under the `h2`; section `aria-describedby` the subtitle;
  "Show items" / "Hide items" toggle (chevron, `aria-expanded`, `aria-controls`); the "Showing n of m / Show all" line, aggregate row and card list
  sit in one region that stays mounted and uses the `hidden` attribute (view model and Start review never depend on the DOM). A metric card click opens the lane.
- `Worklist/useLaneOpen.ts` (new): localStorage under `spaarke.worklist.laneOpen` (`{"Decide":bool,"Do":bool}`), default collapsed, every read/write in try/catch,
  write re-reads storage so the other lane's value is kept. Copies the shape of SpaarkeAi `usePaneCollapse` (shared lib cannot import SpaarkeAi).
- Empty-lane message stays outside the region (always visible). The toggle shows only when the lane has items or an aggregate row.
- `MetricCardRow` unchanged; escalation trigger did not fire.

## Defect caught by a test
The region's `display: flex` class overrode the `hidden` attribute (author CSS beats the UA stylesheet), so a collapsed list would have stayed visible in a real browser.
The flex class is now applied only while open; the collapsed test asserts computed `display: none`.

## Edited existing assertions (WorklistWidget.test.tsx)
1. Added a top-level `beforeEach(() => window.localStorage.clear())` and an import of `LANE_OPEN_STORAGE_KEY`: open state is now remembered per device, so tests must not leak it.
2. Test "nothing is remembered ... nothing is written to browser storage" (renamed "the filter is not remembered ... only the lane open state is written to browser storage"):
   `expect(setItem).not.toHaveBeenCalled()` became: every `setItem` call uses `LANE_OPEN_STORAGE_KEY`, and the stored value is `{ Do: true }`.
   Why: the metric-card click in that test now opens the Do lane and stores it, which is the new D-195 behaviour; the point of the test (no filter is remembered) is kept.
No other existing assertion was edited. The `sprk_/Xrm/localStorage` source-scan test is unchanged: storage access lives in `useLaneOpen.ts`, not in a scanned file.

## New tests (WorklistLaneCollapse.test.tsx, 8)
subtitles + describedby; starts collapsed (title/subtitle/count/button/cards visible, computed display none); toggle open/close + aria-controls + stored;
re-mount restore + lanes independent (+ other lane value kept); throwing storage; corrupt JSON; metric card opens own lane only; Start review while collapsed.
Beyond the POML list: "corrupt JSON" (one line: JSON.parse failure is the other try/catch path).
All 8 fail on the old panel; pass on the new.

## Counts
Worklist jest: before 8 suites / 245 pass; after 9 suites / 253 pass (245 + 8 new). Other consumers (AI.Widgets, DailyBriefing, LegalWorkspace) mock the widget; none reaches the panel.
`tsc --noEmit` (Spaarke.UI.Components): 9 errors, all pre-existing in unrelated files (missing `@spaarke/sdap-client` / `@spaarke/auth` builds, `FileUploadService`); none in Worklist. eslint on Worklist: clean.

## Noticed, not fixed
- Existing tests that click an `issue-line` while a lane is still collapsed (e.g. WorklistWidget.test.tsx ~lines 392-395, 425) pass because userEvent ignores `display: none`; they do not prove the line is reachable by a user. Not edited (they do not fail).
- AI.Widgets has no installed node_modules in the worktree, so its jest was not run (it mocks `@spaarke/ui-components`).

## Fix round 1 (review of bc4a478a72)
- F3-1: `WorklistWidget.test.tsx` has an `openedLine(lane, index)` helper (toggle the lane when `aria-expanded="false"`, then `expect(line).toBeVisible()`); all 13 lane-line clicks use it (the 11 reported plus the two that already followed a count card). Edited lines: every `userEvent.click(within(lane(..)).getAllByTestId('issue-line')[n])` became `userEvent.click(openedLine(.., n))`.
- F3-2: `InquiryDispositionPicker.test.tsx` `line()` expands the line's lane (toggle when collapsed) and asserts the line visible; `openPicker` goes through `line()`.
- F3-3: two tests: an empty Decide lane has no toggle and shows `lane-empty-Decide`; an empty Do lane with an aggregate row has a toggle, `agg` hidden until opened, `lane-empty-Do` ("No other items to do.") always visible.
- F4-4: a test that `worklist-aggregate-row` and `lane-showing-Do` sit inside the hidden region (not visible collapsed, visible open).
- F4-5: the source-scan test is retitled to name `useLaneOpen.ts` as the sole storage exemption.
- Proof (region forced `hidden={true}` always, then reverted): 61 tests fail: InquiryDispositionPicker 40 of 44, WorklistWidget 14 of 48, WorklistLaneCollapse 6 of 11, worklistNarrative 1 of 27 (it clicks "Show all" in a lane). Real code: 256 pass in 9 suites (253 + 3 new).
- Known limit (accepted, K): a filtered, collapsed lane hides "Show all" (it sits in the region). The filter is still cleared from the metric card (selecting the selected card returns to All).

rework: fix-round-1 task 169
