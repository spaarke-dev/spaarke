# Task 141 — Live gate run G-1..G-3 (2026-10-02)

> Written to this new file because `notes/task-141-identity-binding.md` does not exist on
> `work/unified-access-control-r2` at `53e09647d`. The B2 sections (§8, §9, §13) live on `task/uac-r2-141-f3`.
> When the task branch merges, fold this into that file as section "Live gate run G-1..G-3 (2026-10-02)".

**Authority:** owner round 4 item 6 (`notes/session27-owner-decisions-and-research.md`): "Yes, can run it". The approval
covers 141's schema, FLS, the acct claim, the workforce tenant setting and the reconciliation job, with each step run
in dry-run and verify mode.
**Scripts used:** the read-only worktree `C:\wt141`, detached at `task/uac-r2-141-f3` @ `743970bae` (the verified B2
version). No file in that worktree was edited.
**Operator identity:** az CLI login `ralph.schroeder@spaarke.com`, tenant `a221a95e-6abc-4434-aecc-e48338a1b2f2`,
subscription "Spaarke Devlopment Environment".
**Target:** Dataverse `https://spaarkedev1.crm.dynamics.com` (org `spaarkedev1`), App Service `spaarke-bff-dev` in
`rg-spaarke-dev`. `az webapp deployment slot list` returned `[]`: there is **no staging slot**.

## Outcome

| Gate | Result |
|---|---|
| **G-1** Schema | **STOPPED — partially applied.** The dry run matched the §8 G-1 row. `-Apply` failed in step (c) on the first picklist column, because of a script defect (below). Three metadata components were created before the failure. |
| **G-2** `acct` claim | **NOT RUN.** The procedure stops at the first unexpected result. The before-state was captured read-only (below). |
| **G-3** Tenant setting | **NOT RUN**, for the same reason. The before-state was captured read-only (below). |

## G-1 — dry run (read-only)

`C:\wt141\scripts\Set-ContactIdentityBindingSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c` → exit 0

```
Platform rules
  OK       no alternate-key column is field-secured (key on contact.sprk_externalobjectidkey; FLS on contact.sprk_externalobjectid)
(a) Uniqueness mirror and stored oids
  WOULD    create contact.sprk_externalobjectidkey (Text 100, NOT field-secured)
  OK       6 contact(s) carry an oid
  WOULD    contact 43c4e819-9496-f111-b8db-0022482fb5a7: copy '631b513c-16de-42d8-b09f-148933726f78' into sprk_externalobjectidkey (was '')
  WOULD    contact 52bb55e7-9d15-f111-8343-7c1e520aa4df: copy 'c5c02604-9f53-4dc7-9548-2214424291e0' into sprk_externalobjectidkey (was '')
  WOULD    contact 8e9918a9-9021-f111-88b5-7c1e520aa4df: copy '6a9fa229-7033-4e7c-abb5-0a8439cdd068' into sprk_externalobjectidkey (was '')
  WOULD    contact 2e419a4f-010d-f111-8342-7ced8d1dc988: copy '06646385-cbbc-4321-a458-f631e0096328' into sprk_externalobjectidkey (was '')
  WOULD    contact 8cb95c16-e974-f111-ab0e-7ced8ddc4a05: copy '6b917a49-feeb-48d6-ad9c-29bad951ad17' into sprk_externalobjectidkey (was '')
  WOULD    contact 394fda9f-ab95-f111-b8dc-7ced8ddc4cc6: copy 'b720e239-7833-4cdd-92f7-b8c134018e1d' into sprk_externalobjectidkey (was '')
(b) Uniqueness key (on the mirror)
  WOULD    create alternate key sprk_ExternalObjectIdUniqueKey on contact(sprk_externalobjectidkey)
(c) Plane and collision columns
  WOULD    create global choice sprk_identityplane (2 options)
  WOULD    create global choice sprk_identitycollisionreason (12 options)
  WOULD    create contact.sprk_identityplane / sprk_identitycollisionon / sprk_identitycollisionoid /
           sprk_identitycollisionplane / sprk_identitycollisionreason / sprk_identitycollisionparties
  WOULD    backfill of 6 bound contact(s) as External (after the column exists)
  WOULD    create view 'Contacts with Identity Collisions' (open identity collisions, newest first)
(d) Field-level security
  WOULD    create profile 'Spaarke Identity Link Readers' / 'Spaarke Identity Link Writers'
  WOULD    Writers member: app user '# mi-bff-api-dev'; app user 'SDAP-BFF-SPE-API'
  WOULD    Readers member: default teams 'Secure Record', 'Spaarke Business Unit 1', 'Spaarke Demo', 'Spaarke Dev 1', 'Spaarke Test 1', 'Spaarke'
  WOULD    secure contact.sprk_externalobjectid (+ Readers and Writers permissions)
  WOULD    secure systemuser.sprk_primarycontact (+ Readers and Writers permissions)
(e) Solution components
  OK       contact.sprk_externalobjectid in SpaarkeCore
  WOULD    add systemuser.sprk_primarycontact to SpaarkeCore
DRY RUN complete — nothing was written.
```

