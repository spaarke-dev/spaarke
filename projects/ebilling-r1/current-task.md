# Current Task State

> **Last Updated**: 2026-11-03 ~21:40Z. Session ebilling-r1-7b. Work head pushed; see `git log -1 origin/work/ebilling-r1`. The dev BFF is on 5796774091 (Healthy), which predates everything listed under "Merged into work".

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Parallel waves of task-execute agents, max 4 (owner, 2026-11-03). Merge each finished branch into work, launch the next ready task, one adversarial review per change set (two for auth). |
| **Running agents** | 118b-2 staging (Opus). 161 calibration report (Sonnet). 086 rebase onto 089a (Opus; brings the prvReadsprk_StoredFile role row; then a second access review over 089a+086). Merged since the last checkpoint: 111 fix (110c6de472), AP export fix (a6ff149cd9; uac-r2 PASSED, goes in the master PR), 089a fix (27e9cdc90d). |
| **Live blocker (now)** | sprk_billingexport is NOT a component of any app on dev (source AppModule.xml:49 and sitemap subarea_eb_export have it; dev doesn't). So the owner can't open Billing Exports or click Email to AP, and it probably also hides the invoice grid's Export to AP (the display rule is EntityPrivilegeRule Create on sprk_billingexport). The published ribbon does contain the button. An agent (Sonnet, worktree) is writing `scripts/ebilling/Add-BillingExportToMatterManagementApp.ps1`: additive AddAppComponents plus a sitemap SubArea insert, PublishXml of the app and sitemap only; -WhatIf/-Verify run read-only; -Apply needs -OwnerApproval. Next: send uac-r2 the -WhatIf output and diff; the owner's go comes in uac-r2's session, then -Apply; then check the Export to AP button, and file a defect if it is still hidden. |
| **Live test in progress** | First Email-to-AP test on dev, owner-approved. Both invoices are Approved. Batch **14680609-b198-48e2-8391-3ada84fda689** is Completed, owned by BU1, created by demo@ at 21:55:05Z by the -ExportOnly script (POST export-batches straight to the BFF; the grid button is still untested from the UI). Source on master already has the table, sprk_communication and the subarea (ac920d4184), so dev lost them later. Owner batches f6788e95 and ac07ec43 stay Failed (root BU; owner: leave them). Next, after the app fix: the owner opens the batch form, checks there is no + New on the AP emails grid, and clicks Email to AP; then send uac-r2 the UTC time and record the result in notes/153. The AP address is ap@finance.spaarke.com. |
| **Next action** | (1) When the export fix lands: send uac-r2 the diff for its 4-point check (same RestrictedContentOwnerTeam question and acting user; a fault is 503; nothing written before the check; tests for root 409, fault 503, BU1 pass). (2) Merge the 111 fix, 118b-2 and the 089a fix as they report. (3) Then the master PR batch: re-run the filtered gate on the work tip, measure the publish size against a fresh master, and open ONE master PR. Reviewers: ontology-r1, uac-r2, R3. Auto-merge after approvals. (4) The owner's dev go: SPA-r3 BFF deploy (after -Verify of the creator-person schema), source-email field security (-WhatIf first), the backfill, the 151e web resource and forms; the 112 deploy of the review page. (5) After that deploy: the NFR-05 census once, output to uac-r2. |

## Merged into work this session (not on master)
- 095a/095b plus 2 fix rounds; the gate fixes in 82786ce7f3 (CallerPrivilegeAnswer zero value is Faulted; step-0 test clears its queries).
- 151e plus its fix round.
- 052.
- 110, its fix round 2, and 111 (review done; the fix round is running).
- 095c-1 plus its review fix round (6ffc056494): both keys kept in SpaarkeMaster source; the existing row is checked before firm_required; the script publishes per entity and re-counts before the firm-key delete.
- 089a (3450edd6dc): -WhatIf and -Verify passed, uac-r2 OK, probes not re-run (privileges applied 2026-10-27). The review found nothing fix-now on real paths; the fix round is running.
- 118b-1 (08ec7ee1dc): the shared document read, the contract, the caller Read check, sameness, and an interim 503 until 118b-2.
- The ontology-r1 billing-guideline evaluation set (PR #2545) and the regenerated manifest.
- notes/153 (the AP mailbox).

## Owner decisions 2026-11-03
- Firm optional (095c/095d): hold until a firm is set; assign-firm is a Decision Record (D-201); renumber; external visibility unchanged; schema option B.
- 118b: option A (stage at intake through one shared read; main-session decision, no schema).
- The AP test: invoices 05166 and 2025023029, accept all lines; AP address ap@ (never receive-enabled); export as demo@; leave the Failed batches.

## Open owner questions
1. Files section: a user who can't read the source email doesn't see that file. Keep it, or ask uac-r2 to change the rule?
2. 153 end-to-end test invoices.
3. The refused bell links to the submission. Confirm.
4. Triage the six guideline gaps (notes/evaluation-set/guidelines/gap-report.md).
5. Firm-name check: should intake flag an invoice whose printed firm (extracted, used in the invoice name) differs from the firm it is submitted under? Seen 2026-11-03: INV-2025-05166 reads "Garrison and Ellison LLP" but sits under Morrison Foerster LLP (67577f8c), with no finding.

## Peer state
- uac-r2 (38): waits for the export-fix diff and the AP send time; the NFR-05 census after deploy; prvReadsprk_StoredFile goes in 086's follow-up.
- ontology-r1 (a9): PR #2545 merged; we seed one firm per reference-data key and score 161/162.
- word-add-in (dc): got the 095c contract delta (2026-11-03, advance notice: not live until 095c-2..4). Still waits for the master PR and SHA.
- email-communication: no live session. Filed #2548 (two .eml documents for one message sent to the mailbox and saved from the pane).
- customer-provisioning-orchestration-r1 (09): the both-keys notice was sent, but the session HELD it (permission mode), so it was not delivered. Resend once that session accepts, or ask the owner.

## Ready queue
- 086 follow-up: rebase 086's three commits (6703ca3f3f, 6482277b9b, e88825a528) onto work without the earlier 089 commits; it carries the prvReadsprk_StoredFile role row.
- 095c-2 (after 118b-2: same files), then 095c-3/4, then 095d (gets the K1 hand-off: set sprk_trackingscope on assign).
- 118 (after 118b-2).
- 092 (after 095c-2).
- 089b, then 089c (with the "089c must" list from the 089a review).
- 161: report done (notes/calibration-report.md, 0cd37befa9); DEF-051 (#2547, GA date day-for-month) is filed for 162. Live S1 (~$26) or S2 (~$22) needs the owner's OK; then re-render. 162 after that.
- 112 deploy (WP4).
- Blocked: 121 (uac-r2 191) and 096 (word-add-in).
- File the ISS-005 timing ArchTest as flaky under load.

## Standing rules (see project CLAUDE.md)
- No bare git stash, no --no-verify. Live writes and role changes need the owner's OK. Schema runs need notices before and after; publishes are entity-scoped only.
- StoredFileReconcileJob stays off. ebilling never deploys the dev BFF (SPA-r3 does).
- Master PRs: ontology-r1 review, then auto-merge, then the SHA to peers.
- Max 4 subagents, and every launch states a model. Run the FILTERED unit gate.
- No bare jq, no grep -P. Use `python -I` with `sys.stdout.reconfigure(encoding='utf-8')`.
