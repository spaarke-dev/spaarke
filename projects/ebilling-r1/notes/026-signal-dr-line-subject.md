# Task 026: Signal and Decision Record invoice-line subject

Completed 2026-10-16; fix-round-1 2026-10-17. Rigor FULL. uac-r2 decision D-94 (option A): `sprk_billingevent` (the Invoice Line) is an intermediate stamp root.

## Commits

| SHA | Subject |
|---|---|
| 33ad6c984 | feat: invoice-line subject lookup and firm reason schema script |
| a49fa8699 | feat: file Signals and Decision Records under an invoice line |
| b8e4c019a | feat: SpaarkeMaster source for the invoice-line subject and firm reason |
| b5f4a4c78 | test: invoice-line subject derivation, refusal, restamp and storage |
| 68fcc3cff | docs: task 026 notes and completion |

The diff range of the task is `ef3efce73..b5f4a4c78`.

Fix-round-1:

| SHA | Item | Subject |
|---|---|---|
| 484d76300 | 1 | fix: refusal kinds; a two-root line Signal is CoreRecordAmbiguous |
| 387f2eb60 | 3a | fix: a Decision Record's core must be its invoice-family subject's own |
| 64136db3d | 3b | fix: a line belongs only to an invoice of its own core record |
| 3525050bd | 6 | test: the intermediate-root lockstep is order-sensitive |
| 3e97b2984 | 2 | feat: field-secure the Signal firm reason (D-95(2)) |
| (this commit) | 4, 5 | docs: notes, deploy order, project CLAUDE.md, rework line |

## Live changes (spaarkedev1)

All changes are additive. There were no imports, no user role or team changes and no PublishAllXml.

### Task 026 (2026-10-16, about 23:40Z)

`scripts/ebilling/Deploy-EbillingSignalLineSubjectSchema.ps1` adds the components to the SpaarkeMaster solution.

| Run | Result |
|---|---|
| WhatIf | 4 would-create |
| Apply | 4 created, published (entity-scoped PublishXml for sprk_signal and sprk_decisionrecord only) |
| Rerun | 0 created, 4 skipped, no publish |

The four components:
- The relationship `sprk_signal_reginvoiceline` (sprk_signal.sprk_regardinginvoiceline → sprk_billingevent). Delete and Archive are RemoveLink; everything else is NoCascade.
- The relationship `sprk_decisionrecord_reginvoiceline`, with the same shape.
- The column `sprk_signal.sprk_firmreason`: Memo, MaxLength 2000.
- A catalog row in `sprk_recordtype_ref`, "Invoice Line" (INVL), with id `fb30d221-bbc9-f111-a05a-7c1e520a989f`, regarding field `sprk_regardinginvoiceline` and display-name field `sprk_name`.

### Fix-round-1, item 2: firm reason field security (owner D-95(2))

`scripts/ebilling/Set-EbillingFirmReasonFieldSecurity.ps1`:

| Run | Result |
|---|---|
| WhatIf | all preconditions OK; would create the profile, add its member, secure the column and grant both profiles |
| Write | created the profile `Spaarke Ontology Writer Firm Reason Writers` (`ec323a05-f95c-4308-aac5-6d899032691e`) in SpaarkeMaster; added `# mi-ontology-writer-dev` (appId 69040982-…) as its only member; secured `sprk_signal.sprk_firmreason`; granted `Spaarke BFF-Managed Field Readers` read=4 and the new profile 4/4/4; masked window 15.2 s; published sprk_signal |
| Rerun | everything OK, nothing written, no publish |
| -Verify | PASS, exit 0 |

- **Who reads it.** Every user reads it through `Spaarke BFF-Managed Field Readers`, which is already on all 6 business-unit default teams (checked live; the script FAILs and writes nothing if one is missing, because adding it would be a team change). No team or role assignment was made.
- **Other identities.** The writer MI is also on the `Spaarke` default team. The BFF application users (`# mi-bff-api-dev`, `SDAP-BFF-SPE-API`, `# spaarke-bff-api-prod`) are System Administrators and bypass field security. No code reads or writes the column yet (task 130 will), and 0 Signals carried a value.
- **Why a new profile.** Ontology's D-136 profile `Spaarke Ontology Writer Field Writers` has the same member, but its `-Verify` (`scripts/Set-ChildRecordCreatorPersonSchema.ps1`) FAILs on any row outside `sprk_createdbyperson`. That `-Verify` was re-run after this change and still PASSes.
  - Existing: the D-136 profile.
  - Extension: extending it would break ontology's gate.
  - Cost of doing nothing: either no non-administrator identity could write the firm reason, or any Signal writer could change what a firm is told.
