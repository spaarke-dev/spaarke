# Batch 4 closure sweep (owner round 90), 2026-10-10

**Rule applied (owner round 90):** a task closes when its code is merged and deployed and its server-side live gate
passes. Owner browser checks do not hold a task open; they stay on `notes/owner-hands-on-checklist-2026-10-08.md`.

**Environment:** spaarke-bff-dev at master `ebacd2e15` (healthz 200) and spaarkedev1. Batch 4 was merged by PR #1312
(`d254d7166`), with later fixes #1319, #1320, #1322, #1327, #1328 and #1332.

**Identities:** admin (`az` default) and testuser1 (`AZURE_CONFIG_DIR=C:/tmp/az-uac-child`). testuser1's linked contact
`ac6d7b68…` stands in for a CIAM contact, as it did in the 174 and #1410 gates. No app setting, Entra role or Key Vault
secret was read or written. The only values read were two boolean names: `IdentityLink__Reconciliation__WritesEnabled`
and `ExternalAccess__Reconciliation__WritesEnabled`, both `true`.

**Scripts and logs:** session scratchpad `sweep-b4/`:
- `g157.py`: the 157 server half;
- `sweep.py`: the 137, 140, 142 and 143 gates;
- `run1.out` and `sweep-b4.log`: raw output;
- `meta.py`, `priv.py`: read-only lookups.

**Evidence keys:**
- B4-03 = `batch4-live-gates-2026-10-03.md`
- B4-06 = `batch4-live-gates-2026-10-06.md`
- B5-07 = `batch5-live-gates-2026-10-07.md`
- B5-08 = `batch5-live-gates-2026-10-08.md`
- IS = `batch4-integration-steps.md`
- CL = the owner checklist

## Verdicts

