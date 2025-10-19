# Simple Test App Design - Isolate OBO and SPE Upload

**Date**: 2025-10-16
**Purpose**: Design a minimal test harness to verify OBO flow and SPE upload without PCF control complexity
**Status**: Design only - no code created

---

## The Problem with Current Testing

**Current Flow (Complex)**:
```
User → Dataverse Form → PCF Control (React) → MSAL.js → Token A →
  BFF API → OBO Exchange → Token B → Graph SDK → SPE Container
```

**Too many moving parts**:
- PCF control packaging
- Dataverse deployment
- Form configuration
- MSAL.js in browser context
- Entity relationships
- Container ID resolution

**Can't easily isolate**: Is the problem in PCF, API, OBO, or SPE?

---

## Simple Test App Approach

**Simplified Flow**:
```
Console App → Acquire Token → Call BFF API Endpoint → See Actual Error
```

**Benefits**:
- ✅ No PCF deployment
- ✅ No Dataverse complexity
- ✅ Direct API testing
- ✅ See actual HTTP responses
- ✅ Can test OBO in isolation
- ✅ Iterate in seconds, not minutes

---

## Option 1: Postman Collection (EASIEST - 5 minutes)

### Why Postman?
- ✅ No code to write
- ✅ Built-in OAuth 2.0 support
- ✅ Can save/share test cases
- ✅ Visual response inspection
- ✅ Easy token management

### Setup Steps

#### Step 1: Create New Request

**Request Details**:
```
Method: PUT
URL: https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/containers/b!21yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50/files/test.txt
```

#### Step 2: Configure OAuth 2.0

**Authorization Tab**:
- Type: `OAuth 2.0`
- Grant Type: `Authorization Code`
- Auth URL: `https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/oauth2/v2.0/authorize`
- Access Token URL: `https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/oauth2/v2.0/token`
- Client ID: `170c98e1-d486-4355-bcbe-170454e0207c` (PCF Client)
- Scope: `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation`
- State: (auto-generated)
- Client Authentication: `Send as Basic Auth header`

#### Step 3: Body

**Body Tab**:
- Type: `Binary`
- Select a small text file (< 1MB)

#### Step 4: Send Request

**Click "Send"**

**Expected Responses**:
- ✅ **201 Created** = Success! Upload worked!
- ❌ **401 Unauthorized** = Token A validation failed
- ❌ **403 Forbidden** = OBO succeeded but Token B lacks permissions
- ❌ **404 Not Found** = Container doesn't exist OR scope missing
- ❌ **500 Internal Server Error** = OBO failed or Graph SDK error

#### Step 5: Analyze Response

**If 404 "Item not found"**:
- Look at response headers
- Check if it's HTML (IIS error) or JSON (Graph error)
- Review Application Insights for actual error

**If 403 "Access denied"**:
- Confirms scope issue
- Check if error mentions "FileStorageContainer.Selected"

**If 401**:
- Token A is wrong
- Check Postman OAuth settings

---

## Option 2: cURL Script (MEDIUM - 10 minutes)

### Why cURL?
- ✅ Scriptable/repeatable
- ✅ No extra tools needed
- ✅ Easy to share commands
- ✅ Good for automation

### Script Design

```bash
#!/bin/bash
# test-spe-upload.sh

# Configuration
TENANT_ID="a221a95e-6abc-4434-aecc-e48338a1b2f2"
PCF_CLIENT_ID="170c98e1-d486-4355-bcbe-170454e0207c"
BFF_API_SCOPE="api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation"
CONTAINER_ID="b!21yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"
API_URL="https://spe-api-dev-67e2xz.azurewebsites.net"

# Step 1: Get Token A (user token for BFF API)
# Uses device code flow (easiest for scripts)
echo "🔐 Getting user token for BFF API..."
TOKEN_A=$(az account get-access-token \
  --resource "api://1e40baad-e065-4aea-a8d4-4b7ab273458c" \
  --query accessToken -o tsv)

if [ -z "$TOKEN_A" ]; then
  echo "❌ Failed to get Token A"
  exit 1
fi

echo "✅ Token A acquired (${#TOKEN_A} chars)"

# Step 2: Upload file to BFF API
echo "📤 Uploading test file..."
curl -X PUT \
  -H "Authorization: Bearer $TOKEN_A" \
  -H "Content-Type: text/plain" \
  -d "Test file content from cURL script" \
  --write-out "\n📊 HTTP Status: %{http_code}\n" \
  --verbose \
  "$API_URL/api/obo/containers/$CONTAINER_ID/files/curl-test.txt"

echo ""
echo "✅ Test complete"
```

