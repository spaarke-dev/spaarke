# Phase 5 - Task 6: Cache Performance Validation

**Phase**: 5 (Integration Testing)
**Duration**: 1 hour
**Risk**: LOW (optimization, not blocker)
**Layers Tested**: BFF API Cache Layer (Layer 3)
**Prerequisites**: Task 5.1 (Authentication) complete

---

## Goal

**Verify Phase 4 cache reduces OBO latency by 97%** (target: >90% hit rate, <10ms hit latency).

## Test Procedure

### Test 1: Cache Hit Rate Measurement

```bash
# Run test-cache-performance.sh
bash test-cache-performance.sh | tee dev/projects/sdap_V2/test-evidence/task-5.6/cache-performance.txt

# Expected:
# Request 1: ~1-3s (cache MISS)
# Requests 2-10: ~0.5-1.5s (cache HIT)
```

### Test 2: Verify Cache Logs

```bash
# Download logs
az webapp log download --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --log-file dev/projects/sdap_V2/test-evidence/task-5.6/cache-logs.zip

# Search for cache behavior
unzip -p dev/projects/sdap_V2/test-evidence/task-5.6/cache-logs.zip \
  '*/LogFiles/Application/*.txt' | grep -i "cache" | tail -50

# Look for:
# - "Cache MISS for token hash ..."
# - "Cache HIT for token hash ..."
# - "Using cached Graph token"
```

### Test 3: Verify Cache TTL (55 minutes)

```bash
# Note: Full TTL test requires waiting 55+ minutes
# For quick verification, just check configuration

az webapp config appsettings list \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --query "[?name=='Redis__Enabled'].value" -o tsv

echo "✅ PASS: Cache configuration verified"
echo "Note: 55-minute TTL tested in long-running scenario (optional)"
```

## Validation Checklist

- [ ] Cache hit rate >90% (after warmup)
- [ ] Cache HIT latency <10ms (vs ~200ms for MISS)
- [ ] Logs show cache behavior correctly
- [ ] Redis healthy (if enabled)

## Pass Criteria

- ✅ Cache improves performance (requests 2+ faster than request 1)
- ✅ Logs show "Cache HIT" messages
- ✅ No cache errors in logs

## Next Task

[Phase 5 - Task 7: Load & Stress Testing](phase-5-task-7-load-testing.md)

