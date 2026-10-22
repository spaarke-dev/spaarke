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

### UAC-r2 acceptance (`0cae77ab77..4530e597f0`: no F items) and K-a

**UAC-r2's concurrence conditions for 018b.** UAC-r2 concurs with dropping the maker's-contact default (K-7), on two
conditions:

1. The Office adapter surfaces `record_creation.default_value_dropped` to the caller.
2. Nothing writes Assigned To app-only after the drop. That covers the adapter, a post-create step, and any other
   writer.

**K-a: the "check could not complete → drop" path for references had no test.** It is test only; the behaviour is
unchanged.

- `ScriptedUserClient` (the `CreateWorld` caller) gains `PrivilegesThrowFor`, beside the existing `PrivilegesFault`
  (a non-throwing status).
- New seam theory `AReferenceWhoseCheckCannotComplete_IsDroppedWithAWarning_AndTheMatterIsCreated`, with two cases:
  - the `prvAppendTosprk_mattertype_ref` privilege read **throws**;
  - the same read **answers 503**.
- In both cases the matter type is not written and the warning is `record_creation.reference_value_dropped` on
  `sprk_mattertype`. The practice area, whose check completed, is kept, and the matter is created.

**Observation (not changed, outside 018a's files).** The two cases end in different wording:

- The thrown read reaches the reference pass's own catch: "it could not be checked".
- The 503 is collapsed by `CallerWriteAuthorization.CallerMayAppendToOrganizationOwnedAsync` (lines 397-401) into
  `false`, which is no AppendTo. It therefore reads as the not-usable wording: "it was not found, or you do not have
  permission to use it".

Both drop the reference, so both fail closed. The difference is only the caller-facing reason for an organization-owned
reference, and it predates 018a. A 503 answer cannot produce the `ClientFailure → "could not be checked"` branch for a
registry reference: every declared reference is organization-owned, and the table's own Create/Append read failing
would refuse the create at step 5 anyway.

Mutants (each run, then reverted):

| Mutant | Result |
|---|---|
| Ka1: keep the reference when its check throws | Killed by the throws case. |
| Ka2: keep the reference whenever its check does not succeed, which includes the 503 | Killed by the 503 case, and by the existing denied-reference test. |

**#2238 (K-c, existed before 018a; for 018b, not fixed here).** At step 9, the server binds are not de-duplicated
case-insensitively against a **system-principal** item: `ownerid@odata.bind`, the creator-person bind and
`sprk_issecure`. The K3 removal of case-variant keys covers only the search-index columns. A server-built item that
carries one of those keys under another spelling could reach the body twice.

Verification (K-a):

- `dotnet build Spaarke.sln`: 0 warnings, 0 errors.
- ArchTests, built separately and run in full: 1347 passed, 0 failed.
- The same filtered set: 2781 passed, 7 skipped, 0 failed (2788 in total).
- The unfiltered suite was not run.

## 018b-1: inventory, then stopped before the swap (2026-10-22)

FULL rigor, opus at high effort, branch `task/fm-018b1` from `6ce5ec7929`. Item 0 (NFR-01) is done below. The swap was **not
started**, for the two reasons the brief names:

- **An inventory item has no landing place and no covering decision:** G1, "No matter type was supplied".
- **Further decisions are needed** before the adapter can be written without guessing (G2 to G7).
- **Size:** the pass is well over the agent's ~100-step limit (see "Sizing and proposed split").

No code or test changed. File:line references are at `6ce5ec7929`. Old core = `Services/Office/OfficeRecordCreationService.cs`;
service = `Services/RecordCreation/RecordCreationService.cs` (`.Mapping.cs` where stated).

### Inventory: the Matter and Project legs of `OfficeRecordCreationService`

The Office entry points are `OfficeService.QuickCreateAsync` (dispatch :1964-1969), `QuickCreateViaCreationServiceAsync`
(:2064-2108) and `MapCreationFailureStatus` (:2117).

| # | Old behaviour (file:line) | Lands in | Note |
|---|---|---|---|
| 1 | Not Matter/Project → `entity_type_not_supported` 400 (:615-623) | Unreachable: the dispatch sends only Matter/Project (OfficeService.cs:1964) | Goes with 018c |
| 2 | Name trimmed; blank → `name_required` 400 (:628-633, :763-768) | Endpoint `OFFICE_007` (`QuickCreateFieldRequirements.Validate`) and the `OfficeService` null guard (:1957-1961); the adapter trims into the payload | Unreachable code |
| 3 | No resolved `OwnerSystemUserId` → `owner_unresolved` 403 (:637-650, :772-785) | Service step 1 WhoAmI (service :299-316) → `PrincipalUnresolved`, mapped to `owner_unresolved` 403; **plus G3** | G2, G3 |
| 4 | Description trimmed; blank omitted (:655-658, :790-793) | Adapter payload | — |
| 5 | Matter type: `Guid.Empty` = unset; an app-only existence read (`CheckReferenceAsync` :1325-1355); not found → "…was not found…" warning, a fault → "…could not be checked…"; never refused (:660-677) | Service step 2 reference pass: `ReferenceLookupColumns` = `RecordTableRule.OptionalReferenceLookups` (RecordTableRules.cs:111; service :388-402; Mapping.cs:725-765). Checked **as the caller**: the AppendTo privilege plus a caller read of the row (CallerWriteAuthorization.cs:387-405) | Wording changes to K-3's uniform text (Mapping.cs:62, :763): **G6** |
| 6 | Practice area: the same (`ApplyReferenceAsync` :681, :1297-1312) | The same pass (RecordTableRules.cs:111) | G6 |
| 7 | Project type: the same (:796) | The same pass (RecordTableRules.cs:136) | G6 |
| 8 | BU defaults from the **caller's** BU: `sprk_searchindexname` (fill) and the `sprk_ai_search_index` lookup; a read fault → warning (:1427-1484) | Service step 8 `SearchIndexDefaultAsync` (service :1082-1131) from the **owning** team's BU. Same warning text. | Decided (FR-10, Q9): the same BU for an unfiled matter, the matter's BU for a project under it |
| 9a | Profile read fault → warning (:1505-1522) | Mapping.cs:233-243, the same text (`MappingConfigurationUnreadable` Mapping.cs:45) | — |
| 9b | No profile → silent no-op (:1524-1528) | Mapping.cs:245 | — |
| 9c | Profile with no rules → warning (:1530-1535) | Mapping.cs:247-253, the same text | — |
| 9d | Source read app-only and masked (#2188, :1537-1574); unreadable → `SourceUnreadableWarning` (:1583), and Default rules **still applied** | Mapping.cs:255-288: Read probe as the caller (FR-04), then `MappingSourceCallerMask` | **G7**: different text, and no Default rules when the source is unavailable |
| 9e | Rules overwrite (`CreateTimeFieldMapping.Apply`, :1576) | `ApplyFillOnly` (Mapping.cs:306) | Decided: Q6, ruling 1 of 2026-10-21 |
| 9f | Protected targets (`Matter/ProjectProtectedAttributes` :258-277) | `ProtectedFromMapping` (Mapping.cs:182-199): the same registry set, plus the server-owned columns; the same engine warnings | — |
| 10 | Mapping blanks the name → the requested name is restored, with a warning (:693, :824, :1363-1376) | Intentionally gone: fill-only never replaces the caller's name | Decided ruling 1; 2 of the 4 rewrites |
| 11 | Mapping writes a non-matter-type value → reverted to the requested type, or removed (:696, :1383-1409) | Intentionally gone: a requested type wins (fill-only). With none, a mapped reference to another table does not map into the merged item and is dropped with `mapped_value_dropped` (Mapping.cs:360-391; `Fits` :943 checks only "is a reference") | Decided ruling 1; 2 of the 4 rewrites |
| 12 | **"No matter type was supplied. Open the matter and set its type."**, unless a type warning was already given (:696-700) | **None**: see G1 | **G1** |
| 13a | Assigned To: the requested contact wins (:384-388) | Adapter: a bind in the caller's payload | It now gets AppendTo as the caller (step 5); decided, UAC-r2 (b) item 1. The code is pass 2's: G5 |
| 13b | A mapped value is kept (:390-395) | Fill-only | — |
| 13c | Else the maker's linked contact (:397-401; `ResolveMakerContactIdAsync` :415-431, which never throws) | `PostMappingDefaults` keyed by `rule.ForPerson.Column` (RecordTableRules.cs:97, :125; Mapping.cs:602-715). The adapter resolves the value with the same identity service | A default the caller cannot AppendTo is dropped (decided 2026-10-22). Its warning reaches `QuickCreateResponse.Warnings` through `outcome.Warnings` (service :259-260); pass 2 adds the tests |
| 13d | No linked contact → log `assigned_internal_unset`, blank (:403-406) | Adapter log | — |
| 14 | Owner = the caller's BU default team; none → `OFFICE_022` 403 (:501-513, :708-713, :840-845) | Service step 7, record-first (service :705-743): an unfiled matter gets the acting user's team (the same answer); a project under a matter gets the matter's BU team. `OwnerUnresolved` is mapped to `OFFICE_022` 403 (the service says 409) | Decided Q9 |
| 15 | Creator person stamp (:717, :848) | Service step 9 (service :1188-1211); the requester is WhoAmI's id | — |
| 16 | The number is never written, and is protected from mapping (:184-188) | Step 2 blocks the autonumber column in the caller's payload (service :374-378); `ProtectedFromMapping` adds it (Mapping.cs:188-189) | — |
| 17 | Number retry ×3 → `record_number_unavailable` 409 (:540-564, :719-722, :870-873) | Service step 11 (service :199-238), `Conflict`, the same code; a 412 is classified too (:1322-1324) | Mapped to 409, same code |
| 18 | Any other create fault propagates (→ 500 `OFFICE_INTERNAL`) | The service rethrows (service :230-237); the endpoint's generic catch is unchanged | — |
| 19 | Number read-back warning (:576-604) | Service step 12 `WarnIfNumberMissingAsync` (service :1450-1472), the same text | — |
| 20 | Assigned-access materializer after the create (:357-358, :730, :927) | Service step 12, roots (service :1387-1392) | — |
| 21 | `sprk_containerid` never written | Protected (RecordTableRules.cs:93, :121); never in the adapter's payload | — |
| 22 | Project: the pair is derived from the source **before** mapping (:800-814; `IsFiledUnderItsSource` :1051-1056; `DeriveFiledUnderPairAsync` :1064-1098). No catalog row → `record_owner_parent_undetermined` "ask an administrator"; a read fault → the same code, "Try again" (`FiledUnderUndetermined` :1236-1242) | Adapter (request mapping): `sprk_regardingrecordid` (canonical lower-case "D") and the `sprk_RegardingRecordType` bind, in the caller's payload | `OfficeSearchService.ResolveRegardingRecordTypeIdAsync` (OfficeSearchService.cs:554-574) collapses a fault and a missing row to `null`, so keeping both wordings needs the old read ported into the adapter, not reused |
| 23 | `CallerMayFileUnderAsync`: AppendTo **as the caller** on the derived pair, for every source (:1001-1026, :1116-1119) → `caller_cannot_file_under_parent` 403; no probe or token → `caller_rights_unverifiable` 500 | Service step 5: `CheckCallerMayCreateAsync` (2) always asks AppendTo on the pair's record, ordinary or secure (CallerWriteAuthorization.cs:114-131) → `NotFoundOrDenied` `record_creation.not_found`, mapped to `caller_cannot_file_under_parent` 403. No token → step 1 (G2) | G2, G5 |
| 24 | A mapped pair is fill-only over the derived pair; the canonical id spelling (:1121-1148) | Kept by construction: the adapter writes the canonical pair; `ApplyFillOnly` never writes a pair (CreateTimeFieldMapping.cs:186-199) | — |
| 25 | A secure source with any other pair → `secure_source_filing_mismatch` 403; an unreadable flag → undetermined (retryable); an **empty** flag → `record_owner_parent_undetermined` (non-retryable) (:1150-1177). Reached only when a **mapped** pair differs from the derived one | The mismatch is **unreachable** on the new path: G4. The empty-flag refusal still holds through the step-6 plan (SecureRootInheritance.cs:3740; SecureRootFilingGate.cs:128 → `record_owner_parent_undetermined`; `PlanRefused` default arm, service :1268), mapped to 403 | **G4** |
| 26 | Into isolation: `prvCreatesprk_project` plus AppendTo on every secure parent, as the caller; no token → `caller_rights_unverifiable` 500; `caller_cannot_create` 403 (:855-865, :945-994) | Step 6 plan with `CallerMayFileUnder` (service :613-616, :1218-1238); the Create privilege is step 5's (CallerWriteAuthorization.cs:63-90) → `Forbidden` `record_creation.denied`, mapped to `caller_cannot_create` 403 | — |
| 27 | Isolated create: the Secure team owner and `sprk_issecure` in the create (:867-868, :1249-1253) | Step 9 `IsolatedCreateFields` (service :1202-1211) | — |
| 28 | `CompleteIsolatedCreateAsync`: removed → 500 with its reason code; stranded or incomplete → warnings (:882-924) | Step 12 (service :1352-1385): `Undecided` 500, the same codes, the same texts with the noun templated | Mapped to 500, code passed through |
| 29 | `KindFor` (:1265-1273), `MapCreationFailureStatus` (OfficeService.cs:2117) | The service's `PlanRefused` (service :1257-1270) plus the adapter's refusal map | — |
| 30 | Membership event in the endpoint (OfficeEndpoints.cs:1860-1871) | Step 12 (service :1422-1427; `MembershipPublishingTables` :74-75), with the known owner team | Remove the endpoint publish in the swap commit |
| 31 | — (new) | A field-secured column in the caller's own payload is refused (step 5, CallerWriteAuthorization.cs:93-112); the I-1 re-stamp and I-16 run after the create (service :1394-1406) | The first is ADR-008 path A / FR-08 (the POML's purpose); the second was accepted by UAC-r2 (b) |

### Gaps and decisions needed before the swap

- **G1, no landing place: "No matter type was supplied" (old :696-700).**
  - The adapter cannot tell whether field mapping filled `sprk_mattertype`. `RecordCreationOutcome` reports no written
    columns, and the mapping warnings name only drops.
  - It is pinned by `Post_Matter_NameOnly_…`, `…WithEmptyGuidMatterType_…`, `…WithTypeSetOnlyByFieldMapping_…` (no
    warning) and `…AndNoTypeWasRequested_RemovesIt`.
  - Options:
    - (a) add `RecordCreationOutcome.WrittenColumns` (`Decision.Item.Columns` at step 11), which is new service surface
      and needs §11;
    - (b) the adapter reads `sprk_mattertype` back after the create, which costs one more app-only read;
    - (c) drop the warning, which needs an owner decision.
  - **Recommend (a).** The adapter also keeps the old suppression: no "not supplied" warning after a
    `reference_value_dropped` on `sprk_mattertype`.
- **G2, no token vs no Dataverse identity.**
  - Step 1 folds the WhoAmI failures `AccessDenied`, `UserContextRequired`, `OboExchangeFailed` and
    `OboNotConfigured` into one `PrincipalUnresolved` `sdap.access.deny.caller_unresolved`, with no `ClientFailure`
    (service :303-313; `ChildRecordEndpoints.IsUnresolvedCaller` :412-416).
  - Office needs 500 `caller_rights_unverifiable` for no token (UAC-r2 (b) item 2) and 403 `owner_unresolved` for no
    identity. The adapter cannot tell them apart.
  - Options:
    - (a) the service carries the WhoAmI failure as `ClientFailure` on that refusal (one property set; no new type);
    - (b) the adapter checks that a bearer token is present before calling, which is an adapter-side check (the
      ADR-008 tension).
  - **Recommend (a)**, mapping:
    - `UserContextRequired`, `OboNotConfigured` and `OboExchangeFailed` → 500 `caller_rights_unverifiable`;
    - `AccessDenied` → 403 `owner_unresolved`.
- **G3, the `owner_unresolved` precondition.**
  - Old: refused when the endpoint's app-only `ICallerSystemUserResolver` (OfficeEndpoints.cs:1815-1816) did not
    resolve the caller.
  - The adapter needs that systemuser id **before** the call anyway, to resolve the maker's contact for
    `PostMappingDefaults`. The service exposes no WhoAmI id.
  - **Recommend:** keep it as the adapter's input precondition, recorded as not an authorization check. The service's
    WhoAmI stays the identity check (ADR-028).
  - If it is dropped instead, a caller whom the resolver misses but WhoAmI names would be created without the maker
    default. That is a live change.
- **G4, `secure_source_filing_mismatch` is unreachable on the new path.**
  - The adapter writes the derived pair, and fill-only mapping never writes a pair. So with a secure source, a
    profile rule aimed at the pair is skipped with `mapping_rule_skipped`, and the project is created under the derived
    pair, into isolation.
  - Old: 403. New: 201. Isolation is kept; the refusal is not.
  - The mismatch tests construct `OfficeRecordCreationService` directly (SecureRootInheritanceWriterTests), so they
    stay green until 018c regardless.
  - **Decide:** is "only the derived pair is accepted" held by construction (recommended), or must the adapter refuse
    when a rule targets the pair under a secure source?
- **G5, `NotFoundOrDenied` is one refusal for several lookups.**
  - Step 5 returns one uniform `record_creation.not_found` whether the refused lookup is the derived pair's record or
    the named Assigned To contact (CallerWriteAuthorization.cs:327-351).
  - Pass 1 can only map all of it to `caller_cannot_file_under_parent` 403. Pass 2's Assigned To code then needs the
    refusal to name the refusing lookup, which is a service change. Do not parse the `Detail` text.
  - Plan this into pass 2.
- **G6, the reference-drop wording (decided by 018a K-3, but not among the brief's four rewrites).**
  - `Post_Matter_WithUnknownMatterType_…` asserts "matter type was not found".
    `OfficeQuickCreateFieldsContractTests.cs:111` asserts "practice area was not found".
  - The service says: "The value you chose for 'sprk_mattertype' was not written: it was not found, or you do not have
    permission to use it; the matter was created without it."
  - The "could not be checked" test can pass unchanged when its fault is a throw. A 503 instead reads as "not found or
    no permission" (018a K-a observation).
  - **Sign-off needed** to rewrite those assertions. Any project-type equivalent is in the same class.
- **G7, an unavailable mapping source.**
  - Old: the warning was `SourceUnreadableWarning`, and **Default rules still applied**.
  - New (FR-04): the warning is `MappingSourceReadProbe.SourceUnavailableWarning` ("The related record is not available
    to you, so no fields were copied from it."), and nothing is mapped.
  - An unreadable source is refused earlier by `QuickCreateSourceAccessFilter`, so this happens only when the app read
    or the caller read faults.
  - Pinned by `OfficeQuickCreateSourceFieldSecurityTests.cs:120`, and by `SecureRootInheritanceWriterTests.cs:1061` (the
    old class, green until 018c).
  - **Sign-off needed:** it is FR-04, but not on the brief's decided list.

### Draft refusal map (for the swap; final after G2 and G5)

| Service kind | Service code | Office code | Status |
|---|---|---|---|
| PrincipalUnresolved | `sdap.access.deny.caller_unresolved` | `owner_unresolved`, or `caller_rights_unverifiable` (G2) | 403 / 500 |
| OwnerUnresolved | `record_owner_parent_undetermined` | the same | 403 |
| OwnerUnresolved | any other (`record_owner_no_source`, resolver codes, `record_creation.ownership_exception`) | `OFFICE_022` | 403 |
| Conflict | `record_number_unavailable` | the same | 409 |
| Forbidden | `record_creation.denied` | `caller_cannot_create` | 403 |
| Forbidden | plan codes (`ReasonCreatorNoAccess`, `sdap.access.secure_flag_transition_only`, AccessInheritance server-only) | passed through | 403 |
| NotFoundOrDenied | `record_creation.not_found` | `caller_cannot_file_under_parent` (G5) | 403 |
| Undecided | plan or provisioning codes (`ReasonCreatorNoAccessUnverifiable`, `record_owner_secure_team_unresolved`, `sdap.inherit.*` row removed) | passed through | 500 |
| Undecided | `record_creation.no_decision` | `OFFICE_INTERNAL` | 500 |
| Unavailable | `record_creation.check_failed`, `record_creation.owner_unavailable` | `OFFICE_INTERNAL` (the old core propagated faults to the generic 500) | 500 |
| Invalid, BlockedColumn | any (the adapter builds the payload, so this means a server defect) | `OFFICE_INTERNAL` | 500 |
| Upstream | `DATAVERSE_*` | `OFFICE_INTERNAL` | 500 |
| any unmapped kind | — | `OFFICE_INTERNAL` (fail closed) | 500 |

The generic Office failure is `OFFICE_INTERNAL` 500: the endpoint's catch-all (OfficeEndpoints.cs QuickCreateAsync) and
the old core's fault path.

### Sizing and proposed split

These suites drive Matter/Project quick-create over HTTP through `OfficeQuickCreateTestWebAppFactory`:

| Suite | Tests |
|---|---|
| contract Matter | 33 |
| contract Project | 16 |
| contract Fields | 13 |
| `OfficeRecordOwnershipTests` | 12 |
| `SecureRootCreateRouteTests.OfficeQuickCreate_*` | 4 |
| `OfficeQuickCreateSourceFieldSecurityTests` | 4 |
| Issue1079 | 1 |

That is about 83 tests, plus `OfficeMembershipPublishingTests` (7, the endpoint publish).

- **The fixture models the old app-only boundary:** `IGenericEntityService.CreateAsync`, app reads of reference rows,
  and `CallerRecordAccessProbe` for AppendTo.
- **The host's `IDataverseUserClient` is `MappingSourceCallerReadDouble`,** which answers only the #2188 mask read.
- **The service asks the caller's Dataverse:**
  - WhoAmI;
  - table metadata (`Privileges`, `OwnershipType`, `PrimaryIdAttribute`);
  - `RetrieveUserSetOfPrivilegesByNames`;
  - attribute `IsSecured`;
  - `RetrievePrincipalAccess` for each lookup;
  - caller reads of organization-owned reference rows;
  - the record-type catalog;
  - mapping metadata (navigation properties, entity sets, `AttributeType`, `IsValidForCreate`);
  - the mask read.
- **The nearest reusable double** is `SecureChildOwnershipAiToolTests.ScriptedUserClient`. Its fixed lookup table lacks
  `sprk_projecttype_ref` and the mapping targets the contract profiles use (for example `sprk_client`).

Proposed split, each part on the same PR:

1. **018b-1a (this commit):** the inventory and G1 to G7.
2. **018b-1b, fixture only, no live change:**
   - a contract-host caller-Dataverse double: the mask read exactly as `MappingSourceCallerReadDouble` answers it, plus
     scripted caller answers that each test can configure;
   - wired into `OfficeQuickCreateTestWebAppFactory`;
   - a write-capture helper that views `UpdateRecordFieldsAsync`'s dictionary as an SDK `Entity`.
   - Every suite stays green on the old path. About 40 steps.
3. **018b-1c, the swap:**
   - the adapter;
   - the refusal map with its table-driven test;
   - removing the endpoint publish, in the same commit;
   - removing the constructor parameter;
   - re-pointing each test's mechanism helpers;
   - the agreed assertion rewrites;
   - mutants.
   - About 70 steps, once G1 to G7 are answered.

## 018b-1s: service outcome members (G1, G2, G5): commit 3a97cce150

No live behaviour change; the service still has no live caller.

- **`RecordCreationOutcome.WrittenColumns` (G1):** the keys of the step-11 create body, exactly as sent.
  - A lookup appears as its bind key in the metadata's navigation casing (`sprk_MatterType@odata.bind`). Compare case-insensitively, with and without the bind suffix.
  - It includes the server-added keys (owner, creator person, search-index default) and leaves out dropped values.
  - It is empty on every refusal.
- **`ClientFailure` on `PrincipalUnresolved` (G2):** the WhoAmI failure (`AccessDenied`, `UserContextRequired`, `OboExchangeFailed`, `OboNotConfigured`). The code and the 403 are unchanged.
- **`RecordCreationRefusal.RefusedColumn` (G5):** the logical lookup column, or `sprk_regardingrecordid` for the pair's record, including the unreadable record-type row.
  - It is set only on a step-5 `NotFoundOrDenied` refusal and is carried from a new `OwnedChildWrite.Outcome.RefusedColumn`.
  - It is null elsewhere, including on the step-6 `PlanRefused` and pre-write not-found refusals.
  - It is never rendered by the HTTP map.
  - The live child-records route sets it but never serializes it.
  - It holds the column name only, never the value (UAC-r2).
- **Known limit:** when a typed filing lookup and the pair name the same record, the existing `DistinctBy` names the typed lookup.
- **Tests:** 17 new, in `tests/integration/seam/RecordCreation/RecordCreationServiceOutcomeSeamTests.cs` and `RecordCreationServicePipelineSeamTests.PrincipalFailure.cs`. Five mutants, all killed.
- **§11:** see `decisions.md`, 2026-10-22 G1–G7.
  - `WrittenColumns`: no outcome member names written columns, and a read-back adds an app-only read and a race.
  - `ClientFailure`: an existing member, set on a second kind.
  - `RefusedColumn`: without it, an adapter would have to parse `Detail`.

## 018b-1b: Office contract-host caller-Dataverse double: commit 84d2add15b

Fixture only; no `src/` change.

**Files**, all in `tests/integration/contract/Api/Office/`:
- `OfficeCallerDataverseDouble.cs`: a closed-world double. Unknown requests throw `UnscriptedCallerRequestException`. Mask reads are passed through untouched to the existing `MappingSourceCallerReadDouble`.
- `RecordCreationWriteCapture.cs`: `factory.CaptureRecordCreationWrite()` exposes `.Entity` / `.Entities` as an SDK view, and `FailAttempt` drives the 412 retry.
- `OfficeCallerDataverseDoubleTests.cs`: 58 self-tests; three deliberate breakages made 11 of them fail.

**Counts:** every Office suite is unchanged. The filtered total went from 3753 to 3811 passed, 7 skipped.

**Notes for the swap:**
- The service picks the new record's id, so number read-back setups must match any id.
- New row ids need `CallerDataverse.WithRow(...)`.
- Turn old app-only "not found" or fault setups into `Missing`, `Respond` or `Fault` overrides.
- `HeldPrivileges` (endpoint filter) and `CallerDataverse.Held` are separate.
- `sprk_client` is a fixture-only lookup.
- A caller PATCH from step 12 would throw and must be scripted.

## 018b-1c: the swap (048058b8fe), re-pointed suites (2aa3b69650), new tests (09ec9d7d35)

**Live change.** Office quick-create Matter and Project now create through `RecordCreationService` as the caller (`OfficeService.QuickCreateViaCreationServiceAsync`).

- **Payload:** trimmed name and description, plus the registry's optional references through `ReferenceLookupColumns`.
- **Assigned To:**
  - a contact the caller names is a bind in the caller payload, so it is checked and refused as the caller;
  - otherwise the maker's linked contact is a `PostMappingDefaults` entry on `rule.ForPerson.Column`;
  - if there is no contact, the adapter logs `assigned_internal_unset`.
- **Project:** the pair is derived before the call, with both `record_owner_parent_undetermined` wordings. The id is lower-case "D".
- **G3:** the endpoint-resolved systemuser stays an input precondition (403 `owner_unresolved`).
- **G1:** the adapter emits `NoMatterTypeWarning` from `WrittenColumns`, suppressed after a `reference_value_dropped` on `sprk_mattertype`.
- **Membership:** the endpoint's membership publish was removed in the same commit. Service step 12 publishes once, for matters only.
- **`OfficeService` is now `internal`,** because its constructor takes internal types. It no longer depends on `OfficeRecordCreationService`; the class and its DI registration stay until 018c.
- **Invoice leg:** unchanged apart from the marker comment.

**Refusal map (`MapQuickCreateRefusal`), with a table test over the service codes (75 cases):**

| Service | Office | Status |
|---|---|---|
| PrincipalUnresolved + `AccessDenied` | `owner_unresolved` | 403 |
| PrincipalUnresolved, other or missing failure | `caller_rights_unverifiable` | 500 |
| NotFoundOrDenied, `RefusedColumn` = Assigned To | `assignee_inaccessible` (an existing Office code, so no NFR-07 addition) | 403 |
| NotFoundOrDenied, any other column or none | `caller_cannot_file_under_parent` | 403 |
| Forbidden `record_creation.denied` | `caller_cannot_create` | 403 |
| Forbidden plan codes (creator no access, secure flag, server-only) | passed through | 403 |
| OwnerUnresolved `record_owner_parent_undetermined` | passed through | 403 |
| OwnerUnresolved, other | `OFFICE_022` | 403 |
| Conflict `record_number_unavailable` | passed through | 409 |
| Undecided: creator-no-access-unverifiable, secure team unresolved, `sdap.inherit.*` | passed through | 500 |
| Anything else (fail closed) | `OFFICE_INTERNAL` | 500 |

**Changed assertions (agreed: fill-only rulings, G6, G7):**
1. Unknown matter type: "matter type was not found" → the K-3 drop text.
2. Unknown practice area: the same, for `'sprk_practicearea'`.
3. The two blanked-name tests: the "name you entered was kept" warning is now asserted absent.
4. A mapped non-type value, with a type requested: the "not a matter type" warning is now asserted absent.
5. A mapped non-type value, with no type requested: `mapped_value_dropped` plus the no-type warning.
6. `OfficeQuickCreateSourceFieldSecurityTests` → `MappingSourceReadProbe.SourceUnavailableWarning`.
7. Hard-coded ids → the captured id.

Mechanism-only re-points keep each test's claim. The full list is in commit 2aa3b69650.

**New tests (09ec9d7d35):**
- `OfficeQuickCreateAdapterContractTests`: Assigned To refused or written; the maker default dropped (warning, one write), used, or absent; G1 ×3; the WhoAmI theory (no token → 500, no user → 403); the `QuickCreateRequest` regarding-member reflection pin.
- `OfficeQuickCreateMembershipPublishTests`: one event per matter with the owner team; none for a project.
- `SecureRootCreateRouteTests`: G4 pins (secure and ordinary, with extra regarding members); G4 conditions 2 and 3; the AppendTo check is now asserted through the caller's Dataverse.
- `OfficeQuickCreateSourceFieldSecurityTests`: the BU read is scripted, and the warning is asserted absent.
- `ConfidentialClientMigrationSeamTests`: the real `DataverseUserClient` with no bearer token returns `UserContextRequired` before any exchange.

**Mutants (14, all killed):**
- one per map row group (m01–m07);
- G1 suppression;
- `Wrote` case-sensitivity;
- the endpoint publish re-added;
- the pair written in upper case;
- the `PostMappingDefaults` entry removed;
- `RegardingRecordId` added to the request (the reflection test fails with UAC-r2's exact message).

**Counts:** filtered suites plus Issue1079: 3927 passed, 7 skipped, 0 failed. ArchTests: 1352 of 1352.

## 018b-2, K2 and review round 1

**018b-2 (d53d3005b3, b629550198, 8b3e6d7d61).**
- Tests in `SecureRootCreateRouteTests.OfficeSecureOwner.cs`:
  - A Project under a secure matter: owned by the Secure Record Owners team (step 7 never asked), secure, and the Restricted floor set by the isolated completion. Only the Secure team's business unit is read for the search index.
  - Control: under an ordinary matter, the owner is the matter's BU team and the project is not secure.
  - Isolated ordering: no caller-BU team is needed. The ordinary-matter control gives 403 `OFFICE_022`.
  - The uniform AppendTo refusal under a secure matter.
  - An empty or unreadable flag gives 403 `record_owner_parent_undetermined`, and nothing is written.
- #2238 is fixed: step 9 writes the owner, the creator person, `sprk_issecure` and the search index each under one key (`ServerBindSpellingTests`).
- #2124: the create path writes a `Money`. The Repair half is a 052 criterion.

**K2 (1392035123, da8ee69e72).**
- Create-time mapping drops any rule whose target `RefuseAppOnlyWrite` refuses, and gives `mapping_rule_skipped` with the refusal code.
- `ProtectedFromMapping` unions `InheritedAccessPermission.OwnerColumns` and `CreatorColumns` (#2249).
- Exemption: `sprk_document` plus `sprk_accesspermission`, create only. UAC-r2 accepted it on two conditions, both met: the exemption stays that narrow, and the Repair must-not-fire test passes. There is no `TODO(#2249)` left.

**Opus adversarial review (one pass), four fix-now findings, all fixed in a94f1929d9:**
- **F2-1:** a removed isolated row passes its `ProvisionProjectEndpoint` code through at 500 with its detail (it was `OFFICE_INTERNAL`).
- **F3-2:** the map test enumerates the provisioning codes. The two old-core removal tests are ported to the Office route.
- **F4-3:** the assignee refusal now has one shape (`OFFICE_009` + `reasonCode assignee_inaccessible`, title Forbidden), built by the filter's own `Deny`. The `WithDescription` text is corrected (assignee, record-first owner).
- **F2-4:** the field-secured refusal text no longer says "from chat".

Known limits:
- A no-token request is refused 403 by the endpoint filter, before the service runs.
- `RefusedColumn` names the first lookup when two name the same record.

**Scoped Sonnet re-check:** no F findings. Its K1 is closed by `TheOnlyProvisioningReasonConstantOutsideTheCodePrefix_IsTheBodyKey`. The rest are recorded:
- K2: an out-of-class code fails closed;
- K3: the endpoint's assignee branch is keyed on `OFFICE_009` + `reasonCode`;
- K4: production reflection over constants, with no trimming.

**Verify at be6628b378:**
- sln: 0 warnings, 0 errors.
- ArchTests: 1353 / 1353.
- Filtered suites: 4428 passed, 7 skipped, 0 failed.

## UAC-r2 condition (b) review of 018b, and fix round 2 (29d06d80f3, 0a46acad7f)

UAC-r2 tested at 830c2b0f1c. **Every checklist item passed.** The one fix-now item was REJECT F1-1.

**F1-1: the OwnerUnresolved wording.** `OFFICE_022` stays 403. The detail now depends on the code:
- `acting_user_unresolved`, and `no_default_team` on a create that is not filed under a parent, keep the caller-unit wording.
- `no_default_team` on a Project filed under a parent gets neutral wording, naming the parent's business unit. The adapter passes in `filedUnderParent`.
- Every other code passes the service's detail through, for example `NoOwnerSource`'s "Try again".

**K1, taken in 018b.** The pane sends the maker's own contact back as Assigned To. Without this fix, a caller without AppendTo on that contact would be refused on every create. Now the quick-create defaults endpoint offers no assignee the caller cannot AppendTo:
- the check runs as the caller, through the service's own AppendTo check;
- it fails closed to no default.

The create then follows the agreed maker-default drop with a warning.

**Notes kept:**
- K2: the map test checks codes, not kind and code together. `ReasonCreatorUnresolved` as `OwnerUnresolved` maps to `OFFICE_022`, which can't happen for a caller and fails closed.
- K3: `SecureRootAccessColumnsNeverMappedOfficeTests` and the old-core `SecureRootInheritanceWriterTests` mismatch tests still build the dead core. They are re-pointed or deleted in 018c.

**Mutants:**
- all-fixed detail: 7 failures;
- flag ignored: 2;
- defaults check skipped: 4.

**Verify:** sln 0/0; ArchTests 1361/1361; filtered suites 4515 passed, 7 skipped.

## 018c: delete the Office core

`Services/Office/OfficeRecordCreationService.cs` (1588 lines) and its `OfficeModule` registration are deleted. `git grep -n OfficeRecordCreationService -- src tests docs` is empty (no tombstone). The Invoice leg, the pre-create stamps (017) and Repair are untouched; no `RecordCreationService` call was added for `sprk_invoice`.

**No unlanded check, default or error code.** Before deleting, the core was walked against the 018b inventory (rows 1–31, G1–G7). Every check, default and code lands in the service or the adapter, or was removed on purpose by 018b (G4: the mapped-pair refusals; rows 10–11: overwrite mapping). No security finding.

### Where `ResolveDefaultAssigneeAsync` went

`OfficeService.ResolveDefaultAssigneeAsync` (on `IOfficeService`), moved intact. It now reads the adapter's own `ResolveMakerContactIdAsync`, the one place the default assignee is decided, so the prefill and the create's post-mapping default are one answer.
- `GET /quickcreate/defaults` takes `IOfficeService`; its K1 AppendTo check is unchanged.
- The stale comments in `OfficeEndpoints.cs` and `quickCreateDefaultsService.ts` name the new home.
- No new endpoint, DI registration or package.

### Constants

No new constants class.
- `NumberUnavailableCode`: a literal `"record_number_unavailable"` in `OfficeQuickCreateContractTests`.
- `SecureFlagAttribute`: `SecurableEntityRegistry.SecureFlagAttribute`.
- `MatterEntity` / `ProjectEntity`: `QuickCreateFieldRequirements.GetLogicalName`.
- `Matter/ProjectProtectedAttributes`: `RecordTableRules.Get(t).MappingProtectedColumns`.

### `CreateTimeFieldMapping.Apply`

Deleted (no caller left). `ApplyFillOnly`, `CollectSourceColumns` and `RegardingPairAndResolverColumns` are kept. `callerSupplied` is non-nullable inside the engine now, and the "legacy overwrite path" branch comment is gone.
- `CreateTimeFieldMappingTests.Run` drives `ApplyFillOnly` over an empty target, which writes what `Apply` wrote. All engine tests are kept.
- `CreateTimeFieldMappingFillOnlyTests` lost `Apply_TheOverwriteEntryPoint_StillReplacesAnExistingValue` and `Apply_TheLegacyOverwriteEntryPoint_StillWritesAMappedPair_…` (intentionally removed, G4 / rows 10–11), and the helper's `fillOnly: false` branch.

### Guard changes

- `MappingSourceCallerMaskGuardTests`:
  - `KnownCallSites` 3 → 2 (step 3 and Repair).
  - The engine-surface fact now requires `ApplyFillOnly` only, and reports any other way in, so a restored `Apply` fails it.
  - `EngineMethods` still lists `Apply`: the synthetic `Apply(...)` controls stay meaningful, and a restored overwrite entry point is scanned by the same rule.
  - The allow-list reason and the docs no longer say "two entry points".
- `CreateOwnerResolutionGuardTests.Allowlist`: the Office entry (expired at 018) is removed; the list is empty.
- `RecordOwnerAssignmentCensusTests`: the three Office entries (`CreateMatterAsync`, `CreateProjectAsync`, `IsolateProjectCreate`) are removed, with a comment naming the unified create's `OwnedCreateFields` / `IsolatedCreateFields` that now own those writes.
- `RecordTableRulesRegistryGuardTests`: the Office tables are read from `QuickCreateFieldRequirements.GetLogicalName(Matter/Project)`, the map the adapter picks its table from.

### Per-file disposition

| File / test | Disposition | Where now |
|---|---|---|
| `RecordCreationAssignedInternalTests` (deleted file): prefill tests | re-pointed | `OfficeQuickCreateAdapterContractTests.GetDefaults_ThePrefill_IsTheContactTheCreateAssigns_AndNoneWithoutALink`, `GetDefaults_WhenTheContactCannotBeRead_GivesNoPrefill_NeverThrows` |
| same: `QuickCreate_*` (named / default / no link) | covered | `OfficeQuickCreateAdapterContractTests.Post_With…NamedContact…`, `Post_WhenTheCallerCanUseTheMakersContact_…`, `Post_WhenTheMakerHasNoLinkedContact_…`; `RecordCreationServiceMappingSeamTests.PostMappingDefault_*` |
| same: "a mapped value means the contact is not even looked up" | removed on purpose | the adapter resolves the default before mapping (018b) |
| `RecordCreationNumberReadBackTests` (deleted file) | re-pointed | `RecordCreationServicePipelineSeamTests.CreateAsync_AMatter_WhenTheRequestIsCancelledAfterTheCreateCommits_StillSucceeds_WithoutANumberWarning`; the other cases are in `OfficeQuickCreateContractTests.Post_Matter_WhenTheNumberCannotBeReadBack_…` and the seam's `…NumberReadBackFaults…` |
| `SecureRootAccessColumnsNeverMappedOfficeTests` (deleted file) | covered | `RecordCreationServiceMappingSeamTests.CreateAsync_ARuleTargetingASecureRootsFlagOrAccessColumn_IsSkippedWithTheWarning_…`, `…AnOrdinaryColumnRule_IsStillFilled_BesideASkippedSecureFlagRule` |
| `RecordCreatorPersonStampTests` (both theories) | re-pointed | the same file, over the Office contract host (matter and project) |
| `AssignedAccessWriterTriggerTests` Office section | re-pointed | the same file, `OfficeHost` over the Office route (share before return; a materializer fault never fails the create) |
| `RecordTableRulesTests` protected-view pins | re-pointed | the same file, on `RecordTableRules…MappingProtectedColumns` |
| `RecordTableRulesTests.OfficeCreate_MatterAndProject_ProtectExactlyTheSame…` | covered | `UnifiedCreate_EverySecureRoot_…` in the same file |
| `OfficeQuickCreateContractTests` :730 | literal | `"record_number_unavailable"`; assertion unchanged |
| `CrossSurfaceOwnerTests.TheLiveOfficeQuickCreate_KeepsTheActingUsersTeam_…_UntilTask018` | removed on purpose (018b record-first) | `SecureRootCreateRouteTests.OfficeQuickCreate_UnderAnOrdinaryMatter_IsOwnedByTheMattersUnitTeam_…` |
| `CrossSurfaceOwnerTests` office halves (unfiled owner; no-BU refusal) | covered | `OfficeQuickCreateProjectContractTests.Post_Project_Returns201_OwnedByCallersTeam_…`, `…WhenTheCallersTeamCannotBeResolved_Returns403_…` (and the matter twin) |
| `SecureRootInheritanceWriterTests` Office: `IsCreatedIntoIsolation` | covered | `SecureRootCreateRouteTests.OfficeQuickCreate_OfAProjectFromASecureMatter_IsCreatedSecure_EndToEnd`, `…UnderASecureMatter_IsOwnedByTheSecureTeam_…` |
| `WhenTheMakerCannotBeShared_TheProjectIsRemoved` | covered | `…OfficeQuickCreate_WhenTheMakerCannotBeShared_TheProjectIsRemovedAgain_Is500…` |
| `…WalledOffAfterThePlan_TheProjectIsRemoved_…` | covered | `…OfficeQuickCreate_WhenTheMakerIsWalledOffAfterThePlan_TheProjectIsRemovedAgain_…` |
| `…MakerCannotBeSharedNorTheProjectRemoved_WarnsSo`, `…CompensationDeleteAnswersSuccessButTheProjectSurvives…`, `…WalledOffAfterThePlan_AndTheProjectCannotBeRemoved_PromisesNoSelfHeal` | re-pointed | `SecureRootCreateRouteTests.OfficeFiling` (same names, over the route) |
| `…MakerIsOnTheSecureMattersNoAccessList…` | re-pointed | `…OfficeFiling.OfficeQuickCreate_WhenTheMakerIsOnTheSecureMattersNoAccessList_IsRefused_…` (403, `ReasonCreatorNoAccess`, same detail) |
| `…FromASecureMatter_WhenTheCaller/AppReadOfTheSourceFails_…`, `…FromAnOrdinaryMatter_IsAnOrdinaryProject_FiledUnderIt` | re-pointed | `…OfficeFiling` (theories; two new fixture switches `SourceAppReadFails` / `SourceCallerReadFailStatus`, reset by `ResetRoutes`) |
| `…FromASecureProject_WhenTheCallerReadOfTheSourceFails_…` | re-pointed | `…OfficeFiling.OfficeQuickCreate_FromASecureProject_IsFiledUnderIt_AndIsolated` (no project→project profile: the pair is derived, never read from the mapping) |
| `…FromAnOrdinaryProject_WhenTheCallerCannotAppendToIt…`, `…FromAnOrdinarySource_WhenTheCallerCanAppendToIt…` | re-pointed | `…OfficeFiling` (project source); the matter source is `…UnderASecureMatterTheCallerCannotAppendTo_IsRefusedExactlyAsAnOrdinaryOne…` |
| `…WhenThePairCannotBeDerived_…` (both), `…WhenTheCatalogReadFails_…` | re-pointed | `OfficeQuickCreateAdapterContractTests.Post_Project_FromARecordWhosePairCannotBeDerived_Returns403Undetermined_AndWritesNothing` (theory: no row → "Ask an administrator", fault → "Try again.") |
| `…CallerCannotFileUnderTheSecureMatter`, `…FromAnOrdinaryMatter_WhenTheCallerCannotAppendToIt`, `…RightsReadCouldNotAnswer` | covered | `SecureRootCreateRouteTests.OfficeQuickCreate_WhenTheCallerCannotFileUnderTheSecureMatter_Is403…`, `…UnderASecureMatterTheCallerCannotAppendTo_IsRefusedExactlyAsAnOrdinaryOne…` |
| `…UnderAMatterWhoseFlagCannotBeRead` | covered | `…OfficeQuickCreate_UnderAMatterWhoseSecureFlagIsEmpty_Is403Undetermined…`, `…CannotBeRead_Is403Undetermined…` |
| `…CallersRightsCannotBeChecked`, `…FromAnOrdinaryMatter_WhenTheCallersRightsCannotBeAsked` | covered | `OfficeQuickCreateProjectContractTests.Post_Project_WhenTheRequestCarriesNoBearerToken_Returns500CallerRightsUnverifiable_…`, `OfficeQuickCreateAdapterContractTests.Post_WhenWhoAmIFails_…` |
| `…CallerCannotCreateProjects` | covered | `OfficeQuickCreateContractTests.Post_QuickCreate_WhenTheCallerLacksTheTablesCreatePrivilege_Returns403_…` |
| `…PlansRightsReadThrows`, `…PlansRightsReadAnswersNone`, `…RightsReadThrows` | covered (the two-read mechanism is gone) | `RecordCreationServicePipelineSeamTests.CreateAsync_WhenACallerCheckThrows_Is503_AndCreatesNothing`, `OfficeQuickCreateContractTests.Post_Matter_WhenTheRightsProbeThrows_Returns403_FailClosed`, and the uniform AppendTo refusal above |
| `…AMappingRuleTargetingTheSecureFlag_IsSkipped` | covered | the seam's `SecureRootAccessColumns` tests (above) |
| `…MappingFilesItUnderAnotherRecord_IsRefused` / `…KeepsTheMappedPair`, `…MappingWritesHalfAPair`, `…MappingNamesAnotherRecord_AndTheSourceFlag…` (×2), `…MappingWritesItsOwnPairInAnotherSpelling` | removed on purpose (G4: no mapping rule writes the pair; the derived pair is written by construction) | the canonical lower-case "D" spelling: `SecureRootCreateRouteTests.OfficeQuickCreate_UnderASecureMatter_ABodyWithRegardingMembers_IsFiledUnderTheDerivedPair_AndSecure` (its `ShouldBeFiledUnderTheDerivedPair`); the skipped pair rule: `…WithAPairRule_…` |
| `OfficeCallerRightsSetup.cs` | deleted | no user left |

### §11 answers

No new surface in `src`: no endpoint, DI registration, package, column or class. `IOfficeService.ResolveDefaultAssigneeAsync` is a member moved from the deleted concrete class (which the endpoint took before) onto `IOfficeService`: `OfficeModule` registers only the interface, and the method uses `OfficeService`'s own fields, so an interface member is the smallest surface (a concrete parameter would need a new DI registration).
1. **Existing:** `OfficeRecordCreationService.ResolveDefaultAssigneeAsync`, which is deleted here.
2. **Extension:** `IOfficeService` is extended; nothing new is added.
3. **Cost of doing nothing:** the defaults endpoint keeps the 1588-line dead core alive, with its own maker-contact resolution beside the adapter's, so the prefill could drift from the server default (task 100).

The two test-fixture switches on `SecureRootCreateRouteFixture` are test-only.

### Verification

- `dotnet build Spaarke.sln`: 0 warnings, 0 errors.
- ArchTests: 1361 / 1361.
- Filtered `Sprk.Bff.Api.Tests`, run 1 (Office | RecordCreation | FieldMapping | RecordOwnership | CreatorPerson | CrossSurface): 2472 passed, 7 skipped, 1 failed. The failure was `CreateTimeFieldMappingTests.Apply_ARuleThatThrows_…`: its `ThrowingSet` seam also threw in fill-only's supplied-column snapshot, which reads the protected set before any rule runs. The seam now throws from the second lookup on, so it reaches the per-rule guard the test pins; the assertions are unchanged. The `CreateTimeFieldMapping` re-run is 134 / 134.
- Run 2 (SecureRoot | AssignedAccess | OwnedChildWrite | SecureChildOwnershipAiTool | RouteAuthorization | Census | MappingSourceCallerMask): 1308 / 1308. The two runs overlap: some SecureRoot tests carry "Office" in their names.
- Before the core was deleted, the re-pointed suites (route, adapter contract, CrossSurface, writer, AssignedAccess, CreatorPerson, RecordTableRules, pipeline seam, Office contract) were 299 / 299.
- The assertions in the Office QuickCreate Contract, Project and Fields suites are unchanged (one constant became its literal).

**Known limit (test seam only):** `ApplyFillOnly`'s supplied-column snapshot runs outside the per-rule try/catch, so a protected set whose `Contains` throws would escape. The real sets (`RecordTableRules`' `HashSet`s) never throw. Listed, not fixed (out of scope).

**Mutants (each reverted):**
- (a) `ResolveDefaultAssigneeAsync` always returns null: `GetDefaults_ThePrefill_IsTheContactTheCreateAssigns_AndNoneWithoutALink` fails (2 of 2 cases).
- (b) `Apply` restored, with an unmasked call site in `OfficeService`: `EverySourceHandedToTheMappingEngine_PassedThroughTheCallerMask` and `TheEngine_OffersNoOtherPathThatTakesASourceEntity` fail (2 of 79).
- (c) the Office allow-list entry re-added: `CreateOwnerResolutionGuardTests.EveryAllowlistEntry_StillUsesTheName` fails (a stale entry for a gone file).

### 018c review (Opus adversarial, one pass)

No production defect. **F3** (fixed): `OfficeQuickCreate_FromAnOrdinaryMatter_IsAnOrdinaryProject_FiledUnderIt(true)` set
`SourceCallerReadFailStatus` without proving the read failed; it now asserts `SourceUnavailableWarning`. **K4** (fixed): the
`ApplyFillOnly` "Never throws" comment now says a faulting rule never escapes and the snapshot is unguarded; the §11
sentence above is corrected. **K2** (known limit, not fixed): the re-pointed cancellation seam test cancels in the number
read-back, not in the create callback, and covers Matter only (the deleted test covered Project too). Not a live defect:
nothing observes `ct` between step 11 and the read-back, and step 12 uses `CancellationToken.None`. A step that starts
using `ct` there would still be caught.
