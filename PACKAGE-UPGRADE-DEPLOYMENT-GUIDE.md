# BFF API Package Upgrade Deployment Guide

**Date:** October 20, 2025
**Target Environment:** Azure Web App (spe-api-dev-67e2xz)
**Deployment Type:** Direct Production Deployment with Rollback Plan

---

## Prerequisites Completed ✅

- ✅ Local testing passed (see [PACKAGE-UPGRADE-TEST-RESULTS.md](./PACKAGE-UPGRADE-TEST-RESULTS.md))
- ✅ Code committed to git (commit: 6d80f3f)
- ✅ Package versions locked in csproj
- ✅ Zero compilation errors
- ✅ All API contracts preserved

---

## Deployment Strategy

Since no staging slot is configured, we'll use a **direct deployment with rollback capability**.

### Option 1: Direct Deployment (Recommended)
Deploy upgraded code directly to production with instant rollback available via Azure Web App deployment history.

**Pros:**
- ✅ Fastest deployment
- ✅ Built-in Azure rollback (< 1 minute)
- ✅ Simple process

**Cons:**
- ⚠️ Brief downtime during deployment (typically < 30 seconds)
- ⚠️ Rollback requires manual trigger

### Option 2: Create Staging Slot (Optional)
Create a staging slot for blue-green deployment with zero downtime.

**Pros:**
- ✅ Zero downtime
- ✅ Can test in staging before swap
- ✅ Instant swap/rollback

**Cons:**
- ⚠️ Requires additional Azure resources
- ⚠️ More complex setup

---

## Deployment Steps (Option 1: Direct Deployment)

### Step 1: Build and Publish

```bash
cd /c/code_files/spaarke/src/api/Spe.Bff.Api

# Clean build to ensure fresh compilation
dotnet clean
dotnet restore
dotnet build --configuration Release

# Publish for deployment
dotnet publish --configuration Release --output ./publish
```

**Validation:**
- Ensure `publish/` folder contains all DLLs
- Verify Microsoft.Graph.dll size (~39MB for v5.x)
- Check for any build warnings

### Step 2: Create Deployment Archive

```bash
cd publish
tar -czf ../deployment.tar.gz *
cd ..
```

**Validation:**
- Verify deployment.tar.gz exists
- Check file size (should be ~50-60MB compressed)

### Step 3: Deploy to Azure

```bash
# Deploy using Azure CLI
az webapp deploy \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --src-path deployment.tar.gz \
  --type tar
```

**Expected Output:**
```json
{
  "active": true,
  "complete": true,
  "deployer": "ZipDeploy",
  "id": "...",
  "status": 4
}
```

### Step 4: Restart Web App

```bash
az webapp restart \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz
```

**Wait Time:** 30-60 seconds for full restart

### Step 5: Verify Deployment

#### 5.1 Check Health Endpoint
```bash
curl -i https://spe-api-dev-67e2xz.azurewebsites.net/healthz
```

**Expected:** `HTTP/1.1 200 OK` with `Healthy` response

#### 5.2 Check Dataverse Health
```bash
curl -i https://spe-api-dev-67e2xz.azurewebsites.net/healthz/dataverse
```

**Expected:** `HTTP/1.1 200 OK` (should work in production with proper config)

#### 5.3 Monitor Logs
```bash
az webapp log tail \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --filter Error
```

**Watch for:**
- Graph API errors (ODataError exceptions)
- Polly retry/circuit breaker messages
- Authentication errors

#### 5.4 Test Graph Operations
Use Postman or PCF to test:
- Container listing
- File upload
- File download
- Error handling (403/401 responses)

---

## Rollback Procedure

### Quick Rollback (< 1 minute)

If issues are detected after deployment:

```bash
# Get list of deployments
az webapp deployment list \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --output table

# Redeploy previous version (use ID from list above)
az webapp deployment source show \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --deployment-id <PREVIOUS_DEPLOYMENT_ID>

# Restart
az webapp restart \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz
```

### Git Rollback (Alternate)

