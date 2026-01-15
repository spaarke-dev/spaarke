# Lessons Learned - AI Node-Based Playbook Builder

> **Project**: ai-node-playbook-builder
> **Duration**: January 8-15, 2026
> **Tasks Completed**: 47 tasks across 6 phases

---

## What Worked Well

### 1. Architecture Refactor Decision (Phase 2.5)

**Decision**: Pivoted from iframe + React 18 to direct React Flow v10 + React 16 in PCF

**Why it worked**:
- Eliminated postMessage complexity and dual deployment
- Reduced bundle size significantly (8MB → 239KB)
- Simplified state management with single Zustand store
- Better debugging (no cross-origin console issues)

**Key insight**: Recognizing the need to pivot early (after Phase 2) saved significant rework later.

### 2. Dual-Path Auto-Save Pattern

**Implementation**: Both `notifyOutputChanged()` AND `webAPI.updateRecord()`

**Why it worked**:
- Users never lose work (even if they close without form Save)
- Standard PCF binding still works for form-level saves
- 500ms debounce prevents excessive API calls

**Key insight**: Don't rely solely on PCF bound properties - direct WebAPI gives reliable persistence.

### 3. Incremental Phase Delivery

**Approach**: Each phase was deployable and testable independently

**Why it worked**:
- Phase 1 delivered working backend before UI existed
- Phase 2.5 refactor didn't block Phase 3-5 work
- Each deployment validated previous phase

**Key insight**: Vertical slices (backend + frontend + deploy) per phase work better than horizontal layers.

### 4. ADR-Driven Development

**Pattern**: Load ADR constraints before implementation, validate after

**Why it worked**:
- ADR-022 (React 16) caught the React 18 mistake early
- ADR-013 (extend BFF) prevented scope creep to microservices
- Quality gates (adr-check) caught violations before merge

**Key insight**: ADRs are most valuable when enforced during development, not just documented.

---

## Challenges and Solutions

### 1. React Version Compatibility (Phase 2)

**Challenge**: React Flow v12 requires React 18, but Dataverse provides React 16

**Symptoms**:
- `createRoot` not available errors
- SSR/hydration mismatches
- Platform library conflicts

**Solution**:
- Migrated to react-flow-renderer v10 (last React 16 compatible version)
- Used `ReactDOM.render` instead of `createRoot`
- Added `platform-library` declarations in manifest

**Time lost**: ~4 hours debugging before recognizing root cause

**Prevention**: Check framework version compatibility FIRST when evaluating libraries.

### 2. Dataverse Field Name Discovery

**Challenge**: WebAPI save failed silently due to wrong field name

**Symptoms**:
- Console showed "save successful"
- Data didn't persist after refresh
- No error in Network tab (200 response, but no effect)

**Solution**:
- Added detailed debug logging for all bound parameters
- Discovered field was `sprk_canvaslayoutjson` not `sprk_canvasjson`
- Verified form binding matched WebAPI field

**Prevention**: Always verify exact Dataverse field names from entity definition, not assumptions.

### 3. PCF Bundle Size

**Challenge**: Initial bundle was 8MB, causing slow loads

**Root cause**:
- react-flow-renderer includes all optional dependencies
- Fluent UI tree-shaking not configured
- Dev mode bundle deployed

**Solution**:
- Production build with `npm run build:prod`
- Platform libraries for React and Fluent UI
- Explicit imports instead of barrel imports

**Result**: 239KB final bundle (97% reduction)

### 4. Layout Shift on Dirty Indicator

**Challenge**: "Unsaved changes" appearing/disappearing caused header shift

**Root cause**: Conditional rendering removed element from DOM flow

**Solution**:
- Use `visibility: hidden/visible` instead of conditional render
- Add `minWidth` to reserve space
- Element always in DOM, just invisible

**Key insight**: For status indicators, prefer visibility over conditional rendering.

---

## Architectural Decisions Made

### 1. Direct PCF vs. Iframe Architecture

**Decision**: Direct React rendering in PCF (no iframe)

**Rationale**:
- Simpler deployment (one artifact instead of two)
- Better performance (no postMessage overhead)
- Easier debugging (single console)
- React 16 constraint acceptable for our use case

**Trade-off**: Can't use latest React Flow features (v12+)

### 2. Zustand for State Management

**Decision**: Zustand over Redux or Context

**Rationale**:
- Minimal boilerplate
- Works with React 16
- Built-in persistence helpers
- No provider wrapper needed

**Trade-off**: Less ecosystem support than Redux

### 3. Canvas-First Persistence

**Decision**: Canvas JSON is source of truth, not individual node records

**Rationale**:
- Simpler data model (one field vs. many records)
- Atomic updates (all or nothing)
- Easy export/import
- Version control friendly

**Trade-off**: Can't query individual nodes in Dataverse

### 4. Exponential Backoff for Retries

**Decision**: NodeRetryPolicy with exponential backoff + jitter

**Rationale**:
- Prevents thundering herd on Azure OpenAI
- Configurable per-node via `retryCount` property
- Matches Azure SDK patterns

**Configuration**: Base delay 1s, multiplier 2x, max 3 retries

---

## Metrics Summary

| Metric | Target | Actual | Notes |
|--------|--------|--------|-------|
| Tasks completed | 47 | 47 | All phases delivered |
| Tests passing | N/A | 97 | Unit + Integration + Load |
| P95 latency | <10s | 558ms | 94% better than target |
| Bundle size | <500KB | 239KB | 52% of budget |
| DI registrations | ≤15 | 12 | 20% under limit |

---

## Recommendations for Future Projects

### 1. Platform Constraints First

Always verify platform constraints (React version, browser support, API limits) before selecting libraries.

### 2. Debug Logging Early

Add comprehensive debug logging from the start - it's invaluable for diagnosing deployment issues.

### 3. Field Name Verification

For Dataverse integrations, verify exact field names from entity metadata, not documentation or assumptions.

### 4. Incremental Deployment

Deploy after each phase, not just at the end. Early deployments catch integration issues.

### 5. ADR Quality Gates

Run ADR validation as part of CI/CD, not just manual review.

---

*Document created: January 15, 2026*
