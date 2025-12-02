# Architecture Correction: PCF → BFF Direct (No Plugin)

## The Mistake

I incorrectly designed a Custom API + Plugin that made **outbound HTTP calls** to the BFF API, which violates the SDAP architecture principle:

> **Plugins are transaction-scoped guardrails only. All external I/O (Graph/SPE, OAuth, web calls) belongs in Spe.Bff.Api or BackgroundService workers.**

## Correct Architecture: Option A (PCF Calls BFF Directly)

```
┌─────────────┐
│ PCF Control │ (in Dataverse form)
└──────┬──────┘
       │ 1. Acquire BFF token (MSAL.js)
       │ 2. GET /api/documents/{id}/preview
       ↓
┌─────────────────┐
│  Spe.Bff.Api    │
│  Preview        │
│  Endpoint       │
└──────┬──────────┘
       │ 3. Enforce UAC (endpoint filter)
       │ 4. Call Graph/SPE driveItem:preview
       ↓
┌─────────────────┐
│ SharePoint      │
│ Embedded (SPE)  │
└─────────────────┘
```

**No Dataverse plugin, no Custom API, no outbound HTTP from plugins.**

## What to Revert

### 1. Custom API (Delete from Dataverse)
- **Custom API**: `sprk_GetFilePreviewUrl`
- **Output Parameters**: All 6 parameters created
- **Action**: Delete via XrmToolBox Custom API Manager or PRT

### 2. Plugin Assembly (Delete from Dataverse)
- **Assembly**: `Spaarke.Dataverse.CustomApiProxy`
- **Plugin Type**: `GetFilePreviewUrlPlugin`
- **Action**: Delete via Plugin Registration Tool

### 3. External Service Config (Optional - Keep for Future)
- **Table**: `sprk_externalserviceconfig`
- **Record**: `SDAP_BFF_API`
- **Action**: Leave it (may be useful for future plugins that need config, just not HTTP calls)

### 4. Code Changes (Revert in Git)
- Delete or archive the plugin project:
  ```
  c:\code_files\spaarke\src\dataverse\Spaarke.CustomApiProxy\
  ```
- **Action**: Git revert or move to `_archive/` folder

## BFF Endpoint (Already Exists)

From Phase 1 (Task 1.2), the BFF endpoint was already created:

**Endpoint**: `GET /api/documents/{id}/preview-url`

**Expected Response**:
```json
{
  "data": {
    "previewUrl": "https://.../embed?...",
    "contentType": "application/pdf",
    "expiresAt": "2025-11-22T18:45:00Z"
  },
  "metadata": {
    "fileName": "document.pdf",
    "fileSize": 245678
  }
}
```

**Security**:
- ✅ UAC enforcement via endpoint filter
- ✅ App-only token to Graph/SPE (service principal)
- ✅ User context validated before call

### Verify BFF Endpoint

Let me verify the endpoint exists and has the correct implementation:

**File to check**: `c:\code_files\spaarke\src\web\Spe.Bff.Api\Endpoints\DocumentEndpoints.cs` (or similar)

## PCF Control Implementation

### Phase 3: Build PCF Control

The PCF control needs to:

1. **Authenticate** - Acquire BFF API token using MSAL.js
2. **Call BFF** - `GET /api/documents/{id}/preview-url`
3. **Display** - Render preview URL in iframe or trigger download

### MSAL Configuration (in PCF)

```typescript
import { PublicClientApplication } from "@azure/msal-browser";

const msalConfig = {
  auth: {
    clientId: "YOUR_CLIENT_ID", // Dataverse app registration
    authority: "https://login.microsoftonline.com/YOUR_TENANT_ID",
  }
};

const msalInstance = new PublicClientApplication(msalConfig);

// Acquire token for BFF API
const tokenRequest = {
  scopes: ["api://YOUR_BFF_APP_ID/.default"], // BFF audience
  account: msalInstance.getAllAccounts()[0]
};

const response = await msalInstance.acquireTokenSilent(tokenRequest);
const accessToken = response.accessToken;
```

### Call BFF from PCF

```typescript
const response = await fetch(
  `https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/${documentId}/preview-url`,
  {
    headers: {
      "Authorization": `Bearer ${accessToken}`,
      "Content-Type": "application/json"
    }
  }
);

const result = await response.json();
const previewUrl = result.data.previewUrl;

// Display in iframe
iframe.src = previewUrl;
```

## Architecture Decision Record (ADR)

**Decision**: For client-initiated operations that require external service calls (Graph, SPE, etc.), the PCF control authenticates to the BFF and calls BFF endpoints directly. The BFF enforces UAC and handles all external I/O.

**Reasoning**:
- Keeps plugins transaction-scoped (validation/projection only)
- Centralizes auth, retry, audit in BFF
- Reduces latency (no extra hop through Dataverse)
- Leverages standard OAuth patterns (user → BFF → Graph)

**Consequences**:
- PCF controls need MSAL.js and BFF token configuration
- BFF must expose endpoints for client operations
- No Custom APIs needed for simple read operations

## Next Steps

### Immediate (Revert Phase 2 Work)
- [ ] Delete Custom API `sprk_GetFilePreviewUrl` from Dataverse
- [ ] Delete plugin assembly `Spaarke.Dataverse.CustomApiProxy` from Dataverse
- [ ] Archive or delete plugin code from repository
- [ ] Update documentation to reflect correct architecture

### Phase 3 (PCF Development)
- [ ] Verify BFF endpoint `/api/documents/{id}/preview-url` exists and works
- [ ] Create PCF control project
- [ ] Implement MSAL authentication
- [ ] Call BFF endpoint
- [ ] Render preview URL
- [ ] Handle errors and token refresh

### Phase 4 (Testing)
- [ ] Test authentication flow (MSAL token acquisition)
- [ ] Test BFF call with valid token
- [ ] Test UAC enforcement (unauthorized user)
- [ ] Test preview URL display
- [ ] Test token expiration and refresh

### Phase 5 (Documentation)
- [ ] Document PCF → BFF architecture
- [ ] Update ADRs
- [ ] Create deployment guide
- [ ] Add troubleshooting guide

## Files to Check/Update

1. **BFF Endpoint**: `c:\code_files\spaarke\src\web\Spe.Bff.Api\Endpoints\DocumentEndpoints.cs`
2. **BFF Service**: `c:\code_files\spaarke\src\web\Spe.Bff.Api\Services\SpeFileStore.cs`
3. **PCF Project**: TBD (will create in Phase 3)

## Summary

**Before (Wrong)**:
```
PCF → Custom API → Plugin → HTTP → BFF → SPE
        ❌ Plugin makes HTTP calls
```

**After (Correct)**:
```
PCF → MSAL → BFF → SPE
      ✅ No plugin, BFF handles I/O
```

This aligns with SDAP architecture: **plugins are thin, BFF handles external I/O**.
