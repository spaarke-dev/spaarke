# SDAP Testing Strategy - Comprehensive Design Document

**Purpose:** Establish comprehensive testing strategy for SDAP with real integration testing as core requirement
**Status:** Planning Document for Sprint 5+
**Priority:** 🔴 P0 CRITICAL - Required before production deployment
**Owner:** Development Team
**Created:** October 3, 2025

---

## Table of Contents
1. [Executive Summary](#executive-summary)
2. [Philosophy & Principles](#philosophy--principles)
3. [Current State Assessment](#current-state-assessment)
4. [Testing Pyramid](#testing-pyramid)
5. [Test Infrastructure Requirements](#test-infrastructure-requirements)
6. [Module-by-Module Test Requirements](#module-by-module-test-requirements)
7. [Development Workflow Integration](#development-workflow-integration)
8. [CI/CD Integration](#cicd-integration)
9. [Prioritization & Phasing](#prioritization--phasing)
10. [Effort Estimates](#effort-estimates)
11. [Dependencies & Blockers](#dependencies--blockers)
12. [Success Criteria](#success-criteria)
13. [Risks & Mitigation](#risks--mitigation)

---

## Executive Summary

### The Problem

**Current Testing Gap:**
- ✅ Unit tests exist and pass
- ✅ Integration tests exist but test **placeholders, not real integrations**
- ❌ No tests validate actual Graph API, Dataverse, Service Bus, Redis integration
- ❌ Hidden issues only discovered in production

**Key Insight:**
> "Fake testing is not real testing. If we don't do real testing, we will have hidden issues."

**Impact:**
- High risk of production failures
- Cannot confidently deploy
- Breaking changes in external services go undetected
- Permission issues discovered too late

### The Solution

**Establish real integration testing as a core requirement:**
1. Build test infrastructure (test environments, service principals, cleanup automation)
2. Write real integration tests for all critical modules
3. Integrate into development workflow (tests required for module completion)
4. Run in CI/CD pipeline (automated validation on every PR)

### Expected Outcomes

- ✅ Confidence in deployments (know code works with real services)
- ✅ Early detection of breaking changes
- ✅ Validated permissions and configuration
- ✅ Reduced production incidents
- ✅ Faster debugging (test failures point to exact issue)

### Timeline & Effort

| Phase | Focus | Effort | Timeline |
|-------|-------|--------|----------|
| **Phase 1** | Test Infrastructure Setup | 8-12 hours | Sprint 5 |
| **Phase 2** | Critical Path Modules | 16-24 hours | Sprint 5-6 |
| **Phase 3** | Core Features | 12-16 hours | Sprint 6-7 |
| **Phase 4** | Supporting Features | 8-12 hours | Sprint 7+ |
| **Total** | All modules | **44-64 hours** | **3-4 sprints** |

---

## Philosophy & Principles

### Core Principles

#### 1. Real Over Fake
**Principle:** Integration tests must test against REAL external services, not mocks.

**Rationale:**
- Mocks hide breaking changes
- Mocks don't validate permissions
- Mocks don't catch serialization issues
- Mocks don't test error handling with real errors

**Example:**
```csharp
// ❌ FAKE - Mock hides issues
var mockGraphClient = new Mock<GraphServiceClient>();
mockGraphClient.Setup(x => x.Drives[It.IsAny<string>()].Root.ItemWithPath(It.IsAny<string>()).Content.PutAsync(It.IsAny<Stream>()))
    .ReturnsAsync(new DriveItem { Id = "fake-id" });

// ✅ REAL - Actually uploads to SharePoint
var realGraphClient = CreateGraphClientWithRealCredentials();
var driveItem = await realGraphClient.Drives[testContainerId].Root.ItemWithPath("test.txt").Content.PutAsync(fileStream);
// If this fails, we know immediately there's a problem
```

#### 2. Module Complete = Tests Pass
**Principle:** A module is not "complete" until real integration tests pass.

**Definition of Done:**
- ✅ Code implemented
- ✅ Unit tests pass (business logic)
- ✅ **Real integration tests pass (against test environment)**
- ✅ Code reviewed
- ✅ Documentation updated

#### 3. Test Early, Test Often
**Principle:** Write integration tests WHEN module is built, not later.

**Workflow:**
```
Build Module → Write Unit Tests → Write Integration Tests → Fix Issues → Mark Complete
```

**NOT:**
```
Build All Modules → Write Fake Tests → Mark Complete → Discover Issues in Production
```

#### 4. Fail Fast, Fix Fast
**Principle:** Integration tests fail immediately on issues, blocking deployment.

**CI/CD Integration:**
- PR cannot merge if integration tests fail
- Deployment cannot proceed if integration tests fail
- Developers see test failures immediately

#### 5. Clean Up After Yourself
**Principle:** Integration tests must clean up test data (files, records, messages).

**Requirements:**
- Each test creates unique test data (GUIDs, timestamps)
- Each test deletes created data in cleanup phase
- Test environments remain clean for next run

---

## Current State Assessment

### Existing Tests (Good Foundation)

**Unit Tests:**
- ✅ JobContract serialization/validation tests
- ✅ JobOutcome state machine tests
- ✅ Business logic tests (no external dependencies)

**Integration Tests (Placeholder Only):**
- ⚠️ SystemIntegrationTests.cs - Tests HTTP endpoints return 401/400
- ⚠️ Validates error format, not success cases
- ⚠️ No authentication, no real service calls

### Critical Gaps

**No tests for:**
1. ❌ Graph API integration (container CRUD, file upload/download)
2. ❌ Dataverse integration (document metadata CRUD)
3. ❌ Service Bus integration (job submission, processing, DLQ)
4. ❌ Redis integration (distributed cache, idempotency)
5. ❌ Authorization flow (OBO token flow, Managed Identity)
6. ❌ Upload sessions (chunked upload for large files)
7. ❌ End-to-end workflows (upload → process → metadata)

### Risk Assessment

| Module | Current Coverage | Risk Level | Impact if Breaks |
|--------|------------------|------------|------------------|
| **SpeFileStore** | 0% real tests | 🔴 Critical | Cannot upload/download files |
| **DataverseWebApiService** | 0% real tests | 🔴 Critical | Cannot store metadata |
| **ServiceBusJobProcessor** | 0% real tests | 🔴 Critical | Background jobs fail |
| **UploadSessionManager** | 0% real tests | 🟡 High | Large file uploads fail |
| **IdempotencyService** | 0% real tests | 🟡 High | Duplicate processing |
| **GraphClientFactory** | 0% real tests | 🟡 High | Auth failures |
| **Authorization** | 0% real tests | 🟡 High | Security bypass |

**Overall Risk:** 🔴 **CRITICAL** - Cannot confidently deploy to production

---

## Testing Pyramid

### Test Distribution

```
        /\
       /  \  E2E Tests (5%)
      /____\  - Full workflows
     /      \  - User scenarios
    /        \ Integration Tests (25%)
   /__________\ - Real external services
  /            \ - API contracts
 /              \ Unit Tests (70%)
/________________\ - Business logic
                    - Validation
                    - Transformations
```

### Test Types Defined

#### 1. Unit Tests (70% of tests)
**Purpose:** Validate business logic in isolation
**Characteristics:**
- No external dependencies
- Fast (milliseconds)
- Run on every build
- Mock/stub external services

**Example:**
```csharp
[Fact]
public void JobContract_IsAtMaxAttempts_ReturnsCorrectValue()
{
    var job = new JobContract { Attempt = 3, MaxAttempts = 3 };
    job.IsAtMaxAttempts.Should().BeTrue();
}
```

**When to Write:** Immediately when implementing business logic

#### 2. Integration Tests (25% of tests)
**Purpose:** Validate integration with REAL external services
**Characteristics:**
- Tests against real Graph API, Dataverse, Service Bus, Redis
- Slower (seconds to minutes)
- Run in CI/CD pipeline
- Cleanup test data after each run

**Example:**
```csharp
[Fact]
public async Task UploadSmallAsync_RealSharePoint_CreatesFile()
{
    // Arrange - REAL credentials, REAL container
    var fileName = $"test-{Guid.NewGuid()}.txt";
    var content = new MemoryStream(Encoding.UTF8.GetBytes("Test"));

    // Act - ACTUALLY uploads to SharePoint
    var result = await _fileStore.UploadSmallAsync(_testContainerId, fileName, content);

    // Assert - ACTUALLY verifies file exists
    result.Should().NotBeNull();
    result.Id.Should().NotBeNullOrEmpty();

    // Cleanup - DELETE from SharePoint
    await _fileStore.DeleteAsync(_testContainerId, result.Id);
}
```

**When to Write:** When module is complete, before marking "Done"

#### 3. End-to-End Tests (5% of tests)
**Purpose:** Validate complete user workflows
**Characteristics:**
- Tests full scenarios (upload → process → metadata → download)
- Slowest (minutes)
- Run before deployment
- Critical path only

**Example:**
```csharp
[Fact]
public async Task CompleteDocumentWorkflow_UploadProcessDownload_Success()
{
    // 1. Upload file to SharePoint
    var uploadResult = await UploadFileAsync("test-doc.pdf");

    // 2. Submit background job
    await SubmitProcessingJobAsync(uploadResult.FileId);

    // 3. Wait for job completion (poll or webhook)
    await WaitForJobCompletionAsync(uploadResult.JobId, timeout: 30s);

    // 4. Verify metadata in Dataverse
    var metadata = await GetDocumentMetadataAsync(uploadResult.FileId);
    metadata.Status.Should().Be("Processed");

    // 5. Download and verify content
    var downloadedContent = await DownloadFileAsync(uploadResult.FileId);
    downloadedContent.Should().Equal(originalContent);
}
```

**When to Write:** After critical modules complete, before production deployment

---

## Test Infrastructure Requirements

### Infrastructure Components

#### 1. Test Environments

**SharePoint Embedded (Graph API):**
- Dedicated test container in dev SPE instance
- Service principal with permissions:
  - `Sites.ReadWrite.All`
  - `Files.ReadWrite.All`
- Container ID: Stored in test configuration
- Cleanup: Automated deletion of files older than 24 hours

**Dataverse:**
- Dedicated test environment (separate from dev/prod)
- Service principal with permissions:
  - Create/Read/Write/Delete on `sprk_document` entity
  - Read on `sprk_container` entity
- Test records: Prefixed with `TEST_` for easy identification
- Cleanup: Automated deletion after each test

**Service Bus:**
- Option A: Service Bus emulator (local, fast, free)
- Option B: Dedicated test namespace in Azure (isolated, realistic)
- Test queue: `sdap-jobs-test`
- DLQ: `sdap-jobs-test/$deadletterqueue`
- Cleanup: Purge queue after each test run

**Redis:**
- Option A: Redis container (docker-compose, local)
- Option B: Azure Redis Cache test instance (shared, low cost)
- Test instance prefix: `test:`
- Cleanup: Flush test keys after each run

#### 2. Service Principals & Credentials

**Test Service Principal:**
- Name: `sdap-test-sp`
- Permissions:
  - Graph API: Sites.ReadWrite.All, Files.ReadWrite.All
  - Dataverse: System User role (test environment only)
  - Service Bus: Send/Receive on test queue
  - Redis: Connection string with full access
- Secrets stored in: Azure Key Vault (test vault)

**Configuration Management:**
```json
// appsettings.Test.json
{
  "Graph": {
    "TenantId": "...",
    "ClientId": "test-sp-client-id",
    "ClientSecret": "@Microsoft.KeyVault(...)",
    "TestContainerId": "test-container-guid"
  },
  "Dataverse": {
    "EnvironmentUrl": "https://test.crm.dynamics.com",
    "ClientId": "test-sp-client-id",
    "ClientSecret": "@Microsoft.KeyVault(...)"
  },
  "ConnectionStrings": {
    "ServiceBus": "Endpoint=sb://sdap-test.servicebus.windows.net/...",
    "Redis": "sdap-test.redis.cache.windows.net:6380,..."
  },
  "TestSettings": {
    "CleanupEnabled": true,
    "TimeoutSeconds": 30,
    "RetryAttempts": 3
  }
}
```

#### 3. Test Data Management

**Naming Convention:**
```csharp
public class TestDataHelper
{
    public static string GenerateTestFileName()
        => $"TEST_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid()}.txt";

    public static string GenerateTestDocumentName()
        => $"TEST_Document_{Guid.NewGuid()}";

    public static bool IsTestData(string name)
        => name.StartsWith("TEST_", StringComparison.OrdinalIgnoreCase);
}
```

**Cleanup Strategy:**
```csharp
public class TestCleanupService
{
    // Run after each test
    public async Task CleanupTestDataAsync()
    {
        // 1. Delete test files from SharePoint (created in last 24h)
        await DeleteOldTestFilesAsync(_testContainerId, age: TimeSpan.FromDays(1));

        // 2. Delete test records from Dataverse
        await DeleteTestRecordsAsync("sprk_document", filter: "startswith(sprk_name, 'TEST_')");

        // 3. Purge Service Bus test queue
        await PurgeQueueAsync("sdap-jobs-test");

        // 4. Flush Redis test keys
        await FlushKeysAsync(pattern: "test:*");
    }

    // Run nightly (scheduled task)
    public async Task CleanupOrphanedDataAsync()
    {
        // Delete any test data older than 7 days (orphaned from failed tests)
        await DeleteOldTestFilesAsync(_testContainerId, age: TimeSpan.FromDays(7));
    }
}
```

#### 4. Test Fixtures & Utilities

**Base Integration Test Fixture:**
```csharp
public class IntegrationTestFixture : IAsyncLifetime
{
    public IGraphClientFactory GraphClientFactory { get; private set; }
    public IDataverseService DataverseService { get; private set; }
    public ServiceBusClient ServiceBusClient { get; private set; }
    public IDistributedCache RedisCache { get; private set; }

    public string TestContainerId { get; private set; }

    private readonly List<string> _createdFileIds = new();
    private readonly List<Guid> _createdDocumentIds = new();

    public async Task InitializeAsync()
    {
        // Load test configuration
        var config = LoadTestConfiguration();

        // Initialize real clients
        GraphClientFactory = CreateRealGraphClientFactory(config);
        DataverseService = CreateRealDataverseService(config);
        ServiceBusClient = CreateRealServiceBusClient(config);
        RedisCache = CreateRealRedisCache(config);

        TestContainerId = config["Graph:TestContainerId"]!;
    }

    public async Task DisposeAsync()
    {
        // Cleanup all created test data
        foreach (var fileId in _createdFileIds)
        {
            await DeleteFileAsync(fileId);
        }

        foreach (var docId in _createdDocumentIds)
        {
            await DataverseService.DeleteDocumentAsync(docId.ToString());
        }

        // Purge Service Bus queue
        await PurgeServiceBusQueueAsync();

        // Flush Redis test keys
        await FlushRedisTestKeysAsync();
    }

    // Helper methods for tracking test data
    public void TrackCreatedFile(string fileId) => _createdFileIds.Add(fileId);
    public void TrackCreatedDocument(Guid docId) => _createdDocumentIds.Add(docId);
}
```

**Test Helper Methods:**
```csharp
public static class TestHelpers
{
    public static async Task<Stream> CreateTestFileAsync(long sizeInBytes, string content = null)
    {
        var stream = new MemoryStream();
        var writer = new StreamWriter(stream);

        if (content != null)
        {
            await writer.WriteAsync(content);
        }
        else
        {
            // Generate random content to reach desired size
            var random = new Random();
            while (stream.Length < sizeInBytes)
            {
                await writer.WriteAsync($"Test data {random.Next()}\n");
            }
        }

        await writer.FlushAsync();
        stream.Position = 0;
        return stream;
    }

    public static async Task<bool> WaitForConditionAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        TimeSpan pollInterval = default)
    {
        pollInterval = pollInterval == default ? TimeSpan.FromSeconds(1) : pollInterval;
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < timeout)
        {
            if (await condition())
                return true;

            await Task.Delay(pollInterval);
        }

        return false;
    }
}
```

---

## Module-by-Module Test Requirements

### Critical Path Modules (Priority 1)

#### Module 1: SpeFileStore (Graph API Integration)

**Functionality:** Upload, download, delete files in SharePoint Embedded

**Test Requirements:**

**1.1 Small File Upload (< 4MB)**
```csharp
[Fact]
public async Task UploadSmallAsync_ValidFile_CreatesInSharePoint()
{
    // Arrange
    var fileName = TestDataHelper.GenerateTestFileName();
    var content = await TestHelpers.CreateTestFileAsync(1024); // 1KB

    // Act
    var result = await _fileStore.UploadSmallAsync(_testContainerId, fileName, content);

    // Assert
    result.Should().NotBeNull();
    result.Id.Should().NotBeNullOrEmpty();
    result.Name.Should().Be(fileName);
    result.Size.Should().BeGreaterThan(0);

    _fixture.TrackCreatedFile(result.Id);
}
```

**1.2 Large File Upload (> 4MB) - Chunked Upload**
```csharp
[Fact]
public async Task CreateUploadSession_LargeFile_UploadsInChunks()
{
    // Arrange
    var fileName = TestDataHelper.GenerateTestFileName();

    // Act - Create upload session
    var session = await _uploadManager.CreateUploadSessionAsync(_testContainerId, fileName);

    // Assert
    session.Should().NotBeNull();
    session.UploadUrl.Should().NotBeNullOrEmpty();

    // Upload chunks (simulate 10MB file in 3MB chunks)
    var chunk1 = await TestHelpers.CreateTestFileAsync(3 * 1024 * 1024);
    var chunk2 = await TestHelpers.CreateTestFileAsync(3 * 1024 * 1024);
    var chunk3 = await TestHelpers.CreateTestFileAsync(4 * 1024 * 1024);

    var result1 = await _uploadManager.UploadChunkAsync(session.UploadUrl, "bytes 0-3145727/10485760", chunk1.ToArray());
    result1.StatusCode.Should().Be(202); // Accepted

    var result2 = await _uploadManager.UploadChunkAsync(session.UploadUrl, "bytes 3145728-6291455/10485760", chunk2.ToArray());
    result2.StatusCode.Should().Be(202);

    var result3 = await _uploadManager.UploadChunkAsync(session.UploadUrl, "bytes 6291456-10485759/10485760", chunk3.ToArray());
    result3.StatusCode.Should().Be(201); // Created (final chunk)
    result3.DriveItem.Should().NotBeNull();

    _fixture.TrackCreatedFile(result3.DriveItem.Id);
}
```

**1.3 Download File**
```csharp
[Fact]
public async Task DownloadAsync_ExistingFile_ReturnsCorrectContent()
{
    // Arrange - Upload test file first
    var expectedContent = "Test file content for download";
    var fileName = TestDataHelper.GenerateTestFileName();
    var uploadStream = new MemoryStream(Encoding.UTF8.GetBytes(expectedContent));
    var uploadResult = await _fileStore.UploadSmallAsync(_testContainerId, fileName, uploadStream);
    _fixture.TrackCreatedFile(uploadResult.Id);

    // Act - Download
    var downloadStream = await _fileStore.DownloadAsync(_testContainerId, uploadResult.Id);

    // Assert
    downloadStream.Should().NotBeNull();
    var actualContent = await new StreamReader(downloadStream).ReadToEndAsync();
    actualContent.Should().Be(expectedContent);
}
```

**1.4 Delete File**
```csharp
[Fact]
public async Task DeleteAsync_ExistingFile_RemovesFromSharePoint()
{
    // Arrange - Upload test file
    var fileName = TestDataHelper.GenerateTestFileName();
    var content = await TestHelpers.CreateTestFileAsync(1024);
    var uploadResult = await _fileStore.UploadSmallAsync(_testContainerId, fileName, content);

    // Act - Delete
    await _fileStore.DeleteAsync(_testContainerId, uploadResult.Id);

    // Assert - Verify file no longer exists
    Func<Task> getDeleted = async () => await _fileStore.GetFileMetadataAsync(_testContainerId, uploadResult.Id);
    await getDeleted.Should().ThrowAsync<ServiceException>()
        .Where(ex => ex.ResponseStatusCode == 404);
}
```

**1.5 Permission Handling**
```csharp
[Fact]
public async Task UploadSmallAsync_InsufficientPermissions_ThrowsServiceException()
{
    // Arrange - Create client with read-only permissions
    var readOnlyClient = CreateGraphClientWithLimitedPermissions(permissions: ["Files.Read.All"]);
    var readOnlyFileStore = new SpeFileStore(readOnlyClient, _logger);

    var fileName = TestDataHelper.GenerateTestFileName();
    var content = await TestHelpers.CreateTestFileAsync(1024);

    // Act & Assert
    Func<Task> upload = async () => await readOnlyFileStore.UploadSmallAsync(_testContainerId, fileName, content);
    await upload.Should().ThrowAsync<ServiceException>()
        .Where(ex => ex.ResponseStatusCode == 403);
}
```

**Total Tests for SpeFileStore:** 8-10 tests
**Estimated Effort:** 4-6 hours

---

#### Module 2: DataverseWebApiService (Dataverse Integration)

**Functionality:** CRUD operations on `sprk_document` entity

**Test Requirements:**

**2.1 Create Document**
```csharp
[Fact]
public async Task CreateDocumentAsync_ValidRequest_CreatesInDataverse()
{
    // Arrange
    var request = new CreateDocumentRequest
    {
        Name = TestDataHelper.GenerateTestDocumentName(),
        ContainerId = _testContainerId,
        Description = "Integration test document",
        FileSize = 1024,
        MimeType = "text/plain"
    };

    // Act
    var documentId = await _dataverseService.CreateDocumentAsync(request);

    // Assert
    documentId.Should().NotBeEmpty();
    _fixture.TrackCreatedDocument(Guid.Parse(documentId));

    // Verify document exists
    var document = await _dataverseService.GetDocumentAsync(documentId);
    document.Should().NotBeNull();
    document.Name.Should().Be(request.Name);
    document.FileSize.Should().Be(request.FileSize);
}
```

**2.2 Read Document**
```csharp
[Fact]
public async Task GetDocumentAsync_ExistingDocument_ReturnsCorrectData()
{
    // Arrange - Create test document first
    var createRequest = new CreateDocumentRequest
    {
        Name = TestDataHelper.GenerateTestDocumentName(),
        ContainerId = _testContainerId,
        Description = "Test description",
        FileSize = 2048
    };
    var documentId = await _dataverseService.CreateDocumentAsync(createRequest);
    _fixture.TrackCreatedDocument(Guid.Parse(documentId));

    // Act
    var document = await _dataverseService.GetDocumentAsync(documentId);

    // Assert
    document.Should().NotBeNull();
    document.Name.Should().Be(createRequest.Name);
    document.Description.Should().Be(createRequest.Description);
    document.FileSize.Should().Be(createRequest.FileSize);
}
```

**2.3 Update Document**
```csharp
[Fact]
public async Task UpdateDocumentAsync_ExistingDocument_UpdatesInDataverse()
{
    // Arrange - Create test document
    var createRequest = new CreateDocumentRequest { Name = TestDataHelper.GenerateTestDocumentName() };
    var documentId = await _dataverseService.CreateDocumentAsync(createRequest);
    _fixture.TrackCreatedDocument(Guid.Parse(documentId));

    // Act - Update
    var updateRequest = new UpdateDocumentRequest { Description = "Updated description" };
    await _dataverseService.UpdateDocumentAsync(documentId, updateRequest);

    // Assert - Verify update
    var updated = await _dataverseService.GetDocumentAsync(documentId);
    updated.Description.Should().Be("Updated description");
}
```

**2.4 Delete Document**
```csharp
[Fact]
public async Task DeleteDocumentAsync_ExistingDocument_RemovesFromDataverse()
{
    // Arrange - Create test document
    var createRequest = new CreateDocumentRequest { Name = TestDataHelper.GenerateTestDocumentName() };
    var documentId = await _dataverseService.CreateDocumentAsync(createRequest);

    // Act - Delete
    await _dataverseService.DeleteDocumentAsync(documentId);

    // Assert - Verify deletion
    var document = await _dataverseService.GetDocumentAsync(documentId);
    document.Should().BeNull();
}
```

**2.5 Query Documents by Container**
```csharp
[Fact]
public async Task GetDocumentsByContainerAsync_MultipleDocuments_ReturnsAll()
{
    // Arrange - Create 3 test documents in same container
    var docIds = new List<Guid>();
    for (int i = 0; i < 3; i++)
    {
        var request = new CreateDocumentRequest
        {
            Name = $"{TestDataHelper.GenerateTestDocumentName()}_{i}",
            ContainerId = _testContainerId
        };
        var docId = await _dataverseService.CreateDocumentAsync(request);
        docIds.Add(Guid.Parse(docId));
        _fixture.TrackCreatedDocument(Guid.Parse(docId));
    }

    // Act
    var documents = await _dataverseService.GetDocumentsByContainerAsync(_testContainerId);

    // Assert
    documents.Should().HaveCountGreaterOrEqualTo(3);
    documents.Should().Contain(d => docIds.Contains(Guid.Parse(d.DocumentId)));
}
```

**Total Tests for DataverseWebApiService:** 6-8 tests
**Estimated Effort:** 3-4 hours

---

#### Module 3: ServiceBusJobProcessor (Service Bus Integration)

**Functionality:** Background job processing with retry and DLQ

**Test Requirements:**

**3.1 Job Submission**
```csharp
[Fact]
public async Task SubmitJobAsync_ValidJob_SendsToServiceBus()
{
    // Arrange
    var job = new JobContract
    {
        JobId = Guid.NewGuid(),
        JobType = "DocumentProcessing",
        SubjectId = "test-user",
        IdempotencyKey = Guid.NewGuid().ToString(),
        Attempt = 1,
        MaxAttempts = 3,
        Payload = JsonDocument.Parse("{\"fileId\": \"test-file\"}")
    };

    // Act
    await _jobSubmissionService.SubmitJobAsync(job);

    // Assert - Verify message in queue
    var receiver = _serviceBusClient.CreateReceiver("sdap-jobs-test");
    var message = await receiver.ReceiveMessageAsync(timeout: TimeSpan.FromSeconds(5));

    message.Should().NotBeNull();
    message.MessageId.Should().Be(job.JobId.ToString());

    // Cleanup
    await receiver.CompleteMessageAsync(message);
}
```

**3.2 Job Processing**
```csharp
[Fact]
public async Task ServiceBusJobProcessor_ValidJob_ProcessesSuccessfully()
{
    // Arrange - Submit test job
    var job = new JobContract
    {
        JobId = Guid.NewGuid(),
        JobType = "TestJob",
        SubjectId = "test-user",
        IdempotencyKey = Guid.NewGuid().ToString(),
        Attempt = 1,
        MaxAttempts = 3,
        Payload = JsonDocument.Parse("{\"message\": \"test\"}")
    };
    await _jobSubmissionService.SubmitJobAsync(job);

    // Act - Start processor
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var processorTask = _serviceBusJobProcessor.StartAsync(cts.Token);

    // Wait for processing
    await Task.Delay(2000);

    // Assert - Job should be processed (queue empty)
    var receiver = _serviceBusClient.CreateReceiver("sdap-jobs-test");
    var message = await receiver.ReceiveMessageAsync(timeout: TimeSpan.FromSeconds(1));
    message.Should().BeNull("Job should have been processed and removed from queue");

    // Stop processor
    await _serviceBusJobProcessor.StopAsync(CancellationToken.None);
}
```

**3.3 Idempotency (Duplicate Job)**
```csharp
[Fact]
public async Task ServiceBusJobProcessor_DuplicateJob_ProcessesOnlyOnce()
{
    // Arrange - Submit same job twice (same IdempotencyKey)
    var idempotencyKey = Guid.NewGuid().ToString();

    var job1 = new JobContract
    {
        JobId = Guid.NewGuid(),
        JobType = "TestJob",
        IdempotencyKey = idempotencyKey, // Same key
        Attempt = 1,
        MaxAttempts = 3,
        Payload = JsonDocument.Parse("{\"message\": \"first\"}")
    };

    var job2 = new JobContract
    {
        JobId = Guid.NewGuid(),
        JobType = "TestJob",
        IdempotencyKey = idempotencyKey, // Same key
        Attempt = 1,
        MaxAttempts = 3,
        Payload = JsonDocument.Parse("{\"message\": \"second\"}")
    };

    await _jobSubmissionService.SubmitJobAsync(job1);
    await _jobSubmissionService.SubmitJobAsync(job2);

    // Act - Start processor
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var processorTask = _serviceBusJobProcessor.StartAsync(cts.Token);

    // Wait for processing
    await Task.Delay(3000);

    // Assert - Only one job processed (check Redis cache)
    var processedCount = await GetProcessedJobCountAsync(idempotencyKey);
    processedCount.Should().Be(1, "Duplicate job should be rejected via idempotency");

    // Stop processor
    await _serviceBusJobProcessor.StopAsync(CancellationToken.None);
}
```

**3.4 Retry on Failure**
```csharp
[Fact]
public async Task ServiceBusJobProcessor_FailingJob_RetriesMaxAttempts()
{
    // Arrange - Submit job that will fail
    var job = new JobContract
    {
        JobId = Guid.NewGuid(),
        JobType = "FailingTestJob", // Handler that always throws
        IdempotencyKey = Guid.NewGuid().ToString(),
        Attempt = 1,
        MaxAttempts = 3,
        Payload = JsonDocument.Parse("{\"shouldFail\": true}")
    };
    await _jobSubmissionService.SubmitJobAsync(job);

    // Act - Start processor
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var processorTask = _serviceBusJobProcessor.StartAsync(cts.Token);

    // Wait for retries
    await Task.Delay(15000);

    // Assert - Job should be in DLQ after max retries
    var dlqReceiver = _serviceBusClient.CreateReceiver("sdap-jobs-test", new ServiceBusReceiverOptions
    {
        SubQueue = SubQueue.DeadLetter
    });

    var dlqMessage = await dlqReceiver.ReceiveMessageAsync(timeout: TimeSpan.FromSeconds(5));
    dlqMessage.Should().NotBeNull("Job should be in DLQ after max retries");
    dlqMessage.MessageId.Should().Be(job.JobId.ToString());

    // Cleanup
    await dlqReceiver.CompleteMessageAsync(dlqMessage);
    await _serviceBusJobProcessor.StopAsync(CancellationToken.None);
}
```

**Total Tests for ServiceBusJobProcessor:** 6-8 tests
**Estimated Effort:** 4-6 hours

---

#### Module 4: IdempotencyService (Redis Integration)

**Functionality:** Distributed idempotency using Redis

**Test Requirements:**

**4.1 Mark Event as Processed**
```csharp
[Fact]
public async Task MarkProcessedAsync_NewEvent_StoresInRedis()
{
    // Arrange
    var eventId = Guid.NewGuid().ToString();
    var result = new JobOutcome
    {
        JobId = Guid.NewGuid(),
        JobType = "TestJob",
        Status = JobStatus.Completed
    };

    // Act
    await _idempotencyService.MarkProcessedAsync(eventId, result);

    // Assert - Verify stored in Redis
    var wasProcessed = await _idempotencyService.HasBeenProcessedAsync(eventId);
    wasProcessed.Should().BeTrue();

    // Cleanup
    await _redisCache.RemoveAsync($"idempotency:processed:{eventId}");
}
```

**4.2 Check If Event Processed**
```csharp
[Fact]
public async Task HasBeenProcessedAsync_ProcessedEvent_ReturnsTrue()
{
    // Arrange - Mark as processed first
    var eventId = Guid.NewGuid().ToString();
    await _idempotencyService.MarkProcessedAsync(eventId, JobOutcome.Success(Guid.NewGuid(), "TestJob", TimeSpan.FromSeconds(1)));

    // Act
    var wasProcessed = await _idempotencyService.HasBeenProcessedAsync(eventId);

    // Assert
    wasProcessed.Should().BeTrue();

    // Cleanup
    await _redisCache.RemoveAsync($"idempotency:processed:{eventId}");
}
```

**4.3 Distributed Lock**
```csharp
[Fact]
public async Task TryAcquireLockAsync_Concurrency_OnlyOneSucceeds()
{
    // Arrange
    var lockKey = $"test-lock-{Guid.NewGuid()}";

    // Act - Try to acquire lock from two concurrent tasks
    var task1 = _idempotencyService.TryAcquireLockAsync(lockKey, TimeSpan.FromSeconds(10));
    var task2 = _idempotencyService.TryAcquireLockAsync(lockKey, TimeSpan.FromSeconds(10));

    var results = await Task.WhenAll(task1, task2);

    // Assert - Only one should succeed
    results.Count(r => r).Should().Be(1, "Only one task should acquire the lock");

    // Cleanup
    await _redisCache.RemoveAsync($"idempotency:lock:{lockKey}");
}
```

**4.4 TTL Expiration**
```csharp
[Fact]
public async Task MarkProcessedAsync_WithTTL_ExpiresAfterDuration()
{
    // Arrange
    var eventId = Guid.NewGuid().ToString();
    var ttl = TimeSpan.FromSeconds(2); // Short TTL for testing

    // Act
    await _idempotencyService.MarkProcessedAsync(eventId, JobOutcome.Success(Guid.NewGuid(), "TestJob", TimeSpan.FromSeconds(1)), ttl);

    // Assert - Should exist initially
    var wasProcessed = await _idempotencyService.HasBeenProcessedAsync(eventId);
    wasProcessed.Should().BeTrue();

    // Wait for TTL expiration
    await Task.Delay(3000);

    // Assert - Should be expired
    var stillProcessed = await _idempotencyService.HasBeenProcessedAsync(eventId);
    stillProcessed.Should().BeFalse("Entry should have expired after TTL");
}
```

**Total Tests for IdempotencyService:** 5-6 tests
**Estimated Effort:** 2-3 hours

---

### Core Features (Priority 2)

#### Module 5: GraphClientFactory (Authentication)

**Test Requirements:**
- OBO token flow with real user token
- Managed Identity token acquisition
- Token caching and refresh
- Error handling (invalid token, expired token)

**Estimated Tests:** 4-6 tests
**Estimated Effort:** 3-4 hours

#### Module 6: Authorization Pipeline

**Test Requirements:**
- Resource access policy evaluation
- Dataverse permission checks
- OBO endpoint authorization
- Permission denial handling

**Estimated Tests:** 6-8 tests
**Estimated Effort:** 4-5 hours

#### Module 7: Container Management

**Test Requirements:**
- Container creation
- Container listing
- Container metadata retrieval
- Drive association

**Estimated Tests:** 4-5 tests
**Estimated Effort:** 2-3 hours

---

### Supporting Features (Priority 3)

#### Module 8: Distributed Cache (General)

**Test Requirements:**
- GetOrCreate pattern
- Cache versioning
- TTL behavior
- Serialization/deserialization

**Estimated Tests:** 4-5 tests
**Estimated Effort:** 2-3 hours

#### Module 9: Health Checks

**Test Requirements:**
- Redis health check
- Graph API health check
- Dataverse health check
- Composite health endpoint

**Estimated Tests:** 4-5 tests
**Estimated Effort:** 2-3 hours

---

### End-to-End Workflows (Priority 1)

#### E2E 1: Complete Document Upload Workflow

**Scenario:** User uploads document → Background job processes → Metadata stored → Document downloadable

```csharp
[Fact]
public async Task E2E_DocumentUpload_CompleteWorkflow_Success()
{
    // 1. Upload file to SharePoint
    var fileName = TestDataHelper.GenerateTestFileName();
    var fileContent = "Test document content for E2E workflow";
    var uploadStream = new MemoryStream(Encoding.UTF8.GetBytes(fileContent));

    var uploadResult = await _fileStore.UploadSmallAsync(_testContainerId, fileName, uploadStream);
    uploadResult.Should().NotBeNull();
    _fixture.TrackCreatedFile(uploadResult.Id);

    // 2. Submit background job to process document
    var job = new JobContract
    {
        JobId = Guid.NewGuid(),
        JobType = "DocumentProcessing",
        SubjectId = "e2e-test-user",
        IdempotencyKey = Guid.NewGuid().ToString(),
        Attempt = 1,
        MaxAttempts = 3,
        Payload = JsonDocument.Parse($"{{\"fileId\": \"{uploadResult.Id}\", \"containerId\": \"{_testContainerId}\"}}")
    };

    await _jobSubmissionService.SubmitJobAsync(job);

    // 3. Start job processor
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var processorTask = _serviceBusJobProcessor.StartAsync(cts.Token);

    // 4. Wait for job completion (poll Dataverse for document record)
    var documentCreated = await TestHelpers.WaitForConditionAsync(
        async () =>
        {
            var documents = await _dataverseService.GetDocumentsByContainerAsync(_testContainerId);
            return documents.Any(d => d.Name.Contains(fileName));
        },
        timeout: TimeSpan.FromSeconds(20),
        pollInterval: TimeSpan.FromSeconds(2)
    );

    documentCreated.Should().BeTrue("Document metadata should be created in Dataverse after job processing");

    // 5. Verify metadata in Dataverse
    var documents = await _dataverseService.GetDocumentsByContainerAsync(_testContainerId);
    var document = documents.First(d => d.Name.Contains(fileName));

    document.Should().NotBeNull();
    document.FileSize.Should().Be(uploadResult.Size);
    document.Status.Should().Be("Processed");
    _fixture.TrackCreatedDocument(Guid.Parse(document.DocumentId));

    // 6. Download file and verify content
    var downloadStream = await _fileStore.DownloadAsync(_testContainerId, uploadResult.Id);
    var downloadedContent = await new StreamReader(downloadStream).ReadToEndAsync();
    downloadedContent.Should().Be(fileContent);

    // Stop processor
    await _serviceBusJobProcessor.StopAsync(CancellationToken.None);
}
```

**Estimated Tests:** 3-4 E2E workflows
**Estimated Effort:** 6-8 hours

---

## Development Workflow Integration

### Module Development Lifecycle

#### Phase 1: Planning
```
Sprint Planning → Estimate includes test time → Assign tasks
```

#### Phase 2: Implementation
```
Write Code → Write Unit Tests → Run Unit Tests → Code Review (unit tests only)
```

#### Phase 3: Integration Testing (NEW)
```
Write Integration Tests → Run Against Test Environment → Fix Issues → Mark Module Complete
```

#### Phase 4: Code Review
```
Review Code + Unit Tests + Integration Tests → Approve/Request Changes
```

#### Phase 5: Merge
```
PR Approved → CI/CD Runs Integration Tests → Tests Pass → Merge to Main
```

### Definition of Done (Updated)

**A module is "Done" when:**
- ✅ Code implemented according to requirements
- ✅ **Unit tests written and passing** (business logic)
- ✅ **Integration tests written and passing** (real external services)
- ✅ Code reviewed and approved
- ✅ Documentation updated
- ✅ No critical bugs or security issues
- ✅ **CI/CD pipeline passes** (all tests green)

### Developer Checklist

**Before marking task "Complete":**
```
[ ] Code implements all acceptance criteria
[ ] Unit tests cover business logic (70%+ coverage)
[ ] Integration tests validate real external services
[ ] All tests pass locally
[ ] Code follows coding standards
[ ] No secrets or credentials in code
[ ] Documentation updated (README, inline comments)
[ ] PR created with clear description
[ ] Self-review completed
```

---

## CI/CD Integration

### Pipeline Stages

#### Stage 1: Build & Unit Tests (Fast - 2-3 minutes)
```yaml
- name: Build & Unit Tests
  steps:
    - dotnet restore
    - dotnet build --no-restore
    - dotnet test tests/unit/**/*.csproj --no-build
  triggers: Every commit, every PR
  fail_fast: true
```

#### Stage 2: Integration Tests (Slower - 5-10 minutes)
```yaml
- name: Integration Tests
  steps:
    - dotnet test tests/integration/**/*.csproj --no-build
  environment:
    - TEST_CONTAINER_ID: ${{ secrets.TEST_CONTAINER_ID }}
    - GRAPH_CLIENT_SECRET: ${{ secrets.GRAPH_TEST_SP_SECRET }}
    - DATAVERSE_CLIENT_SECRET: ${{ secrets.DATAVERSE_TEST_SP_SECRET }}
    - SERVICE_BUS_CONNECTION: ${{ secrets.SERVICE_BUS_TEST_CONNECTION }}
    - REDIS_CONNECTION: ${{ secrets.REDIS_TEST_CONNECTION }}
  triggers: PR to main, scheduled nightly
  fail_fast: true
```

#### Stage 3: E2E Tests (Slowest - 10-15 minutes)
```yaml
- name: E2E Tests
  steps:
    - dotnet test tests/e2e/**/*.csproj --no-build
  environment: [same as integration]
  triggers: PR to main (manual approval), pre-deployment
  fail_fast: true
```

#### Stage 4: Deploy (After all tests pass)
```yaml
- name: Deploy to Environment
  depends_on:
    - Build & Unit Tests
    - Integration Tests
    - E2E Tests
  steps:
    - Deploy to Azure App Service
  triggers: Manual approval after tests pass
```

### Test Execution Strategy

**Developer Workflow:**
1. Developer commits code
2. **Unit tests run immediately** (fast feedback)
3. Developer creates PR
4. **Integration tests run automatically** (PR validation)
5. If tests pass → Ready for review
6. If tests fail → Fix issues, push new commit

**Scheduled Testing:**
- **Nightly:** Full test suite (unit + integration + E2E)
- **Weekly:** Extended smoke tests + performance tests
- **Before deployment:** Full test suite + manual approval

### Test Results & Reporting

**Test Dashboard (Azure DevOps / GitHub Actions):**
- ✅ Total tests run
- ✅ Pass rate (target: 100%)
- ✅ Failing tests (with logs)
- ✅ Test execution time (track trends)
- ✅ Coverage report (unit test coverage)

**Notifications:**
- ❌ Test failure → Slack notification to team channel
- ❌ Repeated failures → Alert engineering lead
- ✅ All tests pass → Green checkmark on PR

---

## Prioritization & Phasing

### Phase 1: Foundation (Sprint 5 - Week 1)
**Goal:** Build test infrastructure

**Tasks:**
1. Provision test environments (SPE container, Dataverse, Service Bus, Redis)
2. Create test service principals with permissions
3. Configure test secrets in Key Vault
4. Build IntegrationTestFixture base class
5. Build TestHelpers utility class
6. Set up CI/CD pipeline stages

**Deliverables:**
- Test environments provisioned and accessible
- IntegrationTestFixture ready for use
- CI/CD pipeline configured (not yet enforcing)

**Effort:** 8-12 hours
**Owner:** DevOps + Senior Developer
**Success Criteria:** Can run one integration test end-to-end

---

### Phase 2: Critical Path Modules (Sprint 5-6)
**Goal:** Real integration tests for modules blocking production deployment

**Modules:**
1. **SpeFileStore** (8-10 tests, 4-6 hours)
   - Upload small files
   - Upload large files (chunked)
   - Download files
   - Delete files
   - Permission handling

2. **DataverseWebApiService** (6-8 tests, 3-4 hours)
   - CRUD operations on documents
   - Query by container
   - Permission handling

3. **ServiceBusJobProcessor** (6-8 tests, 4-6 hours)
   - Job submission
   - Job processing
   - Idempotency
   - Retry and DLQ

4. **IdempotencyService** (5-6 tests, 2-3 hours)
   - Mark processed
   - Check processed
   - Distributed lock
   - TTL expiration

**Deliverables:**
- 25-32 integration tests for critical modules
- All tests passing in CI/CD
- Tests enforced in PR pipeline (cannot merge if tests fail)

**Effort:** 16-24 hours
**Owner:** Full development team (divide modules)
**Success Criteria:** Critical path modules have real integration test coverage

---

### Phase 3: Core Features (Sprint 6-7)
**Goal:** Real integration tests for supporting features

**Modules:**
1. **GraphClientFactory** (4-6 tests, 3-4 hours)
2. **Authorization Pipeline** (6-8 tests, 4-5 hours)
3. **Container Management** (4-5 tests, 2-3 hours)

**Deliverables:**
- 14-19 additional integration tests
- All core features validated against real services

**Effort:** 12-16 hours
**Owner:** Development team
**Success Criteria:** All core features have integration test coverage

---

### Phase 4: E2E Workflows (Sprint 7)
**Goal:** Validate complete user workflows

**Workflows:**
1. **Document Upload → Process → Metadata → Download** (E2E 1)
2. **Large File Chunked Upload → Process → Verify** (E2E 2)
3. **OBO Upload → Authorization Check → Process** (E2E 3)
4. **Concurrent Uploads → Idempotency → All Processed** (E2E 4)

**Deliverables:**
- 3-4 E2E workflow tests
- Full pipeline validation

**Effort:** 6-8 hours
**Owner:** Senior developer + QA
**Success Criteria:** Critical user workflows validated end-to-end

---

### Phase 5: Supporting Features (Sprint 7+)
**Goal:** Fill remaining gaps

**Modules:**
1. **Distributed Cache** (4-5 tests, 2-3 hours)
2. **Health Checks** (4-5 tests, 2-3 hours)
3. **Additional edge cases** (variable)

**Deliverables:**
- Complete test coverage for all modules

**Effort:** 8-12 hours
**Owner:** Development team
**Success Criteria:** 100% of modules have integration test coverage

---

## Effort Estimates

### Summary by Phase

| Phase | Focus | Tests | Effort (hours) | Duration |
|-------|-------|-------|----------------|----------|
| **Phase 1** | Infrastructure | 0-1 | 8-12 | 1 week |
| **Phase 2** | Critical Path | 25-32 | 16-24 | 2 weeks |
| **Phase 3** | Core Features | 14-19 | 12-16 | 1-2 weeks |
| **Phase 4** | E2E Workflows | 3-4 | 6-8 | 1 week |
| **Phase 5** | Supporting | 8-10 | 8-12 | 1 week |
| **Total** | All modules | **50-66** | **50-72** | **6-7 weeks** |

### Team Distribution

**Option A: Serial (Single Developer):**
- 50-72 hours = **6-9 days** of focused work
- Spread over **6-7 weeks** (parallel with other work)

**Option B: Parallel (Team of 3):**
- 50-72 hours / 3 = **17-24 hours per person**
- Can complete in **2-3 weeks** if prioritized

**Recommended:** Option B (parallel) for Phase 2-4, serial for Phase 1 (foundation)

---

## Dependencies & Blockers

### External Dependencies

#### Azure Resources Required
1. **SharePoint Embedded Test Container**
   - Owner: SPE Admin
   - Timeline: 1-2 days to provision
   - Cost: Minimal (storage only)

2. **Dataverse Test Environment**
   - Owner: Power Platform Admin
   - Timeline: 1-2 days to provision
   - Cost: $20-50/month (trial or dev environment)

3. **Service Bus Test Namespace**
   - Owner: Azure Admin
   - Timeline: < 1 day to provision
   - Cost: ~$10/month (Basic tier)

4. **Redis Test Instance**
   - Owner: Azure Admin
   - Timeline: < 1 day to provision
   - Cost: ~$16/month (Basic C0)

**Total Infrastructure Cost:** ~$50-100/month

#### Service Principals & Permissions
1. **Test Service Principal** (one for all services)
   - Graph API: `Sites.ReadWrite.All`, `Files.ReadWrite.All`
   - Dataverse: System User role in test environment
   - Service Bus: Send/Receive on test queue
   - Owner: Azure AD Admin
   - Timeline: 1 day to create and assign permissions

2. **Key Vault Access**
   - Store test secrets in Key Vault
   - Grant test SP access to secrets
   - Owner: Azure Admin
   - Timeline: < 1 day

**Critical Path:** Infrastructure provisioning blocks Phase 1

---

### Internal Dependencies

#### Code Dependencies
1. **JobProcessor removal** already complete ✅
2. **Service Bus emulator** already configured ✅
3. **Redis provisioning** documentation complete ✅
4. **Analyzer/standards** deferred (not blocking)

#### Team Dependencies
1. **DevOps support** needed for CI/CD pipeline configuration
2. **Azure Admin access** needed for resource provisioning
3. **Power Platform Admin** needed for Dataverse environment

#### Knowledge Dependencies
1. **Graph SDK testing patterns** (learn from Microsoft docs)
2. **Dataverse Web API testing** (learn from Microsoft docs)
3. **Service Bus testing patterns** (documented in KM guides)
4. **xUnit test fixtures** (team already familiar)

**No critical knowledge gaps** - patterns well-documented

---

## Success Criteria

### Phase 1 Success Criteria (Infrastructure)
- [ ] Test SharePoint Embedded container provisioned and accessible
- [ ] Test Dataverse environment provisioned with test data isolated
- [ ] Test Service Bus namespace created with test queue
- [ ] Test Redis instance provisioned and accessible
- [ ] Test service principal created with all required permissions
- [ ] Test secrets stored in Key Vault and accessible
- [ ] IntegrationTestFixture class created and tested
- [ ] TestHelpers utility class created
- [ ] CI/CD pipeline configured with test stages (not enforcing yet)
- [ ] One smoke integration test runs successfully end-to-end

### Phase 2 Success Criteria (Critical Path Modules)
- [ ] SpeFileStore: 8-10 integration tests written and passing
- [ ] DataverseWebApiService: 6-8 integration tests written and passing
- [ ] ServiceBusJobProcessor: 6-8 integration tests written and passing
- [ ] IdempotencyService: 5-6 integration tests written and passing
- [ ] All tests run in CI/CD pipeline automatically
- [ ] PR pipeline enforces test passing (cannot merge if tests fail)
- [ ] 90%+ test pass rate
- [ ] Test execution time < 10 minutes total

### Phase 3 Success Criteria (Core Features)
- [ ] GraphClientFactory: 4-6 integration tests written and passing
- [ ] Authorization: 6-8 integration tests written and passing
- [ ] Container Management: 4-5 integration tests written and passing
- [ ] All core features validated against real services
- [ ] Test coverage report shows 80%+ integration test coverage

### Phase 4 Success Criteria (E2E Workflows)
- [ ] 3-4 E2E workflow tests written and passing
- [ ] Critical user scenarios validated end-to-end
- [ ] E2E tests run in pre-deployment pipeline
- [ ] Manual approval required if E2E tests fail

### Overall Success Criteria (Production Ready)
- [ ] 50-66 integration tests written and passing
- [ ] 100% pass rate in last 5 CI/CD runs
- [ ] All modules have integration test coverage
- [ ] Test execution time < 15 minutes (integration + E2E)
- [ ] No test flakiness (tests are deterministic)
- [ ] Test cleanup automation works (no orphaned data)
- [ ] Team trained on writing integration tests
- [ ] Testing strategy documented and followed

---

## Risks & Mitigation

### Risk 1: Test Infrastructure Delays
**Risk:** Azure resource provisioning takes longer than expected

**Impact:** Phase 1 delayed, blocks all testing work

**Likelihood:** Medium

**Mitigation:**
- Start provisioning requests ASAP (Sprint 5 Day 1)
- Use Service Bus emulator as temporary fallback
- Use Docker containers for Redis (local testing)
- Escalate to management if delays > 3 days

### Risk 2: Test Flakiness
**Risk:** Integration tests fail intermittently due to network, timeouts, service throttling

**Impact:** CI/CD unreliable, team loses trust in tests

**Likelihood:** Medium-High

**Mitigation:**
- Use retry logic with exponential backoff
- Set generous timeouts (30s default)
- Implement test isolation (unique test data per run)
- Monitor test results for flakiness patterns
- Implement "quarantine" for flaky tests (run separately)

### Risk 3: Test Environment Pollution
**Risk:** Test data not cleaned up, environments become cluttered

**Impact:** Tests fail due to data conflicts, manual cleanup required

**Likelihood:** Medium

**Mitigation:**
- Implement IAsyncLifetime cleanup in all fixtures
- Use unique identifiers (GUIDs) for all test data
- Implement nightly cleanup job (delete test data > 7 days old)
- Monitor test environment storage usage

### Risk 4: Insufficient Test Coverage
**Risk:** Tests written but don't cover critical scenarios

**Impact:** False sense of security, issues still reach production

**Likelihood:** Medium

**Mitigation:**
- Peer review all integration tests (not just code)
- Create test coverage checklist per module
- Run E2E tests before every production deployment
- Track production incidents vs. test coverage gaps

### Risk 5: Excessive Test Execution Time
**Risk:** Integration tests take too long, slows down CI/CD

**Impact:** Developers skip tests or disable in CI/CD

**Likelihood:** Low-Medium

**Mitigation:**
- Run unit tests first (fast feedback)
- Run integration tests only on PR to main (not every commit)
- Parallelize test execution where possible
- Optimize slow tests (reduce delays, use smaller test data)
- Set hard limit: Integration tests must complete in < 10 minutes

### Risk 6: Cost Overruns
**Risk:** Test environments cost more than expected

**Impact:** Management pushes back, reduces test infrastructure

**Likelihood:** Low

**Mitigation:**
- Use cheapest tiers (Basic/Standard, not Premium)
- Share test environments across team (not per-developer)
- Implement auto-shutdown for dev environments (nights/weekends)
- Monitor Azure spending, set budget alerts
- Document ROI (cost of testing < cost of one production incident)

---

## Prioritization: Testing vs Frontend Development

### Critical Question
> "Is this required before we move to building our front-end React application which is scheduled?"

### Answer: PARTIAL OVERLAP RECOMMENDED

**Short Answer:** No, not ALL testing is required before starting frontend. But SOME testing should happen in parallel.

### Recommended Approach

#### Parallel Track Strategy

**Track 1: Backend Testing (Phase 1-2)**
- Sprint 5: Infrastructure + Critical Path Modules
- **Blocks:** Production deployment of backend
- **Does NOT block:** Frontend development

**Track 2: Frontend Development**
- Sprint 5+: Start React application development
- **Blocks:** Nothing (can start immediately)
- **Depends on:** Stable API contracts (already defined)

**Synchronization Points:**
- Frontend needs working API endpoints (already exist)
- Frontend needs authentication flow (already works)
- Integration testing validates API behavior (reduces frontend debugging)

#### Timeline Recommendation

```
Sprint 5:
├── Backend: Phase 1 (Infrastructure) + Phase 2 (Critical Path)
└── Frontend: Start React app, API integration, mock data

Sprint 6:
├── Backend: Phase 3 (Core Features) + Phase 4 (E2E)
└── Frontend: Continue development with real API

Sprint 7:
├── Backend: Phase 5 (Supporting)
└── Frontend: Continue development

Sprint 8+:
├── Backend: Maintenance, bug fixes
└── Frontend: Complete development, user testing
```

### What Frontend Needs From Backend Testing

**MUST HAVE (Before frontend integration):**
1. ✅ API contracts stable (already true)
2. ✅ Authentication works (already true)
3. ⚠️ API endpoints return correct data (needs validation via integration tests)

**SHOULD HAVE (Reduces frontend debugging):**
4. ⚠️ File upload/download validated (Phase 2 testing)
5. ⚠️ Authorization enforced correctly (Phase 3 testing)
6. ⚠️ Error responses consistent (already documented, needs validation)

**NICE TO HAVE (Reduces production issues):**
7. ⚠️ E2E workflows validated (Phase 4 testing)
8. ⚠️ All edge cases tested (Phase 5 testing)

### Decision Matrix

| Scenario | Testing Status | Frontend Impact | Recommendation |
|----------|----------------|-----------------|----------------|
| **API contracts defined** | ✅ Documented | Can mock responses | ✅ **Start frontend now** |
| **API endpoints exist** | ✅ Working | Can call APIs | ✅ **Start frontend now** |
| **Integration tests (Phase 2)** | ⚠️ In progress | Better stability | ⏸️ **Parallel development** |
| **E2E tests (Phase 4)** | ❌ Not started | Fewer bugs | ⏸️ **Nice to have, not blocking** |

### Recommendation: START FRONTEND NOW

**Rationale:**
1. **API contracts are stable** - Frontend can develop against defined contracts
2. **Endpoints exist and work** - Frontend can integrate immediately
3. **Integration tests run in parallel** - Improves backend quality without blocking frontend
4. **Faster time to market** - Both teams work simultaneously

**Coordination Points:**
- **Weekly sync:** Backend testing team reports findings to frontend team
- **Breaking changes:** Backend notifies frontend immediately if integration tests reveal API changes needed
- **Shared bug tracking:** Frontend logs API issues, backend prioritizes based on severity

**Risks of Starting Frontend Before Testing Complete:**
- Frontend may encounter backend bugs (mitigated by integration tests finding them early)
- API contracts may change if issues found (unlikely - contracts already defined)
- Frontend may build workarounds for backend bugs (code review prevents this)

**Benefits of Parallel Development:**
- ✅ Faster overall delivery
- ✅ Frontend team not blocked
- ✅ Integration tests catch issues before frontend integrates deeply
- ✅ Backend testing validates what frontend actually uses (prioritizes real scenarios)

---

## Conclusion

### The Path Forward

**Phase 1 (Sprint 5, Week 1):**
- Provision test infrastructure
- Build test fixtures and helpers
- Configure CI/CD pipeline
- **Start frontend development in parallel**

**Phase 2 (Sprint 5-6):**
- Write integration tests for critical path modules
- Enforce tests in CI/CD (PR requirement)
- **Continue frontend development with stable backend**

**Phase 3-5 (Sprint 6-7+):**
- Complete test coverage for all modules
- Validate E2E workflows
- **Frontend integration with fully tested backend**

**Production Deployment:**
- Backend: After Phase 4 complete (E2E tests pass)
- Frontend: After user acceptance testing complete
- **Both:** Full integration tests passing in CI/CD

### Success Metrics

**3 Months Post-Implementation:**
- ✅ 90%+ integration test pass rate
- ✅ < 2 production incidents caused by untested code
- ✅ 100% of new modules include integration tests
- ✅ < 15 minute test execution time in CI/CD
- ✅ Team trained and following testing strategy

**ROI:**
- Cost of testing: ~50-72 hours + $50-100/month infrastructure
- Cost of ONE production incident: Hours of debugging + customer impact + reputation
- **ROI: Positive after first prevented incident**

### Final Recommendation

**START FRONTEND DEVELOPMENT NOW**

**Implement Testing Strategy in Parallel**

**Both tracks deliver value independently and together**

---

**Document Status:** Planning Complete - Ready for Sprint 5 Kickoff
**Next Steps:**
1. Review with team
2. Prioritize Phase 1 tasks
3. Assign owners
4. Begin infrastructure provisioning
5. Start frontend development

**Questions or Feedback:** Contact development team lead
