# Correct SharePoint Embedded Integration Pattern for Power Apps

**Date:** 2025-10-13
**Purpose:** Authoritative reference for SPE + Power Apps integration
**Sources:** Microsoft documentation, Azure AD best practices, proven patterns

---

## Architecture Overview

### The Correct Pattern

```
┌──────────────────────────────────────────────────────────────────┐
│  POWER APPS MODEL-DRIVEN APP (Dataverse)                         │
│                                                                   │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │  PCF Control (Browser - Public Client)                     │  │
│  │  • Uses MSAL.js (PublicClientApplication)                  │  │
│  │  • Acquires user delegated token                           │  │
│  │  • Token Audience: BFF API                                 │  │
│  └────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────┘
                              │
                              │ HTTP + Bearer Token
                              ▼
┌──────────────────────────────────────────────────────────────────┐
│  BACKEND FOR FRONTEND (BFF) API                                  │
│  • .NET 8 Web API                                                │
│  • Hosted in Azure App Service                                   │
│  • TWO authentication mechanisms:                                │
│                                                                   │
│  A) DELEGATED (User Context) - For file operations              │
│     • Validates incoming user token                              │
│     • Performs OBO to get Graph token                            │
│     • Calls Graph API as user                                    │
│                                                                   │
│  B) APPLICATION (Service Context) - For Dataverse operations     │
│     • Uses Managed Identity or Service Principal                 │
│     • Calls Dataverse as service account                         │
│     • No user context needed                                     │
└──────────────────────────────────────────────────────────────────┘
            │                                    │
            │ Delegated                          │ Application
            ▼                                    ▼
┌─────────────────────────┐    ┌─────────────────────────────────┐
│  MICROSOFT GRAPH API    │    │  DATAVERSE WEB API              │
│  (SharePoint Embedded)  │    │  (Dataverse entities)           │
└─────────────────────────┘    └─────────────────────────────────┘
```

---

## Required Azure AD App Registrations

### App Registration 1: PCF Control Client

**Purpose:** Public client for PCF control running in browser

**Configuration:**
```yaml
Name: "Spaarke PCF Client"
Application (client) ID: <GUID-1>
Type: Single-page application (SPA)

Authentication:
  Redirect URIs:
    - https://<your-dataverse-env>.crm.dynamics.com
  Platform: Single-page application
  Allow public client flows: No (uses PKCE)

API Permissions (Delegated):
  - BFF API / user_impersonation
  - Microsoft Graph / User.Read (optional)

Admin Consent: Required

Client Secret: NONE (public client)

Token Configuration:
  Access token version: 2.0
```

**MSAL Configuration (PCF):**
```typescript
const msalConfig: Configuration = {
  auth: {
    clientId: "<GUID-1>",  // PCF Client ID
    authority: "https://login.microsoftonline.com/<tenant-id>",
    redirectUri: "https://<your-dataverse-env>.crm.dynamics.com",
    navigateToLoginRequestUrl: false
  },
  cache: {
    cacheLocation: "sessionStorage",
    storeAuthStateInCookie: false
  }
};

const loginRequest = {
  scopes: ["api://<GUID-2>/user_impersonation"]  // BFF API scope
};
```

---

### App Registration 2: BFF API Server

**Purpose:** Confidential client for backend API (OBO flow)

**Configuration:**
```yaml
Name: "Spaarke BFF API"
Application (client) ID: <GUID-2>
Type: Web application

Authentication:
  Redirect URIs: (none needed for API)
  Platform: Web

Client Credentials:
  Client Secret: <SECRET>
  Expires: Set appropriately (1-2 years)

Application ID URI:
  api://<GUID-2>

Expose an API:
  Scopes:
    - Scope Name: user_impersonation
      Admin consent display name: Access BFF API
      Admin consent description: Allows the app to access BFF API on behalf of the signed-in user
      State: Enabled
      Who can consent: Admins and users

Manifest Configuration:
  knownClientApplications:
    - "<GUID-1>"  # Pre-authorize PCF client for OBO

API Permissions (Delegated):
  - Microsoft Graph / Files.ReadWrite.All
  - Microsoft Graph / Sites.Read.All
  - Microsoft Graph / Sites.ReadWrite.All
  - Microsoft Graph / FileStorageContainer.Selected

Admin Consent: Required

Token Configuration:
  Access token version: 2.0
```

