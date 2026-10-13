# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas" (incl. "Multi-customer add-in (Model 1)" and "Guest sign-in, PCFs and telemetry" blocks). History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-10-13 (128/129 merged to master via #1796 at `99ec749cd`; dev BFF redeployed from master; T126 option 1 dropped for ADR-028 A8).

## ⚡ Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | 126 (Model 1 guests count as internal) — blocked. 128/129 done and on master (#1796, `99ec749cd`). 129 history: `IThreadResolver.ReconcileThreadForFilingAsync` (commits `48a2f8687`, `09397337f`, `cb9e10aac`), empty-thread retirement + backfill job `communication-thread-filing-sweep` (`fb9e0cdb3`), sweep review fixes (`46687a851`). Reviews: security, behaviour, re-check, sweep adversarial — no open F-class. BFF publish +25,916 B (36.46 → 36.49 MB), no vulnerable packages. |
| **Status** | 128 ✅ (owner retest passed 2026-10-12). 129 ✅ (owner confirmed 2026-10-12). Dev BFF redeployed 2026-10-13 from master `99ec749cd` (the 10-12 deploy from `f9cc25f73` was based on 10-11 master and had rolled back other projects' work — restored). 129 first deployed 2026-10-12 from `f9cc25f73` (adds party anchoring: an email re-filed to a contact / account / organization takes its thread there, owner kept). Sweep run: dry run 143 candidates + 26 owner fixes → apply 143 reconciled / 26 assigned / 0 unresolved, refused, failed → verify dry run 0 candidates / 0 wouldAssign. `CommunicationThreadFilingSweep__Apply` removed again; job stays registered disabled. Reports: session scratchpad `sweep-{dryrun,apply,verify}-20261012-*`. PAT-444023 email `b22bb67e` now in active thread `b62bb67e` anchored to matter `5b297187`, owner team "Spaarke Business Unit 1". |
| **Next Action** | Open finding from the owner's screenshot: the add-in "Link another record" picker lists sprk_communication / sprk_analysis / sprk_document / sprk_reportcard by logical name, and sprk_communication / sprk_document / sprk_reportcard have no thread lookup, so an email related only to them shows on no Messages panel — owner to decide. T126: the home-tenant scheme ("option 1") is DROPPED (conflicts with ADR-028 A8; parked unpushed on local branch `wip/t126-workforce-customer`, never deployed) — see `notes/126-message-to-uac-r2.md` "Update 2026-10-13". Dewey retest waits on R3 deploying its task 040 SPA build (owner-gated). #1799 (mapped `upn` claim, closes #1798) open with auto-merge. 126 still blocked on UAC-r2's external-flag answer. |

### Unpushed / uncommitted
- Open PR #1799 (branch `fix/workforce-email-upn-claim`, worktree `C:\wt1798`). Dev BFF verified live at `1.0.0+99ec749cd` (hash-verified, healthz OK) 2026-10-13. Deploy worktree `C:\wt1796d` can be removed.

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
