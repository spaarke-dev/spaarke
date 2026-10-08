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
