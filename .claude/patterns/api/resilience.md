# Resilience Pattern

> **Domain**: BFF API / Fault Tolerance
> **Last Validated**: 2025-12-25
> **Source ADRs**: ADR-017

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Infrastructure/Http/ResilientHttpHandler.cs` | Polly policy handler |
| `src/server/api/Sprk.Bff.Api/Infrastructure/Extensions/HttpClientExtensions.cs` | HttpClient setup |
| `src/server/api/Sprk.Bff.Api/Program.cs` | Policy registration |

---

## Resilience Strategy

```
Request → Retry (3x) → Circuit Breaker → Timeout → Fallback
```

1. **Retry**: Transient failures (5xx, network errors)
2. **Circuit Breaker**: Prevent cascade failures
3. **Timeout**: Prevent hanging requests
4. **Fallback**: Graceful degradation

---

## Implementation Pattern

### 1. Polly Policy Configuration

```csharp
// HttpClientExtensions.cs
public static IHttpClientBuilder AddResilientHttpClient(
    this IServiceCollection services,
    string name,
    Action<HttpClient> configureClient)
{
    return services.AddHttpClient(name, configureClient)
        .AddPolicyHandler(GetRetryPolicy())
        .AddPolicyHandler(GetCircuitBreakerPolicy())
        .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(30));
}

private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Exponential backoff
            onRetry: (outcome, timespan, retryAttempt, context) =>
            {
                Log.Warning("Retry {Attempt} after {Delay}s due to {StatusCode}",
                    retryAttempt, timespan.TotalSeconds, outcome.Result?.StatusCode);
            });
}

private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 5,
            durationOfBreak: TimeSpan.FromSeconds(30),
            onBreak: (outcome, breakDelay) =>
                Log.Warning("Circuit breaker opened for {BreakDelay}s", breakDelay.TotalSeconds),
            onReset: () => Log.Information("Circuit breaker reset"));
}
```

### 2. Service Registration

```csharp
// Program.cs
services.AddResilientHttpClient("GraphApi", client =>
{
    client.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
    client.Timeout = TimeSpan.FromSeconds(60);
});

services.AddResilientHttpClient("AzureOpenAI", client =>
{
    client.BaseAddress = new Uri(config["AzureOpenAI:Endpoint"]);
    client.DefaultRequestHeaders.Add("api-key", config["AzureOpenAI:ApiKey"]);
});
```

### 3. Usage in Services

```csharp
public class SpeFileStore
{
    private readonly HttpClient _httpClient;

    public SpeFileStore(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("GraphApi");
    }

    public async Task<Container> GetContainerAsync(string id, CancellationToken ct)
    {
        // Polly policies are applied automatically via the handler
        var response = await _httpClient.GetAsync($"storage/fileStorage/containers/{id}", ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Container>(ct);
    }
}
```

---

## Retry Configuration by Service

| Service | Retries | Base Delay | Max Timeout |
|---------|---------|------------|-------------|
| Graph API | 3 | 2s exponential | 60s |
| Azure OpenAI | 3 | 2s exponential | 120s |
| Dataverse | 2 | 1s exponential | 30s |
| Redis | 2 | 500ms | 5s |

---

## Circuit Breaker Settings

```csharp
// Default settings
handledEventsAllowedBeforeBreaking: 5,  // Open after 5 failures
durationOfBreak: TimeSpan.FromSeconds(30),  // Stay open for 30s
samplingDuration: TimeSpan.FromMinutes(1)  // Rolling window
```

---

## Rate Limit Handling

```csharp
// Handle 429 with Retry-After header
.OrResult(response => response.StatusCode == HttpStatusCode.TooManyRequests)
.WaitAndRetryAsync(
    retryCount: 3,
    sleepDurationProvider: (retryAttempt, response, context) =>
    {
        var retryAfter = response.Result?.Headers.RetryAfter;
        if (retryAfter?.Delta.HasValue == true)
            return retryAfter.Delta.Value;
        return TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
    });
```

---

## Fallback Pattern

```csharp
// Graceful degradation for non-critical features
var fallbackPolicy = Policy<AnalysisResult>
    .Handle<Exception>()
    .FallbackAsync(
        fallbackValue: AnalysisResult.Unavailable("Service temporarily unavailable"),
        onFallbackAsync: (result, context) =>
        {
            Log.Warning("Fallback activated: {Exception}", result.Exception?.Message);
            return Task.CompletedTask;
        });
```

---

## Observability

```csharp
// Log policy events for monitoring
.WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
    onRetry: (outcome, timespan, retryAttempt, context) =>
    {
        _logger.LogWarning(
            "Retry {RetryAttempt}/3 for {PolicyKey} after {Delay}ms. Reason: {Reason}",
            retryAttempt,
            context.PolicyKey,
            timespan.TotalMilliseconds,
            outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());

        // Emit metric
        _metrics.IncrementCounter("http_retry_total", new[] { context.PolicyKey });
    });
```

---

## Anti-Patterns

```csharp
// ❌ Bad: No resilience
var response = await _httpClient.GetAsync(url);

// ❌ Bad: Retry on all errors (including 4xx)
.HandleResult(r => !r.IsSuccessStatusCode)

// ❌ Bad: Fixed delay (causes thundering herd)
.WaitAndRetryAsync(3, _ => TimeSpan.FromSeconds(1))

// ✅ Good: Exponential backoff with jitter
.WaitAndRetryAsync(3, retryAttempt =>
    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) +
    TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000)))
```

---

## Related Patterns

- [Error Handling](error-handling.md) - Error response format
- [Service Registration](service-registration.md) - HttpClient DI setup

---

**Lines**: ~175
