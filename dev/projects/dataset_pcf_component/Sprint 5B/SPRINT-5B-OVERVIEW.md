# Sprint 5B: Universal Dataset Grid - Full Compliance & Refactor

**Sprint Goal:** Make Universal Dataset Grid fully compliant with Fluent UI v9 ADR and PCF best practices

**Start Date:** 2025-10-05
**Target Completion:** 2025-10-12 (1 week)
**Priority:** HIGH - Blocking SDAP integration

---

## Context

Following the compliance assessment in `Sprint 5/UniversalDatasetGrid_Compliance_Assessment.md`, we identified critical architectural and ADR compliance issues that must be resolved before proceeding with SDAP integration.

**Key Issues Identified:**
1. ⚠️ Mixed React renderers (legacy + React 18)
2. ⚠️ DOM rebuilt on every `updateView()`
3. ⚠️ Grid/toolbar use raw HTML instead of Fluent UI v9 components
4. ⚠️ Hard-coded styles violating Fluent design tokens
5. ⚠️ No theme awareness (always light theme)
6. ⚠️ No dataset paging/virtualization

---

## Sprint Structure

### Phase A: Architecture Refactor (CRITICAL)
**Duration:** 2-3 days
**Status:** 🔴 Not Started

- Task A.1: Create Single React Root Architecture
- Task A.2: Implement Fluent UI DataGrid
- Task A.3: Implement Fluent UI Toolbar
- Task A.4: Refactor State Management

### Phase B: Theming & Design Tokens
**Duration:** 1-2 days
**Status:** 🔴 Not Started

- Task B.1: Implement Dynamic Theme Resolution
- Task B.2: Replace Inline Styles with Tokens
- Task B.3: Create Component Styles with makeStyles

### Phase C: Performance & Dataset Optimization
**Duration:** 1-2 days
**Status:** 🔴 Not Started

- Task C.1: Implement Dataset Paging
- Task C.2: Optimize State Management
- Task C.3: Add Virtualization (if needed)

### Phase D: Code Quality & Standards
**Duration:** 1 day
**Status:** 🔴 Not Started

- Task D.1: Remove Debug Logging
- Task D.2: Add ESLint Rules
- Task D.3: Update Documentation
- Task D.4: Create Error Boundaries

---

## Success Criteria

### Functional Requirements
- ✅ Control loads without errors in Power Apps
- ✅ Dataset displays with all columns from metadata
- ✅ Row selection works (single & multi-select)
- ✅ Command bar buttons enable/disable correctly
- ✅ All Fluent UI v9 components render properly
- ✅ Theme switches between light/dark modes
- ✅ Large datasets (100+ records) render smoothly

### Technical Requirements
- ✅ Single React root using `createRoot()`
- ✅ All UI uses Fluent UI v9 components (DataGrid, Toolbar, Button, etc.)
- ✅ No raw HTML elements (`<table>`, `<div>` with inline styles)
- ✅ All styles use Fluent design tokens
- ✅ Bundle size remains under 5 MB
- ✅ ESLint passes with no violations
- ✅ No console errors or warnings

### Compliance Requirements
- ✅ ADR-021: Fluent UI v9 compliance (100%)
- ✅ PCF lifecycle best practices
- ✅ React 18 best practices (no legacy APIs)
- ✅ Accessibility (WCAG 2.1 AA)
- ✅ Power Apps theming integration

---

## Dependencies

**Blocked By:**
- None (can start immediately)

**Blocks:**
- Sprint 6 Phase 3: SDAP Integration
- All future Universal Dataset Grid features

**Related:**
- Sprint 5: Initial deployment (completed)
- Sprint 6 Phase 1-2: Basic infrastructure (completed)

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Breaking changes during refactor | Medium | High | Comprehensive testing, git restore point created |
| Bundle size increases | Low | Medium | Monitor with webpack-bundle-analyzer, tree-shaking enabled |
| Power Apps compatibility issues | Low | High | Test in multiple environments, use PCF test harness |
| Timeline overrun | Medium | Medium | Prioritize Phase A, defer non-critical items |

---

## References

- **Compliance Assessment:** `Sprint 5/UniversalDatasetGrid_Compliance_Assessment.md`
- **Fluent UI v9 Docs:** https://react.fluentui.dev/
- **PCF Documentation:** https://learn.microsoft.com/power-apps/developer/component-framework/
- **ADR Repository:** `docs/adr/` (Fluent UI compliance ADR)
- **Knowledge Base:**
  - `docs/KM-FLUENT-DESIGN-DEPENDENT-LIBRARIES.md`
  - `docs/PCF-V9-PACKAGING.md`

---

## Team Notes

**Current Status (2025-10-05):**
- Restore point created: `restore-point-universal-grid-v2.0.2`
- Bundle: 3.71 MB (under 5 MB limit ✅)
- React 18: Partially migrated (ThemeProvider + CommandBar ✅, Grid ❌)
- Fluent UI v9: Partially compliant (CommandBar ✅, Grid ❌)

**Next Actions:**
1. Review and approve Sprint 5B plan
2. Begin Task A.1: Single React Root Architecture
3. Daily check-ins to track progress
4. Deploy and test after each phase

---

_Document Version: 1.0_
_Last Updated: 2025-10-05_
_Owner: PCF Engineering Squad_
