# Sprint 6: SDAP + Universal Dataset Grid Integration

**Project**: SharePoint Document Access Platform (SDAP) + Universal Dataset Grid
**Sprint Start**: 2025-10-04
**Sprint Duration**: 2-3 weeks (estimated)
**Status**: 🚀 PLANNING

---

## Executive Summary

Sprint 6 integrates the **SDAP SharePoint Embedded document management system** with the **Universal Dataset Grid PCF control** to create a unified, powerful document management experience within Dataverse.

**Goal**: Enable users to manage SharePoint Embedded documents directly from the Universal Dataset Grid when viewing the `sprk_document` entity in forms, subgrids, and views.

---

## Business Objective

### The Vision

Users working with documents in Dataverse should be able to:
- ✨ **Add files** to SharePoint Embedded with one click
- ✨ **Remove files** from SharePoint while maintaining Dataverse records
- ✨ **Update/Replace files** with new versions
- ✨ **Download files** directly from the grid
- ✨ **Open files** via clickable SharePoint links
- ✨ **See real-time status** of document operations
- ✨ **Work seamlessly** across forms, subgrids, and views

### Current State

**What Works**:
- ✅ SDAP API: Complete document CRUD operations
- ✅ SDAP API: SharePoint Embedded file operations (upload, download, delete, update)
- ✅ SDAP API: OBO (On-Behalf-Of) authentication
- ✅ SDAP API: Granular authorization with AccessRights
- ✅ Universal Grid: Deployed and working on multiple entities
- ✅ Universal Grid: Works in forms, subgrids, views
- ✅ Dataverse: `sprk_document` entity with all required fields

**What's Missing**:
- ❌ File operation buttons in Universal Grid
- ❌ JavaScript integration between grid and SDAP API
- ❌ Custom commands for document operations
- ❌ Upload dialog and file picker
- ❌ Progress indicators for file operations
- ❌ Error handling for failed operations
- ❌ Automatic field updates after file operations

---

## Architecture Overview

### Integration Points

```
┌─────────────────────────────────────────────────────────────┐
│                    Power Platform UI                         │
│  ┌──────────────────────────────────────────────────────┐  │
│  │         Universal Dataset Grid PCF Control            │  │
│  │  ┌────────────────────────────────────────────────┐  │  │
│  │  │  Document Grid View (sprk_document entity)     │  │  │
│  │  │  ┌──────────────────────────────────────────┐  │  │  │
│  │  │  │ Custom Commands Toolbar:                │  │  │  │
│  │  │  │  [+Add File] [-Remove] [^Update] [↓DL]  │  │  │  │
│  │  │  └──────────────────────────────────────────┘  │  │  │
│  │  │                                                 │  │  │
│  │  │  Columns: Name, Type, Size, Link, Modified  │  │  │  │
│  │  │  ┌──────────────────────────────────────────┐  │  │  │
│  │  │  │ Document 1.pdf  | PDF  | 2.3MB | [Link]  │  │  │  │
│  │  │  │ Contract.docx   | Word | 156KB | [Link]  │  │  │  │
│  │  │  │ Invoice.xlsx    | Excel| 45KB  | [Link]  │  │  │  │
│  │  │  └──────────────────────────────────────────┘  │  │  │
│  │  └─────────────────────────────────────────────────┘  │  │
│  └──────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
                           │
                           │ JavaScript API Calls
                           ▼
┌─────────────────────────────────────────────────────────────┐
│                      SDAP BFF API                            │
│  ┌──────────────────────────────────────────────────────┐  │
│  │  /api/documents/{id}/upload        (POST)            │  │
│  │  /api/documents/{id}/download      (GET)             │  │
│  │  /api/documents/{id}/delete        (DELETE)          │  │
│  │  /api/documents/{id}/update        (PUT)             │  │
│  │  /api/documents                    (GET/POST)        │  │
│  └──────────────────────────────────────────────────────┘  │
│                           │                                  │
│          ┌────────────────┴────────────────┐                │
│          ▼                                  ▼                │
│  ┌───────────────┐               ┌──────────────────┐      │
│  │   Dataverse   │               │ SharePoint       │      │
│  │   Web API     │               │ Embedded (SPE)   │      │
│  │               │               │ Graph API        │      │
│  │ sprk_document │               │ File Storage     │      │
│  └───────────────┘               └──────────────────┘      │
└─────────────────────────────────────────────────────────────┘
```

### Component Responsibilities

