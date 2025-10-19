# Test SharePoint token validity

$clientId = "170c98e1-d486-4355-bcbe-170454e0207c"
$clientSecret = "~Ac8Q~JGnsrvNEODvFo8qmtKbgj1PmwmJ6GVUaJj"
$tenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
$containerTypeId = "8a6ce34c-6055-4681-8f87-2f4f9f921c06"

Write-Host "Testing SharePoint token..." -ForegroundColor Cyan

# Get token
$tokenBody = @{
    client_id = $clientId
    client_secret = $clientSecret
    scope = "https://spaarke.sharepoint.com/.default"
    grant_type = "client_credentials"
}

try {
    $tokenResponse = Invoke-RestMethod `
        -Uri "https://login.microsoftonline.com/$tenantId/oauth2/v2.0/token" `
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

    $uri = "https://spaarke.sharepoint.com/_api/v2.1/storageContainerTypes/$containerTypeId/applicationPermissions"

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
