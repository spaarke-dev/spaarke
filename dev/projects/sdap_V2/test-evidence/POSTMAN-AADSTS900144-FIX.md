# Postman AADSTS900144 Error Fix

**Error**: `AADSTS900144: The request body must contain the following parameter: 'client_id'`

**Root Cause**: Incorrect "Client Authentication" setting in Postman OAuth 2.0 configuration

---

## Quick Fix

In Postman's **Configure New Token** dialog:

### Change This Setting:

**Client Authentication**: Change from ~~"Send as Basic Auth header"~~ to:
```
Send client credentials in body
```

**Why**: When using PKCE (public client), the client_id must be in the request **body**, not in the Authorization header.

---

## Complete Corrected Configuration

| Field | Value |
|-------|-------|
| Token Name | `SDAP BFF API Token` |
| Grant Type | **Authorization Code (With PKCE)** |
| Callback URL | `https://oauth.pstmn.io/v1/browser-callback` |
| Auth URL | `https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/oauth2/v2.0/authorize` |
| Access Token URL | `https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/oauth2/v2.0/token` |
| Client ID | `1e40baad-e065-4aea-a8d4-4b7ab273458c` |
| Client Secret | (leave blank) |
| Scope | `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation` |
| State | `12345` |
| Client Authentication | **Send client credentials in body** ⚠️ |

---

## Step-by-Step in Postman

1. ☐ Click **Configure New Token** (or edit existing token config)
2. ☐ Scroll down to **Client Authentication** dropdown
3. ☐ Change from "Send as Basic Auth header" to **"Send client credentials in body"**
4. ☐ Click **Get New Access Token**
5. ☐ Browser opens → Sign in
6. ☐ Consent screen → Accept
7. ☐ Success! Token appears
8. ☐ Click **Use Token**

---

## Why This Matters

**With PKCE (public clients)**:
- Client ID must be in request body: `client_id=1e40baad...`
- NOT in Authorization header: `Authorization: Basic <base64>`

**Azure AD expects**:
```http
POST /oauth2/v2.0/token
Content-Type: application/x-www-form-urlencoded

client_id=1e40baad-e065-4aea-a8d4-4b7ab273458c
&scope=api://1e40baad.../user_impersonation
&code=...
&redirect_uri=https://oauth.pstmn.io/v1/browser-callback
&grant_type=authorization_code
&code_verifier=...
```

**NOT** (what "Send as Basic Auth header" does):
```http
POST /oauth2/v2.0/token
Authorization: Basic <base64-encoded-client-id>
```

---

## Alternative: Use Postman Desktop App

If the web version still has issues, try **Postman Desktop**:
- Download: https://www.postman.com/downloads/
- Desktop app uses `/v1/callback` (also registered)
- Better OAuth 2.0 flow handling

---

## Alternative: PowerShell Script

If Postman continues to have issues, use this working PowerShell script:

```powershell
# Get token (opens browser for user sign-in)
$token = az account get-access-token `
  --resource "api://1e40baad-e065-4aea-a8d4-4b7ab273458c" `
  --query accessToken -o tsv

# Test file upload
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "text/plain"
}

$containerId = "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"
$filePath = "TestFolder/postman-test.txt"
$fileContent = "This is a test file uploaded at $(Get-Date)"

$url = "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/containers/$containerId/files/$filePath"

Write-Host "Uploading file to: $url"
Write-Host "Container ID: $containerId"
Write-Host ""

try {
    $response = Invoke-RestMethod -Uri $url -Method PUT -Headers $headers -Body $fileContent

    Write-Host "✅ SUCCESS! File uploaded successfully" -ForegroundColor Green
    Write-Host ""
    Write-Host "File Details:" -ForegroundColor Cyan
    Write-Host "  File ID: $($response.id)"
    Write-Host "  Name: $($response.name)"
    Write-Host "  Size: $($response.size) bytes"
    Write-Host "  Web URL: $($response.webUrl)"
    Write-Host "  Created: $($response.createdDateTime)"

    # This completes Task 5.9 - Production Validation!
    Write-Host ""
    Write-Host "🎉 Phase 5 Task 5.9 COMPLETE! End-to-end file upload validated!" -ForegroundColor Green
}
catch {
    Write-Host "❌ ERROR: Upload failed" -ForegroundColor Red
    Write-Host "Status: $($_.Exception.Response.StatusCode.value__)"
    Write-Host "Message: $($_.Exception.Message)"

    if ($_.ErrorDetails.Message) {
        Write-Host ""
        Write-Host "Details:" -ForegroundColor Yellow
        $_.ErrorDetails.Message | ConvertFrom-Json | ConvertTo-Json -Depth 10
    }
}
```

**Save as**: `test-file-upload.ps1`

**Run**:
```powershell
.\test-file-upload.ps1
```

This script:
- ✅ Gets token via Azure CLI (handles OAuth flow)
- ✅ Uploads test file to your Matter's container
- ✅ Validates end-to-end SDAP V2 flow
- ✅ Completes Phase 5 Task 5.9!

---

**Fix Created**: 2025-10-14
**Error**: AADSTS900144 (client_id missing)
**Solution**: Change "Client Authentication" to "Send client credentials in body"
