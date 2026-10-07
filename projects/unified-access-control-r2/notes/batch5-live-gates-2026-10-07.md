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
