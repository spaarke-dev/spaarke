# Session Restore Load Test Plan & Report

> **Task**: AIPU2-106 (Session Restore E2E)
> **NFR Target**: p95 < 500ms end-to-end restore latency
> **Endpoint**: `GET /api/ai/chat/sessions/{sessionId}/restore`
> **Date**: 2026-05-17

---

## 1. Test Session Specification

The test session must represent a realistic worst-case scenario for restore latency:

| Property | Value | Rationale |
|----------|-------|-----------|
| Message count | 25+ | Triggers summarization (threshold: 25 messages or 8,000 tokens) |
| Widget states | 3 | Simulates active three-pane layout with entity-analysis, findings, and timeline widgets |
| Entity references | 2-3 | At least one `sprk_matter` and one `sprk_document` with saved ETags |
| Conversation summary | Present | Forces the summary + last-10-messages reconstruction path |
| Playbook | Non-null | Exercises the full `PlaybookId` resolution path |

### Widget State Payloads

Each widget state is a serialized JSON string stored in `StoredSession.WidgetStates`:

| Widget Instance ID | Description | Approximate Size |
|--------------------|-------------|------------------|
| `entity-analysis-1` | Entity analysis panel state (selected entity, analysis results) | ~2 KB |
| `findings-1` | Findings widget state (finding list, filters, sort order) | ~1.5 KB |
| `timeline-1` | Timeline widget state (date range, visible events) | ~1 KB |

---

## 2. Restore Flow & Instrumentation Points

The session restore flow has four server-side phases and one client-side phase:

```
Client                        BFF API                        Cosmos DB / Dataverse
  |                              |                                  |
  |-- GET .../restore ---------->|                                  |
  |                              |-- Step 1: LoadSession ---------->| Cosmos
  |                              |<-- StoredSession ----------------|
  |                              |                                  |
  |                              |-- Step 2: CheckStaleness ------->| Dataverse (parallel)
  |                              |<-- staleRefs[] ------------------|
  |                              |                                  |
  |                              |-- Step 3: ReconstructContext     | (in-memory)
  |                              |-- Step 4: CollectWidgetStates    | (in-memory)
  |                              |                                  |
  |<-- SessionRestoreResponse ---|                                  |
  |                                                                 |
  |-- Render (React setState) --->                                  |
```

### Server-Side Timing Spans

These spans are already instrumented via `Stopwatch` in `SessionRestoreService.RestoreSessionAsync`:

| Span ID | Phase | Code Location | What It Measures |
|---------|-------|---------------|------------------|
| `S1` | Cosmos DB read | `_persistence.LoadSessionAsync()` | Network RTT to Cosmos + deserialization |
| `S2` | Entity staleness check | `CheckEntityStalenessAsync()` | Token acquisition + parallel Dataverse OData GETs |
| `S3` | Context reconstruction | `ReconstructContext()` | StringBuilder assembly (summary + last 10 messages) |
| `S4` | Widget state collection | `session.WidgetStates` access | Dictionary read (negligible) |
| `S_total` | Full server restore | `sw.ElapsedMilliseconds` (line 113-119) | All four phases combined |

#### Recommended Additional Instrumentation

Add `Stopwatch` sub-spans inside `RestoreSessionAsync` for per-phase visibility:

```csharp
// Phase 1 timing
var swPhase1 = Stopwatch.StartNew();
var session = await _persistence.LoadSessionAsync(tenantId, sessionId, ct);
swPhase1.Stop();

// Phase 2 timing
var swPhase2 = Stopwatch.StartNew();
var staleRefs = await CheckEntityStalenessAsync(session.EntityRefs, ct);
swPhase2.Stop();

// Phase 3 timing
var swPhase3 = Stopwatch.StartNew();
var (reconstructedContext, wasSummarized) = ReconstructContext(session);
swPhase3.Stop();

_logger.LogInformation(
    "SessionRestoreService: phase breakdown — cosmos={CosmosMs}ms, staleness={StalenessMs}ms, reconstruct={ReconstructMs}ms",
    swPhase1.ElapsedMilliseconds, swPhase2.ElapsedMilliseconds, swPhase3.ElapsedMilliseconds);
```

### Frontend Timing (performance.mark)

The `useSessionRestore` hook already measures total client time via `performance.now()` (line 74, 111). Add `performance.mark` / `performance.measure` for structured DevTools visibility:

```typescript
// Before fetch
performance.mark("session-restore-start");

// After response parsed
performance.mark("session-restore-server-done");

// After React state applied
performance.mark("session-restore-render-done");

performance.measure("session-restore-fetch", "session-restore-start", "session-restore-server-done");
performance.measure("session-restore-render", "session-restore-server-done", "session-restore-render-done");
performance.measure("session-restore-e2e", "session-restore-start", "session-restore-render-done");
```

---

## 3. Measurement Methodology

### Test Protocol

1. **Create test session** using `scripts/Create-TestSession.ps1` (25+ messages, 3 widgets, entity refs)
2. **Warm-up**: Send 3 restore requests (discard results) to prime Cosmos SDK connection pool and JIT
3. **Measurement**: Execute 20 sequential requests using `scripts/Test-SessionRestoreLatency.ps1`
4. **Record**: Capture per-request latency, HTTP status, and server-reported `restoreLatencyMs`
5. **Repeat**: Run 3 measurement rounds, report aggregate statistics

### Why Sequential (Not Concurrent)

