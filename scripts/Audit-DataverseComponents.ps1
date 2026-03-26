#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Audit all Spaarke customizations in a Dataverse environment and compare against solution contents.

.DESCRIPTION
    Discovers ALL Spaarke-related customizations in a Dataverse environment:
      - Custom entities (sprk_* prefix)
      - Custom fields on ANY entity (including standard: Account, Contact, BusinessUnit, etc.)
      - Security roles with sprk publisher
      - Model-driven apps
      - Sitemaps
      - Web resources (sprk_* prefix)
      - Environment variable definitions (sprk_* prefix)
      - Global option sets (sprk_* prefix)
      - PCF controls registered
      - Dashboards and charts
      - Business rules on sprk_ entities
      - Connection roles

    Compares discovered components against what is already in SpaarkeCore and SpaarkeFeatures
    solutions, and reports:
      - Components in environment but NOT in any deployable solution (ORPHANED)
      - Components correctly assigned to SpaarkeCore or SpaarkeFeatures
      - Suggested assignment for orphaned components

    Prerequisites:
      - PAC CLI authenticated to the target environment (`pac auth create`)
      - Or: Azure CLI authenticated for Dataverse Web API access

.PARAMETER DataverseUrl
    Target Dataverse environment URL (e.g., https://spaarkedev1.crm.dynamics.com)

.PARAMETER OutputPath
    Path to write the audit report. Defaults to logs/dataverse-audit-{timestamp}.md

.PARAMETER CoreSolutionName
    Name of the SpaarkeCore solution in Dataverse. Defaults to "SpaarkeCore".

.PARAMETER FeaturesSolutionName
    Name of the SpaarkeFeatures solution in Dataverse. Defaults to "SpaarkeFeatures".

.PARAMETER PublisherPrefix
    Publisher prefix to search for. Defaults to "sprk".

.PARAMETER AddToSolution
    If specified, automatically adds orphaned components to the appropriate solution.
    Requires SpaarkeCore and SpaarkeFeatures solutions to exist in the environment.

.EXAMPLE
    .\Audit-DataverseComponents.ps1 -DataverseUrl "https://spaarkedev1.crm.dynamics.com"
    # Generates audit report

.EXAMPLE
    .\Audit-DataverseComponents.ps1 -DataverseUrl "https://spaarkedev1.crm.dynamics.com" -AddToSolution
    # Generates report AND adds orphaned components to appropriate solutions
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$DataverseUrl,

    [string]$OutputPath,

    [string]$CoreSolutionName = "SpaarkeCore",

    [string]$FeaturesSolutionName = "SpaarkeFeatures",

    [string]$PublisherPrefix = "sprk",

    [switch]$AddToSolution
)

$ErrorActionPreference = "Stop"
$DataverseUrl = $DataverseUrl.TrimEnd('/')

# ─────────────────────────────────────────────────────────────────────
# Configuration
# ─────────────────────────────────────────────────────────────────────

# Standard entities known to have sprk_ customizations
$StandardEntitiesWithCustomizations = @(
    "account", "contact", "businessunit", "systemuser",
    "email", "appointment", "task", "phonecall", "letter",
    "annotation", "connection", "connectionrole"
)

# Components that should go in SpaarkeCore (schema, platform)
$CoreComponentTypes = @(
    "Entity",              # Tables (entities)
    "Attribute",           # Columns (fields)
    "OptionSet",           # Global option sets
    "SecurityRole",        # Security roles
    "AppModule",           # Model-driven apps
    "SiteMap",             # Sitemaps
    "EnvironmentVariableDefinition",  # Env var definitions
    "EnvironmentVariableValue",       # Env var values
    "SavedQuery",          # System views
    "SystemForm",          # Forms
    "EntityRelationship",  # Relationships
    "EntityKey",           # Alternate keys
    "Chart",               # Charts
    "Dashboard",           # Dashboards
    "ConnectionRole",      # Connection roles
    "Workflow"             # Business rules / workflows
)

