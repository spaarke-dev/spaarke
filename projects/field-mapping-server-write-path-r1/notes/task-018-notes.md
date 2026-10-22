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
  be checked". `ScriptedUserClient` (the `CreateWorld` caller) has no per-call fault knob, so this branch has no
  dedicated test. It mirrors `CheckMergedPayloadAsync`'s per-value fault handling. See K-5 below for the accepted
  deviation.
- (Superseded by the fix round, K-4: a fault while the defaults merge now drops only the defaults.)

### 018a fix round (Opus adversarial review of `0cae77ab77..2a5f076857`)

#### Fixes

| Finding | Fix |
|---|---|
| **F4-1**: the reference exclusion was not enforced. Any named non-filing lookup could be dropped, including the for-person column, `sprk_regardingrecordtype` and the protected lookups. The reviewer's probe: an event whose unattachable contact was marked a reference, plus a maker default, was created with the maker's contact. | The allowed set is now driven by the registry. `RecordTableRule.OptionalReferenceLookups` (`RecordTableRules.cs`) lists exactly what `OfficeRecordCreationService.ApplyReferenceAsync` drops today: `sprk_mattertype` and `sprk_practicearea` on a matter, `sprk_projecttype_ref` on a project. Every other table is empty. `CreateAsync` throws `ArgumentException`, before anything is read, when a requested reference is not in its table's set, or when `RecordCreationService.IsNeverAReference` names it. That check covers: the for person, `ProtectedFromMapping`, the regarding pair and resolver columns, a filing lookup, an access-conferring column (secure flag, access record, security BU, container) and any `sprk_assigned*` column. The registry tests assert that no registry entry is ever one of these. |
| **F1-2**: a default overwrote a caller's explicit null on a choice or flag. | `DefaultsToFill` now treats a `bool`, `OptionSetValue` or `OptionSetValueCollection` default as supplied whenever its key is present, as `ApplyFillOnly` does. Its comment is corrected. |
| **F4-3**: a default the column could not take faulted the create (500) instead of being dropped. | Step 3½ reads `TargetAttributesAsync` for the unfilled default columns. It drops any column that does not exist or is not `IsValidForCreate` (`NoSuchColumn`), and any value that fails `Fits` (`CannotTakeValue`), with `record_creation.default_value_dropped`. The wording is mapping's own: `CannotTakeValue` says "the mapped value" even for a default. It is shared with pinned mapping warnings, so it was left unchanged. Those are field mapping's own checks. Metadata that cannot be read drops every default ("it could not be checked"). |
| **K-4** (fixed; the change was contained) | The defaults now merge over the step-3 item rather than over the caller's item together with the mapped values. A default that does not map costs only itself, and the step-3 item and its mapped values stand. A fault while the defaults merge is caught: every default is dropped ("it could not be checked"), the step-3 row is created, and the fault's text stays in the log. This replaces the 503 `check_failed` recorded above. |
| **K-7a** | Two default keys that name one column (they differ only in case) are an `ArgumentException`. |
| **K-7b** | Defaults on a `MappingExclusion` table (the ontology-content tables) are an `ArgumentException`, as field mapping's exclusion is. |
| (new, found while fixing F1-2) | A `null` default value is an `ArgumentException`: a default fills a value, it never clears one. This is an equivalent mutant: `ToItemValue(null)` has no Web API form, so the null was already refused. The explicit guard stays for nullability. |

#### Recorded, not changed

- **K-5: "the check could not complete" drops the reference.** This is kept as the accepted deviation from NFR-01's
  wording ("a check that faulted is never a pass"), for a caller-owned **optional** reference only. It matches live
  Office: `OfficeRecordCreationService.CheckReferenceAsync`'s catch-all at lines 1346-1353 creates the record without
  the reference ("could not be checked just now"). It stays fail-closed for the value, because the reference is never
  written past a check that did not complete. The coordinator records it in `decisions.md`.
