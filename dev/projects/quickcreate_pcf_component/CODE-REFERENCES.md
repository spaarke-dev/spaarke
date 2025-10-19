# Code References: Universal Document Upload PCF

This document catalogs all code files for the Universal Document Upload PCF migration project.

---

## Files to KEEP (No Changes)

These files work correctly and should remain unchanged:

### SPE Upload Infrastructure ✅
```
src/controls/UniversalQuickCreate/UniversalQuickCreate/
├── services/
│   ├── SdapApiClient.ts                    ✅ KEEP - BFF API client
│   ├── FileUploadService.ts                ✅ KEEP - File upload orchestration
│   └── auth/
│       └── MsalAuthProvider.ts             ✅ KEEP - OAuth2 authentication
│
├── utils/
│   └── logger.ts                           ✅ KEEP - Logging utility
│
└── config/
    └── msalConfig.ts                       ✅ KEEP - MSAL configuration
```

**Rationale:** SharePoint Embedded file upload works correctly. No Quick Create dependencies.

---

## Files to MODIFY

These files need refactoring to remove Quick Create dependencies:

### PCF Control (Major Changes) 🔄
```
src/controls/UniversalQuickCreate/UniversalQuickCreate/
├── UniversalQuickCreatePCF.ts              🔄 MODIFY - Remove Quick Create logic
│   │
│   ├── REMOVE:
│   │   • ButtonManagement integration
│   │   • Quick Create form context detection
│   │   • context.webAPI.createRecord() calls
│   │   • scheduleRecordCreation() method
│   │
│   └── ADD:
│       • Custom page parameter handling
│       • Xrm.WebApi integration
│       • Dialog close logic
│       • Version display
│
├── index.ts                                🔄 MODIFY - Update export name
│
├── ControlManifest.Input.xml              🔄 MODIFY - Change to custom page binding
│   │
│   ├── REMOVE:
│   │   • bound="true" field binding
│   │   • Quick Create description
│   │
│   └── ADD:
│       • Custom page input parameters
│       • parentEntityName property
│       • parentRecordId property
│       • containerId property
│       • parentDisplayName property
│
└── css/UniversalQuickCreate.css            🔄 MODIFY - Update dialog styles
```

### Services (Moderate Changes) 🔄
```
src/controls/UniversalQuickCreate/UniversalQuickCreate/services/
├── MultiFileUploadService.ts               🔄 MODIFY - Remove Phase 2 (record creation)
│   │
│   ├── REMOVE:
│   │   • handleSyncParallelUpload() - Phase 2 logic
│   │   • recordService.createDocument() calls
│   │   • documentRecordIds array
│   │
│   └── KEEP:
│       • Phase 1: File upload logic
│       • Validation logic
│       • Progress tracking
│
└── DataverseRecordService.ts               ❌ DELETE - Replace with new version
```

**Rationale:**
- `MultiFileUploadService` should only handle file uploads
- Record creation moves to new `DocumentRecordService` using `Xrm.WebApi`

---

## Files to CREATE (New)

### Configuration 🆕
```
src/controls/UniversalQuickCreate/UniversalQuickCreate/config/
└── EntityDocumentConfig.ts                 🆕 CREATE - Parent entity configuration
    │
    ├── interface EntityDocumentConfig
    ├── ENTITY_DOCUMENT_CONFIGS constant
    └── getEntityConfig() helper
```

**Purpose:** Central configuration for multi-entity support (Matter, Project, Invoice, Account, Contact)

### Services 🆕
```
src/controls/UniversalQuickCreate/UniversalQuickCreate/services/
└── DocumentRecordService.ts                🆕 CREATE - Xrm.WebApi record creation
    │
    ├── createDocuments() - Main method
    ├── buildRecordPayload() - OData formatting
    ├── getEntityConfig() - Configuration resolution
    └── Error handling
```

**Purpose:** Create Document records using `Xrm.WebApi.createRecord()` (no Quick Create limitations)

