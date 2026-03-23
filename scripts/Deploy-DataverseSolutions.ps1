<#
.SYNOPSIS
    Import all Spaarke managed solutions to a Dataverse environment in dependency order.

.DESCRIPTION
    Deploys all 10 Spaarke managed solutions to a target Dataverse environment.
    Solutions are imported in dependency order (SpaarkeCore first, then webresources,
    then feature solutions). Handles PAC CLI authentication via service principal,
    checks for existing solutions (upgrade vs fresh import), verifies each import,
    and provides comprehensive error handling with rollback guidance.

    The script is idempotent — running it multiple times with the same parameters
    produces the same result (existing solutions are upgraded, not duplicated).

.PARAMETER EnvironmentUrl
    Target Dataverse environment URL (e.g., https://spaarke-demo.crm.dynamics.com)

.PARAMETER TenantId
    Azure AD tenant ID for authentication

.PARAMETER ClientId
    Service principal (app registration) client ID for PAC CLI auth

.PARAMETER ClientSecret
    Service principal client secret. Mutually exclusive with -CertificateThumbprint.

.PARAMETER CertificateThumbprint
    Certificate thumbprint for service principal auth. Mutually exclusive with -ClientSecret.

.PARAMETER SolutionPath
    Path to directory containing managed solution ZIP files.
    Defaults to src/solutions/ relative to repository root.

.PARAMETER SolutionsToImport
    Optional array of solution folder names to import (subset).
    If not specified, all 10 solutions are imported.

.PARAMETER SkipVerification
    Skip post-import verification step (faster, less safe).

.PARAMETER WhatIf
    Show what would be imported without actually importing.

.EXAMPLE
    .\Deploy-DataverseSolutions.ps1 `
        -EnvironmentUrl "https://spaarke-demo.crm.dynamics.com" `
        -TenantId "a221a95e-1234-5678-9abc-def012345678" `
        -ClientId "12345678-abcd-efgh-ijkl-123456789012" `
        -ClientSecret "my-secret"

.EXAMPLE
    .\Deploy-DataverseSolutions.ps1 `
        -EnvironmentUrl "https://spaarke-demo.crm.dynamics.com" `
        -TenantId "a221a95e-1234-5678-9abc-def012345678" `
        -ClientId "12345678-abcd-efgh-ijkl-123456789012" `
        -CertificateThumbprint "ABC123DEF456" `
        -SolutionsToImport @("SpaarkeCore", "webresources", "LegalWorkspace")

.EXAMPLE
    .\Deploy-DataverseSolutions.ps1 `
        -EnvironmentUrl "https://spaarke-demo.crm.dynamics.com" `
        -TenantId "a221a95e-..." `
        -ClientId "..." `
        -ClientSecret "..." `
        -WhatIf
    # Shows import plan without executing
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $true)]
    [string]$EnvironmentUrl,

    [Parameter(Mandatory = $true)]
    [string]$TenantId,

    [Parameter(Mandatory = $true)]
    [string]$ClientId,

    [Parameter(Mandatory = $true, ParameterSetName = "Secret")]
    [string]$ClientSecret,

    [Parameter(Mandatory = $true, ParameterSetName = "Certificate")]
    [string]$CertificateThumbprint,

    [string]$SolutionPath,

    [string[]]$SolutionsToImport,

    [switch]$SkipVerification
)

$ErrorActionPreference = "Stop"
$StartTime = Get-Date

# Resolve pac to pac.cmd (bash wrapper scripts can't be piped in PowerShell)
$script:PacExe = $null
try {
    $pacPath = Get-Command pac -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
    if ($pacPath -match '\.cmd$') {
        $script:PacExe = $pacPath
    }
    elseif (Test-Path "$env:LOCALAPPDATA\Microsoft\PowerAppsCLI\pac.cmd") {
        $script:PacExe = "$env:LOCALAPPDATA\Microsoft\PowerAppsCLI\pac.cmd"
    }
    else {
        $script:PacExe = $pacPath
    }
}
catch {
    if (Test-Path "$env:LOCALAPPDATA\Microsoft\PowerAppsCLI\pac.cmd") {
        $script:PacExe = "$env:LOCALAPPDATA\Microsoft\PowerAppsCLI\pac.cmd"
    }
}

# ============================================================================
# CONFIGURATION: Solution dependency order
# ============================================================================
# SpaarkeCore MUST be first — all other solutions depend on its entities.
# webresources second — JS files referenced by feature solutions.
# Feature solutions are independent of each other and can be in any order.
# ============================================================================

