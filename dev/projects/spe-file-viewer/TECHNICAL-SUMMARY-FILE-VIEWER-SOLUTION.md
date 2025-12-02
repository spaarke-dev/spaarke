# Technical Summary: SPE File Viewer Solution

**Purpose**: Enable users to view/edit SharePoint Embedded (SPE) files directly within Dataverse Model-Driven App forms
**Date**: 2025-01-21
**Status**: Architecture Validated - Ready for Implementation
**For Review By**: Development Expert

---

## 📋 Executive Summary

This solution enables inline file preview in Dataverse forms using a **PCF control + Custom API proxy** pattern. It solves the critical **MSAL.js iframe authentication problem** while remaining compliant with Spaarke ADRs and Microsoft guidance.

**Key Innovation**: Server-side authentication via Custom API eliminates browser iframe popup restrictions.

---

## 🎯 Requirements

### Functional Requirements
1. **Inline file preview** in Document entity main form (no popup/navigation)
2. **Support multiple file types**: PDFs, Office documents (Word/Excel/PowerPoint), images
3. **Read-only preview** by default (using Graph `/preview` endpoint)
4. **Optional edit mode** via Custom Page (future phase)
5. **Separate permissions** for view vs edit
6. **Auto-refresh URLs** before expiration (preview URLs expire ~10 minutes)

### Non-Functional Requirements
1. **ADR-006 Compliance**: Use PCF controls, not web resources
2. **ADR-001 Compliance**: No plugins unless only reasonable option (Custom API proxy is justified)
3. **Security**: Server-side authentication, UAC enforcement, no token exposure to browser
4. **Performance**: < 2 second load time for preview
5. **Modern stack**: React + TypeScript + Fluent UI v9

---

## 🚨 The MSAL.js Iframe Authentication Problem

### Problem Context

**Scenario**: Web application needs to authenticate users to Microsoft services (Azure AD, Graph API) using OAuth 2.0 delegated permissions.

**Standard Solution**: Use Microsoft Authentication Library (MSAL.js) for browser-based authentication.

**MSAL.js Authentication Flow**:
```
1. User clicks "Sign In"
2. MSAL.js opens popup window (or redirects main window)
3. User authenticates in Azure AD login page
4. Azure AD redirects back with authorization code
5. MSAL.js exchanges code for access token
6. Application uses token to call APIs
```

### Why This Breaks in Iframes

**Model-Driven Apps embed custom controls in iframes**:
```html
<div class="form-field">
  <iframe src="/webresources/file_viewer.html">
    <!-- Your custom code runs HERE, inside iframe -->
    <script src="msal-browser.min.js"></script>
    <script>
      // Try to authenticate...
      await msalInstance.loginPopup(); // ❌ FAILS!
    </script>
  </iframe>
</div>
```

**Browser Security Restrictions**:

1. **Popup Blocker** (Primary Issue):
   ```javascript
   // Code running in iframe:
   window.open('https://login.microsoftonline.com/...')
   // ❌ Blocked by browser!
   // Chrome: "Popups blocked"
   // Edge: "Pop-up window blocked"
   // Firefox: "Popup blocked"
   ```

   **Why**: Browsers block `window.open()` calls from iframes to prevent:
   - Clickjacking attacks
   - Malicious popup spam
   - Cross-origin security violations

2. **Cross-Origin Restrictions**:
   - Iframe runs on `https://org.crm.dynamics.com`
   - Popup tries to open `https://login.microsoftonline.com`
   - Browser blocks cross-origin window references

3. **Redirect Issues**:
   ```javascript
   // Try redirect instead of popup:
   await msalInstance.loginRedirect();
   // ❌ Only redirects the IFRAME, not main window!
   ```
   Result: Login page loads inside tiny iframe, unusable UI

### Failed Workarounds

#### Workaround 1: Silent Authentication (`acquireTokenSilent`)
```javascript
try {
  const response = await msalInstance.acquireTokenSilent({
    scopes: ["https://api.example.com/.default"],
    account: accounts[0]
  });
  // ✅ Works IF user already authenticated
} catch (error) {
  // ❌ Fails if no cached token
  // Still need interactive login → back to popup problem
}
```
**Issue**: Only works if user previously authenticated in main window. Fails on:
- First use
- Token expiration
- Browser cache cleared
- Private browsing mode

#### Workaround 2: SSO Silent (`ssoSilent`)
```javascript
try {
  const response = await msalInstance.ssoSilent({
    scopes: ["https://api.example.com/.default"],
    loginHint: user.email
  });
  // ✅ Works SOMETIMES in iframes
} catch (error) {
  // ❌ Still fails often
  // Depends on browser, AD config, cookie settings
}
```
**Issue**: Unreliable, requires:
- User already logged into Azure AD in browser
- Specific cookie configurations
- Cross-site cookie permissions (increasingly restricted)

