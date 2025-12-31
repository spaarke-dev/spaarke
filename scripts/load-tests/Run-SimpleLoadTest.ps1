<#
.SYNOPSIS
    Simple load test for available SDAP endpoints.

.DESCRIPTION
    Tests the endpoints that are currently deployed in dev environment.
    Use this before deploying the full R3 code.

.PARAMETER BaseUrl
    The API base URL.

.PARAMETER Concurrency
    Number of concurrent requests.

.PARAMETER Duration
    Test duration in seconds.

.EXAMPLE
    .\Run-SimpleLoadTest.ps1 -Concurrency 10 -Duration 30
#>

param(
    [string]$BaseUrl = "https://spe-api-dev-67e2xz.azurewebsites.net",
    [int]$Concurrency = 10,
    [int]$Duration = 30
)

Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host "SDAP Simple Load Test" -ForegroundColor Cyan
Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host ""
Write-Host "Testing: $BaseUrl"
Write-Host "Concurrency: $Concurrency VUs, Duration: $Duration seconds"
Write-Host ""

# Simple endpoints that should be available
$endpoints = @("/ping", "/healthz", "/status")

$startTime = Get-Date
$endTime = $startTime.AddSeconds($Duration)
$results = [System.Collections.ArrayList]::Synchronized([System.Collections.ArrayList]::new())
$runspacePool = [runspacefactory]::CreateRunspacePool(1, $Concurrency)
$runspacePool.Open()

$scriptBlock = {
    param($BaseUrl, $Endpoints, $EndTime)

    $localResults = @()
    while ((Get-Date) -lt $EndTime) {
        $endpoint = $Endpoints | Get-Random
        $url = "$BaseUrl$endpoint"
        $sw = [System.Diagnostics.Stopwatch]::StartNew()

        try {
            $response = Invoke-WebRequest -Uri $url -Method GET -TimeoutSec 10 -UseBasicParsing
            $sw.Stop()
            $localResults += @{
                Endpoint = $endpoint
                Status = $response.StatusCode
                Duration = $sw.ElapsedMilliseconds
                Success = $true
            }
        }
        catch {
            $sw.Stop()
            $localResults += @{
                Endpoint = $endpoint
                Status = 0
                Duration = $sw.ElapsedMilliseconds
                Success = $false
            }
        }

        Start-Sleep -Milliseconds (Get-Random -Minimum 100 -Maximum 500)
    }

    return $localResults
}

Write-Host "Starting $Concurrency virtual users..." -ForegroundColor Green

$runspaces = @()
for ($i = 0; $i -lt $Concurrency; $i++) {
    $ps = [PowerShell]::Create().AddScript($scriptBlock).AddArgument($BaseUrl).AddArgument($endpoints).AddArgument($endTime)
    $ps.RunspacePool = $runspacePool
    $runspaces += @{
        PowerShell = $ps
        Handle = $ps.BeginInvoke()
    }
}

# Wait and collect results
$allResults = @()
foreach ($rs in $runspaces) {
    $allResults += $rs.PowerShell.EndInvoke($rs.Handle)
    $rs.PowerShell.Dispose()
}
$runspacePool.Close()

$actualDuration = ((Get-Date) - $startTime).TotalSeconds

# Calculate statistics
$total = $allResults.Count
$successful = ($allResults | Where-Object { $_.Success }).Count
$successRate = [math]::Round(($successful / [math]::Max($total, 1)) * 100, 2)
$durations = $allResults | Where-Object { $_.Success } | ForEach-Object { $_.Duration } | Sort-Object
$avgLatency = if ($durations.Count -gt 0) { [math]::Round(($durations | Measure-Object -Average).Average, 2) } else { 0 }
$p95Index = [math]::Floor($durations.Count * 0.95)
$p95 = if ($durations.Count -gt 0) { $durations[[math]::Min($p95Index, $durations.Count - 1)] } else { 0 }
$rps = [math]::Round($total / $actualDuration, 2)

Write-Host ""
Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host "Results:" -ForegroundColor Yellow
Write-Host "  Duration:       $([math]::Round($actualDuration, 1)) seconds"
Write-Host "  Total Requests: $total"
Write-Host "  Successful:     $successful ($successRate%)"
Write-Host "  Failed:         $($total - $successful)"
Write-Host "  RPS:            $rps requests/sec"
Write-Host ""
Write-Host "Latency:" -ForegroundColor Yellow
Write-Host "  Average:        $avgLatency ms"
Write-Host "  P95:            $p95 ms"
Write-Host ""

# Per-endpoint
Write-Host "Per Endpoint:" -ForegroundColor Yellow
$allResults | Group-Object { $_.Endpoint } | ForEach-Object {
    $count = $_.Count
    $success = ($_.Group | Where-Object { $_.Success }).Count
    $eDurations = $_.Group | Where-Object { $_.Success } | ForEach-Object { $_.Duration } | Sort-Object
    $eAvg = if ($eDurations.Count -gt 0) { [math]::Round(($eDurations | Measure-Object -Average).Average, 2) } else { 0 }
    Write-Host "  $($_.Name): $success/$count OK, avg $eAvg ms"
}

Write-Host ""
if ($successRate -ge 95 -and $p95 -le 1000) {
    Write-Host "PASS: Success rate >= 95% and P95 <= 1000ms" -ForegroundColor Green
} else {
    Write-Host "Review: Success=$successRate%, P95=$p95 ms" -ForegroundColor Yellow
}
Write-Host "=" * 60 -ForegroundColor Cyan
