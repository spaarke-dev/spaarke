# Task Index: AI Search & Visualization Module

> **Project**: ai-azure-search-module
> **Last Updated**: 2026-01-10
> **Total Tasks**: 44

---

## Quick Status

| Phase | Status | Progress |
|-------|--------|----------|
| Phase 1: Core Infrastructure | Complete | 8/8 |
| Phase 2: PCF Control Development | Complete | 10/10 |
| Phase 3: Integration & Ribbon | In Progress | 1/3 (2 deferred) |
| Phase 4: Polish & Documentation | Not Started | 0/4 |
| **Phase 5: Schema Migration** | **In Progress** | **13/16** |
| Wrap-up | Not Started | 0/1 |

---

## All Tasks

| ID | Title | Phase | Status | Dependencies | Rigor |
|----|-------|-------|--------|--------------|-------|
| 001 | Update Azure AI Search index schema | 1 | ✅ | none | FULL |
| 002 | Create IVisualizationService interface | 1 | ✅ | none | FULL |
| 003 | Implement VisualizationService | 1 | ✅ | 001, 002 | FULL |
| 004 | Create VisualizationAuthorizationFilter | 1 | ✅ | none | FULL |
| 005 | Create VisualizationEndpoints | 1 | ✅ | 003, 004 | FULL |
| 006 | Backfill existing documents | 1 | ✅ | 001 | STANDARD |
| 007 | Unit tests for VisualizationService | 1 | ✅ | 003 | STANDARD |
| 008 | Deploy Phase 1 API | 1 | ✅ | 005, 007 | STANDARD |
| 010 | Scaffold DocumentRelationshipViewer PCF | 2 | ✅ | none | FULL |
| 011 | Integrate React Flow with d3-force | 2 | ✅ | 010 | FULL |
| 012 | Implement DocumentNode component | 2 | ✅ | 011 | FULL |
| 013 | Implement DocumentEdge component | 2 | ✅ | 011 | FULL |
| 014 | Implement control panel | 2 | ✅ | 012, 013 | FULL |
| 015 | Implement node action bar | 2 | ✅ | 012 | FULL |
| 016 | Implement full-screen modal | 2 | ✅ | 014, 015 | FULL |
| 017 | Component tests for PCF control | 2 | ✅ | 016 | STANDARD |
| 018 | Integration tests with Azure AI Search | 2 | ✅ | 005, 016 | STANDARD |
| 019 | Deploy Phase 2 PCF | 2 | ✅ | 017, 018 | STANDARD |
| 020 | Register PCF on sprk_document form | 3 | ✅ | 019 | FULL |
| 021 | Create ribbon button command | 3 | ⏭️ | 020 | FULL |
| 022 | Implement modal dialog launcher | 3 | ⏭️ | 021 | FULL |
| 023 | End-to-end testing in Dataverse | 3 | 🔲 | 020 | STANDARD |
| 024 | Deploy Phase 3 ribbon | 3 | 🔲 | 023 | STANDARD |
| 030 | Implement export functionality | 4 | 🔲 | 024 | FULL |
| 031 | Performance optimization | 4 | 🔲 | 024 | STANDARD |
| 032 | Accessibility audit and fixes | 4 | 🔲 | 030, 031 | STANDARD |
| 033 | Create user documentation | 4 | 🔲 | 032 | MINIMAL |
| **025** | **Fix Azure index configuration** | **5-fix** | **✅** | **none** | **STANDARD** |
| 040 | Create RAG index v2 schema JSON | 5a | ✅ | 025 | FULL |
| 041 | Add migration fields to existing index | 5a | ✅ | 040 | STANDARD |
| 042 | Update KnowledgeDocument model | 5a | ✅ | 041 | FULL |
| 043 | Update RagService indexing | 5a | ✅ | 042 | FULL |
| 050 | Update embedding configuration | 5b | ✅ | 043 | STANDARD |
| 051 | Add 3072-dim vector fields | 5b | ✅ | 050 | STANDARD |
| 052 | Create EmbeddingMigrationService | 5b | ✅ | 051 | FULL |
| 053 | Run embedding migration | 5b | ✅ | 052 | STANDARD |
| 060 | Update VisualizationService | 5c | ✅ | 053 | FULL |
| 061 | Update visualization API DTOs | 5c | ✅ | 060 | STANDARD |
| 062 | Update PCF types | 5c | ✅ | 061 | STANDARD |
| 063 | Update DocumentNode icons | 5c | ✅ | 062 | STANDARD |
| 064 | Unit tests for new scenarios | 5c | 🔲 | 060-063 | STANDARD |
| 070 | Remove deprecated fields | 5d | 🔲 | 064 | STANDARD |
| 071 | Update Azure configuration | 5d | 🔲 | 070 | STANDARD |
| 072 | E2E regression testing | 5d | 🔲 | 071 | STANDARD |
| 090 | Project wrap-up | wrap | 🔲 | 072 | FULL |

