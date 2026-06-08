# sprk_recordtype_ref Registration for sprk_todo

> **Task**: 003-register-sprk-todo-in-recordtype-ref
> **Date**: 2026-06-07
> **Environment**: https://spaarkedev1.crm.dynamics.com/

## Row Created

One row inserted into `sprk_recordtype_ref` registering `sprk_todo` as a valid polymorphic-resolver target per ADR-024 and spec FR-04.

| Field | Value |
|-------|-------|
| `sprk_recordtype_refid` (PK) | `d4a606e6-cf62-f111-ab0c-70a8a58ae145` |
| `sprk_recordtypename` (required) | `To Do` |
| `sprk_recordlogicalname` | `sprk_todo` |
| `sprk_recorddisplayname` | `To Do` |
| `sprk_regardingfield` | `sprk_regardingtodo` |
| `sprk_recordtypecode` | (not set — unused by all existing rows) |

## Template Referenced

Pattern matched against the 12 pre-existing rows in `sprk_recordtype_ref` (queried in step 2). All existing rows follow the same four-field shape:

- `sprk_recordtypename` = friendly display label
- `sprk_recordlogicalname` = Dataverse entity logical name
- `sprk_recorddisplayname` = friendly display label (mirrors `sprk_recordtypename`)
- `sprk_regardingfield` = name of the corresponding lookup field on parent entities

Closest template: `sprk_workassignment` (Work Assignment / sprk_regardingworkassignment) and `sprk_matter` (Matter / sprk_regardingmatter). The pattern is consistent across all 12 rows.

## Decisions Made

1. **`sprk_regardingfield = "sprk_regardingtodo"`** — Follows the `sprk_regarding{entityname}` convention used by 9 of 12 existing rows. Note: this is the field name that OTHER entities would use to point at `sprk_todo` (future-proofing per FR-04). `sprk_todo` itself does not currently appear as a regarding target on any existing entity, but this row exists so the polymorphic-resolver registry can resolve it if/when that changes.

2. **`sprk_recordtypecode` left null** — None of the 12 existing rows populate this field. Consistent with established convention.

3. **`sprk_recorddisplayname = sprk_recordtypename = "To Do"`** — Matches design.md and entity-schema.md display name; mirrors the convention where these two fields are identical across all existing rows.

## Idempotency Check

Pre-insert query returned zero rows matching `sprk_recordlogicalname = 'sprk_todo'`. No duplicate created.

## Verification

Post-insert query returned exactly one row with the expected field values. Acceptance criterion met.

## Unexpected Schema Findings

- The Person/Contact row uses `sprk_recordlogicalname = "sprk_contact"` even though the actual OOB target is `contact`. This is an existing data anomaly unrelated to this task. The `sprk_todo` row uses the correct logical name (`sprk_todo`).
- Several `sprk_regardingfield` values in existing rows have typos (e.g., `sprk_regarrdingbudget`, `sprk_egardingproject`, `sprk_regardingbillinganaysis`). These are pre-existing and out of scope for this task. The `sprk_todo` row uses the correctly-spelled `sprk_regardingtodo`.