### Run It

```bash
chmod +x test-spe-upload.sh
./test-spe-upload.sh
```

### Benefits
- See full HTTP request/response
- Easy to modify container ID or filename
- Can add logging
- Scriptable for CI/CD

---

## Option 3: Minimal .NET Console App (DETAILED - 30 minutes)

### Why .NET Console App?
- ✅ Can reuse MSAL libraries
- ✅ Can test OBO exchange locally
- ✅ Step through code with debugger
- ✅ Test different scenarios easily

### Project Structure

```
TestApp/
  ├── TestApp.csproj
  ├── Program.cs
  └── appsettings.json
```

### Key Components

**1. Acquire Token A (User Token for BFF API)**
```csharp
// Using MSAL PublicClientApplication
// Scopes: api://1e40baad.../user_impersonation
// Auth flow: Interactive browser login
```

**2. Call BFF API with Token A**
```csharp
// PUT /api/obo/containers/{containerId}/files/{filename}
// Header: Authorization: Bearer {Token A}
// Body: File content
```

**3. Display Response**
```csharp
// Show HTTP status code
// Show response body
// Show headers
// Parse error messages
```

### What You'll Learn

**If it works**:
- ✅ OBO flow is correct
- ✅ Token B has right scopes
- ✅ Container exists
- ✅ Problem is in PCF control

**If it fails**:
- ❌ See actual error message
- ❌ Can debug step-by-step
- ❌ Can test different container IDs
- ❌ Can test different scopes

---

## Option 4: Existing BFF API Test Endpoint (FASTEST - 2 minutes)

### Add Test Endpoint to Existing API

**Add to Program.cs**:

```csharp
// Test endpoint - no authentication required
app.MapGet("/api/test/container-exists/{containerId}", async (
    string containerId,
    IGraphClientFactory factory) =>
{
    try
    {
        // Use app-only client (no OBO needed for test)
        var graphClient = factory.CreateAppOnlyClient();

        var drive = await graphClient.Drives[containerId].GetAsync();

        return Results.Ok(new {
            exists = true,
            id = drive.Id,
            name = drive.Name,
            driveType = drive.DriveType
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(new {
            exists = false,
            error = ex.Message,
            type = ex.GetType().Name
        });
    }
});
```

**Test It**:
```bash
curl "https://spe-api-dev-67e2xz.azurewebsites.net/api/test/container-exists/b!21yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"
```

**Benefits**:
- ✅ Tests if container exists
- ✅ Uses your existing auth setup
- ✅ No OBO complexity
- ✅ Quick to add

---

## Option 5: PowerShell Script (WINDOWS-FRIENDLY - 15 minutes)

### Why PowerShell?
- ✅ Native on Windows
- ✅ Easy MSAL integration
- ✅ Good for Windows devs
- ✅ Can use existing Az modules

### Script Outline

```powershell
# test-spe-upload.ps1

# Import modules
Import-Module Az.Accounts

# Connect (interactive)
Connect-AzAccount

# Get token for BFF API
$token = (Get-AzAccessToken -ResourceUrl "api://1e40baad-e065-4aea-a8d4-4b7ab273458c").Token

# Upload file
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "text/plain"
}

$body = "Test content from PowerShell"

$uri = "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/containers/b!21yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50/files/ps-test.txt"

try {
    $response = Invoke-RestMethod -Uri $uri -Method Put -Headers $headers -Body $body
    Write-Host "✅ Success!" -ForegroundColor Green
    Write-Host ($response | ConvertTo-Json -Depth 10)
}
catch {
    Write-Host "❌ Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Status Code: $($_.Exception.Response.StatusCode.value__)"

    # Try to read error body
    $reader = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
    $errorBody = $reader.ReadToEnd()
    Write-Host "Response: $errorBody"
}
```

