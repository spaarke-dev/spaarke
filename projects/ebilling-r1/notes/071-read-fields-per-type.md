# Task 071 - Billing rule handlers and the read-field contract (FR-24)

> Rigor FULL. Base `4c1203b42` (070 complete, ontology-r1 re-check clean) on `work/ebilling-r1` `9d24ae7f0`. Executed 2026-10-17.
> Commits: `eccb1fe6f` handlers and dispatch, `1dbfbbf76` Duplicate remark, `2f13d6697` tests and ArchTest guard.

## Result

Eleven pure handlers, a closed dispatch, the fact-set contract (`BillingFactSet`) and the finding contract (`BillingFinding`),
all under `Services/Signals/Billing/` (handlers under `Handlers/`). No file outside that folder changed in `src/`; Ontology
evaluation, task 070's definitions and the catalogue are untouched. **No 070 extension was needed**: policy id and clause
travel in a separate `BillingRuleOrigin`, because `BillingRuleDefinition` (obtainable only through the billing gate) has neither.

Static and concrete: no DI interface, no registration, no package, no csproj change. The ADR-010 ceiling is untouched (166; the
ArchTests ADR-010 test is green).

## Contracts (what 072 builds and 073 reads)

- `BillingRuleHandlers.Evaluate(BillingRuleDefinition, BillingRuleOrigin, BillingFactSet)` returns `IReadOnlyList<BillingFinding>`.
  A rule with no handler, or lacking a parameter its handler needs, throws `BillingRuleRefusedException`
  (`billing.rule.no_handler` / `billing.rule.parameter_missing`). 072 catches it per rule: no finding, the run continues, the
  refusal is logged and metered there. The dispatch is a `switch` over `RuleType` returning method groups; `_ => null` is the
  refusal. A test enumerates every `RuleType` member.
- **Order.** Same input, same findings, same order: invoice-level findings first, then lines in invoice order, within a line
  the order the handler checks. Every handler test runs the rule twice and asserts equality.
- **A handler judges every subject of the invoice.** Rule selection and "which rule version is in force on this line's date" are
  072's: keep the findings whose subject the version covers.
- **`BillingFinding`**: rule type, policy id/code, policy version id, clause reference and location, `Subject` (Invoice, or Line
  with the line index), a stable `ReasonCode` (no prose; firm-visible text is composed downstream), `ValuesRead`,
  `SuggestedAdjustment` (ApprovedRate: Reduce; NonBillableActivity: Disallow; else null), `FactValues` (the `{{token}}` values;
  keys are always within 070's `TemplateFactFields` for the type, enforced when a finding is built, so a handler cannot name an
  outside token), `ReadFields` and `ReadsLowConfidenceValue`.
