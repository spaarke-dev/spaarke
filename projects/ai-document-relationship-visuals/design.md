# AI Document Relationship Visualization - Design Document

> **Project**: AI Document Relationship Visuals
> **Version**: 1.0
> **Date**: January 20, 2026
> **Author**: Product Team
> **Status**: Draft - Pending Review

---

## Executive Summary

This document outlines the design for enhancing the Document Relationship Visualization capabilities within the Spaarke Document Access Platform. The enhancement introduces two new display modes for viewing document relationships discovered by the RAG (Retrieval-Augmented Generation) pipeline:

1. **List View** - A tabular view with sortable columns and Excel/CSV export capability
2. **Card View** - A compact count display for dashboards that opens a full viewer dialog on click

These enhancements address user feedback requesting more flexible ways to view and export related document data beyond the current graph visualization.

---

## Problem Statement

### Current State

The **DocumentRelationshipViewer** PCF control (v1.0.29) currently provides:
- Interactive force-directed graph visualization of related documents
- Relationship type filtering (semantic, same_matter, same_email, etc.)
- Node selection with tooltip details
- Support for parent hub nodes (Matter, Project, Invoice)

### Pain Points

| Issue | User Impact |
|-------|-------------|
| **Graph-only view** | Users cannot easily scan/compare many documents in tabular format |
| **No export capability** | Users cannot extract relationship data to Excel for reporting |
| **Full-size only** | Cannot embed a compact summary on dashboards or form headers |
| **No quick count** | Users must open the full graph just to see how many related documents exist |

### User Feedback

From the Document Profile form (screenshot reference):
- The "RELATED DOCUMENTS" section in the sidebar shows a count ("8") - users want this same pattern available
- Users want to export document relationship lists to Excel for compliance reporting
- Dashboard designers need a small card that shows count with drill-through capability

---

## Proposed Solution

### Two-Control Architecture

Implement two separate PCF controls optimized for different use cases:

| Control | Purpose | Typical Deployment |
|---------|---------|-------------------|
| **DocumentRelationshipViewer** (enhanced) | Full-featured viewer with Graph and List modes | Main form sections, Custom Pages, full-screen dialogs |
| **DocumentRelationshipCard** (new) | Compact count card with dialog drill-through | Dashboards, form headers, sidebar sections |

### Rationale for Separate Controls

1. **Different sizing requirements** - Card is fixed small (180-280px), Viewer fills available space
2. **Different interaction patterns** - Card clicks to open dialog, Viewer is the interactive destination
3. **Independent deployment** - Form designers can choose which control fits their layout
4. **Reduced bundle size** - Card doesn't need React Flow graph library

---

## Requirements

### Functional Requirements

#### FR-1: List View (DocumentRelationshipViewer Enhancement)

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-1.1 | View toggle button to switch between Graph and List views | Must Have |
| FR-1.2 | List view displays related documents in a sortable DataGrid | Must Have |
| FR-1.3 | List columns: Document Name, Type, Similarity %, Relationship Type, Created Date | Must Have |
| FR-1.4 | All columns are sortable (ascending/descending) | Must Have |
| FR-1.5 | Export to CSV button downloads filtered document list | Must Have |
| FR-1.6 | Relationship type filter applies to both Graph and List views | Must Have |
| FR-1.7 | Row click in List view triggers same selection behavior as node click in Graph | Should Have |
| FR-1.8 | Default view (Graph or List) is configurable via control property | Should Have |
| FR-1.9 | View toggle can be hidden via control property (lock to single mode) | Could Have |
| FR-1.10 | **Similarity threshold slider** to filter results by minimum similarity score | Should Have |

#### FR-2: Card View (New DocumentRelationshipCard Control)

| ID | Requirement | Priority |
|----|-------------|----------|
| FR-2.1 | Display "RELATED DOCUMENTS" title (configurable) | Must Have |
| FR-2.2 | Display count of related documents as large number | Must Have |
| FR-2.3 | Click anywhere on card opens Custom Page as dialog (95% x 95%) | Must Have |
| FR-2.4 | Custom Page name is configurable via control property | Must Have |
| FR-2.5 | Display "Last updated" timestamp | Should Have |
| FR-2.6 | Small open/expand icon indicates clickability | Should Have |
| FR-2.7 | Loading spinner while fetching count | Should Have |
| FR-2.8 | Error state display if API fails | Should Have |

### Non-Functional Requirements

