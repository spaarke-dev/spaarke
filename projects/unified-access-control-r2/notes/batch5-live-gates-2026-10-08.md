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

## Task 173: child records show their parent's Access Permission (PR #1458, merged `d7fdcafc3`), live gates 2026-10-08 22:57–23:12Z

**Deployed by the coordinator, in order:**
1. BFF (healthz 200).
2. `Set-DocumentAccessPermissionSchema.ps1 -Verify`: PASS.
3. Web resource `sprk_accesspermission_inherited`: created.
4. `Set-InheritedAccessPermissionFormLock.ps1 -Apply` then `-Verify`: PASS on the To Do, Event, Message and Document main forms. Snapshot `…\scratchpad\snapshots\accesspermission-lock-snapshot-20261008185630.json`.

The gates below were run by the task-173 agent through the Dataverse Web API and App Insights (`spe-insights-dev-67e2xz`), read-only except for the throwaway rows.

**Reconcile runs** (`secure-child-reconciliation`, `[CHILD-ACCESS-PERMISSION] run=` traces, mode `write` throughout; failure null, failed 0, deferred 0, writeCapReached false in every run):

| Run (UTC) | rowsChanged | examined | alreadyCorrect | changed | parentless | sweep |
|---|---|---|---|---|---|---|
| 22:57:22 | 0 | 1000 | 127 | **401** | 472 | 1000 rows, not complete |
| 22:58:43 | 401 | 420 | 404 | **10** | 6 | 18 rows, **complete** |
| 23:00:27 | 172 | 178 | 178 | 0 | 0 | — |
| 23:02:07 | 6 | 5 | 0 | 4 | 1 | — (G-2a) |
| 23:04:05 | 4 | 4 | 3 | 1 | 0 | — (G-2b) |
| 23:06:06 | 2 | 4 | 0 | 4 | 0 | — (G-2c) |
| 23:08:05 | 4 | 4 | 4 | 0 | 0 | — |
| 23:10:05 | 0 | 0 | 0 | 0 | 0 | — |

- The backfill converged in two runs: 411 rows set, under the 500 cap.
- 0 undetermined.
- 0 warning-level `CHILD-ACCESS-PERMISSION` traces.
- Sample sets: To Do `dc011e7f…` and `cd7c55a3…` went Standard → Restricted from PAT-176903 (`232852f8…`); To Do `9fb4ece2…` went Standard → Limited from matter `2444af6d…`.

