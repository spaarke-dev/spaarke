<#
.SYNOPSIS
    Measures session restore endpoint latency over 20 sequential requests.

.DESCRIPTION
    Calls GET /api/ai/chat/sessions/{sessionId}/restore repeatedly and reports
    per-request timing, percentile statistics, and NFR compliance against the
    p95 < 500ms target (ADR-015, AIPU2-106).

    The script performs 3 warm-up requests (discarded) followed by 20 measured
    requests. Results are output as a markdown table for easy inclusion in
    the load test report (notes/session-restore-load-test-report.md).

.PARAMETER SessionId
    The session ID to restore. Create one with Create-TestSession.ps1.

.PARAMETER BffBaseUrl
    The BFF API base URL. Defaults to dev environment.

.PARAMETER Iterations
    Number of measured requests. Defaults to 20.

.PARAMETER WarmUpCount
    Number of warm-up requests (results discarded). Defaults to 3.

.PARAMETER AuthToken
    Bearer token for authentication. If not provided, attempts to acquire
    via 'az account get-access-token'.

.PARAMETER TenantId
    Tenant ID sent via X-Tenant-Id header. Defaults to TENANT_ID env var.

.PARAMETER OutputFile
    Optional file path to write the markdown results table.

.EXAMPLE
    .\Test-SessionRestoreLatency.ps1 -SessionId "abc123-..."
    .\Test-SessionRestoreLatency.ps1 -SessionId "abc123-..." -BffBaseUrl "https://localhost:7071" -Iterations 50
    .\Test-SessionRestoreLatency.ps1 -SessionId "abc123-..." -OutputFile "../notes/results.md"

.NOTES
    Author:       Spaarke AI Platform Team
    Created:      2026-05-17
    Task:         AIPU2-106
    NFR Target:   p95 < 500ms (end-to-end restore)
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$SessionId,

    [string]$BffBaseUrl = "https://spe-api-dev-67e2xz.azurewebsites.net",

    [int]$Iterations = 20,

    [int]$WarmUpCount = 3,

    [string]$AuthToken = "",

    [string]$TenantId = $env:TENANT_ID,

    [string]$OutputFile = ""
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Acquire auth token if not provided
# ---------------------------------------------------------------------------

if ([string]::IsNullOrWhiteSpace($AuthToken)) {
    Write-Host "Acquiring auth token via az CLI..." -ForegroundColor Cyan
    try {
        $tokenJson = az account get-access-token --resource "https://spaarkedev1.crm.dynamics.com" 2>&1
        $tokenObj = $tokenJson | ConvertFrom-Json
        $AuthToken = $tokenObj.accessToken
        Write-Host "Token acquired (length: $($AuthToken.Length))" -ForegroundColor Green
    }
    catch {
        Write-Host "Failed to acquire token via az CLI." -ForegroundColor Yellow
        Write-Host "Trying pac auth token..." -ForegroundColor Cyan
        try {
            $AuthToken = (& pac auth token 2>&1 | Out-String).Trim()
            if ([string]::IsNullOrWhiteSpace($AuthToken) -or $AuthToken.Contains("Error")) {
                throw "pac auth token returned empty or error"
            }
            Write-Host "Token acquired via pac CLI (length: $($AuthToken.Length))" -ForegroundColor Green
        }
        catch {
            Write-Error "Could not acquire auth token. Provide -AuthToken parameter, or run 'az login' / 'pac auth create' first."
            exit 1
        }
    }
}

if ([string]::IsNullOrWhiteSpace($TenantId)) {
    $TenantId = "test-tenant-loadtest"
}

# ---------------------------------------------------------------------------
# Build request
# ---------------------------------------------------------------------------

$restoreUrl = "$($BffBaseUrl.TrimEnd('/'))/api/ai/chat/sessions/$SessionId/restore"

$headers = @{
    "Authorization" = "Bearer $AuthToken"
    "Accept"        = "application/json"
    "X-Tenant-Id"   = $TenantId
}

# ---------------------------------------------------------------------------
# Helper: single request with timing
# ---------------------------------------------------------------------------

function Invoke-TimedRestore {
    param([string]$Url, [hashtable]$Headers)

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $response = Invoke-WebRequest -Uri $Url -Method GET -Headers $Headers -UseBasicParsing -TimeoutSec 30
        $sw.Stop()

        $body = $response.Content | ConvertFrom-Json
        $serverMs = if ($body.PSObject.Properties.Name -contains "restoreLatencyMs") { $body.restoreLatencyMs } else { -1 }

        return @{
            StatusCode  = $response.StatusCode
            ClientMs    = $sw.ElapsedMilliseconds
            ServerMs    = $serverMs
            Success     = $true
            Error       = $null
            MessageCount = if ($body.PSObject.Properties.Name -contains "recentMessages") { $body.recentMessages.Count } else { 0 }
            WidgetCount  = if ($body.PSObject.Properties.Name -contains "widgetStates") { ($body.widgetStates.PSObject.Properties | Measure-Object).Count } else { 0 }
            HasStale     = if ($body.PSObject.Properties.Name -contains "hasStaleEntities") { $body.hasStaleEntities } else { $false }
        }
    }
    catch {
        $sw.Stop()
        $statusCode = 0
        if ($_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }
        return @{
            StatusCode  = $statusCode
            ClientMs    = $sw.ElapsedMilliseconds
            ServerMs    = -1
            Success     = $false
            Error       = $_.Exception.Message
            MessageCount = 0
            WidgetCount  = 0
            HasStale     = $false
        }
    }
}

