# Check what apps are registered with the ContainerType

$ContainerTypeId = "8a6ce34c-6055-4681-8f87-2f4f9f921c06"
$SharePointDomain = "spaarke.sharepoint.com"

Write-Host "Checking ContainerType registration..." -ForegroundColor Yellow
Write-Host "Container Type ID: $ContainerTypeId" -ForegroundColor Gray
Write-Host ""

try {
    # Get access token for SharePoint
    $token = (Get-AzAccessToken -ResourceUrl "https://$SharePointDomain").Token

    # Call SharePoint API to get registrations
    $url = "https://$SharePointDomain/_api/v2.1/storageContainerTypes/$ContainerTypeId/applicationPermissions"
    $response = Invoke-RestMethod -Uri $url -Headers @{
        'Authorization' = "Bearer $token"
    } -Method Get

    Write-Host "✅ Successfully retrieved registrations" -ForegroundColor Green
    Write-Host ""
    Write-Host "Registered Applications:" -ForegroundColor Cyan
    Write-Host ""

    foreach ($app in $response.value) {
        Write-Host "App ID: $($app.appId)" -ForegroundColor White
        Write-Host "  Delegated: $($app.delegated -join ', ')" -ForegroundColor Gray
        Write-Host "  App-Only: $($app.appOnly -join ', ')" -ForegroundColor Gray
        Write-Host ""
    }

    # Check for our specific apps
    $owningApp = $response.value | Where-Object { $_.appId -eq "170c98e1-d486-4355-bcbe-170454e0207c" }
    $bffApp = $response.value | Where-Object { $_.appId -eq "1e40baad-e065-4aea-a8d4-4b7ab273458c" }

    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host "VERIFICATION" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host ""

    if ($owningApp) {
        Write-Host "✅ Owning App (170c98e1) registered" -ForegroundColor Green
    } else {
        Write-Host "❌ Owning App (170c98e1) NOT FOUND" -ForegroundColor Red
    }

    if ($bffApp) {
        Write-Host "✅ BFF API (1e40baad) registered" -ForegroundColor Green
    } else {
        Write-Host "❌ BFF API (1e40baad) NOT FOUND" -ForegroundColor Red
    }

} catch {
    Write-Host "❌ Failed to retrieve registrations" -ForegroundColor Red
    Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Gray

    if ($_.ErrorDetails.Message) {
        Write-Host ""
        Write-Host "Details:" -ForegroundColor Gray
        $_.ErrorDetails.Message | Write-Host -ForegroundColor Gray
    }
}
