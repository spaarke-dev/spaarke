<#
.SYNOPSIS
    Creates the sprk_aichatcontextmap entity in Dataverse.

.DESCRIPTION
    Creates the sprk_aichatcontextmap entity, its local option set (sprk_pagetype),
    all attributes, and the lookup relationship to sprk_analysisplaybook using the
    Dataverse Metadata Web API. Requires Azure CLI authentication.

.PARAMETER EnvironmentUrl
    The Dataverse environment URL (e.g., https://spaarkedev1.crm.dynamics.com)

.PARAMETER SolutionName
    The Dataverse solution to add the entity to (default: spaarke_core)

.EXAMPLE
    .\Create-AiChatContextMapEntity.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

.NOTES
    Project: AI SprkChat Context Awareness
    Task: 003 - Create sprk_aichatcontextmap entity
    Created: 2026-03-15
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com",

    [Parameter(Mandatory = $false)]
    [string]$SolutionName = "spaarke_core"
)

$ErrorActionPreference = "Stop"

# ============================================================================
# Helper Functions
# ============================================================================

function Get-DataverseToken {
    param([string]$EnvironmentUrl)

    Write-Host "Getting authentication token from Azure CLI..." -ForegroundColor Cyan

    $tokenResult = az account get-access-token --resource $EnvironmentUrl --query "accessToken" -o tsv 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get token from Azure CLI. Error: $tokenResult. Make sure you're logged in with 'az login'"
    }

    return $tokenResult.Trim()
}

function Invoke-DataverseApi {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null
    )

    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Content-Type"     = "application/json; charset=utf-8"
        "Prefer"           = "odata.include-annotations=*"
    }

    $uri = "$BaseUrl/api/data/v9.2/$Endpoint"

    $params = @{
        Uri     = $uri
        Method  = $Method
        Headers = $headers
    }

    if ($Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 20)
    }

    try {
        $response = Invoke-RestMethod @params
        return $response
    }
    catch {
        $errorDetails = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $errorJson = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($errorJson.error.message) {
                $errorDetails = $errorJson.error.message
            }
        }
        throw "API Error ($Method $Endpoint): $errorDetails"
    }
}

function New-Label {
    param([string]$Text)
    return @{
        "@odata.type"     = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(
            @{
                "@odata.type"  = "Microsoft.Dynamics.CRM.LocalizedLabel"
                "Label"        = $Text
                "LanguageCode" = 1033
            }
        )
    }
}

function Test-EntityExists {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$LogicalName
    )

    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$LogicalName')" -Method "GET" | Out-Null
        return $true
    }
    catch {
        if ($_.Exception.Message -match "does not exist|404") {
            return $false
        }
        throw
    }
}

function Test-AttributeExists {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$EntityLogicalName,
        [string]$AttributeLogicalName
    )

    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')" -Method "GET" | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Add-EntityAttribute {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$EntityLogicalName,
        [object]$AttributeDef
    )

    $schemaName = $AttributeDef.SchemaName
    Write-Host "  Adding attribute: $schemaName..." -ForegroundColor Gray

    if (Test-AttributeExists -Token $Token -BaseUrl $BaseUrl -EntityLogicalName $EntityLogicalName -AttributeLogicalName $schemaName.ToLower()) {
        Write-Host "    Already exists, skipping." -ForegroundColor Yellow
        return
    }

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" -Method "POST" -Body $AttributeDef

    Write-Host "    Added: $schemaName" -ForegroundColor Green
}

# ============================================================================
# Main Execution
# ============================================================================

