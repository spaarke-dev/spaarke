# Current Task State

> **Last Updated**: 2026-10-28 (checkpoint after reviews of 086/094/095/098). The 095 path is urgent (EB-D-158); word-add-in-r1 waits on 095 working end to end on dev.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Branch** | work/ebilling-r1 (draft PR #1911). Gate on e03283c381: broad 9,472 / Arch 1,557; full integration 199/8/0 and 353/23/0 (re-run on the final tip for the master PR body). |
| **Running agents (4)** | **095 fix round** (implementer opus, worktree agent-ac6e3dd4a7414fc48; items A-I: base64 attachments + marked-SHA check + `attachment_not_preserved` 422, same-email resubmission 409, EB-D-165, F1-2 text, null attachment 400, IdempotencyFilter double-invoke, writer guard, census waiver, contract text). **094 fix round** (094 agent resumed, worktree agent-a9d1b5c4b2a19392a; uac F1-a PRIVATE wording "step A decides 'unfiled' from the D-140 chain", F1-b, F1-c + EB-D-166, F2-a, F2-b, K6, K7, undo ordering; seam parked until the 095 fix head). **098 fix round** (implementer opus, worktree agent-a35ba7af6c7970aa3; submission sprk_document as source, read_query cap, interface guard, test, comm-only source). **094 adversarial review** at 5a5ffabfa8 (send its findings to the 094 agent as an addendum). |
| **Blocked: 086 + 092 merge** | 086 adversarial F1: the ordinary owner (BU default team) lacks prvReadsprk_StoredFile, so Dataverse refuses ordinary Stored File creates (092 persister) and re-owns on Unsecure; same for 089 sprk_procedureassignment. Asked uac-r2 for the design (lean: app owner when ordinary); then owner decision. Then the 086 fix round: that fix + uac F3-1 + K1-K6 from scratchpad fix-briefs-uac-2026-10-28.md + neutral names in 086 notes + deploy-order line + adversarial K2-K6 (deploy gate "carrying 086", 080 verify covers sprk_storedfile, evidence PENDING guard, census lookup order). |
| **Next actions** | (1) uac-r2 answer on the 086 owner rule → owner question → 086 fix round; merge 092 after. (2) As each fix head lands: send to uac-r2 for re-check; 095 fix head → 094 agent folds 095 + implements the seam; tell word-add-in-r1 the new codes (`attachment_not_preserved`, firm 403). (3) 094 adversarial findings → 094 agent. (4) 089 waits for uac 194b (see 089 row). (5) After 086+092 merge: launch 123. (6) `.claude/constraints/provisioning.md:269` (retired Finance__EbillingOutputContainerId). |

## Branch status
| Task | Branch @ SHA | State |
|---|---|---|
| 092 | task/ebill-092-submission-files @ 3add11454c | Reviewed and fixed; merge held on the 086 owner-rule decision (its persister creates ordinary Stored Files). |
| 089 | task/ebill-089-assignments @ 8f4920ce0c | Live on dev. Waits for uac 194b: merge it, sprk_policy primary parent, BRR immutability test, NFR-05 must-fire evidence, POML Q10 Restrict→RemoveLink; 2 adversarial passes; reuse 086's merge resolution 22b803e1a5. Also affected by the owner-rule blocker. |
| 086 | task/ebill-086-storedfile-secure-child @ e88825a528 | Live on dev (role 40/40, creator-person PASS). Pass 1 done; blocked (above); needs pass 2 after fixes. |
| 098 | task/ebill-098-external-marker @ 2f50cddec9 | Fix round running. |
| 095 | task/ebill-095-from-email @ 96681b8825 | Fix round running; uac accepted with conditions. |
| 094 | task/ebill-094-submit-invoice @ 5a5ffabfa8 | Fix round running; uac NOT ready (5 fix-now). |

## Owner decisions 2026-10-27
**166** a document an invoice was submitted from cannot be deleted. **165** OP-5 allow another user's re-attempt after Failed (095 fix pending). **164** malware scanning required on production stamps, may be off on dev/test (sent to customer-provisioning for #2232). EB-D-153..161 (see notes/decisions-log.md); **162** Billing Admin Read Global → Deep (DONE: merged e03283c381, EB-D-150 exception removed per uac-r2, live on dev, -Verify PASS); **163** retire Procedure Library SPE container → new task 124 (after 116, 092); customer-provisioning told (they built nothing; #2232 is their only eBilling item, waits for #1911 on master; they have the Finance__FileStore__* settings).

## Open owner questions
- Explicit re-check button (R2) into R1?
- Pending uac-r2 ruling → owner: ordinary owner of hard-deny tables (Stored File, Procedure Assignment); uac lean (a) app owner as a declared resolver rule. uac also answering: flagged-external direct Read on sprk_invoice, read_query document-side cap (188), SemanticSearch InvoiceName / IRetrievalAccessTrim (098 K2).

## Peer state
- **uac-r2:** reviews owed: 086, 089 (on 194b head), 094, 095, 098, 117, 123, 091 items. Will send 194b head.
- **ontology-r1:** agreed 089 items 1-3 (BRR immutable — needs a test), no objection to EB-D-162.
- **word-add-in-r1:** file_scan_timeout in contract and their PR; neutral 409 sentence in #2365 (no reason field); EB-D-165 needs nothing from them; tell them `attachment_not_preserved` + firm 403 when 095 fix lands; always sends X-Idempotency-Key; #2359 merged (inert until manifest redeploy); release waits for end-to-end on dev.
- **R3:** #2360 merged (078f62bcad); dev stays on de38296499 until contact security schema applied; no master-built BFF to dev before that.

## Standing rules (see project CLAUDE.md)
No bare git stash, no --no-verify. Live writes and role changes need the owner's OK; schema runs need notices (uac-r2, R3, ontology as relevant). StoredFileReconcileJob is not enabled anywhere before F4 merges and DEF-044 is fixed. After merges: build the 5 test projects, broad filter + ArchTests before pushing. Remove worktrees only with Remove-WorktreeSafe.ps1. Max 4 concurrent subagents.
