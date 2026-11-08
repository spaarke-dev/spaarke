# Task 049 part: the SubmissionIntegrity system billing policy (D-123, D-125)

Branch `worktree-agent-aa840f638550c9546`, based on `f2609855e` (the `work/ebilling-r1` tip). Written, not run, against any environment.

## What was built

| Piece | Where |
|---|---|
| `RuleType.SubmissionIntegrity = 100000014` (next free value; `RuleDomains.IsBilling` true) | `Services/Signals/RuleType.cs`, `RuleDomain.cs` |
| Catalogue entries for every per-type switch: schema suffix, `TemplateFactFields`, `IsRejectEligible` false, `AllowedConsequences` = {Flag}, `RejectCapableReasons` empty, `ConsequenceRefusal`, `Describe`; the four reasons' `ReasonTemplate`, `ReasonHeadline`, `IsFlagOnlyReason` | `Services/Signals/Billing/BillingRuleCatalogue.cs` |
| Strict empty body schema `{}` (`additionalProperties: false`) | `Services/Signals/Schemas/billing-submission-integrity.schema.json`, `RuleBodySchemaValidator.cs` |
| Reason codes `submission.invoice_number_mismatch`, `coding.refused_code`, `coding.firm_code_mismatch`, `coding.code_set_needs_review` | `BillingReasonCodes` (in `BillingFinding.cs`) |
| Factory `BillingSystemFindings` (+ `BillingSystemPolicyVersion`) | `Services/Signals/Billing/BillingSystemFindings.cs` |
| Never authorable | `PolicyVersionValidator.ValidateBilling` (all callers, so save, edit, accept, publish and the billing gate) and `ValidateForAuthoring`; absent from the drafting Action (it lists `BillingRuleCatalogue.Types`, which does not hold the system type) |
| Never evaluated from a body | no handler (`BillingRuleHandlers.HandlerFor` returns null); `InvoiceComplianceEvaluator` skips a stored row of the type |
| Rule-set surface | `RuleSetService.FindRuleAsync` refuses a system rule (409 `ruleset.system_rule`) for edit, accept and reject; publish refuses it through the validator |
| Solution source | `SpaarkeMaster/Entities/sprk_PolicyVersion/Entity.xml` carries the 15th option (`RuleTypeOptionSetSyncTests` demanded it) |
| Scripts | `scripts/ebilling/Deploy-EbillingSubmissionIntegrityOption.ps1`, `scripts/ebilling/Seed-BillingSystemPolicy.ps1`; `Deploy-EbillingRuleTypeOptions.ps1` now tolerates the 15th option |
| Data model doc | `docs/data-model/sprk_ruleset.md`, section "`sprk_ruletype` (local choice) - options and the system value" |

The system type is **not** in `BillingRuleCatalogue.Types` (that list is the eleven authorable types, with FR-24 positions). It has no catalogue position, so `CataloguePosition(SubmissionIntegrity)` throws; no caller reaches it (positions are for handler-run rules). It is added to the schema table of `RuleBodySchemaValidator` separately. Tests that meant "every authorable billing type" now iterate `Types` instead of `Enum.GetValues<RuleType>().Where(IsBilling)`.

## Reasons (own template and headline each; each claims only what was compared)

| Reason | Headline | Message |
|---|---|---|
| `submission.invoice_number_mismatch` | Invoice number differs from the submission | The invoice number on the submission, {{submission.invoiceNumber}}, does not match the invoice number read from the document, {{invoice.invoiceNumber}}. |
| `coding.refused_code` | Suggested code not applied | The suggested {{line.codeKind}} code {{line.refusedCode}} is not allowed for this line in its code set, so it was not applied. |
| `coding.firm_code_mismatch` | Billed code differs from the suggested code | The {{line.codeKind}} code on the line, {{line.firmCode}}, differs from the code suggested for it, {{line.suggestedCode}}. |
| `coding.code_set_needs_review` | Suggested code set needs review | The code set used to suggest codes for this invoice, {{codeSet.name}}, was itself suggested and has not been confirmed. |

The 047 notes name the inputs (`RefusedCode`, `HasFirmCodeMismatch`, `CodeSetNeedsReview`) but no reason-code strings, so the brief's names were used unchanged. All four are flag-only reasons (`IsFlagOnlyReason`): the mapper counts them as Flag whatever a stored version says, and writes them on the Decide lane at Info severity.

## How 049 uses it (part D)

