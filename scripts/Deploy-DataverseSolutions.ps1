<#
.SYNOPSIS
    Import all Spaarke managed solutions to a Dataverse environment in dependency order,
    tier-by-tier, with Package Deployer upgrade semantics (H6 handler entrypoint).

.DESCRIPTION
    Deploys the 8 authoritative Spaarke managed solutions (per $SolutionImportOrder
    and spec.md §11.1a / FR-09) to a target Dataverse environment. Solutions are
    imported in dependency-tier order:

        Tier 1: SpaarkeCore
        Tier 2: webresources               (depends on Tier 1)
        Tier 3: CalendarSidePane, DocumentUploadWizard, EventCommands,
                EventDetailSidePane, EventsPage, LegalWorkspace
                (independent of each other; depend on Tier 1+2)

    Per-tier gating: no Tier N+1 solution is imported until every Tier N solution
    has both imported and version-verified. On any import or verification failure,
    the script exits non-zero WITHOUT attempting subsequent tiers (fail-fast per
    project constraint 5 + acceptance criterion c).

    Package Deployer upgrade semantics: in upgrade mode (-Mode Upgrade or
    -Mode Auto when the solution already exists), `pac solution import` is invoked
    with `--stage-and-upgrade`, which stages the incoming solution alongside the
    installed one, applies the upgrade delta, and retires the holding solution
    (spec.md FR-09 acceptance + design.md §14A.2 H6 upgrade-mode row).

    Handles PAC CLI authentication via service principal, checks for existing
    solutions (upgrade vs fresh import), verifies each import, and provides
    error handling with rollback guidance.

    The script is idempotent — running it multiple times with the same parameters
    produces the same result (existing solutions are upgraded to the ZIP's version,
    or detected as already-at-version and skipped).

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
    Optional array of solution folder names to import (subset). Names must match
    keys in $SolutionImportOrder (SpaarkeCore, webresources, CalendarSidePane,
    DocumentUploadWizard, EventCommands, EventDetailSidePane, EventsPage,
    LegalWorkspace). Any name outside these 8 is a defect per spec Q2.
    If not specified, all 8 solutions are imported.

.PARAMETER SkipVerification
    Skip post-import per-tier version verification (faster, less safe).
    In upgrade mode this bypasses the Tier N → Tier N+1 gate — use with caution.

.PARAMETER Mode
    Import mode. One of:
      Auto         — (default) infer per-solution; solution present → upgrade
                     (uses --stage-and-upgrade), absent → fresh install.
      FreshInstall — treat every solution as a fresh install. Fails fast if any
                     target solution already exists (safety gate to prevent
                     accidental double-install).
      Upgrade      — treat every solution as an upgrade. Solutions already
                     present are upgraded via --stage-and-upgrade (Package
                     Deployer semantics); absent solutions fall back to fresh
                     install of the same ZIP.

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

    [switch]$SkipVerification,

    [ValidateSet('Auto','FreshInstall','Upgrade')]
    [string]$Mode = 'Auto'
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
# CONFIGURATION: Solution dependency tiers (authoritative — 8 solutions)
# ============================================================================
# This ordered hashtable IS the single source of truth for the 8 solutions H6
# ships (spec.md §11.1a + Q2 owner clarification). No ad-hoc list literals
# anywhere else in the script (acceptance criterion d).
#
# Tier gating (enforced by the import loop below):
#   Tier 1 MUST fully import + verify before ANY Tier 2 solution starts.
#   Tier 2 MUST fully import + verify before ANY Tier 3 solution starts.
#   Tier 3 solutions are independent of each other; iteration order within
#   Tier 3 is not semantically meaningful.
# ============================================================================

