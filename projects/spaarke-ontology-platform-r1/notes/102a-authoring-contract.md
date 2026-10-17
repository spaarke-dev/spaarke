# 102a: the authoring contract for the rule wizard

Task: [`tasks/102a-authoring-contract-for-rule-wizard.poml`](../tasks/102a-authoring-contract-for-rule-wizard.poml).
It builds on task 101 ([`101-ontology-admin-authoring.md`](101-ontology-admin-authoring.md)).

## What shipped

| Piece | Where |
|---|---|
| `GET /authoring/catalog` gains a `lanes[]` block (Decide, Do), with `actions`, `nextSteps`, `dismissalReasons`, `severities` and `workTypes`. Subjects and joins gain `authorableFields` | `OntologyAdminAuthoringService.BuildCatalog` / `LaneCatalog` |
| `POST /authoring/validate` compiles a draft only and returns 200 `{valid, errors[], reasonCode, templateEligibleFields[]}`. It is gated exactly like the dry run (WhoAmI, then a fresh `prvCreatesprk_PolicyVersion` check as the caller), and a `policyId` is read as the caller (the uniform 404) | `OntologyAdminAuthoringEndpoints.ValidateAsync`, `OntologyAdminAuthoringService.ValidateAsync` |
| The authorable-column allow-list (owner option 101(b)) | `PolicyVersionValidator.AuthorableColumns`, `ValidateForAuthoring`; `CompiledPredicate.ReadColumns` |
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

## Known limits (wizard concerns, not defects in this contract)

- **`assign-work` appears in both lists.** It is in the Decide lane's `actions` and in `nextSteps`. The plan resolver refuses a code that appears in both a plan's actions and its next steps. The wizard must not offer one code in both places.
- **Validate on a retired policy still validates the body.** Publish then refuses the retired policy for its own reason, as it does today.

## Evidence

- **Live, read-only (spaarkedev1, 2026-10-16/17):**
  - All 51 allow-listed columns exist, and every one has `IsSecured == false`.
  - All 5 live Ontology policies' current versions pass `ValidateForAuthoring` (POL-COMMIT-BUDGET v2, POL-TASK-OVERDUE, POL-TASK-DUESOON, POL-WA-RESPONSE, POL-TODO-OVERDUE).
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
