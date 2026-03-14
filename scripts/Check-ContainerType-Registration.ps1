# Check what apps are registered with a SharePoint Embedded Container Type

param(
    [Parameter(Mandatory)][string]$ContainerTypeId,
    [Parameter(Mandatory)][string]$SharePointDomain,  # e.g., "spaarke.sharepoint.com"
    [string]$OwningAppId  # Optional: verify this app is registered as owner
)

Write-Host "Checking ContainerType registration..." -ForegroundColor Yellow
Write-Host "Container Type ID:  $ContainerTypeId" -ForegroundColor Gray
Write-Host "SharePoint Domain:  $SharePointDomain" -ForegroundColor Gray
Write-Host ""

try {
    # Get access token for SharePoint
    $token = (Get-AzAccessToken -ResourceUrl "https://$SharePointDomain").Token

    # Call SharePoint API to get registrations
    $url = "https://$SharePointDomain/_api/v2.1/storageContainerTypes/$ContainerTypeId/applicationPermissions"
    $response = Invoke-RestMethod -Uri $url -Headers @{
        'Authorization' = "Bearer $token"
    } -Method Get

    Write-Host "Successfully retrieved registrations" -ForegroundColor Green
    Write-Host ""
    Write-Host "Registered Applications:" -ForegroundColor Cyan
    Write-Host ""

    foreach ($app in $response.value) {
        Write-Host "App ID: $($app.appId)" -ForegroundColor White
        Write-Host "  Delegated: $($app.delegated -join ', ')" -ForegroundColor Gray
        Write-Host "  App-Only:  $($app.appOnly -join ', ')" -ForegroundColor Gray
        Write-Host ""
    }

    # Optional: verify specific owning app
    if ($OwningAppId) {
        Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
        Write-Host "VERIFICATION" -ForegroundColor Cyan
        Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
        Write-Host ""

        $owningApp = $response.value | Where-Object { $_.appId -eq $OwningAppId }
        if ($owningApp) {
            Write-Host "Owning App ($OwningAppId) registered" -ForegroundColor Green
            Write-Host "  Delegated: $($owningApp.delegated -join ', ')" -ForegroundColor Gray
            Write-Host "  App-Only:  $($owningApp.appOnly -join ', ')" -ForegroundColor Gray
        } else {
            Write-Host "Owning App ($OwningAppId) NOT FOUND in registrations" -ForegroundColor Red
        }
    }

} catch {
    Write-Host "Failed to retrieve registrations" -ForegroundColor Red
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Gray

    if ($_.ErrorDetails.Message) {
        Write-Host ""
        Write-Host "Details:" -ForegroundColor Gray
        $_.ErrorDetails.Message | Write-Host -ForegroundColor Gray
    }
}
