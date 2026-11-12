# Task 022b: mapping from a Restricted source; a document's Access Permission set in the create

Rigor: FULL. Branch `task/fm-022b` (from `work/field-mapping-server-write-path-r1`). UAC-r2 reviews the diff before merge.

## Rules built

1. **Source predicate.** Whether a source table carries `sprk_accesspermission` comes from metadata the code already holds:
   `InheritedAccessPermission.Tables` (to-do, event, communication, document) plus every secure root whose server-owned columns
   name it (`SecureRootColumns`, from the table registry). `InheritedAccessPermission.CarriesColumn` / `TablesCarryingColumn`.
   No list of its own. UAC-r2 (2026-11-12, Q1): `sprk_agreement` is declared in the SOURCE set only
   (`InheritedAccessPermission.SourceOnlyTables`; `SourceCarriesColumn` / `SourceTablesCarryingColumn`), so the
   Restricted-source check reads its `sprk_accesspermission` and fails closed on it. It adds no inheritance or enforcement on
   agreements; it is a source check only. The value is read in step 3's one app-only source read (`MappingSourceAccessColumns`);
   a table without the column is never asked for it. `AccessPermissionTablesGuardTests` holds the source set exactly equal to
   the solution XML (no baseline).
2. **Target answer** (`InCreateAccessPermissionAsync`), from the row as it ends (the stamp plan applied):
   - to-do, event, communication: the step-10 value (I-16) when every parent was read; a parentless row keeps its own value;
     parents that cannot be read decide nothing;
   - document: the document decision (below);
   - project, work assignment (UAC-r2 Q2, option c): the secure-root plan's Access Permission floor alone decides
     (`SecureRootCreatePlan.AccessPermissionFloor`, below): Restricted exactly when the floor is Restricted. The caller cannot
     send `sprk_accesspermission` on a root (`RecordTableRules.RootServerOwnedColumns` refuses it at step 2, and the column is
     mapping-protected). `RootAccessPermission` keeps its own-value branch as defence in depth only. Only parents filed in the
     create count, for an ordinary plan and a plan into isolation alike. An undecided floor is never Restricted and strips. The
     create writes nothing new: no Access Permission and no access record (both stay the cascade's);
   - a matter: its own value;
   - a table without the column: never Restricted.
   An answer that cannot be told is never Restricted. For a to-do, event or communication the three states are used as such
   (`RecordCreationService.ChildAccessPermission`): `Unknown` writes nothing and is never Restricted; `NoParent` keeps the row's
   own value; otherwise the known level is written, Restricted exactly when not `NoRestrictedAncestor`.
3. **Document at step 10.** `sprk_accesspermission` and its record (`AccessInheritance.Column`) are set in the create, together,
   from the filing parents: `CoreAncestorResolver.DecideNewDocumentAccessPermissionAsync` reads the parents with the existing
   walk and `InheritedAccessPermission.ResolveAsync`, then calls the existing `DecideDocument(own, null, parents, inherited)`
   (or `DecideDocumentRaiseOnly` where the environment has no record column). The payload's value is the stored value; there is
   no record yet. No second decision, the fence unchanged (the private I-16 path still gives a document nothing), step 12's
   refresh unchanged. Nothing written when it cannot be decided. Review F2: the create's decision and the refresh share one
   private helper (`DecideDocumentAsync`: the parents' answer and its found/value guard, `DecideDocument` or the raise-only
   form by the record column, the undecided handling; `DocumentRecordColumnAsync`: the record-column probe). Behaviour
   unchanged; the create's path now also logs the raise-only and no-record-column cases, as the refresh did.
4. **Resolver states.** `InheritedAccessPermission.Answer` gained `NoRestrictedAncestor` (NoParent, or Found with a KNOWN level
   other than Restricted: Standard or Limited), `Unknown` (Undetermined, or Found with no value or a value that is no known
   level), and the `NoParent` / `Undetermined(reason)` factories. The
   private I-16 path now returns the `Answer` (`InheritedAccessPermissionAnswerAsync`); the public `int?` method wraps it
   unchanged. `ResolveNewRowInheritedAccessPermissionAsync` gives a new row's answer. No new resolver.
5. **Restricted predicate** (`EnforceMappingSourceRestrictedAsync`, right after `EnforceMappingSourceOwnerAsync`):
   - a source that is Restricted, or carries the column and its value cannot be read, maps only into a row known to end
     Restricted in this create;
   - otherwise the mapped values are removed (the caller's values stay), with warning `record_creation.mapping_source_restricted`;
   - a source read as Standard or Limited, or a table without the column, maps unchanged (confirmed by UAC-r2, Q3);
   - an empty source value counts as Standard; an unreadable one fails closed (confirmed, Q3);
   - a removed value that files the row refuses the create, as fix round 2 does, with the same code
     `record_creation.mapping_source_secure_filing` and Restricted wording (confirmed, Q3; the reuse is documented where the
     code is defined, `RecordCreationRefusal.MappingSourceSecureFilingCode`);
   - the pre-write rules run again after a strip (the shared `StripMappedSourceValuesAsync`, extracted unchanged from fix
     round 2);
   - the step-10 answer is decided once on the item the check saw and handed to step 10; after a strip step 10 decides again
     on the stripped row.
   Create only.

## Section 11 answers (new members)

| Member | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `InheritedAccessPermission.CarriesColumn` / `TablesCarryingColumn` | `Tables`, `SecureRootColumns` | It is a read over those two sets, placed beside them | The rule would need a hand list, or would read the column on tables without it (a failed read) |
| `Answer.NoRestrictedAncestor` / `Unknown` / `NoParent` / `Undetermined()` | `Answer`, `ParentTopsStatus` | Members of the existing record; read by `ChildAccessPermission` | Callers could not tell "no Restricted ancestor" from "unknown" (the brief's three states), and a found value that is no known level would be written as if known |
| `RecordCreationService.ChildAccessPermission` (pure) | The answer switch inside `InCreateAccessPermissionAsync` | That switch, moved out so it can be tested on its own | The three states would have no direct test |
| `CoreAncestorResolver.InheritedAccessPermissionAnswerAsync` (private) | `InheritedAccessPermissionAsync` | The same body returning the answer; the old method wraps it | The int? collapse loses the unknown state |
| `CoreAncestorResolver.ResolveNewRowInheritedAccessPermissionAsync` | `ResolveInheritedAccessPermissionAsync` (public, int?, used by the incoming-association path) | Changing that one's return type would touch an unrelated caller | Step 10 could not tell unknown from no parent |
| `CoreAncestorResolver.DecideNewDocumentAccessPermissionAsync` + `NewDocumentAccessPermission(Target, Write, Record)` | `RefreshDocumentAccessPermissionAsync` (post-create) | The refresh needs a stored row; this is the same decision for a row not yet created, through the shared helper below (review F2: the unread `Parents` member removed) | A document under a Restricted parent would not be Restricted from creation (AC3) |
| `CoreAncestorResolver.DecideDocumentAsync` + `DocumentRecordColumnAsync` (private) | The wrapper code that was duplicated in the create's decision and the refresh | Extracted from both; no new behaviour | Two copies of the guard and undecided handling could drift (review F2) |
| `SecureRootCreatePlan.AccessPermissionFloor` + `AccessPermissionFloor` | `SecureParentsAnswer.StrictestPermission`, read inside `PlanCreateAsync` | Returned on the plan the create already holds; read only | The project and work-assignment create could not know the floor without a second read (UAC-r2 Q2) |
| `RecordCreationService.RootAccessPermission` (pure) | The roots' line in `InCreateAccessPermissionAsync` | That line, given the floor | A project or work assignment under a Restricted matter would strip (UAC-r2 Q2) |
| `CoreAncestorRestamper.DecideNewDocumentAccessPermissionAsync` (pass-through) | `ResolveInheritedAccessPermissionAsync` (pass-through) | Same pattern as its neighbour | The service holds the restamper, not the resolver |
| `RecordCreationService.InCreateAccessPermission` + `InCreateAccessPermissionAsync` + `FiledAsTheRowEnds` | The I-16 block of `StampBeforeCreateAsync` | That block, moved out so the check and step 10 share one decision | The check would decide the target a second, possibly different, way |
| `OwnAccessPermissionOf`, `BindInCreateAccessPermission` | `PutServerValue` | `BindInCreateAccessPermission` is the one writer the writer guard lists | A document's value and record could be written apart |
| `MappingSourceAccess`, `MappingSourceAccessColumns`, `MappingSourceAccessOf` | `MappingSourceCopyGate` (the secure predicate) | The copy gate answers "secure", a different column and rule | The Restricted value would not be read or classified |
| `EnforceMappingSourceRestrictedAsync`, `MappingSourceRestrictedFilingRefused`, two message constants, `MappingSourceRestrictedCode` | `EnforceMappingSourceOwnerAsync` | Shares its strip (`StripMappedSourceValuesAsync`, extracted) | Restricted values would be written into rows that are not Restricted |
| `InheritedAccessPermission.SourceOnlyTables`, `SourceTablesCarryingColumn`, `SourceCarriesColumn` | `TablesCarryingColumn`, `CarriesColumn` (the target set) | The target set must not gain the agreement: a source-only table takes no inheritance and no enforcement (UAC-r2 Q1) | An agreement source would not be checked for Restricted |
| `AccessPermissionTablesGuardTests` (ArchTest) | `DocumentAccessPermissionWriterGuardTests` (writers, not tables) | Different subject | The code's table set could drift from the solution unseen |
| `StampWorld.WithColumn` (test fixture) | `WithoutColumn` | Its counterpart | The seam world could not give the matter and children the column |

## Placement justification

All in existing BFF files on the create path (`RecordCreationService`, `.Mapping`, `CoreAncestorResolver`, `CoreAncestorRestamper`,
`ParentLineage`, and the read-only floor on `SecureRootInheritance`'s plan). No new endpoint, DI registration, package, Dataverse
column or background work. The publish-size delta and the CVE check are not run in this task: the main session measures the delta
and runs the CVE check at merge (the change adds no package).

## Fail closed

- A source value that cannot be read (no read, another type, an option that is no known level) is treated like Restricted.
  Review K2: no read is Unreadable before the table is looked at, and a mapped value with no gate read has no source table
  (never the target's), so it cannot pass as "a table without the column".
- A row whose answer cannot be told (a parent that cannot be read, a fault, the document's own value unreadable) is not
  Restricted, so a Restricted or unreadable source strips; the warning then says the check could not be made.
- **A target table without the column is never Restricted, so a Restricted source always strips there** (even when the row is
  filed under that source).
- A removed value that files the row refuses: 400 when the source is Restricted and the row is known not to be; 503
  (`MappingSourceCheckFailed`) when the check could not be made.

## Fail-then-pass evidence

Run: the new and changed tests (`RestrictedSource`, `RestrictedMappingSource`, `PreCreateStamp`, the read-columns test), 61 tests.

- **Before** (behaviour switched off, the new pure members kept so the tests compile: the Restricted check never runs, a document
  returns from step 10 untouched, the source read does not ask for the column): **13 failed, 48 passed**. The failures are every
  must-fire case, AC3, the document cases, the fail-closed cases, the read-columns test and the pipeline document test.
- **After:** 61 passed. (A 62nd seam test, "a project filed under the Restricted matter maps nothing", was added after that
  run; UAC-r2's Q2 answer replaced it, below.)
- The pure unit tests (`RestrictedMappingSourceTests`) do not compile against HEAD (the members are new).
- The guard: `Disagreements_MustFireAndMustNotFire` seeds a table only one side names (fires) and equal sets (does not).
- UAC-r2 Q1 (the agreement in the source set, no baseline): with `sprk_agreement` taken out of `SourceOnlyTables`,
  `AccessPermissionTablesGuardTests` **1 failed, 1 passed**; restored, **2 passed**. `RestrictedMappingSourceTests.TheAgreement_IsASourceOnlyTable`
  does not compile against the round before it (the members are new).
- **UAC-r2 Q2 (the plan's floor)**, run `RestrictedMappingSourceTests` + `RestrictedSource`, 68 tests. Switched off (the roots'
  answer ignores the floor; an undecided floor counted as decided; `IsRestricted` without `Decided`; `Of` ignoring
  `IsKnown`): **4 failed, 64 passed** (the project and work-assignment must-fire seam tests, the undecided-floor unit tests).
  Restored: **68 passed**.
- **Review F4, F2 and K2**, same run, 79 tests. Switched off (a parentless row's `EndsRestricted` set to `true` in
  `ChildAccessPermission`, the F4 flip point; `NoRestrictedAncestor` / `Unknown` back to their earlier definitions; the
  table looked at before the missing read): **6 failed, 73 passed** — the parentless To Do strip (seam), the unknown-level
  answer (2 cases), the child under an unknown answer, a parentless child keeping its own value, and the K2 order.
  Restored: **79 passed**.
- **Review F1 (client)**, `webApiCreateError.test.ts`: the util at HEAD **1 failed, 49 passed** (the Restricted sentence is
  replaced by the secure one); changed **50 passed**.
- **Adversarial pass 2, F3 (the isolated plan's floor)**, `RecordCreationServiceSecureRootSeamTests`, 13 tests. With the
  `AccessPermissionFloor = …` initialiser removed from `PlanCreateAsync`'s isolated return: **1 failed, 12 passed** (the
  project under a secure Restricted matter, created into isolation, whose mapped values map). Restored: **13 passed**. The
  second new test (an isolated plan whose only gap is an unrepeated pair: floor undecided, the service's answer strips) passes
  either way, since an undecided floor is also the property's default.

## The `SecureRootInheritance` edit (UAC-r2 Q2, option c) — for UAC-r2's separate review

UAC-r2 approved one named, read-only edit: `PlanCreateAsync`'s result also returns the Access Permission floor it already
computes. Commit `00316da472`, file `src/server/api/Sprk.Bff.Api/Services/Access/SecureRootInheritance.cs`:

- `SecureRootCreatePlan` (lines ~299-333): `Ordinary` now sets `AccessPermissionFloor = AccessPermissionFloor.NoParent`; a new
  init-only property `AccessPermissionFloor` (default `AccessPermissionFloor.Undecided`, so every refusal is undecided); a new
  record `AccessPermissionFloor(int? Value, bool Decided)` with `NoParent`, `Undecided`, `IsRestricted` (decided and
  Restricted) and `Of(SecureParentsAnswer)` (its `StrictestPermission` value when `IsKnown`, else undecided).
- `PlanCreateAsync` (line ~1256): the ordinary return after the parent read is `Ordinary with { AccessPermissionFloor =
  AccessPermissionFloor.Of(answer) }`.
- `PlanCreateAsync` (lines ~1320-1323): the isolated return sets `AccessPermissionFloor = AccessPermissionFloor.Of(answer)`.

Nothing else: no read added, no decision, refusal or write changed; `DecideParentsAsync`, `Decide()`, the post-create step and
task 175 F1-1 unchanged. The floor is the same answer the plan already decides on, so separating it changes nothing it decides.
`SecureRootFilingGate.PlanCreateAsync` is unchanged (its early returns are `Ordinary`, a create filed under nothing, or
refusals, undecided). Per-class runs of every test class on the plan pass (see the report).

## Known limits

1. **K: a project's or work assignment's stored value follows after the create.** The create writes no Access Permission for
   a root (option c); the secure-root inheritance cascade raises it to the floor when it follows the row's parents, after the
   create and outside the create request. The seam cannot drive that step; the unit suite pins the cascade's decision for a
   new project under a Restricted parent (`TheCascade_RaisesANewProjectUnderARestrictedParent_ToRestricted`).
2. **K: the floor counts only the parents filed in the create, one level** (the plan's own read). A work assignment filed
   under a project whose stored value is not yet raised reads that project's stored value; anything not Restricted there
   strips (fail closed).
3. **K: an unreadable parent refuses the create through the plan** (its existing rule), so the undecided-floor strip is reached
   only where the plan proceeds with an unknown answer (an isolated create whose only gap is an unrepeated pair). The unified
   create refuses such a pair at its caller check (step 5) before the plan is asked, so this is pinned at the plan
   (`PlanCreateAsync_AnIsolatedPlanWhoseOnlyGapIsAnUnrepeatedPair_HasAnUndecidedFloor_AndItsMappedValuesAreRemoved`, the real
   plan over the provisioning fixture) and on `RootAccessPermission` in the unit suite.
4. **K: a parent option value that is no known level** reads as Standard in the plan's floor (`FilingPermission.Rank`,
   unchanged). It can only make the floor not Restricted, so it strips; a Restricted parent beside it still decides.
5. **K: a document's access permission is set from its typed filing lookups at create; one filed only through the regarding
   pair takes its value from the post-create reconcile.** Accepted by UAC-r2 (Q4): the strip fails closed meanwhile.
6. **K: no caller path reaches the agreement source check yet.** `EntityAccessFilter` has no agreement entity set, so the
   caller's read of an agreement cannot be asked and nothing is mapped from it (fail closed). The check is in place for when
   a path reaches it; none is added here.
7. **K: the client shows the server's sentence for `record_creation.mapping_source_secure_filing`** (secure and Restricted
   refusals share the code). Through the child-record adapter, a body with no `detail` gives its `title` as the message, which
   would be shown; the BFF always sends a `detail` for this code.
8. **K: deployment order.** The app-only source read asks for `sprk_accesspermission` from a static set of source tables
   (`InheritedAccessPermission.SourceTablesCarryingColumn`, held equal to the solution by `AccessPermissionTablesGuardTests`).
   In an environment whose solution does not yet have the column on a source table, that read faults: nothing is mapped and
   the user is told (fail closed). Deploy the solution with the column before, or with, this BFF change.

## Test doubles changed

- `RouteRecordCreation.Entities()` answers every column it is asked for with a text value. A document source now asks for
  `sprk_accesspermission`, and a text value reads as unreadable, so the rule stripped the document-to-analysis mapping in
  `AnalysisEndpointsAuthorizationContractTests` (043 AC1/AC3). The double now returns no value for that column (Standard), as
  fix round 2 made it return Dataverse types for the owner and the secure flag.
- `RecordCreationServiceMappingSeamTests`: the default-only read-columns test also expects the column; the pipeline seam's
  document test now expects Restricted and its record in the create (step 10 "Ran"). The "project under the Restricted matter
  maps nothing" test is replaced by the floor tests (UAC-r2 Q2); those give the plan a gate over matters it can read
  (`PlanReadingMatters`), where the harness default reads nothing.
- `RestrictedMappingSourceTests`: "no read on a table without the column is NoColumn" now expects Unreadable (review K2).
- `RecordCreationServiceSecureRootSeamTests` (adversarial pass 2, F3): `Service` and `AppCreatesIntoTheWorld` take an optional
  field-mapping profile (the app-only reader's one profile); new helpers give the mapping source read, the caller's read of it
  (`MappingSourceCallerReadDouble`) and a caller-rights probe answering Read and Write. Existing tests pass no profile, as before.

## Verification (after the Q2 and review-fix commits)

- BFF build: 0 warnings, 0 errors. ArchTests: 2530 passed.
- Filtered BFF suite (record creation, mapping source, child records, inherited access permission, core ancestor, secure root,
  repair, push, events, Office To Do, analysis, document; Office API classes excluded): 6879 passed, 1 skipped.
- Per class, every class on the secure-root plan (18 classes, including the `SecureRootInheritance*` classes): all passed.
- Office API classes one per run: all passed (the two quick-create contract tests' expected source-read columns now include
  `sprk_accesspermission`, commit `ea95e000b9`).
- `webApiCreateError.test.ts`: 50 passed; ESLint clean on both files.

## Found in passing

- The Restricted-source rule for Repair and the field-mapping push is task 022c (#2738).
- The existing I-16 walk reads a parent's value only when the column probe says the parent table has it; a parent table the
  probe does not list reads as Standard. Pre-existing; not changed here.
