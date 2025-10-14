# Phase 5 - Task 0: Pre-Flight Environment Verification

**Phase**: 5 (Integration Testing)
**Duration**: 30-45 minutes
**Risk**: Critical (blocks all subsequent testing)
**Layers Tested**: Infrastructure & Configuration
**Prerequisites**: Phases 1-4 complete, BFF API deployed

---

## 🤖 AI PROMPT

```
CONTEXT: You are starting Phase 5 - End-to-End Integration Testing. This is Task 0, the pre-flight verification that ensures the environment is ready for testing.

TASK: Verify all infrastructure, configuration, and dependencies are correctly set up before starting integration tests.

CONSTRAINTS:
- Must verify ALL components (BFF API, Azure AD, Dataverse, SPE)
- Must collect baseline metrics for comparison
- Must document current configuration for troubleshooting
- If ANY verification fails, STOP and resolve before continuing to Task 5.1

VERIFICATION CATEGORIES:
1. Code Deployment (Phases 1-4 code is live)
2. Azure AD Configuration (apps, permissions, admin consent)
3. Environment Variables (all secrets configured)
4. Service Health (API running, health checks passing)
5. Tool Availability (pac, az, curl, node)

FOCUS: This is NOT a test of functionality - it's a test of readiness to test. Think of it as "checking the runway before takeoff."
```

---

## Goal

Verify that the testing environment is properly configured and all dependencies are available before starting integration tests.

