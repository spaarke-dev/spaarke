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
      5. Naming Conformance (H13)  -  invokes scripts/naming-conformance-check.ps1 against
                                      r1-owned surfaces (bicep + seeder + appsettings.template
                                      + config); exit 0 required for H13 acceptance to pass.
                                      Per customer-provisioning-orchestration-r1 spec FR-35 +
                                      SC #17 + design.md H13. Added by r1 task 021.

    This is the capstone validation tool for the production environment setup project.
    Run this after deploying to any non-dev environment to catch configuration issues.

    Prerequisites:
      - Azure CLI authenticated (`az login`)
      - PAC CLI authenticated to the target Dataverse environment (`pac auth create`)
      - PowerShell 7.4+ available in PATH (naming-conformance-check.ps1 uses Set-StrictMode Latest)

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

    Extension:
      Project: customer-provisioning-orchestration-r1
      Task: 021  -  Wire naming-conformance-check.ps1 into H13 acceptance (spec FR-35 + SC #17).
                    The r3-owned check (scripts/naming-conformance-check.ps1, r3 task 063) is
                    invoked as-is; this script MUST NOT modify it. Exceptions codified in
                    projects/customer-provisioning-orchestration-r1/notes/naming-exception-registry.md
                    are honored by construction: the r3 check hardcodes `spaarke-spekvcert` as
                    the legacy vault exception, and grandfathers PascalCase secrets like
                    `BFF-API-ClientSecret` / `Dataverse-ClientSecret` via R2 casing-drift
                    semantics (flags only multiple casings). No exception-registry file
                    argument is required or supported.
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
# Check 5: Naming Conformance (H13 acceptance gate)
# ─────────────────────────────────────────────────────────────────────
# Per customer-provisioning-orchestration-r1 spec FR-35 + SC #17 + design.md H13.
# Invokes the r3-owned repo-scanner scripts/naming-conformance-check.ps1 (r3 task 063).
# SCOPE: r1-owned surfaces only. The r3 check's default -Path auto-discovers exactly
# these: infrastructure/bicep/**/*.bicep + scripts/Seed-ProductionKeyVault.ps1 +
# src/server/api/Sprk.Bff.Api/appsettings.template.json + config/environments.json.
# It does NOT touch live Azure resources — no live-dev sweep occurs by construction
# (satisfies owner directive #3).
# EXCEPTIONS: Codified in projects/customer-provisioning-orchestration-r1/notes/
# naming-exception-registry.md and honored by the r3 check's built-in logic
# (`spaarke-spekvcert` vault + PascalCase secret grandfathering via R2 casing-drift).
# EXIT: Any non-zero exit surfaces as `Fail` via Add-TestResult; the final-verdict block
# then exits the outer script with 1, which propagates as an H13 acceptance-gate failure.

function Test-NamingConformance {
    Write-TestHeader "CHECK 5: Naming Conformance (H13 acceptance)"

    $checkScript = Join-Path $PSScriptRoot 'naming-conformance-check.ps1'
    if (-not (Test-Path $checkScript)) {
        Add-TestResult -Group 'H13 Naming' -Test 'naming-conformance-check.ps1 available' -Status 'Fail' `
            -Message "Expected r3 check at '$checkScript' but not found. r3 task 063 owns this file; restore from origin/master before re-running H13."
        return
    }

    Add-TestResult -Group 'H13 Naming' -Test 'naming-conformance-check.ps1 available' -Status 'Pass' `
        -Message "Located at $checkScript"

    try {
        # Invoke r3 check with default -Path (auto-discovers r1-owned surfaces).
        # Merge all output streams so violation table + PASS/FAIL header are captured.
        $output = & $checkScript 2>&1 | Out-String
        $exitCode = $LASTEXITCODE

        if ($exitCode -eq 0) {
            # r3 script prints "naming-conformance-check: PASS — N file(s) scanned, 0 violations."
            $summaryLine = ($output -split "`n" | Where-Object { $_ -match 'naming-conformance-check:' } | Select-Object -First 1).Trim()
            Add-TestResult -Group 'H13 Naming' -Test 'naming-conformance-check exit 0' -Status 'Pass' `
                -Message $(if ($summaryLine) { $summaryLine } else { 'r3 gate returned exit 0 — no violations.' })
            Add-TestResult -Group 'H13 Naming' -Test 'H13 acceptance gate (naming)' -Status 'Pass' `
                -Message "Exceptions honored by construction (spaarke-spekvcert + PascalCase grandfathering per naming-exception-registry.md)."
        }
        else {
            # Non-zero exit: capture the violation snippet for the diagnostic. The r3 script's
            # Format-Table output already cites Rule + Name + File + expected canonical form
            # ("R1 env-token-in-name" / "R2 casing-drift" / "R3 vault-name-drift"). Truncate to
            # keep the report readable; full detail is on stdout for the operator.
            $snippet = ($output.Trim() -split "`n" | Select-Object -First 20) -join "`n        "
            Add-TestResult -Group 'H13 Naming' -Test 'naming-conformance-check exit 0' -Status 'Fail' `
                -Message "H13 FAIL: naming-conformance-check exit $exitCode.`n        See full output above. First lines:`n        $snippet"
            Add-TestResult -Group 'H13 Naming' -Test 'H13 acceptance gate (naming)' -Status 'Fail' `
                -Message "H13 blocks acceptance. Remediate the flagged resource per docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md or, if the name is a codified carve-out, add it to projects/customer-provisioning-orchestration-r1/notes/naming-exception-registry.md AND coordinate with r3 to extend the built-in gate exceptions."
            # Also echo the raw script output so nothing is swallowed.
            Write-Host ""
            Write-Host "  --- naming-conformance-check.ps1 output ---" -ForegroundColor DarkGray
            Write-Host $output -ForegroundColor DarkGray
            Write-Host "  -------------------------------------------" -ForegroundColor DarkGray
        }
    }
    catch {
        Add-TestResult -Group 'H13 Naming' -Test 'naming-conformance-check invocation' -Status 'Fail' `
            -Message "Invocation threw before returning an exit code: $($_.Exception.Message). Verify pwsh 7.4+ is available and the r3 script is executable."
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
Test-NamingConformance   # H13 acceptance gate — customer-provisioning-orchestration-r1 task 021

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
