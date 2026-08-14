# R2 grid-widget "empty grids" — root-cause diagnosis + fix (2026-08-07)

> Empirically diagnosed from App Insights (`spe-insights-dev-67e2xz`) + live Dataverse (MCP).
> The handoff hypothesis ("client-side shared grid path") was WRONG — the real causes are in the
> **Dataverse config records** (wrong attribute names) and the **BFF Tier-2 scoping architecture**
> (page-1-then-filter) + a **data-model gap** (invoices not project-linked).

## Method (empirical, per §F.3)
App Insights `requests` + `traces` + `exceptions` for the two UAT sessions (workforce systemuser
`1d02f31c`, 16 projects; CIAM hotmail contact). Confirmed: real BFF calls fire (not mock), principal
resolves correctly (16 accessible projects, `SystemUserMembership=True, ContactGrants=True`).

## Per-grid ground truth (from logs)
| Grid | Fetch HTTP | Tier-2 result | Cause |
|---|---|---|---|
| **Projects** | **500** | never scoped | inline FetchXML selects `sprk_name` — **does not exist** on `sprk_project` |
| **Matters** | **500** | never scoped | inline FetchXML selects `sprk_mattertitle` — **does not exist** on `sprk_matter` |
| **Documents** | 200 | **0/25** | valid query, but only 49/828 docs are project-linked; unfiltered page-1 (25 by `createdon desc`) is nearly all null-project → all dropped; paging disabled (`MoreRecords` forced false) |
| **Invoices** | 200 | **0/9** | invoices link to `sprk_matter`, **not** `sprk_project` (0/9 project-linked) → project-scoping can NEVER match |
| **Work Assignments** | 200 | **4/21** ✓ | works — linked via `sprk_regardingproject` |

## Real schema (verified via MCP)
- `sprk_project`: name=`sprk_projectname` (NOT `sprk_name`), ref=`sprk_projectnumber` (NOT `sprk_referencenumber`), status=`statuscode` (NOT `sprk_status`).
- `sprk_matter`: name=`sprk_mattername` (NOT `sprk_mattertitle`); `sprk_matternumber` + `statuscode` were correct.
- Total active projects = 16 (all accessible to the systemuser; ≤ pageSize 25 → all fit page 1).

## ✅ UAT VERIFIED (2026-08-07, both planes)
| | Organization (workforce, ralph@spaarke.com) | Partner (CIAM, ralph@hotmail) |
|---|---|---|
| Projects | 16 (all) | 2 (granted) |
| Documents | 49 (all project-linked) | 14 (across the 2 projects) |
| Work Assignments | 5 | 4 |
| Matters | 0 (coming soon) | — |
| Invoices | 0 (deferred — links to `sprk_matter`, not `sprk_project`) | 0 |

Server-side scoping confirmed correct on both planes (workforce sees all project-linked records; partner sees the strict per-grant subset).

## Fixes

### ✅ DONE (deploy-free — live Dataverse config-record data fix, 2026-08-07)
Updated `sprk_gridconfiguration.sprk_configjson` for:
- **Projects** `61711823-1092-f111-b8dc-7ced8ddc4a05` — `sprk_name`→`sprk_projectname`, `sprk_referencenumber`→`sprk_projectnumber`, `sprk_status`→`statuscode` (fetchXml + layoutXml + columns + order + jump). → Projects grid now returns 16 rows.
- **Matters** `583a2a33-1092-f111-b8dc-7ced8ddc4a05` — `sprk_mattertitle`→`sprk_mattername` (fetchXml + layoutXml + jump + order). → fetch now succeeds; Tier-2 predicate is `EmptyRecordIds` by design (D-016-1) so it shows the clean "Matter-level workspace access is coming soon" empty state instead of a 500.

No repo seed file authors these records (task 016 created them directly in Dataverse), so nothing regresses on redeploy. The DataGrid reads the config record uncached per mount → effective immediately.

### ⏳ RECOMMENDED (needs decision — BFF code + redeploy)
**Documents** — the module-data seam (`ExternalModuleDataEndpoints.ExecuteScopedFetchAsync`) executes the caller's FetchXML app-only **unfiltered** (one page), then filters in-memory (`ExternalModuleDescriptor.ScopeRows`). For a sparse/ large table (documents 49/828 linked) the accessible rows are almost never on page 1, and paging is disabled. **Proper fix**: inject a server-side `<filter>` on the module's `RecordIdAttribute IN (accessibleIds)` into the FetchXML **before** execution, so Dataverse returns only accessible rows (paging works, no in-memory drop). Benefits ALL child modules (documents/invoices/work-assignments/projects). Empty-set case (matters) → inject an impossible filter → 0 rows cleanly. This touches the security-scoping path → surface per §10/§6.

**Invoices** — deeper data-model gap: invoices carry `sprk_matter`, not `sprk_project` (0/9 project-linked). Project-based Tier-2 scoping can never surface them. Needs the Contact→Organization→Matter access model that D-016-1 explicitly deferred ("Matter-level access coming soon"). Recommend deferring invoices to that matter-access work, OR scoping invoices via `matter → project` join once matter-access exists.
