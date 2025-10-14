# POSTMAN File Upload Test Guide - SDAP V2

**Purpose**: Test end-to-end file upload to SharePoint Embedded via BFF API using Postman
**Date**: 2025-10-14
**Environment**: DEV (spe-api-dev-67e2xz.azurewebsites.net)

---

## Prerequisites

**Test Data** (from Task 5.5):
- **Matter ID**: `3a785f76-c773-f011-b4cb-6045bdd8b757`
- **Container ID**: `b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50`
- **Dataverse URL**: `https://org7fbec2a1.crm.dynamics.com`
- **BFF API URL**: `https://spe-api-dev-67e2xz.azurewebsites.net`
- **BFF API App ID**: `1e40baad-e065-4aea-a8d4-4b7ab273458c`

---

## Step 1: Get Access Token in Postman

### Option A: Using Postman's Built-in OAuth 2.0 (RECOMMENDED)

**1.1 Create New Request**:
- Click "New" → "HTTP Request"
- Name it: "SDAP - Upload File to Container"
- Save to a collection: "SDAP V2 Testing"

**1.2 Configure Authorization**:
1. Go to the **Authorization** tab
2. Type: Select **OAuth 2.0**
3. Add auth data to: **Request Headers**
4. Click **Configure New Token** button

**1.3 OAuth 2.0 Configuration**:

| Field | Value |
|-------|-------|
| Token Name | `SDAP BFF API Token` |
| Grant Type | **Authorization Code (With PKCE)** |
| Callback URL | `https://oauth.pstmn.io/v1/callback` |
| Auth URL | `https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/oauth2/v2.0/authorize` |
| Access Token URL | `https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/oauth2/v2.0/token` |
| Client ID | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| Client Secret | (leave blank for PKCE) |
| Scope | `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation` |
| State | `12345` (any random string) |
| Client Authentication | **Send as Basic Auth header** |

**1.4 Get Token**:
1. Click **Get New Access Token**
2. Browser window opens → Sign in with your Dataverse user account
3. Consent screen appears (first time only) → Click **Accept**
4. Token appears in Postman → Click **Use Token**

**Expected Result**:
- ✅ Token acquired successfully
- ✅ Token appears in Authorization header preview
- ✅ Token starts with `eyJ0eXAiOiJKV1QiLCJhbGc...`

### Option B: Using Azure CLI (If Option A Fails)

**1.1 Get Token via CLI**:
```powershell
# Get token for BFF API
$token = az account get-access-token --resource "api://1e40baad-e065-4aea-a8d4-4b7ab273458c" --query accessToken -o tsv

# Copy to clipboard
$token | Set-Clipboard

Write-Host "Token copied to clipboard!"
Write-Host "Token preview: $($token.Substring(0,50))..."
```

**1.2 Configure in Postman**:
1. Authorization tab → Type: **Bearer Token**
2. Token field: Paste the token from clipboard
3. Click outside the field to apply

---

## Step 2: Upload Small File (<250MB)

**2.1 Request Configuration**:

| Setting | Value |
|---------|-------|
| Method | **PUT** |
| URL | `https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/containers/{containerId}/files/{path}` |
| Authorization | OAuth 2.0 (configured in Step 1) |

**2.2 Replace Path Variables**:

Replace `{containerId}` with:
```
b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50
```

Replace `{path}` with your test file path:
```
Test Documents/postman-upload-test.txt
```

**Full URL Example**:
```
https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/containers/b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50/files/Test%20Documents/postman-upload-test.txt
```

**2.3 Request Headers** (should be automatic):
- `Authorization: Bearer {token}` (from Step 1)
- `Content-Type: text/plain` (or appropriate MIME type)

**2.4 Request Body**:
1. Go to **Body** tab
2. Select **binary**
3. Click **Select File** button
4. Choose a small test file (e.g., text file, image, PDF)

**2.5 Send Request**:
1. Click **Send** button
2. Wait for response (should be <5 seconds for small files)

**Expected Response** (HTTP 200 OK):
```json
{
  "id": "01ABCDEF1234567890",
  "name": "postman-upload-test.txt",
  "size": 12345,
  "webUrl": "https://...",
  "createdDateTime": "2025-10-14T19:30:00Z",
  "lastModifiedDateTime": "2025-10-14T19:30:00Z",
  "createdBy": {
    "user": {
      "displayName": "Your Name"
    }
  }
}
```

**Success Indicators**:
- ✅ HTTP Status: **200 OK**
- ✅ Response contains `id` field (Drive Item ID)
- ✅ Response contains `webUrl` (link to file in SPE)
- ✅ Response contains `size` matching your file size

