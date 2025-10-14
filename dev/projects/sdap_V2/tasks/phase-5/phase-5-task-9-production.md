# Phase 5 - Task 9: Production Environment Validation

**Phase**: 5 (Integration Testing)
**Duration**: 2-3 hours
**Risk**: CRITICAL (final gate before user release)
**Layers Tested**: All layers in production environment
**Prerequisites**: Tasks 5.1-5.8 complete in DEV

---

## Goal

**Validate the complete system in PRODUCTION environment** before releasing to users.

This is the **final gate** - if this task fails, do NOT release to users.

## Pre-Production Checklist

### 1. Redis Cache MUST Be Enabled

```bash
# CRITICAL: Production MUST use Redis, not in-memory cache
az webapp config appsettings list \
  --name spe-api-prod-<ID> \
  --resource-group spe-infrastructure-prod \
  --query "[?name=='Redis__Enabled'].value" -o tsv

# Expected: true (NOT false!)
# If false, STOP - this is a CRITICAL blocker

# Verify Redis connection string exists
az webapp config appsettings list \
  --name spe-api-prod-<ID> \
  --resource-group spe-infrastructure-prod \
  --query "[?name=='Redis__ConnectionString'].value" -o tsv

# Should return connection string (not empty)
```

**Why This Matters**:
> SDAP v1 used in-memory cache in production, which broke when scaling out to multiple instances.
> Each instance had its own cache, causing inconsistent behavior.

### 2. Application Insights Configured

```bash
# Verify Application Insights instrumentation key
az webapp config appsettings list \
  --name spe-api-prod-<ID> \
  --resource-group spe-infrastructure-prod \
  --query "[?name=='APPLICATIONINSIGHTS_CONNECTION_STRING'].value" -o tsv

# Should return connection string
```

### 3. Azure AD Production Configuration

```bash
# Verify production app registration is correct
az webapp config appsettings list \
  --name spe-api-prod-<ID> \
  --resource-group spe-infrastructure-prod \
  --query "[?starts_with(name, 'Graph__')].{name:name, value:value}" -o table

# Verify:
# - Graph__ClientId matches production app registration
# - Graph__TenantId is correct
# - Graph__KeyVaultUrl is production Key Vault
```

### 4. Key Vault Access Verified

```bash
# Test Key Vault access from production App Service
az keyvault secret show \
  --vault-name <prod-keyvault-name> \
  --name spe-app-cert \
  --query "value" -o tsv

# If this fails, App Service Managed Identity doesn't have access
```

## Test Procedure

### Test 1: Production Health Check

```bash
# Check health endpoint
HEALTH_RESPONSE=$(curl -s https://spe-api-prod-<ID>.azurewebsites.net/api/health)

echo "$HEALTH_RESPONSE" | jq .

# Expected:
# {
#   "status": "Healthy",
#   "redis": "Connected",
#   "keyVault": "Accessible",
#   "graph": "Authenticated"
# }

# Verify each component
REDIS_STATUS=$(echo "$HEALTH_RESPONSE" | jq -r '.redis')
if [ "$REDIS_STATUS" != "Connected" ]; then
  echo "❌ CRITICAL: Redis not connected in production"
  exit 1
fi

echo "✅ PASS: Production health check"
```

### Test 2: Production Authentication Flow

```bash
# Get production token (delegated auth)
PROD_TOKEN=$(az account get-access-token \
  --resource api://<prod-bff-app-id> \
  --query accessToken -o tsv)

# Test /me endpoint
curl -s -H "Authorization: Bearer $PROD_TOKEN" \
  https://spe-api-prod-<ID>.azurewebsites.net/api/me | jq .

# Expected: User profile with production UPN

# Test OBO exchange performance (should use cache)
echo "Testing cache performance in production..."
time curl -s -H "Authorization: Bearer $PROD_TOKEN" \
  https://spe-api-prod-<ID>.azurewebsites.net/api/me > /dev/null

# First request: ~1-3s (cache MISS)
time curl -s -H "Authorization: Bearer $PROD_TOKEN" \
  https://spe-api-prod-<ID>.azurewebsites.net/api/me > /dev/null

# Second request: <1s (cache HIT via Redis)

echo "✅ PASS: Production authentication"
```

