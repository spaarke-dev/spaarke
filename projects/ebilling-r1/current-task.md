# Current Task State

> **Last Updated**: 2026-10-31 ~02:30Z (context-handoff). origin/work/ebilling-r1 = 169c55a498 (097a–d, the 097d follow-up and the 097 second-review fixes merged; gate passed).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Work branch** | 169c55a498 pushed. Gate on the review2 branch: ArchTests 1857/1 (ISS-005 flaky, #2469), unit filter 9917/0/7, integration 199/0/8. |
| **Running** | Implementer (sonnet/high) on `task/ebill-resubmissionof-fls`: script `scripts/ebilling/Set-EbillingResubmissionOfFieldSecurity.ps1`, write/read inventory, app-only write guard, -WhatIf on dev only. |
| **Next action** | (1) When the agent reports: review, merge the branch into work, run the gate, push. (2) If no as-caller write exists: send the schema notice to ontology-r1, uac-r2, R3 and word-add-in; run the script live on dev (owner OK given), then `-Verify`. (3) Notify word-add-in of the review2 codes (below). (4) 097e: send its shape to word-add-in before building. (5) 097f, 151a/b/c (EB-D-181). |
| **Master PR (later)** | Must carry 097b AND 097c together (ontology-r1 condition), full integration counts, ontology-r1 review. |

## Owner decisions in force
- EB-D-179 (097 split, both routes, same number), EB-D-180 (superseded submission stays Accepted + note → 097f), EB-D-181 (151: OOB views + ribbons on views and form + modal + download + Email to AP; AP email column on settings, attach CSV+JSON, sent from the user's mailbox; split 151a/b/c).
- Field-secure `sprk_resubmissionof` (2026-10-30): readable, writable only by the server identity. One live schema change on dev, with a peer notice first.

## Owed to word-add-in
New 097b reason `submission_in_progress`. New stored codes `ebilling.submission.correction.submission_in_progress` and `.different_invoice_number`, plus 097c's `.different_firm`, `.different_core_record` and `ebilling.submission.correction_target_unavailable`. The from-document `mode` value is exact-case. Check the exact code strings in `notes/097b-correction-intake.md` before sending.

## Dev state
- spaarke-bff-dev runs master 9f4e3820b2 (uac-r2, 19:22Z); our EB-D-173 settings were verified intact. A /healthz and settings re-check is optional.
- 015 column delete: waits for R3's next dev SPA deploy from master (≥ e0df773395); then -WhatIf, a notice to R3 and uac-r2, then -Delete.
- Temp folders `C:\p097db`, `C:\p097dh`, `C:\p097db_out` and `C:\p097dh_out` can be deleted.

## Open owner items
- 050a §9 items 2–4; 115 (ontology PR review + dev option run).

## Standing rules (see project CLAUDE.md)
No bare git stash, no --no-verify. Live writes and role changes need the owner's OK; schema runs need notices. StoredFileReconcileJob stays off until F4 merges and DEF-044 is fixed. After merges: 5 builds, ArchTests, broad filter before pushing (ISS-005 flaky, #2469). Max 4 concurrent subagents; every agent launch states a model. No bare jq, no grep -P.
