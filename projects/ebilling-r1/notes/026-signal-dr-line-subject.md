# Task 026: Signal and Decision Record invoice-line subject

Completed 2026-10-16. Rigor FULL. uac-r2 decision D-94 (option A): `sprk_billingevent` (the Invoice Line) is an intermediate stamp root.

## Commits

| SHA | Subject |
|---|---|
| fafb0425c | feat: invoice-line subject lookup and firm reason schema script |
| febd800c4 | feat: file Signals and Decision Records under an invoice line |
| f3982e62e | feat: SpaarkeMaster source for the invoice-line subject and firm reason |
| 56e296754 | test: invoice-line subject derivation, refusal, restamp and storage |

The diff range for uac-r2's review is `73e912d22..56e296754`.

## Live changes (spaarkedev1, 2026-10-16, about 23:40Z)

All changes are additive. They were made by `scripts/ebilling/Deploy-EbillingSignalLineSubjectSchema.ps1`, which adds the components to the SpaarkeMaster solution.

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

There were no imports, no role or privilege changes and no PublishAllXml.

## D-94 conditions

1. **Live metadata read.** `EntityDefinitions(sprk_billingevent)/ManyToOneRelationships` was read on 2026-10-16 and is cited in the comments in all three tables.
   - Core lookups: `sprk_matter`→sprk_matter, `sprk_project`→sprk_project, `sprk_regardingworkassignment`→sprk_workassignment.
   - Excluded on purpose: `sprk_invoice` (D-75) and `sprk_resolvedtimekeeper` (a party).
2. **Restamp on re-file, and reconciler coverage.**
   - `CoreAncestorRestamper.AfterWriteAsync` is generic over `IntermediateRootColumns`. Every BFF path that edits a line's lookups (ChildRecordEndpoints, DataverseUpdateHandler, FieldMappingEndpoints, UpdateRecordActionCore) now cascades to Signals and Decision Records about the line.
   - StampSourceColumns for sprk_signal and sprk_decisionrecord now include `("sprk_regardinginvoiceline", "sprk_billingevent")`. They are pinned in `OntologyStampedChildRestampTests.Registration_*` and `CoreAncestorResolverTests`.
   - Tests:
     - re-filing a line matter → project re-stamps the row and leaves the decision context byte-identical;
     - a two-root line stamps nothing;
     - the reconciliation job finds a stale line row (report-only by default, repaired when released).
3. **Must-fire tests, and writer refusal on NoAncestor.**
   - A line with no core is refused: CoreRecordUnresolved in SignalWriter, SubjectUnfileable in DecisionRecordWriter.
   - A line with two roots is a permanent derivation error, refused by both writers.
   - An invoice with two cores is CoreRecordAmbiguous or SubjectUnfileable, never resolved matter-over-project.
   - A line's invoice is never read for its root.
4. **Storage resolver.**
   - A line of a secure matter or a secure project takes that root's container.
   - A two-root line gets 409 `container_ancestor_unresolved`, with no business-unit or fallback container, before any root read.
   - A to-do whose pair alone names a line is `container_ancestor_unverifiable`.
5. **Jest.** The suite is `PolymorphicResolverService.coreAncestor.test.ts`. The TS mirror derives a line's project and never selects `_sprk_invoice_value`. Both suites: 83/83 pass.

## Files

