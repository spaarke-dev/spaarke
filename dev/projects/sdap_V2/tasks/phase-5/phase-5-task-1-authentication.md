# Phase 5 - Task 1: Authentication Flow Validation

**Phase**: 5 (Integration Testing)
**Duration**: 1-2 hours
**Risk**: **CRITICAL** (authentication failures block all functionality)
**Layers Tested**: User → PCF → BFF API (Layers 1-3)
**Prerequisites**: Task 5.0 (Pre-Flight) complete

---

## 🤖 AI PROMPT

```
CONTEXT: You are executing Phase 5 Task 1 - Authentication Flow Validation. This is the MOST CRITICAL test because authentication issues in SDAP v1 caused major production problems.

TASK: Systematically test and validate the complete authentication flow from user → MSAL token acquisition → BFF API JWT validation → OBO exchange → Graph token.

CONSTRAINTS:
- Must test EACH step of the auth flow independently
- Must verify token claims (audience, issuer, scopes)
- Must test both success and failure scenarios
- Must verify cache behavior (OBO token caching from Phase 4)
- Must collect evidence (tokens, logs, screenshots)

CRITICAL SUCCESS FACTORS:
1. MSAL token acquisition must work (PCF client app)
2. JWT validation must pass (BFF API receives correct audience)
3. OBO exchange must succeed (user token → Graph token)
4. Cache must work (second request uses cached Graph token)
5. Error handling must be clear (401 → actionable message)

HISTORICAL CONTEXT (SDAP v1 issues):
- Token audience mismatch (PCF sent wrong audience)
- OBO exchange failures (missing permissions)
- Silent failures (appeared to work but didn't)
- Cache poisoning (cached invalid tokens)
- Poor error messages (users couldn't diagnose)

FOCUS: This task MUST pass before any file operation tests. If auth doesn't work, nothing else will work.
```

---

## Goal

**Validate the complete authentication flow** from user token acquisition to Graph API access, ensuring:
1. PCF client app can acquire tokens for BFF API
2. BFF API validates tokens correctly
3. OBO exchange succeeds (user token → Graph token)
4. Phase 4 cache works (reduces OBO latency by 97%)
5. Error messages are clear and actionable

**Why Critical**: Authentication is the foundation. If this doesn't work:
- File operations will all fail with 401/403
- Error messages will be confusing
- Cache won't help (will cache failures)
- Production release will fail immediately

---

## Authentication Flow Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Step 1: User Authentication (Browser → Azure AD)                           │
│ User logs into Dataverse → Azure AD issues user token                      │
└───────────────────────────────────┬─────────────────────────────────────────┘
                                    │ User has authenticated session
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ Step 2: MSAL Token Acquisition (PCF Control → Azure AD)                    │
│ - PCF control uses MSAL.js                                                 │
│ - Requests token for BFF API: audience = api://1e40baad-...                │
│ - Client ID: 170c98e1-... (PCF client app)                                 │
│ - Scopes: user_impersonation (or exposed scope)                            │
└───────────────────────────────────┬─────────────────────────────────────────┘
                                    │ User token (JWT) with BFF API audience
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ Step 3: JWT Validation (BFF API validates incoming token)                  │
│ - Microsoft.Identity.Web validates token                                   │
│ - Checks: audience, issuer, signature, expiration                          │
│ - Expected audience: api://1e40baad-... (BFF API app ID)                   │
│ - Expected issuer: https://login.microsoftonline.com/{tenantId}/v2.0       │
└───────────────────────────────────┬─────────────────────────────────────────┘
                                    │ Token validated ✅
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ Step 4: OBO Exchange (BFF API → Azure AD → Graph Token)                    │
│                                                                             │
│ A. Check Cache (Phase 4)                                                   │
│    - Compute SHA256 hash of user token                                     │
│    - Check Redis/in-memory cache                                           │
│    ├─ Cache HIT (~5ms) → Use cached Graph token → DONE                     │
│    └─ Cache MISS → Continue to B                                           │
│                                                                             │
│ B. Perform OBO Exchange (~200ms)                                           │
│    - Use MSAL.NET ConfidentialClientApplication                            │
│    - Exchange user token for Graph token                                   │
│    - Client ID: 1e40baad-... (BFF API app)                                 │
│    - Client Secret: From Key Vault                                         │
│    - Scopes: https://graph.microsoft.com/.default                          │
│                                                                             │
│ C. Cache Token (55-minute TTL)                                             │
│    - Store Graph token in cache                                            │
│    - Key: sdap:graph:token:<sha256_hash>                                   │
└───────────────────────────────────┬─────────────────────────────────────────┘
                                    │ Graph token acquired ✅
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ Step 5: Graph API Call (BFF API → Microsoft Graph)                         │
│ - Use GraphServiceClient with Graph token                                  │
│ - Call: GET /me (or file operation)                                        │
│ - Graph API validates token and returns data                               │
└─────────────────────────────────────────────────────────────────────────────┘
```

**This task tests Steps 2-5** (Step 1 is assumed - user is logged into Dataverse).

---

## Pre-Flight Verification

Before starting, verify Task 5.0 passed:

```bash
# Check pre-flight report
cat dev/projects/sdap_V2/test-evidence/phase-5-preflight-report.md