# Components that should go in SpaarkeFeatures (UI, behavior)
$FeatureComponentTypes = @(
    "WebResource",         # JS, HTML, CSS, images
    "CustomControl",       # PCF controls
    "RibbonCustomization", # Ribbon XML
    "CanvasApp",           # Canvas apps (if any)
    "PluginAssembly",      # Plugin DLLs
    "PluginType",          # Plugin steps
    "SDKMessageProcessingStep" # SDK steps
)

# Exclusions — solutions/components to ignore
$ExcludedSolutions = @(
    "PowerAppsToolsTemp_sprk",    # Dev tool
    "TemplatePCFImport",          # Dev scaffolding
    "SPRKMAINDEV1250801",         # Legacy snapshot
    "SPRKDOCINTELLIGENCE",        # Legacy
    "spaarke_core",               # Old version (lowercase)
    "Default",                    # Default solution
    "Cr2b7d5"                     # Common Data Services default
)

# ─────────────────────────────────────────────────────────────────────
# Authentication & API Helpers
# ─────────────────────────────────────────────────────────────────────

function Get-DataverseToken {
    param([string]$Url)

    # Try Azure CLI first
    try {
        $token = az account get-access-token --resource "$Url" --query accessToken -o tsv 2>$null
        if ($token -and $LASTEXITCODE -eq 0) {
            Write-Host "  Auth: Using Azure CLI token" -ForegroundColor Gray
            return $token
        }
    }
    catch { }

    # Fallback: try to extract from PAC CLI
    Write-Warning "Could not get token via Azure CLI. Ensure 'az login' is active and has Dataverse access."
    Write-Warning "Alternative: Run 'az login' with an account that has Dataverse System Administrator role."
    throw "Authentication failed. Run 'az login' first."
}

function Invoke-DataverseApi {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$Query,
        [switch]$AllPages
    )

    $headers = @{
        Authorization  = "Bearer $Token"
        Accept         = "application/json"
        "OData-Version" = "4.0"
        "OData-MaxVersion" = "4.0"
        Prefer         = "odata.include-annotations=*,odata.maxpagesize=5000"
    }

    $url = "$BaseUrl/api/data/v9.2/$Query"
    $allResults = @()

    do {
        try {
            $response = Invoke-RestMethod -Uri $url -Headers $headers -Method Get -ContentType "application/json"
        }
        catch {
            $statusCode = $_.Exception.Response.StatusCode.value__
            $errorBody = $_.ErrorDetails.Message
            Write-Warning "API call failed ($statusCode): $errorBody"
            Write-Warning "URL: $url"
            return @()
        }

        if ($null -ne $response.value) {
            $allResults += $response.value
        }
        elseif ($response -and ($null -eq $response.value)) {
            # Single entity response (metadata queries return object, not array)
            return @($response)
        }

        $url = $response.'@odata.nextLink'
    } while ($AllPages -and $url)

    return $allResults
}

# ─────────────────────────────────────────────────────────────────────
# Discovery Functions
# ─────────────────────────────────────────────────────────────────────

function Get-CustomEntities {
    param([string]$Token, [string]$BaseUrl, [string]$Prefix)

    Write-Host "  Discovering custom entities (${Prefix}_*)..." -ForegroundColor Cyan
    # Metadata API doesn't support startswith — fetch all custom entities and filter client-side
    $query = "EntityDefinitions?`$filter=IsCustomEntity eq true&`$select=LogicalName,SchemaName,DisplayName,EntitySetName,IsCustomEntity,IsManaged,MetadataId"
    $allEntities = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Query $query -AllPages
    $entities = $allEntities | Where-Object { $_.LogicalName -like "${Prefix}_*" }
    Write-Host "    Found: $($entities.Count) custom entities (filtered from $($allEntities.Count) total custom)" -ForegroundColor Green
    return @($entities)
}

