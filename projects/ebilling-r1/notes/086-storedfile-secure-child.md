# 086: invoice Stored Files join the secure family (leaf child of sprk_invoice)

Branch `task/ebill-086-storedfile-secure-child`. Base: `origin/task/ebill-089-assignments` 8f4920ce0c, merged with `origin/work/ebilling-r1` (master uac-194) in its own commit 22b803e1a5. Rigor FULL, opus @ high. The code and tests are done. The live steps are listed below and have **not been run**: the main session runs them (owner EB-D-158). The role row's evidence is `PENDING-LIVE-PROBE`. The only live calls from this task were metadata and count reads.

## What was built

All of it ships in one change, as procedure model 5.2 states.

- **`SecureChildLineage`.** `T("sprk_storedfile", "sprk_storedfiles", ("sprk_invoice", "sprk_invoice"))` has one lineage column.
  - `sprk_invoice` is declared first. Under uac-r2 194b, declaration order becomes the primary-parent order; with one column this is trivially true.
  - These lookups are deliberately left out: `sprk_procedureversion`, `sprk_billingexport`, `sprk_sourcedocument` and `sprk_sourcecommunication`.
  - The table is not an ownership parent and is not in `OWNERSHIP_CHILD_TABLES` (`bffChildWriteAdapter.ts`, unchanged), so `SecureChildNewCommandAgreementTests` holds.
- **`RecordOwnershipResolver.CitationLookups`** gains `(sprk_storedfile, sprk_sourcedocument)` and `(sprk_storedfile, sprk_sourcecommunication)`. This is a fix found in scope (deviation 1).
- **`config/secure-record-owner-role.json`.** A new row: `sprk_storedfile`, `prvReadsprk_StoredFile`, kind child, with a reason (model 5.2 / 9.1) and evidence `PENDING-LIVE-PROBE`. The file now holds 40 tables.
- **Creator person.**
  - `RecordCreatorPerson.StampedChildTables` gains `sprk_storedfile`.
  - `Set-ChildRecordCreatorPersonSchema.ps1` gains it in `$Tables` and in `$SolutionHomes` with `@('SpaarkeMaster')`. Because 088 created the column with the canonical relationship name `sprk_systemuser_sprk_storedfile_createdbyperson`, the script only empty-checks, secures and verifies it, and never creates it.
  - The script's Pester pin gains a new `It` and the simulation's master-only list.
  - `CreatorPersonColumnSecuredGuardTests.SecuredOnlyByDeployScript` gains the table, since it has no Entity.xml.
  - `StoredFileService.BuildRow` keeps its explicit stamp, which the persister needs. `ApplyTo` now stamps as well, writing the same column with the same person.
- **Census (`RecordOwnerAssignmentCensusTests`).**
  - `ChildTables` gains `sprk_storedfile`.
  - New Routed entry `StoredFileService.cs / sprk_storedfile / 1`. `BuildRow` is the one create at HEAD.
  - New PersonBearingWriters entries: `StoredFileService.StageCreateFlipAsync` (RequestedBy) and `StoredFileService.BuildRow` (`RecordCreatorPerson.Stamp`).
  - Scanner fix (deviation 2).
- **Flagged-external guard (`ProcedureRolePrivilegeGuardTests`).** Until uac-r2 191, no source-controlled role may hold any `sprk_storedfile` privilege. Must-fire seed: `prvReadsprk_StoredFile`. Must-not-fire look-alike: `prvReadsprk_storedfileversion`.
- **NFR-05.** `prvReadsprk_StoredFile` was already on 089's separate non-throwing list (`GuardedChildPrivileges`). It is now also derived from the codified set, and `GuardedChildPrivilegeTables` de-duplicates the two. `GuardedPrivileges` (the three roots) is unchanged.
- **Not changed:**
  - `OwnershipParentEntities`;
  - `ExternalModuleRegistry`;
  - `InvoiceFamilyCoreRecordGuard.CoreRecordLookups`;
  - any delete code or delete-Restrict.

## Delete contract (model 5.2, uac-r2 Q1)

`sprk_storedfile` is on uac-r2's **hard-deny** list: a core-record delete never deletes a Stored File, and uac-r2 owns `core-record-delete-design.md`. This task adds no delete code and no delete-Restrict. 088's parent lookups stay RemoveLink.

## Deviations

