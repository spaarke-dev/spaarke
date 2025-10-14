# Phase 2 - Task 4: Update Test Mocking Strategy

**Phase**: 2 (Simplify Service Layer)
**Duration**: 2-3 hours
**Risk**: Medium
**Patterns**: Mock at infrastructure boundaries (IGraphClientFactory)
**Anti-Patterns**: [anti-pattern-interface-proliferation.md](../patterns/anti-pattern-interface-proliferation.md)

---

## Current State (Before Starting)

**Current Test Problem**:
- Tests mock service interfaces: `Mock<IResourceStore>`, `Mock<ISpeService>`
- After consolidation, these interfaces won't exist
- Tests too coupled to implementation (mock every layer)
- Hard to maintain: Change service = change interface + tests

**Test Impact**:
- Brittle tests: Break when refactoring (even if behavior unchanged)
- Mock explosion: Create mocks for every service layer
- Test doesn't match reality: Uses mocks, not real code paths
- Unclear test purpose: Testing mock setup or actual behavior?

**Quick Verification**:
```bash
# Check current test mocking
grep -rn "Mock<IResourceStore>\|Mock<ISpeService>" tests/

# Should see interface mocks
# If you see "Mock<IGraphClientFactory>" - task already done!
```

---

## Background: Why Tests Mock Every Layer

**Historical Context**:
- Testing tutorials: "Create interface for every class to make it testable"
- Unit testing purist approach: "Mock all dependencies"
- Assumption: "Can't test without mocking everything"
- Each service layer got interface "for mocking"

**How Test Strategy Evolved**:
1. **Initial**: No tests (bad)
2. **V2**: Added tests, mocked database/external calls (good)
3. **V3**: Created interfaces for every service to "improve testability" (over-mocked)
4. **V4**: Mocked every layer of call chain (brittle, over-engineered)

**Why This Seemed Correct**:
- Classic unit testing: "Test in isolation, mock all dependencies"
- Mocking frameworks made it easy: "Just create Mock<IService>"
- Fast tests: "Mock everything = no I/O = fast"
- "Professional" testing: "Real codebases use interfaces and mocks"

**What Changed Our Understanding**:
- **Modern testing philosophy**: "Test behavior, not implementation"
- **Mock at seams**: Only mock infrastructure boundaries (external systems)
- **Use real code**: Test with real service implementations when possible
- Kent Beck: "I get paid for code that works, not for tests"
- Integration > unit: Integration tests find more bugs

**Why Mocking at Infrastructure is Correct**:
- **Tests actual code paths**: Uses real SpeFileStore logic
- **Less brittle**: Refactor SpeFileStore internals without breaking tests
- **Clear boundaries**: Mock external systems (Graph API), not internal services
- **Easier maintenance**: Change SpeFileStore implementation, tests still pass
- **Better coverage**: Tests real interactions, not mock interactions

**Real Example**:
```csharp
// ❌ OLD: Mock every layer (brittle, tests mock behavior not real behavior)
var mockResourceStore = new Mock<IResourceStore>();
mockResourceStore
    .Setup(x => x.UploadAsync(...))
    .ReturnsAsync(new DriveItem { ... });
// Problem: Testing mock setup, not real upload logic!

// ✅ NEW: Mock infrastructure, use real service (tests actual code)
var mockGraphFactory = new Mock<IGraphClientFactory>();
mockGraphFactory
    .Setup(x => x.CreateOnBehalfOfClientAsync(...))
    .ReturnsAsync(mockGraphClient);

var fileStore = new SpeFileStore(mockGraphFactory.Object, logger);  // Real implementation!
var result = await fileStore.UploadFileAsync(...);  // Tests real upload logic!
```

**Key Insight**: We're not testing if GraphClientFactory works (that's Graph SDK's job). We're testing if **our** SpeFileStore correctly uses the Graph client.

---

## 🤖 AI PROMPT

```
CONTEXT: You are working on Phase 2 of the SDAP BFF API refactoring, specifically updating test files to mock at infrastructure boundaries instead of mocking service interfaces.

TASK: Update test files to mock IGraphClientFactory (infrastructure boundary) instead of IResourceStore/ISpeService, and use real SpeFileStore instances in tests.

CONSTRAINTS:
- Must mock IGraphClientFactory (infrastructure boundary)
- Must use real SpeFileStore instance (concrete class)
- Must NOT create mocks of concrete classes (violates test best practices)
- Must preserve test coverage and assertions

VERIFICATION BEFORE STARTING:
1. Verify Phase 2 Task 3 complete (DI registrations updated)
2. Verify test files exist that use old interfaces
3. Verify tests currently pass with old mocking strategy
4. If any verification fails, STOP and complete previous tasks first

FOCUS: Stay focused on updating test mocking strategy only. Do NOT delete old service files (that's Task 2.6) or simplify authorization (that's Task 2.5).
```