### Test 3: Production File Operations (Test Container Only!)

**WARNING**: Only test in a designated TEST container. Do NOT test in real client containers.

```bash
# Use a production TEST Matter (pre-configured)
PROD_TEST_DRIVE_ID="<production-test-drive-id>"

# Upload test file
FILE_NAME="prod-validation-$(date +%s).txt"
echo "Production validation test" > /tmp/prod-test.txt

UPLOAD_RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}" \
  -X PUT \
  -H "Authorization: Bearer $PROD_TOKEN" \
  --data-binary @/tmp/prod-test.txt \
  "https://spe-api-prod-<ID>.azurewebsites.net/api/obo/drives/$PROD_TEST_DRIVE_ID/upload?fileName=$FILE_NAME")

HTTP_STATUS=$(echo "$UPLOAD_RESPONSE" | grep "HTTP_STATUS:" | cut -d':' -f2)
BODY=$(echo "$UPLOAD_RESPONSE" | grep -v "HTTP_STATUS:")

if [ "$HTTP_STATUS" == "200" ] || [ "$HTTP_STATUS" == "201" ]; then
  echo "✅ PASS: Production upload successful"
  ITEM_ID=$(echo "$BODY" | jq -r '.id')
else
  echo "❌ FAIL: Production upload failed (HTTP $HTTP_STATUS)"
  exit 1
fi

# Download and verify
curl -s -H "Authorization: Bearer $PROD_TOKEN" \
  -o /tmp/prod-download.txt \
  "https://spe-api-prod-<ID>.azurewebsites.net/api/obo/drives/$PROD_TEST_DRIVE_ID/items/$ITEM_ID/content"

if diff /tmp/prod-test.txt /tmp/prod-download.txt; then
  echo "✅ PASS: Production download and content integrity verified"
else
  echo "❌ FAIL: Content mismatch"
  exit 1
fi

# Delete test file
curl -s -X DELETE \
  -H "Authorization: Bearer $PROD_TOKEN" \
  "https://spe-api-prod-<ID>.azurewebsites.net/api/obo/drives/$PROD_TEST_DRIVE_ID/items/$ITEM_ID"

echo "✅ PASS: Production file operations complete"
```

### Test 4: Application Insights Telemetry

```bash
# Verify telemetry is being logged
az monitor app-insights query \
  --app <prod-app-insights-name> \
  --resource-group spe-infrastructure-prod \
  --analytics-query "requests | where timestamp > ago(5m) | summarize count() by resultCode" \
  --offset 5m

# Expected: Should show recent requests (200, 401, etc.)

echo "✅ PASS: Application Insights telemetry active"
```

### Test 5: Performance Baseline

```bash
# Establish production performance baseline
echo "Measuring production performance..."

# 10 sequential requests to measure average latency
TOTAL_TIME=0
for i in {1..10}; do
  START=$(date +%s%3N)
  curl -s -H "Authorization: Bearer $PROD_TOKEN" \
    https://spe-api-prod-<ID>.azurewebsites.net/api/me > /dev/null
  END=$(date +%s%3N)
  ELAPSED=$((END - START))
  TOTAL_TIME=$((TOTAL_TIME + ELAPSED))
  echo "Request $i: ${ELAPSED}ms"
done

AVG_LATENCY=$((TOTAL_TIME / 10))
echo "Average latency: ${AVG_LATENCY}ms"

# Save baseline for future comparison
echo "$AVG_LATENCY" > dev/projects/sdap_V2/test-evidence/task-5.9/production-baseline-latency.txt

if [ $AVG_LATENCY -lt 2000 ]; then
  echo "✅ PASS: Production performance acceptable (<2s)"
else
  echo "⚠️  WARNING: Production performance slow (${AVG_LATENCY}ms)"
fi
```

