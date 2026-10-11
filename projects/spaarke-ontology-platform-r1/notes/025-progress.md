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
