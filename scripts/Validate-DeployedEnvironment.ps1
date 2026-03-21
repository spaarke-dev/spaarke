#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Validates that all environment-specific configuration is correctly set in a deployed Spaarke environment.

.DESCRIPTION
    Comprehensive validation script that verifies:
      1. Dataverse Environment Variables  -  all 7 required variables exist and have non-empty values
      2. BFF API Health  -  /healthz and /ping return 200
      3. CORS Origin  -  BFF API CORS configuration includes the Dataverse org URL
      4. Dev Value Leakage  -  no dev-only identifiers remain in env var values

    This is the capstone validation tool for the production environment setup project.
    Run this after deploying to any non-dev environment to catch configuration issues.

    Prerequisites:
      - Azure CLI authenticated (`az login`)
      - PAC CLI authenticated to the target Dataverse environment (`pac auth create`)

.PARAMETER DataverseUrl
    The Dataverse organization URL (e.g., https://myorg.crm.dynamics.com).

.PARAMETER BffApiUrl
    BFF API base URL. If omitted, reads from the sprk_BffApiBaseUrl environment variable in Dataverse.

.EXAMPLE
    .\Validate-DeployedEnvironment.ps1 -DataverseUrl "https://myorg.crm.dynamics.com"
    # Validates env vars, API health, CORS, and dev leakage

.EXAMPLE
    .\Validate-DeployedEnvironment.ps1 -DataverseUrl "https://myorg.crm.dynamics.com" -BffApiUrl "https://api.spaarke.com"
    # Validates with explicit BFF API URL

.NOTES
    Project: production-environment-setup-r2
    Task: ENV-050  -  Create Validate-DeployedEnvironment.ps1 validation script
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$DataverseUrl,

    [Parameter(Mandatory = $false)]
    [string]$BffApiUrl
)

$ErrorActionPreference = "Continue"
$script:Results = [System.Collections.ArrayList]::new()

# ─────────────────────────────────────────────────────────────────────
# Configuration
# ─────────────────────────────────────────────────────────────────────

# Normalize DataverseUrl  -  strip trailing slash
$DataverseUrl = $DataverseUrl.TrimEnd('/')

# The 7 required Dataverse Environment Variables
$RequiredEnvVars = @(
    'sprk_BffApiBaseUrl',
    'sprk_BffApiAppId',
    'sprk_MsalClientId',
    'sprk_TenantId',
    'sprk_AzureOpenAiEndpoint',
    'sprk_ShareLinkBaseUrl',
    'sprk_SharePointEmbeddedContainerId'
)

# Dev value patterns that should NEVER appear in non-dev environments
$DevLeakagePatterns = @(
    @{ Pattern = 'spaarkedev1';  Description = 'Dev Dataverse org (spaarkedev1)' },
    @{ Pattern = 'spe-api-dev';  Description = 'Dev App Service name (spe-api-dev)' },
    @{ Pattern = '67e2xz';       Description = 'Dev App Service suffix (67e2xz)' },
    @{ Pattern = '170c98e1';     Description = 'Dev GUID fragment (170c98e1)' },
    @{ Pattern = '1e40baad';     Description = 'Dev GUID fragment (1e40baad)' },
    @{ Pattern = 'b36e9b91';     Description = 'Dev GUID fragment (b36e9b91)' }
)

# ─────────────────────────────────────────────────────────────────────
# Helper Functions
# ─────────────────────────────────────────────────────────────────────

function Write-TestHeader {
    param([string]$GroupName)
    Write-Host ""
    Write-Host ("=" * 70) -ForegroundColor Cyan
    Write-Host "  $GroupName" -ForegroundColor Cyan
    Write-Host ("=" * 70) -ForegroundColor Cyan
}

function Add-TestResult {
    param(
        [string]$Group,
        [string]$Test,
        [ValidateSet('Pass', 'Fail', 'Warn')]
        [string]$Status,
        [string]$Message = ''
    )

    $icon = switch ($Status) {
        'Pass' { '[PASS]' }
        'Fail' { '[FAIL]' }
        'Warn' { '[WARN]' }
    }

    $color = switch ($Status) {
        'Pass' { 'Green' }
        'Fail' { 'Red' }
        'Warn' { 'Yellow' }
    }

    Write-Host "  $icon $Test" -ForegroundColor $color
    if ($Message) {
        Write-Host "        $Message" -ForegroundColor DarkGray
    }

    [void]$script:Results.Add([PSCustomObject]@{
        Group   = $Group
        Test    = $Test
        Status  = $Status
        Message = $Message
    })
}

# ─────────────────────────────────────────────────────────────────────
# Check 1: Dataverse Environment Variables
# ─────────────────────────────────────────────────────────────────────

