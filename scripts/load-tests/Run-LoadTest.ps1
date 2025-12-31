<#
.SYNOPSIS
    PowerShell-based load test for SDAP AI features.

.DESCRIPTION
    Runs concurrent HTTP requests against the SDAP BFF API to test load handling.
    Uses native PowerShell - no external dependencies required.

.PARAMETER BaseUrl
    The API base URL. Defaults to dev environment.

.PARAMETER Concurrency
    Number of concurrent requests. Defaults to 10.

.PARAMETER Duration
    Test duration in seconds. Defaults to 60.

.PARAMETER TestType
    Type of test: 'baseline', 'target', 'stress'. Defaults to 'baseline'.

.EXAMPLE
    # Baseline test (10 concurrent, 1 minute)
    .\Run-LoadTest.ps1 -TestType baseline

    # Target test (100 concurrent, 5 minutes)
    .\Run-LoadTest.ps1 -TestType target

    # Stress test (200 concurrent, 10 minutes)
    .\Run-LoadTest.ps1 -TestType stress

    # Custom configuration
    .\Run-LoadTest.ps1 -Concurrency 50 -Duration 120
#>

param(
    [string]$BaseUrl = "https://spe-api-dev-67e2xz.azurewebsites.net",
    [int]$Concurrency = 10,
    [int]$Duration = 60,
    [ValidateSet('baseline', 'target', 'stress', 'custom')]
    [string]$TestType = 'custom',
    [string]$AuthToken = ""
)

# Set concurrency/duration based on test type
switch ($TestType) {
    'baseline' {
        $Concurrency = 10
        $Duration = 120  # 2 minutes
    }
    'target' {
        $Concurrency = 100
        $Duration = 300  # 5 minutes
    }
    'stress' {
        $Concurrency = 200
        $Duration = 600  # 10 minutes
    }
}

Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host "SDAP AI Load Test - PowerShell Edition" -ForegroundColor Cyan
Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host ""
Write-Host "Configuration:" -ForegroundColor Yellow
Write-Host "  Base URL:     $BaseUrl"
Write-Host "  Concurrency:  $Concurrency virtual users"
Write-Host "  Duration:     $Duration seconds"
Write-Host "  Test Type:    $TestType"
Write-Host ""

# Results collection
$global:results = [System.Collections.Concurrent.ConcurrentBag[PSObject]]::new()
$global:errors = [System.Collections.Concurrent.ConcurrentBag[string]]::new()
$global:circuitBreakerOpens = 0
$global:startTime = Get-Date

# Endpoints to test
$endpoints = @(
    @{ Name = "resilience_health"; Method = "GET"; Path = "/api/resilience/health"; RequiresAuth = $false },
    @{ Name = "circuit_breakers"; Method = "GET"; Path = "/api/resilience/circuits"; RequiresAuth = $false },
    @{ Name = "ping"; Method = "GET"; Path = "/ping"; RequiresAuth = $false }
)

# Add authenticated endpoints if token provided
if ($AuthToken) {
    $endpoints += @(
        @{ Name = "rag_search"; Method = "POST"; Path = "/api/ai/rag/search"; RequiresAuth = $true; Body = @{
            query = "contract terms"
            tenantId = "load-test-tenant"
            deploymentModel = "Shared"
            topK = 5
        }}
    )
}

function Invoke-LoadTestRequest {
    param(
        [hashtable]$Endpoint,
        [int]$VirtualUserId
    )

    $headers = @{
        "Accept" = "application/json"
        "Content-Type" = "application/json"
    }

    if ($Endpoint.RequiresAuth -and $AuthToken) {
        $headers["Authorization"] = "Bearer $AuthToken"
    }

    $url = "$BaseUrl$($Endpoint.Path)"
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    try {
        $params = @{
            Uri = $url
            Method = $Endpoint.Method
            Headers = $headers
            TimeoutSec = 30
            ErrorAction = "Stop"
        }

        if ($Endpoint.Body) {
            $params.Body = $Endpoint.Body | ConvertTo-Json -Depth 10
        }

        $response = Invoke-WebRequest @params
        $stopwatch.Stop()

        $result = [PSCustomObject]@{
            Timestamp = Get-Date
            Endpoint = $Endpoint.Name
            VirtualUserId = $VirtualUserId
            StatusCode = $response.StatusCode
            Duration = $stopwatch.ElapsedMilliseconds
            Success = $true
            Error = $null
        }

        # Check for circuit breaker opens
        if ($Endpoint.Name -eq "resilience_health" -and $response.StatusCode -eq 503) {
            $global:circuitBreakerOpens++
        }

        $global:results.Add($result)
    }
    catch {
        $stopwatch.Stop()
        $result = [PSCustomObject]@{
            Timestamp = Get-Date
            Endpoint = $Endpoint.Name
            VirtualUserId = $VirtualUserId
            StatusCode = 0
            Duration = $stopwatch.ElapsedMilliseconds
            Success = $false
            Error = $_.Exception.Message
        }
        $global:results.Add($result)
        $global:errors.Add("[$($Endpoint.Name)] $($_.Exception.Message)")
    }
}