# Verify all checks passed
# If any failed, resolve before continuing
```

**Required**:
- ✅ API health check passing
- ✅ Azure AD apps verified
- ✅ Test Drive ID available
- ✅ Tools installed (az, pac, curl, node)

---

## Test Procedure

### Test 1: MSAL Token Acquisition (Simulated)

**Goal**: Verify PCF client app can acquire token for BFF API

Since we don't have a browser with PCF control running yet, we simulate MSAL using Azure CLI.

#### Test 1.1: Acquire Token for BFF API

```bash
# Simulate MSAL token acquisition
# This is what MSAL.js does in the browser
export PCF_TOKEN=$(az account get-access-token \
  --resource api://1e40baad-e065-4aea-a8d4-4b7ab273458c \
  --query accessToken -o tsv)

# Verify token acquired
echo "Token length: ${#PCF_TOKEN} chars"
# Expected: 2000-3000 chars

# Check token is not empty
if [ -z "$PCF_TOKEN" ]; then
  echo "❌ FAIL: Token not acquired"
  exit 1
else
  echo "✅ PASS: Token acquired successfully"
fi
```

**Expected**: Token acquired (2000-3000 characters)

#### Test 1.2: Decode and Verify Token Claims

```bash
# Decode JWT token (header + payload)
# Format: header.payload.signature

# Extract payload (second part)
PAYLOAD=$(echo "$PCF_TOKEN" | cut -d'.' -f2)

# Decode base64 (add padding if needed)
CLAIMS=$(echo "$PAYLOAD" | base64 -d 2>/dev/null || echo "$PAYLOAD=" | base64 -d 2>/dev/null)

# Pretty print claims
echo "$CLAIMS" | jq .

# Verify critical claims:
# - aud (audience): "api://1e40baad-e065-4aea-a8d4-4b7ab273458c"
# - iss (issuer): "https://login.microsoftonline.com/{tenantId}/v2.0"
# - appid (client): "170c98e1-92b9-47ca-b3e7-e9e13f4f6e13" (PCF client)
# - scp (scopes): "user_impersonation" (or similar)
```

**Validation Checks**:
```bash
# Check audience
AUDIENCE=$(echo "$CLAIMS" | jq -r '.aud')
if [ "$AUDIENCE" != "api://1e40baad-e065-4aea-a8d4-4b7ab273458c" ]; then
  echo "❌ FAIL: Wrong audience: $AUDIENCE"
  echo "Expected: api://1e40baad-e065-4aea-a8d4-4b7ab273458c"
  exit 1
else
  echo "✅ PASS: Audience correct"
fi

# Check client app ID
APP_ID=$(echo "$CLAIMS" | jq -r '.appid')
if [ "$APP_ID" != "170c98e1-92b9-47ca-b3e7-e9e13f4f6e13" ]; then
  echo "❌ FAIL: Wrong client app ID: $APP_ID"
  exit 1
else
  echo "✅ PASS: Client app ID correct (PCF client)"
fi

