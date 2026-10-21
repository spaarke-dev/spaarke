# Task 115: AddedFee and Discount billing rule types

Design source: `procedure-and-file-model.md` §6.3 ("Rule types the examples need"), O-16. ontology-r1's model-level position is in `procedure-model-reviews.md` §1 item 6 and §8 answer 3. Branch `task/ebill-115-ruletypes` (from `work/ebilling-r1`).

## 1. Status of the gates

| Step | State |
|---|---|
| 1 Design (this note) | Written 2026-10-21. |
| 2 ontology-r1 design review | **Pending.** The option values and the three outcomes were agreed at model level (reviews §1 item 6, §8 answer 3). The parameter shapes, the facts read and the decisions in §6 have not been reviewed yet. Code was written in parallel at the main session's direction ("code and scripts only; live steps gated"). If the review changes the design, the code follows it. |
| 6 Option script | Written, **not run** against any environment. |
| 8 ontology-r1 PR review | Pending. |
| 9 Dev option run | Pending the owner's OK, after the ontology-r1 schema review of the script and its -WhatIf output (TASK-INDEX owner gates: 080, 088, 115). |
| Publish-size delta | Not measured: see §8. |

## 2. Option values

| Value | Name (= option label) | Domain | Catalogue position |
|---|---|---|---|
| 100000015 | AddedFee | Billing | 12 |
| 100000016 | Discount | Billing | 13 |

- 100000014 is SubmissionIntegrity (049, D-125), a system type with no catalogue position. So the rule "position N = option value − 100000002" holds for positions 1–11 only. The new types are positions 12 and 13.
- Read-only check of dev on 2026-10-21 (`describe tables/sprk_policyversion`): `sprk_ruletype` has 100000000–100000013. 100000014 (049) is not on dev yet, and 100000015 and 100000016 are free. The escalation trigger "already used with another label" does not fire.
- **Label deviation.** The POML gives the labels "Added fee" and "Discount". Every existing option label is the enum member name (`AiObservation`, `SubmissionIntegrity`), and `RuleTypeOptionSetSyncTests` requires label = name. The labels are therefore **`AddedFee`** and `Discount`. Renaming every label to display text is a separate change that ontology-r1 owns.

## 3. AddedFee (100000015)

A fee the firm adds to an invoice (O-16: a late fee is just an added fee): a late fee, an administrative fee, interest or another added fee.

### Body (`billing-added-fee.schema.json`)

```json
{ "type": "AddedFee", "feeKind": "lateFee", "allowance": "cap", "feeCodes": ["E120"], "capAmount": 100 }
{ "type": "AddedFee", "feeKind": "administrativeFee", "allowance": "cap", "feeCodes": ["X1"], "capPercent": 2 }
{ "type": "AddedFee", "feeKind": "lateFee", "allowance": "notAllowed" }                      // schema-valid, refused (§3, "Form refusals")
{ "type": "AddedFee", "feeKind": "interest", "allowance": "afterDays", "afterDays": 30 }       // schema-valid, refused (§3, "Form refusals")
```

| Member | Rule |
|---|---|
| `feeKind` | Required. One of `lateFee`, `administrativeFee`, `interest`, `other`. Describes the rule only: no fact carries a fee kind, so the handler does not read it. |
| `allowance` | Required. One of `notAllowed`, `cap`, `afterDays`. |
| `feeCodes` | Required for `cap` (1–500 unique codes, the shared code pattern). They identify the fee's lines. Not allowed for the other two forms. |
| `capAmount` | `cap` only: more than 0, at most 1,000,000,000, in the invoice currency. |
| `capPercent` | `cap` only: more than 0, at most 100, a percent of the invoice's other lines. |
| `afterDays` | `afterDays` only: an integer from 1 to 3650. |

- A `cap` body has exactly one of `capAmount` and `capPercent`.
- A form never carries another form's members, and additionalProperties is false.

### Form refusals (validator)

`PolicyVersionValidator.ValidateBilling` runs at save (`ValidateForSave`, `atSave: true`) and at the billing gate (`TryPrepareBillingRule`). After the schema has passed, it refuses the two forms that cannot be judged from `BillingFactSet`, with the new reason `rule_form_not_supported` (`OntologyWriterFailureReason.RuleFormNotSupported`). RuleSetService maps the reason to the field `ruleBody` (the default arm), and the gate logs and meters it as before.

| Form | Missing fact | Message (start) |
|---|---|---|
| `notAllowed` | A per-line fee kind | "An AddedFee rule with allowance 'notAllowed' cannot be evaluated yet: it needs the fee kind of each invoice line, which invoice extraction does not return. …" |
| `afterDays` | A per-line fee kind and the invoice due date | "An AddedFee rule with allowance 'afterDays' cannot be evaluated yet: it needs the fee kind of each invoice line and the invoice due date, which the billing facts do not carry. …" |

