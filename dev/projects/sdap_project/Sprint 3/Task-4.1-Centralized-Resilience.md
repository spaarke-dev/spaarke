# Task 4.1: Centralized Resilience - DelegatingHandler for Graph API

**Priority:** MEDIUM (Sprint 3, Phase 4)
**Estimated Effort:** 2-3 days
**Status:** IMPROVES RELIABILITY
**Dependencies:** Task 2.1 (OboSpeService - real Graph calls)

---

## Context & Problem Statement

Currently, **retry and resilience logic is scattered** across the codebase, with each caller manually creating Polly policies:

**Problem Patterns**:
1. **Manual Polly Policy Creation**: Each service creates its own retry policies
2. **Inconsistent Retry Logic**: Different timeout values, retry counts across services
3. **No Centralized Monitoring**: Can't track retry attempts or circuit breaker state
4. **Code Duplication**: Same retry patterns repeated in multiple places
5. **Hard to Test**: Retry logic coupled with business logic

**Example** (common pattern scattered across code):
```csharp
var retryPolicy = Policy
    .Handle<ServiceException>(ex => ex.ResponseStatusCode == 429)
    .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

await retryPolicy.ExecuteAsync(async () =>
{
    return await graph.Drives[driveId].Items[itemId].GetAsync();
});
```

This pattern exists in multiple services, creating maintenance burden and inconsistency.

---

## Goals & Outcomes

### Primary Goals
1. Create `GraphHttpMessageHandler` as a DelegatingHandler with centralized resilience policies
2. Integrate Polly for retry, circuit breaker, and timeout patterns
3. Register via IHttpClientFactory for automatic injection into GraphServiceClient
4. Remove manual retry logic from services (Graph SDK will handle it)
5. Add telemetry for retry attempts, circuit breaker events, timeouts

### Success Criteria
- [ ] GraphHttpMessageHandler created with Polly policies
- [ ] Registered with IHttpClientFactory for Graph clients
- [ ] Services no longer need manual retry logic
- [ ] Consistent retry behavior across all Graph API calls
- [ ] Telemetry tracks retry attempts, circuit breaker state
- [ ] Integration tests validate retry and circuit breaker behavior
- [ ] Configuration allows tuning retry policies per environment

### Non-Goals
- Custom retry logic per operation (use centralized policies)
- Distributed circuit breaker (single instance for now)
- Advanced Polly features (hedging, bulkhead isolation) - Sprint 4+

---

## Architecture & Design

### Current State (Sprint 2) - Manual Retries
```
┌──────────────────────┐
│  Service Code        │
│  (OboSpeService,     │
│   SpeFileStore, etc) │
└──────┬───────────────┘
       │
       v
┌──────────────────────┐
│ Manual Polly Policy  │ ← Created per call
│ WaitAndRetryAsync    │
└──────┬───────────────┘
       │
       v
┌──────────────────────┐
│ GraphServiceClient   │
└──────┬───────────────┘
       │
       v
┌──────────────────────┐
│ Microsoft Graph API  │
└──────────────────────┘
```

### Target State (Sprint 3) - Centralized Handler
```
┌──────────────────────┐
│  Service Code        │
│  (No retry logic)    │ ← Clean, focused on business logic
└──────┬───────────────┘
       │
       v
┌──────────────────────┐
│ IGraphClientFactory  │
└──────┬───────────────┘
       │
       v
┌──────────────────────────────┐
│ IHttpClientFactory           │
│ + GraphHttpMessageHandler    │ ← Centralized resilience
│   - Retry (429, 503, 504)    │
│   - Circuit Breaker          │
│   - Timeout (30s)            │
│   - Telemetry                │
└──────┬───────────────────────┘
       │
       v
┌──────────────────────┐
│ GraphServiceClient   │
└──────┬───────────────┘
       │
       v
┌──────────────────────┐
│ Microsoft Graph API  │
└──────────────────────┘
```

---

## Relevant ADRs

### ADR-010: DI Minimalism
- **IHttpClientFactory**: Proper HTTP client lifecycle management
- **DelegatingHandler**: Middleware pattern for cross-cutting concerns

### ADR-002: No Heavy Plugins
- **Polly Over Custom**: Use Polly for resilience, not custom retry loops
- **Standard Patterns**: Circuit breaker, retry, timeout are standard

---

## Implementation Steps

### Step 1: Install Polly NuGet Packages

**File**: `src/api/Spe.Bff.Api/Spe.Bff.Api.csproj`

