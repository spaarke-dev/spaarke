# End-to-End SharePoint Embedded Testing Guide

## Overview

This guide provides comprehensive testing of the **complete SharePoint Embedded (SPE) stack**:
- **PCF Control** (UniversalDatasetGrid) → **SDAP BFF API** → **Microsoft Graph API** → **SharePoint Embedded**

It includes:
1. Pre-deployment PCF-to-BFF integration testing (without building PCF control)
2. SDAP BFF API endpoint testing
3. End-to-end SPE file operations (upload, download, delete, replace)
4. Cache performance verification (Phase 4)
5. Authentication flow testing (MSAL → OBO → Graph)

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ USER (Dataverse Model-Driven App)                                          │
└───────────────────────────────────┬─────────────────────────────────────────┘
                                    │
                                    │ 1. User Action (Upload/Download/Delete)
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ PCF CONTROL: UniversalDatasetGrid                                          │
│ Location: src/controls/UniversalDatasetGrid/                               │
│                                                                             │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │ MSAL.js (Browser)                                                    │  │
│  │ - Client: 170c98e1-92b9-47ca-b3e7-e9e13f4f6e13 (PCF Client App)     │  │
│  │ - Acquires user token (delegated permissions)                        │  │
│  └────────────────────────────────┬─────────────────────────────────────┘  │
│                                   │ 2. User JWT Token                       │
│  ┌────────────────────────────────▼─────────────────────────────────────┐  │
│  │ SdapApiClient.ts                                                     │  │
│  │ - Calls SDAP BFF API with Bearer token                              │  │
│  │ - Endpoints: /api/obo/drives/{driveId}/upload, etc.                 │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
└───────────────────────────────────┬─────────────────────────────────────────┘
                                    │ 3. HTTP Request (Authorization: Bearer <user_token>)
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ SDAP BFF API (Backend for Frontend)                                        │
│ Deployed: https://spe-api-dev-67e2xz.azurewebsites.net                     │
│ Location: src/api/Spe.Bff.Api/                                             │
│                                                                             │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │ PHASE 1: JWT Validation (Microsoft.Identity.Web)                    │  │
│  │ - Validates user token from PCF                                      │  │
│  │ - ClientId: 1e40baad-e065-4aea-a8d4-4b7ab273458c (BFF API)          │  │
│  │ - Audience: api://1e40baad-...                                      │  │
│  └────────────────────────────────┬─────────────────────────────────────┘  │
│                                   │ 4. Token Validated ✅                   │
│  ┌────────────────────────────────▼─────────────────────────────────────┐  │
│  │ PHASE 2: Endpoints (OBOEndpoints.cs)                                │  │
│  │ - UploadFile, DownloadFile, DeleteFile                              │  │
│  └────────────────────────────────┬─────────────────────────────────────┘  │
│                                   │ 5. Call SpeFileStore                    │
│  ┌────────────────────────────────▼─────────────────────────────────────┐  │
│  │ PHASE 3: SpeFileStore (Facade)                                      │  │
│  │ - Coordinates operations: DriveItemOperations, UploadSessionManager │  │
│  └────────────────────────────────┬─────────────────────────────────────┘  │
│                                   │ 6. Get Graph Client                     │
│  ┌────────────────────────────────▼─────────────────────────────────────┐  │
│  │ PHASE 4: GraphClientFactory (with Cache!)                           │  │
│  │                                                                      │  │
│  │  A. Check GraphTokenCache (SHA256 hash of user token)               │  │
│  │     ├─ Cache HIT (~5ms) → Use cached Graph token                    │  │
│  │     └─ Cache MISS (~200ms) → Perform OBO exchange                   │  │
│  │                                                                      │  │
│  │  B. OBO Flow (On-Behalf-Of)                                         │  │
│  │     - Uses MSAL.NET ConfidentialClientApplication                   │  │
│  │     - Exchanges user token for Graph token                          │  │
│  │     - ClientId: 1e40baad-... (BFF API)                              │  │
│  │     - ClientSecret: From Key Vault                                  │  │
│  │                                                                      │  │
│  │  C. Cache Token (55-minute TTL)                                     │  │
│  │     - Store in Redis (or in-memory for dev)                         │  │
│  │     - Key: sdap:graph:token:<sha256_hash>                           │  │
│  │                                                                      │  │
│  │  D. Create GraphServiceClient                                       │  │
│  │     - Authenticated with Graph token                                │  │
│  └────────────────────────────────┬─────────────────────────────────────┘  │
│                                   │ 7. Graph Token (from OBO or cache)      │
└───────────────────────────────────┬─────────────────────────────────────────┘
                                    │ 8. HTTP Request to Microsoft Graph
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ MICROSOFT GRAPH API                                                         │
│ https://graph.microsoft.com/v1.0/                                           │
│                                                                             │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │ Endpoints Used:                                                      │  │
│  │ - PUT /drives/{driveId}/root:/{fileName}:/content (upload)          │  │
│  │ - GET /drives/{driveId}/items/{itemId}/content (download)           │  │
│  │ - DELETE /drives/{driveId}/items/{itemId} (delete)                  │  │
│  └────────────────────────────────┬─────────────────────────────────────┘  │
└───────────────────────────────────┬─────────────────────────────────────────┘
                                    │ 9. Call SharePoint Embedded
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ SHAREPOINT EMBEDDED (SPE)                                                  │
│ Storage Backend: Azure Blob Storage (Container Type)                       │
│                                                                             │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │ Container Type: SPE_CONTAINER_TYPE (from environment)                │  │
│  │ Container: Specific container instance (driveId)                     │  │
│  │ File: Stored as blob in Azure Storage                               │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │ 10. Response (Success/Error)
                                    ▼
                              [Back to User]
