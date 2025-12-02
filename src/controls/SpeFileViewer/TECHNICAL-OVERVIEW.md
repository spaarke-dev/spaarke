# SPE File Viewer PCF Control - Technical Overview

## Executive Summary

**Purpose:** Display SharePoint Embedded file previews on Dataverse Document forms

**Current Status:** ⚠️ Deployment issues with v1.0.4 (control not appearing in UI)

**Architecture:** PCF Field Control → MSAL Authentication → BFF API → Dataverse → Graph API → SharePoint Preview

---

## Problem Being Solved

### Original Issue
- Users need to preview SharePoint Embedded files from Dataverse Document records
- Direct Graph API calls from PCF not feasible (requires app-only permissions)
- Need secure, user-delegated access with proper authentication

### Solution Approach
**Three-Layer Architecture:**
1. **PCF Control** (Browser) - Handles UI, MSAL authentication, renders preview
2. **BFF API** (.NET) - Mediates between PCF and Dataverse/Graph, handles server-side logic
3. **Dataverse + Graph** - Data storage and file access

---

## Complete Architecture Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│ 1. Document Form Loads (Dataverse UI)                                    │
│    - User opens Document record (GUID: ad1b0c34-52a5-f011-bbd3...)      │
└─────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────┐
│ 2. PCF Control Initializes                                               │
│    - Reads config (clientAppId, bffAppId, tenantId, bffApiUrl)          │
│    - Creates MSAL PublicClientApplication                                │
│    - Generates correlation ID for distributed tracing                    │
└─────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────┐
│ 3. MSAL Authentication (AuthService.ts)                                  │
│    - Tries ssoSilent() first (leverage Dataverse session)               │
│    - Falls back to acquireTokenSilent() (cached tokens)                 │
│    - Falls back to acquireTokenPopup() (user consent if needed)         │
│    - Scope: api://[BFF_APP_ID]/SDAP.Access (NAMED scope, NOT .default) │
│    - Returns: JWT Bearer token                                           │
└─────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────┐
│ 4. Extract Document ID (index.ts)                                        │
│    - Option A: Use documentId property (if user configured)             │
│    - Option B: Use form record ID (context.mode.contextInfo.entityId)   │
│    - Must be Dataverse GUID format (NOT SharePoint Item ID)             │
└─────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────┐
│ 5. Call BFF API (BffClient.ts)                                          │
│    - GET /api/documents/{documentId}/preview-url                         │
│    - Headers:                                                            │
│      * Authorization: Bearer [MSAL_TOKEN]                                │
│      * X-Correlation-Id: [UUID]                                          │
│      * Accept: application/json                                          │
│    - CORS: enabled (cross-origin from Dataverse → BFF)                  │
└─────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────┐
│ 6. BFF API Processing (Spe.Bff.Api)                                     │
│    - Validates JWT token (Azure AD issuer, audience, scope)             │
│    - Validates correlation ID                                            │
│    - Queries Dataverse for Document record by GUID                       │
│    - Retrieves: sprk_graphitemid, sprk_graphdriveid                     │
│    - Calls Graph API: GET /drives/{driveId}/items/{itemId}/preview      │
│    - Returns: { previewUrl, documentInfo, correlationId }               │
└─────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────┐
│ 7. Render Preview (FilePreview.tsx)                                     │
│    - Receives preview URL from BFF                                       │
│    - Renders Office 365 preview iframe                                   │
│    - Displays document name, file type, size                             │
│    - Handles loading states, errors, retry logic                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Key Components

### 1. index.ts (Main Entry Point)
**Responsibilities:**
- PCF lifecycle management (init, updateView, destroy)
- Configuration extraction from manifest properties
- MSAL authentication orchestration
- Document ID extraction (from input or form context)
- React component rendering

**Critical Logic:**
```typescript
// Extract Document ID - v1.0.3+ allows blank input
private extractDocumentId(context: ComponentFramework.Context<IInputs>): string {
    const rawValue = context.parameters.documentId.raw;

    // If configured, use that
    if (rawValue && typeof rawValue === 'string' && rawValue.trim() !== '') {
        return rawValue.trim();
    }

    // Otherwise use form record ID (default behavior)
    const recordId = (context.mode as any).contextInfo?.entityId;
    if (recordId) {
        return recordId;
    }

    return '';
}
```

### 2. AuthService.ts (MSAL Authentication)
**Responsibilities:**
- MSAL PublicClientApplication initialization
- Three-tier token acquisition strategy (SSO → Cached → Popup)
- Token caching (sessionStorage)
- Scope management (CRITICAL: uses named scope, NOT .default)

