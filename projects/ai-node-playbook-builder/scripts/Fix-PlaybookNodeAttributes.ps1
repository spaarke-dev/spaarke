<#
.SYNOPSIS
    Add missing attributes to sprk_playbooknode entity in Dataverse

.DESCRIPTION
    This script adds attributes that were missed during initial deployment
    due to transient API errors.

.PARAMETER Environment
    Dataverse environment URL (default: spaarkedev1.crm.dynamics.com)
#>

param(
    [string]$Environment = "spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = "Stop"
$BaseUrl = "https://$Environment/api/data/v9.2"

Write-Host "`n================================================================" -ForegroundColor Cyan
Write-Host " Fix sprk_playbooknode Missing Attributes" -ForegroundColor Cyan
Write-Host "================================================================`n" -ForegroundColor Cyan
Write-Host "Environment: https://$Environment`n"

# Get token
Write-Host "Getting authentication token from Azure CLI..."
$token = (az account get-access-token --resource "https://$Environment" --query accessToken -o tsv)
if (-not $token) {
    Write-Error "Failed to get authentication token. Run 'az login' first."
    exit 1
}
Write-Host "Authentication successful`n" -ForegroundColor Green

$headers = @{
    "Authorization" = "Bearer $token"
    "OData-MaxVersion" = "4.0"
    "OData-Version" = "4.0"
    "Content-Type" = "application/json"
    "Accept" = "application/json"
    "Prefer" = "return=representation"
}

function Invoke-DataverseApi {
    param(
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null
    )

    $uri = "$BaseUrl/$Endpoint"
    $params = @{
        Uri = $uri
        Headers = $headers
        Method = $Method
    }

    if ($Body) {
        $params.Body = ($Body | ConvertTo-Json -Depth 20 -Compress)
    }

    try {
        $response = Invoke-RestMethod @params
        return @{ Success = $true; Data = $response }
    }
    catch {
        $errorMessage = $_.Exception.Message
        if ($_.Exception.Response) {
            try {
                $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                $errorMessage = $reader.ReadToEnd()
            } catch {}
        }
        return @{ Success = $false; Error = $errorMessage }
    }
}

function New-Label {
    param([string]$Text)
    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(
            @{
                "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                "Label" = $Text
                "LanguageCode" = 1033
            }
        )
    }
}

function Add-AttributeIfMissing {
    param(
        [string]$EntityLogicalName,
        [string]$AttributeLogicalName,
        [hashtable]$AttributeDef
    )

    # Check if attribute exists
    $check = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')" -Method "GET"
    if ($check.Success) {
        Write-Host "    $AttributeLogicalName already exists, skipping..." -ForegroundColor Yellow
        return $true
    }

    # Create attribute
    Write-Host "    Creating $AttributeLogicalName..." -ForegroundColor White
    $result = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" -Method "POST" -Body $AttributeDef

    if ($result.Success) {
        Write-Host "    $AttributeLogicalName created" -ForegroundColor Green
        return $true
    } else {
        Write-Host "    Failed to create $AttributeLogicalName`: $($result.Error)" -ForegroundColor Red
        return $false
    }
}

function Add-LookupRelationship {
    param(
        [string]$ReferencedEntity,
        [string]$ReferencingEntity,
        [string]$LookupSchemaName,
        [string]$LookupDisplayName,
        [string]$LookupDescription,
        [bool]$Required = $false
    )

    # Check if relationship/lookup already exists
    $check = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='$ReferencingEntity')/Attributes(LogicalName='$($LookupSchemaName.ToLower())')" -Method "GET"
    if ($check.Success) {
        Write-Host "    $LookupSchemaName already exists, skipping..." -ForegroundColor Yellow
        return $true
    }

    Write-Host "    Creating relationship $ReferencingEntity -> $ReferencedEntity ($LookupSchemaName)..." -ForegroundColor White

    $relationshipSchemaName = "sprk_$($ReferencedEntity)_$($ReferencingEntity)_$($LookupSchemaName -replace 'sprk_', '')"

    $relationshipDef = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
        "SchemaName" = $relationshipSchemaName
        "ReferencedEntity" = $ReferencedEntity
        "ReferencingEntity" = $ReferencingEntity
        "CascadeConfiguration" = @{
            "Assign" = "NoCascade"
            "Delete" = "Restrict"
            "Merge" = "NoCascade"
            "Reparent" = "NoCascade"
            "Share" = "NoCascade"
            "Unshare" = "NoCascade"
            "RollupView" = "NoCascade"
        }
        "Lookup" = @{
            "SchemaName" = $LookupSchemaName
            "DisplayName" = New-Label -Text $LookupDisplayName
            "Description" = New-Label -Text $LookupDescription
            "RequiredLevel" = @{
                "Value" = if ($Required) { "ApplicationRequired" } else { "None" }
            }
        }
    }

    $result = Invoke-DataverseApi -Endpoint "RelationshipDefinitions" -Method "POST" -Body $relationshipDef

    if ($result.Success) {
        Write-Host "    $LookupSchemaName created" -ForegroundColor Green
        return $true
    } else {
        Write-Host "    Failed: $($result.Error)" -ForegroundColor Red
        return $false
    }
}