#### 1. **Universal Dataset Grid (Enhanced)**
- Display `sprk_document` entity records
- Render custom command buttons for file operations
- Handle user interactions (button clicks)
- Call JavaScript helper functions
- Display operation progress and results
- Update grid after operations

#### 2. **JavaScript Web Resource (New)**
- `sprk_DocumentGridIntegration.js`
- Interface between grid and SDAP API
- Handle file picker dialogs
- Execute file upload/download/delete/update
- Update Dataverse record fields
- Error handling and user feedback

#### 3. **SDAP BFF API (Existing)**
- Process file operations
- Manage SharePoint Embedded files
- Update Dataverse records
- Handle authentication/authorization
- Return operation results

#### 4. **PCF Control Configuration (New)**
- Custom commands definition
- Entity-specific configuration for `sprk_document`
- Button labels and actions
- Permission-based visibility

---

## Sprint 6 Phases

### Phase 1: Configuration & Planning (Days 1-2)
**Duration**: 8 hours

**Objectives**:
- Define custom commands for file operations
- Design configuration schema for document grid
- Plan JavaScript API integration
- Define success criteria

**Deliverables**:
- Custom commands specification
- Configuration JSON schema
- API integration design
- Testing plan

### Phase 2: Enhanced Universal Grid (Days 3-5)
**Duration**: 16 hours

**Objectives**:
- Add custom command support to PCF control
- Implement command execution framework
- Add configuration parsing for document operations
- Test command infrastructure

**Deliverables**:
- Enhanced PCF control with custom commands
- Configuration support
- Command executor
- Unit tests

### Phase 3: JavaScript Integration (Days 6-8)
**Duration**: 20 hours

**Objectives**:
- Create `sprk_DocumentGridIntegration.js`
- Implement file upload with file picker
- Implement file download
- Implement file delete with confirmation
- Implement file update/replace
- Add progress indicators

**Deliverables**:
- Complete JavaScript web resource
- File operation handlers
- Error handling
- User feedback mechanisms

### Phase 4: Field Updates & Links (Days 9-10)
**Duration**: 8 hours

**Objectives**:
- Auto-populate file metadata fields
- Create clickable SharePoint links
- Handle field updates after operations
- Refresh grid after changes

**Deliverables**:
- Automatic field population
- Clickable file links
- Grid refresh logic
- Field mapping complete

### Phase 5: Testing & Refinement (Days 11-14)
**Duration**: 16 hours

**Objectives**:
- End-to-end testing of all operations
- Error scenario testing
- Permission-based testing
- Performance testing
- User acceptance testing

**Deliverables**:
- Test results documentation
- Bug fixes
- Performance optimizations
- User documentation

### Phase 6: Deployment & Documentation (Days 15)
**Duration**: 8 hours

**Objectives**:
- Deploy enhanced PCF control
- Deploy JavaScript web resource
- Create user guide
- Create admin guide

**Deliverables**:
- Production deployment
- User documentation
- Admin guide
- Training materials

---

## Technical Specifications

### Custom Commands Definition

```json
{
  "schemaVersion": "1.0",
  "entityConfigs": {
    "sprk_document": {
      "viewMode": "Grid",
      "enabledCommands": ["open", "refresh"],
      "customCommands": {
        "addFile": {
          "label": "+ Add File",
          "icon": "Add",
          "actionType": "javascript",
          "actionHandler": "Spaarke.DocumentGrid.addFile",
          "requiresSelection": false,
          "visibleWhen": "hasCreateAccess"
        },
        "removeFile": {
          "label": "- Remove File",
          "icon": "Delete",
          "actionType": "javascript",
          "actionHandler": "Spaarke.DocumentGrid.removeFile",
          "requiresSelection": true,
          "confirmMessage": "Are you sure you want to remove this file?",
          "visibleWhen": "hasDeleteAccess"
        },
        "updateFile": {
          "label": "^ Update File",
          "icon": "Upload",
          "actionType": "javascript",
          "actionHandler": "Spaarke.DocumentGrid.updateFile",
          "requiresSelection": true,
          "visibleWhen": "hasWriteAccess"
        },
        "downloadFile": {
          "label": "↓ Download",
          "icon": "Download",
          "actionType": "javascript",
          "actionHandler": "Spaarke.DocumentGrid.downloadFile",
          "requiresSelection": true,
          "visibleWhen": "hasReadAccess"
        }
      },
      "columns": {
        "include": ["sprk_name", "sprk_filetype", "sprk_filesize", "sprk_sharepointurl", "modifiedon"],
        "customRenderers": {
          "sprk_sharepointurl": {
            "type": "link",
            "label": "Open in SharePoint",
            "target": "_blank"
          }
        }
      }
    }
  }
}
```

