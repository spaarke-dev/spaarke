<#
.SYNOPSIS
    Drift detection for the Spaarke package (READ-ONLY): does SpaarkeMaster in the authoring environment hold
    exactly what the package scope rule says it should?

.DESCRIPTION
    T218c (ADR-027 §3 amended 2026-10-07). Reports, and fails on:
      1. MISSING FROM PACKAGE  — in scope by rule (docs/data-model/package-scope.json), not excluded, not in
                                 SpaarkeMaster. (First run 2026-10-07: 44, e.g. 11 PCFs and the AI Setup app.)
      2. EXCLUDED BUT PACKAGED — listed as excluded, yet SpaarkeMaster holds it.
      3. STALE EXCLUSION       — an exclusion that matches nothing in the environment.
      4. OOB COLUMN UNLISTED   — a sprk_ column on an OOB table that docs/data-model/oob-customizations.yaml does not
                                 list (governance rule 4: listed in the same PR that adds it).
      5. INVENTORY DRIFT       — the committed docs/data-model/spaarke-components-inventory.json differs from a fresh
                                 Get-SpaarkeComponents.ps1 run (skipped when -SkipInventory).

    Exit 0 = clean; 1 = drift (with -FailOnDrift, the default).

.EXAMPLE
    ./Test-SolutionCompleteness.ps1
.EXAMPLE
    ./Test-SolutionCompleteness.ps1 -FailOnDrift:$false -SkipInventory   # report only, rule checks only
#>

