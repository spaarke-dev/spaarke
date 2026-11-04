# Current Task State

> **Last Updated**: 2026-11-04 ~06:30Z. Session ebilling-r1-7b (authoritative). Work head pushed (`git log -1 origin/work/ebilling-r1`). Dev BFF is still master d763dc120e. **None of this work is on master or dev BFF yet.** Ribbon applied and verified on dev 04:37:54Z.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Parallel task-execute agents, max 4 (owner 2026-11-03). Merge each finished branch into work, launch the next ready task. One adversarial review per change set, two for auth. |
| **Running agents** | (1) 095c-5 + 095c-4 review fixes (Opus, task/ebilling-095c-5; -WhatIf only; -Apply needs the owner's go plus uac-r2 notices). (2) 089c change rule + removal (Opus, task/ebilling-089c; needs 2 adversarial passes). 089d merged at 0c438d4071; pass 1 done: F4 isinherited not pinned (dev roles default to 1), F4 guide missing the live-proof gate, K2 operator left in the team on a partial -Apply, K3 guide file pattern |
| **Live blocker** | None. Email to AP is on the batch FORM (open the record); the owner hasn't clicked it yet. When they do, send uac-r2 the UTC time and record it in notes/153. |
| **Next action** | 1) Answer the owner's 089d question 'why a team, not the role assignment?': check whether dev has modernized business units (role assigned from another BU, OrgDBOrgSettings). If on, a direct role assignment in the customer top unit replaces the team; take that to uac-r2 before the fix round. 2) 089d fix round: isinherited=0 plus -Verify, the guide's live-proof gate, member removal straight after team create, the file-pattern wording; then pass 2; then the update notice to provisioning-09 (it waits for 089d on master). 3) Merge 095c-5 when it reports, run its review, get the owner's go for -Apply. 4) Then the full gate and ONE master PR. |

## Merged into work this session (not on master)
- Earlier: 095a/b, 151e, 052, 110/111, 095c-1, 089a, PR #2545 eval set, the AP export owner pre-check a6ff149cd9 (uac-r2 PASS).
- 118b complete: 118b-1, 118b-2 (9156468030), review 1 fixes (12628890e3: filing check plus per-file 413), review 2 fixes (42044c1af4: 101 MB total budget, OpenAPI 413). Two reviews done. K2-c must close before StoredFileReconcileEnabled is turned on.
- 118 email UI (731bf1423b); its review fix round is running.
- 161 report (0cd37befa9): notes/calibration-report.md.
- App/sitemap source sync to dev (9a2ca1968d, bf85f93aee): whole area_finance, 9 entity components; uac-r2 OK. Script `scripts/ebilling/Add-BillingExportToMatterManagementApp.ps1` (-Verify passes). The owner added the tables in the app designer at 22:13Z.
- Billing-export-only-from-Export-to-AP (627575107f, b92530be4c, f801c16452), uac-r2 OK:
  - Mark Sent refuses a batch with no files or no invoices (409);
  - 7 ribbon hides (New, Delete, Save & New);
  - legacy CSV/JSON Document fields off the form;
  - `scripts/ebilling/Deploy-EbillingBillingExportRibbon.ps1` (dry run 7/7; rollback copy plus content compare added per ribbon-edit).
  - **Deploy order after master + BFF:** ribbon -Apply/-Verify, then Deploy-EbillingFormsViews.ps1. The owner's go is in uac-r2's session.

## Filed this session
#2560 (UAC share mirror passes more than Read on hard-deny rows; uac-r2 owns; replaces deleted #2559), DEF-051 #2547 (GA day-for-month dates), DEF-052 #2553 (job copy and .eml parts skip the size limit), #2548 (email-communication duplicate .eml), #2555 (Email Review OData passed to a FetchXML client).

## Owner decisions 2026-11-03
- No-firm invoices stay VISIBLE to external participants on the matter/project (095c notes §13); uac-r2 told.
- Firm optional: B; hold until a firm is set; D-201.
- 118b option A.
- The AP test: invoices 05166 and 2025023029, ap@finance.spaarke.com, export as demo@, leave the Failed batches.
- Batch 14680609 is Completed (BU1; made by the -ExportOnly script).

## Open owner questions
0. 118b K6: a Restricted supporting document hides the whole invoice from its firm (fail-closed). Confirm.
1. Files section rule (source email not readable).
2. Refused bell links to the submission.
3. Triage of six guideline gaps.
4. Firm-name check at intake (INV-2025-05166 prints Garrison and Ellison but sits under Morrison Foerster).
5. 161 live run S1 ~$26 or S2 ~$22.
6. A leftover "lint-staged automatic backup" stash entry 1e84ffafed (holds committed changes only); dropping it needs the owner's confirmation.
7. 089b: the owner said YES to rule Read organisation-wide. uac-r2's design is a Billing Rule Readers team in the customer top unit, with Deep Read on sprk_policy, to be built in task 089d. Not Global and not root-held (D-123). The 403 stays until the team exists. -Apply on dev needs the owner's go.
8. The owner asked whether to make the GitHub repo private. Answered: it is their call; Claude Code keeps working through authenticated gh and git.
9. 089d members: the OWNER ANSWERED that Billing Admin is a security role explicitly assigned to a limited set of users (operator adds; default OK). The owner asked: why a team rather than the role assignment? (pending answer). The owner changed provisioning-r1's permission mode (messages now deliver).

## Peer state
- uac-r2 (38): runs ALL dev BFF deploys now (R3 stopped); the procedure is docs/procedures/dev-bff-deploy-coordination.md. Deploy requests carry the master SHA, the PRs and the pre-checks; the next deploy also carries #2550. It waits for the Email to AP UTC time, the master PR, and the NFR-05 census after deploy.
- word-add-in (dc): building the no-firm pane on a branch. Prefill `firm: null`, `firmId: null` OK, firms route unchanged. Wants the master SHA AFTER the dev deploy that carries 095c-2..4.
- customer-provisioning (09): the both-keys notice EXPIRED undelivered (permission mode). Resend only when the owner asks.
- ontology-r1 (a9): reviews master PRs.

## Ready queue (after the running agents)
095c-3 → 095c-4 → 095d (K1: set sprk_trackingscope on assign); 092 (after 095c-2); 089b → 089c (after 086); 112 deploy; 161 live (owner OK); file the ISS-005 and ContactCreateOwnerPolicyGuard timing ArchTests as flaky under load. Blocked: 121, 096.