# ---------------------------------------------------------------------------
# Banner
# ---------------------------------------------------------------------------

Write-Host ""
Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host "Session Restore Latency Test" -ForegroundColor Cyan
Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host ""
Write-Host "  Endpoint:    $restoreUrl"
Write-Host "  Iterations:  $Iterations (+ $WarmUpCount warm-up)"
Write-Host "  Tenant ID:   $TenantId"
Write-Host "  NFR Target:  p95 < 500ms"
Write-Host ""

# ---------------------------------------------------------------------------
# Warm-up phase
# ---------------------------------------------------------------------------

Write-Host "Warm-up ($WarmUpCount requests)..." -ForegroundColor Yellow
for ($i = 1; $i -le $WarmUpCount; $i++) {
    $result = Invoke-TimedRestore -Url $restoreUrl -Headers $headers
    $status = if ($result.Success) { "OK ($($result.ClientMs)ms)" } else { "FAIL ($($result.StatusCode))" }
    Write-Host "  Warm-up $i`: $status" -ForegroundColor Gray
}
Write-Host ""

# ---------------------------------------------------------------------------
# Measurement phase
# ---------------------------------------------------------------------------

Write-Host "Measuring ($Iterations requests)..." -ForegroundColor Yellow
$results = @()

for ($i = 1; $i -le $Iterations; $i++) {
    $result = Invoke-TimedRestore -Url $restoreUrl -Headers $headers
    $results += $result

    $indicator = if ($result.Success) {
        if ($result.ClientMs -le 500) { "PASS" } else { "SLOW" }
    } else { "FAIL" }

    $color = switch ($indicator) {
        "PASS" { "Green" }
        "SLOW" { "Yellow" }
        "FAIL" { "Red" }
    }

    Write-Host ("  #{0,2}: {1,4}ms (server: {2,4}ms) [{3}]" -f $i, $result.ClientMs, $result.ServerMs, $indicator) -ForegroundColor $color
}

Write-Host ""

# ---------------------------------------------------------------------------
# Calculate statistics
# ---------------------------------------------------------------------------

$successResults = $results | Where-Object { $_.Success }
$failCount = ($results | Where-Object { -not $_.Success }).Count

if ($successResults.Count -eq 0) {
    Write-Host "All requests failed. Cannot calculate statistics." -ForegroundColor Red
    exit 1
}

# Client-side latencies
$clientDurations = $successResults | ForEach-Object { $_.ClientMs } | Sort-Object
$clientMin  = $clientDurations[0]
$clientMax  = $clientDurations[-1]
$clientMean = [math]::Round(($clientDurations | Measure-Object -Average).Average, 1)
$clientP50  = $clientDurations[[math]::Floor($clientDurations.Count * 0.50)]
$clientP95  = $clientDurations[[math]::Min([math]::Floor($clientDurations.Count * 0.95), $clientDurations.Count - 1)]

# Server-side latencies (from response body)
$serverDurations = $successResults | Where-Object { $_.ServerMs -ge 0 } | ForEach-Object { $_.ServerMs } | Sort-Object
$hasServerTiming = $serverDurations.Count -gt 0

if ($hasServerTiming) {
    $serverMin  = $serverDurations[0]
    $serverMax  = $serverDurations[-1]
    $serverMean = [math]::Round(($serverDurations | Measure-Object -Average).Average, 1)
    $serverP50  = $serverDurations[[math]::Floor($serverDurations.Count * 0.50)]
    $serverP95  = $serverDurations[[math]::Min([math]::Floor($serverDurations.Count * 0.95), $serverDurations.Count - 1)]
}

# ---------------------------------------------------------------------------
# Build markdown output
# ---------------------------------------------------------------------------

$md = @()
$md += "## Session Restore Latency Results"
$md += ""
$md += "| Setting | Value |"
$md += "|---------|-------|"
$md += "| Date | $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') |"
$md += "| Endpoint | ``$restoreUrl`` |"
$md += "| Session ID | ``$SessionId`` |"
$md += "| Iterations | $Iterations |"
$md += "| Warm-up | $WarmUpCount |"
$md += "| Failures | $failCount |"
$md += ""

