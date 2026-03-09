# AI Semantic Search UI R2 - Implementation Plan

> **Version**: 1.0
> **Created**: 2026-01-20
> **Source**: [spec.md](spec.md)

---

## Executive Summary

Build a PCF control for semantic search with natural language query support, dynamic filters, infinite scroll results, and Dataverse-native navigation. The control follows ADR-021 (Fluent v9) and ADR-022 (React 16 platform libraries).

---

## Phase Overview

| Phase | Name | Tasks | Focus |
|-------|------|-------|-------|
| **1** | Project Setup & Foundation | 001-006 | Control scaffolding, manifest, theming |
| **2** | Core Components | 007-016 | Search input, filters, result cards |
| **3** | API Integration & State | 017-023 | API service, search hooks, pagination |
| **4** | Infinite Scroll & Performance | 024-029 | Intersection observer, DOM cap, load-more |
| **5** | Navigation & Actions | 030-034 | Open Record, Open File, View All |
| **6** | Error Handling & States | 035-040 | Loading, error, empty states |
| **7** | Testing & Polish | 041-046 | Unit tests, integration, accessibility |
| **8** | Solution & Deployment | 047-050 | Solution packaging, deployment |

---

## Phase 1: Project Setup & Foundation

**Goal**: Scaffold the PCF control with proper configuration, theming, and base structure.

### Tasks

| ID | Task | Description | Dependencies |
|----|------|-------------|--------------|
| 001 | Create PCF project | Scaffold `SemanticSearchControl` using PAC CLI | - |
| 002 | Configure manifest | Define properties: apiBaseUrl, tenantId, searchScope, etc. | 001 |
| 003 | Configure platform libraries | Set up featureconfig.json for React/Fluent platform libs | 001 |
| 004 | Set up index.ts entry | React 16 render pattern (ReactDOM.render) | 001 |
| 005 | Implement FluentProvider wrapper | Theme detection from Dataverse context | 004 |
| 006 | Create base component structure | SemanticSearchControl.tsx with layout skeleton | 005 |

### Deliverables
- Working PCF project that renders "Hello World" with proper theming
- All manifest properties defined
- Platform libraries configured

---

## Phase 2: Core Components

**Goal**: Build the main UI components using Fluent v9.

### Tasks

| ID | Task | Description | Dependencies |
|----|------|-------------|--------------|
| 007 | Create SearchInput component | Text input with search button | 006 |
| 008 | Create FilterPanel component | Container for filter dropdowns | 006 |
| 009 | Create FilterDropdown component | Reusable dropdown for filter types | 008 |
| 010 | Create DateRangeFilter component | Date range picker for date filtering | 008 |
| 011 | Create ResultsList component | Container for result cards | 006 |
| 012 | Create ResultCard component | Individual result display | 011 |
| 013 | Create SimilarityBadge component | Score indicator with color coding | 012 |
| 014 | Create HighlightedSnippet component | Content highlight with markup | 012 |
| 015 | Create EmptyState component | No results message | 011 |
| 016 | Create ErrorState component | Error with retry button | 011 |

### Deliverables
- All UI components rendering with mock data
- Consistent styling via makeStyles
- Components ready for data binding

---

## Phase 3: API Integration & State

**Goal**: Integrate with the Semantic Search API and manage application state.

### Tasks

| ID | Task | Description | Dependencies |
|----|------|-------------|--------------|
| 017 | Copy MsalAuthProvider | Adapt from DocumentRelationshipViewer | 005 |
| 018 | Create SemanticSearchApiService | API client for /api/ai/search/semantic | 017 |
| 019 | Define TypeScript interfaces | SearchResult, SearchFilters, etc. | - |
| 020 | Create useSemanticSearch hook | Search execution and state | 018, 019 |
| 021 | Create useFilters hook | Filter state management | 019 |
| 022 | Create DataverseMetadataService | Fetch optionset values | 017 |
| 023 | Integrate filters with Dataverse metadata | Populate dropdowns dynamically | 021, 022 |

### Deliverables
- Working API integration with authentication
- Search execution with real API
- Dynamic filter population from Dataverse

---

## Phase 4: Infinite Scroll & Performance

**Goal**: Implement infinite scroll with performance guardrails.

### Tasks

| ID | Task | Description | Dependencies |
|----|------|-------------|--------------|
| 024 | Create useInfiniteScroll hook | Intersection observer for load-more | - |
| 025 | Integrate infinite scroll with results | Connect observer to ResultsList | 024, 020 |
| 026 | Implement offset-based pagination | Track offset, append results | 020, 025 |
| 027 | Add loading-more state | Inline spinner during fetch | 025, 026 |
| 028 | Implement DOM cap (200 items) | Stop loading, show "View all" link | 026 |
| 029 | Performance testing | Verify smooth scrolling, memory usage | 028 |

### Deliverables
- Smooth infinite scroll experience
- DOM capped at 200 items
- "View all" link when results exceed cap

