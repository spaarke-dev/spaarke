# Task 071 - Billing rule handlers and the read-field contract (FR-24)

> Rigor FULL. Base `4c1203b42` (070 complete, ontology-r1 re-check clean) on `work/ebilling-r1` `9d24ae7f0`; the branch was later rebased, and the first 071 commit sits on `d4e555e38`. Executed 2026-10-17.
> Commits (range `d4e555e38..d22c8e26d`): `6b99e980f` handlers and dispatch, `ef8d87819` Duplicate remark, `468955dfa` tests and ArchTest guard,
> `d22c8e26d` these notes. (Before the rebase these were `eccb1fe6f`, `1dbfbbf76`, `2f13d6697`; those hashes are no longer on the branch.)
> **Review fix round 1** (adversarial review of 071; owner decision D-105): code and tests `11b7c3abf..6cb6dbed3` on base `eacad6b21`, then the notes commit `4257e7f35`. Listed in "Review fix round 1" below. (The hashes first written here, `1275f38ae..7764c2584`, were pre-rebase and are not on the branch; verified with `git log --oneline -- <handler paths>` on `work/ebilling-r1`.)
> **Fix round 2** (ontology-r1's review at `4257e7f35`; owner decisions D-111, D-112): see "Fix round 2" below.

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
- **Credit lines are not charges (D-105).** A credit line is a line with negative hours or a negative amount, defined once in
  `BillingMath.IsCreditLine`. ApprovedRate, ExpenseRule, InvoiceAge and CoreRecordWindow skip it entirely; NonBillableActivity
  still raises its finding on it (the reviewer sees the credit) but with **no** suggested adjustment (never a Disallow of a
  negative amount). So 072/073 can see no finding at all for a credit line from those four types, and a NonBillableActivity
  finding with `SuggestedAdjustment == null` is a credit. The other types (TimeIncrement, DailyHoursCap, Duplicate,
  RequiredData, AiObservation, ApprovedTimekeeper) are not in D-105 and still judge credit lines (see the owner question below).
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
  for them in `BillingPriorStatus`). The submission being evaluated is excluded by `SubmissionId` (a required, non-nullable Guid on
  `BillingInvoiceFacts`). **Priors come from both sides of the secure wall (D-112)**, and 072 sets `SecureRoots` on each prior and on
  the invoice (required `IReadOnlySet<Guid>?`: empty = ordinary, `null` = could not be read) app-only from
  `SecureChildShareSynchronizer`'s secure-root walk. The walk starts from CHILD rows, never a root table (`SecureRootsAboveAsync`
  throws for a root table, so a core record is never passed itself): a prior's `sprk_invoice` and `sprk_invoicesubmission` rows, their
  sets unioned (072 review A1), and the judged invoice as an unsaved `sprk_invoice` probe. Failed, NotApplicable and Undetermined are
  handled per uac-r2's calling conditions: Failed (ambiguous Secure owner team) or a fault means every set is unresolved; NotApplicable
  (no Secure owner team) means every set is empty; an Undetermined row, or a Secure-owned row whose lookups reach no secure root, leaves
  its side unresolved. 071 does not call it.
- `CoreRecord` (open date, close date, closed). `null` means CoreRecordWindow judges nothing; 072 need not call the rule for a
  submission with no core record.
- Code tables (018) are **not** in the fact set: no handler reads them (NonBillableActivity and ExpenseRule compare the rule's
  own code lists with the AI-assigned codes from 047, which are validated upstream).

## Read-field contract per type (for task 073)

`ReadFields` = one `FieldRef(lineIndex?, field)` per **extracted** value the finding read or displays, whether or not it was the
value that failed (reference values - rates, core-record dates, prior tracking numbers - have no extraction confidence and are
not listed). 073 rejects only when the consequence is Reject, **every** `ReadFields` entry has confidence at or above the
threshold, and the type is reject-eligible (FR-26: catalogue positions 1, 2, 6, 7, 8, 9 (invoice level), 10). The list is
deliberately conservative: a value shown to the firm that is low-confidence stops a rejection. **073 must not infer read fields**:
they are exactly what the finding lists. So a finding built after the credit test (`IsCreditLine`, D-105) also lists the Hours and
Amount that test read, appended after the finding's own values by `HandlerContext.WithCreditTestReads` (fix round 2): a reversal
printed "(1,437.50)" and extracted as +1437.50 at confidence 0.50 is judged as a charge, and its finding carries that 0.50.

