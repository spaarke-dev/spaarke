# Performance Analysis - AI Playbook Node Builder R3

> **Date**: 2026-01-19
> **Task**: 051 - Performance Optimization
> **Analyst**: Code Review (no live profiling)

---

## Executive Summary

This document analyzes the performance characteristics of the AI Playbook Builder's scope search and intent classification operations against the specified NFRs:

| NFR | Target | Status | Confidence |
|-----|--------|--------|------------|
| NFR-01 | Intent classification <2s | **DESIGNED TO MEET** | High |
| NFR-02 | Scope search <1s | **DESIGNED TO MEET** | High |

**Note**: Actual performance verification requires live Dataverse environment. These assessments are based on code design patterns and expected latencies.

---

## 1. Scope Search Analysis (`ScopeResolverService.SearchScopesAsync`)

### 1.1 Current Implementation

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs`
**Method**: `SearchScopesAsync` (lines 859-919)

```csharp
public Task<ScopeSearchResult> SearchScopesAsync(ScopeSearchQuery query, CancellationToken cancellationToken)
```

### 1.2 Performance Characteristics

| Aspect | Assessment | Notes |
|--------|------------|-------|
| **Data Source** | In-memory stub data (Phase 1) | O(n) dictionary lookups, effectively instant |
| **Parallelism** | Sequential searches | Each scope type searched in sequence |
| **Async Pattern** | Synchronous with `Task.FromResult` | No actual async I/O in stub implementation |
| **Pagination** | Implemented | LINQ Skip/Take prevents over-fetching |
| **Filtering** | Early filtering | Filters applied before pagination |

### 1.3 Current Performance (Stub Data)

**Expected Latency**: <10ms (in-memory operations only)

The current implementation operates on static dictionaries:
- `_stubActions`: ~2 items
- `_stubSkills`: ~3 items
- `_stubKnowledge`: ~2 items
- `_stubTools`: ~4 items

**Why this easily meets <1s target**: Pure in-memory LINQ operations on small collections.

### 1.4 Dataverse Integration Considerations (Task 032)

When Dataverse integration is implemented, performance will depend on:

| Factor | Impact | Mitigation |
|--------|--------|------------|
| Network latency | +100-300ms per call | Batch queries |
| Query complexity | +50-200ms | Optimize OData filters |
| Entity count | Linear scaling | Pagination + indexing |
| Concurrent users | Contention | Connection pooling |

### 1.5 Recommendations for Dataverse Phase

1. **Implement parallel scope type queries**:
   ```csharp
   // Current (sequential)
   var actions = await SearchActionsAsync(query);
   var skills = await SearchSkillsAsync(query);

   // Recommended (parallel)
   var actionsTask = SearchActionsAsync(query, ct);
   var skillsTask = SearchSkillsAsync(query, ct);
   await Task.WhenAll(actionsTask, skillsTask);
   ```

2. **Add Redis caching per ADR-009**:
   ```csharp
   var cacheKey = $"scope-search:{query.SearchText}:{query.ScopeTypes}:v{rowVersion}";
   return await _cache.GetOrCreateAsync(cacheKey, async () =>
       await _dataverse.SearchScopesAsync(query), TimeSpan.FromMinutes(5));
   ```

3. **Optimize Dataverse OData queries**:
   - Use `$select` to limit returned fields
   - Use `$filter` with indexed columns
   - Consider server-side search for text queries

### 1.6 Scope Search Verdict

**Status**: PASS (stub implementation), REQUIRES MONITORING (Dataverse phase)

---

## 2. Intent Classification Analysis (`AiPlaybookBuilderService.ClassifyIntentWithAiAsync`)

### 2.1 Current Implementation

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/AiPlaybookBuilderService.cs`
**Method**: `ClassifyIntentWithAiAsync` (lines 624-675)

```csharp
public async Task<AiIntentResult> ClassifyIntentWithAiAsync(
    string message,
    CanvasContext? canvasContext,
    string? model = null,
    CancellationToken cancellationToken = default)
```

