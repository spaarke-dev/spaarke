<#
.SYNOPSIS
    Assemble SpaarkeMaster in the authoring environment: add every component the package scope rule says belongs in
    it but is missing, then bump the version. WRITES to the authoring environment — the release owner runs it.

.DESCRIPTION
    T218c (ADR-027 §3 amended 2026-10-07). The scope is a RULE (scripts/solution-authoring/SpaarkePackageScope.psm1 +
    docs/data-model/package-scope.json): every unmanaged sprk_ component of the scoped types, sprk_ columns on OOB
    tables and the Spaarke root-unit roles, minus committed exclusions. Before T218c this script added only what was
    already inside some Spaarke solution; the first rule run (2026-10-07) found 44 in-scope components missing.

    Components are added with AddRequiredComponents = true (F12, lessons-learned-model1-prod-standup-2026-08-22.md:
    with false, the managed export referenced its own sub-components as external customizations and would not install
    in a fresh environment). An OOB table whose sprk_ column is added goes in METADATA-ONLY first
    (DoNotIncludeSubcomponents), so Spaarke never ships Microsoft's table customizations.

    Export is a separate step: Export-SpaarkeMasterSource.ps1.

.PARAMETER WhatIf
    Report what would be added and the new version; change nothing.

.EXAMPLE
    ./Assemble-SpaarkeMasterSolution.ps1 -WhatIf
.EXAMPLE
    ./Assemble-SpaarkeMasterSolution.ps1 -VersionBumpKind Minor
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
    [string]$MasterSolutionUniqueName = 'SpaarkeMaster',
    [string]$ScopePath = "$PSScriptRoot/../../docs/data-model/package-scope.json",
    [ValidateSet('Build', 'Revision', 'Minor', 'Major')]
    [string]$VersionBumpKind = 'Build'
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
    'Content-Type'     = 'application/json'
}
$get = {
    param($endpoint)
    $uri = if ($endpoint -match '^https://') { $endpoint } else { "$EnvironmentUrl/api/data/v9.2/$endpoint" }
    Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
}.GetNewClosure()

function Invoke-DataverseWrite([string]$Endpoint, [string]$Method, $Body) {
    Invoke-RestMethod -Uri "$EnvironmentUrl/api/data/v9.2/$Endpoint" -Headers $headers -Method $Method -Body ($Body | ConvertTo-Json -Depth 5)
}

# ---- Target solution ----------------------------------------------------------
$master = @((& $get "solutions?`$select=solutionid,uniquename,version,ismanaged&`$filter=uniquename eq '$MasterSolutionUniqueName'").value)
if ($master.Count -eq 0) { throw "$MasterSolutionUniqueName not found in $EnvironmentUrl." }
$master = $master[0]
if ($master.ismanaged) { throw "$MasterSolutionUniqueName is MANAGED here — the authoring environment must hold it unmanaged." }
Write-Host "    $($master.uniquename) v$($master.version)" -ForegroundColor Green

# ---- Rule vs membership --------------------------------------------------------
Write-Host '==> Applying the package scope rule' -ForegroundColor Cyan
$rule = @(Get-PackageRuleComponents -Get $get -Scope $scope)
$membership = Get-SolutionMembershipKeys -Get $get -SolutionUniqueName $MasterSolutionUniqueName
$diff = Compare-PackageScope -RuleComponents $rule -MembershipKeys $membership -Scope $scope
$toAdd = @($diff.MissingFromPackage)

Write-Host "    In scope: $($rule.Count) (excluded: $(@($rule | Where-Object Excluded).Count)) ; already in $MasterSolutionUniqueName : $($rule.Count - $toAdd.Count - @($rule | Where-Object Excluded).Count)"
foreach ($g in ($toAdd | Group-Object TypeName | Sort-Object Name)) { Write-Host ("    +{0,-32} {1,5}" -f $g.Name, $g.Count) -ForegroundColor Yellow }
foreach ($x in $diff.ExcludedButInPackage) { Write-Warning "Excluded but packaged: $($x.TypeName) $($x.Name) — remove it from $MasterSolutionUniqueName or drop the exclusion." }
foreach ($x in $diff.UnmatchedExclusions) { Write-Warning "Stale exclusion (matches nothing): $($x.type) $($x.name)" }

if ($toAdd.Count -eq 0) { Write-Host "==> Nothing to add. $MasterSolutionUniqueName is complete." -ForegroundColor Green; exit 0 }

$newVersion = & {
    $parts = @(($master.version -split '\.') + @('0', '0', '0', '0'))[0..3] | ForEach-Object { [int]$_ }
    switch ($VersionBumpKind) {
        'Major' { $parts = @($parts[0] + 1, 0, 0, 0) }
        'Minor' { $parts = @($parts[0], $parts[1] + 1, 0, 0) }
        'Build' { $parts = @($parts[0], $parts[1], $parts[2] + 1, 0) }
        'Revision' { $parts = @($parts[0], $parts[1], $parts[2], $parts[3] + 1) }
    }
    $parts -join '.'
}

if ($WhatIfPreference) {
    $toAdd | Sort-Object TypeName, Name | ForEach-Object { Write-Host "    WOULD ADD: $($_.TypeName) $($_.Name)" -ForegroundColor DarkYellow }
    Write-Host "    WOULD BUMP: $($master.version) -> $newVersion ($VersionBumpKind)" -ForegroundColor DarkYellow
    exit 0
}

# ---- Apply --------------------------------------------------------------------
function Add-Component([string]$Id, [int]$Type, [bool]$Required, [bool]$NoSubcomponents) {
    Invoke-DataverseWrite 'AddSolutionComponent' 'POST' @{
        ComponentId               = $Id
        ComponentType             = $Type
        SolutionUniqueName        = $MasterSolutionUniqueName
        AddRequiredComponents     = $Required
        DoNotIncludeSubcomponents = $NoSubcomponents
    } | Out-Null
}

$failed = @()
$addedTables = @{}
foreach ($item in ($toAdd | Sort-Object ComponentType, Name)) {
    try {
        if ($item.TypeName -eq 'Attribute' -and $item.ParentEntityId -and -not $membership.Contains("1|$($item.ParentEntityId)") -and -not $addedTables.ContainsKey($item.ParentEntityId)) {
            Add-Component $item.ParentEntityId 1 $false $true      # OOB table, metadata-only
            $addedTables[$item.ParentEntityId] = $true
        }
        Add-Component $item.ObjectId $item.ComponentType $true $false
        Write-Host "    + $($item.TypeName) $($item.Name)" -ForegroundColor Green
    } catch {
        $failed += $item
        Write-Warning "    FAILED: $($item.TypeName) $($item.Name) : $($_.Exception.Message)"
    }
}

Invoke-DataverseWrite "solutions($($master.solutionid))" 'PATCH' @{ version = $newVersion } | Out-Null
Write-Host "==> $MasterSolutionUniqueName $($master.version) -> $newVersion ; added $($toAdd.Count - $failed.Count) / $($toAdd.Count)" -ForegroundColor Green
if ($failed.Count -gt 0) { Write-Error "$($failed.Count) component(s) failed to add — see warnings. Run Test-SolutionCompleteness.ps1 after fixing." }
Write-Host '    Next: Test-SolutionCompleteness.ps1 (must exit 0), then Export-SpaarkeMasterSource.ps1.'
