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

    Wave-7 (Q9 chat-tool batch migration — trivial group):
      - TextRefinementHandler             (3 rows via method-discriminator)
          TEXT-REFINE / TEXT-KEYPOINTS / TEXT-SUMMARY

    Source rows are JSON files in infra/dataverse/ (one per row, not per handler). This
    script reads each row, upserts to sprk_analysistools. Upsert key is sprk_toolcode with
    a safety filter requiring sprk_name to start with 'SYS-' (refined 2026-06-08 from the
    earlier handler-class key when Wave-7 introduced rows sharing one handler class via
    method-discriminator): toolcode is unique per row even when multiple rows share a
    handler class. The SYS- prefix prevents accidental PATCH of legacy non-R6 rows even
    if a toolcode collision occurs in the future. PATCH if drift, POST if missing.

    Wave-1, Wave-2, and Wave-7 tasks each contribute their own row JSON file to
    infra/dataverse/ and add an entry to the $RowFiles map below. Map keys are the
    sprk_toolcode (unique per row).

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

# R6 audit item 1 — dot-source the JSON Schema validator helper so seeds get
# write-time validation (admins catch malformed schemas immediately instead of
# silently passing them through to fail at LLM invocation).
. (Join-Path $PSScriptRoot "Test-AnalysisToolSchemaValid.ps1")

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
    # Wave 7 — chat-tool migration (legacy hardcoded tool → typed handler)
    "AnalysisQueryHandler"             = "$RepoRoot/infra/dataverse/sprk_analysistool-analysis-query-row.json"
    # Wave 7 — TextRefinementHandler serves 3 rows via the method-discriminator in
    # sprk_configuration (refine / keypoints / summary). Because the handler class is
    # the same for all three, the upsert key MUST be sprk_toolcode (not handler-class)
    # for these rows — see Find-ExistingRow's $ToolCode parameter (added 2026-06-08).
    # Map keys are the unique sprk_toolcode values; iteration variable is just a label.
    "TEXT-REFINE"                      = "$RepoRoot/infra/dataverse/sprk_analysistool-text-refine-row.json"
    "TEXT-KEYPOINTS"                   = "$RepoRoot/infra/dataverse/sprk_analysistool-text-keypoints-row.json"
    "TEXT-SUMMARY"                     = "$RepoRoot/infra/dataverse/sprk_analysistool-text-summary-row.json"
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
# Find existing row by sprk_handlerclass (idempotency key) WITH sprk_name 'SYS-%'
# safety filter, optionally disambiguated by sprk_toolcode.
#
# Why sprk_handlerclass + (optional) sprk_toolcode?
#   Per R6 audit item 4 consolidation (2026-06-07), sprk_handlerclass = nameof(handler)
#   is the stable runtime routing key. For 1:1 handler→row rows (Wave 1, Wave 2)
#   handler-class alone is unique. For multi-row-per-handler rows (Wave 7
#   TextRefinementHandler — 3 rows via method-discriminator, 2026-06-08), the
#   $ToolCode parameter must be supplied to disambiguate. Caller behavior:
#     - If only $HandlerClass given → match by handler-class (existing behavior).
#     - If both given → match by handler-class AND toolcode (Wave 7 path).
#
# Why the 'SYS-%' name filter?
#   Pre-R6 legacy seed-data (`scripts/seed-data/Deploy-Tools.ps1`) created
#   `Clause Analyzer`/`Date Extractor`/etc. rows with the same sprk_handlerclass
#   values but no SYS- prefix. The audit item 4 consolidation PATCHed those into
#   `SYS-*` canonical rows. The startswith filter ensures future runs only touch
#   the R6 canonical row even if some other system pathway reintroduces a
#   non-SYS handler-class collision.
# -----------------------------------------------------------------------------
function Find-ExistingRow {
    param(
        [string]$BaseUrl,
        [string]$Token,
        [string]$HandlerClass,
        [string]$ToolCode
    )
    $headers = @{
        "Authorization"      = "Bearer $Token"
        "OData-MaxVersion"   = "4.0"
        "OData-Version"      = "4.0"
        "Accept"             = "application/json"
        "Prefer"             = "odata.include-annotations=*"
    }
    $filter = "sprk_handlerclass eq '$HandlerClass' and startswith(sprk_name,'SYS-')"
    if (-not [string]::IsNullOrWhiteSpace($ToolCode)) {
        $filter = "$filter and sprk_toolcode eq '$ToolCode'"
    }
    $query = "$BaseUrl/api/data/v9.2/sprk_analysistools?`$filter=$filter&`$select=sprk_analysistoolid,sprk_name,sprk_handlerclass,sprk_toolcode"
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

Write-Host "  Upsert key  : sprk_handlerclass + sprk_toolcode (when multi-row-per-handler) with sprk_name LIKE 'SYS-%' safety filter"
Write-Host ""

if (-not $WhatIf) {
    $token = Get-DataverseAccessToken -ResourceUrl $DataverseUrl
}

foreach ($rowKey in $RowFiles.Keys) {
    $jsonPath = $RowFiles[$rowKey]
    if (-not (Test-Path $jsonPath)) {
        Write-Warning "Row JSON file missing for $rowKey at $jsonPath — skipping."
        continue
    }

    $payload = Get-PayloadFromRowJson -JsonFilePath $jsonPath
    $toolCode = $payload["sprk_toolcode"]
    # NOTE: read handler class from the payload (not the map key) because Wave 7
    # rows use toolcode as the map key, since one handler class serves multiple rows.
    $handlerClass = $payload["sprk_handlerclass"]

    Write-Host "--- $rowKey ($toolCode → $handlerClass) ---"

    # R6 audit item 1: catalog-write-time JSON Schema validation. We refuse to
    # seed a row whose sprk_jsonschema is structurally invalid — admins see the
    # error here rather than at LLM invocation. The BFF is still the authoritative
    # validator at chat-session start; this is fast-feedback defense-in-depth.
    if ($payload.Contains("sprk_jsonschema") -and -not [string]::IsNullOrWhiteSpace($payload["sprk_jsonschema"])) {
        $schemaOk = Test-AnalysisToolSchemaValid -SchemaJson $payload["sprk_jsonschema"] -ToolName $toolCode
        if (-not $schemaOk) {
            Write-Error "[$toolCode] sprk_jsonschema failed structural validation. Fix the JSON in $jsonPath before re-running. (See warnings above.)"
            continue
        }
    }

    if ($WhatIf) {
        Write-Host "  [WhatIf] Would UPSERT row from $jsonPath"
        Write-Host "  sprk_name         : $($payload["sprk_name"])"
        Write-Host "  sprk_handlerclass : $($payload["sprk_handlerclass"])"
        Write-Host "  sprk_toolcode     : $($payload["sprk_toolcode"])"
        continue
    }

    # When multiple rows share a handler class (Wave 7 TextRefinementHandler),
    # pass the toolcode to disambiguate the upsert lookup.
    $existing = Find-ExistingRow -BaseUrl $DataverseUrl -Token $token -HandlerClass $handlerClass -ToolCode $toolCode

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