- **Solution source.** SpaarkeMaster carries `IsSecured=1` on the column, the reader's row, the new profile and its root component. `FirmReasonFieldSecurityAgreementTests` pins the script against the source.

## D-94 conditions

1. **Live metadata read.** `EntityDefinitions(sprk_billingevent)/ManyToOneRelationships` was read on 2026-10-16 and is cited in the comments in all three tables.
   - Core lookups: `sprk_matter`→sprk_matter, `sprk_project`→sprk_project, `sprk_regardingworkassignment`→sprk_workassignment.
   - Excluded on purpose: `sprk_invoice` (D-75) and `sprk_resolvedtimekeeper` (a party).
2. **Restamp on re-file, and reconciler coverage.**
   - `CoreAncestorRestamper.AfterWriteAsync` is generic over `IntermediateRootColumns`. StampSourceColumns for sprk_signal and sprk_decisionrecord include `("sprk_regardinginvoiceline", "sprk_billingevent")`. They are pinned in `OntologyStampedChildRestampTests.Registration_*` and `CoreAncestorResolverTests`.
   - Corrected in fix-round-1. These are the BFF paths that can change a line's core lookups, checked against the code on 2026-10-17. Each one calls `AfterWriteAsync` after its write.

     | Path | Writes a line's core lookups? | AfterWriteAsync | FR-05 guard |
     |---|---|---|---|
     | `POST /api/v1/child-records/sprk_billingevent` (ChildRecordEndpoints.CreateAsync) | on create only | `ChildRecordEndpoints.cs:298` | yes (OwnedChildWrite) |
     | `PATCH /api/v1/child-records/{table}/{id}` | **no**: `RefileTables` (`:97-100`) leaves out sprk_billingevent, so the route answers 400 `child_record.table_unsupported` | (`:546` is this route; it never runs for a line) | — |
     | chat `dataverse.update_record` (DataverseUpdateRecordHandler) | yes | `DataverseUpdateRecordHandler.cs:336` | yes (OwnedChildWrite.RefileAsync, before anything else) |
     | playbook update (UpdateRecordActionCore, ActionSeam) | yes (rendered lookups) | `UpdateRecordActionCore.cs:251` | **no** |
     | output orchestrator (DataverseUpdateHandler) | yes (EntityReference fields) | `DataverseUpdateHandler.cs:95` | **no** |
     | field-mapping push (FieldMappingEndpoints) | yes (a mapping rule onto a line) | `FieldMappingEndpoints.cs:1068` | **no** |

     Our reviewer was right. ontology-r1's `:298/:546` reading holds only for the create (`:298`): `:546` is the re-file route, which refuses a line. A line edited on its model-driven form reaches its Signal and DR rows only through the reconciliation job (K3).
   - Tests:
     - re-filing a line matter → project re-stamps the row and leaves the decision context byte-identical;
     - a two-root line stamps nothing;
     - the reconciliation job finds a stale line row (report-only by default, repaired when released).
3. **Must-fire tests, and writer refusal on NoAncestor.**
   - A line with no core is refused: CoreRecordUnresolved in SignalWriter, SubjectUnfileable in DecisionRecordWriter.
   - A line with two roots is CoreRecordAmbiguous in SignalWriter (fix-round-1 item 1) and SubjectUnfileable in DecisionRecordWriter.
   - An invoice with two cores is CoreRecordAmbiguous or SubjectUnfileable, never resolved matter-over-project.
   - A line's invoice is never read for its root.
