# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-02-25
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | UniversalDatasetGrid Multi-Source Adaptation |
| **Step** | Phase 1+2: Shared library + Semantic Search adoption |
| **Status** | in-progress |
| **Next Action** | Implementing shared library changes (external data mode, renderer registration) then replacing SearchResultsGrid with UniversalDatasetGrid in App.tsx |

### Active Work: UniversalDatasetGrid Adaptation

**Spec**: `projects/ai-semantic-search-ui-r3/notes/universal-dataset-grid-adaptation-spec.md`
**EventsPage Migration Guide**: `projects/ai-semantic-search-ui-r3/notes/eventspage-grid-migration-guide.md`

**What we're doing**: Extending `@spaarke/ui-components` UniversalDatasetGrid to support a 3rd data mode ("External Data") for non-Dataverse sources (Azure AI Search, BFF APIs). Then adopting it in the Semantic Search Code Page to replace the custom `SearchResultsGrid.tsx`. Also creating a migration guide for EventsPage to follow the same pattern.

**Phase 1 — Shared Library Changes** (in `src/client/shared/Spaarke.UI.Components/`):
- [x] P1A: Add `IExternalDataConfig` interface to `DatasetTypes.ts`
- [x] P1B: Create `useExternalDataMode.ts` hook
- [x] P1C: Add `registerRenderer()` API + `Percentage`/`StringArray`/`FileType` renderers
- [x] P1D: Update `UniversalDatasetGrid.tsx` to detect and route `externalConfig`

**Phase 2 — Semantic Search Adoption** (in `src/client/code-pages/SemanticSearch/`):
- [ ] P2A: Create `searchResultAdapter.ts` — map `DocumentSearchResult`/`RecordSearchResult` → `IDatasetRecord[]`
- [ ] P2B: Create `useSearchViewDefinitions.ts` — fetch column configs from `sprk_gridconfiguration` with `domainColumns.ts` fallback
- [ ] P2C: Replace `SearchResultsGrid` with `UniversalDatasetGrid` in `App.tsx`
- [ ] Build + deploy + verify

**Key Files (Shared Library — main spaarke repo)**:
- `src/client/shared/Spaarke.UI.Components/src/types/DatasetTypes.ts` — added `IExternalDataConfig`
- `src/client/shared/Spaarke.UI.Components/src/hooks/useExternalDataMode.ts` — NEW
- `src/client/shared/Spaarke.UI.Components/src/services/ColumnRendererService.tsx` — added `registerRenderer()`
- `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/UniversalDatasetGrid.tsx` — added mode 3

**Key Types**:
```typescript
// IExternalDataConfig — caller provides pre-fetched data, grid displays it
interface IExternalDataConfig {
  records: IDatasetRecord[];     // Mapped data from any source
  columns: IDatasetColumn[];     // Column definitions (from sprk_gridconfiguration or fallback)
  loading: boolean;
  error?: string | null;
  totalCount?: number;
  hasNextPage?: boolean;
  onLoadNextPage?: () => void;
  onRefresh?: () => void;
}

// IDatasetRecord — simple shape, works for any data source
interface IDatasetRecord { id: string; entityName: string; [key: string]: any; }

// IDatasetColumn — drives rendering via dataType
interface IDatasetColumn { name: string; displayName: string; dataType: string; ... }
```

**Important**: Side panes (`Xrm.App.sidePanes`) are NOT part of grid architecture. They're in EventsPage `App.tsx` `onRecordClick` handler. UniversalDatasetGrid already has `onRecordClick(recordId)` — side panes work unchanged.

---

## Project Overview

**Project**: AI Semantic Search UI R3 — Full-page React 18 Code Page for system-wide AI-powered semantic search in Dataverse.

**Branch**: `work/ai-semantic-search-ui-r3`

**Completion**: 52/54 tasks complete. Tasks 071 (sitemap) and 073 (E2E validation) are manual/in-progress.

**All code is written**. We are in the **manual deployment + E2E testing** phase. The user is uploading the built HTML web resource to Dataverse and testing in a browser.

---

## E2E Bug-Fix History (Critical Context)

### Round 1 Bugs Found (user deployed first build)

| Bug | Root Cause | Fix Applied | File |
|-----|-----------|-------------|------|
| MSAL not initialized error | `index.tsx` rendered without calling `msalAuthProvider.initialize()` first | Added async MSAL init before `createRoot().render()` | `src/client/code-pages/SemanticSearch/src/index.tsx` |
| No search query text field | `SearchFilterPane` had no `<Input>` element | Added `<Input>` with search icon, `query`/`onQueryChange` props | `src/client/code-pages/SemanticSearch/src/components/SearchFilterPane.tsx` |
| Filter dropdowns show placeholders | Placeholder `<div>` elements instead of real components | Replaced with real `<FilterDropdown>` and `<DateRangeFilter>` components | `src/client/code-pages/SemanticSearch/src/components/SearchFilterPane.tsx` |
| Dark mode defaulting incorrectly | ThemeProvider checked `navbarbackgroundcolor` (brand color = dark red) | Changed to check `backgroundcolor` instead | `src/client/code-pages/SemanticSearch/src/providers/ThemeProvider.ts` |

### Round 2 Bugs Found (user tested Round 1 fixes)

