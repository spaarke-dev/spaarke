# Document Check-Out/Check-In Viewer

> **Project**: document-checkout-viewer
> **Status**: Design
> **Created**: December 17, 2025
> **Priority**: High

---

## Executive Summary

This specification defines a unified document viewing and editing component for the Spaarke platform. The component provides:

1. **Preview Mode**: Read-only document preview using SharePoint embed.aspx
2. **Edit Mode**: Full Office Online editing with check-out/check-in version control
3. **File Operations**: Delete, Download, Replace functionality
4. **Reusable Design**: Single component for use across Document forms and Analysis Workspace

The design addresses security concerns (Share button exposure) while providing a seamless user experience aligned with Power Platform conventions.

---

## Background

### Current State

The platform currently has two separate document viewer implementations:

| Component | Location | Usage |
|-----------|----------|-------|
| `SpeFileViewer` | `src/client/pcf/SpeFileViewer/` | Document entity form |
| `SourceDocumentViewer` | `src/client/pcf/AnalysisWorkspace/control/components/` | Analysis Workspace |

Both components have similar functionality but different implementations, violating DRY principles and ADR-012 (shared component library).

### Problem Statement

1. **Security**: Office Online embed view exposes Share button, allowing users to share documents outside the DMS
2. **Version Control**: No explicit version commit points - document changes are continuous
3. **AI Analysis**: AI needs stable document states to analyze, not in-progress edits
4. **Consistency**: Two separate viewer implementations need consolidation
5. **File Operations**: Delete functionality not available in current viewers

---

## Solution Overview

### Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          Dataverse Model-Driven App                          │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │                    Document Form / Analysis Workspace                  │  │
│  │  ┌─────────────────────────────────────────────────────────────────┐  │  │
│  │  │                    SpeDocumentViewer PCF Control                 │  │  │
│  │  │                                                                   │  │  │
│  │  │  ┌─────────────────────────────────────────────────────────────┐ │  │  │
│  │  │  │                    Toolbar (Fluent v9)                       │ │  │  │
│  │  │  │  [Doc Name]  [Refresh] [Edit/CheckOut] [CheckIn] [Delete]   │ │  │  │
│  │  │  └─────────────────────────────────────────────────────────────┘ │  │  │
│  │  │                                                                   │  │  │
│  │  │  ┌─────────────────────────────────────────────────────────────┐ │  │  │
│  │  │  │           PREVIEW MODE          │      EDIT MODE            │ │  │  │
│  │  │  │         (embed.aspx)            │    (embedview)            │ │  │  │
│  │  │  │                                 │                           │ │  │  │
│  │  │  │  • Read-only preview            │  • Full Office Online     │ │  │  │
│  │  │  │  • No Share button              │  • Share visible (OK)     │ │  │  │
│  │  │  │  • Cached (OK - stable)         │  • Real-time auto-save    │ │  │  │
│  │  │  │                                 │  • Comments enabled       │ │  │  │
│  │  │  └─────────────────────────────────────────────────────────────┘ │  │  │
│  │  └─────────────────────────────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
                                        │
                                        │ HTTPS + Bearer Token
                                        ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                              BFF API (Sprk.Bff.Api)                          │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │  GET  /api/documents/{id}/preview-url     → SharePoint embed URL      │  │
│  │  GET  /api/documents/{id}/open-links      → Office Online + Desktop   │  │
│  │  GET  /api/documents/{id}/content         → Download file             │  │
│  │  POST /api/documents/{id}/checkout        → Lock document (NEW)       │  │
│  │  POST /api/documents/{id}/checkin         → Unlock + version (NEW)    │  │
│  │  POST /api/documents/{id}/discard         → Cancel checkout (NEW)     │  │
│  │  DELETE /api/documents/{id}               → Delete doc + file (NEW)   │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                                        │                                     │
│                                        │ OBO + Managed Identity              │
│                                        ▼                                     │
│                    ┌──────────────────────────────────┐                     │
│                    │  SharePoint Embedded Container   │                     │
│                    │  + Dataverse (Document entity)   │                     │
│                    └──────────────────────────────────┘                     │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## User Experience Design

### State Machine

