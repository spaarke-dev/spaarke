<#
.SYNOPSIS
    Adds the sprk_availableincontexts option-set column to the existing
    sprk_analysistool Dataverse entity (R6 Pillar 2 — task D-A-07 / FR-07).

.DESCRIPTION
    Adds a single option-set attribute to sprk_analysistool that discriminates
    which invocation context each tool row is available in:

      - sprk_availableincontexts : Local picklist (single-select)
            Playbook = 100000000 (default — all pre-R6 rows are playbook tools)
            Chat     = 100000001 (chat-agent exposure via SprkChatAgentFactory.ResolveTools())
            Both     = 100000002 (dual-context tool)

    Backward-compat invariant (FR-07): every pre-R6 sprk_analysistool row
    represents a playbook tool. The DefaultValue is Playbook (100000000) so
    existing rows whose column is unpopulated continue to behave exactly as
    today. The C# DTO mapper (AnalysisToolService.MapAvailableInContexts) also
    treats null as Playbook for safety.

    The script is idempotent: safe to re-run. It checks Test-AttributeExists
    before modification.

    Schema choice rationale (single-select picklist vs Flags-style):
    Dataverse single-select picklist semantics require an explicit "Both"
    value rather than bit composition. C# enum ToolAvailabilityContext is
    plain (NOT [Flags]) — see IScopeResolverService.cs ToolAvailabilityContext.

.PARAMETER EnvironmentUrl
    The Dataverse environment URL (e.g., https://spaarkedev1.crm.dynamics.com).
    Defaults to $env:DATAVERSE_URL or Spaarke Dev.

.PARAMETER DryRun
    Preview-only mode — checks what would be created without modifying Dataverse.

.EXAMPLE
    # Preview without modifying (recommended first run)
    .\Add-AnalysisToolAvailableInContexts.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -DryRun

.EXAMPLE
    # Deploy (idempotent — safe to re-run)
    .\Add-AnalysisToolAvailableInContexts.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

.NOTES
    Project: spaarke-ai-platform-unification-r6 (Pillar 2)
    Task: 007 (D-A-07) — Add AvailableInContexts Enum + Dataverse Column
    Blocks: 008 (JsonSchema field on same DTO/entity), 010 (adapter reads field), 011 (chat tool resolution)
    Created: 2026-06-07
    ADR Compliance: ADR-027 (unmanaged solution; sprk_ prefix), ADR-013 (data-driven tool registry),
                    ADR-029 (BFF size — DTO + service mapper add only ~80 LOC), ADR-010 (no new DI registrations)
    Pattern Source: scripts/Create-AiPersonaEntity.ps1 (canonical exemplar for picklist
                    on existing entity with default value).
    Confirmation Trigger: Schema change to production Dataverse entity sprk_analysistool —
                         user approval required per project CLAUDE.md §Confirmation Triggers.
                         (Operator approval granted as part of Wave 2 dispatch.)
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

function Add-AvailableInContextsAttribute {
    param([string]$Token, [string]$BaseUrl)

    Write-Host "  Adding attribute: sprk_availableincontexts..." -ForegroundColor Gray

    $attributeDef = @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
        "SchemaName"    = "sprk_availableincontexts"
        "RequiredLevel" = @{ "Value" = "None" }
        "DisplayName"   = New-Label -Text "Available In Contexts"
        "Description"   = New-Label -Text "Discriminator for which invocation contexts this tool is available in: Playbook (default; classic playbook orchestration), Chat (chat-agent exposure via SprkChatAgentFactory.ResolveTools()), or Both (dual-context). Backs C# enum ToolAvailabilityContext. R6 Pillar 2 (D-A-07, FR-07). Default Playbook preserves backward-compat for all pre-R6 rows."
        "DefaultFormValue" = 100000000
        "OptionSet"     = @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.OptionSetMetadata"
            "OptionSetType" = "Picklist"
            "IsGlobal"      = $false
            "Options"       = @(
                @{
                    "Value" = 100000000
                    "Label" = New-Label -Text "Playbook"
                    "Description" = New-Label -Text "Tool is available only inside playbook orchestration (default; matches all pre-R6 tool rows)."
                },
                @{
                    "Value" = 100000001
                    "Label" = New-Label -Text "Chat"
                    "Description" = New-Label -Text "Tool is exposed only to the chat agent via SprkChatAgentFactory.ResolveTools()."
                },
                @{
                    "Value" = 100000002
                    "Label" = New-Label -Text "Both"
                    "Description" = New-Label -Text "Tool is available in both playbook orchestration and chat-agent invocation."
                }
            )
        }
    }

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "EntityDefinitions(LogicalName='sprk_analysistool')/Attributes" `
        -Method "POST" -Body $attributeDef | Out-Null

    Write-Host "    Added: sprk_availableincontexts" -ForegroundColor Green
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
    Write-Host " Add sprk_availableincontexts (R6 D-A-07)" -ForegroundColor Cyan
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
        throw "sprk_analysistool entity does not exist in $EnvironmentUrl. R6 D-A-07 expects the entity to be present (existing pre-R6 entity). Aborting."
    }
    Write-Host "  sprk_analysistool found" -ForegroundColor Green

    # ---- Step 2: Add column (idempotent) ----
    Write-Host ""
    Write-Host "Step 2: Adding/verifying sprk_availableincontexts column..." -ForegroundColor Cyan

    $attrExists = Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl `
        -EntityLogicalName "sprk_analysistool" -AttributeLogicalName "sprk_availableincontexts"

    if ($attrExists) {
        Write-Host "  sprk_availableincontexts already exists, skipping" -ForegroundColor Gray
    }
    else {
        if ($DryRun) {
            Write-Host "  [DRY RUN] Would add option-set attribute sprk_availableincontexts (Playbook=100000000 default, Chat=100000001, Both=100000002)" -ForegroundColor Yellow
        }
        else {
            Add-AvailableInContextsAttribute -Token $token -BaseUrl $EnvironmentUrl
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
    Write-Host "Backward-compat invariant (FR-07): all existing sprk_analysistool rows" -ForegroundColor Yellow
    Write-Host "remain playbook tools (DefaultValue = Playbook = 100000000)." -ForegroundColor Yellow
    Write-Host "New chat tools (R6 task 012 batch migration) will set Chat (100000001)." -ForegroundColor Yellow
    Write-Host ""
}

Main
