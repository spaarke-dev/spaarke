# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas". History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-11-25 ~23:30Z. Fix wave 136–138 is DONE and live. Two follow-ups from the owner's dev check are in flight: **task 139** (implementer running) and **task 140 = ISS-043** (being specified). This project runs in the **Claude Desktop session `word-add-in-r1`**.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task 139** | IN PROGRESS (FULL, sonnet/high). POML `tasks/139-pane-startup-item-errors.poml`; TASK-INDEX row 🔲. Worktree `C:\wt139`, branch `fix/word-addin-139` from origin/master c3a42ad85a (NOT pushed). An **implementer agent is running** there; brief: show adapter errors' real message (adapter throws plain `{code,message}` objects), Retry instead of Sign in, compute `isAuthenticated` independently of the item read (today a signed-in user whose item read fails gets the Sign in screen), NO_ITEM_SELECTED → "Select an email", Outlook `ItemChanged` handler remounts `<App>` (re-init adapter if it caches mode), no auth re-init. Fail-before jest tests in new files. |
| **Task 140 = ISS-043 (#3194)** | Owner 2026-11-25: *"ISS-034 should be fixed here; and yes confirm the suggestion"*. **Read as ISS-043** (ISS-034 is the unrelated code-page App Insights key issue); the owner was told and can correct it. Decision: an Office save to the record a Suggested communication already points at CONFIRMS it (status Resolved). Design so far: (1) server, `Services/Office/ReconciledEmailFiling.cs` (ours, task 121): when the communication is already filed, not Resolved, and its primary regarding IS the save's record, then after the caller's Write check on the communication (same `CallerRecordAccessProbe` path, ADR-008 A1 instance) write `sprk_associationstatus` = Resolved, the same plain host-field write the review panel's Confirm makes (`advanceAssociationStatus` in `Spaarke.Communication.Components/src/logic/connections/ConnectionsWriteHandler.ts`). Never move a filing to another record. Check whether the unfiled-then-filed path should also end Resolved (verify what `AssociationStatusMapper` gives an explicit rung-0 regarding). Report it in `SaveResponse.EmailFiling`. (2) Client: `derivePrimaryReview` (`.../logic/connections/provenance.ts`) shows "No confident match" for a Suggested row at 0.99 whose regarding is written; diagnose from the dev row's provenance (fetch it: `sprk_communications` filtered by `contains(sprk_subject,'MAT-000113 - New Matter Create')`, select `sprk_associationprovenance,sprk_associationstatus,sprk_regardingrecordid`; there is NO `sprk_regardingrecordtype` column) and show that record as "needs confirmation". The panel ships in code pages (the communication reconciliation web resource), so a code-page deploy is needed: owner go. The Needs Review feed excludes Resolved (`CommunicationQueueFeedService.cs:191`). Tell uac-r2 about the new write for review. Likely opus/high, BFF + shared UI, own worktree `C:\wt140`; conflict-check (BFF hot path). |
| **Next Action** | Fix the provenance query (drop `sprk_regardingrecordtype`) → write POML 140 + TASK-INDEX row + a Decisions Made row in project CLAUDE.md (owner decision above) → update ISS-043 in `notes/defer-issues.md` and comment on #3194 ("taken here as task 140") → run 140 via task-execute (implementer in `C:\wt140`). On the 139 report: verify the diff and gates, one adversarial review, code-review/adr-check, push, PR, owner merge go, add-in deploy, owner re-check in Outlook on the web. Then 090. |
| **Owner questions open** | (1) The pane message for ebilling's 413 `too_large` (now live on dev, unreachable at the default 25 MB)? A small separate PR. (2) Confirm ISS-043 was meant (not ISS-034). |
| **Done today** | #3134 (137), #3139 (136), #3157 (138, ADR-008 A1) merged; dev BFF **aa933c09e6** LIVE 21:49:11Z (uac-r2 gate PASS 22:20Z; rollback a1b7db1def); add-in deploy run 45689178193. Owner live checks: 137 PASS, 138 PASS; 136 no 136 defect. 136–138 CLOSED (352f4a6f5e). ISS-043 filed (#3194). fm added guard G7 (planner read-only) on their branch, for their task 035 PR. |
| **Worktrees** | `C:\wt139` (active). After use, remove with Remove-WorktreeSafe.ps1: `C:\wt136`, `C:\wt137`, `C:\wt138`, `C:\wt139`. `C:\wt137pub` needs manual deletion (tell the owner). |

## Still with the owner
- Test-diet deletions (`notes/test-diet-report.md`); #996 blocking gate (ruleset choice); 125/135 checks; ISS-001 has no GitHub issue; F-g leftover `event` entry in EntityAccessFilter; task 065 leftover checks.
- Mention: three stray python processes spinning since 11/23–24 (PIDs 41460, 84288, 43772); quality alerts #1821 and #1906 (do not fix).
- Prod Entra: the add-in's `brk-…://addins.spaarke.com` redirect on prod `1958aec2`. Teams admin: reject the duplicate "Spaarke portal" submission `5d88b948…` (keep `60055573…`). Excel/PowerPoint add-in (no go yet).

## Word Email tab: templates + AI draft (ontology D-220, owner ruling 2026-11-12)
- Ontology task 189 builds `GET /api/communications/templates`. Then this project adds the pane side as a new task. Trigger: ontology sends the master SHA + contract.

## Submit Invoice
- **Slice C (needs the owner's explicit go):** remove Invoice from Save and "+ New" in both hosts; tell the owner Word loses Invoice save; then tell ebilling for 096.
- ebilling task 123 upload contract agreed (`projects/ebilling-r1/notes/123-upload-contract.md` on `task/ebilling-123`); the pane's supporting-files choice stays unwired until 123 merges.

## Peers (SendMessage by session name)
- `uac-r2`: dev BFF deploys (owner go per SHA); review of task 140's new confirm write.
- `field-mapping-r1`: G7 guard in their task 035 PR. `ebilling-r1`: #3155 live. `ontology-r1`: task 189 templates route.