```
                              ┌──────────────┐
                              │   LOADING    │
                              │  (Spinner)   │
                              └──────┬───────┘
                                     │ Load complete
                                     ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                            PREVIEW MODE                                     │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │ Toolbar: [DocName] [Refresh] [Edit ✏️] [Download ⬇] [Delete 🗑]      │  │
│  ├──────────────────────────────────────────────────────────────────────┤  │
│  │                                                                       │  │
│  │                     embed.aspx (read-only)                           │  │
│  │                                                                       │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
└──────────────────────┬─────────────────────────────────────────────────────┘
                       │ Click "Edit" (user has Write permission)
                       ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                              EDIT MODE                                      │
│  Status: 🔒 Checked out by You                                             │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │ Toolbar: [DocName] [Open Desktop 💻] [Check In ✓] [Discard ✕]        │  │
│  ├──────────────────────────────────────────────────────────────────────┤  │
│  │                                                                       │  │
│  │                 Office Online embedview (full editing)               │  │
│  │                 - Comments, Share, full Word/Excel UI                │  │
│  │                 - Auto-saves to SPE                                  │  │
│  │                                                                       │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
└──────────────────────┬─────────────────────────────────────────────────────┘
                       │ Click "Check In"
                       ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                            PROCESSING                                       │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │                                                                       │  │
│  │          "Saving document... Updating preview... Running AI..."      │  │
│  │                            [Spinner]                                  │  │
│  │                                                                       │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
└──────────────────────┬─────────────────────────────────────────────────────┘
                       │ Processing complete
                       ▼
                    PREVIEW MODE (updated)
```

### Other Users View (Document Checked Out by Someone Else)

```
┌────────────────────────────────────────────────────────────────────────────┐
│                         LOCKED PREVIEW MODE                                 │
│  Status: 🔒 Checked out by Jane Smith (since 2:30 PM)                      │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │ Toolbar: [DocName] [Refresh] [Download ⬇]                            │  │
│  │          Edit button disabled with tooltip: "Checked out by Jane"    │  │
│  ├──────────────────────────────────────────────────────────────────────┤  │
│  │                                                                       │  │
│  │                     embed.aspx (last committed version)              │  │
│  │                                                                       │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────────────────┘
```

---

## Toolbar Design (Fluent v9)

### Design Principles

1. **Consistency**: Match Power Platform command bar styling
2. **Icon-first**: Use icon buttons with tooltips for compact design
3. **Contextual**: Show/hide buttons based on state and permissions
4. **Responsive**: Adapt to container width

### Toolbar Layout

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ 📄 Document Name.docx (24.5 KB)    │ [🔄] [✏️] [💻] [⬇️] [🗑️] │ [⬜ Expand] │
│ └─────── Left (info) ──────────────┘ └── Center (actions) ──┘ └─ Right ────┘│
└─────────────────────────────────────────────────────────────────────────────┘
```

### Button Specifications

| Button | Icon | Appearance | Visibility | Action |
|--------|------|------------|------------|--------|
| **Refresh** | `ArrowClockwiseRegular` | subtle, small | Always | Reload preview |
| **Edit / Check Out** | `EditRegular` | subtle, small | Preview mode + Write permission | Enter edit mode |
| **Open in Desktop** | `DesktopRegular` | subtle, small | Edit mode + Office file | Launch desktop app |
| **Check In** | `CheckmarkCircleRegular` | subtle, small, primary | Edit mode (checked out by user) | Commit and exit edit |
| **Discard** | `DismissCircleRegular` | subtle, small | Edit mode (checked out by user) | Cancel changes |
| **Download** | `ArrowDownloadRegular` | subtle, small | Always | Download file |
| **Delete** | `DeleteRegular` | subtle, small | Preview mode + Delete permission | Delete document |
| **Expand** | `FullScreenMaximize24Regular` | subtle, small | When onFullscreen prop provided | Open in floating dialog |

### Button States

```typescript
// Preview Mode (not checked out)
[Refresh] [Edit] [Download] [Delete] [Expand]

// Preview Mode (checked out by someone else)
[Refresh] [Edit:disabled] [Download] [Delete:disabled] [Expand]
// Tooltip on Edit: "Checked out by Jane Smith"

// Edit Mode (checked out by current user)
[Open Desktop] [Check In] [Discard]

// Processing State
[All buttons disabled with spinner]
```

### Fluent v9 Implementation

```typescript
import {
    makeStyles,
    Button,
    Tooltip,
    Text,
    Spinner,
    tokens
} from "@fluentui/react-components";
import {
    DocumentRegular,
    ArrowClockwiseRegular,
    EditRegular,
    CheckmarkCircleRegular,
    DismissCircleRegular,
    ArrowDownloadRegular,
    DeleteRegular,
    DesktopRegular,
    FullScreenMaximize24Regular
} from "@fluentui/react-icons";

