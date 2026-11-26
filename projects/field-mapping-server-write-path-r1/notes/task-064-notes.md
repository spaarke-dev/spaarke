# Task 064 notes: copy ordinary mapped values regardless of the caller's rights (owner 4b/4c)

Rigor: FULL (tags bff-api, security, auth). Model: Opus @ high, run as an implementer sub-agent. Branch `task/fm-064`, cut
from `f8eabbbeaa`. Nothing pushed; no GitHub write; no live write; nothing under `.claude/`; no UAC-r2 file and none of
`ChildCreateExecutorBase`, `ChildRecordEndpoints`, `closureService.ts`, `CloseProjectDialog.tsx` edited. Inputs read: the
POML, decisions.md (2026-10-27 × 2, 2026-11-26), project CLAUDE.md, spec FR-04/FR-06/FR-08, `Sprk.Bff.Api/CLAUDE.md` scope,
and the coordinator's UAC-r2 additions A–C (2026-11-26, in this task's brief).

## 1. What changed (create path only)

Line numbers are at the branch tip; `M` = `Services/RecordCreation/RecordCreationService.Mapping.cs`,
`S` = `Services/RecordCreation/RecordCreationService.cs`.

| Rule | Where it is enforced |
|---|---|
| 4b/4c: an ORDINARY mapped value from the filing parent is written without the caller's AppendTo / field-security check | the one predicate `MayCopyWithoutCallerChecks` (M ~514); step 5 keeps such a value unasked (M ~1283-1293) and leaves it out of the final "row as written" check (M ~1329-1346) |
| Boundary 1: a mapped FILING lookup keeps the caller's AppendTo check; a failure refuses | unchanged: the caller's values + mapped filing lookups are asked together and refuse (M ~1264-1277); the predicate excludes `isFilingLookup` (M ~516); K-2 server-set guard unchanged (S ~510) |
| Boundary 2: BFF-owned invariant columns keep the caller's checks (dropped on failure), whatever the source | closed list `CopyInvariantColumns` (extends `AccessConferringColumns`, M ~1374) + `CopyInvariantPrefixes` (`sprk_assigned`, `sprk_regarding`, `sprk_related`, M ~489) + every column `InheritedAccessPermission.RefuseAppOnlyWrite(AppOnly)` refuses; predicate `IsCopyInvariantColumn` (M ~497). Most are not mapped at all (unchanged: `ProtectedFromMapping` M ~395, `DropAppOnlyRefusedRules` M ~910) |
| Boundary 3: a value from a field-secured SOURCE column is never copied (dropped with a warning) | `SplitFieldSecuredSourceRules` (M ~461), applied in step 3 (M ~388-416): the engine applies only the copyable rules; the secured-source rules run once more on a copy of the row (`CopyOf`, M ~481), after the copyable ones, only to name each column they would still have filled. The masked source reaches only the two engine calls (#2188 guard); the source columns' `IsSecured` read as the caller, beside the target-columns read (`SecuredColumnsAsync`, M ~940-1030); unreadable → nothing mapped, one `MappingUnavailable` warning (fail closed) |
| Boundary C (new): the skip applies only to a source that is the caller's filing parent | `SourceIsCallersFilingParent` (existing, M ~563) computed once (M ~361), carried on `MappingStep.FromFilingParent` (M ~91, ~435) and `mappedFromFilingParent` (S ~535, ~573, ~752) |
| FR-04: source Read as the caller | unchanged (`ReadMappingInputsAsync` probe; `MappingSourceCallerMask` M ~344) |
| FR-08: the caller's OWN values | unchanged: asked first, alone; a failure refuses, a fault propagates (M ~1221-1238) |
| Adapter defaults (step 3½) and copied defaults | keep the caller's checks (`isDefault` in the predicate) |

ADR-008 path A: every check is still `CallerWriteAuthorization.CheckCallerMayCreateAsync`, asked by the service; this task
only decides which mapped values are asked. Route-level authorization stays in endpoint filters. Cite in the PR:
"ADR-008 path A — payload-dependent create checks live in `CallerWriteAuthorization`, called by the service (project
CLAUDE.md §2, spec ADR Tensions)."

## 2. Which sources step 5 reads from (coordinator C)