**Ontology-owned (ontology-r1 review requested):**
- `src/server/api/Sprk.Bff.Api/Services/Signals/SignalWriter.cs`
- `src/server/api/Sprk.Bff.Api/Services/Signals/DecisionRecordWriter.cs`
- `src/server/api/Sprk.Bff.Api/Services/Workspace/TodoRegardingBuilder.cs` (the shared builder's Signal/DR map)
- `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Signal/Entity.xml`
- `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_DecisionRecord/Entity.xml`
- `tests/unit/domain/Signals/SignalWriterTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/DecisionRecordWriterTests.cs`
- `tests/integration/data-mutation/Signals/SignalPairTypeSchemaAgreementTests.cs`
- `tests/integration/data-mutation/CoreAncestorStamping/OntologyStampedChildRestampTests.cs`

**uac-r2 lockstep tables (uac-r2 review requested):**
- `src/server/api/Sprk.Bff.Api/Services/Dataverse/CoreAncestorResolver.cs`: IntermediateRootColumns, OntologySubjectSources, and the new `SingleRootIntermediates`.
- `src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts`: INTERMEDIATE_ROOT_COLUMNS.
- `src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/RecordContainerResolver.cs`: KindByEntity, ByEntity, and the single-root 409.
- `src/server/api/Sprk.Bff.Api/Services/Access/SecureChildLineage.cs`: the Signal/DR line parent.

**Other:** the schema script, `Other/Relationships/sprk_BillingEvent.xml`, `Other/Relationships.xml`, and the following tests:
- `tests/unit/domain/Access/SecureChildLineageTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Dataverse/CoreAncestorResolverTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/RecordTypeCatalogStub.cs`
- `tests/integration/data-mutation/CoreAncestorStamping/StampWorld.cs`
- `tests/integration/data-mutation/CoreAncestorStamping/CoreAncestorStampReconciliationJobTests.cs`
- `tests/integration/data-mutation/RegardingBuilder/RegardingBuilderHostTableTests.cs`
- `tests/integration/auth/UnifiedAccessControl/ChildRecordContainerResolutionTests.cs`
- `PolymorphicResolverService.coreAncestor.test.ts`

## Solution source

`Export-SpaarkeMasterSource.ps1` was run against a temp folder (`C:\tmp\sm026\out`). Only the four files above were copied; every hunk in them is additive. There was no version bump.

The export stopped at its F12 guard on 7 pre-existing missing dependencies, none of them this task's:
- `sprk_externaltenantid`, `sprk_homeobjectid` and `sprk_hometenantid`, each needed by type 70 (two each);
- the `sprk_SpaarkePlatform` app module.

The live solution also has about 238 drifted or extra files belonging to other projects. These include app modules and sitemaps, PCF bundles, and Entity.xml for sprk_Budget, sprk_Communication and sprk_Organization. They were not copied, so the next owner's export picks them up.

## Verification

| Check | Result |
|---|---|
| BFF build | Green; no new warnings in changed files |
| Targeted + Signals + Finance + Invoice filter | 4,782 passed, 5 skipped, 0 failed |
| Spaarke.ArchTests | 1,056 / 1,056 |
| Jest (PolymorphicResolverService) | 83 / 83 |
| ADR-010 registrations | No DI changes (ceiling 165 untouched) |
| Route ledger | No endpoints (131 untouched) |
| Publish size (linux-x64 zip, base 73e912d22 → HEAD) | 39,223,607 → 39,225,138 B (+1.5 KB), total 39.2 MB |
| Vulnerable packages | None; no package changes |

Fail-before proof:
- With the source from fafb0425c: 34 of the new or changed .NET tests failed, and the jest line test failed.
- With only the two writer checks disabled: 5 writer tests failed (no-core line; no-core and two-core invoice). The two-root-line writer tests are covered by the resolver refusal.

## Deviations and justifications beyond the contract

- `SingleRootIntermediates` is new surface:
  - Existing: CoreAncestorResolver already fails closed when one table has two different ids, but not on two roots of different types.
  - Extension: it is the smallest extension of that rule.
  - Cost of doing nothing: a line under a matter and a project would be stamped with both, and its Signal would group under the matter (D-37), which is the "pick" FR-05 forbids.
- `SignalWriter.RequireSingleInvoiceFamilyCore` is new:
  - Without it, a Do-lane line or invoice with no core would be written as a no-core item (D-35).
  - An invoice with two cores would be grouped matter-over-project.
- `VerifiedSubjects` gains `sprk_invoice` as well as `sprk_billingevent`, because a billing finding can be about an invoice (FR-15).

## Known limits

- K1, invoice parity (question for uac-r2):
  - CoreAncestorResolver still derives both stamps for an invoice under a matter and a project, and the storage resolver does not refuse it. Only the writers refuse it.
  - Making the invoice single-root too would change to-dos and documents filed under such invoices, so it is left to uac-r2.
- K2, TS preview:
  - The browser derivation does not apply the single-root rule, so a two-root line would preview two stamps where the server refuses.
  - This is unreachable today: no client host can be filed under a line (sprk_todo has no line lookup).
- K3, model-driven re-file: a re-file made outside the BFF reaches Signal and DR rows only through the reconciliation job, which keeps sprk_signal and sprk_decisionrecord report-only by default. This is the existing posture for every ontology subject.
- K4, reason code: a two-root line Signal is refused as CoreRecordUnresolved (the resolver's permanent Error), while a two-core invoice is CoreRecordAmbiguous. The permanent Error also covers "subject no longer exists", so the writer cannot tell the two apart.

## Reviews

- ontology-r1: review of the ontology-owned files above: requested, pending.
- uac-r2: review of `73e912d22..56e296754` (the three tables, restamp coverage and tests) and the K1 question: requested, pending.
