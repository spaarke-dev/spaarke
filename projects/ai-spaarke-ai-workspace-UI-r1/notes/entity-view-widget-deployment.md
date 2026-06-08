# Dataverse Entity-View Widget — Deployment Note

> **Scope**: Objective #4 of `ai-spaarke-ai-workspace-UI-r1`. The shared
> `<DataverseEntityViewWidget>` is consumed by 4 system widgets (Documents,
> Projects, Invoices, Work Assignments) — each backed by a
> `sprk_gridconfiguration` Dataverse row created by an operator.
>
> **This note must be actioned before the widgets render usefully in any
> environment.** The widget falls back to a clear empty state when the
> `configId` resolves to an unknown record — no production crash — but no
> records will list until each configId points to a real row.

## spaarkedev1 — wired (2026-06-08)

| Entity | sprk_gridconfiguration GUID | Friendly name | Origin |
|---|---|---|---|
| `sprk_document` | `d99a4352-4913-f111-8343-7ced8d1dc988` | Semantic Search Documents View | Pre-existing |
| `sprk_project` | `97ee98e7-7a63-f111-ab0c-70a8a53ec687` | Active Projects (Workspace) | Created 2026-06-08 |
| `sprk_invoice` | `d021827b-9b5e-f111-ab0c-7c1e521545d7` | Invoice Matter Budget Performance | Pre-existing |
| `sprk_workassignment` | `9c5b0ee7-7a63-f111-ab0c-000d3a4d8152` | Active Work Assignments (Workspace) | Created 2026-06-08 |

All 8 placeholder constants in code have been replaced with these GUIDs.
The four entity-view widgets now render against real data in spaarkedev1.

**For other environments (e.g. prod, demo, hipc):** the operator steps below still
apply — each environment needs its own four rows + a follow-up commit replacing
the GUIDs for that branch / deployment target. Consider promoting these to
environment-specific config (or a `sprk_dashboardconfiguration` Dataverse entity)
in a follow-up if multi-env deploys become routine.

## What was built

| Surface | File | configId constant |
|---|---|---|
| Direct widget — Documents | `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts` | `ENTITY_VIEW_CONFIG_IDS.documents` |
| Direct widget — Projects | same file | `ENTITY_VIEW_CONFIG_IDS.projects` |
| Direct widget — Invoices | same file | `ENTITY_VIEW_CONFIG_IDS.invoices` |
| Direct widget — Work Assignments | same file | `ENTITY_VIEW_CONFIG_IDS.workAssignments` |
| Dashboard section — Documents | `src/solutions/LegalWorkspace/src/sections/documents.registration.ts` | `DOCUMENTS_CONFIG_ID` |
| Dashboard section — Projects | `src/solutions/LegalWorkspace/src/sections/projects.registration.ts` | `PROJECTS_CONFIG_ID` |
| Dashboard section — Invoices | `src/solutions/LegalWorkspace/src/sections/invoices.registration.ts` | `INVOICES_CONFIG_ID` |
| Dashboard section — Work Assignments | `src/solutions/LegalWorkspace/src/sections/workAssignments.registration.ts` | `WORK_ASSIGNMENTS_CONFIG_ID` |

All 8 constants currently hold placeholder strings (`REPLACE-ME-…-CONFIG-ID`).

## Operator steps (one-time per environment)

For **each** of the 4 entities below, create a `sprk_gridconfiguration` Dataverse row
that wires the framework to the desired saved query:

| # | Entity logical name | Suggested view name | Suggested config name |
|---|---|---|---|
| 1 | `sprk_document` | Active Documents (or similar) | `My Documents (Workspace)` |
| 2 | `sprk_project` | Active Projects | `Projects (Workspace)` |
| 3 | `sprk_invoice` | Active Invoices | `Invoices (Workspace)` |
| 4 | `sprk_workassignment` | Default | `Work Assignments (Workspace)` |

### Row creation (per entity)

1. In Power Apps maker (`make.powerapps.com`), open the solution that
   contains `sprk_gridconfiguration` and create a new row.
2. Set:
   - `sprk_name` — friendly name from column 4 above.
   - `sprk_entitylogicalname` — entity logical name from column 2.
   - `sprk_configjson` — see template below. Replace `<SAVEDQUERY-GUID>` with
     the GUID of the entity's chosen view.
3. Save and copy the row GUID (visible in the URL bar or `Edit columns` Id).
4. Replace the corresponding placeholder constant in the source files (table above).
5. Commit the constant updates as part of the deployment PR.

### Minimal `sprk_configjson` template

```json
{
  "_version": "1.0",
  "source": {
    "type": "savedquery",
    "savedQueryId": "<SAVEDQUERY-GUID>"
  },
  "display": {
    "title": "My Documents"
  }
}
```

The DataGrid framework derives columns, sort, and filter chips from the saved
query's `layoutXml` automatically. Per `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`,
the framework's invalid-config guard silently falls back to metadata-derived
defaults if any field is missing — so this minimal config is enough to ship.

Optional richer keys (`columns`, `filterChips`, `commandBar`, `behavior.parentContextFilter`)
can be added later without code changes. See
`docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md` for the full menu.

### Resolving the saved-query GUID

For `sprk_document` (example):

```powershell
# Replace <ORG-URL> with your environment URL (e.g. https://spaarkedev1.crm.dynamics.com).
$req = "<ORG-URL>/api/data/v9.2/savedqueries?$filter=returnedtypecode eq 'sprk_document' and isuserdefined eq false and isdefault eq true&$select=savedqueryid,name"
# Use your preferred Web API client (pac, REST, Postman).
```

For non-default views (e.g. "Active Documents"), use:

```
$filter=returnedtypecode eq 'sprk_document' and name eq 'Active Documents'
```

## Verification

After replacing the 8 placeholder constants and rebuilding:

1. Open SpaarkeAi (`sprk_spaarkeai` Code Page).
2. From the Workspaces dropdown, select any layout that includes one of the
   four new sections (or create a custom layout via "+ New Workspace").
3. The section renders the chosen saved query's grid with the DataGrid
   framework's standard chrome (view selector, filter chips, command bar,
   lazy paging, column resize, sort).
4. From the Workspaces dropdown's "+ Add tab" (if/when the dispatcher is wired
   — see backlog), each Direct widget can be added as a standalone tab.

If a section renders an empty state ("DataGrid configuration not found"), the
placeholder constant for that section was not replaced. Check the constant
file and confirm the configId matches a real Dataverse row.

## Future work

- Wire dispatchers (Assistant pane, Context pane wizards) to launch the
  four new Direct widget types via `widget_load` events.
- Consider hoisting the four configIds into a single Dataverse-driven catalog
  so makers can add new entity-view widgets without code changes.
- Retire the legacy `DocumentsTab` component (still present in
  `src/solutions/LegalWorkspace/src/components/RecordCards/DocumentsTab.tsx`
  — no longer imported by `documents.registration.ts` but other call sites
  may exist; needs an orphan audit before deletion).
