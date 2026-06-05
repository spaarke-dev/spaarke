# Task 050 — DatasetGrid + UniversalDatasetGrid consumer audit

> **Created**: 2026-06-04
> **Task**: 050 — Audit consumers of `@spaarke/ui-components/DatasetGrid/{GridView, CardView, ListView, VirtualizedGridView, VirtualizedListView}` + `UniversalDatasetGrid` PCF.
> **Status**: ✅ Complete. **Headline finding: zero external consumers** of any retiring symbol; task 052's premise is wrong (DashboardPage was never on UDG); tasks 051 + 053 are safe to execute as drop-deletes.

---

## Scope

This audit covers retirement targets for Phase F:

1. **5 React components** in `@spaarke/ui-components/components/DatasetGrid/`: `GridView.tsx`, `CardView.tsx`, `ListView.tsx`, `VirtualizedGridView.tsx`, `VirtualizedListView.tsx` (task 051).
2. **`UniversalDatasetGrid.tsx`** — the composition root that wires the 5 view components together (task 051; deletion is implied because it has no surviving callees).
3. **`UniversalDatasetGrid` PCF** at `src/client/pcf/UniversalDatasetGrid/` (task 053).
4. **`DashboardPage.tsx` in SpeAdminApp** — task 052's premise that it consumes the UDG PCF.

---

## Audit method

```bash
# 1. Any source consumer of the 5 view components OR the composition root
grep -rnE "from\s+['\"]@spaarke/ui-components" src --include='*.ts' --include='*.tsx' \
  | grep -E "GridView|CardView|ListView|VirtualizedGridView|VirtualizedListView|UniversalDatasetGrid"

# 2. Deep-path imports that bypass the barrel
grep -rnE "from\s+['\"][^'\"]*DatasetGrid/(GridView|CardView|ListView|VirtualizedGridView|VirtualizedListView|UniversalDatasetGrid)" \
  src --include='*.ts' --include='*.tsx'

# 3. Dataverse customizations / solution XML / SiteMap references to the UDG PCF
grep -rln "sprk_Spaarke.UI.Components.UniversalDatasetGrid\|SpaarkeUniversalDatasetGrid" infrastructure src/dataverse
```

---

## Results

### 1. Source consumers of the 5 view components or `UniversalDatasetGrid` symbol — **ZERO**

Both grep variants returned no hits. The only matches across `src/` were:

| File | Match type | Verdict |
|---|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/index.ts:5-10` | Barrel `export * from './DatasetGrid/{...}'` | Internal — deleted by task 051 |
| `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/UniversalDatasetGrid.tsx:19-21` | Composition root imports the 3 routed views | Internal — file itself deleted by task 051 |
| `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/GridView.tsx:24` | `import { VirtualizedGridView } from './VirtualizedGridView'` | Internal — both files deleted together |
| `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/ListView.tsx:11` | `import { VirtualizedListView } from './VirtualizedListView'` | Internal — both files deleted together |
| `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/__tests__/GridView.test.tsx` | Test file | Internal — deleted by task 051 |

**Verdict**: The 5 view components + `UniversalDatasetGrid.tsx` form a self-contained subgraph with no external consumers. Task 051 can delete the entire `components/DatasetGrid/` directory + its barrel exports in one move.

### 2. UDG PCF Dataverse references — **ZERO**

`UniversalDatasetGrid` PCF references in `infrastructure/` and `src/dataverse/` outside of the PCF's own solution descriptor:

```
(none)
```

The PCF's own solution descriptors at `src/client/pcf/UniversalDatasetGrid/Solution/` (`solution.xml`, `customizations.xml`, `pack.ps1`, `run-pack.ps1`) all reference `sprk_Spaarke.UI.Components.UniversalDatasetGrid` for self-deployment but nothing in the broader Dataverse solution graph (entity forms, SiteMap, Custom Pages, dashboards) binds to this PCF. **Removing it has no downstream Dataverse impact.**

### 3. SpeAdminApp DashboardPage — **NEVER ON UDG**

| Symbol | Source file | Import |
|---|---|---|
| `<DataGrid>`, `<DataGridHeader>`, `<DataGridCell>`, etc. | `containers/ContainersPage.tsx:40-49` | `from "@fluentui/react-components"` |
| Same | `audit/AuditLogPage.tsx:13-18` | `from "@fluentui/react-components"` |
| Same | `recycle-bin/RecycleBinPage.tsx:9-14` | `from "@fluentui/react-components"` |
| Same | `settings/EnvironmentConfig.tsx:40-46` | `from "@fluentui/react-components"` |
| Same | `files/FileBrowserPage.tsx:14-19` | `from "@fluentui/react-components"` |

Every grid surface in SpeAdminApp consumes **Fluent v9 primitives** from `@fluentui/react-components` — not the Spaarke shared `<DataGrid>` framework and not the UDG PCF.

`DashboardPage.tsx:402-406` contains a comment:
> *"ADR-012: Simple table used for the activity grid (see RecentActivityGrid comment for rationale vs. UniversalDatasetGrid)."*

This comment is the source of task 052's premise. Reading it in context makes the intent clear: it documents a **design choice not to use** the UDG PCF in favor of a simple inline table. DashboardPage was never on UDG, so there is nothing to migrate.

### 4. Stale build artifact (informational only)

`src/client/pcf/SpeDocumentViewer/solution/src/Controls/Spaarke.SpeDocumentViewer/bundle.js` contains the string `UniversalDatasetGrid.js` at lines 2164, 2166, 2170, 3260, 3470. This is leftover from an older bundle when the shared library barrel pulled UDG into every consumer (tree-shaking failure). SpeDocumentViewer's source code does **not** import any DatasetGrid symbol — only `createLogger` from `@spaarke/ui-components/dist/utils/logger`. A clean rebuild after task 051 removes the artifact.

Not a Phase F blocker; flagged for awareness.

---

## Decisions for downstream tasks

| Task | Original premise | Revised premise based on audit | Action |
|---|---|---|---|
| **051** — Remove DatasetGrid components | Audit may reveal consumers we have to migrate first | **No external consumers found.** Drop-delete the 5 view files + `UniversalDatasetGrid.tsx` + barrel exports + test file in one commit. | Proceed as planned; no migration prerequisite. |
| **052** — Migrate DashboardPage from UDG PCF to new DataGrid | DashboardPage binds UDG PCF | **DashboardPage uses Fluent v9 `<DataGrid>` directly, not UDG.** No migration needed. | Mark `partial-scope ✅¹` with audit reference; close as a no-op. Optionally remove the stale comment that triggered the misread. |
| **053** — Retire UDG PCF + solution version bump | UDG PCF may still be referenced by consumers | **No Dataverse references outside its own solution.** Safe to retire end-to-end. | Proceed as planned. |
| **054** — Phase F deploy + UAT (DashboardPage visual diff) | Visual diff vs. pre-UDG render | **No render change** (DashboardPage didn't change). Scope reduces to verifying that removing the UDG solution + deleting the 5 components doesn't break any deployed consumer. | Re-scope UAT to: (a) verify `Deploy-AllDataGridConsumers.ps1` rebuilds + redeploys all 4 active consumers cleanly, (b) verify SpeAdminApp still builds + renders. |

---

## Consumer inventory (complete table)

| Surface | What it imports from `@spaarke/ui-components` | Phase F impact |
|---|---|---|
| `src/solutions/EventsPage/src/App.tsx` | `DataGrid` (new framework), `DataGridPageShell`, `XrmDataverseClient`, `resolveCodePageTheme`, `setupCodePageThemeListener` | None — already on new framework (Phase D) |
| `src/solutions/LegalWorkspace/` | `WorkspaceConfig` types, dynamic workspace builders | None — workspace consumer, not grid consumer |
| `src/solutions/CalendarSidePane/` | `sendSidePaneFilter`, theme helpers | None |
| `src/solutions/WorkspaceLayoutWizard/` | `LAYOUT_TEMPLATES`, section types | None |
| `src/solutions/{Create*Wizard}/` (9 wizards) | `resolveCodePageTheme`, `setupCodePageThemeListener` | None |
| `src/solutions/AllDocuments/` | Theme helpers | None |
| `src/solutions/SpeAdminApp/` | (no `@spaarke/ui-components` imports of grid symbols; uses Fluent v9 directly) | None |
| `src/client/code-pages/SemanticSearch/` | `useDataverse` hook, types | None — Phase E kept its own data layer per scope change |
| `src/client/shared/Spaarke.Events.Components/widgets/CalendarWorkspaceWidget/` | New `DataGrid` framework, side-pane orchestrator | None — already migrated (Phase D task 033) |
| `src/client/pcf/SpeDocumentViewer/control/{index.ts, SpeDocumentViewerHost.tsx}` | `createLogger` (deep path) | None |
| `src/client/pcf/UniversalDatasetGrid/` | (PCF being retired in task 053) | Subject of task 053 |

No surface in the table consumes any of the 5 retiring view components or the `UniversalDatasetGrid` symbol from the barrel.

---

## Acceptance criteria

- [x] Every consumer is listed with a migration target. → All 5 retiring components + UDG symbol have **zero** external consumers. Migration target = N/A (drop-delete).
- [x] No consumer is missed. → 3 independent grep approaches (named-symbol imports, deep-path imports, Dataverse customizations) returned the same result.

---

*Foundation for tasks 051 (proceed), 052 (close as no-op), 053 (proceed), 054 (re-scope UAT).*
