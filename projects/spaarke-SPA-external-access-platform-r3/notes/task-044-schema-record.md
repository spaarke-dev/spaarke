# Task 044 — workforce-home identity schema: dev record

> Environment: `https://spaarkedev1.crm.dynamics.com` (org `spaarkedev1`), solution `SpaarkeCore`. Date: 2026-10-14.
> Script: `scripts/Set-WorkforceHomeIdentitySchema.ps1` (commit `5cdc88216`). No secret or token appears below. The two IDs are the BFF application (client) IDs, which are not secrets.

## Approvals

| Step | Owner approval |
|---|---|
| Dry run | 2026-10-14 ("Yes, run the dry run") |
| Apply | 2026-10-14 ("1. 044 schema apply"), together with the stated rollback |

## Commands run (in order)

```
# 1. Dry run (read-only) - clean
pwsh -NoProfile -File scripts/Set-WorkforceHomeIdentitySchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c

# 2. Apply (owner-approved)
pwsh -NoProfile -File scripts/Set-WorkforceHomeIdentitySchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Apply

# 3. Verify (read-only)
pwsh -NoProfile -File scripts/Set-WorkforceHomeIdentitySchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify

# 4. Re-run as a dry run (read-only; idempotency evidence)
pwsh -NoProfile -File scripts/Set-WorkforceHomeIdentitySchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c
```

## Results

### Apply: every write succeeded; exit 1 from two false FAIL rows

The apply created and read back every item: the option, the five columns, the key (reached Active after about 7 polls), the field security and the solution membership. It then published contact, systemuser and `sprk_identityplane`.

It exited 1 because of two rows:

```
DONE     created systemuser.sprk_homeobjectid (read back: Text, MaxLength )
FAIL     systemuser.sprk_homeobjectid exists but is not a text column - this script never alters an existing column
DONE     created systemuser.sprk_homeobjectidkey (read back: Text, MaxLength )
FAIL     systemuser.sprk_homeobjectidkey exists but is not a text column - this script never alters an existing column
```

**Cause: a race in the script, not in dev.**
- After a create, `Wait-DvCondition { Get-ColumnState ... }` waits only until the base attribute row is readable.
- The typed `.../Microsoft.Dynamics.CRM.StringAttributeMetadata` read can lag behind it.
- So `IsString` came back false and `MaxLength` came back empty.

Three facts show the columns are correct text columns:
- The alternate key built on `sprk_homeobjectidkey` and reached Active. Dataverse keys only text, number and similar columns, not an untyped one.
- Field security applied to `sprk_homeobjectid`.
- Step 3 read both columns as Text(36).

Fix: the post-create wait now also requires `IsString` (see "Script fix" below).

### Verify: PASS (exit 0)

```
(a) OK  option 100000002 'WorkforceHome' on sprk_identityplane   (100000000 and 100000001 unchanged)
(b) OK  contact.sprk_externaltenantid
    OK  systemuser.sprk_hometenantid
    OK  systemuser.sprk_homeobjectid
    OK  systemuser.sprk_hometenantidkey
    OK  systemuser.sprk_homeobjectidkey          (all Text, MaxLength 36; mirrors not secured)
(c) OK  sprk_HomeIdentityUniqueKey is Active      (systemuser: sprk_hometenantidkey, sprk_homeobjectidkey)
(d) OK  contact.sprk_externaltenantid, systemuser.sprk_hometenantid, systemuser.sprk_homeobjectid field-secured
    OK  Spaarke Identity Link Readers + Writers permissions on each of the three
    OK  Writers members: app users '# mi-bff-api-dev', 'SDAP-BFF-SPE-API'
    OK  Readers members: default teams of the six business units
(e) OK  all five columns, the key, the choice and both profiles are in SpaarkeCore
VERIFY PASS: the workforce-home identity schema is complete.
```

### Re-run (dry run): no change

The re-run printed 39 `OK` rows and 0 `WOULD`, `FAIL` or `MISSING` rows, then "DRY RUN complete - nothing was written". Exit 0.

## Acceptance criteria

| Criterion | Evidence |
|---|---|
| WorkforceHome = 100000002 in XML and on dev; existing values unchanged | `OptionSets/sprk_identityplane.xml`; verify (a) |
| contact.sprk_externaltenantid text(36), secured like sprk_externalobjectid | `Entities/Contact/Entity.xml`; verify (b) and (d) |
| systemuser secured pair, unsecured mirrors, key Active | `Entities/SystemUser/Entity.xml`; verify (b), (c) and (d) |
| Re-running changes nothing | Re-run dry run: 0 WOULD |
| No apply before approval; commands and rollback listed; no secrets | This record |

## Rollback (not run)

```
pwsh -NoProfile -File scripts/Set-WorkforceHomeIdentitySchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Rollback          # dry run first
pwsh -NoProfile -File scripts/Set-WorkforceHomeIdentitySchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Rollback -Apply
```

The rollback removes, in order: the field-permission rows, the key, the five columns, then the option. It refuses and writes nothing if any row holds a value in those columns, or if any contact uses plane WorkforceHome. Nothing writes them until task 046 ships.

## Script fix (post-apply)

In `Set-WorkforceHomeIdentitySchema.ps1` step (b), the post-create wait now returns only once the column reads back as a string column, not merely once it exists. This removes the false FAIL seen above. It changes no write and no read-only mode.
