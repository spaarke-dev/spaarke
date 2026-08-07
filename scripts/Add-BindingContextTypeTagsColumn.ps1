<#
.SYNOPSIS
    Adds the sprk_contexttypetags column to the sprk_playbookconsumer (Binding) entity.

.DESCRIPTION
    Adds one new field to the existing sprk_playbookconsumer entity:
    - sprk_contexttypetags: Single line text (string, max 200 chars) — comma-separated
      list of workspace context-type tags from the closed WidgetContextType set
      { email, document, compose-doc, matter-grid, dashboard, calendar }.

    This MIRRORS the existing sprk_surfaces column pattern (NVARCHAR CSV read by the
    BFF ConsumerRoutingService and split into a token list). It is the DATA CARRIER for
    ADR-039-compliant deterministic pre-filtering of proactive follow-on chips by the
    active tab's context type (spaarkeai-assistant-enhancements-r2, FR-B2 / FR-D11).

    Project-scoped exception (CLAUDE.md §6.5 Path A, owner-approved 2026-08-05): overrides
    FR-B2 "no deploy" because no context-type field existed — a carrier must be created.

    Uses the Dataverse Metadata Web API. Idempotent — safe to re-run.
    Requires Azure CLI authentication (az login).

.PARAMETER EnvironmentUrl
    The Dataverse environment URL (default: https://spaarkedev1.crm.dynamics.com)

.EXAMPLE
    .\Add-BindingContextTypeTagsColumn.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

.NOTES
    Project: spaarkeai-assistant-enhancements-r2
    Task: 021 - Context-type tag column + BFF contract + seed (Option C)
    Created: 2026-08-05
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com"
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
    Write-Host "==============================================================" -ForegroundColor Cyan
    Write-Host " Add sprk_contexttypetags to sprk_playbookconsumer (Binding)" -ForegroundColor Cyan
    Write-Host "==============================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Yellow
    Write-Host ""

    # --- Authentication ---
    $token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
    Write-Host "Authentication successful" -ForegroundColor Green
    Write-Host ""

    # --- Step 1: Verify sprk_playbookconsumer entity exists ---
    Write-Host "Step 1: Verifying sprk_playbookconsumer entity exists..." -ForegroundColor Cyan
    try {
        Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "EntityDefinitions(LogicalName='sprk_playbookconsumer')" -Method "GET" | Out-Null
        Write-Host "  sprk_playbookconsumer entity exists." -ForegroundColor Green
    }
    catch {
        throw "sprk_playbookconsumer entity does not exist. Cannot add fields to a non-existent entity."
    }

    # --- Step 2: Create sprk_contexttypetags (String/Single line text) ---
    Write-Host ""
    Write-Host "Step 2: Adding sprk_contexttypetags (Single Line Text, mirrors sprk_surfaces)..." -ForegroundColor Cyan

    Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_playbookconsumer" -AttributeDef @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_contexttypetags"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 200
        "DisplayName"   = New-Label -Text "Context Type Tags"
        "Description"   = New-Label -Text "Comma-separated workspace context-type tags from the closed set { email, document, compose-doc, matter-grid, dashboard, calendar }. Scopes ADR-039 proactive follow-on chip selection to the active tab's context type (deterministic pre-filter DATA, not a classifier). Empty = offered for any context. Mirrors sprk_surfaces."
    }

    # --- Step 3: Publish customizations ---
    Write-Host ""
    Write-Host "Step 3: Publishing customizations for sprk_playbookconsumer..." -ForegroundColor Cyan

    $publishXml = @{
        "ParameterXml" = "<importexportxml><entities><entity>sprk_playbookconsumer</entity></entities></importexportxml>"
    }

    try {
        Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "PublishXml" -Method "POST" -Body $publishXml
        Write-Host "  Customizations published" -ForegroundColor Green
    }
    catch {
        Write-Host "  Warning: Publish may have timed out, but the field should be available" -ForegroundColor Yellow
    }

    # --- Step 4: Verify field ---
    Write-Host ""
    Write-Host "Step 4: Verifying sprk_contexttypetags..." -ForegroundColor Cyan

    if (Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_playbookconsumer" -AttributeLogicalName "sprk_contexttypetags") {
        Write-Host "  sprk_contexttypetags - EXISTS" -ForegroundColor Green
    }
    else {
        Write-Host "  sprk_contexttypetags - MISSING" -ForegroundColor Red
    }

    # Test Web API query
    try {
        Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "sprk_playbookconsumers?`$select=sprk_contexttypetags&`$top=1" -Method "GET" | Out-Null
        Write-Host "  Web API query successful" -ForegroundColor Green
    }
    catch {
        Write-Host "  Warning: Web API query failed - field may need publishing" -ForegroundColor Yellow
    }

    # --- Summary ---
    Write-Host ""
    Write-Host "==============================================================" -ForegroundColor Green
    Write-Host " Context Type Tags Column Complete!" -ForegroundColor Green
    Write-Host "==============================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Entity: sprk_playbookconsumer" -ForegroundColor White
    Write-Host "Field:  sprk_contexttypetags (single line text, max 200 chars)" -ForegroundColor Gray
    Write-Host ""
}

# Run main
Main