---

## Goal

Update test files to mock at **infrastructure boundaries** (IGraphClientFactory) instead of mocking service interfaces, and use real **SpeFileStore** instances.

**Problem**:
- Tests currently mock interfaces (IResourceStore, ISpeService)
- These interfaces no longer exist after consolidation
- Tests are too coupled to implementation details

**Target**:
- Tests mock IGraphClientFactory (infrastructure boundary)
- Tests use real SpeFileStore instances
- Tests verify behavior, not implementation

---

## Pre-Flight Verification

### Step 0: Verify Context and Prerequisites

```bash
# 1. Verify Phase 2 Task 3 complete
- [ ] Check DI registrations updated
grep "AddScoped<SpeFileStore>" src/api/Spe.Bff.Api/Extensions/*.cs

# 2. Find test files to update
- [ ] Locate tests using old interfaces
grep -r "Mock<IResourceStore>\|Mock<ISpeService>" tests/

# 3. Run existing tests (baseline)
- [ ] dotnet test
- [ ] Record: Pass/Fail count: _____
```

**If any verification fails**: STOP and complete previous tasks first.

---

## Files to Edit

```bash
Find and update test files:
- [ ] tests/Spe.Bff.Api.Tests/Api/OBOEndpointsTests.cs
- [ ] tests/Spe.Bff.Api.Tests/Api/DocumentsEndpointsTests.cs
- [ ] tests/Spe.Bff.Api.Tests/Services/SpeResourceStoreTests.cs (rename to SpeFileStoreTests.cs)
- [ ] tests/Spe.Bff.Api.Tests/Services/OboSpeServiceTests.cs (may delete or merge)

Note: Exact files depend on your test structure
```

---

## Implementation

### Step 1: Update OBOEndpointsTests.cs

**File**: `tests/Spe.Bff.Api.Tests/Api/OBOEndpointsTests.cs`

#### Before (OLD - mocking service interface):
```csharp
using Moq;
using Xunit;

namespace Spe.Bff.Api.Tests.Api;

public class OBOEndpointsTests
{
    // ❌ OLD: Mock service interface
    [Fact]
    public async Task UploadFile_ValidRequest_ReturnsOk()
    {
        // Arrange
        var mockResourceStore = new Mock<IResourceStore>();
        mockResourceStore
            .Setup(x => x.UploadAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<string>()))
            .ReturnsAsync(new DriveItem { Id = "test-id", Name = "test.txt" });

        // Act
        var result = await UploadFile(mockResourceStore.Object, ...);

        // Assert
        Assert.NotNull(result);
    }
}
```

