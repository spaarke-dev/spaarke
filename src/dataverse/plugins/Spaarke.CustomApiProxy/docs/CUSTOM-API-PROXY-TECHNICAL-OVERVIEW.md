# Spaarke.CustomApiProxy - Technical Overview

**Purpose**: Thin Custom API proxy plugins for SharePoint Document Access Platform (SDAP)
**Architecture**: Server-side proxy pattern
**Status**: Production-Ready
**ADR Compliance**: ADR-002 (Thin plugins, no heavy orchestration)
**Last Updated**: 2025-12-03
**Maintainers**: Spaarke Engineering Team
**Status**: ✅ Production-Ready, ADR-002 Compliant

---

## Overview

Spaarke.CustomApiProxy provides Dataverse Custom API plugins that act as thin proxies between Power Apps clients and the SDAP BFF API. This architecture eliminates the need for client-side MSAL.js authentication and provides a secure, auditable path for SharePoint Embedded operations.

---

## Architecture

### System Architecture

```
┌─────────────────────────────────────────────┐
│      Power Apps Client (PCF Control)        │
│  - UniversalQuickCreate                     │
│  - SpeFileViewer                            │
└─────────────────┬───────────────────────────┘
                  │
                  ↓ Calls Custom API (OData function)
┌─────────────────────────────────────────────┐
│      Dataverse Custom API Plugin            │
│  ┌────────────────────────────────────────┐ │
│  │  GetFilePreviewUrlPlugin               │ │
│  │  - Validates inputs                    │ │
│  │  - Generates correlation ID            │ │
│  │  - Extracts document GUID              │ │
│  └──────┬─────────────────────────────────┘ │
└─────────┼───────────────────────────────────┘
          │
          ↓ HTTP POST with app-only token
┌─────────────────────────────────────────────┐
│      SDAP BFF API                           │
│  /api/documents/{id}/preview-url            │
│  ┌────────────────────────────────────────┐ │
│  │ 1. Validates user access (UAC check)  │ │
│  │ 2. Queries Dataverse for SPE pointers │ │
│  │ 3. Calls Graph API (app-only)         │ │
│  │ 4. Returns ephemeral preview URL      │ │
│  └────────────────────────────────────────┘ │
└─────────────────┬───────────────────────────┘
                  │
                  ↓ Calls with service principal
┌─────────────────────────────────────────────┐
│      Microsoft Graph API                    │
│  /drives/{driveId}/items/{itemId}/preview   │
│  - Returns ephemeral preview URL (~10 min)  │
└─────────────────────────────────────────────┘
```

### Why This Architecture?

**Problem**: Client-side authentication with MSAL.js is complex:
- Requires user consent for Graph API
- Token management in browser
- CORS challenges
- Security risks (tokens in client)

**Solution**: Server-side proxy pattern:
- ✅ Plugin validates and proxies request
- ✅ BFF API handles authentication (app-only)
- ✅ BFF API enforces authorization (UAC)
- ✅ Client receives only the ephemeral URL
- ✅ No client-side secrets or tokens

---

## ADR-002 Compliance

### ADR-002: Keep Dataverse Plugins Thin

**Decision**: Plugins perform only synchronous validation and proxying. No orchestration, no HTTP/Graph calls to external services besides the BFF API.

**Compliance Check**:

✅ **Thin Plugin Pattern**:
```csharp
public class GetFilePreviewUrlPlugin : BaseProxyPlugin
{
    public override void Execute(IServiceProvider serviceProvider)
    {
        // 1. Validate inputs (synchronous, fast)
        ValidateInputs(context);

        // 2. Call BFF API (simple HTTP call, no orchestration)
        var response = await _httpClient.PostAsync(bffApiUrl, requestBody);

        // 3. Return result (no processing, just pass-through)
        context.OutputParameters["PreviewUrl"] = response.PreviewUrl;
    }
}
```

❌ **What We Don't Do** (ADR-002 violations):
- ❌ No Graph API calls directly from plugin
- ❌ No complex orchestration logic
- ❌ No long-running operations
- ❌ No Service Bus messaging
- ❌ No heavy business logic

✅ **What We Do** (ADR-002 compliant):
- ✅ Input validation (fast, synchronous)
- ✅ Single HTTP call to BFF API
- ✅ Pass-through response
- ✅ Error handling and logging
- ✅ Correlation ID generation

---

## Components

### 1. BaseProxyPlugin

**File**: `Plugins/Spaarke.Dataverse.CustomApiProxy/BaseProxyPlugin.cs`
**Purpose**: Base class for all proxy plugins

