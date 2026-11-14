# Task 031 notes: widget Decide lane over the MCP shims

Branch `worktree-agent-a4d87bef36f8d9863`, reset to `work/spaarkeai-mcp-bff-api-r1` @ 727ed0382e before starting. Model: Sonnet 5.5 @ high. Rigor FULL.
No `src/server/**` file touched. No adversarial pass (the main session runs it after merge); ADR check and a self-review were done.

## What shipped

| File | What |
|---|---|
| `src/client/widgets/spaarke-decisions/src/lane/DecideLane.tsx` | The lane: reads, shows `WorklistLanePanel` (deep import) over `buildLaneView`, a "Finding" row for billing items (through the new `renderItem` seam), Start review walker with "n of N", the `onOpenWizard` event for 032, loading / error (Try again; no button on 403) states. |
| `.../lane/decideLaneFetch.ts` | The paged read: follows `spaarke_list_decision_page`'s cursor chain through `createMcpFetch`, builds the Signal read route's body, so the real `createWorklistService` parses it. |
| `.../lane/billingLinkOut.ts` | `buildBillingLinkOut` (Invoice Review URL from `buildInvoiceReviewLaunch(...).pageInput.data` + `consoleBaseUrl`, data encoded once more), `normalizeConsoleBaseUrl`, `consoleBaseUrlOf`. |
| `.../src/App.tsx` | Mounts the lane when connected; builds the service over `useMcpHost().callTool`; passes `openLink` and the base from the host's `spaarke_show_decisions` result. Optional `onOpenWizard` prop for 032. |
| `src/client/shared/Spaarke.UI.Components/src/components/Worklist/{MatterCard,WorklistLanePanel}.tsx` | One optional seam: `renderItem?: (item) => ReactNode` (default undefined = standard `IssueLine`). |
| tests | `DecideLane.test.tsx` (40 incl. 200-item walk), `decideLaneFetch.test.ts` (13), `billingLinkOut.test.ts` (24), `mcpHost.test.tsx` (11, the wave 6 gap), `WorklistRenderItem.test.tsx` (3, shared lib), `App.test.tsx` (2 replaced), `widgetSurface.test.ts` (scans the lane files), `laneFixtures.ts` (helpers). `jest.setup.cjs`: `ResizeObserver` stand-in. |

Step 1 result: `WorklistLanePanel` mounts standalone with `buildLaneView`; the `WorklistWidget` single-lane fallback was NOT needed. Step 2 result: no paging seam in the panel was needed (the whole lane is read before it is shown).

## Acceptance criteria

| Criterion | Result | Evidence |
|---|---|---|
| Header 'Decide', count, subtitle; cards with count + oldest age (click filters); rows grouped by matter with reason and age | Pass, except "done today" (below) | `DecideLane.test.tsx` "renders the Console lane" group |
| Counts equal `spaarke_show_decisions`' per-category counts for the same data | Pass | test restates the BFF partition over the same 70 rows; hand-checked 70 / 2 / 32 / 17 / 14 / 5 |
| Start review walks every non-billing item across pages with "n of N", reaches item 200 in a 200-item mock | Pass | "reaches item 200 of 200": 5 pages, 202 rows (2 billing on page boundaries), label `200 of 200`, last id = row 200 |
| Clicking a non-billing row emits `openWizard(signalId)` | Pass | "raises onOpenWizard with the signal id, its core, and the non-billing visible list" |
| Billing row: 'Finding', link with `invoiceId` (+ `lineId` only when present), lower-case guids, encoded once; no decide action; never opens the wizard | Pass | `billingLinkOut.test.ts`, `DecideLane.test.tsx` "billing Work Items link out only" |
| Negative: billing item with no invoice id renders no link and no decide action | Pass | `{ invoiceId: '{}' }` row (a bare `''` is dropped by the shared parser, so such an item is not billing) and null/empty/blank cases in `billingLinkOut.test.ts` |
| Loading, empty, error states; isError shows the server message | Pass | "loading, empty and error states" |
| Existing shared-lib Worklist, LegalWorkspace, AI.Widgets, DailyBriefing worklist tests pass with no edits | Pass (see Evidence) | no existing test file was edited except the widget's own 030 `App.test.tsx` / `widgetSurface.test.ts` (placeholder replaced; file list extended) |
| No hex / inline colour (grep); keyboard-operable; `dist/index.html` < 3 MB | Pass | grep over `src/lane` finds comments only; rows are native buttons, the Finding link is an `<a href>`, review controls are Fluent buttons; bundle 873,146 bytes |
| Test scope: stated contract plus named edge cases | Pass | extras justified below |

**Not met as written: "done today".** No v1 tool returns the items closed today; the Console reads that set (`set=closedToday`) from the worklist route. The lane passes `doneToday` through when given (test proves "5 of 75 done today" renders) and nothing supplies it, so the All card shows no progress line. Needs an owner decision (see questions).

## Test scope beyond the stated contract (one line each)