$SolutionImportOrder = [ordered]@{
    # Tier 1: Base solution (entities, option sets, security roles)
    "SpaarkeCore"          = @{ DisplayName = "Spaarke Core";             SolutionName = "SpaarkeCore";          Tier = 1 }

    # Tier 2: Web resources (JS files used by forms and ribbons)
    "webresources"         = @{ DisplayName = "Spaarke Web Resources";    SolutionName = "SpaarkeWebResources";  Tier = 2 }

    # Tier 3: Feature solutions (independent of each other)
"CalendarSidePane"     = @{ DisplayName = "Calendar Side Pane";       SolutionName = "CalendarSidePane";     Tier = 3 }
    "DocumentUploadWizard" = @{ DisplayName = "Document Upload Wizard";   SolutionName = "DocumentUploadWizard"; Tier = 3 }
    "EventCommands"        = @{ DisplayName = "Event Ribbon Commands";    SolutionName = "EventRibbons";         Tier = 3 }
    "EventDetailSidePane"  = @{ DisplayName = "Event Detail Side Pane";   SolutionName = "EventDetailSidePane";  Tier = 3 }
    "EventsPage"           = @{ DisplayName = "Events Page";              SolutionName = "EventsPage";           Tier = 3 }
    "LegalWorkspace"       = @{ DisplayName = "Legal Workspace";          SolutionName = "LegalWorkspace";       Tier = 3 }
    "TodoDetailSidePane"   = @{ DisplayName = "Todo Detail Side Pane";    SolutionName = "TodoDetailSidePane";   Tier = 3 }
}

# ============================================================================
# FUNCTIONS
# ============================================================================

function Write-StepHeader {
    param([int]$Step, [int]$Total, [string]$Message)
    Write-Host ""
    Write-Host "[$Step/$Total] $Message" -ForegroundColor Yellow
}

function Write-Success {
    param([string]$Message)
    Write-Host "  $Message" -ForegroundColor Green
}

function Write-Detail {
    param([string]$Message)
    Write-Host "  $Message" -ForegroundColor Gray
}

function Write-Warn {
    param([string]$Message)
    Write-Host "  WARNING: $Message" -ForegroundColor DarkYellow
}

function Test-PacCli {
    try {
        if (-not $script:PacExe) { return $false }
        $version = & $script:PacExe help 2>&1 | Out-String
        if ($version -match 'Microsoft PowerPlatform CLI|Usage: pac') {
            Write-Detail "PAC CLI: available ($script:PacExe)"
            return $true
        }
        return $false
    }
    catch {
        return $false
    }
}

function Get-ExistingSolutions {
    param([string]$Environment)

    Write-Detail "Querying existing solutions..."
    $output = & $script:PacExe solution list --environment $Environment 2>&1 | Out-String

    if ($LASTEXITCODE -ne 0) {
        Write-Warn "Could not query existing solutions. Proceeding with fresh import assumption."
        return @{}
    }

    $existing = @{}
    foreach ($entry in $SolutionImportOrder.GetEnumerator()) {
        $solName = $entry.Value.SolutionName
        if ($output -match $solName) {
            $versionMatch = [regex]::Match($output, "$solName\s+([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)")
            $version = if ($versionMatch.Success) { $versionMatch.Groups[1].Value } else { "unknown" }
            $existing[$entry.Key] = $version
        }
    }

    return $existing
}

function Import-ManagedSolution {
    param(
        [string]$ZipPath,
        [string]$DisplayName,
        [string]$Environment,
        [bool]$IsUpgrade
    )

    $action = if ($IsUpgrade) { "Upgrading" } else { "Importing" }
    Write-Host "  $action $DisplayName..." -ForegroundColor Cyan
    Write-Detail "ZIP: $ZipPath"

    $importArgs = @(
        "solution", "import",
        "--path", $ZipPath,
        "--publish-changes",
        "--force-overwrite",
        "--environment", $Environment
    )

    $output = & $script:PacExe @importArgs 2>&1 | Out-String

    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "  FAILED: Import of $DisplayName failed (exit code $LASTEXITCODE)" -ForegroundColor Red
        Write-Host ""
        Write-Host "  PAC CLI output:" -ForegroundColor Yellow
        Write-Host $output
        Write-Host ""
        Write-Host "  Common causes:" -ForegroundColor Yellow
        Write-Host "    - Missing dependency: Ensure SpaarkeCore was imported first" -ForegroundColor White
        Write-Host "    - Version conflict: Target has newer version than source" -ForegroundColor White
        Write-Host "    - Auth expired: Re-authenticate with pac auth create" -ForegroundColor White
        Write-Host "    - Managed/unmanaged conflict: Remove unmanaged version first" -ForegroundColor White
        return $false
    }

    Write-Success "$DisplayName imported successfully."
    return $true
}

