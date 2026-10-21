# Task 002 — Grid columns per owner list (C4, FR-09): proposal

> **Status (2026-10-12): PROPOSAL ONLY (POML step 4, "write the new column JSON; show the diff and rollback to the owner").**
> Nothing was read from or written to live Dataverse. No code, no solution source, no TASK-INDEX, no POML status, no `current-task.md` was changed.
> **Owner decision (2026-10-11), replacing "the owner will send a list": "match columns to existing main entity views".** For each external tab the columns are those of the entity's default public (`isdefault=1`, `querytype=0`) view in `src/dataverse/solutions/SpaarkeMaster/Entities/<Entity>/SavedQueries/*.xml`.

## 0. Summary

| Tab | Source main view | Main-view columns | Proposed (allowed) | Excluded (outside the allow-list) |
|---|---|---|---|---|
| Projects | Active Projects `{195ab203-233e-4285-a34d-4acb5c0869ef}` | 6 | **2** | 4: sprk_projecttype_ref, sprk_practicearea, ownerid, createdon |
| Matters | Active Matters `{3ba2301f-692e-4a29-be2e-365c923b01dd}` | 5 | **2** | 3: sprk_mattertype, sprk_practicearea, createdon |
| Work Assignments | Active Work Assignments `{c8391ddf-35f3-4e10-a98c-06869362e01a}` | 4 | **1** | 3: sprk_assignedto, ownerid, createdon |
| Documents | Active Documents `{82d75343-f530-4743-ac74-c2d13e83b1c0}` | 4 | **2** | 2: **sprk_filepath (pointer column, hard-blocked)**, sprk_searchindexed |
| Invoices | Active Invoices `{2220a3bc-330e-4405-b441-3605df961878}` | 6 | **3** (Option A) | 3: statuscode, sprk_invoiceduedate, sprk_totalamount_base |
| Service Requests | Active Service Requests `{76636ba5-096f-4a5e-b9be-e6ae9a9f5871}` | 2 | **2** | 0 |

- Every entity has exactly one default public view, and each is named "Active …"; no ambiguity, so nothing is left for the owner to choose between views.
- **Totals: 12 columns proposed, 15 main-view columns excluded.** No allow-list change, BFF change or test change is needed for the proposal as written (Option A, §3.5).
- **The current column JSON exists only in live Dataverse** (§2, §6). The "current" and "rollback" sections below therefore hold the last-known repo snapshot (not authoritative) and a slot for the exact live value, to be captured before any write.
- The proposal changes only `source.layoutXml`, the `columns` map and (WA, Invoices) one `<attribute>` in `source.fetchXml`. Every other key of each record's JSON stays as it is.

## 1. How the records are built (code-verified, not from docs)

`docs/data-model/json-field-schemas.md` §12 shows a `columns` **array** with `logicalName/width/sortable` and a `features` block. **That is stale for this grid.** The shared `<DataGrid configId>` reads the v1.0 schema (`Spaarke.UI.Components/src/types/DataGridConfiguration.ts`; `components/DataGrid/configResolution.ts`):

- The **visible columns and their order and widths are the `<cell>` elements of `source.layoutXml`** (`parseLayoutColumns`). The first cell, and any cell equal to the entity's primary name attribute, becomes the open-record link.
- `columns` is a **map keyed by logical name** of per-column overrides (`label`, `width`, `renderer`, `align`, `hidden`, `tooltip`). It does not add or remove columns; it decorates cells that layoutXml already lists.
- `source.fetchXml` must select every layout column, or the cell renders blank. It also carries the hidden scope attributes the BFF needs (Documents: `sprk_project`, `sprk_matter`, `sprk_workassignment`; Invoices: `sprk_matter`, `sprk_project`; Service Requests: `sprk_requestedby`). Those stay in the FetchXML and are simply not in layoutXml.
- All six records are `source.type = "inline"` (task 134 §2, task 157 §3). The external SPA's grid never shows a view selector and never lists saved queries (task 157), so the main views are used only as the *source of the column list*, not at run time. The records do not reference a savedquery.
- `display`, `behavior` (`pageSize`), `commandBar`, `filterChips`, `rowOpen` and anything else in each record are **not touched**.

Cell rendering note (affects Option B only, §3.5). `renderCellValue` in `DataGrid.tsx` does not read `@formattedValues`; objects render as `.name` or `JSON.stringify`. The seam returns SDK-typed values (`FetchService.ProjectEntity`). Whether option-set (`sprk_invoicestatus`, `statuscode`) and Money cells already show a readable value on the live grids cannot be established from source. Every proposed Option A column is plain text or a datetime, so this does not affect Option A.

## 2. Where the current column JSON lives