**Responsibilities**:
- HttpClient management
- Correlation ID generation
- Error handling and logging
- Common authentication logic

**Key Features**:
```csharp
public abstract class BaseProxyPlugin : IPlugin
{
    protected HttpClient _httpClient;
    protected ITracingService _tracingService;

    protected string GenerateCorrelationId()
    {
        return Guid.NewGuid().ToString();
    }

    protected async Task<HttpResponseMessage> CallBffApiAsync(
        string endpoint,
        object requestBody,
        string correlationId)
    {
        // Authentication via External Service Config
        var token = await GetBffApiTokenAsync();

        // HTTP call with correlation header
        _httpClient.DefaultRequestHeaders.Add("X-Correlation-ID", correlationId);
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        return await _httpClient.PostAsync(endpoint, content);
    }
}
```

---

### 2. GetFilePreviewUrlPlugin

**File**: `Plugins/Spaarke.Dataverse.CustomApiProxy/GetFilePreviewUrlPlugin.cs`
**Purpose**: Custom API to retrieve ephemeral SharePoint Embedded preview URLs

**Custom API Registration**:
- **Unique Name**: `sprk_GetFilePreviewUrl`
- **Binding Type**: Entity (sprk_document)
- **Is Function**: Yes (GET operation)
- **Allowed Custom Processing Step Type**: Synchronous

**Input Parameters**:
- None (uses bound Document entity ID from URL)

**Output Parameters**:
| Parameter | Type | Description |
|-----------|------|-------------|
| `PreviewUrl` | String | Ephemeral preview URL (expires ~10 min) |
| `FileName` | String | File name for display |
| `FileSize` | Integer | File size in bytes |
| `ContentType` | String | MIME type (e.g., "application/pdf") |
| `ExpiresAt` | DateTime | When preview URL expires (UTC) |
| `CorrelationId` | String | Request tracking ID |

**Implementation**:
```csharp
public class GetFilePreviewUrlPlugin : BaseProxyPlugin
{
    private const string SERVICE_NAME = "SDAP_BFF_API";

    public GetFilePreviewUrlPlugin() : base("GetFilePreviewUrl")
    {
    }

    public override void Execute(IServiceProvider serviceProvider)
    {
        var context = GetPluginExecutionContext(serviceProvider);
        var tracingService = GetTracingService(serviceProvider);

        try
        {
            // 1. Extract document ID from bound entity
            var documentId = context.PrimaryEntityId;

            // 2. Generate correlation ID for tracing
            var correlationId = GenerateCorrelationId();

            tracingService.Trace($"GetFilePreviewUrl: Document {documentId}, Correlation {correlationId}");

            // 3. Call BFF API
            var bffApiUrl = GetBffApiEndpoint(SERVICE_NAME, $"/documents/{documentId}/preview-url");
            var response = await CallBffApiAsync(bffApiUrl, correlationId);

            // 4. Parse response
            var result = JsonConvert.DeserializeObject<PreviewUrlResponse>(response);

            // 5. Return output parameters
            context.OutputParameters["PreviewUrl"] = result.PreviewUrl;
            context.OutputParameters["FileName"] = result.FileName;
            context.OutputParameters["FileSize"] = result.FileSize;
            context.OutputParameters["ContentType"] = result.ContentType;
            context.OutputParameters["ExpiresAt"] = result.ExpiresAt;
            context.OutputParameters["CorrelationId"] = correlationId;

            tracingService.Trace($"✓ GetFilePreviewUrl: Success, URL expires {result.ExpiresAt}");
        }
        catch (Exception ex)
        {
            tracingService.Trace($"✗ GetFilePreviewUrl: Error - {ex.Message}");
            throw new InvalidPluginExecutionException($"Failed to retrieve preview URL: {ex.Message}", ex);
        }
    }
}
```

**Client Usage** (from PCF control):
```typescript
// Call Custom API from Power Apps PCF
const documentId = "doc-guid-123";
const response = await Xrm.WebApi.online.execute({
    getMetadata: () => ({
        boundParameter: "entity",
        operationType: 1, // Function
        operationName: "sprk_GetFilePreviewUrl",
        parameterTypes: {}
    }),
    entity: {
        entityType: "sprk_document",
        id: documentId
    }
});

// Parse response
const result = await response.json();
const previewUrl = result.PreviewUrl;  // Ephemeral URL
const fileName = result.FileName;
const expiresAt = new Date(result.ExpiresAt);

// Display preview (Office Online Viewer)
window.open(previewUrl, "_blank");
```

---

### 3. SimpleAuthHelper

