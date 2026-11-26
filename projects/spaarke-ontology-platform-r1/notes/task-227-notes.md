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
