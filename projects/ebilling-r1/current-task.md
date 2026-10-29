# Current Task State

> **Last Updated**: 2026-10-28 (checkpoint: 095 merged; 094 seam built; 098 round 4). The 095 path is urgent (EB-D-158); word-add-in-r1 waits on 095 working end to end on dev.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Branch** | work/ebilling-r1 (draft PR #1911) @ 095 merged (8733963565; gate broad 10,721/0, Arch 1,564/0; full integration counts to re-run on the final tip for the master PR body). |
| **Running agents** | None. 094 round 5 complete at 97c793670b (task/ebill-094-submit-invoice; includes 098 797398d548; gate green except #2426 flaky provisioning test). Master→work merge prepared on review branch `task/ebill-master-sync` @ 4beed693da (worktree agent-ae9910636512ed719, local branch ebill-master-sync; NOT on work yet). |
| **Waiting on uac-r2** | (1) Re-check of 094 diff c7253943f5..97c793670b. (2) Ruling on the RecordFamilyEdges proposal (24 edges: 22 rows, 2 baselines; 3 "UAC-r2: decide" items) → then push the master merge to work/ebilling-r1 (fast-forward from 36057eb78e), add the "UAC-r2 review:" line to #1911, watch Router + Client lint. (3) R4b-2c sha (EB-D-168/168a) → 086 fix round, 092 merge, content-team writers. |
| **Next actions** | (1) 4b done → send 098 head to uac-r2; then 098 merges to work (after uac accept) and 094 merges the 098 round-4 head and unskips `FromEmailInvoice_CommittedByThePersister_IsVisibleToTheFirm`. (2) 094 pass-2 findings → 094 agent (a9d1b5c4b2a19392a, worktree agent-a9d1b5c4b2a19392a). (3) R4b sha → merge 194b into 086/092/094/089 branches; 086 fix round (uac F3-1 + K1-K5, adversarial K2-K6, neutral names in 086 notes, deploy-order line; scratchpad fix-briefs-uac-2026-10-28.md); content-team writers with ordinary→content team / Secure→Secure team tests. (4) Then merge order to work: 098 → 092 → 086 → 094 (094 contains 092, 095, 098). (5) Tell word-add-in-r1 when the seam is on work/dev (route stops answering 503). (6) After 086+092: launch 123. (7) `.claude/constraints/provisioning.md:269` (retired Finance__EbillingOutputContainerId). (8) 089 waits for uac 194b (sprk_policy primary parent, BRR immutability test, NFR-05 must-fire evidence, POML Q10, 2 passes; also needs EB-D-168 writers). |

## Branch status
| Task | Branch @ SHA | State |
|---|---|---|
| 095 | task/ebill-095-from-email @ 85464826b6 | ACCEPTED by uac; merged into work. TASK-INDEX 🔄 until 094's seam is on work. |
| 094 | task/ebill-094-submit-invoice @ c7253943f5 | Seam built, EB-D-167 built, uac conditions done; contains 092, 095, 098@f2446e638e. Pass 2 + uac re-check running. Waits for 098 r4 head + R4b. |
| 098 | task/ebill-098-external-marker @ 552d1ee96d (4a) | uac acceptance withdrawn pending round 4; 4b running. |
| 092 | task/ebill-092-submission-files @ 3add11454c | Merge held on R4b (writes sprk_storedfile). |
| 086 | task/ebill-086-storedfile-secure-child @ e88825a528 | Live on dev; blocked on R4b (EB-D-168), then fix round + pass 2. |
| 089 | task/ebill-089-assignments @ 8f4920ce0c | Live on dev; waits for 194b/R4b. |

## Owner decisions 2026-10-27
**168** hard-deny tables' ordinary owner = memberless content owner team (owner via uac-r2). **167** re-attempt after Failed on internal routes. **166** a document an invoice was submitted from cannot be deleted. **165** OP-5 allow another user's re-attempt after Failed (095 fix pending). **164** malware scanning required on production stamps, may be off on dev/test (sent to customer-provisioning for #2232). EB-D-153..161 (see notes/decisions-log.md); **162** Billing Admin Read Global → Deep (DONE: merged e03283c381, EB-D-150 exception removed per uac-r2, live on dev, -Verify PASS); **163** retire Procedure Library SPE container → new task 124 (after 116, 092); customer-provisioning told (they built nothing; #2232 is their only eBilling item, waits for #1911 on master; they have the Finance__FileStore__* settings).

## Open owner questions
- Explicit re-check button (R2) into R1?
- Pending uac-r2 ruling → owner: ordinary owner of hard-deny tables (Stored File, Procedure Assignment); uac lean (a) app owner as a declared resolver rule. uac also answering: flagged-external direct Read on sprk_invoice, read_query document-side cap (188), SemanticSearch InvoiceName / IRetrievalAccessTrim (098 K2).

## Peer state
- **uac-r2:** reviews owed: 086, 089 (on 194b head), 094, 095, 098, 117, 123, 091 items. Will send 194b head.
- **ontology-r1:** agreed 089 items 1-3 (BRR immutable — needs a test), no objection to EB-D-162.
- **word-add-in-r1:** file_scan_timeout in contract and their PR; neutral 409 sentence in #2365 (no reason field); EB-D-165 needs nothing from them; tell them `attachment_not_preserved` + firm 403 when 095 fix lands; always sends X-Idempotency-Key; #2359 merged (inert until manifest redeploy); release waits for end-to-end on dev.
- **R3:** dev BFF now e56a3a51d4 (no ebilling code; from-email not served on dev until ebilling reaches master).

## Standing rules (see project CLAUDE.md)
No bare git stash, no --no-verify. Live writes and role changes need the owner's OK; schema runs need notices (uac-r2, R3, ontology as relevant). StoredFileReconcileJob is not enabled anywhere before F4 merges and DEF-044 is fixed. After merges: build the 5 test projects, broad filter + ArchTests before pushing. Remove worktrees only with Remove-WorktreeSafe.ps1. Max 4 concurrent subagents.