#### After (NEW - mock infrastructure boundary):
```csharp
using Moq;
using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Spe.Bff.Api.Storage;
using Spe.Bff.Api.Infrastructure;

namespace Spe.Bff.Api.Tests.Api;

public class OBOEndpointsTests
{
    private readonly Mock<IGraphClientFactory> _mockGraphFactory;
    private readonly Mock<GraphServiceClient> _mockGraphClient;
    private readonly Mock<ILogger<SpeFileStore>> _mockLogger;
    private readonly SpeFileStore _fileStore;

    public OBOEndpointsTests()
    {
        // ✅ NEW: Mock infrastructure boundary
        _mockGraphFactory = new Mock<IGraphClientFactory>();
        _mockGraphClient = CreateMockGraphClient();
        _mockLogger = new Mock<ILogger<SpeFileStore>>();

        // ✅ NEW: Use real SpeFileStore with mocked dependencies
        _fileStore = new SpeFileStore(_mockGraphFactory.Object, _mockLogger.Object);
    }

    // ✅ NEW: Test with real SpeFileStore
    [Fact]
    public async Task UploadFile_ValidRequest_ReturnsFileUploadResult()
    {
        // Arrange
        var containerId = "test-container";
        var fileName = "test.txt";
        var userToken = "fake-token";
        var content = new MemoryStream(Encoding.UTF8.GetBytes("test content"));

        var expectedDriveItem = new DriveItem
        {
            Id = "test-id",
            Name = fileName,
            Size = 12,
            File = new Microsoft.Graph.File { MimeType = "text/plain" },
            WebUrl = "https://example.com/test.txt",
            CreatedDateTime = DateTimeOffset.UtcNow
        };

        // Mock Graph API response
        SetupMockGraphClient(containerId, fileName, expectedDriveItem);

        _mockGraphFactory
            .Setup(x => x.CreateOnBehalfOfClientAsync(userToken))
            .ReturnsAsync(_mockGraphClient.Object);

        // Act
        var result = await _fileStore.UploadFileAsync(
            containerId,
            fileName,
            content,
            userToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-id", result.ItemId);
        Assert.Equal(fileName, result.Name);
        Assert.Equal(12, result.Size);
        Assert.Equal("text/plain", result.MimeType);
        Assert.NotNull(result.WebUrl);

        // Verify infrastructure interaction
        _mockGraphFactory.Verify(
            x => x.CreateOnBehalfOfClientAsync(userToken),
            Times.Once);
    }

    [Fact]
    public async Task UploadFile_NullContainerId_ThrowsArgumentException()
    {
        // Arrange
        var fileName = "test.txt";
        var userToken = "fake-token";
        var content = new MemoryStream();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _fileStore.UploadFileAsync(null!, fileName, content, userToken));
    }

    [Fact]
    public async Task UploadFile_EmptyFileName_ThrowsArgumentException()
    {
        // Arrange
        var containerId = "test-container";
        var userToken = "fake-token";
        var content = new MemoryStream();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _fileStore.UploadFileAsync(containerId, "", content, userToken));
    }

    // ============================================================================
    // Helper Methods
    // ============================================================================

    private Mock<GraphServiceClient> CreateMockGraphClient()
    {
        // Create mock Graph client
        // Note: Mocking GraphServiceClient is complex, consider using a test helper
        var mock = new Mock<GraphServiceClient>(
            new DelegateAuthenticationProvider((request) => Task.CompletedTask));

        return mock;
    }

    private void SetupMockGraphClient(
        string containerId,
        string fileName,
        DriveItem expectedDriveItem)
    {
        // Mock the Graph SDK fluent API chain
        // This is complex - consider extracting to test helper class

        // Example (simplified - actual implementation depends on Graph SDK version):
        // _mockGraphClient
        //     .Setup(x => x.Drives[containerId].Root.ItemWithPath(fileName).Content.Request().PutAsync<DriveItem>(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
        //     .ReturnsAsync(expectedDriveItem);

        // Note: Graph SDK mocking is notoriously difficult
        // Consider using Microsoft.Graph.DotnetCore.Test package or test adapters
    }
}
```

### Step 2: Create SpeFileStoreTests.cs (Rename from SpeResourceStoreTests)

**File**: `tests/Spe.Bff.Api.Tests/Storage/SpeFileStoreTests.cs`

