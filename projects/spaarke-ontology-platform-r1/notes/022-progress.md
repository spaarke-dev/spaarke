## STOP-STATUS (machine restarting — written just before shutdown, no new build/test run)

- **Findings 1-11: ALL FIXED** (code + tests written and last verified green before this stop): (1) never-throws
  two-layer catch-all + independently-found Json.Schema.Net 7.3.4 thread-safety bug, fixed with a lock; (2)
  exact structured-field no-leak assertion; (3) `SignalWriter.HasMalformedPlaceholder`, both sides; (4)
  `ReadFields`→`ConditionFields`; (5) `PositiveReadFields`/`AmbiguousPositiveFields` (F5 implemented); (6)
  `TryPrepareForEvaluation` sole path, `Compile` non-public REJECTED WITH REASON (documented); (7) six bounded
  reason constants, exact metric-tag tests; (8) exact rule-type name parsing; (9) `MaxRuleBodyLength`/
  `MaxInListLength` caps; (10) `ToFrozenSet(StringComparer.Ordinal)`; (11) all listed test gaps added.
- **Nothing left unfixed/outstanding** from the review list as of the last test run.
- **Build status at last check (before this stop — NOT re-verified just now, per instruction not to run anything)**:
  `dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` → **0 Warnings, 0 Errors**. Signals-filtered
  test run (`tests/unit/domain/Signals/**`) → **186/186 passed**, 3 consecutive runs, zero flakes.
- Nothing committed by me. All changes are on disk, uncommitted, in this worktree — see `git status` (POML +
  6 modified `src/` files + 3 modified/new test files + this notes file).

---

# Task 022 — Rule-body validation refuses invalid bodies at save: record

> **Date**: 2026-10-04 · **Status**: rework complete after independent review FAIL · **Rigor**: FULL · **Model**: Sonnet @ effort high

## Rework (2026-10-04) — independent review returned FAIL; disposition of every finding

The first pass (below, "What was built" onward) shipped a `PolicyVersionValidator.Validate` /
`.ValidateForEvaluation` API. An independent review reproduced four concrete defects and the coordinator
settled two open design questions; this section is the authoritative record of what changed. **The API
surface was renamed**: `Validate` → `ValidateForSave` (pure, save-time); `ValidateForEvaluation` →
`TryPrepareForEvaluation(PolicyVersionSnapshot, Guid? subjectId, out CompiledPredicate? compiled)` (F6 — the
ONLY path to a compiled predicate). `PredicateCompiler.CompiledPredicate.ReadFields` → three properties:
`ConditionFields` (unchanged meaning, renamed), `PositiveReadFields`, `AmbiguousPositiveFields` (F5, new).

