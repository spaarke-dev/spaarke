# 153 — WP6 deploy and end-to-end: notes

## The AP mailbox (dev)

- The eBilling settings row on dev (`sprk_ebillingsetting` 92acd624-c6c8-f111-a05c-7c1e52154cae) sends Email to AP to `sprk_apexportemail` = **ap@finance.spaarke.com**. It was set 2026-11-03 at 15:42:32 and read back. The delivery service reads it live, so no deploy is needed.
- Earlier the same day, uac-r2 set it to invoices@finance.spaarke.com for a one-off test. The owner then created a dedicated AP mailbox and approved the switch, so the test email never reached the future intake mailbox.
- **Standing rule:** the AP address must never be a receive-enabled (monitored) communication account. If it becomes one, Email to AP refuses with 409 `ebilling.export.ap_email_recipient_monitored`, and nothing is sent. That is by design: the backup poller would otherwise file the AP email as an inbound communication.
- **invoices@finance.spaarke.com** is planned as the shared intake mailbox. Don't set it as the AP address once intake is on.

## First Email-to-AP test (2026-11-03, owner-approved)

- **Invoices:** Garrison and Ellison INV-2025-05166 (9723a5aa-3f01-6c5e-1ece-f3902a625006) and Ravenscroft and Vance INV #2025023029 (7c200dd1-57f6-8b24-4c67-feb3794841fb).
- **Eligibility:** the owner is an eligible approver on both through Assigned To Internal, the settings' default approver role (the matters have no role of their own).
- **Approvals:** done by a script the owner runs with their own token. The review page (110) is not deployed yet.
  - The script calls GET review, then POST lines/decisions (accept the flagged lines), then POST approve, which accepts the clean lines in bulk, then POST export-batches.
  - No app-only calls and no Dataverse writes.
- **Then:** the owner opens the batch, checks there is no New button on the AP emails grid (Add Existing may still show; known limit K1), and clicks Email to AP.
- **Check:** uac-r2 looks in App Insights for the route status, the server-only send line, no OWNERSHIP-REFUSED, and no inbound filing of the message.
- **The export as the owner failed twice:** batches f6788e95 (15:43) and ac07ec43 (16:20, dev local time) were left Failed. The owner's account is in the root business unit, and export files refuse a root-unit owner (194b R4b-2c). The owner decided to leave both rows. The retry runs as demo@demo.spaarke.com (BU1) from the invoice view's Export to AP.
- **The fix** is merged into work as a6ff149cd9, notes/150. The owner check now runs before anything is written, and a root unit gets a 409 `ebilling.export.owner_unit_not_allowed`. uac-r2 passed it on all four points on 2026-11-03.
- **Owed:** the live proof that the pre-check passes for a BU1 caller on the real resolver. Record it here after the first BU1 export on a BFF that has the fix.
- Result: (pending)

## Billing Export in the Matter Management app (dev, 2026-11-03)