```csharp
using Moq;
using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Spe.Bff.Api.Storage;
using Spe.Bff.Api.Infrastructure;

namespace Spe.Bff.Api.Tests.Storage;

public class SpeFileStoreTests
{
    private readonly Mock<IGraphClientFactory> _mockGraphFactory;
    private readonly Mock<ILogger<SpeFileStore>> _mockLogger;
    private readonly SpeFileStore _sut; // System Under Test

    public SpeFileStoreTests()
    {
        _mockGraphFactory = new Mock<IGraphClientFactory>();
        _mockLogger = new Mock<ILogger<SpeFileStore>>();
        _sut = new SpeFileStore(_mockGraphFactory.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task UploadFileAsync_ValidInput_ReturnsFileUploadResult()
    {
        // Arrange
        var containerId = "container-123";
        var fileName = "document.pdf";
        var userToken = "user-token";
        var content = new MemoryStream(new byte[1024]);

        var mockGraphClient = new Mock<GraphServiceClient>(
            new DelegateAuthenticationProvider((req) => Task.CompletedTask));

        var expectedDriveItem = new DriveItem
        {
            Id = "item-123",
            Name = fileName,
            Size = 1024,
            File = new Microsoft.Graph.File { MimeType = "application/pdf" },
            WebUrl = "https://sharepoint.com/document.pdf",
            CreatedDateTime = DateTimeOffset.UtcNow,
            ETag = "etag-123"
        };

        // Setup Graph client factory
        _mockGraphFactory
            .Setup(x => x.CreateOnBehalfOfClientAsync(userToken))
            .ReturnsAsync(mockGraphClient.Object);

        // Setup Graph API call (complex - depends on Graph SDK version)
        // mockGraphClient.Setup(...).ReturnsAsync(expectedDriveItem);

        // Act
        var result = await _sut.UploadFileAsync(containerId, fileName, content, userToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("item-123", result.ItemId);
        Assert.Equal(fileName, result.Name);
        Assert.Equal(1024, result.Size);
        Assert.Equal("application/pdf", result.MimeType);
        Assert.Equal("https://sharepoint.com/document.pdf", result.WebUrl);
        Assert.Equal("etag-123", result.ETag);

        // Verify factory was called
        _mockGraphFactory.Verify(
            x => x.CreateOnBehalfOfClientAsync(userToken),
            Times.Once);
    }

    [Theory]
    [InlineData(null, "file.txt", "content", "token")]
    [InlineData("", "file.txt", "content", "token")]
    [InlineData("container", null, "content", "token")]
    [InlineData("container", "", "content", "token")]
    [InlineData("container", "file.txt", null, "token")]
    [InlineData("container", "file.txt", "content", null)]
    [InlineData("container", "file.txt", "content", "")]
    public async Task UploadFileAsync_InvalidInput_ThrowsArgumentException(
        string containerId,
        string fileName,
        string contentIndicator,
        string userToken)
    {
        // Arrange
        var content = contentIndicator == null ? null : new MemoryStream();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.UploadFileAsync(containerId, fileName, content!, userToken));
    }

    [Fact]
    public async Task DownloadFileAsync_ValidInput_ReturnsFileDownloadResult()
    {
        // Arrange
        var containerId = "container-123";
        var itemId = "item-123";
        var userToken = "user-token";

        var mockGraphClient = new Mock<GraphServiceClient>(
            new DelegateAuthenticationProvider((req) => Task.CompletedTask));

        var driveItem = new DriveItem
        {
            Id = itemId,
            Name = "document.pdf",
            Size = 2048,
            File = new Microsoft.Graph.File { MimeType = "application/pdf" }
        };

        var contentStream = new MemoryStream(new byte[2048]);

        _mockGraphFactory
            .Setup(x => x.CreateOnBehalfOfClientAsync(userToken))
            .ReturnsAsync(mockGraphClient.Object);

        // Setup Graph API calls
        // mockGraphClient.Setup(...metadata...).ReturnsAsync(driveItem);
        // mockGraphClient.Setup(...content...).ReturnsAsync(contentStream);

        // Act
        var result = await _sut.DownloadFileAsync(containerId, itemId, userToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("document.pdf", result.FileName);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.Equal(2048, result.ContentLength);
        Assert.NotNull(result.Content);
    }

    [Fact]
    public async Task DeleteFileAsync_ValidInput_CompletesSuccessfully()
    {
        // Arrange
        var containerId = "container-123";
        var itemId = "item-123";
        var userToken = "user-token";

        var mockGraphClient = new Mock<GraphServiceClient>(
            new DelegateAuthenticationProvider((req) => Task.CompletedTask));

        _mockGraphFactory
            .Setup(x => x.CreateOnBehalfOfClientAsync(userToken))
            .ReturnsAsync(mockGraphClient.Object);

        // Setup delete operation
        // mockGraphClient.Setup(...delete...).Returns(Task.CompletedTask);

        // Act
        await _sut.DeleteFileAsync(containerId, itemId, userToken);

        // Assert
        // Verify factory was called
        _mockGraphFactory.Verify(
            x => x.CreateOnBehalfOfClientAsync(userToken),
            Times.Once);
    }

    [Fact]
    public async Task GetFileMetadataAsync_ValidInput_ReturnsFileMetadata()
    {
        // Arrange
        var containerId = "container-123";
        var itemId = "item-123";
        var userToken = "user-token";

        var mockGraphClient = new Mock<GraphServiceClient>(
            new DelegateAuthenticationProvider((req) => Task.CompletedTask));

        var driveItem = new DriveItem
        {
            Id = itemId,
            Name = "document.pdf",
            Size = 1024,
            File = new Microsoft.Graph.File { MimeType = "application/pdf" },
            WebUrl = "https://sharepoint.com/document.pdf",
            CreatedDateTime = DateTimeOffset.UtcNow,
            LastModifiedDateTime = DateTimeOffset.UtcNow,
            CreatedBy = new IdentitySet { User = new Identity { DisplayName = "John Doe" } },
            LastModifiedBy = new IdentitySet { User = new Identity { DisplayName = "Jane Doe" } }
        };

        _mockGraphFactory
            .Setup(x => x.CreateOnBehalfOfClientAsync(userToken))
            .ReturnsAsync(mockGraphClient.Object);

        // Setup Graph API call
        // mockGraphClient.Setup(...).ReturnsAsync(driveItem);

        // Act
        var result = await _sut.GetFileMetadataAsync(containerId, itemId, userToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(itemId, result.ItemId);
        Assert.Equal("document.pdf", result.Name);
        Assert.Equal(1024, result.Size);
        Assert.Equal("John Doe", result.CreatedBy);
        Assert.Equal("Jane Doe", result.ModifiedBy);
    }

    [Fact]
    public async Task ListFilesAsync_RootFolder_ReturnsFileList()
    {
        // Arrange
        var containerId = "container-123";
        var userToken = "user-token";

        var mockGraphClient = new Mock<GraphServiceClient>(
            new DelegateAuthenticationProvider((req) => Task.CompletedTask));

        var driveItems = new List<DriveItem>
        {
            new DriveItem
            {
                Id = "item-1",
                Name = "file1.pdf",
                Size = 1024,
                File = new Microsoft.Graph.File { MimeType = "application/pdf" }
            },
            new DriveItem
            {
                Id = "item-2",
                Name = "file2.docx",
                Size = 2048,
                File = new Microsoft.Graph.File { MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document" }
            }
        };

        _mockGraphFactory
            .Setup(x => x.CreateOnBehalfOfClientAsync(userToken))
            .ReturnsAsync(mockGraphClient.Object);

        // Setup Graph API call
        // mockGraphClient.Setup(...).ReturnsAsync(driveItems);

        // Act
        var result = await _sut.ListFilesAsync(containerId, null, userToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count());
        Assert.Contains(result, f => f.Name == "file1.pdf");
        Assert.Contains(result, f => f.Name == "file2.docx");
    }
}
```

