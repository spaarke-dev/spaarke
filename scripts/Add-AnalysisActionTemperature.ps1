<#
.SYNOPSIS
    Adds the sprk_temperature Decimal column to the existing sprk_analysisaction
    Dataverse entity (Hotfix Wave B-G9c1 / B6 — per-action temperature override).

.DESCRIPTION
    Adds a Decimal attribute to sprk_analysisaction that stores a per-action temperature
    override (0.0–2.0) for AI model calls. Resolves Hotfix B6 (same-file → different-
    summary) — see projects/spaarke-ai-platform-unification-r6/notes/wave-b-g9c-medium-bugs.md
    section B6 for root-cause analysis.

      - sprk_temperature : Decimal (Precision = 1, MinValue = 0.0, MaxValue = 2.0)
            When non-null, BFF AnalysisActionService reads this value into
            AnalysisAction.Temperature; AiAnalysisNodeExecutor pipes it into
            ToolExecutionContext.Temperature; the 8 tool handlers
            (SummaryHandler, SemanticSearchToolHandler, DocumentClassifierHandler,
            EntityExtractorHandler, ClauseAnalyzerHandler, RiskDetectorHandler,
            GenericAnalysisHandler, InvoiceExtractionToolHandler) pass it to
            IOpenAiClient.GetStructuredCompletionRawAsync.

    Backward-compat invariant (Wave B-G9c1): every existing sprk_analysisaction row keeps
    sprk_temperature=null. NULL semantics: handlers default to 0.0 (deterministic) —
    matching sibling structured methods (GetStructuredCompletionAsync<T>,
    StreamStructuredCompletionAsync) which hardcode Temperature=0. The previous behavior
    (using DocumentIntelligenceOptions.Temperature default 0.3) was the bug source.

    The script is idempotent: safe to re-run. It checks Test-AttributeExists before
    modification.

    Schema choice rationale (Decimal Precision=1 vs Double, Money):
    - Decimal Precision=1: matches Azure OpenAI's documented temperature granularity
      (0.0, 0.1, 0.2, …, 2.0); avoids float-precision artifacts in the operator UI.
    - MinValue=0 / MaxValue=2: matches Azure OpenAI valid range. Values >1.0 are rarely
      useful for structured output but allowed for compatibility.
    - Nullable (RequiredLevel=None): null = "use the deterministic 0.0 default."

