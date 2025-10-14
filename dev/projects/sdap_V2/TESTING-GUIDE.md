# Testing Strategy for Refactoring

## Test-First Principle
Before making any change:
1. Ensure existing tests pass
2. Add new tests if coverage gaps exist
3. Make refactoring changes
4. Ensure tests still pass (update mocks if needed)
5. Commit

## Unit Test Patterns

### Testing Services Without Interfaces
```csharp
// Before refactoring: Mocked interface
var mockStore = new Mock<IResourceStore>();
mockStore.Setup(x => x.UploadAsync(...)).ReturnsAsync(...);

// After refactoring: Real service with mocked dependencies
public class SpeFileStoreTests
{
    private readonly Mock<IGraphClientFactory> _mockGraphFactory;
    private readonly SpeFileStore _fileStore;
    
    public SpeFileStoreTests()
    {
        _mockGraphFactory = new Mock<IGraphClientFactory>();
        _fileStore = new SpeFileStore(_mockGraphFactory.Object);
    }
    
    [Fact]
    public async Task UploadFileAsync_ValidFile_ReturnsFileHandle()
    {
        // Arrange
        var mockGraphClient = CreateMockGraphClient();
        _mockGraphFactory
            .Setup(x => x.CreateOnBehalfOfClientAsync(It.IsAny<string>()))
            .ReturnsAsync(mockGraphClient);
        
        // Act
        var result = await _fileStore.UploadFileAsync(...);
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal("test.pdf", result.Name);
    }
}
Testing Endpoints
csharppublic class OBOEndpointsTests
{
    [Fact]
    public async Task UploadFile_ValidRequest_Returns200()
    {
        // Arrange
        var mockFileStore = new Mock<SpeFileStore>(...);
        mockFileStore.Setup(x => x.UploadFileAsync(...)).ReturnsAsync(new FileHandleDto { ... });
        
        var mockRequest = CreateMockHttpRequest();
        var mockLogger = new Mock<ILogger<OBOEndpoints>>();
        
        // Act
        var result = await OBOEndpoints.UploadFile(...);
        
        // Assert
        var okResult = Assert.IsType<Ok<FileHandleDto>>(result);
        Assert.NotNull(okResult.Value);
    }
}
Integration Test Patterns
Testing with Real Redis
csharp[Collection("Redis")]
public class GraphTokenCacheIntegrationTests : IAsyncLifetime
{
    private readonly IDistributedCache _cache;
    
    public GraphTokenCacheIntegrationTests()
    {
        var services = new ServiceCollection();
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = "localhost:6379";
            options.InstanceName = "test:";
        });
        _cache = services.BuildServiceProvider().GetRequiredService<IDistributedCache>();
    }
    
    [Fact]
    public async Task SetToken_ThenGetToken_ReturnsToken()
    {
        var tokenCache = new GraphTokenCache(_cache);
        
        await tokenCache.SetTokenAsync("hash123", "token456", TimeSpan.FromMinutes(5));
        var retrieved = await tokenCache.GetTokenAsync("hash123");
        
        Assert.Equal("token456", retrieved);
    }
    
    public async Task InitializeAsync()
    {
        // Clear test keys
        await _cache.RemoveAsync("test:graph:token:hash123");
    }
}
Manual Testing Checklist
After Phase 1
bash# Test configuration changes
curl -X GET https://localhost:5001/healthz/dataverse \
  -H "Authorization: Bearer {token}"

# Expected: 200 OK
After Phase 2
bash# Test file upload
curl -X PUT https://localhost:5001/api/obo/containers/{containerId}/files/test.pdf \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/pdf" \
  --data-binary @test.pdf

# Expected: 200 OK with FileHandleDto JSON

# Test file download
curl -X GET https://localhost:5001/api/obo/drives/{driveId}/items/{itemId}/content \
  -H "Authorization: Bearer {token}" \
  -o downloaded.pdf

# Expected: 200 OK with file content
After Phase 4
bash# Test token caching
# First request (should see OBO exchange in logs)
curl -X PUT https://localhost:5001/api/obo/containers/{containerId}/files/test1.pdf \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/pdf" \
  --data-binary @test1.pdf

# Second request (should see cache hit in logs)
curl -X PUT https://localhost:5001/api/obo/containers/{containerId}/files/test2.pdf \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/pdf" \
  --data-binary @test2.pdf

# Check Redis
redis-cli
> KEYS sdap:graph:token:*
> TTL sdap:graph:token:{hash}

---
