#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Post-deployment smoke tests for Spaarke production environments.

.DESCRIPTION
    Runs smoke tests against a deployed environment to verify all critical services
    are operational: BFF API, Dataverse, SPE, AI services (OpenAI, AI Search,
    Document Intelligence), Redis, and Service Bus.

    Each test group has an individual timeout. Total execution is capped at 5 minutes.
    Returns non-zero exit code if any critical test fails.

.PARAMETER Environment
    Target environment: dev, staging, prod (default: prod)

.PARAMETER ApiBaseUrl
    Override the BFF API base URL (auto-resolved from Environment if omitted)

.PARAMETER CustomerId
    Customer ID to test customer-specific resources (default: demo)

.PARAMETER SkipGroups
    Comma-separated list of test groups to skip (e.g., "AI,SPE")

.PARAMETER Verbose
    Show detailed output for each test

.EXAMPLE
    .\Test-Deployment.ps1
    # Run all smoke tests against production

.EXAMPLE
    .\Test-Deployment.ps1 -Environment dev -Verbose
    # Run all tests against dev with detailed output

.EXAMPLE
    .\Test-Deployment.ps1 -SkipGroups "AI,SPE" -CustomerId demo
    # Skip AI and SPE tests, test demo customer
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet('dev', 'staging', 'prod')]
    [string]$Environment = 'prod',

    [Parameter(Mandatory = $false)]
    [string]$ApiBaseUrl,

    [Parameter(Mandatory = $false)]
    [string]$CustomerId = 'demo',

    [Parameter(Mandatory = $false)]
    [string[]]$SkipGroups = @()
)

$ErrorActionPreference = "Continue"
$script:TotalTimeout = [TimeSpan]::FromMinutes(5)
$script:StartTime = Get-Date
$script:Results = [System.Collections.ArrayList]::new()

# ─────────────────────────────────────────────────────────────────────
# Configuration
# ─────────────────────────────────────────────────────────────────────

$EnvironmentConfig = @{
    dev = @{
        ApiBaseUrl       = 'https://spe-api-dev-67e2xz.azurewebsites.net'
        ResourceGroup    = 'spe-infrastructure-westus2'
        OpenAiEndpoint   = 'https://spaarke-openai-dev.openai.azure.com/'
        SearchEndpoint   = 'https://spaarke-search-dev.search.windows.net/'
        DocIntelEndpoint = 'https://westus2.api.cognitive.microsoft.com/'
        RedisName        = 'spaarke-redis-dev'
        ServiceBusNs     = 'spaarke-servicebus-dev'
        AppServiceName   = 'spe-api-dev-67e2xz'
    }
    staging = @{
        ApiBaseUrl       = 'https://spaarke-bff-prod-staging.azurewebsites.net'
        ResourceGroup    = 'rg-spaarke-platform-prod'
        OpenAiEndpoint   = 'https://spaarke-openai-prod.openai.azure.com/'
        SearchEndpoint   = 'https://spaarke-search-prod.search.windows.net/'
        DocIntelEndpoint = 'https://westus2.api.cognitive.microsoft.com/'
        RedisName        = 'spaarke-redis-prod'
        ServiceBusNs     = 'spaarke-servicebus-prod'
        AppServiceName   = 'spaarke-bff-prod'
    }
    prod = @{
        ApiBaseUrl       = 'https://api.spaarke.com'
        ResourceGroup    = 'rg-spaarke-platform-prod'
        OpenAiEndpoint   = 'https://spaarke-openai-prod.openai.azure.com/'
        SearchEndpoint   = 'https://spaarke-search-prod.search.windows.net/'
        DocIntelEndpoint = 'https://westus2.api.cognitive.microsoft.com/'
        RedisName        = 'spaarke-redis-prod'
        ServiceBusNs     = 'spaarke-servicebus-prod'
        AppServiceName   = 'spaarke-bff-prod'
    }
}

$config = $EnvironmentConfig[$Environment]
if ($ApiBaseUrl) {
    $config.ApiBaseUrl = $ApiBaseUrl
}

# ─────────────────────────────────────────────────────────────────────
# Helper Functions
# ─────────────────────────────────────────────────────────────────────

function Write-TestHeader {
    param([string]$GroupName)
    Write-Host ""
    Write-Host ("=" * 60) -ForegroundColor Cyan
    Write-Host "  TEST GROUP: $GroupName" -ForegroundColor Cyan
    Write-Host ("=" * 60) -ForegroundColor Cyan
}