- `decideLaneFetch.test.ts`: a failure on any page fails the read, a repeated cursor and a 100+ page chain stop with 502, an unreadable page is 502 not an empty lane: paging is new code on the lane's only read path, and a partial or looping lane would show wrong counts.
- `mcpHost.test.tsx`: the wave 6 review gap (handshake-context merge, `callServerTool({ name, arguments })` shape, rethrow, `openLink`); mutation-checked (merge order, replace-instead-of-merge) with 3 failing.
- `billingLinkOut.test.ts`: base-URL rejections (http, user info, query, `javascript:`): the widget hands the link to the host, so an unusable base must give no link.
- `WorklistRenderItem.test.tsx`: the only shared-lib change is the seam; fails before (2 of 3 failed against HEAD's MatterCard / panel), passes after; its first case is the unchanged-default control.
- `DecideLane.test.tsx` "opening another row during a review moves the label": found in self-review (the label could name a different item than the one in front of the user).

## Evidence

- Widget `npx jest`: 9 suites, 166 pass (baseline before the task: 5 suites, 89 pass, 1 skipped). `npx tsc --noEmit`: clean. `npm run build` (tsc, vite, size gate): `dist/index.html` 873,146 bytes (0.83 MB), 0.10 MB above the 030 bundle (762,809), under the 1 MB aim and the 3 MB gate. No `@spaarke/sdap-client` value import survived bundling, so no Vite alias was needed (wave 6 item d).
- Mutation checks (restored after): dropping `renderItem` wiring and stopping the paged read after page 1 failed 21 of the lane tests.
- Shared lib `npx jest src/components/Worklist`: 12 suites, 288 pass (285 existing + 3 new), including `WorklistInvoiceReview`, `laneCountFilters` and `laneCountFilters.parity`. Shared lib `eslint` on the three files: clean. `tsc --noEmit -p .`: 10 errors, all unresolved `@spaarke/auth` / `@spaarke/sdap-client` imports in files this task did not touch (no link in a fresh worktree); none in `Worklist/`.
- AI.Widgets `npx jest src/widgets/workspace`: 20 suites, 273 pass (`WorklistWorkspaceWidget` included).
- DailyBriefing (needs `--testMatch "**/test/**/*.test.ts" "**/test/**/*.test.tsx"` here: its config's absolute `testMatch` glob does not match under a `.claude/worktrees` root): 23 of 26 suites, 175 tests pass, including `worklistSectionRegistration`, `decisionRecordsSectionRegistration`, `legalWorkspaceSectionRegistry`, `briefingCutoverLayouts`. 3 suites (`DailyBriefingApp.openRecord`, `criticalTodayRetired`, `emailShareDraft`) cannot compile: `TS2307 Cannot find module '@spaarke/ui-components/types/LookupTypes'` in `DailyBriefingApp.tsx` (package link missing in a fresh install); nothing to do with Worklist, and no test ran in them.
- LegalWorkspace has no jest suite for the worklist mount; `worklist.registration.ts` is unedited and `vite build` of the Console bundle succeeds against the changed shared lib (3,984 modules).
- Fail-before for the new widget tests: they target modules this task adds (none can run without them). For the shared-lib seam, the old `MatterCard` / `WorklistLanePanel` were copied back: 2 failed, 1 passed (the default-unchanged control); my versions restored.

## ADR check

- ADR-012: shared components reused by deep path (`.../Worklist/WorklistLanePanel`, not the barrel); no per-project copy; the shared change is an optional prop.
- ADR-021: Fluent v9 semantic tokens only (grep clean); theme from the host context (030's `hostTheme`); no colour stored. The lane's open state is the shared `useLaneOpen` (localStorage in try/catch) as the POML requires; the widget source itself uses no storage (`widgetSurface` guard).
- ADR-038: the POML says tests "live on a KEEP path (tests/...)". That list is the .NET layout; the widget's tests are co-located in `src/__tests__` like 030's, mock only the host boundary (`callServerTool`, `openLink`) and use no `Mock<HttpMessageHandler>`. Flagged for the owner.
- Component justification (CLAUDE.md section 11): new files are `DecideLane.tsx`, `billingLinkOut.ts` (named by the POML) and `decideLaneFetch.ts` (the paged read; existing `createMcpFetch` makes one call per request and was not changed, per the instruction). New shared surface: one optional prop (cost of doing nothing: billing rows would render as openable lines).

## Self-review

- Fixed in scope: the review label could disagree with the row opened mid-review (test added).
- Known limits (K): `CounterBadge` caps the visible heading count at "99+" (as in the Console; the aria-label carries the full count); the lane starts collapsed (D-195: the cards show, rows open on a card click or "Show items"); a lane of more than 100 pages (5,000 items) is refused rather than read; a decision made in 032 needs a lane reload hook (not built, 032's).
- Out-of-scope defects found: none new. Pre-existing and reported: DailyBriefing's jest `testMatch` does not match under a `.claude/worktrees` root path (CI path is unaffected).

## Hand-off to 032

- `DecideLane` raises `onOpenWizard({ signalId, core, visibleList, position, total })`; `App` takes an optional `onOpenWizard`. `visibleList` is the non-billing view across all pages, ready to be `DecisionWizard`'s `itemIds`.
- After a Confirm the lane must be read again: remount `DecideLane` with a new `service`, or add a `reloadKey` prop (simple; not added here).
- The wizard shim's route table goes through `createMcpFetch` as `decideLaneFetch.ts` does; the per-route `result` selector (used here to validate a page) is what projects `{ plan, suppression, row }`.

## Open questions for the owner

1. "Done today": add a tool (or `closedToday` count to `spaarke_show_decisions`) so the All card shows "n of m done today", or drop it from FR-12's wording for v1?
2. Add the core record's type / label to `WorklistCoreSummary` (a few bytes per core) so headings say "Matter · MTR-00001" as the Console does, or accept name and number only?
3. Start the lane expanded in the widget (a `defaultOpen` seam on the lane panel), or keep the Console's collapsed default?
4. ADR-038's "KEEP path" wording is .NET-shaped; confirm co-located widget tests are right for TS work.

rework: none task 031