const useStyles = makeStyles({
    toolbar: {
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
        borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
        backgroundColor: tokens.colorNeutralBackground2,
        minHeight: "40px"
    },
    toolbarInfo: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalS,
        flex: 1,
        overflow: "hidden"
    },
    toolbarActions: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS
    },
    statusBadge: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalXS,
        padding: `${tokens.spacingVerticalXXS} ${tokens.spacingHorizontalS}`,
        backgroundColor: tokens.colorPaletteYellowBackground2,
        borderRadius: tokens.borderRadiusMedium,
        fontSize: tokens.fontSizeBase200
    }
});
```

---

## Dataverse Schema Changes

### New Entity: File Version (`sprk_fileversion`)

A dedicated entity to track version history with full metadata for each check-out/check-in cycle.

#### Entity Metadata

| Property | Value |
|----------|-------|
| Schema Name | `sprk_fileversion` |
| Display Name | File Version |
| Plural Name | File Versions |
| Primary Field | `sprk_name` (auto-generated: "v{number} - {date}") |
| Ownership | Organization |

#### Fields

| Field | Schema Name | Type | Description |
|-------|-------------|------|-------------|
| Name | `sprk_name` | Text (100) | Auto-generated: "v2 - Dec 17, 2025" |
| Document | `sprk_documentid` | Lookup (sprk_document) | Parent document (required) |
| Version Number | `sprk_versionnumber` | Whole Number | Sequential version (1, 2, 3...) |
| Checked Out By | `sprk_checkedoutby` | Lookup (systemuser) | User who checked out |
| Checked Out At | `sprk_checkedoutat` | DateTime | When checked out |
| Checked In By | `sprk_checkedinby` | Lookup (systemuser) | User who checked in (may differ) |
| Checked In At | `sprk_checkedindat` | DateTime | When checked in |
| Comment | `sprk_comment` | Text (500) | Check-in comment |
| File Size | `sprk_filesize` | Whole Number | File size at check-in (bytes) |
| Status | `sprk_status` | Choice | CheckedOut, CheckedIn, Discarded |

#### Relationship

```
sprk_document (1) ──────── (N) sprk_fileversion
     │                              │
     │ sprk_currentversionid        │ sprk_documentid
     │ (Lookup to latest version)   │ (Parent document)
     ▼                              ▼
```

### Modified Entity: Document (`sprk_document`)

Add fields for quick access to current checkout status (denormalized for performance):

| Field | Schema Name | Type | Description |
|-------|-------------|------|-------------|
| Current Version | `sprk_currentversionid` | Lookup (sprk_fileversion) | Latest checked-in version |
| Current Version Number | `sprk_currentversionnumber` | Whole Number | Latest version number (denormalized) |
| Is Checked Out | `sprk_ischeckedout` | Yes/No | Quick check for lock status |
| Checked Out By | `sprk_checkedoutby` | Lookup (systemuser) | Current lock holder |
| Checked Out At | `sprk_checkedoutat` | DateTime | When current checkout started |

### Version Flow Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           CHECK-OUT FLOW                                     │
│                                                                              │
│  1. User clicks "Edit"                                                       │
│  2. BFF creates sprk_fileversion record:                                     │
│     - sprk_versionnumber = current + 1                                       │
│     - sprk_checkedoutby = current user                                       │
│     - sprk_checkedoutat = now                                                │
│     - sprk_status = "CheckedOut"                                             │
│  3. BFF updates sprk_document:                                               │
│     - sprk_ischeckedout = true                                               │
│     - sprk_checkedoutby = current user                                       │
│     - sprk_checkedoutat = now                                                │
│  4. Return edit URL to PCF                                                   │
│                                                                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                           CHECK-IN FLOW                                      │
│                                                                              │
│  1. User clicks "Check In"                                                   │
│  2. BFF updates sprk_fileversion record:                                     │
│     - sprk_checkedinby = current user (may differ from checkout user)        │
│     - sprk_checkedindat = now                                                │
│     - sprk_comment = user comment                                            │
│     - sprk_filesize = current file size                                      │
│     - sprk_status = "CheckedIn"                                              │
│  3. BFF updates sprk_document:                                               │
│     - sprk_currentversionid = this version                                   │
│     - sprk_currentversionnumber = this version number                        │
│     - sprk_ischeckedout = false                                              │
│     - sprk_checkedoutby = null                                               │
│     - sprk_checkedoutat = null                                               │
│  4. Trigger AI analysis                                                      │
│  5. Return preview URL to PCF                                                │
│                                                                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                           DISCARD FLOW                                       │
│                                                                              │
│  1. User clicks "Discard"                                                    │
│  2. BFF updates sprk_fileversion record:                                     │
│     - sprk_status = "Discarded"                                              │
│  3. BFF reverts SPE file to previous version (if possible)                   │
│  4. BFF updates sprk_document:                                               │
│     - sprk_ischeckedout = false                                              │
│     - sprk_checkedoutby = null                                               │
│     - sprk_checkedoutat = null                                               │
│  5. Return preview URL to PCF                                                │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Version History View

The Document form can include a subgrid showing version history:

```
┌────────────────────────────────────────────────────────────────────────────┐
│  Version History                                                            │
├──────┬──────────────┬───────────────┬───────────────┬─────────────────────┤
│  v3  │ Dec 17, 2:45 │ John Smith    │ Jane Smith    │ "Final review edits"│
│  v2  │ Dec 16, 4:30 │ Jane Smith    │ Jane Smith    │ "Added section 3"   │
│  v1  │ Dec 15, 9:00 │ John Smith    │ John Smith    │ "Initial upload"    │
├──────┼──────────────┼───────────────┼───────────────┼─────────────────────┤
│  Ver │ Checked In   │ Checked Out By│ Checked In By │ Comment             │
└────────────────────────────────────────────────────────────────────────────┘
```

### Security Model

| Permission | Required Role | Actions |
|------------|---------------|---------|
| View Preview | Read on sprk_document | View embed.aspx preview |
| View History | Read on sprk_fileversion | View version history subgrid |
| Edit/Check Out | Write on sprk_document | Enter edit mode, check out |
| Check In | Write on sprk_document | Commit changes, release lock |
| Delete | Delete on sprk_document | Delete document and SPE file |
| Force Unlock | Custom privilege (Admin) | Override someone else's checkout |

---

## BFF API Specification

### New Endpoints

#### POST /api/documents/{id}/checkout

Locks the document for editing by the current user.

**Request:**
```http
POST /api/documents/{id}/checkout
Authorization: Bearer {token}
X-Correlation-Id: {uuid}
```

**Response (200 OK):**
```json
{
    "success": true,
    "checkedOutBy": {
        "id": "user-guid",
        "name": "John Smith",
        "email": "john@company.com"
    },
    "checkedOutAt": "2025-12-17T14:30:00Z",
    "editUrl": "https://tenant.sharepoint.com/...?action=embedview",
    "desktopUrl": "ms-word:ofe|u|https://...",
    "correlationId": "{uuid}"
}
```

**Response (409 Conflict):**
```json
{
    "error": "document_locked",
    "detail": "Document is checked out by Jane Smith since 2:30 PM",
    "checkedOutBy": {
        "id": "other-user-guid",
        "name": "Jane Smith"
    },
    "checkedOutAt": "2025-12-17T14:30:00Z"
}
```

#### POST /api/documents/{id}/checkin

Releases the lock and creates a new version.

**Request:**
```http
POST /api/documents/{id}/checkin
Authorization: Bearer {token}
Content-Type: application/json

