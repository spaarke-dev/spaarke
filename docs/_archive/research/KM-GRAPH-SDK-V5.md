# Microsoft Graph SDK v5 Documentation for Spaarke

## Overview
This guide covers Microsoft Graph SDK v5 usage patterns for SharePoint Embedded (SPE) operations in the Spaarke platform, aligned with ADR-007 (SPE storage seam minimalism).

## SDK Setup and Configuration

### NuGet Packages
```xml
<PackageReference Include="Microsoft.Graph" Version="5.48.0" />
<PackageReference Include="Azure.Identity" Version="1.11.0" />
<PackageReference Include="Microsoft.Graph.Core" Version="3.1.0" />
```

### Dependency Injection Setup
```csharp
// Program.cs - Configure Graph SDK per ADR-007
builder.Services.AddSingleton<GraphServiceClient>(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    
    // Use managed identity for app-only calls
    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ManagedIdentityClientId = configuration["ManagedIdentity:ClientId"]
    });
    
    // Configure retry handler and telemetry
    var handlers = GraphClientFactory.CreateDefaultHandlers(new DelegateAuthenticationProvider(
        async (requestMessage) =>
        {
            var token = await credential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://graph.microsoft.com/.default" }));
            requestMessage.Headers.Authorization = 
                new AuthenticationHeaderValue("Bearer", token.Token);
        }));
    
    // Add correlation handler
    handlers.Add(new CorrelationHandler());
    
    var httpClient = GraphClientFactory.Create(handlers);
    
    return new GraphServiceClient(httpClient);
});

// Correlation handler for request tracking
public class CorrelationHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();
        request.Headers.Add("client-request-id", correlationId);
        
        var response = await base.SendAsync(request, cancellationToken);
        
        if (response.Headers.TryGetValues("request-id", out var requestIds))
        {
            Activity.Current?.SetTag("graph.request-id", requestIds.FirstOrDefault());
        }
        
        return response;
    }
}
```

## Authentication Patterns

### On-Behalf-Of (OBO) Flow for User Context
```csharp
public class GraphOboService
{
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;
    
    public async Task<GraphServiceClient> GetUserGraphClientAsync()
    {
        var userToken = _httpContextAccessor.HttpContext?
            .Request.Headers["Authorization"]
            .ToString().Replace("Bearer ", "");
        
        if (string.IsNullOrEmpty(userToken))
            throw new UnauthorizedAccessException("No user token found");
        
        // Exchange user token for Graph token via OBO
        var confidentialClient = ConfidentialClientApplicationBuilder
            .Create(_configuration["AzureAd:ClientId"])
            .WithClientSecret(_configuration["AzureAd:ClientSecret"])
            .WithAuthority($"{_configuration["AzureAd:Instance"]}{_configuration["AzureAd:TenantId"]}")
            .Build();
        
        var result = await confidentialClient
            .AcquireTokenOnBehalfOf(
                new[] { "https://graph.microsoft.com/.default" },
                new UserAssertion(userToken))
            .ExecuteAsync();
        
        var authProvider = new DelegateAuthenticationProvider(async (requestMessage) =>
        {
            requestMessage.Headers.Authorization = 
                new AuthenticationHeaderValue("Bearer", result.AccessToken);
        });
        
        return new GraphServiceClient(authProvider);
    }
}
```

### Managed Identity for App-Only Context
```csharp
public class GraphAppOnlyService
{
    private readonly GraphServiceClient _graphClient;
    
    public GraphAppOnlyService(GraphServiceClient graphClient)
    {
        _graphClient = graphClient; // Injected singleton
    }
    
    public async Task<Drive> GetSpeContainerAsync(string containerId)
    {
        try
        {
            return await _graphClient.Drives[containerId]
                .GetAsync(config =>
                {
                    config.QueryParameters.Select = new[] { "id", "name", "quota" };
                });
        }
        catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // SDK retry handler should handle this, but log if it bubbles up
            throw new InvalidOperationException(
                $"Graph throttled after retries: {ex.ResponseHeaders?.RetryAfter}", ex);
        }
    }
}
```

## SharePoint Embedded Operations

