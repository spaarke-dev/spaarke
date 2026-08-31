<#
.SYNOPSIS
    Enumerate all Spaarke-publisher-owned components in a Dataverse environment.

.DESCRIPTION
    Queries the Dataverse Web API against the specified environment (defaults to spaarkedev1)
    for all solutions owned by the Spaarke publisher, then enumerates every component
    inside those solutions grouped by component type.

    Emits a deterministic JSON inventory that Assemble-SpaarkeMasterSolution.ps1 consumes
    to know what to include in the packaged managed solution, and that Test-SolutionCompleteness.ps1
    compares against for drift detection.

    This is a READ-ONLY script - safe to run any time.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER PublisherUniqueName
    Spaarke publisher unique name. Default: Spaarke

.PARAMETER OutputPath
    Path to write the inventory JSON. Default: docs/data-model/spaarke-components-inventory.json

.PARAMETER ExcludeSolutionPatterns
    Regex patterns for solution unique names to exclude (test/scratch/temp).
    Default: 'Test$|Temp|SCRATCH|MasterTest|TestSpaarke'

.EXAMPLE
    ./Get-SpaarkeComponents.ps1

.EXAMPLE
    ./Get-SpaarkeComponents.ps1 -EnvironmentUrl 'https://otherorg.crm.dynamics.com' -OutputPath './inventory.json'

.NOTES
    Requires: az CLI logged in with permission to acquire tokens for the target Dataverse env.
    Auth via: az account get-access-token --resource {EnvironmentUrl}
#>

[CmdletBinding()]
param(
    [string]$EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
    [string]$PublisherUniqueName = 'Spaarke',
    [string]$OutputPath = "$PSScriptRoot/../../docs/data-model/spaarke-components-inventory.json",
    [string]$ExcludeSolutionPatterns = 'Test$|Temp|SCRATCH|MasterTest|TestSpaarke'
)

$ErrorActionPreference = 'Stop'
$env:AZURE_CORE_OUTPUT = 'json'

# ---- Component type registry (from Dataverse metadata) -----------------------

$ComponentTypeNames = @{
    1     = 'Entity'
    2     = 'Attribute'
    9     = 'OptionSet'
    10    = 'EntityRelationship'
    20    = 'Role'
    26    = 'SavedQuery'
    59    = 'Chart'
    60    = 'SystemForm'
    61    = 'WebResource'
    62    = 'SiteMap'
    66    = 'CustomControl'
    80    = 'AppModule'
    91    = 'PluginAssembly'
    92    = 'SDKMessageProcessingStep'
    93    = 'ServiceEndpoint'
    300   = 'CanvasApp'
    371   = 'ConnectionReference'
    380   = 'EnvironmentVariableDefinition'
    381   = 'EnvironmentVariableValue'
}

function Get-ComponentTypeName([int]$type) {
    if ($ComponentTypeNames.ContainsKey($type)) { return $ComponentTypeNames[$type] }
    return "ComponentType_$type"
}

# ---- Acquire access token ---------------------------------------------------

Write-Host "==> Acquiring access token for $EnvironmentUrl" -ForegroundColor Cyan
$tokenJson = az account get-access-token --resource $EnvironmentUrl 2>$null
$azExit = $LASTEXITCODE
if ($azExit -ne 0 -or -not $tokenJson) {
    throw "Failed to acquire az access token for $EnvironmentUrl. Run 'az login' first and confirm your account has access to the Dataverse env."
}
$token = ($tokenJson | ConvertFrom-Json).accessToken

$headers = @{
    Authorization        = "Bearer $token"
    'OData-MaxVersion'   = '4.0'
    'OData-Version'      = '4.0'
    Accept               = 'application/json'
    Prefer               = 'odata.include-annotations="*"'
}

function Invoke-Dataverse([string]$Endpoint) {
    $uri = "$EnvironmentUrl/api/data/v9.2/$Endpoint"
    try {
        return Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
    } catch {
        throw "Dataverse GET failed for $uri : $($_.Exception.Message)"
    }
}

# ---- 1. Publisher lookup -----------------------------------------------------

Write-Host "==> Looking up Spaarke publisher" -ForegroundColor Cyan
$pubQuery = "publishers?`$select=publisherid,uniquename,customizationprefix,customizationoptionvalueprefix&`$filter=uniquename eq '$PublisherUniqueName'"
$pubResult = Invoke-Dataverse $pubQuery

if (-not $pubResult.value -or $pubResult.value.Count -eq 0) {
    throw "Publisher '$PublisherUniqueName' not found in $EnvironmentUrl"
}

$publisher = $pubResult.value[0]
$publisherId = $publisher.publisherid
Write-Host "    Publisher: $($publisher.uniquename) (id: $publisherId, prefix: $($publisher.customizationprefix))" -ForegroundColor Green

# ---- 2. Solutions owned by Spaarke publisher --------------------------------

Write-Host "==> Enumerating Spaarke-owned solutions" -ForegroundColor Cyan
$solQuery = "solutions?`$select=solutionid,uniquename,friendlyname,version,ismanaged&`$filter=_publisherid_value eq $publisherId"
$solResult = Invoke-Dataverse $solQuery
$allSolutions = $solResult.value

Write-Host "    Total Spaarke-owned solutions: $($allSolutions.Count)" -ForegroundColor Green

