<#
.SYNOPSIS
    Builds (or rebuilds) the SpaarkeMaster solution in the dev Dataverse environment.

.DESCRIPTION
    Programmatically composes the SpaarkeMaster solution by adding all Spaarke platform
    components using independent discovery logic. Each component type is discovered via
    its own query (sprk_ prefix, explicit IDs, or name matching) — no dependencies on
    any existing solution's component list.

    Components included (386 total):
      - 87 custom entities (sprk_* prefix, full subcomponents)
      - 4 standard entities (account, contact, systemuser, businessunit — metadata-only)
      - 8 custom attributes on standard entities (sprk_* columns)
      - 195 web resources (sprk_* prefix)
      - 24 global option sets (sprk_* prefix)
      - 11 PCF custom controls (confirmed in-use, explicit IDs)
      - 7 security roles (root BU, "Spaarke" name match)
      - 21 environment variable definitions (sprk_* prefix, type 380)
      - 9 environment variable values (parent definition sprk_* prefix, type 381)
      - 1 MDA app (sprk_MatterManagement, explicit ID)
      - 1 sitemap (sprk_MatterManagement, explicit ID)
      - 14 app module components (from SpaarkeCorporateCounselApp solution, type 10075)
      - 4 entity relationships (M2M junctions, automatic subcomponents)

    What's NOT included:
      - Canvas apps, PowerApps settings/components
      - Managed solutions (Creator Kit, Dataverse Accelerator)
      - email entity customizations
      - 12 orphaned PCF controls
      - 3 legacy sitemaps (tech debt)
      - Reference data (deployed separately via Track 2.5 scripts)

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to dev.

.PARAMETER ExpectedComponentCount
    Expected total component count for verification. Defaults to 386.

.PARAMETER Force
    Delete and recreate SpaarkeMaster if it already exists.

.PARAMETER WhatIf
    Show what would be done without making changes.

.EXAMPLE
    .\scripts\Build-SpaarkeMaster.ps1
    # Builds SpaarkeMaster in dev (creates if missing, errors if exists)

.EXAMPLE
    .\scripts\Build-SpaarkeMaster.ps1 -Force
    # Deletes existing SpaarkeMaster and rebuilds from scratch

.EXAMPLE
    .\scripts\Build-SpaarkeMaster.ps1 -WhatIf
    # Shows discovery results without creating anything
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",
    [int]$ExpectedComponentCount = 385,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

# --- Configuration ---

$PublisherId = "6aeef721-ba73-f011-b4cb-6045bdd6a665"  # Spaarke publisher

# 11 confirmed in-use PCF control IDs
$IncludedPcfIds = @(
    "e88fe153-a88a-4f0c-b2f7-30439142debe"  # DocumentRelationshipViewer
    "b85d8eac-309d-4c22-8f1d-62e4dd7fd067"  # EventFormController
    "69e63415-7604-4c81-863f-a5bed6363507"  # RelatedDocumentCount
    "49b0cecd-705a-4c45-84f6-1014b075139d"  # SpeDocumentViewer
    "14c0701e-242e-417a-8999-62694c3cdcac"  # VisualHost
    "7bfadd63-1e26-4278-92b9-9cfbf9335b6e"  # SemanticSearchControl
    "a109c074-5cc7-4db7-b958-4a2a3ab2d0a0"  # UpdateRelatedButton
    "d5740853-5442-4fe6-ace5-c40f9a00ac8f"  # EmailProcessingMonitor
    "1027a38f-4bdd-4d6b-9422-61f6dfaea686"  # ThemeEnforcer
    "d8c352f3-91d7-454f-b258-d85d30136708"  # RegardingLink
    # EXCLUDED: 88dbb4ef (UniversalDatasetGrid) — broken styles.css web resource reference
)

# MDA App
$MdaAppId = "729afe6d-ca73-f011-b4cb-6045bdd8b757"      # sprk_MatterManagement
$MdaSitemapId = "57410a70-ca73-f011-b4cb-6045bdd6a665"   # sprk_MatterManagement sitemap
$MdaSolutionId = "e871701b-ab28-f111-88b4-7c1e525abd8b"  # SpaarkeCorporateCounselApp (for type 10075 components)

# Standard entities to include (metadata-only + sprk_ columns)
$StandardEntities = @("account", "contact", "systemuser", "businessunit")

