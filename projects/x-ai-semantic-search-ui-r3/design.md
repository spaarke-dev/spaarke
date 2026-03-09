# Semantic Search Code Page — Design Document

> **Project**: ai-semantic-search-ui-r3
> **Author**: Ralph Schroeder
> **Date**: February 24, 2026
> **Status**: Draft — Rev 2

---

## 1. Executive Summary

Build a full-page Semantic Search code page (Dataverse web resource) that provides **system-wide, multi-entity AI-powered search**. Users can search across Documents, Matters, Projects, and Invoices — the four primary business entities — using natural language, with the ability to add more entity types in the future.

This complements the existing matter-bound SemanticSearchControl PCF (R2) with a standalone experience accessible from the Dataverse sitemap, command bars, or direct URL.

The code page combines the search/filter UX established in the PCF with the Graph/Grid visualization from the DocumentRelationshipViewer, using the Universal DatasetGrid shared component for the tabular view, the ViewSelector pattern for saved searches, and a metadata-driven graph clustering visualization.

**Key capabilities**:
- **Multi-entity search**: Search Documents, Matters, Projects, Invoices (extensible to future entity types)
- **Search domain selection**: User chooses which record type(s) to search, with domain-specific result columns
- **Graph clustering**: Visualize results clustered by metadata categories (Matter Type, Practice Area, Document Type, Organization, Person/Contact)
- **Saved searches**: Save both filter strategies and domain-specific field selections as favorites
- **Consistent UX**: Threshold and Mode as dropdowns (matching PCF v1.0.46)

---

## 2. Problem Statement

Users can currently search documents only within the context of a specific Matter form (via the PCF control). There is no way to:
- Search across all documents in the system regardless of parent entity
- **Search for Matters, Projects, or Invoices directly** by name, description, or related entities
- Save and recall frequently-used search configurations
- View search results in a full-page graph visualization with meaningful clustering
- Perform bulk operations (delete, email, reindex) on search results
- Access semantic search from the Dataverse sitemap without navigating to a specific record
- See domain-specific columns when searching different entity types

---

## 3. Scope

### In Scope
- Full-page **React 19** code page deployed as a single self-contained Dataverse HTML web resource
- **Multi-entity search** across Documents, Matters, Projects, and Invoices
- **Search domain selector** — user picks which entity type(s) to search
- Left filter/search pane with all filters from PCF plus Matter Type
- Graph and Grid view toggle for search results
- **Graph clustering** by metadata categories (Matter Type, Practice Area, Document Type, Organization, Person/Contact) with node size proportional to record count
- Grid view using Universal DatasetGrid shared component with **domain-specific columns**
- Saved Searches as "Favorites" — saving both filter strategies AND field selections per domain
- Context-sensitive command bar (selection-aware actions)
- Entity record dialog on open (document → Document form, matter → Matter form, etc.)
- Dark mode support via Fluent UI v9 tokens
- **New BFF API endpoints** as needed for multi-entity search
- Update existing DocumentRelationshipViewer grid to use Universal DatasetGrid

### Out of Scope
- Custom page (model-driven app page) — this is a web resource code page per ADR-006
- User authentication changes (reuses existing MSAL pattern)
- Mobile/responsive layout (desktop-first, Dataverse context)
- Real-time search-as-you-type (explicit Search button, matching PCF behavior)

---

## 4. User Stories

### US-1: Multi-Entity Semantic Search
**As a** legal professional, **I want to** search across Documents, Matters, Projects, and Invoices using natural language, **so that** I can find relevant records regardless of type or which entity they belong to.

### US-2: Search Domain Selection
**As a** user, **I want to** select which entity type(s) to search (Documents, Matters, Projects, Invoices), **so that** I can focus my search on the record types most relevant to my current task.

### US-3: Domain-Specific Result Columns
**As a** user, **I want to** see columns appropriate to the entity type I'm searching (e.g., Matter Number for Matters, Invoice Number for Invoices, File Type for Documents), **so that** results are meaningful and actionable.

### US-4: Filter and Refine Results
**As a** user, **I want to** filter search results by Document Type, File Type, Matter Type, Date Range, Threshold, and Search Mode, **so that** I can narrow results to what's relevant.

### US-5: Visualize Results with Clustering
**As a** user, **I want to** view search results in a graph visualization clustered by metadata categories (Matter Type, Practice Area, Document Type, Organization, Person/Contact), **so that** I can understand patterns and relationships across results.

### US-6: Save Favorite Searches
**As a** user, **I want to** save my search configurations (query + filters + domain + column selection) as named favorites, **so that** I can quickly recall frequently-used searches without re-entering criteria.

