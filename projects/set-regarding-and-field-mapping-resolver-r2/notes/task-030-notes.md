# Task 030 — Seed Attorney Matrix — Notes

> **Verdict**: ✅ COMPLETE (2026-07-09). All 3 pairs seeded + verified live in `spaarkedev1`. The Report Card blocker was resolved by the main session under owner approval (Path C→A: investigated the `sprk_recordtype_ref` convention, then created the missing registry row + the 8 rules). See §8 for the resolution. Original blocker detail retained in `task-030-BLOCKED.md` (now RESOLVED).
> **Environment**: spaarkedev1 (live Dataverse dev), via `mcp__dataverse__*` tools.
> **Date**: 2026-07-09

## 1. Destructive-op cleanup (verified before + after)

**Pre-cleanup query** confirmed exactly the expected records — no ambiguity:

| Kind | Record | Before | Action | After (verified) |
|---|---|---|---|---|
| Orphan rule | `d2bc58eb-a779-f111-ab0e-7ced8ddc4a05` ("New Field Mapping Rule") — no `sprk_fieldmappingprofile` link, `sprk_mapping_type`/`sprk_executionorder`/`sprk_sourcefield`/`sprk_targetfield`/`sprk_isactive` all null | Active, orphaned/empty | **DELETED** | Query for this id returns `[]` — confirmed gone |
| Stale profile | `f3d88a0a-b179-f111-ab0e-7ced8ddc4a05` "Matter → Event cascade UAT (SRFR-084)" | statecode=0 (Active) | **DEACTIVATED** | statecode=1, statuscode=2 (Inactive) — confirmed |
| Stale profile | `e5c5260b-cd7a-f111-ab0e-7ced8ddc4a05` "Project → Event cascade UAT (SRFR-084)" | statecode=0 (Active) | **DEACTIVATED** | statecode=1, statuscode=2 (Inactive) — confirmed |

The two stale profiles' 4 child rules (`sprk_mattername→sprk_description`, `sprk_matternumber→sprk_priorityreason`, `sprk_projectname→sprk_description`, `sprk_projectnumber→sprk_priorityreason`) were left untouched (still `sprk_isactive=true`) since their parent profile is now inactive and the task only specified deactivating the profiles, not their child rules.

Deletion was performed under the task's own explicit written pre-authorization ("Only hard-DELETE the single specifically-identified orphaned empty rule") after verification confirmed an exact 1-record match — no live user prompt was issued mid-task since the task file itself constitutes the informed, scoped consent for this precise, verified record.

## 2. Describe-verified target schema (re-confirmed at seed time, not assumed)

### `sprk_event` — 8 fields, IDENTICAL names to Matter (matrix confirmed exactly as predicted)
`sprk_assignedattorney1/2` (contact), `sprk_assignedparalegal1/2` (contact), `sprk_assignedlawfirm1/2` (sprk_organization), `sprk_assignedtoexternal` (contact), `sprk_assignedtointernal` (contact) — all present, all lookups.

