# Step 1: Backend Updates

**Phase**: 1 of 5
**Duration**: ~2 hours
**Prerequisites**: None

---

## Overview

Update the SDAP BFF API endpoint and Dataverse plugin to implement the corrected app-only authentication architecture. This phase creates a thin server-side proxy that eliminates all client-side authentication complexity.

**Repository Locations** (see [REPOSITORY-STRUCTURE.md](REPOSITORY-STRUCTURE.md)):
- **BFF API**: `src/api/Spe.Bff.Api/`
- **Plugin**: `src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/`

**Key Changes**:
- Rename plugin: `GetDocumentFileUrlPlugin` → `GetFilePreviewUrlPlugin`
- Change BFF route: `/preview` → `/preview-url`
- Simplify plugin to thin proxy (no Graph logic)
- Add correlation ID support for request tracing

---

## Task 1.1: Verify/Create SpeFileStore Service

**Goal**: Ensure SpeFileStore service has a method to get preview URLs

### Check if Service Exists

```bash
# Search for SpeFileStore service
pac pae code search "class SpeFileStore" --path src/api/Spe.Bff.Api
```

### Expected: Service Should Have

```csharp
public interface ISpeFileStore
{
    Task<FilePreviewDto> GetPreviewUrlAsync(string driveId, string itemId, CancellationToken ct = default);
}
```

### If Method Missing, Add It

**File**: `src/api/Spe.Bff.Api/Services/SpeFileStore.cs`

```csharp
public async Task<FilePreviewDto> GetPreviewUrlAsync(
    string driveId,
    string itemId,
    CancellationToken ct = default)
{
    _logger.LogInformation("Getting preview URL for driveId={DriveId}, itemId={ItemId}", driveId, itemId);

    // Call Graph API preview action
    var previewRequest = new PreviewPostRequestBody
    {
        // Optional: Set viewer="onedrive" or viewer="office" for different experiences
        Viewer = "office"
    };

    var previewResult = await _graphClient.Drives[driveId]
        .Items[itemId]
        .Preview
        .PostAsync(previewRequest, cancellationToken: ct);

    if (previewResult == null || string.IsNullOrEmpty(previewResult.GetUrl))
    {
        throw new InvalidOperationException($"Failed to get preview URL for item {itemId}");
    }

    return new FilePreviewDto(
        PreviewUrl: previewResult.GetUrl,
        PostUrl: previewResult.PostUrl,
        ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10), // Preview URLs expire in ~10 min
        ContentType: null // Will be enriched from Document metadata
    );
}
```

**Validation**:
```bash
# Build to verify no compile errors
dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj
```

---

## Task 1.2: Update BFF Endpoint

**Goal**: Change route, add UAC validation, verify app-only token usage

### File to Edit

`src/api/Spe.Bff.Api/Api/FileAccessEndpoints.cs`

### Changes Required

1. **Change route**: Line 30 - `/preview` → `/preview-url`
2. **Add correlation ID support**: Extract from request header
3. **Add UAC validation**: Verify user can access document
4. **Use SpeFileStore service**: Replace direct Graph call

### Updated Implementation

