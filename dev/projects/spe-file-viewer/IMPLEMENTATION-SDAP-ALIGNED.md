# SPE File Viewer - SDAP-Aligned Implementation
## Architecture: PCF → BFF with Proper UAC, Scopes, and Telemetry

---

## 🎯 Core Principles

1. **No outbound HTTP from plugins** - Plugins are transaction-scoped only
2. **BFF handles all external I/O** - Graph/SPE calls only in BFF or workers
3. **Named scopes for SPAs** - Use `api://<BFF_APP_ID>/SDAP.Access`, not `.default`
4. **UAC before storage** - Enforce via endpoint filters with AuthorizationService
5. **Correlation tracking** - X-Correlation-Id throughout the chain
6. **Short TTLs** - Preview URLs expire in minutes, enforce and surface expiresAt

---

## ✅ Phase 1: BFF Endpoints (Update Existing)

### 1.1 Route Group Setup

**File**: `Spe.Bff.Api/Program.cs` (or endpoint registration location)

```csharp
// Document endpoints with authorization
var docs = app.MapGroup("/api/documents")
    .RequireAuthorization()
    .WithTags("Documents");

// Preview URL for embeddable files (PDF, images, Office via iframe)
docs.MapGet("/{id:guid}/preview-url",
    async (Guid id, DocumentPreviewService svc, CancellationToken ct) =>
        Results.Json(await svc.GetPreviewAsync(id, ct)))
    .AddEndpointFilter<DocumentAuthorizationFilter>(DocumentOperation.Read)
    .WithName("GetDocumentPreview")
    .Produces<PreviewResponse>(200)
    .ProducesProblem(401)
    .ProducesProblem(403)
    .ProducesProblem(404);

// Download URL for non-embeddable files or direct download
docs.MapGet("/{id:guid}/download",
    async (Guid id, DocumentDownloadService svc, CancellationToken ct) =>
        await svc.GetDownloadHandleAsync(id, ct))
    .AddEndpointFilter<DocumentAuthorizationFilter>(DocumentOperation.Read)
    .WithName("GetDocumentDownload")
    .Produces<DownloadResponse>(200)
    .ProducesProblem(401)
    .ProducesProblem(403)
    .ProducesProblem(404);
```

### 1.2 Document Authorization Filter (Real UAC)

**File**: `Spe.Bff.Api/Api/Filters/DocumentAuthorizationFilter.cs` (update existing)

```csharp
using Microsoft.AspNetCore.Http.HttpResults;
using Spaarke.Authorization;
using Spe.Bff.Api.Infrastructure.Errors;

namespace Spe.Bff.Api.Api.Filters;

public class DocumentAuthorizationFilter : IEndpointFilter
{
    private readonly DocumentOperation _operation;

    public DocumentAuthorizationFilter(DocumentOperation operation)
    {
        _operation = operation;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var authService = httpContext.RequestServices.GetRequiredService<IAuthorizationService>();
        var logger = httpContext.RequestServices.GetRequiredService<ILogger<DocumentAuthorizationFilter>>();

        // Extract correlation ID
        var correlationId = httpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? httpContext.TraceIdentifier;

        // Extract document ID from route
        var documentId = context.GetArgument<Guid>(0);

        // Extract user ID from JWT claims
        var userId = httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("[{CorrelationId}] User ID not found in JWT claims", correlationId);
            return TypedResults.Unauthorized();
        }

        logger.LogInformation(
            "[{CorrelationId}] Authorizing {Operation} on document {DocumentId} for user {UserId}",
            correlationId, _operation, documentId, userId);

        // Check UAC via AuthorizationService
        var authResult = await authService.AuthorizeDocumentAccessAsync(
            userId,
            documentId.ToString(),
            _operation);

        if (!authResult.IsAuthorized)
        {
            logger.LogWarning(
                "[{CorrelationId}] Access denied for user {UserId} on document {DocumentId}: {Reason}",
                correlationId, userId, documentId, authResult.FailureReason);

            return ProblemDetailsHelper.Forbidden(
                $"Access denied: {authResult.FailureReason}",
                correlationId);
        }

        logger.LogInformation(
            "[{CorrelationId}] Access granted for user {UserId} on document {DocumentId}",
            correlationId, userId, documentId);

        // Store correlation ID in HttpContext for downstream use
        httpContext.Items["CorrelationId"] = correlationId;

        // Proceed to endpoint
        return await next(context);
    }
}

public enum DocumentOperation
{
    Read,
    Write,
    Delete
}
```