| ID | Requirement | Priority |
|----|-------------|----------|
| NFR-1 | Both controls must support dark mode (Fluent UI v9 design tokens) | Must Have |
| NFR-2 | Both controls must use React 16 APIs (platform library compatibility) | Must Have |
| NFR-3 | Card control must render within 500ms | Should Have |
| NFR-4 | List view must handle 100+ documents without performance degradation | Should Have |
| NFR-5 | CSV export must support 500+ rows | Should Have |
| NFR-6 | Both controls deployed as unmanaged solutions | Must Have |

---

## Technical Approach

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                     DOCUMENT RELATIONSHIP VISUALIZATION                      │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                    BFF API (Existing)                                │   │
│  │  GET /api/ai/visualization/related/{documentId}                      │   │
│  │  → Returns: nodes[], edges[], metadata { totalResults, latencyMs }   │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                              ▲                                              │
│                              │ HTTPS + Bearer Token                         │
│                              │                                              │
│  ┌───────────────────────────┴───────────────────────────────────────────┐ │
│  │                         PCF Controls                                   │ │
│  │                                                                        │ │
│  │  ┌──────────────────────────────┐  ┌──────────────────────────────┐  │ │
│  │  │ DocumentRelationshipViewer   │  │ DocumentRelationshipCard     │  │ │
│  │  │ v1.0.30                      │  │ v1.0.0                       │  │ │
│  │  │                              │  │                              │  │ │
│  │  │ • Graph View (existing)      │  │ • Count display              │  │ │
│  │  │ • List View (NEW)            │  │ • Click → Custom Page dialog │  │ │
│  │  │ • CSV Export (NEW)           │  │ • Lightweight (~50KB)        │  │ │
│  │  │ • View Toggle (NEW)          │  │                              │  │ │
│  │  │ • ~300KB bundle              │  │                              │  │ │
│  │  └──────────────────────────────┘  └──────────────────────────────┘  │ │
│  │                                                                        │ │
│  │                     ↓ Click on Card                                   │ │
│  │  ┌─────────────────────────────────────────────────────────────────┐  │ │
│  │  │ Custom Page (Canvas App)                                        │  │ │
│  │  │ sprk_documentrelationshipviewer_xxxxx                           │  │ │
│  │  │                                                                 │  │ │
│  │  │ Contains: DocumentRelationshipViewer (full control)             │  │ │
│  │  │ Opened as: Dialog (95% width/height)                            │  │ │
│  │  └─────────────────────────────────────────────────────────────────┘  │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Component Design

#### DocumentRelationshipViewer v1.0.30

**New Components:**

| Component | Purpose | Key Implementation Details |
|-----------|---------|---------------------------|
| `ViewToggle.tsx` | Toggle between Graph/List | Fluent UI v9 ToggleButton group |
| `DocumentList.tsx` | Sortable data table | Fluent UI v9 DataGrid with sortable columns |
| `SimilaritySlider.tsx` | Adjust minimum similarity threshold | Fluent UI v9 Slider, triggers API re-fetch |
| `CsvExportService.ts` | Export to CSV | Blob + anchor download pattern |

**Note**: The similarity slider uses the existing `threshold` API parameter. When the user adjusts the slider, the control re-fetches data with the new threshold value.

**Modified Components:**

| Component | Changes |
|-----------|---------|
| `DocumentRelationshipViewer.tsx` | Add view state, conditional rendering, toggle in header |
| `ControlManifest.Input.xml` | Add displayMode, defaultView, showViewToggle properties |
| `index.ts` | Pass new properties to React component |

**New Control Properties:**

```xml
<!-- Display mode for initial load -->
<property name="displayMode" of-type="Enum" usage="input" default-value="graph">
  <value name="graph" display-name-key="Graph View" />
  <value name="list" display-name-key="List View" />
</property>

<!-- Default view when toggle is shown -->
<property name="defaultView" of-type="Enum" usage="input" default-value="graph">
  <value name="graph" display-name-key="Graph View" />
  <value name="list" display-name-key="List View" />
</property>

<!-- Whether to show the view toggle -->
<property name="showViewToggle" of-type="TwoOptions" usage="input" default-value="true" />
```

#### DocumentRelationshipCard v1.0.0 (New)

**Project Structure:**

```
src/client/pcf/DocumentRelationshipCard/
├── DocumentRelationshipCard.pcfproj
├── DocumentRelationshipCard/
│   ├── ControlManifest.Input.xml
│   ├── index.ts                        # PCF entry point
│   ├── DocumentRelationshipCard.tsx    # Main component
│   ├── components/
│   │   └── CardContent.tsx             # Card UI
│   ├── services/
│   │   ├── VisualizationApiService.ts  # (copy from viewer)
│   │   ├── MsalAuthProvider.ts         # (copy from viewer)
│   │   └── CustomPageNavigator.ts      # Xrm.Navigation wrapper
│   ├── hooks/
│   │   └── useRelationshipCount.ts     # Lightweight count fetch
│   └── types/
│       └── index.ts
├── package.json
├── tsconfig.json
└── featureconfig.json
```

