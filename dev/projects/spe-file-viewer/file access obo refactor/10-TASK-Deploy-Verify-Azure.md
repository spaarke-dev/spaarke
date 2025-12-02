# Task 10: Deploy to Azure and Verify

**Task ID**: `10-Deploy-Verify-Azure`
**Estimated Time**: 30 minutes
**Status**: Not Started
**Dependencies**: 09-TASK-Build-Test-Local

---

## 📋 Prompt

Deploy the refactored BFF API to Azure App Service and verify the OBO flow works end-to-end with the PCF control. This is the final step to fix the "Access denied" errors.

---

## ✅ Todos

- [ ] Publish BFF API to local folder
- [ ] Create deployment ZIP
- [ ] Deploy to Azure App Service
- [ ] Verify deployment succeeded
- [ ] Test health endpoint in Azure
- [ ] Test FileAccessEndpoints from PCF control
- [ ] Monitor Azure logs for errors
- [ ] Verify OBO token exchange succeeds
- [ ] Verify user permissions are enforced
- [ ] Document success criteria

---

## 📚 Required Knowledge

### Azure Deployment Process
1. **Publish**: Compile and bundle API into `publish/` folder
2. **Compress**: Create ZIP file for deployment
3. **Deploy**: Upload ZIP to Azure App Service
4. **Verify**: Test endpoints and check logs

### Azure Resources
- **App Service**: `spe-api-dev-67e2xz.azurewebsites.net`
- **Resource Group**: `spe-infrastructure-westus2`
- **Region**: West US 2

### Expected Outcomes
- ✅ User with container access → 200 OK with preview URL
- ✅ User without access → 403 Forbidden (Graph API enforces permissions)
- ✅ Invalid document ID → 400 `"invalid_id"`
- ✅ Missing SPE pointers → 409 `"mapping_missing_drive"` or `"mapping_missing_item"`

---

## 📂 Related Files

**Deployment Files**:
- `src/api/Spe.Bff.Api/publish/` (output folder)
- `src/api/Spe.Bff.Api/spe-bff-api-deployment.zip` (deployment package)

**Azure Configuration**:
- App Settings (environment variables)
- Managed Identity (for app-only operations)
- CORS settings (for PCF)

---

## 🎯 Implementation

### 1. Publish API to Local Folder

```bash
cd c:/code_files/spaarke

# Publish in Release mode
dotnet publish src/api/Spe.Bff.Api/Spe.Bff.Api.csproj \
  --configuration Release \
  --output src/api/Spe.Bff.Api/publish
```

**Expected Output**:
```
Spe.Bff.Api -> c:\code_files\spaarke\src\api\Spe.Bff.Api\publish\
```

**Verify**:
- [ ] `publish/` folder created
- [ ] `Spe.Bff.Api.dll` exists in `publish/`
- [ ] All dependencies copied

### 2. Create Deployment ZIP

```bash
cd src/api/Spe.Bff.Api

# Remove old ZIP if exists
rm -f spe-bff-api-deployment.zip

# Create new ZIP (compress publish folder)
tar -a -cf spe-bff-api-deployment.zip -C publish .
```

**Verify**:
- [ ] `spe-bff-api-deployment.zip` created
- [ ] ZIP file size > 1 MB (contains all DLLs)

### 3. Deploy to Azure App Service

```bash
az webapp deploy \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --src-path spe-bff-api-deployment.zip \
  --type zip
```

**Expected Output**:
```
Deployment succeeded.
```

**If deployment fails**:
- Check Azure portal for error messages
- Verify resource group and app name are correct
- Ensure you're logged into Azure CLI (`az login`)

### 4. Verify Deployment Succeeded

```bash
# Check deployment status
az webapp show \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --query state
```

**Expected Output**:
```
"Running"
```

### 5. Test Health Endpoint

```bash
# Test health endpoint
curl https://spe-api-dev-67e2xz.azurewebsites.net/health
```

**Expected Response**:
```
Healthy
```

**If health check fails**:
- Check Azure App Service logs
- Verify API started successfully
- Look for startup exceptions

