# Task 051 - the one worklist row component (MatterCard + IssueLine): notes

Branch `task/ontology-051-row-component` (worktree `C:\wts-051`, off origin/master c8a87d8184). Shared `@spaarke/ui-components`,
`src/components/Worklist/`. Rigor FULL (frontend, new .tsx). Gate: 057 merged (#1382, 418914d69 is an ancestor of origin/master),
012 and 007 recorded done; 007 verified in `notes/007-schema-verification.md`.

## Step 0 - B.6 pre-start check
HANDOFF @ ae1cc9f section 1.2 L74-76 (one card per matter, one line per item: headline, rule display name, age or due state; no
buttons, rule codes, clause marks or badges; whole line opens the wizard) and section 1.3 (`MatterCard` + `IssueLine` = "a matter and
its items") read. Data S-1 / S-2 (reconciliation): subject name, lane, policy short name, firstdetected, `sprk_duedate` are all built
(007). The assignee / assigner (S-2) are not shown in a row, so the component does not take them. No prototype code was ported; only the
contract was read. Nothing in v4 conflicts with the solution for this row.

## Design
- `MatterCard` (the one component) takes `core: WorklistCore | null` (null = "Not filed", D-35) and `items: WorklistItem[]` (already in
  rank order). Core label, name and number come from the row data (catalog-driven, D-36); a test scans the sources for `sprk_*` and
  type literals.
- `IssueLine` is MatterCard's own part (a native `<button>`, the whole line). It is deliberately NOT exported from the barrel, and a
  test asserts the barrel exports exactly one component. A repo scan asserts no other MatterCard / IssueLine / WorklistRow file exists.
- Headline = subject name (`sprk_regardingrecordname`), else the Signal's stored `sprk_name`, else "Untitled item" (never blank). It
  never shows a sentence, witness value or rule code (D-23 / FR-48).
- Age (Decide) = calendar days since `sprk_firstdetected`; due state (Do) = calendar days to `sprk_duedate`, viewer-local, through the
  shared `parseDueDate` / `daysBetweenLocalMidnight` / `dueUrgencyForDays` (no second date helper). Text colour by tier follows the
  SmartTodo palette (red / dark orange / yellow / grey), tokens only.
- Open event: `{ itemId, core: {recordType, recordId} | null, visibleList }`. `visibleList` is supplied by the host (a card only
  knows its own items; defaults to the card's items) - task 059 passes the lane's full browse set.
- No React 18-only API (no `useId`): the library is consumed by React 16/17 PCF hosts.

## Beyond the stated contract (one-line justifications)
- `note` prop: HANDOFF section 1.4 L123 requires the card to say "+1 other open item on this matter, not in this filter"; the host
  (052/059) owns the text, the card owns the layout.
- `pastDueWording` on the item ('overdue' | 'late'): v4 binding text reads "5d overdue" for tasks and "3d late" for work assignments;
  the route (050/059) supplies it from the policy work type, so the component has no per-type branch. Default is 'overdue'.
- Missing data statements ("No due date", "Age unknown", "Untitled item"): the Missing-never-blank rule (HANDOFF section 1.4).
- No severity edge and no uncertainty icons: v4 puts evidence tiers in the wizard (W-6); severity drives rank, which is given.

## Deviations from the POML
None of substance. The POML says "tests/ui-tests": the four ui-tests are covered by jest (one card with four lines; line content;
click/Enter/Space; dark theme render and a no-colour-literal scan). A visual dark-mode eyeball was not done (no harness / deployed env).

## For the consumers
- 052 / 054 / 059 host the card; 059 supplies `visibleList` and `onOpenItem`, the filter note, and `pastDueWording`.
- Date Only handling: pass `sprk_duedate` as returned (`YYYY-MM-DD`); do not convert it to a Date first.

## Tests and gates
37 jest tests (Worklist.test.tsx) pass in the default zone, America/Los_Angeles and Pacific/Auckland; statements 100%, branches 88%.
eslint and prettier clean on the new files. `tsc --noEmit` for the package reports 9 errors, none in Worklist (unresolved sibling
packages and `unknown` catch variables in files this task did not touch; see the D-106 list in the PR).
code-review (Step 9.5): no F-class finding. K4 only: `toLocaleDateString` is locale-dependent (display text "due 12 Oct" vs "due Oct 12").
adr-check: ADR-021 (Fluent v9, tokens only, no literals, dark via host provider), ADR-012 (context-agnostic, no Xrm) clean; no BFF touched.

## Bundle size (consumer: SpaarkeAi / the Console, vite build, same machine, barrel line removed = master)
index.html (single-file): 6,079,254 B -> 6,084,321 B = +5,067 B raw (+0.08%), gzip 1,690.82 kB -> 1,692.09 kB (+1.27 kB). No consumer
imports MatterCard yet (059 will), so this is the unused-export cost of the barrel line (module-level `makeStyles`). Well under the 5 MB
threshold. No PCF or other bundle touched.

Re-based per coordinator: 057 (418914d69), 012 (StatusBadge) and 007 (`007-schema-verification.md`) outputs verified present on
origin/docs/ontology-platform-design (4a8a6ffe5); merged clean (no conflicts). Tests re-run after the merge (Worklist + ConsoleKit, 68 pass)
and the SpaarkeAi bundle delta re-measured against that base: unchanged, +5,067 B raw.
