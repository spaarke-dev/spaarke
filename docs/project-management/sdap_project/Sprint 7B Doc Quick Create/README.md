# Sprint 7B: Document Quick Create with Multi-File Upload

**Status:** Ready for Implementation
**Duration:** 5-7 days
**Last Updated:** 2025-10-07

---

## Overview

This sprint implements multi-file upload to SharePoint Embedded (SPE) from Dataverse Quick Create forms. Users can select multiple files, upload them in a single operation, and create multiple Document records with SPE metadata.

### Key Features

- ✅ Multi-file selection and upload from Quick Create
- ✅ Custom "Save and Create Documents" button in form footer
- ✅ Real-time upload progress with per-file status
- ✅ Adaptive upload strategy (sync-parallel vs long-running)
- ✅ Automatic Document record creation with SPE metadata
- ✅ Form auto-close and subgrid refresh
- ✅ Partial failure handling

---

## User Story (10 Steps)

1. User on Matter record, Documents tab
2. Clicks **"+ New Document"** → Quick Create opens
3. Quick Create shows file upload PCF control
4. User clicks **"+ Add File"** and selects file(s) - **multi-select supported**
5. User can add additional files or remove files
6. User fills optional fields (Description, etc.)
7. User clicks **"Save and Create Documents"** button in form footer
8. SPE upload process starts with progress bar
9. System uploads files to SPE and retrieves metadata
10. System creates **multiple Document records** (one per file)
11. Quick Create closes and **Document subgrid refreshes automatically**

---

## Architecture

```
┌────────────────────────────────────────────────────────┐
│              Quick Create Form                          │
│  ┌──────────────────────────────────────────────────┐  │
│  │      UniversalQuickCreate PCF Control            │  │
│  │  • FileUploadField (React)                       │  │
│  │  • UploadProgress (React)                        │  │
│  │  • MultiFileUploadService                        │  │
│  │  • DataverseRecordService                        │  │
│  └──────────────────────────────────────────────────┘  │
│                                                         │
│  [Custom Button: "Save and Create 3 Documents"]        │
└────────────────────────────────────────────────────────┘
                        ↓
                ┌──────────────┐
                │ SDAP BFF API  │
                └──────────────┘
                        ↓
        ┌──────────────────────────────┐
        │  SharePoint Embedded          │
        │  • Upload files               │
        │  • Return metadata            │
        └──────────────────────────────┘
                        ↓
        ┌──────────────────────────────┐
        │  Dataverse                    │
        │  • Create Document records    │
        │  • Populate SPE metadata      │
        └──────────────────────────────┘
```

---

## Implementation Plan

### Phase 1: Core Services (Days 1-2)

**Work Item 1:** Multi-File Upload Service (6 hours)
- Upload strategy selection (sync-parallel vs long-running)
- Batch management with adaptive batch size
- Progress tracking and callbacks
- Error handling and partial success

**Work Item 2:** Button Management (3 hours)
- Hide standard "Save and Close" button (CSS injection)
- Inject custom button in form footer (DOM manipulation)
- Dynamic button state (disabled/enabled/uploading)
- MutationObserver to re-inject if footer changes

---

### Phase 2: UI Components (Days 3-4)

**Work Item 3:** Update Manifest (1 hour)
- Change from dataset to field binding
- Configure properties (sdapApiBaseUrl, allowMultipleFiles)
- Declare features (WebAPI, Utility)

**Work Item 4:** File Upload UI (4 hours)
- File picker with multi-select
- Selected files list (name, size, remove button)
- Optional description field
- Fluent UI styling

**Work Item 5:** Upload Progress Component (3 hours)
- Overall progress bar (0-100%)
- Per-file status icons (pending/uploading/complete/failed)
- Current file indicator
- Error messages for failed files

**Work Item 6:** Integration Layer (5 hours)
- Connect all services and components
- PCF lifecycle methods (init, updateView, destroy)
- Form data extraction
- Parent context extraction
- Close form and refresh subgrid

---

### Phase 3: Deployment & Testing (Days 5-7)

**Work Item 7:** Configure Quick Create Form (2 hours)
- Enable Quick Create for Document entity
- Add PCF control to form
- Configure control properties
- Add optional fields (Title, Description)
- Publish customizations

**Work Item 8:** Build and Deploy (2 hours)
- Build PCF control (`npm run build`)
- Package solution (`pac solution pack`)
- Import to Dataverse
- Verify control registration