---

## Phase 5: Navigation & Actions

**Goal**: Implement result card actions using Dataverse-native navigation.

### Tasks

| ID | Task | Description | Dependencies |
|----|------|-------------|--------------|
| 030 | Implement Open File action | window.open for SPE file URLs | 012 |
| 031 | Implement Open Record (Modal) | Xrm.Navigation.navigateTo target:2 | 012 |
| 032 | Implement Open Record (New Tab) | Xrm.Navigation.navigateTo target:1 | 012 |
| 033 | Add action menu to ResultCard | Dropdown with Open options | 031, 032 |
| 034 | Implement View All navigation | Navigate to Custom Page | 028 |

### Deliverables
- Working navigation actions
- Modal dialog for record preview
- Custom Page navigation for "View all"

---

## Phase 6: Error Handling & States

**Goal**: Implement all loading, error, and empty states.

### Tasks

| ID | Task | Description | Dependencies |
|----|------|-------------|--------------|
| 035 | Create LoadingState component | Skeleton cards for initial load | - |
| 036 | Integrate loading state | Show skeleton during search | 035, 020 |
| 037 | Implement error handling | Catch API errors, show ErrorState | 016, 020 |
| 038 | Implement retry functionality | "Try Again" button re-executes search | 037 |
| 039 | Integrate empty state | Show when results array empty | 015, 020 |
| 040 | Add scope-aware filter visibility | Hide irrelevant filters based on scope | 021 |

### Deliverables
- Complete state handling (loading, error, empty)
- Retry functionality
- Scope-aware filter visibility

---

## Phase 7: Testing & Polish

**Goal**: Ensure quality through testing and refinement.

### Tasks

| ID | Task | Description | Dependencies |
|----|------|-------------|--------------|
| 041 | Unit tests for hooks | Test useSemanticSearch, useFilters, useInfiniteScroll | 020, 021, 024 |
| 042 | Unit tests for components | Test ResultCard, FilterPanel, etc. | 007-016 |
| 043 | Integration test: search flow | End-to-end search execution | 020 |
| 044 | Integration test: navigation | Verify Xrm.Navigation calls | 030-033 |
| 045 | Dark mode testing | Verify all themes render correctly | All |
| 046 | Bundle size verification | Ensure < 1MB (excluding platform libs) | All |

### Deliverables
- Test coverage for critical paths
- Verified dark mode support
- Bundle size under limit

---

## Phase 8: Solution & Deployment

**Goal**: Package and deploy to Dataverse.

### Tasks

| ID | Task | Description | Dependencies |
|----|------|-------------|--------------|
| 047 | Create solution project | Configure SpaarkeSemanticSearch solution | All |
| 048 | Build and package solution | Generate managed/unmanaged solutions | 047 |
| 049 | Deploy to dev environment | pac solution import | 048 |
| 050 | Deployment documentation | Document deployment steps, troubleshooting | 049 |

### Deliverables
- Deployable Dataverse solution
- Deployment documentation
- Control available in dev environment

---

## Parallel Execution Groups

Tasks that can execute in parallel when dependencies are satisfied:

| Group | Tasks | Shared Dependency |
|-------|-------|-------------------|
| **Components A** | 007, 008, 011, 015, 016 | 006 |
| **Components B** | 009, 010 | 008 |
| **Components C** | 012, 013, 014 | 011 |
| **API Services** | 017, 019, 022 | 005 |
| **Testing** | 041, 042 | Components complete |
| **Actions** | 030, 031, 032 | 012 |

---

## Success Criteria (from spec.md)

| # | Criterion | Phase |
|---|-----------|-------|
| 1 | Control renders in form sections | 8 |
| 2 | Control renders in Custom Pages | 8 |
| 3 | Search returns results from Foundation API | 3 |
| 4 | Filters populate from Dataverse metadata | 3 |
| 5 | Infinite scroll loads more results | 4 |
| 6 | Infinite scroll stops at DOM cap (200) | 4 |
| 7 | Results display similarity scores and highlights | 2 |
| 8 | Open File opens document in new tab | 5 |
| 9 | Open Record opens modal dialog | 5 |
| 10 | Scope-aware filters hide when appropriate | 6 |
| 11 | Dark mode renders correctly | 7 |
| 12 | Loading states display correctly | 6 |
| 13 | Error states display with retry | 6 |
| 14 | Bundle size < 1MB | 7 |
| 15 | MSAL authentication works | 3 |

---

## Risk Mitigations

| Risk | Mitigation | Phase |
|------|------------|-------|
| Filter extensibility complexity | Design spike task in Phase 2 | 2 |
| Dataverse metadata fetch slow | Cache optionset values | 3 |
| Large result sets | DOM cap at 200; "View all" link | 4 |
| Cross-origin API calls | Document external domain strategy | 1 |
| MSAL popup blocked | Follow established pattern; error message | 3 |

---

*Plan generated from spec.md on 2026-01-20*