**Control Properties:**

```xml
<property name="documentId" of-type="SingleLine.Text" usage="bound" required="true" />
<property name="tenantId" of-type="SingleLine.Text" usage="input" />
<property name="apiBaseUrl" of-type="SingleLine.Text" usage="input" />
<property name="customPageName" of-type="SingleLine.Text" usage="input" required="true" />
<property name="cardTitle" of-type="SingleLine.Text" usage="input" default-value="RELATED DOCUMENTS" />
```

**Custom Page Navigation:**

```typescript
// Uses Dataverse Xrm.Navigation API
Xrm.Navigation.navigateTo(
    {
        pageType: "custom",
        name: customPageName,  // e.g., "sprk_documentrelationshipviewer_c0199"
        recordId: documentId
    },
    {
        target: 2,      // Dialog
        position: 1,    // Center
        width: { value: 95, unit: "%" },
        height: { value: 95, unit: "%" }
    }
);
```

### Data Flow

#### List View Data Flow

```
User clicks "List" toggle
    │
    ▼
DocumentRelationshipViewer.tsx
    │ currentView = "list"
    │
    ▼
<DocumentList nodes={nodes} onNodeSelect={handleSelect} />
    │
    ├─► DataGrid renders rows from nodes[]
    │     • Filters out source node (isSource=true)
    │     • Applies relationshipType filter
    │
    └─► User clicks "Export CSV"
          │
          ▼
        CsvExportService.exportNodes(filteredNodes, "related-documents.csv")
          │
          ▼
        Browser downloads CSV file
```

#### Card View Data Flow

```
DocumentRelationshipCard mounts
    │
    ▼
useRelationshipCount(documentId, tenantId)
    │ Fetches: GET /api/ai/visualization/related/{documentId}?limit=1
    │ Extracts: metadata.totalResults
    │
    ▼
CardContent displays count
    │
    ├─► isLoading: Show Spinner
    ├─► error: Show error message
    └─► success: Show count number
          │
          ▼
        User clicks card
          │
          ▼
        CustomPageNavigator.openCustomPage(customPageName, documentId)
          │
          ▼
        Custom Page dialog opens with DocumentRelationshipViewer
```

---

## User Interface Design

### List View Layout

```
┌─────────────────────────────────────────────────────────────────────────┐
│ RELATED DOCUMENTS                                                        │
│                                                                          │
│ ┌────────────────────────────────────────────────────────────────────┐  │
│ │ Header                                                              │  │
│ │  [Icon] Document Relationships    [Graph | List*]  [All types ▼]   │  │
│ └────────────────────────────────────────────────────────────────────┘  │
│                                                                          │
│ ┌────────────────────────────────────────────────────────────────────┐  │
│ │ Toolbar                                                             │  │
│ │  Similarity: [50%|════════●══════|100%]              [📥 Export]   │  │
│ └────────────────────────────────────────────────────────────────────┘  │
│                                                                          │
│ ┌────────────────────────────────────────────────────────────────────┐  │
│ │ DataGrid                                                            │  │
│ │ ┌─────────────────┬──────────┬───────────┬─────────────┬─────────┐ │  │
│ │ │ Document Name ▼ │ Type     │ Similarity│ Relationship│ Created │ │  │
│ │ ├─────────────────┼──────────┼───────────┼─────────────┼─────────┤ │  │
│ │ │ Contract_v2.pdf │ Contract │ [███ 92%] │ Same matter │ Jan 15  │ │  │
│ │ │ Invoice_Q4.pdf  │ Invoice  │ [██░ 78%] │ Same matter │ Jan 12  │ │  │
│ │ │ Email_Thread.msg│ Email    │ [██░ 71%] │ Same thread │ Jan 10  │ │  │
│ │ │ Report_2024.docx│ Report   │ [█░░ 65%] │ Semantic    │ Jan 08  │ │  │
│ │ └─────────────────┴──────────┴───────────┴─────────────┴─────────┘ │  │
│ └────────────────────────────────────────────────────────────────────┘  │
│                                                                          │
│ ┌────────────────────────────────────────────────────────────────────┐  │
│ │ Footer: 12 related documents · v1.0.30 · Built 2026-01-20          │  │
│ └────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
```

### Card View Layout

