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
| **Next Action** | 1) 176 smoke still owed: needs an open Revise-budget Work Item on dev (test data = owner's go); tell uac-r2 when it runs. 2) **#2695 VERIFIED on dev `6dbe69b9e2`** (owner 2026-11-12: new Assign Work email saved Resolved, panel shows the matter confirmed); the one pre-deploy row (2026-11-11 20:01) marked Resolved with the owner's go and read back. Nothing left on #2695. 3) Tell word-add-in-r1 master `c152d7d0b1` when it reappears. |
| **Uncommitted** | None. |

## Open items

**Owner**
- **Dev BFF = `6dbe69b9e2`** (uac-r2 deploy 2026-11-12: master `a654da59c4` + mcp-r1 spike, local merge; DLL read by us). Contains 177, #2689, #2691, ebilling 116, **#2695**. 177 re-check had PASSED on the previous build `b3ca99cad5`.
- **#2695 merged to master 2026-11-11 as `25ac8ea931`** (squash; outgoing email association status → Resolved when the send files it; replies Resolved only from a Resolved parent). Not deployed. Dev backfill of 76 existing rows done (owner-approved). Worktree `C:\wtoas` removed.
- **177 smoke PASSED on dev 2026-11-11** (owner, Assign Work task email): Outgoing, internal-only, `sprk_regardingmatter` set, .eml document on the same matter, shows in Messages + Documents. Sent to uac-r2.
- **176 smoke NOT RUN:** no open Revise-budget Work Item on dev to send a Budget inquiry from; verify `sprk_regardingmatter` + `sprk_regardingservicerequest`, internal-only, Private thread, .eml on the matter, Messages. Note: after #2695 deploys, inquiry replies auto-file (Resolved) instead of Suggested; `ConfirmSuggestedAssociationAsync` then no-ops (owner-approved).
- **UAT round 4 (2026-11-12, owner as testuser1):** 162, #2394, 114, 111 row 4, F-R3-1 PASS → tasks 111, 114, 162 closed (POML + index + completion; drift check clean); #1428 closed. Still open: 160 + 161 (proposed: close 161 on DR-1167 + round 3 evidence; fold 160's visual checks into the 176 test), 176 (needs a zz- fee/scope comm, owner's go), 105 (owner wants to review and update the R1–R24 checklist). **090 held.**
- **New round 4 findings (uat-v4-log Round 4):** F-R4-1 defect: Create Project wizard outside-counsel search queries `sprk_organization.sprk_name` (does not exist; column is `sprk_organizationname`) at `CreateProjectWizard.tsx:141-152` and `projectService.ts:187-197`; fix = use `matterService.searchOrganizationsAsLookup`. F-R4-2 request: side-pane lookups in the create wizards' Assign Work step. F-R4-3 request: create wizards' Send Email step on the shared EmailComposer. **F-R4-1 fix: PR [#2737](https://github.com/spaarke-dev/spaarke/pull/2737)** (branch `fix/create-project-org-search` from master `9456083c78`, head `5843e678e2`, worktree `C:\wtorg`; owner said "Yes, fix it"): both searches now call `matterService.searchOrganizationsAsLookup`; new test fails 2/3 before, passes after; 24 suites / 287 tests, tsc clean. Auto-merge (squash) ENABLED 2026-11-12 on the owner's "merge on green". Next: GitHub merges on green → Console auto-deploys from master → owner re-checks Outside Counsel → remove `C:\wtorg` with Remove-WorktreeSafe.ps1. Owner still to say where F-R4-2/3 go.
- **Terminology:** connector renamed **Spaarke MCP** (owner 2026-11-10, recorded in mcp-bff `phase0-registration-facts.md`); `ontology-component-model.md` §1.1 rows updated on docs 2026-11-12 at mcp-r1's request; mcp-r1 task 050 (root CLAUDE.md product-names row) waits on it reaching master with the next docs→master PR.
- 160 + 161 were exercised by the owner 2026-11-09 22:14Z (DR-1167 on REAL-2026-123456.02: Send budget inquiry Done, Revise budget Done, Approve variance Skipped) but no verdict is recorded in `notes/uat-v4-log.md`; that inquiry predates 176 on dev, so the 176 smoke still needs a NEW fee/scope item (none open on dev 2026-11-12: SIG-1467 and SIG-1468 both Resolved).
- 3 zz- communication deletes: the owner's condition ("after the Revise budget test") is met (DR-1167); the delete call still needs the explicit yes: `c73b1661-5dda-f111-84e8-00224829aef9`, `c93b1661-5dda-f111-84e8-00224829aef9`, `16613093-2fdb-f111-84e7-000d3a9c9c16`.
- DEF-059 (#2635) is DONE (closed 2026-11-09): option C, a one-privilege Decision Record reader role on the BFF identity only, live on dev; customer environments go to customer-provisioning-orchestration-r1. Nothing owed by the owner.
- F-R3-1 footer fix is on dev (in `a654da59c4`): owner can confirm an Assign Task notification footer shows no logical name or GUID.

**uac-r2 (`uac-r2`)**
- #2670 (198, Direct 1:1 participant access) merged to master `c71414c7a0`, not deployed. Our post-merge review found **F-198-1** (race in `ReownToDirectAsync`); their fix **#2673** (`2bb9f25ee6`) **re-checked and approved by us** — they merge on green. K1–K5 recorded in their 198 notes §7.8.

**ebilling-r1 (`ebilling-r1`)**
- EB-D-216 (inactive Draft policy versions): no draft lister on our side; their guard stays out of `Services/Ontology/**` and `Services/Signals/**`; `ReadVersionsAsync` stays unfiltered. They send file:line if their sweep finds one of ours.
- DEF-061 review F1 + rulings sent; waiting for their follow-on SHA. Owed by them: RowReads rerun, DEF-059 outcome. DEF-060 builds after 116 + 130.
- **084 (EB-D-217, owner brought into R1):** 084d adds a caller-read dry-run route over `Services/Signals/Billing/ProcedureResolver.cs` via an in-memory row-source overlay (selection logic unchanged). They send the design before building, plus any `Services/Signals/**` diff. Our review conditions sent 2026-11-11: overlay at the row-source seam only; parity test through the real resolver; caller-read filtering (D-123); per-request, uncached; no writes; route census. Part A and 084a–c don't touch Signals. **Owner moved the mark to new task 084e (EB-D-219), built under our R1–R6 (binding constraints + our must-fire/must-not-fire cases as acceptance; opus).** 084e step 1 sends us the shared function's signature + evaluator call site BEFORE building; Signals diff + parity test to us before merge. 084 itself ships four marks (excluded, changed, stale, base retired) with no `Services/Signals/**` change. Our original call: show it only as "Outranked by X" with the "unless X is refused on an invoice; a line can differ" note, under R1–R6 (top = highest gate-accepted, not-downgraded rank; Split needs a stated cap; ties never; "by" lists all top-rank displacers; ONE pure precedence function extracted from evaluator step 4 into Services/Signals/Billing with a parity test, diff to us; legacy versions grouped too); otherwise leave it out. Reason: HandlerAccepts runs on the invoice facts, so validity is not knowable per record (B2).
- **Held PRs ours:** 174 (#2580) and 175 (#2579) wait for ebilling 112 (Invoice Review) on dev; then merge both and send SHAs to uac-r2.

**provisioning-r1** — task 267 (writer into its own business unit): we answered "the writer reads no field-secured column as itself"; recorded in their notes. No ACME check needed now.

**mcp-r1** — pings us when #2587 merges; then DI-143 #2630 builds on `WorkItemPrivilege`.

**Ours, after the critical path:** DI-144 #2632, DI-145 #2639, DI-146 #2650, DI-147 #2653, **DI-148 #2671**, **DI-149 #2672**; F-R3-1 footer fix (reaches master with this docs→master); `OntologyAdminClassificationService.cs:658/685/705` waits for `sprk_publishstate`; stale `DecisionRecordWriter.cs` comments wait for ebilling 074; close #2212 when ebilling `f0884a175f` reaches master.

**Worktree cleanup (use `scripts/worktree/Remove-WorktreeSafe.ps1` only):** 176/177 agent worktrees removed 2026-11-11 (tips verified inside #2666 / master). Left: task174 `agent-a87a71b050eceafb1` under `C:/code_files/spaarke/.claude/worktrees/` (after #2580/#2579 merge).

**ebilling master PR [#2689](https://github.com/spaarke-dev/spaarke/pull/2689):** ontology scope **APPROVED 2026-11-11 at `ec11c6d0e2`** (PR comment 6799396706; one adversarial review, no F-class; DEF-061 `340b6c53ad` approved explicitly; test fixes `49a49437bc` + `14e54c9e06` accepted). Public K1 (`sprk_publishedon` before BFF), K3, K5 on the PR; K2/K4 PRIVATE, sent to ebilling by message. **MERGED 2026-11-11 as master `2fc434c912`** (head `60628550fb`; delta since `ec11c6d0e2` has no change in `Services/Signals/**`, `Api/Signals/**`, `FinanceBaseValues.cs`). Their K4 O3 guard comment fix `584f6cc34d` (work/ebilling-r1) **checked 2026-11-12: K4 closed**, rides their next master PR; non-blocking note sent (ReadingRoleFile still names Ontology Service while DEF-059's reader role supplies the BFF read); 116 is open to master as [#2724](https://github.com/spaarke-dev/spaarke/pull/2724) (checked at `2c4ef48445`: no ontology-owned files; billing drafts can't reach our readers, which filter `sprk_domain` to Ontology). They still send a notice before any live `sprk_policyversion` write.

## Dev state

- Every dev BFF deploy runs in uac-r2's session with the owner's go per SHA; before/after notices to word-add-in, R3/R4 and uac-r2. Console auto-deploys from master (CLAUDE.md §6).
- Stash: two old `lint-staged automatic backup` entries are not ours. Leave them.

## Peers (re-check with ListAgents)

Desktop: `uac-r2` (dev BFF deploy owner) · `provisioning-r1` · `ebilling-r1` · `mcp-r1` · `word-add-in-r1` · `acclerator-r1` · `spa-r4` · `field-mapping-r1` · `accelerator-claude-r2`. VS Code: R3 `spaarke-wt-spa-external-access-platform-1f` (no longer deploys).