# ===== Add Missing Attributes to sprk_playbooknode =====

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " Adding Missing Attributes to sprk_playbooknode" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# 1. Lookup: sprk_playbookid -> sprk_analysisplaybook (CASCADE delete)
Write-Host "`nLookup: sprk_playbookid -> sprk_analysisplaybook"
$check = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_playbooknode')/Attributes(LogicalName='sprk_playbookid')" -Method "GET"
if ($check.Success) {
    Write-Host "    sprk_playbookid already exists" -ForegroundColor Yellow
} else {
    Write-Host "    Creating sprk_playbookid lookup (CASCADE delete)..." -ForegroundColor White
    $relDef = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
        "SchemaName" = "sprk_analysisplaybook_playbooknode_playbookid"
        "ReferencedEntity" = "sprk_analysisplaybook"
        "ReferencingEntity" = "sprk_playbooknode"
        "CascadeConfiguration" = @{
            "Assign" = "NoCascade"
            "Delete" = "Cascade"
            "Merge" = "NoCascade"
            "Reparent" = "NoCascade"
            "Share" = "NoCascade"
            "Unshare" = "NoCascade"
            "RollupView" = "NoCascade"
        }
        "Lookup" = @{
            "SchemaName" = "sprk_playbookid"
            "DisplayName" = New-Label -Text "Playbook"
            "Description" = New-Label -Text "The playbook this node belongs to"
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
        }
    }
    $result = Invoke-DataverseApi -Endpoint "RelationshipDefinitions" -Method "POST" -Body $relDef
    if ($result.Success) { Write-Host "    Created sprk_playbookid" -ForegroundColor Green }
    else { Write-Host "    Failed: $($result.Error)" -ForegroundColor Red }
}

# 2. Lookup: sprk_actionid -> sprk_analysisaction (RESTRICT delete)
Write-Host "`nLookup: sprk_actionid -> sprk_analysisaction"
Add-LookupRelationship -ReferencedEntity "sprk_analysisaction" -ReferencingEntity "sprk_playbooknode" `
    -LookupSchemaName "sprk_actionid" -LookupDisplayName "Action" `
    -LookupDescription "The action definition for this node" -Required $true

# 3. Lookup: sprk_toolid -> sprk_analysistool (RESTRICT delete)
Write-Host "`nLookup: sprk_toolid -> sprk_analysistool"
Add-LookupRelationship -ReferencedEntity "sprk_analysistool" -ReferencingEntity "sprk_playbooknode" `
    -LookupSchemaName "sprk_toolid" -LookupDisplayName "Tool" `
    -LookupDescription "Optional tool to use for this node" -Required $false

# 4. Lookup: sprk_modeldeploymentid -> sprk_aimodeldeployment (RESTRICT delete)
Write-Host "`nLookup: sprk_modeldeploymentid -> sprk_aimodeldeployment"
Add-LookupRelationship -ReferencedEntity "sprk_aimodeldeployment" -ReferencingEntity "sprk_playbooknode" `
    -LookupSchemaName "sprk_modeldeploymentid" -LookupDisplayName "Model Deployment" `
    -LookupDescription "AI model deployment override for this node" -Required $false

# 5. sprk_configjson (Memo)
Write-Host "`nAttribute: sprk_configjson"
$configJsonAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
    "SchemaName" = "sprk_configjson"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName" = New-Label -Text "Configuration"
    "Description" = New-Label -Text "Node-specific configuration in JSON format"
    "MaxLength" = 1048576
}
Add-AttributeIfMissing -EntityLogicalName "sprk_playbooknode" -AttributeLogicalName "sprk_configjson" -AttributeDef $configJsonAttr