---

## Status Legend

| Symbol | Meaning |
|--------|---------|
| 🔲 | Not started |
| 🔄 | In progress |
| ⏸️ | Blocked |
| ✅ | Completed |
| ⏭️ | Deferred |

---

## Critical Path

```
Phase 1-4 (Complete/In Progress):
001-008 (API infra) ──→ 010-019 (PCF) ──→ 020-024 (ribbon) ──→ 030-033 (polish)

Phase 5 (Schema Migration - Unblocks Visualization):
025 (fix config) ──→ 040-043 (schema extension) ──→ 050-053 (embedding migration)
                                                              │
                                                              ↓
                                                    060-064 (service updates)
                                                              │
                                                              ↓
                                                    070-072 (cutover) ──→ 090 (wrap-up)

IMMEDIATE PRIORITY: Task 025 unblocks visualization testing
```

---

## Phase Details

### Phase 1: Core Infrastructure (Days 1-5)
**Goal**: Working API endpoint with document-level embeddings

| Task | Est Hours | Dependencies |
|------|-----------|--------------|
| 001 - Update Azure AI Search index schema | 2h | none |
| 002 - Create IVisualizationService interface | 1h | none |
| 003 - Implement VisualizationService | 4h | 001, 002 |
| 004 - Create VisualizationAuthorizationFilter | 2h | none |
| 005 - Create VisualizationEndpoints | 3h | 003, 004 |
| 006 - Backfill existing documents | 3h | 001 |
| 007 - Unit tests for VisualizationService | 3h | 003 |
| 008 - Deploy Phase 1 API | 2h | 005, 007 |

### Phase 2: PCF Control Development (Days 6-10)
**Goal**: Working visualization control with all UI components

| Task | Est Hours | Dependencies |
|------|-----------|--------------|
| 010 - Scaffold DocumentRelationshipViewer PCF | 2h | none |
| 011 - Integrate React Flow with d3-force | 4h | 010 |
| 012 - Implement DocumentNode component | 3h | 011 |
| 013 - Implement DocumentEdge component | 2h | 011 |
| 014 - Implement control panel | 3h | 012, 013 |
| 015 - Implement node action bar | 2h | 012 |
| 016 - Implement full-screen modal | 3h | 014, 015 |
| 017 - Component tests for PCF control | 3h | 016 |
| 018 - Integration tests with Azure AI Search | 3h | 005, 016 |
| 019 - Deploy Phase 2 PCF | 2h | 017, 018 |

### Phase 3: Integration & Ribbon (Days 11-13)
**Goal**: Ribbon button opens modal with full functionality

| Task | Est Hours | Dependencies |
|------|-----------|--------------|
| 020 - Register PCF on sprk_document form | 2h | 019 |
| 021 - Create ribbon button command | 2h | 020 |
| 022 - Implement modal dialog launcher | 3h | 021 |
| 023 - End-to-end testing in Dataverse | 3h | 022 |
| 024 - Deploy Phase 3 ribbon | 2h | 023 |

### Phase 4: Polish & Documentation (Days 14-15)
**Goal**: Production-ready feature with documentation

| Task | Est Hours | Dependencies |
|------|-----------|--------------|
| 030 - Implement export functionality | 4h | 024 |
| 031 - Performance optimization | 3h | 024 |
| 032 - Accessibility audit and fixes | 3h | 030, 031 |
| 033 - Create user documentation | 2h | 032 |

### Phase 5: Schema Migration (Unblocks Visualization)
**Goal**: Fix index schema mismatch, standardize on 3072-dim embeddings, support orphan files

**Background**: Investigation of 500 errors revealed visualization was misconfigured to use `spaarke-records-index` (Record Matching) instead of RAG index. Additionally, user clarified:
- "Documents" = `sprk_document` Dataverse records (metadata)
- "Files" = SharePoint Embedded files (content)
- Similarity is by FILE content vectors, not Document metadata
- Files are ALWAYS linked through `sprk_document`, never directly to other entities
- Orphan files (no linked Document) should be supported

**Phase 5-fix: Immediate Fix**
| Task | Est Hours | Dependencies |
|------|-----------|--------------|
| 025 - Fix Azure index configuration | 1h | none |