`BillingSystemFindings.InvoiceNumberMismatch(policy, typed, read, confidence?)` (invoice level), `CodeRefused(policy, lineIndex, RefusedCode)`, `FirmCodeMismatch(policy, lineIndex, kind, firmCode, suggestedCode)` (line level), `CodeSetNeedsReview(policy, codeSetName)` (invoice level). `policy` is a `BillingSystemPolicyVersion(PolicyId, PolicyCode, PolicyVersionId)` read from the seeded policy row's current version. The findings go to `BillingFindingSignalMapper.MapAll` with a `BillingRuleCandidate` for that version (`RuleType` "100000014", body `{}`, consequence Flag) in `rulesByVersion`. A blank value throws `ArgumentException` (a comparison with nothing on one side is not a mismatch). Open item for part D: the lookup of the policy and its version in force by `sprk_policycode` (the loader does not read it, because it belongs to no rule set).

## Tests

New: `tests/unit/domain/Signals/Billing/BillingSystemPolicyTests.cs`, `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/RuleSetServiceSystemRuleTests.cs`. Must-fire: save, authoring and gate refusals by name and by value; the evaluator skip; the rule-set refusals; the mapper outcome (Flag, own headline, Decide, Info, fingerprint ending `|Flag`, even for a stored Reject on a Do lane). Must-not-fire: existing billing types accepted at save, an Ontology type still compiled by `ValidateForAuthoring`, an ordinary rule still editable, other reasons not flag-only, the drafting list still the eleven.

## Section 11 answers

**The `sprk_ruletype` option value 100000014 (`SubmissionIntegrity`).** Existing: the choice already holds the 14 values; the marker could instead have been a `sprk_policy` column or a validator flag. Extension: not possible, the owner chose a rule type (D-125) so the K2 consequence gate and the type switches apply unchanged. Cost of doing nothing: findings 049 creates have no policy to be written under, and D-123's Signals cannot exist.

**The factory `BillingSystemFindings`.** Existing: `HandlerContext.Finding`, which needs a `BillingRuleDefinition` that only the billing gate can produce (and the gate refuses this type). Extension: not possible without giving the gate a body-less path. Cost of doing nothing: 049 hand-builds findings with unchecked fact tokens, and a template that names a missing token loses the Signal.

**The scripts.** Existing: `Deploy-EbillingRuleTypeOptions.ps1` (inserts the eleven, refuses any option outside the 14 expected) and `Apply-DoLanePolicies.ps1` (reads a JSON definition for authored policies, creates them disabled). Extension: the first would have to be changed to add a twelfth value, and it is the one script that refuses a foreign option, so a separate one-value script is safer; the second creates policies Off and never writes a Billing-domain row. Cost of doing nothing: dev has no option value and no policy row, so the Signals cannot be written.

## Follow-ups and owner questions

1. **Rule Review read-only display not done.** `src/solutions/RuleReview` lists rule sets and their proposals only; the system policy belongs to no rule set, so the page never shows it. A read-only badge would need a read model that lists it; none exists. If the owner wants the policy visible (so an admin can find the on/off switch), that is a new surface (a list endpoint on `RuleSetService` or the Ontology admin, plus UI). Today an administrator turns it off in the model-driven app (`sprk_enabled`) or by the Ontology admin's policy list, which filters to the Ontology domain and therefore does not show it.
2. **Disabling is not yet honoured by 049.** Part D must read `sprk_enabled` and `statecode` on the policy and write no Signal when it is off, while still keeping the findings in the submission's findings JSON (D-125).
3. **No deletion path exists** for a policy outside a rule set (the only deletes are compensation of a failed draft or create); the Dataverse privilege on `sprk_policy` delete is what refuses it. Not changed here. If the owner wants a code-level refusal it needs a delete endpoint to hang it on.
4. **Seed ownership. RESOLVED by D-197 and the `-CustomerBusinessUnit` parameter.** `Seed-BillingSystemPolicy.ps1` takes the optional `-CustomerBusinessUnit` (the customer's top business unit, by id or name), copied from `Apply-DoLanePolicies.ps1` (ontology task 171) with the same behaviour. With it, the `sprk_policy` and `sprk_policyversion` rows the seed CREATES belong to that unit's memberless team "Spaarke Restricted Content Owners" (`ownerid@odata.bind` on both creates) instead of to the operator. The run refuses, before any write, a unit that is the root, not the root's direct child or the Secure Record unit, a team that is missing, ambiguous or has a member, and an operator without Assign on the two tables; a parameter passed blank is refused. `-WhatIf` names the owner; `-Verify` reports an existing row owned by anyone else as DRIFT; an existing row is never re-owned. Without the parameter the script behaves as before. Pester: `tests/scripts/Seed-BillingSystemPolicy.Tests.ps1` (run by the `ontology-script-tests` CI job), which also pins the copied functions to the ones in `Apply-DoLanePolicies.ps1`.
5. **Seed `sprk_subjecttype` label** is looked up as `Invoice` on the live column; the script stops with a precondition failure if the label differs. Not verified against dev (nothing was run).
6. The Ontology admin ("Spaarke Platform" policies list) and `PolicyScopeResolver` already exclude `sprk_domain = Billing`, so the system policy cannot be loaded as an Ontology policy.