- **K-6 (§11): the existing overlap for `PostMappingDefaults`.** `RecordTableRule.ForPerson` (`RecordTableRules.cs`
  :97 matter, :122 project) names the for-person column and its table. `RecordTableRule.ServerDefaults` (:98-104,
  :123-129) names, per create path, the column and its precedence rule in words. For Office, that is
  `sprk_assignedtointernal`: "the contact the request names, else a mapped value, else the maker's linked contact".
  - **What the registry can drive:** the column, but not the value. `ServerDefault` is descriptive: it has no value,
    and the value (the maker's linked contact) is resolved per request, which only the adapter can do.
  - **Decision:** `PostMappingDefaults` stays opt-in and per request. 018b derives the default's column from the
    registry (the `ServerDefault` whose `Path` is "Office quick-create", which equals `rule.ForPerson.Column`) and
    supplies only the value.
  - **Not done:** restricting `PostMappingDefaults` to `ServerDefaults` columns in the service. F4-3 already makes an
    unknown column harmless. The restriction is cheap if the reviewer wants it.
- **Decided (coordinator, 2026-10-22; put to UAC-r2 for concurrence): DROP the maker's-contact default.** A maker's
  contact the caller cannot AppendTo is dropped with `record_creation.default_value_dropped` (the FR-08 mapped-value
  rule). It is not refused. A **named** Assigned To contact still refuses (UAC-r2 condition (b) item 1). Conditions for
  018b:
  - the Office adapter surfaces the `default_value_dropped` warning to the caller;
  - after a drop it **never** writes Assigned To itself, app-only.

#### Tests added (all fail before their fix; mutants below)

| File | Tests |
|---|---|
| `tests/unit/domain/Dataverse/RecordCreation/RecordTableRulesTests.cs` | `OptionalReferenceLookups_AreExactlyWhatTheOfficeCreateDropsToday`, using literals so the pin outlives 018c. `OptionalReferenceLookups_NeverHoldAColumnThatIsNeverAReference`. `IsNeverAReference_Fires` has 15 cases (for person, Assigned*, regarding pair, protected, filing, access), each also in upper case. `IsNeverAReference_DoesNotFire_OnTheDeclaredReferences` has 3 cases. |
| `tests/integration/seam/RecordCreation/RecordCreationServiceReferenceLookupSeamTests.cs` | Must-fire: `AnEventsAssignedTo_NamedAsAReference_IsAnArgumentError_NeverCreatedWithTheMakersContact` (the reviewer's probe), and `ALookupTheTableDoesNotDeclareAsAReference_IsAnArgumentError`, 5 cases including the matter's `sprk_assignedtointernal`. The filing test is now `AFilingLookupNamedAsAReference_IsAnArgumentError_NeverDropped`. Must-not-fire: the existing matter-type drop-with-warning test, unchanged. |
| `tests/integration/seam/RecordCreation/RecordCreationServiceMappingSeamTests.PostMappingDefaults.cs` | F1-2: `PostMappingDefault_ACallersExplicitNull_OnAChoiceOrFlag_IsKept`, a choice and a flag. F4-3: `PostMappingDefault_TheColumnCannotTake_IsDroppedWithADefaultWarning_AndTheRowIsCreated`, 3 cases (unknown, not creatable, wrong type). K-4: `PostMappingDefault_ThatDoesNotMap_IsDropped_AndTheMappedValuesStand` and `PostMappingDefault_WhenTheDefaultsMergeFaults_OnlyTheDefaultsAreDropped_AndTheMappedRowIsCreated`. K-7a: `PostMappingDefault_TwoKeysNamingOneColumn_IsAnArgumentError`. K-7b: `PostMappingDefault_OnATableFieldMappingDoesNotApplyTo_IsAnArgumentError`. And `PostMappingDefault_ANullValue_IsAnArgumentError`. |

#### Mutants (each run, then reverted)

| Mutant | Result |
|---|---|
| D1: `ValidateReferenceLookupColumns` not called (the 2a5f076857 behaviour) | Killed: the reviewer's probe test, all 5 `ALookupTheTableDoesNotDeclare…` cases, and the filing test. |
| D2: `DefaultsToFill` uses the 2a5f076857 rule (`null` is unsupplied for every kind) | Killed: both explicit-null cases. |
| D3: no fit check on defaults (every unfilled default reaches the merge) | Killed: all 3 `TheColumnCannotTake…` cases. |
| D4: `sprk_assignedtointernal` added to the matter's `OptionalReferenceLookups` | Killed: both registry pins. |
| E1: the 2a5f076857 merge (over the caller's item, mapped and default values together), K-4's base change only | Survived in this round. The tests had no non-throwing failure of the merge, and the catch masked the base in the fault test. **Killed in the re-check** (below) by `…WhenTheDefaultsMergeIsRefused503…`. |
| E1′: the 2a5f076857 merge **and** no catch on the defaults merge | Killed: `…WhenTheDefaultsMergeFaults…` (the fault reached `CreateAsync` and gave 503). |
| E2: no duplicate-key check | Killed: `…TwoKeysNamingOneColumn…`. |
| E3: no `MappingExclusion` check | Killed: `…OnATableFieldMappingDoesNotApplyTo…`. |
| E4: a `null` default allowed past the guard | Survived, an equivalent mutant: `ToItemValue(null)` has no form, so the default is refused anyway. Recorded above. |

#### Verification (fix round)

- `dotnet build Spaarke.sln`: 0 warnings, 0 errors.
- `tests/Spaarke.ArchTests`, built separately and run in full: 1347 passed, 0 failed.
- The filtered set: 2775 passed, 7 skipped, 0 failed (2782 in total). The set is `RecordCreation`, `FieldMapping`,
  `Office`, `RecordOwnership`, `SecureRoot`, `SearchIndex`, `CreatorPerson`, `RecordTableRule` and
  `MappingSourceCallerMask`.
- Per the coordinator, the unfiltered `Sprk.Bff.Api.Tests` was not run (the test host reached 7.7 GB).

### 018a re-check (scoped re-check of `2a5f076857..58e09ce726`: the five items resolved, then these)

| Item | Change | Test |
|---|---|---|
| **F3**: K-4 was unpinned (the only K-4 test faulted twice, and the catch masked the base) | Test only. The seam double `MappingUserClient` gains `FailOn` (`Func<string, DataverseUserResponse?>`), beside `ThrowOn`, for a non-throwing 429/503. | `PostMappingDefault_WhenTheDefaultsMergeIsRefused503_TheMappedValuesStand_AndOnlyTheDefaultIsDropped`. After the defaults' own `AttributeType` read for `sprk_regardingproject`, the mapper's `ManyToOneRelationships` and `EntitySetName` reads answer `Fail(503, RateLimited, "busy")`. The mapped `sprk_description` and `sprk_assignedto` are still written, and the only warning is `default_value_dropped` on `sprk_regardingproject`. |
| **K-2**: duplicate warnings when the defaults merge answers a failure and then a per-column map throws | The step-3½ catch records the warning count before the merge, truncates back to it, then adds one `Dropped` per default. | `PostMappingDefault_WhenTheMergeIsRefusedThenFaults_EachDefaultIsNamedOnce`: the whole merge answers 503, the first default alone answers 503, the second throws. There are exactly two warnings, one per default. |
| **K-3**: no seam test for unavailable defaults metadata | Test only. The behaviour came with the fix round. | `PostMappingDefault_WhenTheDefaultsColumnMetadataIsUnavailable_EveryDefaultIsDropped_AndTheRowIsCreated`, with `Mapping = null` (so the only column-metadata read is the defaults' own) and two cases: the read throws, and the read answers 503. Both defaults are dropped with `default_value_dropped` ("it could not be checked"), and the row is created. |

Mutants (each run, then reverted):

| Mutant | Result |
|---|---|
| F1 (= E1): the 2a5f076857 merge base, catch kept | **Killed** by the F3 test. E1 is now killed. |
| F2: no truncation in the catch | Killed by the K-2 test (`sprk_assignedto` was named twice). |
| F3: unreadable defaults metadata treated as "fits" | Killed by both K-3 cases. |
| F4: no catch on the defaults' column-metadata read | Killed by the K-3 throws case. |

Verification (re-check):

- `dotnet build Spaarke.sln`: 0 warnings, 0 errors.
- ArchTests, built separately and run in full: 1347 passed, 0 failed.
- The same filtered set: 2779 passed, 7 skipped, 0 failed (2786 in total).
- The unfiltered suite was not run.
