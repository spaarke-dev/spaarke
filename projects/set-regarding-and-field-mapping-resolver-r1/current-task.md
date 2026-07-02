# Current Task

**Project**: set-regarding-and-field-mapping-resolver-r1
**Wave**: 3 (about to start) + Wave 4 (parallel)
**Status**: not-started

## Ready for Wave 3 (RegardingResolver) + Wave 4 (presave) — mixed parallel groups

**Wave 3 group B** — RegardingResolver v1.2.0 → v1.3.0 (all touch `src/client/pcf/RegardingResolver/`):

| # | Task | Rigor | Effort | Parallel-safe |
|---|---|---|---|---|
| 030 | 2-row layout + toolbar-icon + PolymorphicPicker consumption | FULL | 6h | ✅ (group B) |
| 031 | Modal-open on record-number click | FULL | 2h | ✅ (group B) |
| 032 | Populate `pending.recordNumber` for presave bridge | FULL | 2h | ✅ (group B) |
| 033 | Preserve read-only + URL; version bump v1.2→v1.3 | FULL | 2h | after 030-032 |

**Wave 4** — Presave webresource update (independent, runs concurrently with Wave 3):

| # | Task | Rigor | Effort |
|---|---|---|---|
| 040 | Update `sprk_todo_regarding_presave.js` v1.1→v1.2 (add recordNumber) | FULL | 2h |

## Wave 2 completion summary

- ✅ SRFR-020 — `applyResolverFields` 5-field write, +226 LOC service, +561 LOC tests (23/23 pass), ~13min actual
- ✅ SRFR-021 — `PolymorphicPicker` Fluent v9 shared component, +290 LOC + 14 tests, ~12min actual
- ✅ SRFR-022 — `FieldMappingHandler` relocated to shared lib, +563 -547, 54 tests pass, ~14min actual
- ✅ SRFR-023 — `EntityLookupConfig` extended with `regardingRecordNumberField?`, +24 LOC, ~8min actual

**Aggregate**: ~5 min wall-clock (parallel), 91 tests pass, 0 regressions.

## Known issue (pre-existing; deferred)

`src/client/shared/Spaarke.UI.Components/src/services/EntityCreationService.ts` imports `@spaarke/sdap-client` module which is not installed. Blocks full `npm run build` but does NOT affect individual Wave 2 files (tests pass, targeted tsc clean). Must resolve before Wave 8 deploy.

## Recommendation

Wave 3 (group B: 030, 031, 032) can be dispatched in parallel like Wave 2 was. Wave 4 (040) is independent and can also be a 5th parallel agent. Cross-task coordination: 030 (main layout) has small dependencies on 031 (modal handler wired into hyperlink) and 032 (presave global write on selection) — all three touch the RegardingResolver React root, so serialize risk is real. Alternative: run 030 first, then 031+032 in parallel after.

## Next action

Say `continue` to dispatch 4 parallel agents (Wave 3 group B + Wave 4). Or specify individual task.
