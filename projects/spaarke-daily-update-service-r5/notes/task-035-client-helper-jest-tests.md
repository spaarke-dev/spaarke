# Task 035 — Jest tests for buildSegments / isEmptyResponse / useInlineTodoCreate / actionToBadge+reasonToLabel

**Status**: Completed. All 4 target helpers now have Jest coverage at the package's KEEP path (`src/client/shared/Spaarke.DailyBriefing.Components/test/`).

## Deviation: export-only production change (per task constraint)

Per the task constraint — "Do NOT modify the production helper files — this task adds tests only. If a helper is untestable as written (e.g. not exported), the minimum change is exporting it; note any such change explicitly" — four previously module-private functions were given a bare `export` keyword. **No behavior changed** (no signature change, no logic change, no new imports/exports added beyond the keyword itself). This mirrors the existing precedent in the same file family: `computeDueDate` in `useInlineTodoCreate.ts` was already exported and directly unit-tested in `useInlineTodoCreate.computeDueDate.test.ts`.

| File | Function | Line (pre-change) |
|---|---|---|
| `src/components/NarrativeCitedText.tsx` | `buildSegments` | 107 |
| `src/hooks/useBriefingRender.ts` | `isEmptyResponse` | 60 |
| `src/components/HighPrioritySection.tsx` | `actionToBadge` | 158 |
| `src/components/HighPrioritySection.tsx` | `reasonToLabel` | 213 |

`useInlineTodoCreate` (the hook itself) was already exported — no change needed; tested via `renderHook`.

## New test files (KEEP path: `test/`, mirrors `*.test.ts`/`*.test.tsx` convention)

- `test/NarrativeCitedText.buildSegments.test.ts` — 7 tests: happy path, empty input, no-mentioned-refs, overlapping/malformed segment (2 refs whose match ranges overlap), missing/unresolvable itemId (empty `entityId`), too-short entity name, case-insensitive match.
- `test/useBriefingRender.isEmptyResponse.test.ts` — 8 tests: fully-empty response, `highPriorityItems` omitted entirely, partial data via `channelNarratives`/`tldr.summary`/`tldr.topAction`/`tldr.keyTakeaways`, whitespace-only tldr text (still empty), `highPriorityItems` present alone (R7 W12 item 9 — keeps widget out of empty state).
- `test/useInlineTodoCreate.test.ts` — 6 tests: primary-contact resolve path (binds `sprk_AssignedTo@odata.bind`), no-record path (lookup succeeds but no contact — fails soft), no-userId-supplied (lookup skipped), primary-contact lookup rejects (fails soft), `createRecord` failure path (`status: 'error'`, `getError` surfaces message), `webApi === null` no-op.
- `test/HighPrioritySection.badges.test.ts` — 15 tests: `actionToBadge` for each of the 4 mapped `action` values (`Overdue`/`DueToday`/`DueSoon`/`Recent`, with and without the optional date field where applicable) plus 3 unknown/undefined/empty fallback cases (→ `null`); `reasonToLabel` for each of the 3 mapped `reason` values (`Both`/`HighPriority`/`Monitor`) plus undefined/unknown fallback cases (→ `''`).

Total: 36 new tests, all passing.

`classifyDueDate` does not exist in the codebase (confirmed via grep) — no test references it, per the R7-note naming-error correction in the task POML.

## Test run

Command: `npm test` (jest) in `src/client/shared/Spaarke.DailyBriefing.Components/`.

- The 4 new test files: **36/36 passing** (isolated run: `npx jest test/NarrativeCitedText.buildSegments.test.ts test/useBriefingRender.isEmptyResponse.test.ts test/useInlineTodoCreate.test.ts test/HighPrioritySection.badges.test.ts`).
- Full package suite: **162 passed / 3 failed / 7 skipped** (172 total). The 3 failures are in `test/legalWorkspaceSectionRegistry.test.ts` (suite fails to run — pre-existing compile issue) and `test/ActivityNotesSection.callbacks.test.tsx` (3 stale menu-item assertions unrelated to this task's surfaces). **Verified pre-existing**: stashed the 3 export-only production edits and re-ran both suites — identical 3 failures / 5 passed with the edits reverted, confirming these failures are NOT caused by this task's changes. Out of scope for task 035 (not one of the 4 target helpers); flagged here rather than silently fixed, per the task's "test ONLY these four surfaces" constraint.

## Acceptance criteria

- [x] Jest tests exist at the KEEP path for buildSegments, isEmptyResponse, useInlineTodoCreate, and actionToBadge + reasonToLabel.
- [x] buildSegments tests cover empty input, malformed/overlapping segment, missing/unresolvable itemId; all pass.
- [x] isEmptyResponse tests cover fully-empty response AND partial-data-present; all pass.
- [x] useInlineTodoCreate tests cover primary-contact resolve path, no-record/missing-itemId path, and failure path; all pass.
- [x] actionToBadge and reasonToLabel tests cover each mapped enum value plus unknown/undefined fallback; all pass.
- [x] No test file references classifyDueDate. The 36 new tests all pass; the package's pre-existing (unrelated) 3 failures were verified via git-stash to predate this task.
- [x] No production helper file modified except adding exports (4 functions, bare `export` keyword only) — documented above.
