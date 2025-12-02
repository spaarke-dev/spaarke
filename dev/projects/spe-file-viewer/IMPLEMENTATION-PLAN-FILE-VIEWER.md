# Implementation Plan: SPE File Viewer with PCF Control

**Version**: 2.0 (Revised with GPT Design Feedback)
**Date**: 2025-01-21
**Status**: Ready for Step-by-Step Implementation

---

## 📋 Executive Summary

### What We're Building
A **PCF-based file viewer** that displays SharePoint Embedded (SPE) files inline in Dataverse Model-Driven App forms using **server-side authentication via Custom API**.

### Key Architectural Changes (Based on GPT Feedback)

**CRITICAL CLARIFICATION**: SharePoint Embedded uses **app-only access** by design, NOT delegated/OBO.

| Original Assumption | Corrected Understanding |
|---------------------|------------------------|
| ❌ Use OBO (On-Behalf-Of) tokens | ✅ Use app-only service principal tokens |
| ❌ SPE has per-user permissions | ✅ SPE is headless, Spaarke UAC enforces permissions |
| ❌ BFF calls Graph with user context | ✅ BFF calls Graph with service principal (app identity) |
| ❌ Complex delegated auth flow | ✅ Simple app-only auth flow |

**Result**: Simpler architecture, aligns with Microsoft's SPE design intent.

---

## 🎯 Final Architecture

```
┌─────────────────────────────────────────────────────────────┐
│ Document Form (Model-Driven App)                            │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ PCF Control: SpaarkeSpeFileViewer                      │ │
│  │ • No authentication logic                              │ │
│  │ • Calls Custom API via context.webAPI                  │ │
│  │ • Renders iframe with preview URL                      │ │
│  │ • Auto-refreshes before expiration                     │ │
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                     │
                     │ (1) context.webAPI.execute()
                     │     Uses Dataverse session (no auth needed)
                     ▼
┌─────────────────────────────────────────────────────────────┐
│ Dataverse Custom API: sprk_GetFilePreviewUrl                │
│ • Validates user has read permission on Document            │
│ • Enforces Dataverse UAC                                    │
└─────────────────────────────────────────────────────────────┘
                     │
                     │ (2) Plugin executes
                     ▼
┌─────────────────────────────────────────────────────────────┐
│ Plugin: GetFilePreviewUrlPlugin                              │
│ • THIN proxy layer                                          │
│ • Validates documentId input                                │
│ • Gets service principal token (app-only)                   │
│ • Calls BFF API                                             │
│ • NO Graph logic                                            │
│ • Adds correlation IDs to traces                            │
└─────────────────────────────────────────────────────────────┘
                     │
                     │ (3) Bearer token (service principal)
                     ▼
┌─────────────────────────────────────────────────────────────┐
│ SDAP BFF API                                                │
│ Endpoint: GET /api/documents/{documentId}/preview-url       │
│                                                              │
│ (4) Validate user authorization:                            │
│     • Verify userId has access to documentId (UAC)          │
│     • Spaarke UAC is the enforcement layer                  │
│                                                              │
│ (5) Resolve SPE metadata:                                   │
│     • Query Dataverse for driveId, itemId                   │
│                                                              │
│ (6) Call SPE preview service:                               │
│     • SpeFileStore.GetPreviewUrlAsync(driveId, itemId)      │
│     • Uses app-only Graph token (service principal)         │
└─────────────────────────────────────────────────────────────┘
                     │
                     │ (7) App-only Graph token
                     ▼
┌─────────────────────────────────────────────────────────────┐
│ Microsoft Graph API                                          │
│ POST /drives/{driveId}/items/{itemId}/preview               │
│                                                              │
│ • Accepts app-only token (service principal)                │
│ • SPE trusts application identity                           │
│ • Returns embeddable preview URL                            │
│ • URL expires in ~10 minutes                                │
└─────────────────────────────────────────────────────────────┘
                     │
                     │ (8) Preview URL
                     ▼
┌─────────────────────────────────────────────────────────────┐
│ SharePoint Embedded Storage                                 │
│ • Serves file content via preview URL                       │
│ • Headless storage (no per-user ACLs)                       │
│ • Spaarke UAC enforces access, not SPE                      │
└─────────────────────────────────────────────────────────────┘
```

---

## 🔑 Key Principles (from GPT Feedback)

### 1. App-Only Access
- **SPE is designed for app-only access**
- Service principal is the actor for all Graph/SPE operations
- NO delegated/OBO tokens needed
- Simpler token acquisition

### 2. Spaarke UAC is Enforcement Layer
- **SPE does not enforce per-user permissions**
- Spaarke enforces access via:
  - Dataverse record-level security
  - Spaarke UAC policies
  - BFF endpoint-level authorization

### 3. Plugin is Thin Proxy
- Validates inputs
- Calls BFF with service principal token
- NO Graph logic
- NO SPE logic
- Adds correlation IDs

### 4. BFF Validates Authorization
- Verifies userId has access to documentId
- Resolves driveId/itemId from Dataverse
- Calls Graph with app-only token
- Returns safe preview URL

### 5. PCF is Display Only
- Calls Custom API (NOT BFF directly)
- Renders iframe
- Auto-refreshes URL
- NO authentication logic

---

## 📦 What Already Exists

### ✅ Infrastructure (Reusable)

