# Current Task State — `unified-access-control-r2`

> **Format (2026-10-06):** this file holds CURRENT state only and is REWRITTEN at each checkpoint — never prepend a new block on top of old ones. Standing directives + environment gotchas live in the project `CLAUDE.md` ("Standing directives & gotchas"). Session history lives in git (checkpoint commit messages) and the verbatim archive `notes/handoff-history/current-task-archive-2026-10-06.md` (do NOT load it on recovery; grep it only if you need a specific past detail). Why: see `.claude/skills/context-handoff/SKILL.md` "State, not history" and `notes/handoff-history/2026-10-06-conversion-review.md`.

> **Last Updated**: 2026-10-06 ~20:15 UTC (checkpoint #18, commit `657dbc9de`). Converted to state-only format from the #18 block.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Batch-4 dev live gates + hotfixes; batch 5 started with task 171 (broker-only SPE bytes). 116/168 tasks complete |
| **Step** | Hotfixes deployed to dev: #1319 (No Access lookup filters + event create 500), #1320 (No Access enforce privilege), #1322 (owner's own share revoked as owner); BFF at master b977fc7a6. Gate groups G1-G7 done (results: notes/batch4-live-gates-2026-10-06.md). Completed today: 003, 132, 133, 148, 149, 156, 158, 159, 160, 167. RUNNING (Agent tool, background): gates-g6 (163-166/168/169 API gates), exec171 (task 171 executor, worktree C:\wt171, branch task/uac-r2-171), fmpush (field-mapping push lookup fix D-G6-3, worktree C:\wtM, branch fix/uac-r2-field-mapping-push-lookups) |
| **Status** | in-progress |
| **Next Action** | (1) When fmpush reports: review diff, open PR, merge on Router green, deploy (C:\wtR fresh master; Deploy-BffApi.ps1 dev), re-run 166 (e)+ and 12. (2) When gates-g6 reports: record results into the gate note, mark tasks whose gates all passed. (3) When exec171 reports: review, PR, merge, deploy, run its post-deploy live checklist. (4) Owner pending: is SPE 'Modified by = BFF app' acceptable for app-only writes (171); G146-3 alert setting (admin user id); 137 WritesEnabled; 18 empty test SPE containers to delete (round 68 list + G7's 3; operator step, no BFF delete route). (5) Owner screen session (~2.5-3h): CIAM (136/037/039, 137 obs, 140, 157), workforce sign-in (141 G-8), wizards/forms (047, 142 G-7, 147 G147-4/6, 150 G-10+UI, 168 (h), 169, 163/164/166 UI), BU1 SPE admin for 165 leaf gates, a second non-admin login. (6) Then batch 5 rest: 154, 113/114 (round 67 rescope)/105/101, 064, 153/067, 099, 036 (ADR-034 path B pending owner). Owner decisions today: rounds 67 (114 Restricted/isexternal), 68 (gate findings), 69-70 (broker-only + standing BU membership + JIT secure edit; Office/Word desktop system-user only). |

## Running now
No workflows are running. Three **Agent-tool** agents run in the background. They are not workflow agents, so SendMessage by name is allowed (a workflow agent is never SendMessage'd; see memory `workflow-agent-messaging`).

| Agent | Work | Worktree / branch |
|---|---|---|
| `gates-g6` | 163-166/168/169 API gates | (results → `notes/batch4-live-gates-2026-10-06.md`, written by the main session) |
| `exec171` | task 171 executor | `C:\wt171` / `task/uac-r2-171` |
| `fmpush` | field-mapping push lookup fix D-G6-3 | `C:\wtM` / `fix/uac-r2-field-mapping-push-lookups` |

## Branches and environment
- **Work branch:** `work/unified-access-control-r2` @ `657dbc9de`. Clean, 0 unpushed (measured at conversion).
- **Master and dev BFF:** `b977fc7a6` (#1322). Deploy worktree `C:\wtR` (fresh master).
- **Integration branch:** none in flight. `integ/uac-r2-batch4` was consumed by PR #1312 (`d254d7166`, squash). Follow-up PR #1314 merged (`891cfd9a3`) and the BFF was redeployed.
- **Live gates run as `testuser1@spaarke.com`** (the non-admin child-BU user; `uac.child.user` has no known password). Token: `AZURE_CONFIG_DIR=C:/tmp/az-uac-child az account get-access-token --resource api://1e40baad-e065-4aea-a8d4-4b7ab273458c`. Helpers `gate161.py` / `recheck1314.py` (`call()`/`tok()`) are in `C:/Users/RALPHS~1/AppData/Local/Temp/claude/c--code-files-spaarke-wt-unified-access-control-r2/993ea642-9455-4e0e-8f57-ed99084c5e37/scratchpad/`. 150's UI tests are run by hand with the owner.

## Dev state (deployed 2026-10-06 from `C:\wtD` @ d254d7166; backups/manifests in `C:\wtD\scripts\logs\{,deploy-phase2\,deploy-phase3\}`)
- **Phase 1:**
  - Schema: 142/158 ledger, 143, 140, 133.
  - 165 marker and binding backfill (5/5 stamped).
  - 144 migration (0 rows).
  - BFF (36.13 MB) plus the external SPA.
  - 150 FLS lock.
- **Phase 2:**
  - Clients that wrote `sprk_issecure` rebuilt.
  - 166 gate 23a/26; 148 backfill (0 changes); 168 in full.
  - PCFs RegardingResolver 1.6.1, CommunicationConnections 1.7.0, TrackingFieldTrio 1.0.35.
  - 4 web resources; 142 G-4 form libraries.
  - Ribbons 147 G147-6 and 142 G-5 (Update Access + Remove Secure).
  - 144 -Verify PASS.
- **Phase 3:**
  - Removed (owner-approved, backed up first): UniversalDatasetGrid, `sprk_externalworkspace`, UniversalDocumentUpload (UQC solution and its "File Upload" custom page).
  - "New Document" appaction repointed to `Spaarke_UploadDocumentsStandalone`.
  - Gate 21: 0 of 5,327 web resources write the pointer.
  - Gate 22 census.
  - Gate 23 (option a): `sprk_graphdriveid` and the relocation columns locked; `sprk_graphitemid` NOT secured (alternate key `sprk_graphitemid_uk`, 0x80060896).
  - Gate 24: 367 moved, 0 failed.
  - Gate 25: `DocumentPointer__StrictDerivedContainer=true`.
  - 150 G-11: Make Secure live.
- **Held:**
  - Power BI workspace id: owner says leave unset; reporting answers 503.
  - `PowerBi__ClientSecret` is a plain app setting: owner says leave it.

## Task status
- **116/168 complete.** Completed 2026-10-06: 003, 132, 133, 148, 149, 156, 158, 159, 160, 161, 167.
- **Still 🔄 [wip] "INTEGRATED, completes at live gates"** (measured from TASK-INDEX at conversion): 137, 140, 142, 143, 146, 147, 150, 157, 162, 163, 164, 165, 166, 168, 169. Each becomes ✅ only when its live gates pass on dev.
- **171** 🔲: R69, batch 5 FIRST (critical), executing via `exec171`. **170** 🚫 cancelled.
- Batch 5 dispositions follow round 59. The drift check was clean at 167/167.

## Batch 5 (per `notes/batch5-scope-review-2026-10-05.md` + Next Action 6)
- **Order:** 171 → 154 (reuse the existing picker) → 113, 114 (round 67 rescope), 105, 101 → 064 → 153 and 067 → 099 → 036 (read set plus per-record write checks; ADR-034 path B pending owner) → 090.

## Open follow-ups and issues
- **Recorded in the #1314 body:**
  - `/disable` does not survive a restart.
  - The `pac` bash shim.
  - `deploy-spaarke-ai.yml` fails ("Could not resolve react").
  - Orphans: `sprk_OpenDocumentQuickCreate` and the empty "Upload Documents" form.
  - `Create_Task_From_Email` schema error (missing `dueDate`).
  - Thread `sprk_regardingreportcard` fault on send (161 note §4.9).
- **#1313:** the 165 backfill cannot list a config with no secret name (Model 1). Container `b!MVasATu…` is unexamined (fails closed).
- **#1317:** insert-template returns raw XSLT for Dynamics-editor templates. Origin is email-r5, not UAC scope.
- **#1293** (fix for #1290) is a DRAFT. Rebase it once #1123 merges and keep only its unique parts; the owner chose "grid fills the row".
- **Other issues:** #1290, #1303–1307, #1310.

## Key notes
`notes/session27-owner-decisions-and-research.md` (owner/main-session rounds 1–70; rounds 66–70 are from 2026-10-05/06) · `notes/batch4-live-gates-2026-10-06.md` · `notes/batch5-scope-review-2026-10-05.md` · per-task notes for each task's live gates · `notes/handoff-history/2026-10-06-conversion-review.md` (items to verify from the conversion).
