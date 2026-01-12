# Project Plan: AI Search & Visualization Module

> **Last Updated**: 2026-01-08
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Deliver an AI-powered document relationship visualization module that enables users to discover semantically similar documents and explore relationships through an interactive graph interface within the Spaarke Power Apps model-driven application.

**Scope**:
- BFF API endpoint for finding related documents
- DocumentRelationshipViewer PCF control with React Flow canvas
- Document-level embedding storage and backfill migration
- "Find Related" ribbon button integration
- Unit, component, and integration tests

**Estimated Effort**: 12-18 days

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-006**: Use PCF controls for all new UI, no legacy JavaScript webresources
- **ADR-008**: Use endpoint filters for authorization, not global middleware
- **ADR-009**: Redis-first caching, no hybrid L1+L2 without profiling proof
- **ADR-013**: Extend BFF API, no separate AI microservice, no Azure Functions
- **ADR-021**: Fluent UI v9 exclusively, dark mode required, use design tokens
- **ADR-022**: React 16 APIs in PCF (`ReactDOM.render`), platform-library declarations

**From Spec**:
- API latency < 500ms P95, graph render < 200ms
- Maximum 100 visible nodes, default 25 per level
- Similarity threshold default 65%, depth default 1 level
- Reuse existing SPE viewer, auth flows, navigation patterns

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Continue R3 architecture | SharePoint Embedded not supported by Foundry IQ | Use existing IRagService, IEmbeddingCache |
| Full-screen modal | Maximum canvas area for complex graphs | Better UX for exploration |
| d3-force layout | Natural clustering, interactive exploration | Similarity = edge distance |
| Dedicated documentVector | Optimal query performance | Backfill existing documents |

### Discovered Resources

**Applicable ADRs**:
- `.claude/adr/ADR-006-pcf-over-webresources.md` - PCF over webresources
- `.claude/adr/ADR-008-endpoint-filters.md` - Endpoint filters for auth
- `.claude/adr/ADR-009-redis-caching.md` - Redis-first caching
- `.claude/adr/ADR-013-ai-architecture.md` - AI architecture patterns
- `.claude/adr/ADR-021-fluent-design-system.md` - Fluent UI v9 requirements
- `.claude/adr/ADR-022-pcf-platform-libraries.md` - PCF platform libraries

**Applicable Skills**:
- `.claude/skills/dataverse-deploy/` - For PCF deployment
- `.claude/skills/ribbon-edit/` - For ribbon customization
- `.claude/skills/ui-test/` - For browser-based testing
- `.claude/skills/code-review/` - Quality gate
- `.claude/skills/adr-check/` - ADR validation

**Pattern Files**:
- `.claude/patterns/api/endpoint-definition.md` - API endpoint patterns
- `.claude/patterns/api/endpoint-filters.md` - Authorization filter patterns
- `.claude/patterns/pcf/control-initialization.md` - PCF control setup
- `.claude/patterns/pcf/theme-management.md` - Fluent UI theming
- `.claude/patterns/ai/` - AI service patterns
- `.claude/patterns/testing/` - Testing patterns

**Constraint Files**:
- `.claude/constraints/api.md` - API constraints
- `.claude/constraints/pcf.md` - PCF constraints
- `.claude/constraints/ai.md` - AI feature constraints
- `.claude/constraints/testing.md` - Testing constraints

**Available Scripts**:
- `scripts/Deploy-PCFWebResources.ps1` - PCF deployment
- `scripts/Export-EntityRibbon.ps1` - Ribbon export
- `scripts/Test-SdapBffApi.ps1` - API testing

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Core Infrastructure (Days 1-5)
|- Backend API endpoint and service
|- Document-level embedding support
|- Backfill migration
|- Unit tests

Phase 2: PCF Control Development (Days 6-10)
|- DocumentRelationshipViewer control scaffold
|- React Flow + d3-force integration
|- Fluent UI v9 components
|- Control panel and action bar
|- Component tests

Phase 3: Integration & Ribbon (Days 11-13)
|- PCF registration on sprk_document
|- Ribbon button and modal launcher
|- E2E testing

