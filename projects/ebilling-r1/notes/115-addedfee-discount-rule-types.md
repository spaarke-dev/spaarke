# Task 115: AddedFee and Discount billing rule types

Design source: `procedure-and-file-model.md` §6.3 ("Rule types the examples need"), O-16. ontology-r1's model-level position is in `procedure-model-reviews.md` §1 item 6 and §8 answer 3. Branch `task/ebill-115-ruletypes` (from `work/ebilling-r1`).

## 1. Status of the gates

| Step | State |
|---|---|
| 1 Design (this note) | Written 2026-10-21. |
| 2 ontology-r1 design review | **FINDINGS on `9f412274c5`, fixed (§12).** The option values and the three outcomes were agreed at model level (reviews §1 item 6, §8 answer 3). Code was written in parallel at the main session's direction ("code and scripts only; live steps gated"). D-39 precedence (RuleType alone vs RuleType + feeKind) is open with the owner and unchanged here. |
| 6 Option script | Written. `-WhatIf` run against dev under a GET-only guard (§11); **no write** to any environment. |
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
| `feeKind` | Required. One of `lateFee`, `administrativeFee`, `interest`, `other`. Describes the rule only: no fact carries a fee kind, so the handler does not read it. The finding names it by its label (`FeeKindLabel`: "late fee", "administrative fee", "interest", "added fee"). |
| `allowance` | Required. One of `notAllowed`, `cap`, `afterDays`. |
| `feeCodes` | Required for `cap` (1–500 unique codes, the shared code pattern). They identify the fee's lines. Not allowed for the other two forms. |
| `capAmount` | `cap` only: more than 0, at most 1,000,000,000, in the invoice currency. |
| `capPercent` | `cap` only: more than 0, at most 100, a percent of the invoice's other lines. Not a rate on an overdue balance over time ("1.5% per month"): drafting sends that clause to notConverted. |
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
| Fee total | The sum of the fee lines' amounts, credits included (a credited fee lowers the total). A fee line with no amount is left out. |
| Limit, amount | `capAmount`, to the cent. |
| Limit, percent | `ToCent(base × capPercent / 100)`. The base is the sum of the amounts of every other line (fee and expense, credits included). |
| Percent cap not checked | When the base is 0 or less, or any other line has no amount, the cap is **not compared**: the invoice gets one flag-only `added_fee.not_checked` finding (title "Added-fee limit not checked", its own message, no token; the D-115 pattern), if the fee total is above 0. |
| Fires | Fee total (to the cent) is **above** the limit. A total equal to the limit holds. No fee line, or a total of 0 or less, gives no finding. |
| Findings | One per rule per invoice, **on the invoice** (`sprk_invoice`). The cap is a property of the invoice, so the version in force is the one on the invoice date; a first fee line dated before the version cannot drop the finding. |
| Reads | Every fee line's code (as ExpenseRule names it), amount and date, plus the reference values `feeTotal` and `feeLimit` (`feeLimit` only when compared). For a percent cap the base is the reference value `feeBase`; the other lines' amounts are not listed one by one. That keeps the finding bounded (O2), and AddedFee never rejects, so 073 never needs them. |
| Reason code | `added_fee.over_cap` |
| Tokens | `feeTotal`, `feeLimit`, `rule.feeKind` (the label). No line token: the finding is on the invoice. Wording: "The {{rule.feeKind}} charges matched by their expense codes total {{feeTotal}}, more than the agreed limit of {{feeLimit}}." |
| Consequence | Flag only. Not reject-eligible (§7). No suggested adjustment. |
| Currency (D-20, D-115) | A `capAmount` is money: on an invoice whose currency is missing, unclear or not USD, its findings are dropped and the version is listed as skipped (the "Rates and expense caps were not checked" finding), as for ExpenseRule's cap. A `capPercent` is a ratio of the invoice's own amounts and runs in any currency. |

## 4. Discount (100000016)