{
    "comment": "Updated section 3 with new requirements"
}
```

**Response (200 OK):**
```json
{
    "success": true,
    "versionNumber": 3,
    "versionComment": "Updated section 3 with new requirements",
    "previewUrl": "https://tenant.sharepoint.com/_layouts/15/embed.aspx?...",
    "aiAnalysisTriggered": true,
    "correlationId": "{uuid}"
}
```

#### POST /api/documents/{id}/discard

Cancels the checkout without saving changes.

**Request:**
```http
POST /api/documents/{id}/discard
Authorization: Bearer {token}
```

**Response (200 OK):**
```json
{
    "success": true,
    "message": "Checkout discarded. Document reverted to last committed version.",
    "previewUrl": "https://...",
    "correlationId": "{uuid}"
}
```

#### DELETE /api/documents/{id}

Deletes the Document record and associated SPE file.

**Request:**
```http
DELETE /api/documents/{id}
Authorization: Bearer {token}
X-Correlation-Id: {uuid}
```

**Response (200 OK):**
```json
{
    "success": true,
    "message": "Document and file deleted successfully",
    "correlationId": "{uuid}"
}
```

**Response (409 Conflict):**
```json
{
    "error": "document_locked",
    "detail": "Cannot delete document while it is checked out"
}
```

### Modified Endpoints

#### GET /api/documents/{id}/preview-url

Add checkout status to response:

```json
{
    "previewUrl": "https://...",
    "documentInfo": {
        "name": "Report.docx",
        "fileExtension": ".docx",
        "size": 45678
    },
    "checkoutStatus": {
        "isCheckedOut": true,
        "checkedOutBy": {
            "id": "user-guid",
            "name": "John Smith"
        },
        "checkedOutAt": "2025-12-17T14:30:00Z",
        "isCurrentUser": true
    },
    "versionInfo": {
        "versionNumber": 2,
        "lastModified": "2025-12-17T12:00:00Z",
        "lastModifiedBy": "Jane Smith"
    },
    "correlationId": "{uuid}"
}
```

---

## PCF Control Specification

### Component: SpeDocumentViewer

Replaces both `SpeFileViewer` and `SourceDocumentViewer` as a unified component.

#### Properties (ControlManifest.Input.xml)

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `value` | SingleLine.Text | Yes | Bound property for field control discoverability |
| `documentId` | SingleLine.Text | No | Document GUID (falls back to form record ID) |
| `bffApiUrl` | SingleLine.Text | No | BFF API base URL |
| `clientAppId` | SingleLine.Text | Yes | MSAL client app ID |
| `bffAppId` | SingleLine.Text | Yes | BFF app ID for scope |
| `tenantId` | SingleLine.Text | Yes | Azure AD tenant ID |
| `controlHeight` | Whole.None | No | Minimum height (default: 600) |
| `showToolbar` | TwoOptions | No | Show/hide toolbar (default: true) |
| `enableEdit` | TwoOptions | No | Enable edit/checkout (default: true) |
| `enableDelete` | TwoOptions | No | Enable delete button (default: true) |
| `enableDownload` | TwoOptions | No | Enable download button (default: true) |

#### Props Interface

```typescript
export interface ISpeDocumentViewerProps {
    documentId: string;
    bffApiUrl: string;
    accessToken: string;
    correlationId: string;
    isDarkTheme: boolean;

