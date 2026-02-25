# Semantic Search Code Page (R3) - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-02-24
> **Source**: design.md (Rev 2, February 24, 2026)
> **Depends On**: ai-semantic-search-ui-r2 (completed — PCF control v1.0.46), ai-semantic-search-foundation-r1 (completed — API endpoints)

---

## Executive Summary

Build a full-page **Semantic Search code page** (Dataverse HTML web resource) that provides system-wide, multi-entity AI-powered search across Documents, Matters, Projects, and Invoices. The code page combines the search/filter UX from the PCF SemanticSearchControl (R2) with graph clustering visualization, the Universal DatasetGrid for tabular results, and saved search favorites — all in a single self-contained HTML file deployed to Dataverse. Also migrates the DocumentRelationshipViewer grid to Universal DatasetGrid for visual consistency.

---

## Scope

### In Scope

- Full-page **React 19** code page deployed as a single self-contained Dataverse HTML web resource
- **Multi-entity search** across Documents, Matters, Projects, and Invoices (single domain at a time via tab selector)
- **Search domain tabs** — horizontal segmented pill buttons (Documents, Matters, Projects, Invoices); single selection only
- Left filter/search pane with domain-adaptive filters (Document Type, File Type, Matter Type, Date Range, Threshold, Mode)
- Graph view with **metadata-driven clustering** (Matter Type, Practice Area, Document Type, Organization, Person/Contact)
- Grid view using **Universal DatasetGrid** shared component with domain-specific columns
- **Saved searches** as favorites stored in `sprk_gridconfiguration` — saving filter strategy + field selections per domain
- Context-sensitive command bar (selection-aware + entity-type-aware actions)
- Entity record dialog on open (Xrm.Navigation.navigateTo with entity-appropriate form)
- Dark mode via Fluent UI v9 tokens (ADR-021)
- **New BFF API endpoint** `POST /api/ai/search/records` for entity record search
- **Enhanced BFF API endpoint** `POST /api/ai/search` — enable `scope=all` and `entityTypes` filter
- **DocumentRelationshipViewer grid migration** to Universal DatasetGrid
- Sitemap entry and command bar button for navigation
- URL parameter support (theme, query, domain, scope, entityId, savedSearchId)

### Out of Scope

- Custom page (model-driven app page) — this is a web resource code page per ADR-006
- User authentication changes (reuses existing MSAL pattern)
- Mobile/responsive layout (desktop-first, Dataverse context)
- Real-time search-as-you-type (explicit Search button, matching PCF behavior)
- Multi-domain simultaneous search (single tab active at a time)
- New AI indexing pipelines (uses existing `knowledge-index` and `spaarke-records-index`)
- Changes to the PCF SemanticSearchControl (R2 remains as-is)

### Affected Areas

| Path | Description |
|------|-------------|
| `src/client/code-pages/SemanticSearch/` | **New** — primary deliverable, full-page code page |
| `src/client/code-pages/DocumentRelationshipViewer/` | **Modified** — migrate grid to Universal DatasetGrid |
| `src/client/shared/Spaarke.UI.Components/` | **Modified** — potential headless data adapter for UniversalDatasetGrid |
| `src/server/api/Sprk.Bff.Api/` | **Modified** — new records search endpoint, enhance document search |
| `src/solutions/` | **Modified** — solution packaging for web resource deployment |

---

## Requirements

### Functional Requirements

