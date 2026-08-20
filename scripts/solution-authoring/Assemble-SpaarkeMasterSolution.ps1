<#
.SYNOPSIS
    Assemble the Spaarke Master solution: augment existing SpaarkeMaster with missing
    components identified by Get-SpaarkeComponents.ps1 + OOB customizations manifest.
    Bump version and export as a managed ZIP for customer provisioning.

.DESCRIPTION
    Reads the inventory JSON produced by Get-SpaarkeComponents.ps1 + the OOB
    customizations manifest at docs/data-model/oob-customizations.yaml. For each
    component (or OOB attribute) that is NOT already in the target SpaarkeMaster
    solution, adds it via the Dataverse Web API AddSolutionComponent action.

    IDEMPOTENT: safe to re-run. Existing components are skipped; only NEW ones are
    added. Version bump and export happen only if any component was added.

    OUTPUT: managed solution ZIP at the specified path, ready to be consumed by
    the H6 solution import handler in customer-provisioning-orchestration-r1.

.PARAMETER InventoryPath
    Path to spaarke-components-inventory.json produced by Get-SpaarkeComponents.ps1.

.PARAMETER OobManifestPath
    Path to oob-customizations.yaml manifest.

.PARAMETER EnvironmentUrl
    Dataverse environment URL (the DEV environment holding SpaarkeMaster).

.PARAMETER MasterSolutionUniqueName
    Target solution unique name. Default: SpaarkeMaster

.PARAMETER OutputZipPath
    Where to write the exported managed ZIP.

.PARAMETER ExcludedPCFs
    Optional list of PCF (Custom Control) unique names to EXCLUDE from the assembly.

.PARAMETER VersionBumpKind
    Which version segment to bump when new components are added. Values: Build (default) | Revision | Minor | Major

.PARAMETER SkipExport
    If set, only augment the solution; do not export.

.PARAMETER WhatIf
    Dry-run mode: report what WOULD be added / bumped / exported, without touching Dataverse.

.EXAMPLE
    ./Assemble-SpaarkeMasterSolution.ps1

.EXAMPLE
    ./Assemble-SpaarkeMasterSolution.ps1 -WhatIf

.EXAMPLE
    ./Assemble-SpaarkeMasterSolution.ps1 -ExcludedPCFs @('SpaarkeSemanticSearch','SpaarkePlaybookBuilderHost') -VersionBumpKind Minor

.NOTES
    Prereqs: az CLI logged in with permission to acquire tokens for the target Dataverse env.
    Prereq: Get-SpaarkeComponents.ps1 has run and produced a current inventory JSON.
    Prereq: Spaarke.Plugins is removed (Wave H-3 pre-work; see spaarkedev1 audit 2026-08-20).
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$InventoryPath = "$PSScriptRoot/../../docs/data-model/spaarke-components-inventory.json",
    [string]$OobManifestPath = "$PSScriptRoot/../../docs/data-model/oob-customizations.yaml",
    [string]$EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
    [string]$MasterSolutionUniqueName = 'SpaarkeMaster',
    [string]$OutputZipPath = "$PSScriptRoot/../../out/SpaarkeMaster.zip",
    [string[]]$ExcludedPCFs = @(),
    [ValidateSet('Build', 'Revision', 'Minor', 'Major')]
    [string]$VersionBumpKind = 'Build',
    [switch]$SkipExport
)

$ErrorActionPreference = 'Stop'

# ---- 0. Pre-flight ----------------------------------------------------------

if (-not (Test-Path $InventoryPath)) {
    throw "Inventory not found at $InventoryPath. Run Get-SpaarkeComponents.ps1 first."
}
if (-not (Test-Path $OobManifestPath)) {
    throw "OOB manifest not found at $OobManifestPath."
}

$inventory = Get-Content $InventoryPath -Raw | ConvertFrom-Json
Write-Host "==> Inventory loaded: $($inventory.metadata.distinctComponents) distinct components across $($inventory.metadata.includedSolutions) solutions" -ForegroundColor Cyan

# Parse OOB yaml (simple parse; if the file grows, use powershell-yaml module)
$oobYaml = Get-Content $OobManifestPath -Raw
$oobEntities = @{}
$currentEntity = $null
foreach ($line in ($oobYaml -split "`n")) {
    if ($line -match '^\s{2}(\w+):\s*$' -and $line -notmatch '^\s+\-') {
        $indent = ($line -replace '\S.*$', '').Length
        if ($indent -eq 2) {
            $currentEntity = $Matches[1]
            $oobEntities[$currentEntity] = @()
        }
    }
    if ($currentEntity -and $line -match '^\s+\-\s+(sprk_[\w]+)') {
        $oobEntities[$currentEntity] += $Matches[1]
    }
}
Write-Host "==> OOB manifest: $($oobEntities.Count) entities, $((($oobEntities.Values | ForEach-Object { $_.Count }) | Measure-Object -Sum).Sum) sprk_ columns" -ForegroundColor Cyan
foreach ($e in $oobEntities.Keys) {
    Write-Host "    $e : $($oobEntities[$e].Count) columns" -ForegroundColor DarkGray
}

