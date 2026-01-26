<#
.SYNOPSIS
    Deploy BFF API directly to Azure App Service (bypasses GitHub Actions)

.DESCRIPTION
    Fast deployment for dev iteration:
    1. Builds the API in Release mode
    2. Creates deployment package
    3. Deploys via Azure CLI
    4. Verifies health check

    Use this instead of pushing to master when testing API changes.

.PARAMETER SkipBuild
    Skip the build step (use existing publish folder)

.PARAMETER Environment
    Target environment (default: dev)

.EXAMPLE
    .\Deploy-BffApi.ps1
    # Full build and deploy

.EXAMPLE
    .\Deploy-BffApi.ps1 -SkipBuild
    # Deploy existing build (faster)
#>

param(
    [switch]$SkipBuild,
    [string]$Environment = "dev"
)

$ErrorActionPreference = "Stop"

# Configuration
$ApiProject = "$PSScriptRoot\..\src\server\api\Sprk.Bff.Api"
$PublishPath = "$ApiProject\publish"
$ZipPath = "$ApiProject\publish.zip"

# Azure resources (dev environment)
$ResourceGroup = "spe-infrastructure-westus2"
$AppServiceName = "spe-api-dev-67e2xz"
$HealthCheckUrl = "https://spe-api-dev-67e2xz.azurewebsites.net/healthz"

Write-Host "=== BFF API Deployment ===" -ForegroundColor Cyan
Write-Host "Environment: $Environment"
Write-Host "App Service: $AppServiceName"
Write-Host ""

# Step 1: Build
if (-not $SkipBuild) {
    Write-Host "[1/4] Building API..." -ForegroundColor Yellow
    Push-Location $ApiProject
    try {
        dotnet publish -c Release -o ./publish --no-restore 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed"
        }
        Write-Host "  Build successful" -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
} else {
    Write-Host "[1/4] Skipping build (using existing publish)" -ForegroundColor Gray
    if (-not (Test-Path $PublishPath)) {
        throw "Publish folder not found. Run without -SkipBuild first."
    }
}

# Step 2: Package
Write-Host "[2/4] Creating deployment package..." -ForegroundColor Yellow
if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}
Compress-Archive -Path "$PublishPath\*" -DestinationPath $ZipPath -Force
$zipSize = (Get-Item $ZipPath).Length / 1MB
Write-Host "  Package created: $([math]::Round($zipSize, 2)) MB" -ForegroundColor Green

# Step 3: Deploy
Write-Host "[3/4] Deploying to Azure..." -ForegroundColor Yellow
Write-Host "  This may take 30-60 seconds..."

# Capture both stdout and stderr, ignore warnings
$ErrorActionPreference = "Continue"
$deployOutput = az webapp deploy `
    --resource-group $ResourceGroup `
    --name $AppServiceName `
    --src-path $ZipPath `
    --type zip `
    --async false 2>&1 | Out-String

$ErrorActionPreference = "Stop"

# Check for actual failure (not just warnings)
if ($LASTEXITCODE -ne 0) {
    Write-Host "  Deploy command failed:" -ForegroundColor Red
    Write-Host $deployOutput
    throw "Deployment failed"
}
Write-Host "  Deployment complete" -ForegroundColor Green

# Wait for app to restart
Write-Host "  Waiting for app restart..." -ForegroundColor Gray
Start-Sleep -Seconds 10

# Step 4: Verify
Write-Host "[4/4] Verifying deployment..." -ForegroundColor Yellow
$maxRetries = 6
$retryCount = 0
$healthy = $false

while ($retryCount -lt $maxRetries -and -not $healthy) {
    try {
        $response = Invoke-RestMethod -Uri $HealthCheckUrl -TimeoutSec 10
        if ($response -eq "Healthy" -or $response.status -eq "Healthy") {
            $healthy = $true
            Write-Host "  Health check passed!" -ForegroundColor Green
        }
    }
    catch {
        $retryCount++
        if ($retryCount -lt $maxRetries) {
            Write-Host "  Waiting for API to start... (attempt $retryCount/$maxRetries)" -ForegroundColor Gray
            Start-Sleep -Seconds 5
        }
    }
}

if (-not $healthy) {
    Write-Host "  Health check failed after $maxRetries attempts" -ForegroundColor Red
    Write-Host "  Check: $HealthCheckUrl" -ForegroundColor Yellow
    exit 1
}

# Summary
Write-Host ""
Write-Host "=== Deployment Complete ===" -ForegroundColor Green
Write-Host "API URL: https://$AppServiceName.azurewebsites.net"
Write-Host "Health:  $HealthCheckUrl"
Write-Host ""
Write-Host "Quick test commands:" -ForegroundColor Gray
Write-Host "  curl $HealthCheckUrl"
Write-Host "  curl https://$AppServiceName.azurewebsites.net/ping"