# 6. sprk_timeoutseconds (Integer)
Write-Host "`nAttribute: sprk_timeoutseconds"
$timeoutAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
    "SchemaName" = "sprk_timeoutseconds"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName" = New-Label -Text "Timeout (seconds)"
    "Description" = New-Label -Text "Maximum execution time for this node"
    "MinValue" = 0
    "MaxValue" = 3600
}
Add-AttributeIfMissing -EntityLogicalName "sprk_playbooknode" -AttributeLogicalName "sprk_timeoutseconds" -AttributeDef $timeoutAttr

# 7. sprk_retrycount (Integer)
Write-Host "`nAttribute: sprk_retrycount"
$retryAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
    "SchemaName" = "sprk_retrycount"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName" = New-Label -Text "Retry Count"
    "Description" = New-Label -Text "Number of retry attempts on failure"
    "MinValue" = 0
    "MaxValue" = 10
}
Add-AttributeIfMissing -EntityLogicalName "sprk_playbooknode" -AttributeLogicalName "sprk_retrycount" -AttributeDef $retryAttr

# 8. sprk_position_x (Integer)
Write-Host "`nAttribute: sprk_position_x"
$posXAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
    "SchemaName" = "sprk_position_x"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName" = New-Label -Text "Canvas X"
    "Description" = New-Label -Text "X position on the visual canvas"
    "MinValue" = -2147483648
    "MaxValue" = 2147483647
}
Add-AttributeIfMissing -EntityLogicalName "sprk_playbooknode" -AttributeLogicalName "sprk_position_x" -AttributeDef $posXAttr

# 9. sprk_position_y (Integer)
Write-Host "`nAttribute: sprk_position_y"
$posYAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
    "SchemaName" = "sprk_position_y"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName" = New-Label -Text "Canvas Y"
    "Description" = New-Label -Text "Y position on the visual canvas"
    "MinValue" = -2147483648
    "MaxValue" = 2147483647
}
Add-AttributeIfMissing -EntityLogicalName "sprk_playbooknode" -AttributeLogicalName "sprk_position_y" -AttributeDef $posYAttr

# 10. sprk_isactive (Boolean)
Write-Host "`nAttribute: sprk_isactive"
$isActiveAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
    "SchemaName" = "sprk_isactive"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName" = New-Label -Text "Is Active"
    "Description" = New-Label -Text "Whether this node is active"
}
Add-AttributeIfMissing -EntityLogicalName "sprk_playbooknode" -AttributeLogicalName "sprk_isactive" -AttributeDef $isActiveAttr

# ===== Add Missing Attributes to sprk_deliverytemplate =====

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host " Adding Missing Attributes to sprk_deliverytemplate" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Lookup: sprk_deliverytypeid -> sprk_analysisdeliverytype
Write-Host "`nLookup: sprk_deliverytypeid -> sprk_analysisdeliverytype"
Add-LookupRelationship -ReferencedEntity "sprk_analysisdeliverytype" -ReferencingEntity "sprk_deliverytemplate" `
    -LookupSchemaName "sprk_deliverytypeid" -LookupDisplayName "Delivery Type" `
    -LookupDescription "The type of delivery for this template" -Required $true

# sprk_templatecontent (Memo)
Write-Host "`nAttribute: sprk_templatecontent"
$templateContentAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
    "SchemaName" = "sprk_templatecontent"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName" = New-Label -Text "Template Content"
    "Description" = New-Label -Text "Template content for delivery"
    "MaxLength" = 1048576
}
Add-AttributeIfMissing -EntityLogicalName "sprk_deliverytemplate" -AttributeLogicalName "sprk_templatecontent" -AttributeDef $templateContentAttr

# sprk_templatefileid (String)
Write-Host "`nAttribute: sprk_templatefileid"
$templateFileAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
    "SchemaName" = "sprk_templatefileid"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName" = New-Label -Text "Template File"
    "Description" = New-Label -Text "Reference to template file in SPE"
    "MaxLength" = 500
}
Add-AttributeIfMissing -EntityLogicalName "sprk_deliverytemplate" -AttributeLogicalName "sprk_templatefileid" -AttributeDef $templateFileAttr