### 1.3 DocumentPreviewService (Updated)

**File**: `Spe.Bff.Api/Services/DocumentPreviewService.cs`

```csharp
using Spaarke.Dataverse;
using Spe.Bff.Api.Infrastructure.Graph;
using Spe.Bff.Api.Models;

namespace Spe.Bff.Api.Services;

public class DocumentPreviewService
{
    private readonly IDataverseService _dataverseService;
    private readonly SpeFileStore _speFileStore;
    private readonly ILogger<DocumentPreviewService> _logger;

    public DocumentPreviewService(
        IDataverseService dataverseService,
        SpeFileStore speFileStore,
        ILogger<DocumentPreviewService> logger)
    {
        _dataverseService = dataverseService;
        _speFileStore = speFileStore;
        _logger = logger;
    }

    public async Task<PreviewResponse> GetPreviewAsync(Guid documentId, CancellationToken ct)
    {
        var correlationId = GetCorrelationId();

        _logger.LogInformation(
            "[{CorrelationId}] Getting preview URL for document {DocumentId}",
            correlationId, documentId);

        // Get document from Dataverse
        var document = await _dataverseService.GetDocumentAsync(documentId.ToString());
        if (document == null)
        {
            throw new FileNotFoundException($"Document {documentId} not found");
        }

        // Validate SPE metadata
        if (string.IsNullOrEmpty(document.GraphDriveId) || string.IsNullOrEmpty(document.GraphItemId))
        {
            throw new InvalidOperationException(
                "Document does not have associated SharePoint Embedded metadata");
        }

        // Call Graph API via SpeFileStore (app-only token)
        var previewResult = await _speFileStore.GetPreviewUrlAsync(
            document.GraphDriveId,
            document.GraphItemId,
            correlationId); // Pass correlation ID for Graph telemetry

        _logger.LogInformation(
            "[{CorrelationId}] Preview URL retrieved successfully for document {DocumentId}",
            correlationId, documentId);

        return new PreviewResponse
        {
            Data = new PreviewData
            {
                PreviewUrl = previewResult.PreviewUrl,
                PostUrl = previewResult.PostUrl,
                ExpiresAt = previewResult.ExpiresAt,
                ContentType = document.MimeType
            },
            Metadata = new ResponseMetadata
            {
                CorrelationId = correlationId,
                DocumentId = documentId.ToString(),
                FileName = document.FileName,
                FileSize = document.FileSize,
                Timestamp = DateTimeOffset.UtcNow
            }
        };
    }

    private string GetCorrelationId()
    {
        // Retrieved from HttpContext.Items set by DocumentAuthorizationFilter
        return HttpContextAccessor.HttpContext?.Items["CorrelationId"]?.ToString()
            ?? Guid.NewGuid().ToString();
    }
}
```

### 1.4 SpeFileStore Update (Add Correlation ID Logging)

**File**: `Spe.Bff.Api/Infrastructure/Graph/SpeFileStore.cs` (update GetPreviewUrlAsync)

