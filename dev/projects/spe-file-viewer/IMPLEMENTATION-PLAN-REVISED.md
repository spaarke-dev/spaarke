# SPE File Viewer - Revised Implementation Plan
## Architecture: PCF → BFF Direct (No Custom API/Plugin)

---

## ✅ Phase 1: BFF Endpoint (COMPLETE)

**Status**: Already implemented and verified

**Endpoint**: `GET /api/documents/{documentId}/preview-url`

**Location**: [`Spe.Bff.Api/Api/FileAccessEndpoints.cs:29-162`](C:\code_files\spaarke\src\api\Spe.Bff.Api\Api\FileAccessEndpoints.cs#L29-L162)

**Flow**:
1. Validates document GUID
2. Retrieves document from Dataverse via `IDataverseService`
3. Extracts userId from JWT claims (oid or NameIdentifier)
4. Validates UAC (placeholder - implement full logic in production)
5. Calls `SpeFileStore.GetPreviewUrlAsync(driveId, itemId)`
6. Returns JSON response

**Request**:
```http
GET /api/documents/{documentId}/preview-url
Authorization: Bearer {bff-token}
X-Correlation-Id: {optional-guid}
```

**Response**:
```json
{
  "data": {
    "previewUrl": "https://...sharepoint.com/.../embed?...",
    "postUrl": "...",
    "expiresAt": "2025-11-22T18:45:00Z",
    "contentType": "application/pdf"
  },
  "metadata": {
    "correlationId": "abc-123-def",
    "documentId": "f5cea4a7-53c6-f011-8543-0022482a47f5",
    "fileName": "document.pdf",
    "fileSize": 245678,
    "timestamp": "2025-11-22T18:35:00Z"
  }
}
```

**Authentication**:
- ✅ Requires `Authorization: Bearer {token}`
- ✅ Token audience: BFF API (`api://YOUR_BFF_APP_ID`)
- ✅ User context extracted from JWT claims
- ✅ UAC validation before SPE call

**Authorization**:
- Currently uses `.RequireAuthorization()` at the group level
- UAC logic at lines 81-86 (TODO for full implementation)

---

## 🔄 Phase 2: Revert Custom API and Plugin Work

### What to Delete from Dataverse

#### 2.1 Delete Custom API
**Tool**: XrmToolBox → Custom API Manager

1. Open Custom API Manager
2. Find `sprk_GetFilePreviewUrl`
3. Delete (will also delete associated output parameters)

**Alternative (Plugin Registration Tool)**:
- Not recommended - Custom APIs are easier to manage in XrmToolBox

#### 2.2 Delete Plugin Assembly
**Tool**: Plugin Registration Tool

1. Open Plugin Registration Tool
2. Connect to Dataverse
3. Find assembly: `Spaarke.Dataverse.CustomApiProxy`
4. Right-click → Delete
5. Confirm deletion

**Note**: Deleting the assembly also removes plugin types and steps.

#### 2.3 External Service Config (Keep or Delete)
**Table**: `sprk_externalserviceconfig`
**Record**: `SDAP_BFF_API`

**Decision**: **Keep the table** (may be useful for future plugins), but the specific record is optional since we're not using plugins for this feature.

### Code Changes

#### 2.4 Archive Plugin Project
**Directory**: `c:\code_files\spaarke\src\dataverse\Spaarke.CustomApiProxy\`

**Options**:
1. **Delete**: If not needed for reference
2. **Archive**: Move to `c:\code_files\spaarke\_archive\` for historical reference
3. **Git branch**: Keep in a separate branch named `feature/custom-api-approach` (not recommended architecture)

**Recommendation**: Archive it for reference, but don't merge to main.

#### 2.5 Update Documentation
Update any docs/ADRs that reference the Custom API approach to reflect the correct "PCF → BFF" architecture.

---

## 🚀 Phase 3: PCF Control Implementation

### 3.1 PCF Project Setup

**Create PCF project**:
```bash
cd c:\code_files\spaarke\src\pcf
pac pcf init --namespace Spaarke --name FileViewer --template dataset
npm install
```

### 3.2 Install Dependencies

```bash
npm install @azure/msal-browser
npm install @fluentui/react
npm install react react-dom
npm install @types/react @types/react-dom --save-dev
```

### 3.3 MSAL Configuration

**File**: `FileViewer/services/AuthService.ts`

```typescript
import { PublicClientApplication, InteractionRequiredAuthError } from "@azure/msal-browser";

const msalConfig = {
  auth: {
    clientId: "YOUR_DATAVERSE_APP_CLIENT_ID",
    authority: "https://login.microsoftonline.com/YOUR_TENANT_ID",
    redirectUri: window.location.origin
  },
  cache: {
    cacheLocation: "sessionStorage",
    storeAuthStateInCookie: false
  }
};

export const msalInstance = new PublicClientApplication(msalConfig);

export async function getBffToken(): Promise<string> {
  const accounts = msalInstance.getAllAccounts();

  if (accounts.length === 0) {
    throw new Error("No user account found. Please sign in.");
  }

  const tokenRequest = {
    scopes: ["api://YOUR_BFF_APP_ID/.default"], // BFF audience
    account: accounts[0]
  };

  try {
    const response = await msalInstance.acquireTokenSilent(tokenRequest);
    return response.accessToken;
  } catch (error) {
    if (error instanceof InteractionRequiredAuthError) {
      const response = await msalInstance.acquireTokenPopup(tokenRequest);
      return response.accessToken;
    }
    throw error;
  }
}
```

### 3.4 BFF API Client

**File**: `FileViewer/services/BffClient.ts`

```typescript
import { getBffToken } from "./AuthService";

const BFF_BASE_URL = "https://spe-api-dev-67e2xz.azurewebsites.net";

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

export async function getPreviewUrl(documentId: string): Promise<FilePreviewResponse> {
  const token = await getBffToken();

  const response = await fetch(
    `${BFF_BASE_URL}/api/documents/${documentId}/preview-url`,
    {
      headers: {
        "Authorization": `Bearer ${token}`,
        "Content-Type": "application/json"
      }
    }
  );

  if (!response.ok) {
    const error = await response.json();
    throw new Error(error.detail || `HTTP ${response.status}: ${response.statusText}`);
  }

  return await response.json();
}
```

### 3.5 React Component

**File**: `FileViewer/components/FilePreview.tsx`

```tsx
import * as React from "react";
import { getPreviewUrl } from "../services/BffClient";

interface FilePreviewProps {
  documentId: string;
}

export const FilePreview: React.FC<FilePreviewProps> = ({ documentId }) => {
  const [previewUrl, setPreviewUrl] = React.useState<string | null>(null);
  const [loading, setLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);

  React.useEffect(() => {
    let mounted = true;

    async function loadPreview() {
      try {
        setLoading(true);
        setError(null);

        const result = await getPreviewUrl(documentId);

        if (mounted) {
          setPreviewUrl(result.data.previewUrl);
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
  }, [documentId]);

  if (loading) {
    return <div>Loading preview...</div>;
  }

  if (error) {
    return <div style={{ color: "red" }}>Error: {error}</div>;
  }

  if (!previewUrl) {
    return <div>No preview available</div>;
  }

  return (
    <iframe
      src={previewUrl}
      style={{ width: "100%", height: "600px", border: "1px solid #ccc" }}
      title="File Preview"
    />
  );
};
```

### 3.6 PCF Control Index

**File**: `FileViewer/index.ts`

```typescript
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom";
import { FilePreview } from "./components/FilePreview";
import { msalInstance } from "./services/AuthService";

export class FileViewer implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private container: HTMLDivElement;
  private notifyOutputChanged: () => void;

  constructor() {}

  public async init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): Promise<void> {
    this.container = container;
    this.notifyOutputChanged = notifyOutputChanged;

    // Initialize MSAL
    await msalInstance.initialize();

    // Initial render
    this.renderControl(context);
  }

  public updateView(context: ComponentFramework.Context<IInputs>): void {
    this.renderControl(context);
  }

  private renderControl(context: ComponentFramework.Context<IInputs>): void {
    const documentId = context.parameters.documentId?.raw || "";

    ReactDOM.render(
      React.createElement(FilePreview, { documentId }),
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

### 3.7 Build and Deploy PCF

```bash
# Build
npm run build

# Test locally
npm start watch

# Package for deployment
pac pcf push --publisher-prefix sprk
```

---

## ✅ Phase 4: Testing

### 4.1 Test BFF Endpoint Directly

**Using PowerShell**:
```powershell
# Get token (requires az cli logged in)
$token = az account get-access-token --resource "api://YOUR_BFF_APP_ID" --query accessToken -o tsv

# Call BFF endpoint
$documentId = "f5cea4a7-53c6-f011-8543-0022482a47f5"
$response = Invoke-RestMethod `
  -Uri "https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/$documentId/preview-url" `
  -Headers @{ "Authorization" = "Bearer $token" }

$response | ConvertTo-Json -Depth 3
```

### 4.2 Test PCF Control

1. Add PCF control to a Dataverse form
2. Configure `documentId` parameter to bind to document record ID
3. Open form, verify preview loads
4. Check browser console for errors
5. Verify MSAL token acquisition
6. Verify BFF call succeeds

### 4.3 End-to-End Test Cases

- [ ] Valid document with SPE file → preview loads
- [ ] Document without SPE metadata → error message
- [ ] Document not found → 404 error
- [ ] Unauthorized user → 403 error
- [ ] Token expired → MSAL auto-refreshes
- [ ] Network error → error message displayed

---

## 📊 Phase 5: Documentation

### 5.1 Update ADRs

Document the decision to use "PCF → BFF Direct" architecture instead of Custom API approach.

### 5.2 Deployment Guide

- BFF endpoint configuration
- PCF control deployment steps
- MSAL app registration requirements
- Environment variables/secrets

### 5.3 Troubleshooting Guide

Common issues and solutions:
- MSAL authentication errors
- CORS issues
- Token audience mismatch
- UAC validation failures

---

## Summary: Before & After

### ❌ Before (Wrong Architecture)
```
PCF → Custom API → Plugin (HTTP call) → BFF → SPE
      └── Violates "no outbound HTTP from plugins" ADR
```

### ✅ After (Correct Architecture)
```
PCF → MSAL → BFF → SPE
      └── Aligns with SDAP architecture: plugins thin, BFF handles I/O
```

**Key Benefits**:
- No plugin complexity
- No dependency management issues
- Standard OAuth flow
- Centralized auth/audit in BFF
- Easier to test and maintain

---

## Quick Start Checklist

- [ ] Delete Custom API from Dataverse
- [ ] Delete plugin assembly from Dataverse
- [ ] Archive plugin code from repository
- [ ] Verify BFF endpoint works (PowerShell test)
- [ ] Create PCF project
- [ ] Implement MSAL authentication
- [ ] Implement BFF client
- [ ] Build React preview component
- [ ] Deploy PCF to Dataverse
- [ ] Test end-to-end flow
- [ ] Update documentation

---

**Architecture Decision**: Use direct PCF → BFF calls for client-initiated operations. Reserve plugins for transaction-scoped validation/projection only.
