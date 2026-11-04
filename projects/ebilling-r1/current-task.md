# Current Task State

> **Last Updated**: 2026-11-04 ~02:30Z (local 2026-11-03 20:05). Session ebilling-r1-7b (authoritative; the idle -9c session is not). Work head pushed: f801c16452 or later (`git log -1 origin/work/ebilling-r1`). The dev BFF is master d763dc120e (live 22:26:31Z, uac-r2 gate passed). **None of the work below is on master or dev yet.**

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Parallel task-execute agents, max 4 (owner 2026-11-03). Merge each finished branch into work, launch the next ready task. One adversarial review per change set, two for auth. |
| **Running agents** | (1) 095c-2 F1 retry fix (Sonnet, fix/ebilling-095c-2-review). (2) 089a+086 review K4/doc fixes (Sonnet, fix/ebilling-086-review). (3) 089b review F1 system-rule fix (Sonnet, fix/ebilling-089b-review); 089b needs a 2nd adversarial pass after it. (4) 095c-4 readers + approval refusal (Opus, task/ebilling-095c-4). 095c-3 review done: no F; K1-K4 folded into 095c-4. **MASTER PR BLOCKED until 095c-4 merges** (a fresh SpaarkeMaster stamp reaches P2 without the script; a zero-line no-firm invoice is approvable). |
| **Live blocker** | The owner sees no **Email to AP** on batch 14680609. uac-r2 says the screenshots so far were the GRID (Email to AP is form-only). Dev checks out: ribbondiff/ribboncommand/ribbonrule rows present, apexport_ribbon.js has batchIsCompleted, which returns true on a fake form, and the location pattern matches invoice/matter forms. Waiting for the FORM's command bar screenshot after Ctrl+F5 (and under the ⋮ menu). If none of the 4 form buttons show, the cause is the group or the bar; if only Email is missing, its rules. |
| **Next action** | When agents report: merge, then build. When 095c-2, the 118 fixes and 086 are in: run the FILTERED unit gate on the work tip, measure publish size vs a fresh master, open ONE master PR (reviewers ontology-r1, uac-r2, R3; uac-r2 reviews the whole diff), auto-merge after approvals, send PR# and SHA to uac-r2 (and word-add-in, after the DEPLOY carrying 095c-2..4). |

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
#2559 (UAC share mirror grants Write/Delete/Share on hard-deny rows; uac-r2 owns), DEF-051 #2547 (GA day-for-month dates), DEF-052 #2553 (job copy and .eml parts skip the size limit), #2548 (email-communication duplicate .eml), #2555 (Email Review OData passed to a FetchXML client).

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
7. 089b: should Billing Administrator get Read on sprk_policy at Global, so child-unit admins can Exclude top-unit rules? Until then the 403 is correct (uac-r2: a role change, not a filter).
8. #2559 contains an access-escalation scenario in a public repo; uac-r2 has taken to the owner how it is tracked. Do not edit it until the owner decides.

## Peer state
- uac-r2 (38): runs ALL dev BFF deploys now (R3 stopped); the procedure is docs/procedures/dev-bff-deploy-coordination.md. Deploy requests carry the master SHA, the PRs and the pre-checks; the next deploy also carries #2550. It waits for the Email to AP UTC time, the master PR, and the NFR-05 census after deploy.
- word-add-in (dc): building the no-firm pane on a branch. Prefill `firm: null`, `firmId: null` OK, firms route unchanged. Wants the master SHA AFTER the dev deploy that carries 095c-2..4.
- customer-provisioning (09): the both-keys notice EXPIRED undelivered (permission mode). Resend only when the owner asks.
- ontology-r1 (a9): reviews master PRs.

## Ready queue (after the running agents)
095c-3 → 095c-4 → 095d (K1: set sprk_trackingscope on assign); 092 (after 095c-2); 089b → 089c (after 086); 112 deploy; 161 live (owner OK); file the ISS-005 and ContactCreateOwnerPolicyGuard timing ArchTests as flaky under load. Blocked: 121, 096.
