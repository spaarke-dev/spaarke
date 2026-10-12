# Task 025 - Threshold rule type: schema and compiler

Rigor FULL, sonnet @ high. Branch `task/ontology-025-threshold` from `origin/docs/ontology-platform-design`. 2026-10-11.

## What shipped

- `Schemas/threshold-rule.schema.json` (embedded by the existing `Schemas/*.json` glob): `{ type: "Threshold", subject, threshold: { field, operator, value }, when?, quietWindowDays? }`.
  `operator` is one of `>`, `>=`, `<`, `<=`; `value` is a JSON number; every object is closed. `all`, `exists`, a second comparison, a string/expression value and a field that is not a plain logical name are schema errors with a pointer (`/threshold/value`, `/all`, ...). `when` reuses the Existence per-value grammar as a verbatim copy of its `$defs`; `ThresholdRuleTests.TheThresholdSchemaFilterGrammar_IsAVerbatimCopyOfTheExistenceSchemas` fails if the two copies drift.
- `RuleBodySchemaValidator`: schema dispatch for Threshold; Switch still throws `NotSupportedException`.
- `PredicateCompiler`: `Compile(body, subjectId, ruleType)` / `CompileSchemaValidated(body, subjectId, ruleType)` (defaults keep every Existence caller unchanged). `CompileThreshold` emits ONE condition in the subject's root filter and records the compared column like a `when` field. `ThresholdColumns` is the closed list of numeric columns: `sprk_matter.sprk_budgetutilizationpercent` (Decimal), `sprk_totalbudget`, `sprk_totalspendtodate` (Currency). Compiler refusals (also when the schema is skipped): column not listed, operator not gt/ge/lt/le, value not a number, `all` present, Switch.
- `PolicyVersionValidator`: only Switch is `RuleTypeUnsupported` now; Threshold goes through schema -> compiler -> template check like Existence, at save and at evaluation (`TryPrepareForEvaluation`).
- `RuleBodyDescriber` (task 026 hand-off): reads the body's own `type`; a Threshold body is described as an optional `when` line plus a `threshold` line ("The matter's total budget is at least 250000."), with numeric wording ("is at least", not "on or after"). Threshold bodies the compiler refuses are refused by the describer too.
- `SignalEvaluator`: 8 lines (see deviation 1).

## Deviations and decisions

1. **One small additive change to `SignalEvaluator.EvaluateAsync`, none to `SignalReevaluationJob`.** The evaluator reads the raw body's `when` keys as the columns and facts it fetches, and nothing else. A Threshold body's compared column is not under `when`, so without a change it would never be fetched and a template naming it (the compared value is eligible, task constraint) would be refused at write time (missing fact). The change adds the body's `threshold.field` to the same list. The job, the compiled-predicate contract and `TryPrepareForEvaluation` are untouched; `SignalReevaluationJobTests` runs a Threshold policy through the real job and evaluator. If the owner wants a strict zero-change evaluator, the alternative is to drop the compared column from `TemplateEligibleFields` (a sentence could not state the value) - say so and it is a 3-line revert plus a template-vocabulary change.
2. **"The threshold value is template-eligible only as the pinned literal"**: read as: the declared number is NOT a `{{token}}`; the only eligible token is the compared column, whose value is the subject's own (the `when` class), because `>=`/`<`... never pin a field to one value. A template that wants to say "over 90" types 90 as prose, exactly as an Existence template states a window. Tested (`ATemplate_NamingAnythingElse_IsRefused`).
3. **Closed `ThresholdColumns` list** instead of "any identifier": the body cannot say a column is numeric and a threshold on a text/date column would not fail at save. The seam test `ThresholdColumns_AreNumericOnTheLiveSchema` checks the real type. Matter only (the D-16 spend threshold); `sprk_project` has the same columns but was not asked for.
4. **Describer keys off the body's `type`** (its public signature carries only the body; its one caller, `DecisionPlanEndpoints`, passes only the body). The schema pins `type` to the version's rule type, so a mismatch is unevaluable anyway; describing it is harmless.
5. The existing tests that asserted "Threshold is RuleTypeUnsupported / throws NotSupported" were rewritten to Switch (the only type still unsupported): `PolicyVersionValidatorTests` (3), `RuleBodySchemaValidatorTests` (2). `RuleBodyDescriberTests` keeps its Threshold-refused row (a body with `when`/`all` is not a Threshold body).
6. `SignalReevaluationJobTests` (shared file): `World.AddPolicy` takes a rule type; the fake's FetchXML read applies the subject's own numeric root conditions (ge/gt/le/lt/eq with a numeric value) so "over the threshold yes, under it no" is a property of the compiled query, not of a canned list. Four tests added in a block before `Fixtures`.

## Known limits (K-class, one line each)

- Dataverse compares nothing with null: a matter with no roll-up never fires, for any operator, including `<` (K2: fails closed; stated in the schema description).
- Roll-up freshness is the finance module's (F-68 / #1663).
- Money columns compare in the record's own currency (no conversion); the rule is a number, not an amount in a currency (K2; the three listed roll-ups are the matter's own currency).

