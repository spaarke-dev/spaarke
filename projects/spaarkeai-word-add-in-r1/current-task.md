# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas" (incl. "Multi-customer add-in (Model 1)" and "Guest sign-in, PCFs and telemetry" blocks). History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-10-09 (by context-handoff, before /compact).

## ⚡ Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **127 rollout** (`tasks/127-client-telemetry-per-environment.poml`, done in code — PR #1552 merged `cc9fd0809`) — owner-approved 4-step dev rollout. Step 1 (BFF) ✅. Steps 2-4 wait for **PR #1580** (external-service-usage alignment). |
| **Status** | #1580 created via REST (GraphQL was rate-limited); background watcher `brbls8bmq` polls `Router` on head `5ae149b2` and REST-merges it (`merge_method=merge`). Check: `gh api repos/spaarke-dev/spaarke/pulls/1580 --jq .merged`. |
| **Next Action** | 1) Confirm #1580 merged (if the watcher failed: re-check Router, merge via `gh pr merge 1580 --merge` or REST). 2) Run 127 steps 2-4 from a fresh short-path worktree of origin/master (after #1580), per `notes/127-client-telemetry.md` and `/pcf-deploy`: **(2)** PCFs SemanticSearchControl 1.1.84→**1.1.85**, VisualHost 1.4.39→**1.4.40** (preflight: new > dev in all 5 locations; post-import `customcontrol.version`); **(3)** code pages DailyBriefing (`sprk_dailyupdate`), EmailPage (`sprk_emailpage`), CommunicationReconciliation (`sprk_communicationreconciliation`) — check dev `modifiedon` vs master first; **(4)** Matter main form `4fa382f2-c273-f011-b4cb-6045bdd6a665`: back up formxml, remove `appInsightsKey` + `tenantId` from the SemanticSearchControl binding `{535b5bb1-2a4e-4fb4-87aa-be8351b15fb2}` (3 form factors, 6 lines), publish `sprk_matter`. Then tell the owner what to check (Documents grid telemetry, no regressions). 3) Record in TASK-INDEX 127 + docs PR. |

### Unpushed / uncommitted
- `projects/spaarkeai-word-add-in-r1/CLAUDE.md` (new standing bullets) + this file — commit LOCALLY; **push only after #1580 has merged** (a push changes the PR head and breaks the pending REST merge).

## Live in dev (2026-10-08/09)
- Guest sign-in fix (#1453, tasks 122-125): @spaarke/auth + 10 PCFs + 8 code pages + 3 web resources deployed and version-verified; SemanticSearchControl **1.1.84** + `sprk_documentrelationshipviewer` deployed after #1415 merged.
- Task 121 (pane Message-ID, per-caller duplicate check, file-if-unfiled): #1495 `81e6bbfbb`, add-in + BFF deployed.
- Task 127 BFF: deployed from `cc9fd0809`; `/api/config/client` returns `appInsightsConnectionString` (4 standard parts only).

## Waiting on the owner (live checks)
- **Guest + member UAT** for the sign-in fix (list given 2026-10-09: Document preview/related/ribbon/Find Similar, Matter tracking/Email & Messages/Documents grid, Communication record, Email page, Daily Briefing, upload wizard; member regression + SPE Admin, Playbook Builder, AI chat ribbon).
- **Task 121 live checks**: Message-ID header + link to existing communication; unfiled communication gets filed; "Related to" cards; reopen shows saved; pre-121 email still saved; second user gets own document.
- **078**: one Word-on-the-web run + desktop Word build number. **080**: Test User 1 opens a matter-filed document they saved + Run Index / document To Do.

## Blocked / waiting on others
- **126** (Model 1 guests count as internal — external flag script, WorkforceIdentity member test, contact binding): blocked on UAC-r2's answers to `notes/coordination/2026-10-08-to-uac-r2-model1-guests.md` (owner shared it). Provisioning merged **T255** (customer workforce tenant list + acct claim) — fold into 126.
- **customer-provisioning-orchestration-r1**: directory endpoint (240c) → runtime customer selection + first production deploy. Diagnostics view stays on dev until their guest tests finish.
- **UAC-r2**: To Do access-permission column (their task); full UAT (rounds 5-12) after their secure project. Told via PR: task 121 renamed two entries in `RecordOwnerAssignmentCensusTests.cs`.

## Open owner items / later
- Optional: reassign the 3 pre-task-146 app-owned dev events (#1034 comment) — Dataverse write, owner go.
- SpeDocumentViewer 1.0.29, CommunicationConnections 1.7.1, RegardingResolver 1.6.2 (external-service-usage=true) deploy at their next deploy.
- 090 wrap-up (`/test-diet`) after full UAT; dev package rename "Spaarke (Dev)"; AppStore publishing later.
- Filed follow-ups (other owners): #1464, #1468, #1469, #1470, #1471, #1476, #1477, #1505, #1540, #1551; #1034 status comment.

## Main checkout
`C:\code_files\spaarke` may be on another branch with another session's changes — leave it (memory).
