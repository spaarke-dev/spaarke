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

Out-of-scope defects: push declassification (section 3), for the main session to file. rework: none task 064
