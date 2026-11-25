# eBilling R1 — owner testing checklist

Updated 2026-11-21. Each section says when it can be tested. Report anything odd to the `ebilling-r1` session with the record and the time.

## A. Testable on dev now (pane from #2789 / #2793, plus #2913 live 2026-11-18)

In Outlook, open an invoice email, then the Spaarke pane's Invoice tab.

- [x] With no firm linked to the record, the Firm field is a law-firm search. Pick a firm and it is added to the record, then used for the submission. (Passed, owner, 2026-11-18. #2913 removed the old "Check again" and separate add button.)
- [ ] The banner shows the firm's name.
- [ ] "Open in Spaarke" opens the record.
- [ ] Submit a Correction with a note. The new invoice shows the saved note (Correction Note field).

## B. After task 112 deploys 084 to dev (Procedures on a matter, project or work assignment)

Sign in as a **billing administrator** unless a step says otherwise.

**Impact preview (084d-4)**
- [ ] Open a Procedure's wizard (Follow, Pin or Exclude) and go to Impact. It shows "if saved now", with before and after, and how many open invoices are affected.
- [ ] If it times out or fails, Retry works. A count that can't be computed reads "can't be counted", not 0.

**Assign Procedure (084d-5)**
- [ ] The "Assign Procedure" button shows in the Procedures header for a billing admin.
- [ ] As a user who is not a billing admin, the button is not there at all (not greyed out).
- [ ] The list shows Procedures not yet on the record, each with the status it would have here and its version.
- [ ] A Procedure with no version on today's date shows a warning that it will hold invoices.
- [ ] A Procedure that already has an override on this record is greyed out, with the reason.
- [ ] With more than 50 matches, you get a search box instead of a list. Typing part of a name narrows it.
- [ ] Draft-only Procedures, and Procedures for another firm, are not listed. A note says why.
- [ ] Switch the panel's firm. The list changes to that firm's Procedures.
- [ ] Assign one with Follow latest, then one with Pin (it asks for a version and a reason). Impact shows the change. After Confirm, the Procedures list refreshes with it.

**Refusals**
- [ ] Deactivate a Procedure in Dataverse while leaving its Status Active. It disappears from the Assign list.
- [ ] Assigning or pinning that deactivated Procedure, by any route, is refused with a "deactivated" message.
- [ ] Changing one of its rules is refused as "not in force". Excluding one of its rules still works. Removing its assignment still works.
- [ ] A Procedure whose Area is not Billing cannot be assigned.

**Procedure-change banner on the Invoice Review page (113b, needs the dev BFF with #2839)**
- [ ] Open an invoice in review whose firm has a Procedure. With nothing changed since it was checked, no banner shows.
- [ ] Change that firm's Procedures (for example pin a different version), then reopen the invoice. A warning says the procedures have changed and need a re-check. There is no re-check button yet (R2).
- [ ] Decisions and corrections still work while the banner shows.
- [ ] A decided invoice shows no banner.

**Invoices (ISS-021)**
- [ ] On a record that has the deactivated Procedure, submit or re-run an invoice. That Procedure's rules no longer produce findings.

## C. After field-mapping-r1's 045g reaches dev (invoice filing, EB-D-231)

- [ ] In the Create Matter wizard, "Associate To" no longer offers Invoice.
- [ ] Moving an invoice to another matter or project through Spaarke is refused. The message says to return it to the firm and resubmit.
- [ ] Approve, Reject, Return and corrections on an invoice still work as before.

## D. 095e and 095f: ready to test on dev (deployed 2026-11-14 ~15:19Z, BFF 5a9b687a0b)

- [ ] A submission with no firm is refused in every channel (portal, internal wizard, Outlook pane), and nothing is created.
- [ ] A new invoice's name reads "{firm name} - {invoice number}" (or the date when there is no number), whatever the PDF says.
- [ ] Invoices already waiting on "Set firm" can still be given a firm.
- [ ] After Set firm on a held invoice, new findings in the Worklist show the new invoice name.
- [ ] In the Outlook pane, a Correction asks for a note. Without one it is refused. With one, the new invoice shows the note (Correction Note field).

## E. 093 invoice files in the external portal: after uac-r2's dev BFF deploy and spa-r4's SPA deploy

Not yet. #2839 (BFF) and #2840 (SPA) are merged to master. uac-r2 deploys the BFF on your go in its session, then spa-r4 deploys the SPA straight after. Sign in to the external portal as a law-firm user.

- [ ] Open one of your firm's invoices. Its files are listed with name, type, size and date.
- [ ] A file that is still being virus-scanned shows as checking, and opening it says it isn't ready yet. Once the scan is clean it opens.
- [ ] A clean file opens in the browser.
- [ ] An invoice of another firm cannot be opened. It looks the same as an invoice that doesn't exist.
- [ ] Nothing in the portal still calls the old invoice-documents address (no "not found" error in the files section).

## F. Decision notes (095f part 2): after the decision-note columns are applied (open question (c)), then a BFF and an Invoice Review page that carry 095f-2a..2ui

Nothing here is testable until those deploys run. On an In Review invoice in Invoice Review:

- [ ] Approve, Reject and Return each show an optional **Note** box with an n / 2000 counter. A note over 2000 shows a field error, and nothing is cut off silently.
- [ ] Line decisions (accept, reduce, disallow), Correct value and Confirm as read each take a note. **Accept all Clean has no Note box.**
- [ ] Add adjustment and Remove adjustment take a note.
- [ ] On a blocked internal submission, Override and Return take a note.
- [ ] A note with only spaces is treated as no note.
- [ ] After a decision, the note appears on its Decision Record (and the reject/return note appears on the invoice's decision note). The firm does NOT see the note in the SPA.
- [ ] Confirm a value that is already confirmed, with a note: the page says the note was not saved (instead of "Saved").

## G. Finish a stranded decision as operator (ISS-034, iss034a + iss034b): after a BFF and an Invoice Review page that carry them

Not yet deployed. You need a held invoice whose Override (or blocked Return) by person A was recorded but did not finish; sign in as a billing admin B.

- [ ] B sees **Finish as operator** on the hold banner, naming A's pending Override or Return. A non-admin, and A themself, do not see it.
- [ ] Finishing needs a reason (note optional). Afterwards the invoice is released (or returned) exactly as A recorded it, and the Decision Records show A's decision plus B's "finish" step.
- [ ] If the page says the decision was saved in part, the form stays open with that message; repeating it finishes the close.

## H. Submit a corrected invoice from Invoice Review (095f-2e2a server + 095f-2e2b client): after a BFF and the Create Invoice wizard / Invoice Review pages that carry them

Not yet built end to end (2e2b in progress).

- [ ] On an In Review invoice, **Submit corrected invoice** opens the Create Invoice wizard with the firm invoice number filled in and the record locked.
- [ ] The wizard asks for a note; without one it refuses.
- [ ] After upload, the new invoice gets the next tracking number (`number.2`), shows the note, and the old invoice becomes Superseded.
- [ ] A double click on submit sends one correction only.

## I. Correction rows are writer-only on dev (iss028, applied 2026-11-17)

- [ ] As an ordinary user (Spaarke Core User), open an invoice that has a correction. Try to delete it from the model-driven app. **Expect** a refusal (no privilege on the correction rows). End invoices by status instead.
- [ ] As the same user, open an invoice-correction row directly (Advanced Find or the table view). **Expect** read-only: no Save, Delete, Assign or Share.
- [ ] In Invoice Review, correct a line value and confirm a line. **Expect** both to save, with the correction rows written as usual (the app writes them, not the user).

## J. Finish a decision that did not finish (iss032): after a BFF and an Invoice Review page that carry it

These states are hard to cause by hand; they need a fault during a decision. If you see one in normal use, test it then:
- [ ] An invoice showing Rejected, Returned or Approved with the "This decision did not finish" banner: **Finish decision…** opens a form. Before the decision was recorded, it asks for the reason again and says why. After it was recorded, the reason is shown locked. Submitting finishes the decision, and the page then shows "View decision".
- [ ] A decided invoice with no banner shows only "View decision" (nothing changed for complete decisions).

## K. "Finished by" on the History tab (iss034d): after a BFF and an Invoice Review page that carry it

- [ ] A blocked Override or Return that one person recorded and an operator finished (section G): its History entry shows **Finished by {operator}** under the decision.
- [ ] A blocked decision its own decider finished, and any decision made before iss034c, shows no "Finished by" line.

## L. Rule set "Visible to Firms" is set only by Spaarke (ISS-040): applied to dev 2026-11-17, ready to test

- [ ] As an internal user, open a rule set in the model-driven app. The "Visible to Firms" value still shows on the form and in the view.
- [ ] Try to change "Visible to Firms" directly on the form. **Expect** it to be read-only (only Spaarke changes it).
- [ ] Create a rule set in the model-driven app. **Expect** the save to succeed with "Visible to Firms" = No.
- [ ] In Rule Review, switch a rule set's visibility on and off. **Expect** it to work as before.

## M. Notifications (task 132): ready to test on dev (BFF 5ae2804884 live ~02:01Z 2026-11-19; firm notice Action seeded ~02:05Z)

Firm emails come from the shared mailbox. Reject and Return emails go only when the reviewer confirms a notice. The step that does that is built (132ui) but is not on dev until task 112 deploys the Invoice Review page (see M2).
- [ ] Submit an invoice that the checks reject. The submitter (and the firm's billing contact, if they can open that matter) gets one email with the findings table in the body.
- [ ] Approve an invoice with an adjustment. The same two people get one email; approving with no adjustment sends none.
- [ ] A billing contact who has no access to a secure matter gets no email for that matter's invoice.
- [ ] In the external portal, the bell shows an item (with the invoice name) for your own Rejected / Returned / adjusted invoices only; another firm member who neither submitted nor is the billing contact sees none.
- [ ] Approvers get one in-app notification "Invoice ready for your review" per invoice; non-approvers get none.
- [ ] With eBilling switched off, the portal bell shows no billing items.
- [ ] The "notice text" field is locked (applied to dev 2026-11-17, ready to test now). As a non-admin user, the submission form shows the field masked (\*\*\*\*\*) and you can't edit it. It disappears from the form once the SpaarkeMaster release with task 132's form change is imported.

### M2. Notice to the firm on Reject / Return (task 132ui): after task 112 deploys the Invoice Review page to dev (owner 2026-11-23: wait for 112; the page has never been on dev)

On an In Review invoice, choose Decide → Reject or Return to firm, give a reason, then Next:
- [ ] A **Notify firm** step shows the drafted subject and message, and says whether the AI wrote it or the standard wording was used. Approve never shows this step.
- [ ] Edit the message and finish. The firm's email carries your edited text plus the findings table, which Spaarke adds.
- [ ] Type a figure or an amount into the message (for example "$500" or "two lines"). Finishing is refused with a message under the notice, and nothing is decided. Remove it and finish again.
- [ ] Turn "Send this notice to the firm" off, or Skip the step. The Confirm step says the firm gets no email, and none is sent. The decision is still recorded.
- [ ] Switch between Reject and Return. Each keeps its own draft and edits.
- [ ] "Restore draft" puts back the drafted text.
- [ ] (ISS-041) A stranded Reject or Return finished after a page reload: if a "Finish decision…" banner shows on an invoice (a Reject or Return that stopped part way), reload the page and use the banner. The finish form offers the same Notify fields, and the firm's email carries the notice you confirm there. Turning sending off sends none. Hard to set up by hand; skip it if no banner appears.

## N. Billing Guidelines in the external portal (task 135): the dev BFF carries it (5ae2804884); test after spa-r4 deploys the external SPA

Dev has no rule sets yet, so this needs a published Billing rule set first.
- [ ] As a firm user, open the Billing Guidelines card. It lists the published billing rules in force for your firm(s), with rule set name, version, section and quoted clause text; never a document link.
- [ ] A user linked to several firms sees all of them, and the firm filter narrows the list (EB-D-260).
- [ ] A rule set with "Visible to Firms" off does not appear at all; neither does an Internal Standard Procedure's rule set.
- [ ] With eBilling switched off, the card stays and shows "Billing guidelines aren't available." (no error styling, no Retry).

## O. The firm's invoice outcome (tasks 130g and 134): after the dev BFF and external SPA carry both (deploy task 136)

Both are merged into the work branch. Sign in to the external portal as a law-firm user and open one of your firm's invoices; the record shows an **Outcome** section.
- [ ] An Approved invoice with adjustments lists each reduced, disallowed or added item with its reason or the quoted guideline, and the billed and approved totals.
- [ ] An In Review invoice says "Under review", lists only items already decided or added, and shows no approved total.
- [ ] A Rejected or Returned invoice lists its findings, each with its reason code, and shows no approved amount, per line or in total.
- [ ] **Print** opens the browser print dialog with only the report on the page (no portal chrome, nothing cut off), from the record and from the submit result screen.
- [ ] After you submit an invoice, the result screen shows the same report.
- [ ] With eBilling switched off, the Outcome section is not shown.
- [ ] Open one of your firm's invoices that a reviewer Rejected or Returned **with** a notice (M2). The page shows the message the reviewer sent, as plain text, without the subject and without the internal reason or note.
- [ ] An invoice rejected or returned **without** a notice (send off or Skip), an invoice the checks rejected automatically, and an In Review or Approved invoice show no message line.

## P. Legacy extraction removed (task 050b): after the next dev BFF deploy that carries it

Merged into the work branch 2026-11-22 (18b02cbdcf). Nothing visible changes; this is a regression check.
- [ ] Submit an invoice from the Create Invoice wizard: it is read and coded as before (In Review, lines filled).
- [ ] In Invoice Review, **Confirm** an invoice document from the review queue: it still creates the invoice through the submission (as 050a).
- [ ] The BFF starts with no warnings about `Finance:InvoiceExtractionPlaybookId` or `Finance:ExtractionDeploymentName` (both keys are gone).
- Owner choice, no test: the old **FinanceExtraction** (PB-013) playbook rows in dev Dataverse are now unused. Leave them, or tell me to deactivate them (a live change).

## Q. A firm submits through the external portal (task 053): PASSED 2026-11-24 (053 closed)
This closes task 053. After it, task 079 has no open dependency. The dev BFF and the external portal both run master (the portal was deployed 2026-11-23).
- 2026-11-24, first run: **Failed** on matter MAT-000113. The cause is that matter's dev setup, not the invoice: MAT-000113 and MAT-000065 cannot take invoices on dev. Re-run on another of the firm's matters, e.g. PAT-792345. In the form, type the invoice number as `2026024892` (the portal stores exactly what is typed in Invoice number and PO number) and leave PO number blank.
- 2026-11-24 ~16:10Z: the dev setup that blocked MAT-000113 was fixed (owner-approved, done by uac-r2), so MAT-000113 can take invoices again; PAT-792345 is still fine. Use a matter, project or work assignment that already exists; one newly created with no parent may still hit the same block (#3047).
- 2026-11-24 ~22:05Z, second run: **Passed** on MAT-000113. The invoice was accepted with tracking number INV-123.1.1, and in Spaarke it is In Review with 42 lines (1 Flagged) and an approval portion (checked read-only). Found: the lines carry no resolved UTBMS code in their code columns; ISS-051 #3080 is filed. The Invoice Review page was blank on dev because it had not been deployed; it was deployed ahead of task 112 at 22:48Z (section S).
- [x] Sign in to the external portal as a firm user who has access to a matter. Submit the sample `Accepted-sample-B-0050-INV-2026024892.pdf` (sent in the session; evaluation-set B-0050, invoice 2026024892) on that matter. The portal shows **Processing**, then **Accepted**, with a tracking number such as `INV-123.1`.
- [x] In Spaarke, that invoice is **In Review** on the matter. Its lines are filled in with codes, and it has an approval portion.
- Moved to task 079 (2026-11-24): the **Rejected** check. Dev has no published Reject rule yet; 079 step 10 publishes the first rules. Once it does, you submit `Rejected-sample-B-0082-INV-2025-34586-dated-2025-04-01.pdf` (sent in the session), which breaks a 180-day invoice-age rule, and the portal should show **Rejected**.
- [ ] The portal's submission screens look right in dark mode too.
- [ ] After the next portal deploy from master (iss048): the result screen's title follows the outcome ("Invoice accepted", "Invoice rejected", "Invoice not processed"). A failure caused by the record's setup no longer says "Submit it again".

## R. Office add-in no longer files to an invoice (task 096): live on dev since 2026-11-24 (BFF 996928732e)
- [ ] In the Outlook add-in, open Save and search for an invoice. It appears in the search results, but it cannot be chosen as the place to save the email.
- [ ] Saving an email to a matter or a project still works, and so does quick-create of a matter or a project.

## S. Invoice Review page on dev (deployed ahead of task 112, 2026-11-24 22:48Z)
Deployed with the owner's go: web resource `sprk_invoicereviewpage` built from master fa96792811 (sha256 08b373e0…ae43ee, read back from dev). Dev's BFF is 20129e985c, so the ISS-050 lock renewal (#3073) is not live yet; the ISS-041 Finish-step change is in the page. The web resource is in the Default solution only; task 112 adds it to SpaarkeMaster (AddSolutionComponent, type 61) so the next export carries it.
- [ ] In the Console, open the review worklist and open the invoice "Morrison Foerster LLP - INV-123.1" (MAT-000113). The dialog is no longer blank: the lines grid shows 42 lines (38 Fee, 4 Expense), one Flagged, with the PDF on the right.
- [ ] Click the Flagged line: the pane shows the finding (rule, severity) and the line's values on the PDF.
- [ ] The resolved UTBMS codes show on the lines (the page reads them from the coding result; ISS-051 is about other readers, not this page).
- [ ] Dark mode: the page follows it and the PDF page stays light.
- Optional, changes data (only if you want to try it): decide one line (Accept or Reduce with a reason), then Approve. Approve needs you to be an eligible approver on MAT-000113.

## T. Resolved codes on invoice lines (ISS-051): after the next dev BFF deploy that carries iss051
Once a dev BFF with iss051 is live, new and corrected lines carry the resolved UTBMS code in their task / activity / expense code fields. Existing lines need a one-off backfill, which is a live dev write that needs your typed OK. Run it only after that BFF is live, because today's dev BFF does not update the fields when a reviewer corrects a code. Report run on 2026-11-24: 80 lines on open (In Review) invoices, 139 fields to set; lines of Sent to AP and Superseded invoices are skipped so they keep matching what AP received.
- [ ] Owner OK, then `-Apply` and the re-index sweep the script prints.
- [ ] Open INV-123.1's lines in Spaarke: the Task / Activity / Expense code columns show codes.
- [ ] After the next AP export of a coded invoice, the export file carries the resolved code, not the firm's own text.

## U. Links on the portal's "Invoice received" screen (ISS-052): after the next dev BFF and external portal deploys from master
- [ ] Submit an invoice in the portal. When it is Accepted, the result screen shows **Open invoice** and **Open matter** (or project / work assignment). Each opens that record.
- [ ] Open the resume link (`/invoices/submit?submissionId=…`) of the same submission: the same two links show.
- [ ] From a matter's record view, use **Submit invoice**; on the result, **Open invoice** opens the invoice over the matter, and closing it returns you to the matter with focus on the Submit invoice button. **Open matter** just closes back to the matter.
- [ ] A Rejected submission shows no **Open invoice**, and its red bar says "The invoice was rejected." with no code like `invoice.read.unreadable` (the report below still lists rule findings with their descriptions).

## V. Task 079 on dev: Billing tab, Rule Review page (2026-11-25, by script; owner plan A)
Live on dev since ~02:20Z. The billing-rule-draft Action and Binding, the Billing-tab firm fields, the Rule Review page (`sprk_rulereviewpage`, built from master 2146b8fdaf) and its menu entry. There is no command on the rule-set form yet (DEF-069, #3112): open Rule Review from the menu.
- [ ] Open a matter's main form: the **Billing** tab has a "Billing firm" section with **Billing firm** and **Billing responsible**. Do the same on a project, and on a work assignment, which now has a Billing tab of its own.
- [ ] The forms still load as before: no script error, and the header and other tabs are unchanged.
- [ ] In the Spaarke Administration app, the eBilling group lists **Rule Review** after Rule Sets. It opens the page, which lists rule sets, in light and dark mode.
- [ ] Signed-in route checks (079 step 9, second half). I need a token for each user, so I do these with you:
  - As a billing admin, the WP3 reads answer 2xx.
  - As a non-admin, a write answers 403.
  - As a flagged-external test user, `/review` answers 403, and `/invoice-submissions/{id}` answers the same 404 for a real id and a made-up id.
  - The four retired `/documents` routes answer 404.
- [ ] 079 step 12: 077's UI tests against dev, including dark mode. This runs after the step 10 data exists.
