# Task 022 — Rule-body validation gate: record

> **Date**: 2026-10-05 · **Status**: rework round 3 complete, awaiting the main session's full-suite run · **Rigor**: FULL
> **Current API**: `PolicyVersionValidator.ValidateForSave(ruleType, ruleBody, messageTemplate)` (pure, save-time) and
> `PolicyVersionValidator.TryPrepareForEvaluation(PolicyVersionSnapshot, Guid? subjectId, out CompiledPredicate?)`
> (the fail-closed evaluation-time gate and the ONLY sanctioned path to a compiled predicate — see round 3 L1 for
> exactly what the architecture test enforces).

## Rework round 3 (2026-10-05) — third independent review PASS-WITH-FINDINGS; every finding fixed

The review found no fail-open and verified every round-2 disposition. Every finding below is FIXED.

**How each fix was proven.** I reverted fixes and checked that the pinning tests fail. There were three Signals
runs, kept separate so each failure is attributable:

- **Run A** reverted M1 (the `InvalidOperationException` catches in both classes, made unreachable) and L2 (the
  `then` check skipped, and the schema's `then` reopened to `type: object`). It also injected an M2 template leak
  into the log, and two M3 metric faults: template_malformed metered wrong, and the catch-all reporting
  `compile_refused`. Result: 274 → 19 failures, all of them M1/M2/M3/L2 tests.
- **Run B** reverted L3 (raw-text emission, ordinal-string pins). Result: 6 failures.
- **Run C** reverted L5 (more than one pin always ambiguous). Result: 7 failures.

The architecture guard was checked twice. With its three new detectors disabled, all 4 control-fixture rows
failed. With a forged predicate, a `with`-copy and an expression tree over `Compile` injected into
`SignalWriter`, the main fact failed and named all three. The originals were restored after every run.

| # | Finding | Disposition | Pinned by |
|---|---|---|---|
| M1 | An unpaired UTF-16 surrogate escape in a property name threw `InvalidOperationException` out of `RuleBodySchemaValidator.Validate` and `Compile`, and was metered `internal_error` | **FIXED.** The throw actually comes from the strict parse itself: `AllowDuplicateProperties=false` unescapes every name. It can also come from the name walk. `RuleBodySchemaValidator` now catches `InvalidOperationException` beside `JsonException` and returns "not valid JSON", with a fixed message (no exception text). `PredicateCompiler.ParseBounded` does the same. `CompileParsed` wraps the compile body so that a surrogate in a VALUE, reachable through `CompileSchemaValidated`, is also a `PredicateCompilationException`. The "never throws for Existence" doc now lists this case and is true | Shared theory rows `RuleBodySchemaValidatorTests.PathologicalBodies` (duplicate key, `1e999999`, two surrogate-name rows, one surrogate-value row), run by `Validate_WithPathologicalBody_IsRefused_NeverThrows` and by `ValidateForSave_PathologicalBody_IsRefused_NeverThrows_ReasonSchemaInvalid`. Also `Validate_WithUnpairedSurrogateInPropertyName_IsRefusedAsInvalidJson` (×2), `TryPrepareForEvaluation_UnpairedSurrogateInPropertyName_RecordsSchemaInvalid_NotInternalError`, `Compile_UnpairedSurrogateEscape_*` (×2) and `CompileSchemaValidated_UnpairedSurrogateEscape_*` (×2) |
| M2 | The no-leak test used a null template, so a template leak could not fail it | **FIXED.** It is now a theory with a distinctive non-null body and template (`SECRET-BODY…`, `SECRET-TEMPLATE…`). There are three rows: a body refusal (`schema_invalid`) and two TEMPLATE refusals (`template_malformed`, `template_token_outside_read_set`). The exact message, the exact field-key set and the reason are all asserted | `TryPrepareForEvaluation_Refusal_LogsExactlyTheAllowedStructuredFields_NoBodyOrTemplateLeak` (×3) |
| M3 | The template-failure test asserted only the log, and `internal_error` was never metered in a test | **FIXED.** The template test now also asserts the metric reason `template_malformed`. After M1, no known authored input reaches `internal_error`. It is forced through a seam that already existed: `PredicateCompiler`'s injected `TimeProvider`, here throwing an exception type nothing in the validator names. **No production seam was added.** The test asserts metric `internal_error`, the log reason, the exact keys, and that the exception text is not logged | `TryPrepareForEvaluation_TemplateFailure_AlsoLogsAndMeters`, `TryPrepareForEvaluation_UnexpectedExceptionInsideValidation_RecordsInternalError_LogsOnlyTheAllowedFields` |
| L1 | The IL scan did not cover forging, `with`-copying, or expression trees | **FIXED (coordinator decision).** `PredicateCompilerCallerGuardTests` now also flags `ldtoken` of `Compile*` (expression trees), `newobj` of `CompiledPredicate`'s constructor, and calls to its `<Clone>$` (`with`). The last two are allowed only in `PredicateCompiler` and the record itself. A permanent negative-control fixture, `PredicateCompilerGuardControlFixtures` (direct call, delegate, expression tree, forge, `with`), and a control theory prove each detector fires. The "ONLY path" doc (`PolicyVersionValidator` remarks, with a pointer from `PredicateCompiler.Compile`) now states exactly what is enforced. **Not enforced:** reflection, `dynamic`, and other assemblies | `OnlyTheSanctionedPathProducesACompiledPredicate`, `EveryDetectorFiresOnItsControlFixture` (×5), `PolicyVersionValidatorNeverCallsTheSchemaEvaluatingCompile` |
| L2 | An open `then` object let messageTemplate lookalikes through | **FIXED (coordinator decision): `then` is refused outright** ("`/then: 'then' is reserved; no task defines it yet…`"). This runs in `RuleBodySchemaValidator` before schema evaluation. The schema marks `then` as `not: {}` with a RESERVED description. **Grep first**: nothing needs `then`. The only `src/server` hits are this schema and the unrelated `Models/Ai/node-routing-config.schema.json`. The seeded Path B body (notes/004 and `tests/fixtures/signals/pathb-existence.rulebody.json`) has no `then`. The only uses were two test fixture bodies carrying `"then": {}` (removed) and the F8 tests (still pass: the `messageTemplate` walk runs first) | `Validate_WithThenProperty_IsRefusedAsReserved` (×3, including a `msgTemplate` lookalike), `ValidateForSave_ThenProperty_IsRefusedAsReserved_ReasonSchemaInvalid` |
| L3 | Numbers were emitted with their raw JSON spelling, and pins compared as strings | **FIXED.** `FormatScalar` emits `decimal.ToString(CultureInfo.InvariantCulture)`, so `1e2` becomes `100` and `1E-3` becomes `0.001`. Pins compare by value: numbers as boxed `decimal` (1 == 1.0 == 1e0), strings ordinally. That is **conservative and documented**: Dataverse string `eq` is case- and accent-insensitive, so `"Fee"` and `"fee"` are refused as ambiguous rather than guessed equal. `SignalWriteRequest.FactValues` doc is softened: for a string pin, the pinned literal is what was tested, and the stored spelling may differ | `Compile_Number_IsEmittedAsACanonicalInvariantDecimal` (×4), the `1`/`1.0` and `100`/`1e2` rows of the two same-value theories, `Compile_TwoExistsClausesPinningStringsDifferingOnlyInCase_IsAmbiguous` |
| L4 | "Defined whatever the operator" is false for `<>` on a `when` field | **FIXED (doc).** The `TemplateEligibleFields` doc, the `Classify` comment and the `FactValues` doc now say: one row, so at most one value, but possibly NULL (Dataverse `ne` includes nulls). `RenderSentence` refuses a null fact, so it surfaces as a refused write. Eligibility is unchanged; the main session flags it for task 031 | — (doc) |
| L5 | No positive case for two clauses pinning the same value | **FIXED** | `Compile_TwoExistsClausesPinningTheSameFieldToTheSameValue_IsTemplateEligible` (×4), `ValidateForSave_TemplateReferencingFieldPinnedToTheSameValueByTwoClauses_IsAccepted` (×3). Run C shows these fail when ">1 pin is always ambiguous" |
| L6 | `PolicyVersionInvalid` doc said six sub-reasons | **FIXED.** It now names all seven | — (doc) |
| L7 | The round-2 mutation summary over-claimed | **FIXED.** See the corrected paragraph under "Rework round 2" below | — |

## Rework round 2 (2026-10-04) — second independent review FAIL; disposition of every finding

Every finding is FIXED. **What the round-2 mutation run actually did (corrected in round 3, finding L7):** one
combined Signals run with nine edits.

- **Eight edits removed round-2 code**: the F1 `Guid.Empty` check; the F2 length checks in the validator and the
  schema validator; the F3 pin rule (unpinned treated as eligible, and conflicting pins treated as eligible);
  the F7 schema `when` reference and the compiler's decimal bound; the F8 `messageTemplate` walk; the F9
  `ResolveSchema` ordering; the F12 full-width braces; and the F13 `NumberStyles.None`.
- **The ninth disabled the renderer's `HasMalformedPlaceholder` call.** That is **round-1** code, not a round-2
  fix. F5 was test-only.

Result: 243 → 47 failures, all in tests named below. The `RenderSentence_MalformedPlaceholder_Throws` rows
failed **only because of that ninth edit**. They show the F5 test detects a missing renderer check; they do not
show a round-2 code fix. Of the F12 rows, only the full-width ones depend on round-2 code. The lone-ASCII-brace
rows were already refused by round 1. The architecture guard was mutated separately: a rogue `Compile` call was
injected into `SignalWriter`, and the validator was switched back to `Compile`. Both facts failed with the
expected messages. The originals were then restored.

| # | Finding | Disposition | Pinned by |
|---|---|---|---|
| F1 | `TryPrepareForEvaluation(valid, Guid.Empty)` logged 50301 + metered `compile_refused`, blaming a valid policy for a caller bug | **FIXED.** `subjectId == Guid.Empty` throws `ArgumentException` (`ParamName = "subjectId"`) **before** validation and outside its catch-all, so nothing is logged or metered. This is now documented as one of exactly two caller-contract throws (with the null-snapshot `ArgumentNullException`) in the class remarks and the method's `<exception>` docs | `TryPrepareForEvaluation_EmptySubjectId_ThrowsArgumentException_AndLogsAndMetersNothing` (asserts the throw, zero log entries and zero metric reasons) |
| F2 | The 32 KB cap ran after a full schema evaluation under the process-wide lock; the schema was also evaluated twice | **FIXED.** `RuleBodySchemaValidator.MaxRuleBodyLength` (32 768) is the one number. `PredicateCompiler.MaxRuleBodyLength` is an alias of it. It is enforced before any parse in `RuleBodySchemaValidator.Validate`, in `PolicyVersionValidator.ValidateInternal` (new bounded reason `rule_body_too_large`) and in the compiler. **Duplicate evaluation removed**: the validator evaluates the schema once and then calls the new `internal PredicateCompiler.CompileSchemaValidated`, which runs every compiler check except the schema | `ValidateForSave_OversizedBodyThatIsAlsoInvalidJson_IsRefusedForSize_NotParsed`, `ValidateForSave_BodyExactlyAtTheCap_IsNotRefusedForSize`, `TryPrepareForEvaluation_OversizedBody_RecordsRuleBodyTooLargeReasonTag`, `RuleBodySchemaValidatorTests.Validate_WithOversizedBodyThatIsAlsoInvalidJson_ReturnsTheSizeError_NotTheJsonError`; single evaluation by arch fact `PolicyVersionValidatorNeverCallsTheSchemaEvaluatingCompile` |
| F3 | Template tokens could name an `exists` field that matches N rows with N values (undefined value, §3.1) | **FIXED per coordinator decision.** `CompiledPredicate.PositiveReadFields`/`AmbiguousPositiveFields` are replaced by `TemplateEligibleFields`, `AmbiguousTemplateFields` and `UnpinnedTemplateFields` (disjoint). They are defined in ONE place, `PredicateCompiler.PositiveFieldUse.Classify`. A field is eligible when it is a subject `when` field (any operator), or an `exists` field that every occurrence pins to exactly one value (bare scalar, `{"=":v}`, or a one-element `in`), with all pins agreeing. A field is ambiguous when it is on more than one entity, read by both `when` and `exists` (two rows), or pinned to different values. `SignalWriteRequest.FactValues` doc now tells task 031: **for an `exists` token the value IS the clause's pinned literal**. The old test `Compile_SameEntityReferencedByTwoExistsClauses_FieldIsNotAmbiguous` was deleted | Validator: `..._TemplateReferencingRangeFilteredExistsField_IsRefused_AsNotPinned`, `..._TemplateReferencingMultiValueOrNotEqualExistsField_IsRefused` (×2), `..._TemplateReferencingEqPinnedExistsField_IsAccepted` (×3), `..._TemplateReferencingWhenFieldWithRangeFilter_IsAccepted`, `..._TemplateReferencingFieldPinnedToDifferentValuesByTwoClauses_IsRefused_AsAmbiguous`. Compiler: `Compile_LivePathB_HasNoTemplateEligibleField_*`, `Compile_WhenFieldWithRangeFilter_IsTemplateEligible`, `Compile_ExistsFieldPinnedToOneValue_IsTemplateEligible` (×3), `Compile_ExistsFieldNotPinnedToOneValue_*` (×3), `Compile_TwoExistsClausesPinningTheSameFieldToDifferentValues_IsAmbiguous`, `Compile_TwoExistsClausesOneRangeOnePinned_FieldIsNotTemplateEligible` |
| F4 | Stale evidence in these notes and the POML | **FIXED.** The STOP-STATUS block is removed. "What was built", "Acceptance criteria — evidence" and "Known limitation" are rewritten below for the current API, test names and reason constants. The POML `<completion>` is updated. `<status>` and TASK-INDEX are left to the main session | — |
| F5 | `RenderSentence` (the "both sides" half) had no test for the five malformed shapes | **FIXED** (test only; the code already refused them) | `SignalWriterTests.RenderSentence_MalformedPlaceholder_Throws` (7 rows: the five shapes + F12's two) |
| F6 | The clause-naming test only asserted each error contains `:` | **FIXED.** It now asserts the real JSON Pointer locations `/all/0` (the `bind` property) and `/all/1/filter/sprk_revisedon` (the `$`-dereference) | `ValidateForSave_ValidJsonFailingExistenceSchema_IsRefusedNamingTheOffendingClause_ReasonSchemaInvalid` |
| F7 | `when` values skipped per-value schema rules (`1e999999` went into FetchXML verbatim) | **FIXED in both layers.** Schema: a new `$defs/filterConditions` (the field→`filterValue` grammar) is shared by `when` and the clause `filter`. `filter` = `filterConditions` + `minProperties: 1`. **Deviation from the literal `#/$defs/filter`:** referencing `filter` directly would have refused `"when": {}`, which the schema documents as "all subjects" and the live Path B body uses. Compiler: `FormatScalar` refuses a number not representable as `System.Decimal`, the same bound the schema library enforces through `GetDecimal` | Schema: `ValidateForSave_WhenValueBreakingTheFilterValueGrammar_IsRefused_ReasonSchemaInvalid` (×3), `RuleBodySchemaValidatorTests.Validate_WithWhenValueBreakingTheFilterValueGrammar_IsRefused` (×4), `Validate_WithEmptyWhen_StillMeansAllSubjects`. Compiler: `CompileSchemaValidated_OutOfRangeNumberInWhen_IsRefusedByTheCompiler` (×2), `Compile_InRangeDecimalNumber_PassesThroughVerbatim` |
| F8 | `then.messageTemplate` accepted but never checked | **FIXED: refused** (decision and evidence below) | `ValidateForSave_MessageTemplateInsideRuleBody_IsRefused_NamingItsLocation` (×3), `RuleBodySchemaValidatorTests.Validate_WithMessageTemplateInsideBody_IsRefused_NamingItsLocation` (×3) |
| F9 | `Validate` doc claimed "never throws for ANY input" but throws `NotSupportedException` for Threshold/Switch | **FIXED: doc corrected and behaviour made consistent.** `ResolveSchema` now runs first, so a type with no authored schema throws for **any** body, before the body is read. Before, a blank or malformed body returned a Failure for these types while `"{}"` threw. The doc states the one throwing case. `PolicyVersionValidator` behaviour is unchanged: it refuses those types as `rule_type_unsupported` before calling `Validate` | `Validate_WithRuleTypeHavingNoAuthoredSchemaYet_ThrowsWhateverTheBody` (×2) |
| F10 | Misnamed tests | **FIXED.** `..._SchemaInvalidBody_RecordsCompileRefusedReasonTag` → `TryPrepareForEvaluation_UnverifiedJoin_RecordsCompileRefusedReasonTag`. `..._AmbiguousTemplateField_*` → `TryPrepareForEvaluation_NotExistsOnlyTemplateField_RecordsTemplateTokenOutsideReadSetReasonTag`. `Compile_PositiveReadFields_*IncludesExistsAndWhenFields` was replaced by `Compile_LivePathB_*`, plus a direct when-field assertion in `Compile_WhenFieldWithRangeFilter_IsTemplateEligible` | — |
| F11 | `ConditionFields` had no consumer; a frozen-set type test (ADR-038 ban); a null-snapshot throw-helper test; no enforcement of the single compile path | **FIXED.** `CompiledPredicate.ConditionFields` is removed. `Compile_ConditionFields_And_PositiveReadFields_AreFrozenSets` and `Compile_ConditionFields_IncludesBothExistsAndNotExistsFilterAttributes` are deleted, and so is `TryPrepareForEvaluation_NullSnapshot_Throws` (B4). **New arch test** `tests/Spaarke.ArchTests/PredicateCompilerCallerGuardTests.cs`. NetArchTest is type-granular and cannot express the rule, because `SignalWriter` (which reads `EvaluatorGlobalReadableEntities`) and `SignalsModule` (DI) legitimately depend on the type. So the test is **method-level**: it scans IL call sites with Mono.Cecil, which NetArchTest is built on and which is already a transitive dependency, so no new package. NetArchTest still selects the subject type for the non-vacuity check. Only `PolicyVersionValidator` and `PredicateCompiler` may call `PredicateCompiler.Compile*`. It has a positive control: the validator's own call must be detected | `OnlyPolicyVersionValidatorCallsPredicateCompilerCompile`, `PolicyVersionValidatorNeverCallsTheSchemaEvaluatingCompile` |
| F12 | A lone literal brace in prose is refused but undocumented; full-width braces passed | **FIXED.** The authoring rule ("braces are reserved for `{{field}}` placeholders; a single literal brace in prose such as `(see {policy})` is refused — write prose without braces") is in the validator's refusal message, the renderer's exception message, and `HasMalformedPlaceholder`'s doc. U+FF5B/U+FF5D are now refused (`SearchValues` over `{}｛｝`) | `ValidateForSave_MalformedPlaceholder_IsRefused_ReasonTemplateMalformed` (new rows: lone brace, full-width, mixed), `ValidateForSave_MalformedPlaceholderRefusal_StatesTheReservedBraceAuthoringRule`, `RenderSentence_MalformedPlaceholder_Throws` |
| F13 | `TryParseRuleType` accepted `" +100000002 "` | **FIXED.** `int.TryParse(…, NumberStyles.None, CultureInfo.InvariantCulture, …)` — digits only | `RuleBodySchemaValidatorTests.TryParseRuleType_WithSignWhitespaceOrSeparators_IsRefused` (×5), `ValidateForSave_NumericRuleTypeWithSignOrWhitespace_IsRefused_ReasonRuleTypeUnsupported` (×3) |

### F8 decision — `then.messageTemplate` is NOT a designed location; it is refused

- **Where it came from**: early drafts put the template in the body. These are `notes/mvp-technical-spec.md` §3.3
  (a *Threshold* example using single-brace `{field:C0}` syntax) and `notes/ontology-architecture-feedback.md`
  §3.1/§3.3 (marked `[PROPOSED]`).
- **What superseded it**: `notes/schema-draft.md` §4 makes `sprk_messagetemplate` a dedicated column on
  `sprk_policyversion`, next to `sprk_shortheadline` and `sprk_proposedaction` ("Renders `sprk_signal.sprk_sentence`.
  §0.3-bound"). `spec.md` NFR-02 names `sprk_policyversion.sprk_messagetemplate`, task 004 seeded the live row with
  a column template and a body that has **no** `then`, and the evaluator/writer (task 030) reads only the column.
- **So**: a body-embedded template would be §0.3 prose that nothing checks against the read set. It is refused,
  case-insensitively, **anywhere** in the body, with its JSON Pointer (e.g. `/then/messageTemplate`). It is
  reported as `schema_invalid`, and the check runs before schema evaluation. The column remains the single
  source. The schema's `then` description states this.

### Rework round 1 (earlier the same day) — for the record

Findings 1-11 of the first review were fixed in round 1. In summary: there is a two-layer catch-all, and
duplicate keys and `1e999999` are refused. A Json.Schema.Net 7.3.4 thread-safety bug was found independently
and fixed with a lock. The no-leak test is exact. `HasMalformedPlaceholder` covers both sides. The bounded
reason constants are asserted on the metric tag. `TryParseRuleType` uses exact names. The body and `in`-list
length caps were added. The API was renamed to `ValidateForSave` / `TryPrepareForEvaluation`. **Round 2
superseded two of round 1's outcomes**: (a) `ConditionFields` and `PositiveReadFields`/`AmbiguousPositiveFields`
no longer exist (F3, F11), and (b) "`Compile` cannot be restricted" is now enforced by the arch test (F11)
rather than left to review.

## Owner decision this task implements

Admins can author `sprk_policy`/`sprk_policyversion` rows directly in the Spaarke Platform app, bypassing the BFF,
and a policy version is immutable after create. **Validation therefore runs at EVALUATION time, fail closed, not
only at save** (POML constraint "OWNER DECISION 2026-10-03"). The task 020 escalation trigger about non-BFF
authoring is resolved by that decision. No BFF write path for `sprk_policyversion` exists today (grep across
`Sprk.Bff.Api/Api/**` and `Sprk.Bff.Api/Services/**`, 2026-10-04).

## What was built (current state)

`src/server/api/Sprk.Bff.Api/Services/Signals/PolicyVersionValidator.cs` is the single fail-closed seam over
`RuleBodySchemaValidator` (task 020) and `PredicateCompiler` (task 021):

- **`ValidateForSave(ruleTypeRaw, ruleBodyJson, messageTemplate)`** is pure, with no log and no metric, and never
  throws. It is for any BFF write path that comes to exist (ADR-002: one owner per invariant). It returns
  `PolicyVersionValidationResult(IsValid, Errors, Reason)`, with field-level errors and one bounded reason.
- **`TryPrepareForEvaluation(PolicyVersionSnapshot, Guid? subjectId, out CompiledPredicate?)`** is the
  evaluation-time gate and the only sanctioned way to get a `CompiledPredicate`. The arch test enforces this
  for calls, delegates, expression trees, construction and `with`-copies. It does not cover reflection,
  `dynamic` or other assemblies (round 3, L1). On
  refusal it logs `OntologyWriterEvents.PolicyVersionInvalid` (EventId 50301) at Error, carrying only the policy
  version id, the policy code and the reason. It records `ontology.policy.invalid{reason}`, returns `false` and
  sets `compiled = null`. It never throws for the content. It throws only for caller bugs: a null snapshot, or
  `subjectId == Guid.Empty` (F1).
- **Check order** (`ValidateInternal`) is: rule type (exact name, or digits-only option value) →
  **length cap** (`rule_body_too_large`) → schema, evaluated **once** (`schema_invalid`, which includes
  malformed/duplicate-key JSON, unreadable text such as an unpaired surrogate escape, an out-of-range number, a
  body-embedded `messageTemplate` and the reserved `then`) →
  `PredicateCompiler.CompileSchemaValidated` (`compile_refused`) → malformed placeholder (`template_malformed`)
  → every token in `TemplateEligibleFields` (`template_token_outside_read_set`). A final catch-all gives
  `internal_error`.
- **Bounded reasons** (`OntologyWriterFailureReason`): `rule_type_unsupported`, `rule_body_too_large`,
  `schema_invalid`, `compile_refused`, `template_token_outside_read_set`, `template_malformed`, `internal_error`.
- **`CompiledPredicate`** carries `SubjectEntity`, `SubjectIdAttribute`, `FetchXml`, `WindowAnchorUtc`,
  `TemplateEligibleFields`, `AmbiguousTemplateFields` and `UnpinnedTemplateFields` (see F3 for the definition).
- **Shared with the renderer**: `SignalWriter.ExtractTemplateTokens` and `SignalWriter.HasMalformedPlaceholder`.
  There is one token grammar, used by the validator (which refuses) and by `RenderSentence` (which throws).
- **Schema** (`Schemas/existence-rule.schema.json`): `when` and clause filters share `$defs/filterConditions` (F7).
  `then` is RESERVED and refused (`not: {}`, round 3 L2).
- **FetchXML numbers** are emitted as canonical invariant decimals (round 3 L3).
- **Wiring**: `SignalsModule` registers `PolicyVersionValidator` as a singleton (unchanged in round 2).

## Acceptance criteria — evidence (current test names, `tests/unit/domain/Signals/`)

1. **Valid Existence body → accepted.** `ValidateForSave_ValidExistenceBody_NoTemplate_ReturnsSuccess`,
   `ValidateForSave_TemplateReferencingOnlyEligibleFields_ReturnsSuccess`,
   `ValidateForSave_NumericExistenceRuleType_ReturnsSuccess`.
2. **Malformed JSON → field-level error.** `ValidateForSave_MalformedJson_IsRefusedWithFieldLevelError_ReasonSchemaInvalid`.
   No row is created because no BFF write path exists. The contract is that the caller must not create on
   `IsValid == false`.
3. **Valid JSON failing the schema → refused, offending clause named.**
   `ValidateForSave_ValidJsonFailingExistenceSchema_IsRefusedNamingTheOffendingClause_ReasonSchemaInvalid`
   asserts `/all/0` and `/all/1/filter/sprk_revisedon`.
4. **Shape not matching the declared rule type → refused.**
   `ValidateForSave_BodyShapeDoesNotMatchDeclaredRuleType_IsRefused_ReasonSchemaInvalid`. Also covered:
   `ValidateForSave_SchemaValidButCompilerRefuses_IsStillInvalid_ReasonCompileRefused` (the compiler
   enforces more than the schema).
5. **Bypass path documented with mitigation.** See "Known limitation" below.
6. **All tests pass; BFF builds.** See "Verification" below.
7. **Evaluator seam: invalid → no predicate, failure recorded with the policy version id, others continue.**
   `TryPrepareForEvaluation_InvalidBody_ReturnsFalse_OutputsNull_NeverThrows`,
   `TryPrepareForEvaluation_InvalidBody_CompiledOutParamIsNull`,
   `TryPrepareForEvaluation_Refusal_LogsExactlyTheAllowedStructuredFields_NoBodyOrTemplateLeak` (×3),
   `TryPrepareForEvaluation_InvalidBody_RecordsExactReasonTagOnTheMetric`,
   `TryPrepareForEvaluation_ThresholdRuleType_ReturnsFalse_NeverThrows`. This task builds the seam; task 031
   builds the evaluator that calls it.
8. **An invalid body authored in the model-driven app never evaluates as if valid.**
   `TryPrepareForEvaluation` takes only the column values (`PolicyVersionSnapshot`), never a provenance flag, so
   every row passes the same gate. The arch test makes it the only path to a predicate.

## Known limitation — and its mitigation (acceptance criterion 5)

**Gap**: anyone with write access to `sprk_policyversion` can save an invalid `sprk_rulebody` (or a
`sprk_messagetemplate` with an ineligible token) directly through the UCI form or `Xrm.WebApi`. No plugin enforces
it (ADR-002), and no BFF write path exists to intercept it.

**Mitigation (the owner's resolution)**: such a row is never load-bearing. The evaluator (task 031) can obtain a
predicate only through `PolicyVersionValidator.TryPrepareForEvaluation`. That is enforced by
`PredicateCompilerCallerGuardTests` for calls, delegates, expression trees, construction and `with`-copies.
Reflection, `dynamic` and other assemblies remain a review concern. An invalid row therefore produces no Signal, and it is
not silent: it logs EventId 50301 with the policy version id and policy code, and increments
`ontology.policy.invalid{reason}`, within one evaluation cycle.

**Residual risk**: the row itself stays invalid in storage, and only its use is blocked. If a BFF write path for
`sprk_policyversion` is added, it MUST call `ValidateForSave` at save time. That is stated on the class.

## Placement Justification (root CLAUDE.md §10 / `.claude/constraints/bff-extensions.md`)

The change is pure in-process C# validation inside the existing `Services/Signals/` and `Telemetry/` folders.
There is no new endpoint, package, DI registration or Meter, and no AI-internal type (ADR-013 is not
implicated). It is synchronous with no queue or schedule, so ADR-052 does not apply. Round 2 adds no new public
type. It adds an `internal` method (`CompileSchemaValidated`), one reason constant (`RuleBodyTooLarge`), two
public constants and a message helper on `RuleBodySchemaValidator` (`MaxRuleBodyLength`,
`ForbiddenMessageTemplateProperty`, `RuleBodyTooLongMessage`), and a test-only arch file. Round 3 adds one
public constant (`RuleBodySchemaValidator.ReservedThenProperty`, so the refusal and its tests share one name) and
private helpers only.

**Component justification for round 2's new surface (CLAUDE.md §11)**:
- `CompileSchemaValidated`. **Existing**: `Compile`. **Extension**: not possible without the duplicate
  evaluation that F2 removes. **Cost of doing nothing**: two schema evaluations under the process-wide lock on
  every validation.
- `RuleBodyTooLarge`. **Existing**: `SchemaInvalid` / `CompileRefused`. **Extension**: neither describes the
  refusal accurately. **Cost of doing nothing**: an oversized row is mis-metered as whatever its content would
  have produced.
- `UnpinnedTemplateFields`. **Existing**: the eligible and ambiguous sets. **Extension**: an unpinned field fits
  neither. **Cost of doing nothing**: the author cannot be told to pin the field.

## Deviations from the POML's literal text

- **Test location**: the POML names `tests/unit/Sprk.Bff.Api.Tests/Services/Signals`. The live convention for
  this project's Signals tests is `tests/unit/domain/Signals/` (ADR-038 KEEP path, linked into
  `Sprk.Bff.Api.Tests`), and that convention was followed.
- **No BFF write endpoint for `sprk_policyversion` was added.** None exists today. The seam is ready for one, and
  the evaluator is the owner's chosen enforcement point.
- **F7**: `when` references `$defs/filterConditions`, not literally `$defs/filter`, to keep `"when": {}` legal
  (see the F7 row).

## Verification (round 3)

- `dotnet build src/server/api/Sprk.Bff.Api/ --no-incremental`: **0 warnings, 0 errors**.
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj --filter "FullyQualifiedName~Signals"`:
  **274/274 passed, three consecutive runs**. This task's four files account for 244 of them:
  `PolicyVersionValidatorTests` 77, `PredicateCompilerTests` 76, `RuleBodySchemaValidatorTests` 53,
  `SignalWriterTests` 38. The filter also matches 30 tests in other `*Signal*` classes. (Round 2 was 243 total.)
- `dotnet test tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj --filter "FullyQualifiedName~PredicateCompilerCallerGuardTests"`:
  **7/7 passed**: the main fact, the F2 fact, and 5 control-fixture rows.
- Fix-reverted mutation runs (see "Rework round 3"): every negative test fails without its fix.
- **A verification hazard caught along the way:** the originals were restored with `Copy-Item`, which keeps the
  scratch copy's OLD timestamp. Incremental MSBuild therefore kept the last mutated DLL, and one arch run
  briefly saw an injected probe that was no longer in source. Every restored file was touched and the BFF
  rebuilt with `--no-incremental`, and all numbers above come from that clean build. The mutation runs
  themselves were valid: each mutation wrote a fresh timestamp and forced a real compile.
- **Not run here, per instruction**: the full BFF unit suite, publish size, CVE scan and conflict-check. The
  main session runs those next. There is no package or `.csproj` change.

## Files changed (task 022, all rounds)

- `src/server/api/Sprk.Bff.Api/Services/Signals/PolicyVersionValidator.cs` (new in round 0)
- `src/server/api/Sprk.Bff.Api/Services/Signals/PredicateCompiler.cs`
- `src/server/api/Sprk.Bff.Api/Services/Signals/RuleBodySchemaValidator.cs`
- `src/server/api/Sprk.Bff.Api/Services/Signals/Schemas/existence-rule.schema.json`
- `src/server/api/Sprk.Bff.Api/Services/Signals/SignalWriter.cs`
- `src/server/api/Sprk.Bff.Api/Services/Signals/OntologyWriterEvents.cs`
- `src/server/api/Sprk.Bff.Api/Telemetry/OntologyWriterTelemetry.cs`
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/SignalsModule.cs`
- `tests/unit/domain/Signals/{PolicyVersionValidatorTests,PredicateCompilerTests,RuleBodySchemaValidatorTests,SignalWriterTests}.cs`
- `tests/Spaarke.ArchTests/PredicateCompilerCallerGuardTests.cs` (new in round 2; round 3 added the L1 detectors
  and the `PredicateCompilerGuardControlFixtures` negative controls)
