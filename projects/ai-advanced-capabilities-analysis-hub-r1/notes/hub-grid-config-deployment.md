# Analysis Hub Grid — Deployment Note

> **Scope**: Task 030 (`AnalysisHubWidget`, spec FR-10). The hub's Analysis grid is the
> shared `<DataverseEntityViewWidget>` (which itself renders `<DataGrid configId=… />`),
> pointed at a `sprk_gridconfiguration` row over `sprk_analysis`. This mirrors the pattern
> `ai-spaarke-ai-workspace-UI-r1`'s `entity-view-widget-deployment.md` established for the
> 6 other `ENTITY_VIEW_CONFIG_IDS` entries — read that note for the general recipe; this
> note covers the Analysis-specific view set.
>
> **This note must be actioned before the hub's grid renders real data in any
> environment.** The widget falls back to a clear empty state when `configId` resolves to
> an unknown record — no production crash — but no records will list until the config
> record is seeded and the placeholder constant is replaced.

## Current state (as of task 030, 2026-07-29)

`AnalysisHubWidget.tsx` declares:

```ts
const ANALYSIS_HUB_GRID_CONFIG_ID = '00000000-0000-0000-0000-000000000000'; // PLACEHOLDER
```

No `sprk_gridconfiguration` row over `sprk_analysis` exists yet in any environment. Handed to
task 071 (deploy) to seed + replace this constant.

## ✅ SEEDED in spaarkedev1 (2026-07-29, front-door wiring)

All records below created via the Dataverse MCP against `spaarkedev1`. The widget constant is now
baked: `ANALYSIS_HUB_GRID_CONFIG_ID = 'e7c8126a-968b-f111-8077-7ced8ddc4a05'`.

| Artifact | Name | GUID |
|---|---|---|
| savedquery (default) | Analysis Hub — All Analyses | `f6252a4e-968b-f111-8077-7ced8ddc4a05` |
| savedquery | Analysis Hub — Agreement Review (`sprk_worktype=100000000`) | `50130256-968b-f111-8077-7ced8ddc4a05` |
| savedquery | Analysis Hub — Legal Research (`sprk_worktype=100000001`) | `53130256-968b-f111-8077-7ced8ddc4a05` |
| savedquery | Analysis Hub — Patent Application (`sprk_worktype=100000002`) | `58130256-968b-f111-8077-7ced8ddc4a05` |
| sprk_gridconfiguration | Analysis Hub (→ sprk_analysis; `source.availableViews` allowlist = the 4 views above) | `e7c8126a-968b-f111-8077-7ced8ddc4a05` |
| sprk_workspacelayout | Analysis (single-column, sectionId `analysis`, isSystem=true, sortOrder 11) | `666ce576-968b-f111-8077-7ced8ddc4a05` |

Columns on all 4 views: `sprk_name`, `sprk_worktype`, `statuscode`, `createdon`. The gridconfig's
`availableViews` allowlist scopes the DataGrid `ViewSelector` dropdown to exactly these 4 (not every
sibling `sprk_analysis` savedquery). Views are live immediately (no publish needed — the DataGrid
framework reads `savedquery` records directly via the BFF/Web API).

## What must be created (one row + four saved queries)

Unlike the 6 pre-existing `ENTITY_VIEW_CONFIG_IDS` entries (each pointing at ONE saved query), the
hub's "view by type" dropdown requires the config's entity to have **FOUR sibling saved queries** —
the DataGrid framework's own `ViewSelector` (in `DataGrid.tsx`) automatically surfaces every sibling
saved query for the resolved entity as a dropdown, no code change needed once they exist:

| # | View name | FetchXML filter | Purpose |
|---|---|---|---|
| 1 | "All Analyses" | (none — every `sprk_analysis` row) | Default view; the config's `source.savedQueryId` |
| 2 | "Agreement Review Analyses" | `sprk_worktype eq 100000000` | Filters to Agreement Review work type |
| 3 | "Legal Research Analyses" | `sprk_worktype eq 100000001` | Filters to Legal Research work type |
| 4 | "Patent Application Analyses" | `sprk_worktype eq 100000002` | Filters to Patent Application work type |

### Row creation steps

1. In Power Apps maker (`make.powerapps.com`), create the 4 saved queries (views) above over the
   `sprk_analysis` entity, if they don't already exist as part of the entity's standard views.
2. Create ONE new `sprk_gridconfiguration` row:
   - `sprk_name` — e.g. `Analysis Hub`.
   - `sprk_entitylogicalname` — `sprk_analysis`.
   - `sprk_configjson` — see template below. `source.savedQueryId` points at view #1 ("All
     Analyses") — the DEFAULT view shown on first render. Views 2–4 do NOT need to be listed
     explicitly in `sprk_configjson` unless an `availableViews` allowlist is later added to narrow
     the dropdown (see `DataGrid.tsx`'s `availableViewsAllowlist` prop / FR-05); by default DataGrid
     surfaces ALL sibling saved queries for the entity.
3. Save and copy the row GUID.
4. Replace `ANALYSIS_HUB_GRID_CONFIG_ID` in
   `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/AnalysisHubWidget.tsx` with the real
   GUID.
5. Commit the constant update as part of the deployment PR.

### Minimal `sprk_configjson` template

```json
{
  "_version": "1.0",
  "source": {
    "type": "savedquery",
    "savedQueryId": "<ALL-ANALYSES-SAVEDQUERY-GUID>"
  },
  "display": {
    "title": "Analyses"
  }
}
```

The DataGrid framework derives columns, sort, and filter chips from the saved query's `layoutXml`
automatically (see `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`). Optional richer
keys (`columns`, `filterChips`, `commandBar`) can be added later without code changes — see
`docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md`.

## Verification

After replacing the placeholder GUID and rebuilding:

1. Open SpaarkeAi and navigate to the Analysis hub tab (`'analysis-hub'` widget type — reached via
   task 050's entry routing once that task lands).
2. Confirm the three "Create new" cards render (Agreement Review actionable; Legal Research +
   Patent Application disabled with "Coming soon").
3. Confirm the grid below lists existing `sprk_analysis` records.
4. Open the view dropdown (top-left of the grid chrome) and confirm all four views are selectable;
   picking a work-type view narrows the rows; picking "All Analyses" restores the full set.

If the grid renders an empty state ("No grid configuration was supplied" / "Dataverse is
unavailable"), either the placeholder constant was not replaced or the row/entity name mismatches —
check the constant against the real Dataverse row.

## Future work

- If a maker-facing "view by type" filter chip (rather than the full ViewSelector menu) is
  preferred later, the DataGrid framework's `behavior.filterChips` config key can add one without
  a code change.
- Task 050 (entry routing) wires `dataService`/`authenticatedFetch`/`navigationService`/
  `searchUsers` into the Agreement Review card's `create-analysis-wizard` dispatch so the wizard
  becomes fully operational (see `notes/task-030-deviations.md` §4).
