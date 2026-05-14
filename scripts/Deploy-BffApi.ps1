<#
.SYNOPSIS
    Deploy BFF API to Azure App Service with optional staging slot and zero-downtime swap.

.DESCRIPTION
    Multi-environment deployment for the Spaarke BFF API:
    1. Builds the API in Release mode
    2. Creates deployment package (zip)
    3. Deploys to target slot (production direct or staging slot)
    4. Verifies health check on deployed slot
    5. Swaps staging -> production (if using slot deploy)
    6. Verifies health check on production after swap
    7. Rolls back via swap-back if post-swap health check fails

    Supports dev, staging, and production environments.
    Default parameters preserve backward compatibility with existing dev workflow.

.PARAMETER SkipBuild
    Skip the build step (use existing publish folder).

.PARAMETER Environment
    Target environment name (dev, staging, production). Used for display and publish configuration.
    Default: dev

.PARAMETER ResourceGroupName
    Azure resource group containing the App Service.
    Default: spe-infrastructure-westus2 (dev environment)

.PARAMETER AppServiceName
    Azure App Service name to deploy to.
    Default: spe-api-dev-67e2xz (dev environment)

.PARAMETER UseSlotDeploy
    Deploy to staging slot first, then swap after health check.
    Enables zero-downtime deployment. Required for production.
    Default: $false (direct deploy for dev)

.PARAMETER SlotName
    Name of the deployment slot to use when UseSlotDeploy is enabled.
    Default: staging

.PARAMETER HealthCheckPath
    Path for health check verification (appended to App Service URL).
    Default: /healthz

.PARAMETER RollbackOnFailure
    If post-swap health check fails, automatically swap back to previous version.
    Only applies when UseSlotDeploy is enabled.
    Default: $true

.PARAMETER MaxHealthCheckRetries
    Maximum number of health check retry attempts before declaring failure.
    Default: 12

.PARAMETER HealthCheckIntervalSeconds
    Seconds to wait between health check retries.
    Default: 5

.PARAMETER PublishPath
    Override the publish output directory.
    Default: deploy/api-publish (outside project source tree per deployment constraints)

.EXAMPLE
    .\Deploy-BffApi.ps1
    # Dev deployment: build, deploy directly, verify health check (backward compatible)

.EXAMPLE
    .\Deploy-BffApi.ps1 -SkipBuild
    # Dev deployment using existing build (faster iteration)

.EXAMPLE
    .\Deploy-BffApi.ps1 -Environment production `
        -ResourceGroupName "rg-spaarke-platform-prod" `
        -AppServiceName "spaarke-bff-prod" `
        -UseSlotDeploy
    # Production: build, deploy to staging slot, health check, swap, verify, rollback on failure

.EXAMPLE
    .\Deploy-BffApi.ps1 -Environment production `
        -ResourceGroupName "rg-spaarke-platform-prod" `
        -AppServiceName "spaarke-bff-prod" `
        -UseSlotDeploy -SkipBuild
    # Production: deploy existing build to staging slot with zero-downtime swap
#>

param(
    [switch]$SkipBuild,

    [ValidateSet("dev", "staging", "production")]
    [string]$Environment = "dev",

    [string]$ResourceGroupName = "spe-infrastructure-westus2",

    [string]$AppServiceName = "spe-api-dev-67e2xz",

    [switch]$UseSlotDeploy,

    [string]$SlotName = "staging",

    [string]$HealthCheckPath = "/healthz",

    [bool]$RollbackOnFailure = $true,

    # Default 24 retries × 5 s = 120 s. Sized for Linux App Service cold start
    # after a stop -> Kudu zipdeploy -> start cycle (the hardened script's
    # auto-recover path), which can take 90-120 s before /healthz responds.
    # Windows direct deploys typically respond in <30 s but pay no penalty
    # for the longer ceiling.
    [int]$MaxHealthCheckRetries = 24,

    [int]$HealthCheckIntervalSeconds = 5,

    [string]$PublishPath
)

$ErrorActionPreference = "Stop"

# --- Configuration ---
$RepoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$ApiProject = "$RepoRoot\src\server\api\Sprk.Bff.Api"