| Tab | Widget file → `CONFIG_ID` (= `sprk_gridconfigurationid`) | In repo? |
|---|---|---|
| Projects | `ProjectsWidget.tsx` → `61711823-1092-f111-b8dc-7ced8ddc4a05` | **No** (live only) |
| Matters | `MattersWidget.tsx` → `583a2a33-1092-f111-b8dc-7ced8ddc4a05` | **No** (live only) |
| Work Assignments | `WorkAssignmentsWidget.tsx` → `42f4102c-1092-f111-b8dc-7ced8ddc4a05` | **No** (live only) |
| Documents | `DocumentsWidget.tsx` → `3af4102c-1092-f111-b8dc-7ced8ddc4a05` | **No** (live only) |
| Invoices | `InvoicesWidget.tsx` → `3ff4102c-1092-f111-b8dc-7ced8ddc4a05` | **No** (live only) |
| Service Requests | `ServiceRequestsWidget.tsx` → `403e5d37-cb94-f111-b8db-00224835447a` ("Internal — Service Requests (external widget)") | **No** (live only) |

Evidence that no repo file authors them: the R2 diagnosis states "No repo seed file authors these records (task 016 created them directly in Dataverse)" (`projects/spaarke-SPA-external-access-platform-r2/notes/grid-widget-empty-diagnosis.md:45`); a search of `scripts/` finds a seed only for the four *communication reconciliation* configs (`scripts/seed-reconciliation-gridconfig.ps1`); `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_GridConfiguration/` holds the table schema, forms and views only (a solution carries no row data). The six ids appear in the repo only in the widget files, `ExternalAccessModule.cs` comments and R2/UAC notes.

**What the repo does hold — a last-known snapshot, 2026-09-30/10-01, for reference only** (UAC-r2 tasks 134 and 157 read these records live, read-only; `ExternalModuleColumnAllowListTests.cs:662-671` and `LiveDocumentsGridFetchXml` pin the FetchXML):

| Tab | Last-known `source.fetchXml` (attributes, order) | Source |
|---|---|---|
| Projects | `sprk_projectid, sprk_projectname, sprk_projectnumber, statuscode, modifiedon`; order `sprk_projectname` asc | test fixture line 665 |
| Matters | `sprk_matterid, sprk_mattername, sprk_matternumber, statuscode`; order `sprk_mattername` asc | line 669 |
| Work Assignments | `sprk_workassignmentid, sprk_workassignmentnumber, sprk_priority, sprk_responseduedate, statuscode, sprk_regardingproject`; order `sprk_responseduedate` asc | line 668 |
| Documents | `sprk_documentid, sprk_documentname, sprk_documenttype, createdon, sprk_project, sprk_matter, sprk_workassignment`; order `createdon` desc | lines 78-82 |
| Invoices | `sprk_invoiceid, sprk_invoicenumber, sprk_invoicedate, sprk_invoicestatus, sprk_totalamount, sprk_project, sprk_matter`; order `sprk_invoicedate` desc | line 667 |
| Service Requests | `sprk_servicerequestid, sprk_servicerequestnumber, sprk_name, statuscode, createdon, sprk_requestedby`; order `createdon` desc | line 670 |

The snapshot has **no layoutXml, widths, labels or `columns` map**, and it may be stale (task 032 added creator stamps to the Documents allow-list; the Documents record might have been touched since). Treat it as a hint of what the grids probably show, never as the rollback.

## 3. Per tab

Allow-list = the module `ReadableColumns` in `src/server/api/Sprk.Bff.Api/Infrastructure/DI/ExternalAccessModule.cs` (lines 348-495, 691-711), which `ExternalModuleColumnAllowListTests.cs` pins (`LiveDerivedAllowLists`, lines 735-764; the `RuleBOnlyColumns` theory pins that the dropped columns stay refused). A column outside the list is refused with `DV_FETCHXML_COLUMN_NOT_PERMITTED` (400) if it is in the FetchXML, and stripped from the result otherwise. A column is also hard-blocked if it is in `ExternalModuleRegistry.PointerColumns`.

Notation for each "PROPOSED" block: it holds **only the keys that change**. The live procedure (§5) merges them into the live record JSON and leaves every other key as read.

### 3.1 Projects — `sprk_project`, config `61711823-1092-f111-b8dc-7ced8ddc4a05`

- **Source view:** `Entities/sprk_Project/SavedQueries/{195ab203-233e-4285-a34d-4acb5c0869ef}.xml`, "Active Projects" (default, qt 0). Its filter is `statecode = 0`, order `sprk_projectnumber` asc.
- **Main-view columns, in order:** `sprk_projectnumber` 150 · `sprk_projectname` 300 · `sprk_projecttype_ref` 200 · `sprk_practicearea` 200 · `ownerid` 200 · `createdon` 150.
- **Allow-list (5):** `sprk_projectid, sprk_projectname, sprk_projectnumber, statuscode, modifiedon`.
- **Allowed:** `sprk_projectnumber`, `sprk_projectname`.
- **Excluded:**
  - `sprk_projecttype_ref` — lookup to an internal reference table; dropped from the allow-list by task 157 (rule-(b)-only column).
  - `sprk_practicearea` — lookup to an internal reference table; dropped by task 157.
  - `ownerid` — the internal owning user/team (an internal staff identity); dropped by task 157.
  - `createdon` — a creation timestamp; not on the Projects list (dropped by task 157). Harmless in content but not allowed without an allow-list change.
