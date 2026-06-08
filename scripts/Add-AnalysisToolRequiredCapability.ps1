<#
.SYNOPSIS
    Adds the sprk_requiredcapability single-line text column to the existing
    sprk_analysistool Dataverse entity (R6 Pillar 2 — Wave 7b infrastructure).

.DESCRIPTION
    Adds a single-line text attribute to sprk_analysistool that stores the canonical
    capability constant (e.g. "verify_citations", "write_back", "web_search",
    "code_interpreter", "legal_research", "reanalyze") REQUIRED for a chat-available
    tool to register with the LLM at chat-session start.

      - sprk_requiredcapability : Single-line text (String), nullable, MaxLength 100
            When non-null, the data-driven block in SprkChatAgentFactory.ResolveTools()
            skips the tool unless the current playbook's capabilities (or the
            CoreCapabilities allow-list in standalone-chat mode) contains a
            case-insensitive match for the stored string.

    Backward-compat invariant (Wave 7b): every existing sprk_analysistool row keeps
    sprk_requiredcapability=null, which is interpreted as "always available" — exactly
    the behavior pre-Wave 7b. Only rows MIGRATED in Waves 7c (VerifyCitations), 8
    (LegalResearch/WebSearch/CodeInterpreter), and 9 (WorkingDocumentTools) set this
    column to their canonical PlaybookCapabilities constant.

    The filter is enforced at chat-session start (SprkChatAgentFactory.ResolveTools()
    data-driven block) — NOT at this column's RequiredLevel — because (1) most rows
    legitimately have no capability gate (e.g., AnalysisQuery, TextRefinement) and
    (2) the chat-side resolver is the single source of truth for capability
    enforcement (mirrors the hardcoded `if (capabilities.Contains(X))` blocks that
    this column REPLACES per Wave 7b).

    Distinction from ADR-018 feature flags: this is NOT a feature flag. It is a
    per-tool authorization filter on existing tools — analogous to ACL entries
    rather than kill-switches. See HandlerRegistrationConventions.md "Capability-
    Gated Tools" section for the contract.

    The script is idempotent: safe to re-run. It checks Test-AttributeExists before
    modification.

    Schema choice rationale (single-line text vs picklist):
    Capability values are CANONICAL string constants defined in C# (PlaybookCapabilities
    class) and indexed by the Dataverse global multi-select choice on sprk_analysisplaybook.
    Storing the capability NAME (not the option-set int) on the tool side keeps the column
    self-documenting, decouples it from option-set integer codes, and supports forward
    compatibility with capabilities that haven't been added yet (admin can write
    "future_capability" and the filter is a no-op until matching playbook capabilities
    exist). MaxLength=100 comfortably accommodates the longest canonical value
    ("legal_research" at 14 chars) with headroom for future names.