### JavaScript API Integration

**File Upload Flow**:
```javascript
// 1. User clicks "+ Add File" button
// 2. PCF control calls: Spaarke.DocumentGrid.addFile(context, selectedRecords)
// 3. JavaScript opens file picker dialog
// 4. User selects file
// 5. JavaScript validates file (size, type)
// 6. JavaScript calls SDAP API: POST /api/documents/{id}/upload
// 7. API uploads to SharePoint Embedded
// 8. API updates Dataverse record fields
// 9. JavaScript updates PCF control data
// 10. Grid refreshes to show updated record
```

### Field Mappings

After file upload, these fields are automatically populated:

| Dataverse Field | Source | Example Value |
|----------------|--------|---------------|
| `sprk_filename` | File name | "Contract.pdf" |
| `sprk_filetype` | File extension | "pdf" |
| `sprk_filesize` | File size (bytes) | 2458624 |
| `sprk_sharepointurl` | SharePoint URL | "https://[tenant].sharepoint.com/..." |
| `sprk_sharepointfileid` | SharePoint file ID | "01ABC..." |
| `sprk_uploadedby` | Current user | Lookup to systemuser |
| `sprk_uploadedon` | Current timestamp | 2025-10-04T10:30:00Z |
| `sprk_modifiedon` | Current timestamp | 2025-10-04T10:30:00Z |

---

## Success Criteria

### Phase 1: Configuration & Planning ✅
- [ ] Custom commands specification approved
- [ ] Configuration schema defined
- [ ] API integration design documented
- [ ] Testing plan created

### Phase 2: Enhanced Universal Grid ✅
- [ ] Custom commands appear in toolbar
- [ ] Commands execute when clicked
- [ ] Configuration parsing works
- [ ] Command visibility rules working

### Phase 3: JavaScript Integration ✅
- [ ] File picker opens on "+ Add File"
- [ ] File upload completes successfully
- [ ] File download works
- [ ] File delete confirms and executes
- [ ] File update/replace works
- [ ] Progress indicators show during operations

### Phase 4: Field Updates & Links ✅
- [ ] File metadata auto-populates
- [ ] SharePoint URL is clickable
- [ ] Links open SharePoint in new tab
- [ ] Grid refreshes after operations

### Phase 5: Testing & Refinement ✅
- [ ] All operations work end-to-end
- [ ] Error scenarios handled gracefully
- [ ] Permissions respected (show/hide buttons)
- [ ] Performance acceptable (<3s per operation)
- [ ] No console errors

### Phase 6: Deployment & Documentation ✅
- [ ] Enhanced control deployed to SPAARKE DEV 1
- [ ] JavaScript web resource deployed
- [ ] Configuration applied to sprk_document
- [ ] User guide complete
- [ ] Admin guide complete

---

## Risks & Mitigation

### Risk 1: Bundle Size (Again) 🔴
**Probability**: High
**Impact**: High

**Issue**: Adding custom command support might increase bundle size
**Mitigation**:
- Use minimal version approach (proven to work)
- Keep command handling in JavaScript web resource
- PCF control just triggers JavaScript functions
- Monitor bundle size during development

### Risk 2: JavaScript-PCF Integration Complexity 🟡
**Probability**: Medium
**Impact**: Medium

**Issue**: Coordinating between PCF control and external JavaScript
**Mitigation**:
- Use well-defined message passing
- Implement robust error handling
- Test integration thoroughly
- Document integration patterns

### Risk 3: SDAP API Compatibility 🟢
**Probability**: Low
**Impact**: Medium

**Issue**: SDAP API might need modifications
**Mitigation**:
- Review existing API endpoints first
- Validate API is ready for integration
- Add endpoints only if needed
- Maintain backward compatibility

### Risk 4: Performance with Large File Lists 🟡
**Probability**: Medium
**Impact**: Medium

**Issue**: Grid might be slow with 100+ documents
**Mitigation**:
- Implement virtualization (Phase 2 from previous sprint)
- Use paging for document lists
- Optimize API calls
- Test with realistic data volumes

---

## Dependencies

### From Previous Sprints