- **CURRENT:** live only. Last-known FetchXML in §2; layoutXml and `columns` unknown. Read with §4.
- **PROPOSED** (FetchXML already selects both columns; no fetchXml change):
```json
{
  "source": {
    "layoutXml": "<grid name=\"resultset\" jump=\"sprk_projectnumber\" select=\"1\" icon=\"1\" preview=\"1\"><row name=\"result\" id=\"sprk_projectid\"><cell name=\"sprk_projectnumber\" width=\"150\" /><cell name=\"sprk_projectname\" width=\"300\" /></row></grid>"
  },
  "columns": {
    "sprk_projectnumber": { "label": "Project Number" },
    "sprk_projectname": { "label": "Project Name" }
  }
}
```
- **ROLLBACK:** the exact raw `sprk_configjson` read in §4 (slot below).

```text
ROLLBACK 3.1 (paste the raw sprk_configjson string, with modifiedon and versionnumber, here before any PATCH)
<not yet captured: live read pending>
```

### 3.2 Matters — `sprk_matter`, config `583a2a33-1092-f111-b8dc-7ced8ddc4a05`

- **Source view:** `Entities/sprk_Matter/SavedQueries/{3ba2301f-692e-4a29-be2e-365c923b01dd}.xml`, "Active Matters" (default, qt 0). Filter `statecode = 0`, order `sprk_matternumber` asc.
- **Main-view columns:** `sprk_matternumber` 100 · `sprk_mattername` 300 · `sprk_mattertype` 150 · `sprk_practicearea` 150 · `createdon` 150.
- **Allow-list (4):** `sprk_matterid, sprk_mattername, sprk_matternumber, statuscode`.
- **Allowed:** `sprk_matternumber`, `sprk_mattername`.
- **Excluded:** `sprk_mattertype` (lookup to an internal reference table; dropped by task 157), `sprk_practicearea` (same), `createdon` (not on the Matters list).
- **CURRENT:** live only (§2 for the last-known FetchXML).
- **PROPOSED** (FetchXML already selects both; no change):
```json
{
  "source": {
    "layoutXml": "<grid name=\"resultset\" jump=\"sprk_matternumber\" select=\"1\" icon=\"1\" preview=\"1\"><row name=\"result\" id=\"sprk_matterid\"><cell name=\"sprk_matternumber\" width=\"100\" /><cell name=\"sprk_mattername\" width=\"300\" /></row></grid>"
  },
  "columns": {
    "sprk_matternumber": { "label": "Matter Number" },
    "sprk_mattername": { "label": "Matter Name" }
  }
}
```
- **ROLLBACK:**
```text
ROLLBACK 3.2 (raw sprk_configjson, modifiedon, versionnumber)
<not yet captured: live read pending>
```

### 3.3 Work Assignments — `sprk_workassignment`, config `42f4102c-1092-f111-b8dc-7ced8ddc4a05`

- **Source view:** `Entities/sprk_WorkAssignment/SavedQueries/{c8391ddf-35f3-4e10-a98c-06869362e01a}.xml`, "Active Work Assignments" (default, qt 0). Filter `statecode = 0`, order `sprk_name` asc.
- **Main-view columns:** `sprk_name` 300 · `sprk_assignedto` 111 · `ownerid` 100 · `createdon` 125.
- **Allow-list (7):** `sprk_workassignmentid, sprk_name, sprk_workassignmentnumber, sprk_priority, sprk_responseduedate, statuscode, sprk_regardingproject`.
- **Allowed:** `sprk_name` only.
- **Excluded:**
  - `sprk_assignedto` — lookup (the assignee: an internal person); dropped by task 157; `RuleBOnlyColumns` and the "hand-crafted fetch selecting `sprk_assignedto` must 400" gate (task 157 §6.2) pin it as refused.
  - `ownerid` — internal owner; dropped by task 157.
  - `createdon` — not on the list.