    // Feature flags
    showToolbar?: boolean;      // default: true
    enableEdit?: boolean;       // default: true
    enableDelete?: boolean;     // default: true
    enableDownload?: boolean;   // default: true

    // Callbacks
    onFullscreen?: (previewUrl: string) => void;
    onDocumentDeleted?: () => void;
    onCheckoutStatusChange?: (status: CheckoutStatus) => void;
}

export interface CheckoutStatus {
    isCheckedOut: boolean;
    checkedOutBy?: {
        id: string;
        name: string;
    };
    checkedOutAt?: Date;
    isCurrentUser: boolean;
}
```

#### State Interface

```typescript
export interface ISpeDocumentViewerState {
    // View state
    viewMode: "preview" | "edit" | "loading" | "processing" | "error";

    // Document info
    previewUrl: string | null;
    editUrl: string | null;
    documentInfo: IDocumentInfo | null;

    // Checkout state
    checkoutStatus: CheckoutStatus | null;

    // Loading states
    isLoading: boolean;
    isIframeLoading: boolean;
    isCheckingOut: boolean;
    isCheckingIn: boolean;
    isDeleting: boolean;
    isDownloading: boolean;

    // Error
    error: string | null;

    // UI
    showDeleteConfirm: boolean;
    showDiscardConfirm: boolean;
    checkInComment: string;
}
```

---

## Integration with Analysis Workspace

### Current SourceDocumentViewer Usage

```typescript
// AnalysisWorkspaceApp.tsx
<SourceDocumentViewer
    documentId={resolvedDocumentId}
    containerId={resolvedContainerId}
    fileId={resolvedFileId}
    apiBaseUrl={apiBaseUrl}
    onFullscreen={handleFullscreen}
/>
```

### Migration to SpeDocumentViewer

```typescript
// AnalysisWorkspaceApp.tsx
<SpeDocumentViewer
    documentId={resolvedDocumentId}
    bffApiUrl={apiBaseUrl}
    accessToken={accessToken}
    correlationId={correlationId}
    isDarkTheme={isDarkTheme}

    // Analysis Workspace specific settings
    enableDelete={false}           // Don't allow delete from Analysis view
    enableEdit={false}             // Read-only in Analysis context
    showToolbar={true}

    onFullscreen={handleFullscreen}
