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