function Get-CustomFieldsOnEntity {
    param([string]$Token, [string]$BaseUrl, [string]$Prefix, [string]$EntityLogicalName)

    # Metadata API doesn't support startswith — fetch all custom attributes and filter client-side
    $query = "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes?`$filter=IsCustomAttribute eq true&`$select=LogicalName,SchemaName,DisplayName,AttributeType,IsCustomAttribute,MetadataId"
    $allFields = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Query $query -AllPages
    $fields = $allFields | Where-Object { $_.LogicalName -like "${Prefix}_*" }
    return @($fields)
}

function Get-CustomFieldsOnStandardEntities {
    param([string]$Token, [string]$BaseUrl, [string]$Prefix, [string[]]$Entities)

    Write-Host "  Discovering custom fields on standard entities..." -ForegroundColor Cyan
    $results = @{}

    foreach ($entity in $Entities) {
        $fields = Get-CustomFieldsOnEntity -Token $Token -BaseUrl $BaseUrl -Prefix $Prefix -EntityLogicalName $entity
        if ($fields -and $fields.Count -gt 0) {
            $results[$entity] = $fields
            Write-Host "    $entity`: $($fields.Count) custom fields" -ForegroundColor Green
        }
    }

    return $results
}

function Get-WebResources {
    param([string]$Token, [string]$BaseUrl, [string]$Prefix)

    Write-Host "  Discovering web resources (${Prefix}_*)..." -ForegroundColor Cyan
    $query = "webresourceset?`$filter=startswith(name,'${Prefix}_')&`$select=name,displayname,webresourcetype,description,ismanaged&`$orderby=name"
    $resources = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Query $query -AllPages
    Write-Host "    Found: $($resources.Count) web resources" -ForegroundColor Green
    return $resources
}

function Get-SecurityRoles {
    param([string]$Token, [string]$BaseUrl, [string]$Prefix)

    Write-Host "  Discovering security roles..." -ForegroundColor Cyan
    # Filter by roles that contain 'spaarke' or 'sprk' in the name (case-insensitive)
    $query = "roles?`$filter=contains(name,'Spaarke') or contains(name,'spaarke') or contains(name,'SPRK') or contains(name,'sprk')&`$select=name,roleid,ismanaged,iscustomizable"
    $roles = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Query $query -AllPages
    Write-Host "    Found: $($roles.Count) security roles" -ForegroundColor Green
    return $roles
}

function Get-ModelDrivenApps {
    param([string]$Token, [string]$BaseUrl)

    Write-Host "  Discovering model-driven apps..." -ForegroundColor Cyan
    $query = "appmodules?`$select=name,uniquename,appmoduleid,ismanaged,description&`$filter=ismanaged eq false"
    $apps = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Query $query -AllPages
    Write-Host "    Found: $($apps.Count) model-driven apps" -ForegroundColor Green
    return $apps
}

function Get-EnvironmentVariableDefinitions {
    param([string]$Token, [string]$BaseUrl, [string]$Prefix)

    Write-Host "  Discovering environment variable definitions (${Prefix}_*)..." -ForegroundColor Cyan
    $query = "environmentvariabledefinitions?`$filter=startswith(schemaname,'${Prefix}_')&`$select=schemaname,displayname,type,defaultvalue,environmentvariabledefinitionid"
    $vars = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Query $query -AllPages
    Write-Host "    Found: $($vars.Count) environment variable definitions" -ForegroundColor Green
    return $vars
}

function Get-GlobalOptionSets {
    param([string]$Token, [string]$BaseUrl, [string]$Prefix)

    Write-Host "  Discovering global option sets (${Prefix}_*)..." -ForegroundColor Cyan
    # GlobalOptionSetDefinitions doesn't support $filter — fetch all and filter client-side
    $query = "GlobalOptionSetDefinitions?`$select=Name,DisplayName,OptionSetType,IsManaged,MetadataId"
    $allOptionSets = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Query $query -AllPages
    $optionSets = $allOptionSets | Where-Object { $_.Name -like "${Prefix}_*" }
    Write-Host "    Found: $($optionSets.Count) global option sets (filtered from $($allOptionSets.Count) total)" -ForegroundColor Green
    return @($optionSets)
}