```xml
<ItemGroup>
  <PackageReference Include="Polly" Version="8.2.0" />
  <PackageReference Include="Polly.Extensions.Http" Version="3.0.0" />
  <PackageReference Include="Microsoft.Extensions.Http.Polly" Version="8.0.0" />
</ItemGroup>
```

---

### Step 2: Create GraphHttpMessageHandler

**New File**: `C:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Http\GraphHttpMessageHandler.cs`

```csharp
using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;
using Polly.Timeout;
using System.Net;

namespace Spe.Bff.Api.Infrastructure.Http;

/// <summary>
/// DelegatingHandler that provides centralized resilience patterns for Microsoft Graph API calls.
/// Implements retry, circuit breaker, and timeout policies using Polly.
/// </summary>
public class GraphHttpMessageHandler : DelegatingHandler
{
    private readonly ILogger<GraphHttpMessageHandler> _logger;
    private readonly IAsyncPolicy<HttpResponseMessage> _resiliencePolicy;

    public GraphHttpMessageHandler(ILogger<GraphHttpMessageHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePolicy = BuildResiliencePolicy();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return await _resiliencePolicy.ExecuteAsync(async ct =>
        {
            return await base.SendAsync(request, ct);
        }, cancellationToken);
    }

    private IAsyncPolicy<HttpResponseMessage> BuildResiliencePolicy()
    {
        // 1. Retry Policy: Handle transient errors (429, 503, 504)
        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError() // 5xx and 408
            .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests) // 429
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: (retryAttempt, response, context) =>
                {
                    // Honor Retry-After header if present (Graph API standard)
                    if (response.Result?.Headers.RetryAfter?.Delta.HasValue == true)
                    {
                        var retryAfter = response.Result.Headers.RetryAfter.Delta.Value;
                        _logger.LogWarning("Graph API throttling, honoring Retry-After: {RetryAfter}s", retryAfter.TotalSeconds);
                        return retryAfter;
                    }

                    // Exponential backoff: 2^retryAttempt seconds (2s, 4s, 8s)
                    var backoff = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                    return backoff;
                },
                onRetryAsync: async (outcome, timespan, retryAttempt, context) =>
                {
                    var statusCode = outcome.Result?.StatusCode ?? HttpStatusCode.InternalServerError;
                    _logger.LogWarning("Graph API request failed with {StatusCode}, retrying in {Delay}s (attempt {Attempt}/3)",
                        statusCode, timespan.TotalSeconds, retryAttempt);

                    // Telemetry: Track retry attempts
                    RecordRetryAttempt(statusCode, retryAttempt);

                    await Task.CompletedTask;
                });

        // 2. Circuit Breaker Policy: Open circuit after consecutive failures
        var circuitBreakerPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (outcome, duration) =>
                {
                    _logger.LogError("Circuit breaker OPENED due to {ConsecutiveFailures} consecutive failures. Breaking for {Duration}s",
                        5, duration.TotalSeconds);
                    RecordCircuitBreakerState("open");
                },
                onReset: () =>
                {
                    _logger.LogInformation("Circuit breaker RESET to closed state");
                    RecordCircuitBreakerState("closed");
                },
                onHalfOpen: () =>
                {
                    _logger.LogInformation("Circuit breaker in HALF-OPEN state, testing connection");
                    RecordCircuitBreakerState("half-open");
                });

        // 3. Timeout Policy: 30 seconds per request
        var timeoutPolicy = Policy
            .TimeoutAsync<HttpResponseMessage>(
                timeout: TimeSpan.FromSeconds(30),
                onTimeoutAsync: async (context, timespan, task) =>
                {
                    _logger.LogWarning("Graph API request timed out after {Timeout}s", timespan.TotalSeconds);
                    RecordTimeout();
                    await Task.CompletedTask;
                });

        // Combine policies: Timeout -> Retry -> Circuit Breaker (inner to outer)
        return Policy.WrapAsync(timeoutPolicy, retryPolicy, circuitBreakerPolicy);
    }

    private void RecordRetryAttempt(HttpStatusCode statusCode, int attempt)
    {
        // TODO: Emit telemetry (Application Insights, Prometheus, etc.)
        // Example: _telemetry.TrackMetric("GraphApi.Retry", attempt, new { StatusCode = (int)statusCode });
    }

    private void RecordCircuitBreakerState(string state)
    {
        // TODO: Emit circuit breaker state change
        // Example: _telemetry.TrackEvent("GraphApi.CircuitBreaker", new { State = state });
    }

    private void RecordTimeout()
    {
        // TODO: Emit timeout event
        // Example: _telemetry.TrackMetric("GraphApi.Timeout", 1);
    }
}
```

---

### Step 3: Register Handler with IHttpClientFactory