**Comparison with the §8 G-1 row: it matches.** The mirror column, the 6 copies, the key `sprk_ExternalObjectIdUniqueKey`,
2 choices (reason = 12 options), 6 columns, the view, 2 FLS profiles, 2 writer members, 6 BU default teams,
2 secured fields and the backfill of 6 are all present. The dry run also shows (e) solution membership, followed by (f) a
publish of contact and systemuser. The §8 row's summary does not list them, but §4.1 documents both as script steps, and
they touch only the listed components. Judged **not a material difference**, so the run proceeded.

## G-1 — `-Apply` (LIVE) → FAILED in step (c)

Same command with `-Apply`:

```
Mode        : APPLY
Platform rules
  OK       no alternate-key column is field-secured
(a) Uniqueness mirror and stored oids
  DONE     created contact.sprk_externalobjectidkey
  OK       6 contact(s) carry an oid
  WOULD    contact … (all 6 copies)            <- skipped: see defect 2
(b) Uniqueness key (on the mirror)
  WOULD    create alternate key sprk_ExternalObjectIdUniqueKey on contact(sprk_externalobjectidkey)   <- skipped: defect 2
(c) Plane and collision columns
  DONE     created global choice sprk_identityplane
  DONE     created global choice sprk_identitycollisionreason
Invoke-RestMethod: C:\wt141\scripts\Set-ContactIdentityBindingSchema.ps1:172
  { "error": { "code": "0x0", "message": "Guid should contain 32 digits with 4 dashes (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)." } }
```

### Every live write that landed (Dataverse spaarkedev1, metadata only, no record data)

| # | Component | Id | Solution |
|---|---|---|---|
| 1 | Column `contact.sprk_externalobjectidkey` (Text 100, `IsSecured = false`) | MetadataId `3f44ae9f-7abe-f111-a05b-3833c5e9614d` | Default only. Not yet in SpaarkeCore, because step (e) never ran |
| 2 | Global choice `sprk_identityplane` (2 options) | MetadataId `334baea5-7abe-f111-a05b-3833c5e9614d` | Default only |
| 3 | Global choice `sprk_identitycollisionreason` (12 options) | MetadataId `714baea5-7abe-f111-a05b-3833c5e9614d` | Default only |

No contact row was written: the 6 copies were skipped and the backfill was never reached. No key, view, FLS profile,
member, secured flag or publish happened. The deployed dev BFF does not read any of these components (G-4 has not run),
so the partial state has no runtime effect. Nothing was deleted.

### Defect 1 (blocking) — picklist columns bind their global choice by name

At `Set-ContactIdentityBindingSchema.ps1` around lines 330, 339 and 342 of `743970bae`:

```powershell
'GlobalOptionSet@odata.bind' = "/GlobalOptionSetDefinitions(Name='$PlaneOptionSet')"
```

The Dataverse metadata API accepts only the MetadataId in this bind: `/GlobalOptionSetDefinitions(<guid>)`. The dry run
cannot catch this, because it never POSTs, and the §13.1 preflight demo stopped at authentication. **Fix (task branch,
not done here):** look up the choice's `MetadataId` after `Ensure-GlobalOptionSet` and bind
`"/GlobalOptionSetDefinitions($id)"`. The three columns `sprk_identityplane`, `sprk_identitycollisionplane` and
`sprk_identitycollisionreason` need it.

### Defect 2 (non-blocking; self-heals on re-run) — the mirror re-read right after creation returned null