[CmdletBinding()]
param(
    [string]$EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
    [string]$MasterSolutionUniqueName = 'SpaarkeMaster',
    [string]$ScopePath = "$PSScriptRoot/../../docs/data-model/package-scope.json",
    [string]$OobManifestPath = "$PSScriptRoot/../../docs/data-model/oob-customizations.yaml",
    [string]$InventoryPath = "$PSScriptRoot/../../docs/data-model/spaarke-components-inventory.json",
    [switch]$SkipInventory,
    [bool]$FailOnDrift = $true
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SpaarkePackageScope.psm1') -Force
$scope = Read-PackageScope -Path $ScopePath

Write-Host "==> Acquiring access token for $EnvironmentUrl" -ForegroundColor Cyan
$tokenJson = az account get-access-token --resource $EnvironmentUrl 2>$null
if ($LASTEXITCODE -ne 0 -or -not $tokenJson) { throw "Failed to acquire an az access token for $EnvironmentUrl. Run 'az login' first." }
$headers = @{
    Authorization      = "Bearer $(($tokenJson | ConvertFrom-Json).accessToken)"
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    Accept             = 'application/json'
}
$get = {
    param($endpoint)
    $uri = if ($endpoint -match '^https://') { $endpoint } else { "$EnvironmentUrl/api/data/v9.2/$endpoint" }
    Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
}.GetNewClosure()

# ---- 1-3. Rule vs SpaarkeMaster membership ------------------------------------
Write-Host '==> Applying the package scope rule' -ForegroundColor Cyan
$rule = @(Get-PackageRuleComponents -Get $get -Scope $scope)
$membership = Get-SolutionMembershipKeys -Get $get -SolutionUniqueName $MasterSolutionUniqueName
$diff = Compare-PackageScope -RuleComponents $rule -MembershipKeys $membership -Scope $scope
Write-Host "    In scope: $($rule.Count) (excluded: $(@($rule | Where-Object Excluded).Count)); $MasterSolutionUniqueName components: $($membership.Count)"

# ---- 4. OOB columns listed in oob-customizations.yaml --------------------------
$listed = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
if (Test-Path $OobManifestPath) {
    $entity = $null
    foreach ($line in (Get-Content $OobManifestPath)) {
        if ($line -match '^\s{2}([a-z_]+):\s*$') { $entity = $Matches[1] }
        elseif ($entity -and $line -match '^\s+-\s+(sprk_\w+)') { [void]$listed.Add("$entity.$($Matches[1])") }
    }
}
$oobUnlisted = @($rule | Where-Object { $_.TypeName -eq 'Attribute' -and -not $listed.Contains($_.Name) })

# ---- 5. Committed inventory vs fresh run --------------------------------------
$invAdded = @(); $invRemoved = @()
if (-not $SkipInventory -and (Test-Path $InventoryPath)) {
    $tempPath = Join-Path ([IO.Path]::GetTempPath()) "spaarke-inventory-live-$(Get-Random).json"
    & "$PSScriptRoot/Get-SpaarkeComponents.ps1" -EnvironmentUrl $EnvironmentUrl -OutputPath $tempPath | Out-Null
    $committed = Get-Content $InventoryPath -Raw | ConvertFrom-Json
    $live = Get-Content $tempPath -Raw | ConvertFrom-Json
    Remove-Item $tempPath -Force -ErrorAction SilentlyContinue
    $c = @{}; foreach ($x in $committed.distinctComponents) { $c["$($x.ComponentType)|$($x.ObjectId)"] = $x }
    $l = @{}; foreach ($x in $live.distinctComponents) { $l["$($x.ComponentType)|$($x.ObjectId)"] = $x }
    $invAdded = @($l.Keys | Where-Object { -not $c.ContainsKey($_) } | ForEach-Object { $l[$_] })
    $invRemoved = @($c.Keys | Where-Object { -not $l.ContainsKey($_) } | ForEach-Object { $c[$_] })
}

# ---- Report -------------------------------------------------------------------
function Write-Section([string]$Title, $Items, [scriptblock]$Format) {
    $list = @($Items | Where-Object { $null -ne $_ })   # an empty pipeline result is $null, not an item
    if ($list.Count -eq 0) { return }
    Write-Host "    $Title : $($list.Count)" -ForegroundColor Yellow
    $list | ForEach-Object { Write-Host "      $(& $Format $_)" -ForegroundColor Yellow }
}

Write-Host ''
Write-Host '==> DRIFT REPORT' -ForegroundColor Cyan
Write-Section 'MISSING FROM PACKAGE (add with Assemble-SpaarkeMasterSolution.ps1, or exclude with a reason)' ($diff.MissingFromPackage | Sort-Object TypeName, Name) { "+ $($args[0].TypeName) $($args[0].Name)" }
Write-Section 'EXCLUDED BUT PACKAGED' ($diff.ExcludedButInPackage) { "! $($args[0].TypeName) $($args[0].Name) — $($args[0].Reason)" }
Write-Section 'STALE EXCLUSION (matches nothing — remove it)' ($diff.UnmatchedExclusions) { "~ $($args[0].type) $($args[0].name)" }
Write-Section 'OOB COLUMN NOT LISTED in oob-customizations.yaml' $oobUnlisted { "? $($args[0].Name)" }
Write-Section 'INVENTORY: new in dev (refresh with Get-SpaarkeComponents.ps1)' ($invAdded | Group-Object ComponentTypeName) { "+ $($args[0].Name) $($args[0].Count)" }
Write-Section 'INVENTORY: gone from dev (investigate)' ($invRemoved | Group-Object ComponentTypeName) { "- $($args[0].Name) $($args[0].Count)" }

$hasDrift = (@($diff.MissingFromPackage).Count + @($diff.ExcludedButInPackage).Count + @($diff.UnmatchedExclusions).Count +
    $oobUnlisted.Count + $invAdded.Count + $invRemoved.Count) -gt 0
if (-not $hasDrift) { Write-Host '    No drift.' -ForegroundColor Green }
Write-Host ''
if ($hasDrift -and $FailOnDrift) { Write-Host '==> EXIT 1 (drift)' -ForegroundColor Red; exit 1 }
Write-Host '==> EXIT 0' -ForegroundColor Green
exit 0
