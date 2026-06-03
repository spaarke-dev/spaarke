#requires -Version 7

<#
.SYNOPSIS
    Idempotent setup for Insights Engine Dataverse schema + lookup-target dispatch rows.

.DESCRIPTION
    Brings a Dataverse environment to the state required by Insights Engine r2 Wave B (the
    lookup-target dispatch architecture per `decisions/D-01-wave-b-root-cause-corrected.md`):

    1. Schema: ensures `sprk_executoractiontype` (Whole Number, nullable) exists on the
       `sprk_analysisactiontype` entity. Per amended ADR-027, this is direct Web API metadata
       work (no managed solution required).
    2. Backfill: sets `sprk_executoractiontype = 0` (AiAnalysis) on any existing
       `sprk_analysisactiontype` rows that have it null. Preserves existing dispatch behaviour.
    3. Seed: ensures the 7 Insights ActionType lookup rows exist with their dispatch values:
            "60 - Agent Service"           sprk_executoractiontype = 60
            "70 - Grounding Verify"        sprk_executoractiontype = 70
            "80 - Live Fact Resolver"      sprk_executoractiontype = 80
            "90 - Index Retrieve"          sprk_executoractiontype = 90
            "100 - Evidence Sufficiency"   sprk_executoractiontype = 100
            "110 - Decline to Find"        sprk_executoractiontype = 110
            "120 - Return Insight Artifact" sprk_executoractiontype = 120

    The 7 `INS-*` `sprk_analysisaction` rows (INS-FACT, INS-IDXR, INS-EVID, INS-GRND,
    INS-DECL, INS-RART, INS-AGNT) with JPS prompt content are NOT created by this script —
    see `projects/ai-spaarke-insights-engine-r2/notes/drafts/wave-b-action-codes.md` for the
    JPS prompt drafts and Guids. Those rows can be created via `mcp__dataverse__create_record`
    or a follow-up seeding script. This script focuses on the SCHEMA gap which is the
    load-bearing dependency for the BFF `AnalysisActionService.cs` lookup-based dispatch.

    Idempotent — safe to re-run. Each step checks current state before acting.

.PARAMETER DataverseUrl
    Target Dataverse environment URL. Defaults to $env:DATAVERSE_URL.

.PARAMETER DryRun
    Show what would change without making any modifications.

.EXAMPLE
    # Bring a Dataverse environment up to the Wave B baseline
    .\scripts\Setup-InsightsEngineSchema.ps1 -DataverseUrl "https://spaarkedev1.crm.dynamics.com"

.EXAMPLE
    # Preview without modifying
    .\scripts\Setup-InsightsEngineSchema.ps1 -DataverseUrl "https://spaarkedev1.crm.dynamics.com" -DryRun

.NOTES
    Authored 2026-06-02 as part of Insights Engine r2 Wave B post-merge protection.
    Per ADR-027 amendment 2026-06-02: schema work uses direct Web API + unmanaged-by-default
    (no managed solution required).
#>

[CmdletBinding()]
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if (-not $DataverseUrl) {
    throw "DataverseUrl is required. Set DATAVERSE_URL env var or pass -DataverseUrl parameter."
}

# ===========================================================================
# Auth
# ===========================================================================
Write-Host '=== Insights Engine Schema Setup ===' -ForegroundColor Cyan
Write-Host "Environment : $DataverseUrl" -ForegroundColor White
Write-Host "Mode        : $(if ($DryRun) { 'DRY-RUN' } else { 'LIVE' })" -ForegroundColor White
Write-Host ''

Write-Host '[1/4] Authenticating via Azure CLI...' -ForegroundColor Yellow
$token = az account get-access-token --resource $DataverseUrl --query 'accessToken' -o tsv 2>&1
if (-not $token -or $LASTEXITCODE -ne 0) {
    throw "Failed to acquire access token. Run 'az login' first."
}
Write-Host '  Token acquired.' -ForegroundColor Green

$apiBase = "$DataverseUrl/api/data/v9.2"
$headers = @{
    'Authorization' = "Bearer $token"
    'OData-MaxVersion' = '4.0'
    'OData-Version' = '4.0'
    'Accept' = 'application/json'
    'Content-Type' = 'application/json'
}

# ===========================================================================
# Step 1: Ensure sprk_executoractiontype field exists on sprk_analysisactiontype
# ===========================================================================
Write-Host ''
Write-Host '[2/4] Checking sprk_executoractiontype field on sprk_analysisactiontype...' -ForegroundColor Yellow