function Find-SolutionZip {
    param(
        [string]$SolutionFolder,
        [string]$BasePath
    )

    $folderPath = Join-Path $BasePath $SolutionFolder

    # Look for managed solution ZIP in common locations
    $searchPatterns = @(
        (Join-Path $folderPath "bin" "Release" "*.zip"),
        (Join-Path $folderPath "bin" "Debug" "*.zip"),
        (Join-Path $folderPath "*.zip"),
        (Join-Path $folderPath "out" "*.zip")
    )

    foreach ($pattern in $searchPatterns) {
        $zips = Get-ChildItem -Path $pattern -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match "managed" -or $_.Name -notmatch "unmanaged" } |
            Sort-Object LastWriteTime -Descending
        if ($zips) {
            return $zips[0].FullName
        }
    }

    return $null
}

# ============================================================================
# MAIN EXECUTION
# ============================================================================

Write-Host ""
Write-Host "======================================================" -ForegroundColor Cyan
Write-Host "  Spaarke Dataverse Solution Deployment" -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Environment : $EnvironmentUrl"
Write-Host "  Tenant      : $TenantId"
Write-Host "  Auth Method : $(if ($ClientSecret) { 'Client Secret' } else { 'Certificate' })"
Write-Host ""

# Determine solution path
if (-not $SolutionPath) {
    $SolutionPath = Join-Path $PSScriptRoot ".." "src" "solutions"
}
$SolutionPath = (Resolve-Path $SolutionPath -ErrorAction Stop).Path
Write-Host "  Solution Dir: $SolutionPath"
Write-Host ""

# Filter solutions if subset requested
$solutionsToProcess = [ordered]@{}
if ($SolutionsToImport -and $SolutionsToImport.Count -gt 0) {
    foreach ($sol in $SolutionImportOrder.GetEnumerator()) {
        if ($SolutionsToImport -contains $sol.Key) {
            $solutionsToProcess[$sol.Key] = $sol.Value
        }
    }

    # Warn if SpaarkeCore is not included but other solutions are
    if (-not $solutionsToProcess.Contains("SpaarkeCore") -and $solutionsToProcess.Count -gt 0) {
        Write-Warn "SpaarkeCore is not in the import list. Ensure it is already installed in the target environment."
    }

    Write-Host "  Importing subset: $($solutionsToProcess.Keys -join ', ')"
} else {
    foreach ($sol in $SolutionImportOrder.GetEnumerator()) {
        $solutionsToProcess[$sol.Key] = $sol.Value
    }
    Write-Host "  Importing all $($solutionsToProcess.Count) solutions"
}
Write-Host ""

$totalSteps = 4
$stepNum = 1

# ---- Step 1: Verify prerequisites -------------------------------------------

Write-StepHeader $stepNum $totalSteps "Verifying prerequisites"
$stepNum++

# Check PAC CLI
if (-not (Test-PacCli)) {
    Write-Host "  ERROR: PAC CLI not found or not working." -ForegroundColor Red
    Write-Host "  Install: https://learn.microsoft.com/en-us/power-platform/developer/cli/introduction" -ForegroundColor Yellow
    Write-Host "  Or: dotnet tool install --global Microsoft.PowerApps.CLI.Tool" -ForegroundColor Yellow
    exit 1
}
Write-Success "PAC CLI available."

# Locate solution ZIPs
$solutionZips = [ordered]@{}
$missingZips = @()

foreach ($entry in $solutionsToProcess.GetEnumerator()) {
    $zipPath = Find-SolutionZip -SolutionFolder $entry.Key -BasePath $SolutionPath
    if ($zipPath) {
        $solutionZips[$entry.Key] = $zipPath
        Write-Detail "Found: $($entry.Value.DisplayName) -> $(Split-Path $zipPath -Leaf)"
    } else {
        $missingZips += $entry.Key
        Write-Warn "No ZIP found for $($entry.Value.DisplayName) in $SolutionPath\$($entry.Key)"
    }
}

if ($missingZips.Count -eq $solutionsToProcess.Count) {
    Write-Host ""
    Write-Host "  ERROR: No solution ZIPs found." -ForegroundColor Red
    Write-Host "  Ensure managed solution ZIP files exist under: $SolutionPath" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Expected locations (per solution):" -ForegroundColor Yellow
    Write-Host "    {SolutionFolder}/bin/Release/*.zip" -ForegroundColor White
    Write-Host "    {SolutionFolder}/*.zip" -ForegroundColor White
    Write-Host ""
    Write-Host "  Build solutions first:" -ForegroundColor Yellow
    Write-Host "    pac solution pack --zipfile <output.zip> --folder <solution-folder> --packagetype Managed" -ForegroundColor White
    exit 1
}