---

## Step 3: Verify File in SharePoint Embedded

**3.1 List Files in Container**:

**Request**:
- Method: **GET**
- URL: `https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/containers/{containerId}/files`
- Authorization: Same OAuth 2.0 token

**Expected Response**:
```json
{
  "value": [
    {
      "id": "01ABCDEF1234567890",
      "name": "postman-upload-test.txt",
      "folder": { "childCount": 0 },
      "size": 12345,
      "webUrl": "https://..."
    }
  ]
}
```

**3.2 Verify in Dataverse** (Optional):

Query the Matter to confirm Container ID:
- Method: **GET**
- URL: `https://org7fbec2a1.crm.dynamics.com/api/data/v9.2/sprk_matters(3a785f76-c773-f011-b4cb-6045bdd8b757)?$select=sprk_matterid,sprk_containerid`
- Authorization: Separate token for Dataverse (scope: `https://org7fbec2a1.crm.dynamics.com/.default`)

**Expected Response**:
```json
{
  "sprk_matterid": "3a785f76-c773-f011-b4cb-6045bdd8b757",
  "sprk_containerid": "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"
}
```

---

## Step 4: Test Large File Upload (>250MB) - Chunked Upload

**4.1 Create Upload Session**:

**Request**:
- Method: **POST**
- URL: `https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/{containerId}/upload-session?path=Test%20Documents/large-file.pdf&conflictBehavior=rename`
- Authorization: OAuth 2.0 token

**Expected Response** (HTTP 200 OK):
```json
{
  "uploadUrl": "https://graph.microsoft.com/v1.0/...",
  "expirationDateTime": "2025-10-14T20:30:00Z",
  "nextExpectedRanges": ["0-"]
}
```

**4.2 Upload Chunk**:

**Request**:
- Method: **PUT**
- URL: `https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/upload-session/chunk`
- Authorization: OAuth 2.0 token
- Headers:
  - `Upload-Session-Url: {uploadUrl from 4.1}`
  - `Content-Range: bytes 0-327679/10485760` (example: 320KB chunk of 10MB file)
- Body: **binary** (select 320KB chunk of file)

**Expected Response** (HTTP 202 Accepted):
```json
{
  "expirationDateTime": "2025-10-14T20:30:00Z",
  "nextExpectedRanges": ["327680-"]
}
```

**Note**: For simplicity, test small files first. Chunked uploads require multiple requests.

---

## Step 5: Test Error Scenarios

### Test 5.1: Invalid Container ID (404)

**Request**:
- URL: `https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/containers/invalid-container-id/files/test.txt`
- Method: PUT
- Body: (any file)

**Expected Response** (HTTP 404):
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
  "title": "error",
  "status": 404,
  "detail": "Container not found or access denied",
  "graphErrorCode": "itemNotFound",
  "graphRequestId": "abc-123-def-456"
}
```

### Test 5.2: Missing Authorization (401)

**Request**:
- Same URL as Step 2
- Authorization: **No Auth** (remove token)

**Expected Response** (HTTP 401):
```
Unauthorized
```

### Test 5.3: Invalid Path Characters (400)

**Request**:
- URL: `https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/containers/{containerId}/files/test/../../../etc/passwd`
- Method: PUT

**Expected Response** (HTTP 400):
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Validation Error",
  "status": 400,
  "detail": "path must not contain '..'"
}
```

### Test 5.4: Rate Limiting (429)

**Request**:
- Make 100+ rapid requests to same endpoint
- Use Postman Collection Runner with 100 iterations

**Expected Response** (after ~20 requests for write operations):
```json
{
  "type": "https://tools.ietf.org/html/rfc6585#section-4",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Rate limit exceeded. Please retry after the specified duration.",
  "retryAfter": "60 seconds"
}
```

**Headers**:
```
Retry-After: 60
```

---

## Step 6: Cache Performance Testing

**Goal**: Verify token caching reduces latency

**6.1 First Request** (Cache MISS):
1. Clear token cache (restart BFF API or wait 55 minutes)
2. Upload file (Step 2)
3. Check response time in Postman (bottom right)
4. **Expected**: ~250-300ms (includes OBO exchange)

**6.2 Second Request** (Cache HIT):
1. Immediately upload another file (within 55 minutes)
2. Check response time
3. **Expected**: ~55-105ms (cached Graph token, no OBO)

**6.3 Verify Cache Logs** (Optional):

PowerShell command to check logs:
```powershell
az webapp log tail `
  --name spe-api-dev-67e2xz `
  --resource-group spe-infrastructure-westus2 `
  --filter "Cache" 2>&1 | Select-String "Cache HIT|Cache MISS"
