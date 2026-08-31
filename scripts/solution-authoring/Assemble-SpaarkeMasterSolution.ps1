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

.PARAMETER ExcludedAppModules
    List of AppModule (MDA) unique names to EXCLUDE. Default excludes 'sprk_SpaarkePlatform'.

.PARAMETER ExcludedEntities
    List of Entity logical names to EXCLUDE. Default excludes 'email' (OOB email is not used).

.PARAMETER ExcludeCanvasApps
    If set (default true), exclude Canvas Apps (componenttype=300) entirely.

.PARAMETER SkipTransitiveFilter
    If set, DISABLE the rootcomponentbehavior=0 transitive-inclusion filter (debug only;
    causes over-inclusion of sub-components that are already transitively in SpaarkeMaster).

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
    [string[]]$ExcludedAppModules = @('sprk_SpaarkePlatform'),
    [string[]]$ExcludedEntities = @('email'),
    [bool]$ExcludeCanvasApps = $true,
    [switch]$SkipTransitiveFilter,
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
$currentResult = Invoke-Dataverse "solutioncomponents?`$select=componenttype,objectid,rootcomponentbehavior&`$filter=_solutionid_value eq $($master.solutionid)"
$currentSet = @{}
$masterFullIncludeEntities = @{}  # entities in SpaarkeMaster where rootcomponentbehavior=0 (transitively include sub-components)
foreach ($c in $currentResult.value) {
    $key = "$($c.componenttype)|$($c.objectid)"
    $currentSet[$key] = $true
    if ($c.componenttype -eq 1 -and $c.rootcomponentbehavior -eq 0) {
        $masterFullIncludeEntities[$c.objectid] = $true
    }
}
Write-Host "    Current baseline: $($currentSet.Count) components already in $MasterSolutionUniqueName" -ForegroundColor Green
Write-Host "    Full-include entities (rootcomponentbehavior=0): $($masterFullIncludeEntities.Count) [sub-components of these are transitively in export]" -ForegroundColor Green

# ---- 2b. Load metadata lookup tables for transitive-inclusion filter --------

$attributeToEntity = @{}     # attribute MetadataId -> parent entity MetadataId
$savedqueryToEntity = @{}    # savedqueryid -> entity MetadataId
$systemformToEntity = @{}    # formid -> entity MetadataId
$entityLogicalNames = @{}    # entity MetadataId -> logicalname
$appModuleNames = @{}        # appmoduleid -> uniquename
$customControlNames = @{}    # customcontrolid -> name (for -ExcludedPCFs)
$objTypeCodeToEntityId = @{} # logicalname -> MetadataId