function Test-DataverseEnvironmentVariables {
    Write-TestHeader "CHECK 1: Dataverse Environment Variables"

    $script:EnvVarValues = @{}

    foreach ($varName in $RequiredEnvVars) {
        try {
            # Query the environment variable definition and its current value
            # Uses Dataverse Web API via az rest with the PAC auth context
            $filter = "schemaname eq '$varName'"
            $apiUrl = "$DataverseUrl/api/data/v9.2/environmentvariabledefinitions?`$filter=$filter&`$expand=environmentvariabledefinition_environmentvariablevalue(`$select=value)&`$select=schemaname,displayname,defaultvalue"

            $response = az rest --method GET --url $apiUrl --resource "$DataverseUrl" 2>&1
            if ($LASTEXITCODE -ne 0) {
                Add-TestResult -Group 'Env Vars' -Test "$varName exists" -Status 'Fail' `
                    -Message "Failed to query Dataverse: $($response | Out-String)"
                continue
            }

            $json = $response | ConvertFrom-Json
            if ($json.value.Count -eq 0) {
                Add-TestResult -Group 'Env Vars' -Test "$varName exists" -Status 'Fail' `
                    -Message "Environment variable definition not found. Create it in the Dataverse solution."
                continue
            }

            $definition = $json.value[0]

            # Get the current value: prefer value record, fall back to default
            $currentValue = $null
            if ($definition.environmentvariabledefinition_environmentvariablevalue -and $definition.environmentvariabledefinition_environmentvariablevalue.Count -gt 0) {
                $currentValue = $definition.environmentvariabledefinition_environmentvariablevalue[0].value
            }
            if ([string]::IsNullOrWhiteSpace($currentValue)) {
                $currentValue = $definition.defaultvalue
            }

            if ([string]::IsNullOrWhiteSpace($currentValue)) {
                Add-TestResult -Group 'Env Vars' -Test "$varName has value" -Status 'Fail' `
                    -Message "Variable exists but has no value and no default. Set a value in the Dataverse Environment Variable UI."
            }
            else {
                # Truncate display for security (show first 30 chars max)
                $displayVal = if ($currentValue.Length -gt 30) { $currentValue.Substring(0, 30) + '...' } else { $currentValue }
                Add-TestResult -Group 'Env Vars' -Test "$varName has value" -Status 'Pass' `
                    -Message "Value: $displayVal"
                $script:EnvVarValues[$varName] = $currentValue
            }
        }
        catch {
            Add-TestResult -Group 'Env Vars' -Test "$varName exists" -Status 'Fail' `
                -Message "Error querying variable: $($_.Exception.Message)"
        }
    }
}

# ─────────────────────────────────────────────────────────────────────
# Check 2: BFF API Health
# ─────────────────────────────────────────────────────────────────────

function Test-BffApiHealth {
    Write-TestHeader "CHECK 2: BFF API Health"

    # Resolve the BFF API URL
    $apiUrl = $BffApiUrl
    if ([string]::IsNullOrWhiteSpace($apiUrl)) {
        if ($script:EnvVarValues.ContainsKey('sprk_BffApiBaseUrl')) {
            $apiUrl = $script:EnvVarValues['sprk_BffApiBaseUrl']
            Write-Host "  Using BFF API URL from Dataverse env var: $apiUrl" -ForegroundColor DarkGray
        }
        else {
            Add-TestResult -Group 'BFF API' -Test 'BFF API URL available' -Status 'Fail' `
                -Message "No -BffApiUrl parameter provided and sprk_BffApiBaseUrl not found in Dataverse. Provide -BffApiUrl parameter."
            return
        }
    }

    $apiUrl = $apiUrl.TrimEnd('/')

    # 2a. GET /healthz
    try {
        $response = Invoke-WebRequest -Uri "$apiUrl/healthz" -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop
        if ($response.StatusCode -eq 200) {
            Add-TestResult -Group 'BFF API' -Test 'GET /healthz returns 200' -Status 'Pass' `
                -Message "HTTP 200  -  Healthy"
        }
        else {
            Add-TestResult -Group 'BFF API' -Test 'GET /healthz returns 200' -Status 'Fail' `
                -Message "Expected 200, got $($response.StatusCode)"
        }
    }
    catch {
        $statusCode = $null
        if ($_.Exception.Response) {
            $statusCode = $_.Exception.Response.StatusCode.value__
        }
        Add-TestResult -Group 'BFF API' -Test 'GET /healthz returns 200' -Status 'Fail' `
            -Message "Request failed$(if ($statusCode) { " (HTTP $statusCode)" }): $($_.Exception.Message)"
    }

    # 2b. GET /ping
    try {
        $response = Invoke-WebRequest -Uri "$apiUrl/ping" -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop
        if ($response.StatusCode -eq 200) {
            Add-TestResult -Group 'BFF API' -Test 'GET /ping returns 200' -Status 'Pass' `
                -Message "HTTP 200  -  Pong"
        }
        else {
            Add-TestResult -Group 'BFF API' -Test 'GET /ping returns 200' -Status 'Fail' `
                -Message "Expected 200, got $($response.StatusCode)"
        }
    }
    catch {
        $statusCode = $null
        if ($_.Exception.Response) {
            $statusCode = $_.Exception.Response.StatusCode.value__
        }
        Add-TestResult -Group 'BFF API' -Test 'GET /ping returns 200' -Status 'Fail' `
            -Message "Request failed$(if ($statusCode) { " (HTTP $statusCode)" }): $($_.Exception.Message)"
    }
}

