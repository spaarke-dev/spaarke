# AI Semantic Search UI - Design Document

> **Project**: ai-semantic-search-ui-r2
> **Version**: 1.0
> **Created**: January 2026
> **Status**: Draft
> **Depends On**: ai-semantic-search-foundation-r1

---

## Executive Summary

This project delivers a **PCF control for semantic search** that enables users to search documents using natural language with filters. The control integrates with the Semantic Search Foundation API and can be deployed on:

- **Toolbar/Command Bar**: Global document search
- **Form Sections**: Context-aware search within a Matter/Project
- **Custom Pages**: Full-page search experience

---

## Problem Statement

### Current State

- No UI for semantic search exists
- Users rely on Dataverse views with exact-match filters
- Document discovery requires knowing exact names/metadata
- No natural language search capability

### Business Need

- Users need to search documents using natural language
- Search must be accessible from multiple entry points (toolbar, forms, Copilot)
- Results must show relevance and allow quick navigation
- Search must integrate with existing Dataverse navigation patterns

---

## Scope

### In Scope (R2)

| Feature | Description |
|---------|-------------|
| **Search Input** | Text input with search button |
| **Filter Panel** | Document Type, Matter Type, Date Range dropdowns |
| **Results List** | Ranked results with similarity scores |
| **Result Actions** | Click to open document, open in Dataverse |
| **Loading States** | Skeleton, spinner, empty state |
| **Error Handling** | User-friendly error messages |
| **Dark Mode** | Full ADR-021 compliance |
| **Responsive** | Works in sidebar, full-width, Custom Page |

### Out of Scope (Future)

| Feature | Deferred To |
|---------|-------------|
| Auto-suggest/typeahead | Future enhancement |
| Search history | Future enhancement |
| Saved searches | Future enhancement |
| "Did you mean..." suggestions | Agentic RAG project |
| Voice search | Not planned |

---

## User Experience

### Entry Points

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           ENTRY POINTS                                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  1. TOOLBAR BUTTON                                                          │
│     ┌──────────────────────────────────────────────────────────────────┐    │
│     │  [🔍 Search Documents]  → Opens Search Dialog (Custom Page)       │    │
│     └──────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  2. MATTER FORM SECTION                                                     │
│     ┌──────────────────────────────────────────────────────────────────┐    │
│     │  Matter: Acme Corp Litigation                                     │    │
│     │  ┌────────────────────────────────────────────────────────────┐  │    │
│     │  │  [Search within this Matter...]        [🔍]                │  │    │
│     │  │  ───────────────────────────────────────                   │  │    │
│     │  │  Results scoped to this Matter only                        │  │    │
│     │  └────────────────────────────────────────────────────────────┘  │    │
│     └──────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  3. CUSTOM PAGE (FULL EXPERIENCE)                                           │
│     ┌──────────────────────────────────────────────────────────────────┐    │
│     │  ┌─────────────────────────────────────────────────────────────┐ │    │
│     │  │ [Search documents using natural language...]    [🔍 Search] │ │    │
│     │  └─────────────────────────────────────────────────────────────┘ │    │
│     │  ┌──────────┬──────────────────────────────────────────────────┐ │    │
│     │  │ Filters  │ Results (127 found)                              │ │    │
│     │  │          │                                                  │ │    │
│     │  │ Doc Type │ ┌─────────────────────────────────────────────┐ │ │    │
│     │  │ [▼ All ] │ │ 📄 Master Service Agreement.pdf    [87%]   │ │ │    │
│     │  │          │ │    Contract · Acme Corp · Jun 2024          │ │ │    │
│     │  │ Matter   │ │    "...payment terms shall be net 30..."    │ │ │    │
│     │  │ [▼ All ] │ └─────────────────────────────────────────────┘ │ │    │
│     │  │          │ ┌─────────────────────────────────────────────┐ │ │    │
│     │  │ Date     │ │ 📄 Invoice #2024-001.pdf            [72%]   │ │ │    │
│     │  │ [▼ Any ] │ │    Invoice · Acme Corp · Jan 2024           │ │ │    │
│     │  └──────────┴─└─────────────────────────────────────────────┘ │ │    │
│     └──────────────────────────────────────────────────────────────────┘    │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Search Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           USER SEARCH FLOW                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  1. User enters natural language query                                      │
│     "Find contracts about payment terms from last year"                     │
│                                                                              │
│  2. User optionally selects filters                                         │
│     Document Type: Contract ✓                                               │
│     Date Range: Last 12 months ✓                                            │
│                                                                              │
│  3. User clicks Search (or presses Enter)                                   │
│                                                                              │
│  4. Loading state shown                                                     │
│     [Searching... ████████░░░░░░░░]                                         │
│                                                                              │
│  5. Results displayed with:                                                 │
│     - Document name and icon                                                │
│     - Similarity score badge (87%)                                          │
│     - Document type and matter                                              │
│     - Highlighted snippet from content                                      │
│     - Actions: Open File, Open Record                                       │
│                                                                              │
│  6. User clicks result → Document opens                                     │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Technical Architecture

