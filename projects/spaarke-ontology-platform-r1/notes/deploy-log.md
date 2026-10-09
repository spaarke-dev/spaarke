# Deploy log (spaarke-ontology-platform-r1)

## Task 111 - WizardShell consumer regression and deploy (2026-10-08)

Environment: **spaarkedev1** (https://spaarkedev1.crm.dynamics.com) only. Owner approval D-71 (2026-10-08). BFF and other environments untouched.
Build base: PR #1415 `feat/wizard-consumers-111`, head `be5ee3bf1`, 0 commits behind origin/master `23c359eaf` at deploy time.

### Deploys (all UTC, 2026-10-08)

| Surface | Method | Time | Result |
|---|---|---|---|
| sprk_spaarkeai (Console, incl. CreateAnalysisWizardWidget) | scripts/Deploy-SpaarkeAi.ps1 | 13:43 | Updated + published. Content byte-identical to dist/spaarkeai.html (5,910,332 B). Prior dev copy modified 2026-10-06 05:05Z |
| sprk_smarttodo | scripts/Deploy-SmartTodo.ps1 | 13:43 | Updated + published. Byte-identical (1,373,682 B). Prior: 2026-10-06 13:15Z |
| sprk_documentrelationshipviewer | scripts/Deploy-WebResourceInline.ps1 | 13:43 | Updated + published. Byte-identical (1,279,517 B). Prior: 2026-05-19 |
| External SPA (Power Pages `sprk-external-workspace`) | - | - | **NOT DEPLOYED** - see deviations |
| SemanticSearchControl PCF 1.1.82 (solution SpaarkeSemanticSearch) | build:prod via Invoke-PcfBuildProd.ps1; pack.ps1; `pac solution import --publish-changes` | 13:45 | Imported; dev solution now 1.1.82 (was 1.1.80). Note: `--publish-changes` publishes all customizations (PublishAll) |

PCF bundle: master source (1.1.81) 773,714 B -> this branch (1.1.82) 773,660 B (-54 B). Committed Solution bundle before: 770,149 B. Escalation trigger (growth) not hit.

### Regression rows (modal note 5.2) - live, light and dark, 100% and large

**NOT EXECUTED.** The session has no browser / UI automation tool (no Chrome or built-in browser MCP), and Dataverse SSO cannot be scripted. No row below has been verified live. Rows need a human (or a session with browser tooling).

| # | Row | Live result |
|---|---|---|
| 1 | CreateAnalysisWizardWidget (canary), SpaarkeAi | NOT VERIFIED live. Deployed + byte-verified; jest (CreateAnalysisWizardWidget 11/11) green |
| 2 | SmartTodo CreateTodoWizard | NOT VERIFIED live. Deployed + byte-verified |
| 3 | DocumentRelationshipViewer DocumentEmailWizard | NOT VERIFIED live. Deployed + byte-verified |
| 4 | external-spa DocumentUploadPage | NOT DEPLOYED, NOT VERIFIED |
| 5 | SemanticSearchControl PCF (preview -> Email stacked; bulk Email) | NOT VERIFIED live. Imported 1.1.82 |
| - | 8 embedded navigateTo wizard smoke opens | NOT VERIFIED live (embedded markup is locked by the characterization snapshot test, class hashes only changed) |

Dark-mode (ADR-021) check and Escape/close/finish checks: not executed live. Covered only by unit tests (explicit dismiss, snapshot).

### Expected user-visible changes to check when verifying
- Create Analysis wizard: 62vw x min(74vh,760px) (was 60vw x 70vh).
- SemanticSearch Email wizard: `lg` = min(1280px,94vw) x min(85vh,880px); height now capped at 880px on tall viewports.
- Wizard stepper hairline/ring widths now use strokeWidthThin/Thick tokens (visually same at 100%, scale with UI scale).

## 2026-10-08 - D-75: master 73d3b970e (PR #1429, task 106) to dev (spaarkedev1 / spaarke-bff-dev)

Owner decision D-75, dev only. Deployed from a fresh detached worktree at origin/master = `73d3b970e0077beed39be84c4675f9bf7bb4de67`.

Pre-check: dev BFF was running `7bae5950f` (assembly informational version; deployed 16:57Z), an ancestor of master (master = 7bae5950f + #1429). Not newer, so no STOP.

| Surface | Result (UTC) |
|---|---|
| BFF `spaarke-bff-dev` | `scripts/Deploy-BffApi.ps1`, 17:10-17:12Z. Package 36.25 MB. 4 critical files SHA-256 verified; /healthz Healthy; CORS check passed. Deployed DLL carries +73d3b970e. |
| `sprk_spaarkeai` (Console) | `Deploy-SpaarkeAi.ps1`, 17:19:09Z, PublishXml for that web resource only. Byte-verified against dist (5920196 B). |
| `sprk_smarttodo` | `Deploy-SmartTodo.ps1`, 17:19:33Z. Byte-verified (1373816 B). |
| `sprk_dailyupdate` | `Deploy-DailyBriefing.ps1`, 17:19:17Z. Byte-verified (1553914 B). |
| LegalWorkspace | NOT deployed: delivered as a custom page/solution (`Deploy-LegalWorkspaceCustomPage.ps1` imports a solution + publish-all), forbidden by D-75 scope. Its task 106 changes (quickSummaryConfig, useActivityFeedFilters, queryHelpers) are date-filter reads. |
| External SPA | NOT deployed (separate surface, not in scope). |

Live verification: `POST /api/v1/child-records/sprk_todo` with a user token (az CLI, api://1e40baad...) body `{"sprk_name":"zz-075-duedate-check","sprk_duedate":"2026-10-15"}` returned 201; Dataverse stored sprk_duedate = 2026-10-15; row deleted (204), zero zz-075- rows remain. External To Do routes need an external-user token: not testable here.

Build notes: fresh worktree needed `npm install --legacy-peer-deps` in the three solutions plus `src/client/shared/Spaarke.UI.Components` (dompurify unresolved otherwise). SpaarkeAi `build:ribbon` step fails in a clean worktree (`@spaarke/sdap-client` unresolved); the HTML artifact is produced before that step and the ribbon is not part of the web-resource deploy.

Still needs a human in the browser: SmartTodo quick-add with a due date; Daily Briefing "Add to To Do"; Console To Do widget add/complete; external app To Do create with a due date and Mark complete/incomplete (also needs the external SPA built/deployed if its client change is wanted: web-api-client.ts, SmartTodo.tsx); LegalWorkspace To Do date filters unverified until its solution is deployed.

## 2026-10-08 - Task 121: which live surface serves LegalWorkspace code (read-only, spaarkedev1 only)

No import, no web-resource write, no publish of any kind was run. Read-only repo and Web API GETs (az CLI token) only.

### Import graph (repo)
`src/solutions/SpaarkeAi/vite.config.ts` (lines 62-66, 149-157, 232-243) aliases/transpiles `../LegalWorkspace/src` and the `@spaarke/legal-workspace` barrel (`src/client/shared/Spaarke.LegalWorkspace/src`, re-exports `LegalWorkspaceApp`). `SpaarkeAi/src/components/workspace/WorkspacePane.tsx` and `main.tsx` import `LegalWorkspaceApp`; `LegalWorkspaceApp` -> QuickSummaryRow -> `quickSummaryConfig.ts` -> `services/queryHelpers.ts`; `ActivityFeed` -> `useActivityFeedFilters.ts`. All three 106 files are reachable from the Console bundle. `git diff 73d3b970e^1 73d3b970e -- src/solutions/LegalWorkspace`: quickSummaryConfig.ts and queryHelpers.ts changed runtime code (`formatDateOnly(new Date())` replaces `now.toISOString()` / UTC-midnight ISO); useActivityFeedFilters.ts changed COMMENTS ONLY (no runtime effect, nothing to verify live).

### Live sprk_spaarkeai (webresourceid 5206a442-3451-f111-bec7-7ced8d1dc988, modifiedon 2026-10-08T17:19:09Z, 5,920,196 B decoded)
Carries both runtime changes. Minified bundle contains `case ar.Overdue:return\`sprk_duedate lt ${sE(new Date)} and statuscode eq 1\`` and `...(statuscode eq 1 or statuscode eq 659490001) and sprk_duedate lt ${e}` with `e=sE(new Date)`; `sE` is `getFullYear()-pad(getMonth()+1)-pad(getDate())` (local calendar date). The old forms (`setUTCHours(0,0,0,0)` + `toISOString()` in the `sprk_duedate lt` filters) are absent. Deviation: step 1's clean-worktree vite build at 73d3b970e was not run; the live bundle's distinct template strings are the stronger evidence, and D-75 already byte-verified the deployed content against its dist. The escalation trigger (Console lacks the changes) did NOT fire.

### Other surfaces
| Item | Result |
|---|---|
| canvasapps `sprk_LegalOperationsWorkspace` (and any name/displayname containing "Legal") | **Does not exist** (47 canvas apps listed; none match) |
| solution `SpaarkeLegalWorkspace` | Exists, unmanaged, v1.0.1, modifiedon 2026-02-18. Contains ONE component: customcontrol `sprk_Spaarke.LegalWorkspace` v1.0.1 (modifiedon 2026-02-18). Web resource `cc_Spaarke.LegalWorkspace/bundle.js` (2026-02-18) is its bundle |
| webresource `sprk_corporateworkspace` (8b7e8863-020d-f111-8342-7ced8d1dc988) | Exists, modifiedon 2026-07-07T16:40:38Z, 3,759,131 B, **OLD code** (UTC-midnight `toISOString()` filter, no `formatDateOnly`). Member of 7 solutions. Retired 2026-05-26 (OC-R4-05); orphaned |
| references to the above | Sitemaps of all 11 apps referenced by appmodulecomponents checked: none mention corporateworkspace or LegalOperations (Matter Management references sprk_spaarkeai, sprk_smarttodo, etc.). No systemform XML contains LegalWorkspace/corporateworkspace. No appmodulecomponent points at sprk_corporateworkspace or sprk_spaarkeai directly |
| repo: PCF source `src/client/pcf/LegalWorkspace` | Removed from master (commit 5557abaa80); `scripts/Package-LegalWorkspace.ps1` and `Deploy-LegalWorkspaceCustomPage.ps1` cannot build or find a ZIP |

### Conclusion
No LegalWorkspace deploy is needed: the 106 date filters are already live inside the Console (D-75). Deploy-LegalWorkspaceCustomPage.ps1 targets a Custom Page that does not exist and a PCF whose source is gone. Recommendation: RETIRE it (guard copied from Deploy-CorporateWorkspace.ps1) and apply the same to Package-LegalWorkspace.ps1. PENDING OWNER DECISION. Live UI check (step 6) still needs a human; checklist below.

### Step 6 checklist for a human (after 20:00 local, Console embedded workspace)
1. Create To Dos `zz-121-today` (due today) and `zz-121-yesterday` (due yesterday), owned by you, open status.
2. Quick Summary: overdue count includes zz-121-yesterday and NOT zz-121-today; Activity Feed Overdue filter agrees.
3. Repeat in dark mode (badges/filters themed).
4. Delete both rows; confirm `sprk_todos?$filter=startswith(sprk_name,'zz-121-')` returns 0.

### Task 121 - owner decisions D-82 applied (2026-10-08)
- Scripts: grep of origin/master found no caller of Deploy-LegalWorkspaceCustomPage.ps1 / Package-LegalWorkspace.ps1 (no workflow, skill, README or script); targets gone (PCF source removed in 5557abaa80; Custom Page absent live). DELETED both in PR #1455 (branch fix/legalworkspace-deploy-121, head 748106457d); LEGALWORKSPACE-RETIREMENT.md updated. Not merged.
- Orphan web resource DELETED from spaarkedev1 (owner-approved): `sprk_corporateworkspace`, id 8b7e8863-020d-f111-8342-7ced8d1dc988, modifiedon 2026-07-07T16:40:38Z, 3,759,131 B decoded, sha256 60f0e524711b7e755f4f45d67c8e0e3767d17d23b02dc389ecc2f29de5d12e35. Backup: scratchpad `sprk_corporateworkspace.backup.html`. DELETE returned 204; GET by id returns 404; name query returns 0 rows. No publish run.
- Live UI check (step 6) moved to the owner's checklist page; no zz-121- rows were created.
- Tenant-wide publish in other scripts/skills moved to task 130 (D-83).

### Task 129 - D-63 definitions + VisualHost 1.4.39 (2026-10-08, PARTIAL: awaiting owner approval)
- Inventory (read-only) and build done; see notes/129-finalduedate-definitions.md. No Dataverse definition edited, no import run, no publish of any kind.
- Build from a throwaway worktree of origin/master (C:\wt129) because this branch lacks #1413. Invoke-PcfBuildProd.ps1 succeeded; bundle 783,088 B, reports 1.4.39, 0 sprk_finalduedate strings. Zip: C:\wt129\src\client\pcf\VisualHost\Solution\bin\VisualHostSolution_v1.4.39.zip (250,670 B).
- Browser checklist after approval: zz-129-A (due in 3 days, final due in the past) and zz-129-B (due yesterday, final due in the future), open Task type, on a matter. Due Date Card List shows A with sprk_duedate and not overdue, B overdue; TASKS & EVENTS calendar places each on its sprk_duedate; "All Tasks Open 7 Days" lists A and B by sprk_duedate; Matter Tasks overdue count includes B not A; dark mode themed; delete both and confirm `sprk_events?$filter=startswith(sprk_eventname,'zz-129-')` returns 0.

### Task 122 - external SPA dev deploy (2026-10-08, owner-approved D-84; browser checks pending)
- Target: Static Web App swa-spaarke-external-spa-dev, https://green-dune-0c4f1221e.7.azurestaticapps.net (dev only; workflow unchanged; Power Pages path not used).
- Deployed SHA: 885d0c5f92c89bcb44fc01ad678a318ce835001f (origin/master; contains 23c359eaf/056 and 73d3b970e/106). Master unchanged between ship-list review and dispatch.
- Run: 37823351696, `gh workflow run deploy-external-spa.yml --ref master`, created 2026-10-08T18:19:12Z, finished success 18:30:21Z. Previous run 37741946955 (37d0c944c).
- Ships: 056 WizardShell in SprkModal (#1386); 106 To Do date-only (#1429); 098 event DateOnly (#1359); uac-r2 067 AccessGrantModal No Access list (#1434); spaarkeai SprkChat playbook opt-in (60ccc1b95, 414cfa9643); test-only timezone fixes.
- Local build/tests at the SHA before dispatch: vite build OK, SPA vitest 5 files / 43 tests pass.
- Verification: build emits unhashed names (assets/app.js), so compared content. Live assets/app.js is 1,343,276 B, sha256 begins 9a6ea229a62f17b9f6e0, identical to the local build of the same SHA (same size, same hash prefix; was 7ed8a75bfe2be008 before deploy). index.html references assets/app.js.
- Not done: live browser checks; #1428 stays open until the owner's checks pass (task 111 regression row 4 to be updated then).

Owner browser checks (dev external SPA, test data prefixed zz-122-):
1. Start a document upload (DocumentUploadPage wizard): Escape and a backdrop click must NOT close it; the x and Cancel must close it; no resize corner; light and dark both fully themed.
2. Create a To Do `zz-122-today` due today: it shows today's date and is not overdue. Mark it complete, then reopen it, no error.
3. Events calendar / dashboard dates fall on the correct day (not shifted by one).
4. Manage Access dialog shows the No Access list without error.
5. Sign-in works (a CIAM authority/client/audience error means stop and report).
6. Delete zz-122- To Dos; confirm none remain.

### Task 129 - applied under D-85 / D-86 (2026-10-08)
- Edited 2 views + 2 chart rows (before/after in notes/129-finalduedate-definitions.md); one PublishXml (entity sprk_event). Matter Tasks cardDescription fixed to `{overdue} overdue`.
- VisualHost: `pac solution import` without --publish-changes succeeded; served bundle only flipped after PublishXml of the two cc_Spaarke.Visuals.VisualHost web resources (bundle.js now 783,088 B, has 1.4.39). `customcontrols.version` still 1.4.38 (no scoped publish element for custom controls). No PublishAllXml.
- Stale-bundle guard: PR #1463 (f173e1b8f), not merged. C:\wt129 removal started.

## 2026-10-09 - D-89: task 120 (ISS-018) live steps, spaarkedev1 only - STOPPED at step 5 (two new defects)

| Step | Result (UTC) |
|---|---|
| Pre-check | dev BFF ran `d7fdcafc3` (ancestor of master; 31 commits behind). Not newer; proceed. |
| 1 BFF | Fresh detached worktree `C:\wt120d` at origin/master `e9f08b764` (PR #1461 + ADR-034 97d6e0be3). `pwsh scripts/Deploy-BffApi.ps1` 02:24:04-02:25:53Z. Package 36.33 MB; 4 critical files SHA-256 verified; /healthz Healthy; CORS OK. Deployed DLL InformationalVersion `1.0.0+e9f08b76490ac6527489c42e49c76fd4f815d669`; /healthz/dataverse 200. Console untouched (#1456 auto-deploy). |
| 2 D-80 audit | org isauditenabled=True (unchanged). `sprk_playbooknode` IsAuditEnabled False -> True (PUT 02:27:06Z), PublishXml for sprk_playbooknode ONLY 02:27:15Z, read back True. Attributes configjson/dependsonjson/executortype/isactive/name/outputvariable already audit-enabled. |
| 3 Sync (4) | `Deploy-NotificationPlaybooks.ps1 -Only 'Tasks Overdue','Tasks Due Soon','New Work Assignments','Matter/Project Activity Summary'`: -DryRun 02:27:29Z (4/4 OK), real 02:28:14-02:28:32Z, 4/4 OK, every node + playbook row read back equal to the definition. Records: `notes/task-120-records/*.before.json` / `*.after.json`. Docs/Emails/Events NOT synced (uac-r2 #1355). |
| 4 Alert | `az deployment group create -g spe-infrastructure-westus2 -n iss018-notification-alert ...` first attempt failed (Git Bash rewrote `/subscriptions/...` into a Windows path; nothing created); re-run with MSYS_NO_PATHCONV=1 02:29:47Z Succeeded. Rule `notification-playbook-total-failure-dev`: enabled, Sev 1, every 15 min / 1 h window, scope spe-insights-dev-67e2xz, action group ag-spaarke-oncall-dev (enabled, dev@spaarke.com). |
| 5 Live proof | zz-120 rows 02:32:31-02:33:05Z: matter fc04e4a8 (created by operator 1d02f31c, then owner = team "Spaarke" 09fbf21c), Task events 428e45bd (due 2026-10-06), 448e45bd (due 2026-10-10), work assignment 01af6abc, all team-owned -> only the fetchInGuids membership branch can match. Cleared sprk_lastrundate on the 4 synced playbooks (before: Overdue/Due Soon 2026-10-08 18:00:39/18:00:26, WA/MA 11:00:27/11:00:19 as returned by the Web API); triggered `POST /api/admin/jobs/notification-playbook-scheduler/trigger` 02:33:42Z, run 4d79e284 (correlation 51190a4bfc7c4789b3230ad060e4d268) completed 02:34:30Z **Failed**. **STOPPED.** |
| 6 Live test | Not re-run (stopped at step 5). |

Run 4d79e284 results: Matter Activity Failed 0/15; Tasks Overdue Failed 0/15; Tasks Due Soon PartialFailure 1/15; New Work Assignments PartialFailure 1/15; Emails/Docs/Events Skipped (not due). D-78 worked: Error traces "failed for every user" for MA and Overdue (02:33:55Z, 02:34:19Z), lastrundate not advanced, job run Failed; the alert KQL returns both rows (fires at next evaluation).

Per-user errors (App Insights, run window): 14 users x 4 playbooks "Create Notification ... Notification body is required" (Condition false but Create still runs); 1 user (operator) on Overdue/MA "priority 300000000 / 100000000 outside the valid range. Accepted 200000000, 200000001".

Delivered (appnotification, all to operator 1d02f31c only): Due Soon 7 (6 real + "Due soon: zz-120-due-soon task", regardingId 448e45bd, viaMatter.name "zz-120-matter (ISS-018 live proof)"), WA 1 ("New assignment: zz-120-work assignment", regardingId 01af6abc, viaMatter.name zz-120-matter). No zz-120 notification for any other user. Overdue and MA zz-120 not delivered (priority defect).

Cleanup 02:36Z: deleted 2 zz-120 notifications, 2 events, 1 work assignment, 1 matter; confirmed 0 zz-120 matters/events/work assignments/notifications. The 6 real Due Soon notifications stay (product output).

### D-91 pause (2026-10-09, spaarkedev1)
- Mechanism verified in code before the change: `PlaybookSchedulerJob.QueryNotificationPlaybooksAsync` (deployed master e9f08b764, lines 389-392) selects `sprk_playbooktype = 2 AND statecode = 0`; no `sprk_enabled` exists on that path. So `statecode` is the switch. sprk_analysisplaybook status pairs: Active 0/1, Inactive 1/2.
- Before (02:42:37Z): all 7 statecode=0 statuscode=1. Overdue/MA lastrun null (cleared for the D-89 proof, not advanced by D-78); Docs 2026-10-08 18:00:10; Emails 11:00:08; Events 18:00:19; Due Soon 2026-10-09 02:34:08; WA 02:34:30.
- Change (PATCH statecode=1, statuscode=2 only; names + type checked first), 02:42:39-02:42:40Z: Tasks Overdue 4369cab2, Matter/Project Activity Summary 24051c80, New Documents 29051c80, New Emails 2f46208e, New Events a4bc529c. No node edit, no publish (data row, nothing to publish).
- Read back (02:42:40Z): the five statecode=1 statuscode=2; Due Soon and WA unchanged (0/1). lastrundate values untouched.
- Next tick 03:00:00Z (run 99196f19, trigger Scheduled, Succeeded, 0.36 s): children = Tasks Due Soon (Skipped - not due) and New Work Assignments (Skipped - not due) ONLY; the five paused playbooks are absent (not queried). Due Soon / WA last ran 02:34Z and are daily, so their next real run is the first tick after ~02:34Z 2026-10-10.
- To resume a playbook: PATCH statecode=0, statuscode=1 (its sprk_lastrundate decides when it is next due).

### D-100 retire (2026-10-09, spaarkedev1)
Owner D-100: retire the notification playbooks (the Daily Briefing does not read appnotification; they only fed the model-driven bell). Switch = statecode (scheduler selects sprk_playbooktype = 2 AND statecode = 0, verified for D-91).
- Before (04:14:57Z): Tasks Due Soon 77f77aa5 statecode=0 statuscode=1 (lastrun 2026-10-09 02:34:08); New Work Assignments be7874be 0/1 (lastrun 02:34:30). The other five already 1/2 (D-91).
- Change: PATCH statecode=1, statuscode=2 (name + type checked first): Tasks Due Soon 04:14:58Z, New Work Assignments 04:14:59Z. Nothing else written (no node, alert, repo or code change; lastrundate untouched).
- Read back (04:14:59Z): all 7 notification playbooks statecode=1 statuscode=2 - Matter/Project Activity Summary, New Documents, New Emails, New Events, New Work Assignments, Tasks Due Soon, Tasks Overdue.
- BFF process restarted between 04:15Z and 04:45Z (in-memory run history empty); same build still running (`1.0.0+e9f08b764`, no new deploy - last OneDeploy 02:24:46Z).
- Next tick 05:00:00Z: run 1e29021b, trigger Scheduled, Succeeded in 0.37 s, processedItems=0, children=[] -> no notification playbook queried or run.
- Left as is (task 131 / owner): alert rule notification-playbook-total-failure-dev (no new Error traces can occur), node rows, repo JSONs, code, C:\wt120d.