| Bug | Root Cause | Fix Applied | File |
|-----|-----------|-------------|------|
| Dark mode STILL active | `index.html` body had no background-color → transparent → system preference fallback (user's Windows = dark) | Added `background-color: #ffffff` to `html, body` style | `src/client/code-pages/SemanticSearch/index.html` |
| AADSTS500011 token error | `BFF_API_SCOPES` used client app ID (`170c98e1...`) instead of BFF API app ID (`1e40baad...`) | Added separate `BFF_API_APP_ID` constant, fixed scope URI | `src/client/code-pages/SemanticSearch/src/services/auth/msalConfig.ts` |
| 404 on sprk_gridconfigurations | Entity doesn't exist in dev environment | No code fix needed — non-critical, saved searches show empty | N/A |

### Round 2 Status: Fixes applied, rebuilt, **awaiting user upload and test**

**Built artifact**: `src/client/code-pages/SemanticSearch/out/sprk_semanticsearch.html` (1,177 KB)

---

## Key Architecture & Auth Details

### Two Azure AD App Registrations (CRITICAL)

| App | ID | Purpose |
|-----|----|---------|
| **Client App** (MSAL clientId) | `170c98e1-d486-4355-bcbe-170454e0207c` | Used in `msalConfig.ts` for `auth.clientId` |
| **BFF API App** (scope target) | `1e40baad-e065-4aea-a8d4-4b7ab273458c` | Used in `BFF_API_SCOPES`: `api://1e40baad.../user_impersonation` |

**DO NOT confuse these.** The client app ID is for MSAL login. The BFF API app ID is for the scope URI. All PCF controls in the repo use `1e40baad...` for the BFF API scope.

### Two-Step Build Pipeline

```
npm run build         → webpack → out/bundle.js
build-webresource.ps1 → inlines bundle.js into index.html → out/sprk_semanticsearch.html
```

**Both steps required** before uploading to Dataverse.

### Code Page vs PCF Control

| Component | Type | React Version | Bundle |
|-----------|------|--------------|--------|
| `cc_Sprk.SemanticSearchControl/bundle.js` | Field-bound PCF control | React 16 | Separate |
| `sprk_semanticsearch` (this project) | Standalone Code Page | React 18 | Self-contained HTML |

### Dataverse Web Resource

- **Name**: `sprk_semanticsearch` (no `.html` extension in name)
- **Type**: Web Page (HTML)
- **Content**: Upload `out/sprk_semanticsearch.html`

### ThemeProvider Priority (4 levels)

1. URL param `theme=dark|light`
2. Xrm frame-walk → `backgroundcolor` (NOT `navbarbackgroundcolor`)
3. System preference (`prefers-color-scheme`)
4. Default: light (via `index.html` white background)

---

## Files Modified During E2E Bug-Fix (Since Last Commit)

### Code Page Source (all under `src/client/code-pages/SemanticSearch/`)

| File | Changes |
|------|---------|
| `index.html` | Added `background-color: #ffffff` to body style |
| `src/index.tsx` | Added MSAL `initialize()` before render; async init pattern |
| `src/App.tsx` | Added `query` and `onQueryChange={setQuery}` props to SearchFilterPane |
| `src/components/SearchFilterPane.tsx` | Added `<Input>` search field, replaced placeholder divs with real FilterDropdown/DateRangeFilter components, added `query`/`onQueryChange` props, added handler callbacks |
| `src/services/auth/msalConfig.ts` | Added `BFF_API_APP_ID` constant, fixed `BFF_API_SCOPES` to use API app ID |
| `src/providers/ThemeProvider.ts` | Changed from `navbarbackgroundcolor` to `backgroundcolor`, changed DOM fallback from navbar to `document.body` |

### BFF API & Tests (modified earlier in task execution, before last commit)

All BFF API changes, integration tests, and unit tests are listed in the git diff output above. These are modifications from Phases 2-7 task execution.

---

## Remaining Work

| # | Item | Owner | Status |
|---|------|-------|--------|
| 1 | Upload Round 2 `sprk_semanticsearch.html` to Dataverse | User | Pending |
| 2 | Test Round 2 fixes in browser | User | Pending |
| 3 | Fix any remaining bugs found | Claude | On-demand |
| 4 | Task 071: Add sitemap entry | User | Manual Dataverse config |
| 5 | Task 073: E2E validation checklist | User | `notes/archive/debug/e2e-validation.md` |
| 6 | Git commit all changes | Claude | After E2E passes |
| 7 | Push to feature branch | Claude | After commit |
| 8 | `/merge-to-master` | Claude | After push |

---

## Build & Deploy Commands

```bash
# Build (from code page directory)
cd src/client/code-pages/SemanticSearch
npm run build

# Generate self-contained HTML
pwsh -File build-webresource.ps1

# Output: out/sprk_semanticsearch.html (upload this to Dataverse)
```

---

## Session Notes

### Key Learnings
- ADR-021 (revised 2026-02-23) confirms React 18 for Code Pages (project was initialized as React 19 but uses React 18 APIs)
- DocumentRelationshipViewer uses webpack (not Vite as ADR-026 suggests)
- Bundle size dominated by Fluent UI umbrella package (~33-37%)
- esbuild-loader provides ~2x build speedup over ts-loader
- **MSAL must initialize before rendering** — async init pattern required in Code Pages
- **Two app registrations**: client app ID ≠ BFF API app ID — scope uses API app ID
- **Xrm theme detection**: `backgroundcolor` = content area (light/dark indicator), `navbarbackgroundcolor` = brand color (always dark for Spaarke — NOT a dark mode indicator)
- **index.html must set explicit background-color** to prevent system dark mode preference from leaking through transparent backgrounds

---

## Quick Reference

### Project Context
- **Project**: ai-semantic-search-ui-r3
- **Branch**: `work/ai-semantic-search-ui-r3`
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)
- **E2E Validation Checklist**: [`notes/archive/debug/e2e-validation.md`](./notes/archive/debug/e2e-validation.md)
- **BFF API Base**: `https://spe-api-dev-67e2xz.azurewebsites.net`
- **Dataverse Dev**: `https://spaarkedev1.crm.dynamics.com`

---

*This file is the primary source of truth for active work state. Keep it updated.*
