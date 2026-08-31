# Task 081 — Retention verification (FR-05 / Success Criterion 7) — completion note

## Canonical-reference correction (per dispatch instructions)

The task POML's `relevant-files` cites a canonical reference at
`src/solutions/NavigatorPane/src/services/retentionService.ts`. **This file was
never created by task 031 and does not exist.** The actual prune-on-write
30-day retention logic (task 031, spec FR-05 / OQ-2) lives in
`@spaarke/ui-components`:

- `src/client/shared/Spaarke.UI.Components/src/services/navigator/navItemRepository.ts`
  — `deleteHistoryItemsOlderThan(ownerId, cutoff: Date)`. Builds the OData
  filter `_ownerid_value eq {ownerId} and sprk_type eq 100000000 and
  sprk_lastvisited lt {cutoff.toISOString()}`, retrieves matching
  `sprk_navitemid`s, and deletes each one via `webApi.deleteRecord`.
- `src/client/shared/Spaarke.UI.Components/src/services/navigator/navigatorCaptureService.ts`
  — `startNavigatorCapture`'s poll `tick()` calls `deleteHistoryItemsOlderThan`
  inline, immediately after a SUCCESSFUL history upsert this tick (never on a
  no-op tick, never before a failed upsert). `HISTORY_RETENTION_DAYS = 30`. A
  prune failure is routed through `options.onError` exactly like an upsert
  failure and is non-fatal — it does not stop the poll and is retried on the
  next successful capture write.

This verification test therefore imports and exercises that real code, not an
imagined `retentionService.ts`. No production file under either path was
modified by this task.

## What was built

New file: `src/solutions/NavigatorPane/src/services/__tests__/retention.verify.test.ts`.

An end-to-end-style verification test at the NavigatorPane-consumer /
package-boundary level (imports via `@spaarke/ui-components/services/navigator/...`,
the exact subpath style `RecentTab.tsx`/`PinnedTab.tsx` already use — not a
relative cross-package path), complementing the task-031 unit-level retention
`describe` block already present in
`src/client/shared/Spaarke.UI.Components/src/services/navigator/__tests__/navigatorCaptureService.test.ts`
(which covers the same three scenarios separately, plus a non-fatal-failure
case). This task's test asserts all three scenarios together against ONE
seeded scenario, per Success Criterion 7's combined acceptance shape.

### Harness

Mirrors `navigatorCaptureService.test.ts`'s fake `Xrm.WebApi`: an in-memory
`store` array; `retrieveMultipleRecords` parses the real OData filter clauses
the repository emits (`_ownerid_value eq`, `sprk_type eq`,
`sprk_targetlogicalname eq`/`sprk_targetid eq`, `sprk_lastvisited lt`);
`createRecord` owner-stamps every row to the signed-in user (mirrors real
Dataverse host-context ownership); `deleteRecord` mutates the store directly.
`window.Xrm` is installed so `getXrm()` resolves it via its normal frame-walk
— no module mock of `xrmContext`.

### Driving the capture tick to invoke prune

`jest.useFakeTimers()` + `jest.setSystemTime(NOW)` pin the clock to a fixed
instant (`2026-08-12T12:00:00.000Z`). All seeded `sprk_lastvisited` values are
computed as offsets from that same `NOW` constant — never `Date.now()`/
`new Date()` read at assertion time — so the test's outcome cannot depend on
real wall-clock. `startNavigatorCapture({ onCurrentPageChange, onError })` is
started with the fake page context already set to a fresh, previously-unseen
target (`sprk_matter`); `jest.advanceTimersByTimeAsync(0)` flushes the
loop's immediate first tick, which upserts (creates) a history row for that
fresh target — the SAME branch of `tick()` that then runs the inline prune
(only reached after a successful history write this tick).

### Seed + assertions

Seeded before the capture tick runs:
1. A `History` row (`seed-old-history`) owned by the signed-in user,
   `sprk_lastvisited` = 31 days before `NOW` (past the 30-day cutoff).
2. A `Pin` row (`seed-pin`) owned by the signed-in user, `sprk_lastvisited` =
   365 days before `NOW` (pins never auto-expire regardless of age).
3. A `History` row (`seed-other-user-old-history`) owned by a DIFFERENT user
   (`OTHER_USER_ID`), also 31 days before `NOW`.

Results after the capture tick:

| Assertion | Result |
|---|---|
| Seeded >30-day history row for the signed-in user is gone (`deleteRecord` called with its id; absent from `store`) | PASS |
| Pin row survives (`deleteRecord` never called with its id; still in `store`, `sprk_type` still `Pin`, `sprk_lastvisited` unchanged) | PASS |
| Other user's old history row is untouched (`deleteRecord` never called with its id; still in `store`, owner unchanged, `sprk_lastvisited` unchanged) | PASS |
| No upsert/prune error (`onError` not called) and the fresh-page history write succeeded (`createRecord` called for the new target) | PASS |
| Final store state = exactly the 2 survivors + the 1 freshly-captured row | PASS |

## Build + test evidence

- `npm run build` (tsc) in `src/client/shared/Spaarke.UI.Components` — clean,
  no errors (recompiled `@spaarke/ui-components` dist first, per dispatch
  instructions, so the NavigatorPane Jest `moduleNameMapper`'s
  `@spaarke/ui-components/(.*) -> dist/$1` resolves current code).
- `npx jest` in `src/solutions/NavigatorPane` — **10 test suites, 100 tests,
  all passing** (99 pre-existing + this task's 1 new test in the new
  `retention.verify.test.ts` suite). No regressions.
- `npx tsc --noEmit -p tsconfig.json` in `src/solutions/NavigatorPane` —
  clean, no output/errors.

## Verdict

**PASS.** Retention behaves per FR-05: the signed-in user's history rows
older than 30 days are pruned on the next successful capture write; pins
never auto-expire; the prune is strictly owner-scoped (NFR-03) — no defect
found. No escalation, no defect note filed.

## Deviations

- Canonical-reference correction documented above (the POML's
  `retentionService.ts` path does not exist; the real logic lives in
  `@spaarke/ui-components`'s `navItemRepository.ts`/`navigatorCaptureService.ts`).
  This is a documentation correction, not a scope change — the acceptance
  criteria (old row pruned / pin survives / other-user untouched / green
  build) are all met exactly as specified.
- No production code was modified. `TASK-INDEX.md`/`current-task.md` were
  intentionally left untouched per this task's dispatch instructions (owned
  by the main session).
