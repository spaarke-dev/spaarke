# Task 151c-1b: Email to AP and Mark Sent to AP, UI

Branch `task/ebill-151c1b-email-ui`, from `work/ebilling-r1` at `7d493b53db`. Rigor STANDARD (frontend, ribbon and view source; no BFF change). Model: Sonnet @ high. Decisions EB-D-181 to EB-D-184. Builds on 151b (the `sprk_apexport` page, `apexport_ribbon.js`, the `sprk_billingexport` RibbonDiff) and calls the two routes of 151c-1a (`notes/151c1a-email-route.md` is the contract).

Nothing was imported or published. The deploy steps for the main session are at the end.

## What was built

| Component | Where | What it does |
|---|---|---|
| Email to AP step | `src/solutions/ApExport/src/components/BatchEmailView.tsx` | The batch, the AP address, optional CCs and message, Send to AP, the result, Download, Mark Sent to AP. Opened from the export result ("Email to AP", with a Back button) and from the Billing Export form |
| Mark Sent to AP | `components/MarkSentSection.tsx`, `components/MarkSentDialog.tsx` | A button with its result. In the Email step (below the downloads) and as its own dialog mode |
| Launch modes | `services/launch.ts`, `App.tsx` | `mode=email|marksent&batch=<id>` next to the 151b `mode=export|markpaid&ids=...`. The page reads the batch for display, then shows the step or the dialog |
| Error mapping and the sender | `services/emailFlow.ts` | Client checks of CC and message; every error code as one sentence plus a next step; `createEmailSender` (one send at a time, none after the email went) |
| Mark Sent mapping | `services/markSentFlow.ts` | 200 / 409 `batch_not_completed` / 404 / 503 as plain words |
| Display reads | `services/batch.ts` | The batch (`sprk_billingexport`) and the AP address (`sprk_ebillingsetting`) through `Xrm.WebApi`, read-only; date formatting |
| API client | `services/apExportApi.ts` | `emailBatch`, `markSentToAp` (the only two new calls) |
| Ribbon script 1.1.0 | `src/client/webresources/js/sprk_apexport_ribbon.js` + SpaarkeMaster mirror | `openFromBatchForm(primaryControl, "email"|"marksent")`, `batchLaunchData`; the new modes in `MODES`. The invoice commands refuse the batch modes |
| Billing Export ribbon | `Entities/sprk_billingexport/RibbonDiff.xml` | Email to AP (`email.svg`) and Mark Sent to AP (`Accept`) on the form |
| Views | SpaarkeMaster `SavedQueries` + `Deploy-EbillingFormsViews.ps1` | `sprk_senttoapdate` on the batch Active view; the batch's date as a linked-entity column on the Sent to AP and Paid invoice views |

### The modal

