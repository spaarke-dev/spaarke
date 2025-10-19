# Register BFF API with Container Type using Certificate Authentication
# This version uses direct REST API calls (no MSAL.PS dependency)

param(
    [string]$TenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2",
    [string]$OwningAppId = "170c98e1-d486-4355-bcbe-170454e0207c",
    [string]$BffApiAppId = "1e40baad-e065-4aea-a8d4-4b7ab273458c",
    [string]$ContainerTypeId = "8a6ce34c-6055-4681-8f87-2f4f9f921c06",
    [string]$CertThumbprint = "269691A5A60536050FA76C0163BD4A942ECD724D",
    [string]$SharePointDomain = "spaarke.sharepoint.com"
)

Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "REGISTER BFF API WITH CERTIFICATE AUTH" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "SharePoint Embedded requires certificate authentication" -ForegroundColor Yellow
Write-Host "for container type management APIs" -ForegroundColor Yellow
Write-Host ""
Write-Host "Certificate Thumbprint: $CertThumbprint" -ForegroundColor Gray
Write-Host "Owning App: $OwningAppId" -ForegroundColor Gray
Write-Host ""

# Step 1: Find certificate
Write-Host "Step 1: Looking for certificate in local store..." -ForegroundColor Yellow
$cert = Get-ChildItem -Path Cert:\CurrentUser\My | Where-Object {
    $_.Thumbprint -eq $CertThumbprint
}

if (-not $cert) {
    # Try LocalMachine
    $cert = Get-ChildItem -Path Cert:\LocalMachine\My -ErrorAction SilentlyContinue | Where-Object {
        $_.Thumbprint -eq $CertThumbprint
    }
}

if (-not $cert) {
    Write-Host "❌ Certificate not found" -ForegroundColor Red
    Write-Host "   Expected thumbprint: $CertThumbprint" -ForegroundColor Gray
    exit 1
}

Write-Host "✅ Found certificate" -ForegroundColor Green
Write-Host "   Subject: $($cert.Subject)" -ForegroundColor Gray
Write-Host "   Expires: $($cert.NotAfter)" -ForegroundColor Gray
Write-Host ""

# Step 2: Create JWT assertion for client credentials flow
Write-Host "Step 2: Creating JWT assertion with certificate..." -ForegroundColor Yellow

# Base64Url encode function
function ConvertTo-Base64Url {
    param([byte[]]$bytes)
    $base64 = [Convert]::ToBase64String($bytes)
    return $base64.Replace('+', '-').Replace('/', '_').TrimEnd('=')
}

# Get x5t (certificate thumbprint as base64url)
$thumbprintBytes = for ($i = 0; $i -lt $cert.Thumbprint.Length; $i += 2) {
    [Convert]::ToByte($cert.Thumbprint.Substring($i, 2), 16)
}
$x5t = ConvertTo-Base64Url -bytes $thumbprintBytes

# Create JWT header
$header = @{
    alg = "RS256"
    typ = "JWT"
    x5t = $x5t
} | ConvertTo-Json -Compress

# Create JWT payload
$now = [Math]::Floor([decimal](Get-Date(Get-Date).ToUniversalTime()-uformat "%s"))
$exp = $now + 600  # 10 minutes from now

$payload = @{
    aud = "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token"
    exp = $exp
    iss = $OwningAppId
    jti = [guid]::NewGuid().ToString()
    nbf = $now
    sub = $OwningAppId
} | ConvertTo-Json -Compress

# Base64Url encode header and payload
$headerBytes = [System.Text.Encoding]::UTF8.GetBytes($header)
$payloadBytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
$headerEncoded = ConvertTo-Base64Url -bytes $headerBytes
$payloadEncoded = ConvertTo-Base64Url -bytes $payloadBytes
$message = "$headerEncoded.$payloadEncoded"