**File**: [`BaseProxyPlugin.cs`](./src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/BaseProxyPlugin.cs)

**Provides**:
- `GetServiceConfig(serviceName)` - Reads `sprk_externalserviceconfig`
- `CreateAuthenticatedHttpClient(config)` - Creates HttpClient with Bearer token
- `ExecuteWithRetry(action, config)` - Retry logic with exponential backoff
- `LogRequest()` / `LogResponse()` - Audit logging to `sprk_proxyauditlog`
- `GetClientCredentialsToken(config)` - Gets app-only token via `ClientSecretCredential`

**Authentication**: Already supports app-only access via `ClientSecretCredential` (Azure.Identity)

### ✅ SDAP BFF API Endpoints (Partially Built)

**File**: [`FileAccessEndpoints.cs`](./src/api/Spe.Bff.Api/Api/FileAccessEndpoints.cs)

**Status**:
- ✅ `/preview` endpoint exists (line 30-131)
- ⚠️ May need updates to ensure app-only token usage
- ⚠️ May need route change: `/preview` → `/preview-url`

### ✅ Plugin Assembly Built

**File**: `Spaarke.Dataverse.CustomApiProxy.dll`
**Location**: `c:\code_files\spaarke\src\dataverse\Spaarke.CustomApiProxy\Plugins\Spaarke.Dataverse.CustomApiProxy\bin\Release\net462\`
**Status**: Compiled successfully (0 errors)

---

## 🚧 What Needs to Be Built/Updated

### 1. SpeFileStore Service (New or Update)

**Purpose**: Abstraction layer for SPE operations

**Required Method**:
```csharp
Task<PreviewUrlResult> GetPreviewUrlAsync(string driveId, string itemId)
```

**Location**: `src/api/Spe.Bff.Api/Infrastructure/Graph/` or `src/shared/Spaarke.Dataverse/Services/`

**Implementation**:
```csharp
public class SpeFileStore
{
    private readonly GraphServiceClient _graphClient; // App-only client

    public async Task<PreviewUrlResult> GetPreviewUrlAsync(string driveId, string itemId)
    {
        // Call Graph API preview endpoint
        var previewResult = await _graphClient.Drives[driveId]
            .Items[itemId]
            .Preview
            .PostAsync(new PreviewPostRequestBody());

        return new PreviewUrlResult
        {
            PreviewUrl = previewResult.GetUrl,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            ContentType = previewResult.PostUrl // May need to determine from item metadata
        };
    }
}
```

**Need to Verify**: Does `SpeFileStore` already exist? Check if it uses app-only or delegated tokens.

---

### 2. Custom API Registration

**Name**: `sprk_GetFilePreviewUrl` (NOT `sprk_GetDocumentFileUrl`)

**Configuration**:
- **Unique Name**: `sprk_GetFilePreviewUrl`
- **Display Name**: `Get File Preview URL`
- **Binding Type**: Entity (sprk_document)
- **Is Function**: Yes
- **Is Private**: No

**Input Parameter**:
- **Name**: `sprk_documentId` (NOT `EndpointType`)
- **Type**: String (GUID)
- **Required**: Yes

**Output Parameters**:
- `FileUrl` (String) - Preview URL
- `FileName` (String) - File name
- `FileSize` (Integer) - Size in bytes
- `ContentType` (String) - MIME type
- `ExpiresAt` (DateTime) - URL expiration
- `CorrelationId` (String) - For troubleshooting

---

### 3. Plugin Update

**File**: Update `GetDocumentFileUrlPlugin.cs` → `GetFilePreviewUrlPlugin.cs`

**Key Changes**:
1. Rename class: `GetFilePreviewUrlPlugin`
2. Remove `EndpointType` parameter
3. Always call `/preview-url` endpoint (not dynamic)
4. Simplify to thin proxy
5. Add correlation ID to output
6. Keep app-only token acquisition (already correct in BaseProxyPlugin!)

**Updated Code Structure**:
```csharp
public class GetFilePreviewUrlPlugin : BaseProxyPlugin
{
    private const string SERVICE_NAME = "SDAP_BFF_API";

    public GetFilePreviewUrlPlugin() : base("GetFilePreviewUrl") { }

    protected override void ExecuteProxy(IServiceProvider serviceProvider, string correlationId)
    {
        // (1) Validate input
        var documentId = ExecutionContext.InputParameters["sprk_documentId"]?.ToString();
        if (string.IsNullOrEmpty(documentId) || !Guid.TryParse(documentId, out _))
            throw new InvalidPluginExecutionException("Invalid sprk_documentId");

        // (2) Get service config (includes app-only credentials)
        var config = GetServiceConfig(SERVICE_NAME);

        // (3) Call BFF with retry logic
        // BaseProxyPlugin.CreateAuthenticatedHttpClient uses app-only token!
        var result = ExecuteWithRetry(() =>
            CallBffApi(documentId, config, correlationId),
            config
        );

        // (4) Return to PCF
        ExecutionContext.OutputParameters["FileUrl"] = result.FileUrl;
        ExecutionContext.OutputParameters["FileName"] = result.FileName;
        ExecutionContext.OutputParameters["FileSize"] = result.FileSize;
        ExecutionContext.OutputParameters["ContentType"] = result.ContentType;
        ExecutionContext.OutputParameters["ExpiresAt"] = result.ExpiresAt;
        ExecutionContext.OutputParameters["CorrelationId"] = correlationId;
    }

