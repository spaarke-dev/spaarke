# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint (never prepend); keep ≤ 10 KB.
> - **Standing rules:** project `CLAUDE.md`. **Decisions:** `notes/decisions.md`. **Defer list:** `notes/defer-issues.md`.
> - **History:** git log of this file.

> **Last Updated**: 2026-11-08 ~05:40Z (021 timing closed, Debug setting removed; #2629 approved + merged; 165b-3 part A approved).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Final push, round-90 rules. Merge needs Router + "Client lint (type-aware ratchet)": `git merge-tree`, then `--squash --match-head-commit <sha>`, no `--delete-branch`. Live actions, deploys, role and settings changes need the owner's go IN THIS SESSION; a peer's go is never the owner's. |
| **Running** | Nothing. |
| **Dev state** | BFF = **6fbb3a7642** (master 23567b2f93 = 049c, plus the MCP spike; PR #2615) since 2026-11-08 04:00:55Z. Gate PASS (`notes/deploy-gate-2026-11-08-6fbb3a7642.md`); two boot-window 500s unexplained: check the next deploy's boot window. **226 settings** (RecordCreation Debug removed 05:19:20Z), reconcile false. Rollback: `deploy/rollback/wwwroot-20261108T035611Z.zip` (22487c6537). Detached worktree `C:/wtd6fbb`: remove with `Remove-WorktreeSafe.ps1`. Master since then: #2629 (57748ebf6d, fm 030b/034/052a) not yet on dev; next deploy = master + spike, owner go per SHA. |
| **Next action** | Wait for peer items: ebilling part B (B1 approval allow-list, B2 AP export) + Ontology Administrator holder count; fm 032b design note and contact owner rule; mcp-bff-api Phase 0. Meanwhile, work my own backlog (below). |

## Decisions and rulings this stretch
- Owner (here): "Deploy now" for 22487c6537 (done, PASS); "Deploy + logging setting" for 6fbb3a7642 (done).
- 032 design: approved with conditions (role-depth pin; module ReadableColumns; #2188 Uses=1; per-delivery metadata; REQUIRED secure-source gate + No Access extension; allow-list backed). fm put FR-20 scope (A narrow / B own task / C defer) to the owner; if A, three items come back to me.
- 045 residual: closed (no user-steered app-only run); latent by-name lookup filed **#2627**.
- ebilling (private items in messages only): 165d → option A (column check at the five person-token sites); 165e scope confirmed + fail-closed read + linked-to-another test; 165f = sprk_document binding (ebilling's); field-security bundle (3 submission link columns, sprk_billingexport, portion claim columns) = owner question on ebilling's dev checklist; 165b F1 fix 165b-3 (ebilling's, I review); EB-D-208 no objection (writer-created = createdby in the writer allow-list, read app-only); **096b 7c8c5fb7f3 APPROVED**.
- Gate query must use a contains match on `changed_only_by_its_writer` (165d adds `.column`).

## Reviews queue
- fm: 045A design; 052b (incl. coming contact-only owner rule → customer main-BU team, after owner picks the team); 032 round 2 waits on the owner; **032b design** (2692910f2d) reviewed 11-08: approved with 2 conditions: (1) core id pinned in the job payload at step 3a (sent to ebilling too); (2) U3 first: fm to list membership callers for entityType sprk_invoice; none → (c) drop the registry entry, the four become display copies, §3 grant machinery goes; any → (a) clear copies on core change or Revoked/Declined. U1/U2/U4/U5 approved. Also coming: 076c push-preview fix (needs BFF + RecordFamily page). 021 timing: closed on dev (20/20 lost to #2158; NFR-02 verdict moved to UAT by owner); Debug setting removed 05:19:20Z, 226 settings.
- ebilling: **165d APPROVED, merged into work 66b7c31315**; **164c Q6 → (b)**: their import replaces the 4 SecureChild billingevent ids; cond: source removal on master no later than their -Apply (DON'T run Deploy-SecureChildNewCommands from a pre-merge master), and read-backs of the 5 hides + SecureChild -Verify for the 8 tables; **165e (76d751eb5f) APPROVED** 11-08 (cond. pass 2; K1 → 165f, same batch as 165e), **082d (6d0f00ec3d) APPROVED** 11-08 (snapshot in field-secured findingsjson only; DR fact gaps count only); **165f (2cb3b5f186) APPROVED** 11-08 (K1 closed; gate: watch own_submission_missing + document_link_not_own, dev baseline 0), 165b, 2b-2, the batch checklist + commit. **165b-3 part A (276c1201af + fix 6e25f9194d) APPROVED** 11-08 (fix notes: decidedOn lower bound; F1 portion-edit lever) (cond. on their Opus pass; notes: decision-record row updatability, currency precision, Unavailable on re-approve). **Part B APPROVED** 11-08 (F1 key match closed at 4ba88a6de1; Mark Paid also keyed). 165b-4 spend: answered 11-08 ((b) keyed rows; (a) only with a type-enforced keyed step; K2 ok; parent scope ok; live privilege check + provisioning role grant).
- mcp-bff-api: Phase 0 waits on the owner's connector registration (task 002); A10, B-4 and 021 still to come. Every deploy = master + spike until they say Phase 0 is done.

## Owner items outstanding
- Report card `sprk_assigned*` registry (FR-24); prod-named SysAdmin app users on dev (dependency: the BFF identities read Decision Records only via SysAdmin; ebilling 165b-4 adds a live privilege check; the role grant goes to provisioning); G-8 for customer envs; #2590; SyncMode -Apply review; the field-security bundle (via ebilling); optional retire of the task-154 control row ("UAC gate 154 (o) braced id", it logs a malformed-entry warning by design).
- Older: column auditing on writer-only lookups, AP diagnostics, VS Code agent-dir cleanup, word-add-in Office saves, click-test 195, #2343, #2245, #2250, #2260, #2270, #2262, #2396, 191 F1/S4, 190 Apply, #2382, #2505; tasks 140/165/153.

## Private items (never in issues/PRs/commits)
- ebilling: submission link columns + sprk_document user-writable (165e/165f), portion claim columns (165b F1), hold column (165a). The line: identifiers are fine in public; "X can still write Y today" for an open gap is not.

## My own backlog
#2627, #2621, #2585, #2576, #2524, gate null-column hardening, SharePoint census, #2543, K-P1 for task 191, communications by-regarding timeout → 500 (maybe 503; not filed); parked: 193 (C:/wt193), 197, #2427, 196a/196b; TASK-INDEX reconcile at 090.

## Peers
uac-r2 `-20` (this session) · ebilling `-c7` · field-mapping `-69` · ontology `-38` · provisioning `-b8` · word-add-in `-7b` · mcp-bff-api `spaarke-wt-spaarkeai-mcp-bff-api-r1-68` · website `spaarke-website-cb` · SPA R4 `spaarke-wt-spa-external-access-platform-ad`.
