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
