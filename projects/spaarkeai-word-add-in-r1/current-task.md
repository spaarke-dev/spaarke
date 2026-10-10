# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas" (incl. "Multi-customer add-in (Model 1)" and "Guest sign-in, PCFs and telemetry" blocks). History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-10-10 (task 127 rollout complete in dev).

## ⚡ Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | None active. Task 127 is rolled out to dev (all 4 steps; record in `notes/127-client-telemetry.md` "Rollout record"). |
| **Status** | Waiting on owner live checks (below). Task 126 blocked on UAC-r2. |
| **Next Action** | Wait for the owner's UAT results and fix what they find. If the Matter footer still shows v1.1.84 after a hard refresh, the next step is an owner-approved publish that moves `customcontrols.version` (see #1591 / project CLAUDE.md Deploy). |

### Unpushed / uncommitted
- None after the docs PR for this checkpoint merges.

## Live in dev (2026-10-08/10)
- Guest sign-in fix (#1453, tasks 122-125): @spaarke/auth + 10 PCFs + 8 code pages + 3 web resources.
- Task 121 (pane Message-ID, per-caller duplicate check, file-if-unfiled): #1495, add-in + BFF.
- Task 127 (#1537): BFF `cc9fd0809`; code pages DailyBriefing/EmailPage/CommunicationReconciliation from `9c04c4320`; SemanticSearchControl **1.1.85** + VisualHost **1.4.40** from `f022544d6`; Matter main form static `appInsightsKey` + `tenantId` removed, `sprk_matter` published.

## Waiting on the owner (live checks)
- **Task 127**:
  - Hard-refresh a Matter record. The Documents grid footer must read `v1.1.85`, and the console must show no `[AppInsightsService]` warning.
  - The visuals (VisualHost) render as before.
  - The dev App Insights resource receives `customEvents` such as `view_toggled` and `card_rendered`.
  - Daily Briefing, the Email page and Communication Reconciliation open and behave as before.
- **Guest + member UAT** for the sign-in fix (list given 2026-10-09).
- **Task 121 live checks**:
  - Message-ID header, and the link to an existing communication.
  - An unfiled communication gets filed.
  - "Related to" cards.
  - Reopening shows saved.
  - A pre-121 email still shows saved.
  - A second user gets their own document.
- **078**: one Word-on-the-web run + desktop Word build number. **080**: Test User 1 opens a matter-filed document they saved + Run Index / document To Do.

## Blocked / waiting on others
- **126** (Model 1 guests count as internal): blocked on UAC-r2's answers to `notes/coordination/2026-10-08-to-uac-r2-model1-guests.md`. Fold in provisioning's **T255** (customer workforce tenant list + acct claim).
- **customer-provisioning-orchestration-r1**: directory endpoint (240c) → runtime customer selection + first production deploy. Diagnostics view stays on dev until their guest tests finish.
- **UAC-r2**: To Do access-permission column; full UAT (rounds 5-12) after their secure project.
- **#1591** (`Import-SolutionScoped.ps1` false success when pac exits 0 on a failed import): filed for the script's owners.

## Open owner items / later
- Optional: reassign the 3 pre-task-146 app-owned dev events (#1034 comment). Dataverse write, needs owner go.
- SpeDocumentViewer 1.0.29, CommunicationConnections 1.7.1, RegardingResolver 1.6.2 (external-service-usage=true) deploy at their next deploy.
- Repo copy of the Matter form loses the 6 lines at the next SpaarkeMaster release export (not hand-edited).
- 090 wrap-up (`/test-diet`) after full UAT; dev package rename "Spaarke (Dev)"; AppStore publishing later.
- Filed follow-ups (other owners): #1464, #1468, #1469, #1470, #1471, #1476, #1477, #1505, #1540, #1551, #1591; #1034 status comment.

## Main checkout
`C:\code_files\spaarke` may be on another branch with another session's changes — leave it (memory). Deploy worktree `C:\wt127` (detached at `f022544d6`) is this session's own and can be removed.
