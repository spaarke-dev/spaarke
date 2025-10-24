# Sprint Plan: Custom Page Migration for Universal Quick Create

**Sprint Goal:** Migrate Universal Document Upload from Quick Create form to Custom Page dialog

**Sprint Duration:** 2 weeks (10 working days)
**Team:** Development Team
**Current Version:** 2.3.0 (Phase 7 Complete)
**Target Version:** 3.0.0 (Custom Page)

---

## Table of Contents

1. [Sprint Overview](#sprint-overview)
2. [Success Criteria](#success-criteria)
3. [Dependencies & Prerequisites](#dependencies--prerequisites)
4. [Risk Assessment](#risk-assessment)
5. [Sprint Backlog](#sprint-backlog)
6. [Testing Plan](#testing-plan)
7. [Deployment Plan](#deployment-plan)
8. [Rollback Plan](#rollback-plan)

---

## Sprint Overview

### What We're Building

Replace the existing Quick Create form-based document upload with a Custom Page dialog that provides:
- Better UI control (full Fluent UI v9)
- Independent workflow execution (no form dependency)
- Modern modal dialog experience
- Simplified deployment (single solution artifact)

### What We're NOT Changing

**CRITICAL: These components remain UNCHANGED:**
- ✅ BFF API (Spe.Bff.Api) - No changes required
- ✅ Phase 7 NavMapClient - Keep as-is
- ✅ DocumentRecordService - Keep as-is
- ✅ FileUploadService - Keep as-is
- ✅ SdapApiClient - Keep as-is
- ✅ MsalAuthProvider - Keep as-is
- ✅ EntityDocumentConfig - Keep as-is
- ✅ All service layer code - Keep as-is
- ✅ Authentication flows - Keep as-is
- ✅ Dataverse schema - Keep as-is

**What IS Changing:**
- 🔄 PCF Control (UniversalQuickCreate/index.ts) - Add Custom Page mode detection
- 🔄 React Components - Update for standalone dialog mode
- 🔄 Ribbon Commands - Change navigation from Quick Create to Custom Page
- ✅ Custom Page - NEW component (sprk_DocumentUploadDialog)

---

## Success Criteria

### Functional Requirements

1. ✅ User can open document upload dialog from any supported entity (Matter, Project, Invoice, Account, Contact)
2. ✅ Dialog opens as modal (centered, 800px width)
3. ✅ All Phase 7 features work (dynamic metadata discovery, caching)
4. ✅ File upload to SPE works (parallel upload)
5. ✅ Document record creation works (sequential, correct navigation property)
6. ✅ Dialog closes automatically on success
7. ✅ Subgrid refreshes after dialog closes
8. ✅ Error handling works (validation, upload failures, record creation failures)
9. ✅ Progress tracking works (real-time updates)
10. ✅ Cancel button closes dialog without creating records

### Non-Functional Requirements

1. ✅ Performance matches v2.3.0 (no degradation)
2. ✅ Authentication works (OAuth2 OBO flow)
3. ✅ Security model unchanged (same permissions required)
4. ✅ All existing entities continue to work
5. ✅ Backward compatible (can deploy alongside v2.3.0 for testing)
6. ✅ Application Insights logging maintained
7. ✅ No breaking changes to BFF API
8. ✅ No Dataverse schema changes required

### Quality Gates

- [ ] All unit tests pass
- [ ] Manual testing complete on all 5 entities
- [ ] Phase 7 metadata discovery validated
- [ ] Performance benchmarks met
- [ ] Security review passed
- [ ] Code review complete
- [ ] Documentation updated
- [ ] Deployment runbook tested

---

## Dependencies & Prerequisites

### Technical Prerequisites

**Required Knowledge:**
1. [ARCHITECTURE.md](./ARCHITECTURE.md) - Complete architecture overview
2. [SDAP-ARCHITECTURE-GUIDE.md](../../docs/SDAP-ARCHITECTURE-GUIDE.md) - SDAP ecosystem
3. [PHASE-7-DEPLOYMENT-STATUS.md](../../docs/PHASE-7-DEPLOYMENT-STATUS.md) - Phase 7 implementation
4. Microsoft Custom Pages documentation

**Required Environment:**
1. DEV Dataverse environment (SPAARKE DEV 1)
2. BFF API deployed and healthy (spe-api-dev-67e2xz.azurewebsites.net)
3. Phase 7 operational (NavMap endpoints working)
4. Redis cache available (15-min TTL)
5. SPE containers configured
6. pac CLI installed and authenticated
7. Visual Studio 2022 or VS Code with PCF tooling
8. Node.js 18+ and npm

**Required Permissions:**
1. Dataverse System Customizer (minimum)
2. PCF control deployment permissions
3. Solution import/export permissions
4. Custom Page creation permissions

### External Dependencies

**No external dependencies** - This is a UI migration only

---

## Risk Assessment

### High Risk Items

1. **Risk:** Custom Page API may differ from expected behavior
   - **Mitigation:** Create prototype Custom Page in Task 1
   - **Contingency:** Fall back to Quick Create form if Custom Page issues arise

2. **Risk:** PCF control lifecycle in Custom Page vs Quick Create form
   - **Mitigation:** Thorough testing of init/updateView/destroy lifecycle
   - **Contingency:** Add compatibility mode for both contexts

3. **Risk:** Dialog close behavior may not refresh subgrid
   - **Mitigation:** Test refresh logic in Task 3
   - **Contingency:** Add manual refresh button as fallback

### Medium Risk Items

1. **Risk:** Navigation parameter passing may fail
   - **Mitigation:** Validate all parameters in Task 2
   - **Contingency:** Add parameter validation with error messages

2. **Risk:** Version conflicts during deployment
   - **Mitigation:** Test side-by-side deployment in Task 6
   - **Contingency:** Staged rollout (entity-by-entity)

### Low Risk Items

1. **Risk:** UI styling differences in Custom Page
   - **Mitigation:** Use same Fluent UI v9 components
   - **Impact:** Visual only, no functional impact

---

## Sprint Backlog

### Sprint Timeline

```
Week 1:
  Day 1-2: Tasks 1-2 (Custom Page + PCF Updates)
  Day 3-4: Task 3 (Ribbon Commands)
  Day 5:   Task 4 (Solution Packaging)

Week 2:
  Day 6-7: Task 5 (Testing)
  Day 8:   Task 6 (Deployment to DEV)
  Day 9:   Task 7 (UAT)
  Day 10:  Sprint Review & Retro
```

### Task List

1. **[Task 1: Create Custom Page](#task-1-create-custom-page)** (8 hours)
   - Create Custom Page JSON definition
   - Configure PCF control embedding
   - Test parameter passing

2. **[Task 2: Update PCF Control for Custom Page](#task-2-update-pcf-control-for-custom-page)** (12 hours)
   - Add Custom Page context detection
   - Implement closeDialog() method
   - Remove form save dependency
   - Update React components

3. **[Task 3: Update Ribbon Commands](#task-3-update-ribbon-commands)** (6 hours)
   - Update ribbon button JavaScript
   - Change navigation to Custom Page
   - Update display name field logic

4. **[Task 4: Solution Packaging](#task-4-solution-packaging)** (4 hours)
   - Add Custom Page to solution
   - Update version to 3.0.0
   - Export solution package

5. **[Task 5: Testing & Validation](#task-5-testing--validation)** (12 hours)
   - Functional testing (all entities)
   - Phase 7 validation
   - Error scenario testing
   - Performance testing

6. **[Task 6: Deployment to DEV](#task-6-deployment-to-dev)** (4 hours)
   - Import solution
   - Publish customizations
   - Smoke testing
   - Monitor Application Insights

7. **[Task 7: User Acceptance Testing](#task-7-user-acceptance-testing)** (8 hours)
   - UAT with real users
   - Feedback collection
   - Bug fixes (if needed)

8. **[Task 8: Documentation & Knowledge Transfer](#task-8-documentation--knowledge-transfer)** (4 hours)
   - Update user documentation
   - Create deployment runbook
   - Knowledge transfer session

**Total Estimate:** 58 hours (~1.5 sprints with buffer)

---

## Testing Plan

### Unit Testing

**Location:** `src/controls/UniversalQuickCreate/__tests__/`

**Test Cases:**
1. Custom Page context detection
2. Parameter parsing from navigation
3. Dialog close behavior
4. File validation logic (existing)
5. Payload building logic (existing)

### Integration Testing

**Test Matrix:**

| Entity | Container ID | Upload 1 File | Upload 10 Files | Error Handling | Phase 7 Cache |
|--------|--------------|---------------|-----------------|----------------|---------------|
| sprk_matter | ✅ | ✅ | ✅ | ✅ | ✅ |
| sprk_project | ✅ | ✅ | ✅ | ✅ | ✅ |
| sprk_invoice | ✅ | ✅ | ✅ | ✅ | ✅ |
| account | ✅ | ✅ | ✅ | ✅ | ✅ |
| contact | ✅ | ✅ | ✅ | ✅ | ✅ |

### Performance Testing

**Benchmarks (must match v2.3.0):**
- Upload 1 file: ≤ 5 seconds
- Upload 10 files (100MB): ≤ 30 seconds
- Dialog open time: ≤ 2 seconds
- Metadata query (cached): ≤ 50ms
- Metadata query (first): ≤ 500ms

### Security Testing

**Validation:**
1. User permissions enforced (Create on sprk_document)
2. OAuth2 token validation
3. SPE access control
4. No elevation of privileges
5. Audit logs captured

---

## Deployment Plan

### Phase 1: DEV Environment (Week 2, Day 6)

**Steps:**
1. Backup current Quick Create form configuration
2. Import SpaarkeDocumentUpload_3_0_0_0.zip
3. Publish all customizations
4. Test on sprk_matter entity first
5. Validate Phase 7 metadata discovery
6. Monitor Application Insights for errors
7. If successful, enable on remaining entities

### Phase 2: UAT Environment (Week 2, Day 7)

**Steps:**
1. Import solution to UAT
2. User acceptance testing
3. Performance validation
4. Collect feedback
5. Bug fixes (if needed)

### Phase 3: Production (Post-Sprint)

**Steps:**
1. Schedule deployment window
2. Import solution to PROD
3. Publish customizations
4. Pilot users validate
5. Monitor for 48 hours
6. Full rollout to all users

---

## Rollback Plan

### Immediate Rollback (< 1 hour)

**If critical issues occur:**

1. **Revert ribbon button commands**
   - Change navigation back to Quick Create form
   - No solution uninstall needed
   - Users see old experience immediately

2. **Steps:**
   ```javascript
   // Change ribbon button back to Quick Create
   function openDocumentUpload(primaryControl) {
       Xrm.Utility.openQuickCreate("sprk_document", {
           sprk_matter: { id: recordId, name: displayName }
       });
   }
   ```

### Full Rollback (1-2 hours)

**If Custom Page has fundamental issues:**

1. Remove Custom Page from solution
2. Deactivate Custom Page in Dataverse
3. Restore Quick Create form configuration
4. Rollback PCF control to v2.3.0
5. Publish all customizations

**Rollback Success Criteria:**
- All users can upload documents via Quick Create form
- No errors in Application Insights
- Phase 7 still operational

---

## Sprint Artifacts

### Documentation to Produce

1. ✅ [ARCHITECTURE.md](./ARCHITECTURE.md) - Already updated
2. [ ] [SPRINT-PLAN.md](./SPRINT-PLAN.md) - This document
3. [ ] [SPRINT-TASKS.md](./SPRINT-TASKS.md) - Detailed task breakdown
4. [ ] [TESTING-REPORT.md](./TESTING-REPORT.md) - Test execution results
5. [ ] [DEPLOYMENT-RUNBOOK.md](./DEPLOYMENT-RUNBOOK.md) - Step-by-step deployment
6. [ ] [USER-GUIDE.md](./USER-GUIDE.md) - End-user documentation

### Code to Produce

1. [ ] `sprk_DocumentUploadDialog.json` - Custom Page definition
2. [ ] `src/controls/UniversalQuickCreate/index.ts` - Updated PCF control
3. [ ] `src/controls/UniversalQuickCreate/components/*.tsx` - Updated React components
4. [ ] `sprk_subgrid_commands.js` - Updated ribbon commands
5. [ ] `SpaarkeDocumentUpload_3_0_0_0.zip` - Solution package

---

## Daily Standup Template

**What did you do yesterday?**
- [Task completed]
- [Progress made]

**What will you do today?**
- [Task planned]
- [Expected completion]

**Any blockers?**
- [Blocker description]
- [Help needed]

---

## Sprint Review Checklist

- [ ] All tasks completed
- [ ] All test cases passed
- [ ] Documentation updated
- [ ] Solution deployed to DEV
- [ ] UAT completed successfully
- [ ] No critical bugs
- [ ] Performance benchmarks met
- [ ] Security review passed
- [ ] Deployment runbook validated
- [ ] User feedback positive
- [ ] Ready for production deployment

---

## Sprint Retrospective

**What went well?**
- [To be filled after sprint]

**What could be improved?**
- [To be filled after sprint]

**Action items for next sprint:**
- [To be filled after sprint]

---

**Sprint Status:** 🟡 Planning Phase
**Last Updated:** 2025-10-20
**Next Review:** [Sprint Start Date]

---

## Related Documentation

- [ARCHITECTURE.md](./ARCHITECTURE.md) - Architecture overview
- [SPRINT-TASKS.md](./SPRINT-TASKS.md) - Detailed task breakdown
- [../../docs/SDAP-ARCHITECTURE-GUIDE.md](../../docs/SDAP-ARCHITECTURE-GUIDE.md) - SDAP architecture
- [../../docs/PHASE-7-DEPLOYMENT-STATUS.md](../../docs/PHASE-7-DEPLOYMENT-STATUS.md) - Phase 7 status
