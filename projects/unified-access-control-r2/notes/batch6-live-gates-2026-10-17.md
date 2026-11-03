# Batch 6 live record, 2026-10-17

## Server-side live gates after the R3 BFF deploy (G1–G6), run 2026-10-17 02:33–03:21Z

Owner-approved. Run against spaarke-bff-dev and spaarkedev1, with the existing identities only:
- **admin:** ralph.schroeder, the default `az` login, systemuser `1d02f31c…`;
- **testuser1:** non-admin, in "Spaarke Business Unit 1", `AZURE_CONFIG_DIR=C:/tmp/az-uac-child`, systemuser `8d7bad7a…`.

Tokens: BFF `api://1e40baad…`, Dataverse `https://spaarkedev1.crm.dynamics.com`. No token, password or invite code was printed or written.

No Entra, Key Vault, app-setting, role/privilege, team-membership or tenant-config write. App settings were read by NAME only. No restart and no redeploy. No SPE container was deleted.

Raw logs are in the session scratchpad `b6/`: `b6.log` and the scripts `g1.py` … `g6d.py`, `final.py`, plus the `q*.kql` queries. Times are UTC.

**Deploy evidence.**
- **Build.** R3 deployed `d5401cf2d` (ProductVersion `1.0.0+d5401cf2d…`; it contains master `e702c3401`). `git merge-base --is-ancestor` confirms all six gate commits are in it: `b1eaecb49`, `2e4ffc654`, `0ccc75b4c`, `72b55bb6c`, `9b72ea2c8`, `77cd69a03`.
- **Deployment.** `az webapp log deployment list` shows OneDeploy `4690aa8d…`, active, 02:28:41–02:28:45Z. The one before it was 2026-10-16 14:40Z. R3 reports the window as 02:27:51–02:29:47Z, with no app-setting change.
- **App start** (App Insights `spe-insights-dev-67e2xz`, appId `6a76b012…`, role `spaarke-bff-dev`):
  - 02:29:08Z `ScheduledJobHost refreshed — 18 job(s) scheduled, 20 definition(s), 20 handler(s)`;
  - 02:29:10Z `Application started`.
- **Health.** `healthz` 200 at 02:33Z and 03:12Z.

**Role changes by other teams.** Ontology is applying role changes on spaarkedev1. None touched the identities or roles these gates use, and no role-related surprise was seen.

### Throwaway records

All created as admin. All deleted and read back **404**, or reverted, with a final sweep at 03:20:21Z.

- **matter** `bf5912d5-d8c9-f111-a05a-7c1e520a989f` "zz-1620-gate owner-unshare 20261017-0314Z" (G2): deleted 204 and read back **404** at 03:14:37Z.
  - Its shares: the GrantAccess to testuser1 and testuser1's `/share-user` to admin were both removed through `/unshare-user` (the gate itself) before the delete.
- **appnotification** `90cd02db…` for admin, "You were given access to the matter …", created by G2's `/share-user`: deleted 204.
- **team share on playbook** `4a72f99c…` "Summarize File" (G3, existing record; only its share was written): to team "Spaarke Test 1" `1c75377a…`, which has 0 members.
  - The share was granted through the route, changed three times with ModifyAccess, and removed through `POST /unshare` 200.
  - `RetrieveSharedPrincipalsAndAccess` now returns `[]`. The playbook's `modifiedon` (2026-06-04T19:52:27Z) and etag are unchanged.
  - Dataverse keeps a revoked POA row with mask 0 (`41e64f89…`, team, `accessrightsmask 0`). This is Dataverse's own artifact, not a share; see G3's finding.
- **Final sweep (03:20:21Z):**
  - 0 `zz-*` matters and 0 `zz-*` projects;
  - 0 appnotifications since 03:10Z for admin or testuser1;
  - the playbook holds 0 shares;
  - the gate-171 project `31e232ae…` is untouched (owner Secure Record Owners, `modifiedon` 2026-10-09T21:35:05Z).

### Standing fixture (#1755). Do NOT delete it or its container.

- **Project** `a9e04977-d9c9-f111-a05a-7c1e520a989f`, "UAC gate 1755 (secure) 2026-10-17".
  - Created as admin through the Web API at 03:18:47Z: unflagged, owner admin, `createdby` admin.
  - Then provisioned through `POST /api/v1/external-access/provision-project`.
- **SPE container** `b!fKbjJ_N40UGstBFox-ob3dNtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y`.
  - Display name "Secure Project — UAC gate 1755 (secure) 2026-10-17"; container type `8a6ce34c…` (Spaarke PAYGO 1).
  - Bound to the "Secure Record" business unit `d9ec0b6f…`.
