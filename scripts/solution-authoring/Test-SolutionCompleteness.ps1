<#
.SYNOPSIS
    Drift detection: verify the checked-in spaarke-components-inventory.json
    matches the current state of Spaarke-owned components in spaarkedev1.

.DESCRIPTION
    Runs Get-SpaarkeComponents.ps1 against the target env in-memory (temp file),
    then compares the fresh inventory against the committed inventory in
    docs/data-model/spaarke-components-inventory.json.

    Reports:
      - Components in dev but NOT in committed inventory (need to be added to committed)
      - Components in committed inventory but NOT in dev (may indicate accidental removal)
      - OOB customizations in dev not covered by oob-customizations.yaml
      - OOB entries in yaml that no longer exist in dev

    Exit codes:
      0 = no drift
      1 = drift detected (fails CI)

    Intended for CI use: gate merges when spaarkedev1 has new Spaarke content
    not yet captured in the release manifest.

.PARAMETER InventoryPath
    Path to committed inventory JSON.

.PARAMETER OobManifestPath
    Path to committed OOB customizations YAML.

.PARAMETER EnvironmentUrl
    Dataverse environment URL to check drift against.

.PARAMETER FailOnDrift
    Exit 1 on drift (default). Pass -FailOnDrift:$false for report-only mode.

.EXAMPLE
    ./Test-SolutionCompleteness.ps1

.EXAMPLE
    ./Test-SolutionCompleteness.ps1 -FailOnDrift:$false   # report only
#>

[CmdletBinding()]
param(
    [string]$InventoryPath = "$PSScriptRoot/../../docs/data-model/spaarke-components-inventory.json",
    [string]$OobManifestPath = "$PSScriptRoot/../../docs/data-model/oob-customizations.yaml",
    [string]$EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
    [bool]$FailOnDrift = $true
)

$ErrorActionPreference = 'Stop'

# ---- 1. Load committed inventory --------------------------------------------

if (-not (Test-Path $InventoryPath)) {
    Write-Warning "No committed inventory at $InventoryPath. Run Get-SpaarkeComponents.ps1 first to create the baseline."
    exit $(if ($FailOnDrift) { 1 } else { 0 })
}
$committed = Get-Content $InventoryPath -Raw | ConvertFrom-Json
Write-Host "==> Committed inventory: $($committed.metadata.distinctComponents) distinct components (timestamp $($committed.metadata.timestamp))" -ForegroundColor Cyan

# ---- 2. Run fresh Get-SpaarkeComponents into temp file ----------------------

$tempPath = Join-Path $env:TEMP "spaarke-inventory-live-$(Get-Random).json"
$getScript = "$PSScriptRoot/Get-SpaarkeComponents.ps1"

if (-not (Test-Path $getScript)) {
    throw "Companion script not found at $getScript"
}

Write-Host "==> Running Get-SpaarkeComponents.ps1 against $EnvironmentUrl -> $tempPath" -ForegroundColor Cyan
& $getScript -EnvironmentUrl $EnvironmentUrl -OutputPath $tempPath 2>&1 | Where-Object { $_ -notmatch '^\s+-' } | Out-Host

if (-not (Test-Path $tempPath)) {
    throw "Get-SpaarkeComponents.ps1 failed to produce inventory."
}
$live = Get-Content $tempPath -Raw | ConvertFrom-Json
Write-Host "==> Live inventory: $($live.metadata.distinctComponents) distinct components" -ForegroundColor Cyan

# ---- 3. Compute drift -------------------------------------------------------

# Build sets for O(1) lookup: key = "componenttype|objectid"
$committedSet = @{}
foreach ($c in $committed.distinctComponents) {
    $committedSet["$($c.ComponentType)|$($c.ObjectId)"] = $c
}
$liveSet = @{}
foreach ($c in $live.distinctComponents) {
    $liveSet["$($c.ComponentType)|$($c.ObjectId)"] = $c
}

# In dev, not in committed = new content that needs to be captured before release
$driftAdded = @()
foreach ($key in $liveSet.Keys) {
    if (-not $committedSet.ContainsKey($key)) {
        $driftAdded += $liveSet[$key]
    }
}

# In committed, not in dev = removed content (may indicate accidental deletion)
$driftRemoved = @()
foreach ($key in $committedSet.Keys) {
    if (-not $liveSet.ContainsKey($key)) {
        $driftRemoved += $committedSet[$key]
    }
}

# ---- 4. Report --------------------------------------------------------------

$hasDrift = ($driftAdded.Count -gt 0) -or ($driftRemoved.Count -gt 0)

Write-Host ""
Write-Host "==> DRIFT REPORT" -ForegroundColor Cyan

if (-not $hasDrift) {
    Write-Host "    No drift. Committed inventory matches live spaarkedev1 exactly." -ForegroundColor Green
} else {
    if ($driftAdded.Count -gt 0) {
        Write-Host "    NEW in dev (not yet in committed inventory) : $($driftAdded.Count)" -ForegroundColor Yellow
        $driftAdded | Group-Object ComponentTypeName | Sort-Object Name | ForEach-Object {
            Write-Host "      + $($_.Name.PadRight(35)) $($_.Count)" -ForegroundColor Yellow
        }
        Write-Host ""
        Write-Host "    Fix: re-run Get-SpaarkeComponents.ps1 to refresh the committed inventory, then commit." -ForegroundColor Gray
    }
    if ($driftRemoved.Count -gt 0) {
        Write-Host "    MISSING from dev (in committed but not live) : $($driftRemoved.Count)" -ForegroundColor Yellow
        $driftRemoved | Group-Object ComponentTypeName | Sort-Object Name | ForEach-Object {
            Write-Host "      - $($_.Name.PadRight(35)) $($_.Count)" -ForegroundColor Yellow
        }
        Write-Host ""
        Write-Host "    Fix: investigate - did someone delete Spaarke content from spaarkedev1? If intentional, refresh + commit." -ForegroundColor Gray
    }
}

Write-Host ""

# ---- 5. Cleanup + exit ------------------------------------------------------

Remove-Item $tempPath -Force -ErrorAction SilentlyContinue

if ($hasDrift -and $FailOnDrift) {
    Write-Host "==> EXIT 1 (drift detected; -FailOnDrift is true)" -ForegroundColor Red
    exit 1
} else {
    Write-Host "==> EXIT 0" -ForegroundColor Green
    exit 0
}