```bash
# Revert commit locally
cd /c/code_files/spaarke
git revert 6d80f3f  # Revert package upgrade commit

# Rebuild and redeploy
cd src/api/Spe.Bff.Api
dotnet publish --configuration Release --output ./publish
cd publish
tar -czf ../deployment.tar.gz *
cd ..
az webapp deploy --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz --src-path deployment.tar.gz --type tar
az webapp restart --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz
```

---

## Monitoring Checklist

### First 15 Minutes After Deployment

Monitor Application Insights for:

1. **Error Rate**
   - Check for spikes in 500 errors
   - Watch for Graph API errors (403, 401)
   - Monitor Polly circuit breaker events

2. **Response Times**
   - Baseline: < 100ms for health endpoint
   - Graph operations: < 2 seconds
   - Watch for timeout increases

3. **Exception Types**
   - ODataError exceptions should be handled
   - No ServiceException (old SDK) errors
   - Polly timeout/circuit breaker exceptions

4. **Dependency Health**
   - Graph API calls successful
   - Dataverse connections working
   - Service Bus processing functional

### Integration Testing with PCF

1. Open Dataverse environment
2. Navigate to sprk_Document grid
3. Test Universal Dataset Grid:
   - ✅ Grid loads with data
   - ✅ File uploads work (OBO flow)
   - ✅ File downloads work
   - ✅ Error messages display correctly

4. Check browser console for:
   - No CORS errors
   - No authentication errors
   - API responses in ProblemDetails format

---

## Known Issues and Mitigations

### Issue 1: Service Bus Connection Errors (Local Only)
**Symptom:** "No connection could be made because the target machine actively refused it"
**Impact:** Low (only affects local development)
**Mitigation:** Expected in local env; production has Service Bus configured

### Issue 2: Dataverse Configuration Errors (Local Only)
**Symptom:** "Invalid URI: The URI scheme is not valid"
**Impact:** Low (only affects local development)
**Mitigation:** Expected in local env; production has connection string configured

### Issue 3: Microsoft.Identity.Web Vulnerability Warning
**Symptom:** NU1902: Package has known moderate severity vulnerability
**Impact:** Low (non-blocking)
**Mitigation:** Plan upgrade to v3.3.0+ in future sprint

---

## Success Criteria

Deployment is considered successful when:

- ✅ Health endpoint returns 200 OK
- ✅ Dataverse health endpoint returns 200 OK
- ✅ PCF control loads and operates normally
- ✅ File uploads/downloads work via OBO flow
- ✅ Error responses maintain ProblemDetails format
- ✅ No error spikes in Application Insights
- ✅ Response times within acceptable range
- ✅ Zero Graph SDK v0.x errors in logs

---

## Post-Deployment Tasks

After successful deployment and verification:

1. ✅ Monitor for 24 hours for stability
2. ✅ Document any issues encountered
3. ✅ Update Phase 7 status to continue implementation
4. ✅ Begin NavMapEndpoints integration (Task 7.3)

---

## Deployment Schedule

**Recommended Time:** Off-peak hours (evening or weekend)
**Estimated Duration:** 30-45 minutes including verification
**Rollback Time:** < 5 minutes if needed

---

## Contact Information

**Azure Web App:** spe-api-dev-67e2xz
**Resource Group:** spe-infrastructure-westus2
**Region:** West US 2
**Environment URL:** https://spe-api-dev-67e2xz.azurewebsites.net

---

## Related Documents

- [PACKAGE-UPGRADE-TEST-RESULTS.md](./PACKAGE-UPGRADE-TEST-RESULTS.md) - Local testing results
- [PACKAGE-UPGRADE-IMPACT-ANALYSIS.md](./PACKAGE-UPGRADE-IMPACT-ANALYSIS.md) - Impact analysis
- [BFF-API-DEPENDENCY-ISSUE-ANALYSIS.md](./BFF-API-DEPENDENCY-ISSUE-ANALYSIS.md) - Root cause analysis

---

**Prepared by:** Claude (AI Assistant)
**Review Status:** Pending user approval
**Deployment Status:** Ready for execution
