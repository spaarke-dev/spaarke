# AI Semantic Search UI R2 - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-01-20
> **Source**: design.md
> **Depends On**: ai-semantic-search-foundation-r1 (completed)

---

## Executive Summary

Build a **PCF control for semantic search** that enables users to search documents using natural language with filters. The control integrates with the Semantic Search Foundation API (`/api/ai/search/semantic`) and can be deployed on toolbar/command bar (via Custom Page dialog), form sections (matter-scoped), and dedicated Custom Pages (full experience).

---

## Scope

### In Scope

| Feature | Description |
|---------|-------------|
| **Search Input** | Text input with search button, placeholder text configurable |
| **Filter Panel** | Document Type, Matter Type, Date Range, File Type - fetched from Dataverse metadata |
| **Infinite Scroll Results** | Fluent v9 list with load-more pattern (Power Apps grid style) |
| **Result Cards** | Document name, similarity badge, metadata, highlighted snippet |
| **Result Actions** | Open File (new tab), Open Record (new tab OR modal dialog) |
| **Loading States** | Skeleton loaders, inline spinners |
| **Error Handling** | User-friendly error messages with retry |
| **Dark Mode** | Full ADR-021 compliance via FluentProvider |
| **Responsive Layout** | Compact (form section), Full (Custom Page) |
| **Scope-Aware Filters** | Hide irrelevant filters when scoped (e.g., hide Matter Type on Matter form) |
| **Extensible Filter Schema** | Design supports adding custom filters in future |

### Out of Scope

| Feature | Reason |
|---------|--------|
| Auto-suggest/typeahead | Future enhancement |
| Search history | Future enhancement |
| Saved searches | Future enhancement |
| "Did you mean..." suggestions | Agentic RAG project |
| Voice search | Not planned |
| Copilot-enhanced mode | Future (Agentic RAG) |
| Ribbon button creation | Deployment task (use ribbon-edit skill separately) |

### Affected Areas

| Path | Description |
|------|-------------|
| `src/client/pcf/SemanticSearchControl/` | New PCF control (primary deliverable) |
| `src/client/shared/Spaarke.UI.Components/` | May add shared components if reusable |
| `src/solutions/SpaarkeSemanticSearch/` | Solution packaging |

---

## Requirements

### Functional Requirements

| ID | Requirement | Acceptance Criteria |
|----|-------------|---------------------|
| **FR-01** | Search input accepts natural language queries | User can type free-form text; Search button triggers API call |
| **FR-02** | Filter panel shows dynamic filter options | Document Type, Matter Type populated from Dataverse optionsets on init |
| **FR-03** | Filters require explicit Search click | Changing filter does NOT auto-execute; user must click Search |
| **FR-04** | Results display as infinite scroll list | Initial load shows N results; scroll triggers load-more; follows Fluent v9 List pattern |
| **FR-05** | Result cards show relevance and metadata | Each card: name, similarity badge (%), type, matter, date, snippet highlight |
| **FR-06** | Open File action opens document in new tab | Clicking "Open File" opens `fileUrl` in `_blank` |
| **FR-07** | Open Record offers new tab OR modal | User can choose: new tab navigation or modal dialog (preserves list context) |
| **FR-08** | Scope-aware filter visibility | When `searchScope="matter"`, hide Matter Type filter (already scoped) |
| **FR-09** | Empty state displays helpful message | When no results: show message with query echo and suggestions |
| **FR-10** | Error state displays with retry option | API errors show user-friendly message + "Try Again" button |
| **FR-11** | Loading state shows skeleton/spinner | Initial load: skeleton cards; load-more: inline spinner |
| **FR-12** | Dark mode renders correctly | All colors via Fluent tokens; no hard-coded colors |
| **FR-13** | Compact mode for form sections | Reduced height, limited results, "View all" link to Custom Page |

### Non-Functional Requirements

| ID | Requirement | Target |
|----|-------------|--------|
| **NFR-01** | Bundle size | < 1MB (platform libraries excluded) |
| **NFR-02** | Initial render | < 500ms to interactive |
| **NFR-03** | Search response display | Results render within 100ms of API response |
| **NFR-04** | Accessibility | Rely on Fluent UI v9's built-in accessibility; no custom ARIA implementation required |

---

## Technical Constraints

### Applicable ADRs

| ADR | Requirement | Compliance Approach |
|-----|-------------|---------------------|
| **ADR-006** | PCF over webresources | Native PCF control, no legacy JS |
| **ADR-012** | Shared component library | Import from `@spaarke/ui-components` where applicable |
| **ADR-021** | Fluent UI v9 design system | All components from `@fluentui/react-components`; tokens for styling |
| **ADR-022** | PCF platform libraries | React 16 APIs (`ReactDOM.render`); `platform-library` declarations |