    private FileUrlResult CallBffApi(string documentId, ExternalServiceConfig config, string correlationId)
    {
        using (var httpClient = CreateAuthenticatedHttpClient(config)) // App-only token!
        {
            // Add correlation ID header
            httpClient.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

            var endpoint = $"/documents/{documentId}/preview-url";
            var response = httpClient.GetAsync(endpoint).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                throw new InvalidPluginExecutionException($"BFF API error: {response.StatusCode} - {errorContent}");
            }

            var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return ParseBffResponse(content);
        }
    }
}
```

---

### 4. BFF Endpoint Update

**Route**: Change from `/preview` to `/preview-url`

**File**: Update `FileAccessEndpoints.cs`

**Key Changes**:
1. Route: `GET /api/documents/{documentId}/preview-url`
2. Add `userId` parameter (from HTTP context)
3. Add UAC validation
4. Ensure app-only Graph token usage
5. Add correlation ID support

**Updated Code Structure**:
```csharp
fileAccessGroup.MapGet("/{documentId}/preview-url", async (
    string documentId,
    [FromServices] IDataverseService dataverseService,
    [FromServices] SpeFileStore speFileStore, // Use abstraction
    [FromServices] ILogger<Program> logger,
    HttpContext context) =>
{
    var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault()
        ?? context.TraceIdentifier;
    var userId = context.User.FindFirst("oid")?.Value; // Get from JWT

    try
    {
        // (1) Validate documentId
        if (!Guid.TryParse(documentId, out _))
            return ProblemDetailsHelper.ValidationError("Invalid documentId");

        logger.LogInformation("Getting preview URL for document {DocumentId}, user {UserId}, correlation {CorrelationId}",
            documentId, userId, correlationId);

        // (2) Query Dataverse for Document record
        var document = await dataverseService.GetDocumentAsync(documentId);
        if (document == null)
            return TypedResults.NotFound(new {
                code = "DocumentNotFound",
                message = $"Document {documentId} not found",
                correlationId,
                httpStatus = 404
            });

        // (3) Validate user has access (Spaarke UAC enforcement)
        // TODO: Implement UAC check
        // if (!await uacService.CanUserAccessDocument(userId, documentId))
        //     return TypedResults.Forbid();

        // (4) Validate SPE metadata
        if (string.IsNullOrEmpty(document.GraphDriveId) || string.IsNullOrEmpty(document.GraphItemId))
        {
            logger.LogWarning("Document {DocumentId} missing SPE metadata", documentId);
            return ProblemDetailsHelper.ValidationError(
                "Document does not have associated SharePoint Embedded file metadata");
        }

        // (5) Call SpeFileStore (uses app-only Graph token)
        var previewResult = await speFileStore.GetPreviewUrlAsync(
            document.GraphDriveId,
            document.GraphItemId
        );

        // (6) Return response
        var response = new
        {
            fileUrl = previewResult.PreviewUrl,
            fileName = document.FileName,
            fileSize = document.FileSize,
            contentType = document.MimeType,
            expiresAt = previewResult.ExpiresAt,
            correlationId
        };

        logger.LogInformation("Preview URL generated successfully for document {DocumentId}", documentId);

        return TypedResults.Ok(response);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to generate preview URL for document {DocumentId}", documentId);
        return TypedResults.Problem(
            statusCode: 500,
            title: "Internal Server Error",
            detail: "An unexpected error occurred while generating preview URL",
            extensions: new Dictionary<string, object?> {
                ["correlationId"] = correlationId
            });
    }
});
```

---

### 5. PCF Control (New)

**Name**: `SpaarkeSpeFileViewer`
**Type**: Field control (bound to Document entity)
**Technology**: React + TypeScript + Fluent UI v9

**Project Structure**:
```
SpaarkeSpeFileViewer/
├── ControlManifest.Input.xml
├── index.ts
├── components/
│   ├── FileViewer.tsx
│   ├── LoadingSpinner.tsx
│   ├── ErrorMessage.tsx
│   └── FileToolbar.tsx
├── services/
│   └── CustomApiService.ts
├── types/
│   └── index.ts
└── css/
    └── FileViewer.css
```

**Key Implementation Points**:

**index.ts**:
```typescript
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom";
import { FileViewer } from "./components/FileViewer";

export class SpaarkeSpeFileViewer implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private _context: ComponentFramework.Context<IInputs>;
    private _container: HTMLDivElement;
    private _documentId: string;

    public init(context: ComponentFramework.Context<IInputs>, notifyOutputChanged: () => void, state: ComponentFramework.Dictionary, container: HTMLDivElement): void {
        this._context = context;
        this._container = container;

        // Get document ID from form context (bound entity)
        this._documentId = context.mode.contextInfo.entityId;

        // Render React component
        this.renderComponent();
    }

    public updateView(context: ComponentFramework.Context<IInputs>): void {
        this._context = context;
        this.renderComponent();
    }

    private renderComponent(): void {
        ReactDOM.render(
            React.createElement(FileViewer, {
                context: this._context,
                documentId: this._documentId
            }),
            this._container
        );
    }

    public destroy(): void {
        ReactDOM.unmountComponentAtNode(this._container);
    }
}
```

**components/FileViewer.tsx**:
```typescript
import * as React from "react";
import { Stack, Spinner, MessageBar, MessageBarType, PrimaryButton, DefaultButton, Text } from "@fluentui/react";
import { CustomApiService } from "../services/CustomApiService";
import { IFilePreviewResult } from "../types";

