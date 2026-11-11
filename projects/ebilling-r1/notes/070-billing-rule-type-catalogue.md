# Task 070 - Billing rule-type catalogue (FR-24)

> Rigor FULL. Base `9db5d265e` (contains `f292ad119`, #1111). Executed 2026-10-16.

## Result

The eleven FR-24 billing types are members of the shared closed `RuleType` set, each with a schema, describer entry, save-time
validation and a fail-closed billing gate. Every Ontology consumer refuses them, and the Signals readers load Ontology policies
only (allowlist: `sprk_domain` null OR Ontology). Handlers are 071; consequence eligibility is 073.

## Step 0

- `git merge-base --is-ancestor f292ad119 HEAD`: ok.
- 019's domain column on dev: `sprk_policy.sprk_domain` bound to global `sprk_policydomain` (Billing 100000000, Ontology
  100000001, Docketing 100000002), present in SpaarkeMaster source (`Entities/sprk_Policy/Entity.xml`). No collision with an
  ontology column (019 notes, section 1).
- Billing `sprk_ruletype` values on dev before the run: none (only Threshold / Switch / Existence).

## Option values (dev, verified by metadata read)

`scripts/ebilling/Deploy-EbillingRuleTypeOptions.ps1`: `-WhatIf` (11 would-insert, no write) -> run 1 (11 inserted, published
`sprk_policyversion`, verified 14 options) -> run 2 (`created 0, changed 0, skipped 11`). Local choice
`sprk_policyversion_sprk_ruletype`:

| # (FR-24 / FR-26) | Name | Value |
|---|---|---|
| - | Threshold / Switch / Existence | 100000000 / 100000001 / 100000002 (unchanged) |
| 1 | ApprovedTimekeeper | 100000003 |
| 2 | ApprovedRate | 100000004 |
| 3 | TimeIncrement | 100000005 |
| 4 | DailyHoursCap | 100000006 |
| 5 | NonBillableActivity | 100000007 |
| 6 | ExpenseRule | 100000008 |
| 7 | InvoiceAge | 100000009 |
| 8 | CoreRecordWindow | 100000010 |
| 9 | Duplicate | 100000011 |
| 10 | RequiredData | 100000012 |
| 11 | AiObservation | 100000013 |

Position N = value - 100000002 (pinned by `BillingType_NameAndOptionValueAndDomain_ArePinnedBothWays`).

**Solution source (resolved, commit 0ee94c3f6):** SpaarkeMaster `Entities/sprk_PolicyVersion/Entity.xml` now lists all 14
options, the three Ontology ones plus the eleven billing ones above (option (a): 070 edits only the ruletype option set, after 029,
like 015 / 026). Source-only; it ships with the next release import. See "Main-session resolution" below. Since fix-round-1 a
unit test pins the source option set to `Enum.GetValues<RuleType>()`, names and values (`RuleTypeOptionSetSyncTests`).

## Parameters (design.md 6.1 -> schema)

All schemas: `Services/Signals/Schemas/billing-{type}.schema.json` (flat; the csproj glob is not recursive), `type` const,
`additionalProperties: false`, parameters only (no rates, timekeepers, code descriptions, consequence).

| Type | Parameters |
|---|---|
| ApprovedTimekeeper | none |
| ApprovedRate | `tolerancePercent` number 0-100 |
| TimeIncrement | `incrementHours` number (0, 1] |
| DailyHoursCap | `maxHoursPerDay` number (0, 24] |
| NonBillableActivity | `codes` 1-500 unique code identifiers (`^[A-Za-z0-9][A-Za-z0-9._-]{0,19}$`) |
| ExpenseRule | `nonReimbursableCodes` (as above) and / or `maxAmountPerItem` number (0, 1e9]; at least one |
| InvoiceAge | `maxAgeDays` integer 1-3650 |
| CoreRecordWindow | `graceDays` integer 0-365 |
| Duplicate | `matchFields` unique subset of firmInvoiceNumber, timekeeper, lineDate, hours, narrative, amount |
| RequiredData | `fields` unique subset of firmInvoiceNumber, invoiceDate, billingPeriodStart, billingPeriodEnd, timekeeper, lineDate, narrative, taskCode, activityCode, expenseCode |
| AiObservation | `observation` blockBilling / vagueDescription; `minConfidence` number (0, 1] |