- **Consequence for the owner:** the grid becomes **one column** (the name). The current grid probably shows number, priority, response-due date and status (§2 snapshot); the main view has none of them. See Q3.
- **FetchXML change needed:** the last-known FetchXML does **not** select `sprk_name` (the allow-list admits it only for the /record default projection). The cell would render blank. Add `<attribute name='sprk_name'/>`; it is allowed, so the guard still admits the query.
- **CURRENT:** live only (§2).
- **PROPOSED:**
```json
{
  "source": {
    "fetchXml": "<fetch><entity name='sprk_workassignment'><attribute name='sprk_workassignmentid'/><attribute name='sprk_name'/><attribute name='sprk_workassignmentnumber'/><attribute name='sprk_priority'/><attribute name='sprk_responseduedate'/><attribute name='statuscode'/><attribute name='sprk_regardingproject'/><order attribute='sprk_responseduedate' descending='false'/></entity></fetch>",
    "layoutXml": "<grid name=\"resultset\" jump=\"sprk_name\" select=\"1\" icon=\"1\" preview=\"1\"><row name=\"result\" id=\"sprk_workassignmentid\"><cell name=\"sprk_name\" width=\"300\" /></row></grid>"
  },
  "columns": {
    "sprk_name": { "label": "Name" }
  }
}
```
  The `fetchXml` shown is the 2026-09-30 snapshot plus the one `sprk_name` attribute. At update time the procedure inserts the attribute into the **live** FetchXML (§5); it does not overwrite the live FetchXML with this string. The order stays as it is (the main view orders by name; ordering is not part of the owner's rule).
- **ROLLBACK:**
```text
ROLLBACK 3.3 (raw sprk_configjson, modifiedon, versionnumber)
<not yet captured: live read pending>
```

### 3.4 Documents — `sprk_document`, config `3af4102c-1092-f111-b8dc-7ced8ddc4a05`

- **Source view:** `Entities/sprk_Document/SavedQueries/{82d75343-f530-4743-ac74-c2d13e83b1c0}.xml`, "Active Documents" (default, qt 0). Filter `statecode = 0`, order `createdon` desc.
- **Main-view columns:** `sprk_documentname` 300 · `sprk_filepath` 300 · `sprk_searchindexed` 127 · `createdon` 125.
- **Allow-list (9):** `sprk_documentid, sprk_documentname, sprk_documenttype, createdon, sprk_project, sprk_matter, sprk_workassignment, sprk_createdbyperson, sprk_createdbycontact`.
- **Allowed:** `sprk_documentname`, `createdon`.
- **Excluded:**
  - **`sprk_filepath` — a SPE/Graph pointer column.** It is in `ExternalModuleRegistry.PointerColumns` (the registry refuses to start with it on any list) and is the column task 134 was written to keep off the external plane (a contact must not read the storage path of every in-scope document). **Never add it, even if the owner asks.** A change would need an ADR-028 broker-only amendment, not a config edit.
  - `sprk_searchindexed` — an internal AI-search indexing flag (bit); not on the list.
- **CURRENT:** live only (§2). Task 032 may have added the two creator-stamp attributes to the record's FetchXML for delete-own; the live read decides.
- **PROPOSED** (FetchXML already selects both columns; no change):
```json
{
  "source": {
    "layoutXml": "<grid name=\"resultset\" jump=\"sprk_documentname\" select=\"1\" icon=\"1\" preview=\"1\"><row name=\"result\" id=\"sprk_documentid\"><cell name=\"sprk_documentname\" width=\"300\" /><cell name=\"createdon\" width=\"125\" /></row></grid>"
  },
  "columns": {
    "sprk_documentname": { "label": "Document Name" },
    "createdon": { "label": "Created On", "renderer": "datetime" }
  }
}
```
  The hidden scope attributes (`sprk_project`, `sprk_matter`, `sprk_workassignment`) stay in the FetchXML and out of the layout.
- **ROLLBACK:**
```text
ROLLBACK 3.4 (raw sprk_configjson, modifiedon, versionnumber)
<not yet captured: live read pending>
```

### 3.5 Invoices — `sprk_invoice`, config `3ff4102c-1092-f111-b8dc-7ced8ddc4a05`

- **Source view:** `Entities/sprk_Invoice/SavedQueries/{2220a3bc-330e-4405-b441-3605df961878}.xml`, "Active Invoices" (default, qt 0). Filter `statecode = 0`, order `sprk_name` asc.
- **Main-view columns:** `sprk_invoicenumber` 132 · `sprk_name` 300 · `statuscode` 119 · `sprk_invoicedate` 112 · `sprk_invoiceduedate` 139 · `sprk_totalamount_base` 158.
- **Allow-list (8):** `sprk_invoiceid, sprk_name, sprk_invoicenumber, sprk_invoicedate, sprk_invoicestatus, sprk_totalamount, sprk_project, sprk_matter`.
- **Allowed:** `sprk_invoicenumber`, `sprk_name`, `sprk_invoicedate`.
- **Excluded (strict reading):**
  - `statuscode` (state-reason) — not on the list. The external grid's own status column is a different attribute, `sprk_invoicestatus` (picklist), which is allowed.
  - `sprk_invoiceduedate` — a date; not on the list.
  - `sprk_totalamount_base` — the system-generated base-currency copy of the Money column `sprk_totalamount`. Not on the list; the allowed column is `sprk_totalamount`. The view shows the `_base` one, the grid has used the other.
- **FetchXML change needed:** the last-known FetchXML does not select `sprk_name`; add `<attribute name='sprk_name'/>` (allowed).
- **Option A (strict, the proposal):** 3 columns.
```json
{
  "source": {
    "fetchXml": "<fetch><entity name='sprk_invoice'><attribute name='sprk_invoiceid'/><attribute name='sprk_name'/><attribute name='sprk_invoicenumber'/><attribute name='sprk_invoicedate'/><attribute name='sprk_invoicestatus'/><attribute name='sprk_totalamount'/><attribute name='sprk_project'/><attribute name='sprk_matter'/><order attribute='sprk_invoicedate' descending='true'/></entity></fetch>",
    "layoutXml": "<grid name=\"resultset\" jump=\"sprk_name\" select=\"1\" icon=\"1\" preview=\"1\"><row name=\"result\" id=\"sprk_invoiceid\"><cell name=\"sprk_invoicenumber\" width=\"132\" /><cell name=\"sprk_name\" width=\"300\" /><cell name=\"sprk_invoicedate\" width=\"112\" /></row></grid>"
  },
  "columns": {
    "sprk_invoicenumber": { "label": "Invoice Number" },
    "sprk_name": { "label": "Name" },
    "sprk_invoicedate": { "label": "Invoice Date", "renderer": "date" }
  }
}
```
  (As in 3.3, the `fetchXml` is the snapshot plus one attribute; the procedure edits the live FetchXML, not this string.) Both `sprk_invoicenumber` and `sprk_name` render as links (first cell, and the entity's primary-name attribute); that is how the shared grid treats the main view's column order and is not a defect.
- **Option B (needs an owner decision; NOT proposed):** substitute the allowed twins of the two excluded columns, keeping the main view's order:
  `sprk_invoicenumber` 132 · `sprk_name` 300 · `sprk_invoicestatus` 119 · `sprk_invoicedate` 112 · `sprk_totalamount` 158 (the due date has no allowed twin and stays out). This adds no new allow-listed column, but it is a different column from the view's. It also puts an option-set and a Money cell on the grid, and §1 explains that source alone cannot show how those render; check the live Invoices grid first.
  Layout for B: `<grid name="resultset" jump="sprk_name" select="1" icon="1" preview="1"><row name="result" id="sprk_invoiceid"><cell name="sprk_invoicenumber" width="132" /><cell name="sprk_name" width="300" /><cell name="sprk_invoicestatus" width="119" /><cell name="sprk_invoicedate" width="112" /><cell name="sprk_totalamount" width="158" /></row></grid>`
- **CURRENT:** live only (§2).
- **ROLLBACK:**
```text
ROLLBACK 3.5 (raw sprk_configjson, modifiedon, versionnumber)
<not yet captured: live read pending>
```

### 3.6 Service Requests — `sprk_servicerequest`, config `403e5d37-cb94-f111-b8db-00224835447a`

- **Source view:** `Entities/sprk_ServiceRequest/SavedQueries/{76636ba5-096f-4a5e-b9be-e6ae9a9f5871}.xml`, "Active Service Requests" (default, qt 0). Filter `statecode = 0`, order `sprk_name` asc.
- **Main-view columns:** `sprk_name` 300 · `createdon` 125.
- **Allow-list (6):** `sprk_servicerequestid, sprk_servicerequestnumber, sprk_name, statuscode, createdon, sprk_requestedby`.
- **Allowed:** both. **Excluded:** none.
- **Note:** the module's scope attribute `sprk_requestedby` must stay in the FetchXML (hidden), as the last-known FetchXML has it.
- **CURRENT:** live only (§2).
- **PROPOSED** (FetchXML already selects both; no change):
```json
{
  "source": {
    "layoutXml": "<grid name=\"resultset\" jump=\"sprk_name\" select=\"1\" icon=\"1\" preview=\"1\"><row name=\"result\" id=\"sprk_servicerequestid\"><cell name=\"sprk_name\" width=\"300\" /><cell name=\"createdon\" width=\"125\" /></row></grid>"
  },
  "columns": {
    "sprk_name": { "label": "Name" },
    "createdon": { "label": "Created On", "renderer": "datetime" }
  }
}
```
- **ROLLBACK:**
```text
ROLLBACK 3.6 (raw sprk_configjson, modifiedon, versionnumber)
<not yet captured: live read pending>
```

### 3.7 Labels, renderers and widths

- Widths are the main view's, unchanged. Order is the main view's, unchanged.
- Labels and the explicit `renderer` values above are **proposals**. Where the live `columns` map already has an entry for a kept column, **keep the live entry** and ignore mine. Entries for columns that leave the layout are removed. Without a label the grid uses entity metadata, then a humanised logical name ("Projectnumber"), so a label is cheap insurance; the main view's own headers are the attribute display names, which I did not read.
- `createdon` is `datetime` (the view shows date and time); `sprk_invoicedate` is `date`, which also handles a Date Only value without a one-day shift (`renderCellValue`, task 098).

## 4. Live READ procedure (read-only; shown, NOT run)

Environment: Dataverse SPAARKE DEV 1, `https://spaarkedev1.crm.dynamics.com` (project CLAUDE.md §5). Reads only.

**Option 1 — Dataverse MCP (TDS):** `mcp__dataverse__read_query`
```sql
SELECT sprk_gridconfigurationid, sprk_name, sprk_entitylogicalname, sprk_configjson, modifiedon, versionnumber
FROM sprk_gridconfiguration
WHERE sprk_gridconfigurationid IN (
  '61711823-1092-f111-b8dc-7ced8ddc4a05',
  '583a2a33-1092-f111-b8dc-7ced8ddc4a05',
  '42f4102c-1092-f111-b8dc-7ced8ddc4a05',
  '3af4102c-1092-f111-b8dc-7ced8ddc4a05',
  '3ff4102c-1092-f111-b8dc-7ced8ddc4a05',
  '403e5d37-cb94-f111-b8db-00224835447a')
```
**Option 2 — Web API, one GET per id** (what the earlier read-only derivations used):
```powershell
$env  = 'https://spaarkedev1.crm.dynamics.com'
$tok  = az account get-access-token --resource $env --query accessToken -o tsv
$h    = @{ Authorization = "Bearer $tok"; Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }
$ids  = '61711823-1092-f111-b8dc-7ced8ddc4a05','583a2a33-1092-f111-b8dc-7ced8ddc4a05','42f4102c-1092-f111-b8dc-7ced8ddc4a05',
        '3af4102c-1092-f111-b8dc-7ced8ddc4a05','3ff4102c-1092-f111-b8dc-7ced8ddc4a05','403e5d37-cb94-f111-b8db-00224835447a'
foreach ($id in $ids) {
  Invoke-RestMethod -Headers $h -Uri "$env/api/data/v9.2/sprk_gridconfigurations($id)?`$select=sprk_name,sprk_entitylogicalname,sprk_configjson,modifiedon,versionnumber"
}
```
Record, per id: the raw `sprk_configjson` string (byte-exact), `modifiedon` and `versionnumber`. Paste the raw string into the matching ROLLBACK slot in §3. If `modifiedon` is later than 2026-08-13 or the JSON does not match §2, say so in the notes before proceeding (someone has edited the record since the UAC-r2 read).

## 5. Live UPDATE procedure (main session, AFTER owner approval; not run)

**Preconditions:** (1) the owner has approved the §3 JSON diff, with the answers to §7; (2) every ROLLBACK slot holds the live raw JSON (§4); (3) nothing else is editing these records. Project CLAUDE.md §5: owner approval and a stated rollback before every live change.

**Per record (6), a read-modify-write:**
1. `GET` the record again and compare `versionnumber` with the one captured in §4. If it differs, stop and re-capture.
2. Parse the raw `sprk_configjson` (PowerShell `ConvertFrom-Json -Depth 32`).
3. Set `source.layoutXml` to the §3 string.
4. For Work Assignments and Invoices only: insert `<attribute name='sprk_name'/>` into the live `source.fetchXml` if it is not already selected (edit the XML document; do not replace the string).
5. Rebuild `columns`: keep live entries for columns that stay in the layout, drop entries for columns that leave, add the §3 entry for any kept column that has none.
6. Leave every other key as read. Serialise (`ConvertTo-Json -Depth 32`). Before sending, compare every other top-level key (`_version`, `display`, `behavior`, `commandBar`, `filterChips`, `rowOpen`, `secondaryActions`, …) between the old and new objects and the unchanged members of `source`; any difference means the round-trip altered something, so stop.
7. Show the owner a before/after of the layout cells for that tab.
8. PATCH with `If-Match: *` so a wrong id fails instead of creating a record:
```powershell
$body = @{ sprk_configjson = $newJson } | ConvertTo-Json -Depth 5     # $newJson is the full JSON string from step 6
Invoke-RestMethod -Method Patch -Headers ($h + @{ 'Content-Type' = 'application/json'; 'If-Match' = '*' }) `
  -Uri "$env/api/data/v9.2/sprk_gridconfigurations($id)" -Body $body
```
   Request body per record: exactly `{"sprk_configjson":"<the whole updated JSON, escaped as a string>"}`. No other field is written.
9. Read it back and confirm the layout cells (and, for 3.3 and 3.5, the FetchXML attribute).

**Order and blast radius:** the grid reads the record uncached on every mount (R2 diagnosis), so each change is live at once on dev and no publish step is needed. Do one tab, check it (POML step 5: open the tab in the dev SPA, confirm the columns and order, and that rows load), then the next. A wrong layout column that the FetchXML does not select shows a blank column; a column outside the allow-list that is *also* in the FetchXML returns a 400 `DV_FETCHXML_COLUMN_NOT_PERMITTED` and the whole grid errors. Neither happens with the §3 JSON.

**Rollback:** `PATCH` the saved raw string back (`{"sprk_configjson":"<ROLLBACK raw string>"}`) to the same record; verify with a read and an open of the tab. Rolling back one tab does not affect the others.

**Verification in the browser (POML step 5):** the signed-in plane decides what is visible: workforce sees all six tabs, a CIAM partner does not see Service Requests (module `planes`, `ServiceRequestsWidget.tsx`). Check each tab's header cells against §3, the order, the widths, the open-record link on the first column, and the row count against the last-known count (R2 UAT: Projects 16, Documents 49 for the workforce user).

## 6. How the change is captured in solution source

- **The records are data, not solution components.** `SpaarkeMaster/Entities/sprk_GridConfiguration/` carries the table's schema, forms and views; `pac solution export` (dataverse-deploy Scenario 2) never contains rows of this table. So an update of the six rows **cannot** be "exported into the solution source", and POML step 4's "export the change into the solution source" cannot be satisfied literally. No `src/dataverse/**` file changes for this task.
- **Repo precedent for keeping record JSON in source:** `scripts/seed-reconciliation-gridconfig.ps1` plus four `*.gridconfiguration.json` files under `src/client/shared/Spaarke.Communication.Components/src/components/ReconciliationGrid/` (idempotent PATCH-or-create of the record from a JSON file).
- **Proposed capture (a list for the main session; NOT applied here):**
  1. Add the six final JSONs, as they stand live after the update, as `<tab>.gridconfiguration.json` files. Suggested home: `scripts/external-spa-gridconfigs/` (the folder is a proposal; there is no existing home for these).
  2. Add `scripts/seed-external-spa-gridconfig.ps1` modelled on `scripts/seed-reconciliation-gridconfig.ps1`: the six fixed ids above, PATCH `sprk_configjson` from the file with `If-Match: *`.
  3. Record the ids and the "live is the source of truth, the file is the capture" rule in the task notes. This is a new script (root CLAUDE.md §11: Existing: the reconciliation seed; Extension: copied, not extended, because it hard-codes the communication entity; Cost of doing nothing: the six rows exist in no source, as today, and a re-provisioned environment loses the owner's columns).
  This is optional and needs the owner's say-so (Q5). The owner's acceptance criterion (previous JSON saved in the notes as rollback) is met by §3 once the slots are filled.
- **Stale text to fix when the change lands (comments/fixtures only; no behaviour):**
  - `ExternalAccessModule.cs:433`, `:412`: "Primary name = sprk_name (not on the grid; …)" is no longer true for Work Assignments and Invoices after 3.3 and 3.5.
  - The `LiveGridFetchXml` rows for `sprk_workassignment` and `sprk_invoice` in `ExternalModuleColumnAllowListTests.cs` (lines 667-668) would be one attribute behind the live records; the test would still pass, since `sprk_name` is on both lists.
  - `docs/data-model/json-field-schemas.md` §12: the grid example (columns array, `features`) does not match `DataGridConfiguration.ts` (a `columns` map, layoutXml cells, `source`). Needs a docs fix (`docs-data-model` skill).

## 7. Escalations and open questions for the owner

**Escalation (allow-list). Columns the owner's rule selects that the external plane may not read. Not added.** Each would need a deliberate allow-list change in `ExternalAccessModule.cs` (+ `ExternalModuleColumnAllowListTests.cs`), i.e. a BFF change and a statement of what outside parties may read.

| Tab | Column | Why it matters |
|---|---|---|
| Documents | `sprk_filepath` | Pointer column to the stored file. Hard-blocked by `PointerColumns`; the BFF will not start with it on a list. Not a candidate for a "yes". |
| Documents | `sprk_searchindexed` | Internal AI-search flag; no meaning to an outside party. |
| Projects | `ownerid` | Names the internal owning user or team. |
| Work Assignments | `ownerid`, `sprk_assignedto` | Internal staff identities (the assignee and the owner). |
| Projects | `sprk_projecttype_ref`, `sprk_practicearea` | Lookups to internal reference records (the lookup's *name* would be returned). |
| Matters | `sprk_mattertype`, `sprk_practicearea` | Same. |
| Projects, Matters, Work Assignments | `createdon` | Low risk (a timestamp), but not on those three lists. |
| Invoices | `statuscode`, `sprk_invoiceduedate`, `sprk_totalamount_base` | Business data; the due date and the amount are plausible things for outside counsel to see, but they are not on the list. `statuscode`/`sprk_totalamount_base` have allowed twins (Option B). |

**Open questions:**
1. **Which of the excluded columns, if any, does the owner want added to the external allow-list** (a separate task with its own approval; `createdon`, `sprk_invoiceduedate` are the likeliest yes)? Default if no answer: none, Option A as written.
2. **Invoices: Option A (3 columns) or Option B** (`sprk_invoicestatus` and `sprk_totalamount` standing in for `statuscode` and `sprk_totalamount_base`)? Needs the live check of how those two cells render first (§1).
3. **Work Assignments become a one-column grid** (name only), and every other tab loses columns the grids now show (Projects 5 → 2, Matters 4 → 2, Documents loses its type column, Service Requests loses number, status and requested-by). Is that the intent of "match the main view", or should the allowed columns of the current grid that the main view does not have be kept (a different rule)? Default: follow the rule literally.
4. **Active filter.** The main views filter `statecode = 0`; the external FetchXMLs do not, and `statecode` is not on any allow-list (task 157 dropped it where it was), so a condition on it would be refused. Inactive records therefore remain visible in the external grids. The rule here is about columns only and the filter is not replicated. If the owner wants active-only, that is a separate allow-list/BFF decision.
5. **Capture in source** (§6): add the JSON files and a seed script, or accept "live is the source of truth, the task notes hold the rollback"?
6. **Ordering.** The main views sort by number/name; the grids keep their current sort (Projects by name, Documents newest first, Invoices by date, Service Requests newest first). Keep, or follow the views?

## 8. Acceptance criteria — status

| Criterion | Status |
|---|---|
| Each of the six tabs shows exactly the owner's listed columns, in order | Proposal ready (§3). Not applied. Verification is POML step 5 after approval. |
| No column outside the external allow-list is added | Met by construction: 12 proposed columns, each checked against `ReadableColumns` (§3); 15 excluded and listed (§7). |
| Previous column JSON for every changed record saved in the notes as the rollback | **Not yet met**: the current JSON is live only. Slots are in §3, to be filled by the live read in §4 before any write. |

## 9. What was and was not done

- Read: the POML, the project and root CLAUDE.md, `docs/data-model/json-field-schemas.md` §12, the six widget files, `GridWidgetBody.tsx`, the six default views, `ExternalAccessModule.cs`, `ExternalModuleColumnAllowListTests.cs`, `ExternalModuleRegistry.cs`, `DataGridConfiguration.ts`, `configResolution.ts`, `DataGrid.tsx` (renderers), `FetchService.cs`, UAC-r2 notes 134 and 157, R2 notes (grid diagnosis, 028), R3 task 050 notes, the dataverse-deploy skill.
- No Dataverse call of any kind (no MCP, no Web API, no `pac`). No git history or stash command. No file other than this one was created or changed.

## Live capture 2026-10-20 (owner OK'd the read-only read; Dataverse MCP read_query on dev)

The six `sprk_gridconfiguration` records as they are live today. This supersedes the §2 snapshot; it is the rollback baseline for any 002 change.

| Tab | Record (modifiedon) | Visible columns (layoutXml order, width, label/renderer) | Selected in fetchXml but hidden | Order |
|---|---|---|---|---|
| Projects | `61711823…` (2026-08-07) | Name 220 · Number 140 · Status 120 (badge) · Last Activity = modifiedon 140 (date) | — | name asc |
| Matters | `583a2a33…` (2026-08-10) | Name 240 · Number 150 · Status 120 (badge) | — | name asc |
| Work Assignments | `42f4102c…` (2026-08-06) | Number 160 · Priority 110 (badge) · Response Due 140 (date) · Status 120 (badge) | sprk_regardingproject | response due asc |
| Documents | `3af4102c…` (2026-08-12) | Name 260 · Type 150 (badge) · Created 140 (date) | sprk_project, sprk_matter, sprk_workassignment | created desc |
| Invoices | `3ff4102c…` (2026-10-17) | Number 160 · Invoice Date 130 (date) · Status = statuscode 130 (badge) · Total = sprk_totalamount 140 (currency, right) | sprk_project, sprk_matter | invoice date desc |
| Service Requests | `403e5d37…` (2026-08-12) | Number 150 · Name 280 · Status 120 (badge) · Submitted = createdon 140 (date) | sprk_requestedby | created desc |

All six records: `source.type = inline`; row open = formDialog; command bar shows Refresh and Export to Excel only (no New, Delete, Edit columns or Edit filters); page size 25 (Projects, Matters, WA) or 250 (others). Every visible column is on the server allow-list (Invoices `statuscode` was added 2026-10-17, ebilling D-41).
