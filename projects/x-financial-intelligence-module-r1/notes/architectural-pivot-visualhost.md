# Architectural Pivot: Custom PCF → VisualHost + Denormalization

**Date:** 2026-02-11
**Status:** ✅ Approved and Implemented
**Impact:** Tasks 041, 043, 044 removed; Task 042 simplified; Task 049 added

---

## Executive Summary

Replaced custom PCF Finance Intelligence Panel (Tasks 041-044) with hybrid denormalization approach using existing VisualHost component. This simplifies implementation, reduces bundle size concerns, and aligns with existing platform investment.

---

## Problem Statement

**Original Plan:**
- Custom PCF control with React components (BudgetGauge, SpendTimeline)
- Tasks 041-044: Scaffold, components, signals, theming (26h total)
- BFF API endpoint providing aggregated finance summary

**Issue Identified:**
- User questioned: "Can't we use VisualHost instead of building separate PCF?"
- VisualHost exists but requires Dataverse entity data (FetchXML queries)
- Finance data stored in separate snapshot entities (normalized architecture)
- Complex joins required to surface data via VisualHost

---

## Decision

**APPROVED:** Hybrid denormalization approach with VisualHost configuration

### Key Changes

1. **Add Finance Fields to Parent Entities**
   - Target entities: `sprk_matter`, `sprk_project`
   - Fields (6 total):
     - `sprk_budget` (Money)
     - `sprk_currentspend` (Money)
     - `sprk_budgetvariance` (Money)
     - `sprk_budgetutilizationpct` (Decimal)
     - `sprk_velocitypct` (Decimal)
     - `sprk_lastfinanceupdatedate` (DateTime)

2. **Update Mechanism**
   - **Service Layer Updates:** SpendSnapshotGenerationJobHandler updates parent entity fields
   - **User Preference:** BFF API services (NOT rollup fields, calculated fields, or plugins)
   - **Update Trigger:** After snapshot generation (existing job handler)

3. **Visualization**
   - **VisualHost Configuration:** Task 042 simplified to chart definition configuration only
   - **No Custom Code:** Leverage existing VisualHost framework
   - **Estimate:** 2h (down from 26h for custom PCF)

4. **IDataverseService Extension**
   - **New Task 049:** Extend IDataverseService for finance entities
   - **Purpose:** Addresses 2 BLOCKER deployment TODOs (CreateAsync, UpdateAsync)
   - **Blocks:** Tasks 016, 019, 032

---

## Rationale

### Why Denormalization?

| Benefit | Impact |
|---------|--------|
| **Simpler Queries** | Direct access to finance metrics without complex joins |
| **VisualHost Compatibility** | FetchXML can query parent entity directly |
| **Performance** | No runtime aggregation for common display scenarios |
| **Manual Entry Support** | Users can override calculated values if needed |

### Why Not Rollup/Calculated Fields?

- **User Preference:** Explicit rejection of Dataverse automation
- **Flexibility:** BFF services already exist for AI processing
- **Control:** Service layer updates provide audit trail and business logic hooks
- **Consistency:** Aligns with existing architecture patterns

### Why Remove Custom PCF?

| Consideration | Custom PCF | VisualHost |
|---------------|------------|------------|
| **Effort** | 26h (Tasks 041-044) | 2h (Task 042 config) |
| **Bundle Size** | < 5MB risk | N/A (existing component) |
| **Maintenance** | New code to maintain | Configuration only |
| **Theme Support** | Must implement dark mode | Built-in theme support |
| **Flexibility** | Custom UX possible | Limited to chart types |

**Conclusion:** For single-matter display use case, VisualHost is sufficient. Custom dashboard (if needed) should be React 18 Custom Page, not PCF.

---

## Implementation Plan

### Modified Tasks