**File**: `C:\code_files\spaarke\src\api\Spe.Bff.Api\Program.cs`

```csharp
// Register GraphHttpMessageHandler
builder.Services.AddTransient<GraphHttpMessageHandler>();

// Configure HttpClient for Graph with resilience handler
builder.Services.AddHttpClient("GraphApiClient")
    .AddHttpMessageHandler<GraphHttpMessageHandler>()
    .ConfigureHttpClient(client =>
    {
        client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
        client.DefaultRequestHeaders.Add("Accept", "application/json");
    });

// Note: IGraphClientFactory needs to be updated to use named HttpClient
```

---

### Step 4: Update IGraphClientFactory to Use Named HttpClient

**File**: `C:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Graph\GraphClientFactory.cs`

**Current** (likely creates HttpClient directly):
```csharp
public GraphServiceClient CreateAppOnlyClient()
{
    var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
    return new GraphServiceClient(credential);
}
```

**Updated** (use IHttpClientFactory):
```csharp
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;

namespace Spe.Bff.Api.Infrastructure.Graph;

public class GraphClientFactory : IGraphClientFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GraphClientFactory> _logger;

    public GraphClientFactory(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GraphClientFactory> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public GraphServiceClient CreateAppOnlyClient()
    {
        var tenantId = _configuration["Graph:TenantId"];
        var clientId = _configuration["Graph:ClientId"];
        var clientSecret = _configuration["Graph:ClientSecret"];

        var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        var authProvider = new AzureIdentityAuthenticationProvider(credential);

        // Get HttpClient with resilience handler
        var httpClient = _httpClientFactory.CreateClient("GraphApiClient");

        return new GraphServiceClient(httpClient, authProvider);
    }

    public async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken)
    {
        var authProvider = new DelegateAuthenticationProvider(async (request) =>
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userAccessToken);
            await Task.CompletedTask;
        });

        // Get HttpClient with resilience handler
        var httpClient = _httpClientFactory.CreateClient("GraphApiClient");

        return new GraphServiceClient(httpClient, authProvider);
    }
}
```

---

### Step 5: Remove Manual Retry Logic from Services

**Example**: Update `OboSpeService` to remove manual Polly policies.

**Before** (hypothetical - if manual retries existed):
```csharp
public async Task<DriveItem> GetItemAsync(string driveId, string itemId)
{
    var retryPolicy = Policy
        .Handle<ServiceException>(ex => ex.ResponseStatusCode == 429)
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

    return await retryPolicy.ExecuteAsync(async () =>
    {
        var graph = await _factory.CreateOnBehalfOfClientAsync(userBearer);
        return await graph.Drives[driveId].Items[itemId].GetAsync();
    });
}
```

**After** (clean - handler provides resilience):
```csharp
public async Task<DriveItem> GetItemAsync(string driveId, string itemId)
{
    var graph = await _factory.CreateOnBehalfOfClientAsync(userBearer);
    return await graph.Drives[driveId].Items[itemId].GetAsync();
    // Retry, circuit breaker, timeout handled by GraphHttpMessageHandler
}
```

---

### Step 6: Add Configuration for Policy Tuning

**File**: `appsettings.json`

```json
{
  "GraphResilience": {
    "RetryCount": 3,
    "RetryBackoffSeconds": 2,
    "CircuitBreakerFailureThreshold": 5,
    "CircuitBreakerBreakDuration": 30,
    "TimeoutSeconds": 30
  }
}
```

**Update GraphHttpMessageHandler** to read from configuration:
```csharp
private readonly GraphResilienceOptions _options;

public GraphHttpMessageHandler(
    ILogger<GraphHttpMessageHandler> logger,
    IOptions<GraphResilienceOptions> options)
{
    _logger = logger;
    _options = options.Value;
    _resiliencePolicy = BuildResiliencePolicy();
}

private IAsyncPolicy<HttpResponseMessage> BuildResiliencePolicy()
{
    var retryPolicy = HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(
            retryCount: _options.RetryCount,
            sleepDurationProvider: (retryAttempt, response, context) =>
            {
                // ... use _options.RetryBackoffSeconds
            });

    // ... use other config values
}
```

---

## AI Coding Prompts