**Critical Configuration:**
```typescript
// CORRECT: Named scope for user-delegated access
this.namedScope = `api://${bffAppId}/SDAP.Access`;

// WRONG: .default scope (daemon/confidential client pattern)
// this.namedScope = `${bffAppId}/.default`;
```

**Why Named Scope Matters:**
- `.default` scope = "give me all permissions the app has" (app-only/daemon pattern)
- Named scope = "give me this specific permission on behalf of the user" (delegated pattern)
- BFF API expects user context, not app-only context

### 3. BffClient.ts (HTTP Communication)
**Responsibilities:**
- HTTP calls to BFF API
- Bearer token authentication
- Correlation ID tracking (X-Correlation-Id header)
- Error handling (4xx, 5xx responses)
- CORS configuration

**API Contract:**
```typescript
// Request
GET /api/documents/{documentId}/preview-url
Headers:
  Authorization: Bearer [JWT]
  X-Correlation-Id: [UUID]
  Accept: application/json

// Response 200 OK
{
  "previewUrl": "https://...",
  "documentInfo": {
    "id": "ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5",
    "name": "Contract.docx",
    "fileExtension": "docx",
    "size": 45678,
    "graphItemId": "01LBYCMX...",
    "graphDriveId": "b!..."
  },
  "correlationId": "..."
}

// Response 404 Not Found
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
  "title": "Document not found",
  "status": 404,
  "detail": "Document with ID 'ad1b0c34-...' does not exist",
  "correlationId": "..."
}
```

### 4. FilePreview.tsx (React UI Component)
**Responsibilities:**
- Call BFF API via BffClient
- Render loading/error/preview states
- Display document metadata
- Office 365 preview iframe rendering
- Retry logic for error recovery

**States:**
- `isLoading: true` → Spinner with "Loading preview..."
- `error: string` → Error message with Retry button
- `previewUrl: string` → Iframe with SharePoint preview
- Empty → "No document selected"

---

## Configuration Requirements

### Manifest Properties (ControlManifest.Input.xml)
```xml
<control namespace="Spaarke"
         constructor="SpeFileViewer"
         version="1.0.4"
         control-type="virtual">  <!-- ⚠️ ISSUE: Should be "standard" -->

  <!-- Document ID - Optional input (uses form record ID if blank) -->
  <property name="documentId"
            of-type="SingleLine.Text"
            usage="input"      <!-- v1.0.3+ changed from "bound" -->
            required="false" />

  <!-- BFF API URL -->
  <property name="bffApiUrl"
            of-type="SingleLine.Text"
            usage="input"
            required="false"
            default-value="https://spe-api-dev-67e2xz.azurewebsites.net" />

  <!-- PCF Client App ID (for MSAL authentication) -->
  <property name="clientAppId"
            of-type="SingleLine.Text"
            usage="input"
            required="true" />

  <!-- BFF Application ID (for scope construction) -->
  <property name="bffAppId"
            of-type="SingleLine.Text"
            usage="input"
            required="true" />

  <!-- Azure AD Tenant ID -->
  <property name="tenantId"
            of-type="SingleLine.Text"
            usage="input"
            required="true" />
</control>
```

### Azure AD Application Configuration

**PCF Client App (b36e9b91-ee7d-46e6-9f6a-376871cc9d54):**
- Type: Public client / SPA
- Redirect URIs: Dataverse environment URLs
- API Permissions:
  - `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/SDAP.Access` (delegated)
  - Admin consent: Required
- Authentication: SPA (implicit flow + PKCE)

**BFF API App (1e40baad-e065-4aea-a8d4-4b7ab273458c):**
- Type: Web API
- Expose API: `api://1e40baad-e065-4aea-a8d4-4b7ab273458c`
- Scopes: `SDAP.Access` (delegated, user-impersonation)
- API Permissions:
  - Microsoft Graph (delegated): Files.Read.All, Sites.Read.All
  - Dynamics CRM (delegated): user_impersonation
  - Admin consent: Required

---

## Dependencies

### NPM Packages (package.json)
```json
{
  "dependencies": {
    "@azure/msal-browser": "^4.26.2",    // MSAL authentication
    "@fluentui/react": "^8.125.1",       // Microsoft Fluent UI components
    "react": "^19.2.0",                  // React 19
    "react-dom": "^19.2.0",              // React DOM
    "uuid": "^13.0.0"                    // Correlation ID generation
  },
  "devDependencies": {
    "pcf-scripts": "^1",                 // PCF build tooling
    "typescript": "^5.8.3",              // TypeScript compiler
    "typescript-eslint": "^8.31.0"       // Linting
  }
}
```

### Bundle Size
- Development: 3.03 MB (unminified)
- Production: 679 KB (minified)
- ⚠️ Warning: Exceeds recommended 244 KB limit (consider code splitting)

