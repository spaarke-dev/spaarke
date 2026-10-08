<#
.SYNOPSIS
    Assemble SpaarkeMaster in the authoring environment: add every component the package scope rule says belongs in
    it but is missing, then bump the version. WRITES to the authoring environment — the release owner runs it.

.DESCRIPTION
    T218c (ADR-027 §3 amended 2026-10-07). The scope is a RULE (scripts/solution-authoring/SpaarkePackageScope.psm1 +
    docs/data-model/package-scope.json): every unmanaged sprk_ component of the scoped types, sprk_ columns on OOB
    tables, unmanaged views/forms on OOB tables, unmanaged dashboards, field security profiles and Spaarke roles of
    the ROOT business unit, minus committed exclusions. Before T218c this script added only what was
    already inside some Spaarke solution; the first rule run (2026-10-07) found 44 in-scope components missing.

    Flags (T218c review): every custom table goes in WITH all its subcomponents (DoNotIncludeSubcomponents = false) -
    the F12 root cause (lessons-learned-model1-prod-standup-2026-08-22.md) was tables added without their columns,
    forms and ribbons, so the managed export referenced them as external customizations. AddRequiredComponents is
    FALSE: with true, the 2026-08-23 rebuild dragged five Microsoft tables (environmentvariabledefinition/value,
    msdyn_aimodelcatalog, msdyn_analysisoverride, msdyn_analysisresultdetail) into SpaarkeMaster whole. Dependencies
    on Microsoft components are platform dependencies of the import; dependencies on Spaarke components are in scope
    by rule. An OOB table whose sprk_ column, view or form is added goes in as a SHELL first (DoNotIncludeSubcomponents
    + empty IncludedComponentSettingsValues - rootcomponentbehavior 2), so Spaarke never ships Microsoft's table metadata. A sprk_ table packaged as a shell (behavior != 0) is re-added with all subcomponents.

    -RemoveUnexplained (T218e) also removes from the solution what the rule does not explain: components packaged
    OUTSIDE THE RULE (e.g. Microsoft tables dragged in as dependencies, environment-variable VALUES) and components
    EXCLUDED BUT PACKAGED. RemoveSolutionComponent only takes a component out of the solution; it deletes nothing from
    the environment. Without the switch these are reported, never removed.

    Export is a separate step: Export-SpaarkeMasterSource.ps1.

.PARAMETER WhatIf
    Report what would be added (and removed, with -RemoveUnexplained) and the new version; change nothing.

.PARAMETER Version
    Set this exact version instead of a -VersionBumpKind bump. It must be higher than the current version (H6 refuses
    downgrades; v1.1.0.0 was exported on 2026-08-21 although the authoring copy later read 1.0.0.0).

.PARAMETER RemoveUnexplained
    Remove components packaged outside the rule and excluded components from the solution (owner-approved run).

.EXAMPLE
    ./Assemble-SpaarkeMasterSolution.ps1 -WhatIf
.EXAMPLE
    ./Assemble-SpaarkeMasterSolution.ps1 -VersionBumpKind Minor
.EXAMPLE
    ./Assemble-SpaarkeMasterSolution.ps1 -RemoveUnexplained -WhatIf
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
    [string]$MasterSolutionUniqueName = 'SpaarkeMaster',
    [string]$ScopePath = "$PSScriptRoot/../../docs/data-model/package-scope.json",
    [ValidateSet('Build', 'Revision', 'Minor', 'Major')]
    [string]$VersionBumpKind = 'Build',
    [string]$Version,
    [switch]$RemoveUnexplained
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
$shells = @($diff.PackagedAsShell)

Write-Host "    In scope: $($rule.Count) (excluded: $(@($rule | Where-Object Excluded).Count)) ; already in $MasterSolutionUniqueName : $($rule.Count - $toAdd.Count - @($rule | Where-Object Excluded).Count)"
foreach ($g in ($toAdd | Group-Object TypeName | Sort-Object Name)) { Write-Host ("    +{0,-32} {1,5}" -f $g.Name, $g.Count) -ForegroundColor Yellow }
foreach ($x in $shells) { Write-Host "    ~ shell -> full: $($x.Name)" -ForegroundColor Yellow }
# What the rule does not explain: removed only with -RemoveUnexplained, otherwise reported.
$toRemove = [System.Collections.Generic.List[object]]::new()
foreach ($x in $diff.ExcludedButInPackage) {
    $toRemove.Add([PSCustomObject]@{ ComponentType = $x.ComponentType; ObjectId = $x.ObjectId; Label = "excluded $($x.TypeName) $($x.Name)" })
}
if (@($diff.OutsideRule).Count -gt 0) {
    $entityNames = Get-EntityNameMap -Get $get
    foreach ($x in $diff.OutsideRule) {
        $label = if ($x.ComponentType -eq 1 -and $entityNames.ContainsKey($x.ObjectId)) { "table $($entityNames[$x.ObjectId])" } else { "type $($x.ComponentType) $($x.ObjectId)" }
        $toRemove.Add([PSCustomObject]@{ ComponentType = $x.ComponentType; ObjectId = $x.ObjectId; Label = "outside the rule: $label" })
    }
}
if (-not $RemoveUnexplained) {
    foreach ($x in $toRemove) { Write-Warning "Not explained by the rule: $($x.Label) - re-run with -RemoveUnexplained (owner-approved) or change the rule." }
    $toRemove.Clear()
}
foreach ($x in $diff.UnmatchedExclusions) { Write-Warning "Stale exclusion (matches nothing): $($x.type) $($x.name)" }
foreach ($x in $diff.UnmatchedAlsoIncluded) { Write-Warning "roleNamesAlsoIncluded matches no root role: $x" }