```

**Expected Output**:
```
Cache MISS for token hash 5b3a1f2e... (OBO exchange performed)
Cache HIT for token hash 5b3a1f2e... (5ms, 97% overhead reduction)
Cache HIT for token hash 5b3a1f2e... (4ms, 97% overhead reduction)
```

---

## Troubleshooting

### Error: "AADSTS65001 - Admin consent required"

**Cause**: Using Azure CLI client ID instead of direct authentication

**Solution**: Use Postman's OAuth 2.0 configuration (Step 1, Option A)
- Postman authenticates as **user** (you), not as Azure CLI
- User delegation doesn't require admin consent
- BFF API performs OBO on your behalf

### Error: "AADSTS65005 - Scope doesn't exist"

**Cause**: Wrong scope name - used `Files.ReadWrite.All` instead of `user_impersonation`

**Solution**: The BFF API exposes `user_impersonation` scope, NOT `Files.ReadWrite.All`. Use:
```
api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation
```

**Why?** The BFF API uses `Files.ReadWrite.All` to call Microsoft Graph on your behalf (OBO pattern). You authenticate to the BFF API itself using the `user_impersonation` scope.

### Error: "Container not found"

**Cause**: Wrong Container ID or no permission

**Solutions**:
1. Verify Container ID matches: `b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50`
2. Verify you have access to Matter in Dataverse
3. Check BFF API has `FileStorageContainer.Selected` permission

### Error: "Callback URL not whitelisted"

**Cause**: Postman callback URL not registered in app registration

**Solution**:
1. Go to Azure Portal → App Registrations → BFF API (1e40baad...)
2. Authentication → Redirect URIs
3. Add: `https://oauth.pstmn.io/v1/callback`
4. Save and retry in Postman

### Error: HTTP 404 - Endpoint not found

**Cause**: Wrong URL or endpoint path

**Solution**: Verify URL format:
```
PUT /api/obo/containers/{containerId}/files/{path}
```

**NOT** (old V1 format):
```
PUT /api/obo/drives/{driveId}/upload?fileName={name}
```

---

## Success Criteria

**Phase 5 - Task 5.9 (Production Validation) Requirements**:

| Test | Status | Evidence |
|------|--------|----------|
| Upload small file | ✅ | HTTP 200, file ID returned |
| List files in container | ✅ | File appears in listing |
| Download file | ✅ | Content matches upload |
| Delete file | ✅ | HTTP 204, file removed |
| Invalid container ID | ✅ | HTTP 404 with clear message |
| Missing authorization | ✅ | HTTP 401 |
| Invalid path | ✅ | HTTP 400 with validation error |
| Rate limiting | ✅ | HTTP 429 with Retry-After |
| Cache performance | ✅ | 2nd request <50% of 1st request time |

**Overall**: 9/9 tests pass → Phase 5 COMPLETE → READY FOR PRODUCTION DEPLOYMENT

---

## Postman Collection Export (Optional)

**Create Collection**:
1. Save all requests to collection: "SDAP V2 - Integration Tests"
2. Add folder: "File Operations"
3. Add folder: "Error Scenarios"
4. Add folder: "Cache Performance"

**Configure Collection Variables**:
- `bff_api_url`: `https://spe-api-dev-67e2xz.azurewebsites.net`
- `container_id`: `b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50`
- `matter_id`: `3a785f76-c773-f011-b4cb-6045bdd8b757`
- `tenant_id`: `a221a95e-6abc-4434-aecc-e48338a1b2f2`
- `client_id`: `1e40baad-e065-4aea-a8d4-4b7ab273458c`

**Export Collection**:
1. Right-click collection → Export
2. Save as: `SDAP-V2-Integration-Tests.postman_collection.json`
3. Commit to repo: `dev/projects/sdap_V2/test-evidence/`

---

## Next Steps After Testing

**If All Tests Pass**:
1. ✅ Mark Task 5.9 (Production Validation) as COMPLETE
2. ✅ Update Phase 5 Summary document
3. ✅ Create deployment checklist (Phase 6)
4. ✅ Deploy PCF control to Dataverse
5. ✅ Deploy BFF API to Production App Service

**If Tests Fail**:
1. Document error messages and HTTP status codes
2. Check Application Insights for detailed logs
3. Verify app registration configuration
4. Verify Container Type permissions
5. Create bug report with reproduction steps

---

**Guide Created**: 2025-10-14
**Purpose**: Postman testing for SDAP V2 file upload validation
**Target**: Task 5.9 (Production Validation) completion
