# Task 022c: the Restricted-source rule on Repair and the field-mapping push

Rigor: FULL. Branch `task/fm-022c` (from `work/field-mapping-server-write-path-r1` at `93242e9a39`). UAC-r2 reviews the diff
before merge. UAC-r2 confirmed both POML rules as written (2026-11-12): the push filing rule for a Restricted or unreadable
source, and the stored `sprk_accesspermission` as a row's answer.

## Rules built

1. **One answer for "is this stored row Restricted"** (`RecordCreationService.StoredRowIsRestricted(table, row)`, Mapping.cs,
   beside 022b's `MappingSourceAccessOf`). True only when the row's table carries the column as a target
   (`InheritedAccessPermission.CarriesColumn`) and the row holds the Restricted option. No row, a table without the column, an
   absent or null value, a value of another type, or any other option (Standard, Limited, an unknown one) is not Restricted.
   `StoredRowAccessColumns(table)` names the column to read on such a table, none otherwise. Both writers call these two; neither
   re-derives the answer.
2. **The source's answer** is 022b's `MappingSourceAccessOf` (NoColumn, NotRestricted, Restricted, Unreadable), read in each
   writer's existing one source read (`MappingSourceAccessColumns` added to it). Not forked.
3. **Repair** (`RepairMappingAsync`): after the secure copy gate (`SourceMayBeCopiedAsync`, unchanged), a source that is
   Restricted or Unreadable into a row that is not Restricted maps nothing for the row — the same whole-mapping skip as a secure
   source — reported as `record_repair.mapping_source_restricted` (`RecordRepairReason.MappingSourceRestricted`) and logged by
   the row's and the source's ids. The row's answer is its stored value, held by the whole-row read (`ColumnSet(true)`):
   Repair reads nothing more. Repair never maps `sprk_accesspermission` (`ColumnsRepairNeverMaps`), so the stored value is the
   value the row keeps. A secure source keeps its own reason (`MappingSourceSecure`), which is asked first.
4. **Push** (`FieldMappingPushSourceGate.RefusalForChildAsync`):
   - The source's one read (`ReadSourceAsync`) also asks for `sprk_accesspermission` where the source table carries it.
   - A source that is not secure and is NoColumn or NotRestricted returns before any child is read (the early return stays).
   - Otherwise the child is read once, with only what the answer needs: its owner (a secure source), the filing columns the
     push writes, and its `sprk_accesspermission` (a Restricted or Unreadable source, where the child's table carries it). With
     nothing to read the child is not read.
   - A Restricted or Unreadable source never changes a child's filing: `RefileNotAllowedReasonCode` and its text, as a secure
     source (UAC-r2 (a)), even into a Restricted child.
   - A secure source then asks the owner question unchanged (`NotIsolatedReasonCode` wins over the Restricted answer).
   - A child that is not Restricted: `field_mapping.push.restricted_source_not_restricted` for a Restricted source (fixed text
     `RestrictedSourceNotRestrictedMessage`, which names no record; `MessageFor` returns it), `UndeterminedReasonCode` for an
     Unreadable one. Nothing is written to it.
   - The preview and the apply both reach the gate through the existing path (`FieldMappingPushFamily.PreviewAsync` and
     `ApplyMappingsToChildRecordsAsync`), so they give the same code.
5. **Fail closed.** A source that does not come back, a fault, a child that does not come back or whose read faults is
   `UndeterminedReasonCode` (push) or the existing unreadable-source skip (Repair). Cancellation is rethrown.
6. **Unchanged.** The secure copy gate's verdicts, codes and texts; the create (022b); `SecureRootInheritance.cs` and UAC-r2's
   files. The only text change on the secure path is the refile LOG line, which now also names the source's Access Permission.

## Section 11 answers (new members and constants)

| Member | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `RecordCreationService.StoredRowIsRestricted` | 022b's `InCreateAccessPermissionAsync` (a row being created, decided from its parents and stamp plan) | That decides a row not yet written; a stored row's answer is its stored value (UAC-r2), a different input, so a separate pure member placed beside `MappingSourceAccessOf` | Repair and the push would each derive "Restricted" on their own, or not at all, and a Restricted source's values would be written into rows that are not Restricted |
| `RecordCreationService.StoredRowAccessColumns` | `MappingSourceAccessColumns` (the SOURCE set, which includes the agreement) | The target set differs (`CarriesColumn`), so reusing the source helper would read the column on a target that takes no enforcement | The push would read the column on tables without it (a failed read), or never read it |
| `RecordRepairReason.MappingSourceRestricted` (`record_repair.mapping_source_restricted`) | `MappingSourceSecure` | A different rule; reusing the secure code would misreport why nothing was mapped (POML: a new reason) | A Repair run could not report or count the Restricted skip |
| `FieldMappingPushSourceGate.RestrictedSourceNotRestrictedReasonCode` + `RestrictedSourceNotRestrictedMessage` | `NotIsolatedReasonCode` + its text | The not-isolated text says the source is secure, which a Restricted source need not be (POML: a new code) | The person would be told the wrong reason, or nothing, for a child the push does not write |

No new endpoint, DI registration, package, Dataverse column or background work. All changes are in existing BFF files on the
two write paths (`RecordCreationService.Repair.cs`, `.Mapping.cs`, `FieldMappingPushSourceGate.cs`) and the DTO's doc comment.

## Evidence

Tests added (55 cases, KEEP paths only):
- `tests/integration/data-mutation/RecordOwnership/RecordRepairModeTests.RestrictedSource.cs` (27; the class and `RepairWorld`
  made `partial`, `RepairWorld.Copy` made public, two fixture helpers: a report card under the matter, a to-do under an
  ordinary invoice).
- `tests/unit/domain/Dataverse/FieldMapping/FieldMappingPushSourceGateTests.RestrictedSource.cs` (26; the class made `partial`).
- `tests/integration/contract/Api/FieldMappings/FieldMappingPushFamilyContractTests.RestrictedSource.cs` (2; the class made
  `partial`).

AC map:
- **AC1** (Repair must fire): `ARestrictedSource_IntoARowThatIsNotRestricted_MapsNothing_WithTheRestrictedReason` (Standard,
  Limited, no value, an unknown option, a value of another type), `ARestrictedSource_IntoARowWhoseTableHasNoColumn_...`
  (report card), `AnUnreadableSource_IntoARowThatIsNotRestricted_...` (unknown option, another type),
  `TheReason_IsLoggedWithTheRowsId`.
- **AC2** (Repair must not fire): `ARestrictedOrUnreadableSource_IntoARestrictedRow_Maps`,
  `ASourceThatIsNotRestricted_MapsUnchanged_AndTheRowIsNotReadAgain` (Standard, Limited, no value; no read of the row's table),
  `ASourceTableWithoutTheColumn_IsNotAskedForIt_AndMaps` (invoice source holding a Restricted value it cannot carry),
  `AStandardSource_IntoARowWhoseTableHasNoColumn_Maps`, `TheSourceRead_AsksForTheAccessPermission_InItsOneRead`, and the shared
  answer `AStoredRow_IsRestricted_OnlyWhenItsTableCarriesTheColumn_AndItHoldsTheRestrictedOption` (9 cases) + `NoRow_IsNotRestricted`.
- **AC3** (push must fire): `ARestrictedSource_AndAChildThatIsNotRestricted_SkipsTheChild_WithTheRestrictedCode_AndWritesNothing`
  (5 cases), `ARestrictedSource_AndAChildWhoseTableHasNoColumn_IsRefused_WithoutReadingTheChild`,
  `AnUnreadableSource_AndAChildThatIsNotRestricted_SkipsTheChild_WithTheUndeterminedCode` (2),
  `ARestrictedOrUnreadableSource_ChangingAChildsFiling_IsRefusedWithTheRefileCode_EvenIntoARestrictedChild` (4: change and clear),
  `ARestrictedSecureSource_AndAnIsolatedChildThatIsNotRestricted_GetsTheRestrictedCode`,
  `ARestrictedSecureSource_AndAChildNotIsolated_KeepsTheSecureGatesCode`, `TheRestrictedCodesText_NamesNoRecord_AndIsWhatMessageForGives`;
  route: `Apply_FromARestrictedSource_WritesOnlyTheRestrictedChild_AndGivesTheOtherTheRestrictedCode` and
  `Preview_FromARestrictedSource_ListsOnlyTheRestrictedChild_AndReturnsTheOtherWithTheSameCode` (same code on both).
- **AC4** (push must not fire): `ARestrictedOrUnreadableSource_AndARestrictedChild_WithNoFilingChange_IsWritten` (2; the child is
  read for its Access Permission only), `ARestrictedSecureSource_AndAnIsolatedRestrictedChild_IsWritten`,
  `AnOrdinarySourceThatIsNotRestricted_IsWritten_AndReadsNoChild` (Standard, Limited, no value; read count `Times.Never`),
  `ASourceTableWithoutTheColumn_IsNotAskedForIt_AndReadsNoChild`, `TheSourceRead_AsksForTheAccessPermission_InItsOneRead`
  (`Times.Once`), plus the existing `AnOrdinarySource_IsUnchanged_AndNoChildOwnerIsRead`.
- **AC5** (fail closed): `ARestrictedSource_AndAChildThatCannotBeRead_SkipsTheChild_WithTheUndeterminedCode` (read faults, does
  not come back), `ARestrictedSource_ACancelledChildRead_IsRethrown`; every existing Repair and push test passes unchanged.

Fail-then-pass (classes `RecordRepairModeTests`, `FieldMappingPushSourceGateTests`, `FieldMappingPushFamilyContractTests`,
201 tests): with the rule switched off (both writers restored to the base commit, keeping only the new constants so the tests
compile; 022c's pure members in Mapping.cs kept) **31 failed, 170 passed** — every must-fire case of AC1, AC3 and AC5, the
two source-read tests, the two route tests, and the push must-not-fire cases that pin the child read. All 146 existing tests
passed both ways. Restored: **201 passed**.

Verification:
- `dotnet build src/server/api/Sprk.Bff.Api/` and the BFF test project: 0 warnings, 0 errors.
- Filtered BFF suite `FieldMapping|RecordRepair|RecordCreation|MappingSource|RestrictedSource|InheritedAccessPermission`
  (`&FullyQualifiedName!~Api.Office.`): **1607 passed**, 0 failed.
- Broader filtered BFF suite `Push|ChildRecord|CoreAncestor|SecureRoot|SecureChild|Event|Analysis|Document|RecordFamily|
  RecordOwnership` (`&FullyQualifiedName!~Api.Office.`): **9279 passed, 1 skipped**, 0 failed.
- `dotnet build Spaarke.sln`: 0 warnings, 0 errors. `dotnet test tests/Spaarke.ArchTests/`: **2530 passed**.
- Publish-size delta and CVE check: the main session's (no package added).
- `dotnet format whitespace --folder --verify-no-changes` on the ten touched files: clean.

## Known limits

**K: Restricted-ness is decided at the write** (unified-access-control-r2, 2026-11-12; the same text is in the 022b notes).
The rule maps a Restricted source's values only into a row known to end Restricted at the time of the write (unified-access-control-r2's rule, 2026-11-08). A target whose Access Permission is later loosened keeps the values mapped into it. That happens in one of two ways:
- **By hand:** a deliberate act by a user with the rights to change it.
- **By its parent:** inherited values follow the parent down, but a source that is not that parent leaves its values in place.

Nothing strips them again later; no code is written for this.

**K: a row whose stored value has not yet been raised reads as not Restricted** (unified-access-control-r2, 2026-11-12). A
row under a Restricted parent whose stored `sprk_accesspermission` has not yet been raised (the cascade or reconcile has not
reached it) reads as not Restricted, so its mapping is skipped: a missed mapping, never a write into the wrong row. A later
Repair or push maps once the stored value is Restricted.

**K: time of check (push).** The child's Access Permission is read just before its write, with its owner and filing; a change
between the read and the write is not seen (the same stated limit as the secure gate's owner check).

**K: a source-only table (the agreement) as a target is never Restricted.** `StoredRowIsRestricted` uses the target set
(`CarriesColumn`), which leaves out `InheritedAccessPermission.SourceOnlyTables`: an agreement takes no enforcement from its
value (022b, UAC-r2 Q1), so a Restricted source maps nothing into one (fail closed).

**K: the push texts for an unreadable source and for a re-file name "secure".** The POML reuses `UndeterminedReasonCode` and
`RefileNotAllowedReasonCode` with their existing texts, which say "whether the source record is secure" and "a push from a
secure record". A Restricted or unreadable source that is not secure gets those same texts.

**K: the client does not know the new push code yet.** `recordFamilyPushService.ts` maps only the three secure-source codes.
On the apply, the panel shows the server's fixed text for the new code (`pushErrorText` falls back to `error`). On the
preview, a child with the new code is not listed with values (correct) but is not counted in the skip lines either
(`isSourceRuleSkip` does not know it), so the person is not told it will not be updated. Client change is outside this task's
scope (BFF only); reported to the operator for a follow-up.

## Found in passing

- The client gap above (`src/client/shared/Spaarke.UI.Components/src/components/RecordFamilyModal/recordFamilyPushService.ts`,
  `SOURCE_RULE_TEXT` and `sourceRuleSkipText`): the new code needs its line there.
