# Task 018 notes: Office quick-create onto the unified create

Task 018 is split into 018a (service prerequisites), 018b (the Office Matter/Project adapter) and 018c (deleting
`OfficeRecordCreationService`). See `decisions.md` at `0cae77ab77` and `8538113a48`.

## 018a: service prerequisites (no live change)

### Rigor and scope

FULL, inherited from the 018 POML. Executed on opus at high effort. The main session runs the adversarial review; the
implementing agent launches none.

- Branch `task/fm-018a`, cut from `0cae77ab77`.
- Changes are in `RecordCreationService` files only, plus tests at KEEP paths.
- No endpoint, DI registration, package or background work, and no caller passes the new options yet. Every live create
  path is unchanged. 018b is the first caller.

Coordinator notes received during 018a. Neither changes 018a's scope; both are recorded here for 018b and 018c:

- **Invoice leg** (`445d1a5458`): it never moves onto the service. In 018b and 018c, leave it exactly as it is apart from
  the comment "removed by ebilling-r1 task 096".
- **UAC-r2 condition (b)** (`8538113a48`):
  - Assigned To refuses, as the caller, when the caller lacks AppendTo on the contact. It never reads as "matter not
    found".
  - A missing bearer token gives 500 `caller_rights_unverifiable`.
  - The post-create order is accepted on three conditions:
    - the membership event fires once;
    - a Project under a secure matter is owned by the Secure Record Owners team;
    - the refusal map is table-driven.

  Because of the Assigned To ruling, the K-3 reference path still excludes Assigned To. The service has no column list:
  018b names the reference columns, and must not name `sprk_assignedtointernal` or `sprk_assignedto`. The test
  `WithTheOption_ALookupItDoesNotName_TheCallerMayNotAppendTo_IsStillRefused_AndNothingIsCreated` pins that an unnamed
  Assigned To keeps its refusal.

### What changed

| Item | Change |
|---|---|
| **K1**: a step-7 catalog read fault | Test only. The behaviour was already correct. `DecideOwnerAsync`'s catch turns a fault in `PairParentAsync`'s catalog read into 503 `record_creation.owner_unavailable`, and nothing is written. |
| **K3**: an explicit null under lower-case `sprk_ai_search_index@odata.bind` | Fixed in step 9. When step 8 fills a search-index column, it first removes any key that differs from the canonical key only in case (`OwnedChildWrite.BodyFields` is Ordinal-keyed). The body then carries one key, the canonical one. |
| **K-3**: dropping an unusable reference with a warning | New request option `ReferenceLookupColumns`. It runs at the end of step 2, before mapping, so a mapping rule may still fill a dropped reference, and that value is then checked as a mapped value. See below. |
| **K-7**: the post-mapping default hook | New request option `PostMappingDefaults`. It runs as step 3½, after mapping and before the step-5 checks. Defaulted values are merged with the mapped values, so step 5 checks them by origin. See below. |

**`ReferenceLookupColumns`** (`IReadOnlySet<string>?`, default null):

- Each lookup of the caller's payload that it names is asked alone, as the caller, through
  `CallerWriteAuthorization.CheckCallerMayCreateAsync`, on a one-lookup item.
- When the caller may not use the lookup (AppendTo, Append or field security), when the lookup cannot be found, or when
  the check could not be answered or faulted, the lookup is dropped. The warning is
  `record_creation.reference_value_dropped`, naming the column: "The value you chose for '{column}' was not written: it
  was not found, or you do not have permission to use it; the {noun} was created without it." "Not permitted" and "not
  found" share one wording, so the record's existence is never disclosed (ADR-019 / uniform refusal).
- The remainder is re-mapped as the caller.
- A filing lookup (`IsFilingLookup`) or a server-set lookup is never treated as a reference. It keeps the caller's own
  rule: refuse.
- A caller's cancellation rethrows.
- When nothing is dropped, the step-2 trace note is unchanged. Otherwise it gains "; N reference value(s) dropped".

**`PostMappingDefaults`** (`IReadOnlyDictionary<string, object?>?`, default null):

- Each value is in a mapping rule's value form: a lookup as `EntityReference`, a choice as `OptionSetValue`, or text, a
  number or a flag.
- A default is written only when neither the caller nor a mapping rule supplied the column. The FR-02 test applies: a
  column counts as unset when it is absent, null, blank text, or a lookup with no id.
