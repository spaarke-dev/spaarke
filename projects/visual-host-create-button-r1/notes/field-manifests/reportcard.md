# Field Manifest — Report Card (`sprk_reportcard`)

> Written 2026-07-08, post-Phase-D, replacing the original KPI Assessment wizard scope per owner decision (see below). Validated against live Dataverse schema (spaarkedev1).

## Scope pivot (owner decision, 2026-07-08)

The Visual Host "+" button's third wizard target changed from `sprk_kpiassessment` directly to `sprk_reportcard`. `sprk_kpiassessment` records are line-items that belong to a parent `sprk_reportcard` (via `sprk_kpiassessment.sprk_reportcard`, confirmed in Phase 0 discovery). Adding KPI Assessment line-items to a Report Card is a separate, later capability (e.g. a subgrid "+ new" on the Report Card form) — this wizard creates the Report Card shell only.

Registry key renamed `kpi-assessment` → `report-card`. `wizardRegistry.ts`'s `ENTITY_TO_WIZARD_KEY` maps both `sprk_reportcard` and `sprk_kpiassessment` to `report-card`, so a Visual Host visual bound to either entity defaults to this flow unless the maker sets an explicit key.

## Resolver readiness — ✅ FULLY READY, no schema delta

`sprk_reportcard` is **already fully ADR-024 resolver-ready** — better than the original KPI target:

| Field | Type | Present? |
|---|---|---|
| `sprk_regardingrecordtype` | Lookup → `sprk_recordtype_ref` | ✅ |
| `sprk_regardingrecordid` | Text(100) | ✅ |
| `sprk_regardingrecordname` | Text(100) | ✅ |
| `sprk_regardingrecordurl` | URL(1000) | ✅ |
| `sprk_regardingrecordnumber` | Text(100) | ✅ |

Entity-specific lookups: `sprk_regardingmatter`, `sprk_regardingproject` (Matter + Project only — matches the schema, not just a scope decision; no other `sprk_regarding{entity}` lookups exist on this table).

Note: a second, oddly-named `sprk_regarding_record_url` (URL, NVARCHAR(1000), with underscores) also exists on this entity — legacy/duplicate field, NOT the ADR-024 resolver field. `applyResolverFields` writes `sprk_regardingrecordurl` (no underscores) — ignore `sprk_regarding_record_url`, do not populate it.

## Enter Info manifest (owner decision 2026-07-08)

| Field (logical) | Type | Required? | Notes |
|---|---|---|---|
| `sprk_name` | Text(850) | **Required** (NOT NULL in schema) | Primary name |
| `sprk_narrative` | Multiline text | No | |
| `sprk_duedate` | Date only | No | |
| `sprk_assignedattorney1` | Lookup → contact | No | |
| `sprk_assignedattorney2` | Lookup → contact | No | |
| `sprk_assignedparalegal1` | Lookup → contact | No | |
| `sprk_assignedparalegal2` | Lookup → contact | No | |
| `sprk_assignedtolawfirm1` | Lookup → sprk_organization | No | note asymmetric naming vs. `sprk_assignedlawfirm2` — both are real schema field names, do not "fix" |
| `sprk_assignedlawfirm2` | Lookup → sprk_organization | No | |
| `sprk_assignedtoexternal` | Lookup → contact | No | |
| `sprk_assignedtointernal` | Lookup → contact | No | |

Owner decision: collect assigned-resource fields at creation time (not deferred to a later review step). All 8 assignment lookups are optional.

**Out of scope for Enter Info** (owner decision): `sprk_acceptdate`, `sprk_requestdate`, `sprk_submitdate` — these are workflow-progression dates set later, not at creation. `sprk_reportcardnumber` — likely an autonumber or set server-side; do not attempt to set client-side unless schema requires it (verify: no `NOT NULL` constraint observed on this field, and no autonumber format visible via basic describe — if the create call fails without it, treat as a blocking finding, not a silent workaround).

## Files — NOT included (owner decision 2026-07-08)

No Add Files step. `sprk_containerid` exists on the entity but the wizard doesn't populate it or offer document upload — same as the original KPI scope decision. No file dual-bind, no `sprk_document` schema changes needed for this wizard.

## Next Steps

Same as Event/Invoice: Send Email / Add To Do / Assign Work via the shared `WizardFollowOns` module.

## 🔴 Schema addition needed (owner-approved Path A, 2026-07-08): `sprk_todo` → `sprk_reportcard` lookup

`sprk_todo` has no lookup to `sprk_reportcard` today (confirmed via live describe — the existing `sprk_regarding*` lookups on `sprk_todo` are: analysis, budget, communication, contact, document, event, invoice, matter, organization, project, workassignment — no report card). Without this, the Add To Do follow-on card cannot set a To Do's regarding to a newly-created Report Card.

**Owner-approved fix**: add a new lookup column to `sprk_todo` targeting `sprk_reportcard` (additive, via `dataverse-create-schema`, same pattern as the two Phase-0 schema additions). Then add a matching entry to `TODO_REGARDING_CATALOG` (`src/client/shared/Spaarke.UI.Components/src/services/TodoRegardingUpdateBuilder.ts`):
```ts
{ entityType: 'sprk_reportcard', entitySet: 'sprk_reportcards', lookupAttribute: '<new column's SchemaName>', navPropHint: 'reportcard' }
```
Verify the exact `SchemaName` casing of the new lookup attribute via a metadata query BEFORE writing the catalog entry (same verification discipline as the Phase-0 `sprk_Event` lookup — Dataverse lookup attribute SchemaNames are PascalCase, e.g. likely `sprk_ReportCard`; do not guess).
