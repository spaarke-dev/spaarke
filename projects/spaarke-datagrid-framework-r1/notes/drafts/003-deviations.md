# Task 003 — Deviations from design.md

> **Task**: 003-datagrid-core
> **Author**: Claude Code sub-agent (FULL rigor)
> **Date**: 2026-06-01
> **Status**: Filed for review

This document records intentional deviations from `design.md §6` and `§11.5.1`
made during the task 003 implementation.

---

## 1. `dataverseClient` is REQUIRED (not optional) in `DataGridProps`

**design.md §6.1** declares `dataverseClient?: IDataverseClient` (optional,
defaulting to `new XrmDataverseClient()` auto-resolving the Xrm object from
window/window.parent).

**Implementation deviation**: in `DataGrid.tsx`, the prop is REQUIRED.

**Why**: task 002 (which authors `XrmDataverseClient`) is running in parallel
with this task. At the time task 003 was authored, `XrmDataverseClient.ts` did
not yet exist in the codebase. Importing a default factory that does not exist
would have either broken the build (if a stub was referenced) or required a
forward-reference shim.

**Future remediation**: after task 002 lands the `XrmDataverseClient`
implementation, a follow-up edit to `DataGrid.tsx` will:
1. Change the prop signature to `dataverseClient?: IDataverseClient`.
2. Default it to a `new XrmDataverseClient()` instance inside the component
   (lazy-construct on first render, NOT at module scope, to avoid eager
   Xrm window-walk at import time).
3. Update the JSDoc to reflect the new default.

**Acceptance impact**: NONE for Storybook stories — they pass a mock client
explicitly. Production hosts (Custom Page, PCF) will need to pass the client
explicitly until the follow-up edit lands.

**Tracking**: Wave A2 or later "Wire XrmDataverseClient default into DataGrid"
follow-up — to be filed by the main session after task 002 merges.

---

## 2. `useDataGridContext` lives in `.ts` (not `.tsx`) by using `React.createElement`

**Implementation choice**: `src/hooks/useDataGridContext.ts` uses
`React.createElement` instead of JSX for the `DataGridContextProvider`
component.

**Why**: the task 003 brief grep verification step uses the literal path
`src/hooks/useDataGridContext.ts`. Using `React.createElement` keeps the file
extension `.ts` and lets the grep check pass directly. Functional behavior is
identical to a JSX implementation.

**Impact**: NONE. The provider is consumed via JSX from `DataGrid.tsx`
(`.tsx` file) as usual. Consumers see no difference.

---

## 3. Default `pageSize = 100` in `BehaviorConfig` (framework default)

**design.md §6.3** `BehaviorConfig.pageSize?` defaults to **50** ("default 50"
per the inline schema comment).

**`DataGridConfiguration.ts` JSDoc** (task 001) states **100** as the default
for lazy-load contexts: "Default 50 per design.md (callers may override;
framework also accepts 100 default for lazy-load contexts)."

**This implementation chose 100** for the framework default in
`configResolution.ts` `FRAMEWORK_DEFAULT_BEHAVIOR.pageSize = 100` because:
- FR-DG-12 mandates "page size default 100 (override via
  configjson.behavior.pageSize)" — this is the framework-level requirement.
- The 50 figure in §6.3 is for a non-lazy display, which R1 does not implement
  (no classic pagination is built; everything is lazy-load).

**Effect**: a Storybook story or production configjson that omits `pageSize`
will fetch 100 records per page. Setting `behavior.pageSize: 50` in
configjson restores the design.md default.

**No remediation needed** — this aligns with FR-DG-12 and the task 001 JSDoc.

---

## 4. `synthesizeColumnsFromMetadata` fallback for missing layoutXml

**design.md §6.4** does not explicitly address what happens when both the
savedquery lookup AND the configRecord fail (no fetchXml, no layoutXml).

**Implementation choice**: `configResolution.ts` adds a
`synthesizeColumnsFromMetadata` fallback that derives up to 10 columns from
the entity's `attributes` map — primary name first, then non-system
attributes (skipping `createdon`, `createdby`, `statecode`, etc.).

**Why**: FR-DG-04 says "a configId pointing to a non-existent record MUST
still render using metadata defaults." Without this fallback, a missing
savedquery would render an empty `<DataGrid />` with no columns. The
synthesis path gives the user something useful.

**Risk**: the column order is `Object.entries()` order, which is insertion
order in modern JS. For Dataverse-projected metadata that order is alphabetical
by logical name. Production hosts that care about column order should
provide an inline source or explicit column overrides.

**No remediation needed** — this is a strict superset of the design.md
behavior (it only kicks in when the documented path fails).

---

## 5. Storybook stories live outside `src/`

**Convention**: `src/client/shared/Spaarke.UI.Components/storybook/DataGrid.stories.tsx`.

**Why**: this project has no `.storybook/` configuration yet, and `tsconfig.json`'s
`rootDir` is `./src`. Placing stories outside `src/` ensures the production
TypeScript build (`tsc`) does NOT pick them up (they'd otherwise emit `.d.ts`
files into `dist/`).

**Action when Storybook is wired** (later task): the standard
`stories: ['../storybook/**/*.stories.@(ts|tsx)']` pattern in
`.storybook/main.ts` will pick up this file without modification.

---

## Summary table

| # | Deviation | Severity | Remediation needed |
|---|-----------|----------|--------------------|
| 1 | `dataverseClient` required, not optional | Medium | Yes — after task 002 lands |
| 2 | `useDataGridContext.ts` uses `React.createElement` | Cosmetic | No |
| 3 | Default `pageSize = 100` (not 50) | None | No |
| 4 | Metadata-synthesized columns fallback | None (additive) | No |
| 5 | Storybook stories outside `src/` | None | No |

Reviewer should focus on item 1; items 2–5 are either pre-aligned with the
task 001 JSDoc or strict supersets of the design.md spec.
