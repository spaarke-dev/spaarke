<#
.SYNOPSIS
    Deploys the node-based playbook schema to Dataverse.

.DESCRIPTION
    Creates all entities, option sets, fields, and relationships for the
    AI Node-Based Playbook Builder feature using the Dataverse Metadata Web API.
    Requires Azure CLI authentication.

.PARAMETER EnvironmentUrl
    The Dataverse environment URL (e.g., https://spaarkedev1.crm.dynamics.com)

.EXAMPLE
    .\Deploy-PlaybookNodeSchema.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

.NOTES
    Project: ai-node-playbook-builder
    Task: 009 - Phase 1 Tests and Deployment
    Created: 2026-01-09
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = "Stop"

#region Helper Functions

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

function Test-EntityExists {
    param([string]$Token, [string]$BaseUrl, [string]$LogicalName)
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "EntityDefinitions(LogicalName='$LogicalName')" -Method "GET" | Out-Null
        return $true
    }
    catch {
        if ($_.Exception.Message -match "does not exist|404|0x80060888") { return $false }
        throw
    }
}

function Get-GlobalOptionSet {
    param([string]$Token, [string]$BaseUrl, [string]$Name)
    try {
        return Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "GlobalOptionSetDefinitions(Name='$Name')" -Method "GET"
    }
    catch { return $null }
}

function Test-GlobalOptionSetExists {
    param([string]$Token, [string]$BaseUrl, [string]$Name)
    return $null -ne (Get-GlobalOptionSet -Token $Token -BaseUrl $BaseUrl -Name $Name)
}

function Test-AttributeExists {
    param([string]$Token, [string]$BaseUrl, [string]$EntityLogicalName, [string]$AttributeLogicalName)
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')" -Method "GET" | Out-Null
        return $true
    }
    catch { return $false }
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

function New-OptionValue {
    param([int]$Value, [string]$Label)
    return @{
        "Value" = $Value
        "Label" = New-Label -Text $Label
    }
}

function New-GlobalOptionSet {
    param(
        [string]$Token, [string]$BaseUrl, [string]$Name, [string]$DisplayName, [array]$Options
    )
    Write-Host "  Creating global option set: $Name..." -ForegroundColor Gray
    $optionSetDef = @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
        "Name"          = $Name
        "DisplayName"   = New-Label -Text $DisplayName
        "IsGlobal"      = $true
        "OptionSetType" = "Picklist"
        "Options"       = $Options
    }
    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "GlobalOptionSetDefinitions" -Method "POST" -Body $optionSetDef
    Write-Host "    Created: $Name" -ForegroundColor Green
}

function Add-EntityAttribute {
    param([string]$Token, [string]$BaseUrl, [string]$EntityLogicalName, [object]$AttributeDef)
    $schemaName = $AttributeDef.SchemaName
    Write-Host "  Adding attribute: $schemaName to $EntityLogicalName..." -ForegroundColor Gray
    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" -Method "POST" -Body $AttributeDef
    Write-Host "    Added: $schemaName" -ForegroundColor Green
}

function New-Entity {
    param(
        [string]$Token, [string]$BaseUrl, [string]$SchemaName, [string]$DisplayName,
        [string]$PluralName, [string]$Description, [string]$PrimaryColumn = "sprk_name",
        [int]$PrimaryColumnMaxLength = 200, [bool]$IsAutoNumber = $false
    )
    Write-Host "Creating entity: $SchemaName..." -ForegroundColor Yellow

    $primaryAttr = @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = $PrimaryColumn
        "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
        "MaxLength"     = $PrimaryColumnMaxLength
        "DisplayName"   = New-Label -Text "Name"
        "Description"   = New-Label -Text "Primary name field"
        "IsPrimaryName" = $true
    }

    if ($IsAutoNumber) {
        $primaryAttr["AutoNumberFormat"] = "RUN-{SEQNUM:6}"
    }

    $entityDef = @{
        "@odata.type"           = "Microsoft.Dynamics.CRM.EntityMetadata"
        "SchemaName"            = $SchemaName
        "DisplayName"           = New-Label -Text $DisplayName
        "DisplayCollectionName" = New-Label -Text $PluralName
        "Description"           = New-Label -Text $Description
        "OwnershipType"         = "OrganizationOwned"
        "IsActivity"            = $false
        "HasNotes"              = $false
        "HasActivities"         = $false
        "PrimaryNameAttribute"  = $PrimaryColumn
        "Attributes"            = @($primaryAttr)
    }

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "EntityDefinitions" -Method "POST" -Body $entityDef
    Write-Host "  Entity created: $SchemaName" -ForegroundColor Green
}

function New-StringAttribute {
    param([string]$SchemaName, [string]$DisplayName, [string]$Description, [int]$MaxLength = 200, [bool]$Required = $false)
    return @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = $SchemaName
        "RequiredLevel" = @{ "Value" = if ($Required) { "ApplicationRequired" } else { "None" } }
        "MaxLength"     = $MaxLength
        "DisplayName"   = New-Label -Text $DisplayName
        "Description"   = New-Label -Text $Description
    }
}

