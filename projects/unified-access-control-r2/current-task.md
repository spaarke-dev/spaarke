# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md` (latest 2026-10-29: WA owner paths follow the matter; "a family shares one ownership; records are owned by BUs/teams").
> - **Defer list:** `notes/defer-issues.md`. **Previous copy:** `notes/handoff-history/current-task-2026-10-22-cp60.md`.

> **Last Updated**: 2026-10-29 late (checkpoint #75, by context-handoff).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge only when Router + "Client lint (type-aware ratchet)" are green; `--squash --match-head-commit <COMMIT sha>`, no `--delete-branch`, after `git merge-tree`. Deploy via R3 (owner's go given in R3's session). Task closes on merge + deploy + server-side gate. |
| **Fix bar** | Most technically correct fix. Every defect fixed or filed. PUBLIC repo: no exploit mechanism in issues/PRs/commits (neutral wording; owner decides tracking of access defects). |
| **Agents** | Opus executors/verifiers; Sonnet high for scoped re-checks. Model always stated. Never `taskkill /IM`. No `jq`: use `python -I`. Session cwd keep uppercase `C:/code_files/...`. |
| **Next action** | (1) Decide/act on Q1 review F1 (playbooks, below) — recommend option (b); (2) when unit rerun `bznu71zi4` finishes: triage failures from `$TEMP/uacwo_trx/run.trx`, then push `fix/uac-r2-wa-owner-paths` + PR (body: scratchpad `uacwo_pr.md`, fill RESULTS_*) ; (3) R3 deploy gate when R3 sends SHA. |

## In flight

1. **`fix/uac-r2-wa-owner-paths`** (worktree `C:/wtuacwo`, NOT pushed). HEAD `84e0c60480` = 9 commits (3acbf7e220 fix1, 49279436a6 fix2, 4b5feb88ad/7b5afddd4d/f96fe48a3c/eaf5c114e4 review fixes, cdb72ee781/cacaaea69f/ddb04d74f1 re-check fixes, d1dfcaf3a6 coverage) + merge of master c8bb55d0cd.
   - Reviews done: 2 Opus security passes, Opus re-check, Sonnet re-check — no open F.
   - First full unit run on merged branch: 62/37788 failed, all Office/Compose/Docs/AI/route-contract classes at ~3-4 min (host timeouts on a loaded box) + PredictMatterCost eval 30 min. ArchTests 1858/1858. Re-run with trx in progress (task `bznu71zi4`, results `$TEMP/uacwo_trx`). Re-run any still-failing class alone; if real, compare with master.
   - Dev impact (owner GO given): 2 events → BU1; 1 doc + 1 communication (user-owned) reported only. Writes after the report-only run confirms.
2. **`fix/uac-r2-root-family-owner`** (worktree `C:/wtuacrf`, stacked on 84e0c60480): fix 3 implementer running (agent `uacrf-fix3`). Roots (WA/project) re-owned to family team by `SecureRootInheritance.FollowParentsPassAsync`; unfiled person-owned → owner's BU default team; switch `SecureRoot:Inheritance:RootOwnerWritesEnabled` (report-only default); cap 25/run. Then 2 security passes. Dev estimate: 10 filed WAs → BU1 team, 12 unfiled → owner's unit team; projects no-op until task 195. Owner approved moving dev data.
3. **`fix/uac-r2-product-read-global`** (worktree `C:/wtuacq1`, commit `bb28f366b0`, NOT pushed). Q1: Core/Basic User Read Deep→Global on 12 product tables. **APPLIED on dev** (script `-Apply`, `-Verify` PASS; RetrievePrincipalAccess: BU1 user reads playbook/grid/tooltype; policy None).
   - Review (Opus) found: **F1** `sprk_analysisplaybook` holds per-user PRIVATE playbooks (`POST /api/ai/playbooks`, clone; `PlaybookAuthorizationFilter.cs:145-170` uses Dataverse Read) → Global makes private playbooks org-wide. Dev: 9 private rows, all seeded product, no customer content. Options: (a) restrict create/clone/share to AI Analysis Admin; (b) drop playbook from the list (back to Deep on dev), set the 9 product playbooks public — confirm no client reads playbooks via Xrm.WebApi. **Recommend (b).** **F2** guard must require privilegeName == "prvRead"+logicalName + must-fire. **F3 (pre-existing, file PRIVATELY/neutral)**: Core/Basic hold Global Read on `sprk_ExternalServiceConfig`, `sprk_clientsecret` not field-secured; 0 rows on dev. **K1** add `sprk_externalserviceconfig`, `sprk_dataverseenvironment`, `sprk_aimodeldeployment` to excluded/forbidden. K2 gridconfiguration saved-search latent (guard: no Create for Core/Basic). K4: all 299 product rows owned by owner's user `1d02f31c` → reassign to a root team/app user BEFORE the BU1 move (ties to Q2).
4. **R3 dev BFF deploy**: owner said yes (my session); UAC OK sent for **c91b99e575** (contains 194b 2cd0f62e2c, #2451, #1911 disabled, #2463). R3 needs owner's go in R3's session. On deployed SHA+time: run App Insights gate (`uacmerge/uacg/uacg_gate.sh`; SecureChild reconciler, owner refusals, census), then tell ebilling (they wait for 194b deploy notice + #2427 sha).
   - Accepted until BU1 move: root-homed users refused for hard-deny writes.

## Next (mine, in order)
1. Q1 F1/F2/K1 fix on `fix/uac-r2-product-read-global`, re-apply on dev, PR.
2. PR + merge `fix/uac-r2-wa-owner-paths`; send SHA to R3 for next deploy.
3. Fix 3 → reviews → PR (after #2 merges; rebase/merge).
4. **Task 195** (#2352): Create Project wizard writes the project pair + dev backfill. Field-mapping task 040 waits on it. Run via task-execute.
5. BU1 move: bring owner recommended answers for Q2–Q12 (`$TEMP/uacmerge/uacg/rbu1/rbu1_plan.md` §6). Include K4 (reassign product rows first).
6. Parked (verify state): task 193 (C:/wt193), task 197 (C:/wt197), #2427 output lookups (C:/wt2427; ebilling waits for sha), 196a/196b, 194b close (C:/wt194 remove via Remove-WorktreeSafe.ps1).

## Owner items outstanding
- Q1 F1 option (a)/(b) (recommend b).
- BU1 Q2–Q12.
- Older: decisions #2343, #2245, #2250, #2260, K5/#2270 (via R3), #2262, #2396; task 191 F1/S4; task 190 Apply; #2382 UAT check 5.

## Done this session (2026-10-29)
- PR #2440, #2451 merged; decisions recorded (3d07529242). Old branch `fix/uac-r2-wa-primary-order` deleted.
- Peer reviews: ontology 168 rounds 3-4 PASS (merged #2463); 173 sender-only OK; ebilling #1911 PASS + 5e863d29bd Adjustment-line ForChild PASS (merged eaa0d44f7f); R3 SPA-only deploy OK (194b hold = BFF only).
- Filed #2465 (filed-root listing scale).

## Peers
R3 `spaarke-wt-spa-external-access-platform-ce`; ebilling `spaarke-wt-ebilling-r1-0a`; field-mapping `spaarke-wt-field-mapping-server-write-pa-11`; ontology `spaarke-wt-spaarke-ontology-platform-r1-be`; word-add-in; provisioning.