**SDAP (Sprint 4 Complete)**:
- ✅ SDAP BFF API operational
- ✅ SharePoint Embedded integration working
- ✅ Document CRUD endpoints available
- ✅ File upload/download/delete/update endpoints
- ✅ OBO authentication working
- ✅ Authorization with AccessRights
- ✅ `sprk_document` entity in Dataverse

**Universal Grid (Sprint 5 Complete)**:
- ✅ PCF control deployed to SPAARKE DEV 1
- ✅ Minimal version working (9.89 KiB)
- ✅ Works on forms, subgrids, views
- ✅ Works across multiple entity types
- ✅ Basic grid rendering functional

### External Dependencies

- ✅ Dataverse environment (SPAARKE DEV 1) - Available
- ✅ SharePoint Embedded provisioned - Available
- ✅ SDAP API deployed - Assumed available
- ⏸️ User accounts for testing - Need to verify
- ⏸️ Sample documents for testing - Need to create

---

## Resource Requirements

### Development Time
- **Phase 1**: 8 hours (1 day)
- **Phase 2**: 16 hours (2 days)
- **Phase 3**: 20 hours (2.5 days)
- **Phase 4**: 8 hours (1 day)
- **Phase 5**: 16 hours (2 days)
- **Phase 6**: 8 hours (1 day)
- **Total**: 76 hours (~10 days of focused development)

### Resources Needed
- Developer time: 76 hours
- Testing time: Included in Phase 5
- Documentation time: Included in Phase 6
- SDAP API access: Required
- Dataverse environment: Available
- SharePoint Embedded tenant: Required

---

## Timeline

### Sprint Duration: 2-3 Weeks

**Week 1** (Days 1-5):
- Monday-Tuesday: Phase 1 (Configuration & Planning)
- Wednesday-Friday: Phase 2 (Enhanced Universal Grid)

**Week 2** (Days 6-10):
- Monday-Wednesday: Phase 3 (JavaScript Integration)
- Thursday-Friday: Phase 4 (Field Updates & Links)

**Week 3** (Days 11-15):
- Monday-Thursday: Phase 5 (Testing & Refinement)
- Friday: Phase 6 (Deployment & Documentation)

**Contingency**: +3 days buffer for unexpected issues

---

## Next Steps

### Immediate Actions (This Week)

1. **Review & Approve Sprint Plan** (1 hour)
   - Review this document
   - Validate approach
   - Confirm timeline and resources
   - Get stakeholder approval

2. **Environment Validation** (2 hours)
   - Verify SDAP API is accessible
   - Confirm SharePoint Embedded is working
   - Test file upload/download via API directly
   - Validate user permissions

3. **Start Phase 1** (8 hours)
   - Begin configuration design
   - Define custom commands
   - Design JavaScript integration
   - Create detailed task breakdown

### Weekly Milestones

**Week 1 Goal**: Enhanced PCF control with custom command framework
**Week 2 Goal**: Full JavaScript integration with file operations
**Week 3 Goal**: Tested, deployed, and documented solution

---

## Related Documentation

### SDAP Documentation
- [Sprint 4 Summary](../Sprint 4/SPRINT-4-FINALIZATION-COMPLETE.md)
- [SDAP Comprehensive Assessment](../SDAP-PROJECT-COMPREHENSIVE-ASSESSMENT.md)
- [JavaScript Integration Task 3.2](../Sprint 2/Task-3.2-JavaScript-File-Management-Integration.md)
- [Document CRUD API](../Sprint 2/Task-1.3-Document-CRUD-API-Endpoints.md)

### Universal Grid Documentation
- [Sprint 5 Summary](../../dataset_pcf_component/Sprint 5/TASK-5.3-DEPLOY-TEST-COMPLETE.md)
- [Deployment Success](../../dataset_pcf_component/Sprint 5/DEPLOYMENT-SUCCESS-FINAL.md)
- [Next Steps Roadmap](../../dataset_pcf_component/NEXT-STEPS-ROADMAP.md)

---

## Summary

Sprint 6 combines two major projects (SDAP and Universal Dataset Grid) into a unified, powerful document management solution. The integration will enable users to manage SharePoint Embedded documents directly from the familiar grid interface, providing a seamless experience across the Power Platform.

**Status**: Ready to begin
**Approval Required**: Yes
**First Task**: Phase 1 - Configuration & Planning

---

**Last Updated**: 2025-10-04
**Status**: Planning Complete
**Next Action**: Begin Phase 1
