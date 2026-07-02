# Current Task

**Project**: set-regarding-and-field-mapping-resolver-r1
**Wave**: 5 (about to start — AssociationResolver)
**Status**: not-started

## Ready for Wave 5 — AssociationResolver v1.1.0 → v1.2.0 (parallel group C)

| # | Task | Rigor | Effort | Parallel-safe |
|---|---|---|---|---|
| 050 | Retire `ENTITY_LOOKUP_CONFIGS`; transition getEntityConfig callers | FULL | 3h | ✅ (group C) |
| 051 | `RecordSelectionHandler` → thin adapter delegating to shared `applyResolverFields` | FULL | 4h | ✅ (group C) |
| 052 | AssociationResolver consumes shared `PolymorphicPicker` | FULL | 3h | ✅ (group C) |
| 053 | Import relocated `FieldMappingHandler` + version bump v1.1→v1.2 | FULL | 2h | after 050-052 |

## Waves 0-4 completion summary (12 of 28 tasks)

- **Wave 0** (SRFR-001, 002) — Discovery + data-fix. D-1..D-15 divergences all resolved.
- **Wave 1** (SRFR-010) — `sprk_regardingrecordnumber` added to 11 target entities.
- **Wave 2** (SRFR-020, 021, 022, 023) — Shared lib refactor. 91 tests pass.
- **Wave 3** (SRFR-030, 031, 032, 033) — RegardingResolver v1.3.0. 27 tests pass, build:prod succeeds.
- **Wave 4** (SRFR-040) — Presave webresource v1.2.0.

**Aggregate**: ~2h total wall-clock across 12 tasks (vs 24h+ estimated).

## Known deferred issues

- **@spaarke/sdap-client** missing module — blocks full shared-lib `npm run build` but tests pass. Fix before Wave 8 deploy.
- **React types mismatch** shared-lib React 19 vs PCF React 16 — cast at seam; permanent fix out of scope (idea issue candidate).

## Coordination notes for Wave 5

All 4 Wave 5 tasks modify `src/client/pcf/AssociationResolver/`:
- 050 + 051 both touch `RecordSelectionHandler.ts`
- 052 touches `AssociationResolverApp.tsx`
- 053 touches ControlManifest + imports (after 050-052)

**Staged plan**:
- Phase 1: 050 alone (retire hardcoded list — establishes dynamic-first)
- Phase 2: 051 + 052 in parallel (after 050 lands — 051 refactors handler, 052 swaps picker)
- Phase 3: 053 (version bump + FMH import — after 051+052)

## Next action

Say `continue` to proceed with staged Wave 5 dispatch.