4. **Storage resolver.**
   - A line of a secure matter or a secure project takes that root's container.
   - A two-root line gets 409 `container_ancestor_unresolved`, with no business-unit or fallback container, before any root read.
   - A to-do whose pair alone names a line is `container_ancestor_unverifiable`.
   - **Deviation, accepted by uac-r2 as meeting condition 4:**
     - A document or to-do regarding a line in a secure matter gets 409 `container_ancestor_unverifiable`. It fails closed, with no container and no business-unit fallback.
     - This is unreachable today, because no upload or storage route takes a line as its parent (project CLAUDE.md gotcha).
     - It becomes a carrier (`sprk_invoice`) in uac-r2 #2047.
5. **Jest.** The suite is `PolymorphicResolverService.coreAncestor.test.ts`. The TS mirror derives a line's project and never selects `_sprk_invoice_value`. Both suites: 83/83 pass.

## Fix-round-1

The coordinator merged three reviews into six items.

1. **Refusal kinds.**
   - `CoreAncestorResult` / `CoreAncestorStampOutcome` carry `RefusalKind` (Transient, NotFound, MultipleRoots). The property is additive: Status, Stamps, Error and IsPermanent are unchanged for every caller. `Refusals_CarryTheirKind_AndTheSamePermanenceAsBefore` and `Successes_CarryNoRefusalKind` cover every existing intermediate and core table.
   - SignalWriter maps only a permanent MultipleRoots on an invoice-family subject to CoreRecordAmbiguous. NotFound and Transient stay CoreRecordUnresolved.
   - A two-root line and a two-core invoice now give the same reason (this was K4).
2. **Firm reason field security.** See Live changes.
3. **Line core.**
   - (a) DecisionRecordWriter refuses SubjectUnfileable when an invoice or line subject's `request.Core` is not its single derived core.
   - (b) FR-05 / task 013 already refused a line naming no core or more than one, but nothing compared a line's core with its invoice's. `InvoiceFamilyCoreRecordGuard.CheckWriteAsync` now refuses `invoice_family_line_invoice_core_record_mismatch` when a line names an invoice of another core record, or of none or several. An unreadable invoice fails closed with `read_failed` or `record_not_found`.
     - `sprk_invoice` is now a guarded lookup of the line, so an update that touches it is checked.
     - D-75 holds: the invoice is compared, never the source of the line's core. `ColumnsToRead`, the stored-row twin, is unchanged.
     - The write core reads a stored line's invoice from `sprk_invoices`.
     - This covers the child-record create route and the chat create and update tools. The three unguarded paths in the condition 2 table are a pre-existing FR-05 coverage gap (ISS-010 scoped the guard to the write core). They are reported to the coordinator, not fixed here.
4. These notes.
5. **Deploy order.**
   - From task 026 on, the BFF's Signal and Decision Record reads `$select` `sprk_regardinginvoiceline`: SecureChildLineage and the restamp / reconciliation stamp sources.
   - A BFF deployed where that column does not exist answers 400 on every Signal and Decision Record read.
   - Task 053 now has a binding step 5 (schema verified before the BFF; elsewhere the SpaarkeMaster import comes first, then the firm reason script), and DEF-021 has the same line.
6. **Lockstep order.** `IntermediateRootColumns_MatchTheTypeScriptSide` uses `WithStrictOrdering`. Swapping two TS rows fails it.

## Files