function Get-SolutionComponents {
    param([string]$Token, [string]$BaseUrl, [string]$SolutionName)

    Write-Host "  Loading components from solution '$SolutionName'..." -ForegroundColor Cyan

    # First get the solution ID
    $query = "solutions?`$filter=uniquename eq '$SolutionName'&`$select=solutionid,uniquename,friendlyname,version"
    $solution = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Query $query

    if (-not $solution -or $solution.Count -eq 0) {
        Write-Host "    Solution '$SolutionName' not found — will be created if -AddToSolution is used." -ForegroundColor DarkYellow
        return @{ SolutionId = $null; Version = $null; Components = @() }
    }

    $sol = $solution[0]
    $solutionId = $sol.solutionid
    $solutionVersion = $sol.version
    Write-Host "    Solution ID: $solutionId (v$solutionVersion)" -ForegroundColor Gray

    if (-not $solutionId) {
        Write-Host "    No solution ID returned — treating as empty." -ForegroundColor DarkYellow
        return @{ SolutionId = $null; Version = $null; Components = @() }
    }

    # Get all components in the solution
    $query = "solutioncomponents?`$filter=_solutionid_value eq $solutionId&`$select=componenttype,objectid,rootcomponentbehavior"
    $components = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Query $query -AllPages
    Write-Host "    Components: $($components.Count)" -ForegroundColor Green

    return @{
        SolutionId = $solutionId
        Version    = $solutionVersion
        Components = $components
    }
}

function Get-Dashboards {
    param([string]$Token, [string]$BaseUrl)

    Write-Host "  Discovering custom dashboards..." -ForegroundColor Cyan
    $query = "systemforms?`$filter=type eq 0 and iscustomizable/Value eq true and ismanaged eq false&`$select=name,formid,objecttypecode,type"
    $dashboards = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Query $query -AllPages
    Write-Host "    Found: $($dashboards.Count) custom dashboards" -ForegroundColor Green
    return $dashboards
}

function Get-SiteMaps {
    param([string]$Token, [string]$BaseUrl)

    Write-Host "  Discovering sitemaps..." -ForegroundColor Cyan
    $query = "sitemaps?`$filter=ismanaged eq false&`$select=sitemapname,sitemapid"
    $sitemaps = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Query $query -AllPages
    Write-Host "    Found: $($sitemaps.Count) sitemaps" -ForegroundColor Green
    return $sitemaps
}

# ─────────────────────────────────────────────────────────────────────
# Report Generation
# ─────────────────────────────────────────────────────────────────────

