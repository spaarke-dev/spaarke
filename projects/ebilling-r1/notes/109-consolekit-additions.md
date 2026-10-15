# Task 109 - ConsoleKit additions

Library: `src/client/shared/Spaarke.UI.Components/src/components/ConsoleKit/`. Status: completed, then reworked after the ontology-r1 review (2026-10-15). **Only `statusOverride` remains.**

## Review outcome (ontology-r1, CLAUDE.md section 11)

ConsoleKit is the ontology state-vocabulary kit (delivered by ontology-r1 task 057). The first delivery of this task also added `OutcomeCards`, `CountFilters` and `Disclosure`. The reviewer's direction: those overlap components that already exist, so they were removed (components, barrel exports and tests) and task 110 reuses the existing pieces instead.

| Removed | Reuse in task 110 (exact imports are in 110's constraints) |
|---|---|
| `CountFilters` | WorkspaceShell `MetricCardRow` + `MetricCard`, cards built as `MetricCardConfig[]` (`selected` gives aria-pressed, `disableWhenEmpty` disables a card at count 0), as `Worklist/laneCountFilters.ts` does. D-24: never a new card component for count filters. |
| `Disclosure` | Fluent v9 `Accordion` / `AccordionItem` / `AccordionHeader` / `AccordionPanel` directly (as `ConsoleKit/RecordRow.tsx` and `DecisionWizard/steps/FoundStep.tsx` do; `Worklist/WorklistDisclosures.tsx` exists on branch `task/ontology-059-worklist-widget` only). This reverses the earlier decision to add `Disclosure` for `aria-controls`; accepted, since Accordion's `aria-expanded` is enough and one disclosure pattern across the library is worth more. |
| `OutcomeCards` | A plain Fluent `RadioGroup` + `Radio` for the choice. `SprkChat/OutcomeCard` was checked and does not fit the choice: it is a read-only completion card driven by a server `IOutcomeCard` payload, not a selector. It can show the result of a confirmed decision if the server returns such a payload. |

## What remains

| Piece | Where | Notes |
|---|---|---|
| `statusOverride?: ConsoleStatusOverride` (`label`, `tone`, optional `description`) | `StatusBar.tsx`, `RecordRow.tsx`; type in `consoleStatus.ts`; exported from `ConsoleKit/index.ts` | Display override only. `ConsoleState` and `CONSOLE_STATE_BADGES` are untouched (still nine entries). On `StatusBar` the bar intent follows the override tone (the `Dismissed` intent exception does not apply to an override). `state` stays a required prop and is only echoed in `data-state` when an override is given. Without an override the output is unchanged (tested). |

## Verification (after the rework)

- `npx jest src/components/ConsoleKit`: 2 suites, 44 tests pass (35 existing in `ConsoleKit.test.tsx`, unchanged; 9 in `ConsoleKitStatusOverride.test.tsx`, renamed from `ConsoleKitAdditions.test.tsx`, which keeps the override tests, the "absent override is unchanged" tests and a dark-theme render of the override).
- `npx tsc --noEmit` in the library: clean (after building sibling `Spaarke.Auth` and `Spaarke.SdapClient` dist).
- `npx eslint src/components/ConsoleKit`: clean.
- `node scripts/quality/client-lint/client-lint.mjs check --package ui-components`: ok; no suppression added.
- The original fail-before proof for the override (4 override tests fail with the original `StatusBar.tsx` / `RecordRow.tsx`) is unchanged by the rework.

Browser ui-tests are not run here (no Chrome integration); task 110 runs the visual checks for `statusOverride` and the reused components on its page.

## ontology-r1 notification

The change to ConsoleKit is now one additive optional prop plus one type export; no existing signature or behaviour changes. The escalation trigger (an objection to the override prop) has not fired. Alternative if they object: compose an eBilling status bar from `StatusBadge` and `MessageBar` in the page.

## Deviations

None from the POML beyond the review rework above. Task 109 step 9 (TASK-INDEX.md) was not done: the parallel-mode overrides reserve that file for the orchestrator.
