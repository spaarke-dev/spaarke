# Phase 4: PCF Control Implementation - Ready for Execution

## Status: ✅ Ready to Begin

**Prerequisites Complete**:
- ✅ Phase 3 BFF changes implemented and tested
- ✅ DocumentAuthorizationFilter wired to all endpoints
- ✅ Correlation ID tracking end-to-end
- ✅ CORS configured for Dataverse origins
- ✅ Build verification passed

---

## Overview

Create a PowerApps Component Framework (PCF) control that:
1. Displays file previews from SharePoint Embedded
2. Authenticates to BFF API using MSAL.js
3. Calls BFF directly (no Custom API or plugin)
4. Handles errors, token refresh, and correlation tracking

**Architecture**: `PCF → MSAL → BFF → SPE`

---

## Project Structure

Based on existing PCF controls (UniversalQuickCreate, UniversalDatasetGrid):

```
src/controls/SpeFileViewer/
├── SpeFileViewer/                    # Main PCF control
│   ├── components/                   # React components
│   │   └── FilePreview.tsx          # Preview iframe component
│   ├── services/                     # Business logic
│   │   ├── AuthService.ts           # MSAL authentication
│   │   └── BffClient.ts             # BFF API client
│   ├── types/                        # TypeScript types
│   │   └── BffTypes.ts              # Response interfaces
│   ├── utils/                        # Utilities
│   │   └── CorrelationId.ts         # GUID generation
│   ├── css/                          # Styles
│   │   └── SpeFileViewer.css        # Component styles
│   ├── strings/                      # Localization
│   │   └── SpeFileViewer.1033.resx  # English strings
│   ├── generated/                    # Auto-generated (PCF SDK)
│   │   └── ManifestTypes.d.ts       # Type definitions
│   ├── ControlManifest.Input.xml    # PCF manifest
│   └── index.ts                      # Control entry point
├── SpeFileViewerSolution/            # Dataverse solution
│   ├── Other/                        # Solution metadata
│   └── src/                          # Control reference
├── package.json                      # NPM dependencies
├── tsconfig.json                     # TypeScript config
├── pcfconfig.json                    # PAC PCF config
└── SpeFileViewer.pcfproj             # MSBuild project
```

---

## Implementation Steps

### Step 1: Create PCF Project

**Location**: `c:\code_files\spaarke\src\controls\SpeFileViewer`

**Commands**:
```bash
cd c:\code_files\spaarke\src\controls
mkdir SpeFileViewer
cd SpeFileViewer

# Initialize PCF project
pac pcf init \
  --namespace Spaarke \
  --name SpeFileViewer \
  --template field \
  --run-npm-install

# Create solution
cd ..
pac solution init \
  --publisher-name Spaarke \
  --publisher-prefix sprk

pac solution add-reference --path SpeFileViewer
```

**Why `field` template?**
- Field controls work on forms (bound to a field)
- Dataset controls work on grids (multiple records)
- We want to show preview for a single document → Use `field`

---

### Step 2: Install Dependencies

**File**: `package.json`

```bash
cd SpeFileViewer
npm install @azure/msal-browser
npm install react react-dom
npm install @fluentui/react
npm install @types/react @types/react-dom --save-dev
```

**Expected `package.json` additions**:
```json
{
  "dependencies": {
    "@azure/msal-browser": "^3.0.0",
    "@fluentui/react": "^8.110.0",
    "react": "^17.0.2",
    "react-dom": "^17.0.2"
  },
  "devDependencies": {
    "@types/react": "^17.0.0",
    "@types/react-dom": "^17.0.0"
  }
}
```

---

### Step 3: Update Control Manifest

**File**: `SpeFileViewer/ControlManifest.Input.xml`