```csharp
public async Task<PreviewResult> GetPreviewUrlAsync(
    string driveId,
    string itemId,
    string? correlationId = null)
{
    correlationId ??= Guid.NewGuid().ToString();

    _logger.LogInformation(
        "[{CorrelationId}] Calling Graph API preview for drive {DriveId}, item {ItemId}",
        correlationId, driveId, itemId);

    try
    {
        var graphClient = await _graphClientFactory.GetClientAsync();

        var previewResult = await graphClient.Drives[driveId]
            .Items[itemId]
            .Preview
            .PostAsync(new PreviewPostRequestBody
            {
                // viewer can be "embed", "onedrive", etc.
            });

        if (previewResult == null || string.IsNullOrEmpty(previewResult.GetUrl))
        {
            throw new InvalidOperationException("Graph API did not return a preview URL");
        }

        // Log Graph request ID for supportability
        var graphRequestId = /* extract from response headers if available */;
        _logger.LogInformation(
            "[{CorrelationId}] Graph preview successful. Graph-Request-Id: {GraphRequestId}",
            correlationId, graphRequestId);

        return new PreviewResult
        {
            PreviewUrl = previewResult.GetUrl,
            PostUrl = previewResult.PostUrl,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10) // SPE preview URLs ~10min TTL
        };
    }
    catch (Exception ex)
    {
        _logger.LogError(ex,
            "[{CorrelationId}] Failed to get preview URL from Graph",
            correlationId);
        throw;
    }
}
```

### 1.5 CORS Configuration

**File**: `Spe.Bff.Api/Program.cs`

```csharp
// CORS for Dataverse origins
builder.Services.AddCors(options =>
{
    options.AddPolicy("DataverseOrigins", policy =>
    {
        policy.WithOrigins(
            "https://*.dynamics.com",
            "https://*.crm.dynamics.com",
            "https://*.powerapps.com"
        )
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials();
    });
});

// ...

app.UseCors("DataverseOrigins");
```

### 1.6 JWT Audience Validation

**File**: `Spe.Bff.Api/Program.cs`

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(options =>
    {
        builder.Configuration.Bind("AzureAd", options);

        // CRITICAL: Validate audience is BFF API app ID
        options.TokenValidationParameters.ValidAudiences = new[]
        {
            $"api://{builder.Configuration["AzureAd:ClientId"]}",
            builder.Configuration["AzureAd:ClientId"]
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILogger<Program>>();

                var audience = context.Principal?.FindFirst("aud")?.Value;
                logger.LogInformation("JWT validated. Audience: {Audience}", audience);

                return Task.CompletedTask;
            }
        };
    },
    options => builder.Configuration.Bind("AzureAd", options));
```

---

## 🚀 Phase 2: PCF Control (Corrected MSAL Scopes)

### 2.1 MSAL Configuration (Named Scope)

**File**: `FileViewer/services/AuthService.ts`

```typescript
import {
  PublicClientApplication,
  InteractionRequiredAuthError,
  AccountInfo,
  SilentRequest
} from "@azure/msal-browser";

const msalConfig = {
  auth: {
    clientId: "YOUR_DATAVERSE_APP_CLIENT_ID", // PCF app registration
    authority: "https://login.microsoftonline.com/YOUR_TENANT_ID",
    redirectUri: window.location.origin,
    navigateToLoginRequestUrl: false // Important for iframe scenarios
  },
  cache: {
    cacheLocation: "sessionStorage",
    storeAuthStateInCookie: false
  }
};

export const msalInstance = new PublicClientApplication(msalConfig);

/**
 * Acquire BFF API token using named scope (NOT .default).
 * @param loginHint Optional user hint to improve silent acquisition
 */
export async function getBffToken(loginHint?: string): Promise<string> {
  const accounts = msalInstance.getAllAccounts();

  if (accounts.length === 0) {
    throw new Error("No user account found. Please sign in.");
  }

  const account = accounts[0];

  const tokenRequest: SilentRequest = {
    scopes: ["api://YOUR_BFF_APP_ID/SDAP.Access"], // ✅ Named scope, not .default
    account: account,
    forceRefresh: false
  };

  if (loginHint) {
    (tokenRequest as any).loginHint = loginHint;
  }

  try {
    // Try silent acquisition first
    const response = await msalInstance.acquireTokenSilent(tokenRequest);
    return response.accessToken;
  } catch (error) {
    if (error instanceof InteractionRequiredAuthError) {
      // Silent failed - try popup (works better in iframes than redirect)
      try {
        const response = await msalInstance.acquireTokenPopup(tokenRequest);
        return response.accessToken;
      } catch (popupError) {
        // Last resort - redirect (may not work in iframe)
        const response = await msalInstance.acquireTokenRedirect(tokenRequest);
        return response.accessToken;
      }
    }
    throw error;
  }
}
```

### 2.2 BFF Client (With Correlation ID)

**File**: `FileViewer/services/BffClient.ts`

```typescript
import { getBffToken } from "./AuthService";
import { v4 as uuidv4 } from "uuid"; // npm install uuid

