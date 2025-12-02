# Task 4.2: Testing Improvements - Integration Tests for Real Dependencies

**Priority:** HIGH (Sprint 3, Phase 4)
**Estimated Effort:** 4-5 days
**Status:** CRITICAL FOR QUALITY
**Dependencies:** Task 2.1 (OboSpeService real implementation), Task 1.2 (Configuration)

---

## Context & Problem Statement

The current test suite has **critical gaps** that undermine confidence in the system:

**Problems**:
1. **Tests Assert Placeholder Logic**: Tests validate mock/sample data generation instead of real integrations
2. **No Real Graph API Tests**: No integration tests against actual SharePoint Embedded containers
3. **No Dataverse Integration Tests**: Dataverse operations untested with real environment
4. **Missing Failure Scenario Tests**: No tests for 429 (throttling), 403 (forbidden), 5xx errors
5. **Unit Tests Mock Everything**: Even interfaces that should use real implementations
6. **No WireMock for HTTP Simulation**: Can't test retry logic, error handling without real API calls

This creates **false confidence** - tests pass but production fails.

---

## Goals & Outcomes

### Primary Goals
1. Add WireMock integration tests for Graph API and Dataverse Web API
2. Test failure scenarios: 429 throttling, 403 forbidden, 404 not found, 5xx errors
3. Add integration tests against test SPE container and test Dataverse environment
4. Replace tests that assert mock data with tests that verify real behavior
5. Achieve 70%+ code coverage with meaningful tests (not just mocking)

### Success Criteria
- [ ] WireMock tests for Graph API operations (list, download, upload, delete)
- [ ] WireMock tests for Dataverse Web API (CRUD operations)
- [ ] Integration tests against test SPE container
- [ ] Integration tests against test Dataverse environment
- [ ] Tests for retry logic (429 handling with Retry-After)
- [ ] Tests for circuit breaker behavior
- [ ] Tests for timeout scenarios
- [ ] Code coverage > 70% (excluding generated code)
- [ ] No tests asserting mock/sample data generation

### Non-Goals
- 100% code coverage (diminishing returns)
- UI/E2E tests (Sprint 4+)
- Performance/load tests (Sprint 4+)
- Chaos engineering (Sprint 4+)

---

## Architecture & Design

### Current State (Sprint 2) - Weak Tests
```
┌──────────────────────┐
│  Unit Tests          │
│  (Mock Everything)   │
│                      │
│ - Mock IGraphClient  │
│ - Mock IDataverse    │
│ - Assert sample data │
│ - No failure tests   │
└──────────────────────┘

❌ No integration tests
❌ No WireMock
❌ Tests pass but production fails
```

### Target State (Sprint 3) - Robust Tests
```
┌──────────────────────────────────┐
│  Unit Tests (Focused)            │
│  - Test business logic only      │
│  - Mock at seam boundaries       │
│  - Fast (<1s per test)           │
└──────────────────────────────────┘
           │
           v
┌──────────────────────────────────┐
│  WireMock Integration Tests      │
│  - Simulate Graph API responses  │
│  - Test retry logic (429)        │
│  - Test error handling (403,404) │
│  - Test circuit breaker          │
│  - Test timeout behavior         │
└──────────────────────────────────┘
           │
           v
┌──────────────────────────────────┐
│  Real Integration Tests          │
│  - Test SPE container (optional) │
│  - Test Dataverse environment    │
│  - Validate end-to-end flows     │
│  - Run in CI/CD (feature flag)   │
└──────────────────────────────────┘
```

---

## Relevant ADRs

### ADR-010: DI Minimalism
- **Test at Seams**: Mock IGraphClientFactory, IDataverseService (interfaces)
- **Real Implementations**: Use real classes where possible

---

## Implementation Steps

### Step 1: Install Testing NuGet Packages

**File**: `tests/Spe.Bff.Api.Tests/Spe.Bff.Api.Tests.csproj`

```xml
<ItemGroup>
  <!-- Existing -->
  <PackageReference Include="xunit" Version="2.6.0" />
  <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4" />
  <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.0" />
  <PackageReference Include="FluentAssertions" Version="6.12.0" />

  <!-- New for integration testing -->
  <PackageReference Include="WireMock.Net" Version="1.5.45" />
  <PackageReference Include="Moq" Version="4.20.70" />
  <PackageReference Include="Testcontainers" Version="3.6.0" /> <!-- Optional: for Dataverse emulator -->
</ItemGroup>
```

