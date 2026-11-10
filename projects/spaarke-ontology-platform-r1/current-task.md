# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-11-09 (by context-handoff, before moving this project's main session from VS Code to the Claude Desktop
> session **`ontology-r1`**). State only: history is in git log, task `<completion>` blocks and `notes/handoff-history/2026-11.md`
> (the previous checkpoint is archived there verbatim). Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 and §6. Decisions:
> [`notes/decisions.md`](notes/decisions.md) (D-1..D-215). Deferred work: one self-contained GitHub issue per item, label
> `ontology-deferred`, plus a DI-nn row in both tables of `notes/defer-issues.md` (next free: **DI-146**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Role** | Main session for ontology R1: orchestrate to completion; independent review of every PR touching ontology-owned files (`Services/Signals/**`, `Api/Signals/**`, `Services/Finance/FinanceBaseValues.cs`, `Services/Signals/Billing/**`); answer peers; hold wrap-up 090 until the owner's UAT is done. Peer messages are not owner approvals; owner choices relayed by peers are recorded as "relayed". |
| **Working dir** | `C:\code_files\spaarke-wt-spaarke-ontology-platform-r1`, branch `docs/ontology-platform-design`. The new session must open THIS folder so the project memory (`~/.claude/projects/c--code-files-spaarke-wt-spaarke-ontology-platform-r1/memory/`), hooks and this file apply. |
| **Uncommitted** | None after this checkpoint commit. Agent worktree left: task174 (`C:/code_files/spaarke/.claude/worktrees/agent-a87a71b050eceafb1`, idle; remove with `Remove-WorktreeSafe.ps1` after #2580/#2579 merge). |
| **Next Action** | (1) Tell the owner the fee/scope item on REAL-2026-123456.02 is up (Signal `317dba75-e5db-f111-84e7-00224829aef9`, Decide, Active, raised 2026-11-09 02:30Z by the nightly; MCP shows 21:30 because it returns local time UTC-5): test **Revise budget** (161). (2) After that test, delete the three zz- comms (owner said yes 2026-11-08; MCP delete asks again at call time): `c73b1661-5dda-f111-84e8-00224829aef9`, `c93b1661-5dda-f111-84e8-00224829aef9`, `16613093-2fdb-f111-84e7-000d3a9c9c16`; confirm by query. (3) Wait for ebilling's 165b-4 AuthorableColumns full-green counts (after 166 is applied on dev) before the BFF build carrying 165b-4 deploys to dev. |

## Open items

**Owner (UAT and decisions)**
- **161 Revise budget** on REAL-2026-123456.02: ready now (above). **Create budget on MAT-34929-0040: DONE 2026-11-09 03:58Z** (MCP local time 22:58) (budget 120,000; DR-1164 `688099cf-f1db-f111-84e7-00224829aef9`, steps create-budget + assign-task). That decision also closed 17 "Submission integrity (system)" billing Signals in the same matter row: the owner chose that deliberately ("add-on option ... a good feature; no changes necessary"; D-214). Reopen them only if another test needs them (Signal writes need the owner's yes).
- **162:** owner checks uploads in Create Work Assignment / Project / Invoice.
- **Still open UAT:** 111 row 4, 114, 105, 090 (wrap-up held). Owner re-checks: project create as a Core User (#2394), check 6 (the composer); then close 114, 158–163, 166.
- **DEF-059 (#2635, ebilling's):** BFF identity Decision Record read. Owner leans option C (new read-only role, Global Read on `sprk_decisionrecord`) and will take it up in ebilling's session. Our position: A (nothing; System Administrator covers it) is safe today; B (Spaarke Ontology Service) is wrong (carries Signal/DR create/write, breaks writer separation); Ontology Administrator is wrong (human-held, Deep, plus authoring rights; Global would bypass D-123 for human admins).
- **Task 166 (ebilling field security on invoice totals):** owner said yes here; ebilling requires the go typed in its own session (told the owner).

**ebilling-r1 (`spaarke-wt-ebilling-r1-c7`)**
- **165b-4** (spend from server-written sources) and **085** (procedure resolver) are MERGED into `work/ebilling-r1` (`5ebe26a03d`, `052307dc0b`) after our approvals. Our rulings: lazy writer-id provider (no config-keyed zero; counted rows + no writer = unknown); D-213 (a Published rate card is not evidence a version opened a window; ebilling DEF-061 #2640 = publish stamp, owner-gated schema). **Deploy gate:** full `AuthorableColumns_ExistAndAreNotFieldSecured` green on dev after 166's apply, counts to us, before any BFF with 165b-4 reaches dev. Any later fix touching `FinanceBaseValues.cs` or `Services/Signals/**` comes to us as a diff first.
- **Held PRs ours:** 174 (#2580, BFF route refuses billing Signals) and 175 (#2579, Invoice decisions card) wait for ebilling 112 (Invoice Review) on dev; then merge both and send SHAs to uac-r2.

**spaarkeai-mcp-bff-api-r1 (desktop `mcp-r1`)**
- FR-04 ruled (D-212). Their 018 (`WorkItemPrivilege`) reviewed OK; it reaches master only with their project PR #2587 (not soon). They ping on merge; then DI-143 #2630 builds on `WorkItemPrivilege.SourcesOf` + `FlaggedAsync`. Next from them: reviews of 022–024.

**uac-r2 (`uac-r2`)**
- **D-215 (owner, 2026-11-09):** gate Decision Records of Direct messages follow the message's internal participants (shared at write, kept on ordinary filing, re-owned on unfile back to Direct). uac 198 Part E edits `GateDecisionRecordSharing.cs` after fm 054a lands `sprk_directrecipient` and 198's R2 rework; the 7 conditions + unfile-back case were sent privately by message. **We review the diff + one independent review before merge.** Waiting for their SHA.
- Move notice to `spa-r4` expired undelivered (permission-mode mismatch); owner told.

**Ours, after the critical path:** DI-144 #2632 (writer lookups also require `createdonbehalfby` empty); DI-145 #2639 (environment identity ids in source/tests/notes, public-repo hygiene); F-R3-1 footer fix (on docs only; needs docs→master + dev deploy); `OntologyAdminClassificationService.cs:658/685/705` waits for `sprk_publishstate`; five stale `DecisionRecordWriter.cs` comments wait for ebilling 074; close #2212 when ebilling `f0884a175f` reaches master.

**Dev checks DONE 2026-11-09 (ontology-r1):** (a) `20669df6a9` gate open point **PASS**: Signals routes since 16:44:51Z all 200/204 (worklist 9, decision-plan 5, suppression 4, decision POST 2), no 5xx, no 403. (b) Task 170 **PASS**: 1 WhoAmI per commit (was 35); wall time 18.6 s (one decision) and 41.7 s (Create budget that also closed 17 billing items, D-214), about 18 sequential Dataverse calls per item closed. (c) **BUD-001003** on the new budget: PASS. Evidence row in `notes/168-173-live-proof.md` §6. Method: Log Analytics `spe-logs-dev-67e2xz` (`AppRequests`/`AppDependencies`), because the App Insights query returns only the current day; telemetry is sampled at 25%. **Gotcha:** Dataverse MCP returns datetimes in local time (UTC-5), not UTC.

**(verify) older lane items:** uac-r2's move of root-homed humans to BU1: hold the memo-LIKE proof until they confirm seed-policy placement. fm 044 (core-pair FLS) is on master per D-210; confirm 052's rewording (cite D-204) was agreed and the post-apply census + Worklist re-read was done.

**ACME:** on SpaarkeMaster 1.3.0.0 (deploy-log `ba14945d53`). Still to verify: the ACME writer actually writes after its next evaluator run. Provisioning items go to `provisioning-r1`. **Ask ready, not sent (provisioning-r1 offline 2026-11-09):** on ACME, the 02:30Z `SignalReevaluationJob` runs on 11-08/11-09 (counts, writer failures), the count of `sprk_signal`/`sprk_decisionrecord` rows created by the writer app user since 2026-11-07, and if zero, whether ACME has any data the policies can match (else "not yet provable", not failed).

## Dev state (spaarkedev1 / spaarke-bff-dev)

- **BFF:** `20669df6a9` live since 2026-11-06 16:44:51Z (uac-r2, owner go; gate PASS). Every dev BFF deploy runs in uac-r2's session with the owner's go per SHA; send before/after notices to word-add-in, R3/R4 and uac-r2.
- **SpaarkeMaster:** 1.3.0.0 on ACME; dev per the deploy log. **POL-COMMIT-BUDGET** (fee/scope) live; it raised on MAT-34929-0040 (now Acted) and REAL-2026-123456.02 (open).
- **Stash:** two old `lint-staged automatic backup` entries are not ours. Leave them.

## Next docs→master

Last: PR #2463 → master `c8bb55d0cd` (2026-10-29). Before the next: notices to R3, uac-r2 and word-add-in; merge master into docs; build, filtered Signals tests, full ArchTests. Don't push to docs while a docs→master PR is open.

## Peers (as of 2026-11-09; re-check with ListAgents)

Desktop sessions: uac-r2 **`uac-r2`** (dev BFF deploy owner; a held message means the owner approves it there) · provisioning **`provisioning-r1`** (#2605, #2618, role check at next stamp upgrade) · word-add-in **`word-add-in-r1`** · legal-ai-accelerator **`acclerator-r1`** (spelled so) · R4 **`spa-r4`** · mcp-bff **`mcp-r1`** (pings us when #2587 merges) · accelerator-claude-r2 `accelerator-claude-r2` · field-mapping **`field-mapping-r1`** (moved 2026-11-09; 8e0b48eadb reaches master in its next PR).
VS Code sessions: ebilling `spaarke-wt-ebilling-r1-c7` · R3 `spaarke-wt-spa-external-access-platform-1f` (no longer deploys).
This project's own session is now **`ontology-r1`** (desktop); the VS Code session `spaarke-wt-spaarke-ontology-platform-r1-38` is retiring.
