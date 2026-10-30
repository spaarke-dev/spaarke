# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md` (latest 2026-10-29: WA owner paths follow the matter; "a family shares one ownership; records are owned by BUs/teams").
> - **Defer list:** `notes/defer-issues.md`. **Previous copy:** `notes/handoff-history/current-task-2026-10-22-cp60.md`.

> **Last Updated**: 2026-10-30 (checkpoint #78: fix 3 merged as #2477).

## Deployed and gated
- **Dev BFF = 9f4afe82ac** (R3 deployed it 14:40Z on 2026-10-30, with the owner's go). It contains #2471, the #2468 fix.
- **App Insights gate PASS** (14:40–15:12Z, against a 7-day baseline). R3 was told, and ebilling can go. Pre-existing and unchanged: SECURE-CENSUS root-BU reach (until the BU1 move), and the acme FLS-proof/ID-LINK failures (#2163).
- **Merged to master, NOT deployed:** #2474 (Q1 roles + playbook admin gate; roles already applied on dev), #2475 (WA owner paths, bf8c775ce2) and #2477 (fix 3, c30686bdfc). The next deploy needs the owner's go. #2475 moves dev rows: 2 events → BU1 (owner GO given). #2477 changes no data until both switches are on.
- **#2471 follow-up PR** (K, non-blocking):
  - EmailFiling on the early OFFICE_022 refusal (`OfficeService.cs` ~682);
  - the refusal message chosen by RefusalCode;
  - `IsCommunicationFiledAsync`: no InvalidOperationException catch, and `ReadThreadAsync` returns null when the row is missing;
  - cache the filed answer per communication id.

  Also file the OFFICE_022 leftover filing (not an access issue).

## Fix 3: MERGED #2477 (c30686bdfc)
- Two Opus reviews, one fix round, then a Sonnet re-check: ready, no F.
- Root-owner writes need BOTH `SecureRoot__Inheritance__RootOwnerWritesEnabled` AND `SecureChild__Reconciliation__OrdinaryMovesWritesEnabled`. The expected document container follows the owning unit, and only the ordinary-moves pass relocates files. Per run: 25 re-owns and 200 resolves, with an in-memory cursor.
- Dev plan (read-only): 28 roots.
  - 6 projects stay in their own unit's team.
  - 10 WAs move to the BU1 team (crossUnit).
  - 12 go to the root team.
  - It converges in 2 runs.
- **After deploy (owner's go):** turning both switches on is an app-settings change, so it is an owner decision; the data move itself is approved. Then run an App Insights gate on [ORDINARY-REOWN] and [SECURE-CHILD-RECONCILE].
- Known limits, in the PR: the cursor is in memory; an unfiled project's children keep the project's unit until the project moves; slow cycles at scale.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge only when Router + "Client lint (type-aware ratchet)" are green; `--squash --match-head-commit <COMMIT sha>`, no `--delete-branch`, after `git merge-tree`. Deploy via R3 (owner's go given in R3's session). Task closes on merge + deploy + server-side gate. |
| **Fix bar** | Most technically correct fix. Every defect fixed or filed. PUBLIC repo: no exploit mechanism in issues/PRs/commits (neutral wording; owner decides tracking of access defects). |
| **Agents** | Opus executors/verifiers; Sonnet high for scoped re-checks. Model always stated. Never `taskkill /IM`. No `jq`: use `python -I`. Session cwd keep uppercase `C:/code_files/...`. |
| **Next action** | (1) Get the owner's go to deploy master ≥ c30686bdfc (#2474, #2475, #2477) via R3, then gate it; (2) the #2471 follow-up PR; (3) task 195 via task-execute; (4) BU1 Q2–Q12 recommendations for the owner. |

## In flight
- Nothing running.
- Parked worktrees to clean up with `Remove-WorktreeSafe.ps1`: C:/wt2468, C:/wtuacq1, C:/wtuacwo and C:/wtuacrf (all merged), and C:/wt194.

## Next (mine, in order)
1. Deploy #2474/#2475/#2477 (owner's go) → gate → both root switches on dev (owner) → gate.
2. #2471 follow-up PR (see above).
3. **Task 195** (#2352): the Create Project wizard writes the project pair, plus a dev backfill. Field-mapping task 040 waits on it. Run via task-execute.
4. BU1 move: bring the owner recommended answers for Q2–Q12 (`$TEMP/uacmerge/uacg/rbu1/rbu1_plan.md` §6), including K4 (reassign product rows first).
5. Parked (verify state):
   - task 193 (C:/wt193), task 197 (C:/wt197);
   - #2427 output lookups (C:/wt2427; ebilling waits for its sha);
   - 196a/196b;
   - 194b close (remove C:/wt194 via Remove-WorktreeSafe.ps1).

## Owner items outstanding
- The owner-private items in `scratchpad/q1_owner_private.md` (how to track them; whether to scrub the checkpoint #75 wording from history).
- Whether to apply the credential-column secure to dev's schema now or wait for the next import.
- BU1 Q2–Q12.
- Older:
  - decisions #2343, #2245, #2250, #2260, K5/#2270 (via R3), #2262, #2396;
  - task 191 F1/S4;
  - task 190 Apply;
  - #2382 UAT check 5.

## Done 2026-10-29/30
- Merged: #2471 (9f4afe82ac, fixes #2468), #2474 (cd3f84662e), #2475 (bf8c775ce2), #2477 (c30686bdfc, fix 3), #2440, #2451.
- 9f4afe82ac deployed to dev; gate PASS.
- Q1 applied on dev, plus the externalserviceconfig grant removed (Verify PASS). Filed #2472 and #2473.
- Peer reviews:
  - ontology 168 PASS (#2463);
  - ebilling #1911 and the Adjustment-line PASS;
  - R3 SPA-only deploy OK.

## Peers
R3 `spaarke-wt-spa-external-access-platform-ce`; ebilling `spaarke-wt-ebilling-r1-0a`; field-mapping `spaarke-wt-field-mapping-server-write-pa-11`; ontology `spaarke-wt-spaarke-ontology-platform-r1-be`; word-add-in; provisioning.
