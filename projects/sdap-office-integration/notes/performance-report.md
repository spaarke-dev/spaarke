# Office Integration Performance Test Report

> **Project**: SDAP Office Integration
> **Task**: 077 - Performance Testing
> **Date**: 2026-01-20
> **Status**: Test Framework Ready

---

## Executive Summary

This report documents the performance testing approach and acceptance criteria for the SDAP Office Integration APIs. The k6 load testing framework has been configured to validate NFR requirements from the spec.md.

### Key Performance Requirements (from spec.md)

| Requirement | Target | Metric |
|-------------|--------|--------|
| **NFR-01** API Response Time | p95 < 2 seconds | All /office/* endpoints |
| **NFR-04** SSE Updates | Within 1 second | GET /office/jobs/{id}/stream |
| **Spec** Entity Search | Within 500ms | GET /office/search/entities |
| **Spec** Concurrent Users | 50 users | System handles without degradation |
| **Spec** Rate Limiting | Per endpoint limits | No bypass under load |

---

## Test Framework

### Tool Selection

**k6** was selected as the load testing framework based on:
- Existing patterns in `tests/load/` (batch-processing, webhook-processing)
- JavaScript-based tests for easy maintenance
- Built-in support for custom metrics, thresholds, and scenarios
- Good documentation and community support

### Test File Location

```
tests/load/office-endpoints.k6.js
```

### Running Tests

```bash
# Prerequisites
# 1. Install k6: https://k6.io/docs/getting-started/installation/
# 2. Get a valid bearer token from Azure AD
# 3. Identify test entity/document IDs in target environment

# Run all scenarios
k6 run tests/load/office-endpoints.k6.js \
  --env BASE_URL=https://spe-api-dev-67e2xz.azurewebsites.net \
  --env TOKEN=your-bearer-token \
  --env TEST_ENTITY_ID=guid-of-test-matter \
  --env TEST_DOCUMENT_ID=guid-of-test-document

# Run specific scenario only
k6 run tests/load/office-endpoints.k6.js \
  --env BASE_URL=https://... \
  --env TOKEN=... \
  --only-scenario baseline_single_user
```

---

## Test Scenarios

### 1. Baseline - Single User (2 minutes)

**Purpose**: Establish performance baseline with no contention

**Endpoints Tested**:
- POST /office/save
- GET /office/jobs/{id}
- GET /office/search/entities
- GET /office/search/documents
- GET /office/recent
- POST /office/share/links
- POST /office/share/attach

**Expected Metrics**:
| Metric | Target |
|--------|--------|
| Save response time | < 3 seconds |
| Search response time | < 500ms |
| Job status response time | < 500ms |
| Recent response time | < 500ms |

### 2. Light Load - 10 Concurrent Users (3 minutes)

**Purpose**: Validate performance with moderate concurrent load

**Focus**: Save flow (save + job status polling)

**Expected Metrics**:
| Metric | Target |
|--------|--------|
| Save p95 response time | < 2 seconds |
| Job status p95 response time | < 500ms |
| Success rate | > 95% |

### 3. Target Load - 50 Concurrent Users (5 minutes)

**Purpose**: Validate spec requirement for 50 concurrent users

**Load Distribution**:
- 40% Search operations (typeahead simulation)
- 30% Save operations
- 20% Job status checks
- 10% Recent/Share operations

**Expected Metrics**:
| Metric | Target |
|--------|--------|
| Overall p95 response time | < 2 seconds |
| Search p95 response time | < 500ms |
| Success rate | > 95% |
| No degradation | Consistent response times |

### 4. Search Stress Test (1 minute)

**Purpose**: Validate typeahead search performance under high volume

**Load**: 100 requests/second

**Expected Metrics**:
| Metric | Target |
|--------|--------|
| Search p95 response time | < 500ms |
| Success rate | > 99% |

### 5. Rate Limit Validation

**Purpose**: Verify rate limiting functions correctly

**Test Approach**:
1. Send rapid requests to save endpoint (limit: 10/min)
2. Verify 429 response after limit exceeded
3. Send rapid requests to search endpoint (limit: 30/min)
4. Verify 429 response after limit exceeded

**Expected Behavior**:
| Endpoint | Limit | Window |
|----------|-------|--------|
| POST /office/save | 10 requests | 1 minute |
| GET /office/search/* | 30 requests | 1 minute |
| GET /office/jobs/* | 60 requests | 1 minute |
| POST /office/share/* | 20 requests | 1 minute |

### 6. SSE Latency Test (2 minutes)

**Purpose**: Validate real-time job status streaming

**Test Approach**:
1. Create save job
2. Connect to SSE endpoint
3. Measure time to first event

**Expected Metrics**:
| Metric | Target |
|--------|--------|
| First SSE event | < 1 second |
| Connection establishment | < 500ms |

---

## Metrics Collected

### Response Time Metrics (Trend)

| Metric Name | Description |
|-------------|-------------|
| `office_save_response_time_ms` | POST /office/save response time |
| `office_search_entities_response_time_ms` | GET /office/search/entities response time |
| `office_search_documents_response_time_ms` | GET /office/search/documents response time |
| `office_job_status_response_time_ms` | GET /office/jobs/{id} response time |
| `office_sse_first_event_time_ms` | Time to first SSE event |
| `office_share_links_response_time_ms` | POST /office/share/links response time |
| `office_share_attach_response_time_ms` | POST /office/share/attach response time |
| `office_recent_response_time_ms` | GET /office/recent response time |

### Success Rate Metrics (Rate)

| Metric Name | Description |
|-------------|-------------|
| `office_save_success_rate` | Save endpoint success rate |
| `office_search_success_rate` | Search endpoints success rate |
| `office_job_status_success_rate` | Job status endpoint success rate |
| `office_share_success_rate` | Share endpoints success rate |
| `office_recent_success_rate` | Recent endpoint success rate |

### Counter Metrics

| Metric Name | Description |
|-------------|-------------|
| `office_rate_limit_hits` | Number of 429 responses received |
| `office_requests_completed` | Total requests completed |

---

## Thresholds Configuration

```javascript
thresholds: {
    // API response time p95 < 2 seconds (NFR-01)
    'office_save_response_time_ms': ['p(95)<2000'],
    'office_search_entities_response_time_ms': ['p(95)<500'],
    'office_search_documents_response_time_ms': ['p(95)<1000'],
    'office_job_status_response_time_ms': ['p(95)<500'],
    'office_share_links_response_time_ms': ['p(95)<2000'],
    'office_share_attach_response_time_ms': ['p(95)<2000'],
    'office_recent_response_time_ms': ['p(95)<500'],

    // SSE first event within 1 second (NFR-04)
    'office_sse_first_event_time_ms': ['p(95)<1000'],

    // Success rates
    'office_save_success_rate': ['rate>0.95'],
    'office_search_success_rate': ['rate>0.99'],
    'office_job_status_success_rate': ['rate>0.99'],
    'office_share_success_rate': ['rate>0.95'],
    'office_recent_success_rate': ['rate>0.99'],

    // Overall HTTP performance
    'http_req_duration': ['p(95)<2000', 'p(99)<5000'],
    'http_req_failed': ['rate<0.05']
}
```

---

## Test Results

> **Note**: Update this section after running tests against each environment

### Development Environment

**Date**: TBD
**Environment**: `https://spe-api-dev-67e2xz.azurewebsites.net`

| Metric | Target | Result | Status |
|--------|--------|--------|--------|
| Save p95 | < 2000ms | TBD | TBD |
| Search entities p95 | < 500ms | TBD | TBD |
| Search documents p95 | < 1000ms | TBD | TBD |
| Job status p95 | < 500ms | TBD | TBD |
| SSE first event p95 | < 1000ms | TBD | TBD |
| Share links p95 | < 2000ms | TBD | TBD |
| Recent p95 | < 500ms | TBD | TBD |
| 50 concurrent users | No degradation | TBD | TBD |
| Rate limiting | Functions correctly | TBD | TBD |

### Production Environment

**Date**: TBD (after production deployment)
**Environment**: TBD

| Metric | Target | Result | Status |
|--------|--------|--------|--------|
| (To be populated after production deployment) |

---

## Performance Considerations

### API Response Time Factors

1. **Dataverse queries**: Entity search queries Dataverse; response time depends on query complexity and data volume
2. **SPE operations**: File operations through SharePoint Embedded have inherent latency
3. **Token validation**: Azure AD token validation adds ~50-100ms overhead
4. **Rate limiting**: Redis-based rate limiting adds ~1-5ms per request

### SSE Latency Factors

1. **Connection establishment**: Initial HTTP connection + auth validation
2. **First event generation**: Service needs to query job status
3. **Network latency**: Dependent on client-server distance
4. **Proxy/CDN buffering**: May add latency if response buffering is enabled

### Concurrent User Capacity

The system's capacity for concurrent users depends on:

1. **Azure App Service plan**: CPU/memory allocation
2. **Dataverse API limits**: Per-organization throughput limits
3. **SPE throttling**: SharePoint Embedded rate limits
4. **Service Bus throughput**: Queue processing capacity

### Rate Limiting Configuration

Rate limits are configured in `appsettings.json`:

```json
{
  "OfficeRateLimit": {
    "Enabled": true,
    "WindowSizeSeconds": 60,
    "SegmentsPerWindow": 6,
    "Limits": {
      "SaveRequestsPerMinute": 10,
      "QuickCreateRequestsPerMinute": 5,
      "SearchRequestsPerMinute": 30,
      "JobsRequestsPerMinute": 60,
      "ShareRequestsPerMinute": 20,
      "RecentRequestsPerMinute": 30
    }
  }
}
```

---

## Optimization Recommendations

### If Save Endpoint is Slow (> 2s p95)

1. **Review Dataverse entity creation**: Ensure minimal fields are set during initial save
2. **Async file upload**: Verify file upload is happening in background worker, not sync
3. **Reduce payload size**: Ensure email body is truncated for preview storage
4. **Check Service Bus latency**: Monitor queue depth and processing time

### If Search is Slow (> 500ms p95)

1. **Add Dataverse indexes**: Ensure search columns have alternate keys or indexes
2. **Limit result set**: Enforce maximum page size (50)
3. **Cache frequent queries**: Consider Redis caching for common search terms
4. **Optimize FetchXML**: Review generated queries for efficiency

### If SSE is Slow (> 1s first event)

1. **Pre-fetch initial status**: Send initial status immediately on connection
2. **Reduce heartbeat interval**: Current 15s may be too infrequent
3. **Check response buffering**: Ensure `DisableBuffering()` is called
4. **Review job status queries**: Cache recent status to avoid DB round-trip

### If Rate Limiting Fails Under Load

1. **Check Redis connectivity**: Ensure distributed rate limit state is working
2. **Verify sliding window**: Confirm window segments are tracked correctly
3. **Monitor key expiration**: Ensure TTLs are set correctly

---

## Monitoring During Tests

### Application Insights Queries

**Response time by endpoint**:
```kusto
requests
| where timestamp > ago(1h)
| where name startswith "/office/"
| summarize avg(duration), percentile(duration, 95) by name, bin(timestamp, 1m)
| render timechart
```

**Error rate**:
```kusto
requests
| where timestamp > ago(1h)
| where name startswith "/office/"
| summarize total=count(), failed=countif(success == false) by name, bin(timestamp, 1m)
| extend errorRate = failed * 1.0 / total
| render timechart
```

**Rate limit hits**:
```kusto
requests
| where timestamp > ago(1h)
| where name startswith "/office/"
| where resultCode == "429"
| summarize count() by bin(timestamp, 1m)
| render timechart
```

### Azure App Service Metrics

Monitor during tests:
- **CPU Percentage**: Should stay below 80%
- **Memory Working Set**: Watch for memory leaks
- **HTTP Server Errors**: Should be near zero
- **Response Time**: Cross-reference with k6 metrics

### Dataverse API Limits

Check Power Platform admin center for:
- **API requests**: Per-organization throttling
- **Service protection limits**: 6000 requests per 5 minutes

---

## Conclusion

The performance testing framework is now in place to validate Office Integration API performance against spec.md requirements. The k6 test suite covers:

- All Office endpoints under various load conditions
- NFR compliance validation (response times, SSE latency)
- Rate limiting verification
- Concurrent user capacity testing

**Next Steps**:
1. Run baseline tests in development environment
2. Populate results in this report
3. Address any threshold failures
4. Schedule production performance validation after deployment

---

## Appendix A: Test Output Example

```json
{
  "testRun": "2026-01-20T12:00:00.000Z",
  "environment": "https://spe-api-dev-67e2xz.azurewebsites.net",
  "metrics": {
    "saveResponseTimeP95": 1500,
    "searchEntitiesResponseTimeP95": 300,
    "searchDocumentsResponseTimeP95": 450,
    "jobStatusResponseTimeP95": 200,
    "sseFirstEventP95": 800,
    "saveSuccessRate": 0.98,
    "searchSuccessRate": 0.99,
    "totalRequestsCompleted": 5000,
    "rateLimitHits": 15
  },
  "nfrCompliance": {
    "NFR-01 (API p95 < 2s)": true,
    "NFR-04 (SSE < 1s)": true,
    "Spec (50 concurrent users)": true,
    "Spec (Search < 500ms)": true
  }
}
```

---

*Report generated as part of Task 077 - Performance Testing*