function Write-AuditReport {
    param(
        [hashtable]$Results,
        [string]$Path
    )

    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $sb = [System.Text.StringBuilder]::new()

    [void]$sb.AppendLine("# Dataverse Component Audit Report")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("> **Generated**: $timestamp")
    [void]$sb.AppendLine("> **Environment**: $($Results.DataverseUrl)")
    [void]$sb.AppendLine("> **Publisher Prefix**: $($Results.PublisherPrefix)")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("---")
    [void]$sb.AppendLine("")

    # Summary
    [void]$sb.AppendLine("## Summary")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Component Type | Count | In SpaarkeCore | In SpaarkeFeatures | Orphaned |")
    [void]$sb.AppendLine("|---------------|-------|----------------|--------------------|---------:|")

    $totalOrphaned = 0
    foreach ($category in $Results.Categories.GetEnumerator() | Sort-Object Key) {
        $total = $category.Value.Total
        $inCore = $category.Value.InCore
        $inFeatures = $category.Value.InFeatures
        $orphaned = $category.Value.Orphaned
        $totalOrphaned += $orphaned
        [void]$sb.AppendLine("| $($category.Key) | $total | $inCore | $inFeatures | **$orphaned** |")
    }
    [void]$sb.AppendLine("")

    if ($totalOrphaned -gt 0) {
        [void]$sb.AppendLine("### ⚠️ $totalOrphaned orphaned components found — not in any deployable solution")
        [void]$sb.AppendLine("")
    }
    else {
        [void]$sb.AppendLine("### ✅ All components accounted for in SpaarkeCore or SpaarkeFeatures")
        [void]$sb.AppendLine("")
    }

    # Detail sections
    foreach ($section in $Results.Details) {
        [void]$sb.AppendLine("## $($section.Title)")
        [void]$sb.AppendLine("")
        if ($section.Items.Count -eq 0) {
            [void]$sb.AppendLine("_(none found)_")
        }
        else {
            [void]$sb.AppendLine("| Name | Type | Suggested Solution | Status |")
            [void]$sb.AppendLine("|------|------|-------------------|--------|")
            foreach ($item in $section.Items) {
                $status = if ($item.InSolution) { "✅ In $($item.InSolution)" } else { "⚠️ ORPHANED" }
                [void]$sb.AppendLine("| $($item.Name) | $($item.Type) | $($item.SuggestedSolution) | $status |")
            }
        }
        [void]$sb.AppendLine("")
    }

    # Recommended actions
    [void]$sb.AppendLine("## Recommended Actions")
    [void]$sb.AppendLine("")
    if ($totalOrphaned -gt 0) {
        [void]$sb.AppendLine("1. Review orphaned components above")
        [void]$sb.AppendLine("2. Run with ``-AddToSolution`` to add them to the appropriate solution:")
        [void]$sb.AppendLine("   ``````powershell")
        [void]$sb.AppendLine("   .\Audit-DataverseComponents.ps1 -DataverseUrl ""$($Results.DataverseUrl)"" -AddToSolution")
        [void]$sb.AppendLine("   ``````")
        [void]$sb.AppendLine("3. After adding, re-run the audit to verify completeness")
        [void]$sb.AppendLine("4. Export solutions:")
        [void]$sb.AppendLine("   ``````powershell")
        [void]$sb.AppendLine("   pac solution export --name SpaarkeCore --path ./exports/SpaarkeCore.zip --overwrite")
        [void]$sb.AppendLine("   pac solution export --name SpaarkeFeatures --path ./exports/SpaarkeFeatures.zip --overwrite")
        [void]$sb.AppendLine("   ``````")
    }
    else {
        [void]$sb.AppendLine("All components are accounted for. Ready to export:")
        [void]$sb.AppendLine("``````powershell")
        [void]$sb.AppendLine("pac solution export --name SpaarkeCore --path ./exports/SpaarkeCore.zip --overwrite")
        [void]$sb.AppendLine("pac solution export --name SpaarkeFeatures --path ./exports/SpaarkeFeatures.zip --overwrite")
        [void]$sb.AppendLine("``````")
    }

    $report = $sb.ToString()
    $report | Out-File -FilePath $Path -Encoding utf8
    Write-Host ""
    Write-Host "Report written to: $Path" -ForegroundColor Yellow
    return $report
}

# ─────────────────────────────────────────────────────────────────────
# Main Execution
# ─────────────────────────────────────────────────────────────────────

$StartTime = Get-Date
Write-Host ""
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Yellow
Write-Host "  Dataverse Component Audit" -ForegroundColor Yellow
Write-Host "  Environment: $DataverseUrl" -ForegroundColor Yellow
Write-Host "  Prefix: ${PublisherPrefix}_" -ForegroundColor Yellow
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Yellow
Write-Host ""

# Set default output path
if (-not $OutputPath) {
    $logsDir = Join-Path $PSScriptRoot ".." "logs"
    if (-not (Test-Path $logsDir)) { New-Item -ItemType Directory -Path $logsDir -Force | Out-Null }
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $OutputPath = Join-Path $logsDir "dataverse-audit-$timestamp.md"
}

# Step 1: Authenticate
Write-Host "[1/8] Authenticating..." -ForegroundColor Yellow
$token = Get-DataverseToken -Url $DataverseUrl