```
┌────────────────────────────────────┐
│ ┌──────────────────────────────┐   │
│ │ [📊] RELATED DOCUMENTS   [↗] │   │  ← Title + Open icon
│ │                              │   │
│ │            25                │   │  ← Large count number
│ │                              │   │
│ │     Last updated: 5min ago   │   │  ← Timestamp
│ └──────────────────────────────┘   │
└────────────────────────────────────┘
         │
         │ Click
         ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ Custom Page Dialog                                                    X │
│ ─────────────────────────────────────────────────────────────────────── │
│ │                                                                     │ │
│ │   [Full DocumentRelationshipViewer with Graph/List toggle]          │ │
│ │                                                                     │ │
│ │   • All features available                                          │ │
│ │   • Filter by relationship type                                     │ │
│ │   • Export to CSV                                                   │ │
│ │   • Node selection                                                  │ │
│ │                                                                     │ │
│ └─────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────┘
```

### CSV Export Format

```csv
Document Name,Document Type,Similarity %,Relationship Type,Created Date,File Type,Document ID
"Contract_v2.pdf","Contract",92,"Same matter","2026-01-15","pdf","abc123..."
"Invoice_Q4.pdf","Invoice",78,"Same matter","2026-01-12","pdf","def456..."
"Email_Thread.msg","Email",71,"Same thread","2026-01-10","msg","ghi789..."
```

---

## Applicable ADRs and Constraints

### Architecture Decision Records

| ADR | Title | Relevance |
|-----|-------|-----------|
| ADR-006 | PCF over Web Resources | Both controls must be PCF, not legacy webresources |
| ADR-021 | Fluent UI v9 Design System | All UI uses Fluent v9 components and design tokens |
| ADR-022 | PCF Platform Libraries | React 16 APIs only, unmanaged solutions, platform-library declarations |
| ADR-012 | Shared Component Library | Reuse formatters from @spaarke/ui-components |

### Key Constraints

| Constraint | Source | Impact |
|------------|--------|--------|
| React 16 compatibility | ADR-022 | Must use ReactDOM.render(), not createRoot() |
| Fluent UI v9 only | ADR-021 | No Fluent v8 components; use design tokens for colors |
| Dark mode required | ADR-021 | All styling via tokens, respect fluentDesignLanguage |
| Unmanaged solutions | ADR-022 | Deploy as unmanaged, not managed solutions |
| Spaarke publisher | Conventions | All controls use sprk_ prefix, Spaarke publisher |

---

## Dependencies

### Technical Dependencies

| Dependency | Version | Used By |
|------------|---------|---------|
| react | 16.14.0 | Both controls (platform library) |
| @fluentui/react-components | 9.46.2 | Both controls (platform library) |
| react-flow-renderer | 10.x | DocumentRelationshipViewer only |
| d3-force | 3.x | DocumentRelationshipViewer only |

### External Dependencies

| Dependency | Description |
|------------|-------------|
| BFF API | `/api/ai/visualization/related/{documentId}` endpoint |
| MSAL Authentication | Token acquisition for API calls |
| Custom Page | Must be created in Dataverse for Card dialog |

### Deployment Order

1. **DocumentRelationshipViewer v1.0.30** - Enhance existing control
2. **Create Custom Page** - Canvas App with embedded Viewer control
3. **DocumentRelationshipCard v1.0.0** - Deploy card pointing to Custom Page

---

## Success Criteria

### Acceptance Criteria

| ID | Criterion | Verification |
|----|-----------|--------------|
| AC-1 | User can toggle between Graph and List views | Manual test: click toggle |
| AC-2 | List view displays all related documents with correct data | Manual test: compare to API response |
| AC-3 | All columns in List view are sortable | Manual test: click column headers |
| AC-4 | CSV export downloads file with all visible documents | Manual test: export, open in Excel |
| AC-5 | Card displays correct count | Manual test: compare to full viewer count |
| AC-6 | Clicking card opens Custom Page dialog | Manual test: click card |
| AC-7 | Both controls render correctly in dark mode | Manual test: toggle dark mode |
| AC-8 | Both controls deployed to Dataverse successfully | PAC CLI: pac solution list |

### Performance Criteria

| Metric | Target | Measurement |
|--------|--------|-------------|
| Card initial render | < 500ms | Browser DevTools |
| List view render (100 docs) | < 1000ms | Browser DevTools |
| CSV export (500 docs) | < 2000ms | Manual timing |
| Bundle size (Card) | < 100KB | npm run build analysis |
| Bundle size (Viewer) | < 400KB | npm run build analysis |

---

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Custom Page not created before Card deployment | Medium | Card won't function | Document dependency clearly; create Custom Page first |
| DataGrid performance with large datasets | Low | Slow scrolling | Use virtualization if >100 rows |
| Fluent v9 DataGrid React 16 compatibility | Low | Build errors | DataGrid supports React 16/17/18 |
| CSV export blocked by browser | Low | Export fails | Use standard blob/anchor pattern |

