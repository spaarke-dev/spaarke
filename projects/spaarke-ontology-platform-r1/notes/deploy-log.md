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

## 2026-10-09 - D-104(a): delete the notification alert rule (spaarkedev1 subscription, main session)

- Before (11:37Z, `az monitor scheduled-query show`): `notification-playbook-total-failure-dev` in `spe-infrastructure-westus2`, enabled, severity 1.
- Action: `az monitor scheduled-query delete -g spe-infrastructure-westus2 -n notification-playbook-total-failure-dev --yes` at 2026-10-09T11:37:11Z.
- After: `show` returns `ResourceNotFound`. The bicep that created it is deleted by PR #1493 (task 131), so no deploy recreates it.

## 2026-10-09 - D-108: writer role grants (spaarkedev1, main session)

Spaarke Ontology Service (root copy `b1fb7ee0-bfbe-f111-aaaf-0022482913fc`): 46 -> 50 privileges at 15:04:10Z (`AddPrivilegesRole`, HTTP 204): Append budget revision, AppendTo budget, AppendTo User, Assign budget revision, all Global. Before/after read back with `RetrieveRolePrivilegesRole`: only those four added. Details: notes/security-roles.md.

## 2026-10-09 - D-109: Read on budget revision (spaarkedev1, main session)

Spaarke Basic User +`prvReadsprk_BudgetRevision` Deep (16:06:59Z, 648 -> 649). Secure Record Owner +`prvReadsprk_BudgetRevision` Basic via `Set-SecureRecordOwnerRolePrivileges.ps1` (-Apply 28 -> 33, SharePoint four stripped -> 29, -Verify PASS), negative control 403 before, positive control 201 x2 after (probes deleted). Details: notes/security-roles.md.

## 2026-10-10 - Repair the triage-email Action prompt (spaarkedev1, main session; owner approved)

- **Why:** task 074's recall run failed at 21.8%. The `sprk_analysisaction` row `triage-email` (`c1fa96bf-2697-f111-b8dc-7ced8ddc4a05`, last modified 2026-09-29T18:07:46Z) had 33 values replaced by PowerShell `ConvertTo-Json` artefacts (`{"Length": N}` / `{"Count": N}`): all 9 `instruction.constraints`, the 5 `metadata.tags`, and fields in the examples. `PromptSchemaRenderer` fell back to flat text, so the task-072 tie-breaker guidance never reached the model (#1584).
- **What:** PATCH `sprk_systemprompt` = the repo mirror `infra/dataverse/actions/triage-email.action.json` minus the six deploy-row scalars and every `$comment*` key (the original deploy shape). Before writing, every non-corrupt value in the live row was verified byte-identical to the mirror, and every damaged string's `Length` matched the mirror text.
- **Read-back:** 2026-10-10T13:33:08Z, identical to the repaired document; 9 string constraints; 0 Length/Count objects. A scan of all 79 `sprk_analysisaction` rows found no other row with the artefact.
- **Backup** of the corrupt row: session scratchpad `triage-fix/live-row-backup.json`.
- **Owner of the row:** the email project (email-communication-intelligence); told on #1584.

## 2026-10-10 - D-110: AppendTo on AI search index for Spaarke Core User (spaarkedev1, main session)