if (-not $PublishPath) {
    $PublishPath = "$RepoRoot\deploy\api-publish"
}
$ZipPath = "$RepoRoot\deploy\api-publish.zip"

# Construct URLs
$BaseUrl = "https://$AppServiceName.azurewebsites.net"
$ProductionHealthCheckUrl = "$BaseUrl$HealthCheckPath"

if ($UseSlotDeploy) {
    $SlotUrl = "https://$AppServiceName-$SlotName.azurewebsites.net"
    $SlotHealthCheckUrl = "$SlotUrl$HealthCheckPath"
}

# --- Display Configuration ---
$totalSteps = if ($UseSlotDeploy) { 7 } else { 4 }

Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  BFF API Deployment" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Environment:     $Environment"
Write-Host "  Resource Group:  $ResourceGroupName"
Write-Host "  App Service:     $AppServiceName"
Write-Host "  Deploy Mode:     $(if ($UseSlotDeploy) { "Staging Slot ($SlotName) -> Swap" } else { "Direct Deploy" })"
Write-Host "  Health Check:    $HealthCheckPath"
if ($UseSlotDeploy) {
    Write-Host "  Rollback:        $(if ($RollbackOnFailure) { 'Enabled (auto swap-back on failure)' } else { 'Disabled' })"
}
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""

# Warn if deploying to production without slot deploy
if ($Environment -eq "production" -and -not $UseSlotDeploy) {
    Write-Host "  WARNING: Production deployment without staging slot!" -ForegroundColor Red
    Write-Host "  This will cause downtime. Use -UseSlotDeploy for zero-downtime." -ForegroundColor Red
    Write-Host ""
    $confirm = Read-Host "  Continue without slot deploy? (y/N)"
    if ($confirm -ne "y") {
        Write-Host "  Deployment cancelled." -ForegroundColor Yellow
        exit 0
    }
}

# --- Helper Functions ---

function Test-HealthCheck {
    param(
        [string]$Url,
        [int]$MaxRetries,
        [int]$IntervalSeconds,
        [string]$Label
    )

    Write-Host "  Checking health: $Url" -ForegroundColor Gray
    $retryCount = 0
    $healthy = $false

    while ($retryCount -lt $MaxRetries -and -not $healthy) {
        try {
            $response = Invoke-RestMethod -Uri $Url -TimeoutSec 10 -ErrorAction Stop
            if ($response -eq "Healthy" -or $response.status -eq "Healthy") {
                $healthy = $true
                Write-Host "  $Label health check passed!" -ForegroundColor Green
            }
        }
        catch {
            $retryCount++
            if ($retryCount -lt $MaxRetries) {
                Write-Host "  Waiting for $Label to respond... (attempt $retryCount/$MaxRetries)" -ForegroundColor Gray
                Start-Sleep -Seconds $IntervalSeconds
            }
        }
    }

    return $healthy
}

# --- Step 1: Build ---
$stepNum = 1

if (-not $SkipBuild) {
    Write-Host "[$stepNum/$totalSteps] Building API in Release mode..." -ForegroundColor Yellow

    # Clean publish directory to avoid stale artifacts
    if (Test-Path $PublishPath) {
        Remove-Item $PublishPath -Recurse -Force
    }

    Push-Location $ApiProject
    try {
        dotnet publish -c Release -o $PublishPath --no-restore 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed with exit code $LASTEXITCODE"
        }
        Write-Host "  Build successful" -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
} else {
    Write-Host "[$stepNum/$totalSteps] Skipping build (using existing publish)" -ForegroundColor Gray
    if (-not (Test-Path $PublishPath)) {
        throw "Publish folder not found at '$PublishPath'. Run without -SkipBuild first."
    }
}

# --- Step 2: Package ---
$stepNum++
Write-Host "[$stepNum/$totalSteps] Creating deployment package..." -ForegroundColor Yellow

# Ensure deploy directory exists
$deployDir = Split-Path $ZipPath -Parent
if (-not (Test-Path $deployDir)) {
    New-Item -ItemType Directory -Path $deployDir -Force | Out-Null
}