### US-7: Perform Bulk Operations
**As a** user, **I want to** select multiple records and perform batch actions (delete, email links, send to AI index), **so that** I can manage records efficiently from search results.

### US-8: Open Entity Record
**As a** user, **I want to** click on a search result and see the full entity record in a dialog (Document form, Matter form, etc.), **so that** I can view/edit details without leaving the search page.

---

## 5. Architecture

### 5.1 Code Page Pattern (ADR-006)

Following the established code page architecture with **single-file deployment**:

```
src/client/code-pages/SemanticSearch/
    src/
        index.tsx               # React 19 createRoot, URL param parsing, FluentProvider
        App.tsx                 # Main layout: command bar + left pane + main area
        components/
            SearchFilterPane.tsx    # Left sidebar: search domain + input + filters + search button
            SearchDomainTabs.tsx     # Segmented tab bar (Documents, Matters, Projects, Invoices)
            SearchResultsGrid.tsx   # Grid view wrapper using Universal DatasetGrid
            SearchResultsGraph.tsx  # Graph view using @xyflow/react v12 with clustering
            ClusterNode.tsx         # Cluster node component (category group)
            RecordNode.tsx          # Individual record node (document, matter, etc.)
            SearchCommandBar.tsx    # Selection-aware command bar
            SavedSearchSelector.tsx # Favorites dropdown (ViewSelector pattern)
            EntityRecordDialog.tsx  # Entity record dialog wrapper (multi-entity)
        hooks/
            useSemanticSearch.ts    # Search execution, pagination, state management
            useRecordSearch.ts      # Entity record search (Matters, Projects, Invoices)
            useFilterOptions.ts     # Fetch filter options from Dataverse
            useSavedSearches.ts     # CRUD for saved search configurations
            useDocumentActions.ts   # Open, delete, email, reindex actions
            useClusterLayout.ts     # d3-force layout with clustering by metadata
        services/
            SemanticSearchApiService.ts  # BFF API client — document search
            RecordSearchApiService.ts    # BFF API client — entity record search
            auth/
                MsalAuthProvider.ts      # MSAL singleton (same pattern as DocRelViewer)
                msalConfig.ts            # App registration config
        types/
            index.ts                # All type definitions
    index.html                  # Minimal HTML template
    webpack.config.js           # Production webpack (no externals, bundles React 19)
    build-webresource.ps1       # Inline bundle.js into HTML → single self-contained file
    package.json                # Dependencies
    tsconfig.json               # TypeScript config
```

### 5.2 Build & Deployment Pipeline

Two-step build producing a **single self-contained HTML file** (same as DocumentRelationshipViewer):

```
Step 1: npm run build → out/bundle.js
        Webpack bundles ALL code (React 19, Fluent UI, @xyflow/react, d3-force,
        application code) into a single bundle.js

Step 2: build-webresource.ps1 → out/sprk_semanticsearch.html
        Inlines bundle.js + CSS into the HTML template → single file with
        all HTML, CSS, and JavaScript embedded

Deploy: Upload sprk_semanticsearch.html to Dataverse as Webpage (HTML) web resource

⚠️ MANDATORY: The deployable artifact is the .html file in out/, NOT index.html or bundle.js
⚠️ MANDATORY: Both steps must run — deploying bundle.js alone does NOT work
```

### 5.3 Technology Stack

| Component | Technology | Version | Rationale |
|-----------|-----------|---------|-----------|
| UI Framework | React | **19** | Matches DocumentRelationshipViewer; bundled (ADR-006, ADR-022) |
| Component Library | Fluent UI v9 | 9.54+ | Design system (ADR-021) |
| Graph Visualization | @xyflow/react | 12.x | Proven in DocumentRelationshipViewer |
| Graph Layout | d3-force | 3.x | Physics-based node positioning with clustering |
| Data Grid | Universal DatasetGrid | Shared lib | Consistent with platform grids |
| Authentication | @azure/msal-browser | 3.x | Existing pattern |
| Build | webpack | 5.x | Existing pattern |
| Language | TypeScript | 5.4+ | Existing pattern |

### 5.4 Backend Integration

The code page requires **two search endpoints** — one for document chunks (existing, enhanced) and one for entity records (new):

#### Existing Endpoint — Enhanced

| Feature | Endpoint | Method | Status |
|---------|----------|--------|--------|
| Document Search | `/api/ai/search` | POST | Enhance: enable `scope=all`, add `entityTypes` filter |
| Result Count | `/api/ai/search/count` | POST | Enhance: support `scope=all` |
| Open Links | `/api/documents/{id}/open-links` | GET | Existing — no changes |
| Preview | `/api/documents/{id}/preview-url` | GET | Existing — no changes |
| Reindex | `/api/documents/{id}/analyze` | POST | Existing — no changes |

