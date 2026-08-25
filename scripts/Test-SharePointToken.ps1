# Test SharePoint token validity

param(
    [string]$ClientId = $env:API_APP_ID,
    # NOTE (2026-08-24, spaarke-auth-v4 task 033): this is the SPE OWNING-APP credential, which is
    # explicitly OUT OF SCOPE of the secret-free migration -- ADR-028 exception E-1 (per-customer
    # owning apps). SharePoint Embedded container-type registration is a client-credentials call that
    # a managed identity cannot make, so a secret remains correct here.
    # BUT the old instruction 'retrieve it from Key Vault' NO LONGER WORKS for the shared dev estate:
    # `BFF-API-ClientSecret` / `bff-api-client-secret` were deleted from `spaarke-spekvcert` on
    # 2026-08-24 (soft-deleted, recoverable until 2026-11-22). In dev the owning app IS the BFF app
    # registration, so its secret went with the migration. Pass -OwningAppSecret explicitly, or use a
    # per-customer owning-app secret (`spe-owning-app-secret` is the E-1 entry and is untouched).
    [string]$ClientSecret = $env:API_CLIENT_SECRET,
    [string]$TenantId = $env:TENANT_ID,
    [string]$ContainerTypeId = $env:SPE_CONTAINER_TYPE_ID,
    [string]$SharePointDomain = $env:SHAREPOINT_DOMAIN  # e.g., "spaarke.sharepoint.com"
)

if (-not $ClientId) { throw "ClientId required. Pass -ClientId or set API_APP_ID env var." }
if (-not $ClientSecret) { throw "ClientSecret required. Pass -ClientSecret or set API_CLIENT_SECRET env var." }
if (-not $TenantId) { throw "TenantId required. Pass -TenantId or set TENANT_ID env var." }
if (-not $ContainerTypeId) { throw "ContainerTypeId required. Pass -ContainerTypeId or set SPE_CONTAINER_TYPE_ID env var." }
if (-not $SharePointDomain) { throw "SharePointDomain required. Pass -SharePointDomain or set SHAREPOINT_DOMAIN env var." }

Write-Host "Testing SharePoint token..." -ForegroundColor Cyan

# Get token
$tokenBody = @{
    client_id = $ClientId
    client_secret = $ClientSecret
    scope = "https://$SharePointDomain/.default"
    grant_type = "client_credentials"
}

try {
    $tokenResponse = Invoke-RestMethod `
        -Uri "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token" `
        -Method Post `
        -Body $tokenBody

    Write-Host "✅ Token acquired" -ForegroundColor Green

    $token = $tokenResponse.access_token

    # Decode token
    $tokenParts = $token.Split('.')
    $payload = $tokenParts[1]
    while ($payload.Length % 4 -ne 0) { $payload += "=" }
    $bytes = [Convert]::FromBase64String($payload)
    $json = [Text.Encoding]::UTF8.GetString($bytes)
    $claims = $json | ConvertFrom-Json

    Write-Host ""
    Write-Host "Token info:" -ForegroundColor Yellow
    Write-Host "  aud: $($claims.aud)" -ForegroundColor White
    Write-Host "  roles: $($claims.roles -join ', ')" -ForegroundColor White
    Write-Host "  exp: $($claims.exp) ($(([DateTimeOffset]::FromUnixTimeSeconds($claims.exp)).LocalDateTime))" -ForegroundColor White
    Write-Host ""

    # Test API call
    Write-Host "Testing API call to SharePoint..." -ForegroundColor Yellow

    $headers = @{
        "Authorization" = "Bearer $token"
        "Accept" = "application/json"
    }

    $uri = "https://$SharePointDomain/_api/v2.1/storageContainerTypes/$ContainerTypeId/applicationPermissions"

    $result = Invoke-RestMethod -Uri $uri -Method Get -Headers $headers -ErrorAction Stop

    Write-Host "✅ API call successful!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Currently registered apps:" -ForegroundColor Cyan

    if ($result.value) {
        foreach ($app in $result.value) {
            Write-Host "  - $($app.appId)" -ForegroundColor White
            Write-Host "    Delegated: $($app.delegated -join ', ')" -ForegroundColor Gray
            Write-Host "    App-Only: $($app.appOnly -join ', ')" -ForegroundColor Gray
        }
    } else {
        Write-Host "  (none)" -ForegroundColor Gray
    }

} catch {
    Write-Host "❌ Failed: $($_.Exception.Message)" -ForegroundColor Red

    if ($_.ErrorDetails.Message) {
        $errorJson = $_.ErrorDetails.Message | ConvertFrom-Json
        Write-Host ""
        Write-Host "Error details:" -ForegroundColor Yellow
        Write-Host "  Code: $($errorJson.error.code)" -ForegroundColor Red
        Write-Host "  Message: $($errorJson.error.message)" -ForegroundColor Red
    }
}