### MUST Rules

- **MUST** use `@fluentui/react-components` (Fluent v9) exclusively
- **MUST** use React 16 APIs (`ReactDOM.render`, NOT `createRoot`)
- **MUST** declare `platform-library` for React and Fluent in manifest
- **MUST** wrap UI in `FluentProvider` with theme from context
- **MUST** use Fluent design tokens for all colors and spacing
- **MUST** support light, dark, and high-contrast modes
- **MUST** use `makeStyles` (Griffel) for custom styling
- **MUST** keep PCF bundle under 1MB (5MB absolute max)
- **MUST** follow Power Apps grid control patterns for infinite scroll
- **MUST** import icons from `@fluentui/react-icons`

### MUST NOT Rules

- **MUST NOT** use Fluent v8 (`@fluentui/react`)
- **MUST NOT** use React 18 APIs (`createRoot`, `hydrateRoot`)
- **MUST NOT** hard-code colors (hex, rgb, named)
- **MUST NOT** bundle React/ReactDOM (use platform libraries)
- **MUST NOT** import from granular `@fluentui/react-*` packages
- **MUST NOT** make API calls from ribbon scripts

### Existing Patterns to Follow

| Pattern | Reference |
|---------|-----------|
| PCF control structure | `src/client/pcf/DocumentRelationshipViewer/` |
| MSAL auth provider | `src/client/pcf/DocumentRelationshipViewer/services/MsalAuthProvider.ts` |
| Theme handling | `src/client/pcf/DocumentRelationshipViewer/` (fluentDesignLanguage) |
| API service pattern | `src/client/pcf/*/services/*ApiService.ts` |
| Infinite scroll | Fluent v9 List + intersection observer pattern |

---

## Component Architecture

### Directory Structure

```
src/client/pcf/SemanticSearchControl/
├── SemanticSearchControl.pcfproj
├── SemanticSearchControl/
│   ├── ControlManifest.Input.xml
│   ├── index.ts                          # PCF entry point (React 16 render)
│   ├── SemanticSearchControl.tsx         # Main component
│   ├── components/
│   │   ├── SearchInput.tsx               # Search box with button
│   │   ├── FilterPanel.tsx               # Filter dropdowns (dynamic)
│   │   ├── ResultsList.tsx               # Infinite scroll container
│   │   ├── ResultCard.tsx                # Single result card
│   │   ├── SimilarityBadge.tsx           # Score indicator (color-coded)
│   │   ├── HighlightedSnippet.tsx        # Content highlight with markup
│   │   ├── EmptyState.tsx                # No results message
│   │   ├── ErrorState.tsx                # Error with retry
│   │   └── LoadingState.tsx              # Skeleton/spinner
│   ├── hooks/
│   │   ├── useSemanticSearch.ts          # Search API + pagination state
│   │   ├── useFilters.ts                 # Filter state + Dataverse metadata fetch
│   │   └── useInfiniteScroll.ts          # Intersection observer for load-more
│   ├── services/
│   │   ├── SemanticSearchApiService.ts   # API client for /api/ai/search/semantic
│   │   ├── DataverseMetadataService.ts   # Fetch optionset values
│   │   └── MsalAuthProvider.ts           # Auth (copy pattern from Viewer)
│   └── types/
│       ├── search.ts                     # SearchResult, SearchFilters, etc.
│       └── props.ts                      # Component prop interfaces
├── Solution/
│   └── (solution packaging files)
├── package.json
├── tsconfig.json
└── featureconfig.json                    # pcfReactPlatformLibraries: on
```

### Control Properties (Manifest)

| Property | Type | Usage | Description |
|----------|------|-------|-------------|
| `apiBaseUrl` | SingleLine.Text | input | BFF API base URL |
| `tenantId` | SingleLine.Text | input | Azure AD tenant ID |
| `searchScope` | Enum (all/matter/custom) | input | Search scope mode |
| `scopeId` | SingleLine.Text | bound | ID for scoped search (e.g., Matter ID) |
| `showFilters` | TwoOptions | input | Show/hide filter panel |
| `resultsLimit` | Whole.None | input | Results per load (default: 25) |
| `placeholder` | SingleLine.Text | input | Search input placeholder text |
| `compactMode` | TwoOptions | input | Compact layout for form sections |
| `selectedDocumentId` | SingleLine.Text | output | Selected document ID (for form binding) |

---

## API Integration

### Semantic Search Endpoint

**Endpoint**: `POST /api/ai/search/semantic`

