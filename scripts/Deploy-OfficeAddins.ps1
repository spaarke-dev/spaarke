<#
.SYNOPSIS
    Deploys Office Add-ins to Azure Static Web App.

.DESCRIPTION
    Builds and deploys the Office Add-ins (Outlook, Word) to the Azure Static Web App.
    Handles the SWA CLI spinner issue by redirecting output to a log file.
    Gets a fresh deployment token each time to avoid token expiration issues.

.PARAMETER SkipBuild
    Skip the webpack build step (use existing dist folder).

.PARAMETER Environment
    Target environment: 'production' (default) or 'preview'.

.PARAMETER Verbose
    Show detailed output during deployment.

.EXAMPLE
    .\Deploy-OfficeAddins.ps1
    # Full build and deploy to production

.EXAMPLE
    .\Deploy-OfficeAddins.ps1 -SkipBuild
    # Deploy existing dist folder without rebuilding

.EXAMPLE
    .\Deploy-OfficeAddins.ps1 -Environment preview
    # Deploy to preview environment

.NOTES
    Requires:
    - Azure CLI (az) installed and logged in
    - Node.js and npm
    - SWA CLI (@azure/static-web-apps-cli)
#>

param(
    [switch]$SkipBuild,
    [ValidateSet('production', 'preview')]
    [string]$Environment = 'production',
    [switch]$Verbose
)

$ErrorActionPreference = 'Stop'

# Configuration
$SwaName = 'spaarke-office-addins'
$ResourceGroup = 'spe-infrastructure-westus2'
$SwaUrl = 'https://icy-desert-0bfdbb61e.6.azurestaticapps.net'
$SourceDir = Join-Path $PSScriptRoot '..\src\client\office-addins'
$DistDir = Join-Path $SourceDir 'dist'
$LogFile = Join-Path $env:TEMP 'swa-deploy.log'

Write-Host "`n=== Office Add-ins Deployment ===" -ForegroundColor Cyan
Write-Host "Environment: $Environment" -ForegroundColor Gray
Write-Host "Source: $SourceDir" -ForegroundColor Gray

# Step 1: Build (unless skipped)
if (-not $SkipBuild) {
    Write-Host "`n[1/4] Building production bundle..." -ForegroundColor Yellow
    Push-Location $SourceDir
    try {
        $buildOutput = & npx webpack --mode production 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Build failed!" -ForegroundColor Red
            Write-Host $buildOutput
            exit 1
        }
        Write-Host "Build completed successfully" -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
} else {
    Write-Host "`n[1/4] Skipping build (using existing dist)" -ForegroundColor Gray
}

# Verify dist folder exists
if (-not (Test-Path $DistDir)) {
    Write-Host "Error: dist folder not found at $DistDir" -ForegroundColor Red
    exit 1
}

# Step 2: Get fresh deployment token
Write-Host "`n[2/4] Getting deployment token..." -ForegroundColor Yellow
try {
    $token = & az staticwebapp secrets list `
        --name $SwaName `
        --resource-group $ResourceGroup `
        --query properties.apiKey `
        -o tsv 2>&1

    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) {
        Write-Host "Failed to get deployment token. Are you logged into Azure?" -ForegroundColor Red
        Write-Host "Run: az login" -ForegroundColor Yellow
        exit 1
    }
    Write-Host "Token retrieved successfully" -ForegroundColor Green
}
catch {
    Write-Host "Error getting deployment token: $_" -ForegroundColor Red
    exit 1
}

# Step 3: Deploy using SWA CLI
Write-Host "`n[3/4] Deploying to Azure Static Web App..." -ForegroundColor Yellow
Write-Host "  This may take 30-60 seconds..." -ForegroundColor Gray

Push-Location $SourceDir
try {
    # Use Start-Process to handle the spinner output issue
    $process = Start-Process -FilePath 'powershell.exe' `
        -ArgumentList "-NoProfile", "-Command", "npx swa deploy ./dist --deployment-token '$token' --env $Environment *> '$LogFile'" `
        -NoNewWindow -Wait -PassThru

    # Read the log file
    if (Test-Path $LogFile) {
        $logContent = Get-Content $LogFile -Raw

        if ($Verbose) {
            Write-Host "`nDeployment log:" -ForegroundColor Gray
            Write-Host $logContent
        }

        # Check for success
        if ($logContent -match 'deployed to') {
            Write-Host "Deployment completed successfully!" -ForegroundColor Green
        }
        elseif ($logContent -match 'error|failed') {
            Write-Host "Deployment may have failed. Check log:" -ForegroundColor Red
            Write-Host $logContent
            exit 1
        }
        else {
            Write-Host "Deployment status unclear. Log:" -ForegroundColor Yellow
            Write-Host $logContent
        }
    }
}
finally {
    Pop-Location
}

# Step 4: Verify deployment
Write-Host "`n[4/4] Verifying deployment..." -ForegroundColor Yellow
$timestamp = Get-Date -Format 'HHmmss'
$verifyUrl = "$SwaUrl/outlook/manifest.xml?v=$timestamp"

try {
    $response = Invoke-WebRequest -Uri $verifyUrl -UseBasicParsing -TimeoutSec 10

    # Extract version from manifest
    if ($response.Content -match '<Version>([^<]+)</Version>') {
        $version = $Matches[1]
        Write-Host "  Manifest version: $version" -ForegroundColor Cyan
    }

    Write-Host "  URL: $SwaUrl" -ForegroundColor Green
    Write-Host "`nDeployment verified!" -ForegroundColor Green
}
catch {
    Write-Host "Warning: Could not verify deployment. Check manually: $SwaUrl" -ForegroundColor Yellow
}

# Summary
Write-Host "`n=== Deployment Summary ===" -ForegroundColor Cyan
Write-Host "Static Web App: $SwaUrl"
Write-Host "Manifest: $SwaUrl/outlook/manifest.xml"
Write-Host "Taskpane: $SwaUrl/outlook/taskpane.html"
Write-Host "`nIf manifest changed, upload to M365 Admin Center:" -ForegroundColor Yellow
Write-Host "  1. Download: $SwaUrl/outlook/manifest.xml"
Write-Host "  2. Upload to: admin.microsoft.com > Integrated Apps"
