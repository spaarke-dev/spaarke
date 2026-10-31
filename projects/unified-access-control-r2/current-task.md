# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md` (latest 2026-10-29: WA owner paths follow the matter; "a family shares one ownership; records are owned by BUs/teams").
> - **Defer list:** `notes/defer-issues.md`. **Previous copy:** `notes/handoff-history/current-task-2026-10-22-cp60.md`.

> **Last Updated**: 2026-10-30 ~20:20Z (checkpoint #79: 9f4e3820b2 deployed by me with the owner's go; both switches ON on dev).

## Deployed and gated
- **Dev BFF = 9f4e3820b2** (master, including #2474, #2475, #2477 and #2481).
  - I deployed it at 19:22:50Z on 2026-10-30, with the owner's go in this session.
  - R3 gave its OK on the condition Router passed on that SHA, and it did.
  - `Set-ChildRecordCreatorPersonSchema -Verify` exited 0 before the deploy (it needs `-BffApplicationIds 5967251e…,1e40baad…` and `-OntologyWriterApplicationId 69040982…`).
  - Deploy gate PASS (19:22–19:43Z).
- **Dev app settings ON** (owner's go):
  - `SecureRoot__Inheritance__RootOwnerWritesEnabled=true` at 19:45:36Z;
  - `SecureChild__Reconciliation__OrdinaryMovesWritesEnabled=true` at 19:53:19Z.

  Ontology lifted its review gate first. R3 and ebilling were given notice before each change. ebilling's keys are unchanged.
- **Post-change gate PASS** (19:53–20:13Z):
  - All 28 roots re-owned in 2 runs, as planned: 10 WAs and project fb73b08c to the BU1 team cf15f587; the rest to the root team 09fbf21c. 0 person-owned WAs or projects remain.
  - The 9 WA-core Signals moved to BU1. Ontology is re-reading and will send pass or findings.
  - One budget, 2a1b0d23, moved BU1→root to follow its root-unit matter d14d79f4.
  - refused=0 and failed=0 on every run; document relocations all in place; 0 5xx; 0 [OWNERSHIP-REFUSED]; external routes had no traffic.
  - The ordinary-moves pass is still converging in time slices.
- **Pre-existing warnings, unchanged:** SECURE-CENSUS root-BU reach, the acme readability/ID-LINK failures (#2163), and the AI catalog-drift health check at startup (since 10-22).
- **Merged since checkpoint 78:**
  - #2481 (9f4e3820b2): the #2471 follow-up. Thread reads fail closed, the refusal has its own code and message, there is a per-pass filed cache, and Submit Invoice step A answers 503 on a fault.
  - Filed #2478: an Office filing is left behind after a refused save. Not an access issue.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge only when Router + "Client lint (type-aware ratchet)" are green; `--squash --match-head-commit <COMMIT sha>`, no `--delete-branch`, after `git merge-tree`. Deploy via R3 (owner's go given in R3's session). Task closes on merge + deploy + server-side gate. |
| **Fix bar** | Most technically correct fix. Every defect fixed or filed. PUBLIC repo: no exploit mechanism in issues/PRs/commits (neutral wording; owner decides tracking of access defects). |
| **Agents** | Opus executors/verifiers; Sonnet high for scoped re-checks. Model always stated. Never `taskkill /IM`. No `jq`: use `python -I`. Session cwd keep uppercase `C:/code_files/...`. |
| **Next action** | (1) Task 195 via task-execute; (2) BU1 Q2–Q12 recommendations for the owner; (3) review the #2483 follow-up when field-mapping sends it (its change 1 goes to the owner through them). Done: ontology PASS on the 9 Signals. Ordinary moves converged: 212 runs to about 03:05Z on 10-31, 28 moved, the last at 20:25Z, 0 refused or failed. Since then 0 5xx, 0 [OWNERSHIP-REFUSED] and no new errors. #2486 (field-mapping 017) approved by UAC at ff2f76e39e after the F1 fix (chat grantor None); carries #2483/#2484/#2485 and my #2487 (stale membership row until the nightly cleanup). |

## In flight
- Nothing running.
- Parked worktrees to clean up with `Remove-WorktreeSafe.ps1`: C:/wt2468, C:/wtuacq1, C:/wtuacwo and C:/wtuacrf and C:/wt2471f (all merged), and C:/wt194. C:/wt2468 is the deploy checkout (detached at 9f4e3820b2).

## Next (mine, in order)
1. Ontology's re-read of the 9 Signals → act on any findings. Then the convergence re-gate for ordinary moves.
2. (Done: the #2471 follow-up merged as #2481.)
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
- Merged: #2471 (9f4afe82ac, fixes #2468), #2474 (cd3f84662e), #2475 (bf8c775ce2), #2477 (c30686bdfc, fix 3), #2481 (9f4e3820b2), #2440, #2451.
- Deployed 9f4e3820b2 to dev; both switches ON; gates PASS.
- 9f4afe82ac deployed to dev; gate PASS.
- Q1 applied on dev, plus the externalserviceconfig grant removed (Verify PASS). Filed #2472 and #2473.
- Peer reviews:
  - ontology 168 PASS (#2463);
  - ebilling #1911 and the Adjustment-line PASS;
  - R3 SPA-only deploy OK.

## Peers
R3 `spaarke-wt-spa-external-access-platform-ce`; ebilling `spaarke-wt-ebilling-r1-0a`; field-mapping `spaarke-wt-field-mapping-server-write-pa-11`; ontology `spaarke-wt-spaarke-ontology-platform-r1-be`; word-add-in; provisioning.