function New-MemoAttribute {
    param([string]$SchemaName, [string]$DisplayName, [string]$Description, [int]$MaxLength = 100000)
    return @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "SchemaName"    = $SchemaName
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = $MaxLength
        "DisplayName"   = New-Label -Text $DisplayName
        "Description"   = New-Label -Text $Description
    }
}

function New-IntegerAttribute {
    param([string]$SchemaName, [string]$DisplayName, [string]$Description, [bool]$Required = $false)
    return @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
        "SchemaName"    = $SchemaName
        "RequiredLevel" = @{ "Value" = if ($Required) { "ApplicationRequired" } else { "None" } }
        "DisplayName"   = New-Label -Text $DisplayName
        "Description"   = New-Label -Text $Description
        "MinValue"      = -2147483648
        "MaxValue"      = 2147483647
    }
}

function New-BooleanAttribute {
    param([string]$SchemaName, [string]$DisplayName, [string]$Description, [bool]$Default = $false)
    return @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
        "SchemaName"    = $SchemaName
        "RequiredLevel" = @{ "Value" = "None" }
        "DisplayName"   = New-Label -Text $DisplayName
        "Description"   = New-Label -Text $Description
    }
}

function New-PicklistAttribute {
    param([string]$SchemaName, [string]$DisplayName, [string]$Description, [string]$GlobalOptionSetId, [bool]$Required = $false)
    return @{
        "@odata.type"                = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        "SchemaName"                 = $SchemaName
        "RequiredLevel"              = @{ "Value" = if ($Required) { "ApplicationRequired" } else { "None" } }
        "DisplayName"                = New-Label -Text $DisplayName
        "Description"                = New-Label -Text $Description
        "GlobalOptionSet@odata.bind" = "/GlobalOptionSetDefinitions($GlobalOptionSetId)"
    }
}

function New-DateTimeAttribute {
    param([string]$SchemaName, [string]$DisplayName, [string]$Description)
    return @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
        "SchemaName"    = $SchemaName
        "RequiredLevel" = @{ "Value" = "None" }
        "DisplayName"   = New-Label -Text $DisplayName
        "Description"   = New-Label -Text $Description
        "Format"        = "DateAndTime"
    }
}

function New-OneToManyRelationship {
    param(
        [string]$Token, [string]$BaseUrl,
        [string]$ReferencedEntity, [string]$ReferencingEntity,
        [string]$LookupSchemaName, [string]$LookupDisplayName, [string]$LookupDescription,
        [bool]$Required = $false
    )

    Write-Host "  Creating relationship: $ReferencingEntity -> $ReferencedEntity ($LookupSchemaName)..." -ForegroundColor Gray

    $relationshipDef = @{
        "@odata.type"               = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
        "SchemaName"                = "sprk_$($ReferencedEntity)_$($ReferencingEntity)_$($LookupSchemaName -replace 'sprk_', '')"
        "ReferencedEntity"          = $ReferencedEntity
        "ReferencingEntity"         = $ReferencingEntity
        "CascadeConfiguration"      = @{
            "Assign"   = "NoCascade"
            "Delete"   = "RemoveLink"
            "Merge"    = "NoCascade"
            "Reparent" = "NoCascade"
            "Share"    = "NoCascade"
            "Unshare"  = "NoCascade"
        }
        "Lookup"                    = @{
            "SchemaName"    = $LookupSchemaName
            "DisplayName"   = New-Label -Text $LookupDisplayName
            "Description"   = New-Label -Text $LookupDescription
            "RequiredLevel" = @{ "Value" = if ($Required) { "ApplicationRequired" } else { "None" } }
        }
    }

    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "RelationshipDefinitions" -Method "POST" -Body $relationshipDef
        Write-Host "    Created: $LookupSchemaName" -ForegroundColor Green
        return $true
    }
    catch {
        if ($_.Exception.Message -match "already exists|duplicate") {
            Write-Host "    $LookupSchemaName already exists, skipping..." -ForegroundColor Yellow
            return $true
        }
        Write-Host "    Error creating $LookupSchemaName : $_" -ForegroundColor Red
        return $false
    }
}

#endregion

#region Schema Definitions