---

## Out of Scope

The following items are explicitly out of scope for this project:

- **Inline editing** in List view
- **Multi-select export** (export only filtered view, not selected rows)
- **Graph view enhancements** (focus on List and Card views)
- **Real-time updates** (data fetched on load only)
- **Mobile-optimized layouts** (responsive but desktop-first)
- **Semantic search within results** (deferred to ai-semantic-search-foundation-r1)

---

## Future Enhancement: Semantic Search Integration

### Related Project Dependency

**Project**: `ai-semantic-search-foundation-r1`

Once the Semantic Search Foundation is complete, the DocumentRelationshipViewer can add a search box that performs semantic search **within** the related documents subset.

### Integration Pattern

```
User enters search query in Viewer
    │
    ▼
POST /api/ai/search/semantic
{
  "query": "payment terms",
  "scope": "documentIds",
  "documentIds": [IDs of related documents from graph]
}
    │
    ▼
Results filtered/ranked by search relevance
    │
    ▼
Graph/List shows only matching documents
```

### UI Enhancement (Phase 3)

| Component | Description |
|-----------|-------------|
| `SearchInput.tsx` | Text input with search icon in Viewer header |
| Search mode toggle | Switch between "Filter by name" and "Semantic search" |
| Search results highlighting | Show why each result matched |

### Prerequisite

This enhancement requires:
1. `ai-semantic-search-foundation-r1` API deployed
2. `POST /api/ai/search/semantic` endpoint with `scope: "documentIds"` support

**Note**: Client-side name filtering is included in Phase 1 as an interim solution. Semantic search will replace/augment this when the Foundation project is complete.

---

## Related Projects

| Project | Relationship | Status |
|---------|--------------|--------|
| `ai-semantic-search-foundation-r1` | Provides API for future semantic search in graph | Planned |
| `ai-semantic-search-ui-r2` | Standalone search control (separate from this project) | Planned |
| Future: Agentic RAG | Full Playbook scope integration for search | Future |

---

## Glossary

| Term | Definition |
|------|------------|
| **PCF** | Power Apps Component Framework - custom control technology |
| **RAG** | Retrieval-Augmented Generation - AI pipeline that discovers document relationships |
| **Custom Page** | Canvas App embedded in model-driven app, openable as dialog |
| **BFF API** | Backend-for-Frontend API serving visualization data |
| **Design Token** | Fluent UI v9 semantic color/spacing variable (e.g., tokens.colorBrandBackground) |

---

## Appendix

### A. API Contract Reference

**Endpoint**: `GET /api/ai/visualization/related/{documentId}`

**Query Parameters**:
| Parameter | Type | Description |
|-----------|------|-------------|
| `tenantId` | string | Required. Tenant identifier for API routing |
| `threshold` | number | Optional. Minimum similarity score (0.0-1.0) |
| `limit` | number | Optional. Maximum results to return |
| `countOnly` | boolean | **NEW**. If true, skip graph building, return only metadata |

**Response Schema**:
```typescript
interface DocumentGraphResponse {
    nodes: ApiDocumentNode[];      // Empty array if countOnly=true
    edges: ApiDocumentEdge[];      // Empty array if countOnly=true
    metadata: {
        sourceDocumentId: string;
        tenantId: string;
        totalResults: number;      // ← Used by Card for count
        threshold: number;
        searchLatencyMs: number;   // ← Used for "last updated" timing
        cacheHit: boolean;
    };
}
```

**API Enhancement Required (Phase 1)**:
- Add `countOnly` query parameter to existing endpoint
- When `countOnly=true`, skip graph topology calculation
- Return only `metadata` with empty `nodes[]` and `edges[]`
- Reduces latency for Card control count fetch

### B. Existing Control Reference

**Current DocumentRelationshipViewer** (v1.0.29):
- Location: `src/client/pcf/DocumentRelationshipViewer/`
- Solution: `SpaarkeDocumentRelationshipViewer`
- Namespace: `Spaarke.Controls`
- Features: Graph view, relationship filter, node tooltips, MSAL auth

### C. Related Documentation

- [PCF Deployment Guide](../../docs/guides/PCF-DEPLOYMENT-GUIDE.md)
- [Spaarke AI Architecture](../../docs/guides/SPAARKE-AI-ARCHITECTURE.md)
- [RAG Architecture](../../docs/guides/RAG-ARCHITECTURE.md)

---

*Document Version History*

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-01-20 | Product Team | Initial draft |