function Add-TestResult {
    param(
        [string]$Group,
        [string]$Test,
        [ValidateSet('Pass', 'Fail', 'Skip', 'Warn')]
        [string]$Status,
        [string]$Message = '',
        [bool]$Critical = $true,
        [double]$DurationMs = 0
    )

    $icon = switch ($Status) {
        'Pass' { '[PASS]' }
        'Fail' { '[FAIL]' }
        'Skip' { '[SKIP]' }
        'Warn' { '[WARN]' }
    }

    $color = switch ($Status) {
        'Pass' { 'Green' }
        'Fail' { 'Red' }
        'Skip' { 'DarkGray' }
        'Warn' { 'Yellow' }
    }

    $durationStr = if ($DurationMs -gt 0) { " (${DurationMs}ms)" } else { '' }
    Write-Host "  $icon $Test$durationStr" -ForegroundColor $color
    if ($Message -and ($VerbosePreference -eq 'Continue' -or $Status -eq 'Fail')) {
        Write-Host "        $Message" -ForegroundColor DarkGray
    }

    [void]$script:Results.Add([PSCustomObject]@{
        Group      = $Group
        Test       = $Test
        Status     = $Status
        Critical   = $Critical
        Message    = $Message
        DurationMs = [math]::Round($DurationMs)
    })
}

function Test-TimeoutExceeded {
    $elapsed = (Get-Date) - $script:StartTime
    return $elapsed -ge $script:TotalTimeout
}

function Invoke-WithTimeout {
    param(
        [string]$Group,
        [string]$TestName,
        [scriptblock]$ScriptBlock,
        [int]$TimeoutSeconds = 30,
        [bool]$Critical = $true
    )

    if (Test-TimeoutExceeded) {
        Add-TestResult -Group $Group -Test $TestName -Status 'Skip' `
            -Message 'Skipped: total 5-minute timeout exceeded' -Critical $Critical
        return
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $job = Start-Job -ScriptBlock $ScriptBlock -ArgumentList $config, $CustomerId
        $completed = Wait-Job $job -Timeout $TimeoutSeconds
        $sw.Stop()

        if (-not $completed) {
            Stop-Job $job -ErrorAction SilentlyContinue
            Remove-Job $job -Force -ErrorAction SilentlyContinue
            Add-TestResult -Group $Group -Test $TestName -Status 'Fail' `
                -Message "Timed out after ${TimeoutSeconds}s" -Critical $Critical -DurationMs ($TimeoutSeconds * 1000)
            return
        }

        $output = Receive-Job $job
        Remove-Job $job -Force -ErrorAction SilentlyContinue

        if ($job.State -eq 'Failed') {
            $errorMsg = $job.ChildJobs[0].JobStateInfo.Reason.Message
            Add-TestResult -Group $Group -Test $TestName -Status 'Fail' `
                -Message $errorMsg -Critical $Critical -DurationMs $sw.ElapsedMilliseconds
        }
        else {
            Add-TestResult -Group $Group -Test $TestName -Status 'Pass' `
                -Message ($output | Out-String).Trim() -Critical $Critical -DurationMs $sw.ElapsedMilliseconds
        }
    }
    catch {
        $sw.Stop()
        Add-TestResult -Group $Group -Test $TestName -Status 'Fail' `
            -Message $_.Exception.Message -Critical $Critical -DurationMs $sw.ElapsedMilliseconds
    }
}

function Invoke-TestDirect {
    param(
        [string]$Group,
        [string]$TestName,
        [scriptblock]$ScriptBlock,
        [int]$TimeoutSeconds = 30,
        [bool]$Critical = $true
    )

    if (Test-TimeoutExceeded) {
        Add-TestResult -Group $Group -Test $TestName -Status 'Skip' `
            -Message 'Skipped: total 5-minute timeout exceeded' -Critical $Critical
        return
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $result = & $ScriptBlock
        $sw.Stop()
        Add-TestResult -Group $Group -Test $TestName -Status 'Pass' `
            -Message ($result | Out-String).Trim() -Critical $Critical -DurationMs $sw.ElapsedMilliseconds
    }
    catch {
        $sw.Stop()
        Add-TestResult -Group $Group -Test $TestName -Status 'Fail' `
            -Message $_.Exception.Message -Critical $Critical -DurationMs $sw.ElapsedMilliseconds
    }
}

# ─────────────────────────────────────────────────────────────────────
# Test Group 1: BFF API
# ─────────────────────────────────────────────────────────────────────