| ID | Requirement | Acceptance Criteria |
|----|-------------|---------------------|
| **FR-01** | Multi-entity semantic search | User can search across Documents, Matters, Projects, and Invoices using natural language from a full-page interface |
| **FR-02** | Search domain selector (single-domain) | Horizontal tab bar with Documents/Matters/Projects/Invoices; one tab active at a time; switching tabs re-executes search against appropriate index |
| **FR-03** | Domain-specific grid columns | Grid displays columns appropriate to the active domain (e.g., Matter Number for Matters, Invoice Amount for Invoices, File Type for Documents) |
| **FR-04** | Domain-adaptive filters | Left pane shows/hides filters based on active domain (e.g., File Type hidden when domain ≠ Documents); filters: Document Type, File Type, Matter Type, Date Range, Threshold, Mode |
| **FR-05** | Graph clustering visualization | Results displayed in graph view clustered by selectable metadata category (Matter Type, Practice Area, Document Type, Organization, Person/Contact); node size proportional to record count |
| **FR-06** | Graph drill-down | Click cluster node to expand inline sub-layout showing individual record nodes within that cluster |
| **FR-07** | Grid view with Universal DatasetGrid | Tabular results using shared UniversalDatasetGrid component with sorting, selection, infinite scroll, and 44px row height |
| **FR-08** | Saved search favorites | Save and recall search configurations (query + filters + domain + column selection + view mode) as named favorites stored in `sprk_gridconfiguration` |
| **FR-09** | Selection-aware command bar | Command bar shows context-appropriate actions: Delete, Email a Link, Send to Index (Documents only), Open in Web (Documents only), Open in Desktop (Documents only), Download (Documents only), Refresh |
| **FR-10** | Entity record dialog | Click result opens appropriate entity form in Dataverse dialog (sprk_document, sprk_matter, sprk_project, sprk_invoice); results persist after dialog close |
| **FR-11** | Dark mode support | Full dark mode via Fluent UI v9 tokens; theme detection from Dataverse host or URL parameter |
| **FR-12** | Search query preserved across tab switches | Changing domain tab preserves query text, allowing comparison of results across entity types |
| **FR-13** | URL parameter navigation | Support `theme`, `query`, `domain`, `scope`, `entityId`, `savedSearchId` URL parameters for deep-linking |
| **FR-14** | Sitemap and command bar entry points | Accessible from Dataverse sitemap and global/entity command bars |
| **FR-15** | DocumentRelationshipViewer grid migration | Update existing DocRelViewer `RelationshipGrid.tsx` to use Universal DatasetGrid component |

### Non-Functional Requirements

| ID | Requirement | Target |
|----|-------------|--------|
| **NFR-01** | Search results render time | Within 500ms of API response |
| **NFR-02** | Graph view node capacity | Smooth rendering with up to 100 cluster/record nodes |
| **NFR-03** | Grid infinite scroll capacity | Up to 1000 results via infinite scroll |
| **NFR-04** | Saved search load time | Within 1 second |
| **NFR-05** | First page load | Within 3 seconds (single-file HTML deployment) |
| **NFR-06** | Accessibility | All controls keyboard-navigable; ARIA labels on interactive elements; Fluent UI v9 built-in a11y |
| **NFR-07** | Extensibility | New entity types addable by: adding domain tab, defining columns, adding recordType to API call |

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance | Key Constraint |
|-----|-----------|----------------|
| **ADR-001** | BFF API endpoints | Minimal API pattern; no Azure Functions |
| **ADR-006** | Code page architecture | Web resource code page (not custom page); standalone dialog → Code Page |
| **ADR-008** | API authorization | Endpoint filters for auth; no global middleware |
| **ADR-009** | Caching | Redis-first for expensive AI results |
| **ADR-012** | Shared components | Import from `@spaarke/ui-components`; UniversalDatasetGrid, ViewSelector |
| **ADR-013** | AI architecture | Extend BFF, not separate service; AI Tool Framework patterns |
| **ADR-021** | Fluent UI v9 design | All UI via Fluent v9; tokens for styling; dark mode required |
| **ADR-022** | React versioning | Code Pages: React 19 bundled; PCF: React 16 platform-provided (not applicable here) |

### MUST Rules