### Component Structure

```
src/client/pcf/SemanticSearchControl/
├── SemanticSearchControl.pcfproj
├── SemanticSearchControl/
│   ├── ControlManifest.Input.xml
│   ├── index.ts                          # PCF entry point
│   ├── SemanticSearchControl.tsx         # Main component
│   ├── components/
│   │   ├── SearchInput.tsx               # Search box with button
│   │   ├── FilterPanel.tsx               # Filter dropdowns
│   │   ├── ResultsList.tsx               # Results container
│   │   ├── ResultItem.tsx                # Single result card
│   │   ├── SimilarityBadge.tsx           # Score indicator
│   │   ├── HighlightedSnippet.tsx        # Content highlight
│   │   ├── EmptyState.tsx                # No results message
│   │   └── LoadingState.tsx              # Skeleton/spinner
│   ├── hooks/
│   │   ├── useSemanticSearch.ts          # Search API hook
│   │   ├── useFilters.ts                 # Filter state management
│   │   └── useDebounce.ts                # Input debouncing
│   ├── services/
│   │   ├── SemanticSearchApiService.ts   # API client
│   │   └── MsalAuthProvider.ts           # Auth (copy from Viewer)
│   └── types/
│       ├── search.ts                     # Search types
│       └── api.ts                        # API request/response types
├── Solution/
│   ├── solution.xml
│   ├── customizations.xml
│   ├── [Content_Types].xml
│   ├── Controls/sprk_Spaarke.Controls.SemanticSearchControl/
│   └── pack.ps1
├── package.json
├── tsconfig.json
└── featureconfig.json
```

### Control Manifest

```xml
<?xml version="1.0" encoding="utf-8" ?>
<manifest>
  <control namespace="Spaarke.Controls" constructor="SemanticSearchControl" version="1.0.0"
           display-name-key="Semantic Search"
           description-key="AI-powered natural language document search"
           control-type="virtual">

    <external-service-usage enabled="true">
      <domain>spe-api-dev-67e2xz.azurewebsites.net</domain>
      <domain>login.microsoftonline.com</domain>
    </external-service-usage>

    <!-- Configuration Properties -->
    <property name="apiBaseUrl" display-name-key="API Base URL"
              of-type="SingleLine.Text" usage="input" required="false" />

    <property name="tenantId" display-name-key="Tenant ID"
              of-type="SingleLine.Text" usage="input" required="false" />

    <!-- Scope Properties -->
    <property name="searchScope" display-name-key="Search Scope"
              of-type="Enum" usage="input" required="false" default-value="all">
      <value name="all" display-name-key="All Documents" />
      <value name="matter" display-name-key="Current Matter" />
      <value name="custom" display-name-key="Custom Scope" />
    </property>

    <property name="scopeId" display-name-key="Scope ID"
              of-type="SingleLine.Text" usage="bound" required="false" />

    <!-- UI Configuration -->
    <property name="showFilters" display-name-key="Show Filters"
              of-type="TwoOptions" usage="input" default-value="true" />

    <property name="resultsLimit" display-name-key="Results Limit"
              of-type="Whole.None" usage="input" default-value="25" />

    <property name="placeholder" display-name-key="Placeholder Text"
              of-type="SingleLine.Text" usage="input"
              default-value="Search documents using natural language..." />

    <!-- Output Properties -->
    <property name="selectedDocumentId" display-name-key="Selected Document"
              of-type="SingleLine.Text" usage="output" />

    <resources>
      <code path="index.ts" order="1"/>
      <platform-library name="React" version="16.14.0" />
      <platform-library name="Fluent" version="9.46.2" />
    </resources>

    <feature-usage>
      <uses-feature name="WebAPI" required="true" />
      <uses-feature name="Utility" required="true" />
    </feature-usage>
  </control>
</manifest>
```

