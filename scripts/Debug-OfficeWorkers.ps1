<#
.SYNOPSIS
    Debug Office workers - check if they're running and processing messages.

.DESCRIPTION
    Quick diagnostic script to troubleshoot worker issues:
    1. Check queue depths (are messages piling up?)
    2. Check App Service status
    3. Check recent logs for errors
    4. Test Service Bus connection
    5. Verify configuration

.EXAMPLE
    .\Debug-OfficeWorkers.ps1
#>

$ErrorActionPreference = "Continue"

Write-Host "=== Office Workers Diagnostic ===" -ForegroundColor Cyan
Write-Host ""

# Configuration
$ResourceGroup = "spe-infrastructure-westus2"
$AppServiceName = "spe-api-dev-67e2xz"
$ServiceBusResourceGroup = "SharePointEmbedded"
$ServiceBusNamespace = "spaarke-servicebus-dev"

# Step 1: Check App Service status
Write-Host "[1/6] Checking App Service status..." -ForegroundColor Yellow
$appStatus = az webapp show --name $AppServiceName --resource-group $ResourceGroup --query "state" --output tsv 2>&1
Write-Host "  App Service state: $appStatus" -ForegroundColor $(if ($appStatus -eq "Running") { "Green" } else { "Red" })

# Step 2: Check queue depths
Write-Host ""
Write-Host "[2/6] Checking Service Bus queue depths..." -ForegroundColor Yellow

