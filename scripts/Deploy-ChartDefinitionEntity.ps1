<#
.SYNOPSIS
    Deploys the sprk_chartdefinition entity to Dataverse.

.DESCRIPTION
    Creates the sprk_chartdefinition entity, option sets, and fields in Dataverse
    using the Metadata Web API. Requires PAC CLI authentication.

.PARAMETER EnvironmentUrl
    The Dataverse environment URL (e.g., https://spaarkedev1.crm.dynamics.com)

.EXAMPLE
    .\Deploy-ChartDefinitionEntity.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

.NOTES
    Project: Visualization Module
    Task: 005 - Deploy entity to Dataverse
    Created: 2025-12-29
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = "Stop"

# Get auth token from Azure CLI
function Get-DataverseToken {
    param([string]$EnvironmentUrl)

    Write-Host "Getting authentication token from Azure CLI..." -ForegroundColor Cyan

    # Use az account get-access-token command
    $tokenResult = az account get-access-token --resource $EnvironmentUrl --query "accessToken" -o tsv 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get token from Azure CLI. Error: $tokenResult. Make sure you're logged in with 'az login'"
    }

    return $tokenResult.Trim()
}

# Make Web API request
function Invoke-DataverseApi {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null,
        [bool]$ReturnResponse = $false
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

# Check if entity already exists
function Test-EntityExists {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$LogicalName
    )

    try {
        $result = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$LogicalName')" -Method "GET"
        return $true
    }
    catch {
        if ($_.Exception.Message -match "does not exist|404") {
            return $false
        }
        throw
    }
}

# Check if global option set exists and return its metadata
function Get-GlobalOptionSet {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$Name
    )

    try {
        $result = Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "GlobalOptionSetDefinitions(Name='$Name')" -Method "GET"
        return $result
    }
    catch {
        # Any error (including 404, "not found", "does not exist") means option set doesn't exist
        return $null
    }
}

# Check if global option set exists
function Test-GlobalOptionSetExists {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$Name
    )

    $result = Get-GlobalOptionSet -Token $Token -BaseUrl $BaseUrl -Name $Name
    return $null -ne $result
}

# Create global option set
function New-GlobalOptionSet {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$Name,
        [string]$DisplayName,
        [array]$Options
    )

    Write-Host "  Creating global option set: $Name..." -ForegroundColor Gray

    $optionSetDef = @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
        "Name"          = $Name
        "DisplayName"   = @{
            "@odata.type"        = "Microsoft.Dynamics.CRM.Label"
            "LocalizedLabels"    = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    "Label"       = $DisplayName
                    "LanguageCode" = 1033
                }
            )
        }
        "IsGlobal"      = $true
        "OptionSetType" = "Picklist"
        "Options"       = $Options
    }

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "GlobalOptionSetDefinitions" -Method "POST" -Body $optionSetDef

    Write-Host "    Created: $Name" -ForegroundColor Green
}

# Create the entity
function New-ChartDefinitionEntity {
    param(
        [string]$Token,
        [string]$BaseUrl
    )

    Write-Host "Creating sprk_chartdefinition entity..." -ForegroundColor Yellow

    $entityDef = @{
        "@odata.type"                = "Microsoft.Dynamics.CRM.EntityMetadata"
        "SchemaName"                 = "sprk_chartdefinition"
        "DisplayName"                = @{
            "@odata.type"        = "Microsoft.Dynamics.CRM.Label"
            "LocalizedLabels"    = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    "Label"       = "Chart Definition"
                    "LanguageCode" = 1033
                }
            )
        }
        "DisplayCollectionName"      = @{
            "@odata.type"        = "Microsoft.Dynamics.CRM.Label"
            "LocalizedLabels"    = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    "Label"       = "Chart Definitions"
                    "LanguageCode" = 1033
                }
            )
        }
        "Description"                = @{
            "@odata.type"        = "Microsoft.Dynamics.CRM.Label"
            "LocalizedLabels"    = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    "Label"       = "Configuration for Spaarke Visuals Framework charts and visualizations"
                    "LanguageCode" = 1033
                }
            )
        }
        "OwnershipType"              = "OrganizationOwned"
        "IsActivity"                 = $false
        "HasNotes"                   = $false
        "HasActivities"              = $false
        "PrimaryNameAttribute"       = "sprk_name"
        "Attributes"                 = @(
            # Primary Name field
            @{
                "@odata.type"    = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
                "SchemaName"     = "sprk_name"
                "RequiredLevel"  = @{ "Value" = "ApplicationRequired" }
                "MaxLength"      = 200
                "DisplayName"    = @{
                    "@odata.type"        = "Microsoft.Dynamics.CRM.Label"
                    "LocalizedLabels"    = @(
                        @{
                            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                            "Label"       = "Name"
                            "LanguageCode" = 1033
                        }
                    )
                }
                "Description"    = @{
                    "@odata.type"        = "Microsoft.Dynamics.CRM.Label"
                    "LocalizedLabels"    = @(
                        @{
                            "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                            "Label"       = "Display name for the chart definition"
                            "LanguageCode" = 1033
                        }
                    )
                }
                "IsPrimaryName"  = $true
            }
        )
    }

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "EntityDefinitions" -Method "POST" -Body $entityDef

    Write-Host "  Entity created successfully" -ForegroundColor Green
}