| Task | Verdict | Evidence | Owner input needed |
|---|---|---|---|
| 137 | **CLOSE** (stand-in) | Criterion 14, the report-only run, PASS: B4-06 G1 "137 criterion 14". Criterion 13, the posture: writes have been on on dev since round 79 (project CLAUDE.md:136); the app setting reads `true`. **New run today (S1, S2):** live-gate steps 1, 2 and 4 PASS. Inactive root: 200, then 403 on the first request after deactivation, then 200 after reactivation. An org-grant revoke by a non-admin Write-holder: 403 on the next request, 2.4 s later. | Accept the testuser1 stand-in, as for 174. Step 3 (a deactivated CIAM contact's sign-in is refused) needs a CIAM sign-in: CL §4. |
| 140 | **WAIT-OWNER** | G-140-1 schema PASS: B4-06 G1 "140". **New run today (S0):** criterion 4 PASS. A systemuser that has a linked contact gets 403 `use_manage_access` on POST, GET and revoke of `/api/v1/external/contact-grants`; an anonymous call gets 401. Gate (a)–(h) needs a CONTACT principal as the grantor. By design, no workforce systemuser can act as one. | Four CIAM identities: a Collaborate contact; a colleague in the same organization; a contact in another organization; a View Only contact. The Manage Access half of (f) is a UI check (CL §4). |
| 142 | **CLOSE** | Passed earlier: G-1 ledger PASS (B4-06 G1 "142/158"); sync authorization PASS (B4-06 G2 "142"); G-4 form libraries PASS (B5-08 gatesA #3); G-5 ribbon deployed with `-Verify` PASS (B5-07 "114" step 5); inline materialization PASS (B4-06 G8 B2). **New run today (S3–S7):** criterion 21 (i)–(iv) server halves PASS, and the secure-root suggestion PASS. (ii) Teams/SPA half: 403. A Dataverse share does not count on Teams/SPA until task 036, which is still open, so this follows the design and is not a 142 defect. | (v) Create Matter wizard and the UX checks: CL §1. G-8: set `ExternalAccess__AssignedAccess__JobRevokeOnChangeEnabled=true` once you accept G-7. The job today reports `revokeOnChange:false`. |
| 143 | **CLOSE** | Passed earlier: G-1 schema PASS (B4-03; B4-06 G1 "143"); O2 applied (B5-08 task 154 step 5; gatesA #4 "154 (h)"); D-G5-2 fixed in #1320 (B4-06 "Update" + G7 §3). **New run today (S8–S12):** criterion 14 (i)–(v) PASS on a throwaway secure project. MDA rights were checked by RetrievePrincipalAccess (RPA). The Teams/SPA veto was checked through the contact grant. A Web API GrantAccess stood in for the Share dialog (round 68 precedent). | The UI checks only: CL §1 "143 gate 14". |
| 147 | **CLOSE** | G147-2 and G147-3 PASS (B4-06 G3). G147-5 applied with `-Verify` PASS (IS:65). G147-6 PASS (B5-08 gatesA #9). | G147-4 drives the client writers in the forms: CL §1 "147 / 168 / 169". |
| 150 | **CLOSE** | G-0 PASS (B4-03 "150 G-0"). G-4 and G-5 PASS (B4-06 G1 "150"). G-6 to G-9 PASS (B4-06 G5 "Task 150" and "continued"). G-11 Access ribbon deployed with `-Verify` PASS (B5-07 "114" step 5). | G-10 (the wizard against a LOCAL BFF with a misconfigured owner team) and the ribbon UI tests are client/UI checks: CL §1 "150". |
| 157 | **CLOSE** | API part PASS (B4-06 G2 "157"). The SPA was deployed from master on 2026-10-08 (B5-08 "Task 105", run 37741946955). **New run today (S13):** for all 6 external grids, the live `sprk_gridconfiguration` fetchXml is a subset of the module allow-list (criterion 4, re-derived live). testuser1 also reads each config and runs that exact fetch, and every one answers 200. | The grid walk in Teams: CL §4 "157". |
| 162 | **CLOSE** | (a)–(g), (k) and the delete probe PASS (B4-06 G2 "162"). (i)/(j), the anchorless rows, are covered by G2 (c): f46b4105 is 200 for its creator and 404 for testuser1, and 0e995a06 is 404 for both. (m) PASS (B4-06 G1 "162 (m)"). | (h) and (l) are wizard/UI checks: CL §2. |
| 163 | **CLOSE** | (a), (b) interim and (c) PASS (B4-06 G6 "Task 163"). (b) literal PASS, and the server half of (d) (index-file 200, `sprk_searchindexcompletedon` stamped) PASS: B5-08 gatesA #5. | (d) through the wizard and (e) the insight card: CL §2. |
| 164 | **CLOSE** | (a)–(e), (k) and the retired (h)/(i) routes PASS (B4-06 G6 "Task 164"). D-G6-1 and D-G6-2 were fixed in #1327 and pass live (B4-06 G8 A1, A2). (j) is recorded "not available" (B5-08 gatesA #6), which the POML allows. | (f), (g), (l) and (m) are UI checks: CL §2. |
| 165 | **WAIT-OWNER** | (a), (c) root half and (k) PASS (B4-06 G6 "Task 165"). F-G6-1 fixed in #1327 (B4-06 G8 A3). | (b) and (c) need a leaf-BU SPE admin: grant the SPE Admin app role to testuser1 (an Entra change), and approve the write probes on a throwaway config and throwaway containers. (d) is the SpeAdminApp UI. CL §5. |
| 166 | **WAIT-OWNER**, only for (f) | Passed: §17.1, (a), (b), (c) and (h) (B4-06 G6 "Task 166"); (e) and gate 12 (B4-06 G8 B1, B3); (d), gate 23 and gate 21 (B5-08 gatesA #7); gates 13, 15b, 24/25 and the 27 refusal half (B4-06 G6); the 27 attach positive (B5-07 "171: both F1s"). Reporting (g/11/18) is recorded "module not configured", as the POML allows; the gate 26 FLS refusal PASS. Gate 19 has no FLS-mapped profile and is proven by tests. Gate 14 is moot under 171, because contacts never hold SPE permissions. **(f) has not been run.** testuser1 holds `prvCreatesprk_Matter`, `…Project` and `…Invoice` at Deep (read today), so none of our identities can produce the 403. The positive half PASS (B4-06 G4 "(g) part 1"). | Either a non-admin user WITHOUT `prvCreatesprk_Matter` (or `…Project`) and a token for it, or accept `RecordCreationService` / `QuickCreateSourceAccessFilter` tests as the proof, as was done for gate 19. |
| 168 | **CLOSE** | (a): all four `-Verify` runs exit 0 (B4-06 G6 "Task 168 -Verify"). | (b)–(j) are form checks: CL §1. |
| 169 | **CLOSE** | RegardingResolver 1.6.1 on dev equals master (B5-08 gatesA #8). The invoice-subgrid precheck answers YES (B4-06 G6 "Task 169 precheck"). The derivation is client-side only, so there is no server gate. | Probes (1)–(6) are in the form: CL §1. The next deploy must be 1.6.2 (#1444). |
| 171 | **OUT OF SCOPE** (its row differs: batch 5, #1333) | For reference: its open items are E, the contact plane on a secure record; D0, blocked by #1351; and the owner items G, B2, J4, D3-create and C3 (B5-07; B5-08 gatesA #10). | — (not assessed in this sweep) |

## New gate runs (2026-10-10, UTC)

| # | Gate | Result | Evidence |
|---|---|---|---|
| S0 | 140 criterion 4 | PASS | 04:06:39Z. testuser1 POST, GET and `/revoke` on `/api/v1/external/contact-grants` → **403 ×3**, `sdap.access.contact_grant.use_manage_access`. Anonymous → **401**. |
| S1 | 137 inactive root (live gate 1–2) | PASS | Matter M137 `1e64f7fe…` has a direct ViewOnly grant `2564f7fe…` to `ac6d7b68…`. testuser1 `GET /api/v1/external/matters/M137/todos`: **200**. Deactivated at 04:06:51Z: the first request is **403** (04:06:53Z). Reactivated: **200**. The grant row is unchanged (statecode 0, level 100000000). |
| S2 | 137 org-grant revoke (live gate 4) | PASS | Org `bb3314f9…` has the contact as an active member (`bd3314f9…`). M137b `2364f7fe…` has a Collaborate org grant `2764f7fe…`. testuser1 was given Read/Write by a Web API share, so testuser1 is a non-admin Write-holder (RPA confirmed). Read before: **200**. testuser1 `/revoke` → **200** (04:06:59Z). The next request is **403** at 04:07:02Z, 2.4 s later. Row statecode 1. |
| S3 | 142 (i) | PASS | Matter M142 `11cd7209…` (admin, root BU). An unlinked contact `39ce3d06…` was set as Assigned Attorney 2. Admin `/assigned-access/sync` → **200**. Grant `2969360c…`: level 100000001, expires **2027-01-08** (today + 90); ledger Granted. |
| S4 | 142 (ii) MDA half | PASS | Before: testuser1 RPA **None**. testuser1's contact was set as Assigned Attorney 1, then synced. Result: a POA share (R/W/A/AT/Share), RPA the same, ledger Shared, no grant row for the contact. Teams/SPA read: **403**, because of 036 (see 142 above). |
| S5 | 142 (iii) | PASS | Admin `/revoke` of the auto grant → 200. The re-save sync → 200 with **0 writes**; entry `Declined / removed-by-operator`. Then `assigned-access-reconciliation` ManualAdmin run `41956e98…` Succeeded (`writes 0`). There are 0 active grants for the contact afterwards. |
| S6 | 142 (iv) | PASS | Assigned Attorney 1 cleared (204), then synced → 200 with 2 writes; entry `Revoked / access-removed`. testuser1 POA: none; RPA **None**. |
| S7 | 142 secure root (owner (a), "prompt") | PASS | Secure P `076e033b…`; the unlinked contact was set as Assigned Paralegal 1. Sync → 200; entry `PendingConfirmation` (ledger 100000003), **0 grants**. |
| S8 | 143 (i) | PASS | P was provisioned → 200 (Secure Record Owners, container below). testuser1 RPA before: **None**. `/share-user` Collaborate → 200 (mask 262167): RPA has Read/Write. External `GET /projects/P` (direct ViewOnly contact grant `be02dd4c…`) → **200**. |
| S9 | 143 (ii) | PASS | Entry E1 `840c4c51…` (subject systemuser testuser1, object P). `/no-access/enforce` → **200**, `removed` = testuser1 (previous mask 262167). Afterwards: RPA **None**, no POA, external GET **403**, and `user-shares` no longer lists testuser1. |
| S10 | 143 (iii) | PASS | `/share-user` again → **403** `sdap.access.user_share.subject_no_access`, "This person is on the No Access list for this record, so it was not shared with them." Nothing was written: RPA None. |
| S11 | 143 (iv) | PASS | E1 deactivated (204). `no-access-share-reconciliation` run `ae82b441…` Succeeded. RPA still **None**; the share is not restored. |
| S12 | 143 (v) | PASS | Second entry E2 `0a6f3d63…` active. Web API GrantAccess (the Share-dialog stand-in) at 04:09:36Z → RPA Read/Write. The job run `d0a8292b…` Succeeded with **`removed 1`**. Afterwards: RPA **None**, no POA, external GET **403**. |
| S13 | 157 grids | PASS | 04:02Z. The 6 configs (`61711823…`, `3af4102c…`, `3ff4102c…`, `42f4102c…`, `583a2a33…`, `403e5d37…`) are all inline, and none has attributes outside its allow-list. testuser1: config read **200** ×6; the configured fetch **200** ×6 (20 / 141 / 8 / 0 / 59 / 0 rows). |

### Records created and cleaned up

Everything was deleted between 04:09:54Z and 04:10:22Z (each DELETE 204, each read-back 404):
- ledger rows `2769360c…`, `2c69360c…`, `e302dd4c…`;
- grants `2564f7fe…`, `2764f7fe…`, `2969360c…`, `be02dd4c…`;
- entries E1 and E2;
- membership `bd3314f9…`;
- matters `11cd7209…`, `2364f7fe…`, `1e64f7fe…`;
- project `076e033b…`;
- contact `39ce3d06…`;
- org `bb3314f9…`.

The final sweep found:
- 0 `zz-b4sweep*` matters, projects, contacts, organizations and entries;
- 0 ledger rows since 04:00Z;
- active grants for `ac6d7b68…` at 2, as before.

No existing record was modified. testuser1's linked contact was deliberately NOT deactivated, because the identity-link job writes every 5 minutes on dev.

**SPE container created (empty), for `Remove-TestContainers.ps1`:** P143
`b!WIEh0AfQbkSHzeuucOCsydNtZCRkudVMkCm7XnMdUkAEyFUFBjlmQJQzNXlrHp-y` (orphaned by the delete, #1445).

## Defects

None found. Every earlier batch-4 defect is fixed live (#1319, #1320, #1322, #1327, #1328, #1332) or filed: #1313, #1317, #1318, #1324–#1326, #1351, #1352, #1435–#1439, #1443–#1445. All are still open. No issue was filed in this sweep.
