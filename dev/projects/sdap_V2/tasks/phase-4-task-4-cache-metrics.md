# Phase 4 - Task 4: Add Cache Metrics (Optional)

**Phase**: 4 (Token Caching)
**Duration**: 30-45 minutes
**Risk**: Low
**Patterns**: System.Diagnostics.Metrics (OpenTelemetry-compatible)
**Anti-Patterns**: N/A
**Status**: ⚠️ OPTIONAL - Recommended for production monitoring

---

## 🤖 AI PROMPT

```
CONTEXT: You are working on Phase 4 of the SDAP BFF API refactoring, specifically adding observability for cache performance.

TASK: Create CacheMetrics.cs to track cache hit/miss rates and latency using System.Diagnostics.Metrics (OpenTelemetry-compatible).

CONSTRAINTS:
- Must use System.Diagnostics.Metrics (modern .NET metrics)
- Must NOT use deprecated APIs (EventCounters, etc.)
- Must be OpenTelemetry-compatible
- Must track: cache hits, misses, hit rate, latency

VERIFICATION BEFORE STARTING:
1. Verify Phase 4 Tasks 1-3 complete (cache fully operational)
2. Verify .NET 7+ (System.Diagnostics.Metrics available)
3. Verify cache working (logs show hits/misses)
4. If cache not working, STOP and fix Tasks 1-3 first

FOCUS: This task is OPTIONAL. Only implement if production monitoring is needed. Can be deferred to later sprint.
```

---

## Goal

Create **CacheMetrics** service to track cache performance metrics using **System.Diagnostics.Metrics** (OpenTelemetry-compatible).

**Metrics to Track**:
1. **Cache hits** (counter)
2. **Cache misses** (counter)
3. **Hit rate** (computed from hits/total)
4. **Cache latency** (histogram)

**Why**: Monitor cache effectiveness, detect issues, optimize performance

**Note**: This task is OPTIONAL. Core caching works without metrics. Add for production observability.

---

## Pre-Flight Verification

### Step 0: Verify Context and Prerequisites

```bash
# 1. Verify Phase 4 Tasks 1-3 complete
- [ ] GraphTokenCache created (Task 4.1) ✅
- [ ] Cache integrated in GraphClientFactory (Task 4.2) ✅
- [ ] Cache registered in DI (Task 4.3) ✅

# 2. Verify cache is working
- [ ] Check logs for cache hits/misses
dotnet run --project src/api/Spe.Bff.Api
# Expected: Should see "Cache HIT/MISS" messages

# 3. Verify .NET version (7+ required for System.Diagnostics.Metrics)
- [ ] grep "<TargetFramework>" src/api/Spe.Bff.Api/Spe.Bff.Api.csproj
# Expected: net7.0, net8.0, or net9.0

# 4. Decide if metrics are needed
- [ ] Is this for production? (Yes → Implement, No → Skip)
- [ ] Is OpenTelemetry configured? (Yes → Implement, No → Defer)
```

**If not needed**: Skip this task, proceed to final validation. Metrics can be added later.

---

## Files to Create

```bash
- [ ] src/api/Spe.Bff.Api/Telemetry/CacheMetrics.cs
- [ ] src/api/Spe.Bff.Api/Telemetry/MetricsExtensions.cs (optional)
```

---

## Implementation

### Step 1: Create CacheMetrics Service

**File**: `src/api/Spe.Bff.Api/Telemetry/CacheMetrics.cs`

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Spe.Bff.Api.Telemetry;