| # | Finding | Disposition |
|---|---|---|
| 1 | "Never throws" was false: duplicate JSON keys (top-level + nested) and `1e999999` escaped as unhandled `ArgumentException`/`FormatException` | **FIXED.** `RuleBodySchemaValidator.Validate` now parses with `JsonDocumentOptions{AllowDuplicateProperties=false}` first (catches duplicates at ANY depth as one `JsonException`) and catches `FormatException`/`OverflowException`/`ArgumentException`/`IndexOutOfRangeException` around the schema `Evaluate` call. `PolicyVersionValidator.ValidateInternal` has its own final catch-all (bounded message = exception TYPE name only). `TryPrepareForEvaluation` wraps its own call into `ValidateInternal` in a second catch. **Independently discovered while fixing this finding, not one of the reviewer's listed items**: Json.Schema.Net 7.3.4's `JsonSchema.Evaluate` is NOT thread-safe for concurrent calls against the SAME `JsonSchema` instance — confirmed with a throwaway repro (`Parallel.For`, 2000-4000 iterations) against the package directly: ~40% false `IsValid=true` on an actually-invalid body, and a separate run threw an unhandled `IndexOutOfRangeException` from inside the library's own `SchemaConstraint.BuildEvaluation`. Since `ExistenceSchema` is a process-wide static singleton and the validator is `AddSingleton`, concurrent calls are a real production shape (concurrent evaluation runs, concurrent future save requests), not a test artifact — a false `IsValid=true` is a **fail-OPEN** against the owner's fail-closed mandate. Fixed with a `lock` around the `Evaluate` call (`RuleBodySchemaValidator.EvaluationGate`); the repro confirmed zero false positives/crashes across the same iteration counts with the lock in place. Two permanent `Parallel.For`-based regression tests pin this (`RuleBodySchemaValidatorTests.Validate_CalledConcurrently*`). |
| 2 | Tautological no-leak test: asserted `"not valid json"` against the real text `"not valid JSON"` — would have passed even with a real leak | **FIXED.** Reassembled as an exact-equality check: the structured field KEY SET (`{PolicyVersionId, PolicyCode, Reason, {OriginalFormat}}`, nothing else) and the fully rendered Message string, both asserted with `.Should().BeEquivalentTo`/`.Should().Be` rather than a substring check. |
| 3 | Malformed placeholders (`{{sprk-x}}`, `{{a b}}`, `{{}}`, `{{x`, `{{{x}}}`) passed and would render literally | **FIXED.** New shared `SignalWriter.HasMalformedPlaceholder` — strips every well-formed `{{token}}` match then checks for ANY leftover single `{`/`}` (not only a doubled residual, which `{{{x}}}` would not leave — it matches the regex's INNER `{{x}}` and leaves single stray braces). Called from `PolicyVersionValidator` (refuses, reason `template_malformed`) AND from `SignalWriter.RenderSentence` itself (throws `SignalSentenceTemplateException`) — "both sides", per the instruction. Theory test covers all five shapes. |
| 4 | `ReadFields` overclaimed what was "read" (it was filter-condition attributes only, including unreadable `notExists` fields) | **FIXED.** Renamed `ConditionFields`; doc corrected to state exactly what it is (both `exists` AND `notExists` filter keys) and explicitly NOT to use for template-token validity. |
| 5 | (Coordinator design decision) §0.3 template vocabulary = positive fields only | **IMPLEMENTED.** `CompiledPredicate.PositiveReadFields` = subject `when` fields + `exists` clause fields (never `notExists`). `AmbiguousPositiveFields` = field names appearing on >1 distinct positive ENTITY (not >1 clause on the same entity — verified by a dedicated test). The existing test asserting `{{sprk_revisedon}}` (a `notExists`-only field) as valid now asserts it is REFUSED. **What task 031 must put in `SignalWriteRequest.FactValues`** (documented on the class): exactly the values read for the FIRING SUBJECT from its positive clauses — the subject's own field values for a `when`-filtered field; the matched related row's field values for an `exists`-clause field; NEVER a value sourced from a `notExists` clause; never an ambiguous field's value without first disambiguating by entity. |
| 6 | (Coordinator design decision) Make the safe path the only path | **IMPLEMENTED.** `TryPrepareForEvaluation(PolicyVersionSnapshot, Guid? subjectId, out CompiledPredicate? compiled)` is the only method returning a `CompiledPredicate`; `Validate` renamed `ValidateForSave`. `PredicateCompiler.Compile` was **NOT** made non-public — **rejected with reason**, documented on the class: `PredicateCompiler`, `PolicyVersionValidator` and the future evaluator all live in the SAME `Sprk.Bff.Api` assembly, so `internal` would not stop the evaluator from calling `Compile` directly (it only blocks a different assembly, never the actual risk). True single-path enforcement would require nesting `PredicateCompiler` as a private class inside `PolicyVersionValidator`, breaking its own standalone, already-tested public contract (task 021's `PredicateCompilerTests.cs`) and independent DI registration for no real gain. The coordinator's own mitigation (a POML constraint on tasks 031/032) is the right-sized control. |
| 7 | Bounded sub-reasons; assert the metric's reason tag in tests | **FIXED.** Six constants added to `OntologyWriterFailureReason`: `rule_type_unsupported`, `schema_invalid`, `compile_refused`, `template_token_outside_read_set`, `template_malformed`, `internal_error` — replacing the single `policy_version_rule_body_invalid` bucket. Used for both the Error log's `Reason` property and the metric's `reason` tag (one EventId, `reason` distinguishes which). Tests assert the EXACT tag value via the `MeterListener`'s tag callback, not only the count. |
| 8 | Rule-type parsing must be exact names only (no comma/flag-combining) | **FIXED.** `TryParseRuleType` now matches each `RuleType` member by exact `.ToString()` (case-insensitive) in a loop, never `Enum.TryParse` on the raw string (which treats a comma list as a bitwise-OR combination even on a non-`[Flags]` enum). Also fixed a bug this surfaced: `PolicyVersionValidator` was delegating rule-type recognition to `RuleBodySchemaValidator.ValidateRaw`, which folds "unknown ruletype" and "known-type body fails schema" into the SAME failure — every unknown/blank ruletype was being mis-metered as `schema_invalid`. Rule-type parsing is now decided once, directly, in `PolicyVersionValidator.ValidateInternal`, before any schema call. |
| 9 | Bounded refusals: cap `in`-list length and body length in the compiler | **FIXED.** `PredicateCompiler.MaxRuleBodyLength = 32_768` (the shipped Path B body is ~0.6 KB; 32 KB is >50x that while bounding parse/compile cost) and `MaxInListLength = 200` (shipped example uses 2; 200 bounds the FetchXML size and Dataverse's per-query cost for a large `in`). Enforced as bounded `PredicateCompilationException` refusals. **Observation, not implemented** (literal scope of this finding was "in the compiler"): `RuleBodySchemaValidator`'s own parse+schema-evaluate runs BEFORE the compiler's checks, so an extremely large body still pays that cost first; flagging for awareness rather than expanding scope unasked. |
| 10 | `ReadFields` → `ToFrozenSet(StringComparer.Ordinal)` | **FIXED.** All three sets (`ConditionFields`, `PositiveReadFields`, `AmbiguousPositiveFields`) built via `.ToFrozenSet(StringComparer.Ordinal)`. |
| 11 | Missing tests (whitespace/null body; numeric rule type; malformed/uppercase/dotted tokens; template-failure log path; metric tag) | **FIXED.** All added — see `PolicyVersionValidatorTests.cs` (`ValidateForSave_NullOrWhitespaceBody_*`, `*_NumericExistenceRuleType_*`, `*_MalformedPlaceholder_*` theory, `*_UppercaseTokenName_*`, `*_DottedTokenName_*`, `TryPrepareForEvaluation_TemplateFailure_AlsoLogsAndMeters`, `*_RecordsExactReasonTagOnTheMetric`). |

**Test run after the rework**: `tests/unit/domain/Signals/**` (the four maintain-class files touched) —
**186/186 passed**, run three consecutive times with zero flakes (the concurrency fix eliminated the
previously-intermittent `RuleBodySchemaValidatorTests` failure that surfaced DURING this rework, caused by the
library bug above, not by any of this task's own logic). Full BFF suite result recorded separately (see the
POML `<rework>` element / final report — the machine was heavily contended, per the coordinator's own
expectation).

**Publish size**: not re-measured. No `.csproj`/package changes; the diff is ~305 lines of plain C# logic
across 6 modified files + one new ~280-line class, all doc comments (stripped from the compiled DLL) or small
bounded checks — the same order of magnitude as the first pass's measured +0.036 MB. Re-measuring a second
fresh-worktree pair for a change this small was judged not to meet the "grows materially" bar the coordinator
set as the trigger.

## Owner decision this task implements (already in the POML, restated for the record)

Admins can author `sprk_policy`/`sprk_policyversion` rows directly in the Spaarke Platform app, bypassing the
BFF entirely — a policy version is immutable after create, so a bad body saved once is permanent. **Validation
therefore runs at EVALUATION time, fail closed, not only at save.** The task 020 escalation trigger ("a policy
row can be authored by a path that bypasses the BFF") is RESOLVED by this decision: it is not silently accepted
— it is the reason this task's seam exists. No BFF write path for `sprk_policyversion` exists today (confirmed
by grep across `Sprk.Bff.Api/Api/**` and `Sprk.Bff.Api/Services/**`, 2026-10-04) — if one is added later, it
MUST call `PolicyVersionValidator.Validate` at save time per ADR-002 (ask again below, §"Known limitation").

## What was built

**New**: `src/server/api/Sprk.Bff.Api/Services/Signals/PolicyVersionValidator.cs` — the single, reusable,
fail-closed validation seam, on top of the two existing task 020/021 building blocks:

- `Validate(ruleTypeRaw, ruleBodyJson, messageTemplate)` — pure, side-effect-free. Runs
  `RuleBodySchemaValidator.ValidateRaw` (shape), then (for `Existence`) `PredicateCompiler.Compile` (strictly
  MORE than shape — verified joins, global-readable entities, no `$`-refs, Dataverse link-entity limits), then
  the NEW message-template read-set check (see below). Never throws — even a `RuleType` with no authored schema
  yet (`Threshold`/`Switch`) becomes a `Failure`, not a propagated `NotSupportedException`, so a caller looping
  over many policy versions is never aborted by one bad one.
- `ValidateForEvaluation(policyVersionId, policyCode, ruleTypeRaw, ruleBodyJson, messageTemplate)` — the
  fail-closed gate task 031 (the evaluator, not yet built) calls once per policy version before using its body.
  On refusal: logs `OntologyWriterEvents.PolicyVersionInvalid` (EventId 50301) at Error carrying ONLY the policy
  version id + policy code (never the body/template/error text), records
  `OntologyWriterTelemetry.RecordPolicyInvalid(PolicyVersionRuleBodyInvalid)`, and returns `false` — never
  throws, so the evaluator's own loop can `continue` to the next policy (acceptance criterion 7).

**Extended** (not duplicated — CLAUDE.md §11 reuse-first):
- `PredicateCompiler.CompiledPredicate` gained a `ReadFields` property (`IReadOnlySet<string>`) — the union of
  the body's `when` filter + every clause's `filter` attribute keys, populated as a side effect of the existing
  `CompileFilterConditions` walk (one new `readFields.Add(attribute)` line at each of its two call sites). This
  is the flat, un-prefixed field-name vocabulary `SignalWriter.RenderSentence`'s fact snapshot is already keyed
  by, so the new template check and the existing runtime renderer can never disagree about what "a field the
  predicate reads" means.
- `SignalWriter` gained `internal static ExtractTemplateTokens(string template)` — the SAME `{{token}}` regex
  `RenderSentence` already uses, exposed so the validator does not duplicate the token grammar.
- `OntologyWriterEvents` gained `PolicyVersionInvalid` (EventId 50301) — sibling to `WriteRefused` (50300), same
  "one EventId per refusal category, `reason` distinguishes which" discipline.
- `OntologyWriterTelemetry` gained `PolicyInvalidCounter` (`ontology.policy.invalid`) + `RecordPolicyInvalid`,
  on the SAME `Meter("Sprk.Bff.Api.Ontology")` task 030 already registered — no new Meter, no new DI module.
- `OntologyWriterFailureReason` gained `PolicyVersionRuleBodyInvalid` — the bounded-cardinality reason tag the
  log and the metric share.
- `SignalsModule.AddSignalsModule()` gained one line: `services.AddSingleton<PolicyVersionValidator>();`

**New check this task adds beyond task 020/021 (F8/R2, task 030's second independent review)**: a
`sprk_messagetemplate` token (`{{fieldName}}`) that names a field the compiled predicate does NOT itself read is
now refused at validation time. Previously this was only caught by `SignalWriter.RenderSentence` THROWING the
first time an evaluation run happened to need that field and found it absent from the detection-time fact
snapshot — i.e. a mismatch could hide for a long time. It is now caught against `CompiledPredicate.ReadFields`
at the same point every other rule-body problem is caught.

## Acceptance criteria — evidence

1. **Valid Existence body saved → accepted.** `Validate_ValidExistenceBody_NoTemplate_ReturnsSuccess` +
   `Validate_ValidExistenceBody_TemplateReferencingOnlyReadFields_ReturnsSuccess`.
2. **Malformed JSON → field-level error, no row.** `Validate_MalformedJson_IsRefusedWithFieldLevelError`. (No
   row is created because no BFF write path exists yet to create one — see "Known limitation" below; the
   validator's contract is that the caller must not proceed to create on `IsValid == false`.)
3. **Valid JSON failing the Existence schema → refused, offending clause named.**
   `Validate_ValidJsonFailingExistenceSchema_IsRefusedNamingTheOffendingClause` — every error string is a JSON
   Pointer location, never a bare boolean.
4. **Body shape not matching declared rule type → refused.**
   `Validate_BodyShapeDoesNotMatchDeclaredRuleType_IsRefused` (schema `type` const mismatch) AND
   `Validate_SchemaValidButCompilerRefuses_IsStillInvalid` (schema-valid but compiler-level refusal — an
   unverified join — which this task's seam ALSO catches, strictly more than task 020 alone would).
5. **Bypass path documented with mitigation.** See "Known limitation" below.
6. **All tests pass; BFF builds.** See "Test run" and "Build" below.
7. **Evaluator seam: invalid policy version → no Signal, failure recorded with the policy version id, continues
   with the others.** `ValidateForEvaluation_InvalidBody_ReturnsFalse_NeverThrows`,
   `ValidateForEvaluation_InvalidBody_LogsStableEventId_CarryingOnlyPolicyVersionIdAndPolicyCode`,
   `ValidateForEvaluation_InvalidBody_IncrementsPolicyInvalidMetric`. This task does NOT build the evaluator
   (task 031's job) — it builds the seam 031 calls, per the POML's explicit scope line.
8. **A rule body invalid against the schema never evaluates as if valid, even when authored directly in the
   model-driven app.** This is exactly what `ValidateForEvaluation` is FOR: it does not know or care how the
   policy version was authored — Platform-app-authored and BFF-authored rows go through the identical
   evaluation-time gate, because `PolicyVersionValidator` takes only the four column values (`sprk_ruletype`,
   `sprk_rulebody`, `sprk_messagetemplate`, plus the id/code for logging), never a provenance flag.

## Known limitation — recorded with its mitigation (acceptance criterion 5 / step 5)

**Gap**: Nothing prevents a model-driven-app admin (or anyone with write access to `sprk_policyversion`) from
saving an invalid `sprk_rulebody` directly via `Xrm.WebApi` / the UCI form. No Dataverse plugin enforces this
(ADR-002: Spaarke ships no plugins) and no BFF write path for this table exists today to intercept it.

**Mitigation (this is the owner's own resolution, not a gap left open)**: the invalid row is never load-bearing,
because the EVALUATOR (task 031) is the thing that actually turns a policy version into a Signal, and task 031
MUST call `PolicyVersionValidator.ValidateForEvaluation` before using any policy version's body (documented on
the class itself, in `SignalsModule`'s registration comment, and here). An invalid row therefore:
- never produces a Signal (fails closed), and
- is NOT silent — it logs `OntologyWriterEvents.PolicyVersionInvalid` (stable EventId 50301) and increments
  `ontology.policy.invalid{reason}`, so an admin/operator watching the metric or the log sees it within one
  evaluation cycle, not "no Signals ever fire and nobody notices why."

**Residual risk**: the row ITSELF is still invalid in storage (Dataverse has no column-level CHECK constraint
here) — only its USE is blocked. If a future task adds a BFF write path for `sprk_policyversion` (none exists
today), that path MUST call `PolicyVersionValidator.Validate` at save time (ADR-002: one owner per invariant,
and the save-time rejection is strictly cheaper than discovering it at every future evaluation cycle). This is
stated as a MUST in the new class's own XML doc remarks, not left to be rediscovered.

**This is NOT the WP-3 "bypasses the BFF" failure mode the task 020 escalation trigger warns about** — that
trigger fires when a bypass means the invariant is NOT enforced at all. Here the invariant (no Signal from an
invalid body) IS enforced, just at a different point (evaluation, not save) — which is the owner's own explicit
decision, made BEFORE this task started, specifically to close this gap. The escalation trigger is therefore
resolved, not silently accepted; see POML `<constraints>` "OWNER DECISION 2026-10-03" and root CLAUDE.md §6.5
path reasoning (this is path A-shaped — a documented, narrow, owner-approved deviation from "validate once at
the single write owner" — except it is not even a deviation: ADR-002's "one owner per invariant" is satisfied
by the EVALUATOR being that one owner for USE of the body, which is the only thing that matters operationally).

## Placement Justification (root CLAUDE.md §10 / `.claude/constraints/bff-extensions.md`)

All new code is pure C# domain logic inside the existing `Services/Signals/` + `Telemetry/` folders of
`Sprk.Bff.Api` — no new endpoint, no new package, no new DI module (`AddSignalsModule()` gained one
`AddSingleton` line), no AI-internal type touched (ADR-013 is not implicated — this is validation logic, not an
AI capability). `PolicyVersionValidator`'s three dependencies (`RuleBodySchemaValidator`, `PredicateCompiler`,
`ILogger<T>`) are all either already-registered singletons or framework types. Per ADR-052 / §A.1 of
`bff-extensions.md`: this is synchronous, in-process, no latency/queue/schedule component — there is no
"elsewhere" question to answer; it stays exactly where its two collaborators already live.

## Component justification (CLAUDE.md §11, for `PolicyVersionValidator` as the one new public class)

1. **Existing** — `RuleBodySchemaValidator` (shape only, task 020) and `PredicateCompiler` (compiles + now
   exposes `ReadFields`, task 021). Neither owns "is this whole policy version usable, and if not, say so
   loudly with a stable identity attached."
2. **Extension** — not onto either existing class: `RuleBodySchemaValidator`'s own XML doc states its ADR-013
   scope is pure JSON-in/bool-out, no I/O, no logging; `PredicateCompiler` is pure for the same reason (no
   logger, no telemetry dependency). Adding logging/metric/policy-identity concerns to either would break their
   documented, already-tested scope.
3. **Cost of doing nothing** — task 031 (the evaluator) would either skip the fail-closed check entirely (an
   admin-authored invalid body silently never fires, with NOTHING surfacing why — the exact silent-failure mode
   the owner's 2026-10-04 no-silent-failure directive targets) or hand-roll schema+compile+template-token+
   logging+metric inline, risking the EventId/reason-vocabulary drift CLAUDE.md's telemetry conventions exist
   to prevent (two independently-invented reason strings for what is conceptually one failure class).

## Deviations from the POML's literal text

- **Test location**: the POML's `<outputs>` named `tests/unit/Sprk.Bff.Api.Tests/Services/Signals` (empty —
  confirmed by directory listing before writing anything). The project's OWN binding convention (confirmed live
  in this worktree: `RuleBodySchemaValidatorTests.cs`, `PredicateCompilerTests.cs`, `SignalWriterTests.cs` — all
  of task 020/021/030's own tests) puts Signals domain tests at `tests/unit/domain/Signals/` (ADR-038 KEEP
  path, linked into the `Sprk.Bff.Api.Tests` assembly via `<Compile Include="..\domain\**\*.cs">`). Followed the
  live convention, not the POML's stale path, per the task's own `<steps mode="directional">` contract.
- **No BFF write-endpoint for `sprk_policyversion` was added.** The task's `<goal>` is about making an invalid
  body unsavable; since no save PATH exists today, "unsavable" is satisfied by the seam being ready for
  whichever path (BFF write endpoint, or — per the owner decision — the evaluator) comes to exist. Confirmed by
  grep (see "Owner decision" above) rather than assumed.

## Test run (this task's files, isolated)

`dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj --filter "FullyQualifiedName~Signals"` →
**148 passed, 0 failed** (21 new `PolicyVersionValidatorTests` + 127 pre-existing `PredicateCompilerTests` /
`RuleBodySchemaValidatorTests` / `SignalWriterTests`, all still green after the `CompiledPredicate.ReadFields`
addition).

Full BFF unit suite result + publish-size measurement + CVE scan: recorded in the task's `<completion>` element
and the final report to the main session (not duplicated here to avoid two sources of truth going stale
independently).

## Files changed

- NEW `src/server/api/Sprk.Bff.Api/Services/Signals/PolicyVersionValidator.cs`
- NEW `tests/unit/domain/Signals/PolicyVersionValidatorTests.cs`
- MODIFIED `src/server/api/Sprk.Bff.Api/Services/Signals/PredicateCompiler.cs` (`CompiledPredicate.ReadFields`)
- MODIFIED `src/server/api/Sprk.Bff.Api/Services/Signals/SignalWriter.cs` (`ExtractTemplateTokens`)
- MODIFIED `src/server/api/Sprk.Bff.Api/Services/Signals/OntologyWriterEvents.cs` (`PolicyVersionInvalid`)
- MODIFIED `src/server/api/Sprk.Bff.Api/Telemetry/OntologyWriterTelemetry.cs` (`PolicyInvalidCounter` +
  `PolicyVersionRuleBodyInvalid`)
- MODIFIED `src/server/api/Sprk.Bff.Api/Infrastructure/DI/SignalsModule.cs` (one registration line + doc update)