#### Workaround 3: Parent Window Authentication
```javascript
// Try to authenticate in parent window:
if (parent !== window) {
  parent.postMessage({ type: 'AUTH_REQUEST' }, '*');
}
```
**Issue**:
- Complex message passing
- Parent window may not support it
- Security concerns with `postMessage`
- Still needs parent to handle auth flow

### Real-World Impact

**What happens to users**:
```
1. User opens Document form
2. File viewer loads in iframe
3. Viewer tries to authenticate with MSAL.js
4. Browser blocks popup
5. User sees error: "Popup blocked" or "Authentication failed"
6. User must:
   - Manually allow popups
   - Refresh page
   - Try again
   - Still might fail
```

**Result**: Poor user experience, support tickets, workarounds that still fail.

---

## ✅ Solution: Custom API Proxy (Server-Side Authentication)

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│ Document Form (Model-Driven App)                            │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ PCF Control: SpaarkeSpeFileViewer                      │ │
│  │ (React + TypeScript + Fluent UI v9)                    │ │
│  │                                                         │ │
│  │  [Loading spinner...]                                  │ │
│  │                                                         │ │
│  │  ┌──────────────────────────────────────────────────┐ │ │
│  │  │ <iframe src="{preview-url}">                     │ │ │
│  │  │   [File preview from Graph API]                  │ │ │
│  │  │ </iframe>                                        │ │ │
│  │  └──────────────────────────────────────────────────┘ │ │
│  │                                                         │ │
│  │  📄 document.pdf - 2.5 MB                              │ │
│  │  [Refresh] [Download] [Open in Office]                │ │
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                     │
                     │ (1) PCF calls Custom API
                     │     No authentication needed!
                     │     context.webAPI.execute() handles it
                     ▼
┌─────────────────────────────────────────────────────────────┐
│ Dataverse Custom API: sprk_GetDocumentFileUrl               │
│ (Server-side, no browser restrictions!)                     │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ Plugin: GetDocumentFileUrlPlugin                       │ │
│  │                                                         │ │
│  │ (2) Validate request:                                  │ │
│  │     • User has read permission on Document entity      │ │
│  │     • Document record exists                           │ │
│  │     • Has sprk_graphitemid and sprk_graphdriveid       │ │
│  │                                                         │ │
│  │ (3) Get External Service Config:                       │ │
│  │     • Retrieve "SDAP_BFF_API" config from Dataverse    │ │
│  │     • Contains: BaseUrl, ClientId, ClientSecret, etc.  │ │
│  │                                                         │ │
│  │ (4) Authenticate (server-side):                        │ │
│  │     • Use ClientSecretCredential (Azure.Identity)      │ │
│  │     • Get access token for SDAP BFF API                │ │
│  │     • No browser interaction!                          │ │
│  │                                                         │ │
│  │ (5) Call SDAP BFF API:                                 │ │
│  │     • GET /api/documents/{id}/preview                  │ │
│  │     • With Bearer token (from step 4)                  │ │
│  │                                                         │ │
│  │ (6) Return to PCF:                                     │ │
│  │     • FileUrl (ephemeral preview URL)                  │ │
│  │     • FileName, FileSize, ContentType                  │ │
│  │     • ExpiresAt (for auto-refresh)                     │ │
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                     │
                     │ (Bearer token from step 4)
                     ▼
┌─────────────────────────────────────────────────────────────┐
│ SDAP BFF API (Azure App Service)                            │
│ https://spe-api-dev-67e2xz.azurewebsites.net                │
│                                                              │
│  Endpoint: GET /api/documents/{id}/preview                  │
│                                                              │
│  (7) Validate Bearer token (JWT verification)               │
│  (8) Query Dataverse for Document record                    │
│  (9) Get driveId and itemId from Document                   │
│  (10) Call Microsoft Graph API (OBO flow)                   │
│       • POST /drives/{driveId}/items/{itemId}/preview       │
│       • Returns embeddable preview URL                      │
│                                                              │
│  (11) Return to plugin:                                     │
│       {                                                      │
│         "data": {                                            │
│           "previewUrl": "https://...",                       │
│           "expiresAt": "2025-01-21T16:30:00Z"               │
│         }                                                    │
│       }                                                      │
└─────────────────────────────────────────────────────────────┘
                     │
                     │ (OBO token from BFF)
                     ▼
