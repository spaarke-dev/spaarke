# 086: invoice Stored Files join the secure family (leaf child of sprk_invoice)

> **Ported 2026-11-03** onto `work/ebilling-r1` 27e9cdc90d as branch `task/ebill-086-rebased` (089a and uac-r2 194b had landed). Read "Port onto work/ebilling-r1" below first: it supersedes deviation 1, the owner-pin and Unsecure expectations, and the live-step status in the sections after it, which record the original branch as built.

Branch `task/ebill-086-storedfile-secure-child`. Base: `origin/task/ebill-089-assignments` 8f4920ce0c, merged with `origin/work/ebilling-r1` (master uac-194) in its own commit 22b803e1a5. Rigor FULL, opus @ high. The code and tests are done. The live steps are listed below and have **not been run**: the main session runs them (owner EB-D-158). The role row's evidence is `PENDING-LIVE-PROBE`. The only live calls from this task were metadata and count reads.

## Port onto work/ebilling-r1 (2026-11-03)

Only 086's own commits were brought, by `git cherry-pick -x`: `6703ca3f3f` (feature), `e88825a528` (probe evidence), `6482277b9b` (these notes). The earlier 089 attempt (`b9a80f9bd2`, `9082a05a04`, `8f4920ce0c`) and the merge `22b803e1a5` were not.

