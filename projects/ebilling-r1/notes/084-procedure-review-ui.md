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
