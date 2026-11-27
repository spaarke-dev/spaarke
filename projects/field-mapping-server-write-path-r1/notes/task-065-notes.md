# Task 065: copy a mapped value from the filing parent whatever its source column's field security

Owner decision 2026-11-26 (`decisions.md`): "if two records are related and there is field mapping then the field should copy write - security does not matter."

## 1. The rule after 065

| Path | Field-secured source column |
|---|---|
| Create, source = the caller's filing record (table and id) | Copied. Still read as the caller (`MappingSourceCallerMask`), so only a value the caller can read is copied. |
| Create, any other source (explicit `mappingSource` not the filing record, id match with another table, unresolvable pair) | Not copied: dropped with the `SourceColumnFieldSecured` warning. "Security cannot be read" maps nothing (fail closed). |
| Push (ordinary mode: the rows whose single non-citation lookup to the source table names it; family mode: the Filing edge) | Copied. The source is read as the caller (`RetrieveSourceRecordValuesAsCallerAsync`). The push no longer reads `IsSecured`. |
| Repair pass (system principal) | Not copied, unchanged: there is no caller to mask the read against. |

Once copied, a value is readable by whoever can read the target column. To keep it restricted, field-secure the target column too.

Unchanged: the mapped filing lookup keeps the caller's AppendTo check (refuse); invariant and access-conferring columns keep the caller's checks; sources outside the filing chain and adapter defaults keep them; the FR-04 Read probe; `EnforceMappingSourceOwnerAsync`; the final whole-row check.

## 2. Commits

