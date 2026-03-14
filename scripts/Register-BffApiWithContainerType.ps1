# Register owning app with SharePoint Embedded Container Type
# The owning app (BFF API) gets full delegated + full appOnly permissions

param(
    [Parameter(Mandatory)][string]$ContainerTypeId,
    [Parameter(Mandatory)][string]$OwningAppId,
    # Retrieve from Key Vault: az keyvault secret show --vault-name <name> --name <secret> --query value -o tsv
    [Parameter(Mandatory)][string]$OwningAppSecret,
    [Parameter(Mandatory)][string]$TenantId,
    [Parameter(Mandatory)][string]$SharePointDomain  # e.g., "spaarke.sharepoint.com"
)

Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "REGISTER OWNING APP WITH CONTAINER TYPE" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""
Write-Host "Container Type ID: $ContainerTypeId" -ForegroundColor White
Write-Host "Owning App ID:     $OwningAppId" -ForegroundColor White
Write-Host "SharePoint Domain: $SharePointDomain" -ForegroundColor White
Write-Host ""

# Step 1: Get token for SharePoint using owning app credentials
Write-Host "Step 1: Getting SharePoint access token..." -ForegroundColor Yellow

$tokenBody = @{
    client_id = $OwningAppId
    client_secret = $OwningAppSecret
    scope = "https://$SharePointDomain/.default"
    grant_type = "client_credentials"
}

try {
    $tokenResponse = Invoke-RestMethod -Uri "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token" `
        -Method Post `
        -Body $tokenBody `
        -ErrorAction Stop

    $accessToken = $tokenResponse.access_token

    Write-Host "Got SharePoint access token" -ForegroundColor Green
    Write-Host ""

    # Step 2: Register owning app with container type (full permissions)
    Write-Host "Step 2: Registering owning app with container type..." -ForegroundColor Yellow
    Write-Host "  - Owning App: Full delegated + Full appOnly permissions" -ForegroundColor Gray

    $registrationBody = @{
        value = @(
            @{
                appId = $OwningAppId
                delegated = @("full")
                appOnly = @("full")
            }
        )
    } | ConvertTo-Json -Depth 3

    $headers = @{
        "Authorization" = "Bearer $accessToken"
        "Content-Type" = "application/json"
        "Accept" = "application/json"
    }

    $uri = "https://$SharePointDomain/_api/v2.1/storageContainerTypes/$ContainerTypeId/applicationPermissions"

    Write-Host "Calling: PUT $uri" -ForegroundColor Gray
    Write-Host ""

    $response = Invoke-RestMethod -Uri $uri `
        -Method Put `
        -Headers $headers `
        -Body $registrationBody `
        -ErrorAction Stop

    Write-Host "REGISTRATION SUCCESSFUL!" -ForegroundColor Green
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
    Write-Host "   az webapp restart --name <app-service-name> --resource-group <rg-name>" -ForegroundColor Gray
    Write-Host ""
    Write-Host "2. Test the upload endpoint:" -ForegroundColor White
    Write-Host "   PUT /api/containers/{containerId}/files/test.txt" -ForegroundColor Gray
    Write-Host ""
    Write-Host "3. Expected result: HTTP 200 OK (not 403 Forbidden)" -ForegroundColor White
    Write-Host ""

} catch {
    Write-Host "REGISTRATION FAILED" -ForegroundColor Red
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
    Write-Host "  - Verify the OwningAppId is the actual owner of this container type" -ForegroundColor Gray
    Write-Host "  - Use Find-ContainerTypeOwner.ps1 to check" -ForegroundColor Gray
    Write-Host "  - Requires SharePoint admin access to query container types" -ForegroundColor Gray
    Write-Host ""
    Write-Host "If error is 'Container.Selected permission required':" -ForegroundColor Yellow
    Write-Host "  - Grant FileStorageContainer.Selected permission to the owning app" -ForegroundColor Gray
    Write-Host "  - Azure Portal > App Registrations > owning app > API Permissions" -ForegroundColor Gray
    Write-Host "  - Add Microsoft Graph > Application > FileStorageContainer.Selected" -ForegroundColor Gray
    Write-Host "  - Grant admin consent" -ForegroundColor Gray
    Write-Host ""
}