**Work Item 9:** End-to-End Testing (4 hours)
- Happy path (single file, multiple files)
- Error scenarios (network failure, partial failure)
- Edge cases (no files, large files, special characters)
- Performance testing
- Browser compatibility

**Work Item 10:** Documentation (3 hours)
- Administrator Guide
- End User Guide
- Developer Guide
- Troubleshooting Guide
- Configuration Reference

---

## Upload Strategies

### Sync-Parallel (Fast)

**Trigger:** ≤3 files AND each <10MB AND total <20MB
**Method:** `Promise.all()` - parallel upload
**Performance:** ~3-4 seconds
**Use Case:** Small file sets for speed

### Long-Running (Safe)

**Trigger:** >3 files OR large files
**Method:** Sequential batches (adaptive batch size: 2-5)
**Performance:** ~17-25 seconds for 5 files
**Use Case:** Large file sets, reliability

### Adaptive Batch Size

```typescript
if (avgSize < 1MB) → 5 files per batch
if (avgSize < 5MB) → 3 files per batch
else               → 2 files per batch
```

---

## Work Items

| # | Work Item | Time | Status |
|---|-----------|------|--------|
| 1 | Multi-File Upload Service | 6h | Ready |
| 2 | Button Management | 3h | Ready |
| 3 | Update Manifest | 1h | Ready |
| 4 | File Upload UI Component | 4h | Ready |
| 5 | Upload Progress Component | 3h | Ready |
| 6 | Integration Layer | 5h | Ready |
| 7 | Configure Quick Create Form | 2h | Ready |
| 8 | Build and Deploy | 2h | Ready |
| 9 | End-to-End Testing | 4h | Ready |
| 10 | Documentation | 3h | Ready |

**Total Estimated Time:** 33 hours (5-7 days)

---

## Key Files

### Work Items (Instructional)
- [WORK-ITEM-1-MULTIFILE-UPLOAD-SERVICE.md](WORK-ITEM-1-MULTIFILE-UPLOAD-SERVICE.md)
- [WORK-ITEM-2-BUTTON-MANAGEMENT.md](WORK-ITEM-2-BUTTON-MANAGEMENT.md)
- [WORK-ITEM-3-UPDATE-MANIFEST.md](WORK-ITEM-3-UPDATE-MANIFEST.md)
- [WORK-ITEM-4-FILE-UPLOAD-UI.md](WORK-ITEM-4-FILE-UPLOAD-UI.md)
- [WORK-ITEM-5-PROGRESS-COMPONENT.md](WORK-ITEM-5-PROGRESS-COMPONENT.md)
- [WORK-ITEM-6-INTEGRATION.md](WORK-ITEM-6-INTEGRATION.md)
- [WORK-ITEM-7-CONFIGURE-FORM.md](WORK-ITEM-7-CONFIGURE-FORM.md)
- [WORK-ITEM-8-BUILD-DEPLOY.md](WORK-ITEM-8-BUILD-DEPLOY.md)
- [WORK-ITEM-9-TESTING.md](WORK-ITEM-9-TESTING.md)
- [WORK-ITEM-10-DOCUMENTATION.md](WORK-ITEM-10-DOCUMENTATION.md)

### Code References (Complete Patterns)
- [CODE-REFERENCE-BUTTON-MANAGEMENT.md](CODE-REFERENCE-BUTTON-MANAGEMENT.md)
- [CODE-REFERENCE-UI-COMPONENTS.md](CODE-REFERENCE-UI-COMPONENTS.md)

### Planning Documents
- [SPRINT-7B-SCOPE.md](SPRINT-7B-SCOPE.md) - Complete scope and requirements
- [IMPLEMENTATION-PLAN.md](IMPLEMENTATION-PLAN.md) - Detailed 5-7 day plan

---

## Technical Constraints

### ADR: No Backend Plugins
All processing happens in PCF frontend. No Dataverse plugins used.

### Quick Create Limitations
- Only supports field-level controls (not dataset controls)
- Solution: Bind to `sprk_fileuploadmetadata` field (value not used)

### Button Replacement
- Standard "Save and Close" creates only ONE record
- Solution: Hide standard button, inject custom button, bypass Quick Create save

### Multiple Records
- Quick Create designed for single record
- Solution: PCF creates records directly via `context.webAPI.createRecord()`

---

## Success Criteria