const BFF_BASE_URL = "https://spe-api-dev-67e2xz.azurewebsites.net";

export interface PreviewResponse {
  data: {
    previewUrl: string;
    postUrl: string | null;
    expiresAt: string; // ISO 8601 datetime
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

export interface DownloadResponse {
  data: {
    downloadUrl: string;
    contentType: string;
    fileName: string;
    size: number;
    expiresAt: string;
  };
  metadata: {
    correlationId: string;
    documentId: string;
    timestamp: string;
  };
}

/**
 * Get preview URL for embeddable file types.
 */
export async function getPreviewUrl(
  documentId: string,
  loginHint?: string
): Promise<PreviewResponse> {
  const token = await getBffToken(loginHint);
  const correlationId = uuidv4();

  console.log(`[${correlationId}] Requesting preview for document ${documentId}`);

  const response = await fetch(
    `${BFF_BASE_URL}/api/documents/${documentId}/preview-url`,
    {
      headers: {
        "Authorization": `Bearer ${token}`,
        "Content-Type": "application/json",
        "X-Correlation-Id": correlationId
      }
    }
  );

  if (!response.ok) {
    const error = await response.json();
    console.error(`[${correlationId}] BFF error:`, error);
    throw new Error(error.detail || `HTTP ${response.status}: ${response.statusText}`);
  }

  const result = await response.json();
  console.log(`[${correlationId}] Preview URL received, expires at ${result.data.expiresAt}`);

  return result;
}

/**
 * Get download URL for non-embeddable or download scenarios.
 */
export async function getDownloadUrl(
  documentId: string,
  loginHint?: string
): Promise<DownloadResponse> {
  const token = await getBffToken(loginHint);
  const correlationId = uuidv4();

  console.log(`[${correlationId}] Requesting download URL for document ${documentId}`);

  const response = await fetch(
    `${BFF_BASE_URL}/api/documents/${documentId}/download`,
    {
      headers: {
        "Authorization": `Bearer ${token}`,
        "Content-Type": "application/json",
        "X-Correlation-Id": correlationId
      }
    }
  );

  if (!response.ok) {
    const error = await response.json();
    console.error(`[${correlationId}] BFF error:`, error);
    throw new Error(error.detail || `HTTP ${response.status}: ${response.statusText}`);
  }

  const result = await response.json();
  console.log(`[${correlationId}] Download URL received, expires at ${result.data.expiresAt}`);

  return result;
}
```

### 2.3 PCF Index (With loginHint)

**File**: `FileViewer/index.ts`

```typescript
import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom";
import { FilePreview } from "./components/FilePreview";
import { msalInstance } from "./services/AuthService";

export class FileViewer implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private container: HTMLDivElement;
  private context: ComponentFramework.Context<IInputs>;

  public async init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): Promise<void> {
    this.container = container;
    this.context = context;

    // Initialize MSAL
    await msalInstance.initialize();

    // Attempt SSO with loginHint from user context
    const userSettings = (context as any).userSettings;
    const loginHint = userSettings?.userName; // May be email or UPN

    console.log("PCF initialized with loginHint:", loginHint);

    this.renderControl(context, loginHint);
  }

  public updateView(context: ComponentFramework.Context<IInputs>): void {
    this.context = context;
    const userSettings = (context as any).userSettings;
    const loginHint = userSettings?.userName;
    this.renderControl(context, loginHint);
  }