$SolutionImportOrder = [ordered]@{
    # Tier 1: Base solution (entities, option sets, security roles)
    "SpaarkeCore"          = @{ DisplayName = "Spaarke Core";             SolutionName = "SpaarkeCore";          Tier = 1 }

    # Tier 2: Web resources (JS files used by forms and ribbons)
    "webresources"         = @{ DisplayName = "Spaarke Web Resources";    SolutionName = "SpaarkeWebResources";  Tier = 2 }

    # Tier 3: Feature solutions (independent of each other; Tier-3-parallel-safe)
    "CalendarSidePane"     = @{ DisplayName = "Calendar Side Pane";       SolutionName = "CalendarSidePane";     Tier = 3 }
    "DocumentUploadWizard" = @{ DisplayName = "Document Upload Wizard";   SolutionName = "DocumentUploadWizard"; Tier = 3 }
    "EventCommands"        = @{ DisplayName = "Event Ribbon Commands";    SolutionName = "EventRibbons";         Tier = 3 }
    "EventDetailSidePane"  = @{ DisplayName = "Event Detail Side Pane";   SolutionName = "EventDetailSidePane";  Tier = 3 }
    "EventsPage"           = @{ DisplayName = "Events Page";              SolutionName = "EventsPage";           Tier = 3 }
    "LegalWorkspace"       = @{ DisplayName = "Legal Workspace";          SolutionName = "LegalWorkspace";       Tier = 3 }
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

function Get-SolutionVersionFromZip {
    <#
    .SYNOPSIS
        Extracts the SolutionManifest/Version value from a managed solution ZIP.
    .DESCRIPTION
        Reads solution.xml directly from the ZIP without unpacking to disk. Used
        to determine the expected version for per-tier verification and to detect
        the "already at version" state (acceptance criterion b).
    .OUTPUTS
        The version string (e.g., "1.2.3.4") or $null if unreadable.
    #>
    param([string]$ZipPath)

    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop
        $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
        try {
            $entry = $zip.Entries | Where-Object { $_.Name -eq 'solution.xml' } | Select-Object -First 1
            if (-not $entry) { return $null }
            $reader = New-Object System.IO.StreamReader($entry.Open())
            try {
                $xml = [xml]$reader.ReadToEnd()
            } finally {
                $reader.Close()
            }
            return $xml.ImportExportXml.SolutionManifest.Version
        } finally {
            $zip.Dispose()
        }
    }
    catch {
        Write-Warn "Could not read solution.xml version from $ZipPath — $($_.Exception.Message)"
        return $null
    }
}

function Get-InstalledSolutionVersion {
    <#
    .SYNOPSIS
        Queries pac solution list and returns the currently installed version
        for a specific solution unique name, or $null if not installed.
    #>
    param(
        [string]$SolutionName,
        [string]$Environment
    )

    $output = & $script:PacExe solution list --environment $Environment 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { return $null }

    $versionMatch = [regex]::Match($output, "$([regex]::Escape($SolutionName))\s+([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)")
    if ($versionMatch.Success) { return $versionMatch.Groups[1].Value }
    return $null
}

function Test-TierImport {
    <#
    .SYNOPSIS
        Verifies every solution in the just-imported tier is installed at the
        expected version (from its ZIP). Returns $true only if ALL match.
    .DESCRIPTION
        This is the gate that blocks Tier N+1 from starting until Tier N is
        confirmed successful (project constraint 4 + acceptance criterion c).
        Called AFTER all solutions in a tier have finished their import call.
    #>
    param(
        [array]$TierImports,        # array of hashtables: SolutionName, DisplayName, ExpectedVersion
        [string]$Environment
    )

    $allOk = $true
    foreach ($imp in $TierImports) {
        $installed = Get-InstalledSolutionVersion -SolutionName $imp.SolutionName -Environment $Environment
        if (-not $installed) {
            Write-Warn "$($imp.DisplayName) ($($imp.SolutionName)) NOT found in solution list after import."
            $allOk = $false
            continue
        }
        if ($imp.ExpectedVersion -and $installed -ne $imp.ExpectedVersion) {
            Write-Warn "$($imp.DisplayName): expected v$($imp.ExpectedVersion), found v$installed."
            $allOk = $false
            continue
        }
        $versionSuffix = if ($imp.ExpectedVersion) { "v$installed" } else { "v$installed (any-version verify)" }
        Write-Success "$($imp.DisplayName) verified at $versionSuffix."
    }
    return $allOk
}

