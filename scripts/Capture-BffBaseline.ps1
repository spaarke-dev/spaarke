<#
.SYNOPSIS
    Captures a deterministic synthetic baseline of every BFF API endpoint --
    status code distribution + P50/P95/P99 latency per route. Replaces the
    Phase 3 task 033 "48h App Insights baseline" calendar gate with a
    repeatable on-demand measurement.

.DESCRIPTION
    The Phase 3 design originally required 48 hours of organic App Insights
    telemetry to establish a regression baseline. For Spaarke's single-user
    dev environment that yields noisy/empty data for most endpoints -- a
    synthetic test gives strictly better signal in 5-10 minutes.

    This script:
      1. Reads the canonical route list from baseline/endpoints-smoke.json
         (produced by Phase 3 task 032 -- 323 routes).
      2. For each route, fires N HTTP probes (no auth) and records
         status + latency per probe.
      3. Computes P50/P95/P99 latency + status distribution per route.
      4. Writes JSON to baseline/synthetic-baseline.json.

    Probes are intentionally UNAUTHENTICATED. For protected routes this
    measures the pre-auth pipeline (TLS, routing, auth middleware returning
    401). For open routes (/healthz, /ping, /status) it measures end-to-end.
    What it tests: route exists, responds with expected status, latency
    within bounds. What it does NOT test: handler internals, downstream
    Graph/Dataverse calls. For deeper regression checks of specific Phase 4
    candidates, augment with focused authenticated probes.

    The output replaces app-insights-48h.json for Phase 4 regression checks.

.PARAMETER BffUrl
    BFF API base URL. Default: https://spaarke-bff-dev.azurewebsites.net

.PARAMETER RoutesJson
    Path to the route inventory JSON (from task 032). Default:
    projects/sdap-bff-api-remediation-fix/baseline/endpoints-smoke.json

.PARAMETER OutputJson
    Path to write the synthetic baseline. Default:
    projects/sdap-bff-api-remediation-fix/baseline/synthetic-baseline.json

.PARAMETER Samples
    Probes per route. More = tighter percentiles + longer runtime.
    Default: 10 (gives meaningful P50/P95; P99 = max-of-10).

.PARAMETER TimeoutSec
    Per-probe HTTP timeout. Default: 10s.

.EXAMPLE
    .\scripts\Capture-BffBaseline.ps1
    # Full baseline capture against the default dev BFF (5-8 min).

.EXAMPLE
    .\scripts\Capture-BffBaseline.ps1 -Samples 30 -OutputJson "./post-change.json"
    # Higher-sample baseline after a Phase 4 candidate; diff vs the canonical baseline.

.NOTES
    Designed to be run multiple times:
      - Once now as the Phase 3 baseline gate (replaces task 033 48h wait)
      - Once before each Phase 4 candidate
      - Once after each Phase 4 candidate
    Diffing the JSON files between runs surfaces regressions
    (route returns different status, latency P95 grew >X%, etc.).
#>
[CmdletBinding()]
param(
    [string]$BffUrl       = 'https://spaarke-bff-dev.azurewebsites.net',
    [string]$RoutesJson   = 'projects/sdap-bff-api-remediation-fix/baseline/endpoints-smoke.json',
    [string]$OutputJson   = 'projects/sdap-bff-api-remediation-fix/baseline/synthetic-baseline.json',
    [int]   $Samples      = 10,
    [int]   $TimeoutSec   = 10
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $RoutesJson)) {
    Write-Error "Route inventory not found at $RoutesJson -- run task 032 endpoint-smoke first."
    exit 1
}

$inventory = Get-Content $RoutesJson -Raw | ConvertFrom-Json
$routes = $inventory.routes
$totalRoutes = $routes.Count

Write-Host "==============================================================="
Write-Host "Synthetic BFF baseline capture"
Write-Host "==============================================================="
Write-Host "Target URL  : $BffUrl"
Write-Host "Routes      : $totalRoutes (from $RoutesJson)"
Write-Host "Samples each: $Samples"
Write-Host "Total probes: $($totalRoutes * $Samples)  (~$([Math]::Round(($totalRoutes * $Samples * 0.05))) sec wall-clock estimate)"
Write-Host ""

$results = New-Object System.Collections.ArrayList
$routeIdx = 0
$start = Get-Date

