# Task 007 - schema v4 data needs and the episode-scoped dedupe key: verification record

> **Date**: 2026-10-07 · **Env**: `spaarkedev1` · solution `OntologyPlatformSolution` (publisher Spaarke, prefix `sprk`)
> **Method**: Dataverse Web API, the recipe in `schema-draft.md`: explicit PascalCase `SchemaName`, header
> `MSCRM.SolutionUniqueName: OntologyPlatformSolution`, `DateTimeBehavior` on the date column. No MCP `create_table`.
> Published per entity with `PublishXml` (`sprk_signal`, `sprk_policy`, `sprk_policyversion`, `sprk_decisionrecord`); no PublishAllXml.

## Before (live describe, step 1)

None of the columns below existed. `sprk_dedupekey`: String, MaxLength **400**, alternate key `sprk_signaldedupekey` **Active**.
`sprk_signal.sprk_matter` and `sprk_decisionrecord.sprk_matter`: RequiredLevel **None** on both.
`sprk_policy.statuscode`: Active (1, state 0), Inactive (2, state 1). `sprk_signal` rows: **0**.

## Columns created (all HTTP 204; read back live)

| Table.Column | Type | Detail | Before | After |
|---|---|---|---|---|
| sprk_signal.sprk_duedate | DateTime | Format DateOnly, **DateTimeBehavior DateOnly**, optional | absent | present |
| sprk_signal.sprk_corerecordtype | Lookup -> sprk_recordtype_ref | optional, RemoveLink on delete | absent | present |
| sprk_signal.sprk_corerecordid | String | MaxLength 100, optional | absent | present |
| sprk_policy.sprk_severity | Picklist (local) | Info 100000000, Warning 100000001, Critical 100000002 (same values as sprk_signal.sprk_severity) | absent | present |
| sprk_policy.sprk_shortname | String | MaxLength 100 | absent | present |
| sprk_policy.sprk_worktype | Picklist (local) | 100000000 Ask outside counsel, 100000001 Approve or rebudget, 100000002 Chase a reply, 100000003 Finish or reschedule, 100000004 Coming due, 100000005 Chase a response | absent | present |
| sprk_policy.sprk_retiredreason | Memo | MaxLength 2000 | absent | present |
| sprk_policy.statuscode | Status | new value **Retired = 100000000** on state 1 (Inactive), via InsertStatusValue | Active, Inactive | Active (1/0), Inactive (2/1), Retired (100000000/1) |
| sprk_policyversion.sprk_decisionplan | Memo | JSON, MaxLength 1048576 | absent | present |
| sprk_decisionrecord.sprk_steps | Memo | JSON, MaxLength 1048576 | absent | present |
| sprk_decisionrecord.sprk_followons | Memo | JSON, MaxLength 1048576 | absent | present |
| sprk_decisionrecord.sprk_gatetier | String | MaxLength 100 | absent | present |
| sprk_decisionrecord.sprk_corerecordtype | Lookup -> sprk_recordtype_ref | optional | absent | present |
| sprk_decisionrecord.sprk_corerecordid | String | MaxLength 100, optional | absent | present |
| sprk_decisionrecord.sprk_project | Lookup -> sprk_project | optional; typed lineage edge (D-36) | absent | present |
| sprk_decisionrecord.sprk_workassignment | Lookup -> sprk_workassignment | optional; typed lineage edge (D-36) | absent | present |

All new columns: RequiredLevel None. Relationship schema names: `sprk_signal_corerecordtype_recordtype_ref`,
`sprk_decisionrecord_corerecordtype_recordtype_ref`, `sprk_decisionrecord_project_sprk_project`,
`sprk_decisionrecord_workassignment_sprk_workassignment`.

Solution membership: `OntologyPlatformSolution` holds the ontology tables as whole-entity components
(componenttype 1, RootComponentBehavior 0 = include all subcomponents), so the columns belong to it through the
entity. There are no per-attribute solution components to list (a componenttype 2 query returns 0 rows).

## The dedupe key (D-13)

- Longest key = policycode (<= 100) + `|` + subject type (a table logical name, about 40 at most) + `|` + 36-char GUID + `|` + episode (<= 6): under 190 characters. `sprk_dedupekey` MaxLength is **400**, so it fits and **MaxLength was NOT changed** (400 before and after).
- Alternate key `sprk_signaldedupekey`: **Active** before and after.
- Re-key: `sprk_signal` rows **0 before, 0 after**; nothing to re-key (a valid result). Writers must write `|1` on first raise.
- No `sprk_episode` column exists (no attribute name containing "episode" on any of the four tables).

## D-35: sprk_matter required level

`sprk_signal.sprk_matter`: **None** before, **None** after. `sprk_decisionrecord.sprk_matter`: **None** before, **None** after.
Neither was ApplicationRequired, so no change was needed and the escalation trigger did not fire.

## D-36 catalog check

`sprk_recordtype_ref` has active rows with `sprk_recorddisplaynamefield` set for every type in `CoreAncestorResolver.CoreRecordEntities` on origin/master:
sprk_matter MTR (`sprk_mattername`), sprk_project PRJT (`sprk_projectname`), sprk_workassignment WRK (`sprk_workassignmentname`),
sprk_servicerequest SVCR (`sprk_name`). No catalog rows were added.

## uac-r2 coordination (origin/master re-read 2026-10-07)

- `CoreAncestorResolver.CoreRecordEntities` = sprk_project, sprk_matter, sprk_workassignment, sprk_servicerequest; `CoreAncestorLookups` are the `sprk_regarding{core}` columns.
- `SecureChildLineage.Children` (column -> ONE target table) lists `sprk_spendsignal` but has **no entry for `sprk_signal` or `sprk_decisionrecord`**, and `config/secure-record-owner-role.json` has none either. The existing `sprk_matter` plus the new typed `sprk_project` / `sprk_workassignment` on `sprk_decisionrecord` are the lineage edges D-36 needs, but **the uac-r2 data change (a SecureChildLineage entry for `sprk_decisionrecord`, and for `sprk_signal` via its regarding lookups per task 039, plus role-file entries) is NOT made here**; it is outside this task and needs uac-r2 review (see task 039 / 079).
- `sprk_servicerequest` has no typed lookup on the Decision Record, as D-36 states; SecureChildLineage deliberately excludes it.

## Negative checks

No `sprk_episode`; nothing added outside the four tables; no core-record type CHOICE column; no typed core lookup added to `sprk_signal`;
no grouping lookup beyond the D-36 lineage edges on `sprk_decisionrecord`.

## Deviations

1. `sprk_policy.sprk_severity` is a new LOCAL option set with the Signal's values: the Signal's set (`sprk_signal_sprk_severity`) is local, not global, so it cannot be reused.
2. JSON memo columns use MaxLength 1048576 (the platform maximum); the POML did not specify.