- ✅ MUST use React 19 with `createRoot()` entry point (Code Page, ADR-021/022)
- ✅ MUST bundle React 19 + Fluent UI v9 in the Code Page output (not platform-provided)
- ✅ MUST use `@fluentui/react-components` (Fluent v9) exclusively (ADR-021)
- ✅ MUST wrap all UI in `FluentProvider` with theme (ADR-021)
- ✅ MUST use Fluent design tokens for all colors, spacing, typography (ADR-021)
- ✅ MUST support light, dark, and high-contrast modes (ADR-021)
- ✅ MUST use `makeStyles` (Griffel) for custom styling (ADR-021)
- ✅ MUST import icons from `@fluentui/react-icons` (ADR-021)
- ✅ MUST deploy as single self-contained HTML file (two-step build: webpack → inline) (ADR-006)
- ✅ MUST read parameters from `URLSearchParams` (not PCF context) (ADR-021)
- ✅ MUST use endpoint filters for API authorization (ADR-008)
- ✅ MUST import shared components from `@spaarke/ui-components` (ADR-012)
- ✅ MUST follow Minimal API pattern for new endpoints (ADR-001)

### MUST NOT Rules

- ❌ MUST NOT use Fluent v8 (`@fluentui/react`) (ADR-021)
- ❌ MUST NOT hard-code colors (hex, rgb, named colors) (ADR-021)
- ❌ MUST NOT import from granular `@fluentui/react-*` packages (ADR-021)
- ❌ MUST NOT use alternative UI libraries (MUI, Ant Design, etc.) (ADR-021)
- ❌ MUST NOT create a separate AI microservice (ADR-013)
- ❌ MUST NOT call Azure AI services directly from the code page (ADR-013)
- ❌ MUST NOT use global auth middleware for the new endpoints (ADR-008)
- ❌ MUST NOT use Azure Functions for AI processing (ADR-013)

### Existing Patterns to Follow

| Pattern | Reference |
|---------|-----------|
| Code Page structure + build pipeline | `src/client/code-pages/DocumentRelationshipViewer/` |
| MSAL auth provider (singleton) | `src/client/code-pages/DocumentRelationshipViewer/src/services/auth/MsalAuthProvider.ts` |
| Graph visualization (@xyflow/react) | `src/client/code-pages/DocumentRelationshipViewer/src/components/DocumentGraph.tsx` |
| d3-force layout | `src/client/code-pages/DocumentRelationshipViewer/src/hooks/useForceLayout.ts` |
| UniversalDatasetGrid | `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/` |
| ViewSelector (saved views) | `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/ViewSelector.tsx` |
| Filter components (adapt from PCF) | `src/client/pcf/SemanticSearchControl/` (FilterDropdown, DateRangeFilter) |
| BFF API endpoint pattern | `src/server/api/Sprk.Bff.Api/` (existing AI search endpoints) |

---

## Architecture

### Component Structure

```
src/client/code-pages/SemanticSearch/
    src/
        index.tsx                  # React 19 createRoot, URL param parsing, FluentProvider
        App.tsx                    # Main layout: command bar + left pane + main area
        components/
            SearchFilterPane.tsx       # Left sidebar: search input + domain-adaptive filters + search button
            SearchDomainTabs.tsx       # Segmented tab bar (Documents, Matters, Projects, Invoices)
            SearchResultsGrid.tsx      # Grid view wrapper using Universal DatasetGrid
            SearchResultsGraph.tsx     # Graph view using @xyflow/react v12 with clustering
            ClusterNode.tsx            # Cluster node component (category group with count, avg similarity)
            RecordNode.tsx             # Individual record node (document, matter, etc.)
            SearchCommandBar.tsx       # Selection-aware + entity-type-aware command bar
            SavedSearchSelector.tsx    # Favorites dropdown (ViewSelector pattern, backed by sprk_gridconfiguration)
            EntityRecordDialog.tsx     # Entity record dialog wrapper (multi-entity Xrm.Navigation)
        hooks/
            useSemanticSearch.ts       # Search execution, pagination, state management
            useRecordSearch.ts         # Entity record search (Matters, Projects, Invoices)
            useFilterOptions.ts        # Fetch filter options from Dataverse
            useSavedSearches.ts        # CRUD for saved search configurations (sprk_gridconfiguration)
            useDocumentActions.ts      # Open, delete, email, reindex actions
            useClusterLayout.ts        # d3-force layout with clustering by metadata
        services/
            SemanticSearchApiService.ts    # BFF API client — document search (POST /api/ai/search)
            RecordSearchApiService.ts      # BFF API client — entity record search (POST /api/ai/search/records)
            auth/
                MsalAuthProvider.ts        # MSAL singleton (same pattern as DocRelViewer)
                msalConfig.ts              # App registration config
        types/
            index.ts                   # All type definitions
    index.html                     # Minimal HTML template
    webpack.config.js              # Production webpack (no externals, bundles React 19)
    build-webresource.ps1          # Inline bundle.js into HTML → single self-contained file
    package.json                   # Dependencies
    tsconfig.json                  # TypeScript config
```

