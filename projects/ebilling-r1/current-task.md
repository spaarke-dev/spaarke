# Current Task State

> **Last Updated**: 2026-11-03 ~20:30Z (checkpoint at 86% context). This session is ebilling-r1-7b. The work head is pushed; see `git log -1 origin/work/ebilling-r1`. The dev BFF is on 5796774091 (Healthy) and predates 095a/095b/151e.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Parallel waves of task-execute agents (max 4), at the owner's request 2026-11-03: "why are we not running task execute on these? and running in parallel?". Merge each finished branch into work, then launch the next ready task. Run one adversarial review per change set (two for auth). |
| **Running agents** | 089a (secure-child registration; stops at `Set-SecureRecordOwnerRolePrivileges.ps1 -WhatIf`, with no -Apply and no probes). 095c-1 (schema script and allocator, option B phase probe; not run live). An adversarial review of 110. |
| **Running gate** | `scratchpad/gate-filtered.sh` -> `scratchpad/gate-filtered.txt`, on the tree with the three 095a fixes (commit 1d5248b181). The earlier unfiltered whole-project run (gate-combined) had 404 failures, all load timeouts of 3-7 min; 3 of them reran green alone. The ISS-005 timing ArchTest fails under load only, so file it as a flaky test. |
| **Next action** | (1) When the filtered gate is green, measure the publish size against a fresh master (`scratchpad/pubsize151c.ps1` method). Then open ONE master PR for 095a+095b+151e. 052 and 110 are merged into work too; decide whether they go in this PR or wait for the WP4 PR. Reviewers: ontology-r1 (a9), uac-r2 (38), R3 (f2). Auto-merge only after approvals. (2) Ask the owner for the dev go: the SPA-r3 BFF deploy (after `Set-ChildRecordCreatorPersonSchema.ps1 -Verify`), `Set-EbillingSourceEmailFieldSecurity.ps1` (-WhatIf first, notice to uac-r2), the backfill (it covers 0d67bcc9 via source B), and the 151e web resource and forms (`Deploy-WebResourceInline.ps1 sprk_invoicefiles`, then `Import-SolutionScoped.ps1` for sprk_Invoice and sprk_billingexport). (3) Send word-add-in (dc) the PR number and SHA. (4) Write task 118b: the server adds `supportingDocumentIds` on POST from-document, with Stored File copies made through the persister's from-document path, as 094's from-email does. Amend 118: the CommunicationActions PCF "Link Invoice" button (CommunicationActionsApp.tsx ~697-705) also becomes Submit Invoice through the shared flow, and `launchCreate` drops 'invoice' (it opens the standard form today). 118 depends on 118b. Then launch 118b. (5) Answer ontology-r1's billing-guideline compliance-test message (owner option C; their four questions: folder and branch, personas, the 078 reference-data import format, a subset of about 120 invoices). |

## Merged into work this session (not on master)
- 095a/095b plus the fix round (F1-F4, K).
- 151e plus its fix round: rights fault 503, step-0 fault 503, Quick View shape, managed copies. The sprk_invoicefiles web resource is a 153 prerequisite.
- 052: the wizard is rewired; browser ui-tests wait for 112.
- 110: the Invoice Review page; the ui-tests and the CSP check for the jsdelivr PDF worker wait for 112.
- 095c run-1 notes.
- The three fixes from the second 095a review, in 1d5248b181:
  - the Step0Refusal test ordering;
  - `ReadPrivilegeResponse` is tri-state, so a bad 2xx body is Faulted;
  - the overlay `IsCitation` check (defensive and unreachable, so no test; K).

## Owner decisions 2026-11-03
- 053 steps 2-3 are done. INV-2025-78232.3 is Accepted, a correction of .2, with 22 lines, the Patent code set, and 16 fee lines flagged because they carry litigation codes (notes/053).
- Firm optional (095c/095d):
  - hold the invoice until a firm is set;
  - assign-firm is a Decision Record (ontology D-201);
  - renumber;
  - external visibility stays as it is;
  - schema option B: a sprk_trackingscope column plus a new key, with the old key retired only after the new one is Active.
- 095c is split into 095c-1..4. Main-session design in the POML:
  - phase probe P0/P1/P2;
  - the 9-step schema script;
  - a no-firm invoice skips resolution, firm coding and rule evaluation, with every line explicitly flagged rules.not_checked.
- 095d recomputes the flags and runs the full pipeline. A blocking failure found after assignment is HELD, not rejected.
- 089 is split into 089a/b/c:
  - the uac-r2 privilege answer is (a);
  - ontology D-202 sets the record-rule code to `BRR-{policyId:N}`;
  - 089c has an expected stop on stale base-code Signals; the owner chooses whether to leave them or close them as Superseded.

## Open owner questions
1. Files section: a user who can't read the source email doesn't see that file. Keep it, or ask uac-r2 to change the rule?
2. 153 end-to-end test invoices.
3. The refused bell links to the submission. Confirm.
4. Interim approval: none until 110/111/112 are deployed.

## Peer state
- word-add-in (dc): waiting for the 095a PR and SHA, and for the 095c contract delta (sentence "Submitted without a firm. A reviewer will add the firm before approval.", code ebilling.submission.firm_missing).
- uac-r2 (38): waiting for 089a's -WhatIf and the 095c schema notice (key columns, the backfill rule, race behaviour across phases).
- ontology-r1 (a9): reviews 095c-3 and the 089 Signals stop. Needs answers on the compliance-test collaboration.
- email-communication: tell them about the duplicate unfiled sprk_document rows when an email is also sent To: Spaarke Mailbox (not sent yet).

## Ready queue after the running agents
- 092 (waits for 095c-2: same files).
- 118b, then 118.
- 161.
- 111 (after 110).
- 089b, then 089c.
- 095c-2..4, then 095d.
- Blocked: 121 (uac-r2 191) and 096 (word-add-in).

## Standing rules (see project CLAUDE.md)
- No bare git stash, no --no-verify.
- Live writes and role changes need the owner's OK. Schema runs need notices before and after, and the publish is entity-scoped only.
- StoredFileReconcileJob stays off.
- ebilling never deploys the dev BFF; SPA-r3 does.
- Master PRs: ontology-r1 review, then auto-merge, then the SHA to peers.
- Max 4 subagents, and every launch states a model.
- No bare jq, no grep -P. Use `python -I` with `sys.stdout.reconfigure(encoding='utf-8')`.
- Run the FILTERED unit gate, not the whole project.