**Ontology-owned (ontology-r1 review requested):**
- `src/server/api/Sprk.Bff.Api/Services/Signals/SignalWriter.cs` (task 026; fix-round-1 item 1)
- `src/server/api/Sprk.Bff.Api/Services/Signals/DecisionRecordWriter.cs` (task 026; fix-round-1 item 3a)
- `src/server/api/Sprk.Bff.Api/Services/Workspace/TodoRegardingBuilder.cs` (the shared builder's Signal/DR map)
- `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Signal/Entity.xml` (task 026; fix-round-1 item 2: `IsSecured`)
- `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_DecisionRecord/Entity.xml`
- `tests/unit/domain/Signals/SignalWriterTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/DecisionRecordWriterTests.cs`
- `tests/integration/data-mutation/Signals/SignalPairTypeSchemaAgreementTests.cs`
- `tests/integration/data-mutation/CoreAncestorStamping/OntologyStampedChildRestampTests.cs`

**uac-r2 owned (uac-r2 review requested):**
- `src/server/api/Sprk.Bff.Api/Services/Dataverse/CoreAncestorResolver.cs`: IntermediateRootColumns, OntologySubjectSources, `SingleRootIntermediates`; fix-round-1 `CoreAncestorRefusalKind`.
- `src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts`: INTERMEDIATE_ROOT_COLUMNS.
- `src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/RecordContainerResolver.cs`: KindByEntity, ByEntity, and the single-root 409.
- `src/server/api/Sprk.Bff.Api/Services/Access/SecureChildLineage.cs`: the Signal/DR line parent.
- `src/server/api/Sprk.Bff.Api/Services/Dataverse/OwnedChildWrite.cs` (fix-round-1 item 3b: the other invoice's entity set).

**Other:**
- Scripts: the schema script and `scripts/ebilling/Set-EbillingFirmReasonFieldSecurity.ps1`.
- `src/server/api/Sprk.Bff.Api/Services/Dataverse/InvoiceFamilyCoreRecordGuard.cs` (ours, task 013).
- SpaarkeMaster: `Other/Relationships/sprk_BillingEvent.xml`, `Other/Relationships.xml`, `Other/FieldSecurityProfiles.xml`, `Other/Solution.xml`.
- Tests:
  - `tests/unit/domain/Access/SecureChildLineageTests.cs`
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Dataverse/CoreAncestorResolverTests.cs`
  - `tests/unit/Sprk.Bff.Api.Tests/Services/Dataverse/InvoiceFamilyCoreRecordGuardTests.cs`
  - `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/RecordTypeCatalogStub.cs`
  - `tests/integration/data-mutation/CoreAncestorStamping/StampWorld.cs`
  - `tests/integration/data-mutation/CoreAncestorStamping/CoreAncestorStampReconciliationJobTests.cs`
  - `tests/integration/data-mutation/RegardingBuilder/RegardingBuilderHostTableTests.cs`
  - `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.InvoiceFamilyGuard.cs`
  - `tests/integration/data-mutation/Signals/FirmReasonFieldSecurityAgreementTests.cs`
  - `tests/integration/auth/UnifiedAccessControl/ChildRecordContainerResolutionTests.cs`
  - `PolymorphicResolverService.coreAncestor.test.ts`

## Solution source

`Export-SpaarkeMasterSource.ps1` was run against a temp folder (`C:\tmp\sm026\out`). Only the four task files were copied; every hunk in them is additive. There was no version bump.

The export stopped at its F12 guard on 7 pre-existing missing dependencies, none of them this task's (DEF-021, #2045):
- `sprk_externaltenantid`, `sprk_homeobjectid` and `sprk_hometenantid`, each needed by type 70 (two each);
- the `sprk_SpaarkePlatform` app module.

The live solution also has about 238 drifted or extra files belonging to other projects. These include app modules and sitemaps, PCF bundles, and Entity.xml for sprk_Budget, sprk_Communication and sprk_Organization. They were not copied, so the next owner's export picks them up.

Fix-round-1 edited the source by hand, to match what the firm reason script applied live: Entity.xml `IsSecured`, the two profile entries and the root component. It did not run an export, because of DEF-021.

## Verification

| Check | Task 026 | Fix-round-1 |
|---|---|---|
| BFF build (-m:1) | Green; no new warnings in changed files | Green; no warnings in changed files |
| Targeted + Signals + Finance + Invoice filter | 4,782 passed, 5 skipped, 0 failed | 4,813 passed, 5 skipped, 0 failed (adds Dataverse, RecordOwnership, UnifiedAccessControl, DecisionRecord, FieldSecurity) |
| Spaarke.ArchTests | 1,056 / 1,056 | 1,056 / 1,056 |
| Jest (PolymorphicResolverService) | 83 / 83 | 83 / 83 |
| ADR-010 registrations | No DI changes (ceiling 165 untouched) | No DI changes |
| Route ledger | No endpoints (131 untouched) | No endpoints |
| Publish size (linux-x64 zip) | 73e912d22 (task 026 base) → task 026 head: 39,223,607 → 39,225,138 B (+1,531 B), total 39.2 MB | 794dd173c → fix-round-1 head: 39,233,041 → 39,235,193 B (+2,152 B), total 39.2 MB |
| Vulnerable packages | None; no package changes | No package changes |

Fail-before proofs:
- Task 026, with the source from 33ad6c984: 34 of the new or changed .NET tests failed, and the jest line test failed. With only the two writer checks disabled, 5 writer tests failed.
- Fix-round-1:
  - item 1: with the MultipleRoots mapping disabled, 2 tests fail;
  - item 3a: with the core check commented out, 3 fail;
  - item 3b: with the line branch disabled, 13 fail; with the stored-line entity-set fallback reverted, 1 fails;
  - item 6: with two TS rows swapped, the lockstep fails;
  - item 2: with Entity.xml `IsSecured` reverted, the agreement test fails.

## Deviations and justifications beyond the contract

- `SingleRootIntermediates` is new surface:
  - Existing: CoreAncestorResolver already fails closed when one table has two different ids, but not on two roots of different types.
  - Extension: it is the smallest extension of that rule.
  - Cost of doing nothing: a line under a matter and a project would be stamped with both, and its Signal would group under the matter (D-37), which is the "pick" FR-05 forbids.
- `SignalWriter.RequireSingleInvoiceFamilyCore` is new:
  - Without it, a Do-lane line or invoice with no core would be written as a no-core item (D-35).
  - An invoice with two cores would be grouped matter-over-project.
- `VerifiedSubjects` gains `sprk_invoice` as well as `sprk_billingevent`, because a billing finding can be about an invoice (FR-15).
- Fix-round-1:
  - `CoreAncestorRefusalKind` is an additive property, with no behaviour change for any existing caller. Without it, the writer cannot tell "two roots" from "subject gone".
  - `LineInvoiceCoreRecordMismatchCode` is a new refusal code inside the existing guard.
  - The firm reason writer profile is a new profile (see Live changes).

## Known limits

- K1, invoice two roots:
  - CoreAncestorResolver still derives both stamps for an invoice under a matter and a project, and the storage resolver does not refuse it. Only the writers refuse it.
  - This is owned by uac-r2 under #2047. uac-r2 confirmed that single-root invoices are theirs to decide there, and not part of 026. Live dev: 0 of 10 invoices name two roots.
- K2, TS preview:
  - The browser derivation does not apply the single-root rule, so a two-root line would preview two stamps where the server refuses.
  - This is unreachable today: no client host can be filed under a line (sprk_todo has no line lookup).
- K3, model-driven re-file: a re-file made outside the BFF reaches Signal and DR rows only through the reconciliation job, which keeps sprk_signal and sprk_decisionrecord report-only by default. This is the existing posture for every ontology subject.
- K4: resolved in fix-round-1 item 1.
- K5, RemoveLink on append-only DRs: deleting or archiving a line clears `sprk_regardinginvoiceline` on its Decision Records, which are append-only, while their pair text (`sprk_regardingrecordid`, name, URL) survives. The DR still says which line it was about, but the lookup is gone. This is the same cascade as `sprk_decisionrecord_reginvoice`.
- K6, a no-root line in storage: a line with no core lookup would resolve storage through the business-unit fallback rather than refusing. This is unreachable, because every BFF create and update path refuses a no-core line (FR-05) and no storage route takes a line as parent.
- K7, FR-05 coverage: the playbook update, the output orchestrator and the field-mapping push can write a line's core lookups without the invoice-family guard (pre-existing; see condition 2). This is reported to the coordinator.

## Reviews

| Reviewer | Scope | Verdict |
|---|---|---|
| ours (code-review) | the whole task | fix-now items; folded into fix-round-1 items 1, 3, 4 and 6 |
| ontology-r1 | the ontology-owned files | F1: the Ontology Service role lacked AppendTo on the invoice line and invoice. ontology-r1 applied that live and carries the XML and guard in its own PR (D-95, D-95a). Its condition 2 reading (`:298/:546`) is corrected above. Other items folded into fix-round-1. |
| uac-r2 | `ef3efce73..b5f4a4c78`: the three tables, restamp coverage, tests and the K1 question | No fix-now. All five D-94 conditions met (condition 4 with the deviation above). MultipleRoots → CoreRecordAmbiguous accepted. Single-root invoices are uac-r2's under #2047. uac-r2 will re-check only the CoreAncestorResolver refusal-kind change. |

Owner decision D-95(2) (field security on the firm reason) came from the task 026 review.
