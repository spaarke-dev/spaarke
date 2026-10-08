# Batch 5 live record, 2026-10-08

## Task 154: No Access forms (PR #1393, `39ba73dbc`), applied to spaarkedev1 2026-10-08 ~04:07–04:30Z

1. **BFF:** deployed; healthz 200.
2. **Form library:** `sprk_/scripts/noaccessentry_postsave.js` deployed.
3. **`Deploy-NoAccessEntryForms.ps1 -Apply`:** applied the entry form, the view, the Organization NO ACCESS section (2 subgrids), the Contact NO ACCESS section (1 subgrid) and the site-map subarea. Snapshot `C:\wtR2\scripts\logs\noaccess-forms-snapshot-20261008001043.json`.
   - **Gap:** `AddAppComponents` answered 204 and added nothing. `-Verify` shows "sprk_noaccessentry is not a component of the app"; `ValidateApp` passes.
   - **For the owner:** check whether "No Access Entries" shows in the left nav; if not, add the table in the app designer. The script now warns instead of claiming success (PR #1407).
4. **`Deploy-NoAccessEntryRibbon.ps1 -Apply`:** a no-op the first time. `& pac` resolved to the `~/bin/pac` bash shim under Git Bash, so the post-import check failed. A manual `pac.cmd` import then worked, and `-Verify` PASSES (Add Existing hidden; 2 HideCustomAction diffs). Fixed in PR #1407.
5. **`Set-NoAccessEntryRolePrivileges.ps1`:**
   - The first `-Apply` created "Spaarke Access Administrator" with its set, then failed on `RemovePrivilegeRole`, because the payload shape was wrong. Fixed in PR #1407.
   - The re-run removed Read from Spaarke Core User and assigned the role to ralph.schroeder@spaarke.com. `-Verify` PASSES (O2).
   - The platform added 9 default privileges (SharePoint data, plugin/SDK reads at Global), which are reported only.
   - Microsoft's service roles were left as they are; per the main-session default, the owner did not object.
6. **Owner manual live gate:** checklist (a)–(p) in `notes/task-154-no-access-management-forms.md`.

## Task 105: external data paging (PR #1408, `37d0c944c`, closes #963), deployed 2026-10-08

- **Deployed:** the BFF (healthz 200) and the external SPA (workflow `deploy-external-spa.yml`, run 37741946955, success).
- **Live gate pending owner OK:** seed 250 `sprk_document` rows on a test project, then confirm the external `/documents` returns 250 with no `truncated` and the SPA shows no notice. No dev project has more than 200 children today.
- **Filed:** #1409 (an external to-do gets an empty regarding name on a transient read failure).

## Task 064: per-record No Access read (PR #1411, merged `c5f71487f`), live gates 2026-10-08 14:53–15:01Z

Run against spaarke-bff-dev (healthz 200) and spaarkedev1, with the existing identities only: admin (ralph.schroeder, `az`) and testuser1 (non-admin, BU1, `AZURE_CONFIG_DIR=C:/tmp/az-uac-child`). Tokens: `api://1e40baad…/.default`. Route: `GET /api/v1/records/{type}/{id}/no-access`. No app setting, Entra or Key Vault change.

**Throwaway records** (all created as admin; all deleted and read back 404 at 15:00:54Z; a final query found 0 `zz-064*` entries, work assignments or projects, and `sprk_noaccessentries` holds 0 rows, as before the gates):
- project PRO `0fa6100f…` ("zz-064-gate read-only project", non-secure), shared **Read only** to testuser1 (RetrievePrincipalAccess as testuser1: `ReadAccess`);
- contact C1 `3e2ae113…`;
- E1 `f0c7f50f…`: subject C1, object PRO;
- E2 `bdc8a712…`: subject testuser1 (systemuser), object PRO (non-secure);
- E3 `9579fb14…`: subject uac.child (systemuser, no share anywhere), object the existing secure project `31e232ae…` ("UAC gate 171 … (secure)"; only read, never written);
- W1 `a5332ff5…`: a work assignment filed under `31e232ae…` (`sprk_regardingproject`), created at 15:00:21Z (21 s after the */5 secure-inheritance run) and deleted at 15:00:28Z, before any job could secure it. Its etag and owner were unchanged, so nothing was provisioned for it.

**Side effects checked:** the No Access reconciliation job ran with E1–E3 active (14:55, 15:00). Shares were unchanged: `31e232ae…` kept testuser1 262167, and PRO kept testuser1 Read until it was deleted. uac.child had no share to remove, and E2 is on a non-secure record.

| # | Gate | Result | Evidence |
|---|---|---|---|
| G1 | The route answers on each of the three tables | **PASS** | admin, 200: `sprk_project` PRO; `sprk_matter` a333c338… and `sprk_workassignment` 84e8f01a… (`secure`/`noAccess` doesNotApply, `entriesState` complete, `entries` []). `recordType`/`recordId` echoed. `/api/v1/records/contact/{id}/no-access` → 404 (not mapped). |
| G2 | Unknown vs denied: the same uniform 404, and the same time | **PASS** | Bodies match except the correlation id: `reasonCode` `sdap.access.deny.record_unavailable`, detail "The requested record was not found.", no id. Cases: testuser1 on matter d14d79f4… (Dataverse 403 0x80048306); testuser1 and admin on unknown ids. Timings over 6 interleaved samples: denied 2.27–2.70 s, unknown 2.25–2.59 s. Allowed (testuser1 on a333c338…): 0.71–0.86 s. The F3 equalisation holds live. |
| G3 | Not signed in | **PASS** | 401. |
| G4 | A Read-only caller gets the signals only | **PASS** | testuser1 on PRO: 200 `{"secure":"doesNotApply","noAccess":"applies","entriesState":"notShown","entries":null}`. No entry id, subject id, name or reason in the body. |
| G5 | A Write caller gets the entries (shape, names, inert user wall) | **PASS** | admin on PRO: E1 contact wall `inForce:true`; E2 user wall on a non-secure record `inForce:false`, `notInForceReason:"userWallOnNonSecureRecord"` (F4). Names come from formatted values (subject, modified by). No `reason` field. |
| G6 | A non-admin Write caller on a Secure record | **PASS** | testuser1 (share 262167) on `31e232ae…`: `secure:"applies"`, `noAccess:"applies"`, E3 user wall `inForce:true`. |
| G7 | A secure-parent entry is in force on a filed child whose own flag is No (owner ruling "the parent permissions control") | **PASS** | admin on W1 (`sprk_issecure` No): `secure:"doesNotApply"`, `noAccess:"applies"`; E3 `viaSecureParent:true`, `coveredRecordType:"sprk_project"`, `coveredRecordId:31e232ae…`, `inForce:true`. |
| G8 | After the entries are removed | **PASS** | admin on `31e232ae…`: `noAccess:"doesNotApply"`, entries []. |

**Not run live (need an owner step or a second human account):**
- **G9: the overlap case.** A secure parent and its filed child both reference the walled organization, and the wall is a user wall. The only secure parents in dev are `31e232ae…` (no organization lookup set) and `65a3fab2…` (do-not-touch), so this would mean writing an organization lookup on a gate record or provisioning a new secure parent, which creates an SPE container. Covered offline: `RecordNoAccessEndpointTests.AUserWallOverAnOrganizationTheRecordAndItsSecureParentBothReference_IsInForce_…`. Steps with owner OK:
  1. Set an organization lookup (e.g. `sprk_assignedlawfirm1`) on `31e232ae…` to org X.
  2. Create a work assignment filed under it with the same lookup set to X, at least 20 s after a */5 boundary.
  3. Create an entry with subject = uac.child and object organization = X.
  4. GET the route on the work assignment as admin.
  5. **PASS:** `noAccess:"applies"`, a single row with `viaSecureParent:false`, `alsoViaSecureParent:true`, `inForce:true`.
  6. Delete the entry and the work assignment, clear the lookup, and read each back.
- **G10: the consumers' UI** (task 153's form banner; task 067's Manage Access list). These belong to 153 and 067: a Read-only human and a Write human in the browser, live session D.

**Defects found:** none in 064. The only items seen were test-data limits (no secure matter or work assignment exists in dev), which is why G9 is listed above.

**064 can be marked done.** G1–G8 pass; G9 is covered offline and listed for an owner-approved run; G10 belongs to 153 and 067.

## Gates run 2026-10-08 (gatesA)

Owner-approved in round 83. Dev BFF = master `c5f71487f` (healthz 200); spaarkedev1. Identities as in batch 4: admin (`az`
default, ralph.schroeder), testuser1 (`AZURE_CONFIG_DIR=C:/tmp/az-uac-child`; linked contact `ac6d7b68…`), and the
container type's owning app `170c98e1…` for Graph reads (client credentials; secret read from `sprk-prod-kv`, never
printed). No app-setting, Entra, Key Vault or container-type change; no `-MintClientSecret`; `bfac7f6e` untouched.
Raw logs: session scratchpad `gatesA/` (`g0-*.json|txt`, `g1.log` … `g10.log`, `g23-verify.log`, `g024.log`,
`deploy-results.md`, `g147-6-*.log`). Times are UTC.