# Step 2: Load existing solution contents
Write-Host ""
Write-Host "[2/8] Loading existing solution contents..." -ForegroundColor Yellow
$coreSolution = Get-SolutionComponents -Token $token -BaseUrl $DataverseUrl -SolutionName $CoreSolutionName
$featuresSolution = Get-SolutionComponents -Token $token -BaseUrl $DataverseUrl -SolutionName $FeaturesSolutionName

# Build lookup of component IDs already in solutions
$coreComponentIds = @{}
if ($coreSolution.Components) {
    foreach ($c in $coreSolution.Components) {
        $coreComponentIds[$c.objectid] = $true
    }
}
$featuresComponentIds = @{}
if ($featuresSolution.Components) {
    foreach ($c in $featuresSolution.Components) {
        $featuresComponentIds[$c.objectid] = $true
    }
}

# Step 3: Discover custom entities
Write-Host ""
Write-Host "[3/8] Discovering custom entities..." -ForegroundColor Yellow
$customEntities = Get-CustomEntities -Token $token -BaseUrl $DataverseUrl -Prefix $PublisherPrefix

# Step 4: Discover custom fields on standard entities
Write-Host ""
Write-Host "[4/8] Discovering custom fields on standard entities..." -ForegroundColor Yellow
$standardEntityFields = Get-CustomFieldsOnStandardEntities -Token $token -BaseUrl $DataverseUrl -Prefix $PublisherPrefix -Entities $StandardEntitiesWithCustomizations

# Step 5: Discover web resources
Write-Host ""
Write-Host "[5/8] Discovering web resources, env vars, option sets..." -ForegroundColor Yellow
$webResources = Get-WebResources -Token $token -BaseUrl $DataverseUrl -Prefix $PublisherPrefix
$envVarDefs = Get-EnvironmentVariableDefinitions -Token $token -BaseUrl $DataverseUrl -Prefix $PublisherPrefix
$globalOptionSets = Get-GlobalOptionSets -Token $token -BaseUrl $DataverseUrl -Prefix $PublisherPrefix

# Step 6: Discover security roles, apps, sitemaps, dashboards
Write-Host ""
Write-Host "[6/8] Discovering security roles, apps, sitemaps..." -ForegroundColor Yellow
$securityRoles = Get-SecurityRoles -Token $token -BaseUrl $DataverseUrl -Prefix $PublisherPrefix
$modelDrivenApps = Get-ModelDrivenApps -Token $token -BaseUrl $DataverseUrl
$sitemaps = Get-SiteMaps -Token $token -BaseUrl $DataverseUrl
$dashboards = Get-Dashboards -Token $token -BaseUrl $DataverseUrl

# Step 7: Build the report
Write-Host ""
Write-Host "[7/8] Building audit report..." -ForegroundColor Yellow

$details = @()
$categories = @{}

# --- Custom Entities ---
$entityItems = @()
$coreCount = 0; $featuresCount = 0; $orphanedCount = 0
foreach ($entity in $customEntities) {
    $id = $entity.MetadataId
    $name = $entity.LogicalName
    $displayName = if ($entity.DisplayName.UserLocalizedLabel) { $entity.DisplayName.UserLocalizedLabel.Label } else { $name }
    $inSolution = $null
    if ($coreComponentIds.ContainsKey($id)) { $inSolution = "SpaarkeCore"; $coreCount++ }
    elseif ($featuresComponentIds.ContainsKey($id)) { $inSolution = "SpaarkeFeatures"; $featuresCount++ }
    else { $orphanedCount++ }

    $entityItems += @{
        Name = "$displayName ($name)"
        Type = "Entity"
        SuggestedSolution = "SpaarkeCore"
        InSolution = $inSolution
        Id = $id
    }
}
$categories["Custom Entities"] = @{ Total = $customEntities.Count; InCore = $coreCount; InFeatures = $featuresCount; Orphaned = $orphanedCount }
$details += @{ Title = "Custom Entities (${PublisherPrefix}_*)"; Items = $entityItems }