### Technology Stack

| Component | Technology | Version | Rationale |
|-----------|-----------|---------|-----------|
| UI Framework | React | **19** | Bundled in Code Page (ADR-006, ADR-022) |
| Component Library | Fluent UI v9 | 9.54+ | Design system (ADR-021) |
| Graph Visualization | @xyflow/react | 12.x | Proven in DocumentRelationshipViewer |
| Graph Layout | d3-force | 3.x | Physics-based node positioning with clustering |
| Data Grid | Universal DatasetGrid | Shared lib | Consistent with platform grids (ADR-012) |
| Authentication | @azure/msal-browser | 3.x | Existing pattern |
| Build | webpack | 5.x | Existing code page pattern |
| Language | TypeScript | 5.4+ | Existing pattern |

### Build & Deployment Pipeline

Two-step build producing a single self-contained HTML file:

```
Step 1: npm run build → out/bundle.js
        Webpack bundles ALL code (React 19, Fluent UI, @xyflow/react, d3-force,
        application code) into a single bundle.js

Step 2: build-webresource.ps1 → out/sprk_semanticsearch.html
        Inlines bundle.js + CSS into the HTML template → single file with
        all HTML, CSS, and JavaScript embedded

Deploy: Upload sprk_semanticsearch.html to Dataverse as Webpage (HTML) web resource
```

### Backend API Changes

#### Enhanced Endpoint — Document Search

**`POST /api/ai/search`** — Enable `scope=all` and add `entityTypes` filter.

Request additions:
```json
{
  "scope": "all",
  "entityTypes": ["matter", "project", "invoice"]
}
```

Response includes per result: `documentId`, `name`, `fileType`, `documentType`, `combinedScore`, `highlights`, `parentEntityType`, `parentEntityId`, `parentEntityName`, `fileUrl`, `recordUrl`, `createdAt`, `updatedAt`, `summary`, `tldr`.

#### New Endpoint — Entity Records Search

**`POST /api/ai/search/records`** — Search `spaarke-records-index` for Matters, Projects, Invoices.

Request:
```json
{
  "query": "Acme Corporation litigation",
  "recordTypes": ["sprk_matter", "sprk_project", "sprk_invoice"],
  "filters": {
    "organizations": [],
    "people": [],
    "referenceNumbers": []
  },
  "options": {
    "limit": 20,
    "offset": 0,
    "hybridMode": "rrf | vectorOnly | keywordOnly"
  }
}
```

Response per result: `recordId`, `recordType`, `recordName`, `recordDescription`, `confidenceScore`, `matchReasons`, `organizations`, `people`, `keywords`, `createdAt`, `modifiedAt`. Plus metadata: `totalCount`, `searchTime`, `hybridMode`.

#### Domain → API Service Routing