---

## Solution Package Contents

**File:** `SpeFileViewerSolution_v1.0.4.zip` (195 KB)

**Structure:**
```
SpeFileViewerSolution_v1.0.4.zip
├── [Content_Types].xml              // MIME type definitions
├── solution.xml                      // Solution metadata
│   - UniqueName: SpeFileViewerSolution
│   - Version: 1.0
│   - Publisher: Spaarke (prefix: sprk)
│   - Managed: 0 (Unmanaged)
│
├── customizations.xml                // Customizations wrapper
│   - CustomControls section (empty in current package - ⚠️ ISSUE?)
│
└── Controls/
    └── sprk_Spaarke.SpeFileViewer/
        ├── ControlManifest.xml       // Control definition (control-type="virtual")
        ├── bundle.js                 // Compiled control code (679 KB)
        ├── bundle.js.map             // Source map
        ├── SpeFileViewer.css         // Styles
        └── strings/
            └── SpeFileViewer.1033.resx  // English localization
```

**What Gets Imported:**
1. Custom control registration (`sprk_Spaarke.SpeFileViewer`)
2. Control manifest (properties, resources, version)
3. Compiled JavaScript bundle (React + MSAL + control logic)
4. CSS styles
5. Localization resources

---

## Version History & Changes

### v1.0.0 (Initial)
- Two-app MSAL architecture
- `documentId` property: `usage="bound"` (required field binding)
- `control-type="standard"`

### v1.0.1
- Added `clientAppId` property (separate from `bffAppId`)

### v1.0.2
- Fixed: Array extraction for field values
- Issue: Still required binding `documentId` to `sprk_graphitemid` (wrong field!)

### v1.0.3
- **Fix:** Changed `documentId` from `usage="bound"` to `usage="input"`
- **Goal:** Allow leaving documentId blank → uses form record ID (Dataverse GUID)
- **Result:** Should fix 404 errors (sending correct Document GUID instead of SharePoint Item ID)
- **Status:** ✅ This change was CORRECT

### v1.0.4 (Current - BROKEN)
- **Change:** Changed `control-type="standard"` to `control-type="virtual"`
- **Why:** Misunderstood user question about "why is it a field control"
- **Result:** ❌ Control no longer appears in control lists
- **Issue:** Virtual controls require `data-set` elements (for grids/datasets)
- **Status:** ⚠️ NEEDS REVERT

---

## Current Issues

### Issue #1: Control Not Appearing in UI (v1.0.4)
**Symptom:** After importing v1.0.4, control doesn't appear in "+ Component" menu or field controls list

**Root Cause:** `control-type="virtual"` without `data-set` element is invalid configuration

**PCF Control Types:**
- `control-type="standard"` = Field control (replaces default controls on fields)
- `control-type="virtual"` = Dataset control (for grids, requires `data-set` property)

**Virtual Control Requirements:**
```xml
<!-- REQUIRED for virtual controls -->
<data-set name="dataset" display-name-key="Dataset" />
```

**Fix Required:**
1. Revert `control-type="virtual"` → `control-type="standard"`
2. Keep `usage="input"` for documentId (that was correct)
3. Control should be added to forms via a field (can be hidden/display-only)

### Issue #2: Original 404 Error (May Still Exist)
**Symptom:** "Document not found" when trying to preview files

**Root Cause (Identified):**
- v1.0.2 and earlier: Control sent SharePoint Item ID instead of Dataverse GUID
- Why: `documentId` had `usage="bound"`, user bound it to `sprk_graphitemid` (SharePoint Item ID)
- BFF expects: Dataverse Document GUID (e.g., `ad1b0c34-52a5-f011-bbd3-7c1e5215b8b5`)
- Control was sending: SharePoint Item ID (e.g., `01LBYCMX76QPLGITR47BB355T4G2CVDL2B`)

**Fix Implemented (v1.0.3):**
- Changed `documentId` to `usage="input"` (not bound to field)
- When left blank, control uses `context.mode.contextInfo.entityId` (form record ID = Dataverse GUID)

**Status:** ✅ Should be fixed in v1.0.3+, but NOT TESTED due to v1.0.4 deployment issues

---

## Potential Architecture Concerns

### 1. PCF Control Type Confusion
**Observation:** User said control "previously was a standalone component"

**Reality Check:**
- PCF controls are either field controls or dataset controls
- No true "standalone component" option in PCF framework
- Field controls (`control-type="standard"`) CAN be added to forms without meaningful field binding

**Possible Interpretations:**
1. Control was always a field control, user misunderstood
2. Control should be an HTML web resource instead (different technology)
3. User expects "code component" behavior (Power Apps Component Framework v2)

