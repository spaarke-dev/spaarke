# Batch 7 live record, 2026-10-17

## Server-side live gates for #1576 (PR #2025) and 099a (PR #2035), run 2026-10-17 06:08–06:26Z

Run against spaarke-bff-dev and spaarkedev1, with the existing identities only:
- **admin:** ralph.schroeder, the default `az` login, oid `c74ac1af…`, systemuser `1d02f31c…`;
- **testuser1:** non-admin, in the child BU "Spaarke Business Unit 1", `AZURE_CONFIG_DIR=C:/tmp/az-uac-child`, oid `bcde7809…`, systemuser `8d7bad7a…`. Its roles are Spaarke Basic User, Spaarke Core User and Spaarke Office Add In User. It is in one team, the BU default team.

Tokens: BFF `api://1e40baad…/.default`; Dataverse `https://spaarkedev1.crm.dynamics.com` (admin; testuser1 for one read-only symptom check); App Insights `https://api.applicationinsights.io`; ARM for Kudu VFS reads. No token, password or invite code was printed or written.

**Read-only.** The only calls with an effect were the test users' BFF GETs that the gates themselves make. No Dataverse, Entra, role, share, Key Vault or app-setting write. No publish, restart or redeploy. No vault was read.

Raw log and scripts are in the session scratchpad `b7/`: `b7.log`; the scripts `a0.py`, `a1.py`, `d0.py`, `d1.py`, `b0.py` … `b3.py`, `gb.py`, `p0.py` … `p6.py`; the `q*.kql` queries; and `pcf/` (the published and unpublished bundles and the import-job XML). Times are UTC.

