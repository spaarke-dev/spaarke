# Task 030: Information fields, live-metadata check (FR-18a)

Environment: SPAARKE DEV 1 (`https://spaarkedev1.crm.dynamics.com`).

The Dataverse MCP server did not connect in this session, so Option A (`describe`) is not available. This task uses **Option B, Web API metadata**. Every call is a GET on `EntityDefinitions`.

## Candidates

"Already" means the column is in `ReadableColumns` today (`ExternalAccessModule.cs`, read 2026-11-23). Already-listed columns are read again only to confirm their type.

| Table | Candidates | Already allow-listed |
|---|---|---|
| sprk_matter | sprk_practicearea, sprk_assignedoutsidecounsel, sprk_matterdescription, sprk_assignedattorney1, sprk_assignedparalegal1, sprk_responsibleattorney (design: not found), sprk_opendate (design: not found), createdon | sprk_mattertype, modifiedon |
| sprk_project | sprk_matter, sprk_projectdescription, sprk_assignedattorney1, sprk_assignedparalegal1, sprk_startdate, sprk_duedate, sprk_practicearea, sprk_projectlead (design: not found) | sprk_projecttype_ref, modifiedon |
| sprk_workassignment | sprk_regardingmatter, sprk_assignedto, sprk_description, sprk_assignedby (design: not found), sprk_instructions (design: not found) | sprk_mattertype, modifiedon |
| sprk_invoice | sprk_invoicedescription, sprk_billingperiod (design: not found) | — |

## Command list (read-only), shown to the owner before any run

The script is `read-030-metadata.ps1` in the session scratchpad, run with `-OutDir projects/spaarke-SPA-external-access-platform-r4/notes/task-030-raw`. It does exactly this:

