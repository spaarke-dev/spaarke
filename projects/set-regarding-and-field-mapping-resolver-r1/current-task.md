# Current Task

**Project**: set-regarding-and-field-mapping-resolver-r1
**Wave**: 2 (about to start)
**Status**: not-started

## Ready for Wave 2 — Shared library refactor (parallel group A)

4 tasks can execute in parallel (all touch `src/client/shared/Spaarke.UI.Components/` in different files):

| # | Task | Rigor | Effort | Parallel-safe |
|---|---|---|---|---|
| 020 | Extend `PolymorphicResolverService.applyResolverFields()` for 5-field write | FULL | 5h | ✅ |
| 021 | Extract `PolymorphicPicker` Fluent v9 shared component | FULL | 6h | ✅ |
| 022 | Relocate `FieldMappingHandler` to `@spaarke/ui-components` | FULL | 3h | ✅ |
| 023 | Extend `EntityLookupConfig` interface with `regardingRecordNumberField?` | FULL | 1h | ✅ |

## Wave 0-1 completion summary

- ✅ SRFR-001 Wave 0 discovery audit (~1.5h)
- ✅ SRFR-002 Wave 0 data-fix (all 4 workstreams + W1a expansion, ~1.5h)
- ✅ SRFR-010 Wave 1 schema additions (11 entities + D-12/13 findings, ~30min)

## Cumulative divergences surfaced

| # | Finding | Resolution |
|---|---|---|
| D-1 | sprk_fieldmappingprofile schema (lookups, compatibilitymode) | Accepted; spec Appendix A rewritten |
| D-2 | sprk_mapping_type field | Added to Dataverse (underscore convention) |
| D-3 | Per-rule syncmode | Spec updated |
| D-4 | 3 catalog regardingfield typos | Fixed (Project + Budget; Billing Analysis row deleted) |
| D-5 | All catalog rows empty | Populated all 10 (2 intentional-null) |
| D-6 | Contact catalog `sprk_contact` → `contact` | Fixed |
| D-7 | 13 record types | Accepted (then reduced to 12 via D-9) |
| D-8 | 3 sprk_ entities missing target-number fields | Added via W1a |
| D-9 | sprk_billinganalysis table doesn't exist | Catalog row deleted |
| D-10 | sprk_communication uses `sprk_regardingperson` | Deferred to Wave 5 |
| D-11 | MCP underscore naming convention | Documented + spec updated |
| D-12 | Matter didn't have `sprk_regardingrecordnumber` | Added |
| D-13 | contact/account got entity prefix (contact_/account_) | Documented; Wave 2 task 020 must convention-derive per-target field name |
| D-14 | Column MaxLength=MAX not =100 | Documented (functionally OK) |
| D-15 | IsSearchable not explicitly set | Documented (default) |

## Recommendation for Wave 2

Given parallel-safe status, dispatch all 4 tasks concurrently via ONE message with MULTIPLE Skill invocations (per root CLAUDE.md task-execute Step 0.3 parallel mode). Each will be a `task-execute` call.

## Next action

Say `execute wave 2` or `continue` to dispatch parallel Wave 2. Or `execute task 020` to run individually.