```xml
<?xml version="1.0" encoding="utf-8" ?>
<manifest>
  <control namespace="Spaarke" constructor="SpeFileViewer" version="1.0.0" display-name-key="SpeFileViewer_Display_Key" description-key="SpeFileViewer_Desc_Key" control-type="standard">

    <!-- Input: Document GUID -->
    <property name="documentId" display-name-key="DocumentId_Display_Key" description-key="DocumentId_Desc_Key" of-type="SingleLine.Text" usage="bound" required="true" />

    <!-- Input: BFF API Base URL (for environment flexibility) -->
    <property name="bffApiUrl" display-name-key="BffApiUrl_Display_Key" description-key="BffApiUrl_Desc_Key" of-type="SingleLine.Text" usage="input" required="false" default-value="https://spe-api-dev-67e2xz.azurewebsites.net" />

    <!-- Input: BFF App ID (for MSAL scope) -->
    <property name="bffAppId" display-name-key="BffAppId_Display_Key" description-key="BffAppId_Desc_Key" of-type="SingleLine.Text" usage="input" required="true" />

    <!-- Input: Tenant ID (for MSAL authority) -->
    <property name="tenantId" display-name-key="TenantId_Display_Key" description-key="TenantId_Desc_Key" of-type="SingleLine.Text" usage="input" required="true" />

    <!-- Resources -->
    <resources>
      <code path="index.ts" order="1" />
      <css path="css/SpeFileViewer.css" order="1" />
      <resx path="strings/SpeFileViewer.1033.resx" version="1.0.0" />
    </resources>

    <!-- Feature usage -->
    <feature-usage>
      <uses-feature name="WebAPI" required="true" />
    </feature-usage>
  </control>
</manifest>
```

**Key Properties**:
- `documentId` (bound) - Binds to document GUID field on form
- `bffApiUrl` (input) - BFF base URL (environment-specific)
- `bffAppId` (input) - For MSAL scope construction
- `tenantId` (input) - For MSAL authority

---

### Step 4: Create TypeScript Types

**File**: `SpeFileViewer/types/BffTypes.ts`

```typescript
/**
 * Response from BFF /api/documents/{id}/preview-url endpoint
 */
export interface FilePreviewResponse {
  data: {
    previewUrl: string;
    postUrl: string | null;
    expiresAt: string;
    contentType: string | null;
  };
  metadata: {
    correlationId: string;
    documentId: string;
    fileName: string | null;
    fileSize: number | null;
    timestamp: string;
  };
}

/**
 * Error response from BFF API
 */
export interface BffErrorResponse {
  type: string;
  title: string;
  status: number;
  detail: string;
  traceId?: string;
  correlationId?: string;
}
```

---

### Step 5: Create Correlation ID Utility

**File**: `SpeFileViewer/utils/CorrelationId.ts`

```typescript
/**
 * Generates a correlation ID for request tracking.
 * Uses crypto.randomUUID() if available, otherwise falls back to manual generation.
 */
export function generateCorrelationId(): string {
  // Modern browsers support crypto.randomUUID()
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }

  // Fallback for older browsers
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === 'x' ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}
```

---

### Step 6: Create MSAL Auth Service

**File**: `SpeFileViewer/services/AuthService.ts`

**⚠️ CRITICAL**: Use **named scope** `api://<BFF_APP_ID>/SDAP.Access`, NOT `.default`

```typescript
import {
  PublicClientApplication,
  InteractionRequiredAuthError,
  SilentRequest,
  PopupRequest,
  AccountInfo
} from "@azure/msal-browser";

/**
 * MSAL instance - singleton across control lifecycle
 */
let msalInstance: PublicClientApplication | null = null;

/**
 * Initialize MSAL with tenant and client configuration.
 * Must be called before getBffToken().
 */
export async function initializeMsal(
  tenantId: string,
  clientId: string
): Promise<void> {
  if (msalInstance) {
    return; // Already initialized
  }

  const msalConfig = {
    auth: {
      clientId: clientId, // Dataverse app registration client ID
      authority: `https://login.microsoftonline.com/${tenantId}`,
      redirectUri: window.location.origin
    },
    cache: {
      cacheLocation: "sessionStorage" as const,
      storeAuthStateInCookie: false
    }
  };

  msalInstance = new PublicClientApplication(msalConfig);
  await msalInstance.initialize();
}

/**
 * Acquire BFF API token using NAMED scope (NOT .default).
 *
 * @param bffAppId - BFF application ID (e.g., "12345678-1234-1234-1234-123456789abc")
 * @returns Access token for BFF API
 *
 * @example
 * const token = await getBffToken("12345678-1234-1234-1234-123456789abc");
 * // Token audience: api://12345678-1234-1234-1234-123456789abc
 * // Token scope: api://12345678-1234-1234-1234-123456789abc/SDAP.Access
 */
