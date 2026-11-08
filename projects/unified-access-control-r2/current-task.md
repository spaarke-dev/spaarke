# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.

> **Last Updated**: 2026-11-08 (checkpoint #104: peer review stretch; 032b round 2 arrived, review next).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge needs Router + "Client lint (type-aware ratchet)": `git merge-tree`, then `--squash --match-head-commit <sha>`, no `--delete-branch`. Live actions, deploys, role and settings changes need the owner's go IN THIS SESSION; a peer's go is never the owner's. |
| **Running** | Nothing. Worktree clean, pushed. |
| **Dev state** | BFF = **6fbb3a7642** (master 23567b2f93 = 049c + MCP spike, PR #2615) since 2026-11-08 04:00:55Z. Gate PASS (`notes/deploy-gate-2026-11-08-6fbb3a7642.md`); two boot-window 500s unexplained: check the next deploy's boot window. **226 settings** (RecordCreation Debug removed 05:19:20Z), reconcile false. Rollback: `deploy/rollback/wwwroot-20261108T035611Z.zip` (22487c6537). Detached worktree `C:/wtd6fbb`: remove with `Remove-WorktreeSafe.ps1`. Master since: #2629 (57748ebf6d, fm 030b/034/052a), not on dev. Next deploy = master + spike, owner go per SHA. |
| **Next action** | Wait for peer items (no review in hand). fm: the 032b registry-removal PR (review: one-entry removal of sprk_invoice from CanonicalAccessConferringRegistry + guard) and 032b-2/3 code; contact owner rule after the owner picks the team. ebilling: ribbon source removal on master, then dev batch SHA for the gate. |

## Rulings this stretch (2026-11-08)
- fm 021 timing: dev export lost 20/20 to #2158; owner moved NFR-02 verdict to UAT. Closed.
- fm #2629 approved and merged (57748ebf6d).
- fm 032b round 1: approved with C1 (core pinned in the job payload; ebilling accepted) and C2 (settle U3 first). U1/U2/U4/U5 approved.
- ebilling (private detail only in messages): 165d APPROVED, merged into work 66b7c31315. 165e APPROVED; 165f APPROVED (165e K1 closed); 165e+165f ship in the same batch; gate watches `own_submission_missing` + `document_link_not_own` (dev baseline 0). 165b-3 part A + fix APPROVED; part B APPROVED after F1 (keyed batched read) fixed at 4ba88a6de1; Mark Paid also keyed. 082d APPROVED. 165b-4 spend: (b) keyed rows, K2 ok, parent scope ok (attribution known limit), live privilege check + #2635.
- ebilling 164c Q6 → (b): their billing-event ribbon import may replace exactly 8 SecureChild ids (merged into work 931fe905b0). Conditions: source removal (billingevent out of SecureChild `$Tables` + RibbonDiff) on master BEFORE their dev -Apply; I must NOT run Deploy-SecureChildNewCommands from a pre-merge master; read-backs to me: all 11 hides stored + SecureChild -Verify for the other 8 tables.
- Gate query uses a contains match on `changed_only_by_its_writer` (165d adds `.column`).

## Reviews queue
- fm: **032b round 2 APPROVED** 11-08 (U3 (c) confirmed on master + ebilling work: only project/matter/WA compose rights; no dev registry override; U7 accepted, owner to see it; E7 map-nothing OK under (c); inline placement OK; registry removal in fm's branch with my review); 045A design; 052b incl. the contact-only owner rule (after the owner picks the team); 032 round 2 waits on the owner; 076c push-preview fix (BFF + RecordFamily page) joins their next PR.
- ebilling: tell-me items: ribbon source removal reaching master; dev batch SHA (165e/f etc.) for the gate; 2b-2; the batch checklist + commit; field-security owner question.
- mcp-bff-api: Phase 0 waits on the owner's connector registration; A10, B-4, 021 later. Every deploy = master + spike until they say Phase 0 is done.

## Owner items outstanding
- **#2635** (BFF identity gets Decision Record read via a SpaarkeMaster role in env setup; ontology says the live test is enough, I recommend the grant) — linked to: prod-named SysAdmin app users on dev (BFF identities read Decision Records only via SysAdmin).
- Report card `sprk_assigned*` registry (FR-24); G-8 for customer envs; #2590; SyncMode -Apply review; the field-security bundle (via ebilling); optional retire of the task-154 control row.
- Older: column auditing on writer-only lookups, AP diagnostics, VS Code agent-dir cleanup, word-add-in Office saves, click-test 195, #2343, #2245, #2250, #2260, #2270, #2262, #2396, 191 F1/S4, 190 Apply, #2382, #2505; tasks 140/165/153.

## Private items (never in issues/PRs/commits)
- ebilling: submission link columns + sprk_document user-writable (165e/165f), portion claim columns (165b F1; F1 portion-edit lever kept fail-closed), hold column (165a), Decision Record editable by Ontology Admin/SysAdmin (known limit), BFF identities' SysAdmin role detail. The line: identifiers are fine in public; "X can still write Y today" for an open gap is not.

## My own backlog
#2627, #2621, #2585, #2576, #2524, gate null-column hardening, SharePoint census, #2543, K-P1 for task 191, communications by-regarding timeout → 500 (maybe 503; not filed); parked: 193 (C:/wt193), 197, #2427, 196a/196b; TASK-INDEX reconcile at 090. Unknown: a pre-compaction background agent ("Update-path existing-only fix") finished with empty output.

## Peers
uac-r2 `-20` (this session) · ebilling `-c7` · field-mapping `-69` · ontology `-38` · provisioning `-b8` · word-add-in `-7b` · mcp-bff-api `spaarke-wt-spaarkeai-mcp-bff-api-r1-68` · website `spaarke-website-cb` · SPA R4 **`spa-r4`** (since 11-08; the old `-ad` session closed; it has the #2578 handoff; send dev BFF before/after notices and gate results there).