```

---

## Prerequisites

### Required Information

You'll need the following information (check appsettings or Azure portal):

```bash
# API Configuration
API_BASE_URL="https://spe-api-dev-67e2xz.azurewebsites.net"
API_APP_ID="1e40baad-e065-4aea-a8d4-4b7ab273458c"

# PCF Client Configuration (for MSAL testing)
PCF_CLIENT_ID="170c98e1-92b9-47ca-b3e7-e9e13f4f6e13"

# SharePoint Embedded
CONTAINER_TYPE_ID="8a6ce34c-6055-4681-8f87-2f4f9f921c06"
DRIVE_ID="<get_from_dataverse_or_api>"

# Tenant
TENANT_ID="a221a95e-6abc-4434-aecc-e48338a1b2f2"
```

### Required Tools

```bash
# Azure CLI (for token acquisition and API testing)
az login

# PowerShell PAC CLI (for Dataverse auth)
pac auth create --environment <your-env-url>

# cURL or Postman (for API testing)

# jq (optional, for JSON parsing)
```

---

## Part 1: Pre-Deployment PCF Integration Testing

**Goal**: Test PCF → BFF API integration **without building the PCF control**

This verifies:
- MSAL token acquisition works
- BFF API accepts tokens from PCF client app
- OBO flow succeeds
- Graph API calls succeed
- SPE operations work

### Step 1.1: Get Delegated Token (Simulate PCF Auth)

The PCF control uses `MSAL.js` to acquire a delegated token for the **PCF client app** (170c98e1...).

We can simulate this using Azure CLI:

```bash
# Acquire token for PCF client app (delegated permissions)
# This simulates what MSAL.js does in the browser

az account get-access-token \
  --resource api://1e40baad-e065-4aea-a8d4-4b7ab273458c \
  --query accessToken -o tsv > /tmp/pcf-token.txt

PCF_TOKEN=$(cat /tmp/pcf-token.txt)

echo "PCF Token Length: ${#PCF_TOKEN}"
echo "Token acquired ✅"
```

**Expected**: Token length ~2500-3000 characters

### Step 1.2: Test API Health (No Auth Required)

```bash
curl -s https://spe-api-dev-67e2xz.azurewebsites.net/ping | jq
```

**Expected Response**:
```json
{
  "service": "Spe.Bff.Api",
  "version": "1.0.0",
  "environment": "Development",
  "timestamp": "2025-10-14T15:00:00Z"
}
```

### Step 1.3: Test /api/me Endpoint (Simulates PCF → BFF Flow)

This endpoint uses OBO to exchange the user token for a Graph token and calls Microsoft Graph.

```bash
curl -s -w "\nHTTP Status: %{http_code}\n" \
  -H "Authorization: Bearer $PCF_TOKEN" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/me | jq
