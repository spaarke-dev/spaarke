# Current Task State

> **Last Updated**: 2026-10-29 (context-handoff). Owner's request: commit/push → update from master → /conflict-check → /merge-to-master for work/ebilling-r1 (PR #1911). Steps 1–3 done. Step 4 (merge-to-master) is waiting on CI, the ontology-r1 review and the uac Q2 fix.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Work branch** | origin/work/ebilling-r1 = 66c49d5247 (PR #1911, draft, MERGEABLE). It carries master through d511a3297e (194b included), 094 @ 600079fd11 (uac READY), 098, the 194b writer fix 1028e5ccdd, the publish-lint and Pillow fix ba63082008, and the EB-D-170–172 log. Master is now 1e409b2ad2. The trial merge (merge-tree) is clean; master is not merged in yet. |
| **Do not push to work** | Until ontology-r1 posts its review of ba63082008 (it asked for no pushes; it knows about 66c49d5247, which is docs only). If CI needs a fix, send ontology the new SHA first. |
| **Running** | (1) Monitor on the #1911 checks. (2) Agent **ebill-storedfile-owner** (opus, worktree; branch task/ebill-storedfile-owner, pushes only that branch): the uac Q2 fix, StoredFileService.StageCreateFlipAsync ContentOf → ForRow(sprk_storedfile), plus the Q1 parity test, the no-core fallback pin and the hard-deny ArchTest. (3) Agent **ebill-103** (implementer, worktree agent-aad68f32362f173e0): the 103 fix round (EB-D-171 caller contact, EB-D-172 close Signals, F2–F4, K1–K4). |
| **Next action** | When the storedfile fix is green: send uac the diff and wait for its re-check. After the ontology review: merge the fix branch plus master (1e409b2ad2+) into work, run the gate, push, and tell ontology the SHA. Then, with CI green and zero pending, put the full integration counts in the PR body, `gh pr ready 1911`, `gh pr merge 1911 --auto --merge`, and do the A.6 sync. |

## PR #1911 merge checklist
- [x] scoped-publish-lint: comments reworded; the StoredFile schema test drops the duplicate negative checks (ba63082008).
- [x] Trivy: all alerts were the Pillow 12.3.0 pin in the eval-set generator; now 13.0.0 (ba63082008).
- [ ] CI green, zero pending, CLEAN.
- [ ] ontology-r1 review (asked 2026-10-29; running).
- [ ] uac: Q1 accepted with the parity test; Q2 fix re-checked.
- [ ] PR body: full integration counts (ab1ca98f45: 199/0/8 and 353/0/23; update after the final gate).

## Task branches (not on work)

| Task | Branch @ head | State |
|---|---|---|
| 101 | task/ebill-101-corrections (local, worktree agent-ae6eabaa0ec9d718e) | Waiting. Resume: rebase on work, persister prep (EB-D-170 provenance + Q2 Clean status for unflagged lines + an end-to-end persister → approve test), then build 101. |
| 102 | task/ebill-102-adjustments @ 5ae2385531 | Done. Merge after 103; route ledger count reconciles to 145; check ForParents against 194b. |
| 103 | task/ebill-103-approval | Fix round running (agent). Then file a follow-up issue for the "Approved with no Decision Record" sweep. |
| 086 / 092 / 089 | existing branches | Merge work, then the content-team writers and the 086 fix round (uac F3-1, K1–K5; adversarial K2–K6). |

## Peers
- uac-r2: Q1 accepted (parity test required); Q2 is a defect, take (b) before #1911 goes ready, then it re-checks the diff. #2427 OutputLookups sha to come. 194b dev-deploy notice to come.
- ontology-r1: reviewing #1911 at ba63082008; will post a PR comment.
- R3: removing `sprk_invoicestatus` from the master external allow-list in its task-002 PR. When it lands, drop InvoiceStatusRetirementGuardTests' TransitionalExemptions (and the two marked lines) in the same merge into work. R3 sends the sha.
- word-add-in-r1: told that 094 is on work (2026-10-29). Notify again when dev is live.
- Filed: #2426 (flaky ProvisionProject timeout test).

## Open items
- Pre-existing defect to fix or file: InvoiceLineDecisionService.CorrectionRow `CoreOf(invoice)!.Value` throws before the no-core refusal.
- Mark 101/102/103 🔄 in TASK-INDEX.
- Owner question still open: whether the R2 re-check button comes into R1.

## Standing rules (see project CLAUDE.md)
No bare git stash, no --no-verify. Live writes and role changes need the owner's OK; schema runs need notices. StoredFileReconcileJob is not enabled anywhere before F4 merges and DEF-044 is fixed. After merges: build the 5 test projects, then run the broad filter and ArchTests before pushing. Remove worktrees only with Remove-WorktreeSafe.ps1. Max 4 concurrent subagents. Scratchpad queue: scratchpad/queue.md.