| Task | Before | After | Change |
|------|--------|-------|--------|
| 002 | Add 16 fields to sprk_document | **Add 6 finance fields to sprk_matter + sprk_project** | ✅ Already complete, needs revision |
| 019 | SpendSnapshotGenerationJobHandler | **Add parent entity field updates** | ✅ Complete, needs enhancement |
| 041 | Scaffold Finance Intelligence PCF | ~~Removed~~ | ❌ Deleted directory |
| 042 | BudgetGauge + SpendTimeline components | **Configure VisualHost chart definitions** | 🔲 Simplified to MINIMAL rigor |
| 043 | Active Signals + Invoice History panel | ~~Removed~~ | ❌ Not needed |
| 044 | PCF Theming and Dark Mode | ~~Removed~~ | ❌ VisualHost handles |
| 049 | - | **Extend IDataverseService** | ➕ New task added |

### Files Modified

**Deleted:**
- `src/client/pcf/FinanceIntelligencePanel/` (entire directory)

**To Update:**
- Task 002 POML: Add finance fields to Matter/Project
- Task 019 POML: Add parent entity update logic
- Task 042 POML: Rewrite for VisualHost configuration
- TASK-INDEX.md: ✅ Updated (Phase 4, dependencies, critical path, progress tracking)
- current-task.md: ✅ Updated (session notes, decisions)

### Deployment Readiness Impact

**BLOCKERS Addressed by Task 049:**
1. ✅ BillingEvent creation logic → IDataverseService.CreateAsync
2. ✅ Invoice reviewer corrections → IDataverseService.UpdateAsync

**Remaining TODOs:**
- HIGH (1): Search index metadata enrichment
- MEDIUM (3): Text storage decision, tenant context, metadata access
- LOW (1): Audit trail for signal generation

---

## Future Considerations

### Law Department Dashboard

**Context:** User provided `law-department-dashboard.png` screenshot showing full billing intelligence dashboard.

**Decision:** This is **NOT** part of Finance Intelligence Module R1.

**Rationale:**
- Dashboard is full React 18 Custom Page (not PCF)
- No React 16 constraints, no bundle size limits
- Requires different UI framework (not Fluent UI v9 for PCF)
- Separate project scope

**Recommendation:** Create new project for dashboard when R1 complete.

---

## Risks & Mitigation

| Risk | Likelihood | Mitigation |
|------|-----------|------------|
| Denormalized fields become stale | Low | BFF services update on every snapshot generation |
| Manual edits overwritten | Low | Document that manual budget entry is supported, spend fields are calculated |
| VisualHost lacks needed chart type | Medium | Phase 4 includes tuning tasks (045, 046) to validate |
| IDataverseService changes break existing code | Low | Task 049 FULL rigor protocol includes tests |

---

## Metrics

| Metric | Before | After | Delta |
|--------|--------|-------|-------|
| **Total Tasks** | 37 | 35 | -2 (net: -3 removed, +1 added) |
| **Phase 4 Effort** | 35h | 28h | -7h (20% reduction) |
| **Critical Path** | 52h | 36h | -16h (31% reduction) |
| **PCF Bundle Size Risk** | High | None | Eliminated |
| **Custom Code to Maintain** | React components (4 files) | Configuration only | Minimal |

---

## Approval & Execution

**Approved By:** User (2026-02-11)
**Quote:** "yes that's all correct. please proceed"

**Execution Status:**
- ✅ TASK-INDEX.md updated
- ✅ current-task.md updated
- ✅ Architectural decision documented
- 🔲 Task 049 ready to execute (next priority)
- 🔲 Remaining Phase 4 tasks available

---

## Related Documents

- [TASK-INDEX.md](../tasks/TASK-INDEX.md) - Updated task registry
- [current-task.md](../current-task.md) - Session notes and decisions
- [DEPLOYMENT-READINESS-CHECKLIST.md](DEPLOYMENT-READINESS-CHECKLIST.md) - Production blockers
- [VisualHost Architecture](../../../docs/guides/VISUALHOST-ARCHITECTURE.md) - Component capabilities
- [law-department-dashboard.png](screens-shots/law-department-dashboard.png) - Future dashboard reference

---

*This pivot demonstrates the value of questioning assumptions and leveraging existing platform capabilities before building custom solutions.*
