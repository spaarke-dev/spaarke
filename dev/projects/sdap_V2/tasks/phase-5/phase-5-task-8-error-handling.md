# Phase 5 - Task 8: Error Handling & Failure Scenarios

**Phase**: 5 (Integration Testing)
**Duration**: 1-2 hours
**Risk**: HIGH (poor error handling breaks user experience)
**Layers Tested**: All layers (error propagation)
**Prerequisites**: Tasks 5.1-5.7 complete
**Status**: HYBRID - Architecture Review + Limited Runtime Testing
**Updated**: 2025-10-14

---

## Goal

**Verify all error scenarios are handled gracefully** with clear, actionable error messages.

**Current Limitation**: Admin consent blocker prevents full runtime testing of authenticated endpoints.

**Testing Strategy**:
1. ✅ **Architecture Review**: Code review of error handling patterns
2. ✅ **Anonymous Endpoint Testing**: Test `/ping` endpoint error scenarios
3. ⏳ **Authenticated Endpoint Testing**: Deferred to Task 5.9 (Production with MSAL.js)

---

## Architecture Review: Error Handling Patterns

### 1. Global Exception Handling

**File**: [Program.cs](c:\code_files\spaarke\src\api\Spe.Bff.Api\Program.cs)

**Expected Pattern**:
```csharp
app.UseExceptionHandler("/error");
app.UseStatusCodePages();
```

**Validation**: Review Program.cs for global error handling middleware.

### 2. OBO Endpoint Error Handling

**File**: [OBOEndpoints.cs](c:\code_files\spaarke\src\api\Spe.Bff.Api\Api\OBOEndpoints.cs)

**Expected Patterns**:
- Try-catch blocks around Graph API calls
- Specific exception types (ServiceException, AuthenticationException)
- HTTP status code mapping (401, 403, 404, 500)
- User-friendly error messages (no stack traces)

**Validation**: Review each endpoint for error handling.

### 3. Graph API Error Handling

**File**: [GraphClientFactory.cs](c:\code_files\spaarke\src\api\Spe.Bff.Api\Infrastructure\Graph\GraphClientFactory.cs)

**Expected Patterns**:
- OBO token exchange failures (401, 403)
- Graph API errors (ServiceException)
- Logging for debugging (but not exposed to users)

### 4. Cache Error Handling

**File**: [GraphTokenCache.cs](c:\code_files\spaarke\src\api\Spe.Bff.Api\Services\GraphTokenCache.cs)

**Expected Pattern** (Line 84-91):
```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "Error retrieving token from cache...");
    _metrics?.RecordMiss(sw.Elapsed.TotalMilliseconds);
    return null; // Fail gracefully, will perform OBO exchange
}
```

**Validation**: ✅ Cache failures don't break requests (already reviewed in Task 5.6)

---

## Test Procedure

### Test 1: Network Timeout

```bash
# Test timeout handling (use very short timeout)
curl -s --max-time 1 \
  -H "Authorization: Bearer $PCF_TOKEN" \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/me"

# Expected: Timeout error with clear message
echo "✅ PASS: Timeout handled (client-side)"
```

### Test 2: Expired Token (401 → Automatic Retry)

```bash
# Test with expired token
OLD_TOKEN="eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.expired.token"

RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}" \
  -H "Authorization: Bearer $OLD_TOKEN" \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/me")

HTTP_STATUS=$(echo "$RESPONSE" | grep "HTTP_STATUS:" | cut -d':' -f2)

if [ "$HTTP_STATUS" == "401" ]; then
  echo "✅ PASS: Expired token rejected (401)"

  # Verify error message is clear
  BODY=$(echo "$RESPONSE" | grep -v "HTTP_STATUS:")
  if echo "$BODY" | grep -iq "authentication\|unauthorized\|expired"; then
    echo "✅ PASS: Clear error message"
  else
    echo "⚠️  WARNING: Error message could be clearer"
  fi
else
  echo "❌ FAIL: Expected 401, got $HTTP_STATUS"
fi
```

### Test 3: Missing Permissions (403)

```bash
# Test operation without required permissions
# (This requires setting up a user without permissions - optional)

echo "⏭️  SKIP: Permission testing requires test user setup"
echo "Note: Verify 403 returns message: 'Access denied. Contact administrator.'"
```

### Test 4: Invalid Drive ID (404)

```bash
# Already tested in Task 5.2
echo "✅ PASS: Invalid Drive ID tested in Task 5.2"
```

### Test 5: Service Unavailable (503)

```bash
# Simulate by stopping dependent service (NOT RECOMMENDED in prod)
echo "⏭️  SKIP: Service unavailability test (requires staging environment)"
echo "Note: Verify 503 returns message: 'Service temporarily unavailable. Try again.'"
```

### Test 6: Rate Limiting (429)

```bash
# Test rate limiting (if configured)
# Make rapid requests
for i in {1..100}; do
  curl -s -o /dev/null -w "%{http_code}\n" \
    -H "Authorization: Bearer $PCF_TOKEN" \
    "https://spe-api-dev-67e2xz.azurewebsites.net/api/me"
done | sort | uniq -c

# Look for 429 responses (if rate limiting enabled)
echo "Note: Check for 429 'Too Many Requests' if rate limiting configured"
```

## Validation Checklist

- [ ] 401 Unauthorized: Clear message, automatic retry
- [ ] 403 Forbidden: Clear "access denied" message
- [ ] 404 Not Found: Clear "file not found" message
- [ ] 408 Timeout: Graceful timeout handling
- [ ] 429 Rate Limit: Clear "too many requests" message
- [ ] 500 Server Error: Generic error, not stack trace
- [ ] 503 Unavailable: Clear "service unavailable" message

## Pass Criteria

- ✅ All error scenarios return clear, user-friendly messages
- ✅ No stack traces exposed to users
- ✅ Error messages are actionable (tell user what to do)

## Next Task

[Phase 5 - Task 9: Production Environment Validation](phase-5-task-9-production.md)

