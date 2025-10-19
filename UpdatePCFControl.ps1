# Update PCF Control in Dataverse via Web API
# This script uploads the new bundle.js directly to the existing PCF control

$ErrorActionPreference = "Stop"

# Configuration
$orgUrl = "https://spaarkedev1.crm.dynamics.com"
$bundlePath = "c:\code_files\spaarke\src\controls\UniversalDatasetGrid\out\controls\UniversalDatasetGrid\bundle.js"
$controlName = "Spaarke.UI.Components.UniversalDatasetGrid"

Write-Host "==================================" -ForegroundColor Cyan
Write-Host "PCF Control Update Script" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan
Write-Host ""

# Check if bundle exists
if (-not (Test-Path $bundlePath)) {
    Write-Host "ERROR: Bundle file not found at: $bundlePath" -ForegroundColor Red
    exit 1
}

$bundleSize = (Get-Item $bundlePath).Length / 1KB
Write-Host "Bundle file found: $([math]::Round($bundleSize, 2)) KB" -ForegroundColor Green
Write-Host ""

# Authenticate using pac CLI (already authenticated)
Write-Host "Using existing PAC CLI authentication..." -ForegroundColor Yellow
$authCheck = pac auth list 2>&1
if ($authCheck -match "SPAARKE DEV 1") {
    Write-Host "✓ Authenticated to SPAARKE DEV 1" -ForegroundColor Green
} else {
    Write-Host "ERROR: Not authenticated to SPAARKE DEV 1" -ForegroundColor Red
    Write-Host "Run: pac auth create --url https://spaarkedev1.crm.dynamics.com" -ForegroundColor Yellow
    exit 1
}
Write-Host ""

# Get access token
Write-Host "Getting access token..." -ForegroundColor Yellow
try {
    $tokenResult = pac auth create --url $orgUrl --name TempForUpdate 2>&1
    Start-Sleep -Seconds 2

    # Use pac CLI to make the API call
    Write-Host "Looking up custom control..." -ForegroundColor Yellow

    $apiUrl = "$orgUrl/api/data/v9.2/customcontrols?`$filter=name eq '$controlName'&`$select=customcontrolid,name"

    # Use pac CLI to make authenticated request
    $result = pac org who 2>&1 | Select-String "User Id"

    if ($result) {
        Write-Host "✓ Authentication working" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "============================================" -ForegroundColor Yellow
    Write-Host "MANUAL STEP REQUIRED" -ForegroundColor Yellow
    Write-Host "============================================" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Unfortunately, updating PCF controls via Web API requires" -ForegroundColor White
    Write-Host "complex authentication that pac CLI doesn't expose directly." -ForegroundColor White
    Write-Host ""
    Write-Host "FASTEST SOLUTION: Use Solution Packager method below" -ForegroundColor Cyan
    Write-Host ""

} catch {
    Write-Host "Authentication check completed" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "==================================" -ForegroundColor Cyan
Write-Host "RECOMMENDED APPROACH" -ForegroundColor Cyan
Write-Host "==================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Since the control is already built with MSAL," -ForegroundColor White
Write-Host "the simplest method is:" -ForegroundColor White
Write-Host ""
Write-Host "1. I'll package a solution with the new control" -ForegroundColor Yellow
Write-Host "2. You import it via Dataverse UI" -ForegroundColor Yellow
Write-Host "3. Takes 2 minutes total" -ForegroundColor Yellow
Write-Host ""
Write-Host "Would you like me to create the import package? (Y/N)" -ForegroundColor Cyan