export async function getBffToken(bffAppId: string): Promise<string> {
  if (!msalInstance) {
    throw new Error("MSAL not initialized. Call initializeMsal() first.");
  }

  const accounts = msalInstance.getAllAccounts();

  if (accounts.length === 0) {
    throw new Error("No user account found. Please sign in to Dataverse.");
  }

  const account: AccountInfo = accounts[0];

  // ✅ CRITICAL: Use named scope, NOT .default
  // .default is for daemon/confidential clients
  // Named scopes are for SPAs and delegated permissions
  const tokenRequest: SilentRequest = {
    scopes: [`api://${bffAppId}/SDAP.Access`], // ✅ Named scope
    account: account,
    forceRefresh: false
  };

  try {
    // Try silent acquisition (uses cached token if available)
    const response = await msalInstance.acquireTokenSilent(tokenRequest);
    return response.accessToken;
  } catch (error) {
    if (error instanceof InteractionRequiredAuthError) {
      // Silent acquisition failed, requires user interaction
      const popupRequest: PopupRequest = {
        scopes: [`api://${bffAppId}/SDAP.Access`],
        account: account,
        prompt: "select_account"
      };

      const response = await msalInstance.acquireTokenPopup(popupRequest);
      return response.accessToken;
    }

    // Re-throw other errors
    throw error;
  }
}

/**
 * Get current user's principal name (for debugging/logging)
 */
export function getCurrentUserPrincipalName(): string | null {
  if (!msalInstance) {
    return null;
  }

  const accounts = msalInstance.getAllAccounts();
  return accounts.length > 0 ? accounts[0].username : null;
}
```

**Key Points**:
- ✅ Uses `api://${bffAppId}/SDAP.Access` (named scope)
- ✅ Falls back to popup if silent acquisition fails
- ✅ Initializes MSAL only once (singleton)
- ✅ Uses sessionStorage for cache

---

### Step 7: Create BFF API Client

**File**: `SpeFileViewer/services/BffClient.ts`

```typescript
import { getBffToken } from "./AuthService";
import { FilePreviewResponse, BffErrorResponse } from "../types/BffTypes";
import { generateCorrelationId } from "../utils/CorrelationId";

/**
 * BFF API client for file preview operations.
 * Handles authentication, correlation tracking, and error handling.
 */
export class BffClient {
  private readonly baseUrl: string;
  private readonly bffAppId: string;

  constructor(baseUrl: string, bffAppId: string) {
    this.baseUrl = baseUrl.endsWith("/") ? baseUrl.slice(0, -1) : baseUrl;
    this.bffAppId = bffAppId;
  }

  /**
   * Get preview URL for a document.
   *
   * @param documentId - Document GUID
   * @returns Preview URL response with metadata
   * @throws Error if request fails (includes correlation ID)
   */
  async getPreviewUrl(documentId: string): Promise<FilePreviewResponse> {
    const correlationId = generateCorrelationId();

    try {
      // Acquire BFF token
      const token = await getBffToken(this.bffAppId);

      // Call BFF endpoint
      const response = await fetch(
        `${this.baseUrl}/api/documents/${documentId}/preview-url`,
        {
          method: "GET",
          headers: {
            "Authorization": `Bearer ${token}`,
            "Content-Type": "application/json",
            "X-Correlation-Id": correlationId // ✅ Send correlation ID
          }
        }
      );

      if (!response.ok) {
        await this.handleErrorResponse(response, correlationId);
      }

      const data: FilePreviewResponse = await response.json();

      // Verify correlation ID round-trip
      if (data.metadata.correlationId !== correlationId) {
        console.warn(
          `Correlation ID mismatch: sent ${correlationId}, received ${data.metadata.correlationId}`
        );
      }

      return data;
    } catch (error) {
      // Wrap error with correlation ID for debugging
      throw new Error(
        `[${correlationId}] Failed to get preview URL: ${error instanceof Error ? error.message : String(error)}`
      );
    }
  }

  /**
   * Handle error responses from BFF API.
   * Throws Error with formatted message including correlation ID.
   */
  private async handleErrorResponse(response: Response, correlationId: string): Promise<never> {
    let errorDetail: string;

    try {
      const errorData: BffErrorResponse = await response.json();
      errorDetail = errorData.detail || errorData.title || "Unknown error";
    } catch {
      errorDetail = response.statusText || "Request failed";
    }

    const statusCode = response.status;

    switch (statusCode) {
      case 401:
        throw new Error(`[${correlationId}] Unauthorized: Please sign in and try again.`);
      case 403:
        throw new Error(`[${correlationId}] Access Denied: You do not have permission to view this file.`);
      case 404:
        throw new Error(`[${correlationId}] Not Found: Document or file does not exist.`);
      case 500:
      case 502:
      case 503:
        throw new Error(`[${correlationId}] Server Error: ${errorDetail}`);
      default:
        throw new Error(`[${correlationId}] HTTP ${statusCode}: ${errorDetail}`);
    }
  }
}
```