### Functional
- ✅ User can select multiple files from Quick Create
- ✅ Custom button appears in form footer
- ✅ Upload shows real-time progress
- ✅ Multiple Document records created (one per file)
- ✅ All SPE metadata populated correctly
- ✅ Form closes automatically
- ✅ Subgrid refreshes without manual refresh
- ✅ Partial failures handled gracefully

### Performance
- ✅ Single file (<1MB): <3 seconds
- ✅ 3 files (sync-parallel): <5 seconds
- ✅ 5 files (long-running): <25 seconds
- ✅ 10 files: <45 seconds

### Quality
- ✅ No console errors
- ✅ Works in Chrome, Edge, Firefox
- ✅ Error messages clear and actionable
- ✅ UI matches Power Apps styling

---

## Dependencies

### NPM Packages
- `react@18.2.0`
- `react-dom@18.2.0`
- `@fluentui/react-components@9.54.0`
- `@fluentui/react-icons@2.0.239`
- `@azure/msal-browser@4.24.1`

### External Services
- **SDAP BFF API** - SharePoint Embedded file operations
- **SharePoint Embedded** - Container-based file storage
- **Dataverse Web API** - Record creation

### PCF Framework
- Power Apps Component Framework
- `pcf-scripts` for build
- `pac` CLI for deployment

---

## Related Documentation

### Existing Codebase
- [UniversalDatasetGrid PCF control](../../../src/controls/UniversalDatasetGrid/) - Reference for existing patterns
- [DataverseRecordService](../../src/controls/UniversalQuickCreate/UniversalQuickCreate/services/DataverseRecordService.ts) - Record creation service

### SDAP Project
- [TASK-7B-2A-MULTI-FILE-ENHANCEMENT.md](../Sprint 7_Dataset Grid to SDAP/TASK-7B-2A-MULTI-FILE-ENHANCEMENT.md) - Original enhancement task (basis for this sprint)
- [SDAP-PROJECT-COMPREHENSIVE-ASSESSMENT.md](../SDAP-PROJECT-COMPREHENSIVE-ASSESSMENT.md) - Overall project context

---

## Testing Strategy

### Unit Testing
- MultiFileUploadService strategy selection
- Batch size calculation
- Progress tracking accuracy

### Integration Testing
- PCF → SDAP API → SPE → Dataverse
- Form data extraction
- Parent relationship creation

### End-to-End Testing
- Full user workflow (10 steps)
- Multiple browsers
- Various file types and sizes
- Error scenarios

### Performance Testing
- Upload speed benchmarks
- Concurrent operations
- Large file handling

---

## Known Limitations

### Current Sprint Scope
- ❌ NO field inheritance (Matter → Document)
  → Deferred to future sprint, handled by backend process

- ❌ NO custom field mapping
  → Hardcoded for Document entity

- ❌ NO offline support
  → Requires network connection

### Future Enhancements
- Field inheritance from parent entity
- Configurable field mappings (JSON in control properties)
- Support for other entities (not just Document)
- Offline queue for uploads
- Duplicate file detection
- File preview before upload

---

## Getting Started

### For Developers

1. Review [SPRINT-7B-SCOPE.md](SPRINT-7B-SCOPE.md)
2. Read [IMPLEMENTATION-PLAN.md](IMPLEMENTATION-PLAN.md)
3. Start with [WORK-ITEM-1-MULTIFILE-UPLOAD-SERVICE.md](WORK-ITEM-1-MULTIFILE-UPLOAD-SERVICE.md)
4. Reference [CODE-REFERENCE-BUTTON-MANAGEMENT.md](CODE-REFERENCE-BUTTON-MANAGEMENT.md) for patterns
5. Follow work items sequentially 1-10

### For Administrators

1. Complete Work Item 8 (Build and Deploy)
2. Follow Work Item 7 (Configure Quick Create Form)
3. Test using Work Item 9 (Testing scenarios)
4. Refer to Admin Guide (Work Item 10)

### For End Users

1. Wait for deployment
2. Refer to User Guide (Work Item 10)
3. Watch training video (if provided)

---

## Support

### Issues
Report bugs or request features in project tracking system.

### Questions
Contact development team or refer to documentation.

### Troubleshooting
See [TROUBLESHOOTING-QUICK-CREATE.md](docs/TROUBLESHOOTING-QUICK-CREATE.md) (Work Item 10)

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0.0 | 2025-10-07 | Initial implementation plan |

---

## Contributors

- Sprint Lead: [Name]
- Developers: [Names]
- QA: [Names]
- Product Owner: [Name]

---

**Ready to begin implementation!** 🚀