- The defaults are merged with the mapped values through `MapMergedAsync`. Step 5 then checks them as mapped values:
  - one the caller may not set is dropped with `record_creation.default_value_dropped` ("The default value for
    '{column}' was not written: …");
  - a filing lookup the caller may not file under refuses, with the uniform 404 `record_creation.not_found`.
- Defaults apply whether or not field mapping was asked for.
- When nothing is defaulted, the step-4 note is unchanged. Otherwise it gains "; N default(s) filled".
- These are argument errors (`ArgumentException`), thrown before anything is read:
  - a key that is not a logical name;
  - a column field mapping never writes (`ProtectedFromMapping`: server-set, owner and the like; the regarding pair and
    resolver columns);
  - a `DateTime` value, whose storage behaviour a default cannot state;
  - a value with no Web API form.

Both options are a caller's create only. With either option set, the system principal gets an `ArgumentException`.

### Component justification (CLAUDE.md §11)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `RecordCreationRequest.ReferenceLookupColumns` and warning code `record_creation.reference_value_dropped` | FR-08's mapped-value drop path (`CheckMergedPayloadAsync`, `MappedValueDroppedCode`). `OfficeRecordCreationService` drops an unverifiable matter type, practice area or project type itself (the `OfficeQuickCreateContractTests` "dangling lookup" cases). | An init-only property on the existing request record, reusing the existing per-lookup caller check. It is not a new service. The mapped-value path cannot be reused directly: these values are the caller's own, which step 5 must refuse for every other caller. | 018b cannot move Office onto the service without a live change. An Office matter whose type or practice area the user cannot use would be refused instead of created without it, which UAC-r2's review of 013 (K-3) ruled out. |
| `RecordCreationRequest.PostMappingDefaults` and warning code `record_creation.default_value_dropped` | Mapping rules' own `DefaultValue` (a per-rule constant, not per-request). The Office service's assigned-to fallback to the maker's contact (`OfficeRecordCreationService`). | An init-only property, merged through the existing `MapMergedAsync` and `CheckMergedPayloadAsync`. A mapping-rule default cannot express "the maker's contact", which is per request. | Office's "named contact, else mapped, else the maker's contact" order cannot be expressed. If 018b wrote the maker's contact into the caller payload, a mapped value could never win, and the default would be refused instead of dropped (task 014 review K-7). |

### Tests (KEEP paths)

| File | Tests |
|---|---|
| `tests/integration/data-mutation/RecordOwnership/CrossSurfaceOwnerTests.cs` | K1: `AStep7CatalogReadFault_IsTheOwnerUnavailableRefusal_503_AndNothingIsWritten`. `CreateWorld` gains `ServiceCatalogReadFault`, which faults only the service's own reader, so the step-6 plan still answers. It also gains a `Caller` accessor. |
| `tests/integration/data-mutation/RecordOwnership/SearchIndexDefaultTests.cs` | K3: `AnExplicitNullBind_IsUnset_AndIsFilledUnderTheCanonicalKeyOnly`, a theory with two cases: lower-case (the duplicate-key case) and canonical. |
| `tests/integration/seam/RecordCreation/RecordCreationServiceReferenceLookupSeamTests.cs` (new) | K-3, six tests. Must-fire: drop with a warning, the create proceeds. Kept: a usable reference, with the trace note unchanged. Must-not-fire: without the option, still refused (404 uniform); with the option, an unnamed lookup (Assigned To) the caller cannot AppendTo is still refused; a filing lookup named as a reference is never dropped. The system principal is an argument error. |
| `tests/integration/seam/RecordCreation/RecordCreationServiceMappingSeamTests.PostMappingDefaults.cs` (new partial) | K-7, 12 cases plus the non-opt-in proof below: the named contact wins; the mapped value wins; with neither, the default is written (with and without mapping); a default the caller may not AppendTo is dropped with the default warning; a defaulted filing lookup refuses at step 5; argument errors (3 columns, a date, the system principal). |
| `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.cs` (fixture) | `ScriptedUserClient` gains the matter's `sprk_mattertype` / `sprk_practicearea` lookup metadata and the `sprk_practicearea_refs` entity set. No existing test used either. |

**Non-opt-in proof.** `WithoutEitherOption_OrWithBothEmpty_TheCreateIsUnchanged`:

- A create with neither option, and one with both options empty, write identical fields, warnings and steps.
- The step-2 and step-4 notes read exactly as they did before 018a.
- A dropped mapped value keeps `mapped_value_dropped`.

The whole existing filtered suite also passes unchanged, below.

### Mutants (each run against the new tests, then reverted)

| Mutant | Killed by |
|---|---|
| K1: `DecideOwnerAsync`'s catch never matches (`when (ex is null)`) | the K1 test (`check_failed` instead of `owner_unavailable`) |
| K3: the case-variant key removal skipped | the K3 lower-case case (two keys). The canonical case passes under it, as expected for a regression pin. |
| A1: the reference pass never runs | the K-3 must-fire test |
| A2: the defaults are never applied | the default-written case (×2), the default-dropped test, and the filing-default test |
| B1: a filing lookup named as a reference is droppable | `AFilingLookupNamedAsAReference_IsNeverDropped_ItsRefusalStands` |
| B2: defaults overwrite caller and mapped values | the named-contact-wins and mapped-value-wins tests |
| B3: the default/mapped warning wording swapped | the default-dropped test and the non-opt-in test |
| C1: defaults merged as the caller's own values, so step 5 refuses instead of dropping | the default-dropped test and the default-written case (×2) |
| C2: no protected-column guard | the argument-error theory (`ownerid`, `sprk_regardingrecordid`) |
| C3: no system-principal guard | both system-principal tests. The first draft of the defaults test passed under C3, because it threw for an unrelated reason (a Web API payload); it was rewritten to use a mapped item and to assert the message. |

### Verification

- `dotnet build Spaarke.sln`: 0 warnings, 0 errors.
- `tests/Spaarke.ArchTests`, built separately and run in full: 1347 passed, 0 failed.
- The filtered set: 2739 passed, 7 skipped, 0 failed (2746 in total). The set is `RecordCreation`, `FieldMapping`,
  `Office`, `RecordOwnership`, `SecureRoot`, `SearchIndex`, `CreatorPerson`, `RecordTableRule`,
  `RouteAuthorizationGuardTests`, `RecordOwnerAssignmentCensusTests`, `SecureChildOwnershipAiToolTests`,
  `MappingSourceCallerMask` and `CrossSurfaceOwnerTests`.

### Known limits (not defects on a real path)

- When the K-3 reference check faults (`CheckCallerMayCreateAsync` throws), the reference is dropped with "it could not
  be checked". `ScriptedUserClient` has no per-call fault knob, so this branch has no dedicated test. It mirrors
  `CheckMergedPayloadAsync`'s per-value fault handling.
- When `MapMergedAsync` faults while it merges defaults, the fault propagates to `CreateAsync` and gives 503
  `record_creation.check_failed`, the step-5 rule for a fault (NFR-01). It is not treated as a mapping fault, because
  defaults are not mapping.