**Key Features**:
- ✅ Generates and sends X-Correlation-Id header
- ✅ Verifies correlation ID round-trip
- ✅ Comprehensive error handling with user-friendly messages
- ✅ Wraps errors with correlation ID for debugging

---

### Step 8: Create React Preview Component

**File**: `SpeFileViewer/components/FilePreview.tsx`

```typescript
import * as React from "react";
import { FilePreviewResponse } from "../types/BffTypes";

export interface FilePreviewProps {
  documentId: string;
  bffClient: any; // BffClient instance
}

export const FilePreview: React.FC<FilePreviewProps> = ({ documentId, bffClient }) => {
  const [previewUrl, setPreviewUrl] = React.useState<string | null>(null);
  const [loading, setLoading] = React.useState<boolean>(true);
  const [error, setError] = React.useState<string | null>(null);
  const [metadata, setMetadata] = React.useState<FilePreviewResponse["metadata"] | null>(null);

  React.useEffect(() => {
    let mounted = true;

    async function loadPreview() {
      if (!documentId) {
        setError("No document selected");
        setLoading(false);
        return;
      }

      try {
        setLoading(true);
        setError(null);

        const response = await bffClient.getPreviewUrl(documentId);

        if (mounted) {
          setPreviewUrl(response.data.previewUrl);
          setMetadata(response.metadata);
          setLoading(false);
        }
      } catch (err) {
        if (mounted) {
          setError(err instanceof Error ? err.message : "Failed to load preview");
          setLoading(false);
        }
      }
    }

    loadPreview();

    return () => {
      mounted = false;
    };
  }, [documentId, bffClient]);

  if (loading) {
    return (
      <div className="spe-file-viewer-loading">
        <div className="spinner"></div>
        <p>Loading preview...</p>
      </div>
    );
  }

  if (error) {
    return (
      <div className="spe-file-viewer-error">
        <div className="error-icon">⚠️</div>
        <p className="error-message">{error}</p>
        {metadata?.correlationId && (
          <p className="error-correlation-id">
            Correlation ID: <code>{metadata.correlationId}</code>
          </p>
        )}
      </div>
    );
  }

  if (!previewUrl) {
    return (
      <div className="spe-file-viewer-no-preview">
        <p>No preview available for this document.</p>
      </div>
    );
  }

  return (
    <div className="spe-file-viewer-container">
      {metadata && (
        <div className="spe-file-viewer-metadata">
          <span className="file-name">{metadata.fileName || "Unknown file"}</span>
          {metadata.fileSize && (
            <span className="file-size"> ({(metadata.fileSize / 1024).toFixed(1)} KB)</span>
          )}
        </div>
      )}
      <iframe
        src={previewUrl}
        className="spe-file-viewer-iframe"
        title="File Preview"
        sandbox="allow-scripts allow-same-origin allow-popups"
      />
    </div>
  );
};
```

**Key Features**:
- ✅ Displays file metadata (name, size)
- ✅ Shows correlation ID in error state (for debugging)
- ✅ Loading and error states
- ✅ Sandboxed iframe for security
- ✅ Cleanup on unmount

---

### Step 9: Create Control Entry Point

**File**: `SpeFileViewer/index.ts`