- **State now:**
  - `sprk_issecure` true; owner team Secure Record Owners `6eabc7f9…`; business unit Secure Record `d9ec0b6f…`;
  - POA: admin only, `ReadAccess, WriteAccess, AppendAccess, AppendToAccess, ShareAccess` (the creator rights).
- **Business units and teams created: none.** Business units 6 → 6 and teams 15 → 15. Both the Secure Record BU and the Secure Record Owners team already existed; the response names them.

### Gate table

| # | Gate | Result | Evidence |
|---|---|---|---|
| G1 | #2021 (`b1eaecb49`): `POST /api/documents/{id}/share-link` is retired | **PASS** | As admin, 03:12:26Z: `POST /api/documents/8a3e47ce-…/share-link` and `POST /api/documents/553b8cbf-…/share-link` (random GUIDs) → **404, empty body** (route not mapped). The old build answered 500/502. With a bad token → 401.<br>Controls: `GET /api/me` → **200** (admin, oid `c74ac1af…`). `GET /api/documents/{random}` → **405**: that path is mapped for other verbs, so a 404 on POST share-link is "no endpoint", not a lookup miss.<br>This also proves the new build is serving. |
| G2 | #1620 (PR #2016 `2e4ffc654`): `/unshare-user` removes a share held by the record's owning user | **PASS** | The batch5 repro (`repro_unshare.py`, :366) exactly; it never changes the owner. Matter M `bf5912d5…` (owner and owning user: admin).<br>1. `POST /GrantAccess` testuser1 `ReadAccess, WriteAccess, ShareAccess` → 204.<br>2. testuser1 `POST /api/v1/external-access/share-user` {matter, M, admin, ViewOnly} → 200 `outcome created`, mask 1. RSPAA: testuser1 `Read, Write, Share`; admin `ReadAccess`.<br>3. **admin `POST /api/v1/external-access/unshare-user` {matter, M, systemUserId admin = the owner}** at 03:14:29Z → **200 `{"removed": true}`**. On the old build this was 500 `sdap.access.user_share.write_not_confirmed` with the share kept. RSPAA after: **admin's share gone**, testuser1's kept. The owner is still admin (`_owninguser_value` unchanged). `GET /user-shares` → 200, testuser1 only.<br>4. Non-owner control: `POST /unshare-user` {testuser1} at 03:14:33Z → **200 `removed: true`**; RSPAA `{}`. |
| G3 | #994 (PR #2013 `0ccc75b4c`): playbook sharing reads ShareAccess (262144), not AssignAccess (524288) | **PASS** | Projection: `GET /api/ai/playbooks/{id}/sharing` → `sharedWithTeams[].accessRights` (`PlaybookAccessRights`: Read 1, Write 2, Share 4), from `PlaybookSharingService.MapFromDataverseAccessRights` over team POA rows. The projection reads team shares only, so a team was used, not testuser1.<br>Playbook `4a72f99c…` "Summarize File" (admin-owned, not public, no shares before: `sharingLevel 0`, teams []). Team "Spaarke Test 1" `1c75377a…` (0 members).<br>• `POST /api/ai/playbooks/{id}/share` {teamIds [T], accessRights 5} → 200. POA `ReadAccess, ShareAccess` → `/sharing` **accessRights 5 (Read+Share)**.<br>• `ModifyAccess` → `ReadAccess, AssignAccess` → **1 (Read; no Share)**.<br>• `ModifyAccess` → `AssignAccess` only (524288) → **0 (not shareable)**. The old code read this as Share.<br>• `ModifyAccess` → `ShareAccess` only (262144) → **4 (Share)**. The old code read this as no Share.<br>• `POST /unshare` {teamIds [T]} → 200.<br>Times 03:15:59–03:16:20Z. |
| G4 | #2002 (PR #2029 `72b55bb6c`): the "carries no core-ancestor stamp" no-root line is Debug on read-only resolution | **FAIL**: the fix did not reach the line's source. Filed **#2049**. | App Insights, `traces` where message has "carries no core-ancestor stamp", role `spaarke-bff-dev`:<br>• **24 h before the deploy** (2026-10-16 02:28:40Z → 2026-10-17 02:28:40Z): **4,912 at Warning** (sev 2).<br>• **Since 02:29Z** (to 03:10Z): **155 at Warning**: 18 per run (todo 11, event 3, analysis 2, invoice 1), 47 by 02:35Z.<br>• The job ran: `core-ancestor-stamp-reconciliation` (*/5) every 5 min from 02:30:00Z (02:30 → `success=True … duration=10991ms`; 11 successful runs by 03:21Z; heartbeat `mode=write scanned=136`).<br>• **Attribution:** every Warning, before and after (4,910 of 4,912 before; all after), falls between that job's `Dispatching` and `completed` lines. That job calls `ResolveStampsAsync(intermediate, intermediateId, ct)` with the default **Write** intent at `CoreAncestorStampReconciliationJob.cs:469`. PR #2029 changed only `TodoAssigneeOwnerReconciliationJob.cs:657` and `DocumentIndexParentResolver.cs:338`.<br>• #2002's text ("the 144 reconciliation scan") names this job. The rate is unchanged, about 5k a day. |
| G5 | #1363 (PR #2017 `9b72ea2c8`): demo expiry is the `demo-expiration` IScheduledJob (`0 0 * * *`) on ScheduledJobHost, inside the demo-provisioning gate | **PASS (registration); run PENDING** | `GET /api/admin/jobs` (SystemAdmin, read-only) 03:13:11Z → 200; 20 jobs, including **`demo-expiration`, `0 0 * * *`, enabled**. `GET /api/admin/jobs/demo-expiration/status` → `lastRunStartedOn null`, **`nextScheduledOn 2026-10-18T00:00:00+00:00`**.<br>The job is registered only inside `if (IsDemoProvisioningEnabled(configuration))` (`RegistrationModule.cs:79-90`), so **the gate is on in dev**. Setting NAMES present: `DemoProvisioning__AccountDomain`, `__AdminNotificationEmails__0`, `__DemoUsersGroupId`, `__Licenses__FabricFreeSkuId`, `__Licenses__PowerAppsPlan2TrialSkuId`, `__Licenses__PowerAutomateFreeSkuId`. No values were read.<br>Start-up: 02:29:08Z `ScheduledJobHost refreshed — 18 job(s) scheduled, 20 definition(s), 20 handler(s)`.<br>**Old timer silent:** `DemoExpirationService` lines over 30 days: last `started` 2026-10-16 14:40:52Z (the previous build) and last run 2026-10-17 00:00:00–01Z ("0 expired, 0 warnings"). **0 lines after 02:28:40Z.** |
| G6 | #1755 (PR #2024 `77cd69a03`): secure provisioning creates the container, activates it explicitly, binds it and records it | **PASS** | **(a)** admin `POST /api/v1/external-access/provision-project` {recordType project, recordId `a9e04977…`} at 03:18:47Z → **200** in 16.9 s: `businessUnitName "Secure Record"`, `ownerTeamName "Secure Record Owners"`, `speContainerId b!fKbjJ_N4…`, `sharedToCreatorSystemUserId` admin, `resumed false`, `children Completed 0/0`, `filedRecords Completed 0`.<br>**(b)** Dataverse read-back: `sprk_containerid = b!fKbjJ_N40UGstBFox-ob3dNtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y`, `sprk_issecure` true, owner team `6eabc7f9…`.<br>**(c)** App Insights, in order:<br>• 03:18:58.356Z `Successfully created SPE container b!fKbjJ_N4… with display name Secure Project — UAC gate 1755 …`;<br>• **03:18:58.997Z `Container b!fKbjJ_N40UGstBFox-ob3dNtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y activated and bound to business unit d9ec0b6f-80a0-f111-aaac-000d3a99d1d7`**;<br>• 03:18:59.401Z `Marked container … as owned by customer spaarke`;<br>• 03:18:59.613Z `[PROVISION] Recorded container … on project a9e04977…`;<br>• then `Provisioning complete`. No `could not be activated` line.<br>**(d)** admin `GET /api/spe/containers/{id}?configId=c3a25b9a-e81f-f111-88b2-7c1e525abd8b` (Spaarke PAYGO 1, the container's type) → **200, `status: "active"`**, containerTypeId `8a6ce34c…`, createdDateTime 03:18:55.543Z. The other config, `68f9a952…` (type `fb3817a8…`), → 404 `spe.admin.deny.container_out_of_scope`, which is expected (wrong type). |