The mapping source is resolved by `MappingSourceResolver.ResolveAsync`: an explicit `mappingSource` (resolver line ~93,
`MappingSourceOrigin.Explicit`), else the regarding pair (~113/~133), else the single typed parent (~141). Pair and typed
parent are always the caller's filing parent. An explicit source is a filing parent only when one of the caller's own
filing parents names it (`SourceIsCallersFilingParent`). Explicit sources that need NOT be the filing parent:

- the child-record route's `mappingSourceEntity` / `mappingSourceId` (`Api/ChildRecordEndpoints.cs` ~296-311);
- the Office create's `SourceEntityType` / `SourceRecordId` (`Services/Office/OfficeService.cs` ~2599-2605);
- any caller of `RecordCreationMapping.ForCaller(probe, token, sourceTable, sourceId)`.

So some sources are not the filing chain: the conservative split is implemented (fail closed). A value mapped from such a
source keeps today's caller checks and drop. **For the owner (via the main session):** this narrows "regardless of
permissions" to values from the filing parent. Also narrowed: the core ANCESTOR of the filing parent is not treated as
filing chain (only the direct filing parent is), because the ancestor is derived after step 5 (step 6½); an explicit source
naming the ancestor therefore keeps the caller's checks.

## 3. Push

Push never had the per-value drop. It writes each child AS THE CALLER (`MSCRMCallerID`): `Api/FieldMappings/FieldMappingEndpoints.cs`
~1629 (`UpdateExistingRecordFieldsAsync(..., callerSystemUserId, ...)`) and ~1613 (invoice family, `UpdateExistingSplitAsync`
with the caller's rights), so Dataverse applies the caller's Write, AppendTo and field security to the whole child; a
failure fails that child, it never drops one value. The source is read as the caller (~603). Push is therefore unchanged:
every pushed value keeps the caller's rights (stricter than create's new rule). If the owner wants 4b/4c on push too, it is
a separate change (an app-only push write), not a predicate swap.

**Push and boundary 3 (rule):** boundary 3 applies on push as on create. A field-secured source column is copied only
into a target secured by the same field-security profile set. The push reads the source attribute catalog
(`FieldMappingPushLookups.AttributeMetadataQuery`), which is where that check belongs. This rule is not built on push in
this task. It is tracked with unified-access-control-r2, which owns the access review, and it depends on the
profile-set question in section 4.

## 4. Coordinator items A–C

- **A (closed list):** done as described above. Existing set extended (`AccessConferringColumns`), not a second one. The
  Assigned family is prefix-matched. The No Access list is a table (`sprk_noaccessentry`, in `InheritedAccessPermission.AccessTables`),
  so any column of it is covered by the `RefuseAppOnlyWrite` arm; contact identity / binding likewise (`AccessColumns[contact]`).
  **Deviation for the owner:** the list's columns keep TODAY's caller checks (dropped when they fail), rather than being
  dropped always. Dropping always would end two standing decisions: Assigned-To Copy rules at create (the registry's 15
  targets, 2026-10-22, decision 4a keeps them One-time) and a document's Access Permission fill at create (UAC-r2 D7,
  `IsFillOnlyAtCreateExemption`). Behaviour for these columns is exactly as before this task (no widening). If UAC-r2 wants
  "never copied", that is a one-line change in `MayCopyWithoutCallerChecks`'s caller (drop instead of ask), but it needs the
  owner because of 4a/D7.
- **B (profile-set comparison): not built.** No existing read resolves a column's security-profile set (the nearest,
  `SecureRootInheritance.Cascade.PlatformReadableTablesAsync` and `SecuredColumnReadProof`, read the BFF's / a user's
  `fieldpermission` grants, not a column's profile set; Dataverse also refuses a `fieldpermission.entityname` filter by
  logical name, so the read would page all `fieldpermission` rows for the column). Interim, fail closed: a value from a
  secured source column is never copied, into any target (secured→unsecured: dropped with `SourceColumnFieldSecured`;
  secured→secured: also dropped, which is exactly what happened before this task, because the step-5 check refused every
  field-secured mapped column). The profile-set read is only needed to ENABLE secured→same-profile copies. Decision for the
  main session / owner.
- **C (filing chain):** done (section 2).

## 5. Tests

New: `tests/integration/seam/RecordCreation/RecordCreationServiceMappingSeamTests.Task064.cs` (9),
`tests/unit/domain/RecordCreation/MappedCopyRightsTests.cs` (36 cases). Updated: the "#1617" section header and the
Assigned-To test's summary in `RecordCreationServiceMappingSeamTests.cs` (no test deleted or rewritten; the old #1617
Assigned-To drop now pins boundary 2).