```

**Expected Response** (if successful):
```json
{
  "id": "user-id",
  "displayName": "Your Name",
  "mail": "yourname@example.com",
  "userPrincipalName": "yourname@example.com"
}
HTTP Status: 200
```

**If 401 Unauthorized**:
- Check Azure AD app registration:
  - PCF client app (170c98e1...) has API permissions to BFF API (api://1e40baad-...)
  - BFF API (1e40baad...) exposes scope (e.g., `user_impersonation`)
  - Admin consent granted

### Step 1.4: Test Cache Performance (Phase 4 Verification)

Make **5 consecutive requests** to observe cache behavior:

```bash
# Request 1: Cache MISS (first request, OBO exchange ~200ms)
echo "Request 1 (Cache MISS expected):"
time curl -s -H "Authorization: Bearer $PCF_TOKEN" \
  https://spe-api-dev-67e2xz.azurewebsites.net/api/me > /dev/null

# Request 2-5: Cache HIT (subsequent requests, ~5ms for cache lookup)
for i in {2..5}; do
  echo "Request $i (Cache HIT expected):"
  time curl -s -H "Authorization: Bearer $PCF_TOKEN" \
    https://spe-api-dev-67e2xz.azurewebsites.net/api/me > /dev/null
  sleep 0.5
done
```

**Expected Timing**:
- Request 1: ~1-2 seconds total (includes OBO exchange ~200ms + Graph API call)
- Requests 2-5: ~0.5-1 second total (cache hit ~5ms + Graph API call)

**To verify cache in logs**:
```bash
az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
```

**Look for**:
- `Cache MISS for token hash ...` (first request)
- `Cache HIT for token hash ...` (subsequent requests)
- `Using cached Graph token (cache hit)` (GraphClientFactory)

---

## Part 2: SDAP BFF API Endpoint Testing

Test all SPE file operation endpoints.

### Step 2.1: List Containers (if available)

**Note**: This endpoint requires `canlistcontainers` policy (admin permission).

```bash
CONTAINER_TYPE_ID="8a6ce34c-6055-4681-8f87-2f4f9f921c06"

curl -s -w "\nHTTP Status: %{http_code}\n" \
  -H "Authorization: Bearer $PCF_TOKEN" \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/containers?containerTypeId=$CONTAINER_TYPE_ID" | jq
```

**Expected Response** (if authorized):
```json
[
  {
    "id": "drive-id-1",
    "displayName": "Matter 001 - Contract Files",
    "containerTypeId": "8a6ce34c-6055-4681-8f87-2f4f9f921c06",
    "createdDateTime": "2025-01-01T00:00:00Z"
  },
  // ... more containers
]
HTTP Status: 200
```

**If 403 Forbidden**: You don't have admin permissions. Get `DRIVE_ID` from Dataverse or ask admin.

### Step 2.2: Get Drive ID from Dataverse

If you can't list containers, get the Drive ID from Dataverse:

```bash
# Using PAC CLI
pac data read --entity-logical-name sprk_matter --id <matter-guid> --columns sprk_driveid

# Or use API (requires Dataverse auth)
DATAVERSE_URL="https://your-org.crm.dynamics.com"
DATAVERSE_TOKEN=$(az account get-access-token --resource $DATAVERSE_URL --query accessToken -o tsv)

curl -s -H "Authorization: Bearer $DATAVERSE_TOKEN" \
  "$DATAVERSE_URL/api/data/v9.2/sprk_matters(<matter-guid>)?$select=sprk_driveid" | jq -r '.sprk_driveid'
```

Save the Drive ID:
```bash
DRIVE_ID="<your-drive-id>"
```

### Step 2.3: Upload File (Test SPE Write)

```bash
# Create test file
echo "Hello from SPE test! $(date)" > /tmp/test-file.txt

