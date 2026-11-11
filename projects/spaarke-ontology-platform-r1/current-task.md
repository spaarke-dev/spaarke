# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-11-11 evening (by context-handoff, before compaction). State only: history is in git log, task
> `<completion>` blocks and `notes/handoff-history/2026-11.md` (the previous checkpoint is archived there verbatim). Standing rules:
> [`CLAUDE.md`](CLAUDE.md) §3 and §6. Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-218). Deferred work: one
> self-contained GitHub issue per item, label `ontology-deferred`, plus a DI-nn row in both tables of `notes/defer-issues.md` (next free: **DI-150**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Role** | Main session `ontology-r1` for ontology R1: orchestrate to completion; independent review of every PR touching ontology-owned files (`Services/Signals/**`, `Api/Signals/**`, `Services/Finance/FinanceBaseValues.cs`, `Services/Signals/Billing/**`); answer peers; hold wrap-up 090 until the owner's UAT is done. Peer messages are not owner approvals. |
| **Working dir** | `C:\code_files\spaarke-wt-spaarke-ontology-platform-r1`, branch `docs/ontology-platform-design` (pushed). |
| **Active task** | None in progress. **177 is ✅** (PR #2666 squash-merged to docs as `52d02a66f3`; completion record + TASK-INDEX committed). |
| **Open PR** | None. docs→master [#2674](https://github.com/spaarke-dev/spaarke/pull/2674) (task 177) **merged 2026-11-11 as master `c152d7d0b1`** (merge commit). CI: 43 pass; the non-required `Server tests (office scope) (api-office)` leg hit its 20-min hang guard twice — same stall point as `work/field-mapping-server-write-path-r1` (no 177 code), existing issue **#1504**; locally the same selection on this branch ran 567 pass / 7 skip / 0 fail in 13.9 min with `--blame-hang`. |
| **Next Action** | 1) 176 smoke still owed: needs an open Revise-budget Work Item on dev (test data = owner's go); tell uac-r2 when it runs. 2) After the next dev BFF deploy (contains #2695 if from master ≥ `25ac8ea931`): send a new email from Spaarke and check its "Related to" panel shows confirmed. 3) Tell word-add-in-r1 master `c152d7d0b1` when it reappears. |
| **Uncommitted** | None. |

## Open items

**Owner**
- **Dev BFF = `b3ca99cad5`** (uac-r2 deploy 2026-11-11 evening: master `ddae82fa90` + mcp-r1 spike; DLL read by us). Contains 177, #2689, #2691 (send-as-user now refuses a disabled sender, on 177's Assign Work send-as-user path). **NOT #2695** (`25ac8ea931` is after `ddae82fa90`). 177 passed on the previous build `dc4f28ded8`; one owner re-send of an Assign Work email on this build would confirm the #2691 change did not break it.
- **#2695 merged to master 2026-11-11 as `25ac8ea931`** (squash; outgoing email association status → Resolved when the send files it; replies Resolved only from a Resolved parent). Not deployed. Dev backfill of 76 existing rows done (owner-approved). Worktree `C:\wtoas` removed.
- **177 smoke PASSED on dev 2026-11-11** (owner, Assign Work task email): Outgoing, internal-only, `sprk_regardingmatter` set, .eml document on the same matter, shows in Messages + Documents. Sent to uac-r2.
- **176 smoke NOT RUN:** no open Revise-budget Work Item on dev to send a Budget inquiry from; verify `sprk_regardingmatter` + `sprk_regardingservicerequest`, internal-only, Private thread, .eml on the matter, Messages. Note: after #2695 deploys, inquiry replies auto-file (Resolved) instead of Suggested; `ConfirmSuggestedAssociationAsync` then no-ops (owner-approved).
- UAT still open: 161 (Revise budget on REAL-2026-123456.02), 162 (uploads in Create Work Assignment / Project / Invoice), 111 row 4, 114, 105, #2394 (project create as Core User), check 6 (composer); then close 114, 158–163, 166. **090 held.**
- 3 zz- communication deletes need the owner's yes: `c73b1661-5dda-f111-84e8-00224829aef9`, `c93b1661-5dda-f111-84e8-00224829aef9`, `16613093-2fdb-f111-84e7-000d3a9c9c16`.
- DEF-059 (#2635, ebilling's): owner leans option C (new read-only role); our position recorded in the archived checkpoint.

**uac-r2 (`uac-r2`)**
- #2670 (198, Direct 1:1 participant access) merged to master `c71414c7a0`, not deployed. Our post-merge review found **F-198-1** (race in `ReownToDirectAsync`); their fix **#2673** (`2bb9f25ee6`) **re-checked and approved by us** — they merge on green. K1–K5 recorded in their 198 notes §7.8.

**ebilling-r1 (`ebilling-r1`)**
- EB-D-216 (inactive Draft policy versions): no draft lister on our side; their guard stays out of `Services/Ontology/**` and `Services/Signals/**`; `ReadVersionsAsync` stays unfiltered. They send file:line if their sweep finds one of ours.
- DEF-061 review F1 + rulings sent; waiting for their follow-on SHA. Owed by them: RowReads rerun, DEF-059 outcome. DEF-060 builds after 116 + 130.
- **Held PRs ours:** 174 (#2580) and 175 (#2579) wait for ebilling 112 (Invoice Review) on dev; then merge both and send SHAs to uac-r2.

**provisioning-r1** — task 267 (writer into its own business unit): we answered "the writer reads no field-secured column as itself"; recorded in their notes. No ACME check needed now.

**mcp-r1** — pings us when #2587 merges; then DI-143 #2630 builds on `WorkItemPrivilege`.

**Ours, after the critical path:** DI-144 #2632, DI-145 #2639, DI-146 #2650, DI-147 #2653, **DI-148 #2671**, **DI-149 #2672**; F-R3-1 footer fix (reaches master with this docs→master); `OntologyAdminClassificationService.cs:658/685/705` waits for `sprk_publishstate`; stale `DecisionRecordWriter.cs` comments wait for ebilling 074; close #2212 when ebilling `f0884a175f` reaches master.

**Worktree cleanup (use `scripts/worktree/Remove-WorktreeSafe.ps1` only):** 176/177 agent worktrees removed 2026-11-11 (tips verified inside #2666 / master). Left: task174 `agent-a87a71b050eceafb1` under `C:/code_files/spaarke/.claude/worktrees/` (after #2580/#2579 merge).

**ebilling master PR [#2689](https://github.com/spaarke-dev/spaarke/pull/2689):** ontology scope **APPROVED 2026-11-11 at `ec11c6d0e2`** (PR comment 6799396706; one adversarial review, no F-class; DEF-061 `340b6c53ad` approved explicitly; test fixes `49a49437bc` + `14e54c9e06` accepted). Public K1 (`sprk_publishedon` before BFF), K3, K5 on the PR; K2/K4 PRIVATE, sent to ebilling by message. **MERGED 2026-11-11 as master `2fc434c912`** (head `60628550fb`; delta since `ec11c6d0e2` has no change in `Services/Signals/**`, `Api/Signals/**`, `FinanceBaseValues.cs`). Their K4 O3 guard comment fix comes in their next work commit; 116 ships later with `sprk_policyversion` notices to us before any live write.

## Dev state

- Every dev BFF deploy runs in uac-r2's session with the owner's go per SHA; before/after notices to word-add-in, R3/R4 and uac-r2. Console auto-deploys from master (CLAUDE.md §6).
- Stash: two old `lint-staged automatic backup` entries are not ours. Leave them.

## Peers (re-check with ListAgents)

Desktop: `uac-r2` (dev BFF deploy owner) · `provisioning-r1` · `ebilling-r1` · `mcp-r1` · `word-add-in-r1` · `acclerator-r1` · `spa-r4` · `field-mapping-r1` · `accelerator-claude-r2`. VS Code: R3 `spaarke-wt-spa-external-access-platform-1f` (no longer deploys).