| # | Task / gate | Result | Evidence |
|---|---|---|---|
| 0 | Container clean-up pre-check (21 ids in `Remove-TestContainers.ps1`) | **SAFE ×21** | 23 container-id columns found by a metadata scan (business unit, project, matter, work assignment, document `sprk_containerid` + `sprk_graphdriveid`, `sprk_container` `sprk_specontainerid` + `sprk_driveid`, communication, communication attachment, event, invoice, budget, organization, report card, upload context, analysis, playbook, analysis working version, AI search index, billing event, Dataverse environment, SPE config default container): **0 rows** reference any of the 21 (`In` filter; a positive control on two live project containers returned their 4 rows). Graph read as the owning app: **20 × 200** `status: active`, type `8a6ce34c…`, display names "Secure Project/Matter/Work Assignment – UAC gate … 2026-10-06/07" (request ids in `g0-graph.txt`); `b!HBRbo…` → **400** "Invalid hostname for this tenancy" (the known orphan; the delete script will report it SKIPPED). |
| 1 | 105 §6 (paging) | **PASS (API)** | Project **`edb87d10-2ac3-f111-a05c-6045bdfe7b25`** "UAC gate 105 paging 2026-10-08 (owner SPA check)", contact grant **`144b5110…`** (ViewOnly, `/grant` 200). 250 `sprk_document` seeds (5 × 50 `$batch`, all 204) 15:08:34–45Z. testuser1 `GET /api/v1/external/projects/edb87d10…/documents` → **200, 250 rows, ids = the seeded set, no `truncated` key** (body keys `["value"]`). Seeds deleted 15:10:47Z (250 × 204), read back **250 × 404**; the project now has 0 documents. |
| 2 | 114 (3) admin reports | **PASS — nothing to action** | `assigned-access-reconciliation` (*/5, enabled): every run since the 114 deploy (258 runs, 2026-10-07 17:21Z → 2026-10-08 15:10Z, App Insights) reports `noInternalReader=0 ownerIsExternal=0`; `restrictedCandidates` went 1→2→3. The last 5 runs `Succeeded`, `externalSharesRemoved 0`. `[RESTRICTED-EXTERNAL]` traces are info level only, for 3 Restricted matters (`dc091784…`, `b68299c6…`, `232852f8…`), each "removed 0, no internal reader False, external owner none, failures 0"; **0** warnings over 7 days. |
| 3 | 142 G-4 form libraries | **PASS** | Project main form `5aa00242…`, Matter main form `4fa382f2…`, Work Assignment main form `7e578eef…`: `sprk_/scripts/bff_auth.js` loads before `sprk_/scripts/assignedaccess_postsave.js` (indexes 0/1, 4/5, 0/1); OnLoad `Spaarke.AssignedAccess.onLoad`, `passExecutionContext=true`, enabled, on all three. The three "Information" forms carry neither library but are in no app (0 `appmodulecomponents`); the main forms are in all four Spaarke apps. |
| 4 | 154 (h) | **PASS** | testuser1 Web API `GET sprk_noaccessentries` → **403 `0x80040220`** "missing prvReadsprk_noaccessentry privilege" (not 200 []). |
| 4 | 154 (b) organization wall | **PASS** | Org O `1ef48a0c…`, matter M `a2f7be0c…` (`sprk_assignedlawfirm1` = O), contact grant `eac88a1c…`. Before: testuser1 `GET /api/v1/external/matters/M/todos` → 200. Entry Eb `a4b08d2c…` (subject contact ac6d7b68, object org O) + `/no-access/enforce` → 200 `evaluated`. After: **403** "You do not have access to this matter". |
| 4 | 154 (d) record wall via the stored id | **PASS** | Project P `2846590a…`, grant `3346590a…`. Before: `GET /external/projects/P` → 200. Entry Ed `ceef0a29…` (subject contact, object type project, id = P in lower case). After: project **403**, documents **403**, P gone from `/external/me`. Control: Ed deleted → P **200** again, while Eb still held M at **403**. |
| 4 | 154 (j) after the role change | **PASS** | (b)/(d) ran after `Set-NoAccessEntryRolePrivileges` removed Core User's Read: the BFF's app-only deny read still denies the walled contact. |
| 4 | 154 (o) seed | **SEEDED — left for the owner** | Entry **`33a4e845-2bc3-f111-a05c-3833c5ef3c0c`** "UAC gate 154 (o) braced id 2026-10-08 - owner check": subject systemuser uac.child `d6f8f439…`, object type project, `sprk_objectrecordid` = `{EDB87D10-2AC3-F111-A05C-6045BDFE7B25}` (the gate-105 project, braced, upper case). A user wall on a non-secure record is not in force, so it blocks nothing. modifiedon 15:16:48Z. |
| 5 | 163 (b) literal | **PASS** | Matter `fe29cb85…` created by admin and provisioned → 200, owner Secure Record Owners, container `b!R6Qrq…`; testuser1 RPA None. Doc `864c0ab7…`: `POST /api/v1/child-records/sprk_document` 201, app-only `PUT /api/obo/records/sprk_matter/…/files` 200, attach 200, `POST /api/ai/rag/index-file` 200 `chunksIndexed 1`, `sprk_searchindexcompletedon` stamped. `POST /api/ai/rag/search {topK 20, minScore 0}` with two marker queries: **admin finds the chunk** (`documentId 864c0ab7…`); **testuser1 gets 3 and 5 rows, 0 chunks of it**. Clean-up: chunk deleted (`DELETE /api/ai/rag/{key}` 200; re-search 0), file deleted (Graph 204, then 404), doc and matter deleted (204, then 404). |
| 6 | 164 (j) Copilot agent | **NOT AVAILABLE to the agent** | Deployment cannot be confirmed with the existing identities: the Teams app catalog read is 403 for the `az` token (no AppCatalog scope); `appPackage/env.dev.json` `TEAMS_APP_ID` is the zero GUID; App Insights shows 24 `/api/agent/*` requests in 90 days, the last `POST /api/agent/message` on 2026-08-24. An agent turn needs M365 Copilot as testuser1 (UI). Not substituted (POML). Owner item. |
| 7 | 166 (d) close-project | **PASS** | Secure PS `d533861c…` (provisioned, container `b!juvu…`) and non-secure PN `d933861c…` (BU "Spaarke", container `b!vzGD…`, 12 permissions incl. testuser1 writer). Contact grants `17598130…` / `20598130…`. On PS: admin JIT writer (`open-links`), testuser1 shared Collaborate + JIT writer. `close-project` PN → 200 `accessRecordsRevoked 1, speContainerMembersRemoved 0`; BU container **unchanged** (testuser1 still writer). `close-project` PS → 200 `revoked 1, removed 1`; PS container before `[admin writer, testuser1 writer]` → after **`[admin writer]`** (the grantee's email-keyed permission removed, the internal non-grantee kept); BU container unchanged. Both grants `statecode 1`. All rows deleted (404); file deleted. |
| 7 | 166 gate 23 | **PASS** | `Set-DocumentPointerFieldSecurity.ps1 -Verify` exit 0: p4a "none of the 5327 deployed JavaScript / HTML web resources writes a document pointer"; drive id, item-id copy and relocation columns secured; readers on 6 default teams; writers = the 2 BFF app users. |
| 7 | 166 gate 21 | **PASS (pointer goal) / drift filed** | `UniversalDatasetGrid` is **not** on dev (no `customcontrols` row). PCF versions vs master: see #1444 (9 controls carry #1309 source changes at the deployed version number; ScopeConfigEditor dev 1.2.7 < master 1.3.0; SemanticSearchControl dev 1.1.82 > master 1.1.81). |
| 8 | 169 RegardingResolver | **PASS — no deploy** | Dev `customcontrols` and `RegardingResolverSolution` = **1.6.1** (2026-10-06 05:37Z, after #1312 merged) = master. Not redeployed. Master's RegardingResolver source changed afterwards in `14a5e229c` (#1309) without a bump, so the next deploy must be 1.6.2 (#1444). |
| 9 | 147 G147-6 | **PASS** | Already applied once on 2026-10-06 (import `81eed1ac…`, unrecorded until now). Recorded run: dry run exit 0 ("All checks passed", 28 subgrids covered, 9 tables, no drift); export backup `SpaarkeSecureChildRibbons_backup_20261008.zip` (SHA256 `557C5418…E7C3`); `-Apply` exit 0, import job `895f99ec-86ca-48eb-b3a2-33af27500166` 15:15:45–15:16:24Z, published; `-Verify` exit 0 first time (no cache wait). `pac.cmd` used. The merge script's "added … to it" message on an existing rule was misleading → fixed in PR #1440. |
| 10 | 171 D3 (no new user) | **PASS (flag path)** | Standing writer Final Test `a151efdd…` (root BU; writer + `SprkStd…` marker on `b!vzGD…`; no record shares). `sprk_isexternal=true` → triggered run `df51ad4f…`: `standing.removed 1`, permission and marker gone (12→11). Restored to null → run `8c7cfc20…`: `standing.granted 1`, writer + marker back (12). Read-back null. Both runs show "Failed" only because of #1352 (`65a3fab2` ODataError). |
| 10 | 171 H (API) | **PASS** | testuser1, throwaway docs on secure PS `31e232ae…` (`30d1096d…`) and BU project `fb73b08c…` (`b6284531…`): Compose load 200; an out-of-band new version (Graph PUT) → `check-changes` **`changed: true`**, then `false`; `versions` lists 2.0 and 1.0 on both. Defect found: a second viewer never sees the change, and a just-created file reads as changed on the first poll → **#1443**. Docs and files deleted (404). |
| 10 | 171 B1 text | **FIXED** | `task-171-broker-only-document-bytes.md` lines 21 and 187 now say `ms-word:https://…` (`DesktopUrlBuilder.cs:38-61`). |
| 11 | 047 | **Spec PR #1440; evidence recorded** | (i) Graph "exists": gate 0 above — 20 provisioned containers read 200 `active` app-only (the first independent Graph read; B4-06:560/633). (ii) Registry under the BFF's own identity: B4-06:679 (`[SECURE-CONTAINER] Resolved sprk_todo … to the OWN container of the secure sprk_project`, a path through `RecordContainerResolver.IsSecurableAsync` → `SecurableEntityRegistry`), and today 5 more `[SECURE-CONTAINER] Resolved secure sprk_matter/sprk_project … to its OWN container` lines (15:19–15:23Z), with **0** `SecurableEntityRegistry` traces or exceptions in 7 days. (iii) Task 024: the permissions endpoint **pages** (`$top=1` → `nextLink` with `$skiptoken`; the plain GET of 12 has none), recorded in `task-024-spe-paging-parity.md` §1. |
| 12 | Unfiled defects | **Filed** | #1435 (F-G8-1), #1436 (F-G8-2), #1437 (O-G8-1), #1438 (G2 O-2), #1439 (G5 O-3); O-G6-6 is already covered by #1324. Harness scripts fixed here: `task-133-live-gate.ps1:103` (the PowerShell 7 header is a `string[]`), `task-132-live-gate.ps1:154,171` (an ISO time re-parsed as local). |

**047, still unproven:** FR-28's share as a human sees it (a shared user reading the secure project in BOTH the MDA and
the SPA) is an owner UI check; the registry on the inbound-communication archive path itself
(`IncomingCommunicationProcessor`) was not exercised (the resolver path it shares was); and the reworked e2e spec (PR
#1440) type-checks but has not run against dev (it needs a creator's delegated tokens). Document isolation itself is now
shown live: uploads land in the record's own container (163b, 166d, 171 A1), and other users get no bytes or chunks.

**Records left for the owner:** project `edb87d10-2ac3-f111-a05c-6045bdfe7b25` and its grant
`144b5110-2ac3-f111-a05c-0022482913fc` (105 SPA check; delete both afterwards); No Access entry
`33a4e845-2bc3-f111-a05c-3833c5ef3c0c` (154 (o); delete afterwards). Containers `b!R6Qrq…` (163b) and `b!juvu…` (166d,
still holding admin's JIT writer) are empty and were added to `Remove-TestContainers.ps1` (now 23 ids). Everything else
created was deleted and read back 404 (final sweep 15:40Z: no other "UAC gate" project, matter, organization, document
or entry since 15:00Z; no new ledger row).

**Defects filed this run:** #1443 (Compose check-changes shared delta), #1444 (dev PCF drift), #1445 (deleting a
provisioned record orphans its container and JIT roles), #1441 (invitation-onboarding e2e cannot pass; found by the
PR review). **Observation (by design):** close-project's email-keyed removal also drops the JIT role of the INTERNAL
user who shares the contact's email (testuser1); JIT is re-granted on the next `open-links` while they hold Write.

## Task 113: a refused /grant writes nothing (#1008) + owner round 80 (PR #1406, merged `d4bedd39c`), live gates 2026-10-08 17:33–17:34Z

Run against spaarke-bff-dev (healthz 200, `/ping` pong) and spaarkedev1 with the existing identities only:
- admin (`az` default, ralph.schroeder);
- testuser1 (`AZURE_CONFIG_DIR=C:/tmp/az-uac-child`), given a Read/Write/Append/AppendTo/Share share on the gate project, with no Delete, so a Collaborate ceiling.

Tokens: `api://1e40baad…/.default` for the BFF, and the admin `az` token for Dataverse set-up and read-back. No app-setting, Entra or Key Vault change. Script and raw log: session scratchpad `gate113/gate113.py`, `gate113.log`.

**How "lapsed" and "live" are made.** A grant is created through the route. Admin then PATCHes `sprk_expiresdate` (and the level, where noted) to yesterday (2026-10-07) or to today + 12 (2026-10-20). Today is the UTC date 2026-10-08, so today + 90 = **2027-01-06**.

**Throwaway records** (all created as admin):
- project P `06af8650…` "zz-113-gate project …" (non-secure, Standard);
- contact C `01131556…`;
- C's grant G `9eeddc52…`;
- the CIAM Test User's grant GI `96052360…`. That contact, `394fda9f…`, is bound on the External plane, so the invite is `AlreadyProvisioned`: no CIAM call, no email. Its `modifiedon` stays 2026-10-02.
- No Access entry E `90b10d5c…` (subject C, object P);
- testuser1's share on P.

Clean-up: E was deleted after G3 (204 → 404). The share was revoked (204). GI, G, C and P were deleted (204 each) and all read back **404**. A final sweep found 0 `zz-113` projects, contacts or entries, and 0 new grant rows for the CIAM contact. The Assigned-To ledger held 0 rows for P throughout.

| # | Gate | Result | Evidence |
|---|---|---|---|
| G1a | `/grant` re-add over a LAPSED grant, at a LOWER level (round 80) | **PASS** | Before: FullAccess, expired 2026-10-07. admin `/grant` ViewOnly, no date → 200 `grantedAccessLevel` 100000000, `narrowed` false, same `accessRecordId`. Row: ViewOnly, **2027-01-06**; 1 active row. |
| G1b | Same, at a HIGHER level | **PASS** | Before: ViewOnly, expired. admin `/grant` FullAccess → 200 `grantedAccessLevel` 100000002. Row: FullAccess, 2027-01-06. |
| G1c | Same, capped by the grantor ceiling | **PASS** | Before: ViewOnly, expired. testuser1 `/grant` FullAccess → 200 `grantedAccessLevel` 100000001, **`narrowed` true**. Row: **Collaborate**, 2027-01-06. |
| G2 | Never-lower still holds over a lapsed higher grant | **PASS** | Before: FullAccess, expired. testuser1 `/grant` FullAccess (narrowed to Collaborate) → **409 `sdap.access.grant.would_lower_existing`** "…Nothing was changed…". The row is identical before and after (level, date, `versionnumber` 27275248, `modifiedon`). |
| G3 | A refused `/grant` writes nothing (#1008) | **PASS** | Before: ViewOnly, expired, and C is on P's No Access list (E). admin `/grant` FullAccess → **422 `sdap.access.grant.grantee_denied`**. The row is identical (`versionnumber` 27275250). G2 shows the same for the 409. `expired_not_restored` is no longer reachable on `/grant` after round 80, so these two are the refusals left. |
| G4 | A re-add over a LIVE grant keeps its date | **PASS** | Before: ViewOnly, 2026-10-20. admin `/grant` Collaborate → 200 Collaborate. Row: Collaborate, **2026-10-20** (unchanged). |
| G5a | `/invite-and-grant` re-add over a LAPSED grant, LOWER, honest 200 | **PASS** | Seed invite FullAccess → 200 `AlreadyProvisioned`, GI. Lapsed (FullAccess, 2026-10-07). Invite ViewOnly → 200 `grantedAccessLevel` 100000000, same `accessRecordId`. Row: ViewOnly, 2027-01-06. |
| G5b | Same, HIGHER | **PASS** | Lapsed ViewOnly → invite FullAccess → 200 `grantedAccessLevel` 100000002. Row: FullAccess, 2027-01-06. |
| G5c | One row per key | **PASS** | 1 active row for C, and 1 for the CIAM contact, on P. |
| G6 | The Assigned-To rule does not use the restore | **Covered offline (not observable live)** | The rule calls the core without `reAddRestoresLapsed` (`AssignedAccessMaterializer.WriteGrantAsync`). Live, it never sends a dateless request over a lapsed key: with nothing conferring it sends today + 90 itself. So the difference shows only in the lapse race between its read and the core's, which can't be staged live. Pinned by `AGrantThatLapsesBetweenTheRulesReadAndTheCoresRead_…` and the Q3 perturbation (forcing the restore on for every caller fails 6 tests). |

**Defects found:** none. **Harness note:** the script's RetrievePrincipalAccess pre-check URL was malformed (404). G1c's `narrowed: true` restore at Collaborate shows the Collaborate ceiling directly.

**113 can be marked done.** G1–G5 pass live. G6 is covered offline by design. The POML's ISS-023 half was cut (round 59).

## #1410: the read-time veto honours a secure parent's list (PR #1419, merged `65177354f`), live gates 2026-10-08 21:50–21:56Z

Run against spaarke-bff-dev (healthz 200) and spaarkedev1 with the existing identities only:
- admin (`az` default);
- testuser1 (`AZURE_CONFIG_DIR=C:/tmp/az-uac-child`), whose linked contact is `ac6d7b68…`.

Tokens: `api://1e40baad…/.default` for the BFF, and the admin `az` token for Dataverse set-up and read-back. No app-setting, Entra or Key Vault access, and no prod Key Vault read. Script and raw log: session scratchpad `gate1410/gate1410.py`, `run1.out`.

**Why a fresh secure parent, not `31e232ae…`.** A wall naming testuser1 on `31e232ae…` would make the enforcer remove testuser1's share there (262167), and the sync in G3 triggers the enforcer. So a throwaway secure parent was provisioned instead. Two SPE containers were created as a result (below).

**How testuser1 reaches the records on Teams/SPA.** Through a direct ViewOnly contact grant to `ac6d7b68…`, which counts on a secure record. That is the systemuser plane's contact-grant term. testuser1's Dataverse share alone does not put a record in its Teams/SPA set (036 is not live).

**The read.** `GET /api/v1/external/workassignments/{id}/todos` as testuser1. It is gated on the caller's composed work-assignment set, the same set the veto trims. The SPA API has no work-assignment LIST route (`/external/projects` lists projects only), so "disappears from the list" is shown as the composed-scope read going 200 → 403 → 200.

**Timing.** Phase B ran between 21:55:25Z and 21:55:58Z, 25 s after a */5 boundary, so no secure-inheritance, No Access or assigned-access job ran inside it.

**Throwaway records** (all created as admin; names `zz-1410-gate*`):
- **M** `bb712d42…`: project, provisioned → secure, owner Secure Record Owners, container below.
- **W1** `05bbcc4e…`: work assignment filed under M (`sprk_regardingproject`), provisioned → secure, container below. Contact grant `5db7a350…`.
- **W2** `c4be49f6…`: work assignment filed under M, NOT provisioned (`sprk_issecure` false, never secured). Contact grant `c6be49f6…`.
- **W3** `16be04fb…`: work assignment filed under M, not provisioned, `sprk_assignedattorney1` = `ac6d7b68…`.
- **E** `5d73b7fb…`: No Access entry, subject systemuser testuser1, object project M (id in lower case).
- Ledger rows `79565efd…` (W3) and `6da7d2e7…`.

**Clean-up:** all deleted (204) and read back **404** at 21:56:00–07Z: E (deleted inside the gate, before the "lifted" reads), the 2 ledger rows, the 2 grants, W3, W2, W1 and M. A final sweep at 21:56:35Z found:
- 0 `zz-1410` projects or work assignments;
- the only No Access entry left is the owner's 154 (o) seed;
- 0 grants for `ac6d7b68…` and 0 ledger rows created since 21:49Z.

**Containers to add to `Remove-TestContainers.ps1`** (orphaned by deleting the provisioned records, #1445):
- M: `b!CtWYdqDTo0GSJGqApjHauRG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`
- W1: `b!HMt6g1HOGEe9Jp1UZUDQrxG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`

| # | Gate | Result | Evidence |
|---|---|---|---|
| G1 | A secure parent's wall reaches a secure child | **PASS** | testuser1 read of W1 (secure, its own list empty): before E **200**; with E on M **403** (21:55:40Z); E deleted (204 → 404) **200** again (21:55:54Z). |
| G2 | A NOT-yet-secure child (owner round 82) | **PASS** | W2 read back `sprk_issecure` **false**, owner unchanged, at 21:55:40Z, 5 s after E was created and minutes before any inheritance run. testuser1: before **200**, walled **403**, lifted **200**. |
| G3 | Assignee materializer (F2) | **PASS** | `POST /assigned-access/sync` on W3 (`sprk_issecure` false) with E in force → 200, entry `Skipped` / `no-access` for testuser1. The share read showed **no principal** on W3. The sync's own No Access pass: `coveredRecords 4`, `removed []`, complete. After E was lifted, the second sync → 200 `Shared` / `shared`, and W3 shows testuser1 **Read/Write/Append/AppendTo/Share**; ledger `79565efd…` is the same row, now Shared. |

**Defects found:** none.

**#1410 can be closed.** G1–G3 pass live on the merged build. The rule is pinned offline by `SecureParentReadVetoTests`, `SecureParentReadVetoInheritanceTests`, `NoAccessShareEnforcerTests` and `AssignedAccessParentWallTests`. What is left belongs to other issues: #1425 (contact plane) and #1426 (synchronizer, direct parents only).