Template fact-field sets (`BillingRuleCatalogue.TemplateFactFields`) are the tokens 071's handlers must supply; optional
parameters (ExpenseRule's) are not tokens. The parameter names and the two closed vocabularies are judgment calls (design.md
names the parameters but not their units or the vocabularies); 071 may extend a vocabulary by a reviewed code change.

## Design points

- `RuleDomain` / `RuleDomains` (`Services/Signals/RuleDomain.cs`): exhaustive `DomainOf`, the `sprk_policydomain` values, and the
  allowlist helpers. **Deviation:** the POML put "domain mapping" under `Billing/`; it sits in `Signals/` because the Ontology
  readers depend on it.
- Validator: `ValidateInternal` refuses a non-Ontology type FIRST at evaluation (`rule_type_unsupported`, before length / schema /
  compile); at save a billing type runs `ValidateBilling` (length cap, schema, template vs fact-field set; never compiled).
  `TryPrepareBillingRule` is the billing gate (logs `PolicyVersionInvalid` with id, code and reason only; meters
  `billing.rule.invalid` since fix-round-1, not `ontology.policy.invalid`; never throws for content). `BillingRuleDefinition` has a private constructor and an internal factory called only by the gate, enforced by
  `tests/Spaarke.ArchTests/BillingRuleDefinitionCallerGuardTests.cs` (must-fire controls for call, ldftn, ldtoken).
- `PredicateCompiler.RequireCompilable` was already an allowlist (Existence, Threshold); only its message changed.
- Describer: a present `"type"` that names no member (ordinal, so `"existence"`, a number, null, a comma list) is refused as
  `rule_type_unsupported`; a body with no `"type"` still falls back to Existence (its validation refuses it), so Existence /
  Threshold output is unchanged. Billing types get one deterministic sentence + formal form, no lookup read.
- Readers (allowlist in the query AND per row via `IsOntologyPolicy`, which also rejects a non-`OptionSetValue` value):
  `SignalReevaluationJob.ReadEnabledPoliciesAsync` (nightly + event trigger), `ReevaluatePolicyAsync` (retrieve by id: throws
  `InvalidOperationException` for a non-Ontology policy, like the existing Off / Retired refusal), `PolicyScopeResolver.LoadEnabledPoliciesAsync`,
  `SignalClosureService.PoliciesNotInForceAsync`. `SignalEvaluationTriggerHandler` goes through `EvaluateSubjectAsync`, so it is covered.
- A billing-typed version on a null-domain policy is still read by the nightly job and refused by the gate -> `invalidPolicies`
  +1, run failed (visible), as the POML asks.

## D-88 / D-89 / D-91

- D-88: Duplicate's outcome is the policy version's consequence; nothing in the body or catalogue hard-codes it.
- D-89: this task does not encode consequence eligibility (073 does), so there is no conflict. D-89 holds ApprovedTimekeeper
  through the reference-missing confidence cap (0.80 < 0.90 threshold), not through eligibility; FR-26's "1, 2, 6-10" is pinned
  as catalogue positions only. **For 073:** if it adds an eligibility table, check whether type 1 stays reject-eligible on paper
  (harmless under the cap) or is made non-eligible; that is 073's call.
- D-91: a typed-vs-extracted firm-number mismatch is a job finding (049 / 052), not a rule type; nothing here.

## Fail-closed seeding proof

Each guard was removed on its own (scratch script `seed.py`: replace one anchor, rebuild, run the named tests, restore the
file), 2026-10-16:

| Guard removed | Named test(s) | Seeded result |
|---|---|---|
| `PredicateCompiler.RequireCompilable` allowlist (`if (false)`) | `PredicateCompiler_RefusesEveryBillingBody_ByItsTypeGuard` | 11 / 11 failed |
| Ontology gate: `forOntologyEvaluation ||` dropped (billing type validated as billing at evaluation) | `OntologyEvaluationGate_RefusesEveryBillingRow_RuleTypeUnsupported_LoggedAndMetered` | 11 / 11 failed |
| Billing gate: non-billing branch disabled (`&& ruleType == (RuleType)(-1)`; a constant `false` does not compile, CS0162) | `BillingGate_RefusesOntologyUnknownInvalidAndOversizedRows_LogsMetersNeverThrows` | 4 / 14 failed: exactly the Ontology rows (Existence by name and value, Threshold, Switch) |
| Describer: unrecognised `"type"` falls back to Existence | `Describer_RefusesAnUnrecognisedType_NeverDescribesItAsExistence` | 5 / 5 failed |
| Nightly job per-row allowlist (`ReadEnabledPoliciesAsync`) | `Run_EnabledBillingDomainPolicy_...`, `Run_DocketingDomainPolicy_...`, `EvaluateSubject_EnabledBillingDomainPolicy_...` | 3 / 3 failed |
| Nightly job retrieve-by-id refusal (`ReevaluatePolicyAsync`) | `ReevaluatePolicy_BillingDomainPolicyId_IsRefused_BeforeAnyEvaluation` | 1 / 1 failed |
| Nightly job query allowlist | `Reader_QueriesCarryTheOntologyAllowlist` | 1 / 1 failed |
| `PolicyScopeResolver` per-row allowlist | `ResolveAsync_ReturnsNullAndOntologyDomainPolicies_...` | 1 / 1 failed |
| `SignalClosureService.PoliciesNotInForceAsync` per-row allowlist | `PoliciesNotInForce_ExcludesABillingDomainPolicy` | 1 / 1 failed |