.PARAMETER EnvironmentUrl
    The Dataverse environment URL (e.g., https://spaarkedev1.crm.dynamics.com).
    Defaults to $env:DATAVERSE_URL or Spaarke Dev.

.PARAMETER DryRun
    Preview-only mode — checks what would be created without modifying Dataverse.

.EXAMPLE
    # Preview without modifying (recommended first run)
    .\Add-AnalysisToolRequiredCapability.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -DryRun

.EXAMPLE
    # Deploy (idempotent — safe to re-run)
    .\Add-AnalysisToolRequiredCapability.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

.NOTES
    Project: spaarke-ai-platform-unification-r6 (Pillar 2)
    Wave: 7b — Per-playbook capability filter infrastructure
    Blocks: Wave 7c (VerifyCitations re-attempt), Wave 8 (LegalResearch / WebSearch /
            CodeInterpreter migrations), Wave 9 (WorkingDocumentTools migration)
    Depends on: 008 (sprk_jsonschema column), 007 (sprk_availableincontexts column)
    Created: 2026-06-08
    ADR Compliance: ADR-027 (unmanaged solution; sprk_ prefix), ADR-013 (AI architecture —
                    DTO field stays AI-internal, NOT exposed via PublicContracts),
                    ADR-029 (BFF size — DTO + service mapper add ~40 LOC),
                    ADR-010 (no new DI registrations),
                    ADR-018 (this is NOT a feature flag — it is an authorization filter)
    Pattern Source: scripts/Add-AnalysisToolJsonSchema.ps1 (R6 task 008 — sibling column
                    on same entity; idempotent Memo column pattern adapted to single-line
                    text for this column's smaller payload).
    Confirmation Trigger: Schema change to production Dataverse entity sprk_analysistool —
                         operator approval implicit in Wave 7b dispatch.
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

function Add-RequiredCapabilityAttribute {
    param([string]$Token, [string]$BaseUrl)

    Write-Host "  Adding attribute: sprk_requiredcapability..." -ForegroundColor Gray

    $attributeDef = @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = "sprk_requiredcapability"
        "RequiredLevel" = @{ "Value" = "None" }
        "MaxLength"     = 100
        "FormatName"    = @{ "Value" = "Text" }
        "DisplayName"   = New-Label -Text "Required Capability"
        "Description"   = New-Label -Text "When non-null, the canonical PlaybookCapabilities constant (e.g., 'verify_citations', 'write_back', 'web_search', 'code_interpreter', 'legal_research', 'reanalyze') that the current playbook's capability set MUST contain (case-insensitive) for this tool to register with the chat agent. Null = always available (existing behavior pre-Wave 7b). Filter enforced at chat-session start in SprkChatAgentFactory.ResolveTools() data-driven block — replaces the hardcoded `if (capabilities.Contains(X))` gates removed in Waves 7c / 8 / 9. NOT a feature flag (ADR-018): this is a per-tool authorization filter on existing tools."
    }

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "EntityDefinitions(LogicalName='sprk_analysistool')/Attributes" `
        -Method "POST" -Body $attributeDef | Out-Null

    Write-Host "    Added: sprk_requiredcapability" -ForegroundColor Green
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
    Write-Host " Add sprk_requiredcapability (Wave 7b)" -ForegroundColor Cyan
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
        throw "sprk_analysistool entity does not exist in $EnvironmentUrl. Wave 7b expects the entity to be present (existing pre-R6 entity). Aborting."
    }
    Write-Host "  sprk_analysistool found" -ForegroundColor Green

    # ---- Step 2: Add column (idempotent) ----
    Write-Host ""
    Write-Host "Step 2: Adding/verifying sprk_requiredcapability column..." -ForegroundColor Cyan

    $attrExists = Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl `
        -EntityLogicalName "sprk_analysistool" -AttributeLogicalName "sprk_requiredcapability"

    if ($attrExists) {
        Write-Host "  sprk_requiredcapability already exists, skipping" -ForegroundColor Gray
    }
    else {
        if ($DryRun) {
            Write-Host "  [DRY RUN] Would add String attribute sprk_requiredcapability (nullable; MaxLength=100)" -ForegroundColor Yellow
        }
        else {
            Add-RequiredCapabilityAttribute -Token $token -BaseUrl $EnvironmentUrl
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
    Write-Host "Backward-compat invariant (Wave 7b): all existing sprk_analysistool rows" -ForegroundColor Yellow
    Write-Host "remain with sprk_requiredcapability=null (always-available — matches pre-Wave-7b" -ForegroundColor Yellow
    Write-Host "behavior). Waves 7c (VerifyCitations), 8 (LegalResearch/WebSearch/CodeInterpreter)," -ForegroundColor Yellow
    Write-Host "and 9 (WorkingDocumentTools) will populate this field on the migrated rows to" -ForegroundColor Yellow
    Write-Host "preserve the today-hardcoded `if (capabilities.Contains(X))` gates exactly." -ForegroundColor Yellow
    Write-Host ""
}

Main
