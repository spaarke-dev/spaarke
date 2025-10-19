# Register BFF API with Container Type
# Uses PCF app credentials (assuming it's the owning application)

param(
    [string]$ContainerTypeId = "8a6ce34c-6055-4681-8f87-2f4f9f921c06",
    [string]$BffApiAppId = "1e40baad-e065-4aea-a8d4-4b7ab273458c",
    [string]$OwningAppId = "170c98e1-d486-4355-bcbe-170454e0207c",
    [string]$OwningAppSecret = "~Ac8Q~JGnsrvNEODvFo8qmtKbgj1PmwmJ6GVUaJj",
    [string]$TenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
)

Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "REGISTER BFF API WITH CONTAINER TYPE" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "Container Type ID: $ContainerTypeId" -ForegroundColor White
Write-Host "BFF API App ID:    $BffApiAppId" -ForegroundColor White
Write-Host "Owning App ID:     $OwningAppId" -ForegroundColor White
Write-Host ""

# Step 1: Get token for SharePoint using owning app credentials
Write-Host "Step 1: Getting SharePoint access token..." -ForegroundColor Yellow

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

    $accessToken = $tokenResponse.access_token

    Write-Host "✅ Got SharePoint access token" -ForegroundColor Green
    Write-Host ""

    # Step 2: Register both owning app and BFF API with container type
    Write-Host "Step 2: Registering applications with container type..." -ForegroundColor Yellow
    Write-Host "  - Owning App (PCF): Full permissions" -ForegroundColor Gray
    Write-Host "  - Guest App (BFF API): WriteContent delegated" -ForegroundColor Gray

    $registrationBody = @{
        value = @(
            @{
                appId = $OwningAppId
                delegated = @("full")
                appOnly = @("full")
            },
            @{
                appId = $BffApiAppId
                delegated = @("WriteContent")
                appOnly = @()
            }
        )
    } | ConvertTo-Json -Depth 3

    $headers = @{
        "Authorization" = "Bearer $accessToken"
        "Content-Type" = "application/json"
        "Accept" = "application/json"
    }

    $uri = "https://spaarke.sharepoint.com/_api/v2.1/storageContainerTypes/$ContainerTypeId/applicationPermissions"

    Write-Host "Calling: PUT $uri" -ForegroundColor Gray
    Write-Host "Body: $registrationBody" -ForegroundColor Gray
    Write-Host ""

    $response = Invoke-RestMethod -Uri $uri `
        -Method Put `
        -Headers $headers `
        -Body $registrationBody `
        -ErrorAction Stop

    Write-Host "✅ REGISTRATION SUCCESSFUL!" -ForegroundColor Green
    Write-Host ""
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "REGISTRATION RESULT" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""

    if ($response.value) {
        foreach ($perm in $response.value) {
            Write-Host "App ID:              $($perm.appId)" -ForegroundColor White
            Write-Host "App Display Name:    $($perm.appDisplayName)" -ForegroundColor White
            Write-Host "Delegated Perms:     $($perm.delegated -join ', ')" -ForegroundColor Green
            Write-Host "App-Only Perms:      $($perm.appOnly -join ', ')" -ForegroundColor Gray
            Write-Host ""
        }
    } else {
        $response | ConvertTo-Json -Depth 5
    }

    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "NEXT STEPS" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "1. Restart the BFF API app service to clear MSAL cache:" -ForegroundColor White
    Write-Host "   az webapp restart --name spe-api-dev-67e2xz --resource-group [rg-name]" -ForegroundColor Gray
    Write-Host ""
    Write-Host "2. Test the OBO upload endpoint:" -ForegroundColor White
    Write-Host "   PUT /api/obo/containers/{id}/files/test.txt" -ForegroundColor Gray
    Write-Host ""
    Write-Host "3. Expected result: HTTP 200 OK (not 403 Forbidden)" -ForegroundColor White
    Write-Host ""

} catch {
    Write-Host "❌ REGISTRATION FAILED" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red

    if ($_.Exception.Response) {
        try {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $reader.BaseStream.Position = 0
            $reader.DiscardBufferedData()
            $responseBody = $reader.ReadToEnd()
            Write-Host ""
            Write-Host "Response body:" -ForegroundColor Yellow
            Write-Host $responseBody -ForegroundColor Gray

            # Try to parse as JSON for better display
            try {
                $errorJson = $responseBody | ConvertFrom-Json
                Write-Host ""
                Write-Host "Error Details:" -ForegroundColor Yellow
                Write-Host "  Code:    $($errorJson.error.code)" -ForegroundColor Red
                Write-Host "  Message: $($errorJson.error.message)" -ForegroundColor Red
            } catch {
                # Not JSON, already displayed
            }
        } catch {
            Write-Host "Could not read error response" -ForegroundColor Gray
        }
    }

    Write-Host ""
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "TROUBLESHOOTING" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "If error is 'Access denied' or '401/403':" -ForegroundColor Yellow
    Write-Host "  - The PCF app may not be the owning application" -ForegroundColor Gray
    Write-Host "  - Need to find the actual owning application ID" -ForegroundColor Gray
    Write-Host "  - Requires SharePoint admin access to query container types" -ForegroundColor Gray
    Write-Host ""
    Write-Host "If error is 'Container.Selected permission required':" -ForegroundColor Yellow
    Write-Host "  - Grant Container.Selected app-only permission to PCF app" -ForegroundColor Gray
    Write-Host "  - Go to Azure Portal > App Registrations > PCF app > API Permissions" -ForegroundColor Gray
    Write-Host "  - Add SharePoint > Application > Container.Selected" -ForegroundColor Gray
    Write-Host "  - Grant admin consent" -ForegroundColor Gray
    Write-Host ""
}
