# Task 031 — Retention prune-on-write (30-day, history-only) — completion note

No deviations from the POML. Documenting a couple of implementation decisions for future reference:

- **Repository fn name**: `deleteHistoryItemsOlderThan(ownerId, cutoff)` (not `pruneHistoryOlderThan`) — matches the existing `deleteNavItem` naming convention (verb + noun) already established in `navItemRepository.ts`, and mirrors `listHistoryItems`'s owner-scoped filter shape with an added age predicate.
- **Sequential delete, no `$top`/batching**: the prune query has no page-size cap and deletes candidates one at a time via `webApi.deleteRecord` (no `ExecuteMultiple`/batch request). Flagged as a Warning in code-review but accepted as-is: this is a single-user, 30-day prune-on-write table (bounded growth by construction — prune runs on every capture write), so unbounded-volume risk is low. If usage patterns change (e.g., a future bulk-import of history), reconsider paging/batching.
- **Prune only runs after a successful upsert**: the tick's control flow now returns early on an upsert failure (`return` inside the catch) before reaching the prune block, satisfying "only prune when a history write actually occurred, not on every no-op tick."
- **Test fake enhancements**: `navigatorCaptureService.test.ts`'s `buildFakeXrm` helper was extended (not just the new tests) to (a) stamp `_ownerid_value` on every `createRecord`d row (mirrors real Dataverse owner-stamping under host-context calls) and (b) make `retrieveMultipleRecords` honor `_ownerid_value`/`sprk_type`/`sprk_lastvisited lt` filter clauses generically, and (c) implement a real `deleteRecord` that splices the fake store. These changes are additive/backward-compatible — all 6 pre-existing tests in the file still pass unmodified.

Quality gates: code-review and adr-check both ran clean (0 critical/violations). tsc `--noEmit` clean; `npm run build` succeeds; ESLint clean; Jest suite green (10/10, `navigatorCaptureService.test.ts`).
