# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas" (incl. "Multi-customer add-in (Model 1)" and "Guest sign-in, PCFs and telemetry" blocks). History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-10-13 (ADR-028 A9 recorded; task 126 re-scoped and unblocked; 128/129 on master; dev BFF on master `99ec749cd`).

## ⚡ Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **126**, re-scoped 2026-10-13 under ADR-028 A9: only the external-flag script part remains. 🔲 open, no longer blocked. Step 1 is owner-gated: pick the rule for "this guest is the stamp's customer staff", option (a) domain lookup, (b) recorded set, or (c) every guest in a Model 1 stamp is internal plus an exception list. See the POML. |
| **Status** | **Sign-in decided (ADR-028 A9, owner 2026-10-13, PR #1810):**<br>- Licensed core users are B2B guests (A8).<br>- Workforce contacts use their company account in the browser SPA only, not guests.<br>- Partners use CIAM.<br>- SPA-r3 owns workforce sign-in end to end, UAC-r2 the baseline role, provisioning the registrations and consent.<br>**For this project:**<br>- The add-in changes nothing (licensed users only, on A8). See the project `CLAUDE.md` "Multi-customer add-in (Model 1)".<br>- The 2026-10-12 home-directory scheme is shelved on the local branch `wip/t126-workforce-customer` (unpushed, never deployed); keep it for reference only.<br>**Done:**<br>- 128 and 129 ✅, on master via #1796 (`99ec749cd`). Dev BFF live at `1.0.0+99ec749cd`.<br>- #1799 (mapped `upn` claim) merged; it reaches dev with the next BFF deploy from master. |
| **Next Action** | 1. Owner picks the task 126 Step 1 rule, then run task-execute on 126. Never `-Apply` the external-flag script on a Model 1 stamp before it lands.<br>2. Owner decision: the add-in's "Link another record" list shows sprk_communication / sprk_analysis / sprk_document / sprk_reportcard by internal name. communication / document / report card have no thread lookup, so an email linked only to them shows on no Messages panel. Recommendation: remove those three.<br>3. Owner decision: licensing (multiplexing) for workforce contacts and partners. Recommendation: a Microsoft licensing specialist before the first production customer. Research: `.claude/agent-memory/researcher/pp-licensing-multiplexing-isv-workforce-partners-2026-10-13.md`. |

### Unpushed / uncommitted
- Local only: branch `wip/t126-workforce-customer` (shelved). Worktree `C:\wt1798` (branch `fix/workforce-email-upn-claim`, merged) can be removed.
## Live in dev (2026-10-08/11)
- 2026-10-12 03:12 UTC (evening of 10-11 local): `sprk_communicationreconciliation` (fix: page loads again — `4fb7c8da6`) and `sprk_documentuploadwizard` (task 128 — `db37e6a31`) built from `a70019e3e`, published, read back byte-equal.
- Guest sign-in fix (#1453, tasks 122-125): @spaarke/auth + 10 PCFs + 8 code pages + 3 web resources.
- Task 121 (pane Message-ID, per-caller duplicate check, file-if-unfiled): #1495, add-in + BFF.
- Task 127 (#1537): BFF `cc9fd0809`; code pages DailyBriefing/EmailPage/CommunicationReconciliation from `9c04c4320`; SemanticSearchControl **1.1.85** + VisualHost **1.4.40** from `f022544d6`; Matter main form static `appInsightsKey` + `tenantId` removed, `sprk_matter` published.
- PCF re-sync to master (#1613, built from `b69b85a95`): 14 controls imported and verified. The record is in `notes/pcf-dev-resync-2026-10-10.md`.

## Waiting on the owner (live checks)
- **Task 127**:
  - Hard-refresh a Matter record. The Documents grid footer must read `v1.1.85`, and the console must show no `[AppInsightsService]` warning.
  - The visuals (VisualHost) render as before.
  - The dev App Insights resource receives `customEvents` such as `view_toggled` and `card_rendered`.
  - Daily Briefing, the Email page and Communication Reconciliation open and behave as before.
- **PCF re-sync**:
  - A Matter record shows MatterHeader 1.0.22. A Project, Invoice, Agreement or Work Assignment record shows RecordHeader 1.1.12.
  - The Communication form shows the timeline, conversation panel and actions, and they work as before.
  - The document relationship viewer opens.
- **Guest + member UAT** for the sign-in fix (list given 2026-10-09).
- **A9 live-test item:** a licensed B2B guest signs in to the add-in from their OWN company's desktop Office and Word, with the authority pinned to the Spaarke tenant. Outlook on the web was verified on 2026-10-08. If it fails, raise it with the owner before any workaround.
- **Task 121 live checks**:
  - Message-ID header, and the link to an existing communication.
  - An unfiled communication gets filed.
  - "Related to" cards.
  - Reopening shows saved.
  - A pre-121 email still shows saved.
  - A second user gets their own document.
- **078**: one Word-on-the-web run + desktop Word build number. **080**: Test User 1 opens a matter-filed document they saved + Run Index / document To Do.

## Blocked / waiting on others
- **SPA-r3 (A9):** workforce-contact sign-in, registration, the stored link and the task 040 re-scope. Provisioning: multitenant BFF app plus customer admin consent. The Dewey Cheatham retest waits on SPA-r3's A9 build. Nothing for this project to do.
- **customer-provisioning-orchestration-r1**: directory endpoint (240c) → runtime customer selection + first production deploy. Diagnostics view stays on dev until their guest tests finish.
- **UAC-r2**: To Do access-permission column; full UAT (rounds 5-12) after their secure project.
- **#1591** (`Import-SolutionScoped.ps1` false success when pac exits 0 on a failed import): filed for the script's owners.

## Open owner items / later
- Optional: reassign the 3 pre-task-146 app-owned dev events (#1034 comment). Dataverse write, needs owner go.
- Repo copy of the Matter form loses the 6 lines at the next SpaarkeMaster release export (not hand-edited).
- 090 wrap-up (`/test-diet`) after full UAT; dev package rename "Spaarke (Dev)"; AppStore publishing later.
- Filed follow-ups (other owners): #1464, #1468, #1469, #1470, #1471, #1476, #1477, #1505, #1540, #1551, #1591; #1034 status comment.

## Main checkout
`C:\code_files\spaarke` may be on another branch with another session's changes — leave it (memory). Deploy worktree `C:\wt127` (detached at `b69b85a95`) is this session's own and can be removed.