# M2M intersection tables to exclude (come as relationship subcomponents)
$M2MExclusions = @(
    "sprk_analysis_knowledge", "sprk_analysis_skill", "sprk_analysis_tool",
    "sprk_analysisplaybook_action", "sprk_analysisplaybook_analysisoutput", "sprk_analysisplaybook_mattertype",
    "sprk_playbook_knowledge", "sprk_playbook_skill", "sprk_playbook_tool",
    "sprk_playbooknode_skill", "sprk_playbooknode_knowledge", "sprk_playbooknode_tool",
    "sprk_matter_contact", "sprk_event_contact", "sprk_project_contact",
    "sprk_project_organization", "sprk_matter_organization", "sprk_project_sprk_matter"
)

# --- Helper Functions ---

function Get-DataverseToken {
    $token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
    if (-not $token) { throw "Failed to get access token. Run 'az login' first." }
    return $token
}

function Invoke-DataverseApi {
    param([string]$Method, [string]$Path, [object]$Body)
    $token = Get-DataverseToken
    $headers = @{
        "Authorization"  = "Bearer $token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"  = "4.0"
        "Content-Type"   = "application/json"
    }
    $uri = "$EnvironmentUrl/api/data/v9.2/$Path"
    $params = @{ Uri = $uri; Method = $Method; Headers = $headers }
    if ($Body) { $params.Body = ($Body | ConvertTo-Json -Depth 10) }
    Invoke-RestMethod @params
}

function Add-SolutionComponent {
    param(
        [string]$ComponentId,
        [int]$ComponentType,
        [bool]$DoNotIncludeSubcomponents = $false
    )
    $body = @{
        ComponentId              = $ComponentId
        ComponentType            = $ComponentType
        SolutionUniqueName       = "SpaarkeMaster"
        AddRequiredComponents    = $false
        DoNotIncludeSubcomponents = $DoNotIncludeSubcomponents
    }
    try {
        Invoke-DataverseApi -Method POST -Path "AddSolutionComponent" -Body $body | Out-Null
        return $true
    } catch {
        return $false
    }
}

# --- Main ---

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Build-SpaarkeMaster.ps1" -ForegroundColor Cyan
Write-Host "  Target: $EnvironmentUrl" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 0: Check if SpaarkeMaster exists
Write-Host "Checking for existing SpaarkeMaster solution..." -ForegroundColor Yellow
$existing = (Invoke-DataverseApi -Method GET -Path "solutions?`$select=solutionid&`$filter=uniquename eq 'SpaarkeMaster'").value
if ($existing.Count -gt 0) {
    if ($Force) {
        if ($PSCmdlet.ShouldProcess("SpaarkeMaster", "Delete existing solution")) {
            Write-Host "  Deleting existing SpaarkeMaster ($($existing[0].solutionid))..." -ForegroundColor Yellow
            Invoke-DataverseApi -Method DELETE -Path "solutions($($existing[0].solutionid))" | Out-Null
            Write-Host "  Deleted." -ForegroundColor Green
        }
    } else {
        throw "SpaarkeMaster already exists (ID: $($existing[0].solutionid)). Use -Force to rebuild."
    }
}

# Step 1: Create SpaarkeMaster solution
if ($PSCmdlet.ShouldProcess("SpaarkeMaster", "Create solution")) {
    Write-Host "Creating SpaarkeMaster solution..." -ForegroundColor Yellow
    Invoke-DataverseApi -Method POST -Path "solutions" -Body @{
        uniquename                = "SpaarkeMaster"
        friendlyname              = "Spaarke Master"
        version                   = "1.0.0.0"
        description               = "Complete Spaarke platform solution - all sprk_ components plus customized standard entities"
        "publisherid@odata.bind"  = "publishers($PublisherId)"
    } | Out-Null
    Write-Host "  Created." -ForegroundColor Green
}

$stats = @{}

# Step 2: Add sprk_ entities (full)
Write-Host ""
Write-Host "[1/11] Adding sprk_ entities (full)..." -ForegroundColor Cyan
$entities = (Invoke-DataverseApi -Method GET -Path "EntityDefinitions?`$select=LogicalName,MetadataId").value |
    Where-Object { $_.LogicalName.StartsWith("sprk_") -and $_.LogicalName -notin $M2MExclusions }
$added = 0
foreach ($e in $entities) {
    if ($PSCmdlet.ShouldProcess($e.LogicalName, "Add entity")) {
        if (Add-SolutionComponent -ComponentId $e.MetadataId -ComponentType 1) { $added++ }
    }
}
$stats["Entities (custom)"] = $added
Write-Host "  Added $added entities" -ForegroundColor Green