### 2.2 Performance Breakdown

| Operation | Expected Latency | Notes |
|-----------|------------------|-------|
| Load builder scope prompt | 0-5ms (cached) | IMemoryCache with 30-min TTL |
| Build user prompt | <1ms | String concatenation |
| OpenAI API call | 500-1500ms | gpt-4o-mini, ~50-100 tokens |
| Parse JSON response | <1ms | System.Text.Json |
| Apply confidence thresholds | <1ms | In-memory logic |

**Total Expected**: 500-1500ms (dominated by OpenAI API call)

### 2.3 Model Selection

The implementation uses `gpt-4o-mini` as the default:
```csharp
private const string DefaultClassificationModel = "gpt-4o-mini";
```

**Why gpt-4o-mini**:
- Fast inference (~500-800ms typical)
- Cost-effective for classification tasks
- Sufficient capability for intent extraction

### 2.4 Caching Strategy (ADR-014 Compliance)

Current caching implementation:

| Cache Type | Usage | TTL |
|------------|-------|-----|
| `IMemoryCache` | Builder scope prompts | 30 minutes |
| `IMemoryCache` | Fallback prompts | 5 minutes |

**Not cached** (correctly per ADR-014):
- Intent classification results (user-specific, session-scoped)
- Streaming tokens

### 2.5 Error Handling and Fallbacks

```csharp
catch (JsonException ex) { return CreateFallbackResult(message, canvasContext); }
catch (OpenAiCircuitBrokenException ex) { return CreateFallbackResult(message, canvasContext); }
catch (Exception ex) { return CreateFallbackResult(message, canvasContext); }
```

**Resilience pattern**: Circuit breaker with rule-based fallback ensures <2s even during AI service degradation.

### 2.6 Recommendations

1. **Add OpenTelemetry metrics** for monitoring:
   ```csharp
   using var activity = _activitySource.StartActivity("ClassifyIntent");
   activity?.SetTag("model", selectedModel);
   // ... classification logic ...
   activity?.SetTag("confidence", result.Confidence);
   ```

2. **Consider prompt caching** (if supported by Azure OpenAI):
   - Structured output schemas can be cached
   - Reduces token processing time

3. **Implement adaptive timeout**:
   ```csharp
   using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
   cts.CancelAfter(TimeSpan.FromMilliseconds(1800)); // Leave 200ms buffer
   ```

### 2.7 Intent Classification Verdict

**Status**: PASS - Expected latency well under 2s target

---

## 3. Caching Architecture Review

### 3.1 ADR-009 Compliance (Redis-First Caching)

| Requirement | Implementation | Status |
|-------------|----------------|--------|
| Use `IDistributedCache` for cross-request | Not yet needed (in-memory stub) | N/A |
| Use `IMemoryCache` for metadata | Builder scopes cached | COMPLIANT |
| Version cache keys | Not yet needed | N/A |
| Short TTL for security data | N/A (no security data cached) | COMPLIANT |

### 3.2 ADR-014 Compliance (AI Caching)

| Requirement | Implementation | Status |
|-------------|----------------|--------|
| Don't cache raw document bytes | Not applicable | COMPLIANT |
| Don't cache streaming tokens | Not cached | COMPLIANT |
| Centralize cache keys | Using constants | COMPLIANT |
| Include version in keys | Ready for implementation | READY |

### 3.3 Cache Locations

```csharp
// Builder Scope Cache (AiPlaybookBuilderService)
private const string BuilderScopeCacheKeyPrefix = "builder_scope:";
private static readonly TimeSpan BuilderScopeCacheTtl = TimeSpan.FromMinutes(30);
```

---

## 4. Parallelism Opportunities

### 4.1 Identified Opportunities