#### New Endpoint — Entity Records Search

| Feature | Endpoint | Method | Status |
|---------|----------|--------|--------|
| Record Search | `/api/ai/search/records` | POST | **New** — search Matters, Projects, Invoices by name/description/entities |

See Section 7 for full API contract details.

---

## 6. UI Design

### 6.1 Layout Structure

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

The **domain tabs** are segmented pill buttons above the results area (not in the filter pane). The active tab is highlighted with the Fluent UI v9 accent color. Switching tabs:
- Changes grid columns to match the entity type
- Updates available filters in the left pane (e.g., hides File Type when domain ≠ Documents)
- Re-executes the search against the appropriate index
- Feels like switching views in Outlook/Teams — familiar UX pattern

### 6.2 Search Domain Tabs

Horizontal segmented pill buttons positioned above the results area, always visible. Uses Fluent UI v9 `TabList` with `appearance="subtle"` and pill-shaped indicator.

```typescript
import { TabList, Tab } from "@fluentui/react-components";
import { DocumentRegular, GavelRegular, FolderRegular, ReceiptRegular } from "@fluentui/react-icons";

<TabList
    selectedValue={searchDomain}
    onTabSelect={(_, data) => setSearchDomain(data.value as SearchDomain)}
    appearance="subtle"
    size="medium"
>
    <Tab icon={<DocumentRegular />} value="Documents">Documents</Tab>
    <Tab icon={<GavelRegular />}    value="Matters">Matters</Tab>
    <Tab icon={<FolderRegular />}   value="Projects">Projects</Tab>
    <Tab icon={<ReceiptRegular />}  value="Invoices">Invoices</Tab>
</TabList>
```

**Behavior on tab switch**:
1. Grid columns update to match the selected entity type (see Section 6.3)
2. Available filters in the left pane update (e.g., File Type hidden when domain ≠ Documents)
3. Search re-executes against the appropriate index (knowledge-index for Documents, records-index for others)
4. Saved search selector resets or filters to domain-relevant saved searches
5. Search query text is preserved across tab switches (allows comparing results for same query across entity types)

### 6.3 Left Filter/Search Pane

Adapted from the PCF SemanticSearchControl (v1.0.46) filter panel. **Search domain selection is in the tabs above the results area, not in the filter pane.**

| Filter | Type | Options | Notes |
|--------|------|---------|-------|
| Search Input | Text input | Free text, Enter triggers search | Empty query returns all (matches PCF) |
| Document Type | Multi-select dropdown | Fetched from Dataverse | FilterDropdown component |
| File Type | Multi-select dropdown | Fetched from Dataverse | FilterDropdown component; hidden when domain ≠ Documents |
| Matter Type | Multi-select dropdown | Fetched from Dataverse | **New** — not in matter-bound PCF |
| Date Range | From/To date inputs | Quick presets dropdown | DateRangeFilter component |
| Threshold | Single-select dropdown | Off, 25%, 50%, 75%, 100% | **Dropdown** (matches PCF v1.0.46) |
| Mode | Single-select dropdown | Hybrid, Concept Only, Keyword Only | **Dropdown** (matches PCF v1.0.46) |
| Search Button | Primary button | Triggers search | Bottom of pane |

**Domain → Filter Visibility Rules**:

| Domain | Visible Filters |
|--------|----------------|
| Documents | All filters (Document Type, File Type, Matter Type, Date Range, Threshold, Mode) |
| Matters | Matter Type, Date Range, Threshold, Mode |
| Projects | Date Range, Threshold, Mode |
| Invoices | Date Range, Threshold, Mode |

The pane is collapsible via a `<` button (same as PCF).

### 6.4 Grid View (Universal DatasetGrid)

The grid view uses the Universal DatasetGrid shared component. **Columns change based on the selected search domain.**

**Documents Grid Columns**:

| Column | Width | Content | Sortable |
|--------|-------|---------|----------|
| (checkbox) | 32px | Row selection | No |
| Document | flex | File icon + document name | Yes |
| Similarity | 80px | Percentage (color-coded) | Yes |
| Type | 100px | Document type (Contract, Invoice, etc.) | Yes |
| File Type | 80px | pdf, docx, xlsx, etc. | Yes |
| Parent Entity | 150px | Matter/Project name | Yes |
| Modified | 120px | Date formatted as "MMM DD, YYYY" | Yes |

**Matters Grid Columns**:

| Column | Width | Content | Sortable |
|--------|-------|---------|----------|
| (checkbox) | 32px | Row selection | No |
| Matter Name | flex | Matter name | Yes |
| Similarity | 80px | Confidence score | Yes |
| Matter Number | 120px | Reference number | Yes |
| Matter Type | 120px | Type classification | Yes |
| Practice Area | 120px | Practice area | Yes |
| Organizations | 150px | Related organizations | No |
| Modified | 120px | Date | Yes |

**Projects Grid Columns**:

| Column | Width | Content | Sortable |
|--------|-------|---------|----------|
| (checkbox) | 32px | Row selection | No |
| Project Name | flex | Project name | Yes |
| Similarity | 80px | Confidence score | Yes |
| Status | 100px | Active, Closed, etc. | Yes |
| Parent Matter | 150px | Associated matter | Yes |
| Modified | 120px | Date | Yes |

**Invoices Grid Columns**:

| Column | Width | Content | Sortable |
|--------|-------|---------|----------|
| (checkbox) | 32px | Row selection | No |
| Invoice | flex | Invoice number + vendor name | Yes |
| Similarity | 80px | Confidence score | Yes |
| Amount | 100px | Currency formatted | Yes |
| Vendor | 150px | Vendor organization | Yes |
| Parent Matter | 150px | Associated matter | Yes |
| Date | 120px | Invoice date | Yes |

**Grid Features** (from Universal DatasetGrid):
- Column sorting
- Column filtering
- Row selection (single + multi via checkbox)
- Infinite scroll pagination
- 44px row height (platform standard)
- Dark mode via Fluent UI v9 tokens

### 6.5 Graph View — Metadata Clustering

The graph view extends the DocumentRelationshipViewer's visualization with **metadata-driven clustering**. Instead of showing individual document nodes in a flat force layout, results are organized into meaningful clusters.

#### Cluster Categories

Users can switch between clustering modes via a toolbar dropdown:

| Cluster By | Nodes Represent | Node Size | Description |
|------------|----------------|-----------|-------------|
| **Matter Type** | Litigation, Transactional, Advisory, etc. | Proportional to record count | Groups results by the type of matter they relate to |
| **Practice Area** | Corporate, IP, Real Estate, etc. | Proportional to record count | Groups by legal practice area |
| **Document Type** | Contract, Invoice, Memo, Pleading, etc. | Proportional to record count | Groups by document classification |
| **Organization** | Client/vendor organization names | Proportional to record count | Groups by related organizations |
| **Person/Contact** | Contact names (attorneys, parties) | Proportional to record count | Groups by related people |

#### Cluster Node Design

```
┌─────────────────────────────────┐
│  🏢  Litigation                 │  ← Category icon + label
│  ─────────────────────          │
│  42 results                     │  ← Count (determines node size)
│  ████████████░░░ 78% avg sim    │  ← Average similarity bar
│                                 │
│  Top matches:                   │
│  • Amendment to Service Agmt    │  ← Top 3 results preview
│  • MSA Redline v3               │
│  • Engagement Letter - Acme     │
└─────────────────────────────────┘
```

#### Cluster Layout

- **d3-force** with custom cluster gravity — nodes in the same cluster attract, different clusters repel
- **Cluster-to-cluster edges** when results in two clusters share relationships (semantic similarity, same matter, etc.)
- **Edge thickness** proportional to the number of cross-cluster relationships
- **Click cluster node** → expand to show individual records within that cluster (drill-down)
- **Expanded cluster** → individual record nodes in a sub-layout, connected by similarity edges
- **Zoom controls, minimap** (from DocumentRelationshipViewer)
- **Color-coded** by cluster category value (auto-assigned from Fluent UI v9 palette, dark-mode safe)

#### Individual Record Nodes (Drill-Down)

