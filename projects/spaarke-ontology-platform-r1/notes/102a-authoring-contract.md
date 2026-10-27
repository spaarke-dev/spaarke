# 102a: the authoring contract for the rule wizard

Task: [`tasks/102a-authoring-contract-for-rule-wizard.poml`](../tasks/102a-authoring-contract-for-rule-wizard.poml).
It builds on task 101 ([`101-ontology-admin-authoring.md`](101-ontology-admin-authoring.md)).

## What shipped

| Piece | Where |
|---|---|
| `GET /authoring/catalog` gains a `lanes[]` block (Decide, Do), with `actions`, `nextSteps`, `dismissalReasons`, `severities` and `workTypes`. Subjects and joins gain `authorableFields` | `OntologyAdminAuthoringService.BuildCatalog` / `LaneCatalog` |
| `POST /authoring/validate` compiles a draft only and returns 200 `{valid, errors[], reasonCode, templateEligibleFields[]}`. It is gated exactly like the dry run (WhoAmI, then a fresh `prvCreatesprk_PolicyVersion` check as the caller), and a `policyId` is read as the caller (the uniform 404) | `OntologyAdminAuthoringEndpoints.ValidateAsync`, `OntologyAdminAuthoringService.ValidateAsync` |
| The authorable-column allow-list (owner option 101(b)). It **narrows** the dry run's column-value oracle to the listed columns; it does not close it (see "Residual oracle" below) | `PolicyVersionValidator.AuthorableColumns`, `ValidateForAuthoring`; `CompiledPredicate.ReadColumns` |
| One validation path. Validate, dry run, save and publish all go through `ValidateRule` → `ValidateForAuthoring`. A column refusal returns `reasonCode = ontology.admin.column_not_authorable` (a ValidationProblem extension on dry run, save and publish) | `OntologyAdminAuthoringService.ValidateRule` |
| A route-ledger entry for the validate route | `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` (**uac-r2 owns it; needs their review**) |

### Server sources (no value is invented)

| Catalog list | Source |
|---|---|
| `actions` | `DecisionActionCatalog.All` where `PlanLane == lane` (the same test as `DecisionPlanService.Resolve`) |
| `nextSteps` | `DecisionActionCatalog.All` where `IsNextStep` |
| `dismissalReasons` | `DecisionActionCatalog.DismissalReasons(lane)` |
| `severities`, `workTypes` | `OntologyAdminRulesService.SeverityLabels` / `WorkTypeLabels`, which `OntologyAdminRulesSchemaAgreementTests` pins to the solution source |
| `authorableFields` | `PolicyVersionValidator.AuthorableColumns` for that table, without the threshold columns and the join paths |

## Deviations and decisions (all within the brief or approved by the coordinator)

1. **Additions.** `nextSteps[]` and `authorableFields[]` are additions the coordinator approved (2026-10-16). The brief did not name them.
2. **Severities and work types are lane-independent.** The server has no lane mapping for them, so each lane carries the full list. The coordinator approved this. If a lane mapping is wanted later, it needs a server source first.
3. **Seed: `statecode` on `sprk_budget` and `sprk_memo`.** The existing authoring tests (the "no policy subject type" dry run) read them, so they are part of the "columns the tests use" seed.
4. **Threshold columns and join paths are left out of `authorableFields`.** Both are on the allow-list. The catalog offers a threshold column as a `thresholdFields` entry, because a `when` on its stored copy is refused (task 025b). A join path is named by `path`, never as a filter field.
5. **Validate takes `policyId` or `lane`.** With `policyId` it checks as publish would, using the policy's own lane read as the caller. Without one, `lane` is required and it checks as save would, including the policy-subject-type check.
6. **Where it is enforced.** Only in `ValidateForAuthoring`, outside the evaluator (approved by the coordinator). `ValidateForSave`, `TryPrepareForEvaluation` and `RuleBodyDescriber` are unchanged. A version written outside the BFF still evaluates as before; the evaluator shows its answers to no one.
7. **The allow-list check runs before the template check.** `templateEligibleFields` is returned whenever the body compiled, even when the template or the allow-list refused it. It lists only fields whose every read is allow-listed.

