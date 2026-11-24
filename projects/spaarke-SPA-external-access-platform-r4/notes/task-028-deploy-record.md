# Task 028 — P2 deploy record (dev)

## Preconditions (2026-11-23)

| Item | State | Evidence |
|---|---|---|
| Dev BFF | `9df4e5c819`, live about 12:30Z 2026-11-23 (uac-r2 deploy, owner's go in uac-r2). uac-r2's 30-minute check runs until about 13:00Z. | uac-r2 live notice |
| #2941 (record-documents grid id admitted) | In `9df4e5c819` | `git merge-base --is-ancestor 9461dc0bb3 9df4e5c819` |
| ebilling 082c-2 (`sprk_reviewdetail` readable) | Live since BFF `5ae2804884` (task 020 notes) and still present | — |
| ebilling 093 file routes (#2839, `4f1192ab9d`) | In `9df4e5c819`, and in the previous BFF `c8ab2cba9e` | merge-base |
| Task 021 (invoice Files section) | Already live in SPA `2963d1565b` | merge-base |
| P2 SPA SHA to deploy | master `2dcbe35e34` (includes 020, 022, 023a–c, 024) | `git log origin/master`, checked 12:44Z |
| Current live SPA (rollback target) | `2963d1565b`, run 44441594640 (2026-11-19) | `gh run list` |

FR-11 and FR-13 can both be checked in this round; neither is deferred.

## Live changes

### A. Grid configuration row `286be8b4-f96f-4d5a-b1d5-82fd83ca9693` ("External record documents")

- **Owner go:** 2026-11-23, in this session ("Yes proceed").
- **Rollback (stated before the run):** delete the row by id. Nothing used it before the SPA deploy.
- `-LintOnly`: Definition OK (1497 of 20000 characters).
- `-WhatIf` (read-only): would CREATE; the row was absent.
- Apply at 12:44Z: `CREATED`, then `READ-BACK OK: row equals the definition.`
- `-Verify -RequireRow`: `IN SYNC`, exit 0.

### B. SPA deploy

- **Owner go:** 2026-11-23 ("Yes deploy"), on condition that uac-r2's check passed. uac-r2 reported GATE PASS at 13:01Z (window 12:32–12:59Z: 0 5xx, 0 exceptions).
- **Rollback (stated before the run):** redeploy `2963d1565b`.
- Master HEAD was re-checked just before dispatch: still `2dcbe35e34`.
- Run **45180011706** (`deploy-external-spa.yml`, environment dev, ref master, head `2dcbe35e34`) dispatched at 13:02Z: success, finished 13:04:40Z.
- **Served build check:** `https://green-dune-0c4f1221e.7.azurestaticapps.net/` answers 200. Its `/assets/app.js` (1,585,673 bytes) contains "No version history", "Use Upload above" and "No messages yet" (024) and the grid id `286be8b4-…` (023c).

### C. Invoices grid `3ff4102c` Review detail column

- **Owner go for the read-only read:** 2026-11-23 ("Yes run the read").
- **Read** (GET `$select=sprk_name,sprk_configjson,modifiedon,versionnumber`): name "Outside Counsel — Invoices (external widget)", modifiedon `2026-11-01T16:06:04Z`, versionnumber `29336457`, 1846 characters.
- **Matches task 020's expectations:**
  - `statuscode` is selected and in the layout;
  - `sprk_reviewdetail` is absent;
  - `sprk_name` and `sprk_totalamount_base` are present (R3).
- **`filterChips`:** absent, so the grid uses `auto` (`configResolution.ts:281`). `auto` would make a chip of the new option-set column, which the BFF refuses to filter on. The change switches to `denylist: ["sprk_reviewdetail"]`. `discoverDenylist` equals `discoverAuto` minus the listed names (`chipDiscovery.ts:159-183`), so the other chips are unchanged.
- **The change**, built offline from the saved copy with Node `JSON.parse`/`JSON.stringify`, 1846 → 2045 characters:
  1. fetch: `<attribute name='sprk_reviewdetail'/>` on `sprk_invoice`;
  2. layout: `<cell name="sprk_reviewdetail" width="200" />` right after `statuscode`;
  3. `columns.sprk_reviewdetail = { label: "Review detail" }`;
  4. `filterChips = { mode: "denylist", denylist: ["sprk_reviewdetail"] }`.
  
  The other keys, the other source keys and the existing columns are byte-identical (checked).
- **PATCH:**
  - Owner go: 2026-11-23 ("Yes, write it"). Rollback stated beforehand: write the saved string below back (`patch-3ff4102c.ps1 -Rollback`).
  - At 14:58:28Z the pre-check passed (versionnumber unchanged). The PATCH was `PATCHED: read-back equals target (2045 chars)`. The row is now modifiedon `2026-11-23T14:58:28Z`, versionnumber `32877194`.

**ROLLBACK 3ff4102c** (raw `sprk_configjson` before the change; modifiedon `2026-11-01T16:06:04Z`, versionnumber `29336457`). Rollback = PATCH this string back:

```text
{"_version":"1.0","source":{"type":"inline","fetchXml":"<fetch><entity name='sprk_invoice'><attribute name='sprk_invoiceid' /><attribute name='sprk_name' /><attribute name='sprk_invoicenumber' /><attribute name='sprk_invoicedate' /><attribute name='statuscode' /><attribute name='sprk_totalamount' /><attribute name='sprk_project' /><attribute name='sprk_matter' /><attribute name='sprk_totalamount_base' /><order attribute='sprk_invoicedate' descending='true' /></entity></fetch>","layoutXml":"<grid name=\"result\" object=\"1\" jump=\"sprk_invoicenumber\" select=\"1\" icon=\"1\" preview=\"1\"><row name=\"result\" id=\"sprk_invoiceid\"><cell name=\"sprk_invoicenumber\" width=\"200\" /><cell name=\"sprk_name\" width=\"300\" /><cell name=\"sprk_invoicedate\" width=\"100\" /><cell name=\"statuscode\" width=\"100\" /><cell name=\"sprk_totalamount\" width=\"100\" /><cell name=\"sprk_totalamount_base\" width=\"100\" /></row></grid>"},"display":{"title":"Invoices","emptyStateMessage":"No invoices found across the records you can access."},"rowOpen":{"type":"formDialog"},"commandBar":{"showDefaultCommands":{"newRecord":false,"refresh":true,"exportExcel":true,"delete":false,"editColumns":false,"editFilters":false}},"columns":{"sprk_invoicedate":{"renderer":"date","label":"Invoice Date","width":100},"statuscode":{"renderer":"badge","label":"Status","width":100},"sprk_totalamount":{"renderer":"currency","align":"right","label":"Total Amount (Local)","width":100},"sprk_project":{"hidden":true},"sprk_matter":{"hidden":true},"sprk_invoicenumber":{"label":"Invoice Number","width":200},"sprk_name":{"label":"Invoice Name","width":300},"sprk_totalamount_base":{"label":"Total Amount (Base)","width":100,"renderer":"currency","align":"right"}},"behavior":{"selectionMode":"none","pageSize":250,"enableSorting":true,"enableColumnResize":true}}
```

## E2E

### Round 1: owner, writer (Collaborate) on PRJ-000046 and PRJ-000038, 2026-11-23 from about 15:00Z

| # | Check | Result | Note |
|---|---|---|---|
| 1 | PDF preview | ✅ | It previews in place, and Download is in the toolbar. |
| 1 | .docx | ✅ (as designed) | The record opens with "This type of file cannot be previewed here. Use Download…". The owner asked for a docx viewer: none exists in the shared lib (`mammoth` is used only for chat text extraction). Internal apps preview Word through SharePoint's viewer, which the external plane can't use (ADR-028 A1, ADR-007). A docx preview would be new scope; the owner decides. |
| 2 | Documents grid at 1000px: no overlap | ✅ | At 1000px the Type and Date headers shrink to "Ty…" / "D…" and the dates are cut off ("11/12/…"). |
| 2 | Column resize | ❌ | The cursor shows the resize grab, but dragging doesn't resize (the header text gets selected instead). Under investigation: we don't yet know whether it happens only on the record grid or also on the home grids. |
| 2 | Upload / download / delete own (writer) | ✅ | Delete is offered only on own rows. |
| 3 | Review detail | ❓ | INV-123456.7 shows "In Review" but no Review detail line in Information. By the gate, raw statuscode 1 with a blank value should show "The client is reviewing this invoice." So either its "In Review" is not statuscode 1, or there is a defect. A read-only read is prepared (`read-invoice-status.ps1`). The owner hasn't yet checked the home Invoices grid for the new column; the record's Invoices section has no column, it shows a line suffix. |
| 4 | Invoice file download | ✅ | INV-123456.7.1.pdf downloads and opens. |
| 5 | Empty states | ✅ | The new shared style: "No Documents" with icon and the "Use Upload above" hint, "No events yet" with the Add event hint, "No invoices". |
| — | Cosmetic | ⚠️ | An empty rounded band sits above the Documents grid header. |
| — | View-only partner | pending | |

**Review detail: diagnosis.** Owner-approved read-only read at about 15:20Z: INV-123456.7 has statecode 0, statuscode **1** ("In Review"), and an empty `sprk_reviewdetail`. Invoice status options: 659490000 Approved, 1 In Review, 659490001 Sent to AP, 2 Cancelled, 659490002 Paid, 659490003 Rejected, 659490004 Returned to Firm, 659490005 Superseded.

So the Information field SHOULD show "The client is reviewing this invoice." It doesn't. That is a defect, F1, in task 020's code.

**Server side of round 1** (uac-r2's readout, BFF `9df4e5c819`, 14:59–15:45Z):

- 50 external requests, all 2xx: 48 × 200, 1 × 201, 1 × 204.
- None of these occurred:
  - 403s on `/documents`, `/invoices`, `/invoices/{id}/files` or grid config;
  - `DV_FETCHXML_COLUMN_NOT_PERMITTED`;
  - 5xx responses;
  - exceptions.
- Response times, p50 / p90 in ms:

  | Route | p50 | p90 | Note |
  |---|---|---|---|
  | `dataverse/fetch` | 956 | 1553 | n = 19 |
  | metadata | 530 | 728 | |
  | `/me` | 720 | 1118 | |
  | project documents | 1155 | 1411 | |
  | notifications | 2459 | 2575 | #2845 not met yet |
  | upload | 4657 | — | single request |
  | delete | 6479 | — | single request |

- Not R4's: IDX40001 Error-level traces on every partner request (the ExternalCollaboration policy tries the non-matching schemes). They existed before R4, and the requests are unaffected. Filed as #3003 (uac-r2).

**Fix round:** PR #3000 has both fixes, the option-set value shape and the DataGrid column resize. One adversarial review found two F1s in the first resize fix, and both are fixed.

**Round 2** runs on BFF `a48398746d` (uac-r2 swap, owner-named SHA) after the SPA redeploy.

### D. SPA redeploy (fix round)

- **Owner go:** merge PR #3000 on 2026-11-23 ("Yes, merge"); it was squash-merged as `9d08576e63`. Then the redeploy ("Yes, deploy").
- **Rollback, stated before the run:** redeploy `2dcbe35e34`.
- **Before dispatch:** BFF `a48398746d` passed uac-r2's gate. uac-r2 confirmed the build works against it. Master HEAD was re-checked just before dispatch: still `9d08576e63`.
- **What else the build carries** (told to the owner and uac-r2 beforehand):
  - ebilling 134, the Outcome report;
  - the ui-components focus-return fixes #2988, #2996 and #3004;
  - the communication 055/056b client changes.
- **The run:** **45260396182**, dispatched 20:32:19Z; succeeded, finishing 20:34:15Z.
- **Served build check:** `/` answers 200. `/assets/app.js` is 1,607,408 bytes, and the first 16 hex characters of its SHA-256 are `23D3DE2C8F562CCB`. It contains the ebilling 134 strings, the Review detail line and the grid id `286be8b4-…`.

Likely cause: the record and the Invoices-section rows come from the module fetch seam. `FetchService.ProjectEntity` passes SDK values through as objects, so `statuscode` arrives as the serialised `OptionSetValue`, about `{"value":1,...}`. The status label still shows because `formatDetailValue` uses `@formattedValues`. But `reviewDetail.ts` `wholeNumberOf` accepts only a number or a digit string, so the gate always answers blank. The task 020 tests used a bare `statuscode: 1`, so they never saw the live shape.

The same gate feeds all four render points: the grid cell, the section line, the header line and Information. So a real review detail value would be hidden everywhere too. The BFF-side gate (`ExternalInvoiceReviewDetail.IsInReview`) does handle `OptionSetValue`, so the server is right; the SPA is not.

### Round 2: owner (writer, then the View-only partner), BFF `a48398746d` + SPA `9d08576e63`, 20:34Z to 20:43:33Z

**Owner result: all checks pass.**

| # | Check | Result |
|---|---|---|
| 1 | Review detail on INV-123456.7: the "The client is reviewing this invoice." line in Information and the header | ✅ |
| 2 | Home Invoices grid "Review detail" column | ✅ |
| 3 | Record Documents grid: drag resize, and the width and sort survive a sort, window resize, selection or filter | ✅ |
| 4 | Home Invoices grid drag resize | ✅ |
| 5 | Documents upload / download / delete, PDF preview | ✅ |
| 6 | View-only partner: no create or Delete actions; "Your access" shows View Only; nothing offered is refused | ✅ |

**Server side** (uac-r2 readout, window 20:34–20:45Z, telemetry present to 20:43:20Z): **PASS**.

- External: 9 requests, all 200. There were no 4xx or 5xx responses, no exceptions and no `DV_FETCHXML_COLUMN_NOT_PERMITTED`.
- Latency, p50 / max in ms:

  | Route | p50 | max | Note |
  |---|---|---|---|
  | metadata | 570 | 615 | |
  | fetch | 610 | 1144 | |
  | `matters/{id}/events` | — | 1227 | single request |
  | `me` | — | 762 | single request |
  | record | — | 623 | single request |
  | ebilling outcome | — | 1442 | single request |

- Error-level logs: only IDX40001 (#3003, pre-existing; no request failed).
- No notifications call ran in this window, so #2845 isn't measured here.

**Still open, filed:**
- D-31 #3008: narrow header minimums ("Ty…"/"D…").
- D-32 #3009: the empty band above the Documents grid header.
- D-33 #3010: the FetchService comment drift.

**Task 028 complete** 2026-11-23.
