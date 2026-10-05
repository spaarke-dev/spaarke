# Task 022 — Rule-body validation gate: record

> **Date**: 2026-10-04 · **Status**: rework round 2 complete, awaiting re-review · **Rigor**: FULL
> **Current API**: `PolicyVersionValidator.ValidateForSave(ruleType, ruleBody, messageTemplate)` (pure, save-time) and
> `PolicyVersionValidator.TryPrepareForEvaluation(PolicyVersionSnapshot, Guid? subjectId, out CompiledPredicate?)`
> (the fail-closed evaluation-time gate and the ONLY sanctioned path to a compiled predicate).

## Rework round 2 (2026-10-04) — second independent review FAIL; disposition of every finding

Every finding is FIXED. Each fix has a test that was **verified to fail with the fix reverted**: all fixes were
mutated back out together, and the Signals-filtered run went from 243/243 to 47 failures, every one of them a
test named below (F5 and F12 renderer rows, F1, F2, F3, F7, F8, F9, F13). The architecture guard was mutated
separately: a rogue `Compile` call was injected into `SignalWriter` and the validator was switched back to
`Compile`, and both facts failed with the expected messages. The originals were then restored.

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
  evaluation-time gate and the only sanctioned way to get a `CompiledPredicate` (the arch test enforces this). On
  refusal it logs `OntologyWriterEvents.PolicyVersionInvalid` (EventId 50301) at Error, carrying only the policy
  version id, the policy code and the reason. It records `ontology.policy.invalid{reason}`, returns `false` and
  sets `compiled = null`. It never throws for the content. It throws only for caller bugs: a null snapshot, or
  `subjectId == Guid.Empty` (F1).
- **Check order** (`ValidateInternal`) is: rule type (exact name, or digits-only option value) →
  **length cap** (`rule_body_too_large`) → schema, evaluated **once** (`schema_invalid`, which includes
  malformed/duplicate-key JSON, an out-of-range number and a body-embedded `messageTemplate`) →
  `PredicateCompiler.CompileSchemaValidated` (`compile_refused`) → malformed placeholder (`template_malformed`)
  → every token in `TemplateEligibleFields` (`template_token_outside_read_set`). A final catch-all gives
  `internal_error`.
- **Bounded reasons** (`OntologyWriterFailureReason`): `rule_type_unsupported`, `rule_body_too_large`,
  `schema_invalid`, `compile_refused`, `template_token_outside_read_set`, `template_malformed`, `internal_error`.
- **`CompiledPredicate`** carries `SubjectEntity`, `SubjectIdAttribute`, `FetchXml`, `WindowAnchorUtc`,
  `TemplateEligibleFields`, `AmbiguousTemplateFields` and `UnpinnedTemplateFields` (see F3 for the definition).
- **Shared with the renderer**: `SignalWriter.ExtractTemplateTokens` and `SignalWriter.HasMalformedPlaceholder`.
  There is one token grammar, used by the validator (which refuses) and by `RenderSentence` (which throws).
- **Schema** (`Schemas/existence-rule.schema.json`): `when` and clause filters share `$defs/filterConditions` (F7),
  and `then` documents the `messageTemplate` refusal (F8).
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
   `TryPrepareForEvaluation_InvalidBody_LogsExactlyTheAllowedStructuredFields_NoBodyOrTemplateLeak`,
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
`PredicateCompilerCallerGuardTests`, not left to review. An invalid row therefore produces no Signal, and it is
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
`ForbiddenMessageTemplateProperty`, `RuleBodyTooLongMessage`), and a test-only arch file.

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

## Verification (round 2)

- `dotnet build src/server/api/Sprk.Bff.Api/`: **0 warnings, 0 errors**.
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj --filter "FullyQualifiedName~Signals"`:
  **243/243 passed, three consecutive runs**. This task's four files account for 213 of them:
  `PolicyVersionValidatorTests` 66, `PredicateCompilerTests` 64, `RuleBodySchemaValidatorTests` 45,
  `SignalWriterTests` 38. The filter also matches 30 tests in other `*Signal*` classes.
- `dotnet test tests/Spaarke.ArchTests/Spaarke.ArchTests.csproj --filter "FullyQualifiedName~PredicateCompilerCallerGuardTests"`:
  **2/2 passed**. The arch project builds.
- Fix-reverted mutation runs (see the top of this file): every negative test fails without its fix.
- **Full BFF unit suite**: not run here, per instruction. The main session runs it on a quiet machine.
- **Publish size**: not measured. There is no package or `.csproj` change, and round 2 is plain C# logic, a JSON
  schema edit and doc comments. The first pass measured +0.036 MB against fresh master (POML `<publish-size>`).
- **CVE scan**: not re-run. No package was added.

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
- `tests/Spaarke.ArchTests/PredicateCompilerCallerGuardTests.cs` (new in round 2)