## Residual oracle (accepted: the owner re-signed the ADR-028 tension at this scope, D-161)

The dry run evaluates as the evaluator, which reads org-wide; only the rows it shows are filtered by the caller. The allow-list
limits which columns a body may name. It does not stop an `exists` or `notExists` filter on an **allow-listed column of a
joinable related table** from answering about a related row the author cannot read. A policy-version author (a holder of
`prvCreatesprk_PolicyVersion`) can still learn that column's value on such a row, one bit per readable subject row per dry run
(every shown matter answers in the same run). Today those columns are:

| Related table (join path) | Allow-listed filter columns |
|---|---|
| `sprk_communication` (`sprk_regardingmatter`) | `sprk_triagecategory`, `sprk_reviewoutcome`, `sprk_receiveddate`, `statecode` |
| `sprk_budgetrevision` (`sprk_matter`) | `sprk_revisedon`, `statecode` |
| `sprk_budget` (`sprk_matter`, task 063's join, merged in fix round 1) | `statecode`, `sprk_budgetstartdate`, `sprk_budgetenddate` |
| Derived Threshold base reads (no join; adversarial pass 2) | A Threshold on `sprk_totalspendtodate`, `sprk_totalbudget` or `sprk_budgetutilizationpercent` records only the stored subject column, but is decided from org-wide grouped sums (`FinanceBaseValues.BuildGroupedSumFetchXml`, `SignalEvaluator.cs:233`) over `sprk_invoice.sprk_totalamount`, `sprk_invoice.sprk_invoicestatus`, `sprk_budget.sprk_totalbudget`. Not on the allow-list and not in the live `IsSecured` test. A moving threshold reveals the current sum including unreadable invoices (the stored roll-up already shows a possibly stale copy) |

Membership of the related-table filter columns is the owner's call; this task does not change it.

## Known limits (wizard concerns, not defects in this contract)

- **`assign-work` appears in both lists.** It is in the Decide lane's `actions` and in `nextSteps`. The plan resolver refuses a code that appears in both a plan's actions and its next steps. The wizard must not offer one code in both places.
- **Validate does not run the dry run's `ProofScope` (tenant id) check** (adversarial pass 2, K2). It records no proof and reads
  nothing tenant-scoped, and Entra tokens always carry `tid`, so the difference is not reachable.
- **Validate on a retired policy still validates the body.** Publish then refuses the retired policy for its own reason, as it does today.
- **The live `IsSecured` check is one run on one environment** (spaarkedev1, 2026-10-17). If field-level security is enabled on a
  listed column later, or another environment secures one, the oracle reopens for that column until the live test is re-run there.
- **A code in both `actions` and `nextSteps`** gets its own message and the code `ontology.admin.duplicate_action` (review fix
  round 1), not the "outside the lane's catalog" message.

## Evidence

- **Live, read-only (spaarkedev1, 2026-10-16/17):**
  - **50** allow-listed columns after the docs merge in fix round 1: the 48 of the first run, plus `sprk_matter.createdon` (the
    POL-MATTER-NOBUDGET seed) and `sprk_budget.sprk_matter` (task 063's join path). An earlier draft of this note said 51, which
    was wrong. All 50 exist, and every one has `IsSecured == false`. Output of the read-only re-run on 2026-10-17, after the merge
    (`AuthorableColumns_ExistAndAreNotFieldSecured_OnTheLiveSchema`: Passed; 50 of 50 lines say `IsSecured=False`):

    ```
    sprk_budget.sprk_budgetenddate: DateTimeAttributeMetadata, IsSecured=False
    sprk_budget.sprk_budgetstartdate: DateTimeAttributeMetadata, IsSecured=False
    sprk_budget.sprk_matter: LookupAttributeMetadata, IsSecured=False
    sprk_budget.statecode: StateAttributeMetadata, IsSecured=False
    sprk_budgetrevision.sprk_matter: LookupAttributeMetadata, IsSecured=False
    sprk_budgetrevision.sprk_revisedon: DateTimeAttributeMetadata, IsSecured=False
    sprk_budgetrevision.statecode: StateAttributeMetadata, IsSecured=False
    sprk_communication.sprk_receiveddate: DateTimeAttributeMetadata, IsSecured=False
    sprk_communication.sprk_regardingmatter: LookupAttributeMetadata, IsSecured=False
    sprk_communication.sprk_reviewoutcome: PicklistAttributeMetadata, IsSecured=False
    sprk_communication.sprk_triagecategory: LookupAttributeMetadata, IsSecured=False
    sprk_communication.statecode: StateAttributeMetadata, IsSecured=False
    sprk_event.sprk_approveddate: DateTimeAttributeMetadata, IsSecured=False
    sprk_event.sprk_basedate: DateTimeAttributeMetadata, IsSecured=False
    sprk_event.sprk_completeddate: DateTimeAttributeMetadata, IsSecured=False
    sprk_event.sprk_duedate: DateTimeAttributeMetadata, IsSecured=False
    sprk_event.sprk_eventtype_ref: LookupAttributeMetadata, IsSecured=False
    sprk_event.sprk_finalduedate: DateTimeAttributeMetadata, IsSecured=False
    sprk_event.sprk_meetingdate: DateTimeAttributeMetadata, IsSecured=False
    sprk_event.sprk_tododuedate: DateTimeAttributeMetadata, IsSecured=False
    sprk_event.statecode: StateAttributeMetadata, IsSecured=False
    sprk_event.statuscode: StatusAttributeMetadata, IsSecured=False
    sprk_invoice.sprk_invoicedate: DateTimeAttributeMetadata, IsSecured=False
    sprk_invoice.sprk_invoiceduedate: DateTimeAttributeMetadata, IsSecured=False
    sprk_invoice.statecode: StateAttributeMetadata, IsSecured=False
    sprk_matter.createdon: DateTimeAttributeMetadata, IsSecured=False
    sprk_matter.sprk_budgetutilizationpercent: DecimalAttributeMetadata, IsSecured=False
    sprk_matter.sprk_closeddate: DateTimeAttributeMetadata, IsSecured=False
    sprk_matter.sprk_lastreviewdate: DateTimeAttributeMetadata, IsSecured=False
    sprk_matter.sprk_matterid: AttributeMetadata, IsSecured=False
    sprk_matter.sprk_nextreviewdate: DateTimeAttributeMetadata, IsSecured=False
    sprk_matter.sprk_openeddate: DateTimeAttributeMetadata, IsSecured=False
    sprk_matter.sprk_totalbudget: MoneyAttributeMetadata, IsSecured=False
    sprk_matter.sprk_totalspendtodate: MoneyAttributeMetadata, IsSecured=False
    sprk_matter.statecode: StateAttributeMetadata, IsSecured=False
    sprk_memo.statecode: StateAttributeMetadata, IsSecured=False
    sprk_project.sprk_budgetutilizationpercent: DecimalAttributeMetadata, IsSecured=False
    sprk_project.sprk_closeddate: DateTimeAttributeMetadata, IsSecured=False
    sprk_project.sprk_lastreviewdate: DateTimeAttributeMetadata, IsSecured=False
    sprk_project.sprk_nextreviewdate: DateTimeAttributeMetadata, IsSecured=False
    sprk_project.sprk_openeddate: DateTimeAttributeMetadata, IsSecured=False
    sprk_project.sprk_totalbudget: MoneyAttributeMetadata, IsSecured=False
    sprk_project.sprk_totalspendtodate: MoneyAttributeMetadata, IsSecured=False
    sprk_project.statecode: StateAttributeMetadata, IsSecured=False
    sprk_todo.sprk_duedate: DateTimeAttributeMetadata, IsSecured=False
    sprk_todo.statecode: StateAttributeMetadata, IsSecured=False
    sprk_todo.statuscode: StatusAttributeMetadata, IsSecured=False
    sprk_workassignment.sprk_respondedon: DateTimeAttributeMetadata, IsSecured=False
    sprk_workassignment.sprk_responseduedate: DateTimeAttributeMetadata, IsSecured=False
    sprk_workassignment.statecode: StateAttributeMetadata, IsSecured=False
    ```

  - Live policies: all 6 live Ontology policies' current versions pass `ValidateForAuthoring`.
    `EveryLiveOntologyPolicysCurrentVersion_PassesValidateForAuthoring` passed on the same re-run:

    ```
    POL-TASK-OVERDUE: valid=True
    POL-TASK-DUESOON: valid=True
    POL-WA-RESPONSE: valid=True
    POL-TODO-OVERDUE: valid=True
    POL-MATTER-NOBUDGET: valid=True
    POL-COMMIT-BUDGET: valid=True
    ```
  - Tests: `SignalPredicateTests.AuthorableColumns_ExistAndAreNotFieldSecured_OnTheLiveSchema` and `EveryLiveOntologyPolicysCurrentVersion_PassesValidateForAuthoring`.
- **Seeding proofs** (each change was made, the suite run, and the change restored):

  | Change | Tests that failed |
  |---|---|
  | Allow-list check disabled | 7 |
  | Validate route unmapped | 13 (no-token still passes via the fallback 401, as in 101) |
  | Privilege gate removed from `ValidateAsync` | 3 |

- **Publish size** (`dotnet publish -c Release -r linux-x64 --self-contained false`, zipped):

  | | Files | Zip bytes | Raw bytes |
  |---|---|---|---|
  | Base `a956b1d54` | 192 | 39,099,014 | 130,557,212 |
  | Branch | 192 | 39,115,186 | 130,592,680 |

  The delta is **+16,172 B zip (+0.02 MB)**. No package was added.

## Escalation resolved (fix round 1): POL-MATTER-NOBUDGET

The 2026-10-17 live re-run of `EveryLiveOntologyPolicysCurrentVersion_PassesValidateForAuthoring` printed:

```
POL-TASK-OVERDUE: valid=True
POL-TASK-DUESOON: valid=True
POL-WA-RESPONSE: valid=True
POL-TODO-OVERDUE: valid=True
POL-MATTER-NOBUDGET: valid=False all[0].path: 'sprk_budget.sprk_matter' is not a verified lookup from sprk_budget to sprk_matter. ...
```

**Why it fails here.** On this branch the failure comes from the branch base. The `(sprk_matter, sprk_budget)` join came in with
task 063 (`origin/docs/ontology-platform-design`), after `a956b1d54`.

**Why it would still fail after a merge.** After a merge, the join compiles, and its path and `sprk_budget.statecode` are
allow-listed. But the body's `when` reads **`sprk_matter.createdon`**, which is not on the allow-list: it is neither in the seed
nor a Date Only column. So `ValidateForAuthoring` would refuse POL-MATTER-NOBUDGET with `column_not_authorable`.

**Impact.** The evaluator is unaffected, because the allow-list is authoring-only. A dry run, or publishing a new version of that
policy, would be refused.

**Why it is escalated, not fixed.** The brief's trigger "the allow-list would break a live policy" applies, and allow-list
membership is the owner's call.

**The option that keeps the seed rule** ("the columns the live policies read"):
1. Merge `origin/docs/ontology-platform-design` into this branch.
2. Add `("sprk_matter", "createdon")` to the seed. It is a system column; check `IsSecured` with the live test.
3. Add the policy to `AuthorableColumnsTests.LivePolicies`.
4. Re-run both live tests.

**Resolution (coordinator, 2026-10-17).** The seed rule is "exactly the columns the live policies read". POL-MATTER-NOBUDGET is
now a live policy (Off). `sprk_matter.createdon` is a subject `when` column, and subject columns carry no exposure beyond field
security (adversarial pass 1). So adding it is within D-151(b), not an owner call. Done:
1. `origin/docs/ontology-platform-design` was merged into this branch. That brought in the `(sprk_matter, sprk_budget)` join and
   `config/know-promotion.policies.json`.
2. `("sprk_matter", "createdon")` was added to the seed.
3. `AuthorableColumnsTests.EveryPolicyDefinition_PassesValidateForAuthoring` now reads both `config/do-lane.policies.json` and
   `config/know-promotion.policies.json`.
4. Both live tests were re-run read-only: both passed, with 50 of 50 columns `IsSecured=False` and all 6 policies `valid=True`
   (output under "Evidence").
