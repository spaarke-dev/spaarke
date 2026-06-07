<#
.SYNOPSIS
    Adds the sprk_jsonschema Memo (multi-line text) column to the existing
    sprk_analysistool Dataverse entity (R6 Pillar 2 — task D-A-08 / FR-08).

.DESCRIPTION
    Adds a single Memo attribute to sprk_analysistool that stores the JSON Schema
    document (Draft 2020-12 family) describing the tool's parameter shape for LLM
    function-calling:

      - sprk_jsonschema : Memo (multi-line text), nullable, MaxLength 100000 (~100 KB)
            Stores the JSON Schema string consumed by ToolHandlerToAIFunctionAdapter
            (task D-A-10) to wrap an IToolHandler as a Microsoft.Extensions.AI.AIFunction.
            The LLM sees this schema as the function's parameter declaration.

    Backward-compat invariant (FR-08): every pre-R6 sprk_analysistool row represents
    a playbook tool whose handler invocation is in-process C# — the column stays null.
    The C# DTO mapper (AnalysisToolService.MapJsonSchema) treats null as "no schema set"
    and validates non-null values as well-formed JSON, logging + nulling malformed values
    rather than passing garbage to the LLM downstream.

    Required-for-chat rule: chat-available tools (rows whose sprk_availableincontexts ∋
    Chat or Both per task D-A-07) MUST populate this column. That rule is enforced at the
    chat-side resolver (task 011) — NOT at this column's RequiredLevel — because the
    column must remain assignable from playbook-only rows. Task 012 batch migration
    populates this field for the 10 migrated chat tools.

    The script is idempotent: safe to re-run. It checks Test-AttributeExists before
    modification.

    Schema choice rationale (Memo vs single-line text):
    JSON Schema documents for production tools can run hundreds to thousands of
    characters (e.g., DocumentSearch tool with paramters for query, filters, page size,
    etc.). Single-line text (NVARCHAR(4000)) is insufficient. MaxLength=100000 matches
    the Spaarke convention for "system prompt"-shape multi-line text columns
    (see scripts/Create-AiPersonaEntity.ps1 sprk_systemprompt).

.PARAMETER EnvironmentUrl
    The Dataverse environment URL (e.g., https://spaarkedev1.crm.dynamics.com).
    Defaults to $env:DATAVERSE_URL or Spaarke Dev.

.PARAMETER DryRun
    Preview-only mode — checks what would be created without modifying Dataverse.

.EXAMPLE
    # Preview without modifying (recommended first run)
    .\Add-AnalysisToolJsonSchema.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -DryRun

.EXAMPLE
    # Deploy (idempotent — safe to re-run)
    .\Add-AnalysisToolJsonSchema.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

.NOTES
    Project: spaarke-ai-platform-unification-r6 (Pillar 2)
    Task: 008 (D-A-08) — Add JsonSchema Field to AnalysisTool DTO + Dataverse Column
    Blocks: 010 (adapter reads field), 011 (chat tool resolver), 012 (chat tool batch migration)
    Depends on: 006 (IToolHandler rename), 007 (sprk_availableincontexts column)
    Created: 2026-06-07
    ADR Compliance: ADR-027 (unmanaged solution; sprk_ prefix), ADR-013 (AI architecture — data-driven
                    tool registry; the JSON Schema is the LLM-facing parameter contract),
                    ADR-029 (BFF size — DTO + service mapper add ~70 LOC),
                    ADR-010 (no new DI registrations)
    Pattern Source: scripts/Add-AnalysisToolAvailableInContexts.ps1 (R6 task 007 — sibling column on
                    same entity) + scripts/Create-AiPersonaEntity.ps1 sprk_systemprompt (canonical
                    Memo attribute exemplar with MaxLength 100000).
    Confirmation Trigger: Schema change to production Dataverse entity sprk_analysistool —
                         user approval required per project CLAUDE.md §Confirmation Triggers.
                         (Operator approval granted as part of Wave 3 dispatch.)
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = ($env:DATAVERSE_URL ?? "https://spaarkedev1.crm.dynamics.com"),

    [Parameter(Mandatory = $false)]
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# -----------------------------------------------------------------------------
# Authentication
# -----------------------------------------------------------------------------

function Get-DataverseToken {
    param([string]$EnvironmentUrl)

    Write-Host "Getting authentication token from Azure CLI..." -ForegroundColor Cyan
    $tokenResult = az account get-access-token --resource $EnvironmentUrl --query "accessToken" -o tsv 2>&1

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to get token from Azure CLI. Error: $tokenResult. Run 'az login' first."
    }

    return $tokenResult.Trim()
}

# -----------------------------------------------------------------------------
# Web API Helpers
# -----------------------------------------------------------------------------

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
        return Invoke-RestMethod @params
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

# -----------------------------------------------------------------------------
# Idempotency Checks
# -----------------------------------------------------------------------------