**Phase 5a: Schema Extension**
| Task | Est Hours | Dependencies |
|------|-----------|--------------|
| 040 - Create RAG index v2 schema JSON | 2h | 025 |
| 041 - Add migration fields to existing index | 3h | 040 |
| 042 - Update KnowledgeDocument model | 2h | 041 |
| 043 - Update RagService indexing | 3h | 042 |

**Phase 5b: Embedding Migration**
| Task | Est Hours | Dependencies |
|------|-----------|--------------|
| 050 - Update embedding configuration | 2h | 043 |
| 051 - Add 3072-dim vector fields | 2h | 050 |
| 052 - Create EmbeddingMigrationService | 6h | 051 |
| 053 - Run embedding migration | 4h | 052 |

**Phase 5c: Service Updates**
| Task | Est Hours | Dependencies |
|------|-----------|--------------|
| 060 - Update VisualizationService | 4h | 053 |
| 061 - Update visualization API DTOs | 2h | 060 |
| 062 - Update PCF types | 2h | 061 |
| 063 - Update DocumentNode icons | 2h | 062 |
| 064 - Unit tests for new scenarios | 4h | 060-063 |

**Phase 5d: Cutover**
| Task | Est Hours | Dependencies |
|------|-----------|--------------|
| 070 - Remove deprecated fields | 3h | 064 |
| 071 - Update Azure configuration | 2h | 070 |
| 072 - E2E regression testing | 4h | 071 |

### Wrap-up
| Task | Est Hours | Dependencies |
|------|-----------|--------------|
| 090 - Project wrap-up | 2h | 072 |

---

## Execution Notes

**Phase 1 Complete (2026-01-09):**
- All 8 Phase 1 tasks completed successfully
- Kiota package mismatch resolved by adding explicit package references
- API deployed to: https://spe-api-dev-67e2xz.azurewebsites.net
- Visualization endpoint live: /api/ai/visualization/related/{documentId}

**Phase 2 Started (2026-01-09):**
- Task 010: Scaffolded DocumentRelationshipViewer PCF control
- Platform libraries: React 16.14.0, Fluent 9.46.2 (externalized per ADR-022)
- Control uses ReactControl interface (returns React elements from updateView)
- FluentProvider wrapper with theme resolution for dark mode support (ADR-021)
- Task 011: Integrated react-flow-renderer v10 (React 16 compatible) + d3-force
- useForceLayout hook calculates positions: edge distance = 200 * (1 - similarity)
- DocumentGraph component with Background, Controls, MiniMap
- Sample data generates 8 nodes with varying similarity for testing
- Bundle size: 1.4 MiB (includes react-flow-renderer + d3-force)
- Task 012: Implemented DocumentNode component with Fluent UI v9 Card
- File type icons (PDF, DOCX, images, etc.) using @fluentui/react-icons
- Source node: filled brand background with "Source Document" badge
- Related nodes: outline style with similarity badge (color-coded)
- Task 013: Implemented DocumentEdge component with similarity-based styling
- High similarity (≥90%): thick green edge with green label
- Medium similarity (≥75%): medium blue edge with brand label
- Low similarity (≥65%): thin yellow edge with warning label
- Very low (<65%): thin dashed gray edge with subtle label
- Bundle size: 2.15 MiB (includes react-flow-renderer + d3-force + icons)
- Task 014: Implemented ControlPanel component with Fluent UI v9
- Similarity threshold slider (50-95%, step 5, default 65%)
- Depth limit slider (1-3 levels, default 1)
- Max nodes per level slider (10-50, step 5, default 25)
- Document type filter checkboxes (PDF, DOCX, XLSX, PPTX, TXT, Other)
- Active filters badge shows count of non-default settings
- All controls use Fluent tokens for dark mode support
- Task 015: Implemented NodeActionBar component with Fluent UI v9
- Open Document Record button (Xrm.Navigation.openForm)
- View in SharePoint button (window.open with fileUrl)
- Expand button (callback to load next level)
- Header shows document name and parent entity
- Expand hidden for source nodes (cannot expand source)
- Task 016: Implemented RelationshipViewerModal full-screen modal
- Overlay with z-index: 10000 for Dataverse form compatibility
- Header with title, subtitle (source document name), close button
- Left sidebar (300px) with ControlPanel
- Main canvas with DocumentGraph and NodeActionBar overlay
- Footer with stats (nodes/edges) and version
- Escape key and click-outside-to-close handlers
- Task 017: Implemented comprehensive component tests with Jest and React Testing Library
- Added @testing-library/react v12.1.5 (React 16 compatible)
- Jest 29 with ts-jest and jsdom environment
- DocumentNode.test.tsx: 15 tests (source/related rendering, similarity, file types)
- ControlPanel.test.tsx: 18 tests (sliders, checkboxes, active filters badge)
- NodeActionBar.test.tsx: 20 tests (Xrm.Navigation mocks, window.open, callbacks)
- DocumentGraph.test.tsx: 21 tests (React Flow mocked for performance)
- Global Xrm mock in jest.setup.ts for Dataverse navigation testing
- All 74 tests pass, covering key user interactions and rendering
- Task 018: Integration tests with Azure AI Search completed
- Created VisualizationIntegrationTests.cs with 23 response structure validation tests
- Tests validate: DocumentGraphResponse, DocumentNode, DocumentEdge, GraphMetadata, VisualizationOptions
- Unit tests for VisualizationService (19 tests) already exist in Sprk.Bff.Api.Tests
- Total visualization tests: 42 tests (19 unit + 23 integration structure tests)
- Integration tests don't require Azure infrastructure (validates model contracts)
- Task 019: Deployed DocumentRelationshipViewer PCF v1.0.1 to Dataverse
- Used pac pcf push --publisher-prefix sprk (quick dev deploy method)
- Control deployed to spaarkedev1.crm.dynamics.com in PowerAppsToolsTemp_sprk solution
- Bundle size: 2.15 MiB with React 16.14.0 and Fluent 9.46.2 externalized
- Control namespace: Spaarke.Controls.DocumentRelationshipViewer

