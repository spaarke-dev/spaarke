# SRFR-049 — Scope adjustments + sprk_reportcard schema fix

**Date**: 2026-07-06
**Rigor**: MINIMAL (schema + docs only; no code changes)
**Owner-driven**: yes (mid-project scope refinement)

## Scope adjustments (owner-confirmed 2026-07-06)

Original SRFR-046 planned placement on 5 child forms:
sprk_todo, sprk_event, sprk_invoice, sprk_communication, sprk_kpiassessment

Owner refined the entity model during initial UAT:

1. **To Do + Events aligned parent lists** — both associate to Matter/Project/Work Assignment (Event with To Do as extra for To Do). Structural, not a schema change.

2. **sprk_kpiassessment DROPPED as primary child** — replaced by `sprk_reportcard`. ReportCards is the actual primary child-of-Matter/Project entity used operationally. KPI Assessments retained in schema but not part of the resolver-placement plan.

3. **sprk_invoice DROPPED** — an invoice may associate to multiple parent entities AND multiple records per parent (e.g., one invoice with fees across Matter 1, Matter 2, Project 1, Project 2). Single-regarding-field resolver pattern does not fit. Deferred to follow-on project `invoice-polymorphic-multi-parent-r1` (Idea Issue filed in SRFR-090 wrap-up).

Final child-form placement list (4 forms):
- sprk_todo ✅ (placed pre-project, retested with v1.3.7 + v1.4.1)
- sprk_event ✅ (placed 2026-07-06 by owner; sprk_regardingcommunication clear-loop error → SRFR-048 v1.4.1 fix)
- sprk_reportcard ✅ (placed 2026-07-06 by owner; sprk_regardingrecordurl missing → SRFR-049 schema fix)
- sprk_communication ⏳ (schema verified 2026-07-06; owner placement pending)

## sprk_reportcard schema fix

Owner reported error on placement + first pick:
`PATCH sprk_reportcards(...) 400: Invalid property 'sprk_regardingrecordurl' was found in entity 'Microsoft.Dynamics.CRM.sprk_reportcard'`

Root cause: `sprk_reportcard` was NOT in the Wave 1 SRFR-010 schema batch (which added the 5-field resolver set to 10 entities). Someone previously added 4 of the 5 fields (`sprk_regardingrecordtype`, `sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordnumber`) plus the 2 entity-specific lookups (`sprk_regardingmatter`, `sprk_regardingproject`), but skipped `sprk_regardingrecordurl`.

Fix applied via MCP `update_table`:
- Column added: **sprk_regardingrecordurl** (URL type)
- First attempt with display name "Regarding Record URL" produced logical name `sprk_regarding_record_url` (D-11 MCP underscore convention). Corrected on second attempt by using display name "RegardingRecordURL" (no spaces) → produced canonical `sprk_regardingrecordurl` matching the other 10 entities.
- Wrong-named field `sprk_regarding_record_url` remains on the entity (unused; MCP has no delete-column). Recommend owner manual delete via make.powerapps.com if desired.

Verification:
- `SELECT sprk_regardingrecordurl FROM sprk_reportcard` returns rows (empty on existing record; will populate on next resolver write)
- `sprk_recordtype_ref` catalog verified: Matter + Project entries present with correct `sprk_regardingfield` + `sprk_regardingrecordnumberfield` metadata

## sprk_communication verification

Verified all 9 required columns present:
- 5 resolver fields (recordtype, recordid, recordname, recordurl, recordnumber)
- 4 entity-specific lookups (regardingmatter, regardingproject, regardingworkassignment, regardingorganization)

No schema changes needed. Placement proceeds directly.

## SRFR-047 dropped

Owner elected to leave Push Updates ribbon on Matter + Project only (no expansion to Work Assignment, Event, or other parents). Manual cascade via `sprk_fieldmappingprofile` MDA form remains available for other parents.

## Impact on TASK-INDEX

- SRFR-046 scope narrowed: 4 child forms (was 5)
- SRFR-047 removed from active plan
- SRFR-048 already logged (v1.4.1 nav-prop-limited clear)
- SRFR-049 = this task (schema + scope adjustment)
- SRFR-084 UAT scope narrowed accordingly
- SRFR-090 wrap-up must file follow-on Idea for `invoice-polymorphic-multi-parent-r1`