if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}

# Enable stdout logging in web.config before packaging (per azure-deployment.md)
$webConfigPath = "$PublishPath\web.config"
if (Test-Path $webConfigPath) {
    $webConfig = Get-Content $webConfigPath -Raw
    $webConfig = $webConfig -replace 'stdoutLogEnabled="false"', 'stdoutLogEnabled="true"'
    Set-Content -Path $webConfigPath -Value $webConfig -NoNewline
}

Compress-Archive -Path "$PublishPath\*" -DestinationPath $ZipPath -Force
$zipSize = (Get-Item $ZipPath).Length / 1MB
Write-Host "  Package created: $([math]::Round($zipSize, 2)) MB" -ForegroundColor Green

# Validate zip size per deployment constraints (~60 MB expected)
if ($zipSize -gt 100) {
    Write-Host "  WARNING: Package is larger than expected ($([math]::Round($zipSize, 2)) MB > 100 MB)" -ForegroundColor Yellow
    Write-Host "  This may indicate stale publish artifacts. Consider a clean build." -ForegroundColor Yellow
}

# --- Step 3: Deploy ---
$stepNum++

if ($UseSlotDeploy) {
    # Deploy to staging slot
    Write-Host "[$stepNum/$totalSteps] Deploying to staging slot '$SlotName'..." -ForegroundColor Yellow
    Write-Host "  This may take 30-60 seconds..."

    $ErrorActionPreference = "Continue"
    $deployOutput = az webapp deploy `
        --resource-group $ResourceGroupName `
        --name $AppServiceName `
        --slot $SlotName `
        --src-path $ZipPath `
        --type zip `
        --async false 2>&1 | Out-String
    $ErrorActionPreference = "Stop"

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  Slot deployment failed:" -ForegroundColor Red
        Write-Host $deployOutput
        throw "Deployment to staging slot failed"
    }
    Write-Host "  Deployed to staging slot '$SlotName'" -ForegroundColor Green

    # --- Step 4: Verify staging slot health ---
    $stepNum++
    Write-Host "[$stepNum/$totalSteps] Verifying staging slot health..." -ForegroundColor Yellow
    Write-Host "  Waiting for staging slot to start..." -ForegroundColor Gray
    Start-Sleep -Seconds 10

    $slotHealthy = Test-HealthCheck `
        -Url $SlotHealthCheckUrl `
        -MaxRetries $MaxHealthCheckRetries `
        -IntervalSeconds $HealthCheckIntervalSeconds `
        -Label "Staging slot"

    if (-not $slotHealthy) {
        Write-Host ""
        Write-Host "  FAILED: Staging slot health check failed after $MaxHealthCheckRetries attempts" -ForegroundColor Red
        Write-Host "  URL: $SlotHealthCheckUrl" -ForegroundColor Yellow
        Write-Host "  Deployment aborted. Production is UNCHANGED." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  Troubleshooting:" -ForegroundColor Gray
        Write-Host "    az webapp log tail --resource-group $ResourceGroupName --name $AppServiceName --slot $SlotName" -ForegroundColor Gray
        exit 1
    }

    # --- Step 5: Swap slots ---
    $stepNum++
    Write-Host "[$stepNum/$totalSteps] Swapping staging -> production (zero-downtime)..." -ForegroundColor Yellow

    $ErrorActionPreference = "Continue"
    $swapOutput = az webapp deployment slot swap `
        --resource-group $ResourceGroupName `
        --name $AppServiceName `
        --slot $SlotName `
        --target-slot production 2>&1 | Out-String
    $ErrorActionPreference = "Stop"

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  Slot swap failed:" -ForegroundColor Red
        Write-Host $swapOutput
        throw "Slot swap failed. Staging slot still has the new version. Production unchanged."
    }
    Write-Host "  Slot swap complete" -ForegroundColor Green

    # --- Step 6: Verify production health after swap ---
    $stepNum++
    Write-Host "[$stepNum/$totalSteps] Verifying production health after swap..." -ForegroundColor Yellow
    Start-Sleep -Seconds 5

    $prodHealthy = Test-HealthCheck `
        -Url $ProductionHealthCheckUrl `
        -MaxRetries $MaxHealthCheckRetries `
        -IntervalSeconds $HealthCheckIntervalSeconds `
        -Label "Production"

    if (-not $prodHealthy) {
        Write-Host ""
        Write-Host "  FAILED: Production health check failed after swap!" -ForegroundColor Red

        if ($RollbackOnFailure) {
            # --- Step 7: Rollback ---
            $stepNum++
            Write-Host "[$stepNum/$totalSteps] ROLLING BACK: Swapping back to previous version..." -ForegroundColor Red

            $ErrorActionPreference = "Continue"
            $rollbackOutput = az webapp deployment slot swap `
                --resource-group $ResourceGroupName `
                --name $AppServiceName `
                --slot $SlotName `
                --target-slot production 2>&1 | Out-String
            $ErrorActionPreference = "Stop"

            if ($LASTEXITCODE -ne 0) {
                Write-Host "  CRITICAL: Rollback swap failed!" -ForegroundColor Red
                Write-Host $rollbackOutput
                Write-Host ""
                Write-Host "  Manual intervention required:" -ForegroundColor Red
                Write-Host "    az webapp deployment slot swap --resource-group $ResourceGroupName --name $AppServiceName --slot $SlotName --target-slot production" -ForegroundColor Yellow
                exit 2
            }

            # Verify rollback health
            Start-Sleep -Seconds 5
            $rollbackHealthy = Test-HealthCheck `
                -Url $ProductionHealthCheckUrl `
                -MaxRetries $MaxHealthCheckRetries `
                -IntervalSeconds $HealthCheckIntervalSeconds `
                -Label "Production (rollback)"

            if ($rollbackHealthy) {
                Write-Host "  Rollback successful. Previous version restored." -ForegroundColor Yellow
            } else {
                Write-Host "  CRITICAL: Rollback health check also failed!" -ForegroundColor Red
                Write-Host "  Manual investigation required." -ForegroundColor Red
            }

            Write-Host ""
            Write-Host "  Deployment FAILED and was rolled back." -ForegroundColor Red
            Write-Host "  Check staging slot logs for the root cause:" -ForegroundColor Yellow
            Write-Host "    az webapp log tail --resource-group $ResourceGroupName --name $AppServiceName --slot $SlotName" -ForegroundColor Gray
            exit 1
        } else {
            Write-Host "  Rollback disabled. Manual investigation required." -ForegroundColor Yellow
            Write-Host "  To rollback manually:" -ForegroundColor Gray
            Write-Host "    az webapp deployment slot swap --resource-group $ResourceGroupName --name $AppServiceName --slot $SlotName --target-slot production" -ForegroundColor Gray
            exit 1
        }
    }
} else {
    # Direct deploy (dev workflow). The "running process holds DLL file
    # handles" failure mode where `az webapp deploy` reports success but the
    # DLLs on disk are never replaced has been observed (May 2026) on Windows
    # App Service plans. Mitigation: hash the critical files locally BEFORE
    # deploy and re-fetch them via Kudu VFS AFTER deploy; if any hash mismatches,
    # auto-recover by stopping the app, redeploying via Kudu zipdeploy, and
    # starting. Fail loudly if the auto-recover also fails.
    Write-Host "[$stepNum/$totalSteps] Deploying directly to App Service..." -ForegroundColor Yellow
    Write-Host "  This may take 30-60 seconds..."

    # Capture pre-deploy hashes of critical files for post-deploy verification.
    $criticalFiles = @(
        'Sprk.Bff.Api.dll',
        'Sprk.Bff.Api.exe',
        'Sprk.Bff.Api.deps.json',
        'Spaarke.Core.dll',
        'Spaarke.Dataverse.dll',
        'web.config'
    )
    $localHashes = @{}
    foreach ($f in $criticalFiles) {
        $localPath = Join-Path $PublishPath $f
        if (Test-Path $localPath) {
            $localHashes[$f] = (Get-FileHash -Algorithm SHA256 -Path $localPath).Hash
        }
    }
    Write-Host "  Captured pre-deploy hashes for $($localHashes.Count) critical files" -ForegroundColor Gray

    $ErrorActionPreference = "Continue"
    $deployOutput = az webapp deploy `
        --resource-group $ResourceGroupName `
        --name $AppServiceName `
        --src-path $ZipPath `
        --type zip `
        --async false 2>&1 | Out-String
    $deployExitCode = $LASTEXITCODE
    $ErrorActionPreference = "Stop"

    # If `az webapp deploy` itself failed (commonly Linux App Service "rsync exit code
    # 123" file-in-use, or Windows 400 errors), skip straight to the auto-recover
    # path: stop -> Kudu zipdeploy -> start. The hash-verify after-the-fact will
    # confirm the result was correct.
    if ($deployExitCode -ne 0) {
        Write-Host "  Deploy command failed (exit=$deployExitCode). Trying stop -> Kudu zipdeploy -> start..." -ForegroundColor Yellow
        Write-Host $deployOutput -ForegroundColor Gray

        $mgmtToken = az account get-access-token --resource https://management.azure.com --query accessToken -o tsv
        $kuduHeaders = @{ Authorization = "Bearer $mgmtToken" }

        az webapp stop --name $AppServiceName --resource-group $ResourceGroupName | Out-Null
        Start-Sleep -Seconds 15

        $publishUrl = "https://$AppServiceName.scm.azurewebsites.net/api/zipdeploy?isAsync=true"
        Invoke-WebRequest -Uri $publishUrl -Headers $kuduHeaders -Method Post `
            -InFile $ZipPath -ContentType 'application/zip' -UseBasicParsing | Out-Null

        $deployComplete = $false
        for ($i = 0; $i -lt 30; $i++) {
            Start-Sleep -Seconds 10
            try {
                $latest = Invoke-RestMethod -Uri "https://$AppServiceName.scm.azurewebsites.net/api/deployments/latest" -Headers $kuduHeaders
                if ($latest.complete) { $deployComplete = $true; break }
            } catch {}
        }

        az webapp start --name $AppServiceName --resource-group $ResourceGroupName | Out-Null

        if (-not $deployComplete) {
            throw "Kudu zipdeploy fallback did not complete within 5 minutes"
        }
        Write-Host "  Stop -> Kudu zipdeploy -> start completed; will verify hashes" -ForegroundColor Green
    } else {
        Write-Host "  Deployment command returned success" -ForegroundColor Green
    }

    # Wait for app to restart
    Write-Host "  Waiting for app restart..." -ForegroundColor Gray
    Start-Sleep -Seconds 10

    # --- Step 4: Verify file replacement (the bit that az webapp deploy lies about) ---
    $stepNum++
    Write-Host "[$stepNum/$totalSteps] Verifying file replacement on server..." -ForegroundColor Yellow

    $mgmtToken = az account get-access-token --resource https://management.azure.com --query accessToken -o tsv
    $kuduHeaders = @{ Authorization = "Bearer $mgmtToken" }
    $kuduBase = "https://$AppServiceName.scm.azurewebsites.net/api/vfs/site/wwwroot"

    $mismatches = @()
    foreach ($f in $localHashes.Keys) {
        try {
            $tmp = New-TemporaryFile
            Invoke-WebRequest -Uri "$kuduBase/$f" -Headers $kuduHeaders -OutFile $tmp -UseBasicParsing | Out-Null
            $remoteHash = (Get-FileHash -Algorithm SHA256 -Path $tmp).Hash
            Remove-Item $tmp -Force
            if ($remoteHash -ne $localHashes[$f]) {
                $mismatches += $f
            }
        } catch {
            $mismatches += "$f (fetch failed: $($_.Exception.Message.Split([Environment]::NewLine)[0]))"
        }
    }

    if ($mismatches.Count -gt 0) {
        Write-Host ""
        Write-Host "  Deploy reported success BUT $($mismatches.Count) file(s) were not replaced:" -ForegroundColor Red
        foreach ($m in $mismatches) { Write-Host "    - $m" -ForegroundColor Yellow }
        Write-Host ""
        Write-Host "  Auto-recovering: stop -> zipdeploy via Kudu -> start..." -ForegroundColor Yellow

        az webapp stop --name $AppServiceName --resource-group $ResourceGroupName | Out-Null
        Start-Sleep -Seconds 15

        $publishUrl = "https://$AppServiceName.scm.azurewebsites.net/api/zipdeploy?isAsync=true"
        Invoke-WebRequest -Uri $publishUrl -Headers $kuduHeaders -Method Post `
            -InFile $ZipPath -ContentType 'application/zip' -UseBasicParsing | Out-Null

        # Poll Kudu until deployment completes
        $deployEnd = $false
        for ($i = 0; $i -lt 30; $i++) {
            Start-Sleep -Seconds 10
            try {
                $latest = Invoke-RestMethod -Uri "https://$AppServiceName.scm.azurewebsites.net/api/deployments/latest" -Headers $kuduHeaders
                if ($latest.complete) { $deployEnd = $true; break }
            } catch {}
        }

        az webapp start --name $AppServiceName --resource-group $ResourceGroupName | Out-Null

        # Re-verify
        Write-Host "  Re-verifying file hashes after auto-recover..." -ForegroundColor Yellow
        Start-Sleep -Seconds 10
        $stillMismatched = @()
        foreach ($f in $localHashes.Keys) {
            try {
                $tmp = New-TemporaryFile
                Invoke-WebRequest -Uri "$kuduBase/$f" -Headers $kuduHeaders -OutFile $tmp -UseBasicParsing | Out-Null
                $remoteHash = (Get-FileHash -Algorithm SHA256 -Path $tmp).Hash
                Remove-Item $tmp -Force
                if ($remoteHash -ne $localHashes[$f]) { $stillMismatched += $f }
            } catch {
                $stillMismatched += "$f (fetch failed)"
            }
        }

        if ($stillMismatched.Count -gt 0) {
            Write-Host ""
            Write-Host "  CRITICAL: Auto-recover did NOT fix the mismatch:" -ForegroundColor Red
            foreach ($m in $stillMismatched) { Write-Host "    - $m" -ForegroundColor Red }
            Write-Host "  Manual intervention required. See bff-deploy skill troubleshooting." -ForegroundColor Yellow
            exit 1
        }

        Write-Host "  Auto-recover succeeded - all files now match local build" -ForegroundColor Green
    } else {
        Write-Host "  All $($localHashes.Count) critical files match local build (SHA-256 verified)" -ForegroundColor Green
    }

    # --- Step 5: Verify health ---
    $stepNum++
    Write-Host "[$stepNum/$totalSteps] Verifying health endpoint..." -ForegroundColor Yellow

    $healthy = Test-HealthCheck `
        -Url $ProductionHealthCheckUrl `
        -MaxRetries $MaxHealthCheckRetries `
        -IntervalSeconds $HealthCheckIntervalSeconds `
        -Label $Environment

    if (-not $healthy) {
        Write-Host ""
        Write-Host "  Health check failed after $MaxHealthCheckRetries attempts" -ForegroundColor Red
        Write-Host "  Check: $ProductionHealthCheckUrl" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "  Troubleshooting:" -ForegroundColor Gray
        Write-Host "    az webapp log tail --resource-group $ResourceGroupName --name $AppServiceName" -ForegroundColor Gray
        exit 1
    }
}

# --- Summary ---
Write-Host ""
Write-Host "================================================================" -ForegroundColor Green
Write-Host "  Deployment Complete" -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Green
Write-Host "  Environment:  $Environment"
Write-Host "  App Service:  $AppServiceName"
Write-Host "  API URL:      $BaseUrl"
Write-Host "  Health:       $ProductionHealthCheckUrl"
if ($UseSlotDeploy) {
    Write-Host "  Deploy Mode:  Staging slot swap (zero-downtime)"
    Write-Host "  Staging URL:  $SlotUrl"
}
Write-Host "================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "Quick test commands:" -ForegroundColor Gray
Write-Host "  curl $ProductionHealthCheckUrl"
Write-Host "  curl $BaseUrl/ping"
Write-Host ""