- The refusal is lifted when extraction returns both facts. That work is not in R1's breakdown and is to be filed with `/project-defer-issue-tracking` (§9).

### The cap form (handler `AddedFeeHandler`)

| Item | Rule |
|---|---|
| Fee lines | **Expense** lines whose code is in `feeCodes` (case ignored): the firm-billed expense code, or the AI expense code when none was billed. This is ExpenseRule's D-117 matching. |
| Fee total | The sum of the fee lines' amounts, credits included (a credited fee lowers the total). A line with no amount is left out. |
| Limit, amount | `capAmount`, to the cent. |
| Limit, percent | `ToCent(base × capPercent / 100)`. The base is the sum of the amounts of every other line (fee and expense, credits included); a line with no amount is left out, and a base below 0 counts as 0. |
| Fires | Fee total (to the cent) is **above** the limit. A total equal to the limit holds. No fee line, or a total of 0 or less, gives no finding. |
| Findings | One per rule per invoice, on the first fee line that carries an amount. The cap is a property of the invoice, as DailyHoursCap's is of the day. |
| Reads | Every fee line's code (as ExpenseRule names it), amount and date, plus the reference values `feeTotal` and `feeLimit`. For a percent cap the base is the reference value `feeBase`; the other lines' amounts are not listed one by one. That keeps the finding bounded (O2), and AddedFee never rejects, so 073 never needs them. |
| Reason code | `added_fee.over_cap` |
| Tokens | `line.date`, `feeTotal`, `feeLimit`, `rule.feeKind` |
| Consequence | Flag only. Not reject-eligible (§7). No suggested adjustment. |
| Currency (D-20, D-115) | A `capAmount` is money: on an invoice whose currency is missing, unclear or not USD, its findings are dropped and the version is listed as skipped (the "Rates and expense caps were not checked" finding), as for ExpenseRule's cap. A `capPercent` is a ratio of the invoice's own amounts and runs in any currency. |

## 4. Discount (100000016)

**A check only** ("agreed discount not applied"). It never adjusts an amount.

### Body (`billing-discount.schema.json`)

```json
{ "type": "Discount", "discountPercent": 10 }
{ "type": "Discount", "discountPercent": 10, "tolerancePercent": 1 }
```

| Member | Rule |
|---|---|
| `discountPercent` | Required. More than 0, less than 100: the agreed discount off the rack rate. |
| `tolerancePercent` | Optional. 0 to 100; absent means 0. The same name and meaning as ApprovedRate's. Optional so a drafted "10% discount" clause that states no tolerance is not refused ("never guess a number"). |

### Handler `DiscountHandler`