# Step 3: Add standard entities (metadata-only) + sprk_ columns
Write-Host "[2/11] Adding standard entities (metadata-only)..." -ForegroundColor Cyan
$stdAdded = 0; $attrAdded = 0
foreach ($entityName in $StandardEntities) {
    $meta = Invoke-DataverseApi -Method GET -Path "EntityDefinitions(LogicalName='$entityName')?`$select=MetadataId"
    if ($PSCmdlet.ShouldProcess($entityName, "Add standard entity (metadata-only)")) {
        if (Add-SolutionComponent -ComponentId $meta.MetadataId -ComponentType 1 -DoNotIncludeSubcomponents $true) { $stdAdded++ }
    }
    # Add sprk_ columns
    $attrs = (Invoke-DataverseApi -Method GET -Path "EntityDefinitions(LogicalName='$entityName')/Attributes?`$select=LogicalName,MetadataId&`$filter=IsCustomAttribute eq true").value |
        Where-Object { $_.LogicalName.StartsWith("sprk_") -and -not $_.LogicalName.EndsWith("name") -and -not $_.LogicalName.EndsWith("yominame") }
    foreach ($a in $attrs) {
        if ($PSCmdlet.ShouldProcess("$entityName.$($a.LogicalName)", "Add attribute")) {
            if (Add-SolutionComponent -ComponentId $a.MetadataId -ComponentType 2) { $attrAdded++ }
        }
    }
}
$stats["Entities (standard)"] = $stdAdded
$stats["Attributes"] = $attrAdded
Write-Host "  Added $stdAdded standard entities, $attrAdded attributes" -ForegroundColor Green

# Step 4: Add web resources
Write-Host "[3/11] Adding sprk_ web resources..." -ForegroundColor Cyan
$webResources = (Invoke-DataverseApi -Method GET -Path "webresourceset?`$select=webresourceid&`$filter=startswith(name,'sprk_')").value
$added = 0
foreach ($wr in $webResources) {
    if ($PSCmdlet.ShouldProcess($wr.webresourceid, "Add web resource")) {
        if (Add-SolutionComponent -ComponentId $wr.webresourceid -ComponentType 61) { $added++ }
    }
}
$stats["Web Resources"] = $added
Write-Host "  Added $added web resources" -ForegroundColor Green

# Step 5: Add global option sets
Write-Host "[4/11] Adding sprk_ option sets..." -ForegroundColor Cyan
$optionSets = (Invoke-DataverseApi -Method GET -Path "GlobalOptionSetDefinitions?`$select=Name,MetadataId").value |
    Where-Object { $_.Name.StartsWith("sprk_") }
$added = 0
foreach ($os in $optionSets) {
    if ($PSCmdlet.ShouldProcess($os.Name, "Add option set")) {
        if (Add-SolutionComponent -ComponentId $os.MetadataId -ComponentType 9) { $added++ }
    }
}
$stats["Option Sets"] = $added
Write-Host "  Added $added option sets" -ForegroundColor Green

# Step 6: Add PCF controls (11 confirmed in-use)
Write-Host "[5/11] Adding PCF controls (11 confirmed)..." -ForegroundColor Cyan
$added = 0
foreach ($pcfId in $IncludedPcfIds) {
    if ($PSCmdlet.ShouldProcess($pcfId, "Add PCF control")) {
        if (Add-SolutionComponent -ComponentId $pcfId -ComponentType 66) { $added++ }
    }
}
$stats["PCF Controls"] = $added
Write-Host "  Added $added PCF controls" -ForegroundColor Green

# Step 7: Add security roles (root BU, "Spaarke" name)
Write-Host "[6/11] Adding security roles..." -ForegroundColor Cyan
$rootBu = (Invoke-DataverseApi -Method GET -Path "businessunits?`$select=businessunitid&`$filter=parentbusinessunitid eq null").value[0]
$roles = (Invoke-DataverseApi -Method GET -Path "roles?`$select=roleid,name&`$filter=_businessunitid_value eq '$($rootBu.businessunitid)'").value |
    Where-Object { $_.name -match "(?i)spaarke" }
$added = 0
foreach ($r in $roles) {
    if ($PSCmdlet.ShouldProcess($r.name, "Add security role")) {
        if (Add-SolutionComponent -ComponentId $r.roleid -ComponentType 20) { $added++ }
    }
}
$stats["Security Roles"] = $added
Write-Host "  Added $added security roles" -ForegroundColor Green