  private renderControl(
    context: ComponentFramework.Context<IInputs>,
    loginHint?: string
  ): void {
    const documentId = context.parameters.documentId?.raw || "";

    ReactDOM.render(
      React.createElement(FilePreview, { documentId, loginHint }),
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

## ✅ Acceptance Checklist

### BFF
- [ ] `/api/documents/{id}/preview-url` uses DocumentAuthorizationFilter
- [ ] DocumentAuthorizationFilter calls IAuthorizationService.AuthorizeDocumentAccessAsync
- [ ] JWT validation enforces BFF audience (`api://<BFF_APP_ID>`)
- [ ] CORS allows Dataverse origins (*.dynamics.com, *.powerapps.com)
- [ ] X-Correlation-Id is extracted from request and logged throughout
- [ ] SpeFileStore logs Graph-Request-Id for supportability
- [ ] Response includes `expiresAt` with ~10min TTL
- [ ] `/api/documents/{id}/download` endpoint exists for non-embeddable files

### PCF
- [ ] MSAL acquires token with named scope: `api://<BFF_APP_ID>/SDAP.Access`
- [ ] PCF passes loginHint from userSettings for better silent acquisition
- [ ] BFF calls include X-Correlation-Id header
- [ ] No Graph tokens in browser - only BFF audience tokens
- [ ] Popup fallback for silent acquisition failures
- [ ] Error handling shows user-friendly messages

### Revert
- [ ] Custom API `sprk_GetFilePreviewUrl` deleted from Dataverse
- [ ] Plugin assembly `Spaarke.Dataverse.CustomApiProxy` deleted from Dataverse
- [ ] Plugin code archived to `_archive/` or deleted

### Documentation
- [ ] ADR updated with "no outbound HTTP from plugins" rule
- [ ] Architecture diagrams show PCF → BFF flow (no Custom API)
- [ ] Deployment guide covers BFF scope exposure and PCF app registration
- [ ] Troubleshooting guide includes MSAL, CORS, and UAC denial scenarios

---

## 📋 Configuration Checklist

### Azure AD App Registrations

**BFF API App Registration**:
- Expose API scope: `SDAP.Access` (results in `api://<BFF_APP_ID>/SDAP.Access`)
- Add Dataverse PCF app as authorized client application

**Dataverse/PCF App Registration**:
- Add API permission: `api://<BFF_APP_ID>/SDAP.Access`
- Grant admin consent

### BFF appsettings.json
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "Domain": "yourtenant.onmicrosoft.com",
    "TenantId": "YOUR_TENANT_ID",
    "ClientId": "YOUR_BFF_APP_ID"
  },
  "AllowedOrigins": [
    "https://*.dynamics.com",
    "https://*.crm.dynamics.com",
    "https://*.powerapps.com"
  ]
}
```

---

## Summary

**Architecture**:
```
PCF (Dataverse Form)
  ↓ MSAL.js acquires token
  ↓ Scope: api://<BFF_APP_ID>/SDAP.Access
  ↓ Header: X-Correlation-Id
  ↓
DocumentAuthorizationFilter
  ↓ Validates JWT audience
  ↓ Calls IAuthorizationService (real UAC)
  ↓ Logs correlation ID
  ↓
BFF Endpoint (/preview-url or /download)
  ↓ Gets document from Dataverse
  ↓ Calls SpeFileStore with app-only token
  ↓
SpeFileStore
  ↓ Graph API driveItem:preview
  ↓ Logs Graph-Request-Id
  ↓
SharePoint Embedded
  ↓
Returns ephemeral URL with expiresAt
```

**Key Differences from Wrong Approach**:
- ✅ Named scope (`SDAP.Access`), not `.default`
- ✅ Real UAC filter with AuthorizationService
- ✅ Correlation ID propagation
- ✅ Separate `/preview-url` and `/download` endpoints
- ✅ No Custom API or plugin