Every file was restored and rebuilt after the run (the restored files were touched first: the restore kept an mtime older than
the seeded DLL, so a plain incremental build reused the seeded assembly once; caught by the next test run).

## Existing tests modified

Two existing assertions pinned the WHOLE enum as the three Ontology members, which AC 2 (14 members) makes false:
`RuleBodySchemaValidatorTests.RuleType_ClosedSet_ContainsExactlyThresholdSwitchExistence` and
`ThresholdRuleTests.TheClosedRuleTypeSet_IsUnchanged`. Each now pins the Ontology DOMAIN to exactly those three (same intent);
no other existing test changed.

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- `tests/unit/Sprk.Bff.Api.Tests` filter `Signals` (all Signals domain + seam tests, existing and new): **1812 / 1812 passed**.
  New: 176 in `BillingRuleCatalogueTests`, 9 in `SignalReevaluationJobTests.BillingDomain.cs`, 2 in
  `PolicyDomainAllowlistSeamTests`.
- `tests/Spaarke.ArchTests`: **1056 / 1056 passed**, incl. `PredicateCompilerCallerGuardTests` and the new
  `BillingRuleDefinitionCallerGuardTests` (1 rule + 3 must-fire controls).
- `dotnet format --verify-no-changes` on `Services/Signals/`: clean for the files this task touched (two pre-existing
  IMPORTS findings in `Services/Signals/Actions/*` are not this task's).
- Self-review (no adversarial-reviewer: cap full): pathological bodies (null, malformed, duplicate keys, `1e999999`, oversized)
  refused at the gate without throwing (tested); `TryParseRuleType` still rejects sign / whitespace / comma forms for billing
  values (tested); the lazy schema map is a `FrozenDictionary` of thread-safe `Lazy<T>` and evaluation stays under the shared
  `EvaluationGate` lock; a JSON-Schema integer written as `90.0` parses (tested); Ontology validation, compile and template
  paths are unchanged (existing 1600+ Signals tests green, unmodified apart from the two closed-set pins below).

## ADR-010, BFF hygiene, publish size

- No interface added: ADR-010 `knownOneToOneCeiling` unchanged (166).
- No package or csproj change; no new CVE surface. Schemas are embedded by the existing glob.
- Publish size (`dotnet publish -c Release`, framework-dependent, PDBs included, `Compress-Archive -CompressionLevel Optimal`,
  fresh `git archive` trees in scratch, 2026-10-16): master `origin/master` 0ccc75b4c = **37.156 MB**; this branch at 4a88c76a1
  (all ebilling-r1 work so far + 070) = **37.303 MB**; delta **+0.147 MB** (well under the +5 MB trigger and the 60 MB ceiling).
  BFF Hygiene section 10 + NFR-01 verified.
- Grep: no configuration file or Dataverse read defines rule types. `sprk_ruletype` is read only as a version's value;
  `config/do-lane.policies.json` holds policy seed rows using the existing name "Existence".

## Ontology-owned files touched (ontology-r1 review required before merge)

Modified: `Services/Signals/RuleType.cs`, `RuleBodySchemaValidator.cs`, `PolicyVersionValidator.cs`, `PredicateCompiler.cs`
(message only), `RuleBodyDescriber.cs`, `SignalReevaluationJob.cs`, `PolicyScopeResolver.cs`, `SignalClosureService.cs`; tests
`tests/unit/domain/Signals/RuleBodySchemaValidatorTests.cs`, `ThresholdRuleTests.cs`. New under `Services/Signals/`:
`RuleDomain.cs`, `Billing/BillingRuleCatalogue.cs`, `Billing/BillingRuleDefinition.cs`, `Schemas/billing-*.schema.json` (11).

### ontology-r1 review

**Not yet obtained** (the executing agent cannot message other sessions). Main session: send ontology-r1 this note and the
diff, name the target branch before the PR opens (their `docs/ontology-platform-design` touches SignalSuppressionService /
DecisionRecordWriter, neither touched here), and paste their answer here. Questions for them:
1. `ReevaluatePolicyAsync` throws for a non-Ontology policy id (same style as Off / Retired). OK, or return a failed result?
2. The Ontology admin tab (D-22) is their task 104, not built yet: it must apply `RuleDomains.AddOntologyPolicyAllowlist` /
   `IsOntologyPolicy` when it lists `sprk_policy`. The Spaarke Platform app's read-only `sprk_policy` views still list billing
   rows (solution views; source edits gated, see escalation).

## Known limits

- (K1) Any future `sprk_policy` reader must select `sprk_domain`: a missing column reads as Ontology (`RuleDomains.IsOntologyPolicy`:
  an absent attribute is an Ontology policy, by the no-backfill rule), so a reader that forgets the column would hand a Billing
  policy to an Ontology pass. Pinned for `ReevaluatePolicyAsync` (`ReevaluatePolicy_RetrieveById_RequestsTheDomainColumn`) and the
  multi-row reads (`Reader_QueriesCarryTheOntologyAllowlist`) only.
- (K2) A policy whose `sprk_domain` moves off Ontology strands its open Signals: the readers stop loading it, so nothing closes its
  Signals as PolicyRetired and the task 145 redaction sweep never reaches them. A privileged data change, not a product path.
- (K3) Task 071's reader must filter to Billing-domain policies itself: the billing gate's snapshot (`PolicyVersionSnapshot`)
  carries no domain, so the gate cannot tell a Billing policy's version from a billing-typed version on a null-domain policy.
- Reflection or another assembly can still call `BillingRuleDefinition.FromValidatedBody` (the ArchTest scans `Sprk.Bff.Api` IL only).
- A privileged user can change `sprk_domain` directly in Dataverse (019 notes addition (c)); the readers then follow the new value.
- An adversarial-reviewer pass was not run: the session's concurrent-subagent cap (4) was full. A self-review covered the
  same checklist (see Verification).

## Fix-round-1 (2026-10-16, after review)

- F3: the Signals fake `RetrieveAsync` now records the column set requested for `sprk_policy`
  (`PolicyRetrieveColumns`); `ReevaluatePolicy_RetrieveById_RequestsTheDomainColumn` asserts `sprk_domain` is requested. Seeded:
  `sprk_domain` removed from `SignalReevaluationJob.PolicyColumns` -> that test red (1 / 1 failed); restored.
- F4: stale comments corrected (`RuleBodyDescriber` describes billing bodies, which the compiler refuses and the validator checks
  without compiling; `RuleDomains.DomainOf` and `RuleType` / `BillingRuleCatalogue` say "switch with a throwing default": an
  unmapped member compiles and throws at runtime, every caller catches it). `ValidateForSave` takes an optional
  `RuleDomain? expectedDomain`; a closed-set type of another domain is refused as `rule_type_wrong_domain` (new
  `OntologyWriterFailureReason.RuleTypeWrongDomain`), an unknown type stays `rule_type_unsupported`, omitted = unchanged. No caller
  passes it yet: 076 (publish) and the Ontology admin tab (104) should pass `Ontology` / `Billing` for their policy. Seeded: check
  disabled -> 15 / 16 of the new tests failed (the 16th is the unknown-type control); restored.
- K1: `TryPrepareForEvaluation`'s tail is `Conclude` (internal): a valid result with no compiled predicate is logged (50301) and
  metered as `internal_error`, then returns false. Seeded: silent `return false` -> `Conclude_ValidResultButNoCompiledPredicate_...`
  red (1 / 1); restored.
- K3: the billing gate meters a new instrument `billing.rule.invalid` (same registered Meter, same reason vocabulary) instead of
  `ontology.policy.invalid`; the Ontology gate is unchanged. The log stays EventId 50301 (its message says "Billing policy
  version"); no new EventId, because the 5030x range is Ontology's. Seeded: gate back on the Ontology meter -> 14 / 14 gate rows
  red; restored.
- K2 sync guard: `RuleTypeOptionSetSyncTests` reads `sprk_policyversion_sprk_ruletype` from SpaarkeMaster `Entity.xml` and equals it
  to the enum, names and values, with must-fire controls for a missing, extra, renamed and renumbered option. Seeded: the
  AiObservation option removed from `Entity.xml` -> red (2 / 2 failed); restored.
- Verification: BFF build 0 warnings / 0 errors; Signals filter 1833 / 1833 (was 1812; +21 new); `tests/Spaarke.ArchTests` 1056 / 1056.

## Main-session resolution (2026-10-16): Entity.xml

Option (a) taken. The 11 billing options (100000003 ApprovedTimekeeper … 100000013 AiObservation) were added to
`src/dataverse/solutions/SpaarkeMaster/Entities/sprk_PolicyVersion/Entity.xml` (`sprk_policyversion_sprk_ruletype`),
with labels copied from a live metadata read of dev, so solution source matches dev. Source-only; it ships with the next
release import (no import, no version bump here).

## ontology-r1 review (2026-10-16): verdict and answers

- **Verdict:** OK to merge to master from the ontology side (range 7fc6b0593..be28df779), provided the paired change below lands in the same PR. All five fix-round claims confirmed; 1833 Signals tests and 1056 ArchTests reproduced on their side; the 5 live dev policies have empty `sprk_domain` and are admitted.
- **Q1 (retrieve-by-id throws for a non-Ontology policy): keep the throw.** Ontology 101's publish reads the policy through the Ontology domain filter before any write and catches a throw from `ReevaluatePolicyAsync` as `reevaluation = failed` (publish still succeeds). Reaching it needs a privileged `sprk_domain` change mid-request, which should be loud; a "failed, nightly retries" result would be false because the nightly job never loads a non-Ontology policy.
- **Q2 / consumers (corrects the earlier "076 / 104" note):** the consumers that must pass `expectedDomain: Ontology` and filter by `RuleDomains` are ontology 101's `ValidateRule` (OntologyAdminAuthoringService) and the 100 rules list (OntologyAdminRulesService), both already on master (e702c3401) with an interim filter (null or 100000001).
- **Paired change (ontology F3), carried in this PR, to be applied after merging origin/master:** OntologyAdminAuthoringService.cs:1226 passes `RuleDomain.Ontology` and maps `RuleTypeWrongDomain => "ruleType"` (fix the :1239 comment); `OntologyDomain` (:167-181) takes Value/Column from `RuleDomains`; OntologyAdminRulesService.cs:48 and :142 build the filter from `RuleDomains` and drop the "swap" comments; test: a billing ruleType to 101's dry run gives 400 on `ruleType` (`rule_type_wrong_domain`) and nothing is evaluated. Without it, a billing draft passes ValidateRule and the engine refuses it later with a generic `ruleBody` error (fail-closed but wrong).
- **Expected conflicts merging master:** ADR010_DITests.cs, RouteAuthorizationGuardTests.Ledger.cs (master count 129), SignalReevaluationJobTests.cs; src/ merges cleanly.
- **Known limits from ontology:** K1 (cosmetic) 100's version label uses Enum.IsDefined, so a mis-seeded null-domain policy with a billing type shows the billing name (needs privileged mis-seeding). K2 open Signals stranded if a policy's domain moves off Ontology (already recorded).

## fix-round-2 (paired change), 2026-10-17

ontology-r1's paired change (ontology F3), applied on top of the origin/master merge 49acd2f78. Commit 27ca05e0f.

**Merge-resolution detail.** Master's task 102a replaced the single `ValidateForSave` call the review cited (OntologyAdminAuthoringService
~:1226) with `PolicyVersionValidator.ValidateForAuthoring`, the one validation that validate, dry run, save and publish all reach
through `ValidateRule`. In the merge, `ValidateForAuthoring` passes `RuleDomain.Ontology` to `ValidateInternal`, so a billing type is
refused there as `RuleTypeWrongDomain` before any length, schema or compile step. No `ValidateForSave` call remains in the service to
change; `PolicyVersionValidator` is unchanged by this round.

**What changed**
- `OntologyAdminAuthoringService.ValidateRule` (the shared reason-to-field map; no per-route code): `RuleTypeWrongDomain => "ruleType"`,
  and the refusal's reason code is `ontology.admin.rule_type_wrong_domain` (`ReasonRuleTypeWrongDomain`, namespaced like every authoring code at ontology-r1's request; before this it fell to the
  `ruleBody` default arm with no code). The "type is Existence or Threshold" comment now says billing types are refused before that point.