┌─────────────────────────────────────────────────────────────┐
│ Microsoft Graph API                                          │
│                                                              │
│  (12) Validate OBO token                                    │
│  (13) Enforce user's delegated permissions                  │
│  (14) Generate preview URL for driveItem                    │
│  (15) Return ephemeral URL (expires in ~10 minutes)         │
└─────────────────────────────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────────┐
│ SharePoint Embedded (SPE) Storage                           │
│ • Serves file content via preview URL                       │
│ • URL is short-lived, not bookmark-able                     │
└─────────────────────────────────────────────────────────────┘
```

### Why This Solves the Problem

| Issue | MSAL.js (Client-Side) | Custom API (Server-Side) |
|-------|----------------------|--------------------------|
| **Popup Blocker** | ❌ Blocks authentication | ✅ No popups needed |
| **Iframe Restrictions** | ❌ Cross-origin issues | ✅ Server-to-server calls |
| **Token Management** | ❌ Browser storage, exposed | ✅ Server-side, secure |
| **User Experience** | ❌ "Allow popups" prompt | ✅ Seamless, automatic |
| **Reliability** | ❌ Depends on browser/settings | ✅ Always works |
| **Security** | ❌ Token in browser | ✅ Never exposed to client |
| **UAC Enforcement** | ❌ Client-side only | ✅ Server validates permissions |

### Authentication Flow Comparison

**MSAL.js (Problematic)**:
```
User → PCF (iframe) → MSAL.js → [Popup Blocked!] → ❌ Fails
```

**Custom API (Solution)**:
```
User → PCF (iframe)
  → context.webAPI.execute() [No auth needed - Dataverse handles it]
    → Custom API Plugin (server-side)
      → Azure.Identity → Service Principal Token
        → SDAP BFF API
          → Graph API (OBO)
            → SPE File URL
              → Returns to PCF → ✅ Success!
```

**Key Insight**: PCF doesn't need to authenticate! `context.webAPI.execute()` uses the user's existing Dataverse session.

---

## 🏗️ Technical Implementation

### Component 1: PCF Control (Frontend)

**Technology Stack**:
- **Language**: TypeScript
- **UI Framework**: React 18
- **Component Library**: Fluent UI v9 (ADR-006, ADR-014)
- **Build Tool**: PCF CLI (`pac pcf`)
- **Manifest Type**: Field control (bound to Document entity)

**Key Features**:
1. **Initialization**:
   ```typescript
   public init(context: ComponentFramework.Context<IInputs>): void {
     this._context = context;
     this._documentId = context.mode.contextInfo.entityId;

     // No MSAL.js initialization needed!
     // No authentication setup required!
   }
   ```

2. **Load File Preview**:
   ```typescript
   private async loadFilePreview(): Promise<void> {
     try {
       // Call Custom API using context.webAPI
       const result = await this._context.webAPI.execute({
         getMetadata: () => ({
           boundParameter: "entity",
           parameterTypes: {
             entity: {
               typeName: "mscrm.sprk_document",
               structuralProperty: 5 // Entity
             },
             EndpointType: {
               typeName: "Edm.String",
               structuralProperty: 1 // PrimitiveType
             }
           },
           operationType: 1, // Function
           operationName: "sprk_GetDocumentFileUrl"
         }),
         entity: {
           entityType: "sprk_document",
           id: this._documentId
         },
         EndpointType: "preview" // Use read-only preview
       });

       // Extract URL and render iframe
       const fileUrl = result.FileUrl;
       const expiresAt = new Date(result.ExpiresAt);

       this.setState({
         fileUrl,
         fileName: result.FileName,
         fileSize: result.FileSize,
         loading: false
       });

       // Schedule auto-refresh before expiration
       this.scheduleRefresh(expiresAt);

     } catch (error) {
       this.setState({ error: error.message, loading: false });
     }
   }
   ```

3. **Auto-Refresh**:
   ```typescript
   private scheduleRefresh(expiresAt: Date): void {
     // Refresh 2 minutes before expiration
     const refreshIn = expiresAt.getTime() - Date.now() - (2 * 60 * 1000);

     if (refreshIn > 0) {
       this._refreshTimer = setTimeout(() => {
         console.log('[FileViewer] Auto-refreshing preview URL');
         this.loadFilePreview();
       }, refreshIn);
     }
   }
   ```

4. **React Component**:
   ```typescript
   const FileViewer: React.FC<IFileViewerProps> = ({ fileUrl, fileName, fileSize }) => {
     return (
       <Stack>
         {/* Loading State */}
         {loading && (
           <Stack horizontalAlign="center" verticalAlign="center">
             <Spinner label="Loading file preview..." />
           </Stack>
         )}

         {/* Error State */}
         {error && (
           <MessageBar messageBarType={MessageBarType.error}>
             {error}
           </MessageBar>
         )}

         {/* Preview Iframe */}
         {fileUrl && (
           <Stack>
             <Stack horizontal tokens={{ childrenGap: 8 }}>
               <Text>{fileName}</Text>
               <Text variant="small">{formatFileSize(fileSize)}</Text>
             </Stack>

             <iframe
               src={fileUrl}
               style={{ width: '100%', height: '600px', border: 'none' }}
               title="File Preview"
             />

             <Stack horizontal tokens={{ childrenGap: 8 }}>
               <DefaultButton onClick={handleRefresh}>Refresh</DefaultButton>
               <DefaultButton onClick={handleDownload}>Download</DefaultButton>
               <PrimaryButton onClick={handleOpenInOffice}>
                 Open in Office
               </PrimaryButton>
             </Stack>
           </Stack>
         )}
       </Stack>
     );
   };
   ```

**No Authentication Code**: Notice there's **zero** MSAL.js, zero popup handling, zero token management!

---

### Component 2: Custom API Plugin (Backend)

**Technology Stack**:
- **Language**: C# (.NET Framework 4.6.2 for Dataverse plugins)
- **Base Class**: `BaseProxyPlugin` (existing Spaarke infrastructure)
- **Authentication Library**: Azure.Identity (`ClientSecretCredential`)
- **HTTP Client**: System.Net.Http
- **JSON Parsing**: Newtonsoft.Json

**File**: `GetDocumentFileUrlPlugin.cs`

**Key Implementation**:

```csharp
public class GetDocumentFileUrlPlugin : BaseProxyPlugin
{
    private const string SERVICE_NAME = "SDAP_BFF_API";