if (-not $SkipTransitiveFilter) {
    Write-Host "==> Loading metadata for transitive-inclusion filter (may take ~10-30s)" -ForegroundColor Cyan

    # Entities + attributes in one call (expands are efficient in Dataverse)
    $entMeta = Invoke-Dataverse "EntityDefinitions?`$select=MetadataId,LogicalName,IsCustomEntity&`$expand=Attributes(`$select=MetadataId)"
    foreach ($e in $entMeta.value) {
        $entityLogicalNames[$e.MetadataId] = $e.LogicalName
        $objTypeCodeToEntityId[$e.LogicalName] = $e.MetadataId
        foreach ($a in $e.Attributes) {
            $attributeToEntity[$a.MetadataId] = $e.MetadataId
        }
    }
    Write-Host "    Loaded $($entityLogicalNames.Count) entities, $($attributeToEntity.Count) attributes" -ForegroundColor DarkGray

    # Saved queries -> entity via returnedtypecode
    $sqResult = Invoke-Dataverse "savedqueries?`$select=savedqueryid,returnedtypecode"
    foreach ($sq in $sqResult.value) {
        if ($sq.returnedtypecode -and $objTypeCodeToEntityId.ContainsKey($sq.returnedtypecode)) {
            $savedqueryToEntity[$sq.savedqueryid] = $objTypeCodeToEntityId[$sq.returnedtypecode]
        }
    }
    Write-Host "    Loaded $($savedqueryToEntity.Count) savedquery->entity mappings" -ForegroundColor DarkGray

    # System forms -> entity via objecttypecode
    $formResult = Invoke-Dataverse "systemforms?`$select=formid,objecttypecode"
    foreach ($f in $formResult.value) {
        if ($f.objecttypecode -and $objTypeCodeToEntityId.ContainsKey($f.objecttypecode)) {
            $systemformToEntity[$f.formid] = $objTypeCodeToEntityId[$f.objecttypecode]
        }
    }
    Write-Host "    Loaded $($systemformToEntity.Count) systemform->entity mappings" -ForegroundColor DarkGray

    # AppModules -> uniquename (for exclusion)
    $appResult = Invoke-Dataverse "appmodules?`$select=appmoduleid,uniquename"
    foreach ($a in $appResult.value) {
        $appModuleNames[$a.appmoduleid] = $a.uniquename
    }
    Write-Host "    Loaded $($appModuleNames.Count) appmodules" -ForegroundColor DarkGray

    # CustomControls -> name (for -ExcludedPCFs name-based exclusion)
    $ccResult = Invoke-Dataverse "customcontrols?`$select=customcontrolid,name"
    foreach ($cc in $ccResult.value) {
        $customControlNames[$cc.customcontrolid] = $cc.name
    }
    Write-Host "    Loaded $($customControlNames.Count) customcontrols" -ForegroundColor DarkGray
}

# ---- 3. Compute deltas ------------------------------------------------------

# Resolve excluded-entity MetadataIds for cascade filter
$excludedEntityMetadataIds = @{}
foreach ($id in $entityLogicalNames.Keys) {
    if ($ExcludedEntities -contains $entityLogicalNames[$id]) {
        $excludedEntityMetadataIds[$id] = $true
    }
}
Write-Host "==> Excluded entity MetadataIds resolved: $($excludedEntityMetadataIds.Count) [$($ExcludedEntities -join ', ')]" -ForegroundColor DarkGray

# System-managed componenttype ranges (Dataverse-internal; auto-generated with parent entities)
$systemManagedTypeRange = 10000..11000

$toAdd = @()
$skippedByReason = @{
    'transitive-in-master'      = 0
    'excluded-entity'           = 0
    'excluded-entity-cascade'   = 0
    'excluded-appmodule'        = 0
    'excluded-canvasapp'        = 0
    'excluded-pcf'              = 0
    'system-managed-10xxx'      = 0
}