/// <summary>
/// Metrics for Graph token cache performance (OpenTelemetry-compatible).
/// Tracks: cache hits, misses, hit rate, latency.
/// </summary>
public class CacheMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _cacheHits;
    private readonly Counter<long> _cacheMisses;
    private readonly Histogram<double> _cacheLatency;

    // Meter name for OpenTelemetry
    private const string MeterName = "Spe.Bff.Api.Cache";

    public CacheMetrics()
    {
        // Create meter (OpenTelemetry-compatible)
        _meter = new Meter(MeterName, "1.0.0");

        // Counter: Total cache hits
        _cacheHits = _meter.CreateCounter<long>(
            name: "cache.hits",
            unit: "{hit}",
            description: "Total number of cache hits");

        // Counter: Total cache misses
        _cacheMisses = _meter.CreateCounter<long>(
            name: "cache.misses",
            unit: "{miss}",
            description: "Total number of cache misses");

        // Histogram: Cache operation latency
        _cacheLatency = _meter.CreateHistogram<double>(
            name: "cache.latency",
            unit: "ms",
            description: "Cache operation latency in milliseconds");
    }

    /// <summary>
    /// Record a cache hit.
    /// </summary>
    public void RecordHit(double latencyMs, string cacheType = "graph")
    {
        _cacheHits.Add(1, new KeyValuePair<string, object?>("cache.type", cacheType));
        _cacheLatency.Record(latencyMs, new KeyValuePair<string, object?>("cache.result", "hit"));
    }

    /// <summary>
    /// Record a cache miss.
    /// </summary>
    public void RecordMiss(double latencyMs, string cacheType = "graph")
    {
        _cacheMisses.Add(1, new KeyValuePair<string, object?>("cache.type", cacheType));
        _cacheLatency.Record(latencyMs, new KeyValuePair<string, object?>("cache.result", "miss"));
    }

    /// <summary>
    /// Get current cache statistics.
    /// Note: Counters are cumulative, hit rate must be computed externally.
    /// </summary>
    public CacheStats GetStats()
    {
        // Note: System.Diagnostics.Metrics doesn't provide direct counter values
        // Use OpenTelemetry Metrics API or custom tracking for real-time stats
        return new CacheStats
        {
            Message = "Use OpenTelemetry metrics endpoint for real-time stats"
        };
    }

    public void Dispose()
    {
        _meter?.Dispose();
    }
}

/// <summary>
/// Cache statistics (for compatibility).
/// </summary>
public record CacheStats
{
    public string? Message { get; init; }
}
```

### Step 2: Integrate Metrics into GraphTokenCache

**File**: `src/api/Spe.Bff.Api/Services/GraphTokenCache.cs`

**Changes**:
```csharp
using Spe.Bff.Api.Telemetry;  // ✨ NEW
using System.Diagnostics;  // ✨ NEW

namespace Spe.Bff.Api.Services;

public class GraphTokenCache
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<GraphTokenCache> _logger;
    private readonly CacheMetrics _metrics;  // ✨ NEW

    public GraphTokenCache(
        IDistributedCache cache,
        ILogger<GraphTokenCache> logger,
        CacheMetrics metrics)  // ✨ NEW: Inject metrics (optional - can be null)
    {
        _cache = cache;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<string?> GetTokenAsync(string tokenHash)
    {
        var sw = Stopwatch.StartNew();  // ✨ NEW: Measure latency

        try
        {
            var cachedToken = await _cache.GetStringAsync($"sdap:graph:token:{tokenHash}");

            sw.Stop();  // ✨ NEW

            if (cachedToken != null)
            {
                // Cache HIT
                _logger.LogDebug("Cache HIT for token hash {Hash}...", tokenHash[..8]);
                _metrics?.RecordHit(sw.Elapsed.TotalMilliseconds);  // ✨ NEW
            }
            else
            {
                // Cache MISS
                _logger.LogDebug("Cache MISS for token hash {Hash}...", tokenHash[..8]);
                _metrics?.RecordMiss(sw.Elapsed.TotalMilliseconds);  // ✨ NEW
            }

            return cachedToken;
        }
        catch (Exception ex)
        {
            sw.Stop();  // ✨ NEW
            _logger.LogError(ex, "Error retrieving token from cache");
            _metrics?.RecordMiss(sw.Elapsed.TotalMilliseconds);  // ✨ NEW: Treat errors as misses
            return null;
        }
    }

    // SetTokenAsync and other methods remain unchanged
}
```

### Step 3: Register Metrics in DI

**File**: `src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs`

```csharp
using Spe.Bff.Api.Telemetry;  // ✨ NEW

public static class DocumentsModuleExtensions
{
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        // ... existing registrations

        // ✨ NEW: Cache metrics (Singleton - tracks metrics across requests)
        services.AddSingleton<CacheMetrics>();

        // Graph token cache (Singleton - injects CacheMetrics)
        services.AddSingleton<GraphTokenCache>();

        // ... rest of registrations

        return services;
    }
}
```

### Step 4: Configure OpenTelemetry (Optional)

**File**: `src/api/Spe.Bff.Api/Program.cs`

```csharp
using OpenTelemetry.Metrics;

var builder = WebApplication.CreateBuilder(args);

// ... existing configuration

// ✨ NEW: Add OpenTelemetry metrics (optional)
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter("Spe.Bff.Api.Cache")  // CacheMetrics meter
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddPrometheusExporter();  // OR .AddOtlpExporter() for Azure Monitor
    });

var app = builder.Build();

// ✨ NEW: Expose Prometheus metrics endpoint
app.MapPrometheusScrapingEndpoint();  // /metrics

