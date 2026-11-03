# Task 020: Matter and Project creates through the child-records route (FR-15, FR-03)

> Rigor FULL. Branch `task/fm-020`, from 38e82f527c. The brief's overrides are binding:
> - **No wizard work.** Only the BFF route, `bffChildWriteAdapter.ts`, the doc comment in `provisioningService.ts`, and tests.
> - **No deploys** and **no live Dataverse writes**.
> - **Off-limits while task 041 runs:** `RecordCreationService*.cs`, `RecordTableRules.cs`, `SearchIndexNameResolver.cs`, `Services/Ai/**` and `SearchIndexDefaultTests.cs`. None of them was touched.
> - **No search-index assertions.** The tests assert no search-index value (`sprk_ai_search_index` or `sprk_searchindexname`).

## What changed

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Api/ChildRecordEndpoints.cs` | Adds `sprk_matter` and `sprk_project` to `CreateTables`. Adds an optional query, `?mappingSourceEntity=&mappingSourceId=`. Adds the single switch `InferMappingSourceTables` (`sprk_matter`, `sprk_project`) and the pure helper `MappingSourceOf`. The handler passes `RecordCreationMapping.ForCaller(probe, bearer, table, id)` to the service. When no source is named and the table is not in the infer set, it passes `Mapping = null` as before. |
| `src/client/shared/Spaarke.UI.Components/src/utils/adapters/bffChildWriteAdapter.ts` | Adds `sprk_matter` and `sprk_project` to `BFF_CHILD_CREATE_TABLES`. Adds an optional `IBffCreateOptions { mappingSource }` to `createChildRecordViaBff` and `createRecordViaBffWithWarnings`. The adapter adds the query only when a source is passed, and **never changes the body**. |
| `src/client/shared/Spaarke.UI.Components/src/utils/adapters/index.ts` | Exports the two option types. |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/provisioningService.ts` | Doc comment only: the provisionSecureProject split (below). |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` | Extends two reasons: the governed file `Api/ChildRecordEndpoints.cs` and the handler decision `POST /api/v1/child-records/{table}`. Both cover the roots and the fact that `mappingSource` authorizes nothing. |
| `tests/integration/seam/ChildRecords/ChildRecordMatterProjectCreateSeamTests.cs` (new) | A partial of `RecordCreationServiceMappingSeamTests`. It drives the real handler over the real service. |
| `tests/integration/seam/ChildRecords/ChildRecordSecureProjectCreateSeamTests.cs` (new) | A partial of `SecureRootInheritanceWriterTests`. It drives the real handler, the real `SecureRootFilingGate` and provisioning's fixture. |
| `tests/integration/seam/ChildRecords/ChildRecordCreateTablesAgreementTests.cs` (new) | The agreement test: server `CreateTables` == client `BFF_CHILD_CREATE_TABLES` ∪ `RibbonOnlyCreateTables`. Includes must-fire controls and the census-filter control. |
| `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.ChildRecordRoutes.cs` | One `.Where` plus a one-line comment (ruling 1; see "Existing-test exceptions"). |
| `src/client/shared/.../__tests__/bffChildWriteAdapter.test.ts` | Adds to the list test, swaps the fixture (ruling 2) and adds a new describe block. |

### Query parameters, not a request DTO

These parameters replace the POML's "request DTO" wording (ruling 3). The body stays the record's own columns: the route has always taken the Web API payload as is, and a wrapper DTO would change the request shape for every existing table. The parameter names are exactly `mappingSourceEntity` and `mappingSourceId`.

The route never answers these parameters with 400. When a source is half-named, malformed, a non-GUID or `Guid.Empty`:
- it is passed to the resolver as Undetermined;
- nothing is mapped;
- nothing is inferred;
- the response carries one warning (`MappingSourceResolver.SourceUndeterminedWarning`);
- the values are never echoed in a warning or a log message.

## §11 for `mappingSource` and `InferMappingSourceTables`

1. **Existing.** `RecordCreationRequest.Mapping` / `RecordCreationMapping.ForCaller` and `MappingSourceResolver` already exist (W2). This is endpoint binding plus pass-through; no new service, abstraction or DI registration. `CallerRecordAccessProbe` is already registered (`ExternalAccessModule.cs:192`).
2. **Extension.** It extends the existing route and handler. The only new surface is two optional query parameters, one internal set and one internal helper inside `ChildRecordEndpoints.cs`.
3. **Cost of doing nothing.** Without it, the Matter and Project wizards (tasks 021 and 022) cannot stop mapping on the client. FR-15 needs the route to create them, and FR-03 needs a caller to name the source. A route create of a matter or project would carry no server mapping.

**Placement Justification:** this extends an existing BFF route. There is no new endpoint, service, package or background work.

## provisionSecureProject split (AC4)

**AC4 reading** (ruling 5):
- A Project filed under a secure parent is created into isolation by the service. Proven by the seam tests in `ChildRecordSecureProjectCreateSeamTests.cs`:
  - the project is owned by the Secure Record Owners team in the create itself;
  - `sprk_issecure` is set in the create;
  - the record is completed in the request with the flag, its own container and the creator's share;
  - `ShouldBeSecure`.
- The user's Secure toggle on a new root Project stays on `/external-access/provision-project` (UAC-r2). `sprk_issecure` is a server-only, field-secured column.
- Task 022 decides the wizard branching.

Steps that remain on `provisionSecureProject`: the whole call. It is a single POST to `/api/v1/external-access/provision-project`, so nothing is dropped from it. Dropping or splitting it would change the UAC-r2 contract, which is escalation trigger 1. Task 020 changes only its doc comment (`provisioningService.ts`, above `provisionSecureProject`).

### Open for 022

- The wizard needs two branches:
  - a project filed under a secure matter or project goes through `POST /api/v1/child-records/sprk_project` (server isolation; no `provisionSecureProject` call);
  - the user's own Secure toggle on a project that inherits nothing stays on `provisionSecureProject`.
- Decide what the wizard does when both apply.

## Real-world check: no wizard starts using the route early

Adding `sprk_matter` and `sprk_project` to `BFF_CHILD_CREATE_TABLES` moves no current writer onto the route, and makes none of them refuse with `bff_not_configured`. Every Matter and Project creator uses the unwrapped Xrm data service:

- **Shared services.** `Spaarke.UI.Components/src/services/matterService.ts:338` and `projectService.ts:368`. Callers: `CreateMatterWizard`, `CreateProjectWizard`, `SummarizeFilesDialog` and `CreateProjectStep`.
- **Code-page hosts.** `src/solutions/CreateMatterWizard/src/main.tsx:53` and `src/solutions/CreateProjectWizard/src/main.tsx:53` use `createXrmDataService` (not wrapped).
- **`InAppWizardHost`.** It passes the plain `dataService` at :307, :321 and :377. Only `todoDataService` is wrapped by `withBffChildWrites` (:210-214).
- **LegalWorkspace.** `matterService.ts:222` uses its own `_webApi`.

So `withBffChildWrites` never sees a matter or project create today. Tasks 021 and 022 move them.

## Failing-first evidence

**C# (the new tests: 32).** The route's pre-change behaviour was put back in place:
- `sprk_matter` and `sprk_project` removed from `CreateTables`;
- `InferMappingSourceTables` emptied;
- `mapping = null` forced before the service call.

The new signature was kept so that the tests compile. With the HEAD file itself they do not compile, which is itself red. Filter: `RouteCreate_|ChildRecordCreateTablesAgreementTests|MappingSourceOf_|ChildRecordRoute_AProject|TheCensusTheory`.
- **RED: 24 failed, 8 passed.** Every new behaviour test failed:
  - the matter and project owner tests;
  - the named and inferred mapping tests;
  - the denied-versus-missing test;
  - the 6 malformed-source cases;
  - the existing table with a named source;
  - the secure-flag theory for both tables;
  - the 4 secure-project route tests;
  - the 4 agreement tests;
  - `MappingSourceOf_EveryCreateTableThatDoesNotInfer…`.
  The 8 that passed were:
  - the 6 pre-existing `SecureChildOwnershipAiToolTests` route tests matched by the filter;
  - `TheCensusTheory_CoversEveryCreateTable` (the census is unaffected when the matter is absent);
  - `RouteCreate_AnExistingTableWithNoSource_AsksForNoMapping…`, the guard that the old behaviour is unchanged, so it must pass both before and after.
- **GREEN: 32 of 32 passed.** The theory guard was added later, giving 33. Its own red and green results are under "Existing-test exceptions".

**Jest** (`bffChildWriteAdapter.test.ts`):
- RED, with the adapter from HEAD: **5 failed, 39 passed**. The failures were:
  - `lists exactly the census create tables`;
  - both `creates a sprk_matter/sprk_project…` cases;
  - `sends a named mapping source…`;
  - `passes the mapping source…`.
- GREEN: **44 of 44 passed**.

## Acceptance criteria

| # | AC | Result | Evidence |
|---|---|---|---|
| 1 | `CreateTables` and `BFF_CHILD_CREATE_TABLES` both contain `sprk_matter` and `sprk_project`, and a test asserts the sets agree | PASS | `ChildRecordCreateTablesAgreementTests.TheServerCreateTables_AreTheClientCreateTables_PlusTheRibbonOnlyTables` (server == client ∪ the three ribbon-only tables, ruling 2), plus two must-fire controls. Jest list test. |
| 2 | Optional `mappingSource`; when absent, existing tables behave exactly as before | PASS | `RouteCreate_AnExistingTableWithNoSource_AsksForNoMapping_EvenWithAProfileForItsParent` (no profile read, no probe, no warnings); `MappingSourceOf_EveryCreateTableThatDoesNotInfer_AsksForNoMappingWhenNoSourceIsNamed`. The existing contract tests are unchanged and green (verification below). |
| 3 | Matter and Project are owned per FR-09 and mapped fill-only per FR-02 | PASS | `RouteCreate_AnUnfiledMatter_IsOwnedByTheCallersBusinessUnitTeam…`; `RouteCreate_AProjectFiledUnderAMatterOfAnotherUnit_IsOwnedRecordFirst_ByTheMattersTeam`; `RouteCreate_AMatterWithANamedSource_KeepsTheUsersValue_AndFillsTheColumnTheUserNeverTouched`; `RouteCreate_AProjectWithNoSource_InfersItFromTheMatterItIsFiledUnder_FillOnly`. The I-1 stamp does not apply: `sprk_matter` and `sprk_project` are `CoreAncestorResolver.CoreRecordEntities` (roots), not stamped children (`IsStampedChildEntity` is false), so there is no stamp column to assert. The stamp's own reads still run through the real service (`StampWorld`). |
| 4 | A secure Project created through the route goes into isolation; the notes list the remaining `provisionSecureProject` steps | PASS (with the AC4 reading above) | `ChildRecordRoute_AProjectUnderASecureMatter_IsCreatedIntoIsolation_AndComesOutSecure`; `…UnderAnOrdinaryMatter_StaysOrdinary`; the split above. |
| 5 | Negative: no Create or Append-To on the parent gives a uniform refusal; an unreadable mapping source gives no mapping and no oracle | PASS | `ChildRecordRoute_AProjectUnderASecureMatterTheCallerCannotAppendTo_IsTheUniformNotFound_AsAMissingMatter` (same status, code and detail; no id echoed; nothing written); `…WhenTheCallerIsOnItsNoAccessList_Is403_AndNothingIsCreated`; `RouteCreate_ANamedSourceTheCallerCannotRead_MapsNothing_AndAnswersExactlyAsAMissingSource` (identical warnings; no source read). |
| 6 | RouteAuthorizationGuardTests pass with the ledger; BFF build has 0 errors; BFF and jest tests green; publish delta and CVE recorded | PASS | See "Verification": the solution builds with 0 errors; ArchTests are green (the ISS-005 timing guard passed when rerun on its own); the BFF suites are green after the approved swap; jest 44 of 44. Publish delta +774 bytes; no vulnerable packages. |
| 7 | Negative: the user value wins; an untouched mapped field is filled; the payload carries no key the user did not enter | PASS | The user value wins and the untouched field is filled: the two `KeepsTheUsersValue` tests. Payload (brief override: the route and adapter add nothing to the body): jest `creates a %s through the route with no query and the unchanged payload` and `sends a named mapping source as query parameters only` (body `toEqual(payload)`). The server reads the source from the query and never from the body. |
| 8 | The existing child-record route contract tests pass unchanged for the existing tables | PASS | See "Verification": `ChildRecord*` (including `ChildRecordCreateRefusalContractTests`), `SecureChildOwnershipAiToolTests*` and the route suites all pass. The only existing-test edits are the approved exceptions below, which change example tables and one census filter, never a contract assertion. |

Extra coverage, asserting current behaviour without changing it (ruling 1): a caller-supplied `sprk_issecure` on a Matter or Project create, where the column is field-secured as it is live, is refused with 403 `child_record.denied` and nothing is created (`RouteCreate_ACallerSuppliedSecureFlagOnARoot_IsRefused403_AndNothingIsCreated`).

## Existing-test exceptions (approved)

| File:test | Old | New | Reason |
|---|---|---|---|
| `SecureChildOwnershipAiToolTests.ChildRecordRoutes.cs:473`, `TheCensusTheory_CoversEveryCreateTable` | `.BeEquivalentTo(ChildRecordEndpoints.CreateTables.Where(t => !SecureRootInheritance.Inherits(t)),` | `.BeEquivalentTo(ChildRecordEndpoints.CreateTables.Where(t => !SecureRootInheritance.Inherits(t) && !SecureRootInheritance.IsParent(t)),` plus a one-line comment | `sprk_matter` is a top root (a parent that inherits from nothing), not a secure child. Its cases are in `ChildRecordMatterProjectCreateSeamTests`. Must-fire: `ChildRecordCreateTablesAgreementTests.TheCensusFilter_ExcludesTheMatter_ATopRootOnTheRoute_AndNothingElseTheOldFilterKept`. Ruling 1. |
| `bffChildWriteAdapter.test.ts`, `lists exactly the census create tables` (was :40) | the list without matter and project | adds `'sprk_matter'` and `'sprk_project'` | AC1. Ruling 2. |
| `bffChildWriteAdapter.test.ts`, `refuses a table that is not created through the BFF` (was :118-123) | example table `sprk_matter` | `NON_BFF_TABLE` (`'account'`) | `sprk_matter` is now a BFF create table; the intent is unchanged. Every assertion is byte-for-byte the same. Ruling 2. |
| `bffChildWriteAdapter.test.ts`, `passes reads, deletes and non-child writes through unchanged` (was :143-158) | `sprk_matter` / `{ sprk_mattername: … }` | `NON_BFF_TABLE` / `NON_BFF_PAYLOAD` | Same reason as above. |
| `bffChildWriteAdapter.test.ts`, `REFUSES a child write when the host wired no BFF fetch` (was :160-175) | `sprk_matter` / `{ sprk_mattername: … }` | `NON_BFF_TABLE` / `NON_BFF_PAYLOAD` | Same reason as above. |

| `SecureChildOwnershipAiToolTests.ChildRecordRoutes.cs:437`, `ChildCreate_OfATableOutsideTheCensus_IsRefused400` | `[InlineData("sprk_project")]` | `[InlineData("sprk_servicerequest")]` | `sprk_project` is now a create table. The intent, "a real sprk_ table the route does not create", is unchanged. The other two rows and every assertion are byte-for-byte the same. Approved as option A, with conditions. The verify-suites run found it: the only failure, `(table: "sprk_project")`. Before the swap, `sprk_servicerequest` was confirmed absent from `CreateTables`. No POML 021–034 mentions it, and no open PR does either. The only project POMLs that mention it are 017 (a chat-path `RecordTableRules` entry, not the route) and 070 (the edge registry). |

The named constant is guarded by `the pass-through example table is not a BFF create table`. The theory's rows are guarded by `ChildRecordCreateTablesAgreementTests.TheOutsideTheCensusTheory_NamesOnlyTablesTheRouteDoesNotCreate`, which reads the theory's own `[InlineData]` by reflection, so the guard and the theory cannot drift. With the old `sprk_project` row it was RED: the guard failed, and so did the theory row. After the swap it was GREEN: the guard, all three rows and the census theory passed.

## Findings and deferred items

- **F-1 (for main to file or route): the Matter create has no registry-level guard for a caller-supplied `sprk_issecure`.**
  - In the harness with no field security, `POST /child-records/sprk_matter` with `sprk_issecure: true` returned 201 and wrote the flag. The Project refuses with `secure_flag_transition_only`, because it inherits.
  - Live, `sprk_issecure` is field-secured, so the caller-context write is refused (403 `child_record.denied`), and the seam test asserts that path.
  - The only guard is therefore field security. A server-side refusal belongs in `RecordTableRules.cs`, which is off-limits to task 020 (task 041). Not fixed here.
- **Deferred (main files the issue): should the ribbon-only tables (`sprk_budget`, `sprk_kpiassessment`, `sprk_billingevent`) join the client `BFF_CHILD_CREATE_TABLES`?** They are created through the route only by ribbon commands, so the agreement test names them in `RibbonOnlyCreateTables`.
- **Deviation:** the publish delta ran from a scratch copy of `publish-delta.ps1` with different temporary worktree paths. This avoids a collision with task 041, which may be running the same script at the same time on `C:\tmp\hpdm` and `C:\tmp\hpdb`. The logic is unchanged.

## Verification

`projects/field-mapping-server-write-path-r1/scripts/verify-suites.sh` was run over 7b31722b5e + a5b11bcb72, before the census-theory swap.

- **Solution build:** 0 warnings, 0 errors. The BFF builds with 0 errors.
- **ArchTests, full:** 1910 passed, 1 failed. The one failure was the ISS-005 timing guard, `ContactCreateOwnerPolicyGuardTests.Control_ManyAssignmentsToOneLocal_ResolveInBoundedTime`: "resolving 60 assignments took 00:00:10.76". At the time the machine had 1.4 GB free RAM, two other sessions' testhosts were running, and this task's publish builds ran alongside. It is not in the change set. **Rerun on its own** (`FullyQualifiedName~ContactCreateOwnerPolicyGuardTests`): **129 of 129 passed** (39 s). `RouteAuthorizationGuardTests` passed with the updated ledger, inside the 1910.
- **BFF suites, the project filter:** 6596 passed, **1 failed**, 7 skipped.
  - **Failure:** `SecureChildOwnershipAiToolTests.ChildCreate_OfATableOutsideTheCensus_IsRefused400(table: "sprk_project")`, fixed by the approved swap above.
  - **Rerun after the swap** (`ChildRecordCreateTablesAgreementTests | ChildCreate_OfATableOutsideTheCensus | TheCensusTheory`): 9 of 9 passed.
  - **Skipped:** the pre-existing `OfficeEndpointsContractTests` skips, among others; none is new.
  - **AC8:** the filter covers `ChildRecord*` (including `ChildRecordCreateRefusalContractTests`), `SecureChildOwnershipAiToolTests*`, `SecureRoot*` and `RecordCreation*`. The existing route contract tests passed with no change; the only edits to existing tests are the approved exceptions.
- **New C# tests:** 33 of 33 green, the 32 from the red/green run plus the theory guard.
- **Jest** `bffChildWriteAdapter.test.ts`: 44 of 44, rerun after the lint-staged prettier pass.
- **tsc** (`Spaarke.UI.Components`): 9 errors, none of them in a changed file. All 9 are pre-existing and environment-bound:
  - `@spaarke/auth` and `@spaarke/sdap-client` not resolved in this worktree;
  - three `'error' is of type 'unknown'` errors in `FileUploadService.ts`.

## Publish delta and CVE

`publish-delta.ps1` (scratch copy with the temporary paths `C:\tmp\f20m` and `C:\tmp\f20b`; see Deviation), master e9004d25f3 against branch a5b11bcb72:
- master: zip 41,216,895 bytes, 192 files;
- branch: zip 41,217,669 bytes, 192 files;
- **delta +774 bytes; total 39.31 MB**, under the 55 MB review line.

The work-branch base 38e82f527c has no `src/server` difference from origin/master, so the delta is this task's alone. `dotnet list package --vulnerable --include-transitive`: "no vulnerable packages given the current sources". No new HIGH CVE. The temporary worktrees were removed with `Remove-WorktreeSafe.ps1`.

## Conflict check

Run on 2026-11-03 against the 27 open PRs (`gh pr list --state open --json files`). No open PR touches any file in this change:
- `ChildRecordEndpoints.cs`;
- the adapter, its index and its test;
- `provisioningService.ts`;
- the ledger;
- `SecureChildOwnershipAiToolTests*`;
- `tests/integration/seam/ChildRecords/`.

No PR's file list was near the 100-file cap. PR #1501, named in the POML, is CLOSED. The nearest sibling, #2387 (field-mapping task 075, ribbon), has no file overlap.
