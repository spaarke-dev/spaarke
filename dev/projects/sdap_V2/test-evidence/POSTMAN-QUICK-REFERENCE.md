# Postman OAuth 2.0 Configuration - Quick Reference

**Copy these values EXACTLY** (use copy button to avoid typos)

---

## OAuth 2.0 Configuration Values

### Token Name
```
SDAP BFF API Token
```

### Grant Type
```
Authorization Code (With PKCE)
```

### Callback URL
```
https://oauth.pstmn.io/v1/callback
```

### Auth URL
```
https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/oauth2/v2.0/authorize
```

### Access Token URL
```
https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/oauth2/v2.0/token
```

### Client ID
```
1e40baad-e065-4aea-a8d4-4b7ab273458c
```

### Client Secret
```
(leave blank for PKCE)
```

### Scope (⚠️ CRITICAL - Use the correct scope!)
```
api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation
```

**Why `user_impersonation` and not `Files.ReadWrite.All`?**
- The BFF API **exposes** the `user_impersonation` scope (this is what you authenticate to)
- The BFF API **uses** `Files.ReadWrite.All` to call Microsoft Graph on your behalf (OBO pattern)
- You request access to the BFF API, which then acts as you to access Graph

### State
```
12345
```

### Client Authentication
```
Send as Basic Auth header
```

---

## File Upload Request

### Method
```
PUT
```

### URL
```
https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/containers/b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50/files/TestFolder/my-test-file.txt
```

**Note**: Replace `TestFolder/my-test-file.txt` with your desired path

### Body
- Select **binary**
- Click **Select File**
- Choose any small test file

---

## Common Mistakes to Avoid

❌ `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/Files.ReadWrite.All` (WRONG - this scope doesn't exist on BFF API)
✅ `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation` (CORRECT)

❌ `user_impersonation` (missing the api:// prefix)
✅ `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation` (full scope URI)

❌ Using `https://` in scope
✅ Using `api://` in scope (correct)

---

## Step-by-Step Checklist

### In Postman:

1. ☐ Create new request: **File** → **New** → **HTTP Request**
2. ☐ Authorization tab → Type: **OAuth 2.0**
3. ☐ Click **Configure New Token**
4. ☐ Copy-paste **Token Name**: `SDAP BFF API Token`
5. ☐ Grant Type: Select **Authorization Code (With PKCE)** from dropdown
6. ☐ Copy-paste **Callback URL**: `https://oauth.pstmn.io/v1/callback`
7. ☐ Copy-paste **Auth URL**: `https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/oauth2/v2.0/authorize`
8. ☐ Copy-paste **Access Token URL**: `https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/oauth2/v2.0/token`
9. ☐ Copy-paste **Client ID**: `1e40baad-e065-4aea-a8d4-4b7ab273458c`
10. ☐ **Client Secret**: Leave blank
11. ☐ Copy-paste **Scope**: `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation` ⚠️ (NOT Files.ReadWrite.All!)
12. ☐ **State**: Enter any value like `12345`
13. ☐ **Client Authentication**: Select **Send as Basic Auth header**
14. ☐ Click **Get New Access Token** button
15. ☐ Browser opens → Sign in with your account
16. ☐ Consent screen → Click **Accept**
17. ☐ Token appears → Click **Use Token**

### Configure Request:

18. ☐ Method: Select **PUT** from dropdown
19. ☐ URL: Paste full URL (see above)
20. ☐ **Body** tab → Select **binary** radio button
21. ☐ Click **Select File** → Choose a small test file
22. ☐ Click **Send** button

### Expected Result:

23. ☐ Status: **200 OK** (bottom right, green)
24. ☐ Response contains JSON with `id`, `name`, `size`, `webUrl`
25. ☐ Response time: ~200-300ms (first request)
26. ☐ Send again → Response time: ~50-100ms (cached!)

---

## If Callback URL Error

**Error**: `AADSTS50011: The reply URL specified in the request does not match`

**Solution**: Add Postman callback to app registration:

1. Go to: https://portal.azure.com
2. **Azure Active Directory** → **App registrations**
3. Search for: `1e40baad-e065-4aea-a8d4-4b7ab273458c`
4. Click on the app
5. **Authentication** → **Platform configurations** → **+ Add a platform**
6. Select **Web**
7. Redirect URI: `https://oauth.pstmn.io/v1/callback`
8. Click **Configure**
9. Retry in Postman

---

## Alternative: PowerShell Script

If Postman still has issues, use this PowerShell script:

```powershell
# Get token (will open browser for sign-in)
$token = az account get-access-token `
  --resource "api://1e40baad-e065-4aea-a8d4-4b7ab273458c" `
  --query accessToken -o tsv

# Upload file
$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/octet-stream"
}

$containerId = "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50"
$filePath = "TestFolder/my-test-file.txt"
$localFile = "C:\temp\test.txt"

# Create test file if doesn't exist
if (-not (Test-Path $localFile)) {
    "This is a test file uploaded via PowerShell" | Out-File $localFile
}

$url = "https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/containers/$containerId/files/$filePath"

$response = Invoke-RestMethod -Uri $url -Method PUT -Headers $headers -InFile $localFile

Write-Host "✅ Upload successful!" -ForegroundColor Green
Write-Host "File ID: $($response.id)"
Write-Host "Web URL: $($response.webUrl)"
```

---

**Quick Reference Created**: 2025-10-14
**Purpose**: Avoid typos in Postman OAuth 2.0 configuration