- **Why:** task 046 live proof (#1609 notes §F.1, issue #1607): ordinary users holding Create on `sprk_workassignment` (Spaarke Core User, Deep) could not create a work assignment when their business unit names an AI search index — the wizard binds `sprk_AI_Search_Index` and the role had Read but not AppendTo on `sprk_aisearchindex` (UserOwned). Same bind in the Matter/Project/Event/Invoice wizards. Needed for Assign Work as a decision next step (043/046).
- **What:** `AddPrivilegesRole` on the root **Spaarke Core User** (`75e1b627-2ab6-f111-aaad-0022482913fc`): `prvAppendTosprk_AISearchIndex` at **Deep** (matching its Read). Standing approval D-110 (CRUD-class privilege on Core User for users to work).
- **Read-back:** 743 → 744 privileges; exactly one added (`748d407e-3d88-4ba4-86bc-1245bbe00862`, Deep); none removed or changed. Repo role XML updated to match.

## 2026-10-10 - Task 074 D-49: write run 4 recall to the triage category rows (spaarkedev1, fix round for PR #1585)

- **Why:** 074 acceptance criterion: each measured category row holds measured recall, set size and date. The rows still held run 2 values.
- **What:** PATCH (`If-Match: *`) on `sprk_triagecategory` `8b62dd84-1fbc-f111-aaaf-3833c5e9614d` (Fee / rate change) and `8d62dd84-1fbc-f111-aaaf-3833c5e9614d` (Scope / budget change) at 2026-10-10T21:18Z, HTTP 204 each: `sprk_measuredrecall`, `sprk_labelledsetsize`, `sprk_recallmeasuredon` only. `sprk_recallmeasuredon` is DateOnly, written as `2026-10-10`. Guidance and every other column untouched.
- **Before (read back first):** Fee 25.93 / 92 / 2026-10-10; Scope 39.29 / 92 / 2026-10-10 (run 2, as expected).
- **After (read back):** Fee **88.89** / 92 / 2026-10-10 (`modifiedon` 21:18:20Z); Scope **89.29** / 92 / 2026-10-10 (`modifiedon` 21:18:21Z). Strict per-category recall from run 4 (24/27, 25/28).

## 2026-10-15 - Task 035: BFF + evaluator deployed to dev (spaarke-bff-dev); verification, alerts, report-only runs

Dev only. Deploy approved by the owner (D-138); the alert deploy is an Azure change approved under D-138 as part of 035. No Dataverse writes in this section: every run below is read-only (GETs, admin-jobs reads, App Insights queries).

### Deploy outcome (done by the main session)

- Built and deployed from **master `f292ad119`** with `scripts/Deploy-BffApi.ps1` to `spaarke-bff-dev` (`rg-spaarke-dev`) at about 05:00Z.
- Package 37.1 MB. All 4 critical files matched by SHA-256. `/healthz` 200. Signed-in `GET /api/v1/signals/worklist` 200. `/healthz/dataverse` 200.
- **Publish-size delta: zero by construction.** The deployed branch was master itself, so "branch vs a fresh master build" compares master with master. The per-task deltas were measured in each task's PR (CLAUDE.md section 10 bullet 4). Absolute 37.1 MB (below the 55 MB review and 60 MB hard-stop thresholds).
- The branch was master (0 behind). Another project (R3) may redeploy its own branch build over `spaarke-bff-dev` at any time; that is expected and does not invalidate this record.

### Schema pre-flight (step 3.5), passed 2026-10-14

On the target environment (spaarkedev1): `sprk_signal.sprk_regardingrecordtype` and `sprk_decisionrecord.sprk_regardingrecordtype` are both **Lookup to `sprk_recordtype_ref`**, matching the repo solution source; `sprk_decisionrecord` carries all 14 `sprk_Regarding*` attributes from task 140. The BFF was not deployed ahead of its schema.

### Criterion 83: the Signal re-evaluation job is registered (read 2026-10-15 04:47Z, signed in)

`GET https://spaarke-bff-dev.azurewebsites.net/api/admin/jobs` (`JobsEndpoints.cs`, `RequireAuthorization("SystemAdmin")`; token from `az account get-access-token --resource api://1e40baad-e065-4aea-a8d4-4b7ab273458c`) returned 200 with the registry. `GET /api/admin/jobs/signal-reevaluation/status`:

```
{"jobId":"signal-reevaluation","displayName":"Signal Re-evaluation","enabled":true,"cronSchedule":"30 2 * * *",
 "lastRunStartedOn":null,"lastRunCompletedOn":null,"lastRunStatus":null,"nextScheduledOn":"2026-10-16T02:30:00+00:00","recentRuns":[]}
```

Registered and enabled; it has not run yet (first run 2026-10-16 02:30Z). Also registered and running every 5 minutes: `todo-assignee-owner-reconciliation` and `core-ancestor-stamp-reconciliation` (both Succeeded at 04:45Z and 04:50Z).

Side observation (not 035's): `identity-link-reconciliation` reported `lastRunStatus: Failed` at 04:45Z. Not investigated.

### Criterion 85: no 404 behind RequireAuthorization (2026-10-15 04:47Z)

| Route | Anonymous | Signed in |
|---|---|---|
| GET /api/v1/signals/worklist | 401 | 200 |
| GET /api/admin/jobs | 401 | 200 |
| GET /api/admin/jobs/signal-reevaluation/status | 401 | 200 |
| GET /api/me | 401 | 200 |
| GET /api/v1/signals/{guid}/decision-plan (zero guid) | 401 | 404 (ProblemDetails "requested record not found": handler-level, the route matched) |
| GET /api/v1/signals/{guid}/suppression (zero guid) | 401 | 404 (same ProblemDetails) |
| POST /api/v1/signals/{guid}/decision | 401 | not probed (it would write) |
| GET /healthz | 200 | - |

Every protected route answers 401 anonymously, so the route exists and the package is complete. The two signed-in 404s are the handlers reporting an unknown Signal id.

### Alert rules (step 3, deployed 2026-10-15, dev)

Both are `Microsoft.Insights/scheduledQueryRules` in `spe-infrastructure-westus2` (the App Insights resource group; the other deployed alerts live there), scoped to `spe-insights-dev-67e2xz`, action group `ag-spaarke-oncall-dev` (`rg-spaarke-dev`), Sev 1 (critical), enabled, autoMitigate on. Read back with `az monitor scheduled-query list`: both `enabled: true`.

| Rule | Id | Frequency / window | Query |
|---|---|---|---|
| writer failures | `/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/spe-infrastructure-westus2/providers/Microsoft.Insights/scheduledQueryRules/spaarke-bff-dev-ontology-writer-failures` | 5 min / 15 min | `customMetrics \| where name == "ontology.writer.failures" \| summarize failures = sum(valueSum) \| where failures > 0` (no dimension filter) |
| evaluator stalled | `/subscriptions/484bc857-3802-427f-9ea5-ca47b43db0f0/resourceGroups/spe-infrastructure-westus2/providers/Microsoft.Insights/scheduledQueryRules/spaarke-bff-dev-ontology-evaluator-stalled` | 1 h / 1 day | `customMetrics \| where name == "ontology.evaluator.runs" \| where tostring(customDimensions.outcome) == "succeeded" \| summarize succeededRuns = countif(valueSum > 0) \| where succeededRuns == 0` |

Source: `infrastructure/bicep/modules/alerts.bicep` (alerts 7 and 8).

Deviations from the POML, with reasons:
1. **Scheduled-query rules, not metric alerts.** The module's other six alerts are metric alerts, but (a) "no successful run" is an absence condition and a metric alert never fires on missing data, and (b) `customMetrics` held no `ontology.*` series at all on dev (checked 2026-10-15), so a metric alert on a never-emitted series is not creatable. Scheduled-query rules are the pattern the deployed dev alerts use (`redis-cache-*`, and the retired notification-playbook rule).
2. **Window 24 h, not 26 h.** The first deploy failed on `WindowSize of 1560 minutes is not supported` (the allowed set is 5 to 720 minutes, 1440 and 2880). 1440 is the closest to the nightly cadence (job at 02:30Z); 2880 would let one missed night go unreported. A run that finishes a few minutes after the evaluation minute can fire the rule for up to an hour; autoMitigate clears it.
3. **Scoped deployment.** The module also declares six AI metric alerts that were never deployed and a conditional action group. To deploy only the two new rules, the module was compiled (`az bicep build`), the ARM template filtered to the two `ontology` resources, and deployed with `az deployment group create -g spe-infrastructure-westus2 -n ontology-035-alerts` with `alertNamePrefix=spaarke-bff-dev`, `appInsightsId`, `actionGroupId=.../ag-spaarke-oncall-dev`. What-if showed exactly 2 Create and no other change. The first attempt created nothing for the `/subscriptions/...` id (Git Bash rewrote it into a Windows path); the re-run used `MSYS_NO_PATHCONV=1`. A second attempt at 26 h failed after creating the writer-failures rule only; the final run is idempotent.

**Expect `spaarke-bff-dev-ontology-evaluator-stalled` to fire** at its first evaluation: the job has never run on dev, so there is no succeeded run in the last 24 h. It clears after the first nightly run succeeds (2026-10-16 02:30Z plus export delay). It emails the on-call group.

### Report-only runs (step 4); no Dataverse writes

**(a) 144 stamp reconciliation: `pwsh scripts/Invoke-OntologyStampBackfill.ps1` (no `-Apply`), 2026-10-15T04:54:37Z, spaarkedev1:**

```
sprk_signal          total=0 coreSubject=0 intermediate=0 (stampEmpty=0 stampSet=0) noSubject=0
                       typed sprk_matter: set=0 inStamp=0 notInStamp=0
sprk_decisionrecord  total=0 coreSubject=0 intermediate=0 (stampEmpty=0 stampSet=0) noSubject=0
                       typed sprk_matter: set=0 inStamp=0 notInStamp=0
                       typed sprk_project: set=0 inStamp=0 notInStamp=0
                       typed sprk_workassignment: set=0 inStamp=0 notInStamp=0
Report only: nothing was written. The backfill is -Apply (see the help for its prerequisites).
```

Both tables are empty on dev (confirmed independently: `GET sprk_signals?$count=true&$top=1` and `sprk_decisionrecords` both `@odata.count: 0`), so the inventory proves nothing about re-file or stale-stamp behaviour; there is nothing to backfill. The scheduled job itself (`core-ancestor-stamp-reconciliation`, run 2026-10-15T04:50:00Z, Succeeded) reported: `mode=write writesEnabled=true reportOnlyTables=[sprk_decisionrecord,sprk_signal] reportedOnly=0 scanned=64 stale=0 repaired=0 cascaded=0 alreadyApplied=0 claimHeld=0 repairFailures=0 unverified=0 orphanSourceGone=0 scanFailures=0 truncated=false`. The two Ontology tables are held report-only (default `ReportOnlyTables`); `-Apply` was not run.

**(b) 148 to-do assignee/owner reconciler: read from the admin jobs history, not triggered** (`GET /api/admin/jobs/todo-assignee-owner-reconciliation/history`). The job runs every 5 minutes with `Ontology:TodoAssigneeOwner:WritesEnabled` unset (report-only), and the nightly full pass was already taken by the first tick after the deploy, so no trigger, no app-setting change and no write was needed. Full pass, 2026-10-15T04:40:00Z, Succeeded:

```
{"status":"ok","mode":"report-only","writesEnabled":false,"attempt":1,"fullPass":true,"recentToDos":0,"carriedToDos":0,"scanned":0,"stale":0,"reowned":0,"alreadyApplied":0,"skippedCored":0,"filedNonCore":0,"skippedSecure":0,"skippedRefused":0,"configurationRefusals":0,"assigneeTeamRefusals":0,"skippedUserOwned":0,"skippedGone":0,"skippedConflict":0,"fallbackNoAssignee":0,"fallbackAmbiguous":0,"fallbackDisabled":0,"accessDenied":0,"accessDeniedHeld":0,"claimHeld":0,"repairFailures":0,"unverified":0,"scanFailures":0,"truncated":false,"resumed":false,"sampleStale":[]}
```

The incremental runs at 04:45Z and 04:50Z are identical apart from `fullPass:false`. App Insights heartbeat: `[TODO-ASSIGNEE-OWNER] heartbeat status=ok mode=report-only attempt=1 fullPass=False ... scanned=0 ... durationMs=353 trigger=Scheduled runId=a25e95db-b37f-47f7-bace-e704eca298e6`. Zero refusals of any kind. `scanned=0` because dev holds no Signal or Decision Record, so the re-own logic, the `accessDenied` path (F2) and the stale-ETag gate remain unexercised; this run proves only that the job is registered, schedules, reads cleanly and stays report-only. `notes/148-report-only-run.md` ("pending") was not edited.

### Not done (by instruction)

Criterion 89 (blank the writer client id, expect the writer-failure alert to fire, restore the setting): **not run.** The main session schedules it around R3's redeploy. Task 035's POML status and TASK-INDEX are not changed here.

## 2026-10-15 — 035 criterion 89: negative alert test + writer setting (main session)

- **Finding (task 146)**: `Ontology__Writer__ManagedIdentityClientId` had never been set on spaarke-bff-dev, so the deployed writers could not write (`OntologyWriterCredentialFactory` throws on first use). UAMI `mi-ontology-writer-dev` (client id `69040982-612e-469e-a85f-26d5172367c5`) was attached.
- **Negative test** (setting absent, 13:41:44Z): one enabled zz-146 test policy (`ZZ-146-RECIPE`, one matching zz matter), `POST /api/admin/jobs/signal-reevaluation/trigger` → outcome `failed`, policiesEvaluated 1, hits 1, written 0, refused 1. `ontology.writer.failures` reasons `credential_unresolvable` 1, `write_failed` 1. Alert `spaarke-bff-dev-ontology-writer-failures` **fired 13:44:53Z** (Sev 1, action group `ag-spaarke-oncall-dev`).
- **Setting added** 13:46:55Z (D-138 035 deploy approval; word-add-in, R3 and uac-r2 notified before and after); healthz 200 at 13:47:38Z; code unchanged (master `f292ad119`).
- **Positive run** 13:48:04Z → outcome `succeeded`, policiesEvaluated 1, hits 1, written 1, refused 0. First proof of the writer's managed-identity token path on dev.
- The test policy and its rows are cleaned up by task 146 (zz-146 prefix).


## 2026-10-16 — task 055 (dev, spaarkedev1): Console + grid configuration from master; policies on (D-146)

- **BFF:** not deployed by ontology. R3 deployed `1.0.0+eb5ba4011` (R3 + master `7315b3e85`, ancestor check passed, hash-verified, healthz 200) at 01:55Z under the owner-approved plan (a).
- **Policies (D-146):** `sprk_enabled=true` on 6101–6104 and `4d204810…` at 01:56:00Z; manual `signal-reevaluation` run `dd1ebeec`: 5 policies, 87 written, 0 refused.
- **Grid configuration:** `Apply-WorklistGridConfiguration.ps1` from master `44b6a72c1` (worktree `C:\wts-055`): `-Verify` DRIFT → `-WhatIf` one UPDATE → applied 01:56:17Z (read-back OK) → `-Verify -RequireRow` IN SYNC.
- **Console:** `npm run build` in `src/solutions/SpaarkeAi` at master `44b6a72c1` (#1989 + #1996); `Deploy-SpaarkeAi.ps1` updated `sprk_spaarkeai` (`5206a442-…`), PublishXml for that web resource only (D-83). Live 01:56:32Z; live content SHA-256 = local build `0470e7f11752cdffb9a0aaa42c507ba5574e89527db1cec18584eddc40db23f2`.
- **Peer notices:** word-add-in, uac-r2, R3 before and after.
- **Worklist reachability (task 156, found at this deploy):** no Console path opened the worklist. #2004 → master #2008 `a8c3182ec`. Console rebuilt from `a8c3182ec` in `C:\wts-055` (contains the 01:56 build's `44b6a72c1`; live hash checked unchanged before deploying); `Deploy-SpaarkeAi.ps1` 03:44:03–03:44:16Z, live SHA-256 = build `06d393561564f5835ba5702941b5767095b91f8abdf1a32dcba7670c43f218e1`. `seed-worklist-workspace-layout.ps1`: `-WhatIf` one create → CREATED `Worklist` (`202bcde0-13c9-f111-a05c-0022482913fc`, system, sortorder 13, sections `["worklist"]`) → re-run SKIP. The BFF layout route does not cache. Peer notices before and after.
  CI also published the same commit at 03:45:41Z (`deploy-spaarke-ai.yml` run 38846776203, push to master): live SHA-256 `b62f6fca…e8b849` differs from the Windows build only in CRLF/LF on 13 HTML wrapper lines. The owner's `Unknown section ID: worklist` was a browser still on the pre-03:44 bundle (hard reload).
- **Pending (criteria 1–4, 6, 7):** owner checks in the browser (worklist renders real items; one full review with two actions + one Next step; network log; console errors; Daily Briefing).

## 2026-10-17 — task 055 closed (owner browser check)

- Owner: "checked and it seems correct; formal UAT once delivered" (D-154).
- Dataverse read-back (main session): DR-1066 `67d34ab5-…` 15:03:15Z (Judgement; Send budget inquiry + Revise budget; confirmed by the owner; policy version `e51906ea-…`); budget revision 15:03:06Z (amount 150000); inquiry `sprk_communication` status Send; Signal `b35d4ec2-…` Resolved with the record linked. DR-1064 (12:25Z, Revise budget) likewise.
- Not exercised: Next step To Do (DR follow-ons empty, no new `sprk_todo`). Carried to 105 UAT.

## 2026-10-17 — owner answers: policy on, landing tab switched

- ~14:36Z: POL-MATTER-NOBUDGET (`00000000-0000-4000-8000-000000006301`) `sprk_enabled` = Yes (PATCH with If-Match *, read back; unscoped; current version `…6311`) (D-163).
- 14:38Z: dev landing tab switched to the Worklist by `scripts/Set-DefaultWorkspaceLayout.ps1 -To Worklist` (WhatIf first, verified) (D-164). Peers notified before and after.
- Same day, R3 deployed dev BFF `1.0.0+9721819ed…` (14:32:19Z): master `88b3ecb1f` (sync #2073: 063, 102a, 102, 064) + UAC #2074. Console from `88b3ecb1f` at 07:32:56Z.

## 2026-10-21 — task 130 live proof (dev, spaarkedev1): scoped PublishXml, owner-approved (D-176)

- **What ran:** `scripts/Publish-ThemeIcons.ps1` from origin/master (git-archive copy, with `scripts/lib/Publish-SolutionComponents.ps1`), `-DataverseUrl https://spaarkedev1.crm.dynamics.com`. 20:28:43–20:28:58Z. One `PublishXml` built by `New-PublishParameterXml -WebResources` for six web resources: `sprk_ThemeMenu.js`, `sprk_ThemeMenu16.svg`, `sprk_ThemeMenu32.svg`, `sprk_ThemeAuto16.svg`, `sprk_ThemeLight16.svg`, `sprk_ThemeDark16.svg` (all six "Found:"). Output "Published successfully!", exit 0. No import, no PublishAllXml.
- **Before snapshot** (unmanaged web resources 640, saved queries 1001, sitemaps 8, app modules 5): **0 pending** in every set. The run was only allowed to proceed because nothing was pending.
- **After snapshot:** 0 pending everywhere. **0** published-stamp changes on web resources, sitemaps and app modules. Sentinel content hashes (`sprk_spaarkeai`, `sprk_ThemeMenu.js`) are unchanged.
- **Saved queries 1001 → 1008:** the seven new rows are the default system views of `sprk_procedureversion` (Active, Inactive, My, Advanced Find, Associated, Lookup, Quick Find). That table was being created by ebilling's concurrent task 080 schema run, which owns them; the web-resource publish created none of them.
- **Peer notices:** R3, uac-r2, word-add-in and ebilling, before and after.
- **Limit:** this proves the web-resource branch of the scoped publish. The entity, ribbon, app-module and sitemap branches are covered by the module's unit tests (`tests/scripts/Publish-SolutionComponents.Tests.ps1`). ebilling's `Deploy-Ebilling*` scripts use the same module for their entity publishes.

## 2026-10-21 — ebilling 115 option run on sprk_policyversion.sprk_ruletype (dev; ebilling owner OK + ontology PASS)

- ebilling inserted 100000015 AddedFee and 100000016 Discount into the local choice, then ran a scoped publish of sprk_policyversion only.
- Ontology read-back: a local choice (not global) with 16 options. Ontology's 100000000 Threshold, 100000001 Switch and 100000002 Existence are unchanged, as are billing's 100000003–100000013.
- 100000014 SubmissionIntegrity is still absent on dev; ebilling task 049's option script hasn't run.

## 2026-10-21 — ebilling 080 Procedure schema on dev (ebilling owner OK + ontology PASS of 11ead6c938)

- **ebilling's run:** exit 0; created 64, changed 20, skipped 40. Verbatim verify output: `projects/ebilling-r1/notes/080-dev-run-2026-10-21.md` on work/ebilling-r1.
- **Ontology independent read-back** (metadata GETs, after the run):
  - `sprk_procedureassignment`:
    - core lookups `sprk_matter` (repaired from Cascade), `sprk_project` and `sprk_workassignment`: RemoveLink, Assign/Share/Unshare/Reparent NoCascade;
    - `sprk_excludedpolicy`: Cascade, the only cascading relationship;
    - `sprk_procedure`, `sprk_pinnedversion`, `sprk_firm`: Restrict;
    - `sprk_createdbyperson`: RemoveLink.
  - `sprk_procedureversion`: `sprk_procedure` Cascade is the only cascading relationship; `sprk_previousversion` and `sprk_createdbyperson` are RemoveLink.
  - `sprk_ruleset`: the matter, project and work-assignment lookups are RemoveLink; `sprk_procedure` is Restrict.
- **D-146 / #2132:** holds. No core-record delete reaches a Restrict or a cascade on the new tables.


## 2026-10-22 — dev BFF `a7b956fb55` (R3): ontology smoke and nightly heartbeat

- **Deploy:** R3 deployed master `a7b956fb55` at 02:17–02:18:52Z (ProductVersion `1.0.0+a7b956fb55d6…`). Its predecessor `f1b3dadcf5` is an ancestor, and it contains R3 077 `283466d40`. It adds field-mapping #2223/#2228 and UAC #2221. No ontology-owned file changes; the edits to `SecureChildShareSynchronizer`, `CoreAncestorRestamper` and `SecurableEntityRegistry` are additive. We gave OK before the deploy; the owner approved it in R3's session.
- **Smoke:** PASS. Validate on Secure PAT-908068 (`5d46df87`) returns `valid:false` / `scope_matter_secured`; on Standard REAL-2026-123456.01 (`2444af6d`) it returns `valid:true`.
- **Nightly sweep:** `[SIGNAL-EVALUATOR] heartbeat outcome=succeeded` at 02:30:29Z with policies=6 hits=142 written=0 closed=0, the same as 2026-10-21. Since 21:45:37Z the only 503xx trace is the heartbeat, with 0 Signals/Ontology/Gate exceptions.

## 2026-10-23 — dev BFF `58ffc37da3` (R3) + D-185 live order on spaarkedev1 (worklist config, POL-MATTER-NOBUDGET v2, Console User)

- **BFF:** R3 deployed master `58ffc37da3` at 11:18:12Z (owner approved in R3's session). Ontology read-back at about 11:20Z: `Sprk.Bff.Api.dll` ProductVersion `1.0.0+58ffc37da303a6b169b9b6b3d4d1ad6539dfc94b`, `/healthz` 200. It carries 158, 159, 161, 162, 163, 164 (#2303), 160 + #2314 (#2313) and R3 077.
- **(1) Worklist config** (owner approved in this session): `Apply-WorklistGridConfiguration.ps1`.
  - `-WhatIf`: one UPDATE of row `…5010`.
  - Apply at 11:20:10Z: UPDATED, READ-BACK OK.
  - `-Verify -RequireRow`: IN SYNC. Lane counts: decide 56, do 84, closedToday 4, suppressed 0, closedEarlier 8.
- **(2) POL-MATTER-NOBUDGET v2** (D-186): `Apply-DoLanePolicies.ps1 -DefinitionPath config/know-promotion.policies.json`, run 11:20:38Z.
  - Created v2 `…6312` (plan `assign-task`; next steps `add-todo`, `assign-work`).
  - Ended v1 `sprk_inforceto` 2026-10-22T00:00:00Z.
  - Current version now points at v2. The enabled flag is unchanged (True).
  - `-Verify -RequireRows`: all IN SYNC.
- **(3) D-185 role change**: `RemovePrivilegeRole` on root-BU Spaarke Console User `51c924e2-bdbe-f111-a05b-3833c5e9614d`.
  - Removed `prvReadsprk_Policy` (`eda1df58-…`, HTTP 204, 11:21:51Z) and `prvReadsprk_PolicyVersion` (`22640ee9-…`, HTTP 204, 11:21:53Z).
  - Read-back: 21 → 19 privileges. Neither Read is left; `prvReadsprk_Signal` is kept.
  - A first attempt at 11:21:18Z used the wrong payload (`PrivilegeId`). It was rejected with 0x80048d19, and the read-back confirmed nothing had changed. The working payload is `{"Privilege":{"@odata.type":"Microsoft.Dynamics.CRM.privilege","privilegeid":…}}`, as in `Remove-ConsoleSignalWriteAndDecisionRecordCreate.ps1:307`.
  - Spaarke Basic User was not changed (see security-roles §19).
- **uac-r2 server gate: PASS** (window 11:18:00–11:51:05Z):
  - 77 requests: 0 403, 0 5xx; the 9 failures are all 401 probes.
  - 0 `worklist_unavailable`, 0 `signal_unreadable`.
  - No new exception class (one restart-class Redis lease exception at 11:21:20Z).
  - Not covered: no traffic reached the worklist or the inquiry routes, so the owner's browser checks cover them.
  - Evidence: `projects/unified-access-control-r2/notes/deploy-gate-2026-10-23-58ffc37da3.md`.

## 2026-10-23 — Spaarke Core User: AppendTo on `sprk_eventtype_ref` (D-110, task 159), spaarkedev1

- 00:32:20Z, root role `75e1b627-…`: `prvAppendTosprk_EventType_Ref` at Global. Read-back 784 → 785, exact. Details in security-roles.md §18. uac-r2 told on #1355.

## 2026-10-23 — dev BFF `1fa0cf5cb8` (R3): ontology smoke; 157 now on dev

- **Deploy:** R3 deployed master `1fa0cf5cb8` (ProductVersion `1.0.0+1fa0cf5cb88443d9a4279851e558495d71ec9169`); started ~23:58Z on 2026-10-22, new version first read at 00:00:36Z. Its ancestors include R3 077 `283466d40` and our **157** `aa3dc49897` (Duplicate earlier-invoice withholding is now live, latent until Duplicate Signals exist). Changes since our OK'd `f5bf481185`: field-mapping #2282 only (we reviewed it; fixes #2281).
- **Smoke (~00:10Z):** PASS. Validate on Secure PAT-908068 (`5d46df87`) returns `valid:false` / `scope_matter_secured`; on Standard REAL-2026-123456.01 (`2444af6d`) it returns `valid:true`.
- **Logs since 00:00:00Z:** no 503xx trace at Warning or above, no Signals/Ontology/Gate exceptions. The last `[SIGNAL-EVALUATOR]` heartbeat is 2026-10-22 02:30:29Z (succeeded); tonight's run is the next check.
- **Owner check (via R3):** Create Work Assignment from a matter with `sprk_assignedattorney1` copies the attorney with no warning (#2281).

## 2026-10-22 — dev BFF `1283c41817` (R3): ontology smoke

- **Deploy:** R3 deployed master `1283c41817` (ProductVersion `1.0.0+1283c41817`, read via Kudu VFS) after `a7b956fb55`, with 25 commits between them. It contains R3 077 `283466d40` and no change under `Services/Signals`, `Services/Ontology` or `DecisionRecordWriter.cs`. It does **not** yet contain 157 (`aa3dc49897`, #2233), so 157 reaches dev with the next R3 deploy from master.
- **Smoke (re-run 2026-10-22 ~13:35Z):** PASS. Validate on Secure PAT-908068 (`5d46df87`) returns `valid:false` / `scope_matter_secured`; on Standard REAL-2026-123456.01 (`2444af6d`) it returns `valid:true`.
- **Logs since 12:50:18Z:** no 503xx trace at Warning or above, and no Signals/Ontology/Gate exceptions. The last `[SIGNAL-EVALUATOR]` heartbeat is still the 02:30:29Z run (succeeded; policies=6 hits=142); tonight's run is the next check.

## 2026-10-22 — ebilling 088 Stored File schema on dev (owner OK in ebilling's session + ontology PASS)

- **Run 1** stopped with exit 1. The field-permission service refused the BigInt `sprk_sourceversion` (0x8004f508), even though CanBeSecuredFor* read true. The script reverted that column to unsecured. The owner approved a retype: `sprk_sourceversion` becomes text 20 and `sprk_sizebytes` becomes Integer 0..2147483647. ebilling deleted the two empty BigInt columns; our read-only check confirmed 0 rows and 0 permission rows on them, and that 9 columns were already secured, including `sprk_filestatus`.
- **Retype scoped check:** PASS on `44c1ef6a2d..5af7f6c879`. We reproduced the Pester runs (088 74/74, 080 85/85; Pester 3.4 under pwsh 7). -WhatIf creates 13, changes 6, skips 31.
- **Run 2:** exit 0 (13 created, 6 changed, 31 skipped). **Run 3:** idempotent (39 skipped, no publish).
- **Our independent read-back:**
  - `sprk_storedfile` (SchemaName `sprk_StoredFile`, UserOwned, audited, primary `sprk_name` 400 required, 0 rows).
  - 15 columns secured with Writers 444 / Readers 400 plus the platform's System Administrator row; `sprk_blobpath` has Writers only.
  - Marker `sprk_document.sprk_restrictedbysubmission` secured, default No, 0 Yes rows.
  - 6 relationships: RemoveLink ×4 (procedureversion, invoice, billingexport, createdbyperson) and Restrict ×2 (sourcedocument, sourcecommunication). All have NoCascade for assign/share/unshare/reparent and Related menu DoNotDisplay.
  - `sprk_invoice` statuscode 659490005 Superseded at statecode 1, 0 invoices.