interface IFileViewerProps {
    context: ComponentFramework.Context<any>;
    documentId: string;
}

interface IFileViewerState {
    loading: boolean;
    error: string | null;
    fileUrl: string | null;
    fileName: string | null;
    fileSize: number;
    expiresAt: Date | null;
    correlationId: string | null;
}

export const FileViewer: React.FC<IFileViewerProps> = ({ context, documentId }) => {
    const [state, setState] = React.useState<IFileViewerState>({
        loading: true,
        error: null,
        fileUrl: null,
        fileName: null,
        fileSize: 0,
        expiresAt: null,
        correlationId: null
    });

    const customApiService = new CustomApiService(context);
    const refreshTimerRef = React.useRef<number | null>(null);

    React.useEffect(() => {
        loadFilePreview();

        return () => {
            // Cleanup timer on unmount
            if (refreshTimerRef.current) {
                clearTimeout(refreshTimerRef.current);
            }
        };
    }, [documentId]);

    const loadFilePreview = async (): Promise<void> => {
        try {
            setState(prev => ({ ...prev, loading: true, error: null }));

            // Call Custom API (NO authentication needed!)
            const result = await customApiService.getFilePreviewUrl(documentId);

            setState({
                loading: false,
                error: null,
                fileUrl: result.FileUrl,
                fileName: result.FileName,
                fileSize: result.FileSize,
                expiresAt: new Date(result.ExpiresAt),
                correlationId: result.CorrelationId
            });

            // Schedule auto-refresh 2 minutes before expiration
            scheduleRefresh(new Date(result.ExpiresAt));

        } catch (error: any) {
            console.error("[FileViewer] Failed to load preview:", error);
            setState(prev => ({
                ...prev,
                loading: false,
                error: error.message || "Failed to load file preview"
            }));
        }
    };

    const scheduleRefresh = (expiresAt: Date): void => {
        const refreshIn = expiresAt.getTime() - Date.now() - (2 * 60 * 1000); // 2 min buffer

        if (refreshIn > 0) {
            refreshTimerRef.current = window.setTimeout(() => {
                console.log("[FileViewer] Auto-refreshing preview URL");
                loadFilePreview();
            }, refreshIn);
        }
    };

    const handleRefresh = (): void => {
        loadFilePreview();
    };

    const handleDownload = (): void => {
        if (state.fileUrl) {
            window.open(state.fileUrl, "_blank");
        }
    };

    const handleOpenInNewTab = (): void => {
        if (state.fileUrl) {
            window.open(state.fileUrl, "_blank", "noopener,noreferrer");
        }
    };

    return (
        <Stack tokens={{ childrenGap: 12 }} styles={{ root: { height: "100%", padding: 16 } }}>
            {/* Loading State */}
            {state.loading && (
                <Stack horizontalAlign="center" verticalAlign="center" styles={{ root: { height: 200 } }}>
                    <Spinner label="Loading file preview..." />
                </Stack>
            )}

            {/* Error State */}
            {state.error && (
                <MessageBar messageBarType={MessageBarType.error} onDismiss={() => setState(prev => ({ ...prev, error: null }))}>
                    {state.error}
                    {state.correlationId && (
                        <Text variant="small" block>Correlation ID: {state.correlationId}</Text>
                    )}
                </MessageBar>
            )}

            {/* Preview Iframe */}
            {!state.loading && !state.error && state.fileUrl && (
                <Stack tokens={{ childrenGap: 8 }} styles={{ root: { height: "100%" } }}>
                    {/* Toolbar */}
                    <Stack horizontal tokens={{ childrenGap: 8 }} verticalAlign="center">
                        <Text>{state.fileName}</Text>
                        <Text variant="small">({(state.fileSize / (1024 * 1024)).toFixed(2)} MB)</Text>
                        <Stack.Item grow={1} />
                        <DefaultButton onClick={handleRefresh} text="Refresh" />
                        <DefaultButton onClick={handleDownload} text="Download" />
                        <PrimaryButton onClick={handleOpenInNewTab} text="Open in New Tab" />
                    </Stack>

                    {/* File Preview Iframe */}
                    <iframe
                        src={state.fileUrl}
                        title="File Preview"
                        style={{
                            width: "100%",
                            height: "600px",
                            border: "none",
                            borderRadius: 4,
                            backgroundColor: "#f5f5f5"
                        }}
                    />
                </Stack>
            )}
        </Stack>
    );
};
```

**services/CustomApiService.ts**:
```typescript
import { IFilePreviewResult } from "../types";

export class CustomApiService {
    private _context: ComponentFramework.Context<any>;

    constructor(context: ComponentFramework.Context<any>) {
        this._context = context;
    }

