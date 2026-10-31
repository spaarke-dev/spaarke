# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md` (latest 2026-10-31: task 195 owner decisions).
> - **Defer list:** `notes/defer-issues.md`. **Previous copy:** `notes/handoff-history/current-task-2026-10-22-cp60.md`.

> **Last Updated**: 2026-10-31 ~20:05Z (checkpoint #82: b083d62183 deployed to dev; task 195 implementation running).

## Deployed and gated
- **Dev BFF = b083d62183** (master, including #2486, field-mapping 017).
  - I deployed it at 19:52:25Z on 2026-10-31, with the owner's go in this session and R3's OK for that SHA. Router passed.
  - Schema Verify passed; the package was 39.07 MB; the 4 critical-file hashes matched; healthz passed.
  - App settings unchanged: ebilling's keys, and both UAC switches (RootOwnerWritesEnabled, OrdinaryMovesWritesEnabled) still true.
  - R3 and ebilling were told the deploy is done.
- **Gate running:** background job bvwjb76pm writes `$TEMP/uacmerge/uacg/gate_b083.txt` (window 19:52:25Z → about 20:12Z).
  - When it lands, check 5xx, [OWNERSHIP-REFUSED], and external routes separately (R3 asked: `OwnedChildWrite`/`ChildRecordEndpoints` sit on the external write path).
  - Known pre-existing warnings: SECURE-CENSUS, acme ID-LINK #2163, AI catalog drift #1987.
  - Then send the after-notices. Field-mapping gets the deploy time and the gate status, and runs the `SYS-Dataverse Create Record` re-seed in its own session with the owner's OK there. R3 and ebilling get the gate result.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge only when Router + "Client lint (type-aware ratchet)" are green; `--squash --match-head-commit <sha>`, no `--delete-branch`, after `git merge-tree`. Live actions, deploys and settings changes need the owner's go; shared-dev deploys go through the R3 protocol. |
| **Task** | **195** (#2352): the project's regarding pair is its filing root. FULL rigor, Opus/high. Worktree C:/wt195, branch `fix/uac-r2-195-project-filing-root`. The design note `notes/task-195-project-filing-root.md` is updated with the owner's decisions (820aea7bfa, pushed). |
| **Running** | Two Opus implementers in C:/wt195, on disjoint files. **A: server.** One core helper; `PATCH /api/v1/child-records/sprk_project/{id}`; the chat update tool on the same core; the container-ambiguity facts; census counts; .NET tests and gates. **B: client.** The shared file-under-matter function; both wizards; ribbon on the `sprk_Project_Matter_nn` subgrids ("New" opens the wizard, "Add Existing" hidden); `scripts/Set-ProjectFilingFromMatterLink.ps1`; jest and lint. |
| **Next action** | When both report, check that the request body B sends matches the one A accepts, and decide A's container facts. Then one Opus adversarial review (twice: auth/tenant-isolation), fix, open the PR and merge. Deploy the BFF and the solution/web resources with the owner's go. Run the backfill report, then `-Apply` (accepted). Send field-mapping the merge SHA (its task 040 waits on 195). |

## Owner decisions for 195 (2026-10-31)
1. Re-filing to another matter is allowed. Nothing is ever released from isolation.
2. Adding a matter to a project's matter list goes only through the wizard. The model-driven app's out-of-the-box subgrid New/quick-create and Add Existing are replaced.
3. The backfill is accepted with all 3 unit moves, applied after deploy, report first.
   - Project 6 e42aa40d: BU1 → root.
   - PRJ-000034 a8dbfb72: root → BU1.
   - PRJ-000038 479c5a63: root → BU1.

## Next (mine, in order)
1. Gate for b083 → after-notices (above).
2. Task 195 (above).
3. BU1 Q2–Q12 recommendations for the owner (`$TEMP/uacmerge/uacg/rbu1/rbu1_plan.md` §6), including K4.
4. Review field-mapping's #2483 follow-up and its 039 diff when they arrive.
5. Clean up these worktrees with `Remove-WorktreeSafe.ps1`, all merged: C:/wt2468 (deploy checkout, at b083d62183), C:/wtuacq1, C:/wtuacwo, C:/wtuacrf, C:/wt2471f, C:/wt2486, C:/wt194.
6. Parked (verify state):
   - task 193 (C:/wt193), task 197 (C:/wt197);
   - #2427 output lookups (C:/wt2427; ebilling waits for its sha);
   - 196a/196b.

## Owner items outstanding
- The owner-private items in `scratchpad/q1_owner_private.md`.
- Whether chat should send invites, as its own change, through field-mapping.
- BU1 Q2–Q12.
- Older:
  - decisions #2343, #2245, #2250, #2260, K5/#2270 (via R3), #2262, #2396;
  - task 191 F1/S4;
  - task 190 Apply;
  - #2382 UAT check 5.

## Filed this stretch
- #2478: a filing is left behind after a refused Office save. Not an access issue.
- #2487: the membership junction owner row stays stale until the nightly cleanup after a reconciler re-own.

## Peers
R3 `spaarke-wt-spa-external-access-platform-ce`; ebilling `spaarke-wt-ebilling-r1-0a`; field-mapping `spaarke-wt-field-mapping-server-write-pa-11`; ontology `spaarke-wt-spaarke-ontology-platform-r1-be`; word-add-in; provisioning.