Phase 4: Polish & Documentation (Days 14-15)
|- Export functionality
|- Performance optimization
|- Accessibility audit
|- Documentation
```

### Critical Path

**Blocking Dependencies:**
- Phase 2 BLOCKED BY Phase 1 (API must exist for PCF to consume)
- Phase 3 BLOCKED BY Phase 2 (PCF must be built before ribbon integration)
- Phase 4 can partially overlap with Phase 3

**High-Risk Items:**
- Graph rendering performance with 100+ nodes - Mitigation: depth limiting, virtualization
- React Flow bundle size - Mitigation: dynamic imports, tree-shaking
- Dark mode compatibility - Mitigation: use only Fluent v9 tokens, no hard-coded colors

---

## 4. Phase Breakdown

### Phase 1: Core Infrastructure

**Objectives:**
1. Create VisualizationService and API endpoint
2. Add document-level embedding support to index schema
3. Backfill existing documents with documentVector
4. Implement authorization filter
5. Write unit tests with mocked dependencies

**Deliverables:**
- [ ] `IVisualizationService` interface and implementation
- [ ] `GET /api/ai/visualization/related/{documentId}` endpoint
- [ ] `VisualizationAuthorizationFilter` endpoint filter
- [ ] Azure AI Search index schema update (documentVector field)
- [ ] Backfill migration script/job
- [ ] Unit tests for VisualizationService

**Critical Tasks:**
- Index schema update MUST BE FIRST (other tasks depend on it)
- Backfill can run in parallel with other development

**Inputs**:
- spec.md functional requirements FR-01, FR-02
- Existing IRagService, IEmbeddingCache
- Azure AI Search index

**Outputs**:
- Working API endpoint returning graph data
- All existing documents with documentVector

---

### Phase 2: PCF Control Development

**Objectives:**
1. Scaffold DocumentRelationshipViewer PCF control
2. Integrate React Flow with d3-force layout
3. Implement Fluent UI v9 components with dark mode
4. Create control panel with filters
5. Implement node action bar
6. Write component tests

**Deliverables:**
- [ ] DocumentRelationshipViewer PCF control scaffold
- [ ] React Flow canvas with d3-force layout
- [ ] DocumentNode and DocumentEdge components (Fluent v9)
- [ ] Control panel (sliders, checkboxes)
- [ ] Node action bar (Open Record, View File, Expand)
- [ ] Full-screen modal wrapper
- [ ] Component tests with React Testing Library
- [ ] Integration tests against Azure AI Search

**Critical Tasks:**
- PCF scaffold MUST BE FIRST
- React Flow integration before component styling

**Inputs**:
- spec.md functional requirements FR-03 through FR-09
- ADR-021 (Fluent UI v9), ADR-022 (Platform libraries)
- Working API from Phase 1

**Outputs**:
- Working PCF control rendering graph
- Passing component tests

---

### Phase 3: Integration & Ribbon Button

**Objectives:**
1. Register PCF control on sprk_document entity
2. Create ribbon button with JavaScript handler
3. Implement modal dialog launcher
4. End-to-end testing in Dataverse

**Deliverables:**
- [ ] PCF control registered on sprk_document form
- [ ] "Find Related" ribbon button command
- [ ] JavaScript ribbon handler (invocation only)
- [ ] Modal dialog launcher integration
- [ ] E2E tests in Dataverse environment

**Critical Tasks:**
- PCF registration MUST BE FIRST
- Ribbon button depends on modal working

**Inputs**:
- Working PCF control from Phase 2
- sprk_document entity form
- Existing ribbon patterns

**Outputs**:
- Ribbon button visible and functional
- Modal opens with correct document context

---

### Phase 4: Polish & Advanced Features

**Objectives:**
1. Add export functionality (PNG, JSON, CSV)
2. Performance optimization
3. Accessibility audit and fixes
4. User documentation

**Deliverables:**
- [ ] Export to PNG, JSON, CSV
- [ ] Lazy loading / prefetch optimization
- [ ] Accessibility audit (WCAG 2.1 AA)
- [ ] Keyboard navigation fixes
- [ ] User documentation

**Critical Tasks:**
- Export functionality can be parallel with optimization
- Accessibility audit should be last (after all UI complete)

**Inputs**:
- Complete PCF control from Phase 3
- Accessibility requirements from NFR-05

**Outputs**:
- Polished, accessible, documented feature

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Azure AI Search | GA | Low | Existing infrastructure |
| Azure OpenAI | GA | Low | text-embedding-3-small deployed |
| @xyflow/react | Stable | Low | Well-maintained, active community |
| d3-force | Stable | Low | Industry standard |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| R3 RAG architecture | `src/server/api/` | Production |
| IRagService | `src/server/api/Services/Ai/` | Production |
| IEmbeddingCache | `src/server/api/Services/Ai/` | Production |
| sprk_document entity | Dataverse | Production |
| ai-standard rate limit policy | BFF API | Existing |

---

## 6. Testing Strategy

**Unit Tests** (80% coverage target):
- VisualizationService graph building logic
- Similarity scoring and edge creation
- Depth limiting and node capping
- Authorization filter behavior

**Integration Tests**:
- API endpoint with Azure AI Search (dev environment)
- Vector search with real embeddings
- Multi-tenant isolation (tenantId filter)

**Component Tests**:
- DocumentNode renders with correct styling
- Control panel updates trigger API calls
- Node selection shows action bar
- Dark mode renders correctly

**E2E Tests**:
- Ribbon button opens modal
- Graph loads for document
- Filters work correctly
- Navigation to Dataverse record works
- Navigation to SPE file works

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1:**
- [ ] API returns valid graph response (nodes + edges)
- [ ] API latency < 500ms P95
- [ ] All existing documents have documentVector after backfill
- [ ] Unit tests pass with 80% coverage

**Phase 2:**
- [ ] Graph renders within 200ms for 100 nodes
- [ ] Dark mode fully functional (no hard-coded colors)
- [ ] Control panel filters update graph correctly
- [ ] Component tests pass
- [ ] Integration tests pass

**Phase 3:**
- [ ] Ribbon button visible on sprk_document form
- [ ] Modal opens with correct document context
- [ ] Node actions navigate correctly

**Phase 4:**
- [ ] Export functions work (PNG, JSON, CSV)
- [ ] WCAG 2.1 AA accessibility compliance
- [ ] Documentation complete

### Business Acceptance

- [ ] Users can discover related documents they couldn't find before
- [ ] Graph visualization is intuitive and useful
- [ ] Performance meets interactive UX expectations

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Graph performance degrades with large node counts | Medium | High | Depth limiting, hard cap at 100 nodes, virtualization |
| R2 | React Flow bundle increases PCF size beyond 5MB | Low | Medium | Dynamic imports, tree-shaking, verify bundle size |
| R3 | d3-force layout causes jank on slow devices | Low | Medium | Limit simulation iterations, use requestAnimationFrame |
| R4 | Dark mode colors don't match Fluent design | Low | Low | Use only Fluent v9 tokens, no hard-coded colors |
| R5 | Backfill migration takes too long | Medium | Medium | Run during off-hours, batch processing |

---

## 9. Next Steps

1. **Review this plan.md** for accuracy
2. **Generate task files** via project-pipeline Step 3
3. **Create feature branch** with initial commit
4. **Begin Phase 1** with index schema update

---

**Status**: Ready for Tasks
**Next Action**: Generate task files from this plan

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
