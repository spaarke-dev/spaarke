# Performance Analysis - Playbook Execution

> **Task**: 046 - Performance Optimization
> **Date**: 2026-01-13
> **Phase**: 5 - Production Hardening

---

## Executive Summary

Performance optimizations implemented to ensure 5-node execution under 60 seconds target. Key improvements focus on caching, pre-loading, and reduced redundant lookups.

---

## Performance Optimizations Implemented

### 1. Per-Run Action Caching

**Location**: `PlaybookRunContext.cs`

**Problem**: Each node execution called `_scopeResolver.GetActionAsync()` independently, resulting in N Dataverse calls for N nodes even when nodes share the same action.

**Solution**: Added `_actionCache` (ConcurrentDictionary) with `GetOrAddActionAsync()` method that:
- Checks cache first before calling Dataverse
- Thread-safe for parallel batch execution
- Scoped to single run (no cross-run cache pollution)

**Impact**: For a 5-node playbook using 2 unique actions:
- Before: 5 Dataverse calls
- After: 2 Dataverse calls (60% reduction)

### 2. Action Pre-loading

**Location**: `PlaybookOrchestrationService.cs` - `ExecuteNodeBasedModeAsync()`

**Problem**: Sequential action lookups during node execution adds latency to the critical path.

**Solution**: Added pre-loading step before batch execution:
```csharp
var actionIds = nodes.Select(n => n.ActionId).Distinct().ToArray();
await context.PreloadActionsAsync(actionIds, ...);
```

**Impact**:
- All actions loaded in parallel before execution starts
- Removes action lookup from critical path
- Logged with `Action preload completed - DurationMs` for monitoring

### 3. Per-Run Scope Caching

**Location**: `PlaybookRunContext.cs`

**Problem**: Each node called `_scopeResolver.ResolveNodeScopesAsync()` even when the same node might be referenced multiple times (rare but possible in complex playbooks).

**Solution**: Added `_scopeCache` with `GetOrAddScopesAsync()` method that caches resolved scopes by node ID.

**Impact**: Minimal for typical playbooks, but provides protection against edge cases.

### 4. Document Extraction Sharing

**Location**: `PlaybookRunContext.cs`

**Problem**: When document text extraction is implemented, each node processing the same document would extract text independently.

**Solution**: Added `_documentCache` with `GetOrAddDocumentAsync()` method. Infrastructure ready for when document extraction is implemented.

**Impact (Future)**:
- Document text extraction happens once per document per run
- All nodes share the extracted text

### 5. Cache Statistics Logging

**Location**: `PlaybookOrchestrationService.cs` - completion logging

**Addition**: Cache stats included in run completion log:
```
CachedActions: {count}, CachedScopes: {count}, CachedDocuments: {count}
```

**Use**: Monitor cache effectiveness in App Insights, identify optimization opportunities.

---

## Hot Path Analysis

### Critical Path (5-node execution)

| Phase | Description | Estimated Time | Optimized |
|-------|-------------|----------------|-----------|
| 1 | Load nodes from Dataverse | ~100ms | - |
| 2 | Build execution graph | ~1ms | N/A (already fast) |
| 3 | Pre-load actions | ~50-100ms | Yes (parallel) |
| 4 | Per-node execution | 5-15s per node | Cached lookups |
| 5 | AI model inference | 2-10s per node | N/A (external) |

**Key Insight**: AI model inference dominates execution time. Caching optimizations reduce overhead but don't affect the AI call duration.

### Parallel Execution

Nodes in the same batch execute in parallel (up to `DefaultMaxParallelNodes = 3`). This is the primary performance lever for multi-node playbooks.

**Example 5-node playbook**:
- If all 5 nodes are independent: ~2 batches (3 + 2 nodes)
- Total time: ~20s (vs 50s sequential)

---

## Bottleneck Identification

### Current Bottlenecks

1. **AI Model Inference** (60-80% of execution time)
   - Azure OpenAI API latency: 2-10s per call
   - Cannot be cached (content-dependent)
   - Mitigation: Parallel execution, streaming

2. **Document Text Extraction** (when implemented)
   - PDF/Office extraction can take 1-5s per document
   - Now cacheable via `_documentCache`

3. **Dataverse Queries**
   - Node/Action/Scope lookups
   - Now optimized with pre-loading and caching

### Non-Bottlenecks

- Execution graph building: O(n) with n nodes, <1ms
- SSE event streaming: Negligible overhead
- In-memory operations: Microseconds

---

## Performance Metrics

### Target

| Metric | Target | Status |
|--------|--------|--------|
| 5-node execution | < 60 seconds | Expected to meet |
| Action cache hit rate | > 50% | Logged for monitoring |
| Scope cache hit rate | > 0% | Logged for monitoring |

### Monitoring Queries (App Insights KQL)

```kql
// Average execution time by node count
traces
| where message contains "Playbook run completed"
| extend TotalNodes = tolong(customDimensions.TotalNodes)
| extend DurationMs = tolong(customDimensions.DurationMs)
| summarize avg(DurationMs), percentile(DurationMs, 95) by TotalNodes
| order by TotalNodes asc

// Cache effectiveness
traces
| where message contains "Playbook run completed"
| extend CachedActions = tolong(customDimensions.CachedActions)
| extend CachedScopes = tolong(customDimensions.CachedScopes)
| extend TotalNodes = tolong(customDimensions.TotalNodes)
| summarize avg(CachedActions), avg(CachedScopes), avg(TotalNodes)
```

---

## Future Optimization Opportunities

### High Impact

1. **Redis Caching for Actions** (ADR-009)
   - Currently: Per-run in-memory cache
   - Future: Cache actions in Redis with version-based invalidation
   - Impact: Eliminates Dataverse calls for frequently-used actions

2. **Parallel Document Processing**
   - When multiple documents are processed, extract text in parallel
   - Combine with document caching for maximum efficiency

### Medium Impact

3. **Connection Pooling Optimization**
   - Ensure Dataverse HttpClient reuse
   - Already following ADR-010 (single HttpClient per upstream)

4. **Batch Scope Resolution**
   - Instead of per-node scope resolution, batch all nodes
   - Single Dataverse query with `$filter` for multiple IDs

### Low Impact (Not Recommended)

5. **L1 In-Memory Cache**
   - ADR-009 prohibits without profiling proof
   - Per-run cache is sufficient for current workloads

---

## Implementation Details

### Files Modified

| File | Changes |
|------|---------|
| `PlaybookRunContext.cs` | Added caching dictionaries and methods |
| `PlaybookOrchestrationService.cs` | Updated to use cached lookups, added pre-loading |

### Thread Safety

All caches use `ConcurrentDictionary<Guid, T>` with `TryGetValue`/`TryAdd` pattern for thread-safe access during parallel batch execution.

### Memory Considerations

Caches are scoped to `PlaybookRunContext`:
- Garbage collected when run context is removed from `_activeRuns`
- Run contexts cleaned up after 1 hour via `ScheduleCleanup()`

---

## Conclusion

Performance optimizations implemented reduce redundant Dataverse calls and prepare infrastructure for document extraction caching. The 60-second target for 5-node execution is achievable given that:

1. Nodes execute in parallel (3 at a time)
2. Action lookups are pre-loaded and cached
3. Scope resolution is cached per-node

Primary remaining bottleneck is AI model inference time, which is external to the system.