if ($missingZips.Count -gt 0) {
    Write-Warn "$($missingZips.Count) solution(s) will be skipped (no ZIP found): $($missingZips -join ', ')"
}

Write-Host ""

# ---- WhatIf: Show plan and exit ---------------------------------------------

if ($WhatIfPreference) {
    Write-Host ""
    Write-Host "=== IMPORT PLAN (WhatIf) ===" -ForegroundColor Cyan
    Write-Host ""
    $order = 1
    foreach ($entry in $solutionsToProcess.GetEnumerator()) {
        $status = if ($solutionZips.Contains($entry.Key)) { "READY" } else { "SKIP (no ZIP)" }
        $tier = $entry.Value.Tier
        Write-Host "  $order. [$status] $($entry.Value.DisplayName) (Tier $tier)" -ForegroundColor $(if ($status -eq "READY") { "Green" } else { "Yellow" })
        if ($solutionZips.Contains($entry.Key)) {
            Write-Host "     ZIP: $($solutionZips[$entry.Key])" -ForegroundColor Gray
        }
        $order++
    }
    Write-Host ""
    Write-Host "  Target: $EnvironmentUrl" -ForegroundColor Gray
    Write-Host "  Total to import: $($solutionZips.Count) of $($solutionsToProcess.Count)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Remove -WhatIf to execute." -ForegroundColor Yellow
    exit 0
}

# ---- Step 2: Authenticate with PAC CLI --------------------------------------

Write-StepHeader $stepNum $totalSteps "Authenticating with PAC CLI"
$stepNum++

Write-Detail "Creating auth profile for $EnvironmentUrl..."

$authArgs = @(
    "auth", "create",
    "--environment", $EnvironmentUrl,
    "--tenant", $TenantId,
    "--applicationId", $ClientId
)

if ($ClientSecret) {
    $authArgs += "--clientSecret"
    $authArgs += $ClientSecret
} else {
    $authArgs += "--certificateThumbprint"
    $authArgs += $CertificateThumbprint
}

$authOutput = & $script:PacExe @authArgs 2>&1 | Out-String

if ($LASTEXITCODE -ne 0) {
    # Check if auth already exists (not an error)
    if ($authOutput -match "already exists" -or $authOutput -match "successfully") {
        Write-Success "Auth profile exists for environment."
    } else {
        Write-Host "  ERROR: PAC CLI authentication failed." -ForegroundColor Red
        Write-Host $authOutput
        Write-Host ""
        Write-Host "  Troubleshooting:" -ForegroundColor Yellow
        Write-Host "    - Verify TenantId, ClientId, and credentials" -ForegroundColor White
        Write-Host "    - Ensure service principal has Dataverse System Administrator role" -ForegroundColor White
        Write-Host "    - Check: pac auth list" -ForegroundColor White
        exit 1
    }
} else {
    Write-Success "Authenticated successfully."
}

# Select the auth profile for this environment
& $script:PacExe auth select --environment $EnvironmentUrl 2>&1 | Out-Null

# Check existing solutions for upgrade detection
$existingSolutions = Get-ExistingSolutions -Environment $EnvironmentUrl
if ($existingSolutions.Count -gt 0) {
    Write-Host ""
    Write-Host "  Existing solutions detected (will upgrade):" -ForegroundColor Cyan
    foreach ($sol in $existingSolutions.GetEnumerator()) {
        Write-Detail "  $($sol.Key) v$($sol.Value)"
    }
}
Write-Host ""

# ---- Step 3: Import solutions in dependency order ----------------------------

Write-StepHeader $stepNum $totalSteps "Importing solutions in dependency order"
$stepNum++

$imported = @()
$failed = @()
$skipped = @()

$importNum = 1
$importTotal = $solutionZips.Count

