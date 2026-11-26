# Task 227 notes: catalog hint wording (#1848) and evaluator assignee column (#1881, DI-82)

Base: origin/master c438119bca (the POML cites d43fc9e16d; line numbers re-read at c438119bca).

## Findings (step 1)

- #1848: `DecisionActionCatalog.cs` revise-budget parameter `budget`, `Hint` carried "(D-18)" and "the user picks". It is the
  only catalog string with a decision id or "the user": a regex scan of every user-visible catalog string (labels, parameter labels,
  hints, option labels, effect lines, dismissal reasons; `All`, `BillingActions`, `RecordProcedureActions`) found only this one.
  The text is pinned in `tests/integration/contract/Api/Signals/Baselines/plan.200.path-b.json` (twice: the body string and the
  pretty-printed hint).
- #1881: `SignalEvaluator.AssigneeColumnBySubject["sprk_workassignment"]` was `sprk_assignedto`; `OwnedChildWrite.ForPersonColumns`
  (the registry's ForPerson entries) and D-129 name `sprk_assignedtointernal`. `sprk_todo` and `sprk_event` agree
  (`sprk_assignedto`) in both maps, so only the work assignment changes. No schema change; `sprk_assignedtointernal` is already read
  elsewhere (task 152 / RecordTableRules).

## Tests

- `tests/unit/domain/Signals/DecisionCatalogUserTextTests.cs`: scan over every user-visible catalog string, a must-fire control
  (the scan is non-empty and fires on sample developer wording), a must-not-fire control (clean copy), and the exact new hint.
- `tests/unit/domain/Signals/SignalEvaluatorAssigneeColumnTests.cs`: work assignment column equals ForPersonColumns; pins both maps
  for every shared subject (must-fire: the shared set is exactly todo/event/workassignment); other subjects unchanged.

## Results

- Fail before (new tests against unfixed source): 5 of 11 failed (scan, exact hint, work assignment column, shared-subject pin, other subjects
  expectation). The scan named only `revise-budget.budget.Hint`. After: all pass.
- `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~Signals|FullyQualifiedName~DecisionCatalogUserText"`: 5565 passed, 0 failed,
  6 skipped (live seam tests), including `SignalRoutesContractBaselineTests` against the updated baseline.
- `dotnet test tests/Spaarke.ArchTests` (built explicitly first): 2845 passed, 0 failed.
- Publish size (linux-x64 framework-dependent, Release, `Compress-Archive -CompressionLevel Optimal` over the publish folder, PDBs included, fresh
  worktrees at C:\wt227m / C:\wt227b): master c438119bca 41.653 MB, branch 41.654 MB, delta +0.001 MB; 194 files each.
- CVE: no package added or changed, so no new HIGH CVE.
- Touched in `SignalEvaluator.cs`: the `AssigneeColumnBySubject` entry for `sprk_workassignment` and its summary comment, plus one `using`. Nothing else.
- Component justification (§11): no new service, interface, endpoint, DI registration, column or package.

## Found, not fixed

- `Services/Signals/Actions/DecisionActionExecutors.cs:399` (at c438119bca): a doc comment says "the user picks among that matter's budgets (D-18)".
  A code comment, not user-visible; left alone.

rework: none task 227
