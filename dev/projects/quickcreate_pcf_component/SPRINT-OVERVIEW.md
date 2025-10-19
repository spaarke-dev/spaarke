# Sprint: Universal Document Upload - Custom Page Migration

## Epic Summary

**Title:** Migrate Universal Quick Create PCF from Quick Create Form to Custom Page Dialog

**Problem Statement:**
The current Universal Quick Create PCF control is bound to Quick Create forms, which are designed for single-record creation only. When uploading multiple files, the second call to `context.webAPI.createRecord()` fails with a 400 Bad Request error due to Quick Create form context corruption. This prevents users from uploading multiple documents simultaneously.

**Solution:**
Refactor the PCF control to operate in a Custom Page Dialog context, using `Xrm.WebApi.createRecord()` instead of `context.webAPI.createRecord()`. This removes Quick Create limitations and enables unlimited multi-record creation.

---

## Goals

### Primary Goals
1. ✅ Enable users to upload **10 files** (100MB total) and create **10 Document records** without errors
2. ✅ Support **multiple parent entity types** (Matter, Project, Invoice, Account, Contact)
3. ✅ Migrate from Quick Create form context to Custom Page dialog context
4. ✅ Implement Fluent UI v9 components (strict compliance with ADRs)
5. ✅ Maintain existing file upload functionality (SharePoint Embedded via BFF API)

### Secondary Goals
1. ✅ Improve error handling (partial success scenarios)
2. ✅ Better progress indication (file-by-file progress)
3. ✅ Generic, reusable command button script
4. ✅ Proper versioning across all solution components
5. ✅ Single solution package for easy deployment

---

## Success Criteria

### Functional Requirements
- [ ] Can upload **1 file** and create **1 Document record**
- [ ] Can upload **10 files** and create **10 Document records**
- [ ] Can upload up to **100MB total** across all files
- [ ] **File size limits** enforced (10MB per file, 100MB total)
- [ ] **Dangerous file types blocked** (.exe, .dll, .bat, etc.)
- [ ] All Document records correctly linked to parent entity (Matter, Project, Invoice, Account, Contact)
- [ ] SharePoint `itemId` and `driveId` stored correctly in Dataverse
- [ ] Dialog opens/closes properly via command button
- [ ] Subgrid refreshes automatically after successful upload
- [ ] Partial failures handled gracefully (show errors, keep successful uploads)

### Technical Requirements
- [ ] Uses `Xrm.WebApi.createRecord()` (NOT `context.webAPI.createRecord()`)
- [ ] No Quick Create form dependencies
- [ ] Fluent UI v9 components only (NO v8 components)
- [ ] TypeScript strict mode compliance
- [ ] Proper separation of concerns (services, UI, config)
- [ ] Version displayed in dialog footer (`v2.0.0.0 - Build YYYY-MM-DD`)
- [ ] Single solution package containing all components

### User Experience
- [ ] Dialog matches Quick Create visual design
- [ ] Progress bar shows file-by-file progress ("Uploading 2 of 5...")
- [ ] Clear error messages for validation failures
- [ ] Responsive button states (disabled until files selected)
- [ ] Works consistently across all supported entity types

---

## Scope

### In Scope
✅ Refactor PCF control for Custom Page context
✅ Create `DocumentRecordService` using `Xrm.WebApi`
✅ Build Fluent UI v9 form components
✅ Create custom page definition
✅ Generic command button script
✅ Support 5 parent entity types (Matter, Project, Invoice, Account, Contact)
✅ File validation (size, type, count)
✅ Progress tracking
✅ Error handling
✅ Solution packaging

### Out of Scope
❌ Unlimited file uploads (capped at 10 files, 100MB)
❌ Chunked upload for large files (>100MB)
❌ Batch operations (upload to multiple parent records)
❌ Document preview/thumbnails
❌ Drag-and-drop file selection (use native file input)
❌ Edit existing documents (create only)
❌ Version history tracking
❌ Audit trail for uploads

---

## Key Architectural Changes

### Before (Quick Create Form)
```
Matter Form → Quick Create Form Dialog (sprk_document)
              ↓
              PCF Control (bound to sprk_filename field)
              ↓
              context.webAPI.createRecord() ❌ Fails on 2nd record
```

### After (Custom Page Dialog)
```
Matter Form → Custom Page Dialog (sprk_DocumentUploadDialog)
              ↓
              PCF Control (receives parent context via parameters)
              ↓
              Xrm.WebApi.createRecord() ✅ Unlimited records
```

---

## Timeline & Phases

| Phase | Description | Duration | Dependencies |
|-------|-------------|----------|--------------|
| **Phase 1** | Setup & Configuration | 2 hours | None |
| **Phase 2** | Services Refactoring | 3 hours | Phase 1 |
| **Phase 3** | PCF Control Migration | 4 hours | Phase 2 |
| **Phase 4** | UI Components (Fluent UI v9) | 4 hours | Phase 3 |
| **Phase 5** | Custom Page Creation | 2 hours | Phase 4 |
| **Phase 6** | Command Integration | 3 hours | Phase 5 |
| **Phase 7** | Testing & Validation | 4 hours | Phase 6 |
| **Deployment** | Packaging & Release | 2 hours | Phase 7 |

**Total Estimated Duration:** 24 hours (3 working days)

---

## Risks & Mitigations

### Risk 1: Fluent UI v9 Learning Curve
**Impact:** Medium
**Likelihood:** Medium
**Mitigation:** Reference Universal Dataset Grid implementation for patterns

### Risk 2: Custom Page Parameter Passing
**Impact:** High
**Likelihood:** Low
**Mitigation:** Use documented `Xrm.Navigation.navigateTo()` data property pattern

### Risk 3: Multi-Entity Lookup Complexity
**Impact:** Medium
**Likelihood:** Low
**Mitigation:** Use configuration-driven approach with `EntityDocumentConfig`

### Risk 4: Solution Packaging Dependencies
**Impact:** Medium
**Likelihood:** Low
**Mitigation:** Create comprehensive deployment guide with versioning

---

## Stakeholders

**Primary Stakeholder:** Ralph (Product Owner)
**Developer:** AI Assistant (Claude)
**Reference Implementation:** Universal Dataset Grid (Fluent UI v9 patterns)

---

## Related Documentation

- [ARCHITECTURE.md](./ARCHITECTURE.md) - System design and data flow
- [ADR-COMPLIANCE.md](./ADR-COMPLIANCE.md) - Architectural Decision Records compliance
- [CODE-REFERENCES.md](./CODE-REFERENCES.md) - Existing code to reuse/modify
- [DEPLOYMENT-GUIDE.md](./DEPLOYMENT-GUIDE.md) - Solution packaging and deployment

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0.0 | 2025-01-10 | Initial sprint plan created |

---

**Next Step:** Review [ARCHITECTURE.md](./ARCHITECTURE.md) for system design details.
