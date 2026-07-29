# Task 030 — EmailCardList

**Status**: Complete. Build green (`tsc`), jest green (8/8 new tests; 18/18 package-wide).

## What was built

- `src/client/shared/Spaarke.Communication.Components/src/components/EmailCardList/EmailCardList.types.ts` — `EMAIL_COMMUNICATION_TYPE` (100000000), `EmailCardItem`, `EmailCardListProps`.
- `.../EmailCardList/EmailCardList.tsx` — presentational card list: filters `items` to Email-type in-component (defense-in-depth per FR-03, host also pre-filters at query time), renders skeleton cards while `isLoading`, "No emails in this view" empty state, unread (bold + dot) and selected (brand background) visuals, Enter/Space keyboard activation.
- `.../EmailCardList/index.ts` — barrel for the component folder.
- `.../EmailCardList/__tests__/EmailCardList.test.tsx` — 8 tests covering: Email-only rendering + non-Email exclusion negative case, from/subject/preview/date rendering, loading state, empty state (zero items + all-non-Email items), selection + unread visuals, keyboard activation, dark-mode theming with a `console.error` spy.
- `src/client/shared/Spaarke.Communication.Components/src/components/index.ts` — added `export * from './EmailCardList';` (one-line barrel addition, coordinated with sibling task 031 per the parallel-group guardrail).

## Deviations / notes for reviewers

1. **Date-locale fix during Step 9.5 code-review**: `formatCardDate` initially called `Intl.DateTimeFormat(undefined, …)` (host-locale-dependent). Changed to explicit `'en-US'` to avoid CI/locale-dependent test flakiness and to match the codebase's existing date-formatting convention. Re-verified green after the fix.
2. **No `package.json` subpath export added** for `EmailCardList` (unlike `AttachmentList`'s dedicated `"./components/AttachmentList"` entry). This task's barrel guardrail scoped edits to `src/components/index.ts` + the new component folder only — `package.json` was intentionally left untouched to avoid touching a file outside the assigned scope while a sibling task (031) works in the same package concurrently. `EmailCardList` remains reachable via the top-level barrel and `@spaarke/communication-components/components`. Flag for task 032/040 (assembly) if a dedicated subpath is later needed.
3. No ADR violations found (ADR-012, ADR-021, ADR-022/NFR-05 all compliant per Step 9.5 `adr-check`).

## Quality gates (Step 9.5)

- `code-review`: Clean — 0 Critical, 0 Warning (1 warning found + fixed inline: locale-dependent date formatting), 2 Suggestions (package.json subpath deferral — see above; optional row-level `aria-label` consolidation).
- `adr-check`: Clean — 0 Violations, 0 Warnings across ADR-012/021/022.
