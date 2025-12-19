# Mocking Patterns

> **Domain**: Testing / Test Doubles
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-022

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `tests/unit/Sprk.Bff.Api.Tests/Mocks/FakeGraphClientFactory.cs` | Custom fake |
| `tests/unit/Sprk.Bff.Api.Tests/Integration/DataverseWebApiWireMockTests.cs` | HTTP mocking |
| `tests/unit/Sprk.Bff.Api.Tests/AuthorizationTests.cs` | Test data source |

---

## Moq Patterns

### Basic Setup

```csharp
var mockService = new Mock<IMyService>();
mockService.Setup(s => s.GetDataAsync(It.IsAny<string>()))
    .ReturnsAsync(expectedData);

var sut = new Consumer(mockService.Object);
```

### Strict Mocking

```csharp
var mockService = new Mock<IMyService>(MockBehavior.Strict);
mockService.Setup(s => s.GetDataAsync("specific-id"))
    .ReturnsAsync(expectedData);

// Throws if unexpected method called
```

### Sequential Returns

```csharp
mockService.SetupSequence(s => s.GetDataAsync(It.IsAny<string>()))
    .ReturnsAsync(firstResult)
    .ReturnsAsync(secondResult)
    .ThrowsAsync(new Exception("Third call fails"));
```

### Verification

```csharp
mockService.Verify(s => s.SaveAsync(It.IsAny<Data>()), Times.Once);
mockService.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
```

---

## WireMock HTTP Mocking

### Setup

```csharp
public class ApiIntegrationTests : IDisposable
{
    private readonly WireMockServer _mockServer;
    private readonly HttpClient _httpClient;

    public ApiIntegrationTests()
    {
        _mockServer = WireMockServer.Start();
        _httpClient = new HttpClient { BaseAddress = new Uri(_mockServer.Urls[0]) };
    }

    public void Dispose()
    {
        _mockServer?.Stop();
        _mockServer?.Dispose();
    }
}
```

### Request Matching

```csharp
[Fact]
public async Task GetDocument_ReturnsOk()
{
    // Arrange
    _mockServer
        .Given(Request.Create()
            .WithPath("/api/documents/123")
            .UsingGet()
            .WithHeader("Authorization", "Bearer *"))
        .RespondWith(Response.Create()
            .WithStatusCode(200)
            .WithHeader("Content-Type", "application/json")
            .WithBody(@"{ ""id"": ""123"", ""name"": ""Test"" }"));

    // Act
    var response = await _httpClient.GetAsync("/api/documents/123");

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.OK);
}
```

### Stateful Scenarios (Retry Testing)

```csharp
_mockServer
    .Given(Request.Create().WithPath("/api/data").UsingGet())
    .InScenario("retry-scenario")
    .WillSetStateTo("first-call")
    .RespondWith(Response.Create().WithStatusCode(503));

_mockServer
    .Given(Request.Create().WithPath("/api/data").UsingGet())
    .InScenario("retry-scenario")
    .WhenStateIs("first-call")
    .RespondWith(Response.Create().WithStatusCode(200).WithBody("success"));
```

---

## Custom Fakes

### Test Logger

```csharp
public class TestLogger<T> : ILogger<T>
{
    public List<string> LogMessages { get; } = new();

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        LogMessages.Add(formatter(state, exception));
    }

    public bool IsEnabled(LogLevel logLevel) => true;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}
```

---

## Key Points

1. **Moq for interfaces** - Quick mock setup
2. **WireMock for HTTP** - External API simulation
3. **Custom fakes for complex** - When Moq insufficient
4. **TestLogger for logging** - Capture log output
5. **Strict mode sparingly** - Only when needed

---

**Lines**: ~110