**Conflicts and how each was resolved** (089a's content kept; only what 086 needs for `sprk_storedfile` added):

| File | Resolution |
|---|---|
| `RecordOwnershipResolver.cs` | 089a's `CitationLookups` kept unchanged. 086's two citations `(sprk_storedfile, sprk_sourcedocument)` and `(sprk_storedfile, sprk_sourcecommunication)` **dropped** (see below). The file carries no 086 change. |
| `RecordOwnerAssignmentCensusTests.cs` | 089a's Routed entries and PersonBearingWriters kept; the old 089 `ProcedureAssignmentService` entries not brought. Added: `ChildTables` `sprk_storedfile`; Routed `StoredFileService.cs / sprk_storedfile / 1` (text updated to `ForRow` / `StoredFileOwnerContext`); PersonBearingWriters `StageCreateFlipAsync` and `BuildRow`; the scanner's qualified-const fix and its control (deviation 2, unchanged). |
| `SecureChildLineageTests.cs` | The old 089 test not brought (089a has its own). 086's test now pins the invoice as the row's primary and the sources as non-citations. |
| Auto-merged | `config/secure-record-owner-role.json` (row after 089a's `sprk_ruleset`, 40 tables), `SecureChildLineage.cs` (comment updated for 194b), `RecordCreatorPerson.cs`, `StoredFileService.cs`, the creator-person script and Pester pin, `CreatorPersonColumnSecuredGuardTests`, `ProcedureRolePrivilegeGuardTests`. |
| `SecureBuRoleDepthAssertion.cs`, `BillingLookupsConferNoAccessTests.cs` | 086's own commits do not touch them (only the old 089 attempt did); 089a's content stands. The NFR-05 depth list already holds `prvReadsprk_StoredFile`; not duplicated. `GuardedPrivileges` unchanged. |

**Deviation 1 is withdrawn.** uac-r2 194b (R4b-2, owner round 131, "the 086 ruling") now asks a Stored File's owner by the row's own question on the service create (`StoredFileService.OwnerQuestionOf`, `ForRow` over the parent and the source lookups) and in the reconciler (`OrderedParentsOf`), so those two agree, which was the premise of the fix. The Submit Invoice persister asks over the core record only (`InvoiceSubmissionPersister.StoredFileOwnerContext`); see K-P1. And uac-r2 re-check K deliberately keeps the source document as a filing parent of the row's question (`StoredFileServiceOwnershipTests.NoCoreParent_WithASourceDocument_TheDocumentIsThePrimary`: under a parent that files nothing, the source document leads, and a Secure source gives the Secure team). With 086's citations, those four cases fail. For an invoice file the invoice is still the primary (lineage order), so a source in another business unit never decides its team; a Secure source makes it Secure-owned (fail closed).

**HEAD semantics the ported tests now follow** (behaviour from 194b and 094, not changed here):
- An ordinary invoice's Stored File is owned by the **restricted-content owner team** (hard-deny table), not the invoice's business-unit team. An Unsecure moves an isolated invoice file to that team.
- The service path refuses an invoice PDF (task 094's writer guard), so the owner pin uses a supporting file there; the persister case uses `InvoiceSubmissionPersister.StoredFileOwnerContext(core)`.

**Other guards that needed an entry after the port:**
- `RecordFamilyEdgesGuardTests.KnownDisagreements` (task 070, landed since): `sprk_storedfile.sprk_invoice -> sprk_invoice` baselined as ownership lineage only, as 089a did for its tables. **Whether a Stored File joins the displayed family is UAC-r2's call.**
- 089a's depth tests assumed the Stored File was not codified yet. Updated: the theory's Stored File case is now clause 5's finding (`SecureOwnerRoleLacksCodifiedPrivilege`); the un-codified branch has its own test over a three-root set (`Build_WhenAnUncodifiedDepthListPrivilegeIsNotInTheEnvironment_ReportsItOnce`); the census-job F3 test reaches `RestrictedContentPrivilegeNotResolved` through a second owner role (clause 5 cannot grade, 089a K6). Seeded: with `or RestrictedContentPrivilegeNotResolved` dropped from the job, the F3 test fails.

- `InvoiceSubmissionPersisterTests` (outside the task's test filter; found by the adversarial pass): with `sprk_storedfile` a lineage child, `SubmissionCommitGate.FileRootsAsync` walks the copy's roots up from its file rows (the branch 094 wrote for 086). The harness mirrored only the invoice into its isolation world, so the gate found no rows and undid every source submission: 17 "094" tests failed, and `Gate_ReRunCannotBeMade_IsUndone` and `Gate_SourceRootsNotCoveredByTheCopy_IsUndone` passed for that reason rather than their own. The harness now files each created Stored File under its invoice; 196/196, and the happy-path tests that share those controls' set-up pass, so each control fails for its own cause. No production change.

**Seeding proof re-run** (stored-file lineage guard): with the `sprk_storedfile` lineage entry removed, both reconciler must-fire tests, the lineage test and `TheLineageMapCoversExactlyTheCodifiedChildTables` fail; restored.

**Live, read only (2026-11-03, spaarkedev1):**
- `Set-SecureRecordOwnerRolePrivileges.ps1 -WhatIf`: config 40 tables, present 40 of 40, "Outside the file (4)": `prvCreateSharePointData`, `prvReadSharePointData`, `prvReadSharePointDocument`, `prvWriteSharePointData` (Global). `prvReadsprk_StoredFile` is no longer listed. "Nothing to add." Exit 0.
- `Set-SecureRecordOwnerRolePrivileges.ps1 -Verify`: "VERIFY PASS: the role holds Read at Basic on all 40 tables." Exit 0.
- `Set-ChildRecordCreatorPersonSchema.ps1 -Verify`: `sprk_storedfile.sprk_createdbyperson` exists, is field-secured, both BFF profiles granted, in SpaarkeMaster; "VERIFY PASS", exit 0. INFO: 1 app-created Stored File row with no person recorded (dev held 0 rows on 2026-10-27).
- So the role `-Apply` and the creator-person `-Apply` were run on dev after the 2026-10-27 probe; who ran them and when is not recorded in this branch.

**Still pending (live or review; none run by the port):**
1. The pre-deploy count of Stored Files on invoices of Secure records (step 6 below; dev now holds 1 Stored File row, so the count is no longer trivially 0).
2. The owner's OK with dates, and the `-Apply` outputs, for the role and creator-person runs (criterion 6).
3. The live NFR-05 census once a BFF carrying 089a/086 is on dev.
4. The second adversarial pass and the uac-r2 review of `SecureChildLineage.cs`, `RecordCreatorPerson.cs`, the role JSON, the creator-person script and pin, the census, `RecordFamilyEdgesGuardTests` and the 089a depth tests.

`Ebilling:StoredFileReconcileEnabled` stays `false` (HARD RULE); not touched.

**Port review (one adversarial pass, opus, 2026-11-03).** F-class: the persister harness (fixed above); the notes' missing live records (criterion 6, still pending: items 1-2 above). Known limits:
- **K-P1 (K1, dormant until uac-r2 191).** The persister owns an invoice file by the core record only; the service create and the reconciler also read the source document and communication as secure-if-any parents; every walk (descendant load, share sync, recent-changes pass, commit gate) follows `sprk_invoice` only. So a copy whose source is later filed under a Secure matter S stays Secure-owned after its own matter's Unsecure, S's walks never reach it, and S's Make Secure never pulls in a restricted-content copy. Nothing is exposed: no role grants a Stored File privilege, and the BFF serves a file only on the caller's Read of its invoice and its source. For uac-r2 to rule on with 191.
- **K-P2 (K2, fails closed).** The commit gate's walk now passes through the invoice; a Secure core whose invoice is still ordinary-owned (Make Secure racing the gate) gives the copy no roots, so the commit is undone `source_check_failed`. No test pins this race.
- **K-P3 (K3).** `ProcedureRolePrivilegeGuardTests` scans source role XML only; a platform role holding Stored File privileges (Support User, System Customizer, Service Reader/Writer/Deleter on dev) assigned to a flagged-external user is caught only by the live NFR-05 census.
- **K-P4 (K4).** `StoredFileOwnerPinTests` is a pin, not a proof: the owner already comes from 194b's row question and `BuildRow`'s explicit stamp hides the `StampedChildTables` entry, so no case fails without 086's own edits. The persister-path proof is the repaired `InvoiceSubmissionPersisterTests`.
- The original K1 (share mirror puts Delete on Secure-owned Stored Files under Full Access) stands.
- Noticed, not changed: `SubmissionCommitGate.FileRootsAsync`'s pre-086 branch (`SecureChildLineage.IsChild(sprk_storedfile)` false) is now dead, and its comments (`SubmissionCommitGate.cs` remarks and `FileRootsAsync` summary) still describe both states.

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

### uac-r2 answers on the port (2026-11-03)
- **Source citations dropped:** agreed. Since 194b (R4b-2c), a Stored File's owner question includes `sprk_sourcedocument` and `sprk_sourcecommunication` by design. `CitationLookups` stays as 089a has it, with no Stored File entry.
- **Role and creator-person -Apply on dev:** runner and time not recorded. The state was proven by -Verify on 2026-11-03: Read at Basic on 40/40 tables, and the creator-person column secured to both BFF profiles.
- **Family registry:** Stored Files stay out of the displayed family. The baselined edge `sprk_storedfile.sprk_invoice -> sprk_invoice` is accepted. A Stored File is an internal, hard-deny file row, reached only through its document, invoice or batch.
- **Known limits:** all accepted.
  - K1: Full Access on the root already allows deleting its children. Add a note in the access model guide.
  - K-P1: goes to uac-r2 task 191.
  - K-P2: fails closed.
  - K-P3: the live census is the backstop.
- **Gate:** `StoredFileReconcileEnabled` stays false until the deploy carrying 089a and 086 passes uac-r2's gate and the NFR-05 census. The pre-deploy count of Stored Files on invoices of Secure records goes into the deploy request.

### Access review (089a+086) dispositions with uac-r2 (2026-11-03)
- **Share mirror, #2559:** owned by uac-r2. For `RestrictedContentTables` the mirror will pass Read only, before uac-r2 task 191. Do not write this fix on the ebilling side. The owner decides how #2559 is tracked; do not edit it until then.
- **K3:** a Stored File owned by the restricted-content team stays with the old customer when its parent moves across top units. It is reported, not moved (194b's behaviour). Accepted.
- **K4:** Stored Files placed before 086 on matters that were already Secure. If the pre-deploy count is above 0, the deploy request asks for the backfill run after the gate. Accepted.
