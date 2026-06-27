# G — Chart Definition Deployment Notes

> **Task**: R4-080 (Phase 2 Wave 2a) — G workstream
> **Date**: 2026-06-10
> **Spec**: FR-31 through FR-36
> **Status**: Authoring complete; live deployment deferred to task-execute live run

---

## What this task delivered

1. Four `sprk_chartdefinition` upsert payloads:
   - `infrastructure/dataverse/charts/upcoming-todos-matter.json`
   - `infrastructure/dataverse/charts/upcoming-todos-project.json`
   - `infrastructure/dataverse/charts/upcoming-todos-invoice.json`
   - `infrastructure/dataverse/charts/upcoming-todos-workassignment.json`
2. Idempotent deploy script: `scripts/Create-UpcomingTodosChartDefinitions.ps1`
3. These notes.

The deploy script does NOT touch this DOes not get run during task R4-080 itself — the file deliverables ARE the task. Live deployment happens here at task-execute live run time (or by the user before tasks R4-081 through R4-084 can mount the cards on parent forms).

---

## Prerequisites

1. **`sprk_chartdefinition` entity present** in the target environment, with these fields available (all confirmed in spaarkedev1 as of R4-003 spike date):
   - `sprk_name` (Primary Name; string)
   - `sprk_entitylogicalname` (string)
   - `sprk_contextfieldname` (string) — VisualHost-binding contract field
   - `sprk_drillthroughtarget` (string) — VisualHost-binding contract field
   - `sprk_visualtype` (picklist, global option set `sprk_visualtype`; value `100000009` = "Due Date Card List")
   - `sprk_fetchxmlquery` (memo) — the live field name (NOT `sprk_fetchxml`)
2. **Azure CLI** logged in (`az login`) for an account with Dataverse Web API write permissions in the target env.
3. **PowerShell 7** (`pwsh`).
4. (Optional) `DATAVERSE_URL` environment variable set to the target env URL.

---

## Live deploy command (target = spaarkedev1)

From the repo root, run:

```powershell
# Option A — env var
$env:DATAVERSE_URL = "https://spaarkedev1.crm.dynamics.com"
pwsh -File scripts/Create-UpcomingTodosChartDefinitions.ps1

# Option B — pass explicitly
pwsh -File scripts/Create-UpcomingTodosChartDefinitions.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

# Dry-run first (recommended; no Web API writes)
pwsh -File scripts/Create-UpcomingTodosChartDefinitions.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -DryRun
```

Expected exit code: `0` on success; `1` on any record failure.

---

## Expected output (live run, first time)

```
========================================
 Create Upcoming To Dos chart definitions
========================================

Environment: https://spaarkedev1.crm.dynamics.com
Source dir : .../infrastructure/dataverse/charts
Files found: 4

Getting access token via Azure CLI...
  Token acquired

----- upcoming-todos-invoice.json -----
  Name: Upcoming To Dos — Invoice
  Context field: sprk_regardinginvoice
  Drill target : sprk_smarttodo.html
  Visual type  : 100000009
  Creating new record ...
  CREATED id=<guid>
  VERIFIED

(... three more identical blocks for matter / project / workassignment ...)

========================================
 Summary
========================================
File                                    Result Id
----                                    ------ --
upcoming-todos-invoice.json             OK     <guid>
upcoming-todos-matter.json              OK     <guid>
upcoming-todos-project.json             OK     <guid>
upcoming-todos-workassignment.json      OK     <guid>

Done.
```

Re-running the script later updates the same records (matched by `sprk_name`) — no duplicates.

---

## Verification

### Quick check — count + names

```powershell
pwsh -File scripts/query-chartdefinitions.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"
```

Look for the 4 records named:

- `Upcoming To Dos — Matter`
- `Upcoming To Dos — Project`
- `Upcoming To Dos — Invoice`
- `Upcoming To Dos — Work Assignment`

### Deep check — every contract field via Web API

Replace `<GUID>` with each record's id (captured from script output, or via the query script).

```bash
# Pseudo-curl form (substitute your bearer token)
GET https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_chartdefinitions(<GUID>)?$select=sprk_name,sprk_entitylogicalname,sprk_contextfieldname,sprk_drillthroughtarget,sprk_visualtype,sprk_fetchxmlquery
Authorization: Bearer <token>
```

Or via PowerShell:

```powershell
$env = "https://spaarkedev1.crm.dynamics.com"
$token = az account get-access-token --resource $env --query accessToken -o tsv
$headers = @{
    Authorization = "Bearer $token"
    Accept = "application/json"
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
}
$select = "sprk_name,sprk_entitylogicalname,sprk_contextfieldname,sprk_drillthroughtarget,sprk_visualtype,sprk_fetchxmlquery"
$filter = "startswith(sprk_name,'Upcoming To Dos ')"
$uri = "$env/api/data/v9.2/sprk_chartdefinitions?`$select=$select&`$filter=$([uri]::EscapeDataString($filter))"
(Invoke-RestMethod -Uri $uri -Headers $headers).value | Format-Table sprk_name, sprk_contextfieldname, sprk_drillthroughtarget, sprk_visualtype
```

Per-record expected values:

| sprk_name | sprk_entitylogicalname | sprk_contextfieldname | sprk_drillthroughtarget | sprk_visualtype |
|---|---|---|---|---|
| Upcoming To Dos — Matter | sprk_todo | sprk_regardingmatter | sprk_smarttodo.html | 100000009 |
| Upcoming To Dos — Project | sprk_todo | sprk_regardingproject | sprk_smarttodo.html | 100000009 |
| Upcoming To Dos — Invoice | sprk_todo | sprk_regardinginvoice | sprk_smarttodo.html | 100000009 |
| Upcoming To Dos — Work Assignment | sprk_todo | sprk_regardingworkassignment | sprk_smarttodo.html | 100000009 |

All four records share the same FetchXML (the parent-record filter on `sprk_contextfieldname` is added by VisualHost at runtime; the FetchXML in the chart def covers the time-bounded, "still active", or "pinned" filter only).

---

## Rollback (delete) procedure

If you need to remove the 4 records (e.g., to re-author the contract):

```powershell
$env = "https://spaarkedev1.crm.dynamics.com"
$token = az account get-access-token --resource $env --query accessToken -o tsv
$headers = @{
    Authorization = "Bearer $token"
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
}

$names = @(
    "Upcoming To Dos — Matter",
    "Upcoming To Dos — Project",
    "Upcoming To Dos — Invoice",
    "Upcoming To Dos — Work Assignment"
)

foreach ($n in $names) {
    $escaped = $n.Replace("'", "''")
    $uri = "$env/api/data/v9.2/sprk_chartdefinitions?`$filter=sprk_name eq '$escaped'&`$select=sprk_chartdefinitionid"
    $records = (Invoke-RestMethod -Uri $uri -Headers $headers).value
    foreach ($r in $records) {
        $delUri = "$env/api/data/v9.2/sprk_chartdefinitions($($r.sprk_chartdefinitionid))"
        Invoke-RestMethod -Uri $delUri -Headers $headers -Method DELETE
        Write-Host "Deleted $n ($($r.sprk_chartdefinitionid))"
    }
}
```

> **Warning**: only run rollback if you have explicit user direction. The 4 records are consumed by tasks 081–084 (Visual Host control mounts on Matter/Project/Invoice/WorkAssignment forms); deleting them breaks the cards.

---

## Hand-off to tasks 081–084 (form-add tasks)

The form-add tasks each mount a VisualHost control instance on a parent form and bind it to ONE of these 4 chart defs by ID. After running the deploy script:

1. Capture the 4 chart-def IDs from the script summary (or from the verification query above).
2. Provide them to tasks 081–084 via either:
   - a runtime lookup by `sprk_name` (preferred — env-portable per NFR-03), OR
   - manual recording into the task's worksheet (avoid hardcoding GUIDs in source).

Drill-through behavior (per R4-003 spike):

- VisualHost reads `sprk_drillthroughtarget = "sprk_smarttodo.html"`.
- It auto-injects a `data` envelope with keys `entityName`, `filterField` (the value of `sprk_contextfieldname`), `filterValue` (the current parent record ID), and `mode=dialog`.
- It opens the Code Page via `Xrm.Navigation.navigateTo({pageType:"webresource", webresourceName:"sprk_smarttodo.html", data: "<envelope>"}, {target:2, ...dialog options})`.
- SmartTodo's `useLaunchContext` hook MUST be extended (per task R4-034) to recognize the VisualHost wire-format keys and translate `filterField=sprk_regardingmatter` → `regardingType=sprk_matter` for Kanban filter.

---

## Schema gotcha (resolved)

The task POML and spec FR-32 use the shorthand "fetchxml" as the field name. The **actual Dataverse column logical name is `sprk_fetchxmlquery`** (confirmed via `src/client/pcf/VisualHost/control/services/ConfigurationLoader.ts:77` and `src/client/pcf/VisualHost/control/types/index.ts:102`). Both the JSON payloads and the deploy script use the correct name.

---

## Risks / open items for tasks 081–084

| Risk | Mitigation |
|---|---|
| `sprk_smarttodo` web resource registered without `.html` suffix in target env | VisualHost branches on `.html` / `.htm` extension. If suffixless, the deploy notes in the chart def need updating. R4-003 spike §7 documents this; deploy the SmartTodo Code Page as `sprk_smarttodo.html`. |
| Old `UPCOMING TASKS` chart def (`154bd4a4-f359-f111-a825-3833c5d9bcab`) still on Matter form alongside the new "Upcoming To Dos — Matter" | Tasks 081–084 should REMOVE the old `UPCOMING TASKS` visual host instance from the Matter form before/after adding the new one. NOT in scope for R4-080. |
| `sprk_fetchxmlquery` column not present in target env (older env clone) | The deploy script will error with "The property does not exist". If you hit this, run `scripts/Add-ChartDefinitionAttributes.ps1` first (it adds the fetchxml column among others). |
| Statuscode option set 659490001 ("In Progress") absent in target env (pre-R3 clone) | Confirm via `Get-EntityDefinition statuscode` on sprk_todo. R3 task 009 customized this. |

---

*Notes author: task-execute / R4-080 / 2026-06-10*
