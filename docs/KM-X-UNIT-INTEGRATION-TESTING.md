# xUnit Integration Testing with WebApplicationFactory for Spaarke

## Overview
This guide covers integration testing patterns using xUnit and WebApplicationFactory for testing Minimal APIs, Service Bus workers, and end-to-end scenarios in the Spaarke platform.

## NuGet Packages Required
```xml
<PackageReference Include="xunit" Version="2.6.6" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.5.6" />
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.0" />
<PackageReference Include="Microsoft.AspNetCore.TestHost" Version="8.0.0" />
<PackageReference Include="Testcontainers" Version="3.7.0" />
<PackageReference Include="Testcontainers.Redis" Version="3.7.0" />
<PackageReference Include="Testcontainers.Azurite" Version="3.7.0" />
<PackageReference Include="Moq" Version="4.20.70" />
<PackageReference Include="FluentAssertions" Version="6.12.0" />
<PackageReference Include="Bogus" Version="35.3.0" />
<PackageReference Include="WireMock.Net" Version="1.5.46" />
```

## Base Test Infrastructure

### Custom WebApplicationFactory
```csharp
public class SpaarkeWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly TestcontainerDatabase _redisContainer;
    private readonly TestcontainerDatabase _azuriteContainer;
    
    public SpaarkeWebApplicationFactory()
    {
        // Use Testcontainers for real Redis
        _redisContainer = new TestcontainersBuilder<RedisTestcontainer>()
            .WithDatabase(new RedisTestcontainerConfiguration())
            .Build();
            
        // Use Azurite for Azure Storage emulation
        _azuriteContainer = new TestcontainersBuilder<AzuriteTestcontainer>()
            .WithImage("mcr.microsoft.com/azure-storage/azurite:latest")
            .WithPortBinding(10000, 10000) // Blob
            .WithPortBinding(10001, 10001) // Queue
            .WithPortBinding(10002, 10002) // Table
            .Build();
    }
    
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove real services
            RemoveService<DbContext>(services);
            RemoveService<ServiceBusClient>(services);
            RemoveService<GraphServiceClient>(services);
            
            // Add test doubles
            services.AddSingleton<ISystemTime, TestSystemTime>();
            services.AddSingleton<ICorrelationIdProvider, TestCorrelationIdProvider>();
            
            // Configure test authentication
            services.AddAuthentication("Test")
                .AddScheme<TestAuthenticationSchemeOptions, TestAuthenticationHandler>("Test", options => { });
            
            // Override configuration
            services.Configure<SpaarkeOptions>(options =>
            {
                options.Redis.ConnectionString = _redisContainer.ConnectionString;
                options.Storage.ConnectionString = _azuriteContainer.ConnectionString;
            });
        });
        
        builder.ConfigureTestServices(services =>
        {
            // Replace with mocks for external dependencies
            services.AddSingleton(Mock.Of<IOrganizationService>());
            services.AddSingleton(CreateMockGraphClient());
            services.AddSingleton(CreateInMemoryServiceBus());
        });
        
        builder.UseEnvironment("Testing");
    }
    
    private static void RemoveService<TService>(IServiceCollection services)
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(TService));
        if (descriptor != null)
            services.Remove(descriptor);
    }
    
    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();
        await _azuriteContainer.StartAsync();
    }
    
    public new async Task DisposeAsync()
    {
        await _redisContainer.DisposeAsync();
        await _azuriteContainer.DisposeAsync();
        await base.DisposeAsync();
    }
}
```

### Test Authentication Handler
```csharp
public class TestAuthenticationHandler : AuthenticationHandler<TestAuthenticationSchemeOptions>
{
    public TestAuthenticationHandler(IOptionsMonitor<TestAuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock)
        : base(options, logger, encoder, clock) { }
    
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check for test user header
        if (!Request.Headers.TryGetValue("X-Test-User", out var userValue))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
        
        var claims = userValue.ToString() switch
        {
            "admin" => new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "test-admin-id"),
                new Claim(ClaimTypes.Name, "Test Admin"),
                new Claim(ClaimTypes.Role, "Administrator"),
                new Claim("oid", "00000000-0000-0000-0000-000000000001"),
                new Claim("tid", "test-tenant-id")
            },
            "user" => new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
                new Claim(ClaimTypes.Name, "Test User"),
                new Claim(ClaimTypes.Role, "User"),
                new Claim("oid", "00000000-0000-0000-0000-000000000002"),
                new Claim("tid", "test-tenant-id")
            },
            _ => null
        };
        
        if (claims == null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid test user"));
        }
        
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");
        
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
```

## Integration Test Patterns