```csharp
fileAccessGroup.MapGet("/{documentId}/preview-url", async (
    string documentId,
    [FromServices] IDataverseService dataverseService,
    [FromServices] ISpeFileStore speFileStore,
    [FromServices] ILogger<Program> logger,
    HttpContext context) =>
{
    // Extract correlation ID from header (set by plugin)
    var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault()
        ?? Guid.NewGuid().ToString();

    logger.LogInformation(
        "[{CorrelationId}] Getting preview URL for document {DocumentId}",
        correlationId,
        documentId);

    try
    {
        // Step 1: Get Document record from Dataverse
        var document = await dataverseService.GetDocumentAsync(documentId);
        if (document == null)
        {
            logger.LogWarning("[{CorrelationId}] Document not found: {DocumentId}",
                correlationId, documentId);
            return TypedResults.NotFound(new { error = "Document not found" });
        }

        // Step 2: Extract userId from JWT claims
        var userId = context.User.FindFirst("oid")?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            logger.LogWarning("[{CorrelationId}] User ID not found in JWT claims", correlationId);
            return TypedResults.Unauthorized();
        }

        // Step 3: Validate user access via Spaarke UAC
        var hasAccess = await dataverseService.ValidateDocumentAccessAsync(documentId, userId);
        if (!hasAccess)
        {
            logger.LogWarning(
                "[{CorrelationId}] User {UserId} does not have access to document {DocumentId}",
                correlationId, userId, documentId);
            return TypedResults.Forbid();
        }

        // Step 4: Get preview URL from SPE via Graph API (app-only)
        var previewResult = await speFileStore.GetPreviewUrlAsync(
            document.GraphDriveId,
            document.GraphItemId);

        // Step 5: Build response
        var response = new FilePreviewDto(
            PreviewUrl: previewResult.PreviewUrl,
            PostUrl: previewResult.PostUrl,
            ExpiresAt: previewResult.ExpiresAt,
            ContentType: document.MimeType
        );

        var metadata = new
        {
            correlationId,
            documentId,
            fileName = document.FileName,
            fileSize = document.FileSize,
            timestamp = DateTimeOffset.UtcNow
        };

        logger.LogInformation(
            "[{CorrelationId}] Preview URL retrieved successfully for {DocumentId}",
            correlationId, documentId);

        return TypedResults.Ok(new { data = response, metadata });
    }
    catch (Exception ex)
    {
        logger.LogError(ex,
            "[{CorrelationId}] Error getting preview URL for document {DocumentId}",
            correlationId, documentId);
        return TypedResults.Problem(
            title: "Error retrieving file preview URL",
            detail: ex.Message,
            statusCode: 500
        );
    }
})
.RequireAuthorization()
.WithName("GetDocumentPreviewUrl")
.WithTags("File Access")
.Produces<FilePreviewDto>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status401Unauthorized)
.Produces(StatusCodes.Status403Forbidden)
.Produces(StatusCodes.Status404NotFound)
.Produces(StatusCodes.Status500InternalServerError);
```

### Add UAC Validation Method

**File**: `src/api/Spe.Bff.Api/Services/DataverseService.cs`

```csharp
public async Task<bool> ValidateDocumentAccessAsync(string documentId, string userId)
{
    // Query Spaarke UAC tables to verify user has access to this document
    // This is your existing access control enforcement layer

    _logger.LogInformation("Validating access for user {UserId} to document {DocumentId}",
        userId, documentId);

    // TODO: Implement based on your Spaarke UAC model
    // Example FetchXML query:
    var fetchXml = $@"
        <fetch top='1'>
            <entity name='sprk_document'>
                <attribute name='sprk_documentid' />
                <filter>
                    <condition attribute='sprk_documentid' operator='eq' value='{documentId}' />
                </filter>
                <link-entity name='sprk_matter' from='sprk_matterid' to='sprk_matterid' alias='matter'>
                    <link-entity name='sprk_matteraccess' from='sprk_matterid' to='sprk_matterid'>
                        <filter>
                            <condition attribute='sprk_userid' operator='eq' value='{userId}' />
                        </filter>
                    </link-entity>
                </link-entity>
            </entity>
        </fetch>";

    var result = await _organizationService.RetrieveMultipleAsync(new FetchExpression(fetchXml));
    return result.Entities.Count > 0;
}
```

**Validation**:
```bash
# Build to verify no compile errors
dotnet build src/api/Spe.Bff.Api/Spe.Bff.Api.csproj
```

---

## Task 1.3: Update Plugin

**Goal**: Rename plugin, simplify to thin proxy, add correlation ID

### File to Rename and Edit

**Current**: `src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/GetDocumentFileUrlPlugin.cs`
**New Name**: `GetFilePreviewUrlPlugin.cs`

### Rename File

```bash
cd c:/code_files/spaarke/src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy

# Rename file
mv GetDocumentFileUrlPlugin.cs GetFilePreviewUrlPlugin.cs
```

### Updated Plugin Code

Replace entire file contents:

```csharp
using System;
using System.Net.Http;
using Microsoft.Xrm.Sdk;
using Newtonsoft.Json.Linq;

namespace Spaarke.Dataverse.CustomApiProxy
{
    /// <summary>
    /// Custom API Plugin: sprk_GetFilePreviewUrl
    ///
    /// Thin server-side proxy that retrieves ephemeral SharePoint Embedded preview URLs
    /// via the SDAP BFF API. This eliminates client-side MSAL.js authentication complexity.
    ///
    /// Architecture:
    /// - Plugin validates inputs and generates correlation ID
    /// - Calls SDAP BFF API /preview-url endpoint (authenticated with app-only token)
    /// - BFF validates user access via Spaarke UAC
    /// - BFF calls Graph API with service principal (app-only)
    /// - Plugin returns ephemeral preview URL to client
    ///
    /// Custom API Registration:
    /// - Unique Name: sprk_GetFilePreviewUrl
    /// - Binding Type: Entity (sprk_document)
    /// - Is Function: Yes
    ///
    /// Input Parameters:
    /// - None (uses bound Document entity ID)
    ///
    /// Output Parameters:
    /// - PreviewUrl (String) - Ephemeral preview URL (expires ~10 min)
    /// - FileName (String) - File name for display
    /// - FileSize (Integer) - File size in bytes
    /// - ContentType (String) - MIME type
    /// - ExpiresAt (DateTime) - When preview URL expires (UTC)
    /// - CorrelationId (String) - Request tracking ID
    ///
    /// Required Configuration:
    /// - External Service Config record: "SDAP_BFF_API"
    /// - BaseUrl: https://spe-api-dev-67e2xz.azurewebsites.net/api
    /// - AuthType: ClientCredentials (1)
    /// - ClientId, ClientSecret, TenantId, Scope
    /// </summary>
    public class GetFilePreviewUrlPlugin : BaseProxyPlugin
    {
        private const string SERVICE_NAME = "SDAP_BFF_API";

        public GetFilePreviewUrlPlugin() : base("GetFilePreviewUrl")
        {
        }

        protected override void ValidateRequest()
        {
            base.ValidateRequest();

            // Validate this is a bound call on sprk_document entity
            if (ExecutionContext.PrimaryEntityName != "sprk_document" ||
                ExecutionContext.PrimaryEntityId == Guid.Empty)
            {
                throw new InvalidPluginExecutionException(
                    "This Custom API must be called on a Document (sprk_document) record.");
            }
        }

        protected override void ExecuteProxy(IServiceProvider serviceProvider, string correlationId)
        {
            var documentId = ExecutionContext.PrimaryEntityId;

            TracingService.Trace($"[{correlationId}] Getting preview URL for document: {documentId}");

            // Get service configuration
            var config = GetServiceConfig(SERVICE_NAME);

            // Call SDAP BFF API with retry logic
            var result = ExecuteWithRetry(() => CallBffApi(documentId, correlationId, config), config);

            // Set output parameters
            ExecutionContext.OutputParameters["PreviewUrl"] = result.PreviewUrl;
            ExecutionContext.OutputParameters["FileName"] = result.FileName ?? "";
            ExecutionContext.OutputParameters["FileSize"] = result.FileSize;
            ExecutionContext.OutputParameters["ContentType"] = result.ContentType ?? "";
            ExecutionContext.OutputParameters["ExpiresAt"] = result.ExpiresAt;
            ExecutionContext.OutputParameters["CorrelationId"] = correlationId;

            TracingService.Trace($"[{correlationId}] Successfully retrieved preview URL");
        }

        private FilePreviewResult CallBffApi(
            Guid documentId,
            string correlationId,
            ExternalServiceConfig config)
        {
            using (var httpClient = CreateAuthenticatedHttpClient(config))
            {
                // Add correlation ID header for tracing
                httpClient.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

                var endpoint = $"/documents/{documentId}/preview-url";
                TracingService.Trace($"[{correlationId}] Calling BFF API: {config.BaseUrl}{endpoint}");

                var response = httpClient.GetAsync(endpoint).GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    TracingService.Trace($"[{correlationId}] BFF API error: {response.StatusCode} - {errorContent}");

                    throw new InvalidPluginExecutionException(
                        $"SDAP BFF API returned {response.StatusCode}: {errorContent}");
                }

                var content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                TracingService.Trace($"[{correlationId}] BFF API response received (length: {content.Length})");

                // Parse response
                var result = ParseBffResponse(content, correlationId);
                return result;
            }
        }

        private FilePreviewResult ParseBffResponse(string jsonResponse, string correlationId)
        {
            try
            {
                var json = JObject.Parse(jsonResponse);
                var data = json["data"];
                var metadata = json["metadata"];

                if (data == null)
                {
                    throw new InvalidPluginExecutionException(
                        "BFF API response missing 'data' property");
                }

                var result = new FilePreviewResult
                {
                    PreviewUrl = data["previewUrl"]?.ToString(),
                    ContentType = data["contentType"]?.ToString(),
                    ExpiresAt = data["expiresAt"]?.ToObject<DateTime>() ?? DateTime.UtcNow.AddMinutes(10),
                    FileName = metadata?["fileName"]?.ToString() ?? "",
                    FileSize = metadata?["fileSize"]?.ToObject<long>() ?? 0
                };

                if (string.IsNullOrEmpty(result.PreviewUrl))
                {
                    throw new InvalidPluginExecutionException(
                        "BFF API did not return a preview URL");
                }

                TracingService.Trace($"[{correlationId}] Parsed preview URL successfully");
                return result;
            }
            catch (Exception ex)
            {
                TracingService.Trace($"[{correlationId}] Error parsing BFF response: {ex.Message}");
                throw new InvalidPluginExecutionException(
                    $"Failed to parse BFF API response: {ex.Message}", ex);
            }
        }

        private class FilePreviewResult
        {
            public string PreviewUrl { get; set; }
            public string FileName { get; set; }
            public long FileSize { get; set; }
            public string ContentType { get; set; }
            public DateTime ExpiresAt { get; set; }
        }
    }
}
```