**BFF API Configuration (appsettings.json):**
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<tenant-id>",
    "ClientId": "<GUID-2>",
    "ClientSecret": "<SECRET>",
    "Audience": "api://<GUID-2>"
  },

  "OBO": {
    "GraphScopes": [
      "https://graph.microsoft.com/Files.ReadWrite.All",
      "https://graph.microsoft.com/Sites.Read.All"
    ]
  }
}
```

---

### App Registration 3 (Optional): Service Principal for Dataverse

**Two Options:**

#### Option A: Managed Identity (Recommended)

**Configuration:**
```yaml
Type: System-assigned Managed Identity
Enabled on: Azure App Service hosting BFF API
Client ID: <assigned by Azure>

Azure AD:
  Automatically created identity
  No client secret needed

Dataverse:
  Application User:
    - Application ID: <managed-identity-client-id>
    - Name: "BFF API Managed Identity"
    - Security Role: System Administrator (or custom role)
    - Status: Active
```

**BFF API Configuration:**
```json
{
  "Dataverse": {
    "ServiceUrl": "https://<env>.api.crm.dynamics.com",
    "AuthType": "ManagedIdentity"
  }
}
```

**Code (ServiceClient with Managed Identity):**
```csharp
using Azure.Identity;
using Microsoft.PowerPlatform.Dataverse.Client;

var dataverseUrl = configuration["Dataverse:ServiceUrl"];
var credential = new DefaultAzureCredential();

var serviceClient = new ServiceClient(dataverseUrl, async (uri) =>
{
    var token = await credential.GetTokenAsync(
        new TokenRequestContext(new[] { $"{uri}/.default" }),
        cancellationToken: default
    );
    return token.Token;
});

if (!serviceClient.IsReady)
{
    throw new InvalidOperationException($"Dataverse connection failed: {serviceClient.LastError}");
}
```

#### Option B: Service Principal with Client Secret

**Configuration:**
```yaml
Name: "Spaarke BFF API - Dataverse"
Application (client) ID: <GUID-3>
Type: Web application

Client Credentials:
  Client Secret: <SECRET>

API Permissions (Application):
  - Dynamics CRM / user_impersonation (Application)

Admin Consent: Required

Dataverse:
  Application User:
    - Application ID: <GUID-3>
    - Name: "BFF API Service Principal"
    - Security Role: System Administrator (or custom role)
    - Status: Active
```

**BFF API Configuration:**
```json
{
  "Dataverse": {
    "ServiceUrl": "https://<env>.api.crm.dynamics.com",
    "ClientId": "<GUID-3>",
    "ClientSecret": "<SECRET>",
    "AuthType": "ClientSecret"
  }
}
```

**Code (ServiceClient with Client Secret):**
```csharp
using Microsoft.PowerPlatform.Dataverse.Client;

var dataverseUrl = configuration["Dataverse:ServiceUrl"];
var clientId = configuration["Dataverse:ClientId"];
var clientSecret = configuration["Dataverse:ClientSecret"];

var connectionString = $"AuthType=ClientSecret;" +
    $"Url={dataverseUrl};" +
    $"ClientId={clientId};" +
    $"ClientSecret={clientSecret};" +
    $"RequireNewInstance=true";

var serviceClient = new ServiceClient(connectionString);

if (!serviceClient.IsReady)
{
    throw new InvalidOperationException($"Dataverse connection failed: {serviceClient.LastError}");
}
```

---

## SharePoint Embedded Configuration

### Container Type Registration

**Create Container Type:**
```http
POST https://graph.microsoft.com/v1.0/storage/fileStorage/containerTypes
Authorization: Bearer <owner-app-token>
Content-Type: application/json

{
  "displayName": "Spaarke Document Storage",
  "description": "SharePoint Embedded containers for Spaarke documents",
  "owningApplicationId": "<owner-app-id>",
  "azureSubscriptionId": "<subscription-id>",
  "region": "WestUS2"
}

Response:
{
  "id": "<CONTAINER-TYPE-ID>",
  "displayName": "Spaarke Document Storage",
  ...
}
```

### Register BFF API with Container Type

**CRITICAL:** The BFF API must be registered with the Container Type to perform operations

```http
PUT https://<tenant>.sharepoint.com/_api/v2.1/storageContainerTypes/<CONTAINER-TYPE-ID>/applicationPermissions
Authorization: Bearer <owner-app-token-with-Container.Selected>
Content-Type: application/json

