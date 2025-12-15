# SDAP File Viewer Enhancements - Phase 2

> **Project**: sdap-fileviewer-enhancements-2
> **Status**: Planned
> **Created**: December 15, 2025
> **Priority**: Medium

---

## Background

### DocumentOperations.js - Current State

The `sprk_DocumentOperations` webresource (`src/dataverse/webresources/spaarke_documents/DocumentOperations.js`) contains file management operations for SharePoint Embedded integration. However, **this code is currently unused** - there are no ribbon buttons configured to call these functions.

#### File Location
- **Source**: `src/dataverse/webresources/spaarke_documents/DocumentOperations.js`
- **Dataverse Name**: `sprk_DocumentOperations`
- **Solution**: `spaarke_core`
- **Version**: 1.1.0

#### Functions Available

| Function | Purpose | Parameters |
|----------|---------|------------|
| `uploadFile(primaryControl)` | Upload file to new document | Form context |
| `downloadFile(primaryControl)` | Download file from document | Form context |
| `replaceFile(primaryControl)` | Replace existing file | Form context |
| `deleteFile(primaryControl)` | Delete file from document | Form context |

#### Architecture

```
DocumentOperations.js
  │
  ├── ensureInitialized()     ← Lazy init (v1.1.0)
  ├── getApiBaseUrl()         ← Environment detection (DEV/UAT/PROD)
  │
  ├── uploadFile()            ← File picker → POST to BFF → Graph API
  ├── downloadFile()          ← GET from BFF → Blob download
  ├── replaceFile()           ← Confirm → Upload new file
  └── deleteFile()            ← Confirm → DELETE via BFF
        │
        ↓
    BFF API (spe-api-dev)
        │
        ↓
    Microsoft Graph API
        │
        ↓
    SharePoint Embedded Container
```

### Why It's NOT a Ribbon Function

The original design intended these functions for **entity ribbon buttons** on the Document main form. However, this approach has several problems:

1. **ADR-006 Compliance**: ADR-006 states "No new legacy JS webresources" - PCF controls are preferred
2. **Context Mismatch**: Entity ribbon operates at the record level, but file operations often happen from:
   - Subgrid views (multiple documents)
   - Custom pages (Analysis Workspace)
   - PCF control toolbars (SpeFileViewer, UniversalDatasetGrid)
3. **Duplication**: PCF controls already have TypeScript implementations of these operations
4. **UX Consistency**: Users expect file operations in the document viewer/grid, not the form ribbon

### Current PCF Implementation

The PCF controls already have their own file operation services:

```
src/client/pcf/UniversalDatasetGrid/control/services/
├── FileDownloadService.ts    ✅ Implemented
├── FileDeleteService.ts      ✅ Implemented
├── FileReplaceService.ts     ✅ Implemented
└── SdapApiClient.ts          ✅ API client
```

These TypeScript services:
- Use the same BFF API endpoints as DocumentOperations.js
- Are properly typed with TypeScript
- Integrate with the PCF control's command bar
- Follow modern React patterns

---

## Problem Statement

The `DocumentOperations.js` webresource is **dead code** - it's deployed to Dataverse but never invoked. The ribbon buttons were never created, and the PCF controls have their own implementations.

This creates confusion and maintenance burden:
- Two implementations of the same functionality
- Webresource that appears functional but isn't wired up
- README documentation that describes non-existent ribbon buttons

---

## Proposed Solution

### Option A: Delete DocumentOperations.js (Recommended)

Since PCF controls have their own TypeScript implementations, remove the unused webresource:

1. Remove `sprk_DocumentOperations` from `spaarke_core` solution
2. Delete source file at `src/dataverse/webresources/spaarke_documents/DocumentOperations.js`
3. Update documentation to reference PCF services instead

**Pros**: Clean codebase, single source of truth
**Cons**: Loss of standalone form-based file operations (if ever needed)

### Option B: Enhance SpeFileViewer PCF Control

If file operations are needed in contexts where PCF controls aren't available (e.g., native Document form), enhance the SpeFileViewer PCF control to be embeddable on forms:

1. Add SpeFileViewer to Document main form
2. Configure command bar with Upload/Download/Replace/Delete buttons
3. Remove DocumentOperations.js (redundant)

**Pros**: Consistent UX, ADR-006 compliant
**Cons**: Requires form customization

### Option C: Keep As Utility Library

Refactor DocumentOperations.js as a shared utility that PCF controls can import:

1. Convert to ES module format
2. Export functions for use by PCF controls
3. Remove ribbon-specific code
4. Bundle with PCF controls

**Pros**: Code reuse
**Cons**: Increases PCF bundle size, npm package management complexity

---

## Recommendation

**Option A (Delete)** is recommended because:

1. PCF services are already complete and tested
2. DocumentOperations.js has never been used in production
3. Eliminates ADR-006 compliance concerns
4. Reduces maintenance surface area

If form-based file operations become a requirement in the future, enhance the SpeFileViewer PCF control (Option B) rather than wiring up the legacy webresource.

---

## Action Items

### Immediate (No Code Changes)

- [x] Document current state (this document)
- [ ] Confirm with stakeholders that DocumentOperations.js is not needed

### Future Project Tasks

If proceeding with Option A (Delete):

1. Remove `sprk_DocumentOperations` from spaarke_core solution
2. Delete `src/dataverse/webresources/spaarke_documents/DocumentOperations.js`
3. Delete `src/dataverse/webresources/spaarke_documents/README.md`
4. Update any documentation referencing the webresource

If proceeding with Option B (PCF Enhancement):

1. Add SpeFileViewer control to Document main form
2. Configure control properties for file operations
3. Test file operations from Document form
4. Remove DocumentOperations.js (redundant)

---

## Related Files

| File | Description |
|------|-------------|
| `src/dataverse/webresources/spaarke_documents/DocumentOperations.js` | Unused webresource |
| `src/dataverse/webresources/spaarke_documents/README.md` | Documentation for unused code |
| `src/client/pcf/UniversalDatasetGrid/control/services/FileDownloadService.ts` | PCF implementation |
| `src/client/pcf/UniversalDatasetGrid/control/services/FileDeleteService.ts` | PCF implementation |
| `src/client/pcf/UniversalDatasetGrid/control/services/FileReplaceService.ts` | PCF implementation |
| `src/client/pcf/SpeFileViewer/` | File viewer PCF control |

---

## References

- **ADR-006**: PCF Controls over Legacy Webresources
- **SDAP Architecture**: `docs/ai-knowledge/architecture/sdap-architecture.md`
- **PCF Patterns**: `docs/ai-knowledge/architecture/sdap-pcf-patterns.md`
