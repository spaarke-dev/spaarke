# Task 167 notes - Create budget: number, currency, optional fields (D-193)

Lane: `task/167-create-budget-fields` (worktree `C:\wts-167`). Rigor FULL, model sonnet @ high.

## What changed

- **Autonumber.** `sprk_budgetnumber` is `BUD-{SEQNUM:6}` in `SpaarkeMaster/Entities/sprk_Budget/Entity.xml`. The `sprk_budget`
  `RecordTableRule` now declares `AutonumberColumn` and `MappingProtectedColumns` (same as the matter). No code sets the number.
  The live step is the main session's (see the PR body).
- **Option and default sources.** `DecisionActionParameter` gains `DefaultSource` and `NotBefore`. New `DecisionParameterSources`
  (scoped) resolves `OptionsSource` (`metadata:table.column` picklists, `currency:active`) and the matter's default currency.
  `OptionsSource` was declared and passed to the client before this task but nothing resolved it.
- **Plan route** (`GET /decision-plan`) returns resolved `options`, `defaultValue` and `notBefore` per parameter. Never fails the read:
  an unreadable list is empty.
- **Commit.** `CreateBudgetExecutor` writes category, period, year (<= 4 chars), start/end date, `transactioncurrencyid@odata.bind` and
  `sprk_currency` (ISO). A new `ExtendColumnsAsync` hook in `ChildCreateExecutorBase` runs in the preflight (throwaway payload, zero
  writes on refusal) and again before the write.
- **Client.** `dateOrderMessage` (decisionDraft.ts) feeds `stepValid` and the End-date message; `templatedDrafts.defaultsFor` applies
  `defaultValue` to Choice parameters; `ParameterFields` renders the message.

## Placement decision (CLAUDE.md section 10 / 11)

1. **Existing**: the decision-action catalog, `ChildCreateExecutorBase`, `ITenantCache`, `IGenericEntityService`, `IDataverseUserClient`.
2. **Extension**: all three extended; one new class only, `DecisionParameterSources`, because the plan route and the commit must read the
   same lists and the same default (two copies would let the wizard offer what the commit refuses). Registered in `SignalsModule`.
3. **Cost of doing nothing**: the budget gets no currency, or a hard-coded list that drifts from the live option sets.

No new package, endpoint or background work.

## Deviations from the POML (and why)

- **Currency is a Choice, not a Lookup.** The wizard has no `transactioncurrency` lookup parameter kind. The Choice carries the active
  currencies and its value is the currency id; the executor binds it. Flag if the owner wants a lookup control.
- **Refusals answer 422, not 400.** The commit route maps `Refused` to 422; the tests assert that.
- **Year MaxLength = 4** is a constant (`CreateBudgetExecutor.BudgetYearMaxLength`), read from live metadata on 2026-10-28.
- **App-only reads** of matter, business unit, organization and currency (organization configuration, like `CountMatterBudgetsAsync`);
  the picklist metadata is read as the caller. The caller-side bind (`AppendTo` on `transactioncurrency`) is checked by the child-records
  create core; the root-BU Core/Basic User roles already hold Read/Append/AppendTo, so no privilege is added.
- **Default currency fallback.** Matter owning BU -> BU `transactioncurrencyid`; if the BU has none, the organization's base currency
  (logged at Warning); if neither, the budget is refused (never blank). The plan drops a default that is not among the active options.
- **Cache.** Option lists use `ITenantCache` (5 min, ADR-009: no `IMemoryCache`); with no tenant claim nothing is cached.

## Known limits / for the main session

- **No alternate key on `sprk_budget`** (the matter has one). The autonumber retry path in `RecordCreationService` therefore stays dormant
  for budgets; a duplicate-number collision would surface as a normal create failure. Not fixed (critical path only).
- Category labels: the owner relabels later (D-193); they come from the live option set, so no code change is needed.

## Out-of-scope findings

None found.