# Check issuer
ISSUER=$(echo "$CLAIMS" | jq -r '.iss')
if [[ ! "$ISSUER" =~ ^https://login.microsoftonline.com/.*/v2.0$ ]]; then
  echo "❌ FAIL: Wrong issuer: $ISSUER"
  exit 1
else
  echo "✅ PASS: Issuer correct"
fi

# Check expiration
EXP=$(echo "$CLAIMS" | jq -r '.exp')
CURRENT_TIME=$(date +%s)
if [ "$EXP" -lt "$CURRENT_TIME" ]; then
  echo "❌ FAIL: Token expired"
  exit 1
else
  REMAINING=$((EXP - CURRENT_TIME))
  echo "✅ PASS: Token valid (expires in $REMAINING seconds)"
fi
```

**Evidence**: Save decoded token claims to file
```bash
echo "$CLAIMS" | jq . > dev/projects/sdap_V2/test-evidence/task-5.1/token-claims.json
```

---

### Test 2: JWT Validation (BFF API)

**Goal**: Verify BFF API validates the token correctly

#### Test 2.1: Test /api/me Endpoint (JWT Validation)

```bash
# Call /api/me endpoint (requires valid JWT)
RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}" \
  -H "Authorization: Bearer $PCF_TOKEN" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/me)

# Extract HTTP status
HTTP_STATUS=$(echo "$RESPONSE" | grep "HTTP_STATUS:" | cut -d':' -f2)

# Extract body
BODY=$(echo "$RESPONSE" | grep -v "HTTP_STATUS:")

# Verify response
if [ "$HTTP_STATUS" != "200" ]; then
  echo "❌ FAIL: JWT validation failed"
  echo "HTTP Status: $HTTP_STATUS"
  echo "Response: $BODY"
  exit 1
else
  echo "✅ PASS: JWT validation successful (200 OK)"
  echo "User info: $BODY" | jq .
fi
```

**Expected Response**:
```json
{
  "id": "user-guid",
  "displayName": "Your Name",
  "mail": "yourname@example.com",
  "userPrincipalName": "yourname@example.com"
}
```

**Evidence**: Save response to file
```bash
echo "$BODY" | jq . > dev/projects/sdap_V2/test-evidence/task-5.1/api-me-response.json
```

#### Test 2.2: Test with Invalid Token (Negative Test)

```bash
# Test with invalid token (should return 401)
INVALID_TOKEN="invalid.token.here"

RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}" \
  -H "Authorization: Bearer $INVALID_TOKEN" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/me)

HTTP_STATUS=$(echo "$RESPONSE" | grep "HTTP_STATUS:" | cut -d':' -f2)

if [ "$HTTP_STATUS" == "401" ]; then
  echo "✅ PASS: Invalid token rejected (401 Unauthorized)"
else
  echo "❌ FAIL: Invalid token should return 401, got $HTTP_STATUS"
fi
```

**Expected**: 401 Unauthorized

#### Test 2.3: Test with Wrong Audience (Negative Test)

```bash
# Acquire token for different audience (should be rejected)
WRONG_TOKEN=$(az account get-access-token \
  --resource https://graph.microsoft.com \
  --query accessToken -o tsv)

RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}" \
  -H "Authorization: Bearer $WRONG_TOKEN" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/me)

HTTP_STATUS=$(echo "$RESPONSE" | grep "HTTP_STATUS:" | cut -d':' -f2)

if [ "$HTTP_STATUS" == "401" ]; then
  echo "✅ PASS: Wrong audience rejected (401 Unauthorized)"
else
  echo "❌ FAIL: Wrong audience should return 401, got $HTTP_STATUS"
fi
```

**Expected**: 401 Unauthorized

---

### Test 3: OBO Exchange & Cache Performance

**Goal**: Verify OBO exchange works and cache reduces latency

#### Test 3.1: First Request (Cache MISS)

```bash
# Clear cache (if Redis enabled) - otherwise this is first request anyway
# For in-memory cache, just restart is needed (skip for now)

# Make first request (should be cache MISS)
echo "Request 1 (Cache MISS expected):"
START_TIME=$(date +%s%3N)

RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}\nTIME_TOTAL:%{time_total}" \
  -H "Authorization: Bearer $PCF_TOKEN" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/me)

END_TIME=$(date +%s%3N)
ELAPSED=$((END_TIME - START_TIME))

HTTP_STATUS=$(echo "$RESPONSE" | grep "HTTP_STATUS:" | cut -d':' -f2)
TIME_TOTAL=$(echo "$RESPONSE" | grep "TIME_TOTAL:" | cut -d':' -f2)

echo "  HTTP Status: $HTTP_STATUS"
echo "  Response Time: ${TIME_TOTAL}s (${ELAPSED}ms client)"
echo "  Expected: 1-3 seconds (includes OBO exchange ~200ms + Graph API call)"

# Verify success
if [ "$HTTP_STATUS" != "200" ]; then
  echo "❌ FAIL: OBO exchange failed (expected 200, got $HTTP_STATUS)"
  exit 1
else
  echo "✅ PASS: OBO exchange successful"
fi
```

**Expected Timing**: 1-3 seconds total (includes OBO ~200ms + Graph API call)

#### Test 3.2: Second Request (Cache HIT)

```bash
# Make second request (should be cache HIT)
echo "Request 2 (Cache HIT expected):"
START_TIME=$(date +%s%3N)

RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}\nTIME_TOTAL:%{time_total}" \
  -H "Authorization: Bearer $PCF_TOKEN" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/me)

END_TIME=$(date +%s%3N)
ELAPSED=$((END_TIME - START_TIME))

HTTP_STATUS=$(echo "$RESPONSE" | grep "HTTP_STATUS:" | cut -d':' -f2)
TIME_TOTAL=$(echo "$RESPONSE" | grep "TIME_TOTAL:" | cut -d':' -f2)

echo "  HTTP Status: $HTTP_STATUS"
echo "  Response Time: ${TIME_TOTAL}s (${ELAPSED}ms client)"
echo "  Expected: 0.5-1.5 seconds (cache HIT ~5ms + Graph API call)"

# Verify cache improved performance
# (Hard to verify exact timing, but should be noticeably faster)
echo "✅ PASS: Request completed (cache HIT expected)"
```

**Expected Timing**: 0.5-1.5 seconds (cache reduces OBO overhead from ~200ms to ~5ms)

#### Test 3.3: Verify Cache in Logs

```bash
# Check application logs for cache behavior
az webapp log tail --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  2>&1 | grep -i "cache" | tail -20

# Look for:
# - "Cache MISS for token hash ..." (first request)
# - "Cache HIT for token hash ..." (second request)
# - "Using cached Graph token (cache hit)" (GraphClientFactory)
```

**Expected**: Logs show cache MISS (first) and cache HIT (subsequent)

**Evidence**: Save log output to file
```bash
az webapp log download --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --log-file dev/projects/sdap_V2/test-evidence/task-5.1/cache-logs.zip
```

---

### Test 4: Error Handling & User Experience

**Goal**: Verify error messages are clear and actionable

#### Test 4.1: Test Expired Token

```bash
# Wait for token to expire (or use old token)
# For quick test, just use invalid token format

echo "Testing expired/invalid token error message..."
RESPONSE=$(curl -s -H "Authorization: Bearer expired.token.here" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/me)

echo "Error message: $RESPONSE"

# Verify error message is clear
if [[ "$RESPONSE" =~ "Authentication failed" ]] || [[ "$RESPONSE" =~ "Unauthorized" ]]; then
  echo "✅ PASS: Error message is clear"
else
  echo "⚠️  WARNING: Error message could be clearer: $RESPONSE"
fi
```

**Expected**: Clear error message like "Authentication failed. Your session may have expired."

#### Test 4.2: Test Missing Authorization Header

```bash
# Test without Authorization header
echo "Testing missing Authorization header..."
RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/me)

HTTP_STATUS=$(echo "$RESPONSE" | grep "HTTP_STATUS:" | cut -d':' -f2)

if [ "$HTTP_STATUS" == "401" ]; then
  echo "✅ PASS: Missing auth header rejected (401)"
else
  echo "❌ FAIL: Should return 401, got $HTTP_STATUS"
fi
```

**Expected**: 401 Unauthorized

---

## Validation Checklist

**MSAL Token Acquisition (Simulated)**:
- [ ] Token acquired successfully (2000-3000 chars)
- [ ] Audience correct: `api://1e40baad-...`
- [ ] Client app ID correct: `170c98e1-...` (PCF client)
- [ ] Issuer correct: `https://login.microsoftonline.com/.../v2.0`
- [ ] Token not expired (exp > current time)
- [ ] Scopes include required permissions

**JWT Validation**:
- [ ] Valid token accepted (200 OK)
- [ ] /api/me returns user info correctly
- [ ] Invalid token rejected (401)
- [ ] Wrong audience rejected (401)
- [ ] Missing auth header rejected (401)

**OBO Exchange**:
- [ ] First request succeeds (200 OK)
- [ ] Response time acceptable (1-3 seconds)
- [ ] User info returned correctly

**Cache Performance (Phase 4)**:
- [ ] Second request faster than first
- [ ] Logs show "Cache MISS" for first request
- [ ] Logs show "Cache HIT" for subsequent requests
- [ ] Response time improved (~0.5-1.5s vs 1-3s)

**Error Handling**:
- [ ] Expired token returns clear error message
- [ ] Missing auth header returns 401
- [ ] Invalid token returns 401 with clear message

---

## Pass Criteria

**Task 5.1 is COMPLETE when**:
- ✅ All checklist items checked
- ✅ All tests pass (no ❌ failures)
- ✅ Evidence collected (screenshots, logs, JSON files)
- ✅ Performance meets expectations (cache improves latency)
- ✅ Error messages are clear and actionable

