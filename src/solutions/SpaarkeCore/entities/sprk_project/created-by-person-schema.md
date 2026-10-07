# `sprk_createdbyperson` — Created By (Person), on the three secure roots

> **Tables**: `sprk_project`, `sprk_matter`, `sprk_workassignment`
> **Created**: 2026-10-02 · **Project**: `unified-access-control-r2` task 133 (#1054) · **Decision**: owner round 7 item 2, option (a)
> **Script**: [`scripts/Set-RecordCreatorPersonSchema.ps1`](../../../../../scripts/Set-RecordCreatorPersonSchema.ps1) (dry run by default; `-Apply`; `-Verify`)
> **Code**: `src/server/api/Sprk.Bff.Api/Services/Dataverse/RecordCreatorPerson.cs` (name, target and tables pinned against the
> script by `RecordCreatorPersonSchemaAgreementTests`)
> **Live state**: ⏳ NOT yet applied in any environment — a pending manual gate (see "Deployment" below).

---

## Why it exists

`createdby` is the identity that SENT the create. Three BFF paths create these tables **app-only**, so for them `createdby`
is the BFF application user, not the person who asked for the record, and `createdonbehalfby` is empty (verified live
2026-10-01):

| Path | Table(s) | Who `createdby` is |
|---|---|---|
| Office quick-create (`RecordCreationService`) | `sprk_matter`, `sprk_project` | BFF application user |
| `POST /api/v1/work-assignments` (`WorkAssignmentEndpoints`) | `sprk_workassignment` | BFF application user |
| Chat `dataverse.create_record` (`DataverseCreateRecordHandler`, user-OBO) | any, incl. the three | the user |

Secure provisioning's **resume** (`ProvisionProjectEndpoint`) shares a stranded secure record to the person who created it.
For an app-created row it had nobody to share to and could only refuse. This column records that person, persisted.

## Column

| Property | Value |
|---|---|
| Logical name | `sprk_createdbyperson` (schema `sprk_CreatedByPerson`) |
| Display name | Created By (Person) |
| Type | Lookup → `systemuser` |
| Web API read form | `_sprk_createdbyperson_value` |
| Required | None |
| Relationship | `sprk_systemuser_<table>_createdbyperson` (1:N from `systemuser`) |
| Cascade | Assign / Share / Unshare / Reparent / Merge: **NoCascade**; Delete: **RemoveLink** (a user is disabled, never deleted — and nothing about the user may move, share or reparent the record) |
| Field security | **Secured** — see below |

## Who writes it — the BFF, and only the BFF

| Writer | Value | How |
|---|---|---|
| `RecordCreationService` (Office matter / project) | the Office caller's `systemuserid` | in the app-only create payload, set last (also protected from field mapping) |
| `WorkAssignmentEndpoints` | the caller, resolved by WhoAmI over OBO (never the request body) | in the app-only create payload; a caller who cannot be resolved is refused 403 `sdap.workassignment.creator_unresolved` before the create |
| `DataverseCreateRecordHandler` | the row's own `createdby` (the OBO caller, as Dataverse recorded them) | an app-only update right after the user's create; non-fatal (the row's `createdby` is that same person). Task 146 moves the create itself to the app and stamps in that payload |

An AI create item that names the column (any casing, its read form, or a bind) is refused before any Dataverse call. A
record created OUTSIDE the BFF (the model-driven app, a client `Xrm.WebApi` create) has no value here — its `createdby` is
the person, which the resume reads first.

**Field-level security** (the lock):

| Profile | Members | Permission on the column |
|---|---|---|
| Spaarke BFF-Managed Field Readers | every business unit's default team | read |
| Spaarke BFF-Managed Field Writers | the BFF application user(s), explicitly | read, create, update |

Any other profile (besides the platform's System Administrator profile) that can create or update the column is reported
`FAIL` by `-Verify` — and so is any member of the WRITER profile other than the `-BffApplicationIds` users (a human, another
application user, or any team): that membership IS the lock (task 133 r1). Both are reported in every mode, never removed. The two profiles are named for the CLASS of column — task 150 locks `sprk_issecure` the same way and
adds it to them. **A business unit created later** needs `-Apply` re-run, so its default team joins the reader profile.

## Who reads it

`ProvisionProjectEndpoint`, on RESUME only, and only when `createdby` is not a usable person (absent, disabled or an
application user), in a query of its own — never in the Step 1 select. Order (owner round 7 item 2): `createdby` when it is
a usable person → else this column when it names a usable person → else refused (`sdap.provision.resume_creator_unavailable`,
nothing written; never the caller as a substitute — owner decision F8). An unreadable `createdby` stops the decision (500,
the same caller may retry) rather than falling through to the column. A read of this column that Dataverse answers 400 (the
column does not exist in the environment) is `creatorState: column-missing` — deterministic, no retry offered, an
administrator applies the schema; a read Dataverse refuses with 401/403 (the BFF's sign-in or Read privilege) is
`creatorState: refused` — deterministic too, an administrator restores the privilege (owner round 14 item 3); any other
failed read is the transient `unreadable` (task 133 r1).

## Deployment

⚠️ **Order: schema BEFORE the BFF.** A BFF carrying task 133 writes this column on every Office quick-create and every
`POST /api/v1/work-assignments`; Dataverse refuses a create naming a column that does not exist, so those creates fail until
the schema is applied. Provisioning itself tolerates the column's absence (only a resume that needs it reports
`column-missing`).

```powershell
.\scripts\Set-RecordCreatorPersonSchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com `
  -BffApplicationIds <bff-uami-client-id>[,<bff-app-registration-id>]            # dry run: read-only
.\scripts\Set-RecordCreatorPersonSchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com `
  -BffApplicationIds <bff-uami-client-id>[,<bff-app-registration-id>] -Apply
.\scripts\Set-RecordCreatorPersonSchema.ps1 -EnvironmentUrl https://<org>.crm.dynamics.com `
  -BffApplicationIds <bff-uami-client-id>[,<bff-app-registration-id>] -Verify   # must exit 0
```

Dev (spaarkedev1) application ids: BFF managed identity `5967251e-171c-46fe-a6c2-ef843c90309d`, BFF app registration
`1e40baad-e065-4aea-a8d4-4b7ab273458c`.

**No backfill.** Existing app-created rows have no person to backfill (`createdonbehalfby` is empty); the script reports
their count per table (informational). A resume of one of them still refuses, and the administrator recovery in
`docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md` §7a applies.