.PARAMETER EnvironmentUrl
    The Dataverse environment URL (e.g., https://spaarkedev1.crm.dynamics.com).
    Defaults to $env:DATAVERSE_URL or Spaarke Dev.

.PARAMETER DryRun
    Preview-only mode — checks what would be created without modifying Dataverse.

.EXAMPLE
    # Preview without modifying (recommended first run)
    .\Add-AnalysisActionTemperature.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -DryRun

.EXAMPLE
    # Deploy (idempotent — safe to re-run)
    .\Add-AnalysisActionTemperature.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

.NOTES
    Project: spaarke-ai-platform-unification-r6 (Hotfix Wave B-G9c1)
    Bug: B6 — same-file → different-summary across runs
    Created: 2026-06-10
    ADR Compliance: ADR-027 (unmanaged solution; sprk_ prefix), ADR-013 (AI architecture —
                    DTO field stays AI-internal, NOT exposed via PublicContracts),
                    ADR-029 (BFF size — DTO + service mapper add minimal LOC),
                    ADR-010 (no new DI registrations)
    Pattern Source: scripts/Add-AnalysisToolRequiredCapability.ps1 (Wave 7b — sibling
                    column-add pattern; adapted Decimal type from
                    Deploy-PrecedentEntity.ps1 sprk_EffectivenessScore field).
    Confirmation Trigger: Schema change to production Dataverse entity sprk_analysisaction —
                          operator approval implicit in Hotfix Wave B-G9c1 dispatch.
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

function Add-TemperatureAttribute {
    param([string]$Token, [string]$BaseUrl)

    Write-Host "  Adding attribute: sprk_temperature..." -ForegroundColor Gray

    $attributeDef = @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.DecimalAttributeMetadata"
        "SchemaName"    = "sprk_temperature"
        "RequiredLevel" = @{ "Value" = "None" }
        "Precision"     = 1
        "MinValue"      = 0
        "MaxValue"      = 2
        "DisplayName"   = New-Label -Text "Temperature"
        "Description"   = New-Label -Text "Per-action AI temperature override (0.0–2.0). When non-null, the 8 tool handlers (Summary, SemanticSearch, DocumentClassifier, EntityExtractor, ClauseAnalyzer, RiskDetector, GenericAnalysis, InvoiceExtraction) pass this value to IOpenAiClient.GetStructuredCompletionRawAsync. NULL = deterministic 0.0 default (matches sibling structured methods' hardcoded Temperature=0). Resolves Hotfix B6 (same-file → different-summary). NOT a model-deployment override — that uses ModelDeploymentId. NOT exposed via PublicContracts (ADR-013)."
    }

    Invoke-DataverseApi -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "EntityDefinitions(LogicalName='sprk_analysisaction')/Attributes" `
        -Method "POST" -Body $attributeDef | Out-Null

    Write-Host "    Added: sprk_temperature (Decimal, Precision=1, Range 0.0–2.0)" -ForegroundColor Green
}

function Publish-AnalysisActionCustomizations {
    param([string]$Token, [string]$BaseUrl)

    Write-Host "Publishing customizations..." -ForegroundColor Cyan

    $publishXml = @{
        "ParameterXml" = "<importexportxml><entities><entity>sprk_analysisaction</entity></entities></importexportxml>"
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
    Write-Host " Add sprk_temperature (Hotfix B-G9c1)" -ForegroundColor Cyan
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

    # ---- Step 1: Verify sprk_analysisaction entity exists ----
    Write-Host "Step 1: Verifying sprk_analysisaction entity exists..." -ForegroundColor Cyan
    $entityExists = Test-EntityExists -Token $token -BaseUrl $EnvironmentUrl -LogicalName "sprk_analysisaction"

    if (-not $entityExists) {
        throw "sprk_analysisaction entity does not exist in $EnvironmentUrl. Hotfix B-G9c1 expects the entity to be present (existing pre-R6 entity). Aborting."
    }
    Write-Host "  sprk_analysisaction found" -ForegroundColor Green

    # ---- Step 2: Add column (idempotent) ----
    Write-Host ""
    Write-Host "Step 2: Adding/verifying sprk_temperature column..." -ForegroundColor Cyan

    $attrExists = Test-AttributeExists -Token $token -BaseUrl $EnvironmentUrl `
        -EntityLogicalName "sprk_analysisaction" -AttributeLogicalName "sprk_temperature"

    if ($attrExists) {
        Write-Host "  sprk_temperature already exists, skipping" -ForegroundColor Gray
    }
    else {
        if ($DryRun) {
            Write-Host "  [DRY RUN] Would add Decimal attribute sprk_temperature (nullable; Precision=1; Range 0.0-2.0)" -ForegroundColor Yellow
        }
        else {
            Add-TemperatureAttribute -Token $token -BaseUrl $EnvironmentUrl
        }
    }

    # ---- Step 3: Publish customizations ----
    Write-Host ""
    Write-Host "Step 3: Publishing customizations..." -ForegroundColor Cyan
    if ($DryRun) {
        Write-Host "  [DRY RUN] Would publish sprk_analysisaction customizations" -ForegroundColor Yellow
    }
    else {
        Publish-AnalysisActionCustomizations -Token $token -BaseUrl $EnvironmentUrl
    }

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host " Done." -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Backward-compat invariant (B-G9c1): all existing sprk_analysisaction rows" -ForegroundColor Yellow
    Write-Host "remain with sprk_temperature=null. NULL semantics: BFF handlers default to 0.0" -ForegroundColor Yellow
    Write-Host "(deterministic) — matching sibling structured methods. Operators can set per-row" -ForegroundColor Yellow
    Write-Host "overrides (e.g., 0.7 for summary creativity) via the maker portal or PATCH calls." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "SUM-CHAT@v1 binding: leave NULL (deterministic 0.0). Same /summarize streaming" -ForegroundColor Yellow
    Write-Host "path already pins Temperature=0 via StreamStructuredCompletionAsync — this column" -ForegroundColor Yellow
    Write-Host "only affects the 8 RAW-completion handler callsites." -ForegroundColor Yellow
    Write-Host ""
}

Main
