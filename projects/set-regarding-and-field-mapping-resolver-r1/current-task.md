# Current Task

**Project**: set-regarding-and-field-mapping-resolver-r1
**Wave**: Wave 7 in progress (SRFR-072 ✅, SRFR-070 ✅ this session; SRFR-071 remaining)
**Task**: none active
**Status**: idle
**Started**: —
**Rigor**: —

## Quick Recovery

| Field | Value |
|-------|-------|
| **Task** | — (SRFR-070 complete) |
| **Step** | — |
| **Status** | idle |
| **Next Action** | Wave 7 group E residual: **SRFR-071 (ADR-024 amendment MINIMAL)**. After that, Wave 8 (deploys + UAT). |

## Session Notes / Key Learnings (SRFR-070)

- **`entitymap_attributemap` collection nav prop rejects `$expand`** on the `entitymaps` OData collection. Documentation implies it should work; the Dataverse Web API in current-gen environments returns `Could not find a property named 'entitymap_attributemap' on type 'Microsoft.Dynamics.CRM.entitymap'`. Workaround: query `attributemaps?$filter=_entitymapid_value eq {guid}` separately per EntityMap. Same shape, one extra round-trip per EntityMap.
- **Real OOB mappings exist on both surveyed pairs** in spaarkedev1: `sprk_matter→sprk_event` and `sprk_project→sprk_event` each have 2 auto-populate OOB mappings (`sprk_{parent}id` → `sprk_regarding{parent}` and `sprk_{parent}number` → `sprk_regarding{parent}name`). None collide with the current Project→Event single rule (`sprk_assignedlawfirm`). Admins should evaluate them against §A.6 before adding profile rules on the OOB fields.
- **Two-step recordtype resolution**: profile field is `sprk_sourcerecordtype` (LOOKUP to `sprk_recordtype_ref`), not a text logical name. Script batches unique recordtype GUIDs into a single resolution pass.
- **Audit is idempotent**: verified by diffing two consecutive runs (identical output modulo date header, which is deterministic per-day).

## Applicable ADRs (session-level)

- None (audit-only task; no code changes; STANDARD rigor skipped Step 9.5).

## Files Modified This Session

- CREATED: `projects/set-regarding-and-field-mapping-resolver-r1/scripts/audit-oob-mappings.ps1`
- CREATED: `projects/set-regarding-and-field-mapping-resolver-r1/notes/oob-mapping-audit.md`
- CREATED: `projects/set-regarding-and-field-mapping-resolver-r1/notes/wave-7-task-070.log`
- UPDATED: `projects/set-regarding-and-field-mapping-resolver-r1/tasks/070-oob-mapping-audit.poml` (status: not-started → completed)
- UPDATED: `projects/set-regarding-and-field-mapping-resolver-r1/tasks/TASK-INDEX.md` (SRFR-070 🔲 → ✅)