if ($toAdd.Count -eq 0 -and $shells.Count -eq 0 -and $toRemove.Count -eq 0 -and -not $Version) { Write-Host "==> Nothing to change. $MasterSolutionUniqueName is complete." -ForegroundColor Green; exit 0 }

$newVersion = Get-NextPackageVersion -Current $master.version -Kind $VersionBumpKind -Version $Version

if ($WhatIfPreference) {
    $toAdd | Sort-Object TypeName, Name | ForEach-Object { Write-Host "    WOULD ADD: $($_.TypeName) $($_.Name)" -ForegroundColor DarkYellow }
    $shells | ForEach-Object { Write-Host "    WOULD RE-ADD WITH SUBCOMPONENTS: $($_.Name)" -ForegroundColor DarkYellow }
    $toRemove | ForEach-Object { Write-Host "    WOULD REMOVE FROM THE SOLUTION: $($_.Label)" -ForegroundColor DarkYellow }
    Write-Host "    WOULD SET VERSION: $($master.version) -> $newVersion" -ForegroundColor DarkYellow
    exit 0
}

# ---- Apply --------------------------------------------------------------------
function Add-Component([string]$Id, [int]$Type, [bool]$Required, [bool]$NoSubcomponents, [switch]$Shell) {
    $body = @{
        ComponentId               = $Id
        ComponentType             = $Type
        SolutionUniqueName        = $MasterSolutionUniqueName
        AddRequiredComponents     = $Required
        DoNotIncludeSubcomponents = $NoSubcomponents
    }
    if ($Shell) { $body.IncludedComponentSettingsValues = @() }   # with DoNotIncludeSubcomponents: shell only (behavior 2)
    Invoke-DataverseWrite 'AddSolutionComponent' 'POST' $body | Out-Null
}

$failed = @()
foreach ($x in $toRemove) {
    try {
        # SolutionComponent is a reference whose id is the COMPONENT's id (objectid), not the solutioncomponent row id
        # (verified live 2026-10-08: the row id fails with 0x8004f021 "Cannot find solution component").
        Invoke-DataverseWrite 'RemoveSolutionComponent' 'POST' @{
            SolutionComponent  = @{ '@odata.type' = 'Microsoft.Dynamics.CRM.solutioncomponent'; solutioncomponentid = $x.ObjectId }
            ComponentType      = $x.ComponentType
            SolutionUniqueName = $MasterSolutionUniqueName
        } | Out-Null
        Write-Host "    - $($x.Label)" -ForegroundColor Green
    } catch {
        $failed += $x
        Write-Warning "    FAILED to remove: $($x.Label) : $($_.Exception.Message)"
    }
}

$addedTables = @{}
foreach ($item in (@($toAdd) + @($shells) | Sort-Object ComponentType, Name)) {
    try {
        if ($item.TypeName -ne 'Entity' -and $item.ParentEntityId -and -not $membership.ContainsKey("1|$($item.ParentEntityId)") -and -not $addedTables.ContainsKey($item.ParentEntityId)) {
            Add-Component $item.ParentEntityId 1 $false $true -Shell   # OOB table, shell only
            $addedTables[$item.ParentEntityId] = $true
        }
        Add-Component $item.ObjectId $item.ComponentType $false $false   # with subcomponents, no dependencies
        Write-Host "    + $($item.TypeName) $($item.Name)" -ForegroundColor Green
    } catch {
        $failed += $item
        Write-Warning "    FAILED: $($item.TypeName) $($item.Name) : $($_.Exception.Message)"
    }
}

if ($failed.Count -gt 0) {
    Write-Error "$($failed.Count) component(s) failed to add or remove - version NOT bumped. Fix and re-run (idempotent), then Test-SolutionCompleteness.ps1."
    exit 1
}

# Verify before bumping: re-read membership; every in-scope table must now be packaged WITH its subcomponents.
$after = Compare-PackageScope -RuleComponents $rule -MembershipKeys (Get-SolutionMembershipKeys -Get $get -SolutionUniqueName $MasterSolutionUniqueName) -Scope $scope
$stillUnexplained = if ($RemoveUnexplained) { @($after.ExcludedButInPackage).Count + @($after.OutsideRule).Count } else { 0 }
if (@($after.MissingFromPackage).Count -gt 0 -or @($after.PackagedAsShell).Count -gt 0 -or $stillUnexplained -gt 0) {
    @($after.MissingFromPackage) + @($after.PackagedAsShell) | ForEach-Object { Write-Warning "Still not packaged in full: $($_.TypeName) $($_.Name)" }
    if ($stillUnexplained -gt 0) { Write-Warning "$stillUnexplained unexplained component(s) are still in the solution after removal." }
    Write-Error "The adds did not take effect for every component - version NOT bumped. Investigate (a shell may need removing and re-adding), then re-run."
    exit 1
}
Invoke-DataverseWrite "solutions($($master.solutionid))" 'PATCH' @{ version = $newVersion } | Out-Null
Write-Host "==> $MasterSolutionUniqueName $($master.version) -> $newVersion ; added $($toAdd.Count), re-added $($shells.Count) with subcomponents, removed $($toRemove.Count)" -ForegroundColor Green
Write-Host '    Next: Test-SolutionCompleteness.ps1 (must exit 0), then Export-SpaarkeMasterSource.ps1.'
