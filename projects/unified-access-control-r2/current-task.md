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
| **Next action** | (1) The owner report and click-test list for 195 have been sent; act on the owner's answer. (2) BU1 Q2–Q12 recommendations (below). Worktrees C:/wt195, C:/wt2486, C:/wtuacq1, C:/wtuacwo, C:/wtuacrf, C:/wt2471f and C:/wt194 were removed 2026-11-01 (all PRs merged at their local head); keep C:/wt2468 until the next deploy. |

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
