# SDAP BFF API Deployment - File Access Endpoints

**Feature**: Deploy file access endpoints to Azure App Service
**Target**: `spe-api-dev-67e2xz.azurewebsites.net`
**Date**: 2025-01-20
**Status**: Ready for deployment

---

## 📋 What's Being Deployed

### New Files
1. **[FileAccessEndpoints.cs](./Api/FileAccessEndpoints.cs)** - 3 new endpoints for SPE file access
2. **[SpeFileStoreDtos.cs](./Models/SpeFileStoreDtos.cs)** - 3 new DTO records

### Modified Files
1. **[Program.cs](./Program.cs)** - Registered `MapFileAccessEndpoints()`
2. **[UploadSessionManager.cs](./Infrastructure/Graph/UploadSessionManager.cs)** - Added WebUrl parameter (2 locations)
3. **[DriveItemOperations.cs](./Infrastructure/Graph/DriveItemOperations.cs)** - Added WebUrl parameter (2 locations)

### New API Endpoints
- `GET /api/documents/{documentId}/preview` - File preview for iframes
- `GET /api/documents/{documentId}/content` - File download/direct viewing
- `GET /api/documents/{documentId}/office?mode=view|edit` - Office web viewer

---

## 🔧 Prerequisites

### 1. Azure CLI Installed
```bash
az --version
```

### 2. Logged into Azure
```bash
az login
az account show
```

Expected output:
```json
{
  "name": "SPAARKE Subscription",
  "id": "subscription-id",
  "tenantId": "tenant-id"
}
```

### 3. Build Succeeds Locally
```bash
cd c:/code_files/spaarke/src/api/Spe.Bff.Api
dotnet build
```

Expected: `Build succeeded. 0 Error(s)`

---

## 🚀 Deployment Steps

### Step 1: Build for Release

```bash
cd c:/code_files/spaarke/src/api/Spe.Bff.Api

# Disable Directory.Packages.props temporarily
if [ -f "../../Directory.Packages.props" ]; then
    mv "../../Directory.Packages.props" "../../Directory.Packages.props.disabled"
fi

# Build for release
dotnet publish -c Release -o ./publish

# Restore Directory.Packages.props
if [ -f "../../Directory.Packages.props.disabled" ]; then
    mv "../../Directory.Packages.props.disabled" "../../Directory.Packages.props"
fi
```

**Expected Output:**
```
Spe.Bff.Api -> c:\code_files\spaarke\src\api\Spe.Bff.Api\publish\
```

---

### Step 2: Create Deployment Package

```bash
# Navigate to publish directory
cd publish

# Create zip package
tar -czf ../spe-bff-api-deployment.zip *

# Or use PowerShell
# Compress-Archive -Path * -DestinationPath ../spe-bff-api-deployment.zip -Force

cd ..
```

**Verify Package:**
```bash
ls -lh spe-bff-api-deployment.zip
```

Expected: File size ~50-100 MB

---

### Step 3: Deploy to Azure App Service

```bash
# Set variables
RESOURCE_GROUP="spaarke-dev-rg"
APP_NAME="spe-api-dev-67e2xz"
ZIP_FILE="spe-bff-api-deployment.zip"

# Deploy via Azure CLI
az webapp deployment source config-zip \
    --resource-group $RESOURCE_GROUP \
    --name $APP_NAME \
    --src $ZIP_FILE

# Or use az webapp deploy (newer command)
az webapp deploy \
    --resource-group $RESOURCE_GROUP \
    --name $APP_NAME \
    --src-path $ZIP_FILE \
    --type zip
```

**Expected Output:**
```json
{
  "active": true,
  "author": "N/A",
  "complete": true,
  "deployer": "ZipDeploy",
  "message": "Created via a push deployment",
  "status": 4,
  "status_text": ""
}
```

---

### Step 4: Verify Deployment

#### 4.1 Check App Service Status
```bash
az webapp show \
    --resource-group $RESOURCE_GROUP \
    --name $APP_NAME \
    --query state
```