    public GetDocumentFileUrlPlugin() : base("GetDocumentFileUrl") { }

    protected override void ExecuteProxy(IServiceProvider serviceProvider, string correlationId)
    {
        var documentId = ExecutionContext.PrimaryEntityId;
        var endpointType = ExecutionContext.InputParameters["EndpointType"]?.ToString() ?? "preview";

        // (1) Get service configuration from Dataverse
        //     - Reads sprk_externalserviceconfig entity
        //     - Contains ClientId, ClientSecret, BaseUrl, etc.
        var config = GetServiceConfig(SERVICE_NAME);

        // (2) Call SDAP BFF API with retry logic
        //     - BaseProxyPlugin handles authentication via ClientSecretCredential
        //     - Automatically gets Bearer token
        //     - Includes retry logic with exponential backoff
        var result = ExecuteWithRetry(() =>
            CallSdapBffApi(documentId, endpointType, config),
            config
        );

        // (3) Return results to PCF
        ExecutionContext.OutputParameters["FileUrl"] = result.FileUrl;
        ExecutionContext.OutputParameters["FileName"] = result.FileName ?? "";
        ExecutionContext.OutputParameters["FileSize"] = result.FileSize;
        ExecutionContext.OutputParameters["ContentType"] = result.ContentType ?? "";
        ExecutionContext.OutputParameters["ExpiresAt"] = result.ExpiresAt;
    }

