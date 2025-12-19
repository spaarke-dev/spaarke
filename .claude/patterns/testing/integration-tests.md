# Integration Tests Pattern

> **Domain**: Testing / End-to-End
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-022

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs` | Test factory |
| `tests/integration/Spe.Integration.Tests/IntegrationTestFixture.cs` | Integration fixture |
| `tests/integration/Spe.Integration.Tests/SystemIntegrationTests.cs` | E2E tests |

---

## WebApplicationFactory Setup

```csharp
public class CustomWebAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Override configuration for tests
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:Enabled"] = "false",
                ["DocumentIntelligence:Enabled"] = "false"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace services with test doubles
            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            // Use in-memory cache
            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();
        });
    }
}
```

---

## Integration Test Class

```csharp
public class SystemIntegrationTests : IClassFixture<IntegrationTestFixture>
{
    private readonly HttpClient _client;

    public SystemIntegrationTests(IntegrationTestFixture fixture)
    {
        _client = fixture.Client;
    }

    [Fact]
    public async Task HealthCheck_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/healthz");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Healthy");
    }

    [Fact]
    public async Task ErrorResponse_ReturnsProblemDetails()
    {
        var response = await _client.GetAsync("/api/documents/invalid-guid");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }
}
```

---

## Architecture Tests (NetArchTest)

```csharp
[Fact(DisplayName = "ADR-001: No Azure Functions packages")]
public void NoAzureFunctionsPackages()
{
    var assembly = typeof(Program).Assembly;

    var result = Types.InAssembly(assembly)
        .ShouldNot()
        .HaveDependencyOn("Microsoft.Azure.WebJobs")
        .GetResult();

    result.IsSuccessful.Should().BeTrue(
        "ADR-001 violation: Azure Functions packages found");
}

[Fact(DisplayName = "ADR-007: Graph types don't leak outside Infrastructure")]
public void GraphTypesContained()
{
    var result = Types.InAssembly(typeof(Program).Assembly)
        .That()
        .ResideInNamespace("Sprk.Bff.Api.Api")
        .ShouldNot()
        .HaveDependencyOn("Microsoft.Graph")
        .GetResult();

    result.IsSuccessful.Should().BeTrue(
        "ADR-007 violation: Graph SDK leaked to API layer");
}
```

---

## Key Points

1. **WebApplicationFactory** - Full pipeline testing
2. **Service replacement** - Swap real services for fakes
3. **In-memory config** - Override settings for tests
4. **IClassFixture** - Share factory across tests
5. **Architecture tests** - Enforce ADR compliance

---

## Related Patterns

- [Unit Test Structure](unit-test-structure.md) - AAA pattern
- [Mocking Patterns](mocking-patterns.md) - Test doubles

---

**Lines**: ~90
