# Phase 6: PCF Control - Document Record Creation Fix

## Sprint Overview

**Goal:** Fix "undeclared property" errors when creating Document records in Dataverse from the Universal Quick Create PCF control after uploading files to SharePoint Embedded.

**Status:** Ready for Implementation
**Version:** 2.2.0
**Priority:** P0 - Critical
**Estimated Effort:** 3-5 days

---

## Problem Statement

### Current Issue
When users upload files via the Universal Quick Create PCF control:
1. ✅ Files successfully upload to SharePoint Embedded (200 OK)
2. ❌ Document record creation fails with error:
   ```
   An undeclared property 'sprk_matter' which only has property annotations
   in the payload but no property value was found in the JSON request payload.
   ```

### Root Cause
The PCF control uses an incorrect navigation property name when binding the parent lookup relationship. The code assumes the navigation property matches the lookup field name or relationship schema name, but Dataverse requires the actual `ReferencingEntityNavigationPropertyName` from the relationship metadata.

### Solution Approach
Implement a **metadata-driven** approach that:
1. Queries Dataverse metadata to get the correct navigation property name
2. Caches the result per environment + relationship
3. Supports multiple parent entity types (Matter, Account, Contact, etc.)
4. Provides two binding options (Option A: @odata.bind, Option B: relationship URL)

---

## Sprint Goals

### Primary Objectives
- ✅ Fix Document record creation for Matter → Document relationship
- ✅ Implement metadata-driven navigation property resolution
- ✅ Support multi-parent scenarios (extensible to Account, Contact, etc.)
- ✅ Add formData support for document name and description
- ✅ Maintain performance (1 metadata call per relationship, not per file)

### Secondary Objectives
- ✅ Comprehensive error handling and user-friendly messages
- ✅ Complete test coverage (positive, negative, performance)
- ✅ Documentation for future multi-parent deployments
- ✅ Cache implementation with environment isolation

---

## Task Breakdown

### Task 6.1: Metadata Validation (Pre-Implementation)
**Duration:** 0.5 days
**Dependencies:** None
**Deliverables:**
- Validate navigation property names for Matter → Document relationship
- Confirm entity set names (sprk_matters, sprk_documents)
- Document metadata query results for reference

[See: TASK-6.1-METADATA-VALIDATION.md]

---

### Task 6.2: Implement MetadataService
**Duration:** 1 day
**Dependencies:** Task 6.1
**Deliverables:**
- New file: `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/MetadataService.ts`
- Methods:
  - `getLookupNavProp()` - Get navigation property for child → parent lookup
  - `getEntitySetName()` - Get entity set name for parent entity
  - `getCollectionNavProp()` - Get collection navigation property for parent → child (Option B)
- Environment-aware caching
- Comprehensive error handling

[See: TASK-6.2-METADATA-SERVICE.md]

---

### Task 6.3: Update DocumentRecordService
**Duration:** 1 day
**Dependencies:** Task 6.2
**Deliverables:**
- Update `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/DocumentRecordService.ts`
- Add formData parameter support (documentName, description)
- Implement Option A (createDocuments method)
- Implement Option B (createViaRelationship method)
- Fix regex typo: `/[{}]/g` not `/\[{}\]/g`
- Add sprk_documentdescription field

[See: TASK-6.3-DOCUMENT-RECORD-SERVICE.md]

---

### Task 6.4: Update Configuration Files
**Duration:** 0.5 days
**Dependencies:** None
**Deliverables:**
- Update `src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityDocumentConfig.ts`
- Add `relationshipSchemaName` to interface
- Update ENTITY_DOCUMENT_CONFIGS for sprk_matter
- Add commented examples for future parent entities
- Update version to 2.2.0 in `ControlManifest.Input.xml`
- Update version badge in `index.ts`

[See: TASK-6.4-CONFIGURATION-UPDATES.md]

---