# ---- 1. Token + connection --------------------------------------------------

Write-Host "==> Acquiring access token for $EnvironmentUrl" -ForegroundColor Cyan
$tokenJson = az account get-access-token --resource $EnvironmentUrl 2>$null
$azExit = $LASTEXITCODE
if ($azExit -ne 0 -or -not $tokenJson) {
    throw "Failed to acquire az access token. Run 'az login' first."
}
$token = ($tokenJson | ConvertFrom-Json).accessToken

$headers = @{
    Authorization        = "Bearer $token"
    'OData-MaxVersion'   = '4.0'
    'OData-Version'      = '4.0'
    Accept               = 'application/json'
    'Content-Type'       = 'application/json'
}

function Invoke-Dataverse([string]$Endpoint, [string]$Method = 'GET', $Body = $null) {
    $uri = "$EnvironmentUrl/api/data/v9.2/$Endpoint"
    $params = @{ Uri = $uri; Headers = $headers; Method = $Method }
    if ($Body) { $params.Body = ($Body | ConvertTo-Json -Depth 10) }
    return Invoke-RestMethod @params
}

# ---- 2. Look up SpaarkeMaster + its current components ----------------------

Write-Host "==> Looking up $MasterSolutionUniqueName solution" -ForegroundColor Cyan
$masterResult = Invoke-Dataverse "solutions?`$select=solutionid,uniquename,friendlyname,version,ismanaged&`$filter=uniquename eq '$MasterSolutionUniqueName'"
if (-not $masterResult.value -or $masterResult.value.Count -eq 0) {
    throw "$MasterSolutionUniqueName not found in $EnvironmentUrl. Create it in PPAC first, or amend the -MasterSolutionUniqueName param."
}
$master = $masterResult.value[0]
if ($master.ismanaged) {
    throw "$MasterSolutionUniqueName is MANAGED. Cannot augment a managed solution in place. Ensure this is the unmanaged dev version."
}
Write-Host "    Solution: $($master.uniquename) v$($master.version) (id: $($master.solutionid))" -ForegroundColor Green

Write-Host "==> Enumerating current SpaarkeMaster components (baseline)" -ForegroundColor Cyan
$currentResult = Invoke-Dataverse "solutioncomponents?`$select=componenttype,objectid&`$filter=_solutionid_value eq $($master.solutionid)"
$currentSet = @{}
foreach ($c in $currentResult.value) {
    $key = "$($c.componenttype)|$($c.objectid)"
    $currentSet[$key] = $true
}
Write-Host "    Current baseline: $($currentSet.Count) components already in $MasterSolutionUniqueName" -ForegroundColor Green

# ---- 3. Compute deltas ------------------------------------------------------

$toAdd = @()

# 3a. Publisher-owned distinct components from inventory (skip PCFs on exclude list)
foreach ($comp in $inventory.distinctComponents) {
    $key = "$($comp.ComponentType)|$($comp.ObjectId)"
    if ($currentSet.ContainsKey($key)) { continue }

    if ($comp.ComponentType -eq 66 -and $ExcludedPCFs.Count -gt 0) {
        # Custom Control (PCF) - check exclude list. Component objectid is control name typically.
        # NOTE: exclusion by name requires resolving object -> name; deferred to post-first-E2E authoring.
        # For now log a WARNING if user passed excludes so they know it is not yet wired.
    }

    $toAdd += [PSCustomObject]@{
        Source            = 'inventory'
        ComponentType     = $comp.ComponentType
        ComponentTypeName = $comp.ComponentTypeName
        ObjectId          = $comp.ObjectId
        Description       = "$($comp.ComponentTypeName) $($comp.ObjectId) (from $($comp.InSolutions))"
    }
}

if ($ExcludedPCFs.Count -gt 0) {
    Write-Warning "PCF name-based exclusion is not yet wired in this pass. ExcludedPCFs=$($ExcludedPCFs -join ',') will be honored in a follow-on release. Currently ALL PCFs from inventory are included."
}

# 3b. OOB customizations - resolve attribute MetadataId per entity/attribute
Write-Host "==> Resolving OOB attribute MetadataIds (for componenttype=2 add)" -ForegroundColor Cyan
foreach ($entity in $oobEntities.Keys) {
    foreach ($attr in $oobEntities[$entity]) {
        try {
            $meta = Invoke-Dataverse "EntityDefinitions(LogicalName='$entity')/Attributes(LogicalName='$attr')?`$select=MetadataId"
            $attrId = $meta.MetadataId
            $key = "2|$attrId"
            if (-not $currentSet.ContainsKey($key)) {
                $toAdd += [PSCustomObject]@{
                    Source            = 'oob-manifest'
                    ComponentType     = 2
                    ComponentTypeName = 'Attribute'
                    ObjectId          = $attrId
                    Description       = "OOB attribute $entity.$attr (MetadataId $attrId)"
                }
            }
        } catch {
            Write-Warning "Failed to resolve $entity.$attr : $($_.Exception.Message)"
        }
    }
}