/>
```

### Shared Component Library

Per ADR-012, extract to `@spaarke/ui-components`:

```
src/client/shared/
├── components/
│   └── SpeDocumentViewer/
│       ├── SpeDocumentViewer.tsx
│       ├── SpeDocumentViewer.types.ts
│       ├── SpeDocumentViewer.styles.ts
│       ├── hooks/
│       │   ├── useDocumentPreview.ts
│       │   └── useCheckoutStatus.ts
│       └── index.ts
└── index.ts
```

---

## Delete Document Flow

### Design Decision: Ribbon-Triggered Delete

Delete is triggered from the **Document entity ribbon** (command bar), not from the PCF control toolbar. This approach:

1. Uses native Dataverse delete flow with proper cascading
2. Intercepts the delete to also remove the SPE file
3. Works whether or not the PCF viewer is loaded
4. Follows Power Platform conventions for record deletion

### Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    Document Form Command Bar                                 │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │  [Save] [Save & Close] [New] [Delete Document 🗑️]                       ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│                                    │                                         │
│                                    │ Click Delete                            │
│                                    ▼                                         │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │              Custom Ribbon JavaScript / PCF Button                       ││
│  │  sprk_DocumentDelete.js or DeleteDocumentPcf control                    ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│                                    │                                         │
│                                    │ 1. Show confirmation dialog             │
│                                    │ 2. Call BFF DELETE endpoint             │
│                                    │ 3. Navigate away on success             │
│                                    ▼                                         │
└─────────────────────────────────────────────────────────────────────────────┘
                                     │
                                     │ DELETE /api/documents/{id}
                                     ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                              BFF API                                         │
│  1. Validate user has Delete permission                                      │
│  2. Check document not checked out                                          │
│  3. Delete file from SPE container                                          │
│  4. Delete File Version records (cascade)                                   │
│  5. Delete Document record from Dataverse                                   │
│  6. Return success                                                          │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Implementation Options

#### Option A: Custom Ribbon Button + JavaScript (Simple)

Add a custom "Delete Document" button to the Document form ribbon that calls a JavaScript webresource.

**Ribbon XML:**
```xml
<CommandDefinition Id="sprk.document.DeleteCommand">
  <EnableRules>
    <EnableRule Id="sprk.document.DeleteCommand.EnableRule" />
  </EnableRules>
  <DisplayRules>
    <DisplayRule Id="sprk.document.DeleteCommand.DisplayRule" />
  </DisplayRules>
  <Actions>
    <JavaScriptFunction Library="$webresource:sprk_DocumentDelete.js"
                        FunctionName="Spaarke.Document.deleteDocument">
      <CrmParameter Value="PrimaryControl" />
    </JavaScriptFunction>
  </Actions>
</CommandDefinition>
```

**JavaScript (sprk_DocumentDelete.js):**
```javascript
var Spaarke = Spaarke || {};
Spaarke.Document = {
    deleteDocument: async function(primaryControl) {
        const formContext = primaryControl;
        const documentId = formContext.data.entity.getId().replace(/[{}]/g, "");
        const documentName = formContext.getAttribute("sprk_name").getValue();

        // 1. Confirmation dialog
        const confirmResult = await Xrm.Navigation.openConfirmDialog({
            title: "Delete Document?",
            text: `This will permanently delete "${documentName}" and its file from storage.\n\nThis action cannot be undone.`,
            confirmButtonLabel: "Delete",
            cancelButtonLabel: "Cancel"
        });

        if (!confirmResult.confirmed) return;

        // 2. Show progress
        Xrm.Utility.showProgressIndicator("Deleting document...");

        try {
            // 3. Get access token (via PCF bridge or stored token)
            const token = await Spaarke.Auth.getAccessToken();

            // 4. Call BFF to delete SPE file + Dataverse record
            const response = await fetch(
                `${Spaarke.Config.bffApiUrl}/api/documents/${documentId}`,
                {
                    method: "DELETE",
                    headers: {
                        "Authorization": `Bearer ${token}`,
                        "X-Correlation-Id": Spaarke.Utils.newGuid()
                    }
                }
            );

            if (!response.ok) {
                const error = await response.json();
                throw new Error(error.detail || "Delete failed");
            }

            // 5. Navigate back to grid
            Xrm.Utility.closeProgressIndicator();
            Xrm.Navigation.navigateTo({
                pageType: "entitylist",
                entityName: "sprk_document"
            });

        } catch (error) {
            Xrm.Utility.closeProgressIndicator();
            Xrm.Navigation.openErrorDialog({
                message: `Failed to delete document: ${error.message}`
            });
        }
    }
};
```

#### Option B: Delete Button PCF Control (ADR-006 Compliant)

Create a small PCF control that renders a single Delete button, placed in the form header or a dedicated section.

**Pros:**
- TypeScript, testable, ADR-006 compliant
- Can share auth service with SpeDocumentViewer
- Better error handling

**Cons:**
- More complex for a single button
- Requires form customization

#### Option C: Integrate into SpeDocumentViewer PCF (Hybrid)

Keep Delete button in the PCF viewer toolbar, but have it:
1. Show confirmation dialog
2. Call BFF DELETE endpoint
3. Use `Xrm.Navigation.navigateTo` to leave the form

This is simpler but couples delete to the viewer being loaded.

### Recommended Approach: Option A (JavaScript)

For delete specifically, a simple JavaScript webresource is pragmatic:
- Single-purpose function
- Uses native Xrm APIs for confirmation and navigation
- Works even if PCF viewer fails to load
- Can be enhanced later if needed

**Note:** This is a targeted exception to ADR-006 for a single ribbon action. The main document viewing functionality remains in the PCF control.

### User Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  1. User on Document form, clicks "Delete Document" in command bar          │
│                                                                              │
│  2. Confirmation dialog:                                                     │
│     ┌─────────────────────────────────────────────────────────────────────┐ │
│     │  Delete Document?                                                    │ │
│     │                                                                      │ │
│     │  This will permanently delete "Report.docx" and its file            │ │
│     │  from storage.                                                       │ │
│     │                                                                      │ │
│     │  This action cannot be undone.                                       │ │
│     │                                                                      │ │
│     │                              [Cancel]  [Delete]                      │ │
│     └─────────────────────────────────────────────────────────────────────┘ │
│                                                                              │
│  3. Progress indicator: "Deleting document..."                               │
│                                                                              │
│  4. BFF deletes:                                                             │
│     - File from SPE container                                               │
│     - File Version records (cascade)                                        │
│     - Document record                                                       │
│                                                                              │
│  5. Navigate to Document grid view                                          │
└─────────────────────────────────────────────────────────────────────────────┘
```