**Phase 2 Complete (2026-01-09):**
- All 10 Phase 2 tasks completed successfully
- PCF control deployed to Dataverse dev environment
- Ready for Phase 3: Integration & Ribbon

**Phase 3 Started (2026-01-09):**
- Task 020: Registered DocumentRelationshipViewer PCF on sprk_document main form
- Control added to "Search" tab as virtual control bound to document ID
- Configured: documentId (bound), tenantId (static), apiBaseUrl (static)
- Control renders with header "Document Relationships", version v1.0.1 in footer
- Placeholder message displayed when no document context available

**API Integration (2026-01-10):**
- Identified critical gap: PCF used dummy data (`generateSampleData`) instead of calling BFF API
- Created `types/api.ts` - Type definitions mirroring IVisualizationService.cs response models
- Created `services/VisualizationApiService.ts` - API fetch with type mapping (API → PCF graph types)
- Created `hooks/useVisualizationApi.ts` - React hook managing loading/error/data states
- Updated `DocumentRelationshipViewer.tsx` - Integrated hook, removed sample data generator
- Deployed v1.0.17 to Dataverse with full API integration
- API endpoint: `/api/ai/visualization/related/{documentId}?tenantId={tenantId}`

**Architecture Decision - Tasks 021-022 Deferred:**
- Tasks 021 (ribbon button) and 022 (modal launcher) are NO LONGER NEEDED
- Original design: Modal-based visualization launched from ribbon button
- Implemented design: Section-based visualization embedded directly in form
- Control renders inline in "Search" tab, no modal or ribbon button required
- Skip directly to Task 023 (e2e testing) then Task 024 (deployment)

**Phase 5 Created (2026-01-10):**
- Investigation of 500 errors in visualization revealed schema mismatch
- `Analysis__SharedIndexName` was pointing to `spaarke-records-index` (Record Matching) instead of RAG index
- User clarified terminology: "Documents" = Dataverse records, "Files" = SPE content
- User confirmed: similarity is by FILE content (full info), NOT Document metadata (limited)
- User decision: Standardize on 3072 dimensions, extend RAG schema, support orphan files
- Created comprehensive analysis: `notes/024-index-schema-unification.md`
- Created 16 new tasks across 4 sub-phases (5a, 5b, 5c, 5d)
- Task 025 is critical path to unblock visualization testing

**Current Blockers:**
- Visualization 500 errors until Task 025 completes (fix Azure config)

**Parallel Execution Opportunities:**
- Tasks 001, 002, 004 can run in parallel (no dependencies)
- Tasks 012, 013 can run in parallel (both depend on 011)
- Tasks 030, 031 can run in parallel (both depend on 024)

**Deployment Tasks:**
- 008: Deploy BFF API to Azure (after Phase 1)
- 019: Deploy PCF to Dataverse (after Phase 2)
- 024: Deploy ribbon customization (after Phase 3)

---

*Updated automatically by task-execute skill*