### File Upload with Resumable Sessions
```csharp
public class SpeFileStore
{
    private readonly GraphServiceClient _graphClient;
    private const int ChunkSize = 320 * 1024 * 10; // 3.2 MB chunks
    
    public async Task<UploadSessionDto> CreateUploadSessionAsync(
        string containerId, string fileName, long fileSize)
    {
        var uploadSession = new DriveItemUploadableProperties
        {
            Name = fileName,
            AdditionalData = new Dictionary<string, object>
            {
                { "@microsoft.graph.conflictBehavior", "rename" }
            }
        };
        
        var session = await _graphClient
            .Drives[containerId]
            .Root
            .ItemWithPath(fileName)
            .CreateUploadSession
            .PostAsync(new CreateUploadSessionPostRequestBody
            {
                Item = uploadSession
            });
        
        return new UploadSessionDto
        {
            UploadUrl = session.UploadUrl,
            ExpirationDateTime = session.ExpirationDateTime,
            NextExpectedRanges = session.NextExpectedRanges
        };
    }
    
    public async Task<DriveItem> UploadLargeFileAsync(
        Stream fileStream, UploadSessionDto session, IProgress<long> progress = null)
    {
        var uploadTask = new LargeFileUploadTask<DriveItem>(
            new UploadSession 
            { 
                UploadUrl = session.UploadUrl,
                ExpirationDateTime = session.ExpirationDateTime
            },
            fileStream,
            ChunkSize);
        
        // Upload with progress reporting
        var uploadResult = await uploadTask.UploadAsync(progress);
        
        if (!uploadResult.UploadSucceeded)
        {
            throw new InvalidOperationException(
                $"Upload failed at {uploadResult.ItemResponse?.Size ?? 0} bytes");
        }
        
        return uploadResult.ItemResponse;
    }
}
```

### File Download with Range Support
```csharp
public async Task<Stream> DownloadFileAsync(string driveId, string itemId, 
    long? rangeStart = null, long? rangeEnd = null)
{
    var requestInfo = _graphClient
        .Drives[driveId]
        .Items[itemId]
        .Content
        .ToGetRequestInformation();
    
    // Add range header for partial downloads
    if (rangeStart.HasValue || rangeEnd.HasValue)
    {
        var rangeHeader = $"bytes={rangeStart ?? 0}-{rangeEnd?.ToString() ?? ""}";
        requestInfo.Headers.Add("Range", new[] { rangeHeader });
    }
    
    var response = await _graphClient.RequestAdapter.SendAsync<Stream>(
        requestInfo, 
        Stream.CreateFromStream);
    
    return response;
}
```

### Batch Operations
```csharp
public async Task<BatchResponseContent> BatchOperationsAsync(
    IEnumerable<(string id, HttpRequestMessage request)> requests)
{
    using var batchRequest = new BatchRequestContent();
    
    foreach (var (id, request) in requests)
    {
        batchRequest.AddBatchRequestStep(new BatchRequestStep(id, request));
    }
    
    var batchResponse = await _graphClient.Batch.PostAsync(batchRequest);
    
    return batchResponse;
}

// Usage example
public async Task<Dictionary<string, DriveItem>> GetMultipleFilesAsync(
    string driveId, IEnumerable<string> itemIds)
{
    var requests = itemIds.Select((id, index) => 
    {
        var request = _graphClient
            .Drives[driveId]
            .Items[id]
            .ToGetRequestInformation()
            .ToHttpRequestMessage();
        
        return (id, request);
    });
    
    var batchResponse = await BatchOperationsAsync(requests);
    var results = new Dictionary<string, DriveItem>();
    
    foreach (var itemId in itemIds)
    {
        var response = await batchResponse.GetResponseByIdAsync<DriveItem>(itemId);
        if (response != null)
        {
            results[itemId] = response;
        }
    }
    
    return results;
}
```

### Delta Query for Change Tracking
```csharp
public async Task<(List<DriveItem> changes, string deltaToken)> GetChangesAsync(
    string driveId, string deltaToken = null)
{
    var changes = new List<DriveItem>();
    
    var deltaRequest = string.IsNullOrEmpty(deltaToken)
        ? _graphClient.Drives[driveId].Root.Delta.GetAsync()
        : _graphClient.Drives[driveId].Root.Delta
            .GetAsync(config => config.QueryParameters.Token = deltaToken);
    
    var deltaPage = await deltaRequest;
    
    while (deltaPage != null)
    {
        changes.AddRange(deltaPage.Value);
        
        if (deltaPage.OdataNextLink != null)
        {
            // Get next page
            deltaPage = await _graphClient.Drives[driveId].Root.Delta
                .WithUrl(deltaPage.OdataNextLink)
                .GetAsync();
        }
        else
        {
            // Extract delta token from final page
            var newDeltaToken = ExtractDeltaToken(deltaPage.OdataDeltaLink);
            return (changes, newDeltaToken);
        }
    }
    
    return (changes, null);
}

private string ExtractDeltaToken(string deltaLink)
{
    var uri = new Uri(deltaLink);
    var query = HttpUtility.ParseQueryString(uri.Query);
    return query["token"];
}
```

