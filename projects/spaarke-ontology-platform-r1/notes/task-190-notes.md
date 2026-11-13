# Task 190 — Send budget inquiry sends Cc and Bcc end to end (D-222)

PR [#2784](https://github.com/spaarke-dev/spaarke/pull/2784), squash-merged to master as `b77967a750` (owner: merge on green). Branch `task/ontology-190-inquiry-cc-bcc`, worktree `C:/wt190`. Sonnet implementer; Opus adversarial review once.

## What changed
- `DecisionActionCatalog.cs`: optional `cc`/`bcc` on `send-budget-inquiry` only (`send-reminder` keeps the shared `MessageParameters` and its warning).
- `DecisionActionExecutors.cs` `Map`: Cc/Bcc validated like To before any write (`RecipientUnresolved` names the field); cross-line de-dup, case-insensitive.
- `BudgetInquiryExecutor.cs`: `BudgetInquiryRequest` Cc/Bcc; `Build` passes them to `CommunicationService` (already sends and records them); empty is null.
- `InquiryRecordFacts.ResolveAsync`: new overload with `labelAudience`. The record label (matter name in footer and `EntityName`) is decided over To ∪ Cc ∪ Bcc (uac-r2 F1 rule: every recipient confirmed internal); link lines stay on To so a Bcc never adds an "External recipients" line. Overload, not optional parameter: Moq expression trees in `AssignTaskNotifierTests`.
- Client: `EmailMessageFields`, `ParameterFields`, `decisionDraft` (`stepValid` checks Cc/Bcc), `EmailComposer` `initialBcc` (keeps Bcc across remount). Cc/Bcc committed only when the plan declares them.

## Review
- Fix round 1 (F1): the label was decided on To only, so the matter name could reach an external Cc/Bcc recipient; the first test asserted the leak. Fixed in `ec93ae0f8a`; seeding proof: audience reverted to To-only fails 3 of 7 `PrepareAsync` tests.
- Known limits: K2 Bcc stored in Decision Record step values (internal readers only, same as `sprk_communication.sprk_bcc`); K1 address regex accepts bracketed `<a@b.com>` (pre-existing for To); K4 de-dup in both Map and Build.

## Tests
BFF scoped 627 passed (`BudgetInquiry*`, `InquiryRecordFacts*`, `DecisionActionExecutors*`, `AssignTaskNotifier*`, `DecisionCommitServiceTests`); wider Signals/Decision/Communication filter 8121; ArchTests 2538. Jest DecisionWizard + EmailComposer 498.

rework: fix-round-1 task 190