When a cluster is expanded, individual record nodes appear (same design as DocumentRelationshipViewer's DocumentNode):

```
┌──────────────────────┐
│ 📄 Contract.pdf      │  ← File icon + name
│ 87% similarity       │  ← Score badge
│ Acme Corp v2         │  ← Parent entity
└──────────────────────┘
```

Edges between record nodes show:
- Similarity score (label on edge)
- Relationship type (color: semantic=blue, same_matter=green, same_org=orange)

### 6.6 Command Bar (Selection-Aware)

The command bar changes based on the current selection state. **No "+New Document" command** — this page is focused on search, not document creation.

**No selection (list context)**:

| Command | Icon | Action |
|---------|------|--------|
| Delete | Delete | Disabled (no selection) |
| Refresh | ArrowSync | Re-execute current search |
| Email a Link | Mail | Disabled (no selection) |

**Multiple records selected**:

| Command | Icon | Action |
|---------|------|--------|
| Delete | Delete | Delete selected records (confirm dialog) |
| Email a Link | Mail | Compose email with links to selected records |
| Send to Index | BrainCircuit | Queue selected for AI reindexing (Documents only) |

**Single record selected**:

| Command | Icon | Action |
|---------|------|--------|
| Delete | Delete | Delete record (confirm dialog) |
| Email a Link | Mail | Compose email with link |
| Send to Index | BrainCircuit | Queue for AI reindexing (Documents only) |
| Open in Web | Globe | Open document in browser (Documents only) |
| Open in Desktop | DesktopArrowRight | Open in desktop app (Documents only) |
| Download | ArrowDownload | Download file (Documents only) |

**Note**: "Open in Web", "Open in Desktop", "Download", and "Send to Index" are only available when the selected record is a Document. For Matters/Projects/Invoices, these actions are hidden.

### 6.7 Saved Searches (Favorites)

Saved searches capture **two independent aspects**:

#### Aspect 1: Filter Strategy (Query + Filters + Domain)

The core search configuration:

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
  }
}
```

#### Aspect 2: Field Selection (Domain-Specific Columns)

Which columns are visible, their order, and sort settings — these depend on the search domain:

```json
{
  "viewMode": "grid",
  "columns": {
    "Documents": {
      "visible": ["name", "similarity", "documentType", "fileType", "parentEntity", "modified"],
      "sortColumn": "similarity",
      "sortDirection": "desc"
    },
    "Matters": {
      "visible": ["matterName", "similarity", "matterNumber", "matterType", "practiceArea"],
      "sortColumn": "similarity",
      "sortDirection": "desc"
    }
  },
  "graphClusterBy": "matterType"
}
```

#### Combined Saved Search Schema

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

#### UI Pattern

Implements the Dataverse saved views pattern (matching the Events page ViewSelector):

```
[My Saved Searches v]
```

**Dropdown contents**:
- **Default Searches** (system-provided, `savedquery` or `sprk_gridconfiguration`):
  - "All Documents" (empty query, domain=Documents, no filters)
  - "All Matters" (empty query, domain=Matters, no filters)
  - "Recent Documents" (last 30 days, domain=Documents)
  - "High Similarity" (threshold 75%+, domain=Documents)
- **My Searches** (user-created, `userquery`):
  - User-saved search configurations with custom names

**Save mechanism**:
- "Save Current Search" button/menu item in the dropdown
- Prompts for a name
- Stores the combined schema (filter strategy + field selection + domain) as a `userquery` record
- Restoring a saved search sets: domain, query, all filters, view mode, column selection, sort order, and graph cluster mode

### 6.8 Entity Record Dialog

When a user opens a record from search results, display the appropriate entity record in a Dataverse dialog:

```typescript
// Open the correct entity form based on search domain
Xrm.Navigation.navigateTo(
    {
        pageType: "entityrecord",
        entityName: entityLogicalName,  // "sprk_document", "sprk_matter", "sprk_project", "sprk_invoice"
        entityId: recordId,
    },
    {
        target: 2, // Dialog
        width: { value: 70, unit: "%" },
        height: { value: 80, unit: "%" },
    }
);
```

Entity name mapping:

| Search Domain | Entity Logical Name |
|--------------|---------------------|
| Documents | `sprk_document` |
| Matters | `sprk_matter` |
| Projects | `sprk_project` |
| Invoices | `sprk_invoice` |

On dialog close, the search results remain intact.

---

## 7. Backend API

### 7.1 Existing Infrastructure

The BFF API and AI Search already have the infrastructure to support multi-entity search:

| Index | Purpose | Entity Types | Search Mode |
|-------|---------|--------------|-------------|
| `knowledge-index` | Document chunks with vector embeddings | Documents (tracks `parentEntityType`: matter, project, invoice, account, contact) | Hybrid (vector + keyword) |
| `spaarke-records-index` | Entity record metadata with vector embeddings | Matters, Projects, Invoices (via `recordType` filter) | Hybrid (vector + keyword) |
| `spaarke-invoices` | Invoice-specific structured data | Invoices | Hybrid (vector + keyword) |

**Key insight**: The `knowledge-index` already tracks `parentEntityType`, `parentEntityId`, and `parentEntityName` for every document chunk. The `spaarke-records-index` already has 3072-dimension vector embeddings for Matters, Projects, and Invoices. **No index schema changes are needed.**

### 7.2 Existing Endpoint — Enhanced (Document Search)

**`POST /api/ai/search`**

Changes needed:
- Enable `scope=all` (currently returns `SCOPE_NOT_SUPPORTED`)
- Support `entityTypes` filter to search documents across specific parent entity types

**Enhanced Request**:
```json
{
  "query": "contract amendments (max 1000 chars)",
  "scope": "all",
  "entityTypes": ["matter", "project"],
  "filters": {
    "documentTypes": ["Contract", "Invoice"],
    "fileTypes": ["pdf", "docx"],
    "tags": [],
    "dateRange": { "field": "createdAt", "from": "ISO date", "to": "ISO date" }
  },
  "options": {
    "limit": 20,
    "offset": 0,
    "includeHighlights": true,
    "hybridMode": "rrf | vectorOnly | keywordOnly"
  }
}
```

**Response** includes per result:
- `documentId`, `name`, `fileType`, `documentType`
- `combinedScore` (0.0-1.0), `highlights`
- `parentEntityType`, `parentEntityId`, `parentEntityName`
- `fileUrl`, `recordUrl`
- `createdAt`, `updatedAt`, `createdBy`
- `summary`, `tldr` (AI-generated)

### 7.3 New Endpoint — Entity Records Search

**`POST /api/ai/search/records`** (New)

Searches the `spaarke-records-index` to find Matters, Projects, and Invoices by name, description, related organizations, related people, and keywords.

**Request**:
```json
{
  "query": "Acme Corporation litigation",
  "recordTypes": ["sprk_matter", "sprk_project", "sprk_invoice"],
  "filters": {
    "organizations": ["Acme Corp"],
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

**Response**:
```json
{
  "results": [
    {
      "recordId": "GUID",
      "recordType": "sprk_matter",
      "recordName": "Acme Corp v. Widget Inc.",
      "recordDescription": "Patent infringement litigation...",
      "confidenceScore": 0.87,
      "matchReasons": ["name_match", "organization_match"],
      "organizations": ["Acme Corporation", "Widget Inc."],
      "people": ["Jane Smith", "John Doe"],
      "keywords": ["patent", "infringement", "licensing"],
      "createdAt": "2025-06-15T...",
      "modifiedAt": "2026-01-20T..."
    }
  ],
  "metadata": {
    "totalCount": 42,
    "searchTime": "120ms",
    "hybridMode": "rrf"
  }
}
```

**Implementation notes**:
- Queries the existing `spaarke-records-index` (already has vector embeddings)
- Leverages existing `RecordMatchService` infrastructure
- Filters by `recordType` field in the index
- Returns confidence scores and match reasons for transparency

### 7.4 API Service Layer

The code page uses two API service clients:

| Service | Endpoint | Use Case |
|---------|----------|----------|
| `SemanticSearchApiService` | `POST /api/ai/search` | Search Documents (enhanced with `scope=all`) |
| `RecordSearchApiService` | `POST /api/ai/search/records` | Search Matters, Projects, Invoices |

The UI's search domain selector determines which service to call:

| Domain | Service Called | Index Used |
|--------|--------------|------------|
| Documents | SemanticSearchApiService | `knowledge-index` |
| Matters | RecordSearchApiService (recordTypes: `["sprk_matter"]`) | `spaarke-records-index` |
| Projects | RecordSearchApiService (recordTypes: `["sprk_project"]`) | `spaarke-records-index` |
| Invoices | RecordSearchApiService (recordTypes: `["sprk_invoice"]`) | `spaarke-records-index` |

### 7.5 Shared Types

Types shared between PCF and Code Page (candidates for extraction to shared library):

```typescript
// Search types
SearchResult, SearchFilters, SearchMode, SearchScope, DateRange, FilterOption

// API request/response
SemanticSearchRequest, SemanticSearchResponse, SemanticSearchMetadata

// New — entity records
RecordSearchRequest, RecordSearchResponse, RecordSearchResult

// Document actions
OpenLinksResponse, PreviewUrlResponse
```

---

## 8. Universal DatasetGrid Integration

### 8.1 Code Page Grid

The search results grid uses the Universal DatasetGrid shared component from `@spaarke/ui-components`:

```typescript
import { UniversalDatasetGrid } from "@spaarke/ui-components";

<UniversalDatasetGrid
    headlessConfig={{
        entityName: searchDomain === "Documents" ? "sprk_document" : `sprk_${searchDomain.toLowerCase()}`,
        pageSize: 20,
        // Data provided by search results, not FetchXML
    }}
    config={{
        viewMode: "Grid",
        enableVirtualization: true,
        rowHeight: 44,
        selectionMode: "Multiple",
        showToolbar: false,       // Command bar is separate
        scrollBehavior: "Infinite",
        columns: domainColumns,   // Domain-specific column definitions
    }}
    selectedRecordIds={selectedIds}
    onSelectionChange={setSelectedIds}
    onRecordClick={handleOpenRecord}
/>
```

**Note**: The Universal DatasetGrid typically fetches data via FetchXML/WebApi. For search results (which come from the BFF API, not Dataverse queries), we'll need a **headless data adapter** that feeds search results into the grid component's data model. This may require extending the grid's `headlessConfig` to accept a custom data source.

### 8.2 DocumentRelationshipViewer Grid Migration

As part of this project, update the existing DocumentRelationshipViewer's `RelationshipGrid.tsx` to use the Universal DatasetGrid component instead of the inline Fluent DataGrid. This ensures visual consistency across:
- SemanticSearch code page (new)
- DocumentRelationshipViewer code page (updated)
- Events page (existing)
- Any future dataset grid instances

---

## 9. Navigation & Entry Points

### 9.1 Sitemap Entry

Add a sitemap entry for the Semantic Search page:

```xml
<SubArea Id="sprk_semanticsearch"
         ResourceId="sitemap_semanticsearch"
         Url="/WebResources/sprk_semanticsearch"
         Icon="$webresource:sprk_searchicon.svg"
         Title="Semantic Search"
         DescriptionResourceId="Search documents using AI" />
```

### 9.2 Command Bar Button (Global)

Add a "Semantic Search" button to the global command bar or relevant entity command bars that opens the code page:

```typescript
Xrm.Navigation.navigateTo(
    {
        pageType: "webresource",
        webresourceName: "sprk_semanticsearch",
        data: `theme=${isDarkMode ? "dark" : "light"}`,
    },
    {
        target: 1, // Full page (inline)
        // Or target: 2 for dialog mode
    }
);
```

### 9.3 URL Parameters

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `theme` | No | OS preference | "light" or "dark" |
| `query` | No | "" | Pre-populate search query |
| `domain` | No | "Documents" | Search domain: "Documents", "Matters", "Projects", "Invoices" |
| `scope` | No | "all" | "all", or entity-scoped |
| `entityId` | No | null | Pre-scope to specific entity |
| `savedSearchId` | No | null | Load a saved search by ID |

---

## 10. Dependencies & Prerequisites

### 10.1 BFF API: scope=all and Entity Records Search

| Prerequisite | Current Status | Action Needed |
|-------------|---------------|---------------|
| `scope=all` for document search | Returns `SCOPE_NOT_SUPPORTED` | Enable in `SemanticSearchService` — no index changes needed |
| `entityTypes` filter | Not implemented | Add filter builder for `parentEntityType` — field exists in index |
| Entity records search endpoint | Not implemented | New `POST /api/ai/search/records` using `spaarke-records-index` |
| `RecordMatchService` infrastructure | Exists | Leverage for new endpoint implementation |

### 10.2 Universal DatasetGrid Headless Data Adapter

The Universal DatasetGrid currently expects data from Dataverse FetchXML queries or a PCF dataset. For search results (from BFF API), a headless data adapter is needed that:
- Accepts an array of search results (documents or entity records)
- Maps them to the grid's expected data format
- Supports sorting, pagination, and selection
- Does NOT require a Dataverse WebApi connection for data fetching

### 10.3 Existing Components (Reusable)

| Component | Source | Reuse Strategy |
|-----------|--------|----------------|
| FilterDropdown | PCF SemanticSearchControl | Copy + adapt (React 16→19 minor) |
| DateRangeFilter | PCF SemanticSearchControl | Copy + adapt |
| ViewSelector | Shared library | Import directly |
| ViewService | Shared library | Import directly |
| UniversalDatasetGrid | Shared library | Import directly |
| MsalAuthProvider | DocumentRelationshipViewer | Copy (same pattern) |
| DocumentNode/Edge | DocumentRelationshipViewer | Extend for multi-entity + clustering |
| useForceLayout | DocumentRelationshipViewer | Extend for cluster gravity |

---

## 11. Non-Functional Requirements

### 11.1 Performance
- Search results render within 500ms of API response
- Graph view handles up to 100 cluster/record nodes smoothly
- Grid view supports infinite scroll up to 1000 results
- Saved searches load within 1 second
- Single-file HTML deployment loads within 3 seconds on first visit

### 11.2 Accessibility
- All controls keyboard-navigable
- ARIA labels on all interactive elements
- Color not used as sole indicator (badges have text + color)
- Fluent UI v9 components provide built-in a11y

### 11.3 Theming
- Full dark mode support via Fluent UI v9 tokens (ADR-021)
- No hard-coded colors
- Theme detection from Dataverse host or URL parameter

### 11.4 Extensibility
- New search domains (entity types) can be added by:
  1. Adding to the domain selector options
  2. Defining domain-specific grid columns
  3. Adding the record type to the `RecordSearchApiService` call
  4. No code page rebuild needed if column config is stored in saved views

---

## 12. Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| `scope=all` not supported in BFF API | Blocks system-wide document search | Prioritize BFF API update; `parentEntityType` field already exists in index |
| Records index not populated for all entities | Missing Matters/Projects/Invoices in search | Verify `spaarke-records-index` coverage; add indexing pipelines if needed |
| Universal DatasetGrid doesn't support headless data | Blocks grid integration | Build a data adapter; fall back to custom Fluent DataGrid if needed |
| Bundle size too large (React 19 + graph + d3) | Slow load time | Tree-shake imports, lazy-load graph view, code-split |
| MSAL token scoping for cross-entity access | Auth errors on broad search | Verify token scope covers all entity types; add error handling |
| Graph clustering performance with large result sets | UI lag with 500+ results | Limit cluster view to top 100 results; paginate within clusters |

---

## 13. Success Criteria

1. User can search across Documents, Matters, Projects, and Invoices from a full-page interface
2. Search domain selector changes available filters and result columns
3. Results display in both Grid (Universal DatasetGrid) and Graph (clustered) views
4. Graph view clusters by Matter Type, Practice Area, Document Type, Organization, Person/Contact
5. All applicable filters work per domain (Document Type, File Type, Matter Type, Date Range, Threshold, Mode)
6. Users can save and recall search configurations (filter strategy + field selection) as favorites
7. Command bar shows context-appropriate actions based on selection and entity type
8. Opening a record shows the appropriate entity record dialog
9. Dark mode works correctly
10. Single self-contained HTML file deploys to Dataverse
11. DocumentRelationshipViewer grid migrated to Universal DatasetGrid

---

## 14. Open Questions

1. **Records index coverage**: Is the `spaarke-records-index` currently populated for all Matters, Projects, and Invoices, or only a subset?
2. **Saved search storage**: Should we use `userquery` entity (standard Dataverse pattern) or `sprk_gridconfiguration` (custom Spaarke table) for saved searches?
3. **Graph drill-down behavior**: When clicking a cluster node to expand, should it open an inline sub-layout or navigate to a filtered view?
4. **Cross-domain search**: Should users be able to search multiple domains simultaneously (e.g., Documents + Matters), or is single-domain selection sufficient?
5. **DocumentRelationshipViewer migration**: Should the grid migration be done in this project or as a separate task?
6. **Sitemap placement**: Under which area group should the Semantic Search page appear?
7. **Future entity types**: Which additional entity types might be added? (Contacts, Organizations, Tasks, etc.)

---

## Appendix A: Existing Component Inventory

### PCF SemanticSearchControl (R2) — v1.0.46
- Location: `src/client/pcf/SemanticSearchControl/`
- React 16 (platform-provided), field-bound to Matter form
- Components: SearchInput, FilterPanel, FilterDropdown, DateRangeFilter, ResultsList, ResultCard
- Services: SemanticSearchApiService, MsalAuthProvider, NavigationService
- Hooks: useSemanticSearch, useFilters, useFilterOptions, useInfiniteScroll

### DocumentRelationshipViewer Code Page — v1.0.3
- Location: `src/client/code-pages/DocumentRelationshipViewer/`
- **React 19**, standalone web resource (single self-contained HTML)
- Components: DocumentGraph, DocumentNode, DocumentEdge, RelationshipGrid, ControlPanel, NodeActionBar
- Services: VisualizationApiService, MsalAuthProvider
- Hooks: useVisualizationApi, useForceLayout

### Universal DatasetGrid — v2.0.7
- Location: `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/`
- Framework-agnostic shared library
- Views: GridView, CardView, ListView, VirtualizedGridView, VirtualizedListView
- Services: ViewService, CommandRegistry, ColumnRendererService, PrivilegeService
- Features: Column filtering, sorting, selection, infinite scroll, dark mode

### ViewSelector — Shared Library
- Location: `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/ViewSelector.tsx`
- Fetches views from savedquery, userquery, sprk_gridconfiguration
- Used in Events page with hardcoded view GUIDs
- Supports personal views (userquery) and custom configs

## Appendix B: AI Search Index Fields

### knowledge-index (Document Chunks)
- `documentId`, `chunkId`, `content`, `contentVector` (3072 dims)
- `parentEntityType` (filterable: matter, project, invoice, account, contact)
- `parentEntityId`, `parentEntityName`
- `documentType`, `fileType`, `tags`, `createdAt`, `updatedAt`

### spaarke-records-index (Entity Records)
- `recordId`, `recordType` (filterable: sprk_matter, sprk_project, sprk_invoice)
- `recordName`, `recordDescription`
- `organizations` (collection), `people` (collection), `keywords`
- `contentVector` (3072 dims)
- `referenceNumbers`, `createdAt`, `modifiedAt`