**Why Critical**: Starting tests without proper verification leads to:
- False failures (test fails due to config, not code)
- Wasted time (debugging wrong layer)
- Missed issues (assuming config is correct when it's not)
- Incomplete tests (missing tools or access)

**Success Definition**: Every check passes, baseline metrics collected, environment documented.

---

## Pre-Flight Checks

### Category 1: Code Deployment Verification

Verify that all Phase 1-4 code changes are deployed to the test environment.

#### Check 1.1: Verify Latest Commit Deployed

```bash
# Check local git commit
LOCAL_COMMIT=$(git rev-parse HEAD)
echo "Local HEAD: $LOCAL_COMMIT"

# Check what's deployed (from deployment endpoint)
curl -s https://spe-api-dev-67e2xz.azurewebsites.net/ping | jq -r '.version'

# Expected: Version matches or is recent (within last day)
```

**Pass Criteria**: Deployment timestamp within last 24 hours (or matches expected deployment)

#### Check 1.2: Verify Phase 4 Cache Code Deployed

```bash
# Check if CacheMetrics and GraphTokenCache are deployed
# by testing /healthz endpoint (should show cache status)

curl -s https://spe-api-dev-67e2xz.azurewebsites.net/healthz

# Expected: "Healthy" response (200 OK)
```

**Pass Criteria**: Health check returns 200 OK

#### Check 1.3: Verify Configuration Files

```bash
# Download current appsettings (from Kudu/SCM)
az webapp config appsettings list \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --query "[].{name:name, value:value}" \
  -o table | head -20

# Check for required settings:
# - Redis__Enabled (true or false - both valid)
# - API_APP_ID (should be 1e40baad-...)
```

**Pass Criteria**: All required app settings present

---

### Category 2: Azure AD Configuration

Verify Azure AD app registrations and permissions.

#### Check 2.1: BFF API App Registration

```bash
# Verify BFF API app exists and has correct configuration
az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c \
  --query "{displayName:displayName, appId:appId, signInAudience:signInAudience}" \
  -o json

# Expected:
# - displayName: "SPE BFF API" (or similar)
# - appId: "1e40baad-e065-4aea-a8d4-4b7ab273458c"
# - signInAudience: "AzureADMyOrg" or "AzureADMultipleOrgs"
```

**Pass Criteria**: App exists with correct ID

#### Check 2.2: PCF Client App Registration

```bash
# Verify PCF client app exists
az ad app show --id 170c98e1-92b9-47ca-b3e7-e9e13f4f6e13 \
  --query "{displayName:displayName, appId:appId}" \
  -o json

# Expected:
# - displayName: "SPE PCF Client" (or similar)
# - appId: "170c98e1-92b9-47ca-b3e7-e9e13f4f6e13"
```

**Pass Criteria**: App exists with correct ID

#### Check 2.3: API Permissions & Admin Consent

```bash
# Check PCF client app permissions to BFF API
az ad app permission list --id 170c98e1-92b9-47ca-b3e7-e9e13f4f6e13 -o json

# Look for:
# - resourceAppId: "1e40baad-e065-4aea-a8d4-4b7ab273458c" (BFF API)
# - Permission granted (admin consent)
```

**Pass Criteria**: PCF client app has permissions to BFF API, admin consent granted

#### Check 2.4: BFF API Exposed Scopes

```bash
# Verify BFF API exposes required scopes
az ad app show --id 1e40baad-e065-4aea-a8d4-4b7ab273458c \
  --query "api.oauth2PermissionScopes[].{value:value, adminConsentDisplayName:adminConsentDisplayName}" \
  -o table

# Expected: At least one scope (e.g., "user_impersonation")
```

**Pass Criteria**: At least one scope exposed (e.g., `user_impersonation`)

---

### Category 3: Environment Variables & Secrets

Verify all required environment variables and Key Vault secrets are configured.

#### Check 3.1: Azure Web App Settings

```bash
# List all app settings (sensitive values masked)
az webapp config appsettings list \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --query "[].{name:name}" \
  -o table

# Required settings (minimum):
# - API_APP_ID
# - AzureAd__ClientId
# - AzureAd__TenantId
# - Dataverse__ServiceUrl (or from Key Vault)
# - Graph__ClientId
# - Graph__TenantId
# - Redis__Enabled
```

**Pass Criteria**: All required settings present (values can be from Key Vault)

#### Check 3.2: Key Vault Access

```bash
# Verify Web App has Managed Identity with Key Vault access
az webapp identity show \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --query "principalId" \
  -o tsv

# Get the principal ID, then check Key Vault access
PRINCIPAL_ID=$(az webapp identity show \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --query "principalId" \
  -o tsv)

# Check if principal has access to Key Vault
az keyvault show --name spaarke-spekvcert \
  --query "properties.accessPolicies[?objectId=='$PRINCIPAL_ID'].permissions" \
  -o json

# Expected: Web App has "get" and "list" permissions on secrets
```

**Pass Criteria**: Web App has managed identity with Key Vault access

#### Check 3.3: Connection Strings

```bash
# Check connection strings (Redis, Dataverse)
az webapp config connection-string list \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --query "{Redis:Redis, Dataverse:Dataverse}" \
  -o json

# Expected: Connection strings present (or from Key Vault)
```

**Pass Criteria**: Connection strings configured (direct or Key Vault reference)

---

### Category 4: Service Health

Verify all services are running and healthy.

#### Check 4.1: BFF API Health

```bash
# Test basic health endpoint
curl -s -w "\nHTTP Status: %{http_code}\n" \
  https://spe-api-dev-67e2xz.azurewebsites.net/healthz

# Expected: "Healthy" + HTTP 200
```

**Pass Criteria**: Returns "Healthy" with HTTP 200

#### Check 4.2: Dataverse Connectivity

```bash
# Test Dataverse health check (if available)
curl -s -w "\nHTTP Status: %{http_code}\n" \
  https://spe-api-dev-67e2xz.azurewebsites.net/healthz/dataverse

# Expected: HTTP 200 (or endpoint not available = check manually)
```

**Pass Criteria**: Returns 200 (or manual verification via pac CLI)

#### Check 4.3: Redis Connectivity (if enabled)

```bash
# Check Redis health
az webapp config appsettings list \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --query "[?name=='Redis__Enabled'].value" \
  -o tsv

# If Redis__Enabled=true, verify connection
# (Check application logs for Redis connection errors)
az webapp log tail --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  2>&1 | grep -i "redis" | head -10

# Expected: No Redis connection errors
```

**Pass Criteria**: If Redis enabled, no connection errors in logs

#### Check 4.4: Application Logs

```bash
# Download recent logs
az webapp log download \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --log-file preflight-logs.zip

# Check for startup errors
unzip -p preflight-logs.zip */LogFiles/Application/*.txt 2>/dev/null | tail -100

# Expected: No ERROR or FATAL messages in recent logs
```

**Pass Criteria**: No critical errors in application logs

---

### Category 5: Tool Availability

Verify all required testing tools are installed and accessible.

#### Check 5.1: Azure CLI

```bash
az --version | head -1
# Expected: azure-cli 2.x.x or later
```

**Pass Criteria**: Azure CLI installed and authenticated

#### Check 5.2: Power Platform CLI

```bash
pac --version
# Expected: Microsoft PowerPlatform CLI Version: 1.x.x or later
```

**Pass Criteria**: PAC CLI installed

#### Check 5.3: Node.js (for PCF client test)

```bash
node --version
# Expected: v18.x.x or later
```

**Pass Criteria**: Node.js v18+ installed

#### Check 5.4: curl / wget

```bash
curl --version | head -1
# Expected: curl 7.x.x or later
```

**Pass Criteria**: curl installed and working

#### Check 5.5: jq (JSON processor)

```bash
jq --version
# Expected: jq-1.x or later (optional but recommended)
```

**Pass Criteria**: jq installed (optional, but helpful for JSON parsing)

---

### Category 6: Test Data Preparation

Verify test data is available (containers, Drive IDs).

#### Check 6.1: Get Test Container/Drive ID

```bash
# Authenticate to Dataverse
pac auth list

# Get a test matter with Drive ID
pac data read --entity-logical-name sprk_matter \
  --columns sprk_name,sprk_driveid \
  --filter "statecode eq 0" \
  --top 1

# Save Drive ID for testing
export DRIVE_ID="<drive-id-from-output>"
echo "Test Drive ID: $DRIVE_ID"
```

**Pass Criteria**: At least one active matter with Drive ID exists

#### Check 6.2: Verify Container Type ID

```bash
# Check environment variable or config
echo "Container Type ID: 8a6ce34c-6055-4681-8f87-2f4f9f921c06"

# Verify this is correct for your environment
# (Should match the SPE container type registered)
```

**Pass Criteria**: Container Type ID documented and available

---

## Baseline Metrics Collection

Before starting tests, collect baseline metrics for comparison.

### Metric 1: API Response Time (Ping)

```bash
# Measure ping endpoint latency (5 samples)
for i in {1..5}; do
  curl -s -w "Time: %{time_total}s\n" \
    -o /dev/null \
    https://spe-api-dev-67e2xz.azurewebsites.net/ping
done

# Expected: <1 second average
```

**Baseline**: Record average response time

### Metric 2: Health Check Response Time

```bash
# Measure health check latency (5 samples)
for i in {1..5}; do
  curl -s -w "Time: %{time_total}s\n" \
    -o /dev/null \
    https://spe-api-dev-67e2xz.azurewebsites.net/healthz
done

# Expected: <200ms average
```

**Baseline**: Record average response time

### Metric 3: Memory Usage

```bash
# Check current memory usage (if accessible)
az webapp show \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --query "siteConfig.memoryLimit" \
  -o tsv

# Note: Baseline memory for comparison during load tests
```

**Baseline**: Record memory configuration

---

## Environment Documentation

Document the current environment configuration for troubleshooting.

### Create Pre-Flight Report

```bash
# Create preflight report
cat > phase-5-preflight-report.md <<'EOF'
# Phase 5 Pre-Flight Report

**Date**: $(date)
**Environment**: Development (spe-api-dev-67e2xz)

## Deployment Info
- Latest Commit: $(git rev-parse HEAD)
- Deployed: $(az webapp deployment list --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --query "[0].start_time" -o tsv)

## Azure AD Configuration
- BFF API App ID: 1e40baad-e065-4aea-a8d4-4b7ab273458c
- PCF Client App ID: 170c98e1-92b9-47ca-b3e7-e9e13f4f6e13
- Admin Consent: ✅ (verified)

## Service Health
- API Health: ✅ (200 OK)
- Dataverse: ✅ (connected)
- Redis: ⚠️ (in-memory mode, not distributed)

## Baseline Metrics
- Ping latency: ___ ms (average of 5)
- Health check latency: ___ ms (average of 5)
- Memory limit: ___ MB

## Test Data
- Test Drive ID: $DRIVE_ID
- Container Type ID: 8a6ce34c-6055-4681-8f87-2f4f9f921c06

## Tools
- Azure CLI: ✅ (version: $(az --version | head -1))
- PAC CLI: ✅ (version: $(pac --version))
- Node.js: ✅ (version: $(node --version))
- curl: ✅ (version: $(curl --version | head -1))
- jq: ✅ (version: $(jq --version))

## Status
[✅ Ready for Phase 5 testing | ⚠️ Issues found - see below]

## Issues Found
[List any issues that need resolution before testing]

EOF
```

---

## Validation Checklist

**Code Deployment**:
- [ ] Latest commit deployed (within 24 hours)
- [ ] Phase 4 cache code present (health check passes)
- [ ] Configuration files match expected values

**Azure AD**:
- [ ] BFF API app registration exists (1e40baad-...)
- [ ] PCF client app registration exists (170c98e1-...)
- [ ] API permissions configured (PCF → BFF API)
- [ ] Admin consent granted
- [ ] BFF API exposes at least one scope

**Environment Variables**:
- [ ] All required app settings present
- [ ] Key Vault access configured (managed identity)
- [ ] Connection strings configured (Redis, Dataverse)

**Service Health**:
- [ ] API health check returns 200 OK
- [ ] Dataverse connectivity verified
- [ ] Redis connectivity verified (if enabled)
- [ ] No critical errors in application logs

**Tools**:
- [ ] Azure CLI installed and authenticated
- [ ] PAC CLI installed and connected to environment
- [ ] Node.js v18+ installed
- [ ] curl installed and working
- [ ] jq installed (optional)

**Test Data**:
- [ ] Test Drive ID obtained from Dataverse
- [ ] Container Type ID documented

**Baseline Metrics**:
- [ ] Ping latency measured (average: ___ ms)
- [ ] Health check latency measured (average: ___ ms)
- [ ] Memory usage documented

**Documentation**:
- [ ] Pre-flight report created
- [ ] Environment configuration documented
- [ ] Issues documented (if any)

---

## Pass Criteria

**Task 5.0 is COMPLETE when**:
- ✅ All checklist items checked
- ✅ No critical issues found (or all issues resolved)
- ✅ Pre-flight report generated
- ✅ Baseline metrics collected
- ✅ Environment documented

**If ANY check fails**:
- ⚠️ Document the failure in pre-flight report
- 🛑 STOP - Do NOT proceed to Task 5.1
- 🔧 Resolve issue before continuing
- 🔄 Re-run pre-flight verification

---

## Troubleshooting

### Issue: API Health Check Fails (500 Error)

**Cause**: Application startup error

**Fix**:
1. Check application logs: `az webapp log tail ...`
2. Look for startup errors (DI registration, missing config)
3. Verify Key Vault access (secrets loading correctly)
4. Restart app: `az webapp restart ...`

### Issue: Azure AD App Not Found

**Cause**: Wrong tenant or app ID

**Fix**:
1. Verify tenant ID: `az account show --query tenantId`
2. List apps: `az ad app list --query "[].{appId:appId, displayName:displayName}" -o table`
3. Update app IDs in documentation if different

### Issue: Key Vault Access Denied

**Cause**: Managed identity not granted Key Vault access

**Fix**:
```bash
# Get Web App managed identity
PRINCIPAL_ID=$(az webapp identity show \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2 \
  --query "principalId" -o tsv)

# Grant Key Vault access
az keyvault set-policy \
  --name spaarke-spekvcert \
  --object-id $PRINCIPAL_ID \
  --secret-permissions get list
```

### Issue: No Test Data (Drive ID)

**Cause**: No matters in Dataverse or no Drive IDs assigned

**Fix**:
1. Create test matter: `pac data create --entity-logical-name sprk_matter ...`
2. Or use existing matter and assign Drive ID manually
3. Verify container exists in SPE

---

## Evidence Collection

Take screenshots/logs of:
1. ✅ Health check response (200 OK)
2. ✅ Azure AD app registrations (displayName + appId)
3. ✅ App settings list (names only, no values)
4. ✅ Baseline metrics output
5. ✅ Pre-flight report (complete)

**Save to**: `dev/projects/sdap_V2/test-evidence/task-5.0/`

---

## Next Task

✅ **If all checks pass**: [Phase 5 - Task 1: Authentication Flow Validation](phase-5-task-1-authentication.md)

⚠️ **If checks fail**: Resolve issues, re-run Task 5.0, do NOT proceed until all pass.

---

## Related Resources

- **Phase 5 Overview**: [PHASE-5-OVERVIEW.md](../PHASE-5-OVERVIEW.md)
- **Testing Guide**: [END-TO-END-SPE-TESTING-GUIDE.md](../../END-TO-END-SPE-TESTING-GUIDE.md)
- **Health Check Pattern**: [patterns/endpoint-health-check.md](../patterns/endpoint-health-check.md)

---

**Last Updated**: 2025-10-14
**Status**: ✅ Template ready for execution