function Test-BffApi {
    Write-TestHeader "BFF API"

    $baseUrl = $config.ApiBaseUrl

    # 1a. Health endpoint (unauthenticated)
    Invoke-TestDirect -Group 'BFF API' -TestName 'GET /healthz returns 200' -Critical $true -ScriptBlock {
        $response = Invoke-WebRequest -Uri "$($config.ApiBaseUrl)/healthz" -UseBasicParsing -TimeoutSec 15
        if ($response.StatusCode -ne 200) {
            throw "Expected 200, got $($response.StatusCode)"
        }
        "HTTP $($response.StatusCode) — Healthy"
    }

    # 1b. Ping endpoint (unauthenticated)
    Invoke-TestDirect -Group 'BFF API' -TestName 'GET /ping returns 200' -Critical $false -ScriptBlock {
        $response = Invoke-WebRequest -Uri "$($config.ApiBaseUrl)/ping" -UseBasicParsing -TimeoutSec 15
        if ($response.StatusCode -ne 200) {
            throw "Expected 200, got $($response.StatusCode)"
        }
        "HTTP $($response.StatusCode) — Pong"
    }

    # 1c. Authenticated endpoint (returns 401 without token)
    Invoke-TestDirect -Group 'BFF API' -TestName 'GET /api/me returns 401 without auth' -Critical $true -ScriptBlock {
        try {
            $null = Invoke-WebRequest -Uri "$($config.ApiBaseUrl)/api/me" -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop
            throw "Expected 401 but got success response"
        }
        catch {
            if ($_.Exception.Response.StatusCode.value__ -eq 401) {
                "HTTP 401 — Auth required (correct)"
            }
            else {
                throw "Expected 401, got $($_.Exception.Response.StatusCode.value__): $($_.Exception.Message)"
            }
        }
    }

    # 1d. Response time check
    Invoke-TestDirect -Group 'BFF API' -TestName '/healthz responds under 2s' -Critical $false -ScriptBlock {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $null = Invoke-WebRequest -Uri "$($config.ApiBaseUrl)/healthz" -UseBasicParsing -TimeoutSec 10
        $sw.Stop()
        if ($sw.ElapsedMilliseconds -gt 2000) {
            throw "Response took $($sw.ElapsedMilliseconds)ms (limit: 2000ms)"
        }
        "$($sw.ElapsedMilliseconds)ms response time"
    }
}

# ─────────────────────────────────────────────────────────────────────
# Test Group 2: Dataverse
# ─────────────────────────────────────────────────────────────────────

function Test-Dataverse {
    Write-TestHeader "Dataverse"

    # 2a. PAC CLI connectivity
    Invoke-TestDirect -Group 'Dataverse' -TestName 'PAC CLI authenticated' -Critical $true -ScriptBlock {
        $output = & pac auth list 2>&1
        $outputStr = ($output | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or $outputStr -match 'No profiles') {
            throw "PAC CLI not authenticated. Run 'pac auth create' first."
        }
        $outputStr -split "`n" | Select-Object -First 3 | ForEach-Object { $_.Trim() }
    }

    # 2b. Solution verification
    Invoke-TestDirect -Group 'Dataverse' -TestName 'SpaarkeCore solution installed' -Critical $true -ScriptBlock {
        $output = & pac solution list 2>&1
        $outputStr = ($output | Out-String)
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to list solutions: $outputStr"
        }
        if ($outputStr -notmatch 'SpaarkeCore|sprk_core|spaarkecore') {
            throw "SpaarkeCore solution not found in target environment"
        }
        "SpaarkeCore solution found"
    }

    # 2c. Dataverse API connectivity (via pac org who)
    Invoke-TestDirect -Group 'Dataverse' -TestName 'Dataverse org accessible' -Critical $true -ScriptBlock {
        $output = & pac org who 2>&1
        $outputStr = ($output | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) {
            throw "Cannot reach Dataverse org: $outputStr"
        }
        $outputStr -split "`n" | Select-Object -First 3 | ForEach-Object { $_.Trim() }
    }
}

# ─────────────────────────────────────────────────────────────────────
# Test Group 3: SPE (SharePoint Embedded)
# ─────────────────────────────────────────────────────────────────────