Step (a) re-reads the mirror right after the POST that created it. The new attribute was not yet visible, so
`$mirrorReadable = $false`, and the script's own fallback reported the 6 copies and the key as `WOULD` even under
`-Apply`. The read-only `-Verify` seconds later saw the column (`OK contact.sprk_externalobjectidkey`). A re-run of
`-Apply` would therefore do the copies and the key. Optional hardening: poll the re-read for a few seconds, or print
"deferred: re-run -Apply" in place of `WOULD`.

### Is the script idempotent over the partial state? (read from the code, not executed)

Yes, from reading `743970bae`: (a) sees the existing mirror (`OK`), (c) sees the existing choices (`OK` plus an
option-gap check), and the remaining steps are create-if-missing. Once defect 1 is fixed, a fresh dry run followed by
`-Apply` against this partial state should finish the gate without deleting anything. The dry run expected then: the
same plan as above, minus the three components already created.

## G-1 — `-Verify` after the failure (read-only) → exit 1, 35 gaps

```
(a) OK contact.sprk_externalobjectidkey ; OK 6 contact(s) carry an oid ; FAIL x6 copy '<oid>' into sprk_externalobjectidkey (was '')
(b) MISSING alternate key on contact(sprk_externalobjectidkey)
(c) OK global choice sprk_identityplane ; OK global choice sprk_identitycollisionreason
    MISSING the 6 contact columns, the backfill, the view
(d) MISSING both profiles, 2 writer members, 6 reader team members, both secured flags, 4 permissions
(e) MISSING choice sprk_identityplane / choice sprk_identitycollisionreason / contact.sprk_externalobjectidkey /
    systemuser.sprk_primarycontact in SpaarkeCore ; OK contact.sprk_externalobjectid in SpaarkeCore
VERIFY FAIL (35)
```

The key never got as far as creation, so there was no key build to poll.

## G-2 — NOT RUN. Before-state (read-only)

`az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --query optionalClaims`:

```
accessToken: email, preferred_username, upn   (essential=false, source=null, additionalProperties=[])
idToken: []    saml2Token: []
```

`acct` is absent. When G-2 runs, the after-state must show `acct` **plus** these three claims.

## G-3 — NOT RUN. Before-state (read-only)

`az webapp config appsettings list -g rg-spaarke-dev -n spaarke-bff-dev`: **0** settings named `WorkforceIdentity*`.
There is no staging slot, so G-3 is a single `appsettings set` on the production slot.

## To resume

1. Fix defect 1 on `task/uac-r2-141-f3`. Optionally fix defect 2. Re-verify, then build a new read-only worktree.
2. G-1: dry run (expect the plan above minus the 3 created components) → `-Apply` → `-Verify` until the key is Active.
3. G-2 and G-3 as in §8. Both are independent of G-1, but were held back by the stop-on-first-unexpected rule.

## Completion run (main session, 2026-10-02 ~17:30Z), after the two script fixes

The partial run above stopped on a script defect. The fixes were committed on `integ/uac-r2-batch3`:
- `8e9f85c5e` binds global choices by MetadataId, and the `Wait-DvRead` helper waits for newly created metadata (and for the mirror in data queries);
- `f39402410` retries the field-permission grant on 0x8004f508 while column security propagates.

The completion was run by the main session; no subagent was available (weekly usage limit).

| Gate | Result |
|---|---|
| **G-1 dry run** | It matched the §8 plan less the 3 components already created: 6 copies, the key, 6 columns, the backfill of 6, the view, 2 FLS profiles + 2 writers + 6 reader teams, 2 secured fields, and the solution components. |
| **G-1 -Apply** | **Three runs.** Runs 1 and 2 each stopped on 0x8004f508 at the WRITER grant, after securing a column and granting the readers (FLS propagation; this is what led to the retry fix). Run 3 completed: the writer grant on `systemuser.sprk_primarycontact`, all solution components (incl. the key, both profiles and the view), and the publish. No error was left over: each re-run skipped what was done. |
| **G-1 -Verify** | **`VERIFY PASS: the identity-binding schema is complete.` exit 0.** Key `sprk_ExternalObjectIdUniqueKey` is **Active** on `sprk_externalobjectidkey`. 6/6 oids are mirrored and carry plane External. Both fields are field-secured: readers are the 6 BU default teams, and writers are `# mi-bff-api-dev` and `SDAP-BFF-SPE-API`. Everything is in SpaarkeCore. |
| **G-2** | `Register-EntraAppRegistrations.ps1 -AcctClaimOnly -AcctClaimAppId 1e40baad…` exited 0. `optionalClaims.accessToken` = email, preferred_username, upn, **acct** (before: the first three). |
| **G-3** | `WorkforceIdentity__CustomerTenantIds__0 = a221a95e-6abc-4434-aecc-e48338a1b2f2` on spaarke-bff-dev, read back. There is no staging slot. After the restart, healthz and ping returned 200. |