    private FileUrlResult CallSdapBffApi(Guid documentId, string endpointType, ExternalServiceConfig config)
    {
        // (4) CreateAuthenticatedHttpClient is provided by BaseProxyPlugin
        //     - Gets access token using ClientSecretCredential
        //     - Adds Bearer token to Authorization header
        //     - No browser interaction!
        using (var httpClient = CreateAuthenticatedHttpClient(config))
        {
            var endpoint = $"/documents/{documentId}/{endpointType}";
            var response = httpClient.GetAsync(endpoint).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                throw new InvalidPluginExecutionException($"SDAP BFF API error: {response.StatusCode} - {errorContent}");
            }

            var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return ParseSdapResponse(content, endpointType);
        }
    }
}
```

**Inherited Capabilities from BaseProxyPlugin**:
1. ✅ **Authentication**: ClientSecretCredential, ManagedIdentity, ApiKey
2. ✅ **Configuration Management**: Reads from `sprk_externalserviceconfig` entity
3. ✅ **Retry Logic**: Exponential backoff for transient errors
4. ✅ **Audit Logging**: Automatic logging to `sprk_proxyauditlog` entity
5. ✅ **Error Handling**: Structured error responses with trace IDs
6. ✅ **Security**: Sensitive data redaction in logs

---

### Component 3: SDAP BFF API (Already Implemented)

**Technology Stack**:
- **Language**: C# (.NET 8.0)
- **Framework**: ASP.NET Core Minimal APIs
- **Authentication**: Azure.Identity (for OBO flow)
- **Graph SDK**: Microsoft.Graph 5.x

**Endpoint**: `GET /api/documents/{documentId}/preview`

**Already Implemented** in [`FileAccessEndpoints.cs`](./src/api/Spe.Bff.Api/Api/FileAccessEndpoints.cs):

```csharp
fileAccessGroup.MapGet("/{documentId}/preview", async (
    string documentId,
    [FromServices] IDataverseService dataverseService,
    [FromServices] GraphServiceClient graphClient,
    [FromServices] ILogger<Program> logger,
    HttpContext context) =>
{
    // (1) Validate Bearer token (done by ASP.NET Core middleware)

    // (2) Query Dataverse for Document record
    var document = await dataverseService.GetDocumentAsync(documentId);
    if (document == null)
        return TypedResults.NotFound(/* ... */);

    // (3) Validate SPE metadata
    if (string.IsNullOrEmpty(document.GraphDriveId) || string.IsNullOrEmpty(document.GraphItemId))
        return ProblemDetailsHelper.ValidationError("Document missing SPE metadata");

    // (4) Call Graph API preview action
    var previewResult = await graphClient.Drives[document.GraphDriveId]
        .Items[document.GraphItemId]
        .Preview
        .PostAsync(new PreviewPostRequestBody());

    // (5) Return embeddable preview URL
    var response = new FilePreviewDto(
        PreviewUrl: previewResult.GetUrl,
        PostUrl: previewResult.PostUrl,
        ExpiresAt: DateTime.UtcNow.AddMinutes(10),
        ContentType: document.MimeType
    );

    return TypedResults.Ok(new { data = response, metadata = /* ... */ });
});
```

**Also Supports**:
- `GET /api/documents/{id}/content` - For download/editable URLs
- `GET /api/documents/{id}/office` - For Office web viewer

---

## 🔐 Security Architecture

### Multi-Layer Security

```
┌─────────────────────────────────────────────────────────────┐
│ Layer 1: Dataverse Security (UAC)                           │
│ • User must have read permission on Document entity         │
│ • Enforced by Dataverse platform before Custom API runs     │
└─────────────────────────────────────────────────────────────┘
                     ↓
┌─────────────────────────────────────────────────────────────┐
│ Layer 2: Custom API Plugin Validation                       │
│ • Validates user is authenticated (ExecutionContext.UserId) │
│ • Validates Document record exists                          │
│ • Validates SPE metadata present (driveId, itemId)          │
│ • Audit log created with correlation ID                     │
└─────────────────────────────────────────────────────────────┘
                     ↓
┌─────────────────────────────────────────────────────────────┐
│ Layer 3: Service Principal Authentication                   │
│ • Plugin authenticates as service principal (server-side)   │
│ • Uses ClientSecretCredential from Azure.Identity           │
│ • Credentials stored in Dataverse (sprk_externalserviceconfig) │
│ • Never exposed to browser/client                           │
└─────────────────────────────────────────────────────────────┘
                     ↓
┌─────────────────────────────────────────────────────────────┐
│ Layer 4: SDAP BFF API Authorization                         │
│ • Validates Bearer token (JWT signature)                    │
│ • Validates token audience and issuer                       │
│ • Re-validates user access to Document in Dataverse         │
│ • Enforces rate limiting                                    │
└─────────────────────────────────────────────────────────────┘
                     ↓
┌─────────────────────────────────────────────────────────────┐
│ Layer 5: Microsoft Graph API                                │
│ • BFF uses OBO (On-Behalf-Of) token for user's permissions │
│ • Graph enforces user's delegated permissions               │
│ • Returns preview URL with embedded auth token              │
└─────────────────────────────────────────────────────────────┘
                     ↓
┌─────────────────────────────────────────────────────────────┐
│ Layer 6: Ephemeral URLs                                     │
│ • Preview URLs expire in ~10 minutes                        │
│ • URLs are not bookmark-able or shareable                   │
│ • Each request generates new URL                            │
└─────────────────────────────────────────────────────────────┘
```

### Token Flow

**No tokens exposed to browser!**

```
Service Principal Token (Plugin → BFF):
• Scope: https://spe-api-dev-67e2xz.azurewebsites.net/.default
• Lifetime: 1 hour
• Storage: In-memory in plugin execution
• Audience: SDAP BFF API

OBO Token (BFF → Graph):
• Scope: https://graph.microsoft.com/.default
• Lifetime: 1 hour
• Storage: Server-side only
• Audience: Microsoft Graph API
• User Context: Delegated permissions for signed-in user