### 2. Bundle Size Warning
**Issue:** 679 KB bundle exceeds recommended 244 KB limit

**Impact:**
- Slower initial load
- May impact form performance on slow connections

**Dependencies Contributing to Size:**
- React + React DOM: ~400 KB
- MSAL Browser: ~200 KB
- Fluent UI: ~100 KB

**Potential Optimizations:**
1. Code splitting (lazy load React components)
2. Tree shaking (remove unused Fluent UI components)
3. External CDN for React/MSAL (if Dataverse allows)

### 3. MSAL Token Acquisition Strategy
**Current Approach:** Try SSO → Cached → Popup

**Potential Issues:**
- Popup blocker interference (if SSO fails)
- User confusion if consent popup appears
- Token refresh not implemented (may expire during session)

**Recommendations:**
- Add token refresh logic (check token expiration, refresh proactively)
- Better error messages for popup blocked scenarios
- Consider redirect flow instead of popup (better mobile support)

### 4. Document ID Extraction Logic
**Current Logic:**
```typescript
// 1. Try configured documentId property
if (rawValue && typeof rawValue === 'string' && rawValue.trim() !== '') {
    return rawValue.trim();
}

// 2. Fall back to form record ID
const recordId = (context.mode as any).contextInfo?.entityId;
if (recordId) {
    return recordId;
}
```

**Potential Issues:**
- `(context.mode as any).contextInfo?.entityId` uses non-typed API
- May not work in all form contexts (e.g., quick create forms, dialogs)
- No validation that entityId is actually a Document record

**Recommendations:**
- Add entity type validation
- Handle edge cases (new records, non-Document forms)
- Provide better error messages when context unavailable

---

## Testing Checklist

### Unit Testing Gaps
- ❌ No automated tests for AuthService
- ❌ No automated tests for BffClient
- ❌ No automated tests for FilePreview component
- ❌ No automated tests for extractDocumentId logic

### Integration Testing Gaps
- ❌ MSAL authentication flow not tested end-to-end
- ❌ BFF API error scenarios not tested
- ❌ Correlation ID round-trip not verified

### Manual Testing Required
1. **Authentication Flow**
   - SSO silent works (no popup)
   - Popup appears if consent needed
   - Token cached properly

2. **Document ID Extraction**
   - Uses form record ID when documentId blank
   - Uses configured documentId when provided
   - Handles missing context gracefully

3. **Preview Rendering**
   - Preview loads for valid documents
   - Error shown for invalid/deleted documents
   - Retry button works

4. **Error Scenarios**
   - 404: Document not found
   - 401: Authentication failed
   - 403: Permission denied
   - 500: Server error
   - Network timeout

---

## Recommended Next Steps

### Immediate (Fix v1.0.4 Deployment Issue)
1. **Revert control-type to "standard"** in ControlManifest.Input.xml
2. **Build v1.0.5** with correct configuration
3. **Delete v1.0.4 solution** from Dataverse
4. **Import v1.0.5** and verify control appears
5. **Test Document ID extraction** (verify uses form record ID)
6. **Test preview rendering** (verify 404 error is fixed)

### Short-term (Improve Robustness)
1. Add token refresh logic (prevent mid-session expiration)
2. Add entity type validation (ensure running on Document forms)
3. Improve error messages (better user guidance)
4. Add retry with exponential backoff (handle transient errors)

### Medium-term (Optimize Performance)
1. Code splitting (reduce initial bundle size)
2. Lazy load Fluent UI components (only load what's needed)
3. Add loading indicators for each step (better UX)
4. Cache preview URLs (avoid redundant BFF calls)

### Long-term (Architecture Review)
1. **Consider HTML Web Resource** instead of PCF if standalone component truly needed
2. **Evaluate PCF vs Canvas Component** (if Component Framework v2 better fit)
3. **Add telemetry** (Application Insights for production monitoring)
4. **Add automated tests** (Jest + React Testing Library)

---

## Questions for Developer Review

1. **Control Type:** Should this be a field control (current) or a different technology entirely (HTML web resource)?
2. **Bundle Size:** Is 679 KB acceptable, or should we prioritize optimization?
3. **MSAL Strategy:** Is popup-based auth acceptable, or should we use redirect flow?
4. **Document ID Logic:** Is form record ID extraction robust enough, or do we need alternate approaches?
5. **Testing:** What level of automated testing is expected for PCF controls?
6. **Error Handling:** Are current error messages sufficient, or do we need more detailed troubleshooting guidance?

---

**Document Version:** 1.0
**Last Updated:** November 24, 2025
**Author:** Claude (AI Assistant)
**Status:** For Developer Review