### BFF Implementation

```csharp
// DocumentEndpoints.cs
app.MapDelete("/api/documents/{id}", async (
    Guid id,
    IDocumentService documentService,
    ISpeFileStore speFileStore,
    IDataverseService dataverseService,
    CancellationToken ct) =>
{
    // 1. Verify document exists and user has delete permission
    var document = await documentService.GetDocumentAsync(id, ct);
    if (document == null)
        return Results.NotFound(new { error = "document_not_found" });

    // 2. Check not checked out
    if (document.IsCheckedOut)
        return Results.Conflict(new {
            error = "document_locked",
            detail = $"Document is checked out by {document.CheckedOutBy?.Name}"
        });

    // 3. Delete file from SPE container
    try
    {
        await speFileStore.DeleteFileAsync(document.DriveId, document.ItemId, ct);
    }
    catch (Exception ex)
    {
        // Log but continue - file may already be deleted
        logger.LogWarning(ex, "Failed to delete SPE file for document {Id}", id);
    }

    // 4. Delete File Version records (Dataverse cascade handles this if configured)
    // If not using cascade, delete explicitly:
    // await dataverseService.DeleteRelatedAsync("sprk_fileversions", "sprk_documentid", id, ct);

    // 5. Delete Document record from Dataverse
    await dataverseService.DeleteAsync("sprk_documents", id, ct);

    return Results.Ok(new { success = true });
});
```

---

## AI Integration

### Check-In Triggers AI Analysis

When a document is checked in:

1. BFF releases the checkout lock
2. BFF increments version number
3. BFF triggers AI analysis pipeline:
   - File summary extraction
   - Metadata extraction (if configured)
   - Custom AI tool execution (if Analysis exists)

```csharp
// CheckInAsync in DocumentService.cs
public async Task<CheckInResult> CheckInAsync(Guid documentId, string? comment, CancellationToken ct)
{
    // 1. Release lock
    await dataverseService.UpdateAsync("sprk_documents", documentId, new {
        sprk_checkedoutby = null,
        sprk_checkedoutat = null,
        sprk_versionnumber = currentVersion + 1,
        sprk_versioncomment = comment
    }, ct);

    // 2. Trigger AI analysis (fire-and-forget)
    _ = aiAnalysisService.EnqueueDocumentAnalysisAsync(documentId, ct);

    // 3. Return success with preview URL
    return new CheckInResult {
        Success = true,
        VersionNumber = currentVersion + 1,
        AiAnalysisTriggered = true
    };
}
```

---

## Ribbon Integration

### Question: Can we use the Document entity ribbon?

**Answer: Yes, but with limitations.**

The Document form command bar (marked as (1) in the screenshot) can have custom buttons added via ribbon customization. However:

**Pros:**
- Native Dynamics 365 look and feel
- Respects form security automatically
- Works without PCF control loaded

**Cons:**
- Ribbon XML is complex to maintain
- Difficult to show/hide buttons based on checkout state
- Requires webresource JavaScript (ADR-006 violation)
- Can't easily update button labels/icons dynamically

**Recommendation:**

Use the **PCF control toolbar** for file operations:
- More control over UX
- Real-time state updates
- TypeScript implementation
- Consistent with ADR-006

Use the **entity ribbon** only for:
- Save / Save & Close (already native)
- Delete Record (use native, or custom if need SPE delete)

### Hybrid Approach

If ribbon is desired:

1. Add "Edit Document" ribbon button
2. JavaScript calls: `Spaarke.Document.checkout(primaryControl)`
3. PCF control listens for checkout event and switches to edit mode