**File**: `Plugins/Spaarke.Dataverse.CustomApiProxy/SimpleAuthHelper.cs`
**Purpose**: Authentication helper for BFF API calls

**Responsibilities**:
- Retrieve External Service Config from Dataverse
- Acquire OAuth tokens via ClientCredentials flow
- Token caching (in-memory, per plugin execution)

**External Service Config**:
```json
{
  "Name": "SDAP_BFF_API",
  "BaseUrl": "https://spe-api-dev-67e2xz.azurewebsites.net/api",
  "AuthType": 1,  // ClientCredentials
  "ClientId": "app-registration-guid",
  "ClientSecret": "stored-in-key-vault",
  "TenantId": "tenant-guid",
  "Scope": "api://bff-api-app-id/.default"
}
```

**Implementation**:
```csharp
public class SimpleAuthHelper
{
    private readonly IOrganizationService _orgService;
    private readonly ITracingService _tracingService;

    public async Task<string> GetAccessTokenAsync(string serviceName)
    {
        // 1. Query External Service Config
        var config = QueryExternalServiceConfig(serviceName);

        // 2. Acquire token via ClientCredentials
        var tokenEndpoint = $"https://login.microsoftonline.com/{config.TenantId}/oauth2/v2.0/token";
        var tokenResponse = await RequestTokenAsync(tokenEndpoint, config);

        // 3. Return access token
        return tokenResponse.AccessToken;
    }

    private ExternalServiceConfig QueryExternalServiceConfig(string serviceName)
    {
        var query = new QueryExpression("sprk_externalserviceconfig")
        {
            ColumnSet = new ColumnSet("sprk_baseurl", "sprk_clientid", "sprk_clientsecret", "sprk_tenantid", "sprk_scope"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("sprk_name", ConditionOperator.Equal, serviceName)
                }
            }
        };

        var results = _orgService.RetrieveMultiple(query);

        if (results.Entities.Count == 0)
        {
            throw new InvalidPluginExecutionException($"External Service Config '{serviceName}' not found");
        }

        return MapToConfig(results.Entities[0]);
    }
}
```

---

## Configuration

### External Service Config Setup

**Required**: Create `sprk_externalserviceconfig` record in Dataverse

**PowerShell Setup**:
```powershell
# Connect to Dataverse
pac auth create --environment "https://your-env.crm.dynamics.com"

# Create External Service Config
pac data create `
  --entity-logical-name "sprk_externalserviceconfig" `
  --columns `
    "sprk_name=SDAP_BFF_API" `
    "sprk_baseurl=https://spe-api-dev-67e2xz.azurewebsites.net/api" `
    "sprk_authtype=1" `
    "sprk_clientid=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" `
    "sprk_clientsecret=stored-in-key-vault" `
    "sprk_tenantid=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" `
    "sprk_scope=api://bff-api-app-id/.default"
```

**Security Note**: `sprk_clientsecret` should reference Azure Key Vault, not store actual secret.

---

## Deployment

### Build Plugin Assembly

```bash
# Navigate to plugin project
cd src/dataverse/plugins/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy

# Build release
dotnet build --configuration Release

# Output: bin/Release/net462/Spaarke.Dataverse.CustomApiProxy.dll
```

### Sign Assembly (Required for Dataverse)

```powershell
# Generate strong name key (one-time)
sn -k SpaarkePlugin.snk

# Add to .csproj
<PropertyGroup>
  <SignAssembly>true</SignAssembly>
  <AssemblyOriginatorKeyFile>SpaarkePlugin.snk</AssemblyOriginatorKeyFile>
</PropertyGroup>
```

### Register Plugin in Dataverse

**Option A: Plugin Registration Tool (GUI)**
1. Download Plugin Registration Tool
2. Connect to Dataverse environment
3. Register New Assembly → Upload `Spaarke.Dataverse.CustomApiProxy.dll`
4. Register Custom API: `sprk_GetFilePreviewUrl`
5. Register Step: PostOperation, Synchronous

**Option B: PAC CLI (Automated)**
```powershell
# Register plugin assembly
pac plugin push `
  --solution-name "Spaarke" `
  --assembly-path "bin/Release/net462/Spaarke.Dataverse.CustomApiProxy.dll"

# Register Custom API
pac customapi create `
  --unique-name "sprk_GetFilePreviewUrl" `
  --display-name "Get File Preview URL" `
  --binding-type "EntityType" `
  --bound-entity-logical-name "sprk_document" `
  --is-function true `
  --plugin-type "Spaarke.Dataverse.CustomApiProxy.GetFilePreviewUrlPlugin"