### API Endpoint Testing
```csharp
public class DocumentApiTests : IClassFixture<SpaarkeWebApplicationFactory>, IAsyncLifetime
{
    private readonly SpaarkeWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly Faker<DocumentRequest> _documentFaker;
    
    public DocumentApiTests(SpaarkeWebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        
        // Configure Bogus faker for test data
        _documentFaker = new Faker<DocumentRequest>()
            .RuleFor(d => d.Name, f => f.System.FileName())
            .RuleFor(d => d.MatterId, f => f.Random.Guid())
            .RuleFor(d => d.FileSize, f => f.Random.Long(1024, 1024 * 1024 * 10));
    }
    
    [Fact]
    public async Task CreateDocument_ValidRequest_Returns201()
    {
        // Arrange
        var request = _documentFaker.Generate();
        _client.DefaultRequestHeaders.Add("X-Test-User", "user");
        
        // Act
        var response = await _client.PostAsJsonAsync("/api/documents", request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        
        var document = await response.Content.ReadFromJsonAsync<DocumentResponse>();
        document.Should().NotBeNull();
        document!.Id.Should().NotBeEmpty();
        document.Name.Should().Be(request.Name);
    }
    
    [Fact]
    public async Task GetDocument_Unauthorized_Returns401()
    {
        // Arrange - No authentication header
        
        // Act
        var response = await _client.GetAsync("/api/documents/00000000-0000-0000-0000-000000000001");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
    
    [Theory]
    [InlineData("", "File name is required")]
    [InlineData("../../../etc/passwd", "Invalid file name")]
    [InlineData("file.exe", "File type not allowed")]
    public async Task CreateDocument_InvalidFileName_Returns400(string fileName, string expectedError)
    {
        // Arrange
        var request = new DocumentRequest { Name = fileName, MatterId = Guid.NewGuid() };
        _client.DefaultRequestHeaders.Add("X-Test-User", "user");
        
        // Act
        var response = await _client.PostAsJsonAsync("/api/documents", request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problemDetails!.Detail.Should().Contain(expectedError);
    }
    
    public Task InitializeAsync() => _factory.InitializeAsync();
    public Task DisposeAsync() => _factory.DisposeAsync();
}
```

### Service Bus Worker Testing
```csharp
public class DocumentProcessingWorkerTests : IClassFixture<SpaarkeWebApplicationFactory>
{
    private readonly SpaarkeWebApplicationFactory _factory;
    private readonly IServiceProvider _serviceProvider;
    
    public DocumentProcessingWorkerTests(SpaarkeWebApplicationFactory factory)
    {
        _factory = factory;
        _serviceProvider = _factory.Services;
    }
    
    [Fact]
    public async Task ProcessJob_ValidDocument_CompletesSuccessfully()
    {
        // Arrange
        var worker = _serviceProvider.GetRequiredService<DocumentProcessingWorker>();
        var envelope = new JobEnvelope
        {
            JobId = Guid.NewGuid(),
            JobType = "DocumentProcess",
            SubjectId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid().ToString(),
            IdempotencyKey = Guid.NewGuid().ToString(),
            Attempt = 1,
            MaxAttempts = 3,
            PayloadJson = JsonSerializer.Serialize(new DocumentProcessingPayload
            {
                DocumentId = Guid.NewGuid(),
                Operation = DocumentOperation.ExtractText
            })
        };
        
        // Mock Service Bus message
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromObjectAsJson(envelope),
            messageId: envelope.JobId.ToString(),
            correlationId: envelope.CorrelationId);
        
        var args = new ProcessMessageEventArgs(
            message,
            new ServiceBusReceiver(),
            CancellationToken.None);
        
        // Act
        await worker.ProcessMessageAsync(args);
        
        // Assert
        // Verify message was completed
        args.IsMessageCompleted.Should().BeTrue();
        
        // Verify idempotency key was cached
        var cache = _serviceProvider.GetRequiredService<IDistributedCache>();
        var cached = await cache.GetAsync($"job:processed:{envelope.IdempotencyKey}");
        cached.Should().NotBeNull();
    }
    
    [Fact]
    public async Task ProcessJob_TransientError_Retries()
    {
        // Arrange
        var mockService = new Mock<IDocumentService>();
        mockService
            .SetupSequence(x => x.ProcessAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TransientException("Network error"))
            .ThrowsAsync(new TransientException("Still failing"))
            .ReturnsAsync(new Document());
        
        var worker = CreateWorkerWithMock(mockService.Object);
        
        // Act & Assert
        // Process message 3 times (initial + 2 retries)
        for (int i = 1; i <= 3; i++)
        {
            var envelope = CreateEnvelope(attempt: i);
            await ProcessMessage(worker, envelope);
        }
        
        mockService.Verify(x => x.ProcessAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), 
            Times.Exactly(3));
    }
}
```