Expected: `"Running"`

#### 4.2 Check Deployment Logs
```bash
az webapp log tail \
    --resource-group $RESOURCE_GROUP \
    --name $APP_NAME
```

Look for:
```
Application started. Press Ctrl+C to shut down.
Hosting environment: Production
Content root path: /home/site/wwwroot
Now listening on: http://[::]:8080
```

#### 4.3 Test Health Endpoint
```bash
curl https://spe-api-dev-67e2xz.azurewebsites.net/health
```

Expected:
```json
{
  "status": "Healthy",
  "timestamp": "2025-01-20T15:30:00Z"
}
```

#### 4.4 Test New Endpoints (With Auth)

**Preview Endpoint:**
```bash
# Get access token (use your preferred method)
TOKEN="your-bearer-token"

# Test preview endpoint
curl -X GET "https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/{documentId}/preview" \
  -H "Authorization: Bearer $TOKEN"
```

Expected: 200 OK with `previewUrl` in response

**Content Endpoint:**
```bash
curl -X GET "https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/{documentId}/content" \
  -H "Authorization: Bearer $TOKEN"
```

Expected: 200 OK with `downloadUrl` in response

---

## 🔐 Configuration Check

### Environment Variables (Azure App Service Configuration)

Verify these are configured in Azure Portal → App Service → Configuration:

```
AzureAd__TenantId = {tenant-id}
AzureAd__ClientId = {sdap-api-client-id}
AzureAd__ClientSecret = @Microsoft.KeyVault(SecretUri=https://...)

GraphApi__Scopes__0 = https://graph.microsoft.com/.default

Dataverse__Url = https://org.crm.dynamics.com
Dataverse__ClientId = {service-principal-id}
Dataverse__ClientSecret = @Microsoft.KeyVault(SecretUri=https://...)

ASPNETCORE_ENVIRONMENT = Production
```

### Update CORS (If Needed)

If you encounter CORS errors:

**Azure Portal → App Service → CORS:**
- Add: `https://org.crm.dynamics.com`
- Add: `https://org.crm4.dynamics.com` (or your Dataverse region)
- Add: `https://make.powerapps.com` (for testing)

Or via CLI:
```bash
az webapp cors add \
    --resource-group $RESOURCE_GROUP \
    --name $APP_NAME \
    --allowed-origins "https://org.crm.dynamics.com" "https://org.crm4.dynamics.com"
```

---

## 🧪 Post-Deployment Testing

### Test 1: Upload File and Preview

1. Open Matter/Project record in Dataverse
2. Click "Add Documents" ribbon button (UniversalQuickCreate PCF)
3. Upload a PDF file
4. Save and create Document record
5. Open created Document record
6. Verify file preview loads in web resource

**Expected:**
- ✅ File uploads successfully
- ✅ Document record created with `sprk_graphitemid` and `sprk_graphdriveid`
- ✅ Web resource calls `/preview` endpoint
- ✅ PDF displays in iframe

### Test 2: Download File

1. Open Document record with file
2. Click "Download File" button in web resource
3. Verify file downloads

**Expected:**
- ✅ Download URL generated
- ✅ File downloads with correct name
- ✅ File content matches original

### Test 3: Office Viewer

1. Upload Word/Excel/PowerPoint file
2. Open Document record
3. Click "Open in Office" button
4. Verify file opens in Microsoft 365 web viewer

**Expected:**
- ✅ Office viewer URL generated
- ✅ File opens in new tab
- ✅ Office web viewer renders file

---

## 🐛 Troubleshooting

### Issue: Build Fails with NU1008 Error

**Error:**
```
error NU1008: Projects that use central package version management should not define the version...
```

**Solution:**
```bash
# Disable Directory.Packages.props before building
mv c:/code_files/spaarke/Directory.Packages.props c:/code_files/spaarke/Directory.Packages.props.disabled

# Build
dotnet publish -c Release

# Restore
mv c:/code_files/spaarke/Directory.Packages.props.disabled c:/code_files/spaarke/Directory.Packages.props
```

