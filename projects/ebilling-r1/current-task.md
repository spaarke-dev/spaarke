# Current Task State

> **Last Updated**: 2026-10-30 ~20:00Z (context-handoff). Local work/ebilling-r1 = 7e6cd95787 (097c, 097d, 097d-followup merged + decisions), NOT pushed: gate after the 097d-followup merge not run yet. origin/work = 4a79526a4b.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Work branch** | Local 7e6cd95787 = origin 4a79526a4b + 097c merge + 097d merge + 097d-followup merge (212ec2ead1) + EB-D-180/181. Gate passed after the 097d merge (ArchTests 1858/0, unit filter 10056/0/7, integration 199/0/8). |
| **Next action** | (1) Run the gate on 7e6cd95787 (5 builds, ArchTests, unit filter incl. Submission/FromEmail, integration), then push. (2) Launch the 097 second-review fix agent (opus, from the pushed work tip) for F1, F2-code, F4, K4, K5 below. (3) Ask the owner F2-schema (field-secure `sprk_resubmissionof`). (4) Then 097e, 097f, 151a/b/c. |
| **Running** | nothing |
| **Master PR (later)** | Must carry 097b AND 097c together (ontology-r1 condition), full integration counts, ontology-r1 review. |

## 097 second independent review (opus) — fix-now
- **F1**: a correction is accepted while the old invoice's own submission is still running (Processing). Fix: read the old submission's `sprk_processingstatus` (add to `CorrectionSubmissionColumns`), refuse unless terminal (Accepted, or Rejected-Blocked) at intake (`InvoiceSubmissionService.cs:119/:1189`) and at commit (`CheckCorrectionTargetAsync`, `InvoiceSubmissionPersister.cs:1086-1131`). Also closes 097c K4.
- **F2 (code)**: commit re-check must also compare the old submission's `sprk_firminvoicenumber` (D-37) — `sprk_resubmissionof` is user-writable. Correct 097b K6 wording. **F2 (schema, owner)**: field-secure `sprk_resubmissionof` like `sprk_findingsjson` — ask the owner.
- **F4**: the (a′) test (POML :117) is missing: hiding the old invoice's source does not hide the new one, and the reverse, against 098's predicate.
- **K4**: `InvoiceSubmissionAfterSteps.cs:1001` — the submission's own invoice not found should be done, not Retry.
- **K5**: from-document now 400s any other `mode` value; add a line to the 094/095 contract notes.

## Owner decisions this session
EB-D-179 (097 split, both routes, same number), EB-D-180 (superseded submission stays Accepted + note → 097f), EB-D-181 (151: OOB views + ribbons on views and form + modal + download + Email to AP; AP email column on settings, attach CSV+JSON, from the user's mailbox; split 151a/b/c). Totals range script run on dev (owner OK).

## Dev state
- spaarke-bff-dev: uac-r2 deployed master 9f4e3820b2 at 19:22Z and set two of its own keys (restarts ~19:45Z and ~19:53Z); our EB-D-173 settings verified intact by uac-r2. Re-check /healthz + our five settings once.
- 015 column delete: waits for R3's next dev SPA deploy from master (≥ e0df773395); then -WhatIf, notice to R3 + uac-r2, -Delete.
- Temp publish folders `C:\p097db`, `C:\p097dh`, `C:\p097db_out`, `C:\p097dh_out` (097d size check) can be deleted.

## Peer agreements
ontology-r1: 097a OK; 097b OK (K6, ship with 097c); 097d ChangedSinceRead→Retry OK on two conditions (done in 212ec2ead1). word-add-in-r1: 097b contract agreed; Correction off until 097b+c+e on master; wants 097e's shape before build; new 097c stored codes need client mapping. field-mapping 017: our review OK; they protect family tables and refuse submission/approval/correction/external-identifier in chat create.

## Standing rules (see project CLAUDE.md)
No bare git stash, no --no-verify. Live writes and role changes need the owner's OK; schema runs need notices. StoredFileReconcileJob stays off until F4 merges and DEF-044 is fixed. After merges: 5 builds, ArchTests, broad filter before pushing (ISS-005 flaky, #2469). Max 4 concurrent subagents; every agent launch states a model. No bare jq, no grep -P.
