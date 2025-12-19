```markdown
# Authentication Performance & Monitoring

> **Source**: AUTHENTICATION-ARCHITECTURE.md
> **Last Updated**: December 4, 2025
> **Applies To**: Performance optimization, alerting, observability

---

## TL;DR

Auth flow adds ~200-400ms latency (Azure AD OBO ~150ms, cold token ~100ms). Cache OBO tokens with 55-min TTL. Monitor via Application Insights. Use Polly resilience for Graph transient failures.

---

## Applies When

- Investigating slow API responses
- Setting up performance baselines
- Configuring alerts for auth failures
- Optimizing token caching
- Debugging intermittent timeouts

---

## Performance Characteristics

### Latency Breakdown

| Operation | Cold (No Cache) | Warm (Cached) |
|-----------|-----------------|---------------|
| PCF → MSAL Token | ~100ms | ~5ms |
| PCF → BFF API Call | ~50ms | ~50ms |
| BFF → Azure AD OBO | ~150ms | ~0ms (cached) |
| BFF → Graph API | ~100-500ms | ~100-500ms |
| **Total Auth Overhead** | ~300-400ms | ~55ms |

### Token Caching Impact

```
Without cache: Every request = +150ms (OBO) + token validation
With cache: First request/hour = +150ms, subsequent = ~0ms

ROI: For 1000 requests/hour, saves ~2.5 minutes of cumulative latency
```

---

## Token Caching Strategy

### OBO Token Cache (Redis)

```csharp
public async Task<string> GetGraphTokenAsync(string userToken)
{
    var cacheKey = $"obo:graph:{ComputeHash(userToken)}";
    
    // Try cache first
    var cached = await _cache.GetStringAsync(cacheKey);
    if (!string.IsNullOrEmpty(cached))
    {
        _logger.LogDebug("OBO cache hit for key {Key}", cacheKey);
        return cached;
    }
    
    // Cache miss - perform OBO
    var result = await _cca.AcquireTokenOnBehalfOf(
        new[] { "https://graph.microsoft.com/.default" },
        new UserAssertion(userToken)
    ).ExecuteAsync();
    
    // Cache with 55-min TTL (5-min buffer before 60-min expiry)
    var ttl = TimeSpan.FromMinutes(55);
    await _cache.SetStringAsync(cacheKey, result.AccessToken,
        new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });
    
    _logger.LogInformation("OBO token cached with TTL {TTL}", ttl);
    return result.AccessToken;
}
```

### Cache Key Strategy

```csharp
// Hash the user token (don't cache full tokens as keys)
private static string ComputeHash(string token)
{
    using var sha = SHA256.Create();
    var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(token));
    return Convert.ToBase64String(bytes)[..16];  // First 16 chars
}
```

---

## Resilience Patterns

### Polly for Graph API

```csharp
// Retry transient failures with exponential backoff
services.AddHttpClient("GraphClient")
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(3, retryAttempt => 
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
}

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
}
```

### OBO Token Retry

```csharp
// Don't retry OBO on auth failures - they won't self-heal
try
{
    return await _cca.AcquireTokenOnBehalfOf(...).ExecuteAsync();
}
catch (MsalServiceException ex) when (ex.ErrorCode == "invalid_grant")
{
    // User must re-auth - don't retry
    throw new UnauthorizedAccessException("Session expired", ex);
}
catch (MsalServiceException ex) when (IsTransient(ex))
{
    // Azure AD hiccup - safe to retry
    await Task.Delay(1000);
    return await _cca.AcquireTokenOnBehalfOf(...).ExecuteAsync();
}
```

---

## Application Insights Integration

### Custom Metrics

```csharp
public class AuthTelemetry
{
    private readonly TelemetryClient _telemetry;
    
    public void TrackOboLatency(TimeSpan duration, bool cacheHit)
    {
        _telemetry.TrackMetric("OBO.Latency.Ms", duration.TotalMilliseconds);
        _telemetry.TrackMetric("OBO.CacheHit", cacheHit ? 1 : 0);
    }
    
    public void TrackAuthFailure(string errorCode, string boundary)
    {
        _telemetry.TrackEvent("Auth.Failure", new Dictionary<string, string>
        {
            ["ErrorCode"] = errorCode,
            ["Boundary"] = boundary  // e.g., "PCF-BFF", "BFF-Graph"
        });
    }
}
```

### KQL Queries for Monitoring

**Auth Failure Rate**:
```kusto
requests
| where timestamp > ago(1h)
| where resultCode == 401 or resultCode == 403
| summarize failures=count(), total=count() by bin(timestamp, 5m)
| extend failureRate = failures * 100.0 / total
| render timechart
```

**OBO Latency Percentiles**:
```kusto
customMetrics
| where name == "OBO.Latency.Ms"
| summarize 
    p50=percentile(value, 50),
    p95=percentile(value, 95),
    p99=percentile(value, 99)
    by bin(timestamp, 5m)
| render timechart
```

**Token Cache Hit Rate**:
```kusto
customMetrics
| where name == "OBO.CacheHit"
| summarize hitRate=avg(value) * 100 by bin(timestamp, 1h)
| render timechart
```

**Error Code Distribution**:
```kusto
customEvents
| where name == "Auth.Failure"
| extend errorCode = tostring(customDimensions.ErrorCode)
| summarize count() by errorCode
| render piechart
```

---

## Alerting Rules

### Recommended Alerts

| Alert | Condition | Severity |
|-------|-----------|----------|
| High Auth Failure Rate | 401/403 > 5% of requests in 5min | Warning |
| OBO Latency Spike | p95 > 500ms for 5min | Warning |
| Token Cache Miss Surge | Cache hit < 80% for 15min | Info |
| Circuit Breaker Open | Graph circuit breaker tripped | Critical |

### Alert Configuration (ARM/Bicep)

```bicep
resource authFailureAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'auth-failure-rate-high'
  properties: {
    severity: 2
    evaluationFrequency: 'PT5M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'AuthFailures'
          metricName: 'Http4xx'
          operator: 'GreaterThan'
          threshold: 50
          timeAggregation: 'Count'
        }
      ]
    }
  }
}
```

---

## Performance Optimization Checklist

- [ ] OBO tokens cached with 55-min TTL?
- [ ] Redis cache for distributed token storage?
- [ ] Polly retry for transient Graph failures?
- [ ] Circuit breaker for sustained Graph outages?
- [ ] Application Insights tracking auth latency?
- [ ] Alerts configured for auth failure spikes?
- [ ] Token validation short-circuited for cached decisions?

---

## Troubleshooting Performance

| Symptom | Likely Cause | Fix |
|---------|--------------|-----|
| First request slow, subsequent fast | Cold cache | Expected behavior |
| All requests slow | Cache not working | Check Redis connection |
| Intermittent timeouts | Graph throttling | Check 429 responses, add backoff |
| Gradual latency increase | Memory pressure on cache | Check Redis memory usage |
| Sudden latency spike | Azure AD service degradation | Check Azure status page |

---

## Related Articles

- [sdap-auth-patterns.md](sdap-auth-patterns.md) - Auth flow implementation
- [oauth-obo-implementation.md](oauth-obo-implementation.md) - OBO caching patterns
- [auth-azure-resources.md](auth-azure-resources.md) - Infrastructure details

---

*Extracted from AUTHENTICATION-ARCHITECTURE.md performance and monitoring sections*
```