### End-to-End Scenario Testing
```csharp
public class DocumentWorkflowTests : IClassFixture<SpaarkeWebApplicationFactory>
{
    private readonly SpaarkeWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly IServiceProvider _serviceProvider;
    
    [Fact]
    public async Task UploadDocument_TriggersProcessing_UpdatesSearch()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var documentId = Guid.NewGuid();
        var fileName = "test-document.pdf";
        
        // Step 1: Upload document
        _client.DefaultRequestHeaders.Add("X-Test-User", "user");
        
        var uploadRequest = new DocumentUploadRequest
        {
            FileName = fileName,
            MatterId = Guid.NewGuid(),
            FileSize = 1024
        };
        
        var uploadResponse = await _client.PostAsJsonAsync("/api/documents/upload", uploadRequest);
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var document = await uploadResponse.Content.ReadFromJsonAsync<DocumentResponse>();
        
        // Step 2: Verify Service Bus message was sent
        var serviceBus = scope.ServiceProvider.GetRequiredService<InMemoryServiceBus>();
        var messages = await serviceBus.GetMessagesAsync("document-events");
        messages.Should().ContainSingle(m => m.Subject == "DocumentCreated");
        
        // Step 3: Process the message
        var worker = scope.ServiceProvider.GetRequiredService<DocumentProcessingWorker>();
        await worker.ProcessTestMessageAsync(messages.First());
        
        // Step 4: Verify document was indexed for search
        var searchService = scope.ServiceProvider.GetRequiredService<ISearchService>();
        var searchResults = await searchService.SearchAsync(fileName);
        searchResults.Should().ContainSingle(r => r.DocumentId == document!.Id);
        
        // Step 5: Verify audit trail
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
        var auditEntries = await auditService.GetEntriesAsync(document.Id);
        auditEntries.Should().Contain(e => e.Action == "DocumentUploaded");
        auditEntries.Should().Contain(e => e.Action == "DocumentProcessed");
        auditEntries.Should().Contain(e => e.Action == "DocumentIndexed");
    }
}
```

### Performance Testing
```csharp
public class PerformanceTests : IClassFixture<SpaarkeWebApplicationFactory>
{
    [Fact]
    public async Task GetDocuments_LoadTest_MeetsPerformanceTargets()
    {
        // Arrange
        var tasks = new List<Task<HttpResponseMessage>>();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "user");
        
        // Warmup
        await client.GetAsync("/api/documents");
        
        // Act - Send 100 concurrent requests
        var stopwatch = Stopwatch.StartNew();
        
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(client.GetAsync("/api/documents"));
        }
        
        var responses = await Task.WhenAll(tasks);
        stopwatch.Stop();
        
        // Assert
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));
        
        // Performance assertions
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000, "100 requests should complete within 5 seconds");
        
        var responseTimes = responses.Select(r => 
            double.Parse(r.Headers.GetValues("X-Response-Time").First()));
        
        responseTimes.Average().Should().BeLessThan(100, "Average response time should be under 100ms");
        responseTimes.Max().Should().BeLessThan(500, "Max response time should be under 500ms");
        
        // P95 calculation
        var p95 = responseTimes.OrderBy(t => t).Skip((int)(responseTimes.Count() * 0.95)).First();
        p95.Should().BeLessThan(200, "P95 response time should be under 200ms");
    }
}
```

### Mock External Services
```csharp
public class ExternalServiceMocks
{
    public static WireMockServer CreateGraphApiMock()
    {
        var server = WireMockServer.Start();
        
        // Mock drive endpoint
        server
            .Given(Request.Create()
                .WithPath("/drives/*/items/*")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    id = Guid.NewGuid().ToString(),
                    name = "test-file.pdf",
                    size = 1024,
                    lastModifiedDateTime = DateTimeOffset.UtcNow
                }));
        
        // Mock upload session
        server
            .Given(Request.Create()
                .WithPath("/drives/*/items/*/createUploadSession")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBodyAsJson(new
                {
                    uploadUrl = $"{server.Urls[0]}/upload/{Guid.NewGuid()}",
                    expirationDateTime = DateTimeOffset.UtcNow.AddHours(24)
                }));
        
        return server;
    }
    
    public static Mock<IOrganizationService> CreateDataverseMock()
    {
        var mock = new Mock<IOrganizationService>();
        
        // Setup common operations
        mock.Setup(x => x.Create(It.IsAny<Entity>()))
            .Returns(() => Guid.NewGuid());
        
        mock.Setup(x => x.Retrieve(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ColumnSet>()))
            .Returns((string entityName, Guid id, ColumnSet columns) =>
            {
                var entity = new Entity(entityName, id);
                entity["name"] = $"Test {entityName}";
                entity["createdon"] = DateTime.UtcNow;
                return entity;
            });
        
        mock.Setup(x => x.RetrieveMultiple(It.IsAny<QueryExpression>()))
            .Returns((QueryExpression query) =>
            {
                var collection = new EntityCollection();
                collection.Entities.Add(new Entity(query.EntityName, Guid.NewGuid()));
                return collection;
            });
        
        return mock;
    }
}
```

