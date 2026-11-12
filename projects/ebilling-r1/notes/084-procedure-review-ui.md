# 084 Part A: Procedures and rules in force on a record (the two read routes)

Task 084 (EB-D-218 option A, EB-D-219). Branch `task/ebilling-084-r1`. The UI is 084a to 084d, which consume these routes. The outranked mark is 084e.

## Routes

Both are `GET`, one literal chain per core record (`matters`, `projects`, `workassignments`), group `/api/finance`:

| Route | Returns |
|---|---|
| `/api/finance/{core}/{recordId}/procedures?firmId=&date=` | `RecordProceduresView` |
| `/api/finance/{core}/{recordId}/rules-in-force?firmId=&date=` | `RecordRulesInForceView` |

- `firmId`: a GUID. Default: the record's `sprk_billingfirm`, else none (only Procedures with no Firm apply). `firmSource` in the answer says which: `Query`, `Record` or `None`.
- `date`: `yyyy-MM-dd`, default today (UTC, from `TimeProvider`). A bad `firmId` or `date` is 400, answered after the record check.
- Filters, in order: step 0 on the group (`RequireInternalFinanceCaller`), `finance.read` on the route record as the caller with `FinanceDenial.UniformNotFound`, the eBilling flag, then the service.
- Statuses: 200; 400 `procedures.invalid_query` (bad query); 404 (step 0, unknown or unreadable record: one byte-identical body); 503 `ebilling.disabled`; 503 `procedures.unavailable` (below). Nothing is written.

### `RecordProceduresView`

`record {table, id, name, distance}`, `date`, `firm {id, name}|null`, `firmSource`, `procedures[]`, `gaps[]`.

Procedure item: `id, name, area ("Billing"), type (ProcedureType name), appliesTo, firm, status (InForce | Excluded | Gap | NotYetInForce), level (highest source: AllRecords | Firm | ManualParent | ManualOwn), sources[], versionInForce {id, number, status, from, to}|null, pinned, newerVersion|null, overrides[]`.