| Domain | Service | Index |
|--------|---------|-------|
| Documents | SemanticSearchApiService → `POST /api/ai/search` | `knowledge-index` |
| Matters | RecordSearchApiService → `POST /api/ai/search/records` (recordTypes: `["sprk_matter"]`) | `spaarke-records-index` |
| Projects | RecordSearchApiService → `POST /api/ai/search/records` (recordTypes: `["sprk_project"]`) | `spaarke-records-index` |
| Invoices | RecordSearchApiService → `POST /api/ai/search/records` (recordTypes: `["sprk_invoice"]`) | `spaarke-records-index` |

---

## UI Design

### Layout Structure

```
+------------------------------------------------------------------+
| Command Bar: [Delete] [Refresh] [Email] [Send to Index]...         |
+------------------------------------------------------------------+
| [Saved Searches v]  [Graph] [Grid]  [Filter icon]                  |
+--------+-----------------------------------------------------------+
|        |  (Documents)   Matters   Projects   Invoices    ← Tabs    |
|        |-----------------------------------------------------------+
| Search |                                                            |
| Input  |     Main Content Area                                      |
|--------|     (Grid View or Graph View)                              |
| Filters|                                                            |
|        |     Grid: Domain-specific columns                          |
| Doc    |     Graph: Clustered by metadata categories                |
| Type   |                                                            |
|        |                                                            |
| File   |                                                            |
| Type   |                                                            |
|        |                                                            |
| Matter |                                                            |
| Type   |                                                            |
|        |                                                            |
| Date   |                                                            |
| Range  |                                                            |
|        |                                                            |
| Thres- |                                                            |
| hold   |                                                            |
|        |                                                            |
| Mode   |                                                            |
|        |                                                            |
|[Search]|                                                            |
+--------+-----------------------------------------------------------+
| Status bar: X results found  ·  search time  ·  version            |
+------------------------------------------------------------------+
```

### Domain-Specific Grid Columns

**Documents**: checkbox, Document (file icon + name), Similarity (%), Type, File Type, Parent Entity, Modified

**Matters**: checkbox, Matter Name, Similarity, Matter Number, Matter Type, Practice Area, Organizations, Modified

**Projects**: checkbox, Project Name, Similarity, Status, Parent Matter, Modified

**Invoices**: checkbox, Invoice (number + vendor), Similarity, Amount (currency), Vendor, Parent Matter, Date

### Domain → Filter Visibility

| Domain | Visible Filters |
|--------|----------------|
| Documents | Document Type, File Type, Matter Type, Date Range, Threshold, Mode |
| Matters | Matter Type, Date Range, Threshold, Mode |
| Projects | Date Range, Threshold, Mode |
| Invoices | Date Range, Threshold, Mode |

### Graph Clustering

- Cluster by selectable category: Matter Type, Practice Area, Document Type, Organization, Person/Contact
- Cluster nodes show: category icon + label, record count, average similarity bar, top 3 results preview
- d3-force layout with cluster gravity (same cluster attracts, different repels)
- Cross-cluster edges where results share relationships (thickness proportional to relationship count)
- Click cluster → inline expand to individual record nodes (sub-layout)
- Color-coded by cluster value (Fluent UI v9 palette, dark-mode safe)
- Zoom controls and minimap (from DocumentRelationshipViewer pattern)

### Saved Search Schema (sprk_gridconfiguration)

Combined schema capturing filter strategy + field selection per domain:
```json
{
  "name": "Active Litigation Contracts",
  "searchDomain": "Documents",
  "query": "contract amendments",
  "filters": {
    "documentTypes": ["Contract"],
    "fileTypes": ["pdf", "docx"],
    "matterTypes": ["Litigation"],
    "dateRange": { "from": "2025-01-01", "to": null },
    "threshold": 50,
    "searchMode": "hybrid"
  },
  "viewMode": "grid",
  "columns": ["name", "similarity", "documentType", "parentEntity", "modified"],
  "sortColumn": "similarity",
  "sortDirection": "desc",
  "graphClusterBy": "matterType"
}
```

Default system searches: "All Documents", "All Matters", "Recent Documents", "High Similarity".

---

## Success Criteria