### `sprk_invoice` — 6 assigned-resource-shaped fields, renamed + reduced set
`sprk_assignedtoattorney1/2` (contact), `sprk_assignedtoparalegal1/2` (contact), `sprk_assignedto1` (contact), `sprk_assignedto2` (contact). **No lawfirm field of any name. No external/internal field of any name** (not renamed — genuinely absent, unlike the task text's framing of "confirm the exact target names via describe" which implied a renamed-but-present field).

### `sprk_reportcard` — matches the predicted matrix exactly
`sprk_assignedattorney1/2` (contact), `sprk_assignedparalegal1/2` (contact), `sprk_assignedtoexternal`/`sprk_assignedtointernal` (contact) — same names as Matter. `sprk_assignedlawfirm2` (contact... actually sprk_organization) — same name as Matter. `sprk_assignedtolawfirm1` (sprk_organization) — renamed from Matter's `sprk_assignedlawfirm1`, exactly as predicted. **This target's schema is fully verified and ready to seed** — only blocked by the missing `sprk_recordtype_ref` row (see BLOCKED note).

### `sprk_recordtype_ref` — the actual blocker
Full table listing (12 rows, no filter): Account, Analysis, Budget, Document, Event, Invoice, Matter, Organization, Person, Project, To Do, Work Assignment. **No row for Report Card / `sprk_reportcard` / `sprk_kpiassessment` in any state.**

## 3. Seeded matrix — DONE (Matter→Event, Matter→Invoice)

### Profile: Matter → Event (`24dc0ed2-537b-f111-ab0e-7ced8ddc4a05`), Active
sourcerecordtype = Matter (`e8547bb4-8600-f111-8407-7c1e520aa4df`), targetrecordtype = Event (`5e9b37ea-8600-f111-8406-7c1e525abd8b`)

| # | Source (Matter) | Target (Event) | Type | Source/Target FieldType | Rule ID |
|---|---|---|---|---|---|
| 1 | sprk_assignedattorney1 | sprk_assignedattorney1 | Copy | Lookup/Lookup | 3113c3d9-537b-f111-ab0e-7ced8ddc4a05 |
| 2 | sprk_assignedattorney2 | sprk_assignedattorney2 | Copy | Lookup/Lookup | 3313c3d9-537b-f111-ab0e-7ced8ddc4a05 |
| 3 | sprk_assignedparalegal1 | sprk_assignedparalegal1 | Copy | Lookup/Lookup | 3513c3d9-537b-f111-ab0e-7ced8ddc4a05 |
| 4 | sprk_assignedparalegal2 | sprk_assignedparalegal2 | Copy | Lookup/Lookup | 3713c3d9-537b-f111-ab0e-7ced8ddc4a05 |
| 5 | sprk_assignedlawfirm1 | sprk_assignedlawfirm1 | Copy | Lookup/Lookup | 3913c3d9-537b-f111-ab0e-7ced8ddc4a05 |
| 6 | sprk_assignedlawfirm2 | sprk_assignedlawfirm2 | Copy | Lookup/Lookup | 3b13c3d9-537b-f111-ab0e-7ced8ddc4a05 |
| 7 | sprk_assignedtoexternal | sprk_assignedtoexternal | Copy | Lookup/Lookup | ed657ae0-537b-f111-ab0e-7ced8ddc4a05 |
| 8 | sprk_assignedtointernal | sprk_assignedtointernal | Copy | Lookup/Lookup | ef657ae0-537b-f111-ab0e-7ced8ddc4a05 |

All 8 confirmed via query-back: `sprk_mapping_type=0` (Copy), `sprk_sourcefieldtype`/`sprk_targetfieldtype=1` (Lookup), `sprk_isactive=true`, `sprk_executionorder` 1-8 in order.

### Profile: Matter → Invoice (`25dc0ed2-537b-f111-ab0e-7ced8ddc4a05`), Active
sourcerecordtype = Matter (`e8547bb4-8600-f111-8407-7c1e520aa4df`), targetrecordtype = Invoice (`8e4a04f1-8600-f111-8406-7c1e525abd8b`)

| # | Source (Matter) | Target (Invoice) | Type | Source/Target FieldType | Rule ID |
|---|---|---|---|---|---|
| 1 | sprk_assignedattorney1 | sprk_assignedtoattorney1 | Copy | Lookup/Lookup | f3657ae0-537b-f111-ab0e-7ced8ddc4a05 |
| 2 | sprk_assignedattorney2 | sprk_assignedtoattorney2 | Copy | Lookup/Lookup | f5657ae0-537b-f111-ab0e-7ced8ddc4a05 |
| 3 | sprk_assignedparalegal1 | sprk_assignedtoparalegal1 | Copy | Lookup/Lookup | ab8a10e7-537b-f111-ab0e-7ced8ddc4a05 |
| 4 | sprk_assignedparalegal2 | sprk_assignedtoparalegal2 | Copy | Lookup/Lookup | ad8a10e7-537b-f111-ab0e-7ced8ddc4a05 |

**No law-firm rules** (constraint satisfied — Invoice has no law-firm field at all). **No external/internal rules** — Invoice has no such field under any name (confirmed absent, not renamed); per spec.md's own assumption ("only fields present on a given target are seeded... absent fields are simply not mapped"), these are correctly omitted, not a schema-drift escalation.

All 4 confirmed via query-back: `sprk_mapping_type=0` (Copy), fieldtypes=1 (Lookup), `sprk_isactive=true`, execution order 1-4.

## 4. `sprk_assignedto1`/`sprk_assignedto2` (Invoice) decision — RESOLVED: OMIT

Per the constraint ("only add a mapping if a clean Matter source counterpart exists; otherwise omit and note it"): **Matter has no `sprk_assignedto1`/`sprk_assignedto2` fields** (Matter's 8 confirmed assigned-resource fields are attorney1/2, paralegal1/2, lawfirm1/2, external, internal — no generic `assignedto1/2`). There is no clean source counterpart. **Decision: omitted, no rule created for either field.** This matches spec.md's own Unresolved Question #3 framing ("likely not — no clean source counterpart").

## 5. NOT DONE — Matter → Report Card (blocked)

See `task-030-BLOCKED.md`. The target schema IS fully describe-verified and ready (would be identical to the design matrix: attorney1/2, paralegal1/2, external, internal identical names; lawfirm1→`sprk_assignedtolawfirm1`, lawfirm2→`sprk_assignedlawfirm2` for a total of 8 rules), but no `sprk_fieldmappingprofile` can be created because **`sprk_recordtype_ref` has no row for Report Card at all** (verified via full-table listing, not a filtered query). This is a missing prerequisite, not a guessable field-name mismatch, so per the task's own escalation posture I stopped rather than inventing an authoritative reference-data row.

**Planned matrix for when unblocked** (for the owner/main-session to seed directly once the recordtype-ref exists, or to re-invoke this task):

| # | Source (Matter) | Target (Report Card) | Type | FieldTypes |
|---|---|---|---|---|
| 1 | sprk_assignedattorney1 | sprk_assignedattorney1 | Copy | Lookup/Lookup |
| 2 | sprk_assignedattorney2 | sprk_assignedattorney2 | Copy | Lookup/Lookup |
| 3 | sprk_assignedparalegal1 | sprk_assignedparalegal1 | Copy | Lookup/Lookup |
| 4 | sprk_assignedparalegal2 | sprk_assignedparalegal2 | Copy | Lookup/Lookup |
| 5 | sprk_assignedlawfirm1 | sprk_assignedtolawfirm1 | Copy | Lookup/Lookup |
| 6 | sprk_assignedlawfirm2 | sprk_assignedlawfirm2 | Copy | Lookup/Lookup |
| 7 | sprk_assignedtoexternal | sprk_assignedtoexternal | Copy | Lookup/Lookup |
| 8 | sprk_assignedtointernal | sprk_assignedtointernal | Copy | Lookup/Lookup |

## 6. Acceptance criteria status

| Criterion | Status |
|---|---|
| Orphaned empty rule deleted; stale UAT profiles deactivated | ✅ DONE, verified |
| Active profiles + Copy rules exist for the three Matter→target pairs | ✅ DONE — Event (8) + Invoice (4) + Report Card (8), all verified |
| Invoice rules include no law-firm mapping; Report Card lawfirm1→sprk_assignedtolawfirm1 | ✅ Invoice confirmed no-lawfirm; Report Card lawfirm1→sprk_assignedtolawfirm1 created + verified |
| Seeded matrix (+ assignedto1/2 decision) recorded in notes | ✅ this file |

## 7. Task status

✅ **COMPLETED** 2026-07-09. All three Matter→target pairs seeded + verified live. See §8 for the Report Card resolution.

## 8. Report Card resolution (main session, 2026-07-09) — Path C→A under owner approval

**Root cause**: Report Card is fully wired as an ADR-024 resolver *child* (`sprk_reportcard` has `sprk_regardingrecordtype → sprk_recordtype_ref`, `sprk_regardingrecordid`, `sprk_regardingmatter`, `sprk_regardingproject`, `sprk_reportcardnumber`, primary name `sprk_name`) but was never registered as a resolver *type* in `sprk_recordtype_ref` — an oversight (all 11 other wizard entities are registered). Not a design decision.

**Investigation (Path C)**: read all 12 existing `sprk_recordtype_ref` rows; the convention is fully mechanical (`sprk_recordtypecode` is null on every row — unused). Proposed row mirrored the pattern; owner approved.

**Created (Path A)**:
- `sprk_recordtype_ref` row **`5bc206a0-587b-f111-ab0e-7ced8ddc4a05`** — `sprk_recordtypename="Report Card"`, `sprk_recordlogicalname="sprk_reportcard"`, `sprk_recorddisplayname="Report Card"`, `sprk_recorddisplaynamefield="sprk_name"`, `sprk_regardingrecordnumberfield="sprk_reportcardnumber"`, `sprk_regardingfield="sprk_regardingreportcard"` (convention; reverse-regarding nav string, not on the copy path), Active.
- Profile **Matter → Report Card (Attorney Matrix)** `b2915dad-587b-f111-ab0e-7ced8ddc4a05` — source=Matter (`e8547bb4…`), target=new row, Active.
- 8 Copy rules (Lookup/Lookup, order 1-8), all query-back verified:

| # | Source (Matter) | Target (Report Card) | Rule ID |
|---|---|---|---|
| 1 | sprk_assignedattorney1 | sprk_assignedattorney1 | 8de027b4-587b-f111-ab0e-7ced8ddc4a05 |
| 2 | sprk_assignedattorney2 | sprk_assignedattorney2 | 8fe027b4-587b-f111-ab0e-7ced8ddc4a05 |
| 3 | sprk_assignedparalegal1 | sprk_assignedparalegal1 | 91e027b4-587b-f111-ab0e-7ced8ddc4a05 |
| 4 | sprk_assignedparalegal2 | sprk_assignedparalegal2 | 94e027b4-587b-f111-ab0e-7ced8ddc4a05 |
| 5 | sprk_assignedlawfirm1 | sprk_assignedtolawfirm1 | 96e027b4-587b-f111-ab0e-7ced8ddc4a05 |
| 6 | sprk_assignedlawfirm2 | sprk_assignedlawfirm2 | ba6ce5bb-587b-f111-ab0e-7ced8ddc4a05 |
| 7 | sprk_assignedtoexternal | sprk_assignedtoexternal | bc6ce5bb-587b-f111-ab0e-7ced8ddc4a05 |
| 8 | sprk_assignedtointernal | sprk_assignedtointernal | be6ce5bb-587b-f111-ab0e-7ced8ddc4a05 |

**Follow-up note for docs (040/041)**: the Report Card `sprk_recordtype_ref` row was created by this project. If Report Card is expected to be a regarding *target* elsewhere, confirm whether a physical `sprk_regardingreportcard` reverse-lookup column is needed (currently the registry names the convention only).