foreach ($r in $routes) {
    $routeIdx++
    if ($routeIdx % 25 -eq 0) {
        $elapsed = (Get-Date) - $start
        Write-Host "  [$routeIdx/$totalRoutes] elapsed: $([Math]::Round($elapsed.TotalSeconds))s"
    }

    # Substitute path parameters with a fixed placeholder so the route still matches
    $path = $r.path -replace '\{[^}]+\}', 'test-id'
    $url  = "$BffUrl$path"
    $method = $r.method

    $latencies = New-Object System.Collections.ArrayList
    $statuses  = New-Object System.Collections.ArrayList

    for ($i = 0; $i -lt $Samples; $i++) {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $status = -1
        try {
            # Use .NET HttpWebRequest directly for PS 5.1 compatibility (no -SkipHttpErrorCheck needed).
            $req = [System.Net.HttpWebRequest]::Create($url)
            $req.Method = $method
            $req.Timeout = $TimeoutSec * 1000
            $req.ReadWriteTimeout = $TimeoutSec * 1000
            $req.AllowAutoRedirect = $false
            # POST/PUT/PATCH need a content-length even when empty
            if (@('POST','PUT','PATCH') -contains $method) {
                $req.ContentLength = 0
            }
            try {
                $resp = $req.GetResponse()
                $status = [int]$resp.StatusCode
                $resp.Close()
            }
            catch [System.Net.WebException] {
                $we = $_.Exception
                if ($we.Response) {
                    $status = [int]$we.Response.StatusCode
                    $we.Response.Close()
                } else {
                    $status = -2   # network error (no response)
                }
            }
        }
        catch {
            $status = -3   # unexpected exception
        }
        $sw.Stop()
        [void]$latencies.Add([int]$sw.ElapsedMilliseconds)
        [void]$statuses.Add($status)
    }

    # Percentiles (sort + index)
    $sorted = $latencies | Sort-Object
    function Percentile([System.Collections.IEnumerable]$arr, [double]$p) {
        $list = @($arr)
        if ($list.Count -eq 0) { return 0 }
        $idx = [int][Math]::Floor($list.Count * $p)
        if ($idx -ge $list.Count) { $idx = $list.Count - 1 }
        return $list[$idx]
    }
    $p50 = Percentile $sorted 0.50
    $p95 = Percentile $sorted 0.95
    $p99 = Percentile $sorted 0.99
    $min = $sorted[0]
    $max = $sorted[-1]

    # Status distribution
    $statusDist = @{}
    foreach ($s in $statuses) {
        $key = [string]$s
        if ($statusDist.ContainsKey($key)) {
            $statusDist[$key] = $statusDist[$key] + 1
        } else {
            $statusDist[$key] = 1
        }
    }

    [void]$results.Add(@{
        method              = $method
        path                = $r.path
        latency_ms          = @{
            p50  = $p50
            p95  = $p95
            p99  = $p99
            min  = $min
            max  = $max
        }
        status_distribution = $statusDist
        sample_count        = $Samples
    })
}

# Aggregate metrics across the whole baseline
$totalProbes = ($results | ForEach-Object { $_.sample_count } | Measure-Object -Sum).Sum
$allStatuses = @{}
foreach ($r in $results) {
    foreach ($k in $r.status_distribution.Keys) {
        $v = $r.status_distribution[$k]
        if ($allStatuses.ContainsKey($k)) { $allStatuses[$k] += $v } else { $allStatuses[$k] = $v }
    }
}
$allP95 = ($results | ForEach-Object { $_.latency_ms.p95 } | Measure-Object -Average).Average

$output = @{
    baseline_captured_at = (Get-Date).ToUniversalTime().ToString('o')
    captured_by          = 'Capture-BffBaseline.ps1'
    target_url           = $BffUrl
    samples_per_route    = $Samples
    total_routes         = $totalRoutes
    total_probes         = $totalProbes
    wall_clock_seconds   = [Math]::Round(((Get-Date) - $start).TotalSeconds)
    aggregate            = @{
        average_p95_latency_ms = [Math]::Round($allP95)
        status_distribution    = $allStatuses
    }
    routes               = $results
}

$json = $output | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText((Resolve-Path -Path (Split-Path $OutputJson -Parent)).Path + '/' + (Split-Path $OutputJson -Leaf), $json, [System.Text.UTF8Encoding]::new($false))

$elapsed = (Get-Date) - $start
Write-Host ''
Write-Host "==============================================================="
Write-Host "Baseline capture complete in $([Math]::Round($elapsed.TotalSeconds))s"
Write-Host "==============================================================="
Write-Host "Output: $OutputJson"
Write-Host ''
Write-Host "Aggregate status distribution:"
$allStatuses.GetEnumerator() | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0,-6}: {1}" -f $_.Name, $_.Value)
}
Write-Host ''
Write-Host "Average P95 latency across all routes: $([Math]::Round($allP95)) ms"