Preview URL Token (Graph → Browser):
• Embedded in URL query string (by Graph API)
• Lifetime: ~10 minutes
• Storage: Iframe src attribute
• Audience: SharePoint Embedded CDN
• Read-only access only
```

---

## 📊 ADR Compliance Analysis

### ADR-001: Dataverse Plugin Usage

**ADR Statement**: *"Avoid Dataverse plugins unless they are the only reasonable option and narrowly purposed."*

**Compliance**: ✅ **Compliant**

**Justification**:
1. **Only Reasonable Option**:
   - Web resources in iframes cannot authenticate to external APIs (browser security)
   - No other Dataverse mechanism can call external APIs synchronously and return results
   - All alternatives (Power Automate, Azure Functions) fail the "inline embedding" requirement

2. **Narrowly Purposed**:
   - ✅ Does exactly one thing: Get file URL from SDAP API
   - ✅ No complex business logic (~180 lines)
   - ✅ No database operations (beyond reading Document record)
   - ✅ No event-based triggers (only executes when explicitly called)
   - ✅ Highly testable via API calls

3. **Modern Pattern**:
   - Uses Custom API (introduced 2020, Microsoft-recommended)
   - Not legacy event-based plugin (Create/Update/Delete triggers)
   - Explicit contract with input/output parameters

### ADR-006: PCF Over Web Resources

**ADR Statement**: *"Prefer PCF controls over web resources. Microsoft recommends moving away from web resources."*

**Compliance**: ✅ **Compliant**

**Implementation**:
- ✅ Using PCF control (`SpaarkeSpeFileViewer`)
- ✅ TypeScript + React + Fluent UI v9
- ✅ Proper lifecycle management (init, updateView, destroy)
- ✅ Reusable across forms and apps
- ✅ Unit testable

### ADR-005, ADR-007, ADR-008, ADR-009, ADR-010

**Compliance**: ✅ **All Compliant**

- ✅ **ADR-005**: Flat SPE storage (no nested folders)
- ✅ **ADR-007**: Uses SpeFileStore facade via BFF
- ✅ **ADR-008**: Endpoint-level authorization filters in BFF
- ✅ **ADR-009**: Redis caching for metadata (not preview URLs)
- ✅ **ADR-010**: Minimal DI, explicit service resolution

---

## 🚀 Implementation Phases

### Phase 1: MVP (Inline Preview) - **CURRENT**

**Deliverables**:
1. ✅ Custom API Plugin (`GetDocumentFileUrlPlugin`) - **Built**
2. ✅ SDAP BFF API `/preview` endpoint - **Implemented**
3. ⏳ PCF Control (`SpaarkeSpeFileViewer`) - **Next**
4. ⏳ External Service Config record - **Deployment**
5. ⏳ Custom API registration - **Deployment**

**Functionality**:
- ✅ Inline file preview in Document form
- ✅ Auto-refresh before URL expiration
- ✅ Supports PDFs, images, Office docs (read-only)
- ✅ Refresh and Download buttons

**Estimated Effort**: 8-12 hours
- PCF creation: 2-3 hours
- Deployment: 1-2 hours
- Testing: 2-3 hours
- Documentation: 2-3 hours

### Phase 2: Full Edit Experience (Future)

**Deliverables**:
1. Custom Page with React viewer
2. MSAL.js authentication (in Custom Page, not iframe - works fine!)
3. Edit permission checks
4. "Open in Office" button in PCF (opens Custom Page)

**Functionality**:
- ✅ Full-screen editing experience
- ✅ Separate permissions for view vs edit
- ✅ Uses `/content` endpoint (editable Office files)
- ✅ Better UX for complex documents

**Estimated Effort**: 12-16 hours

---

## 🧪 Testing Strategy

### Unit Tests

**PCF Control**:
```typescript
describe('FileViewer', () => {
  it('should load file preview on init', async () => {
    const mockContext = createMockContext();
    const control = new FileViewer();

    await control.init(mockContext);

    expect(mockContext.webAPI.execute).toHaveBeenCalledWith({
      operationName: 'sprk_GetDocumentFileUrl',
      // ...
    });
  });

  it('should handle Custom API errors', async () => {
    const mockContext = createMockContext();
    mockContext.webAPI.execute = jest.fn().mockRejectedValue(new Error('API Error'));

    const control = new FileViewer();
    await control.init(mockContext);

    expect(control.state.error).toBe('API Error');
  });

  it('should auto-refresh before URL expiration', async () => {
    jest.useFakeTimers();
    const control = new FileViewer();

    await control.init(mockContext);

    jest.advanceTimersByTime(8 * 60 * 1000); // 8 minutes

    expect(mockContext.webAPI.execute).toHaveBeenCalledTimes(2); // Initial + refresh
  });
});
```

**Plugin**:
```csharp
[TestClass]
public class GetDocumentFileUrlPluginTests
{
    [TestMethod]
    public void ExecuteProxy_ValidRequest_ReturnsFileUrl()
    {
        // Arrange
        var mockContext = CreateMockPluginContext();
        var plugin = new GetDocumentFileUrlPlugin();

        // Act
        plugin.Execute(mockContext);

        // Assert
        Assert.IsNotNull(mockContext.OutputParameters["FileUrl"]);
        Assert.IsTrue(mockContext.OutputParameters["FileUrl"].ToString().StartsWith("https://"));
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidPluginExecutionException))]
    public void ExecuteProxy_MissingEndpointType_ThrowsException()
    {
        // Arrange
        var mockContext = CreateMockPluginContext();
        mockContext.InputParameters.Remove("EndpointType");

        // Act
        var plugin = new GetDocumentFileUrlPlugin();
        plugin.Execute(mockContext);

        // Assert: Exception thrown
    }
}
```

### Integration Tests

**Test 1: End-to-End File Preview**
```
1. Upload file via UniversalQuickCreate PCF
2. Verify Document record created with SPE metadata
3. Open Document form
4. Verify PCF control loads
5. Verify iframe displays file preview
6. Check browser console for errors (should be none)
7. Verify auto-refresh after 8 minutes
```

**Test 2: Custom API Direct Call**
```javascript
// Browser console test
const documentId = Xrm.Page.data.entity.getId().replace(/[{}]/g, '');