---

### Step 2: Create WireMock Tests for Graph API

**New File**: `tests/Spe.Bff.Api.Tests/Integration/GraphApiWireMockTests.cs`

```csharp
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;
using FluentAssertions;
using Microsoft.Graph;
using System.Net;

namespace Spe.Bff.Api.Tests.Integration;

/// <summary>
/// Integration tests for Graph API operations using WireMock to simulate responses.
/// Tests retry logic, error handling, and various HTTP status codes.
/// </summary>
public class GraphApiWireMockTests : IDisposable
{
    private readonly WireMockServer _mockServer;
    private readonly HttpClient _httpClient;

    public GraphApiWireMockTests()
    {
        _mockServer = WireMockServer.Start();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_mockServer.Urls[0])
        };
    }

    [Fact]
    public async Task ListChildren_Success_ReturnsItems()
    {
        // Arrange
        var driveId = "test-drive-id";
        _mockServer
            .Given(Request.Create()
                .WithPath($"/drives/{driveId}/root/children")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(@"{
                    ""value"": [
                        {
                            ""id"": ""item-1"",
                            ""name"": ""Document1.txt"",
                            ""size"": 1024,
                            ""lastModifiedDateTime"": ""2024-01-01T00:00:00Z""
                        },
                        {
                            ""id"": ""item-2"",
                            ""name"": ""Document2.pdf"",
                            ""size"": 2048,
                            ""lastModifiedDateTime"": ""2024-01-02T00:00:00Z""
                        }
                    ]
                }"));

        // Act
        var response = await _httpClient.GetAsync($"/drives/{driveId}/root/children");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Document1.txt");
        content.Should().Contain("Document2.pdf");
    }

    [Fact]
    public async Task ListChildren_Throttled_RetriesWithBackoff()
    {
        // Arrange
        var driveId = "test-drive-id";
        var callCount = 0;

        _mockServer
            .Given(Request.Create()
                .WithPath($"/drives/{driveId}/root/children")
                .UsingGet())
            .RespondWith(request =>
            {
                callCount++;
                if (callCount <= 2)
                {
                    // First 2 calls return 429
                    return Response.Create()
                        .WithStatusCode(429)
                        .WithHeader("Retry-After", "2");
                }
                else
                {
                    // Third call succeeds
                    return Response.Create()
                        .WithStatusCode(200)
                        .WithBody(@"{""value"": []}");
                }
            });

        // Act
        // Note: This requires GraphHttpMessageHandler with retry logic
        // Test will pass once Task 4.1 is complete
        var response = await _httpClient.GetAsync($"/drives/{driveId}/root/children");

        // Assert
        callCount.Should().Be(3); // 2 retries + 1 success
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DownloadContent_NotFound_Returns404()
    {
        // Arrange
        var driveId = "test-drive-id";
        var itemId = "non-existent-item";

        _mockServer
            .Given(Request.Create()
                .WithPath($"/drives/{driveId}/items/{itemId}/content")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(404)
                .WithBody(@"{
                    ""error"": {
                        ""code"": ""itemNotFound"",
                        ""message"": ""The resource could not be found.""
                    }
                }"));

        // Act
        var response = await _httpClient.GetAsync($"/drives/{driveId}/items/{itemId}/content");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UploadSmall_Forbidden_Returns403()
    {
        // Arrange
        var driveId = "test-drive-id";
        var path = "/test/file.txt";

        _mockServer
            .Given(Request.Create()
                .WithPath($"/drives/{driveId}/root:/{path}:/content")
                .UsingPut())
            .RespondWith(Response.Create()
                .WithStatusCode(403)
                .WithBody(@"{
                    ""error"": {
                        ""code"": ""accessDenied"",
                        ""message"": ""Access denied""
                    }
                }"));

        // Act
        var content = new ByteArrayContent(new byte[] { 1, 2, 3 });
        var response = await _httpClient.PutAsync($"/drives/{driveId}/root:/{path}:/content", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteItem_Success_Returns204()
    {
        // Arrange
        var driveId = "test-drive-id";
        var itemId = "item-to-delete";

        _mockServer
            .Given(Request.Create()
                .WithPath($"/drives/{driveId}/items/{itemId}")
                .UsingDelete())
            .RespondWith(Response.Create()
                .WithStatusCode(204)); // No Content

        // Act
        var response = await _httpClient.DeleteAsync($"/drives/{driveId}/items/{itemId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DownloadContent_RangeRequest_ReturnsPartialContent()
    {
        // Arrange
        var driveId = "test-drive-id";
        var itemId = "large-file";
        var fileContent = "This is a test file with some content for range testing";
        var rangeStart = 0;
        var rangeEnd = 9;

        _mockServer
            .Given(Request.Create()
                .WithPath($"/drives/{driveId}/items/{itemId}/content")
                .WithHeader("Range", $"bytes={rangeStart}-{rangeEnd}")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(206) // Partial Content
                .WithHeader("Content-Range", $"bytes {rangeStart}-{rangeEnd}/{fileContent.Length}")
                .WithBody(fileContent.Substring(rangeStart, rangeEnd - rangeStart + 1)));

        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, $"/drives/{driveId}/items/{itemId}/content");
        request.Headers.Add("Range", $"bytes={rangeStart}-{rangeEnd}");
        var response = await _httpClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("This is a ");
    }

    public void Dispose()
    {
        _mockServer?.Stop();
        _mockServer?.Dispose();
        _httpClient?.Dispose();
    }
}
```