- `OntologyAdminAuthoringService.OntologyDomain` takes `Value` / `Column` from `RuleDomains.PolicyDomainOntology` /
  `PolicyDomainColumn`; `OntologyAdminRulesService.OntologyDomain` and `OntologyOnly()` likewise; both "swap to RuleDomains" comments
  removed. A grep of `Services/Ontology` and `Api/Ontology` finds no other local spelling of `sprk_domain` or the Ontology value (the
  remaining `100000001` literals there are other choices: lane, severity, direction, signal status).

**Refusal shape (billing ruleType, e.g. ApprovedTimekeeper, 100000003, AiObservation)**
- validate: 200, `valid: false`, `errors: [{field: "ruleType", ...}]` only, `reasonCode: "ontology.admin.rule_type_wrong_domain"`, `templateEligibleFields: []`.
- dry run (existing policy and new rule), save, publish: 400 validation problem, `errors` has only `ruleType` (same message as validate),
  `reasonCode: "ontology.admin.rule_type_wrong_domain"`. The engine records no evaluation and Dataverse records no write.

**Tests**
- `OntologyAdminAuthoringEndpointTests.ABillingRuleType_IsRefusedOnRuleType_WithTheWrongDomainCode_ByValidateDryRunSaveAndPublish` (3 rows).
- `BillingRuleCatalogueTests.ValidateForAuthoring_RefusesEveryBillingType_WrongDomain_WithNoTemplateFields` (11 rows, by name and option
  value, plus an Ontology control).
