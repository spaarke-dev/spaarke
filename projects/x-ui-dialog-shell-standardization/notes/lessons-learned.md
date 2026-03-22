# Lessons Learned — UI Dialog & Shell Standardization

> **Project**: ui-dialog-shell-standardization
> **Completed**: 2026-03-19

---

## What Went Well

- **IDataService abstraction pattern worked cleanly across all 7 wizards + 2 tool dialogs** — The runtime-injected data service interface (IDataService, IUploadService, INavigationService) allowed every shared component to remain platform-agnostic. Dataverse consumers inject XrmDataService; Power Pages SPA injects BffDataService. Zero direct webApi calls remain in the shared library.

- **Parallel task execution with file-ownership constraints prevented merge conflicts** — Breaking work so each parallel task owned distinct files/directories meant no merge conflicts during the extraction phases. The task decomposition's explicit file-ownership annotations were essential for this.

- **Code Page wrapper pattern is highly replicable (~30-50 LOC each)** — Every Code Page wrapper follows the same structure: parse URL params, detect theme, create IDataService, render shared component inside FluentProvider. This consistency made the 8 wrapper tasks fast and predictable.

- **BFF adapter pattern enables SPA reuse of all shared components** — The IDataService abstraction paid off immediately for Power Pages integration. BffDataService wraps fetch calls to the BFF API, and shared components render identically without any code changes.

---

## What Was Harder Than Expected

- **Navigation property discovery in matterService.ts required careful handling during extraction** — Metadata API calls (`RetrieveMetadataChanges`, `$expand` on navigation properties) are platform-bound to Xrm.WebApi. Abstracting these through IDataService required designing a `getRelatedRecords()` method that hides the navigation property mechanics while remaining flexible enough for different entity relationships.

- **WorkspaceGrid.tsx didn't reach the 500 LOC target (830 LOC remaining due to workspace-specific dialogs that were out of scope)** — QuickSummaryDashboard, GetStartedExpandDialog, and CloseProjectDialog remain inline because they are workspace-specific and not reusable. This was a known scope exclusion but the remaining LOC count is higher than the graduation target anticipated.

- **Barrel export coordination across parallel extraction tasks required the dedicated 005c/011b consolidation tasks** — Multiple parallel extraction tasks each needed to add exports to the shared library's index.ts barrel file. Without a dedicated consolidation task to serialize barrel file updates after parallel work completed, there would have been constant conflicts. The explicit consolidation tasks (005c, 011b) solved this cleanly.

---

## Patterns to Reuse

### Three-Layer Model: Shared Library -> Code Page Wrapper -> Consumer

This architecture should be the default for any new wizard, dialog, or tool UI:

1. **Shared Library** (`@spaarke/ui-components`) — Component logic, steps, validation, state management
2. **Code Page Wrapper** (`src/solutions/`) — Thin entry point (~30-50 LOC): parse params, detect theme, render
3. **Consumer** — Opens via `navigateTo` (Dataverse) or renders inline (Power Pages SPA)

### IDataService / IUploadService / INavigationService Abstraction

Runtime-injected service interfaces decouple shared components from any specific platform API:

- `IDataService` — CRUD operations (createRecord, retrieveRecord, retrieveMultiple, updateRecord, deleteRecord, getRelatedRecords)
- `IUploadService` — File upload operations (uploadFile, getUploadStatus)
- `INavigationService` — Navigation operations (openForm, openUrl, navigateTo)

Implementations: `XrmDataService` (Dataverse), `BffDataService` (Power Pages SPA via BFF API).

### Vite + vite-plugin-singlefile for Single-HTML Code Pages

Every Code Page uses the same Vite config pattern producing a single HTML file with all JS/CSS inlined. This is the ADR-026 standard and should not be deviated from.

### Intent-Based Pre-Selection for PlaybookLibraryShell

Pass `?intent=risk-assessment` (or similar) via URL params to pre-filter the playbook library to a specific category. This replaces the old QuickStart approach of hard-coding action shortcuts and provides a more flexible, data-driven entry point.

### navigateTo Promise for Post-Dialog Refresh

```typescript
const result = await Xrm.Navigation.navigateTo(
  { pageType: "webresource", webresourceName: "sprk_creatematterwizard", data: params },
  { target: 2, width: { value: 85, unit: "%" }, height: { value: 85, unit: "%" } }
);
// Dialog closed — refresh grid data
await refreshGridData();
```

The `navigateTo` call returns a Promise that resolves when the dialog closes. This is the correct pattern for Corporate Workspace to know when to refetch data after a wizard completes.

---

*Generated: 2026-03-19*