```typescript
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom";
import { FilePreview } from "./components/FilePreview";
import { initializeMsal } from "./services/AuthService";
import { BffClient } from "./services/BffClient";

export class SpeFileViewer implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private container: HTMLDivElement;
  private notifyOutputChanged: () => void;
  private bffClient: BffClient | null = null;

  constructor() {}

  public async init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): Promise<void> {
    this.container = container;
    this.notifyOutputChanged = notifyOutputChanged;

    // Get configuration from manifest parameters
    const tenantId = context.parameters.tenantId?.raw || "";
    const bffAppId = context.parameters.bffAppId?.raw || "";
    const bffApiUrl = context.parameters.bffApiUrl?.raw || "https://spe-api-dev-67e2xz.azurewebsites.net";

    // Get Dataverse client ID from environment (or hardcode for now)
    // TODO: Make this configurable via manifest or environment variable
    const dataverseClientId = "YOUR_DATAVERSE_APP_CLIENT_ID"; // Replace with actual

    if (!tenantId || !bffAppId) {
      this.renderError("Missing required configuration: tenantId or bffAppId");
      return;
    }

    try {
      // Initialize MSAL
      await initializeMsal(tenantId, dataverseClientId);

      // Create BFF client
      this.bffClient = new BffClient(bffApiUrl, bffAppId);

      // Initial render
      this.renderControl(context);
    } catch (error) {
      this.renderError(`Failed to initialize: ${error instanceof Error ? error.message : String(error)}`);
    }
  }

  public updateView(context: ComponentFramework.Context<IInputs>): void {
    this.renderControl(context);
  }

  private renderControl(context: ComponentFramework.Context<IInputs>): void {
    const documentId = context.parameters.documentId?.raw || "";

    if (!this.bffClient) {
      this.renderError("BFF client not initialized");
      return;
    }

    ReactDOM.render(
      React.createElement(FilePreview, {
        documentId,
        bffClient: this.bffClient
      }),
      this.container
    );
  }

  private renderError(message: string): void {
    ReactDOM.render(
      React.createElement("div", {
        className: "spe-file-viewer-error",
        children: [
          React.createElement("p", { key: "error" }, message)
        ]
      }),
      this.container
    );
  }

  public getOutputs(): IOutputs {
    return {};
  }

  public destroy(): void {
    ReactDOM.unmountComponentAtNode(this.container);
  }
}
```

---

### Step 10: Create CSS Styles

**File**: `SpeFileViewer/css/SpeFileViewer.css`

```css
.spe-file-viewer-container {
  display: flex;
  flex-direction: column;
  height: 100%;
  width: 100%;
}

.spe-file-viewer-metadata {
  padding: 8px 12px;
  background-color: #f3f2f1;
  border-bottom: 1px solid #d2d0ce;
  font-size: 14px;
}

.spe-file-viewer-metadata .file-name {
  font-weight: 600;
}

.spe-file-viewer-metadata .file-size {
  color: #605e5c;
  margin-left: 8px;
}

.spe-file-viewer-iframe {
  flex: 1;
  width: 100%;
  border: none;
  min-height: 400px;
}

.spe-file-viewer-loading {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  height: 100%;
  padding: 40px;
}

.spe-file-viewer-loading .spinner {
  width: 40px;
  height: 40px;
  border: 4px solid #f3f2f1;
  border-top: 4px solid #0078d4;
  border-radius: 50%;
  animation: spin 1s linear infinite;
  margin-bottom: 16px;
}

@keyframes spin {
  0% { transform: rotate(0deg); }
  100% { transform: rotate(360deg); }
}

.spe-file-viewer-error {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 40px;
  text-align: center;
}

.spe-file-viewer-error .error-icon {
  font-size: 48px;
  margin-bottom: 16px;
}

.spe-file-viewer-error .error-message {
  color: #a80000;
  font-weight: 600;
  margin-bottom: 8px;
}

.spe-file-viewer-error .error-correlation-id {
  font-size: 12px;
  color: #605e5c;
}

.spe-file-viewer-error .error-correlation-id code {
  background-color: #f3f2f1;
  padding: 2px 6px;
  border-radius: 3px;
  font-family: 'Courier New', monospace;
}

.spe-file-viewer-no-preview {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
  padding: 40px;
  color: #605e5c;
}
```

---

## Build and Deploy

### Build Commands

```bash
cd c:\code_files\spaarke\src\controls\SpeFileViewer

# Install dependencies
npm install

# Build PCF control
npm run build

# Watch mode for development
npm start watch
```

### Package for Dataverse

