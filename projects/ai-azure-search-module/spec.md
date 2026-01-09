# AI Search & Visualization Module - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-01-08
> **Source**: DESIGN.md (January 8, 2026)

## Executive Summary

This module enables users to perform AI-powered semantic search across documents stored in SharePoint Embedded and visualize document relationships through an interactive graph interface. Users access the visualization via a "Find Related" ribbon button on the `sprk_document` form, which opens a full-screen modal with a React Flow canvas showing semantically similar documents.

**Core User Questions Addressed**:
1. "What other documents are like this document?" - Vector similarity search
2. "What documents are related to this document?" - Relationship visualization
3. "What other Matters/Projects are similar based on their documents?" - Cross-entity discovery

## Scope

### In Scope

- **Backend API Endpoints**:
  - `GET /api/ai/visualization/related/{documentId}` - Find related documents
  - Document-level embedding generation and storage (`documentVector` field)
  - Backfill migration for existing indexed documents
  - Integration with existing R3 RAG architecture

- **PCF Control**:
  - `DocumentRelationshipViewer` - Virtual PCF control with React Flow canvas
  - Full-screen modal with control panel and action bar
  - d3-force layout for document relationship graph
  - Fluent UI v9 components with dark mode support

- **Ribbon Integration**:
  - "Find Related" ribbon button on `sprk_document` entity
  - JavaScript command handler (invocation only)
  - Modal dialog launcher

- **Testing**:
  - Unit tests with mocked dependencies
  - Component tests with React Testing Library
  - Integration tests against Azure AI Search (dev environment)

### Out of Scope