    public async getFilePreviewUrl(documentId: string): Promise<IFilePreviewResult> {
        try {
            // Call Custom API using context.webAPI.execute
            // NO authentication needed - Dataverse handles it!
            const result = await this._context.webAPI.execute({
                getMetadata: () => ({
                    boundParameter: "entity",
                    parameterTypes: {
                        "entity": {
                            "typeName": "mscrm.sprk_document",
                            "structuralProperty": 5 // Entity
                        },
                        "sprk_documentId": {
                            "typeName": "Edm.String",
                            "structuralProperty": 1 // PrimitiveType
                        }
                    },
                    operationType: 1, // Function
                    operationName: "sprk_GetFilePreviewUrl"
                }),
                entity: {
                    entityType: "sprk_document",
                    id: documentId
                },
                sprk_documentId: documentId
            });

            // Parse response
            return {
                FileUrl: result.FileUrl,
                FileName: result.FileName,
                FileSize: result.FileSize,
                ContentType: result.ContentType,
                ExpiresAt: result.ExpiresAt,
                CorrelationId: result.CorrelationId
            };

        } catch (error: any) {
            console.error("[CustomApiService] Error calling Custom API:", error);
            throw new Error(error.message || "Failed to get file preview URL");
        }
    }
}
```

---

### 6. External Service Config Record

**Entity**: `sprk_externalserviceconfig`
**Name**: `SDAP_BFF_API`

**Fields**:
- `sprk_name`: "SDAP_BFF_API"
- `sprk_baseurl`: "https://spe-api-dev-67e2xz.azurewebsites.net/api"
- `sprk_isenabled`: true
- `sprk_authtype`: 1 (ClientCredentials - app-only!)
- `sprk_tenantid`: "{tenant-id}"
- `sprk_clientid`: "{service-principal-client-id}"
- `sprk_clientsecret`: "{service-principal-client-secret}"
- `sprk_scope`: "https://spe-api-dev-67e2xz.azurewebsites.net/.default"
- `sprk_timeout`: 300
- `sprk_retrycount`: 3
- `sprk_retrydelay`: 1000

---

## 📝 Implementation Tasks (Step-by-Step)

### Phase 1: Backend Updates

#### Task 1.1: Verify/Create SpeFileStore Service
**Context**: Need abstraction for SPE operations
**Files**:
- Check: `src/api/Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs`
- Or: `src/shared/Spaarke.Dataverse/Services/SpeFileStore.cs`

**Requirements**:
- Method: `GetPreviewUrlAsync(driveId, itemId)`
- Uses app-only Graph client
- Returns: `{ PreviewUrl, ExpiresAt, ContentType }`

**Knowledge Needed**:
- Microsoft Graph SDK usage
- DriveItem preview endpoint
- App-only authentication pattern

---

#### Task 1.2: Update BFF Endpoint
**Context**: Change route and ensure app-only token usage
**File**: `src/api/Spe.Bff.Api/Api/FileAccessEndpoints.cs`

**Changes**:
1. Route: `/preview` → `/preview-url`
2. Add correlation ID support
3. Add userId extraction from JWT
4. Add UAC validation (TODO comment if not yet implemented)
5. Call `SpeFileStore.GetPreviewUrlAsync()`
6. Return unified error structure

**Knowledge Needed**:
- ASP.NET Core Minimal APIs
- JWT claims extraction
- Correlation ID patterns
- Error response structure

**Dependencies**:
- Task 1.1 (SpeFileStore)

---

#### Task 1.3: Update Plugin
**Context**: Simplify to thin proxy, rename for clarity
**File**: `src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/GetFilePreviewUrlPlugin.cs`

**Changes**:
1. Rename class from `GetDocumentFileUrlPlugin` to `GetFilePreviewUrlPlugin`
2. Change input parameter: `EndpointType` → `sprk_documentId`
3. Add `CorrelationId` to output parameters
4. Remove endpoint type logic (always call `/preview-url`)
5. Add correlation ID to HTTP headers

**Knowledge Needed**:
- Dataverse plugin patterns
- BaseProxyPlugin usage
- HttpClient header manipulation

**Dependencies**:
- Task 1.2 (BFF endpoint)

---

#### Task 1.4: Build and Test Plugin
**Context**: Ensure plugin compiles and works standalone
**Commands**:
```bash
cd c:/code_files/spaarke/src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy
dotnet build -c Release
```

**Validation**:
- Build succeeds (0 errors)
- DLL created in bin/Release/net462/

**Knowledge Needed**:
- .NET build process
- Plugin assembly requirements

---

### Phase 2: Custom API Registration

#### Task 2.1: Create External Service Config Record
**Context**: Plugin needs configuration to call BFF
**Tool**: Power Apps or PowerShell

**Manual Steps**:
1. Navigate to `sprk_externalserviceconfig` entity
2. Create new record with values from section 6 above

**PowerShell**:
```powershell
Import-Module Microsoft.Xrm.Data.PowerShell
$conn = Get-CrmConnection -InteractiveMode

$config = @{
    "sprk_name" = "SDAP_BFF_API"
    "sprk_baseurl" = "https://spe-api-dev-67e2xz.azurewebsites.net/api"
    "sprk_isenabled" = $true
    "sprk_authtype" = 1
    "sprk_tenantid" = "{tenant-id}"
    "sprk_clientid" = "{client-id}"
    "sprk_clientsecret" = "{client-secret}"
    "sprk_scope" = "https://spe-api-dev-67e2xz.azurewebsites.net/.default"
    "sprk_timeout" = 300
    "sprk_retrycount" = 3
    "sprk_retrydelay" = 1000
}