1. **Source lookups are citations** (fix-now, found in scope). Without them, `SecureChildReconciler.ContextFor` → `RecordOwnershipContext.ParentsOf` read `sprk_sourcedocument` / `sprk_sourcecommunication` as ownership parents, because both `sprk_document` and `sprk_communication` are in `OwnershipParentEntities`.
   - Seeded: with both citations removed, the Unsecure test moved the file to the source document's business-unit team instead of the matter's team.
   - The create paths never named them as parents: `StageCreateFlipAsync` uses `ContentOf(parent)` and the persister uses `ForParents([core])`. So the reconciler disagreed with the create.
   - Model 5.2 says the source lookups are "not access-conferring".
   - This touches a uac-r2 file (`RecordOwnershipResolver.cs`) that 194b also edits. It is two lines in `CitationLookups`.
2. **Census scanner blind spot** (pre-existing, fixed in scope). The scanner resolved a qualified const (`StoredFileSchema.Entity`) by its last segment only, which picked up the first `Entity` const in any file (`"entity"`). So the `sprk_storedfile` create was invisible: before the fix the census passed with no entry.
   - `ScanSiteIndexes` now resolves `Type.Name` in the type it names first (`TypeScopedConsts`), then falls back as before.
   - It has a negative control (`Detector_NegativeControl_QualifiedConstsResolveInTheirType`). Every table in it is a census child table, so each assertion can fire.
   - Seeded: with the qualified branch disabled, the control and the real census both fail.
   - No other hidden child create appeared after the fix: the whole ArchTests suite passes.
3. **Reconciler test location.** It is `tests/integration/data-mutation/ExternalAccess/SecureChildStoredFileLineageTests.cs`, not the POML's `tests/unit/Sprk.Bff.Api.Tests/Services/Access/SecureChildReconcilerStoredFileTests.cs`.
   - It sits next to 089's `SecureChildRecordOverrideLineageTests`, which is an ADR-038 KEEP path.
   - It uses the same `SecureChildShareWorld` harness: the real reconciler and the real resolver over an in-memory Dataverse.