### Step 3: Simplify Graph SDK Mocking (Test Helper)

**File**: `tests/Spe.Bff.Api.Tests/TestHelpers/GraphClientTestHelper.cs`

```csharp
using Microsoft.Graph;
using Moq;

namespace Spe.Bff.Api.Tests.TestHelpers;

/// <summary>
/// Helper for mocking Graph SDK in tests.
/// Graph SDK's fluent API is notoriously difficult to mock.
/// </summary>
public static class GraphClientTestHelper
{
    /// <summary>
    /// Create a mock GraphServiceClient with minimal setup.
    /// </summary>
    public static Mock<GraphServiceClient> CreateMockGraphClient()
    {
        var mockAuthProvider = new DelegateAuthenticationProvider(
            (request) => Task.CompletedTask);

        var mock = new Mock<GraphServiceClient>(mockAuthProvider);
        return mock;
    }

    /// <summary>
    /// Create a test DriveItem with common properties.
    /// </summary>
    public static DriveItem CreateTestDriveItem(
        string id = "test-id",
        string name = "test-file.txt",
        long size = 1024,
        string mimeType = "text/plain")
    {
        return new DriveItem
        {
            Id = id,
            Name = name,
            Size = size,
            File = new Microsoft.Graph.File { MimeType = mimeType },
            WebUrl = $"https://sharepoint.com/{name}",
            CreatedDateTime = DateTimeOffset.UtcNow,
            LastModifiedDateTime = DateTimeOffset.UtcNow,
            ETag = $"etag-{id}"
        };
    }
}
```

**Note**: Mocking Graph SDK is complex. Consider using:
1. **Microsoft.Graph.DotnetCore.Test** package (if available)
2. **Test adapters** to wrap Graph SDK calls
3. **Integration tests** with real Graph API (dev tenant)

---

## Validation

### Build Check
```bash
dotnet build
# Expected: Success, 0 warnings
```

### Test Execution
```bash
# Run all tests
dotnet test

# Expected: All tests pass (same or more tests than before)

# Run specific test file
dotnet test --filter "FullyQualifiedName~SpeFileStoreTests"

# Expected: All SpeFileStore tests pass
```

### Test Coverage Check
```bash
# Run with coverage
dotnet test --collect:"XPlat Code Coverage"

# Expected: Coverage maintained or improved
```

---

## Checklist

