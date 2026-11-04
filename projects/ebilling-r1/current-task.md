# Current Task State

> **Last Updated**: 2026-11-04 ~20:30Z. Session ebilling-r1-c7 (authoritative; d4 is the duplicate). The work head is pushed. The dev BFF is still master d763dc120e: **none of this work is on master or the dev BFF yet.**

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | Parallel task-execute agents, at most 4. Merge each finished branch into work. One adversarial review per change set, two for auth or tenant work; a scoped re-check when a fix changes the approach. FILTERED tests only: the machine is loaded, and full runs time out (HTTP contract tests, TaskCanceledException). |
| **Merged into work today** | 089c, 095c-5, 089d R, **082a** (efcc842244, CoreRecordChain), **095d-1a** (782942288d, allocator renumber plus `sprk_previoussequence`, AccessColumns), **092** (ef14949ed3, scan-wait port). TASK-INDEX: 082 🔄, 095d 🔄, 092 ✅. |
| **089e** (`task/ebilling-089e` at 3d6aedfdf4, NOT merged) | Fix round 2 is done. (A)+(B) on 8 rule-set by-id routes; F4 (a) is OUT (uac-r2 withdrew it) and is recorded as a known limit; K6 is closed by uac-r2; the list is filtered by the caller's Read; the read-back proves commit by the version row; -Verify checks all BU copies; route guard from the route table. The Billing Rule Reader gets Read Deep on `sprk_ruleset` + `sprk_procedure`, and Billing Admin gets `prvReadsprk_procedure` Deep (role sources only). SecureBuRoleDepthAssertion passes, but does not grade `sprk_procedure` (the test uses `Reaches` directly); tell uac-r2. **NEXT: adversarial pass 2 (auth) on 4a5f78ece0..3d6aedfdf4**, then send the SHA to uac-r2 for its full review, then merge. Then the DEV APPLY (owner go typed in this session: "Add to role file + apply on dev" and "Approve role + dev apply"). Notify uac-r2 before; `Deploy-EbillingBillingAdminRole.ps1 -Apply` then -Verify; `Set-CustomerOwnerTeams.ps1` Reader role apply then -Verify; read-back showing only those privileges changed; notify uac-r2 after. Add uac-r2's agreement note to the role ArchTest entry ("uac-r2 agreed 2026-11-04"). Queue: scratchpad `089e-fix-queue.md`. |
| **082b** (`task/ebilling-082b`, NOT merged) | ProcedureResolver. Fix round 2 is RUNNING (agent a8596b2a7d3ec04a2). It covers: a discarded Draft (a Retired version counts only with a Published policy version or a card), D-203(8b)(8c) (same-second test, `overriddencreatedon` guard), K2/K3 faults, the §7.1 model text. Then a scoped re-check, then merge. Send the result to ontology-r1 (it wants the §7.1 commit) and uac-r2. |
| **095d-1b** (RUNNING, agent a5ab5964e539e3742) | The set-firm service and route. Includes the `sprk_firminvoicenumber` AccessColumns listing (uac-r2). Next comes 1c: the record, the Signal facts (no close), 503 incomplete, -Verify window, and the LIVE seam test (needs the owner's go: dev writes). 095d-1 never reaches master without 095d-2. |
| **082c / 082d** | Criteria are in notes/082 (sections "082a review and decisions", "082b review and fix round") and scratchpad `082a-fix-queue.md`. Owner: gap on SPA = the partner sees the warning at submit time; record rule only from its own effective-from. uac-r2: snapshot stores ids in an app-only/field-secured part, findings carry only ForReaders(). |
| **Gate** | The earlier full run (460 failures) was load timeouts; 092's isolated re-run of all 189 failing classes passed. Re-run the full gate with a trx when the machine is quiet, before the master PR. |
| **Open owner items** | Dev column `sprk_previoussequence` -Apply (needs go, before any BFF carrying 095d-1). The 092 live steps (091 storage + 079 FileStore settings live; remove `Finance__EbillingOutputContainerId` on dev). 089d live proof. Email to AP (batch 14680609). The word-add-in notice resend. Deploy-EbillingFormsViews. Delete C:\wt95c4m/b/x, C:\p82h/m/b plus C:\tmp\p82*.tar, C:\wt089em/a/b. The stash entries 1e84ffafed and efeaa73b10 (lint-staged backups). Filed: #2563 (closed by uac-r2), #2565 (scan-wait slot). |
| **Master PR checklist** | Gate green; publish size against fresh master; ONE PR (reviewers ontology-r1, uac-r2 whole diff, R3); auto-merge; send the PR# and SHA to uac-r2 with a deploy request (Stored File count, 095c-5 pre-check, #2550; if 095d-1 is in: `Deploy-EbillingSubmissionPreviousSequenceColumn.ps1 -Verify` exits 0; if 089e is in: the dev roles hold the new privileges, and the Billing Rule Reader extension is applied BEFORE or WITH the BFF deploy (else a child-unit admin's rule-set list is empty); if 092 is in: 091 storage + 079 FileStore settings live); SHA to provisioning-b8 and word-add-in. |

## Hard rules (carry over)

- `Set-ChildRecordCreatorPersonSchema.ps1 -Verify` exits 0 before any BFF deploy.
- `Ebilling:StoredFileReconcileEnabled=false` until uac-r2's gate passes.
- Ebilling never deploys the dev BFF; uac-r2 does.
- No live writes without the owner's go typed in this session, plus notices to uac-r2.
- Public repo: defect plus location only. Access gaps in uac-r2's code go to uac-r2, never to an issue (memory).
- Agent worktrees are removed only with `scripts/worktree/Remove-WorktreeSafe.ps1`.