- **The gap:** `sprk_billingexport` was not a component of the Matter Management app on dev (appmoduleid 729afe6d-ca73-f011-b4cb-6045bdd8b757, `sprk_MatterManagement`), and the sitemap had no subarea for it. Email to AP is a ribbon button on the batch form, so the owner could not open a batch from the app.
- **Source already had both** since ac920d4184 (task 029): the entity component in `AppModule.xml` and a sitemap subarea. Dev lost them later, when every app component row on dev was rewritten at once (2026-10-21 02:29:29Z).
- **What fixed dev:** the table was added through the app designer at 22:13:29Z (app module modifiedon; the sitemap row saved at 22:13:24Z). The designer added three tables and one sitemap subarea each, all under `area_finance`: `sprk_ebillingsetting` (`subarea_c6d9cf09`, group_billing), `sprk_billingexport` (`subarea_8f8f4243`, group_review) and `sprk_ebillingoverride` (`subarea_fa7ac4a0`, group_review). Entity rows only, no form or view rows, like the sibling ebilling tables. The additive script written for this task was never run with `-Apply`; nothing was written to dev by it.
- **Source sync**, so the next import neither removes dev's entries nor shows Billing Export twice. Source now equals dev for entity components (104), form components (50) and app settings (14), and the sitemap differs only by the two source-only subareas below.
  - `AppModule.xml` and `AppModule_managed.xml` gained 9 entity components: `sprk_ebillingsetting` and `sprk_ebillingoverride` (the designer's add) and the seven Finance tables that were only on dev: `sprk_billingevent`, `sprk_budget`, `sprk_invoiceapproval`, `sprk_invoicecorrection`, `sprk_ratecard`, `sprk_ratecardrate`, `sprk_signal`. These seven and `sprk_ebillingsetting` are in the solution source (`Entities/`), so the references resolve.
  - **Dangling reference:** `sprk_ebillingoverride` has no `Entities/` folder yet. It stays listed on purpose (an import that drops it removes the table from the app); the table lands in source with task 112 (D-142). An import of source before then may warn on it.
  - `AppModuleSiteMap.xml` and `AppModuleSiteMap_managed.xml`: removed `subarea_eb_export` (group_work, after the Submission entry) and replaced `area_finance` with dev's whole area, byte for byte (3 groups, 13 subareas, same ids, attributes and order): group_billing (budget, e-billing setting, rate card, rate card rate), group_c6c6d7dd "Invoice" (invoice, invoice approval, invoice correction, billing event, invoice submission), group_review (spend signal, signal, billing export, e-billing override).
  - Each `_managed` copy was byte-identical to its unmanaged copy before the edit and still is.
- **Differences left unchanged** (source vs dev, after the sync):
  - Only in source: `subarea_eb_submission` (sprk_invoicesubmission, group_work). Dev shows Invoice Submission once, under Finance > Invoice (`subarea_d9b7c277`), so an import of source would list it twice.
  - Only in source: `subarea_f819179d` (sprk_communication "Email", group_find). Not on dev.
  - App role map: 3 of 5 role ids match. Source lists `119f245c-3cc8-4b62-b31c-d1a046ced15d` and `627090ff-40a3-4053-8790-584edc5be201`; dev has System Administrator `10fbf21c-1872-f011-b4cb-7c1e52671ad0` and System Customizer `80fff21c-1872-f011-b4cb-7c1e52671ad0`. Built-in roles carry a different id in each environment, so these are probably the same two roles; not checked further.
- **The batch form** (bd0020ec, the only active main form, so the default): it has the AP emails subgrid (`subgrid_apemails`, target `sprk_communication`, view All Outgoing Email). Email to AP is not in the form XML; it is a custom action in the published ribbon of `sprk_billingexport` (display rules: Write on the table, and a Completed batch).
- **The tool:** `scripts/ebilling/Add-BillingExportToMatterManagementApp.ps1` is now a verify and drift tool.
  - `-Verify` (read only) passes when the table is in RetrieveAppComponents, `sprk_invoice` still is (control), ValidateApp is clean, and the published sitemap has a subarea for the table. It also prints the form facts above and the source-vs-dev drift for this table.
  - `-WhatIf` (default, read only) lists what `-Apply` would add. On dev as it is now it plans nothing.
  - `-Apply -OwnerApproval` only adds what is missing, only additively: AddAppComponents, one SubArea spliced into the sitemap (checked byte for byte and node by node before the write, before-XML saved under a unique timestamped name, PATCH with If-Match), then a publish scoped to the app module and the sitemap. The sitemap is matched by **Entity, not subarea Id**, so a table that already has a subarea is never given a second one. AddAppComponents has answered 204 and added nothing on this environment before (notes/029 section 7A); the script reads the result back and stops before the sitemap (exit 3) if the table is still missing.
  - Tests: `tests/scripts/Add-BillingExportToMatterManagementApp.Tests.ps1` (Pester 5, 70 tests, offline: the functions are read out of the script and the whole script runs against a fake Dataverse).
  - Dev, read only: `-Verify` passed 4 of 4 at 22:52Z and again at 23:00Z (ValidateApp: success, 0 errors, 0 Billing Export issues, 106 warnings that were already there); `-WhatIf` planned nothing.
- **Lint:** the comment at `Deploy-EbillingSubmissionTrackingScopeSchema.ps1:125` (095c-1) named the tenant-wide publish by its action name, which the repo's publish lint bans. It now says "the tenant-wide publish"; `Publish-SolutionComponents.Tests.ps1` is 77/77.