# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md` (latest 2026-11-02: BU1 move closed).
> - **Defer list:** `notes/defer-issues.md`. **Previous copy:** `notes/handoff-history/current-task-2026-10-22-cp60.md`.

> **Last Updated**: 2026-11-02 ~15:10Z (checkpoint #87: BU1 move closed; #2506 fixed; ebilling 151c-1a passed the gate).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge only when Router + "Client lint (type-aware ratchet)" are green: `git merge-tree`, then `--squash --match-head-commit <sha>`, no `--delete-branch`. Live actions, deploys and settings changes need the owner's go. Shared-dev deploys go through the R3 protocol. |
| **Active task** | None in progress. |
| **Dev state** | BFF = 3740e1a105 since 2026-11-02 18:52:25Z (R3 deploy; gate passed). Dev SPA = b083d62183. Deploy checkout C:/wt2468, detached at d0c1c5dc3c. |
| **BU1 move** | **Closed 2026-11-02** (record: `notes/decisions.md`).<br>• In BU1: every interactive human except the administrator 1d02f31c, plus both dev BFF app users (roles = BU1 copies of the same set, profiles unchanged by id).<br>• Customer families: 684 rows assigned to the BU1 team; 119 children followed through the reconciler.<br>• Product configuration: 263 rows went to the root team. The 34 playbooks stay with 1d02f31c until #2505.<br>• Step 3 not done, by owner choice: a self-move needs `DoNotRemoveRolesOnChangeBusinessUnit`, so the administrator stays in the root.<br>• Rollback source: `$TEMP/uacmerge/uacg/rbu1/rbu1s0_snapshot_*`. |
| **Next action** | (0) The #2483 addendum verdict was sent to field-mapping (2026-11-02): OK with conditions (a)–(c); Q1 accept the 409 with no exemption; Q2 accept, skip visible; Q3 fix in 042b. #2502 §14 addendum (13c9e89cf9): OK. Routing was never an access control; the boundary is `IRetrievalAccessTrim` at each consumer. Conditions: reword line 123; keep the log at Information until W3, then Warning; close #2206/#2413 on 041's merge after the dev count of non-default stamps; reconcile 042b's step-8 bullet with 041. Waiting for: the 041 diff (scoped check: no trim call moves) and the 042a/042b diffs (042b gets a full review). (1) Review field-mapping's 039/040 diffs and its #2483 follow-up when they arrive. (2) Verify the state of the parked items below. (3) The 195 owner click-test stays on the checklist. |

## Done this stretch
- **#2506:** a test-seed defect (the pair type was seeded as a string; dev has a lookup). Fixed by PR #2509, merged at c0831038d0, with ontology's review. Issue closed; C:/wt2506 removed.
- **ebilling 151c-1a (Email to AP):** three review rounds.
  - Round 1 found F2 (re-ingestion), F3 (a test proving a stub) and F4 (owner = BU default team).
  - Round 2 found F2' (a fault in the account query let the send through) and F4' (the digest dropped attachments with no inline bytes).
  - Round 3 passed. ebilling merged it into work.
  - Owner decisions on their side: Write on the batch is required; both re-ingestion measures; E1 exception EB-D-184.
- **ebilling #2507 K1:** OK for the access gate (the fallback to the caller is kept as a known limit, accepted by ontology).

## Gates and peers
- **R3:** dev BFF = 3740e1a105 since 2026-11-02 18:52:25Z (R3's deploy; rollback fcf01480f4). My 30-min gate PASSED and was reported to R3 (log `$TEMP/uacmerge/uacg/gate3740_30m.txt`). Census follow-up resolved: the drift is project 1cccf07d (Office quick-create, MAT-000032). No BFF path writes the `sprk_Project_Matter_nn` link; it is display-only and confers nothing. Agreed with field-mapping: the server writes the link once the pair is set. Field-mapping owns the create path (`RecordCreationService` step 12; I review their diff). I own the task 195 re-file core (chat update tool). The issue is DRAFTED at `scratchpad/issue_project_matter_link.md`; file it when GitHub is reachable (no DNS on this machine since about 19:00Z), then send field-mapping the number. Also: push the local commits (6718784380 onward).
- **Field-mapping 5c:** the junction exception they reported is #2292 (pre-existing; sighting added). #2522 noted. #2523: any change to how the grants run comes to me before merge.
- **ebilling:** 151c-2 CLEARED 2026-11-02. F1 fixed in 9511a1e17c (`NO_NEW_HOSTS`; both copies identical), K5 409 check in 2abe0f0cbf; re-check OK at tip 1eb91e79de; ebilling merges into work. K1-K4 accepted. No open asks.
- **Field-mapping:** #2504 reviewed OK. 039/040 diffs to come.

## Parked (verify state)
- task 193 (C:/wt193), task 197 (C:/wt197);
- #2427 output lookups (C:/wt2427; ebilling waits for its sha);
- 196a/196b.

## Owner items outstanding
- The 195 click-test (checklist).
- The owner-private items in `scratchpad/q1_owner_private.md`; Q4 private clean-up.
- Whether chat should send invites (as its own change, through field-mapping).
- Older:
  - decisions #2343, #2245, #2250, #2260, K5/#2270 (via R3), #2262, #2396;
  - task 191 F1/S4;
  - task 190 Apply;
  - #2382 UAT check 5.

## Filed this stretch
- #2505: playbook routes authorize by owner == caller, so a team-owned playbook can't be edited, shared or changed.
- #2493, #2494, #2495, #2478, #2487 (earlier).

## Peers
R3 `spaarke-wt-spa-external-access-platform-ce`; ebilling `spaarke-wt-ebilling-r1-0a`; field-mapping `spaarke-wt-field-mapping-server-write-pa-11`; ontology `spaarke-wt-spaarke-ontology-platform-r1-be`; word-add-in; provisioning.