### Side effects checked

- **Exceptions and 5xx, 03:12–03:21Z.** No BFF request answered 5xx. The 2 exceptions were `ODataError "Invalid hostname for this tenancy"` from `SpeContainerMembershipSync` at 03:15:02Z and 03:20:03Z: the known orphan container (#1352, #1830). That job fails on every run both before the deploy (288 of 288 in 24 h) and after it; the gates did not cause it.
- **`secure-record-isolation-census`** reports `success=False` on every run before (96) and after (4). The findings are `[SECURE-CENSUS] HumanPrincipalReachesSecureBusinessUnit` for demo users holding Deep read at root BU `06fbf21c…`. This is pre-existing and known (task-144 notes, clause 1). The fixture adds no human principal.
- **G2.** M's shares were all removed before the delete. The owner never changed. The one notification was deleted.
- **G3.** The playbook's own fields are unchanged and it holds no share now. No team membership was touched.
- **G6.** No business unit or team was created. The fixture's children pass and filed-records pass examined 0 records. The gate-171 project `31e232ae…` was only read.
- The trailing `[DATAVERSE] Updated sprk_project a9e04977… with 1 fields` at 03:19:05.79Z (`modifiedon` 03:19:05Z) came after the 200 response. It is consistent with the post-provision stamp, and no field the gate reads changed.
- **Before starting:** a sweep found no `zz-*` record and no "gate 1755" project from the earlier, cut-off run (only read-only queries had run before the cut), so there was nothing to clean up.

### PENDING

**G5, the first scheduled run of `demo-expiration`, 2026-10-18 00:00Z.** Run after 00:15Z:

```kql
traces
| where timestamp between (datetime(2026-10-17T23:59:00Z) .. datetime(2026-10-18T00:30:00Z))
| where cloud_RoleName == "spaarke-bff-dev"
| where message has "'demo-expiration'" or message has "[Expire]" or message has "DemoExpirationService"
| project timestamp, severityLevel, message
| order by timestamp asc
```

Also run `GET /api/admin/jobs/demo-expiration/status` as admin.

**PASS when:**
- exactly one `Dispatching scheduled job 'demo-expiration' …` and one `scheduled job 'demo-expiration' completed … success=True`;
- **0** `DemoExpirationService` lines;
- the status route shows `lastRunStartedOn` 2026-10-18T00:00Z and `lastRunStatus` Succeeded.

### Found and filed

- **#2049 (new, F2, Small): the #2002 fix missed its source.**
  - `CoreAncestorStampReconciliationJob.cs:469` still calls `ResolveStampsAsync` with the default Write intent.
  - So the no-root Warning (`CoreAncestorResolver.cs:695-697`) still fires about 18 times per 5-minute run, about 5k a day.
  - G4 can be re-run once that call site passes `CoreAncestorIntent.ReadOnly`, or once the PR explains why it must stay Write.
- **#1435 (open, reproduced live):** `/sharing` reports a revoked team as Read.
  - After `POST /unshare`, Dataverse keeps the POA row at `accessrightsmask 0`, and `PlaybookSharingService.cs:391` maps mask 0 to Read (`== 0 ? 1`).
  - So `/sharing` and the unshare response still list the team at `accessRights 1`, with `sharingLevel 1`.
  - Display only: `UserHasSharedAccessAsync` has no caller outside the service.
- **#1436 (open, reproduced live):** every team shows as "Unknown Team".
  - `BaseAddress` is `…/api/data/v9.2` with no trailing slash (`PlaybookSharingService.cs:54-56`), so the relative `teams(…)` at :415 resolves outside `/v9.2`.
  - Admin reads the same team's name directly with 200.

**Verdicts:** G1 PASS · G2 PASS · G3 PASS · G4 **FAIL** (#2049) · G5 PASS for registration, first run PASS (00:00:00Z, success=True) · G6 PASS (fixture kept).
### G5 first run — RESULT (checked 2026-10-18 03:40Z)

**PASS.** App Insights `spe-insights-dev-67e2xz` (role `spaarke-bff-dev`), the KQL above over 2026-10-17 23:50Z – 2026-10-18 00:40Z:
- 00:00:00.017Z `Dispatching scheduled job 'demo-expiration' runId=b870265c-… attempt=1/3`;
- 00:00:00.461Z `scheduled job 'demo-expiration' completed runId=b870265c-… success=True processedItems=0 duration=440ms attempt=1`;
- **0** `DemoExpirationService` lines and 0 `[Expire]` lines. The old timer is gone.

**The status-route criterion cannot hold after a redeploy, and that is not a defect of #1363.** `GET /api/admin/jobs/demo-expiration/status` at 03:38Z → 200, `enabled true`, `nextScheduledOn 2026-10-19T00:00Z`, but `lastRunStartedOn null`, `recentRuns []`. Why:
- R3 redeployed the dev BFF at 03:32:53–03:34:49Z (ProductVersion `+ebc74b331`).
- Run history lives in `Spaarke.Scheduling/InMemoryBackgroundJobStore.cs`, so a restart clears it. `GET /api/admin/jobs` shows the same for every daily job that ran before the restart (02:00, 02:30, 03:30 → `None`), while the 2- and 5-minute jobs show runs from 03:35Z on.

Gotcha for later gates: a job-status check must be read before any redeploy, or taken from App Insights. Durable run history is a ScheduledJobHost design choice, not a UAC defect.

**#1363 closes:** registration (G5) plus the first scheduled run both PASS.
