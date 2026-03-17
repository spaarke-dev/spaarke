<#
.SYNOPSIS
    Creates trigger metadata fields on sprk_analysisplaybook entity.

.DESCRIPTION
    Adds four new fields to the existing sprk_analysisplaybook entity:
    - sprk_triggerphrases: Multiline text (memo, max 4000 chars) for trigger phrases (one per line)
    - sprk_recordtype: Single line text (100 chars) for record type matching
    - sprk_entitytype: Single line text (100 chars) for entity type matching
    - sprk_tags: Multiline text (memo, max 2000 chars) for tags (comma-separated)

    Uses the Dataverse Metadata Web API. Idempotent — safe to re-run.
    Requires Azure CLI authentication.

.PARAMETER EnvironmentUrl
    The Dataverse environment URL (e.g., https://spaarkedev1.crm.dynamics.com)

.PARAMETER SolutionName
    The Dataverse solution to add the fields to (default: spaarke_core)

.EXAMPLE
    .\Create-PlaybookTriggerFields.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

.NOTES
    Project: SprkChat Platform Enhancement R2
    Task: 001 - Dataverse Schema Extensions
    Created: 2026-03-17
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
    Write-Host " Create Playbook Trigger Fields on sprk_analysisplaybook" -ForegroundColor Cyan
    Write-Host "========================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Yellow
    Write-Host "Solution:    $SolutionName" -ForegroundColor Yellow
    Write-Host ""

    # --- Authentication ---
    $token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
    Write-Host "Authentication successful" -ForegroundColor Green
    Write-Host ""

    # --- Step 1: Verify sprk_analysisplaybook entity exists ---
    Write-Host "Step 1: Verifying sprk_analysisplaybook entity exists..." -ForegroundColor Cyan
    try {
        Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "EntityDefinitions(LogicalName='sprk_analysisplaybook')" -Method "GET" | Out-Null
        Write-Host "  sprk_analysisplaybook entity exists." -ForegroundColor Green
    }
    catch {
        throw "sprk_analysisplaybook entity does not exist. Cannot add fields to a non-existent entity."
    }

    # --- Step 2: Create sprk_triggerphrases (Memo/Multiline text) ---
    Write-Host ""
    Write-Host "Step 2: Adding sprk_triggerphrases (Multiline Text)..." -ForegroundColor Cyan

    Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_analysisplaybook" -AttributeDef @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "SchemaName"    = "sprk_triggerphrases"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 4000
        "DisplayName"   = New-Label -Text "Trigger Phrases"
        "Description"   = New-Label -Text "Newline-delimited natural language phrases used for semantic search seeding and exact-match fallback"
    }

    # --- Step 3: Create sprk_recordtype (String/Single line text) ---
    Write-Host ""
    Write-Host "Step 3: Adding sprk_recordtype (Single Line Text)..." -ForegroundColor Cyan

    Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_analysisplaybook" -AttributeDef @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_recordtype"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 100
        "DisplayName"   = New-Label -Text "Record Type"
        "Description"   = New-Label -Text "Record type for pre-filter in playbook dispatcher queries (e.g., matter, project, event)"
    }

    # --- Step 4: Create sprk_entitytype (String/Single line text) ---
    Write-Host ""
    Write-Host "Step 4: Adding sprk_entitytype (Single Line Text)..." -ForegroundColor Cyan

    Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_analysisplaybook" -AttributeDef @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_entitytype"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 100
        "DisplayName"   = New-Label -Text "Entity Type"
        "Description"   = New-Label -Text "Dataverse logical entity name (e.g., sprk_analysisoutput) for scoped metadata-driven dispatch"
    }

    # --- Step 5: Create sprk_tags (Memo/Multiline text) ---
    Write-Host ""
    Write-Host "Step 5: Adding sprk_tags (Multiline Text)..." -ForegroundColor Cyan

    Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_analysisplaybook" -AttributeDef @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "SchemaName"    = "sprk_tags"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 2000
        "DisplayName"   = New-Label -Text "Tags"
        "Description"   = New-Label -Text "Comma-delimited tags for grouping and filtering playbooks"
    }

    # --- Step 6: Publish customizations ---
    Write-Host ""
    Write-Host "Step 6: Publishing customizations for sprk_analysisplaybook..." -ForegroundColor Cyan

    $publishXml = @{
        "ParameterXml" = "<importexportxml><entities><entity>sprk_analysisplaybook</entity></entities></importexportxml>"
    }

    try {
        Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "PublishXml" -Method "POST" -Body $publishXml
        Write-Host "  Customizations published" -ForegroundColor Green
    }
    catch {
        Write-Host "  Warning: Publish may have timed out, but fields should be available" -ForegroundColor Yellow
    }

    # --- Step 7: Verify fields ---
    Write-Host ""
    Write-Host "Step 7: Verifying fields..." -ForegroundColor Cyan

    $fieldsToVerify = @("sprk_triggerphrases", "sprk_recordtype", "sprk_entitytype", "sprk_tags")

    foreach ($field in $fieldsToVerify) {
        if (Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_analysisplaybook" -AttributeLogicalName $field) {
            Write-Host "  $field - EXISTS" -ForegroundColor Green
        }
        else {
            Write-Host "  $field - MISSING" -ForegroundColor Red
        }
    }

    # Test Web API query
    try {
        $result = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "sprk_analysisplaybooks?`$select=sprk_triggerphrases,sprk_recordtype,sprk_entitytype,sprk_tags&`$top=1" -Method "GET"
        Write-Host "  Web API query successful" -ForegroundColor Green
    }
    catch {
        Write-Host "  Warning: Web API query failed - fields may need publishing" -ForegroundColor Yellow
    }

    # --- Summary ---
    Write-Host ""
    Write-Host "========================================================" -ForegroundColor Green
    Write-Host " Playbook Trigger Fields Complete!" -ForegroundColor Green
    Write-Host "========================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Entity: sprk_analysisplaybook" -ForegroundColor White
    Write-Host ""
    Write-Host "Fields created:" -ForegroundColor White
    Write-Host "  - sprk_triggerphrases   (multiline text, max 4000 chars)" -ForegroundColor Gray
    Write-Host "  - sprk_recordtype       (single line text, max 100 chars)" -ForegroundColor Gray
    Write-Host "  - sprk_entitytype       (single line text, max 100 chars)" -ForegroundColor Gray
    Write-Host "  - sprk_tags             (multiline text, max 2000 chars)" -ForegroundColor Gray
    Write-Host ""
}

# Run main
Main