| Gate | Result | Evidence |
|---|---|---|
| **G-2a** — matter Standard → Restricted | **PASS** | Throwaway non-secure matter `a64a4306…` with To Do `99dac406…`, Event `a3dac406…`, Communication `db6a0a0b…` and Document `e06a0a0b…` filed under it. Matter PATCHed to Restricted at 23:00:39Z; all four read **Restricted** at 23:02:26Z, **107 s** later, in the 23:02 run. |
| **G-2b** — hand edit reverted | **PASS** | To Do PATCHed to Standard through the Web API at 23:02:28Z; read **Restricted** again **102 s** later (23:04 run, changed 1). |
| **G-2c** — matter back to Standard | **PASS** | Matter PATCHed to Standard at 23:04:11Z; all four read **Standard** after **134 s** (23:06 run, changed 4). |
| **G-3** — parentless To Do keeps its own value | **PASS** | To Do `c4dac406…`, created with no parent and set to Limited at 23:00Z; still **Limited** at 23:11:28Z, after 5 runs. Never written. |
| **UAT spot check** (the owner's case, through the API) | **PASS** | PAT-176903 (`232852f8…`) is Restricted and not secure. All 4 of its documents read Restricted. The To Do filed on its document `2bcfc5d2…` ("Word To Do Item" `4bebbbc9…`, created from the Word add-in) reads **Restricted**, as do all 4 To Dos carrying its matter stamp. A NEW add-in create was not made: that needs the add-in (G-1). |
| **Cleanup** | done | All 6 throwaway rows deleted, each read back **404**: the 4 children, the G-3 To Do, then the matter. |

**Defects found:** none in the product.
- The first G-2 attempt sent no auth header and got a 401: in the agent's driver script, a function named `H` is shadowed by PowerShell's `h` alias (`Get-History`). It created nothing; it was fixed and re-run.

**Owner gates still open:**
- **G-1:** a NEW To Do created in the Word add-in on a document under PAT-176903 shows Restricted, locked, with "Access permission is inherited from …".
- **G-4:** in the forms UI (To Do, Event, Message and Document main forms):
  - the field is locked with the notification on a filed record and editable on an unfiled one;
  - the TrackingFieldTrio pill change is put back while locked;
  - no console errors;
  - dark mode is readable.

## 174 live gate (2026-10-09)

Task 174: enforcement uses the parent-derived access. PR #1481 merged as `e78c47149`; TrackingFieldTrio 1.0.43 imported. The gate ran 05:53:41–05:55:50Z.

**Environment and identities**
- Targets: spaarke-bff-dev (healthz 200) and spaarkedev1.
- admin (`az` default) did the set-up and the read-back.
- testuser1 (`AZURE_CONFIG_DIR=C:/tmp/az-uac-child`) did the reads.
- Tokens: `api://1e40baad…/.default` for the BFF; the admin `az` token for Dataverse.
- No app-setting, Entra or Key Vault access, and no `sprk-prod-kv` read.
- Script and raw log: session scratchpad `gate174/gate174.py` and `run1.out`.

**The contact.** testuser1's linked contact `ac6d7b68…` stands in for a CIAM contact, because the agent cannot obtain a CIAM token. The workforce token is admitted on the external plane (B4-06), and the read is composed from that contact's grants. The cancellation is the same direct-only predicate over the effective flags on both planes.

**The read.** `GET /api/v1/external/workassignments/{id}/todos` as testuser1. It is gated on the caller's composed work-assignment set, the same read #1410's gate used. The form banner was not used (#1488).

**The secure parent.** There is no secure matter in dev, so a throwaway matter M was made secure by setting `sprk_issecure` directly. There was no provisioning, so no SPE container was created. M was secure for 7 s (05:55:26–05:55:33Z), inside one */5 window (boundary + 25 s), and then restored, so no inheritance job saw it.

**Throwaway records** (all created as admin; names `zz-174-gate*`):
- **O** `11f372c6…`: an organization.
- **J** `18f372c6…`: membership of `ac6d7b68…` in O (active, no dates).
- **M** `d1767dc7…`: a matter, not secure, Standard.
- **P** `26548bc7…`: a project filed under M by the pair, not secure.
- **W1** `1cf372c6…`: a work assignment filed under M (`sprk_regardingmatter`). Own flags: `sprk_issecure` false, Access Permission Standard (100000000).
- **W2** `28548bc7…`: a work assignment filed under P.
- **G** `64c0b0cc…`: an org-wide Collaborate grant on W1 for O, written through `POST /api/v1/external-access/grant`. The answer was 200, `grantedAccessLevel` 100000001, not narrowed.

**Clean-up.**
- All seven rows were deleted (204), in the order G, W2, W1, P, M, J, O, and each read back **404** (05:55:41–50Z).
- M's flag had already been restored before it was deleted.
- A final sweep found 0 `zz-174` matters, projects, work assignments or organizations; 0 memberships for `ac6d7b68…`; its active grants back at 2 (as before); and 0 secure matters.

| # | Step | Result | Evidence |
|---|---|---|---|
| 1 | Set-up: a NOT-flagged, Standard work assignment under a matter; an org-wide Collaborate grant for an organization the contact belongs to | **PASS** | W1 own flags `sprk_issecure` false, `sprk_accesspermission` 100000000. Grant `64c0b0cc…` → 200. |
| 2 | Read path, before / after the parent is secure | **PASS** | M not secure: **200** `{"value": []}` (05:53:58Z).<br>M secure: **403** `{"title":"Forbidden","status":403,"detail":"You do not have access to this work assignment"}` (05:55:33Z). W1's own `sprk_issecure` read **false** at that moment (3 s after M was set). The org-inherited grant is cancelled by the parent's Secure (direct-only).<br>M restored: **200** again (05:55:36Z). |
| 3 | `GET /api/v1/records/sprk_workassignment/{id}/no-access` (064, the banner's source) | **PASS** | Before: W1 → 200 `secure:"doesNotApply"`, `accessPermission:"standard"`, `inheritedFrom:null`.<br>After: W1 → 200 `secure:"applies"`, `accessPermission:"standard"`, `inheritedFrom:{recordType:"sprk_matter", recordId:"d1767dc7…", name:"zz-174-gate M …"}` (the direct parent).<br>After: W2 (W2 → P → M) → 200 `secure:"applies"`, `inheritedFrom:{recordType:"sprk_project", recordId:"26548bc7…", name:"zz-174-gate P middle …"}`. This is the DIRECT parent; M's id appears nowhere in the body (F1-d). |
| 4 | Clean-up | **PASS** | 7 × 204 → 404; M's flag restored (204) before deletion; the sweep is clean (above). |

**Defects found:** none.

**Not covered live:** a real CIAM sign-in, because no CIAM token is available to the agent. The CIAM half is the same composition and veto code (`ComposeContactPlaneAsync`). Offline it is pinned by `EffectiveAccessFollowsParentTests` (CIAM contact plane) and by the seeding proofs in `task-174-effective-access-follows-parent.md`.

**174 can be marked done** once the owner accepts the testuser1 stand-in for the CIAM contact.

## Task 175 live gate (PR #1503, merged `61b0aa77a`), 2026-10-09 22:12:51–22:21:38Z: PASS

Run as the admin through the deployed BFF (master `a27d46e47`), with testuser1 for the field-security refusal. Script, log and results: session scratchpad `gate175/` (`RESULT.md`, `gate175.py`, `run1.out`, `results.json`).

| # | Step | Result | Evidence |
|---|---|---|---|
| 1 | Secure the matter; the work assignment follows | PASS | 22:12:58Z: Make Secure on M1 → 200, W1 `secured`. W1 is then secure, owned by Secure Record Owners (`6eabc7f9…`), with its own container. The marker shows it as inherited (`floorSecure` true, `ownSecure` false). `can-manage-access` reports `floorAccessPermission` "limited". |
| 2a | Unsecure the work assignment under a secure matter | PASS | 22:13:32Z → 409 `sdap.access.access_follows_parent`, naming M1. |
| 2b | Access Permission below the matter's | PASS | Set to Standard directly in Dataverse (no BFF route writes it). The job restored Limited 101 s later. |
| 2c | Caller-supplied `sprk_issecure` / `sprk_accessinheritance` | PASS for the Dataverse half | testuser1 is refused with 403 `0x8004f507` (field-secured) on both columns. The BFF filing-gate codes are reachable only through the AI tools, field-mapping push and Office create, and are covered by unit tests. |
| 3 | Unsecure the matter; the work assignment follows back | PASS | 22:15:15Z → 200, W1 `unsecured`. Flag false, owner the business unit's default team, marker cleared. The container stays recorded, as designed. |
| 4 | Owner's example: the child's own Secure survives the parent's secure and unsecure | PASS | Make Secure on W2 → 200 (`ownSecure` true). Securing then unsecuring M2 → 200. W2 is still secure at 22:21:25Z, after a job run. |
| 5 | Ribbon and form hiding | Owner item | Checklist §9. |
| 6 | Clean-up | PASS | All 4 records deleted and read back 404. testuser1's share revoked. 4 empty containers added to `Remove-TestContainers.ps1`. |

No defects found.

## Task 176 live smoke (PR #1520, merged `a27d46e47`), 2026-10-10 ~00:45Z: PASS

Run against the deployed BFF (master `ebacd2e15`) as the admin and testuser1, GETs and searches only. Files: session scratchpad `smoke176/` (`RESULT.md`, `smoke176.log`, `results.json`).

**The F1 case, from existing data:** document `766ad390…`, 6 chunks in `spaarke-files-index`, under matter `ec408889…`. testuser1 gets 403 on the document and 200 on the matter, because the document is owned in another business unit. On dev, 91 of the 163 re-indexed documents have this shape for testuser1.

| Step | Result | Evidence |
|---|---|---|
| Admin, `/api/ai/search` (`scope=all`, `scope=entity`, filename, `documentIds`) | PASS | The document is present in every call. |
| testuser1, the same calls | PASS | The document and 4 other unreadable siblings are absent. `totalResults` / `returnedResults` equal testuser1's own rows. |
| `/api/ai/rag/search`, matter-scoped and tenant-wide | PASS | Admin gets 17 / 20 rows with the document. testuser1 gets 7 / 1 rows without it. |
| Control and sweep | PASS | The 7 readable siblings are returned to testuser1. All 25 distinct documents returned to testuser1 in 14 calls are readable by it in Dataverse. |
| `/api/ai/search/count` | Known limit #1533 | Both callers see 17. |

**Observed, by design (task 176 note §6, §8.4):** the trim runs after ranking, so a restricted caller gets shorter pages. This costs recall, not access.

## 2026-10-10 live gates 179 + 181

**Environment.** spaarke-bff-dev, master `74dc51c60` (healthz 200), and spaarkedev1. Run 19:41–20:15Z.
- Identities: admin (`az` default, ralph.schroeder `1d02f31c…`); testuser1 (`AZURE_CONFIG_DIR=C:/tmp/az-uac-child`, `8d7bad7a…`, BU1, linked contact `ac6d7b68…`); uac.child (`d6f8f439…`) as a share target only.
- Tokens: `api://1e40baad…/.default` for the BFF; `az` tokens for Dataverse.
- No role, Entra, Key Vault or app-setting write, and no `sprk-prod-kv` read.
- Scripts and raw logs: session scratchpad `gate179_181/`: `gate.py` + `run1.out` (main run), `gate179v.py` + `gate179v.out` (share-based 179 variant), `retry181.out`, `shape181.out`, `repro_unshare.out`, `results*.json`.

**testuser1's roles changed during the run (not by the agent).**
- 19:32Z: direct roles Basic User + Office Add In User. RetrieveUserPrivileges: `prvWritesprk_Matter` Deep, no Share on matter, project or work assignment (the owner's Office Add In User edit, role `modifiedon` 19:23:37Z).
- 19:41:41Z onward: **Spaarke Core User** also direct (Share Deep on all three). The audit has no association record. The coordinator confirms no agent assigned it and is asking the owner to remove it. Intended state: Basic + Office Add In User only.
- So the first run's 179.1 on a record owned by testuser1's BU team (M1 `c53fca96…`) had an invalid premise. testuser1 held Share Deep, RPA answered all eight rights, and can-manage-access, `/share-user` and `/grant` all answered 200. That is correct behaviour for a Share holder, and it is superseded by the share-based variant below.
- The variant reaches Write only through a record share, on a root-BU matter outside testuser1's Deep reach. That makes "Write but no Share" real whatever roles testuser1 holds.

### Gate 179

| # | Step | Result | Evidence |
|---|---|---|---|
| 179.0 | No share, root-BU matter M5 `79e3618f…` (owner admin) | **PASS** (control) | RPA as testuser1 → 403 `0x80048306` ×2. `GET /can-manage-access?recordType=matter&recordId=M5` → **403 `sdap.access.deny.delegation_write_required`**. |
| 179.1 | Write without Share (GrantAccess `ReadAccess, WriteAccess` to testuser1) | **PASS** | RPA ×3 over 12 s: `ReadAccess, WriteAccess`. can-manage-access → **403 `sdap.access.deny.delegation_share_required`**, detail "To change who else can access this record you need Write access to it and the Share privilege on its table (set in your security role)." So canManageAccess is false: the client reads non-200 as no. `POST /share-user` (testuser1 → uac.child, ViewOnly) → **403 `delegation_share_required`**. Nothing written: the shares are testuser1's only. |
| 179.2 | Share from a record share (ModifyAccess `ReadAccess, WriteAccess, ShareAccess`) | **PASS** | RPA ×3: **`ReadAccess, WriteAccess, ShareAccess`**. can-manage-access → **200 `canManageAccess: true`**. `/share-user` → 200 `created`, mask 1. Then unshared (200). |
| 179.2-Q | Does RPA report a SHARED Share? | **Yes, for a caller whose roles hold prvShare** | Shown above: testuser1 holds `prvSharesprk_Matter` Deep through Core User, but the record is outside that depth, so the Share right came from the share alone. **Still open:** the same question for a caller with NO Share privilege in any role (the Microsoft Learn rule "a share cannot give rights the role does not allow"). Re-run 179.1/179.2 with `gate179v.py` after the owner removes Core User from testuser1, re-probing at least 3 times for the privilege cache. If step 2 then answers 200, RPA ignores the privilege for shared Share, and the POML's escalation applies (the table privilege needs a second call). |
| 179.3a | Round 91 child mirror carries Share (request path) | **PASS** | Matter M3 `33aacebc…` with event E3 `35aacebc…` filed under it (`sprk_RegardingMatter`). `POST /provision-project` make-secure → 200, owner Secure Record Owners, `children.reowned 1` (sprk_event), container below. `/share-user` admin → testuser1 Collaborate on M3 → 200, M3 mask `Read, Write, Append, AppendTo, Share`. E3's mirrored share for testuser1 at once: **`ReadAccess, WriteAccess, AppendAccess, AppendToAccess, ShareAccess`**, with no Assign or Delete. The admin's creator share on E3 also carries Share. |
| 179.3b | The scheduled `secure-child-share-reconciliation` (`*/2`) raises an existing child share | **PASS** | 19:43:05Z: ModifyAccess put E3/testuser1 back to a pre-round-91 mask, `Read, Write, Append, AppendTo` (read back). 19:44:06Z, **61 s** later, the next scheduled run had raised it to **`… , ShareAccess`**. testuser1's RPA on E3 then reads `Read, Write, Append, AppendTo, Share`. |

### Gate 181

| # | Step | Result | Evidence |
|---|---|---|---|
| 181.1 | `/share-user` admin → testuser1 Collaborate on M2 `48c0d2ac…` notifies testuser1 | **FAIL: environment (role gap); the BFF behaved correctly** | 200 `outcome created`, mask 262167, **`notificationFailed: true`**. No appnotification row. App Insights 19:42:12Z: `Read Privilege Check For Owner failed … Principal user (Id=8d7bad7a…) is missing prvReadappnotification privilege (Id=24ba2370-9cdc-4d1a-a7f7-c9956548777b) on OTC=10294 … context.Caller=8793f4b0…` (the BFF app user), then `Failed to create appnotification record` (Error) and `Failed to create notification for user …` (Warning, the `[GRANT-NOTIFY] Could not tell user … The grant stands.` line). The share stands. |
| 181.2 | Same-level re-share sends nothing; the granter is not told | **PASS** | Re-share → 200 `outcome unchanged`, `notificationFailed: false`, no attempt logged. No row for the admin (granter). In the first run, testuser1's `/grant` to its own linked contact on M1 → 200, `notificationFailed: false`, no row: the granter is excluded. |
| 181.3 | `/grant` admin → testuser1's linked contact `ac6d7b68…` ViewOnly on M4 `ce0cc2b3…` | **FAIL: same role gap** | 200 `accessRecordId d40cc2b3…`, `grantedAccessLevel 100000000`, **`notificationFailed: true`**. The same `prvReadappnotification` refusal at 19:42:28Z. The notifier did resolve the contact to testuser1 (the attempt was made for `8d7bad7a…`). |
| 181.4 | Row shape, with a recipient who holds the privilege | **PASS** | testuser1 (Read/Write/Share via a share) `/share-user` → admin Collaborate on M6 `5734e2ec…` → 200, narrowed (mask 262147), `notificationFailed: false`. Row `b96045ef…`: title **`You were given access to the matter "# gate181 M6 shape …"`**, body **`Test User 1 gave you access.`** (no level word, because the narrowed mask matches no level), icontype 100000000, priority 200000000, ttl 604800. `data`: `actionUrl` `/main.aspx?etn=sprk_matter&id=5734e2ec…&pagetype=entityrecord`, **`actions[0]` = `{title: "Open matter", data: {url: <same>, navigationTarget: "inline"}}`**, `regardingId`, `category: "access"`. In the first run uac.child also received one (19:41:46Z, `75da9b9c…`). |
| 181.5 | In-app notifications enabled for the model-driven app | **Matter Management: ON** | `appsettings`: `AllowNotificationsEarlyAccess = true` for **Matter Management** (`sprk_MatterManagement`, `729afe6d…`, set 2026-04-06), and for the Dataverse Accelerator App. No override on Spaarke Administration, Document Management, Spaarke Platform or AI Setup, so they use the default `false` and show no bell. |

**Root cause of 181.1 / 181.3 (read-only).**
- Dataverse creates an `appnotification` only if its owner (the recipient) holds `prvReadappnotification` ("Read Privilege Check For Owner").
- **No Spaarke role holds any appnotification privilege**: Basic User, Core User, Office Add In User, AI Analysis User and Reporting Access Viewer, direct and through the BU1 team (`RetrieveRolePrivilegesRole` per role; `RetrieveUserPrivileges` for testuser1 and uac.child). The exported `src/dataverse/solutions/SpaarkeMaster/Roles/*.xml` have none either.
- In the root BU only System Administrator, System Customizer, Service Reader/Writer (Global) and Support User (Basic) hold it. Impersonated `GET appnotifications` as testuser1 and as uac.child → 403 `0x80040220`.
- No non-admin person owns any appnotification on dev (only the admin and the BFF's app user). The bell has never reached a Spaarke-role user, and task 100's reminders are affected the same way.
- uac.child's row at 19:41:46Z was created, but the same share-user to uac.child at 20:01Z failed with the same refusal. Most likely a stale privilege cache after the 19:23Z role edit. It cannot be shown read-only (no role audit).
- Adding Core User did not help: testuser1 failed at 19:42:12, 19:42:58 and 20:01Z, all with Core User held.
- **Minimal fix (owner, role write):** add **`prvReadappnotification` at Basic (User) depth** to **Spaarke Basic User**, the base role every internal user holds (direct or through the BU teams). Read is all Dataverse checks. The BFF creates the row as its own app user, which already holds Create. Then re-run 181.1/181.3 (`gate.py`, or `retry181.py` for one share), re-probing for the privilege cache.

**The client message.** The BFF reports the failure honestly: `notificationFailed: true` on the 200, the grant stands, and the logs name the user and the cause. The modal turns it into the warning "Some people could not be notified; share the record link with them." (`NOTIFICATION_FAILED_SENTENCE`, `AccessGrantModal.tsx:854`, read at :1453/:1549/:1583/:1619). That was not shown in the UI here. Nothing misleading was found. One nit: NotificationService logs this expected configuration gap at Error level.

**Defect found (pre-existing, filed #1620).** `/unshare-user` cannot remove a share held by the record's current owning user. `InternalShareEndpoints.cs:758` calls the app-only `RevokeAccessAsync`, so Dataverse refuses with 400 (0x80040223), and the route answers 500 `sdap.access.user_share.write_not_confirmed` with the share kept. The owner-aware overload (`IDataverseRecordShareService.cs:81`, used by unsecure and provision) is not used. Reproduced twice (M6 `5734e2ec…`, M7 `91cc9122…`; both deleted, so the stranded share went with the record).

**Throwaway records** (all names `# gate179 …` / `# gate181 …`; all deleted and read back **404**):
- matters M1 `c53fca96…`, M2 `48c0d2ac…`, M3 `33aacebc…` (unsecured through the BFF first: 200, `sharesRevoked 2`), M4 `ce0cc2b3…`, M5 `79e3618f…`, M6 `5734e2ec…`, M7 `91cc9122…`, a retry matter `df755b6c…`;
- event E3 `35aacebc…`;
- grants `d40cc2b3…` (revoked through `/revoke` 200, then deleted) and `79da9b9c…` (first-run 179.1, deleted);
- every share was removed through `/unshare-user` (200 `removed: true`), except the owner share above, which went with its record;
- the gate's appnotification rows (uac.child `75da9b9c…`, admin `b96045ef…` and one on M7) were deleted after they were recorded.

Final sweep 20:15Z: 0 `# gate17*` / `# gate18*` matters or events, 0 grant rows created since 19:30Z, 0 appnotifications since 19:30Z for testuser1, uac.child or the admin.

**SPE container created:** M3 `b!KDQEkLSGukOQnI4nJV1qUtNtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y` (empty; add it to `Remove-TestContainers.ps1`, #1445).

**Verdict.**
- **179:** steps 1, 2 and 3 PASS. Still open: step 2's platform question for a caller with no Share privilege, re-run after Core User is removed from testuser1.
- **181:** the server logic PASSES (recipient resolution, gain check, granter exclusion, best-effort failure report, row shape and link). Delivery to internal users FAILS on dev until a Spaarke base role holds `prvReadappnotification`. Matter Management has the bell enabled.


## 2026-10-11 live gate 181 (after the appnotification role fix)

**Environment.** spaarke-bff-dev at master `01ccf46f0`, which includes 181's GrantAccessNotifier (healthz 200), and spaarkedev1. Run 15:56:28–15:57:34Z.
- Identities: admin (`az` default, `1d02f31c…`, the granter); testuser1 (`8d7bad7a…`, linked contact `ac6d7b68…`, BFF and Dataverse tokens via `AZURE_CONFIG_DIR=C:/tmp/az-uac-child`); uac.child (`d6f8f439…`, linked contact `51a5ddb0…`), used as a recipient and read through impersonation only.
- No role, Entra, Key Vault or app-setting write, and no `sprk-prod-kv` read.
- Script and log: session scratchpad `gate179_181/gate181b.py`, `gate181b.out`, `results181b.json`.

**Pre-checks (read-only).**
- `RetrieveUserPrivileges` shows `prvReadappnotification` (`24ba2370…`) at **Basic** only, for both testuser1 and uac.child.
- Impersonated `GET appnotifications` (MSCRMCallerID) answers **200** for both users. On 2026-10-10 it answered 403 `0x80040220`.
- The probe found no stale privilege cache, and no retry was needed.

| # | Step | Result | Evidence |
|---|---|---|---|
| 181b.1a | `/share-user` admin → testuser1, Collaborate, on MA `1f501b52…` ("# gate181b A share-user …") | **PASS** | 200: `outcome created`, mask 262167, **`notificationFailed: false`**. One row, `78501b52…`, owned by testuser1 and created 15:56:39Z. Title `You were given access to the matter "# gate181b A share-user 20261011-1556Z"`. Body `Ralph Schroeder gave you Collaborate access.` icontype 100000000, priority 200000000, ttl 604800. `data.actionUrl` = `actions[0].data.url` = `/main.aspx?etn=sprk_matter&id=1f501b52…&pagetype=entityrecord`. `actions[0].title` **"Open matter"**, navigationTarget `inline`, `category: "access"`. |
| 181b.1b | `/grant` admin → testuser1's linked contact, ViewOnly, on MB `ce71005d…` ("# gate181b B contact-grant …") | **PASS** | 200: `accessRecordId d271005d…`, `grantedAccessLevel 100000000`, **`notificationFailed: false`**. One row, `578d0c5e…`, owned by testuser1 and created 15:56:52Z. Body `Ralph Schroeder gave you View Only access.` Same shape; the link and "Open matter" action point to MB. |
| 181b.2a | `/share-user` admin → uac.child, Collaborate, on MA | **PASS** | 200: `created`, `notificationFailed: false`. Row `6a151458…`, owned by uac.child and created 15:56:41Z. Same title, body, link and action as 181b.1a. |
| 181b.2b | `/grant` admin → uac.child's linked contact, ViewOnly, on MB | **PASS** | 200: `accessRecordId d471005d…`, `notificationFailed: false`. Row `738d0c5e…`, owned by uac.child and created 15:56:54Z. Same shape, linked to MB. |
| 181b.3 | testuser1 reads its own rows as itself, and not uac.child's (User depth) | **PASS** | testuser1's own Dataverse token. List filtered on its own owner id → 200 with exactly its 2 rows (`78501b52…`, `578d0c5e…`). Each row by id → 200. List filtered on uac.child's owner id → 200 with **0 rows**. Each uac.child row by id (`6a151458…`, `738d0c5e…`) → **404 `0x80048d02`**. An unfiltered list (`$top=500`) returns rows of one owner only: testuser1. |
| 181b.4 | Same-level re-share sends no new row | **PASS** | Re-share admin → testuser1, Collaborate, on MA → 200, **`outcome unchanged`**, `notificationFailed: false`. 0 new testuser1 rows since the re-share; testuser1 still holds 2 gate rows. The admin (granter) received 0 rows for the gate matters. |

**Clean-up (PASS).**
- Grants `d271005d…` and `d471005d…`: `/revoke` 200 (`deactivatedCount 1`, `speContainerOutcome NotAttempted`), then deleted (204) and read back 404.
- `/unshare-user` on MA for testuser1 and uac.child: both 200, `removed: true`.
- The 4 gate appnotifications (`78501b52…`, `578d0c5e…`, `6a151458…`, `738d0c5e…`) were recorded above, then deleted (204 each).
- Matters MA and MB were deleted (204) and read back 404.
- Sweep: 0 `# gate18*` matters; 0 gate-matter appnotifications for testuser1, uac.child or the admin; 0 grant rows on the gate matters.

**SPE container created: none.** Neither matter was made secure, and no provision call was made. `/grant` reported `speContainerMembershipGranted: false`.

**Verdict.** **181 PASSES on dev**, delivery included. With `prvReadappnotification` at Basic on Spaarke Basic User / Core User, both internal routes (`/share-user`, and `/grant` to a contact linked to a user) create one correctly shaped row per recipient. Basic depth keeps each user's bell private. A same-level re-share sends nothing. This closes the 2026-10-10 181.1 / 181.3 environment failures.

## 2026-10-11 #1630 rollout (PR #1643, master 848f63913)

- **Reviews.** Correctness verifier PASS: full BFF unit suite 19,427 passed, 0 failed. Security verifier PASS. The known limits found are recorded on #1641.
- **CI.** Router green. "Server tests (office scope)" was cancelled at its 20-minute job timeout with no test failure; it is advisory and does not block the merge.
- **Deploy.** BFF deployed to dev from `C:\wtdep` (detached at 848f63913). The 4 critical-file SHA-256 hashes match and `healthz` returns 200.
- **Repair.** `scripts/Repair-IntermediateFiledWorkAssignments.ps1` dry run found 1 candidate, ORDINARY `b10b7dab…`, and nothing to secure. `-Verify` PASS (exit 0). No `-Apply` was needed.

## 2026-10-11 live gate 165 (b)(c)(g) as uac.child.user

**Environment.** spaarke-bff-dev at master `848f6391` (healthz 200) and spaarkedev1. Run 19:26:39–19:29:54Z.
- **Identities.**
  - Leaf admin: uac.child.user (oid `94047962…`, systemuser `d6f8f439…`). Its business unit is `cb15f587…`, named "Spaarke Business Unit 1" in Dataverse: a direct child of the root "Spaarke" (`06fbf21c…`), with no children. BFF token roles `Admin`, from the separate `AZURE_CONFIG_DIR`.
  - Root admin: ralph (oid `c74ac1af…`, the default `az` profile). Used for setup, the positive controls and clean-up.
- **Sources.**
  - POML criterion (b)/(c).
  - Note §9: (b), (c) and (g). The (g) letter is defined only in §9 and is the environments rule (round 16 item 4). The POML has no (g).
  - §14.9 (c), the newest (c): the leaf half of the "probes after deploy".
- **No writes outside the approval.** No Entra, Key Vault, app-setting or role write, and no `sprk-prod-kv` read. Write probes targeted only throwaway records created for this gate.
- **Files.** Scripts and log are in the session scratchpad `gate165/` (`p1-setup.ps1`, `p2-probes.ps1`, `p3-cleanup.ps1`, `gate165.log`, `p2.out`, `p3.out`). Tokens were held in memory only.

**Setup (root admin).** Every id below was recorded in `state.json` before any probe.
- **Throwaway config X `ed9a81a9…`.** Created with `POST /api/spe/configs` → 201, in the ROOT unit, so it is outside the leaf's unit. Its identity values are fresh random GUIDs that no other config uses: containerTypeId `55dbe16d…`, owningAppId `bacfafaa…`, consumingAppId `8f05d27f…`. No secret name and no environment.
- **Two throwaway containers.** Created with `POST /api/spe/containers?configId=c3a25b9a…` → 201 each, stamped with the root unit: `b!bgjWxy…` and `b!axaKTt…`.
- **Throwaway environment E `404e83b5…`.** Created with `POST /api/spe/environments` → 201, `isDefault: false`.

"Identical" below means equal after replacing `traceId` and the requested id. The `detail` echoes the caller's own id, e.g. "Container type config '<id>' was not found.". Content type and status were equal in every pair.

| # | Step (leaf admin unless stated) | Result | Evidence |
|---|---|---|---|
| b1/b2 | `GET /configs/X` vs `GET /configs/<random>` | **PASS** | 404 / 404 `spe.admin.deny.config_out_of_scope`, identical. |
| b3 | `GET /configs/68f9a952…` (real root config; §9's original target; read-only) | **PASS** | 404, identical to b2. |
| b4 | `GET /configs` | PASS | 200 `[]` (no config in the leaf's unit). |
| b5 | Root `GET /configs/X` (positive control) | **PASS** | 200, full detail of X. |
| c1 | `PUT /configs/X` `{name:"gate165 PWNED"}` vs PUT on a random id | **PASS** | 404 / 404, identical. |
| c2 | `DELETE /configs/X` vs random | **PASS** | 404 / 404, identical. |
| c3 | `POST /bulk/delete {containerIds:[both throwaway], configId:X}` vs random configId | **PASS** | 404 / 404, identical. No `operationId`. |
| c4 | `POST /bulk/permissions {…, configId:X, userId:<leaf oid>, role:owner}` vs random | **PASS** | 404 / 404, identical. No `operationId`. |
| c1–c4 | The four X answers compared with each other | PASS | All identical: the same 404 as b1. |
| c5a | `POST /configs` in the leaf's own unit naming X's **containerTypeId** (new owning app) | **201 — by design; the POML's "→ 403" is superseded** | Owner round 20 item 3 (§11.4; code `DecideConfigWriteAsync`) makes containerTypeId, owningAppId and its secret SHARED identity, so any config may name them. The config it created, `da086bd8…` (unit `cb15f587…`), was deleted in clean-up. |
| c5b | `POST /configs` in its own unit naming X's **consumingAppId** (the per-customer identity under the current rule) | **PASS** | 403 `spe.admin.deny.config_identity_out_of_scope`. |
| c5c | The same, naming X's consumingAppId as the OWNING app (cross-column kind) | **PASS** | 403 `config_identity_out_of_scope`. |
| c5d | `POST /configs` with `businessUnitId` = root | PASS | 403 `spe.admin.deny.business_unit_out_of_scope`. |
| cs1–cs3 | §14.9 (c) leaf half: `GET /security/alerts`, `GET /security/score` (configId `c3a25b9a…`), `GET /containertypes/8a6ce34c…/permissions?configId=c3a25b9a…` | **PASS** | 403 `spe.admin.deny.platform_operator_required` on each. The container-type route now answers the round-49 operator 403 first, which supersedes §12.9's `container_type_shared`. |
| cs4 | `GET /businessunits` | **PASS** | 200: only `cb15f587…` (`isRootUnit:false`). |
| cs5/cs6 | `GET /containers?configId=c3a25b9a…`; `GET /containers/b!bgjWxy…?configId=c3a25b9a…` | PASS | 404 `config_out_of_scope`: the config rule refuses before any container is read. |
| cs7 | `GET /dashboard/metrics` | PASS | 200 with every count 0 and empty per-config maps (the root aggregate is not leaked). |
| g1 | `GET /environments` | **PASS** | 200 `[]`. |
| g2/g3 | `GET /environments/df502cb9…` vs a random id | **PASS** | 404 / 404 `spe.admin.deny.environment_out_of_scope`, identical. The same for throwaway E. |
| g4/g5 | Root: `GET /environments`; `GET /environments/df502cb9…` | **PASS** | 200: the list holds "Spaarke Dev" (and E at the time); GET 200. |
| g6 | `POST /environments {"name":"probe","rootSiteUrl":"https://example.sharepoint.com"}` | **PASS** | 403 `spe.admin.deny.environment_write_requires_platform_operator`. |
| g7/g8/g9 | `PUT` and `DELETE` on throwaway E; `PUT` on a random id | **PASS** | 403, the same code. g6–g9 are identical: decided before any environment is read. The probes did not target the real `df502cb9…`. |

**After the probes (root admin).**
- X: byte-identical to its pre-probe GET (name and `modifiedOn` unchanged).
- E: unchanged. `df502cb9…`: unchanged (`modifiedOn` 2026-03-16).
- Both throwaway containers: still active, with 0 permissions (no leaf owner grant).
- App Insights (`spe-insights-dev-67e2xz`, 19:26–19:29Z):
  - A "tenant scope DENIED" / "environment scope DENIED" / "platform-operator route DENIED" trace for every refused call.
  - **No** `BulkDelete: enqueued`, `BulkPermissions: enqueued`, `UpdateSpeConfig` or `DeleteSpeConfig` trace from the leaf.
  - The only leaf write that ran was c5a's `CreateSpeConfig`.

**Clean-up (PASS).**
- Configs `ed9a81a9…` (X) and `da086bd8…` (c5a): root `DELETE` → 204 each. Dataverse read-back → 404.
- Containers `b!bgjWxy…` and `b!axaKTt…`: root `POST /bulk/delete` (configId `c3a25b9a…`) → 202. Operation `9e4481f6…` finished 2/2 with 0 failed. Active GET → 404 for both, so they are in the SPE recycle bin. **Permanent removal is the owner's** `notes/Remove-TestContainers.ps1`; the full ids are in the agent report.
- Environment E `404e83b5…`: root `DELETE` → 204. Dataverse read-back → 404.
- Sweep: `sprk_specontainertypeconfig` holds only `68f9a952…` and `c3a25b9a…`. `sprk_speenvironment` holds only `df502cb9…` (default, unchanged). No "probe" environment exists.

**Finding F-165-RB (class (a)/(d), fails closed; not a gate of this run; for the main session to file).**
- **What happened.** Root admin `GET /api/spe/recyclebin?configId=c3a25b9a…` returned **200 `{"items":[],"count":0}`** at 19:29:37Z and again at 19:29:54Z.
- **What Graph says.** Graph listed **4** deleted containers of type `8a6ce34c…`: the two above plus `b!QPhJA-…` and `b!vlLJSa…`.
- **Where they were dropped.** Each per-id `GET deletedContainers/{id}` answered 200. These are the ownership guard's (task 227d) marker reads, cached as `sdap:graph:marker:deleted:*`. No binding read followed, and no "left out of a list — reason unbound/malformed" warning was logged.
- **Likely cause.** The deleted-container marker or binding read does not see `customProperties` on `deletedContainers/{id}`. That is the risk §11.16 (i) left unverified.
- **Effect.** No admin, root included, sees any deleted container in the SpeAdminApp recycle bin. The by-id restore and permanent-delete routes probably refuse the same way. Nothing is exposed.
- **Next step.** Confirm with a read-only `GET /beta/storage/fileStorage/deletedContainers/{id}?$select=id,containerTypeId,customProperties` as the BFF app (§11.16 (i)).

**Verdict.**
- **165 (b) PASS. 165 (c) PASS** (POML write probes and the §14.9 leaf half). One exception: the POML's "POST naming X's containerTypeId → 403" is superseded by owner round 20 item 3 and answers 201 by design. The per-customer identity (consumingAppId) gives the 403.
- **165 (g) PASS**, read and write parts.
- **Left for 165 under the POML:** only **(d)**, the SpeAdminApp UI check. (a) and (k) passed in G6, and (e) was done during development. The API half of (d) behaved: the root admin started a bulk delete on throwaway containers and it completed.
- **Note gates outside the POML still unrun:**
  - §9 (f): the register 400 probe.
  - §11.16 (i): the deleted-container binding read. F-165-RB above is negative evidence for it.
  - §11.16 (j) WRITE half: the customproperties PUT → 403. Its read half is covered by cs5/cs6: a leaf admin cannot reach a root config at all.
  - §14.9 (d): the next real L2 provisioning run.

## 2026-10-11 #1629 rollout (PR #1639, master 89480bff8)

**Reviews**
- Two full verifiers (correctness and security) both FAILED round 0.
- Fix round 1 then failed its re-check: a Direct thread that also carries a typed lookup still granted to the thread owner's sharees, and the grant skipped the record's owner team instead of the message's owner team.
- Fix round 2: the re-check passed the code, and its only finding was two doc comments, fixed in round 3.
- Final: full BFF unit suite 19,441 passed, 0 failed; ArchTests 940/940.

**Behaviour that is now live, with no flag**
- Message read grants go to the typed anchor's lookup users, plus members of the teams those lookups name, except the team that owns the message.
- A secure message is never granted.
- The audience is fixed when the message joins its thread, never by re-reading the thread afterwards.
- A Tier-2 default-thread join requires every other typed lookup to be empty.
- Tier 3 (the catch-all thread) grants nobody, which closes the read part of #1637.

**Merge and deploy:** merged on Router green, without `--delete-branch` (the legacy Build & Test was still queued). The BFF is deployed to dev; the 4 critical-file hashes match and `healthz` passes. #1629 is closed.

**Filed:** #1644 (channel-ref hijack; keep `MembershipReconcile:SweepEnabled` off), #1645 (Tier-2 routing hijack), #1646 (report-card threads).

**Owner questions:**
1. Should replying into a thread require AppendTo on its record? That would block View Only sharees from replying.
2. When a regarding is set on an existing Direct thread later, should new messages keep going to the two parties, or to the record's audience (current behaviour, fails closed)?

## 2026-10-12 #1666 rollout (F-165-RB part 2)

- #1666 merged as `67e06a626` (squash, pinned to the verified head `ed464acd5`; both verifiers PASS).
- Attempt 1 of CI run 38199684912 failed 5 Task 167 Production-boot Arch Tests. The cause is the Redis managed-identity path, not #1666: master run 38161601966 at `848f639` failed the same 5 tests. Re-running only the failed jobs passed. Filed as #1672.
- BFF deployed to dev from `C:\wtdep` at `67e06a626`: all 4 critical files match (SHA-256), `healthz` passes, and both CORS origins are present.
- Behaviour now live: a recycle-bin container whose ownership this stamp cannot prove answers the uniform 404, not 503. Protected B (an ownership record that survives deletion, #1652) is next, on `fix/uac-r2-1652-recyclebin-ownership-record`.

## 2026-10-12 #1732 rollout (F-165-RB part 1, "protected B": an ownership record that survives deletion, #1652)

- **Reviews.** Round 1 verifier: CHANGES REQUESTED (F1 concurrent-restore race; F2 untested gate branches). Round 2 (`f32d26e17`): ETag-conditional record changes (Cosmos If-Match), three branch tests, pre-POST credential faults rethrown. The independent Opus re-check came back CLEAN. Known limits K1–K4 are recorded in the PR body (no purge on any path; recoverable within 93 days).
- **Tests at `f32d26e17`.** 1047/1047 SPE admin, recycle-bin, tenant, container and ownership tests. ArchTests 940/940. The executor's full suite had 3–5 TestHost timeouts in unrelated classes under 73–96% machine load; all passed when re-run alone. CI full unit tests (Tier 2) passed.
- **CI.** Router green. "Server tests (office scope)" failed with no failed step after 20 minutes, the same as the cancelled/timed-out runs of that gate on master. Not related to this change.
- **Merged** as `4b0057b5b`: squash, pinned to `f32d26e17`, `merge-tree` clean against `1ce87fa79`.
- **Deployed.** The BFF went to dev from `C:\wtdep` at `4b0057b5b`. All 4 critical files match (SHA-256), `healthz` returns 200, and both CORS origins are present.
- **Filed.** #1755: `ProvisionProjectEndpoint` Step 7 runs on the request token, so a client disconnect leaves an orphan container. Not uac-r2-impacting.
- **Live gate: PASS** (2026-10-12 20:57:56–20:59:16Z, root admin, `spaarke-bff-dev` at `4b0057b5b`).
  - Throwaway container `zz-uac-1732-gate-20261012205759`, type `8a6ce34c…`, config `c3a25b9a…`. It is in the bin with its ownership record and has been added to `Remove-TestContainers.ps1`.
  - **A, listed in the bin:** the pre-check bin was empty (containers deleted before this deploy have no record). After create (201; marker and BU stamp present) and a bulk soft delete (202; 1/1), the bin listed the container on the first read.
  - **B, restore:** `POST /recyclebin/{id}/restore` answered 200. The container was then live, with the marker `spaarkeCustomerId=spaarke` and the BU stamp `06fbf21c…` both back, and it was no longer in the bin.
  - **Clean-up:** a second bulk soft delete (202; 1/1). The live read answers 404 and the bin lists it again.
  - **App Insights:** the traces show "Recorded the ownership … before its soft delete" on both deletes, and "Restored container … re-marked and re-bound … from its ownership record". No rollback Critical log appeared.
  - **Found, not caused by #1732:** `SpeAuditService` fails every audit-row create with a Dataverse 400, including on the 2026-10-11 gate 165 run. The failure is swallowed, so no `sprk_speauditlog` rows are written on dev. Filed as #1779.
  - **Cosmetic:** the bin list returns `deletedDateTime: null`. The soft delete also does not evict the live-marker cache key, but nothing leaks: the live read still answers 404.

## 2026-10-12 #1657 + ADR-028 A7 rollout (form-script sign-in, #1655/#1656)

- #1667 (ADR-028 Amendment A7) merged as `32f8e7712`. #1657 merged as `9695a4ca7` (head `709652dba`). The round-2 re-check PASSED with no F-class findings: Spaarke.Auth 249/249, form scripts 303/303, bundle fresh. Known limits K2 (redirects) and K3 (`http:` base URL) are added to #1671.
- Read-only form check on dev: only the Matter main form loads `sprk_subgrid_parent_rollup.js` and `sprk_matter_kpi_refresh.js`, and it registers `bff_auth.js`. `sprk_kpi_subgrid_refresh.js` and `sprk_kpiassessment_quickcreate.js` are referenced by no form.
- Deployed to dev with `Deploy-WebResourceInline.ps1 -WebResourceType 3` and a scoped PublishXml each, `bff_auth.js` first. All 5 live copies are byte-equal to the source: bff_auth 352,432 B; rollup 14,777; matter_kpi 14,078; kpi_subgrid 15,099; kpiassessment_quickcreate 20,535.
- Still open for task 153: the owner re-checks the access banner as testuser1 in InPrivate. The expected result is no red banner and no AADSTS50058 in the console.

## #1801 / task 184 live gate (2026-10-13)

Run 2026-10-13 15:03–15:45Z against spaarke-bff-dev (healthz 200) and spaarkedev1. Read-only except for the BFF's own invite.

**Identities.** All existing ones:
- admin (`az` default, `1d02f31c…`, System Administrator, so its reads of the secured column are authoritative);
- testuser1 (`8d7bad7a…`, `AZURE_CONFIG_DIR=C:/tmp/az-uac-child`, delegated Dataverse token);
- the CIAM Test User contact `394fda9f…` (oid `b720e239…`, plane External).

**Tokens.** `api://1e40baad…/.default` for the BFF; `az` tokens for Dataverse, App Insights and the Kudu VFS read; one read-only Graph `GET /users` on the CIAM tenant `7052feba…` with the admin's existing `az` login. All held in memory only.

**No writes.** No Entra, Key Vault, app-setting, role or tenant-config write, and no prod vault read. The gate wrote no Dataverse row and created no CIAM user.

**Files.** Session scratchpad `gate184/`: `gate184.py`, `gate184.log`, `ai.py`, `g1*.kql`, `g2*.kql`. The log's `[G2] FAIL` line is a harness defect, corrected in the log itself: the communication check selected `subject` instead of `sprk_subject`, so it answered 400.

**The build is #1801.**
- Kudu OneDeploy `2a79ead3…` ran 14:49:03–14:49:07Z. The single instance `39ab6951…` logged "Application started" at 14:49:26Z.
- The deployed `Sprk.Bff.Api.dll` (17,521,152 B, read through Kudu VFS) contains all five 184-only strings: `sdap.access.invite.invite_marker_not_written`, `sdap.access.deny.contact_not_invited`, `sprk_externalinvitedon`, `ExistingAccount`, `CiamExistingAccountTemplate.html`.

**The column on dev.** `contact.sprk_externalinvitedon` is DateTime with `IsSecured = true`, created 2026-10-13T07:41:27Z. Its field permissions:
- **Spaarke BFF-Managed Field Readers** `42e542c8…`: read 4, create 0, update 0. Held through 6 teams, including "Spaarke Business Unit 1", which testuser1 is in.
- **Spaarke BFF-Managed Field Writers** `917b64c5…`: read, create and update 4. Members: only the 2 BFF app users, `# mi-bff-api-dev` and `SDAP-BFF-SPE-API`.
- **System Administrator** `572329c1…`: read, create and update 4. This is the platform default. It also lists the team "Spaarke Demo" (see #1343).
- No contact on dev carries a marker yet: `sprk_externalinvitedon ne null` returns 0 rows.

| # | Gate | Result | Evidence |
|---|---|---|---|
| G1 | Startup and regression | **PASS** | **Startup:** "Application started" 14:49:26Z, with no startup exception. Eight `RedisTimeoutException`s "could not take the scheduler lease for '…'" all fell at 14:50:05Z, during the cold start, one per scheduled job. The 7 days before had none and none followed. They are transient and outside 184's surface. **Exceptions since 14:45Z:** otherwise only Graph "Invalid hostname for this tenancy" (pre-existing: ~280/day since 2026-10-07, the known orphan container), and one `InvalidOperationException` "Failed to create service request: Forbidden". **5xx since deploy:** exactly one, `POST /api/v1/external/servicerequests` 500 at 14:57:35Z (op `14164aae…`). It was a WORKFORCE caller (admin, `[WF-AUTH]`), and Dataverse refused the create with 403 `0x80040299` "Read Privilege Check For Owner failed … Principal team `cf15f587…`". That is SPA-r3's known intake-403 (I-6), not #1801 code; the next try at 14:59:49Z answered 201. No 5xx on the invite routes. **`[ID-BIND]` / E11:** none since the deploy, and no CIAM-issuer (`ciamlogin`) token reached the BFF, so no CIAM sign-in happened. The last `[ID-BIND]` traces were on 2026-10-09 (25). The live E11 deny/bind is therefore **not observed**. |
| G2 | AlreadyProvisioned unchanged | **PASS** | **Before:** `394fda9f…` oid `b720e239…`, plane 100000000, `sprk_externalinvitedon` null, versionnumber 26691587, modifiedon 2026-10-02T17:48:44Z. **The call:** admin `POST /api/v1/external-access/invite` {spaarkedev@gmail.com, project `54fb306c…` (Standard, non-secure)} → **200** `{"contactId":"394fda9f…","status":"AlreadyProvisioned"}` (op `3946a2da…`, 15:22:49Z). **After:** identical (marker null, versionnumber 26691587, modifiedon unchanged). **Traces:** `[DELEGATION] ALLOWED`, `[EXT-INVITE] Onboarding …`, then `[EXT-INVITE] Contact 394fda9f… already has a CIAM oid bound — skipping account creation`. **Dependencies:** WhoAmI, RetrievePrincipalAccess, sprk_projects, contacts GET, systemusers GET. No Graph call (no `POST /users`), no contact PATCH, no send, no exception. **No email:** `sprk_communications` created ≥ 14:00Z: 0 rows. |
| G3 | ExistingAccount, the real Graph ObjectConflict | **NOT AVAILABLE** (no write made) | The gate cannot reach ExistingAccount on dev within its write limits. `DecideInvite` resolves the invite email over ACTIVE contacts, and with more than one row it returns **Refuse `sdap.access.invite.email_ambiguous`** (409). The binder then **writes identity-collision flags on every matching contact** (`ResolveInviteContactAsync` → `WriteFlagsAsync`). So a throwaway contact on spaarkedev@gmail.com would get a 409 and also flag the bound CIAM Test User `394fda9f…`, never reaching Graph. **Alternatives checked:** a read-only Graph `GET /users` on the CIAM tenant lists 5 email accounts: spaarkedev@gmail.com, sarah.chen@example.com, rschroeder001@gmail.com, eyal.iffergan@spaarke.com, ralph.schroeder@hotmail.com. **Each has exactly one active, bound contact** on dev (`394fda9f…`, `52bb55e7…`, `43c4e819…`, `8cb95c16…`, `2e419a4f…`), so every target has the same problem. Inviting a CIAM user's `smtp:<oid>@spaarkeextid.onmicrosoft.com` proxy address was rejected: whether Graph conflicts on it is a guess, and a wrong guess creates a CIAM user. The throwaway was therefore **not created**, and there was nothing to deactivate. **Not a #1801 defect:** the refusal is task 141's design. ExistingAccount's real case is cross-environment: the person has a CIAM account from another customer and THIS environment has no contact for them, or one unbound contact. Single-environment dev has no such email. |
| G4 | Marker field security, read-only | **PASS (metadata and live read path); value and update legs NOT AVAILABLE** | testuser1 (non-admin) is in the Readers profile through the "Spaarke Business Unit 1" team: read 4, create 0, update 0. It is in neither the Writers profile nor the System Administrator profile. **Live:** testuser1's delegated `GET contacts?$select=contactid,fullname,sprk_externalinvitedon` → 200, 2 readable contacts, with the column present (null). A filter on the column → 200 []. A per-record read of `394fda9f…` and of testuser1's own linked contact `ac6d7b68…` → 403 `0x80048306`, a record-level ReadAccess refusal (both are owned in the root BU), not field security. **The value leg:** no contact holds a marker (G3 could not write one), so a readable null and a masked null look the same. **The update leg:** not attempted, because it would be a Dataverse write outside the gate's allowed set. Update 0 for the Readers profile is shown by the field permission above. |

**Defects found in #1801:** none.

**Observations:**
- **(O-184-1) G3 is blocked by the environment, not by the code.** To prove ExistingAccount live, the owner picks one of:
  - (a) authorise deactivating one fixture contact for the length of the gate. The Sarah Chen contact `52bb55e7…` is preferred, because example.com sends to no one. Then invite a throwaway on that email, deactivate the throwaway, and reactivate the fixture.
  - (b) run it from a second environment (a provisioning stamp), where the person has a CIAM account but no contact. That is the real cross-customer case.
  - (c) accept the offline proof: `ExistingCiamAccountInviteTests` and the "every 400 is ExistingAccount" mutation.
  - The G4 value leg can follow in the same run as (a) or (b), once a marker exists.
- **(O-184-2)** Only the BFF and System Administrators can write the marker, not the BFF alone. The System Administrator field-security profile holds read/create/update by platform default, and on dev it includes the team "Spaarke Demo". That team's 13 members are all disabled today. This is already filed as **#1343** (the Spaarke Demo team carries the System Administrator role). It now also covers `sprk_externalinvitedon`, because a System Administrator can stamp a marker and so open an unbound contact to an E11 email bind. No new issue.
- **(O-184-3)** No CIAM sign-in has happened since the deploy, so the live E11 intent gate (`contact_not_invited`, a marker bind, a grant bind) has not been observed. It needs a real CIAM sign-in, which the agent cannot perform.

**Clean-up:** none needed. Nothing was created. The CIAM Test User contact `394fda9f…` is unchanged (versionnumber 26691587, marker null, no flag written).

## #1827 / task 185 live gate (2026-10-14)

Run 2026-10-14 14:05–14:13Z against spaarke-bff-dev (healthz 200) and spaarkedev1, at master `6da771b96`. Read-only except for the BFF's own idempotent invite. The live E11 CIAM sign-in was **not run**: it needs a real CIAM sign-in, so it goes on the owner checklist.

**Identities.** All existing ones, as for 184:
- admin (`az` default, `1d02f31c…`, System Administrator, so its reads of the secured columns are authoritative);
- testuser1 (`8d7bad7a…`, `AZURE_CONFIG_DIR=C:/tmp/az-uac-child`, delegated Dataverse token; its only team is "Spaarke Business Unit 1");
- the CIAM Test User contact `394fda9f…` (spaarkedev@gmail.com, oid `b720e239…`, plane External, bound).

**Tokens.** `api://1e40baad…/.default` for the BFF, plus `az` tokens for Dataverse, App Insights and the Kudu VFS read (ARM resource). All were held in memory only and never printed or stored.

**No writes.** There was no Entra, Key Vault, app-setting, role or tenant-config write, and no prod or non-dev vault read. The gate created no Dataverse row and no CIAM user. Its only BFF write was the admin `POST /invite` below, which answered AlreadyProvisioned with no side effect. No invite code was seen, and the hash column was reported only as null or non-null (every value was null).

**Files.** Session scratchpad `gate185/`:
- `g0.py`: the Kudu deployment record and the DLL string scan, done in memory.
- `gate185.py` and `gate185.log`: G2, G3 live, G4 and G5.
- `g3b.py`: testuser1's secured-attribute privileges.
- `ai.py`, `g1*.kql`, `g2*.kql` and their `.out` files: App Insights.
- `schema/`: `Set-ExternalInviteCodeHashSchema.ps1` and `common/DataverseSolutionMembership.ps1` as at `6da771b96`, with `verify.log`. The worktree branch does not contain the script, so it ran from this copy.

Three App Insights queries first failed because of the harness, not the BFF. `az.cmd` is a batch file, so cmd.exe read `<id>` and `|` inside the KQL as redirections, and it split the multi-line `let` queries. They were rewritten and rerun, and the results below come from the reruns.

**The build is #1827.**
- Kudu OneDeploy `9089065e…` ran 14:02:33–14:02:36Z. The single instance `39ab6951…` logged "Now listening" and "Application started" at 14:03:03Z, with no startup exception.
- The deployed `Sprk.Bff.Api.dll` is 17,573,376 B (184's was 17,521,152 B), SHA-256 `38dc1ccec4b2088ed94ad8f0f94b0bf8d39506f1889b0faa5414d8480d479f3c`.
- It contains every 185-only literal. These were grepped at `6da771b96` first, and none is present at `06b3654a0` (184). They sit in the DLL as UTF-16 user strings:

  | Literal | Source |
  |---|---|
  | `X-Spaarke-Invite-Code` | `CallerPrincipalResolver.cs:549`, `CorsModule.cs:130` |
  | `sdap.access.invite.email_not_sent` | `GrantInviteResult.cs:59` |
  | `sdap.access.invite.ciam_not_configured` | `GrantInviteResult.cs:65` |
  | `sprk_externalinvitecodehash` | `ContactBindingDecision.cs:1291` |
  | `spaarke.invite-code.v1` | `InviteCode.cs:45` |
  | `[invitation link removed]` | `RegistrationEmailService.cs:201` |
  | `NeedsInternalInvite` | the I-4 grant-response status |

**The column on dev.** `contact.sprk_externalinvitecodehash` was created 2026-10-14T00:40:17Z, with metadata id `94470fcf…`. It is String, max 100, `IsSecured = true`, `IsAuditEnabled = false` and `IsValidForAdvancedFind = false`. Its field permissions:
- **Spaarke BFF-Managed Field Readers** `42e542c8…`: **no field-permission row**, so it grants nothing. Its members are 6 teams (Secure Record, Spaarke Business Unit 1, Spaarke Demo, Spaarke Dev 1, Spaarke Test 1, Spaarke) and no users.
- **Spaarke BFF-Managed Field Writers** `917b64c5…`: read, create and update 4. Its members are only the 2 BFF app users, `# mi-bff-api-dev` (`5967251e…`) and `SDAP-BFF-SPE-API` (`1e40baad…`), and no team.
- **System Administrator** `572329c1…`: read, create and update 4, the platform default. It lists the team "Spaarke Demo" (#1343) and about 50 users, nearly all platform service app users. The others are Ralph Schroeder, Delegated Admin, the disabled `# Admin Development` and the decommissioned `# spaarke-bff-api-prod` (`92ecc702…`, see O-185-3).
- **No contact on dev holds a hash or a marker.** `sprk_externalinvitecodehash ne null` returns 0 rows and `sprk_externalinvitedon ne null` returns 0 rows.

| # | Gate | Result | Evidence |
|---|---|---|---|
| G0 | Build identity | **PASS** | See "The build is #1827" above. The deployment record, the DLL size and the five required literals (plus `[invitation link removed]` and `NeedsInternalInvite`) were read through Kudu VFS. No literal is in 184's source. |
| G1 | Startup and regression | **PASS** | **Startup:** "Application started" at 14:03:03Z, with no startup exception. **Exceptions from 13:52Z (10 min before the deploy) to 14:13Z:** only two kinds, both pre-existing. (1) Graph "Invalid hostname for this tenancy" (the known orphan container, a steady 12/h before and after the deploy). (2) One `RedisConnectionException` at 14:04:40Z, "could not renew the scheduler lease for 'secure-child-reconciliation'", during the cold start. The same exception appears once a day on 10-09, 10-12 and 10-13 and 25 times on 10-06; it is transient and outside 185's surface. **New exceptions:** none. **5xx since the deploy:** 0. Requests since 13:52Z: `/healthz` 200, `GET /` 401 and the gate's own calls. **No code or hash in telemetry:** a 14-day scan of traces, exceptions, requests, dependencies and customEvents (about 7.7 M items, which also covers the earlier 185 builds deployed during review) found **0** matches for the stored-hash shape `v1.<22>.<43>`, **0** for the code-in-link shape `invite=<43 chars>` and **0** for the header name. The "invite" traces since the deploy carry only IDs, the email, a status and the token audience and issuer: `[EXT-INVITE] Onboarding external user …`, `[EXT-INVITE] Contact 394fda9f… … already has a CIAM oid bound — skipping account creation`, `[DELEGATION] ALLOWED …` and the G5 "No Authorization header …" lines. **CIAM:** **no `ciamlogin`-issuer token and no `[ID-BIND]` trace since the deploy.** The last CIAM token was 2026-10-13T20:20:40Z, about 18 h before the deploy, from an SPA session in which all 101 binds were `ResolveByOid` (an already-bound contact). The live E11 code-gated bind (`contact_not_invited`, a marker-and-code bind, the consume) is therefore **not observed**. |
| G2 | AlreadyProvisioned unchanged | **PASS** | **Before:** `394fda9f…` oid `b720e239…`, plane 100000000, statecode 0, marker null, **hash null**, versionnumber 26691587, modifiedon 2026-10-02T17:48:44Z. **The call:** admin `POST /api/v1/external-access/invite` {spaarkedev@gmail.com, project `54fb306c…` (Standard, non-secure)} → **200** `{"contactId":"394fda9f…","inviteRedeemUrl":"https://green-dune-0c4f1221e.7.azurestaticapps.net","status":"AlreadyProvisioned","emailSent":null,"emailErrorCode":null}` (op `3834b766…`, 14:11:15Z). Both new fields are present and null, as `fe25cd36c` specifies, and `inviteRedeemUrl` is the BASE portal URL, with no code. **After:** identical in every field (marker null, hash null, versionnumber 26691587, modifiedon unchanged). **Dependencies of the op:** token and OIDC metadata, WhoAmI, RetrievePrincipalAccess, `sprk_projects` GET, one Organization.svc RetrieveMultiple on `sprk_project`, contacts GET and systemusers GET. There was **no Graph call** (so no `POST /users`), **no contact PATCH** and no exception, and no Graph call or contact write happened in any operation after 14:11Z. **No email:** `sprk_communications` created since 14:11:13Z: 0 rows. |
| G3 | Field security of the hash | **PASS (metadata, profiles, `-Verify`, the platform's own privilege view, live read path); value leg NOT AVAILABLE** | **Metadata and profiles:** see "The column on dev" above. Readers have no row, Writers have 4/4/4 with only the 2 BFF app users, and the System Administrator default applies (#1343 for Spaarke Demo). **`-Verify`:** checked first. Every `Invoke-DvWrite` in the script sits behind an `elseif ($Verify)` / `if ($Verify) { continue }` / `$IsDryRun` guard, `$IsDryRun` is true under `-Verify`, `PublishXml` runs only under `-Apply`, and the dot-sourced helper issues only GETs. `-Verify` also needs `-BffApplicationIds`, so it ran with `5967251e…,1e40baad…` and answered **VERIFY PASS, exit 0**: field-secured, auditing off, Advanced Find off, Readers no permission, Writers 4/4/4, no stranger, and the column plus both profiles in SpaarkeCore and SpaarkeMaster. **The platform's view:** `systemusers(testuser1)/RetrievePrincipalAttributePrivileges()`, as admin and as testuser1 itself, lists 52 secured-attribute privileges. They include the marker (read 4, create 0, update 0) and **do not include the hash**, so testuser1 has no privilege on it. **Live:** testuser1's delegated `GET contacts?$select=contactid,sprk_externalinvitecodehash` → **200, 2 rows, the column key present in both, null in both** (no 403). A `$filter=sprk_externalinvitecodehash ne null` → 200 []. Per-record reads of `8e9918a9…` and `2e419a4f…` → 200, hash null. **The value leg:** no contact on dev holds a hash (the admin's `ne null` also returns 0), so a masked null and a readable null look the same. Writing a hash takes an invite to an unbound contact, which writes the marker and either creates a CIAM user or takes the ExistingAccount branch (184's O-184-1). Both are outside the gate's limits, so it was not attempted. |
| G4 | Migration count (goal 6) | **PASS: 0** | Admin, read-only. `sprk_externalinvitedon ne null and sprk_externalinvitecodehash eq null`: **0 rows, no ids**. The full release-note query (adding `sprk_externalobjectid eq null and statecode eq 0`): 0. `sprk_externalinvitedon ne null`: 0. This matches design §8's 2026-10-13 count. No contact on dev needs a re-invite. |
| G5 | Unauthenticated | **PASS** | No token: `GET` and `POST /api/v1/external-access/invite` → 401; `GET` and `POST /api/v1/external-access/invite-and-grant` → 401; `POST /api/v1/external-access/grant` → 401; each answers `WWW-Authenticate: Bearer`. `POST /invite` with no token and a junk 43-char `X-Spaarke-Invite-Code` → 401. `GET /healthz` → 200. App Insights logs each 401 as "No Authorization header on …" and nothing else. |
| — | Live E11 CIAM sign-in | **NOT RUN (owner checklist)** | It needs a real CIAM sign-in through the new invite link. None reached the BFF after the deploy (G1). |

**Defects found in #1827:** none.

**Observations:**
- **(O-185-1) The live code-gated bind is unproven on dev until the owner runs it.** It needs:
  1. an invite to an unbound contact, which writes the marker and the hash and sends the email with `#invite=`;
  2. a CIAM sign-in through that link, which binds and clears the hash;
  3. a second sign-in, or another account, without the code, which is denied with `contact_not_invited`.

  These steps also give the G3 value leg: once a hash exists, testuser1's read of it should come back null while the admin's comes back non-null. The ExistingAccount constraint is the same as O-184-1.
- **(O-185-2) The System Administrator profile can read and write the hash** (the platform default), as with 184's marker. Any System Administrator, and every member of the "Spaarke Demo" team (#1343; its members are all disabled today), could set a hash of their own on an unbound contact. **#1343 now also covers `sprk_externalinvitecodehash`.** No new issue. The script's PASS line, "only the BFF can read or write it", reports this default as INFO rather than FAIL, which is the design (§3).
- **(O-185-3) The decommissioned `spaarke-bff-api-prod` app user (`92ecc702…`) is still a dev System Administrator.** This is already recorded as a residual item in `docs/assessments/azure-prod-stack-decommission-2026-08-31.md`, where its service principal is `accountEnabled: false`, so it cannot get a token. It is not 185's concern. No new issue.
- **(O-185-4) An unreadable secured column does not refuse a filter.** testuser1's `$filter` on the hash answered 200 [] rather than an error. With no hash on dev, this cannot show whether Dataverse evaluates the filter against the masked value. It belongs to O-185-1's value leg.

**Clean-up:** none needed. Nothing was created. The CIAM Test User contact `394fda9f…` is unchanged: versionnumber 26691587, marker null, hash null, no flag written.

## #1861 / task 187 live gate (2026-10-14)

Run 2026-10-14 18:50–19:10Z against spaarke-bff-dev (healthz 200) and spaarkedev1, at master `8a1f3bb11`. Read-only: every authenticated BFF call and every Dataverse call was a GET.

**Identities.** All existing ones:
- admin (`az` default, `1d02f31c…`, a System Administrator in Dataverse). On the external plane it is a workforce systemuser with a COMPOSED set, not "reads everything": 7 projects (including secure `65a3fab2…`), 24 matters and 22 work assignments (`[WF-AUTHZ] Composed accessible set …`). It cannot read secure project `31e232ae…`;
- testuser1 (`8d7bad7a…`, `AZURE_CONFIG_DIR=C:/tmp/az-uac-child`, linked contact `ac6d7b68…`; 20 projects). It can read **neither** secure project (403 on `65a3fab2…`), so it is the "M-only" caller G4 needs.

**Tokens.** `api://1e40baad…/.default` for both BFF callers, plus `az` tokens for Dataverse, App Insights and the Kudu VFS read (ARM). All were held in memory only and never printed or stored.

**No writes.** There was no BFF write call, and no Dataverse, Entra, Key Vault, app-setting, role or tenant-config write. There was no prod or non-dev vault read. The gate created no record and no CIAM user. G5 sent `PATCH`, `DELETE` and `POST` with **no token** to fixed non-existent ids (`00000000-…-0001`). Each answered 401 in the auth middleware before any handler ran.

**Files.** Session scratchpad `gate187/`:
- `g0.py`, `g0.out`: Kudu deployment record and DLL scan, in memory.
- `pull.py`, `meta.py`, `meta.json`, `data.json`: read-only pull of the lineage tables (ids and lookup GUIDs only).
- `model.py`: the 187 rule modelled locally over that pull (stamps ∪ live tops of the `ParentLineage.ChildFiling` parents, secure-if-any).
- `gate187.py`, `gate187.log`, `run2.out`, `lists.json`: G2, G3 and G5.
- `ai.py`, `g1*.kql`, `g2*.kql`, `g5.kql`, `d*.kql` and the `.out` files: App Insights.

**Harness defect, corrected.** Run 1's "withheld" column read the list items' `id` key. The items carry `sprk_todoid`, `sprk_eventid` and `sprk_documentid`, so run 1 reported every row as withheld. Its n and DV-n columns were right. Run 2 (19:02–19:08Z, `run2.out`) uses the right key and is the authoritative G2/G3 evidence.

**The build is #1861.**
- Kudu OneDeploy `ea825b9d…` ran 18:45:29–18:45:33Z. The single instance `39ab6951…` logged "Now listening" and "Application started" at 18:45:57Z, with no startup exception. This time there was no cold-start Redis lease exception either.
- The deployed `Sprk.Bff.Api.dll` is 17,614,848 B (185's was 17,573,376 B), SHA-256 `5d39bb765e248bf397e2b82bee34f26927ead04b3fdd74643d72ed73159de592`.
- It contains every 187-only literal below, as UTF-16 user strings. Each was grepped at `8a1f3bb11` and is absent at `6da771b96` and at the merge's first parent. The `[EXT-LIVEPARENT]` tag itself does not occur anywhere at `6da771b96`. 185's `sprk_externalinvitecodehash` and `X-Spaarke-Invite-Code` are also present, as expected on master.

  | Literal | Source at `8a1f3bb11` |
  |---|---|
  | `sdap.access.todo_write_unavailable` | `ExternalProjectDataEndpoints.cs:1749` |
  | `The to-do could not be checked right now, and nothing was changed. Try again shortly.` | `ExternalProjectDataEndpoints.cs:1766` |
  | `[EXT-DELETE] {Kind} {Id}: its live parents could not be checked; nothing deleted` | `ExternalProjectDataEndpoints.cs:1865` |
  | `The invoice's secure roots could not be read.` | `ExternalProjectDataEndpoints.cs:964` |
  | `[EXT-LIVEPARENT] Walk cost: {Queries} filing queries for {Records} records ({Parents} parents) in one request.` | `ExternalDataService.cs:1798` |
  | `[EXT-LIVEPARENT] No filing reader is configured; {Count} parent(s) cannot be resolved (fail closed).` | `ExternalDataService.cs:1835` |
  | `Lookup names must be logical names.` | `ExternalDataService.cs:1183` |
  | `' is not a child table of the lineage map, so its parents cannot be derived.` | `ExternalMultiRootRule.cs:489` |

**Dev data (admin, read-only pull, 18:52Z).**
- Row counts: 66 to-dos, 83 events, 584 documents, 305 communications, 230 threads, 10 invoices, 0 agreements, 1,027 analyses, 3 budgets and 3 report cards. Every lineage lookup the map names exists on dev.
- **Secure roots:** 2 projects, `31e232ae…` (the 174 gate's) and `65a3fab2…`. No matter or work assignment is secure, by its own flag or effectively.
- Rows naming a non-root parent: 16 to-dos, 7 events, 152 documents and 249 communications.

| # | Gate | Result | Evidence |
|---|---|---|---|
| G0 | Build identity | **PASS** | See "The build is #1861" above: the deployment record, the DLL size and SHA-256, and the eight 187-only literals, all read through Kudu VFS. |
| G1 | Startup and regression | **PASS** | **Startup:** "Application started" at 18:45:57Z, with no startup exception. **Exceptions from 18:35Z to 19:10Z, two kinds, both pre-existing.** (1) Graph "Invalid hostname for this tenancy": 7, the known orphan container (1,995 in the 7 days before). The one failed dependency since the deploy is the same container's `GET /beta/storage/fileStorage/containers/…` 400. (2) `InvalidOperationException` "The JSON value could not be converted to System.String. Path: $.value[n].sprk_documenttype": 26 recorded. Each comes from one of the gate's own document-list calls on a list that holds a typed document (30 such calls; telemetry is sampled). This class is pre-existing: 4 occurrences on 2026-10-13 (12:44–20:16Z), before this deploy. See defect D-187-G1 below. **New exception class:** none. **5xx since the deploy:** 0. Requests since 18:35Z are `/healthz`, `GET /` 401 and the gate's own calls. No `[EXT-MULTIROOT]` or `[EXT-DELETE]` line appeared. No `[EXT-LIVEPARENT]` Warning or Error appeared (no faulted, unresolved or bounded walk). |
| G2 | External reads answer 200 | **PASS** (documents: 200 but empty on 15 lists, D-187-G1) | **Every list on a reachable root answered 200.** The admin reached 5 projects and 6 matters, testuser1 4 projects and 11 matters. Each was asked for to-dos, events and documents in two runs. A 403 came only from the route gate, for a root outside the caller's composed set ("You do not have access to this matter/project"; 18 for admin, 6 for testuser1). That answer is unchanged and outside the 187 rule. **Counts (run 2), with Dataverse's count in brackets:** admin matter `2444af6d…` to-dos 7 [7], 3 of them naming a non-root parent, and events 7 [7]. Admin project `5496dc6f…` to-dos 1 [1] and events 4 [4]. Admin project `7524864d…` events 2 [2]: `5fb8c3fa…` is stamped to matter `fcdefa37…` AND to this project and names a non-root parent, and it lists. testuser1 matter `b68299c6…` to-dos 13 [13] (8 naming a parent) and events 10 [10]. testuser1 matter `ec408889…` events 3 [3] (all 3 naming a parent). **Truncated:** the field is absent (false) on every to-do and event list, and `true` only on the 15 document lists D-187-G1 empties. **Messages:** admin matter `86335ce3…` 15, matter `2444af6d…` 4, project `7524864d…` 4. testuser1 matter `ec408889…` 19, matter `b68299c6…` 10, matter `86335ce3…` 15, matter `2444af6d…` 4, project `7524864d…` 4. The admin's 403s on `ec408889…` and `b68299c6…` are `sdap.external.messages.record_not_accessible` (not in its set). **Invoice threads (D1/D4):** admin `f749a11e…` 200, 1 message; `6f9641d0…` 200, 0. testuser1 `bcf81d78…`, `4c0f4d12…` and `6f9641d0…` 200, 0. Invoice documents answered 200 on all five (0, as in Dataverse). **Notifications:** admin 200, 12 items; testuser1 200, 23 items. **Latency (client, run 2):** admin 2.6–3.4 s per list, with the first call 9.1 s (cold caches); testuser1 1.7–2.6 s. Server-side since the deploy, p50/p95: matter to-dos 1.57/2.89 s, matter events 1.57/2.69 s, messages 1.72/2.65 s and notifications 3.05/4.40 s. In the 7 days before, matter to-dos were 1.95/2.28 s, matter events 2.03/2.70 s, messages 1.88/2.96 s and notifications 2.70/3.82 s, so no regression is visible. **The walk in App Insights:** 43 `[EXT-LIVEPARENT] Walk cost: …` lines at Debug, in 12 shapes. They range from "1 filing queries for 1 records" to "3 filing queries for 13 records" and "2 filing queries for 22 records (22 parents)". No request passed the 50-query Information threshold (`WalkQueriesWorthReporting`), so **no Information-level walk-cost line exists**, which is the design. Example, testuser1 `GET /matters/b68299c6…/todos` at 19:05:49Z: the `sprk_todos` GET, then two batched `RetrieveMultiple` calls through `Organization.svc` (`[DATAVERSE] RetrieveMultiple on sprk_invoice returned 5 records`, then `on sprk_reportcard returned 1 records`, about 80 ms each), then "Walk cost: 2 filing queries for 6 records (6 parents)". The walk uses the SDK `id IN` reads, so the query text is not in the dependency `data`; the `[DATAVERSE]` trace names the table. Lists whose rows name no non-root parent issue no walk query. |
| G3 | No over-hiding (read-only spot check) | **PASS for to-dos and events; documents only where D-187-G1 does not empty the list** | **To-dos and events:** in 52 list comparisons (run 2, both callers), the external list equals Dataverse's count of rows whose stamp names that root, to the id. **Withheld: none**, so no withheld id needs a secure-parent explanation. These lists include 19 distinct rows (13 to-dos, 6 events) that name non-root parents (communications, invoices, report cards, documents and events), whose live tops are all ordinary. **Documents:** lists without a typed document match exactly: admin and testuser1 project `54fb306c…` 1 [1], matter `007f2a9d…` 1 [1], and the empty ones 0 [0]. Every list holding at least one document with `sprk_documenttype` set returns 0 rows with `Truncated = true`, the whole page failing to deserialize (D-187-G1). Admin: project `5496dc6f…` 0 [3, 1 typed], project `b12496d1…` 0 [6, 1 typed], matter `2444af6d…` 0 [4, 3 typed], `ced2c3d9…` 0 [1, 1], `974c4307…` 0 [1, 1]. testuser1: the same projects, and matters `b68299c6…` 0 [10, 2], `232852f8…` 0 [4, 2], `00aef0ab…` 0 [12, 11], `ec408889…` 0 [8, 6], `fcdefa37…` 0 [2, 2], `42f7ac71…` 0 [6, 3]. **This is not the 187 rule withholding rows.** The failure is `GetCollectionAsync` reading page 1 (`[EXT-DATA] collection_truncated: page 1 failed; nothing was read`, with the exception above) BEFORE the live-parent walk or the rule runs. The local model of the rule finds no secure root, stamped or live, for any of those documents, so the rule would list them all. |
| G4 | Secure wall spot check | **NOT AVAILABLE** (no carrier or stale row exists on dev) | testuser1 is a suitable caller: it cannot read either secure project. But the model over every to-do, event, document, communication and invoice on dev finds **0 rows whose live parents reach a secure root their stamp does not name**, and **0 rows with an undetermined parent**. The only rows under a secure root are 2 documents (`b02ebe33…`, `2b97180e…`) stamped DIRECTLY to `31e232ae…`, which neither caller can reach (route gate). Nothing is filed under `65a3fab2…`. Making a carrier, for example a to-do on ordinary M whose `sprk_regardingcommunication` names a communication under a secure project, takes Dataverse writes, which are outside this gate's limits. **Offline proof:** the PR's tests and mutation evidence (`notes/task-187-external-live-parent.md`, "Mutation and fail-before evidence"). On master source, 26 of 38 new HTTP-level tests fail (every carrier, stale, fail-closed, PATCH and delete-own case). M1, removing the live-parent input, fails 35 of 55 new tests. M2, removing the parent lookups from the `$select`, fails 19. The guard names the five stamps-only sites on master. |
| G5 | Unauthenticated | **PASS** | With no token, every changed route answered **401 with `WWW-Authenticate: Bearer`**: GET to-dos (project, matter, work assignment), events (project, matter), documents (project, invoice), document content and versions, notifications, messages (matter, invoice), and `POST /api/dataverse/fetch`, `PATCH /todos/{id}`, `DELETE /todos/{id}`, `/events/{id}`, `/documents/{id}` and `POST /invoices/{id}/documents`. `GET /healthz` → 200. App Insights did not record these 401s as requests in that window (sampled), so the evidence is client-side. |

**Defects found in #1861:** none.

**Defect found, pre-existing, not 187's (D-187-G1).** An external document list fails whole when any row has `sprk_documenttype` set.
- **Cause:** `ExternalDataService.cs:142` (unchanged since `b78c2360d4`) maps the Choice column as `string?`. The page's JSON deserialization throws, `GetCollectionAsync` logs `collection_truncated: page 1 failed`, and the list answers 200 with `[]` and `Truncated = true`.
- **Reach on dev:** 15 of the gate's lists. The single-document routes may be affected too; they were not exercised.
- **Tracking:** this is SPA-r3 task 064, "External document lists fail when a document has a type". Its TASK-INDEX row says "done, merged `15f2dc686`; reaches dev with the next BFF deploy". **But `15f2dc686` is a merge on the SPA-r3 work branch only. It is not an ancestor of `8a1f3bb11` or of `origin/master`**, so this master deploy does not carry it, and no master deploy will until SPA-r3 merges to master.
- **No new issue filed:** it is tracked by SPA-r3 064. It is reported here and in the summary for routing to SPA-r3's owner (peer-owned).
- **Effect on 187:** it masks the live check of 187's document leg (over-hiding, D4 on invoice routes) on dev until 064 reaches master. A re-check of G3 for documents belongs after that merge.

**Observations:**
- **(O-187-1) A carrier needs a write.** Dev has no carrier or stale row, so the secure wall can be shown live only by creating one: a throwaway to-do on an ordinary matter, with `sprk_regardingcommunication` set to a throwaway communication under a secure project, read by testuser1 (expected withheld) and by a caller with Read on the secure project (expected listed), then deleted. That is outside this gate's limits and needs the owner's say-so.
- **(O-187-2) The walk is cheap on real data.** It took 1–3 filing queries and at most 22 records per request, against the notes' 50-query Information threshold and 250-query ceiling. The Debug walk-cost line does reach App Insights on dev (Verbose traces are shipped), so the per-request cost is visible without the Information line.
- **(O-187-3) The admin is not all-seeing on the external plane.** Its set is the ADR-034 membership plus grants (7 projects, 24 matters, 22 work assignments). Future gates should not assume a System Administrator reads every root through `/api/v1/external/**`.

**Clean-up:** none needed. Nothing was created or changed. Every authenticated call was a GET, and the no-token write verbs were refused 401 before any handler ran.

### #1861 / task 187 live gate: document-leg re-run at `4e23edeb2` (2026-10-14)

Run 2026-10-14 20:15–20:45Z against spaarke-bff-dev (healthz 200) and spaarkedev1. It re-checks the document leg that D-187-G1 masked in the first run. **Read-only:** every authenticated BFF call and every Dataverse call was a GET. Document content was streamed, counted and SHA-256-hashed in memory, then discarded.

**Identities and tokens.** The same as the first run, through the same mechanism (`common.py`, unchanged except the log name):
- admin: the `az` default profile, contact `8e9918a9…`.
- testuser1: `AZURE_CONFIG_DIR=C:/tmp/az-uac-child`, contact `ac6d7b68…`.
- Tokens: `api://1e40baad…/.default` for both BFF callers; `az` tokens for Dataverse, App Insights and the Kudu VFS read (ARM).

Tokens were held in memory only and never printed. There was no Key Vault read of any kind. Phase 1's Dataverse token expired at 20:37Z, after the lists and downloads. Phase 2 (20:39–20:41Z) took fresh tokens the same way and ran the wrong-root control and the invoice routes.

**No writes.** There was no BFF, Dataverse, Entra, Key Vault, app-setting or role write. Secure project `65a3fab2…` got only GET reads: admin's list of it was 0 [0], and testuser1 got 403.

**Files.** Session scratchpad `gate187/docleg/`:
- `g0p.py`, `g0p.out`: Kudu deployment record and DLL scan.
- `pull.py`, `data.json`: the read-only lineage pull, refreshed at 20:19Z.
- `gate_doc.py`, `run.out`: phase 1.
- `gate_doc2.py`, `gate_helpers.py`, `run2.out`: phase 2.
- `g1a.kql`, `g1b.kql`, `g1b.out`: App Insights.
- `docleg.log`: the combined log.

**Dev data at 20:19Z.** Unchanged since the 18:52Z pull:
- Row counts: 584 documents, 66 to-dos, 83 events, 305 communications, 230 threads and 10 invoices. No row was added or removed.
- Secure roots: still only projects `31e232ae…` and `65a3fab2…`.
- Documents stamped to a root: 16 to 6 projects, 135 to 37 matters, 1 to invoice `55328b00…` and 1 to work assignment `84e8f01a…`.

| # | Gate | Result | Evidence |
|---|---|---|---|
| G0' | Build identity | **PASS** | **Deployment:** Kudu OneDeploy `7e142006…` ran 20:14:11–20:14:23Z. The single instance `39ab6951…` logged "Now listening" and "Application started" at 20:14:40Z. The only exception in the startup window is the known Graph "Invalid hostname for this tenancy". **DLL:** `Sprk.Bff.Api.dll` is 17,667,584 B, SHA-256 `8f2d5138c8a30772fab01cdc2263e5e1925a6c2c729a4e6162ae49b877bc6842`. Its InformationalVersion is **`1.0.0+4e23edeb27fcbb74b815aab3d564d6d98fe48047`**. **187 literals:** all five rechecked 187-only literals are present, among them `[EXT-LIVEPARENT] Walk cost: …` and `[EXT-LIVEPARENT] No filing reader is configured; …`, `sdap.access.todo_write_unavailable`, `The invoice's secure roots could not be read.` and `' is not a child table of the lineage map, …`. **Task 064 fix:** both of its literals are present, and both are absent at `8a1f3bb11`. They are `,odata.include-annotations="OData.Community.Display.V1.FormattedValue"` (the `CollectionPrefer` literal, UTF-16) and the `JsonPropertyName` `sprk_documenttype@OData.Community.Display.V1.FormattedValue` (UTF-8, in the blob heap). **SPA-r3-only literals:** two `CommunicationInternalOnlyFloor` messages are present too; they are absent on `origin/master`. **Ancestry:** `8a1f3bb11` and `15f2dc686` (064) are ancestors of `4e23edeb2`. `origin/master` has since moved to `9dfc0576b` (tip committed 20:09:53Z). The master commits missing from this build touch `ExternalDataService.cs` only through `c3445d45e` (#1880, a cherry-pick of the same 064 fix), and the file is byte-identical in both trees. Their other BFF changes are three unrelated files (`AgentTokenService`, `DemoProvisioningOptions`, `IdentityConfigurationValidator`). |
| G1' | Regression (App Insights since 20:14:11Z) | **PASS** | **5xx:** 0. **Exceptions:** one class only, Graph `ODataError` "Invalid hostname for this tenancy" (5). It is the pre-existing orphan container: 1,995 in the 7 days before. Its one failed dependency is that container's `GET /beta/storage/fileStorage/containers/…` 400. **New exception class:** none. **`sprk_documenttype` `InvalidOperationException`:** **0 since the deploy.** The last one was at 19:07:04Z, from the first gate's own calls; by hour it was 13 at 18Z and 13 at 19Z, then none. **Collection reads:** no `[EXT-DATA] collection_truncated` line appeared, and no `[EXT-DATA] documents: … withheld` line either. **`[EXT-LIVEPARENT]` Warning or Error:** none. All 66 `[EXT-LIVEPARENT]` lines are severity 0 (Debug) walk-cost lines, and none passed the 50-query Information threshold. **Walk cost on document lists:** 16 lines in 6 shapes. Matter documents: "3 filing queries for 3 records (2 parents)" ×6, "2 for 2 (1)" ×3, "3 for 5 (3)" ×3, "3 for 8 (5)" ×2 and "2 for 10 (5)" ×1. Invoice documents: "1 for 1 (1)" ×1. The largest is 3 queries or 10 records per request. Lists whose rows name no non-root parent issue no walk query. **Download and versions:** the expected lines appeared: `[EXT-DOWNLOAD] … downloaded` at Information, and `[EXT-DOWNLOAD]` and `[EXT-VERSIONS] … denied` at Warning only for the control below. **Other traffic:** requests from other dev users (`/api/communications/by-regarding`, `no-access`, `ai/search`) were all 200 or 204. |
| G2' | Document reads answer 200 | **PASS** | **Lists:** 124 lists, each caller on 22 projects, 39 matters and the one work assignment. The candidates were every root holding a stamped document, every `/me` project of either caller, and the first run's roots. **79 answered 200: admin 22, testuser1 57.** Every 403 was the route gate's "You do not have access to this project/matter/work assignment", for a root outside the caller's composed set (admin 40, testuser1 5). testuser1's 403s include both secure projects. **Truncated:** absent (false) on **every** 200 list, including all 15 that D-187-G1 emptied. **Coverage:** some caller reached all 37 matters holding documents (135) and 5 of the 6 projects (14 of 16). The exception is secure `31e232ae…` (2 documents), which neither caller can read. **The choice label on the wire:** typed documents now carry `sprk_documenttype` as the label string (for example "Agreement", "Contract", "Email", "Invoice", "Patent", "Proposal"), not the number. Untyped ones carry null. **Download and versions:** a sample of up to 3 per list (a typed document, one naming a non-root parent, then any) made 98 content and 98 versions calls on 77 distinct documents. 41 were typed with no non-root parent, 24 typed and naming a parent, 2 naming a parent only and 31 neither. **95 content calls answered 200**, `application/octet-stream`, 9 B to 7.68 MB. **95 versions calls answered 200**, with 1–40 versions. **3 answered 409** "No file attached", on content and versions alike: `122f4462…` (matter `042f4462…`, both callers) and `d4300ad0…` (project `fb73b08c…`, testuser1). Dataverse confirms both rows have no `sprk_graphitemid` or `sprk_graphdriveid`. That is the post-authorization storage-state answer, correct, and not 187's. **Wrong-root control (uniform 403):** admin asked for `6846bb40…` (under project `5496dc6f…`) through matter `2444af6d…`. testuser1 asked for `c30ddcfd…` (under matter `00aef0ab…`) through project `b12496d1…`. Content and versions both answered 403 "This document is not part of the requested matter/project". Through its own root, the same document answered 200 (37,672 B and 58,520 B). **Invoice documents (all 10 invoices, both callers):** admin got 200 on 2 (`6f9641d0…` and `f749a11e…`, both 0 [0]) and 403 "You do not have access to this invoice" on 8. testuser1 got 200 on 8 and 403 on 2 (`05c8ef8d…`, `b09d85f7…`). `55328b00…` listed 1 [1], its document: content 200 (697,393 B), versions 200 (1). Every other invoice was 0 [0]. |
| G3' | No over-hiding (documents) | **PASS** | **Lists:** in all 79 project, matter and work-assignment document lists that answered 200 (183 rows; 150 distinct documents), plus 10 invoice lists, the external list equals Dataverse's live count of documents stamped through that route's lookup, **to the id**. **Withheld: 0. Extra: 0.** No withheld id needs a secure-root explanation. **Leak check:** no listed document has a secure root, stamped or live, that the caller cannot read. **The 15 lists D-187-G1 emptied now list in full:** admin project `5496dc6f…` 3 [3], project `b12496d1…` 6 [6], matter `2444af6d…` 4 [4], `ced2c3d9…` 1 [1] and `974c4307…` 1 [1]. testuser1: the same two projects 3 [3] and 6 [6], and matters `2444af6d…` 4 [4], `974c4307…` 1 [1], `b68299c6…` 10 [10], `232852f8…` 4 [4], `00aef0ab…` 12 [12], `ec408889…` 8 [8], `fcdefa37…` 2 [2] and `42f7ac71…` 6 [6]. **Typed documents:** the compared lists hold 85 typed-row instances, among them `3a785f76…` with 25 [25] (3 typed) and `89433cdb…` with 5 [5] (all typed). **Non-root parents:** 35 of the 150 distinct listed documents name one: `sprk_relatedcommunication` 19, `sprk_parentdocument` 15 and `sprk_invoice` 1. For 7 of them, the live top is a different ordinary root from the stamp, for example `5b06c600…` and `6fb8c3fa…` on matter `fcdefa37…`, whose live tops include project `7524864d…`. They list, as the rule says they should. No listed document on dev names `sprk_relatedevent` or `sprk_relatedtodo`, so those two parent kinds are exercised only by the offline tests. **The local model (`model.py` over the 20:19Z pull):** it agrees. No document under any reached root has a secure root, stamped or live, and none has an undetermined parent. |

**Defects found:** none. There was no over-hiding, no leak, no 5xx, no new exception class and no `[EXT-LIVEPARENT]` Warning or Error.

**D-187-G1:** fixed on dev by `4e23edeb2` (SPA-r3 064, `15f2dc686`). It is also on master as `c3445d45e` (#1880). Since the deploy, 0 occurrences, and all 15 affected lists list in full.

**Observations:**
- **(O-187-4) The secure wall on documents still has no live carrier.** No document under a reachable root carries a secure root, stamped or live. The only documents under a secure root are the 2 stamped directly to `31e232ae…`, which no caller reaches. So G3' proves no over-hiding, but the withholding branch on documents is shown only offline, as G4 was. O-187-1 (a carrier needs a write) still applies.
- **(O-187-5) Documents with no file answer 409.** `122f4462…` and `d4300ad0…` have no SPE pointers, so content and versions answer 409 "No file attached" after authorization, while the list shows them. This is pre-existing data state and outside 187. A list item gives the SPA no hint that the document has no file. Filed as #1886.
- **(O-187-6) This build is the SPA-r3 work branch, not master.** It contains `8a1f3bb11` and the 064 fix, but not master's three later, unrelated BFF changes (#1867, #1884). The next master deploy carries the same `ExternalDataService.cs`.

**Clean-up:** none needed. Nothing was created or changed.

## PR #2007 / #2005 live gate (2026-10-16) — PASS (server side)

Run 14:42Z against spaarke-bff-dev and spaarkedev1, on R3 build `1.0.0+a6d30891a` (deployed 14:39–14:41Z). That build contains #2007 `96d5b16c2` and R3's fold of its home bind onto #2007's store method, reviewed CLEAN.

**Writes and identities:** the only write was the BFF's idempotent admin `POST /invite` for the bound CIAM Test User. Identities: admin (System Administrator, so its reads of the secured columns are authoritative). Tokens were held in memory only. The hash was reported only as null or non-null. Script and log: session scratchpad `gate2007/`.

- **G0 build:** Kudu OneDeploy `f8ee5958…` finished at 14:40:33Z. `Sprk.Bff.Api.dll` is 19,102,720 B, SHA-256 `e4fa5502…`. All five #2007-only literals are present:
  - the column-missing fallback warning;
  - the "re-read is not an active, unbound contact" mark refusal;
  - the "no longer an active, unbound contact" detail;
  - `=(cleared)`;
  - `invite mark`.
  `/healthz` returned 200.
- **G1, the invariant census:**
  - 19 contacts are bound. **0 are bound AND holding a hash.**
  - 0 contacts hold a hash, and 0 hold a marker.
  - So no legacy row breaks "a bound contact never holds an unused hash", and no data repair is needed.
- **G2, the idempotent invite on a bound contact:**
  - `POST /invite` returned 200 `AlreadyProvisioned`.
  - The contact is unchanged: versionnumber 26691587, no marker, no hash.
  - My first attempt sent no record target and got 403 `delegation_target_unresolved`. That is the delegation gate refusing, as designed; it is not a regression.
- **G3, App Insights after the deploy:**
  - Only the two expected `[EXT-INVITE]` lines.
  - No `[ID-BIND]` warning or error, and no column-missing fallback.
  - The only exception is #1830's known "Invalid hostname for this tenancy".

**Not run here:** a live bind that consumes a code (workforce, systemuser or CIAM). It needs a real sign-in after a fresh invite, so it stays on the owner checklist §12. The unit, integration and seeding proofs cover it.
