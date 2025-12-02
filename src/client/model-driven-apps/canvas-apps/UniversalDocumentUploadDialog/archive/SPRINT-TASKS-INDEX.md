# Sprint Tasks Index: Custom Page Migration v3.0.0

**Sprint Duration:** 2 weeks
**Total Estimated Effort:** 58 hours
**Version:** 2.3.0 → 3.0.0

---

## Quick Links to Task Files

Each task has been broken out into a separate file for better context efficiency and focused execution.

### Development Tasks

1. **[TASK-1: Create Custom Page](TASK-1-CUSTOM-PAGE-CREATION.md)** (8 hours)
   - Status: Manual activity required (Power Apps Maker Portal)
   - Quick Start: [TASK-1-QUICK-START.md](TASK-1-QUICK-START.md)
   - Pre-Review: [TASK-1-PRE-REVIEW.md](TASK-1-PRE-REVIEW.md)
   - Test Script: [test-custom-page-navigation.js](test-custom-page-navigation.js)
   - Spec: [custom-page-definition.json](custom-page-definition.json)

2. **[TASK-2: Update PCF Control](TASK-2-UPDATE-PCF-CONTROL.md)** (12 hours)
   - Add Custom Page mode detection
   - Implement closeDialog() method
   - Update upload workflow
   - Version bump to 3.0.0

3. **[TASK-3: Update Ribbon Commands](TASK-3-UPDATE-RIBBON-COMMANDS.md)** (6 hours)
   - Update web resource JavaScript
   - Update 5 RibbonDiff.xml files
   - Add container ID validation
   - Add subgrid refresh logic

4. **[TASK-4: Solution Packaging](TASK-4-SOLUTION-PACKAGING.md)** (4 hours)
   - Deploy PCF to Dataverse
   - Export solution package
   - Update solution metadata
   - Create deployment documentation

### Testing & Deployment Tasks

5. **[TASK-5: Testing & Validation](TASK-5-TESTING-VALIDATION.md)** (12 hours)
   - Execute test matrix (5 entities × 6 scenarios)
   - Validate Phase 7 metadata discovery
   - Performance benchmarking
   - Error scenario testing

6. **[TASK-6: DEV Deployment](TASK-6-DEV-DEPLOYMENT.md)** (4 hours)
   - Import solution to SPAARKE DEV 1
   - Publish customizations
   - Smoke testing
   - Monitor Application Insights

7. **[TASK-7: UAT](TASK-7-UAT.md)** (8 hours)
   - UAT with 3-5 real users
   - Feedback collection
   - Bug fixes (if needed)
   - Final sign-off

8. **[TASK-8: Documentation](TASK-8-DOCUMENTATION.md)** (4 hours)
   - Update user documentation
   - Update admin documentation
   - Knowledge transfer session
   - Sprint retrospective

---

## Task Dependencies

```
Task 1: Create Custom Page
  └─> Task 2: Update PCF Control (can run parallel)
      └─> Task 3: Update Ribbon Commands
          └─> Task 4: Solution Packaging
              └─> Task 5: Testing & Validation
                  └─> Task 6: DEV Deployment
                      └─> Task 7: UAT
                          └─> Task 8: Documentation
```

---

## Current Status

| Task | Status | Estimate | Deliverables |
|------|--------|----------|--------------|
| Task 1 | 🟡 Preparation Complete | 8h | Custom Page in Dataverse |
| Task 2 | 🔴 Not Started | 12h | PCF v3.0.0 |
| Task 3 | 🔴 Not Started | 6h | Updated ribbon commands |
| Task 4 | 🔴 Not Started | 4h | Solution package (.zip) |
| Task 5 | 🔴 Not Started | 12h | Test report |
| Task 6 | 🔴 Not Started | 4h | DEV deployment log |
| Task 7 | 🔴 Not Started | 8h | UAT sign-off |
| Task 8 | 🔴 Not Started | 4h | Updated documentation |

**Legend:**
- 🔴 Not Started
- 🟡 In Progress / Preparation Complete
- 🟢 Complete

---

## Key References

### Architecture & Planning
- [SDAP-UI-CUSTOM-PAGE-ARCHITECTURE.md](SDAP-UI-CUSTOM-PAGE-ARCHITECTURE.md) - Complete architecture document
- [SPRINT-PLAN.md](SPRINT-PLAN.md) - Sprint overview and success criteria

### Task 1 Resources (Current)
- [TASK-1-QUICK-START.md](TASK-1-QUICK-START.md) - Quick reference for Power Apps steps
- [TASK-1-CUSTOM-PAGE-CREATION.md](TASK-1-CUSTOM-PAGE-CREATION.md) - Detailed 8-step instructions
- [TASK-1-PRE-REVIEW.md](TASK-1-PRE-REVIEW.md) - Pre-task verification results
- [custom-page-definition.json](custom-page-definition.json) - Custom Page specification
- [test-custom-page-navigation.js](test-custom-page-navigation.js) - Browser console test

### Phase 7 Reference
- `docs/PHASE-7-DEPLOYMENT-STATUS.md` - Phase 7 implementation (DO NOT BREAK)

---

## Critical Constraints

**DO NOT CHANGE:**
- ✅ BFF API (Spe.Bff.Api) - No changes required
- ✅ Phase 7 NavMapClient - Keep as-is
- ✅ DocumentRecordService - Keep as-is
- ✅ All service layer code - Keep as-is
- ✅ Dataverse schema - No changes required

**MUST MAINTAIN:**
- ✅ Backward compatibility with Quick Create form
- ✅ Phase 7 dynamic metadata discovery
- ✅ Support for all 5 entities (Matter, Project, Invoice, Account, Contact)
- ✅ Redis caching (15-minute TTL)

---

## Next Steps

1. **User Action Required:** Complete Task 1 manual steps
   - Follow [TASK-1-QUICK-START.md](TASK-1-QUICK-START.md)
   - Create Custom Page in Power Apps Maker Portal
   - Test with [test-custom-page-navigation.js](test-custom-page-navigation.js)
   - Report results

2. **After Task 1 Complete:** Proceed to Task 2
   - Follow [TASK-2-UPDATE-PCF-CONTROL.md](TASK-2-UPDATE-PCF-CONTROL.md)
   - Update PCF control for Custom Page support

---

## Sprint Goal

Migrate Universal Document Upload from Quick Create form to Custom Page dialog, providing a modern modal UI experience while maintaining all Phase 7 functionality and backward compatibility.

**Success Criteria:**
- Custom Page dialog opens from ribbon button
- All 5 entity types supported
- Phase 7 metadata discovery works
- No changes to BFF API
- All tests pass
- User acceptance achieved

---

**Created:** 2025-10-20
**Sprint:** Custom Page Migration
**Version:** 3.0.0