### UI Components (Fluent UI v9) 🆕
```
src/controls/UniversalQuickCreate/UniversalQuickCreate/components/
├── DocumentUploadForm.tsx                  🆕 CREATE - Main form container
│   ├── Fluent UI v9 Dialog
│   ├── Form sections (Upload File, Profile)
│   ├── Progress display
│   └── Button row (Upload & Create, Cancel)
│
├── FileSelectionField.tsx                  🆕 CREATE - File input with validation
│   ├── Multiple file selection
│   ├── File validation (size, type, count)
│   └── Selected files list
│
├── UploadProgressBar.tsx                   🆕 CREATE - Progress indicator
│   ├── File-by-file progress ("2 of 10")
│   ├── Percentage bar
│   └── Current file name
│
└── ErrorMessageList.tsx                    🆕 CREATE - Error display
    ├── Validation errors
    ├── Upload failures
    └── Retry options
```

**Design:** All components use Fluent UI v9 (`@fluentui/react-components`), NOT v8.

### Types 🆕
```
src/controls/UniversalQuickCreate/UniversalQuickCreate/types/
└── index.ts                                🔄 EXTEND - Add new types
    │
    ├── EXISTING TYPES (keep):
    │   • SpeFileMetadata
    │   • ServiceResult<T>
    │   • FileUploadRequest
    │
    └── NEW TYPES (add):
        • ParentContext
        • UploadedFileMetadata
        • CreateResult
        • UploadProgress
        • EntityDocumentConfig
```

---

## Custom Page & Command Integration 🆕

### Custom Page Definition
```
customizations/CustomPages/
└── sprk_DocumentUploadDialog.json          🆕 CREATE - Custom page metadata
    │
    ├── Page definition
    ├── PCF control binding
    ├── Input parameters mapping
    └── Dialog configuration
```

### Command Button
```
customizations/WebResources/
└── sprk_subgrid_commands.js                🆕 CREATE - Generic command script
    │
    ├── openDocumentUploadDialog()
    ├── Entity config resolution
    ├── Parameter extraction
    └── Navigation logic
```

### Ribbon Customization
```
customizations/RibbonCustomizations/
└── sprk_document_upload_button.xml         🆕 CREATE - Command button config
    │
    ├── Button definition (per entity)
    ├── Command definition
    ├── Enable rules
    └── Display rules
```

---

## Files to DELETE ❌

These files are no longer needed:

```
src/controls/UniversalQuickCreate/UniversalQuickCreate/
├── utils/
│   └── ButtonManagement.ts                 ❌ DELETE - Quick Create only
│
├── services/
│   └── DataverseRecordService.ts           ❌ DELETE - Uses context.webAPI (replaced)
│
└── components/
    ├── UploadProgress.tsx                  ❌ DELETE - Replaced with Fluent UI v9 version
    └── FileUploadField.tsx                 ❌ DELETE - Replaced with Fluent UI v9 version
```

**Rationale:**
- `ButtonManagement`: Only needed for Quick Create form footer manipulation
- Old `DataverseRecordService`: Uses `context.webAPI.createRecord()` which has Quick Create limitations
- Old React components: Not Fluent UI v9 compliant

---

## Reference Implementations

### Fluent UI v9 Patterns
**Source:** Universal Dataset Grid PCF (Sprint 5B - Fluent UI Compliance)

```
src/controls/UniversalDatasetGrid/UniversalDatasetGrid/
├── components/
│   ├── CommandBar.tsx                      📖 REFERENCE - Fluent UI v9 buttons
│   ├── DatasetGrid.tsx                     📖 REFERENCE - makeStyles() usage
│   └── UniversalDatasetGridRoot.tsx        📖 REFERENCE - Component structure
│
└── types/
    └── index.ts                            📖 REFERENCE - Type definitions
```

**Key Patterns to Copy:**
1. Import from `@fluentui/react-components`
2. Use `makeStyles()` hook for styling
3. Use design tokens (`tokens.spacingVerticalL`, etc.)
4. CSS Flexbox for layout (no Stack component)