- (c) `ValidateForSave` with expected Billing refusing an Ontology type was already covered by
  `ValidateForSave_ExpectingBilling_RefusesEveryOntologyType_WrongDomain` (fix-round-1); no new test.

**Mutation proofs**
- Whole pre-change `OntologyAdminAuthoringService.cs` restored: integration test red 3 / 3 (`reasonCode` null); unit test unaffected (11 pass).
- Only the `RuleTypeWrongDomain => "ruleType"` arm removed: integration test red 3 / 3 (`errors` keyed `ruleBody`).
- `ValidateForAuthoring` passing `expectedDomain: null`: 14 / 14 red (11 unit: billing body validated as valid; 3 integration). All restored.

**Verification:** BFF build 0 warnings / 0 errors; unit filter `Ontology|PolicyVersion|RuleType|Signal` 2623 passed / 3 skipped / 0 failed
(2626); `tests/Spaarke.ArchTests` 1126 / 1126.

## ontology-r1 re-check of fix-round-2 (2026-10-17)

**Verdict: CLEAN at 288c53acb, with no fix-now findings.**

Scope:
- the 49acd2f78 merge resolution of PolicyVersionValidator;
- the fix range 7496afb1c..288c53acb.

Merge resolution:
- 102a behaviour is fully preserved, and the allow-list still runs on all four routes through the one ValidateForAuthoring call.
- Conclude never hands over a predicate after a refusal.
- The evaluator path is unchanged for Ontology types.

