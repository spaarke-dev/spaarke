# SpeFileViewer: Office Editor Mode Enhancement

**Project**: SPE File Viewer - Office Online Editor Integration
**Date**: 2025-11-26
**Status**: Planning
**Version**: 1.0.4 (proposed)

## Executive Summary

This enhancement adds "Open in Editor" functionality to the SpeFileViewer PCF control, allowing users to edit Office documents (Word, Excel, PowerPoint) directly within Dataverse using Office Online. The solution reuses existing infrastructure and maintains SDAP's zero-trust security model via On-Behalf-Of (OBO) authentication.

## Business Requirements

### User Story
As a Dataverse user viewing an Office document in the SpeFileViewer control, I want to click "Open in Editor" to edit the file in Office Online, so that I can make changes without leaving the Dataverse interface.

### Key Features
1. **Office File Detection**: Automatically detect Office file types (.docx, .xlsx, .pptx, etc.)
2. **Editor Mode Toggle**: Switch between preview mode (read-only) and editor mode (full Office Online)
3. **Permission Awareness**: Inform users when they have read-only access to prevent confusion
4. **Security Enforcement**: Leverage existing OBO flow to ensure user permissions are enforced by SharePoint Embedded

## Architecture Overview

### Current State (v1.0.3)
```
┌─────────────────────────────────────────────────────────┐
│ SpeFileViewer PCF Control (Dataverse)                   │
│                                                          │
│  ┌────────────────────────────────────────────┐         │
│  │ FilePreview.tsx                            │         │
│  │  - Preview Mode Only                       │         │
│  │  - Displays SharePoint embed.aspx iframe   │         │
│  │  - nb=true parameter (no banner)           │         │
│  └────────────────────────────────────────────┘         │
│                        │                                 │
│                        ↓                                 │
│  ┌────────────────────────────────────────────┐         │
│  │ BffClient.ts                               │         │
│  │  - getPreviewUrl() → /preview-url endpoint │         │
│  └────────────────────────────────────────────┘         │
└───────────────────────┬─────────────────────────────────┘
                        │
                        │ HTTPS (Bearer Token)
                        │
                        ↓
┌─────────────────────────────────────────────────────────┐
│ Spe.Bff.Api (Azure Web App)                             │
│                                                          │
│  /api/documents/{id}/preview-url                        │
│    - Returns preview URL (embed.aspx?nb=true)           │
│                                                          │
│  /api/documents/{id}/office                             │
│    - Returns Office webUrl (NOT USED YET)               │
│    - Uses OBO flow (ForUserAsync)                       │
└───────────────────────┬─────────────────────────────────┘
                        │
                        │ OBO Token Exchange
                        │
                        ↓
┌─────────────────────────────────────────────────────────┐
│ Microsoft Graph API                                      │
│  - Drives/{driveId}/Items/{itemId}/Preview               │
│  - Drives/{driveId}/Items/{itemId} (webUrl)              │
│  - Enforces user permissions via SPE                     │
└─────────────────────────────────────────────────────────┘
```