- **Rounding, stated once** (`BillingMath.ToCent`): money rounds to the cent, a midpoint away from zero (2.345 -> 2.35,
  -2.345 -> -2.35). Money and hours are `decimal`; TimeIncrement uses the exact decimal remainder. Text of numbers and dates is
  invariant-culture (a finding does not depend on the machine's culture; tested under de-DE and tr-TR).

### What 072 must put in the fact set

- `ReliabilityThreshold` (the eBilling setting, default 0.90). A handler uses it only to mark a value, never to decide.
- Per-field confidence maps (`BillingInvoiceFacts.FieldConfidence`, `BillingLineFacts.FieldConfidence`) keyed by
  `BillingFieldNames` (the 046 `ResolutionFieldNames` plus Narrative, the AI codes, the firm-billed codes, header names). **An
  absent key is "unknown", and unknown counts as below the threshold** (NFR-05/06): it is flagged, never trusted.
- `Timekeeper` (046 `TimekeeperResolution`) and `ApprovedRate` (046 `RateResolution`) per fee line, as 046 produced them.
- `PriorSubmissions` for Duplicate: **load only Active attempts' worth of meaning; the handler itself ignores Rejected and
  ReturnedToFirm, and 072 should not load Failed or Cancelled attempts at all** (they never reached the firm; there is no status
  for them in `BillingPriorStatus`). The submission being evaluated is excluded by `SubmissionId`.
- `CoreRecord` (open date, close date, closed). `null` means CoreRecordWindow judges nothing; 072 need not call the rule for a
  submission with no core record.
- Code tables (018) are **not** in the fact set: no handler reads them (NonBillableActivity and ExpenseRule compare the rule's
  own code lists with the AI-assigned codes from 047, which are validated upstream).

## Read-field contract per type (for task 073)

`ReadFields` = one `FieldRef(lineIndex?, field)` per **extracted** value the finding read or displays, whether or not it was the
value that failed (reference values - rates, core-record dates, prior tracking numbers - have no extraction confidence and are
not listed). 073 rejects only when the consequence is Reject, **every** `ReadFields` entry has confidence at or above the
threshold, and the type is reject-eligible (FR-26: catalogue positions 1, 2, 6, 7, 8, 9 (invoice level), 10). The list is
deliberately conservative: a value shown to the firm that is low-confidence stops a rejection.

| # | Type | Subject | ReadFields (extracted) | Reference values (not read) | Reject-eligible (FR-26, 073's call) |
|---|---|---|---|---|---|
| 1 | ApprovedTimekeeper | Line | `TimekeeperAsBilled`, `Date` | `timekeeper.unmatchedReason` | Yes in principle, but never in practice: 046 caps an unmatched timekeeper at 0.80 (D-89), so the finding always reads a low-confidence value |
| 2 | ApprovedRate | Line | `Rate`, `Hours` (only when the reduction is computed), `Date`, `TimekeeperAsBilled` | `approvedRate`, `approvedRate.limit`, `approvedRate.basis`, `.scope`, rate-card and rate ids | Yes |
| 3 | TimeIncrement | Line | `Hours`, `Date`, `TimekeeperAsBilled` | - | No (flag) |
| 4 | DailyHoursCap | Line (first line of the timekeeper-day) | `Hours`, `Date`, `TimekeeperAsBilled` of **every** line in the group | `dailyHours` | No (flag) |
| 5 | NonBillableActivity | Line | `ActivityCode`, `TaskCode` (AI-assigned, with coding confidence), `Amount`, `Date`, `TimekeeperAsBilled` | - | No (flag + suggested disallow) |
| 6 | ExpenseRule | Line | `ExpenseCode` (AI-assigned), `Amount`, `Date` | - | Yes |
| 7 | InvoiceAge | Line | `Date` (line), `InvoiceDate` (header) | - | Yes |
| 8 | CoreRecordWindow | Line | `Date` | `coreRecord.openDate`, `.closeDate`, `.closed` | Yes |
| 9 | Duplicate | Invoice (number) or Line | Invoice: `FirmInvoiceNumber`. Line: `TimekeeperAsBilled`, `Date`, plus `Hours`, `Narrative`, `Amount` when configured, plus `FirmInvoiceNumber` (a template token) | `duplicateOf.trackingNumber`, `duplicateOf.lineIndex` | Invoice level only; a line-level finding never rejects |
| 10 | RequiredData | Invoice or Line | The missing field itself (`FirmInvoiceNumber`, `InvoiceDate`, `BillingPeriodStart/End`; `TimekeeperAsBilled`, `Date`, `Narrative`, `BilledTaskCode`, `BilledActivityCode`, `BilledExpenseCode`), or `Currency` | - | Yes. An absent value with no known confidence is flagged: a field the extraction merely failed to find cannot reject (NFR-06) |
| 11 | AiObservation | Line | `Observation.BlockBilling` or `Observation.VagueDescription` (the observation's own confidence), `Date` | - | No: flag only (I-5) |

## Behaviour per type (fires when / consequence)

Reason codes are in `BillingReasonCodes`. "Consequence" is the policy version's, set by the rule set (D-88, FR-26) - never decided here.

1. **ApprovedTimekeeper** (FR-23, D-89). Fires on every **fee** line whose 046 roster match produced no timekeeper, whatever the
   reason; the reason code mirrors 046's `TimekeeperUnmatchedReason` (`unmatched`, `name_missing`, `line_date_missing`,
   `name_incomplete`, `not_on_roster`, `not_approved_on_line_date`, `ambiguous`, `ai_below_threshold`, `ai_not_clearly_ahead`,
   `ai_unavailable`). The confidence is reported unchanged (046's 0.80 cap), so the rule only holds. Nothing maps an unmatched
   name to another timekeeper. Expense lines are skipped.
2. **ApprovedRate.** Limit = `ToCent(approved x (1 + tolerancePercent/100))`; fires when billed rate is **above** the limit
   (equal holds). Suggested adjustment: Reduce by `ToCent((billed - approved) x hours)`; null amount when hours are missing or
   not positive. When 046 found no rate in force / several that disagree / could not look one up, it fires with a distinct reason
   (`no_rate_in_force`, `ambiguous`, `unresolved`, `timekeeper_unmatched`, `line_date_missing`) and suggests nothing. A fee line
   billing no hourly rate is not judged.
3. **TimeIncrement.** Fee line with hours where `hours % incrementHours != 0` (exact decimal; credits judged too). Flag only.
4. **DailyHoursCap.** Fee lines with a date and hours, grouped by (timekeeper, date) within the invoice; the timekeeper is the
   roster match, else the normalised billed name. A group total **above** the cap fires once, on the group's first line. Flag only.
5. **NonBillableActivity.** The AI-assigned activity or task code is in `codes` (case-insensitive), whatever its confidence;
   low confidence is carried and flagged (NFR-05). Suggested adjustment: Disallow, amount = the rounded line amount (null if none).
6. **ExpenseRule.** Expense lines only. `code_not_reimbursable` when the AI expense code is in `nonReimbursableCodes`;
   `amount_over_cap` when the line amount is above `maxAmountPerItem` (item = the expense line; equal holds). A line breaking both gets both, code first.
7. **InvoiceAge.** Fires when `invoiceDate - lineDate` is more than `maxAgeDays` days (exactly N holds). Nothing is judged without an invoice date.
8. **CoreRecordWindow.** Per line: `before_open` (date < open - graceDays), else `after_close` (date > close + graceDays), else
   `core_record_closed` when the record is closed and has no close date. Nothing is judged with no core record.
9. **Duplicate** (FR-36, D-88). Invoice level (when `firmInvoiceNumber` is a match field): the number (whitespace/case ignored)
   equals an **Active** earlier attempt's; Rejected and ReturnedToFirm attempts and the submission itself are ignored; cites the
   lowest-sequence original. Line level (the other match fields): a line whose configured fields all equal an earlier line in an
   active prior submission (`line_prior_submission`) or earlier in this invoice (`line_same_invoice`); the repeat is flagged,
   not the original. A line missing a configured field is not compared.
10. **RequiredData** (FR-21). One finding per missing listed field per subject (`required_data.missing_field`, the field name in
    `missingField`); whitespace is missing; the codes are the **firm-billed** ones. Currency: whenever the rule runs, a read
    currency that is not `USD` (case-insensitive; `$` is not USD) is one invoice-level finding, `currency_not_usd`,
    `missingField` = `currency` - a finding, never an exception, and independent of the listed fields. An unread currency is not flagged.
11. **AiObservation** (I-5). One finding per line for the highest-confidence observation of the configured kind with confidence
    **at or above** `minConfidence` (exactly at holds). The observation's own confidence is the value's confidence and is flagged
    below the reliability threshold (an observation of 0.86 passes a 0.85 rule but is below 0.90).

## Judgment calls and additions beyond the contract (owner-visible)

None blocked execution (no escalation trigger fired). Each is one line to change.

1. **ApprovedRate reduction is to the approved rate, not the tolerance limit** ("adjust-to-approved", research note). The
   tolerance is slack before the rule fires, not a rate the firm is entitled to. Alternative: reduce to the limit.
2. **No approved rate** (not found / ambiguous / unresolved) fires with a reason and no adjustment, following D-47's
   reference-missing cap, so such a finding holds. Alternative: not judge the line.
3. **CoreRecordWindow "or core record closed"**: a closed record with a close date is judged by that date; closed with no close
   date flags every line (`core_record_closed`). (Beyond a bare date window; needed for "or core record closed".)
4. **ApprovedTimekeeper "(and core record)"** is judged at the firm level only: the 017 roster has no core-record scope, so 046
   resolves at firm and line date. A per-core-record roster would extend the resolution, not this handler.
5. **Duplicate same-invoice repeat** (`line_same_invoice`) is beyond the strict POML (prior submissions only). Justification:
   "a line repeats an earlier line" in design.md 6.1 is checkable within the invoice with no I/O, and a double-billed line on one invoice is the common case.
6. **Currency check is always on** when RequiredData runs. 070's `fields` vocabulary has no currency member and I did not
   extend it; a foreign-currency invoice is unsupported in R1 (D-20) however the rule is configured.
7. **AI codes drive NonBillable and ExpenseRule** (validated, with coding confidence); **firm-billed codes drive RequiredData** (what the invoice carries).
8. **No `BillingScope` parameter**: handlers judge every subject; 072 filters by the line date the version is in force for (YAGNI, section 11).
9. `ReadFields` includes every extracted value a finding shows or uses (conservative; see above).

## Component justification (CLAUDE.md section 11)

- **Existing**: `SignalEvaluator` (FetchXML over stored rows) and Finance `SignalEvaluationService` (spend signals) - neither judges an in-memory invoice (D-23). 046's resolution types, 047's `InvoiceLineObservation` and `InvoiceLineType` are reused as-is.
- **Extension**: not possible for the evaluators (different input and output); reused where possible.
- **Cost of doing nothing**: no billing rule can be evaluated at submission, so no finding, flag or rejection exists (FR-24, FR-25, FR-26 fail).
- New surface is internal static code; no DI, no endpoint, no package, no Dataverse column. Public surface: `BillingRuleHandlers`, `BillingRuleRefusedException`, `BillingFactSet` and its parts, `BillingFinding` and its parts (072/073 need them).

## Known limits (accepted, no action)

- (K1) Duplicate compares a rostered line and an unrostered line as different people even when the billed names are equal; the unrostered line is already an ApprovedTimekeeper finding, and a line-level duplicate never rejects.
- (K2) DailyHoursCap is per invoice (R1; design.md 6.1).
- (K3) `EmptyConfidence.Instance` is a shared empty dictionary typed read-only; the purity guard's static-state check covers the handler namespace, not the contracts.

## Tests

`tests/unit/domain/Signals/Billing/Handlers/` (Maintain-class, ADR-038: pure, no mocks, compiled into `Sprk.Bff.Api.Tests`), namespace `Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers`:

- **252 new unit tests** (all pass): one class per type, `BillingRuleHandlersDispatchTests` (enumerates every `RuleType`; non-billing and unknown values refused with `no_handler`; missing parameters refused; token contract; all 11 fire on a fact set that breaks every rule and none fires on a clean one), `BillingMathTests`. Shared `BillingHandlerKit.Run` asserts the per-finding contract and determinism for every finding of every test. Theory data uses text for decimals (no double round trip).
- **20 new ArchTests** (`tests/Spaarke.ArchTests/BillingHandlerPurityGuardTests.cs`): the handlers and contracts reference no I/O, network, clock, randomness, environment, logging, reflection, expression trees, `Activator`/`Type` late binding; no static registry (only readonly `string[]` tables); the dispatch builds no `Dictionary`/`List`/`HashSet`. 14 must-fire controls (one per forbidden shape), a registry control, a must-not-fire control, and a non-vacuity check. Acceptance greps: no `HttpClient`, `TimeProvider`, `DateTime.Now`, `IGenericEntityService`, reflection or registry in `Handlers/` (the guard covers the compiled IL, stronger than a text grep).
- Failed before / pass after: the handler types did not exist before, so the tests cannot run on the old tree (compile failure). The equivalent proof is the mutation table below: 14 of 14 production mutations are killed.
- Run: new Handlers classes 252 passed / 0 failed; `Sprk.Bff.Api.Tests` filter `Billing|RuleType|PolicyVersion|Signal` 2566 passed, 3 skipped (pre-existing live-environment skips), 0 failed; `Spaarke.ArchTests` full project 1146 passed, 0 failed (includes the new 20 and the ADR-010 ceiling). The whole unit project was not run (45+ min, per the brief). Build: `dotnet build src/server/api/Sprk.Bff.Api/ -m:1` 0 warnings, 0 errors.

### Mutation proofs (production check broken, must-fire/boundary test fails, then restored with `git checkout`)

| # | Mutation | Result (tests failing) | Example killer |
|---|---|---|---|
| M01 | ApprovedRate boundary `billed > limit` -> `>=` | 7 | `MustNotFire_WhenTheBilledRateEqualsTheApprovedRate` |
| M02 | ApprovedRate limit not rounded to the cent | 2 | `TheLimitIsRoundedToTheCent_MidpointAwayFromZero` |
| M03 | TimeIncrement decimal remainder -> `double` remainder | 6 | `MustNotFire_ForAnExactMultiple(0.1, 0.3)` |
| M04 | DailyHoursCap `total <= cap` -> `<` | 1 | `MustNotFire_AtExactlyTheCap` |
| M05 | Duplicate (FR-36): Rejected/Returned priors no longer ignored | 5 | `MustNotFire_ForARejectedOrReturnedEarlierAttempt_OfTheSameNumber_FR36` |
| M06 | NFR-05: below-threshold flag never set | 12 | `AnObservationAboveTheRulesThreshold_ButBelowTheReliabilityThreshold_IsFlagged_...` |
| M07 | NFR-05: unknown confidence not flagged | 3 | `ARateWithNoKnownConfidence_IsMarkedUnreliable` |
| M08 | FR-21: currency check disabled | 6 | `MustFire_ForACurrencyOtherThanUsd_AsAFindingOnTheInvoice_FR21` |
| M09 | FR-23/D-89: unmatched timekeeper no longer fires | 18 | `AnUnmatchedTimekeeper_OnlyHolds_ItCarries046sCappedConfidence_AndIsMarkedUnreliable_D89` |
| M10 | Dispatch: `Duplicate` case removed | 33 | `EveryRuleTypeMember_IsABillingTypeWithAHandlerAndASampleBody_OrIsRefusedFailClosed` |
| M11 | InvoiceAge `<= maxAgeDays` -> `<` | 1 | `MustNotFire_ForALineDatedExactlyNDaysBeforeTheInvoice` |
| M12 | CoreRecordWindow: grace days not applied after close | 1 | `TheGraceDaysWidenTheWindowOnBothSides(7, 7, false)` |
| M13 | Token contract: out-of-set token no longer refused | 1 | `AHandlerThatNamesATokenOutsideItsTypesFactFieldSet_Fails_...` |
| M14 | Purity: a handler reads `DateTime.UtcNow` | 1 (ArchTest) | `HandlersReferenceNothingImpure` |

(Each mutation was applied to the committed file, the test project rebuilt, the Handlers namespace run, then the file restored
with `git checkout -- <file>` and touched so the incremental build could not reuse the seeded DLL. The tree was clean after.)

## BFF hygiene (CLAUDE.md section 10)

- Placement: in the BFF, `Services/Signals/Billing/` (as 070); pure in-process domain code, no endpoint, no DI, no background work.
- **Publish size (NFR-01)**: fresh `git archive` exports at short paths (`C:\w71b` = `4c1203b42`, `C:\w71h` = `1dbfbbf76`, the final production tree).
  Command `dotnet publish src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -c Release -r linux-x64 --self-contained false -p:NuGetAudit=false`
  (framework-dependent linux-x64, Release, **PDBs included**), zipped with `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`,
  **192 files on both sides**.

  | Tree | Zip bytes | MB |
  |---|---|---|
  | base `4c1203b42` | 39,462,574 | 37.63 |
  | task 071 `1dbfbbf76` | 39,487,585 | 37.66 |
  | **Delta** | **+25,011** | **+0.024 MB** |

  Well under the +5 MB trigger, 55 MB review and 60 MB ceiling. (The absolute figure is lower than the recorded 44.96 baseline; baselines age and this is measured, not recorded.)
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages. No package or csproj change.
- ADR-010: no DI registration; the ceiling (166) is unchanged and its ArchTest is green.

## ontology-r1 review

No Ontology-owned file is touched (new files only, under `Services/Signals/Billing/`; `RuleType.cs`, `PolicyVersionValidator.cs`,
`PredicateCompiler.cs`, the Signals jobs and their tests are unchanged), and the Signals tests (the `Signal` filter) pass.
**Not obtained**: the executing agent cannot message other sessions. Main session: send ontology-r1 the commit range
`4c1203b42..2f13d6697` (production under `Services/Signals/Billing/` only) and paste the answer here. Question for them: do the new
public names (`BillingFactSet`, `BillingFinding`, `BillingFieldNames`, `BillingReasonCodes`, `BillingRuleHandlers`) collide with
anything planned on `docs/ontology-platform-design`?

## Open questions for the owner (recommendations in brackets)

1. ApprovedRate reduction: to the approved rate (as built) or to the tolerance limit? [As built.]
2. No approved rate found: flag with no adjustment (as built) or do not judge? [As built; such a finding holds by D-47.]
3. Is a same-invoice repeat line a Duplicate (as built, beyond the strict POML)? [Keep.]
4. Should RequiredData's `fields` vocabulary gain a `currency` member (070 schema change), or is the always-on currency check right for R1? [Always-on for R1; revisit when a second currency is supported.]

## Pre-commit hook note

The repo's lint-staged pre-commit hook backs up working-tree state with a `git stash` internally during each commit and restores
it; I ran no `git stash` myself, and the stack held only other sessions' entries afterwards.