---

### Step 3: Create WireMock Tests for Dataverse Web API

**New File**: `tests/Spe.Bff.Api.Tests/Integration/DataverseWebApiWireMockTests.cs`

```csharp
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;
using FluentAssertions;
using System.Net;

namespace Spe.Bff.Api.Tests.Integration;

public class DataverseWebApiWireMockTests : IDisposable
{
    private readonly WireMockServer _mockServer;
    private readonly HttpClient _httpClient;

    public DataverseWebApiWireMockTests()
    {
        _mockServer = WireMockServer.Start();
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_mockServer.Urls[0])
        };
    }

    [Fact]
    public async Task CreateDocument_Success_ReturnsEntityId()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        _mockServer
            .Given(Request.Create()
                .WithPath("/api/data/v9.2/sprk_documents")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("OData-EntityId", $"https://test.crm.dynamics.com/api/data/v9.2/sprk_documents({documentId})"));

        // Act
        var payload = new { sprk_documentname = "Test Document" };
        var response = await _httpClient.PostAsJsonAsync("/api/data/v9.2/sprk_documents", payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Should().ContainKey("OData-EntityId");
    }

    [Fact]
    public async Task GetDocument_NotFound_Returns404()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/data/v9.2/sprk_documents({documentId})")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(404));

        // Act
        var response = await _httpClient.GetAsync($"/api/data/v9.2/sprk_documents({documentId})");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateDocument_Success_Returns204()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/data/v9.2/sprk_documents({documentId})")
                .UsingPatch())
            .RespondWith(Response.Create()
                .WithStatusCode(204));

        // Act
        var updates = new { sprk_documentname = "Updated Name" };
        var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"/api/data/v9.2/sprk_documents({documentId})")
        {
            Content = JsonContent.Create(updates)
        };
        var response = await _httpClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteDocument_Success_Returns204()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        _mockServer
            .Given(Request.Create()
                .WithPath($"/api/data/v9.2/sprk_documents({documentId})")
                .UsingDelete())
            .RespondWith(Response.Create()
                .WithStatusCode(204));

        // Act
        var response = await _httpClient.DeleteAsync($"/api/data/v9.2/sprk_documents({documentId})");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    public void Dispose()
    {
        _mockServer?.Stop();
        _mockServer?.Dispose();
        _httpClient?.Dispose();
    }
}
```

---

### Step 4: Create Real Integration Tests (Optional, CI-Gated)

**New File**: `tests/Spe.Bff.Api.Tests/Integration/RealSpeIntegrationTests.cs`

