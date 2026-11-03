# Current Task State

> **Last Updated**: 2026-11-03 ~16:05Z (checkpoint at 86% context). This session is ebilling-r1-7b. Work head ca795e6293 (pushed). #2530 merged into master as 1571b0be9d. The dev BFF is on 5796774091 and Healthy.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Running** | Work head f9974dcfe6 (pushed) = the 095a/095b fix round (aa4e8dd00f6736d92: F1-F4 + K fixed, agent gate green: ArchTests 1928, unit 9731/0/6) merged with 151e (a222e9efd2d39dbc2: Files section + 151d Quick View; agent gate unit 6418/0/5, ArchTests 1918/1 = the load-sensitive ISS-005 timing test, passes alone). Running now: the combined gate (`scratchpad/gate-combined.sh` -> `gate-combined.txt`), an adversarial review of 151e, and a SECOND adversarial pass on the 095a/b fix diff (auth tag). |
| **Next action** | Fix any F-class findings. Then measure publish size against a fresh master (`scratchpad/pubsize151c.ps1` method). Then open ONE master PR for 095a+095b+151e. Reviewers: ontology-r1 (a9), uac-r2 (38: F2 field security, the 151e list rule), R3 (f2: the O-32 point). Auto-merge only after approvals. Ask the owner for the deploy go plus OK for (a) `Set-EbillingSourceEmailFieldSecurity.ps1` on dev (-WhatIf first, notice to uac-r2), (b) the backfill script, and (c) the 151e web resource and form import (`Deploy-WebResourceInline.ps1 sprk_invoicefiles`, then `Import-SolutionScoped.ps1` for sprk_Invoice + sprk_billingexport, entity-scoped publish). SPA-r3 deploys the BFF after -Verify. Send word-add-in the PR number and SHA. Then start 095c (opus/high), then 095d. |

## Owner decisions 2026-11-03
- 095a and 095b approved. The channel is the bell (appnotification). The pane banner is word-add-in's.
- Column `sprk_invoicesubmission.sprk_sourceemaildocument` (Lookup to sprk_document) was created by the owner on dev. Relationship `sprk_invoicesubmission_SourceEmailDocument_sprk_document`: Delete/Archive RemoveLink (I fixed Archive), others NoCascade. It is in SpaarkeMaster through the table root (behavior 0). After-notices sent. Lookup-name exposure: I recommended accepting it, and the owner didn't object.
- The Files section on the invoice form is wanted ("important"), with a link to the source email Document. That is task 151e, which folds in 151d.
- The owner's go for word-add-in's Outlook desktop sign-in fix was relayed.
- Correction test PASSED: INV-792345.4 supersedes .3 (.3 is Superseded/inactive; the .4 submission has resubmissionof = .3's submission). The -0.03 total is correct (a Microsoft credit note).

## Owner decisions 2026-11-03 (firm optional)
- Firm optional on Submit Invoice from Outlook (relayed by word-add-in): hold the invoice until a firm is set; the firm assignment is a Decision Record (ontology-r1 agrees the kind); renumber into the firm's sequence if secure. Tasks 095c (intake, hold, approval refusal) and 095d (set-firm action). 095c starts after the 095a/095b fix round merges (same files). word-add-in has a holding answer; the pane change waits behind their flag for the contract delta.

## Open owner questions
1. Refused bell: link to the submission (per the POML, being implemented) or to the invoice? Not answered.
2. Approve test invoices for the 153 end-to-end (needed for the AP-emails "no New" click-through and the first Email to AP send; uac-r2 wants the batch link and the send time).

## Just observed (16:00Z)
The owner emailed synthetic invoice B-0001.pdf (INV-2025-78232) to the Spaarke Mailbox (mailbox-central). The inbound communication pipeline processed it twice (polling job a80bbd36 and delta reconciliation d5737ca3): StructuralDetector flagged invoice@0.80, association Suggested at 0.70. **No invoice submission was created**: the mailbox route does not submit invoices (Submit Invoice from the communication is task 118, not built). A sprk_communication query by name found nothing yet. Tell the owner to use Submit Invoice in the Outlook pane on that email. Check for duplicate communication rows from the double processing (email-communication-intelligence's area).

## Known on dev (not defects)
- 053 steps 2-3 done 2026-11-03 (owner OK): the invoice-line-code and invoice-codeset-match Actions and Bindings are on dev. The owner resubmits INV-2025-78232 from the Outlook pane (expect .2: an invoice on the matter, 22 lines coded); then verify the lines, header, totals and code set. Not seeded: billing-rule-draft (needs its own OK).
- No invoice lines on the two Microsoft invoices: expected, since they have no law-firm lines.

## Filed
- #2529: the submission job retries a permanent schema fault 5 times.

## Waiting on
- The owner deletes `C:\w15p`.
- Task 153: the AP-emails grid "no New" check moved into its end-to-end (needs approved invoices and a batch).

## Open items
- 153 step 4 already amended (SPA-r3 deploys). The rest of 153 waits for 136.
- Task 014 note §7: the next `sprk_invoice` RibbonDiff import must decide on the 7 Activate/Deactivate hides.
- 132 must treat the submitter's bell as done by 095b (095b K-note).
- Older notes name the owner in plain text (003, 004, 011, 029, 091): needs a docs pass.
- 050a §9 items 2-4 (owner).

## Standing rules (see project CLAUDE.md)
No bare git stash, no --no-verify. Live writes and role changes need the owner's OK. Schema runs need notices before and after, and the publish is entity-scoped only. StoredFileReconcileJob stays off. ebilling never deploys the dev BFF (SPA-r3 does). Master PRs: ontology-r1 review, then auto-merge, then the SHA to peers. Max 4 subagents, and every agent launch states a model. No bare jq, no grep -P.
