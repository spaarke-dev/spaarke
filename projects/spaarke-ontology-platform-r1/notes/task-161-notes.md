# Task 161 notes - Create budget when the matter has none (D-180)

Lane: implementation, branch `task/161-create-budget`. Stopped before POML step 3 (merge, dev BFF deploy) and step 4 (dev verification); both belong to the main session.

Rigor FULL, model tier sonnet, effort high, steps mode directional.

## What changed

| Area | File | Change |
|---|---|---|
| Catalog | `Services/Signals/Actions/DecisionActionCatalog.cs` | `create-budget` next to `revise-budget`: label "Create budget", Decide lane, WorkType fund, Judgement, Tier2b gate, `ResolvesSignal` default true, params `amount` (Money) and `reason` (TextArea), excludes `approve-variance` (made mutual by `Mutualize`). New `SubstitutionOnly` init property on the definition. |
| Plan reduction | `Services/Signals/Actions/DecisionPlanService.cs` | `ResolvedDecisionPlan.WithCreateBudgetInsteadOfRevise()` (same pattern as `WithoutMarkComplete`); `Resolve` refuses a stored plan that names a `SubstitutionOnly` action (`action_not_allowed`), which the authoring validator reuses. |
| The one shared function | `Services/Signals/SignalCoreRecordAccess.cs` | `WithCreateBudgetWhenNoneAsync(plan, decision, ct)`: one caller-identity read, `sprk_budgets?$filter=_sprk_matter_value eq {core} and statecode eq 0&$top=1`; only when the plan has revise-budget and the core table is `sprk_matter`; unreadable (non-success, non-list body, throw) means no substitution and no failure. `SignalAccessDecision` gained `CoreTable` (set from the catalog row `AuthorizeAsync` already resolved) so neither route resolves the table again. |
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

- K2: a caller who can read no budget of a secure matter reads zero and is offered Create budget, though they could not revise one either; the create core's own checks still decide whether they may create.
- K2: two different reviews committed at the same moment on a matter with no budget can each create one (the review ledger guards a single review id only). Same exposure as the ribbon's New Budget.
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

rework: none task 161