---

## Component Design

### Main Component

```tsx
// SemanticSearchControl.tsx
export const SemanticSearchControl: React.FC<ISemanticSearchControlProps> = ({
  apiBaseUrl,
  tenantId,
  searchScope,
  scopeId,
  showFilters,
  resultsLimit,
  placeholder,
  onDocumentSelected,
  fluentTheme,
}) => {
  const [query, setQuery] = useState("");
  const [filters, setFilters] = useFilters();
  const { results, isLoading, error, search } = useSemanticSearch({
    apiBaseUrl,
    tenantId,
    scope: searchScope,
    scopeId,
    limit: resultsLimit,
  });

  const handleSearch = useCallback(() => {
    if (query.trim()) {
      search(query, filters);
    }
  }, [query, filters, search]);

  return (
    <FluentProvider theme={fluentTheme}>
      <div className={styles.container}>
        <SearchInput
          value={query}
          onChange={setQuery}
          onSearch={handleSearch}
          placeholder={placeholder}
          isLoading={isLoading}
        />

        {showFilters && (
          <FilterPanel
            filters={filters}
            onChange={setFilters}
          />
        )}

        {isLoading && <LoadingState />}

        {error && <ErrorState error={error} onRetry={handleSearch} />}

        {!isLoading && !error && results.length === 0 && query && (
          <EmptyState query={query} />
        )}

        {!isLoading && !error && results.length > 0 && (
          <ResultsList
            results={results}
            onSelect={onDocumentSelected}
          />
        )}
      </div>
    </FluentProvider>
  );
};
```

### Search API Service

```typescript
// SemanticSearchApiService.ts
export class SemanticSearchApiService {
  constructor(private apiBaseUrl: string) {}

  async search(
    query: string,
    params: SearchParams,
    accessToken?: string
  ): Promise<SemanticSearchResponse> {
    const url = `${this.apiBaseUrl}/api/ai/search/semantic`;

    const response = await fetch(url, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        ...(accessToken && { Authorization: `Bearer ${accessToken}` }),
      },
      body: JSON.stringify({
        query,
        scope: params.scope,
        scopeId: params.scopeId,
        documentIds: params.documentIds,
        filters: {
          documentTypes: params.documentTypes,
          matterTypes: params.matterTypes,
          dateRange: params.dateRange,
        },
        options: {
          limit: params.limit ?? 25,
          includeHighlights: true,
        },
      }),
    });

    if (!response.ok) {
      throw new SemanticSearchApiError(response.status, await response.text());
    }

    return response.json();
  }
}
```

### Result Item Component

```tsx
// ResultItem.tsx
export const ResultItem: React.FC<IResultItemProps> = ({
  result,
  onSelect,
}) => {
  const styles = useStyles();

  const handleOpenFile = useCallback(() => {
    if (result.fileUrl) {
      window.open(result.fileUrl, "_blank");
    }
  }, [result.fileUrl]);

  const handleOpenRecord = useCallback(() => {
    if (result.recordUrl) {
      // Use Xrm.Navigation for in-app navigation
      Xrm.Navigation.openUrl(result.recordUrl);
    }
  }, [result.recordUrl]);

  return (
    <Card className={styles.resultCard} onClick={() => onSelect(result)}>
      <CardHeader
        image={<DocumentIcon fileType={result.fileType} />}
        header={
          <div className={styles.headerContent}>
            <Body1Strong className={styles.documentName}>
              {result.name}
            </Body1Strong>
            <SimilarityBadge score={result.combinedScore} />
          </div>
        }
        description={
          <Caption1 className={styles.metadata}>
            {result.documentType} · {result.matterName} · {formatDate(result.createdOn)}
          </Caption1>
        }
      />

      {result.highlights?.length > 0 && (
        <div className={styles.highlightSection}>
          <HighlightedSnippet text={result.highlights[0]} />
        </div>
      )}

      <div className={styles.actions}>
        <Button
          icon={<Open16Regular />}
          appearance="subtle"
          onClick={handleOpenFile}
          title="Open file"
        />
        <Button
          icon={<DocumentBulletList16Regular />}
          appearance="subtle"
          onClick={handleOpenRecord}
          title="Open record"
        />
      </div>
    </Card>
  );
};
```