### Proposed State (v1.0.4)
```
┌─────────────────────────────────────────────────────────┐
│ SpeFileViewer PCF Control (Dataverse)                   │
│                                                          │
│  ┌────────────────────────────────────────────┐         │
│  │ FilePreview.tsx                            │         │
│  │  ┌──────────────────────────────────────┐  │         │
│  │  │ Preview Mode (default)               │  │         │
│  │  │  - SharePoint preview iframe         │  │         │
│  │  │  - "Open in Editor" button           │  │         │
│  │  │    (for Office files only)           │  │         │
│  │  └──────────────────────────────────────┘  │         │
│  │  ┌──────────────────────────────────────┐  │         │
│  │  │ Editor Mode (toggle)                 │  │         │
│  │  │  - Office Online iframe (webUrl)     │  │         │
│  │  │  - "Back to Preview" button          │  │         │
│  │  │  - Permission info dialog            │  │         │
│  │  └──────────────────────────────────────┘  │         │
│  └────────────────────────────────────────────┘         │
│                        │                                 │
│                        ↓                                 │
│  ┌────────────────────────────────────────────┐         │
│  │ BffClient.ts                               │         │
│  │  - getPreviewUrl() → /preview-url          │         │
│  │  - getOfficeUrl()  → /office (NEW)         │         │
│  └────────────────────────────────────────────┘         │
└───────────────────────┬─────────────────────────────────┘
                        │
                        │ HTTPS (Bearer Token)
                        │
                        ↓
┌─────────────────────────────────────────────────────────┐
│ Spe.Bff.Api (Azure Web App)                             │
│                                                          │
│  /api/documents/{id}/preview-url                        │
│    - Returns preview URL                                │
│                                                          │
│  /api/documents/{id}/office (ACTIVATED)                 │
│    - Returns { officeUrl, permissions }                 │
│    - Uses OBO flow (ForUserAsync)                       │
│    - Extracts permissions from driveItem                │
└───────────────────────┬─────────────────────────────────┘
                        │
                        │ OBO Token Exchange
                        │
                        ↓
┌─────────────────────────────────────────────────────────┐
│ Microsoft Graph API                                      │
│  - Drives/{driveId}/Items/{itemId}                       │
│    - Returns webUrl (Office Online editor URL)           │
│    - Returns permissions facet (user's access level)     │
│  - Enforces user permissions via SPE                     │
└─────────────────────────────────────────────────────────┘
```

## Security Model

### Zero-Trust Architecture (Unchanged)

The enhancement **does not introduce new security vulnerabilities** because:

1. **MSAL Authentication**: User must have valid access token
2. **BFF Authorization**: All endpoints require `RequireAuthorization()`
3. **OBO Flow**: Graph API calls use user's identity (not app-only)
4. **SharePoint Embedded**: Final arbiter of permissions (read/write/edit)
5. **Office Online**: Re-validates permissions when file loads

### Permission Flow
```
User clicks "Open in Editor"
    │
    ↓
PCF sends request with Bearer token
    │
    ↓
BFF validates token, exchanges for OBO token
    │
    ↓
Graph API checks user's SPE permissions
    │
    ├─→ Has Edit: Returns webUrl, user can edit
    │
    └─→ Read-Only: Returns webUrl, Office shows read-only mode
```

### UX Enhancement: Read-Only Dialog

**Critical for UX**: When user clicks "Open in Editor" but only has read-only access:

1. Office Online iframe loads in read-only mode (automatic)
2. PCF shows info dialog:
   - **Title**: "File Opened in Read-Only Mode"
   - **Message**: "You have view-only access to this file. To make changes, contact the file owner to request edit permissions."
   - **Icon**: Info (not error)

**Implementation**: Detect read-only from Office iframe load event OR driveItem.permissions facet.

## Files to Modify

### Frontend (PCF Control)

#### 1. **types.ts** (New TypeScript Interfaces)
**Path**: `C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\types.ts`

**Changes**:
```typescript
// Add new interface for Office URL response
export interface OfficeUrlResponse {
    officeUrl: string;
    permissions: {
        canEdit: boolean;
        canView: boolean;
        role: string; // 'owner' | 'editor' | 'reader'
    };
    correlationId: string;
}

// Add to FilePreviewState
export interface FilePreviewState {
    previewUrl: string | null;
    officeUrl: string | null; // NEW
    isLoading: boolean;
    error: string | null;
    documentInfo: DocumentInfo | null;
    mode: 'preview' | 'editor'; // NEW
    showReadOnlyDialog: boolean; // NEW
}
```

#### 2. **BffClient.ts** (New Method)
**Path**: `C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\BffClient.ts`

