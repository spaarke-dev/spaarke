# Deploy to Azure Static Web Apps
# Run this script to deploy the Office Add-ins

param(
    [Parameter(Mandatory=$false)]
    [string]$DeploymentToken
)

Write-Host "Azure Static Web Apps Direct Deployment" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Check if dist folder exists
$distPath = "src\client\office-addins\dist"
if (!(Test-Path $distPath)) {
    Write-Host "Error: dist folder not found. Run 'npm run build' first." -ForegroundColor Red
    exit 1
}

Write-Host "`nDist folder contents:" -ForegroundColor Yellow
Get-ChildItem $distPath -Recurse -File | Select-Object FullName, Length | Format-Table -AutoSize

# Method 1: Using SWA CLI (recommended)
Write-Host "`n=== Method 1: Using SWA CLI ===" -ForegroundColor Green
Write-Host "Run this command manually with your deployment token:" -ForegroundColor Yellow
Write-Host @"

cd src\client\office-addins
npx @azure/static-web-apps-cli deploy ./dist `
  --deployment-token '<YOUR-DEPLOYMENT-TOKEN-HERE>' `
  --env production

"@ -ForegroundColor Cyan

# Method 2: Get deployment token from GitHub secret
Write-Host "`n=== Method 2: Get deployment token from GitHub ===" -ForegroundColor Green
Write-Host "To get your deployment token, run:" -ForegroundColor Yellow
Write-Host "  1. Go to Azure Portal" -ForegroundColor White
Write-Host "  2. Navigate to Static Web Apps > icy-desert-0bfdbb61e" -ForegroundColor White
Write-Host "  3. Go to 'Overview' > 'Manage deployment token'" -ForegroundColor White
Write-Host "  4. Copy the deployment token" -ForegroundColor White
Write-Host "  5. Run: .\deploy-swa.ps1 -DeploymentToken '<token>'" -ForegroundColor White

# Method 3: Direct ZIP upload (fallback)
Write-Host "`n=== Method 3: Manual ZIP Upload ===" -ForegroundColor Green
$zipPath = "office-addins-dist.zip"
Write-Host "Creating ZIP file: $zipPath" -ForegroundColor Yellow
Compress-Archive -Path "$distPath\*" -DestinationPath $zipPath -Force
Write-Host "ZIP created successfully!" -ForegroundColor Green
Write-Host "`nTo deploy manually:" -ForegroundColor Yellow
Write-Host "  1. Go to Azure Portal" -ForegroundColor White
Write-Host "  2. Navigate to Static Web Apps > icy-desert-0bfdbb61e" -ForegroundColor White
Write-Host "  3. Use Azure CLI: az staticwebapp upload --resource-group <rg> --name <name> --source-path $zipPath" -ForegroundColor White

# If deployment token is provided, deploy
if ($DeploymentToken) {
    Write-Host "`n=== Deploying with provided token ===" -ForegroundColor Green
    Set-Location src\client\office-addins
    npx @azure/static-web-apps-cli deploy ./dist `
        --deployment-token $DeploymentToken `
        --env production
    Set-Location ..\..\..
} else {
    Write-Host "`nProvide deployment token to auto-deploy: .\deploy-swa.ps1 -DeploymentToken '<token>'" -ForegroundColor Yellow
}
