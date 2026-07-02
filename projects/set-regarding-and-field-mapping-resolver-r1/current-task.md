# Current Task

**Project**: set-regarding-and-field-mapping-resolver-r1
**Task ID**: SRFR-002 (ready to start)
**Task File**: [`tasks/002-populate-recordtype-metadata.poml`](./tasks/002-populate-recordtype-metadata.poml)
**Wave**: 0
**Status**: not-started

## Ready

Owner resolved all 7 Wave 0 divergences (D-1 through D-7) on 2026-07-02. Post-resolution updates completed:

- ✅ `spec.md` Appendix A §A.2 + §A.3 + §A.3a + §A.4a + §A.5 rewritten to match real schema
- ✅ SRFR-002 POML expanded from 2h → 5h with 4 workstreams (W1 add `sprk_mappingtype`, W2 recreate 3 typo'd lookups, W3 populate all 13 catalog rows, W4 fix Person catalog to OOB `contact`)
- ✅ SRFR-010 POML expanded from 10 → 11 target entities (adds Billing Analysis per D-7)
- ✅ SRFR-060 POML updated for real profile + rule schema (lookups instead of text; `sprk_compatibilitymode`; per-rule fields)
- ✅ SRFR-061 POML updated for two-step lookup-based profile query per revised §A.5
- ✅ SRFR-062 POML: title + prompt updated to reflect **BOTH Matter AND Project** ribbon deploys (Q-07)
- ✅ TASK-INDEX effort updated (SRFR-002: 2h → 5h)

## Owner decisions (2026-07-02)

| Divergence | Decision |
|---|---|
| D-1 (profile schema) | Accept as-is; spec rewritten |
| D-2 (`sprk_mappingtype`) | Add to Dataverse via SRFR-002 W1 |
| D-3 (per-rule syncmode) | Update spec accordingly |
| D-4 (lookup typos) | Remove + recreate with correct logical names via SRFR-002 W2 |
| D-5 (all 13 rows empty) | Populate all 13 via SRFR-002 W3 |
| D-6 (`sprk_contact` → `contact`) | Fix catalog via SRFR-002 W4 |
| D-7 (13 record types) | Confirmed; SRFR-010 scope expanded to include Billing Analysis |

## Next action

Say `execute task 002` or `continue` to run SRFR-002 (Wave 0 data-fix task, ~5h).

## Recently completed

- ✅ SRFR-001 Wave 0 discovery audit — 2026-07-02
- ✅ Divergence resolution pass — spec.md + 5 POML files updated 2026-07-02
