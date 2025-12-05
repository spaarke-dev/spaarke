# Task 040 - Deploy to Test Environment

## Date: December 4, 2025

## Status: Ready for Execution (Manual Steps Required)

This document provides the deployment checklist and commands for deploying the FileViewer enhancements to the test environment.

---

## Pre-Deployment Checklist

### Code Quality
- [x] All unit tests pass (dotnet test)
- [x] Build succeeds without errors
- [x] Code review complete
- [x] ADR compliance verified

### Dependencies Verified
- [x] BFF /open-links endpoint implemented
- [x] PCF FileViewer Edit button implemented
- [x] Integration tests created
- [x] E2E tests created

### Environment Prerequisites
- [ ] Azure CLI authenticated (`az login`)
- [ ] PAC CLI authenticated (`pac auth create`)
- [ ] Access to test App Service
- [ ] Access to test Dataverse environment
- [ ] Rollback plan documented

---

## Deployment Steps

### Step 1: Build BFF API

```bash
cd /c/code_files/spaarke/src/server/api/Spe.Bff.Api

# Build release version
dotnet publish -c Release -o ./publish

# Verify build output
ls ./publish/*.dll | head -5
```

### Step 2: Deploy BFF to Test App Service

```bash
# Option A: Using Azure CLI
az webapp deploy \
    --resource-group rg-spaarke-test \
    --name spe-api-dev-67e2xz \
    --src-path ./publish \
    --type zip

# Option B: Using Azure Portal
# 1. Navigate to App Service > Deployment Center
# 2. Upload publish folder as ZIP

# Option C: Using GitHub Actions (if configured)
# Push to deploy branch triggers CI/CD
```

### Step 3: Verify BFF Deployment

```bash
# Check health endpoint
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz

# Check ping endpoint
curl https://spe-api-dev-67e2xz.azurewebsites.net/ping
# Expected: pong

# Check status endpoint
curl https://spe-api-dev-67e2xz.azurewebsites.net/status
# Expected: {"service":"Spe.Bff.Api","version":"1.0.0",...}

# Check open-links endpoint (requires auth)
# Use Postman or browser with OAuth token
```

### Step 4: Build PCF Solution

```bash
cd /c/code_files/spaarke/src/client/pcf/SpeFileViewer

# Disable central package management for PCF build
if [ -f "../../Directory.Packages.props" ]; then
    mv "../../Directory.Packages.props" "../../Directory.Packages.props.disabled"
fi

# Clean and build
npm run clean
npm run build

# Build solution package
cd SpeFileViewerSolution
"/c/Program Files/Microsoft Visual Studio/2022/Professional/MSBuild/Current/Bin/MSBuild.exe" \
    SpeFileViewerSolution.cdsproj /t:Rebuild /p:Configuration=Release

# Re-enable central package management
if [ -f "../../Directory.Packages.props.disabled" ]; then
    mv "../../Directory.Packages.props.disabled" "../../Directory.Packages.props"
fi
```

### Step 5: Deploy PCF to Test Dataverse

```bash
# Locate solution package
ls ./bin/Release/*.zip

# Import to test environment
pac solution import \
    --path ./bin/Release/SpeFileViewerSolution.zip \
    --environment https://org-test.crm.dynamics.com \
    --async

# Alternative: Using make.powerapps.com
# 1. Solutions > Import Solution
# 2. Upload ZIP file
# 3. Select "Upgrade" if updating existing
```

### Step 6: Verify PCF Deployment

1. Navigate to Power Apps test environment
2. Open a sprk_document record
3. Verify FileViewer control loads
4. Verify loading state appears
5. Verify preview renders
6. Verify Edit button is visible

---

## Smoke Tests

### Test 1: Health Endpoints
```bash
# All should return 200 OK
curl -s -o /dev/null -w "%{http_code}" https://spe-api-dev-67e2xz.azurewebsites.net/healthz
curl -s -o /dev/null -w "%{http_code}" https://spe-api-dev-67e2xz.azurewebsites.net/ping
curl -s -o /dev/null -w "%{http_code}" https://spe-api-dev-67e2xz.azurewebsites.net/status
```

### Test 2: FileViewer Loading
1. Open sprk_document record in test environment
2. Observe loading spinner appears
3. Preview should load within 10 seconds

### Test 3: Edit in Desktop
1. Open Word document (.docx) in FileViewer
2. Click "Edit in Desktop" button
3. Microsoft Word should open with the document
4. Make and save a change

### Test 4: Different File Types
- [ ] Word (.docx) - ms-word: protocol
- [ ] Excel (.xlsx) - ms-excel: protocol
- [ ] PowerPoint (.pptx) - ms-powerpoint: protocol
- [ ] PDF (.pdf) - Edit button disabled/hidden

---

## Rollback Procedure

### BFF Rollback
```bash
# Option A: Swap to previous deployment slot (if using slots)
az webapp deployment slot swap \
    --resource-group rg-spaarke-test \
    --name spe-api-dev-67e2xz \
    --slot staging \
    --target-slot production

# Option B: Redeploy previous version
# Use previous build artifacts from CI/CD
```

### PCF Rollback
```bash
# Import previous version of solution
pac solution import \
    --path ./previous/SpeFileViewerSolution_v1.0.0.zip \
    --environment https://org-test.crm.dynamics.com
```

---

## Verification Checklist

After deployment, verify:

| Check | Status |
|-------|--------|
| BFF /healthz returns 200 | [ ] |
| BFF /ping returns "pong" | [ ] |
| BFF /status returns JSON | [ ] |
| PCF loads in form | [ ] |
| Loading state visible | [ ] |
| Preview renders | [ ] |
| Edit button visible | [ ] |
| Word opens on click | [ ] |
| No console errors | [ ] |

---

## Troubleshooting

### BFF Issues

**Issue:** 503 Service Unavailable
- Check App Service is running
- Check application logs: `az webapp log tail --name spe-api-dev-67e2xz --resource-group rg-spaarke-test`

**Issue:** 401 Unauthorized on /open-links
- Verify OAuth configuration
- Check Azure AD app registration
- Verify token scopes

### PCF Issues

**Issue:** Control not loading
- Clear browser cache
- Check browser console for errors
- Verify solution imported successfully

**Issue:** Edit button not working
- Check BFF URL in PCF configuration
- Verify OAuth scopes configured
- Check network tab for API errors

---

## Next Steps

After successful deployment:
1. Document deployment results in this file
2. Mark Task 040 as complete
3. Proceed to Task 041 (Pilot Validation)