4. **Owner pin test location.** It is `tests/integration/data-mutation/RecordOwnership/StoredFileOwnerPinTests.cs`. It drives the real resolver over the same world and keeps `StoredFileRig`'s blob and Dataverse fakes.
5. **Persister RequestedBy (092's, observed only).** POML 9.1 says the owner is resolved "with RequestedBy = the caller". 092's persister resolves `ForParents([core])` with no RequestedBy and passes the submitter to `BuildRow` as `creatorPersonId`. The owner is the same, because a Secure core resolves to the Secure team either way, and the creator person is the submitter (none for a contact, K6). No change is needed in 086.

## Tests and seeding proofs

| Test | What it pins |
|---|---|
| `SecureChildLineageTests.AnInvoiceStoredFileIsFiledUnderItsInvoiceOnly_AndItsSourcesAreCitations` | Exactly one lookup, `sprk_invoice`, declared first. Not an ownership parent, not reparentable. Sources are citations (must fire); `sprk_invoice` is not, and `sprk_document.sprk_sourcedocument` is not (must not fire). A Procedure-source or export row has no parents. |
| `StoredFileOwnerPinTests` (8 cases) | Service path (`CreateForAppAsync` → `ContentOf(invoice)`) for a Secure matter, project and work assignment: the row is owned by the Secure team and never by the caller's BU team (the caller is in another BU); the creator person is the caller; one resolver call. The persister shape (`ForParents([core])` → `BuildRow`) for the same three cores. An ordinary invoice takes the invoice's team, not the caller's. An unplaceable invoice is refused `OwnerUnresolved` with 0 blob calls, 0 Dataverse writes and one resolver call. |
| `SecureChildStoredFileLineageTests` (3) | Make Secure re-owns the invoice's file; a Procedure-source file citing a document of the same matter keeps its author. Unsecure (flagged, not isolated) moves the file to the matter's team; a source document and communication in another BU are never reached and do not decide the file's team. A Procedure-source file, an export file and an ordinary invoice's file see no owner writes. |
| `ProcedureRolePrivilegeGuardTests` | No source role holds a `sprk_storedfile` privilege (flagged-external users hold the same roles). |
| Pester `Set-ChildRecordCreatorPersonSchema.Tests.ps1` | 44/44 on Pester 5.7.1. Pester 6.2.0, the machine default, fails every test in that file on an `Export-ModuleMember` container error; this is a tooling incompatibility and the file says "Pester 5". |

Seeding proofs (each applied, run and reverted; the reverts were checked with grep):

| Seed | Result |
|---|---|
| Role row renamed | SecureFamilyConsistencyTests fails ("the lineage, the codified role set and the census name the same secure child tables" and its negative control), as do the role-coverage census tests. |
| Lineage entry removed | The reconciler must-fire tests (Secure and Unsecure) fail, and so do `TheLineageMapCoversExactlyTheCodifiedChildTables` and the lineage test. |
| `sprk_sourcedocument` added to the lineage | The lineage test fails. The reconciler still leaves the Procedure-source file alone, because the citation guards it a second time. |
| Citations removed | The Unsecure reconciler test fails (the file goes to the source's BU team), and the lineage test fails. |
| Census count 1 → 2 | "every server create of a child-record table is in the census" fails. |
| `owner.ApplyTo(row)` removed from `BuildRow` | All 7 owner-pin owner cases fail, and so does the existing `BuildRow_SetsEveryAuthorizationColumn`. |
| `sprk_storedfile` removed from the script's `$Tables` | The Pester 086 `It` fails (43/44). |
| Scanner's qualified-const branch disabled | The negative control and the census fail. |

## Gates (local)

All five test projects build with 0 errors. Results:

- **Unit broad filter**, plus `SecureBuRoleDepth`, `SecureRecordIsolationCensus`, `RecordOwnership`, `StoredFile`, `CreatorPerson` and `SecureRecordOwner`: 10,151 passed, 7 skipped, 0 failed.
- **ArchTests:** 1,571 passed, 0 failed.
- **Sprk.Bff.Api.IntegrationTests:** 199 passed, 8 skipped. **Spe.Integration.Tests:** 353 passed, 23 skipped.
- **Pester:** 44/44 (5.7.1).

**Publish size** (root §10 / NFR-01). Each tree was a fresh `git archive` at a short path: `dotnet publish -c Release src/server/api/Sprk.Bff.Api -o out`, framework-dependent linux-x64 (csproj RID), `Compress-Archive -CompressionLevel Optimal`, PDBs included.

| Tree | Compressed |
|---|---|
| master 4ab7c1a9c1 (`C:\wt86m`) | 39,621,551 B (37.79 MB) |
| 086 base 22b803e1a5 (`C:\wt86p`) | 40,539,048 B (38.66 MB) |
| 086 tip (`C:\wt86b`) | 40,539,299 B (38.66 MB) |

- 086's own delta is **+251 B**.
- The branch is +0.88 MB against master. All of that is unmerged ebilling-r1 work, not 086.
- The ceiling is 60 MB.
- No package was added. `dotnet list package --vulnerable --include-transitive`: no vulnerable packages.

**/conflict-check.** The open PRs that touch 086's files are #2360 (`fix/r3-iss-005-combined`, census) and #1911 (`work/ebilling-r1`, role JSON). 086's files auto-merge with #2360. The conflicts `git merge-tree` reports against #2360 (`FieldSecurityProfiles.xml`, `IOptionsDriftTests.cs`, `RouteAuthorizationGuardTests.Ledger.cs`) come from earlier ebilling-r1 work, not from 086.

**092 (`task/ebill-092-submission-files` bbf9cd04e8), checked without merging.** It merges cleanly with 086.

- It adds no `sprk_storedfile` create site. Its new StoredFileService members only stage, read and delete blobs, and `BuildRow`'s signature is unchanged. The census Routed count stays 1.
- The persister's Stored File goes through `BuildRow` and is covered by its UnscannedWriter entry, which 092 extends.
- The owner pin's persister case is the persister's own call shape: `ForParents([core])`, then `BuildRow(..., owner, submitter)`.

**Other consumers of the lineage map.**

- `ParentLineage.ChildFiling` now carries `sprk_storedfile → sprk_invoice`.
- `ChildAccessPermissionReconciler.AboveTheFour` is an upward closure from four tables. A Stored File is filed under nothing, so it is not in it.
- `ExternalMultiRootRule` and `ExternalDataService` use the map only for child tables that an external read serves. `sprk_storedfile` is not in `ExternalModuleRegistry`.
- The share mirror (`SecureChildShareSynchronizer`) now mirrors the root's sharees onto Secure-team-owned Stored Files. This confers nothing while no Spaarke role holds a Stored File privilege, which the source guard above enforces until 191. The BFF serves files by the caller's rights on the parent invoice.

## Live state read (spaarkedev1, 2026-10-27, read only)

- `sprk_storedfile`: entity set `sprk_storedfiles`, OTC 11029, UserOwned.
- `prvReadsprk_StoredFile` id `ef068f7b-6a82-4c37-bf25-95c0129e0b47`.
- `sprk_createdbyperson` exists (it targets systemuser) and is **unsecured**. Relationship: `sprk_systemuser_sprk_storedfile_createdbyperson`.
- Required on create: only `sprk_name` (ApplicationRequired).
- Stored File rows: **0**. The empty-before-securing gate will pass, and the pre-deploy count is 0.
- Roles holding any of the 8 Stored File privileges: Service Reader, Service Writer, Service Deleter, Support User, System Administrator, System Customizer. These are all platform roles.
- Flagged-external users (2):
  - Eyal Iffergan (disabled): roles Spaarke Console User, Basic User and Office Add In User, through the Spaarke team.
  - Ralph Schroeder: Basic User directly, plus the same three through the team.
  - **No role a flagged-external user holds grants a Stored File privilege.**

## Live steps (NOT run; the main session runs them in this order, owner EB-D-158)

1. **Refusal probe, before -Apply.** `POST https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_storedfiles` with:

   ```json
   { "sprk_name": "086 probe", "ownerid@odata.bind": "/teams(6eabc7f9-13be-f111-a05b-0022482913fc)" }
   ```

   - Expect HTTP 403 with code 0x80040299: "Read Privilege Check For Owner failed ... is missing prvReadsprk_StoredFile privilege (Id=ef068f7b-6a82-4c37-bf25-95c0129e0b47) on OTC=11029", and no row created.
   - Quote the response verbatim into the role row's `evidence`, in 089's format.
   - If the create **succeeds**: delete the row, record it and STOP (POML escalation).
2. **Role dry run.** `pwsh scripts/Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com`. Expect exactly one addition: Read (Basic) on `prvReadsprk_StoredFile`.
3. **Role -Apply.** The same command with `-Apply`.
4. **Role -Verify.** The same command with `-Verify`. Expect exit 0 and one more table than 089's 38/38.
5. **Creator person.** Each run uses `-EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -OntologyWriterApplicationId 69040982-612e-469e-a85f-26d5172367c5`.
   - **5a. Dry run** (no mode switch). Expect "sprk_storedfile.sprk_createdbyperson holds no value yet (safe to secure)", then WOULD secure, and WOULD grant both profiles.
   - **5b. `-Apply`.** Expect it secured, both profiles granted, and "still holds no value after securing".
   - **5c. `-Verify`.** Expect exit 0.
6. **Pre-deploy count.** Count Stored Files on invoices of Secure records; expect **0** (dev holds 0 Stored Files today, read 2026-10-27).
   - Run `GET .../sprk_storedfiles?$select=sprk_storedfileid,_sprk_invoice_value&$filter=_sprk_invoice_value ne null`.
   - For each distinct invoice, read its core lookups, then read that core's `sprk_issecure`.
   - While the first query returns no rows, the count is 0.
7. Optional: re-run the probe; it should now succeed. Delete the probe row straight away.
8. Live NFR-05: `AZURE_TOKEN_CREDENTIALS=AzureCliCredential`, `SPAARKE_NFR05_DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com`, then `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~SecureBuRoleDepthAssertionTests"`. The 99 known §5.2 findings on dev are not 086's.
9. Write the probe text, the -Apply / -Verify output, the count and the owner's OK with dates here and in the role JSON evidence. Then commit. Both -Verify runs gate every environment before a BFF carrying 092 deploys there (079 for dev).

## Reviews

- **Inline review** (sub-agents were not available: the session is at its cap). Two passes, tenant isolation then correctness:
  - **F-class, fixed:** deviations 1 and 2, and a vacuous assertion in my own scanner control (a non-child table). The control now uses child tables only.
  - **K1.** The share mirror puts Delete on Secure-team-owned Stored Files when a root share holds Full Access. It confers nothing without a role privilege, which the source guard pins until 191. Once 191 grants Stored File privileges, uac-r2 should decide whether the mirror leaves Delete off this table, given the hard-deny list.
  - **K2.** Stored Files go to the Secure team only through an invoice. An invoice that is user-owned and unfiled would make `ContentOf` answer Unchanged, so the file keeps its creator. That cannot happen today: every invoice is filed under a core record, and the persister uses `ForParents([core])`.
- **ADR check.**
  - ADR-002: no plugin.
  - ADR-010: no new service, interface or DI registration.
  - ADR-028: no auth change; reads stay behind the parent probe.
  - ADR-034: the membership model is unchanged.
  - ADR-038: maintenance-class tests at KEEP paths, with seeding proofs.
  - Root §10: +251 B and no package. Root §11: no new surface, only list entries plus one predicate pair.
- **Pending (main session):** two adversarial passes (tenant isolation), the uac-r2 review of `SecureChildLineage.cs`, `RecordOwnershipResolver.cs`, `RecordCreatorPerson.cs`, the role JSON, the creator-person script and its Pester pin, and the census; then the live steps above.
