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
**Dev apply (2026-11-13, owner OK typed in session; uac-r2 notices before and after).** Steps 1 and 2 above ran on dev: dry runs planned only the two Reads at Deep on each role; both applies changed exactly that; both `-Verify` runs PASS (exit 0). uac-r2's SecureChild `-Verify`: PASS. Task 112 step 2's 084f part is done; step 3 (the 200/503 probe) runs at 112 after the BFF carrying 084 is on dev.
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

### 084e fix round 1 (branch `task/ebilling-084e-r2`, from `47d8dbc337` + work `0c4b97acc7`)

Findings A to F (ontology-r1 review and our adversarial review). A and C are ontology-r1's binding designs. Not marked done: ontology-r1 reviews the `PolicyVersionValidator.cs` and evaluator diff first.

- **A. The read drives no gate telemetry.** `PolicyVersionValidator` has one private core, `TryPrepareBillingRuleCore(pv, out definition, out string? reason)`, holding the try/catch and `ValidateBilling` with no log and no meter. `TryPrepareBillingRule` is unchanged for its callers (core, then the Error log 50301 and `billing.rule.invalid`). New `TryPrepareBillingRuleForRead` (core, then at most a Debug log, no meter; its doc says read-side display only, never an evaluation). No `quiet`/`observe` parameter. `RecordProcedureReadService` calls `TryPrepareBillingRuleForRead`. Tests: `BillingGateForReadTests.ForRead_RefusesExactlyWhatTheGateRefuses` (theory over `BillingRuleCatalogueTests.GateRefusals` plus a system type and a disallowed consequence), `ForRead_AcceptsWhatTheGateAccepts`, `ForRead_Refusal_NoErrorLog_NoMeter` (K3 listener seam, must-fire control on the evaluation entry), `ForRead_NullSnapshot_Throws`; `RecordProcedureOutrankedContractTests.GateRefusedVersion_OnTheRead_NoErrorLog_NoMeter`. Mutations: the read calling `TryPrepareBillingRule` fails the read test; `ForRead` logging Error and metering fails both no-telemetry tests; `ForRead` ignoring the stored consequence fails both theories on that case.
- **B. Client renders the marks.** `RuleMark` gains `text`, `note`, `by` (`OutrankingRule`). `RULE_MARKS` has `Outranked` and `CapOutranked`; `markBadge` shows `mark.text`, else builds "Outranked by X" from `by` (never a bare "Outranked"). A rule in force with a precedence mark keeps "In force" first and is not dimmed (`PRECEDENCE_MARK_KINDS`; `DIMMED_MARK_KINDS` stays `Excluded`). The note is a line under the badges (`markNote`), the outranking rules with their level are the badges' tooltip (`markByHint`). Comments in `model.ts` and `RuleList.tsx` corrected; the open-array tests use an unknown `FutureKind`. Tests (`model.test.ts`): "084e fix 1 (B): Outranked shows the mark text…", "…CapOutranked shows the mark text…", "(B, negative): a precedence badge never reads a bare 'Outranked'…", "(B, negative): no note or tooltip for other marks; 'In force' is not added to a dimmed or not-in-force rule"; (`ProceduresPanel.test.tsx`): "an Outranked rule row shows 'In force' and 'Outranked by X'…", "a CapOutranked rule row…", "(B, negative): a row with no precedence mark has no note line and no tooltip". Mutations: dimming the precedence kinds fails 4, dropping the kept "In force" fails 4, a badge ignoring `text` fails 6.
- **C. Note wording (ontology-r1, exact).** Constants in `RecordProcedureReadService`, `{X}` = the displacing codes:
  - `OutrankedNoteSingular`: "does not run while {X} is in force; it can run if {X} is refused when an invoice is checked; a line can differ (dates, fallback)"
  - `OutrankedNotePlural`: "does not run while {X and Y} are in force; it can run if all of them are refused when an invoice is checked; a line can differ (dates, fallback)"
  - `CapOutrankedNoteSingular`: "its code check always runs; its cap yields to {X}'s and can apply if {X} is refused when an invoice is checked; a line can differ (dates, fallback)"
  - `CapOutrankedNotePlural`: "its code check always runs; its cap yields to those of {X and Y} and can apply if all of them are refused when an invoice is checked; a line can differ (dates, fallback)"
  - Plural = more than one displacing version at Top (`by.Count > 1`). Join rule (`JoinCodes`, client `joinCodes`): one code as is; two "X and Y"; three or more "X, Y and Z" (comma-separated, the last joined by " and ", no serial comma). Text stays "Outranked by X" / "Cap outranked by X" with the same join. Tests: `OutrankedNote_Singular_IsExact`, `OutrankedNote_Plural_IsExact`, `CapOutrankedNote_Singular_IsExact`, `CapOutrankedNote_Plural_IsExact`, `ThreeOutranking_JoinWithCommasAndAnd`, `JoinCodes_Rule`; the three earlier note assertions now use the new wording. Mutation: swapping the singular and plural branches fails 8.
- **D. Test gaps.** R6 on the read: `LegacyV1InForce_CurrentV2Later_V1Outranks` (v1 named under "by"; fails when `SelectVersion` becomes `IsCurrent`), `SystemRule_IsNotGrouped` (a legacy and a Procedure system rule never reach the gate; fails without the `IsSystemRule` filters), `ProcedureInAHole_OnTheDate_OutranksNothing` (fails with `RulesWithFallback`). O-7 wiring: `RecordRuleEpisodeTests.Resolver_RecordRuleVersionOn_AtTheBoundary_AgreesWithTheRead` (fails when `RecordRuleVersionOn` skips `CurrentEpisodeVersions`; the three helper tests still pass under that mutation). Guard seed: the `outranked` seed gains `GENERAL-TI` (All records) and `LEGACY-BAD-TI`, a gate-refused legacy Firm TimeIncrement ranked above that key's top. The removal guard (`EveryVersionRow_IsAsked_OrExemptBecauseTheBodyDoesNotDependOnIt`) now also walks legacy policy and legacy rule-set rows (rig `LegacyRows`; removal by entity set). New exemptions name their reason (procedures route reads no legacy row; below Top; gate-refused above Top). Mutation: exposing gate-refused rows makes the outranked rules-route exemptions stale and fails the guard. The three EB-D-218 guards are green.
- **E.** The consequence summary moved from `IsSystemRule` to `DisallowedConsequenceType`; `IsSystemRule` has its own one-line summary (comment only).
- **F.** `Marks_AreFourKindsOnly` is now `Marks_WithNoHigherRankedRule_AreTheFourRecordKindsOnly`, display name reworded (four record kinds; 084e adds Outranked and CapOutranked).
- **Not a finding (recorded).** A child-unit billing admin who cannot Read a parent-unit legacy rule set gets a 503 where a legacy rule outranks. The same accepted limit as for Procedure sets (EB-D-218, EB-D-220).
- **Results.** BFF build 0 warnings, 0 errors. Tests (filters Procedure, InvoiceCompliance, Billing, RulePrecedence, RecordRuleEpisode, PolicyVersionValidator): 3556 passed, 0 failed. ArchTests 2513 passed. RecordProcedures `npx jest` 10 suites, 204 tests pass (was 197); `npm run build` succeeds (tsc-surface-gate: 0 errors in the page's own files). No new package; publish size not re-measured (no package added). Existing evaluator, ProcedureResolver and PolicyVersionValidator tests and the parity tests are unchanged and green.

## 084b: the Procedure wizard and the rule Exclude

Branch `task/ebilling-084b`, from `6d786b335c`. Folder `src/solutions/RecordProcedures/`. Rigor FULL. No BFF file changed, no new package, no `src/dataverse/solutions/SpaarkeMaster/**` file changed. Every write is one of 089's routes (`ProcedureAssignmentEndpoints.cs`); no `Xrm.WebApi` write.

### What was built

| File | Purpose |
|---|---|
| `src/services/overridesApi.ts` | The five calls: `POST procedure-assignments`, `POST rule-overrides/exclude`, `GET overrides/{id}/removal-preview`, `DELETE overrides/{id}`, and 081's `GET /api/finance/procedures/{id}` (the Pin picker's versions). Bodies are exactly 089's request shapes (blank fields dropped, reasons trimmed). A refusal becomes `WriteFailure` (status, `errorCode`, title, detail, field problems). A 2xx whose body is not what the route returns is a failure, never a reported change. |
| `src/components/wizards/plan.ts` | Pure. A choice becomes ordered steps (`remove`, `assign`, `excludeRule`); `executePlan` runs them and stops at the first refusal. Also: which choices a row offers, which is current, the P-3 and O-20 rules. |
| `src/components/wizards/WizardFrame.tsx` | The shared `WizardShell` with the four steps Procedure or Rule, On this record, Impact, Confirm. Owns the removal-preview fetch and the write. A refusal is thrown out of `onFinish`, so the shell shows the title and stays open; nothing is re-read. |
| `src/components/wizards/ProcedureWizard.tsx`, `RuleWizard.tsx`, `steps.tsx`, `words.ts` | The two wizards, the shared step contents (choice list, Impact, Confirm, the O-21 line) and the words. |
| `src/components/rowOpen.ts` | Row click or Enter or Space opens the wizard; rows are tab stops. |
| `ProceduresPanel.tsx`, `ProcedureList.tsx`, `RuleList.tsx`, `main.tsx`, `model.ts` | The panel owns the open row, the "N of M" nav over the FILTERED list, and a silent re-read of both lists (same firm and date) after a write; `main.tsx` builds the client. |
| `src/__tests__/` | `plan.test.ts`, `overridesApi.test.ts`, `Wizards.test.tsx` (new); `wizardHarness.tsx`, `testKit.ts`, the seam and `ProceduresPanel.test.tsx` extended. |

Choices. Procedure: As set on the parent, Follow latest, Pin a version, Exclude here, Remove from this record. Rule: Run the published rule, Exclude here (firm and reason). There is no Change choice and no dry-run read; Impact shows 089's removal preview where an override is removed, otherwise the row's own before and after. Every Impact step, Confirm step and success screen carries the O-21 line.

### Decisions and deviations

