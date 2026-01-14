# Task 033 - Performance Baseline

## Date: December 4, 2025

## Summary

Performance validation tests created for SpeFileViewer PCF control. This document serves as the baseline template for recording performance measurements.

## Performance Targets (from Spec)

| Metric | Target | Description |
|--------|--------|-------------|
| Loading State | < 200ms | Time from init() call to loading overlay visible |
| Loading State (with nav) | < 500ms | Target + 300ms navigation margin |
| Preview Load (Warm BFF) | < 3s | Time from navigation to preview ready, warm API |
| Preview Load (Cold Start) | < 10s | Time including cold start overhead |
| /ping Response | < 100ms | Lightweight health check |
| /healthz Response | < 500ms | Full health check |

## Performance Test Suite

Created: `tests/e2e/specs/spe-file-viewer/performance.spec.ts`

### Tests Included

| Test | Target | Purpose |
|------|--------|---------|
| `loading state appears within 500ms` | < 500ms | Validates loading UX appears quickly |
| `preview loads within 3 seconds (warm)` | < 3000ms | Validates full load with warm BFF |
| `cold start time (documentation)` | N/A | Measures cold start, does not fail |
| `multiple load measurements` | P95 < 3000ms | Calculates percentile statistics |
| `/ping endpoint` | < 100ms | BFF health check performance |
| `/healthz endpoint` | < 500ms | BFF health check performance |
| `/status endpoint` | < 500ms | BFF status endpoint performance |

## Baseline Template

Record measurements after running tests in test environment:

### Environment: Development
**Date:** YYYY-MM-DD
**BFF URL:** https://spe-api-dev-67e2xz.azurewebsites.net
**Browser:** Microsoft Edge (chromium)

| Metric | Target | P50 | P95 | Max | Status |
|--------|--------|-----|-----|-----|--------|
| Loading State | < 500ms | - | - | - | Pending |
| Preview (Warm) | < 3000ms | - | - | - | Pending |
| Preview (Cold) | < 10000ms | - | - | - | Pending |
| /ping | < 100ms | - | - | - | Pending |
| /healthz | < 500ms | - | - | - | Pending |
| /status | < 500ms | - | - | - | Pending |

### Environment: Production
**Date:** YYYY-MM-DD
**BFF URL:** TBD
**Browser:** Microsoft Edge (chromium)

| Metric | Target | P50 | P95 | Max | Status |
|--------|--------|-----|-----|-----|--------|
| Loading State | < 500ms | - | - | - | Pending |
| Preview (Warm) | < 3000ms | - | - | - | Pending |
| Preview (Cold) | < 10000ms | - | - | - | Pending |
| /ping | < 100ms | - | - | - | Pending |
| /healthz | < 500ms | - | - | - | Pending |
| /status | < 500ms | - | - | - | Pending |

## Running Performance Tests

```bash
# Run all performance tests
npx playwright test spe-file-viewer/performance.spec.ts --headed

# Run with specific project (Edge)
npx playwright test spe-file-viewer/performance.spec.ts --project=edge --headed

# Generate HTML report with performance annotations
npx playwright test spe-file-viewer/performance.spec.ts --reporter=html

# Run multiple times for statistical significance
for i in {1..5}; do npx playwright test spe-file-viewer/performance.spec.ts; done
```

## Performance Optimization Implemented

Based on tasks completed in Phase 3:

| Task | Optimization | Expected Impact |
|------|--------------|-----------------|
| Task 020 | App Service Always On | Eliminates cold start for idle periods |
| Task 021 | /ping endpoint | Fast warm-up endpoint (< 100ms) |
| Task 022 | AbortController | Cancels stale requests on nav change |
| Task 023 | Graph Singleton | Verified singleton reduces overhead |

## Recommendations for Continuous Monitoring

### CI/CD Integration

Add performance tests to deployment pipeline:

```yaml
# .github/workflows/e2e.yml
- name: Run Performance Tests
  run: npx playwright test --grep @performance
  continue-on-error: false

- name: Archive Performance Results
  uses: actions/upload-artifact@v3
  with:
    name: performance-results
    path: test-results/
```

### Alerting Thresholds

| Metric | Warning | Critical |
|--------|---------|----------|
| Preview Load (P95) | > 2500ms | > 3000ms |
| /ping (P95) | > 80ms | > 100ms |
| Cold Start | > 8s | > 10s |

### Monitoring Tools

- **Application Insights:** Track BFF API response times
- **Playwright Reports:** HTML reports with performance annotations
- **Custom Dashboard:** Aggregate metrics over time

## Known Limitations

1. **Network Variability:** Test results vary based on network conditions
2. **Cold Start Measurement:** Azure App Service cold starts are non-deterministic
3. **Browser Overhead:** Playwright adds some overhead to timing measurements
4. **Test Environment:** Results may differ from production

## Acceptance Criteria Status

| Criterion | Status |
|-----------|--------|
| Performance test exists for loading state | ✅ |
| Loading state within 500ms (with margin) | Test created |
| Preview loads within 3s (warm BFF) | Test created |
| Cold start time documented | Test created |
| Performance baseline document created | ✅ |

## Next Steps

1. [ ] Deploy test environment with PCF control
2. [ ] Run performance tests and record baseline
3. [ ] Set up CI/CD performance testing
4. [ ] Configure alerting thresholds
5. [ ] Document production baseline after deployment