{
  "value": [
    {
      "appId": "<GUID-2>",  # BFF API App ID
      "delegated": [
        "ReadContent",
        "WriteContent"
      ],
      "appOnly": []
    }
  ]
}
```

**Why This is Required:**
- SPE validates the `appid` claim in Graph API tokens
- Even in delegated scenarios (OBO), the application must be registered
- This is documented in OBO-403-RESOLUTION-SUMMARY.md

---

## Authentication Flows

### Flow 1: User File Upload (Delegated Context)

```
1. User clicks "Upload" in PCF control
   ↓
2. PCF calls MSAL.js acquireTokenSilent()
   Input: scopes = ["api://<GUID-2>/user_impersonation"]
   Output: Token A (User Token)
     • aud: api://<GUID-2>
     • appid: <GUID-1> (PCF Client)
     • sub: <user-object-id>
     • scp: user_impersonation
   ↓
3. PCF sends Token A to BFF API
   PUT https://bff-api.azurewebsites.net/api/obo/files
   Authorization: Bearer <Token A>
   Body: <file-data>
   ↓
4. BFF API validates Token A
   • Signature check (Azure AD public keys)
   • Audience check (api://<GUID-2>)
   • Expiration check
   ✅ Valid → Continue
   ↓
5. BFF API performs OBO token exchange
   POST https://login.microsoftonline.com/<tenant>/oauth2/v2.0/token
   grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer
   client_id=<GUID-2>
   client_secret=<SECRET>
   assertion=<Token A>
   requested_token_use=on_behalf_of
   scope=https://graph.microsoft.com/.default

   Output: Token B (Graph Token)
     • aud: https://graph.microsoft.com
     • appid: <GUID-2> (BFF API) ← This is CORRECT for OBO
     • sub: <user-object-id>
     • scp: Files.ReadWrite.All Sites.Read.All ...
   ↓
6. BFF API calls Microsoft Graph
   PUT https://graph.microsoft.com/v1.0/storage/fileStorage/containers/<container-id>/drive/items/<filename>:/content
   Authorization: Bearer <Token B>
   Body: <file-data>
   ↓
7. Graph API validates Token B
   • Signature check
   • Audience check (graph.microsoft.com)
   • Check appid: <GUID-2> registered with Container Type? ✅ Yes
   • Check user permissions: User has access? ✅ Yes
   ✅ Allow → Upload file
   ↓
8. Graph returns file metadata
   Response: { id, name, driveId, itemId, webUrl, ... }
   ↓
9. BFF API returns success to PCF
   Response 200 OK with file metadata
   ↓
10. PCF displays success message
```

---

### Flow 2: BFF Dataverse Operations (Service Context)

```
1. BFF API needs to create Dataverse record
   ↓
2. BFF API uses Managed Identity / Service Principal
   (No user token needed)
   ↓
3. Azure AD issues service token
   Input: Managed Identity credentials (automatic)
   Output: Service Token
     • aud: https://<env>.api.crm.dynamics.com
     • appid: <managed-identity-client-id> OR <GUID-3>
     • token type: application
   ↓
4. ServiceClient connects to Dataverse
   Uses: OAuth callback with service token
   ↓
5. Dataverse validates service token
   • Signature check
   • Audience check
   • Check Application User exists? ✅ Yes
   • Check Security Role? ✅ System Administrator
   ✅ Allow
   ↓
6. ServiceClient performs CRUD operations
   Create, Read, Update, Delete records
```

---

## BFF API Implementation

### Startup Configuration (Program.cs)

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.PowerPlatform.Dataverse.Client;
using Azure.Identity;

var builder = WebApplication.CreateBuilder(args);

// 1. JWT Bearer Authentication (validates Token A from PCF)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

// 2. OBO Token Cache (recommended for performance)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, "AzureAd");

// 3. Dataverse ServiceClient (Managed Identity)
builder.Services.AddScoped<IDataverseService>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<DataverseService>>();

    var dataverseUrl = config["Dataverse:ServiceUrl"];
    var credential = new DefaultAzureCredential();

    var serviceClient = new ServiceClient(dataverseUrl, async (uri) =>
    {
        var token = await credential.GetTokenAsync(
            new TokenRequestContext(new[] { $"{uri}/.default" })
        );
        return token.Token;
    });

    if (!serviceClient.IsReady)
    {
        logger.LogError("Dataverse connection failed: {Error}", serviceClient.LastError);
        throw new InvalidOperationException($"Dataverse connection failed: {serviceClient.LastError}");
    }

    return new DataverseService(serviceClient, logger);
});

// 4. Graph Client Factory (OBO)
builder.Services.AddScoped<IGraphClientFactory, GraphClientFactory>();

// 5. CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("https://<env>.crm.dynamics.com")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

---

### GraphClientFactory (OBO Implementation)

```csharp
using Microsoft.Identity.Client;
using Microsoft.Graph;

public class GraphClientFactory : IGraphClientFactory
{
    private readonly IConfiguration _configuration;
    private readonly IConfidentialClientApplication _cca;

    public GraphClientFactory(IConfiguration configuration)
    {
        _configuration = configuration;

        var tenantId = configuration["AzureAd:TenantId"];
        var clientId = configuration["AzureAd:ClientId"];
        var clientSecret = configuration["AzureAd:ClientSecret"];

        _cca = ConfidentialClientApplicationBuilder
            .Create(clientId)
            .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
            .WithClientSecret(clientSecret)
            .Build();
    }

    public async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken)
    {
        // Perform OBO token exchange
        var result = await _cca.AcquireTokenOnBehalfOf(
            scopes: new[] { "https://graph.microsoft.com/.default" },
            userAssertion: new UserAssertion(userAccessToken)
        ).ExecuteAsync();

        // Create Graph client with OBO token
        var authProvider = new DelegateAuthenticationProvider(request =>
        {
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", result.AccessToken);
            return Task.CompletedTask;
        });

        return new GraphServiceClient(authProvider);
    }
}
```

---

### File Upload Controller

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web;

[Authorize]
[ApiController]
[Route("api/obo")]
public class FileUploadController : ControllerBase
{
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<FileUploadController> _logger;

    public FileUploadController(
        IGraphClientFactory graphClientFactory,
        IDataverseService dataverseService,
        ILogger<FileUploadController> logger)
    {
        _graphClientFactory = graphClientFactory;
        _dataverseService = dataverseService;
        _logger = logger;
    }

    [HttpPut("containers/{containerId}/files/{fileName}")]
    public async Task<IActionResult> UploadFile(
        string containerId,
        string fileName,
        [FromBody] Stream fileContent)
    {
        try
        {
            // 1. Get user token from Authorization header
            var userToken = HttpContext.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");

            // 2. Create Graph client with OBO
            var graphClient = await _graphClientFactory.CreateOnBehalfOfClientAsync(userToken);

            // 3. Upload file to SharePoint Embedded
            var uploadedFile = await graphClient
                .Storage
                .FileStorage
                .Containers[containerId]
                .Drive
                .Root
                .ItemWithPath(fileName)
                .Content
                .Request()
                .PutAsync<Microsoft.Graph.DriveItem>(fileContent);

            _logger.LogInformation("File uploaded: {FileName} -> {ItemId}", fileName, uploadedFile.Id);

            // 4. Create Dataverse record (using service context)
            await _dataverseService.CreateDocumentRecord(new DocumentRecord
            {
                Name = fileName,
                GraphDriveId = containerId,
                GraphItemId = uploadedFile.Id,
                FileSize = (int?)uploadedFile.Size ?? 0
            });

            // 5. Return success
            return Ok(new
            {
                id = uploadedFile.Id,
                name = uploadedFile.Name,
                driveId = containerId,
                itemId = uploadedFile.Id,
                webUrl = uploadedFile.WebUrl,
                size = uploadedFile.Size
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "File upload failed: {FileName}", fileName);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
```