1. **Read-only state: not resolved by any route (deviation, owner decision needed).** None of the read routes (084) or write routes (089) answers "may this caller write"; the only signal is the refusal of the write itself. As the brief directs: no client-side role check was invented, no BFF field was added. The wizards open with every choice enabled whoever the caller is; a refusal shows the ProblemDetails title and the wizard stays open. A test pins that the wizard offers every choice and never inspects the caller. **The ui-test "Read-only user" ("the wizard opens read-only; no write choice is enabled") and the goal's last sentence cannot pass as written.** Either reword them to "the write is refused and its title is shown, the wizard stays open", or add a `canWrite` field to the read DTO in a separate BFF task. This is the one place the task cannot meet its text.
2. **Switching an override is two calls.** 089 refuses a second row for the same subject (409 `assignment.duplicate`), so changing an existing override (pin to exclude, parent's to own) is: preview of the removal, `DELETE` the row, then `POST` the new one, in that order. The Confirm step says so ("current override is removed, then the new one is saved"). If the removal succeeds and the create is refused, the wizard stays open, says "the record's earlier override was removed but the new one was not saved", and re-reads the lists because the record did change. This is the only case where a refusal re-reads.
3. **Pin versions** come from 081's `GET /api/finance/procedures/{id}` (084's view lists only the version in force and the newer one). Active and Superseded versions are offered (EB-D-155); Draft is not.
4. **Remove from this record** is offered for a manually assigned Procedure that has an override row of its own on this record and no parent override (with a parent override, "As set on the parent" is the removal). For a Procedure that is not manual, removing the record's override is "Follow latest".
5. **Exclude on a rule** is blocked on a rule changed on this record (and on a record rule): remove the change first (Run the published rule), then exclude. A message says so.
6. **A BaseRetired record rule has no override row id in 084's mark**, so it cannot be removed from here. Gap for 084c, which owns Change.
7. **P-3.** A rule a parent excluded or changed shows "As set on the parent" selected; "Run the published rule" is disabled with "Not available: the parent's rule exclusion applies to this record. Change it on the parent." A disabled choice also cannot be chosen by an event (guard in `ChoiceGroup`, found by a test: jsdom let a click through a disabled radio).
8. **O-20.** A rule that names one firm is excluded for that firm only: the firm field is fixed to it and its id is sent. A rule with no firm offers "All firms" and the firms the panel already knows (billing firm, rules' firms, invoiced firms).
9. **S4 (keyboard access to the row)** from 084a fix round 1 is addressed for the rows: they are tab stops and open the wizard on Enter or Space, and the wizard lists every source of the row. The "and n more" tooltip itself is unchanged.
10. **S6** (`buildCountCards` vs InvoiceReview): not worked, no new count card was added.

### Verification

- `npm run build` in `src/solutions/RecordProcedures`: succeeds (`check-html-css-reset` ok; `tsc-surface-gate`: 0 errors in the page's own files, 98 pre-existing in shared libs); `dist/sprk_recordprocedures.html` 1,283,563 bytes (was 1,181,939 in 084a: the shared `WizardShell` bundle). No new package.
- `npx jest`: 8 suites, 180 tests, all pass (5 suites and 107 tests before this task; 084a's "rows are inert" assertions were rewritten because this task makes rows open the wizard).
- Mutation proof (changes made together, then restored): (a) the refusal path re-reading both lists, (b) the success screen without the O-21 line, (c) the disabled-choice guard removed. 20 of the 36 wizard tests fail, including each of the refusal tests, the O-21 test and the P-3 test; restored, all pass.
- The negatives: no `Change for this record` text, no choice labelled Change on any of the six fixture rows, no call on a route outside the five (the harness classifies any other route as `other` and fails), a scan of the wizard sources (comments stripped) for `rule-overrides/change`, dry-run and change helpers, and a scan that the only HTTP writes in the page are in `overridesApi.ts`. Each scan has a must-fire control.
- `npm run lint`: not run (eslint is not a dependency of this package, as in 084a).

### For task 112 (ui-tests after deploy)

- **Pin a version.** On a firm Procedure with two versions, pin the older one: the row shows "pinned to v1, v2 available", the O-21 line was on Impact and on the success screen, no console errors.
- **Exclude and remove.** Exclude with a reason, open it again and remove the override: the preview shows the affected-invoice count (or "closes no findings") before Confirm, then the row returns to its source state.
- **Switch.** Pin then Exclude on one Procedure: two calls, and the lists show Excluded. Check the 409 path never appears in the normal case.
- **Rule exclude on a child.** On the work assignment, a rule its parent matter excluded: "As set on the parent" is selected, "Run the published rule" is disabled with the reason.
- **O-20.** A rule that names a firm: the firm field is fixed and the row afterwards shows the exclusion for that firm.
- **Refusal.** The "Read-only user" test as reworded in decision 1: a caller 089 refuses sees the ProblemDetails title in the wizard's error bar, the wizard still open, and no list reloaded. Use a user and record where the route answers 403 or 404.
- **Nav.** Click a row, use the arrows through a filtered list ("N of M" follows the count card's filter); the form starts from each row's own state.
- **Dark theme.** Repeat the first test; all colours are Fluent tokens (a test scans the wizard files for colour literals).

### 084b fix round 1 (branch `task/ebilling-084b-r2`, from `a31bfdb737`)

Ten review items, all fixed in `src/solutions/RecordProcedures/` (no BFF, shared-library or package change). New tests: `planFix1.test.ts`, `WizardsFix1.test.tsx`. Mutation proof: the new enum test against the old `plan.ts` fails 7 of 9; reverting the nav/close guard, the `changed` state, the pinnable check, and the partial-save hold each fail their tests.

1. **`appliesTo` word.** `isManual` compared `'Manual'`; the server sends `ProcedureAppliesTo.ToString()` (`AllRecords | Firm | AssignedRecordsOnly`). Now `APPLIES_TO_ASSIGNED_ONLY = 'AssignedRecordsOnly'`; 084a's fixtures and `plan.test.ts` corrected. Every enum word the wizard code compares was checked against the C# (levels, modes, gap kinds, statuses, mark kinds, version statuses): only `appliesTo` was wrong. Tests (`planFix1.test.ts`): "item 1: a Procedure is assigned only here exactly when the server says AssignedRecordsOnly" and "every enum word the wizard code compares against is a word the server sends" (both read the C# source).
2. **Save then ‹ › or ×.** Fixed locally in `WizardFrame`: while a save runs, `nav.onBeforeNavigate` returns false and the close handler does nothing. The shared `WizardShell` (which leaves the header ‹ › and × active during a save) is not edited; that gap is filed separately. Test: "item 2: Save on Alpha, then › and ×: nothing moves, and the result lands under Alpha" (and "after a refused save the header works again").
3. **Rule changed on this record.** `currentRuleChoice` returns `changed` (no radio selected, Next off); the "Run the published rule" caption names the published values (`baseParameterSummary`) against the changed ones. Tests: "item 3: ... opens with nothing selected" (page), "its current state is none of the choices" (plan).
4. **Pin and reason the route would refuse.** A pin plans only for a version in the pinnable set (Active or Superseded, from 081's read), so a Retired own pin cannot be re-saved after its row is removed; the field says "This version can no longer be pinned". Reasons are capped at 2000 characters (Textarea `maxLength`, and the plan checks the trimmed length). Tests: "item 4: a pin of a version that is not in the pinnable set plans nothing", "a reason over the server's 2000 characters ... plans nothing", "the record's own pin is of a Retired version ...", "the Reason fields stop at the server's 2000 characters".
5. **Stale row after a partial save.** The panel replaces the open snapshot with the re-read row of the same key, so a retry sends only the create; if the re-read no longer has the row, Finish stays off. Tests: "item 5: Pin -> Exclude ... a second Save is the create alone" and "when the re-read no longer has the row, Finish stays off".
6. **Exclude over a parent's change.** `excludeBlocker` says "Changed on the parent; change it there." Test: "items 6 and 10: a rule a parent CHANGED ...".
7. **"In force" after removing a rule exclusion.** The choice caption, the after-text and the success screen say the rule "runs again unless a parent excludes it" and that the lists show the result. Test: "item 7: ... does not promise the rule is in force".
8. **409 `assignment.duplicate` on a rule Exclude** adds "An exclusion for another firm may exist on this record; switch the firm to see it." (other refusals unchanged). Test: "item 8: a duplicate exclusion says another firm may hold it".
9. **The "every choice enabled" test** is replaced by a source scan that nothing under `components/wizards/**` reads caller, role or privilege data (with must-fire controls); the 422 refusal case is dropped, because no route in `ProcedureAssignmentEndpoints.cs` or `ProcedureAssignmentService.cs` returns 422. Test: "the wizard code reads nothing about the caller".
10. **BaseRetired wording.** "No override row is available here; see 084c." Test: "a retired base rule's record rule does not claim to be removed from the change flow".

Results: `npx jest` 10 suites, 197 tests, all pass (was 8 suites, 180 tests: the two new files add 18, the refusal table loses its 422 row, and one test is replaced). `npm run build` succeeds (tsc-surface-gate: 0 errors in the page's own files).

**Owner decision on the read-only escalation (2026-11-12):** EB-D-221. The read routes will return the caller's write permissions, and the wizards will open read-only from them. That is task 084g (uac-r2 conditions U-1 to U-5), which makes the "Read-only user" ui-test satisfiable. The shared WizardShell gap from review finding 2 is filed as ISS-016 (#2755).

**084e fix round 2 and close (2026-11-12).** ontology-r1 re-checked fix round 1 (`47d8dbc337..f823ac6489`) and approved it, with two items:

- **The guard.** `tests/Spaarke.ArchTests/BillingGateForReadCallerGuardTests` IL-scans the BFF and allows `TryPrepareBillingRuleForRead` callers only in `Sprk.Bff.Api.Services.Finance.Procedures`. It carries a must-fire control (a fixture caller outside the read models) and a must-not-fire control (the read service call is seen and allowed).
- **The doc comment.** `TryPrepareBillingRule`'s summary now says it is the only way to obtain a definition *for an evaluation*, and names its display-only sibling.

Both are in commit `068b0911de`, and ontology-r1 asked for no further re-check. Known limit, accepted by ontology-r1: the read-entry Debug line reuses the `PolicyVersionInvalid` event id. 084e is DONE.

## 084g

Branch `task/ebilling-084g`, from `f823ac6489` (084e fix round 1) merged with work `fe87088821`. Rigor FULL. No new route, no new package, no DI registration, no ledger change. Status: in progress until uac-r2 passes the BFF diff (`git diff f823ac6489..task/ebilling-084g -- src/server tests`).

### What was built

- **One declaration, two users** (`ProcedureAssignmentEndpoints.cs`). The record check is `RecordWriteCheck` (operation `ebilling.record.override`), used by the routes through `RecordTargets`. Each write's privileges are `AssignOrExcludePrivileges` (= `BillingAdminAuthorization.Admin()`), `RemovePrivileges`, `ChangePrivileges` and `PreviewRemovalPrivileges`, each composed from the existing `BillingAdminAuthorization` declarations. `AssignTargets`, `ExcludeTargets`, `ChangeTargets`, `PreviewRemovalTargets` and `RemoveTargets` are now the subject's Read plus these, in the same order as before. `WritePermissionChecks(flag, set, id)` is `RecordWriteCheck` followed by the flag's privileges: the route's own checks minus its per-subject Read. No privilege name is written in the read path.
- **The evaluator, for the caller** (`FinanceAuthorizationFilter.EvaluateCheckForCallerAsync`). It runs the filter's own `EvaluateAsync` for one check with the caller's oid and bearer token, the filter's Record, Privilege and Document paths, and the registered probe. It is built in `UniformNotFoundFailClosed` mode, so a privilege question with no complete answer is a fault (`Faulted`), not a denial; only `Held` allows, as in the write route's mode. No app-only branch, no role lookup. A missing caller, no `AuthorizationService`, an exception, or a system-failure answer is `Faulted` and logged at Warning.
- **The flags** (`ProcedureAssignmentEndpoints.WritePermissionsForCallerAsync`). Each distinct check is asked once and its verdict reused (the record check and `Admin()` serve all three flags). A flag stops at its first check that is not allowed. Any fault or exception gives `RecordWritePermissions.None` for the flag, with a Warning.
- **The read** (`RecordProcedureEndpoints.cs`, `RecordProcedureReadService.cs`). Both views carry `Permissions` (`{assignOrExclude, remove, change}` on the wire; default all false). The endpoint fills it only after the service returned the list (`Done`). A 404, 503 or 400 asks no permission check and has no flags. The service, its batched Read check (EB-D-218) and the filter order are unchanged.
- **Client** (`src/solutions/RecordProcedures/`). `readPermissions` treats a flag as true only when it is the boolean `true`, so a missing or malformed object is all false. `model.wizardReadOnly` makes a wizard writable only when both answers give `assignOrExclude` and `remove`. The panel snapshots `readOnly` with the row and passes it as a prop. The read-only `WizardFrame` has two steps (the row, On this record): every choice is disabled under "You can view this, but not change it.", there is no Impact, no Confirm and no Finish (Back, Next, Close), and `onFinish` refuses. With `readOnly` false it is 084b's frame.

### uac-r2 conditions

| | How it is met | Tests (`RecordWritePermissionsContractTests` unless stated) |
|---|---|---|
| U-1 | The flags run the write route's own evaluator for the caller's oid and token (`EvaluateCheckForCallerAsync`), over the checks the route declares (`WritePermissionChecks`). No app-only path, no role lookup, no second list. | "both read routes on every core record return permissions; ... asked as the caller" (asserts every privilege question carried the caller's token); "guard: each write route's declarations equal its flag's checks plus that route's per-subject Read"; "guard (behaviour): every write route, run through its real filter chain, asks the caller exactly its flag's record check and privileges" |
| U-2 | The flags are output only. No write route reads them; each still runs its record filter, privilege filter and per-subject Read. The client still shows a refusal at Finish when the flag was true. | "must-fire (U-2): with every flag true, a write whose per-subject Read fails is still refused by the write route" (assign, exclude and change 403; DELETE with an unreadable stored subject 404 `assignment.override_not_found`; nothing written); client `WizardsReadOnly.test.tsx` "U-2: a true flag is a hint; a 403 from the write route at Finish is shown with its title" |
| U-3 | See the cache answer below. | "(U-3): the flags are computed on every read for the caller; a right removed between two reads is false on the second" |
| U-4 | Vetoes come from the same evaluator: the record check is the caller's own rights on the record as Dataverse answers them, so a record whose Write or AppendTo is withheld from the caller gives every flag false, and the write route refuses the same caller (uniform 404). Step 0 refuses every refused caller kind on both read routes before any body, so there are no flags. | "must-fire (U-4): a record the caller may Read but whose rights Dataverse restricts for it ... gives every flag false, and the write route refuses the same caller"; "(U-4): every caller kind step 0 refuses (StatedCaller.RefusedKinds) is refused by both read routes: no body, so no flags, and no check is asked" |
| U-5 | A fault (record check throws, privilege unanswered, privilege question throws, no probe registered) makes the affected flags false, logs a structured Warning, and leaves the list as a 200. Never 503, never true. | "must-fire (U-5): a faulted or unanswered check makes its flags false, never true and never a 503; the list is still 200 and the fault is logged at Warning" (10 cases) |

**U-3 cache answer.** Nothing caches the read's result: the view is built per request and the flags are added afterwards in the endpoint, per request, for that caller only. The record-rights answer the flags' record check uses comes through `IAccessDataSource`. In production that is the existing `CachedAccessDataSource`, keyed by entity set, caller oid and record id with a 60-second lifetime. That is the same cache, keyed by caller, that the write route's record filter uses, so the flag and the route see the same answer. Privilege questions go to the probe uncached. A right removed in Dataverse can therefore show as true for up to 60 seconds on both the flag and the write route; no other caller ever sees the flag.

**Extra calls per read** (only when the list is returned): one record-rights check (shared by the three flags) plus at most six privilege questions, each distinct privilege once. A caller holding every check: 1 + 6. A caller without `Admin()`: 1 + 2 (the change flag asks its first privilege before reaching the shared `Admin()`). A caller without Write or AppendTo on the record: 1 + 0. Test: "the extra checks per read". The read's own batched Read check is unchanged (one batch). The privilege questions are sequential; the evaluator answers one check per call, so they cannot be batched further without a new probe method.

### Acceptance criteria, mapped to tests

1. **Flags on every route; one check denied turns off its flags.** "both read routes on every core record return permissions" (6 routes); "must-fire: denying exactly one check turns off exactly the flags whose write route declares it" (8 checks × 2 lists, from a table stated in the test, not derived from the declarations).
2. **Guard.** "guard: each write route's declarations equal its flag's checks plus that route's per-subject Read" (4 writes × 3 cores, must-fire: a check added to the route only, added to the flag only, or dropped from the route fails it); "guard: the removal preview's declarations are the remove flag's minus Write on sprk_policy"; "guard (behaviour)" (15 routes).
3. **Fault is false with a 200.** U-5 above; "no permission check runs when the list is not returned". The EB-D-218 guards in `RecordProcedureEndpointsContractTests` are unchanged and pass.
4. **No write route's behaviour changes.** `ProcedureAssignmentEndpointsTests` (089) unchanged and green; "guard (behaviour)" runs every write route through its real filter chain. The record filters moved from the literal-operation overload to `RecordTargets`, the same check. `OperationAccessPolicyCompletenessTests` still finds `ebilling.record.override`, now through the `Operation = "…"` initializer in `RecordWriteCheck` (comment updated).
5. **Client.** `WizardsReadOnly.test.tsx`: "the Procedure wizard: every choice disabled with ..., no Impact, no Confirm, nothing written", "the rule wizard: the same", "read-only (negative) when ..." (assignOrExclude false; remove false; either answer false; no permissions in either or in one answer; flags that are not the boolean true; permissions not an object), "every flag true" / "change false" (084b behaviour, saves), the U-2 refusal test, and three parsing tests.
6. **"Read-only user" ui-test** is satisfiable: run it at 112 with a caller without `Admin()`.
7. **Build, size, packages.** See below.

### Deviations and judgement calls

1. **A wizard uses both `assignOrExclude` and `remove`.** The POML says "the matching permission". Each wizard can plan an assign or exclude and a removal (a switch is both, 084b decision 2), so the wizard is read-only unless both are true. It does not enable some choices and disable others. `change` opens nothing until 084c. This is stricter, never looser.
2. **Both answers count.** Both read routes return the flags; the client takes a flag as true only if both answers say so.
3. **The removal preview has no flag of its own.** Its checks are a subset of `remove`'s (guard test), so a true `remove` never hides a preview refusal.
4. **The source scan is unchanged.** The wizards take `readOnly`, a prop; nothing under `components/wizards/**` names permissions or the caller.
5. **084b's fixtures** carry `WRITE_ALL`, so 084b's tests still describe a caller who may write.
6. **The read-module scan** in `ProceduresPanel.test.tsx` reads comments too; the new comment in `recordProceduresApi.ts` names the writes in words rather than by route.

### Verification

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- BFF tests filtered to Procedure, FinanceAuthorization, OperationAccessPolicyCompleteness, RecordWritePermissions and RouteAuthorization: 1701 passed, 0 failed. New file: 93 tests.
- `tests/Spaarke.ArchTests`: 2513 passed, 0 failed (includes RouteAuthorizationGuardTests).
- Fail before: with the old `RecordProcedureEndpoints.cs` (no flags filled), 31 of the 93 new tests fail; the other 62 are the declaration guards and step-0 refusals, which do not compile without the new declarations. With the old panel, frame and wizards, 10 of the 16 client tests fail; the 6 that pass are the unchanged 084b behaviour, U-2 and the parsing tests, which do not compile without `readPermissions` and `wizardReadOnly`. Restored, all pass.
- `npx jest` in `src/solutions/RecordProcedures`: 11 suites, 220 tests, all pass (was 10 suites, 204 tests). `npm run build` succeeds (tsc-surface-gate: 0 errors in the page's own files, 98 pre-existing in shared libs).
- Publish size, measured against a fresh `origin/master` build (`e9208abc5e`). Both sides were built in fresh worktrees `C:\wt84gm` and `C:\wt84gb` with `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` (framework-dependent linux-x64, Release) and zipped with `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included. Each side has 191 files, 4 of them PDBs. Master: 42,034,801 bytes (42.03 MB). Branch: 42,033,318 bytes (42.03 MB). Delta: −1,483 bytes. The delta includes every difference between the ebilling work branch and master, not only 084g. It is far below +5 MB, and the total is below 55 MB.
- No package added or changed, so no new CVE.

### 084g fix round 1 (branch `task/ebilling-084g-r2`, from `47113da620` merged with work `b7a0c2b532`)

Reviews B1–B7 (correctness and client) and A1–A4 (authorization). Commits `404233b840` (BFF and its tests) and `cafb2fb240` (client). Status stays in progress; uac-r2 reviews next.

- **B1, decide per choice.** `plan.ts` `procedureChoiceNeeds` / `ruleChoiceNeeds` say, from the row and the choice only, whether a choice's plans remove this record's own override and whether they create a row. The panel maps the answers to `allowedSteps` (`create` = `assignOrExclude`, `remove` = `remove`, each only when both answers say `true`). A choice needing a kind that is not allowed is disabled, with its reason in place of its caption and without its fields. The wizard is read-only only when no offered choice that makes a call is left. `canChoose` also requires every planned call to be within the choice's needs. Tests (`WizardsAllowed.test.tsx`): "assignOrExclude true, remove false, on a Procedure with no own override: the one-call choices are enabled, and an Exclude saves with one call", "... on a rule with no own exclusion: Exclude here is enabled and saves with one call", "... every choice that removes the record's own override is disabled with the reason; with none left, read-only", "assignOrExclude false, remove true, on a Procedure with its own pin: Follow latest (a removal) is enabled; Pin and Exclude say why not", "... on a Procedure with nothing to remove: read-only, and each create choice says why". Must-fire: "over every fixture row and variant, every choice and every input, each plan is within what its choice needs" (over 100 Procedure rows and over 30 rule rows), "so, under any answers, a choice the answers allow never plans a call they do not allow", and the control "the scan sees a wrong answer". Mutation: making every choice need both kinds (the round-0 rule) fails 3 tests.
- **B2, keep the mode after a partial save.** In the partial branch the panel keeps the open wizard's `allowed` instead of judging the re-read answers. Test: "Pin -> Exclude: the removal succeeds, the create is refused; the re-read allows nothing; the partial-save message stays and Finish works again". Mutation: judging the re-read answers fails it. After B1 a re-read with only `remove:false` no longer flips this row (its Exclude then needs only a create), so the test's re-read allows nothing.
- **B3, fail-before evidence.** The 6 all-false denied rows and the U-4 test now assert that the flags' own record check ran (two record-rights calls on the core record), and each denied row asserts which privilege questions were asked. Corrected numbers are below.
- **B4 and A4, privilege questions at once.** `WritePermissionsForCallerAsync` asks the record check first; if it holds, it asks every other distinct check of the three flags concurrently (`Task.WhenAll`), each through `EvaluateCheckForCallerAsync` with its own verdict, so a fault turns off only the flags that declare it. The probe answers one privilege per question, so batching would need a new probe method; the questions are concurrent instead. Tests: "the privilege questions are asked at once ..." (all six in flight together), "the extra checks per read" (a caller without `Admin()` is now 1 + 6, not 1 + 2), and the new U-5 case "Create on rule set gets no answer" gives (true, true, false).
- **B5, U-3 test name.** It now says the flags are not cached, that in production the record check rides the write route's 60-second per-caller rights cache, and that privileges are uncached.
- **B6, client test gaps.** "› from a read-only row lands read-only on the next row", and the no-call table runs on the rule wizard too ("the rule wizard is read-only (negative) when ..."). These close coverage gaps and passed before. Because of B1, the table now lists the answers that allow no kind of call; one flag false is covered by B1's tests.
- **B7.** `readRulesView`: `rules: (body.rules`. Whitespace only, so it has no test.
- **A1, route guard per record right.** "each write route, with every privilege and subject Read held but the record short of Write+AppendTo, answers the uniform 404, and the matching flag on the same record is false". It covers 15 routes × (Read and Write; Read alone), 30 cases. Mutation: giving the matter assign route a `finance.read` record filter fails 2 of these and U-4; the round-0 behavioural guard stays green under it.
- **A2, a faulted snapshot.** `EvaluateCheckForCallerAsync` runs the evaluator on an `AuthorizationService` over the registered `IAccessDataSource`, through a pass-through decorator that notes a snapshot marked `Faulted`. Such a check is `Faulted`, logged at Warning. `CallerHoldsRightsAsync` and `EvaluateAsync` are unchanged. Test: the U-5 case "the record check answers a faulted snapshot" (the production shape: no rights, `Faulted = true`), on both lists. Before the fix it failed for want of the Warning.
- **A3.** The `RecordProcedureEndpoints` remarks now say that the flags are computed per request for this caller, that the record-rights answer comes from the per-caller 60-second cache the write route also uses, and that privileges are uncached.
- **Accepted, recorded only.** A flag can be true while the route still refuses: a non-GUID `oid` gets 401, and the service's owner refusal gets 409. Neither lets a write succeed (U-2). `ProcedureAssignmentEndpointsTests.cs` (089) is byte-unchanged.
- **R1 (re-check), read-only only when a call is denied.** `plan.ts` `noAllowedCall`: a wizard is read-only only when some offered choice makes a call and none of those is allowed. A row with no call-making choice, such as a rule changed on the parent (both choices refused by their own blockers), keeps the normal frame with full rights or none. No Procedure fixture has such a row, because Pin and Exclude always create. Tests (`WizardsAllowed.test.tsx`): "must-fire: full rights on a rule changed on the parent show the normal frame ...", "must-not-fire: the same row with nothing allowed is still the normal frame ...", "a row with a call-making choice and nothing allowed is still read-only", and the `noAllowedCall` unit test. Mutation: the round-1 expression fails the first two.
- **Known limit (uac-r2, K): concurrent checks share one request.** A comment at the `Task.WhenAll` in `WritePermissionsForCallerAsync` states the assumption. It holds only while the scoped services on the path (`IAccessDataSource` and its cache, `CallerRecordAccessProbe`, the `IAuthorizationRule` set) stay safe for concurrent use within a request, and nothing on the path writes `HttpContext.Items`. Otherwise the checks must be asked one at a time. The comment is the only change; there is no code change.
- **Naming.** The wizards take `allowed` (`{ remove, create }`), a prop; the source scan that nothing under `components/wizards/**` names the caller or permissions still passes. `model.wizardReadOnly` is replaced by `model.allowedSteps`.

**Round trips per read** (only when the list is returned): before, the record check and then up to 6 privilege questions one after another, so up to 7 sequential rounds. Now there are 2 rounds: the record check, then all 6 distinct privilege questions together. Each question is still the probe's own exchange, WhoAmI and `RetrieveUserSetOfPrivilegesByNames`. A caller without Write or AppendTo on the record has 1 round and no privilege question. The panel reads both lists at once, so a panel load is 2 rounds deep.

**Corrected fail-before numbers.**
- Round 0 said 62 tests "do not compile" without the new declarations. That was wrong. With the old `RecordProcedureEndpoints.cs`, which never fills the flags (the DTO default is all false), 31 of the 93 failed and 62 passed. The 62 were the 24 step-0 refusals, the unavailable-list test, 15 declaration guards, 15 behavioural route guards, the 6 all-false denied rows and the U-4 test.
- This round the file has 129 tests. Against the pre-084g `RecordProcedureEndpoints.cs` (`f823ac6489`), 44 fail and 85 pass. The 85 are the 24 step-0 refusals, the unavailable-list test, 15 declaration guards, 15 behavioural route guards and the 30 A1 guards. Each asserts a route's own answer or an absence, which holds whether or not the flags are filled. The 6 all-false denied rows and U-4 now fail before.
- Against round 0's server code (`47113da620` `ProcedureAssignmentEndpoints.cs` and `FinanceAuthorizationFilter.cs`), 13 fail: the 2 concurrency tests, the no-`Admin()` extra-checks case, the 8 denied rows that expect all six privilege questions, and the 2 faulted-snapshot cases.

**Verification.**
- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- BFF tests filtered to RecordWritePermissions, RecordProcedure, ProcedureAssignment, FinanceAuthorization, OperationAccessPolicyCompleteness, FinanceStepZero and RouteAuthorizationGuard: 851 passed, 0 failed. `RecordWritePermissionsContractTests` has 129 tests (was 93).
- `tests/Spaarke.ArchTests`: 2516 passed, 0 failed (includes RouteAuthorizationGuardTests and the EB-D-218 and 084e guards).
- `npx jest` in `src/solutions/RecordProcedures`: 12 suites, 243 tests, all pass after R1 (239 before R1; was 11 suites, 220). `npm run build` succeeds (tsc-surface-gate: 0 errors in the page's own files, 98 pre-existing in shared libs).
- No package and no file of real size, so publish size was not re-measured.

### 084g close (2026-11-13)

- **Re-check of fix round 1.** Server and client pass, except one F2 in the client (R1). A row where no choice makes a call (for example a rule changed on the parent) went read-only even for a user with full rights, and showed "You can view this, but not change it." Fixed in `b85f637784` with `noAllowedCall`: read-only only when some offered choice makes a call and none of those is allowed. Must-fire and must-not-fire tests are in `WizardsAllowed.test.tsx`, proven by mutation.
- **uac-r2 review** of `f823ac6489..767d682324` (all of src/server): **PASS**, U-1 to U-5 met. The Guid.Empty route value still denies with the no-target reason (unchanged behaviour). One known limit is recorded at the `Task.WhenAll` (`2dfa032667`): the concurrent checks share one request scope, which is safe while the scoped services on the path stay safe for concurrent use and nothing on the path writes `HttpContext.Items`. Otherwise ask the checks one at a time. A hint may over-promise (401 for a non-GUID oid, 409 for the owner refusal), never under-protect.
- **Merged** into `work/ebilling-r1` as `fd885aaffb`. The BFF part reaches dev with task 112 (ping uac-r2 for that deploy).
- **For task 112, ui-tests.**
  - "Read-only user": a user who can read the lists but may not write the record sees every choice disabled, "You can view this, but not change it.", and no Confirm.
  - A user with assign/exclude but without remove: on a row with no own override, Exclude, Follow and Pin are enabled; on a row with one, the choices that remove it are disabled with the reason.
  - A rule changed on the parent, opened by a billing admin: the normal frame, with each choice's own reason.

## 084c

Branch `task/ebilling-084c`, from `90da03b6ab`. Rigor FULL. Client and ArchTests only: no BFF change, no new route, no new package, no DI registration, no ledger change (the BFF publish size is unchanged).

### What was built

- **The flow.** Rule wizard: Rule, On this record, **New values**, Impact, Confirm. "Change for this record" is a third choice beside Run the published rule and Exclude here. "On this record" asks the firm scope (as Exclude does) and the reason. "New values" is a dynamic step the shell inserts while Change is chosen (`WizardFrame` `valuesStep`, through `IWizardShellHandle.addDynamicStep`) and removes when another choice is picked. Finish is "Save record rule"; the success screen carries the O-21 line and the server's `message` / `affectedText`.
- **One form per billing rule type** (`src/solutions/RecordProcedures/src/components/wizards/ruleForms/`): 13 `<RuleType>.form.ts` files (data only, one field per line), `types.ts` (the form model), `body.ts` (pure: `toFormValues`, `buildBody`, `sameBody`, `describeBody`, `serverFieldErrors`), `RuleValuesForm.tsx` (Fluent v9 Input, Dropdown, Checkbox, Textarea; no new package, no new shared component) and `index.ts` (`RULE_FORMS`, `formFor`).

  | Rule type | Fields (kind) | Change offered |
  |---|---|---|
  | ApprovedTimekeeper | none | no: "no values to change", Exclude instead |
  | ApprovedRate (rate card) | tolerancePercent (number) | no: rates come from the version's rate card; pin that version of the Procedure |
  | TimeIncrement | incrementHours | yes |
  | DailyHoursCap | maxHoursPerDay | yes |
  | NonBillableActivity | codes (code list) | yes |
  | ExpenseRule | nonReimbursableCodes (code list), maxAmountPerItem; at least one | yes |
  | InvoiceAge | maxAgeDays (whole number) | yes |
  | CoreRecordWindow | graceDays (whole number) | yes |
  | Duplicate | matchFields (set) | yes |
  | RequiredData | fields (set) | yes |
  | AiObservation | observation (choice), minConfidence | yes |
  | AddedFee | feeKind, allowance (choices); feeCodes, capAmount or capPercent when Capped; afterDays when After N days | yes, but "Not allowed" and "Allowed after N days" are listed disabled ("not available yet") with the server's reason |
  | Discount | discountPercent, tolerancePercent (optional) | yes |
  | SubmissionIntegrity (system) | no form | no ("no form for the rule type") |

  Any type the page has no form for (the read model's display words in old fixtures, a type added to the server later) offers no Change, with the reason.
- **Prefill.** The read model carries only `parameterSummary`, not the values, so the wizard reads the published rule's body from Rule Review's `GET /api/finance/rulesets/{ruleSetId}` (the proposal with the row's `policyId`) when Change is chosen. No server change. If the read fails, or the version is not the one in force, the step says "The published values could not be read (...). Enter the values below." and starts empty. The form shows "Published rule: X." under a field whose value differs, and "Start from the published values" restores them. Next waits until the body is complete and differs from the published one.
- **The call.** `changeRule` in `overridesApi.ts`: `POST .../rule-overrides/change` with `{ policyId, reason (trimmed), firmId?, ruleBody }`, `ruleBody` being the whole new body as JSON TEXT (the route's member), the type member included. The 201 is checked for `override.id` and `recordRule`. A rule that names one firm is changed for that firm (O-20, as Exclude). A rule with this record's own Exclude plans `[remove (own Exclude), changeRule]`: 089 keeps one row per record and rule, so a change over an Exclude is 409 `assignment.duplicate`.
- **A refusal.** `WriteFailure.fieldErrors` keeps the 400's `errors[{ field, message, reason }]`. `serverFieldErrors` puts a `ruleBody` error worded `/<member>: <message>` (RuleBodySchemaValidator) on the field it names, "Required properties [a, b]" on each, `firmId` on the firm field, and everything else (reason, consequence, template, a pointer to a member the form lacks) in an error bar on New values: nothing the server said is dropped. The error bar at Confirm says "Go back to New values"; an edit of any field clears the server's messages. A refusal re-reads nothing (as 084b); a partial save (the removal done, the change refused) re-reads, as 084b.
- **Permission mapping** (084g). Change needs the `change` flag; when it also removes the rule's own Exclude it needs `remove` too. `ChoiceNeeds` gains `change`; `allowedBlocker` names the one missing right (`NOT_ALLOWED_CHANGE`), or the "view only" words when more than one is missing. The choice is disabled with the reason, as 084g's others.

### Decisions and deviations

1. **400, not 422.** The POML says a 422 refusal. Task 089's change route answers 400 `ruleoverride.invalid`-style ProblemDetails with `errors[]` (and 409 for a duplicate). The client reads the field errors from the body whatever the 4xx status; the test uses 400.
2. **"Rate card" = the ApprovedRate type.** The POML's "rate cards offer no Change" is read as the rule type whose rates come from the Procedure Version's rate card (ApprovedRate). ApprovedTimekeeper (no parameters) is also not changeable. If the owner meant another set of rules, it is a one-line change in the two form files (`noChange`).
3. **Re-changing a changed rule is not offered.** The read model's `Changed` mark names the record rule's `policyId` but not the BASE rule's, and the change route takes the base rule's policy id. A rule already changed here is therefore "Run the published rule first, then change it again" (its choice is disabled with that reason). Offering the direct re-change (and the stale-rule refresh the POML's cost-of-doing-nothing mentions) needs the base policy id on the `Changed` mark (`basePolicyId`). That is a read-model addition in 084/084e code, outside this task; filed below.
4. **Prefill needs Admin() on the rule-set read.** `GET /api/finance/rulesets/{id}` is declared `Admin()`; the change route's privileges are CreateRuleSet, DraftRules, EditRule and Write on policy. A caller who may change but not read rule sets sees the "could not be read" fallback and enters the values. Check at 112 with the intended billing-admin role.
5. **Bounds are the schema's, as a convenience.** The page checks the bounds so a user is told early; the server's answer is the authority. The ArchTest pins the page's bounds to the schemas.
6. **Copied, not imported, from RuleReview.** `src/solutions/RuleReview/src/services/ruleTypes.ts` was the model for the field catalogue and the body builder; RuleReview is a separate package, so the forms are new here, not imported (no cross-solution import).
7. **Process note.** One bare `python` call hung and was stopped (no process remains); everything else used node scripts.

### Acceptance criteria, mapped to tests

1. **Every billing RuleType has exactly one form; the agreement test passes; a seeded extra server property fails it, then is removed.** `RecordProceduresRuleFormAgreementTests` (ArchTests, 39 tests): "every authorable billing type has exactly one form file and one entry in the table; no other form file exists", "the system type ... has no form", "each form agrees with its server schema" (13 types: properties, requirements, bounds, integer, vocabularies, code pattern and limit, at-least-one / exactly-one), "a form with no fields says it is not changed; AddedFee refuses exactly the allowances the server refuses". Must-fire: "a drift in the server schema fails the agreement" (10 seeds, including an extra server property `newServerProperty`), "a drift in a form fails the agreement" (10 seeds), "an extra field line, a missing field line and a field split over two lines are caught", "the code pattern ... (must-fire on a changed pattern)". Each seed is applied to an in-memory copy; nothing on disk changed. The first run caught two gaps in the test itself (a `required` entry that is not a property, and a comment in `index.ts` naming the system type); both fixed and re-run green.
2. **A valid change sends 089's request with values, firm scope and reason, and re-reads both lists.** `WizardsChange.test.tsx`: "prefills the form from the published values, sends the whole new body as JSON text with the reason, and re-reads both lists", "a firm scope chosen on the first step is sent as firmId", "a rule excluded on this record is un-excluded first (preview, DELETE), then changed ...". `overridesApi.test.ts`: "change rule: POST rule-overrides/change with { policyId, reason, firmId?, ruleBody } where ruleBody is JSON TEXT, not an object".
3. **A refusal shows its field errors on the matching fields; nothing is re-read (negative).** `WizardsChange.test.tsx`: "a 400 puts each field error on its field, keeps the wizard open, and re-reads nothing; Back to New values shows them" (the message is on the input's `aria-describedby`, the input is `aria-invalid`; a general error shows in the bar; an edit clears it), "a 409 duplicate is shown with the hint ...; nothing is re-read". `ruleForms.test.ts`: "the server's refusal goes back on the fields it names" (3).
4. **Rate cards offer no Change; AddedFee's two refused modes are disabled with the reason (negative).** `WizardsChange.test.tsx`: "a rate card rule (ApprovedRate) offers no Change: the choice is disabled with the reason and the pin hint", "a rule with no values (ApprovedTimekeeper) ...", "Not allowed and Allowed after N days are in the list but cannot be chosen; Capped can". `ruleForms.test.ts`: "AddedFee: only the allowance the server checks can be saved". `planChange.test.ts`: "who can be changed" (5).
5. **Build, ArchTests.** See Verification. The ui-tests run after 112 deploys.
6. **Edge cases beyond the contract (one line each):** Change disabled without the `change` flag (`WizardsChange` "without the change right ..."; `WizardsAllowed` property tests now cover the `change` kind over every fixture row, with a real-type variant of each); a rule already changed here (P-3 and own change) is blocked; an own Exclude with no row id plans nothing; values equal to the published ones do not advance; out-of-range values are stopped by the page's own check (`WizardsChange` "the page's own checks ...", `ruleForms.test.ts` bounds table, 20 cases); unreadable published values fall back to an empty form.

### Verification

- `npx jest` in `src/solutions/RecordProcedures`: 15 suites, 322 tests, all pass (was 12 suites, 243 tests). New: `ruleForms.test.ts` (46), `planChange.test.ts` (16), `WizardsChange.test.tsx` (13), plus additions in `overridesApi.test.ts`, `WizardsAllowed.test.tsx`, `WizardsReadOnly.test.tsx`, `Wizards.test.tsx`. Updated for the new shapes: the `AllowedSteps` / `ChoiceNeeds` / `WriteFailure` literals; the negative "no Change choice / no change route" assertions of 084b became "Change is disabled with the reason on a type without a form" and "no dry-run read".
- `npm run build`: succeeds (check-html-css-reset; tsc-surface-gate 0 errors in the page's own files, 98 pre-existing in shared libs; vite build). No package added.
- `dotnet test tests/Spaarke.ArchTests`: 2555 passed, 0 failed (includes the new `RecordProceduresRuleFormAgreementTests`, 39 tests).
- **Fail-before, by mutation** (the feature did not exist before, so the new files do not compile against the old code; each key behaviour was also broken on the new code, the new and affected suites run, then restored):

  | Mutation | Tests failing |
  |---|---|
  | A change over an own Exclude no longer removes it first | 4 |
  | A refusal is not told to the wizard (no field errors on fields) | 1 |
  | ApprovedRate no longer says no-change | 5 |
  | A refused AddedFee allowance is accepted | 1 |
  | Change no longer needs the `change` right | 11 |
  | `ruleBody` sent as an object, not text | 3 |
  | Published values not prefilled | 2 |

### For task 112 (ui-tests after deploy)

- "Change a limit": on a matter, open a daily-hours rule, choose Change for this record, set a value, a firm scope and a reason, confirm. Expect the success screen with the O-21 line, then the Changed badge with the new value after the lists re-read. Needs a caller with the change right and, for the prefill, the Rule Review read (decision 4).
- "Server refusal": the page mirrors the schema's bounds (the ArchTest pins them), so an out-of-range value such as 24.5 hours is stopped on the field before anything is sent, with the same wording style. A refusal only the server can give: change the same rule from a second tab after saving in the first (409, shown in the bar), or use a firm id the server does not know. The field-level display of a server 400 is covered by the jest test, not reachable from the UI by design.
- "Rate card": open an approved-rate rule: Change is disabled with the rate-card reason and the pin hint.
- "Dark Mode Compliance (ADR-021)": switch to the dark theme and repeat "Change a limit". No colour literal exists in the wizard or form files (a scan in `Wizards.test.tsx` covers `ruleForms/`).
- A rule with the record's own Exclude: choose Change; Impact shows the removal preview, Confirm lists two calls, and the result is a Changed rule.

### Filed for the owner (not done here)

- Add `basePolicyId` (the policy the Exclude/Changed row was made on) to the `Changed` mark of the rules-in-force read, so a rule already changed here can be changed again directly and the stale-rule refresh gets a surface (decision 3). Read-model change in 084/084e code.
- The POML says 422; the route answers 400 (decision 1): reword the POML criterion.

### 084c fix round 1 (branch `task/ebilling-084c-r2`, from `3a94a37fc9` merged with work `4e448148e2`)

The merge was clean: only this notes file had changes on both sides, and git merged it automatically. Commits: `2fdcf95804` (server read model), `f8827c731d` (client), `7dc61446ca` (ArchTest), and this notes/POML commit. The server change is limited to `RecordProcedureReadService` and its DTOs, plus tests. Nothing in `Services/Signals/**`, 089's write routes or `ProcedureAssignmentService` was changed. No package was added.

**Read-model fields added** (rules-in-force read). Each is asked of the caller through EB-D-218's single batched check (`Reading.Expose`):

| Field | What it is | What it exposes |
|---|---|---|
| `RuleInForceView.ruleBody` | The row's rule body as a JSON object, or null if the body is not an object | The row's own policy version (`Expose(PolicyVersionSet, version)`), on every rules-in-force row, Excluded rows included |
| `RuleInForceView.consequence` | `Reject` / `Flag` / `SuggestAdjustment` (the `BillingConsequence` name), or null | Nothing new: it comes from the same version `ruleBody` asks for |
| `RuleMarkView.basePolicyId` (Changed mark) | The base rule's policy, which the change route takes | In force: `Expose(PolicySet, base)`; the returned id is the one sent, so it cannot be sent unasked. Base retired: already exposed, because the code is read from it |
| `RuleMarkView.baseRuleBody` (Changed mark, in force) | The base rule's body in force | The base version in force (`Expose(PolicyVersionSet, baseVersion)`). That version was already exposed through `currentBase` and the summary, so the proof is a value test plus a deny-alone test, not a guard mutation |

The base-retired Changed mark has no `baseRuleBody`, because nothing is published in force. All three EB-D-218 guards stay green: `EveryAskedRow_DeniedAlone_Is503_WithNoList`, `EveryVersionRow_IsAsked_OrExemptBecauseTheBodyDoesNotDependOnIt` and `Every200Guid_WasAskedOfTheCaller`. A new guard seed, "change prefill", covers them. Its exempt rows: four procedures-list version rows (the list shows no rule) and EXPENSE v1 on rules-in-force (superseded, so the body does not depend on it). Its integrity row is HOURS v1 on procedures (RequireBasedOn).

**Items:**

1. **F1-1. Re-change a changed rule.** The Changed mark now carries `basePolicyId`. Change plans `[remove (this record's change row), changeRule (policyId: basePolicyId)]`. The form starts from this record's current values, and "Start from the published values" loads `baseRuleBody`. A mark without the base rule or its row id (an older BFF) is blocked with `RECHANGE_NOT_NAMED`. The firm scope stays the record rule's own (`firmScope`).
   - Server tests: `ChangedMark_NamesItsBaseRule_WhichIsAsked` (in force and base retired).
   - Client tests: `planChange` "a rule changed here is changed again" (5), and `WizardsChange` "F1-1 ... Changed" and "Changed and Stale".
   - Mutation, server: when the base id was not exposed, `Every200Guid` failed twice, the "changed: base rule" deny test failed, and `ChangedMark` (in force) failed.
   - Mutation, client: when the change targeted the row's policy, 3 tests failed. When a changed row was blocked again, 5 failed. When the published body was the row's own, 2 failed.
2. **F1-2. Prefill from the read model.** Rows carry `ruleBody`, and the client's `GET /api/finance/rulesets/{id}` is removed (`overridesApi` "the client has no read of a rule set"). With no body, the New values step says why (`NO_VALUES_TEXT`), shows no form, and keeps Next disabled.
   - Server tests: `RulesInForce_CarriesEachRowsBody_AndTheBaseBodyInForce`, `ExcludedRule_CarriesItsBody`, `ABodyThatIsNotAnObject_IsNull`, and `TheVersionABodyIsReadFrom_DeniedAlone_Is503` (3 versions).
   - Client tests: `WizardsChange` "ExpenseRule with two codes and a cap is prefilled from the row" and "F1-2 negative".
   - Mutation: with no prefill, 17 tests failed. With `ruleBody` null on the server, 2 failed. With the base body taken from `basedOn`, 1 failed.
3. **F1-3. Multi fields compare as sets.** `sameBody(a, b, def)` and `fieldDiffers` compare multi keys as sets. `buildBody` keeps the given order without duplicates, so the published order is kept.
   - Tests: `ruleForms` "with the form, a multi-choice field compares as a set", and `WizardsChange` "an unchanged Duplicate whose published order is not the vocabulary's keeps Next off".
   - Mutation: with ordered comparison, 2 tests failed. With vocabulary order in `buildBody`, 2 failed.
4. **F3-4. Partial save.** The "Go back to New values" hint is dropped when the removal succeeded but the change was refused (`refusalHint(failure, partial)`).
   - Test: `WizardsChange` "own Exclude: preview, DELETE accepted, change refused 400". It covers the partial message with no "go back", the lists re-read, and Finish held.
   - Mutation: 1 test failed.
5. **F1-7. Base retired first.** `changeBlocker` checks `isBaseRetired` before the form, so a base-retired row (whose type is null) gets `BASE_RETIRED_NO_CHANGE` rather than "no form".
   - Test: `planChange` "F1-7".
   - Mutation: 1 test failed.
6. **Item 5. Conditional fields and refusal words.**
   - The agreement ArchTest reads each `showWhen { key, equals }` and compares it with the schema's `allOf[].if.properties.<key>.const`. The fields a branch shows must be exactly its `then.required` plus its single-member `then.oneOf` entries, and a showWhen the parser cannot read fails.
   - There are six must-fire seeds: three in the schema and three in the form. All six fail with the check disabled.
   - "the page's tests use the validator's real refusal words" runs `RuleBodySchemaValidator`. The real shapes are `/maxHoursPerDay: 30 should be at most 24` and `: Required properties ["observation","minConfidence"] are not present`. The test asserts that the jest files use exactly these, and the hand-written jest messages were replaced with them.
   - ArchTests for this class: 46 (was 39).
7. **Item 6. Duplicate with Reject.** The form knows the rule's consequence from `consequence`. A Duplicate that Rejects without Firm invoice number is refused on the field before anything is sent (`DUPLICATE_REJECT_NEEDS_INVOICE_NUMBER`, the server's own rule).
   - Tests: `ruleForms` "item 6", and `WizardsChange` "item 6" (Reject is refused; Flag is not).
   - Mutation: 2 tests failed.
8. **Item 8. Rate-card owner question.** Not changed; still open for the owner (see below).
9. **Item 9. AddedFee precedence key.** `feeKind` is read-only, and so is `feeCodes` when the kind is `other`. The reason shown is `FEE_KIND_LOCKED`. The server does not refuse a key change (`ChangeRuleCoreAsync` has no precedence-key check), so the page holds this line.
   - Tests: `ruleForms` "item 9", and `WizardsChange` "item 9" (lateFee and other).
   - Mutation: 3 tests failed.
10. **Item 10. Notes and POML.**
    - (a) Decision 4's premise was wrong. The rule-set read's privileges are a subset of the change route's, so a caller who may change a rule could always read it. The "could not be read" fallback was reachable only through a draft or a fault, never through missing rights. The point is now moot: the prefill no longer reads rule sets (F1-2).
    - (b) The POML criterion now says 400, not 422. The POML stays `completed`.
    - (c) Fixture note: the shared fixtures (`__tests__/fixtures.ts`, from 084a) use display words for `ruleType` ("Daily hours limit", "Expense cap (meals)"), but the server sends the enum name (`DailyHoursCap`). Those rows have no form, so Change is disabled. That is correct for the fixture but not realistic. The 084c tests use enum names.
11. **Found while testing.** All the checkboxes of a multi field took the Field's single control id, so none was named by its own label (duplicate ids). `CheckGroup` now gives the group the Field's label, hint and message, and each checkbox keeps its own id and label.
    - Test: `WizardsChange` "each checkbox has its own id and is named by its own label".
    - Mutation: 4 tests failed.

**Verification:**
- `npx jest` in RecordProcedures: 15 suites, 340 tests, all pass (was 322).
- `npm run build`: succeeds. The surface-owned type errors are 0; the 98 pre-existing errors are in shared libs.
- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- `dotnet test` filtered to RecordProcedure, RecordWritePermissions and ProcedureAssignment: 802 pass. Against the old service file, 4 of the new server tests fail.
- `dotnet test tests/Spaarke.ArchTests`: 2562 pass, 0 fail.

**Still open for the owner:**
- Item 8: confirm that "rate cards offer no Change" means the ApprovedRate type (decision 2).
- Make the ruleType fixture words the enum names across 084a/084b (item 10c). This is test hygiene, with no user-visible effect.

### 084c close (2026-11-13)

- **uac-r2 review** of the read-model additions (`RuleBody`, `Consequence`, `BasePolicyId`, `BaseRuleBody`; `3a94a37fc9..f67e5c6acd`): **PASS**. Each field reaches the answer only through `Reading.Expose`. Accepted K: because the base version row is now always asked, a caller who can Read the shown rule but not its base version gets 503 (EB-D-218: a row denied alone gives 503). The DeniedAlone tests pin this.
- **Scoped re-check of the client fix diff:** no F-class findings. Known limits recorded:
  - **K1.** The `WizardsAllowed` property test has no Changed variant carrying `basePolicyId`. `planChange.test.ts` covers `planWithin` for the re-change plan directly. Add the variant when next touching that file.
  - **K2.** A re-change keeps the firm of the earlier change: the firm field is fixed to the record rule's `firmScope`. To re-change for all firms or another firm, run the published rule first, then change.
  - **K3.** The Duplicate/Reject pre-check reads the record rule's consequence. A re-change goes to the base rule, whose consequence can differ after a Stale refresh. The server then refuses, and the message shows in the general bar; nothing is saved silently.
  - **K4.** `testKit.ts` still classifies a `ruleset` route that the client no longer calls. Dead test code; remove it when next touching the file.
- **Merged** into `work/ebilling-r1` as `a74278e22c`. The BFF part (read-model fields) reaches dev with task 112.
- **Item 8 decided (EB-D-222, owner 2026-11-13):** keep as built. Only ApprovedRate refuses Change (ApprovedTimekeeper has no values); may be revisited at UAT.

## 084d-1

Planning without writes. Server only; there is no route, DI registration or client change. Branch `task/084d-1`, from `549c155ab2`. This is slice 1 of the approved 084d split: 084d-2 adds the dry-run read and the route, 084d-3 the parity, no-write and concurrency tests, and 084d-4 the client.

**Placement.** Everything is in `Services/Finance/Procedures`. Nothing under `Services/Signals` changes: the resolver is reused as-is through its `IGenericEntityService` row source, so there is no Signals diff. Nothing is registered in DI. Both new types are `internal` and built per request.

**What was built:**
- `WriteRefusingEntityService`: the planning service (F1). Reads pass through. All nine write members are explicit interface implementations that throw before anything is sent: create, upsert, update, update-if-row-version-matches, bulk update, bulk update-if-row-versions-match, transaction, delete and associate. A subclass therefore cannot override them. The constructor refuses an inner service that already refuses writes.
- `ProcedureRowOverlay`: the dry run's row source over the four override tables (F4). It derives from the planning service, so it inherits the write refusals.
- `ProcedureOverridePlan`: what a proposed override would do. It holds the after-images, the ids it creates and the ids it deletes.
- `Plan*Async` on `ProcedureAssignmentService`: `PlanAssignProcedureAsync`, `PlanExcludeRuleAsync`, `PlanChangeRuleAsync` and `PlanRemoveAsync`. Each takes the planning service as a `WriteRefusingEntityService` parameter. The write's validation was extracted into shared helpers (`PlanAssignmentRowAsync`, `PlanExcludeRowAsync`, `PlanChangeBasisAsync`), and the reads now take the service they read through. The write and the plan run the same checks.
- The four row builders take `imageOnly`. In a dry run they return the row as built, before the owner question, so no owner is asked.
- The change plan stops before each send. Stage 1 checks the record's set against the image (`IsTheRecordSet`), and stage 2 checks the policy and its versions (`IsReusableRecordRule`, `RecordRuleVersionsAsync`) the way the write does. The policy image is built with `sprk_enabled = false`, then the stage 3 switch-on is applied to it (§2).
- A read fault while planning returns 503 `assignment.read_failed`. Nothing is written.

**§11 justification:**
- `WriteRefusingEntityService`:
  - *Existing:* there is no read-only `IGenericEntityService`; only test doubles refuse writes.
  - *Extension:* every existing implementation writes by design, so a refusing decorator is the only way to give planning a service it cannot write through.
  - *Cost of doing nothing:* no-write would rest on reading the code, and the change path's stage 1 and 2 sends would be one refactor away from running in a preview.
- `ProcedureRowOverlay`:
  - *Existing:* the resolver reads only through `IGenericEntityService` and has no other row source.
  - *Extension:* a typed overlay on the resolver's rows would need rows its first read never loads, and the readers of those rows are private to the resolver. The row-source seam is the one place that needs no Signals change.
  - *Cost of doing nothing:* an admin confirms an override without seeing which rules it changes, or a second resolver drifts from the one that judges invoices.
- `ProcedureOverridePlan`:
  - *Existing:* the write's outcome types (`OverrideView`, `RuleChangedView`, `OverrideRemoved`) describe a written row, not rows to be written.
  - *Extension:* adding images to those types would put unwritten state into answers the routes already return.
  - *Cost of doing nothing:* the overlay has nothing exact to apply, and 084d-2's read cannot show "if saved now".
- `Plan*Async`:
  - *Existing:* the write methods themselves.
  - *Extension:* they are extended by extracting their validation into shared helpers. A plan and a write therefore run one set of checks, not two copies.
  - *Cost of doing nothing:* a dry run that re-implements the checks drifts from the write, and its refusals stop matching the write's.

**The overlay's rules (F4).** A query on the four tables decides which after-images it gets from one exact top-level AND condition. It never evaluates the criteria:
- A primary-key `Equal`/`In` condition gives the images with those ids. This form is added beside F4's chunk rule, because a later step reads a planned policy or version by id.
- Otherwise, a policy query matches its `sprk_ruleset` condition and a version query its `sprk_policy` condition (the resolver's chunks). The image goes only to the chunk that holds its parent.
- A policy or version query with neither condition, with two of them, or under an Or filter throws before it reads.
- On every page, rows that are deleted or replaced (any id with an after-image) are dropped. Images are added once, on the last page, cut to the query's columns, as copies.
- A created image gets `statecode` 0 and the request's one created-on stamp (UTC) when it lacks them. A real row's image stays as read, with the update applied.
- On the four tables, by-id, alternate-key and FetchXML reads throw, and so does FetchXML that cannot be parsed. Other tables pass through.

**Known limits:**
- **K1.** On the assignment and rule-set tables, a query without a primary-key condition gets every image of the table, and the resolver's own per-row re-checks decide membership. The resolver re-checks every row it reads from those two tables after its query: an assignment row by state and chain record, a Procedure's set by its Procedure being in the chunk, and a record set by scope, firm, one home and chain record. An image outside a query's own filter is therefore dropped there, not counted. The resolver-level test covers policy and version images; a set or assignment image through the real resolver is 084d-3's parity test.
- **K2.** The IL no-write guard scans the two dry-run types only. The `Plan*Async` methods share `ProcedureAssignmentService` with the write path, so an IL scan of that type cannot tell them apart. Their no-write rests on the `WriteRefusingEntityService` parameter type, and on contract tests asserting zero writes, transactions, owner questions and closes for every plan, including every refusal.
- **K3.** The image-versus-write comparison skips the set's effective-from date. The builder stamps it with the build day, and the plan and the write are built at different instants of the same test.

**Tests:**
- `tests/integration/contract/Api/Finance/ProcedureDryRunPlanningContractTests.cs` (51):
  - every seam member is classified as a read or a write, as a ratchet;
  - each write throws on both types, and a strict inner service receives nothing;
  - the change plan writes, sends, asks and closes nothing, and its images are the rows the real change then creates;
  - a re-change over an Off record rule patches the real policy On, with version 2;
  - the remove, assign and exclude plans;
  - six refusals give the write's own status and code;
  - a read fault gives 503;
  - the overlay's F4 rules;
  - the REAL resolver over 600+ procedures (more than one chunk of policies and of versions) reads each image exactly once and drops a replaced policy.
- `tests/Spaarke.ArchTests/DryRunNoWriteGuardTests.cs` (8): the IL of the two dry-run types references no write member of the seam, and nothing of the Signal writer, the Decision Record writer, the owner question or Signal closure. It has five must-fire controls, one must-not-fire control and a non-vacuity check.
- 089's tests (`ProcedureAssignmentServiceTests`, `ProcedureAssignmentEndpointsTests`) are unchanged and green.

**Mutation seeds.** Each was applied, run and restored:
- S1: drop the chunk check.
- S2: stop dropping replaced ids.
- S3: pass the row-version update through.
- S4: pass by-id reads through on the four tables.
- S5: planning builds the set with `imageOnly: false`.
- S6: the guard forgets the transaction member.

Each seed fails at least one test: S1 3 (unplaceable-query cases), S2 2 (the replaced-row test and the real-resolver test), S3 2 contract + 1 IL guard, S4 2 (by-id reads), S5 1 (the change plan's no-owner check), S6 1 (the lambda transaction control).

**Publish size.** N/A for this slice: no package, no DI registration and no route; two internal types and internal methods only. 084d-2 adds the route and measures the delta against a fresh master build.

## 084d-2a

The dry run's access, read and routes. Branch `task/084d-2a`, from `3a9130b7be`. 084d-2b adds `closes`, `affectedOpenInvoices` and firm `reach`; they are left out of the response, not stubbed.

**Placement.** The routes sit in `Api/Finance/RecordProcedureEndpoints.cs` beside each core's other Procedure reads, on the same group (step 0, `RequireInternalFinanceCaller`). The access declarations sit in `Api/Finance/ProcedureAssignmentEndpoints.cs` beside the write routes' own, so one file holds every check a write or its dry run asks. The read is `RecordProcedureReadService.DryRunAsync`, beside the two reads it composes. No DI registration changes: the planner is taken by the handler with `[FromServices]`. No package.

**What was built:**
- `AssignChecks`, `ExcludeChecks`, `ChangeChecks` and `RemoveChecks`: the write routes' subject and privilege declarations, single-sourced. `AssignTargets`, `ExcludeTargets`, `ChangeTargets` and `RemoveTargets` now delegate to them, unchanged in behaviour.
- `DryRunRecordChecks` / `DryRunRecordTargets`: `finance.read` on the record, then `RecordWriteCheck` (uniform 404). `DryRunTargets` / `DryRunChecks`: the union, in plan order, of the `*Checks` of every write the dry run plans; a replaced override (`replacesOverrideId`) adds `RemoveChecks`. A missing kind or id is the write's 400.
- `RefuseUnlessCallerReadsOverridesSubjectAsync`: the removal routes' stored-row subject Read, extracted so the write filter and the dry-run filter (`RequireReadOnTheDryRunsOverridesSubject`) share it.
- `RefusalOf`: `Map` for callers in other files (the route guard reads any `.Map(` member call as an unscannable registration).
- Three `POST .../procedures/dry-run` routes in the write routes' filter order: record (404) → subject and privileges (403) → kill switch → the removed or replaced override's subject Read → 401 → service. A planning refusal is the write's own answer.
- `DryRunAsync`: plans the proposal through the 084d-1 planning service (a removal first, then the create over an overlay of it), loads the created ids app-only by id, reads Now and After (After over `ProcedureRowOverlay`), exempts only the created ids that do not exist, exposes a request-named firm, and runs ONE caller check over Now ∪ After. Any refusal or fault is 503 `procedures.unavailable`.
- DTOs: `ProcedureDryRunRequest`, `ProcedureDryRunView`, `ProcedureDryRunProposalView`, `ProcedureDryRunSideView`, `ProcedureDryRunDisclosureView`.

**Response shape.** `{ record, date, firm, firmSource, proposal { kind, overrideKind, replacesOverrideId, overrideId, proposedRowIds }, now { procedures, rules }, after { procedures, rules }, afterLabel: "If saved now", disclosure { predictsOwnerRefusal: false, readAtOneInstant: false, requestFirmAsked, notes[3] } }`. The sides' permission flags are all false: the dry run is not where a write is offered.

**§11 justification:**
- `ProcedureDryRunRequest` and the view records:
  - *Existing:* the write requests and `RecordProceduresView` / `RecordRulesInForceView`.
  - *Extension:* the request wraps the three write requests unchanged; the view holds the two existing views twice. Only the proposal and disclosure records are new.
  - *Cost of doing nothing:* there is no way to send a proposal without writing it, and the client cannot tell Now from After or show the disclaimers.
- `DryRunAsync`:
  - *Existing:* the two record reads (`GetProceduresAsync`, the rules-in-force read) and the 084d-1 plans.
  - *Extension:* it is built from the same `ReadAsync` with a row-source parameter; a second resolver or read path is avoided.
  - *Cost of doing nothing:* 084d-1's overlay has no reader, and the admin confirms an override blind.
- `DryRunTargets` / `*Checks` / `DryRunRecordChecks`:
  - *Existing:* the write routes' `*Targets`.
  - *Extension:* the `*Targets` bodies became the `*Checks`, so the write and the dry run share one declaration each.
  - *Cost of doing nothing:* a dry run with its own list drifts from the write's, so a caller sees rules a write would refuse them.
- `RequireReadOnTheDryRunsOverridesSubject`:
  - *Existing:* `RequireReadOnTheOverridesSubject` on the removal routes.
  - *Extension:* its body became the shared helper; the new filter only reads the id from the body instead of the route.
  - *Cost of doing nothing:* a dry-run removal of an override whose subject the caller cannot Read would answer with that subject's rules.
- The three routes:
  - *Existing:* none answers "if saved now".
  - *Extension:* a query flag on a write route would put a read behind write semantics, and the write routes return the written row.
  - *Cost of doing nothing:* 084d-4 has nothing to call.

**Build conditions → code → tests:**
- **uac-r2 C1** (the synthetic set is decided by an app-only by-id load; a fault is 503 and nothing is exempted): `RecordProcedureReadService.cs` `SyntheticRowsAsync` (≈562), called at ≈455 before any `Exempt` (≈474). Test: `SyntheticLoadFault_Is503_AndNothingIsExempted`.
- **uac-r2 C2** (only the exact synthetic rows are exempt; a created id that exists is real and asked): `Reading.Exempt` / `Expose` (≈651-657), `SyntheticRowsAsync` drops found ids. Tests: `Assign_NowWithout_AfterWith_OneCheck_NothingWritten`, `ExcludeRule_ExemptsExactlyTheSyntheticRow`, `Switch_TheRecreatedIdIsReal_Asked_AndHasNoCreatorAfter` (deny that row alone → 503).
- **uac-r2 C3** (ONE combined check over Now ∪ After): `DryRunAsync` ≈488-490. Tests: the Assign theory (`Batches` single), the dry-run `Every200Guid` guard.
- **uac-r2 C4** (the dry run asks what the writes it plans ask, from one declaration): `ProcedureAssignmentEndpoints.cs` `*Checks` (393-411), `DryRunChecks` (465). Tests: `EachDryRun_DeclaresExactlyThePlannedWritesChecks`, `DryRunGuard_FiresWhenAReplacementDropsTheRemovalsChecks`, `EachDryRunRoute_AsksTheCaller_ExactlyItsDeclaredChecks`.
- **uac-r2 C5** (the write routes' filter order; uniform 404 before any 403): `RecordProcedureEndpoints.cs` 75-140. Tests: `RecordFilter_IsTheUniform404_BeforeAny403`, `SubjectAndPrivilegeFilter_Is403`, `FlagOff_Is503Disabled`, `UnreadableOverrideSubject_IsTheWritesNotFound`, `StepZero_RefusesFirst`.
- **ontology-r1 F3** (a proposed row has no creator; no person exposed for it): `ReadAsync` `proposedRowIds` (≈729). Tests: `ExcludeRule_ExemptsExactlyTheSyntheticRow` (stamp `byId` null), the switch test (After null, Now the original creator).
- **R5 / R10** (no owner prediction; not one instant): disclosure flags and notes. Test: the Assign theory.
- **R6** (a request-named firm is asked as a query firm): `DryRunAsync` ≈482-486. Test: `RequestFirm_IsAskedAsAQueryFirm`.
- **Label** ("If saved now"; nothing presents After as a promise): `AfterLabel`. Test: the Assign theory.

**Known limits:**
- **K1.** `reach`, `closes` and `affectedOpenInvoices` are 084d-2b's.
- **K2.** Change-kind HTTP tests are not added here: change planning is covered by the 084d-1 tests, and the change parity is 084d-3's. The change routes are covered by the two declaration guards.
- **K3.** Both sides run the future-date probe, so a Procedure that opens after the date can make the dry run 503 when its overlap does, exactly as the record read does.
- **K4.** In a switch, Now still shows the replaced row's creator (it is real and asked). F3 removes the creator from After only; the test asserts both.
- **K5.** The sides' permission flags are all false. The client offers writes from the record read, not from the dry run.

**Tests:**
- `tests/integration/contract/Api/Finance/RecordProcedureDryRunContractTests.cs` (34): the HTTP cases above, plus the dry-run `Every200Guid_WasAskedOfTheCaller_DryRun` guard (four seeds, with a planted-GUID must-fire control) and the dry-run version-row guard (four seeds).
- `RecordWritePermissionsContractTests.cs` (+36): `EachDryRun_DeclaresExactlyThePlannedWritesChecks` (21 cases, four must-fire controls each), `DryRunGuard_FiresWhenAReplacementDropsTheRemovalsChecks`, and `EachDryRunRoute_AsksTheCaller_ExactlyItsDeclaredChecks` (21 routes × kinds, eBilling off).
- `RouteAuthorizationGuardTests.Ledger.cs`: the `RequireReadOnTheDryRunsOverridesSubject` credited form; the RecordProcedureEndpoints entry names the dry-run filters.

**Mutation seeds.** Each was applied, built, run against the two test classes and restored:
- S1 drop the R6 firm exposure: 2 fail (the R6 theory).
- S2 exempt every created row without the found check: 2 fail (the switch test, the dry-run Every200Guid guard).
- S3 revert F3: 1 fails (the switch test).
- S4 swallow the by-id fault and exempt everything created: 1 fails (the condition 1 test). The first version of that test faulted every by-id read of the id, so the later stamp read still produced a 503 and S4 survived; the fault now hits only the synthetic load.
- S5 drop `RemoveChecks` for a replacement: 6 fail (declaration equality, the replacing cases).
- S6 swap the record and 403 filters (projects route): 4 fail (the uniform-404 theory, step 0).
- S7 check After only: 3 fail (the removal test, the dry-run Every200Guid guard).

**Gates.** BFF build 0 warnings, 0 errors. ArchTests 2570/2570. Filtered `Sprk.Bff.Api.Tests` (Finance, Procedure, RecordProcedure, RouteAuthorization) 8233 passed, 4 skipped, 0 failed. `dotnet format whitespace --verify-no-changes` clean on the touched files.

**Publish size** (fresh `origin/master` `2a4b86272d` vs this branch, `dotnet publish -c Release -r linux-x64`, `Compress-Archive -CompressionLevel Optimal`, 191 files each): master 42,072,199 bytes, branch 42,065,181 bytes, delta −7,018 bytes (the branch base lags master; the change itself adds no package and no assembly).


### 084d-2a fix round 1 (branch `task/084d-2a-fix1`, from `224007eda7`)

Fix list: the uac-r2 conditional pass and the Opus adversarial pass. Each finding, the change, the test that fails without it.

- **F-1** (the Change kind was never tested through the dry-run routes; the rig could not run it).
  - *Change:* `RecordProcedureRig` now builds a REAL `RuleSetService` over its own validator (`RuleSets`); the test planner takes it instead of an uninitialised object, so `ValidateRule` no longer throws into a 503. New rig helpers: `Changeable(code)` (a Procedure rule in a Published set with a name and a current version) and `rowVersion` on `AddRule` / `ChangeRuleRows` (a stored policy carries a row version; a plan that updates it needs one).
  - *Test:* the `DryRunSeeds` for both guards (`Every200Guid_WasAskedOfTheCaller_DryRun`, `EveryVersionRow_IsAsked_OrTheBodyDoesNotDependOnIt_DryRun`) gained `change` and `re-change` (a Change replacing this record's own Change of the same rule). Each asserts a 200, so the guards reach the exposure path. The must-fire control exempts the seed's real base policy version and the verdict reports it.
- **F-2** (the removed or replaced override's id and kind reached a 200 unasked; a Follow row of a Retired Procedure sits in no view).
  - *Change:* `RecordProcedureReadService.DryRunAsync` exposes `(sprk_procedureassignments, overrideId | replacesOverrideId)` on the Now reading before the combined check. It is a real row, never exempt.
  - *Test:* `TheNamedOverride_IsAsked_EvenWhenNoListShowsIt` (remove and replace). The premise is asserted (the id appears in no list), the row is asked, and denying that row alone is 503.
- **K-3** (the overlay's primary-key match ignored the query's other conditions).
  - *Change:* `ProcedureRowOverlay.ImagesFor`. The key path answers a query only when the key is the sole top-level condition. With other conditions beside the key, the query throws when the key names an after-image (the overlay never evaluates criteria, so the image would be returned unjudged). A key that names no image reads on, because nothing is returned unjudged; the planning reads of the base rule (a key plus status conditions, over the removal's overlay of a different row) depend on that, and a flat throw would make every re-change a 503. A key inside an Or, a nested filter or a non-Equal/In operator throws.
  - *Test:* `AKeyReadWithOtherConditions_ThrowsWhenTheKeyNamesAnImage`, `AKeyInsideAnOrOrANestedFilter_Throws` (`ProcedureDryRunPlanningContractTests`).
- **K-4** (linked entities were not checked).
  - *Change:* `RetrieveMultipleAsync(QueryExpression)` throws before reading when a link, at any depth, names one of the four tables, or when a query on one of the four carries any link. Table names are compared OrdinalIgnoreCase (`Tables` is now case-insensitive, and the canonical name keys the images).
  - *Test:* `AQueryThatLinksAnOverrideTable_Throws_BeforeReading` (four shapes, one in another case, one nested) and the must-not-fire `AQueryThatLinksOtherTables_PassesThrough`.
- **K-5** (the IL no-write scan left out the 2a entry point).
  - *Change:* `RecordProcedureReadService` is in `DryRunNoWriteGuardTests.DryRunTypes`. Adding it gave no false positive, so the whole type is scanned (no method-level narrowing was needed).
  - *Test:* `TheDryRunTypes_ReferenceNoWrite`; `TheScan_ReadsTheRealBodies` now also proves `DryRunAsync`, `PlanAsync` and `SyntheticRowsAsync` bodies (their async state machines) are read.
- **K-7** (the dry-run GUID guard was partly self-referential).
  - *Change (test only):* the allowed exempt set is `proposal.proposedRowIds` minus the ids that exist in any of the four override tables of the rig's world; `host.Exempted` is only compared against it, matched with its entity set (`ExemptionViolations`): each reported exempt pair must name an override set, must be absent from that set's table, and the exempt ids must equal the derived set.
  - *Test:* `Every200Guid_WasAskedOfTheCaller_DryRun`. Must-fire controls: a planted GUID, a real base version exempted, and a synthetic row asked (one exemption dropped).
- **uac-r2 K1** (comment): at the `afterReading.Exempt(...)` call, the exemption relies on `ReadAsync` never calling `Expose`; every exposure happens in `BuildProcedures` / `BuildRulesAsync`, after `Exempt`.

**Mutation seeds** (each applied, run against the named test class, restored):
- F-2: drop the exposure of the named override: 2 fail (`TheNamedOverride_IsAsked_EvenWhenNoListShowsIt`, remove and replace).
- K-3: take the key path whatever else the query holds: 1 fails (`AKeyReadWithOtherConditions_ThrowsWhenTheKeyNamesAnImage`).
- K-4: drop the link check: 4 fail (all four `AQueryThatLinksAnOverrideTable_Throws_BeforeReading` shapes).
- K-7: exempt every created row without the found check: 2 fail (the switch test, and the `switch` seed of the guard on `ExemptionViolations` itself, ahead of the `AlsoAsked` line).

**Known limit K-6.** The firm-existence oracle: planning's 400 "firm not found" for a request-named firm runs before R6's caller check on that firm, so a caller who may not Read a firm can still learn whether a firm id exists. It is the same as the write route's 400 versus 201, so the dry run adds no new oracle. Accepted.

**Gates.** BFF build 0 warnings, 0 errors. ArchTests 2570/2570. Filtered `Sprk.Bff.Api.Tests` (Finance, Procedure, RecordProcedure, RouteAuthorization) 8246 passed, 4 skipped, 0 failed (8233 before the round: +13). `dotnet format whitespace --verify-no-changes` clean on the touched files.

## 084d-2b

The dry run's impact: `closes` and `affectedOpenInvoices`. Branch `task/084d-2b`, from `475c05ed8e` (084d-2a plus its fix round 1). Firm `reach` is not built (see K-1).

**Placement.** Services/Finance/Procedures only:
- The selection and count members sit on `ProcedureAssignmentService`, beside the close they share a selection with.
- The resolution comparison sits on `RecordProcedureReadService`, beside the reader that already holds the overlay and the one combined check.
- The invoice-id overload sits on `AffectedInvoiceCounter`.

There are no new routes, DI registrations or packages, and nothing changes under `Services/Signals`. The routes are unchanged: the planner the handler already takes carries the closure and the counter.

**What was built:**
- `SelectBaseFindingsAsync`: the selection half of `SupersedeBaseFindingsAsync`, pulled out unchanged.
  - The write calls it with its own service after the commit, then logs a children fault and closes.
  - The dry run calls it with the request's After overlay, so the change's Exclude row decides the records under the record as it will once written.
  - The four readers it uses (`RecordsInReviewInvoicesAsync`, `ChildrensInReviewInvoicesAsync`, `InvoicesTakingTheChangeAsync`, `LinesOfInvoicesAsync`) now take the entity service.
- `ProcedureOverridePlan` gains `SwitchedOffRuleId` (set by `PlanRemoveAsync` for a changed rule) and `Supersedes` (set by `PlanChangeRuleAsync`). `PlanAsync` carries the removal's `SwitchedOffRuleId` into a replacement plan, because the two-call write's DELETE closes those findings.
- `PlanClosesAsync` (selection only):
  - A changed-rule removal selects with `SelectOpenSignalsOfPolicyAsync(rule, withBillingSubject: true)`, as `PreviewRemovalAsync` does, and carries `RemoveChangedWarning`.
  - A change selects through `SelectBaseFindingsAsync` and carries the write's own message for that selection: `ChangeCloseCompleteMessage`, `ChangeCloseChildrenOpenMessage`, or `ChangeCloseIncompleteMessage` when the record's own invoices cannot be read. No new text (ontology-r1 F2).
  - The union is counted by `AffectedInvoiceCounter.CountVisibleOpenInvoicesAsync` unchanged. A selection fault gives a null count, and the kinds and warning still show.
- `AffectedInvoiceCounter`: `CountVisibleOpenInvoicesAsync(IReadOnlyCollection<Guid> invoiceIds, …)` and `VisibleOpenInvoicesAsync`, pulled out of the Signal overload, which now maps its Signals to invoices and calls them. The behaviour is the same: empty or no open invoice gives 0 without asking the caller; any unknown gives null.
- `InReviewInvoicesInReachAsync`: the In Review invoices filed on the record and on the records directly under it, with every date each is judged on (the invoice date, and each Fee or Expense line's work date, else the invoice date).
  - It is bounded by `MaxChildRecords` (100).
  - More than one page, or any fault, throws, and the count is then null. (Fix round 1: an invoice not filed on exactly one core record, or with no date, is returned flagged, not thrown on; see F-2.)
- `ResolutionChangedAsync` (in the read service), for kinds that close nothing:
  1. The reach.
  2. Narrowed FIRST to the invoices the caller can Read (the counter's own question).
  3. One resolution per (core record, firm) on each side, at most `MaxComparedResolutions` (10). After reads through the request's overlay.
  4. An invoice counts when, on any date it is judged on, model 8.2's outcome differs: the Procedures applied, their versions and pins, the rules in force and their versions, the record rule's Exclude row, the excluded rules and Procedures, the gaps, and the deciding override rows (row, mode, chain record, subject, pin, firm; the reason is left out because it decides nothing).
- DTOs: `ProcedureDryRunClosesView { d170, kinds[], warning, message }` and `ProcedureDryRunAffectedView { count, text, basis }`. `basis` is `FindingsClosed` when anything closes, else `ResolutionChanged`; `text` is `ProcedureService.AffectedText`. `d170` is true exactly when `ChangedRuleRemoved` is among the kinds: a change closes Superseded, not by D-170, and a plain Exclude closes nothing (R1).

**Final response shape:**
```
{ record, date, firm, firmSource, proposal, now, after, afterLabel, disclosure,
  closes: { d170, kinds: ["ChangedRuleRemoved"|"BaseRuleSuperseded"], warning, message },
  affectedOpenInvoices: { count: int|null, text: string|null, basis: "FindingsClosed"|"ResolutionChanged" } }
```

**EB-D-218 decision for the counts.**
- **Resolution count: asked.** Every row the comparison read is added to the ONE combined check (chain records, override rows, Procedures, Procedure Versions, rule sets, policies and policy versions, on both sides, minus the exact synthetic rows). A denial is therefore 503, as for every other row the answer depends on, and the version-row guard's "asked ⇒ denying refuses" holds. Invoices the caller cannot Read are dropped before any resolution is read, so nothing about them, or about the record they are filed on, reaches the count or the check.
- **Findings-closed count: the write's own counting, unchanged** (design §4: "the selected set goes to `CountVisibleOpenInvoicesAsync` unchanged"). Invoices are narrowed by the caller's Read. The Signal selection and the children's override rows (nearer wins) are app-only, exactly as in 089c's removal preview and 089e's change result, which uac-r2 accepted. The dry run shows that number before the write rather than after it. See K-3.

**§11 justification:**
- `SelectBaseFindingsAsync`, `SupersedeSelection`:
  - *Existing:* the write's close.
  - *Extension:* it is extracted from that close, not added beside it.
  - *Cost of doing nothing:* a dry run with its own selection drifts from the close it predicts (preview ≠ sweep).
- `PlanClosesAsync`, `PlannedCloses`, `PlannedSupersede`, the two plan fields:
  - *Existing:* `PreviewRemovalAsync` (removal only) and the change's close.
  - *Extension:* both selections are called as they are; the plan only names their inputs.
  - *Cost of doing nothing:* the Impact step cannot show the D-170 warning or the change's close (POML ui-test "Removal warning").
- `CountVisibleOpenInvoicesAsync(invoiceIds)`, `VisibleOpenInvoicesAsync`:
  - *Existing:* the Signal overload.
  - *Extension:* its second half became these; the Signal overload calls them.
  - *Cost of doing nothing:* the resolution count would copy the caller-rights rule, and the two could disagree.
- `InReviewInvoicesInReachAsync`, `InvoiceInReach`:
  - *Existing:* the close's d0/d1 readers.
  - *Extension:* those keep only invoices the change decides the base rule on (deciders), which is the wrong set for an assignment. The new reader reuses `ReadChildrenAsync`, `LinesOfInvoicesAsync`, the bound and the judging-date rule.
  - *Cost of doing nothing:* there is no count for the kinds that close nothing (POML ui-test "Impact of a pin").
- `ResolutionChangedAsync` and `OutcomeOf`:
  - *Existing:* nothing compares two resolutions (the 8.2 banner is not built, R9).
  - *Extension:* the resolver's own `ReadRowsAsync` and `RulesOn` are used, so the comparison adds no second reader.
  - *Cost of doing nothing:* the same.

**Conditions → code → tests** (line numbers at the commit):
- **One shared selection** (write and dry run): `ProcedureAssignmentService.cs` `SelectBaseFindingsAsync` ≈961; the write calls it at ≈895 and the dry run at ≈1778. Tests: `Change_SaysWhatWouldClose_WithTheWritesOwnMessageAndSelection` (the child's invoice is selected only because the overlay's Exclude row decides it). `ProcedureAssignmentServiceTests` (the write's close) is unchanged and green.
- **Removing a changed rule reuses the removal preview's selection and warning**: `PlanClosesAsync` ≈1740, ≈1758-1761. Tests: `RemoveChangedRule_ShowsTheRemovalWarning_AndCountsTheFindingsThatClose`, `ReChange_ClosesTheRemovedRecordRulesFindings_AndSupersedesTheBase`.
- **D-170 only when a rule is switched Off** (R1): `RecordProcedureReadService.cs` ≈552. Tests: `PlainExclude_ClosesNothing_EvenWithOpenFindings`, the change test (`d170` false), the plain-removal phase of the removal test.
- **F2, the existing messages only**: `PlanClosesAsync` ≈1778-1786. Test: the change test (`message == ChangeCloseCompleteMessage`).
- **Resolution count, invoice-id overload, caps → null**: `AffectedInvoiceCounter.cs` ≈86-96; `RecordProcedureReadService.cs` `ResolutionChangedAsync` ≈660, narrowing ≈672-677, bound ≈679; `InReviewInvoicesInReachAsync` ≈1816 (children bound ≈1822). Tests: `Assign_ClosesNothing_CountsTheVisibleInvoicesWhoseResolutionChanges`, `ResolutionCount_PastTheBound_OrUnanswered_IsNull`.
- **EB-D-218, count rows asked in the ONE check**: `RecordProcedureReadService.cs` ≈533-538. Tests: the Assign count test (must-fire: deny the child record → 503), `Every200Guid_WasAskedOfTheCaller_DryRun` (`exclude for a firm with an invoice` lists the count-only version in AlsoAsked), `EveryVersionRow_IsAsked_OrTheBodyDoesNotDependOnIt_DryRun` (same seed).
- **No-write**: the closure in the dry-run host is a STRICT mock with only its two selections set up (`VerifyNoOtherCalls` in the new tests); `NoWrites` holds; the IL guard (`DryRunNoWriteGuardTests`) stays green with `RecordProcedureReadService` scanned. `PlanClosesAsync` shares its type with the write, so 084d-1 K2 applies to it too.
- **DTO key set**: `Assign_NowWithout_AfterWith_OneCheck_NothingWritten` now pins the 11 top-level keys and the keys of `closes` and `affectedOpenInvoices`.

**Guards extended.** `DryRunSeeds` gained four seeds: `assign with invoices`, `exclude for a firm with an invoice`, `remove changed with findings` and `change with findings` (10 in all, each run by both guards).
- The GUID guard plants a GUID into `closes` and into `affectedOpenInvoices` and must report it (must-fire per field).
- The version-row guard's must-fire seed is `exclude for a firm with an invoice`. The excluded rule is firm G's, so no list (no firm) shows it; its version is read only by the count's (P, G) resolution, and removing it changes the count.

**Mutation seeds** (each applied, run against `RecordProcedureDryRunContractTests`, restored):
- M1, the dry run selects over the real service instead of the overlay: 1 fails (the change test).
- M2, `d170` true for any close: 1 fails (the change test).
- M3, the compared rows are not asked: 4 fail (the version-row guard, two `Every200Guid` seeds, the Assign count must-fire).
- M4, judge before narrowing to visible: 1 fails (the Assign count test).
- M5, no resolution bound: 1 fails (the bounds test).
- M6, no removal warning: 1 fails (the removal test).
- M7, the change message dropped: 1 fails (the change test).
- M8, a replacement drops the removal's switch-off. It first survived (0 failed); the re-change test was then added, and M8 fails it (1 fails).

**Known limits:**
- **K-1.** Firm `reach` (design §4) is not in this slice's closed scope and is not in the response. The resolution count does not need it: an override for firm F leaves firm G's resolution unchanged, so G's invoices are not counted.
- **K-2.** A null count from the 100-child bound tells the caller that the record has more than 100 direct children, readable or not. The 089e write's `ChangeCloseChildrenOpenMessage` already says the same.
- **K-3.** The findings-closed count depends on app-only rows that are not asked: the Signal selection, and for a change, the children's nearer override rows. This is the same count, from the same selection, that the write returns and 089c's preview returns (accepted by uac-r2). It is not narrowed further here, so that preview = sweep.
- **K-4.** The resolution count reads at most 10 (record, firm) pairs on each side (20 `ReadRowsAsync`) plus the reach reads; R8's cost note applies. Past the bound it is null.
- **K-5 (corrected in fix round 1).** An In Review invoice that the caller CAN Read and that has no firm, no date, or other than one core lookup makes the resolution count null rather than skipping the invoice. One the caller cannot Read is dropped before it is judged and changes nothing. The number of invoices whose rights are asked is bounded (`AffectedInvoiceCounter.MaxDryRunInvoices`, 1000); past it the count is null and no rights call is made.
- **K-6.** The change's selection judges dates against the dry run's clock day (ontology-r1 K1, "if saved now").

**Tests.** `RecordProcedureDryRunContractTests.cs` 54 (40 before this slice): 6 new facts plus 4 new seeds × 2 guards, plus the key-set update. The host now builds a REAL `AffectedInvoiceCounter` over the rig's world and probe, and a STRICT closure mock.

**Gates.** BFF build 0 warnings, 0 errors. ArchTests 2570/2570. Filtered `Sprk.Bff.Api.Tests` (Finance, Procedure, RecordProcedure, RouteAuthorization, Supersede) 8331 passed, 4 skipped, 0 failed. This filter adds `Supersede`, so the number does not compare with 2a's. `dotnet format whitespace --verify-no-changes` clean on the four touched code files.

**Publish size.** Fresh local `master` (`2ef4a17915`, exported with `git archive`) against this branch: `dotnet publish -c Release -r linux-x64`, short output paths, `Compress-Archive -CompressionLevel Optimal`, 191 files each. Master 42,030,897 bytes, branch 42,079,298 bytes, delta +48,401 bytes (≈0.05 MB, far below the +5 MB threshold).
- The delta is the whole ebilling-r1 branch against master, not this slice alone; the master used here differs from 2a's.
- No package and no assembly is added: the file counts match.

### 084d-2b fix round 1

Branch `task/084d-2b-fix1`, from `97e8a59a2a`. Rule applied to every count: a count is a number only when it is complete. Past a bound, on a fault, or when a question about the rows the caller can see cannot be answered, it is null, never partial; and a row the caller cannot Read never changes the answer.

**Findings -> change -> test** (all in `Services/Finance/Procedures`; tests in `RecordProcedureDryRunContractTests.cs`):
- **F-1, a change with unreadable children gave a partial count.** `ProcedureAssignmentService.PlanClosesAsync` (about 1785-1795): when `selection.ChildrenFault` is set, `countable` is now false and the write's `ChangeCloseChildrenOpenMessage` is kept. Before, only the message changed, so past 100 children or on a child-read fault the number covered the record's own invoices only. Tests: `Change_WithUnreadChildren_IsANullCount_NeverAPartialOne` (more than 100 children, a child that cannot be read; must-not-fire: exactly 100 children give 1 and the complete message).
- **Review finding 4, the change-selection fault branch had no test.** Test: `Change_SelectionFault_IsANullCount_WithTheIncompleteMessage` (null count plus `ChangeCloseIncompleteMessage`). No code change.
- **F-2 and K-c, an invoice the caller cannot Read could turn the count null.**
  - `InReviewInvoicesInReachAsync` no longer throws on an invoice with other than one core lookup (FR-05) or with no date. It returns it with `InvoiceInReach.Judgeable == false`, and reads no lines.
  - `RecordProcedureReadService.ResolutionChangedAsync` (about 673-689) narrows by Read first, then returns null only when a VISIBLE invoice is not judgeable.
  - The lines are read after the narrowing, for the kept invoices only (`ProcedureAssignmentService.JudgedOnAsync`, new, split out of the reach read). Before, a lines fault or page overflow on an unreadable invoice also changed the answer; the lines of an unreadable invoice are now never read.
  - Tests: `UnjudgeableInvoice_NullsTheCount_OnlyWhenTheCallerCanReadIt` (no date and two core lookups, each readable -> null, unreadable -> a number); `UnreadableInvoice_ItsLinesAreNeverRead` (an endless lines table does not null a count whose only invoice is unreadable).
- **F-3, tests that proved nothing.**
  - `Assign_ClosesNothing_CountsTheVisibleInvoicesWhoseResolutionChanges`: added the decided child (it has its own row for the Procedure, so its resolution is unchanged and it is not counted). Dropped the parent's-invoice control: a parent's invoice is out of reach and the record's assignment cannot change a parent's resolution, so counting it or not gave the same answer.
  - `ResolutionCount_PastTheBound_OrUnanswered_IsNull`: added exactly 10 resolutions -> a number (catches `>` against `>=`).
- **K-5, cost bound.** `AffectedInvoiceCounter.MaxDryRunInvoices` (1000, four rights pages of 250). `VisibleOpenInvoicesAsync` and the two `CountVisibleOpenInvoicesAsync` overloads take an optional `maxInvoices` (default none, so 081 and 089 are unchanged). More distinct invoices than the bound gives null before any app read of states or any rights call. The dry run passes it on both counts: the resolution count (`ProcedureAssignmentService.VisibleOpenInvoicesAsync`) and the findings-closed count (`PlanClosesAsync`). Test: `InvoiceCount_PastTheRightsBound_IsNull_AndAtTheBoundANumber` (1001 -> null with no invoice rights asked; exactly 1000 -> 1000 with 1000 asked; the findings-closed count past the bound -> null).
- **K-5, cancellation.** The request's `CancellationToken` now reaches the shared selection: `SelectBaseFindingsAsync`, `RecordsInReviewInvoicesAsync`, `ChildrensInReviewInvoicesAsync`, `InvoicesTakingTheChangeAsync`, `LinesOfInvoicesAsync` and the closure's subject selection take an optional token. The write calls them without one, so it gets `CancellationToken.None` as before and its behaviour is unchanged. The children side's catch does not swallow a cancellation of the request. No test: the write's path is the one covered, and the dry run only adds the token.

**Mutation seeds** (each applied, `RecordProcedureDryRunContractTests` run, restored):
- F-1: `countable = false` removed from the children-fault branch -> 2 fail (`Change_WithUnreadChildren...`: more than 100 children, a child that cannot be read).
- F-2: judge before narrowing (`reach.Any(i => !i.Judgeable)` in place of the narrowed list) -> 2 fail (`UnjudgeableInvoice...`, both unreadable cases). A second seed, the lines read for the whole reach instead of the kept invoices -> 2 fail (`UnreadableInvoice_ItsLinesAreNeverRead` and the Assign count test).
- K-5 bound: the bound check disabled -> 1 fails (`InvoiceCount_PastTheRightsBound...`).

**Known limits** (084d-2b, as amended):
- **K-a.** On the findings-closed basis the selection's app-only rows (Signals, and the children's override rows for nearer-wins) are not asked of the caller. The count can depend on a child's override the caller cannot Read, but it counts only invoices the caller CAN Read. This is the write's own response, from the same selection.
- **K-b.** `ChangeCloseChildrenOpenMessage` tells the caller that not all of the records under the record could be read. The write says the same. With F-1, that case also has no count; whether the children were read can itself depend on invoices on the children that the caller cannot Read (a children read past one page of invoices is a fault), which is the same disclosure the write makes.
- **K-6.** A dry run is repeatable, so the findings-closed count can be probed (propose, read the count, change the proposal, read again). It is bounded to invoices the caller can Read, as the write's is, so a probe learns nothing about a row the caller cannot Read.
- **K-5 (corrected).** A visible invoice that has no firm, no date, or other than one core lookup makes the resolution count null. An unreadable one is dropped first and changes nothing. The invoices whose rights are asked are bounded at 1000 (above); the bound is on the total in reach, readable or not, so it discloses only that the total in reach is past 1000, an aggregate of the same kind as K-2.
- **K-d.** The reach read throws on more than one page of In Review invoices in a batch (the reach is read before any Read can narrow it, and a partial page narrowed to Readable invoices would be a partial count). So the count is null when the total in reach, readable or not, is past one page (5000 in Dataverse). An aggregate only, no row. Paging the reach to narrow it is not done. Between 1001 invoices and a page the rights bound gives the null; past a page the reach read does.

**Gates.**
- BFF build: 0 warnings, 0 errors.
- `RecordProcedureDryRunContractTests`: 64 (54 before; 10 new: one fact for the rights bound, one theory of 4 for F-2, one fact for the lines, one theory of 3 for F-1, one fact for the selection fault; the Assign and bound facts changed in place).
- Filtered `Sprk.Bff.Api.Tests` (Finance, Procedure, RecordProcedure, RouteAuthorization, Supersede): 8341 passed, 4 skipped, 0 failed (8331 before this round).
- ArchTests: 2570/2570 passed.
- `dotnet format whitespace --verify-no-changes` clean on the four touched code files and the test file.

### 084d-1 / 2a / 2b close (2026-11-13)

Merged into work at `c313c6c211`: 2a `224007eda7`, 2a fix1 `475c05ed8e`, 2b `97e8a59a2a`, 2b fix1 `4c5d562619`.

**Reviews**
- Opus adversarial pass on 084d-1 + 2a, then fix round 1.
- Sonnet scoped pass on 2b, then fix round 1.
- uac-r2: PASS on 2a, 2a fix1, 2b and 2b fix1.
- ontology-r1: Opus review OK on 084d-1, on 2a + fix1 and on 2b + fix1. No F-class finding and no Signals change.

**Known limits accepted at review**
- **uac-r2:** K-d and the 1000-invoice dry-run bound are the same class: aggregate-size signals on the total of In Review invoices in reach ("more than a page" / "more than 1000").
  - Both need a record the caller can already Read and Write (the route's 404 filter).
  - Neither carries an id or any fact about a single invoice.
  - They are inherent: narrowing first would need the rights call that the bound exists to cap.
- **ontology-r1, to take into 084d-3 / 084d-4:**
  - **O-K1a:** the rows of the resolution comparison join the one caller check. So one unreadable child override row turns the whole preview into a 503, not just the count. 084d-3 considers nulling the count instead; that needs a uac-r2 look, because it changes what the check covers.
  - **O-K1b:** a null count carries no reason; past the bound and a fault look the same. 084d-4 must show null as "can't be counted", never as 0 or blank.
  - **O-K3:** no test pins that the dry-run cancellation token never reaches a write read. 084d-3 extends the write-side guard (`ProcedureAssignmentServiceTests` ~2246) to the `sprk_invoice`, `sprk_billingevent` and core-table reads.
  - **O-K4a:** an overlay refusal surfaces as 503 `assignment.read_failed` "Try again", although it is deterministic. No real path reaches it today.
  - **O-K4b:** the child-fault log line is written after the Signal select, so it is lost if that select throws. Operator log only.

## 084d-3

Parity, no-write and concurrency tests for the dry run. Test-only: no product code changed. Branch `task/084d-3`, from `b341a69005`.

**What was built:**
- `tests/integration/contract/Api/Finance/RecordProcedureWriteRig.cs`: `RecordProcedureRig` in a writable mode, on its own instance.
  - A transaction interpreter over the same in-memory world. Each request is checked in order: a create of an existing id is a duplicate key, an update or delete of a missing row does not exist, and a conditional update needs the row version at its turn. Only then is the whole transaction applied, all or nothing.
  - A created row gets the server-filled columns: `createdon` from the rig clock (UTC), `statecode` 0, `statuscode` 1, and a row version. An update advances the row version.
  - Two REAL `ProcedureAssignmentService`s over it:
    - the writer, with a stub ownership answer (owned, creator person = the rig's Reviewer) and a closure mock with its selections and the retired-close set up;
    - the planner, built as 084d-2's host builds it: a STRICT ownership resolver and a STRICT closure with only its two selections.
  - A per-table rig page size for the queries the resolver pages at 5000 (condition (a)), a page log, a `BeforeQuery` hook for the concurrency tests, and one lock around every read and transaction (the world is not thread-safe).
  - `Strict()`: every write member of the seam throws, the transaction included.
- `tests/integration/contract/Api/Finance/RecordProcedureDryRunParityContractTests.cs` (204 cases).
- `RecordProcedureRig.RightsProbe`: `Batches.Add` under a lock, because concurrent dry runs share one probe.
- `ProcedureAssignmentServiceTests`: the O-K3 extension (below).

**Parity matrix (162 cases).** 9 kinds × 3 seeds × 3 record types (matter M, project P, work assignment W) × 2 dates (Feb 1 and Mar 15; the dry run's and the write's instant is Feb 10 12:00 UTC).
- Kinds: assign Follow-latest, assign Pin (v1 of a two-version Procedure), exclude Procedure, exclude rule, change rule, re-change (a change replacing this record's own change of the rule), switch (Follow → Pin replacing the Follow row), remove, and remove of a changed rule.
- An existing override that a kind needs is written by the REAL writer at Jan 20 09:00 UTC (switch, re-change, both removals), so its rows have the write's shapes.
- Each case:
  1. runs `DryRunAsync`;
  2. reads both lists (`ListProceduresAsync`, `ListRulesInForceAsync`) before the write;
  3. REALLY writes the same change through the writer (a replacement is the wizards' two calls: remove, then create);
  4. reads both lists again.
- Each case asserts:
  - Now equals the read before the write;
  - After equals the read after the write, Procedures and rules each;
  - `proposedRowIds` equals the ids of every create the write sent;
  - both sides carry no permissions (`RecordWritePermissions.None`);
  - it is not vacuous: After differs from Now. The one exception is a first change on Feb 1, where After must EQUAL Now, because the record rule is in force only from the save and a change's Exclude row does not show before then. Where the write creates an override row that shows on the date (Mar 15), the raw post-write read differs from the normalised one, so the normalisation has something to blank.
- Results: 162/162 pass.

**Normalisations (one).**
- A proposed row (an id in `proposedRowIds`) has its `stamp.byId` and `stamp.byName` blanked on both sides.
  - Why: After shows no creator for a proposed row (ontology-r1 F3), while the written row carries the writer's creator person (`sprk_createdbyperson`; `sprk_procedureassignment` is a stamped table).
  - Rows matched by `rowId` (Procedure overrides) or `overrideRowId` (rule marks).
- Not normalised:
  - `stamp.on`: the dry run's one created-on and the write's `createdon` come from the same instant, so they compare exactly;
  - `permissions`: set by the route, so both are None at the service and compared;
  - everything else.
- K3 of 084d-1 (the record set's effective-from uses the build day) needs no normalisation: no view shows it.

**Seeds and conditions.**
- `plain`: the kind's own rows.
- `chain`: also an Excluded Procedure and a Pinned Procedure on the matter, a firm-G-scoped rule Exclude on the project, and a legacy client-default rule.
- `large`: conditions (a) and (b).
  - Rig page size: 1 row on assignments and versions, 50 on rule sets and policies, plus a three-rule Procedure, so every read of the four tables spans pages.
  - 510 filler Procedures applying to every record. Each has a version, a set and an enabled, versionless policy, with ids `fffffff1-…` to `fffffff4-…` that sort after every derived id. So there are two 500-id chunks of rule sets and of policies, and the record's set and record rule sit in the first.
- Asserted per large case, on the pages the dry run read:
  - (a) policies, rule sets and versions were each read over more than one page;
  - (b) the policy image's parent (the record's set) and the version image's parent (the record rule) each sit in a chunk with a later chunk after it.
- (b), exactly once: `EachPolicyAndVersionImage_IsAppendedExactlyOnce` (9 cases: change, change over an Off record rule, remove of a changed rule × 3 record types, large seed).
  - It runs the REAL resolver over a `ProcedureRowOverlay` of the plan, counting every row each page returns.
  - Each policy and version after-image is returned exactly once. The premise asserts above apply.
  - In the dry run itself, a duplicate makes the resolver throw (503) and a missing image breaks the equality. The exception is a switched-off policy image, which the resolver drops either way; that is why the exactly-once case for removing a changed rule exists.
- (c): before each dry run, every seeded `createdon` in the four tables is earlier than the clock.
- One `createdon` per request, Kind Utc: the overlay refuses a non-UTC stamp (084d-1), and the parity's exact `stamp.on` comparison holds only with the request's one stamp.

**Mutation proofs.** Each was applied to the product code, built, run against `RecordProcedureDryRunParityContractTests` (204), and restored:
- M1, drop the overlay's `createdon` stamp (`ProcedureRowOverlay` ctor): 117 fail. That is every case of the 7 kinds that create an override row, 18 each, except the change: 9 (Mar 15 only; on Feb 1 the change shows nothing).
- M2, drop the chunk check (`ImagesFor` returns every image to every chunk): 21 fail. 12 are the large-seed parity cases of change and re-change (the duplicate is a resolver fault, so a 503); 9 are the exactly-once cases.
- M3, drop the policy switch-on (`PlanChangeRuleAsync`: `policyAfter = policy`): 42 fail. 36 are parity cases (change and re-change × every seed, record type and date); 6 are exactly-once cases (change, change over an Off rule).

**No write** (`DryRun_OverAStrictRig_WritesNothing`, 27 cases: 9 kinds × 3 record types, chain seed).
- Setup: the seeding writes are done first, then the rig is made strict, so every write member and the transaction throw.
- Each dry run answers 200 and:
  - calls no write member of the seam;
  - sends no transaction;
  - leaves the four tables byte-identical (row-by-row snapshot);
  - asks the planner's strict ownership resolver nothing;
  - calls only the closure's two selections;
  - does not touch the writer's ownership or closure.
- Must-fire control (`TheStrictRig_RefusesARealWrite`): a real write over the strict rig fails, the transaction is recorded, and the world is unchanged.

**O-K3** (`ProcedureAssignmentServiceTests.Change_WhenTheTransactionCommitsButGivesNoAnswer_AndTheRequestIsCancelled_StillReadsBack`, extended).
- The finding now sits on an invoice with a line.
- The close window is the queries after stage 3 up to the last `sprk_signal` read. It must read `sprk_invoice`, `sprk_billingevent` and the records under the matter (premise), and every one of those reads must run on a token that cannot be cancelled.
- Must-not-fire: the caller-scoped count after the close keeps the request's token.
- Mutation M4: a cancellable token passed to `SelectBaseFindingsAsync` in the write fails the test, but already at the existing Signal-read line.
- Mutation M4b: a cancellable token passed only to the record's invoice and line readers fails only the new assertion (1 fail of 131 `ProcedureAssignmentServiceTests`).

**Concurrency.**
- `InterleavedDryRuns_EachEqualTheirSequentialAnswer` (3 record types). An assign, an exclude rule and a re-change, two of them twice, plus a plain read, all run concurrently on ONE read service and planner, 3 rounds, with a yield before every query.
  - Each answer equals its sequential answer, byte for byte, and the plain read equals Now.
  - Premise: the trace switches between two dry runs at least 3 times.
  - The world is unchanged and nothing is written.
- `ADryRun_WithARealWriteBetweenItsReads_IsSelfConsistent` (project, work assignment). A real one-row write (assign) lands at every one of the dry run's queries in turn, on a fresh rig each time.
  - Now and After are each exactly the sequential answer before or after the write.
  - The post-write read equals the sequential one, so the write is unaffected by the overlay.
  - The proposal's row is never written.
  - Premise: the sweep produced (Now before, After after), so the write really landed between the two reads (R10), and also (after, after).

**O-K1a (no behaviour change).** The parity probe can Read every row, so the whole-preview 503 for an unreadable comparison row never arose in this work. Nothing here shows it to be a problem, and no proposal is made from 084d-3. The question stays with uac-r2 as recorded at the 084d-1/2a/2b close.

**Product bugs found:** none.
- The first run failed until the interpreter filled `statecode` on create. That was rig fidelity: Dataverse fills it, and the resolver's re-checks require it.

**Known limits:**
- **K1.** The design's `outranked` and `change prefill` seeds are not separate seeds. `chain` covers the other chain rows, a legacy rule, a pinned and a firm-scoped row.
- **K2.** "The 503s match" is not covered: every matrix case answers 200 on both sides.
- **K3.** Every proposal is for no firm. A firm-scoped Exclude or change is not in the matrix.
- **K4.** The exactly-once case plans directly (`PlanChangeRuleAsync`, `PlanRemoveAsync`) rather than through `DryRunAsync`, because the overlay is built inside `DryRunAsync` with no seam. The re-change's merged plan is covered by the parity alone.
- **K5.** The concurrency tests run on the in-memory world with one lock between queries. They show that the overlay is per request; they say nothing of Dataverse's isolation. The write-between test uses a one-row write. A multi-row write landing between two queries of one resolution can tear that resolution (R10), as it can for the plain read.

**Gates.** BFF build 0 warnings, 0 errors. ArchTests 2570/2570. Filtered `Sprk.Bff.Api.Tests` (Finance, Procedure, RecordProcedure, RouteAuthorization, Supersede) 8545 passed, 4 skipped, 0 failed (8341 before: +204, the new class). `dotnet format whitespace --verify-no-changes` clean on the four touched test files.

### 084d-3 review round 1 (2026-11-13)

Test code and this file only; no product code changed. Branch `task/084d-3`, from `06139f6aca`.

**F3-1, fixed.** `Change_WhenTheTransactionCommitsButGivesNoAnswer_AndTheRequestIsCancelled_StillReadsBack` seeded no record under the matter, so `ChildrensInReviewInvoicesAsync` returned early and its two reads never ran.
- The test now seeds a work assignment under the matter with an In Review invoice and an open finding on it. The change's own Exclude row on the matter is the override row that decides the child's invoice (the child's chain read finds it).
- `selectionTables` now also holds `sprk_procedureassignment`, `sprk_ruleset`, `sprk_policy` and `sprk_policyversion`: every read of them in the close's window runs on a token that cannot be cancelled.
- Premises, asserted inside the close's window: (1) a `sprk_invoice` read that names the child's lookup column and the child's id (the children's In Review invoice read); (2) a `sprk_procedureassignment` read that names the child in its chain filter (`ProcedureResolver.ReadOverridesAsync`); (3) the child's finding ends Resolved, so the override read kept the invoice.
- Mutation proofs (product code, restored afterwards). The write has no request token, so each mutation records the request's token in a static field in `ChangeRuleAsync` and passes it to one read:
  - M5, the children's `sprk_invoice` read in `ChildrensInReviewInvoicesAsync` takes the request token: the test fails (1 of 1) at the token assertion over `selectionTables`.
  - M6, the `ProcedureResolver.ReadOverridesAsync(entities, chain, ct)` call in `ChildrensInReviewInvoicesAsync` takes the request token: the test fails (1 of 1), at the premise that the override read ran (the cancelled read throws before the rig logs it, the child is dropped, and the close's children side fails). Either way it fails.
  - With no mutation the test passes (1 of 1).

**F3-2, fixed.** The comment said every read of the four tables spans pages, but the large seed had at most one assignment row on a chain.
- A work assignment's chain is itself and its project; the matter is not on it. So the large seed now puts `AssignmentFillers` (2) filler rows on each of the matter, the project and the work assignment: rule excludes of filler policies (enabled, no version, so they run nowhere and show nowhere), ids `ffffffff-...` that sort after every derived row id, `createdon` before the clock (condition (c) of the createdon check still holds).
- The rig pages assignments one row per page in id order, so a replaced row (switch, re-change, remove, remove of a changed rule) with two rows after it is never on the last page. Asserted per large case: the replaced row's id sorts before every filler id; an assignments page with Page > 1 was read; an assignments page 1 with More was read; and for a kind that replaces or removes a row, an assignments page numbered above the filler count was read (the replaced row plus two fillers).
- The comment at the top of the large seed now names what each table's paging comes from.
- No new normalisation. 162/162 parity cases and the 9 exactly-once cases stay green (`RecordProcedure*` 549 passed).
- Mutation proof: with `AssignmentFillers = 0` the large seed's 63 cases fail (54 parity, 9 exactly-once), the other 141 pass. Restored.

**Known limits, accepted:**
- **K-1.** The chunk-order check does not prove read order, and the page log is not After-specific.
- **K-2.** The rig stamps one `createdon` per write, so a per-statement ordering change of the Exclude row and the version would go uncaught. The `statecode` and `createdon` fill is by construction, matching Dataverse defaults.
- **K-3.** The proposed normalisation also blanks the existing row's creator for switch and re-change. Covered by `RecordProcedureReadModelContractTests`.
- **K-4.** Closes and Affected are not compared. Owned by 084d-2b.
- **K-5.** The strict-rig control exercises `ExecuteTransactionAsync` only. `WriteRefusingEntityService` refuses first.
- **K-6.** The O-K3 must-not-fire is weak, by-id reads are not captured, and the window ends at the last `sprk_signal` read.
- **K-7.** Interleaving happens only at `RetrieveMultipleAsync`, and some shared doubles are unsynchronised on refusal paths only.

## 084d-4

The client half of 084d: every wizard's Impact step on the dry run. Branch: the agent worktree, from `588526e579`. Client only: no server, deploy or web-resource version change.

**STOP hit: the Assign Procedure wizard is not built.** No route lists the Procedures that can be assigned to a record. The routes are:
- `GET /api/finance/procedures/{id}` (081): reads one Procedure by id.
- The record's `/procedures` list (084): only the Procedures that already apply to, or are excluded on, this record. That is the wrong set, because the prototype's Choose step offers the catalog MINUS those (`CATALOG.filter(p => !before.procedures.some(...))`). Assigning from it would duplicate the Procedure wizard's Follow/Pin choices, and would never reach an AssignedRecordsOnly Procedure that is not on the chain.

An Xrm.WebApi read is ruled out: 084's POML forbids Xrm reads of the Procedure, override and rule tables. The coordinator split the wizard into **084d-5**: a server list route under EB-D-218, then the wizard. There is no "Assign Procedure" entry point in the UI, not even a disabled stub.

**What was built:**
- `services/dryRunApi.ts`, new:
  - the wire types (the DTOs of `RecordProcedureReadService.cs` ~57-130);
  - `dryRunRequestOf(plan)`, the request mapping;
  - `requestFirmOf`, for R6's scope;
  - `dryRunUrl`, on the same prefix and query as the list reads;
  - `readDryRunView`, where a non-number count reads as null, never 0;
  - `createDryRunApi`. A refusal is the wizards' `WriteFailure`, with the title, detail and errorCode.
- `components/wizards/impact.ts`, new, pure:
  - `impactChanges` (Now → After). Procedures are keyed by id, rules by policy id. Each side is described in the panel's own words (`procedureState` + version; `ruleBadges` + version). A row on one side only is "Not listed".
  - `affectedLine`, `dryRunFailureText`, and the R6, advisory and null-count sentences.
- `steps.tsx` `ImpactStep`:
  - The dry run's answer: the label `Now → {afterLabel}`, the changed Procedures and rules, and the D-170 bar when `closes.d170` (title "Open findings close", body the server's `warning`).
  - The server's `warning` when it comes without d170, and the server's `message`.
  - `affectedOpenInvoices`, and the server's disclosure notes.
  - The O-21 line, always.
  - The choice's own before → after (084b's interim guess) shows only while there is no answer, captioned "Your choice".
- `WizardFrame.tsx` asks for the dry run of the plan, for the firm and date the lists were read with. Answers are keyed by the exact body and tagged with a sequence number, so only the latest ask's answer is kept. Moving to another row bumps the sequence.
- Plumbing:
  - `ProceduresPanel` gains a `dryRun` prop, and snapshots `query` with the open row (`queryOf`: the firm only when `firmSource` is `Query`).
  - `ProcedureWizard` gains `dryRunApi` and `firmId`; `RuleWizard` gains `dryRunApi` and `query`.
  - `main.tsx` builds the client.

**Request mapping** (`plan.ts` step(s) → `ProcedureDryRunRequest`):

| Plan | Request |
|---|---|
| `remove X` | `{ kind: Remove, overrideId: X }` |
| `assign` (FollowLatest / Pinned / Excluded) | `{ kind: Assign, assign: { procedureId, mode, pinnedVersionId?, reason? } }` |
| `excludeRule` | `{ kind: ExcludeRule, excludeRule: { policyId, reason, firmId? } }` |
| `changeRule` | `{ kind: ChangeRule, changeRule: { policyId, reason, firmId?, ruleBody } }` |
| `remove X`, then a create | the create's kind with `replacesOverrideId: X` (`PlanAsync` removes first, then plans the create over the removal) |
| empty plan | nothing is asked |
| any other shape (two creates, create then remove, two removes, three calls) | not sent (`unsupported`). No plan the wizards make has this shape: a test runs every fixture row × choice. |

**Wording and its source:**
- "If saved now": the server's `afterLabel`, shown as given.
- The disclosure notes (R5, R10, not-a-promise): the server's, shown as given.
- The section 15 warning (`RemoveChangedWarning`) and the change's close message (`ChangeClose*Message`): the server's, shown as given (ontology-r1 F2).
- The affected count: the server's `text` (`ProcedureService.AffectedText`).
- **R6** (084d-dry-run-design, uac-r2 review): "This preview isn't available for this firm." It is scoped to a 503 `procedures.unavailable` on a request that names a firm (`excludeRule.firmId` / `changeRule.firmId`, mirroring the server's `RequestFirmOf`). Any other 503 shows the ProblemDetails title and detail.
- **O-K1b:** "How many open invoices you can see are affected can't be counted."
- Chrome of ours, not findings text:
  - the D-170 bar's title "Open findings close";
  - "The preview is not shown";
  - "You can still go on: the preview is only a guide, and the save itself decides.";
  - "The lists read the same.".

**Decisions and deviations:**
1. **089's removal preview is superseded, but kept as a fallback.** The dry run carries the same fields (`closes.d170`/`kinds` ≈ `closesRule`, `closes.warning`, `affectedOpenInvoices.count`/`text`), from the same selection (084d-2b). So the preview is not asked when the dry run answers. It is asked only when the dry run of a plan that removes was refused. The reason: the dry run asks more of the caller (finance.read, the remove route's privileges, and one check over every row of both lists, so one unreadable row is a 503: O-K1a). Without the fallback, the section 15 warning would not show at all before a changed rule's removal in that case.
2. **A refusal never blocks Confirm.** That covers a refused dry run and a refused removal preview. Next waits only while a read is in flight, so an answer (above all a warning) is shown before Confirm. 084b blocked a removal whose preview was refused; that test was changed to match the new behaviour. Reason: the brief, and the design note sets no blocking case.
3. The dry run is asked with the lists' own query, so Now matches the panel. A firm picked in the panel is re-sent; the billing-firm default is not.
4. The 084b source guard "no dry-run read" became "the dry-run route is named only in `dryRunApi.ts`". The write-scan guard now allows `dryRunApi.ts`'s single POST, and asserts it names no 089 route.
5. Test plumbing: `serverFake` records dry runs in `dryRunCalls`, apart from `writeCalls`. With no handler, the dry run fails, so the old tests still exercise the preview fallback. Nine existing `Next` clicks right after Impact now wait for Next to be enabled.

**Criteria → tests:**
- Request mapping, every kind: `dryRunApi.test.ts` "every plan maps …" (9 cases plus empty and unsupported); the body test; the route test.
- Every wizard plan has a mapping: "every plan the two wizards make …". *Justification:* it proves the brief's STOP condition for item 1 does not fire.
- Now → After, label, count, O-21, request and query: `Impact.test.tsx` "sends the plan as the route's request …".
- Nothing shows "will succeed": "nothing presents After as …".
- D-170 and the superseded preview: "D-170: removing a changed rule …".
- The warning without d170, and the message: "a warning without d170 …".
- A null count: "O-K1b: …".
- The 503 R6 case, with Confirm and the save working: "R6: …". The 503 outside R6: "a 503 procedures.unavailable for a request that names no firm …".
- Another refusal, with Confirm working: "another refusal (403) …".
- Re-ask and the stale race: "Back, a new reason, Next …".
- The fallback preview after a refused dry run: the existing `Wizards.test.tsx` removal tests, and the changed "a refused removal PREVIEW …".
- Mutation seeds (each applied, `Impact.test.tsx` run, restored):
  - the stale-answer check dropped: 1 fails;
  - the removal preview always asked: 1 fails;
  - Next blocked on a refusal: 2 fail;
  - a null count shown as 0: 1 fails;
  - R6 not scoped to a request firm: 1 fails.

**Known limits:**
- **K-1.** The Assign Procedure wizard (EB-D-220/221) is not built: STOP, moved to 084d-5.
- **K-2.** "The firms reached" (POML ui-test "Impact of a pin") is not shown: the server does not return `reach` (084d-2b K-1).
- **K-3.** O-K4a: an overlay refusal comes back as 503 `assignment.read_failed` "Try again". It is shown with its title and detail like any other refusal; no real path reaches it today.
- **K-4.** A row is matched across Now and After by Procedure id or policy id. A change shows as the base rule leaving or changing and the record rule appearing, each as its own row.
- **K-5.** ESLint was not run: `src/solutions/RecordProcedures` has no ESLint config and no local eslint, so `npm run lint` fails here. This is pre-existing. The typecheck gate covers the changed files.
- **K-6.** Under full parallel load, a few wizard tests (both old and new code paths) occasionally hit testing-library's 1 s `findBy` limit. 1 of 3 full runs had 3 such failures; the other 2 passed 365/365. Each file passes alone.

**Verification:**
- `npm run typecheck`: surface-owned 0 (98 pre-existing errors in the shared libs).
- `npm test`: 17 suites, 365/365. 340 before, plus 25 new (16 in `dryRunApi.test.ts`, 9 in `Impact.test.tsx`).
- `npm run build`: OK.
- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors (no server file changed).

### 084d-4 review round 1 (2026-11-13)

Client only: no server file changed. Each item is finding, change, test.

1. **A hung dry run held Next, and Confirm with it.**
   - *Finding:* `WizardFrame` kept Next off while the dry run loaded, and the fetch had no timeout or abort. A call that never answered blocked Confirm for ever.
   - *Change:* `raceTimeout` (`overridesApi.ts`) gives a read an abort signal that fires after `READ_TIMEOUT_MS` (20 s; `createDryRunApi` / `createOverridesApi` take `{ timeoutMs }`) or when the caller aborts. The race does not depend on the fetch honouring the signal. A timeout is a failure `client.timed_out`, worded "The preview took too long to come back." (`dryRunFailureText` / `previewFailureText`), so Next is held at most that long. 089's removal preview (the fallback) has the same bound, because Next waits on it too. `WizardFrame` holds one `AbortController` per read, aborts them all when the wizard closes (unmount) or moves to another row, and drops an answer that arrives after either (`mounted`, `epoch`), so nothing sets state after unmount. A client that throws is a failure too (`settle`).
   - *Tests:* `Impact.test.tsx` "is given up on at the timeout …" (real 80 ms timeout, a never-answering handler: "took too long", Next enabled, Retry, save works), "Confirm is reachable after a timeout …", "closing the wizard abandons the read …" (the fetch's signal fires; a late answer reports nothing), "a removal preview that does not answer is given up on …". `dryRunApi.test.ts` "review round 1: the timeout and the abort signal" (timeout with a fetch that ignores its signal, the caller's signal, a fake-timer check that no timer is left; fake timers are used there only, with no real I/O).
2. **A failed dry run had no Retry.**
   - *Change:* the failed bar has Retry (`dryrun-retry`). `retryDryRun` forgets that proposal's cached failure (and the fallback's answer) so the step asks again, as 084b's `retryPreview` did for the preview. Failures stay cached until Retry (Back and Next do not re-ask).
   - *Tests:* "a failed dry run has a Retry that asks again"; the timeout test above; the changed `Wizards.test.tsx` fallback test (Retry on the combined message).
3. **One cached answer, evicted by a late one.**
   - *Change:* answers are a `Map` keyed by the exact request (firm, date, body). A late answer is stored under its own key and cannot replace the live proposal's; there is no sequence number any more. Moving to another row clears the map and bumps `epoch`.
   - *Test:* "a late answer to an earlier proposal never replaces the live one …" (the two proposals each keep their answer; two asks in all).
4. **The removal-preview fallback.**
   - *Change:* `asksRemovalPreview`: asked only after a dry run refused with 403, 404 or 503 (`FALLBACK_STATUSES`); never after 400, 409, a timeout, a network failure or a sign-in failure. When used, the Impact step shows ONE message: the preview alone when it answered (with a note that the lists' preview could not be read; never "The preview is not shown" over a shown preview), or one error bar with `role="alert"` and one Retry when both reads failed.
   - *Tests:* `Impact.test.tsx` "the removal preview stands in only after …" (403, 404, 503 ask; 400, 409, network, timeout do not; one alert; the combined bar names both failures); `dryRunApi.test.ts` "only 403, 404 and 503 …"; `Wizards.test.tsx` fallback test (one alert, no `dryrun-failed`). Mutation: `asksRemovalPreview` made always true: 5 of the 31 `Impact.test.tsx` tests fail.
5. **The advisory line ran into the next sentence.**
   - *Change:* the failure text and the advisory line are separate elements (`dryrun-failed-text`, `dryrun-advisory`).
   - *Test:* "a refusal with a title and no detail does not run into the advisory line" (the text is exactly "Conflict").
6. **`affectedLine` composed a count sentence.**
   - *Change:* the client composes nothing: a number shows the server's `text`; a number with no text shows the number alone; a null count shows "can't be counted".
   - *Tests:* `dryRunApi.test.ts` "affectedLine shows the server's text …"; `Impact.test.tsx` "a number with no text from the server is shown as the number alone".
7. **Confirm for an unchecked removal.**
   - *Change:* when the plan removes an override and the dry run failed and no removal preview answered, Confirm shows `UNCHECKED_CLOSE_LINE`: "What this change closes could not be checked. You can still go on if you choose to; the save itself decides." It does not block. *Beyond the literal brief:* it also shows when the fallback was not asked (a 409, a timeout), because then too nothing said what the removal closes; the strict case (both refused) is a subset.
   - *Tests:* "Confirm says when what a removal closes could not be checked" (both refused, with the removal saved; a conflict; and not shown when the dry run answered, when the preview answered, or when nothing is removed).
8. **Accessibility.**
   - *Change:* the Impact result area is `aria-live="polite"` (`impact-result`) with `aria-busy` while a read is in flight; the failed dry-run bar has `role="alert"`.
   - *Tests:* "the Impact result area is a live region" (two tests).
9. **Tests.**
   - **9a, firm picked in the panel.** `Impact.test.tsx` "the dry-run URL carries the picked firm's firmId". **Mutation proof:** `queryOf` in `ProceduresPanel.tsx` changed to send `firmId: null`; the test fails (`Expected "0a0a0a0a-0000-4000-8000-0000000000a2"`, `Received: null`); the file was restored.
   - **9b, source guard.** `Wizards.test.tsx` now walks EVERY non-test source file under `src` (not a list), asserts `services/dryRunApi.ts` is the only file naming the dry-run route, and has a must-fire control (two other files naming it are caught) and a must-not-fire control (comments and look-alikes).
   - **9c, flakiness.** `jest.setup.cjs` has `configure({ asyncUtilTimeout: 5000 })`. The doubled `waitFor(Next enabled)` lines in `WizardsChange.test.tsx` are removed: the file had THREE (not four; old lines 130, 336, 439).
   - **9d.** The tests listed above. Beyond the named ones, one line of reason each: the `raceTimeout` tests and the abort-on-close test (the contracts of item 1); the `FALLBACK_STATUSES` test (item 4's closed set).
   - **Test plumbing:** the fake server's default dry run is now a 503 (not a plain `Error`), because a network failure no longer asks the removal preview and the old removal tests rely on the fallback; the harness takes `timeoutMs`; `overridesApi.test.ts` expects the preview read to carry an abort signal.

**Known limits (this round).**
- **K1 (R6, deliberate).** A 503 `procedures.unavailable` for a request-named firm still reads "This preview isn't available for this firm." uac-r2 accepted it: the design note's "R6 accepted" line says a body firm the caller cannot Read gives 503, worded "this preview isn't available for this firm", not as an outage. The server's 503 is deliberately the same for an unreadable firm and a fault. Not changed.
- The "unsupported" plan shape shows no explanation. It is unreachable: `dryRunApi.test.ts` "every plan the two wizards make over the fixtures has a dry-run equivalent" runs every fixture row and choice.
- A change of the reason only reads "The lists read the same." (the lists do not differ).
- A 200 whose body is not JSON is worded "Check your connection." (it is read as a failure with no status).

**Verification (round 1):**
- `npm run typecheck`: surface-owned 0 (98 pre-existing errors in the shared libs).
- `npm test`: 17 suites, 392/392 (365 before, plus 27 new).
- Parallel runs: 3 of 3 green at 392/392 (106 s, 75 s, 58 s). `--runInBand` runs: 3 of 3 green at 392/392 (253 s, 235 s, 207 s). `asyncUtilTimeout: 5000` removes K-6 of the first round.
- `npm run build`: OK.
- Mutations (each applied, run, restored): `queryOf` sending `firmId: null`: 1 fails. `asksRemovalPreview` always true: 5 fail. Both that and the abort-on-close removed: 6 fail (so the abort mutation alone: 1).
- No server file changed, so no `dotnet build`.


### For task 112 (ui-tests after deploy)
- **Impact of a pin:**
  - On a matter, open a firm Procedure, choose Pin a version, and reach Impact.
  - Expect Now → If saved now with the Procedures and rules that change, and the affected open invoice count (or "can't be counted").
  - "The firms reached" is not shown (K-2).
- **Removal warning (section 15, D-170):**
  - Remove a changed-rule override whose rule has open findings ("Run the published rule" on a rule changed here), and reach Impact.
  - Expect the "Open findings close" bar with the server's section 15 text and the count.
- **Refusal:**
  - As a caller who cannot Read a firm, exclude a rule for that firm.
  - Expect "This preview isn't available for this firm.", and Confirm and the save still work.
- **Assign Procedure:** not testable here; moved to 084d-5.
- **Dark mode (ADR-021):** repeat the first test in the dark theme. Every colour comes from Fluent v9 tokens; no colour literal was added.

### 084d-4 close (2026-11-13)

Merged into work at `cd869e3050`: `029428af92` (build) + `7af227b4c8` (review round 1).

- Reviews: Opus adversarial pass (9 F-class findings, all fixed in round 1); Sonnet scoped re-check of the fix diff: PASS.
- Known limits from the re-check, accepted:
  - K1: `previewLines` (084b's removal-preview fallback) still composes "N open invoice(s) you can see is/are affected." when `affectedText` is absent; the server always sends it (`ProcedureAssignmentService.cs` ~1391), so the branch is dead on real paths.
  - K2: the dry-run answer cache grows with distinct proposals on one row; reset on row change.
  - K3: the "closing abandons the read" test's console spy cannot fail; the `signal.aborted` assertions carry it.
  - K4: 80 ms real timers in `Impact.test.tsx` could flake under heavy load; raise to 200 ms or more if they do.
  - K5: the 20 s timeout also covers the token step; the worst-case Next hold is 40 s (a dry-run 503 after 20 s, then the fallback).
  - K6: the source guard's comment stripper is approximate.
- The Assign Procedure wizard is NOT in 084d-4; it moved to 084d-5 (no route lists assignable Procedures).

## 084d-5a

The server half of 084d-5: the route that lists the Procedures a record could be assigned, and 089's assign refusing a non-Billing Procedure. Spec: `notes/084d-dry-run-design.md` "084d-5 design" and "084d-5 design: answers and binding conditions (2026-11-13)" (EB-D-227..230, ontology-r1 conditions 1-3, uac-r2 Q2, Q3 and notes 1-5). From `3a1d15a50d`. The client wizard is 084d-5b; nothing under `src/solutions` changed.

**Commits.** `ca8d1f01ea` fix: 089 assign refuses a non-Billing Procedure (EB-D-228). `4dac394f2a` feat: the assignable route.

**Placement.** No new service, DI registration, package or file under `src/server`:
- The candidate query, the own-row read and the ONE plan sit on `ProcedureAssignmentService`, beside the assign write whose checks and row builder they share.
- The Now / After read, the projection and the one batched check sit on `RecordProcedureReadService`, beside `DryRunAsync`, which already holds the overlay, the synthetic-row load and the exemption.
- The three routes sit in `RecordProcedureEndpoints` beside the dry run. Their filters come from `ProcedureAssignmentEndpoints` declarations.
- Nothing under `Services/Signals` changed: the resolver decides applicability, the published window, the version and the firm's final say.

**What was built:**
- `GET /api/finance/{matters|projects|workassignments}/{recordId}/procedures/assignable?firmId=&date=&q=`, one literal route per core table (`RecordProcedureEndpoints.cs:92`, `:129`, `:166`). Filters, in order:
  1. Step 0 on the group.
  2. `DryRunRecordTargets` (finance.read + `RecordWriteCheck`), the uniform 404.
  3. `AssignableTargets` = `AssignOrExcludePrivileges()` = `Admin()` (403; `ProcedureAssignmentEndpoints.cs:519`).
  4. `RequireEbillingEnabled`.
  5. The handler `Assignable` (`RecordProcedureEndpoints.cs:245`): 401 for a non-GUID oid, `TryParseQuery`, `TryParseNameSearch` (`:273`), then the service.
- `ProcedureAssignmentService.PlanAssignCatalogAsync` (`:1645`):
  - Candidates are Status Active, `statecode` 0, Area Billing, and Firm null OR the list's firm (`:1666`), with an optional escaped `Like` on the name, ordered by name then id.
  - `TopCount = bound + 1 + |onRecord|` (`:1658`); past the bound, `AssignableCatalog.Over` (`:1682`).
  - Own rows of any state come from `OwnRowQuery` (`:2218`, which is now also the duplicate refusal's query in `HasRowAsync`).
  - Each other candidate gets the write's Follow-latest image (`ProcedureRowOf` `:478`, the image-only builder), all in ONE `ProcedureOverridePlan`.
- `AssignRefusalOf` (`:457`): the write's Procedure-row refusals (404 not found, 409 retired, 409 not billing), shared by the write and the list. `RecordContainerResolver.EscapeForLike`, the one shared Like escaper (round 1): `%`, `_` and `[` become `[%]`, `[_]`, `[[]`.
- `RecordProcedureReadService.ListAssignableAsync` (`:697`):
  - Now = `ReadAsync` + `BuildProcedures`, as `ListProceduresAsync`.
  - The catalog through a `WriteRefusingEntityService`.
  - On tooMany, only the check over Now's exposed rows.
  - Otherwise, own rows are exposed with their Procedures. `SyntheticRowsAsync(plan)` runs app-only on the real service before any exemption (`:742`). After = `ReadAsync` over `ProcedureRowOverlay(plan, stamp)` with the plan's rows as proposed rows. Offered = After − Now (`:762`), ordered by name (ordinal) then id.
  - ONE `CallerCanReadAllAsync` (`:773`). Any refusal or fault is a 503.
- DTOs: `RecordAssignableProceduresView { record, date, firm, firmSource, q, bound, tooMany, procedures|null, notOffered[{id,name,reason}], label }`, `AssignableProcedureItem { id, name, type, appliesTo, firm, statusIfAssigned, versionInForce, gap }`, `AssignableGapView { kind, text, warning }`. `label` is "If assigned now"; `reason` is `OwnRow`. There are no pin versions.
- Ledger: `RecordProcedureEndpoints.cs`'s reason now names the three GETs; the file count stays 153.

**§11 justification (one line each):**
- `PlanAssignCatalogAsync`. Existing: `PlanAssignAsync` plans one named Procedure. Extension: it is that planner fanned out over a query, reusing `AssignRefusalOf`, `ProcedureRowOf` and the image builder. Without it: no route can list what is assignable (the 084d-4 STOP).
- `AssignRefusalOf` / `ProcedureRowOf`. Existing: the inline checks and constructor in `PlanAssignmentRowAsync`. Extension: pulled out unchanged (pure modification). Without it: the list and the write could disagree on what is refused.
- `OwnRowQuery`. Existing: `HasRowAsync`'s inline query. Extension: the same predicate for many ids. Without it: an own-row candidate would be planned onto an id the write refuses as a duplicate.
- `RecordContainerResolver.EscapeForLike` and `MaxNameSearchChars`. Existing: `RecordContainerResolver.EscapeForLike` (the Like escaper the container lookups use; round 1 corrected this line, which had said there was none in the BFF, and removed the duplicate `EscapeLike` this task had added). Extension: it is now `internal` and called from the planner. Without it: `q` of `%` or `_` matches everything (uac-r2 note 4).
- `AssignableCatalog` / `AssignableOwnRow`. Existing: `ProcedureOverridePlan` holds a plan only. Without it: the tooMany state and the own rows have no carrier.
- `ListAssignableAsync` and the four DTOs. Existing: `DryRunAsync` (one proposal) and `ListProceduresAsync`. Extension: the same reader, overlay and check, with one plan holding many rows. Without it: no answer shape for the wizard.
- `AssignableTargets` and the three routes. Existing: `DryRunTargets` (body-driven). Extension: no body to read here, so it states the assign write's privileges directly. Without it: the list would offer what the caller cannot assign (uac-r2 Q3).

**Conditions → code → tests** (`RecordAssignableProceduresContractTests.cs`, line numbers at the commit):

| Condition | Code | Test |
|---|---|---|
| EB-D-227: at most 50, stable order, null + tooMany past it | `PlanAssignCatalogAsync` `:1658`, `:1682`; `ListAssignableAsync` `:762` | `Bound_At50_And51` (419), `Order_IsByNameThenId` (466) |
| EB-D-228: non-Billing refused; Draft-only allowed | `AssignRefusalOf` `:457` | commit 1 tests; parity (841) asserts the 409 `procedure_not_billing` is seen and that Draft-only is written but never gained |
| EB-D-229: the list follows the panel's firm | `:1666`; Now's `FirmId` passed in | `Firm_NarrowsTheCandidates` (363) |
| EB-D-230: Gap offered with a warning | `GapWarning`, projection | `Gap_IsOffered_WithItsWarning` (268) |
| ontology-r1 1: Area/Firm/Billing constants; firm = resolver's | `:1662-1672` | `FirmPreFilter_IsTheResolversFirmCondition` (392), which pins `FirmConditionHolds` by reflection |
| ontology-r1 2: order, truncated flag, parity over the bound | as above | `Parity_OfferedIffTheRealAssignAddsIt` (841), 14 cases, 2 past the bound |
| ontology-r1 3: unpublished drops through the resolver | no publication condition in the query | `Unpublished_IsPlanned_ButNotOffered` (292) |
| uac-r2 Q2: an unreadable candidate is a 503. SUPERSEDED 2026-11-13: dropped before judging (round 1, item 5) | the one check `:773` | `DeniedCandidate_IsDropped_NotA503` and the byte-identity tests (round 1) |
| uac-r2 Q3: `RecordWriteCheck` + `Admin()` after finance.read | routes; `AssignableTargets` | `NoAdminPrivilege_Is403` (586, every route), `Filters_AreTheWritesDeclarations` (600) |
| uac-r2 note 1: tooMany still asks Now and the firm | `:773` runs on both paths | `TooMany_StillAsksNowRows` (439), `DeniedNowRow_OrFault_Is503` (638) |
| uac-r2 note 2: OwnRow asks the row and its Procedure, inactive too | `:735`; `OwnRowQuery` has no state filter | `OwnRow_IncludingInactive_IsNotOffered_AndAsked` (331) |
| uac-r2 note 3: one 404 for unknown, unreadable, unwritable and step 0, before 403, no read | `DryRunRecordTargets` first | `RecordFilter_IsTheUniform404_BeforeAny403` (542, every route), `StepZero_RefusesFirst_WithTheUniform404` (564) |
| uac-r2 note 4: `q` escaped; control characters, empty or over-long give 400 | `RecordContainerResolver.EscapeForLike`; `TryParseNameSearch` | `Q_MatchesTheNameAsTyped` (491), `BadQ_Is400` (513), `Q_AtTheLimit_IsAccepted_AndOtherBadInputIs400` (523) |
| uac-r2 note 5: ledger names the three GETs; count 153 | Ledger.cs | ArchTests |
| EB-D-218 guards | — | `Every200Guid_WasAskedOfTheCaller_Assignable` (713), `EveryVersionRow_IsAsked_OrTheBodyDoesNotDependOnIt_Assignable` (736) |

**Test support.**
- `RecordProcedureRig.AddProcedure` now seeds `statecode` 0, as a stored row has it.
- `SecureChildShareWorld`'s `Like` now understands `[...]` sets, so `[%]` is a literal, as in Dataverse and SQL.

**Mutation proofs.** Each was applied to the product code, built, run against `RecordAssignableProceduresContractTests`, and restored:
- M1, drop the firm narrowing: 2 tests fail (the firm test and the `FirmConditionHolds` pin).
- M2, skip the batched check when tooMany: 1 fails (`TooMany_StillAsksNowRows`).
- M3, drop the `AssignableTargets` filter from the project route: 2 fail (the 403 test and the kill-switch order). The 403 test then became a theory over all three routes.
- M4, offer every planned candidate instead of After − Now: 16 fail (14 parity cases, the unpublished test and the GUID guard).

**Known limits:**
- **K1, finding (pre-existing).** A Procedure deactivated in Dataverse (`statecode` 1) but still Status Active and Billing is applied by the resolver and accepted by the assign write: neither reads `statecode`. The list drops it, by the binding `statecode` 0 condition. It is kept out of the parity seeds and reported, not fixed here: the resolver is ontology-r1's code.
- **K2 (superseded 2026-11-13, round 1 item 5: the padding is gone; see K5).** `TopCount` was `51 + |Procedures on the record|`. A record with more than about 4,949 Procedures in Now would pass Dataverse's 5,000 and get a 503.
- **K3.** The bound counts candidates before the resolver. Draft-only Procedures, or Procedures that would not apply, count toward 50, so `tooMany` can show although fewer than 50 would be offered. With `q` narrowing, the list stays usable.
- **K4.** A Procedure the record has a row for, but that is not a candidate (Retired, another firm, another area), is not in `notOffered`. It is not offered either.

**Deviations from the design:**
- D1: `AssignRefusalOf(procedure)` takes the row only. The own-row (duplicate) check stays where the write has it, after the pin checks, so the write's refusal order is unchanged.
- D2: `gap` carries `warning` beside `{kind, text}` (EB-D-230). A `NotYetInForce` candidate is offered with `gap` null (it is not a gap).

**Gates.**
- BFF build: 0 warnings, 0 errors.
- `RecordAssignableProceduresContractTests`: 56 passed.
- Filtered `Sprk.Bff.Api.Tests` (Finance, Procedure, RecordProcedure, RouteAuthorization, Supersede): 8608 passed, 4 skipped, 0 failed.
- ArchTests: 2570/2570, ledger count 153.
- `dotnet format whitespace --verify-no-changes`: clean on the eight touched code files.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable package. No package was added.
- Publish size: fresh `origin/master` `c2f366bf52` against the branch head `4dac394f2a`, both exported with `git archive` to short paths. `dotnet publish -c Release -r linux-x64`, `Compress-Archive -CompressionLevel Optimal`, 191 files each. Master 42,088,927 bytes, branch 42,089,205 bytes: a delta of +278 bytes.

**For task 112 (ui-tests after deploy).** These run through the 084d-5b wizard; the server route alone has no UI:
- On a matter, open Assign Procedure. The list shows active Billing Procedures not already on the matter, by name, at most 50, labelled "If assigned now".
- Type a name containing `%` or `_`: only names containing it literally match.
- With more than 50, expect "too many" and no list until the search narrows it.
- A Procedure with a hole between versions on the panel date shows the Gap warning.
- A Procedure the matter has an inactive row for shows as not offered (OwnRow).
- As a caller without the billing-admin privileges, expect the 403 state, not a list.

### 084d-5a review round 1 (2026-11-13)

Each item: finding, then change, then test.

**1. (F2) The `+ onRecord.Count` padding in the candidate `TopCount` was untested.**
- Change: none to product code (the padding was right). It is pinned by a test, and see item 5 below, which replaces the padding.
- Test: `Bound_OnRecordRowSortingFirst_NeverMakesAPartialList`. A Procedure on the record that sorts first ("A on the record"), plus 51 candidates: `tooMany` is true and `procedures` is null.
- Mutation `TopCount = bound + 1`: the new test fails (1 failed, 57 passed); restored, 58 passed.

**2. (F2) `EscapeLike` duplicated `RecordContainerResolver.EscapeForLike`.**
- Change: `RecordContainerResolver.EscapeForLike` is now `internal static` (it was `private`) and is the one Like escaper. `ProcedureAssignmentService.EscapeLike` is deleted; the planner calls the shared helper. The two gave identical output (`[`, `%`, `_` each become a one-character class). The §11 line above ("no Like escaper in the BFF") is corrected. No new public surface, no new file.
- Test: `Q_MatchesTheNameAsTyped` (the `%`, `_` and `[` cases) goes through the shared helper and is unchanged. The existing `RecordContainerResolver` tests are unchanged and green (counts under Gates).

**3. (F4) The parity test checked only that a Procedure is offered.**
- Change: test only. When the real write gains P, the offered item's `statusIfAssigned` and `versionInForce` must equal the post-write list item's `Status` and `VersionInForce`, with a guard that the comparison ran. The 14 parity cases compare InForce, Gap and NotYetInForce items.
- New test: `Bound_CountsOnlyCandidates` (50 candidates plus a Retired, another-area, another-firm and an inactive one: not `tooMany`, 50 offered).
- Design §6 mutation seeds (each applied, run, restored):

| Seed | Result |
|---|---|
| Drop the OwnRow step (`candidates.Count < 0` on the own-row query) | 15 fail (OwnRow tests and the parity cases) |
| The query includes Retired (the Active condition removed) | survived the existing tests; added `Bound_CountsOnlyCandidates`, which now fails it (1 failed) |
| No `ExposeVersionsOf` (empty loop) | 1 fails: `EveryVersionRow_IsAsked_OrTheBodyDoesNotDependOnIt_Assignable` |
| Offered from the candidates instead of After - Now | killed in 084d-5a as M4 (16 fail) |

**4. (K4, notes only) EB-D-228 reach.** `AssignRefusalOf` also refuses a Pin, a Procedure-level Exclude and an empty Area, not only a non-Billing Procedure. Name this as a known limit in the PR. It is harmless: the resolver never reads such rows, so they never change what applies.

**5. (uac-r2 ruling, 2026-11-13) Candidates the caller cannot Read are dropped before judging.**
- **Ruling.** uac-r2 Q2 for candidates changes from "503 on an unreadable candidate" to "drop-before-judge". Finding: the candidate query is app-only, so a 503-versus-200 answer, combined with `q`, revealed the names of Procedures the caller cannot Read.
- **Change.**
  - `RecordProcedureReadService.ReadableProceduresAsync`: the existing batched Read check (`RightsOfAsync`, split out of `CallerCanReadAllAsync`, which behaves as before) over the candidate Procedure ids. No impersonation (EB-D-218).
  - `ProcedureAssignmentService.PlanAssignCatalogAsync` takes it as `readable` and drops the unreadable candidates BEFORE the own-row step, the resolver, the 50 bound and `tooMany`. An unreadable Procedure then looks like one that does not exist: no 503, no `tooMany`, no `notOffered` entry, and its own-row rows are never asked.
  - The `+ onRecord.Count` padding is gone. The candidate fetch is not capped before the filter: it asks for every matching row up to `MaxCandidateRows` (1000, named constant). Past it, the answer is `tooMany`.
  - Faults: the candidate check faulting, or refused as a whole (the caller not established, any row unanswered), is 503 `procedures.unavailable`; never "drop all", never an empty 200. Now's rows, own rows and the firm stay all-or-nothing 503.
  - The candidate batch is a second rights call, so the request now makes two (candidates, then Now ∪ After); each is paged at 250 rows as before.
- **Tests** (`RecordAssignableProceduresContractTests`):
  - `UnreadableCandidate_OnlyMatchOfQ_IsByteIdenticalToItsAbsence` (a): two worlds differing only by one unreadable published, Billing, unfirmed, in-reach Procedure matching `q`; the status and body are byte-identical (traceId excluded). Readable control: it is offered.
  - `UnreadableCandidate_DoesNotTipTheBound_ByteIdenticalToItsAbsence` (b): 50 readable + 1 unreadable equals the 50 alone (not `tooMany`, a list of 50). Control: a readable 51st is `tooMany`.
  - `NoPreFilterCap_AndTheCeilingIsTooMany`: 951 unreadable rows sorting first plus 49 readable offer the 49, and 1000 rows are asked; 1001 rows (1000 unreadable) are `tooMany`, with nothing asked but Now.
  - `CandidateReadCheck_FaultOrRefusal_Is503`: a throw, an unanswered row, and an unestablished caller.
  - `NowOwnRowAndFirm_Unreadable_StillAre503`; `DeniedCandidate_IsDropped_NotA503`. `OwnRow_IncludingInactive_IsNotOffered_AndAsked` now expects a dropped own-row Procedure (200, absent) and a denied own ROW as 503.
- **Mutations** (each applied, run against the class, restored; baseline 63 passed):

| Mutation | Result |
|---|---|
| M5a: no drop (judge every candidate) | 5 fail: (a), (b), `DeniedCandidate_IsDropped_NotA503`, the crowd and ceiling test, and the OwnRow test |
| M5b: a pre-filter cap again (`TopCount = bound + 1 + onRecord.Count`) | 1 fails: the crowd and ceiling test |
| M5c: `TopCount = bound + 1` | 2 fail: `Bound_OnRecordRowSortingFirst_NeverMakesAPartialList` and the crowd and ceiling test |
| M5d: a refused check drops all instead of 503 | 1 fails: `CandidateReadCheck_FaultOrRefusal_Is503` |
| M5e: no ceiling answer | 1 fails: the crowd and ceiling test |

- **Known limits (name in the PR).**
  - **K5.** The ceiling is `MaxCandidateRows` = 1000. A matching set past it is answered `tooMany`, which counts rows the caller cannot Read; narrowing by `q` brings it back. Replaces K2's padding limit (K2 is gone: there is no `+ |onRecord|` padding).
  - **K6.** Read-batch timing, accepted: the candidate check asks one Read question per matching candidate (up to 1000, 250 per page, in addition to Now's and After's), so a large catalog with a broad `q` costs time. Measure at task 112; the bound on cost is the ceiling.

**Round 1 gates (final state).** BFF build 0 warnings, 0 errors. Filtered `Sprk.Bff.Api.Tests` (Finance, Procedure, RecordProcedure, RouteAuthorization, Supersede, RecordContainer, ContainerDocument): 8988 passed, 4 skipped, 0 failed (`RecordContainer` alone: 363 passed, after item 2). `RecordAssignableProceduresContractTests`: 63 passed. ArchTests: 2570/2570. `dotnet format whitespace --verify-no-changes` on the changed files: no finding except ENDOFLINE, which the working tree's LF checkout raises on every line of every file (git converts at commit).