**Key Changes**:
- ✅ Renamed class: `GetDocumentFileUrlPlugin` → `GetFilePreviewUrlPlugin`
- ✅ Removed `EndpointType` parameter (always `/preview-url`)
- ✅ Added `CorrelationId` to output parameters
- ✅ Added correlation ID header to BFF API call
- ✅ Simplified parsing (single response format)
- ✅ Enhanced tracing with correlation ID

---

## Task 1.4: Build and Test Plugin

### Build Plugin Assembly

```bash
# Navigate to plugin directory
cd c:/code_files/spaarke/src/dataverse/Spaarke.CustomApiProxy

# Temporarily disable Directory.Packages.props if needed
if [ -f "../../Directory.Packages.props" ]; then
    mv "../../Directory.Packages.props" "../../Directory.Packages.props.disabled"
fi

# Build plugin in Release mode
dotnet build Plugins/Spaarke.Dataverse.CustomApiProxy/Spaarke.Dataverse.CustomApiProxy.csproj \
    -c Release

# Restore Directory.Packages.props
if [ -f "../../Directory.Packages.props.disabled" ]; then
    mv "../../Directory.Packages.props.disabled" "../../Directory.Packages.props"
fi
```

### Verify Build Output

**Expected**: 0 errors (warnings are acceptable)

**Assembly Location**:
```
c:\code_files\spaarke\src\dataverse\Spaarke.CustomApiProxy\Plugins\Spaarke.Dataverse.CustomApiProxy\bin\Release\net462\Spaarke.Dataverse.CustomApiProxy.dll
```

### Verify Plugin Class in Assembly

```bash
# Install ildasm if not available (optional verification step)
# Or use Visual Studio to inspect assembly

# Verify assembly contains GetFilePreviewUrlPlugin class
```

---

## Validation Checklist

- [ ] **SpeFileStore Service**: Has `GetPreviewUrlAsync` method
- [ ] **BFF Endpoint**: Route changed to `/preview-url`
- [ ] **BFF Endpoint**: UAC validation implemented
- [ ] **BFF Endpoint**: Correlation ID support added
- [ ] **Plugin**: Renamed to `GetFilePreviewUrlPlugin`
- [ ] **Plugin**: Removed `EndpointType` parameter
- [ ] **Plugin**: Added `CorrelationId` output parameter
- [ ] **Plugin**: Builds successfully (0 errors)
- [ ] **Assembly**: Located at expected path

---

## Common Issues

### Issue: Build Fails with NU1008

**Error**: `Projects that use central package version management should not define the version`

**Fix**: Temporarily disable `Directory.Packages.props` as shown in Task 1.4

### Issue: SpeFileStore Service Doesn't Exist

**Fix**: Create new service class implementing `ISpeFileStore` interface, register in DI container

### Issue: UAC Validation Not Implemented

**Fix**: Use placeholder `return true` temporarily, implement full UAC logic based on Spaarke security model

---

## Next Step

Once all validation checks pass, proceed to **Step 2: Custom API Registration** to register the Custom API in Dataverse.

**Files Modified**:
- `src/api/Spe.Bff.Api/Services/SpeFileStore.cs` (or created)
- `src/api/Spe.Bff.Api/Api/FileAccessEndpoints.cs`
- `src/api/Spe.Bff.Api/Services/DataverseService.cs`
- `src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/GetFilePreviewUrlPlugin.cs`

**Build Artifacts**:
- `Spaarke.Dataverse.CustomApiProxy.dll` (Release build)