# Upload to SharePoint Embedded
FILE_NAME="test-upload-$(date +%s).txt"

curl -s -w "\nHTTP Status: %{http_code}\n" \
  -X PUT \
  -H "Authorization: Bearer $PCF_TOKEN" \
  -H "Content-Type: text/plain" \
  --data-binary @/tmp/test-file.txt \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$DRIVE_ID/upload?fileName=$FILE_NAME" | jq
```

**Expected Response**:
```json
{
  "id": "item-id-abc123",
  "name": "test-upload-1728920400.txt",
  "size": 45,
  "createdDateTime": "2025-10-14T15:00:00Z",
  "webUrl": "https://...",
  "downloadUrl": "https://..."
}
HTTP Status: 200
```

**Save the Item ID** for subsequent tests:
```bash
ITEM_ID="<id-from-response>"
```

### Step 2.4: Download File (Test SPE Read)

```bash
curl -s -w "\nHTTP Status: %{http_code}\n" \
  -H "Authorization: Bearer $PCF_TOKEN" \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$DRIVE_ID/items/$ITEM_ID/content" \
  -o /tmp/downloaded-file.txt

echo "Downloaded file content:"
cat /tmp/downloaded-file.txt
```

**Expected**: File content matches uploaded content.

### Step 2.5: Delete File (Test SPE Delete)

```bash
curl -s -w "\nHTTP Status: %{http_code}\n" \
  -X DELETE \
  -H "Authorization: Bearer $PCF_TOKEN" \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$DRIVE_ID/items/$ITEM_ID"
```

**Expected Response**:
```
HTTP Status: 204
```

(204 No Content = success)

---

## Part 3: Test PCF Client Integration (Without Building)

Verify the **SdapApiClient** logic using Node.js (simulates browser environment).

### Step 3.1: Create Test Script

Create `test-pcf-client.js`:

```javascript
/**
 * Test PCF SdapApiClient integration with BFF API
 * Simulates browser environment without building PCF control
 */

// Simulate browser fetch API
const fetch = require('node-fetch');

// Configuration (from PCF control)
const API_BASE_URL = 'https://spe-api-dev-67e2xz.azurewebsites.net';
const USER_TOKEN = process.env.PCF_TOKEN; // From Azure CLI

if (!USER_TOKEN) {
    console.error('Error: PCF_TOKEN environment variable not set');
    console.log('Run: export PCF_TOKEN=$(az account get-access-token --resource api://1e40baad-e065-4aea-a8d4-4b7ab273458c --query accessToken -o tsv)');
    process.exit(1);
}

// Simulate SdapApiClient.uploadFile()
async function testUpload(driveId, fileName, fileContent) {
    console.log(`\n=== Test Upload: ${fileName} ===`);
    console.log(`Drive ID: ${driveId}`);
    console.log(`File Size: ${fileContent.length} bytes`);

    const url = `${API_BASE_URL}/api/obo/drives/${encodeURIComponent(driveId)}/upload?fileName=${encodeURIComponent(fileName)}`;

    try {
        const response = await fetch(url, {
            method: 'PUT',
            headers: {
                'Authorization': `Bearer ${USER_TOKEN}`,
                'Content-Type': 'text/plain'
            },
            body: fileContent
        });

        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(`Upload failed: ${response.status} ${errorText}`);
        }

        const result = await response.json();
        console.log('✅ Upload successful:', result);
        return result;

    } catch (error) {
        console.error('❌ Upload failed:', error.message);
        throw error;
    }
}

// Simulate SdapApiClient.downloadFile()
async function testDownload(driveId, itemId) {
    console.log(`\n=== Test Download ===`);
    console.log(`Drive ID: ${driveId}`);
    console.log(`Item ID: ${itemId}`);

    const url = `${API_BASE_URL}/api/obo/drives/${encodeURIComponent(driveId)}/items/${encodeURIComponent(itemId)}/content`;

    try {
        const response = await fetch(url, {
            method: 'GET',
            headers: {
                'Authorization': `Bearer ${USER_TOKEN}`
            }
        });

        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(`Download failed: ${response.status} ${errorText}`);
        }

        const content = await response.text();
        console.log('✅ Download successful');
        console.log('Content:', content);
        return content;

    } catch (error) {
        console.error('❌ Download failed:', error.message);
        throw error;
    }
}