| # | Criterion | Verification Method |
|---|-----------|---------------------|
| 1 | User can search across Documents, Matters, Projects, Invoices from full-page interface | Navigate via sitemap, execute searches across all 4 domains |
| 2 | Search domain tabs change available filters and result columns | Switch tabs, verify filter visibility and grid column changes |
| 3 | Results display in both Grid (Universal DatasetGrid) and Graph (clustered) views | Toggle view modes, verify correct rendering |
| 4 | Graph view clusters by Matter Type, Practice Area, Document Type, Organization, Person/Contact | Select each clustering mode, verify node grouping |
| 5 | All applicable filters work per domain | Apply filters in each domain, verify search results update |
| 6 | Users can save and recall search configurations as favorites | Save, reload page, verify saved search restores all settings |
| 7 | Command bar shows context-appropriate actions based on selection and entity type | Select records in each domain, verify available actions |
| 8 | Opening a record shows the appropriate entity record dialog | Click results in each domain, verify correct form opens |
| 9 | Dark mode works correctly | Toggle to dark theme, verify no broken colors or unreadable text |
| 10 | Single self-contained HTML file deploys to Dataverse | Build, deploy, verify loads correctly as web resource |
| 11 | DocumentRelationshipViewer grid migrated to Universal DatasetGrid | Open DocRelViewer, verify grid uses new component |
| 12 | `POST /api/ai/search` supports `scope=all` and `entityTypes` filter | API test with scope=all, verify document results returned |
| 13 | `POST /api/ai/search/records` returns Matters, Projects, Invoices | API test with each recordType, verify results |

---

## Dependencies

### Prerequisites

| Dependency | Status | Notes |
|------------|--------|-------|
| ai-semantic-search-foundation-r1 | **Completed** | API endpoints available |
| ai-semantic-search-ui-r2 (PCF) | **Completed** | v1.0.46 deployed; filter UX patterns to reuse |
| DocumentRelationshipViewer Code Page | **Completed** | v1.0.3 deployed; graph patterns to reuse |
| BFF API deployed | **Available** | `spe-api-dev-67e2xz.azurewebsites.net` |
| Dataverse environment | **Available** | `spaarkedev1.crm.dynamics.com` |
| Universal DatasetGrid | **Available** | v2.0.7 in shared library |
| ViewSelector | **Available** | In shared library |

### Investigation Required

| Item | What to Verify | Blocks |
|------|---------------|--------|
| `spaarke-records-index` coverage | Are ALL Matters, Projects, Invoices indexed? Are grid column fields (Matter Number, Practice Area, Organizations, Invoice Amount, Vendor, etc.) available in the index? | FR-02, FR-03 (entity record search) |
| UniversalDatasetGrid headless data adapter | Can the grid accept search results (from BFF API) instead of FetchXML data? Does `headlessConfig` support custom data sources? | FR-07 (grid view) |

### Reusable Components

| Component | Source | Reuse Strategy |
|-----------|--------|----------------|
| FilterDropdown | PCF SemanticSearchControl | Copy + adapt (React 16→19 minor) |
| DateRangeFilter | PCF SemanticSearchControl | Copy + adapt |
| ViewSelector | Shared library | Import directly |
| ViewService | Shared library | Import directly |
| UniversalDatasetGrid | Shared library | Import directly (may need headless adapter) |
| MsalAuthProvider | DocumentRelationshipViewer | Copy (same pattern) |
| DocumentNode/Edge | DocumentRelationshipViewer | Extend for multi-entity + clustering |
| useForceLayout | DocumentRelationshipViewer | Extend for cluster gravity |

### External Dependencies (npm)

| Dependency | Version | Purpose |
|------------|---------|---------|
| react, react-dom | 19.x | UI framework (bundled) |
| @fluentui/react-components | 9.54+ | UI components (bundled) |
| @fluentui/react-icons | 2.x | Icons (bundled) |
| @xyflow/react | 12.x | Graph visualization |
| d3-force | 3.x | Physics-based layout |
| @azure/msal-browser | 3.x | Authentication |
| webpack | 5.x | Build tooling |
| typescript | 5.4+ | Language |