# --- Standard Entity Customizations ---
$standardItems = @()
$stdCoreCount = 0; $stdOrphanedCount = 0
foreach ($entityName in $standardEntityFields.Keys) {
    $fields = $standardEntityFields[$entityName]
    foreach ($field in $fields) {
        $id = $field.MetadataId
        $fname = $field.LogicalName
        $inSolution = $null
        if ($coreComponentIds.ContainsKey($id)) { $inSolution = "SpaarkeCore"; $stdCoreCount++ }
        else { $stdOrphanedCount++ }

        $standardItems += @{
            Name = "$entityName.$fname"
            Type = "Field on Standard Entity"
            SuggestedSolution = "SpaarkeCore"
            InSolution = $inSolution
            Id = $id
        }
    }
}
$categories["Fields on Standard Entities"] = @{ Total = $standardItems.Count; InCore = $stdCoreCount; InFeatures = 0; Orphaned = $stdOrphanedCount }
$details += @{ Title = "Custom Fields on Standard Entities"; Items = $standardItems }

# --- Web Resources ---
$wrItems = @()
$wrCoreCount = 0; $wrFeaturesCount = 0; $wrOrphanedCount = 0
foreach ($wr in $webResources) {
    $id = $wr.webresourceid
    $name = $wr.name
    $wrType = switch ($wr.webresourcetype) {
        1 { "HTML" }; 2 { "CSS" }; 3 { "JS" }; 4 { "XML" };
        5 { "PNG" }; 6 { "JPG" }; 7 { "GIF" }; 8 { "XAP" };
        9 { "XSL" }; 10 { "ICO" }; 11 { "SVG" }; 12 { "RESX" };
        default { "Type$($wr.webresourcetype)" }
    }
    $inSolution = $null
    if ($coreComponentIds.ContainsKey($id)) { $inSolution = "SpaarkeCore"; $wrCoreCount++ }
    elseif ($featuresComponentIds.ContainsKey($id)) { $inSolution = "SpaarkeFeatures"; $wrFeaturesCount++ }
    else { $wrOrphanedCount++ }

    $wrItems += @{
        Name = $name
        Type = "WebResource ($wrType)"
        SuggestedSolution = "SpaarkeFeatures"
        InSolution = $inSolution
        Id = $id
    }
}
$categories["Web Resources"] = @{ Total = $webResources.Count; InCore = $wrCoreCount; InFeatures = $wrFeaturesCount; Orphaned = $wrOrphanedCount }
$details += @{ Title = "Web Resources (${PublisherPrefix}_*)"; Items = $wrItems }

# --- Environment Variable Definitions ---
$evItems = @()
$evCoreCount = 0; $evOrphanedCount = 0
foreach ($ev in $envVarDefs) {
    $id = $ev.environmentvariabledefinitionid
    $name = $ev.schemaname
    $inSolution = $null
    if ($coreComponentIds.ContainsKey($id)) { $inSolution = "SpaarkeCore"; $evCoreCount++ }
    else { $evOrphanedCount++ }

    $evItems += @{
        Name = $name
        Type = "EnvironmentVariableDefinition"
        SuggestedSolution = "SpaarkeCore"
        InSolution = $inSolution
        Id = $id
    }
}
$categories["Environment Variables"] = @{ Total = $envVarDefs.Count; InCore = $evCoreCount; InFeatures = 0; Orphaned = $evOrphanedCount }
$details += @{ Title = "Environment Variable Definitions"; Items = $evItems }

# --- Security Roles ---
$srItems = @()
$srCoreCount = 0; $srOrphanedCount = 0
foreach ($role in $securityRoles) {
    $id = $role.roleid
    $name = $role.name
    $inSolution = $null
    if ($coreComponentIds.ContainsKey($id)) { $inSolution = "SpaarkeCore"; $srCoreCount++ }
    else { $srOrphanedCount++ }

    $srItems += @{
        Name = $name
        Type = "SecurityRole"
        SuggestedSolution = "SpaarkeCore"
        InSolution = $inSolution
        Id = $id
    }
}
$categories["Security Roles"] = @{ Total = $securityRoles.Count; InCore = $srCoreCount; InFeatures = 0; Orphaned = $srOrphanedCount }
$details += @{ Title = "Security Roles"; Items = $srItems }