### Task 6.5: Build and Deploy PCF Control
**Duration:** 1 day
**Dependencies:** Tasks 6.2, 6.3, 6.4
**Deliverables:**
- Build PCF control with npm
- Deploy to Dataverse environment
- Handle Dataverse caching (remove/re-add component)
- Verify version update in browser
- Publish all customizations

[See: TASK-6.5-PCF-DEPLOYMENT.md]

---

### Task 6.6: Testing and Validation
**Duration:** 1 day
**Dependencies:** Task 6.5
**Deliverables:**
- Test single file upload (Option A)
- Test multi-file upload (3-5 files, Option A)
- Test Option B (relationship URL)
- Negative testing (wrong entity set, wrong nav property)
- Performance testing (no repeated metadata calls)
- Verify subgrid refresh on success

[See: TASK-6.6-TESTING-VALIDATION.md]

---

## Files Modified

### New Files
```
src/controls/UniversalQuickCreate/UniversalQuickCreate/services/MetadataService.ts
```

### Modified Files
```
src/controls/UniversalQuickCreate/UniversalQuickCreate/services/DocumentRecordService.ts
src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityDocumentConfig.ts
src/controls/UniversalQuickCreate/UniversalQuickCreate/ControlManifest.Input.xml
src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts
```

---

## Architecture Patterns

### Option A: @odata.bind (Primary)
```typescript
const payload = {
  sprk_documentname: "Document A",
  sprk_filename: "A.pdf",
  sprk_graphitemid: "01K...",
  sprk_graphdriveid: "driveId",
  sprk_filesize: 12345,
  sprk_documentdescription: "Description",
  ["sprk_matter@odata.bind"]: "/sprk_matters(3a785f76-c773-f011-b4cb-6045bdd8b757)"
};

await context.webAPI.createRecord("sprk_document", payload);
```

### Option B: Relationship URL (Fallback/Server)
```typescript
await (context.webAPI as any).execute({
  method: "POST",
  url: "/api/data/v9.2/sprk_matters(3a785f76-c773-f011-b4cb-6045bdd8b757)/sprk_matter_document",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({
    sprk_documentname: "Document A",
    sprk_filename: "A.pdf",
    sprk_graphitemid: "01K...",
    sprk_graphdriveid: "driveId",
    sprk_filesize: 12345,
    sprk_documentdescription: "Description"
  }),
  getMetadata: () => ({ boundParameter: null, parameterTypes: {}, operationType: 0, operationName: "" })
});
```

---

## Success Criteria

### Functional
- ✅ Document records created successfully with Option A
- ✅ Document records created successfully with Option B
- ✅ No "undeclared property" errors in browser console
- ✅ Custom Page closes on success
- ✅ Subgrid refreshes showing new Document records
- ✅ FormData (documentName, description) properly saved to records

### Performance
- ✅ 1 metadata query per relationship per session
- ✅ 100 files uploaded = 1 metadata call + 100 creates
- ✅ Cache persists for PCF lifecycle (browser session)
- ✅ No performance degradation vs current implementation

### Code Quality
- ✅ No regex typos (`/[{}]/g` used correctly)
- ✅ Whitelist payloads (no form data spreading)
- ✅ Comprehensive error handling
- ✅ JSDoc documentation on all public methods
- ✅ TypeScript type safety (no `any` where avoidable)

### Multi-Parent Readiness
- ✅ MetadataService supports dynamic parent entity resolution
- ✅ Configuration pattern documented for adding new parents
- ✅ Validation queries documented per parent type
- ✅ Cache keys isolated by environment + relationship

---

## Risks and Mitigation

### Risk 1: Metadata API Restrictions
**Risk:** User/app may not have permissions to query EntityDefinitions metadata
**Mitigation:**
- Defensive error handling with friendly messages
- Option to hardcode navigation properties for single-parent deployments
- Fallback to Option B (relationship URL) if metadata query fails

### Risk 2: Dataverse Caching
**Risk:** Browser/server caching prevents new version from loading
**Mitigation:**
- Version increment (2.1.0 → 2.2.0)
- Remove and re-add PCF control from form
- Hard browser cache clear
- Publish all customizations