**Not done yet:**
- **G-1b:** awaiting the owner on the stale "Demo 1" registry row.
- **G-4:** deploy the BFF carrying 141.
- **G-5 / G-6:** the job runs.
- **G-7 / G-8:** manual checks.
- The deployed BFF (`bca0941f6`) does not read the new columns yet. The schema is in place ahead of the deploy, as G-1-before-G-4 requires.

**G-1b DONE (2026-10-02, owner-approved: "yes can deactivate stale registry row if not needed").** The "Demo 1" `sprk_dataverseenvironment` row (`5762061b-ef31-f111-88b5-7ced8d1dc988`, `https://spaarke-demo.crm.dynamics.com`) is set to `sprk_isactive = false` (PATCH 204, read back). It was not needed: the dev BFF's identities are not application users in spaarke-demo, so neither registration nor reconciliation could work there. Reversible: set `sprk_isactive = true` after adding the dev BFF MI as an application user in spaarke-demo.

## G-4 — deploy (2026-10-02 23:4xZ)

PR #1096 merged as `5e1fdcc0b` (merge commit; branch kept). Published from a FRESH worktree at the merge commit
(`C:\wt3d`, 212 files) and deployed with `scripts/Deploy-BffApi.ps1 -SkipBuild`: package 45.66 MB, 4 critical files
SHA-256 verified, `/healthz` 200, CORS OK. The dev BFF now runs batches 1-3 (it contains master `93634db58`).

## G-5 — report-only run: PASS (2026-10-02 23:42Z)

`IdentityLink__Reconciliation__WritesEnabled` unset. `POST /api/admin/jobs/identity-link-reconciliation/trigger`
with a delegated token (ralph.schroeder@spaarke.com) → 202, run `c3e9d04d-fe76-4227-9091-51f05126ff06`, Succeeded in
3.2 s, `processedItems = 0`. The scheduled 23:45 run repeated it identically. App Insights heartbeat:

`mode=report-only environment=https://spaarkedev1.crm.dynamics.com readability=Readable usersScanned=11 verified=0
linked=0 boundAndLinked=1 createdAndLinked=7 boundLinkedContact=0 flagged=3 flagAlreadyPresent=0 denied=0 failed=0
truncated=False`

| Against notes/task-141-identity-binding.md §0 | Expected | Observed |
|---|---|---|
| Users in scope (accessmode 0/1/2, enabled, not application users) | 11 | 11 |
| Bind | 1 (testuser1 → unbound contact ac6d7b68) | 1: testuser1, contact `ac6d7b68…`, oid bcde7809 |
| Create | 7 (final.test, demo, jake.schroeder, e2e.test2, chelsea.friez, lori.witkin, ralph@spaarke.onmicrosoft.com) | the same 7 |
| Ralph's existing link | kept, flagged | `Flagged` `LinkedContactBoundToDifferentOid`; before-state shows only a flag added; `sprk_primarycontact` untouched |
| Collisions | 3 (incl. Ralph's link) | 3: ralph.schroeder@spaarke.com, eyal.iffergan@spaarke.com (`BoundToDifferentOid`), ralph.schroeder@hotmail.com guest (`BoundToDifferentOid`) |
| Writes | 0 | 0 (report-only; every line carries `mode=report-only`) |
| Provisioning targets | Demo 1 deactivated (G-1b) | no failed environment reported (`environmentStatus=ok`) |

## G-6 — writes enabled: first write run PARTIAL, a live defect found and fixed (2026-10-02 23:45Z)

`IdentityLink__Reconciliation__WritesEnabled=true` set on spaarke-bff-dev (no staging slot exists); the app
restarted at 23:46:20. A trigger at 23:46:19 was still served by the old process (report-only, identical to G-5).
The first run in write mode (23:47:11, correlation `a1621aa3cad941dab0e7dc73eef1fb39`):

`status=partial mode=write usersScanned=11 boundAndLinked=1 createdAndLinked=0 flagged=3 failed=7 flagsKept=3`

- **Written correctly:** testuser1's bind and link (contact `ac6d7b68…` now bound to oid bcde7809, plane Workforce;
  `sprk_primarycontact` set); the three collision flags (contacts `8e9918a9…`, `8cb95c16…`, `2e419a4f…`). Ralph's
  existing link was not touched.