# ---- 4. Report + optional apply ---------------------------------------------

Write-Host ""
Write-Host "==> DELTA SUMMARY" -ForegroundColor Cyan
Write-Host "    Items to add to $MasterSolutionUniqueName : $($toAdd.Count)"
if ($toAdd.Count -eq 0) {
    Write-Host "    Nothing to do. SpaarkeMaster is already up to date." -ForegroundColor Green
    exit 0
}

$byType = $toAdd | Group-Object ComponentTypeName | Sort-Object Name
foreach ($g in $byType) {
    Write-Host ("    +{0,-38} {1,6}" -f $g.Name, $g.Count) -ForegroundColor Yellow
}
Write-Host ""

if ($WhatIfPreference) {
    Write-Host "==> DRY-RUN MODE (-WhatIf) - no changes applied." -ForegroundColor Yellow
    $toAdd | ForEach-Object { Write-Host "    WOULD ADD: $($_.Description)" -ForegroundColor DarkYellow }
    exit 0
}

# ---- 5. Apply: add each component via AddSolutionComponent action -----------

Write-Host "==> Adding components to $MasterSolutionUniqueName" -ForegroundColor Cyan
$added = 0
$failed = @()
foreach ($item in $toAdd) {
    $body = @{
        ComponentId              = $item.ObjectId
        ComponentType            = $item.ComponentType
        SolutionUniqueName       = $MasterSolutionUniqueName
        AddRequiredComponents    = $false
    }
    try {
        Invoke-Dataverse 'AddSolutionComponent' -Method POST -Body $body | Out-Null
        $added++
        Write-Host "    + $($item.Description)" -ForegroundColor Green
    } catch {
        $failed += $item
        Write-Warning "    FAILED: $($item.Description) : $($_.Exception.Message)"
    }
}

Write-Host ""
Write-Host "    Added: $added / $($toAdd.Count) ; Failed: $($failed.Count)" -ForegroundColor $(if ($failed.Count) { 'Yellow' } else { 'Green' })

if ($failed.Count -gt 0) {
    Write-Warning "Some components failed to add. Review warnings above; some component types (e.g., certain OOB attributes) may require -AddRequiredComponents true or a separate authoring step."
}

# ---- 6. Bump version --------------------------------------------------------

$parts = $master.version -split '\.'
while ($parts.Length -lt 4) { $parts += '0' }
switch ($VersionBumpKind) {
    'Major'    { $parts[0] = [int]$parts[0] + 1 ; $parts[1] = 0 ; $parts[2] = 0 ; $parts[3] = 0 }
    'Minor'    { $parts[1] = [int]$parts[1] + 1 ; $parts[2] = 0 ; $parts[3] = 0 }
    'Build'    { $parts[2] = [int]$parts[2] + 1 ; $parts[3] = 0 }
    'Revision' { $parts[3] = [int]$parts[3] + 1 }
}
$newVersion = $parts -join '.'
Write-Host "==> Bumping version : $($master.version) -> $newVersion ($VersionBumpKind)" -ForegroundColor Cyan
$patchBody = @{ version = $newVersion }
Invoke-Dataverse "solutions($($master.solutionid))" -Method PATCH -Body $patchBody | Out-Null
Write-Host "    Version bumped." -ForegroundColor Green

# ---- 7. Export as managed ---------------------------------------------------

if ($SkipExport) {
    Write-Host "==> SkipExport specified. Done." -ForegroundColor Green
    exit 0
}

$outputDir = Split-Path $OutputZipPath -Parent
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

Write-Host "==> Exporting $MasterSolutionUniqueName as MANAGED to $OutputZipPath" -ForegroundColor Cyan
pac solution export --name $MasterSolutionUniqueName --path $OutputZipPath --managed --overwrite
$pacExit = $LASTEXITCODE
if ($pacExit -ne 0) {
    throw "pac solution export failed (exit $pacExit). Check pac auth is set to $EnvironmentUrl."
}

Write-Host ""
Write-Host "==> DONE" -ForegroundColor Green
Write-Host "    Solution : $MasterSolutionUniqueName v$newVersion"
Write-Host "    Added    : $added components"
Write-Host "    Export   : $OutputZipPath ($([Math]::Round((Get-Item $OutputZipPath).Length / 1KB)) KB)"
Write-Host ""
Write-Host "    Next: hand off $OutputZipPath to customer-provisioning-orchestration-r1 H6 pipeline"
Write-Host "          (upload to provisioning-artifacts blob storage per Wave H-3)."