## Rollback Plan

**If production validation fails, follow this rollback procedure:**

### Immediate Rollback (Emergency)

```bash
# Option 1: Revert to previous deployment slot
az webapp deployment slot swap \
  --name spe-api-prod-<ID> \
  --resource-group spe-infrastructure-prod \
  --slot staging \
  --action swap

# Option 2: Redeploy previous version
PREVIOUS_COMMIT="<commit-hash-of-last-working-version>"
git checkout $PREVIOUS_COMMIT
# Build and deploy (follow deployment procedure)

# Option 3: Disable new features via feature flags (if implemented)
az webapp config appsettings set \
  --name spe-api-prod-<ID> \
  --resource-group spe-infrastructure-prod \
  --settings "FeatureFlags__SDAP_V2=false"
```

### Post-Rollback Actions

1. **Notify stakeholders** (email, Teams, etc.)
2. **Document the failure** in incident report
3. **Preserve logs** for analysis:
   ```bash
   az webapp log download \
     --name spe-api-prod-<ID> \
     --resource-group spe-infrastructure-prod \
     --log-file incident-logs-$(date +%Y%m%d-%H%M%S).zip
   ```
4. **Analyze root cause** before attempting redeployment
5. **Update test procedures** to catch the issue earlier

## Validation Checklist

### Pre-Production
- [ ] Redis cache enabled (NOT in-memory)
- [ ] Application Insights configured
- [ ] Production Azure AD configuration verified
- [ ] Key Vault access confirmed
- [ ] Deployment slot strategy ready (for rollback)

### Production Testing
- [ ] Health check passes (all components green)
- [ ] Authentication flow works
- [ ] File upload successful
- [ ] File download successful with content integrity
- [ ] Cache performance acceptable (<1s for cached requests)
- [ ] Application Insights receiving telemetry
- [ ] Performance baseline established

### Documentation
- [ ] Performance baseline saved
- [ ] Test evidence collected (screenshots, logs, metrics)
- [ ] Rollback plan documented and accessible
- [ ] Stakeholders notified of go-live

## Pass Criteria

- ✅ All production tests pass
- ✅ Redis cache working (not in-memory)
- ✅ Performance meets baseline (<2s average)
- ✅ No errors in Application Insights
- ✅ Rollback plan tested and ready

## Production Sign-Off

**Before releasing to users, obtain sign-off from:**

- [ ] Technical Lead (validates all tests passed)
- [ ] Product Owner (confirms readiness for user release)
- [ ] DevOps (confirms monitoring and rollback ready)

**Sign-off statement**:
> "I confirm that Phase 5 Task 9 (Production Validation) has passed all criteria.
> The system is ready for user release. Rollback plan is in place."

**Date**: _______________
**Signed**: _______________

---

## Next Phase

**Phase 6: User Acceptance Testing (UAT)**

[Phase 6 - Overview](../../phase-6/PHASE-6-OVERVIEW.md)

Or if Phase 6 not yet created:

**Production Release**:
1. Enable production environment for pilot users
2. Monitor Application Insights for first 24 hours
3. Collect user feedback
4. Address any issues discovered
5. Gradual rollout to all users

## Evidence Collection

Save all evidence to: `dev/projects/sdap_V2/test-evidence/task-5.9/`

Required files:
- `production-health-check.json` - Health endpoint response
- `production-auth-test.txt` - Authentication test output
- `production-file-operations.txt` - Upload/download test output
- `production-baseline-latency.txt` - Performance baseline
- `application-insights-screenshot.png` - Telemetry verification
- `production-sign-off.md` - Sign-off documentation

## Notes

**Historical Context**:
> SDAP v1 was released to production without comprehensive testing.
> Issues discovered included:
> - In-memory cache caused inconsistent behavior across instances
> - Permission errors not caught until users reported them
> - Performance degradation not detected (no baseline)
> - Silent failures in file storage

**This task prevents those issues by validating EVERYTHING in production before user release.**
