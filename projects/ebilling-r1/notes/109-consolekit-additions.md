# Task 109 - ConsoleKit additions

Library: `src/client/shared/Spaarke.UI.Components/src/components/ConsoleKit/`. All changes are additive; the existing `ConsoleKit.test.tsx` is unchanged and green.

## What was added

| Piece | Where | Notes |
|---|---|---|
| `statusOverride?: ConsoleStatusOverride` (`label`, `tone`, optional `description`) | `StatusBar.tsx`, `RecordRow.tsx`; type in `consoleStatus.ts` | Display override only. `ConsoleState` and `CONSOLE_STATE_BADGES` are untouched (still nine entries). On `StatusBar` the bar intent follows the override tone (the `Dismissed` intent exception does not apply to an override). `state` stays a required prop and is only echoed in `data-state` when an override is given. Without an override the output is byte-for-byte unchanged (tested). |
| `OutcomeCards<V>` | `OutcomeCards.tsx` | Fluent `RadioGroup` + `Radio`; `ariaLabel` required; body only under the chosen card; disabled option skipped; only reports `onChange` (the host confirms). |
| `CountFilters<K>` | `CountFilters.tsx` | Group of Fluent `ToggleButton` (aria-pressed), names "label: count[, sub]"; one selected; counts are the caller's. |
| `Disclosure` | `Disclosure.tsx` | See decision below. |

Props are named for the library (`OutcomeCardOption`, `CountFilterItem`) rather than the prototype's `Outcome` / `CountFilter`, to avoid vague exports from the shared barrel. No domain wording in any component.

## Decision: Disclosure vs Fluent Accordion (CLAUDE.md section 11)

Added `Disclosure`. Accordion was tried against the two stated needs and does not fit:
- The task requires `aria-expanded` **and** `aria-controls`. Fluent 9.68 `AccordionHeader` sets `aria-expanded` only (`useAccordionHeader`), no `aria-controls`.
- Accordion's header is a full-width heading row with its own padding and chevron placement; it cannot give the inline transparent small button used for "Why this was flagged" (prototype `kit.tsx` Disclosure). `RecordRow` keeps using Accordion for its full-entry block; nothing there changed.

The id for `aria-controls` comes from a module counter held in a ref (no `React.useId`; the library is React-16-safe).

## Verification

- `npx jest src/components/ConsoleKit`: 2 suites, 63 tests pass (35 existing + 28 new).
- Fail-before proof: with the original `StatusBar.tsx` / `RecordRow.tsx` restored, the 4 override tests fail (the 4 "absent override" tests pass, as intended); the other new components have no prior implementation.
- `npx tsc --noEmit` in the library: clean (after building sibling `Spaarke.Auth` and `Spaarke.SdapClient` dist).
- `npx eslint src/components/ConsoleKit`: clean.
- `node scripts/quality/client-lint/client-lint.mjs check --package ui-components`: ok; no suppression added.
- grep: no invoice / billing wording and no `useId` / `useSyncExternalStore` / `createRoot` in the new sources (also asserted in a test). No new npm package.

Not run: the three `<ui-tests>` in a browser (this session has no Chrome integration). The jest suite covers selection by mouse and keyboard, one-selected counts, the override label and tone, and render under the Fluent dark theme. The light, dark and high-contrast visual check is left to task 110's page (the kit uses Fluent tokens only; the existing ConsoleKit colour-literal scan covers the new files).

## ontology-r1 notification

ConsoleKit was delivered by ontology-r1 task 057. The change is additive (new optional prop, three new exports, one new type export); no existing signature or behaviour changes. The escalation trigger (an objection to the override prop) has not fired; the notice is carried in this task's report to the orchestrator, who owns the cross-project channel. Alternative if they object: compose an eBilling status bar from `StatusBadge` and `MessageBar` in the page.

## Deviations

None from the POML. Task 109 step 9 (TASK-INDEX.md) was not done: the parallel-mode overrides reserve that file for the orchestrator.

rework: none task 109
