<#
.SYNOPSIS
    Verifies the existing sprk_outputschemajson Memo column on sprk_analysisaction
    (R6 Pillar 5 — task D-B-01 / task 030 — Option A reshape).

.DESCRIPTION
    This is a VERIFICATION script, NOT an ADD script. R6 task 030's planning POML proposed
    adding a NEW column `sprk_outputschema` to `sprk_analysisaction`. Discovery during task
    execution found that an equivalent custom Memo column ALREADY EXISTS on the entity
    under the LogicalName `sprk_outputschemajson` (note the trailing `json`). That column is
    structurally identical to the planned column (Memo, RequiredLevel=None) with a more
    generous MaxLength (1,048,576 / ~1 MB) than the planned 100 KB cap. It is in active
    production use by `PlaybookExecutionEngine.cs` for Structured Outputs JSON Schema
    binding to Azure OpenAI Structured Outputs streaming.

    Per the autonomous-decision applied at task 030 (Option A, "ADRs are defaults"), this
    task reuses the existing column rather than creating a duplicate or renaming. The
    deliverable for task 030 thus shifts from "deploy new column" to "verify existing column
    + document discovery". This script is the verification deliverable.

    Full discovery details are in
    `projects/spaarke-ai-platform-unification-r6/notes/task-030-schema-deployment-evidence.md`.

    The script:
      (1) Confirms the entity sprk_analysisaction exists;
      (2) Reads the metadata of attribute sprk_outputschemajson;
      (3) Asserts the metadata invariants required by R6 Pillar 5 (Memo, MaxLength >= 100000,
          RequiredLevel == None, IsCustomAttribute == true);
      (4) Queries 5 rows via `sprk_analysisactions?$select=sprk_actioncode,sprk_outputschemajson`
          to confirm the column is queryable and reports per-row population state;
      (5) Reports a summary; exits non-zero if any assertion fails.

    Idempotent — safe to re-run any number of times. Read-only: makes ONLY GET calls; no
    POST / PATCH / DELETE. Does NOT modify Dataverse in any way.

.PARAMETER EnvironmentUrl
    The Dataverse environment URL. Defaults to $env:DATAVERSE_URL or
    https://spaarkedev1.crm.dynamics.com (Spaarke Dev).

.EXAMPLE
    # Verify against Spaarke Dev (default)
    .\Verify-OutputSchemaField.ps1

.EXAMPLE
    # Verify against a different environment
    .\Verify-OutputSchemaField.ps1 -EnvironmentUrl "https://other.crm.dynamics.com"

.NOTES
    Project: spaarke-ai-platform-unification-r6 (Pillar 5 — Schema-Aware Output)
    Task: 030 (D-B-01) — Verify outputSchema column on sprk_analysisaction
    Decision: Option A (reuse existing sprk_outputschemajson; do NOT add a new column)
    Decision basis: project CLAUDE.md "ADRs Are Defaults" + dispatch prompt's explicit
                    Confirmation Trigger for "column already exists with different metadata"
    Created: 2026-06-08
    ADR Compliance:
        ADR-027 (solution management): no schema deployment — discovery only;
        ADR-029 (publish hygiene): BFF publish-size delta = 0 MB;
        ADR-010 (DI minimalism): N/A — no service registrations.
    Pattern Source: scripts/Add-AnalysisToolJsonSchema.ps1 (R6 task 008 — sibling column
                    on sprk_analysistool); inverted from "add" to "verify" per task 030
                    reshape.

    Consumer code (read-only reference; this script does NOT modify):
        src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookExecutionEngine.cs:474-500
        (ResolveActionConfigViaFkChainAsync reads sprk_outputschemajson and feeds it to
         _openAiClient.StreamStructuredCompletionAsync as the Structured Outputs schema).
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = ($env:DATAVERSE_URL ?? "https://spaarkedev1.crm.dynamics.com")
)

$ErrorActionPreference = "Stop"

# -----------------------------------------------------------------------------
# Constants — the verification invariants required by R6 Pillar 5
# -----------------------------------------------------------------------------

$ExpectedEntity        = "sprk_analysisaction"
$ExpectedAttribute     = "sprk_outputschemajson"
$ExpectedAttributeType = "Memo"
$MinAcceptableMaxLength = 100000   # POML planned 100 KB; existing column is 1 MB (1048576)

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
# Web API Helper (GET-only — this script is read-only by design)
# -----------------------------------------------------------------------------

