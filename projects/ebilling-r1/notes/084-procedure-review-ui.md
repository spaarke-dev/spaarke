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
- Statuses: 200; 400 (bad query); 404 (step 0, unknown or unreadable record: one byte-identical body); 503 `ebilling.disabled`; 503 `procedures.unavailable` (below). Nothing is written.

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
| 503, never a partial list | `OneUnavailableThing_Is503_WithNoList` (23 cases: unreadable parent, own record, Procedure, version, override row, creator, firm, rule, rule set, policy version; faulted check; unanswered row; unestablished caller; faulted Procedure / override / policy read; resolver fault), with `SameSeed_NothingRefused_Is200` as the must-not-fire control |
| Nothing written | `Lists_WriteNothing` (strict mock without Create; Create, Upsert, Update, Delete verified never) |
| Ledger | `RouteAuthorizationGuardTests` (ArchTests): `GovernedFile` entry and count 152 -> 153 |

Mutation check: with the rights check neutered (unreadable and unanswered rows allowed) 19 of the 503 cases fail, and with `Stale` neutered the stale test fails.

## Build, tests, publish size, CVE

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 0 warnings.
- Affected unit suites (Finance, Signals, Procedure, RecordProcedure filters): 11,900 passed, 0 failed, 10 skipped (pre-existing skips). New tests: 119 (RecordProcedure filter).
- `tests/Spaarke.ArchTests/` (full run): 2,505 passed, 0 failed. The first run failed two route-guard tests (the scanner needs a file-local `const` for the group path); fixed before the commit.
- Publish size (BFF hygiene item 4, NFR-01): command `dotnet publish -c Release -r linux-x64 --self-contained false src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`; zip `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`; PDBs included; both from fresh worktrees at `C:\wt084m` (master `a654da59c4`) and `C:\wt084b` (branch head `3002ba81db`, master + 12 commits); 191 files each.
  - master 41,980,644 bytes (40.04 MiB); branch 42,018,803 bytes (40.07 MiB); delta +38,159 bytes (+0.04 MiB). No package added. Both temp worktrees removed with `Remove-WorktreeSafe.ps1`.
- `dotnet list src/server/api/Sprk.Bff.Api package --vulnerable --include-transitive`: no vulnerable packages.
