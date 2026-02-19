<#
.SYNOPSIS
    Deploy Workspace BFF endpoints to Azure App Service.

.DESCRIPTION
    Deploys the Legal Operations Workspace BFF API (including all /api/workspace/* endpoints)
    to the Azure App Service. The BFF is deployed as a single application (ADR-001).

    Steps performed:
    1. Build API in Release mode (dotnet publish)
    2. Package and deploy to Azure App Service (az webapp deploy)
    3. Verify health endpoint (/healthz)
    4. Test all workspace endpoints

    Prerequisites:
    - Azure CLI installed and authenticated (az login)
    - Correct subscription selected
    - App Service spe-api-dev-67e2xz is running

.PARAMETER SkipBuild
    Skip the dotnet publish step (use existing ./publish folder).

.PARAMETER SkipEndpointTests
    Skip workspace endpoint smoke tests after deployment.

.PARAMETER Environment
    Target environment. Default: dev

.EXAMPLE
    .\Deploy-WorkspaceBff.ps1
    # Full build, deploy, and endpoint verification

.EXAMPLE
    .\Deploy-WorkspaceBff.ps1 -SkipBuild
    # Deploy existing build (faster for iterative deploys)

.EXAMPLE
    .\Deploy-WorkspaceBff.ps1 -SkipEndpointTests
    # Deploy without running endpoint smoke tests (useful if no auth token available)
#>

param(
    [switch]$SkipBuild,
    [switch]$SkipEndpointTests,
    [string]$Environment = "dev"
)

$ErrorActionPreference = "Stop"

# ---- Configuration -------------------------------------------------------

$RepoRoot       = Split-Path $PSScriptRoot -Parent
$ApiProject     = Join-Path $RepoRoot "src\server\api\Sprk.Bff.Api"
$PublishPath    = Join-Path $ApiProject "publish"
$ZipPath        = Join-Path $ApiProject "publish.zip"

# Azure resources (dev environment)
$ResourceGroup  = "spe-infrastructure-westus2"
$AppServiceName = "spe-api-dev-67e2xz"
$BaseUrl        = "https://spe-api-dev-67e2xz.azurewebsites.net"
$HealthCheckUrl = "$BaseUrl/healthz"

# Workspace endpoint URLs for smoke tests
$WorkspaceEndpoints = @(
    @{ Method = "GET";  Url = "$BaseUrl/api/workspace/portfolio";        Name = "Portfolio Summary"         },
    @{ Method = "GET";  Url = "$BaseUrl/api/workspace/health";           Name = "Health Metrics"            },
    @{ Method = "GET";  Url = "$BaseUrl/api/workspace/briefing";         Name = "Quick Summary Briefing"    },
    @{ Method = "POST"; Url = "$BaseUrl/api/workspace/calculate-scores"; Name = "Batch Score Calculation"   },
    @{ Method = "POST"; Url = "$BaseUrl/api/workspace/ai/summary";       Name = "AI Summary"                }
)

# ---- Banner ---------------------------------------------------------------

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Workspace BFF Endpoint Deployment" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Environment : $Environment" -ForegroundColor White
Write-Host "  App Service : $AppServiceName" -ForegroundColor White
Write-Host "  Resource Grp: $ResourceGroup" -ForegroundColor White
Write-Host "  Base URL    : $BaseUrl" -ForegroundColor White
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# ---- Step 1: Build --------------------------------------------------------

if (-not $SkipBuild) {
    Write-Host "[1/5] Building API in Release mode..." -ForegroundColor Yellow

    $publishArgs = @(
        "publish"
        $ApiProject
        "-c", "Release"
        "-o", $PublishPath
    )

    & dotnet @publishArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  ERROR: dotnet publish failed (exit code $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }

    Write-Host "  Build succeeded. Output: $PublishPath" -ForegroundColor Green
}
else {
    Write-Host "[1/5] Skipping build (using existing publish folder)..." -ForegroundColor Gray

    if (-not (Test-Path $PublishPath)) {
        Write-Host "  ERROR: Publish folder not found at $PublishPath" -ForegroundColor Red
        Write-Host "  Run without -SkipBuild to build first." -ForegroundColor Yellow
        exit 1
    }

    Write-Host "  Using existing publish folder: $PublishPath" -ForegroundColor Gray
}

# ---- Step 2: Package ------------------------------------------------------

Write-Host ""
Write-Host "[2/5] Creating deployment package..." -ForegroundColor Yellow

if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}

Compress-Archive -Path "$PublishPath\*" -DestinationPath $ZipPath -Force

$zipSizeMb = [math]::Round((Get-Item $ZipPath).Length / 1MB, 2)
Write-Host "  Package created: $ZipPath ($zipSizeMb MB)" -ForegroundColor Green

# ---- Step 3: Deploy to Azure App Service ----------------------------------

Write-Host ""
Write-Host "[3/5] Deploying to Azure App Service..." -ForegroundColor Yellow
Write-Host "  Target: $AppServiceName ($ResourceGroup)"
Write-Host "  This may take 30-90 seconds..."

$ErrorActionPreference = "Continue"
$deployOutput = az webapp deploy `
    --resource-group $ResourceGroup `
    --name $AppServiceName `
    --src-path $ZipPath `
    --type zip `
    --async false 2>&1 | Out-String
$deployExitCode = $LASTEXITCODE
$ErrorActionPreference = "Stop"

if ($deployExitCode -ne 0) {
    Write-Host "  ERROR: Deployment failed (exit code $deployExitCode)" -ForegroundColor Red
    Write-Host $deployOutput -ForegroundColor Red
    Write-Host ""
    Write-Host "  Troubleshooting:" -ForegroundColor Yellow
    Write-Host "    1. Verify az login: az account show"
    Write-Host "    2. Verify subscription: az account set --subscription <id>"
    Write-Host "    3. Check App Service status in Azure Portal"
    exit $deployExitCode
}

Write-Host "  Deployment complete." -ForegroundColor Green

# Wait for App Service to restart after deployment
Write-Host "  Waiting 15 seconds for app restart..." -ForegroundColor Gray
Start-Sleep -Seconds 15

# ---- Step 4: Verify Health Endpoint ---------------------------------------

Write-Host ""
Write-Host "[4/5] Verifying health endpoint..." -ForegroundColor Yellow
Write-Host "  GET $HealthCheckUrl"

$MaxRetries  = 8
$RetryDelay  = 5
$RetryCount  = 0
$IsHealthy   = $false

while ($RetryCount -lt $MaxRetries -and -not $IsHealthy) {
    try {
        $response = Invoke-RestMethod -Uri $HealthCheckUrl -TimeoutSec 15 -Method GET

        if ($response -eq "Healthy" -or $response.status -eq "Healthy" -or $response -match "Healthy") {
            $IsHealthy = $true
            Write-Host "  Health check PASSED." -ForegroundColor Green
        }
        else {
            Write-Host "  Unexpected health response: $response" -ForegroundColor Yellow
            $RetryCount++
            Start-Sleep -Seconds $RetryDelay
        }
    }
    catch {
        $RetryCount++
        if ($RetryCount -lt $MaxRetries) {
            Write-Host "  Waiting for API to start... (attempt $RetryCount/$MaxRetries)" -ForegroundColor Gray
            Start-Sleep -Seconds $RetryDelay
        }
    }
}

if (-not $IsHealthy) {
    Write-Host "  ERROR: Health check failed after $MaxRetries attempts." -ForegroundColor Red
    Write-Host "  Check: $HealthCheckUrl" -ForegroundColor Yellow
    Write-Host "  Check App Service logs in Azure Portal for startup errors." -ForegroundColor Yellow
    exit 1
}

# ---- Step 5: Workspace Endpoint Smoke Tests -------------------------------

if ($SkipEndpointTests) {
    Write-Host ""
    Write-Host "[5/5] Skipping workspace endpoint smoke tests (-SkipEndpointTests)." -ForegroundColor Gray
}
else {
    Write-Host ""
    Write-Host "[5/5] Testing workspace endpoints (expect 401 without auth token)..." -ForegroundColor Yellow
    Write-Host "  NOTE: These tests verify the endpoints are reachable." -ForegroundColor Gray
    Write-Host "  A 401 Unauthorized response confirms the endpoint exists and auth is enforced (ADR-008)." -ForegroundColor Gray
    Write-Host ""

    $EndpointResults = @()

    foreach ($ep in $WorkspaceEndpoints) {
        try {
            $params = @{
                Uri         = $ep.Url
                Method      = $ep.Method
                TimeoutSec  = 15
                ErrorAction = "Stop"
            }

            # For POST endpoints, send a minimal JSON body to avoid 400 on missing body
            if ($ep.Method -eq "POST") {
                $params.Body        = "{}"
                $params.ContentType = "application/json"
            }

            $r = Invoke-RestMethod @params
            $EndpointResults += [PSCustomObject]@{
                Name   = $ep.Name
                Method = $ep.Method
                Status = "200 OK (unexpected — check auth config)"
                Pass   = $false
            }
        }
        catch {
            $statusCode = $_.Exception.Response?.StatusCode
            $statusInt  = [int]$statusCode

            if ($statusInt -eq 401) {
                # Expected: endpoint exists and auth is enforced
                Write-Host "  [PASS] $($ep.Method) $($ep.Name) → 401 Unauthorized (auth enforced)" -ForegroundColor Green
                $EndpointResults += [PSCustomObject]@{
                    Name   = $ep.Name
                    Method = $ep.Method
                    Status = "401 Unauthorized"
                    Pass   = $true
                }
            }
            elseif ($statusInt -eq 400) {
                # POST endpoints may return 400 with empty body (expected before auth)
                Write-Host "  [PASS] $($ep.Method) $($ep.Name) → 400 Bad Request (endpoint reachable)" -ForegroundColor Green
                $EndpointResults += [PSCustomObject]@{
                    Name   = $ep.Name
                    Method = $ep.Method
                    Status = "400 Bad Request"
                    Pass   = $true
                }
            }
            elseif ($statusInt -eq 429) {
                Write-Host "  [PASS] $($ep.Method) $($ep.Name) → 429 Too Many Requests (rate limiting active)" -ForegroundColor Green
                $EndpointResults += [PSCustomObject]@{
                    Name   = $ep.Name
                    Method = $ep.Method
                    Status = "429 Too Many Requests"
                    Pass   = $true
                }
            }
            elseif ($statusInt -eq 404) {
                Write-Host "  [FAIL] $($ep.Method) $($ep.Name) → 404 Not Found" -ForegroundColor Red
                Write-Host "         Endpoint may not be registered in Program.cs" -ForegroundColor Red
                $EndpointResults += [PSCustomObject]@{
                    Name   = $ep.Name
                    Method = $ep.Method
                    Status = "404 Not Found — ENDPOINT MISSING"
                    Pass   = $false
                }
            }
            elseif ($statusInt -ge 500) {
                Write-Host "  [FAIL] $($ep.Method) $($ep.Name) → $statusInt Server Error" -ForegroundColor Red
                $EndpointResults += [PSCustomObject]@{
                    Name   = $ep.Name
                    Method = $ep.Method
                    Status = "$statusInt Server Error"
                    Pass   = $false
                }
            }
            else {
                Write-Host "  [WARN] $($ep.Method) $($ep.Name) → $statusInt $statusCode" -ForegroundColor Yellow
                $EndpointResults += [PSCustomObject]@{
                    Name   = $ep.Name
                    Method = $ep.Method
                    Status = "$statusInt $statusCode"
                    Pass   = $false
                }
            }
        }
    }

    # Summary table
    Write-Host ""
    Write-Host "  Workspace Endpoint Test Results:" -ForegroundColor Cyan
    $EndpointResults | Format-Table -AutoSize

    $FailedCount = ($EndpointResults | Where-Object { -not $_.Pass }).Count
    if ($FailedCount -gt 0) {
        Write-Host "  WARNING: $FailedCount endpoint(s) failed smoke tests." -ForegroundColor Yellow
        Write-Host "  Review the results above and check App Service logs." -ForegroundColor Yellow
    }
    else {
        Write-Host "  All workspace endpoints are reachable." -ForegroundColor Green
    }
}

# ---- Deployment Summary ---------------------------------------------------

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "  Deployment Complete" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host "  App Service : $AppServiceName" -ForegroundColor White
Write-Host "  Base URL    : $BaseUrl" -ForegroundColor White
Write-Host "  Health Check: $HealthCheckUrl" -ForegroundColor White
Write-Host ""
Write-Host "  Workspace Endpoints:" -ForegroundColor White
Write-Host "    GET  $BaseUrl/api/workspace/portfolio" -ForegroundColor Gray
Write-Host "    GET  $BaseUrl/api/workspace/health" -ForegroundColor Gray
Write-Host "    GET  $BaseUrl/api/workspace/briefing" -ForegroundColor Gray
Write-Host "    POST $BaseUrl/api/workspace/calculate-scores" -ForegroundColor Gray
Write-Host "    POST $BaseUrl/api/workspace/ai/summary" -ForegroundColor Gray
Write-Host ""
Write-Host "  Quick test commands:" -ForegroundColor White
Write-Host "    Invoke-RestMethod $HealthCheckUrl" -ForegroundColor Gray
Write-Host "    curl $HealthCheckUrl" -ForegroundColor Gray
Write-Host "============================================================" -ForegroundColor Green
Write-Host ""