**Request**:
```json
{
  "query": "find contracts about payment terms",
  "scope": "matter",
  "scopeId": "guid-of-matter",
  "filters": {
    "documentTypes": ["contract", "amendment"],
    "matterTypes": [],
    "dateRange": { "from": "2025-01-01", "to": null },
    "fileTypes": ["pdf", "docx"]
  },
  "options": {
    "limit": 25,
    "offset": 0,
    "includeHighlights": true
  }
}
```

**Response**:
```json
{
  "results": [
    {
      "documentId": "guid",
      "name": "Master Service Agreement.pdf",
      "fileType": "pdf",
      "documentType": "Contract",
      "matterName": "Acme Corp Litigation",
      "matterId": "guid",
      "createdOn": "2024-06-15T...",
      "combinedScore": 0.87,
      "highlights": ["...payment terms shall be net 30..."],
      "fileUrl": "https://...",
      "recordUrl": "https://..."
    }
  ],
  "totalCount": 127,
  "metadata": {
    "searchTimeMs": 245,
    "query": "find contracts about payment terms"
  }
}
```

---

## External Domain Strategy

### Manifest Allowlist Constraint

PCF controls can only make HTTP requests to domains explicitly allowlisted in `ControlManifest.Input.xml`. The `apiBaseUrl` property **must** resolve to an allowlisted domain or the control will fail at runtime.

### Approach Options

#### Option A: Stable Custom Domain (Preferred - Enterprise-Ready)

Use a single stable domain across all environments via Front Door, APIM, or reverse proxy:

| Environment | apiBaseUrl | Manifest Entry |
|-------------|------------|----------------|
| Dev | `https://api.spaarke.io/dev` | `api.spaarke.io` |
| Test | `https://api.spaarke.io/test` | `api.spaarke.io` |
| Prod | `https://api.spaarke.io/prod` | `api.spaarke.io` |

**Benefits**: Single manifest for all environments; cleaner deployment.

#### Option B: Multi-Domain Allowlist (Current State)

Allowlist all known environment-specific domains:

```xml
<external-service-usage enabled="true">
  <domain>spe-api-dev-67e2xz.azurewebsites.net</domain>
  <domain>spe-api-test-*.azurewebsites.net</domain>
  <domain>spe-api-prod-*.azurewebsites.net</domain>
  <domain>login.microsoftonline.com</domain>
</external-service-usage>
```

**Current Implementation**: Using Option B with dev domain. Stable domain is the preferred future state.

### Property Validation

The `apiBaseUrl` property **must** be validated at runtime:
- If domain not in manifest allowlist → show configuration error
- Do not allow arbitrary URLs without corresponding manifest entry

---

## Authentication Pattern

### MSAL Silent Token Acquisition

Follow the established pattern from `DocumentRelationshipViewer`.

**Reference Implementation**: `src/client/pcf/DocumentRelationshipViewer/services/MsalAuthProvider.ts`

### Authentication Flow

```
1. Initialize MSAL PublicClientApplication
   - tenantId from control property
   - clientId from configuration

2. Attempt silent token acquisition
   - acquireTokenSilent() with account hint

3. Handle InteractionRequired
   - Fall back to acquireTokenPopup()

4. Return access token
   - Pass in Authorization: Bearer {token} header
```

### Configuration

| Setting | Source | Value |
|---------|--------|-------|
| Tenant ID | Control property (`tenantId`) | Azure AD tenant GUID |
| Client ID | Environment config | BFF API app registration |
| Scopes | Hardcoded | `api://{client-id}/.default` |

### Error Handling

| Error Type | Action |
|------------|--------|
| `InteractionRequiredAuthError` | Trigger popup authentication |
| `BrowserAuthError` | Show "Sign in required" message with retry |
| Token expired | MSAL handles refresh automatically |
| Popup blocked | Show message to enable popups |

### Files to Create/Adapt

| File | Source | Notes |
|------|--------|-------|
| `MsalAuthProvider.ts` | Copy from DocumentRelationshipViewer | Adapt scopes |
| `msalConfig.ts` | Copy from DocumentRelationshipViewer | Update client ID |

---

## Infinite Scroll Pagination Contract

### API Pagination Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `offset` | number | Starting index (0-based) |
| `limit` | number | Max results to return (default: 25, from `resultsLimit` property) |

### Pagination Rules

| Operation | Implementation |
|-----------|----------------|
| **Initial Load** | `offset=0, limit=resultsLimit` |
| **Load More** | `offset = currentResults.length, limit=resultsLimit` |
| **Stop Condition** | Disable observer when `currentResults.length >= totalCount` |
| **Loading Guard** | Ignore intersection events while `isLoadingMore === true` |
| **Fresh Search** | Clear results array when query or filters change |

