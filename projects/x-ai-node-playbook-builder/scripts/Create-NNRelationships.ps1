<#
.SYNOPSIS
    Create N:N relationships for sprk_playbooknode

.DESCRIPTION
    Creates the many-to-many relationships:
    - sprk_playbooknode_skill (sprk_playbooknode <-> sprk_analysisskill)
    - sprk_playbooknode_knowledge (sprk_playbooknode <-> sprk_analysisknowledge)
#>

param(
    [string]$Environment = "spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = "Stop"
$BaseUrl = "https://$Environment/api/data/v9.2"

Write-Host "`n================================================================" -ForegroundColor Cyan
Write-Host " Create N:N Relationships for sprk_playbooknode" -ForegroundColor Cyan
Write-Host "================================================================`n" -ForegroundColor Cyan

# Get token
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

# Check if sprk_analysisskill exists
Write-Host "Checking if sprk_analysisskill entity exists..."
$skillCheck = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_analysisskill')" -Method "GET"
if (-not $skillCheck.Success) {
    Write-Host "  sprk_analysisskill does not exist - cannot create N:N relationship" -ForegroundColor Yellow
    Write-Host "  This entity may need to be created first" -ForegroundColor Yellow
} else {
    Write-Host "  sprk_analysisskill exists" -ForegroundColor Green

    # Create sprk_playbooknode_skill N:N relationship
    Write-Host "`nCreating N:N relationship: sprk_playbooknode_skill..."

    $nnRelationship = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.ManyToManyRelationshipMetadata"
        "SchemaName" = "sprk_playbooknode_skill"
        "Entity1LogicalName" = "sprk_playbooknode"
        "Entity2LogicalName" = "sprk_analysisskill"
        "Entity1AssociatedMenuConfiguration" = @{
            "Behavior" = "UseCollectionName"
            "Group" = "Details"
            "Order" = 10000
        }
        "Entity2AssociatedMenuConfiguration" = @{
            "Behavior" = "UseCollectionName"
            "Group" = "Details"
            "Order" = 10000
        }
        "IntersectEntityName" = "sprk_playbooknode_skill"
    }

    $result = Invoke-DataverseApi -Endpoint "RelationshipDefinitions" -Method "POST" -Body $nnRelationship
    if ($result.Success) {
        Write-Host "  sprk_playbooknode_skill created" -ForegroundColor Green
    } elseif ($result.Error -like "*already exists*" -or $result.Error -like "*0x80048418*") {
        Write-Host "  sprk_playbooknode_skill already exists" -ForegroundColor Yellow
    } else {
        Write-Host "  Failed: $($result.Error)" -ForegroundColor Red
    }
}

# Check if sprk_analysisknowledge exists
Write-Host "`nChecking if sprk_analysisknowledge entity exists..."
$knowledgeCheck = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_analysisknowledge')" -Method "GET"
if (-not $knowledgeCheck.Success) {
    Write-Host "  sprk_analysisknowledge does not exist - cannot create N:N relationship" -ForegroundColor Yellow
    Write-Host "  This entity may need to be created first" -ForegroundColor Yellow
} else {
    Write-Host "  sprk_analysisknowledge exists" -ForegroundColor Green

    # Create sprk_playbooknode_knowledge N:N relationship
    Write-Host "`nCreating N:N relationship: sprk_playbooknode_knowledge..."

    $nnRelationship2 = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.ManyToManyRelationshipMetadata"
        "SchemaName" = "sprk_playbooknode_knowledge"
        "Entity1LogicalName" = "sprk_playbooknode"
        "Entity2LogicalName" = "sprk_analysisknowledge"
        "Entity1AssociatedMenuConfiguration" = @{
            "Behavior" = "UseCollectionName"
            "Group" = "Details"
            "Order" = 10000
        }
        "Entity2AssociatedMenuConfiguration" = @{
            "Behavior" = "UseCollectionName"
            "Group" = "Details"
            "Order" = 10000
        }
        "IntersectEntityName" = "sprk_playbooknode_knowledge"
    }

    $result2 = Invoke-DataverseApi -Endpoint "RelationshipDefinitions" -Method "POST" -Body $nnRelationship2
    if ($result2.Success) {
        Write-Host "  sprk_playbooknode_knowledge created" -ForegroundColor Green
    } elseif ($result2.Error -like "*already exists*" -or $result2.Error -like "*0x80048418*") {
        Write-Host "  sprk_playbooknode_knowledge already exists" -ForegroundColor Yellow
    } else {
        Write-Host "  Failed: $($result2.Error)" -ForegroundColor Red
    }
}

# Now try creating boolean attributes with proper OptionSet definition
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host " Creating Boolean Attributes" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$boolOptionSet = @{
    "TrueOption" = @{
        "Value" = 1
        "Label" = New-Label -Text "Yes"
    }
    "FalseOption" = @{
        "Value" = 0
        "Label" = New-Label -Text "No"
    }
}

$entities = @("sprk_playbooknode", "sprk_deliverytemplate", "sprk_aiactiontype", "sprk_analysisdeliverytype", "sprk_aimodeldeployment")

foreach ($entity in $entities) {
    Write-Host "`nChecking sprk_isactive on $entity..."
    $check = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='$entity')/Attributes(LogicalName='sprk_isactive')" -Method "GET"
    if ($check.Success) {
        Write-Host "  Already exists" -ForegroundColor Yellow
        continue
    }

    Write-Host "  Creating sprk_isactive..."
    $boolAttr = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
        "SchemaName" = "sprk_isactive"
        "RequiredLevel" = @{ "Value" = "None" }
        "DisplayName" = New-Label -Text "Is Active"
        "Description" = New-Label -Text "Whether this record is active"
        "OptionSet" = $boolOptionSet
    }

    $result = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='$entity')/Attributes" -Method "POST" -Body $boolAttr
    if ($result.Success) {
        Write-Host "  Created" -ForegroundColor Green
    } else {
        Write-Host "  Failed: $($result.Error)" -ForegroundColor Red
    }
}

# Publish
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host " Publishing Customizations" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$publishXml = @{
    "ParameterXml" = "<importexportxml><entities><entity>sprk_playbooknode</entity><entity>sprk_deliverytemplate</entity><entity>sprk_aiactiontype</entity><entity>sprk_analysisdeliverytype</entity><entity>sprk_aimodeldeployment</entity></entities></importexportxml>"
}

$publishResult = Invoke-DataverseApi -Endpoint "PublishXml" -Method "POST" -Body $publishXml
if ($publishResult.Success -or $publishResult.Error -like "*204*") {
    Write-Host "  Customizations published" -ForegroundColor Green
} else {
    Write-Host "  Warning: $($publishResult.Error)" -ForegroundColor Yellow
}

Write-Host "`n================================================================" -ForegroundColor Cyan
Write-Host " Complete!" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
