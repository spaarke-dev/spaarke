# Task 161 notes - Create budget when the matter has none (D-180)

Lane: implementation, branch `task/161-create-budget`. Stopped before POML step 3 (merge, dev BFF deploy) and step 4 (dev verification); both belong to the main session.

Rigor FULL, model tier sonnet, effort high, steps mode directional.

## What changed

| Area | File | Change |
|---|---|---|
| Catalog | `Services/Signals/Actions/DecisionActionCatalog.cs` | `create-budget` next to `revise-budget`: label "Create budget", Decide lane, WorkType fund, Judgement, Tier2b gate, `ResolvesSignal` default true, params `amount` (Money) and `reason` (TextArea), excludes `approve-variance` (made mutual by `Mutualize`). New `SubstitutionOnly` init property on the definition. |
| Plan reduction | `Services/Signals/Actions/DecisionPlanService.cs` | `ResolvedDecisionPlan.WithCreateBudgetInsteadOfRevise()` (same pattern as `WithoutMarkComplete`); `Resolve` refuses a stored plan that names a `SubstitutionOnly` action (`action_not_allowed`), which the authoring validator reuses. |
| The one shared function | `Services/Signals/SignalCoreRecordAccess.cs` | `WithCreateBudgetWhenNoneAsync(plan, decision, ct)` (plan route) over `CanSubstituteCreateBudget` and `CountMatterBudgetsAsync` (commit route): one app-only read of the matter's active budgets (fix round 1, F1-a; originally a caller-identity read); only when the plan has revise-budget and the core table is `sprk_matter`; unreadable means `Unknown`: no substitution and no failure. `SignalAccessDecision` gained `CoreTable` (set from the catalog row `AuthorizeAsync` already resolved) so neither route resolves the table again. |
| Plan route | `Api/Signals/DecisionPlanEndpoints.cs` | Calls the function after the Mark-complete reduction. |
| Commit route | `Services/Signals/DecisionCommitService.cs` | `PrepareAsync` recomputes the substitution through the same function, before the existing "every offered action exactly once" check, so a mismatch is the existing `ActionsDoNotMatchPlan` refusal. |
| Executor | `Services/Signals/Actions/DecisionActionExecutors.cs`, `Infrastructure/DI/SignalsModule.cs` | `CreateBudgetExecutor : ChildCreateExecutorBase` (table `sprk_budget`, `prvCreatesprk_Budget`), registered beside `ReviseBudgetExecutor`. Writes `sprk_name`, `sprk_totalbudget`, `sprk_matter@odata.bind` through `DecisionRouteCores.CreateChildAsync` (the secure ribbon's "New Budget" path), as the caller. |
| Base class hook | same file | `ChildCreateExecutorBase.FileUnderAsync` (virtual) holds the old inline `sprk_regarding{type}` filing; a budget has no regarding lookups (typed `sprk_matter` only), so `CreateBudgetExecutor` overrides it. Existing executors keep byte-identical payloads (their tests pass unchanged). |
| Authoring | `Services/Ontology/OntologyAdminAuthoringService.cs` | `LaneCatalog` drops substitution-only actions and does not name them as exclusions. |

Escalation trigger checked: the child-record route core does create `sprk_budget` for a decision (`CreateTables` has it, `prvCreatesprk_Budget` follows the live `prvReadsprk_Budget` naming, the ownership resolver and `RecordCreatorPerson` already know the table). No second create path was added.

## Decisions

1. **Resume and finish keep the plan they were taken under.** The commit route recomputes the substitution live (D-17), but a review whose Create budget step already landed now sees one budget (its own). Without a rule a resume of a failed commit, or a finish from the stored record, would be refused with the actions-mismatch reason for the budget it created itself. `PrepareAsync` therefore substitutes without a read when (a) it is finishing a stored record (`linkedRecord`) or the ledger shows `action:create-budget` Done, and (b) the review names create-budget and not revise-budget. Nothing is written on either path for that step. Both cases have a test, and each was shown to fail with the shortcut disabled.
2. **Matter cores only.** The substitution and the executor apply to a `sprk_matter` core, as revise-budget does (`Prepared.MatterId`). A project core keeps revise-budget; the executor refuses a non-matter core with `budget_not_on_matter` before anything is written (`sprk_budget` also has a `sprk_project` lookup, but no project path asked for it).
3. **Reason is not stored on the budget.** `sprk_budget` has no column for it (verified in the resolver and data-model doc); it is recorded as the step's value on the Decision Record, as the POML says ("recorded like Revise budget").
4. **Budget name** is `Budget yyyy-MM-dd` (UTC, from the injected `TimeProvider`).
5. **No client change.** The wizard renders from the server's plan; `create-budget` has plain Money and TextArea parameters and no lookup, so the D-18 prefill in `ParameterFields.tsx` is untouched and no client code looks at budget counts. No client test was added (no client code changed; a jest run would need an install for no new coverage).
6. **`approve-variance.excludes` on the wire now reads `["revise-budget","create-budget"]`** (mutual exclusion is derived). Harmless to the client (it removes steps by code, and the absent one is ignored); three existing assertions were updated to say so. The authoring catalog keeps showing only `["revise-budget"]`.

## Known limits (K class, one line each)

- K2 (closed in fix round 1, F1-a): the count was the caller's own read; it is now the BFF's app-only read.
- K2 (closed in fix round 1, K2a): two different reviews committed at the same moment on a matter with no budget could each create one. A `decision-budget:{tenant}:{matterId}` lease now serializes them.
- K4: no re-evaluation trigger is queued after create-budget (revise-budget queues one for its revision); the Signal closes Acted at commit and the no-budget condition clears on the next evaluation.

## Tests

Failed before: the new tests do not compile against the old source (`SubstitutionOnly`, `WithCreateBudgetInsteadOfRevise`, `CreateBudgetExecutor` are missing); verified by reverse-applying the source diff.

| Command | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 warnings, 0 errors |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~Signals\|FullyQualifiedName~Ontology\|FullyQualifiedName~Decision"` | 2767 total: 2766 passed, 1 failed on the first run (the authoring catalog mirror test, updated to drop substitution-only actions); after the update the Ontology authoring class passes 166/166 (includes the new refusal test) |
| `dotnet test tests/Spaarke.ArchTests` (full) | 1093 passed, 0 failed |
| `dotnet format whitespace --verify-no-changes` on the changed BFF files | clean |

New tests: catalog (create-budget twin, authoring catalog, in-place replacement, three refused-plan cases), plan route (0 / 1 / 3 budgets, four unreadable-count shapes, no read for a plan without revise-budget or a project core), commit route (0 budgets, mismatch both ways, unchanged with 1 / 3, unreadable, resume, finish), executor (payload, bad input, non-matter core, preflight refusals, core refusals, 5xx), authoring validate and save refusal.

## BFF hygiene (CLAUDE.md section 10, bff-extensions.md)

- Placement: the BFF, in the existing Signals module. The decision plan and commit routes live here and the shared create core is in-process; no new endpoint, service type, background work, package or Dataverse column. ADRs: ADR-002 (create as the caller through the one server owner, client only previews), ADR-010 (a concrete executor registered in `SignalsModule`, no new interface), ADR-001/008 (no new route). No AI code touched, so no PublicContracts concern.
- Component justification (section 11): existing `revise-budget`, `WithoutMarkComplete`, `ChildCreateExecutorBase`; extended all three. Cost of doing nothing: on a matter with no budget the step cannot be completed.
- Publish size, Release, framework-dependent linux-x64, `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`, `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included (4 present): fresh worktree at `C:\wts-161-base` (origin/docs/ontology-platform-design `0ac5195905`, docs-only difference from this branch's base `a5ed5042e1`) 192 files, 37.342 MB; fresh worktree at `C:\wts-161-head` (this branch) 192 files, 37.346 MB; delta +0.004 MB. Both worktrees removed with `Remove-WorktreeSafe.ps1`.
- CVE: no `.csproj` or `.props` change; `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages.

## Gates

adr-check: compliant with ADR-001, 002, 008, 010, 013 (not touched), 038 (tests under the KEEP paths); no violation. code-review: no F-class finding; the K-class items above. Not done (main session): POML step 3 (merge, deploy dev BFF) and step 4 (dev check on a zz- matter, `notes/deploy-log.md`).

## Fix round 1 (PR #2290, independent Opus review)

| Finding | Fix | Files |
|---|---|---|
| F1-a "has no budget" read as the caller | `SignalCoreRecordAccess.CountMatterBudgetsAsync` reads `sprk_budget` (`sprk_matter` = core, `statecode` 0, `$top 1`) through the BFF's `IGenericEntityService` (app-only), the read the POL-MATTER-NOBUDGET evaluator makes through `PredicateCompiler.VerifiedJoins (sprk_matter, sprk_budget)`. Runs only for an `Allowed` decision whose core table is `sprk_matter` (both routes establish the caller's read of the Signal and matter first); any other decision is `Unknown` with no request. Comment explains why the read is the BFF's. | `SignalCoreRecordAccess.cs` |
| F1-b count fault shows the wrong message | `MatterBudgetCount { None, Some, Unknown }`. Plan route substitutes only on `None`. Commit: `None` substitutes; `Some` or `Unknown` with a review naming create-budget (taken or skipped) is refused: `Some` = `ActionsDoNotMatchPlan` "The matter's budgets changed while you were deciding; reopen the item."; `Unknown` = `Unavailable` (503, reason `signal_unreadable`, the shape of the other transient read faults in `PrepareAsync`) "Whether the matter has a budget could not be checked; nothing was done. Try again." | `SignalCoreRecordAccess.cs`, `DecisionCommitService.cs` |
| K2a duplicate budgets from two commits | `BudgetLeaseId(tenant, matterId)` = `decision-budget:{tenant}:{matterId}` is added to `lockIds` (after the review and Signal leases, same order for everyone) only when the review TAKES create-budget on a matter core. The second commit then recounts and refuses. | `DecisionCommitService.cs` |
| K2b resume stuck after Create budget skipped | `PrepareAsync` also keeps the original plan, without a read, when `resume is not null` and create-budget was not taken. | `DecisionCommitService.cs` |
| K3 untested `FileUnderAsync` fault branch | Test for the entity-set fault in `CreateBudgetExecutor`. | `DecisionActionExecutorsTests.cs` |