app.Run();
```

**Package Requirements**:
```bash
# Install OpenTelemetry packages (if using)
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Instrumentation.AspNetCore
dotnet add package OpenTelemetry.Instrumentation.Http
dotnet add package OpenTelemetry.Instrumentation.Runtime
dotnet add package OpenTelemetry.Exporter.Prometheus.AspNetCore
# OR for Azure Monitor:
# dotnet add package Azure.Monitor.OpenTelemetry.AspNetCore
```

---

## Validation

### Build Check
```bash
dotnet build
# Expected: Success, 0 warnings
```

### Metrics Endpoint Check (if OpenTelemetry configured)
```bash
# Start application
dotnet run --project src/api/Spe.Bff.Api

# Access Prometheus metrics endpoint
curl http://localhost:5000/metrics

# Expected output (sample):
# cache_hits_total{cache_type="graph"} 42
# cache_misses_total{cache_type="graph"} 8
# cache_latency_ms_bucket{cache_result="hit",le="5"} 38
# cache_latency_ms_bucket{cache_result="hit",le="10"} 42
# cache_latency_ms_bucket{cache_result="miss",le="200"} 8
```

### Runtime Metrics Check
```bash
# Make requests to generate metrics
for i in {1..10}; do
  curl -X POST "https://localhost:5001/api/obo/upload?containerId=xxx&fileName=test$i.txt" \
    -H "Authorization: Bearer $TOKEN" \
    -d "test"
done

# Check metrics
curl http://localhost:5000/metrics | grep cache

# Expected:
# cache_hits_total 9
# cache_misses_total 1
# cache_latency_ms_sum{cache_result="hit"} 45  (9 hits × 5ms avg)
# cache_latency_ms_sum{cache_result="miss"} 200  (1 miss × 200ms)
```

---

## Checklist

- [ ] **Pre-flight**: Verified Phase 4 Tasks 1-3 complete
- [ ] **Pre-flight**: Verified cache working (logs show hits/misses)
- [ ] **Pre-flight**: Decided metrics are needed (or skip task)
- [ ] Created `CacheMetrics.cs` in Telemetry folder
- [ ] Used System.Diagnostics.Metrics (modern API)
- [ ] Created Counter for cache hits
- [ ] Created Counter for cache misses
- [ ] Created Histogram for cache latency
- [ ] Injected CacheMetrics into GraphTokenCache
- [ ] Added latency measurement (Stopwatch)
- [ ] Recorded hits/misses in GetTokenAsync
- [ ] Registered CacheMetrics as Singleton in DI
- [ ] (Optional) Configured OpenTelemetry exporter
- [ ] (Optional) Added /metrics endpoint
- [ ] Build succeeds: `dotnet build`

---

## Expected Results

**Metrics Available**:
- ✅ `cache.hits` - Total cache hits (counter)
- ✅ `cache.misses` - Total cache misses (counter)
- ✅ `cache.latency` - Cache operation latency (histogram)

**Metrics Endpoint** (if OpenTelemetry configured):
- ✅ `/metrics` - Prometheus format
- ✅ OpenTelemetry OTLP export (Azure Monitor, Datadog, etc.)

**Sample Metrics**:
```
# TYPE cache_hits counter
cache_hits_total{cache_type="graph"} 850

# TYPE cache_misses counter
cache_misses_total{cache_type="graph"} 150

# TYPE cache_latency histogram
cache_latency_ms_bucket{cache_result="hit",le="5"} 800
cache_latency_ms_bucket{cache_result="hit",le="10"} 850
cache_latency_ms_bucket{cache_result="miss",le="200"} 150