| # | Type | Subject | ReadFields (extracted) | Reference values (not read) | Reject-eligible (FR-26, 073's call) |
|---|---|---|---|---|---|
| 1 | ApprovedTimekeeper | Line | `TimekeeperAsBilled`, `Date` | `timekeeper.unmatchedReason` | Not in practice (D-89 "holds, never auto-rejects"). Today that rests on 046's 0.80 cap staying below the 0.90 threshold, which the handler does not enforce; **task 073 enforces D-89** (refuses a Reject consequence for ApprovedTimekeeper at publish, hold-only in the rejection rule, validates `ReferenceMissingCap < ReliabilityThreshold` in settings) |
| 2 | ApprovedRate | Line | `Rate`, `Hours` (the reduction uses it, and the credit test reads it whenever the line has hours, including zero: fix round 2), `Date`, `TimekeeperAsBilled`, then `Amount` (the credit test; fix round 2). Hours or Amount the line does not have is not read | `approvedRate`, `approvedRate.limit`, `approvedRate.basis`, `.scope`, rate-card and rate ids | Yes |
| 3 | TimeIncrement | Line | `Hours`, `Date`, `TimekeeperAsBilled` | - | No (flag) |
| 4 | DailyHoursCap | Line (first line of the timekeeper-day) | `Hours`, `Date`, `TimekeeperAsBilled` of **every** line in the group | `dailyHours` | No (flag) |
| 5 | NonBillableActivity | Line | `ActivityCode`, `TaskCode` (AI-assigned, with coding confidence), `Amount`, `Date`, `TimekeeperAsBilled`, then `Hours` when the line has them (the credit test that decides the suggested disallow; fix round 2) | - | No (flag + suggested disallow) |
| 6 | ExpenseRule | Line | `ExpenseCode` (AI-assigned), `Amount`, `Date`, then `Hours` when the line has them (the credit test; fix round 2) | - | Yes |
| 7 | InvoiceAge | Line | `Date` (line), `InvoiceDate` (header), then the line's `Hours` and `Amount` when it has them (the credit test; fix round 2) | - | Yes |
| 8 | CoreRecordWindow | Line | `Date`, then the line's `Hours` and `Amount` when it has them (the credit test; fix round 2) | `coreRecord.openDate`, `.closeDate`, `.closed` | Yes |
| 9 | Duplicate | Invoice (number) or Line | Invoice: `FirmInvoiceNumber`. Line: `TimekeeperAsBilled`, `Date`, plus `Hours`, `Narrative`, `Amount` when configured, plus `FirmInvoiceNumber` (a template token). **A withheld finding (D-112, `DetailWithheld`) lists the same fields with the value text left out** | `duplicateOf.trackingNumber`, `duplicateOf.lineIndex` (absent on a withheld finding) | Invoice level only; a line-level finding never rejects |
| 10 | RequiredData | Invoice or Line | The missing field itself (`FirmInvoiceNumber`, `InvoiceDate`, `BillingPeriodStart/End`; `TimekeeperAsBilled`, `Date`, `Narrative`, `BilledTaskCode`, `BilledActivityCode`, `BilledExpenseCode`), or `Currency` | - | Yes. An absent value with no known confidence is flagged: a field the extraction merely failed to find cannot reject (NFR-06) |
| 11 | AiObservation | Line | `Observation.BlockBilling` or `Observation.VagueDescription` (the observation's own confidence), `Date` | - | No: flag only (I-5) |

## Behaviour per type (fires when / consequence)

Reason codes are in `BillingReasonCodes`. "Consequence" is the policy version's, set by the rule set (D-88, FR-26) - never decided here.

1. **ApprovedTimekeeper** (FR-23, D-89). Fires on every **fee** line whose 046 roster match produced no timekeeper, whatever the
   reason; the reason code mirrors 046's `TimekeeperUnmatchedReason` (`unmatched`, `name_missing`, `line_date_missing`,
   `name_incomplete`, `not_on_roster`, `not_approved_on_line_date`, `ambiguous`, `ai_below_threshold`, `ai_not_clearly_ahead`,
   `ai_unavailable`). The confidence is reported unchanged (046's 0.80 cap), so the rule only holds. Nothing maps an unmatched
   name to another timekeeper. Expense lines are skipped.
2. **ApprovedRate.** Limit = `ToCent(approved x (1 + tolerancePercent/100))`; the billed rate is first rounded to the cent
   (`ToCent`), so an extracted 575.004 against 575.00 holds; fires when that rate is **above** the limit (equal holds). A credit
   line (D-105) is skipped. Suggested adjustment: Reduce by `ToCent((billedCent - approved) x hours)`; null amount when hours are
   missing or zero (a zero or absent `Hours` leaves the reduction unknown; `Hours` is still listed as a read field whenever the line has it, because the credit test read it). When 046 found no rate in force / several that disagree / could not look one up, it fires with a distinct reason
   (`no_rate_in_force`, `ambiguous`, `unresolved`, `timekeeper_unmatched`, `line_date_missing`) and suggests nothing. A fee line
   billing no hourly rate is not judged.
3. **TimeIncrement.** Fee line with hours where `hours % incrementHours != 0` (exact decimal; credits judged too). Flag only.
4. **DailyHoursCap.** Fee lines with a date and hours, grouped by (timekeeper, date) within the invoice; the timekeeper is the
   roster match, else the normalised billed name. A group total **above** the cap fires once, on the group's first line. Flag only.
5. **NonBillableActivity.** The AI-assigned activity or task code is in `codes` (case-insensitive), whatever its confidence;
   low confidence is carried and flagged (NFR-05). Suggested adjustment: Disallow, amount = the rounded line amount (null if none);
   **none for a credit line** (D-105): the finding still fires so the reviewer sees the credit, with `SuggestedAdjustment == null`.
6. **ExpenseRule.** Expense lines only. `code_not_reimbursable` when the AI expense code is in `nonReimbursableCodes`;
   `amount_over_cap` when the line amount is above `maxAmountPerItem` (item = the expense line; equal holds). A line breaking both gets both, code first.
   A credit line (a negative amount, e.g. "E109 -50.00"; D-105) is skipped on both checks.
7. **InvoiceAge.** Fires when `invoiceDate - lineDate` is more than `maxAgeDays` days (exactly N holds). Nothing is judged without an invoice date. A credit line is skipped (D-105): a reversal legitimately carries the old date.
8. **CoreRecordWindow.** Per line: `before_open` (date < open - graceDays), else `after_close` (date > close + graceDays), else
   `core_record_closed` when the record is closed and has no close date. Nothing is judged with no core record. A credit line is
   skipped on all three reasons (D-105). D-111: a line dated after the close date is flagged; on a closed record with no close date
   every line is flagged; earlier work billed later is not (matter closed 2026-06-30, line 2026-06-15 on a September invoice: no
   finding; line 2026-07-05: finding). The Describe text says exactly this.
9. **Duplicate** (FR-36, D-88). Invoice level (when `firmInvoiceNumber` is a match field): the number (whitespace/case ignored)
   equals an **Active** earlier attempt's; Rejected and ReturnedToFirm attempts and the submission itself are ignored; cites the
   lowest-sequence original. Line level (the other match fields): a line whose configured fields all equal an earlier line in an
   active prior submission (`line_prior_submission`) or earlier in this invoice (`line_same_invoice`); the repeat is flagged,
   not the original. A line missing a configured field is not compared. **D-112:** a prior in another wall (its secure roots are not a
   subset of the invoice's, or either set is unresolved) still produces the finding, with `DetailWithheld` set: no tracking number,
   no token, no value text, and the message is `BillingRuleCatalogue.WithheldDuplicateTemplate` ("This matches an earlier submission.").
   A match in a prior the viewer may see is preferred over one across the wall. See "Fix round 2".
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
3. **CoreRecordWindow "or core record closed"** (owner decision **D-111**, no longer an open reading): a closed record with a close
   date is judged by that date (a line dated after it is flagged; earlier work billed later is not); closed with no close date flags
   every line (`core_record_closed`). The rule's Describe text now says exactly this (it used to say "and the core record is not
   closed", which the handler never did), and a test pins the text and the owner's 30 June scenario.
4. **ApprovedTimekeeper "(and core record)"** is judged at the firm level only (**D-111**): the 017 roster has no core-record scope,
   so 046 resolves at firm and line date. A per-core-record roster would extend the resolution, not this handler. The Describe text
   now says "approved for the firm on the line date" (no "and core record"), as do the schema description and the `RuleType` doc.
5. **Duplicate same-invoice repeat** (`line_same_invoice`) is beyond the strict POML (prior submissions only). Justification:
   "a line repeats an earlier line" in design.md 6.1 is checkable within the invoice with no I/O, and a double-billed line on one invoice is the common case.
6. **Currency check is always on** when RequiredData runs. 070's `fields` vocabulary has no currency member and I did not
   extend it; a foreign-currency invoice is unsupported in R1 (D-20) however the rule is configured.
7. **AI codes drive NonBillable and ExpenseRule** (validated, with coding confidence); **firm-billed codes drive RequiredData** (what the invoice carries).
8. **No `BillingScope` parameter**: handlers judge every subject; 072 filters by the line date the version is in force for (YAGNI, section 11).
9. `ReadFields` includes every extracted value a finding shows or uses (conservative; see above). (Fix round 1: ApprovedRate's `Hours`
   now follows that rule - read only when the reduction is computed from it.)
10. **Fix round 1, D-105 reading.** "Credit line" is one test for all five rules, `Hours < 0 || Amount < 0` (`BillingMath.IsCreditLine`),
    where the owner text says "negative hours or a negative amount". The brief named hours for ApprovedRate and amount for
    ExpenseRule; using the one shared definition for both means a negative amount on a fee line is also skipped by ApprovedRate.
    A zero amount or zero hours is **not** a credit. CoreRecordWindow skips a credit on all three reasons, the
    closed-record one included. NonBillableActivity keeps the finding and drops only the adjustment (reading "never suggest
    disallowing a negative amount" as "do not hide the credit, do not adjust it").
11. **Fix round 1, sub-cent rate.** ApprovedRate rounds the billed rate to the cent before comparing **and** before computing the
    reduction (the brief asked only for the comparison), so the rate the finding shows is the rate that was judged and the
    reduction adds up from the displayed values: 650.004 is shown as 650.00 and reduces by (650.00 - 575.00) x hours.
12. **Fix round 2, D-111** (calls 3 and 4 above): the Describe texts match the handler behaviour; no handler change was needed.
13. **Fix round 2, D-112, Duplicate across the secure wall** (owner decision): the finding still fires when the matching prior is
    in another wall; it carries no tracking number, no token and no line detail. The wall is the set of secure roots above the
    core record, not a secure/ordinary flag (each Secure record is its own wall; coordinator ruling on both reviewers' finding).
    Detail is shown only when both sets are resolved and the prior's is a subset of the invoice's; unresolved fails closed. Judgment
    calls inside that ruling: (a) a withheld finding keeps its reads (field and confidence) and drops only the value text, so 073
    still sees every read field; (b) when a line or number matches both a visible and a hidden prior, the visible one is cited
    (nothing is withheld that could be shown), instead of the lowest sequence number across walls; (c) the withheld message
    names no token at all (`WithheldDuplicateTemplate`), so it renders even when the invoice number is missing.

## Component justification (CLAUDE.md section 11)

- **Existing**: `SignalEvaluator` (FetchXML over stored rows) and Finance `SignalEvaluationService` (spend signals) - neither judges an in-memory invoice (D-23). 046's resolution types, 047's `InvoiceLineObservation` and `InvoiceLineType` are reused as-is.
- **Extension**: not possible for the evaluators (different input and output); reused where possible.
- **Cost of doing nothing**: no billing rule can be evaluated at submission, so no finding, flag or rejection exists (FR-24, FR-25, FR-26 fail).
- New surface is internal static code; no DI, no endpoint, no package, no Dataverse column. Public surface: `BillingRuleHandlers`, `BillingRuleRefusedException`, `BillingFactSet` and its parts, `BillingFinding` and its parts (072/073 need them).

## Known limits (accepted, no action)

- (K1) Duplicate compares a rostered line and an unrostered line as different people even when the billed names are equal; the unrostered line is already an ApprovedTimekeeper finding, and a line-level duplicate never rejects.
- (K2) DailyHoursCap is per invoice (R1; design.md 6.1).
- (K2) DailyHoursCap can under-count one person across spellings (`DailyHoursCapHandler.TimekeeperKey`): a roster-matched line groups
  under `id:<timekeeper id>`, an unmatched line under `name:<normalised billed name>`, so a matched line and an unmatched spelling of the
  same person (for example "Jane Doe" matched and a typo "Jane Dow" unmatched) form separate groups and their hours are **not** added.
  A person can therefore exceed the cap with no single group above it. It fails closed in the sense that matters: every unmatched
  line is already an ApprovedTimekeeper finding, which holds the invoice for review, and DailyHoursCap is flag-only. Merging a name
  group into an id group would need a guess at identity that FR-23 forbids ("nothing maps an unmatched name to another timekeeper").
- (K4, narrowed in fix round 2) The purity guard now follows call targets declared in `Sprk.Bff.Api` transitively (a helper in
  another type, the catalogue, a lambda or constructor), so `InvoiceAge` calling `BillingRuleCatalogue.SchemaText` is caught. It
  still does not follow a call through an interface, a virtual method or a delegate whose target is chosen at run time. Before
  round 2 it saw only the handlers' own IL call sites. A BCL overload that reads the ambient culture internally, with no
  `CultureInfo.CurrentCulture` call in our code (`decimal.ToString("F2")`, `ToUpper()`, `string.Format`, `decimal.Parse(string)`), is
  not detected. Today every number and date in `Handlers/` is formatted with `CultureInfo.InvariantCulture`, `ToUpperInvariant`, `"D"`
  GUIDs or enum names (grep, fix round 1), and `BillingMathTests` runs the text under de-DE and tr-TR. Closing it needs a guard that
  flags provider-less `ToString`/`Parse`/`ToUpper`/`Format` calls; not built (scope), named here as the next step.
- (K3) `EmptyConfidence.Instance` is a shared empty dictionary typed read-only; the purity guard's static-state check covers the handler namespace, not the contracts.

## Tests

`tests/unit/domain/Signals/Billing/Handlers/` (Maintain-class, ADR-038: pure, no mocks, compiled into `Sprk.Bff.Api.Tests`), namespace `Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers`:

- **301 unit tests** (all pass; 252 from the task, 49 added in fix round 1): one class per type, `BillingRuleHandlersDispatchTests` (enumerates every `RuleType`; non-billing and unknown values refused with `no_handler`; missing parameters refused; token contract; all 11 fire on a fact set that breaks every rule and none fires on a clean one), `BillingMathTests`. Shared `BillingHandlerKit.Run` asserts the per-finding contract and determinism for every finding of every test. Theory data uses text for decimals (no double round trip).
- **24 ArchTests** (20 from the task, 4 must-fire controls added in fix round 1; `tests/Spaarke.ArchTests/BillingHandlerPurityGuardTests.cs`): the handlers and contracts reference no I/O, network, clock, randomness (`Guid.NewGuid`, `Guid.CreateVersion7`), ambient culture or time zone (`CultureInfo.CurrentCulture`/`CurrentUICulture`, `TimeZoneInfo.Local`), environment, logging, reflection, expression trees, `Activator`/`Type` late binding; no static registry (only readonly `string[]` tables); the dispatch builds no `Dictionary`/`List`/`HashSet`. 18 must-fire controls (one per forbidden shape), a registry control, a must-not-fire control (extended in fix round 1 to show `CultureInfo.InvariantCulture`, `TimeZoneInfo.Utc` and `Guid.TryParse`/`Guid.Empty` are not flagged), and a non-vacuity check. Acceptance greps: no `HttpClient`, `TimeProvider`, `DateTime.Now`, `IGenericEntityService`, reflection or registry in `Handlers/` (the guard covers the compiled IL, stronger than a text grep).
- Failed before / pass after: the handler types did not exist before, so the tests cannot run on the old tree (compile failure). The equivalent proof is the mutation table below: 14 of 14 production mutations are killed.
- Run at the end of the task: new Handlers classes 252 passed / 0 failed; `Sprk.Bff.Api.Tests` filter `Billing|RuleType|PolicyVersion|Signal` 2566 passed, 3 skipped (pre-existing live-environment skips), 0 failed; `Spaarke.ArchTests` full project 1146 passed, 0 failed (includes the new 20 and the ADR-010 ceiling). **After fix round 1** (current base): Handlers classes 301 passed / 0 failed; same wider filter 2871 passed, 3 skipped (the same live-environment skips), 0 failed; `Spaarke.ArchTests` full project 1164 passed, 0 failed (24 in the purity class). The whole unit project was not run (45+ min, per the brief). Build: `dotnet build src/server/api/Sprk.Bff.Api/ -m:1` 0 warnings, 0 errors.

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
| M09 | FR-23/D-89: unmatched timekeeper no longer fires | 18 | `AnUnmatchedTimekeeper_PassesThrough046sCappedConfidence_AndIsMarkedUnreliable_ThisIsNotTheD89Enforcement` (named `..._OnlyHolds_..._D89` until fix round 1; it proves the 0.80 cap is passed through, not D-89 itself, which task 073 enforces) |
| M10 | Dispatch: `Duplicate` case removed | 33 | `EveryRuleTypeMember_IsABillingTypeWithAHandlerAndASampleBody_OrIsRefusedFailClosed` |
| M11 | InvoiceAge `<= maxAgeDays` -> `<` | 1 | `MustNotFire_ForALineDatedExactlyNDaysBeforeTheInvoice` |
| M12 | CoreRecordWindow: grace days not applied after close | 1 | `TheGraceDaysWidenTheWindowOnBothSides(7, 7, false)` |
| M13 | Token contract: out-of-set token no longer refused | 1 | `AHandlerThatNamesATokenOutsideItsTypesFactFieldSet_Fails_...` |
| M14 | Purity: a handler reads `DateTime.UtcNow` | 1 (ArchTest) | `HandlersReferenceNothingImpure` |

(Each mutation was applied to the committed file, the test project rebuilt, the Handlers namespace run, then the file restored
with `git checkout -- <file>` and touched so the incremental build could not reuse the seeded DLL. The tree was clean after.)

## Review fix round 1 (adversarial review of 071; owner decision D-105)

Base `eacad6b21` (the branch was rebased after the round was written; the round was first built on `80daf089f`, the 075 fix commit; 071's own commits are `6b99e980f`..`d22c8e26d`). Nine commits. Each was committed from a green build and test run of the Handlers classes; the three handler commits after `0f6c88cd4` (ExpenseRule, InvoiceAge plus CoreRecordWindow, NonBillableActivity) touch independent files and were built and tested together before being committed one by one.

| Commit | Item | Change |
|---|---|---|
| `11b7c3abf` | F1 (D-105) | `BillingMath.IsCreditLine`: the one definition (`Hours < 0 \|\| Amount < 0`); tests in `BillingMathTests` |
| `7aa46053b` | F1 (D-105) | ApprovedRate skips credit lines; the old "negative hours still fires" test now pins must-not-fire |
| `8a50563c6` | F1 low | ApprovedRate compares `ToCent(billed)` against the limit, and computes the reduction from the same rate |
| `0f6c88cd4` | F4 | ApprovedRate adds `Hours` to the read fields only when Hours > 0 (what this file's table already said; superseded by fix round 2: the credit test reads Hours whenever the line has them) |
| `157ece3ce` | F1 (D-105) | ExpenseRule skips credit lines (e.g. "E109 -50.00") |
| `21ae06e9f` | F1 (D-105) | InvoiceAge and CoreRecordWindow skip credit lines |
| `0fcb7f723` | F1 (D-105) | NonBillableActivity: a credit keeps its finding, with no suggested adjustment |
| `f658c4c3c` | K4 | Purity guard forbids `Guid.CreateVersion7`, `CultureInfo.get_CurrentCulture`, `get_CurrentUICulture`, `TimeZoneInfo.get_Local` |
| `6cb6dbed3` | D-89 wording | The D-89 timekeeper test is renamed and re-described: it proves the cap is passed through; task 073 enforces D-89 |

The notes themselves (F4 stale hashes, K2 limit, contract text, this section) are the commit after these: `4257e7f35`.

**Failed before / pass after.** With the five changed handlers put back to `eacad6b21` (the round's base) and the new tests kept, the Handlers classes give
**30 failed, 271 passed** (301 total); with the fixed handlers **0 failed, 301 passed**. The 30: ApprovedRate 13 (credit x7, sub-cent x4,
read fields x2), ExpenseRule 4, InvoiceAge 5, CoreRecordWindow 4, NonBillableActivity 4 (names per item are in the commit tests,
all end `_D105` for the credit rule). The two ApprovedRate tests that pass on both trees are the boundary half of each pair (`575.005`
fires on both; positive hours still read `Hours`) - the must-not-fire half is what fails before. The D-89 rename and the guard are not
behaviour changes; the guard's proof is below.

**Purity guard must-fire evidence (K4).** Besides the four permanent control fixtures (`MintsATimeOrderedIdentity`,
`ReadsTheCurrentCulture`, `ReadsTheCurrentUiCulture`, `ReadsTheLocalTimeZone`, each asserted by name in
`EveryDetectorFiresOnItsControlFixture`), all four members were injected temporarily into production code (`BillingText` in
`HandlerContext.cs`: `CultureInfo.CurrentCulture.Name + CultureInfo.CurrentUICulture.Name + TimeZoneInfo.Local.Id + Guid.CreateVersion7()`):

```
Failed Task 071: the billing handlers and their contracts are pure (no I/O, clock, randomness, logging, reflection) [FAIL]
BillingText::TemporaryInjection calls System.Globalization.CultureInfo::get_CurrentCulture (System.Globalization.CultureInfo.get_CurrentCulture);
BillingText::TemporaryInjection calls System.Globalization.CultureInfo::get_CurrentUICulture (System.Globalization.CultureInfo.get_CurrentUICulture);
BillingText::TemporaryInjection calls System.TimeZoneInfo::get_Local (System.TimeZoneInfo.get_Local);
BillingText::TemporaryInjection calls System.Guid::CreateVersion7 (System.Guid.CreateVersion7)
Failed!  - Failed: 1, Passed: 23, Skipped: 0, Total: 24
```

The injection was then removed (`git checkout -- HandlerContext.cs`, never committed) and the run is green again: **24 passed, 0 failed**.
The existing must-not-fire control stays and now also covers the invariant siblings, so the new entries are member-specific, not whole-type.

**Quality gates (task-execute Step 9.5).** code-review and adr-check run on the fix scope (the diff, its callers and callees; nothing
outside `Services/Signals/Billing/` consumes the handlers yet, so there are no other callers). No F-class finding. ADRs considered:
ADR-010 (no interface or registration added: compliant), ADR-013 (the handlers use `Services/Ai/PublicContracts` types as before: compliant),
ADR-038 (tests stay under `tests/unit/domain/**` and `tests/Spaarke.ArchTests/**`; one theory row was replaced by its must-not-fire
counterpart in the same change, nothing deleted without a replacement), ADR-029 (no package change). Build: 0 warnings, 0 errors; the
pre-commit `dotnet format` ran on every commit. K-class: the two K entries added above.

**Class search (Step 9.5 rule 6).** The pattern is "a charge rule that reads hours or an amount without asking if the line is a credit".
`grep -E "line\.(Hours|Amount|BilledRate)|IsCreditLine"` over `Handlers/`: nine files; the five D-105 handlers now call `IsCreditLine`;
TimeIncrement, DailyHoursCap and Duplicate read hours or amounts but are not in D-105 (owner question 5). No `.Hours <`/`.Amount <` sign
test is repeated outside `BillingMath`. The guard that would catch a sixth hand-written sign test is not built (a source-scan for
`Hours < 0` in `Handlers/` would be a few lines, but it is a new guard: scope change, owner call).

## Fix round 2 (ontology-r1's review at `4257e7f35`; owner decisions D-105, D-111, D-112)

Base `6bcd4d029` (`work/ebilling-r1`). Commits (cherry-picked onto `work/ebilling-r1` by the main session): `8b0604728` F4 read fields and K2, `a563146cb` K1, `eb978396c` D-112, `b78b30c0b` Describe texts (D-111),
`cae4e9051` K4 purity guard, plus this notes commit. Cherry-picked hashes on `work/ebilling-r1`: `8b0604728` (F4, K2), `a563146cb` (K1), `eb978396c` (D-112), `b78b30c0b` (D-111), `cae4e9051` (K4), `7b0204f21` (notes); range `e2288a659..7b0204f21`.

| Item | Change | Evidence |
|---|---|---|
| F4 | `HandlerContext.WithCreditTestReads` appends the extracted Hours and Amount the line has (not repeated) to every finding built after an `IsCreditLine` check: InvoiceAge, CoreRecordWindow, ApprovedRate (both finding shapes), ExpenseRule, NonBillableActivity. Per-type table and `ReadFields_*` tests updated; one scenario test per handler for the reversal extracted as +1437.50 at 0.50 | With the five handlers put back to `6bcd4d029` and the new tests kept: 18 failed, 824 passed (842). With the fix: 0 failed |
| K2 | ExpenseRule: negative hours and a positive amount (over the cap, on a non-reimbursable code) is skipped as a credit | Passes on both trees (behaviour already existed); it is the test that kills ontology-r1's M13 (hours test dropped from `IsCreditLine`) |
| K1 | `BillingInvoiceFacts.SubmissionId` is `required Guid`; `DuplicateHandler.ActivePriors` lost its null branch | `TheInvoiceFactsSubmissionId_IsARequiredNonNullableGuid_K1` (reflection on the property type and `required`) |
| D-112 | `SecureRoots` (required `IReadOnlySet<Guid>?`) on `BillingInvoiceFacts` and `BillingPriorSubmission`; `BillingFinding.DetailWithheld`; `BillingRuleCatalogue.WithheldDuplicateTemplate` and `WithheldTemplateFactFields`; `DuplicateHandler.MayShowDetail` | Invoice level and line level, ten rows each: ordinary/ordinary, same root, ordinary prior on a secure invoice, `{A}` on `{A,B}` (shown); secure prior on an ordinary invoice, roots A vs B, `{A,B}` on `{A}`, prior unresolved, invoice unresolved, both unresolved (withheld: no tracking number, no token, no timekeeper, date, hours or narrative in any value, token or rendered text). With `MayShowDetail` forced to `true`: 14 failed (the 7 withheld rows at both levels) |
| D-111 | Describe texts: CoreRecordWindow states the close-date rule and the closed-no-close-date case; ApprovedTimekeeper says firm level. Schema descriptions and the `RuleType` doc follow | `Describer_CoreRecordWindow_SaysWhatTheHandlerDoes_D111`, `Describer_ApprovedTimekeeper_SaysFirmLevel_...`, and the owner's scenario `D111_AMatterClosedOn30June_...` (06-15 and 06-30 no finding; 07-05 finding, `after_close`) |
| K4 | `BillingHandlerPurityGuardTests` follows call targets declared in `Sprk.Bff.Api` transitively with a visited set (`IlCallScan.ReferencedMethods` / `DeclaredBodies` added; `MethodReferences` uses them) | Must-fire: a handler-shaped fixture reaching `File.ReadAllText` through a helper, a lambda and a constructor (a direct-only scan misses it); must-not-fire: a pure cyclic helper chain; non-vacuity: the walk reaches `BillingRuleCatalogue`. Mutation M10 (InvoiceAge calling `BillingRuleCatalogue.SchemaText`): guard fails with `SchemaText calls ...Assembly::GetManifestResourceNames / StreamReader::.ctor / ...`, passing before. `BillingRuleCatalogue` is not added to the scanned types as a whole: `SchemaText` is legitimate for task 075 |

**Counts at the end of the round.** `Sprk.Bff.Api.Tests` filter `Billing`: 871 passed, 0 failed (Handlers classes: 334 passed; 301 before). `Spaarke.ArchTests` full project: 1175 passed, 0 failed (purity class 27: 24 before plus 3 transitive controls). `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors. The whole unit project was not run.

**For 072/073.** 072 sets `SecureRoots` (required, so omitting it is a compile error) and uses `BillingRuleCatalogue.WithheldDuplicateTemplate`
as the message when `BillingFinding.DetailWithheld` is true (the policy version's template names `{{duplicateOf.trackingNumber}}`, which a
withheld finding does not have). 073 treats a finding's `ReadFields` as complete, as before: a withheld finding lists the same fields.

## BFF hygiene (CLAUDE.md section 10)

- Placement: in the BFF, `Services/Signals/Billing/` (as 070); pure in-process domain code, no endpoint, no DI, no background work.
- **Publish size (NFR-01)**: fresh `git archive` exports at short paths (`C:\w71b` = `4c1203b42`, `C:\w71h` = the final production tree, measured at `1dbfbbf76`, which is `ef8d87819` after the branch rebase; the
  071 files under `Services/Signals/Billing/` and the tests are identical on both, `git diff --stat` shows only an unrelated one-line
  `PolicyVersionValidator.cs` difference from the new base).
  Command `dotnet publish src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -c Release -r linux-x64 --self-contained false -p:NuGetAudit=false`
  (framework-dependent linux-x64, Release, **PDBs included**), zipped with `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`,
  **192 files on both sides**.

  | Tree | Zip bytes | MB |
  |---|---|---|
  | base `4c1203b42` | 39,462,574 | 37.63 |
  | task 071 `ef8d87819` (measured as `1dbfbbf76`) | 39,487,585 | 37.66 |
  | **Delta** | **+25,011** | **+0.024 MB** |

  Well under the +5 MB trigger, 55 MB review and 60 MB ceiling. (The absolute figure is lower than the recorded 44.96 baseline; baselines age and this is measured, not recorded.)
  **Fix round 1 was not re-measured**: it edits six existing files (about 60 changed lines of C#, no new type, no package, no csproj
  change), so it cannot move the zip beyond noise against the +0.024 MB above. Re-measure at the project-close publish check if the owner wants it.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages. No package or csproj change.
- ADR-010: no DI registration; the ceiling (166) is unchanged and its ArchTest is green.

## ontology-r1 review

No Ontology-owned file is touched (new files only, under `Services/Signals/Billing/`; `RuleType.cs`, `PolicyVersionValidator.cs`,
`PredicateCompiler.cs`, the Signals jobs and their tests are unchanged), and the Signals tests (the `Signal` filter) pass.
**Not obtained**: the executing agent cannot message other sessions. Main session: send ontology-r1 the commit range
`d4e555e38..d22c8e26d` plus the fix round `11b7c3abf..6cb6dbed3` (production under `Services/Signals/Billing/` only) and paste the answer here. Question for them: do the new
public names (`BillingFactSet`, `BillingFinding`, `BillingFieldNames`, `BillingReasonCodes`, `BillingRuleHandlers`) collide with
anything planned on `docs/ontology-platform-design`?

## Open questions for the owner (recommendations in brackets)

1. ApprovedRate reduction: to the approved rate (as built) or to the tolerance limit? [As built.]
2. No approved rate found: flag with no adjustment (as built) or do not judge? [As built; such a finding holds by D-47.]
3. Is a same-invoice repeat line a Duplicate (as built, beyond the strict POML)? [Keep.]
4. Should RequiredData's `fields` vocabulary gain a `currency` member (070 schema change), or is the always-on currency check right for R1? [Always-on for R1; revisit when a second currency is supported.]
5. D-105 names five rules. TimeIncrement ("credits judged too" - a credit of -0.25 h against a 0.1 h increment is flagged), DailyHoursCap
   (credits are summed in and offset the day), Duplicate, RequiredData and AiObservation still judge credit lines. TimeIncrement,
   DailyHoursCap, Duplicate (line level) and AiObservation are flag-only; RequiredData and ApprovedTimekeeper can reject in
   principle (ApprovedTimekeeper never, D-89). Should a credit also be exempt from any of them? [No: a credit with missing data or
   a reversal that is itself off-increment is still worth a look, and none of those flags adjusts money. Revisit if reviewers report noise.]

## Pre-commit hook note

The repo's lint-staged pre-commit hook backs up working-tree state with a `git stash` internally during each commit and restores
it; I ran no `git stash` myself, and the stack held only other sessions' entries afterwards.

## Round 3 (072 part W, 2026-10-18)

Two 071 items were carried by 072's review fix round, part W (branch `fix/072-part-w`, base `work/ebilling-r1` `db4eee636`).

| Item | Commit | Change | Evidence |
|---|---|---|---|
| F4 (round 3) | `017503b2a` | `HandlerContext.WithCreditTestReads`: when a line has NEITHER Hours NOR Amount, both are listed as null-valued reads with unknown confidence (below the floor). One absent (an expense line has no hours) is unchanged: nothing is listed | Old `HandlerContext.cs` with the new tests: `AnApprovedRateFinding_OnALineWithNeitherHoursNorAmount_IsHeld_NotRejected_071Round3` fails (the finding rejected); the negative case `OnlyOneOfHoursAndAmountAbsent_IsNotInvented_TheFindingStillRejects_071Round3` passes on both trees |
| D-119 | `b1a6d27f3` | `BillingCurrency.Normalise` / `IsUsd` (in `HandlerContext.cs`) is the one USD test. `RequiredDataHandler` uses it, so a bare `"$"` is USD there too (it was "not USD"); the evaluator's money skip uses the same one | Old `RequiredDataHandler.cs` with the new tests: 4 fail (`"$"` and `" $ "` in the handler's must-not-fire theory and in the evaluator's `TheMoneySkip_AndRequiredData_AgreeOnTheCurrency_D119`). The handler's must-fire theory now uses `"US$"` (not a bare `"$"`) where it used `"$"` |

**A deliberate exception to "absent fields are not invented" (F4).** This file's read-field contract says a field the line does not
have was not read. The credit test (`IsCreditLine`, D-105) is the one place where two absences together are themselves the problem:
"(2.5) @ 650 = (1,625.00)" whose bracketed numbers did not parse is judged as a charge, and with nothing listed its finding looked
reliable, so 073 could reject what is really a credit. Listing both as unknown makes 073 hold it (`read_field_confidence_unknown`).
The rule is narrow on purpose: only when BOTH are absent, and only on findings built after the credit test (the five D-105
handlers). A read of the field that the finding already lists with unknown confidence is not repeated; one with a known confidence
does not stop the unknown read being added.

**Read-field table (above), row update.** ApprovedRate, ExpenseRule, InvoiceAge, CoreRecordWindow and NonBillableActivity also list
`Hours` and `Amount` (null value, unknown confidence) on a line that has neither.

**CoreRecordWindow (D-116, 072 part W `ffe12af4b`).** The core record's facts gain `BillingStatus`; the handler reads it first
(Open: opened date only; Final: also after the close date, every charge line with no close date; Closed: every charge line,
`core_record_window.closed_to_billing`; empty: D-111). Credits stay excluded. Its findings add the reference value
`coreRecord.billingStatus`; the read fields are unchanged.