# sprk_placeholdersjson (Memo)
Write-Host "`nAttribute: sprk_placeholdersjson"
$placeholdersAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
    "SchemaName" = "sprk_placeholdersjson"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName" = New-Label -Text "Placeholders"
    "Description" = New-Label -Text "Placeholder definitions in JSON format"
    "MaxLength" = 100000
}
Add-AttributeIfMissing -EntityLogicalName "sprk_deliverytemplate" -AttributeLogicalName "sprk_placeholdersjson" -AttributeDef $placeholdersAttr

# sprk_isactive (Boolean) for deliverytemplate
Write-Host "`nAttribute: sprk_isactive (deliverytemplate)"
Add-AttributeIfMissing -EntityLogicalName "sprk_deliverytemplate" -AttributeLogicalName "sprk_isactive" -AttributeDef $isActiveAttr

# ===== Add Missing Attributes to sprk_playbookrun =====

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host " Adding Missing Attributes to sprk_playbookrun" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Lookup: sprk_playbookid -> sprk_analysisplaybook (CASCADE)
Write-Host "`nLookup: sprk_playbookid -> sprk_analysisplaybook"
$check = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_playbookrun')/Attributes(LogicalName='sprk_playbookid')" -Method "GET"
if ($check.Success) {
    Write-Host "    sprk_playbookid already exists" -ForegroundColor Yellow
} else {
    Write-Host "    Creating sprk_playbookid lookup (CASCADE delete)..." -ForegroundColor White
    $relDef = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
        "SchemaName" = "sprk_analysisplaybook_playbookrun_playbookid"
        "ReferencedEntity" = "sprk_analysisplaybook"
        "ReferencingEntity" = "sprk_playbookrun"
        "CascadeConfiguration" = @{
            "Assign" = "NoCascade"
            "Delete" = "Cascade"
            "Merge" = "NoCascade"
            "Reparent" = "NoCascade"
            "Share" = "NoCascade"
            "Unshare" = "NoCascade"
            "RollupView" = "NoCascade"
        }
        "Lookup" = @{
            "SchemaName" = "sprk_playbookid"
            "DisplayName" = New-Label -Text "Playbook"
            "Description" = New-Label -Text "The playbook that was executed"
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
        }
    }
    $result = Invoke-DataverseApi -Endpoint "RelationshipDefinitions" -Method "POST" -Body $relDef
    if ($result.Success) { Write-Host "    Created sprk_playbookid" -ForegroundColor Green }
    else { Write-Host "    Failed: $($result.Error)" -ForegroundColor Red }
}

# Lookup: sprk_triggeredby -> systemuser (RESTRICT)
Write-Host "`nLookup: sprk_triggeredby -> systemuser"
Add-LookupRelationship -ReferencedEntity "systemuser" -ReferencingEntity "sprk_playbookrun" `
    -LookupSchemaName "sprk_triggeredby" -LookupDisplayName "Triggered By" `
    -LookupDescription "User who triggered the playbook run" -Required $true

# sprk_status (Picklist - sprk_playbookrunstatus)
Write-Host "`nAttribute: sprk_status"
$statusAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
    "SchemaName" = "sprk_status"
    "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
    "DisplayName" = New-Label -Text "Status"
    "Description" = New-Label -Text "Current status of the playbook run"
    "GlobalOptionSet@odata.bind" = "/GlobalOptionSetDefinitions(Name='sprk_playbookrunstatus')"
}
Add-AttributeIfMissing -EntityLogicalName "sprk_playbookrun" -AttributeLogicalName "sprk_status" -AttributeDef $statusAttr

# sprk_inputcontextjson (Memo)
Write-Host "`nAttribute: sprk_inputcontextjson"
$inputContextAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
    "SchemaName" = "sprk_inputcontextjson"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName" = New-Label -Text "Input Context"
    "Description" = New-Label -Text "Input context for the playbook run"
    "MaxLength" = 1048576
}
Add-AttributeIfMissing -EntityLogicalName "sprk_playbookrun" -AttributeLogicalName "sprk_inputcontextjson" -AttributeDef $inputContextAttr

# sprk_startedon (DateTime)
Write-Host "`nAttribute: sprk_startedon"
$startedOnAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
    "SchemaName" = "sprk_startedon"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName" = New-Label -Text "Started On"
    "Description" = New-Label -Text "When the playbook run started"
    "Format" = "DateAndTime"
    "DateTimeBehavior" = @{ "Value" = "UserLocal" }
}
Add-AttributeIfMissing -EntityLogicalName "sprk_playbookrun" -AttributeLogicalName "sprk_startedon" -AttributeDef $startedOnAttr

