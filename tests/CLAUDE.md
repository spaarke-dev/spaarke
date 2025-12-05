# CLAUDE.md - Tests Module

> **Last Updated**: December 3, 2025
>
> **Purpose**: Module-specific instructions for unit and integration tests.

## Module Overview

Test projects for validating Spaarke components:
- **Unit Tests** - Isolated component testing with mocks
- **Integration Tests** - End-to-end API testing with real (or emulated) dependencies

## Key Structure

```
tests/
├── unit/
│   ├── Spe.Bff.Api.Tests/        # BFF API unit tests
│   └── Spaarke.Plugins.Tests/    # Plugin unit tests
└── integration/
    └── Spe.Integration.Tests/    # API integration tests
```

## Test Patterns

### Unit Test Structure (Arrange-Act-Assert)
```csharp
[Fact]
public async Task MethodName_ExpectedBehavior_WhenCondition()
{
    // Arrange
    var mockDependency = new Mock<IDependency>();
    mockDependency.Setup(d => d.GetDataAsync()).ReturnsAsync(expectedData);
    var sut = new ServiceUnderTest(mockDependency.Object);

    // Act
    var result = await sut.ProcessAsync();

    // Assert
    result.Should().NotBeNull();
    result.Status.Should().Be("Success");
}
```

### Integration Test Pattern
```csharp
public class DocumentEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public DocumentEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetDocument_ReturnsOk_WhenDocumentExists()
    {
        // Arrange
        var documentId = "test-doc-1";

        // Act
        var response = await _client.GetAsync($"/api/documents/{documentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

## Naming Conventions

### Test Class Names
```
{ClassUnderTest}Tests.cs
```
Examples:
- `AuthorizationServiceTests.cs`
- `DocumentEndpointsTests.cs`
- `SpeFileStoreTests.cs`

### Test Method Names
```
{MethodName}_{ExpectedBehavior}_{Condition}
```
Examples:
- `GetDocument_ReturnsStream_WhenDocumentExists`
- `UploadFile_ThrowsUnauthorized_WhenTokenExpired`
- `Validate_ReturnsFalse_WhenRequiredFieldMissing`

## Mocking Guidelines

### Use Moq for Dependencies
```csharp
// ✅ CORRECT: Mock at boundaries
var mockFileStore = new Mock<SpeFileStore>();
mockFileStore
    .Setup(s => s.GetFileContentAsync(It.IsAny<string>()))
    .ReturnsAsync(new MemoryStream(Encoding.UTF8.GetBytes("content")));

// ❌ WRONG: Don't mock what you don't own (e.g., GraphServiceClient internals)
```

### Use FluentAssertions
```csharp
// ✅ CORRECT: Fluent, readable assertions
result.Should().NotBeNull();
result.Items.Should().HaveCount(3);
result.Items.Should().Contain(x => x.Name == "Test");

// ❌ WRONG: Basic Assert
Assert.NotNull(result);
Assert.Equal(3, result.Items.Count);
```

## Test Data

### Use Test Data Builders
```csharp
public class DocumentBuilder
{
    private string _id = Guid.NewGuid().ToString();
    private string _name = "test-document.pdf";
    private int _size = 1024;

    public DocumentBuilder WithId(string id) { _id = id; return this; }
    public DocumentBuilder WithName(string name) { _name = name; return this; }
    public DocumentBuilder WithSize(int size) { _size = size; return this; }

    public Document Build() => new Document
    {
        Id = _id,
        Name = _name,
        Size = _size
    };
}

// Usage
var document = new DocumentBuilder()
    .WithName("contract.pdf")
    .WithSize(5000)
    .Build();
```

## Coverage Requirements

Run tests with coverage:
```bash
dotnet test --collect:"XPlat Code Coverage" --settings config/coverlet.runsettings
```

**Coverage Targets:**
- Core services: 80%+ coverage
- Endpoints: 70%+ coverage
- Utilities: 90%+ coverage

## Integration Test Configuration

```json
// appsettings.json (test project)
{
    "TestSettings": {
        "UseInMemoryDatabase": true,
        "MockExternalServices": true
    }
}
```

## Do's and Don'ts

| ✅ DO | ❌ DON'T |
|-------|----------|
| Test one thing per test | Test multiple behaviors in one test |
| Use descriptive test names | Use vague names like `Test1` |
| Mock at boundaries | Mock internal implementation details |
| Use FluentAssertions | Use basic Assert statements |
| Keep tests fast (<1s each) | Write slow tests that hit real services |
| Clean up test data | Leave test artifacts behind |

## Running Tests

```bash
# Run all tests
dotnet test

# Run specific project
dotnet test tests/unit/Spe.Bff.Api.Tests/

# Run with filter
dotnet test --filter "FullyQualifiedName~AuthorizationService"

# Run with coverage
dotnet test --collect:"XPlat Code Coverage" --settings config/coverlet.runsettings

# Generate coverage report (requires reportgenerator tool)
reportgenerator -reports:**/coverage.cobertura.xml -targetdir:coverage-report
```

---

*Refer to root `CLAUDE.md` for repository-wide standards.*