foreach ($entry in $solutionsToProcess.GetEnumerator()) {
    $folderName = $entry.Key
    $solInfo = $entry.Value

    if (-not $solutionZips.Contains($folderName)) {
        $skipped += $folderName
        continue
    }

    $zipPath = $solutionZips[$folderName]
    $isUpgrade = $existingSolutions.ContainsKey($folderName)

    Write-Host ""
    Write-Host "  --- [$importNum/$importTotal] $($solInfo.DisplayName) (Tier $($solInfo.Tier)) ---" -ForegroundColor Cyan

    $success = Import-ManagedSolution `
        -ZipPath $zipPath `
        -DisplayName $solInfo.DisplayName `
        -Environment $EnvironmentUrl `
        -IsUpgrade $isUpgrade

    if ($success) {
        $imported += $folderName
    } else {
        $failed += $folderName

        # If SpaarkeCore fails, abort — everything else depends on it
        if ($folderName -eq "SpaarkeCore") {
            Write-Host ""
            Write-Host "  CRITICAL: SpaarkeCore import failed. Aborting remaining imports." -ForegroundColor Red
            Write-Host "  All other solutions depend on SpaarkeCore." -ForegroundColor Red
            break
        }

        # If webresources fails, warn but continue with feature solutions
        if ($folderName -eq "webresources") {
            Write-Warn "webresources import failed. Feature solutions may have missing JS references."
        }
    }

    $importNum++
}

Write-Host ""

# ---- Step 4: Verify imports --------------------------------------------------

if (-not $SkipVerification -and $imported.Count -gt 0) {
    Write-StepHeader $stepNum $totalSteps "Verifying imported solutions"
    $stepNum++

    Write-Detail "Querying solution list from environment..."
    $verifyOutput = & $script:PacExe solution list --environment $EnvironmentUrl 2>&1 | Out-String

    if ($LASTEXITCODE -ne 0) {
        Write-Warn "Could not query solution list for verification."
        Write-Host "  Run manually: pac solution list --environment $EnvironmentUrl" -ForegroundColor Gray
    } else {
        $verified = 0
        $verifyFailed = 0

        foreach ($folderName in $imported) {
            $solName = $solutionsToProcess[$folderName].SolutionName
            $displayName = $solutionsToProcess[$folderName].DisplayName

            if ($verifyOutput -match $solName) {
                $versionMatch = [regex]::Match($verifyOutput, "$solName\s+([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)")
                $version = if ($versionMatch.Success) { $versionMatch.Groups[1].Value } else { "detected" }
                Write-Success "$displayName v$version verified."
                $verified++
            } else {
                Write-Warn "$displayName ($solName) not found in solution list. Import may still be processing."
                $verifyFailed++
            }
        }

        Write-Host ""
        Write-Host "  Verification: $verified verified, $verifyFailed not yet confirmed" -ForegroundColor $(if ($verifyFailed -eq 0) { "Green" } else { "Yellow" })
    }
} else {
    Write-StepHeader $stepNum $totalSteps "Verification skipped"
    $stepNum++

    if ($SkipVerification) {
        Write-Detail "Skipped by -SkipVerification flag."
    } else {
        Write-Detail "No solutions were imported."
    }
}

# ============================================================================
# SUMMARY
# ============================================================================

$elapsed = (Get-Date) - $StartTime

Write-Host ""
Write-Host "======================================================" -ForegroundColor Cyan
Write-Host "  Deployment Summary" -ForegroundColor Cyan
Write-Host "======================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Environment : $EnvironmentUrl"
Write-Host "  Duration    : $([math]::Round($elapsed.TotalMinutes, 1)) minutes"
Write-Host ""

if ($imported.Count -gt 0) {
    Write-Host "  Imported ($($imported.Count)):" -ForegroundColor Green
    foreach ($sol in $imported) {
        Write-Host "    [OK] $($solutionsToProcess[$sol].DisplayName)" -ForegroundColor Green
    }
}

if ($skipped.Count -gt 0) {
    Write-Host "  Skipped ($($skipped.Count)):" -ForegroundColor Yellow
    foreach ($sol in $skipped) {
        Write-Host "    [--] $($solutionsToProcess[$sol].DisplayName) (no ZIP)" -ForegroundColor Yellow
    }
}

if ($failed.Count -gt 0) {
    Write-Host "  Failed ($($failed.Count)):" -ForegroundColor Red
    foreach ($sol in $failed) {
        Write-Host "    [XX] $($solutionsToProcess[$sol].DisplayName)" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "  Rollback guidance:" -ForegroundColor Yellow
    Write-Host "    - Failed managed solution imports do not leave partial state" -ForegroundColor White
    Write-Host "    - To rollback a succeeded upgrade: pac solution delete --solution-name <name> --environment $EnvironmentUrl" -ForegroundColor White
    Write-Host "    - Then re-import the previous version ZIP" -ForegroundColor White
    Write-Host ""
    exit 1
}

Write-Host ""
Write-Host "  Deployment complete." -ForegroundColor Green
Write-Host ""
