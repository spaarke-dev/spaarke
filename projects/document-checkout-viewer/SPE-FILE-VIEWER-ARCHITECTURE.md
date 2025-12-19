# SpeDocumentViewer PCF Control Architecture

> **Version**: 2.0.0 (Design)
> **Last Updated**: December 17, 2025
> **Purpose**: Document preview and editing control with version control for SharePoint Embedded files

---

## Overview

SpeDocumentViewer is a unified PowerApps Component Framework (PCF) control for viewing and editing SharePoint Embedded files in Dynamics 365 model-driven apps. It replaces both `SpeFileViewer` and `SourceDocumentViewer` components.

### Key Features

- **Preview Mode**: Read-only document preview using SharePoint embed.aspx
- **Edit Mode**: Full Office Online editing with check-out/check-in version control
- **Version History**: Track all document versions with check-out/check-in metadata
- **Open in Desktop**: Launch files in Word, Excel, or PowerPoint desktop apps
- **Dark Mode**: Automatic theme detection from Power Platform context
- **Reusable**: Single component for Document forms and Analysis Workspace

### Related Documents

- **Full Design Spec**: [spec.md](./spec.md) - Complete implementation specification
- **ADR-006**: PCF over webresources decision
- **ADR-012**: Shared component library

---

## Architecture: Check-Out / Check-In Model

### State Flow

```
┌────────────────────────────────────────────────────────────────────────────┐
│                            PREVIEW MODE (Default)                           │
│                                                                             │
│  • embed.aspx (read-only, no Share button)                                 │
│  • Shows last committed version                                            │
│  • Toolbar: [Refresh] [Edit] [Download]                                    │
│                                                                             │
└──────────────────────────────────┬─────────────────────────────────────────┘
                                   │ Click "Edit" (creates File Version record)
                                   ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                              EDIT MODE                                      │
│                                                                             │
│  • Office Online embedview (full editing)                                  │
│  • Share button visible (acceptable during editing)                        │
│  • Auto-saves to SPE                                                       │
│  • Toolbar: [Open Desktop] [Check In] [Discard]                           │
│                                                                             │
└──────────────────────────────────┬─────────────────────────────────────────┘
                                   │ Click "Check In" (updates File Version)
                                   ▼
┌────────────────────────────────────────────────────────────────────────────┐
│                          PROCESSING                                         │
│                                                                             │
│  • Release lock                                                            │
│  • Update version record (checked-in by, comment)                          │
│  • Trigger AI analysis                                                     │
│  • Refresh preview                                                         │
│                                                                             │
└──────────────────────────────────┬─────────────────────────────────────────┘
                                   │
                                   ▼
                            PREVIEW MODE (updated)
```

### Security Model

| State | Preview URL | Share Button | Rationale |
|-------|-------------|--------------|-----------|
| Preview Mode | embed.aspx | Hidden | Read-only, secure |
| Edit Mode | embedview | Visible | User is intentionally editing |
| Checked Out by Other | embed.aspx | Hidden | Read-only for other users |

---

## Data Model

### New Entity: File Version (`sprk_fileversion`)

```
sprk_document (1) ──────── (N) sprk_fileversion
```

| Field | Type | Description |
|-------|------|-------------|
| `sprk_documentid` | Lookup | Parent document |
| `sprk_versionnumber` | Whole Number | Version 1, 2, 3... |
| `sprk_checkedoutby` | Lookup (User) | Who checked out |
| `sprk_checkedoutdate` | DateTime | When checked out |
| `sprk_checkedinby` | Lookup (User) | Who checked in (may differ) |
| `sprk_checkedindate` | DateTime | When checked in |
| `sprk_comment` | Text | Check-in comment |
| `sprk_status` | Choice | CheckedOut, CheckedIn, Discarded |

### Document Entity Fields (denormalized)

| Field | Type | Description |
|-------|------|-------------|
| `sprk_currentversionid` | Lookup | Latest checked-in version |
| `sprk_ischeckedout` | Yes/No | Quick lock status |
| `sprk_checkedoutby` | Lookup (User) | Current lock holder |

---

## BFF API Endpoints

### Existing Endpoints

| Endpoint | Purpose |
|----------|---------|
| `GET /api/documents/{id}/preview-url` | Get embed.aspx URL + checkout status |
| `GET /api/documents/{id}/open-links` | Get desktop + web URLs |
| `GET /api/documents/{id}/content` | Download file |