- Mobile/tablet responsive design (model-driven apps are desktop-focused)
- Telemetry/analytics (deferred to Phase 4)
- Offline/disconnected support (show standard error message)
- Rebuilding existing SPE file viewer, auth flows, or navigation
- Microsoft Foundry IQ integration (R3 architecture continues)
- `GET /api/ai/visualization/cluster/{tenantId}` - Future enhancement
- `POST /api/ai/visualization/explore` - Future enhancement

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Api/Ai/` - New VisualizationEndpoints.cs
- `src/server/api/Sprk.Bff.Api/Services/Ai/` - New VisualizationService.cs
- `src/client/pcf/` - New DocumentRelationshipViewer control
- `src/solutions/` - Ribbon customization for sprk_document
- Azure AI Search index - New `documentVector` field

## Requirements

### Functional Requirements

1. **FR-01**: Find Related Documents API
   - Endpoint: `GET /api/ai/visualization/related/{documentId}`
   - Returns graph data (nodes + edges) for similar documents
   - Supports query parameters: `threshold`, `limit`, `depth`, `includeKeywords`, `documentTypes`, `includeParentEntity`
   - Acceptance: Returns valid graph response within 500ms P95

2. **FR-02**: Document-Level Embeddings
   - Store `documentVector` field in Azure AI Search index
   - Generate embeddings using Azure OpenAI text-embedding-3-small
   - Backfill existing indexed documents with document-level embeddings
   - Acceptance: All documents have `documentVector` after migration

3. **FR-03**: Graph Visualization Control
   - PCF control: `DocumentRelationshipViewer`
   - React Flow canvas with d3-force layout
   - Center node (source document) + related nodes with similarity scores
   - Edge styling based on similarity (thick/green for high, thin/gray for low)
   - Acceptance: Graph renders within 200ms with up to 100 nodes

4. **FR-04**: Full-Screen Modal Experience
   - Triggered by "Find Related" ribbon button on sprk_document form
   - Maximum canvas area for complex graphs
   - Close button in header
   - Acceptance: Modal opens/closes cleanly, no z-index conflicts

5. **FR-05**: Control Panel
   - Similarity threshold slider (50-95%, default 65%)
   - Depth limit slider (1-3 levels, default 1)
   - Max nodes per level slider (10-50, default 25)
   - Document type filter checkboxes
   - Acceptance: All filters update graph in real-time

6. **FR-06**: Node Action Bar
   - Appears when node is selected (clicked)
   - Actions: "Open Document Record" (Dataverse), "View File in SharePoint" (SPE), "Expand" (load next level)
   - Acceptance: All actions navigate correctly or expand graph

7. **FR-07**: Depth Limiting and Node Expansion
   - Default: depth=1 (source + 25 related documents)
   - Maximum depth: 3 levels
   - "Expand" action loads next level for selected node
   - Hard cap: 100 total visible nodes
   - Acceptance: No exponential growth, performance maintained

8. **FR-08**: Shared Keywords Detection
   - Extract shared keywords from existing `sprk_extractpeople`, `sprk_extractorganization` fields
   - Display shared keywords on edge hover/click
   - Acceptance: Edges show relevant shared entities

9. **FR-09**: Parent Entity Context
   - Include parent Matter/Project info in node data
   - Display parent entity name in node tooltip
   - Enable cross-entity discovery
   - Acceptance: Parent entity visible for documents with Matter/Project association

10. **FR-10**: Ribbon Button Command
    - "Find Related" button on sprk_document form ribbon
    - JavaScript handler (invocation only, no business logic)
    - Opens DocumentRelationshipViewer modal
    - Acceptance: Button visible on form, opens modal correctly

### Non-Functional Requirements

- **NFR-01**: API latency < 500ms at P95 for up to 50 nodes
- **NFR-02**: Graph render time < 200ms for up to 100 nodes
- **NFR-03**: PCF bundle size < 5MB (excluding platform libraries)
- **NFR-04**: Support light, dark, and high-contrast modes
- **NFR-05**: WCAG 2.1 AA accessibility compliance (keyboard navigation, ARIA labels)
- **NFR-06**: Apply `ai-standard` rate limit policy to visualization endpoints
- **NFR-07**: Redis caching for embeddings with 7-day TTL (existing infrastructure)
- **NFR-08**: Multi-tenant isolation via tenantId OData filter on all queries

## Technical Constraints

### Applicable ADRs

| ADR | Requirement | Application |
|-----|-------------|-------------|
| **ADR-006** | PCF over webresources | DocumentRelationshipViewer as PCF control |
| **ADR-008** | Endpoint filters for authorization | VisualizationAuthorizationFilter on /related/{documentId} |
| **ADR-009** | Redis-first caching | Embedding cache with existing infrastructure |
| **ADR-013** | AI Architecture | Extend BFF API, no separate microservice |
| **ADR-021** | Fluent UI v9 Design System | All UI components, dark mode, tokens |
| **ADR-022** | PCF Platform Libraries | React 16 APIs, platform-library declarations |

### MUST Rules (from ADRs)

- **MUST** build DocumentRelationshipViewer as PCF control, not webresource
- **MUST** keep ribbon JavaScript minimal (invocation only, no business logic)
- **MUST** use endpoint filters for document authorization, not global middleware
- **MUST** use `IDistributedCache` (Redis) for embedding caching
- **MUST** use `@fluentui/react-components` (v9) exclusively
- **MUST** use React 16 APIs (`ReactDOM.render`, not `createRoot`)
- **MUST** declare `platform-library` in PCF manifest for React and Fluent
- **MUST** support light, dark, and high-contrast themes via Fluent tokens
- **MUST** use `makeStyles` (Griffel) for custom styling
- **MUST** apply `ai-standard` rate limit policy to endpoints

### MUST NOT Rules

- **MUST NOT** create legacy JavaScript webresources with business logic
- **MUST NOT** create global middleware for resource authorization
- **MUST NOT** call Azure AI services directly from PCF (use BFF API)
- **MUST NOT** use Fluent v8 (`@fluentui/react`)
- **MUST NOT** hard-code colors (use Fluent design tokens)
- **MUST NOT** use React 18 APIs (`createRoot`, concurrent features)
- **MUST NOT** bundle React/Fluent in PCF output
- **MUST NOT** cache authorization decisions (cache data only)
- **MUST NOT** create separate AI microservice

### Existing Patterns to Follow

- See `src/server/api/Sprk.Bff.Api/Api/Ai/` for AI endpoint registration patterns
- See `.claude/patterns/api/endpoint-filters.md` for authorization filter patterns
- See `.claude/patterns/pcf/` for PCF control initialization and theming
- See `.claude/patterns/ai/` for AI service patterns

### Component Reuse Requirements

| Capability | Existing Component | Reuse Strategy |
|------------|-------------------|----------------|
| SPE File Viewer | Existing PCF control | Open via `fileUrl` in new tab |
| Dataverse Navigation | Standard Xrm.Navigation | Use `Xrm.Navigation.openForm()` |
| Document Record | Existing form | Navigate to existing `sprk_document` form |
| Authentication | Existing BFF auth | JWT tokens via existing auth flow |
| Error Handling | Standard patterns | Use existing error handling utilities |
| R3 RAG Service | `IRagService` | Use for vector search operations |
| Embedding Cache | `IEmbeddingCache` | Use existing Redis-based cache |

## Success Criteria

1. [ ] `GET /api/ai/visualization/related/{documentId}` returns valid graph response - Verify: API tests pass
2. [ ] DocumentRelationshipViewer PCF control renders graph with d3-force layout - Verify: Component tests pass
3. [ ] Full-screen modal opens from ribbon button on sprk_document form - Verify: E2E test in Dataverse
4. [ ] Control panel filters update graph in real-time - Verify: User acceptance testing
5. [ ] Node actions navigate to Dataverse record and SPE file correctly - Verify: Manual testing
6. [ ] Graph respects depth limiting (max 3 levels, 100 nodes) - Verify: Load testing
7. [ ] Dark mode fully supported via Fluent v9 tokens - Verify: Visual testing
8. [ ] API latency < 500ms P95, render < 200ms - Verify: Performance testing
9. [ ] Integration tests pass against Azure AI Search dev environment - Verify: CI pipeline
10. [ ] All existing indexed documents have `documentVector` after backfill - Verify: Index query

## Dependencies

### Prerequisites

- Azure AI Search index exists with document chunks indexed
- Azure OpenAI text-embedding-3-small model deployed
- Redis cache infrastructure operational
- R3 RAG architecture deployed (IRagService, IEmbeddingCache)
- `sprk_document` entity with existing extract fields (`sprk_extractpeople`, `sprk_extractorganization`)
- Existing `ai-standard` rate limit policy defined in BFF API

### External Dependencies

- `@xyflow/react` (React Flow) npm package for graph visualization
- `d3-force` npm package for force-directed layout
- Azure AI Search REST API for vector queries
- Azure OpenAI API for embedding generation

## Owner Clarifications

*Answers captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Index Migration | Should existing indexed documents be backfilled with document-level embeddings? | Yes, backfill existing data | Migration task required in Phase 1 |
| Rate Limiting | Is 'ai-standard' rate limit policy already implemented? | Already exists | Apply existing policy to new endpoints |
| Testing Scope | Should integration tests against Azure AI Search be included? | Yes, include integration tests | Add integration tests to Phase 2+ |

## Assumptions

*Proceeding with these assumptions (design document specified or implied):*

- **Similarity threshold**: Default 65% is appropriate; may need tuning based on real data
- **Node limit**: 25 per level is reasonable default; user can adjust via slider
- **Depth default**: 1 level for initial load is correct for performance
- **File preview**: Opens in new browser tab (not inline preview)
- **Error handling**: Standard toast notifications and inline errors per existing patterns
- **Loading states**: Standard Fluent UI v9 spinners and skeletons

## Unresolved Questions

*No blocking questions remain. All design decisions confirmed in DESIGN.md Section 12.*

## Implementation Phases

### Phase 1: Core Infrastructure
- Create `IVisualizationService` interface and implementation
- Implement `GET /api/ai/visualization/related/{documentId}` endpoint
- Add document-level embedding support to indexing pipeline
- Create `VisualizationAuthorizationFilter` endpoint filter
- Backfill existing indexed documents with `documentVector`
- Unit tests with mocked dependencies

### Phase 2: PCF Control Development
- Scaffold `DocumentRelationshipViewer` PCF control
- Integrate @xyflow/react with d3-force layout
- Implement Fluent UI v9 node components (light/dark mode)
- Create control panel with filters and settings
- Implement node action bar
- Component tests with React Testing Library
- Integration tests against Azure AI Search dev environment

### Phase 3: Integration & Ribbon Button
- Register PCF control on `sprk_document` entity
- Create ribbon button command with JavaScript handler
- Implement modal dialog launcher
- End-to-end testing in Dataverse environment

### Phase 4: Polish & Advanced Features
- Add export functionality (PNG, JSON, CSV)
- Performance optimization (lazy loading, prefetch)
- Accessibility audit and fixes
- User documentation

---

*AI-optimized specification. Original design: DESIGN.md*