function Test-EntityExists {
    param([string]$Token, [string]$BaseUrl, [string]$LogicalName)
    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$LogicalName')" -Method "GET" | Out-Null
        return $true
    }
    catch {
        return $false
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
            -Endpoint "EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')" `
            -Method "GET" | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

# -----------------------------------------------------------------------------
# Attribute Creation
# -----------------------------------------------------------------------------

function Add-JsonSchemaAttribute {
    param([string]$Token, [string]$BaseUrl)

    Write-Host "  Adding attribute: sprk_jsonschema..." -ForegroundColor Gray

    $attributeDef = @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
        "SchemaName"    = "sprk_jsonschema"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 100000
        "DisplayName"   = New-Label -Text "JSON Schema"
        "Description"   = New-Label -Text "JSON Schema (Draft 2020-12 family) describing the tool's parameter shape for LLM function-calling. Consumed by ToolHandlerToAIFunctionAdapter (R6 task D-A-10) to wrap an IToolHandler as a Microsoft.Extensions.AI.AIFunction. Nullable for backward-compat with pre-R6 playbook-only rows; REQUIRED for chat-available tools (enforced at chat-side resolver per FR-08). Task 012 batch migration populates this field for the 10 migrated chat tools."
    }

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "EntityDefinitions(LogicalName='sprk_analysistool')/Attributes" `
        -Method "POST" -Body $attributeDef | Out-Null

    Write-Host "    Added: sprk_jsonschema" -ForegroundColor Green
}

function Publish-AnalysisToolCustomizations {
    param([string]$Token, [string]$BaseUrl)

    Write-Host "Publishing customizations..." -ForegroundColor Cyan

    $publishXml = @{
        "ParameterXml" = "<importexportxml><entities><entity>sprk_analysistool</entity></entities></importexportxml>"
    }

    try {
        Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "PublishXml" -Method "POST" -Body $publishXml | Out-Null
        Write-Host "  Customizations published" -ForegroundColor Green
    }
    catch {
        Write-Host "  Warning: Publish may have timed out, but column should be available shortly" -ForegroundColor Yellow
    }
}

# -----------------------------------------------------------------------------
# Main
# -----------------------------------------------------------------------------

function Main {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " Add sprk_jsonschema (R6 D-A-08)" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Yellow
    if ($DryRun) {
        Write-Host "MODE: DRY RUN (no Dataverse modifications)" -ForegroundColor Yellow
    }
    Write-Host ""

    # ---- Step 0: Get auth token ----
    $token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
    Write-Host "Authentication successful" -ForegroundColor Green
    Write-Host ""

    # ---- Step 1: Verify sprk_analysistool entity exists ----
    Write-Host "Step 1: Verifying sprk_analysistool entity exists..." -ForegroundColor Cyan
    $entityExists = Test-EntityExists -Token $token -BaseUrl $EnvironmentUrl -LogicalName "sprk_analysistool"

    if (-not $entityExists) {
        throw "sprk_analysistool entity does not exist in $EnvironmentUrl. R6 D-A-08 expects the entity to be present (existing pre-R6 entity). Aborting."
    }
    Write-Host "  sprk_analysistool found" -ForegroundColor Green

    # ---- Step 2: Add column (idempotent) ----
    Write-Host ""
    Write-Host "Step 2: Adding/verifying sprk_jsonschema column..." -ForegroundColor Cyan

    $attrExists = Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl `
        -EntityLogicalName "sprk_analysistool" -AttributeLogicalName "sprk_jsonschema"

    if ($attrExists) {
        Write-Host "  sprk_jsonschema already exists, skipping" -ForegroundColor Gray
    }
    else {
        if ($DryRun) {
            Write-Host "  [DRY RUN] Would add Memo attribute sprk_jsonschema (nullable; MaxLength=100000)" -ForegroundColor Yellow
        }
        else {
            Add-JsonSchemaAttribute -Token $token -BaseUrl $EnvironmentUrl
        }
    }

    # ---- Step 3: Publish customizations ----
    Write-Host ""
    Write-Host "Step 3: Publishing customizations..." -ForegroundColor Cyan
    if ($DryRun) {
        Write-Host "  [DRY RUN] Would publish sprk_analysistool customizations" -ForegroundColor Yellow
    }
    else {
        Publish-AnalysisToolCustomizations -Token $token -BaseUrl $EnvironmentUrl
    }

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " Done." -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Backward-compat invariant (FR-08): all existing sprk_analysistool rows" -ForegroundColor Yellow
    Write-Host "remain with sprk_jsonschema=null. Pre-R6 playbook-only tools never invoke" -ForegroundColor Yellow
    Write-Host "the LLM function-calling pathway, so the missing schema is expected." -ForegroundColor Yellow
    Write-Host "Task 012 batch migration will populate this field for the 10 migrated chat tools." -ForegroundColor Yellow
    Write-Host ""
}

Main