$queues = @("office-upload-finalization", "office-profile", "office-indexing")
foreach ($queue in $queues) {
    $stats = az servicebus queue show `
        --resource-group $ServiceBusResourceGroup `
        --namespace-name $ServiceBusNamespace `
        --name $queue `
        --query "{Active:messageCount, DeadLetter:deadLetterMessageCount}" `
        --output json 2>&1 | ConvertFrom-Json

    $activeColor = if ($stats.Active -gt 0) { "Yellow" } else { "Green" }
    $deadLetterColor = if ($stats.DeadLetter -gt 0) { "Red" } else { "Green" }

    Write-Host "  Queue: $queue" -ForegroundColor White
    Write-Host "    Active messages: $($stats.Active)" -ForegroundColor $activeColor
    Write-Host "    Dead letter: $($stats.DeadLetter)" -ForegroundColor $deadLetterColor
}

# Step 3: Check Service Bus configuration
Write-Host ""
Write-Host "[3/6] Checking Service Bus configuration..." -ForegroundColor Yellow

$sbConfig = az webapp config appsettings list `
    --name $AppServiceName `
    --resource-group $ResourceGroup `
    --query "[?starts_with(name, 'ServiceBus')].{Name:name, Value:value}" `
    --output json 2>&1 | ConvertFrom-Json

if ($sbConfig) {
    foreach ($setting in $sbConfig) {
        Write-Host "  $($setting.Name): " -NoNewline
        if ($setting.Value -like "*@Microsoft.KeyVault*") {
            Write-Host "Key Vault reference configured" -ForegroundColor Green
        } elseif ($setting.Value -like "Endpoint=sb://*") {
            Write-Host "Connection string configured (direct)" -ForegroundColor Green
        } else {
            Write-Host "$($setting.Value)" -ForegroundColor Gray
        }
    }
} else {
    Write-Host "  ERROR: No ServiceBus configuration found!" -ForegroundColor Red
}

# Step 4: Check for recent errors in App Service logs
Write-Host ""
Write-Host "[4/6] Downloading recent App Service logs..." -ForegroundColor Yellow

$logsZip = "diagnostic-logs-$(Get-Date -Format 'yyyyMMdd-HHmmss').zip"
az webapp log download --name $AppServiceName --resource-group $ResourceGroup --log-file $logsZip 2>&1 | Out-Null

if (Test-Path $logsZip) {
    Write-Host "  Logs downloaded: $logsZip" -ForegroundColor Green

    # Extract and search for worker-related errors
    $extractPath = "diagnostic-logs-temp"
    Expand-Archive -Path $logsZip -DestinationPath $extractPath -Force

    Write-Host "  Searching for worker errors..." -ForegroundColor Gray

    $errorPatterns = @(
        "Worker",
        "ServiceBus",
        "BackgroundService",
        "UploadFinalization",
        "ProfileSummary",
        "IndexingWorker",
        "Exception",
        "Error"
    )

    $recentLogs = Get-ChildItem -Path $extractPath -Filter "*.txt" -Recurse |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 3

    $foundErrors = $false
    foreach ($logFile in $recentLogs) {
        $content = Get-Content $logFile.FullName -Tail 100 -ErrorAction SilentlyContinue

        foreach ($pattern in $errorPatterns) {
            $matches = $content | Select-String -Pattern $pattern
            if ($matches) {
                $foundErrors = $true
                Write-Host ""
                Write-Host "  Found in $($logFile.Name):" -ForegroundColor Yellow
                $matches | Select-Object -First 5 | ForEach-Object {
                    Write-Host "    $_" -ForegroundColor Gray
                }
            }
        }
    }

    if (-not $foundErrors) {
        Write-Host "  No worker-related errors found in recent logs" -ForegroundColor Green
    }

    # Cleanup
    Remove-Item -Path $extractPath -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $logsZip -Force -ErrorAction SilentlyContinue
} else {
    Write-Host "  WARNING: Could not download logs" -ForegroundColor Yellow
}

# Step 5: Test Service Bus connection
Write-Host ""
Write-Host "[5/6] Testing Service Bus connection..." -ForegroundColor Yellow

$connString = az servicebus namespace authorization-rule keys list `
    --resource-group $ServiceBusResourceGroup `
    --namespace-name $ServiceBusNamespace `
    --name RootManageSharedAccessKey `
    --query primaryConnectionString `
    --output tsv 2>&1

if ($connString -and $connString -like "Endpoint=sb://*") {
    Write-Host "  Connection string retrieved successfully" -ForegroundColor Green
    Write-Host "  Endpoint: $($connString.Split(';')[0])" -ForegroundColor Gray
} else {
    Write-Host "  ERROR: Failed to retrieve connection string" -ForegroundColor Red
}

# Step 6: Check API health
Write-Host ""
Write-Host "[6/6] Testing API health endpoint..." -ForegroundColor Yellow

try {
    $response = Invoke-WebRequest -Uri "https://$AppServiceName.azurewebsites.net/healthz" -Method Get -UseBasicParsing -TimeoutSec 10
    Write-Host "  API health: $($response.StatusCode) - $($response.StatusDescription)" -ForegroundColor Green
} catch {
    Write-Host "  ERROR: API health check failed - $($_.Exception.Message)" -ForegroundColor Red
}

# Summary
Write-Host ""
Write-Host "=== Diagnostic Summary ===" -ForegroundColor Cyan
Write-Host ""

$queueStats = az servicebus queue show `
    --resource-group $ServiceBusResourceGroup `
    --namespace-name $ServiceBusNamespace `
    --name "office-upload-finalization" `
    --query "messageCount" `
    --output tsv 2>&1

if ([int]$queueStats -gt 0) {
    Write-Host "ISSUE DETECTED: $queueStats messages waiting in office-upload-finalization queue" -ForegroundColor Red
    Write-Host "This indicates workers are NOT processing messages." -ForegroundColor Red
    Write-Host ""
    Write-Host "Possible causes:" -ForegroundColor Yellow
    Write-Host "  1. Workers failed to start (check logs above for exceptions)" -ForegroundColor Gray
    Write-Host "  2. Service Bus connection string not configured correctly" -ForegroundColor Gray
    Write-Host "  3. Dependency injection error preventing worker instantiation" -ForegroundColor Gray
    Write-Host "  4. Redis connection failure (workers require IDistributedCache)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  1. Check Application Insights for startup exceptions" -ForegroundColor Gray
    Write-Host "  2. Verify all worker dependencies are registered in Program.cs" -ForegroundColor Gray
    Write-Host "  3. Test locally with same configuration" -ForegroundColor Gray
} else {
    Write-Host "Workers appear to be processing messages successfully!" -ForegroundColor Green
}

Write-Host ""
