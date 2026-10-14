# Current Task State — `spaarkeai-word-add-in-r1`

> **Format (2026-10-06):** CURRENT state only, REWRITTEN at each checkpoint. Standing directives + gotchas: project `CLAUDE.md` → "Standing directives & gotchas" (incl. "Multi-customer add-in (Model 1)" and "Guest sign-in, PCFs and telemetry" blocks). History: git log + `notes/handoff-history/` (do not load on recovery).

> **Last Updated**: 2026-10-14 03:10 UTC (pre-UAT dev deploy done; owner UAT next).

## ⚡ Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **042 🔄: owner full UAT.** Everything is on dev from master `3084f0ca1`. 126 ✅ (#1815). |
| **Status** | Pre-UAT deploy done 2026-10-14 (owner go 2026-10-13):<br>- BFF `spaarke-bff-dev` from `3084f0ca1`: 36.61 MB, SHA-256 match, `/healthz` 200.<br>- CommunicationConnections PCF 1.7.2: solution 1.7.2, live bundle byte-equal to the build. The `customcontrols` row still reads 1.7.1, filed as #1832.<br>- Code pages `sprk_emailpage`, `sprk_communicationreconciliation`, `sprk_spaarkeai`: built from `3084f0ca1`, #1812 marker present, read back byte-equal (03:00Z).<br>The add-ins are current (`05356cbf7`). |
| **Next Action** | 1. Owner UAT: 042 (full), 080 (Test User 1 opens a matter-filed document they saved, then Index and document To Do), 121 (email save), 127 (grids), 078 (Word on the web). Fix what they report.<br>2. 090 wrap-up (`/test-diet`) after UAT.<br>3. SPA-r3 waits on the owner's Ralph Dewey sign-in retest (https://green-dune-0c4f1221e.7.azurestaticapps.net/); pass the result on. |

### Unpushed / uncommitted
- None on this branch after the PCF 1.7.2 PR merges. Local only: branch `wip/t126-workforce-customer` (shelved). Worktrees `C:\wt1798` and `C:\wt127` are this session's own and can be removed.

## Owner answers given 2026-10-14
- **"Spaarke Office Add In User" role:** not a gate. No code checks it. Who gets the add-in is set in M365 admin center → Integrated Apps. Rights come from per-record checks run as the user, so the role's privileges add to the base role. UAC-r2 owns its shape: their 10-08 edit removed Share, and task 128 added what `/todo` needs. In dev it is held directly by 7 users and through the root BU's default team "Spaarke" (174 members; default-team roles were approved by the owner, UAC-r2 G146-5). The docs that call it required contradict this: `docs/guides/office-addins-admin-guide.md:556-597` and `office-addins-deployment-checklist.md:85,97,103`. Offered to fix them.

## Waiting on the owner (live checks)
- **Task 127**: Matter Documents grid footer `v1.1.85`, no `[AppInsightsService]` warning; VisualHost renders; dev App Insights gets `customEvents`; Daily Briefing, Email page and Communication Reconciliation behave as before.
- **PCF re-sync**: MatterHeader 1.0.22; RecordHeader 1.1.12; Communication form timeline and actions; document relationship viewer opens.
- **A9 live-test item:** a licensed B2B guest signs in to the add-in from their own company's desktop Office and Word (authority pinned to the Spaarke tenant). If it fails, raise it with the owner before any workaround.
- **Task 121**: Message-ID header and link to an existing communication; an unfiled communication gets filed; "Related to" cards; reopening shows saved; a pre-121 email still shows saved; a second user gets their own document.
- **078**: one Word-on-the-web run + desktop Word build number.

## Blocked / waiting on others
- **SPA-r3 (A9):** workforce-contact sign-in (tasks 044–048, 058, 059, 074–076 not built). Teams is in scope for workforce contacts (owner 2026-10-13).
- **customer-provisioning-orchestration-r1**: T186 awaits the owner's OK; 240b/c not started.
- **UAC-r2**: on 185 (#1827), then a BFF redeploy from master; 186 (workforce baseline role) open.
- **#1591**, **#1832**: filed for the script and platform owners.

## Open owner items / later
- Main checkout `C:\code_files\spaarke` refused the fast-forward to master (untracked files). Left untouched; the owner cleans it.
- Optional: reassign the 3 pre-task-146 app-owned dev events (#1034 comment). Dataverse write, needs owner go.
- Repo copy of the Matter form loses the 6 lines at the next SpaarkeMaster release export (not hand-edited).
- Dev package rename "Spaarke (Dev)"; AppStore publishing later.
- Filed follow-ups (other owners): #1464, #1468, #1469, #1470, #1471, #1476, #1477, #1505, #1540, #1551, #1591, #1832; #1034 status comment.
