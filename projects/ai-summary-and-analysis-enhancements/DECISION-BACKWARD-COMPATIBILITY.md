# Decision: Remove Backward Compatibility Approach

**Date:** 2026-01-07
**Decision:** Remove backward compatibility layer and deploy API + PCF simultaneously
**Reason:** Development phase only, no production users, simpler architecture preferred

---

## Context

Initial implementation (Tasks 020-023) added backward compatibility:
- Kept old `/api/ai/document-intelligence/analyze` endpoint
- Mapped legacy request/response formats to new unified service
- Added 25 compatibility tests
- Planned Phase 2.4 cleanup to remove old code

**This was unnecessary because:**
- No production users exist yet
- PCF controls being updated anyway (forms/pages replacement)
- No external consumers to coordinate with
- Development phase allows for breaking changes

---

## Revised Approach

**Remove old API components entirely:**
- Delete `IDocumentIntelligenceService` interface
- Delete `DocumentIntelligenceService` implementation
- Delete old endpoint methods
- Remove mapping layer

**Update PCF to new endpoint:**
- Change endpoint URL: `/api/ai/document-intelligence/analyze` → `/api/ai/analysis/execute`
- Update request format: Single document → document array + explicit playbook
- Update response handling: Parse new `AnalysisStreamChunk` format

**Deploy together:**
- API deployment removes old code
- PCF deployment uses new endpoint
- Forms/pages updated with new PCF version
- Single coordinated release

---

## Benefits

✅ **Simpler codebase:** No mapping layer, no compatibility tests, no Phase 2.4 cleanup
✅ **Cleaner architecture:** Single endpoint, single service, one source of truth
✅ **Faster development:** Less code to write, test, and maintain
✅ **Complete migration:** No lingering legacy code or technical debt

---

## Deployment Plan

### Prerequisites
- [ ] API changes complete (remove old service)
- [ ] PCF updated to new endpoint
- [ ] All forms/pages using updated PCF identified
- [ ] Staging environment ready for testing

### Deployment Steps
1. Deploy API to Dev (removes old endpoint)
2. Deploy PCF to Dev (uses new endpoint)
3. Test in Dev environment
4. Deploy both to Staging
5. Verify end-to-end functionality
6. Deploy to Production when ready (future)

### Rollback Plan
- Git revert API deployment
- Git revert PCF deployment
- Both must rollback together

---

## Task Structure Changes

**Phase 2.3 Revised (Simplified Migration):**
- Task 020: ~~Route to unified service~~ → **Delete old service**
- Task 021: ~~Mapping tests~~ → **Update PCF to new endpoint**
- Task 022: ~~Obsolete attributes~~ → **Update forms/pages**
- Task 023: ~~Compatibility tests~~ → **Integration tests (new endpoint)**
- Task 024: Deploy and verify (coordinated)

**Phase 2.4: REMOVED** (cleanup no longer needed)

---

## Superseded Work

The following completed tasks are superseded by this decision:

| Task | Original Purpose | Status |
|------|------------------|--------|
| 020 | Route DocumentIntelligence endpoint to unified service | ❌ Superseded |
| 021 | Request/response mapping validation | ❌ Superseded |
| 022 | Add [Obsolete] attributes | ❌ Superseded |
| 023 | Backward compatibility tests | ❌ Superseded |

**Code to remove:**
- `DocumentIntelligenceEndpoints.cs` mapping logic
- `DocumentIntelligenceEndpointsMappingTests.cs`
- `BackwardCompatibilityTests.cs`
- [Obsolete] attributes

**Code to keep:**
- `AnalysisOrchestrationService` (unified service) ✅
- `DocumentProfileFieldMapper` ✅
- `IPlaybookService.GetByNameAsync` ✅
- All Phase 2.1-2.2 work ✅

---

## Impact Assessment

| Area | Impact | Action Required |
|------|--------|-----------------|
| API Codebase | Breaking change | Delete old service files |
| PCF Controls | Breaking change | Update to new endpoint |
| Forms/Pages | Deployment update | Deploy updated PCF |
| Tests | Tests removed | New integration tests needed |
| Documentation | Update needed | Reflect new endpoint only |

---

## Sign-off

**Decision made by:** User
**Approved by:** User
**Implementation lead:** Claude Code
**Date:** 2026-01-07

---

*This decision simplifies the architecture and accelerates development by removing unnecessary backward compatibility for a development-phase project.*