- [ ] **Pre-flight**: Verified Phase 2 Task 3 complete (DI updated)
- [ ] **Pre-flight**: Found test files using old interfaces
- [ ] **Pre-flight**: Recorded baseline test pass/fail count
- [ ] Updated endpoint tests to use real SpeFileStore
- [ ] Updated endpoint tests to mock IGraphClientFactory
- [ ] Renamed SpeResourceStoreTests to SpeFileStoreTests
- [ ] Updated SpeFileStore tests to mock at infrastructure boundary
- [ ] Removed mocks of IResourceStore, ISpeService
- [ ] Created test helper for Graph SDK mocking (optional)
- [ ] All tests pass: `dotnet test`
- [ ] Test coverage maintained or improved
- [ ] No mocks of concrete classes

---

## Expected Results

**Before**:
- ❌ Tests mock service interfaces (IResourceStore, ISpeService)
- ❌ Tests coupled to implementation details
- ❌ Tests will break when interfaces are deleted

**After**:
- ✅ Tests mock infrastructure boundary (IGraphClientFactory)
- ✅ Tests use real SpeFileStore instances
- ✅ Tests verify behavior, not implementation
- ✅ Tests ready for interface deletion

---

## Anti-Pattern Verification

### ✅ Avoided: Mocking Concrete Classes
```bash
# Verify no mocks of SpeFileStore
grep "Mock<SpeFileStore>" tests/
# Expected: No results ✅
```

**Why**: Mocking concrete classes defeats the purpose of using concretes

### ✅ Mocked at Infrastructure Boundary
```bash
# Verify mocking IGraphClientFactory
grep "Mock<IGraphClientFactory>" tests/
# Expected: Should find results ✅
```

**Why**: Test at seams (infrastructure boundaries), not every layer

---

## Troubleshooting

### Issue: Graph SDK mocking is too complex

**Cause**: Graph SDK fluent API is difficult to mock

**Options**:
1. **Use test adapter pattern**: Wrap Graph SDK calls in testable adapter
2. **Use integration tests**: Test against real Graph API (dev tenant)
3. **Use Microsoft.Graph.DotnetCore.Test**: Official test helpers (check availability)

### Issue: Tests fail after refactoring

**Cause**: Missing mock setup for Graph client

**Fix**: Ensure Graph client factory mock is set up:
```csharp
_mockGraphFactory
    .Setup(x => x.CreateOnBehalfOfClientAsync(It.IsAny<string>()))
    .ReturnsAsync(mockGraphClient.Object);
```

### Issue: "Cannot mock sealed class GraphServiceClient"

**Cause**: Some Graph SDK types are sealed

**Fix**: Use interfaces from Graph SDK or create adapter:
```csharp
// Option 1: Use IGraphServiceClient (if available)
Mock<IGraphServiceClient> mockClient;

// Option 2: Create adapter interface
public interface IGraphAdapter
{
    Task<DriveItem> UploadFileAsync(...);
}
```

---

## Context Verification

Before marking complete, verify:
- [ ] ✅ All tests updated to mock IGraphClientFactory
- [ ] ✅ All tests use real SpeFileStore instances
- [ ] ✅ No mocks of IResourceStore, ISpeService remain
- [ ] ✅ All tests pass
- [ ] ✅ Test coverage maintained
- [ ] ✅ Task stayed focused (did NOT delete old files or simplify authz)

**If any item unchecked**: Review and fix before proceeding to Task 2.5

---

## Commit Message

```bash
git add tests/Spe.Bff.Api.Tests/
git commit -m "test: update mocking strategy to infrastructure boundaries

- Mock IGraphClientFactory (infrastructure boundary) instead of service interfaces
- Use real SpeFileStore instances in tests (concrete class)
- Rename SpeResourceStoreTests to SpeFileStoreTests
- Remove mocks of IResourceStore, ISpeService
- Create GraphClientTestHelper for test utilities
- Maintain test coverage at X% (same as before)

Test Strategy: Mock at seams, use real implementations
ADR Compliance: ADR-010 (Test concrete classes, mock infrastructure)
Anti-Patterns Avoided: Mocking concrete classes
Task: Phase 2, Task 4"
```

---

## Next Task

➡️ [Phase 2 - Task 5: Simplify Authorization](phase-2-task-5-simplify-authz.md)

**What's next**: Remove IDataverseSecurityService/IUacService wrappers, use AuthorizationService directly

---

## Related Resources

- **Patterns**: Mock at infrastructure boundaries (IGraphClientFactory, IAccessDataSource)
- **Anti-Patterns**:
  - [anti-pattern-interface-proliferation.md](../patterns/anti-pattern-interface-proliferation.md)
- **Testing**: Use real implementations, mock dependencies
- **ADR**: [ARCHITECTURAL-DECISIONS.md](../ARCHITECTURAL-DECISIONS.md) - ADR-010
