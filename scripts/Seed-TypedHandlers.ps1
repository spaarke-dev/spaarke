<#
.SYNOPSIS
    Seeds sprk_analysistool rows for the 8 R6 Pillar 2 typed handlers (FR-13 through FR-20).

.DESCRIPTION
    Wave-1 (pure deterministic):
      - DateExtractorHandler              (task 101, FR-17)
      - FinancialCalculatorHandler        (task 102, FR-18)
      - ClauseComparisonHandler           (task 103, FR-19)
      - FinancialCalculationToolHandler   (task 104, FR-20)

    Wave-2 (LLM-assisted):
      - EntityExtractorHandler            (task 105, FR-13)
      - ClauseAnalyzerHandler             (task 106, FR-14)
      - RiskDetectorHandler               (task 107, FR-15)
      - InvoiceExtractionToolHandler      (task 108, FR-16)

    Source rows are JSON files in infra/dataverse/ (one per handler). This script reads
    each row, upserts to sprk_analysistools (query by sprk_toolcode first; PATCH if drift,
    POST if missing).

    Wave-1 + Wave-2 tasks each contribute their own row JSON file to infra/dataverse/
    and add an entry to the $RowFiles map below.

.PARAMETER DataverseUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL or Spaarke Dev.

.PARAMETER OnlyHandler
    Optional handler class name filter — seed only the row for the named handler. Useful
    when running from a single handler's task before sibling tasks have landed.

.PARAMETER WhatIf
    Preview-only mode — describes what would be created/updated without modifying Dataverse.

.EXAMPLE
    # Preview all rows
    .\Seed-TypedHandlers.ps1 -WhatIf

.EXAMPLE
    # Deploy only the FinancialCalculatorHandler row (R6 task 102)
    .\Seed-TypedHandlers.ps1 -OnlyHandler FinancialCalculatorHandler

.EXAMPLE
    # Deploy all currently-defined handler rows (idempotent — safe to re-run)
    .\Seed-TypedHandlers.ps1 -DataverseUrl "https://spaarkedev1.crm.dynamics.com"

.NOTES
    Project    : spaarke-ai-platform-unification-r6 (Pillar 2 — typed handler workstream)
    Tasks      : 101–108 (D-H-01..D-H-08)
    Pattern    : clone of scripts/Seed-AiPersonaDefault.ps1 (idempotent UPSERT exemplar)
    ADRs       : ADR-027 (sprk_ prefix), ADR-029 (BFF size unaffected — this is data-only seeding)
    Owner      : Wave-1 / Wave-2 handler PRs each add their JSON row file + map entry
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$DataverseUrl = ($env:DATAVERSE_URL ?? "https://spaarkedev1.crm.dynamics.com"),

    [Parameter(Mandatory = $false)]
    [string]$OnlyHandler,

    [Parameter(Mandatory = $false)]
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

# -----------------------------------------------------------------------------
# Row map — each entry maps the handler class name to its JSON seed file.
# Wave-1 / Wave-2 sibling tasks ADD their entries here as they land.
# -----------------------------------------------------------------------------
$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path

$RowFiles = @{
    # Wave 1 — pure deterministic
    "DateExtractorHandler"             = "$RepoRoot/infra/dataverse/sprk_analysistool-date-extractor-row.json"
    "FinancialCalculatorHandler"       = "$RepoRoot/infra/dataverse/sprk_analysistool-financial-calculator-row.json"
    "ClauseComparisonHandler"          = "$RepoRoot/infra/dataverse/sprk_analysistool-clause-comparison-row.json"
    "FinancialCalculationToolHandler"  = "$RepoRoot/infra/dataverse/sprk_analysistool-financial-calculation-row.json"
    # Wave 2 — LLM-assisted
    "EntityExtractorHandler"           = "$RepoRoot/infra/dataverse/sprk_analysistool-entity-extractor-row.json"
    "ClauseAnalyzerHandler"            = "$RepoRoot/infra/dataverse/sprk_analysistool-clause-analyzer-row.json"
    "RiskDetectorHandler"              = "$RepoRoot/infra/dataverse/sprk_analysistool-risk-detector-row.json"
    "InvoiceExtractionToolHandler"     = "$RepoRoot/infra/dataverse/sprk_analysistool-invoice-extractor-row.json"
}

# -----------------------------------------------------------------------------
# Filter to a single handler if requested.
# -----------------------------------------------------------------------------
if ($OnlyHandler) {
    if (-not $RowFiles.ContainsKey($OnlyHandler)) {
        Write-Error "Handler '$OnlyHandler' is not registered in `$RowFiles. Known handlers: $($RowFiles.Keys -join ', ')"
        exit 1
    }
    $RowFiles = @{ $OnlyHandler = $RowFiles[$OnlyHandler] }
}

