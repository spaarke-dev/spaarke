# Task 151b: AP export and Mark Paid UI (views, ribbons, dialog)

Branch `task/ebill-151b-export-ui`, from `work/ebilling-r1` at `2220b995f8`. Rigor STANDARD (frontend + solution source; no BFF change). Model: Sonnet @ high. Decision EB-D-181. Calls the routes of 150 (create batch) and 151a (Mark Paid, file download). 151c (Email to AP, settings column) is not here: the dialog shows Email to AP disabled.

Nothing was deployed or imported. The deploy steps for the main session are at the end.

## What was built

| Component | Where | What it does |
|---|---|---|
| View "Approved – ready for export" | `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Invoice/SavedQueries/{467cbd98-eb63-4f05-b7fb-3bc71f14bac9}.xml` | `statuscode` = Approved (659490000) and `sprk_billingexport` empty; oldest invoice date first |
| View "Sent to AP" | `.../SavedQueries/{cd153641-74d9-4a9f-9318-3cb7caf4fccf}.xml` | `statuscode` = Sent to AP (659490001); oldest first; with the Export Batch column |
| View "Paid" | `.../SavedQueries/{b0feb580-dbb5-464f-bb07-3ce64fb5e21d}.xml` | `statuscode` = Paid (659490002; inactive state, so no statecode filter); newest first; with the Export Batch column |
| Invoice ribbon | `.../Entities/sprk_Invoice/RibbonDiff.xml` | Export to AP and Mark Paid on the home grid (the views) and on the main form |
| Billing Export ribbon | `.../Entities/sprk_billingexport/RibbonDiff.xml` | Download CSV and Download JSON on the form |
| Ribbon script | `src/client/webresources/js/sprk_apexport_ribbon.js`, mirrored to `.../WebResources/sprk_/scripts/apexport_ribbon.js` (+ `.data.xml`, id `{a20517ef-dd2a-4461-822b-f80c1d735e69}`), root component in `Other/Solution.xml` | Enable rules, dialog launch, file download (namespace `Spaarke.ApExport.Ribbon`) |
| Dialog code page | `src/solutions/ApExport/` → `dist/sprk_apexport.html`, web resource `sprk_apexport` | Export to AP and Mark Paid dialogs (React 19, Fluent v9) |

View columns (all three): Firm (`sprk_vendororg`), Record (`sprk_regardingrecordname`), Invoice number (`sprk_invoicenumber`), Tracking number (`sprk_trackingnumber`), Date (`sprk_invoicedate`), Approved total (`sprk_approvedtotal`), Currency (`sprk_currency`); Sent to AP and Paid add Export Batch (`sprk_billingexport`). The total is the **approved** total: it is what the batch exports and sums (150). FetchXML and layout copied from "Invoices to review" (`{0c7694d8-...}`).

### Ribbon rules (display only; the server decides every export, payment and download)

