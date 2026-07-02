# Current Task

**Project**: set-regarding-and-field-mapping-resolver-r1
**Task ID**: SRFR-002 (not-started)
**Task File**: [`tasks/002-populate-recordtype-metadata.poml`](./tasks/002-populate-recordtype-metadata.poml)
**Wave**: 0
**Status**: BLOCKED — pending owner decisions on Wave 0 divergence findings

## 🚨 Wave 0 escalation required before SRFR-002 begins

`notes/wave-0-discovery.md` surfaced **7 spec/reality divergences** (D-1..D-7). Task 002's scope needs owner input on:

- **D-5**: `sprk_regardingrecordnumberfield` is EMPTY for ALL rows including Matter (owner's 2026-07-02 claim was incorrect). Task 002 scope → populate ALL 12+ rows, not just 10.
- **D-6**: Contact catalog value `sprk_contact` doesn't exist as a table. Fix to OOB `contact`, or plan a custom entity?
- **D-7**: 13 catalog rows including Billing Analysis (`sprk_billinganalysis`). In scope for task 002 population + task 010 schema add?
- **D-4**: Data-quality typos in `sprk_regardingfield` for Project, Budget, Billing Analysis. Authorize inline fix in task 002?
- **D-1..D-3**: `sprk_fieldmappingprofile` + `sprk_fieldmappingrule` schemas don't match spec Appendix A. Affects Waves 6 tasks 060/061/062 substantially. Spec rewrite recommended.

## Recent completion

- ✅ SRFR-001 Wave 0 discovery audit — completed 2026-07-02

## Next action

**Blocking**: present `notes/wave-0-discovery.md` to owner; obtain decisions on D-1..D-7; then update spec.md and adjust task 002/010/060/061/062 scopes before invoking `task-execute` on SRFR-002.