### Prompt 1: Create GraphHttpMessageHandler with Polly
```
Create centralized resilience handler for Microsoft Graph API:

Context:
- Need DelegatingHandler with Polly policies
- Handle retry (429, 503, 504), circuit breaker, timeout
- Integrate with IHttpClientFactory

Requirements:
1. Create GraphHttpMessageHandler : DelegatingHandler
2. Implement retry policy with exponential backoff (2, 4, 8 seconds)
3. Honor Retry-After header for 429 responses
4. Implement circuit breaker (5 consecutive failures, 30s break)
5. Implement timeout policy (30 seconds per request)
6. Wrap policies: Timeout -> Retry -> Circuit Breaker
7. Log retry attempts, circuit breaker state changes, timeouts
8. Add telemetry placeholders for metrics

Code Quality:
- Senior C# developer standards
- Comprehensive logging with structured properties
- Follow Polly best practices
- Async all the way

Files to Create:
- C:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Http\GraphHttpMessageHandler.cs

NuGet Packages:
- Polly 8.2.0
- Polly.Extensions.Http 3.0.0
- Microsoft.Extensions.Http.Polly 8.0.0
```

### Prompt 2: Update GraphClientFactory to Use IHttpClientFactory
```
Update GraphClientFactory to use named HttpClient with resilience handler:

Context:
- Current implementation creates HttpClient directly
- Need to use IHttpClientFactory.CreateClient("GraphApiClient")
- GraphApiClient configured with GraphHttpMessageHandler

Requirements:
1. Inject IHttpClientFactory into GraphClientFactory
2. Update CreateAppOnlyClient to use named client "GraphApiClient"
3. Update CreateOnBehalfOfClientAsync to use named client
4. Pass HttpClient to GraphServiceClient constructor
5. Maintain authentication logic (ClientSecretCredential, OBO token)

Code Quality:
- Proper disposal not needed (IHttpClientFactory manages lifecycle)
- ArgumentNullException.ThrowIfNull for dependencies
- Log client creation

Files to Modify:
- C:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Graph\GraphClientFactory.cs

Files to Modify (DI):
- C:\code_files\spaarke\src\api\Spe.Bff.Api\Program.cs
```

### Prompt 3: Remove Manual Retry Logic from Services
```
Remove manual Polly retry code from services (now handled by DelegatingHandler):

Context:
- Services may have manual retry policies
- GraphHttpMessageHandler now provides centralized resilience
- Need to clean up redundant retry code

Requirements:
1. Search for manual Polly policy creation in services
2. Remove WaitAndRetryAsync, CircuitBreakerAsync calls
3. Simplify service methods to direct Graph SDK calls
4. Verify services still handle ServiceException for domain logic (not retries)
5. Update tests to not mock retry policies

Search Patterns:
- rg "WaitAndRetryAsync|CircuitBreakerAsync" --type cs
- rg "Policy\.Handle" --type cs

Files to Check:
- OboSpeService.cs
- SpeFileStore.cs (or refactored operation classes)
- Any other services calling Graph API
```

---

## Testing Strategy

### Unit Tests
1. **GraphHttpMessageHandler**:
   - Mock HttpResponseMessage with 429, 503, 504 status codes
   - Verify retry count and backoff timing
   - Test circuit breaker opens after threshold
   - Test timeout policy triggers at 30s

2. **GraphClientFactory**:
   - Verify uses IHttpClientFactory.CreateClient("GraphApiClient")
   - Test authentication providers configured correctly

### Integration Tests
1. **Retry Behavior**:
   - Use WireMock to simulate 429 responses with Retry-After header
   - Verify client retries with correct delays
   - Verify eventual success after transient errors

2. **Circuit Breaker**:
   - Simulate 5 consecutive failures
   - Verify circuit opens
   - Verify requests fail fast during break
   - Verify circuit resets after duration

3. **Timeout**:
   - Simulate slow Graph API (delay response > 30s)
   - Verify request times out
   - Verify TimeoutException thrown

---

## Validation Checklist

Before marking this task complete, verify:

- [ ] GraphHttpMessageHandler created with Polly policies
- [ ] Registered with IHttpClientFactory
- [ ] GraphClientFactory updated to use named HttpClient
- [ ] Manual retry logic removed from services
- [ ] Configuration supports tuning retry policies
- [ ] Telemetry placeholders added
- [ ] Unit tests validate retry, circuit breaker, timeout
- [ ] Integration tests with WireMock validate behavior
- [ ] Code review completed

---

## Completion Criteria

Task is complete when:
1. Centralized resilience handler in place
2. All Graph API calls use handler
3. Manual retry code removed
4. Tests validate behavior
5. Configuration allows policy tuning
6. Code review approved

**Estimated Completion: 2-3 days**

---

## Benefits

1. **Consistency**: All Graph API calls have same resilience behavior
2. **Maintainability**: Change policies in one place
3. **Testability**: Services focused on business logic, not retries
4. **Observability**: Centralized telemetry for retries, circuit breaker
5. **Configuration**: Tune policies per environment without code changes
