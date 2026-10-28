# Task 166 notes - POL-MATTER-NOBUDGET v3 (D-192)

Rigor STANDARD (config JSON plus the tests that pin it; tests modified, so code-review and adr-check are owed at review).

## What changed
- `config/know-promotion.policies.json`: v3 (`...6313`, `inForceFrom` 2026-10-28T00:00:00Z) with plan actions `["revise-budget","assign-task"]`, nextSteps `["add-todo","assign-work"]`; `currentVersion` 3. Rule body, headline and message template are copied from v2 unchanged. v1 and v2 entries are unchanged (parsed JSON equal; the only text change is the comma after v2's closing brace). The file's top-level `description` gained one sentence about v3.
- `proposedAction` (free text, <= 400 chars): "revise-budget: set up this matter's budget, or assign someone to set it up." Names only a plan code.

## Tests edited and why
- `KnowPromotionPolicyDefinitionTests.cs`: current-version plan test now asserts v3's plan and `FirstConflict` is null; new D-180 guard (current plan never names create-budget, proposedAction names only plan codes); versions test pins v1, v2, v3 verbatim; all-version plans resolve.
- `DecisionActionCatalogTests.cs`: kept the v2 assertion (v2 plan never substitutes); added the v3 plan substitution unit test and three route tests (0 budgets: Create budget first then Assign Task; 1 and 3 budgets: Revise budget first, must-not-fire). They use the literal v3 plan string, which the definition test pins to the config.
- `tests/scripts/Apply-DoLanePolicies.Tests.ps1`: v2 is now the superseded version (ends at v3 inForceFrom, Stamp when open, None when ended, Drift otherwise); v3 is current (no end); lint rejects v3 not after v2; lint accepts the shipped v3 plan and refuses an authored create-budget.
- No change needed: `AuthorableColumnsTests.cs`, `DecisionActionCatalogScriptListsTests.cs` and `KnowPromotionPolicySeamTests.cs` read the current version or v1's rule body generically (live seam test, opt-in; rule body unchanged).

## Not done here (main session, after merge)
`Apply-DoLanePolicies.ps1` against Dataverse: `-WhatIf` first, then real (commands in the PR body). Not run by this task.