function Deploy-Phase1-OptionSets {
    param([string]$Token, [string]$BaseUrl)

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " Phase 1: Creating Global Option Sets" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    # sprk_playbookmode
    if (-not (Test-GlobalOptionSetExists -Token $Token -BaseUrl $BaseUrl -Name "sprk_playbookmode")) {
        New-GlobalOptionSet -Token $Token -BaseUrl $BaseUrl -Name "sprk_playbookmode" -DisplayName "Playbook Mode" -Options @(
            (New-OptionValue -Value 0 -Label "Legacy")
            (New-OptionValue -Value 1 -Label "NodeBased")
        )
    } else { Write-Host "  sprk_playbookmode already exists, skipping..." -ForegroundColor Yellow }

    # sprk_playbooktype
    if (-not (Test-GlobalOptionSetExists -Token $Token -BaseUrl $BaseUrl -Name "sprk_playbooktype")) {
        New-GlobalOptionSet -Token $Token -BaseUrl $BaseUrl -Name "sprk_playbooktype" -DisplayName "Playbook Type" -Options @(
            (New-OptionValue -Value 0 -Label "AiAnalysis")
            (New-OptionValue -Value 1 -Label "Workflow")
            (New-OptionValue -Value 2 -Label "Hybrid")
        )
    } else { Write-Host "  sprk_playbooktype already exists, skipping..." -ForegroundColor Yellow }

    # sprk_triggertype
    if (-not (Test-GlobalOptionSetExists -Token $Token -BaseUrl $BaseUrl -Name "sprk_triggertype")) {
        New-GlobalOptionSet -Token $Token -BaseUrl $BaseUrl -Name "sprk_triggertype" -DisplayName "Trigger Type" -Options @(
            (New-OptionValue -Value 0 -Label "Manual")
            (New-OptionValue -Value 1 -Label "Scheduled")
            (New-OptionValue -Value 2 -Label "RecordCreated")
            (New-OptionValue -Value 3 -Label "RecordUpdated")
        )
    } else { Write-Host "  sprk_triggertype already exists, skipping..." -ForegroundColor Yellow }

    # sprk_outputformat
    if (-not (Test-GlobalOptionSetExists -Token $Token -BaseUrl $BaseUrl -Name "sprk_outputformat")) {
        New-GlobalOptionSet -Token $Token -BaseUrl $BaseUrl -Name "sprk_outputformat" -DisplayName "Output Format" -Options @(
            (New-OptionValue -Value 0 -Label "JSON")
            (New-OptionValue -Value 1 -Label "Markdown")
            (New-OptionValue -Value 2 -Label "PlainText")
        )
    } else { Write-Host "  sprk_outputformat already exists, skipping..." -ForegroundColor Yellow }

    # sprk_aiprovider
    if (-not (Test-GlobalOptionSetExists -Token $Token -BaseUrl $BaseUrl -Name "sprk_aiprovider")) {
        New-GlobalOptionSet -Token $Token -BaseUrl $BaseUrl -Name "sprk_aiprovider" -DisplayName "AI Provider" -Options @(
            (New-OptionValue -Value 0 -Label "AzureOpenAI")
            (New-OptionValue -Value 1 -Label "OpenAI")
            (New-OptionValue -Value 2 -Label "Anthropic")
        )
    } else { Write-Host "  sprk_aiprovider already exists, skipping..." -ForegroundColor Yellow }

    # sprk_aicapability
    if (-not (Test-GlobalOptionSetExists -Token $Token -BaseUrl $BaseUrl -Name "sprk_aicapability")) {
        New-GlobalOptionSet -Token $Token -BaseUrl $BaseUrl -Name "sprk_aicapability" -DisplayName "AI Capability" -Options @(
            (New-OptionValue -Value 0 -Label "Chat")
            (New-OptionValue -Value 1 -Label "Completion")
            (New-OptionValue -Value 2 -Label "Embedding")
        )
    } else { Write-Host "  sprk_aicapability already exists, skipping..." -ForegroundColor Yellow }

    # sprk_playbookrunstatus
    if (-not (Test-GlobalOptionSetExists -Token $Token -BaseUrl $BaseUrl -Name "sprk_playbookrunstatus")) {
        New-GlobalOptionSet -Token $Token -BaseUrl $BaseUrl -Name "sprk_playbookrunstatus" -DisplayName "Playbook Run Status" -Options @(
            (New-OptionValue -Value 0 -Label "Pending")
            (New-OptionValue -Value 1 -Label "Running")
            (New-OptionValue -Value 2 -Label "Completed")
            (New-OptionValue -Value 3 -Label "Failed")
            (New-OptionValue -Value 4 -Label "Cancelled")
        )
    } else { Write-Host "  sprk_playbookrunstatus already exists, skipping..." -ForegroundColor Yellow }

    # sprk_noderunstatus
    if (-not (Test-GlobalOptionSetExists -Token $Token -BaseUrl $BaseUrl -Name "sprk_noderunstatus")) {
        New-GlobalOptionSet -Token $Token -BaseUrl $BaseUrl -Name "sprk_noderunstatus" -DisplayName "Node Run Status" -Options @(
            (New-OptionValue -Value 0 -Label "Pending")
            (New-OptionValue -Value 1 -Label "Running")
            (New-OptionValue -Value 2 -Label "Completed")
            (New-OptionValue -Value 3 -Label "Failed")
            (New-OptionValue -Value 4 -Label "Skipped")
        )
    } else { Write-Host "  sprk_noderunstatus already exists, skipping..." -ForegroundColor Yellow }

    Write-Host "Phase 1 complete: Option sets created" -ForegroundColor Green
}