### Risk 3: Multi-File Upload Performance
**Risk:** 100 files = 100 metadata calls could cause performance issues
**Mitigation:**
- Cache implementation (1 call per relationship per session)
- Metadata call happens BEFORE file loop
- Cache key includes environment URL to prevent cross-org pollution

### Risk 4: Breaking Changes for Existing Deployments
**Risk:** Updated DocumentRecordService signature breaks existing callers
**Mitigation:**
- `formData` parameter is optional (backward compatible)
- Metadata service handles missing config gracefully
- Comprehensive testing before deployment

---

## Reference Documents

### Knowledge Management
- [KM-PCF-COMPONENT-DEPLOYMENT.md](../../docs/KM-PCF-COMPONENT-DEPLOYMENT.md) - Deployment procedures
- [MULTI-PARENT-SUPPORT-GUIDE.md](./MULTI-PARENT-SUPPORT-GUIDE.md) - Multi-parent configuration guide
- [API-PATTERNS-REFERENCE.md](./API-PATTERNS-REFERENCE.md) - Dataverse Web API patterns

### Architecture References
- [ARCHITECTURE.md](../quickcreate_pcf_component/ARCHITECTURE.md) - Original PCF architecture
- [CODE-REFERENCES.md](../quickcreate_pcf_component/CODE-REFERENCES.md) - Field mappings and entity relationships

### Previous Context
- [PCF CONTROL FIX INSTRUCTIONS 10-19-2025.md](../PCF CONTROL FIX INSTRUCTIONS 10-19-2025.md) - Original fix instructions (now superseded by this sprint)

---

## Definition of Done

### Code Complete
- [ ] All 6 tasks completed
- [ ] All files modified and tested
- [ ] Code review completed
- [ ] No linting errors or warnings

### Testing Complete
- [ ] All test cases in Task 6.6 passing
- [ ] Performance validated (no metadata query spam)
- [ ] Error handling validated
- [ ] Multi-file upload tested (10+ files)

### Deployment Complete
- [ ] PCF control deployed to dev environment
- [ ] Version 2.2.0 visible in browser
- [ ] Dataverse cache cleared successfully
- [ ] All customizations published

### Documentation Complete
- [ ] Task documents reviewed and accurate
- [ ] Multi-parent guide completed
- [ ] API patterns reference completed
- [ ] Future deployment procedures documented

---

## Timeline

| Task | Duration | Start | End | Owner |
|------|----------|-------|-----|-------|
| 6.1 - Metadata Validation | 0.5d | Day 1 AM | Day 1 PM | Dev |
| 6.2 - MetadataService | 1d | Day 2 | Day 2 | Dev |
| 6.3 - DocumentRecordService | 1d | Day 3 | Day 3 | Dev |
| 6.4 - Configuration | 0.5d | Day 4 AM | Day 4 PM | Dev |
| 6.5 - Deployment | 1d | Day 5 | Day 5 | Dev |
| 6.6 - Testing | 1d | Day 6 | Day 6 | QA/Dev |

**Total Duration:** 5 days (with 1 day buffer for issues)

---

## Environment Information

**Dataverse Org:** https://spaarkedev1.crm.dynamics.com
**Tenant ID:** a221a95e-6abc-4434-aecc-e48338a1b2f2
**PCF Client App ID:** 170c98e1-d486-4355-bcbe-170454e0207c
**BFF API App ID:** 1e40baad-e065-4aea-a8d4-4b7ab273458c
**Test Matter GUID:** 3a785f76-c773-f011-b4cb-6045bdd8b757
**Test Container ID:** b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50

---

## Next Steps

1. Review this sprint overview with stakeholders
2. Begin Task 6.1 - Metadata Validation
3. Proceed sequentially through tasks 6.2-6.6
4. Update task documents with actual results as implementation progresses
5. Conduct final review and retrospective after completion

---

**Last Updated:** 2025-10-19
**Sprint Lead:** Development Team
**Stakeholders:** Product Owner, Senior Developer (Expert Consultant)