# Step 8: Add environment variable definitions (type 380)
Write-Host "[7/11] Adding environment variable definitions..." -ForegroundColor Cyan
$envVarDefs = (Invoke-DataverseApi -Method GET -Path "environmentvariabledefinitions?`$select=environmentvariabledefinitionid,schemaname&`$filter=startswith(schemaname,'sprk_')").value
$added = 0
foreach ($ev in $envVarDefs) {
    if ($PSCmdlet.ShouldProcess($ev.schemaname, "Add env var definition")) {
        if (Add-SolutionComponent -ComponentId $ev.environmentvariabledefinitionid -ComponentType 380) { $added++ }
    }
}
$stats["Env Var Definitions"] = $added
Write-Host "  Added $added env var definitions" -ForegroundColor Green

# Step 9: Add environment variable values (type 381)
Write-Host "[8/11] Adding environment variable values..." -ForegroundColor Cyan
$envVarVals = (Invoke-DataverseApi -Method GET -Path "environmentvariablevalues?`$select=environmentvariablevalueid&`$expand=EnvironmentVariableDefinitionId(`$select=schemaname)").value |
    Where-Object { $_.EnvironmentVariableDefinitionId.schemaname.StartsWith("sprk_") }
$added = 0
foreach ($v in $envVarVals) {
    if ($PSCmdlet.ShouldProcess($v.environmentvariablevalueid, "Add env var value")) {
        if (Add-SolutionComponent -ComponentId $v.environmentvariablevalueid -ComponentType 381) { $added++ }
    }
}
$stats["Env Var Values"] = $added
Write-Host "  Added $added env var values" -ForegroundColor Green

# Step 10: Add MDA app + sitemap
Write-Host "[9/11] Adding MDA app (sprk_MatterManagement)..." -ForegroundColor Cyan
$mdaAdded = 0
if ($PSCmdlet.ShouldProcess("sprk_MatterManagement", "Add MDA app")) {
    if (Add-SolutionComponent -ComponentId $MdaAppId -ComponentType 80) { $mdaAdded++ }
}
$stats["MDA App"] = $mdaAdded
Write-Host "  Added MDA app" -ForegroundColor Green

Write-Host "[10/11] Adding MDA sitemap..." -ForegroundColor Cyan
$smAdded = 0
if ($PSCmdlet.ShouldProcess("sprk_MatterManagement sitemap", "Add sitemap")) {
    if (Add-SolutionComponent -ComponentId $MdaSitemapId -ComponentType 62) { $smAdded++ }
}
$stats["Site Map"] = $smAdded
Write-Host "  Added sitemap" -ForegroundColor Green

# Step 11: Add MDA app module components (type 10075)
Write-Host "[11/11] Adding MDA app module components..." -ForegroundColor Cyan
$appComponents = (Invoke-DataverseApi -Method GET -Path "solutioncomponents?`$select=objectid&`$filter=_solutionid_value eq '$MdaSolutionId' and componenttype eq 10075").value
$added = 0
foreach ($c in $appComponents) {
    if ($PSCmdlet.ShouldProcess($c.objectid, "Add app module component")) {
        if (Add-SolutionComponent -ComponentId $c.objectid -ComponentType 10075) { $added++ }
    }
}
$stats["App Module Components"] = $added
Write-Host "  Added $added app module components" -ForegroundColor Green

# --- Verification ---

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Verification" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$solution = (Invoke-DataverseApi -Method GET -Path "solutions?`$select=solutionid&`$filter=uniquename eq 'SpaarkeMaster'").value[0]
$components = (Invoke-DataverseApi -Method GET -Path "solutioncomponents?`$select=componenttype&`$filter=_solutionid_value eq '$($solution.solutionid)'").value
$totalCount = $components.Count

Write-Host ""
Write-Host "  Component Summary:" -ForegroundColor Yellow
foreach ($key in $stats.Keys | Sort-Object) {
    Write-Host "    $($stats[$key].ToString().PadLeft(5))  $key"
}
Write-Host "    -----"
Write-Host "    $($totalCount.ToString().PadLeft(5))  TOTAL (includes auto-added subcomponents)"
Write-Host ""

if ($totalCount -eq $ExpectedComponentCount) {
    Write-Host "  PASSED: Component count matches expected ($ExpectedComponentCount)" -ForegroundColor Green
} elseif ($totalCount -gt $ExpectedComponentCount) {
    Write-Host "  WARNING: Component count ($totalCount) exceeds expected ($ExpectedComponentCount)" -ForegroundColor Yellow
    Write-Host "  This may indicate new subcomponents were auto-added. Verify and update -ExpectedComponentCount." -ForegroundColor Yellow
} else {
    Write-Host "  WARNING: Component count ($totalCount) is less than expected ($ExpectedComponentCount)" -ForegroundColor Yellow
    Write-Host "  Some components may have failed to add. Check output above for errors." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "  SpaarkeMaster build complete." -ForegroundColor Green
Write-Host ""