// Simulate SdapApiClient.deleteFile()
async function testDelete(driveId, itemId) {
    console.log(`\n=== Test Delete ===`);
    console.log(`Drive ID: ${driveId}`);
    console.log(`Item ID: ${itemId}`);

    const url = `${API_BASE_URL}/api/obo/drives/${encodeURIComponent(driveId)}/items/${encodeURIComponent(itemId)}`;

    try {
        const response = await fetch(url, {
            method: 'DELETE',
            headers: {
                'Authorization': `Bearer ${USER_TOKEN}`
            }
        });

        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(`Delete failed: ${response.status} ${errorText}`);
        }

        console.log('✅ Delete successful');

    } catch (error) {
        console.error('❌ Delete failed:', error.message);
        throw error;
    }
}

// Main test
async function runTests() {
    const DRIVE_ID = process.env.DRIVE_ID;

    if (!DRIVE_ID) {
        console.error('Error: DRIVE_ID environment variable not set');
        console.log('Get Drive ID from Dataverse or API, then run:');
        console.log('export DRIVE_ID=<your-drive-id>');
        process.exit(1);
    }

    console.log('='.repeat(80));
    console.log('PCF Client Integration Test (Simulated)');
    console.log('='.repeat(80));

    try {
        // Test 1: Upload
        const fileName = `pcf-test-${Date.now()}.txt`;
        const fileContent = `Test file from PCF client simulation\nTimestamp: ${new Date().toISOString()}`;

        const uploadResult = await testUpload(DRIVE_ID, fileName, fileContent);
        const itemId = uploadResult.id;

        // Test 2: Download
        await testDownload(DRIVE_ID, itemId);

        // Test 3: Delete
        await testDelete(DRIVE_ID, itemId);

        console.log('\n' + '='.repeat(80));
        console.log('✅ All tests passed! PCF client integration verified.');
        console.log('='.repeat(80));

    } catch (error) {
        console.log('\n' + '='.repeat(80));
        console.error('❌ Tests failed:', error.message);
        console.log('='.repeat(80));
        process.exit(1);
    }
}

runTests();
```

### Step 3.2: Run PCF Client Test

```bash
# Install node-fetch (if not already installed)
npm install node-fetch@2

# Set environment variables
export PCF_TOKEN=$(az account get-access-token \
  --resource api://1e40baad-e065-4aea-a8d4-4b7ab273458c \
  --query accessToken -o tsv)

export DRIVE_ID="<your-drive-id>"

# Run test
node test-pcf-client.js
```

**Expected Output**:
```
================================================================================
PCF Client Integration Test (Simulated)
================================================================================

=== Test Upload: pcf-test-1728920400.txt ===
Drive ID: <drive-id>
File Size: 75 bytes
✅ Upload successful: { id: '...', name: '...', size: 75, ... }

=== Test Download ===
Drive ID: <drive-id>
Item ID: <item-id>
✅ Download successful
Content: Test file from PCF client simulation
Timestamp: 2025-10-14T15:00:00.000Z

=== Test Delete ===
Drive ID: <drive-id>
Item ID: <item-id>
✅ Delete successful

================================================================================
✅ All tests passed! PCF client integration verified.
================================================================================
```

---

## Part 4: End-to-End Dataverse Integration Test

Test the **full stack**: Dataverse → PCF → BFF API → Graph API → SPE

### Step 4.1: Create Test Matter in Dataverse

```bash
# Create test matter with Drive ID
pac data create --entity-logical-name sprk_matter \
  --columns sprk_name="Test Matter $(date +%s)",sprk_driveid="<your-drive-id>"

