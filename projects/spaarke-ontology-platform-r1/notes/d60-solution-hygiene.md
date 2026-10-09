# D-60 — `OntologyPlatformSolution` solution hygiene: 11 foreign tables removed

> **Date**: 2026-10-07/08 (UTC 2026-10-08 00:09) · **Env**: `spaarkedev1` · **By**: stream C2 · **Decision**: D-60 (spec §9,
> owner; found by stream C2 during task 047) · **Issue**: ISS-008 in `defer-issues.md` · **Status**: ✅ done.
> Draft task for numbering: [`d60-solution-hygiene-task-draft.poml`](d60-solution-hygiene-task-draft.poml).

## The defect

`OntologyPlatformSolution` (solutionid `f258ed0a-a6be-f111-aaad-7c1e520a989f`) held **11 other domains' tables WHOLE**,
as componenttype 1 with **rootcomponentbehavior 0** (include all subcomponents). They were added 2026-10-02 21:17–21:23
when this project's lookups were created under the solution header, and `sprk_recordtype_ref` on 2026-10-07 (task 007).
**Failure scenario**: exporting the solution for another environment ships every column, form, view, chart and ribbon of
`sprk_matter`, `sprk_project`, `sprk_document` and the rest **as they are in dev**. An unmanaged import overwrites the
target's customizations of those tables. A managed import puts a managed layer over core tables that this project does
not own.

## Inventory BEFORE (every component; there were no separate attribute, form, view or relationship rows)

| Type | rcb | Component | Kind |
|---|---|---|---|
| 1 | 0 | `sprk_budgetrevision` | **ours** |
| 1 | 0 | `sprk_decisionrecord` | **ours** |
| 1 | 0 | `sprk_policy` | **ours** |
| 1 | 0 | `sprk_policyversion` | **ours** |
| 1 | 0 | `sprk_signal` | **ours** |
| 1 | 1 | `systemuser` (metadata only) | foreign, **not** in the D-60 list: left as is |
| 1 | 0 | `sprk_budget` | foreign → remove |
| 1 | 0 | `sprk_communication` | foreign → remove |
| 1 | 0 | `sprk_document` | foreign → remove |
| 1 | 0 | `sprk_event` | foreign → remove |
| 1 | 0 | `sprk_invoice` | foreign → remove |
| 1 | 0 | `sprk_matter` | foreign → remove |
| 1 | 0 | `sprk_project` | foreign → remove |
| 1 | 0 | `sprk_recordtype_ref` | foreign → remove |
| 1 | 0 | `sprk_servicerequest` | foreign → remove, **but it carries this project's columns** (below) |
| 1 | 0 | `sprk_todo` | foreign → remove |
| 1 | 0 | `sprk_workassignment` | foreign → remove |

17 components, all type 1.

## This project's own columns on foreign tables

I searched the project notes for columns created on other tables (`schema-draft.md`, `001-`/`007-schema-verification.md`,
`security-roles.md`, `047-`):

- **Task 001** (created by the owner in the maker UI under this solution, then verified):
  `sprk_servicerequest.sprk_direction` (`33964a3b-55bf-f111-a05b-3833c5e9614d`), `sprk_servicerequest.sprk_disposition`
  (`123bec6e-55bf-f111-aaaf-0022482913fc`), `sprk_servicerequest.sprk_responseduedate`
  (`fa475e8f-55bf-f111-a05b-3833c5e9614d`). **Kept**: once the whole table was removed, each was added back as its own
  attribute component (type 2).
- **Task 007**: every column was on the five ontology tables. The lookups from them to foreign tables (for example
  `sprk_decisionrecord.sprk_workassignment`) are attributes and relationships of **our** tables, which the solution holds
  with rcb 0, so they are unaffected.
- **Task 047** (D-58): `sprk_workassignment.sprk_respondedon` / `sprk_responseoutcome` went to **`SpaarkeCore`** by
  design, never to this solution.
- Seed rows (task 004) are data, not components.

## Method

Script (scratchpad, not committed; one-off): dry run first, then `-Apply`.

1. For each of the 11 tables: `POST /api/data/v9.2/RemoveSolutionComponent` with
   `{ SolutionComponent: { "@odata.type": "Microsoft.Dynamics.CRM.solutioncomponent", solutioncomponentid: <entity MetadataId> }, ComponentType: 1, SolutionUniqueName: "OntologyPlatformSolution" }`.
   Gotcha: the action reads that id as the component's **object id** (the entity MetadataId). Passing the
   solutioncomponent **row** id fails with `0x8004f021 Cannot find solution component Entity <id>`. The first attempt
   did that and changed nothing.
2. For each of the 3 `sprk_servicerequest` columns: `POST AddSolutionComponent` with
   `{ ComponentId, ComponentType: 2, SolutionUniqueName, AddRequiredComponents: false }`.

All 14 calls returned HTTP 200. No publish was needed: solution membership is not a customization. `PublishAllXml` was
never used. **Nothing was deleted**: `RemoveSolutionComponent` drops only the solution's reference.

## Inventory AFTER (read back live)

| Type | rcb | Component |
|---|---|---|
| 1 | 0 | `sprk_budgetrevision` |
| 1 | 0 | `sprk_decisionrecord` |
| 1 | 0 | `sprk_policy` |
| 1 | 0 | `sprk_policyversion` |
| 1 | 0 | `sprk_signal` |
| 1 | 1 | `systemuser` (unchanged) |
| 1 | **2** | `sprk_servicerequest` — re-added **by the platform** as a shell ("do not include subcomponents") when its attributes were added; it carries only the three rows below |
| 2 | — | `sprk_servicerequest.sprk_direction` |
| 2 | — | `sprk_servicerequest.sprk_disposition` |
| 2 | — | `sprk_servicerequest.sprk_responseduedate` |

10 components. None of the 10 foreign tables removed whole is still in the solution.

## The tables themselves are untouched

Each of the 11 still exists, and each is still in its home solutions (for example `sprk_matter`: SpaarkeCore,
spaarke_core, SpaarkeMaster, MatterRibbons, …; `sprk_workassignment`: SpaarkeCore, SpaarkeMaster, WorkAssignmentRibbons,
…). One table has a thin home: `sprk_servicerequest` is otherwise only in **SpaarkeMaster** (not SpaarkeCore). That
pre-dates this work and is noted for the owner, not changed.

## Consequence for export

An export of `OntologyPlatformSolution` now **depends on** the foreign tables (missing-dependency check at import)
instead of **shipping** them. The target environment must already have the Spaarke core solutions. That is the intended
layering.