### New Endpoints

| Endpoint | Purpose |
|----------|---------|
| `POST /api/documents/{id}/checkout` | Lock document, create version record, return edit URL |
| `POST /api/documents/{id}/checkin` | Release lock, update version, trigger AI |
| `POST /api/documents/{id}/discard` | Cancel checkout, revert changes |
| `DELETE /api/documents/{id}` | Delete document + SPE file |

---

## Delete Flow

Delete is triggered from the **Document entity ribbon** (not PCF toolbar):

1. Custom "Delete Document" ribbon button
2. JavaScript webresource shows confirmation
3. Calls `DELETE /api/documents/{id}`
4. BFF deletes: SPE file → File Versions → Document record
5. Navigate to Document grid

See [spec.md](./spec.md#delete-document-flow) for implementation details.

---

## Component Consolidation

### Before (Two Components)

```
SpeFileViewer (Document form)
├── FilePreview.tsx
├── BffClient.ts
└── AuthService.ts

SourceDocumentViewer (Analysis Workspace)
├── SourceDocumentViewer.tsx
├── MsalAuthProvider.ts
└── Direct fetch calls
```

### After (Unified Component)

```
@spaarke/ui-components
└── SpeDocumentViewer/
    ├── SpeDocumentViewer.tsx      (Main component)
    ├── hooks/
    │   ├── useDocumentPreview.ts
    │   └── useCheckoutStatus.ts
    ├── services/
    │   ├── DocumentApiClient.ts
    │   └── AuthService.ts
    └── types.ts
```

Usage in different contexts:

```typescript
// Document Form - Full functionality
<SpeDocumentViewer
    documentId={recordId}
    enableEdit={true}
    enableDelete={true}
/>

// Analysis Workspace - Read-only
<SpeDocumentViewer
    documentId={documentId}
    enableEdit={false}
    enableDelete={false}
/>
```

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     Dynamics 365 Model-Driven App                        │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │                    Document Form (sprk_document)                    │ │
│  │  ┌──────────────────────────────────────────────────────────────┐  │ │
│  │  │                   SpeFileViewer PCF Control                   │  │ │
│  │  │  ┌─────────────┐   ┌────────────────┐   ┌─────────────────┐  │  │ │
│  │  │  │  index.ts   │──▶│ FilePreview.tsx│──▶│    BffClient    │  │  │ │
│  │  │  │ (PCF Entry) │   │  (React UI)    │   │   (API Client)  │  │  │ │
│  │  │  └─────────────┘   └────────────────┘   └────────┬────────┘  │  │ │
│  │  │         │                                         │           │  │ │
│  │  │         ▼                                         │           │  │ │
│  │  │  ┌─────────────┐                                 │           │  │ │
│  │  │  │ AuthService │ ◀── MSAL (ssoSilent/popup)     │           │  │ │
│  │  │  └─────────────┘                                 │           │  │ │
│  │  └──────────────────────────────────────────────────┼───────────┘  │ │
│  └─────────────────────────────────────────────────────┼──────────────┘ │
└────────────────────────────────────────────────────────┼────────────────┘
                                                         │ HTTPS + Bearer Token
                                                         ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         BFF API (Sprk.Bff.Api)                           │
│  ┌────────────────────────────────────────────────────────────────────┐ │
│  │  /api/documents/{id}/preview-url   → Get SharePoint preview URL    │ │
│  │  /api/documents/{id}/open-links    → Get desktop + web URLs        │ │
│  │  /api/documents/{id}/content       → Download file content         │ │
│  │  /api/documents/{id}/office        → Get Office Online editor URL  │ │
│  └────────────────────────────────────────────────────────────────────┘ │
│                                    │                                     │
│                                    │ OBO (On-Behalf-Of)                  │
│                                    ▼                                     │
│                         ┌──────────────────┐                            │
│                         │  Microsoft Graph │                            │
│                         │  + SharePoint    │                            │
│                         │    Embedded      │                            │
│                         └──────────────────┘                            │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Core Component Files

### PCF Entry Point

| File | Purpose |
|------|---------|
| `control/index.ts` | PCF lifecycle (`init`, `updateView`, `destroy`), MSAL initialization, state machine |
| `control/ControlManifest.Input.xml` | PCF manifest defining properties, features, and version |

### React Components

| File | Purpose |
|------|---------|
| `control/FilePreview.tsx` | Main React component rendering preview iframe and toolbar |
| `control/css/SpeFileViewer.css` | Styling for loading states, preview container, dark mode |

### Services

| File | Purpose |
|------|---------|
| `control/AuthService.ts` | MSAL authentication, token acquisition (ssoSilent → popup fallback) |
| `control/BffClient.ts` | HTTP client for BFF API calls with error handling |

### Types

| File | Purpose |
|------|---------|
| `control/types.ts` | TypeScript interfaces for API responses, component state, props |
| `control/generated/ManifestTypes.ts` | Auto-generated types from ControlManifest.Input.xml |

---

## Control Properties (ControlManifest.Input.xml)

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `value` | SingleLine.Text | Yes | Bound property for field control discoverability |
| `documentId` | SingleLine.Text | No | Document GUID (falls back to form record ID if empty) |
| `bffApiUrl` | SingleLine.Text | No | BFF API base URL (default: production URL) |
| `clientAppId` | SingleLine.Text | Yes | PCF Client App Registration ID (for MSAL) |
| `bffAppId` | SingleLine.Text | Yes | BFF API App Registration ID (for scope) |
| `tenantId` | SingleLine.Text | Yes | Azure AD Tenant ID |
| `controlHeight` | Whole.None | No | Minimum height in pixels (default: 600) |

---

## Authentication Flow

### MSAL Configuration

```typescript
// Scope format (CRITICAL: Named scope, NOT .default)
const scope = `api://${bffAppId}/SDAP.Access`;

// Authentication priority:
// 1. ssoSilent() - Leverage Dataverse session (no popup)
// 2. acquireTokenSilent() - Use cached tokens
// 3. acquireTokenPopup() - User interaction (consent/MFA)
```

### Token Flow

```
User lands on Dataverse form
         │
         ▼
  ┌──────────────┐
  │ PCF init()   │
  └──────┬───────┘
         │
         ▼
  ┌──────────────┐     Success
  │ ssoSilent()  │─────────────▶ Token acquired
  └──────┬───────┘
         │ Failure
         ▼
  ┌──────────────┐     Success
  │ Silent cache │─────────────▶ Token acquired
  └──────┬───────┘
         │ Failure
         ▼
  ┌──────────────┐     Success
  │ Popup auth   │─────────────▶ Token acquired
  └──────────────┘
```

---

## BFF API Endpoints

### GET /api/documents/{id}/preview-url

Returns SharePoint preview URL for embedding in iframe.

**Request Headers:**
```
Authorization: Bearer {accessToken}
X-Correlation-Id: {uuid}
Accept: application/json
```

**Response (200 OK):**
```json
{
  "previewUrl": "https://{tenant}.sharepoint.com/_layouts/15/embed.aspx?...",
  "documentInfo": {
    "name": "Report.docx",
    "fileExtension": ".docx",
    "size": 45678
  },
  "correlationId": "{uuid}"
}
```

### GET /api/documents/{id}/open-links

Returns URLs for opening document in desktop apps or web browser.

**Response (200 OK):**
```json
{
  "desktopUrl": "ms-word:https://tenant.sharepoint.com/contentstorage/...",
  "webUrl": "https://tenant.sharepoint.com/contentstorage/.../Report.docx",
  "mimeType": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
  "fileName": "Report.docx",
  "downloadUrl": "https://tenant.sharepoint.com/_api/v2.0/..."
}
```

**Desktop URL Format:**
```
ms-word:{webUrl}     - Word documents
ms-excel:{webUrl}    - Excel spreadsheets
ms-powerpoint:{webUrl} - PowerPoint presentations
```

### GET /api/documents/{id}/content

Downloads file content as blob (for download feature).

**Response:** Binary file content with appropriate Content-Type header.

---

## Component State Machine

```
          init()
             │
             ▼
      ┌──────────┐
      │ Loading  │◀───────────────────────┐
      └────┬─────┘                        │
           │                              │
   ┌───────┴───────┐                      │
   │               │                      │
   ▼               ▼                      │
┌─────┐        ┌───────┐            (retry)
│Ready│        │ Error │──────────────────┘
└─────┘        └───────┘
```

| State | Description |
|-------|-------------|
| `Loading` | Initializing MSAL, acquiring token (shows spinner) |
| `Ready` | React component rendered, preview functional |
| `Error` | Initialization failed (shows error message with correlation ID) |

---

## FilePreview Component State

```typescript
interface FilePreviewState {
  previewUrl: string | null;      // SharePoint preview URL
  isLoading: boolean;             // API call in progress
  isIframeLoading: boolean;       // Iframe content loading
  isEditLoading: boolean;         // "Open in Desktop" button loading
  isWebLoading: boolean;          // "Open in Web" button loading
  error: string | null;           // Error message to display
  documentInfo: {
    name: string;
    fileExtension?: string;
    size?: number;
  } | null;
}
```

---

## Toolbar Actions

### Refresh Button

- **Icon**: `ArrowClockwiseRegular`
- **Behavior**: Cache-busts current preview URL (appends `_cb={timestamp}`)
- **No BFF call**: Reuses existing preview URL with cache-bust parameter

### Open in Desktop Button

- **Icon**: `EditRegular`
- **Visibility**: Only shown for Office files (.docx, .xlsx, .pptx, etc.)
- **Behavior**:
  1. Calls `/api/documents/{id}/open-links` to get `desktopUrl`
  2. Creates hidden anchor tag with protocol URL
  3. Triggers click to launch desktop app
- **Live Sync**: Yes - file opens from SharePoint, changes save back

### Open in Web Button

- **Icon**: `GlobeRegular`
- **Visibility**: Only shown for Office files
- **Behavior**:
  1. Calls `/api/documents/{id}/open-links` to get `webUrl`
  2. Opens new tab with `window.open(webUrl, '_blank')`
- **Live Sync**: Yes - Office Online saves directly to SharePoint

---

## Office File Detection

```typescript
private isOfficeFile(extension?: string): boolean {
    if (!extension) return false;

    // Normalize: ".docx" → "docx"
    const normalizedExt = extension.startsWith('.')
        ? extension.substring(1).toLowerCase()
        : extension.toLowerCase();

    const officeExtensions = [
        'docx', 'doc', 'docm', 'dot', 'dotx', 'dotm',  // Word
        'xlsx', 'xls', 'xlsm', 'xlsb', 'xlt', 'xltx', 'xltm',  // Excel
        'pptx', 'ppt', 'pptm', 'pot', 'potx', 'potm', 'pps', 'ppsx', 'ppsm'  // PowerPoint
    ];

    return officeExtensions.includes(normalizedExt);
}
```

---

## Theme Management

Theme is detected from multiple sources with priority:

1. **localStorage** (`spaarke-theme`): User's explicit choice from global theme menu
2. **Power Platform context**: `context.fluentDesignLanguage.isDarkTheme`
3. **Navbar detection**: Background color of `[data-id='navbar-container']`
4. **System preference**: `prefers-color-scheme: dark` media query

Theme changes are listened for via:
- `storage` event (cross-tab localStorage changes)
- Custom `spaarke-theme-change` event (same-tab changes)
- `matchMedia.change` event (system preference changes)

---

## Error Handling

### BFF Error Codes

| Code | User Message |
|------|--------------|
| `invalid_id` | Invalid document ID format. Please contact support. |
| `document_not_found` | Document not found. It may have been deleted. |
| `mapping_missing_drive` | This file is still initializing. Please try again in a moment. |
| `mapping_missing_item` | This file is still initializing. Please try again in a moment. |
| `storage_not_found` | File has been removed from storage. Contact your administrator. |
| `throttled_retry` | Service is temporarily busy. Please try again in a few seconds. |

### HTTP Status Fallbacks

| Status | User Message |
|--------|--------------|
| 401 | Authentication failed. Please refresh the page. |
| 403 | You do not have permission to access this file. |
| 404 | Document not found. It may have been deleted. |
| 409 | File is not ready for preview. Please try again shortly. |
| 5xx | Server error ({status}). Please try again later. |

---

## Document ID Validation

Document IDs must be valid GUIDs (Dataverse primary keys):

```typescript
// Valid: ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5
// Invalid: 01LBYCMX76QPLGITR47BB355T4G2CVDL2B (SharePoint Item ID)

private isValidGuid(value: string): boolean {
    const guidRegex = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;
    return guidRegex.test(value);
}
```

---

## Fluent UI v9 Components Used

| Component | Usage |
|-----------|-------|
| `FluentProvider` | Theme provider (webLightTheme / webDarkTheme) |
| `Button` | Toolbar buttons (`appearance="subtle"`, `size="small"`) |
| `Spinner` | Loading indicators |
| `MessageBar` | Error and info messages |
| `Tooltip` | Button hover tooltips |
| `Text` | Document name display |
| `tokens` | Spacing and color values |

### Icon Components

| Icon | Usage |
|------|-------|
| `DocumentRegular` | Document icon in toolbar |
| `ArrowClockwiseRegular` | Refresh button |
| `EditRegular` | Open in Desktop button |
| `GlobeRegular` | Open in Web button |

---

## Key Implementation Details

### Protocol URL Triggering

Office protocol URLs (`ms-word:`, etc.) don't work well with `window.location.href` in Dynamics 365 iframe context. The solution uses anchor tag click:

```typescript
const link = document.createElement('a');
link.href = response.desktopUrl;  // "ms-word:https://..."
link.style.display = 'none';
document.body.appendChild(link);
link.click();
document.body.removeChild(link);
```

### Cache-Busting for Refresh

Preview URLs are refreshed by appending a timestamp parameter:

```typescript
const separator = currentUrl.includes('?') ? '&' : '?';
const refreshedUrl = `${currentUrl.split('&_cb=')[0].split('?_cb=')[0]}${separator}_cb=${Date.now()}`;
```

### Iframe Sandbox

```html
<iframe
  sandbox="allow-same-origin allow-scripts allow-forms allow-popups allow-popups-to-escape-sandbox"
  allow="autoplay"
/>
```

---

## Related Components

| Component | Location | Relationship |
|-----------|----------|--------------|
| SourceDocumentViewer | `AnalysisWorkspace/control/components/` | Similar functionality, uses download instead of protocol URLs |
| DesktopUrlBuilder | `Spaarke.Core/Utilities/` | Server-side protocol URL generation |

---

## Deployment

Deployed via `pac pcf push` or solution import:

```bash
cd src/client/pcf/SpeFileViewer
npm run build
pac pcf push --publisher-prefix sprk
```

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.10.3 | Dec 17, 2025 | Office Online embed URL for live preview of Office files; raises security discussion |
| 1.10.2 | Dec 17, 2025 | Fluent v9 subtle/small buttons, document name in toolbar |
| 1.10.1 | Dec 17, 2025 | Fix isOfficeFile extension normalization, anchor click for protocol URLs |
| 1.10.0 | Dec 17, 2025 | Initial protocol URL approach for Open in Desktop |
| 1.9.x | Earlier | Preview-only mode |

---

## Preview URL Strategy (v1.10.3)

### loadPreview() Flow

```
loadPreview()
     │
     ▼
Get preview-url from BFF
     │
     ▼
Is Office file? ──No──▶ Use embed.aspx URL
     │                     │
    Yes                    │
     │                     │
     ▼                     │
Get open-links from BFF   │
     │                     │
     ▼                     │
Build Office Online URL   │
webUrl?action=embedview   │
     │                     │
     └─────────────────────┴──▶ Set previewUrl state
```

### Code Path

```typescript
// For Office files: webUrl?action=embedview (live)
if (this.isOfficeFile(documentInfo.fileExtension)) {
    const openLinksResponse = await this.bffClient.getOpenLinks(...);
    if (openLinksResponse.webUrl) {
        finalPreviewUrl = `${openLinksResponse.webUrl}?action=embedview`;
    }
}
// For other files: embed.aspx (cached)
// Uses previewResponse.previewUrl as-is
```

### Trade-offs

| Approach | Pros | Cons |
|----------|------|------|
| embed.aspx | Read-only, no Share button | 30-60s cache delay |
| embedview | Real-time, comments, rich editing | Exposes Share button, security concerns |
| Desktop app | Full functionality, no iframe limits | Requires desktop Office installed |

---

## See Also

- [IT-DEPLOYMENT-GUIDE.md](../../product-documentation/IT-DEPLOYMENT-GUIDE.md) - Deployment configuration
- [ADR-006](../../reference/adr/ADR-006-pcf-over-webresources.md) - PCF over webresources decision
- [ADR-012](../../reference/adr/ADR-012-shared-component-library.md) - Shared component library