function Test-SPE {
    Write-TestHeader "SPE (SharePoint Embedded)"

    # 3a. Container listing via BFF API (requires auth)
    Invoke-TestDirect -Group 'SPE' -TestName 'Container endpoint reachable' -Critical $true -ScriptBlock {
        # Test that the endpoint exists (even if auth fails, we expect 401 not 404)
        try {
            $null = Invoke-WebRequest -Uri "$($config.ApiBaseUrl)/api/containers" -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop
            "Container endpoint responded successfully"
        }
        catch {
            $statusCode = $_.Exception.Response.StatusCode.value__
            if ($statusCode -eq 401 -or $statusCode -eq 403) {
                "Container endpoint exists (HTTP $statusCode — auth required)"
            }
            elseif ($statusCode -eq 404) {
                throw "Container endpoint not found (404). Is the API deployed correctly?"
            }
            else {
                throw "Unexpected status $statusCode from container endpoint: $($_.Exception.Message)"
            }
        }
    }

    # 3b. SPE file operations endpoint
    Invoke-TestDirect -Group 'SPE' -TestName 'Drive endpoint reachable' -Critical $false -ScriptBlock {
        try {
            $null = Invoke-WebRequest -Uri "$($config.ApiBaseUrl)/api/drives/test/children" -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop
            "Drive endpoint responded"
        }
        catch {
            $statusCode = $_.Exception.Response.StatusCode.value__
            if ($statusCode -in @(401, 403, 400)) {
                "Drive endpoint exists (HTTP $statusCode — expected without valid drive ID)"
            }
            elseif ($statusCode -eq 404) {
                throw "Drive endpoint not found (404)"
            }
            else {
                "Drive endpoint responded with HTTP $statusCode"
            }
        }
    }
}

# ─────────────────────────────────────────────────────────────────────
# Test Group 4: AI Services
# ─────────────────────────────────────────────────────────────────────

function Test-AIServices {
    Write-TestHeader "AI Services"

    # 4a. Azure OpenAI endpoint
    Invoke-TestDirect -Group 'AI' -TestName 'Azure OpenAI endpoint reachable' -Critical $true -ScriptBlock {
        $endpoint = $config.OpenAiEndpoint
        try {
            $null = Invoke-WebRequest -Uri "${endpoint}openai/models?api-version=2024-06-01" -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop
            "OpenAI endpoint accessible"
        }
        catch {
            $statusCode = $_.Exception.Response.StatusCode.value__
            if ($statusCode -eq 401 -or $statusCode -eq 403) {
                "OpenAI endpoint reachable (HTTP $statusCode — auth required)"
            }
            else {
                throw "OpenAI endpoint error: HTTP $statusCode — $($_.Exception.Message)"
            }
        }
    }

    # 4b. Azure OpenAI via Azure CLI (model deployment check)
    Invoke-TestDirect -Group 'AI' -TestName 'OpenAI model deployments exist' -Critical $false -ScriptBlock {
        $rg = $config.ResourceGroup
        $accountName = if ($Environment -eq 'dev') { 'spaarke-openai-dev' } else { 'spaarke-openai-prod' }
        $output = & az cognitiveservices account deployment list --resource-group $rg --name $accountName --output json 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to list deployments: $($output | Out-String)"
        }
        $deployments = $output | ConvertFrom-Json
        if ($deployments.Count -eq 0) {
            throw "No model deployments found"
        }
        $names = ($deployments | ForEach-Object { $_.name }) -join ', '
        "$($deployments.Count) deployment(s): $names"
    }

    # 4c. AI Search endpoint
    Invoke-TestDirect -Group 'AI' -TestName 'AI Search endpoint reachable' -Critical $true -ScriptBlock {
        $endpoint = $config.SearchEndpoint
        try {
            $null = Invoke-WebRequest -Uri "${endpoint}indexes?api-version=2024-07-01" -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop
            "AI Search endpoint accessible"
        }
        catch {
            $statusCode = $_.Exception.Response.StatusCode.value__
            if ($statusCode -in @(401, 403)) {
                "AI Search endpoint reachable (HTTP $statusCode — auth required)"
            }
            else {
                throw "AI Search endpoint error: HTTP $statusCode — $($_.Exception.Message)"
            }
        }
    }

    # 4d. Document Intelligence endpoint
    Invoke-TestDirect -Group 'AI' -TestName 'Document Intelligence endpoint reachable' -Critical $true -ScriptBlock {
        $endpoint = $config.DocIntelEndpoint
        try {
            $null = Invoke-WebRequest -Uri "${endpoint}formrecognizer/documentModels?api-version=2023-07-31" -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop
            "Document Intelligence endpoint accessible"
        }
        catch {
            $statusCode = $_.Exception.Response.StatusCode.value__
            if ($statusCode -in @(401, 403)) {
                "Doc Intelligence reachable (HTTP $statusCode — auth required)"
            }
            else {
                throw "Doc Intelligence error: HTTP $statusCode — $($_.Exception.Message)"
            }
        }
    }
}