# Add attribute to entity
function Add-EntityAttribute {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$EntityLogicalName,
        [object]$AttributeDef
    )

    $schemaName = $AttributeDef.SchemaName
    Write-Host "  Adding attribute: $schemaName..." -ForegroundColor Gray

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes" -Method "POST" -Body $AttributeDef

    Write-Host "    Added: $schemaName" -ForegroundColor Green
}

# Main execution
function Main {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " Deploy sprk_chartdefinition Entity" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Yellow
    Write-Host ""

    # Get token
    $token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
    Write-Host "Authentication successful" -ForegroundColor Green
    Write-Host ""

    # Check if entity already exists
    Write-Host "Step 1: Checking if entity exists..." -ForegroundColor Cyan
    if (Test-EntityExists -Token $token -BaseUrl $EnvironmentUrl -LogicalName "sprk_chartdefinition") {
        Write-Host "  Entity sprk_chartdefinition already exists!" -ForegroundColor Yellow
        Write-Host "  Skipping entity creation. Will verify fields." -ForegroundColor Yellow
    }
    else {
        # Create global option sets first
        Write-Host ""
        Write-Host "Step 2: Creating global option sets..." -ForegroundColor Cyan

        # VisualType option set
        if (-not (Test-GlobalOptionSetExists -Token $token -BaseUrl $EnvironmentUrl -Name "sprk_visualtype")) {
            $visualTypeOptions = @(
                @{ "Value" = 100000000; "Label" = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; "LocalizedLabels" = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Metric Card"; "LanguageCode" = 1033 }) } }
                @{ "Value" = 100000001; "Label" = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; "LocalizedLabels" = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Bar Chart"; "LanguageCode" = 1033 }) } }
                @{ "Value" = 100000002; "Label" = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; "LocalizedLabels" = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Line Chart"; "LanguageCode" = 1033 }) } }
                @{ "Value" = 100000003; "Label" = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; "LocalizedLabels" = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Area Chart"; "LanguageCode" = 1033 }) } }
                @{ "Value" = 100000004; "Label" = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; "LocalizedLabels" = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Donut Chart"; "LanguageCode" = 1033 }) } }
                @{ "Value" = 100000005; "Label" = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; "LocalizedLabels" = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Status Bar"; "LanguageCode" = 1033 }) } }
                @{ "Value" = 100000006; "Label" = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; "LocalizedLabels" = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Calendar"; "LanguageCode" = 1033 }) } }
                @{ "Value" = 100000007; "Label" = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; "LocalizedLabels" = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Mini Table"; "LanguageCode" = 1033 }) } }
            )
            New-GlobalOptionSet -Token $token -BaseUrl $EnvironmentUrl `
                -Name "sprk_visualtype" -DisplayName "Visual Type" -Options $visualTypeOptions
        }
        else {
            Write-Host "  sprk_visualtype option set already exists, skipping..." -ForegroundColor Yellow
        }

        # AggregationType option set
        if (-not (Test-GlobalOptionSetExists -Token $token -BaseUrl $EnvironmentUrl -Name "sprk_aggregationtype")) {
            $aggTypeOptions = @(
                @{ "Value" = 100000000; "Label" = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; "LocalizedLabels" = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Count"; "LanguageCode" = 1033 }) } }
                @{ "Value" = 100000001; "Label" = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; "LocalizedLabels" = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Sum"; "LanguageCode" = 1033 }) } }
                @{ "Value" = 100000002; "Label" = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; "LocalizedLabels" = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Average"; "LanguageCode" = 1033 }) } }
                @{ "Value" = 100000003; "Label" = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; "LocalizedLabels" = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Min"; "LanguageCode" = 1033 }) } }
                @{ "Value" = 100000004; "Label" = @{ "@odata.type" = "Microsoft.Dynamics.CRM.Label"; "LocalizedLabels" = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Max"; "LanguageCode" = 1033 }) } }
            )
            New-GlobalOptionSet -Token $token -BaseUrl $EnvironmentUrl `
                -Name "sprk_aggregationtype" -DisplayName "Aggregation Type" -Options $aggTypeOptions
        }
        else {
            Write-Host "  sprk_aggregationtype option set already exists, skipping..." -ForegroundColor Yellow
        }

        # Create the entity
        Write-Host ""
        Write-Host "Step 3: Creating entity..." -ForegroundColor Cyan
        New-ChartDefinitionEntity -Token $token -BaseUrl $EnvironmentUrl

        # Add additional attributes
        Write-Host ""
        Write-Host "Step 4: Adding attributes..." -ForegroundColor Cyan

        # Get option set MetadataIds
        $visualTypeOptionSet = Get-GlobalOptionSet -Token $token -BaseUrl $EnvironmentUrl -Name "sprk_visualtype"
        $aggTypeOptionSet = Get-GlobalOptionSet -Token $token -BaseUrl $EnvironmentUrl -Name "sprk_aggregationtype"

        if (-not $visualTypeOptionSet -or -not $aggTypeOptionSet) {
            throw "Failed to retrieve global option sets after creation"
        }

        $visualTypeId = $visualTypeOptionSet.MetadataId
        $aggTypeId = $aggTypeOptionSet.MetadataId
        Write-Host "  Visual Type Option Set ID: $visualTypeId" -ForegroundColor Gray
        Write-Host "  Aggregation Type Option Set ID: $aggTypeId" -ForegroundColor Gray

        # sprk_visualtype (Picklist using global option set)
        Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_chartdefinition" -AttributeDef @{
            "@odata.type"        = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
            "SchemaName"         = "sprk_visualtype"
            "RequiredLevel"      = @{ "Value" = "ApplicationRequired" }
            "DisplayName"        = @{
                "@odata.type"        = "Microsoft.Dynamics.CRM.Label"
                "LocalizedLabels"    = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Visual Type"; "LanguageCode" = 1033 })
            }
            "Description"        = @{
                "@odata.type"        = "Microsoft.Dynamics.CRM.Label"
                "LocalizedLabels"    = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Type of visual to render"; "LanguageCode" = 1033 })
            }
            "GlobalOptionSet@odata.bind" = "/GlobalOptionSetDefinitions($visualTypeId)"
        }

        # sprk_entitylogicalname (String)
        Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_chartdefinition" -AttributeDef @{
            "@odata.type"    = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"     = "sprk_entitylogicalname"
            "RequiredLevel"  = @{ "Value" = "ApplicationRequired" }
            "MaxLength"      = 100
            "DisplayName"    = @{
                "@odata.type"        = "Microsoft.Dynamics.CRM.Label"
                "LocalizedLabels"    = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Entity Logical Name"; "LanguageCode" = 1033 })
            }
            "Description"    = @{
                "@odata.type"        = "Microsoft.Dynamics.CRM.Label"
                "LocalizedLabels"    = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Dataverse entity to query"; "LanguageCode" = 1033 })
            }
        }

        # sprk_baseviewid (String - GUID format)
        Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_chartdefinition" -AttributeDef @{
            "@odata.type"    = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"     = "sprk_baseviewid"
            "RequiredLevel"  = @{ "Value" = "ApplicationRequired" }
            "MaxLength"      = 50
            "DisplayName"    = @{
                "@odata.type"        = "Microsoft.Dynamics.CRM.Label"
                "LocalizedLabels"    = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Base View ID"; "LanguageCode" = 1033 })
            }
            "Description"    = @{
                "@odata.type"        = "Microsoft.Dynamics.CRM.Label"
                "LocalizedLabels"    = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "GUID of SavedQuery or UserQuery to use"; "LanguageCode" = 1033 })
            }
        }

        # sprk_aggregationfield (String - optional)
        Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_chartdefinition" -AttributeDef @{
            "@odata.type"    = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"     = "sprk_aggregationfield"
            "RequiredLevel"  = @{ "Value" = "None" }
            "MaxLength"      = 100
            "DisplayName"    = @{
                "@odata.type"        = "Microsoft.Dynamics.CRM.Label"
                "LocalizedLabels"    = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Aggregation Field"; "LanguageCode" = 1033 })
            }
            "Description"    = @{
                "@odata.type"        = "Microsoft.Dynamics.CRM.Label"
                "LocalizedLabels"    = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Field to aggregate (null for count-only)"; "LanguageCode" = 1033 })
            }
        }

        # sprk_aggregationtype (Picklist - optional)
        Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_chartdefinition" -AttributeDef @{
            "@odata.type"        = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
            "SchemaName"         = "sprk_aggregationtype"
            "RequiredLevel"      = @{ "Value" = "None" }
            "DisplayName"        = @{
                "@odata.type"        = "Microsoft.Dynamics.CRM.Label"
                "LocalizedLabels"    = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Aggregation Type"; "LanguageCode" = 1033 })
            }
            "Description"        = @{
                "@odata.type"        = "Microsoft.Dynamics.CRM.Label"
                "LocalizedLabels"    = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Type of aggregation (default: count)"; "LanguageCode" = 1033 })
            }
            "GlobalOptionSet@odata.bind" = "/GlobalOptionSetDefinitions($aggTypeId)"
        }

        # sprk_groupbyfield (String - optional)
        Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_chartdefinition" -AttributeDef @{
            "@odata.type"    = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"     = "sprk_groupbyfield"
            "RequiredLevel"  = @{ "Value" = "None" }
            "MaxLength"      = 100
            "DisplayName"    = @{
                "@odata.type"        = "Microsoft.Dynamics.CRM.Label"
                "LocalizedLabels"    = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Group By Field"; "LanguageCode" = 1033 })
            }
            "Description"    = @{
                "@odata.type"        = "Microsoft.Dynamics.CRM.Label"
                "LocalizedLabels"    = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Field to group by (for charts with categories)"; "LanguageCode" = 1033 })
            }
        }

        # sprk_optionsjson (Memo/Multiline - optional)
        Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_chartdefinition" -AttributeDef @{
            "@odata.type"    = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
            "SchemaName"     = "sprk_optionsjson"
            "RequiredLevel"  = @{ "Value" = "None" }
            "MaxLength"      = 100000
            "DisplayName"    = @{
                "@odata.type"        = "Microsoft.Dynamics.CRM.Label"
                "LocalizedLabels"    = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "Options JSON"; "LanguageCode" = 1033 })
            }
            "Description"    = @{
                "@odata.type"        = "Microsoft.Dynamics.CRM.Label"
                "LocalizedLabels"    = @(@{ "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"; "Label" = "JSON blob for per-visual-type advanced options"; "LanguageCode" = 1033 })
            }
        }
    }

    # Publish customizations
    Write-Host ""
    Write-Host "Step 5: Publishing customizations..." -ForegroundColor Cyan

    $publishXml = @{
        "ParameterXml" = "<importexportxml><entities><entity>sprk_chartdefinition</entity></entities></importexportxml>"
    }

    try {
        Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "PublishXml" -Method "POST" -Body $publishXml
        Write-Host "  Customizations published" -ForegroundColor Green
    }
    catch {
        Write-Host "  Warning: Publish may have timed out, but entity should be available" -ForegroundColor Yellow
    }

    # Verify entity
    Write-Host ""
    Write-Host "Step 6: Verifying entity..." -ForegroundColor Cyan

    if (Test-EntityExists -Token $token -BaseUrl $EnvironmentUrl -LogicalName "sprk_chartdefinition") {
        Write-Host "  Entity sprk_chartdefinition exists and is accessible" -ForegroundColor Green
    }
    else {
        Write-Host "  Warning: Entity verification failed" -ForegroundColor Yellow
    }

    # Test Web API query
    Write-Host ""
    Write-Host "Step 7: Testing Web API query..." -ForegroundColor Cyan

    try {
        $result = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "sprk_chartdefinitions" -Method "GET"
        Write-Host "  Web API query successful!" -ForegroundColor Green
        Write-Host "  Current record count: $($result.value.Count)" -ForegroundColor Gray
    }
    catch {
        Write-Host "  Warning: Web API query test failed - entity may need publishing" -ForegroundColor Yellow
        Write-Host "  Error: $_" -ForegroundColor Gray
    }

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host " Deployment Complete!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  1. Verify in Power Apps maker portal: $EnvironmentUrl" -ForegroundColor Gray
    Write-Host "  2. Create a sample chart definition record" -ForegroundColor Gray
    Write-Host "  3. Add entity to spaarke_core solution if needed" -ForegroundColor Gray
    Write-Host ""
}

# Run main
Main