The NFR target is p95 < 500ms for a **single user resuming a session**. Sequential requests isolate per-request latency without introducing queuing artifacts. Concurrent load testing is covered separately by `scripts/load-tests/Run-LoadTest.ps1`.

### Environment Requirements

- BFF API running (dev environment or local `dotnet run`)
- Cosmos DB accessible (dev or emulator)
- Dataverse accessible (for entity staleness ETag checks)
- Valid auth token (via `az account get-access-token` or `pac auth token`)

---

## 4. Per-Phase Timing Breakdown Template

Fill in after test execution:

| Phase | Description | p50 (ms) | p95 (ms) | min (ms) | max (ms) | mean (ms) |
|-------|-------------|----------|----------|----------|----------|-----------|
| S1 | Cosmos DB read | | | | | |
| S2 | Entity staleness check (parallel) | | | | | |
| S3 | Context reconstruction | | | | | |
| S4 | Widget state collection | | | | | |
| S_total | Server total (from response header) | | | | | |
| C_fetch | Client fetch (network + server) | | | | | |
| C_render | Client render (React setState) | | | | | |
| E2E | End-to-end (client-measured) | | | | | |

---

## 5. Percentile Calculation Approach

Given N=20 measurements sorted ascending:

| Percentile | Formula | Index for N=20 |
|------------|---------|----------------|
| p50 (median) | `values[floor(N * 0.50)]` | `values[10]` |
| p95 | `values[floor(N * 0.95)]` | `values[19]` (last element) |
| min | `values[0]` | `values[0]` |
| max | `values[N-1]` | `values[19]` |
| mean | `sum(values) / N` | arithmetic mean |

The scripts use 0-based indexing with `[math]::Floor()` for index calculation.

---

## 6. Optimization Checklist

If p95 exceeds 500ms, investigate in order of expected impact:

### 6.1 Cosmos DB Read (S1) - Target: < 50ms

- [ ] **Indexing policy**: Ensure composite index on `/tenantId` + `/sessionId` (partition key + id)
- [ ] **Point read**: Verify `LoadSessionAsync` uses `ReadItemAsync` (point read) not `GetItemQueryIterator` (query scan)
- [ ] **Consistency level**: Confirm `Session` or `Eventual` consistency (not `Strong`) for reads
- [ ] **Connection mode**: Use `Direct` mode (TCP) not `Gateway` (HTTP) — check `CosmosClient` config
- [ ] **Region**: Ensure Cosmos account region matches App Service region (cross-region RTT adds 20-100ms)

### 6.2 Entity Staleness Check (S2) - Target: < 200ms

- [ ] **Token caching**: Cache Dataverse bearer token (currently acquires fresh `ClientSecretCredential` per request)
- [ ] **Batch OData request**: Replace N parallel `GET` requests with a single `$batch` request
- [ ] **Skip when no entity refs**: Short-circuit when `EntityRefs.Count == 0` (already implemented)
- [ ] **HTTP connection pooling**: Verify `IHttpClientFactory` reuses connections via `DataverseETagCheck` named client
- [ ] **Conditional skip**: Consider skipping staleness check for sessions < 5 minutes old

### 6.3 Context Reconstruction (S3) - Target: < 5ms

- [ ] **StringBuilder capacity**: Pre-allocate capacity based on message count + summary length
- [ ] **Summary pre-generation**: Ensure `SessionSummarizationService` generates summaries asynchronously (not during restore)
- [ ] **Avoid ToList()**: `messages.Skip(N).ToList()` allocates — use `Span` or direct enumeration

### 6.4 Parallel Widget Refresh - Target: no additional latency

- [ ] **Widget state is passive**: Widget states are just serialized strings — no refresh needed during restore
- [ ] **Frontend lazy load**: If widgets need data refresh post-restore, do it after initial render (not blocking)

### 6.5 Network & Serialization

- [ ] **Response size**: Measure JSON response payload size; consider trimming if > 50 KB
- [ ] **Compression**: Ensure `gzip`/`br` response compression is enabled in BFF API middleware
- [ ] **HTTP/2**: Confirm App Service uses HTTP/2 for reduced latency

---

## 7. Results Template

### Run Configuration

| Setting | Value |
|---------|-------|
| Date | |
| Environment | |
| BFF API version | |
| Session ID | |
| Message count | |
| Widget count | |
| Entity ref count | |
| Has summary | |

### Latency Results (20 requests)

| Request # | HTTP Status | Server Latency (ms) | Client Latency (ms) | Notes |
|-----------|-------------|---------------------|---------------------|-------|
| 1 | | | | |
| 2 | | | | |
| 3 | | | | |
| 4 | | | | |
| 5 | | | | |
| 6 | | | | |
| 7 | | | | |
| 8 | | | | |
| 9 | | | | |
| 10 | | | | |
| 11 | | | | |
| 12 | | | | |
| 13 | | | | |
| 14 | | | | |
| 15 | | | | |
| 16 | | | | |
| 17 | | | | |
| 18 | | | | |
| 19 | | | | |
| 20 | | | | |

### Summary Statistics

| Metric | Server (ms) | Client (ms) |
|--------|-------------|-------------|
| min | | |
| max | | |
| mean | | |
| p50 | | |
| p95 | | |

### NFR Verdict

| Metric | Target | Actual | Status |
|--------|--------|--------|--------|
| p95 server latency | < 500ms | | |
| p95 client latency | < 500ms | | |
| Error rate | 0% | | |

### Observations & Recommendations

_Fill in after test execution._