---

## Owner Clarifications

*Answers captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Records index coverage | Is `spaarke-records-index` populated for all entities with all needed fields? | **Not sure — needs investigation** | Add investigation/spike task before building entity record search UI. Must verify index has Matter Number, Practice Area, Organizations, Invoice Amount, Vendor, etc. |
| Saved search storage | `userquery` vs `sprk_gridconfiguration` vs new entity? | **`sprk_gridconfiguration` (custom)** | Use existing Spaarke custom table. More schema control, already used by Events page ViewSelector. |
| Cross-domain search | Single domain or multi-domain simultaneous? | **Single domain only** | One tab active at a time. Simpler UX and implementation. Query text preserved across tab switches for easy comparison. |
| DocRelViewer grid migration | Include in this project or separate? | **Yes, include in this project** | Adds tasks for migrating `RelationshipGrid.tsx` to Universal DatasetGrid. Ensures visual consistency ships together. |

---

## Assumptions

*Proceeding with these assumptions (owner did not specify):*

- **Graph drill-down behavior**: Assuming inline sub-layout within the graph canvas (not navigation to a filtered view). Matches DocumentRelationshipViewer's expand pattern.
- **Sitemap placement**: Assuming under an "AI" or "Search" area group. Exact placement finalized during deployment tasks.
- **Future entity types**: Assuming the extensibility design in the design document (Section 11.4) is sufficient. No pre-building for specific future entities.
- **Saved search permissions**: Assuming personal only (user sees their own saved searches). System-provided defaults visible to all.
- **Graph node limit**: Assuming limit of 100 nodes in graph view. Results exceeding this show top 100 by similarity. User can switch to grid for full result set.
- **Bulk delete permissions**: Assuming standard Dataverse security roles govern delete permissions. Code page respects existing privilege checks.
- **Records index field availability**: Assuming standard entity fields are available but need verification (investigation task). If specific fields are missing from the index, grid columns will be adjusted to match available data.

---

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| `scope=all` not supported in BFF API | Blocks system-wide document search | Low | BFF update is a project task; `parentEntityType` field already exists in index |
| Records index not populated for all entities | Missing Matters/Projects/Invoices in search | Medium | Investigation task added; add indexing pipeline tasks if needed |
| Universal DatasetGrid doesn't support headless data | Blocks grid integration | Medium | Build data adapter; fall back to custom Fluent DataGrid if needed |
| Bundle size too large (React 19 + graph + d3) | Slow load time exceeding 3s target | Medium | Tree-shake imports, lazy-load graph view, code-split |
| MSAL token scoping for cross-entity access | Auth errors on broad search | Low | Verify token scope covers all entity types; add error handling |
| Graph clustering performance with large result sets | UI lag with 500+ results | Medium | Limit graph to top 100 results; paginate within clusters |
| `sprk_gridconfiguration` schema doesn't support saved search fields | Blocks saved search feature | Low | Investigate current schema; extend if needed |

---

## Unresolved Questions

*To be resolved during implementation (investigation tasks):*

- [ ] **Records index field coverage**: Which specific fields are available per entity type in `spaarke-records-index`? Does the index contain all fields needed for grid columns (Matter Number, Practice Area, Organizations, Invoice Amount, Vendor, Status, etc.)? — Blocks: FR-03 (domain-specific columns)
- [ ] **UniversalDatasetGrid headless support**: Does the current `headlessConfig` API support custom data sources (non-FetchXML), or does a data adapter need to be built? — Blocks: FR-07 (grid view)
- [ ] **sprk_gridconfiguration schema**: Does the existing table schema support the saved search JSON structure, or does it need schema extension? — Blocks: FR-08 (saved searches)

---

*AI-optimized specification. Original design: design.md (Rev 2, February 24, 2026)*
