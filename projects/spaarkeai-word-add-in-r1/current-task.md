# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas". History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-11-25 ~18:30Z. Fix wave 136–138 (owner "Fix them here first", before closing 090): 136 + 137 MERGED and the add-in is live; **138 is PR #3157, waiting on CI and the owner's merge go**. ADR-008 **Amendment A1** (Path B, owner-approved) is in that PR. This project runs in the **Claude Desktop session `word-add-in-r1`**.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **NOW** | Task **138** = PR **#3157** (https://github.com/spaarke-dev/spaarke/pull/3157), branch `fix/word-addin-138`, worktree `C:\wt138`. Pushed: local == remote. Commits: round 1c 0720ae5ae5, A1 + pane wording 47d1e68a52, master merge 583d171015, async contract-test send mock (final). CI was running when this was written; only `Router` is required. Gates are in the PR body (scratchpad `138-pr.md`). The "slowdown" was machine load, not code: host startup and first-save times matched master test by test. |
| **Peers on 138** | uac-r2: **no objection on access grounds** (code read only; check position unchanged since 1f76e7cc06; A1 text in both ADR files carries all four corrections; ledger change is Reason text only). field-mapping-r1: **all four conditions PASS at 3bb88235bc** (own fresh ArchTests 2823/2823). Their K-class notes are in the PR's Known limits: (c) children take the record's current owner, not the parent's; (a) the planner is not in their create-guard scan, which they may add; (b)/(d) test hardening. **Auto-merge (`--merge`) enabled on #3157 at 3bb88235bc** (owner go "merge when green"); ebilling's #3155 (6a968b2a00, 413 too_large) is also on master and will ride the same dev deploy. |
| **Next Action** | Owner's live-check results for 136–138 (on aa933c09e6) → any failure: tell uac-r2 before the ~22:20Z gate → close 136–138 (POML status + TASK-INDEX) → close 090. Then remove `C:\wt136`, `C:\wt137`, `C:\wt138` with Remove-WorktreeSafe.ps1. |
| **Owner questions open** | (1) Merge go on #3157 GIVEN ("merge when green", conditional on CI + fm's four-conditions reply; fm is checking). (2) ANSWERED 2026-11-25: **one dev BFF deploy for 136+137+138, after #3157 merges** (owner). The owner still names that master SHA in uac-r2's session. (3) Add a pane message for ebilling's 413 `ebilling.submission.too_large` (unreachable at the default 25 MB; today the generic sentence)? Small separate PR. |
| **Merged today** | **#3134** task 137 (merge 6331e17e22); **#3139** task 136 (merge 92c6746133). Both on the owner's go, `--merge`, branches kept. Add-in deploy run 45645305698 on 92c6746133 succeeded: the 136/137 PANE changes are live on dev; their SERVER changes are not. |
| **#3157 MERGED** | 2026-11-25 19:50:43Z, merge **aa933c09e6** (auto-merge on the owner's go). Add-in deploy run 45689178193 on aa933c09e6: success (138 pane live on dev). |
| **Live dev BFF** | **aa933c09e6, LIVE 2026-11-25 21:49:11Z** (uac-r2; 30-min gate ~22:20Z; rollback a1b7db1def). Owner live checks for 136–138 sent to the owner; awaiting results. |
| **Deploy record** | **aa933c09e6** (owner go in uac-r2's session; rollback a1b7db1def). Carries 136+137+138 server changes + ebilling #3155. Range a1b7db1def..aa933c09e6 (89 commits) checked and sent to uac-r2: Office server = ours only; Finance = ebilling 413 too_large + per-file limit + unused CoreOf removal + queue duplicate-detection guard; others (Signals/MCP perf, Messages-list notice, fm comment, provisioning seed) touch no add-in route. **Waiting for uac-r2's LIVE notice**, then live checks for 136–138 with the owner. |
| **Previous dev BFF** | a1b7db1def, live 2026-11-25 15:58:53Z (rollback ef0c566d2c). |
| **Known limit (not filed)** | A caller who lost access gets the duplicate answer for their own earlier save: their own job id only; the job route checks ownership. Listed in the PR. |
| **Worktrees to remove after 138** | `C:\wt136`, `C:\wt137`, `C:\wt138` via `scripts/worktree/Remove-WorktreeSafe.ps1`. `C:\wt137pub` needs manual deletion (tell the owner). `C:\wt138m` is already removed. |

## Task 138 facts (binding)
- fm's four conditions: children only via `ProcessSingleAttachmentAsync`; fm guards G1/G3/G5/G6 green; full ArchTests green; the caller's current rights re-checked, failing closed.
- uac-r2 accepted F1 (the .eml must still be filed to the request's record, both link families) and F2 (Write on the .eml via `AuthorizeDocumentsAsync` before any read) at 1f76e7cc06, and confirmed the final SHA does not change them.
- Out of scope, filed: #3135 two same-named attachments overwrite each other (fm's worker).

## Still with the owner
- Test-diet deletions (`notes/test-diet-report.md`); #996 blocking gate (ruleset choice); 125/135 checks; ISS-001 has no GitHub issue; F-g leftover `event` entry in EntityAccessFilter; task 065 leftover checks.
- Mention: three stray python processes spinning since 11/23–24 (PIDs 41460, 84288, 43772); quality alerts #1821 and #1906 (do not fix).
- Prod Entra: the add-in's `brk-…://addins.spaarke.com` redirect on prod `1958aec2`. Teams admin: reject the duplicate "Spaarke portal" submission `5d88b948…` (keep `60055573…`). Excel/PowerPoint add-in (no go yet).

## Word Email tab: templates + AI draft (ontology D-220, owner ruling 2026-11-12)
- Ontology task 189 builds `GET /api/communications/templates` (impersonated caller read, uac-r2-reviewed). Then this project adds the pane side as a new task: EmailView passes `onDraftWithAi`, `onListEmailTemplates`, `onRenderEmailTemplate`; `.d.ts` subset; tests; owner narrow-width check. Trigger: ontology sends the master SHA + contract.

## Submit Invoice
- **Slice C (needs the owner's explicit go):** remove Invoice from Save and "+ New" in both hosts; tell the owner Word loses Invoice save; then tell ebilling for 096.
- ebilling task 123 upload contract agreed (final table `projects/ebilling-r1/notes/123-upload-contract.md` on `task/ebilling-123`); the pane's supporting-files choice stays unwired until 123 merges.
- ebilling's 413 `too_large`: OK'd; recorded at their 1bea5ffc58, reaches master with their batch 4 PR (they notify on merge).

## Recent deploy checks (newest first)
- 2026-11-25 a1b7db1def (live 15:58:53Z): see "Live dev BFF" above.
- 2026-11-24 996928732e: Office server = ebilling 096 only (invoice save/quick-create 400 OFFICE_023, picker canFile=false); fm 056 private messages never reach the add-in. No add-in concern.

## Peers (SendMessage by session name)
- `uac-r2`: dev BFF deploys (owner go per SHA); 138 cleared on access grounds.
- `field-mapping-r1`: 138 final diff sent (four conditions), reply pending.
- `ebilling-r1`: task 123 upload contract; batch 4 PR notice. `ontology-r1`: task 189 templates route.