```

---

## Testing

### Integration Test (Power Apps)

```typescript
// Test in Power Apps console (F12 Developer Tools)
const documentId = "test-doc-guid";

const response = await Xrm.WebApi.online.execute({
    getMetadata: () => ({
        boundParameter: "entity",
        operationType: 1,
        operationName: "sprk_GetFilePreviewUrl",
        parameterTypes: {}
    }),
    entity: {
        entityType: "sprk_document",
        id: documentId
    }
});

const result = await response.json();
console.log("Preview URL:", result.PreviewUrl);
console.log("Expires:", result.ExpiresAt);
console.log("Correlation ID:", result.CorrelationId);
```

### Trace Logs

Plugin traces appear in Dataverse Plugin Trace Logs:

```
GetFilePreviewUrl: Document abc-123, Correlation xyz-789
Calling BFF API: https://spe-api-dev.../api/documents/abc-123/preview-url
✓ GetFilePreviewUrl: Success, URL expires 2025-12-03T10:15:00Z
```

---

## Troubleshooting

### Issue: "External Service Config not found"

**Cause**: `sprk_externalserviceconfig` record missing or wrong name

**Solution**:
```powershell
# Query existing configs
pac data list --entity-logical-name "sprk_externalserviceconfig"

# Create if missing (see Configuration section above)
```

### Issue: "Unauthorized" (401) from BFF API

**Cause**: Invalid client credentials or expired secret

**Solution**:
1. Verify `sprk_clientid` matches app registration
2. Check `sprk_clientsecret` is valid
3. Verify `sprk_scope` matches BFF API app ID
4. Check BFF API allows the service principal

### Issue: "Forbidden" (403) from BFF API

**Cause**: User lacks access to document in Dataverse UAC

**Solution**:
1. Check user has access in `sprk_documentaccess` table
2. Verify BFF API authorization logic
3. Check correlation ID in BFF API logs

---

## Performance Considerations

### Plugin Execution Time

**Target**: < 2 seconds total
- Input validation: < 10ms
- BFF API call: 500-1500ms
- Response parsing: < 10ms
- Total overhead: ~20ms (plugin logic only)

**Optimization**:
- HTTP connection pooling (HttpClient reuse)
- Token caching (avoid auth call every execution)
- Minimal JSON parsing

### BFF API Performance

The BFF API call is the bottleneck:
- UAC check: 50-100ms (cached in Redis)
- Dataverse query: 50-100ms (SPE pointers)
- Graph API call: 200-500ms (preview URL generation)

**Total BFF latency**: 300-700ms (acceptable)

---

## Security

### Plugin Security

✅ **Secure Practices**:
- Input validation (prevent injection)
- Correlation IDs for tracing
- Error messages don't leak sensitive info
- External Service Config isolates secrets

❌ **What to Avoid**:
- Never log secrets or tokens
- Don't return internal error details to client
- Don't bypass UAC checks

### BFF API Security

The BFF API is the security boundary:
- ✅ Validates user access via UAC
- ✅ Uses service principal (no user delegation)
- ✅ Returns ephemeral URLs only
- ✅ Comprehensive audit logging

---

## References

- **ADR-002**: [Keep Dataverse Plugins Thin](../../../docs/adr/ADR-002-thin-plugins.md)
- **SDAP BFF API**: [Technical Overview](../../server/api/Spe.Bff.Api/docs/TECHNICAL-OVERVIEW.md)
- **Custom API Documentation**: [Microsoft Learn](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/custom-api)
- **Plugin Registration**: [Microsoft Learn](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/register-plug-in)

---

## File Structure

```
Spaarke.CustomApiProxy/
├── Plugins/
│   └── Spaarke.Dataverse.CustomApiProxy/
│       ├── BaseProxyPlugin.cs               ← Base class for all proxies
│       ├── GetFilePreviewUrlPlugin.cs       ← Preview URL proxy
│       ├── SimpleAuthHelper.cs              ← Authentication helper
│       ├── Spaarke.Dataverse.CustomApiProxy.csproj
│       └── SpaarkePlugin.snk                ← Strong name key
├── docs/
│   └── TECHNICAL-OVERVIEW.md               ← This file
├── README.md                               ← Quick overview
└── Spaarke.CustomApiProxy.cdsproj          ← Solution project
```

---

## Change Log

| Date | Change | Reason |
|------|--------|--------|
| 2025-12-03 | Documentation created | Repository restructure and cleanup |
| 2025-09-30 | Initial GetFilePreviewUrlPlugin | Phase 7 - Office Online integration |

---
