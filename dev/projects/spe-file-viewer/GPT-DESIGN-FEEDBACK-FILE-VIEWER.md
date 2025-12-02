# Spaarke SPE File Viewer — Updated Design Feedback & Build Guidance

**Document Version:** 1.1  
**Purpose:** Updated technical review incorporating clarification that **SharePoint Embedded uses app‑only Graph access** by design, with **Spaarke UAC + Dataverse as the enforcement layer**, and delegated/OBO access is not required.

---

## 1. Summary of the Approach

The file viewer allows inline preview of SPE-stored documents inside a Dataverse Model-Driven App form.

**Execution path:**

```
PCF Control → Dataverse Custom API → Plugin → Spaarke BFF API → Graph API (SPE preview)
```

The PCF control receives only a **safe, embeddable preview URL**.  
All authentication happens on the server side (plugin and BFF).

---

## 2. Key Architectural Clarification: App-Only vs Delegated Graph

### 2.1 SharePoint Embedded is designed for **app-only access**
SPE is intentionally a **headless storage service**, and the **application identity** (service principal) is the intended actor for:

- Reading/writing files  
- Starting preview sessions  
- Managing containers  

SPE **does not support or require** per-user ACLs or delegated/OBO identity to access files.

### 2.2 Spaarke UAC remains the enforcement layer
Instead of SharePoint enforcing per-user permissions, **Spaarke enforces access** through:

- Dataverse record-level security  
- Spaarke UAC policies  
- BFF endpoint-level authorization (ADR‑003, ADR‑008)

This is the correct architecture for SPE.

### 2.3 Delegated/OBO Graph access is not required
Delegated/OBO may be used in traditional SharePoint Online integrations, but **for SPE it adds complexity with no functional benefit**.

**Therefore:**  
The viewer feature adopts **app-only Graph access** as the primary and recommended pattern.

---

## 3. Finalized Architectural Model

### 3.1 Authentication

| Layer | Identity Model | Purpose |
|-------|---------------|---------|
| PCF | None | Never handles auth/tokens |
| Custom API | Dataverse user | Confirms business/record context |
| Plugin | Service principal (Dataverse App User) | Calls BFF securely |
| BFF API | Service principal | Calls Graph/SPE using **app-only** token |
| Graph/SPE | Service principal | SPE trusts app identity |

### 3.2 Why this is correct
- No iframe/MSAL/interactive auth.  
- No need for per-user SPE permissions.  
- Aligns with ADR-005 (flat storage) and ADR-007 (single SPE facade).  
- Authorization remains consistent and centralized.

---

## 4. Updated Design Guidance

### 4.1 Plugin Responsibilities
- Keep the plugin extremely thin.
- Validate inputs (documentId).
- Call BFF using service principal credentials.
- Never touch tokens or SPE logic.
- Trust BFF responses and map them to Custom API output.

### 4.2 BFF Responsibilities
- Verify Dataverse/UAC authorization using the provided userId/documentId.
- Resolve driveId/itemId.
- Use **app-only Graph token** to call:
  ```
  POST /drives/{driveId}/items/{itemId}/preview
  ```
- Return:
  ```
  { fileUrl, expiresAt, fileName, contentType, correlationId }
  ```

### 4.3 PCF Responsibilities
- Call Custom API via `context.webAPI.execute`.
- Render iframe with preview URL.
- Automatically refresh URL before expiration.
- Surface retry + “open in full window” fallback UI.

---

## 5. Security Model

### 5.1 Access Enforcement
All access rules derive from:

- Dataverse privileges  
- Spaarke UAC  
- Plugin-level validation  
- BFF authorization filter  

**SharePoint Embedded remains blind to individual users**, which is correct and intended.

### 5.2 Logging
- Never log preview URLs.
- Never log access tokens.
- Use correlationId for troubleshooting.

---

## 6. Error Model

All endpoints return a unified structure:

```
{
  code: string,
  message: string,
  correlationId: string,
  httpStatus: number
}
```

PCF should show user-safe errors and developer logs should include correlation IDs.

---

## 7. Final Build Instructions (for Claude Code)

### 7.1 Build the Dataverse Custom API
- Name: `sprk_GetFilePreviewUrl`
- Input: `sprk_documentId`
- Output: preview DTO
- Exposed only to plugin

### 7.2 Build the Plugin
- Serve as proxy to BFF  
- Wires Dataverse → BFF → Custom API  
- Adds correlation IDs to traces  
- Contains **zero Graph logic**

### 7.3 Build the BFF Endpoint
- Route: `GET /api/documents/{documentId}/preview-url`
- Auth: service principal → Graph (app-only)
- Responsibilities:
  - Validate user authorization
  - Resolve drive/item IDs
  - Call Graph preview endpoint
  - Return DTO

### 7.4 SPE Preview Service
Implement:
```
SpeFileStore.GetPreviewUrlAsync(driveId, itemId)
```
Uses app-only Graph client.

### 7.5 PCF Viewer Control
- Calls Custom API (NOT BFF)
- Renders iframe
- Refreshes URL on expiration
- Provides fallback button to open in new tab
- No token logic  
- No Graph access  

---

## 8. Concluding Remarks

This updated design:

- Adheres to Microsoft’s intended SPE architecture  
- Avoids unnecessary delegated/OBO complexity  
- Uses app-only access correctly  
- Fits all Spaarke ADRs  
- Keeps authorization inside the Spaarke platform, not in SPE  

This document should now be provided to **Claude Code** as the authoritative guide for final implementation.