# ─────────────────────────────────────────────────────────────────────
# Check 3: CORS Origin
# ─────────────────────────────────────────────────────────────────────

function Test-CorsOrigin {
    Write-TestHeader "CHECK 3: CORS Origin Check"

    # Resolve the BFF API URL
    $apiUrl = $BffApiUrl
    if ([string]::IsNullOrWhiteSpace($apiUrl)) {
        if ($script:EnvVarValues.ContainsKey('sprk_BffApiBaseUrl')) {
            $apiUrl = $script:EnvVarValues['sprk_BffApiBaseUrl']
        }
        else {
            Add-TestResult -Group 'CORS' -Test 'CORS includes Dataverse origin' -Status 'Fail' `
                -Message "Cannot test CORS  -  BFF API URL not available."
            return
        }
    }

    $apiUrl = $apiUrl.TrimEnd('/')

    try {
        # Send an OPTIONS preflight request with the Dataverse URL as Origin
        $headers = @{
            'Origin'                        = $DataverseUrl
            'Access-Control-Request-Method'  = 'GET'
            'Access-Control-Request-Headers' = 'Authorization'
        }

        $response = Invoke-WebRequest -Uri "$apiUrl/healthz" -Method OPTIONS -Headers $headers `
            -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop

        $allowedOrigin = $response.Headers['Access-Control-Allow-Origin']

        if ($allowedOrigin -and ($allowedOrigin -eq $DataverseUrl -or $allowedOrigin -eq '*')) {
            Add-TestResult -Group 'CORS' -Test 'CORS includes Dataverse origin' -Status 'Pass' `
                -Message "Access-Control-Allow-Origin: $allowedOrigin"
        }
        else {
            Add-TestResult -Group 'CORS' -Test 'CORS includes Dataverse origin' -Status 'Fail' `
                -Message "Dataverse URL '$DataverseUrl' not in CORS allowed origins. Update the BFF API CORS configuration in App Service or appsettings."
        }
    }
    catch {
        $statusCode = $null
        if ($_.Exception.Response) {
            $statusCode = $_.Exception.Response.StatusCode.value__

            # Even on non-2xx, check if CORS headers are present
            $allowedOrigin = $_.Exception.Response.Headers['Access-Control-Allow-Origin']
            if ($allowedOrigin -and ($allowedOrigin -eq $DataverseUrl -or $allowedOrigin -eq '*')) {
                Add-TestResult -Group 'CORS' -Test 'CORS includes Dataverse origin' -Status 'Pass' `
                    -Message "Access-Control-Allow-Origin: $allowedOrigin (HTTP $statusCode)"
                return
            }
        }

        Add-TestResult -Group 'CORS' -Test 'CORS includes Dataverse origin' -Status 'Warn' `
            -Message ("Could not confirm CORS configuration via preflight. Verify manually in Azure Portal / App Service / CORS. Error: " + $_.Exception.Message)
    }
}

# ─────────────────────────────────────────────────────────────────────
# Check 4: Dev Value Leakage Detection
# ─────────────────────────────────────────────────────────────────────

function Test-DevValueLeakage {
    Write-TestHeader "CHECK 4: Dev Value Leakage Detection"

    if ($script:EnvVarValues.Count -eq 0) {
        Add-TestResult -Group 'Dev Leakage' -Test 'Dev value scan' -Status 'Warn' `
            -Message "No environment variable values were retrieved. Cannot check for dev leakage. Fix env var checks first."
        return
    }

    $leaksFound = $false

    foreach ($leak in $DevLeakagePatterns) {
        $matchingVars = @()

        foreach ($varName in $script:EnvVarValues.Keys) {
            $value = $script:EnvVarValues[$varName]
            if ($value -match [regex]::Escape($leak.Pattern)) {
                $matchingVars += $varName
            }
        }

        if ($matchingVars.Count -gt 0) {
            $leaksFound = $true
            Add-TestResult -Group 'Dev Leakage' -Test "No '$($leak.Pattern)' in env vars" -Status 'Fail' `
                -Message "$($leak.Description) found in: $($matchingVars -join ', '). Update these values to production equivalents."
        }
        else {
            Add-TestResult -Group 'Dev Leakage' -Test "No '$($leak.Pattern)' in env vars" -Status 'Pass' `
                -Message "No matches for $($leak.Description)"
        }
    }

    if (-not $leaksFound) {
        Write-Host "  No dev value leakage detected." -ForegroundColor Green
    }
}

# ─────────────────────────────────────────────────────────────────────
# Execution
# ─────────────────────────────────────────────────────────────────────

$startTime = Get-Date

Write-Host ""
Write-Host "====================================================================" -ForegroundColor White
Write-Host "  SPAARKE DEPLOYED ENVIRONMENT VALIDATION" -ForegroundColor White
Write-Host "====================================================================" -ForegroundColor White
Write-Host "  Dataverse URL:  $DataverseUrl" -ForegroundColor White
Write-Host "  BFF API URL:    $(if ($BffApiUrl) { $BffApiUrl } else { '(auto-detect from env var)' })" -ForegroundColor White
Write-Host "  Started:        $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor White
Write-Host "====================================================================" -ForegroundColor White

# Run all check groups in order
Test-DataverseEnvironmentVariables
Test-BffApiHealth
Test-CorsOrigin
Test-DevValueLeakage

# ─────────────────────────────────────────────────────────────────────
# Results Summary
# ─────────────────────────────────────────────────────────────────────

$elapsed = (Get-Date) - $startTime

Write-Host ""
Write-Host "====================================================================" -ForegroundColor White
Write-Host "  RESULTS SUMMARY" -ForegroundColor White
Write-Host "====================================================================" -ForegroundColor White
Write-Host ""

$passCount = ($script:Results | Where-Object { $_.Status -eq 'Pass' }).Count
$failCount = ($script:Results | Where-Object { $_.Status -eq 'Fail' }).Count
$warnCount = ($script:Results | Where-Object { $_.Status -eq 'Warn' }).Count

# Per-group summary
$groups = $script:Results | Select-Object -ExpandProperty Group -Unique
foreach ($group in $groups) {
    $groupResults = $script:Results | Where-Object { $_.Group -eq $group }
    $groupPass = ($groupResults | Where-Object { $_.Status -eq 'Pass' }).Count
    $groupFail = ($groupResults | Where-Object { $_.Status -eq 'Fail' }).Count
    $groupWarn = ($groupResults | Where-Object { $_.Status -eq 'Warn' }).Count

    $groupIcon = if ($groupFail -gt 0) { '[FAIL]' } elseif ($groupWarn -gt 0) { '[WARN]' } else { '[PASS]' }
    $groupColor = if ($groupFail -gt 0) { 'Red' } elseif ($groupWarn -gt 0) { 'Yellow' } else { 'Green' }

    Write-Host "  $groupIcon $($group.PadRight(20)) Pass: $groupPass  Fail: $groupFail  Warn: $groupWarn" -ForegroundColor $groupColor
}

Write-Host ""
Write-Host "  ────────────────────────────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  Total:  $($script:Results.Count) checks" -ForegroundColor White
Write-Host "  Pass:   $passCount" -ForegroundColor Green
if ($failCount -gt 0) {
    Write-Host "  Fail:   $failCount" -ForegroundColor Red
}
if ($warnCount -gt 0) {
    Write-Host "  Warn:   $warnCount" -ForegroundColor Yellow
}
Write-Host "  Time:   $([math]::Round($elapsed.TotalSeconds, 1))s" -ForegroundColor White
Write-Host ""

# Failed test details with actionable guidance
if ($failCount -gt 0) {
    Write-Host "  FAILED CHECKS:" -ForegroundColor Red
    foreach ($fail in ($script:Results | Where-Object { $_.Status -eq 'Fail' })) {
        Write-Host "    - [$($fail.Group)] $($fail.Test)" -ForegroundColor Red
        if ($fail.Message) {
            Write-Host "      $($fail.Message)" -ForegroundColor DarkGray
        }
    }
    Write-Host ""
}

# Final verdict
if ($failCount -gt 0) {
    Write-Host "  VERDICT: FAILED  -  $failCount check(s) failed. Fix the issues above and re-run." -ForegroundColor Red
    Write-Host ""
    exit 1
}
elseif ($warnCount -gt 0) {
    Write-Host "  VERDICT: PASSED WITH WARNINGS  -  $warnCount warning(s). Review manually." -ForegroundColor Yellow
    Write-Host ""
    exit 0
}
else {
    Write-Host "  VERDICT: PASSED  -  All checks successful. Environment is correctly configured." -ForegroundColor Green
    Write-Host ""
    exit 0
}