function Invoke-DataverseGet {
    param(
        [string]$Token,
        [string]$BaseUrl,
        [string]$Endpoint
    )

    $headers = @{
        "Authorization"    = "Bearer $Token"
        "OData-MaxVersion" = "4.0"
        "OData-Version"    = "4.0"
        "Accept"           = "application/json"
        "Prefer"           = "odata.include-annotations=*"
    }

    $uri = "$BaseUrl/api/data/v9.2/$Endpoint"

    try {
        return Invoke-RestMethod -Uri $uri -Method GET -Headers $headers
    }
    catch {
        $errorDetails = $_.Exception.Message
        if ($_.ErrorDetails.Message) {
            $errorJson = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($errorJson.error.message) {
                $errorDetails = $errorJson.error.message
            }
        }
        throw "API Error (GET $Endpoint): $errorDetails"
    }
}

# -----------------------------------------------------------------------------
# Verification step helpers — each returns $true on success, $false on failure
# (and writes a single-line PASS/FAIL message to the console).
# -----------------------------------------------------------------------------

function Assert-EntityExists {
    param([string]$Token, [string]$BaseUrl, [string]$EntityLogical)

    try {
        Invoke-DataverseGet -Token $Token -BaseUrl $BaseUrl `
            -Endpoint "EntityDefinitions(LogicalName='$EntityLogical')" | Out-Null
        Write-Host "  [PASS] Entity '$EntityLogical' exists." -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "  [FAIL] Entity '$EntityLogical' not found: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

function Get-MemoAttributeMetadata {
    param([string]$Token, [string]$BaseUrl, [string]$EntityLogical, [string]$AttributeLogical)

    # Cast to MemoAttributeMetadata to expose MaxLength (which lives on the derived type).
    return Invoke-DataverseGet -Token $Token -BaseUrl $BaseUrl `
        -Endpoint "EntityDefinitions(LogicalName='$EntityLogical')/Attributes(LogicalName='$AttributeLogical')/Microsoft.Dynamics.CRM.MemoAttributeMetadata"
}

function Assert-AttributeInvariants {
    param([object]$Metadata, [string]$AttributeLogical)

    $allPass = $true

    # AttributeType invariant
    if ($Metadata.AttributeType -eq $ExpectedAttributeType) {
        Write-Host "  [PASS] AttributeType == '$ExpectedAttributeType'." -ForegroundColor Green
    }
    else {
        Write-Host "  [FAIL] AttributeType expected '$ExpectedAttributeType'; got '$($Metadata.AttributeType)'." -ForegroundColor Red
        $allPass = $false
    }

    # MaxLength invariant (must be at least the planned cap)
    if ($null -ne $Metadata.MaxLength -and $Metadata.MaxLength -ge $MinAcceptableMaxLength) {
        Write-Host "  [PASS] MaxLength == $($Metadata.MaxLength) (>= planned minimum $MinAcceptableMaxLength)." -ForegroundColor Green
    }
    else {
        Write-Host "  [FAIL] MaxLength expected >= $MinAcceptableMaxLength; got '$($Metadata.MaxLength)'." -ForegroundColor Red
        $allPass = $false
    }

    # RequiredLevel invariant
    $requiredLevelValue = $Metadata.RequiredLevel.Value
    if ($requiredLevelValue -eq "None") {
        Write-Host "  [PASS] RequiredLevel == 'None'." -ForegroundColor Green
    }
    else {
        Write-Host "  [FAIL] RequiredLevel expected 'None'; got '$requiredLevelValue'." -ForegroundColor Red
        $allPass = $false
    }

    # IsCustomAttribute invariant
    if ($Metadata.IsCustomAttribute -eq $true) {
        Write-Host "  [PASS] IsCustomAttribute == true." -ForegroundColor Green
    }
    else {
        Write-Host "  [FAIL] IsCustomAttribute expected true; got '$($Metadata.IsCustomAttribute)'." -ForegroundColor Red
        $allPass = $false
    }

    return $allPass
}

function Show-SampleQuery {
    param([string]$Token, [string]$BaseUrl)

    Write-Host ""
    Write-Host "Sample query: top 5 sprk_analysisaction rows ----" -ForegroundColor Cyan

    $endpoint = "sprk_analysisactions?`$select=sprk_actioncode,sprk_outputschemajson&`$top=5"
    $result = Invoke-DataverseGet -Token $Token -BaseUrl $BaseUrl -Endpoint $endpoint

    foreach ($row in $result.value) {
        $populationState = if ($null -eq $row.sprk_outputschemajson) {
            "NULL"
        }
        else {
            "POPULATED ($($row.sprk_outputschemajson.Length) chars)"
        }
        Write-Host ("    {0,-32} {1}" -f $row.sprk_actioncode, $populationState) -ForegroundColor Gray
    }

    Write-Host "  [PASS] Query surface verified: column is queryable; population state shown above." -ForegroundColor Green
    return $true
}

# -----------------------------------------------------------------------------
# Main
# -----------------------------------------------------------------------------

function Main {
    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host " Verify sprk_outputschemajson on sprk_analysisaction (R6 D-B-01)" -ForegroundColor Cyan
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Environment: $EnvironmentUrl" -ForegroundColor Yellow
    Write-Host "Mode: READ-ONLY VERIFICATION (no Dataverse modifications)" -ForegroundColor Yellow
    Write-Host ""

    $token = Get-DataverseToken -EnvironmentUrl $EnvironmentUrl
    Write-Host "Authentication successful." -ForegroundColor Green
    Write-Host ""

    $overallPass = $true

    Write-Host "Step 1: Entity existence ----" -ForegroundColor Cyan
    if (-not (Assert-EntityExists -Token $token -BaseUrl $EnvironmentUrl -EntityLogical $ExpectedEntity)) {
        $overallPass = $false
    }

    Write-Host ""
    Write-Host "Step 2: Attribute metadata ----" -ForegroundColor Cyan
    $metadata = $null
    try {
        $metadata = Get-MemoAttributeMetadata -Token $token -BaseUrl $EnvironmentUrl `
            -EntityLogical $ExpectedEntity -AttributeLogical $ExpectedAttribute
        Write-Host "  Retrieved metadata for '$ExpectedAttribute'." -ForegroundColor Gray
        Write-Host "    LogicalName       : $($metadata.LogicalName)" -ForegroundColor Gray
        Write-Host "    SchemaName        : $($metadata.SchemaName)" -ForegroundColor Gray
        Write-Host "    AttributeType     : $($metadata.AttributeType)" -ForegroundColor Gray
        Write-Host "    MaxLength         : $($metadata.MaxLength)" -ForegroundColor Gray
        Write-Host "    RequiredLevel     : $($metadata.RequiredLevel.Value)" -ForegroundColor Gray
        Write-Host "    IsCustomAttribute : $($metadata.IsCustomAttribute)" -ForegroundColor Gray
        $displayLabel = ($metadata.DisplayName.LocalizedLabels | Where-Object { $_.LanguageCode -eq 1033 } | Select-Object -First 1).Label
        $descLabel    = ($metadata.Description.LocalizedLabels | Where-Object { $_.LanguageCode -eq 1033 } | Select-Object -First 1).Label
        Write-Host "    DisplayName (en)  : $displayLabel" -ForegroundColor Gray
        Write-Host "    Description (en)  : $descLabel" -ForegroundColor Gray
    }
    catch {
        Write-Host "  [FAIL] Could not retrieve metadata for '$ExpectedAttribute': $($_.Exception.Message)" -ForegroundColor Red
        $overallPass = $false
    }

    if ($null -ne $metadata) {
        Write-Host ""
        Write-Host "Step 3: Metadata invariants ----" -ForegroundColor Cyan
        if (-not (Assert-AttributeInvariants -Metadata $metadata -AttributeLogical $ExpectedAttribute)) {
            $overallPass = $false
        }
    }

    Write-Host ""
    Write-Host "Step 4: Query-surface verification ----" -ForegroundColor Cyan
    try {
        if (-not (Show-SampleQuery -Token $token -BaseUrl $EnvironmentUrl)) {
            $overallPass = $false
        }
    }
    catch {
        Write-Host "  [FAIL] Query failed: $($_.Exception.Message)" -ForegroundColor Red
        $overallPass = $false
    }

    Write-Host ""
    Write-Host "================================================================" -ForegroundColor Cyan
    if ($overallPass) {
        Write-Host " Verification PASSED — sprk_outputschemajson is correctly shaped." -ForegroundColor Green
    }
    else {
        Write-Host " Verification FAILED — see [FAIL] lines above." -ForegroundColor Red
    }
    Write-Host "================================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Downstream tasks 032/033/034/035 populate this column for the 4 R6 migration-" -ForegroundColor Yellow
    Write-Host "scope actions (summarize-document-for-chat (already populated), summarize-" -ForegroundColor Yellow
    Write-Host "document-for-workspace, matter-prefill, project-prefill). Task 040+041 wire" -ForegroundColor Yellow
    Write-Host "the StructuredOutputStreamWidget to READ this field for schema-aware rendering." -ForegroundColor Yellow
    Write-Host ""

    if (-not $overallPass) {
        exit 1
    }
}

Main
