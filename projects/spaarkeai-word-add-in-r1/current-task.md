# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas" (incl. "Multi-customer add-in (Model 1)" and "Guest sign-in, PCFs and telemetry" blocks). History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-10-11 (tasks 128 + Reconciliation page fix deployed to dev; task 129 being implemented).

## ⚡ Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | 129 (filed email visible on the record's Messages panel) — an Opus implementer subagent is building `IThreadResolver.ReconcileThreadForFilingAsync` per `notes/129-filed-email-messages.md`, adapted to #1639's join-time grant audience. Owner-confirmed rules: first filing anchors the thread; a re-filed email that is the only filed email in its thread takes the thread with it; a mixed thread / master thread → only the email moves to the new record's Tier-2 default thread. |
| **Status** | 128 done in code + deployed, awaiting owner retest. 129 implementation running (uncommitted BFF edits in this worktree). |
| **Next Action** | When the implementer reports: review the diff, run 2 adversarial-verifier passes (security-adjacent: grants), measure BFF publish size (§10), commit, then ask the owner for the dev BFF deploy go and write/dry-run the backfill (`scripts/Backfill-ThreadFiling.ps1` + `sprk_communicationthread` in `Backfill-RecordOwnership.ps1`). Also send UAC-r2 the T126 message (Q3: `Set-ExternalFlagForB2BGuests.ps1` must exclude the customer workforce tenants before any Model-1 `-Apply`; Q2: provisioning customer record is canonical; Q4: Events type=Task carry documents, OOB task activity unused) — first confirm how a B2B guest's home tenant appears at sign-in (`tid` vs `idp`). |

### Unpushed / uncommitted
- Task 129 BFF edits (implementer, in progress). Branch otherwise pushed at `a70019e3e`; no PR open.

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
- Repo copy of the Matter form loses the 6 lines at the next SpaarkeMaster release export (not hand-edited).
- 090 wrap-up (`/test-diet`) after full UAT; dev package rename "Spaarke (Dev)"; AppStore publishing later.
- Filed follow-ups (other owners): #1464, #1468, #1469, #1470, #1471, #1476, #1477, #1505, #1540, #1551, #1591; #1034 status comment.

## Main checkout
`C:\code_files\spaarke` may be on another branch with another session's changes — leave it (memory). Deploy worktree `C:\wt127` (detached at `b69b85a95`) is this session's own and can be removed.
