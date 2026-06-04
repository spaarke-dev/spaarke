# Task 052 — Deviation: closed as no-op

> **Created**: 2026-06-04
> **Task**: 052 — Migrate SpeAdminApp/DashboardPage.tsx from UDG PCF to new DataGrid
> **Status**: ✅¹ Closed with partial scope (documentation-only; zero code migration required)

---

## Original POML premise

> "Migrate `src/solutions/SpeAdminApp/src/components/dashboard/DashboardPage.tsx` from binding to `UniversalDatasetGrid` PCF to direct consumption of new `<DataGrid configId={...}/>`. Visual diff: zero regression vs. UDG render."

This premise was wrong. The audit (task 050) established that DashboardPage was never on UDG.

## What the audit actually found

`DashboardPage.tsx` imports from `@fluentui/react-components`, `@fluentui/react-icons`, and local `services/`/`contexts/`/`types/` modules only. Lines 1-23 contain no `@spaarke/ui-components` import. There is no `<UniversalDatasetGrid>` JSX element anywhere in the file. There is no PCF binding in any SpeAdminApp form definition referencing `sprk_Spaarke.UI.Components.UniversalDatasetGrid`.

The original POML's premise traced back to a single comment at line 405-406:

```tsx
/**
 * ADR-012: Simple table used for the activity grid (see RecentActivityGrid
 * comment for rationale vs. UniversalDatasetGrid).
 */
```

Read in context, this comment is documenting a **design decision not to use UDG** — explicitly choosing a simple Fluent v9 table for the activity grid instead. It is the opposite of "DashboardPage is on UDG." Whoever drafted task 052 read this comment as if DashboardPage *was* on UDG, and the rest of the task POML was generated from that misread.

The comment is also stale in a second way: it points at `RecentActivityGrid` for the underlying rationale, but `RecentActivityGrid` no longer exists in the codebase (the only remaining grep hit is this same comment's own pointer).

## Action taken

1. **Closed task 052 as no-op** with this deviation doc, status set to `✅¹` (completed with partial scope).
2. **Removed the misleading lines 405-406** from `DashboardPage.tsx`'s file-level docblock. The rationale comment was doubly stale (references a defunct sibling component AND a UDG PCF being retired in task 053), so leaving it would have generated future confusion. The cleaner replacement is no comment at all — the import list and the JSX make the design choice obvious.

The Dark-mode + ADR-021 comment (the line just above the removed two lines) is preserved.

## Why this is the right call (not a scope creep)

- The audit's acceptance criterion ("every consumer is listed with a migration target") was already satisfied for DashboardPage: target = N/A (not a consumer).
- Migrating a surface that uses Fluent v9 `<DataGrid>` directly to a Spaarke framework `<DataGrid configId={...}/>` would be a different project — it's the Spaarke shared-grid adoption decision, not a UDG-retirement migration. SpeAdminApp's data layer doesn't go through Xrm or BFF the way the framework's `IDataverseClient` expects; it uses `speApiClient` for the SPE admin endpoints. Forcing it onto the framework just for tidiness would be a regression in fit.
- Task 053 retires the UDG PCF safely. There is no remaining DashboardPage dependency to worry about.

## Downstream effect

| Task | Original plan | Revised | Action |
|---|---|---|---|
| **053** — Retire UDG PCF | Wait for 052 to migrate the consumer | Proceed immediately; no consumer remains | Proceed. |
| **054** — Phase F deploy + UAT (DashboardPage visual diff) | Pre/post screenshots required | No render change — DashboardPage was not modified beyond a docblock comment removal | Re-scope: drop the DashboardPage visual diff; verify SpeAdminApp still builds + renders unchanged. The full UAT scope shrinks. |

---

*Foundation: [050-datasetgrid-consumer-audit.md](./050-datasetgrid-consumer-audit.md).*