function Start-VirtualUser {
    param(
        [int]$UserId,
        [int]$DurationSeconds
    )

    $endTime = (Get-Date).AddSeconds($DurationSeconds)

    while ((Get-Date) -lt $endTime) {
        # Pick a random endpoint
        $endpoint = $endpoints | Get-Random

        # Skip auth endpoints if no token
        if ($endpoint.RequiresAuth -and -not $AuthToken) {
            $endpoint = $endpoints | Where-Object { -not $_.RequiresAuth } | Get-Random
        }

        if ($endpoint) {
            Invoke-LoadTestRequest -Endpoint $endpoint -VirtualUserId $UserId
        }

        # Think time: 500ms - 2000ms
        Start-Sleep -Milliseconds (Get-Random -Minimum 500 -Maximum 2000)
    }
}

# Run load test
Write-Host "Starting load test..." -ForegroundColor Green
Write-Host "Press Ctrl+C to stop early" -ForegroundColor Yellow
Write-Host ""

$jobs = @()
for ($i = 1; $i -le $Concurrency; $i++) {
    $jobs += Start-Job -ScriptBlock {
        param($BaseUrl, $Duration, $Endpoints, $AuthToken, $UserId)

        # Re-define function in job context
        function Invoke-LoadTestRequest {
            param($Endpoint, $VirtualUserId, $BaseUrl, $AuthToken)

            $headers = @{
                "Accept" = "application/json"
                "Content-Type" = "application/json"
            }

            if ($Endpoint.RequiresAuth -and $AuthToken) {
                $headers["Authorization"] = "Bearer $AuthToken"
            }

            $url = "$BaseUrl$($Endpoint.Path)"
            $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

            try {
                $params = @{
                    Uri = $url
                    Method = $Endpoint.Method
                    Headers = $headers
                    TimeoutSec = 30
                    ErrorAction = "Stop"
                }

                if ($Endpoint.Body) {
                    $params.Body = $Endpoint.Body | ConvertTo-Json -Depth 10
                }

                $response = Invoke-WebRequest @params
                $stopwatch.Stop()

                return [PSCustomObject]@{
                    Timestamp = Get-Date
                    Endpoint = $Endpoint.Name
                    VirtualUserId = $VirtualUserId
                    StatusCode = $response.StatusCode
                    Duration = $stopwatch.ElapsedMilliseconds
                    Success = $true
                    Error = $null
                }
            }
            catch {
                $stopwatch.Stop()
                return [PSCustomObject]@{
                    Timestamp = Get-Date
                    Endpoint = $Endpoint.Name
                    VirtualUserId = $VirtualUserId
                    StatusCode = 0
                    Duration = $stopwatch.ElapsedMilliseconds
                    Success = $false
                    Error = $_.Exception.Message
                }
            }
        }

        $results = @()
        $endTime = (Get-Date).AddSeconds($Duration)

        while ((Get-Date) -lt $endTime) {
            $endpoint = $Endpoints | Get-Random
            if ($endpoint.RequiresAuth -and -not $AuthToken) {
                $endpoint = $Endpoints | Where-Object { -not $_.RequiresAuth } | Get-Random
            }
            if ($endpoint) {
                $results += Invoke-LoadTestRequest -Endpoint $endpoint -VirtualUserId $UserId -BaseUrl $BaseUrl -AuthToken $AuthToken
            }
            Start-Sleep -Milliseconds (Get-Random -Minimum 500 -Maximum 2000)
        }

        return $results
    } -ArgumentList $BaseUrl, $Duration, $endpoints, $AuthToken, $i
}

Write-Host "Running $Concurrency virtual users for $Duration seconds..." -ForegroundColor Cyan

# Wait for all jobs
$allResults = @()
foreach ($job in $jobs) {
    $jobResults = Receive-Job -Job $job -Wait
    $allResults += $jobResults
    Remove-Job -Job $job
}

$endTime = Get-Date
$testDuration = ($endTime - $global:startTime).TotalSeconds

# Calculate statistics
Write-Host ""
Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host "Load Test Results" -ForegroundColor Cyan
Write-Host "=" * 60 -ForegroundColor Cyan

$totalRequests = $allResults.Count
$successfulRequests = ($allResults | Where-Object { $_.Success }).Count
$failedRequests = $totalRequests - $successfulRequests
$successRate = if ($totalRequests -gt 0) { [math]::Round(($successfulRequests / $totalRequests) * 100, 2) } else { 0 }

