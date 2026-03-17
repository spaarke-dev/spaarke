<#
.SYNOPSIS
    Creates sprk_capabilities and sprk_searchguidance fields on sprk_scope entity.

.DESCRIPTION
    Adds two new fields to the existing sprk_scope entity:
    - sprk_capabilities: Multi-select option set (search, analyze, web_search, write_back, summarize)
    - sprk_searchguidance: Multiline text (memo, max 4000 chars) for scope-specific search guidance

    Uses the Dataverse Metadata Web API. Idempotent — safe to re-run.
    Requires Azure CLI authentication.

.PARAMETER EnvironmentUrl
    The Dataverse environment URL (e.g., https://spaarkedev1.crm.dynamics.com)

.PARAMETER SolutionName
    The Dataverse solution to add the fields to (default: spaarke_core)

.EXAMPLE
    .\Create-ScopeCapabilityFields.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

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
    Write-Host " Create Scope Capability Fields on sprk_scope" -ForegroundColor Cyan
    Write-Host "========================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Yellow
    Write-Host "Solution:    $SolutionName" -ForegroundColor Yellow
    Write-Host ""

    # --- Authentication ---
    $token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
    Write-Host "Authentication successful" -ForegroundColor Green
    Write-Host ""

    # --- Step 1: Verify sprk_scope entity exists ---
    Write-Host "Step 1: Verifying sprk_scope entity exists..." -ForegroundColor Cyan
    try {
        Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "EntityDefinitions(LogicalName='sprk_scope')" -Method "GET" | Out-Null
        Write-Host "  sprk_scope entity exists." -ForegroundColor Green
    }
    catch {
        throw "sprk_scope entity does not exist. Cannot add fields to a non-existent entity."
    }

    # --- Step 2: Create sprk_capabilities (MultiSelectPicklist) ---
    Write-Host ""
    Write-Host "Step 2: Adding sprk_capabilities (Multi-Select Option Set)..." -ForegroundColor Cyan

    $capabilitiesSchemaName = "sprk_capabilities"

    if (Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_scope" -AttributeLogicalName $capabilitiesSchemaName) {
        Write-Host "  sprk_capabilities already exists, skipping." -ForegroundColor Yellow
    }
    else {
        $capabilitiesDef = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.MultiSelectPicklistAttributeMetadata"
            "SchemaName"    = "sprk_capabilities"
            "RequiredLevel" = @{ "Value" = "None" }
            "DisplayName"   = New-Label -Text "Capabilities"
            "Description"   = New-Label -Text "Tools and actions this scope contributes independently of any active playbook"
            "OptionSet"     = @{
                "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
                "IsGlobal"      = $false
                "OptionSetType" = "Picklist"
                "Options"       = @(
                    @{ "Value" = 100000000; "Label" = New-Label -Text "search" }
                    @{ "Value" = 100000001; "Label" = New-Label -Text "analyze" }
                    @{ "Value" = 100000002; "Label" = New-Label -Text "web_search" }
                    @{ "Value" = 100000003; "Label" = New-Label -Text "write_back" }
                    @{ "Value" = 100000004; "Label" = New-Label -Text "summarize" }
                )
            }
        }

        Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "EntityDefinitions(LogicalName='sprk_scope')/Attributes" -Method "POST" -Body $capabilitiesDef

        Write-Host "  sprk_capabilities created successfully" -ForegroundColor Green
    }

    # --- Step 3: Create sprk_searchguidance (Memo/Multiline text) ---
    Write-Host ""
    Write-Host "Step 3: Adding sprk_searchguidance (Multiline Text)..." -ForegroundColor Cyan

    Add-EntityAttribute -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_scope" -AttributeDef @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "SchemaName"    = "sprk_searchguidance"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 4000
        "DisplayName"   = New-Label -Text "Search Guidance"
        "Description"   = New-Label -Text "Free-text guidance for how AI web search should be scoped (e.g., prioritize authoritative legal databases)"
    }

    # --- Step 4: Publish customizations ---
    Write-Host ""
    Write-Host "Step 4: Publishing customizations for sprk_scope..." -ForegroundColor Cyan

    $publishXml = @{
        "ParameterXml" = "<importexportxml><entities><entity>sprk_scope</entity></entities></importexportxml>"
    }

    try {
        Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "PublishXml" -Method "POST" -Body $publishXml
        Write-Host "  Customizations published" -ForegroundColor Green
    }
    catch {
        Write-Host "  Warning: Publish may have timed out, but fields should be available" -ForegroundColor Yellow
    }

    # --- Step 5: Verify fields ---
    Write-Host ""
    Write-Host "Step 5: Verifying fields..." -ForegroundColor Cyan

    $fieldsToVerify = @("sprk_capabilities", "sprk_searchguidance")

    foreach ($field in $fieldsToVerify) {
        if (Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl -EntityLogicalName "sprk_scope" -AttributeLogicalName $field) {
            Write-Host "  $field - EXISTS" -ForegroundColor Green
        }
        else {
            Write-Host "  $field - MISSING" -ForegroundColor Red
        }
    }

    # Test Web API query
    try {
        $result = Invoke-DataverseApi -Token $token -BaseUrl $EnvironmentUrl `
            -Endpoint "sprk_scopes?`$select=sprk_capabilities,sprk_searchguidance&`$top=1" -Method "GET"
        Write-Host "  Web API query successful" -ForegroundColor Green
    }
    catch {
        Write-Host "  Warning: Web API query failed - fields may need publishing" -ForegroundColor Yellow
    }

    # --- Summary ---
    Write-Host ""
    Write-Host "========================================================" -ForegroundColor Green
    Write-Host " Scope Capability Fields Complete!" -ForegroundColor Green
    Write-Host "========================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Entity: sprk_scope" -ForegroundColor White
    Write-Host ""
    Write-Host "Fields created:" -ForegroundColor White
    Write-Host "  - sprk_capabilities     (multi-select: search, analyze, web_search, write_back, summarize)" -ForegroundColor Gray
    Write-Host "  - sprk_searchguidance   (multiline text, max 4000 chars)" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Option Set Values (sprk_capabilities):" -ForegroundColor White
    Write-Host "  100000000 = search" -ForegroundColor Gray
    Write-Host "  100000001 = analyze" -ForegroundColor Gray
    Write-Host "  100000002 = web_search" -ForegroundColor Gray
    Write-Host "  100000003 = write_back" -ForegroundColor Gray
    Write-Host "  100000004 = summarize" -ForegroundColor Gray
    Write-Host ""
}

# Run main
Main