**A check only** ("rate above the agreed discounted rate"). It never adjusts an amount. It compares per-line rates only: a firm may give the discount at invoice level (`InvoiceTotalsService` reads the discount total) or give part of it elsewhere, and that is not seen, so neither the wording nor the reason code claims the discount was not applied.

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
| Rack rate | `line.ApprovedRate` (046's `RateResolution`) with status Found and a `RackRate`. When any line in scope has none, the invoice gets **one** flag-only `discount.not_checked_no_rack_rate` finding (title "Discount not checked: no rack rate", its own message, no token; the D-115 pattern), reading those lines' rate and date. The other lines are still checked. |
| Discounted rate | `ToCent(rack × (1 − discountPercent / 100))`, no tolerance. |
| Limit | `ToCent(rack × (1 − discountPercent / 100) × (1 + tolerancePercent / 100))`, carried as its own value `discountLimit`. |
| Fires | The billed rate to the cent is **above** the limit. A rate equal to the limit or below it holds. |
| Findings | One per offending line, plus at most one "not checked" finding on the invoice. |
| Reads | The line's rate, date and timekeeper as billed (extracted), the credit-test reads (`WithCreditTestReads`), and the reference values `rackRate`, `discountedRate`, `discountLimit` and the rate card ids. |
| Reason code | `discount.rate_above_discounted` (renamed from the unreleased `discount.not_applied`) |
| Tokens | `line.date`, `line.timekeeperName`, `line.rate`, `rackRate`, `discountedRate`, `discountLimit`, `rule.discountPercent`. Headline "Rate above the agreed discounted rate"; wording "{{line.timekeeperName}} billed {{line.rate}} on {{line.date}}, above the limit of {{discountLimit}}: the agreed discount of {{rule.discountPercent}} percent off the standard rate of {{rackRate}} gives {{discountedRate}}." |
| Consequence | Flag only. No suggested adjustment. Not reject-eligible. |
| Currency (D-20) | A rate comparison: not run when the currency is missing, unclear or not USD, and the version is listed as skipped (as ApprovedRate). |

## 5. Facts read

Everything the handlers read is already in `BillingFactSet`: line type, amount, billed rate, hours, date, timekeeper, billed and AI expense codes, `RateResolution.RackRate`, and the currency (through the evaluator). No loader fact or extraction field was added. The escalation trigger "cannot be evaluated from facts BillingFactSet already holds" does not fire for the cap form or for Discount, and the two AddedFee forms that would need new facts are refused (§3, "Form refusals").

## 6. Decisions for ontology-r1 to review

1. Labels follow the name convention (`AddedFee`), not "Added fee" (§2).
2. AddedFee fee lines are expense lines matched on `feeCodes` (ExpenseRule's matching). A firm that bills a late fee as a fee line with a task code is not matched. Lines carry no fee kind, so codes are the only key.
3. The fee total and the percent base are net of credits. A fee line with no amount is left out of the total; another line with no amount makes a percent cap "not checked" (§12).
4. AddedFee gives one finding per invoice, on the invoice (§12). The percent base is cited as one reference value, not as every line's amount.
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
- **Review round (§12), 2026-10-21.** BFF build 0 errors. Unit filter as above: 5861 passed, 0 failed, 5 skipped. `Spaarke.ArchTests` (full): 1311 passed, 0 failed. Handler tests: `AddedFeeHandlerTests` and `DiscountHandlerTests` 24 cases; with the two handlers reverted to `9f412274c5` (only the reason rename applied so they compile), 10 of them fail, including the no-rack "not checked", the base 0-or-less and missing-amount cases, the invoice subject and the label; all pass with the fix.

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
- **-WhatIf against dev, 2026-10-21, read-only.** Run through a harness that replaces `Invoke-RestMethod` (and `Invoke-WebRequest`) with a proxy that throws on any method other than GET before a request is sent; functions resolve before cmdlets, so the module's `Invoke-RestMethod @p` lands in the proxy. Must-fire control: a POST through the proxy is refused ("GET-ONLY GUARD: refused POST ... (nothing was sent)"). The run made 1 GET and attempted no other method. Exit 0.

```
Task 115: sprk_policyversion.sprk_ruletype options 100000015 AddedFee, 100000016 Discount on https://spaarkedev1.crm.dynamics.com (solution SpaarkeMaster) [WhatIf]
  before: 100000000=Threshold, 100000001=Switch, 100000002=Existence, 100000003=ApprovedTimekeeper, 100000004=ApprovedRate, 100000005=TimeIncrement, 100000006=DailyHoursCap, 100000007=NonBillableActivity, 100000008=ExpenseRule, 100000009=InvoiceAge, 100000010=CoreRecordWindow, 100000011=Duplicate, 100000012=RequiredData, 100000013=AiObservation
  note: 100000014 SubmissionIntegrity is not present yet (task 049's option script has not run here); not required.
  WOULD INSERT 100000015 AddedFee
  WOULD INSERT 100000016 Discount
  WOULD PUBLISH sprk_policyversion
Done: created 2, skipped 0.
GET-ONLY GUARD: 1 GET request(s), no other method attempted.
```

- The real run stays gated on ontology-r1's review of this output and the owner's OK (§1).

## 12. ontology-r1 review of `9f412274c5` (FINDINGS) and the fixes

| # | Finding | Fix |
|---|---|---|
| F2-1 | `discountedRate` included the tolerance. | `discountedRate` = rack × (1 − discount), no tolerance; the tolerance-inclusive limit is its own value and token, `discountLimit`. Wording "above the limit of {{discountLimit}}". |
| F2-2 | "Agreed discount not applied" over-claims: only per-line rates are compared, and a discount may be given at invoice level or in part. | Headline "Rate above the agreed discounted rate"; reason code renamed `discount.not_applied` → `discount.rate_above_discounted` everywhere (unreleased); catalogue refusal text, drafting description, `RuleType` and body-schema descriptions follow. |
| F2-3 | A line with no rack rate was skipped silently. | One flag-only invoice-level `discount.not_checked_no_rack_rate` finding when any in-scope fee line has no resolved rack rate (§4). |
| F2-4 | A percent cap compared against a base of 0 or less, or a base that left out a line with no amount. | Base ≤ 0, or another line with no amount: flag-only `added_fee.not_checked` instead of comparing (§3). Drafting sends "per month on an overdue balance" clauses to notConverted (description and the output schema's `capPercent` description). |
| F3-5 | `rule.feeKind` was rendered raw, and "on this invoice" over-stated what the total covers. | The fact value is `FeeKindLabel(feeKind)` (now internal); wording "charges matched by their expense codes". |
| F3-6 | The cap finding sat on the first fee line, so its date chose the version in force and could drop the finding. | The finding's subject is the invoice; `line.date` left AddedFee's token set. D-39 precedence is unchanged (owner decision pending). |

- Both new reasons have their own message and title (`ReasonTemplate`, `ReasonHeadline`) and are flag-only (`IsFlagOnlyReason`), as D-115's "Rates and expense caps were not checked".
- The output schema's `capPercent` description changed, so the deployed drafting Action picks it up with the 079 mirror redeploy (§6 item 8).
- Design-model text that still says "agreed discount not applied" (`procedure-and-file-model.md` §6.3, `procedure-model-reviews.md` §1 item 6) is ontology-r1's / the design owner's to update; not changed here.

## 13. Follow-up 2026-10-21: precedence key RuleType + feeKind (owner)

The owner decided (2026-10-21) that the D-39 replacement key for AddedFee is **the rule type plus the fee kind**, not the rule type alone. Before, a firm AddedFee rule removed every client-default AddedFee rule, whatever its kind. Now a firm cap on one kind replaces only the client default's cap on that kind; the client default's caps on other kinds stay in force. Every other rule type, Discount included, keeps the rule type alone as its key. Branch `task/ebill-115-feekind`.

| Item | Detail |
|---|---|
| Where | `BillingRuleDefinition.PrecedenceKey` (new: `(RuleType, FeeKind, FeeCodeSet)`; the code set is empty except for the AddedFee kind `other`, see the exact-set row; `(RuleType, null, "")` for every other type); `InvoiceComplianceEvaluator` step 4 builds `firmKeys` from it instead of a set of rule types. The generalised rank in `rule-set-scope-design.md` section 3.3 (not built yet) reads "rule type t" as this key. |
| feeKind comparison | Exact (ordinal). The handler does no trim or case folding: `feeKind` is a closed vocabulary (`lateFee`, `administrativeFee`, `interest`, `other`; schema enum), `FeeKindLabel` is an exact switch, and the gate refuses anything else before a definition exists. So "differing only in case or whitespace" cannot produce a second key: `LateFee`, ` lateFee`, blank and missing kinds are all gate-refused, and a refused firm version never displaces the client default (B2). A test pins each. No new normalisation was invented. |
| Blank or missing kind | The model text is silent. It is not reachable (the schema requires the kind and restricts it to the vocabulary). `PrecedenceKey` still gives a null kind a key of its own (blank only replaces blank), should the schema ever allow it. |
| Fee kind `other`: exact fee-code set (ontology-r1 F3, option a) | `other` covers unrelated fees (courier, postage), so its key also carries the rule's `feeCodes`: a firm `other` cap replaces only the client default's `other` cap on the **same** code set. Normalised as `AddedFeeHandler` matches them (`HashSet<string>(..., OrdinalIgnoreCase)`, `AddedFeeHandler.cs:41`): upper-cased, de-duplicated, sorted, joined with `,`, so order and case do not count (no trim: the schema code pattern admits no whitespace, and a case-only duplicate such as `e107`, `E107` passes `uniqueItems` and collapses to one code). **Exact set, not subset (intended):** `{A}` and `{A,B}` are different keys, so a firm cap on `{A}` leaves the client default's cap on `{A,B}` in force and both run. `lateFee`, `administrativeFee` and `interest` keep `(RuleType, kind)` with no codes. |
| `other` with no fee codes | Not reachable: the schema requires `feeCodes` (1 to 500) on a cap, the gate refuses `notAllowed` and `afterDays`, and the handler refuses an empty list. A firm `other` cap with `"feeCodes":[]` is gate-refused and the client default stays (test). Should one ever exist, the empty set is a key of its own (empty replaces only empty). |
| Scope today | The evaluator has only firm over client default. The four-level model (D-136) is designed, not built; the key applies at every level when it is. |
| Tests | `InvoiceComplianceEvaluatorFeeKindTests` (10 cases at first; 18 with F3: the owner's courier / postage example both effective, four same-set variants replaced, two overlapping-set cases both effective, the empty-codes refusal. F3 seeding: with the codes dropped from the `other` key, the courier / postage test and both overlapping-set cases fail, 3 failed, 15 passed). The first 10: different kinds both effective; same kind, only the firm's; a firm cap replaces only its own kind among two client-default kinds; five out-of-vocabulary kinds are refused and the client default stays; Discount and ApprovedRate keep rule-type-only replacement. Seeding: with the key reverted to rule type alone, the two "both effective" tests fail (2 failed, 8 passed). |
