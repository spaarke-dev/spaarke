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
   - project, work assignment (roots): the value written in the create;
   - a table without the column: never Restricted.
   An answer that cannot be told is never Restricted.
3. **Document at step 10.** `sprk_accesspermission` and its record (`AccessInheritance.Column`) are set in the create, together,
   from the filing parents: `CoreAncestorResolver.DecideNewDocumentAccessPermissionAsync` reads the parents with the existing
   walk and `InheritedAccessPermission.ResolveAsync`, then calls the existing `DecideDocument(own, null, parents, inherited)`
   (or `DecideDocumentRaiseOnly` where the environment has no record column). The payload's value is the stored value; there is
   no record yet. No second decision, the fence unchanged (the private I-16 path still gives a document nothing), step 12's
   refresh unchanged. Nothing written when it cannot be decided.
4. **Resolver states.** `InheritedAccessPermission.Answer` gained `NoRestrictedAncestor` (NoParent, or Found and not
   Restricted), `Unknown` (Undetermined, or Found with no value), and the `NoParent` / `Undetermined(reason)` factories. The
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
| `Answer.NoRestrictedAncestor` / `Unknown` / `NoParent` / `Undetermined()` | `Answer`, `ParentTopsStatus` | Members of the existing record | Callers could not tell "no Restricted ancestor" from "unknown" (the brief's three states) |
| `CoreAncestorResolver.InheritedAccessPermissionAnswerAsync` (private) | `InheritedAccessPermissionAsync` | The same body returning the answer; the old method wraps it | The int? collapse loses the unknown state |
| `CoreAncestorResolver.ResolveNewRowInheritedAccessPermissionAsync` | `ResolveInheritedAccessPermissionAsync` (public, int?, used by the incoming-association path) | Changing that one's return type would touch an unrelated caller | Step 10 could not tell unknown from no parent |
| `CoreAncestorResolver.DecideNewDocumentAccessPermissionAsync` + `NewDocumentAccessPermission` | `RefreshDocumentAccessPermissionAsync` (post-create) | The refresh needs a stored row; this is the same decision for a row not yet created | A document under a Restricted parent would not be Restricted from creation (AC3) |
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
`ParentLineage`). No new endpoint, DI registration, package, Dataverse column or background work. Publish-size delta not measured
(not in this task's verify list; the change adds no package).

## Fail closed

- A source value that cannot be read (no read, another type, an option that is no known level) is treated like Restricted.
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
- **After:** 61 passed. A 62nd seam test (a project filed under the Restricted matter maps nothing) was added after the
  before-run; it asserts the strip and its warning, which the switched-off build never produces.
- The pure unit tests (`RestrictedMappingSourceTests`) do not compile against HEAD (the members are new).
- The guard: `Disagreements_MustFireAndMustNotFire` seeds a table only one side names (fires) and equal sets (does not).
- UAC-r2 Q1 (the agreement in the source set, no baseline): with `sprk_agreement` taken out of `SourceOnlyTables`,
  `AccessPermissionTablesGuardTests` **1 failed, 1 passed**; restored, **2 passed**. `RestrictedMappingSourceTests.TheAgreement_IsASourceOnlyTable`
  does not compile against the round before it (the members are new).

## Held: a project's or work assignment's value in the create (UAC-r2 Q2), stopped

UAC-r2 asked for the root's value AND its access record to be written in the create from `SecureRootInheritance`'s own
decision, unchanged, with the stop condition "if it cannot give the access-record write as a reusable call without a rule
change". It cannot, so this was not built:

- The decision is reusable: `SecureRootInheritance.Decide(storedSecure, storedPermission, markerText, ancestry)` is pure.
- The ancestry for a row that does not exist yet is read only inside `PlanCreateAsync` (the private `DecideParentsAsync` over
  `Overlay`), and the plan returns only the secure parents, not the floor.
- `PlanCreateAsync` refuses a create that carries the access record: "the access record is the cascade's alone; a create never
  carries one" (task 175 fix round, verifier F1-1).
- The cascade writes the access record only after `AccessRecordDistrustAsync` proves the column is field-secured and that the
  BFF's read of it works, through `WriteMarkerAsync` (an update of an existing row, with a concurrency check). A create has
  neither step; writing the record there would be a second writer without those guards.
- `Decide` reads an empty stored secure flag as undetermined. An ordinary root create does not write the flag, so the create
  would have to supply one the decision refuses to guess.

So the target answer for a project or work assignment stays "the value written in the create", which is never Restricted on
the caller path (see known limit 2).

## Known limits

1. **Roots.** A caller's project or work-assignment create never writes the column (it is the server's on a root), so a root is
   never Restricted in its create. A Restricted source therefore always strips into a project or work assignment, including
   one filed under that Restricted matter (its parents raise it after the create). Held above (Q2).
2. **K: a document filed only through the regarding pair** (no typed lookup) shows no parents before the create, so the
   decision sees a parentless row: its own value only. Accepted by UAC-r2 (Q4): the strip fails closed, a document's access
   permission is a display copy with no enforcement of its own (round 81), and the reconcile brings it in step.
3. **An agreement cannot be a mapping source on the caller path today.** The caller's read of it cannot be asked (no entity set
   for it in the access map), so nothing is mapped from it (fail closed). The source check is in place for when it can be.

## Test doubles changed

- `RouteRecordCreation.Entities()` answers every column it is asked for with a text value. A document source now asks for
  `sprk_accesspermission`, and a text value reads as unreadable, so the rule stripped the document-to-analysis mapping in
  `AnalysisEndpointsAuthorizationContractTests` (043 AC1/AC3). The double now returns no value for that column (Standard), as
  fix round 2 made it return Dataverse types for the owner and the secure flag.
- `RecordCreationServiceMappingSeamTests`: the default-only read-columns test also expects the column; the pipeline seam's
  document test now expects Restricted and its record in the create (step 10 "Ran").

## Found in passing

- The Restricted-source rule for Repair and the field-mapping push is task 022c (#2738).
- The existing I-16 walk reads a parent's value only when the column probe says the parent table has it; a parent table the
  probe does not list reads as Standard. Pre-existing; not changed here.