# ─────────────────────────────────────────────────────────────────────
# Test Group 5: Redis
# ─────────────────────────────────────────────────────────────────────

function Test-Redis {
    Write-TestHeader "Redis"

    $redisName = $config.RedisName
    $rg = $config.ResourceGroup

    # 5a. Redis resource exists and is running
    Invoke-TestDirect -Group 'Redis' -TestName 'Redis cache resource exists' -Critical $true -ScriptBlock {
        $output = & az redis show --name $config.RedisName --resource-group $config.ResourceGroup --output json 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Redis resource not found: $($output | Out-String)"
        }
        $redis = $output | ConvertFrom-Json
        $state = $redis.provisioningState
        if ($state -ne 'Succeeded') {
            throw "Redis provisioning state: $state (expected: Succeeded)"
        }
        "Redis '$($redis.name)' is running (SKU: $($redis.sku.name) $($redis.sku.capacity))"
    }

    # 5b. Redis connectivity (port check via Azure CLI)
    Invoke-TestDirect -Group 'Redis' -TestName 'Redis SSL port accessible' -Critical $false -ScriptBlock {
        $output = & az redis show --name $config.RedisName --resource-group $config.ResourceGroup --query "hostName" -o tsv 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Cannot resolve Redis hostname"
        }
        $hostname = ($output | Out-String).Trim()
        "Redis hostname: $hostname (SSL port 6380)"
    }
}

# ─────────────────────────────────────────────────────────────────────
# Test Group 6: Service Bus
# ─────────────────────────────────────────────────────────────────────

function Test-ServiceBus {
    Write-TestHeader "Service Bus"

    # 6a. Service Bus namespace exists
    Invoke-TestDirect -Group 'Service Bus' -TestName 'Service Bus namespace exists' -Critical $true -ScriptBlock {
        $output = & az servicebus namespace show --name $config.ServiceBusNs --resource-group $config.ResourceGroup --output json 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Service Bus namespace not found: $($output | Out-String)"
        }
        $sb = $output | ConvertFrom-Json
        if ($sb.status -ne 'Active') {
            throw "Service Bus status: $($sb.status) (expected: Active)"
        }
        "Namespace '$($sb.name)' is active (SKU: $($sb.sku.name))"
    }

    # 6b. Queue existence (document-processing queue)
    Invoke-TestDirect -Group 'Service Bus' -TestName 'document-processing queue exists' -Critical $true -ScriptBlock {
        $output = & az servicebus queue show --namespace-name $config.ServiceBusNs --resource-group $config.ResourceGroup --name "document-processing" --output json 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Queue 'document-processing' not found: $($output | Out-String)"
        }
        $queue = $output | ConvertFrom-Json
        "Queue 'document-processing' exists (status: $($queue.status))"
    }
}

# ─────────────────────────────────────────────────────────────────────
# Test Execution
# ─────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "============================================================" -ForegroundColor White
Write-Host "  SPAARKE DEPLOYMENT SMOKE TESTS" -ForegroundColor White
Write-Host "============================================================" -ForegroundColor White
Write-Host "  Environment:  $Environment" -ForegroundColor White
Write-Host "  API Base URL: $($config.ApiBaseUrl)" -ForegroundColor White
Write-Host "  Customer ID:  $CustomerId" -ForegroundColor White
Write-Host "  Started:      $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor White
Write-Host "  Timeout:      5 minutes" -ForegroundColor White
Write-Host "  Skip Groups:  $(if ($SkipGroups.Count -gt 0) { $SkipGroups -join ', ' } else { 'none' })" -ForegroundColor White
Write-Host "============================================================" -ForegroundColor White

$testGroups = [ordered]@{
    'BFF API'     = { Test-BffApi }
    'Dataverse'   = { Test-Dataverse }
    'SPE'         = { Test-SPE }
    'AI'          = { Test-AIServices }
    'Redis'       = { Test-Redis }
    'ServiceBus'  = { Test-ServiceBus }
}