# sprk_completedon (DateTime)
Write-Host "`nAttribute: sprk_completedon"
$completedOnAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
    "SchemaName" = "sprk_completedon"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName" = New-Label -Text "Completed On"
    "Description" = New-Label -Text "When the playbook run completed"
    "Format" = "DateAndTime"
    "DateTimeBehavior" = @{ "Value" = "UserLocal" }
}
Add-AttributeIfMissing -EntityLogicalName "sprk_playbookrun" -AttributeLogicalName "sprk_completedon" -AttributeDef $completedOnAttr

# sprk_outputsjson (Memo)
Write-Host "`nAttribute: sprk_outputsjson"
$outputsJsonAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
    "SchemaName" = "sprk_outputsjson"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName" = New-Label -Text "Outputs"
    "Description" = New-Label -Text "Collected outputs from all nodes"
    "MaxLength" = 1048576
}
Add-AttributeIfMissing -EntityLogicalName "sprk_playbookrun" -AttributeLogicalName "sprk_outputsjson" -AttributeDef $outputsJsonAttr

# sprk_errormessage (Memo)
Write-Host "`nAttribute: sprk_errormessage"
$errorMsgAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
    "SchemaName" = "sprk_errormessage"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName" = New-Label -Text "Error Message"
    "Description" = New-Label -Text "Error message if run failed"
    "MaxLength" = 100000
}
Add-AttributeIfMissing -EntityLogicalName "sprk_playbookrun" -AttributeLogicalName "sprk_errormessage" -AttributeDef $errorMsgAttr

# ===== Add Missing Attributes to sprk_playbooknoderun =====

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host " Adding Missing Attributes to sprk_playbooknoderun" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Lookup: sprk_playbookrunid -> sprk_playbookrun (CASCADE)
Write-Host "`nLookup: sprk_playbookrunid -> sprk_playbookrun"
$check = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_playbooknoderun')/Attributes(LogicalName='sprk_playbookrunid')" -Method "GET"
if ($check.Success) {
    Write-Host "    sprk_playbookrunid already exists" -ForegroundColor Yellow
} else {
    Write-Host "    Creating sprk_playbookrunid lookup (CASCADE delete)..." -ForegroundColor White
    $relDef = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
        "SchemaName" = "sprk_playbookrun_playbooknoderun_runid"
        "ReferencedEntity" = "sprk_playbookrun"
        "ReferencingEntity" = "sprk_playbooknoderun"
        "CascadeConfiguration" = @{
            "Assign" = "NoCascade"
            "Delete" = "Cascade"
            "Merge" = "NoCascade"
            "Reparent" = "NoCascade"
            "Share" = "NoCascade"
            "Unshare" = "NoCascade"
            "RollupView" = "NoCascade"
        }
        "Lookup" = @{
            "SchemaName" = "sprk_playbookrunid"
            "DisplayName" = New-Label -Text "Playbook Run"
            "Description" = New-Label -Text "The playbook run this node execution belongs to"
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
        }
    }
    $result = Invoke-DataverseApi -Endpoint "RelationshipDefinitions" -Method "POST" -Body $relDef
    if ($result.Success) { Write-Host "    Created sprk_playbookrunid" -ForegroundColor Green }
    else { Write-Host "    Failed: $($result.Error)" -ForegroundColor Red }
}

# Lookup: sprk_playbooknodeid -> sprk_playbooknode (RESTRICT)
Write-Host "`nLookup: sprk_playbooknodeid -> sprk_playbooknode"
Add-LookupRelationship -ReferencedEntity "sprk_playbooknode" -ReferencingEntity "sprk_playbooknoderun" `
    -LookupSchemaName "sprk_playbooknodeid" -LookupDisplayName "Playbook Node" `
    -LookupDescription "The node that was executed" -Required $true

# sprk_status (Picklist - sprk_noderunstatus)
Write-Host "`nAttribute: sprk_status"
$nodeStatusAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
    "SchemaName" = "sprk_status"
    "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
    "DisplayName" = New-Label -Text "Status"
    "Description" = New-Label -Text "Current status of the node run"
    "GlobalOptionSet@odata.bind" = "/GlobalOptionSetDefinitions(Name='sprk_noderunstatus')"
}
Add-AttributeIfMissing -EntityLogicalName "sprk_playbooknoderun" -AttributeLogicalName "sprk_status" -AttributeDef $nodeStatusAttr