```bash
# Build MSBuild project
cd c:\code_files\spaarke\src\controls\SpeFileViewer
msbuild /t:build /restore

# Or use pac CLI
pac solution pack --zipfile SpeFileViewer_1_0_0_0.zip

# Import to Dataverse
pac solution import --path SpeFileViewer_1_0_0_0.zip
```

---

## Configuration Checklist

Before deploying, ensure:

### Azure AD App Registrations

**BFF API App** (`YOUR_BFF_APP_ID`):
- [ ] Expose API scope: `SDAP.Access`
  - Results in: `api://YOUR_BFF_APP_ID/SDAP.Access`
- [ ] Add Dataverse PCF app as authorized client

**Dataverse/PCF App** (`YOUR_DATAVERSE_APP_CLIENT_ID`):
- [ ] Add API permission: `api://YOUR_BFF_APP_ID/SDAP.Access`
- [ ] Grant admin consent for tenant

### Dataverse Form Configuration

When adding control to form:
- [ ] Bind `documentId` property to document GUID field
- [ ] Set `bffApiUrl` to BFF base URL (e.g., `https://spe-api-dev-67e2xz.azurewebsites.net`)
- [ ] Set `bffAppId` to BFF application ID (GUID only, no `api://` prefix)
- [ ] Set `tenantId` to Azure AD tenant ID

---

## Testing Checklist

### Unit Testing (Optional)

- [ ] MSAL token acquisition with named scope
- [ ] BFF client sends X-Correlation-Id
- [ ] Error handling for 401/403/404/500
- [ ] React component renders correctly

### Integration Testing

- [ ] PCF loads on Dataverse form
- [ ] MSAL popup appears if needed
- [ ] Preview URL fetched successfully
- [ ] iframe displays preview
- [ ] Correlation ID in response metadata
- [ ] Error states display correctly

### User Acceptance Testing

- [ ] Authorized user sees preview
- [ ] Unauthorized user sees access denied error
- [ ] Missing document shows not found error
- [ ] Token refresh works after expiration
- [ ] CORS works from Dataverse form

---

## Troubleshooting

### Issue: "No user account found"

**Cause**: MSAL can't find authenticated user
**Fix**: Ensure user is signed into Dataverse/PowerApps

### Issue: "Unauthorized" (401)

**Cause**: Token audience mismatch or expired
**Fix**:
1. Verify `bffAppId` matches BFF app registration
2. Check API permission granted in Azure AD
3. Verify scope is `api://YOUR_BFF_APP_ID/SDAP.Access`

### Issue: "Access Denied" (403)

**Cause**: UAC denies access
**Fix**:
1. Check user has access to document in Dataverse
2. Review AuthorizationService rules
3. Check Application Insights for AUTHORIZATION DENIED logs

### Issue: CORS Error

**Cause**: BFF CORS policy doesn't allow Dataverse origin
**Fix**: Verify Program.cs CORS allows `*.dynamics.com` and `*.powerapps.com`

### Issue: "Failed to initialize MSAL"

**Cause**: Invalid tenantId or clientId
**Fix**: Verify manifest parameters are set correctly on form

---

## Key Differences from Documentation Plan

**Updated**:
1. ✅ Uses `field` template (not `dataset`) - better for single record
2. ✅ Adds manifest parameters for bffApiUrl, bffAppId, tenantId
3. ✅ Generates correlation ID in client (crypto.randomUUID)
4. ✅ Comprehensive error handling with correlation ID
5. ✅ Displays file metadata (name, size)
6. ✅ Sandboxed iframe for security
7. ✅ Proper TypeScript types with imports

**Maintained from Plan**:
- ✅ Named scope `api://<BFF_APP_ID>/SDAP.Access`
- ✅ X-Correlation-Id header
- ✅ React component structure
- ✅ MSAL authentication flow
- ✅ BFF client abstraction

---

## Success Criteria

Phase 4 complete when:
- [ ] PCF builds without errors
- [ ] PCF deploys to Dataverse
- [ ] PCF loads on document form
- [ ] Preview displays for authorized user
- [ ] Correlation ID tracks end-to-end
- [ ] Errors display with user-friendly messages
- [ ] No CORS errors
- [ ] Token refresh works

---

## Next Phase

**Phase 5: Final Documentation**
- Update ADR index
- Create deployment guide
- Create troubleshooting guide
- Document configuration for different environments