- **Where it appears.** In the export result step, the (formerly disabled) Email to AP button now opens the step on the new batch; Back returns to the result. Opened from the batch form, the dialog reads the batch and shows the same step.
- **AP address.** Read through `Xrm.WebApi` from the active `sprk_ebillingsetting` row. An address: shown as "To (AP)". No row or an empty column: the not-configured message, Download only (no CC, no message, no Send). A failed read (for example a user without Read on the settings): "unknown", and the dialog lets the server answer; 409 `ap_email_not_configured` then shows the same message.
- **Compose.** CC box (comma, semicolon or new line separated; blank entries ignored; at most 10; each a bare address; repeats dropped case-insensitively) and a message box with a character count (at most 2000). Errors show after the first Send click and block it. The server's 400 `invalid_request` is mapped too ("correct it and send again").
- **Send.** The button is disabled while a send is in flight, and `createEmailSender` refuses a second call before the network (the React state updates after the next render, so a fast second click could still reach the handler; the sender's flag is synchronous).
- **200.** "Sent to AP", the Sent to AP Date (the user's locale) and the recipients (To, then each CC).
  - `sentToApDateSet:true`: "The email was sent and the Sent to AP Date was recorded."
  - `sentToApDateSet:false` with a date (a re-send): "The email was sent again. The Sent to AP Date was already set and is unchanged."
  - `sentToApDateSet:false`, `sentToApDate:null`: "The email was sent, but the Sent to AP Date could not be recorded; use Mark Sent to AP." The warning shows Mark Sent to AP and no Send.
- **After the email went there is no Send.** After any 200, and after a failure whose next step is not "try again" or "correct the input", the sender is locked (the compose form and Send are gone). To re-send a batch that already holds a date, open the dialog again (EB-D-181: the batch form gets a re-send); the step says "This batch was already sent to AP. Sending again emails AP a second time; the Sent to AP Date stays as it is."
- **Mark Sent to AP** is shown in the step unless a date is held (read from the batch, set by the email, or set by Mark Sent in this dialog). It calls the route, shows "Marked Sent to AP" or "Already marked Sent to AP" (date unchanged), and the 409 / 404 / 503 answers. A repeat is safe (the server never overwrites), so the retryable failures show Try again.

### Every error code, one sentence, one next step

| Status / code | Next step | Sentence (abridged) |
|---|---|---|
| 400 `invalid_request` | edit | A CC address or the message was not accepted (+ the server detail). Correct it and send again |
| 403 `caller_unresolved` | manual | Your user account could not be identified, nothing was sent. Do not try again. Use Download, deliver the files, then Mark Sent to AP |
| 409 `ap_email_not_configured` | manual | No usable AP email address is set (empty, or not a single valid address); nothing was sent. Do not try again. Download, then Mark Sent to AP |
| 409 `file_not_available` | manual | The batch is not Completed, or a file is missing or still being scanned. Do not try again from here. Download, then Mark Sent to AP |
| 409 `ap_email_recipient_monitored` | manual | The AP address, a CC address or your own mailbox is monitored by Spaarke. Do not try again. Download, then Mark Sent to AP (one sentence for the AP / CC and the sender's own mailbox variants, which share the code) |
| 409 `attachments_too_large` | manual | Too large to email (3 MB). Do not try again. Download, then Mark Sent to AP |
| 409 `email_owner_refused` | manual | Could not be tied to the batch's owning team. Do not try again. Download, then Mark Sent to AP |
| 409 `email_send_refused` | manual | The mail service refused to send. Do not try again. Download, then Mark Sent to AP |
| 503 `email_send_failed` | retry | Temporary mail problem; not sent, no date set. Try again |
| 503 `ebilling.export.unavailable` | retry | A needed service is unavailable; nothing sent. Try again in a moment |
| 503 `ebilling.disabled` | manual | eBilling is off; nothing sent. Do not try again; ask an administrator. Download, then Mark Sent to AP |
| 404 (uniform) | none | "This export batch isn't available to you." |
| 401 / sign-in failed | reload | Sign-in could not be verified, nothing sent. Reload and try again |
| no answer (network) | unknown | Not known whether the email was sent. Check your Sent Items before doing anything else; if sent, Mark Sent to AP; if not, Download and deliver the files, then Mark Sent to AP |
| any other | manual | The server's words and code, "check Sent Items if you are not sure", then Download and Mark Sent to AP |

Mark Sent: 409 `batch_not_completed` "This batch is not Completed, so it cannot be marked Sent to AP. Nothing was changed."; 404 as above; 503 `unavailable` and no answer are retryable (a repeat is safe); `ebilling.disabled` and sign-in are not.

### Ribbon (Billing Export form)

| Button | Shown | Enabled |
|---|---|---|
| Email to AP | existing record and Write on `sprk_billingexport` (`sprk.ApExport.BatchWritePrivilege.DisplayRule`, an `EntityPrivilegeRule` copied from `InvoiceWritePrivilege`) | `sprk_exportstatus` = Completed (the existing `batchIsCompleted` rule; false when it cannot be read) |
| Mark Sent to AP | same | same |
| Download CSV / JSON | existing record (unchanged: a user with Read can still download) | Completed (unchanged) |

Display only; the server checks Read and Write on the batch (EB-D-183). After the dialog closes the script refreshes the form and its ribbon, so the Sent to AP Date field updates.

### Views

- `sprk_billingexport` Active view ("Active Billing Exports", `{c19542df-...}`): `sprk_senttoapdate` after the total, in the layout and the fetch; the same column added to the `sprk_billingexport` spec in `Deploy-EbillingFormsViews.ps1` (that script owns the Active view's columns in dev, so the two stay in step).
- 151b invoice views "Sent to AP" (`{cd153641-...}`) and "Paid" (`{b0feb580-...}`): a `link-entity` on `sprk_billingexport` (`from=sprk_billingexportid to=sprk_billingexport`, alias `export_batch`, `link-type="outer"`) with `sprk_senttoapdate`, and the layout cell `export_batch.sprk_senttoapdate` after the Export Batch column. Outer join, so an invoice without a batch still lists. "Approved - ready for export" has no batch by definition, so it is unchanged. No other view was changed.
- No per-invoice column (EB-D-182 (3)).

## Component justification (CLAUDE.md section 11)

**Email to AP step (`BatchEmailView`) and its services**
1. Existing: the 151b export result had Email to AP disabled "not available yet"; no surface calls 151c-1a's route.
2. Extension: the step is a view inside the existing dialog page (a launch mode and a button that was already there); the error mapping follows `exportFlow.ts`; no new page, library or BFF call.
3. Cost of doing nothing: the route has no caller; a user cannot email a batch, and the refusals (monitored mailbox, too large, owner) have nowhere to be explained.

**Mark Sent to AP (section, dialog mode, ribbon button)**
1. Existing: Email to AP sets the date only when it sends; 151c-1a's route has no caller.
2. Extension: the same page and the same ribbon script and RibbonDiff as the other batch commands; one section component used by the step and the dialog.
3. Cost of doing nothing: a batch delivered another way (portal, hand delivery) can never be marked delivered, and the "date not recorded" state has no way out.

**Email to AP button on the batch form**
1. Existing: the batch form has only Download CSV / JSON (151b).
2. Extension: two more commands in the existing RibbonDiff and one function in the existing ribbon script.
3. Cost of doing nothing: a past batch cannot be emailed or re-sent (EB-D-181: "The Billing Export form gets ... Email to AP (re-send)").

**`createEmailSender` / `singleFlight`**
1. Existing: nothing guards a double click; `markPaidFlow` runs one call at a time inside a loop.
2. Extension: not extensible; React state is asynchronous and a second click can reach the handler before the button is disabled.
3. Cost of doing nothing: a double click can email AP twice (the one cost the brief names).

**Sent to AP Date on the views** — extends the existing batch Active view and the two 151b views; the cost of doing nothing is the owner's EB-D-182 (3) requirement (a user knows when an invoice was sent to AP).

Complexity (CLAUDE.md 11.5): `BatchEmailView` is one cohesive step (read, compose, send, result); the rules that can be wrong (mapping, validation, the no-re-send state) are in pure services with tests; the view only renders them.

## ADR-050 / modal standards

Same as 151b: the ribbon script launches with `navigateTo`, the platform dialog carries the title and close; the page renders no second `Dialog`. The step uses the footer contract (Back and Close left, Send to AP right). The Mark Sent action is a section inside the dialog, not a `ConfirmModal` on top of it (a second dialog would double the header); it records a fact the user already holds and is safe to repeat, so no confirm step is added.

## Decisions to confirm

1. **AP address is read by the page** (`Xrm.WebApi`, `sprk_ebillingsetting`, `statecode eq 0`, top 1), a display read like 151b's invoice read. No new BFF call, as the brief required. A user who cannot read the settings sees "unknown" and the server answers; the address is organization-owned configuration, not a secret.
2. **"Offer only Download" when no address is set.** Download stays; the CC / message / Send controls go. Mark Sent to AP also stays, because the not-configured sentence tells the user to use Download plus Mark Sent to AP, and the date is the only record that the batch left.
3. **No answer from the server (network, timeout).** The email may or may not have gone, so the dialog does not offer a retry. It says so, points to Sent Items and offers Mark Sent to AP / Download. This is stricter than the brief's two retryable codes and stays on the side of not emailing AP twice.
4. **Unmapped codes and `caller_unresolved` / `ebilling.disabled` are not retryable** (the brief lists only the two 503 codes as retryable).
5. **CC count is checked on the raw entries (blank entries ignored), before de-duplication.** A list of 11 with a repeat is refused on the client; the server's own order of count and de-duplication is not in the notes. The server stays the authority and its 400 is mapped.
6. **Re-send of a batch that already holds a date is allowed** (EB-D-181 "re-send"; the server leaves the date unchanged). The step says so before sending.
7. **EB-D-184 consequence for the UI test:** the AP email is owned like its batch, so it does not appear in the sender's own Messages thread. The 151c PR cites EB-D-184.

## Tests and build

| Check | Result |
|---|---|
| `npm install --legacy-peer-deps --no-audit --no-fund` (ApExport) | OK |
| `npm test` (jest, node env) | 7 suites, 151 tests, 0 failed (77 before; 74 added) |
| `npm run typecheck` / the build's tsc gate | Surface-owned errors 0 (100 pre-existing shared-lib errors, the gate's known baseline) |
| `npm run build` | `dist/sprk_apexport.html`, 1,024,470 bytes (gzip 310 kB); 151b: 1,016,781 bytes |
| ArchTests filter (WebResourceMirrorParity, SecureChildNewCommand, InvoiceStatusRetirement, SpaarkeMasterApplicationDependency, ExternalSpaGridViewSelector) | 284 passed, 0 failed |
| `npm run lint` | Not runnable: the package has an `eslint` script but no ESLint dependency or config, and `scripts/quality/client-lint` does not cover `src/solutions/ApExport` (its PACKAGES list has SpaarkeAi, LegalWorkspace, Reporting). Same as 151b. The typecheck gate and the pre-commit prettier pass ran |

What the tests pin (`emailFlow.test.ts`, `markSentAndBatch.test.ts`, `ribbonScript.test.ts`):
- every error code of the notes: sentence present, next step, and the "Try again" / "Do not try again ... Download ... Mark Sent to AP" wording; the six non-retryable 409s never allow another send; only the two 503 codes are retryable (plus the 400, to correct input); the exact 404 sentence; sign-in and no-answer cases; an unmapped code keeps the server words;
- the three 200 states, including the exact "date could not be recorded" sentence;
- the CC and message rules (display name, two in one entry, missing @, exactly 10 / 11, exactly 2000 / 2001, repeats, separators);
- the sender: a double click calls the api once; no re-send after a 200 whose date was not recorded, after any 200, after a final failure, a 404, a sign-in failure or no answer; a retryable failure and a 400 leave it open;
- Mark Sent outcomes; the two api calls (URL, body, no empty message); the batch and AP address reads (set, none, unknown); the launch modes;
- the ribbon script in the `vm` sandbox (`openFromBatchForm`, refresh, new form, unknown mode, the invoice commands refuse the batch modes, version 1.1.0, mirror equality); the RibbonDiff wiring (the Write rule's XML, both commands' display and enable rules, their calls, Download without the Write rule); the three view files.

Not run: component rendering (the package has no DOM test harness; RuleReview's and 151b's pattern is logic tests only) and the manual UI steps below (they need the deploy).

### Fail-before proofs (production line changed, tests red, restored)

| Mutation | Red |
|---|---|
| `createEmailSender` never locks after a send | 3 |
| `createSingleFlight` ignores the busy flag (double click) | 2 |
| `emailSuccess` never takes the "date not recorded" branch | 2 |
| `attachments_too_large` removed from the mapping (falls to the generic case) | 1 |
| Write display rule removed from the Email to AP command | 1 |

All restored; 151 green. New files have no earlier version to copy back.

## Files

- `src/solutions/ApExport/src/`: `App.tsx`, `components/ExportDialog.tsx`, `services/launch.ts`, `services/apExportApi.ts` (modified); `components/{BatchEmailView,MarkSentDialog,MarkSentSection}.tsx`, `services/{emailFlow,markSentFlow,batch,singleFlight}.ts`, `__tests__/{emailFlow,markSentAndBatch}.test.ts` (new); `__tests__/ribbonScript.test.ts` (extended)
- `src/client/webresources/js/sprk_apexport_ribbon.js` and `src/dataverse/solutions/SpaarkeMaster/WebResources/sprk_/scripts/apexport_ribbon.js` (identical)
- `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_billingexport/RibbonDiff.xml`, `.../sprk_billingexport/SavedQueries/{c19542df-...}.xml`, `.../sprk_Invoice/SavedQueries/{cd153641-...}.xml`, `{b0feb580-...}.xml`
- `scripts/ebilling/Deploy-EbillingFormsViews.ps1` (one view spec line)
- `projects/ebilling-r1/tasks/151c1b-email-ui.poml`, this file

No BFF change. The ribbon web resource keeps its id `{a20517ef-dd2a-4461-822b-f80c1d735e69}`; no new root component.

## Deploy (main session; nothing here was deployed)

In order:

1. **Prerequisites on the target environment:** the 151c-1a columns `sprk_billingexport.sprk_senttoapdate` and `sprk_ebillingsetting.sprk_apexportemail` exist (dev: yes) and the BFF carries the 151c-1a routes. Set the AP address on the settings record (a monitored mailbox is refused by the server).
2. **Code page** (web resource `sprk_apexport`, built from this branch): `cd src/solutions/ApExport`, `npm install --legacy-peer-deps --no-audit --no-fund`, `npm run build`, then `scripts/Deploy-WebResourceInline.ps1 -DataverseUrl <org> -WebResourceName sprk_apexport -FilePath dist/sprk_apexport.html -DisplayName "AP Export"` and publish. (The page has no embedded version; package version 1.0.0 is unchanged. 1,024,470 bytes.) The new `mode=email|marksent` launch is read by this build only, so the page goes before the ribbon.
3. **Ribbon script web resource** `sprk_/scripts/apexport_ribbon.js` **version 1.1.0** (`Spaarke.ApExport.Ribbon.VERSION`), same web resource id `{a20517ef-dd2a-4461-822b-f80c1d735e69}`, from `src/client/webresources/js/sprk_apexport_ribbon.js`. It must be live before the RibbonDiff that calls `openFromBatchForm`. The Mark Paid / Export commands keep working with 1.0.0 callers (no signature changed).
4. **Ribbon and views**, by the narrow route (ribbon-edit) or a SpaarkeMaster import (check with the owner first: dev is the authoring environment): a temporary solution with `sprk_billingexport` (RibbonDiff + the Active view `{c19542df-...}`) and `sprk_invoice` (views `{cd153641-...}` and `{b0feb580-...}`). The batch Active view's columns can alternatively come from `pwsh scripts/ebilling/Deploy-EbillingFormsViews.ps1 -WhatIf`, then live (the spec has the column); the invoice views have no script, only the solution source.
5. Publish all, hard-refresh the app.

**Unverified until imported:** the linked-entity layout cell name `export_batch.sprk_senttoapdate` (the platform's `<alias>.<attribute>` form for a custom alias). If the grid shows an empty column, open the view in the view designer, add Export Batch > Sent to AP Date once and compare its cell name; the fetch side is standard.

## Manual UI test steps (after the deploy)

Run as a finance user with Write on `sprk_billingexport` unless stated.

1. **Export, then email.** Export two Approved invoices. In the result, Email to AP is enabled; click it: the batch, count and total, "To (AP)" shows the settings address, Download CSV / JSON work, Back returns to the result.
2. **Send.** Add a CC and a short message, Send to AP: "Sent to AP", the Sent to AP Date and the recipients (To and CC). The Send button is gone. The email arrives with both files; open the batch: Sent to AP Date is set; the invoice views "Sent to AP" / "Paid" show the batch's date in the new column. The email is not in your own Messages thread (EB-D-184).
3. **Double click.** On a second Completed batch, double-click Send to AP: one email only.
4. **Validation.** CC `Jane <j@x.com>` and `a@x.com b@y.com`: refused with the CC message. 11 CCs: refused. A message of 2001 characters: refused with the count.
5. **Re-send.** Open the batch form of the sent batch, Email to AP: the step says it was already sent; send again: "Sent to AP", "already set and is unchanged"; the date does not change.
6. **No address.** Clear the AP export email in the settings (or use another environment): the step shows the not-configured message and Download only; Mark Sent to AP is still offered.
7. **Refusals.** Put a monitored mailbox in the AP address (or a CC): the monitored-mailbox sentence, no retry; for a retryable code (stop the mail service in a test environment) the sentence ends "Try again" and the form is still there.
8. **Mark Sent to AP.** On a Completed batch with no date, ribbon Mark Sent to AP: the dialog, the button, "Marked Sent to AP" with the date. Again: "Already marked Sent to AP", date unchanged. On a Generating or Failed batch both buttons are disabled.
9. **Rights.** A user with only Read on `sprk_billingexport` does not see Email to AP or Mark Sent to AP but still sees Download. A user with Write who lacks the right on this batch (other business unit): the buttons show and the dialog says "This export batch isn't available to you."
10. **Dark mode (ADR-021).** Switch to dark and open the step (compose, sent, each message bar, the CC and message fields); no white panels or black text inside the page. Check the browser console for errors in each step.