```csharp
using Xunit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Spe.Bff.Api.Tests.Integration;

/// <summary>
/// Integration tests against real SharePoint Embedded container.
/// Requires test container configured in appsettings.Test.json.
/// Run only in CI/CD or when explicitly enabled.
/// </summary>
[Collection("RealIntegration")]
[Trait("Category", "Integration")]
public class RealSpeIntegrationTests : IClassFixture<IntegrationTestFixture>
{
    private readonly IntegrationTestFixture _fixture;
    private readonly bool _runRealTests;

    public RealSpeIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _runRealTests = fixture.Configuration.GetValue<bool>("IntegrationTests:RunRealTests", false);
    }

    [SkippableFact]
    public async Task CreateContainer_ValidInput_CreatesContainer()
    {
        Skip.IfNot(_runRealTests, "Real integration tests disabled");

        // Arrange
        var containerTypeId = _fixture.Configuration["IntegrationTests:ContainerTypeId"];
        var displayName = $"Test Container {Guid.NewGuid()}";

        // Act
        var container = await _fixture.SpeFileStore.CreateContainerAsync(
            Guid.Parse(containerTypeId),
            displayName,
            "Test container created by integration test");

        // Assert
        container.Should().NotBeNull();
        container!.Id.Should().NotBeNullOrEmpty();
        container.DisplayName.Should().Be(displayName);

        // Cleanup
        // TODO: Delete test container
    }

    [SkippableFact]
    public async Task UploadDownloadDelete_EndToEnd_WorksCorrectly()
    {
        Skip.IfNot(_runRealTests, "Real integration tests disabled");

        // Arrange
        var driveId = _fixture.Configuration["IntegrationTests:TestDriveId"];
        var testFileName = $"test-{Guid.NewGuid()}.txt";
        var testContent = "This is a test file"u8.ToArray();

        // Act 1: Upload
        using var uploadStream = new MemoryStream(testContent);
        var uploadedItem = await _fixture.SpeFileStore.UploadSmallAsync(driveId, testFileName, uploadStream);

        uploadedItem.Should().NotBeNull();
        var itemId = uploadedItem!.Id;

        // Act 2: Download
        var downloadedStream = await _fixture.SpeFileStore.DownloadContentAsync(driveId, itemId);
        downloadedStream.Should().NotBeNull();

        using var ms = new MemoryStream();
        await downloadedStream!.CopyToAsync(ms);
        var downloadedContent = ms.ToArray();

        // Act 3: Delete
        var deleted = await _fixture.SpeFileStore.DeleteItemAsync(driveId, itemId);

        // Assert
        downloadedContent.Should().Equal(testContent);
        deleted.Should().BeTrue();
    }
}
```

---

### Step 5: Replace Tests Asserting Mock Data

**Find and Fix**:
```bash
# Search for tests asserting GenerateSampleItems or mock data
rg "GenerateSampleItems|GenerateSampleFileContent" tests/ --type cs
```

**Example Fix**:
```csharp
// BAD: Testing mock data generation
[Fact]
public async Task ListChildren_ReturnsGeneratedItems()
{
    var items = GenerateSampleItems(10);
    items.Should().HaveCount(10);
}

// GOOD: Testing real behavior with mocked dependencies
[Fact]
public async Task ListChildren_CallsGraphApi_ReturnsMappedItems()
{
    // Arrange
    var mockFactory = new Mock<IGraphClientFactory>();
    var mockGraphClient = new Mock<GraphServiceClient>();
    // ... setup mock to return real DriveItem objects

    var service = new OboSpeService(mockFactory.Object, ...);

    // Act
    var result = await service.ListChildrenAsync("user-token", "container-id", new ListingParameters());

    // Assert
    result.Items.Should().NotBeEmpty();
    mockGraphClient.Verify(x => x.Drives["drive-id"].Root.Children.GetAsync(...), Times.Once);
}
```

---

### Step 6: Add Test Configuration

**New File**: `tests/Spe.Bff.Api.Tests/appsettings.Test.json`

```json
{
  "IntegrationTests": {
    "RunRealTests": false,
    "ContainerTypeId": "your-test-container-type-id",
    "TestDriveId": "your-test-drive-id"
  },
  "WireMock": {
    "Enabled": true
  }
}
```

**CI/CD Configuration** (enable real tests in pipeline):
```yaml
# azure-pipelines.yml
- task: DotNetCoreCLI@2
  displayName: 'Run Integration Tests'
  env:
    IntegrationTests__RunRealTests: true
    IntegrationTests__ContainerTypeId: $(TEST_CONTAINER_TYPE_ID)
    IntegrationTests__TestDriveId: $(TEST_DRIVE_ID)
  inputs:
    command: test
    arguments: '--filter Category=Integration'
```