### 6. Monitor Azure Logs (Real-Time)

Open a terminal to monitor logs:

```bash
az webapp log tail \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz
```

**Keep this running** while testing to see real-time logs.

### 7. Test FileAccessEndpoints from PCF Control

**Prerequisites**:
- PCF control must be deployed (v1.3.0 from previous session)
- User must be logged into Dataverse
- Test document must exist with SPE pointers

**Steps**:
1. Open PowerApps portal
2. Navigate to a record with the file viewer PCF control
3. Open browser DevTools Console
4. Look for network requests to `/api/documents/{id}/preview-url`

**Expected Success** (User has access):
```
GET https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/{guid}/preview-url
Status: 200 OK
Response:
{
  "documentId": "ab1b0c34-52a5-f011-bbd3-7c1e5215b8b5",
  "previewUrl": "https://...",
  "embedUrl": "https://...",
  "correlationId": "..."
}
```

**Expected Failure** (User lacks access):
```
GET https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/{guid}/preview-url
Status: 403 Forbidden
Response:
{
  "type": "https://spaarke.com/errors/graph_error",
  "title": "Graph API Error",
  "detail": "Access denied",
  "status": 403,
  "extensions": {
    "code": "graph_error",
    "correlationId": "..."
  }
}
```

**Note**: 403 is expected and correct! It means Graph API is enforcing user permissions.

### 8. Verify OBO Token Exchange in Logs

In the log tail terminal, look for:

```
[Information] OBO Token Exchange - CCA configured with ClientId from API_APP_ID
[Information] Token Claims - aud: api://{BFF-AppId}, iss: https://sts.windows.net/{tenantId}/
[Information] Using cached Graph token (cache hit)
  OR
[Information] OBO token exchange successful
[Information] OBO token scopes: Files.ReadWrite.All, Sites.FullControl.All, ...
```

**Success Indicators**:
- ✅ OBO token exchange succeeds (no MSAL errors)
- ✅ Graph token scopes include Files.ReadWrite.All
- ✅ Cache hits for subsequent requests (performance)

**Failure Indicators**:
- ❌ `MsalServiceException: AADSTS50013` → Invalid audience (PCF sending wrong token)
- ❌ `MsalServiceException: AADSTS65001` → Consent required (missing delegated permissions)

### 9. Test Validation Errors

**Test Invalid Document ID**:
```bash
curl https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/invalid-id/preview-url \
  -H "Authorization: Bearer {token}"
```

**Expected**: 400 `"invalid_id"`

**Test Non-Existent Document**:
```bash
curl https://spe-api-dev-67e2xz.azurewebsites.net/api/documents/00000000-0000-0000-0000-000000000000/preview-url \
  -H "Authorization: Bearer {token}"
```

**Expected**: 404 `"document_not_found"`

**Test Document with Missing SPE Pointers** (if you have one):

**Expected**: 409 `"mapping_missing_drive"` or `"mapping_missing_item"`

---

## ✅ Acceptance Criteria

### Deployment Success
- [ ] `dotnet publish` succeeds
- [ ] ZIP file created successfully
- [ ] `az webapp deploy` succeeds
- [ ] App Service state is "Running"
- [ ] Health endpoint returns "Healthy"

### Functional Success (User with Access)
- [ ] PCF control displays file preview
- [ ] Network tab shows 200 OK from `/api/documents/{id}/preview-url`
- [ ] Response includes `previewUrl` and `embedUrl`
- [ ] No errors in browser console

### Functional Success (User without Access)
- [ ] Graph API returns 403 Forbidden (expected)
- [ ] Error response matches RFC 7807 format
- [ ] Error includes correlation ID
- [ ] PCF control shows appropriate error message

### Validation Success
- [ ] Invalid GUID → 400 `"invalid_id"`
- [ ] Non-existent document → 404 `"document_not_found"`
- [ ] Missing driveId → 409 `"mapping_missing_drive"`
- [ ] Missing itemId → 409 `"mapping_missing_item"`

### Performance Success
- [ ] First request: OBO token exchange succeeds (~200ms)
- [ ] Subsequent requests: Cache hit (~5ms overhead)
- [ ] Total request latency < 300ms (with cache)

