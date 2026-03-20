# Setup-KnowledgeDeliveryType.ps1
# Creates the sprk_knowledgedeliverytype choice field on sprk_analysisknowledge
# and backfills existing records with Inline (100000000).
#
# Usage:
#   .\Setup-KnowledgeDeliveryType.ps1
#   .\Setup-KnowledgeDeliveryType.ps1 -DryRun
#
# Prerequisites:
#   - az login (authenticated to Azure)
#   - Dataverse environment accessible

param(
    [string]$EnvironmentUrl = $env:DATAVERSE_URL,
    [switch]$DryRun = $false
)

$ErrorActionPreference = "Stop"

Write-Host "=== Setup Knowledge Delivery Type Field ===" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl"
if ($DryRun) {
    Write-Host "Mode: DRY RUN" -ForegroundColor Yellow
} else {
    Write-Host "Mode: LIVE" -ForegroundColor Green
}
Write-Host ""

# Get access token
Write-Host "Getting access token..." -ForegroundColor Gray
$token = az account get-access-token --resource $EnvironmentUrl --query 'accessToken' -o tsv
if (-not $token) {
    Write-Error "Failed to get access token. Run 'az login' first."
    exit 1
}

$headers = @{
    'Authorization' = "Bearer $token"
    'Accept'        = 'application/json'
    'Content-Type'  = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
}

# Step 1: Check if field already exists
Write-Host "Checking if sprk_knowledgedeliverytype field exists..." -ForegroundColor Gray
$metadataUrl = "$EnvironmentUrl/api/data/v9.2/EntityDefinitions(LogicalName='sprk_analysisknowledge')/Attributes(LogicalName='sprk_knowledgedeliverytype')"

$fieldExists = $false
try {
    $existingField = Invoke-RestMethod -Uri $metadataUrl -Headers $headers -Method Get
    $fieldExists = $true
    Write-Host "  Field already exists (Type: $($existingField.AttributeType))" -ForegroundColor Green
} catch {
    if ($_.Exception.Response.StatusCode -eq 404 -or $_.Exception.Response.StatusCode -eq "NotFound") {
        Write-Host "  Field does not exist - will create" -ForegroundColor Yellow
    } else {
        Write-Host "  Error checking field: $_" -ForegroundColor Red
        throw
    }
}

# Step 2: Create the choice field if it doesn't exist
if (-not $fieldExists) {
    Write-Host ""
    Write-Host "Creating sprk_knowledgedeliverytype choice field..." -ForegroundColor Cyan

    $fieldDefinition = @{
        "@odata.type" = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        SchemaName = "sprk_knowledgedeliverytype"
        DisplayName = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            LocalizedLabels = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    Label = "Knowledge Delivery Type"
                    LanguageCode = 1033
                }
            )
        }
        Description = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.Label"
            LocalizedLabels = @(
                @{
                    "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                    Label = "How the knowledge content is delivered: Inline (in Dataverse), Document (SPE), or RAG Index (AI Search)"
                    LanguageCode = 1033
                }
            )
        }
        RequiredLevel = @{
            Value = "None"
            CanBeChanged = $true
        }
        OptionSet = @{
            "@odata.type" = "Microsoft.Dynamics.CRM.OptionSetMetadata"
            IsGlobal = $false
            OptionSetType = "Picklist"
            Options = @(
                @{
                    Value = 100000000
                    Label = @{
                        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
                        LocalizedLabels = @(
                            @{
                                "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                                Label = "Inline"
                                LanguageCode = 1033
                            }
                        )
                    }
                },
                @{
                    Value = 100000001
                    Label = @{
                        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
                        LocalizedLabels = @(
                            @{
                                "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                                Label = "Document"
                                LanguageCode = 1033
                            }
                        )
                    }
                },
                @{
                    Value = 100000002
                    Label = @{
                        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
                        LocalizedLabels = @(
                            @{
                                "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                                Label = "RAG Index"
                                LanguageCode = 1033
                            }
                        )
                    }
                }
            )
        }
    } | ConvertTo-Json -Depth 10

    if ($DryRun) {
        Write-Host "  [DRY RUN] Would create field with definition:" -ForegroundColor Yellow
        Write-Host "    Options: Inline (100000000), Document (100000001), RAG Index (100000002)" -ForegroundColor Gray
    } else {
        $createUrl = "$EnvironmentUrl/api/data/v9.2/EntityDefinitions(LogicalName='sprk_analysisknowledge')/Attributes"
        try {
            $result = Invoke-RestMethod -Uri $createUrl -Headers $headers -Method Post -Body $fieldDefinition
            Write-Host "  Field created successfully" -ForegroundColor Green
        } catch {
            Write-Host "  Error creating field: $_" -ForegroundColor Red
            Write-Host "  Response: $($_.Exception.Response)" -ForegroundColor Red
            throw
        }

        # Publish entity customization
        Write-Host "  Publishing entity customization..." -ForegroundColor Gray
        $publishPayload = @{
            ParameterXml = "<importexportxml><entities><entity>sprk_analysisknowledge</entity></entities></importexportxml>"
        } | ConvertTo-Json
        try {
            Invoke-RestMethod -Uri "$EnvironmentUrl/api/data/v9.2/PublishXml" -Headers $headers -Method Post -Body $publishPayload
            Write-Host "  Published successfully" -ForegroundColor Green
        } catch {
            Write-Host "  Warning: Publish failed (may need manual publish): $_" -ForegroundColor Yellow
        }
    }
}

# Step 3: Backfill existing records with Inline (100000000)
Write-Host ""
Write-Host "Backfilling existing knowledge records..." -ForegroundColor Cyan

# Query all knowledge records that don't have the delivery type set
$queryUrl = "$EnvironmentUrl/api/data/v9.2/sprk_analysisknowledges?`$select=sprk_analysisknowledgeid,sprk_name,sprk_knowledgedeliverytype&`$top=500"
try {
    $records = Invoke-RestMethod -Uri $queryUrl -Headers $headers -Method Get
} catch {
    Write-Host "  Note: Field may not exist yet (query failed). If you just created it, try publishing first." -ForegroundColor Yellow
    Write-Host "  Error: $_" -ForegroundColor Gray
    exit 0
}

$total = $records.value.Count
$updated = 0
$skipped = 0

Write-Host "  Found $total knowledge records" -ForegroundColor Gray

foreach ($record in $records.value) {
    $id = $record.sprk_analysisknowledgeid
    $name = $record.sprk_name
    $currentType = $record.sprk_knowledgedeliverytype

    if ($null -ne $currentType) {
        $skipped++
        Write-Host "  SKIP: $name (already set to $currentType)" -ForegroundColor Gray
        continue
    }

    if ($DryRun) {
        Write-Host "  [DRY RUN] Would set $name -> Inline (100000000)" -ForegroundColor Yellow
        $updated++
    } else {
        $updateUrl = "$EnvironmentUrl/api/data/v9.2/sprk_analysisknowledges($id)"
        $updatePayload = @{ sprk_knowledgedeliverytype = 100000000 } | ConvertTo-Json
        try {
            Invoke-RestMethod -Uri $updateUrl -Headers $headers -Method Patch -Body $updatePayload
            Write-Host "  SET: $name -> Inline (100000000)" -ForegroundColor Green
            $updated++
        } catch {
            Write-Host "  ERROR: Failed to update $name : $_" -ForegroundColor Red
        }
    }
}

Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
Write-Host "  Total records: $total"
Write-Host "  Updated to Inline: $updated" -ForegroundColor Green
Write-Host "  Already set (skipped): $skipped" -ForegroundColor Gray