New-CrmRecord -conn $conn -EntityLogicalName "sprk_externalserviceconfig" -Fields $config
```

**Knowledge Needed**:
- Dataverse configuration entities
- Service principal credentials
- PowerShell CRM cmdlets

---

#### Task 2.2: Register Plugin Assembly
**Context**: Upload plugin DLL to Dataverse
**Tool**: Plugin Registration Tool

**Steps**:
1. Launch Plugin Registration Tool
2. Connect to SPAARKE DEV 1
3. Register → Register New Assembly
4. Select: `Spaarke.Dataverse.CustomApiProxy.dll`
5. Isolation: Sandbox
6. Location: Database
7. Register Selected Plugins

**Knowledge Needed**:
- Plugin Registration Tool usage
- Sandbox isolation
- Plugin assembly structure

---

#### Task 2.3: Create Custom API Record
**Context**: Define Custom API contract
**Tool**: Power Apps or PowerShell

**Manual Steps** (Advanced Settings):
1. Settings → Customizations → Customize System
2. Custom API → New
3. Fill fields from section 2 above

**PowerShell**:
```powershell
$customApi = @{
    "uniquename" = "sprk_GetFilePreviewUrl"
    "name" = "Get File Preview URL"
    "displayname" = "Get File Preview URL"
    "description" = "Server-side proxy for getting SPE file preview URLs"
    "bindingtype" = 1  # Entity
    "boundentitylogicalname" = "sprk_document"
    "isfunction" = $true
    "isprivate" = $false
    "allowedcustomprocessingsteptype" = 0
}

$customApiId = New-CrmRecord -conn $conn -EntityLogicalName "customapi" -Fields $customApi
Write-Host "Custom API ID: $customApiId"
```

**Knowledge Needed**:
- Custom API configuration
- Binding types
- Function vs Action

---

#### Task 2.4: Create Custom API Parameters
**Context**: Define input/output parameters
**Tool**: PowerShell (recommended for consistency)

**Input Parameter**:
```powershell
$inputParam = @{
    "uniquename" = "sprk_documentId"
    "name" = "sprk_documentId"
    "displayname" = "Document ID"
    "description" = "ID of the Document record"
    "type" = 0  # String
    "isoptional" = $false
    "customapiid@odata.bind" = "/customapis($customApiId)"
}

New-CrmRecord -conn $conn -EntityLogicalName "customapirequestparameter" -Fields $inputParam
```

**Output Parameters** (6 total):
- FileUrl (String)
- FileName (String)
- FileSize (Integer)
- ContentType (String)
- ExpiresAt (DateTime)
- CorrelationId (String)

**Knowledge Needed**:
- Custom API parameter types
- OData binding syntax
- Parameter collection

---

#### Task 2.5: Register Plugin Step
**Context**: Wire plugin to Custom API message
**Tool**: Plugin Registration Tool

**Steps**:
1. Expand registered assembly
2. Right-click `GetFilePreviewUrlPlugin` → Register New Step
3. Message: `sprk_GetFilePreviewUrl`
4. Primary Entity: `sprk_document`
5. Stage: Main Operation (30)
6. Mode: Synchronous
7. Register

**Knowledge Needed**:
- Plugin step registration
- Message names (match Custom API unique name)
- Pipeline stages

---

#### Task 2.6: Publish Customizations
**Context**: Make Custom API available
**Tool**: Power Apps or PowerShell

**Commands**:
```powershell
Publish-CrmAllCustomization -conn $conn
```

**Knowledge Needed**:
- Dataverse customization lifecycle
- Publishing requirements

---

### Phase 3: PCF Control Development

#### Task 3.1: Create PCF Project
**Context**: Initialize PCF control project
**Tool**: PAC CLI

**Commands**:
```bash
cd c:/code_files/spaarke/src/controls

pac pcf init \
    --namespace Spaarke \
    --name SpaarkeSpeFileViewer \
    --template field \
    --framework react \
    --outputDirectory ./SpaarkeSpeFileViewer

cd SpaarkeSpeFileViewer
npm install
```

**Knowledge Needed**:
- PAC CLI commands
- PCF project structure
- React template

---

#### Task 3.2: Install Dependencies
**Context**: Add Fluent UI v9 and types

**Commands**:
```bash
npm install @fluentui/react-components @fluentui/react-icons
npm install --save-dev @types/react @types/react-dom
```

**Knowledge Needed**:
- NPM package management
- TypeScript type definitions
- Fluent UI v9 packages

---

#### Task 3.3: Update Manifest
**Context**: Configure control binding and properties
**File**: `ControlManifest.Input.xml`

**Changes**:
- Bind to Document entity (sprk_document)
- Define property for documentId
- Set dimensions (height: 700px)

**Knowledge Needed**:
- PCF manifest schema
- Control binding
- Property definitions

---

#### Task 3.4: Implement Control Logic
**Context**: Create React components and services
**Files**:
- `index.ts` (entry point)
- `components/FileViewer.tsx` (main component)
- `services/CustomApiService.ts` (API wrapper)
- `types/index.ts` (TypeScript types)

**Implementation**: See section 5 above for complete code

**Knowledge Needed**:
- React hooks (useState, useEffect, useRef)
- TypeScript interfaces
- PCF context API
- Custom API execution pattern

---

#### Task 3.5: Build PCF Control
**Context**: Compile TypeScript and package control

**Commands**:
```bash
npm run build
```

**Validation**:
- Build succeeds (no errors)
- Output directory created: `out/controls/SpaarkeSpeFileViewer`

**Knowledge Needed**:
- PCF build process
- TypeScript compilation
- Bundle output structure

---

#### Task 3.6: Test PCF Control Locally
**Context**: Debug control in test harness

**Commands**:
```bash
npm start watch
```

**Validation**:
- Test harness opens in browser
- Control renders without errors
- Can call Custom API (may fail if not deployed yet)

**Knowledge Needed**:
- PCF test harness
- Browser debugging
- Mock data patterns

---

### Phase 4: Deployment and Integration

#### Task 4.1: Deploy SDAP BFF API
**Context**: Deploy updated BFF with new endpoint
**Tool**: Azure CLI

**Commands**:
```bash
cd c:/code_files/spaarke/src/api/Spe.Bff.Api