- `sources[]`: every matching source, highest first: `{kind, winning, distance, via {chain record}|null, mode (FollowLatest | Pinned)|null, rowId|null}`. Exactly the top (level, distance) is `winning`.
- `overrides[]`: every Procedure-level row on the chain for this Procedure, Exclude included: `{rowId, mode (FollowLatest | Pinned | Excluded), decisive, distance, chainRecord, reason, pinnedVersion, newerVersion, stamp {byId, byName, on}}`. `decisive` marks the rows that decided the Procedure (the resolver's own `Overrides`; for NotYetInForce, the nearest rows).
- `gaps[]`: `{procedureId, procedureName, kind (ProcedureGapKind name), workDate, text, overrides[]}`; a Procedure in a gap also appears in `procedures` with status `Gap`.

### `RecordRulesInForceView`

`record, date, firm, firmSource, rules[], gaps[]`.

Rule item: `policyId, policyVersionId, code, name (the short headline), ruleType, parameterSummary (RuleBodyDescriber text, null when it refuses the body), versionNumber, inForceFrom, inForce, recordRule, ruleSetId, procedure {id, name, version, pinned}, level, distance, firmScope, marks[]`.

- Order: the rules that run (exactly `RulesOn(date).Rules`, the resolver's order), then rules a plain Exclude removed, then record rules whose base rule is no longer in force.
- A changed rule is ONE row: the record rule's id, the BASE rule's code (uac-r2 option (a); a `BRR-` code never leaves the BFF), with a `Changed` mark. The base is not repeated as an excluded row.
- `marks[]` is one open array. `kind` is one of `Excluded`, `Changed`, `Stale`, `BaseRetired`; a reader ignores a kind it does not know (084e adds `Outranked`). Members a kind does not use are null.
  - `Excluded`: `reason, firm, distance, chainRecord, stamp, overrideRowId`.
  - `Changed`: `recordRuleId, basedOn {id, number}, currentBase {id, number}, baseCode, baseParameterSummary`, plus the change's Exclude row facts (`reason, firm, distance, chainRecord, stamp, overrideRowId`).
  - `Stale`: `recordRuleId, basedOn, currentBase`. Fires when the record rule's based-on version is not the base rule's version in force on the date. Computed at read time.
  - `BaseRetired`: `recordRuleId, basedOn, baseCode`. The record rule's base rule is not in force on the date, so the resolver gives it no slot and it does not run (`inForce` false).

## Read model (EB-D-218, option A)

- Rows are read ONCE, app-only, through the existing `IGenericEntityService` via `ProcedureResolver.ReadRowsAsync`; `new ProcedureResolution(rows).RulesOn(date)` is the one resolution both lists share. `ProcedureResolver.cs` is unchanged; no change under `Services/Signals/**`. No second selection: marks and sources are computed from the resolver's output and the rows it read.
- Enrichment reads (names, type, creator) are by-id reads of rows the resolution already named: chain-record names, firm names (`sprk_organization`), `sprk_proceduretype`, `sprk_createdbyperson` on override rows, creator `fullname`; for a BaseRetired row also the base policy's code and the based-on version number.
- Then ONE batched caller-rights check (`CallerRecordAccessProbe`: the caller first, then `GetCallerRightsForRecordsOrUnknownAsync`, 250 per page) over EVERY row the answer exposes: the route record, chain records, Procedures, Procedure Versions, rule sets, policies, policy versions, override rows, firms and creators. The set is collected while the DTOs are built, so it is exactly what the answer names. A row without Read, an unanswered row, a faulted check, an unestablished caller, a faulted row read, a resolver fault (`ProcedureResolutionFaultException`) or any other exception is 503 ProblemDetails `procedures.unavailable` with no list. Never a partial list. Resolver exception text names row ids, so it is logged and never returned.
- Step 0 reads the stored external flag (never `#EXT#`, never the business unit); must-fire tests cover a flagged system user, no systemuser row, a flag-read fault and a lookup fault, on all six routes.
- These routes return BFF-managed field-secured columns app-only; for internal callers this matches a native read only while an access invariant holds, which is verified at deploy time (owner/uac-r2 hold the detail).
- Known limit: the routes are internal-only (step 0). Opening them to external callers requires the 191 external-visibility predicate (`AppOnlyExternalVisibility` on master) first.
- Record path: unknown and unreadable are the same 404; a record that vanishes between the check and the read is the same 404.

### No firm

`ProcedureResolver.ReadAsync` throws on `Guid.Empty`. With no query firm and no `sprk_billingfirm`, `RecordProcedureReadService.NoFirm` (`00000000-0000-4000-8000-00000000f1f0`, a fixed id that matches no `sprk_organization`) is passed, so only Procedures with no Firm apply, only rule rows with no Firm count, and no Firm-record set is read. The id never leaves the service; the view says `firm: null`, `firmSource: "None"`.

## Answers to the prototype's questions (084-prototype-questions.md)

- a. Every matching source is listed, the winner marked (`sources[]`, `level`). Per row: id, name, area, type, version in force (number, from, to), `pinned`, `newerVersion`.
- b. An Excluded Procedure is returned (`status: Excluded`) with its row in `overrides[]`: mode, reason, who, when, distance, chain record.
- c. A pin: `versionInForce` is the pinned version, `newerVersion` the newest in-force version above it; the override row repeats both.
- d. Both: `gaps[]` is the separate array (with the conflicting parents' rows), and the Procedure's row has `status: Gap`. A Procedure before its first version is `NotYetInForce`, not a gap.
- e. The firm is `firmId`, else the record's `sprk_billingfirm`. The answer returns the firm in use; it does not return a list of choices. The picker's data (firms with invoices on the record) is 084a's concern, from the invoice list, not from these routes.
- f. One `marks[]` array per rule, four kinds in 084 (`displaced` is 084e, renamed `Outranked`).
- g. `date` is the invoice work date the caller supplies; the default is today.

### Corrections to `spaarke-prototype/projects/ebilling.r1/src/procedures/types.ts` (for 084a)

- No `displaced` mark in 084 (EB-D-219). Keep the array open to `Outranked`.
- No `versions[]` list and no `ruleCount` per Procedure row: a count or a version list would expose more rows to the all-or-nothing rights check than the panel shows. Use the rules route for rules.
- Gaps are an array on the view as well as a row status.
- Status is `InForce | Excluded | Gap | NotYetInForce`.
- A changed rule is one rule row with `recordRule: true` and a `Changed` mark that carries the base rule; a record rule's code is the base rule's code.
- An excluded rule is `inForce: false` with an `Excluded` mark per deciding row.
- `stamp.byName` can be null (the creator's name is read separately); show the id-less fallback the UI already has.

## ontology-r1 R1 to R6 (for 084e)

The conditions for the outranked mark are in the constraints of `projects/ebilling-r1/tasks/084e-outranked-mark.poml` (R1 to R6). This task does not implement or approximate them: 084 has no rank loop and no `displaced` flag, and the `marks[]` array is open to the fifth kind.

## Judgement calls and deviations

1. **"Gate variant" of the probe.** There is no batched "gate" method; `GetCallerRightsForAuthorizationGateAsync` is single-record. The batched check is `GetCallerRightsForRecordsOrUnknownAsync` (as `AffectedInvoiceCounter` and `StoredFileEndpoints` use it), which keeps "unknown" apart from "no".
2. **BaseRetired.** The model text (6.3) says a record rule keeps running when its base is retired; the code gives it no slot (089c K1/K3, ontology-r1-confirmed). Code wins: `BaseRetired` rows are `inForce: false`. They are found from the rows already read (a rule-level Exclude whose base is not resolved on the date and whose derived record rule exists), so no resolver change was needed.
3. **NotYetInForce Procedures.** `RulesOn` neither applies nor gaps a Procedure before its first version. The Procedures route finds them by asking the same resolution for a date after every first version opened; a fault there is 503.
4. **Extra reads.** The Procedure `Type` and the override creator are not in the resolver's rows, so the service reads them by id (one more `sprk_procedure` and `sprk_procedureassignment` query). The rule-body describer is the existing singleton and reads through the app service.
5. **Cost.** The rights check asks one `RetrievePrincipalAccess` per distinct row (4 concurrent). A record with many rules asks many questions. Measure at deploy; if it is slow, cap or cache is a follow-up, not a reason to skip rows.
6. **Risk to verify at deploy time.** `RetrievePrincipalAccess` must answer for `systemusers` and `sprk_organizations` rows for an internal caller. If it does not, every answer that names a creator or a firm is 503 (fail closed). The first deploy check is a record with an override and a firm-scoped rule.
7. **Tests live under `tests/integration/contract/Api/Finance/`** (KEEP category), not the unit paths the POML lists, with one shared rig (`RecordProcedureRig.cs`): the contract tests need the HTTP host and the in-memory Dataverse.
8. **Test count beyond the criteria.** Added, each with its reason: a 401 test per route (the group's `RequireAuthorization`); a bad-query 400 test (the query is parsed by hand); a vanished-record 404 (the race the service returns `RecordNotFound` for); type name, default date, several-sources and NotYetInForce (DTO members 084a renders); a test that every exposed row is in the one batched check (the all-or-nothing contract).

## Tests, mapped to the acceptance criteria

| Criterion | Tests (`tests/integration/contract/Api/Finance/`) |
|---|---|
| Step 0 refusals, 404 byte-identical | `RecordProcedureEndpointsContractTests`: `EveryRoute_StepZero_RefusesBeforeAnyCheckOrRead` (6 routes x 4 kinds), `EveryRoute_StepZero_AdmitsAnInternalCaller_ToTheRecordCheck`, `EveryRoute_UnreadableRecord_IsTheSame404AsAnUnknownOne` |
| Procedures: billing firm, firmId, firm condition, Exclude, two d1 pins | `RecordProcedureReadModelContractTests`: `BillingFirm_ListsItsOwnAllRecordsAndManualProcedures`, `FirmIdG_ListsGsProceduresInsteadOfFs`, `ManualProcedureNamingF_IsAbsentForG`, `ExcludeOnTheRecord_IsReturnedAsExcluded`, `TwoParentsWithDifferentPins_AreInTheGapsArray` |
| Rules equal the resolver's output, four marks | `InForceRules_EqualTheResolversOutput_FromOneResolution`, `ExcludedMark_*`, `ChangedMark_*`, `StaleMark_*`, `BaseRetiredMark_*`, `Marks_AreFourKindsOnly` |
| 503, never a partial list | `OneUnavailableThing_Is503_WithNoList` (31 cases: 14 per route x 2 routes, plus 3 rules-only; unreadable parent, own record, Procedure, version, override row, creator, firm, rule, rule set, policy version; faulted check; unanswered row; unestablished caller; faulted Procedure / override / policy read; resolver fault), with `SameSeed_NothingRefused_Is200` as the must-not-fire control |
| Nothing written | `Lists_WriteNothing` (strict mock without Create; Create, Upsert, Update, Delete verified never) |
| Ledger | `RouteAuthorizationGuardTests` (ArchTests): `GovernedFile` entry and count 152 -> 153 |

Mutation check: with the rights check neutered (unreadable and unanswered rows allowed) 19 of the 503 cases fail, and with `Stale` neutered the stale test fails. (Fix round 1 below adds 20 tests and the seeding proofs.)

## Build, tests, publish size, CVE

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 0 warnings.
- Affected unit suites (Finance, Signals, Procedure, RecordProcedure filters): 11,900 passed, 0 failed, 10 skipped (pre-existing skips). New tests: 119 (RecordProcedure filter); 139 after fix round 1.
- `tests/Spaarke.ArchTests/` (full run): 2,505 passed, 0 failed. The first run failed two route-guard tests (the scanner needs a file-local `const` for the group path); fixed before the commit.
- Publish size (BFF hygiene item 4, NFR-01): command `dotnet publish -c Release -r linux-x64 --self-contained false src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`; zip `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`; PDBs included; both from fresh worktrees at `C:\wt084m` (master `a654da59c4`) and `C:\wt084b` (branch head `3002ba81db`, master + 12 commits); 191 files each.
  - master 41,980,644 bytes (40.04 MiB); branch 42,018,803 bytes (40.07 MiB); delta +38,159 bytes (+0.04 MiB). No package added. Both temp worktrees removed with `Remove-WorktreeSafe.ps1`.
- `dotnet list src/server/api/Sprk.Bff.Api package --vulnerable --include-transitive`: no vulnerable packages.

## Fix round 1 (branch `task/ebilling-084-r2`, from `e4c07d331b`)

Four review findings, fixed with tests; nothing else changed.

1. **F1, fail-open on the rules route.** `pinned`, `level` and `distance` of a rule come from `AppliedProcedure.Overrides` (the rows that decided the Procedure), and neither those rows nor their chain records were in the rights check. A caller who could read project P but not matter M's pin row answered 200 with `pinned: true, level: ManualParent, distance: 1`. `RuleViewAsync` now exposes every row of `applied.Overrides` and its chain record (`ExposeOverrides`). Same audit of every other derived field on both routes: the Procedures route already exposed all of them (every override row of a Procedure with its chain record, the gap rows, the Exclude rows, the sources); on the rules route two more were missing and are now exposed in `BuildRulesAsync`: the rows of each `ExcludedProcedures` entry (the Procedure's rules are absent from the list because of them) and the rows of each rule a record rule replaced (the base rule is absent because of them). Gap rows, excluded rules' rows and a record rule's own Exclude row were already exposed.
2. **F2, untested exposures.** New: `EveryRowOfAChangedAndABaseRetiredRule_IsInTheOneBatchedCheck` (exact asked-set on the rules route, one Changed + Stale rule on matter M and one BaseRetired record rule on project P, `RecordProcedureRig.SeedChangedAndBaseRetired`), and `ChangedAndBaseRetiredRows_OneUnreadable_Is503` (11 cases: base rule, based-on version, base version in force, record rule, record rule set, Exclude row, for the changed rule and for the base-retired one), with a nothing-refused control.
3. **F2, inconsistent id.** A BaseRetired row now carries the record rule's id as `PolicyId` (as the in-force changed row and the DTO doc say); the base rule's id stays in the marks' code only. Asserted in `BaseRetiredMark_FiresWhereTheBaseIsGone`.
4. **F4, missing stable code.** The 400 for a bad `firmId` or `date` is `procedures.invalid_query` (`RecordProcedureEndpoints.InvalidQueryErrorCode`), asserted in `BadQuery_Is400_AndReadsNothing`.

New tests, 20 (119 -> 139): 1 asked-set, 11 F2 503 cases, 1 F2 control, 1 F1 control, 4 F1 parent pin (pin row, parent record; both routes), 2 F1 excluded Procedure (Exclude row, chain record; rules route).

**Before the fix** (service and endpoint file at `e4c07d331b`, new tests in place): 11 failed, 128 passed. They are the asked-set test, the BaseRetired id assertion, the F2 control (it asserts the id), the 4 bad-query tests, 2 excluded-Procedure cases and the 2 rules-route parent-pin cases (the 2 Procedures-route cases passed, as that route already exposed them).

**Seeding proof** (the Expose lines of the changed and base-retired rows commented out: base policy of a changed rule, its Exclude row, the base-retired rule's base policy, record rule, record rule set and Exclude row, the based-on version of a base-retired rule, and the version ref helper): 8 failed, 131 passed. Failing: the asked-set test and F2 cases `changed: base rule`, `changed: based-on version`, `retired: base rule`, `retired: record rule`, `retired: record rule set`, `retired: based-on version`, `retired: exclude row`. Still passing, because the row has a second exposer (so removing one line is harmless): `changed: record rule`, `changed: record rule set` (exposed by `RuleViewAsync`), `changed: exclude row` (`ExposeOverrides` of the replaced rule), `changed: base version in force` (`SummaryAsync`). Lines restored.

## Fix round 2 (branch `task/ebilling-084-r3`, from `556551dddd`)

Four items, each with tests; nothing else changed. Tests: 139 -> 195 (RecordProcedure filter; 56 new).

1. **F2, kill-switch order.** The contract host takes `ebillingEnabled` (as the sibling Finance hosts do). For all six routes: flag off + a readable record is 503 `ebilling.disabled` (and the record's Read was asked as the caller first, no row read); flag off + an unreadable record is the uniform 404, byte-identical to the flag-on unknown-record 404; flag off + a flagged-external, row-less or flag-fault caller is refused at step 0 (`FlagOff_ReadableRecord_Is503Disabled`, `FlagOff_UnreadableRecord_IsTheUniform404_NotTheDisabled503`, `FlagOff_StepZero_RefusesFirst`).
2. **F2, generic exposure guard.** `Every200Guid_WasAskedOfTheCaller`: for five seeds (the 503 refusals seed, `SeedChangedAndBaseRetired`, the conflicting-parents seed, a parent pin, an excluded Procedure) on both routes, every GUID-shaped string in the 200 body must be in `rig.Probe.Asked`. No exemptions: the record id and the firm id are in the body and are asserted like every other id (the record id is also asserted present, so the guard cannot pass on an empty walk). A GUID walk cannot see rows that decided something the body shows only through a derived value or an absence (a pin behind `pinned`/`level`/`distance`, an Exclude behind a missing Procedure), so each seed also lists those rows (`AlsoAsked`) and the guard asserts they were asked. The conflicting-parents seed also has `ConflictingParents_OnePinDenied_Is503` (either pin denied is 503, both routes; the same seed undenied is 200). The guard flagged no GUID: no exposure bug was found.
3. **K4, fault logging.** In `RecordProcedureReadService.LogUnavailable` (the one place the three catch blocks log): `ProcedureResolutionFaultException`, a Dataverse `FaultException` and `HttpRequestException` stay `LogWarning`; anything else is `LogError`. The 503 is unchanged. `Fault_IsLoggedAtItsLevel_AndAnswers503` (unexpected = Error; HTTP and resolver = Warning, no Error; all 503).
4. **K4, 400 correlationId.** `InvalidQuery` sets `correlationId` = `HttpContext.TraceIdentifier`, as the 503 does; asserted in `BadQuery_Is400_AndReadsNothing`.

**Seeding proofs** (each change reverted after):
- Kill-switch order: `RequireEbillingEnabled` moved before the finance-authorization filter on `GET /matters/{id}/procedures` only: 6 failed (the unreadable-record 404 test, the readable-record 503 test, and four step-0 cases for that route). Restored.
- `ExposeOverrides(r, applied.Overrides)` in `RuleViewAsync` commented out: 4 failed (guard: parent pin / rules-in-force and refusals seed / rules-in-force through `AlsoAsked`, plus the two existing F1 parent-pin rules-route cases). The GUID walk alone does NOT catch this call (the deciding rows' ids are not in the body); `AlsoAsked` is what does. Restored.
- `r.Expose(OrganizationSet, firm)` in `FirmOf` commented out: 4 failed (guard refusals seed on both routes by GUID, plus the existing "organization" 503 cases). Restored.
- Fault logging: the Error branch changed to `LogWarning`: the 2 "unexpected" cases failed. `correlationId` removed from `InvalidQuery`: the 4 bad-query cases failed. Restored.

**Known limits recorded for 084a and 112 (no code change):**
- (a) K2. A future-dated overlap in a Procedure's versions (two versions in force on some later date) makes the Procedures list 503 for every date, because the list probes the latest opening date to find not-yet-in-force Procedures and the resolver faults on the overlap. It fails closed. Fix belongs to the Procedure version publish check, not this read.
- (b) K2. A `firmId` the caller cannot Read, or that names no row, returns 503 `procedures.unavailable`, not an empty list. 084a's firm picker must offer only firms the caller can read.
- (c) K1. Rights-check cost: one OBO token exchange plus one WhoAmI per request, the batched Read check in pages of 250 (4 at a time), and a sequential `SummaryAsync` per rule. Measure in dev at task 112; it becomes F3 above about 2 s for a typical record.

## Fix round 3 (uac-r2 review)

Branch `task/ebilling-084-r4` (from the `work/ebilling-r1` head `9e398e23e3`, which contains 084 merged at `e73e185b9c`). Tests: 195 -> 235 (`RecordProcedure` filter; 40 new). Files: `RecordProcedureReadService.cs`, the two contract test files and `RecordProcedureRig.cs`.

### Items

1. **F-A (F1), `basedOn` read from an unexposed row.** `BaseRetiredRulesAsync` shows `basedOn` in both the Changed and the BaseRetired marks, read from the record rule's latest `sprk_policyversion` row, which was never exposed. It now does `r.Expose(PolicyVersionSet, version.PolicyVersionId)`. Tests: that row is in the exact asked-set test (`EveryRowOfAChangedAndABaseRetiredRule...`), in the 503 cases (`retired: record rule version`, plus `changed: record rule version` as a control, which `RuleViewAsync` already exposed) and in the guard seed's `AlsoAskedOnRules`. The in-force Changed path was checked for the same pattern: every value it reads from a version row (`rule.Version`, the based-on ref, the current-base ref, the base version's summary) goes through `RuleViewAsync`, `PolicyVersionRef` or `SummaryAsync`, which all expose. No second instance.
2. **F-B (F1), the Procedure's whole version set.** `ExposeVersionsOf(r, procedureId)` exposes every version of a Procedure. It is called from `BuildProcedure`, `BuildGap` and `RuleViewAsync` (the rules route's applied Procedure, whose chosen version is derived from the whole set). 10 new 503 cases, each first proving the 200 and then denying a version that is not the chosen one: `NotYetInForce` (both versions), `HoleBetweenVersions` (both versions, both routes), `AfterLastVersion` (both routes) and an absent `NewerVersion` (a Draft version 2 behind a pin; both routes).
3. **F-D (decided by uac-r2), a lookup-origin firm is not probed.** `FirmOf(r, id, origin)` takes an explicit `FirmOrigin` (`Lookup` or `Query`). The route's own firm goes through `ResolvedFirmOf`, which says `Query` only when `FirmSource` is `Query` (set where `?firmId=` is read). Every other caller (a Procedure's, an override's, a rule set's firm, a changed rule's Exclude) passes `Lookup`. The name still comes from the existing app-only organization read; only the rights question is dropped. Tests: billing firm F unreadable as an organization, with a firm-scoped Procedure, an Exclude and a changed rule's set all naming F: 200 on both routes, `firm.name` is "Firm F", and the organization is not in the asked set. `?firmId=` naming an unreadable organization, or one with no answer (a row that is not there): 503 on both routes, the organization in the asked set (with a control that the readable one is 200 and asked).
   - **Existing tests updated for F-D:** (i) `EveryExposedRow_IsInTheOneBatchedCheck` no longer expects `("sprk_organizations", F)` (F is the project's billing firm); (ii) the "organization" case of `OneUnavailableThing_Is503_WithNoList` now passes `&firmId=F` (same firm and seed, query origin), because without it the organization is rightly not asked; (iii) `Every200Guid_WasAskedOfTheCaller` has ONE exemption, the refusals seed's firm F, which is in the body and must not be asked (`GuardSeed.LookupFirms`, checked from both sides: in the body, not asked). Its display name no longer says "no exemptions".
4. **Mechanical guard.** Two theories over the five guard seeds on both routes (20 cases):
   - `EveryAskedRow_DeniedAlone_Is503_WithNoList`: with nothing denied, 200 and capture the asked set; then for EVERY row in it, deny that one row alone and assert 503 `procedures.unavailable` with no list. This proves each asked row is load-bearing for the refusal.
   - `EveryVersionRow_IsAsked_OrExemptBecauseTheBodyDoesNotDependOnIt`: for every Procedure Version and policy version row the rig seeded (`RecordProcedureRig.VersionRows`, labelled by what the row is): if it is asked, denying it is 503. If it is not asked, denying it can change nothing (it is never consulted), so that check is vacuous; the real test is a REMOVAL on a fresh rig seeded the same way (the rig's ids are now a counter, so two rigs hold the same ids and bodies compare byte for byte). The 200 body must be identical, otherwise the row is a value the answer depends on and nobody asked about: an F1. Every unasked row is listed with a reason in `UnaskedVersionRows` (a stale or missing entry fails).
5. **K2 (BaseRetired across episodes): a real wrong answer, fixed.** `BaseRetiredRulesAsync` took the record rule's latest version across ALL its versions, while `ProcedureResolution.RecordRuleVersionOn` counts only the current episode (versions created on or after the change's Exclude row) and then the latest in force on the date. Test: a rule changed (version A in force from Feb 15, based on base v1), removed, then changed again (a newer Exclude row; version B in force from Jan 15, based on base v2). The resolver at Feb 20 picks B (asserted in the test). The old code showed A's base (v1) on Mar 15; it now shows v2. The fix is `CurrentEpisodeVersion`, which copies the resolver's rule: episode filter (an Exclude row with no `createdon` counts every version; a version with none does not count beside a known boundary), then the latest in force on the date (effective-from, number, id). When the change is not yet in force on the date it uses the episode's first version. A known boundary that no version reaches is the resolver's own fault state and gives 503 (`BaseRetired_WithNoVersionInTheCurrentEpisode_IsUnavailable`). The old code lacked both the episode rule and the date rule; the date half is the same defect class (a version in force only after the date would be named).

### Seeding results

| Seeded | Result |
|---|---|
| Service reverted to `9e398e23e3`, all new tests in place | 25 failed, 210 passed. Failing: both K2 tests; the F-A exact asked-set test and `retired: record rule version`; all 10 F-B cases; both F-D lookup-origin 200 cases; `EveryExposedRow_IsInTheOneBatchedCheck` (its organization expectation is gone); the GUID guard on 3 cases; the asked-row guard on one case (it asked only 3 rows, below the guard's minimum of 4); the version-row guard on 4 cases. The F-D query-origin 503 cases pass on the old code, as they must (it always asked). |
| F-A only: the new `Expose(PolicyVersionSet, version.PolicyVersionId)` removed | 4 failed, 231 passed: the exact asked-set test, `retired: record rule version`, the GUID guard's `AlsoAskedOnRules` (`changed and base retired` / rules-in-force) and the version-row removal guard (same case: removing the retired record rule's version changes the 200 body and the row is unasked). |

**Honest answer on the brief's question.** The asked-set guard (`EveryAskedRow_DeniedAlone_Is503_WithNoList`) does NOT catch the F-A bug: it passed with the Expose removed, because an unexposed row is never asked and so is never in the set it walks. What catches F-A mechanically is the other guard, by removal (positive dependence: the body changes when the unasked row goes). Denying an unasked row, as the brief words it, is vacuous for the same reason: it is never consulted, so the 200 is identical by construction. The test asserts that and then does the removal.

### The guard's reach

- It finds a value in the body that depends on an unasked **version** row (Procedure Version, policy version), on the five seeds and both routes. It found no F1 beyond F-A and F-B, which are fixed.
- It does NOT find an **absence** derived from a row (negative dependence). Removing a Draft version that decides "no newer version" changes nothing, so the removal check cannot see it. That class is covered by the four F-B scenarios, written by hand; a future absence-derived value needs its own scenario.
- It sees only the rows the five seeds contain, and only version rows. Other tables are covered by the GUID guard, `AlsoAsked` and the per-row 503 cases.
- Version rows asked, of the version rows seeded, per seed and route: changed and base retired 2 of 7 (procedures) and 7 of 7 (rules); conflicting parents 2 of 4 and 2 of 4; excluded procedure 2 of 4 and 2 of 4; parent pin 2 of 3 (procedures) and 3 of 3 (rules); refusals seed 1 of 3 (procedures) and 3 of 3 (rules).

### Exemption list (version rows not asked, removal leaves the 200 byte-identical): 15 rows

- **The Procedures list shows no rule (11 rows).** Every policy version on the `procedures` route: `changed and base retired` (BRR-DROPPED v1, BRR-GENERAL v1, DROPPED v1, GENERAL v2), `conflicting parents` (PIN-1 v1, v2), `excluded procedure` (GENERAL v1, REMOVED v1), `parent pin` (PIN-1 v1), `refusals seed` (RULE-A v1, RULE-B v1). Reason: no policy version can appear in that body.
- **Gap Procedure (2 rows).** `conflicting parents` / rules-in-force, PIN-1 v1 and v2. The Procedure is a Gap (conflicting pins) and none of its rules is listed; the pin rows (asked) decide.
- **Removed Procedure (2 rows).** `excluded procedure` / rules-in-force, the REMOVED Procedure's version 1 and its rule's version 1. A Procedure-level Exclude removes the Procedure and its rules from the list; the Exclude row (asked) decides.
- **Integrity class (`IntegrityFaultRows`, 1 row).** `changed and base retired` / procedures, GENERAL v1 (the version the record rule is based on). Removing it makes the resolver fault (503) on a route that does not show it. That reveals that the data is inconsistent, not what the row says. The rules route asks about it.

### Known limits

- **K1.** For a changed rule, the base rule's Procedure-level rule set is not exposed. It decides nothing shown beyond rows that are exposed (the base rule, its versions, the Procedure and its versions, and the record rule set are). Left as is.
- **K2.** BaseRetired across episodes: confirmed a real wrong answer and fixed (item 5).
- **K3.** Rights-check cost: when a row vanishes between the read and the check, the probe's retry costs about 1.6 s on that request. It fails closed (503) either way. Measure at task 112.
- **K4.** The rules route does not expose a Procedure that is not applied or listed (for example one not yet in force on the date). Nothing in its body is derived from that Procedure's values: the answer reads the same if it did not exist. Not changed.
- **K5 (guard).** The removal guard cannot see negative dependence (see "The guard's reach").
## 084f

Task 084f (EB-D-220, owner option a; uac-r2 finding F-C on 084): billing admins can Read every row 084's two record routes expose. Source only: no live change, no role assignment, no script run against an environment.

**What changed** (copying 089e, `c564ab0709` and `38e8ea9ab2`):
- Role source `src/dataverse/solutions/SpaarkeMaster/Roles/Spaarke Billing Administrator.xml`: `prvReadsprk_procedureversion` and `prvReadsprk_procedureassignment`, both `Deep`. Nothing else added; no other role file touched.
- `config/billing-rule-readers-role.json` (the Billing Rule Reader role, child-unit billing admins): the same two entries (Read, Deep, with reason, evidence and `addedBy`). `scripts/Set-CustomerOwnerTeams.ps1` reads its tables from this file, so only its header text changed.
- `scripts/ebilling/Deploy-EbillingBillingAdminRole.ps1` (not in the brief's file list; it is the script that applies and verifies the Billing Administrator role on an environment): the two privileges join the wanted set and leave the forbidden set, as 089e did for `sprk_procedure`. Without this the script would stop with exit 2 on a role that holds them, and `-Verify` would not ask for them. Any other privilege on the three Procedure tables still stops the run.
- `ProcedureRolePrivilegeGuardTests.AllowedBeforeTask191`: two entries, each with the comment `uac-r2 agreed 2026-11-12 (EB-D-220)`. `UntilTask191` stays `true`. New test `GuardAllowsOnlyTheBillingAdministratorsVersionAndAssignmentReadAtDeep`: must-not-fire for exactly the allowed entries; must-fire for every other verb (Create, Write, Delete, Append, AppendTo, Assign, Share) at Deep and Basic, Read at Basic, Local and Global, other roles (Core User, Basic User, a look-alike name, `Billing Rule Reader`) and a neighbouring table (`sprk_storedfile`). The 089e test lost its old must-fire line for Read on the version at Deep (now allowed).
- `RuleSetRolePrivilegeGuardTests`: the Billing Administrator's exact set includes the two Reads (missing, Global and an extra Write on either are reported).
- `RestrictedContentOwnerRoleGuardTests`: the reader role's table set is the five tables; the old "extra table" controls for the version and assignment became "extra privilege" controls (Write, Create), plus controls for either entry missing, at another depth, or naming another table's Read, and an extra `sprk_storedfile`. The census test (`...PassesTheSecureCensus_...NeverReachesSecureRecord`) now covers the five privileges; `prvReadsprk_procedureassignment` is graded by the census, so the extended role is proven from every customer top unit's copy.
- `tests/scripts/Set-CustomerOwnerTeams.Tests.ps1`: the fixture reads the five tables from the file; the same 68 tests, extended to five privileges (Verify output, dry-run plan, Apply with the role missing, missing and wrong-depth cases for the two new tables, an extra Write on either is outside the file).
- `tasks/TASK-INDEX.md`: 084f marked done.

**Privilege names** (as Dataverse spells them, from the lower-case schema names in the solution source): `prvReadsprk_procedureversion`, `prvReadsprk_procedureassignment`. `prvReadsprk_procedureassignment` is metadata-confirmed on dev (`config/restricted-content-owner-role.json`, Id `287f9674-9656-4006-9eda-ad999cbc69a0`, 2026-10-28). `prvReadsprk_procedureversion` has no recorded metadata read; this task made no live call, so 112's dry run must resolve it from metadata (the deploy script throws "Privilege ... not found" and `Set-CustomerOwnerTeams.ps1 -Verify` reports PRIVILEGE MISSING if it does not exist). Escalation did not trigger: both tables are in SpaarkeMaster and the guards already name both privileges.

**Tests.** ArchTests: 2513 passed, 0 failed (2505 before). Pester (`Set-CustomerOwnerTeams.Tests.ps1`, Pester 5.7.1; 6.x does not load the containers): 68 passed. Before the change (old reader-role file, new tests): 63 passed, 5 failed.

**Seeding proof.** The `prvReadsprk_procedureassignment` entry removed from `AllowedBeforeTask191`: 2 of 6 tests in `ProcedureRolePrivilegeGuardTests` failed (the real-role-file scan, naming `Spaarke Billing Administrator.xml: prvReadsprk_procedureassignment (Deep) - not until uac-r2 task 191`, and the new must-not-fire test). Entry restored; all green.

**Apply steps for task 112** (dev; needs the owner's OK typed in session, and uac-r2 notices before and after):
1. Billing Administrator role: delivered by the SpaarkeMaster solution import carrying the role source (deploy task 079), or by `scripts/ebilling/Deploy-EbillingBillingAdminRole.ps1`. Run it with `-WhatIf` first (it prints the plan; the two Reads show as to add at Deep), then the plain run, then `-Verify` (checks the root role and every business-unit copy). The role source and the script now agree, so either route ends at the same state. Note `scripts/provisioning/role-privilege-expectations/spaarkemaster-1.3.1.0.json` is the expectation file for that solution version; the next version's file (made at 079) should list the two Reads for this role.
2. Billing Rule Reader role: delivered by `scripts/Set-CustomerOwnerTeams.ps1 -EnvironmentUrl <url> -CustomerBusinessUnitId <id>` (dry run, which plans "add Read at Deep" for the two missing privileges), then `-Apply` (owner go), then `-Verify`, once for each customer top business unit. The role's existing holders are not re-assigned; the change is to the role's privileges only.
3. Check after the apply: a billing admin homed in the top unit and one homed in a child unit each get 200 from both of 084's routes on a record with an in-force Procedure; any other internal user still gets 503 `procedures.unavailable` (by design until uac-r2 task 191).
4. Not part of this task: `config/feature-role-privileges.json` (the feature-role matrix and `Set-FeatureRolePrivileges.ps1`) lists the Billing Administrator's `prvReadsprk_procedure` (gap G5) but 089e did not add it, and nothing here does; if uac-r2 wants the two new Reads in that matrix, that is a separate change.
## 084a: the read-only "Procedures on the record" code page

Branch `task/ebilling-084a`, from `work/ebilling-r1` at `cfe9a7b460`. Folder `src/solutions/RecordProcedures/`. Rigor FULL. The contract is 084's two routes above; every list on the page is the server's answer. No BFF file changed. No `src/dataverse/solutions/SpaarkeMaster/**` file changed (form placement is 112's).

### What was built

| File | Purpose |
|---|---|
| `src/main.tsx` | Entry. Copy of InvoiceFiles' shape: `@spaarke/auth` bootstrap, host `FluentProvider` theme (light and dark), `AppErrorBoundary`. Launch = `id` + `typename` from the form. |
| `src/services/launch.ts` | `typename` decides the route (`sprk_matter` -> `matters`, `sprk_project` -> `projects`, `sprk_workassignment` -> `workassignments`). Any other table or a non-GUID id is a launch message, no BFF call. |
| `src/services/recordProceduresApi.ts` | 084's DTO types and the two GET calls. A body that is not the view is a failure, never an empty list. Failure kinds and the sentences. |
| `src/services/model.ts` | Pure: badges, labels, dimming, filters, count-card configs. `RULE_MARKS` and `DIMMED_MARK_KINDS` are the two tables 084e extends. |
| `src/services/firmChoices.ts` | The picker's data (below). |
| `src/components/ProceduresPanel.tsx`, `ProcedureList.tsx`, `RuleList.tsx` | The card, and the two tables. |
| `src/__tests__/` | 5 suites, 82 tests. Fixtures mirror the prototype's Matter 2026-LIT-4471, Project PRJ-20418 and WA-30552 as 084 DTOs. |

Shared pieces: count cards are `MetricCardRow` (layout `wide`) fed `MetricCardConfig[]` (D-24, as `Worklist/laneCountFilters` and InvoiceReview do; ConsoleKit has no count-card component since 109's rework), and `StatusBadge`. Both come from `@spaarke/ui-components`; jest maps that to a test seam that re-exports the real sources. No colour literal; Fluent tokens only. No new package: `package.json` is InvoiceFiles' dependency list (the shared barrel needs it to bundle).

### Decisions

1. **Firm picker source (decision 1).** The picker is: the record's billing firm first, then the other firms that have invoiced the record, each once.
   - The billing firm is learned from the first read, which sends no `firmId` (the server then uses `sprk_billingfirm`): `firmSource: "Record"` returns it; `"None"` means no billing firm and the picker says "No billing firm".
   - Other firms: `Xrm.WebApi` reads `sprk_invoice` as the caller (`_sprk_vendororg_value` of the invoices whose `_sprk_matter_value`, `_sprk_project_value` or `_sprk_regardingworkassignment_value` is the record), then reads those ids from `sprk_organization` as the caller and offers only the rows that come back.
   - Why no BFF route: `GET /api/finance/core-records/{type}/{id}/firms` lists the ACTIVE firms LINKED to the record (external participation), not the firms that invoiced it, so it is a different set; `/invoices/search` is a semantic search that needs a query. Neither lists a core record's invoices with their firm.
   - Why `Xrm.WebApi` is allowed: DATA-ACCESS-DECISION-CRITERIA, a simple read-only lookup in host context; the same pattern as `RuleReview/firmLookup.ts` and `ApExport/invoices.ts`. The POML forbids it only for the Procedure, override and rule tables; a test pins that `firmChoices.ts` reads only `sprk_invoice` and `sprk_organization`. `sprk_vendororg` is field-secured "readable by everyone" (EB-D-200), so the invoice read returns it.
   - K2 (a `firmId` the caller cannot Read makes the routes 503): the organization read as the caller is the filter, so an unreadable firm is never offered. A firm that is not returned is dropped silently; a failed read leaves the list short and the page says "Some firms that have invoiced this record could not be listed."
   - Limit K1: one platform page (5,000 invoices, newest first) is read. A record with more invoices than that could miss an older firm; the billing firm is always offered.
   - If the default read fails (for example the billing firm is unreadable and the route answers 503), the picker still shows, with "Record's billing firm" and the invoiced firms, so the user can pick another firm.
2. **Marks.** Rendered generically. `Excluded` is dimmed; `Excluded`, `Changed` ("Changed here"), `Stale` ("Base rule updated") and `BaseRetired` ("Base rule retired") each get a badge. An unknown kind is a neutral badge with the kind text and is not dimmed. 084e adds one entry to `RULE_MARKS` (label, tone) and one to `DIMMED_MARK_KINDS`; a test adds `Outranked` to the set and sees the row dim. "displaced" appears nowhere (a test scans every rendered fixture).
3. **States.** 404 from either route: "Procedures are not available for this record" (one message, same text whichever route sent it, no hint whether the record exists). 503 `procedures.unavailable`: "Procedures cannot be shown right now". 503 `ebilling.disabled`: "eBilling is not enabled". Both routes are read for every firm and date; if either fails the page shows no row on either tab (a mutation that shows rows when only the Procedures route succeeds fails 3 tests). When both fail, the most definite failure is reported (sign-in, then 404, then eBilling off, then 503). The 404, eBilling-off and sign-in states show no pickers; a 503, 400 or network failure keeps the pickers and a Retry.
4. **Read-only.** No row has a button, link or tab stop; a click or Enter on a row opens nothing and makes no call. Every request is a `GET` of `.../procedures` or `.../rules-in-force`; no 089 route exists in the sources. Tests: rows (no button, no link, no tabindex, click and Enter change nothing, call count unchanged), the full request log of a session using every control (all `GET`, only the two paths), and a source scan with must-fire controls for 089 paths, write verbs and Dataverse write calls.
5. **Today.** The date picker defaults to the user's own calendar day (`yyyy-MM-dd`), sent explicitly. The server default is UTC today; they differ only near midnight UTC. An empty or half-typed date reads as today.

### Prototype elements left out

| Element | Reason |
|---|---|
| "Rules" column (rule count per Procedure) | 084 returns no per-Procedure rule count (corrections list above). |
| "Displaced" card and mark | EB-D-219: the mark is "Outranked", task 084e. |
| Row click to the wizard, previous/next through the list, "Assign Procedure" button, the saved-banner | 084b to 084d; the page is read-only. |
| The "Billing setup" card | Not part of this panel (the Billing tab already carries the firm lookups). |
| `versions[]` and version labels like "2025 rates" | 084 returns the version number and window only; the page shows `v2`, "pinned to v1, v2 available". |

No field the prototype's panel shows is missing from 084's DTOs apart from those documented corrections, so no escalation.

### For task 112 (deploy and form placement)

- **Web resource name:** `sprk_recordprocedures` (InvoiceFiles' convention: `sprk_` + the folder name lower-cased). `npm run build` writes `dist/sprk_recordprocedures.html` (a single file, 1,181,939 bytes). Deploy it BEFORE importing the forms: `scripts/Deploy-WebResourceInline.ps1 -WebResourceName sprk_recordprocedures -FilePath <path>/dist/sprk_recordprocedures.html -DisplayName "Procedures on the record"`. As with `sprk_invoicefiles` (151e, F3b), the form references a web resource the solution does not carry until it is exported once after the first deploy.
- **Control:** web resource control with "Pass record object-type code and unique identifier as parameters" (`id` and `typename`), as the invoice form's Files section does. A new section with a full-width cell, height about 600 px, scrolling on.
- **Forms** (SpaarkeMaster source, read-only here):

| Entity | Main form | Billing tab |
|---|---|---|
| `sprk_matter` | `Entities/sprk_Matter/FormXml/main/{4fa382f2-c273-f011-b4cb-6045bdd6a665}.xml` ("Matter main form") | Has `tab_billing` with sections `tab_billing_section_financials`, `_invoices`, `_budgets`. Add a section `tab_billing_section_procedures` after the budgets. |
| `sprk_project` | `Entities/sprk_Project/FormXml/main/{5aa00242-5212-f111-8342-7ced8d1dc988}.xml` ("Project main form") | Has `tab_billing` with sections `tab_billing_section_financials`, `_invoices`, `tab_6_section_3`. Add a section `tab_billing_section_procedures` at the end. |
| `sprk_workassignment` | `Entities/sprk_WorkAssignment/FormXml/main/{7e578eef-761d-f111-88b3-7c1e520aa4df}.xml` ("Work Assignment main form") | **No Billing tab exists** (tabs: `tab_overview`, and two hidden `tab_2` / `tab_3`). 112 must add a `tab_billing` tab, then the section. |

  Each form also has a `_managed.xml` copy (151e F3a: mirror the edit by hand). The other main forms named "Information" on the three entities are the OOB default and need no change.
- **Navigator side pane: does not apply.** `ensureNavigatorSidePane()` is the standard step for a code page the user navigates TO (its own app page). This page is a form-embedded web resource inside the host form's iframe: the pane is app-level, and the host forms already carry the trigger (the Matter and Project entity ribbons are wired per SPAARKE-SIDE-PANE-NAVIGATION; the global application-ribbon insertion covers every entity). Calling it from an embedded section would only duplicate the host's registration. InvoiceFiles (151e, decision 6) took the same decision. Note for 112: the architecture doc lists Matter and Project among the wired entity ribbons but not Work Assignment; the global application-ribbon insertion covers it, and that is outside this task.
- **ui-tests:** the POML's five browser checks run after 112 deploys (not run here: no Dataverse environment in this task). Verify first: a record with a billing firm and an override, since 084's rights check is all-or-nothing (084 judgement call 6: `RetrievePrincipalAccess` must answer for `sprk_organization` and `systemuser` rows) and the page shows nothing otherwise.

### Verification

- `npm run build` in `src/solutions/RecordProcedures`: succeeds (`check-html-css-reset` ok; `tsc-surface-gate`: 0 errors in the page's own files, 98 pre-existing in shared libs, deferred to Phase B as for every code page); `dist/sprk_recordprocedures.html` 1,181,939 bytes (357.9 kB gzip).
- `npx jest`: 5 suites, 82 tests, all pass. Tests for the criteria: both lists with counts and each count card filtering to exactly its rows; firm and date changes re-reading both routes with the new `firmId` and `date`; picker order (billing firm first, then invoiced firms, each once); 404 (from either route), 503 `procedures.unavailable`, 503 `ebilling.disabled`, 400, network and a malformed body, each with no rows on either tab; the gap bar and Show; read-only negatives; unknown mark; dark theme render; no colour literal.
- Mutation proof: with the panel changed to accept the Procedures route alone (a partial list when the rules route fails), 3 tests fail; restored, 82 pass.
- `npm run lint`: the script exists (copied from InvoiceFiles) but `eslint` is not a dependency of this package, as in InvoiceFiles, so it cannot run; the shared type-aware lint (`scripts/quality/client-lint`) does not cover this folder. Not run.
- Tests beyond the POML criteria, one line each: launch parsing (the page must never guess a route); the firm read (K2 filter, OData injection guard on the record id); wording (the 404 text is identical whichever route sent it); the dimmed-set extension (the 084e hook); the source scan (the read-only criterion's negative).
- TASK-INDEX.md not updated (the brief limits the diff to the folder and this notes file; the orchestrator owns the cell).

### 084a fix round 1 (branch `task/ebilling-084a-r2`, from `7f9dd2a080`)

Review findings W1 to W5 and S1 to S2 fixed; the rest recorded below as known limits. `npm run build` succeeds; `npx jest`: 5 suites, 107 tests, all pass (was 82).

- **W1 (F4), out-of-order responses.** `ProceduresPanel.tsx` already had a `cancelled` guard on its read effect, but nothing proved it. New tests with deferred promises for BOTH routes: (a) two date changes, (b) a firm change then a change back to the billing firm, (c) the older response arriving with its two routes in the opposite order to the newer one's, (d) an older FAILURE arriving after a newer success. In each the newer request is answered first and the older one second, and the newer rows (and the date or firm shown) must remain. Seeding: with the `if (cancelled) return;` in the `.then` removed, all 4 fail; restored, they pass.
- **W5 (F4), ready, then a failing change.** 12 tests: a firm change and a date change, each with (both routes fail / only the Procedures route fails / only the rules route fails), each started from the Procedures tab and from the Rules in force tab. Each asserts the `unavailable` state, that both routes were re-read, no row of either kind, no tab, and a Retry button. Seeding: with the panel changed to accept either route (`procedures.ok || rules.ok`), the 8 single-route cases fail (the 4 both-fail cases still pass, as they should); restored, all pass.
- **W4 (F4), test title.** The sign-in failure case was added at panel level to the "400, a network failure and a sign-in failure" loop (now three cases, so the title is true), plus a test that the sign-in state is standalone (no pickers), says "Sign-in could not be verified", and that Retry reads both routes again and recovers.
- **W3, the 5,000-row cap is silent (`firmChoices.ts`).** `WebApiLike` now types the `nextLink` that `Xrm.WebApi` returns. A `nextLink` on the invoice read (more rows than one page) makes the result `complete: false` (also when the page names no firm yet), and so does one on a name read; the existing "could not be listed" caption then shows. The page is still not followed (K1 stays a known limit; the billing firm is always offered). Tests: nextLink with a firm, nextLink with none, none or empty is complete; 45 firm ids are read in chunks of 20, 20 and 5 (each id once, all firms back); exactly 20 is one chunk and 21 is two; the panel offers readable firms with the caption, and shows no caption when complete. Seeding: with the two `nextLink` checks removed, 2 fail; with `complete = false` removed from the chunk `catch`, 2 fail (the new chunk test and the existing names-fail test); restored, all pass.
- **Decision, a chunk fails while another succeeds:** `invoicedBy` returns the firms from the chunks that were read, with `complete: false`, and the panel OFFERS those firms and shows the caption. A firm is only ever offered after the caller's own `sprk_organization` read returned it (K2), so a partial list never offers a firm the routes would refuse; hiding readable firms because a sibling chunk failed would only remove choices. A later chunk still runs after an earlier one fails. Tested at `firmChoices` level (chunks 1 and 3 offered, chunk 2 not, `complete: false`) and at panel level.
- **S1.** The dead `status === 503 && errorCode === procedures.unavailable` line in `failureKind` is removed. Checked first: it returned `'unavailable'`, as the next line (any 503) does, and nothing sits between them, so no input changes its answer (the existing `failureKind` tests for 503 with and without the code still pass). `CODE_PROCEDURES_UNAVAILABLE` stays exported as documentation of the server's code.
- **S2.** The header comment of `authInit.ts` no longer says "Files page call (list, open, download)" (copied from InvoiceFiles); it says what this page calls: the GET reads of `.../procedures` and `.../rules-in-force`.
- **W2, resolved without code.** The POML's "one state badge" applies to Procedure rows (one badge each, tested). Rule rows show one badge per mark, as the prototype's `RuleBadges` does (`spaarke-prototype/projects/ebilling.r1/src/procedures/parts.tsx:58-64`); the Rules tab test ("one badge per mark") already asserts it.
- **Known limits, no code in this round:**
  - S3: a 401 and a 403 read as "retry" (generic message plus Retry). Revisit after 084f.
  - S4: the "and n more" tooltip cannot be reached by keyboard. Belongs to 084b's row work.
  - S5: the hidden scrollbar was copied from InvoiceFiles.
  - S6: `buildCountCards` may duplicate InvoiceReview's. Check before 084b.
  - S7: the dependency list was copied from InvoiceFiles for the shared barrel.
  - S8 to S10: minor, not worked.

## uac-r2 scoped re-check (2026-11-12): PASS

uac-r2 re-checked fix round 3 (`9e398e23e3..a7dc4c5276`) and the 084f role diff (`a52d754412..02e4d9396f`). This was the fix diff and its direct callers only, not a new full review. Both passed, and 084 and 084f may stay merged in work.

- F-A, F-B, F-D and K2 are accepted as fixed. The deny-each-asked-row and remove-each-unasked-version-row guards are the mechanical form uac-r2 asked for. Each exemption key names its route segment (`/procedures/` or `/rules-in-force/`) and carries a reason.
- uac-r2 asked us to confirm three things, all checked:
  - **Firm name.** `FirmNames` for a Lookup firm is `sprk_organizationname`, the primary name of `sprk_organization` (its `DisplayMask` includes `PrimaryName`). That is the same value the lookup's formatted value shows, and nothing richer.
  - **Episode filter.** The test that pins the read service's episode filter to the resolver's answer stays until 084e extracts one shared helper: `BaseRetired_ShowsTheCurrentEpisodesVersion_AsTheResolverCountsIt` asserts against `ResolverSays`.
  - **084f.** The role diff passed; uac-r2 found NFR-05 holds.
- At 112:
  - The dry run resolves both privileges from metadata and stops if either is absent.
  - Send uac-r2 a notice before and after the dev role apply.
  - After the AFTER notice, uac-r2 re-runs its SecureChild `-Verify`.
  - 112 step 2 and step 5 now say so, and step 5 also deploys `sprk_recordprocedures`.

## 084e: design approved by ontology-r1 (2026-11-12), with binding changes

Design sent 2026-11-12 (the pure function `RulePrecedence.Resolve<T>(precedence, versions, rankOf, statesCap, valid)` → `PrecedenceResult<T>(ProcedureRank? Top, Outcomes, Displacing)`, with outcomes `Applies` / `DisplacedOutright` / `CapDisplaced`). The evaluator and 084's rules-in-force read call it; ProcedureResolver's episode filter becomes one internal static helper. ontology-r1 approved it, binding the following.

**O-1. `valid` stays lazy, in today's order.** An eager `valid` is an F1 behaviour change, because `HandlerAccepts` is not pure: on a refusal it logs, records telemetry and adds a reported `handlerRefusals` entry.
- Resolve walks ranks in descending order and calls `valid` only until it finds Top. It never calls `valid` or `statesCap` on a version ranked below Top.
- Computing "Displacing at Top" may call `valid` on the other versions at Top. They Apply and are judged in the loop anyway, and the result is cached. Say so in a comment.
- Must-fire parity case: under Replace, a valid higher version plus a lower version whose handler throws `BillingRuleRefusedException`. Refusals, telemetry calls and the handler-invocation count are identical before and after. The handler is never invoked for the lower version.

**O-2. Outcomes are a list in input order:** `IReadOnlyList<(T Version, PrecedenceOutcome Outcome)> Outcomes`, never a dictionary keyed by T. `BillingRuleCandidate` is a record with value equality, so duplicate keys would throw, and a list keeps today's iteration order.

**O-3. Ranks and ties use ProcedureRank's own equality and CompareTo** (`GroupBy(rankOf)` + `OrderByDescending(g.Key)`). A tie is an equal ProcedureRank. No new comparer and no Scope term.

**O-4. Grouping by PrecedenceKey stays the caller's job.** Resolve carries the comment "versions MUST be one PrecedenceKey group of one subject". The read tests that a late-fee cap and an administrative-fee cap never mark each other.

**O-5. Parity fixtures.** In each, `displacedOutright`, `capDisplaced`, `subjectsByVersion` and refusals are identical before and after:
- Replace: a valid higher version displaces the lower one.
- Replace: the higher version is refused by its handler, so the lower one stays in force.
- Replace: the higher version is downgraded, so the lower one stays in force.
- Split: a capped higher version makes the lower one CapDisplaced (the lower one is still judged).
- Split: a capless higher version leaves the lower cap in force (must-not-fire).
- A tie at Top: both Apply, and both are in Displacing.
- Additive: nothing is displaced.
- A legacy policy version is in the group (R6).
- The O-1 case.

Existing evaluator tests stay unchanged.

**O-6. The read.**
- `valid` = gate-accepted (`TryPrepareBillingRule`) and not downgraded.
- No rank loop of its own.
- A mark only on an outcome other than Applies; "by" = every Displacing version at Top; the R4 note text.
- Must-not-fire cases:
  - a tie with Top gets no mark;
  - a version above Top that is not displacing (capless under Split, or downgraded) gets no mark.

**O-7. Episode helper.** One boundary test: the helper and `RecordProcedureReadService.CurrentEpisodeVersion` agree when a version's CreatedOn equals the Exclude row's CreatedOn ("on or after" means it is included). ProcedureResolver's existing tests stay unchanged.

**Before merge:** the Services/Signals/** diff and the parity test go to ontology-r1.

## 084e (built), branch `task/ebilling-084e` from `c528133c11`

Commits: `539d467e40` parity tests (green on today's evaluator) → `6d0b1667f6` extraction → `3c2cccbad5` episode helper → `6c139c0d4d` the mark. Signals diff for ontology-r1: `git diff c528133c11 6c139c0d4d -- src/server/api/Sprk.Bff.Api/Services/Signals/`.

**The function** (`Services/Signals/Billing/RulePrecedence.cs`):
`public static PrecedenceResult<T> Resolve<T>(BillingRulePrecedence precedence, IReadOnlyList<T> versions, Func<T, ProcedureRank> rankOf, Func<T, bool> statesCap, Func<T, bool> valid)`
→ `PrecedenceResult<T>(ProcedureRank? Top, IReadOnlyList<(T Version, PrecedenceOutcome Outcome)> Outcomes, IReadOnlyList<T> Displacing)`, outcomes `Applies` / `DisplacedOutright` / `CapDisplaced`. A version displaces when it is valid and (not Split, or it states a cap). The evaluator's step 4 now calls it and keeps its own loop for subjects, refusals and the cap-displaced set.

**How O-1..O-7 are met**
- O-1 (lazy valid): ranks descending, stop at the first rank with a displacing version; `valid`/`statesCap` never called below Top; every version at Top is asked once (cached by the evaluator). Tests: `RulePrecedenceParityTests.Replace_ValidHigher_LowerWhoseHandlerRefuses_IsNeverRun` (refusals, telemetry and handler-call count identical; handler never invoked for the lower version), `RulePrecedenceTests.BelowTop_IsNeverAsked_AndNothingIsAskedTwice`, `Split_CaplessHigher_LeavesTheLowerCap_AndIsNotAskedForValidity`.
- O-2 (list in input order): `RulePrecedenceTests.Outcomes_AreInInputOrder_AndKeepDuplicates`.
- O-3 (ProcedureRank equality/CompareTo, no new comparer): `GroupBy(rankOf)` + `OrderByDescending(g.Key)`; `RulePrecedenceTests.TieAtTop_BothApply_BothDisplace`.
- O-4 (grouping is the caller's): the "one PrecedenceKey group of one subject" comment on Resolve; `RecordProcedureOutrankedContractTests.AddedFee_LateFeeAndAdminFee_AreSeparateKeys`.
- O-5 (parity), all in `RulePrecedenceParityTests`: `Replace_ValidHigher_DisplacesTheLower`, `Replace_HigherRefusedByItsHandler_LowerStaysInForce`, `Replace_HigherDowngraded_LowerStaysInForce`, `Split_CappedHigher_LowerIsCapDisplaced_AndStillJudged`, `Split_CaplessHigher_LeavesTheLowerCapInForce`, `TieAtTop_BothApply_TheLowerIsDisplaced`, `Split_TieAtTop_WithALowerRefusingVersion_SameCountsAndRefusals`, `Additive_NothingIsDisplaced`, `LegacyVersion_IsInTheGroup_AndOutranksAProcedureRule`, plus the O-1 case. Existing evaluator and ProcedureResolver tests are byte-unchanged.
- O-6 (the read), in `RecordProcedureOutrankedContractTests`: must-fire `Replace_ValidHigher_MarksTheLowerOutranked`, `Split_CappedHigher_MarksTheLowerCapOutranked`, `LegacyHigher_OutranksAProcedureRule_AndIsAsked`, `LegacyOutrankingRow_Unreadable_Is503`; must-not-fire `TieAtTop_ListsBoth_MarksNeither`, `GateRefusedHigher_LeavesTheLowerUnmarked`, `DowngradedHigher_LeavesTheLowerUnmarked`, `InvalidLegacyHigher_LeavesTheLowerUnmarked`, `Split_CaplessHigher_LeavesTheLowerCap`, `AboveTopNotDisplacing_GetsNoMark`, `Additive_MarksNothing`; plus `NoDisplacedWording_ReachesTheDto`, `ProceduresRoute_ReadsNoLegacyRules`. `valid` = gate-accepted (`TryPrepareBillingRule`) and not downgraded; the group is legacy rules (as the evaluator loads them) plus the resolver's on-date Procedure rules (R6); no rank loop of its own.
- O-7 (episode helper): `ProcedureResolution.CurrentEpisodeVersions` (internal static) is used by `ProcedureResolver.RecordRuleVersionOn` and `RecordProcedureReadService.CurrentEpisodeVersion`. `RecordRuleEpisodeTests.VersionCreatedAtTheBoundary_IsInTheEpisode_InBothCallers`, `NoBoundary_EveryVersionCounts`, `KnownBoundary_NoVersionReachesIt_IsAFault`.

**The mark.** `RuleMarkView` gains `Text`, `Note` and `By` (a list of `OutrankingRuleView`). Kinds `Outranked` ("Outranked by X") and `CapOutranked` ("Cap outranked by X"); several codes read "A and B" / "A, B and C". Note: "applies unless X is/are refused when an invoice is checked; a line can differ (dates, fallback)" (R4). Only the rules route reads legacy rules. EB-D-218: every candidate at rank ≥ Top is asked of the caller (policy, version, rule set; a legacy policy's versions; a Procedure and its versions); the three guards stay green with a new seed `outranked`. Exemptions, each naming segment and reason: `outranked/procedures/*` (the procedures list shows no rule), and `outranked/rules-in-force/LEGACY-CD-RATE` (below Top, so the read never consults it, per O-1).

**Parity results.** The parity fixtures pass unchanged before and after the extraction. Seeding proofs (each reverted): without the mark, 7 fail; without exposing the deciding rows, 6 fail (including the GUID guard and the version-row guard); ignoring downgrades, 2 fail; `statesCap` always true plus legacy dropped from the group, 8 fail.

**Build, tests, publish size**
- BFF build: 0 warnings, 0 errors.
- Tests (filters Procedure, InvoiceCompliance, Billing, RulePrecedence, RecordRuleEpisode): 3413 passed, 0 failed. RecordProcedure filter: 257 passed. ArchTests: 2513 passed, 0 failed.
- Publish (Release, linux-x64, framework-dependent, `Compress-Archive -CompressionLevel Optimal`): fresh `origin/master` `76b2930961` 191 files, zip 42,034,770 B; branch head `6c139c0d4d` 191 files, zip 42,028,480 B. Delta about 0 MB (−6,290 B; master has later commits than this branch's base). No new package.

**Deviations and known limits**
- `InvoiceComplianceEvaluator.IsSystemRule` and `DisallowedConsequenceType` went from private to internal so the read applies the same filters (bodies unchanged).
- The read builds its gate snapshot the way the evaluator's step 2 does. So a gate-refused rule logs an error and counts `billing.rule.invalid` on each rules read as well as each invoice check (known limit, K-class: telemetry volume only).
- `HandlerAccepts` call order may differ from today only for a Split tie at Top against a lower version. The call set, refusals and counts are identical (`Split_TieAtTop_WithALowerRefusingVersion_SameCountsAndRefusals`).
- `RecordProcedureReadModelContractTests.InForceRules_EqualTheResolversOutput_FromOneResolution` now expects `oneResolution + 1` queries: the rules route reads the legacy rule sets once.
- The `RecordProcedureReadService` constructor takes `BillingFactLoader` and `PolicyVersionValidator`; both were already registered, so DI is unchanged.
- A gate-refused legacy version above Top makes nothing outranked, and it is not asked of the caller (its absence from the mark does not show it).
- Client: render `Outranked` and `CapOutranked` as neutral badges. A cap-outranked rule still applies to everything but its cap, so do not dim it.
- Noticed, not fixed (out of scope): a doc summary above `IsSystemRule` in the evaluator sits on the wrong member, and the display name of `Marks_AreFourKindsOnly` is now stale (there are six kinds).
