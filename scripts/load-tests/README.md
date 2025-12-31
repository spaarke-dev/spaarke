# SDAP AI Load Tests

> **Last Updated**: December 2025
> **Project**: AI Document Intelligence R3

---

## Overview

Load testing scripts to verify SDAP BFF API handles 100+ concurrent AI analysis requests with acceptable latency and no data corruption.

## Test Scenarios

| Scenario | VUs | Duration | Purpose |
|----------|-----|----------|---------|
| Baseline | 10 | 2 min | Warm-up, establish baseline metrics |
| Target | 100 | 5 min | Verify 100+ concurrent handling |
| Stress | 200+ | 10 min | Identify breaking point, test circuit breakers |

## Thresholds

| Metric | Target | Threshold |
|--------|--------|-----------|
| P95 Latency | < 2s | < 3s |
| P99 Latency | < 3s | < 5s |
| Success Rate | > 99% | > 95% |
| Error Rate | < 1% | < 5% |
| Circuit Opens | 0 | < 5 |

## Test Scripts

### Option 1: k6 (Recommended for Production)

**Prerequisites:**
- Install k6: https://k6.io/docs/getting-started/installation/

**Usage:**
```bash
# Baseline test
k6 run --vus 10 --duration 2m k6-ai-load-test.js

# Target test
k6 run --vus 100 --duration 5m k6-ai-load-test.js

# Stress test
k6 run --vus 200 --duration 10m k6-ai-load-test.js

# Full scenario run (baseline -> target -> stress)
k6 run k6-ai-load-test.js

# With authentication
API_BASE_URL="https://your-api.azurewebsites.net" \
AUTH_TOKEN="your-bearer-token" \
k6 run k6-ai-load-test.js
```

### Option 2: PowerShell (No Dependencies)

**Prerequisites:**
- PowerShell 5.1+ (Windows) or PowerShell Core (cross-platform)

**Usage:**
```powershell
# Baseline test
.\Run-LoadTest.ps1 -TestType baseline

# Target test
.\Run-LoadTest.ps1 -TestType target

# Stress test
.\Run-LoadTest.ps1 -TestType stress

# Custom configuration
.\Run-LoadTest.ps1 -Concurrency 50 -Duration 180 -BaseUrl "https://your-api.azurewebsites.net"

# With authentication
.\Run-LoadTest.ps1 -TestType target -AuthToken "your-bearer-token"
```

## Endpoints Tested

### Public (No Auth Required)
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/ping` | GET | Health check |
| `/api/resilience/health` | GET | Circuit breaker health |
| `/api/resilience/circuits` | GET | Circuit breaker states |

### Authenticated
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/ai/rag/search` | POST | RAG hybrid search |
| `/api/ai/analysis/execute` | POST | Analysis execution |
| `/api/ai/analysis/{id}/export` | POST | Export operations |

## Results

Results are saved to `results/` directory:
- `summary.json` - k6 summary output
- `load-test-{type}-{timestamp}.json` - PowerShell results

### Sample Result Format
```json
{
  "TestType": "target",
  "DurationSeconds": 300,
  "Concurrency": 100,
  "TotalRequests": 15000,
  "SuccessfulRequests": 14850,
  "SuccessRate": 99.0,
  "RequestsPerSecond": 50,
  "Latency": {
    "Average": 450,
    "P50": 320,
    "P95": 1200,
    "P99": 2100
  },
  "CircuitBreakerOpens": 0,
  "ThresholdsPassed": true
}
```

## Analyzing Results

### Key Metrics to Watch

1. **P95 Latency**: Should stay under 3000ms
2. **Error Rate**: Should stay under 5%
3. **Circuit Breaker Opens**: Should be 0 under normal load
4. **Throughput**: Should sustain target RPS

### Common Issues

| Symptom | Possible Cause | Fix |
|---------|---------------|-----|
| High P95 during stress | Resource saturation | Scale App Service, optimize queries |
| Circuit breaker opens | Azure OpenAI throttling | Implement backoff, increase quotas |
| Timeout errors | Slow dependencies | Check AI Search, OpenAI latency |
| 401 errors | Token expiration | Refresh auth token |

## CI/CD Integration

Add to Azure DevOps pipeline:
```yaml
- task: k6@0
  inputs:
    filename: 'scripts/load-tests/k6-ai-load-test.js'
    arguments: '--vus 100 --duration 5m'
```

## Related Documentation

- [AI-MONITORING-DASHBOARD.md](../../docs/guides/AI-MONITORING-DASHBOARD.md) - Monitor during tests
- [RAG-TROUBLESHOOTING.md](../../docs/guides/RAG-TROUBLESHOOTING.md) - Debug performance issues
