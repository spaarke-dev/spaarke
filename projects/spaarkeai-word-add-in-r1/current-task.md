# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas". History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-11-25 ~16:05Z. Fix wave 136–138 (owner "Fix them here first", before closing 090): 136 + 137 MERGED and the add-in is live; 138 is in its last fix round; ADR-008 **Amendment A1** (Path B) wording approved by the owner. This project runs in the **Claude Desktop session `word-add-in-r1`**.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **NOW** | Task **138** (#2209, email re-save adds missed attachments), worktree `C:\wt138`, branch `fix/word-addin-138`. Pushed to 1f76e7cc06; local 3f0bedabfc (round 1b) is NOT pushed. Round 1c implementer agent `a5361c93a9cc46a5e` running (sonnet): G `attachmentsNotStored` + pane text; H1 "Adding N attachment(s)… save the email again if they don't appear"; H2 no add while the matched job is unprocessed AND younger than the abandoned age; J design note; plus (fm's ask) `world.Jobs.Should().ContainSingle()` in the two deny tests of `OfficeEmailReSaveFailClosedTests`. Brief: session scratchpad `138-fix1-brief.md` (H superseded by H1/H2). |
| **Next Action** | When round 1c reports: verify commits + gates (Office suite, ArchTests, office-addins jest/typecheck/lint/webpack, publish delta vs 0267ee64a2, vulnerable packages) → in C:\wt138 add **ADR-008 A1** (text: `notes/138-adr-008-amendment-draft.md`, Parts 1–3: full + concise ADR, the `Api/Office/OfficeEndpoints.cs` GovernedFile Reason sentence in `RouteAuthorizationGuardTests.Ledger.cs`, the OfficeEndpoints comment, the 138 design note "resolved by A1", a spec.md ADR Tensions row) → merge origin/master into the branch → push → send the diff to field-mapping-r1 (four conditions) and the final SHA + A1 text to uac-r2 → open the 138 PR → the owner's merge go. Then close 136–138 (POML status + TASK-INDEX), then close 090. |
| **Owner question open** | Do the 136/137 server changes go in the NEXT dev BFF deploy, or wait for 138 so all three go out in one? Either way the owner's go names the SHA in uac-r2's session. |
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