function Deploy-Phase2-LookupEntities {
    param([string]$Token, [string]$BaseUrl)

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " Phase 2: Creating Lookup Entities" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    # sprk_aiactiontype
    if (-not (Test-EntityExists -Token $Token -BaseUrl $BaseUrl -LogicalName "sprk_aiactiontype")) {
        New-Entity -Token $Token -BaseUrl $BaseUrl -SchemaName "sprk_aiactiontype" `
            -DisplayName "AI Action Type" -PluralName "AI Action Types" `
            -Description "Types of actions that nodes can perform"

        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_aiactiontype" `
            -AttributeDef (New-IntegerAttribute -SchemaName "sprk_value" -DisplayName "Value" -Description "Numeric value for the action type" -Required $true)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_aiactiontype" `
            -AttributeDef (New-MemoAttribute -SchemaName "sprk_description" -DisplayName "Description" -Description "Description of the action type" -MaxLength 2000)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_aiactiontype" `
            -AttributeDef (New-BooleanAttribute -SchemaName "sprk_isactive" -DisplayName "Is Active" -Description "Whether this action type is active" -Default $true)
    } else { Write-Host "  sprk_aiactiontype already exists, skipping..." -ForegroundColor Yellow }

    # sprk_analysisdeliverytype
    if (-not (Test-EntityExists -Token $Token -BaseUrl $BaseUrl -LogicalName "sprk_analysisdeliverytype")) {
        New-Entity -Token $Token -BaseUrl $BaseUrl -SchemaName "sprk_analysisdeliverytype" `
            -DisplayName "Analysis Delivery Type" -PluralName "Analysis Delivery Types" `
            -Description "Types of delivery outputs for analysis results"

        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_analysisdeliverytype" `
            -AttributeDef (New-IntegerAttribute -SchemaName "sprk_value" -DisplayName "Value" -Description "Numeric value for the delivery type" -Required $true)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_analysisdeliverytype" `
            -AttributeDef (New-MemoAttribute -SchemaName "sprk_description" -DisplayName "Description" -Description "Description of the delivery type" -MaxLength 2000)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_analysisdeliverytype" `
            -AttributeDef (New-BooleanAttribute -SchemaName "sprk_isactive" -DisplayName "Is Active" -Description "Whether this delivery type is active" -Default $true)
    } else { Write-Host "  sprk_analysisdeliverytype already exists, skipping..." -ForegroundColor Yellow }

    # sprk_aimodeldeployment
    if (-not (Test-EntityExists -Token $Token -BaseUrl $BaseUrl -LogicalName "sprk_aimodeldeployment")) {
        New-Entity -Token $Token -BaseUrl $BaseUrl -SchemaName "sprk_aimodeldeployment" `
            -DisplayName "AI Model Deployment" -PluralName "AI Model Deployments" `
            -Description "Configuration for deployed AI models"

        $providerOptionSet = Get-GlobalOptionSet -Token $Token -BaseUrl $BaseUrl -Name "sprk_aiprovider"
        $capabilityOptionSet = Get-GlobalOptionSet -Token $Token -BaseUrl $BaseUrl -Name "sprk_aicapability"

        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_aimodeldeployment" `
            -AttributeDef (New-PicklistAttribute -SchemaName "sprk_provider" -DisplayName "Provider" -Description "AI provider" -GlobalOptionSetId $providerOptionSet.MetadataId -Required $true)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_aimodeldeployment" `
            -AttributeDef (New-StringAttribute -SchemaName "sprk_modelid" -DisplayName "Model ID" -Description "Model identifier" -MaxLength 100 -Required $true)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_aimodeldeployment" `
            -AttributeDef (New-StringAttribute -SchemaName "sprk_endpoint" -DisplayName "Endpoint" -Description "API endpoint" -MaxLength 500)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_aimodeldeployment" `
            -AttributeDef (New-PicklistAttribute -SchemaName "sprk_capability" -DisplayName "Capability" -Description "AI capability" -GlobalOptionSetId $capabilityOptionSet.MetadataId -Required $true)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_aimodeldeployment" `
            -AttributeDef (New-IntegerAttribute -SchemaName "sprk_contextwindow" -DisplayName "Context Window" -Description "Token context window size")
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_aimodeldeployment" `
            -AttributeDef (New-BooleanAttribute -SchemaName "sprk_isdefault" -DisplayName "Is Default" -Description "Whether this is the default model" -Default $false)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_aimodeldeployment" `
            -AttributeDef (New-BooleanAttribute -SchemaName "sprk_isactive" -DisplayName "Is Active" -Description "Whether this model is active" -Default $true)
    } else { Write-Host "  sprk_aimodeldeployment already exists, skipping..." -ForegroundColor Yellow }

    Write-Host "Phase 2 complete: Lookup entities created" -ForegroundColor Green
}

