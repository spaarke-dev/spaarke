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

## #2049 / #2002 re-gate (one hour after deploy): PASS
- **Window:** 06:03:49Z → 07:03:49Z, App Insights `spe-insights-dev-67e2xz` (appId 6a76b012…), `cloud_RoleName == "spaarke-bff-dev"`.
- **Results:**
  - "carries no core-ancestor stamp": 187 lines, **0 at severity ≥ 2 (Warning)**;
  - `core-ancestor-stamp-reconciliation`: 11 "Dispatching", 11 "success=True".
- **Before** (d5401cf2d, batch 6): 18 Warnings per run.
- **Status:** PASS. Both issues were already closed (by the PR); the evidence is commented on both.

## 099c (PR #2062, merged 883e415c0): TrackingFieldTrio 1.0.54 on dev
- **Import:** `Import-SolutionScoped.ps1 -ZipPath …TrackingFieldTrioSolution_v1.0.54.zip` at about 07:47Z. The scoped publish covered 2 web resources; there was no tenant-wide publish. The source in C:/wt099c is byte-identical to master 883e415c0 for TrackingFieldTrio and AccessGrantModal.
- **Served bundle:** `/WebResources/cc_Spaarke.Controls.TrackingFieldTrio/bundle.js` → 200, 1,066,484 B, sha256 `39bdc0b1d74a268abd870cd37cf84a154461229dfcbc455a835b23009cf94a62`. It equals the committed bundle, version 1.0.54, with 0 `context.webAPI` grant reads and `record-grants` ×1. **PASS.**
- **Behaviour:** covered by 253 AccessGrantModal tests: the Expiration display and divergence, Apply → `/set-record-share-expiry`, every add sending the record date, the Apply/add race, and the lapsed case. The interactive check (pick a date, Apply, add a contact) writes live grant expiries, so it goes to the owner's UAT checklist rather than an agent write.

## #2094 live gate (2026-10-18)

**Scope.** Server-side gate for PR #2094 (issue #2091, master `1473b34c6`), plus a #2083 smoke. Run 22:33–22:52Z against spaarke-bff-dev and spaarkedev1, as admin only (the default `az` login, systemuser `1d02f31c…`).
- Tokens: BFF `api://1e40baad…/.default`, Dataverse, App Insights REST (appId `6a76b012…`), and ARM for Kudu VFS reads. No token or secret was printed or written.
- No Entra, Key Vault, app-setting, role or tenant-config write. No SPE container was touched. Secure fixture project `a9e04977…` was used only as a filing target; `65a3fab2…` was not touched.
- Scripts and logs: session scratchpad `gate2094/` (`g.log`, `g0.py`, `s1.py`, `s2.py`, `s4.py`, `s5.py`, `c0.py`, `c1.py`, `q_*.kql`, `ai.py`).

### Deploy evidence (fallback i): PASS
- **Build.** Kudu VFS `Sprk.Bff.Api.dll` (19,662,336 B, sha256 `7553a9e3…`) has InformationalVersion **`1.0.0+ad0e9e1a72dff82bd662b9d750a65956733dca6d`**. `git merge-base --is-ancestor 1473b34c6 ad0e9e1a7` holds. The previous build, `ebc74b331` (deployed 03:33Z), does **not** contain `1473b34c6`.
- **#2094 literals in the deployed DLL:**
  - user strings: the push's "Not written: this push moves what the record is filed under…", "would be written through", and the malformed-bind refusal "…so the record it would be filed under cannot be read and the update was not written";
  - metadata names: `RefileIsolationRead`, `ParentChangesWithClearsIn`, `MayRefile`, `ParentsUnchanged`.
- **Deployment.** OneDeploy `02002f7a…`, 22:30:01–22:30:05Z.
- **App start.** One instance, `39ab6951…`. At 22:30:24Z `ScheduledJobHost refreshed — 18 job(s) … 20 handler(s)`; at 22:30:26Z `Application started`. `healthz` 200.
- **Not a sha source.** App Insights `application_Version` is empty, and `/status` returns a fixed `1.0.2`.

### Can the four changed writers be reached for a positive re-file? No, not without new config, so none was created.

| Writer | Route | Why no positive re-file |
|---|---|---|
| Field-mapping push (`FieldMappingEndpoints`) | `POST /api/v1/field-mappings/push` | Dev has 4 profiles (`sprk_matter` → event, invoice, report card, work assignment). Every rule maps an `sprk_assigned*` contact or organization lookup (and, for the work assignment, one Lookup→Text rule). **No rule binds a filing lookup**, so a filing move needs a new rule. |
| Playbook update node (`UpdateRecordActionCore`) | `POST /api/ai/playbooks/{id}/execute` | Dev has 2 `sprk_playbooknode` rows with executor type 22: "Update Record Document Profile" (`sprk_document` profile text fields) and `persistEnvelope` (matter-health fields on `sprk_matter`). Neither writes a filing lookup. |
| Output orchestrator (`DataverseUpdateHandler`) | — | Its only caller is `InvoiceExtractionJobHandler` (Service Bus). No admin HTTP route reaches it. |
| Chat update tool (`DataverseUpdateRecordHandler`) | Chat sessions route | The `sprk_analysistool` row "SYS-Dataverse Update Record" has `sprk_availableadhoc` false and side-effect class Write (confirmation gate). It needs a chat playbook that carries the tool, and an LLM-driven turn, so it is not deterministic. For this writer #2094 changed only the isolation read. |

**App Insights since the deploy:** 0 traces from `UpdateRecordNodeExecutor`, `DataverseUpdateHandler`, `DataverseUpdateRecordHandler` or the output orchestrator. The only push was this gate's.

### Gate table