- **All seven creates FAILED** with `sdap.access.deny.contact_create_failed`. App Insights dependencies: each was
  `PATCH /api/data/v9.2/contacts(sprk_externalobjectidkey='<oid>')` → **404**.
- **Root cause (reproduced by hand, read-mostly):** Dataverse answers a create-only upsert on an alternate key
  (`PATCH` on the key URL + `If-None-Match: *`) with **404 `0x80060891`** "A record with the specified key values does
  not exist in contact entity", and creates nothing. Same answer as the operator (System Administrator), so it is the
  platform, not the BFF's identity or FLS. The alternate key itself is fine: `sprk_ExternalObjectIdUniqueKey` on
  `sprk_externalobjectidkey`, `EntityKeyIndexStatus = Active`; a GET by the key answers a normal 404.
- **What works (probe, then deleted):** `POST contacts` with the mirror in the body → 204 + `OData-EntityId`
  (`aa875ce9-bbbe-f111-aaaf-0022482913fc`); a second `POST` with the same mirror value → **412 `0x80060892`** "Entity
  Key External Object ID (unique) violated". The probe contact was deleted (204); 0 probe rows remain. This was one
  temporary dev row, created only to diagnose this approved gate.
- **Why the suites missed it:** `InMemoryContactIdentityStore` modelled the keyed PATCH as a working create-only
  write, and the HTTP shape was pinned only as a path string. The platform's real answer was never exercised; there
  is no Dataverse in CI (owner directive).
- **Impact while deployed:** every contact CREATE by oid fails: the job's creates, and a first sign-in of an
  unlinked workforce user who needs a new contact (refused, fail-closed). Binds, links and flags are unaffected.
- **Fix:** `DataverseContactIdentityStore.BuildCreateRequest` = `POST contacts` with the binding, the mirror and the
  plane in the body; no precondition header. The duplicate comes back as 412 `0x80060892` → `IsDuplicateKey` →
  `KeyConflict`, which the binder already handles as "a racer won, re-read". The in-memory store now answers a
  duplicate mirror with that `KeyConflict`, as the platform does. New tests pin the method, the path, the mirror in
  the body and the live duplicate-fault body; dropping the mirror from the body was seeded and fails two of them.
- **Next:** ship the fix, deploy, then re-run G-6 (run, run again, a third run changes nothing). Writes stay enabled
  meanwhile: each 5-minute run re-fails the same seven creates harmlessly and writes nothing else.

## G-6 — re-run after the fix: PASS (2026-10-03 02:26Z)

#1097 merged as `818840ac6` and was deployed from a fresh worktree (`C:\wt3f`; its tree is identical to the merge
commit). Package 45.66 MB, SHA-256 verified, `/healthz` 200. Writes were still enabled.

| Run | Trigger | Heartbeat |
|---|---|---|
| 1 | ManualAdmin 02:26:03 | `status=ok mode=write verified=1 createdAndLinked=7 flagAlreadyPresent=3 failed=0` (testuser1 now verified; 7 contacts created and linked) |
| 2 | ManualAdmin 02:26:45 | `status=ok mode=write verified=8 createdAndLinked=0 flagAlreadyPresent=3 failed=0`: **changes nothing** |
| 3 | Scheduled 02:30:01 | identical to run 2: **changes nothing** |

**Read back (Dataverse, read-only):** 9 of 11 interactive systemusers now carry `sprk_primarycontact`, exactly the
§6 forecast. The two without a contact are the flagged collisions (eyal.iffergan, and the hotmail guest), which wait
for an operator. Ralph's existing link is kept and flagged. The three flags sit on the view "Contacts with Identity
Collisions". `IdentityLink__Reconciliation__WritesEnabled=true` stays set (owner-approved, round 4 item 6).

**Remaining for 141:** G-7/G-8 (manual, `test.user@demo.spaarke.com` and a non-admin dev user).