---

## AI Coding Prompts

### Prompt 1: Create WireMock Tests for Graph API
```
Create integration tests using WireMock to simulate Microsoft Graph API:

Context:
- Need to test Graph API operations without hitting real API
- Test retry logic (429), error handling (403, 404, 5xx)
- Validate HTTP behavior (range requests, headers)

Requirements:
1. Create GraphApiWireMockTests class
2. Setup WireMock server in constructor
3. Test success scenarios (200 OK)
4. Test error scenarios (404, 403, 429, 5xx)
5. Test retry behavior (multiple 429 then success)
6. Test range request handling (206 Partial Content)
7. Dispose WireMock server properly

Code Quality:
- Senior C# developer standards
- Use FluentAssertions for readable assertions
- Follow AAA pattern (Arrange-Act-Assert)
- Clear test names describing scenario

Files to Create:
- tests/Spe.Bff.Api.Tests/Integration/GraphApiWireMockTests.cs

NuGet: WireMock.Net 1.5.45
```

### Prompt 2: Create WireMock Tests for Dataverse Web API
```
Create integration tests using WireMock for Dataverse REST API:

Context:
- Test CRUD operations (Create, Read, Update, Delete)
- Validate OData response format
- Test error handling

Requirements:
1. Create DataverseWebApiWireMockTests class
2. Test POST /sprk_documents (201 Created with OData-EntityId header)
3. Test GET /sprk_documents(guid) (200 OK, 404 Not Found)
4. Test PATCH /sprk_documents(guid) (204 No Content)
5. Test DELETE /sprk_documents(guid) (204 No Content)
6. Mock OData response format

Code Quality:
- FluentAssertions
- AAA pattern
- Dispose WireMock server

Files to Create:
- tests/Spe.Bff.Api.Tests/Integration/DataverseWebApiWireMockTests.cs
```

### Prompt 3: Replace Mock Data Tests with Real Behavior Tests
```
Find and replace tests that assert mock data generation:

Context:
- Current tests assert GenerateSampleItems() output
- Need tests that verify real Graph SDK calls
- Mock at seam boundaries (IGraphClientFactory)

Requirements:
1. Search for tests using GenerateSampleItems, GenerateSampleFileContent
2. Replace with tests that mock IGraphClientFactory
3. Verify real Graph SDK method calls (using Moq.Verify)
4. Assert business logic, not mock data structure
5. Remove all mock data generator calls from tests

Search:
- rg "GenerateSampleItems|GenerateSampleFileContent" tests/ --type cs

Files to Modify:
- All test files in tests/Spe.Bff.Api.Tests/
```

---

## Testing Strategy

### Test Pyramid
1. **Unit Tests** (70%): Fast, focused on business logic
2. **WireMock Integration Tests** (20%): HTTP behavior without real API
3. **Real Integration Tests** (10%): End-to-end with real services (CI only)

---

## Validation Checklist

Before marking this task complete, verify:

- [ ] WireMock tests created for Graph API operations
- [ ] WireMock tests created for Dataverse Web API
- [ ] Tests for retry logic (429 handling)
- [ ] Tests for error scenarios (403, 404, 5xx)
- [ ] Real integration tests created (optional, CI-gated)
- [ ] Tests asserting mock data replaced with real behavior tests
- [ ] Code coverage > 70%
- [ ] All tests pass
- [ ] Test configuration added (appsettings.Test.json)
- [ ] CI/CD pipeline updated to run integration tests

---

## Completion Criteria

Task is complete when:
1. WireMock tests validate HTTP behavior
2. Mock data tests replaced
3. Real integration tests created (optional)
4. Code coverage > 70%
5. All tests pass
6. CI/CD runs integration tests
7. Code review approved

**Estimated Completion: 4-5 days**

---

## Benefits

1. **Confidence**: Tests validate real behavior, not mocks
2. **Fast Feedback**: WireMock tests run quickly (<5s)
3. **Failure Scenarios**: Tests cover error handling
4. **CI/CD Safety**: Integration tests catch regressions
5. **Maintainability**: Tests document expected behavior