| # | Check | Result | Evidence |
|---|---|---|---|
| R1 | **Positive live re-file into Secure and back out**, through the event filing route `PATCH /api/v1/events/{id}/filing`. It runs the same `IRecordOwnershipResolver.ReparentAsync` that #2094 changed (no-op rebind → `ParentsUnchanged`), then `SecureChildReconciler.AfterRefileAsync`. It is **not** one of the four writers (see above). | **PASS** | **Setup.** Ordinary project P `da2b8239-45cb-f111-a05d-0022482913fc` ("UAC-GATE-2094 20261018-224239Z", Web API, owner admin, root BU). Event E `b68d5239-45cb-f111-a05d-3833c5e9614d` created under P with `POST /api/v1/events` → **201**. E was owned by team "Spaarke" `09fbf21c…`, BU Spaarke, with 0 POA rows. Its event log `b98d5239…` had the same owner.<br>**In** (22:43:07Z): bind `sprk_RegardingProject` → `/sprk_projects(a9e04977…)` plus the ADR-024 regarding fields → **204**.<br>• E's owner became **Secure Record Owners `6eabc7f9-13be-f111-a05b-0022482913fc`**, BU **Secure Record**.<br>• POA: **1 row**, admin (the fixture's only sharee), `Read, Write, Append, AppendTo, Share`, the same mask the fixture holds.<br>• The descendant event log `b98d5239…` was also moved to Secure Record Owners and mirrored (1 POA row, admin).<br>• App Insights: `Re-filed sprk_event b68d5239… owner reassigned to team 6eabc7f9…`; `[SECURE-CHILD-SHARES] … granted=1` (event and log); `[SECURE-CHILD-RECONCILE] reassign: sprk_eventlog … -> team 6eabc7f9…`; `root=sprk_event … isolated=True … changed=1 … refused=0 failed=0`.<br>**No-op rebind** (22:43:43Z): the same body again → **204**. The owner, BU, POA and log were all unchanged. No "owner reassigned" line was logged, which is the `ParentsUnchanged` path. The step still ran on this route: `alreadyCorrect=1 changed=0`, shares `unchanged=1`.<br>**Out** (22:44:03Z), as admin (E's creator, holding Read/Write/Append/AppendTo/Share on the fixture): bind back to P → **204**.<br>• E's owner was back to team **"Spaarke" `09fbf21c…`**, BU Spaarke, **POA 0 rows (released)**.<br>• The event log was also back to "Spaarke" with 0 POA rows.<br>• That request's telemetry was dropped by ingestion sampling, so the Dataverse state is the evidence. |
| R2 (ii) | App Insights: errors from the changed components, after vs before | **PASS** | Windows on role `spaarke-bff-dev`: C = 22:30:26 → 22:52Z (after), B = the same length just before, A = the 24 h before. Query `q_ii.kql`: exceptions whose type, message, details or problemId, and traces at severity ≥ Warning whose message or dimensions, mention `RecordReparent`, `RefileIsolationRead`, `AfterRefileAsync`, `FieldMappingEndpoints`, `DataverseUpdateHandler`, `UpdateRecordActionCore`, `UpdateRecordNodeExecutor`, `DataverseUpdateRecordHandler`, `RecordOwnershipResolver`, `ReparentAsync`, `record_owner_parent_unresolved` or `[SECURE-CHILD-RECONCILE]`. Result: **0 rows in A, B and C**.<br>Control (`q_h.kql`): `[SECURE-CHILD-RECONCILE]` lines do appear (1,423 in A, 22 in B, 28 in C), **all at Information**.<br>Health:<br>• **0 × 5xx** in every window (requests: 2,831 in A, 62 in B, 67 in C).<br>• Exceptions in C are only known ones: 3 × `ODataError "Invalid hostname for this tenancy"` (#1352/#1830; 283 in A) and 20 × `sprk_userentityassociation` alternate-key not-found (833 in A).<br>• Error traces in C are only known ones: `[SECURE-CENSUS] HumanPrincipalReachesSecureBusinessUnit` (84), IDX40001 CIAM issuer (32), IDX10214 audience (14). All are also present before. |
| R3 (iii) | No-op push smoke: a push on a reparentable child that writes no filing lookup still succeeds | **PASS** | Test matter M `fdafd99b-45cb-f111-a05d-0022482913fc` (Web API, `sprk_assignedattorney1` = admin's own contact `8e9918a9…`). Event E2 `dc383a9c-45cb-f111-a05d-3833c5e9614d` created under M with `POST /api/v1/events` → 201.<br>`POST /api/v1/field-mappings/push` {sprk_matter, M, sprk_event} → **200**: `success true`, total 1, updated 1, failed 0, skipped 0, errors []. `sprk_assignedattorney1` **Mapped**; the other 7 rules Skipped ("Source value is null").<br>E2 after: attorney1 = `8e9918a9…`; the owner is unchanged (team "Spaarke"), `sprk_regardingmatter` is unchanged, and there are 0 POA rows.<br>No reconcile line in that operation, as expected (`MayRefile` false). Reverted to null (204) before the delete. |
| #2083 | Admin `GET /api/v1/external-access/assigned-access?recordType=matter&recordId=042f4462-860e-f111-8342-7c1e520aa4df` | **PASS** | **200**, 4 entries.<br>• Both organization entries carry names: **Morrison Foerster LLP** `67577f8c…` (access row `9aed8ab9…`) and **Spaarke Inc** `a5999fcc…` (access row `7959257b…`).<br>• The 2 contact entries are both "Ralph Schroeder" (`8e9918a9…` with systemuser `1d02f31c…`, and `2e419a4f…`).<br>• `residualAccessTerms` [] on all 4. |
| — | Positive re-file through one of #2094's four writers | **NOT REACHABLE without new config** | See the table above. Covered offline by #2094's `RefileWriterBindShapeTests`, `RefileWriterAfterRefileHookTests` and `RefileWriterReconcilerRegistrationTests`. The malformed-bind refusal (`record_owner_parent_unresolved`) was not exercised live, because no reachable writer parses bind strings without a filing rule or node. |

### Created and deleted (all as admin)

| Row | Id | Delete | Read-back |
|---|---|---|---|
| `sprk_eventlog` (E's create log) | `b98d5239-45cb-f111-a05d-3833c5e9614d` | 204 | 404 |
| `sprk_eventlog` (E2's create log) | `de383a9c-45cb-f111-a05d-3833c5e9614d` | 204 | 404 |
| `sprk_event` E | `b68d5239-45cb-f111-a05d-3833c5e9614d` | 204 | 404 |
| `sprk_event` E2 | `dc383a9c-45cb-f111-a05d-3833c5e9614d` | 204 | 404 |
| `sprk_matter` M | `fdafd99b-45cb-f111-a05d-0022482913fc` | 204 | 404 |
| `sprk_project` P | `da2b8239-45cb-f111-a05d-0022482913fc` | 204 | 404 |

- **The admin POA rows the mirror granted** on E and its log were revoked by the move out (0 rows) before the deletes.
- **Final sweep, 22:47Z:**
  - 0 `UAC-GATE-2094*` projects, matters or events;
  - 0 event logs on E or E2;
  - 0 `sprk_assignedaccess` or `sprk_externalrecordaccess` rows on P or M;
  - 0 appnotifications for admin since 22:42Z.
- **Secure fixture `a9e04977…`, re-read at 22:50Z, is unchanged:**
  - `modifiedon` 2026-10-17T03:19:05Z;
  - owner Secure Record Owners;
  - BU Secure Record;
  - same container id;
  - POA admin only, same mask.

### Observations (no defect filed)

- **No-op rebind on the event filing route.** It still runs `AfterRefileAsync`: `ChildRecordEndpoints.UpdateAsync` calls it whenever `outcome.Written`, and a `ParentsUnchanged` result maps to `Written`. That cost about 5 s and 2 share reads, with nothing changed. It is idempotent and correct. #2094's "no step when no parent moved" applies only to the push, the node and the output orchestrator; this route was out of #2094's scope. This is cost only.
- **The move into Secure took 19.4 s.** That covers the owner reassignment, the mirror, and the descendant pass over the event log. It is a pre-existing cost profile, not a #2094 change.
- **Sampling hides some requests.** Ingestion sampling dropped two of this run's 7 requests (E2's create and the move-out PATCH). Gate evidence for re-files should keep coming from Dataverse state, not from request telemetry alone.

**Verdicts:**
- Deploy (i): PASS.
- R1, a positive live re-file into and out of Secure through the shared changed resolver: PASS.
- R2 (ii) App Insights: PASS.
- R3 (iii) no-op push: PASS.
- #2083: PASS.
- A positive re-file through the four #2094 writers is NOT REACHABLE live without creating config (none was created).

## 099b live gate (2026-10-19), dev BFF 1.0.0+44f802a1d (live ad0e9e1a7 + 186 49279def1 + 099b d7eabba3b)

**Verdict: PASS.**

| Step | Result | Evidence |
|---|---|---|
| Backfill `Backfill-RecordOwnership.ps1 -Entities sprk_externalrecordaccess` | **PASS** | Dry run: 101 rows to write (73 Spaarke Business Unit 1, 26 Spaarke, 2 Secure). -Apply on a 3-row sample, read back, then the remaining 96 written with 0 failures; reversal manifests are kept in the session scratchpad. The 2 Secure rows had already been re-owned by the deployed reconciler at 11:38:09Z (modifiedby `mi-bff-api-dev`). **VERIFY: PASS** (103 team-owned rows checked, 0 mismatched, 0 to write, 0 unresolvable, 0 read failures). |
| Grant on an ordinary root | **PASS** | Project `fb73b08c…`: `/grant` → 200 (invite NoEmail). Row `0d347616…` is owned by team `cf15f587…` (that BU's default team); owning user null; createdby `mi-bff-api-dev`. |
| Grant on a Secure root | **PASS** | Fixture `a9e04977…`: `/grant` → 200. Row `46347616…` is owned by Secure Record Owners `6eabc7f9…`; owning user null. |
| Revoke | Ordinary 200; Secure **500**, filed as **#2103** | Both rows end statecode 1 / statuscode 2, still team-owned. The Secure revoke of a no-email contact returns 500 `container_not_cleared`, permanently. This is pre-existing task-017 behaviour; the fix is in progress. |
| Negative / no regression | **PASS** | No token on grant, record-grants and revoke → 401. The record-grants lists are correct, and the contact's 2 other grants are unchanged. |
| Telemetry 11:37Z–11:51Z | **PASS** | 200 ×22, 401 ×6, 500 ×1 (the #2103 revoke). Exceptions: only `ODataError "Invalid hostname for this tenancy"` (#1830). |

186 server side: no new exception types and no 5xx since the deploy. Its live gate (an unlicensed guest's sign-in and registration) needs the owner.

## #2102 (#2084) live gate (2026-10-19)

**Scope.** Server-side gate for PR #2102 (issue #2084, master `947d98e31`). Run 20:36–20:53Z against spaarke-bff-dev and spaarkedev1, as admin only (the default `az` login, systemuser `1d02f31c…`).
- Tokens: ARM (for the Kudu VFS read), Dataverse, and a delegated BFF token `api://1e40baad…/.default` from `az` (Azure CLI client `04b07795…`, scp `SDAP.Access user_impersonation`). No token or secret was printed or written.
- No Entra, Key Vault, app-setting, role, team, solution or tenant-config write. No SPE container was touched. `65a3fab2…` and the fixture `a9e04977…` were not touched. No existing record was modified.
- Scripts and logs: session scratchpad `gate2102/` (`g2102_log.txt`, `g2102_lib.py`, `g2102_s1.py`, `g2102_s2a.py`, `g2102_s2b.py`, `g2102_s3a.py`, `g2102_s3b.py`, `g2102_s4.py`, `g2102_s5.py`, `g2102_chk.py`, `g2102_ai.py`, `g2102_ai_runs*.json`).

**Verdict: PARTIAL.** Deploy, must-not-fire, the ordinary publish and cleanup PASS. Neither the Secure lineage nor the 409/400 refusals could run, because **dev has no Secure matter**.

| Step | Result | Evidence |
|---|---|---|
| 1. Deployed literals | **PASS** | Kudu VFS `Sprk.Bff.Api.dll`: 19,767,808 B, sha256 `eff568d0…`, InformationalVersion **`1.0.0+283466d40b4b6028e74a5f2ef111d87d9e631721`**. `git merge-base --is-ancestor 947d98e31 283466d40` holds.<br>User strings (UTF-16) present: `ontology.admin.rule_held_secure`, `ontology.admin.scope_matter_secured`, `ontology.admin.owner_unavailable`, "This rule is held by a Secure matter's team; publish a new rule instead.", and the entity sets `sprk_policies` and `sprk_policyversions`.<br>Metadata names present: `CheckVersionOwnerAsync`, `IsCitation`, `CitationLookups`, `OwnerUnavailable`.<br>Live use of the lineage entries: from 20:42Z every recent-changes listing reads `sprk_policy` and `sprk_policyversion` (`[DATAVERSE] RetrieveMultiple on sprk_policy returned 1 records`, `… sprk_policyversion returned 1/2 records`). |
| 2. Lineage into Secure via the reconciler | **NOT RUN: no Secure matter on dev** | `sprk_matters?$filter=sprk_issecure eq true` → 200 with **0 rows**. No `sprk_matter` is owned by team `6eabc7f9…`; the team owns only 3 projects (`31e232ae…`, `65a3fab2…`, `a9e04977…`). The reconciler heartbeat agrees: `roots=0/3`. As instructed, no record's secure flag was flipped, so no Secure probe was created. **Nothing is HELD for the ontology smoke.** The ontology `rule_held_secure` smoke needs a Secure matter too: the owner must make one (Make Secure) or name one. |
| 3. Must-not-fire (ordinary matter) | **PASS** | **Probe.** Policy `b8346c69-fdcb-f111-a05b-7c1e520a989f` (`UAC-2102-GATE-20261019-204109Z`, Ontology domain, Off, subject Matter), created 20:41:12Z with `sprk_matter` = ordinary matter `d14d79f4-8fbf-f111-aaaf-0022482913fc` (`sprk_issecure` false, owner admin; read only). Version v1 `236f0569-fdcb-f111-a05d-0022482913fc`, with the current-version pointer set (204).<br>**Sweeps.** The 20:42:05Z and 20:44:05Z runs list `recentRowsChanged=2 recentRoots=0`; after the publish, the 20:48:04Z and 20:50:05Z runs list `recentRowsChanged=3 recentRoots=0`. Every run had `refused=0 failed=0 recentFailure=(null)` at Information.<br>**Owners.** Read at 20:47:09Z and again at 20:51:09Z: the policy, v1 and v2 are all still owned by admin `1d02f31c…`, BU Spaarke. **None was re-owned to `6eabc7f9…`.** |
| 4a. Publish refusal (409 `rule_held_secure` / 400 `scope_matter_secured`) | **NOT TESTED live: no Secure matter** | The delegated BFF token works (see 4b), so only the Secure matter is missing. Covered offline by `OntologyAdminAuthoringSecureHoldTests`. |
| 4b. Ordinary publish through the BFF | **PASS** | `POST /api/v1/ontology/admin/authoring/dry-run` {policyId b8346c69…, Existence, matterId d14d79f4…} → **200** (`recorded true`, 0 rows).<br>`POST /api/v1/ontology/admin/authoring/policies/b8346c69…/versions` {draft, matterId d14d79f4…} → **200**: v2 `01c07fa7-406e-470d-9699-6f490c4f6164`, previous `236f0569…`, `reevaluation notEvaluated` (the rule is Off), superseded 0, raised 0.<br>v2 is owned by the caller (admin), so the resolver's ordinary answer bound no `ownerid`. 503 `owner_unavailable` did not fire.<br>Trace: `Ontology authoring: policy b8346c69… published version 2 01c07fa7… (reevaluation=notEvaluated)`. |
| App Insights 20:40–20:51Z | **PASS** | `0x80040299` / "Read Privilege Check For Owner failed": **0 rows** (traces and exceptions). Reconciler lines with failed ≠ 0: **0**. Requests: no 5xx; the two ontology requests were both 200. Exceptions: only `ODataError "Invalid hostname for this tenancy"` ×3 (#1830). |
| 5. Cleanup | **PASS** | Signals citing the probe: 0. The pointer was cleared (`$ref` DELETE 204). Then deleted, versions first, each 204 with a 404 read-back: v2 `01c07fa7…`, v1 `236f0569…`, policy `b8346c69…`. Sweep: 0 `UAC-2102-GATE*` policies or versions remain. Matter `d14d79f4…` is unchanged (`modifiedon` 2026-10-04T01:07:25Z, owner admin, not secure). |

### Created and deleted (all as admin)

| Row | Id | Delete | Read-back |
|---|---|---|---|
| `sprk_policyversion` v2 (BFF publish) | `01c07fa7-406e-470d-9699-6f490c4f6164` | 204 | 404 |
| `sprk_policyversion` v1 | `236f0569-fdcb-f111-a05d-0022482913fc` | 204 | 404 |
| `sprk_policy` `UAC-2102-GATE-20261019-204109Z` | `b8346c69-fdcb-f111-a05b-7c1e520a989f` | 204 | 404 |

**HELD for ontology smoke; main session deletes:** none. No Secure probe was created, because dev has no Secure matter.

### Observations (no defect filed)

- **The lineage reaches both tables live.** The reconciler's recent-changes listing picked up the new probe rows on the first sweep after they were created, so the deployed lineage includes `sprk_policy` and `sprk_policyversion`. It resolved them to no Secure root, which is correct for an ordinary matter. The positive move into Secure needs a Secure matter.
- **The sweep itself runs `mode=ReportOnly`**, while the recent-changes pass writes. A Secure-matter probe would be re-owned by the recent-changes pass, as the 099b gate saw for `sprk_externalrecordaccess`.
- **The BFF logs a Warning** "Token on POST … appid=04b07795…" for the Azure CLI client's delegated token. The request was accepted. This is diagnostic only and outside #2102.

**Verdicts:**
- Step 1 deploy and literals: PASS.
- Step 2 Secure lineage: NOT RUN (no Secure matter on dev).
- Step 3 must-not-fire: PASS.
- Step 4 refusal: NOT TESTED (no Secure matter); ordinary publish: PASS.
- App Insights: PASS.
- Step 5 cleanup: PASS.

### Secure-matter steps (2026-10-20)

**Scope.** These are the steps blocked above. Run 03:12–03:16Z on 2026-10-20 against the same environment: BFF live `1.0.0+00aba6d99`, which contains 947d98e31. Run as admin only (default `az` login; Dataverse token and the delegated BFF token). No token or secret was printed or written. No Key Vault, role, team, Entra, app-setting, solution or SPE write. `65a3fab2…` and the fixture `a9e04977…` were not touched.
- **The Secure matter** is PAT-908068 `5d46df87-e762-f111-ab0c-000d3a4d8152`, made Secure by the owner. It was only read: `sprk_issecure` true, owner Secure Record Owners `6eabc7f9…`. Before and after, it reads `modifiedon` 2026-10-20T02:30:45Z and `versionnumber` 28602427. Its document, grants and shares were not touched.
- Scripts and log: session scratchpad `gate2102/` (`g2102s_lib.py`, `g2102s_pre.py`, `g2102s_s1.py`, `g2102s_s2.py`, `g2102s_s4.py`, `g2102s_chk.py`, `g2102s_poll.py`, `g2102s_ai.py`, `g2102s_ids.json`, `g2102s_log.txt`, `g2102s_svc947.cs.txt`).

| Step | Result | Evidence |
|---|---|---|
| 1. Lineage into Secure via the reconciler | **PASS** | **Probe.** Policy `6ee40d2a-34cc-f111-a05b-7c1e520a989f` (`UAC-2102-GATE-SECURE-20261020-031303Z`, Ontology domain, Off, subject Matter), with `sprk_matter` = the Secure matter. Version v1 `70e40d2a-34cc-f111-a05b-7c1e520a989f`, with the current-version pointer set (204). Both rows were created 03:13:05Z, owned by admin `1d02f31c…`, BU Spaarke.<br>**Re-own.** The 03:14:03Z recent-changes run (`run=65ba3972…`, `mode=Apply`) found `root=sprk_matter:5d46df87…: a related record changed`. It then logged `reassign: sprk_policy 6ee40d2a… owner systemusers(1d02f31c…) -> team 6eabc7f9…` at 03:14:10Z and `reassign: sprk_policyversion 70e40d2a… -> team 6eabc7f9…` at 03:14:11Z. It also granted the creator a share on both rows (`[SECURE-CHILD-SHARES] grant SystemUser 1d02f31c…`).<br>**Owners.** Read at 03:14:38Z and again at 03:15:48Z: both rows are owned by **Secure Record Owners `6eabc7f9…`**, BU Secure Record, modified by `mi-bff-api-dev`. That is about 65 s after create, on the first sweep. |
| 2. 400 `scope_matter_secured` | **PASS** (the 400 comes from the scope check; the `CheckVersionOwnerAsync` 400 branch is not reachable through the API) | **Code (947d98e31, `OntologyAdminAuthoringService.cs`).** `PublishVersionAsync` runs `CheckScopeAsync` (L1117) before the dry-run proof and before `CheckVersionOwnerAsync` (L1173). `CheckScopeAsync` reads the requested matter as the caller and refuses a Secure, Restricted or unreadable matter with 400 `scope_matter_secured` (L1753–1756). The 400 in `CheckVersionOwnerAsync` (L1288–1300: rule not held, a matter was requested, and no different stored matter) therefore fires only when the requested matter turns Secure between those two reads. That is a race, and a single request cannot produce it without changing the matter. A request naming an already-Secure matter gets the same code from the scope check.<br>**Live.** A SEPARATE probe, policy `33db2139-34cc-f111-a05d-3833c5e9614d` (`UAC-2102-GATE-S2-20261020-031328Z`, no matter, Off) with v1 `3ad11e35-34cc-f111-a05d-0022482913fc` and the pointer set. Once, `POST /api/v1/ontology/admin/authoring/policies/33db2139…/versions` {draft, `matterId` = Secure matter, no dry run} → **400** `reasonCode ontology.admin.scope_matter_secured`, `errors.matterId` "A rule cannot be limited to a Secure or Restricted matter…", detail "Nothing was written."<br>**Nothing written.** The policy was unchanged before and after: `sprk_matter` null, the same pointer, `modifiedon` and `versionnumber`. The versions were unchanged: only v1, `inforceto` null, the same `modifiedon`. The Secure matter was unchanged. App Insights shows no "Ontology authoring" trace in the window, so neither `CheckVersionOwnerAsync` log line fired. That confirms the scope check answered. The probe was then deleted (see below). |
| 3. Do not publish against the step-1 probe | **Done** | No BFF call was made against `6ee40d2a…`. The only ontology request in the window is the step-2 400. |
| App Insights 03:12–03:18Z | **PASS** | `0x80040299` / "Read Privilege Check For Owner failed": **0 rows**. Reconciler failed or error rows (any, and any naming `sprk_policy`): **0**. Requests: no 5xx; the ontology route had one request, a 400 (step 2). Exceptions: only `ODataError "Invalid hostname for this tenancy"` ×1 (#1830). The BFF Warning "Token on POST … appid=04b07795…" was logged again, as before (diagnostic). |
| 4. Cleanup | **PASS** | Step-2 probe: signals citing it 0. Pointer `$ref` DELETE 204. v1 `3ad11e35…` 204 then 404; policy `33db2139…` 204 then 404. Sweep: the only `UAC-2102-GATE*` rows left are the HELD step-1 pair. |

**Created and deleted (as admin)**

| Row | Id | Delete | Read-back |
|---|---|---|---|
| `sprk_policyversion` v1 (step-2 probe) | `3ad11e35-34cc-f111-a05d-0022482913fc` | 204 | 404 |
| `sprk_policy` `UAC-2102-GATE-S2-20261020-031328Z` | `33db2139-34cc-f111-a05d-3833c5e9614d` | 204 | 404 |

**HELD for ontology smoke; main session deletes:**
- `sprk_policy` `6ee40d2a-34cc-f111-a05b-7c1e520a989f` (`UAC-2102-GATE-SECURE-20261020-031303Z`, `sprk_matter` = `5d46df87…`, Off). Owned by `6eabc7f9…`; the current-version pointer is set to the version below.
- `sprk_policyversion` v1 `70e40d2a-34cc-f111-a05b-7c1e520a989f`, owned by `6eabc7f9…`.
- To delete them: clear the pointer (`sprk_CurrentVersion/$ref`), then delete the version, then the policy. The reconciler's creator shares go with the rows.

**Verdicts:** Step 1 Secure lineage PASS. Step 2 400 `scope_matter_secured` PASS, but only via `CheckScopeAsync`; the `CheckVersionOwnerAsync` 400 branch is a race and is not reachable through the API (offline coverage only). Step 3 held. App Insights PASS. Cleanup PASS. No defect.

## #2104 live gate (2026-10-19)

**Scope.** Server-side gate for PR #2104 (issues #2103 and #2105, master `d301c0675`). Run 21:15–21:21Z against spaarke-bff-dev and spaarkedev1, as admin only (the default `az` login).
- Tokens: ARM (for the Kudu VFS read), Dataverse, a delegated BFF token `api://1e40baad…/.default`, and a delegated Graph token (Azure CLI client), all from `az`. No token, secret, email or UPN was printed or written. No transient 401 occurred, so the one-retry path never ran.
- No Entra, Key Vault, app-setting, role, team, solution or tenant-config write. No SPE container was written; the only container contact was the BFF's own reads during the revokes in steps 2–3, plus the read-only GETs in step 4. `65a3fab2…` was not touched. The fixture `a9e04977…` gained only probe grant rows, and those were deleted.
- Scripts and logs: session scratchpad `gate2104/` (`g2104_log.txt`, `g2104_lib.py`, `g2104_s1.py`, `g2104_pre.py`, `g2104_s2.py`, `g2104_s2c.py`, `g2104_s3.py`, `g2104_s4.py`, `g2104_s4b.py`, `g2104_s4c.py`, `g2104_ai.py`, `g2104_ai_out*.txt`, `g2104_s6.py`, `g2104_ids.json`).

**Verdict: PASS. #2103 is fixed live.** The guest-permission shape (step 4) is NOT TESTED, because the fixture container holds no permissions and the admin login cannot read raw Graph permissions.

| Step | Result | Evidence |
|---|---|---|
| 1. Deployed literals | **PASS** | Kudu VFS `Sprk.Bff.Api.dll`: 19,821,056 B, sha256 `453273ac…`, InformationalVersion **`1.0.0+00aba6d998cf9809b99b2a2ea6f90af961bf114d`**. `git merge-base --is-ancestor d301c0675 00aba6d99` holds.<br>User strings (UTF-16) present: `sdap.revoke.incomplete.permission_ambiguous`, `sdap.closure.incomplete.permission_ambiguous`, `sdap.revoke.incomplete.organization_too_large`, `sdap.closure.incomplete.organization_too_large`, `sdap.closure.incomplete.too_many_grants`, and the retained `sdap.revoke.incomplete.container_not_cleared`.<br>Metadata names present: `SpeContainerContactPermissionState`, `ContactPermissionRemains`, `HasNoContainerMembershipKey`, `FindContactPermissions`, `SpeMembershipKey`, `NoMembershipKey`. |
| 2. #2103 repro: Secure revoke of a NO-EMAIL contact | **PASS** | **Probe.** Contact A `295dfa50-02cc-f111-a05b-7c1e520a989f`: no email, unbound (no `sprk_externalobjectid`). Created 21:16:15Z.<br>**Grant.** `POST /grant` on the fixture `a9e04977…`, with the same body as 099b, at 21:16:23Z → **200** (invite `NoEmail`, `speContainerMembershipGranted` false). Row `a1468150-02cc-f111-a05d-3833c5e9614d` is 0/1, owned by Secure Record Owners `6eabc7f9…`, owning user null, createdby `mi-bff-api-dev`.<br>**Revoke.** `POST /revoke` at 21:16:25Z → **200** `speContainerOutcome NotAttempted`, `deactivatedCount 1`. The row ends **statecode 1 / statuscode 2**, still owned by `6eabc7f9…`. Repeating the revoke at 21:16:26Z → 200 `NotAttempted`, `deactivatedCount 0`.<br>**The original 099b #2103 row.** Revoking `46347616…` again (Jonathan `7f6f48e9…`, no email, already inactive) at 21:16:55Z → **200** `NotAttempted`, `deactivatedCount 0`. Before #2104 this row returned 500 `container_not_cleared` permanently. The row and the contact are unchanged (same `versionnumber` and `modifiedon`), and Jonathan still has 2 active grants.<br>**Trace** (×3): `[EXT-REVOKE] Contact … has neither a bound object id nor an emailaddress1 … so no permission on b!fKbj… can be attributed to them` at Information. |
| 3. Secure revoke of a contact with an email but no container permission | **PASS** | **Probe.** Contact B `a3b8d46a-02cc-f111-a05d-0022482913fc`, with a synthetic `@example.invalid` email, unbound.<br>**Grant row written directly in Dataverse.** `/grant` would send a CIAM invite to an unbound contact that has an email (`InviteOnGrantAsync`), which would be an external-identity write. So the row was created as admin with the BFF's payload shape (`sprk_Contact`/`sprk_Project` binds, `ownerid` = `6eabc7f9…`): row `f20c9289-02cc-f111-a05d-0022482913fc`, 0/1.<br>**Revoke.** `POST /revoke` at 21:17:55Z → **200** `speContainerOutcome NoPermissionFound`, `speContainerMembershipRevoked` false, `deactivatedCount 1`. The row ends 1/2.<br>**Trace:** `No SPE container permission exists for Contact a3b8d46a… on b!fKbj… — nothing to remove`. The container was read, and nothing was deleted. |
| 4. Guest permission shape | **NOT TESTED** | **Raw Graph.** `GET /v1.0/storage/fileStorage/containers/{fixture}/permissions` with the admin's delegated Graph token (Azure CLI client) → **403 `accessDenied`**. That client is not registered on the container type. An app-only token for the BFF/SPE app needs its credential (Key Vault), which was not read.<br>**Fallback: the BFF's own app-only read**, `GET /api/spe/containers/{fixture}/permissions`. With config `68f9a952…` → 404 `spe.admin.deny.container_out_of_scope`. With config `c3a25b9a…` → **200, `count 0`**. The fixture container holds **no permissions**, so no user permission exists whose `grantedToV2.user.id` or email/UPN placement could be inspected. Even with permissions present, this DTO would not answer the placement question: `SpeAdminGraphService.MapPermission` drops email and UPN (`email = null`).<br>The #2105 assumption (email and UPN read from `grantedToV2.user` AdditionalData) remains **unverified live**. Testing it needs a container with a hand-added guest permission, read with the SPE app's app-only token. |
| 5. App Insights 21:14–21:23Z | **PASS** | Requests: `POST /external-access/grant` 200 ×1, `POST /external-access/revoke` **200 ×4**; **no 5xx on any route**. `GET /api/spe/containers/{id}/permissions` 200 ×2 and 404 ×1 (step 4). Exceptions: only `ODataError "Invalid hostname for this tenancy"` ×2 (#1830). Every `[EXT-REVOKE]` trace is at Information; none is at Error. |
| 6. Cleanup | **PASS** | Ledger rows (`sprk_assignedaccesses`) for either probe contact: 0. Grants deleted first, then contacts; each returned 204, and each read-back returned 404. Sweep: 0 contacts with `firstname` `UAC2104`. Fixture `a9e04977…` is unchanged (secure, owner `6eabc7f9…`, same container, `modifiedon` 2026-10-17T03:19:05Z); its only grant row is the 099b row `46347616…` (inactive). |

### Created and deleted (all as admin)

| Row | Id | Delete | Read-back |
|---|---|---|---|
| `sprk_externalrecordaccess` A (BFF `/grant`) | `a1468150-02cc-f111-a05d-3833c5e9614d` | 204 | 404 |
| `sprk_externalrecordaccess` B (direct DV) | `f20c9289-02cc-f111-a05d-0022482913fc` | 204 | 404 |
| `contact` A (no email) | `295dfa50-02cc-f111-a05b-7c1e520a989f` | 204 | 404 |
| `contact` B (synthetic `.invalid` email) | `a3b8d46a-02cc-f111-a05d-0022482913fc` | 204 | 404 |

**Observations (no defect filed).**
- The fixture container `b!fKbj…` has **0 permissions**, so on dev every Secure revoke of a contact with an email takes the `NoPermissionFound` path. Neither the positive removal path (an oid or email match) nor the marker exclusion (#2105) can be exercised live without a hand-added container permission. Both are covered offline (`SpeRevokeMatcherTests`, `SpePermissionMatcherTests`).
- `NotAttempted` for a no-key contact is distinct from `NoPermissionFound`, as designed: the container is not read for that contact.

#### Ontology `rule_held_secure` smoke and cleanup (2026-10-20)
- **The smoke (run by ontology with a v2 token):** first a dry run (200), then `POST /authoring/policies/6ee40d2a…/versions` with `{Draft, AllMatters:true}`.
  - Result: **409 `ontology.admin.rule_held_secure`**, correlation 0HNPE4LSGF2VS:00000006.
  - The version count was 1 before and 1 after, so nothing was written.
- **Cleanup by UAC:**
  - pointer cleared (204);
  - version `70e40d2a-34cc-f111-a05b-7c1e520a989f` deleted (204, then 404 on read-back);
  - policy `6ee40d2a-34cc-f111-a05b-7c1e520a989f` deleted (204, then 404).
  - No `UAC-2102-GATE*` rows remain.
- **#2102 (#2084) live gate: PASS.** All steps are complete. #2084 is closed.

## 186 live gate (2026-10-20)

**Scope.** Server-side half of the task 186 dev gate (design `186-model1-workforce-design.md` §6 steps 1–7, read against the A9 code that is deployed). Run 14:06–14:20Z against spaarke-bff-dev (live `1.0.0+00aba6d99`), spaarkedev1, App Insights `spe-insights-dev-67e2xz`, and the Spaarke tenant's directory (Graph read only). Run as admin only (the default `az` login).
- **The identity under test:** "the Tracy guest", a B2B guest in the Spaarke tenant, object id `918d0e7a…`, home tenant `bc3aa7f4…`.
- Tokens: Dataverse, Graph (read), and App Insights, all from `az`. No token, email, UPN or invite code was printed or written.
- **Read-only.** No Dataverse, role, Entra, Key Vault, app-setting or tenant-config write. Step 6's privilege removal was **not** run, because its precondition failed (see below). No vault was read. App settings were read with a name filter only.
- Helper scripts were in a private folder `.g186tmp/` (`g186_*.py`), which was deleted at the end.

**Verdict: NOT RUN. The Tracy guest has not reached the BFF.** The owner's "is verified" matches the guest's **invitation redemption**, not a sign-in to the SPA. **The deployed SPA cannot run the A9 registration flow at all.**

**What happened, from the server side.**
- **Directory (Graph).**
  - The guest was created by invitation at 03:41:41Z and redeemed at 03:42:15Z (`externalUserState` Accepted). It has 0 licences, no group memberships, and is enabled.
  - Every Spaarke-tenant sign-in by the guest is a My Apps / App Access Panel / My Profile sign-in at 03:42–03:45:33Z (`crossTenantAccessType b2bCollaboration`; one 50140 "Keep me signed in" interrupt, then success). There is **no sign-in to `9f1da340…` (Spaarke Workspace) or to any Spaarke app**, and none at all after 03:45:33Z (re-polled at about 14:18Z).
- **BFF (App Insights; `requests` is unsampled, `sum(itemCount)` = `count()`).**
  - 0 `/api/v1/external/*` and 0 `registration` requests since 03:39Z. The last external request was 03:38:20Z, **before the guest existed**.
  - 0 `[A9-REG]`, `[WF-AUTH]`, `[ID-BIND]`, `registration_required` or WorkforceHome lines since 03:39Z.
  - Since 03:39Z: 502 requests in all, 0 × 5xx.
- **The deployed SPA** (`swa-spaarke-external-spa-dev`, environment `default`, last updated 2026-10-18T03:36:36Z) is `deploy-external-spa` run `39094144398`, from `work/spaarke-SPA-external-access-platform-r3` @ **`ebc74b331`**.
  - The served `/assets/app.js` pins the workforce realm to client `9f1da340…` and tenant `a221a95e…` ("organizations/common are refused (ADR-028 A8)"), with BFF scope `api://1e40baad…/user_impersonation` and BFF `spaarke-bff-dev`.
  - It contains **no** `registration_required` handling and no `/registration` call.
  - `ebc74b331` predates `RegistrationPage.tsx` and `WorkforceHomeGate.tsx`, which `00aba6d99` has. So the BFF is A9, but the SPA in front of it is still A8.

**What would happen if the guest signed in through this SPA today (code at `00aba6d99`).** This SPA would get a Spaarke-tenant token (`tid` Spaarke, `acct = 1`, `idp` = `bc3aa7f4`). That token goes to the default scheme, not WorkforceHome, so the A9 path never runs:
- `WorkforcePrincipalResolver.ResolveAsync` finds no systemuser and takes branch (b).
- The binder's member test passes: dev's `WorkforceIdentity:CustomerTenantIds` = [`a221a95e…`, `bc3aa7f4…`], so it returns Member → `EmailBindAndCreate` (`ContactIdentityBinder.cs:297`).
- With no email match it reaches **`CreateByOid` on the first `/me`** (`ContactBindingDecision.cs:830,858`), so the contact is **created silently**, with no `registration_required` and no form.
- That contact is on plane **Workforce (100000001)**, and its create sets **no owner** (`ContactIdentityStore.cs:780-818`: "Only a WorkforceHome create carries … an owner"). It would therefore be owned by the BFF application user in the root BU "Spaarke", **not BU1**.
- `POST /registration` would refuse this token with 403 `registration_home_identity_required` (`ExternalRegistrationEndpoints.cs:117-123`).

So the design's step-1 expectation (`/me` → 403 `registration_required`), and the BU1 owner rule, hold only for a WorkforceHome token from an A9 SPA build.

| Step | Result | Evidence |
|---|---|---|
| 1. Requests: `/me` 403 `registration_required` → registration 201 → `/me` 200 | **NOT RUN** (no guest traffic) | See above: 0 external requests and 0 registration or resolver lines since 03:39Z, and no Entra sign-in to the SPA client. |
| 2. One contact keyed to the guest; registration columns; owner BU1; view | **NOT RUN** (no contact). Config ready; the view does not exist. | Contacts: `sprk_externalobjectidkey` = the guest oid → 0; `startswith(…,'918d0e7a')` → 0; the secured `sprk_externalobjectid` → 0; name contains Tracy or Cheatham → 0; plane WorkforceHome (100000002) → 0 on all of dev; created since 2026-10-19T12:00Z → 0.<br>**Owner config (ready).** `ExternalAccess__CustomerBusinessUnitId` = `cb15f587…` ("Spaarke Business Unit 1", parent "Spaarke" `06fbf21c…`). Its default owner team is **`cf15f587…`** ("Spaarke Business Unit 1", `isdefault`, type Owner), the single team that `BuildDefaultOwnerTeamPath` would find. The App Service last restarted at 13:02:08Z, so the setting is live. A WorkforceHome registration would be owned by `cf15f587…`.<br>**View.** No contact `savedquery` named like "Workforce", "registered", "Self" or "Home" on dev. "Workforce contacts (self-registered)" (design §4b) is not in the source at `00aba6d99` either. Under A9 that part is SPA-r3's and is unbuilt; this is a gap, not a defect. |
| 3. No systemuser for the guest; Ralph Dewey still a systemuser | **PASS** (guest leg: no row; Dewey leg: row read only) | `systemusers` by `azureactivedirectoryobjectid` = the guest oid, any state → **0**; by name Tracy or Cheatham → **0**; created since 2026-10-19T12:00Z → **0**.<br>Ralph Dewey: systemuser `ba65dad3…`, **enabled**, `islicensed` true, access mode Read-Write, BU Spaarke, oid `bc596ecd…`. The BFF logged `[WF-AUTH] Resolved workforce caller … to systemuser ba65dad3…` 410 times in 14 days, the last at 03:38:20Z. |
| 4. Resubmit creates nothing new | **NOT RUN** | No registration happened. There are 0 contacts for the guest and so no duplicates. |
| 5. Entitlements `legal-front-door` + `policy-library`; an intake by this contact | **NOT RUN** | No `/me` or `/me/entitlements` request from the guest. `sprk_servicerequests` created since 03:40Z → **0**. |
| 6. Privilege removal (`prvCreatesprk_ServiceRequest`) and the refusal | **NOT RUN, by rule** (no registered contact and no intake) | The role was read only. "Spaarke Internal Contact User (Workforce)" has root `9deaac9c…` (BU Spaarke, `modifiedon` 2026-10-19T02:37:58Z) and 6 copies. 658 privileges: Basic 314, Global 232, Deep 95, Local 17.<br>On `sprk_servicerequest`: Create **Basic** (`fd03f48f…`), Read Basic, AppendTo Basic, Delete Basic. On `sprk_document`: Create Basic, Read Deep. So the ceiling ask of 2026-10-16 is in place.<br>**Nothing was removed.** For the proposed window, see below. |
| 7. `email` in the guest token | **UNDETERMINED** (no guest token reached the BFF) | App `1e40baad…` ("SDAP-BFF-SPE-API", `AzureADMultipleOrgs`, `requestedAccessTokenVersion` 2) lists `email`, `preferred_username`, `upn`, `acct`, `given_name` and `family_name` as optional **access-token** claims. Whether Entra emits `email` for a B2B guest, with `removeUnverifiedEmailClaim` true, is still unobserved.<br>The server would show the answer as follows: `POST /registration` 403 `sdap.access.registration_email_missing` with the trace `[A9-REG] Registration refused: the home token carries no usable email claim`, or a 201. Only `email` / `ClaimTypes.Email` count, never `preferred_username` or `upn` (`ExternalRegistrationEndpoints.cs:256-264`). On the A8 path it would instead show as an E1 create with no `emailaddress1`. |
| App Insights since 13:02Z | **PASS (no new errors)** | Exceptions: only `ODataError "Invalid hostname for this tenancy"` ×15 (#1830). Error traces: `[SECURE-CENSUS] HumanPrincipalReachesSecureBusinessUnit …` ×336, which has run since 2026-10-13 (57,540 lines in 7 days); it is pre-existing and not related to this gate. One `RoutingConsumerTypeHealthCheck FAILED: AI catalog drift` at boot. |

**What the owner and main session need to do before the gate can run.**
1. **Deploy an A9 SPA build** to `swa-spaarke-external-spa-dev`: `deploy-external-spa` from a commit that has `RegistrationPage.tsx` and `WorkforceHomeGate.tsx` (for example the R3 branch at or after `00aba6d99`, or master once R3 merges). The A9 home sign-in also needs the SPA's multi-tenant workforce-home client variables. That is R3's deploy (SPA-r3 owns registration), not UAC's.
2. **Then** the owner signs in as the Tracy guest **at the home directory** (the WorkforceHome flow) and registers once. Only then is step 1 meaningful.
   - Do **not** sign in through the current A8 SPA first. It would silently create a Workforce-plane contact owned by the root BU (above). That would make the guest a resolved A8 contact, and its oid would differ from the home oid the A9 path uses, which muddies the gate.
3. **Re-run this gate** (steps 1–5 and 7). Step 2 expects `sprk_identityplane` 100000002, `sprk_externaltenantid` `bc3aa7f4…`, `sprk_externalobjectid` and `sprk_externalobjectidkey` = the **home** oid (not `918d0e7a…`), owner team `cf15f587…`, no `parentcustomerid`, and `createdby` the BFF.
4. **Proposed step-3 refusal window** (owner-driven; UAC holds the privilege change):
   - (a) The owner, registered, submits intake #1 → expect 201.
   - (b) UAC records `prvCreatesprk_ServiceRequest` at Basic (`fd03f48f…`) on root `9deaac9c…`, then removes it (RemovePrivilegeRole).
   - (c) The owner submits intake #2 **at least 5 minutes later**, because `WorkforceCeilingTtl` is 5 min (`UserPrivilegeChecker.cs:85`). Expect 403 `sdap.access.deny.workforce_baseline_privilege_missing`.
   - (d) UAC restores the privilege at Basic immediately, in a try/finally, and re-reads the role.
   - The window lasts about 6–8 minutes. Agree a start time with the owner beforehand.

**Observations (no defect filed).**
- **The SPA and BFF are out of step on dev.** The BFF is A9 (`00aba6d99`); the SPA is A8 (`ebc74b331`). Any B2B guest whose home tenant is on the dev list, signing in through the live SPA today, is created silently on plane Workforce with no owner. That is the documented A8 behaviour ("the A8 create sets no owner"), so it is not filed as a defect. It is the reason the gate's BU1 expectation cannot hold until the SPA is redeployed.
- **`[ID-LINK-RECON]` decision lines log the systemuser's email in clear text** (Information, every 5 min). This is outside 186 and pre-existing, and is noted only because 186's own logs promise "never the email". No email is reproduced here.

## #2110 live gate (2026-10-20)

**Scope.** Server-side gate for PR #2110 (issue #2091 family, merged as `2b07480ab`). Run 16:52–17:07Z against spaarke-bff-dev, spaarkedev1 and App Insights `spe-insights-dev-67e2xz` (appId `6a76b012…`), as admin only (the default `az` login, systemuser `1d02f31c…`).
- Tokens: ARM (for the Kudu VFS read), Dataverse, the delegated BFF token `api://1e40baad…/.default` (Azure CLI client), and App Insights REST, all from `az`. No token, secret or UPN was written.
- One echo to note: the send response's `from` field carried the admin's email, and it reached the local console log once. It was redacted from the log file at once, and the logger then scrubbed emails. It is not reproduced here.
- No Entra, Key Vault, app-setting, role, team, solution or tenant-config write. No SPE container was touched. The Secure fixture `a9e04977…` was used only as a filing target; `65a3fab2…` was not touched.
- **External side effect (not Dataverse, cannot be deleted from here).** The respond-into-thread route is the in-app chat send, so it went through ACS (`spaarke-acs-dev`). It created:
  - one BFF service ACS identity ("chat-scope only");
  - one chat thread, `19:acsV2_ANECNPzfsjbVrJb1ME_lSE8KChvtqvN-zqA-y2xzB4E1@thread.v2`, with 30-day retention and the admin as its only participant;
  - one message, `1792515237746`.

  This is the same shape as the admin's routine dev sends of 2026-10-13 to 2026-10-18.
- Helper scripts were in a private folder `.g2110tmp/` (`g2110_*.py`, `g2110_ai3.kql`), which was deleted at the end.

**Verdict: PASS.** Steps 1, 2 and 4 pass. Step 3 passes for #2110: no new error comes from #2110's code. Two pre-existing defects surfaced during the run; one is already filed (#1638), the other is new and not filed (see Defects).

### Deploy evidence: PASS
- Kudu VFS `Sprk.Bff.Api.dll`: 19,854,848 B, sha256 `fa15fcf0…`, InformationalVersion **`1.0.0+21184ecd4789c2124c7d142237ba4dce0d8d5b6e`**. `git merge-base --is-ancestor 2b07480ab 21184ecd4` holds.
- Metadata names present: `PlacedByWriter`, `RefileThreadStep`, `FilingReconcileHost`, `JoinThreadAsync`, `LineageOnlyTargetOf`, `WithLineageLookupsIn`.
- User strings (UTF-16) present: "no SecureChildReconciler is registered" and "the thread reconcile after its re-file failed".
- **Telemetry.** `itemCount` was 1 on every request, trace and exception in the 2 h before the run, so no sampling weights were applied. Two whole operations were still missing (see step 2b).

### Gate table

| # | Check | Result | Evidence |
|---|---|---|---|
| 1 | Thread join → the after-re-file step runs exactly once | **PASS** | **Route.** The respond-into-thread writer, `CommunicationService.AssignExplicitThreadAsync` → `ThreadResolver.JoinThreadAsync`, reached through `POST /api/communications/send` {Type Message 100000004, `threadId`, no associations}. The capture-path join (`ResolveAndAssignAsync`, an existing thread found by key) is reachable only from inbound mail or an ACS thread key, so it was not run live.<br>**Setup.** `POST /api/communications/threads` {sprk_project, fixture} → **200**. Thread T `1d5d8bcd-a6cc-f111-a05d-0022482913fc` ("UAC-GATE-2110 20261020-165346Z", RecordAnchored, Open) is owned by **Secure Record Owners `6eabc7f9…`**, BU Secure Record, with 0 POA rows. The fixture held no thread and no communication before.<br>**Join** (16:53:54Z, op `3740009a…`): send → **200**, communication C `9e9775d6-a6cc-f111-a05d-0022482913fc`.<br>• It inherited the fixture as its regarding ("Inherited regarding sprk_project:a9e04977… from target thread"), so C was created already owned by the Secure team.<br>• **Order in that one operation:** thread read, then the reparent's owner reads, then the isolation read **before** the write, then the single thread-stamp write `Updated sprk_communication … with 1 fields` (16:53:59.42Z), then the step.<br>• **The step's lines in that operation:**<br>&nbsp;&nbsp;– the mirror: **1** `[SECURE-CHILD-SHARES] grant SystemUser 1d02f31c… on sprk_communication 9e9775d6…` (16:54:04.88Z), and `scope=all status=Completed inScope=1 updated=1 granted=1`;<br>&nbsp;&nbsp;– the pass: **1** `[SECURE-CHILD-RECONCILE] root=sprk_communication 9e9775d6… mode=Apply isolated=True mayRelease=False status=Completed examined=0 … failed=0` (16:54:07.17Z);<br>&nbsp;&nbsp;– the gate Decision Record step: **1** `RetrieveMultiple on sprk_decisionrecord returned 0 records` (16:54:07.29Z), after the pass, as the code orders it.<br>&nbsp;&nbsp;– Then "Stamped explicit thread target … (respond-into-thread)" (16:54:07.29Z).<br>• **Not run:** no thread-reconcile or "Joined existing thread" line, which is `PlacedByWriter`.<br>• **Across all operations to 17:07Z**, C has exactly **1** `root=` line and **1** request-side grant. No other writer ran the step.<br>**Dataverse after.** C is owned by **Secure Record Owners**, BU **Secure Record**, filed under the fixture, with thread = T. Its POA is **1 row, admin, `Read, Write, Append, AppendTo, Share`**, the fixture's own (only) sharee and mask. So the owner and the shares mirror the Secure root.<br>**Gate Decision Record.** When the step ran, C had **0** gate records. The comms-policy then decided DENY (confidence 0.35 < 0.45), and the `GateDecisionRecord` job wrote record `244611df-a6cc-f111-a05d-0022482913fc` at 16:54:12Z. The writer resolved that record **directly to Secure Record Owners** (owning user null), so no re-own was needed, and none happened (0 "re-owned to team" lines). The 16:56:05Z share job mirrored it (admin, the same mask). "Re-owned once" therefore had nothing to act on live: the step's gate query ran once and found 0. |
| 2a | Same-parent re-file on a communication → no step | **PASS** | `PATCH /api/communications/{C}/filing` {`sprk_RegardingProject` = the fixture C already holds, plus the ADR-024 fields} at 16:57:47Z (op `3e92b92a…`) → **204**, 3.6 s.<br>The op has 187 traces, and **none** is a step line:<br>• 0 `[SECURE-CHILD-RECONCILE]` and 0 `[SECURE-CHILD-SHARES]` lines;<br>• 0 `root=` lines;<br>• 0 gate-record or D-169 lines;<br>• 0 reassign or thread lines.<br>The only Warning is the known "Token on PATCH … appid=04b07795…" diagnostic. After the write, the op reads the descendant tables (to-do, event, analysis, memo, signal, decision record). That is `CoreAncestorRestamper.RestampChildrenOfAsync`, which runs on every write before the `ParentsUnchanged` check (`ChildRecordEndpoints.UpdateAsync`), not the step.<br>**Dataverse.** Owner, BU, filing, thread and POA are unchanged. Only `modifiedon`/`versionnumber` moved (28740650 → 28740675), because the same values were written ("written, but no re-file"). |
| 2b | Must-fire / must-not-fire pair on `PATCH /api/v1/events/{id}/filing`, the shape #2094 R1 used | **PASS** | **Setup.** Ordinary project P `4733a394-a7cc-f111-a05b-7c1e520a989f` (Web API, admin, BU Spaarke). Event E `d39ffc94-a7cc-f111-a05d-3833c5e9614d` created under P (`POST /api/v1/events` → 201), owned by team "Spaarke", with its log `d59ffc94…`.<br>**Must-fire, in → fixture** (op `8ff0fc31…`, 9.2 s): 204. The op shows `Re-filed sprk_event … owner reassigned to team 6eabc7f9…`, the grant on E and on the log, `reassign: sprk_eventlog … -> team 6eabc7f9…`, and `root=sprk_event … isolated=True changed=1 failed=0`. E and its log ended Secure-owned with the admin mirror. The earlier attempt at 16:59:44Z (op `f9fb2b85…`) showed the same.<br>**Must-not-fire, the same fixture again** (op `5e830866…`, 2.1 s, request retained, 175 traces): 204.<br>• **0** `root=`, `[SECURE-CHILD-*]` or reassign lines; the op holds the one `PATCH /api/data` write.<br>• E's owner, BU, POA and log are unchanged.<br>• **Compare #2094's same rebind on this route** (2026-10-18, before #2110): the step still ran there (`alreadyCorrect=1 changed=0`, shares `unchanged=1`, about 5 s). It no longer runs.<br>**Out → P** (204): E and its log are back to "Spaarke", with POA 0 rows (released).<br>**Telemetry gap.** The first same/out pair (17:00:0x and 17:00:2x) left **no request row and no traces**, although later `/healthz` rows were ingested. That pair was therefore repeated at 17:02:44–17:03:55Z. As #2094 already found, a re-file gate cannot rely on a dev request's telemetry being present. |
| 3 | App Insights: no new exceptions or Errors tied to these operations | **PASS for #2110** (two pre-existing defects found) | **Windows.** Gate 16:53–17:05:30Z against before 16:41–16:53Z (role `spaarke-bff-dev`); 0 × 5xx in both.<br>**Unchanged noise in both windows:**<br>• `[SECURE-CENSUS] HumanPrincipalReachesSecureBusinessUnit` (84 against 84);<br>• `[ID-LINK-RECON] spaarke-acme … binding column Failed` (2 against 2);<br>• `ODataError "Invalid hostname for this tenancy"` (#1830, 2 against 2).<br>**New in the gate window, none from #2110's code:**<br>(a) `sprk_userentityassociation` alternate-key not found ×10, op-less, at 16:59:27Z after the event create. This is known; #2094's baseline had 833 in 24 h.<br>(b) **#1638 (open):** `CommunicationArrivedProducer` faults reading the thread on `sprk_regardingreportcard` (Warning), so `[FANOUT] … privateOrUnknown=True recipients=0`. 17 in 14 days.<br>(c) **New, NOT FILED:** the OpenAI 400 `Invalid schema for response_format 'Create_Task_From_Email' … Missing 'dueDate'` (OpenAiClient Error and CommunicationCreateTaskAi Warning). 34 in 14 days, so every email or message create-task enrichment fails (see Defects).<br>(d) The share job's 400 race (see Observations).<br>**Telemetry after cleanup:** no exceptions and no Errors at 17:07Z. |
| 4 | Cleanup | **PASS** | Every row was deleted (204) and read back (404); see the table. **Sweep at 17:06:39Z:** 0 rows created since 16:53:30Z in `sprk_communication`, `sprk_communicationthread`, `sprk_communicationparticipant`, `sprk_communicationchannelref`, `sprk_decisionrecord`, `sprk_signal`, `sprk_todo`, `sprk_memo`, `sprk_emailreviewlog`, `sprk_event`, `sprk_eventlog`, `sprk_project` or `appnotification`. 0 `UAC-GATE-2110*` projects, events, communications or threads. **Fixture `a9e04977…` is unchanged:** `modifiedon` 2026-10-17T03:19:05Z, version 28460469, Secure Record Owners, BU Secure Record, POA admin only with the same mask. |

### Created and deleted (all as admin)

| Row | Id | Delete | Read-back |
|---|---|---|---|
| `sprk_decisionrecord` (gate record, comms-policy DENY job) | `244611df-a6cc-f111-a05d-0022482913fc` | 204 | 404 |
| `sprk_communicationparticipant` (participant index) | `224611df-a6cc-f111-a05d-0022482913fc` | 204 | 404 |
| `sprk_communication` C (BFF send) | `9e9775d6-a6cc-f111-a05d-0022482913fc` | 204 | 404 |
| `sprk_communicationthread` T (BFF `/threads`) | `1d5d8bcd-a6cc-f111-a05d-0022482913fc` | 204 | 404 |
| `sprk_eventlog` (E's create log) | `d59ffc94-a7cc-f111-a05d-3833c5e9614d` | 204 | 404 |
| `sprk_event` E (BFF `/api/v1/events`) | `d39ffc94-a7cc-f111-a05d-3833c5e9614d` | 204 | 404 |
| `sprk_project` P (Web API) | `4733a394-a7cc-f111-a05b-7c1e520a989f` | 204 | 404 |

Not deletable from here: the ACS chat thread, message and service identity listed in Scope. They expire under the ACS 30-day retention.

### Defects
- **New, not filed (outside #2110; ontology / email-intelligence own it):** `infra/dataverse/actions/create-task-from-email.action.json:50`.
  - The task item's `"required": ["subject", "description", "citation", "reason", "confidence"]` omits `dueDate`, which is declared in `properties` (line 60).
  - OpenAI strict structured output needs every property to be listed in `required`, so every CREATE-TASK-FROM-EMAIL run returns 400 (34 times since 2026-10-07). `CommunicationCreateTaskAi` logs "completion failed" and creates no candidate.
  - The fix keeps the "omit when no date" rule: list `dueDate` as required, and keep the prompt's "empty string when no date" (or make it `["string","null"]`). Then re-deploy the Dataverse action row.
  - Not filed here: the main session decides whether to file it and where.
- **Known, open: #1638.** `CommunicationArrivedProducer.cs:107` selects `RegardingFieldMap.AllRegardingFields`, which includes `sprk_regardingreportcard`, on `sprk_communicationthread`, so no communication-arrived fan-out is delivered for a threaded message. Seen again here.

### Observations (no defect filed)
- **The inline step and the two-minute share job raced on a newly created Secure communication.** The 16:54:00Z `secure-child-share-reconciliation` run picked up C, which was created Secure-owned at 16:53:58Z, while the request's step was mirroring it.
  - The job's own `GrantAccess` for the admin on C got **400** (16:54:04.42Z, Warning).
  - Its read-back then logged "did not read back as its mirror after the writes", and the run ended `status=Incomplete notUpdated=1` (Warning).
  - The request's grant (16:54:04.88Z) landed, and the next run (16:56:05Z) completed with C unchanged.
  - The end state is correct, and the job fails closed and retries as designed.
  - The same 400 also occurred on 2026-10-06, so the race predates #2110. #2110 makes it reachable from respond-into-thread, which used to run no step.
  - Cost: one Warning pair per collision.
- **Same-parent re-files now cost less:** 2.1 s against 9.2 s for a real move on the event route, and 3.6 s on the communication route. In #2094's gate the same no-op ran the full step.
- **A respond-into-thread send is filed under the thread's record when the message is created** (D1 inheritance). The join therefore re-files a message that is already Secure-owned: `wasIsolated` = true and nothing moves owner. On this path the step only mirrors and checks the gate records. A message carrying its own ordinary association, joined into a Secure thread, would move owner inside the join. That shape was not run live; it is covered offline by `RespondIntoThread_IntoASecureMattersThread_…`.

**Verdicts:**
- Deploy: PASS.
- 1 (thread join, step exactly once): PASS.
- 2a (communication same-parent): PASS.
- 2b (event route must-fire / must-not-fire): PASS.
- 3 (App Insights): PASS for #2110, plus one new unfiled pre-existing defect (create-task schema) and #1638 seen again.
- 4 (cleanup): PASS.

## #2130 / #2128 live gates (2026-10-20)

**Scope.** Server-side gates for PR #2133 (#2130 / #2131, `c3ec5d6e5`) and PR #2138 (#2128, `83d360ec8`). Run 18:14–18:50Z, read-only, against spaarke-bff-dev, spaarkedev1 and App Insights `spe-insights-dev-67e2xz`. The code was read at `origin/master` = `c3ec5d6e5`.
- **Deploy.** The BFF has run `1.0.0+c3ec5d6e5…` since 18:12:51Z. A single instance (`…b830db`) logged "Application started" at 18:11:46Z. Every census line after the deploy uses the new id-only finding format, which confirms the build.
- **Tokens.** Dataverse, App Insights REST and the `az webapp config appsettings list` read all came from `az`. No setting values were printed: only whether each key is present and how the values compare.
- **No writes.** Nothing was written to Dataverse, Entra, Key Vault, app settings or roles. No vault was read. No token, email, UPN or person name was printed or written.
- **Helpers.** The helper scripts lived in `.g2130tmp/` (`g2130_*.py`). The folder was deleted at the end.

**Verdict: PASS.** A1, A2, B1 and B2 pass. B3 and B4 are NOT-RUN because no guest signed in. No defect was found in either PR's scope.

### Gate table

| # | Check | Result | Evidence |
|---|---|---|---|
| A1 | Census run after the deploy | **PASS** | **Runs.** Three runs after the deploy: 18:15:01, 18:30:02 and 18:45:01Z (corr `65c7fe32…`, `9d394350…`, `158c81c2…`). Each logged `status=findings verdict=HumanPrincipalReachesSecureBusinessUnit findings=99`.<br>**Before the deploy.** The last run before the deploy (18:00:01Z, corr `1db35ebd…`) and the runs before it each logged `findings=84`.<br>**Ids only.** All 99 × 3 Critical findings name the principal as `systemuser <guid>`. Each run had 0 quoted names and 0 email matches. The run before the deploy had 63 of 84 messages carrying an email.<br>**Other verdicts.** None: 0 `AdministrativeRoleHeldByTeam` and 0 owner-role findings.<br>**84 → 99 (+15), matched by id.** The domain names in the earlier findings were mapped to systemuser ids in memory and not printed.<br>• **Added: 3 principals, +24 findings.** These come from the #2131 change that counts `#`-named users. All three are `#`-named, enabled, access mode 0, not application users, BU Spaarke. Two are `#EXT#` guests (`49f89867…`, `bcebc56c…`). Each holds Basic User and Office Add In User through team "Spaarke", 2 roles × 3 privileges = 6 findings each. The third (`849beec2…`) holds the same two roles through the team plus Core User and Office Add In User directly, for 12 findings. This is the three users the #2131 note describes.<br>• **Removed: 1 principal, −9 findings.** These come from the #2131 admin skip. `1d02f31c…` holds **System Administrator directly**. It is human, and its Global grant on each of the three guarded privileges reaches Secure Record. Its 9 non-admin findings were suppressed by `HoldsAnAdministrativeRoleThatReachesDirectly`: Basic User via the team ×3, Office Add In User via the team ×3, and Office Add In User directly ×3. Matching is by `PrincipalId`, and the rule requires the same privilege and a human principal. The exempting grant is direct System Administrator on the same `prvRead*`, so all three conditions hold.<br>• **No other principal changed.** Basic User direct: 1 → 1. Core User direct: 4 → 5 (the added `849beec2`). Office Add In User direct: 5 → 5 (+`849beec2`, −`1d02f31c`). Team "Spaarke": 9 → 11 (+3, −1).<br>**Where the findings come from (roles and teams only).** All findings are at Deep depth, anchored at the root BU "Spaarke" (`06fbf21c`), the parent of "Secure Record" (`d9ec0b6f`):<br>• **Team "Spaarke"**, the root BU's default owner team (`isdefault=true`), which holds *Spaarke Basic User* and *Spaarke Office Add In User*: 66 findings.<br>• **Direct assignments**: *Spaarke Office Add In User* (15), *Spaarke Core User* (15) and *Spaarke Basic User* (3).<br>The privileges are `prvReadsprk_Project`, `prvReadsprk_Matter` and `prvReadsprk_WorkAssignment`. |
| A2 | Emails in all BFF traces | **PASS** | **Windows.** After the deploy: 18:12:51–18:48:04Z, 22,538 traces. The baseline is the same length before the deploy: 17:37:38–18:12:51Z, 17,323 traces. The pattern is an email regex. Counts only; no address was printed.<br>**After the deploy: 42 hits, all outside the guard's scope.**<br>• `InboundPollingBackupService` (untagged, `Services/Communication`): 28 of 49.<br>• `GraphApiClient` HttpClient `LogicalHandler` and `ClientHandler`: 4 of 466 each (request URIs).<br>• `GraphMailFolderDeltaReader`: 4 of 4.<br>• `GraphSubscriptionManager`: 2 of 6.<br>**Baseline: 278 hits.**<br>• `[SECURE-CENSUS]` **126** and `[ID-LINK-RECON]` **98**, both in scope.<br>• The same Communication and HttpClient sources, 54.<br>**In-scope tags after the deploy, all 0 hits:**<br>• `[SECURE-CENSUS]` 300 lines and `[ID-LINK-RECON]` 119 (both `Services/ExternalAccess`, in scope);<br>• `[ID-BIND]` 8 and `[WF-AUTH]` 25 (`Infrastructure/ExternalAccess`, in scope);<br>• `[SECURE-CHILD-SHARES]` 40 and `[SECURE-CHILD-RECONCILE]` 43.<br>`[ID-BIND]` registration lines carry `tid#…`/`oid#…` hashes. The `[ID-LINK-RECON]` decision lines now carry the systemuser and contact ids only.<br>**Out-of-scope sources.** The Communication templates log `({Email})`, for example `InboundPollingBackupService.cs:158,184,209,218,250`, `GraphSubscriptionManager.cs:187,218,236,337` and `GraphMailFolderDeltaReader.cs:98` (`{Mailbox}`). These are covered by open **#2134**. The `System.Net.Http.HttpClient.GraphApiClient.*` lines log Graph request URIs that contain a mailbox address. That is framework HttpClient logging, not a template, and #2134's placeholder ratchet would not catch it (see Observations). |
| B1 | Startup wording | **PASS (no warning expected)** | `WorkforceIdentityOptionsValidator` warns only when `AzureAd:TenantId` is not a non-zero GUID. On dev, `AzureAd__TenantId` is a plain GUID equal to the `az` tenant, and it is listed as `WorkforceIdentity__CustomerTenantIds__0` (2 entries in all). So no warning is expected, and none was logged from 2026-10-13 to now: 0 old "admission disabled" and 0 new "is not a non-zero GUID". The code has **no** WorkforceIdentity summary line to show. The startup lines after the deploy are only "Now listening on: http://[::]:8080", "Application started" and "Hosting environment: Production" (18:11:46Z). The new wording is pinned by the PR's startup-text unit test, not observable live on dev. |
| B2 | No guest auto-create | **PASS** | **Dataverse.** 0 contacts with `sprk_identityplane eq 100000001` (WorkforceToken) and `createdon gt 18:12:51Z`. 0 WorkforceToken contacts modified since then.<br>**The one contact created since the deploy** is `1d4ab37a…` at 18:31:39Z, on plane **100000002 WorkforceHome**, the A9 home-directory door #2128 points guests to. It is not a guest auto-create.<br>• Op `10ecef86…`: `GET /api/v1/external/me` → **403** `sdap.access.registration_required`.<br>• Op `49c02133…`: `POST /api/v1/external/registration` → **201**, "[ID-BIND] Registration created contact … for tid#… oid#… (WorkforceHome)".<br>• Then `/external/me` → 200, entitlements, notifications and fetch, all 200.<br>• The home tenant `bc3aa7f4…` is `CustomerTenantIds__1`, not the stamp tenant.<br>**`[ID-BIND]` / `[WF-AUTH]` lines since the deploy: 33.**<br>• `[WF-AUTH] Resolved home … to WorkforceHome contact` ×11.<br>• `Workforce ContactOnly … resolved` ×11.<br>• `has no stored link and no WorkforceHome contact (registration_required)` ×2.<br>• `Denying request: sdap.access.registration_required` ×1.<br>• `[ID-BIND] Registration created contact` ×1.<br>• `[ID-BIND] GET contacts… 403 "not a member of the organization"` ×7: the known `spaarke-acme` binding-column probe of `[ID-LINK-RECON]`, every 5 minutes, also present before the deploy.<br>**Deny codes:** `sdap.access.registration_required` ×1. No `acct=1` or guest mention, no create or bind on the WorkforceToken plane, and no `workforce_guest_use_home_signin`. |
| B3 | Live guest refusal | **NOT-RUN** | No `workforce_guest_use_home_signin` line or deny in traces since the deploy (0). No interactive sign-in was attempted. |
| B4 | Licensed guests still admitted | **NOT-RUN** | The three guest-bound contacts map to systemusers as follows: `e6ddaa78…` → `ba65dad3…`, `97a8a4ae…` → `49f89867…` and `a7a8a4ae…` → `bcebc56c…`. All three were `Verified` by `[ID-LINK-RECON]` at 18:15Z. Since 2026-10-13, none of the three systemuser ids appears in any `[WF-AUTH]` line. Since the deploy, the only `[WF-AUTH]` caller is the WorkforceHome contact-only principal above. None of the three signed in. |

### Defects
- None in #2133's or #2128's scope.

### Observations (no defect filed)
- **The Graph HttpClient request logs carry mailbox addresses.** `System.Net.Http.HttpClient.GraphApiClient.LogicalHandler` and `.ClientHandler` log Graph request URIs at Information. Some URIs contain a mailbox address (4 of 466 lines after the deploy). This is framework logging, not a `{Email}` template, so #2134's placeholder ratchet would not catch it. A fix would set `Logging:LogLevel:System.Net.Http.HttpClient.GraphApiClient` to Warning, or redact the URI. Left to the main session to add to #2134 or file separately.
- **The census has more findings because of #2131, and that is intended.** The three `#`-named interactive users (two `#EXT#` guests) are now reported. They read secure records through team "Spaarke" (Deep, root BU). The designated administrator `1d02f31c…` is no longer mislabelled "a non-administrator human".

## Task 186 live gate (2026-10-20)

**Scope.** This is the server-side gate for task 186: design `186-model1-workforce-design.md` §6 steps 1–7, adapted to the A9 WorkforceHome identity. The token `iss`/`tid` is the customer home tenant, not an `acct=1` guest.
- **When and where.** Run 20:20–20:40Z against spaarke-bff-dev, spaarkedev1 and App Insights `spe-insights-dev-67e2xz`.
- **Build.** The BFF is `1.0.0+c3ec5d6e5`. Since 18:00Z there has been one instance (`…b830db`) and one "Application started". The code was read at `c3ec5d6e5`.
- **Tokens.** Dataverse, App Insights REST and the app-settings read all used the `az` login. No vault was read.
- **What was printed.** Ids, codes and timestamps only. No token, email, UPN or name was printed. The name check is a boolean comparison done in memory.
- **No writes.** The approved write step (5b) was **not run** (see below).
- **Helpers.** The helper scripts lived in a private scratch folder `g186_26e93c2e/` (`g186_*`), which was deleted at the end.

**Verdict.** Sign-in, registration, ownership, entitlements, idempotence, the no-systemuser check and the role privileges all **PASS**. Contact `1d4ab37a…` is **Tracy's registration**.
- **No intake was submitted.** Check 3 is **NOT-RUN**, and so the 5b remove/restore step is **NOT-RUN**.
- **The licensed-systemuser leg (6) is NOT-RUN.** No licensed user signed in during the window.
- **The Workforce role is not in SpaarkeMaster on `origin/master`.** It is excluded by `docs/data-model/package-scope.json` "until UAC task 186 closes", so the export check fails until the role ships.

**Why `1d4ab37a…` is Tracy's.**
- It is the **only** WorkforceHome (100000002) contact on all of dev.
- Its home tenant is `bc3aa7f4…`, Tracy's home tenant (the 186 gate earlier today).
- It was created at 18:31:39Z. That falls inside the only WorkforceHome sign-in session since the deploy (18:30:49–18:36:42Z).
- Its first name contains "Tracy" (an in-memory boolean; the value was not printed).

### Gate table

| # | Check | Result | Evidence |
|---|---|---|---|
| 1 | Contact: plane, binding, tenant list, owner, view | **PASS** (the view is absent: an SPA-r3 gap, see below) | **The contact.** `1d4ab37a-b4cc-f111-a05b-7c1e520a989f`. `sprk_identityplane` 100000002 (WorkforceHome). `sprk_externaltenantid` `bc3aa7f4…`. `sprk_externalobjectidkey` = home oid `0ce8b3f7…`, and the secured `sprk_externalobjectid` equals the key. statecode 0 / statuscode 1. `department` is set. No `parentcustomerid`.<br>**Timestamps.** createdon = modifiedon = 18:31:39Z.<br>**Created by.** `createdby` `8793f4b0…` is an application user (accessmode 4, root BU `06fbf21c`); there is no `createdonbehalfby`.<br>**Tenant list.** `bc3aa7f4…` is `WorkforceIdentity__CustomerTenantIds__1` (2 entries in all).<br>**Owner.** Team `cf15f587…`, the default (`isdefault`) owner team of BU `cb15f587…` "Spaarke Business Unit 1". `ExternalAccess__CustomerBusinessUnitId` matches `cb15f587…`. `owningbusinessunit` `cb15f587…`, **not** Secure Record `d9ec0b6f…`.<br>**View.** Of 27 contact views, none is named like "Workforce", "self-registered", "registered" or "Home". The design's §4b view "Workforce contacts (self-registered)" does not exist. Under A9 it is SPA-r3's, and it is unbuilt; this was already noted in the 186 gate earlier today. |
| 2 | Sign-in trail: `/me` 403 → registration 201 → `/me` 200 + entitlements | **PASS** | **Requests since the deploy (unsampled).**<br>• Op `10ecef86`, 18:30:49Z: `GET /external/me` **403**. Trace: "[WF-AUTH] Home tid=bc3aa7f4… oid=0ce8b3f7… has no stored link and no WorkforceHome contact (sdap.access.registration_required)", then "Denying request: sdap.access.registration_required". This is the only `/me` 403.<br>• Op `49c02133`, 18:31:38Z: `POST /external/registration` **201**, "[ID-BIND] Registration created contact 1d4ab37a… for tid#5006ACF2 oid#FBB65093 (WorkforceHome)". This is the only registration request.<br>• Op `cc613ed1`, 18:31:40Z: `GET /external/me` **200**, "Resolved home … to WorkforceHome contact 1d4ab37a…", then "Workforce ContactOnly (systemuser=(null), contact=1d4ab37a…)".<br>• Op `fd02fc7c`, 18:31:41Z: `GET /me/entitlements` **200**, "[EXT-ENTITLE] Workforce home principal — roles claim ignored, default modules only", then "[EXT-ENTITLE] Caller 1d4ab37a… (workforce) entitled to 2 module(s)".<br>**Why that means legal-front-door + policy-library.** For a home principal the resolver returns exactly `WorkforceDefaultModules` = `legal-front-door` + `policy-library` (`ModuleEntitlementResolver.cs:61,186-189` @ c3ec5d6e5). The ceiling intersection can only remove modules, so 2 modules means both survived. The log line gives the count, not the codes.<br>**The ceiling was read live.** "[WF-CEILING] Read role … (9deaac9c…): 660 privileges over 244 tables" at 18:31:43Z.<br>**Then.** Record, metadata and fetch calls, and 7 notifications polls, all 200, until 18:36:42Z. |
| 3 | An intake was accepted | **NOT-RUN: no intake exists** | 0 `POST /api/v1/external/servicerequests` requests since 18:12:51Z (any result).<br>0 `sprk_servicerequest` rows with `_sprk_requestedby_value` = `1d4ab37a…`, and 0 service requests created on dev since 18:00Z.<br>0 `sprk_invoicesubmission` rows created since 18:00Z. No BFF route creates invoice submissions; `invoicesubmission` appears nowhere in `src` on c3ec5d6e5 or on origin/master.<br>The session ended at 18:36:42Z. **The owner signed in and registered but did not submit.**<br>For the re-run: the intake writes `sprk_requestedby` = the contact and `ownerid` = the default team of the contact's BU (`ExternalIntakeEndpoints.cs:223-279`, `ExternalDataService.cs:1080-1088`). `sprk_servicerequest` has no `sprk_createdbycontact`/`sprk_createdbyperson`, so `sprk_requestedby` is its creator stamp (`ExternalDataService.cs` remarks at ~1055). |
| 4 | Resubmit idempotence | **PASS** (count) | Contacts with `sprk_externalobjectidkey` = home oid → **1** (`1d4ab37a…`), and the same with the secured `sprk_externalobjectid` → 1. There was one registration POST and no second one. The later `/me` calls resolved by oid with no create. A literal double submit was not exercised. |
| 5 | No systemuser for the identity | **PASS** | `systemusers` with `azureactivedirectoryobjectid` = home oid, any state → **0**. `systemusers` with `_sprk_primarycontact_value` = `1d4ab37a…` → **0**. `systemusers` created on 2026-10-20 → **0**. |
| 6 | The licensed user still resolves as a systemuser | **NOT-RUN** | Since 18:12:51Z there are 0 `[WF-AUTH] … to systemuser` lines. The `[WF-AUTH]`/`[ID-BIND]` lines are: WorkforceHome contact 13, ContactOnly 11, registration 2, CIAM 11 (contact `394fda9f…` at 20:03Z, a partner, not Tracy), and the known `[ID-LINK-RECON]` binding-column probe 403 ×29. The last systemuser resolution was at 03:38:20Z (1,410 in 14 days). No licensed user signed in during the window. |
| 7 | Workforce role: invoice-submission privileges, live and in the repo | **Live: PASS. Repo: FAIL (role not in the solution)** | **Live.** Root role `9deaac9c…` (BU `06fbf21c`, unmanaged, `modifiedon` 14:39:33Z) has 6 roles by that name (root + 5 BU copies). `RetrieveRolePrivilegesRole`: **660** privileges (Basic 315, Global 232, Deep 96, Local 17).<br>• `prvCreatesprk_InvoiceSubmission` (`be3c3238…`) = **Basic**.<br>• `prvReadsprk_InvoiceSubmission` (`1a2c034f…`) = **Deep**.<br>• Also: `sprk_servicerequest` Create, Read, AppendTo and Delete at Basic (no Write); `sprk_document` Create Basic and Read Deep.<br>**Repo (`origin/master` 0b976628e).** `src/dataverse/solutions/SpaarkeMaster/Roles/` has 12 role files, none of them the Workforce role, and no role file of that name exists in any ref's history. `invoicesubmission` appears nowhere under `src/dataverse`. `docs/data-model/package-scope.json:128-131` excludes the role "until UAC task 186 closes", so **the export does not include either privilege**. The role has to be exported into SpaarkeMaster as part of the 186 close; that is an owner or provisioning action. |

### The approved write step (5b): NOT-RUN

**It was skipped by rule.** The step requires checks 1–3 to pass, and check 3 has no intake. No privilege was removed or added.
- **Read-back (unchanged).** `prvCreatesprk_ServiceRequest` (`fd03f48f…`) on root `9deaac9c…` = **Basic**. This is the privilege the intake needs: `POST /servicerequests` declares `WorkforceRoleCeilingGate.SubmitServiceRequest` = (`sprk_servicerequest`, Create) (`ExternalIntakeEndpoints.cs:110-112`, `WorkforceRoleCeilingGate.cs:69-70`).
- **How the refusal would be shown server-side.**
  - `WorkforceCeilingEndpointFilter` (`WorkforceCeilingEndpointFilter.cs:147-189`) calls `WorkforceRoleCeilingGate.DenyAsync`. That reads `GetWorkforceRoleCeilingAsync` (`WorkforceRoleCeilingGate.cs:224-241`) and returns 403 `sdap.access.deny.workforce_baseline_privilege_missing`.
  - The ceiling is the live root role, read through `RetrieveRolePrivilegesRole` (`UserPrivilegeChecker.cs:556-632`). It is cached in `ITenantCache` for `WorkforceCeilingTtl` = 5 min (`UserPrivilegeChecker.cs:85,529-535`), and a fault is never cached.
  - It is read **only** when a workforce-contact request arrives. So "refused within 5 min" cannot be shown without the contact submitting. A new `[WF-CEILING] Read role … 659 privileges` line would show the reload, but not the refusal.
- **Re-run plan (owner-driven).**
  1. The owner submits intake #1 as Tracy → expect 201.
  2. Record `fd03f48f…` at Basic, then RemovePrivilegeRole.
  3. At least 5 minutes later, the owner submits intake #2 → expect 403 `workforce_baseline_privilege_missing`.
  4. AddPrivilegesRole at Basic within 10 minutes of the removal, then read it back.

### Defects
- None in task 186's BFF scope.

### Gaps (not code defects)
- **The Workforce role is missing from the SpaarkeMaster solution** (check 7, repo leg). It cannot reach a customer stamp until it is exported. That export is a close-out item for task 186.
- **The §4b contact view "Workforce contacts (self-registered)" does not exist on dev or in source.** It is SPA-r3's under A9.

### Observations
- `[WF-AUTH]` lines log the home `tid` and `oid` as full GUIDs. The `[ID-BIND]` registration line hashes the same values (`tid#…`/`oid#…`). These are pseudonymous ids, not email or PII, so nothing is filed. The two lines are inconsistent only.

## #2121 live gate (2026-10-20)

**Scope.** Server-side gate for #2121 (PR #2122, merged as `0b976628e`). Run 20:39–21:13Z against spaarke-bff-dev, spaarkedev1 and App Insights `spe-insights-dev-67e2xz`.
- **Identity.** Admin only (the default `az` login, systemuser `1d02f31c…`, System Administrator). Tokens: ARM (Kudu VFS read), Dataverse, and the delegated BFF token `api://1e40baad…/.default`. No token, email, UPN or name was printed or written; the `/me` bodies were reduced to their key names.
- **Writes.** None: no Dataverse, share, role, team, app-setting, Entra or Key Vault write. No Key Vault was read.
- **Generated traffic.** 4 read-only GETs as admin: `/api/v1/external/me` at 20:50:41Z and 21:04:41Z, `/me` at 21:05:11Z, and `/me/entitlements` at 21:05:16Z. Without them the window had no `/api/v1/external` traffic.
- **`65a3fab2…`.** Read-only only: its share rows, one `RetrievePrincipalAccess`, and the BFF's own read-only compositions for the admin, who reaches it.
- **Helpers.** In a private scratch folder `g2121_18c2c558/` (`g2121_*`). It was deleted at the end.

**Verdict: PASS.** A–E pass, and option (b) was seen working on live traffic. No defect was found in the #2121 code. Check D's exact scenario (a lookup-only Secure reach by an internal user who holds **no** share) has no data on dev; see D.

| Check | Result | Evidence |
|---|---|---|
| A. Deploy health | **PASS** | **Deployment.** ARM OneDeploy `87b542aa…`, 20:35:58–20:36:03Z, status 4, active.<br>**Build.** Kudu VFS `Sprk.Bff.Api.dll`: 19,876,352 B, sha256 `5bc3f109…`, InformationalVersion **`1.0.0+0b976628e48b0082c6235a40e9e3508603f5cd81`**. It contains `ConfirmSecureLookupOnlyAsync`, `DataverseConfirmedTermRights`, `MaxSecureLookupRightsChecks`, `SecureLookupRightsCacheId` and the `secure-lookup-rights` literal.<br>**Startup.** "Application started" at 20:36:31Z on one instance, `39ab6951…`. `application_Version` is empty in the telemetry, so the version comes from the DLL.<br>**Startup errors.** The only one is `RoutingConsumerTypeHealthCheck FAILED` (AI catalog drift) at 20:36:29Z. It is pre-existing: it is logged on every boot since at least 2026-10-18 03:34Z (9 boots). `/healthz` and `/ping` return 200. |
| B. Option (b) on live traffic | **PASS** | **No organic traffic.** Between 20:36Z and 21:13Z the only `/api/v1/external` requests are the 4 the gate made.<br>**The path ran.** In op `853a1f01` (21:04:41Z), "1 Secure sprk_project record(s) reached only through a registry lookup column or business-unit ownership keep no membership-term access" (Information); the Debug id list is `65a3fab2…`.<br>**The BFF asked Dataverse.** One app-only dependency, `GET systemusers(1d02f31c…)/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@p1)`, returned **200**. Then: "Dataverse confirmed Read on **1 of 1** … (denied 0, gone 0, faulted 0, over the ceiling 0); a confirmed record carries Read only".<br>**Cache.** In op `584a154a` (21:05:16Z, 35 s later, inside the 60 s TTL), the same summary "1 of 1" appears with **no** `RetrievePrincipalAccess` dependency, so the answer came from the cache.<br>**Line counts since 20:36Z.** Lookup-only drop (Information) ×2, id list (Debug) ×2, confirmed summary ×2. `check FAULTED` ×0 (no 403, 5xx or timeout), 404 ×0, denial ×0, ceiling-50 Warning ×0, rights-cache fault Warning ×0, no-store Warning ×0, ownership-walk capped ×0.<br>**`[NO-ACCESS-ENFORCE] Dataverse read FAILED`: 0**, so the store's Error never fired for the composer (F2). The only `[NO-ACCESS-ENFORCE]` lines are the pre-existing Warning "Entry `33a4e845…` is malformed", from the enforcer job every 5 min (822 lines since 2026-10-18 before the deploy). |
| C. Prerequisite (`RetrievePrincipalAccess`, app-style, named principal) | **PASS** | **Who asked.** The admin CLI identity (`1d02f31c…`), with no `MSCRMCallerID`, so each answer is for the named principal. The BFF's own identity is not reachable from the CLI, but it made the same call live in B, app-only: 200 with Read, so its app user has the read.<br>**(i) Holds access → Read.**<br>• Fixture `a9e04977…`: its only share is the admin (`principalobjectaccess` mask 262167). The owner team `6eabc7f9…` has **0 members**. Admin → `ReadAccess, WriteAccess, …` (but admin is also System Administrator).<br>• A share-only non-admin: `8d7bad7a…` (BU `cb15f587…`; Basic, Core and Office Add In roles) on Secure project `31e232ae…`, which it holds by a share (mask 262167) → `ReadAccess, WriteAccess, AppendAccess, AppendToAccess, ShareAccess`.<br>**(ii) Ordinary internal user with no access → no Read.** `8d7bad7a…` and `d6f8f439…` (both BU `cb15f587…`) on the fixture → `None`.<br>**Root-BU users get Read.** `7c35232f…` (Core User), `ba65dad3…` (Core User) and `49f89867…` (no direct role) on the fixture → **Read and more**. The Secure BU `d9ec0b6f…` is a **child of the root BU** `06fbf21c…`, so a Deep-depth read role held in the root reaches it. This is the `[SECURE-CENSUS] SECURE PROJECTS ARE NOT ISOLATED` Critical (232 lines in the 36 min before the deploy, 198 after) and the topology item in #1094 (Secure Record BU as a root sibling). It is not #2121: option (b) correctly returns Dataverse's own answer, which the model-driven app also honours. |
| D. Over-grant scenario data | **PASS (one pair; it holds a share)** | **Secure records on dev.** 3 projects (`31e232ae…`, `65a3fab2…`, `a9e04977…`) and 1 matter (`5d46df87…`), all owned by team `6eabc7f9…` in BU `d9ec0b6f…`. There are 0 Secure work assignments. 0 work assignments are filed under a Secure matter or project, directly or through an invoice or event. The Secure BU has **0 users**, so no BU-only (K6) reach exists. The registry is the canonical seed (no `Membership__AccessConferringRoles` app setting).<br>**Assigned lookups.** One Secure record has a registry Assigned lookup: `65a3fab2…`, whose `sprk_assignedattorney1` and `sprk_assignedparalegal1` are contact `8e9918a9…`. That contact is the linked contact of systemuser **`1d02f31c…`** (internal, enabled, root BU). It is the only pair, and the user is **not** without a share: they hold a POA share (mask 262167) and System Administrator. So "an internal user with no share" has **no data on dev**; that path is covered by the unit tests only.<br>**What the BFF does for the pair.** Check C's call → `ReadAccess, …`, so the BFF **keeps it at exactly Read**, and B shows it doing so live. |
| E. Regression (`/api/v1/external` mix) | **PASS** | **Windows.** Post 20:37–21:13Z against pre 20:01–20:37Z, OPTIONS excluded.<br>**Pre.** 11 requests on 9 route templates, all 2xx (one CIAM contact session, contact `394fda9f…`).<br>**Post.** 2 recorded (`/me` 200 and `/me/entitlements` 200), both the gate's own.<br>**All routes.** 0 5xx and 0 403 in either window (pre 70 requests, post 44). No route went from 2xx to 403.<br>**Errors post.** `[ID-LINK-RECON] spaarke-acme binding column` ×7 (pre ×8) and `[SECURE-CENSUS]` ×198 (pre ×232), both pre-existing. `IDX10214 Audience validation failed` ×2: these are the gate's own requests (ops `853a1f01` and `584a154a`), the CIAM scheme failing before the workforce scheme succeeds; 221 on 2026-10-19. `Microsoft.Graph ODataError "Invalid hostname for this tenancy"` ×3 post (32 pre), pre-existing. |

### Defects
- None in #2121's code (`AccessibleRecordSetService.cs` option (b) at `0b976628e`, `NoAccessEnforcementStore.GetPrincipalRightsAsync`). The live path logs, asks, caches and keeps at Read as the PR describes.

### Observations (not #2121)
- **Secure isolation on dev (pre-existing, filed: #1094).** The Secure BU is a child of the root BU, so root-BU users with Deep-depth read roles (`7c35232f…`, `ba65dad3…`, `49f89867…`) get Read on Secure projects from Dataverse. On the systemuser plane, option (b) therefore keeps such a user's lookup-only Secure reach at Read. That matches the model-driven app, and the census reports the topology as Critical.
- **Two of the gate's four requests are missing from App Insights.** `/me` at 20:50:41Z and at 21:05:11Z both returned 200 to the client. Neither has a request, trace or dependency row: there is no `[WF-AUTHZ]` line and no `RetrievePrincipalAccess` dependency, while the neighbouring ops are complete. One instance is running and `itemCount` is 1 on every row. The cause was not established. The likely one is the trace sampling of the Azure Monitor OpenTelemetry distro (`Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0, `UseAzureMonitor()` with defaults, `Program.cs:36`). It is not filed: the cause is unconfirmed and it is not #2121's. A live gate that relies on "no Warning seen" can miss a dropped op.

## 036 FR-20 flag-on live gates (2026-10-20)

**Scope.** Server-side gates for task 036 (FR-20) after the owner set `ExternalAccess__ImpersonatedRootSets__Enabled=true` on spaarke-bff-dev at ~23:51:26Z (build master `4f5f22694c`). Run 23:55–00:13Z against spaarkedev1 and App Insights `spe-insights-dev-67e2xz`.
- **Identity.** Admin only (`az` login, systemuser `1d02f31c…`); delegated BFF token `api://1e40baad…/.default`. No token, email, UPN or name printed or written.
- **Writes.** None (no Dataverse, share, role, team, user, app setting, Entra or Key Vault). No Key Vault read. `65a3fab2…` untouched.
- **User checks.** As the BFF asks: `systemusers(<id>)/RetrievePrincipalAccess` and `RetrieveUserSetOfPrivilegesByNames` with `MSCRMCallerID = <id>`; root sets read with `MSCRMCallerID`.
- **Generated traffic.** 4 admin GETs: `/me` (op `51158874`), `/projects` (op `ee7ed1c9`), `/me` (op `6772e9a1`), `/projects/a9e04977…/todos` (op `b7553731`), 23:58:29–23:58:52Z.
- **Rerun note.** `az monitor app-insights query` looks back 1 h unless `--offset` is passed (`--offset 72h` used for baselines).

**Verdict: PASS server-side.** (i) canary PASS (task-034 note §10); (vii) and Verify PASS; (ii) decision rule PASS server-side; (iii)–(vi) NOT-RUN for lack of a no-write dev fixture — owner setup + SPA steps below.

| Check | Result | Evidence |
|---|---|---|
| (vii) disabled / unlicensed / team-only role (STOP check) | **PASS (no STOP)** | **Disabled** `834a41ae…` (root BU, roles only via root default team `09fbf21c…`) on matter `0ee64da4…`: RPA 403 `0x8004d24d` ("not assigned any License and is disabled"); privilege set 403; `sprk_matter`/`sprk_project` read 403, 0 rows → the impersonated read fails, composition fails, request refused. **Unlicensed** `a7cbf3bd…` (`islicensed=false`, `accessmode=1`, no System Administrator directly or via team; the only enabled unlicensed interactive user on dev): RPA 403 `0x80040220` (missing prvReadsprk_Matter; "unlicensed user with filtered privileges") → BFF reads None; reads 403, 0 rows. **Team-only role** `49f89867…` (0 direct roles, team `09fbf21c…`): RPA on matter `0ee64da4…`, projects `5496dc6f…`/`edb87d10…` = R/W/A/AT/C/Assign (no Delete); impersonated read 62 matters / 23 projects; holds `prvCreatesprk_Event` Global, `prvCreatesprk_Communication` Basic; not `prvCreate/Write/Deletesprk_Todo`, `prvCreatesprk_Document`, `prvDeletesprk_Event` → BFF allows event create + posting, refuses to-do create/edit, upload, own-event delete (guide §12.8 item 4). Model-driven-app half: owner. |
| Verify (access source, `/me`) | **PASS** | "Application started" 23:52:00Z (instance `39ab6951…`). `/me` 200 ×2, `/projects` 200 (23), todos 200. `/me` keys `contactId, email, projects[23], systemUserId, writeAccessPerRecord`; **`writeAccessPerRecord: true`**; 22 ViewOnly + 1 FullAccess (`b12496d1…`, contact-grant union, not traced). `[WF-AUTHZ] Access source for systemuser 1d02f31c… : ImpersonatedRootSet` ×12 (4 ops × project/matter/work assignment); **0 `Membership`**. Todos op: one app-only `systemusers(1d02f31c…)/RetrievePrincipalAccess` 200 → `RetrieveUserSetOfPrivilegesByNames` 200; log "… (composed Read; unanswered None; answered via app-only-named-principal)"; `canCreate: true`. |
| (ii) Read but not Write | **PASS-server-side (decision rule); owner end-to-end pending** | No fixture: every non-admin holds Write wherever it holds Read (BU1 users `d6f8f439…`, `8d7bad7a…` Deep; root-BU users Global via `09fbf21c…`). **Owner setup:** share matter `0ee64da4…` Read-only with canary `d6f8f439…`; confirm RPA = ReadAccess only. **SPA as the canary:** matter listed (ViewOnly in `/me`), document list `canCreate: false`, Upload hidden, direct upload POST → 403, nothing written; MDA form read-only. Remove the share after. |
| (iii) in BU, outside role depth | **NOT-RUN (vacuous on dev)** | No dev user has read depth narrower than BU (BU1 Deep, root Global), so the over-grant the flag closes cannot occur here; covered by unit tests. Depth evidence: impersonated root sets equal the app-only expected sets exactly — canary projects 19/23, matters 58/62, work assignments 0/22; testuser1 projects 20/23 (incl. share on Secure `31e232ae…`), matters 58/62, WA 0/22; every unlisted record answers RPA 403 `0x80048306` (→ None). Running it needs a User-depth (Basic) read role — owner's call. |
| (iv) Assigned To names the user's contact | **NOT-RUN; owner end-to-end pending** | All 53 Shared task-142 ledger rows are for the admin (share mask 262167, RPA full, GET 200) — not dependent. No non-admin contact (`51a5ddb0…`, `ac6d7b68…`) is in any Assigned field. **Owner setup (batch-4 S4):** set `ac6d7b68…` as Assigned Attorney 1 on root-BU matter `d14d79f4…`; confirm ledger Shared, testuser1 `8d7bad7a…` share mask 262167, RPA full. **SPA as testuser1:** matter listed (ViewOnly), lists `canCreate: true`. Then clear the field (S6) → share and listing gone. |
| (v) Secure record + No Access List | **NOT-RUN; owner end-to-end pending** | Only active entry `33a4e845…` (canary on project `edb87d10…`) is on a non-secure root-BU project, so not in force. **Owner setup:** entry for testuser1 `8d7bad7a…` on Secure project `31e232ae…` (today: share mask 262167, RPA full, listed). **Expected (task 143):** share removed, RPA 403 `0x80048306`, not listed. **SPA as testuser1:** absent from `/me` and `/projects`; `GET /projects/31e232ae…` → 403; MDA cannot open. Delete the entry after; restoring the share is the owner's call. |
| (vi) WorkforceHome sign-in | **NOT-RUN** | Needs a WorkforceHome internal SPA sign-in. Only WorkforceHome traffic in the window: a ContactOnly caller (contact `1d4ab37a…`, `systemuser=(null)`), decided on the contact plane as before (0 roots, 200 ×3) — unchanged by the flag, as designed. |
| Regression 23:51:26Z → 00:12:35Z | **PASS** | 96 requests: **0 5xx, 0 403**; 7 401 (anonymous `GET /`, `/robots…`, `/api/me`). External: `/me` ×3, `/me/entitlements`, `/notifications`, `/projects`, todos, `can-manage-access` ×2 — all 200. Every systemuser: `sdap.access.dataverse_unavailable` 0, `[DELEGATION-RPA-UNAVAILABLE]` 0, `[DELEGATION] Rights … : None` 0, "treated as UNKNOWN" 0, "composition FAILS" 0. Errors (pre-flip hour → post), all pre-existing: `[SECURE-CENSUS]` 396 → 99 (#1094), Graph "Invalid hostname" 12 → 4 (#1830), `[ID-LINK-RECON]` acme 12 → 4 (#2163), `RoutingConsumerTypeHealthCheck` 1 → 1; `IDX10214` 0 → 7 / `IDX40001` 0 → 3 = the multi-scheme auth attempts on the post-flip external requests (same lines 117–582/day on 10-18/10-19; every request 200). |

**Defects in 036 code:** none. Refusals traced: `RetrievePrincipalRightsOrUnknownAsync` (`0x80048306`, `0x80040220` → None; `0x8004d24d` → UNKNOWN), the Read-first rule in `WithDataverseRightsAsync`, the privilege asked only after AppendTo, the composition with no catch on the impersonated read.

**Observations:**
- **Load-bearing ordering (filed: #2167).** For unlicensed Administrative-mode `a7cbf3bd…`, the impersonated `RetrieveUserSetOfPrivilegesByNames` **lists** `prvCreatesprk_Event` (Global) and `prvCreatesprk_Communication` (Basic) although RPA refuses. The BFF is safe only because a privilege is never asked on its own (Read first; privilege only after RPA answers AppendTo). A future route that asks the privilege alone would let an unlicensed user write → mechanise the invariant.
- **Disabled-user refusal status (filed: #2168).** A disabled systemuser's `0x8004d24d` classifies as UNKNOWN, so its refusal takes the fault path (503 `sdap.access.dataverse_unavailable`) instead of 403 — fail-closed, but reported as an outage.
- **testuser1 and `edb87d10…`.** ViewOnly via the contact-grant union where Dataverse gives nothing; listed in SPA, writes 403; not in MDA. Designed union, not a parity break.
- **Not verified.** Hits on the 2-minute impersonated root-set cache (telemetry does not mark impersonated reads).
