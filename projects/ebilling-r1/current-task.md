# Current Task State

> **Last Updated**: 2026-11-01 ~03:20Z (context-handoff). origin/work/ebilling-r1 = 8b49cb2432 (097a–e, review2 fixes, sprk_resubmissionof field security, 101 refusal-scope fix, 049 seed owner). Gate on it: ArchTests 1869/0, unit filter 9646/0/7, integration 199/0/8.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Running** | Implementer (sonnet/high) on `task/ebill-191-reader-preflight`: 422 `ebilling.submission.reader_not_configured` on all three intake routes before any write; `ai.invoice.*` codes in 095 contract §9. |
| **Next action** | (1) 015 delete: -WhatIf READY at BFF d0c1c5dc3c / SPA b083d62183; notice sent to R3 + uac-r2; run when R3 says no objection: `pwsh projects/ebilling-r1/scripts/Remove-EbillingInvoiceStatus.ps1 -Delete -OwnerApproval 'D-97 2026-10-17 owner' -BffDeployAttestation d0c1c5dc3c -SpaDeployAttestation b083d62183`, then log it in notes/015 §12. (2) When 191 reports: review, merge, gate, push, send word-add-in the commit. (3) Task 192 `filedUnder` (owner OK) after 191. (4) 097f, 151a/b/c (EB-D-181). |
| **Master PR (later)** | Must carry 097b AND 097c together (ontology-r1 condition), full integration counts, ontology-r1 review. |

## Done on dev 2026-11-01 (owner OK each)
- `ContentUnderstanding__Endpoint` set 02:54:30Z (the invoice reader). Our dev BFF keys are now six: Ebilling__Enabled, Ebilling__StoredFileReconcileEnabled=false, three Finance__FileStore__*, ContentUnderstanding__Endpoint. The catalog check is Unhealthy only from the existing AI catalog drift (#1987).
- 049: option 100000014 SubmissionIntegrity inserted; the BILL-SYSTEM-SUBMISSION policy + v1 seeded, owned by the BU1 restricted-content owner team (D-197); ontology-r1 confirmed.
- `sprk_resubmissionof` field-secured ~03:05Z, -Verify PASS (Writers 4/4/4, Readers 4/0/0).

## Peer state
- word-add-in: retest of INV-012 (resubmit gives INV-012.2) is up to the owner; owed: the 191 commit. 097e shape incl. `replacedBy` agreed.
- ontology-r1: OK on 097a–d, e205c93419, b1054ada6a + the fix 50c9c63ccc/67f07998cc (residual: an uncovered invoice subject flags every line, a known limit in the 101 notes); 115 done.
- field-mapping 039 (sprk_invoice SearchIndexed): no impact on us (we write neither column; updates only drop client values).

## Open items
- Older notes name the owner in plain text (003, 004, 011, 029, 091, and docs/data-model/sprk_todo-date-columns.md, which isn't ours). This is against the neutral-wording rule. Clean up in a docs pass.
- 050a §9 items 2–4 (owner).
- Temp folders `C:\p097db`, `C:\p097dh`, `C:\p097db_out` and `C:\p097dh_out` can be deleted.

## Standing rules (see project CLAUDE.md)
No bare git stash, no --no-verify. Live writes and role changes need the owner's OK; schema runs need notices. StoredFileReconcileJob stays off until F4 merges and DEF-044 is fixed. After merges: 5 builds, ArchTests, broad filter before pushing (ISS-005 flaky, #2469). Max 4 concurrent subagents; every agent launch states a model. No bare jq, no grep -P.