## Evidence

- Unit: `ThresholdRuleTests` (compile, 4 operators, 3 columns, canonical numbers, when + subject narrowing, golden FetchXML vs hand-written reference, 16 schema refusals, reserved members, compiler-only refusals with the schema skipped, Switch, evaluation gate, template eligibility, schema drift guard, describer) and four job tests in `SignalReevaluationJobTests`.
- Live, read-only, spaarkedev1 as the writer principal (`AZURE_TOKEN_CREDENTIALS=AzureCliCredential`, `SIGNALS_LIVE_CALLER_ID=3121bf1b-...`), 2026-10-11: `ThresholdColumns_AreNumericOnTheLiveSchema`, `ThresholdFixture_ReturnsExactlyTheRowsOfTheHandWrittenFetchXml...` (spend >= 20000 -> the 25000 and 250000 matters in, the 10000 matter and every null-spend matter out, equal to the hand-written query), and four operator/column cases (strict `>` excludes the boundary row, `<=` includes it, Currency and Decimal columns). No data was written; the job's Signal write is proven in the unit harness, not live (no Dataverse writes in this task).
- Live seam `DateOnlyColumns_MatchTheLiveSchema_InBothDirections` fails on the project branch for a reason unrelated to this task (F-67 / #1662).

## Fix round (review of #1664), 2026-10-11

1. **F-67 fixed here.** `("sprk_workassignment", "sprk_respondedon")` added to `DateOnlyColumns`; `DateOnlyColumns_MatchTheLiveSchema_InBothDirections` passes live (22/22). Unit: `Compile_WorkAssignmentRespondedOn_IsReportedAsDateOnly`, `Evaluate_DateOnlyBoundOnRespondedOn_IsWidenedLikeTheOtherDateOnlyColumns`. F-67 / #1662 marked resolved in defer-issues.md; close #1662 on merge.
2. **Describer matches the compiled query.** Numbers print in the compiler's canonical form (`1e2` reads 100; a 32-digit value reads the rounded decimal the query compares); a numeric `when` operand uses at least / more than / at most / less than in Threshold and Existence bodies, dates keep date wording.
3. **Live finding (read-only, spaarkedev1, 2026-10-11): the SDK returns both Money and Decimal columns at scale 10** (`sprk_totalspendtodate` Money 25000.0000000000, `sprk_totalbudget` Money 150000.0000000000, `sprk_budgetutilizationpercent` Decimal 1.2500000000), although the column metadata precision is 2. So `SignalWriter.FormatFactValue` trims trailing zeros for decimal and Money (`25000`, `94.5`); the job tests now feed scale-10 values. Recorded by `ThresholdColumns_RangesFitTheLiveColumnMetadata_AndTheSdkReturnsTheirValuesAtTheColumnScale`.
4. **K1 guard.** `PredicateCompiler.ThresholdColumnRanges` (ThresholdColumns is now derived from it): Money columns +-922,337,203,685,477, the percent column +-100,000,000,000 (live Decimal metadata min/max). A value outside is refused at save ("outside the range Dataverse holds"); the live seam asserts each range lies inside the live metadata.
5. **K2** noted next to F-68 in defer-issues.md.
6. **Test fake.** `HoldsNumericConditions` now compares only Money/decimal columns, ignores option set and other non-numeric values (statecode eq 0), and still excludes a missing value for ordered comparisons. Control: `Run_ThresholdPolicy_WithAnOptionSetWhenCondition_IsNotExcludedByTheFake_ControlForTheNumericFilter`.


## D-120: Acted Threshold Signals re-raise after a re-crossing

- **Marker = existing column `sprk_lastevaluated`, no new schema.** Checked sprk_signal on spaarkedev1 (2026-10-11): there is no cleared/observed column. `sprk_lastevaluated` is "last evaluated, written only when the result changes" (D-61; staleness is told from the run-level gauge, never a per-row stamp), and a closure already sets it equal to `sprk_resolvedon`. So an Acted-resolved Signal with `lastevaluated` more than 1 minute after `resolvedon` means "the job observed the condition cleared after the resolution" (`ExistingSignal.ClearedObserved`; the minute absorbs a commit that stamps both).
- **Pass behaviour (Threshold policies only, Decide lane).** Latest episode Acted, subject NOT a hit, no marker: `SignalWriter.MarkClearedObservedAsync` stamps `sprk_lastevaluated` once (row-version guarded). Later hit with the marker: episode n+1 (D-13 key, current version). Hit without the marker (never cleared): silent, counted as awaitingEvidence. A second cleared pass finds the marker and writes nothing. Existence bodies take the unchanged D-119 path and are never stamped.
- **Not proven live:** the stamp is a write to a Resolved sprk_signal row as the writer principal; no Dataverse writes were allowed in this task, so it is proven in the unit harness only (the writer already updates the same column on resolved rows in CloseAsync).