# Per-request table
$md += "### Per-Request Results"
$md += ""
$md += "| # | HTTP Status | Client (ms) | Server (ms) | Messages | Widgets | Stale | Result |"
$md += "|---|-------------|-------------|-------------|----------|---------|-------|--------|"

for ($i = 0; $i -lt $results.Count; $i++) {
    $r = $results[$i]
    $status = if ($r.Success) { $r.StatusCode } else { "$($r.StatusCode) ERR" }
    $verdict = if ($r.Success) { if ($r.ClientMs -le 500) { "PASS" } else { "SLOW" } } else { "FAIL" }
    $md += "| $($i + 1) | $status | $($r.ClientMs) | $($r.ServerMs) | $($r.MessageCount) | $($r.WidgetCount) | $($r.HasStale) | $verdict |"
}

$md += ""
$md += "### Summary Statistics"
$md += ""
$md += "| Metric | Client (ms) | Server (ms) |"
$md += "|--------|-------------|-------------|"
$md += "| min | $clientMin | $(if ($hasServerTiming) { $serverMin } else { 'n/a' }) |"
$md += "| max | $clientMax | $(if ($hasServerTiming) { $serverMax } else { 'n/a' }) |"
$md += "| mean | $clientMean | $(if ($hasServerTiming) { $serverMean } else { 'n/a' }) |"
$md += "| p50 | $clientP50 | $(if ($hasServerTiming) { $serverP50 } else { 'n/a' }) |"
$md += "| **p95** | **$clientP95** | **$(if ($hasServerTiming) { $serverP95 } else { 'n/a' })** |"
$md += ""

# NFR verdict
$clientPass = $clientP95 -le 500
$serverPass = (-not $hasServerTiming) -or ($serverP95 -le 500)
$overallPass = $clientPass -and $serverPass -and ($failCount -eq 0)

$md += "### NFR Verdict"
$md += ""
$md += "| Metric | Target | Actual | Status |"
$md += "|--------|--------|--------|--------|"
$md += "| p95 client latency | < 500ms | ${clientP95}ms | $(if ($clientPass) { 'PASS' } else { 'FAIL' }) |"
if ($hasServerTiming) {
    $md += "| p95 server latency | < 500ms | ${serverP95}ms | $(if ($serverPass) { 'PASS' } else { 'FAIL' }) |"
}
$md += "| Error rate | 0% | $([math]::Round($failCount / $results.Count * 100, 1))% | $(if ($failCount -eq 0) { 'PASS' } else { 'FAIL' }) |"
$md += "| **Overall** | | | **$(if ($overallPass) { 'PASS' } else { 'FAIL' })** |"
$md += ""

$markdownText = $md -join "`n"

# ---------------------------------------------------------------------------
# Console output
# ---------------------------------------------------------------------------

Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host "Summary Statistics" -ForegroundColor Yellow
Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host ""
Write-Host "  Client-side latency:" -ForegroundColor White
Write-Host "    min:  $clientMin ms"
Write-Host "    max:  $clientMax ms"
Write-Host "    mean: $clientMean ms"
Write-Host "    p50:  $clientP50 ms"
Write-Host "    p95:  $clientP95 ms" -ForegroundColor $(if ($clientPass) { "Green" } else { "Red" })

if ($hasServerTiming) {
    Write-Host ""
    Write-Host "  Server-side latency:" -ForegroundColor White
    Write-Host "    min:  $serverMin ms"
    Write-Host "    max:  $serverMax ms"
    Write-Host "    mean: $serverMean ms"
    Write-Host "    p50:  $serverP50 ms"
    Write-Host "    p95:  $serverP95 ms" -ForegroundColor $(if ($serverPass) { "Green" } else { "Red" })
}

Write-Host ""
Write-Host "  Errors: $failCount / $($results.Count)" -ForegroundColor $(if ($failCount -eq 0) { "Green" } else { "Red" })
Write-Host ""

if ($overallPass) {
    Write-Host "  NFR VERDICT: PASS (p95 ${clientP95}ms < 500ms)" -ForegroundColor Green
} else {
    Write-Host "  NFR VERDICT: FAIL" -ForegroundColor Red
    if (-not $clientPass) {
        Write-Host "    Client p95 ${clientP95}ms exceeds 500ms target" -ForegroundColor Red
    }
    if ($hasServerTiming -and -not $serverPass) {
        Write-Host "    Server p95 ${serverP95}ms exceeds 500ms target" -ForegroundColor Red
    }
    if ($failCount -gt 0) {
        Write-Host "    $failCount request(s) failed" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "=" * 60 -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# Write to file if requested
# ---------------------------------------------------------------------------

if (-not [string]::IsNullOrWhiteSpace($OutputFile)) {
    $markdownText | Set-Content -Path $OutputFile -Encoding UTF8
    Write-Host ""
    Write-Host "Results written to: $OutputFile" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "Markdown output (copy to report):" -ForegroundColor Yellow
    Write-Host ""
    Write-Host $markdownText
}
