# Direct deployment to Azure Static Web Apps
# Run this in PowerShell directly (not through bash)

$ErrorActionPreference = "Stop"

Write-Host "=== Azure Static Web Apps Direct Deployment ===" -ForegroundColor Cyan

# Get deployment token from Azure
Write-Host "`nGetting deployment token from Azure..." -ForegroundColor Yellow
$token = az staticwebapp secrets list --name spaarke-office-addins --resource-group spe-infrastructure-westus2 --query 'properties.apiKey' -o tsv

if (!$token) {
    Write-Host "Failed to get deployment token!" -ForegroundColor Red
    exit 1
}

Write-Host "Token retrieved successfully!" -ForegroundColor Green

# Navigate to the office-addins directory
Set-Location src\client\office-addins

# Deploy using SWA CLI
Write-Host "`nDeploying to Azure Static Web Apps..." -ForegroundColor Yellow
Write-Host "This may take a few minutes..." -ForegroundColor Gray

npx @azure/static-web-apps-cli@latest deploy ./dist `
    --deployment-token $token `
    --env production

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n=== Deployment Successful! ===" -ForegroundColor Green
    Write-Host "`nVerifying deployment..." -ForegroundColor Yellow

    Start-Sleep -Seconds 5

    # Verify CORS headers are deployed
    Write-Host "`nChecking CORS headers on icon..." -ForegroundColor Yellow
    $response = Invoke-WebRequest -Uri "https://icy-desert-0bfdbb61e.6.azurestaticapps.net/assets/icon-16.png" -Method Head
    $corsHeader = $response.Headers['Access-Control-Allow-Origin']

    if ($corsHeader -eq '*') {
        Write-Host "✓ CORS headers deployed successfully!" -ForegroundColor Green
    } else {
        Write-Host "⚠ CORS header not found or incorrect. May need a few minutes to propagate." -ForegroundColor Yellow
    }

    Write-Host "`n=== Next Steps ===" -ForegroundColor Cyan
    Write-Host "1. Clear Office cache: Run .\clear-office-cache.ps1" -ForegroundColor White
    Write-Host "2. Test minimal manifest: src\client\office-addins\outlook\manifest-minimal.xml" -ForegroundColor White
    Write-Host "3. Sideload via: https://outlook.office365.com/mail/inclientstore" -ForegroundColor White

} else {
    Write-Host "`n=== Deployment Failed ===" -ForegroundColor Red
    Write-Host "Exit code: $LASTEXITCODE" -ForegroundColor Yellow
}

Set-Location ..\..\..