### State Machine

```
                    ┌─────────────────────────────────────────┐
                    │                                         │
                    ▼                                         │
IDLE ──(scroll to bottom)──► LOADING_MORE ──(success)──► IDLE
                                  │
                                  │ (error)
                                  ▼
                               ERROR ──(retry)──► LOADING_MORE
```

### Intersection Observer Configuration

```typescript
const observer = new IntersectionObserver(
  (entries) => {
    const [entry] = entries;
    if (entry.isIntersecting && !isLoadingMore && hasMore) {
      loadMore();
    }
  },
  {
    threshold: 0.1,
    rootMargin: "100px"  // Trigger slightly before reaching bottom
  }
);
```

### Performance Guardrails

| Constraint | Implementation |
|------------|----------------|
| **DOM Cap** | Keep max **200 items** rendered in DOM |
| **Large Result Sets** | When `totalCount > 200`, show: "Showing first 200 of {totalCount} results. [View all →]" |
| **Virtualization** | Not required for R2 if DOM cap is enforced |
| **Scroll Position** | Preserve scroll position after load-more completes |
| **Memory Management** | Clear results array on new search (query/filter change) |

---

## Navigation Patterns

### Preferred: Dataverse-Native Navigation

Use `Xrm.Navigation` APIs for all navigation to maintain Dataverse context and UX consistency.

| Action | Method | Use Case |
|--------|--------|----------|
| **Open Record (Modal)** | `Xrm.Navigation.navigateTo` with `target: 2` | Preferred - keeps user in search results context |
| **Open Record (New Tab)** | `Xrm.Navigation.navigateTo` with `target: 1` | User explicitly wants separate window |
| **Open File** | Direct URL (`window.open`) | SPE file URLs open natively |
| **View All** | `Xrm.Navigation.navigateTo` with `pageType: "custom"` | Navigate to full Custom Page |

### Implementation Examples

**Open Record in Modal Dialog (Primary Action):**
```typescript
Xrm.Navigation.navigateTo(
  {
    pageType: "entityrecord",
    entityName: result.entityLogicalName,
    entityId: result.recordId
  },
  { target: 2, width: { value: 80, unit: "%" }, height: { value: 80, unit: "%" } }
);
```

**Open Record in New Tab (Secondary Action):**
```typescript
Xrm.Navigation.navigateTo(
  {
    pageType: "entityrecord",
    entityName: result.entityLogicalName,
    entityId: result.recordId
  },
  { target: 1 }  // Opens in new browser tab
);
```

**Open File (Direct Download):**
```typescript
// SPE file URLs are pre-authenticated; open directly
window.open(result.fileUrl, "_blank");
```

**Navigate to Custom Page (View All):**
```typescript
Xrm.Navigation.navigateTo({
  pageType: "custom",
  name: "sprk_semanticsearchpage",
  recordId: scopeId  // Pass current context if scoped
});
```

### Result Card Actions

| Button | Primary Click | Menu Options |
|--------|---------------|--------------|
| **Open** | Open record in modal dialog | "Open in New Tab" |
| **File** | Open file in new tab | - |

### Avoid

- `Xrm.Navigation.openUrl` for entity records (less integrated experience)
- `window.open` for Dataverse records (loses navigation context)

---

## Success Criteria

| # | Criterion | Verification Method |
|---|-----------|---------------------|
| 1 | Control renders in form sections | Deploy to Matter form, verify renders |
| 2 | Control renders in Custom Pages | Create Custom Page, add control, verify |
| 3 | Search returns results from Foundation API | Execute search, verify API call and results |
| 4 | Filters populate from Dataverse metadata | Check network calls for optionset fetch |
| 5 | Infinite scroll loads more results | Scroll to bottom, verify load-more triggers |
| 6 | Infinite scroll stops at DOM cap (200) | Load 200+ results, verify "View all" message |
| 7 | Results display similarity scores and highlights | Visual inspection of result cards |
| 8 | Open File opens document in new tab | Click action, verify new tab opens |
| 9 | Open Record opens modal dialog | Click action, verify modal dialog opens via Xrm.Navigation |
| 10 | Scope-aware filters hide when appropriate | Place on Matter form, verify Matter Type hidden |
| 11 | Dark mode renders correctly | Toggle system theme, verify no broken colors |
| 12 | Loading states display correctly | Slow network, verify skeleton/spinner |
| 13 | Error states display with retry | Force API error, verify message and retry button |
| 14 | Bundle size < 1MB | Check build output |
| 15 | MSAL authentication works | Verify token acquisition and API auth header |