# Filter excluded
$includedSolutions = $allSolutions | Where-Object { $_.uniquename -notmatch $ExcludeSolutionPatterns }
$excludedSolutions = $allSolutions | Where-Object { $_.uniquename -match $ExcludeSolutionPatterns }

Write-Host "    Included: $($includedSolutions.Count), Excluded (test/scratch): $($excludedSolutions.Count)" -ForegroundColor Green
if ($excludedSolutions) {
    Write-Host "    Excluded solutions:" -ForegroundColor Gray
    $excludedSolutions | ForEach-Object { Write-Host "      - $($_.uniquename)" -ForegroundColor Gray }
}

# ---- 3. Enumerate components per solution -----------------------------------

Write-Host "==> Enumerating components per solution" -ForegroundColor Cyan
$allComponents = @()

foreach ($sol in $includedSolutions) {
    $compQuery = "solutioncomponents?`$select=solutioncomponentid,componenttype,objectid,rootcomponentbehavior&`$filter=_solutionid_value eq $($sol.solutionid)"
    try {
        $compResult = Invoke-Dataverse $compQuery
        $count = $compResult.value.Count
        Write-Host "    $($sol.uniquename): $count components" -ForegroundColor DarkGray

        foreach ($comp in $compResult.value) {
            $allComponents += [PSCustomObject]@{
                SolutionUniqueName = $sol.uniquename
                SolutionId         = $sol.solutionid
                ComponentType      = $comp.componenttype
                ComponentTypeName  = Get-ComponentTypeName $comp.componenttype
                ObjectId           = $comp.objectid
                RootBehavior       = $comp.rootcomponentbehavior
            }
        }
    } catch {
        Write-Warning "    Failed to enumerate $($sol.uniquename): $($_.Exception.Message)"
    }
}

Write-Host "    Total components collected: $($allComponents.Count)" -ForegroundColor Green

# ---- 4. Group + summary -----------------------------------------------------

$groupedByType = $allComponents | Group-Object ComponentTypeName | Sort-Object Name

Write-Host "==> Component counts by type (across all included solutions)" -ForegroundColor Cyan
$groupedByType | ForEach-Object {
    Write-Host ("    {0,-38} {1,6}" -f $_.Name, $_.Count) -ForegroundColor DarkGray
}

# Deduplicate: same objectid + componenttype across multiple solutions
$distinctComponents = $allComponents | Group-Object ObjectId, ComponentType | ForEach-Object {
    $first = $_.Group[0]
    [PSCustomObject]@{
        ComponentType      = $first.ComponentType
        ComponentTypeName  = $first.ComponentTypeName
        ObjectId           = $first.ObjectId
        RootBehavior       = $first.RootBehavior
        InSolutions        = ($_.Group | ForEach-Object { $_.SolutionUniqueName }) -join ','
    }
}

Write-Host "    Distinct components (dedup): $($distinctComponents.Count)" -ForegroundColor Green

# ---- 5. Emit inventory JSON -------------------------------------------------

$outputDir = Split-Path $OutputPath -Parent
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

$inventory = [PSCustomObject]@{
    metadata           = [PSCustomObject]@{
        environment          = $EnvironmentUrl
        publisher            = $PublisherUniqueName
        publisherId          = $publisherId
        publisherPrefix      = $publisher.customizationprefix
        timestamp            = (Get-Date -Format 'yyyy-MM-ddTHH:mm:ssZ')
        totalSolutions       = $allSolutions.Count
        includedSolutions    = $includedSolutions.Count
        excludedSolutions    = $excludedSolutions.Count
        totalComponents      = $allComponents.Count
        distinctComponents   = $distinctComponents.Count
        excludePatterns      = $ExcludeSolutionPatterns
    }
    solutions          = @($includedSolutions | ForEach-Object {
        [PSCustomObject]@{
            uniqueName    = $_.uniquename
            friendlyName  = $_.friendlyname
            version       = $_.version
            isManaged     = $_.ismanaged
            solutionId    = $_.solutionid
        }
    })
    excludedSolutions  = @($excludedSolutions | ForEach-Object {
        [PSCustomObject]@{
            uniqueName    = $_.uniquename
            friendlyName  = $_.friendlyname
        }
    })
    componentsByType   = @($groupedByType | ForEach-Object {
        [PSCustomObject]@{
            typeName = $_.Name
            count    = $_.Count
        }
    })
    distinctComponents = @($distinctComponents | Sort-Object ComponentTypeName, ObjectId)
}

$inventoryJson = $inventory | ConvertTo-Json -Depth 10
Set-Content -Path $OutputPath -Value $inventoryJson -Encoding UTF8

Write-Host ""
Write-Host "==> Inventory written: $OutputPath" -ForegroundColor Green
Write-Host "    File size: $((Get-Item $OutputPath).Length) bytes" -ForegroundColor Green
Write-Host ""
Write-Host "SUMMARY:" -ForegroundColor Cyan
Write-Host "  Publisher      : $PublisherUniqueName"
Write-Host "  Solutions      : $($includedSolutions.Count) included / $($excludedSolutions.Count) excluded"
Write-Host "  Components     : $($distinctComponents.Count) distinct / $($allComponents.Count) with-duplicates"
Write-Host "  OOB manifest   : docs/data-model/oob-customizations.yaml (checked separately)"
Write-Host ""
