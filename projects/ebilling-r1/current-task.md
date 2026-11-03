# Current Task State

> **Last Updated**: 2026-11-03 (checkpoint, context ~85%). 151c is on master (#2526, 5796774091); the dev BFF has been on 5796774091 since 13:29Z (SPA-r3). This session is ebilling-r1-7b; the duplicate 9c session after the crash should stay idle or be closed.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Blocking defect on dev** | `sprk_invoice.sprk_trackingnumber` does NOT exist on dev, although the 151b note says it was created on 10/30. `InvoiceSubmissionPersister` (around line 1263) writes it on every invoice create, so every intake fails with `ebilling.submission.transaction_failed`. The owner's INV-792345.1 (submission 5a1a526f-4868-c2c7-050b-2a7d80514275) failed this way at 13:39Z. App Insights `6a76b012-46d9-412f-b4ab-4905658a9559` shows "'sprk_invoice' entity doesn't contain attribute … sprk_trackingnumber", retried 5 times (about 14 s each). **The owner is creating the column by hand**: Text, max length 100, optional; display name "Tracking Number"; schema name `sprk_TrackingNumber`; the description is in `scripts/ebilling/Deploy-EbillingInvoiceTrackingNumberSchema.ps1` line 59; it must be in SpaarkeMaster. When they say it's done, verify it with the script's -WhatIf, which should report "skipped". |
| **Next action** | (1) After the column exists, create the three invoice views from SpaarkeMaster source through the Web API, with fixed ids and the `MSCRM.SolutionUniqueName: SpaarkeMaster` header: `{467cbd98-eb63-4f05-b7fb-3bc71f14bac9}` Approved – ready for export, `{cd153641-74d9-4a9f-9318-3cb7caf4fccf}` Sent to AP, `{b0feb580-dbb5-464f-bb07-3ce64fb5e21d}` Paid. The layoutxml grid needs `object="<sprk_invoice ObjectTypeCode>"`, because the source omits it and Web API validation requires it. Then PublishXml `sprk_invoice` only, after the pending-changes check. (2) Run uac-r2's after-notice checks: (a) the temporary solution `sprkEbillingApExportUiTemp` holds only the two table shells with RibbonDiffs (behavior 1; no forms, attributes or views); (b) the batch form still has `subgrid_apemails`; (c) the link relationship's cascade is unchanged; (d) the `sprk_communication` subgrid ribbon still uses `nativeNewAllowed` and `newChildAvailable`, and dev's `secure_child_ribbon.js` still has NO_NEW_HOSTS; (e) K3 (Office Add In User Read on `sprk_billingexport`), report only. Then send after-notices to uac-r2, ontology-r1 and SPA-r3. Delete the temporary solution record; that leaves its components in place. (3) File issues: (i) the submission job retries a permanent schema fault 5 times (should be non-retryable); (ii) the 151b note wrongly says the tracking-number column was created on dev; (iii) `ai.invoice.binding_missing` for invoice-codeset-match on dev, which isn't fatal because it falls back to embedding ranking. (4) The owner retests the submission in Outlook web, then the correction test; ebilling verifies the server side by tracking number. |

## Done on dev 2026-11-03 (owner OK for the UI import)
- Web resources `sprk_apexport` (837ac16f-8bd7-f111-84e8-7ced8dda31f8) and `sprk_/scripts/apexport_ribbon.js` 1.1.0 (a20517ef-dd2a-4461-822b-f80c1d735e69): created in SpaarkeMaster, published, read back byte-identical.
- RibbonDiffs for `sprk_invoice` and `sprk_billingexport` imported through `Import-SolutionScoped.ps1`, solution `sprkEbillingApExportUiTemp`, with a scoped publish of the two tables. The invoice RibbonDiff is dev's current one plus the 36 ApExport elements. It deliberately leaves out task 014's 7 HideCustomAction elements (Activate and Deactivate), which are in source but were never deployed; that's outside this OK. Package scratch: `scratchpad/apui/`.
- The batch Active view `{c19542df-…}` gained the Sent to AP Date column (PATCH with `object=11020`) and `sprk_billingexport` was published.
- Before-notices went to uac-r2 (no objection; it asked for the checks in step 2), ontology-r1 and SPA-r3.

## Owner's Outlook report (sent to word-add-in 2026-11-03)
- Outlook desktop: the sign-in popup fails with "Bad Request - Request Too Long" (`naa=false`). This is word-add-in's to fix, and the owner wants it ASAP.
- Outlook web: a 404 on `…%3E/suggestions` (a malformed URL), also word-add-in's.
- Over a minute of processing: the 5 retries; a good run takes about 15 s. The owner asked to be able to leave and come back with a notification; I asked word-add-in what fits the add-in.

## Waiting on
- word-add-in's answers on the above.
- The click-through check that the AP emails grid shows no New button (on the owner's checklist through uac-r2).
- Delete `C:\w15p` (owner; my rm was blocked).

## Open items
- Task 153 step 4 conflicts with the rule that only SPA-r3 deploys the dev BFF: amend the POML. The rest of 153 waits for 136.
- 151d: the invoice form shows the batch's Sent to AP Date.
- 053 must add `sprk_trackingnumber` to `sprk_Invoice/Entity.xml` before any non-dev import.
- Older notes name the owner in plain text (003, 004, 011, 029, 091): docs pass.
- 050a §9 items 2–4 (owner).

## Standing rules (see project CLAUDE.md)
No bare git stash, no --no-verify. Live writes and role changes need the owner's OK. Schema runs need notices before and after, and the publish is entity-scoped only. StoredFileReconcileJob stays off. ebilling never deploys the dev BFF (SPA-r3 does). Master PRs: ontology-r1 review, then auto-merge, then the SHA to peers. Max 4 subagents, and every agent launch states a model. No bare jq, no grep -P.
