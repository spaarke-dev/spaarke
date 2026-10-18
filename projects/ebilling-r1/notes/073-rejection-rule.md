# Task 073 - Rejection rule and reject-eligibility at publish (FR-26, I-5, NFR-06, D-89)

> Rigor FULL. Base `33723dbdc` on `work/ebilling-r1`. Executed 2026-10-18.
> Commits (range `33723dbdc..bba56e6e3`, plus this notes commit):
> `7c95567c8` consequence allowed by type (catalogue, validator, rule-set service, evaluator pass-through, fixtures);
> `1ffd98815` settings invariant and reader log; `ccfd1539b` the rejection rule, DI, tests;
> `bba56e6e3` RequiredData real-evaluator tests and the data-model line.

## 1. What shipped

| Piece | Where | What |
|---|---|---|
| Eligibility on the closed catalogue | `Services/Signals/Billing/BillingRuleCatalogue.cs` | `IsRejectEligible` (positions 1, 2, 6 to 10), `IsHoldOnly` (ApprovedTimekeeper, D-89), `AllowedConsequences`, `IsConsequenceAllowed`, `ConsequenceRefusal` |
| Publish-time guard | `PolicyVersionValidator.ValidateForSave(..., billingConsequence)`; `RuleSetService.ValidateRule` passes it | A rule whose type may not carry its consequence is refused with `consequence_not_allowed` on accept, edit, publish and drafting (they all use `ValidateRule`) |
| Evaluation-time gate | `PolicyVersionSnapshot.Consequence`; `TryPrepareBillingRule` | A stored version with a consequence its type may not carry is refused, logged (`PolicyVersionInvalid`) and metered (`billing.rule.invalid`, reason `consequence_not_allowed`) |
| Settings invariant | `EbillingSettings.RejectionInvariantProblem`; `EbillingSettingsReader` | Threshold in (0, 1] and `ReferenceMissingCap` below it. A row that breaks it logs one Error per cache cycle; the rule then rejects nothing |
| The decider | `Services/Finance/SubmissionRejectionRule.cs` (new), registered in `FinanceModule` | `DecideAsync(BillingEvaluationResult)` and the pure `Decide(findings, consequenceOf, settings)` |
| One-line change in 072's file | `InvoiceComplianceEvaluator.cs` | The gate snapshot now carries `candidate.Consequence` (reported in section 7) |
| Telemetry reason | `OntologyWriterFailureReason.ConsequenceNotAllowed` | Shared vocabulary; same instrument as 070's billing refusals |

No endpoint, no column, no package, no interface, no background work. The decider is a concrete singleton (ADR-010). Ontology save
and evaluation paths are unchanged: `PolicyVersionValidatorTests.cs` and every other Ontology test are untouched and green.

## 2. Behaviour

**The three conditions (FR-26).** A finding rejects only when all hold, evaluated in this order, and the first that fails is the
finding's `HeldBecause` (`SubmissionHoldReasons`):