# -----------------------------------------------------------------------------
# Acquire token via az CLI (matches Seed-AiPersonaDefault.ps1 pattern).
# -----------------------------------------------------------------------------
function Get-DataverseAccessToken {
    param([string]$ResourceUrl)
    Write-Verbose "Acquiring access token for $ResourceUrl via az CLI"
    $tokenJson = az account get-access-token --resource $ResourceUrl --output json 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to acquire access token. Ensure 'az login' has been run. Output: $tokenJson"
    }
    return ($tokenJson | ConvertFrom-Json).accessToken
}

# -----------------------------------------------------------------------------
# Find existing row by sprk_toolcode (idempotency key).
# -----------------------------------------------------------------------------
function Find-ExistingRow {
    param(
        [string]$BaseUrl,
        [string]$Token,
        [string]$ToolCode
    )
    $headers = @{
        "Authorization"      = "Bearer $Token"
        "OData-MaxVersion"   = "4.0"
        "OData-Version"      = "4.0"
        "Accept"             = "application/json"
        "Prefer"             = "odata.include-annotations=*"
    }
    $query = "$BaseUrl/api/data/v9.2/sprk_analysistools?`$filter=sprk_toolcode eq '$ToolCode'&`$select=sprk_analysistoolid,sprk_name,sprk_handlerclass,sprk_toolcode"
    try {
        $response = Invoke-RestMethod -Uri $query -Headers $headers -Method Get -ErrorAction Stop
        if ($response.value -and $response.value.Count -gt 0) {
            return $response.value[0]
        }
        return $null
    }
    catch {
        Write-Warning "Query for existing row failed: $_"
        return $null
    }
}

# -----------------------------------------------------------------------------
# Build Dataverse PATCH/POST payload from the JSON row file (strip _comment_* keys).
# -----------------------------------------------------------------------------
function Get-PayloadFromRowJson {
    param([string]$JsonFilePath)

    $raw = Get-Content -Raw -Path $JsonFilePath
    $obj = $raw | ConvertFrom-Json

    $payload = [ordered]@{}
    foreach ($prop in $obj.PSObject.Properties) {
        if ($prop.Name.StartsWith("_comment")) { continue }
        # sprk_jsonschema + sprk_configuration are persisted as serialized strings.
        if ($prop.Name -in @("sprk_jsonschema", "sprk_configuration")) {
            $payload[$prop.Name] = ($prop.Value | ConvertTo-Json -Depth 50 -Compress)
        }
        else {
            $payload[$prop.Name] = $prop.Value
        }
    }
    return $payload
}

# -----------------------------------------------------------------------------
# Main upsert loop.
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "Seeding R6 Pillar 2 typed handler sprk_analysistool rows"
Write-Host "  Environment : $DataverseUrl"
Write-Host "  Rows        : $($RowFiles.Keys -join ', ')"
Write-Host "  Preview     : $WhatIf"
Write-Host ""

if (-not $WhatIf) {
    $token = Get-DataverseAccessToken -ResourceUrl $DataverseUrl
}

foreach ($handlerClass in $RowFiles.Keys) {
    $jsonPath = $RowFiles[$handlerClass]
    if (-not (Test-Path $jsonPath)) {
        Write-Warning "Row JSON file missing for $handlerClass at $jsonPath — skipping."
        continue
    }

    $payload = Get-PayloadFromRowJson -JsonFilePath $jsonPath
    $toolCode = $payload["sprk_toolcode"]

    Write-Host "--- $handlerClass ($toolCode) ---"

    if ($WhatIf) {
        Write-Host "  [WhatIf] Would UPSERT row from $jsonPath"
        Write-Host "  sprk_name         : $($payload["sprk_name"])"
        Write-Host "  sprk_handlerclass : $($payload["sprk_handlerclass"])"
        Write-Host "  sprk_toolcode     : $($payload["sprk_toolcode"])"
        continue
    }

    $existing = Find-ExistingRow -BaseUrl $DataverseUrl -Token $token -ToolCode $toolCode

    $headers = @{
        "Authorization"      = "Bearer $token"
        "OData-MaxVersion"   = "4.0"
        "OData-Version"      = "4.0"
        "Accept"             = "application/json"
        "Content-Type"       = "application/json; charset=utf-8"
        "Prefer"             = "return=representation"
    }
    $payloadJson = ($payload | ConvertTo-Json -Depth 50 -Compress)

    if ($null -eq $existing) {
        Write-Host "  No existing row — POSTing new sprk_analysistool"
        $createUrl = "$DataverseUrl/api/data/v9.2/sprk_analysistools"
        $response = Invoke-RestMethod -Uri $createUrl -Headers $headers -Method Post -Body $payloadJson -ErrorAction Stop
        Write-Host "  Created with sprk_analysistoolid = $($response.sprk_analysistoolid)"
    }
    else {
        $existingId = $existing.sprk_analysistoolid
        Write-Host "  Existing row found (sprk_analysistoolid = $existingId) — PATCHing"
        $patchUrl = "$DataverseUrl/api/data/v9.2/sprk_analysistools($existingId)"
        Invoke-RestMethod -Uri $patchUrl -Headers $headers -Method Patch -Body $payloadJson -ErrorAction Stop | Out-Null
        Write-Host "  Patched."
    }
}

Write-Host ""
Write-Host "Done."