### Similarity Badge

```tsx
// SimilarityBadge.tsx
export const SimilarityBadge: React.FC<{ score: number }> = ({ score }) => {
  const percent = Math.round(score * 100);

  const color = useMemo(() => {
    if (percent >= 80) return "success";
    if (percent >= 60) return "warning";
    return "subtle";
  }, [percent]);

  return (
    <Badge appearance="tint" color={color} size="small">
      {percent}%
    </Badge>
  );
};
```

---

## Filter Panel Design

### Supported Filters

| Filter | Type | Source |
|--------|------|--------|
| Document Type | Multi-select dropdown | Dataverse optionset |
| Matter Type | Multi-select dropdown | Dataverse optionset |
| Date Range | Preset dropdown | Created/Modified |
| File Type | Multi-select dropdown | Static list |

### Filter State

```typescript
interface SearchFilters {
  documentTypes: string[];
  matterTypes: string[];
  fileTypes: string[];
  dateRange: {
    preset: "any" | "today" | "week" | "month" | "quarter" | "year" | "custom";
    from?: string;
    to?: string;
  };
}
```

### Date Range Presets

| Preset | Label | Date Calculation |
|--------|-------|------------------|
| `any` | Any time | No filter |
| `today` | Today | Today only |
| `week` | Last 7 days | -7 days |
| `month` | Last 30 days | -30 days |
| `quarter` | Last 90 days | -90 days |
| `year` | Last 12 months | -365 days |
| `custom` | Custom range | User-selected dates |

---

## Playbooks Integration Considerations

### Current: Direct API Consumption

The PCF control directly calls the Semantic Search Foundation API. No Playbooks integration in R2.

### Future: Enhanced Search via Playbooks

When the Agentic RAG project is complete, the control could optionally leverage Playbooks for enhanced search:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                     FUTURE: PLAYBOOKS ENHANCED SEARCH                        │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  User Query: "Find contracts about payment terms"                           │
│                        │                                                    │
│                        ▼                                                    │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │  PCF Control → AI Chat (Copilot)                                      │  │
│  │                        │                                              │  │
│  │                        ▼                                              │  │
│  │  Playbook Orchestrator                                                │  │
│  │  - Knowledge: Expand "payment terms" → synonyms                       │  │
│  │  - Skills: Classify intent → document search                          │  │
│  │  - Tools: Execute search_documents tool                               │  │
│  │  - Outcomes: Format results for display                               │  │
│  │                        │                                              │  │
│  │                        ▼                                              │  │
│  │  Enhanced results with explanations                                   │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Control Property for Mode Selection

```xml
<property name="searchMode" display-name-key="Search Mode"
          of-type="Enum" usage="input" default-value="api">
  <value name="api" display-name-key="Direct API" />
  <value name="copilot" display-name-key="Copilot Enhanced" />  <!-- Future -->
</property>
```

---

## Deployment Strategy

### Solution Packaging

| Component | Solution Name | Version |
|-----------|---------------|---------|
| SemanticSearchControl | SpaarkeSemanticSearch | 1.0.0 |

### Deployment Locations

| Location | Configuration | Use Case |
|----------|---------------|----------|
| **Toolbar button** | Opens Custom Page as dialog | Global search |
| **Matter form** | `searchScope="matter"`, `scopeId` bound to Matter ID | Scoped search |
| **Custom Page** | Full-width, all features enabled | Dedicated search page |
| **Dashboard** | Compact mode, limited results | Quick access |

---

## ADR Compliance

| ADR | Requirement | Implementation |
|-----|-------------|----------------|
| ADR-006 | PCF over webresources | Native PCF control |
| ADR-021 | Fluent UI v9 | All components use @fluentui/react-components |
| ADR-021 | Design tokens | All styles use tokens.* |
| ADR-021 | Dark mode | Theme from context.fluentDesignLanguage |
| ADR-022 | React 16 | ReactDOM.render, platform-library declarations |
| ADR-022 | Unmanaged solutions | Deployed unmanaged |

---

## Success Criteria

