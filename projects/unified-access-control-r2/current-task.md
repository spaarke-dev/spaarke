# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md` (latest 2026-10-29: WA owner paths follow the matter; "a family shares one ownership; records are owned by BUs/teams").
> - **Defer list:** `notes/defer-issues.md`. **Previous copy:** `notes/handoff-history/current-task-2026-10-22-cp60.md`.

> **Last Updated**: 2026-10-30 ~13:40Z (checkpoint #76).

## #2468 is DONE: PR #2471 merged, master 9f4afe82ac
- UAC OK has been sent to R3 for the dev BFF deploy of **9f4afe82ac**. R3 builds it, collects 5 OKs and asks the owner for a go in R3's session.
- **After the deploy:** R3 sends the SHA and time, then run the App Insights gate (`uacmerge/uacg/uacg_gate.sh`), then tell ebilling.
- No data repair is needed: 194b never deployed (R3's live read and a DV query).
- **Follow-up PR** (K, non-blocking):
  - put EmailFiling on the early OFFICE_022 refusal (`OfficeService.cs` ~682);
  - choose the refusal message by RefusalCode, with a dedicated reason for the unfiled-communication no-parent case;
  - `IsCommunicationFiledAsync`: stop catching InvalidOperationException, and have `ReadThreadAsync` return null when the thread is missing;
  - cache the filed answer per communication id in `RecordOwnerLookupCache`;
  - optional: add the thread's parents to the secure-only check.
  - Also file an issue for the OFFICE_022 leftover filing (not an access problem: `FileIfUnfiledAsync` checks the caller's Write).

## Merge queue (ONE merge job at a time)
- **#2474** (Q1: product-config Global read + playbook admin-only gate + unused grant removed / credential column secured):
  - head 053e65d951; watcher `bwyhk2sui` (`scratchpad/merge_wait_2474.sh`);
  - reviewed: 2 Opus passes + a Sonnet re-check, no open F;
  - dev: applied, `-Verify` PASS. The column secure lands at the next SpaarkeMaster import.
- **Next:** `fix/uac-r2-wa-owner-paths` (C:/wtuacwo):
  - unit is clean (the 114 timeout classes pass alone, 2364/2364);
  - merge master (now containing #2471: resolver overlap), rebuild, re-run the resolver/Office/SecureChild classes and the integration suites;
  - then push and open the PR (body: `scratchpad/uacwo_pr.md`; fill in the RESULTS_* lines).
- **Owner-private items** (pre-existing grants; Office Message-ID; checkpoint #75 wording in history): `scratchpad/q1_owner_private.md`. The owner decides tracking.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge only when Router + "Client lint (type-aware ratchet)" are green; `--squash --match-head-commit <COMMIT sha>`, no `--delete-branch`, after `git merge-tree`. Deploy via R3 (owner's go given in R3's session). Task closes on merge + deploy + server-side gate. |
| **Fix bar** | Most technically correct fix. Every defect fixed or filed. PUBLIC repo: no exploit mechanism in issues/PRs/commits (neutral wording; owner decides tracking of access defects). |
| **Agents** | Opus executors/verifiers; Sonnet high for scoped re-checks. Model always stated. Never `taskkill /IM`. No `jq`: use `python -I`. Session cwd keep uppercase `C:/code_files/...`. |
| **Next action** | (1) Wait for R3's deploy of 9f4afe82ac, then run the server-side gate; (2) #2474 merges via watcher `bwyhk2sui`; (3) WA owner-paths branch: merge master, re-test, open the PR; (4) #2471 follow-up PR. |

## In flight

1. **`fix/uac-r2-wa-owner-paths`** (`C:/wtuacwo`, not pushed; HEAD 84e0c60480):
   - 9 commits plus a merge of master c8bb55d0cd;
   - reviews: 2 Opus passes and 2 re-checks, no open F;
   - unit is clean (see the merge queue); ArchTests 1858/1858;
   - dev impact (owner GO): 2 events → BU1; 1 document and 1 communication (user-owned) are report-only. Write after the report-only run confirms.
2. **`fix/uac-r2-root-family-owner`** (`C:/wtuacrf`, stacked on the WA branch; fix 3: 796336a9c7, 99f4d0ede0; not pushed):
   - roots are re-owned to their family team via `FollowParentsPassAsync`, behind `SecureRoot__Inheritance__RootOwnerWritesEnabled`;
   - still open:
     - decide whether rule 2 excludes projects until task 195;
     - then 2 Opus security passes;
     - PR after the WA branch merges;
     - dev data move (owner approved).
3. **R3 dev BFF deploy of 9f4afe82ac:** UAC OK sent. It waits on R3's 5 OKs and the owner's go in R3's session.
   - Once R3 reports the deployed SHA and time: run the App Insights gate (SecureChild reconciler, owner refusals, census), then tell ebilling (they wait for the 194b deploy notice and the #2427 sha).
   - Accepted until the BU1 move: root-homed users are refused for hard-deny writes.

## Next (mine, in order)
1. After #2474 merges: WA owner-paths branch: merge master, re-test, open the PR, merge.
2. Fix 3: resolve the rule-2-vs-projects question → reviews → PR (after #1).
3. #2471 follow-up PR (see above).
4. **Task 195** (#2352): the Create Project wizard writes the project pair, plus a dev backfill. Field-mapping task 040 waits on it. Run via task-execute.
5. BU1 move: bring the owner recommended answers for Q2–Q12 (`$TEMP/uacmerge/uacg/rbu1/rbu1_plan.md` §6), including K4 (reassign product rows first).
6. Parked (verify state):
   - task 193 (C:/wt193), task 197 (C:/wt197);
   - #2427 output lookups (C:/wt2427; ebilling waits for its sha);
   - 196a/196b;
   - 194b close (remove C:/wt194 via Remove-WorktreeSafe.ps1).
   - Remove C:/wt2468 via Remove-WorktreeSafe.ps1 (#2471 merged).

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
- #2471 merged (9f4afe82ac): it fixes #2468. #2440 and #2451 merged.
- Q1 applied on dev. #2474 opened. Filed #2472 and #2473.
- Peer reviews:
  - ontology 168 PASS (#2463);
  - ebilling #1911 and the Adjustment-line PASS;
  - R3 SPA-only deploy OK.

## Peers
R3 `spaarke-wt-spa-external-access-platform-ce`; ebilling `spaarke-wt-ebilling-r1-0a`; field-mapping `spaarke-wt-field-mapping-server-write-pa-11`; ontology `spaarke-wt-spaarke-ontology-platform-r1-be`; word-add-in; provisioning.