**Changes**:
```typescript
/**
 * Get Office Online editor URL for a document
 *
 * Calls: GET /api/documents/{documentId}/office
 *
 * @param documentId Document GUID
 * @param accessToken Bearer token from MSAL
 * @param correlationId Correlation ID for distributed tracing
 * @returns OfficeUrlResponse with editor URL and permissions
 * @throws Error if API call fails
 */
public async getOfficeUrl(
    documentId: string,
    accessToken: string,
    correlationId: string
): Promise<OfficeUrlResponse> {
    // Implementation similar to getPreviewUrl
    // Maps error codes (document_not_found, mapping_missing, etc.)
}
```

**Lines**: Add after `getPreviewUrl()` method (~195)

#### 3. **FilePreview.tsx** (UI Updates)
**Path**: `C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\FilePreview.tsx`

**Changes**:

**a) Add State**:
```typescript
this.state = {
    previewUrl: null,
    officeUrl: null,          // NEW
    isLoading: true,
    error: null,
    documentInfo: null,
    mode: 'preview',          // NEW
    showReadOnlyDialog: false // NEW
};
```

**b) Add Methods**:
```typescript
/**
 * Switch to editor mode
 */
private handleOpenEditor = async (): Promise<void> => {
    // Call BffClient.getOfficeUrl()
    // Update state: mode='editor', officeUrl=response.officeUrl
    // If !canEdit, set showReadOnlyDialog=true
}

/**
 * Switch back to preview mode
 */
private handleBackToPreview = (): void => {
    this.setState({ mode: 'preview', showReadOnlyDialog: false });
}

/**
 * Check if file is Office type
 */
private isOfficeFile(extension?: string): boolean {
    const officeExtensions = ['docx', 'doc', 'xlsx', 'xls', 'pptx', 'ppt'];
    return officeExtensions.includes(extension?.toLowerCase() || '');
}

/**
 * Dismiss read-only dialog
 */
private dismissReadOnlyDialog = (): void => {
    this.setState({ showReadOnlyDialog: false });
}
```

**c) Update Render**:
```typescript
// In render method, add conditional rendering:

{/* Open in Editor Button (Preview Mode Only) */}
{mode === 'preview' && !isLoading && !error && previewUrl &&
 this.isOfficeFile(documentInfo?.fileExtension) && (
    <button
        className="spe-file-viewer__open-editor-button"
        onClick={this.handleOpenEditor}
        aria-label="Open in Editor"
    >
        Open in Editor
    </button>
)}

{/* Back to Preview Button (Editor Mode Only) */}
{mode === 'editor' && (
    <button
        className="spe-file-viewer__back-to-preview-button"
        onClick={this.handleBackToPreview}
        aria-label="Back to Preview"
    >
        Back to Preview
    </button>
)}

{/* Iframe - Dynamic src based on mode */}
<iframe
    className="spe-file-viewer__iframe"
    src={mode === 'editor' ? officeUrl : previewUrl}
    title={mode === 'editor' ? 'Office Editor' : 'File Preview'}
    sandbox="allow-same-origin allow-scripts allow-forms allow-popups allow-popups-to-escape-sandbox"
    allow="autoplay"
/>

{/* Read-Only Dialog */}
{showReadOnlyDialog && (
    <Dialog
        hidden={!showReadOnlyDialog}
        onDismiss={this.dismissReadOnlyDialog}
        dialogContentProps={{
            type: DialogType.normal,
            title: 'File Opened in Read-Only Mode',
            subText: 'You have view-only access to this file. To make changes, contact the file owner to request edit permissions.'
        }}
    >
        <DialogFooter>
            <PrimaryButton onClick={this.dismissReadOnlyDialog} text="OK" />
        </DialogFooter>
    </Dialog>
)}
```

**Lines to Modify**:
- State initialization: ~26-32
- Add methods: ~125-190
- Update render: ~165-187

#### 4. **SpeFileViewer.css** (Button Styles)
**Path**: `C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\css\SpeFileViewer.css`