| Test | Before (old prod code) | After |
|---|---|---|
| AnOrdinaryMappedLookupFromTheFilingParent_TheCallerCannotAppendTo_IsWritten (AC1) | FAIL | pass |
| AnOrdinaryMappedValueIntoAFieldSecuredTarget_FromAnUnsecuredSourceColumn_IsWritten (AC2) | FAIL | pass |
| AValueFromAFieldSecuredSourceColumn_IntoAnUnsecuredTarget_IsDropped (boundary 3) | FAIL | pass |
| WhenWhetherTheSourceColumnsAreSecuredCannotBeRead_NothingIsMapped (boundary 3, fail closed) | FAIL | pass |
| AMappedAssignedToFromTheFilingParent_..._IsStillDropped_TheOtherValuesAreWritten (boundary 2 + 4b/4c together) | FAIL | pass |
| AValueFromAFieldSecuredSourceColumn_IntoAFieldSecuredTarget_IsStillNotCopied (B interim) | pass | pass |
| AnOrdinaryMappedLookupFromASourceThatIsNotTheFilingParent_..._IsStillDropped (C) | pass | pass |
| ASourceTheCallerCannotRead_MapsNothing (FR-04) | pass | pass |
| ACallersOwnOrdinaryLookupTheyCannotAppendTo_StillRefuses (AC5) | pass | pass |
| existing: AMappedFilingLookupTheCallerCannotAppendTo_IsRefused (AC3, boundary 1) | pass | pass |
| existing: #1617 AMappedLookupTheCallerCannotAppendTo_IsDropped (Assigned-To, boundary 2) | pass | pass |
| MappedCopyRightsTests (36) | does not compile (new members) | pass |

Mutation proofs (on the new code, each reverted): the filing-chain condition removed → 2 fail (the C seam test and the
unit test); the `sprk_assigned` prefix removed → 10 fail (unit must-fire rows, the closed-list test, the Assigned-To seam
tests and three existing seam tests that rely on an Assigned-To drop); the secured-source split disabled → 3 fail (both
boundary-3 seam tests and the existing `AMappedFieldSecuredValue_IsDropped_ButACallersFieldSecuredValue_Refuses`). The
closed-list test fails if a named entry is lost.

The pass-before tests are must-not-regress tests for boundaries that were already enforced; their failing mutation is above
(C) or is the pre-existing boundary's own guard.

## 6. Gates

