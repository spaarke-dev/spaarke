# eBilling R1 — owner testing checklist

Updated 2026-11-13. Each section says when it can be tested. Report anything odd to the `ebilling-r1` session with the record and the time.

## A. Testable on dev now (if word-add-in-r1 has deployed the pane from PRs #2789 / #2793)

In Outlook, open an invoice email, then the Spaarke pane's Invoice tab.

- [ ] With no firm linked to the record, the tab asks for a firm before you can submit.
- [ ] The banner shows the firm's name.
- [ ] "Open in Spaarke" opens the record.
- [ ] Add a firm to the record in Spaarke, then click "Check again" in the pane. The pane picks it up without a reload.

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