---

## My Recommendation: Start with Postman

**Why Postman First?**

1. **Fastest** (5 minutes setup)
2. **No code to write**
3. **Visual feedback**
4. **Easy OAuth setup**
5. **Can share collection**

**If Postman succeeds**:
- Problem is in PCF control
- API and OBO work fine
- Focus on PCF debugging

**If Postman fails**:
- Problem is in API or OBO
- See actual error message
- Can iterate quickly

---

## What Each Test Will Reveal

### Test: Does Container Exist?

**Command**:
```bash
TOKEN=$(az account get-access-token --resource https://graph.microsoft.com --query accessToken -o tsv)
curl -H "Authorization: Bearer $TOKEN" \
  "https://graph.microsoft.com/beta/drives/b!21yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"
```

**Results**:
- ✅ 200 OK = Container exists, different problem
- ❌ 404 Not Found = Container doesn't exist
- ❌ 403 Forbidden = Token missing FileStorageContainer.Selected

---

### Test: Can You Upload via BFF API?

**Tool**: Postman with OAuth 2.0

**Results**:
- ✅ 201 Created = Everything works! Problem is PCF-specific
- ❌ 404 Not Found = Container issue or scope issue
- ❌ 500 Error = OBO or API problem
- ❌ 401 Unauthorized = Auth configuration problem

---

### Test: What Scopes Does Token B Have?

**Add to GraphClientFactory.cs temporarily**:

```csharp
// After line 159
_logger.LogWarning("🔍 DEBUG - Token B scopes: {Scopes}",
    string.Join(", ", result.Scopes));
```

**Then trigger upload and check logs**

**Expected**:
```
Token B scopes: Sites.FullControl.All, Files.ReadWrite.All
```

**Should be**:
```
Token B scopes: Sites.FullControl.All, Files.ReadWrite.All, FileStorageContainer.Selected
```

---

## Decision Tree

```
START: Want to test OBO and SPE upload
  │
  ├─> Quickest test? (2 min)
  │   └─> Use cURL to test container exists:
  │       curl GET /drives/{containerId}
  │
  ├─> Need full flow test? (5 min)
  │   └─> Use Postman:
  │       - OAuth 2.0 setup
  │       - PUT /api/obo/containers/.../files/test.txt
  │
  ├─> Need scriptable test? (10 min)
  │   └─> Use bash/PowerShell script:
  │       - Get Token A
  │       - Call BFF API
  │       - Parse response
  │
  └─> Need debuggable test? (30 min)
      └─> Use .NET Console App:
          - Full MSAL integration
          - Step through code
          - Test different scenarios
```

---

## Recommended Testing Sequence

### Phase 1: Quick Validation (5 minutes)

1. **Test if container exists** (cURL to Graph API directly)
   - Reveals: Does container exist? Do you have access?

2. **Check Token B scopes** (Application Insights query)
   - Reveals: Is FileStorageContainer.Selected in Token B?

### Phase 2: Isolated API Test (10 minutes)

3. **Use Postman to call BFF API**
   - Reveals: Does upload work outside of PCF?
   - If YES: Problem is in PCF
   - If NO: Problem is in API/OBO

### Phase 3: Fix Based on Results

4. **If Postman succeeds but PCF fails**:
   - Debug PCF control token acquisition
   - Check container ID resolution
   - Verify file handling

5. **If both fail**:
   - Add FileStorageContainer.Selected scope to OBO
   - OR create container if doesn't exist
   - OR fix token acquisition

---

**Which approach would you like to try first?**

**My suggestion**:
1. Run the container exists test (30 seconds)
2. Then Postman upload test (5 minutes)
3. Based on results, we'll know exactly what to fix

---

**Document Created**: 2025-10-16 07:15 AM
**Status**: Design complete - awaiting user decision on which test approach to use
**No code created** per user request