- 1ffefa6629 (merged as 2a978a9e55): create step 3 lifts `FieldSecuredSourceRule.Split` for the filing record, decided once and reused at step 5; push step 2a removed (an exact revert of 08ba672339 plus a rule comment); `FieldMappingPushLookups.SourceAttributeMetadataQuery` / `SecuredColumns` deleted; ArchTest guard now requires `Split` on create only; docs.
- 74efbf10ee: `NormalizedRule` test follows 064d (a defect from 064d, found here; also PR #3296 to master).
- 934c1a2a89 (merged as acd0470942): review fixes (below).

## 3. Must-fire tests by boundary

- Field-secured source copies on create: `CreateAsync_AValueFromAFieldSecuredSourceColumnOfTheFilingParent_TheCallerCanRead_IsWritten_WithNoWarning`, `..._IntoAFieldSecuredTarget_IsWritten`, `CreateAsync_FromTheFilingParent_WhenWhetherTheSourceColumnsAreSecuredCannotBeRead_TheValuesAreStillMapped`, `Task065_AFieldSecuredSourceColumnOfTheSourceResolvedFromTheRegardingPair_TheCallerCanRead_IsWritten_WithNoWarning`, `CreateAsync_AMappedFieldSecuredValueFromTheFilingParent_IsWritten_ButACallersFieldSecuredValue_Refuses`.
- Copies on push: `Push_ARuleWhoseSourceColumnIsFieldSecured_PushesTheValueTheCallerCanRead_AndIsNotCountedAsSkipped`, `Push_ARuleWhoseSourceColumnSecurityCannotBeTold_PushesLikeAnyOther`.
- A copy reads only the one source record, as the caller: `Push_ACopyReadsOnlyTheOneSourceRecord_AsTheCaller_AndAsksNoColumnSecurity`, `Push_ReadsTheSourceFieldsAsTheCaller_NeverAppOnly`.
- Table AND id: `Task065_AFieldSecuredSourceColumnOfAnExplicitSourceWithThePairsIdButAnotherTable_IsNotCopied_WithTheFieldSecuredWarning` (fails under an id-only mutation).
- Non-filing source keeps the drop and the fail-closed fallback: `CreateAsync_AValueFromAFieldSecuredSourceColumnOfASourceThatIsNotTheFilingParent_IsDroppedWithAWarning_AndTheRowIsCreated`, `..._IntoAFieldSecuredTarget_IsNotCopied` (now asserts the reason), `CreateAsync_FromASourceThatIsNotTheFilingParent_WhenWhetherTheSourceColumnsAreSecuredCannotBeRead_NothingIsMapped_AndTheRowIsCreated`, the `Task064c_*` field-secured tests, `Task064d_ARuleWhoseSourceFieldIsSpelledInMixedCaseWithTrailingSpace_IsCheckedForFieldSecurity`.
- Filing lookup AppendTo (refuse): `CreateAsync_AMappedFilingLookupTheCallerCannotAppendTo_IsRefused_AndNothingIsWritten`, `Task064c_AFilingLookupMappedFromTheFilingParent_TheCallerCannotAppendTo_IsRefused`, `MappedCopyRightsTests.AFilingLookup_KeepsTheCallersChecks`.
- Invariant and access-conferring columns: `CreateAsync_AMappedAssignedToFromTheFilingParent_TheCallerCannotAppendTo_IsStillDropped_TheOtherValuesAreWritten`, `Task064c_AnAccessConferringRegistryColumnWithoutThePrefix_KeepsTheCallersChecks`, `Task064d_WhenTheAccessConferringRegistryCannotBeRead_AValueFromTheFilingParent_KeepsTheCallersChecks`, `MappedCopyRightsTests.AnInvariantColumn_IsNeverCopiedWithoutTheCallersChecks_FromTheFilingParentEither`.
- Source outside the filing chain: `CreateAsync_AnOrdinaryMappedLookupFromASourceThatIsNotTheFilingParent_TheCallerCannotAppendTo_IsStillDropped`, `Task064c_AnExplicitSourceWithThePairsIdButAnotherTable_KeepsTheCallersChecks`.
- Adapter defaults: `Task064c_AnOrdinaryAdapterDefaultTheCallerCannotAppendTo_IsDroppedAsADefault`, `MappedCopyRightsTests.AnAdaptersDefault_KeepsTheCallersChecks`.
- FR-04 Read probe: `CreateAsync_ASourceTheCallerCannotRead_MapsNothing_EvenAnOrdinaryValue`.
- Caller mask: `CreateAsync_AFieldSecuredSourceColumnOfTheFilingParent_TheCallerCannotRead_CopiesNothing_LeaksNothing_AndDoesNotRefuse`, `CreateAsync_ASourceColumnTheCallerCannotRead_IsNotCopied_ButTheReadableOnesAre`, `Push_ASourceColumnTheCallerCannotReadUnderFieldSecurity_IsNeverCopiedIntoAChild`.
- Secure-source owner rule: `MappingSourceRule_*` tests.
- Final whole-row check: `CreateAsync_WhenTheRowAsWrittenFailsTheFinalWholeRowCheck_TheCreateIsRefused_AndNothingIsWritten` (new; no earlier test made it refuse on its own).

Reverting the four 065 production files fails 8 tests (Opus review, throwaway worktree).

## 4. Reviews

- Opus and Fable adversarial passes on the diff: no F1, F3 or F4.
- Fixed (934c1a2a89): F2-1 (no test pinned "table AND id": added, plus a pair-resolved positive case); F2-2 (a moved drop test asserted only code and column: now asserts the reason; it fails when only the reason changes); F2-3 (stale comments in `RecordCreationService.Mapping.cs`, `FieldSecuredSourceRule.cs`, `FieldMappingEndpoints.cs`).
- Known limits:
  - K1, push "parent": in ordinary mode the pushed rows are those whose single non-citation lookup names the source, which need not be their filing lookup. This matches the owner's "two records are related" and is stated in the framework doc; create still lifts only for the filing record.
  - K2, Repair: never copies a field-secured source column (no caller). Stated in the framework doc. Owner question open: should it follow the new rule?
  - K4, data masking: a field-secured column with a Dataverse masking rule may copy its masked text for a caller with masked read. No masking rule exists in the repo or on dev as far as known. Stated in the admin guide.
  - K4, push: relies on Dataverse returning null for a secured column the impersonated caller cannot read (documented platform behaviour). Dev check after deploy: a user without read on the secured column pushes, children unchanged.
  - K3: the `IsSecured` read still runs for filing-parent creates though unused there (parallel, memoised; no measurable cost).

## 5. Gates

On the merged tip acd0470942:
- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- Filtered `Sprk.Bff.Api.Tests` (RecordCreationServiceMappingSeamTests, FieldSecuredSource, CallerWriteAuthorization, MappedCopyRights, FieldMappingPush, RecordCreationMappedValueTests, MappingSourceCallerMask, OfficeQuickCreateSourceFieldSecurity, BudgetInquiry, RecordRepair): 1304/1304.
- Jest `secureChildRibbonScript.test.ts`: 67/67.
- `Spaarke.ArchTests` (no `--no-build`): 2854/2854.
- Publish delta, 065 + 066 (74efbf10ee) vs master 8975e13034, fresh builds, Compress-Archive: −35,679 bytes, 41.66 MB. `dotnet list package --vulnerable --include-transitive`: none.
- UAC-r2 gate: requested (their four checks: only the field-secured-source rule changes and every other boundary keeps a must-fire test; must-fire on create AND push; the decision record quotes the owner and states the consequence; a copy reads only the one parent's data).
