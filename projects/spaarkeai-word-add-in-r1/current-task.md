# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas". History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-11-25 ~16:05Z. Fix wave 136–138 (owner "Fix them here first", before closing 090): 136 + 137 MERGED and the add-in is live; 138 is in its last fix round; ADR-008 **Amendment A1** (Path B) wording approved by the owner. This project runs in the **Claude Desktop session `word-add-in-r1`**.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **NOW** | Task **138**, `C:\wt138`, branch `fix/word-addin-138`, local HEAD 583d171015 = round 1c 0720ae5ae5 + A1/wording commit 47d1e68a52 + master merge (147 commits, no conflicts). NOT pushed (remote at 1f76e7cc06). **Uncommitted in C:\wt138:** `OfficeEndpointsContractTests.cs` send-mock made async (no `GetAwaiter().GetResult()`); it gained only ~7 s. Gates on 583d171015: build 0 warnings; ArchTests 2823/2823; Office suite 1214 passed / 6 failed (all `OfficeEmailDocumentCommunicationLinkTests`, 2.5-min stream timeouts; 33/33 alone). **Investigating a real slowdown:** that class takes ~1m40s on 138 vs ~1m05s on master 5ca9b84cd4 (C:\wt138m), uniformly +~1.8 s per test, even plain document saves. Bisect running in the background (`scratchpad\bisect138.ps1`, results in `scratchpad\bisect138.txt`; it checks out 0267ee64a2 / 2ccdb6c51c / 1f76e7cc06 / 3f0bedabfc / 0720ae5ae5 in C:\wt138m). Pane: not-stored notice now names the "Save again as a new document" link; 46/46 jest, Prettier/ESLint clean. PR body drafted: `scratchpad\138-pr.md` (GATES_PLACEHOLDER to fill). |
| **Next Action** | Read the bisect → find what adds ~1.8 s per save (prod code or test setup) → fix → re-time the class → full Office suite once more → commit → push → send field-mapping-r1 the diff (four conditions) and uac-r2 the final SHA → open the 138 PR (fill the gates) → owner merge go. Remove `C:\wt138m` after, with Remove-WorktreeSafe.ps1. The duplicate-answer case is NOT filed: it returns only the caller's own job (owner-checked job route); it's listed as a known limit in the PR. Then close 136–138 (POML status + TASK-INDEX), then 090. |
| **Owner questions open** | (1) Do the 136/137 server changes go in the NEXT dev BFF deploy, or wait for 138? Either way the owner's go names the SHA in uac-r2's session. (2) Add the pane message for ebilling's new 413 `ebilling.submission.too_large` (OK'd to ebilling; unreachable at the default 25 MB; today it shows the generic sentence)? Small separate PR. |
| **Merged today** | **#3134** task 137 (merge 6331e17e22); **#3139** task 136 (merge 92c6746133). Both on the owner's go, `--merge`, branches kept. Add-in deploy run 45645305698 on 92c6746133: success, so the 136/137 PANE changes are live on dev. Their SERVER changes are NOT on dev yet. |
| **Live dev BFF** | **a1b7db1def**, live 2026-11-25 15:58:53Z (uac-r2, owner go; rollback ef0c566d2c). Cut before #3134/#3139. Range check 996928732e..a1b7db1def: no Office server change; Finance only (ebilling #3038/#3065/#3073/#3084/#3130: duplicate multipart field → 400, truncated body → 400, `invoiceId` external read only); no add-in concern; sent to uac-r2. Until the next deploy: 137's polling falls back to the old behaviour; 136's pane works against the old server. |
| **ADR-008 A1** | Owner chose Path B (2026-11-25) and approved the wording. Peer reviews applied (no change of meaning): fm (Path A bullet wording; a job row counts as a write); uac-r2 (system-user vs contact check; re-reading the same id allowed; the narrowing helper's app-only step; instances table "(known)" with an "On refusal" column). fm's 028 send-time fill is fm's Path A, NOT A1 (the request names the analysis id). Not yet committed anywhere. |
| **Uncommitted (this worktree)** | `notes/defer-issues.md` (ISS-040 #3135, ISS-041 #3136, ISS-042 #3137); `notes/138-adr-008-amendment-draft.md` (new); this file. |
| **Worktrees to remove after 138** | `C:\wt136`, `C:\wt137`, `C:\wt138` via `scripts/worktree/Remove-WorktreeSafe.ps1`. `C:\wt137pub` needs manual deletion (tell the owner). |

## Task 138 facts (binding)
- fm's four conditions: children only via `ProcessSingleAttachmentAsync`; fm guards G1/G3/G5/G6 green; full ArchTests green; the caller's current rights re-checked, failing closed. fm cleared the earlier diff; they check the final diff.
- uac-r2 accepted F1 (the .eml must still be filed to the request's record, both link families) and F2 (Write on the .eml via `AuthorizeDocumentsAsync` before any read) at 1f76e7cc06; they want the final SHA only to confirm the later rounds don't touch that check.
- Out of scope, filed: #3135 two same-named attachments overwrite each other (fm's worker); known limits recorded in the design note.
- "Duplicate answer for a caller who has lost access returns the old document id" is older behaviour; possibly file it (owner not yet asked).

## Still with the owner
- Test-diet deletions (`notes/test-diet-report.md`); #996 blocking gate (ruleset choice); 125/135 checks; ISS-001 has no GitHub issue; F-g leftover `event` entry in EntityAccessFilter; task 065 leftover checks.
- Mention: three stray python processes spinning since 11/23–24 (PIDs 41460, 84288, 43772); quality alerts #1821 and #1906 (do not fix).
- Prod Entra: the add-in's `brk-…://addins.spaarke.com` redirect on prod `1958aec2`. Teams admin: reject the duplicate "Spaarke portal" submission `5d88b948…` (keep `60055573…`). Excel/PowerPoint add-in (no go yet).

## Word Email tab: templates + AI draft (ontology D-220, owner ruling 2026-11-12)
- Ontology task 189 builds `GET /api/communications/templates` (impersonated caller read, uac-r2-reviewed). Then this project adds the pane side as a new task: EmailView passes `onDraftWithAi`, `onListEmailTemplates`, `onRenderEmailTemplate`; `.d.ts` subset; tests; owner narrow-width check. Trigger: ontology sends the master SHA + contract.

## Submit Invoice
- **Slice C (needs the owner's explicit go):** remove Invoice from Save and "+ New" in both hosts; tell the owner Word loses Invoice save; then tell ebilling for 096.
- ebilling task 123 upload contract agreed (final table `projects/ebilling-r1/notes/123-upload-contract.md` on `task/ebilling-123`); the pane's supporting-files choice stays unwired until 123 merges.

## Recent deploy checks (newest first)
- 2026-11-25 a1b7db1def (live 15:58:53Z): see "Live dev BFF" above.
- 2026-11-24 996928732e: Office server = ebilling 096 only (invoice save/quick-create 400 OFFICE_023, picker canFile=false); fm 056 private messages never reach the add-in. No add-in concern.

## Peers (SendMessage by session name)
- `uac-r2`: dev BFF deploys (owner go per SHA), A1 review, 138 final SHA.
- `field-mapping-r1`: 138 final diff (four conditions); A1 reviewed.
- `ebilling-r1`: task 123 upload contract. `ontology-r1`: task 189 templates route.