```javascript
// Ribbon JavaScript (sprk_DocumentRibbon.js)
var Spaarke = Spaarke || {};
Spaarke.Document = {
    checkout: function(primaryControl) {
        // Dispatch custom event that PCF control listens for
        var event = new CustomEvent('spaarke:document:checkout', {
            detail: { formContext: primaryControl }
        });
        window.dispatchEvent(event);
    }
};
```

---

## Implementation Plan

### Phase 1: Core PCF Control (Week 1-2)

1. Create `SpeDocumentViewer` component in shared library
2. Implement preview mode (embed.aspx)
3. Implement toolbar with Fluent v9
4. Add download functionality
5. Unit tests

### Phase 2: Check-Out/Check-In (Week 2-3)

1. Add Dataverse fields (sprk_checkedoutby, etc.)
2. Implement BFF checkout/checkin endpoints
3. Add edit mode (embedview)
4. Implement state management
5. Add confirmation dialogs
6. Integration tests

### Phase 3: Delete & Integration (Week 3-4)

1. Implement delete functionality (BFF + PCF)
2. Migrate SpeFileViewer to use SpeDocumentViewer
3. Migrate SourceDocumentViewer in Analysis Workspace
4. Deprecate old components
5. Update documentation

### Phase 4: AI Integration (Week 4)

1. Connect check-in to AI analysis pipeline
2. Add processing state UI
3. Test version history + AI analysis
4. End-to-end testing

---

## Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|------------|
| Office Online embed parameters change | Medium | Abstract URL building, monitor MS docs |
| SharePoint preview cache longer than expected | Low | Acceptable - shows committed version |
| Concurrent checkout conflicts | Low | Proper locking with clear error messages |
| User forgets to check in | Medium | Auto-reminder after 24 hours (future) |

---

## Success Metrics

| Metric | Target |
|--------|--------|
| Preview load time | < 3 seconds |
| Check-out operation | < 1 second |
| Check-in operation | < 2 seconds |
| Delete operation | < 2 seconds |
| User satisfaction (internal testing) | > 4/5 |

---

## Related Documents

- [SPE-FILE-VIEWER-ARCHITECTURE.md](../../docs/ai-knowledge/architecture/SPE-FILE-VIEWER-ARCHITECTURE.md)
- [ADR-006: PCF over Webresources](../../docs/reference/adr/ADR-006-pcf-over-webresources.md)
- [ADR-012: Shared Component Library](../../docs/reference/adr/ADR-012-shared-component-library.md)
- [SDAP Architecture](../../docs/ai-knowledge/architecture/sdap-architecture.md)

---

## Appendix A: Fluent v9 Button Patterns

### Standard Toolbar Button

```tsx
<Tooltip content="Refresh preview" relationship="label">
    <Button
        appearance="subtle"
        size="small"
        icon={<ArrowClockwiseRegular />}
        onClick={handleRefresh}
        disabled={isLoading}
        aria-label="Refresh"
    />
</Tooltip>
```

### Primary Action Button (Check In)

```tsx
<Tooltip content="Save and release lock" relationship="label">
    <Button
        appearance="primary"
        size="small"
        icon={<CheckmarkCircleRegular />}
        onClick={handleCheckIn}
        disabled={isCheckingIn}
    >
        Check In
    </Button>
</Tooltip>
```

### Destructive Action Button (Delete)

```tsx
<Dialog>
    <DialogTrigger disableButtonEnhancement>
        <Tooltip content="Delete document and file" relationship="label">
            <Button
                appearance="subtle"
                size="small"
                icon={<DeleteRegular />}
                aria-label="Delete"
            />
        </Tooltip>
    </DialogTrigger>
    <DialogSurface>
        <DialogTitle>Delete Document?</DialogTitle>
        <DialogContent>
            This will permanently delete the document and file.
        </DialogContent>
        <DialogActions>
            <DialogTrigger disableButtonEnhancement>
                <Button appearance="secondary">Cancel</Button>
            </DialogTrigger>
            <Button appearance="primary" onClick={handleDelete}>
                Delete
            </Button>
        </DialogActions>
    </DialogSurface>
</Dialog>
```

---

## Appendix B: Status Badge Component

```tsx
const CheckoutStatusBadge: React.FC<{ status: CheckoutStatus }> = ({ status }) => {
    const styles = useStyles();

    if (!status.isCheckedOut) return null;

    const timeAgo = formatDistanceToNow(status.checkedOutAt);

    return (
        <div className={styles.statusBadge}>
            <LockClosedRegular />
            <Text size={200}>
                Checked out by {status.isCurrentUser ? "you" : status.checkedOutBy?.name}
                {" "}({timeAgo} ago)
            </Text>
        </div>
    );
};
```