---

### Issue: Deployment Shows "Conflict" Status

**Symptom:**
```
Deployment Status: 409 Conflict
```

**Cause:** Previous deployment still in progress or locked

**Solution:**
```bash
# Stop app service
az webapp stop --resource-group $RESOURCE_GROUP --name $APP_NAME

# Wait 30 seconds
sleep 30

# Deploy again
az webapp deploy --resource-group $RESOURCE_GROUP --name $APP_NAME --src-path spe-bff-api-deployment.zip --type zip

# Start app service
az webapp start --resource-group $RESOURCE_GROUP --name $APP_NAME
```

---

### Issue: Endpoint Returns 401 Unauthorized

**Symptom:**
```json
{
  "status": 401,
  "title": "Unauthorized"
}
```

**Possible Causes:**
1. Missing or invalid Bearer token
2. Token expired
3. Azure AD app registration misconfigured

**Debug Steps:**
```bash
# Check Azure AD configuration
az ad app show --id {client-id}

# Verify API permissions
az ad app permission list --id {client-id}

# Test with fresh token
# Get token via Azure CLI
TOKEN=$(az account get-access-token --resource https://spe-api-dev-67e2xz.azurewebsites.net --query accessToken -o tsv)

# Test endpoint
curl -X GET "https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/{id}/preview" \
  -H "Authorization: Bearer $TOKEN"
```

---

### Issue: Graph API Returns 403 Forbidden

**Symptom:**
```json
{
  "status": 500,
  "title": "Graph API Error",
  "detail": "Failed to generate preview: Forbidden"
}
```

**Cause:** Service principal lacks permissions to Graph API

**Solution:**
```bash
# Grant Graph API permissions to service principal
az ad app permission add \
    --id {client-id} \
    --api 00000003-0000-0000-c000-000000000000 \
    --api-permissions 01d4889c-1287-42c6-ac1f-5d1e02578ef6=Scope  # Files.Read.All (delegated)

# Grant admin consent
az ad app permission admin-consent --id {client-id}
```

---

## 📊 Monitoring

### Application Insights Queries

**Preview Endpoint Usage:**
```kusto
requests
| where url contains "/api/documents/" and url contains "/preview"
| summarize count() by resultCode, bin(timestamp, 1h)
| render timechart
```

**Error Rate:**
```kusto
requests
| where url contains "/api/documents/"
| summarize ErrorRate = countif(resultCode >= 400) * 100.0 / count() by bin(timestamp, 5m)
| render timechart
```

**Average Response Time:**
```kusto
requests
| where url contains "/api/documents/"
| summarize avg(duration) by operation_Name, bin(timestamp, 5m)
| render timechart
```

---

## ✅ Deployment Verification Checklist

- [ ] Build succeeds locally (Debug)
- [ ] Build succeeds for Release
- [ ] Deployment package created (~50-100 MB)
- [ ] Deployed to Azure App Service
- [ ] App Service status: Running
- [ ] Health endpoint returns 200 OK
- [ ] Preview endpoint accessible (with auth)
- [ ] Content endpoint accessible (with auth)
- [ ] Office endpoint accessible (with auth)
- [ ] CORS configured for Dataverse origins
- [ ] Application Insights showing requests
- [ ] No errors in App Service logs
- [ ] Integration test: Upload → Preview → Download works end-to-end

---

## 📚 Related Documentation

- [FILE-ACCESS-ENDPOINTS.md](../../controls/UniversalQuickCreate/docs/FILE-ACCESS-ENDPOINTS.md) - API usage documentation
- [Azure App Service Deployment](https://learn.microsoft.com/en-us/azure/app-service/deploy-zip)
- [Azure CLI Reference](https://learn.microsoft.com/en-us/cli/azure/webapp/deployment/source)

---

**Last Updated**: 2025-01-20
**Deployed By**: [Your Name]
**Deployment Date**: [Date]
**Build Version**: [Version from git commit or build number]