## Error Handling and Retry Logic

### Polly Integration for Advanced Retry
```csharp
public class GraphServiceWithPolly
{
    private readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy;
    
    public GraphServiceWithPolly()
    {
        _retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r => 
                r.StatusCode == HttpStatusCode.TooManyRequests ||
                r.StatusCode == HttpStatusCode.ServiceUnavailable)
            .WaitAndRetryAsync(
                3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    var logger = context.Values["logger"] as ILogger;
                    logger?.LogWarning(
                        "Graph API retry {RetryCount} after {Delay}ms",
                        retryCount, timespan.TotalMilliseconds);
                });
    }
    
    public async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation, ILogger logger)
    {
        var context = new Context { ["logger"] = logger };
        
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            try
            {
                return await operation();
            }
            catch (ServiceException ex)
            {
                logger.LogError(ex, 
                    "Graph API error: {Status} - {Code}", 
                    ex.StatusCode, ex.Error?.Code);
                throw;
            }
        });
    }
}
```

### Graph Error Response Handling
```csharp
public static class GraphErrorHandler
{
    public static ProblemDetails ToProblemDetails(ServiceException ex)
    {
        return new ProblemDetails
        {
            Title = "Graph API Error",
            Detail = ex.Error?.Message ?? ex.Message,
            Status = (int?)ex.StatusCode,
            Extensions = new Dictionary<string, object?>
            {
                ["error_code"] = ex.Error?.Code,
                ["request_id"] = ex.ResponseHeaders?["request-id"],
                ["date"] = ex.ResponseHeaders?["Date"],
                ["retry_after"] = ex.ResponseHeaders?.RetryAfter
            }
        };
    }
}
```

## Performance Optimization

### Request Optimization
```csharp
// Select only required fields
var driveItem = await _graphClient
    .Drives[driveId]
    .Items[itemId]
    .GetAsync(config =>
    {
        config.QueryParameters.Select = new[] { "id", "name", "size", "lastModifiedDateTime" };
        config.QueryParameters.Expand = new[] { "thumbnails" };
    });

// Use $top for pagination
var items = await _graphClient
    .Drives[driveId]
    .Root
    .Children
    .GetAsync(config =>
    {
        config.QueryParameters.Top = 20;
        config.QueryParameters.Orderby = new[] { "lastModifiedDateTime desc" };
    });
```

### Caching with ETags
```csharp
public async Task<(DriveItem item, bool cached)> GetWithETagAsync(
    string driveId, string itemId, string etag = null)
{
    var requestInfo = _graphClient
        .Drives[driveId]
        .Items[itemId]
        .ToGetRequestInformation();
    
    if (!string.IsNullOrEmpty(etag))
    {
        requestInfo.Headers.Add("If-None-Match", new[] { etag });
    }
    
    try
    {
        var item = await _graphClient.RequestAdapter
            .SendAsync(requestInfo, DriveItem.CreateFromDiscriminatorValue);
        return (item, false);
    }
    catch (ServiceException ex) when (ex.StatusCode == HttpStatusCode.NotModified)
    {
        return (null, true); // Content hasn't changed
    }
}
```

## Testing Graph SDK Code

```csharp
[Fact]
public async Task UploadFile_Success()
{
    // Arrange
    var mockResponse = new DriveItem { Id = "123", Name = "test.pdf" };
    var mockGraphClient = new Mock<GraphServiceClient>();
    
    mockGraphClient
        .Setup(x => x.Drives[It.IsAny<string>()]
            .Items[It.IsAny<string>()]
            .Content
            .PutAsync(It.IsAny<Stream>(), It.IsAny<Action<RequestConfiguration<DefaultQueryParameters>>>()))
        .ReturnsAsync(mockResponse);
    
    var service = new SpeFileStore(mockGraphClient.Object);
    
    // Act
    var result = await service.UploadFileAsync("drive123", "test.pdf", stream);
    
    // Assert
    Assert.Equal("123", result.Id);
}
```

## Key Graph SDK v5 Principles for Spaarke

1. **Use singleton GraphServiceClient** - Configure once with retry handlers
2. **Never expose Graph types above facade** - Per ADR-007
3. **Handle throttling gracefully** - Use SDK retry + Polly
4. **Optimize requests** - Use $select, $expand, $top
5. **Track all requests** - Add correlation IDs
6. **Cache with ETags** - Reduce unnecessary calls
7. **Use batch for multiple operations** - Reduce round trips
8. **Stream large files** - Use chunked upload/download