**Changes**:
```css
/* ========== Action Buttons ========== */

/* Open in Editor button (floating top-right) */
.spe-file-viewer__open-editor-button {
    position: absolute;
    top: 12px;
    right: 12px;
    z-index: 10;
    padding: 8px 16px;
    background-color: #0078d4; /* Fluent UI primary blue */
    color: white;
    border: none;
    border-radius: 2px;
    font-size: 14px;
    font-weight: 600;
    cursor: pointer;
    transition: background-color 0.2s ease;
    box-shadow: 0 2px 4px rgba(0, 0, 0, 0.2);
}

.spe-file-viewer__open-editor-button:hover {
    background-color: #106ebe;
}

.spe-file-viewer__open-editor-button:active {
    background-color: #005a9e;
}

.spe-file-viewer__open-editor-button:focus {
    outline: 2px solid #605e5c;
    outline-offset: 2px;
}

/* Back to Preview button (floating top-left) */
.spe-file-viewer__back-to-preview-button {
    position: absolute;
    top: 12px;
    left: 12px;
    z-index: 10;
    padding: 8px 16px;
    background-color: #f3f2f1; /* Fluent UI neutral */
    color: #323130;
    border: 1px solid #8a8886;
    border-radius: 2px;
    font-size: 14px;
    font-weight: 600;
    cursor: pointer;
    transition: background-color 0.2s ease;
    box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
}

.spe-file-viewer__back-to-preview-button:hover {
    background-color: #e1dfdd;
}

.spe-file-viewer__back-to-preview-button:active {
    background-color: #d2d0ce;
}

.spe-file-viewer__back-to-preview-button:focus {
    outline: 2px solid #605e5c;
    outline-offset: 2px;
}

/* Container adjustment for floating buttons */
.spe-file-viewer__preview {
    position: relative; /* Enable absolute positioning for buttons */
}
```

**Lines to Add**: After line 87 (after `.spe-file-viewer__iframe`)

#### 5. **Solution.xml** (Version Bump)
**Path**: `C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewerSolution\src\Other\Solution.xml`

**Changes**:
```xml
<Version>1.0.4</Version>
```

**Line**: 11

### Backend (BFF API)

#### 6. **FileAccessEndpoints.cs** (Response Enhancement)
**Path**: `c:\code_files\spaarke\src\api\Spe.Bff.Api\Api\FileAccessEndpoints.cs`

**Changes**:

**Modify GetOffice endpoint** (lines 323-388) to return permissions:

```csharp
// 4. Get Office web app URL using OBO
var graphClient = await graphFactory.ForUserAsync(context, ct);

var driveItem = await graphClient.Drives[document.GraphDriveId!]
    .Items[document.GraphItemId!]
    .GetAsync(requestConfiguration =>
    {
        // Request additional fields for permissions
        requestConfiguration.QueryParameters.Select = new[] {
            "id", "name", "webUrl", "permissions"
        };
        requestConfiguration.QueryParameters.Expand = new[] { "permissions" };
    }, cancellationToken: ct);

if (string.IsNullOrEmpty(driveItem?.WebUrl))
{
    throw new SdapProblemException(
        "office_url_not_available",
        "Office URL Not Available",
        $"Graph API did not return a webUrl for document {documentId}",
        500
    );
}

// 5. Extract user permissions from driveItem
var canEdit = driveItem.Permissions?.Any(p =>
    p.Roles?.Contains("write") == true ||
    p.Roles?.Contains("owner") == true
) ?? false;

var canView = driveItem.Permissions?.Any(p =>
    p.Roles?.Contains("read") == true
) ?? true; // Default to view if no permissions returned

var userRole = canEdit ? "editor" : "reader";

logger.LogInformation(
    "Office URL retrieved | CanEdit: {CanEdit} | CanView: {CanView} | Role: {Role}",
    canEdit, canView, userRole
);

// 6. Return structured response
return TypedResults.Ok(new
{
    officeUrl = driveItem.WebUrl,
    permissions = new
    {
        canEdit = canEdit,
        canView = canView,
        role = userRole
    },
    correlationId = context.TraceIdentifier
});
```

**Alternative (Simpler)**: If permissions facet is unreliable, return basic response and let Office Online handle read-only mode:

```csharp
// Simplified: Office Online will enforce permissions
return TypedResults.Ok(new
{
    officeUrl = driveItem.WebUrl,
    permissions = new
    {
        canEdit = true, // Unknown at API level
        canView = true,
        role = "unknown"
    },
    correlationId = context.TraceIdentifier
});
```

**Decision**: Start with simplified approach (Option 2), add permission detection if UX requires it.

## Implementation Plan

### Phase 1: Core Functionality
1. Add TypeScript interfaces (types.ts)
2. Add BffClient.getOfficeUrl() method
3. Add FilePreview state and methods
4. Add "Open in Editor" button with basic toggle
5. Test with Office files (Word, Excel, PowerPoint)

### Phase 2: UX Enhancements
6. Add read-only dialog (Fluent UI Dialog)
7. Add permission detection (if needed)
8. Add button animations/loading states
9. Test with read-only users

### Phase 3: Testing & Deployment
10. Update Solution.xml version to 1.0.4
11. Build PCF and solution package
12. Deploy to Dataverse
13. User acceptance testing
14. Update documentation

## Testing Strategy

### Unit Tests
- `isOfficeFile()` correctly identifies Office extensions
- State transitions (preview ↔ editor)
- Error handling for missing permissions

### Integration Tests
1. **Edit Permissions**: User with edit access can open editor and make changes
2. **Read-Only Access**: User with view-only access sees read-only mode dialog
3. **Non-Office Files**: Button does not appear for PDFs, images, etc.
4. **Error Scenarios**: Missing driveId/itemId, network errors, token expiration

### User Acceptance Testing
- Test with real Dataverse users
- Verify Office Online loads correctly
- Confirm read-only dialog clarity
- Test "Back to Preview" workflow

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Office Online doesn't load** | High | Test with all Office file types; add error boundary |
| **Read-only confusion** | Medium | Implement clear dialog with actionable message |
| **Permission detection unreliable** | Low | Use simplified approach; rely on Office Online enforcement |
| **Performance degradation** | Low | Reuse existing iframe; no new API calls on initial load |
| **Security bypass** | Critical | Already mitigated by OBO flow + SPE permissions |

## Dependencies

### Existing Infrastructure (No Changes Required)
- `/api/documents/{id}/office` endpoint (already exists)
- OBO authentication flow (already implemented)
- Graph API permissions (already configured)
- MSAL token acquisition (already working)

### New Dependencies
- **Fluent UI Dialog** (may need npm install if not already included)
  - Check: `@fluentui/react` package includes Dialog component
  - If missing: `npm install @fluentui/react --save`

## Rollback Plan

If issues occur after deployment:
1. **Immediate**: Hide button via CSS (deploy CSS hotfix)
2. **Short-term**: Revert to v1.0.3 solution package
3. **Long-term**: Fix issues and redeploy v1.0.4

## Success Criteria

- [ ] Office files show "Open in Editor" button
- [ ] Button opens Office Online editor in same iframe
- [ ] Non-Office files do not show button
- [ ] Read-only users see informative dialog
- [ ] Edit users can make changes and save
- [ ] "Back to Preview" returns to preview mode
- [ ] No security vulnerabilities introduced
- [ ] Performance remains acceptable (< 2s load time)

## References

- **Microsoft Graph API**: [DriveItem Resource](https://learn.microsoft.com/en-us/graph/api/resources/driveitem)
- **Office Online**: [Office Viewer/Editor URLs](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/concepts/app-concepts/office-experiences)
- **OBO Flow**: [FileAccessEndpoints.cs](c:\code_files\spaarke\src\api\Spe.Bff.Api\Api\FileAccessEndpoints.cs)
- **Current PCF**: [SpeFileViewer v1.0.3](c:\code_files\spaarke\src\controls\SpeFileViewer)

## Next Steps

1. Review this technical overview with team
2. Create individual task documents (see TASKS.md)
3. Begin implementation (Phase 1)
4. Schedule testing session with stakeholders