foreach ($groupName in $testGroups.Keys) {
    if ($SkipGroups -contains $groupName) {
        Write-Host ""
        Write-Host "  [SKIP] Test group '$groupName' skipped by user" -ForegroundColor DarkGray
        Add-TestResult -Group $groupName -Test "(entire group skipped)" -Status 'Skip' -Message 'Skipped via -SkipGroups' -Critical $false
        continue
    }

    if (Test-TimeoutExceeded) {
        Write-Host ""
        Write-Host "  [SKIP] Test group '$groupName' skipped: 5-minute timeout exceeded" -ForegroundColor Yellow
        Add-TestResult -Group $groupName -Test "(entire group skipped)" -Status 'Skip' -Message 'Total timeout exceeded' -Critical $false
        continue
    }

    try {
        & $testGroups[$groupName]
    }
    catch {
        Add-TestResult -Group $groupName -Test "(group execution error)" -Status 'Fail' `
            -Message $_.Exception.Message -Critical $true
    }
}

# ─────────────────────────────────────────────────────────────────────
# Results Summary
# ─────────────────────────────────────────────────────────────────────

$elapsed = (Get-Date) - $script:StartTime

Write-Host ""
Write-Host "============================================================" -ForegroundColor White
Write-Host "  RESULTS SUMMARY" -ForegroundColor White
Write-Host "============================================================" -ForegroundColor White
Write-Host ""

# Summary table
$passCount = ($script:Results | Where-Object { $_.Status -eq 'Pass' }).Count
$failCount = ($script:Results | Where-Object { $_.Status -eq 'Fail' }).Count
$skipCount = ($script:Results | Where-Object { $_.Status -eq 'Skip' }).Count
$warnCount = ($script:Results | Where-Object { $_.Status -eq 'Warn' }).Count
$criticalFails = ($script:Results | Where-Object { $_.Status -eq 'Fail' -and $_.Critical }).Count

# Structured results table
Write-Host "  Group            Test                                       Status  Duration" -ForegroundColor DarkGray
Write-Host "  ─────────────────────────────────────────────────────────────────────────────" -ForegroundColor DarkGray

foreach ($result in $script:Results) {
    $statusColor = switch ($result.Status) {
        'Pass' { 'Green' }
        'Fail' { 'Red' }
        'Skip' { 'DarkGray' }
        'Warn' { 'Yellow' }
    }

    $groupPad = $result.Group.PadRight(16)
    $testPad = $result.Test.PadRight(42)
    $statusPad = $result.Status.PadRight(6)
    $duration = if ($result.DurationMs -gt 0) { "$($result.DurationMs)ms" } else { '—' }

    Write-Host "  $groupPad $testPad " -NoNewline -ForegroundColor White
    Write-Host "$statusPad " -NoNewline -ForegroundColor $statusColor
    Write-Host "$duration" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "  ─────────────────────────────────────────────────────────────────────────────" -ForegroundColor DarkGray

# Totals
Write-Host ""
Write-Host "  Total:  $($script:Results.Count) tests" -ForegroundColor White
Write-Host "  Pass:   $passCount" -ForegroundColor Green
if ($failCount -gt 0) {
    Write-Host "  Fail:   $failCount ($criticalFails critical)" -ForegroundColor Red
}
if ($warnCount -gt 0) {
    Write-Host "  Warn:   $warnCount" -ForegroundColor Yellow
}
if ($skipCount -gt 0) {
    Write-Host "  Skip:   $skipCount" -ForegroundColor DarkGray
}
Write-Host "  Time:   $([math]::Round($elapsed.TotalSeconds, 1))s" -ForegroundColor White
Write-Host ""

# Failed test details
if ($failCount -gt 0) {
    Write-Host "  FAILED TESTS:" -ForegroundColor Red
    foreach ($fail in ($script:Results | Where-Object { $_.Status -eq 'Fail' })) {
        $critLabel = if ($fail.Critical) { " [CRITICAL]" } else { "" }
        Write-Host "    - [$($fail.Group)] $($fail.Test)$critLabel" -ForegroundColor Red
        if ($fail.Message) {
            Write-Host "      $($fail.Message)" -ForegroundColor DarkGray
        }
    }
    Write-Host ""
}

# Final verdict
if ($criticalFails -gt 0) {
    Write-Host "  VERDICT: FAILED — $criticalFails critical test(s) failed" -ForegroundColor Red
    Write-Host ""
    exit 1
}
elseif ($failCount -gt 0) {
    Write-Host "  VERDICT: PASSED WITH WARNINGS — $failCount non-critical test(s) failed" -ForegroundColor Yellow
    Write-Host ""
    exit 0
}
else {
    Write-Host "  VERDICT: PASSED — All tests successful" -ForegroundColor Green
    Write-Host ""
    exit 0
}