1. The consequence is Reject. Missing, undefined or Flag / Suggest adjustment: held (`consequence_unknown` / `consequence_not_reject`).
2. The type is reject-eligible and nothing narrows it for this finding:
   - not eligible (TimeIncrement, DailyHoursCap, NonBillableActivity, AiObservation, or a non-billing type): `type_not_reject_eligible`;
   - ApprovedTimekeeper: `type_holds_only` (D-89, by type, not by 046's cap staying under the threshold);
   - ApprovedRate with any reason other than `approved_rate.above_approved` (no approved rate to compare against): `reference_missing_holds` (071 judgment call 2);
   - Duplicate at line level: `duplicate_at_line_level` (design 6.1, "Reject (invoice), Flag (line)").
3. The threshold is usable (`settings_unavailable` otherwise: settings null, the read threw, or `RejectionInvariantProblem` is set), and
   every extracted field the finding read (`BillingFinding.ReadFields`, exactly as listed, never inferred) has a combined
   confidence at or above the threshold. No listed field: `no_read_fields`. A confidence that is null or outside 0 to 1:
   `read_field_confidence_unknown`. Below the threshold: `read_field_below_threshold`. `UnreliableFields` names the fields.

A held finding is a Flag; the submission is `Accepted` unless some other finding rejects. `DecidingFindings` lists the rejecting ones.

**Fail closed.** Every "cannot establish" gives Accepted plus a Flag, never Rejected. A settings read that throws is logged (Error)
and treated as "threshold unavailable"; cancellation is rethrown.

**Threshold applied now.** `DecideAsync` reads the settings on every decision (the reader's 30 s cache is its only lag), and compares
`confidence >= threshold` itself. It does not use `BillingValueRead.BelowReliabilityThreshold`, which was set against whatever
threshold the evaluation read.

**Stored Reject that a type may not carry** (a row written around publish): the evaluator's gate already refuses it. If one still
reaches the decider (a hand-built result), the finding is held and one Warning per policy version is logged.

**Withheld Duplicate (071 round 2, D-112).** `ValuesRead` of a `DetailWithheld` finding is returned with every `Value` null, however
the finding was built. Through the real chain (evaluate, decide, map, real `SignalWriter`) no column of the written Signal and
nothing in the serialised decision carries the tracking number; an invoice-level Duplicate with the default template is written
(`Invoice INV-100 duplicates INV-100.1.`). An invoice-level withheld Duplicate still rejects: withholding hides the detail only.

**What the consequence matrix is** (`AllowedConsequences`, design 6.1 narrowed by D-89 and I-5):

| Type | Reject | Flag | Suggest adjustment |
|---|---|---|---|
| ApprovedTimekeeper | no (D-89) | yes | no |
| ApprovedRate | yes | yes | yes |
| TimeIncrement, DailyHoursCap, AiObservation | no | yes | no |
| NonBillableActivity | no | yes | yes |
| ExpenseRule, InvoiceAge, CoreRecordWindow, Duplicate, RequiredData | yes | yes | no |

## 3. Tests

New tests: 267. All in `Sprk.Bff.Api.Tests` (the domain folder is compiled into it).

| File | Tests | Covers |
|---|---|---|
| `tests/unit/domain/Signals/Billing/SubmissionRejectionRuleTests.cs` | 160 | the three-condition rule; each condition false on its own (consequence Flag / Suggest / null / undefined; one read field below; one with no confidence; confidence outside 0 to 1; no read fields; each non-eligible type with Reject at perfect confidence; unreadable threshold: null settings, invariant broken, reader throws); exact-threshold equality; D-89 (threshold 0.80 with an ApprovedTimekeeper Reject rule never rejects; through the real handler; 046's `CombinedConfidenceCalculator` at its cap); ApprovedRate holds with a control; line-level Duplicate; threshold change without a restart; stored AiObservation Reject never rejects and is logged once; multi-finding outcome and order; empty findings; withheld Duplicate chain; evaluator-gate refusals (five types) and passes (six types); RequiredData through the real evaluator; the catalogue matrix and the FR-26 position table; the NFR-06 property test |
| `tests/unit/domain/Signals/Billing/BillingConsequenceGateTests.cs` | 61 | `ValidateForSave` matrix by type and consequence, stable reason `consequence_not_allowed`, undefined values, body-first precedence, Ontology type ignores it, null unchanged; the billing gate with a snapshot consequence is refused, logged and metered on `billing.rule.invalid` only; null passes; the Ontology gate ignores it |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/RuleSetServiceConsequenceTests.cs` | 34 | publish refused for the set and nothing written, for each of the five types; eligible types publish; whole matrix by type; a Proposed rule does not block; accept refused (and decline still possible); edit into a disallowed consequence refused and writes nothing; drafting lists it as not converted; the real refusal through the real endpoint: 400, `ruleset.publish_invalid`, `errors[].reason = consequence_not_allowed` |
| `EbillingSettingsReaderTests.cs` (appended) | 12 | invariant violation logs one Error per load, the row is still returned, out-of-range threshold, seeded values and the no-row defaults log none, `RejectionInvariantProblem` cases |

**NFR-06 property test** (`NFR06_NoCombinationOfAnUnreliableField_EverRejects...`): every billing type, invoice and line subject, five
thresholds, one to four read fields, the bad field at every position, six bad values (null, 0, threshold minus epsilon, half the
threshold, -0.1, 1.5), the others at the threshold or 1. More than 10,000 cases, none rejects; a control in the same grid shows the
grid can reject (reliable reads reject exactly where FR-26 allows).

**Publish endpoint status.** `CodePublishInvalid` maps to **400** (`RuleSetEndpoints.InvalidProblem`), as the POML says. The existing
`Publish_OneInvalidRule_Returns400...` pins the mapping; `APublishRefusedForAConsequence_ReachesTheAdminAs400...` feeds the real
service refusal for an AiObservation Reject rule through the real endpoint and asserts the body.

**Existing tests changed (fixtures only).** Before the fixtures were changed, 39 existing tests failed, all in 075/076 code that
drafts or edits a Reject onto TimeIncrement or DailyHoursCap, which is the behaviour this task forbids:
`RuleSetServiceDraftTests` (the `Increment()` helper and 2 assertions), `RuleSetServiceCompensationTests` (the same helper),
`BillingRuleDraftSeamTests` (the model reply), `RuleSetServiceTests` (2 edit tests: the consequence edit now goes from a stored
Reject to Flag, since a DailyHoursCap rule can only Flag). No assertion was weakened; the expected consequence changed from Reject to
Flag. After the change: 0 failures.

**Suites after the change.**
- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- `Sprk.Bff.Api.Tests` filtered `Billing|RuleSet|Rejection|Finance`: **2,554 passed, 3 skipped (live-Dataverse tests), 0 failed** (2,557).
- `Sprk.Bff.Api.Tests` filtered `Signals|PolicyVersion|Ontology` (the Ontology guard): 3,267 passed, 2 skipped (live), 0 failed.
- `Spaarke.ArchTests`: 1,175 / 1,175.

## 4. Mutation proofs

Each mutation was applied to the committed source, the test project rebuilt, the 073 suites plus 072's evaluator and mapper tests run,
and the file restored (script: session scratchpad `mutate.py`). All 23 are caught. (A first batch of seven used `if (false)`, which the
compiler refuses as CS0162; they were re-run with a non-constant predicate.)

| # | Mutation | Failing tests (count of failing cases) |
|---|---|---|
| M1 | **confidence condition removed** (the proof asked for) | 29: below / unknown / out-of-range / misread rate / missing confidence / NFR-06 property / per-line / twice-read ... |
| M1b | a finding with no read field is accepted as evidence | `AFindingThatListsNoReadField_Holds...` (6) |
| M1c | null confidence counted as reliable | 8: `...OneReadFieldWithNoConfidence`, `ARealEvaluation_WithAMissingConfidence`, NFR-06 property |
| M1d | `>=` becomes `>` | 11: exact-threshold, NFR-06 property, invariant theory |
| M2 | **eligibility condition removed** (the proof asked for) | 7: non-eligible types, non-billing types, stored AiObservation, NFR-06 property |
| M3 | D-89 hold removed | 8: D-89 threshold 0.80, real handler, stored row logged, cap-not-below-threshold, NFR-06 property |
| M4 | consequence condition removed | 8: mixed outcome, Flag / Suggest held, ordinary Flag |
| M4b | unknown / missing consequence treated as Reject | 8: unknown consequence, version not in the evaluation, gate-without-consequence |
| M5 | decider ignores the settings invariant | 11: invariant theory, cap-not-below-threshold, logged warning |
| M6 | missing threshold fails open | 18: unreadable settings, reader failure, invariant |
| M7 | ApprovedRate reference-missing hold removed | 5 |
| M8 | line-level Duplicate hold removed | 5: line Duplicate, mixed outcome, withheld chain, NFR-06 property |
| M9 | withheld Duplicate redaction removed | `AWithheldDuplicate_ShowsNoValueText_HoweverTheFindingWasBuilt` |
| M10 | a settings read failure propagates | `ASettingsReadThatFails_IsLogged_AndNothingRejects` |
| M11 | AiObservation made eligible | 5: non-eligible theory, stored AiObservation, FR-26 positions, NFR-06 property, matrix |
| M12 | **publish-time consequence check removed** (the proof asked for) | 73: validator matrix, gate, publish, accept, edit, drafting, endpoint 400 |
| M13 | `RuleSetService` does not pass the consequence | 25: publish, accept, edit, drafting, endpoint |
| M14 | billing gate ignores the stored consequence | 21: gate refusal, log, meter |
| M15 | 072's evaluator does not pass `candidate.Consequence` | `TheBillingGate_RefusesAStoredVersion_WhoseConsequenceIsReject` (5) |
| M16 | invariant `>=` becomes `>` | 6: invariant theories, reader Error |
| M17 | threshold range check removed | 4 |
| M18 | reader does not log the invariant | 3 |
| M19 | catalogue allows Reject for ApprovedTimekeeper | 9: matrix, hold-only, publish, gate, accept, drafting |

Fail-before for the existing-file edits: the 39 failures listed in section 3 are the before state of the fixture changes. The new
files test types that did not exist before, so they could not compile against the old source; the mutations above stand in for the
"copy the old file back" proof.

## 5. Measurements

Publish (`dotnet publish -c Release -r linux-x64 --self-contained false`, zipped with `Compress-Archive -CompressionLevel Optimal`,
PDBs included; fresh `git archive` exports at `C:\m73\{master,before,after}`):

| Build | Zip | Files | `Sprk.Bff.Api.dll` |
|---|---|---|---|
| master `c690b0ac2` | 39,208,107 B (37.39 MB) | 192 | 19,651,584 |
| before (`33723dbdc`) | 39,630,571 B (37.79 MB) | 192 | 20,703,744 |
| after (`ccfd1539b`; later commits change no `src/`) | 39,637,681 B (37.80 MB) | 192 | 20,722,176 |

Task delta: **+7,110 B (+0.007 MB)** against the before build; branch against master +0.41 MB. Total 37.80 MB (< 55 MB review line;
< +5 MB task line). File counts match. `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`: no vulnerable
packages. No csproj or props change.

## 6. Component justification (CLAUDE.md section 11)

| New surface | 1. Existing | 2. Extension | 3. Cost of doing nothing |
|---|---|---|---|
| `SubmissionRejectionRule` (one concrete singleton) | None decides a submission outcome. `InvoiceReviewService` only rejects a non-invoice document; the evaluator judges rules; the handlers never decide (071) | No: the evaluator is pure and has no settings dependency; deciding there would make 072's findings depend on settings and mix judgment with decision | Nothing turns findings into Accepted / Rejected, so FR-26 cannot be met, or a rejection rests on the consequence alone and breaks I-5 and NFR-06 |
| `BillingRuleCatalogue` eligibility and allowed consequences | The catalogue already holds the closed per-type facts (positions, template fields) | Done in place; no new class | Publish and the gate cannot refuse Reject on an AiObservation rule (I-5); the decider has no code source for eligibility |
| `OntologyWriterFailureReason.ConsequenceNotAllowed` | The shared reason vocabulary (070) | Added one constant | A refusal would reuse `schema_invalid` and hide the cause from the admin, the log and the metric |
| `PolicyVersionSnapshot.Consequence` and `ValidateForSave(billingConsequence)` | The snapshot and the validator that already own save and gate checks | Optional parameters on both | The gate cannot see a stored consequence, so a row written around publish is trusted |
| `EbillingSettings.RejectionInvariantProblem` and the reader's Error | The settings record and reader (025) | One property, one log call | A cap set at or above the threshold lets a reference-missing field support a rejection (D-89) |

BFF placement (`bff-extensions.md`): in the BFF, in process; the decider is a pure function over in-memory findings plus the cached
settings read, called by 049's pipeline job. No endpoint, no hosted service, no package, no AI dependency (so the `PublicContracts`
facade does not apply), no new ceiling count (no interface). Unconditional registration (FinanceModule flag rule).

## 7. Deviations and judgment calls (one line each)

1. **Settings validation is a read-time check, not a refused save.** The POML asks for "settings validation that ReferenceMissingCap < ReliabilityThreshold". No BFF path writes the settings row (it is edited in Dataverse), and ADR-002 forbids a plugin or a business-rule owner. So the reader logs an Error and the rule refuses to act (fail closed). Owner question 2.
2. **One-line edit in 072's `InvoiceComplianceEvaluator.cs`** (required by the POML's "evaluation gate refuses a stored version whose consequence is not allowed"): the `PolicyVersionSnapshot` it builds now includes `candidate.Consequence` (lines 165 to 169). No other change to 072; its tests are unmodified and green. `BillingFactLoader` and `DecisionRecordWriter` are untouched.
3. **A null consequence at the gate is "not stated", not refused.** 072's fixtures carry none, and publish already requires one. The decider treats a missing consequence as a Flag. A non-null value outside the three defined is refused at the gate.
4. **D-89 narrows design 6.1.** The table says ApprovedTimekeeper may Reject; D-89 (explicit in the POML) makes it hold-only, so the allowed set is Flag.
5. **Suggest adjustment is limited to ApprovedRate and NonBillableActivity** (the two types design 6.1 gives a suggested adjustment). This tightens what 075/076 accepted before.
6. **Existing 075/076 fixtures changed** (section 3). They assumed a Reject on TimeIncrement or DailyHoursCap.
7. **Billing gate tests are in a new file**, `BillingConsequenceGateTests.cs`, not appended to `PolicyVersionValidatorTests.cs`, so the Ontology validator tests stay byte-for-byte untouched.
8. **ArithmeticFailCap is not part of the invariant** (only the pair the POML names). Owner question 1.

## 8. Owner questions (with recommendation)

1. **`ArithmeticFailCap` below the threshold is not enforced.** A failed arithmetic check caps a field at 0.50 (046), and the 046 doc says a field that fails arithmetic or has no reference match cannot support a rejection. Only the reference-missing cap is checked. *Recommend:* add `ArithmeticFailCap >= ReliabilityThreshold` to `RejectionInvariantProblem` (one condition, two tests); say yes and it goes in the next touch.
2. **No write-side guard for the settings pair.** The Dataverse column ranges are checked, the pair is not, and nothing in the BFF writes the row. *Recommend:* keep the read-time guard (it fails closed) and add a note to the settings form or the 025 schema script's checklist; do not add a plugin (ADR-002).
3. **A line-level Duplicate rule configured as Reject never rejects** (design 6.1: invoice rejects, line flags). The admin can still choose Reject for it. *Recommend:* leave the save as is; have 077's review page say "rejects only on a duplicate invoice number" next to a Duplicate rule.
4. **RequiredData with Reject almost never fires for a field the extraction never found** (no confidence, so unknown, so held; 071's design, NFR-06). It does fire for a reliably read blank value and for a foreign currency. *Recommend:* accept for R1 and revisit with the calibration report (161).
5. **Drafting.** The draft action's prompt lists "Reject, Flag or SuggestAdjustment" for every type (`infra/dataverse/actions/billing-rule-draft.action.json`). A model proposal of Reject for a TimeIncrement rule is now refused by validation and listed as not converted (the clause is kept for the admin) instead of stored. *Recommend:* tell the model the allowed set per type in the prompt, keep the refusal as the backstop. Not changed here: it is 075's file and a prompt change needs its own contract-test run.
6. **Pre-existing stored rows.** A published billing version whose consequence its type may not carry now stops evaluating (refused by the gate, logged, metered on `billing.rule.invalid`, no finding), not downgraded to Flag. No billing rules should be in force outside dev yet. *Recommend:* a one-time dev query for `sprk_policyversion` rows of those types with Reject (or Suggest on the types that suggest none) before 049 goes live; fix by editing the rule. I ran no query.
7. **Confirm the Suggest-adjustment rows of the matrix** (section 2). They follow design 6.1 exactly; they are listed because they are new enforcement.

## 9. Reviews

- **ontology-r1 review: REQUESTED, not yet held** (this agent cannot message other sessions). Request for the main session: ask ontology-r1 to review the `PolicyVersionValidator` change: `PolicyVersionSnapshot` gained an optional `Consequence` (default null), `ValidateForSave` an optional `billingConsequence`, `ValidateBilling` one check at its end, and `OntologyWriterFailureReason` one constant. Every Ontology path passes neither, so none is changed; `PolicyVersionValidatorTests.cs` and the Ontology gate tests are unmodified and green.
- **Step 9.5 code-review (self, standard depth): no F-class finding.** `SubmissionRejectionRule.cs` is 324 lines, one responsibility (decide); the pure `DecideFinding` is one flat sequence of guard clauses, one per hold reason; cohesive, no decomposition needed. K4 observations, none acted on:
  - `DecideAsync` reads the settings even when there are no findings (cached; harmless).
  - The `confidences.Length == 0` branch is unreachable for a real finding (`ReadFields` is derived from `ValuesRead`) and is kept as a fail-closed guard for a hand-built one.
  - `TryPrepareBillingRule` logs the refusal reason code and the policy code, not the body (asserted).
- **Same-class grep** (rule 6): `ValidateForSave(` callers are `RuleSetService.ValidateRule` (updated) and `RuleBodyDescriber` (describes a body, no consequence). `new PolicyVersionSnapshot(` is built by `InvoiceComplianceEvaluator` (updated) and `SignalEvaluator` (Ontology, no consequence). `sprk_consequence` is written only by `RuleSetService` (create, edit, draft), each after `ValidateRule`; the three paths are covered by tests. Ontology authoring uses `ValidateForAuthoring`, which has no consequence parameter.
- **adr-check:** compliant with every ADR touched.
  - ADR-001 / 008 / 019: no endpoint added; the publish refusal keeps the existing ProblemDetails shape.
  - ADR-002: no plugin; the settings invariant is checked by the BFF on read and fails closed (WP-5 / WP-6).
  - ADR-004 / 036 / 052: no background work.
  - ADR-009: the settings cache is 025's, unchanged.
  - ADR-010: concrete singleton, no interface (ceiling untouched; the ArchTest is green).
  - ADR-013: no AI dependency.
  - ADR-028: no auth or credential change.
  - ADR-029: publish delta measured (section 5).
  - ADR-032: no conditional dependency.
  - ADR-038: pure logic in `tests/unit/domain`, rule-set behaviour over the 076 harness.
- **Process note:** the repository's `lint-staged` pre-commit hook takes a temporary `git stash` of the working tree while it formats and drops it again; each commit here restored the tree cleanly and left no entry. I ran no `git stash` myself.
- **Adversarial review (one pass per task):** not run by this agent. The caller schedules it.