### Database Testing with EF Core
```csharp
public class DatabaseIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SpaarkeDbContext> _options;
    
    public DatabaseIntegrationTests()
    {
        // Use in-memory SQLite for tests
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        
        _options = new DbContextOptionsBuilder<SpaarkeDbContext>()
            .UseSqlite(_connection)
            .Options;
        
        // Create schema
        using var context = new SpaarkeDbContext(_options);
        context.Database.EnsureCreated();
    }
    
    [Fact]
    public async Task DocumentRepository_Create_PersistsToDatabase()
    {
        // Arrange
        using var context = new SpaarkeDbContext(_options);
        var repository = new DocumentRepository(context);
        
        var document = new Document
        {
            Id = Guid.NewGuid(),
            Name = "test.pdf",
            MatterId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        
        // Act
        await repository.CreateAsync(document);
        await context.SaveChangesAsync();
        
        // Assert
        using var verifyContext = new SpaarkeDbContext(_options);
        var saved = await verifyContext.Documents.FindAsync(document.Id);
        saved.Should().NotBeNull();
        saved!.Name.Should().Be(document.Name);
    }
    
    public void Dispose()
    {
        _connection?.Dispose();
    }
}
```

### Test Data Builders
```csharp
public class TestDataBuilder
{
    private readonly Faker _faker = new();
    
    public DocumentBuilder Document() => new DocumentBuilder(_faker);
    public MatterBuilder Matter() => new MatterBuilder(_faker);
    public UserBuilder User() => new UserBuilder(_faker);
    
    public class DocumentBuilder
    {
        private readonly Faker _faker;
        private Guid _id = Guid.NewGuid();
        private string _name;
        private Guid _matterId = Guid.NewGuid();
        private long _size;
        
        public DocumentBuilder(Faker faker)
        {
            _faker = faker;
            _name = faker.System.FileName();
            _size = faker.Random.Long(1024, 10485760);
        }
        
        public DocumentBuilder WithId(Guid id)
        {
            _id = id;
            return this;
        }
        
        public DocumentBuilder WithName(string name)
        {
            _name = name;
            return this;
        }
        
        public DocumentBuilder WithMatter(Guid matterId)
        {
            _matterId = matterId;
            return this;
        }
        
        public Document Build() => new()
        {
            Id = _id,
            Name = _name,
            MatterId = _matterId,
            Size = _size,
            CreatedAt = DateTimeOffset.UtcNow
        };
        
        public List<Document> BuildMany(int count) =>
            Enumerable.Range(0, count).Select(_ => 
                new DocumentBuilder(_faker).Build()).ToList();
    }
}
```

## Test Configuration

### xunit.runner.json
```json
{
  "$schema": "https://xunit.net/schema/current/xunit.runner.schema.json",
  "parallelizeAssembly": false,
  "parallelizeTestCollections": true,
  "maxParallelThreads": 4,
  "methodDisplay": "method",
  "methodDisplayOptions": "all",
  "diagnosticMessages": false,
  "internalDiagnosticMessages": false,
  "preEnumerateTheories": true
}
```

### Test Collection for Shared Context
```csharp
[CollectionDefinition("Spaarke Integration Tests")]
public class SpaarkeTestCollection : ICollectionFixture<SpaarkeWebApplicationFactory>
{
    // This class has no code, and is never created.
    // Its purpose is to be the place to apply [CollectionDefinition]
}

[Collection("Spaarke Integration Tests")]
public class SharedContextTests
{
    private readonly SpaarkeWebApplicationFactory _factory;
    
    public SharedContextTests(SpaarkeWebApplicationFactory factory)
    {
        _factory = factory;
    }
}
```

## Key Testing Principles for Spaarke

1. **Use WebApplicationFactory** - Test the real application setup
2. **Isolate external dependencies** - Mock Graph, Dataverse, Service Bus
3. **Use Testcontainers** - Real Redis/Storage for integration tests
4. **Test authentication/authorization** - Verify security at integration level
5. **Generate test data with Bogus** - Realistic, varied test scenarios
6. **Assert on behavior, not implementation** - Focus on outcomes
7. **Test error scenarios** - Transient failures, validation, auth errors
8. **Measure performance** - Ensure SLAs are met