$durations = $allResults | Where-Object { $_.Success } | Select-Object -ExpandProperty Duration | Sort-Object
$avgLatency = if ($durations.Count -gt 0) { [math]::Round(($durations | Measure-Object -Average).Average, 2) } else { 0 }
$p50Index = [math]::Floor($durations.Count * 0.50)
$p95Index = [math]::Floor($durations.Count * 0.95)
$p99Index = [math]::Floor($durations.Count * 0.99)
$p50 = if ($durations.Count -gt 0) { $durations[$p50Index] } else { 0 }
$p95 = if ($durations.Count -gt 0) { $durations[$p95Index] } else { 0 }
$p99 = if ($durations.Count -gt 0) { $durations[[math]::Min($p99Index, $durations.Count - 1)] } else { 0 }
$requestsPerSecond = [math]::Round($totalRequests / $testDuration, 2)

Write-Host ""
Write-Host "Summary:" -ForegroundColor Yellow
Write-Host "  Test Duration:      $([math]::Round($testDuration, 1)) seconds"
Write-Host "  Virtual Users:      $Concurrency"
Write-Host "  Total Requests:     $totalRequests"
Write-Host "  Successful:         $successfulRequests ($successRate%)"
Write-Host "  Failed:             $failedRequests"
Write-Host "  Requests/sec:       $requestsPerSecond"
Write-Host ""
Write-Host "Latency (successful requests):" -ForegroundColor Yellow
Write-Host "  Average:            $avgLatency ms"
Write-Host "  P50 (Median):       $p50 ms"
Write-Host "  P95:                $p95 ms"
Write-Host "  P99:                $p99 ms"
Write-Host ""

# Per-endpoint breakdown
Write-Host "Per-Endpoint Breakdown:" -ForegroundColor Yellow
$allResults | Group-Object Endpoint | ForEach-Object {
    $endpointResults = $_.Group
    $endpointSuccess = ($endpointResults | Where-Object { $_.Success }).Count
    $endpointTotal = $endpointResults.Count
    $endpointDurations = $endpointResults | Where-Object { $_.Success } | Select-Object -ExpandProperty Duration | Sort-Object
    $endpointP95Index = [math]::Floor($endpointDurations.Count * 0.95)
    $endpointP95 = if ($endpointDurations.Count -gt 0) { $endpointDurations[[math]::Min($endpointP95Index, $endpointDurations.Count - 1)] } else { 0 }

    Write-Host "  $($_.Name): $endpointSuccess/$endpointTotal successful, P95: $endpointP95 ms"
}

# Circuit breaker check
$circuitOpens = ($allResults | Where-Object { $_.Endpoint -eq "resilience_health" -and $_.StatusCode -eq 503 }).Count
if ($circuitOpens -gt 0) {
    Write-Host ""
    Write-Host "Circuit Breaker Events: $circuitOpens (503 responses)" -ForegroundColor Red
}

Write-Host ""
Write-Host "=" * 60 -ForegroundColor Cyan

# Thresholds check
Write-Host ""
Write-Host "Threshold Validation:" -ForegroundColor Yellow
$passed = $true

if ($p95 -gt 3000) {
    Write-Host "  [FAIL] P95 latency $p95 ms > 3000 ms threshold" -ForegroundColor Red
    $passed = $false
} else {
    Write-Host "  [PASS] P95 latency $p95 ms <= 3000 ms threshold" -ForegroundColor Green
}

if ($successRate -lt 95) {
    Write-Host "  [FAIL] Success rate $successRate% < 95% threshold" -ForegroundColor Red
    $passed = $false
} else {
    Write-Host "  [PASS] Success rate $successRate% >= 95% threshold" -ForegroundColor Green
}

if ($Concurrency -ge 100 -and $failedRequests -eq 0) {
    Write-Host "  [PASS] 100+ concurrent handled with no failures" -ForegroundColor Green
} elseif ($Concurrency -ge 100) {
    Write-Host "  [WARN] 100+ concurrent but $failedRequests failures" -ForegroundColor Yellow
}

Write-Host ""
if ($passed) {
    Write-Host "OVERALL: PASS" -ForegroundColor Green
} else {
    Write-Host "OVERALL: FAIL" -ForegroundColor Red
}

# Export results to JSON
$resultsDir = Join-Path $PSScriptRoot "results"
if (-not (Test-Path $resultsDir)) {
    New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null
}

$timestamp = (Get-Date).ToString("yyyyMMdd-HHmmss")
$resultsFile = Join-Path $resultsDir "load-test-$TestType-$timestamp.json"

$summary = @{
    TestType = $TestType
    StartTime = $global:startTime.ToString("o")
    EndTime = $endTime.ToString("o")
    DurationSeconds = [math]::Round($testDuration, 1)
    Concurrency = $Concurrency
    TotalRequests = $totalRequests
    SuccessfulRequests = $successfulRequests
    FailedRequests = $failedRequests
    SuccessRate = $successRate
    RequestsPerSecond = $requestsPerSecond
    Latency = @{
        Average = $avgLatency
        P50 = $p50
        P95 = $p95
        P99 = $p99
    }
    CircuitBreakerOpens = $circuitOpens
    ThresholdsPassed = $passed
}

$summary | ConvertTo-Json -Depth 10 | Out-File $resultsFile -Encoding UTF8
Write-Host ""
Write-Host "Results saved to: $resultsFile" -ForegroundColor Cyan