# 3a. Publisher-owned distinct components from inventory
foreach ($comp in $inventory.distinctComponents) {
    $key = "$($comp.ComponentType)|$($comp.ObjectId)"
    if ($currentSet.ContainsKey($key)) { continue }

    # ---- Exclusion filters ----

    # Canvas Apps entirely (per owner directive)
    if ($ExcludeCanvasApps -and $comp.ComponentType -eq 300) {
        $skippedByReason['excluded-canvasapp']++
        continue
    }

    # OOB entities we do NOT want (email etc.)
    if ($comp.ComponentType -eq 1 -and $entityLogicalNames.ContainsKey($comp.ObjectId)) {
        $logicalName = $entityLogicalNames[$comp.ObjectId]
        if ($ExcludedEntities -contains $logicalName) {
            $skippedByReason['excluded-entity']++
            continue
        }
    }

    # Excluded AppModules by uniquename (e.g. Spaarke Platform)
    if ($comp.ComponentType -eq 80 -and $appModuleNames.ContainsKey($comp.ObjectId)) {
        $appUniqueName = $appModuleNames[$comp.ObjectId]
        if ($ExcludedAppModules -contains $appUniqueName) {
            $skippedByReason['excluded-appmodule']++
            continue
        }
    }

    # Excluded PCFs (CustomControls) by name — customer-provisioning-orchestration-r1 owner directive 2026-08-20
    # These stay deployed in spaarkedev1 but are kept OUT of the shipped SpaarkeMaster ZIP.
    # Follow-on retirement work: see pcf-legacy-retirement-r1 (both non-shipping-safe PCFs AND
    # in-service PCFs currently mislabeled as retired).
    if ($comp.ComponentType -eq 66 -and $customControlNames.ContainsKey($comp.ObjectId)) {
        $ccName = $customControlNames[$comp.ObjectId]
        if ($ExcludedPCFs -contains $ccName) {
            $skippedByReason['excluded-pcf']++
            continue
        }
    }

    # ---- Cascade: skip sub-components whose parent entity is in ExcludedEntities ----
    $parentEntityId = $null
    switch ($comp.ComponentType) {
        2  { $parentEntityId = $attributeToEntity[$comp.ObjectId] }        # Attribute
        26 { $parentEntityId = $savedqueryToEntity[$comp.ObjectId] }       # SavedQuery
        60 { $parentEntityId = $systemformToEntity[$comp.ObjectId] }       # SystemForm
    }
    if ($parentEntityId -and $excludedEntityMetadataIds.ContainsKey($parentEntityId)) {
        $skippedByReason['excluded-entity-cascade']++
        continue
    }

    # ---- Transitive-inclusion filter (rootcomponentbehavior=0 on parent entity) ----
    if (-not $SkipTransitiveFilter -and $parentEntityId) {
        if ($masterFullIncludeEntities.ContainsKey($parentEntityId)) {
            $skippedByReason['transitive-in-master']++
            continue
        }
    }

    # ---- System-managed 10xxx types (Dataverse auto-generates with parents) ----
    if ($comp.ComponentType -ge 10000 -and $comp.ComponentType -le 11000) {
        $skippedByReason['system-managed-10xxx']++
        continue
    }

    # ---- Genuine addition ----
    $toAdd += [PSCustomObject]@{
        Source            = 'inventory'
        ComponentType     = $comp.ComponentType
        ComponentTypeName = $comp.ComponentTypeName
        ObjectId          = $comp.ObjectId
        Description       = "$($comp.ComponentTypeName) $($comp.ObjectId) (from $($comp.InSolutions))"
    }
}

if ($ExcludedPCFs.Count -gt 0) {
    Write-Host "==> PCF exclusion active — $($ExcludedPCFs.Count) PCF(s) will be kept OUT of $MasterSolutionUniqueName" -ForegroundColor Cyan
    foreach ($p in $ExcludedPCFs) { Write-Host "    excluded-pcf: $p" -ForegroundColor DarkGray }
    if ($skippedByReason['excluded-pcf'] -eq 0) {
        Write-Warning "None of the -ExcludedPCFs names matched any customcontrol in the delta. Check that the names are exact matches to customcontrol.name (e.g. 'sprk_Spaarke.Controls.UniversalDocumentUpload'). No PCFs were actually excluded."
    }
}

Write-Host ""
Write-Host "==> FILTER STATS" -ForegroundColor Cyan
Write-Host "    Skipped (transitive-in-master via behavior=0) : $($skippedByReason['transitive-in-master'])"
Write-Host "    Skipped (system-managed 10xxx types)          : $($skippedByReason['system-managed-10xxx'])"
Write-Host "    Skipped (Canvas Apps)                         : $($skippedByReason['excluded-canvasapp'])"
Write-Host "    Skipped (excluded OOB entities)               : $($skippedByReason['excluded-entity'])"
Write-Host "    Skipped (cascade: sub-components of excluded) : $($skippedByReason['excluded-entity-cascade'])"
Write-Host "    Skipped (excluded AppModules)                 : $($skippedByReason['excluded-appmodule'])"
Write-Host "    Skipped (excluded PCFs by name)               : $($skippedByReason['excluded-pcf'])"

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