| Button | Where | Shown | Enabled |
|---|---|---|---|
| Export to AP | home grid | the user holds Create on `sprk_billingexport` (`EntityPrivilegeRule`, 150's note: "show the Export action to a user who can export") | 1 to 200 rows selected (`SelectionCountRule`; 200 = the route's cap). Not filtered by status on purpose: the server refuses per invoice and the dialog shows why |
| Mark Paid | home grid | always | 1 to 200 selected and every selected invoice is Sent to AP (custom rule, one `Xrm.WebApi` read of the selection's `statuscode`) |
| Export to AP | invoice form | existing record and Create on `sprk_billingexport` | the invoice is Approved (custom rule: `statuscode` from the form, or read through `Xrm.WebApi` when the form does not carry it: the "Information" form does not) |
| Mark Paid | invoice form | existing record | the invoice is Sent to AP (same rule) |
| Download CSV / JSON | Billing Export form | existing record | `sprk_exportstatus` = Completed (only a Completed batch lists files, 150) |

A rule that cannot read the status answers false (the button stays disabled). Labels are `LocLabels` with `Alt` (ribbon-edit). Icons: `sprk_/icons/cmd/export.svg` (Export), built-in `Accept` (Mark Paid), `sprk_/icons/cmd/save.svg` (Download); all existing.

### Dialog behaviour

Launched by the ribbon script with `Xrm.Navigation.navigateTo({ pageType: "webresource", webresourceName: "sprk_apexport", data: "mode=<export|markpaid>&ids=<id>,..." }, { target: 2, position: 1, 60% x 70% })`, the wizard dialog size. After it closes the script refreshes the grid, or the form and its ribbon.

- **Selection.** The page reads the selected invoices for display through `Xrm.WebApi` (firm, record, numbers, date, approved total, currency, status), in the selection's order, with the count and the approved total per currency. A row the read does not return is listed by id and still sent; a failed read shows a warning and the ids only. The read decides nothing.
- **Export to AP.** One `POST /api/finance/export-batches` with the selection (both formats).
  - 201: "Exported to AP", the batch name, count and total, **Download CSV** / **Download JSON** for the formats the batch has a file for (151a's route; the file is saved under `ap-export-{batchId}.{format}`, or the `Content-Disposition` name when the browser exposes it), and **Email to AP** disabled ("not available yet"; 151c wires it).
  - 400 `selection_refused`: "N of M selected invoices cannot be exported. Nothing was exported." and each refused invoice with its reason (`invoice_not_approved`, `invoice_already_exported`, `invoice_not_found`), plus the count and total of the rest; **Export the other K** sends a new request for the rest (the server checks them again).
  - 403 `not_permitted`: "You may not export this selection. Nothing was exported." (names no invoice, as the server names none, EB-D-178). 503 `authorization_unavailable`, 409 `invoice_conflict`, 503 `unavailable` / `file_failed` / `transition_failed`, a network error: the message and **Try again**. `ebilling.disabled`, sign-in failure, 422, other 400s: the message only. A 500 `*_incomplete` names the batch id for an administrator.
- **Mark Paid.** A confirm step (the selection, count, total, and "Only an invoice that is Sent to AP can be marked Paid; a Paid invoice cannot be reopened"), then one `POST /api/finance/invoices/{id}/mark-paid` per invoice, in order, one at a time, with a progress bar and each answer in the table: Paid; 409 `invoice.transition_not_allowed` "Not changed: the invoice is {currentStatus}, and only a Sent to AP invoice can be marked Paid" ("Already Paid" for Paid); 409 `status_unknown` / `concurrent_change`; 404 "Not found, or you may not mark it Paid"; 503 `payment_unavailable` with the server words. A sign-in failure or `ebilling.disabled` stops the run (every later call would get the same answer) and lists the rest as "Not attempted". The summary says "x of y marked Paid; z not changed".
- **Billing Export form downloads.** The ribbon script calls 151a's route with `Spaarke.BffAuth` (bearer token, BFF origin only) and saves the file; a refusal shows the server's detail and code (e.g. the scan-pending 409, `file_not_available`).
- **Auth.** The page uses the repo's code-page pattern, copied from RuleReview: `createRuntimeConfigStore` + `createCodePageAuthInitializer` (`requireSilentOnly`, ADR-028 INV-5), `authenticatedFetch` for every BFF call, a retry state if sign-in fails. The ribbon download uses `sprk_/scripts/bff_auth.js` + `assignedaccess_postsave.js` loaded as `isNaN`, as `secure_child_ribbon.js` does.
- **Theme.** `resolveCodePageTheme` / `setupCodePageThemeListener` on the host `FluentProvider`, the surface colour on a wrapper div (F-10); Fluent v9 semantic tokens only (no hex, `rgb(` or inline colour; `tokens.strokeWidthThin`); the body uses `thinScrollbarStyle`. The platform dialog's title bar is white in dark mode (platform chrome, ADR-050 known limit).
- **Navigator.** `ensureNavigatorSidePane()` on mount (CLAUDE.md section 17).

## Component justification (CLAUDE.md section 11)

**The three views**
1. Existing: the `sprk_invoice` views are All / Active / Inactive / My / review-queue / Matter-context views; none filters by Approved, Sent to AP or Paid, and none shows the export batch. 150's candidates route lists exportable invoices but has no surface.
2. Extension: an existing view cannot be re-filtered without changing what its users see; a view is the smallest unit (EB-D-181 chose views over a new page). Each is a copy of "Invoices to review" with another filter.
3. Cost of doing nothing: finance cannot find the invoices to export or to mark paid without building an Advanced Find filter by hand; FR-41 / FR-42 have no working list.

**Export to AP button (grid and form)**
1. Existing: no command starts an export; 150's route has no caller in the app.
2. Extension: added to the existing `sprk_invoice` RibbonDiff next to the existing commands; the command opens the dialog page (below), no new script library besides the one ribbon script.
3. Cost of doing nothing: no way to create an AP export batch from the app (FR-41, success criterion 7).

**Mark Paid button (grid and form)**
1. Existing: no command calls 151a's Mark Paid route (`grep mark-paid src/client src/solutions` before this task: none).
2. Extension: same RibbonDiff and same ribbon script and dialog page as Export (a mode).
3. Cost of doing nothing: invoices stay Sent to AP forever; FR-42 fails.

**Download CSV / JSON buttons (Billing Export form)**
1. Existing: the `sprk_billingexport` form had no commands; 090's file route needs a file id the form does not show.
2. Extension: the first commands in that entity's existing (empty) RibbonDiff; one function of the same ribbon script; calls 151a's route.
3. Cost of doing nothing: a past batch's files cannot be fetched again from the batch (EB-D-181: "The Billing Export form gets Download files").

**Ribbon script `sprk_/scripts/apexport_ribbon.js`**
1. Existing: `sprk_wizard_commands` opens code pages but has no selection or status rules; `secure_child_ribbon.js` calls the BFF but only for child creates.
2. Extension: adding AP export functions to either would mix unrelated commands into a file other ribbons load and their tests pin (`SecureChildNewCommandAgreementTests`); one small script per feature is the repo's idiom. It copies their patterns (navigateTo envelope, `Spaarke.BffAuth` call).
3. Cost of doing nothing: the buttons have no enable rules and no action.

**Dialog code page `sprk_apexport` (`src/solutions/ApExport/`)**
1. Existing: no surface creates a batch or pays an invoice. `sprk_invoicespage` is a Matter-scoped grid (not extended, as the parent POML says). The shared library has `SprkModal` presets but no export or payment content.
2. Extension: a hostless ribbon command can only open a page through `navigateTo` (ADR-050); there is no existing page to add a mode to. The page reuses the shared bootstrap pieces (auth factory, theme, error boundary, Navigator, `parseDataParams`, `createXrmNavigationService`), Fluent v9 and `thinScrollbarStyle`; no new npm package (the dependency set is RuleReview's).
3. Cost of doing nothing: Export to AP and Mark Paid have nowhere to show the selection, the per-invoice refusals and outcomes, the totals and the download buttons the parent POML and EB-D-181 require.

Complexity (CLAUDE.md 11.5): the page is small and split by responsibility: `services/` (API client, launch parsing, display read, export outcome, Mark Paid run; all pure and tested) and `components/` (two dialogs, a table, a summary, the layout).

## ADR-050 / modal standards: how the dialog fits

- Launch: a hostless ribbon script uses `navigateTo` (ADR-050, MODAL-DECISION-CRITERIA "Launching"). The platform dialog is the modal envelope and carries the title and close.
- Inside, the page renders no `SprkModal`: a second Fluent `Dialog` inside the platform dialog would be a double header (MODAL-DESIGN-SYSTEM section 3, single-title-source rule). It follows the footer contract instead: Cancel / Close left, actions right. This is how a ribbon-hosted page is built (WizardShell's `embedded` + `hideTitle` is the wizard equivalent; this is not a wizard).
- The Mark Paid confirm is the dialog's first step, not a `ConfirmModal` over it, for the same reason (the parent POML's "FormModal for confirmations" assumed an in-app React host). The danger styling of a destructive primary is not used: Mark Paid is a normal primary (Paid is final, and the text says so).

## Deviations to confirm

1. **SpaarkeMaster edited by this task.** The project CLAUDE.md (2026-10-15 pipeline note) and the parent POML (constraint "Do not edit SpaarkeMaster here; create in dev, list for 153") reserve `src/dataverse/solutions/SpaarkeMaster/**` for serial tasks and 153. The brief for 151b asked for the views and ribbons in solution source, so they are there (3 views, 2 RibbonDiffs, 1 web resource + data.xml, 1 root component line). 153 should treat them as already synced; nothing was created in dev.
2. **`sprk_trackingnumber` is not in SpaarkeMaster yet.** The three views select it (the brief lists tracking number). The column exists on dev (`Deploy-EbillingInvoiceTrackingNumberSchema.ps1`, run 2026-10-30) but SpaarkeMaster's `sprk_invoice/Entity.xml` does not carry it (053 adds it). Importing this source into an environment where the column does not exist will fail on these views. Either 053's column sync lands first, or the script runs on the target environment before the import. Dev is fine.
3. **The code page HTML is not in SpaarkeMaster.** The 1 MB build output is deployed as a web resource (below) and joins the solution there; 153 (or the next export) brings it into source, as with `sprk_rulereviewpage` (077 / 079).

## Files

- `src/solutions/ApExport/` (new): `package.json`, `package-lock.json` (RuleReview's dependency set, name changed), `index.html`, `vite.config.ts`, `tsconfig.json`, `tsconfig.node.json`, `jest.config.cjs`, `src/main.tsx`, `src/App.tsx`, `src/config/runtimeConfig.ts`, `src/services/{authInit,apExportApi,launch,invoices,exportFlow,markPaidFlow,download}.ts`, `src/components/{DialogLayout,InvoiceTable,SelectionSummary,ExportDialog,MarkPaidDialog}.tsx`, `src/__tests__/{testKit,apExportApi,exportFlow,markPaidFlow,launchAndInvoices,ribbonScript}.test.ts`
- `src/client/webresources/js/sprk_apexport_ribbon.js` (new)
- `src/dataverse/solutions/SpaarkeMaster/`: `Entities/sprk_Invoice/SavedQueries/` (3 new), `Entities/sprk_Invoice/RibbonDiff.xml`, `Entities/sprk_billingexport/RibbonDiff.xml`, `WebResources/sprk_/scripts/apexport_ribbon.js` (+ `.data.xml`), `Other/Solution.xml` (one root component)

No BFF change. None was needed: the routes, shapes and codes the page uses are 150's and 151a's as documented.

## Tests and build

| Check | Result |
|---|---|
| `npm install --legacy-peer-deps --no-audit --no-fund` (ApExport) | OK |
| `npm test` (jest, node env) | 5 suites, 70 tests, 0 failed |
| `npm run typecheck` / the build's tsc gate | Surface-owned errors 0 (100 pre-existing shared-lib errors, the gate's known baseline) |
| `npm run build` | `dist/sprk_apexport.html`, 1,016,781 bytes (gzip 314 kB) |
| `pac solution pack` of SpaarkeMaster (unmanaged, local, scratch zip) | Packs; the zip carries the 3 views, both RibbonDiffs and the web resource |
| ArchTests filter (WebResourceMirrorParity, SecureChildNewCommand, InvoiceStatusRetirement, SpaarkeMasterApplicationDependency, ExternalSpaGridViewSelector) | 284 passed, 0 failed |

What the tests pin: create-batch request shape; the per-invoice refusals (reason texts, the rest of the selection, case-insensitive ids, counts in the message); every other export refusal's message and whether it can be retried (403 names no invoice); partial Mark Paid (order, one call at a time, each outcome, `currentStatus` named, the summary), the stop on `ebilling.disabled` (later invoices not called), a rejected call; the download URL, file name fallback and `Content-Disposition` parsing, the formats offered; the launch envelope (mode, ids, braces, repeats, the 200 cap); the display read (query, formatted values, order, missing rows, failed read) and the per-currency total. The ribbon script runs in a `vm` sandbox with a fake `Xrm` / `fetch` / sign-in helper: the three enable rules (form status from the form and from a read, selection status all / mixed / partial read / empty / too large / failed read, Completed batch), the dialog launch (page, data, size, title) and the refresh after it, the download (URL, bearer, file name, refusal text, no token, helpers missing), and that the SpaarkeMaster mirror equals the source.

"Fails before" is by construction (every file is new). Seeding proof: three mutations, each reverted after: `withoutRefused` returning the whole selection, `runMarkPaid` ignoring the stop, `selectionHasStatus` using `some` instead of `every` (source and mirror then differ) → 6 tests red; restored → 70 green.

Not run: the ui-tests below (they need the deploy) and component rendering tests (the package has none; RuleReview's pattern is logic tests only).

## Deploy (main session; nothing here was deployed)

1. **Prerequisite on the target environment:** `sprk_invoice.sprk_trackingnumber` exists (dev: yes; elsewhere run `scripts/ebilling/Deploy-EbillingInvoiceTrackingNumberSchema.ps1` or land 053's column sync first). The BFF carries 150 and 151a.
2. **Code page:** `cd src/solutions/ApExport`, `npm install --legacy-peer-deps --no-audit --no-fund`, `npm run build`, then upload `dist/sprk_apexport.html` as web resource **`sprk_apexport`** (type 1, Webpage (HTML)) and publish: `scripts/Deploy-WebResourceInline.ps1 -DataverseUrl <org> -WebResourceName sprk_apexport -FilePath dist/sprk_apexport.html -DisplayName "AP Export"`. That script creates the row but does not add it to a solution: add it to SpaarkeMaster afterwards (maker portal "Add existing", or `AddSolutionComponent`), so 153's export picks it up.
3. **Views, ribbons and the ribbon script:** import from the SpaarkeMaster source (pack unmanaged from `src/dataverse/solutions/SpaarkeMaster`, import, publish all). The import carries `sprk_/scripts/apexport_ribbon.js` with its id `{a20517ef-dd2a-4461-822b-f80c1d735e69}`, before the ribbons that reference it. Dev is the authoring environment (`solutionPackageType none` in `Deploy-Release.ps1`), so a full import there overwrites unmanaged dev changes made since the last source export: check with the owner first. The narrower route (ribbon-edit): a temporary solution with only `sprk_invoice` (the three views and the RibbonDiff) and `sprk_billingexport` (the RibbonDiff), after creating the script web resource **with the same id** (a `webresourceset` POST that sets `webresourceid`; type 3) from `src/client/webresources/js/sprk_apexport_ribbon.js`. Do not create the script under another id: a later SpaarkeMaster import would then clash on the name.
4. Hard-refresh the app. The three views appear under the invoice view selector in Matter Management (the app module includes all `sprk_invoice` views).

## Manual UI test steps (after the deploy)

Run as a finance user (Create on `sprk_billingexport`, the decide right and Read on the invoices) unless stated.

1. **Views.** Invoices → view selector: "Approved – ready for export", "Sent to AP", "Paid" exist; each lists only its status; Approved lists none that has an Export Batch; Sent to AP and Paid show the Export Batch column; columns: Firm, Record, Invoice number, Tracking number, Date, Approved total, Currency.
2. **Export, happy path.** In "Approved – ready for export" select two invoices → Export to AP. The dialog lists both with count and total → Export 2 invoices → "Exported to AP", batch name, count 2 and the total; Download CSV and Download JSON each save `ap-export-{batchId}.csv/.json`; Email to AP is disabled. Close: the grid refreshes and both invoices are gone from the view and appear under "Sent to AP" with the batch.
3. **Export, refusals.** Switch to "Sent to AP", select one Sent to AP invoice plus (from All Invoices) one Approved invoice, Export to AP → "1 of 2 selected invoices cannot be exported. Nothing was exported." with the Sent to AP one marked "Already in an export batch" or "Not Approved"; Export the other 1 creates a batch of one.
4. **Mark Paid, grid.** In "Sent to AP" select one or more → Mark Paid is enabled (it is disabled when the selection includes a non-Sent to AP invoice, e.g. from All Invoices). Confirm → each shows Paid; the summary "n of n marked Paid"; Close: they move to "Paid".
5. **Mark Paid, refusal shown.** The enable rules keep Mark Paid off for anything but Sent to AP, so the 409 is seen through a stale selection: in "Sent to AP" select two invoices, open one of them in another tab and mark it Paid there, then (without refreshing the grid) choose Mark Paid on the two → the invoice paid in the other tab shows "Already Paid. Not changed." (409 `invoice.transition_not_allowed`, `currentStatus` Paid), the other is Paid, and the summary says "1 of 2 marked Paid; 1 not changed".
6. **Form buttons.** An Approved invoice form shows Export to AP enabled and Mark Paid disabled; a Sent to AP invoice the reverse; a Paid invoice both disabled; a new invoice form shows neither.
7. **Billing Export form.** Open a Completed batch: Download CSV and Download JSON save the files, byte for byte the batch's (compare with step 2). A Failed batch: both disabled.
8. **Non-finance user.** A user without Create on `sprk_billingexport` does not see Export to AP. A user with Create but without the decide right on an invoice: Export to AP → "You may not export this selection. Nothing was exported." Mark Paid on an invoice without the decide right → "Not found, or you may not mark it Paid." No status changes.
9. **Dark mode (ADR-021).** Switch the app to dark (Dark Mode menu), open both dialogs (review, result, refusal, Mark Paid outcomes) and the confirm step: backgrounds, text, table, badges and message bars use the dark theme; no white panels or black text inside the page (the platform dialog title bar stays white: platform chrome). Check the browser console for errors in each step.

## Open questions for the owner

- Export to AP on the grid is enabled for any selection (the server refuses per invoice and the dialog explains), as the brief says; the form button is enabled only for Approved. Say if the grid button should also require Approved.
- Mark Paid has no privilege display rule (151a asks the decide right per invoice; a user without it sees each invoice refused as not available). An `EntityPrivilegeRule` on Write of `sprk_invoice` could hide it from read-only users.