### MSAL Authentication
**Source:** Universal Dataset Grid (Sprint 8 - MSAL Integration)

```
src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/auth/
└── MsalAuthProvider.ts                     📖 REFERENCE - OAuth2 flow
```

**Pattern:** Singleton provider, token acquisition, error handling

### File Upload Flow
**Source:** Current Universal Quick Create (keep as-is)

```
src/controls/UniversalQuickCreate/UniversalQuickCreate/services/
├── SdapApiClient.ts                        📖 REFERENCE - BFF API client
└── FileUploadService.ts                    📖 REFERENCE - Upload orchestration
```

**Pattern:** Service composition, error wrapping, progress callbacks

---

## File Organization Summary

| Category | Keep | Modify | Create | Delete | Total |
|----------|------|--------|--------|--------|-------|
| PCF Control | 0 | 2 | 0 | 0 | 2 |
| Services | 3 | 2 | 2 | 1 | 8 |
| UI Components | 0 | 0 | 4 | 2 | 6 |
| Configuration | 2 | 1 | 1 | 0 | 4 |
| Custom Page | 0 | 0 | 3 | 0 | 3 |
| **Total** | **5** | **5** | **10** | **3** | **23** |

---

## Migration Checklist

### Phase 1: Setup
- [ ] Create `EntityDocumentConfig.ts`
- [ ] Update `ControlManifest.Input.xml`
- [ ] Update `types/index.ts` with new types

### Phase 2: Services
- [ ] Create `DocumentRecordService.ts` (Xrm.WebApi)
- [ ] Modify `MultiFileUploadService.ts` (remove Phase 2)
- [ ] Delete old `DataverseRecordService.ts`

### Phase 3: PCF Control
- [ ] Modify `UniversalQuickCreatePCF.ts` (remove Quick Create dependencies)
- [ ] Add custom page parameter handling
- [ ] Remove `ButtonManagement` integration
- [ ] Delete `ButtonManagement.ts`

### Phase 4: UI Components
- [ ] Create `DocumentUploadForm.tsx` (Fluent UI v9)
- [ ] Create `FileSelectionField.tsx` (Fluent UI v9)
- [ ] Create `UploadProgressBar.tsx` (Fluent UI v9)
- [ ] Create `ErrorMessageList.tsx` (Fluent UI v9)
- [ ] Delete old `UploadProgress.tsx` and `FileUploadField.tsx`

### Phase 5: Custom Page
- [ ] Create custom page definition JSON
- [ ] Create command script `sprk_subgrid_commands.js`
- [ ] Create ribbon customization XML

### Phase 6: Testing
- [ ] Test file upload (10 files, 100MB)
- [ ] Test record creation (Xrm.WebApi)
- [ ] Test across all entity types
- [ ] Verify Fluent UI v9 compliance

---

## Code Metrics

### Estimated Lines of Code

| File Type | Lines | Complexity |
|-----------|-------|------------|
| EntityDocumentConfig.ts | ~150 | Low |
| DocumentRecordService.ts | ~200 | Medium |
| UniversalQuickCreatePCF.ts | ~500 | High |
| DocumentUploadForm.tsx | ~300 | Medium |
| FileSelectionField.tsx | ~150 | Low |
| UploadProgressBar.tsx | ~100 | Low |
| ErrorMessageList.tsx | ~100 | Low |
| sprk_subgrid_commands.js | ~150 | Medium |
| **Total New/Modified** | **~1,650** | **Medium** |

### Deleted Code
- ButtonManagement.ts: ~300 lines
- Old DataverseRecordService.ts: ~175 lines
- Old React components: ~200 lines
- **Total Deleted:** ~675 lines

**Net Change:** +975 lines (refactoring, not just additions)

---

**Next Step:** Proceed to [PHASE-1-SETUP.md](./PHASE-1-SETUP.md) to begin implementation.