$attrUrl = "$apiBase/EntityDefinitions(LogicalName='sprk_analysisactiontype')/Attributes(LogicalName='sprk_executoractiontype')"
$fieldExists = $false
try {
    Invoke-RestMethod -Uri $attrUrl -Headers $headers -Method GET -ErrorAction Stop | Out-Null
    $fieldExists = $true
    Write-Host '  Field already exists.' -ForegroundColor Green
} catch {
    if ($_.Exception.Response.StatusCode -eq 'NotFound') {
        Write-Host '  Field MISSING — will create.' -ForegroundColor Yellow
    } else {
        throw "Unexpected error checking field: $($_.Exception.Message)"
    }
}

if (-not $fieldExists) {
    if ($DryRun) {
        Write-Host '  [DRY-RUN] Would create sprk_executoractiontype (Whole Number, nullable, range 0-200).' -ForegroundColor Gray
    } else {
        $createAttrUrl = "$apiBase/EntityDefinitions(LogicalName='sprk_analysisactiontype')/Attributes"
        $attrBody = @{
            '@odata.type' = 'Microsoft.Dynamics.CRM.IntegerAttributeMetadata'
            'AttributeType' = 'Integer'
            'AttributeTypeName' = @{ 'Value' = 'IntegerType' }
            'SchemaName' = 'sprk_executoractiontype'
            'LogicalName' = 'sprk_executoractiontype'
            'DisplayName' = @{ 'LocalizedLabels' = @(@{ 'Label' = 'Executor ActionType'; 'LanguageCode' = 1033 }) }
            'Description' = @{ 'LocalizedLabels' = @(@{ 'Label' = 'Dispatch ActionType integer used by PlaybookOrchestrationService to look up the matching INodeExecutor in NodeExecutorRegistry. Single source of truth for dispatch — no duplication on every sprk_analysisaction row. Per Insights Engine r2 D-01 (2026-06-02). Null defaults to AiAnalysis (0) for backward compat with rows authored before this field existed.'; 'LanguageCode' = 1033 }) }
            'RequiredLevel' = @{ 'Value' = 'None' }
            'MinValue' = 0
            'MaxValue' = 200
            'Format' = 'None'
        } | ConvertTo-Json -Depth 10 -Compress

        try {
            Invoke-RestMethod -Uri $createAttrUrl -Headers $headers -Method POST -Body $attrBody | Out-Null
            Write-Host '  Field created.' -ForegroundColor Green
        } catch {
            throw "Failed to create sprk_executoractiontype: $($_.Exception.Message)"
        }
    }
}

# ===========================================================================
# Step 2: Backfill existing sprk_analysisactiontype rows where field is null
# ===========================================================================
Write-Host ''
Write-Host '[3/4] Backfilling existing rows where sprk_executoractiontype is null...' -ForegroundColor Yellow

# Query rows where the field is null (catches both rows authored before the field existed
# AND any future row created without setting it).
$queryUrl = "$apiBase/sprk_analysisactiontypes?`$select=sprk_analysisactiontypeid,sprk_name,sprk_executoractiontype&`$filter=sprk_executoractiontype eq null"
$rowsNeedingBackfill = @()
try {
    $result = Invoke-RestMethod -Uri $queryUrl -Headers $headers -Method GET
    $rowsNeedingBackfill = $result.value
} catch {
    # If the field was just created (not yet committed in metadata cache), the filter may fail.
    # In that case, fetch all rows and filter client-side.
    if ($_.Exception.Message -match "sprk_executoractiontype") {
        Write-Host '  (Field too new to query directly; falling back to client-side filter.)' -ForegroundColor Gray
        $allUrl = "$apiBase/sprk_analysisactiontypes?`$select=sprk_analysisactiontypeid,sprk_name,sprk_executoractiontype"
        $result = Invoke-RestMethod -Uri $allUrl -Headers $headers -Method GET
        $rowsNeedingBackfill = $result.value | Where-Object { $null -eq $_.sprk_executoractiontype }
    } else {
        throw
    }
}

if ($rowsNeedingBackfill.Count -eq 0) {
    Write-Host '  No backfill needed — all rows already have a dispatch value.' -ForegroundColor Green
} else {
    Write-Host "  Found $($rowsNeedingBackfill.Count) row(s) needing backfill (default = 0 = AiAnalysis)." -ForegroundColor Yellow
    foreach ($row in $rowsNeedingBackfill) {
        $rowId = $row.sprk_analysisactiontypeid
        $rowName = $row.sprk_name
        if ($DryRun) {
            Write-Host "    [DRY-RUN] Would PATCH ${rowName} ($rowId) → sprk_executoractiontype = 0" -ForegroundColor Gray
        } else {
            $patchUrl = "$apiBase/sprk_analysisactiontypes($rowId)"
            $patchBody = @{ 'sprk_executoractiontype' = 0 } | ConvertTo-Json -Compress
            try {
                Invoke-RestMethod -Uri $patchUrl -Headers $headers -Method PATCH -Body $patchBody | Out-Null
                Write-Host "    Backfilled: $rowName → 0" -ForegroundColor Green
            } catch {
                Write-Warning "    Failed to backfill ${rowName}: $($_.Exception.Message)"
            }
        }
    }
}

