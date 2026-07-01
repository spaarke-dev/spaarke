# Task 081 — sprk_playbooknode Form Update: Actuals vs Plan

> **Executed**: 2026-06-29 by main session (operator pre-authorized "Power Apps form via MCP" path)
> **Environment**: spaarkedev1
> **Method**: Dataverse Web API (PUT on `PicklistAttributeMetadata` typed cast + `PublishXml`)

## Pre-state findings

The POML assumed the form still had a `sprk_nodetype` control and needed both removal of that control AND addition of a `sprk_executortype` control. Empirical state in spaarkedev1 (verified 2026-06-29):

| Surface | Pre-state | Notes |
|---|---|---|
| `sprk_playbooknode.sprk_nodetype` column | ABSENT from schema | Removed per FR-02 schema-removal-DONE (2026-06-27 wave) |
| `sprk_playbooknode.sprk_executortype` column | PRESENT (Choice, 33 values, global `sprk_playbookexecutortype`) | Schema-creation pre-R7-run |
| "Playbook Node main form" (`df6ea305-…`) — control bound to `sprk_nodetype` | ABSENT | — |
| "Playbook Node main form" — control bound to `sprk_executortype` | PRESENT (labelled "Executor Type", cell `7286dda1-…`) | Already wired into the form's Information section |
| `sprk_executortype.DefaultFormValue` | `-1` (no default) | **This was the only outstanding FR-21 gap** |
| "Information" form (`7d06c799-…`) — system default form | No `sprk_*` columns beyond defaults | Not user-facing for editing; out of scope |

So the only delta needed was setting the column-level `DefaultFormValue` from `-1` → `1` (AiCompletion). All other FR-21 acceptance criteria were already satisfied by prior work.

## Change applied

```http
PUT /api/data/v9.2/EntityDefinitions(LogicalName='sprk_playbooknode')/Attributes(LogicalName='sprk_executortype')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata
Body: full current metadata with DefaultFormValue = 1
```

Followed by `PublishXml` against the entity.

Post-PUT verification (read back):
```
SchemaName       : sprk_ExecutorType
DefaultFormValue : 1 (expected: 1 = AiCompletion)
```

## Why PUT (not PATCH)

`PATCH` on `/EntityDefinitions(...)/Attributes(...)` returns `405 Method Not Allowed` for metadata. The supported pattern is `PUT` on the typed-cast URL with the full metadata body (Dataverse merges all properties, not just the changed one). Empirically discovered this turn; recording here so future schema-update tasks (similar pattern) don't repeat the round-trip.

## Acceptance criteria status

| Criterion (per task POML) | Status | Evidence |
|---|---|---|
| Form no longer displays Node Type | ✅ | `sprk_nodetype` absent from both forms (FormXML scan) |
| Form displays Executor Type Choice with 33 values | ✅ | FormXML contains `<control id="sprk_executortype" classid="{3EF39988-…}" datafieldname="sprk_executortype"`; OptionSet has 33 values |
| New-row default = AiCompletion (1) | ✅ | `DefaultFormValue: 1` post-PUT |
| Form imports + publishes cleanly | ✅ | `PublishXml` 200; idempotent re-publish OK |
| UAT spot-check confirms behavior | ⏸️ | **Operator-only** — requires browser session in spaarkedev1. Suggested check: open a new sprk_playbooknode record via the model-driven app and confirm Executor Type pre-fills to "AI Completion". |
| code-review + adr-check pass at Step 9.5 | n/a | No source-file change; nothing for code-review to scan. Form XML was not edited (column was already present); only metadata default was changed. |

## Why no solution export

The change is metadata-only (column DefaultFormValue) + already-deployed form. Exporting the solution to capture a delta would create churn for zero benefit — the next solution export (e.g., for Wave 8 task 089d deployment) will pick up the metadata change automatically.

## Follow-ups left for operator

- Browser UAT in spaarkedev1: open a fresh `sprk_playbooknode` record; verify Executor Type pre-populates "AI Completion (1)" before manual selection.
- If operator wishes, export `SpaarkePlaybookCore` (or whichever managed solution owns this entity) to capture the metadata change in source for the Wave 8 PR.
