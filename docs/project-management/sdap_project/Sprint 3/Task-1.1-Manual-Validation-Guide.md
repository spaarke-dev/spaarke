# Sprint 3 Task 1.1 - Manual Validation Guide
## Step-by-Step Validation in Live Environment

**Date**: 2025-10-01
**Task**: Authorization Implementation - Live Environment Validation
**Prerequisites**: Dataverse environment, test users, test documents
**Estimated Time**: 2-3 hours

---

## Table of Contents

1. [Environment Setup](#environment-setup)
2. [Test Data Preparation](#test-data-preparation)
3. [Validation Scenarios](#validation-scenarios)
4. [Performance Testing](#performance-testing)
5. [Audit Log Verification](#audit-log-verification)
6. [Troubleshooting](#troubleshooting)
7. [Sign-Off Checklist](#sign-off-checklist)

---

## Environment Setup

### Step 1: Verify Environment Configuration

**Purpose**: Ensure the development/staging environment is properly configured.

#### 1.1 Check Application Configuration

**File**: `appsettings.Development.json` or `appsettings.Staging.json`

Verify these settings are present:

```json
{
  "Dataverse": {
    "ServiceUrl": "https://your-org.crm.dynamics.com",
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret"
  },
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "your-tenant-id",
    "ClientId": "your-api-client-id",
    "Audience": "api://your-api-client-id"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Spaarke.Core.Auth": "Information",
      "Spaarke.Dataverse": "Information"
    }
  }
}
```

**✅ Verification**:
```bash
# Check configuration is valid
dotnet run --project src/api/Spe.Bff.Api -- --environment Development
# Should start without configuration errors
```

#### 1.2 Verify Dataverse Connectivity

**Test**: Can the application connect to Dataverse?

```bash
# Run health check endpoint
curl https://localhost:5001/ping

# Expected response:
{
  "service": "Spe.Bff.Api",
  "version": "1.0.0",
  "environment": "Development",
  "timestamp": "2025-10-01T10:00:00Z"
}
```

**✅ Checkpoint**: Application starts and responds to health check

---

### Step 2: Deploy Application

**Purpose**: Deploy the updated application to dev/staging environment.

#### 2.1 Build and Publish

```bash
# Build solution
cd c:\code_files\spaarke
dotnet build --configuration Release

# Publish API
dotnet publish src/api/Spe.Bff.Api/Spe.Bff.Api.csproj \
  --configuration Release \
  --output ./publish/api
```

**✅ Checkpoint**: Build succeeds with 0 errors

#### 2.2 Deploy to Environment

Choose your deployment method:

**Option A: Azure App Service**
```bash
# Deploy to Azure App Service
az webapp deployment source config-zip \
  --resource-group your-rg \
  --name your-app-name \
  --src ./publish/api.zip
```

**Option B: Local IIS** (for dev environment)
```bash
# Copy files to IIS directory
xcopy /E /I ./publish/api "C:\inetpub\wwwroot\spaarke-api"

# Restart IIS application pool
appcmd recycle apppool /apppool.name:SpaarkeApiPool
```

**Option C: Docker**
```bash
# Build and run Docker container
docker build -t spaarke-api:validation .
docker run -d -p 5001:80 --name spaarke-validation spaarke-api:validation
```

**✅ Checkpoint**: Application deployed and running

#### 2.3 Verify Deployment

```bash
# Test deployed application
curl https://your-app-url/ping

# Check logs for startup errors
# Azure: az webapp log tail --name your-app-name --resource-group your-rg
# Docker: docker logs spaarke-validation
```

**✅ Checkpoint**: Deployed application responds successfully

---

## Test Data Preparation

### Step 3: Prepare Dataverse Test Data

**Purpose**: Create test users and documents for validation scenarios.

#### 3.1 Create Test Users in Dataverse

**Using Power Platform Admin Center**:

1. Navigate to https://admin.powerplatform.microsoft.com
2. Select your environment → Settings → Users + permissions → Users
3. Create the following test users:

| User Email | Full Name | Business Unit | Security Role | Purpose |
|-----------|-----------|---------------|---------------|---------|
| `testuser.noaccess@yourorg.com` | Test User - No Access | Default | Basic User | Test denial scenario |
| `testuser.grant@yourorg.com` | Test User - Granted | Default | SPE Reader | Test grant scenario |
| `testuser.deny@yourorg.com` | Test User - Denied | Default | Basic User | Test explicit deny |
| `testuser.team@yourorg.com` | Test User - Team Member | Default | Basic User | Test team access |
| `testuser.admin@yourorg.com` | Test User - Admin | Default | System Administrator | Test admin access |

**✅ Checkpoint**: 5 test users created in Dataverse

#### 3.2 Create Test Documents

**Using Power Apps or API**:

Create test documents in the `sprk_document` entity:

```powershell
# PowerShell script to create test documents
$dataverseUrl = "https://your-org.crm.dynamics.com"
$accessToken = "your-access-token" # Get via Azure CLI or MSAL

$documents = @(
    @{
        sprk_name = "Document - No Access"
        sprk_documentid = "doc-no-access-001"
        description = "Test document for no access scenario"
    },
    @{
        sprk_name = "Document - Granted Access"
        sprk_documentid = "doc-granted-002"
        description = "Test document for grant scenario"
    },
    @{
        sprk_name = "Document - Explicit Deny"
        sprk_documentid = "doc-denied-003"
        description = "Test document for deny scenario"
    },
    @{
        sprk_name = "Document - Team Shared"
        sprk_documentid = "doc-team-004"
        description = "Test document shared with team"
    }
)

foreach ($doc in $documents) {
    $body = $doc | ConvertTo-Json
    Invoke-RestMethod -Uri "$dataverseUrl/api/data/v9.2/sprk_documents" `
        -Method Post `
        -Headers @{
            "Authorization" = "Bearer $accessToken"
            "Content-Type" = "application/json"
        } `
        -Body $body
}
```

**Alternative: Manual Creation via Power Apps**:
1. Open Power Apps → Solutions → Spaarke Solution
2. Navigate to `sprk_document` table
3. Create 4 test documents manually

**✅ Checkpoint**: 4 test documents created

#### 3.3 Configure Document Permissions

**Set up Dataverse permissions**:

**Document 1: No Access**
- Owner: `testuser.admin@yourorg.com`
- Shared with: No one
- Expected: `testuser.noaccess@yourorg.com` cannot access

**Document 2: Granted Access**
- Owner: `testuser.admin@yourorg.com`
- Shared with: `testuser.grant@yourorg.com` (Read privilege)
- Expected: `testuser.grant@yourorg.com` can read

**Document 3: Explicit Deny**
- Owner: `testuser.admin@yourorg.com`
- Shared with: `testuser.deny@yourorg.com` (No Access - revoked)
- Expected: `testuser.deny@yourorg.com` denied

**Document 4: Team Shared**
- Owner: `testuser.admin@yourorg.com`
- Shared with: "Legal Team" (team that includes `testuser.team@yourorg.com`)
- Expected: `testuser.team@yourorg.com` can access

**To Share a Document in Dataverse**:

```powershell
# PowerShell script to share document
$documentId = "doc-granted-002"
$targetUserId = "user-guid-for-testuser.grant"
$accessMask = 1  # Read = 1, Write = 2, Delete = 65536

$grantAccessRequest = @{
    Target = @{
        sprk_documentid = $documentId
        "@odata.type" = "Microsoft.Dynamics.CRM.sprk_document"
    }
    PrincipalAccess = @{
        AccessMask = $accessMask
        Principal = @{
            systemuserid = $targetUserId
            "@odata.type" = "Microsoft.Dynamics.CRM.systemuser"
        }
    }
} | ConvertTo-Json -Depth 5

Invoke-RestMethod -Uri "$dataverseUrl/api/data/v9.2/GrantAccess" `
    -Method Post `
    -Headers @{
        "Authorization" = "Bearer $accessToken"
        "Content-Type" = "application/json"
    } `
    -Body $grantAccessRequest
```

**✅ Checkpoint**: All 4 documents configured with correct permissions

#### 3.4 Create Access Team

**Create "Legal Team" for team access testing**:

1. In Power Apps, navigate to Settings → Security → Teams
2. Create new team:
   - **Name**: Legal Team
   - **Team Type**: Access Team
   - **Business Unit**: Default
3. Add member: `testuser.team@yourorg.com`
4. Share Document 4 with this team

**✅ Checkpoint**: Access team created and document shared

---

## Validation Scenarios

### Step 4: Test Scenario 1 - Unauthorized Request (401)

**Purpose**: Verify authentication is required.

#### 4.1 Test Without Token

```bash
# Request without Authorization header
curl -X GET https://your-app-url/api/containers \
  -H "Content-Type: application/json" \
  -v

# Expected Response:
# HTTP/1.1 401 Unauthorized
# WWW-Authenticate: Bearer
```

**✅ Expected**: HTTP 401 Unauthorized

#### 4.2 Test With Invalid Token

```bash
# Request with invalid/expired token
curl -X GET https://your-app-url/api/containers \
  -H "Authorization: Bearer invalid-token-12345" \
  -v

# Expected Response:
# HTTP/1.1 401 Unauthorized
```

**✅ Expected**: HTTP 401 Unauthorized

**✅ Checkpoint**: Authentication is enforced

---

### Step 5: Test Scenario 2 - No Access (403)

**Purpose**: Verify users without permissions are denied.

#### 5.1 Get Access Token for Test User

```bash
# Use Azure CLI to get token as test user
az login --username testuser.noaccess@yourorg.com
TOKEN=$(az account get-access-token --resource api://your-api-client-id --query accessToken -o tsv)
echo $TOKEN
```

**Alternative: Use Postman**:
1. OAuth 2.0 Authentication
2. Grant Type: Authorization Code
3. Auth URL: `https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize`
4. Access Token URL: `https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token`
5. Client ID: Your API client ID
6. Scope: `api://your-api-client-id/.default`
7. User: `testuser.noaccess@yourorg.com`

#### 5.2 Attempt to Access Document

```bash
# Try to access document user doesn't have permission for
curl -X GET https://your-app-url/api/containers/doc-no-access-001/metadata \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -v

# Expected Response:
# HTTP/1.1 403 Forbidden
# Content-Type: application/problem+json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.3",
  "title": "Forbidden",
  "status": 403,
  "detail": "Insufficient permissions to access this resource"
}
```

**✅ Expected**: HTTP 403 Forbidden

#### 5.3 Verify Audit Log

Check application logs for authorization denial:

```bash
# Azure App Service
az webapp log tail --name your-app-name --resource-group your-rg | grep "AUTHORIZATION DENIED"

# Docker
docker logs spaarke-validation | grep "AUTHORIZATION DENIED"

# Expected Log Entry:
# [10:30:15 WRN] AUTHORIZATION DENIED: User testuser.noaccess@yourorg.com denied read_files on doc-no-access-001 by ExplicitGrantRule - Reason: sdap.access.deny.no_rule (AccessLevel: None, Duration: 45ms)
```

**✅ Expected**: Audit log shows denial with reason and duration

**✅ Checkpoint**: No access scenario working correctly

---

### Step 6: Test Scenario 3 - Granted Access (200)

**Purpose**: Verify users with permissions can access resources.

#### 6.1 Get Access Token for Grant User

```bash
# Get token as user with granted access
az login --username testuser.grant@yourorg.com
TOKEN_GRANT=$(az account get-access-token --resource api://your-api-client-id --query accessToken -o tsv)
```

#### 6.2 Access Granted Document

```bash
# Access document user has permission for
curl -X GET https://your-app-url/api/containers/doc-granted-002/metadata \
  -H "Authorization: Bearer $TOKEN_GRANT" \
  -H "Content-Type: application/json" \
  -v

# Expected Response:
# HTTP/1.1 200 OK
# Content-Type: application/json
{
  "documentId": "doc-granted-002",
  "name": "Document - Granted Access",
  "description": "Test document for grant scenario",
  "createdOn": "2025-10-01T10:00:00Z",
  "owner": {
    "id": "...",
    "name": "Test User - Admin"
  }
}
```

**✅ Expected**: HTTP 200 OK with document metadata

#### 6.3 Verify Audit Log

```bash
# Check logs for successful authorization
az webapp log tail --name your-app-name --resource-group your-rg | grep "AUTHORIZATION GRANTED"

# Expected Log Entry:
# [10:35:20 INF] AUTHORIZATION GRANTED: User testuser.grant@yourorg.com granted read_files on doc-granted-002 by ExplicitGrantRule - Reason: sdap.access.allow.explicit_grant (AccessLevel: Grant, Duration: 38ms)
```

**✅ Expected**: Audit log shows grant with AccessLevel: Grant

#### 6.4 Test Write Operation (if granted)

```bash
# Attempt to update document metadata
curl -X PATCH https://your-app-url/api/containers/doc-granted-002/metadata \
  -H "Authorization: Bearer $TOKEN_GRANT" \
  -H "Content-Type: application/json" \
  -d '{"description": "Updated description"}' \
  -v

# Expected: 200 OK if user has Write, 403 if Read-only
```

**✅ Expected**: Response matches user's actual permissions

**✅ Checkpoint**: Granted access scenario working correctly

---

### Step 7: Test Scenario 4 - Explicit Deny (403)

**Purpose**: Verify explicit deny overrides other permissions.

#### 7.1 Get Access Token for Deny User

```bash
# Get token as user with explicit deny
az login --username testuser.deny@yourorg.com
TOKEN_DENY=$(az account get-access-token --resource api://your-api-client-id --query accessToken -o tsv)
```

#### 7.2 Attempt Access to Denied Document

```bash
# Try to access document with explicit deny
curl -X GET https://your-app-url/api/containers/doc-denied-003/metadata \
  -H "Authorization: Bearer $TOKEN_DENY" \
  -H "Content-Type: application/json" \
  -v

# Expected Response:
# HTTP/1.1 403 Forbidden
```

**✅ Expected**: HTTP 403 Forbidden

#### 7.3 Verify Audit Log Shows Explicit Deny

```bash
# Check logs for explicit deny
az webapp log tail --name your-app-name --resource-group your-rg | grep "ExplicitDenyRule"

# Expected Log Entry:
# [10:40:30 WRN] AUTHORIZATION DENIED: User testuser.deny@yourorg.com denied read_files on doc-denied-003 by ExplicitDenyRule - Reason: sdap.access.deny.explicit_deny (AccessLevel: Deny, Duration: 42ms)
```

**✅ Expected**: Audit log shows `ExplicitDenyRule` and `AccessLevel: Deny`

**✅ Checkpoint**: Explicit deny scenario working correctly

---

### Step 8: Test Scenario 5 - Team Membership Access (200)

**Purpose**: Verify team membership grants access.

#### 8.1 Get Access Token for Team User

```bash
# Get token as user who is team member
az login --username testuser.team@yourorg.com
TOKEN_TEAM=$(az account get-access-token --resource api://your-api-client-id --query accessToken -o tsv)
```

#### 8.2 Access Team-Shared Document

```bash
# Access document shared with user's team
curl -X GET https://your-app-url/api/containers/doc-team-004/metadata \
  -H "Authorization: Bearer $TOKEN_TEAM" \
  -H "Content-Type: application/json" \
  -v

# Expected Response:
# HTTP/1.1 200 OK
```

**✅ Expected**: HTTP 200 OK

#### 8.3 Verify Audit Log Shows Team Access

```bash
# Check logs for team membership authorization
az webapp log tail --name your-app-name --resource-group your-rg | grep "TeamMembershipRule"

# Expected Log Entry:
# [10:45:40 INF] AUTHORIZATION GRANTED: User testuser.team@yourorg.com granted read_files on doc-team-004 by TeamMembershipRule - Reason: sdap.access.allow.team_membership (AccessLevel: Grant, Duration: 52ms)
```

**✅ Expected**: Audit log shows `TeamMembershipRule` granted access

**✅ Checkpoint**: Team membership scenario working correctly

---

### Step 9: Test Scenario 6 - Admin Access (200)

**Purpose**: Verify system administrators have full access.

#### 9.1 Get Access Token for Admin

```bash
# Get token as system administrator
az login --username testuser.admin@yourorg.com
TOKEN_ADMIN=$(az account get-access-token --resource api://your-api-client-id --query accessToken -o tsv)
```

#### 9.2 Access All Documents

```bash
# Admin should have access to all documents
for doc in doc-no-access-001 doc-granted-002 doc-denied-003 doc-team-004; do
  echo "Testing access to $doc..."
  curl -X GET https://your-app-url/api/containers/$doc/metadata \
    -H "Authorization: Bearer $TOKEN_ADMIN" \
    -H "Content-Type: application/json" \
    -s -o /dev/null -w "HTTP Status: %{http_code}\n"
done

# Expected: All return 200 OK
```

**✅ Expected**: Admin can access all documents (200 OK)

**✅ Checkpoint**: Admin access working correctly

---

## Performance Testing

### Step 10: Measure Authorization Check Latency

**Purpose**: Verify authorization checks meet < 200ms P95 target.

#### 10.1 Single Request Latency Test

```bash
# Test single authorization check timing
for i in {1..10}; do
  curl -X GET https://your-app-url/api/containers/doc-granted-002/metadata \
    -H "Authorization: Bearer $TOKEN_GRANT" \
    -H "Content-Type: application/json" \
    -w "\nTime: %{time_total}s\n" \
    -o /dev/null \
    -s
done

# Expected: Most requests < 0.2s (200ms)
```

**✅ Expected**: P95 < 200ms

#### 10.2 Extract Timing from Audit Logs

```bash
# Extract authorization durations from logs
az webapp log tail --name your-app-name --resource-group your-rg | \
  grep "AUTHORIZATION GRANTED\|AUTHORIZATION DENIED" | \
  grep -oP 'Duration: \K\d+' | \
  awk '{
    sum+=$1;
    values[NR]=$1
  }
  END {
    asort(values)
    print "Count:", NR
    print "Mean:", sum/NR, "ms"
    print "P50:", values[int(NR*0.5)], "ms"
    print "P95:", values[int(NR*0.95)], "ms"
    print "P99:", values[int(NR*0.99)], "ms"
  }'

# Expected Output:
# Count: 100
# Mean: 45 ms
# P50: 42 ms
# P95: 85 ms
# P99: 150 ms
```

**✅ Expected**: P95 < 200ms, P99 < 500ms

#### 10.3 Load Test (100 Concurrent Requests)

**Using Apache Bench**:
```bash
# Install Apache Bench (if not already installed)
# Ubuntu: sudo apt-get install apache2-utils
# Windows: Download from https://www.apachelounge.com/download/

# Run load test
ab -n 1000 -c 100 -H "Authorization: Bearer $TOKEN_GRANT" \
  https://your-app-url/api/containers/doc-granted-002/metadata

# Expected Results:
# Requests per second: > 100
# Time per request (mean): < 100ms
# Time per request (95%): < 200ms
```

**Expected Output**:
```
Concurrency Level:      100
Time taken for tests:   5.234 seconds
Complete requests:      1000
Failed requests:        0
Requests per second:    191.06 [#/sec] (mean)
Time per request:       523.392 [ms] (mean)
Time per request:       5.234 [ms] (mean, across all concurrent requests)

Percentage of requests served within a certain time (ms)
  50%     42
  66%     58
  75%     72
  80%     85
  90%    125
  95%    175
  98%    245
  99%    320
 100%    523 (longest request)
```

**✅ Expected**:
- P95 < 200ms ✅
- No failed requests ✅
- Requests per second > 50 ✅

**⚠️ If Performance Issues**:
- Check Dataverse query performance
- Consider adding Redis distributed cache
- Review database indexes
- Check network latency to Dataverse

**✅ Checkpoint**: Performance meets requirements

---

## Audit Log Verification

### Step 11: Verify Comprehensive Audit Logging

**Purpose**: Ensure all authorization decisions are logged for compliance.

#### 11.1 Verify Log Structure

**Check logs contain all required fields**:

```bash
# Get sample log entry
az webapp log tail --name your-app-name --resource-group your-rg | \
  grep "AUTHORIZATION GRANTED" | head -1

# Expected format:
# [timestamp] [level] AUTHORIZATION GRANTED: User {userId} granted {operation} on {resourceId} by {ruleName} - Reason: {reasonCode} (AccessLevel: {level}, Duration: {ms}ms)
```

**✅ Required Fields**:
- ✅ Timestamp
- ✅ Log level (INF for granted, WRN for denied, ERR for errors)
- ✅ UserId (who attempted access)
- ✅ Operation (what they tried to do)
- ✅ ResourceId (what they tried to access)
- ✅ RuleName (which rule made the decision)
- ✅ ReasonCode (structured reason for decision)
- ✅ AccessLevel (None, Deny, Grant)
- ✅ Duration (performance tracking)

#### 11.2 Test All Log Types

**Test 1: Successful Authorization (LogInformation)**
```bash
curl -X GET https://your-app-url/api/containers/doc-granted-002/metadata \
  -H "Authorization: Bearer $TOKEN_GRANT" \
  -s -o /dev/null

# Check log:
az webapp log tail --name your-app-name --resource-group your-rg | grep "AUTHORIZATION GRANTED"

# ✅ Expected: Log level INF, all fields present
```

**Test 2: Denied Authorization (LogWarning)**
```bash
curl -X GET https://your-app-url/api/containers/doc-no-access-001/metadata \
  -H "Authorization: Bearer $TOKEN" \
  -s -o /dev/null

# Check log:
az webapp log tail --name your-app-name --resource-group your-rg | grep "AUTHORIZATION DENIED"

# ✅ Expected: Log level WRN, all fields present
```

**Test 3: System Error (LogError)**

Simulate error by providing invalid document ID:
```bash
curl -X GET https://your-app-url/api/containers/invalid-doc-999/metadata \
  -H "Authorization: Bearer $TOKEN_GRANT" \
  -s -o /dev/null

# Check log:
az webapp log tail --name your-app-name --resource-group your-rg | grep "AUTHORIZATION ERROR"

# ✅ Expected: Log level ERR, fail-closed (denied access), exception details
```

**✅ Checkpoint**: All authorization decisions are logged with complete information

#### 11.3 Export Audit Logs for Compliance

```bash
# Export last 1000 authorization log entries
az webapp log download \
  --name your-app-name \
  --resource-group your-rg \
  --log-file authorization-audit-$(date +%Y%m%d).zip

# Extract and filter
unzip authorization-audit-*.zip
cat *.log | grep "AUTHORIZATION" > authorization-audit-report.log

# Generate summary report
cat authorization-audit-report.log | \
  awk '{
    if ($0 ~ /GRANTED/) granted++
    else if ($0 ~ /DENIED/) denied++
    else if ($0 ~ /ERROR/) errors++
  }
  END {
    print "=== Authorization Audit Summary ==="
    print "Granted:", granted
    print "Denied:", denied
    print "Errors:", errors
    print "Total:", granted+denied+errors
  }'
```

**Sample Report**:
```
=== Authorization Audit Summary ===
Granted: 850
Denied: 142
Errors: 8
Total: 1000
```

**✅ Checkpoint**: Audit logs exportable and contain compliance data

---

## Troubleshooting

### Common Issues and Solutions

#### Issue 1: All Requests Return 403

**Symptoms**:
- Even admin users get 403 Forbidden
- Audit logs show `AccessLevel: None` for all users

**Diagnosis**:
```bash
# Check if DataverseAccessDataSource is connecting
az webapp log tail --name your-app-name --resource-group your-rg | grep "DataverseAccessDataSource"

# Look for errors like:
# "Failed to fetch access data for user"
# "Dataverse connection error"
```

**Possible Causes & Solutions**:

1. **Dataverse Connection String Wrong**
   ```bash
   # Verify configuration
   az webapp config appsettings list \
     --name your-app-name \
     --resource-group your-rg \
     --query "[?name=='Dataverse:ServiceUrl'].value"

   # Fix: Update app setting
   az webapp config appsettings set \
     --name your-app-name \
     --resource-group your-rg \
     --settings Dataverse:ServiceUrl="https://correct-url.crm.dynamics.com"
   ```

2. **Authentication Failing to Dataverse**
   ```bash
   # Check if client credentials are valid
   az ad sp show --id your-client-id

   # Fix: Reset client secret and update configuration
   ```

3. **HttpClient Not Configured**
   ```bash
   # Check startup logs for DI registration errors
   az webapp log tail --name your-app-name --resource-group your-rg | grep "AddHttpClient"
   ```

---

#### Issue 2: Performance > 200ms

**Symptoms**:
- Authorization checks taking > 200ms
- P95 or P99 too high

**Diagnosis**:
```bash
# Check authorization durations in logs
az webapp log tail --name your-app-name --resource-group your-rg | \
  grep "Duration:" | \
  grep -oP 'Duration: \K\d+' | sort -n | tail -20

# If many > 200ms, investigate further
```

**Possible Causes & Solutions**:

1. **Slow Dataverse Queries**
   ```bash
   # Check Dataverse service health
   curl https://your-org.crm.dynamics.com/api/data/v9.2/$metadata

   # Solution: Contact Microsoft support or consider caching
   ```

2. **Network Latency**
   ```bash
   # Test latency to Dataverse
   ping your-org.crm.dynamics.com

   # Solution: Deploy closer to Dataverse region
   ```

3. **No Caching**
   ```bash
   # Check if RequestCache is registered
   az webapp log tail --name your-app-name --resource-group your-rg | grep "RequestCache"

   # Solution: Verify SpaarkeCore.AddSpaarkeCore() registers RequestCache
   ```

4. **Add Distributed Cache (Redis)**
   ```csharp
   // In Program.cs, add:
   builder.Services.AddStackExchangeRedisCache(options =>
   {
       options.Configuration = builder.Configuration["Redis:ConnectionString"];
       options.InstanceName = "SpaarkeAuth_";
   });

   // In DataverseAccessDataSource, cache for 5 minutes
   ```

---

#### Issue 3: Audit Logs Missing

**Symptoms**:
- No "AUTHORIZATION GRANTED" or "AUTHORIZATION DENIED" logs
- Application works but no audit trail

**Diagnosis**:
```bash
# Check log level configuration
az webapp config appsettings list \
  --name your-app-name \
  --resource-group your-rg \
  --query "[?name=='Logging:LogLevel:Spaarke.Core.Auth'].value"

# Should be "Information" or "Debug", not "Warning" or "Error"
```

**Solution**:
```bash
# Set correct log level
az webapp config appsettings set \
  --name your-app-name \
  --resource-group your-rg \
  --settings \
    "Logging:LogLevel:Spaarke.Core.Auth=Information" \
    "Logging:LogLevel:Spaarke.Dataverse=Information"

# Restart app
az webapp restart --name your-app-name --resource-group your-rg
```

---

#### Issue 4: Team Access Not Working

**Symptoms**:
- Users in teams get 403 even though document shared with team
- Audit log shows `AccessLevel: None` for team members

**Diagnosis**:
```bash
# Check if team memberships are being queried
az webapp log tail --name your-app-name --resource-group your-rg | \
  grep "QueryUserTeamMembershipsAsync"

# Check if TeamMembershipRule is registered
az webapp log tail --name your-app-name --resource-group your-rg | \
  grep "TeamMembershipRule"
```

**Possible Causes & Solutions**:

1. **Access Team vs. Owner Team**
   - Verify team type is "Access Team" in Dataverse
   - Owner teams don't work the same way

2. **User Not Actually in Team**
   ```powershell
   # Verify team membership in Dataverse
   $dataverseUrl = "https://your-org.crm.dynamics.com"
   $userId = "user-guid"

   Invoke-RestMethod -Uri "$dataverseUrl/api/data/v9.2/systemusers($userId)/teammembership_association" `
     -Headers @{"Authorization" = "Bearer $token"}
   ```

3. **Document Not Shared with Team**
   ```powershell
   # Check who has access to document
   $documentId = "doc-id"
   Invoke-RestMethod -Uri "$dataverseUrl/api/data/v9.2/RetrieveSharedPrincipalsAndAccess" `
     -Method Post `
     -Headers @{"Authorization" = "Bearer $token"; "Content-Type" = "application/json"} `
     -Body (@{Target = @{sprk_documentid=$documentId; "@odata.type"="Microsoft.Dynamics.CRM.sprk_document"}} | ConvertTo-Json)
   ```

---

#### Issue 5: 401 Unauthorized for Valid Tokens

**Symptoms**:
- Users have valid tokens but get 401
- Token works in other applications

**Diagnosis**:
```bash
# Decode JWT token to check claims
echo $TOKEN | cut -d'.' -f2 | base64 -d | jq

# Check for:
# - "aud" (audience) matches your API
# - "iss" (issuer) matches your tenant
# - "oid" (object ID) or "sub" exists
```

**Solution**:
```bash
# Verify Azure AD configuration matches token
az webapp config appsettings list \
  --name your-app-name \
  --resource-group your-rg \
  --query "[?starts_with(name, 'AzureAd')].{name:name,value:value}"

# Fix audience mismatch
az webapp config appsettings set \
  --name your-app-name \
  --resource-group your-rg \
  --settings "AzureAd:Audience=api://correct-client-id"
```

---

## Sign-Off Checklist

### Step 12: Final Validation Sign-Off

**Complete this checklist before promoting to production**:

#### Security Validation

- [ ] **Authentication Required**: Unauthorized requests return 401
- [ ] **No Access Denied**: Users without permissions return 403
- [ ] **Granted Access Works**: Users with permissions return 200
- [ ] **Explicit Deny Works**: Deny overrides other permissions
- [ ] **Team Access Works**: Team membership grants access
- [ ] **Admin Access Works**: Administrators have full access
- [ ] **Fail-Closed Verified**: Errors result in 403 (not 200)
- [ ] **No Security Bypasses**: Cannot access resources without proper auth

#### Audit & Compliance

- [ ] **All Grants Logged**: Every successful authorization logged at Information level
- [ ] **All Denials Logged**: Every denied authorization logged at Warning level
- [ ] **All Errors Logged**: Every error logged at Error level with exception details
- [ ] **Log Fields Complete**: UserId, ResourceId, Operation, RuleName, ReasonCode, AccessLevel, Duration present in all logs
- [ ] **Logs Exportable**: Can export audit logs for compliance reporting
- [ ] **Correlation IDs Present**: Can trace requests end-to-end

#### Performance

- [ ] **P50 < 100ms**: Median authorization check under 100ms
- [ ] **P95 < 200ms**: 95th percentile under 200ms
- [ ] **P99 < 500ms**: 99th percentile under 500ms
- [ ] **No Timeouts**: No authorization checks timing out
- [ ] **Load Test Passed**: 100 concurrent requests handled successfully
- [ ] **No Memory Leaks**: Application memory stable under load

#### Functional

- [ ] **Health Check Works**: /ping endpoint returns 200
- [ ] **All Test Scenarios Pass**: Scenarios 1-6 all passed
- [ ] **Different Operations Tested**: Read, write, delete operations all tested
- [ ] **Different Policies Tested**: canmanagecontainers, canwritefiles, canreadfiles all tested
- [ ] **Error Handling Works**: Invalid document IDs handled gracefully

#### Code Quality

- [ ] **No TODO Comments**: All TODO comments removed from authorization code
- [ ] **Code Reviewed**: Senior developer reviewed implementation
- [ ] **Documentation Updated**: All documentation reflects current implementation
- [ ] **Integration Tests Pass**: All integration tests passing
- [ ] **Build Succeeds**: Solution builds with 0 errors

#### Deployment

- [ ] **Configuration Verified**: All app settings correct for environment
- [ ] **Secrets Secured**: No secrets in code or logs
- [ ] **Monitoring Configured**: Application Insights or equivalent configured
- [ ] **Alerts Configured**: Alerts for authorization errors and performance issues
- [ ] **Rollback Plan Ready**: Can rollback deployment if issues arise

---

## Validation Report

### Step 13: Generate Validation Report

**Create final validation report**:

```markdown
# Sprint 3 Task 1.1 - Validation Report

**Date**: [Date]
**Environment**: [Dev/Staging/Production]
**Validated By**: [Your Name]

## Summary

Authorization implementation validated and **APPROVED / NOT APPROVED** for production deployment.

## Test Results

| Scenario | Status | Notes |
|----------|--------|-------|
| Unauthorized (401) | ✅ PASS | Returns 401 without token |
| No Access (403) | ✅ PASS | Denies users without permissions |
| Granted Access (200) | ✅ PASS | Allows users with permissions |
| Explicit Deny (403) | ✅ PASS | Deny overrides other permissions |
| Team Access (200) | ✅ PASS | Team membership grants access |
| Admin Access (200) | ✅ PASS | Admins have full access |

## Performance Results

- **P50**: 42ms ✅
- **P95**: 85ms ✅
- **P99**: 150ms ✅
- **Max**: 320ms ✅

## Audit Logging

- **Granted**: 850 logged ✅
- **Denied**: 142 logged ✅
- **Errors**: 8 logged ✅
- **Total**: 1000 logged ✅

## Issues Found

[List any issues found during validation]

1. [Issue description]
   - Severity: Low/Medium/High/Critical
   - Resolution: [How it was fixed or will be fixed]

## Recommendations

[Any recommendations for production deployment]

1. [Recommendation 1]
2. [Recommendation 2]

## Sign-Off

**Approved for Production**: YES / NO

**Signatures**:
- Developer: ___________________ Date: _______
- QA: ___________________ Date: _______
- Security: ___________________ Date: _______
- Product Owner: ___________________ Date: _______
```

---

## Next Steps

### After Successful Validation

1. **Update README**: Document authorization configuration for ops team
2. **Train Support Team**: Ensure support understands authorization audit logs
3. **Schedule Production Deployment**: Plan production rollout
4. **Monitor Post-Deployment**: Watch authorization metrics for first 48 hours

### If Validation Fails

1. **Document Issues**: Record all failures in issue tracker
2. **Fix Critical Issues**: Address any security or functional failures
3. **Re-test**: Re-run validation after fixes
4. **Escalate if Needed**: Involve senior developers/architects for complex issues

---

**Validation Guide Version**: 1.0
**Last Updated**: 2025-10-01
**Maintained By**: Sprint 3 Team