---

## Deployment Checklist

### Azure AD Setup

- [ ] Create PCF Client App Registration (<GUID-1>)
  - [ ] Configure as SPA
  - [ ] Add redirect URI (Dataverse URL)
  - [ ] Add API permission to BFF API
  - [ ] No client secret needed

- [ ] Create BFF API App Registration (<GUID-2>)
  - [ ] Configure as Web application
  - [ ] Create client secret
  - [ ] Set Application ID URI: `api://<GUID-2>`
  - [ ] Expose scope: `user_impersonation`
  - [ ] Add `knownClientApplications`: `["<GUID-1>"]`
  - [ ] Add Graph API permissions (delegated)
  - [ ] Grant admin consent

- [ ] Configure Dataverse Authentication (Choose One):
  - [ ] **Option A (Recommended):** Enable Managed Identity on App Service
  - [ ] **Option B:** Create Service Principal App Registration (<GUID-3>)

### SharePoint Embedded Setup

- [ ] Create Container Type (via owner app)
- [ ] Save Container Type ID
- [ ] Register BFF API with Container Type
  - [ ] App ID: <GUID-2>
  - [ ] Delegated permissions: ReadContent, WriteContent

### Dataverse Setup

- [ ] Create Application User
  - [ ] Client ID: <managed-identity-client-id> OR <GUID-3>
  - [ ] Assign Security Role (System Administrator or custom)
  - [ ] Verify Status: Active

