# Phase 5 - Task 7: Load & Stress Testing

**Phase**: 5 (Integration Testing)
**Duration**: 2-3 hours
**Risk**: MEDIUM (identifies scale issues)
**Layers Tested**: All layers under load
**Prerequisites**: Tasks 5.1-5.6 complete

---

## Goal

**Test system performance under concurrent load**.

Tests:
- Concurrent uploads (10+ users)
- Large file uploads (>100MB)
- Sustained load (5+ minutes)
- Memory/CPU usage monitoring

## Test Procedure

### Test 1: Concurrent Uploads (10 users)

```bash
# Create concurrent upload test script
cat > test-concurrent-uploads.sh << 'EOF'
#!/bin/bash
for i in {1..10}; do
  (
    FILE_NAME="concurrent-test-${i}-$(date +%s).txt"
    echo "User $i uploading..." > /tmp/concurrent-$i.txt

    curl -s -X PUT \
      -H "Authorization: Bearer $PCF_TOKEN" \
      --data-binary @/tmp/concurrent-$i.txt \
      "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$DRIVE_ID/upload?fileName=$FILE_NAME"

    echo "User $i completed"
  ) &
done

wait
echo "All concurrent uploads completed"
EOF

chmod +x test-concurrent-uploads.sh
time ./test-concurrent-uploads.sh

# Expected: All complete in <30 seconds
```

### Test 2: Large File Upload (>100MB)

```bash
# Create large file (~100MB)
dd if=/dev/urandom of=/tmp/large-file-100mb.bin bs=1M count=100 2>/dev/null

FILE_NAME="large-file-$(date +%s).bin"

echo "Uploading 100MB file..."
START_TIME=$(date +%s)

curl -s -w "\nHTTP_STATUS:%{http_code}\nTIME_TOTAL:%{time_total}" \
  --max-time 300 \
  -X PUT \
  -H "Authorization: Bearer $PCF_TOKEN" \
  -H "Content-Type: application/octet-stream" \
  --data-binary @/tmp/large-file-100mb.bin \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$DRIVE_ID/upload?fileName=$FILE_NAME"

END_TIME=$(date +%s)
ELAPSED=$((END_TIME - START_TIME))

echo "Upload completed in ${ELAPSED}s"

# Expected: <2 minutes for 100MB
if [ $ELAPSED -lt 120 ]; then
  echo "✅ PASS: Large file upload performance acceptable"
else
  echo "⚠️  WARNING: Slow upload (${ELAPSED}s)"
fi

# Note: For files >250MB, UploadSessionManager should be used automatically
```

### Test 3: Sustained Load (5 minutes)

```bash
# Continuous requests for 5 minutes
cat > test-sustained-load.sh << 'EOF'
#!/bin/bash
END_TIME=$(($(date +%s) + 300))  # 5 minutes from now
REQUEST_COUNT=0

while [ $(date +%s) -lt $END_TIME ]; do
  curl -s -o /dev/null \
    -H "Authorization: Bearer $PCF_TOKEN" \
    "https://spe-api-dev-67e2xz.azurewebsites.net/api/me"

  REQUEST_COUNT=$((REQUEST_COUNT + 1))
  sleep 0.5
done

echo "Completed $REQUEST_COUNT requests in 5 minutes"
echo "Average: $(echo "scale=2; $REQUEST_COUNT / 5" | bc) requests/minute"
EOF

chmod +x test-sustained-load.sh
./test-sustained-load.sh

# Expected: No errors, stable memory usage
```

### Test 4: Monitor Resource Usage

```bash
# Check application metrics during load
az monitor metrics list \
  --resource /subscriptions/.../resourceGroups/spe-infrastructure-westus2/providers/Microsoft.Web/sites/spe-api-dev-67e2xz \
  --metric "MemoryWorkingSet,CpuPercentage" \
  --start-time $(date -u -d '5 minutes ago' +%Y-%m-%dT%H:%M:%SZ) \
  --end-time $(date -u +%Y-%m-%dT%H:%M:%SZ) \
  --interval PT1M \
  --aggregation Average

echo "✅ PASS: Resource monitoring complete"
```

## Validation Checklist

- [ ] 10 concurrent users supported
- [ ] Large files (>100MB) upload successfully
- [ ] Sustained load (5 min) no errors
- [ ] Memory usage stable (<80%)
- [ ] CPU usage acceptable (<70% sustained)

## Pass Criteria

- ✅ All concurrent uploads complete successfully
- ✅ Large file uploads work
- ✅ No errors during sustained load
- ✅ Resource usage stable

## Next Task

[Phase 5 - Task 8: Error Handling & Failure Scenarios](phase-5-task-8-error-handling.md)