function Main {
    Write-Host ""
    Write-Host "========================================================" -ForegroundColor Cyan
    Write-Host " Deploy sprk_aichatcontextmap Entity" -ForegroundColor Cyan
    Write-Host "========================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Yellow
    Write-Host "Solution:    $SolutionName" -ForegroundColor Yellow
    Write-Host ""

    # --- Authentication ---
    $token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
    Write-Host "Authentication successful" -ForegroundColor Green
    Write-Host ""

    # --- Step 1: Check if entity already exists ---
    Write-Host "Step 1: Checking if entity exists..." -ForegroundColor Cyan
    $entityExists = Test-EntityExists -Token $token -BaseUrl $EnvironmentUrl -LogicalName "sprk_aichatcontextmap"
    if ($entityExists) {
        Write-Host "  Entity sprk_aichatcontextmap already exists!" -ForegroundColor Yellow
        Write-Host "  Will verify and add any missing attributes." -ForegroundColor Yellow
    }

    # --- Step 2: Verify lookup target exists ---
    Write-Host ""
    Write-Host "Step 2: Verifying lookup target sprk_analysisplaybook exists..." -ForegroundColor Cyan
    if (-not (Test-EntityExists -Token $token -BaseUrl $EnvironmentUrl -LogicalName "sprk_analysisplaybook")) {
        Write-Host "  WARNING: sprk_analysisplaybook entity does not exist!" -ForegroundColor Red
        Write-Host "  The lookup relationship will fail. Create sprk_analysisplaybook first." -ForegroundColor Red
        Write-Host "  Continuing with entity and non-lookup attributes..." -ForegroundColor Yellow
        $skipLookup = $true
    }
    else {
        Write-Host "  sprk_analysisplaybook exists." -ForegroundColor Green
        $skipLookup = $false
    }

    # --- Step 3: Create entity ---
    if (-not $entityExists) {
        Write-Host ""
        Write-Host "Step 3: Creating sprk_aichatcontextmap entity..." -ForegroundColor Cyan

        $entityDef = @{
            "@odata.type"           = "Microsoft.Dynamics.CRM.EntityMetadata"
            "SchemaName"            = "sprk_aichatcontextmap"
            "DisplayName"           = New-Label -Text "AI Chat Context Map"
            "DisplayCollectionName" = New-Label -Text "AI Chat Context Maps"
            "Description"           = New-Label -Text "Maps entity type + page type combinations to AI playbooks for SprkChat context awareness"
            "OwnershipType"         = "OrganizationOwned"
            "IsActivity"            = $false
            "HasNotes"              = $false
            "HasActivities"         = $false
            "PrimaryNameAttribute"  = "sprk_name"
            "Attributes"            = @(
                @{
                    "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                    "SchemaName"    = "sprk_name"
                    "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
                    "MaxLength"     = 200
                    "DisplayName"   = New-Label -Text "Name"
                    "Description"   = New-Label -Text "Display name for the mapping"
                    "IsPrimaryName" = $true
                }
            )
        }

        Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "EntityDefinitions" -Method "POST" -Body $entityDef

        Write-Host "  Entity created successfully" -ForegroundColor Green
    }

    # --- Step 4: Add attributes ---
    Write-Host ""
    Write-Host "Step 4: Adding attributes..." -ForegroundColor Cyan

    # 4a. sprk_entitytype (String, required)
    Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_aichatcontextmap" -AttributeDef @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_entitytype"
        "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
        "MaxLength"     = 200
        "DisplayName"   = New-Label -Text "Entity Type"
        "Description"   = New-Label -Text "Dataverse entity logical name (e.g., sprk_matter, sprk_project, * for wildcard)"
    }

    # 4b. sprk_pagetype (Local Picklist)
    Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_aichatcontextmap" -AttributeDef @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        "SchemaName"    = "sprk_pagetype"
        "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
        "DisplayName"   = New-Label -Text "Page Type"
        "Description"   = New-Label -Text "The type of page where this mapping applies"
        "OptionSet"     = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
            "IsGlobal"      = $false
            "OptionSetType" = "Picklist"
            "Options"       = @(
                @{ "Value" = 100000000; "Label" = New-Label -Text "entityrecord" }
                @{ "Value" = 100000001; "Label" = New-Label -Text "entitylist" }
                @{ "Value" = 100000002; "Label" = New-Label -Text "dashboard" }
                @{ "Value" = 100000003; "Label" = New-Label -Text "webresource" }
                @{ "Value" = 100000004; "Label" = New-Label -Text "custom" }
                @{ "Value" = 100000005; "Label" = New-Label -Text "any" }
            )
        }
    }

    # 4c. sprk_sortorder (Integer)
    Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_aichatcontextmap" -AttributeDef @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
        "SchemaName"    = "sprk_sortorder"
        "RequiredLevel" = @{ "Value" = "None" }
        "MinValue"      = 0
        "MaxValue"      = 10000
        "DisplayName"   = New-Label -Text "Sort Order"
        "Description"   = New-Label -Text "Priority within tier (lower = higher priority)"
    }

    # 4d. sprk_isdefault (Boolean)
    Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_aichatcontextmap" -AttributeDef @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
        "SchemaName"    = "sprk_isdefault"
        "RequiredLevel" = @{ "Value" = "None" }
        "DisplayName"   = New-Label -Text "Is Default"
        "Description"   = New-Label -Text "Whether this is the default playbook for this context"
        "OptionSet"     = @{
            "TrueOption"  = @{ "Value" = 1; "Label" = New-Label -Text "Yes" }
            "FalseOption" = @{ "Value" = 0; "Label" = New-Label -Text "No" }
        }
    }

    # 4e. sprk_description (Memo/Multiline text)
    Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_aichatcontextmap" -AttributeDef @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "SchemaName"    = "sprk_description"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 10000
        "DisplayName"   = New-Label -Text "Description"
        "Description"   = New-Label -Text "Admin description of this mapping"
    }

    # 4f. sprk_isactive (Boolean)
    Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_aichatcontextmap" -AttributeDef @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
        "SchemaName"    = "sprk_isactive"
        "RequiredLevel" = @{ "Value" = "None" }
        "DisplayName"   = New-Label -Text "Is Active"
        "Description"   = New-Label -Text "Whether this mapping is active"
        "OptionSet"     = @{
            "TrueOption"  = @{ "Value" = 1; "Label" = New-Label -Text "Yes" }
            "FalseOption" = @{ "Value" = 0; "Label" = New-Label -Text "No" }
        }
    }

    # --- Step 5: Create lookup relationship to sprk_analysisplaybook ---
    Write-Host ""
    Write-Host "Step 5: Creating lookup relationship to sprk_analysisplaybook..." -ForegroundColor Cyan

    if ($skipLookup) {
        Write-Host "  SKIPPED: sprk_analysisplaybook does not exist yet." -ForegroundColor Yellow
        Write-Host "  Re-run this script after creating sprk_analysisplaybook to add the lookup." -ForegroundColor Yellow
    }
    else {
        # Check if lookup attribute already exists
        if (Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl `
                -EntityLogicalName "sprk_aichatcontextmap" -AttributeLogicalName "sprk_playbookid") {
            Write-Host "  Lookup sprk_playbookid already exists, skipping." -ForegroundColor Yellow
        }
        else {
            $relationshipDef = @{
                "@odata.type"          = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
                "SchemaName"           = "sprk_analysisplaybook_chatcontextmap_playbookid"
                "ReferencedEntity"     = "sprk_analysisplaybook"
                "ReferencingEntity"    = "sprk_aichatcontextmap"
                "CascadeConfiguration" = @{
                    "Assign"   = "NoCascade"
                    "Delete"   = "Restrict"
                    "Merge"    = "NoCascade"
                    "Reparent" = "NoCascade"
                    "Share"    = "NoCascade"
                    "Unshare"  = "NoCascade"
                }
                "Lookup"               = @{
                    "SchemaName"    = "sprk_playbookid"
                    "DisplayName"   = New-Label -Text "Playbook"
                    "Description"   = New-Label -Text "The AI playbook to use for this context"
                    "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
                }
            }

            Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
                -Endpoint "RelationshipDefinitions" -Method "POST" -Body $relationshipDef

            Write-Host "  Lookup sprk_playbookid created successfully" -ForegroundColor Green
        }
    }

    # --- Step 6: Publish customizations ---
    Write-Host ""
    Write-Host "Step 6: Publishing customizations..." -ForegroundColor Cyan

    $publishXml = @{
        "ParameterXml" = "<importexportxml><entities><entity>sprk_aichatcontextmap</entity></entities></importexportxml>"
    }

    try {
        Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "PublishXml" -Method "POST" -Body $publishXml
        Write-Host "  Customizations published" -ForegroundColor Green
    }
    catch {
        Write-Host "  Warning: Publish may have timed out, but entity should be available" -ForegroundColor Yellow
    }

    # --- Step 7: Add entity to solution ---
    Write-Host ""
    Write-Host "Step 7: Adding entity to solution '$SolutionName'..." -ForegroundColor Cyan

    try {
        $addToSolution = @{
            "ComponentId"   = (Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
                    -Endpoint "EntityDefinitions(LogicalName='sprk_aichatcontextmap')?`$select=MetadataId" -Method "GET").MetadataId
            "ComponentType" = 1  # Entity
            "SolutionUniqueName" = $SolutionName
            "AddRequiredComponents" = $false
        }

        Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "AddSolutionComponent" -Method "POST" -Body $addToSolution

        Write-Host "  Entity added to solution '$SolutionName'" -ForegroundColor Green
    }
    catch {
        Write-Host "  Warning: Could not add to solution. Error: $($_.Exception.Message)" -ForegroundColor Yellow
        Write-Host "  You may need to add it manually via the Maker Portal." -ForegroundColor Yellow
    }

    # --- Step 8: Verify ---
    Write-Host ""
    Write-Host "Step 8: Verifying entity..." -ForegroundColor Cyan

    if (Test-EntityExists -Token $token -BaseUrl $EnvironmentUrl -LogicalName "sprk_aichatcontextmap") {
        Write-Host "  Entity sprk_aichatcontextmap exists and is accessible" -ForegroundColor Green
    }
    else {
        Write-Host "  Warning: Entity verification failed" -ForegroundColor Red
    }

    # Test Web API query
    try {
        $result = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "sprk_aichatcontextmaps" -Method "GET"
        Write-Host "  Web API query successful - record count: $($result.value.Count)" -ForegroundColor Green
    }
    catch {
        Write-Host "  Warning: Web API query failed - entity may need publishing" -ForegroundColor Yellow
    }

    # --- Summary ---
    Write-Host ""
    Write-Host "========================================================" -ForegroundColor Green
    Write-Host " Deployment Complete!" -ForegroundColor Green
    Write-Host "========================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Entity: sprk_aichatcontextmap (AI Chat Context Map)" -ForegroundColor White
    Write-Host ""
    Write-Host "Fields created:" -ForegroundColor White
    Write-Host "  - sprk_name          (string, primary name, required)" -ForegroundColor Gray
    Write-Host "  - sprk_entitytype    (string, required)" -ForegroundColor Gray
    Write-Host "  - sprk_pagetype      (choice: entityrecord/entitylist/dashboard/webresource/custom/any)" -ForegroundColor Gray
    Write-Host "  - sprk_playbookid    (lookup -> sprk_analysisplaybook, required)" -ForegroundColor Gray
    Write-Host "  - sprk_sortorder     (integer, 0-10000)" -ForegroundColor Gray
    Write-Host "  - sprk_isdefault     (boolean)" -ForegroundColor Gray
    Write-Host "  - sprk_description   (multiline text)" -ForegroundColor Gray
    Write-Host "  - sprk_isactive      (boolean)" -ForegroundColor Gray
    Write-Host ""

    if ($skipLookup) {
        Write-Host "ACTION REQUIRED:" -ForegroundColor Red
        Write-Host "  Lookup sprk_playbookid was SKIPPED because sprk_analysisplaybook does not exist." -ForegroundColor Yellow
        Write-Host "  After creating sprk_analysisplaybook, re-run this script to add the lookup." -ForegroundColor Yellow
        Write-Host ""
    }

    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  1. Verify in Power Apps Maker Portal: $EnvironmentUrl" -ForegroundColor Gray
    Write-Host "  2. Create seed records for default context mappings" -ForegroundColor Gray
    Write-Host "  3. Add security roles if needed" -ForegroundColor Gray
    Write-Host ""
}

# Run main
Main