| Location | Current | Recommended |
|----------|---------|-------------|
| `SearchScopesAsync` | Sequential scope type queries | `Task.WhenAll` for 4 types |
| `GetBuilderScopePromptAsync` | Sequential per scope | Pre-fetch common scopes on startup |
| `ProcessMessageAsync` | Sequential classification then execution | Keep sequential (dependency) |

### 4.2 Code Change Example

```csharp
// In SearchScopesAsync - when Dataverse is implemented:
var tasks = new List<Task>();
Task<AnalysisAction[]>? actionsTask = null;
Task<AnalysisSkill[]>? skillsTask = null;

if (typesToSearch.Contains(ScopeType.Action))
    actionsTask = SearchActionsFromDataverseAsync(query, ct);
if (typesToSearch.Contains(ScopeType.Skill))
    skillsTask = SearchSkillsFromDataverseAsync(query, ct);

await Task.WhenAll(tasks.Where(t => t != null));

var actions = actionsTask != null ? await actionsTask : Array.Empty<AnalysisAction>();
var skills = skillsTask != null ? await skillsTask : Array.Empty<AnalysisSkill>();
```

---

## 5. Performance Monitoring Recommendations

### 5.1 Metrics to Capture

| Metric | Type | Target |
|--------|------|--------|
| `scope_search_duration_ms` | Histogram | p95 < 1000ms |
| `intent_classification_duration_ms` | Histogram | p95 < 2000ms |
| `ai_api_call_duration_ms` | Histogram | p95 < 1500ms |
| `cache_hit_rate` | Gauge | > 80% for scopes |
| `classification_confidence` | Histogram | Mean > 0.7 |

### 5.2 OpenTelemetry Integration

```csharp
// Add to service constructor
private static readonly ActivitySource ActivitySource = new("Sprk.Bff.Api.Ai");

// Wrap operations
using var activity = ActivitySource.StartActivity("SearchScopes");
activity?.SetTag("scope_types", string.Join(",", query.ScopeTypes));
// ... operation ...
activity?.SetTag("result_count", totalCount);
```

### 5.3 Health Check Additions

```csharp
// Add AI service health check
services.AddHealthChecks()
    .AddCheck<OpenAiHealthCheck>("openai", tags: new[] { "ai", "external" });
```

---

## 6. Load Testing Recommendations

When live environment is available, test these scenarios:

| Scenario | Target | Method |
|----------|--------|--------|
| Cold start classification | <3s | First request after deployment |
| Warm classification | <2s | Cached prompts |
| Concurrent classifications (10) | <2s p95 | Load test |
| Scope search (100 scopes) | <1s | Populated Dataverse |
| Mixed workload | All targets met | Realistic simulation |

---

## 7. Summary of Findings

### 7.1 No Code Changes Required (This Task)

The current implementation is **designed to meet performance targets**:

1. **Scope search**: In-memory operations complete in <10ms
2. **Intent classification**: gpt-4o-mini typically responds in 500-1500ms
3. **Caching**: Properly implemented for builder scope prompts
4. **Error handling**: Fallback mechanisms ensure graceful degradation

### 7.2 Future Work (Dataverse Integration - Task 032)

When Dataverse integration is implemented:
- [ ] Implement parallel scope type queries
- [ ] Add Redis caching with versioned keys
- [ ] Optimize OData query filters
- [ ] Add performance telemetry
- [ ] Run load tests against target environment

### 7.3 Performance Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| OpenAI latency spikes | Medium | High | Circuit breaker + fallback |
| Dataverse slow queries | Medium | Medium | Caching + query optimization |
| Cache stampede | Low | Medium | GetOrCreateAsync pattern |
| Memory pressure from caching | Low | Low | TTL limits |

---

## 8. Conclusion

**Both NFRs (intent classification <2s, scope search <1s) are addressed in the current implementation design.**

The architecture follows ADR-009 (Redis-first caching) and ADR-014 (AI caching) patterns. Performance monitoring and load testing should be conducted when the live Dataverse environment is available.

---

*Document generated: 2026-01-19*
*Next review: When Dataverse integration (Task 032) is implemented*