**Deploy evidence.**
- **Build.** The `Sprk.Bff.Api.dll` read through Kudu VFS (`/api/vfs/site/wwwroot/`) has ProductVersion **`1.0.0+28720f9976c0934b0109a160255d1d0156e31f63`**.
  - `git merge-base --is-ancestor` puts both `bed14dd67` (#2025) and `1d435e695` (#2035) in it.
  - Neither commit is in the previous build, `d5401cf2d`, so the "before" windows below are a valid baseline.
- **Deployment.** OneDeploy `5bb9b2a5…`, active, 06:02:44–06:02:48Z. The deployment before it was `4690aa8d…` at 02:28Z (batch 6).
- **App start.** One instance (`39ab6951…`):
  - 06:03:10Z `ScheduledJobHost refreshed — 18 job(s) scheduled, 20 definition(s), 20 handler(s)`;
  - 06:03:12Z `Application started`.
  - `healthz` 200.
- **App Insights sampling.** 16 of the 24 document requests in A(d) reached `requests`, so ingestion is sampled. Counts below are counts of sampled telemetry.

### Gate table

| # | Gate | Result | Evidence |
|---|---|---|---|
| A(b) | #1576: a disabled systemuser never resolves; an enabled user still works | **Caller leg NOT AVAILABLE. Resolver evidence: PASS** | **Why the caller leg is not available.** Dev has 5 disabled systemusers with an Entra oid: Eyal Iffergan `834a41ae…`, Final Test `a151efdd…`, Admin Development `2a5c4fc5…`, Spaarke DMS-SPE Dev 1 `5a80bf13…` and E2E Test `9d9589bf…`. None is a test identity this session holds credentials for. No user was disabled (per the brief). App Insights over 30 days (`qa1.kql`) shows **no BFF request from any of those oids, before or after the deploy**. The only hits are background `[ID-LINK-RECON]` job lines, the last on 2026-10-14. So a disabled caller cannot be observed live.<br>**Deployed code.** The deployed `Spaarke.Dataverse.dll` (Kudu VFS, read-only) contains exactly one oid lookup literal: `systemusers?$filter=azureactivedirectoryobjectid eq '{oid}' and isdisabled eq false&$select=systemuserid`, followed by the per-request `Bearer` header. `Sprk.Bff.Api.dll` holds the same lookup, and 4 `isdisabled eq false` literals in all.<br>**Replay of that exact query** (read-only, as admin; `a1.py`):<br>• all **5 disabled oids → 0 rows** with the deployed filter, against 1 row (`isdisabled: true`) without it, which is what the old code resolved;<br>• admin and testuser1 → 1 row each, with or without the filter.<br>So the deployed lookup answers "no matching systemuser" for a disabled oid, which `GetUserAccessAsync` turns into None. The enabled users still resolve.<br>**Enabled controls (live).** `GET /api/me`: admin → 200 (oid `c74ac1af…`); testuser1 → 200 (oid `bcde7809…`). Every A(d) request also resolved its caller (below).<br>**Post-deploy log check (`qh4.kql`):** 0 lines of `Could not find Dataverse user for Azure AD OID`, `Failed to query team memberships`, `Failed to query user roles`, `SENDER_NOT_RESOLVED`, `no-matching-systemuser`, `lookup-failed`, `Failed to fetch access data for user` or `ActingUserUnresolved`, both in the 24 h before the deploy and since. |
| A(d) | #1576: on a user request, Dataverse calls carry that request's own token, not a cached cross-request identity | **PASS** | **Requests** (`d1.py`, 06:11:27–06:11:32Z). 24 `GET /api/v1/documents/{id}` requests, shuffled and run on 8 concurrent workers, mixing admin and testuser1. Each user/document pair ran once, so no snapshot was cached. The route is gated by `DocumentAuthorizationFilter("read")` → `AuthorizationService` → `DataverseAccessDataSource.GetUserAccessAsync`.<br>• 6 documents on which testuser1's RetrievePrincipalAccess is None (`2cf67ed0…`, `366737fe…`, `5d373e39…`, `c16b28c2…`, `c7072969…`, `d550a87a…`): admin → **200 × 6**; testuser1 → **403 × 6**, `sdap.access.deny.insufficient_rights`.<br>• 6 documents on which testuser1 has Read and above (`6198b30c…`, `6798b30c…`, `8d11f769…`, `9e5b0836…`, `e1004513…`, `e7ac58df…`): both users → **200 × 6**.<br>• **0 mismatches.** A cross-request token would have given testuser1 a 200 on a None document, or admin a 403.<br>**Traces (`qd1.kql`), joined on operation_Id.** All 16 sampled operations log `[UAC-DIAG] GetUserAccessAsync START … UsingOBO=True` and then `Access snapshot retrieved for user …`. In every one, **the snapshot's oid is the request's caller**, and the counts are that caller's own:<br>• admin: `Teams=5, Roles=5`, `AccessRights=Read, Write, Delete, Create, Append, AppendTo, Share`;<br>• testuser1: `Teams=1, Roles=3`, which matches its 1 team and 3 roles in Dataverse; `None` on the None documents; `Faulted=False` throughout.<br>**Dependencies (`qd4.kql`)**, Dataverse HTTP calls in those operations, by caller:<br>• admin, 10 operations: systemusers lookup, RetrievePrincipalAccess, team and role reads → all **200**.<br>• testuser1 on readable documents, 3 operations: all **200**.<br>• testuser1 on None documents, 3 operations: lookup, team and role reads 200; **RetrievePrincipalAccess 403 and `GET sprk_documents(…)` 403 from Dataverse itself**.<br>Interleaved with admin's 200s, Dataverse's own 403 shows that those calls carried testuser1's OBO token and not admin's. The token itself is not observable; the PR names no other live signal.<br>**Health since the deploy** (06:03:13Z → 06:24:15Z; `qh1`, `qh3`, `qh5`):<br>• 52 requests, **0 × 5xx**. The 4xx are all this run's: 3 doc 403s and 2 record-grants 403s, plus 401s on `/`, `/robots933456.txt` and the gate's no-token call.<br>• **Exceptions: only 4 × `ODataError "Invalid hostname for this tenancy"`**: the known orphan container (#1352, #1830), 289 in the 24 h before. No new exception type. |
| B+ | 099a positive: a child-BU Spaarke Core User with Write and Share on a record that has a root-owned grant G gets 200 with G listed, with `expiresDate` and `isExpired` | **PASS** | Found read-only (`b1.py`). Dev has **62 active grants**: 61 owned in the root BU "Spaarke" (43 by `# mi-bff-api-dev`, 14 by `SDAP-BFF-SPE-API`, 5 by admin) and 1 in "Spaarke Demo". testuser1 holds Read, Write, Append, AppendTo, Create, Delete, **Share** and Assign on 22 of the 32 grant roots.<br>**The pre-fix symptom, live.** testuser1 reading `sprk_externalrecordaccesses` directly as itself (what TrackingFieldTrio ≤1.0.47 did via `context.webAPI`) → **200 with 0 rows** on matter `2444af6d…` and on project `e42aa40d…`. Admin reads **5** and **1** rows.<br>**The route** (`gb.py`, 06:14:24Z), as testuser1:<br>• `GET /api/v1/external-access/record-grants?recordType=matter&recordId=2444af6d-e1f2-f011-8406-7ced8d1dc988` → **200**, `truncated: false`, **5 grants**: Eyal Iffergan `a53d2081…`, Jonathan Smith `b03d2081…`, Morrison Foerster LLP `60995b33…` (organization), Sarah Chen `abf5560d…` and CIAM Test User `0452ab4b…`. All are root-owned, each with `expiresDate` (2026-12-10 … 2027-01-11), `isExpired: false`, `grantedDate` and `accessLevel`.<br>• Each row's keys: `accessLevel, accessRecordId, contactId, contactName, expiresDate, grantedByContactName, grantedByName, grantedDate, isExpired, organizationId, organizationName`.<br>• `GET …?recordType=project&recordId=e42aa40d-361b-f111-88b3-7ced8d1dc988` → **200**, 1 grant: CIAM Test User `5e59257b…`, root BU, owner mi-bff-api-dev, `expiresDate` 2027-01-04, `isExpired: false`.<br>Admin gets the identical lists on both. `/can-manage-access` for testuser1 on the matter → 200, `canManageAccess: true`.<br>**Not shown live:** `isExpired: true`. No active grant on dev is lapsed or unbounded (`b3.py`: 0 rows with `statecode 0` and `sprk_expiresdate` null or before 2026-10-17). Making one would be a write. It is covered offline by `RecordGrantsEndpointTests` (the unbounded legacy row is listed as expired). |
| B−1 | 099a negative: a caller without Write gets 403 | **PASS** | No record on dev gives testuser1 Read without Write, or Write without Share. Scan of the 80 most recently modified work assignments, projects and matters (`b2.py`): testuser1 has either full W+S or None. So "without Write" was run on records where testuser1 has None.<br>• testuser1 on work assignment `c08ed03e-1a1e-f111-88b3-7c1e520aa4df` (3 grants) → **403** `sdap.access.deny.delegation_write_required`: "To change who else can access this record you need Write access to it and the Share privilege on its table (set in your security role)."<br>• testuser1 on project `edb87d10-2ac3-f111-a05c-6045bdfe7b25` (1 grant) → **403**, same reason code.<br>• Controls: admin on the same two records → **200**, with 3 and 1 grants.<br>• No token → **401**.<br>• The Read-only twin (`delegation_write_required` with Read held) and Write-without-Share (`delegation_share_required`) are covered offline in `RecordGrantsEndpointTests`. |
| B−2 | 099a negative: 503 on a read fault | **Unit-tested only (by design)** | No fault was injected live. Covered by `RecordGrantsEndpointTests` (a read fault gives 503 with `sdap.access.record_grants.read_failed` and its detail) and `AccessGrantModal.currentAccessRead.test.tsx`. There were 0 `[RECORD-GRANTS]` log lines since the deploy. |
| B-PCF | TrackingFieldTrio 1.0.50 is the live control, and its bundle no longer reads `sprk_externalrecordaccess` via `context.webAPI` | **FAIL: imported, but never published. Dev still serves 1.0.47.** | **Import.** `importjobs`: one import today, **TrackingFieldTrioSolution, 06:05:02–06:05:29Z**, by Ralph Schroeder, `succeeded="success"`, solution manifest `<Version>1.0.50</Version>` (job `22a42dbe…`). The `solutions` row reads 1.0.50.<br>**Unpublished copy** (`webresourceset(93d95d1a…)/RetrieveUnpublished()`), `cc_Spaarke.Controls.TrackingFieldTrio/bundle.js`, modified 06:05:16Z:<br>• 1,061,698 B, sha256 **`da47e49b…`**, version string 1.0.50;<br>• **0** `retrieveMultipleRecords("sprk_externalrecordaccess"`; 1 `record-grants`.<br>It is byte-identical to master's `src/client/pcf/TrackingFieldTrio/Solution/Controls/…/bundle.js` and to the bundle in `C:\wt099a\…\bin\TrackingFieldTrioSolution_v1.0.50.zip`. So the import carried the right code.<br>**Published copy** (plain `GET webresourceset(…)`, and the served URL `https://spaarkedev1.crm.dynamics.com/WebResources/cc_Spaarke.Controls.TrackingFieldTrio/bundle.js` → 200):<br>• 1,056,201 B, sha256 **`827d0110…`**, version string **1.0.47**;<br>• still has `try{t=yield this.context.webAPI.retrieveMultipleRecords("sprk_externalrecordaccess",i)}catch(e){return[]}`; 0 `record-grants`.<br>It is byte-identical to the 1.0.47 bundle (`74dc51c60`), and to master's SpaarkeMaster copy (099a K2).<br>**Control row.** `customcontrols` `sprk_Spaarke.Controls.TrackingFieldTrio` (`eb6835e1…`): `version` **1.0.47**; the manifest's `<control … version="1.0.47">`; `modifiedon` 2026-10-10. The only component layer is "Active", and it holds 1.0.47.<br>Re-checked at 06:25:37Z: unchanged.<br>**Cause.** This is the documented pcf-deploy trap (`.claude/skills/pcf-deploy/SKILL.md:414`): an import rewrites `bundle.js`, but the served copy stays old until a `PublishXml` of the `cc_…` web resources and the control. So every user still gets the old Current Access read: "No active grants" for a child-BU user on a record that has grants. |

### What the main session needs to do

**B-PCF: publish the TrackingFieldTrio import on spaarkedev1.** This is a Dataverse write, so it was not done here.
- Publish the two web resources `cc_Spaarke.Controls.TrackingFieldTrio/bundle.js` (`93d95d1a-5682-41cf-9f61-ca65933c2662`) and `…/styles.css` (`f859e50a-8b55-4114-bda5-3d745638853b`), and the custom control (or run a PublishAllXml).
- Then re-run `b7/p5.py` and `b7/p6.py`.
- **PASS when:** the published and served bundles have sha256 `da47e49b41468f703a2b88ba0218d2dc825a5efb4409149238ca7c362bb32d67`, 0 `retrieveMultipleRecords("sprk_externalrecordaccess"`, and 1 `record-grants`.
- Per the skill, `customcontrols.version` may keep reading 1.0.47 after a publish; the bundle hash is the test.
- After that, the owner UI check (a child-BU user opens Manage Access on matter `2444af6d…` and sees the 5 grants in Current Access) is meaningful.
- Whichever step imported 1.0.50 at 06:05Z did not publish it, and no log here says which tool ran. If it was `Import-SolutionScoped.ps1`, its publish step should be checked; compare #1591.

### KQL used (App Insights `spe-insights-dev-67e2xz`, appId `6a76b012…`)

Disabled oids in BFF traffic (`qa1.kql`):
```kql
let oids = dynamic(["8fbf7c84-c694-4f14-ab13-01bfca7202fe","e7549564-a1ef-45b1-896c-f46eab25a7bc","7717a2b7-2ffb-427a-8641-1ca7ad66e51f","4003c822-ff87-42ee-87e0-44f5eece5944","551cad28-75c0-40d3-ae5a-47f29152b4e4"]);
let sus = dynamic(["834a41ae-b278-f011-b4cc-6045bdd8b757","a151efdd-2a30-f111-88b5-7c1e520aa4df","2a5c4fc5-7a99-f011-bbd3-7c1e5217cd7c","5a80bf13-789d-f011-bbd3-7c1e5217cd7c","9d9589bf-2e30-f111-88b5-7ced8d1dc988"]);
union traces, requests, exceptions
| where timestamp > ago(30d) and cloud_RoleName == "spaarke-bff-dev"
| extend txt = strcat(message, " ", tostring(customDimensions), " ", user_AuthenticatedId, " ", url)
| where txt has_any (oids) or txt has_any (sus)
| extend win = iff(timestamp < datetime(2026-10-17T06:03:00Z), "A_before", "B_after")
| summarize n=count(), t1=max(timestamp) by win, itemType
```

Snapshot identity per request (`qd1.kql`):
```kql
let t0 = datetime(2026-10-17T06:11:26Z); let t1 = datetime(2026-10-17T06:11:34Z);
let reqs = requests | where timestamp between (t0 .. t1) and cloud_RoleName == "spaarke-bff-dev" and url has "/api/v1/documents/"
 | project operation_Id, resultCode, doc=extract("documents/([0-9a-f-]+)",1,url);
traces | where timestamp between (t0 .. t1) and cloud_RoleName == "spaarke-bff-dev"
| where message startswith "[UAC-DIAG] GetUserAccessAsync START" or message startswith "Access snapshot retrieved"
| extend oid = extract("(?:AzureAdOid=|for user )([0-9a-f-]{36})", 1, message), obo = extract("UsingOBO=([A-Za-z]+)",1,message),
         teams=extract("Teams=([0-9]+)",1,message), roles=extract("Roles=([0-9]+)",1,message)
| join kind=inner reqs on operation_Id
| summarize start_oid=anyif(oid, message startswith "[UAC"), obo=anyif(obo, message startswith "[UAC"),
            snap_oid=anyif(oid, message startswith "Access"), teams=anyif(teams, message startswith "Access"), roles=anyif(roles, message startswith "Access")
  by operation_Id, doc, resultCode
```

Dataverse calls per caller (`qd4.kql`):
```kql
let t0 = datetime(2026-10-17T06:11:26Z); let t1 = datetime(2026-10-17T06:11:40Z);
let snaps = traces | where timestamp between (t0 .. t1) and message startswith "Access snapshot retrieved" | project operation_Id, snapOid=extract("for user ([0-9a-f-]{36})",1,message);
let reqs = requests | where timestamp between (t0 .. t1) and cloud_RoleName == "spaarke-bff-dev" and url has "/api/v1/documents/" | project operation_Id, reqCode=resultCode;
dependencies | where timestamp between (t0 .. t1) and cloud_RoleName == "spaarke-bff-dev" and target has "spaarkedev1.crm.dynamics.com" and type == "HTTP"
| join kind=inner reqs on operation_Id | join kind=leftouter snaps on operation_Id
| extend caller = case(snapOid startswith "c74ac1af", "admin", snapOid startswith "bcde7809", "tu1", "no-snapshot")
| extend call = case(name has "RetrievePrincipalAccess" or data has "RetrievePrincipalAccess", "RPA", name has "teammembership" or data has "teammembership", "teams",
                     name has "systemuserroles" or data has "systemuserroles", "roles", name has "/systemusers", "systemusers*", name has "sprk_documents", "doc-read", substring(name,0,50))
| summarize n=count(), codes=make_set(resultCode) by caller, reqCode, call
```

Health since the deploy (`qh1`, `qh3`, `qh4`):
```kql
requests | where timestamp > datetime(2026-10-17T06:03:13Z) and cloud_RoleName == "spaarke-bff-dev"
| summarize n=count(), n5xx=countif(toint(resultCode) >= 500), n4xx=countif(toint(resultCode) between (400 .. 499))

exceptions | where timestamp > datetime(2026-10-16T06:03:13Z) and cloud_RoleName == "spaarke-bff-dev"
| extend win = iff(timestamp < datetime(2026-10-17T06:03:13Z), "A_24h_before", "B_after_deploy")
| summarize n=count(), t1=max(timestamp), msg=any(substring(outerMessage,0,140)) by win, type

traces | where timestamp > datetime(2026-10-16T06:03:13Z) and cloud_RoleName == "spaarke-bff-dev"
| where message has_any ("Could not find Dataverse user for Azure AD OID", "Failed to query team memberships", "Failed to query user roles", "SENDER_NOT_RESOLVED",
                         "no-matching-systemuser", "lookup-failed", "[RECORD-GRANTS]", "Failed to fetch access data for user", "ActingUserUnresolved")
| extend win = iff(timestamp < datetime(2026-10-17T06:03:13Z), "A_24h_before", "B_after_deploy")
| summarize n=count() by win, k=substring(message,0,45)
```

### Side effects

- **None written.** Every Dataverse call was a GET or a read function (`RetrievePrincipalAccess`, `RetrieveUnpublished`). Kudu was used only for VFS file reads of the two DLLs.
- The test users' BFF calls were all GETs.
- No grant, share, role or record was created or changed. No invite code was involved.

**Verdicts:**
- **A(b):** live caller NOT AVAILABLE (no disabled identity is held, and none calls the BFF). Deployed-filter and replay evidence PASS; enabled users PASS.
- **A(d):** PASS (0 × 5xx; no new exceptions).
- **B+:** PASS.
- **B−1:** PASS.
- **B−2:** unit-tested only.
- **B-PCF:** **FAIL.** 1.0.50 is imported but not published, so dev serves the 1.0.47 bundle with the `context.webAPI` grant read. It needs a publish; then re-check the hash.

## B PCF bundle: remediated 06:28Z, now PASS
- **Cause:** the 06:05Z import was a plain `pac solution import` with no scoped publish (main-session miss; the pcf-deploy skill says to use `Import-SolutionScoped.ps1`).
- **Remediation:** `scripts/Import-SolutionScoped.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -SolutionUniqueName TrackingFieldTrioSolution -PublishOnly`. `-PlanOnly` first showed exactly 2 web resources (93d95d1a bundle.js, f859e50a styles.css): 0 entities, no tenant-wide publish (D-83).
- **Re-verified with b7/p5.py and p6.py:**

  | Copy | sha256 | Detail |
  |---|---|---|
  | Published | `da47e49b41468f703a2b88ba0218d2dc825a5efb4409149238ca7c362bb32d67` | |
  | Served `/WebResources/cc_Spaarke.Controls.TrackingFieldTrio/bundle.js` | same | 200, 1,061,698 B, version 1.0.50, 0 `context.webAPI` grant reads, `record-grants` ×1 |

  Both match master's bundle. **PASS.**