# sprk_inputjson (Memo)
Write-Host "`nAttribute: sprk_inputjson"
$inputJsonAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
    "SchemaName" = "sprk_inputjson"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName" = New-Label -Text "Input"
    "Description" = New-Label -Text "Input to this node execution"
    "MaxLength" = 1048576
}
Add-AttributeIfMissing -EntityLogicalName "sprk_playbooknoderun" -AttributeLogicalName "sprk_inputjson" -AttributeDef $inputJsonAttr

# sprk_outputjson (Memo)
Write-Host "`nAttribute: sprk_outputjson"
$outputJsonAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
    "SchemaName" = "sprk_outputjson"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName" = New-Label -Text "Output"
    "Description" = New-Label -Text "Output from this node execution"
    "MaxLength" = 1048576
}
Add-AttributeIfMissing -EntityLogicalName "sprk_playbooknoderun" -AttributeLogicalName "sprk_outputjson" -AttributeDef $outputJsonAttr

# sprk_tokensin (Integer)
Write-Host "`nAttribute: sprk_tokensin"
$tokensInAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
    "SchemaName" = "sprk_tokensin"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName" = New-Label -Text "Tokens In"
    "Description" = New-Label -Text "Number of input tokens consumed"
    "MinValue" = 0
    "MaxValue" = 2147483647
}
Add-AttributeIfMissing -EntityLogicalName "sprk_playbooknoderun" -AttributeLogicalName "sprk_tokensin" -AttributeDef $tokensInAttr

# sprk_tokensout (Integer)
Write-Host "`nAttribute: sprk_tokensout"
$tokensOutAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
    "SchemaName" = "sprk_tokensout"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName" = New-Label -Text "Tokens Out"
    "Description" = New-Label -Text "Number of output tokens generated"
    "MinValue" = 0
    "MaxValue" = 2147483647
}
Add-AttributeIfMissing -EntityLogicalName "sprk_playbooknoderun" -AttributeLogicalName "sprk_tokensout" -AttributeDef $tokensOutAttr

# sprk_durationms (Integer)
Write-Host "`nAttribute: sprk_durationms"
$durationAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.IntegerAttributeMetadata"
    "SchemaName" = "sprk_durationms"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName" = New-Label -Text "Duration (ms)"
    "Description" = New-Label -Text "Execution duration in milliseconds"
    "MinValue" = 0
    "MaxValue" = 2147483647
}
Add-AttributeIfMissing -EntityLogicalName "sprk_playbooknoderun" -AttributeLogicalName "sprk_durationms" -AttributeDef $durationAttr

# sprk_errormessage (Memo) - reuse definition from above
Write-Host "`nAttribute: sprk_errormessage"
Add-AttributeIfMissing -EntityLogicalName "sprk_playbooknoderun" -AttributeLogicalName "sprk_errormessage" -AttributeDef $errorMsgAttr

# sprk_validationwarnings (Memo)
Write-Host "`nAttribute: sprk_validationwarnings"
$validationWarningsAttr = @{
    "@odata.type" = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
    "SchemaName" = "sprk_validationwarnings"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName" = New-Label -Text "Validation Warnings"
    "Description" = New-Label -Text "Any validation warnings from this node"
    "MaxLength" = 100000
}
Add-AttributeIfMissing -EntityLogicalName "sprk_playbooknoderun" -AttributeLogicalName "sprk_validationwarnings" -AttributeDef $validationWarningsAttr

# ===== Publish Customizations =====

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host " Publishing Customizations" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$publishXml = @{
    "ParameterXml" = "<importexportxml><entities><entity>sprk_playbooknode</entity><entity>sprk_playbookrun</entity><entity>sprk_playbooknoderun</entity><entity>sprk_deliverytemplate</entity></entities></importexportxml>"
}

$publishResult = Invoke-DataverseApi -Endpoint "PublishXml" -Method "POST" -Body $publishXml
if ($publishResult.Success -or $publishResult.Error -like "*204*") {
    Write-Host "  Customizations published" -ForegroundColor Green
} else {
    Write-Host "  Warning: $($publishResult.Error)" -ForegroundColor Yellow
}

Write-Host "`n================================================================" -ForegroundColor Cyan
Write-Host " Fix Complete!" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "`nRe-run the verification script to confirm all attributes exist."