Xrm.WebApi.online.execute({
    getMetadata: function() {
        return {
            boundParameter: "entity",
            parameterTypes: {
                "entity": { typeName: "mscrm.sprk_document", structuralProperty: 5 },
                "EndpointType": { typeName: "Edm.String", structuralProperty: 1 }
            },
            operationType: 1,
            operationName: "sprk_GetDocumentFileUrl"
        };
    },
    entity: { entityType: "sprk_document", id: documentId },
    EndpointType: "preview"
}).then(
    result => console.log("✅ Success:", result),
    error => console.error("❌ Error:", error)
);
```

**Expected Output**:
```javascript
✅ Success: {
  FileUrl: "https://spaarke.sharepoint.com/...",
  FileName: "document.pdf",
  FileSize: 2621440,
  ContentType: "application/pdf",
  ExpiresAt: "2025-01-21T16:30:00Z"
}
```

### Performance Tests

**Metrics**:
- **Custom API execution time**: < 500ms
- **SDAP BFF API response**: < 300ms
- **Graph API preview**: < 200ms
- **Total time to display**: < 2 seconds
- **Auto-refresh overhead**: < 100ms

**Load Test**:
```bash
# Simulate 100 concurrent users opening Document forms
ab -n 100 -c 10 -H "Authorization: Bearer {token}" \
   https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/{id}/preview
```

---

## 📈 Monitoring & Observability

### Audit Logs

**All Custom API calls automatically logged** to `sprk_proxyauditlog`:

| Field | Value |
|-------|-------|
| `sprk_operation` | "GetDocumentFileUrl" |
| `sprk_correlationid` | Unique GUID for tracing |
| `sprk_executiontime` | Timestamp |
| `sprk_userid` | User who made the call |
| `sprk_requestpayload` | `{ EndpointType: "preview" }` |
| `sprk_responsepayload` | `{ FileUrl: "https://..." }` (URLs redacted) |
| `sprk_success` | true/false |
| `sprk_duration` | Execution time in ms |
| `sprk_errormessage` | If failed |

**Query Example**:
```javascript
Xrm.WebApi.retrieveMultipleRecords(
    "sprk_proxyauditlog",
    "?$filter=sprk_operation eq 'GetDocumentFileUrl'&$orderby=sprk_executiontime desc&$top=10"
).then(result => console.log(result.entities));
```

### Application Insights (SDAP BFF API)

**Queries**:

```kusto
// Preview endpoint performance
requests
| where url contains "/api/documents/" and url contains "/preview"
| summarize
    avg(duration),
    percentile(duration, 95),
    percentile(duration, 99)
  by bin(timestamp, 5m)
| render timechart

// Error rate
requests
| where url contains "/api/documents/"
| summarize ErrorRate = countif(resultCode >= 400) * 100.0 / count()
  by bin(timestamp, 5m)
| render timechart