| Item | Rule |
|---|---|
| Lines | **Fee** lines with a billed rate. Credit lines are skipped (D-105). |
| Rack rate | `line.ApprovedRate` (046's `RateResolution`) with status Found and a `RackRate`. Any other line ("no resolved rack rate") is **skipped**, with no finding. ApprovedRate already reports a rate it could not establish. |
| Limit | `ToCent(rack × (1 − discountPercent / 100) × (1 + tolerancePercent / 100))` |
| Fires | The billed rate to the cent is **above** the limit. A rate equal to the limit or below it holds. |
| Findings | One per offending line. |
| Reads | The line's rate, date and timekeeper as billed (extracted), the credit-test reads (`WithCreditTestReads`), and the reference values `rackRate`, `discountedRate` and the rate card ids. |
| Reason code | `discount.not_applied` |
| Tokens | `line.date`, `line.timekeeperName`, `line.rate`, `rackRate`, `discountedRate`, `rule.discountPercent` |
| Consequence | Flag only. No suggested adjustment. Not reject-eligible. |
| Currency (D-20) | A rate comparison: not run when the currency is missing, unclear or not USD, and the version is listed as skipped (as ApprovedRate). |

## 5. Facts read

Everything the handlers read is already in `BillingFactSet`: line type, amount, billed rate, hours, date, timekeeper, billed and AI expense codes, `RateResolution.RackRate`, and the currency (through the evaluator). No loader fact or extraction field was added. The escalation trigger "cannot be evaluated from facts BillingFactSet already holds" does not fire for the cap form or for Discount, and the two AddedFee forms that would need new facts are refused (§3, "Form refusals").

## 6. Decisions for ontology-r1 to review

1. Labels follow the name convention (`AddedFee`), not "Added fee" (§2).
2. AddedFee fee lines are expense lines matched on `feeCodes` (ExpenseRule's matching). A firm that bills a late fee as a fee line with a task code is not matched. Lines carry no fee kind, so codes are the only key.
3. The fee total and the percent base are net of credits. A line with no amount is left out.
4. AddedFee gives one finding per invoice, on the first fee line. The percent base is cited as one reference value, not as every line's amount.
5. Discount's tolerance is optional (default 0) and multiplies the discounted rate, like ApprovedRate's tolerance multiplies the approved rate.
6. Money skip (D-20): Discount is not run, and an AddedFee `capAmount` is dropped, on a non-USD or unclear currency; `capPercent` runs. `InvoiceComplianceEvaluator` (ontology-owned) gained these two cases.
7. New failure reason `rule_form_not_supported` for the two refused AddedFee forms, rather than reusing `schema_invalid`. The body is schema-valid, and the refusal is about missing facts.
8. Both types are in `BillingRuleCatalogue.Types`, so the rule drafting Action (075) is offered them. Its output schema (`infra/dataverse/outputschemas/billing-rule-draft.schema.json`) gains the two type names and the new parameter members; `BillingRuleDraftActionContractTests` pins that. The deployed Action row keeps its old schema until the mirror is redeployed (task 079), and until then the model cannot propose either type (Structured Outputs enum), which is harmless. The drafting description tells the model that only the AddedFee cap form can be checked.

## 7. Reject eligibility (FR-26, I-5)

- `IsRejectEligible` is false for both types. `AllowedConsequences` is {Flag} for both, so a Reject (or a SuggestAdjustment) is refused at publish through the existing D-89 path (`consequence_not_allowed`). A stored row that carries one is downgraded to Flag at evaluation.
- `RejectCapableReasons` is empty for both.
- The FR-26 list (1, 2, 6–10) is unchanged.

## 8. Not done in this task, and why

- **Publish-size delta:** not measured. No package was added, and the change adds two small embedded JSON schemas and two handler classes, so the delta is a few kilobytes. A fresh-master publish comparison is for the merge into `work/ebilling-r1` or the 079 deploy.
- **Rule Review page (`src/solutions/RuleReview/src/services/ruleTypes.ts`):** has no typed inputs for the two types. By design (077) a type it does not know is shown read-only with its parameters as JSON, and the consequence, wording and clause stay editable. Typed inputs are a follow-up.
- **Dev option run:** gated (§1).

## 9. Deferred

- Extraction of a per-line fee kind and the invoice due date into `BillingFactSet`, which lifts the two AddedFee refusals (POML notes). To file with `/project-defer-issue-tracking` from the main session (it writes `notes/defer-issues.md` and a GitHub issue together).

## 10. Verification (2026-10-21)

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors.
- Unit tests, filter Signals | Finance | RuleSet | BillingRuleDraft (`Sprk.Bff.Api.Tests`): 5859 passed, 0 failed, 5 skipped.
- `Spaarke.ArchTests` (full, including `BillingHandlerPurityGuardTests`): 1311 passed, 0 failed.
- Integration filter Billing | RuleSet | PolicyDomain | Signals (`Sprk.Bff.Api.IntegrationTests`): 69 passed, 2 skipped. The drafting contract and seam tests (`BillingRuleDraftActionContractTests`, `BillingRuleDraftSeamTests`) compile into `Sprk.Bff.Api.Tests` and are among the 5859: 15 passed.
- **Fail before.** With `PolicyVersionValidator.cs`, `InvoiceComplianceEvaluator.cs` and `Entity.xml` reverted to the base commit, 7 tests failed: `RuleTypeOptionSetSyncTests` ×2, the two form refusals at save, the two at the gate, and the D-20 EUR case. They pass with the change.
- **Tests added:**
  - `AddedFeeHandlerTests` (14 cases) and `DiscountHandlerTests` (8 cases);
  - the AddedFee form refusals, the cap-shape refusals and the Reject / SuggestAdjustment publish refusals in `BillingRuleCatalogueTests`;
  - the D-20 case in `InvoiceComplianceEvaluatorTests`.
- **Pinned-set updates:** the catalogue, dispatch, translator, job-handler, mapper, rejection-rule and system-policy tests now count 13 types.
- Every test added falls within the acceptance criteria. The D-20 case is the one addition beyond them: it covers decision 6 (§6).

## 11. Option script

`scripts/ebilling/Deploy-EbillingAddedFeeDiscountOptions.ps1`, a copy of `Deploy-EbillingSubmissionIntegrityOption.ps1`.

- It inserts 100000015 `AddedFee` and 100000016 `Discount` into the LOCAL choice `sprk_policyversion.sprk_ruletype` (InsertOptionValue, solution SpaarkeMaster), publishes the table and verifies by metadata read.
- It is idempotent: an option already present with the same label is skipped, and nothing is published.
- It refuses (exit 2, nothing written) when:
  - the column is a global choice;
  - either value is present with another label, or either label is present under another value;
  - any of 100000000–100000013 is missing or relabelled;
  - 100000014 is present with a label other than `SubmissionIntegrity`.
- 100000014 being absent (049's script not yet run on the environment) is reported, not refused.
- -WhatIf output: not recorded. The script was not run against any environment in this task (no live changes); the -WhatIf run comes with the ontology-r1 schema review.