Test-rig note: `TheRecordLookup_IsByThisCallerAndThisReviewIdInSprkSteps` captured the LAST `RetrieveMultiple`; the budget count is now an app-only `RetrieveMultiple`, so it filters on `sprk_decisionrecord` and answers the budget query with one row (its assertions are unchanged).

### Before / after proof

Before = the new and changed tests run against the unfixed source (`dotnet test --filter "F1a|F1b|K2a|K2b|K3|D180|D17"`): 33 selected, 13 failed. After the fix: 34 selected (adds the count-state test), 34 passed.

| Test | Before | After |
|---|---|---|
| `TheCallerSeesNoBudget_ButTheBffSeesOne_NothingIsSubstituted_AndCreateBudgetIsRefused_F1a` (commit) | Recorded (a second active budget written) | Refused, specific detail, nothing written |
| `TheBffSeesNoBudget_ButTheCallerSeesOne_StillSubstitutes_MustNotFire_F1a` | Refused (caller read decided) | Recorded (substitution as today) |
| `TheAppOnlyBudgetRead_IsTheActiveBudgetsOfTheCoreMatter_AndNeverRunsForACallerWhoCannotReadTheMatter_F1a` | no app read at all | one app read with the right criteria; 0 app reads when the caller cannot read the matter (404) |
| `ThePlanRoute_StillOffersReviseBudget_WhenOnlyTheBffCanSeeTheBudget_..._F1a`, `..._OffersCreateBudget_..._D180`, `..._StillOffersReviseBudget_..._MustNotFire_D180` (1, 3), `..._DoesNotFail_WhenTheBudgetsCannotBeCounted_D180` (plan route) | Create budget offered on the caller's 0 / no app read | app read decides; caller read never made |
| `ThePlanRoute_NeverReadsBudgetsApp_Only_ForACallerWhoCannotReadTheMatter_F1a`, `TheBudgetCount_IsNone_Some_OrUnknown_...F1b` | passes / new API | passes (the count-state test needs the new enum, so it has no "before") |
| `TheBudgetsCannotBeCounted_ReviseBudgetStays_AndACreateBudgetReviewIsUnavailable_NothingWritten_F1b` | Refused 422 "must list every action" | Unavailable 503 "Whether the matter has a budget could not be checked..." (taken and skipped create-budget; Revise review still Recorded) |
| `CreateBudget_ReviewedOnZeroBudgets_ButTheMatterNowHasOne_..._D17` | generic mismatch detail | "The matter's budgets changed while you were deciding; reopen the item." |
| `TakingCreateBudget_HoldsTheMatterBudgetLease_...K2a` | Recorded (no lease) | InProgress while the lease is held; Recorded after release; lease released after a commit |
| `TheMatterBudgetLease_IsNotTaken_WhenCreateBudgetIsSkipped_OrNotInThePlan_MustNotFire_K2a` | passes | passes (held lease ignored when create-budget is skipped or absent) |
| `ARetryOfAFailedReview_ThatSkippedCreateBudget_KeepsThePlan_..._K2b` | Refused (actions mismatch) | Recorded |
| `CreateBudget_WhenTheMattersEntitySetCannotBeResolved_IsFailed_..._K3` | passes (branch already correct, untested) | passes; with the branch's `catch` replaced by `throw;` it fails (mutation check), then restored |

### Fix round 1 verification

| Command | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 warnings, 0 errors |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~Signals\|FullyQualifiedName~Decision\|FullyQualifiedName~Ontology\|FullyQualifiedName~ChildRecord"` | 3107 total: 3105 passed, 2 skipped, 0 failed |
| `dotnet test tests/Spaarke.ArchTests` (full) | 1093 passed, 0 failed |
| `dotnet format whitespace --verify-no-changes` on the two changed BFF files | clean |

No package, DI registration, route, or Dataverse change. PR #2289 (task 159) edits the same executor / catalog / module files; not rebased, the main session sequences the merges.

rework: fix round 1 task 161