# Sign with certificate
$messageBytes = [System.Text.Encoding]::UTF8.GetBytes($message)
$signature = $cert.PrivateKey.SignData($messageBytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
$signatureEncoded = ConvertTo-Base64Url -bytes $signature

$jwt = "$message.$signatureEncoded"

Write-Host "✅ JWT assertion created" -ForegroundColor Green
Write-Host ""

# Step 3: Get token using certificate assertion
Write-Host "Step 3: Getting SharePoint token with certificate..." -ForegroundColor Yellow

$tokenBody = @{
    client_id = $OwningAppId
    client_assertion_type = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer"
    client_assertion = $jwt
    scope = "https://$SharePointDomain/.default"
    grant_type = "client_credentials"
}

try {
    $tokenResponse = Invoke-RestMethod `
        -Uri "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token" `
        -Method Post `
        -Body $tokenBody `
        -ContentType "application/x-www-form-urlencoded"

    $token = $tokenResponse.access_token
    Write-Host "✅ Token acquired" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Host "❌ Failed to get token" -ForegroundColor Red
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Gray
    if ($_.ErrorDetails.Message) {
        $errorDetails = $_.ErrorDetails.Message | ConvertFrom-Json
        Write-Host "   Details: $($errorDetails.error_description)" -ForegroundColor Gray
    }
    exit 1
}

# Step 4: Register applications with container type
Write-Host "Step 4: Registering applications with container type..." -ForegroundColor Yellow

$registrationBody = @{
    value = @(
        @{
            appId = $OwningAppId  # PCF app (owner)
            delegated = @("full")
            appOnly = @("full")
        },
        @{
            appId = $BffApiAppId  # BFF API (guest)
            delegated = @("ReadContent", "WriteContent")
            appOnly = @("none")
        }
    )
} | ConvertTo-Json -Depth 3

$headers = @{
    "Authorization" = "Bearer $token"
    "Content-Type" = "application/json"
}

$registrationUrl = "https://$SharePointDomain/_api/v2.1/storageContainerTypes/$ContainerTypeId/applicationPermissions"

try {
    $response = Invoke-RestMethod `
        -Uri $registrationUrl `
        -Method Put `
        -Headers $headers `
        -Body $registrationBody

    Write-Host ""
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Green
    Write-Host "✅ REGISTRATION SUCCESSFUL!" -ForegroundColor Green
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Green
    Write-Host ""
    Write-Host "The following applications have been registered:" -ForegroundColor White
    Write-Host ""

    Write-Host "Owning App: $OwningAppId" -ForegroundColor Cyan
    Write-Host "  Delegated: full" -ForegroundColor White
    Write-Host "  App-Only: full" -ForegroundColor White
    Write-Host ""

    Write-Host "Guest App (BFF API): $BffApiAppId" -ForegroundColor Cyan
    Write-Host "  Delegated: ReadContent, WriteContent" -ForegroundColor White
    Write-Host "  App-Only: none" -ForegroundColor White
    Write-Host ""

} catch {
    Write-Host "❌ Registration failed" -ForegroundColor Red
    Write-Host "   Status: $($_.Exception.Response.StatusCode.Value__)" -ForegroundColor Gray
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Gray

    if ($_.ErrorDetails.Message) {
        Write-Host ""
        Write-Host "Response:" -ForegroundColor Gray
        $_.ErrorDetails.Message | ConvertFrom-Json | ConvertTo-Json -Depth 10 | Write-Host -ForegroundColor Gray
    }
    exit 1
}

Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "NEXT STEPS" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Restart BFF API to clear MSAL token cache:" -ForegroundColor White
Write-Host "   az webapp restart --name spe-api-dev-67e2xz --resource-group <rg-name>" -ForegroundColor Gray
Write-Host ""
Write-Host "2. Test OBO upload endpoint" -ForegroundColor White
Write-Host "   PUT /api/obo/containers/{containerId}/files/test.txt" -ForegroundColor Gray
Write-Host "   Expected: HTTP 200 OK (not 403 Forbidden)" -ForegroundColor Gray
Write-Host ""