### Azure App Service Setup

- [ ] Deploy BFF API
- [ ] Configure App Settings:
  ```
  AzureAd__TenantId=<tenant-id>
  AzureAd__ClientId=<GUID-2>
  AzureAd__ClientSecret=<SECRET>
  AzureAd__Audience=api://<GUID-2>
  Dataverse__ServiceUrl=https://<env>.api.crm.dynamics.com
  ```
- [ ] Enable Managed Identity (if using Option A)
- [ ] Configure CORS (allow Dataverse URL)

### PCF Control Setup

- [ ] Update MSAL configuration with <GUID-1>
- [ ] Update API endpoint URL (BFF API)
- [ ] Update scope: `api://<GUID-2>/user_impersonation`
- [ ] Build and deploy PCF control

---

## Testing Procedure

### 1. Test JWT Validation
```bash
curl -X GET https://<bff-api>/ping \
  -H "Authorization: Bearer <invalid-token>"

Expected: 401 Unauthorized
```

### 2. Test Dataverse Connection
```bash
curl -X GET https://<bff-api>/healthz/dataverse

Expected: 200 OK {"status":"healthy"}
```

### 3. Test OBO Flow (via PCF)
- Open Dataverse form with PCF control
- Click upload button
- Select file
- Check browser console for:
  - ✅ MSAL token acquisition successful
  - ✅ API call returns 200 OK
  - ✅ File uploaded successfully

### 4. Verify File in SPE
```bash
curl -X GET https://graph.microsoft.com/v1.0/storage/fileStorage/containers/<container-id>/drive/items \
  -H "Authorization: Bearer <user-token>"

Expected: List includes uploaded file
```

### 5. Verify Dataverse Record
- Check `sprk_document` table
- Verify record created with correct metadata
- Verify lookup to parent entity

---

## Common Issues & Solutions

### Issue: 401 Unauthorized from BFF API

**Cause:** Token validation failing

**Check:**
```bash
# Verify audience in token matches BFF API configuration
# Decode JWT at jwt.ms
# Check "aud" claim = api://<GUID-2>
```

**Solution:** Update `AzureAd__Audience` in App Settings

---

### Issue: 403 Forbidden from Graph API

**Cause:** BFF API not registered with Container Type

**Solution:** Register BFF API (`<GUID-2>`) with Container Type delegated permissions

---

### Issue: 500 Error - Dataverse Connection Failed

**Cause:** Application User missing or Managed Identity not configured

**Check:**
```bash
# If using Managed Identity:
az webapp identity show --name <app-service-name> --resource-group <rg>

# Should return: systemAssignedIdentity with clientId
```

**Solution:** Create Application User in Dataverse with correct Client ID

---

### Issue: AADSTS50013 - Assertion failed signature validation

**Cause:** `knownClientApplications` not configured in BFF API app

**Solution:** Add PCF Client ID to `knownClientApplications` in manifest

---

## Summary: The Correct Pattern

```
┌─────────────────────────────────────────────────────────────┐
│  THREE SEPARATE APP IDENTITIES:                             │
│                                                              │
│  1. PCF Client (<GUID-1>)                                   │
│     • Public client (browser)                               │
│     • Acquires user token for BFF API                       │
│     • No secrets                                            │
│                                                              │
│  2. BFF API (<GUID-2>)                                      │
│     • Confidential client (server)                          │
│     • Performs OBO for Graph API                            │
│     • Has client secret                                     │
│     • Registered with SPE Container Type                    │
│                                                              │
│  3. Service Identity (Managed Identity OR <GUID-3>)         │
│     • For Dataverse operations only                         │
│     • Application User in Dataverse                         │
│     • No user context needed                                │
└─────────────────────────────────────────────────────────────┘

AUTHENTICATION FLOWS:

User Operations (File Upload/Download):
  PCF → Token A → BFF → OBO → Token B → Graph → SPE
  Uses: <GUID-1> (PCF) + <GUID-2> (BFF)
  Context: User delegated permissions

Service Operations (Dataverse CRUD):
  BFF → Managed Identity Token → Dataverse
  Uses: Managed Identity OR <GUID-3>
  Context: Service application permissions
```

---

**This is the correct, production-ready pattern for SharePoint Embedded integration with Power Apps.**
