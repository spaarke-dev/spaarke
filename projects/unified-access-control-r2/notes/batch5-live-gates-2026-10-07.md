# Batch 5 dev live gates (2026-10-07)

Dev BFF: master `dae5869d2` (tasks 171 + 172), deployed 2026-10-07.
- Applied before the deploy: 171 schema (`sprk_graphitemidbound`), field security, then the backfill (544/544 equal).
- `DocumentPointer__ItemIdBoundBackfillComplete=true` set; `sprk_documentuploadwizard` deployed (SHA-256 read back equal).

Full record: session scratchpad `gates/G10-171-results.md` and `gates/g10/g10log.txt`.

## Task 172: PASS (completed)
- testuser1 (BU1, a child BU): `GET /api/users/me/memberships/sprk_matter?roles=ownerid&identityTypes=Team` returned exactly the 59 matters owned by BU1's default team. The pre-fix code would have returned none.
- #1011 closed.

## Task 171: results
- **PASS:**
  - A1, A2: app-only PUT and upload-session.
  - A4, A5: Compose load/save, chat. HARNESS: admin-written pointer on throwaway rows.
  - A6, A7, C1.
  - B1–B3 (API parts).
  - D1, D2, D4: 9 standing writers on BU1 in 13 s, no 429s; hand-granted roles untouched.
  - F: Make Secure. HARNESS.
  - H (API part).
  - I.
  - J1–J3; J5 (variant).
  - F4 (a), (b); F10 (d) for secure and Restricted.
- **FAIL F1 — attach refuses app-uploaded files.** `AttachFileAsync` requires Graph `createdBy.user.id` = caller, so every wizard upload leaves a row with no file. Fix on `fix/uac-r2-171-attach-app-upload`: a server-side uploader binding.
- **FAIL F1 — JIT grant never removed after a full unshare.** Dataverse answers 403 `0x80048306` and the job read it as unknown. Same fix branch.
- **F4 (pre-existing):** `versions/{current}/content` returns 500. Same fix branch.
- **BLOCKED / NOT RUN:**
  - (c) and A3 attach: re-test after the fix. Kept rows: PS 31e232ae with docs 2b97180e and b02ebe33; BU doc 08cbce55 on fb73b08c.
  - D0: blocked by #1351.
  - D3, J4, E, G, C3, Word web/desktop, H desktop, J1 UI: owner screen session.
- **Pre-existing defects filed:**
  - #1350: share-link 502 on every standard document. SPE rejects the link; needs a product decision under broker-only.
  - #1351: SPE admin user grant does not bind the UPN; custom-properties PUT doc mismatch.
  - #1352: the sync job reports Failed every run because of 65a3fab2/#1313; history is short.
- **Note correction owed:** `desktopUrl` is `ms-word:https://…` by design (`DesktopUrlBuilder`: the `ofe|u|` form is blocked by Office security zones). The 171 note's B1 text is wrong.
- **Leftover:** empty container `b!6BMp2UkK50GCP66tPeRxURG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`, added to the owner's container-delete script.

## Update: the 171 hotfix (#1353), task 114 (#1342) and the Access ribbon (#1366), deployed 2026-10-07

Dev BFF: master `dc189faac`, which contains #1353, #1342 and #1366.

### 171: both F1s re-tested live, both PASS
- **Upload then attach**, as testuser1:
  - On secure PS 31e232ae (doc 2b97180e) and on the BU project fb73b08c (doc 08cbce55), a fresh upload followed by `POST /api/v1/documents/{id}/file` returned **200**. Each row's `sprk_graphitemid` equals its `sprk_graphitemidbound` and `sprk_hasfile` is true. `/content` returns 200 on both. This closes gate (c) and the A3 attach.
- **Negative case:** testuser1 attached an admin-uploaded item in the same BU container to its own new doc d4300ad0. Result: **403 `NotTheUploader`**. A cross-container attempt got 409 `WrongContainer`.
- **JIT removal after a full unshare (C2):**
  - `open-links` gave testuser1 a writer role on PS's container. The admin then ran RevokeAccess.
  - Scheduled run db98b221: `jit.removed: 1, unknown: 0`. PS's permission list is empty. Before the fix the same check gave `unknown: 1`.
  - The run shows "Failed" only because of 65a3fab2 (#1352).
  - testuser1's creator share was then restored.

### 114: deployed in this order
1. Guest flags (owner-approved): 2 `#EXT#` users set to Yes, `-Verify` PASS.
2. BFF.
3. Web resources `access_ribbon.js` 1.6.0 and `assignedaccess_postsave.js` 1.1.0.
4. TrackingFieldTrio 1.0.36 (solution and control read back 1.0.36).
5. The Access ribbon (`SpaarkeAccessRibbons`, `-SecureTransitionDeployed`), after #1366 fixed the Share button ids.
   - `-Apply`'s own verify fails straight after publish because the ribbon cache lags; a read-only `-Verify` 75 s later PASSED on all three entities.
   - #1368 makes `-Apply` retry.

**Owed to the owner session:**
- Share is hidden on a Restricted record's form and in a grid selection that includes one.
- The "External user — no access" label.
- The no-internal-reader and owner-is-external admin reports after the job's first runs.

### Test data left
- **testuser1 uploads (test data, BU1 container and PS):**
  - `retest-*.txt`, attached to 2b97180e and 08cbce55;
  - `retest-shape-*.txt`, unattached;
  - `admin-owned-*.txt`, an admin upload.
- **Row d4300ad0** on fb73b08c, unattached.

## Owner test round 1 on 114 (2026-10-07, TrackingFieldTrio 1.0.36) → fixed in 1.0.37 (PR #1369, `8c932a4b0`)

- **Restricted banner, users only:** PASS (owner).
- **Lookup made Manage Access vanish:** fixed. While a lookup is open, `SprkModal` `yieldToSidePane` keeps the modal visible, docked left of the pane, dimmed and `inert`. The composer has the same behaviour → #1371, awaiting the owner's call.
- **External-flagged users offered on Restricted:** fixed. The "+ User" lookup filters `sprk_isexternal ne 1 OR null`; the server's 422 remains the backstop for recent records.
- **Vague "1 failed. Please try again.":** fixed. `/share-user`'s person-specific refusals (external, disabled, not a person, not found, No Access, unverifiable, read fault) now name the person and their email. This also covers the Assigned-To suggestion Grant path.
- **Owner ruling, round 78:** secure-only records may be shared with external-flagged users; the message says "Restricted records" only.
- **Deployed:** TrackingFieldTrio 1.0.37 to SPAARKE DEV 1 (imported and published). The owner re-tests.