function Deploy-Phase3-ExtendExistingEntities {
    param([string]$Token, [string]$BaseUrl)

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " Phase 3: Extending Existing Entities" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    # Get option set IDs
    $playbookModeOs = Get-GlobalOptionSet -Token $Token -BaseUrl $BaseUrl -Name "sprk_playbookmode"
    $playbookTypeOs = Get-GlobalOptionSet -Token $Token -BaseUrl $BaseUrl -Name "sprk_playbooktype"
    $triggerTypeOs = Get-GlobalOptionSet -Token $Token -BaseUrl $BaseUrl -Name "sprk_triggertype"
    $outputFormatOs = Get-GlobalOptionSet -Token $Token -BaseUrl $BaseUrl -Name "sprk_outputformat"

    # Extend sprk_analysisplaybook (if it exists)
    if (Test-EntityExists -Token $Token -BaseUrl $BaseUrl -LogicalName "sprk_analysisplaybook") {
        Write-Host "Extending sprk_analysisplaybook..." -ForegroundColor Yellow

        $playbookAttrs = @(
            @{ Name = "sprk_playbookmode"; Def = (New-PicklistAttribute -SchemaName "sprk_playbookmode" -DisplayName "Playbook Mode" -Description "Legacy or Node-Based mode" -GlobalOptionSetId $playbookModeOs.MetadataId) }
            @{ Name = "sprk_playbooktype"; Def = (New-PicklistAttribute -SchemaName "sprk_playbooktype" -DisplayName "Playbook Type" -Description "Type of playbook" -GlobalOptionSetId $playbookTypeOs.MetadataId) }
            @{ Name = "sprk_canvaslayoutjson"; Def = (New-MemoAttribute -SchemaName "sprk_canvaslayoutjson" -DisplayName "Canvas Layout" -Description "JSON layout for visual builder" -MaxLength 1048576) }
            @{ Name = "sprk_triggertype"; Def = (New-PicklistAttribute -SchemaName "sprk_triggertype" -DisplayName "Trigger Type" -Description "How the playbook is triggered" -GlobalOptionSetId $triggerTypeOs.MetadataId) }
            @{ Name = "sprk_triggerconfigjson"; Def = (New-MemoAttribute -SchemaName "sprk_triggerconfigjson" -DisplayName "Trigger Config" -Description "JSON configuration for triggers" -MaxLength 100000) }
            @{ Name = "sprk_version"; Def = (New-IntegerAttribute -SchemaName "sprk_version" -DisplayName "Version" -Description "Playbook version number") }
            @{ Name = "sprk_maxparallelnodes"; Def = (New-IntegerAttribute -SchemaName "sprk_maxparallelnodes" -DisplayName "Max Parallel Nodes" -Description "Maximum concurrent node execution") }
            @{ Name = "sprk_continueonerror"; Def = (New-BooleanAttribute -SchemaName "sprk_continueonerror" -DisplayName "Continue On Error" -Description "Continue execution if a node fails" -Default $false) }
        )

        foreach ($attr in $playbookAttrs) {
            if (-not (Test-AttributeExists -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_analysisplaybook" -AttributeLogicalName $attr.Name)) {
                Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_analysisplaybook" -AttributeDef $attr.Def
            } else { Write-Host "    $($attr.Name) already exists, skipping..." -ForegroundColor Yellow }
        }
    } else { Write-Host "  sprk_analysisplaybook does not exist, skipping extension..." -ForegroundColor Yellow }

    # Extend sprk_analysisaction (if it exists)
    if (Test-EntityExists -Token $Token -BaseUrl $BaseUrl -LogicalName "sprk_analysisaction") {
        Write-Host "Extending sprk_analysisaction..." -ForegroundColor Yellow

        # Non-lookup attributes
        $actionAttrs = @(
            @{ Name = "sprk_outputschemajson"; Def = (New-MemoAttribute -SchemaName "sprk_outputschemajson" -DisplayName "Output Schema" -Description "JSON schema for action output" -MaxLength 1048576) }
            @{ Name = "sprk_outputformat"; Def = (New-PicklistAttribute -SchemaName "sprk_outputformat" -DisplayName "Output Format" -Description "Format of the output" -GlobalOptionSetId $outputFormatOs.MetadataId) }
            @{ Name = "sprk_allowsskills"; Def = (New-BooleanAttribute -SchemaName "sprk_allowsskills" -DisplayName "Allows Skills" -Description "Whether skills can be attached" -Default $true) }
            @{ Name = "sprk_allowstools"; Def = (New-BooleanAttribute -SchemaName "sprk_allowstools" -DisplayName "Allows Tools" -Description "Whether tools can be attached" -Default $true) }
            @{ Name = "sprk_allowsknowledge"; Def = (New-BooleanAttribute -SchemaName "sprk_allowsknowledge" -DisplayName "Allows Knowledge" -Description "Whether knowledge can be attached" -Default $true) }
            @{ Name = "sprk_allowsdelivery"; Def = (New-BooleanAttribute -SchemaName "sprk_allowsdelivery" -DisplayName "Allows Delivery" -Description "Whether delivery can be configured" -Default $false) }
        )

        foreach ($attr in $actionAttrs) {
            if (-not (Test-AttributeExists -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_analysisaction" -AttributeLogicalName $attr.Name)) {
                Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_analysisaction" -AttributeDef $attr.Def
            } else { Write-Host "    $($attr.Name) already exists, skipping..." -ForegroundColor Yellow }
        }

        # Lookup relationships (create as 1:N relationships)
        if (-not (Test-AttributeExists -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_analysisaction" -AttributeLogicalName "sprk_actiontypeid")) {
            New-OneToManyRelationship -Token $Token -BaseUrl $BaseUrl -ReferencedEntity "sprk_aiactiontype" -ReferencingEntity "sprk_analysisaction" `
                -LookupSchemaName "sprk_actiontypeid" -LookupDisplayName "Action Type" -LookupDescription "Type of action"
        } else { Write-Host "    sprk_actiontypeid already exists, skipping..." -ForegroundColor Yellow }

        if (-not (Test-AttributeExists -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_analysisaction" -AttributeLogicalName "sprk_modeldeploymentid")) {
            New-OneToManyRelationship -Token $Token -BaseUrl $BaseUrl -ReferencedEntity "sprk_aimodeldeployment" -ReferencingEntity "sprk_analysisaction" `
                -LookupSchemaName "sprk_modeldeploymentid" -LookupDisplayName "Default Model" -LookupDescription "Default AI model for this action"
        } else { Write-Host "    sprk_modeldeploymentid already exists, skipping..." -ForegroundColor Yellow }
    } else { Write-Host "  sprk_analysisaction does not exist, skipping extension..." -ForegroundColor Yellow }

    # Extend sprk_analysistool (if it exists)
    if (Test-EntityExists -Token $Token -BaseUrl $BaseUrl -LogicalName "sprk_analysistool") {
        Write-Host "Extending sprk_analysistool..." -ForegroundColor Yellow

        $toolAttrs = @(
            @{ Name = "sprk_outputschemajson"; Def = (New-MemoAttribute -SchemaName "sprk_outputschemajson" -DisplayName "Output Schema" -Description "JSON schema for tool output" -MaxLength 1048576) }
            @{ Name = "sprk_outputexamplejson"; Def = (New-MemoAttribute -SchemaName "sprk_outputexamplejson" -DisplayName "Output Example" -Description "Example output in JSON" -MaxLength 100000) }
        )

        foreach ($attr in $toolAttrs) {
            if (-not (Test-AttributeExists -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_analysistool" -AttributeLogicalName $attr.Name)) {
                Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_analysistool" -AttributeDef $attr.Def
            } else { Write-Host "    $($attr.Name) already exists, skipping..." -ForegroundColor Yellow }
        }
    } else { Write-Host "  sprk_analysistool does not exist, skipping extension..." -ForegroundColor Yellow }

    Write-Host "Phase 3 complete: Existing entities extended" -ForegroundColor Green
}

function Deploy-Phase4-NewEntities {
    param([string]$Token, [string]$BaseUrl)

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " Phase 4: Creating New Entities" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    # sprk_deliverytemplate
    if (-not (Test-EntityExists -Token $Token -BaseUrl $BaseUrl -LogicalName "sprk_deliverytemplate")) {
        New-Entity -Token $Token -BaseUrl $BaseUrl -SchemaName "sprk_deliverytemplate" `
            -DisplayName "Delivery Template" -PluralName "Delivery Templates" `
            -Description "Templates for delivering analysis results"

        # Non-lookup attributes first
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_deliverytemplate" `
            -AttributeDef (New-MemoAttribute -SchemaName "sprk_templatecontent" -DisplayName "Template Content" -Description "Template content" -MaxLength 1048576)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_deliverytemplate" `
            -AttributeDef (New-StringAttribute -SchemaName "sprk_templatefileid" -DisplayName "Template File" -Description "Reference to template file" -MaxLength 500)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_deliverytemplate" `
            -AttributeDef (New-MemoAttribute -SchemaName "sprk_placeholdersjson" -DisplayName "Placeholders" -Description "JSON definition of placeholders" -MaxLength 100000)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_deliverytemplate" `
            -AttributeDef (New-BooleanAttribute -SchemaName "sprk_isactive" -DisplayName "Is Active" -Description "Whether this template is active" -Default $true)

        # Lookup relationship
        New-OneToManyRelationship -Token $Token -BaseUrl $BaseUrl -ReferencedEntity "sprk_analysisdeliverytype" -ReferencingEntity "sprk_deliverytemplate" `
            -LookupSchemaName "sprk_deliverytypeid" -LookupDisplayName "Delivery Type" -LookupDescription "Type of delivery" -Required $true
    } else { Write-Host "  sprk_deliverytemplate already exists, skipping..." -ForegroundColor Yellow }

    # sprk_playbooknode
    if (-not (Test-EntityExists -Token $Token -BaseUrl $BaseUrl -LogicalName "sprk_playbooknode")) {
        New-Entity -Token $Token -BaseUrl $BaseUrl -SchemaName "sprk_playbooknode" `
            -DisplayName "Playbook Node" -PluralName "Playbook Nodes" `
            -Description "Individual nodes within a playbook"

        # Non-lookup attributes first
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbooknode" `
            -AttributeDef (New-IntegerAttribute -SchemaName "sprk_executionorder" -DisplayName "Execution Order" -Description "Order of execution" -Required $true)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbooknode" `
            -AttributeDef (New-MemoAttribute -SchemaName "sprk_dependsonjson" -DisplayName "Depends On" -Description "JSON array of dependency node IDs" -MaxLength 100000)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbooknode" `
            -AttributeDef (New-StringAttribute -SchemaName "sprk_outputvariable" -DisplayName "Output Variable" -Description "Variable name for output" -MaxLength 100 -Required $true)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbooknode" `
            -AttributeDef (New-MemoAttribute -SchemaName "sprk_conditionjson" -DisplayName "Condition" -Description "JSON condition for execution" -MaxLength 100000)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbooknode" `
            -AttributeDef (New-MemoAttribute -SchemaName "sprk_configjson" -DisplayName "Configuration" -Description "JSON configuration for the node" -MaxLength 1048576)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbooknode" `
            -AttributeDef (New-IntegerAttribute -SchemaName "sprk_timeoutseconds" -DisplayName "Timeout" -Description "Execution timeout in seconds")
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbooknode" `
            -AttributeDef (New-IntegerAttribute -SchemaName "sprk_retrycount" -DisplayName "Retry Count" -Description "Number of retries on failure")
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbooknode" `
            -AttributeDef (New-IntegerAttribute -SchemaName "sprk_position_x" -DisplayName "Canvas X" -Description "X position on canvas")
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbooknode" `
            -AttributeDef (New-IntegerAttribute -SchemaName "sprk_position_y" -DisplayName "Canvas Y" -Description "Y position on canvas")
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbooknode" `
            -AttributeDef (New-BooleanAttribute -SchemaName "sprk_isactive" -DisplayName "Is Active" -Description "Whether this node is active" -Default $true)

        # Lookup relationships
        New-OneToManyRelationship -Token $Token -BaseUrl $BaseUrl -ReferencedEntity "sprk_analysisplaybook" -ReferencingEntity "sprk_playbooknode" `
            -LookupSchemaName "sprk_playbookid" -LookupDisplayName "Playbook" -LookupDescription "Parent playbook" -Required $true
        New-OneToManyRelationship -Token $Token -BaseUrl $BaseUrl -ReferencedEntity "sprk_analysisaction" -ReferencingEntity "sprk_playbooknode" `
            -LookupSchemaName "sprk_actionid" -LookupDisplayName "Action" -LookupDescription "Action to execute" -Required $true
        New-OneToManyRelationship -Token $Token -BaseUrl $BaseUrl -ReferencedEntity "sprk_analysistool" -ReferencingEntity "sprk_playbooknode" `
            -LookupSchemaName "sprk_toolid" -LookupDisplayName "Tool" -LookupDescription "Optional tool override"
        New-OneToManyRelationship -Token $Token -BaseUrl $BaseUrl -ReferencedEntity "sprk_aimodeldeployment" -ReferencingEntity "sprk_playbooknode" `
            -LookupSchemaName "sprk_modeldeploymentid" -LookupDisplayName "Model Deployment" -LookupDescription "AI model override"
    } else { Write-Host "  sprk_playbooknode already exists, skipping..." -ForegroundColor Yellow }

    # sprk_playbookrun
    if (-not (Test-EntityExists -Token $Token -BaseUrl $BaseUrl -LogicalName "sprk_playbookrun")) {
        New-Entity -Token $Token -BaseUrl $BaseUrl -SchemaName "sprk_playbookrun" `
            -DisplayName "Playbook Run" -PluralName "Playbook Runs" `
            -Description "Execution records for playbook runs" -IsAutoNumber $true

        $runStatusOs = Get-GlobalOptionSet -Token $Token -BaseUrl $BaseUrl -Name "sprk_playbookrunstatus"

        # Non-lookup attributes first
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbookrun" `
            -AttributeDef (New-PicklistAttribute -SchemaName "sprk_status" -DisplayName "Status" -Description "Execution status" -GlobalOptionSetId $runStatusOs.MetadataId -Required $true)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbookrun" `
            -AttributeDef (New-MemoAttribute -SchemaName "sprk_inputcontextjson" -DisplayName "Input Context" -Description "JSON input context" -MaxLength 1048576)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbookrun" `
            -AttributeDef (New-DateTimeAttribute -SchemaName "sprk_startedon" -DisplayName "Started On" -Description "When the run started")
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbookrun" `
            -AttributeDef (New-DateTimeAttribute -SchemaName "sprk_completedon" -DisplayName "Completed On" -Description "When the run completed")
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbookrun" `
            -AttributeDef (New-MemoAttribute -SchemaName "sprk_outputsjson" -DisplayName "Outputs" -Description "JSON outputs from the run" -MaxLength 1048576)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbookrun" `
            -AttributeDef (New-MemoAttribute -SchemaName "sprk_errormessage" -DisplayName "Error Message" -Description "Error message if failed" -MaxLength 100000)

        # Lookup relationships
        New-OneToManyRelationship -Token $Token -BaseUrl $BaseUrl -ReferencedEntity "sprk_analysisplaybook" -ReferencingEntity "sprk_playbookrun" `
            -LookupSchemaName "sprk_playbookid" -LookupDisplayName "Playbook" -LookupDescription "Playbook that was executed" -Required $true
        New-OneToManyRelationship -Token $Token -BaseUrl $BaseUrl -ReferencedEntity "systemuser" -ReferencingEntity "sprk_playbookrun" `
            -LookupSchemaName "sprk_triggeredby" -LookupDisplayName "Triggered By" -LookupDescription "User who triggered the run" -Required $true
    } else { Write-Host "  sprk_playbookrun already exists, skipping..." -ForegroundColor Yellow }

    # sprk_playbooknoderun
    if (-not (Test-EntityExists -Token $Token -BaseUrl $BaseUrl -LogicalName "sprk_playbooknoderun")) {
        New-Entity -Token $Token -BaseUrl $BaseUrl -SchemaName "sprk_playbooknoderun" `
            -DisplayName "Playbook Node Run" -PluralName "Playbook Node Runs" `
            -Description "Execution records for individual node runs" -IsAutoNumber $true

        $nodeRunStatusOs = Get-GlobalOptionSet -Token $Token -BaseUrl $BaseUrl -Name "sprk_noderunstatus"

        # Non-lookup attributes first
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbooknoderun" `
            -AttributeDef (New-PicklistAttribute -SchemaName "sprk_status" -DisplayName "Status" -Description "Node execution status" -GlobalOptionSetId $nodeRunStatusOs.MetadataId -Required $true)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbooknoderun" `
            -AttributeDef (New-MemoAttribute -SchemaName "sprk_inputjson" -DisplayName "Input" -Description "JSON input to the node" -MaxLength 1048576)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbooknoderun" `
            -AttributeDef (New-MemoAttribute -SchemaName "sprk_outputjson" -DisplayName "Output" -Description "JSON output from the node" -MaxLength 1048576)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbooknoderun" `
            -AttributeDef (New-IntegerAttribute -SchemaName "sprk_tokensin" -DisplayName "Tokens In" -Description "Input tokens consumed")
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbooknoderun" `
            -AttributeDef (New-IntegerAttribute -SchemaName "sprk_tokensout" -DisplayName "Tokens Out" -Description "Output tokens generated")
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbooknoderun" `
            -AttributeDef (New-IntegerAttribute -SchemaName "sprk_durationms" -DisplayName "Duration (ms)" -Description "Execution duration in milliseconds")
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbooknoderun" `
            -AttributeDef (New-MemoAttribute -SchemaName "sprk_errormessage" -DisplayName "Error Message" -Description "Error message if failed" -MaxLength 100000)
        Add-EntityAttribute -Token $Token -BaseUrl $BaseUrl -EntityLogicalName "sprk_playbooknoderun" `
            -AttributeDef (New-MemoAttribute -SchemaName "sprk_validationwarnings" -DisplayName "Validation Warnings" -Description "Validation warnings" -MaxLength 100000)

        # Lookup relationships
        New-OneToManyRelationship -Token $Token -BaseUrl $BaseUrl -ReferencedEntity "sprk_playbookrun" -ReferencingEntity "sprk_playbooknoderun" `
            -LookupSchemaName "sprk_playbookrunid" -LookupDisplayName "Playbook Run" -LookupDescription "Parent playbook run" -Required $true
        New-OneToManyRelationship -Token $Token -BaseUrl $BaseUrl -ReferencedEntity "sprk_playbooknode" -ReferencingEntity "sprk_playbooknoderun" `
            -LookupSchemaName "sprk_playbooknodeid" -LookupDisplayName "Playbook Node" -LookupDescription "Node that was executed" -Required $true
    } else { Write-Host "  sprk_playbooknoderun already exists, skipping..." -ForegroundColor Yellow }

    Write-Host "Phase 4 complete: New entities created" -ForegroundColor Green
}

function Deploy-Phase5-Publish {
    param([string]$Token, [string]$BaseUrl)

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " Phase 5: Publishing Customizations" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    $publishXml = @{
        "ParameterXml" = "<importexportxml><entities><entity>sprk_aiactiontype</entity><entity>sprk_analysisdeliverytype</entity><entity>sprk_aimodeldeployment</entity><entity>sprk_deliverytemplate</entity><entity>sprk_playbooknode</entity><entity>sprk_playbookrun</entity><entity>sprk_playbooknoderun</entity><entity>sprk_analysisplaybook</entity><entity>sprk_analysisaction</entity><entity>sprk_analysistool</entity></entities></importexportxml>"
    }

    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl -Endpoint "PublishXml" -Method "POST" -Body $publishXml
        Write-Host "  Customizations published" -ForegroundColor Green
    }
    catch {
        Write-Host "  Warning: Publish may have timed out, but entities should be available" -ForegroundColor Yellow
    }

    Write-Host "Phase 5 complete: Customizations published" -ForegroundColor Green
}

#endregion

#region Main Execution

function Main {
    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host " Deploy Node-Based Playbook Schema to Dataverse" -ForegroundColor Cyan
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Yellow
    Write-Host ""

    # Get token
    $token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
    Write-Host "Authentication successful" -ForegroundColor Green

    # Execute deployment phases
    Deploy-Phase1-OptionSets -Token $token -BaseUrl $EnvironmentUrl
    Deploy-Phase2-LookupEntities -Token $token -BaseUrl $EnvironmentUrl
    Deploy-Phase3-ExtendExistingEntities -Token $token -BaseUrl $EnvironmentUrl
    Deploy-Phase4-NewEntities -Token $token -BaseUrl $EnvironmentUrl
    Deploy-Phase5-Publish -Token $token -BaseUrl $EnvironmentUrl

    # Verification
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " Verification" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    $entities = @("sprk_aiactiontype", "sprk_analysisdeliverytype", "sprk_aimodeldeployment",
                  "sprk_deliverytemplate", "sprk_playbooknode", "sprk_playbookrun", "sprk_playbooknoderun")

    foreach ($entity in $entities) {
        if (Test-EntityExists -Token $token -BaseUrl $EnvironmentUrl -LogicalName $entity) {
            Write-Host "  $entity exists" -ForegroundColor Green
        } else {
            Write-Host "  $entity MISSING" -ForegroundColor Red
        }
    }

    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Green
    Write-Host " Deployment Complete!" -ForegroundColor Green
    Write-Host "================================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  1. Verify in Power Apps maker portal: $EnvironmentUrl" -ForegroundColor Gray
    Write-Host "  2. Create N:N relationships (sprk_playbooknode_skill, sprk_playbooknode_knowledge)" -ForegroundColor Gray
    Write-Host "  3. Seed reference data (action types, delivery types, model deployments)" -ForegroundColor Gray
    Write-Host ""
}

Main

#endregion