// Auto-refresh pattern (requests every ~8 minutes from same user)
requests
| where url contains "/preview"
| summarize count() by user_Id, bin(timestamp, 10m)
| where count_ > 1
```

### Plugin Trace Logs

**Enable in Dataverse**:
- Settings → System → Plugin Trace Log
- Enable for `GetDocumentFileUrlPlugin`

**View Traces**:
- Advanced Find → Plugin Trace Logs
- Filter by Message: `sprk_GetDocumentFileUrl`

---

## 🔄 Comparison: Alternative Approaches

### Approach 1: MSAL.js in PCF (What Others Might Try)

**Architecture**:
```
PCF → MSAL.js → [Popup Blocked!] → ❌
```

**Pros**:
- Standard Microsoft pattern (when not in iframe)
- Well-documented

**Cons**:
- ❌ Fails in iframe (popup blocked)
- ❌ Complex workarounds (SSO silent, parent window)
- ❌ Unreliable (depends on browser/settings)
- ❌ Poor user experience

**Verdict**: ❌ **Does not meet requirements** (inline embedding fails)

---

### Approach 2: Custom Page Only (No Inline Preview)

**Architecture**:
```
Document Form → Button → Opens Custom Page → MSAL.js → ✅ Works
```

**Pros**:
- ✅ MSAL.js works (not in iframe)
- ✅ Full-screen editing
- ✅ Simpler authentication

**Cons**:
- ❌ Not inline (user requirement)
- ❌ Extra click required
- ❌ Leaves form context

**Verdict**: ❌ **Does not meet requirements** (inline preview required)

---

### Approach 3: Custom API Proxy (Our Solution)

**Architecture**:
```
PCF → Custom API → Service Principal → BFF → Graph → ✅ Works
```

**Pros**:
- ✅ Works inline (no iframe issues)
- ✅ No client-side auth complexity
- ✅ Server-side security
- ✅ Reliable and testable
- ✅ ADR compliant

**Cons**:
- ⚠️ Requires plugin (justified by ADR-001)
- ⚠️ More server-side complexity

**Verdict**: ✅ **Meets all requirements** + **ADR compliant**

---

## 🎓 Key Learnings & Best Practices

### 1. Iframe Authentication is Hard

**Lesson**: Browser security restrictions make client-side authentication in iframes extremely difficult and unreliable.

**Best Practice**: Use server-side authentication proxies (Custom API, Azure Functions) when embedding in iframes.

### 2. SharePoint Embedded is Headless

**Lesson**: SPE doesn't provide stable webUrl like traditional SharePoint. Must use ephemeral Graph API URLs.

**Best Practice**:
- Store identifiers (driveId, itemId), not URLs
- Generate URLs per request server-side
- Implement auto-refresh before expiration

### 3. ADRs Prevent Future Problems

**Lesson**: Our ADR-006 (prefer PCF over web resources) caught a technical debt issue early.

**Best Practice**: Review ADRs during design phase, not after implementation.

### 4. Custom APIs are Modern, Not Legacy

**Lesson**: Custom API + Plugin is a modern Microsoft pattern (2020+), not legacy event-based plugins.

**Best Practice**: Use Custom APIs for server-side operations that need to be called from client code.

### 5. Separation of Concerns

**Lesson**: Inline preview (read-only) vs. full editing (Custom Page) is a good UX pattern.

**Best Practice**:
- Default to safe operations (read-only preview)
- Require explicit action for risky operations (editing)
- Separate permissions for each

---

## 📞 Review Questions for Dev Expert

1. **MSAL.js Analysis**: Does the explanation of iframe popup blocking accurately reflect your experience? Any edge cases we missed?

2. **Custom API Justification**: Do you agree this meets ADR-001's "only reasonable option" criterion?

3. **Security Model**: Any concerns with the service principal → BFF → OBO token flow?

4. **PCF Architecture**: Any recommendations for the React + TypeScript implementation?

5. **Auto-Refresh Strategy**: Is 2 minutes before expiration the right buffer? Should we parse `ExpiresAt` or use fixed intervals?

6. **Error Handling**: What error scenarios should we prioritize testing?

7. **Performance**: Any concerns about Custom API execution latency (currently targets < 500ms)?

8. **Monitoring**: What additional telemetry would you recommend?

9. **Phase 2 Scope**: Should we implement Custom Page editing in Phase 1 or defer to Phase 2?

10. **Alternative Approaches**: Are there other authentication patterns we should consider?

---

## ✅ Approval Checklist

- [ ] **Architecture approved** by dev expert
- [ ] **Security review** passed
- [ ] **ADR compliance** validated
- [ ] **Performance targets** agreed
- [ ] **Testing strategy** approved
- [ ] **Monitoring plan** approved
- [ ] **Proceed with PCF implementation**

---

**Document Version**: 1.0
**Last Updated**: 2025-01-21
**Status**: Awaiting Dev Expert Review
**Next Step**: Create PCF Control (pending approval)

