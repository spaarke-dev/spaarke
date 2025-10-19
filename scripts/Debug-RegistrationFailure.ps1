# Debug why registration failed when PCF app IS the owner

$containerTypeId = "8a6ce34c-6055-4681-8f87-2f4f9f921c06"
$OwningAppId = "170c98e1-d486-4355-bcbe-170454e0207c"
$OwningAppSecret = "~Ac8Q~JGnsrvNEODvFo8qmtKbgj1PmwmJ6GVUaJj"
$TenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2"

Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "DEBUGGING REGISTRATION FAILURE" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "Container Type: $containerTypeId" -ForegroundColor White
Write-Host "Owner (PCF app): $OwningAppId" -ForegroundColor Green
Write-Host ""

# Step 1: Get SharePoint token
Write-Host "Step 1: Getting SharePoint token..." -ForegroundColor Yellow

$tokenBody = @{
    client_id = $OwningAppId
    client_secret = $OwningAppSecret
    scope = "https://spaarke.sharepoint.com/.default"
    grant_type = "client_credentials"
}

try {
    $tokenResponse = Invoke-RestMethod -Uri "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token" `
        -Method Post `
        -Body $tokenBody `
        -ErrorAction Stop

    Write-Host "✅ Token acquired" -ForegroundColor Green

    $accessToken = $tokenResponse.access_token

    # Decode token
    $tokenParts = $accessToken.Split('.')
    $paddedPayload = $tokenParts[1]
    while ($paddedPayload.Length % 4 -ne 0) {
        $paddedPayload += "="
    }
    $payloadBytes = [System.Convert]::FromBase64String($paddedPayload)
    $payloadJson = [System.Text.Encoding]::UTF8.GetString($payloadBytes)
    $claims = $payloadJson | ConvertFrom-Json

    Write-Host ""
    Write-Host "Token Claims:" -ForegroundColor Cyan
    Write-Host "  aud: $($claims.aud)" -ForegroundColor White
    Write-Host "  appid: $($claims.appid)" -ForegroundColor White

    if ($claims.roles) {
        Write-Host "  roles: $($claims.roles -join ', ')" -ForegroundColor White

        if ($claims.roles -contains "Container.Selected") {
            Write-Host ""
            Write-Host "✅ Token HAS Container.Selected permission" -ForegroundColor Green
        } else {
            Write-Host ""
            Write-Host "❌ Token LACKS Container.Selected permission" -ForegroundColor Red
            Write-Host "   This is why registration failed!" -ForegroundColor Yellow
            Write-Host ""
            Write-Host "FIX: Grant Container.Selected app permission to PCF app" -ForegroundColor Yellow
            exit 1
        }
    } else {
        Write-Host "  roles: (none)" -ForegroundColor Red
        Write-Host ""
        Write-Host "❌ No app roles in token - Container.Selected not granted" -ForegroundColor Red
        exit 1
    }

    Write-Host ""

    # Step 2: Try to query container type (read-only test)
    Write-Host "Step 2: Testing read access to container type..." -ForegroundColor Yellow

    $headers = @{
        "Authorization" = "Bearer $accessToken"
        "Accept" = "application/json"
    }

    $uri = "https://spaarke.sharepoint.com/_api/v2.1/storageContainerTypes/$containerTypeId/applicationPermissions"

    try {
        $currentPerms = Invoke-RestMethod -Uri $uri -Method Get -Headers $headers -ErrorAction Stop

        Write-Host "✅ Successfully read container type permissions" -ForegroundColor Green
        Write-Host ""
        Write-Host "Current registered applications:" -ForegroundColor Cyan

        if ($currentPerms.value) {
            foreach ($app in $currentPerms.value) {
                Write-Host "  - App ID: $($app.appId)" -ForegroundColor White
                Write-Host "    Delegated: $($app.delegated -join ', ')" -ForegroundColor Gray
                Write-Host "    App-Only: $($app.appOnly -join ', ')" -ForegroundColor Gray
            }
        } else {
            Write-Host "  (none registered yet)" -ForegroundColor Gray
        }

    } catch {
        Write-Host "❌ Failed to read permissions: $($_.Exception.Message)" -ForegroundColor Red
    }

} catch {
    Write-Host "❌ Token acquisition failed" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
}
