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
6. **Unchanged.** The secure copy gate's verdicts and codes; the create (022b); `SecureRootInheritance.cs` and UAC-r2's files.
   The undetermined and refile TEXTS were reworded on the main session's instruction so they are true for a secure and a
   Restricted source (below); the refile LOG line now also names the source's Access Permission.

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
value (022b, UAC-r2 Q1), so a Restricted source maps nothing into one: the rule refuses, and never writes a Restricted
source's values into a row that is not Restricted.

**K: an unknown source option reads as Unreadable.** A source whose `sprk_accesspermission` holds an option that is not
Standard, Limited or Restricted (or a value of another type) is Unreadable (`MappingSourceAccessOf`), so the push answers its
children with `UndeterminedReasonCode`, whose text ends "Try again later". Trying again does not help until the source's value
is one of the three options; nothing is written meanwhile (fail closed).

## Follow-up in 022c (main session, 2026-11-12): the client code and the wording

Two items first recorded as known limits change what a person sees, so they were fixed here.

1. **Client knows the new code.** `recordFamilyPushService.ts` exports `RESTRICTED_SOURCE_NOT_RESTRICTED_REASON_CODE`
   (`field_mapping.push.restricted_source_not_restricted`) beside the three secure-source codes, with its `SOURCE_RULE_TEXT`
   entry (the server's text, exactly). The preview's skip lines count it ("N record(s) will not be updated because the source
   record's access is Restricted and its/their access is not."), and the apply shows its text, as for the other three codes.
2. **Wording true for both kinds of source.** The codes are unchanged; only the texts change, on server and client in step:
   - `UndeterminedMessage`: "No fields were copied to this record because it could not be checked whether the source
     record's access allows copying. Try again later." (preview line: "... will not be updated because it could not be
     checked whether the source record's access allows copying. Try again later.")
   - `RefileNotAllowedMessage`: "No fields were copied to this record because its filing can't be changed by a push from a
     secure or Restricted record." (preview line: "... filing can't be changed by a push from a secure or Restricted record.")
   - `NotIsolatedMessage` and the Restricted text are unchanged. None names a record.
3. **Parity.** The server pins each code's exact text (`FieldMappingPushSourceGateTests.EachCodesText_IsTheFixedTextTheClientShows`,
   4 cases) and the codes (`TheCodes_AreUnchanged`); the jest suite pins the same strings
   (`RecordFamilyActionPanel.test.tsx`, "reads the reason code of an entry and maps each source-rule code to its wording").
   The .NET tests that compare against the constants (`FieldMappingPushFamilyContractTests`, `FieldMappingPushSourceGateTests`,
   `RefileWriterBindShapeTests`) follow the constants and needed no change.

Jest (`RecordFamilyActionPanel.test.tsx`): 3 new tests (the preview says the record will not be updated, the apply shows the
Restricted text, the skip-line count singular and plural) and 5 updated (the undetermined and refile texts on the preview and
the apply, the parse test and the count test).

Evidence for the follow-up:
- Jest, `src/components/RecordFamilyModal` (2 suites): with `recordFamilyPushService.ts` at the base commit plus only the new
  exported constant (so the tests compile), **8 failed, 79 passed**; restored, **87 passed**.
- Server texts: with the earlier 022c gate (old wording), `FieldMappingPushSourceGateTests` + `FieldMappingPushFamilyContractTests`
  + `RefileWriterBindShapeTests` **2 failed, 145 passed** (the undetermined and refile text pins); restored, **147 passed**.
- Filtered BFF suite `FieldMapping|RecordRepair|RecordCreation|MappingSource|RestrictedSource|Refile|RecordFamily`
  (`&FullyQualifiedName!~Api.Office.`): **2144 passed**, 0 failed.
- `dotnet build Spaarke.sln`: 0 warnings, 0 errors. `dotnet format whitespace --verify-no-changes` on the two touched .cs
  files: clean.
- Shared library: ESLint and Prettier clean on the two touched files. `tsc --noEmit -p tsconfig.json` (no `typecheck` script)
  reports 9 errors, none in `RecordFamilyModal`: all in `AccessGrantModal/types.ts`, `services/document-upload/*`,
  `EntityCreationService.ts` and `useWizardPageBootstrap.ts`, which cannot resolve the sibling packages `@spaarke/auth` and
  `@spaarke/sdap-client` (not built in this worktree), plus three `unknown` errors that follow from them. The tsconfig
  excludes tests; ts-jest type-checks the test file in the jest run above.

UAC-r2 accepted the reworded refile and undetermined texts (2026-11-12). The POML's "no other change" constraint is amended to
say so: codes unchanged, those two texts reworded for both kinds of source on the main session's instruction.

## Fix round 2 (2026-11-12): access columns are never mapping targets

**Decision: access columns are never mapping targets (unified-access-control-r2, 2026-11-12).** The secure flag
(`sprk_issecure`), the Access Permission (`sprk_accesspermission`), the access inheritance record (`sprk_accessinheritance`),
the security business unit (`sprk_securitybu`) and the container (`sprk_containerid`) are never a field-mapping target, on any
table and from any source. Reason: each decides who can see a record, and changes only through the access endpoints and the
stamp / reconcile path; a mapping rule is configuration applied to every row it reaches, which is not that path.

- **One list.** `RecordCreationService.AccessColumnsNeverMapped`. Repair keeps using it (`ColumnsRepairNeverMaps` adds each
  column with `MappedValueConfersAccess`, as before; only the inline array became the list). The push does NOT take the whole
  `ColumnsRepairNeverMaps`: the regarding pair still pushes (a push re-files, FR-23), and the Assigned and display-copy
  columns keep their push behaviour.
- **The push.** `FieldMappingEndpoints.IsAccessColumnNeverMapped` (the list, matched by `SecureRootInheritance.NormalizeColumn`,
  so a Web API key or any case matches) is asked in two places: `PushMayWrite` (the preview's plan leaves the column out of
  `changes`, as for every refused target) and the apply's per-rule loop, which skips the rule VISIBLY: a `FieldResults` entry
  with `Status = Skipped` and `ErrorMessage = AccessColumnNotMappedMessage`, which carries the code
  `field_mapping.push.access_column_not_mapped`. The rest of the push applies. A document's own access columns keep their
  earlier refusal and text (`RefuseAppOnlyWrite` is asked first). The code is in the `PushFieldMappingsResponse.FieldResults`
  and `FieldMappingResultDto.ErrorMessage` doc comments. The client reads neither `fieldResults` nor the rule text, so it needs
  no change.
- **Saving a rule: not built.** The brief asked that saving a rule refuse such a target "at the rule-target refusals
  (FieldMappingEndpoints.cs ~1362)". No BFF path saves a field-mapping rule: the field-mapping endpoints are the profile reads,
  `POST validate` (type compatibility only, no callers) and the push; rules are authored in Dataverse (and seeded by the
  provisioning seeder). Line ~1362 is the push's per-rule refusal, which is where the skip was added. A save-time refusal needs
  a decision (a new save path, the seeder, or the push skip as the one enforcement); reported to the main session.
- **End-value check: not built** (main session, superseded). The push can no longer carry `sprk_accesspermission` in its
  payload (the pin `AccessColumnPushTargetTests.EveryAccessColumn_IsRefusedByTheColumnsAPushMayWrite` and the apply test
  prove it on every table), so the child's stored value is the value it ends with, and the Restricted check reads that.
  Repair needs nothing for this: `ColumnsRepairNeverMaps` already keeps the column out of its payload.
- **F3.** `ARestrictedSecureSource_AndAnIsolatedRestrictedChild_IsWritten` now verifies the one child read asks for both
  `ownerid` and `sprk_accesspermission` (the test double returns every column, so the result alone could not show it).

Section 11 answers:

| Member | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `RecordCreationService.AccessColumnsNeverMapped` | `ColumnsRepairNeverMaps`'s inline access-column array | Lifted out of it, not added beside it: Repair and the push read the one list | The push and Repair would hold separate lists that drift, and a push could write a column that decides access |
| `FieldMappingEndpoints.AccessColumnNotMappedReasonCode` + `AccessColumnNotMappedMessage` | The writer-only column skip text (`ChangedOnlyByItsWriterColumnReasonCode`) | A different rule on every table; reusing that code would misreport why the rule was skipped | A stored rule aimed at an access column would be written, or skipped with no stated reason |
| `FieldMappingEndpoints.IsAccessColumnNeverMapped` | `PushMayWrite` | Asked by `PushMayWrite` and the apply loop, so both answer the same | The preview and the apply could disagree on a column |

Tests:
- New `tests/integration/data-mutation/ChildAccessPermission/AccessColumnPushTargetTests.cs`: must fire, a Copy rule onto
  `sprk_accesspermission` on `sprk_event`, `sprk_todo`, `sprk_communication` (and a mixed-case key) skipped with its code while
  the other column is pushed; every list column on six tables skipped with nothing written (30); the pin, every list column
  refused by `PushMayWrite` (30); the list's exact members; the code. Must not fire: an ordinary column on the three tables is
  pushed; the regarding pair is not an access column and `PushMayWrite` allows it (FR-23).
- Updated to the ruling (they pinned the earlier behaviour, where the push left these columns to the caller's privileges):
  `PushRuleSelectionTests.AnOrdinaryColumn_IsWritable_AndNoTableTakesItsAccessPermissionFromAPush` (a matter's Access
  Permission is no longer writable by a push), `DocumentAccessPermissionPushTargetTests.APushRule_TargetingTheSameColumnOnAToDoOrAnEvent_IsSkipped_AsAnAccessColumn`,
  `AccessTableAppOnlyWriterTests.APushRule_OntoAColumnTheCallersPrivilegesDecide_IsSentAsTheCaller` (the matter and project
  access-column rows removed; the access-table and contact rows stay), and
  `SecureRootInheritanceWriterTests.FieldMappingPush_OntoTheSecureFlag_IsSkipped_AndWritesNothing` (skipped with the code,
  before the filing gate, instead of failing the record; nothing written either way).

Evidence for fix round 2:
- Access-column rule off (`IsAccessColumnNeverMapped` answering false): `AccessColumnPushTargetTests`, `PushRuleSelectionTests`,
  `DocumentAccessPermissionPushTargetTests`, `AccessTableAppOnlyWriterTests.APushRule*`,
  `SecureRootInheritanceWriterTests.FieldMappingPush*`: **68 failed, 51 passed** (the passes are the must-not-fire cases, the
  list and code pins, and the document refusals that answer first); restored, all pass.
- F3: with the access column dropped from the child read when the source is secure, `FieldMappingPushSourceGateTests`
  **1 failed** (`ARestrictedSecureSource_AndAnIsolatedRestrictedChild_IsWritten`), **68 passed**; restored, 69 passed.
- Filtered BFF suite `FieldMapping|RecordRepair|RecordCreation|MappingSource|RestrictedSource|Refile|RecordFamily|
  ChildAccessPermission|PushRuleSelection|SecureRootInheritanceWriter` (`&FullyQualifiedName!~Api.Office.`): **4369 passed**.
- Broader filtered suite (round 0's set, `&FullyQualifiedName!~Api.Office.`): **9350 passed, 1 skipped, 3 failed**; the three
  (`ChatMessageStreamingContractTests`, `RagEndpointsAuthorizationContractTests`, `DocumentEmailIdentityContractTests`, each
  2.5 to 3 minutes under machine load) pass when run alone (6 passed), and touch no field-mapping code.
- `dotnet build Spaarke.sln`: 0 warnings, 0 errors. `dotnet test tests/Spaarke.ArchTests/`: **2530 passed**.
- `dotnet format whitespace --folder --verify-no-changes` on the ten touched .cs files: clean. No client change, so no jest run.