# Build for Release
dotnet publish -c Release -o ./publish

# Create deployment package
cd publish
tar -czf ../spe-bff-api.zip *
cd ..

# Deploy to Azure
az webapp deploy \
    --resource-group spaarke-dev-rg \
    --name spe-api-dev-67e2xz \
    --src-path spe-bff-api.zip \
    --type zip

# Verify deployment
curl https://spe-api-dev-67e2xz.azurewebsites.net/health
```

**Knowledge Needed**:
- ASP.NET Core publishing
- Azure App Service deployment
- Health check verification

---

#### Task 4.2: Package PCF Solution
**Context**: Create solution package for Dataverse

**Commands**:
```bash
cd c:/code_files/spaarke/src/controls/SpaarkeSpeFileViewer

pac solution init --publisher-name Spaarke --publisher-prefix sprk
pac solution add-reference --path .
msbuild /t:restore
msbuild /p:Configuration=Release

# Output: bin/Release/SpaarkeSpeFileViewer.zip
```

**Knowledge Needed**:
- PAC solution packaging
- MSBuild commands
- Solution publisher configuration

---

#### Task 4.3: Import PCF to Dataverse
**Context**: Upload PCF control to environment
**Tool**: Power Apps or PAC CLI

**Manual Steps**:
1. make.powerapps.com
2. Solutions → Import solution
3. Browse → Select SpaarkeSpeFileViewer.zip
4. Import
5. Publish

**PAC CLI**:
```bash
pac solution import --path ./bin/Release/SpaarkeSpeFileViewer.zip
```

**Knowledge Needed**:
- Solution import process
- Publishing requirements
- PCF control registration

---

#### Task 4.4: Add PCF to Document Form
**Context**: Embed control in form
**Tool**: Form Designer

**Steps**:
1. Open Document entity → Forms
2. Open main form in Form Designer
3. Add new section: "File Preview"
4. Add field or component
5. Select: SpaarkeSpeFileViewer
6. Configure: Bind to Document ID
7. Set height: 700px
8. Save and Publish

**Knowledge Needed**:
- Form Designer usage
- Control binding
- Form publishing

---

### Phase 5: Testing

#### Task 5.1: End-to-End Test
**Context**: Verify complete flow works

**Test Steps**:
1. Upload file via UniversalQuickCreate PCF
2. Verify Document record created
3. Open Document form
4. Verify PCF control loads
5. Verify file preview displays in iframe
6. Wait 8 minutes, verify auto-refresh
7. Click Download, verify file downloads
8. Click "Open in New Tab", verify opens

**Expected Results**:
- ✅ File preview loads within 2 seconds
- ✅ No authentication errors
- ✅ Auto-refresh works
- ✅ Download works
- ✅ New tab works

**Knowledge Needed**:
- End-to-end testing
- Browser debugging
- Log analysis

---

#### Task 5.2: Custom API Direct Test
**Context**: Validate Custom API works standalone

**Test Script** (Browser Console):
```javascript
const documentId = Xrm.Page.data.entity.getId().replace(/[{}]/g, '');