- Build `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- Filtered tests (`RecordCreation|FieldMapping|Push|MappedCopyRights`): 1765/1765 pass (1720 before the change).
- ArchTests (`dotnet test tests/Spaarke.ArchTests`, no `--no-build`): 2845/2845 pass. Two guards fired on the first draft and
  were satisfied by fixing the code shape, not by exempting it: #2188 (the masked source may reach only engine calls, so the
  secured-source split takes no entity and the warning comes from a second engine call on a row copy); 056 census
  (`IsCopyInvariantColumn` asks `RefuseAppOnlyWrite`, listed as a create-only predicate that writes nothing, ADR-002).
- `CreatePath_ANamedSourceMappedCreate_WithDelayedReads_...` failed once in the old-code run and once in a mutation run and
  passed in every full run: a timing test under machine load (K2, pre-existing).
- Publish delta, CVE check, adversarial review (two passes, security tag) and the UAC-r2 diff review: main session.

### Self code-review (Step 9.5)

- F-class: one fixed in scope — the class remarks of `RecordCreationService.Mapping.cs` described step 5's old drop-everything
  rule (F2, comment contradicting code); rewritten.
- K2: a Copy rule with a default value whose source column is secured is skipped whole, its default included (fails closed).
- NFR-02: one more caller metadata read per mapped create (source columns' `IsSecured`), run concurrently with the
  existing target-columns read; when step 5's whole-row check fails and a value was copied unchecked, one more remap of the
  checked subset. No new round trip on the common path where the whole row passes.
- §11 component justification: `MayCopyWithoutCallerChecks` / `IsCopyInvariantColumn` — Existing: `IsNeverAReference`,
  `AccessConferringColumns`, `ProtectedFromMapping`; Extension: the invariant list extends `AccessConferringColumns`;
  Cost of doing nothing: the owner's 4b/4c rule cannot be applied without one predicate naming what keeps the checks.
  `SecuredColumnsAsync` — Existing: `CallerWriteAuthorization.NoFieldSecuredColumnAsync` (private, answers only "first
  secured column" for the target) and `MappingColumnMetadata.ReadAsync` (app-only, whole table); Extension: neither answers
  a source column set as the caller without widening a shared helper; Cost of doing nothing: boundary 3 is unenforceable on
  create once the target check no longer runs.

### ADR check

ADR-002 (no plugin) ✓; ADR-008 path A ✓ (cited above); ADR-010 (no DI registration) ✓; ADR-019 (warnings name the column,
never a value or source id) ✓; ADR-028 (caller reads under the caller's token) ✓; ADR-038 (tests at KEEP paths:
`tests/integration/seam/**`, `tests/unit/domain/**`; nothing deleted) ✓.

## 7. Proposed FR-08 binding text (project CLAUDE.md §2, for the main session)

Replace the second sub-bullet of "MUST run caller checks on the merged payload":

> - a **mapped** value from the record the caller's row is filed under is written **without** the caller's AppendTo or
>   field-security check (owner 4b/4c, 2026-10-27, reconfirmed 2026-11-26; task 064), **except**:
>   - a mapped **filing** lookup (`SecureChildLineage` / `FilingParentOf`) that fails AppendTo → **refuse** (unchanged);
>   - a BFF-owned invariant column (`RecordCreationService.IsCopyInvariantColumn`: owner, secure/access columns,
>     `sprk_assigned*`, `sprk_regarding*`, `sprk_related*`, creator, file binding, external flag, contact identity, access
>     tables) keeps the caller's checks: a failure drops it with a warning;
>   - a value mapped from a source that is NOT the caller's filing parent, or an adapter's default, keeps the caller's
>     checks (dropped with a warning on failure);
> - a value from a field-secured SOURCE column is never copied (dropped with a warning), pending the profile-set rule.

And §3 "A mapped value failing AppendTo or field security is skipped with a warning." → "An ordinary mapped value from the
filing parent is copied regardless of the caller's rights (4b/4c); the exceptions are in §2 FR-08."

Follow-ons: the push field-secured source rule (DI-051, #3258) is done in section 8. rework: none task 064

## 8. 064b push follow-on (DI-051, #3258)

Branch `task/fm-064b`, cut from `ff44742744`. Rigor FULL (security). Nothing pushed; no GitHub write; no live write; nothing
under `.claude/`; none of the files the brief fences off was edited.

### 8.1 The rule on push

A push applies create's field-secured SOURCE rule (section 1, boundary 3). `M` = `RecordCreationService.Mapping.cs`,
`E` = `Api/FieldMappings/FieldMappingEndpoints.cs`, `L` = `Api/FieldMappings/FieldMappingPushLookups.cs`,
`R` = `Services/Dataverse/FieldMapping/FieldSecuredSourceRule.cs`.

| Rule | Where |
|---|---|
| ONE predicate: anything but an explicit `IsSecured == false` counts as secured | `R` `CountsAsSecured` (~33); create asks it in `SecuredColumnsAsync` (`M` ~1005), push in `SecuredColumns` (`L` ~74) |
| ONE split: a rule reading a secured source column (Copy source, or any Concat/Template placeholder) is not applied | `R` `Split` (~42), moved from create's `SplitFieldSecuredSourceRules` (not copied); create `M` ~394, push `E` ~606 |
| Push reads the source catalog once, as the caller, with `IsSecured` | `L` `SourceAttributeMetadataQuery` (~67), read at step 2a (`E` ~600); the same rows feed `RetrieveSourceRecordValuesAsCallerAsync` (no second read). The target catalog query is unchanged |
| A skipped rule's target column is named; the rule is counted with the skipped rules | `E` `SecuredSourceWarnings` (~867); `Warnings` + `SkippedRuleCount` on the apply, the no-child answer and the preview (appended to the preview's own warnings) |
| A secured column is not even read for the push | the source `$select` is built from the remaining rules only |
| Every rule secured: nothing is read or written | `E` ~614-634, the "no rule" answer with the warnings |
| Security that cannot be told | per column: no `IsSecured`, or not a boolean → secured (skipped, warned). Whole catalog read faults → the push fails before anything is read or written (unchanged: 500) |

Unchanged on push: writes as the caller (`MSCRMCallerID`), FR-23 re-file (`ReparentAsync`, root filing gate, restamp,
`AfterRefileAsync`), the invoice-family path, `MappingSourceCopyGate`, Assigned-To selection (an Assigned-To rule from a secured
source column is skipped too, fail closed).

### 8.2 Save path for mapping rules (finding)

The BFF has no endpoint that saves a mapping rule: `FieldMappingEndpoints` maps only `GET profiles`, `POST validate` (type
compatibility; writes nothing), `GET profiles/{source}/{target}` and `POST push`; no BFF source names `sprk_fieldmappingrule`
outside the shared read in `Spaarke.Dataverse`. Rules are configured on the native Dataverse forms (the project's configuration
surface). The generic caller-OBO record tools (chat create/update, Update Record node) are not a rule-save surface: they write
any table only with the caller's own Dataverse rights, the same as the native form. So no save-time refusal was added, and no
plugin, business rule or flow (ADR-002). Enforcement is at use: create (`M` step 3) and push (`E` step 2a) both skip a rule whose
source column is field-secured, so a rule saved by any path cannot copy a secured value.

### 8.3 Tests

New: `tests/integration/contract/Api/FieldMappings/FieldMappingPushAuthorizationContractTests.FieldSecuredSource.cs` (4),
`tests/Spaarke.ArchTests/FieldSecuredSourceRuleGuardTests.cs` (6). Updated (fakes only, the live catalog shape): the push hosts'
source catalog rows carry `IsSecured: false` (`FieldMappingPushAuthorizationContractTests.cs` `Attribute`, its `FakeDataverse`
rows and a `SourceColumnSecurity` knob; `FieldMappingPushDocumentSourceTests.cs`), and the expected source-catalog query string
(`Push_ToATargetWhoseLookupIsNotTheRegardingConvention_...`) gained `,IsSecured`. No test deleted.

| Test | Old push code | New |
|---|---|---|
| Push_ARuleWhoseSourceColumnIsFieldSecured_IsSkippedWithAWarningNamingItsTarget_AndTheOtherRulesPush | FAIL | pass |
| Push_ARuleWhoseSourceColumnSecurityCannotBeTold_MapsNothingFromIt_AndWarns | FAIL | pass |
| Push_WhenEveryRuleReadsAFieldSecuredColumn_NothingIsReadOrWritten_AndEachTargetIsNamed | FAIL | pass |
| Push_ARuleWhoseSourceColumnIsExplicitlyNotSecured_PushesAsBefore_WithNoWarning (must not regress) | pass | pass |
| ArchTest EachWriter_UsesTheSharedRule_AndKeepsNoPrivateCopy (×3: create, push endpoint, push catalog) | FAIL ×3 | pass |
| ArchTest NoOtherFieldMappingSource_ReadsIsSecuredItself | FAIL (create's private reading) | pass |
| ArchTest controls: must-fire (private reading; missing shared call) / must-not-fire (shared calls) | pass | pass |

Gates: `dotnet build src/server/api/Sprk.Bff.Api/` 0 warnings, 0 errors. Filtered tests (`FieldMapping|Push|RecordCreation`):
1769/1769 pass (1765 before; the 36 push tests that failed with the new code and the old fakes passed once the fakes stated
`IsSecured: false`, which is the point: an unstated security now fails closed). ArchTests (`dotnet test tests/Spaarke.ArchTests`,
no `--no-build`): 2855/2855 pass.

### 8.4 Self code-review and adr-check

- No F-class finding open. K1: a catalog read that faults still answers 500 (unchanged), not a warning; nothing is written.
- NFR-02: no new round trip (the `IsSecured` column rides the existing source catalog read).
- §11 justification for `FieldSecuredSourceRule` (new file, moved surface): Existing — create's `SplitFieldSecuredSourceRules` and
  its inline `IsSecured` reading; Extension — moved, not duplicated, to the field-mapping engine folder both writers already use;
  Cost of doing nothing — the push copies a secured value into an unsecured child column (#3258). `SourceAttributeMetadataQuery`,
  `SecuredColumns`, `SecuredSourceWarnings`: extensions of the existing push catalog read and answer.
- ADR-002 (no plugin; no save-time hook) ✓. ADR-008 path A ✓: route authorization unchanged (endpoint filters); the payload rule
  is decided by the endpoint's service-side step, as on create — cite "ADR-008 path A — payload-dependent mapping checks are
  decided server-side in the write path, not in an endpoint filter (project CLAUDE.md §2, spec ADR Tensions)". ADR-010 (no DI
  registration) ✓. ADR-019 (warnings name the column, never a value) ✓. ADR-028 (catalog read as the caller) ✓. ADR-038 (tests at
  KEEP paths; nothing deleted) ✓. BFF hygiene: no package; publish-size delta not measured here (main session).

### 8.5 Coordinator's widened scope (task-064 review findings): not on this branch

The main session asked, mid-task, for the task-064 review findings to be fixed here too (F1: the pair arm of
`SourceIsCallersFilingParent`; F2: Assigned-To classification through the shared prefix constant plus the bound registry; F2: one
metadata reader through `MappingColumnMetadata`; F2: the step-trace text; seam tests (a)-(h)). They are not on this branch:
together they exceed one implementer pass, and one of them needs a decision first. The push follow-on above is complete and
committed on its own. Proposed split, one branch each, in this order:

1. **064c: F1, F2 classification, trace text** (`RecordCreationService.Mapping.cs`, the trace in `RecordCreationService.cs`;
   seam tests (a)-(e), with the `defaultColumns` mutation note). Small and self-contained.
2. **064d: one metadata reader** (decide first). Moving create and push from the caller-read `IsSecured` predicate
   (`FieldSecuredSourceRule.CountsAsSecured`) to `MappingColumnMetadata.ReadAsync` / `CopyableSourceColumns` changes three things:
   (i) the read becomes app-only `IGenericEntityService.GetColumnMetadataAsync` instead of the caller's metadata read (metadata
   only, so probably acceptable); (ii) an unstated `IsSecured` maps nothing from the source (that gate's rule) instead of
   skipping only the rule that reads it; (iii) every host of `RecordCreationService` whose `IGenericEntityService` double does not
   answer `GetColumnMetadataAsync` (the create seam fixture's is `MockBehavior.Strict`) would then map nothing, so suites outside
   the `FieldMapping|Push|RecordCreation` filter (Office, the chat create tool, the child-record route) need checking: an
   unfiltered run or a census of the doubles. Seam tests (f)-(h) belong here. `FieldSecuredSourceRuleGuardTests` then names the
   new shared reader as the required call.

## 9. 064c: task 064 review fixes

Branch `task/fm-064c`, cut from `e35a400a00`. Rigor FULL (security). Nothing pushed; no GitHub write; no live write; nothing
under `.claude/`; none of the files the brief fences off was edited. `M` = `RecordCreationService.Mapping.cs`, `S` =
`RecordCreationService.cs`, `C` = `Services/Dataverse/CallerWriteAuthorization.cs`.

### 9.1 Rules and where they are enforced

| Rule | Where |
|---|---|
| An explicit mapping source is the filing parent only when its table and id both match a record the caller's row is filed under (a filing lookup, or the regarding pair as `MappingSourceResolver` resolves it from its record type, read as the caller). A pair whose table cannot be told is no match (fail closed) | `SourceIsCallersFilingRecordAsync` (`M` ~616), asked for step 5's skip only (`M` ~462), after step 3 has values; the record-type read happens only when the pair's id equals the source's id |
| The step-3 secure-source gate keeps its own question (`SourceIsCallersFilingParent`, id match on the pair) | unchanged; the owner check before the create (`EnforceMappingSourceOwnerAsync`) is that gate's invariant. `MappingSourceRuleTests` line 55 unchanged |
| The Assigned-To class of a mapped column is the shared prefix (`InheritedAccessPermission.AssignedToColumnPrefix`) united with the canonical and bound access-conferring registries (the push's classifier, `PushRuleSelection.AssignedToColumnsOf`, as Repair's `ColumnsRepairNeverMaps` unites them); a registry that cannot be read classes every column as Assigned-To (every mapped value keeps the caller's checks) | `CopyInvariantPrefixes` (`M` ~485), `IsCopyInvariantColumn` (`M` ~495), `AssignedToColumnsOfRegistries` (`M` ~512), asked at step 5 (`M` ~1355) |
| The caller-checks step trace names how many mapped values were copied without the caller's checks | `MappingStep.CopiedWithoutCallerChecks` (`M` ~104, set `M` ~1419), trace `S` ~762 |
| A target column whose `IsSecured` is anything but an explicit false counts as field-secured in the caller's create check | `C` `SecuredColumnIn` (~855), asking `FieldSecuredSourceRule.CountsAsSecured` |
| A source column asked for that the caller's metadata read does not answer counts as field-secured | `SecuredColumnsAsync` (`M` ~1099) |

### 9.2 Decision (C path): one secured-source reader, no app-only metadata read

The 064d proposal (section 8.5, item 2: move the secured-source read onto `MappingColumnMetadata`) is not built. The shared
`FieldSecuredSourceRule`, asked by create and push, stays the single definition, over each writer's caller-read catalog.
Reasons: an app-only read would change create's fail-closed scope (an unstated `IsSecured` would map nothing from the source
instead of skipping only the rule that reads it), and every host whose app-only double does not answer the metadata call
(the create seam fixture's is strict) would map nothing. The push already skips a rule whose source column its catalog does
not list (the column is never selected), so the "not answered counts as secured" rule needs no push change.

### 9.3 Tests

New: `tests/integration/seam/RecordCreation/RecordCreationServiceMappingSeamTests.Task064c.cs` (13). Updated fakes (the live
catalog shape, no assertion changed): `SecureChildOwnershipAiToolTests.ScriptedUserClient` answers every column asked with its
`IsSecured` stated (it listed only the secured ones); `OfficeCallerDataverseDouble` declares the source columns the Office suites'
rules read (`sprk_project`: `sprk_mattertype`, `sprk_notonsource`, `sprk_feearrangement`; `sprk_matter`: `sprk_primarycontact`).
Six Office tests failed with the new rule and the old double, and pass with the double declaring them. No test deleted.

Rule consequence (for the owner): a Concat or Template placeholder, or a Copy source, naming a column the source table does not
have is now not applied (the column's security cannot be told); the column the rule targets is named in a warning when the
rule would have filled it. Before,
the placeholder was left blank and the rest of the template written.

| Test | Old code | New |
|---|---|---|
| (a) AFilingLookupMappedFromTheFilingParent_TheCallerCannotAppendTo_IsRefused | pass | pass |
| (b) AnOrdinaryValueFromTheRegardingPairSource_TheCallerCannotAppendTo_IsWritten | pass | pass |
| (c) AnExplicitSourceThatIsTheFilingParent_ByTableAndId_IsWritten (pair: Office shape; lookup: child-route shape) | pass ×2 | pass ×2 |
| (d) AnExplicitSourceWithThePairsIdButAnotherTable_KeepsTheCallersChecks (item 1) | FAIL | pass |
| (e) AnOrdinaryAdapterDefaultTheCallerCannotAppendTo_IsDroppedAsADefault | pass | pass |
| (f) ATemplateRuleWithAFieldSecuredPlaceholder_IsDropped | pass | pass |
| (g) ASourceColumnWhoseSecurityIsNotStated_IsTreatedAsSecured (answered without `IsSecured` / not answered) | pass / FAIL | pass / pass |
| (h) WhenTheSourceSecurityReadThrows_NothingIsMapped_WithAWarning | pass | pass |
| AnAccessConferringRegistryColumnWithoutThePrefix_KeepsTheCallersChecks (item 2) | FAIL | pass |
| TheCallerChecksStep_CountsTheValuesCopiedWithoutTheCallersChecks (item 3) | FAIL | pass |
| ACallersTargetColumnWhoseSecurityIsNotStated_IsCheckedAsFieldSecured_AndRefuses (item 4) | FAIL | pass |

Mutation proof for (e): with the `defaultColumns?.Contains(c)` argument replaced by `false`, (e) fails (the default is copied
unchecked); restored, it passes. (c) with the pair shape passes only because the pair's table is resolved: dropping the pair
arm from the skip decision would fail it.

### 9.4 Known limits (recorded, not changed here)

- K1: a profile Default rule's constant (not an adapter default) mapped from the filing parent is copied without the caller's
  checks like a copied value. Whether a profile constant counts as "from the filing parent" is an owner question.
- K2: when values are copied unchecked, step 5 still asks the whole row once more without them (the final check); it is
  redundant with the per-value checks on that path. Kept: it is the row-as-written guard.
- K3: when a copyable rule and a secured-source rule share a target column, the copyable rule's value is written and the
  secured-source rule is not named in a warning (the naming probe runs after the copyable rules, fill-only).
- K4: a mapped value dropped by step 5 whose column an adapter default then fills is named as "not written", while the column
  holds the default.

### 9.5 Gates

- Build `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- Filtered tests (`RecordCreation|FieldMapping|Push|CallerWriteAuthorization|CallerChecks|MappedCopyRights|MappingSourceRule|
  SecureChildOwnership|OfficeQuickCreate|OfficeTodo|OfficeCallerDataverseDouble|ChildRecord|ChatCreated|CrossSurfaceOwner|
  RecordCreatorPerson|SecureRootCreateRoute|SecureRootInheritanceWriter|AssignedAccess`): 4012 run, 4004 pass, 3 skipped, 5
  failed; the 3 Office ones pass after the double declares the source columns (re-run of the Office and timing set: 473 run,
  469 pass, 3 skipped, 1 timing test failed), and the timing tests (`CreatePath_*`, `CallerChecksConcurrencyTests`) pass alone (49/49); they fail only under machine load
  (K2, pre-existing).
- ArchTests (`dotnet test tests/Spaarke.ArchTests`, no `--no-build`): 2855/2855 pass.
- ADR-002 (no plugin) ✓; ADR-008 path A (checks stay in `CallerWriteAuthorization`, asked by the
service) ✓; ADR-010 (no DI registration; the registry is read through the existing scope factory, as Repair does) ✓; ADR-019
(warnings name the column only) ✓; ADR-028 (record-type and metadata reads as the caller) ✓; ADR-038 (tests at KEEP paths) ✓.
§11: `SourceIsCallersFilingRecordAsync` — Existing: `SourceIsCallersFilingParent`, `MappingSourceResolver`; Extension: reuses
the resolver's pair resolution and the same filing-lookup arm; Cost of doing nothing: a source on another table with the pair's
id is copied without the caller's checks. `AssignedToColumnsOfRegistries` — Existing: `PushRuleSelection.AssignedToColumnsOf`,
`ColumnsRepairNeverMaps`; Extension: calls the push classifier; Cost of doing nothing: a registry Assigned-To column without the
prefix is copied without the caller's checks.

## 10. 064d: re-check of the 064c fixes

A scoped re-check of `e35a400a00..1ccf73a503` found no defect on a real path. It found two fallbacks that no test pinned, and one point to confirm on dev. 064d (64e61f88a8, merged as fd8172ea32):

- **Tests for the fail-closed fallbacks.**
  - An access-conferring registry that cannot be read keeps the caller's checks on every mapped value, and the create still succeeds.
  - A regarding pair whose record type is not resolved (null or a fault) is not the filing record.
  - A control test: an explicit source carrying the pair's id is the filing record only when the record type's table is the source's table.
  - Each test fails when the production line it pins is reverted.
- **Source column names.** A rule's source field is trimmed and lower-cased with the rule (`NormalizedRule`), as the target already was. The field-security read asks for and compares that name. A rule spelled in mixed case is found on the source row and is checked like any other. A column the read does not answer still counts as secured. `SourceField` is used only as a Copy rule's column name; Template and Concat text lives in `Expression` and is not changed.
- **Doc comment.** `SourceIsCallersFilingParent` matches the regarding pair by id for step 3's owner gate. `SourceIsCallersFilingRecordAsync` (step 5) also requires the table to match. The two differ on purpose.
- **Dev checks (read-only, 2026-11-26).**
  - Every column on `sprk_matter`, `sprk_event`, `sprk_project` and `contact` returns a boolean `IsSecured`, including virtual and `*name` columns. So the target-side rule (anything but an explicit false counts as secured) changes nothing for real columns.
  - All 57 dev mapping rules are Copy rules (no Template or Concat).
  - No dev rule has a mixed-case or padded column name.
- **Gates.** Build 0 warnings. Filtered tests (`RecordCreationServiceMappingSeamTests`, `FieldSecuredSource`, `CallerWriteAuthorization`, `MappedCopyRights`) 372/372. ArchTests 2855/2855. Format clean. Publish delta for 064 as a whole, against 3e0268349a: +5,482 bytes, 194 files on both sides, 41.64 MB, no vulnerable packages.
- **Kept as is.** The target-side check in `CallerWriteAuthorization` is unchanged.
