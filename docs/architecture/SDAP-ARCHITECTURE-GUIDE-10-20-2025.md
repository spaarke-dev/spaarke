# SDAP Architecture Guide
## SharePoint Document Access Platform

**Version:** 1.1.0 (Phase 8 - File Preview & Office Online Editor Integration)
**Last Updated:** November 26, 2025
**Status:** Production Ready
**Environment:** SPAARKE DEV 1 (Dataverse) + Azure WestUS2

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [System Architecture](#system-architecture)
3. [Component Architecture](#component-architecture)
4. [Data Flow Diagrams](#data-flow-diagrams)
5. [Azure Resources](#azure-resources)
6. [Dataverse Resources](#dataverse-resources)
7. [Authentication & Security](#authentication--security)
8. [Key Files & Components](#key-files--components)
9. [Setup & Configuration](#setup--configuration)
10. [Critical Issues & Resolutions](#critical-issues--resolutions)
11. [Deployment Procedures](#deployment-procedures)
12. [Monitoring & Troubleshooting](#monitoring--troubleshooting)
13. [Reference Documentation](#reference-documentation)

---

## Executive Summary

**SDAP** (SharePoint Document Access Platform) is an enterprise document management solution that integrates Microsoft Dataverse with SharePoint Embedded (SPE) for secure, scalable document storage, preview, and collaborative editing.

### Key Capabilities

- **Multi-Entity Document Upload:** Upload documents to any configured Dataverse entity (Matter, Project, Invoice, etc.)
- **SharePoint Embedded Storage:** Files stored in SPE containers, not Dataverse attachments
- **File Preview & Office Online Editor (Phase 8):** View and edit Office documents directly within Dataverse forms
- **Dynamic Metadata Discovery (Phase 7):** Automatically discovers correct navigation property names from Dataverse metadata
- **Unified Authentication:** Single sign-on using Microsoft Entra ID (Azure AD) with On-Behalf-Of token flow
- **Scalable Architecture:** BFF pattern with Redis caching, supports high concurrent usage
- **PCF Control Integration:** Native Dataverse UI with Fluent UI components

### Business Value

- **Storage Efficiency:** SPE eliminates Dataverse file size limits (up to 250GB per file)
- **Cost Optimization:** SharePoint Embedded is more cost-effective than Dataverse file storage
- **User Experience:** Seamless upload experience directly within Dataverse forms
- **Compliance:** Inherits SharePoint governance, retention, and security policies
- **Maintainability:** Phase 7 eliminates hardcoded configuration for new entities

---

## System Architecture

### High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         USER BROWSER                                    │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │  Dataverse Model-Driven App (https://spaarkedev1.crm.dynamics.com)│ │
│  │  ┌─────────────────────────────────────────────────────────────┐  │  │
│  │  │  Matter/Project/Invoice Form                               │  │  │
│  │  │  ┌──────────────────────────────────────────────────────┐  │  │  │
│  │  │  │  Documents Subgrid                                   │  │  │  │
│  │  │  │  ┌────────────────────────────────────────────────┐  │  │  │  │
│  │  │  │  │  Universal Quick Create PCF Control (v2.3.0)  │  │  │  │  │
│  │  │  │  │  - File Upload UI (Fluent UI)               │  │  │  │  │
│  │  │  │  │  - MSAL Authentication                      │  │  │  │  │
│  │  │  │  │  - NavMapClient (Phase 7)                   │  │  │  │  │
│  │  │  │  └────────────────────────────────────────────────┘  │  │  │  │
│  │  │  └──────────────────────────────────────────────────────┘  │  │  │
│  │  │                                                             │  │  │
│  │  │  ┌──────────────────────────────────────────────────────┐  │  │  │
│  │  │  │  Document Form (sprk_document)                       │  │  │  │
│  │  │  │  ┌────────────────────────────────────────────────┐  │  │  │  │
│  │  │  │  │  SpeFileViewer PCF Control (v1.0.6, Phase 8)  │  │  │  │  │
│  │  │  │  │  - File Preview (Office Online Viewer)      │  │  │  │  │
│  │  │  │  │  - Office Online Editor (docx/xlsx/pptx)    │  │  │  │  │
│  │  │  │  │  - MSAL Authentication                      │  │  │  │  │
│  │  │  │  │  - Responsive Height (600px min)            │  │  │  │  │
│  │  │  │  └────────────────────────────────────────────────┘  │  │  │  │
│  │  │  └──────────────────────────────────────────────────────┘  │  │  │
│  │  └─────────────────────────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ HTTPS (OAuth 2.0 Bearer Token)
                                    ↓
┌─────────────────────────────────────────────────────────────────────────┐
│                    AZURE WEB APP (WestUS2)                              │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │  SPE BFF API (spe-api-dev-67e2xz.azurewebsites.net)               │  │
│  │  ┌─────────────────────────────────────────────────────────────┐  │  │
│  │  │  API Endpoints (ASP.NET Core 8.0 Minimal APIs)             │  │  │
│  │  │  ├─ POST /upload/file           (File Upload)              │  │  │
│  │  │  ├─ POST /upload/session        (Large File Upload)        │  │  │
│  │  │  ├─ GET  /healthz                (Health Check)            │  │  │
│  │  │  ├─ GET  /api/navmap/{entity}/{relationship}/lookup        │  │  │
│  │  │  ├─ GET  /api/navmap/{entity}/{relationship}/collection    │  │  │
│  │  │  ├─ GET  /api/documents/{id}/preview-url (Preview Access)  │  │  │
│  │  │  └─ GET  /api/documents/{id}/office      (Editor Access)   │  │  │
│  │  └─────────────────────────────────────────────────────────────┘  │  │
│  │  ┌─────────────────────────────────────────────────────────────┐  │  │
│  │  │  Services Layer                                             │  │  │
│  │  │  ├─ GraphClientFactory (On-Behalf-Of Token Exchange)       │  │  │
│  │  │  ├─ UploadSessionManager (Large File Chunking)             │  │  │
│  │  │  ├─ IDataverseService (Metadata Queries)                   │  │  │
│  │  │  ├─ NavMapEndpoints (Phase 7 Metadata Discovery)           │  │  │
│  │  │  └─ FileAccessEndpoints (Phase 8 Preview & Editor)         │  │  │
│  │  └─────────────────────────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
            │                                          │
            │ OBO Token Exchange                       │ Client Secret Auth
            │ (User → Graph)                           │ (App → Dataverse)
            ↓                                          ↓
┌──────────────────────────┐           ┌─────────────────────────────────┐
│  Microsoft Graph API     │           │  Dataverse Web API               │
│  (graph.microsoft.com)   │           │  (spaarkedev1.api.crm.dynamics.com)│
│  ┌────────────────────┐  │           │  ┌───────────────────────────┐  │
│  │ SharePoint Embedded│  │           │  │ EntityDefinitions         │  │
│  │ - Drive/DriveItem  │  │           │  │ - OneToManyRelationship   │  │
│  │ - Upload Sessions  │  │           │  │ - Navigation Properties   │  │
│  │ - Container Mgmt   │  │           │  │                           │  │
│  └────────────────────┘  │           │  └───────────────────────────┘  │
└──────────────────────────┘           └─────────────────────────────────┘
            │                                          │
            │ Files Stored in SPE                      │ Metadata Only
            ↓                                          ↓
┌──────────────────────────┐           ┌─────────────────────────────────┐
│  SharePoint Embedded     │           │  Dataverse Database              │
│  Container Storage       │           │  ┌───────────────────────────┐  │
│  - Files/Folders         │           │  │ sprk_matter (Matter)      │  │
│  - Permissions           │           │  │ sprk_project (Project)    │  │
│  - Versioning            │           │  │ sprk_document (Document)  │  │
│  - Metadata              │           │  │   - sprk_graphitemid      │  │
└──────────────────────────┘           │  │   - sprk_graphdriveid     │  │
                                       │  │   - sprk_matter lookup    │  │
                                       │  └───────────────────────────┘  │
                                       └─────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│                    CACHING LAYER (Azure Redis)                          │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │  Distributed Cache (15-minute TTL)                                │  │
│  │  - Navigation Property Metadata                                   │  │
│  │  - Entity Relationship Mappings                                   │  │
│  │  - Graph Access Tokens (per user)                                 │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Component Architecture

### 1. PCF Control (Universal Quick Create)

**Technology:** TypeScript, React, Fluent UI v9, MSAL.js
**Version:** 2.3.0 (Phase 7)
**Location:** `src/controls/UniversalQuickCreate/`

#### Core Components

```typescript
UniversalQuickCreate (index.ts)
├─ DocumentUploadForm.tsx          // Main UI component
├─ FileList.tsx                    // File selection display
├─ UploadProgress.tsx              // Progress indicators
├─ ParentRecordInfo.tsx            // Context display
│
├─ Services
│  ├─ MsalAuthProvider.ts          // OAuth 2.0 authentication
│  ├─ SdapApiClient.ts             // BFF API HTTP client
│  ├─ NavMapClient.ts              // Phase 7 metadata discovery
│  ├─ FileUploadService.ts         // Single file upload
│  ├─ MultiFileUploadService.ts    // Batch upload orchestration
│  └─ DocumentRecordService.ts     // Dataverse record creation
│
└─ Config
   └─ EntityDocumentConfig.ts      // Entity-relationship mappings
```

#### Key Responsibilities

1. **File Selection:** HTML5 file input, drag-and-drop support
2. **Authentication:** MSAL.js for user token acquisition
3. **Upload Orchestration:** Parallel file uploads with progress tracking
4. **Metadata Discovery:** Query BFF API for navigation properties (Phase 7)
5. **Record Creation:** Create sprk_document records with correct @odata.bind
6. **Error Handling:** User-friendly error messages, retry logic

#### Configuration (EntityDocumentConfig.ts)

```typescript
export interface EntityDocumentConfig {
    entityName: string;              // e.g., "sprk_matter"
    lookupFieldName: string;         // e.g., "sprk_matter"
    relationshipSchemaName: string;  // e.g., "sprk_matter_document_1n"
    containerIdField: string;        // e.g., "sprk_containerid"
    displayNameField: string;        // e.g., "sprk_matternumber"
    entitySetName: string;           // e.g., "sprk_matters"
}
```

**Supported Entities:**
- `sprk_matter` (Matter)
- `sprk_project` (Project)
- `sprk_invoice` (Invoice)
- `account` (Account - if configured)
- `contact` (Contact - if configured)

---

### 1a. PCF Control (SpeFileViewer)

**Technology:** TypeScript, React, Fluent UI v8, MSAL.js
**Version:** 1.0.6 (Phase 8)
**Location:** `src/controls/SpeFileViewer/`

#### Core Components

```typescript
SpeFileViewer (index.ts)
├─ FilePreview.tsx                // Main React component
├─ BffClient.ts                   // BFF API HTTP client
│
├─ Types & Interfaces
│  ├─ FilePreviewProps            // Component props
│  ├─ FilePreviewState            // Component state
│  ├─ PreviewUrlResponse          // BFF API response
│  ├─ OfficeUrlResponse           // Office Online URL response
│  └─ DocumentInfo                // File metadata
│
└─ Styles
   └─ SpeFileViewer.css           // Fluent UI styles
```

#### Key Responsibilities

1. **File Preview:** Display SharePoint files in read-only mode using Office Online viewer
2. **Office Online Editor:** Open Office files (docx, xlsx, pptx) in edit mode
3. **Authentication:** MSAL.js integration for user token acquisition
4. **Permission Handling:** Read-only dialog for users without edit permissions
5. **Responsive Height:** Configurable minimum height with flexible expansion
6. **Error Recovery:** User-friendly error messages with retry functionality

#### Preview & Editor Workflow

**Preview Mode (All File Types):**
```typescript
1. Component receives documentId parameter from Dataverse field
2. Call BffClient.getPreviewUrl(documentId, accessToken, correlationId)
3. BFF returns SharePoint preview URL with access token
4. Render iframe with preview URL (Office Online viewer)
5. Display "Open in Editor" button for Office file types
```

**Editor Mode (Office Files Only):**
```typescript
1. User clicks "Open in Editor" button
2. Call BffClient.getOfficeUrl(documentId, accessToken, correlationId)
3. BFF returns Office Online editor URL with permissions
4. Switch iframe to editor URL
5. If permissions.canEdit = false, show read-only dialog
6. Display "Back to Preview" button to return to viewer
```

#### BFF API Integration

**BffClient.getPreviewUrl()**
```typescript
GET /api/documents/{documentId}/preview-url
Authorization: Bearer <user-access-token>
X-Correlation-Id: <correlation-id>

Response:
{
  "previewUrl": "https://...-my.sharepoint.com/_layouts/15/Doc.aspx?...",
  "documentInfo": {
    "name": "Contract_2025.docx",
    "fileExtension": "docx",
    "size": 45678
  }
}
```

**BffClient.getOfficeUrl()**
```typescript
GET /api/documents/{documentId}/office
Authorization: Bearer <user-access-token>
X-Correlation-Id: <correlation-id>

Response:
{
  "officeUrl": "https://...-my.sharepoint.com/_layouts/15/WopiFrame.aspx?...",
  "permissions": {
    "canEdit": true,
    "role": "write"
  },
  "documentInfo": {
    "name": "Contract_2025.docx",
    "fileExtension": "docx",
    "size": 45678
  }
}
```

#### PCF Configuration Properties

**Control Manifest (ControlManifest.Input.xml):**
```xml
<property name="documentId" display-name-key="DocumentId_Display_Key"
          of-type="SingleLine.Text" usage="bound" required="true" />

<property name="bffApiUrl" display-name-key="BffApiUrl_Display_Key"
          of-type="SingleLine.Text" usage="input" required="true" />

<property name="clientAppId" display-name-key="ClientAppId_Display_Key"
          of-type="SingleLine.Text" usage="input" required="true" />

<property name="bffAppId" display-name-key="BffAppId_Display_Key"
          of-type="SingleLine.Text" usage="input" required="true" />

<property name="tenantId" display-name-key="TenantId_Display_Key"
          of-type="SingleLine.Text" usage="input" required="true" />

<property name="controlHeight" display-name-key="ControlHeight_Display_Key"
          of-type="Whole.None" usage="input" default-value="600" />
```

**Form Designer Configuration:**
- **Document ID Field:** Bound to `sprk_documentid` text field on form
- **BFF API URL:** `https://spe-api-dev-67e2xz.azurewebsites.net`
- **Client App ID:** Entra ID app registration for PCF control
- **BFF App ID:** Entra ID app registration for BFF API
- **Tenant ID:** Azure AD tenant GUID
- **Control Height:** 600px (default), configurable via Form Designer

#### Responsive Height Feature (v1.0.5+)

**Implementation:**
```typescript
// index.ts - init() method
const controlHeight = context.parameters.controlHeight?.raw ?? 600;
this.container.style.minHeight = `${controlHeight}px`;
this.container.style.height = '100%';
this.container.style.display = 'flex';
this.container.style.flexDirection = 'column';
```

**Behavior:**
- **Minimum Height:** Control always at least configured pixel height (default 600px)
- **Expansion:** Control fills available vertical space beyond minimum
- **Flexibility:** Works across desktop, tablet, and mobile screen sizes

#### UI Layout (v1.0.6+)

**Structure:**
```html
<div class="spe-file-viewer">
  <div class="spe-file-viewer__actions">        <!-- Action header -->
    <button class="...--primary">Open in Editor</button>
    <button class="...--secondary">← Back to Preview</button>
  </div>
  <iframe class="spe-file-viewer__iframe" />    <!-- Preview/Editor iframe -->
</div>
```

**Action Buttons:**
- **Position:** Top-right header above iframe (not floating)
- **Size:** Compact (4px/12px padding, 13px font)
- **Alignment:** Right-aligned using flexbox
- **Visibility:**
  - Preview mode + Office file → "Open in Editor" button
  - Editor mode → "Back to Preview" button

#### Supported File Types

**Office Files (Preview + Editor):**
- Word: docx, doc, docm, dot, dotx, dotm
- Excel: xlsx, xls, xlsm, xlsb, xlt, xltx, xltm
- PowerPoint: pptx, ppt, pptm, pot, potx, potm, pps, ppsx, ppsm

**Other Files (Preview Only):**
- PDF, images (png, jpg, gif), text files, etc.

#### Error Handling

**Error States:**
- No document selected → Info message bar
- API call failure → Error message bar with retry button
- Permission denied → Read-only dialog in editor mode
- Network timeout → Error message with retry option

**Logging:**
```typescript
console.log('[SpeFileViewer] Initializing control...');
console.log('[SpeFileViewer] Loading preview for document: {documentId}');
console.log('[SpeFileViewer] Correlation ID: {correlationId}');
console.log('[SpeFileViewer] Preview loaded: {fileName}');
console.log('[SpeFileViewer] Editor opened | CanEdit: {canEdit} | Role: {role}');
console.warn('[SpeFileViewer] User has read-only access...');
console.error('[SpeFileViewer] Failed to load preview: {error}');
```

---

### 2. BFF API (Backend-for-Frontend)

**Technology:** ASP.NET Core 8.0, Minimal APIs
**Hosting:** Azure Web App (Linux, B1 tier)
**Location:** `src/api/Spe.Bff.Api/`

#### API Endpoints

```csharp
// File Upload Endpoints
POST /upload/file
  - Single file upload (< 4MB)
  - Accepts: multipart/form-data
  - Returns: DriveItem metadata

POST /upload/session
  - Large file upload session (> 4MB, up to 250GB)
  - Chunked upload with resume support
  - Returns: Session URL for chunked uploads

// Health & Diagnostics
GET /healthz
  - Returns: "Healthy" (200 OK)
  - Validates: Graph client, Dataverse client, Redis cache

// Navigation Property Metadata (Phase 7)
GET /api/navmap/{childEntity}/{relationshipSchemaName}/lookup
  - Query: OneToManyRelationship metadata
  - Returns: Navigation property name for lookup fields
  - Example: /api/navmap/sprk_document/sprk_matter_document_1n/lookup

GET /api/navmap/{childEntity}/{relationshipSchemaName}/collection
  - Query: Collection navigation properties
  - Returns: Navigation property name for related entity collections
  - Example: /api/navmap/sprk_matter/sprk_matter_document_1n/collection

// File Preview & Editor (Phase 8)
GET /api/documents/{id}/preview-url
  - Query: SharePoint file preview URL
  - Returns: Office Online viewer URL with access token
  - Authentication: On-Behalf-Of flow
  - Example: /api/documents/a1b2c3d4-e5f6-7890/preview-url

GET /api/documents/{id}/office
  - Query: Office Online editor URL
  - Returns: Office Online editor URL with edit permissions
  - Authentication: On-Behalf-Of flow
  - Example: /api/documents/a1b2c3d4-e5f6-7890/office
```

#### Core Services

**GraphClientFactory** (`Infrastructure/Graph/GraphClientFactory.cs`)
```csharp
// On-Behalf-Of (OBO) token exchange
// User token → Graph access token
public GraphServiceClient CreateClient(string userAccessToken)
{
    var oboToken = await GetOnBehalfOfTokenAsync(userAccessToken);
    return new GraphServiceClient(new DelegateAuthenticationProvider(...));
}
```

**UploadSessionManager** (`Infrastructure/Graph/UploadSessionManager.cs`)
```csharp
// Large file upload with chunking
public async Task<DriveItem> UploadLargeFileAsync(
    string driveId,
    string fileName,
    Stream fileStream,
    long fileSize)
{
    // Create upload session
    var session = await graphClient.CreateUploadSession(...);

    // Upload 10MB chunks
    var provider = new ChunkedUploadProvider(session, graphClient, fileStream);
    return await provider.UploadAsync();
}
```

**IDataverseService** (`Services/IDataverseService.cs`)
```csharp
// Dataverse metadata queries (Phase 7)
Task<OneToManyRelationshipMetadata> GetLookupNavigationPropertyAsync(
    string childEntity,
    string relationshipSchemaName);

Task<OneToManyRelationshipMetadata> GetCollectionNavigationPropertyAsync(
    string parentEntity,
    string relationshipSchemaName);
```

**DataverseServiceClientImpl** (`Spaarke.Dataverse/DataverseServiceClientImpl.cs`)
```csharp
// ServiceClient with connection string authentication
public DataverseServiceClientImpl(IConfiguration config, ILogger logger)
{
    var connectionString = $"AuthType=ClientSecret;Url={dataverseUrl};" +
                          $"ClientId={clientId};ClientSecret={clientSecret}";

    _serviceClient = new ServiceClient(connectionString);
}
```

**NavMapEndpoints** (`Api/NavMapEndpoints.cs`)
```csharp
// Phase 7: Dynamic metadata discovery endpoints
public static void MapNavMapEndpoints(this IEndpointRouteBuilder app)
{
    app.MapGet("/api/navmap/{childEntity}/{relationship}/lookup",
        async (string childEntity, string relationship,
               IDataverseService dataverse, IDistributedCache cache) =>
    {
        // Check cache first (15-min TTL)
        var cacheKey = $"navmap:lookup:{childEntity}:{relationship}";
        var cached = await cache.GetStringAsync(cacheKey);
        if (cached != null) return Results.Ok(JsonSerializer.Deserialize(cached));

        // Query Dataverse metadata
        var metadata = await dataverse.GetLookupNavigationPropertyAsync(
            childEntity, relationship);

        // Cache for 15 minutes
        await cache.SetStringAsync(cacheKey,
            JsonSerializer.Serialize(metadata),
            new DistributedCacheEntryOptions {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
            });

        return Results.Ok(metadata);
    });
}
```

**FileAccessEndpoints** (`Api/FileAccessEndpoints.cs`)
```csharp
// Phase 8: File preview and Office Online editor endpoints
public static void MapFileAccessEndpoints(this IEndpointRouteBuilder app)
{
    // Preview URL endpoint (read-only viewer)
    app.MapGet("/api/documents/{documentId}/preview-url",
        async (string documentId,
               HttpContext httpContext,
               IDataverseService dataverse,
               IGraphClientFactory graphFactory,
               ILogger<Program> logger) =>
    {
        var correlationId = httpContext.Request.Headers["X-Correlation-Id"].ToString();
        logger.LogInformation("[FileAccess] GET preview-url for {DocumentId} | Correlation: {CorrelationId}",
            documentId, correlationId);

        // 1. Retrieve sprk_document from Dataverse
        var document = await dataverse.GetDocumentAsync(documentId);
        if (document == null)
            return Results.NotFound(new { error = "Document not found" });

        // 2. Get user access token from Authorization header
        var authHeader = httpContext.Request.Headers["Authorization"].ToString();
        var userToken = authHeader.Replace("Bearer ", "");

        // 3. Create Graph client using On-Behalf-Of flow
        var graphClient = await graphFactory.CreateClientAsync(userToken);

        // 4. Get DriveItem from SharePoint
        var driveItem = await graphClient.Drives[document.GraphDriveId]
            .Items[document.GraphItemId]
            .GetAsync();

        // 5. Generate preview URL with nb=true (hide SharePoint header)
        var previewUrl = driveItem.WebUrl + "?web=1&action=embedview&nb=true";

        return Results.Ok(new
        {
            previewUrl = previewUrl,
            documentInfo = new
            {
                name = driveItem.Name,
                fileExtension = Path.GetExtension(driveItem.Name)?.TrimStart('.'),
                size = driveItem.Size ?? 0
            }
        });
    });

    // Office URL endpoint (editor mode with permissions)
    app.MapGet("/api/documents/{documentId}/office",
        async (string documentId,
               HttpContext httpContext,
               IDataverseService dataverse,
               IGraphClientFactory graphFactory,
               ILogger<Program> logger) =>
    {
        var correlationId = httpContext.Request.Headers["X-Correlation-Id"].ToString();
        logger.LogInformation("[FileAccess] GET office for {DocumentId} | Correlation: {CorrelationId}",
            documentId, correlationId);

        // 1. Retrieve sprk_document from Dataverse
        var document = await dataverse.GetDocumentAsync(documentId);
        if (document == null)
            return Results.NotFound(new { error = "Document not found" });

        // 2. Get user access token from Authorization header
        var authHeader = httpContext.Request.Headers["Authorization"].ToString();
        var userToken = authHeader.Replace("Bearer ", "");

        // 3. Create Graph client using On-Behalf-Of flow
        var graphClient = await graphFactory.CreateClientAsync(userToken);

        // 4. Get DriveItem with permissions
        var driveItem = await graphClient.Drives[document.GraphDriveId]
            .Items[document.GraphItemId]
            .GetAsync(config => config.QueryParameters.Expand = new[] { "permissions" });

        // 5. Get user's effective permissions
        var userPermissions = driveItem.Permissions?.FirstOrDefault(p =>
            p.GrantedToV2?.User?.Email == httpContext.User.Identity.Name);

        var canEdit = userPermissions?.Roles?.Contains("write") ?? false;

        // 6. Generate Office Online editor URL with nb=true
        var officeUrl = driveItem.WebUrl + "?web=1&action=edit&nb=true";

        return Results.Ok(new
        {
            officeUrl = officeUrl,
            permissions = new
            {
                canEdit = canEdit,
                role = canEdit ? "write" : "read"
            },
            documentInfo = new
            {
                name = driveItem.Name,
                fileExtension = Path.GetExtension(driveItem.Name)?.TrimStart('.'),
                size = driveItem.Size ?? 0
            }
        });
    });
}
```

**Key Features:**
- **On-Behalf-Of Authentication:** User token → Graph token exchange
- **Dataverse Integration:** Query sprk_document for GraphDriveId and GraphItemId
- **Permission Checking:** Validate user's write access for editor mode
- **SharePoint Header Removal:** `nb=true` parameter hides SharePoint navigation
- **Correlation ID Tracking:** Request tracing for troubleshooting

---

### 3. Dataverse Resources

#### Custom Tables

**sprk_document** (Document)
```
Primary Key: sprk_documentid (GUID)
Primary Name: sprk_documentname (Text, 200)

Fields:
├─ sprk_filename (Text, 500)           - Original file name
├─ sprk_filesize (Whole Number)        - File size in bytes
├─ sprk_graphitemid (Text, 200)        - SPE DriveItem ID
├─ sprk_graphdriveid (Text, 200)       - SPE Container Drive ID
├─ sprk_documentdescription (Multi-line) - Optional description
├─ sprk_matter (Lookup → sprk_matter)  - Matter relationship
├─ sprk_project (Lookup → sprk_project) - Project relationship
├─ sprk_invoice (Lookup → sprk_invoice) - Invoice relationship
└─ ... (additional lookup fields for other entities)

Relationships:
├─ sprk_matter_document_1n (1:N from sprk_matter)
├─ sprk_Project_Document_1n (1:N from sprk_project)
├─ sprk_invoice_document (1:N from sprk_invoice)
└─ ... (additional relationships)
```

**sprk_matter** (Matter)
```
Primary Key: sprk_matterid (GUID)
Primary Name: sprk_matternumber (Text, 100)

Fields:
├─ sprk_containerid (Text, 200)        - SPE Container Drive ID
├─ sprk_matternumber (Text, 100)      - Matter number
├─ sprk_mattername (Text, 500)        - Matter name
└─ ... (other matter fields)

Relationships:
└─ sprk_matter_document_1n → sprk_document
```

**sprk_project** (Project)
```
Primary Key: sprk_projectid (GUID)
Primary Name: sprk_projectname (Text, 100)

Fields:
├─ sprk_containerid (Text, 200)        - SPE Container Drive ID
├─ sprk_projectname (Text, 100)       - Project name
└─ ... (other project fields)

Relationships:
└─ sprk_Project_Document_1n → sprk_document
```

#### PCF Control Registration

**Control Name:** `Spaarke.Controls.UniversalDocumentUpload`
**Namespace:** `Spaarke.Controls`
**Version:** 2.3.0
**Bundle:** `bundle.js` (~8.76 MB - includes Fluent UI, React, MSAL)

**Control Properties:**
```xml
<property name="sdapApiBaseUrl"
          display-name-key="API Base URL"
          description-key="BFF API base URL (e.g., https://spe-api-dev.azurewebsites.net/api)"
          of-type="SingleLine.Text"
          usage="input"
          required="true" />
```

**Deployment:**
- Solution: `UniversalQuickCreateSolution` (Unmanaged)
- Publisher Prefix: `sprk`
- Deployment Method: `pac pcf push --publisher-prefix sprk`

---

## Data Flow Diagrams

### Flow 1: Document Upload (End-to-End)

```
User Action: Click "Universal Quick Create" → Select File → Click "Upload and Create"

┌─────────────────────────────────────────────────────────────────────────┐
│ STEP 1: USER AUTHENTICATION (MSAL.js)                                  │
└─────────────────────────────────────────────────────────────────────────┘
PCF Control (MsalAuthProvider)
  ├─ Check token cache for scope: api://{BFF_APP_ID}/user_impersonation
  ├─ If cached and valid → Use cached token
  └─ If missing/expired → Acquire token silently (ssoSilent)
      ├─ Discover account from browser session
      └─ Return access token (JWT, 90-min expiry)

Token Structure:
{
  "aud": "api://1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "iss": "https://login.microsoftonline.com/{tenant}/v2.0",
  "scp": "user_impersonation",
  "upn": "user@spaarke.com",
  "exp": 1729533600
}

┌─────────────────────────────────────────────────────────────────────────┐
│ STEP 2: FILE UPLOAD TO SPE (via BFF API)                               │
└─────────────────────────────────────────────────────────────────────────┘
PCF Control (FileUploadService)
  └─ POST https://spe-api-dev-67e2xz.azurewebsites.net/upload/file
      Headers:
        Authorization: Bearer {user_access_token}
        Content-Type: multipart/form-data
      Body:
        file: {binary_data}
        containerId: {parent_record_containerid}
        fileName: "document.pdf"

BFF API (UploadController)
  ├─ Validate JWT token (Microsoft.Identity.Web)
  ├─ Extract user claims (upn, oid, tid)
  └─ Call GraphClientFactory.CreateClient(userAccessToken)

GraphClientFactory
  ├─ Exchange user token for Graph token (OBO flow)
  │   POST https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token
  │     grant_type: urn:ietf:params:oauth2:grant-type:jwt-bearer
  │     client_id: {BFF_APP_ID}
  │     client_secret: {BFF_CLIENT_SECRET}
  │     assertion: {user_access_token}
  │     requested_token_use: on_behalf_of
  │     scope: https://graph.microsoft.com/.default
  │
  └─ Return GraphServiceClient with OBO token

Upload to SharePoint Embedded
  └─ If file < 4MB:
      POST https://graph.microsoft.com/v1.0/drives/{driveId}/root:/fileName:/content
        Authorization: Bearer {graph_obo_token}
        Content-Type: {mime_type}
        Body: {file_binary}

  └─ If file >= 4MB:
      1. Create upload session:
         POST /drives/{driveId}/root:/fileName:/createUploadSession

      2. Upload chunks (10MB each):
         PUT {uploadUrl}
           Content-Range: bytes 0-10485759/52428800
           Body: {chunk_1}

      3. Complete upload → Return DriveItem

Response to PCF:
{
  "id": "01LBYCMX5WQTZKWT2DTNF3DBZVRQEGYHBP",
  "name": "document.pdf",
  "size": 245760,
  "parentId": "01LBYCMX56Y2GOVW7725BZO354PWSELRRZ",
  "createdDateTime": "2025-10-20T17:53:03Z",
  "webUrl": "https://..."
}

┌─────────────────────────────────────────────────────────────────────────┐
│ STEP 3: QUERY NAVIGATION PROPERTY METADATA (Phase 7)                   │
└─────────────────────────────────────────────────────────────────────────┘
PCF Control (NavMapClient)
  └─ GET /api/navmap/sprk_document/sprk_matter_document_1n/lookup
      Authorization: Bearer {user_access_token}

BFF API (NavMapEndpoints)
  ├─ Check Redis cache: key = "navmap:lookup:sprk_document:sprk_matter_document_1n"
  ├─ If cache hit (< 15 min old) → Return cached metadata
  └─ If cache miss → Query Dataverse

IDataverseService.GetLookupNavigationPropertyAsync()
  ├─ Authenticate to Dataverse using ClientSecretCredential
  │   - Tenant ID, Client ID, Client Secret (from App Settings)
  │   - Scope: https://spaarkedev1.api.crm.dynamics.com/.default
  │
  ├─ Query EntityDefinitions via ServiceClient
  │   var relationship = _serviceClient.RetrieveMultiple(
  │       new QueryExpression("relationship")
  │       {
  │           Criteria = new FilterExpression
  │           {
  │               Conditions =
  │               {
  │                   new ConditionExpression("schemaname", ConditionOperator.Equal,
  │                                          "sprk_matter_document_1n")
  │               }
  │           }
  │       });
  │
  └─ Extract navigation property: ReferencingEntityNavigationPropertyName

Response from Dataverse:
{
  "ReferencingEntityNavigationPropertyName": "sprk_Matter",  // ← Capital M!
  "ReferencedEntity": "sprk_matter",
  "ReferencingEntity": "sprk_document",
  "SchemaName": "sprk_matter_document_1n"
}

Cache metadata in Redis (15-min TTL)
  └─ SET navmap:lookup:sprk_document:sprk_matter_document_1n
      Value: { navigationPropertyName: "sprk_Matter", source: "dataverse", ... }
      TTL: 900 seconds

Response to PCF:
{
  "childEntity": "sprk_document",
  "relationship": "sprk_matter_document_1n",
  "navigationPropertyName": "sprk_Matter",  // ← Correct case!
  "targetEntity": "sprk_matter",
  "source": "dataverse"  // or "cache" on subsequent calls
}

┌─────────────────────────────────────────────────────────────────────────┐
│ STEP 4: CREATE DATAVERSE DOCUMENT RECORD                               │
└─────────────────────────────────────────────────────────────────────────┘
PCF Control (DocumentRecordService)
  ├─ Build payload with discovered navigation property:
  │   {
  │     "sprk_documentname": "document.pdf",
  │     "sprk_filename": "document.pdf",
  │     "sprk_filesize": 245760,
  │     "sprk_graphitemid": "01LBYCMX5WQTZKWT2DTNF3DBZVRQEGYHBP",
  │     "sprk_graphdriveid": "b!yLRdWEOAdkaWXskuRfByI...",
  │     "sprk_Matter@odata.bind": "/sprk_matters(3a785f76-c773-f011-b4cb-6045bdd8b757)"
  │                    ^^^^^^ - Correct case from metadata discovery!
  │   }
  │
  └─ Create record using context.webAPI.createRecord()
      POST https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_documents
        Authorization: Bearer {dataverse_user_token}
        Content-Type: application/json
        Body: {payload}

Dataverse Response:
{
  "sprk_documentid": "ca5bbb9f-ddad-f011-bbd3-7c1e5217cd7c",
  "@odata.etag": "W/\"12345678\"",
  ...
}

┌─────────────────────────────────────────────────────────────────────────┐
│ STEP 5: UPDATE UI & NOTIFY USER                                        │
└─────────────────────────────────────────────────────────────────────────┘
PCF Control
  ├─ Update progress: 100% complete
  ├─ Display success message: "1 document uploaded successfully"
  ├─ Refresh subgrid to show new document
  └─ Close dialog

Total Time: ~2-5 seconds (depending on file size and network)
```

---

### Flow 2: Metadata Caching Strategy (Phase 7)

```
Request Timeline:

┌─────────────────────────────────────────────────────────────────────────┐
│ FIRST UPLOAD (Cache Miss)                                              │
└─────────────────────────────────────────────────────────────────────────┘
T=0s   PCF → GET /api/navmap/sprk_document/sprk_matter_document_1n/lookup

T=0.1s BFF → Check Redis cache → KEY NOT FOUND

T=0.2s BFF → Query Dataverse EntityDefinitions via ServiceClient
            └─ Authenticate with ClientSecretCredential
            └─ RetrieveMultiple("relationship") with filter
            └─ Extract ReferencingEntityNavigationPropertyName

T=2.5s Dataverse → Returns metadata: { navigationPropertyName: "sprk_Matter" }

T=2.6s BFF → Store in Redis:
            └─ Key: navmap:lookup:sprk_document:sprk_matter_document_1n
            └─ Value: { navigationPropertyName: "sprk_Matter", ... }
            └─ TTL: 900 seconds (15 minutes)

T=2.7s BFF → Response to PCF: { source: "dataverse", ... }

Total: ~2.7 seconds

┌─────────────────────────────────────────────────────────────────────────┐
│ SECOND UPLOAD (Cache Hit, within 15 minutes)                           │
└─────────────────────────────────────────────────────────────────────────┘
T=0s   PCF → GET /api/navmap/sprk_document/sprk_matter_document_1n/lookup

T=0.1s BFF → Check Redis cache → KEY FOUND!

T=0.2s BFF → Return cached value (no Dataverse query)

T=0.3s BFF → Response to PCF: { source: "cache", ... }

Total: ~0.3 seconds (88% faster!)

┌─────────────────────────────────────────────────────────────────────────┐
│ CACHE EXPIRATION (After 15 minutes)                                    │
└─────────────────────────────────────────────────────────────────────────┘
T=900s Redis → Auto-delete key (TTL expired)

Next upload → Cache miss → Repeat "FIRST UPLOAD" flow

Cache Benefits:
- 88% reduction in response time
- Reduced Dataverse API calls (cost savings)
- Lower load on Dataverse service
- Better user experience (faster uploads)
```

---

### Flow 3: File Preview & Office Online Editor (Phase 8)

```
User Action: Open sprk_document form → View file preview in SpeFileViewer PCF control

┌─────────────────────────────────────────────────────────────────────────┐
│ STEP 1: PREVIEW MODE INITIALIZATION                                    │
└─────────────────────────────────────────────────────────────────────────┘
SpeFileViewer PCF Control (index.ts)
  ├─ Component mounts on form
  ├─ Receive parameters from form context:
  │   ├─ documentId: "ca5bbb9f-ddad-f011-bbd3-7c1e5217cd7c" (bound to sprk_documentid)
  │   ├─ bffApiUrl: "https://spe-api-dev-67e2xz.azurewebsites.net"
  │   ├─ clientAppId: "{PCF_CLIENT_APP_ID}"
  │   ├─ bffAppId: "{BFF_APP_ID}"
  │   ├─ tenantId: "{TENANT_ID}"
  │   └─ controlHeight: 1200 (from form configuration)
  │
  ├─ Apply responsive height styling:
  │   container.style.minHeight = "1200px"
  │   container.style.height = "100%"
  │   container.style.display = "flex"
  │
  └─ Render React component: <FilePreview />

FilePreview Component (FilePreview.tsx)
  ├─ componentDidMount() triggered
  ├─ Acquire MSAL access token:
  │   └─ Scope: api://{BFF_APP_ID}/user_impersonation
  │   └─ Returns: Bearer token (JWT)
  │
  └─ Call: loadPreview()

┌─────────────────────────────────────────────────────────────────────────┐
│ STEP 2: GET PREVIEW URL FROM BFF                                       │
└─────────────────────────────────────────────────────────────────────────┘
BffClient.getPreviewUrl()
  └─ GET https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/{documentId}/preview-url
      Headers:
        Authorization: Bearer {user_access_token}
        X-Correlation-Id: {correlation_id}

BFF API (FileAccessEndpoints)
  ├─ Validate JWT token (Microsoft.Identity.Web)
  ├─ Extract documentId from route parameter
  │
  ├─ Query Dataverse for sprk_document:
  │   └─ IDataverseService.GetDocumentAsync(documentId)
  │   └─ Returns: { sprk_graphitemid, sprk_graphdriveid, sprk_filename, ... }
  │
  ├─ Exchange user token for Graph token (OBO flow):
  │   └─ GraphClientFactory.CreateClientAsync(userAccessToken)
  │
  ├─ Get DriveItem from SharePoint:
  │   └─ GET https://graph.microsoft.com/v1.0/drives/{driveId}/items/{itemId}
  │   └─ Returns: DriveItem with webUrl, name, size, etc.
  │
  └─ Build preview URL:
      └─ driveItem.WebUrl + "?web=1&action=embedview&nb=true"
      └─ nb=true parameter hides SharePoint header/navigation

Response to PCF:
{
  "previewUrl": "https://...-my.sharepoint.com/_layouts/15/Doc.aspx?sourcedoc={guid}&action=embedview&nb=true",
  "documentInfo": {
    "name": "Contract_2025.docx",
    "fileExtension": "docx",
    "size": 45678
  }
}

┌─────────────────────────────────────────────────────────────────────────┐
│ STEP 3: RENDER PREVIEW IFRAME                                          │
└─────────────────────────────────────────────────────────────────────────┘
FilePreview Component
  ├─ Update state:
  │   ├─ previewUrl: "https://...-my.sharepoint.com/..."
  │   ├─ documentInfo: { name, fileExtension, size }
  │   ├─ isLoading: false
  │   └─ mode: "preview"
  │
  └─ Render UI:
      ├─ Action header (right-aligned):
      │   └─ [Open in Editor] button (if Office file type)
      │
      └─ <iframe src={previewUrl} /> (full-frame below header)
          └─ Office Online viewer displays document in read-only mode

Total Time: ~1-2 seconds

┌─────────────────────────────────────────────────────────────────────────┐
│ STEP 4: SWITCH TO EDITOR MODE (User clicks "Open in Editor")          │
└─────────────────────────────────────────────────────────────────────────┘
User Action: Click "Open in Editor" button (only visible for Office files)

FilePreview.handleOpenEditor()
  ├─ Set loading state
  └─ Call: BffClient.getOfficeUrl()

BffClient.getOfficeUrl()
  └─ GET https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/{documentId}/office
      Headers:
        Authorization: Bearer {user_access_token}
        X-Correlation-Id: {correlation_id}

BFF API (FileAccessEndpoints)
  ├─ Validate JWT token
  ├─ Query Dataverse for sprk_document (same as preview flow)
  ├─ Exchange user token for Graph token (OBO flow)
  │
  ├─ Get DriveItem WITH permissions:
  │   └─ GET https://graph.microsoft.com/v1.0/drives/{driveId}/items/{itemId}?$expand=permissions
  │   └─ Returns: DriveItem + permissions array
  │
  ├─ Check user's effective permissions:
  │   └─ Find permission where grantedToV2.user.email == current user
  │   └─ Check if roles contains "write"
  │   └─ canEdit = (roles.contains("write"))
  │
  └─ Build Office Online editor URL:
      └─ driveItem.WebUrl + "?web=1&action=edit&nb=true"

Response to PCF:
{
  "officeUrl": "https://...-my.sharepoint.com/_layouts/15/WopiFrame.aspx?sourcedoc={guid}&action=edit&nb=true",
  "permissions": {
    "canEdit": true,    // or false if user has read-only access
    "role": "write"     // or "read"
  },
  "documentInfo": {
    "name": "Contract_2025.docx",
    "fileExtension": "docx",
    "size": 45678
  }
}

┌─────────────────────────────────────────────────────────────────────────┐
│ STEP 5: RENDER EDITOR IFRAME                                           │
└─────────────────────────────────────────────────────────────────────────┘
FilePreview Component
  ├─ Update state:
  │   ├─ officeUrl: "https://...-my.sharepoint.com/..."
  │   ├─ mode: "editor"
  │   ├─ isLoading: false
  │   └─ showReadOnlyDialog: !canEdit (if user lacks edit permissions)
  │
  └─ Render UI:
      ├─ Action header (right-aligned):
      │   └─ [← Back to Preview] button
      │
      ├─ <iframe src={officeUrl} /> (Office Online editor)
      │   └─ User can edit document directly in browser
      │   └─ Office Online enforces read-only if user lacks write permissions
      │
      └─ Read-Only Dialog (if showReadOnlyDialog = true):
          └─ Title: "File Opened in Read-Only Mode"
          └─ Message: "You have view-only access to this file..."
          └─ Button: [OK] (dismisses dialog)

Total Time: ~1-2 seconds

┌─────────────────────────────────────────────────────────────────────────┐
│ STEP 6: RETURN TO PREVIEW (User clicks "Back to Preview")             │
└─────────────────────────────────────────────────────────────────────────┘
User Action: Click "← Back to Preview" button

FilePreview.handleBackToPreview()
  ├─ Update state:
  │   ├─ mode: "preview"
  │   └─ showReadOnlyDialog: false
  │
  └─ Render UI:
      ├─ Switch iframe src back to previewUrl
      └─ Show "Open in Editor" button again

Total Time: Instant (client-side state change)

┌─────────────────────────────────────────────────────────────────────────┐
│ SUPPORTED FILE TYPES                                                   │
└─────────────────────────────────────────────────────────────────────────┘
Office Files (Preview + Editor):
├─ Word: docx, doc, docm, dot, dotx, dotm
├─ Excel: xlsx, xls, xlsm, xlsb, xlt, xltx, xltm
└─ PowerPoint: pptx, ppt, pptm, pot, potx, potm, pps, ppsx, ppsm

Other Files (Preview Only):
├─ PDF: Rendered in Office Online PDF viewer
├─ Images: png, jpg, gif (browser image viewer)
└─ Text: txt, md (browser text viewer)
```

---

## Azure Resources

### Resource Group: `spe-infrastructure-westus2`

**Location:** West US 2
**Subscription:** (Your Azure subscription)

#### 1. Web App (BFF API)

```
Resource Name: spe-api-dev-67e2xz
Type: App Service (Linux)
Runtime: .NET 8.0
Pricing Tier: Basic B1 (1 Core, 1.75 GB RAM)
URL: https://spe-api-dev-67e2xz.azurewebsites.net

App Settings (Configuration):
├─ TENANT_ID: a221a95e-6abc-4434-aecc-e48338a1b2f2
├─ API_APP_ID: 1e40baad-e065-4aea-a8d4-4b7ab273458c
├─ API_CLIENT_SECRET: (KeyVault reference)
├─ Dataverse__ServiceUrl: @Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/SPRK-DEV-DATAVERSE-URL)
├─ Dataverse__ClientSecret: @Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/BFF-API-ClientSecret)
├─ Redis__ConnectionString: (Redis connection string)
└─ ASPNETCORE_ENVIRONMENT: Production

Deployment:
├─ Method: ZIP deployment via Azure CLI
├─ Command: az webapp deploy --name spe-api-dev-67e2xz --src-path deployment.zip --type zip
└─ Source: src/api/Spe.Bff.Api/publish/

Health Endpoint: /healthz
- Returns: "Healthy" (200 OK)
- Validates: Graph client, Dataverse client, Redis cache
```

#### 2. Key Vault (spaarke-spekvcert)

```
Resource Name: spaarke-spekvcert
Type: Key Vault
Location: West US 2

Secrets:
├─ SPRK-DEV-DATAVERSE-URL
│   Value: https://spaarkedev1.api.crm.dynamics.com
│   Used By: BFF API (Dataverse connection)
│
├─ BFF-API-ClientSecret
│   Value: (Client secret for App ID 1e40baad-e065-4aea-a8d4-4b7ab273458c)
│   Used By: BFF API (Azure AD authentication)
│
└─ (Other secrets for SPE, Graph, etc.)

Access Policies:
└─ BFF API Web App (Managed Identity)
    Permissions: Get, List (Secrets)
```

#### 3. Redis Cache (Optional - if not using Azure Redis)

```
If using Azure Cache for Redis:
├─ Resource Name: (your Redis cache name)
├─ Pricing Tier: Basic C0 (250 MB)
├─ Connection String: (stored in Web App config)
└─ Used For: Distributed caching (metadata, tokens)

Configuration:
├─ TTL for metadata: 15 minutes (900 seconds)
├─ TTL for tokens: 89 minutes (matches Graph token expiry)
└─ Eviction Policy: allkeys-lru (least recently used)
```

---

## Dataverse Resources

### Environment: SPAARKE DEV 1

```
Environment Name: SPAARKE DEV 1
URL: https://spaarkedev1.crm.dynamics.com
Region: North America
Type: Sandbox
Dataverse Database: Enabled
```

### Tables

**Custom Tables:**
- `sprk_document` (Document) - 1 primary, 10+ relationships
- `sprk_matter` (Matter) - Has sprk_containerid field
- `sprk_project` (Project) - Has sprk_containerid field
- `sprk_invoice` (Invoice) - Has sprk_containerid field

**Standard Tables (if configured):**
- `account` (Account) - Add sprk_containerid field
- `contact` (Contact) - Add sprk_containerid field

### Relationships (1:N)

```
sprk_matter → sprk_document
├─ Relationship Name: sprk_matter_document_1n
├─ Lookup Field: sprk_matter (on sprk_document)
└─ Navigation Property: sprk_Matter (capital M!)

sprk_project → sprk_document
├─ Relationship Name: sprk_Project_Document_1n
├─ Lookup Field: sprk_project (on sprk_document)
└─ Navigation Property: sprk_Project (capital P!)

(Similar for other entities...)
```

### Forms Configuration

**Matter Main Form:**
```
Sections:
└─ Documents
    ├─ Type: Subgrid
    ├─ Table: sprk_document
    ├─ Relationship: sprk_matter_document_1n
    ├─ Default View: Active Documents
    └─ Command Bar: Universal Quick Create button (ribbon customization)
```

**Universal Quick Create Button (Ribbon):**
```xml
<CommandDefinition Id="sprk.uploadcontext.grid.CustomButton">
  <EnableRules>
    <EnableRule Id="sprk.uploadcontext.grid.EnableRule" />
  </EnableRules>
  <DisplayRules>
    <DisplayRule Id="sprk.uploadcontext.grid.DisplayRule" />
  </DisplayRules>
  <Actions>
    <JavaScriptFunction FunctionName="openQuickCreate" Library="sprk_uploadcontext.js">
      <CrmParameter Value="SelectedControl" />
      <CrmParameter Value="SelectedControlSelectedItemIds" />
      <CrmParameter Value="PrimaryControl" />
    </JavaScriptFunction>
  </Actions>
</CommandDefinition>
```

### PCF Control Configuration

**Control Binding:**
```xml
<control id="Spaarke.Controls.UniversalDocumentUpload"
         namespace="Spaarke.Controls"
         constructor="UniversalDocumentUpload"
         version="2.3.0">
  <property name="sdapApiBaseUrl"
            usage="bound"
            of-type="SingleLine.Text"
            default-value="https://spe-api-dev-67e2xz.azurewebsites.net/api" />
</control>
```

---

## Authentication & Security

### Authentication Flow Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│ 1. USER AUTHENTICATION (Browser)                                       │
└─────────────────────────────────────────────────────────────────────────┘
User opens Dataverse form
  └─ Already authenticated to Dataverse (Entra ID SSO)
  └─ Session cookies + bearer token for crm.dynamics.com

┌─────────────────────────────────────────────────────────────────────────┐
│ 2. PCF CONTROL AUTHENTICATION (MSAL.js)                                │
└─────────────────────────────────────────────────────────────────────────┘
PCF Control loads
  └─ Initialize MSAL PublicClientApplication
      Config:
        clientId: "5175798e-f23e-41c3-b09b-7a90b9218189"  // PCF App Registration
        authority: "https://login.microsoftonline.com/{tenant}"
        redirectUri: "https://spaarkedev1.crm.dynamics.com"

User clicks "Upload and Create"
  └─ MsalAuthProvider.getToken(["api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation"])
      ├─ Check token cache (localStorage)
      ├─ If missing/expired:
      │   └─ acquireTokenSilent({ scopes: [...], account: {...} })
      │       ├─ Uses hidden iframe (ssoSilent)
      │       ├─ Discovers account from browser session
      │       └─ Returns token without user interaction
      └─ Return JWT token

Token Claims:
{
  "aud": "api://1e40baad-e065-4aea-a8d4-4b7ab273458c",  // BFF API App ID
  "iss": "https://login.microsoftonline.com/{tenant}/v2.0",
  "scp": "user_impersonation",  // Scope
  "upn": "user@spaarke.com",
  "oid": "user-object-id",
  "tid": "{tenant-id}",
  "exp": 1729533600,
  "nbf": 1729527200
}

┌─────────────────────────────────────────────────────────────────────────┐
│ 3. BFF API AUTHENTICATION (JWT Validation)                             │
└─────────────────────────────────────────────────────────────────────────┘
PCF → POST /upload/file
  Headers:
    Authorization: Bearer {user_access_token}

BFF API (Microsoft.Identity.Web middleware)
  ├─ Validate JWT signature (using Azure AD public keys)
  ├─ Validate issuer: https://login.microsoftonline.com/{tenant}/v2.0
  ├─ Validate audience: api://1e40baad-e065-4aea-a8d4-4b7ab273458c
  ├─ Validate expiration (exp claim)
  └─ Extract user claims (upn, oid, scp)

If valid → Proceed to GraphClientFactory
If invalid → Return 401 Unauthorized

┌─────────────────────────────────────────────────────────────────────────┐
│ 4. ON-BEHALF-OF (OBO) TOKEN EXCHANGE                                   │
└─────────────────────────────────────────────────────────────────────────┘
GraphClientFactory.CreateClient(userAccessToken)
  └─ POST https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token
      Content-Type: application/x-www-form-urlencoded
      Body:
        grant_type: urn:ietf:params:oauth2:grant-type:jwt-bearer
        client_id: 1e40baad-e065-4aea-a8d4-4b7ab273458c
        client_secret: {BFF_CLIENT_SECRET}
        assertion: {user_access_token}  // Original user token
        requested_token_use: on_behalf_of
        scope: https://graph.microsoft.com/.default

Azure AD Response:
{
  "token_type": "Bearer",
  "expires_in": 5399,  // 89 minutes
  "access_token": "{graph_obo_token}",  // New token for Graph API
  "refresh_token": null  // OBO tokens don't include refresh tokens
}

Graph Token Claims:
{
  "aud": "https://graph.microsoft.com",
  "scp": "Files.ReadWrite.All Sites.ReadWrite.All",  // Delegated permissions
  "upn": "user@spaarke.com",  // Same user as original token
  "azpacr": "1"  // Client credentials used
}

┌─────────────────────────────────────────────────────────────────────────┐
│ 5. GRAPH API CALL (SharePoint Embedded)                                │
└─────────────────────────────────────────────────────────────────────────┘
GraphServiceClient → POST /drives/{driveId}/root:/file.pdf:/content
  Headers:
    Authorization: Bearer {graph_obo_token}
    Content-Type: application/pdf

SharePoint Embedded
  ├─ Validate token (signature, expiration, audience)
  ├─ Check user permissions on container
  │   └─ User inherits permissions from container ownership
  └─ Store file in container

┌─────────────────────────────────────────────────────────────────────────┐
│ 6. DATAVERSE AUTHENTICATION (BFF → Dataverse)                          │
└─────────────────────────────────────────────────────────────────────────┘
DataverseServiceClientImpl (Server-to-Server, NO user context)
  └─ Connection String Authentication:
      AuthType=ClientSecret
      Url=https://spaarkedev1.api.crm.dynamics.com
      ClientId=1e40baad-e065-4aea-a8d4-4b7ab273458c
      ClientSecret={BFF_CLIENT_SECRET}

ServiceClient internally:
  ├─ POST https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token
  │     grant_type: client_credentials
  │     client_id: 1e40baad-e065-4aea-a8d4-4b7ab273458c
  │     client_secret: {BFF_CLIENT_SECRET}
  │     scope: https://spaarkedev1.api.crm.dynamics.com/.default
  │
  └─ Azure AD returns app-only token (NO user context)

Dataverse Token Claims:
{
  "aud": "https://spaarkedev1.api.crm.dynamics.com",
  "iss": "https://login.microsoftonline.com/{tenant}/v2.0",
  "appid": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
  "roles": ["user_impersonation"],  // App role, not user scope
  "oid": "{service-principal-object-id}"
}

Dataverse Validation:
  ├─ Validate token signature and claims
  ├─ Lookup Application User by appid (1e40baad-e065-4aea-a8d4-4b7ab273458c)
  ├─ Check security roles: System Administrator
  └─ Grant access to EntityDefinitions, Relationships
```

---

### Azure AD App Registrations

#### 1. PCF App Registration (User-facing)

```
App Name: SDAP PCF Control (or similar)
Application ID: 5175798e-f23e-41c3-b09b-7a90b9218189
Purpose: User authentication in PCF control (MSAL.js)

Authentication:
├─ Platform: Single-page application (SPA)
├─ Redirect URIs:
│   ├─ https://spaarkedev1.crm.dynamics.com
│   └─ http://localhost:8181 (dev only)
└─ Implicit grant: ID tokens, Access tokens (legacy, can disable with MSAL 2.0)

API Permissions:
├─ Microsoft Graph (Delegated)
│   ├─ User.Read (Sign in and read user profile)
│   └─ offline_access (Maintain access to data)
├─ BFF API (Delegated)
│   └─ api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation
└─ Admin consent: Required and granted

Expose an API:
└─ Not applicable (this app consumes APIs, doesn't expose them)
```

#### 2. BFF API App Registration (Backend)

```
App Name: spe-bff-api
Application ID: 1e40baad-e065-4aea-a8d4-4b7ab273458c
Purpose: BFF API authentication, OBO token exchange, Dataverse access

Authentication:
├─ Platform: Web
├─ Redirect URIs: (none - server-to-server only)
└─ Client Secret: (stored in Key Vault)
    Name: BFF-API-ClientSecret
    Value: CBi8Q~v52JqvSMeKb2lIn~8mSjvNQZRu5yIvrcEy
    Expires: (check expiration, rotate before expiry!)

API Permissions:
├─ Microsoft Graph (Delegated)
│   ├─ Files.ReadWrite.All (Read and write files in all site collections)
│   ├─ Sites.ReadWrite.All (Read and write items in all site collections)
│   └─ User.Read (Sign in and read user profile)
│
├─ Microsoft Graph (Application)
│   ├─ Files.ReadWrite.All (NOT USED - delegated only)
│   └─ Sites.ReadWrite.All (NOT USED - delegated only)
│
├─ Dynamics CRM (Delegated)
│   └─ user_impersonation (Access Dynamics 365 as organization users)
│
└─ Admin consent: Required and granted for ALL

Expose an API:
├─ Application ID URI: api://1e40baad-e065-4aea-a8d4-4b7ab273458c
└─ Scopes:
    └─ user_impersonation
        Display name: Access SDAP BFF API
        Description: Allows the app to access the SDAP BFF API on behalf of the signed-in user
        Who can consent: Admins and users
        State: Enabled

Certificates & Secrets:
└─ Client Secret: BFF-API-ClientSecret
    Created: (creation date)
    Expires: (expiration date - MONITOR THIS!)
    Value: (never shown again after creation - stored in Key Vault)
```

---

### Dataverse Application User

**CRITICAL:** The BFF API must be registered as an Application User in Dataverse for ServiceClient authentication to work.

```
Application User Configuration:

User Details:
├─ Application ID: 1e40baad-e065-4aea-a8d4-4b7ab273458c (BFF API App Registration)
├─ Display Name: SPE BFF API Service (or similar)
├─ Business Unit: (default organization business unit)
└─ Status: Enabled

Security Roles:
└─ System Administrator
    Purpose: Required for EntityDefinitions and Relationship metadata queries
    Can be restricted in production to custom role with specific permissions:
      ├─ Read privilege on EntityDefinitions
      ├─ Read privilege on Relationships
      └─ No user data access required

Setup Method (Power Platform Admin Center):
1. Go to https://admin.powerplatform.microsoft.com/
2. Environments → SPAARKE DEV 1 → Settings
3. Users + permissions → Application users
4. + New app user
5. Add app: 1e40baad-e065-4aea-a8d4-4b7ab273458c
6. Select business unit
7. Create
8. Edit → Assign "System Administrator" security role → Save

Verification:
$ pac admin list-service-principals --environment https://spaarkedev1.crm.dynamics.com
(Should show entry with ApplicationId: 1e40baad-e065-4aea-a8d4-4b7ab273458c)
```

---

## Key Files & Components

### Repository Structure

```
c:\code_files\spaarke\
├─ src\
│  ├─ api\
│  │  └─ Spe.Bff.Api\                      // BFF API (.NET 8.0)
│  │      ├─ Program.cs                    // Main entry point, DI configuration
│  │      ├─ Api\
│  │      │   ├─ UploadController.cs       // File upload endpoints
│  │      │   └─ NavMapEndpoints.cs        // Phase 7 metadata discovery
│  │      ├─ Infrastructure\
│  │      │   └─ Graph\
│  │      │       ├─ GraphClientFactory.cs  // OBO token exchange
│  │      │       ├─ UploadSessionManager.cs // Large file uploads
│  │      │       └─ DriveItemOperations.cs  // SPE operations
│  │      ├─ Services\
│  │      │   └─ IDataverseService.cs      // Dataverse metadata interface
│  │      └─ appsettings.json              // Configuration (KeyVault refs)
│  │
│  ├─ controls\
│  │  └─ UniversalQuickCreate\             // PCF Control (TypeScript + React)
│  │      ├─ UniversalQuickCreate\
│  │      │   ├─ index.ts                  // Main PCF component
│  │      │   ├─ components\
│  │      │   │   ├─ DocumentUploadForm.tsx // Upload UI
│  │      │   │   ├─ FileList.tsx          // File selection
│  │      │   │   └─ UploadProgress.tsx    // Progress indicators
│  │      │   ├─ services\
│  │      │   │   ├─ MsalAuthProvider.ts   // MSAL.js authentication
│  │      │   │   ├─ SdapApiClient.ts      // BFF API client
│  │      │   │   ├─ NavMapClient.ts       // Phase 7 metadata client
│  │      │   │   ├─ FileUploadService.ts  // Upload orchestration
│  │      │   │   └─ DocumentRecordService.ts // Record creation
│  │      │   ├─ config\
│  │      │   │   └─ EntityDocumentConfig.ts // Entity mappings
│  │      │   └─ ControlManifest.Input.xml // PCF manifest
│  │      ├─ package.json                  // npm dependencies
│  │      └─ tsconfig.json                 // TypeScript config
│  │
│  └─ shared\
│      └─ Spaarke.Dataverse\               // Dataverse client library
│          ├─ DataverseServiceClientImpl.cs // ServiceClient wrapper
│          └─ IDataverseService.cs         // Interface definition
│
├─ docs\
│  ├─ SDAP-ARCHITECTURE-GUIDE.md           // This file!
│  ├─ HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md     // Entity configuration guide
│  ├─ PHASE-7-ADD-DATAVERSE-PERMISSION.md  // Azure AD setup
│  └─ PHASE-7-CREATE-BFF-APP-USER.md       // Dataverse App User setup
│
└─ scripts\
   ├─ Deploy-PCFWebResources.ps1           // PCF deployment script
   └─ Test-SdapBffApi.ps1                  // API testing script
```

---

### Critical Files Deep Dive

#### 1. EntityDocumentConfig.ts (PCF Configuration)

**Location:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityDocumentConfig.ts`

**Purpose:** Central configuration for all entity-document relationships

**Key Exports:**
```typescript
export interface EntityDocumentConfig {
    entityName: string;              // Entity logical name
    lookupFieldName: string;         // Lookup field on sprk_document
    relationshipSchemaName: string;  // Dataverse relationship name (CRITICAL!)
    containerIdField: string;        // Container ID field on parent entity
    displayNameField: string;        // Primary name field
    entitySetName: string;           // OData entity set name (plural)
}

export const ENTITY_DOCUMENT_CONFIGS: Record<string, EntityDocumentConfig> = {
    'sprk_matter': {
        entityName: 'sprk_matter',
        lookupFieldName: 'sprk_matter',
        relationshipSchemaName: 'sprk_matter_document_1n',  // Must match Dataverse!
        containerIdField: 'sprk_containerid',
        displayNameField: 'sprk_matternumber',
        entitySetName: 'sprk_matters'
    },
    // ... other entities
};

export function getEntityDocumentConfig(entityName: string): EntityDocumentConfig | null;
export function isEntitySupported(entityName: string): boolean;
export function getSupportedEntities(): string[];
```

**Usage:**
```typescript
// In DocumentRecordService.ts
const config = getEntityDocumentConfig(parentContext.parentEntityName);
if (!config) {
    throw new Error(`Unsupported entity type: ${parentContext.parentEntityName}`);
}

// Query metadata using relationship name
const navMetadata = await this.navMapClient.getLookupNavigation(
    'sprk_document',
    config.relationshipSchemaName  // e.g., "sprk_matter_document_1n"
);
```

**Modification Instructions:**
- To add new entity: Add entry to `ENTITY_DOCUMENT_CONFIGS`
- To rename relationship: Update `relationshipSchemaName` (ONLY field that needs change!)
- Build and deploy: `npm run build && pac pcf push --publisher-prefix sprk`

---

#### 2. NavMapEndpoints.cs (Phase 7 Metadata Discovery)

**Location:** `src/api/Spe.Bff.Api/Api/NavMapEndpoints.cs`

**Purpose:** REST API endpoints for querying Dataverse relationship metadata

**Key Methods:**
```csharp
public static void MapNavMapEndpoints(this IEndpointRouteBuilder app)
{
    // Lookup navigation property query
    app.MapGet("/api/navmap/{childEntity}/{relationship}/lookup",
        [Authorize]
        async (string childEntity, string relationship,
               IDataverseService dataverseService,
               IDistributedCache cache,
               ILogger<Program> logger) =>
    {
        var cacheKey = $"navmap:lookup:{childEntity}:{relationship}";

        // Check cache first
        var cachedJson = await cache.GetStringAsync(cacheKey);
        if (cachedJson != null)
        {
            logger.LogInformation("NavMap cache hit: {CacheKey}", cacheKey);
            return Results.Ok(JsonSerializer.Deserialize<object>(cachedJson));
        }

        // Query Dataverse
        var metadata = await dataverseService.GetLookupNavigationPropertyAsync(
            childEntity, relationship);

        // Cache for 15 minutes
        await cache.SetStringAsync(cacheKey,
            JsonSerializer.Serialize(metadata),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
            });

        return Results.Ok(metadata);
    });

    // Collection navigation property query (similar structure)
    app.MapGet("/api/navmap/{parentEntity}/{relationship}/collection", ...);
}
```

**Response Format:**
```json
{
  "childEntity": "sprk_document",
  "relationship": "sprk_matter_document_1n",
  "logicalName": "sprk_matter",
  "schemaName": "sprk_Matter",
  "navigationPropertyName": "sprk_Matter",
  "targetEntity": "sprk_matter",
  "source": "dataverse"  // or "cache" if from Redis
}
```

---

#### 3. DataverseServiceClientImpl.cs (Dataverse Authentication)

**Location:** `src/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs`

**Purpose:** ServiceClient wrapper with connection string authentication

**Key Code:**
```csharp
public DataverseServiceClientImpl(
    IConfiguration configuration,
    ILogger<DataverseServiceClientImpl> logger)
{
    _logger = logger;

    var dataverseUrl = configuration["Dataverse:ServiceUrl"];
    var tenantId = configuration["TENANT_ID"];
    var clientId = configuration["API_APP_ID"];
    var clientSecret = configuration["API_CLIENT_SECRET"];

    // Connection string method (Microsoft recommended)
    var connectionString = $"AuthType=ClientSecret;" +
                          $"Url={dataverseUrl};" +
                          $"ClientId={clientId};" +
                          $"ClientSecret={clientSecret}";

    _serviceClient = new ServiceClient(connectionString);

    if (!_serviceClient.IsReady)
    {
        var error = _serviceClient.LastError ?? "Unknown error";
        throw new InvalidOperationException($"Failed to connect to Dataverse: {error}");
    }
}

public async Task<OneToManyRelationshipMetadata> GetLookupNavigationPropertyAsync(
    string childEntity,
    string relationshipSchemaName)
{
    var query = new QueryExpression("relationship")
    {
        ColumnSet = new ColumnSet("referencingentitynavigationpropertyname",
                                 "referencedentity",
                                 "referencingentity"),
        Criteria = new FilterExpression
        {
            Conditions =
            {
                new ConditionExpression("schemaname", ConditionOperator.Equal,
                                       relationshipSchemaName),
                new ConditionExpression("referencingentity", ConditionOperator.Equal,
                                       childEntity)
            }
        }
    };

    var results = await Task.Run(() => _serviceClient.RetrieveMultiple(query));

    if (results.Entities.Count == 0)
    {
        throw new InvalidOperationException(
            $"Relationship not found: {relationshipSchemaName} (child: {childEntity})");
    }

    var relationship = results.Entities[0];

    return new OneToManyRelationshipMetadata
    {
        NavigationPropertyName = relationship.GetAttributeValue<string>(
            "referencingentitynavigationpropertyname"),
        ReferencedEntity = relationship.GetAttributeValue<string>("referencedentity"),
        ReferencingEntity = relationship.GetAttributeValue<string>("referencingentity"),
        SchemaName = relationshipSchemaName
    };
}
```

**Authentication Flow:**
1. ServiceClient uses connection string
2. Internally calls Azure AD token endpoint:
   ```
   POST https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token
   grant_type: client_credentials
   client_id: {API_APP_ID}
   client_secret: {API_CLIENT_SECRET}
   scope: https://spaarkedev1.api.crm.dynamics.com/.default
   ```
3. Azure AD validates credentials and returns access token
4. ServiceClient uses token for all Dataverse API calls

---

#### 4. MsalAuthProvider.ts (PCF Authentication)

**Location:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/services/auth/MsalAuthProvider.ts`

**Purpose:** MSAL.js wrapper for user authentication in PCF control

**Key Code:**
```typescript
export class MsalAuthProvider {
    private msalInstance: PublicClientApplication;
    private tokenCache: Map<string, CachedToken> = new Map();

    constructor(config: Configuration) {
        this.msalInstance = new PublicClientApplication({
            auth: {
                clientId: config.auth.clientId,  // PCF App Registration
                authority: config.auth.authority,
                redirectUri: config.auth.redirectUri
            },
            cache: {
                cacheLocation: 'localStorage',
                storeAuthStateInCookie: false
            }
        });
    }

    async getToken(scopes: string[]): Promise<string> {
        const scopeKey = scopes.join(' ');

        // Check in-memory cache
        const cached = this.tokenCache.get(scopeKey);
        if (cached && cached.expiresOn > Date.now()) {
            return cached.token;
        }

        // Acquire token silently
        const account = this.msalInstance.getAllAccounts()[0];

        if (!account) {
            throw new Error('No authenticated account found');
        }

        const result = await this.msalInstance.acquireTokenSilent({
            scopes,
            account
        });

        // Cache token
        this.tokenCache.set(scopeKey, {
            token: result.accessToken,
            expiresOn: result.expiresOn!.getTime()
        });

        return result.accessToken;
    }
}
```

**Usage in PCF:**
```typescript
// Initialize auth provider
const authProvider = new MsalAuthProvider(msalConfig);

// Get token for BFF API
const token = await authProvider.getToken([
    'api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation'
]);

// Use token in API calls
const response = await fetch(apiUrl, {
    headers: {
        'Authorization': `Bearer ${token}`
    }
});
```

---

## Setup & Configuration

### Prerequisites

**Azure Resources:**
- Azure subscription with Owner or Contributor role
- Azure Key Vault access
- Azure Web App (Linux, .NET 8.0)
- Azure Redis Cache (optional but recommended)

**Dataverse:**
- System Administrator or System Customizer role
- Power Platform maker license
- Solution import permissions

**Development Tools:**
- Node.js 18+ (for PCF development)
- .NET SDK 8.0 (for BFF API)
- Power Platform CLI (pac CLI)
- Azure CLI
- Git

---

### Step-by-Step Setup

#### Phase 1: Azure AD App Registrations

**1.1 Create PCF App Registration**
```bash
# Azure Portal → Microsoft Entra ID → App registrations → New registration
Name: SDAP PCF Control
Supported account types: Single tenant
Redirect URI:
  - Type: Single-page application (SPA)
  - URI: https://spaarkedev1.crm.dynamics.com

# After creation, note the Application (client) ID
PCF_APP_ID=5175798e-f23e-41c3-b09b-7a90b9218189
```

**1.2 Create BFF API App Registration**
```bash
# Azure Portal → Microsoft Entra ID → App registrations → New registration
Name: spe-bff-api
Supported account types: Single tenant
Redirect URI: (leave blank for now)

# After creation, note the Application (client) ID
BFF_APP_ID=1e40baad-e065-4aea-a8d4-4b7ab273458c

# Create client secret
# App registration → Certificates & secrets → New client secret
Description: BFF-API-ClientSecret
Expires: 24 months
# COPY THE SECRET VALUE IMMEDIATELY (only shown once)
BFF_CLIENT_SECRET=CBi8Q~v52JqvSMeKb2lIn~8mSjvNQZRu5yIvrcEy

# Expose an API
App registration → Expose an API → Set Application ID URI
URI: api://1e40baad-e065-4aea-a8d4-4b7ab273458c

# Add scope
Add a scope:
  Scope name: user_impersonation
  Who can consent: Admins and users
  Admin consent display name: Access SDAP BFF API
  Admin consent description: Allows the app to access the SDAP BFF API on behalf of the signed-in user
  User consent display name: Access SDAP BFF API
  User consent description: Allows the app to access the SDAP BFF API on your behalf
  State: Enabled

# Add API permissions
API permissions → Add a permission:
  Microsoft Graph (Delegated):
    - Files.ReadWrite.All
    - Sites.ReadWrite.All
    - User.Read

  Dynamics CRM (Delegated):
    - user_impersonation

# Grant admin consent
API permissions → Grant admin consent for [Tenant]
```

**1.3 Configure PCF App Registration Permissions**
```bash
# PCF App Registration → API permissions → Add a permission
Custom APIs → (search for BFF_APP_ID)
Select: api://1e40baad-e065-4aea-a8d4-4b7ab273458c
Delegated permissions:
  - user_impersonation

Grant admin consent for [Tenant]
```

---

#### Phase 2: Azure Key Vault Setup

```bash
# Store secrets in Key Vault
az keyvault secret set \
  --vault-name spaarke-spekvcert \
  --name SPRK-DEV-DATAVERSE-URL \
  --value "https://spaarkedev1.api.crm.dynamics.com"

az keyvault secret set \
  --vault-name spaarke-spekvcert \
  --name BFF-API-ClientSecret \
  --value "CBi8Q~v52JqvSMeKb2lIn~8mSjvNQZRu5yIvrcEy"

# Grant Web App access to Key Vault
# Azure Portal → Key Vault → Access policies → Add access policy
Permissions: Get, List (Secrets only)
Select principal: spe-api-dev-67e2xz (Web App managed identity)
Save
```

---

#### Phase 3: Azure Web App Configuration

```bash
# Create Web App (if not exists)
az webapp create \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --plan {app_service_plan_name} \
  --runtime "DOTNETCORE:8.0"

# Configure app settings
az webapp config appsettings set \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --settings \
    TENANT_ID="a221a95e-6abc-4434-aecc-e48338a1b2f2" \
    API_APP_ID="1e40baad-e065-4aea-a8d4-4b7ab273458c" \
    API_CLIENT_SECRET="@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/BFF-API-ClientSecret)" \
    Dataverse__ServiceUrl="@Microsoft.KeyVault(SecretUri=https://spaarke-spekvcert.vault.azure.net/secrets/SPRK-DEV-DATAVERSE-URL)" \
    Redis__ConnectionString="{redis_connection_string}" \
    ASPNETCORE_ENVIRONMENT="Production"

# Enable managed identity (if not already enabled)
az webapp identity assign \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2
```

---

#### Phase 4: Dataverse Application User Setup

```bash
# Option 1: Power Platform Admin Center (GUI - EASIEST)
1. Go to https://admin.powerplatform.microsoft.com/
2. Environments → SPAARKE DEV 1 → Settings
3. Users + permissions → Application users
4. + New app user
5. Add app: 1e40baad-e065-4aea-a8d4-4b7ab273458c (BFF API)
6. Select business unit
7. Create
8. Edit → Assign "System Administrator" security role → Save

# Option 2: PowerShell
Install-Module Microsoft.PowerApps.Administration.PowerShell -Force
Add-PowerAppsAccount

$environmentId = "YOUR_ENVIRONMENT_ID"
$appId = "1e40baad-e065-4aea-a8d4-4b7ab273458c"

New-PowerAppManagementApp `
  -EnvironmentName $environmentId `
  -ApplicationId $appId `
  -DisplayName "SPE BFF API Service"

# Assign security role via Power Platform Admin Center (step 8 above)

# Verification
pac admin list-service-principals --environment https://spaarkedev1.crm.dynamics.com
# Should show entry with ApplicationId: 1e40baad-e065-4aea-a8d4-4b7ab273458c
```

---

#### Phase 5: Dataverse Table & Relationship Setup

**5.1 Create sprk_document Table**
```bash
# Power Apps Maker Portal → Tables → New table
Display name: Document
Plural name: Documents
Schema name: sprk_document
Primary column: sprk_documentname (Text, 200)

# Add columns:
Column name: sprk_filename (Text, 500)
Column name: sprk_filesize (Whole Number)
Column name: sprk_graphitemid (Text, 200)
Column name: sprk_graphdriveid (Text, 200)
Column name: sprk_documentdescription (Multiple lines of text)

Save and publish
```

**5.2 Add Container ID to Parent Entities**
```bash
# For each entity (sprk_matter, sprk_project, etc.)
Tables → {entity_name} → Columns → New column
Display name: Container ID
Schema name: sprk_containerid (lowercase!)
Data type: Text
Max length: 200
Required: No

Save and publish
```

**5.3 Create Relationships**
```bash
# For each entity (example: sprk_matter)
Tables → sprk_matter → Relationships → New relationship → One-to-many
Related table: sprk_document
Lookup column:
  Display name: Matter
  Schema name: sprk_Matter (capital M!)
Relationship name: sprk_matter_document_1n

Save and publish
```

**5.4 Add Documents Subgrid to Forms**
```bash
# For each entity form
Tables → {entity_name} → Forms → Main form (Information)
Add section: "Documents"
Add subgrid:
  Label: Documents
  Table: Document (sprk_document)
  Default view: Active Documents
  Relationship: {relationship_name} (e.g., sprk_matter_document_1n)

Save and publish
```

---

#### Phase 6: PCF Control Deployment

```bash
# 6.1 Clone repository and install dependencies
cd /c/code_files/spaarke/src/controls/UniversalQuickCreate
npm install

# 6.2 Update EntityDocumentConfig.ts
# Add/verify entity configurations in:
# UniversalQuickCreate/config/EntityDocumentConfig.ts

# 6.3 Build PCF control
npm run build

# 6.4 Authenticate to Dataverse
pac auth create --url https://spaarkedev1.crm.dynamics.com

# 6.5 Deploy to Dataverse
pac pcf push --publisher-prefix sprk

# 6.6 Verify deployment
# Power Apps → Solutions → Default Solution
# Custom controls → Spaarke.Controls.UniversalDocumentUpload (v2.3.0)
```

---

#### Phase 7: BFF API Deployment

```bash
# 7.1 Build BFF API
cd /c/code_files/spaarke/src/api/Spe.Bff.Api
dotnet clean --configuration Release
dotnet publish --configuration Release --output ./publish

# 7.2 Create deployment package
powershell -Command "Compress-Archive -Path publish\* -DestinationPath deployment.zip -Force"

# 7.3 Deploy to Azure Web App
az webapp deploy \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --src-path deployment.zip \
  --type zip

# 7.4 Restart Web App
az webapp restart \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz

# 7.5 Verify deployment
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz
# Expected: "Healthy" (200 OK)
```

---

## Critical Issues & Resolutions

### Issue 1: AADSTS500011 - Resource Principal Not Found

**Symptom:**
```
AADSTS500011: The resource principal named https://spaarkedev1.api.crm.dynamics.com/...
was not found in the tenant
```

**Root Cause:**
- BFF API App Registration not registered as Application User in Dataverse
- OR: Dynamics CRM API permission not granted to BFF API

**Resolution Steps:**

**1. Verify Dynamics CRM API Permission**
```bash
# Azure Portal → App registrations → spe-bff-api → API permissions
# Check for: Dynamics CRM (user_impersonation) with Admin consent granted

# If missing, add permission:
API permissions → Add a permission → Dynamics CRM → Delegated permissions
Select: user_impersonation
Grant admin consent for [Tenant]
```

**2. Verify Application User in Dataverse**
```bash
# Power Platform Admin Center
Environments → SPAARKE DEV 1 → Settings
Users + permissions → Application users
Look for: Application ID = 1e40baad-e065-4aea-a8d4-4b7ab273458c

# If missing, create (see Phase 4 setup above)
```

**3. Restart Web App and Test**
```bash
az webapp restart --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz

# Wait 30 seconds for restart
sleep 30

# Test NavMap endpoint
curl "https://spe-api-dev-67e2xz.azurewebsites.net/api/navmap/sprk_document/sprk_matter_document_1n/lookup"
# Expected: 401 (no auth token) or 200 (with valid token)
# Should NOT be 500 with AADSTS500011 error
```

**Files Modified (during troubleshooting):**
- `src/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` - Changed from ManagedIdentityCredential to connection string authentication

---

### Issue 2: OAuth Scope Error - Friendly Name vs Application ID URI

**Symptom:**
```
AADSTS500011: The resource principal named api://spe-bff-api was not found
```

**Root Cause:**
PCF control using friendly name `api://spe-bff-api/user_impersonation` instead of full Application ID URI

**Resolution:**
```typescript
// WRONG (index.ts, UniversalDocumentUploadPCF.ts)
const token = await this.authProvider.getToken(['api://spe-bff-api/user_impersonation']);

// CORRECT
const token = await this.authProvider.getToken([
    'api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation'
]);
```

**Files Modified:**
- `src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts` (line 253)
- `src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalDocumentUploadPCF.ts` (line 253)

**Commit:** `a4196a1`

---

### Issue 3: 404 Not Found - Double /api Path

**Symptom:**
```
GET https://spe-api-dev-67e2xz.azurewebsites.net/api/api/navmap/... 404 (Not Found)
```

**Root Cause:**
NavMapClient received baseUrl with `/api` suffix, then added `/api/navmap` internally

**Resolution:**
```typescript
// In index.ts and UniversalDocumentUploadPCF.ts
private initializeServices(context: ComponentFramework.Context<IInputs>): void {
    const rawApiUrl = context.parameters.sdapApiBaseUrl?.raw ||
                     'spe-api-dev-67e2xz.azurewebsites.net/api';

    const apiBaseUrl = rawApiUrl.startsWith('http://') || rawApiUrl.startsWith('https://')
        ? rawApiUrl
        : `https://${rawApiUrl}`;

    // NavMapClient needs base URL without /api suffix
    const navMapBaseUrl = apiBaseUrl.endsWith('/api')
        ? apiBaseUrl.substring(0, apiBaseUrl.length - 4)  // Remove trailing /api
        : apiBaseUrl;

    const navMapClient = new NavMapClient(navMapBaseUrl, tokenProvider);
}
```

**Files Modified:**
- `src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts` (lines 237-256)
- `src/controls/UniversalQuickCreate/UniversalQuickCreate/UniversalDocumentUploadPCF.ts` (lines 237-256)

**Commit:** `f4654ae`

---

### Issue 4: Relationship Not Found - Incorrect Schema Name

**Symptom:**
```
[NavMapClient] Failed to get lookup navigation
Error: Metadata not found. The entity or relationship may not exist in Dataverse.
```

**Root Cause:**
PCF configuration used assumed relationship name (e.g., `sprk_project_document`) but actual Dataverse relationship had different name (e.g., `sprk_Project_Document_1n`)

**Resolution:**
```typescript
// In EntityDocumentConfig.ts
'sprk_project': {
    entityName: 'sprk_project',
    lookupFieldName: 'sprk_project',
    relationshipSchemaName: 'sprk_Project_Document_1n',  // ← EXACT Dataverse name
    containerIdField: 'sprk_containerid',
    displayNameField: 'sprk_projectname',
    entitySetName: 'sprk_projects'
},
```

**How to Find Correct Relationship Name:**
```bash
# Power Apps Maker Portal
Tables → {parent_entity} → Relationships → Find relationship to sprk_document
Click relationship → Note "Relationship name" field exactly
```

**Files Modified:**
- `src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityDocumentConfig.ts` (line 82)

**Commit:** Included in final Phase 7 commit

---

### Issue 5: ManagedIdentityCredential Authentication Failed

**Symptom:**
```
ManagedIdentityCredential authentication failed: No User Assigned or Delegated Managed Identity found
```

**Root Cause:**
Attempted to use ManagedIdentityCredential for Dataverse authentication, but:
- Managed Identity not properly configured with Dataverse
- Token acquisition failed at Azure level

**Resolution:**
Switched to **connection string authentication** (Microsoft's recommended approach):

```csharp
// BEFORE (DataverseServiceClientImpl.cs) - FAILED
var credential = new ManagedIdentityCredential(managedIdentityClientId);
_serviceClient = new ServiceClient(
    instanceUrl: new Uri(dataverseUrl),
    tokenProviderFunction: async (uri) => {
        var token = await credential.GetTokenAsync(...);
        return token.Token;
    }
);

// AFTER - SUCCESS
var connectionString = $"AuthType=ClientSecret;Url={dataverseUrl};" +
                      $"ClientId={clientId};ClientSecret={clientSecret}";
_serviceClient = new ServiceClient(connectionString);
```

**Why This Works:**
- Connection string method is Microsoft's standard for ServiceClient
- Uses same ClientSecretCredential pattern as Graph/SPE
- Simpler configuration, more reliable
- Only requires Application User registration in Dataverse

**Files Modified:**
- `src/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` (lines 23-63)

**Commit:** `f650391` (Phase 7 implementation)

---

## Deployment Procedures

### PCF Control Deployment

**Method 1: pac pcf push (Recommended)**
```bash
cd /c/code_files/spaarke/src/controls/UniversalQuickCreate

# Build
npm run build

# Authenticate to Dataverse (if not already)
pac auth create --url https://spaarkedev1.crm.dynamics.com

# Deploy
pac pcf push --publisher-prefix sprk

# Verification
# Power Apps → Solutions → Default Solution
# Custom controls → Spaarke.Controls.UniversalDocumentUpload
# Version should be 2.3.0 (or latest)
```

**Method 2: Solution Import (Alternative)**
```bash
# Build solution package
cd /c/code_files/spaarke/src/controls/UniversalQuickCreate
msbuild /t:Rebuild /p:Configuration=Release

# Solution package created at:
# bin/Release/UniversalQuickCreateSolution.zip

# Import via Power Apps
# Solutions → Import → Select file → Next → Import
# Wait for import to complete
# Publish all customizations
```

**Rollback Procedure:**
```bash
# If deployment fails or breaks functionality
# Option 1: Revert code changes and redeploy
git checkout HEAD~1 -- UniversalQuickCreate/
npm run build
pac pcf push --publisher-prefix sprk

# Option 2: Delete and reimport previous solution version
# Power Apps → Solutions → Select solution → Delete
# Import previous solution backup
```

---

### BFF API Deployment

**Standard Deployment (Azure Web App)**
```bash
cd /c/code_files/spaarke/src/api/Spe.Bff.Api

# Clean and publish
dotnet clean --configuration Release
dotnet publish --configuration Release --output ./publish

# Create deployment package
powershell -Command "Compress-Archive -Path publish\* -DestinationPath deployment.zip -Force"

# Deploy to Azure
az webapp deploy \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --src-path deployment.zip \
  --type zip

# Wait for deployment (usually 30-60 seconds)
# Check deployment status
az webapp deployment list \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --query "[0].{Status:status, Time:endTime}"

# Restart Web App (clears caches, loads new assemblies)
az webapp restart \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz

# Verify deployment
sleep 30
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz
# Expected: "Healthy" (200 OK)
```

**Health Check Validation:**
```bash
# Detailed health check
curl -i https://spe-api-dev-67e2xz.azurewebsites.net/healthz

# Expected response:
HTTP/1.1 200 OK
Content-Type: text/plain; charset=utf-8
Date: Mon, 20 Oct 2025 17:44:04 GMT
Content-Length: 7

Healthy

# If unhealthy, check logs:
az webapp log tail \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz
```

**Rollback Procedure:**
```bash
# Option 1: Redeploy previous version
git checkout {previous_commit_sha}
cd src/api/Spe.Bff.Api
dotnet publish --configuration Release --output ./publish
# ... (repeat deployment steps above)

# Option 2: Use Azure Web App deployment slots (Production setup)
# Configure staging slot
# Deploy to staging
# Test
# Swap slots (instant rollback capability)
```

---

## Monitoring & Troubleshooting

### Monitoring Endpoints

**1. BFF API Health Check**
```bash
# Basic health
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz

# Monitor continuously (every 30 seconds)
while true; do
  echo "$(date): $(curl -s -o /dev/null -w '%{http_code}' https://spe-api-dev-67e2xz.azurewebsites.net/healthz)"
  sleep 30
done
```

**2. Application Insights (if configured)**
```bash
# Azure Portal → Application Insights → {your_app_insights}
# Live Metrics: Real-time requests, failures, performance
# Failures: Exception traces, failed requests
# Performance: Response times, dependency calls
```

**3. Web App Logs**
```bash
# Enable logging (if not enabled)
az webapp log config \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --application-logging filesystem \
  --level verbose

# Stream logs
az webapp log tail \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2

# Filter for errors only
az webapp log tail \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --filter Error
```

---

### Common Troubleshooting Scenarios

#### Scenario 1: Upload Fails with 401 Unauthorized

**Symptoms:**
- PCF control shows "Upload failed" error
- Browser console: `POST /upload/file 401 (Unauthorized)`

**Diagnosis:**
```javascript
// Check browser console for:
[MsalAuthProvider] Token acquired successfully ✅
// vs
[MsalAuthProvider] Failed to acquire token: {...}
```

**Possible Causes & Fixes:**

**Cause 1: Token expired**
```javascript
// User's token expired (90-min lifetime)
// Fix: Refresh page or re-authenticate
// PCF will auto-acquire new token via ssoSilent
```

**Cause 2: Incorrect scope**
```typescript
// Check token scope in MsalAuthProvider
const token = await this.authProvider.getToken([
    'api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation'  // ← Must match exactly
]);
```

**Cause 3: BFF API app registration misconfigured**
```bash
# Verify API permissions in Azure Portal
App registrations → spe-bff-api → Expose an API
Application ID URI: api://1e40baad-e065-4aea-a8d4-4b7ab273458c ✓
Scopes: user_impersonation (Enabled) ✓
```

---

#### Scenario 2: NavMap API Returns 500 Error

**Symptoms:**
- Phase 7 metadata query fails
- Browser console: `GET /api/navmap/... 500 (Internal Server Error)`

**Diagnosis:**
```bash
# Check BFF API logs
az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2

# Look for:
AADSTS500011: The resource principal named https://spaarkedev1.api.crm.dynamics.com...
# OR
Microsoft.PowerPlatform.Dataverse.Client.Utils.DataverseConnectionException: Failed to connect
```

**Possible Causes & Fixes:**

**Cause 1: Application User not registered in Dataverse**
```bash
# Verify Application User exists
# Power Platform Admin Center → Environments → SPAARKE DEV 1
# Users + permissions → Application users
# Look for: 1e40baad-e065-4aea-a8d4-4b7ab273458c

# If missing, create (see "Critical Issues" section)
```

**Cause 2: Application User missing security role**
```bash
# Edit Application User
# Security roles → Should include "System Administrator" (or custom role with metadata read)
# If missing, assign role and save
```

**Cause 3: Relationship doesn't exist in Dataverse**
```bash
# Verify relationship
# Power Apps → Tables → {entity} → Relationships
# Check for relationship name exactly matching PCF config

# Example: If PCF config says "sprk_matter_document_1n"
# Dataverse must have relationship with EXACT name (case-sensitive!)
```

---

#### Scenario 3: Document Upload Succeeds but Record Creation Fails

**Symptoms:**
- File uploaded to SPE successfully
- Document record creation fails with 400 Bad Request
- Browser console: `POST https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_documents 400`

**Diagnosis:**
```javascript
// Check browser console for payload
[DocumentRecordService] Payload: {
  "sprk_Matter@odata.bind": "/sprk_matters(guid)"  // Check this line!
}

// Error response
{
  "error": {
    "message": "Property 'sprk_matter@odata.bind' does not exist on type 'sprk_document'"
  }
}
```

**Possible Causes & Fixes:**

**Cause: Navigation property case mismatch**
- With Phase 7, this should be auto-resolved
- If still occurring, check:

```bash
# 1. Verify NavMap API returned correct property name
# Browser console should show:
[NavMapClient] Lookup navigation retrieved {
  navigationPropertyName: 'sprk_Matter',  // ← Check casing
  source: 'dataverse'
}

# 2. If cache is stale, clear it
# Azure Portal → Redis Cache → Console
> DEL navmap:lookup:sprk_document:sprk_matter_document_1n
> QUIT

# 3. Retry upload - will query fresh metadata
```

---

## Reference Documentation

### Microsoft Official Documentation

**Dataverse:**
- [ServiceClient Documentation](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/authenticate-dot-net-framework)
- [Entity Metadata](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/entity-metadata)
- [Web API Reference](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/reference/entitytypes)

**SharePoint Embedded:**
- [SharePoint Embedded Overview](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/overview)
- [Container Management](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/concepts/admin-exp/consuming-tenant-admin)
- [File Upload via Graph API](https://learn.microsoft.com/en-us/graph/api/driveitem-put-content)

**Microsoft Graph:**
- [On-Behalf-Of Flow](https://learn.microsoft.com/en-us/azure/active-directory/develop/v2-oauth2-on-behalf-of-flow)
- [Large File Upload](https://learn.microsoft.com/en-us/graph/sdks/large-file-upload)
- [Graph API Reference](https://learn.microsoft.com/en-us/graph/api/overview)

**PCF (Power Apps Component Framework):**
- [PCF Overview](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/overview)
- [Create PCF Component](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/create-custom-controls-using-pcf)
- [PCF API Reference](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/reference/)

**MSAL.js:**
- [MSAL.js Documentation](https://learn.microsoft.com/en-us/azure/active-directory/develop/msal-overview)
- [Single Sign-On with MSAL.js](https://learn.microsoft.com/en-us/azure/active-directory/develop/msal-js-sso)
- [Token Cache](https://learn.microsoft.com/en-us/azure/active-directory/develop/msal-acquire-cache-tokens)

---

### Project-Specific Documentation

**Setup & Configuration:**
- [HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md](./HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md) - Complete guide for adding SDAP to new entities (15-30 min)
- [PHASE-7-ADD-DATAVERSE-PERMISSION.md](./PHASE-7-ADD-DATAVERSE-PERMISSION.md) - Azure AD API permission setup
- [PHASE-7-CREATE-BFF-APP-USER.md](./PHASE-7-CREATE-BFF-APP-USER.md) - Dataverse Application User registration

**Architecture & Design:**
- This document (SDAP-ARCHITECTURE-GUIDE.md) - Comprehensive architecture reference

---

### Git Commits (Phase 7)

```
Phase 7 Implementation:
├─ f650391 - feat(phase-7): Implement dynamic navigation property metadata discovery
│   - Changed Dataverse auth from ManagedIdentity to connection string
│   - Updated sprk_project relationship schema name
│   - Added Phase 7 documentation
│
├─ 0e918d9 - docs: Add relationship renaming guide with AI code agent prompts
│   - Added "Updating Existing Relationship Names" section
│   - 4 AI code agent prompts for common scenarios
│   - Testing checklists and rollback procedures
│
└─ (Previous commits)
    ├─ a4196a1 - fix: OAuth scope - use full Application ID URI
    └─ f4654ae - fix: URL path - remove duplicate /api
```

---

### Testing Checklist

**Pre-Deployment:**
- [ ] BFF API builds with 0 errors
- [ ] PCF Control builds with 0 errors
- [ ] All unit tests pass (if implemented)
- [ ] Azure AD app registrations configured correctly
- [ ] Key Vault secrets up to date
- [ ] Application User exists in Dataverse with System Administrator role

**Post-Deployment:**
- [ ] Health endpoint returns 200 OK
- [ ] NavMap API returns metadata (test with curl + auth token)
- [ ] PCF control loads in Dataverse form
- [ ] Single file upload (<4MB) works
- [ ] Large file upload (>4MB) works
- [ ] Document record created with correct @odata.bind
- [ ] Second upload uses cached metadata (source: 'cache')
- [ ] Subgrid refreshes and shows new document
- [ ] Document file accessible via SPE link

**Multi-Entity Testing:**
- [ ] Matter entity upload works
- [ ] Project entity upload works
- [ ] (Other configured entities)

**Error Handling:**
- [ ] Upload with no container ID creates container
- [ ] Invalid file type handled gracefully
- [ ] Network error shows retry option
- [ ] Auth failure shows clear error message
- [ ] Relationship not found shows helpful error

---

### Support & Maintenance

**Monitoring Schedule:**
- **Daily:** Check health endpoint (automated monitoring recommended)
- **Weekly:** Review Application Insights errors and performance
- **Monthly:** Review API permissions and certificate expirations
- **Quarterly:** Review and rotate client secrets (if expiring)

**Backup & Disaster Recovery:**
- **Code:** Git repository with regular commits
- **Azure Resources:** ARM templates for infrastructure as code
- **Dataverse:** Solution backups before major changes
- **Secrets:** Key Vault with access audit logs

**Security Best Practices:**
- Rotate client secrets before expiration (24-month max)
- Monitor Application Insights for unusual activity
- Use least-privilege access for Application Users
- Regular security reviews of API permissions
- Enable Azure AD Conditional Access policies

**Performance Optimization:**
- Monitor Redis cache hit rate (target: >80%)
- Review Application Insights dependency durations
- Optimize large file chunking size if needed
- Consider Azure CDN for static PCF assets

---

## Conclusion

SDAP (SharePoint Embedded Document Attachment Platform) provides a comprehensive, enterprise-grade solution for document management in Dataverse. With Phase 7 dynamic metadata discovery, the system automatically adapts to new entities and relationships without code changes.

**Key Achievements:**
- ✅ **Multi-entity support** - Matter, Project, Invoice, and extensible to any entity
- ✅ **Phase 7 benefits** - 88% faster uploads via caching, no hardcoded metadata
- ✅ **Production-ready** - Deployed and tested in SPAARKE DEV 1
- ✅ **Comprehensive documentation** - Architecture, setup, troubleshooting, and AI prompts
- ✅ **Maintainable** - Clear separation of concerns, configuration-driven

**Next Steps:**
- Add SDAP to additional entities (Invoice, Account, Contact)
- Implement monitoring dashboards (Application Insights)
- Consider production deployment to additional environments
- Evaluate performance optimizations (CDN, advanced caching)

**Questions or Issues:**
- Review [HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md](./HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md) for entity configuration
- Check [Critical Issues & Resolutions](#critical-issues--resolutions) for common problems
- Review git commit history for implementation details

---

**Document Version:** 1.0.0
**Last Updated:** October 20, 2025
**Maintained By:** Development Team
**License:** Internal Use - Spaarke Inc.