function Import-ManagedSolution {
    <#
    .SYNOPSIS
        Imports a managed solution ZIP via pac CLI with mode-aware semantics.
    .DESCRIPTION
        In upgrade mode (IsUpgrade == $true AND effective Mode allows upgrade),
        invokes `pac solution import --stage-and-upgrade` which implements
        Microsoft Package Deployer's upgrade semantics: the new solution is
        staged alongside the installed one, the upgrade delta is applied, and
        the holding solution is retired (spec FR-09 acceptance).

        In fresh-install mode, invokes plain `pac solution import` with
        --publish-changes + --force-overwrite.
    #>
    param(
        [string]$ZipPath,
        [string]$DisplayName,
        [string]$Environment,
        [bool]$IsUpgrade,
        [string]$EffectiveMode      # 'FreshInstall' | 'Upgrade' | 'Auto'
    )

    # FreshInstall mode is a safety gate: fail early if the solution already
    # exists on the target env (prevents accidental double-install obscuring an
    # earlier install run).
    if ($EffectiveMode -eq 'FreshInstall' -and $IsUpgrade) {
        Write-Host ""
        Write-Host "  ERROR: -Mode FreshInstall specified but $DisplayName already exists." -ForegroundColor Red
        Write-Host "  Use -Mode Upgrade or -Mode Auto for existing environments." -ForegroundColor Yellow
        return $false
    }

    # Determine whether to invoke Package Deployer upgrade semantics.
    # Upgrade mode fires when: solution already exists AND effective mode is
    # Auto or Upgrade. This is spec FR-09 upgrade-mode → retire holding solution.
    $useStageAndUpgrade = $IsUpgrade -and ($EffectiveMode -eq 'Upgrade' -or $EffectiveMode -eq 'Auto')

    $action = if ($useStageAndUpgrade) { "Upgrading (Package Deployer stage-and-upgrade)" }
              elseif ($IsUpgrade)      { "Reinstalling" }
              else                     { "Importing (fresh)" }
    Write-Host "  $action $DisplayName..." -ForegroundColor Cyan
    Write-Detail "ZIP: $ZipPath"

    $importArgs = @(
        "solution", "import",
        "--path", $ZipPath,
        "--publish-changes",
        "--force-overwrite",
        "--environment", $Environment
    )

    if ($useStageAndUpgrade) {
        # --stage-and-upgrade: stage new solution, apply upgrade delta, retire
        # holding solution (Package Deployer upgrade semantics, spec FR-09).
        $importArgs += "--stage-and-upgrade"
    }

    $output = & $script:PacExe @importArgs 2>&1 | Out-String

    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "  FAILED: Import of $DisplayName failed (exit code $LASTEXITCODE)" -ForegroundColor Red
        Write-Host ""
        Write-Host "  PAC CLI output:" -ForegroundColor Yellow
        Write-Host $output
        Write-Host ""
        Write-Host "  Common causes:" -ForegroundColor Yellow
        Write-Host "    - Missing dependency: Ensure the prior tier imported successfully" -ForegroundColor White
        Write-Host "    - Version conflict: Target has newer version than source" -ForegroundColor White
        Write-Host "    - Auth expired: Re-authenticate with pac auth create" -ForegroundColor White
        Write-Host "    - Managed/unmanaged conflict: Remove unmanaged version first" -ForegroundColor White
        Write-Host "    - --stage-and-upgrade requires PAC CLI >= 1.10 (operator setup pins >= 1.35)" -ForegroundColor White
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
Write-Host "  Mode        : $Mode"
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

# ---- Step 3: Import solutions in dependency tiers (fail-fast) ---------------
#
# Tier-by-tier execution:
#   1. Import every solution in Tier N (calling Import-ManagedSolution).
#   2. If any Tier N import fails, EXIT NON-ZERO immediately — do NOT start
#      Tier N+1 (project constraint 5 + acceptance criterion c).
#   3. Verify (unless -SkipVerification) that every Tier N solution is installed
#      at the expected version from its ZIP. Verification failure = same
#      fail-fast semantics (blocks Tier N+1).
#   4. Only when all Tier N solutions are imported AND verified does Tier N+1
#      begin (project constraint 4 + acceptance criterion c).
#
# "Already at version" detection: if the installed version equals the ZIP's
# version AND we're in Auto/Upgrade mode, the import is still attempted; PAC
# handles the no-op idempotently. Acceptance criterion b is satisfied because
# the re-run exits 0 without doing destructive work.
# -----------------------------------------------------------------------------

Write-StepHeader $stepNum $totalSteps "Importing solutions in dependency tiers"
$stepNum++

$imported = @()
$failed = @()
$skipped = @()

# Group solutions by tier while preserving dependency ordering.
$solutionsByTier = [ordered]@{}
foreach ($entry in $solutionsToProcess.GetEnumerator()) {
    $tier = [int]$entry.Value.Tier
    if (-not $solutionsByTier.Contains($tier)) {
        $solutionsByTier[$tier] = @()
    }
    $solutionsByTier[$tier] += $entry
}

$sortedTierKeys = @($solutionsByTier.Keys | Sort-Object)
$importNum = 1
$importTotal = $solutionZips.Count

foreach ($tier in $sortedTierKeys) {
    Write-Host ""
    Write-Host "  ==== Tier $tier ====" -ForegroundColor Magenta

    # Per-tier bookkeeping — used by Test-TierImport for the gate below.
    $tierImports = @()
    $tierHadFailure = $false

    foreach ($entry in $solutionsByTier[$tier]) {
        $folderName = $entry.Key
        $solInfo = $entry.Value

        if (-not $solutionZips.Contains($folderName)) {
            $skipped += $folderName
            Write-Warn "  Skipping $($solInfo.DisplayName) — no ZIP found."
            continue
        }

        $zipPath = $solutionZips[$folderName]
        $isUpgrade = $existingSolutions.ContainsKey($folderName)
        $expectedVersion = Get-SolutionVersionFromZip -ZipPath $zipPath

        # "Already at version" pre-check for acceptance criterion b.
        if ($isUpgrade -and $expectedVersion) {
            $installedVersion = $existingSolutions[$folderName]
            if ($installedVersion -eq $expectedVersion) {
                Write-Detail "  $($solInfo.DisplayName) already at v$expectedVersion — skipping import call."
                $imported += $folderName
                $tierImports += @{
                    SolutionName = $solInfo.SolutionName
                    DisplayName = $solInfo.DisplayName
                    ExpectedVersion = $expectedVersion
                }
                $importNum++
                continue
            }
        }

        Write-Host ""
        Write-Host "  --- [$importNum/$importTotal] $($solInfo.DisplayName) (Tier $tier)$(if ($expectedVersion) { " → v$expectedVersion" }) ---" -ForegroundColor Cyan

        $success = Import-ManagedSolution `
            -ZipPath $zipPath `
            -DisplayName $solInfo.DisplayName `
            -Environment $EnvironmentUrl `
            -IsUpgrade $isUpgrade `
            -EffectiveMode $Mode

        if ($success) {
            $imported += $folderName
            $tierImports += @{
                SolutionName = $solInfo.SolutionName
                DisplayName = $solInfo.DisplayName
                ExpectedVersion = $expectedVersion
            }
        } else {
            $failed += $folderName
            $tierHadFailure = $true
        }

        $importNum++
    }

    # ---- Fail-fast gate: block Tier N+1 on ANY Tier N failure ----
    if ($tierHadFailure) {
        Write-Host ""
        Write-Host "  CRITICAL: Tier $tier had import failure(s): $($failed -join ', ')" -ForegroundColor Red
        Write-Host "  Aborting BEFORE attempting subsequent tiers." -ForegroundColor Red
        Write-Host "  (Package Deployer dependency-order guarantee — subsequent tiers depend on this one.)" -ForegroundColor Red
        break
    }

    # ---- Version-verification gate: block Tier N+1 unless every Tier N ----
    # ---- solution is installed at the expected version.               ----
    if (-not $SkipVerification -and $tierImports.Count -gt 0) {
        Write-Host ""
        Write-Detail "Verifying Tier $tier solutions before proceeding..."
        $tierVerified = Test-TierImport -TierImports $tierImports -Environment $EnvironmentUrl
        if (-not $tierVerified) {
            Write-Host ""
            Write-Host "  CRITICAL: Tier $tier verification failed. Aborting BEFORE Tier $($tier + 1)." -ForegroundColor Red
            Write-Host "  Subsequent tiers cannot proceed without a verified base tier." -ForegroundColor Red
            # Mark verification failure by promoting affected imports to failed.
            $failed += ($tierImports | ForEach-Object { $_.SolutionName })
            break
        }
    }
}

Write-Host ""

# ---- Step 4: Final rollup verification --------------------------------------
# Per-tier verification (Test-TierImport) already ran inline during Step 3 as
# the gate that blocks Tier N+1. This step is a final rollup for the summary —
# lightweight and non-gating (the fail-fast decision has already been made).

if (-not $SkipVerification -and $imported.Count -gt 0) {
    Write-StepHeader $stepNum $totalSteps "Final rollup: verifying all imported solutions"
    $stepNum++

    Write-Detail "Querying solution list from environment..."
    $verifyOutput = & $script:PacExe solution list --environment $EnvironmentUrl 2>&1 | Out-String

    if ($LASTEXITCODE -ne 0) {
        Write-Warn "Could not query solution list for final rollup."
        Write-Host "  Run manually: pac solution list --environment $EnvironmentUrl" -ForegroundColor Gray
    } else {
        $verified = 0
        $verifyFailed = 0

        foreach ($folderName in $imported) {
            $solName = $solutionsToProcess[$folderName].SolutionName
            $displayName = $solutionsToProcess[$folderName].DisplayName

            if ($verifyOutput -match [regex]::Escape($solName)) {
                $versionMatch = [regex]::Match($verifyOutput, "$([regex]::Escape($solName))\s+([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)")
                $version = if ($versionMatch.Success) { $versionMatch.Groups[1].Value } else { "detected" }
                Write-Success "$displayName v$version rollup-verified."
                $verified++
            } else {
                Write-Warn "$displayName ($solName) not found in solution list. Import may still be processing."
                $verifyFailed++
            }
        }

        Write-Host ""
        Write-Host "  Rollup: $verified verified, $verifyFailed not yet confirmed" -ForegroundColor $(if ($verifyFailed -eq 0) { "Green" } else { "Yellow" })
    }
} else {
    Write-StepHeader $stepNum $totalSteps "Verification skipped"
    $stepNum++

    if ($SkipVerification) {
        Write-Detail "Skipped by -SkipVerification flag (per-tier gates also skipped)."
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