---

## Dependencies

### Prerequisites

| Dependency | Status | Notes |
|------------|--------|-------|
| ai-semantic-search-foundation-r1 | **Completed** | API endpoints available |
| BFF API deployed | Available | `spe-api-dev-67e2xz.azurewebsites.net` |
| Dataverse environment | Available | `spaarkedev1.crm.dynamics.com` |

### Shared Components to Reuse

| Component | Source | Reuse Approach |
|-----------|--------|----------------|
| MsalAuthProvider | DocumentRelationshipViewer | Copy pattern |
| Theme handling | DocumentRelationshipViewer | Copy pattern |
| FluentProvider setup | Existing PCF controls | Follow pattern |

### External Dependencies

| Dependency | Version | Purpose |
|------------|---------|---------|
| @fluentui/react-components | ^9.46.0 | UI components (devDep) |
| @fluentui/react-icons | ^2.0.0 | Icons (devDep) |
| Platform React | 16.14.0 | Runtime provided by Dataverse |
| Platform Fluent | 9.46.2 | Runtime provided by Dataverse |

---

## Owner Clarifications

*Answers captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Pagination | Page numbers or infinite scroll? | **Infinite scroll** following Fluent v9 and Power Apps grid patterns | Use intersection observer, offset-based API |
| Filter trigger | Auto-search or explicit click? | **Explicit Search click required** | Simpler state, no debounce needed |
| Open Record | Same tab or new tab? | **New tab + modal option** | Dual actions: "Open in New Tab" / "Open in Dialog" |
| View all | Where does it navigate? | **Open Custom Page** | `Xrm.Navigation.navigateTo` with pageType: "custom" |
| Filter source | Hard-coded or dynamic? | **Dataverse metadata API** | Fetch optionset values on init |
| Scoped filtering | Show all filters or hide irrelevant? | **Hide irrelevant** (e.g., hide Matter Type when on Matter form) | Conditional render based on `searchScope` |
| Filter extensibility | Fixed or extensible? | **Extensible schema** (research if complex) | Design filter system for future additions; spike task if needed |
| Ribbon button | In scope? | **Generally use command bar pattern**; may embed in PCF depending on function | Flexible deployment, ribbon-edit skill for deployment |

*Additional clarifications from spec review:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Keyboard shortcuts | Include custom shortcuts (Ctrl+K, etc.)? | **No** - remove complexity; rely on Fluent built-in | Simplified scope |
| Accessibility | Full WCAG/ARIA implementation? | **Rely on Fluent UI v9 built-in** - no custom ARIA | Reduced complexity |
| Navigation | openUrl vs navigateTo? | **Prefer Dataverse-native** `Xrm.Navigation.navigateTo` | Better UX integration |
| DOM performance | Virtualization required? | **No** - enforce 200 item DOM cap with "View all" | Simpler implementation |
| External domains | Stable domain or multi-domain? | **Multi-domain allowlist** (current); stable domain preferred future | Document both options |

---

## Assumptions

*Proceeding with these assumptions (not explicitly specified):*

| Topic | Assumption | Affects |
|-------|------------|---------|
| Minimum query length | 1 character (allow single-word searches) | Input validation |
| Maximum query length | 500 characters | Input validation, UI truncation |
| Results per load | 25 (configurable via property) | API calls, scroll behavior |
| Date range filter | Applies to document `createdOn` field | API filter mapping |
| Similarity badge colors | Green (>=80%), Yellow (60-79%), Gray (<60%) | Badge component styling |
| File icons | Use Fluent file type icons based on extension | Icon selection logic |

---

## Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Filter extensibility adds complexity | Medium | Medium | Include design spike task; define extensible schema early |
| Dataverse metadata fetch slow | Low | Low | Cache optionset values; show filters after load |
| Large result sets impact performance | Medium | Medium | DOM cap at 200 items; "View all" link for larger sets; no virtualization needed |
| Cross-origin API calls fail in test/prod | Medium | High | Document external domain strategy; validate apiBaseUrl against manifest allowlist |
| MSAL popup blocked by browser | Low | Medium | Follow established pattern; show user-friendly message to enable popups |

---

## Open Questions

*To be resolved during implementation:*

| Question | Blocks | Resolution Path |
|----------|--------|-----------------|
| Exact filter extensibility design | Task creation | Design spike in Phase 1 |
| Custom Page route/name (`sprk_semanticsearchpage`) | Deployment tasks | Define during solution setup |
| Stable custom domain (api.spaarke.io) timeline | Future deployment | Infrastructure decision; multi-domain works for now |

---

*AI-optimized specification. Original design: design.md*