Fix:
- The field mapping is in the one shared map.
- The RuleDomains constants are used.
- The refusal shape holds.
- Nothing is evaluated or written.

Ontology-r1 independently reverted three mutations, and each one failed the tests:
- null domain: 14 of 14 failed;
- field arm: 3 of 3 failed;
- reason-code arm: 3 of 3 failed.

Results: build 0 warnings; filtered tests 2,623 passed, 0 failed; ArchTests 1,126/1,126; `git merge-tree` against ontology's docs branch is conflict-free.

Naming: at ontology-r1's request the reason code is namespaced as `ontology.admin.rule_type_wrong_domain` (cc01731b3). Ontology-r1 checks only that diff.

Known limits accepted by ontology-r1, no action:
- **K-a:** `RuleBodyDescriber.cs:127` calls ValidateForSave with no domain. Its callers read Ontology-filtered policies, so reaching it needs privileged mis-seeding. This is K1 above.
- **K-b:** a blank ruleBody reports "ruleBody required" before the type check. This is harmless.

ontology-r1 checked the namespacing commit cc01731b3 and found it clean. Its run of the `OntologyAdminAuthoringEndpointTests` filter at cc01731b3 passed 96 tests with 0 failures. That clears the 070 paired change (the 49acd2f78 resolution plus 7496afb1c..cc01731b3) on the ontology side for the master PR. **Task 070 is complete as of 2026-10-17.**

Merge note: ontology task 102b changes the same authoring files. Whichever project reaches master second resolves the conflict.