# --- Model-Driven Apps ---
$appItems = @()
$appCoreCount = 0; $appOrphanedCount = 0
foreach ($app in $modelDrivenApps) {
    $id = $app.appmoduleid
    $name = $app.name
    $inSolution = $null
    if ($coreComponentIds.ContainsKey($id)) { $inSolution = "SpaarkeCore"; $appCoreCount++ }
    else { $appOrphanedCount++ }

    $appItems += @{
        Name = $name
        Type = "AppModule"
        SuggestedSolution = "SpaarkeCore"
        InSolution = $inSolution
        Id = $id
    }
}
$categories["Model-Driven Apps"] = @{ Total = $modelDrivenApps.Count; InCore = $appCoreCount; InFeatures = 0; Orphaned = $appOrphanedCount }
$details += @{ Title = "Model-Driven Apps"; Items = $appItems }

# --- Global Option Sets ---
$osItems = @()
$osCoreCount = 0; $osOrphanedCount = 0
foreach ($os in $globalOptionSets) {
    $id = $os.MetadataId
    $name = $os.Name
    $inSolution = $null
    if ($coreComponentIds.ContainsKey($id)) { $inSolution = "SpaarkeCore"; $osCoreCount++ }
    else { $osOrphanedCount++ }

    $osItems += @{
        Name = $name
        Type = "GlobalOptionSet"
        SuggestedSolution = "SpaarkeCore"
        InSolution = $inSolution
        Id = $id
    }
}
$categories["Global Option Sets"] = @{ Total = $globalOptionSets.Count; InCore = $osCoreCount; InFeatures = 0; Orphaned = $osOrphanedCount }
$details += @{ Title = "Global Option Sets"; Items = $osItems }

# Build results
$results = @{
    DataverseUrl    = $DataverseUrl
    PublisherPrefix = $PublisherPrefix
    Categories      = $categories
    Details         = $details
    CoreSolution    = $coreSolution
    FeaturesSolution = $featuresSolution
}

# Write report
$report = Write-AuditReport -Results $results -Path $OutputPath

# Step 8: Print summary
Write-Host ""
Write-Host "[8/8] Audit Complete" -ForegroundColor Yellow
Write-Host "═══════════════════════════════════════════════════════" -ForegroundColor Yellow
Write-Host ""

$totalComponents = 0
$totalOrphaned = 0
foreach ($cat in $categories.GetEnumerator()) {
    $totalComponents += $cat.Value.Total
    $totalOrphaned += $cat.Value.Orphaned
}

Write-Host "  Total components discovered: $totalComponents" -ForegroundColor White
Write-Host "  In SpaarkeCore:              $($categories.Values | ForEach-Object { $_.InCore } | Measure-Object -Sum | Select-Object -ExpandProperty Sum)" -ForegroundColor Green
Write-Host "  In SpaarkeFeatures:          $($categories.Values | ForEach-Object { $_.InFeatures } | Measure-Object -Sum | Select-Object -ExpandProperty Sum)" -ForegroundColor Green
Write-Host "  ORPHANED (not in solution):  $totalOrphaned" -ForegroundColor $(if ($totalOrphaned -gt 0) { "Red" } else { "Green" })
Write-Host ""

$elapsed = (Get-Date) - $StartTime
Write-Host "  Duration: $($elapsed.TotalSeconds.ToString('F1'))s" -ForegroundColor Gray
Write-Host "  Report:   $OutputPath" -ForegroundColor Gray
Write-Host ""

if ($totalOrphaned -gt 0 -and -not $AddToSolution) {
    Write-Host "  ⚠️  Run with -AddToSolution to add orphaned components to appropriate solutions." -ForegroundColor DarkYellow
    Write-Host ""
}

# Return results for pipeline use
return $results
