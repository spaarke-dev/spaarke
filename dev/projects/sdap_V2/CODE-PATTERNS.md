# Code Patterns and Examples for SDAP V2

**Last Updated**: 2025-10-13
**Status**: Active - Target Architecture Patterns
**Purpose**: Practical code patterns and examples for implementing SDAP V2 architecture

---

## Table of Contents

1. [Minimal API Endpoint Pattern](#1-minimal-api-endpoint-pattern)
2. [DTO Pattern (Never Expose Graph SDK Types)](#2-dto-pattern-never-expose-graph-sdk-types)
3. [Authorization Pattern](#3-authorization-pattern)
4. [Error Handling Pattern](#4-error-handling-pattern)
5. [Options Pattern for Configuration](#5-options-pattern-for-configuration)
6. [Graph Client Factory Pattern](#6-graph-client-factory-pattern)
7. [Token Caching Pattern](#7-token-caching-pattern)
8. [Dataverse Access Pattern](#8-dataverse-access-pattern)
9. [Background Service Pattern](#9-background-service-pattern)
10. [Logging Pattern](#10-logging-pattern)

---

## 1. Minimal API Endpoint Pattern

### Standard Endpoint Structure

**File Location**: `src/api/Spe.Bff.Api/Api/OBOEndpoints.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using Spe.Bff.Api.Infrastructure.Graph;
using Spe.Bff.Api.Models;

namespace Spe.Bff.Api.Api;

/// <summary>
/// On-Behalf-Of (OBO) endpoints for user-context file operations.
/// All operations use OBO token exchange to preserve user identity.
/// </summary>
public static class OBOEndpoints
{
    public static void MapOBOEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/api/obo")
            .RequireAuthorization()
            .WithTags("OBO Operations")
            .WithOpenApi();

        // File upload endpoint
        group.MapPut("containers/{containerId}/files/{fileName}", UploadFile)
            .WithName("UploadFileOBO")
            .RequireAuthorization("canuploadfiles")
            .WithRateLimiting("upload-heavy")
            .Produces<FileUploadResult>(200)
            .Produces(401)  // Unauthorized
            .Produces(403)  // Forbidden
            .Produces(429); // Too Many Requests

        // File download endpoint
        group.MapGet("containers/{containerId}/files/{fileId}", DownloadFile)
            .WithName("DownloadFileOBO")
            .RequireAuthorization("candownloadfiles")
            .WithRateLimiting("graph-read")
            .Produces<FileContentHttpResult>(200)
            .Produces(401)
            .Produces(403)
            .Produces(404);

        // File delete endpoint
        group.MapDelete("containers/{containerId}/files/{fileId}", DeleteFile)
            .WithName("DeleteFileOBO")
            .RequireAuthorization("candeletefiles")
            .WithRateLimiting("graph-write")
            .Produces(204)  // No Content
            .Produces(401)
            .Produces(403)
            .Produces(404);
    }

    /// <summary>
    /// Upload file to SPE container using OBO flow.
    /// </summary>
    private static async Task<IResult> UploadFile(
        [FromRoute] string containerId,
        [FromRoute] string fileName,
        HttpRequest request,
        SpeFileStore fileStore,          // Inject concrete class (ADR-007)
        ILogger<OBOEndpoints> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // Extract user token from Authorization header
            var token = ExtractBearerToken(request);
            if (string.IsNullOrEmpty(token))
            {
                return Results.Unauthorized();
            }

            // Validate file name
            if (!IsValidFileName(fileName))
            {
                return Results.BadRequest(new { error = "Invalid file name" });
            }

            logger.LogInformation(
                "Uploading file {FileName} to container {ContainerId}",
                fileName, containerId);

            // Upload file using SpeFileStore facade (ADR-007)
            var result = await fileStore.UploadFileAsync(
                containerId,
                fileName,
                request.Body,
                token,
                cancellationToken);

            logger.LogInformation(
                "File uploaded successfully: {ItemId}",
                result.ItemId);

            return Results.Ok(result);
        }
        catch (ServiceException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            logger.LogWarning("Access denied: {Message}", ex.Message);
            return Results.Problem(
                detail: "Access denied to container or insufficient permissions",
                statusCode: 403);
        }
        catch (ServiceException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogWarning("Container not found: {ContainerId}", containerId);
            return Results.NotFound(new { error = "Container not found" });
        }
        catch (ServiceException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            var retryAfter = ex.ResponseHeaders?.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60);
            logger.LogWarning("Throttled by Graph API, retry after {RetryAfter}", retryAfter);
            return Results.StatusCode(429);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Upload failed for {FileName}", fileName);
            return Results.Problem(
                detail: "An unexpected error occurred during file upload",
                statusCode: 500);
        }
    }

    /// <summary>
    /// Download file from SPE container using OBO flow.
    /// </summary>
    private static async Task<IResult> DownloadFile(
        [FromRoute] string containerId,
        [FromRoute] string fileId,
        HttpRequest request,
        SpeFileStore fileStore,
        ILogger<OBOEndpoints> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var token = ExtractBearerToken(request);
            if (string.IsNullOrEmpty(token))
            {
                return Results.Unauthorized();
            }

            logger.LogInformation(
                "Downloading file {FileId} from container {ContainerId}",
                fileId, containerId);

            // Get file stream and metadata
            var stream = await fileStore.DownloadFileAsync(
                containerId,
                fileId,
                token,
                cancellationToken);

            var metadata = await fileStore.GetFileMetadataAsync(
                containerId,
                fileId,
                token,
                cancellationToken);

            logger.LogInformation("File downloaded successfully: {FileId}", fileId);

            // Return file with proper content type
            return Results.File(
                stream,
                contentType: metadata.MimeType ?? "application/octet-stream",
                fileDownloadName: metadata.Name,
                enableRangeProcessing: true);
        }
        catch (ServiceException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogWarning("File not found: {FileId}", fileId);
            return Results.NotFound(new { error = "File not found" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Download failed for {FileId}", fileId);
            return Results.Problem("Download failed", statusCode: 500);
        }
    }

    /// <summary>
    /// Delete file from SPE container using OBO flow.
    /// </summary>
    private static async Task<IResult> DeleteFile(
        [FromRoute] string containerId,
        [FromRoute] string fileId,
        HttpRequest request,
        SpeFileStore fileStore,
        ILogger<OBOEndpoints> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var token = ExtractBearerToken(request);
            if (string.IsNullOrEmpty(token))
            {
                return Results.Unauthorized();
            }

            logger.LogInformation(
                "Deleting file {FileId} from container {ContainerId}",
                fileId, containerId);

            await fileStore.DeleteFileAsync(containerId, fileId, token, cancellationToken);

            logger.LogInformation("File deleted successfully: {FileId}", fileId);

            return Results.NoContent();
        }
        catch (ServiceException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogWarning("File not found: {FileId}", fileId);
            return Results.NotFound(new { error = "File not found" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Delete failed for {FileId}", fileId);
            return Results.Problem("Delete failed", statusCode: 500);
        }
    }

    // Helper methods
    private static string? ExtractBearerToken(HttpRequest request)
    {
        var authHeader = request.Headers.Authorization.ToString();
        return authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authHeader.Substring("Bearer ".Length).Trim()
            : null;
    }

    private static bool IsValidFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;

        // Block dangerous file extensions
        var blockedExtensions = new[] { ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs" };
        return !blockedExtensions.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }
}
```

### Registration Pattern

**File Location**: `src/api/Spe.Bff.Api/Program.cs`

```csharp
var app = builder.Build();

// Map endpoint groups
app.MapOBOEndpoints();
app.MapDocumentsEndpoints();
app.MapUploadEndpoints();
app.MapPermissionsEndpoints();
app.MapHealthChecks();

app.Run();
```

---

## 2. DTO Pattern (Never Expose Graph SDK Types)

### ✅ CORRECT: SDAP DTOs

**File Location**: `src/api/Spe.Bff.Api/Models/FileUploadResult.cs`

```csharp
namespace Spe.Bff.Api.Models;

/// <summary>
/// Result of file upload operation.
/// NEVER exposes Graph SDK types (DriveItem, Drive, etc.)
/// </summary>
public record FileUploadResult
{
    /// <summary>
    /// SPE drive item ID (unique identifier for file in container)
    /// </summary>
    public required string ItemId { get; init; }

    /// <summary>
    /// File name as stored in SPE
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// MIME type (e.g., "application/pdf")
    /// </summary>
    public string? MimeType { get; init; }

    /// <summary>
    /// Web URL for viewing file in Office Online
    /// </summary>
    public string? WebUrl { get; init; }

    /// <summary>
    /// When file was created in SPE
    /// </summary>
    public DateTimeOffset? CreatedDateTime { get; init; }

    /// <summary>
    /// ETag for optimistic concurrency
    /// </summary>
    public string? ETag { get; init; }
}
```

**File Location**: `src/api/Spe.Bff.Api/Models/ContainerDto.cs`

```csharp
namespace Spe.Bff.Api.Models;

/// <summary>
/// SPE container metadata.
/// </summary>
public record ContainerDto
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public DateTimeOffset? CreatedDateTime { get; init; }
    public string? Description { get; init; }
    public string? ContainerTypeId { get; init; }
}
```

### Mapping Graph SDK to DTOs

**File Location**: `src/api/Spe.Bff.Api/Storage/SpeFileStore.cs`

```csharp
public async Task<FileUploadResult> UploadFileAsync(
    string containerId,
    string fileName,
    Stream content,
    string userToken,
    CancellationToken cancellationToken = default)
{
    var graphClient = await _graphFactory.CreateOnBehalfOfClientAsync(userToken);

    // Call Graph SDK (returns DriveItem)
    var driveItem = await graphClient
        .Storage
        .FileStorage
        .Containers[containerId]
        .Drive
        .Root
        .ItemWithPath(fileName)
        .Content
        .Request()
        .PutAsync<DriveItem>(content, cancellationToken);

    // ALWAYS map to DTO before returning
    return new FileUploadResult
    {
        ItemId = driveItem.Id!,
        Name = driveItem.Name!,
        Size = driveItem.Size ?? 0,
        MimeType = driveItem.File?.MimeType,
        WebUrl = driveItem.WebUrl,
        CreatedDateTime = driveItem.CreatedDateTime,
        ETag = driveItem.ETag
    };
}
```

### ❌ WRONG: Exposing Graph SDK Types

```csharp
// DON'T DO THIS - Never return Graph SDK types
public async Task<DriveItem> UploadFileAsync(...) // ← Graph SDK type leaked!
{
    return await graphClient.Drives[driveId]...;
}

// DON'T DO THIS - Never use Graph SDK types in endpoint signatures
public static async Task<IResult> GetFile(
    string fileId,
    IGraphClientFactory factory)
{
    var client = factory.CreateAppOnlyClient();
    var driveItem = await client.Drives[...]...;
    return Results.Ok(driveItem); // ← Exposing DriveItem to API consumer!
}
```

---

## 3. Authorization Pattern

### Resource-Based Authorization with Dataverse

**File Location**: `src/api/Spe.Bff.Api/Infrastructure/Authorization/ResourceAccessHandler.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Spaarke.Core.Auth;

namespace Spe.Bff.Api.Infrastructure.Authorization;

/// <summary>
/// Authorization handler that checks user permissions in Dataverse.
/// Implements resource-based authorization for document operations.
/// </summary>
public class ResourceAccessHandler : AuthorizationHandler<ResourceAccessRequirement>
{
    private readonly AuthorizationService _authService;
    private readonly ILogger<ResourceAccessHandler> _logger;

    public ResourceAccessHandler(
        AuthorizationService authService,
        ILogger<ResourceAccessHandler> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceAccessRequirement requirement)
    {
        // Get HTTP context
        var httpContext = context.Resource as HttpContext;
        if (httpContext == null)
        {
            context.Fail();
            return;
        }

        // Extract document ID from route
        var documentId = httpContext.GetRouteValue("fileId")?.ToString()
            ?? httpContext.GetRouteValue("itemId")?.ToString();

        if (string.IsNullOrEmpty(documentId))
        {
            _logger.LogWarning("No document ID found in route for authorization");
            context.Fail();
            return;
        }

        // Extract user ID from token claims
        var userId = context.User.FindFirst("oid")?.Value
            ?? context.User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("No user ID found in token claims");
            context.Fail();
            return;
        }

        // Check user permissions in Dataverse
        var authResult = await _authService.EvaluateAsync(new AuthorizationContext
        {
            UserId = userId,
            ResourceId = documentId,
            Operation = requirement.Operation,
            RequestPath = httpContext.Request.Path
        });

        if (authResult.Authorized)
        {
            _logger.LogDebug(
                "User {UserId} authorized for operation {Operation} on document {DocumentId}",
                userId, requirement.Operation, documentId);

            context.Succeed(requirement);
        }
        else
        {
            _logger.LogWarning(
                "User {UserId} denied access for operation {Operation} on document {DocumentId}: {Reason}",
                userId, requirement.Operation, documentId, authResult.Reason);

            context.Fail();
        }
    }
}
```

### Authorization Policy Registration

**File Location**: `src/api/Spe.Bff.Api/Extensions/AuthorizationExtensions.cs`

```csharp
namespace Spe.Bff.Api.Extensions;

public static class AuthorizationExtensions
{
    public static IServiceCollection AddAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            // File content operations
            options.AddPolicy("canuploadfiles",
                p => p.Requirements.Add(new ResourceAccessRequirement("driveitem.content.upload")));

            options.AddPolicy("candownloadfiles",
                p => p.Requirements.Add(new ResourceAccessRequirement("driveitem.content.download")));

            options.AddPolicy("candeletefiles",
                p => p.Requirements.Add(new ResourceAccessRequirement("driveitem.delete")));

            // Metadata operations
            options.AddPolicy("canreadmetadata",
                p => p.Requirements.Add(new ResourceAccessRequirement("driveitem.get")));

            options.AddPolicy("canupdatemetadata",
                p => p.Requirements.Add(new ResourceAccessRequirement("driveitem.update")));

            // Container operations
            options.AddPolicy("canlistcontainers",
                p => p.Requirements.Add(new ResourceAccessRequirement("container.list")));

            options.AddPolicy("cancreatecontainers",
                p => p.Requirements.Add(new ResourceAccessRequirement("container.create")));
        });

        return services;
    }
}
```

---

## 4. Error Handling Pattern

### Standard Error Handling in Endpoints

```csharp
private static async Task<IResult> OperationAsync(
    string resourceId,
    SomeService service,
    ILogger<EndpointClass> logger)
{
    try
    {
        var result = await service.PerformOperationAsync(resourceId);
        return Results.Ok(result);
    }
    catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
    {
        logger.LogWarning("Resource not found: {ResourceId}, Message: {Message}",
            resourceId, ex.Message);

        return Results.Problem(
            title: "Resource Not Found",
            detail: $"The requested resource '{resourceId}' was not found",
            statusCode: 404,
            instance: $"/api/resources/{resourceId}");
    }
    catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
    {
        logger.LogWarning("Access denied to resource: {ResourceId}, Message: {Message}",
            resourceId, ex.Message);

        return Results.Problem(
            title: "Access Denied",
            detail: "You do not have permission to access this resource",
            statusCode: 403);
    }
    catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
    {
        var retryAfter = ex.ResponseHeaders?.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60);

        logger.LogWarning("Throttled by Graph API, retry after {RetryAfter}s",
            retryAfter.TotalSeconds);

        var response = Results.StatusCode(429);
        // Note: Set Retry-After header in middleware
        return response;
    }
    catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
    {
        logger.LogWarning("Conflict detected: {Message}", ex.Message);

        return Results.Problem(
            title: "Conflict",
            detail: "The operation conflicts with the current state of the resource",
            statusCode: 409);
    }
    catch (OperationCanceledException)
    {
        logger.LogInformation("Operation cancelled by client");
        return Results.StatusCode(499); // Client Closed Request
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unexpected error in operation for resource {ResourceId}", resourceId);

        return Results.Problem(
            title: "Internal Server Error",
            detail: "An unexpected error occurred. Please try again later.",
            statusCode: 500);
    }
}
```

### Custom Exception Types

**File Location**: `src/api/Spe.Bff.Api/Exceptions/SdapException.cs`

```csharp
namespace Spe.Bff.Api.Exceptions;

/// <summary>
/// Base exception for SDAP-specific errors.
/// </summary>
public class SdapException : Exception
{
    public string ErrorCode { get; }
    public int StatusCode { get; }

    public SdapException(
        string message,
        string errorCode,
        int statusCode = 500,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }
}

/// <summary>
/// Exception for authorization failures.
/// </summary>
public class UnauthorizedAccessException : SdapException
{
    public UnauthorizedAccessException(string message, string? userId = null)
        : base(message, "UNAUTHORIZED_ACCESS", 403)
    {
        Data["UserId"] = userId;
    }
}

/// <summary>
/// Exception for resource not found errors.
/// </summary>
public class ResourceNotFoundException : SdapException
{
    public ResourceNotFoundException(string resourceType, string resourceId)
        : base($"{resourceType} '{resourceId}' not found", "RESOURCE_NOT_FOUND", 404)
    {
        Data["ResourceType"] = resourceType;
        Data["ResourceId"] = resourceId;
    }
}
```

---

## 5. Options Pattern for Configuration

### Configuration Class with Validation

**File Location**: `src/api/Spe.Bff.Api/Configuration/DataverseOptions.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace Spe.Bff.Api.Configuration;

/// <summary>
/// Configuration options for Dataverse connection.
/// </summary>
public class DataverseOptions
{
    public const string SectionName = "Dataverse";

    [Required(ErrorMessage = "Dataverse ServiceUrl is required")]
    [Url(ErrorMessage = "Dataverse ServiceUrl must be a valid URL")]
    public string ServiceUrl { get; set; } = string.Empty;

    [Required(ErrorMessage = "Dataverse ClientId is required")]
    [RegularExpression(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        ErrorMessage = "Dataverse ClientId must be a valid GUID")]
    public string ClientId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Dataverse ClientSecret is required")]
    [MinLength(1, ErrorMessage = "Dataverse ClientSecret cannot be empty")]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Maximum retry attempts for transient failures
    /// </summary>
    [Range(0, 10, ErrorMessage = "MaxRetryAttempts must be between 0 and 10")]
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Timeout for Dataverse operations in seconds
    /// </summary>
    [Range(5, 300, ErrorMessage = "TimeoutSeconds must be between 5 and 300")]
    public int TimeoutSeconds { get; set; } = 30;
}
```

### Registration with Validation

**File Location**: `src/api/Spe.Bff.Api/Program.cs`

```csharp
// Register options with validation
builder.Services
    .AddOptions<DataverseOptions>()
    .Bind(builder.Configuration.GetSection(DataverseOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart(); // Fail fast if configuration is invalid

builder.Services
    .AddOptions<GraphOptions>()
    .Bind(builder.Configuration.GetSection(GraphOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<RedisOptions>()
    .Bind(builder.Configuration.GetSection(RedisOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

### Usage in Services

```csharp
using Microsoft.Extensions.Options;

public class SomeService
{
    private readonly DataverseOptions _options;
    private readonly ILogger<SomeService> _logger;

    public SomeService(
        IOptions<DataverseOptions> options,
        ILogger<SomeService> logger)
    {
        _options = options.Value; // Get validated configuration
        _logger = logger;

        _logger.LogInformation(
            "Initialized with Dataverse URL: {ServiceUrl}",
            _options.ServiceUrl);
    }

    public async Task PerformOperationAsync()
    {
        // Use configuration
        var timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
        var client = CreateClientWithTimeout(timeout);

        // ...
    }
}
```

---

## 6. Graph Client Factory Pattern

### Interface Definition

**File Location**: `src/api/Spe.Bff.Api/Infrastructure/Graph/IGraphClientFactory.cs`

```csharp
namespace Spe.Bff.Api.Infrastructure.Graph;

/// <summary>
/// Factory for creating Microsoft Graph clients with different authentication patterns.
/// Interface allowed per ADR-010 (factory pattern).
/// </summary>
public interface IGraphClientFactory
{
    /// <summary>
    /// Create Graph client using On-Behalf-Of (OBO) flow.
    /// Preserves user identity for delegated permissions.
    /// </summary>
    /// <param name="userAccessToken">User's access token from frontend</param>
    /// <returns>GraphServiceClient authenticated as user</returns>
    Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken);

    /// <summary>
    /// Create Graph client using app-only authentication.
    /// Used for admin/background operations without user context.
    /// </summary>
    /// <returns>GraphServiceClient authenticated as application</returns>
    GraphServiceClient CreateAppOnlyClient();
}
```

### Implementation with Token Caching

**File Location**: `src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs`

```csharp
using Azure.Core;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Kiota.Authentication.Azure;

namespace Spe.Bff.Api.Infrastructure.Graph;

public sealed class GraphClientFactory : IGraphClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfidentialClientApplication _cca;
    private readonly GraphTokenCache _tokenCache;
    private readonly ILogger<GraphClientFactory> _logger;
    private readonly string _tenantId;
    private readonly string _clientId;
    private readonly string? _clientSecret;

    public GraphClientFactory(
        IHttpClientFactory httpClientFactory,
        GraphTokenCache tokenCache,
        IConfiguration configuration,
        ILogger<GraphClientFactory> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenCache = tokenCache;
        _logger = logger;

        _tenantId = configuration["TENANT_ID"]
            ?? throw new InvalidOperationException("TENANT_ID not configured");

        _clientId = configuration["API_APP_ID"]
            ?? throw new InvalidOperationException("API_APP_ID not configured");

        _clientSecret = configuration["API_CLIENT_SECRET"];

        // Build confidential client application for OBO
        var ccaBuilder = ConfidentialClientApplicationBuilder
            .Create(_clientId)
            .WithAuthority($"https://login.microsoftonline.com/{_tenantId}");

        if (!string.IsNullOrWhiteSpace(_clientSecret))
        {
            ccaBuilder = ccaBuilder.WithClientSecret(_clientSecret);
        }

        _cca = ccaBuilder.Build();

        _logger.LogInformation(
            "GraphClientFactory initialized for client {ClientId}",
            _clientId.Substring(0, 8) + "...");
    }

    public async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken)
    {
        // Step 1: Check token cache (ADR-009)
        var tokenHash = _tokenCache.ComputeTokenHash(userAccessToken);
        var cachedToken = await _tokenCache.GetTokenAsync(tokenHash);

        if (cachedToken != null)
        {
            _logger.LogDebug("Cache HIT for token hash {Hash}...", tokenHash[..8]);
            return CreateGraphClientWithToken(cachedToken);
        }

        _logger.LogDebug("Cache MISS for token hash {Hash}..., performing OBO exchange", tokenHash[..8]);

        // Step 2: Perform OBO token exchange
        var result = await _cca.AcquireTokenOnBehalfOf(
            scopes: new[]
            {
                "https://graph.microsoft.com/Sites.FullControl.All",
                "https://graph.microsoft.com/Files.ReadWrite.All"
            },
            userAssertion: new UserAssertion(userAccessToken))
            .ExecuteAsync();

        _logger.LogInformation("OBO token exchange completed, caching for 55 minutes");

        // Step 3: Cache token (55-minute TTL, 5-minute buffer before 60-minute expiration)
        await _tokenCache.SetTokenAsync(
            tokenHash,
            result.AccessToken,
            TimeSpan.FromMinutes(55));

        return CreateGraphClientWithToken(result.AccessToken);
    }

    public GraphServiceClient CreateAppOnlyClient()
    {
        TokenCredential credential;

        if (!string.IsNullOrWhiteSpace(_clientSecret))
        {
            // Use client secret for local development
            credential = new ClientSecretCredential(_tenantId, _clientId, _clientSecret);
            _logger.LogDebug("Creating app-only client with ClientSecretCredential");
        }
        else
        {
            // Use managed identity in Azure
            credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ExcludeInteractiveBrowserCredential = true,
                ExcludeSharedTokenCacheCredential = true,
                ExcludeVisualStudioCodeCredential = true,
                ExcludeVisualStudioCredential = true
            });
            _logger.LogDebug("Creating app-only client with DefaultAzureCredential");
        }

        var authProvider = new AzureIdentityAuthenticationProvider(
            credential,
            scopes: new[] { "https://graph.microsoft.com/.default" });

        // Use named HttpClient with Polly resilience policies
        var httpClient = _httpClientFactory.CreateClient("GraphClient");

        return new GraphServiceClient(httpClient, authProvider);
    }

    private GraphServiceClient CreateGraphClientWithToken(string accessToken)
    {
        var tokenCredential = new SimpleTokenCredential(accessToken);
        var authProvider = new AzureIdentityAuthenticationProvider(
            tokenCredential,
            scopes: new[] { "https://graph.microsoft.com/.default" });

        var httpClient = _httpClientFactory.CreateClient("GraphClient");
        return new GraphServiceClient(httpClient, authProvider);
    }
}
```

---

## 7. Token Caching Pattern

### GraphTokenCache Implementation

**File Location**: `src/api/Spe.Bff.Api/Services/GraphTokenCache.cs`

```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;

namespace Spe.Bff.Api.Services;

/// <summary>
/// Caches Graph API OBO tokens to reduce Azure AD load (ADR-009).
/// Target: 95% cache hit rate, 97% reduction in auth latency.
/// </summary>
public class GraphTokenCache
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<GraphTokenCache> _logger;

    public GraphTokenCache(
        IDistributedCache cache,
        ILogger<GraphTokenCache> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Compute SHA256 hash of user token for cache key.
    /// Ensures consistent key length and prevents token exposure in logs.
    /// </summary>
    public string ComputeTokenHash(string userToken)
    {
        if (string.IsNullOrEmpty(userToken))
            throw new ArgumentException("User token cannot be null or empty", nameof(userToken));

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(userToken));
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Get cached Graph token by user token hash.
    /// </summary>
    /// <param name="tokenHash">SHA256 hash of user token</param>
    /// <returns>Cached Graph token or null if cache miss</returns>
    public async Task<string?> GetTokenAsync(string tokenHash)
    {
        var cacheKey = $"sdap:graph:token:{tokenHash}";

        try
        {
            var cachedToken = await _cache.GetStringAsync(cacheKey);

            if (cachedToken != null)
            {
                _logger.LogDebug("Cache HIT for token hash {Hash}...", tokenHash[..8]);
            }
            else
            {
                _logger.LogDebug("Cache MISS for token hash {Hash}...", tokenHash[..8]);
            }

            return cachedToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving token from cache");
            return null; // Fail gracefully, will perform OBO exchange
        }
    }

    /// <summary>
    /// Cache Graph token with TTL.
    /// </summary>
    /// <param name="tokenHash">SHA256 hash of user token</param>
    /// <param name="graphToken">Graph API access token from OBO exchange</param>
    /// <param name="expiry">TTL (typically 55 minutes for 1-hour tokens)</param>
    public async Task SetTokenAsync(string tokenHash, string graphToken, TimeSpan expiry)
    {
        var cacheKey = $"sdap:graph:token:{tokenHash}";

        try
        {
            await _cache.SetStringAsync(
                cacheKey,
                graphToken,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = expiry
                });

            _logger.LogDebug(
                "Cached token for hash {Hash}... with TTL {TTL} minutes",
                tokenHash[..8],
                expiry.TotalMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching token");
            // Don't throw - caching is optimization, not requirement
        }
    }

    /// <summary>
    /// Remove token from cache (e.g., on logout or token revocation).
    /// </summary>
    public async Task RemoveTokenAsync(string tokenHash)
    {
        var cacheKey = $"sdap:graph:token:{tokenHash}";

        try
        {
            await _cache.RemoveAsync(cacheKey);
            _logger.LogDebug("Removed cached token for hash {Hash}...", tokenHash[..8]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing token from cache");
        }
    }
}
```

### Redis Configuration

**File Location**: `src/api/Spe.Bff.Api/Extensions/RedisCacheExtensions.cs`

```csharp
namespace Spe.Bff.Api.Extensions;

public static class RedisCacheExtensions
{
    public static IServiceCollection AddRedisCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisOptions = configuration.GetSection("Redis").Get<RedisOptions>();

        if (redisOptions?.Enabled == true)
        {
            var connectionString = configuration.GetConnectionString("Redis")
                ?? throw new InvalidOperationException("Redis connection string not configured");

            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = connectionString;
                options.InstanceName = redisOptions.InstanceName ?? "sdap:";
            });

            services.AddSingleton<GraphTokenCache>();
        }
        else
        {
            // Fallback to in-memory cache for development
            services.AddDistributedMemoryCache();
            services.AddSingleton<GraphTokenCache>();
        }

        return services;
    }
}
```

---

## 8. Dataverse Access Pattern

### Connection String Pattern (Singleton)

**File Location**: `src/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs`

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Spaarke.Dataverse;

/// <summary>
/// Dataverse service client using client secret authentication.
/// Singleton lifetime for connection pooling (ADR-010).
/// </summary>
public class DataverseServiceClientImpl
{
    private readonly ServiceClient _serviceClient;
    private readonly ILogger<DataverseServiceClientImpl> _logger;

    public DataverseServiceClientImpl(
        IOptions<DataverseOptions> options,
        ILogger<DataverseServiceClientImpl> logger)
    {
        _logger = logger;
        var config = options.Value;

        var connectionString =
            $"AuthType=ClientSecret;" +
            $"Url={config.ServiceUrl};" +
            $"ClientId={config.ClientId};" +
            $"ClientSecret={config.ClientSecret};" +
            $"RequireNewInstance=false;"; // Enable connection pooling

        try
        {
            _serviceClient = new ServiceClient(connectionString);

            if (!_serviceClient.IsReady)
            {
                var lastError = _serviceClient.LastError;
                throw new InvalidOperationException(
                    $"Failed to connect to Dataverse: {lastError}");
            }

            _logger.LogInformation(
                "Connected to Dataverse: {OrgName} (v{Version})",
                _serviceClient.ConnectedOrgFriendlyName,
                _serviceClient.ConnectedOrgVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Dataverse connection");
            throw;
        }
    }

    /// <summary>
    /// Create document record in Dataverse.
    /// </summary>
    public async Task<Guid> CreateDocumentAsync(
        string documentName,
        string containerId,
        string driveItemId,
        long fileSize,
        Guid parentRecordId,
        string parentLookupField,
        CancellationToken cancellationToken = default)
    {
        var entity = new Entity("sprk_document")
        {
            ["sprk_documentname"] = documentName,
            ["sprk_filename"] = documentName,
            ["sprk_graphdriveid"] = containerId,
            ["sprk_graphitemid"] = driveItemId,
            ["sprk_filesize"] = fileSize,
            [parentLookupField] = new EntityReference("sprk_matter", parentRecordId)
        };

        try
        {
            var documentId = await _serviceClient.CreateAsync(entity, cancellationToken);

            _logger.LogInformation(
                "Created document {DocumentId} for file {FileName}",
                documentId, documentName);

            return documentId;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create document record for {FileName}",
                documentName);
            throw;
        }
    }

    /// <summary>
    /// Get document by ID.
    /// </summary>
    public async Task<Entity?> GetDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var query = new QueryExpression("sprk_document")
        {
            ColumnSet = new ColumnSet(true),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("sprk_documentid", ConditionOperator.Equal, documentId)
                }
            },
            TopCount = 1
        };

        try
        {
            var results = await _serviceClient.RetrieveMultipleAsync(query, cancellationToken);
            return results.Entities.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve document {DocumentId}", documentId);
            throw;
        }
    }

    /// <summary>
    /// Test Dataverse connection.
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var whoAmI = await _serviceClient.ExecuteAsync(
                new Microsoft.Crm.Sdk.Messages.WhoAmIRequest());

            _logger.LogInformation("Dataverse connection test successful");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dataverse connection test failed");
            return false;
        }
    }
}
```

### Registration Pattern

**File Location**: `src/api/Spe.Bff.Api/Extensions/DataverseModuleExtensions.cs`

```csharp
namespace Spe.Bff.Api.Extensions;

public static class DataverseModuleExtensions
{
    public static IServiceCollection AddDataverseModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register Dataverse client as Singleton for connection pooling
        services.AddSingleton<DataverseServiceClientImpl>();

        // Register access data source as Scoped for per-request queries
        services.AddScoped<IAccessDataSource, DataverseAccessDataSource>();

        return services;
    }
}
```

---

## 9. Background Service Pattern

### Service Bus Processor Pattern

**File Location**: `src/api/Spe.Bff.Api/Services/Jobs/DocumentEventProcessor.cs`

```csharp
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace Spe.Bff.Api.Services.Jobs;

/// <summary>
/// Background service that processes document events from Service Bus.
/// Implements ADR-004 pattern for background processing.
/// </summary>
public class DocumentEventProcessor : BackgroundService
{
    private readonly ServiceBusProcessor _processor;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DocumentEventProcessor> _logger;

    public DocumentEventProcessor(
        IOptions<ServiceBusOptions> options,
        IServiceProvider serviceProvider,
        ILogger<DocumentEventProcessor> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        var connectionString = options.Value.ConnectionString;
        var queueName = "document-events";

        var client = new ServiceBusClient(connectionString);
        _processor = client.CreateProcessor(queueName, new ServiceBusProcessorOptions
        {
            AutoCompleteMessages = false,
            MaxConcurrentCalls = 5,
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5)
        });

        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DocumentEventProcessor starting");

        await _processor.StartProcessingAsync(stoppingToken);

        _logger.LogInformation("DocumentEventProcessor started");

        // Keep running until cancellation
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var messageBody = args.Message.Body.ToString();

        _logger.LogInformation(
            "Processing message {MessageId} from queue",
            args.Message.MessageId);

        try
        {
            // Create scope for scoped services
            using var scope = _serviceProvider.CreateScope();

            // Resolve handler from scope
            var handler = scope.ServiceProvider
                .GetRequiredService<DocumentProcessingJobHandler>();

            // Process message
            await handler.HandleAsync(messageBody, args.CancellationToken);

            // Complete message
            await args.CompleteMessageAsync();

            _logger.LogInformation(
                "Successfully processed message {MessageId}",
                args.Message.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error processing message {MessageId}",
                args.Message.MessageId);

            // Dead letter the message after max retry attempts
            if (args.Message.DeliveryCount >= 3)
            {
                await args.DeadLetterMessageAsync(
                    "Max delivery count exceeded",
                    ex.Message);
            }
            else
            {
                // Abandon to retry
                await args.AbandonMessageAsync();
            }
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(
            args.Exception,
            "Service Bus processor error in {EntityPath}: {ErrorSource}",
            args.EntityPath,
            args.ErrorSource);

        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DocumentEventProcessor stopping");

        await _processor.StopProcessingAsync(cancellationToken);
        await _processor.DisposeAsync();

        _logger.LogInformation("DocumentEventProcessor stopped");

        await base.StopAsync(cancellationToken);
    }
}
```

---

## 10. Logging Pattern

### Structured Logging with Log Levels

```csharp
public class SomeService
{
    private readonly ILogger<SomeService> _logger;

    public async Task PerformOperationAsync(string resourceId, string userId)
    {
        // Debug: Detailed information for debugging
        _logger.LogDebug(
            "Starting operation for resource {ResourceId} by user {UserId}",
            resourceId, userId);

        // Information: Track normal flow
        _logger.LogInformation(
            "Processing resource {ResourceId}",
            resourceId);

        try
        {
            // ... operation logic ...

            // Information: Successful completion
            _logger.LogInformation(
                "Operation completed successfully for resource {ResourceId}, Duration: {Duration}ms",
                resourceId, elapsed.TotalMilliseconds);
        }
        catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Warning: Expected error that doesn't indicate a problem with the application
            _logger.LogWarning(
                "Resource not found: {ResourceId}, Message: {Message}",
                resourceId, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            // Error: Unexpected error that indicates a problem
            _logger.LogError(
                ex,
                "Operation failed for resource {ResourceId}",
                resourceId);
            throw;
        }
    }
}
```

### Log Scopes for Correlation

```csharp
public async Task<IResult> HandleRequestAsync(
    string requestId,
    SomeService service,
    ILogger<EndpointClass> logger)
{
    // Create log scope with correlation ID
    using var scope = logger.BeginScope(new Dictionary<string, object>
    {
        ["RequestId"] = requestId,
        ["Timestamp"] = DateTime.UtcNow
    });

    logger.LogInformation("Request started");

    try
    {
        var result = await service.PerformOperationAsync(requestId);

        logger.LogInformation("Request completed successfully");

        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Request failed");
        return Results.Problem();
    }
}
```

---

## Summary: Quick Reference

| Pattern | File Location | Key Points |
|---------|--------------|------------|
| **Minimal API Endpoints** | `Api/OBOEndpoints.cs` | Group routes, inject concrete classes, structured error handling |
| **DTOs** | `Models/FileUploadResult.cs` | Never expose Graph SDK types, always map to DTOs |
| **Authorization** | `Infrastructure/Authorization/ResourceAccessHandler.cs` | Resource-based, query Dataverse for permissions |
| **Error Handling** | All endpoints | Map ServiceException by status code, structured Problem Details |
| **Configuration** | `Configuration/DataverseOptions.cs` | Options pattern with validation, fail-fast on startup |
| **Graph Factory** | `Infrastructure/Graph/GraphClientFactory.cs` | OBO with caching, app-only for admin operations |
| **Token Caching** | `Services/GraphTokenCache.cs` | Redis-first, SHA256 hashing, 55-minute TTL |
| **Dataverse** | `Spaarke.Dataverse/DataverseServiceClientImpl.cs` | Singleton for pooling, client secret auth, async operations |
| **Background Services** | `Services/Jobs/DocumentEventProcessor.cs` | Service Bus processor, scoped service resolution |
| **Logging** | All services | Structured logging, appropriate log levels, correlation |

---

**Related Documents**:
- [ARCHITECTURAL-DECISIONS.md](./ARCHITECTURAL-DECISIONS.md) - ADR details and enforcement rules
- [TARGET-ARCHITECTURE.md](./TARGET-ARCHITECTURE.md) - Target architecture diagrams
- [SDAP-ARCHITECTURE-OVERVIEW-V2-2025-10-13-2213.md](../../SDAP-ARCHITECTURE-OVERVIEW-V2-2025-10-13-2213.md) - Complete architecture reference

---

**Last Review**: 2025-10-13 by Architecture Team
**Next Review**: After Phase 1 implementation