```powershell
$org = 'https://spaarkedev1.crm.dynamics.com'
$tok = az account get-access-token --resource $org --query accessToken -o tsv   # never printed
$h = @{ Authorization = "Bearer $tok"; Accept = 'application/json'; 'OData-Version' = '4.0' }
# For each table T in sprk_matter, sprk_project, sprk_workassignment, sprk_invoice, with filter
# F = "LogicalName eq 'c1' or LogicalName eq 'c2' ..." over T's candidates above:
Invoke-RestMethod -Headers $h -Uri "$org/api/data/v9.2/EntityDefinitions(LogicalName='T')/Attributes?`$select=LogicalName,AttributeType,IsSecured,IsValidForRead&`$filter=F"
Invoke-RestMethod -Headers $h -Uri "$org/api/data/v9.2/EntityDefinitions(LogicalName='T')/Attributes/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=LogicalName,Targets&`$filter=F"
# Each response body is saved as notes/task-030-raw/T.attributes.json and T.lookups.json
# (body only: no headers, no token). One line per candidate is printed: FOUND (type, secured, readable, targets) or NOT FOUND.
```

That is 8 GETs in total (2 per table). There are no writes, publishes, imports, or role or setting changes.

**Owner approval:** 2026-11-23, in the session ("Yes, run the read"), after the list above was shown. The run started at 22:14:59Z and made exactly the 8 GETs listed. Each request returned 200; there were no 401s or 403s.

## Results (live, 2026-11-23 22:15Z)

The raw responses are in `notes/task-030-raw/{table}.attributes.json` and `{table}.lookups.json`. They hold the body only, and a scan found no token, email or bearer string. Every FOUND column has `IsValidForRead = true`.

| Table | Column | Result | Type | Lookup target | IsSecured | Allow-listed today |
|---|---|---|---|---|---|---|
| sprk_matter | sprk_practicearea | FOUND | Lookup | sprk_practicearea_ref | false | no |
| sprk_matter | sprk_assignedoutsidecounsel | **NOT FOUND** → dropped | — | — | — | — |
| sprk_matter | sprk_matterdescription | FOUND | Memo | — | false | no |
| sprk_matter | sprk_assignedattorney1 | FOUND | Lookup | contact | false | no |
| sprk_matter | sprk_assignedparalegal1 | FOUND | Lookup | contact | false | no |
| sprk_matter | sprk_responsibleattorney | **NOT FOUND** → dropped | — | — | — | — |
| sprk_matter | sprk_opendate | **NOT FOUND** → dropped | — | — | — | — |
| sprk_matter | createdon | FOUND | DateTime | — | false | no |
| sprk_matter | sprk_mattertype | FOUND | Lookup | sprk_mattertype_ref | false | **yes** |
| sprk_matter | modifiedon | FOUND | DateTime | — | false | **yes** |
| sprk_project | sprk_matter | **NOT FOUND** → dropped | — | — | — | — |
| sprk_project | sprk_projectdescription | FOUND | Memo | — | false | no |
| sprk_project | sprk_assignedattorney1 | FOUND | Lookup | contact | false | no |
| sprk_project | sprk_assignedparalegal1 | FOUND | Lookup | contact | false | no |
| sprk_project | sprk_startdate | **NOT FOUND** → dropped | — | — | — | — |
| sprk_project | sprk_duedate | **NOT FOUND** → dropped | — | — | — | — |
| sprk_project | sprk_practicearea | FOUND | Lookup | sprk_practicearea_ref | false | no |
| sprk_project | sprk_projectlead | **NOT FOUND** → dropped | — | — | — | — |
| sprk_project | sprk_projecttype_ref | FOUND | Lookup | sprk_projecttype_ref | false | **yes** |
| sprk_project | modifiedon | FOUND | DateTime | — | false | **yes** |
| sprk_workassignment | sprk_regardingmatter | FOUND | Lookup | sprk_matter | false | no |
| sprk_workassignment | sprk_assignedto | FOUND | Lookup | contact | false | no |
| sprk_workassignment | sprk_description | FOUND | Memo | — | false | no |
| sprk_workassignment | sprk_assignedby | **NOT FOUND** → dropped | — | — | — | — |
| sprk_workassignment | sprk_instructions | **NOT FOUND** → dropped | — | — | — | — |
| sprk_workassignment | sprk_mattertype | FOUND | Lookup | sprk_mattertype_ref | false | **yes** |
| sprk_workassignment | modifiedon | FOUND | DateTime | — | false | **yes** |
| sprk_invoice | sprk_invoicedescription | **NOT FOUND** → dropped | — | — | — | — |
| sprk_invoice | sprk_billingperiod | **NOT FOUND** → dropped | — | — | — | — |

No found column is field-secured, so the IsSecured escalation does not fire.

**Dropped, NOT FOUND (11).** No substitute is proposed for any of these.
- matter: `sprk_assignedoutsidecounsel`, `sprk_responsibleattorney`, `sprk_opendate`;
- project: `sprk_matter`, `sprk_startdate`, `sprk_duedate`, `sprk_projectlead`;
- work assignment: `sprk_assignedby`, `sprk_instructions`;
- invoice: `sprk_invoicedescription`, `sprk_billingperiod`.

**Differs from design §5:**
- `sprk_assignedoutsidecounsel` was listed there as a real column. It does not exist on dev.
- Project `sprk_matter` and the project dates, marked "verify" there, do not exist either.

**Invoice:** neither candidate exists, so the invoice Information field gets no new column from this task.

## Owner decisions (2026-11-23, in the session: "Yes to both")

| Question | Answer |
|---|---|
| Should `createdon` stand in for the matter open date (`sprk_opendate` does not exist)? | **Yes.** Show `createdon` on the matter as the open date. |
| Should matter and project `sprk_assignedattorney1` be shown to partners? | **Yes** |
| Should matter and project `sprk_assignedparalegal1` be shown to partners? | **Yes** |
| Should work assignment `sprk_assignedto` be shown to partners? | **Yes** |

`sprk_assignedoutsidecounsel` was a planned question, but it does not exist, so nothing was asked about it.

Each "yes" still needs UAC's review per column in task 031 (spec FR-18; owner 2026-11-08).

## Final list for task 031

These columns are FOUND, approved by the owner and not secured. Task 031 adds them to `ReadableColumns`, with UAC's review recorded, and to the Information section.

| Type | New columns for 031 | Already allow-listed (keep) |
|---|---|---|
| Matter | `sprk_practicearea` (→ sprk_practicearea_ref), `sprk_matterdescription`, `sprk_assignedattorney1` (→ contact), `sprk_assignedparalegal1` (→ contact), `createdon` (shown as the open date) | `sprk_mattertype`, `modifiedon` |
| Project | `sprk_projectdescription`, `sprk_assignedattorney1` (→ contact), `sprk_assignedparalegal1` (→ contact), `sprk_practicearea` (→ sprk_practicearea_ref) | `sprk_projecttype_ref`, `modifiedon` |
| Work assignment | `sprk_regardingmatter` (→ sprk_matter), `sprk_assignedto` (→ contact), `sprk_description` | `sprk_mattertype`, `modifiedon` |
| Invoice | none (both candidates NOT FOUND) | — |

**For task 031's UAC review:**
- **Contact lookups.** The four contact lookups show an internal person's name through `@formattedValues`. 031 confirms from code that the module fetch returns the formatted name without reading `contact`.
- **`sprk_regardingmatter` on a work assignment.** It is a lookup to a root record, so its formatted name could name a matter the partner holds no grant for. This is a disclosure question that UAC decides in 031.

**Dropped (NOT FOUND, never substituted).** These 11 are listed under Results above.
