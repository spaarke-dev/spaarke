# Task 059 - Worklist widget assembly: notes

Branch `task/ontology-059-worklist-widget` (worktree `C:\wts-059`, off `origin/docs/ontology-platform-design` 7fc51a796). Rigor FULL
(frontend `.tsx`, new files), Sonnet 5.5 @ high, directional steps. Dependencies 038, 051, 052, 054, 058 (and 152's per-person Do-lane
suppression) are merged.

## Step 0 - B.6 pre-start check
HANDOFF section 1.2 (all), section 1.4 L115 and L121-124, section 1.1 L51-53 read against the shipped parts. Matches: two lanes with
Decide always above Do; one MatterCard per core record of any type, no per-type code (D-34/D-36); the reader's Not filed items only in the Do
lane (D-35); count filters are lenses (task 052's helpers over `MetricCardRow layout="wide"`), held in component state and gone on remount;
"Showing n of m - Show all"; "+N other open item(s) on this matter, not in this filter"; the narrative hidden while any filter is on; the
aggregate email row at the top of the Do lane, outside both counts, hidden under a Do filter; Closed today / Suppressed (until date) /
Closed earlier disclosures; "Start review" (Decide) and "Work through these" (Do) walking the visible, filtered list. Nothing in v4 conflicts
with the solution. Gaps where the 038 route does not return what v4 shows are listed below.

## Design (what was built)
Shared `@spaarke/ui-components`, `src/components/Worklist/` (no Xrm, ADR-012; the fetch, lookup, aggregate row and narrative are injected):
- `WorklistWidget` (barrel export, with `MatterCard` the only two runtime component exports), plus parts it owns and does not export:
  `WorklistLanePanel`, `WorklistDisclosures`.
- `worklistService.ts` - `createWorklistService({fetch, baseUrl})` reads `GET /api/v1/signals/worklist?set=lanes|closedToday|suppressed|closedEarlier`
  and `parseWorklistResponse` (defensive: an unknown shape is an empty lane, never a made-up row). A thrown `ApiError` and a returned non-2xx
  both become one `WorklistRequestError` carrying the status and the route's problem detail. The route response is the ONLY data source (D-15).
- `worklistView.ts` - the order authority is the route: groups, items and the browse set keep the route's order; a filter only hides, and each
  group counts what it hides. No sort, reverse or regroup anywhere (a source scan asserts it).
- `worklistWizardItem.ts` - builds the wizard's item from the rows already held (the wizard never re-reads), and the closed / suppressed text.
- `useWorklistData.ts` - lanes and closedToday on mount; suppressed and closedEarlier on first open; last good data is kept while a refresh
  fails; stale answers are ignored; a recorded decision reloads the held sets once the wizard closes.
- `launchDecisionWizard` (task 058) is the default launcher; `DecisionWizardHost` is already mounted in `ThreePaneShell`, so
  `src/solutions/SpaarkeAi` needed no change for the wizard (FR-28).
- AI.Widgets host: `Spaarke.AI.Widgets/src/widgets/workspace/WorklistWorkspaceWidget.tsx` (route client over the session fetch, wizard lookup,
  task 054's `ReconciliationAggregateCard` whose "Open Email Review" dispatches `widget_load` for `communications-reconciliation`),
  `worklistLookup.ts` (Person -> contacts, `sprk_budget` of the item's matter or project, `sprk_organization`; any other entity returns no
  candidates), and a `safeRegisterWidget('worklist', ...)` entry (single instance, `defaultOrder` 247, `contextType` 'dashboard', explicit
  Assistant-contract opt-out).

Changes to shipped parts, each needed so a closed item the route returns renders honestly:
- `WorklistItem.timingText` (types, `composeIssueLine`): replaces age / due state on a closed or suppressed line ("Decided", "Not raised until 28 Oct").
- `DecisionResolution.note` and `DecisionEvidenceRef.withheld` (`decisionWizardTypes.ts`); `DecisionItemWizard` shows the note in the status
  bar for a left-open resolution; `FoundStep` passes `withheld`; `EvidenceLine` keeps the stored tier and reads "Details withheld (restricted
  record)" instead of Missing.
- `hasRecordedDecision` now also requires `resolution.steps != null`: a decided item whose Decision Record the route does not return is opened
  read-only with no replay (otherwise every action showed "Skipped").
- The two existing source-scan guards (barrel export count, `laneCountFilters` file list) were widened for the new files, with comments.

## Gaps, deviations and escalation
1. **ESCALATION - D-111 disposition picker NOT built.** The POML constraint (added 2026-10-09, D-111, task 071) needs the inquiry To Do's
   own record id (to recompute the version-5 id and to read `sprk_notes`) and then Dataverse reads of the matter's open outbound service
   requests and the inquiry's incoming replies. The 038 route returns neither `subjectRecordId` nor `subjectRecordType` on an item, and the
   brief forbids reading Dataverse from the widget or extending the BFF in this task. Needed from the route: per item `subjectRecordId`
   (guid) and `subjectRecordType` (logical name), and for this one To Do the `sprk_notes` first line or a reply list. Everything else in the
   task was built. A follow-up (route fields + picker) is filed as an issue.
2. The route does not return the rule's sentence, so the wizard's sentence is null and the wizard shows its own Missing statement. No sentence is invented (D-23).
3. The route does not return the Decision Record for closed items (task 045's read). Closed items open read-only, with no replayed steps;
   the left pane says how the item closed. Needs the 045 record read joined to the closed sets.
4. HANDOFF puts Not filed last in the Do lane; the route returns it where its rank puts it. The widget renders the route's order and never
   moves a group (the route is the order authority); the fixture proves it with a deliberately unranked Do lane. Server-side ordering is the fix.
5. The Briefing narrative slot is built and tested (hidden under any filter, tag text "Written by the Assistant ..."), but the AI.Widgets host
   passes none: the narrative's own contract is task 062's.
6. A suppressed item has no `resolution` in the route's shape, so the wizard is given a left-open resolution with the note
   "Not raised again until <date>". A closed item with no resolution type reads "Closed - how it closed was not recorded".

## Known limits
- No browser, deployed environment or screenshots: every UI check here is jest (jsdom) only; dark-theme checks are renders under the dark
  theme plus a no-colour-literal scan. No visual eyeball was done.
- Pre-existing, not caused by this task: `DecisionWizard.test.tsx` "Escape and the backdrop never close ..." fails (the re-opened nested
  Discard dialog) only when the whole `src/components/DecisionWizard` folder runs; it passes alone and fails identically on the untouched
  task 058 worktree (058's K1 jsdom / tabster limit). Filed as an issue.
- `tsc --noEmit` in UI.Components shows 9 errors, none in a Worklist file (unresolved `@spaarke/auth`, `@spaarke/sdap-client`, and `unknown` catches in
  `FileUploadService.ts`). AI.Widgets shows 31 errors, all unresolved sibling packages. Correction: this note first said none were in a touched file,
  but `register-workspace-widgets.ts` (a file task 059 touched) has 7 pre-existing unresolved `@spaarke/ai-outputs/output-widgets/*` errors (lines 232-397);
  none of them is on a line this task added.

## Tests
- UI.Components jest (Worklist, ConsoleKit, DecisionWizard, WorkspaceShell): 343 tests, 342 pass, 1 fail (the pre-existing folder-run flake above; it passes alone).
  Worklist + ConsoleKit alone: 168 pass. New AI.Widgets tests: 10 pass (host, lookup); `register-workspace-widgets.test.ts` with the new
  `worklist` describe: 69 pass (79 with the host and lookup suites).
- `tsc --noEmit`: UI.Components 9 errors, identical to before (unresolved `@spaarke/auth`, `@spaarke/sdap-client`); AI.Widgets 31, all unresolved sibling
  packages, none in a file this task touched. `vite build` of `src/solutions/SpaarkeAi` succeeds (index.html 6,165 kB, gzip 1,720 kB); no baseline build
  was taken, so no size delta is claimed.
- Mutation proofs (each change failed named tests, then was reverted): sort the visible ids (6 failed), narrative shown under a filter (2),
  aggregate row shown under a Do filter (1), start button ignoring the filter (1), no reload after recording (3), replay guard dropped (2), withheld
  ignored (2).
- Fix-the-class grep: `EvidenceLine` has one real consumer (`FoundStep`, now passes `withheld`); `hasRecordedDecision` has two callers, both use the guard.
- ADR check (ADR-012, -021, -022, -028, -038): no Xrm, token, colour literal, Fluent v8 or React 18-only API in the shared Worklist sources; the host
  uses the session's authenticated fetch.

## Issues filed
#1958 (D-111 picker blocked on route fields), #1959 (route gaps: sentence, Decision Record, Not filed last), #1960 (narrative wiring, task 062),
#1961 (pre-existing DecisionWizard folder-run flake). Label `ontology-deferred`; DI numbers are for the main session to assign.

## Review fixes (PR #1964 adversarial review)
- Closed today / Suppressed / Closed earlier lines now open their own set's copy: the wizard's item loader is built per launch from the set the
  line was in (lanes for a lane line or a start button) and reads that set only. A stale open copy in the lanes can no longer win. A lane line does not
  open a closed copy either.
- An unrecognised 200 body (not an object with a `lanes` array, or no Decide or no Do entry) now throws `WorklistRequestError('The worklist answer could
  not be read.')`, so the widget shows its error state, not "Nothing to do.". Unreadable items or groups inside a readable lane are still skipped.
- With the email aggregate row shown and the Do lane empty, the lane says "No other items to do.".
- A 403 on the lanes read shows "Your worklist isn't available for your account. Contact your administrator." with no Try again.
- `WorklistRequestError` is now a value export of the Worklist barrel (hosts can use `instanceof`).
- Not in this round (tasks 154 / 155): Not filed ordering (the route's), the inquiry-outcome To Do's mark-complete (server side), the D-111 picker.