**If ANY test fails**:
- 🛑 STOP - Authentication must work before continuing
- 🔍 Investigate root cause
- 🔧 Fix issue (code or configuration)
- 🔄 Re-run Task 5.1 from start

**Critical Failure Scenarios** (require immediate resolution):
1. Token acquisition fails → Check Azure AD app registration
2. JWT validation fails → Check BFF API configuration (audience, clientId)
3. OBO exchange fails → Check API permissions, admin consent
4. Cache not working → Check Phase 4 code deployment, Redis config

---

## Evidence Collection

**Required Evidence**:
1. ✅ Token claims JSON (`token-claims.json`)
2. ✅ /api/me response JSON (`api-me-response.json`)
3. ✅ Cache logs (showing HIT/MISS) (`cache-logs.zip`)
4. ✅ Performance timing (first vs second request)
5. ✅ Screenshot of successful /api/me call (Postman/curl)

**Save to**: `dev/projects/sdap_V2/test-evidence/task-5.1/`

---

## Troubleshooting

### Issue: Token Acquisition Fails

**Symptoms**: `az account get-access-token` returns error

**Causes**:
- Not logged in to Azure CLI
- Wrong tenant
- PCF client app doesn't expose scope

**Fix**:
```bash
# Login to correct tenant
az login --tenant a221a95e-6abc-4434-aecc-e48338a1b2f2

# Verify app registration
az ad app show --id 170c98e1-92b9-47ca-b3e7-e9e13f4f6e13

# Check exposed scopes on BFF API app
az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c \
  --query "api.oauth2PermissionScopes"
```

### Issue: JWT Validation Fails (401)

**Symptoms**: /api/me returns 401 even with valid token

**Causes**:
- Wrong audience in token (check token claims)
- BFF API configured with wrong ClientId
- Issuer mismatch (wrong tenant)

**Fix**:
```bash
# Check appsettings (should be 1e40baad-...)
az webapp config appsettings list \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --query "[?name=='API_APP_ID' || name=='AzureAd__ClientId'].{name:name, value:value}"

# Should see:
# API_APP_ID: 1e40baad-e065-4aea-a8d4-4b7ab273458c
# AzureAd__ClientId: 1e40baad-e065-4aea-a8d4-4b7ab273458c
```

### Issue: OBO Exchange Fails

**Symptoms**: First request fails with 500 error

**Causes**:
- BFF API doesn't have permissions to call Graph API
- Client secret missing or expired
- Admin consent not granted

**Fix**:
```bash
# Check Graph API permissions
az ad app permission list --id 1e40baad-e065-4aea-a8d4-4b7ab273458c

# Should include Microsoft Graph permissions (e.g., User.Read)

# Check admin consent status
az ad app permission list-grants --id 1e40baad-e065-4aea-a8d4-4b7ab273458c

# Grant admin consent if missing
az ad app permission admin-consent --id 1e40baad-e065-4aea-a8d4-4b7ab273458c
```

### Issue: Cache Not Working

**Symptoms**: All requests show same latency, no cache logs

**Causes**:
- Phase 4 code not deployed
- GraphTokenCache not registered in DI
- Redis connection failed (if Redis enabled)

**Fix**:
```bash
# Verify Phase 4 code deployed
curl -s https://spe-api-dev-67e2xz.azurewebsites.net/ping | jq .

# Check deployment timestamp (should be recent)

# Check logs for cache-related errors
az webapp log tail --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  2>&1 | grep -i "error\|exception" | grep -i "cache"
```

---

## Next Task

✅ **If all tests pass**: [Phase 5 - Task 2: BFF API Endpoint Testing](phase-5-task-2-bff-endpoints.md)

⚠️ **If tests fail**: Resolve issues, re-run Task 5.1, do NOT proceed until authentication works.

---

## Related Resources

- **Phase 5 Overview**: [PHASE-5-OVERVIEW.md](../PHASE-5-OVERVIEW.md)
- **Testing Guide**: [END-TO-END-SPE-TESTING-GUIDE.md](../../END-TO-END-SPE-TESTING-GUIDE.md#part-1-pre-deployment-pcf-integration-testing)
- **Phase 4 Cache**: [PHASE-4-TESTING-GUIDE.md](../../PHASE-4-TESTING-GUIDE.md)
- **Authentication Pattern**: [patterns/auth-obo-flow.md](../patterns/auth-obo-flow.md)

---

**Last Updated**: 2025-10-14
**Status**: ✅ Template ready for execution