Xrm.WebApi.online.execute({
    getMetadata: function() {
        return {
            boundParameter: "entity",
            parameterTypes: {
                "entity": { typeName: "mscrm.sprk_document", structuralProperty: 5 },
                "sprk_documentId": { typeName: "Edm.String", structuralProperty: 1 }
            },
            operationType: 1,
            operationName: "sprk_GetFilePreviewUrl"
        };
    },
    entity: { entityType: "sprk_document", id: documentId },
    sprk_documentId: documentId
}).then(
    result => console.log("✅ Success:", result),
    error => console.error("❌ Error:", error)
);
```

**Expected Output**:
```javascript
✅ Success: {
  FileUrl: "https://...",
  FileName: "document.pdf",
  FileSize: 2621440,
  ContentType: "application/pdf",
  ExpiresAt: "2025-01-21T17:00:00Z",
  CorrelationId: "abc-123-def"
}
```

**Knowledge Needed**:
- Browser console usage
- Custom API execution syntax
- Response validation

---

#### Task 5.3: Audit Log Verification
**Context**: Verify logging works

**Query** (JavaScript):
```javascript
Xrm.WebApi.retrieveMultipleRecords(
    "sprk_proxyauditlog",
    "?$filter=sprk_operation eq 'GetFilePreviewUrl'&$orderby=sprk_executiontime desc&$top=10"
).then(result => console.log(result.entities));
```

**Expected**:
- Records created for each Custom API call
- Contains correlation IDs
- Duration < 500ms
- No sensitive data (URLs redacted)

**Knowledge Needed**:
- Dataverse audit logs
- WebAPI query syntax
- OData filtering

---

#### Task 5.4: Performance Test
**Context**: Validate performance targets

**Metrics**:
- Custom API execution: < 500ms
- BFF API response: < 300ms
- Graph API preview: < 200ms
- Total load time: < 2 seconds

**Tools**:
- Browser DevTools Network tab
- Application Insights (SDAP BFF)
- Plugin Trace Logs

**Knowledge Needed**:
- Performance profiling
- Network analysis
- Bottleneck identification

---

#### Task 5.5: Error Scenario Testing
**Context**: Test error handling

**Test Cases**:
1. Document with no file (missing SPE metadata)
2. Invalid document ID
3. User without access permissions
4. BFF API down
5. Graph API error
6. Expired preview URL

**Expected**:
- Graceful error messages
- Correlation IDs in errors
- No stack traces to user
- Proper HTTP status codes

**Knowledge Needed**:
- Error testing strategies
- Error message design
- Debugging techniques

---

## 📚 Knowledge References

### Architectural Documents
- [`TECHNICAL-SUMMARY-FILE-VIEWER-SOLUTION.md`](./TECHNICAL-SUMMARY-FILE-VIEWER-SOLUTION.md) - Original technical analysis
- [`GPT-DESIGN-FEEDBACK-FILE-VIEWER.md`](./GPT-DESIGN-FEEDBACK-FILE-VIEWER.md) - **AUTHORITATIVE** design guidance
- [`CUSTOM-API-FILE-ACCESS-SOLUTION.md`](./CUSTOM-API-FILE-ACCESS-SOLUTION.md) - Custom API approach

### Code References
- [`BaseProxyPlugin.cs`](./src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/BaseProxyPlugin.cs) - Plugin base infrastructure
- [`FileAccessEndpoints.cs`](./src/api/Spe.Bff.Api/Api/FileAccessEndpoints.cs) - Existing BFF endpoints
- [`SpeFileStoreDtos.cs`](./src/api/Spe.Bff.Api/Models/SpeFileStoreDtos.cs) - DTO definitions

### Microsoft Documentation
- [Custom API Overview](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/custom-api)
- [PCF Overview](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/overview)
- [Graph API: driveItem preview](https://learn.microsoft.com/en-us/graph/api/driveitem-preview)
- [SharePoint Embedded](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/)
- [Azure.Identity ClientSecretCredential](https://learn.microsoft.com/en-us/dotnet/api/azure.identity.clientsecretcredential)

### Spaarke ADRs
- **ADR-001**: Plugin usage (justified for Custom API proxy)
- **ADR-003**: Authorization patterns
- **ADR-005**: Flat SPE storage
- **ADR-006**: PCF over web resources (compliance critical)
- **ADR-007**: Single SPE facade
- **ADR-008**: Endpoint-level auth filters

---

## ✅ Definition of Done

### Phase 1 Complete When:
- [ ] SpeFileStore service exists with GetPreviewUrlAsync method
- [ ] BFF endpoint route is `/preview-url`
- [ ] BFF endpoint uses app-only Graph token
- [ ] Plugin renamed to GetFilePreviewUrlPlugin
- [ ] Plugin uses sprk_documentId input parameter
- [ ] Plugin adds CorrelationId to output
- [ ] Plugin builds successfully (0 errors)

### Phase 2 Complete When:
- [ ] External Service Config record created
- [ ] Plugin assembly registered in Dataverse
- [ ] Custom API record created (sprk_GetFilePreviewUrl)
- [ ] Custom API has 1 input + 6 output parameters
- [ ] Plugin step registered on Custom API message
- [ ] All customizations published
- [ ] Custom API testable via browser console

### Phase 3 Complete When:
- [ ] PCF project created with React template
- [ ] Fluent UI v9 dependencies installed
- [ ] FileViewer component implemented
- [ ] CustomApiService implemented
- [ ] PCF builds successfully (no errors)
- [ ] PCF tested in local test harness

### Phase 4 Complete When:
- [ ] SDAP BFF API deployed to Azure
- [ ] PCF solution package created
- [ ] PCF imported to Dataverse
- [ ] PCF added to Document form
- [ ] Document form published

### Phase 5 Complete When:
- [ ] End-to-end test passes
- [ ] Custom API direct test passes
- [ ] Audit logs contain entries
- [ ] Performance targets met (< 2s load time)
- [ ] Error scenarios handled gracefully
- [ ] No authentication errors in browser console

---

## 🎯 Success Criteria

### Functional
- ✅ Users can view files inline in Document form
- ✅ Preview loads within 2 seconds
- ✅ Auto-refresh works before URL expiration
- ✅ Download and "Open in New Tab" work
- ✅ No authentication popups or errors

### Non-Functional
- ✅ ADR-006 compliant (PCF, not web resource)
- ✅ ADR-001 compliant (justified plugin usage)
- ✅ App-only authentication (per GPT feedback)
- ✅ Spaarke UAC enforces access
- ✅ Correlation IDs in all logs
- ✅ No sensitive data in logs

### Operational
- ✅ Audit logs queryable
- ✅ Errors traceable via correlation ID
- ✅ Performance metrics in Application Insights
- ✅ Plugin traces available for debugging

---

**Document Version**: 2.0
**Last Updated**: 2025-01-21
**Status**: Ready for Step-by-Step Implementation
**Next Step**: Create focused step documents for each phase