# Computed hit rate: 850 / (850 + 150) = 85%
```

---

## Monitoring Queries

### Prometheus/Grafana Queries

**Cache Hit Rate**:
```promql
rate(cache_hits_total[5m]) / (rate(cache_hits_total[5m]) + rate(cache_misses_total[5m]))
```

**Average Cache Hit Latency**:
```promql
histogram_quantile(0.50, rate(cache_latency_ms_bucket{cache_result="hit"}[5m]))
```

**Average Cache Miss Latency**:
```promql
histogram_quantile(0.50, rate(cache_latency_ms_bucket{cache_result="miss"}[5m]))
```

**P95 Cache Latency**:
```promql
histogram_quantile(0.95, rate(cache_latency_ms_bucket[5m]))
```

---

## Troubleshooting

### Issue: Metrics not appearing in /metrics endpoint

**Cause**: OpenTelemetry not configured

**Fix**: Add OpenTelemetry packages and configuration (see Step 4)

### Issue: "CacheMetrics not resolved" error

**Cause**: CacheMetrics not registered in DI

**Fix**: Add to DocumentsModule.Extensions.cs:
```csharp
services.AddSingleton<CacheMetrics>();
```

### Issue: Metrics show 0 values

**Cause**: Metrics not being recorded or no traffic

**Fix**: Verify RecordHit/RecordMiss called:
```bash
# Check logs for cache hits/misses
dotnet run | grep "Cache HIT\|Cache MISS"
```

---

## Context Verification

Before marking complete, verify:
- [ ] ✅ CacheMetrics.cs created (or task skipped)
- [ ] ✅ Metrics integrated into GraphTokenCache (or task skipped)
- [ ] ✅ CacheMetrics registered in DI (or task skipped)
- [ ] ✅ (Optional) OpenTelemetry configured
- [ ] ✅ (Optional) /metrics endpoint accessible
- [ ] ✅ Build succeeds
- [ ] ✅ Metrics appear in endpoint (if configured)

**If task skipped**: Mark as complete, metrics can be added later if needed.

---

## Commit Message

```bash
git add src/api/Spe.Bff.Api/Telemetry/
git add src/api/Spe.Bff.Api/Services/GraphTokenCache.cs
git add src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs
# (Optional) git add src/api/Spe.Bff.Api/Program.cs

git commit -m "feat(telemetry): add cache metrics for observability (optional)

- Create CacheMetrics.cs with System.Diagnostics.Metrics
- Track cache hits, misses, and latency (OpenTelemetry-compatible)
- Integrate metrics into GraphTokenCache
- Register CacheMetrics as Singleton in DI
- (Optional) Configure OpenTelemetry Prometheus exporter
- (Optional) Expose /metrics endpoint

Metrics:
- cache.hits (counter): Total cache hits
- cache.misses (counter): Total cache misses
- cache.latency (histogram): Cache operation latency

Benefits:
- Monitor cache effectiveness (hit rate)
- Detect performance issues (latency spikes)
- Optimize cache strategy based on data
- OpenTelemetry-compatible (Prometheus, Azure Monitor, Datadog)

Task: Phase 4, Task 4 (Optional)"
```

---

## Phase 4 Complete!

🎉 **Congratulations!** Phase 4 (Token Caching) is complete.

**Achievements**:
- ✅ Created GraphTokenCache (Task 4.1)
- ✅ Integrated cache into GraphClientFactory (Task 4.2)
- ✅ Registered cache in DI (Task 4.3)
- ✅ Added cache metrics for observability (Task 4.4 - optional)

**Performance Metrics**:
| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| Cache hit latency | N/A | ~5ms | New capability |
| Cache miss latency | 200ms | 200ms | Same |
| Overall avg latency | 200ms | ~25ms* | 87% reduction |
| Azure AD load | 100% | ~10%* | 90% reduction |
| Hit rate | N/A | >90%* | After warmup |

*Assuming 90% cache hit rate after warmup

---

## All Phases Complete! 🎉

🚀 **SDAP BFF API Refactoring Complete!**

**Summary**:
- ✅ Phase 1: Configuration & Critical Fixes (3 tasks)
- ✅ Phase 2: Simplify Service Layer (6 tasks)
- ✅ Phase 3: Feature Module Pattern (2 tasks)
- ✅ Phase 4: Token Caching (4 tasks)

**Total**: 15 tasks (14 required, 1 optional)

**Metrics**:
| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| Interface count | 10 | 3 | 70% reduction |
| Call chain depth | 6 layers | 3 layers | 50% reduction |
| Program.cs DI lines | 80+ | ~13 | 84% reduction |
| File upload latency | 700ms | 150ms | 78% faster |
| Dataverse query | 650ms | 50ms | 92% faster |
| Cache hit rate | N/A | >90% | New capability |

---

## Next Steps

1. **Validation**: Run full integration tests
2. **Documentation**: Update architecture diagrams
3. **Deployment**: Deploy to dev/staging
4. **Monitoring**: Set up Grafana dashboards for metrics
5. **PR**: Create pull request with summary

➡️ See [REFACTORING-CHECKLIST.md](../REFACTORING-CHECKLIST.md#final-validation--pr) for final validation steps

---

## Related Resources

- **Patterns**: System.Diagnostics.Metrics, OpenTelemetry
- **Monitoring**: Prometheus, Grafana, Azure Monitor
- **Architecture**: [TARGET-ARCHITECTURE.md](../TARGET-ARCHITECTURE.md#observability)
- **Phase 4 Overview**: [REFACTORING-CHECKLIST.md](../REFACTORING-CHECKLIST.md#phase-4-token-caching)