# Get matter ID from output
MATTER_ID="<matter-guid-from-output>"
```

### Step 4.2: Open Model-Driven App

1. Navigate to your Dataverse model-driven app
2. Open the test matter record
3. Navigate to the **Documents** tab (where UniversalDatasetGrid PCF control is embedded)

**If PCF control is deployed**, you should see:
- Grid with file columns
- Upload button
- Download/Delete actions

**If PCF control is NOT deployed yet**, this test validates the API is ready.

### Step 4.3: Test Upload (via PCF or API)

**Option A: Via PCF Control** (if deployed)
- Click Upload button
- Select file
- Verify file appears in grid

**Option B: Via API** (simulating PCF)
```bash
# Upload file using API (simulates PCF control)
curl -X PUT \
  -H "Authorization: Bearer $PCF_TOKEN" \
  -H "Content-Type: application/pdf" \
  --data-binary @/path/to/test.pdf \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/$DRIVE_ID/upload?fileName=test-matter-document.pdf"
```

### Step 4.4: Verify in Dataverse

```bash
# Query Dataverse to verify file metadata was saved
pac data read --entity-logical-name sprk_document \
  --filter "sprk_matterid eq $MATTER_ID" \
  --columns sprk_name,sprk_itemid,sprk_driveid
```

**Expected**: Document record exists with:
- `sprk_name`: File name
- `sprk_itemid`: SPE item ID (from Graph API response)
- `sprk_driveid`: Container drive ID

---

## Part 5: Performance & Cache Verification

### Step 5.1: Measure Cache Performance

```bash
# Create performance test script
cat > test-cache-performance.sh <<'EOF'
#!/bin/bash
API_URL="https://spe-api-dev-67e2xz.azurewebsites.net/api/me"
TOKEN="$PCF_TOKEN"

echo "=== Cache Performance Test ==="
echo "Endpoint: $API_URL"
echo ""

for i in {1..10}; do
  echo "Request $i:"
  START=$(date +%s%3N)

  RESPONSE=$(curl -s -w "\nHTTP_STATUS:%{http_code}\nTIME_TOTAL:%{time_total}" \
    -H "Authorization: Bearer $TOKEN" \
    "$API_URL")

  END=$(date +%s%3N)
  ELAPSED=$((END - START))

  HTTP_STATUS=$(echo "$RESPONSE" | grep "HTTP_STATUS:" | cut -d':' -f2)
  TIME_TOTAL=$(echo "$RESPONSE" | grep "TIME_TOTAL:" | cut -d':' -f2)

  echo "  HTTP Status: $HTTP_STATUS"
  echo "  Response Time: ${TIME_TOTAL}s (${ELAPSED}ms client)"

  if [ $i -eq 1 ]; then
    echo "  Expected: Cache MISS (~1-2s total)"
  else
    echo "  Expected: Cache HIT (~0.5-1s total)"
  fi
  echo ""

  sleep 0.5
done

echo "=== Summary ==="
echo "Request 1: Should be slower (OBO exchange ~200ms)"
echo "Requests 2-10: Should be faster (cache hit ~5ms)"
EOF

chmod +x test-cache-performance.sh
./test-cache-performance.sh
```

**Expected Results**:
- Request 1: ~1-2 seconds (OBO exchange + Graph API)
- Requests 2-10: ~0.5-1 second (cache hit + Graph API)
- **97% reduction in OBO overhead**

### Step 5.2: Monitor Application Logs

```bash
# Watch logs in real-time
az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
```

**Look for**:
```
[Debug] Cache MISS for token hash abc12345... (first request)
[Debug] Cache HIT for token hash abc12345... (subsequent requests)
[Information] Using cached Graph token (cache hit)
```

---

## Troubleshooting

### 401 Unauthorized Errors

**Cause**: Token validation failed

**Check**:
```bash
# Decode JWT token to inspect claims
echo "$PCF_TOKEN" | cut -d'.' -f2 | base64 -d 2>/dev/null | jq
```

**Verify**:
- `aud` (audience) = `api://1e40baad-e065-4aea-a8d4-4b7ab273458c` (BFF API)
- `appid` (client) = `170c98e1-92b9-47ca-b3e7-e9e13f4f6e13` (PCF client app)
- `scp` (scopes) includes `user_impersonation` or similar