### Logging Success
- [ ] OBO token exchange logged with correlation IDs
- [ ] Validation errors logged at appropriate levels
- [ ] No sensitive data (tokens, passwords) in logs

---

## 📝 Verification Checklist

| Test | Endpoint | Expected | Actual | ✅/❌ |
|------|----------|----------|--------|------|
| Health | `GET /health` | 200 "Healthy" | | |
| Invalid ID | `GET /api/documents/invalid/preview-url` | 400 `invalid_id` | | |
| Not Found | `GET /api/documents/{zero-guid}/preview-url` | 404 `document_not_found` | | |
| Valid (access) | `GET /api/documents/{valid-id}/preview-url` | 200 with URL | | |
| Valid (no access) | `GET /api/documents/{valid-id}/preview-url` | 403 Forbidden | | |
| Missing SPE | `GET /api/documents/{no-spe}/preview-url` | 409 `mapping_missing_*` | | |

---

## 🚨 Troubleshooting

### Issue 1: OBO Token Exchange Fails (AADSTS50013)
**Symptom**: `MsalServiceException: AADSTS50013: Assertion audience claim '{audience}' does not match the Realm issuer`

**Cause**: PCF is sending a Graph token instead of BFF token

**Solution**:
1. Verify PCF MSAL config uses `api://{BFF-AppId}/SDAP.Access` as scope
2. Check PCF code for hardcoded Graph scopes
3. Decode token in logs to verify audience claim

### Issue 2: Missing Delegated Permissions (AADSTS65001)
**Symptom**: `MsalServiceException: AADSTS65001: The user or administrator has not consented to use the application`

**Cause**: BFF App Registration is missing delegated permissions for Graph API

**Solution**:
1. Open Azure Portal → App Registrations → BFF API
2. Go to API Permissions
3. Add delegated permissions: Files.ReadWrite.All, Sites.FullControl.All
4. Grant admin consent

### Issue 3: Access Denied Despite Correct Permissions
**Symptom**: Graph API returns 403 even though user should have access

**Cause**: User is not a member of the container's security group

**Solution**:
1. Verify user is in the container's Azure AD security group
2. Check Graph API logs for specific error details
3. Test with a different user who has confirmed access

### Issue 4: Validation Errors Not Appearing
**Symptom**: All errors return 500 instead of specific validation codes

**Cause**: Global exception handler not catching `SdapProblemException`

**Solution**:
1. Verify global exception handler is in Program.cs
2. Check exception handler ordering (must be before UseAuthorization)
3. Verify `SdapProblemException` case is in the switch statement

---

## 🎉 Success Metrics

### Business Success
- [ ] PCF file preview works end-to-end
- [ ] Zero manual container grants needed (scalable)
- [ ] Clear error messages with correlation IDs

### Technical Success
- [ ] OBO token exchange succeeds
- [ ] Graph API enforces user permissions (403 for unauthorized)
- [ ] Validation catches invalid inputs (400/409)
- [ ] Error responses match RFC 7807

### Performance Success
- [ ] Request latency < 300ms (with cache)
- [ ] OBO token cache hit rate > 90%
- [ ] No performance degradation vs previous version

---

## 📸 Post-Deployment Checklist

- [ ] Take screenshot of successful PCF file preview
- [ ] Save Azure logs showing successful OBO token exchange
- [ ] Save example RFC 7807 error response
- [ ] Document any configuration changes made
- [ ] Update project documentation with deployment date

---

## 🔗 Related Documentation

- [00-PROJECT-OVERVIEW.md](./00-PROJECT-OVERVIEW.md) - Success criteria
- [FILE-ACCESS-OBO-REFACTOR-REVIEW.md](./FILE-ACCESS-OBO-REFACTOR-REVIEW.md) - Technical review

---

**Previous Task**: [09-TASK-Build-Test-Local.md](./09-TASK-Build-Test-Local.md)

**🎉 FINAL TASK - PROJECT COMPLETE AFTER THIS! 🎉**
