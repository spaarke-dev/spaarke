# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-16 ~03:50Z (main session; 156 deployed, waiting for the owner's 055 browser check). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (D-106 critical path; D-123 secure family;
> D-126 deploys from master; D-127 MVP first; D-129/D-130 team ownership; D-138 owner approvals; D-139; D-140) and §6 gotchas.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-148). Deferred work: one self-contained GitHub issue per item,
> label `ontology-deferred`, plus a DI-nn row in `notes/defer-issues.md` (next free: **DI-108**).
> Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | 055 — waiting for the owner's browser check. 156 done (#2004 → master #2008 `a8c3182ec`). |
| **Status** | Console live from master `a8c3182ec` (03:44:12Z, hash `06d39356…218e1`); "Worklist" system layout row `202bcde0-…` seeded (sortorder 13). Peers notified. |
| **Next Action** | When the owner reports the browser check: verify in Dataverse the DR, budget revision, To Do and Signal = Acted; then mark 055 done (POML + TASK-INDEX) and finish `notes/deploy-log.md`. |

## Lanes in flight (launched 2026-10-16, owner: "continue ... in parallel")

Shared brief: scratchpad `lane-brief.md` (no live writes/deploys; PR into docs branch; don't merge). Each: review → fix round → re-check → merge.

| Task | Worktree / branch | Model | State |
|---|---|---|---|
| 062 Know narrative, retire Critical Today | `C:\wts-062` | sonnet/high | ✅ merged #2018 `33a98774`. To master in next batch; after the dev BFF has it run `Sync-BriefNarrateOutputSchemas.ps1 -IncludeSystemPrompt` (-DryRun first; row ce299eb4-…) |
| 063 first Know-promotion rule | `C:\wts-063` `task/ontology-063` | sonnet/medium | ESCALATED: needs (sprk_matter, sprk_budget) VerifiedJoin; owner asked (recommend inside 063) |
| 045 Decision Record tab [uac] | `C:\wts-045` | sonnet/medium | ✅ merged #2014 `1931b405c`; after Console deploys from master run `scripts/seed-decision-records-workspace-layout.ps1` (-WhatIf first) |
| 104 admin probe + tab [uac] | `C:\wts-104` | opus/high | ✅ merged #2019 `b187253a`; to master in next batch |
| 126 AppendTo depth [uac] | `C:\wts-126` | opus/high | ✅ merged #2012 `c7e0f8106`; to master in next batch (uac-r2 OK) |
| 073 memo source #2 | `C:\wts-073` | sonnet/high | ESCALATED: owner decisions (trigger, sprk_memo schema add, separate policy, coverage); consumer diff can be empty |
| 049 revoke Signal Write + DR Create [uac] | `C:\wts-049` | sonnet/high | PR #2022 MERGED into docs (2 adversarial passes + uac-r2 OK + re-check). LIVE APPLY PENDING owner: after the 055 check and the Ontology Administrator answer (Q6; `$A5Waiver = $null` if removed). Run from a checkout of the docs branch: `pwsh -File scripts/Remove-ConsoleSignalWriteAndDecisionRecordCreate.ps1 -Apply` then `-Verify` (expect exit 0). Notify uac-r2 before/after. Rollback: security-roles.md §14 |
| 101 admin authoring BFF [auth] | - | - | ✅ merged #2039 `e153d225e` (live 2/2). PENDING OWNER: Append privileges on Ontology Administrator (prvAppendsprk_Policy + prvAppendsprk_PolicyVersion, Deep; change ROOT role 2f2b2137-… only, then read a child-BU copy + a team member's RetrieveUserPrivilegeByPrivilegeName; notify uac-r2 before/after; then add a row to notes/security-roles.md) |
| 102 rule wizard UI | `C:\wts-102` `task/ontology-102` | sonnet/xhigh | ESCALATED (POML trigger): 101 contract lacks template-eligible fields, the action catalog by lane + excludes, dismissal reasons, severity/work-type values. Owner: A = BFF follow-up (extend catalog route + compile-only validate returning templateEligibleFields) [recommended] or B = client mirrors; and is re-authoring the condition on New version OK. Local commit `8993db08c` (Retire dialog jest timeouts) not pushed; npm installed |

111: code + deploys done (dev SemanticSearchControl 1.1.85 = master, 2026-10-16 check); only the owner browser rows 1-5 remain (row 4 external SPA DocumentUploadPage never deployed; it rides the external SPA deploy). Docs->master #2031 (062+049+104) MERGED 9381e6f62 (2026-10-16; after an ArchTest false-positive fix 58e810405). Tell R3 when its session is back so 9381e6f62 goes into the next dev BFF line; then run the 062 prompt sync. Earlier: #2020 (126+045) MERGED 93850f961 (2026-10-16). R3 session not running: tell R3 when it is back so 93850f961 goes into its next BFF line; then seed the decisionRecords layout row. 049 PR #2022 (head cf6e09a39) re-sent to uac-r2 (session `spaarke-wt-unified-access-control-r2-e1`). Next wave as slots free: 049 after the 055 check. Then 064 (after 062+063), 100/101/103 (after 104), 102 (after 101), 114 (after 111).

**Docs->master #2044 MERGED** `e702c3401` (2026-10-16, 100+101+103). Console auto-deployed from it (Deploy SpaarkeAi success 23:28:56Z); peers told (uac-r2, R3; word-add-in notice HELD by its session). R3 asked to include e702c3401 in its next dev BFF line and send the ProductVersion; until then the admin tab's Rules/Classification areas call routes the live BFF may not have (only Ontology Administrators see the tab).

**ebilling 070 (#1911) re-checked 2026-10-16** (7fc6b0593..be28df779): OK from ontology side; Q1 keep the throw. PAIRED CHANGE asked of ebilling in their 070 PR (ontology F3): 101 ValidateRule passes RuleDomain.Ontology + RuleTypeWrongDomain -> "ruleType"; 100/101 domain filters from RuleDomains constants; test billing type -> 400 on ruleType. If they decline: do it as our follow-up right after 070 merges (fail-closed window). Owner D-92 (ebilling log f4a96f10e) records schema sign-off - ask owner to confirm. ebilling 026 (touches Signal + DR writers, sprk_regardinginvoiceline) comes to us for full review. Temp worktree C:\wts-eb070 (detached be28df779) - remove with scripts/worktree/Remove-WorktreeSafe.ps1.

**ebilling 026 reviewed 2026-10-16** (ef3efce73..b5f4a4c78; Opus, writers + secure family; uac-r2 reviews resolvers): NOT READY. F1 writer lacks prvAppendTosprk_BillingEvent (Spaarke Ontology Service) - role change needs OWNER approval; told ebilling not to apply until I confirm. K1-K6 sent (K2 sprk_firmreason not write-protected; K4 import before BFF). DI-107 #2046 filed (writer AppendTo on core tables only via root team Basic User). Temp worktree C:\wts-eb026 (detached).

**Endpoint census at docs->master:** uac-r2 #2035 (099a) sets master ExpectedEndpointFileCount 127 (104 = 125->126, 099a = 126->127). Each ontology admin file (100 merged, 101, 103) adds one: expect a conflict and recompute from the census when the next docs->master PR merges after #2035.

## Dev state (spaarkedev1 / spaarke-bff-dev)

- BFF `1.0.0+eb5ba4011844…` (R3's line; read the version via Kudu VFS, project CLAUDE.md §6).
- Console from master `a8c3182ec`; Worklist reachable via the workspace dropdown.
- Grid config row `…5010` in sync. Five policies on; 87 Signals. 144/148 writes on and converged; uac-r2 post-enable check CLEAN.

## 055 owner browser check

Open https://spaarkedev1.crm.dynamics.com/WebResources/sprk_spaarkeai → workspace dropdown → Worklist. Check: both lanes render; one full review (Send budget inquiry with recipient = self, Revise budget, Next step Add To Do); network log; no console errors; Daily Briefing still fine.

## Next, in order (after 055)

1. 049 (Signal Write + DR Create removals from users and Ontology Administrator, D-147; PR to uac-r2 first).
2. Re-run `SignalReadRouteLiveTests` as the non-Console user (DI-83 #1882), close #1882.
3. 062, 063, 064, 105, 045, 104.
4. ebilling: their ConsoleKit PR plus 070, 013, 015, 026, 050, 074 come to us for review.

## Promises to peers

- **R3** owns the dev BFF line: message first for any BFF change; `git merge-base --is-ancestor <live> <deploy>` before any deploy. R3 asked how to read ProductVersion (asked them back).
- **word-add-in** (owner UAT on the Console): before/after notices with start/finish times for any Console or BFF change.
- **uac-r2:** notify before/after deploys; PR for 049 before the live role removal.

## Open owner questions

- None.

## Peers (current session names)

word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-45`; R3 `spaarke-wt-spa-external-access-platform-49`; uac-r2 `spaarke-wt-unified-access-control-r2-19`; ebilling `spaarke-wt-ebilling-r1-de`; provisioning `spaarke-wt-customer-provisioning-orchest-db`. Quality alert #1906 (client-tests) is not ours.