# ===========================================================================
# Step 3: Seed Insights ActionType lookup rows
# ===========================================================================
Write-Host ''
Write-Host '[4/4] Seeding Insights ActionType lookup rows...' -ForegroundColor Yellow

$insightsRows = @(
    @{ Name = '60 - Agent Service';              ExecutorActionType = 60 },
    @{ Name = '70 - Grounding Verify';           ExecutorActionType = 70 },
    @{ Name = '80 - Live Fact Resolver';         ExecutorActionType = 80 },
    @{ Name = '90 - Index Retrieve';             ExecutorActionType = 90 },
    @{ Name = '100 - Evidence Sufficiency';      ExecutorActionType = 100 },
    @{ Name = '110 - Decline to Find';           ExecutorActionType = 110 },
    @{ Name = '120 - Return Insight Artifact';   ExecutorActionType = 120 }
)

foreach ($row in $insightsRows) {
    $rowName = $row.Name
    $rowValue = $row.ExecutorActionType

    # Check if row exists by exact name match
    $escapedName = [Uri]::EscapeDataString($rowName)
    $existsUrl = "$apiBase/sprk_analysisactiontypes?`$select=sprk_analysisactiontypeid,sprk_executoractiontype&`$filter=sprk_name eq '$escapedName'"
    $existingRow = $null
    try {
        $existingResult = Invoke-RestMethod -Uri $existsUrl -Headers $headers -Method GET
        if ($existingResult.value -and $existingResult.value.Count -gt 0) {
            $existingRow = $existingResult.value[0]
        }
    } catch {
        Write-Warning "    Failed to query for ${rowName}: $($_.Exception.Message)"
        continue
    }

    if ($existingRow) {
        if ($existingRow.sprk_executoractiontype -eq $rowValue) {
            Write-Host "  ${rowName}: present + correct value ($rowValue)." -ForegroundColor Green
        } else {
            if ($DryRun) {
                Write-Host "  [DRY-RUN] ${rowName}: would PATCH sprk_executoractiontype $($existingRow.sprk_executoractiontype) → $rowValue" -ForegroundColor Gray
            } else {
                $patchUrl = "$apiBase/sprk_analysisactiontypes($($existingRow.sprk_analysisactiontypeid))"
                $patchBody = @{ 'sprk_executoractiontype' = $rowValue } | ConvertTo-Json -Compress
                Invoke-RestMethod -Uri $patchUrl -Headers $headers -Method PATCH -Body $patchBody | Out-Null
                Write-Host "  ${rowName}: PATCHED sprk_executoractiontype → $rowValue" -ForegroundColor Yellow
            }
        }
    } else {
        if ($DryRun) {
            Write-Host "  [DRY-RUN] ${rowName}: would CREATE with sprk_executoractiontype = $rowValue" -ForegroundColor Gray
        } else {
            $createUrl = "$apiBase/sprk_analysisactiontypes"
            $createBody = @{
                'sprk_name' = $rowName
                'sprk_executoractiontype' = $rowValue
            } | ConvertTo-Json -Compress
            Invoke-RestMethod -Uri $createUrl -Headers $headers -Method POST -Body $createBody | Out-Null
            Write-Host "  ${rowName}: CREATED with sprk_executoractiontype = $rowValue" -ForegroundColor Yellow
        }
    }
}

# ===========================================================================
# Summary
# ===========================================================================
Write-Host ''
Write-Host '=== Setup Complete ===' -ForegroundColor Cyan
Write-Host "Schema      : sprk_executoractiontype on sprk_analysisactiontype $(if ($fieldExists) { '(was present)' } else { '(CREATED)' })" -ForegroundColor White
Write-Host "Backfill    : $($rowsNeedingBackfill.Count) row(s)" -ForegroundColor White
Write-Host "Lookup rows : 7 Insights ActionType rows ensured (60 + 70/80/90/100/110/120)" -ForegroundColor White
Write-Host ''
Write-Host 'NEXT STEPS:' -ForegroundColor Yellow
Write-Host '  1. The 7 INS-* sprk_analysisaction rows (with JPS prompts) are NOT created by this' -ForegroundColor Yellow
Write-Host '     script — see projects/ai-spaarke-insights-engine-r2/notes/drafts/wave-b-action-codes.md' -ForegroundColor Yellow
Write-Host '  2. The predict-matter-cost@v1 playbook is deployed via scripts/Deploy-Playbook.ps1' -ForegroundColor Yellow
Write-Host '     -Force after the action rows + their sprk_actiontypeid FKs are set.' -ForegroundColor Yellow
Write-Host ''
