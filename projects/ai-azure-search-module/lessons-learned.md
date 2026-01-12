# AI Search & Visualization Module - Lessons Learned

> **Project**: ai-azure-search-module
> **Completed**: 2026-01-12
> **Duration**: 5 days (2026-01-08 to 2026-01-12)

---

## Executive Summary

This project delivered an AI-powered document relationship visualization module for the Spaarke platform. The implementation successfully integrated Azure AI Search with a PCF control for interactive graph visualization. Key achievements include a 3072-dimension vector migration, orphan file support, and comprehensive test coverage.

---

## What Went Well

### 1. Phased Implementation Approach

Breaking the project into 5 clear phases (Infrastructure, PCF, Integration, Polish, Schema Migration) allowed for:
- Clear progress tracking with 44 tasks across phases
- Parallel work opportunities identified early
- Clean dependency management between components

### 2. Early Discovery of Schema Issues (Phase 5)

The investigation of 500 errors in Phase 3 testing revealed a critical schema mismatch:
- Visualization was misconfigured to use wrong index (`spaarke-records-index` instead of RAG index)
- Discovery early allowed for comprehensive Phase 5 schema migration
- Result: Clean architecture with 3072-dim vectors and orphan file support

### 3. ADR-Driven Development

Following established ADRs (006, 008, 009, 013, 021, 022) prevented common pitfalls:
- ADR-021: Fluent UI v9 with dark mode support worked seamlessly
- ADR-022: Platform libraries externalization reduced bundle from 24MB to 6.6MB
- ADR-008: Endpoint filters provided clean authorization pattern

### 4. Comprehensive Test Coverage

- **85 .NET unit tests** covering VisualizationService, RagService, embedding scenarios
- **40 PCF component tests** covering DocumentNode, ControlPanel, NodeActionBar, DocumentGraph
- **18 E2E tests** validating the full pipeline
- Test-driven approach caught several edge cases (orphan files, 1536/3072 fallback)

### 5. Documentation-First Design

Starting with spec.md and plan.md before implementation:
- Clarified terminology confusion (Documents vs Files)
- Identified architectural decisions early
- Enabled smooth task decomposition

---

## What Could Be Improved

### 1. Index Configuration Management

The critical bug (wrong index name in Azure configuration) highlights need for:
- **Recommendation**: Add configuration validation at startup
- **Recommendation**: Include index name in health check response
- **Recommendation**: Environment-specific config validation tests

### 2. Earlier Integration Testing

500 errors weren't discovered until Phase 3 testing:
- **Recommendation**: Add smoke test in Phase 1 that validates full pipeline
- **Recommendation**: Include actual Azure AI Search calls in integration tests (not just mocks)

### 3. Schema Design Iteration

The schema went through 3 iterations (original, v2 fields, cutover):
- **Recommendation**: Design for orphan files from the start
- **Recommendation**: Use 3072 dimensions by default (not migrate later)
- **Recommendation**: Include `speFileId` as required field in initial design

### 4. Modal vs Section Pivot

Original design called for ribbon button + modal, implemented as inline section:
- Lost: Full-screen immersive experience for complex graphs
- Gained: Simpler integration, no ribbon customization needed
- **Recommendation**: Validate UX approach with stakeholders earlier

---

## Technical Decisions That Worked

### 1. d3-force Layout Algorithm

- Natural clustering based on similarity scores
- Edge distance = `200 * (1 - similarity)` created intuitive visual relationships
- Interactive dragging and zoom worked well for exploration

### 2. Dual Vector Field Strategy

Supporting both `contentVector3072` (chunk) and `documentVector3072` (document-level):
- Enables chunk-level RAG search AND document-level visualization
- `GetBestVector()` helper handles migration gracefully
- No breaking changes to existing consumers

### 3. File Type Display Mapping

Creating `FILE_TYPES` constants with 20+ file types:
- Consistent iconography across the UI
- `getFileTypeDisplayName()` for human-readable labels
- Easy extensibility for new file types

### 4. Orphan File Support

Treating files without Dataverse records as first-class citizens:
- `documentId` nullable, `speFileId` required
- Distinct visual styling (dashed border, "File only" badge)
- NodeActionBar disables "Open Document Record" for orphans

---

## Challenges Encountered and Solutions

### Challenge 1: Bundle Size Explosion

**Problem**: Initial PCF bundle was 24.4MB, far exceeding 5MB limit.

**Solution**:
- Externalized React and Fluent UI via platform-library declarations
- Tree-shaking unused react-flow-renderer components
- Final bundle: 6.65MB (including d3-force, icons)

### Challenge 2: React 16 Compatibility

**Problem**: react-flow v11 requires React 18, but Dataverse PCF uses React 16.

**Solution**:
- Used react-flow-renderer v10 (last React 16 compatible version)
- Avoided React 18 APIs (`createRoot`, concurrent features)
- Used `ReactDOM.render` per ADR-022

### Challenge 3: Embedding Dimension Migration

**Problem**: Existing documents indexed with 1536-dim vectors, new design needs 3072-dim.

**Solution**:
- Added parallel fields (`contentVector3072`, `documentVector3072`)
- `GetBestVector()` prefers 3072, falls back to 1536
- Created `EmbeddingMigrationService` for batch re-embedding
- DocumentVectorBackfillService for averaging chunk vectors

### Challenge 4: Tenant ID Resolution

**Problem**: PCF control needed tenant ID but couldn't access Xrm.Utility.getGlobalContext() reliably.

**Solution**:
- Passed `tenantId` as control property from form
- Form configured with environment variable binding
- Fallback to organization ID from Xrm if needed

---

## Metrics

| Metric | Value |
|--------|-------|
| Total Tasks | 44 (42 completed, 2 deferred) |
| .NET Unit Tests | 85 |
| PCF Component Tests | 40 |
| E2E Tests | 18 |
| Lines of Code (est.) | ~5,000 (C#), ~3,000 (TypeScript) |
| Bundle Size | 6.65 MB |
| Search Latency (avg) | 235ms |
| Vector Search Latency (avg) | 147ms |

---

## Recommendations for Future Projects

1. **Add index configuration validation** to startup and health checks
2. **Include full-pipeline smoke tests** in Phase 1 before UI development
3. **Design for extensibility** (orphan files, multiple dimensions) from the start
4. **Validate UX decisions** (modal vs inline) with stakeholders early
5. **Use 3072-dim embeddings** by default for new projects
6. **Track bundle size** in CI to catch regressions early

---

## Acknowledgments

- **ADR Authors**: For establishing patterns that prevented common pitfalls
- **RAG Architecture (R3)**: Solid foundation that enabled rapid development
- **Fluent UI Team**: Excellent v9 components with built-in dark mode

---

*Lessons learned documented: 2026-01-12*