**Fix**:
1. Check Azure AD app registrations:
   - PCF client app → API Permissions → BFF API (api://1e40baad-...)
   - Admin consent granted
2. Check BFF API appsettings.json:
   - `AzureAd.ClientId` = `1e40baad-...`
   - `AzureAd.Audience` = `api://1e40baad-...`

### 403 Forbidden Errors

**Cause**: User lacks permissions (authorization policy)

**Check**: Dataverse access rights for user

**Fix**: Grant user appropriate security role in Dataverse

### OBO Flow Failures

**Symptom**: "AADSTS50013: Assertion failed signature validation"

**Cause**: BFF API not configured in Azure AD

**Fix**:
1. BFF API app registration → Expose an API → Add scope
2. PCF client app → API Permissions → Add permission to BFF API

### Cache Not Working

**Symptom**: All requests show same latency (no cache hits)

**Check**:
```bash
# Verify GraphTokenCache is registered
grep -rn "GraphTokenCache" src/api/Spe.Bff.Api/Infrastructure/DI/
```

**Fix**: Ensure Phase 4 code is deployed

---

## Success Criteria

### ✅ All Tests Pass

- [ ] Health check: `/ping` returns 200 OK
- [ ] User info: `/api/me` returns user details (200 OK)
- [ ] Upload: File uploaded to SPE (200 OK, returns metadata)
- [ ] Download: File downloaded from SPE (200 OK, correct content)
- [ ] Delete: File deleted from SPE (204 No Content)
- [ ] Cache: Request 1 slower, requests 2+ faster (logs show cache hits)

### ✅ Performance Targets

- [ ] Cache HIT latency: ~5ms (vs 200ms for OBO exchange)
- [ ] Upload latency: <2 seconds (small files <1MB)
- [ ] Download latency: <2 seconds (small files <1MB)
- [ ] Cache hit rate: >90% after warmup

### ✅ Integration Verified

- [ ] PCF client token accepted by BFF API
- [ ] OBO flow succeeds (user token → Graph token)
- [ ] Graph API calls succeed (file operations work)
- [ ] SharePoint Embedded stores files correctly
- [ ] Dataverse metadata synced (if applicable)

---

## Next Steps

Once all tests pass:

1. **Build and deploy PCF control**:
   ```bash
   cd src/controls/UniversalDatasetGrid
   npm run build:prod
   pac pcf push --publisher-prefix sprk
   ```

2. **Test in model-driven app**:
   - Open Dataverse app
   - Navigate to entity with PCF control
   - Test upload/download/delete via UI

3. **Enable Redis for production** (if not already):
   ```bash
   az webapp config appsettings set \
     --name spe-api-dev-67e2xz \
     --resource-group spe-infrastructure-westus2 \
     --settings "Redis__Enabled=true" \
              "ConnectionStrings__Redis=<your-redis-connection-string>"
   ```

4. **Monitor metrics** (OpenTelemetry):
   - Configure Prometheus/Azure Monitor exporter
   - Monitor cache hit rate (target: >90%)
   - Monitor latency percentiles (P50, P95, P99)

---

## Related Documentation

- **Phase 4 Testing**: [PHASE-4-TESTING-GUIDE.md](PHASE-4-TESTING-GUIDE.md)
- **Architecture**: [TARGET-ARCHITECTURE.md](dev/projects/sdap_V2/TARGET-ARCHITECTURE.md)
- **Implementation Checklist**: [IMPLEMENTATION-CHECKLIST.md](dev/projects/sdap_V2/IMPLEMENTATION-CHECKLIST.md)
- **API Endpoints**: [OBOEndpoints.cs](src/api/Spe.Bff.Api/Api/OBOEndpoints.cs)
- **PCF Control**: [UniversalDatasetGrid](src/controls/UniversalDatasetGrid/)
- **SDAP API Client**: [SdapApiClient.ts](src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClient.ts)

---

**Last Updated**: 2025-10-14
**Status**: ✅ BFF API deployed and functional, ready for end-to-end testing