- [ ] Control renders in form sections and Custom Pages
- [ ] Search returns results from Foundation API
- [ ] Filters correctly modify search parameters
- [ ] Results display with similarity scores and highlights
- [ ] Click opens document file or Dataverse record
- [ ] Loading and error states display correctly
- [ ] Dark mode works correctly
- [ ] Control works in toolbar-launched dialog

---

## Dependencies

### Required (Must Complete First)

| Dependency | Project | Status |
|------------|---------|--------|
| Semantic Search API | ai-semantic-search-foundation-r1 | Pending |

### Shared Components (Reuse)

| Component | Source | Reuse |
|-----------|--------|-------|
| MsalAuthProvider | DocumentRelationshipViewer | Copy |
| Theme handling | DocumentRelationshipViewer | Copy pattern |
| File icons | Existing components | Import |

---

## Testing Strategy

| Test Type | Scope | Tools |
|-----------|-------|-------|
| Component tests | UI rendering, interactions | React Testing Library |
| Integration tests | API communication | MSW (Mock Service Worker) |
| E2E tests | Full search flow | Playwright |
| Accessibility tests | Screen reader, keyboard | axe-core |

---

## Future Enhancements

| Enhancement | Description | Priority |
|-------------|-------------|----------|
| Auto-suggest | Typeahead suggestions while typing | Medium |
| Search history | Recent searches dropdown | Low |
| Saved searches | Save and name frequent searches | Low |
| Keyboard shortcuts | Ctrl+K to open search | Medium |
| Voice input | Speech-to-text for search | Low |
| Copilot mode | Route through AI Chat for enhanced results | High (Agentic RAG) |

---

## Appendix: UI Mockups

### Compact Mode (Form Section)

```
┌────────────────────────────────────────────────────────────────┐
│ [🔍 Search documents...]                              [Search] │
├────────────────────────────────────────────────────────────────┤
│ 📄 Contract Amendment.pdf                              [85%]   │
│    Contract · Updated Jan 2025                                 │
├────────────────────────────────────────────────────────────────┤
│ 📄 Payment Schedule.xlsx                               [72%]   │
│    Spreadsheet · Created Dec 2024                              │
├────────────────────────────────────────────────────────────────┤
│ Showing 2 of 15 results · [View all →]                         │
└────────────────────────────────────────────────────────────────┘
```

### Full Mode (Custom Page)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  DOCUMENT SEARCH                                                       [X]  │
├─────────────────────────────────────────────────────────────────────────────┤
│  ┌─────────────────────────────────────────────────────────────┐ [Search]  │
│  │ Find contracts about payment terms from last year           │           │
│  └─────────────────────────────────────────────────────────────┘           │
│                                                                             │
│  ┌────────────┬───────────────────────────────────────────────────────────┐│
│  │ FILTERS    │ RESULTS (127 found in 0.4s)                               ││
│  │            │                                                           ││
│  │ Doc Type   │ ┌───────────────────────────────────────────────────────┐ ││
│  │ [Contract▼]│ │ 📄 Master Service Agreement - Acme Corp.pdf   [87%]  │ ││
│  │            │ │    Contract · Acme Corp Litigation · Jun 15, 2024     │ ││
│  │ Matter     │ │    "...payment terms shall be net 30 days from..."   │ ││
│  │ [All     ▼]│ │    [Open File]  [Open Record]                        │ ││
│  │            │ └───────────────────────────────────────────────────────┘ ││
│  │ Date       │                                                           ││
│  │ [Last year]│ ┌───────────────────────────────────────────────────────┐ ││
│  │            │ │ 📄 Payment Terms Amendment.pdf                [82%]  │ ││
│  │ File Type  │ │    Amendment · Acme Corp Litigation · Aug 20, 2024   │ ││
│  │ [All     ▼]│ │    "...revised payment conditions include..."        │ ││
│  │            │ │    [Open File]  [Open Record]                        │ ││
│  │ ─────────  │ └───────────────────────────────────────────────────────┘ ││
│  │ [Clear all]│                                                           ││
│  └────────────┴───────────────────────────────────────────────────────────┘│
│                                                                             │
│  ◀ 1 2 3 4 5 ... 13 ▶                                                      │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Related Projects

| Project | Relationship |
|---------|--------------|
| `ai-semantic-search-foundation-r1` | Provides API this control consumes |
| `ai-document-relationship-visuals` | May embed search component |
| Future: Agentic RAG | Enables Copilot-enhanced search mode |
