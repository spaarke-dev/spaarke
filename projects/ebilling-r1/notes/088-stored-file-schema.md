# Task 088 - Stored File schema, submission marker, invoice status Superseded (steps 1-5 done; 6-11 wait)

Status 2026-10-22: **the script and its Pester tests, the ParentLineage.cs marker refusal and its tests, and the review fixes are written. `-WhatIf` ran (reads only). NOTHING was written to Dataverse.** Branch `task/ebill-088-stored-file`, from `origin/work/ebilling-r1` at `9985d9f07e` (it contains 080's schema script fix `11ead6c938`).

Steps 6 (the before-notices to ontology-r1 and uac-r2, ontology-r1's PASS and the owner's OK), 7 (080's real run is recorded in `notes/080-procedure-schema.md`), 8 (the real run and the second run), 9 (the verification), 10 (the after-notices and the `docs/data-model/sprk_invoice.md` Status section) and 11 (TASK-INDEX) are left for the main session. The `sprk_invoice.md` update is listed as an output, but step 10 places it after the live run, so it is not in this branch.

Script: `scripts/ebilling/Deploy-EbillingStoredFileSchema.ps1`. Tests: `tests/scripts/Deploy-EbillingStoredFileSchema.Tests.ps1` (Pester 3.4, 35 tests, no live call).

Live run (main session, after `-WhatIf` and the gates):

```
pwsh scripts/ebilling/Deploy-EbillingStoredFileSchema.ps1 -WhatIf
pwsh scripts/ebilling/Deploy-EbillingStoredFileSchema.ps1
pwsh scripts/ebilling/Deploy-EbillingStoredFileSchema.ps1          # second run: every item skipped, nothing published
```

Exit codes: 0 = done; 2 = escalation (nothing further changed; owner decision); 1 = error (re-runnable). On exit 1 or 2 after a write, the script still publishes what it changed (`Publish-AfterStop`).

## 1. Step 1 - read-only dev checks (2026-10-22, GET only, `https://spaarkedev1.crm.dynamics.com`)

| Check | Result |
|---|---|
| Global choice `sprk_storedfilepurpose` | absent |
| Table `sprk_storedfile` | absent |
| `sprk_document.sprk_restrictedbysubmission` | absent; 0 field-permission rows name it |
| The six planned relationship names | all free |
| `sprk_invoice` statuscode | exactly the seven values of task 012: 1 In Review/s0, 2 Cancelled/s1, 659490000 Approved/s0, 659490001 Sent to AP/s0, 659490002 Paid/s1, 659490003 Rejected/s1, 659490004 Returned to Firm/s1. 659490005 is free |
| Invoices at 659490005 | 0 |
| Task 080 | has run: `sprk_procedureversion` exists with its columns (the script refuses, exit 2, otherwise) |
| Reader profile "Spaarke BFF-Managed Field Readers" | exists; on all 6 business-unit default teams (Secure Record, Spaarke Business Unit 1, Spaarke Demo, Spaarke Dev 1, Spaarke Test 1, Spaarke) |
| Writer profile "Spaarke BFF-Managed Field Writers" | exists; holds the BFF application users `# mi-bff-api-dev` and `SDAP-BFF-SPE-API` only |

**Which identity creates the .eml Document (`/office/save`)**: the BFF application identity. `OfficeDocumentPersistence.cs:268` ("this create is app-only") and `:283` (`CreateDocumentAsync`), `UploadFinalizationWorker.cs:650` (the worker path). Consequence for task 095: the marker's writer row is the BFF application user, which is the identity that writes the document, so the submission can set the marker server-side. The escalation trigger (a user-identity create) does not fire.

## 2. What the script creates (item list, delete behaviours, field security)

**F1** global choice `sprk_storedfilepurpose`: 100000000 Procedure source, 100000001 Invoice PDF, 100000002 Invoice supporting file, 100000003 Export file.

**F2** table `sprk_storedfile`: user-owned, audited, primary `sprk_name` (file name, 400, ApplicationRequired; set after the create because Dataverse ignores both in the create body).

| Lookup | Target | Relationship | Delete | Assign / Share / Unshare / Reparent |
|---|---|---|---|---|
| `sprk_procedureversion` | `sprk_procedureversion` | `sprk_storedfile_procedureversion_procedureversion` | RemoveLink | NoCascade |
| `sprk_invoice` | `sprk_invoice` | `sprk_storedfile_invoice_invoice` | RemoveLink | NoCascade |
| `sprk_billingexport` | `sprk_billingexport` | `sprk_storedfile_billingexport_billingexport` | RemoveLink | NoCascade |
| `sprk_sourcedocument` | `sprk_document` | `sprk_storedfile_sourcedocument_document` | Restrict | NoCascade |
| `sprk_sourcecommunication` | `sprk_communication` | `sprk_storedfile_sourcecommunication_communication` | Restrict | NoCascade |
| `sprk_createdbyperson` | `systemuser` | `sprk_systemuser_sprk_storedfile_createdbyperson` | RemoveLink | NoCascade |

No relationship is parental. Other columns: `sprk_purpose` (F1 choice), `sprk_firmvisible` (two options, default No), `sprk_filestatus` (local choice 100000000 Not yet servable, 100000001 Servable, 100000002 Removed), `sprk_sourceattachmentidentity` (1000), `sprk_sourceversion` (text 20; ONE canonical string form of the source row version), `sprk_sourcesha256` (64), `sprk_sha256` (64), `sprk_contenttype` (255), `sprk_sizebytes` (whole number 0 to 2147483647), `sprk_blobpath` (1024).

**F3** field security, 16 columns, reusing uac-r2's profile pair (permission rows only; members are never added or removed):
- writer "Spaarke BFF-Managed Field Writers": read 4, create 4, update 4;
- reader "Spaarke BFF-Managed Field Readers": read 4 only;
- every F2 column except `sprk_name` and `sprk_createdbyperson` (task 086 secures the creator), plus the marker;
- `sprk_blobpath` is server-only: the writer row only, NO reader row.

**F4** `sprk_document.sprk_restrictedbysubmission` (two options, default No), secured immediately after its create.

**F5** `sprk_invoice` statuscode `Superseded` = 659490005 at statecode 1 (Inactive). The script inserts the value only: no invoice is set to it and no code constant is added (097 owns both).

**Publish**: entity-scoped `PublishXml` of `sprk_storedfile`, `sprk_document`, `sprk_invoice` and the option set `sprk_storedfilepurpose`. Never `PublishAllXml`.

**Solution**: components go into SpaarkeMaster through the `MSCRM.SolutionUniqueName` header, as in 080. The repo's `src/dataverse/solutions/SpaarkeMaster/**` is not touched (079 syncs it).

### -WhatIf output (2026-10-22, after the review fixes; exit 0)

```
0. Pre-flight (read-only)
  sprk_procedureversion exists (task 080 has run)
  global choice sprk_storedfilepurpose: absent
  table sprk_storedfile: absent
  sprk_document.sprk_restrictedbysubmission: absent
  sprk_invoice statuscode: 1=In Review/s0, 2=Cancelled/s1, 659490000=Approved/s0, 659490001=Sent to AP/s0, 659490002=Paid/s1, 659490003=Rejected/s1, 659490004=Returned to Firm/s1
  Superseded 659490005 at statecode 1: free (will be inserted)
  no existing item conflicts
  field security profiles: OK (reader on the 6 default teams; writer holds '# mi-bff-api-dev' and 'SDAP-BFF-SPE-API')
  lookup plan (6 lookups) and field security plan (16 columns) as in the tables above
1. F1  WOULD CREATE global option set sprk_storedfilepurpose (100000000=Procedure source, ..., 100000003=Export file)
2. F2  WOULD CREATE table sprk_storedfile (user-owned)
       WOULD SET sprk_name max length -> 400 and required level -> ApplicationRequired after the create
       WOULD SET auditing on sprk_storedfile
       WOULD CREATE the 6 lookups and the 10 other columns
       securable check of the sprk_storedfile F3 columns (before the marker exists)
3. F4  WOULD CREATE sprk_restrictedbysubmission (Boolean)
       WOULD SECURE sprk_document.sprk_restrictedbysubmission after it is created, then grant: Writers (4/4/4); Readers (4/0/0)
4. F5  WOULD INSERT 'Superseded' = 659490005 (statecode 1)
5. F3  WOULD SECURE each of the 15 sprk_storedfile columns, then grant Writers (4/4/4) + Readers (4/0/0);
       sprk_blobpath: Writers only (server-only: NO reader row)
Publish  WhatIf: publish skipped
created : 51   (option set, table, 16 columns + the marker, statuscode 659490005, 31 field-permission rows)
changed : 16   (16 columns secured)
skipped : 0
```

The full output is in the session scratchpad (`088-whatif.txt`); the main session re-runs `-WhatIf` for the before-notices.

## 3. Code: the marker in the app-only write refusal (step 3)

`ParentLineage.cs`: a new constant, `SubmissionMarkerColumn = "sprk_restrictedbysubmission"`. `RefuseAppOnlyWrite` now refuses an app-only generic write that names it on `sprk_document`, with the same reason code (`sdap.access.document_access_server_only`) and its own message ("only the invoice submission writes it, with the narrowing"). The callers (FieldMappingEndpoints.cs:1054 push, UpdateRecordActionCore.cs:151, DataverseUpdateHandler.cs:67) are unchanged.

Tests:
- `DocumentAccessPermissionAppOnlyWriterTests`
  - must-fire: a playbook update naming the marker is refused and writes nothing;
  - must-fire: an output mapping naming the marker is refused;
  - must-not-fire: the marker on `sprk_todo` is written;
  - must-not-fire: a document payload without an access column or the marker is written.
- `DocumentAccessPermissionPushTargetTests`
  - must-fire: a push rule targeting the marker is Skipped and the rest of the push applies;
  - each branch's message is asserted.

**Test-scope justification** (step 3 allows two must-fire cases): the extra must-fire row covers the second app-only writer (output mapping), because the refusal is wired separately at each of the three callers.

**Seeding proof** (step 4, 2026-10-22): with the marker removed from the predicate, 3 rows fail and 72 pass. The failing rows are PushTarget(target: marker), AnOutputMapping(column: marker) and APlaybookUpdate(column: marker, "true"). Restored: 75/75 pass. The 72 earlier tests are unchanged.

## 4. Build, tests, publish size (step 5)

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- `Sprk.Bff.Api.Tests`, filtered to ChildAccessPermission, ParentLineage, FieldMapping, UpdateRecord and DataverseUpdateHandler: **532 passed, 0 failed**.
- ArchTests: 1349/1349 passed (on the final tree, after the fix round).
- Pester 3.4: 088 **35/35**; 080 85/85 (shared module unchanged).
- `dotnet list package --vulnerable --include-transitive` (Sprk.Bff.Api): no vulnerable packages.

**Publish size** (bff-hygiene item 4; 2026-10-22):
- Trees: fresh trees from `git archive`, extracted to short paths (`C:\wt088sm` master, `C:\wt088sb` base, `C:\wt088sx` branch).
- Build: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`, framework-dependent per the csproj, PDBs included.
- Zip: `Compress-Archive -CompressionLevel Optimal`.
- File counts: 192 on every side.

| Side | Commit | Zip |
|---|---|---|
| master | `802c2c14fb` | 37.70 MB |
| base (`origin/work/ebilling-r1`) | `9985d9f07e` | 38.29 MB |
| branch | `620fbb412f` | 38.29 MB |

- Delta branch - base = **0.000 MB**. The fix round after `620fbb412f` changed only a doc comment under `src/`.
- Delta branch - master = +0.582 MB. This is the whole ebilling-r1 branch, not task 088.
- No package was added.

## 5. Deviations and decisions

1. **Forms and views.** The script authors no form, view or subgrid. Creating the table still makes Dataverse add its default system forms and views. The verify step lists them as the platform defaults, and their names are recorded after the live run. Owner question: whether the negative criterion "no form, view or subgrid" is met by "none authored", or whether 079 should remove the defaults.
2. **Profiles.** uac-r2's existing profile pair is reused. The considered `-CreateProfiles` option was dropped: the script never creates a profile or changes its members, and it stops (exit 2) when the preconditions do not hold. uac-r2 still has to agree to this reuse and review the ParentLineage.cs diff (step 6).
3. **Copied helpers.** The global-choice, audit and field-security helpers are copied from 080's script, as the POML prescribes (step 2), rather than moved into `EbillingSchema.psm1`. Recommended follow-up: consolidate them into the module once 080 and 088 have both run.
4. **Securability pre-pass.** Before the marker is created, the script reads `CanBeSecuredForRead/Create/Update` on the 15 `sprk_storedfile` columns. If any is false, it stops (exit 2) with the marker not created and nothing secured. The marker gets its own check, then is secured at once. Reason: on dev, 50 of 53 BigInt columns are unsecurable (platform image timestamps). The 3 securable ones (for example `flowlog.duration`) show that a BigInt can be secured, but a new custom BigInt had not been tried live. Run 1 then showed the pre-pass is not enough for a BigInt (section 8); both BigInt columns were retyped and the early "BigInt first" step was removed. The pre-pass over all 15 columns stays.

## 6. Review (task-execute step 9.5 and the adversarial pass)

Adversarial reviewer (opus, one pass over `9985d9f07e..620fbb412f`, 2026-10-22). Findings and disposition:

| # | Class | Finding | Disposition |
|---|---|---|---|
| 1 | F1 | The marker was created before the securability pre-pass; an unsecurable BigInt would exit 2 with the marker writable by every user | **Fixed**: Stored File pre-pass before F4; marker checked and secured right after its create; step 5 secures only the Stored File columns. Pester pins the order |
| 2 | F4 | Task notes missing | **Fixed**: this file |
| 3 | F4 | "No form, view or subgrid" not verified; platform defaults unrecorded | **Fixed**: verify lists the table's forms and views (read only, Pester-pinned to GETs); deviation 5.1 above |
| 4 | F2 | `$script:Planned` unbound, so the sprk_name 400 / required `-WhatIf` line never printed | **Fixed**: bound; the line prints (section 2) |
| 5 | F2 | Reason-code doc comment omitted the marker; the marker's message branch untested | **Fixed**: comment updated; push test asserts each branch's message |
| 6 | K2 | A failed first run never publishes, and the re-run skips everything (inherited from 080) | **Fixed**: `Publish-AfterStop` in both catch blocks (publish failure prints a manual-publish hint without masking the stop). 080 has the same pattern: reported, not changed here |
| 7 | K4 | Pre-flight checked only the type of an existing item | **Fixed**: existing `sprk_firmvisible` / marker must default No; existing `sprk_filestatus` must be the local choice with the planned values |
| 8 | K4 | Verify compared `sprk_filestatus` labels, not values | **Fixed**: verify compares Value=Label pairs and the local binding |
| 9 | K4 | Securing a BigInt has not run live | Known limit; mitigated by the pre-pass (deviation 5.4). **Occurred in run 1; resolved by the retype, section 8** |
| 10 | K1 | Pre-existing: `DocumentProfileOutputMapper.cs:50-106` writes AI output to `sprk_document` app-only without `RefuseAppOnlyWrite` | Not caused by 088. Same exposure as `sprk_accesspermission`, already accepted in the ArchTest remarks. Needs the output schema to name the column, and a raw string fails the Boolean type check. **Reported to the operator** for a decision |
| 11 | K3 | Pester covers the plan, the F5 decision and text scans; not the pre-flight, grants, skip paths or publish | Known limit; the live second run (step 8) proves idempotency |

Re-checks after the fixes: Pester 35/35; 532 unit tests; BFF build 0 warnings; `-WhatIf` exit 0 (51 created / 16 changed / 0 skipped). The verify's forms/views GETs were tried read-only on dev against `sprk_invoice` (5 forms, 12 views returned), so the filter shape works.

Step 9.5 gates on the fix diff (2026-10-22):
- **code-review:** 0 Critical, 0 Warning. 1 Suggestion (K4), fixed: the escalation line said "nothing further was changed" and then published, so it now says the publish follows.
- **adr-check:**
  - Compliant: ADR-002, ADR-010, ADR-027, ADR-028, ADR-029, ADR-038, ADR-044.
  - ADR-002 detail: no plugin; the marker's invariant has one server-side owner (field security plus `RefuseAppOnlyWrite`, fail closed).
  - ADR-038 detail: tests are in the `data-mutation` KEEP path, with no banned mocks.
  - 0 violations, 0 warnings.

### 6.5 Reader pre-flight: named business units, not every default team (uac-r2 condition, 2026-10-22)

The pre-flight used to require the reader field-security profile on ALL default teams (6 on dev). A field-security reader row grants column read, not row read, and UAC task 191 will add an "External Collaborators" business unit whose default team must NEVER hold the reader profile: the profile must never be the boundary for flagged-external users, and an "every default team" check would fail there or push someone to add the profile. The script now takes `-ReaderBusinessUnits` (default: the six dev BUs whose default teams hold the profile today: Secure Record, Spaarke Business Unit 1, Spaarke Demo, Spaarke Dev 1, Spaarke Test 1, Spaarke) and checks only those. A named BU whose default team lacks the profile, or that has no enabled default team (a misspelt name), stops the run (exit 2, owner decision). Default teams of other BUs are not checked; one INFO line lists them. The script still never adds or removes a profile member. The writer check (only the two BFF app users) is unchanged. Pester pins: a named BU lacking the profile fires; an extra BU (External Collaborators) lacking it does not, and the INFO line names it. `-WhatIf` on dev: exit 0, 51 created / 16 changed / 0 skipped, no INFO line (no extra BU yet).

### 6.6 Pre-run fixes requested by ontology-r1 after the schema-gate PASS (2026-10-22)

**K-a, SchemaName.** `Add-Table` (in `EbillingSchema.psm1`) gained optional `-SchemaName` / `-PrimarySchema`, defaulting to the logical names, so every other script's body is unchanged and 080's script is untouched. This script now sends table SchemaName `sprk_StoredFile` and primary column SchemaName `sprk_Name` (neither can change after the create); the module refuses a SchemaName that does not lowercase to the logical name. The other eleven column SchemaNames and the six lookup SchemaNames were already PascalCase after the prefix. Logical names are unchanged everywhere, and the pre-flight "name is free" checks and the verify still compare logical names (`LogicalName=` in every read). **K-b, Related menu.** `Add-Lookup` gained optional `-MenuBehavior`; with it the relationship body carries `AssociatedMenuConfiguration = { Behavior, Group = 'Details', Order = 10000 }` (the shape `Add-ManyToMany` already sends). All six lookups pass `DoNotDisplay`, so none adds a "Stored Files" entry to the Related menu of its target form; the verify reads `AssociatedMenuConfiguration.Behavior` back on each relationship and fails on anything else (an existing relationship is not changed, only reported). **K-c, publish.** The scoped `PublishXml` now names `sprk_storedfile`, `sprk_document`, `sprk_invoice`, `sprk_communication`, `sprk_procedureversion`, `sprk_billingexport`, `systemuser` and the `sprk_storedfilepurpose` choice; still no `PublishAllXml`. **Item 4 (done).** `sprk_sourceversion` and `sprk_sizebytes` (the two BigInt columns) are created right after the table and its audit, and their `CanBeSecuredFor*` flags are checked at once, before any lookup or other column. If the platform refuses to secure that type, the run stops (exit 2) with only `sprk_storedfile` and those two columns to remove by hand (the table holds no rows; delete it and re-run). The full securable pass over all fifteen columns before the marker, and "nothing secured before it", are unchanged. Tests: 59 Pester (088) and 85 (080) pass; the module's create bodies are captured with `Invoke-Dv` mocked inside the module.

## 7. Open questions for the owner and the peers

1. Forms and views: is "none authored, platform defaults only" acceptable (5.1)?
2. uac-r2: agreement on reusing its profile pair, and its review of the ParentLineage.cs diff (step 6).
3. ~~Live run: watch the BigInt securability check.~~ Answered by run 1 (section 8): the owner chose a String and a whole number.
4. `DocumentProfileOutputMapper` app-only writer (K10): whether to add `RefuseAppOnlyWrite` there (affects the access columns too).
5. Consolidating the helpers copied from 080 into `EbillingSchema.psm1`.

## 8. Run 1 and the retype (2026-10-22)

**The failure.** Run 1 of `scripts/ebilling/Deploy-EbillingStoredFileSchema.ps1` on dev stopped with exit 1 on the first BigInt column. Field security on `sprk_storedfile.sprk_sourceversion` (BigInt) was accepted (`IsSecured` PUT succeeded), but the field-permission service refused the permission row: `POST fieldpermissions` returned HTTP 400 `0x8004f508` "FieldPermissionService ValidatePermission failed on Create for attribute sprk_sourceversion ... because sprk_sourceversion is NOT secured for entity fieldpermission", on all 12 retries, even though `CanBeSecuredForRead/Create/Update` read true on the column. The script's revert path took the column back to not secured, so no reader was left masked.

**Lesson.** `CanBeSecuredFor*` is not enough for a BigInt column: the flags read true, and the securability pre-pass (deviation 5.4) passed, yet the field-permission service would not accept a row for it. Only the permission-row POST proves a column type can carry field security. For a new column type on a secured table, try the type live on a throwaway column (secure it and grant) before it goes in a plan, or use a type already secured in this project (String, Integer, Boolean, Picklist, Lookup).

**Owner decision, 2026-10-22.** Retype both BigInt columns: `sprk_sourceversion` becomes a String, max length 20 (Text format); `sprk_sizebytes` becomes a whole number (Integer), 0 to 2147483647. SchemaNames stay `sprk_SourceVersion` and `sprk_SizeBytes`. The same type is already used for `sprk_invoicecorrection.sprk_sourceversion` (text 20).

**The delete (the main session's record, verbatim).** Before the delete: 0 rows, 0 fieldpermission rows on the two columns, both IsSecured=false. DELETE of both attributes succeeded (the platform refuses a delete that has dependencies). Published `sprk_storedfile`. 0 remaining. `RetrieveDependenciesForDelete` was not run separately before the delete.

**State of dev after the delete and before run 2.**
- Created: the option set `sprk_storedfilepurpose`; the table `sprk_storedfile` with every column except the two, and all 6 lookups; the marker `sprk_document.sprk_restrictedbysubmission` (secured, writer and reader rows); invoice statuscode Superseded 659490005.
- Secured and granted (9 `sprk_storedfile` columns, each with Readers read-only, Writers read/create/update and the platform's own System Administrator row): `sprk_procedureversion`, `sprk_invoice`, `sprk_billingexport`, `sprk_purpose`, `sprk_firmvisible`, `sprk_filestatus`, `sprk_sourcedocument`, `sprk_sourcecommunication`, `sprk_sourceattachmentidentity`.
- Not secured: `sprk_sourcesha256`, `sprk_sha256`, `sprk_contenttype`, `sprk_blobpath`. `sprk_sourceversion` and `sprk_sizebytes` do not exist.

**Script changes.**
- `sprk_sourceversion` is `New-StringDef ... 20`; `sprk_sizebytes` is `New-WholeNumberDef ... 0 2147483647` (copied from `Deploy-EbillingProcedureSchema.ps1`). `New-BigIntDef`, `$BigIntColumns` and the "BigInt first" creation and securable step are gone; both columns are created in the normal column order.
- Unchanged: the securability pre-pass over all 15 `sprk_storedfile` columns before the marker is created; the pre-flight existing-column type check. A planned column that already exists with another type (for example a BigInt still resolving because the delete is still processing) stops the run with exit 2 before any write: "sprk_storedfile.sprk_sizebytes exists as BigInt, expected Integer".
- The verify now also reads each Integer column back (range 0 to 2147483647); String length 20 is read by the existing length check.

**Run 2 plan (the re-run is idempotent against the partial state).** Everything present is skipped (option set, table, name length / required / audit, 6 lookups, other columns, marker with its rows, Superseded, the 9 secured columns with their rows). Created: `sprk_sourceversion` (String) and `sprk_sizebytes` (Integer). Secured and granted: exactly 6 columns, `sprk_sourceversion`, `sprk_sourcesha256`, `sprk_sha256`, `sprk_contenttype`, `sprk_sizebytes`, `sprk_blobpath` (11 permission rows: writer on all six, reader on five; `sprk_blobpath` has the writer row only). End state: 15 secured `sprk_storedfile` columns (every F2 column except `sprk_name` and `sprk_createdbyperson`) plus the marker. The System Administrator row the platform adds to a secured column is expected; `Test-OtherProfiles` already exempts it.

`-WhatIf` against dev (read-only GETs, exit 0): created 13 (the 2 columns and 11 permission rows), changed 6 (the 6 `secure` items), skipped 31. Output: `C:/Users/RALPHS~1/AppData/Local/Temp/claude/c--code-files-spaarke-wt-ebilling-r1/e7feb8db-bd91-431a-995c-acf68e28cb28/scratchpad/088-whatif2.txt`.

**Tests.** `tests/scripts/Deploy-EbillingStoredFileSchema.Tests.ps1` (74 pass). The partial state is proved by running the script in a child process against a fake Dataverse (`tests/scripts/fixtures/Invoke-StoredFileSchemaAgainstFake.ps1`) that answers GETs from the state above (including `sprk_filestatus` secured and the System Administrator rows) and records any non-GET: the planned actions are asserted line by line. Must-fire: an existing BigInt `sprk_sizebytes` (or `sprk_sourceversion`) gives exit 2 with no write.

**Known limits.**
- **A file larger than 2,147,483,647 bytes (2 GiB) cannot be stored.** `sprk_sizebytes` is capped there; task 090 refuses such a file (fail closed, see its constraints).
- `RetrieveDependenciesForDelete` was not run before the delete; the platform's own refusal of a delete with dependencies was the guard.

## 9. Run 2 and the idempotence run on dev (2026-10-22)

Before-notices went to ontology-r1 and uac-r2 on 2026-10-22. uac-r2 accepted the ParentLineage diff and the profile reuse, on the condition that the reader pre-flight checks a named set of business units (§6.5). ontology-r1 passed the schema gate, then the pre-run fixes (§6.6), then the retype (44c1ef6a2d..5af7f6c879). The owner's OK covers dev schema runs of 080/115/088, and the retype was approved separately on 2026-10-22. 080 had already run on 2026-10-21 (notes/080-dev-run-2026-10-21.md).

- **Run 2** (5af7f6c879), exit 0. Created 13: the two retyped columns and 11 field-permission rows. Changed 6: secured sourceversion, sourcesha256, sha256, contenttype, sizebytes and blobpath. Skipped 31. Verify passed: "all 18 expected columns present with the right type and target; every check above passed".
- **Run 3**, exit 0. Created 0, changed 0, skipped 39. Output: "nothing changed - no publish" and "Idempotent: nothing was created or changed."
- **Verify results:**
  - 15 columns secured. Each has a writer row (R/C/U). 14 also have a reader row (R); blobpath has none.
  - The marker is secured and no document holds Yes.
  - Superseded 659490005 is at statecode 1 and no invoice holds it.
  - All 6 relationships are NoCascade except on delete, and each has its Related menu set to DoNotDisplay. On delete, sourcedocument and sourcecommunication are Restrict and the other four are RemoveLink.
  - The table has only the platform-default forms (3) and views (7).
- **Independent read-backs:** uac-r2 and ontology-r1 each did one on 2026-10-22, and both match the verify item for item. ontology-r1 logged it in its deploy-log.
- **Publish size:** unchanged; see §4.
- **Logs:** in the session scratchpad (088-run1.txt, 088-run2.txt, 088-run3.txt). The run 2 summary and verify are quoted in the after-notices.

