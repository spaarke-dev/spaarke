# Current Task

**Project**: set-regarding-and-field-mapping-resolver-r1
**Task ID**: SRFR-010 (ready to start)
**Task File**: [`tasks/010-add-regardingrecordnumber-column.poml`](./tasks/010-add-regardingrecordnumber-column.poml)
**Wave**: 1
**Status**: not-started

## Ready

Wave 0 complete (SRFR-001 + SRFR-002). Catalog + `sprk_fieldmappingrule` schema are now aligned with spec's revised Appendix A. Ready to begin Wave 1 schema additions.

## Recently completed

- ✅ SRFR-002 Wave 0 data-fix (2026-07-02) — all 4 workstreams + owner-decision W1a expansion successful in ~1.5h (vs 5h estimate)

## New Wave 0 findings (documented in notes/wave-0-metadata-population.log)

- **D-8** (resolved via W1a): 3 target entities were missing `sprk_Xnumber` fields → added
- **D-9** (resolved via W4a): Billing Analysis table doesn't exist → catalog row removed. **SRFR-010 scope adjustment**: now 10 target entities not 11 (Billing Analysis excluded)
- **D-10** (deferred to Wave 5): `sprk_communication` uses `sprk_regardingperson` while catalog says `sprk_regardingcontact` — asymmetry across hosts for Person target
- **D-11** (documented): MCP tool converts spaces to underscores in logical names → new attributes are `sprk_mapping_type`, `sprk_analysis_number`, `sprk_organization_number`, `sprk_document_number`

## SRFR-010 scope adjustment (D-9)

Original SRFR-010 target list was 11 entities. After D-9 (Billing Analysis catalog row removed), scope is **10 target entities**: Project, Invoice, Event, Analysis, Organization, contact, Document, WorkAssignment, Budget, Account. (Matter already has `sprk_regardingrecordnumber` column from 2026-07-01.)

## Next action

Say `execute task 010` or `continue` to run SRFR-010 (add `sprk_regardingrecordnumber` text column to 10 target entities).
