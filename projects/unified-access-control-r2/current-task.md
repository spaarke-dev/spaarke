# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md` (latest 2026-10-31: task 195 owner decisions).
> - **Defer list:** `notes/defer-issues.md`. **Previous copy:** `notes/handoff-history/current-task-2026-10-22-cp60.md`.

> **Last Updated**: 2026-11-01 ~03:50Z (checkpoint #85: task 195 closed. Merged, deployed to dev, gate PASS; work branch merged from master).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge only when Router + "Client lint (type-aware ratchet)" are green; `--squash --match-head-commit <sha>`, no `--delete-branch`, after `git merge-tree`. Live actions, deploys and settings changes need the owner's go; shared-dev deploys go through the R3 protocol. |
| **Active task** | None in progress. Task 195 is ✅ (d0c1c5dc3c, #2496; live result in `notes/task-195-project-filing-root.md` §5). |
| **Dev state** | BFF = d0c1c5dc3c since 2026-11-01 03:09Z. Web resources and the `SpaarkeProjectMatterLinkRibbons` ribbon are live. Dev SPA = b083d62183. Deploy checkout C:/wt2468, detached at d0c1c5dc3c. |
| **BU1 move (owner go 2026-11-01, all recommendations; `notes/decisions.md`)** | Step 0 done, read-only. Files are in `$TEMP/uacmerge/uacg/rbu1/rbu1s0_*` (snapshot = rollback source; dry runs). Plan §7, 7a (ebilling checks) and 7b (R3 conditions) are in `rbu1_plan.md`. **Playbooks stay with 1d02f31c until #2505** (owner). The product hand-over is **263 rows** to root team 09fbf21c (`rbu1s1_product_apply.ps1`; check = ready 263). **Run `-Apply` only after R3's "done"** on the CMRCL-479772 re-check. Then: Step 1 probe e44317a0 → Step 2 humans → Step 3 1d02f31c (DoNotMoveAllRecords, pre-assign BU1 System Administrator if the probe proves it) → Step 4 customer Assigns (687 explicit: A 30, B 5, C 257, D 327 + 68; check the 32 `sprk_externalrecordaccess` rows' owner use first) → Step 5 BFF app users (window to R3 and ebilling; read back roles and FSPs incl. Identity Link Writers) → Step 6 verify (+ebilling and R3 checks). Each write step: show the list, get the owner's go, then tell R3 the external effect. |
| **#2506 (in progress)** | A D134 `DecisionCommitServiceTests` case (`todo-pair-only`, a to-do whose pair names a document) fails on master: Unavailable "The item's owner could not be checked", from `DecisionCommitService.cs:1083-1088` (the resolver threw OwnerResolutionFailed or FilingFailed). Suspects are our fault-propagation changes #2471 9f4afe82ac and #2481 9f4e3820b2, also #2475 and #2477. Worktree C:/wt2506 is detached at 0e3962069b. The repro runs in the background (build + `--filter DecisionCommitServiceTests.D134_NoSingleEnabledAssignee`). Next: bisect, fix in scope (it's ours if a uac change caused it), and tell ebilling and ontology. |
| **Gates and peers** | fcf01480f4 gate PASS (sent to R3). Waiting for R3's "done" on the CMRCL-479772 re-check before any BU1 write. ebilling 151c: I advised (c), a plain `sprk_billingexport` reference plus a server-set owner and no file copies; I review the 151c-1a diff before it merges. Field-mapping #2504 reviewed OK, auto-merge on. |
| **Next action** | (1) #2506. (2) BU1 move, above: product `-Apply` after R3's "done", then Step 1 probe, each with the owner's go. (3) The 195 owner click-test is on the checklist. Worktrees C:/wt195, C:/wt2486, C:/wtuacq1, C:/wtuacwo, C:/wtuacrf, C:/wt2471f and C:/wt194 were removed 2026-11-01 (all PRs merged at their local head); keep C:/wt2468 until the next deploy. |

## Task 195: done
- **After-notices:** sent to R3 (gate, external 0), ebilling (the transient SQL 207 in its column-delete window; acknowledged and recorded in its 015 notes) and field-mapping.
- **Field-mapping:** recorded 195 live in its decisions.md. 040 still waits on its 005 (`-Apply` on hold with the owner) and 019. It will send 040's and 039's diffs to me before merge.
- **Owner browser click-test** stays on the checklist (§5 of the note) and does not hold the task open (round 90).

## Next (mine, in order)
1. BU1 Q2–Q12 recommendations for the owner (`$TEMP/uacmerge/uacg/rbu1/rbu1_plan.md` §6), including K4.
2. Review field-mapping's 039 diff (FR-38 widening: WA, event and invoice search-indexed; both index columns server-owned), its #2483 follow-up and later 040, when they arrive.
3. Parked (verify state):
   - task 193 (C:/wt193), task 197 (C:/wt197);
   - #2427 output lookups (C:/wt2427; ebilling waits for its sha);
   - 196a/196b.

## Owner items outstanding
- The 195 click-test (checklist).
- The owner-private items in `scratchpad/q1_owner_private.md`.
- Whether chat should send invites, as its own change, through field-mapping.
- BU1 Q2–Q12.
- Older:
  - decisions #2343, #2245, #2250, #2260, K5/#2270 (via R3), #2262, #2396;
  - task 191 F1/S4;
  - task 190 Apply;
  - #2382 UAT check 5.

## Filed this stretch
- #2493: Document Upload wizard parent name.
- #2494: launch-data double decode.
- #2495: no in-product retry after a failed filing.
- #2478: a filing is left behind after a refused Office save.
- #2487: a stale membership junction owner row until the nightly cleanup.

## Peers
R3 `spaarke-wt-spa-external-access-platform-ce`; ebilling `spaarke-wt-ebilling-r1-0a`; field-mapping `spaarke-wt-field-mapping-server-write-pa-11`; ontology `spaarke-wt-spaarke-ontology-platform-r1-be`; word-add-in; provisioning.
